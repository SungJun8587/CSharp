// UdpService.cs : implementation of the UdpServiceOptions and UdpService classes.
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Net;

/// <summary>UDP 서비스 통계 스냅샷.</summary>
public readonly record struct UdpStatsSnapshot(
    long DatagramsReceived,
    long DatagramsSent,
    long BytesReceived,
    long BytesSent,
    long SendFailures);

/// <summary>데이터그램 수신 핸들러. data 는 호출이 끝나면 무효가 된다.</summary>
public delegate void UdpReceiveHandler(ReadOnlySpan<byte> data, IPEndPoint remote);

/// <summary>UdpService 옵션.</summary>
public sealed class UdpServiceOptions
{
    /// <summary>동시에 걸어 둘 ReceiveFromAsync 개수.</summary>
    public int ReceiveCount { get; init; } = 4;

    /// <summary>
    /// 수신 버퍼 한 개의 크기이자 받을 수 있는 데이터그램 최대 크기. 이보다 큰 데이터그램은 잘리거나(Linux) 버려진다(Windows).
    /// </summary>
    public int MaxDatagramSize { get; init; } = 8192;

    /// <summary>소켓 수신 버퍼 크기. 0 이하이면 OS 기본값을 유지한다.</summary>
    public int SocketRecvBufferSize { get; init; } = 0;

    /// <summary>소켓 송신 버퍼 크기. 0 이하이면 OS 기본값을 유지한다.</summary>
    public int SocketSendBufferSize { get; init; } = 0;

    /// <summary>내부 이벤트를 받을 로거. null 이면 기록하지 않는다.</summary>
    public INetLogger? Logger { get; init; }
}

/// <summary>
/// UDP 송수신 서비스. 소켓 하나에 ReceiveFromAsync 를 여러 개 걸어 두고, 도착한 데이터그램을 OnRecv 로 전달한다.
/// 송신은 SendTo 로 풀링된 SocketAsyncEventArgs 와 ArrayPool 버퍼를 사용한다(호출한 버퍼는 복사되므로 즉시 재사용 가능).
/// 클라이언트로 쓸 때는 포트 0 으로 바인드하고 SendTo 로 서버 주소에 보내면 된다.
/// OnRecv 는 I/O 완료 스레드에서 직접 호출되므로 오래 블로킹하면 안 되며, data 는 콜백이 끝나면 무효가 된다.
/// </summary>
public abstract class UdpService : IDisposable, IAsyncDisposable
{
    // IPv4 UDP 데이터그램 페이로드 최대 크기
    private const int MaxUdpPayload = 65507;

    private sealed class RecvSlot
    {
        public SocketAsyncEventArgs Args = new SocketAsyncEventArgs();
        public byte[] Buffer = Array.Empty<byte>();
        public int ErrorStreak;
    }

    private readonly IPEndPoint _bindEndPoint;
    private readonly UdpServiceOptions _options;
    private readonly IPEndPoint _anyRemote;
    private readonly ConcurrentStack<SocketAsyncEventArgs> _sendPool = new ConcurrentStack<SocketAsyncEventArgs>();
    private readonly System.Collections.Generic.List<RecvSlot> _slots = new System.Collections.Generic.List<RecvSlot>();
    private readonly TaskCompletionSource<bool> _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private Socket? _socket;
    private int _started;
    private int _closing;
    private int _activeRecv;
    private int _activeSend;
    private int _released;

    private long _datagramsReceived;
    private long _bytesReceived;
    private long _datagramsSent;
    private long _bytesSent;
    private long _sendFailures;

    protected UdpService(IPEndPoint bindEndPoint, UdpServiceOptions? options = null)
    {
        _bindEndPoint = bindEndPoint ?? throw new ArgumentNullException(nameof(bindEndPoint));
        _options = options ?? new UdpServiceOptions();

        if (_options.ReceiveCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "ReceiveCount 는 1 이상이어야 합니다.");

        if (_options.MaxDatagramSize <= 0 || _options.MaxDatagramSize > MaxUdpPayload)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDatagramSize 는 1~65507 이어야 합니다.");

        _anyRemote = new IPEndPoint(
            bindEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
    }

    /// <summary>실제로 바인드된 주소(포트 0 으로 바인드했다면 할당된 포트). Start 전에는 null.</summary>
    public IPEndPoint? LocalEndPoint => _socket?.LocalEndPoint as IPEndPoint;

    public bool IsClosing => Volatile.Read(ref _closing) == 1;

    /// <summary>받은 데이터그램 수.</summary>
    public long DatagramsReceived => Interlocked.Read(ref _datagramsReceived);

    /// <summary>받은 바이트 수.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>보낸 데이터그램 수.</summary>
    public long DatagramsSent => Interlocked.Read(ref _datagramsSent);

    /// <summary>보낸 바이트 수.</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>송신에 실패한(소켓 오류 등) 데이터그램 수.</summary>
    public long SendFailures => Interlocked.Read(ref _sendFailures);

    /// <summary>현재 통계. 각 값은 개별적으로 읽으므로 서로 정확히 같은 시점의 값은 아니다.</summary>
    public UdpStatsSnapshot GetStats()
        => new UdpStatsSnapshot(DatagramsReceived, DatagramsSent, BytesReceived, BytesSent, SendFailures);

    /// <summary>데이터그램이 도착했을 때 호출된다. data 는 이 호출 동안만 유효하다.</summary>
    protected abstract void OnRecv(ReadOnlySpan<byte> data, IPEndPoint remote);

    /// <summary>OnRecv 에서 예외가 났을 때 호출된다. 기본 구현은 Trace 로 기록한다.</summary>
    protected virtual void OnException(Exception exception)
    {
        System.Diagnostics.Trace.TraceError("[UdpService] {0}", exception);
    }

    /// <summary>바인드하고 수신을 시작한다. 바인드에 실패하면 SocketException 을 던진다.</summary>
    public void Start()
    {
        if (IsClosing)
            throw new ObjectDisposedException(nameof(UdpService));

        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("이미 시작되었습니다.");

        Socket socket = SocketUtils.CreateUdpSocket(_bindEndPoint.AddressFamily);

        try
        {
            if (_options.SocketRecvBufferSize > 0)
                SocketUtils.SetRecvBufferSize(socket, _options.SocketRecvBufferSize);

            if (_options.SocketSendBufferSize > 0)
                SocketUtils.SetSendBufferSize(socket, _options.SocketSendBufferSize);

            DisableConnReset(socket);
            socket.Bind(_bindEndPoint);
        }
        catch (Exception)
        {
            SocketUtils.CloseSocket(socket);
            throw;
        }

        _socket = socket;
        Log(NetLogLevel.Info, $"UDP 시작: {LocalEndPoint}");

        // 슬롯을 모두 만든 뒤 수신을 건다(수신 완료가 빨라도 _activeRecv 가 0 으로 오인되지 않도록).
        for (int i = 0; i < _options.ReceiveCount; i++)
        {
            var slot = new RecvSlot { Buffer = ArrayPool<byte>.Shared.Rent(_options.MaxDatagramSize) };
            slot.Args.UserToken = slot;
            slot.Args.SetBuffer(slot.Buffer, 0, _options.MaxDatagramSize);
            slot.Args.Completed += OnIoCompleted;
            _slots.Add(slot);
        }

        Interlocked.Add(ref _activeRecv, _slots.Count);

        foreach (RecvSlot slot in _slots)
            RegisterRecv(slot);
    }

    /// <summary>
    /// 데이터그램 하나를 보낸다. data 는 복사되므로 반환 즉시 재사용해도 된다.
    /// </summary>
    /// <returns>서비스가 닫혔거나 시작되지 않았거나 즉시 실패하면 false. 전송 도중 실패는 SendFailures 로 집계된다.</returns>
    public bool SendTo(ReadOnlySpan<byte> data, IPEndPoint remote)
    {
        if (remote == null)
            throw new ArgumentNullException(nameof(remote));

        if (data.Length > MaxUdpPayload)
            throw new ArgumentOutOfRangeException(nameof(data), "UDP 데이터그램은 65507바이트를 넘을 수 없습니다.");

        Socket? socket = _socket;
        if (socket == null || IsClosing)
            return false;

        Interlocked.Increment(ref _activeSend);

        // 닫기와 경합해 _activeSend 증가가 늦었다면 되돌린다.
        if (IsClosing)
        {
            EndSend();
            return false;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, data.Length));
        data.CopyTo(buffer);

        if (!_sendPool.TryPop(out SocketAsyncEventArgs? args))
        {
            args = new SocketAsyncEventArgs();
            args.Completed += OnIoCompleted;
        }

        args.SetBuffer(buffer, 0, data.Length);
        args.RemoteEndPoint = remote;
        args.UserToken = buffer;

        bool pending;
        try
        {
            pending = socket.SendToAsync(args);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _sendFailures);
            FinishSend(args);
            return false;
        }

        if (!pending)
            CompleteSend(args);

        return true;
    }

    /// <summary>소켓을 닫고 진행 중인 I/O 가 모두 끝날 때까지 기다린다.</summary>
    /// <returns>제한 시간 안에 정리가 끝났으면 true.</returns>
    public bool Close(int timeoutMs = NetConstants.DefaultCloseTimeoutMs)
    {
        BeginClose();
        return _drained.Task.Wait(timeoutMs);
    }

    /// <summary>Close 의 비동기 버전.</summary>
    public async Task<bool> CloseAsync(int timeoutMs = NetConstants.DefaultCloseTimeoutMs, CancellationToken cancellationToken = default)
    {
        BeginClose();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timeout = Task.Delay(timeoutMs, cts.Token);
        Task finished = await Task.WhenAny(_drained.Task, timeout).ConfigureAwait(false);
        cts.Cancel();

        cancellationToken.ThrowIfCancellationRequested();
        return finished == _drained.Task;
    }

    public void Dispose() => Close();

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private void BeginClose()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
            return;

        if (Volatile.Read(ref _started) == 0)
        {
            _drained.TrySetResult(true);
            return;
        }

        SocketUtils.CloseSocket(_socket);
        Log(NetLogLevel.Info, "UDP 종료");
        TryRelease();
    }

    private void RegisterRecv(RecvSlot slot)
    {
        SocketAsyncEventArgs args = slot.Args;

        while (true)
        {
            if (IsClosing)
            {
                EndRecv();
                return;
            }

            args.RemoteEndPoint = _anyRemote;

            bool pending;
            try
            {
                pending = _socket!.ReceiveFromAsync(args);
            }
            catch (ObjectDisposedException)
            {
                EndRecv();
                return;
            }
            catch (Exception e)
            {
                Log(NetLogLevel.Error, "ReceiveFromAsync 예외", e);
                EndRecv();
                return;
            }

            if (pending)
                return;

            if (!ProcessRecv(slot))
                return;
        }
    }

    /// <returns>수신을 계속 이어가야 하면 true.</returns>
    private bool ProcessRecv(RecvSlot slot)
    {
        SocketAsyncEventArgs args = slot.Args;

        if (args.SocketError == SocketError.Success)
        {
            slot.ErrorStreak = 0;

            int count = args.BytesTransferred;
            Interlocked.Increment(ref _datagramsReceived);
            Interlocked.Add(ref _bytesReceived, count);

            try
            {
                OnRecv(new ReadOnlySpan<byte>(slot.Buffer, 0, count), (IPEndPoint)args.RemoteEndPoint!);
            }
            catch (Exception e)
            {
                try { OnException(e); }
                catch (Exception) { }
            }

            return true;
        }

        switch (args.SocketError)
        {
            case SocketError.OperationAborted:
            case SocketError.Interrupted:
            case SocketError.Shutdown:
            case SocketError.NotSocket:
            case SocketError.Disconnecting:
                EndRecv();
                return false;

            // 이전에 보낸 데이터그램의 ICMP 도달 불가 통지나 버퍼보다 큰 데이터그램은 소켓 자체의 문제가 아니다.
            case SocketError.ConnectionReset:
            case SocketError.NetworkReset:
            case SocketError.MessageSize:
                return !IsClosing || EndRecvFalse();

            default:
                if (IsClosing)
                {
                    EndRecv();
                    return false;
                }

                // 알 수 없는 오류가 연속되면 CPU 를 태우는 루프가 되지 않도록 이 슬롯을 중단한다.
                if (++slot.ErrorStreak >= 100)
                {
                    Log(NetLogLevel.Error, $"UDP 수신 오류가 연속되어 슬롯을 중단합니다: {SocketUtils.GetErrorText(args.SocketError)}");
                    EndRecv();
                    return false;
                }

                if (slot.ErrorStreak == 1)
                    Log(NetLogLevel.Warning, $"UDP 수신 오류: {SocketUtils.GetErrorText(args.SocketError)}");

                return true;
        }
    }

    private bool EndRecvFalse()
    {
        EndRecv();
        return false;
    }

    private void OnIoCompleted(object? sender, SocketAsyncEventArgs args)
    {
        if (args.LastOperation == SocketAsyncOperation.SendTo)
        {
            CompleteSend(args);
            return;
        }

        var slot = (RecvSlot)args.UserToken!;
        if (ProcessRecv(slot))
            RegisterRecv(slot);
    }

    private void CompleteSend(SocketAsyncEventArgs args)
    {
        if (args.SocketError == SocketError.Success)
        {
            Interlocked.Increment(ref _datagramsSent);
            Interlocked.Add(ref _bytesSent, args.BytesTransferred);
        }
        else
        {
            Interlocked.Increment(ref _sendFailures);
        }

        FinishSend(args);
    }

    private void FinishSend(SocketAsyncEventArgs args)
    {
        if (args.UserToken is byte[] buffer)
            ArrayPool<byte>.Shared.Return(buffer);

        args.SetBuffer(null, 0, 0);
        args.UserToken = null;
        args.RemoteEndPoint = null;

        if (IsClosing)
            args.Dispose();
        else
            _sendPool.Push(args);

        EndSend();
    }

    private void EndRecv()
    {
        Interlocked.Decrement(ref _activeRecv);
        TryRelease();
    }

    private void EndSend()
    {
        Interlocked.Decrement(ref _activeSend);
        TryRelease();
    }

    // 닫는 중이고 진행 중인 I/O 가 없을 때 한 번만 자원을 정리하고 완료를 알린다.
    private void TryRelease()
    {
        if (!IsClosing || Volatile.Read(ref _activeRecv) != 0 || Volatile.Read(ref _activeSend) != 0)
            return;

        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;

        foreach (RecvSlot slot in _slots)
        {
            slot.Args.Dispose();
            ArrayPool<byte>.Shared.Return(slot.Buffer);
        }

        _slots.Clear();

        while (_sendPool.TryPop(out SocketAsyncEventArgs? args))
            args.Dispose();

        _drained.TrySetResult(true);
    }

    // Windows 에서는 이전 송신에 대한 ICMP 도달 불가가 다음 수신을 ConnectionReset 으로 실패시킨다. 이를 끈다.
    private static void DisableConnReset(Socket socket)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        }
        catch (Exception) { }
    }

    private void Log(NetLogLevel level, string message, Exception? exception = null)
    {
        INetLogger? logger = _options.Logger;
        if (logger == null)
            return;

        try
        {
            if (logger.IsEnabled(level))
                logger.Log(level, message, exception);
        }
        catch (Exception) { }
    }
}

/// <summary>상속 없이 핸들러 델리게이트만으로 쓰는 UdpService.</summary>
public sealed class DelegateUdpService : UdpService
{
    private readonly UdpReceiveHandler _handler;

    public DelegateUdpService(IPEndPoint bindEndPoint, UdpReceiveHandler handler, UdpServiceOptions? options = null)
        : base(bindEndPoint, options)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    protected override void OnRecv(ReadOnlySpan<byte> data, IPEndPoint remote) => _handler(data, remote);
}
