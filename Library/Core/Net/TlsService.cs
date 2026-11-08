// TlsService.cs : implementation of the TlsServiceOptions, TlsSession, TlsPacketSession, TlsService, TlsServerService and TlsClientService classes.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Net;

/// <summary>TLS 서비스 옵션. 서버는 ServerCertificate, 클라이언트는 TargetHost 가 필요하다.</summary>
public sealed class TlsServiceOptions
{
    /// <summary>서버 인증서(개인 키 포함). 서버에서 필수.</summary>
    public X509Certificate? ServerCertificate { get; init; }

    /// <summary>서버가 클라이언트 인증서를 요구할지 여부.</summary>
    public bool ClientCertificateRequired { get; init; } = false;

    /// <summary>클라이언트가 서버 인증서의 호스트명 검증에 사용할 이름(서버 인증서의 CN/SAN 과 일치해야 한다). 클라이언트에서 필수.</summary>
    public string? TargetHost { get; init; }

    /// <summary>클라이언트가 제시할 인증서 목록.</summary>
    public X509CertificateCollection? ClientCertificates { get; init; }

    /// <summary>
    /// 상대 인증서 검증 콜백. null 이면 시스템 기본 검증을 따른다.
    /// 자체 서명 인증서를 쓰는 개발 환경에서는 여기서 지문(Thumbprint) 비교 등으로 직접 판단한다.
    /// </summary>
    public RemoteCertificateValidationCallback? RemoteCertificateValidation { get; init; }

    /// <summary>허용할 TLS 버전. None 이면 운영체제 기본값을 따른다.</summary>
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;

    /// <summary>핸드셰이크 제한 시간(ms). 넘으면 연결을 끊는다(핸드셰이크를 끌어 자원을 점유하는 공격 방어).</summary>
    public int HandshakeTimeoutMs { get; init; } = 5000;

    /// <summary>동시 세션 수 제한(핸드셰이크 중인 연결 포함).</summary>
    public int MaxSessionCount { get; init; } = 1000;

    /// <summary>세션당 송신 대기 최대 바이트. 넘으면 SendQueueOverflow 로 끊는다. 0 이하이면 무제한.</summary>
    public long MaxSendQueueBytes { get; init; } = 16 * 1024 * 1024;

    public bool NoDelay { get; init; } = true;

    /// <summary>
    /// true 이면 데이터가 올 때까지 0바이트 읽기로 기다리고 도착한 뒤에만 수신 버퍼를 대여한다.
    /// 플랫폼이 0바이트 읽기를 즉시 완료하더라도 동작은 정확하다(그 경우 이득만 없다).
    /// </summary>
    public bool ZeroByteRecv { get; init; } = true;

    /// <summary>수신 기준 유휴 제한 시간(ms) 및 불완전 패킷 정체(slowloris) 제한. 0 이하이면 사용하지 않는다.</summary>
    public int IdleTimeoutMs { get; init; } = 0;

    /// <summary>true 이면 송신 진행도 활동으로 인정한다(NetServiceOptions.IdleIncludesSend 와 같다).</summary>
    public bool IdleIncludesSend { get; init; } = false;

    /// <summary>DisconnectAfterSend 후 close_notify 를 보내고 상대가 닫기를 기다리는 최대 시간(ms).</summary>
    public int GracefulCloseTimeoutMs { get; init; } = 3000;

    /// <summary>listen backlog (서버 전용).</summary>
    public int Backlog { get; init; } = 512;

    public INetLogger? Logger { get; init; }
}

/// <summary>
/// TLS(SslStream) 위의 세션. Session 과 같은 콜백 모델(OnConnected/OnRecv/OnSend/OnDisconnected)과
/// Send/Disconnect/DisconnectAfterSend/Jobs 를 제공하지만, 핸드셰이크와 암호화는 SslStream 이 담당하므로
/// I/O 는 async/await 기반이며 콜백은 ThreadPool 스레드에서 실행된다.
/// OnRecv/OnSend 는 같은 방향에서 동시에 실행되지 않는다.
/// </summary>
public abstract class TlsSession
{
    private readonly RecvBuffer _recvBuffer;
    private TlsService? _service;
    private Socket? _socket;
    private SslStream? _ssl;

    private int _connected;
    private int _finalized;
    private int _pendingIo;
    private int _closeReason;

    private long _lastActivityTick;
    private long _lastProgressTick;
    private volatile bool _hasPartial;
    private bool _idleIncludesSend;
    private bool _zeroByteRecv;
    private long _bytesReceived;
    private long _bytesSent;

    private readonly object _sendLock = new object();
    private readonly Queue<SendBuffer> _sendQueue = new Queue<SendBuffer>();
    private readonly List<SendBuffer> _sendBatch = new List<SendBuffer>();
    private bool _sendRunning;
    private long _pendingSendBytes;
    private long _maxSendQueueBytes;

    private volatile bool _closeAfterSend;
    private CloseReason _afterSendReason = CloseReason.Local;
    private int _gracefulStarted;
    private ScheduledJob? _gracefulTimer;
    private volatile bool _gracefulClosing;
    private int _gracefulTimeoutMs;

    private JobQueue? _jobs;
    private bool _jobsOwned;

    protected TlsSession(int recvBufferSize = NetConstants.BufferSizeDefault)
    {
        _recvBuffer = new RecvBuffer(recvBufferSize);
    }

    public long SessionId { get; private set; }

    public IPEndPoint? RemoteEndPoint { get; private set; }

    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    /// <summary>협상된 TLS 버전. 핸드셰이크 전에는 None.</summary>
    public SslProtocols SslProtocol => _ssl?.SslProtocol ?? SslProtocols.None;

    /// <summary>핸드셰이크에서 받은 상대 인증서(없으면 null).</summary>
    public X509Certificate? RemoteCertificate => _ssl?.RemoteCertificate;

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public long PendingSendBytes => Interlocked.Read(ref _pendingSendBytes);

    /// <summary>세션 전용 직렬 잡 큐. Session.Jobs 와 같다.</summary>
    public JobQueue Jobs
    {
        get
        {
            JobQueue? queue = Volatile.Read(ref _jobs);
            if (queue != null)
                return queue;

            var created = new JobQueue();
            queue = Interlocked.CompareExchange(ref _jobs, created, null);
            if (queue != null)
                return queue;

            _jobsOwned = true;

            if (Volatile.Read(ref _finalized) != 0)
                created.Close();

            return created;
        }
    }

    /// <summary>외부(공유) 잡 큐를 지정한다. 세션이 끝나도 닫지 않는다.</summary>
    public void AttachJobQueue(JobQueue queue)
    {
        if (queue == null)
            throw new ArgumentNullException(nameof(queue));

        if (Interlocked.CompareExchange(ref _jobs, queue, null) != null)
            throw new InvalidOperationException("이미 잡 큐가 지정되었거나 Jobs 가 사용되었습니다.");
    }

    //****************************************************************
    // 콜백
    //****************************************************************

    /// <summary>핸드셰이크가 끝나 통신이 가능해졌을 때 호출된다.</summary>
    protected virtual void OnConnected() { }

    /// <summary>
    /// 복호화된 데이터가 도착했을 때 호출된다. 처리한 바이트 수를 돌려주며, 음수를 돌려주면(또는 예외) 연결을 끊는다.
    /// </summary>
    protected abstract int OnRecv(ReadOnlySpan<byte> buffer);

    /// <summary>송신이 끝날 때마다 호출된다.</summary>
    protected virtual void OnSend(int numOfBytes) { }

    /// <summary>연결이 끝났을 때 정확히 한 번 호출된다(핸드셰이크에 실패한 연결에는 호출되지 않는다).</summary>
    protected virtual void OnDisconnected(CloseReason reason) { }

    /// <summary>콜백에서 예외가 났을 때 호출된다. 기본 구현은 서비스 로거(있으면)에 Error 로 남긴다.</summary>
    protected virtual void OnException(Exception exception)
    {
        if (_service != null && _service.IsLogEnabled(NetLogLevel.Error))
            _service.Log(NetLogLevel.Error, $"TLS 세션 {SessionId} 콜백 예외", exception);
        else
            System.Diagnostics.Trace.TraceError("[TlsSession {0}] {1}", SessionId, exception);
    }

    //****************************************************************
    // 송신 / 종료
    //****************************************************************

    /// <summary>데이터를 복사해 송신 큐에 넣는다.</summary>
    public bool Send(ReadOnlySpan<byte> data) => Send(SendBufferManager.Create(data));

    /// <summary>송신 큐에 넣는다. 같은 SendBuffer 를 여러 세션에 보내도 된다(Broadcast).</summary>
    /// <returns>연결되어 있지 않거나 종료가 예약되었거나 송신 제한을 넘어 거부되면 false.</returns>
    public bool Send(SendBuffer buffer)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        if (!IsConnected || _closeAfterSend)
            return false;

        int size = buffer.WriteSize;
        if (size == 0)
            return true;

        long queued = Interlocked.Add(ref _pendingSendBytes, size);
        if (_maxSendQueueBytes > 0 && queued > _maxSendQueueBytes)
        {
            Interlocked.Add(ref _pendingSendBytes, -size);
            Disconnect(CloseReason.SendQueueOverflow);
            return false;
        }

        bool start = false;
        lock (_sendLock)
        {
            // 락 안에서 종료 예약을 다시 확인한다: DisconnectAfterSend 가 close_notify 를 시작한 뒤에는
            // 송신 루프가 새로 시작되어 SslStream 에 동시에 쓰는 일이 없어야 한다.
            if (!IsConnected || _closeAfterSend)
            {
                Interlocked.Add(ref _pendingSendBytes, -size);
                return false;
            }

            _sendQueue.Enqueue(buffer);

            if (!_sendRunning)
            {
                _sendRunning = true;
                start = true;
                Interlocked.Increment(ref _pendingIo);
            }
        }

        if (start)
            _ = SendLoopAsync();

        return true;
    }

    /// <summary>즉시 연결을 끊는다(대기 중인 송신은 버려진다).</summary>
    public void Disconnect(CloseReason reason = CloseReason.Local)
    {
        Interlocked.CompareExchange(ref _closeReason, (int)reason, 0);
        Interlocked.Exchange(ref _connected, 0);

        // 소켓을 닫으면 진행 중인 읽기/쓰기/핸드셰이크가 예외로 끝나고, 그 뒤 정리 단계로 이어진다.
        SocketUtils.CloseSocket(_socket);
    }

    /// <summary>
    /// 큐에 있는 데이터를 모두 보낸 뒤 close_notify 를 보내 정상 종료한다. 상대가 닫기를 기다리다 제한 시간이 지나면 끊는다.
    /// 이후 Send 는 거부된다. 종료 사유는 reason 으로 기록된다.
    /// </summary>
    public void DisconnectAfterSend(CloseReason reason = CloseReason.Local)
    {
        if (!IsConnected)
            return;

        _afterSendReason = reason;
        _closeAfterSend = true;

        bool idle;
        lock (_sendLock)
            idle = !_sendRunning && _sendQueue.Count == 0;

        if (idle)
            BeginGraceful();
    }

    //****************************************************************
    // 서비스 내부용
    //****************************************************************

    internal void Attach(TlsService service, Socket socket, long sessionId)
    {
        _service = service;
        _socket = socket;
        SessionId = sessionId;
        _maxSendQueueBytes = service.Options.MaxSendQueueBytes;
        _zeroByteRecv = service.Options.ZeroByteRecv;
        _idleIncludesSend = service.Options.IdleIncludesSend;
        _gracefulTimeoutMs = service.Options.GracefulCloseTimeoutMs;
    }

    internal void CheckIdle(long nowTick, int timeoutMs)
    {
        if (!IsConnected)
            return;

        bool idle = nowTick - Volatile.Read(ref _lastActivityTick) > timeoutMs;
        bool stalled = _hasPartial && nowTick - Volatile.Read(ref _lastProgressTick) > timeoutMs;

        if (idle || stalled)
            Disconnect(CloseReason.IdleTimeout);
    }

    /// <summary>
    /// 핸드셰이크를 수행하고 성공하면 연결 상태로 전환해 수신 루프를 시작한다. 실패하면 정리 후 예외를 다시 던진다.
    /// </summary>
    internal async Task StartAsync(Func<SslStream, CancellationToken, Task> authenticate, int handshakeTimeoutMs)
    {
        try
        {
            RemoteEndPoint = _socket!.RemoteEndPoint as IPEndPoint;
        }
        catch (Exception) { }

        var stream = new NetworkStream(_socket!, ownsSocket: false);
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        _ssl = ssl;

        try
        {
            using var cts = handshakeTimeoutMs > 0
                ? new CancellationTokenSource(handshakeTimeoutMs)
                : new CancellationTokenSource();

            await authenticate(ssl, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            FailHandshake();

            if (e is OperationCanceledException)
                throw new TimeoutException($"TLS 핸드셰이크가 {handshakeTimeoutMs}ms 안에 끝나지 않았습니다.", e);

            throw;
        }

        // 핸드셰이크 도중 서비스 종료 등으로 Disconnect 되었다면 연결 상태로 만들지 않는다.
        if (Volatile.Read(ref _closeReason) != 0)
        {
            FailHandshake();
            throw new OperationCanceledException("핸드셰이크 완료 전에 연결이 종료되었습니다.");
        }

        long now = Environment.TickCount64;
        Volatile.Write(ref _lastActivityTick, now);
        Volatile.Write(ref _lastProgressTick, now);

        Interlocked.Increment(ref _pendingIo);   // 수신 루프 몫
        Interlocked.Exchange(ref _connected, 1);
        _service!.OnSessionStarted();

        try { OnConnected(); }
        catch (Exception e) { SafeOnException(e); }

        _ = RecvLoopAsync();
    }

    private void FailHandshake()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return;

        SocketUtils.CloseSocket(_socket);
        try { _ssl?.Dispose(); } catch (Exception) { }
        _recvBuffer.Release();
        _service?.RemoveSession(this);
    }

    private async Task RecvLoopAsync()
    {
        try
        {
            while (IsConnected)
            {
                if (_zeroByteRecv)
                {
                    // 데이터(또는 EOF)가 올 때까지 버퍼 없이 기다린다.
                    await _ssl!.ReadAsync(Memory<byte>.Empty).ConfigureAwait(false);
                    if (!IsConnected)
                        break;
                }

                _recvBuffer.EnsureRented();

                if (_recvBuffer.FreeSize == 0)
                {
                    Disconnect(CloseReason.ProtocolError);
                    break;
                }

                int numOfBytes = await _ssl!.ReadAsync(
                    new Memory<byte>(_recvBuffer.Array, _recvBuffer.WritePos, _recvBuffer.FreeSize)).ConfigureAwait(false);

                if (numOfBytes == 0)
                {
                    Disconnect(_gracefulClosing ? _afterSendReason : CloseReason.RemoteClosed);
                    break;
                }

                long now = Environment.TickCount64;
                Volatile.Write(ref _lastActivityTick, now);
                Interlocked.Add(ref _bytesReceived, numOfBytes);

                bool hadData = _recvBuffer.DataSize > 0;

                if (!_recvBuffer.OnWrite(numOfBytes))
                {
                    Disconnect(CloseReason.ProtocolError);
                    break;
                }

                int processed;
                if (_gracefulClosing)
                {
                    processed = _recvBuffer.DataSize;   // 정상 종료 대기 중에는 새 데이터를 버린다
                }
                else
                {
                    try { processed = OnRecv(_recvBuffer.ReadSpan); }
                    catch (Exception e)
                    {
                        SafeOnException(e);
                        processed = -1;
                    }
                }

                if (processed < 0 || !_recvBuffer.OnRead(processed))
                {
                    Disconnect(CloseReason.ProtocolError);
                    break;
                }

                _recvBuffer.Clean();

                if (_recvBuffer.DataSize == 0)
                {
                    _hasPartial = false;
                }
                else
                {
                    if (processed > 0 || !hadData)
                        Volatile.Write(ref _lastProgressTick, now);

                    _hasPartial = true;
                }

                if (_zeroByteRecv)
                    _recvBuffer.ReleaseIfEmpty();
            }
        }
        catch (Exception e)
        {
            Disconnect(ClassifyRecvError(e));
        }
        finally
        {
            DecrementPending();
        }
    }

    private CloseReason ClassifyRecvError(Exception e)
    {
        if (!IsConnected)
            return CloseReason.Local;   // 이미 Disconnect 가 사유를 기록했으면 CAS 에서 무시된다

        return IsRemoteClose(e) ? CloseReason.RemoteClosed : CloseReason.RecvError;
    }

    private static bool IsRemoteClose(Exception e)
    {
        for (Exception? current = e; current != null; current = current.InnerException)
        {
            if (current is SocketException se && SocketUtils.IsNormalDisconnect(se.SocketErrorCode))
                return true;

            if (current is ObjectDisposedException || current is EndOfStreamException)
                return true;
        }

        return false;
    }

    private async Task SendLoopAsync()
    {
        try
        {
            while (true)
            {
                int total = 0;
                bool done = false;

                lock (_sendLock)
                {
                    if (_sendQueue.Count == 0)
                    {
                        _sendRunning = false;
                        done = true;
                    }
                    else
                    {
                        while (_sendBatch.Count < NetConstants.MaxSendBatch && _sendQueue.Count > 0 && total < 256 * 1024)
                        {
                            SendBuffer buffer = _sendQueue.Dequeue();
                            _sendBatch.Add(buffer);
                            total += buffer.WriteSize;
                        }
                    }
                }

                if (done)
                    break;

                foreach (SendBuffer buffer in _sendBatch)
                {
                    ArraySegment<byte> segment = buffer.Segment;
                    await _ssl!.WriteAsync(new ReadOnlyMemory<byte>(segment.Array, segment.Offset, segment.Count)).ConfigureAwait(false);
                }

                _sendBatch.Clear();
                Interlocked.Add(ref _pendingSendBytes, -total);
                Interlocked.Add(ref _bytesSent, total);

                if (_idleIncludesSend)
                    Volatile.Write(ref _lastActivityTick, Environment.TickCount64);

                try { OnSend(total); }
                catch (Exception e) { SafeOnException(e); }
            }

            if (_closeAfterSend)
                BeginGraceful();
        }
        catch (Exception e)
        {
            lock (_sendLock)
                _sendRunning = false;

            Disconnect(ClassifyRecvError(e) == CloseReason.RemoteClosed ? CloseReason.RemoteClosed : CloseReason.SendError);
        }
        finally
        {
            DecrementPending();
        }
    }

    private void BeginGraceful()
    {
        if (!IsConnected || Interlocked.Exchange(ref _gracefulStarted, 1) != 0)
            return;

        _gracefulClosing = true;
        Interlocked.Increment(ref _pendingIo);
        _ = GracefulAsync();
    }

    private async Task GracefulAsync()
    {
        try
        {
            int timeout = _gracefulTimeoutMs;

            if (timeout > 0)
            {
                try
                {
                    // close_notify 전송 후 TCP 송신 측을 닫고, 상대가 닫기(수신 루프의 EOF)를 기다린다.
                    await _ssl!.ShutdownAsync().WaitAsync(TimeSpan.FromMilliseconds(timeout)).ConfigureAwait(false);
                    _socket!.Shutdown(SocketShutdown.Send);
                }
                catch (Exception) { }

                CloseReason reason = _afterSendReason;
                _gracefulTimer = JobTimer.Shared.Reserve(timeout, () => Disconnect(reason));
            }
            else
            {
                Disconnect(_afterSendReason);
            }
        }
        finally
        {
            DecrementPending();
        }
    }

    private void DecrementPending()
    {
        if (Interlocked.Decrement(ref _pendingIo) == 0)
            TryFinalize();
    }

    private void TryFinalize()
    {
        // 모든 I/O 가 끝났는데 아직 연결 상태라면(이론상) 종료 사유를 정해 둔다.
        Interlocked.CompareExchange(ref _closeReason, (int)CloseReason.Local, 0);
        Interlocked.Exchange(ref _connected, 0);

        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return;

        SocketUtils.CloseSocket(_socket);
        _gracefulTimer?.Cancel();

        lock (_sendLock)
        {
            _sendQueue.Clear();
            _sendBatch.Clear();
        }

        CloseReason reason = (CloseReason)Volatile.Read(ref _closeReason);
        if (_service != null && _service.IsLogEnabled(NetLogLevel.Trace))
            _service.Log(NetLogLevel.Trace, $"TLS 세션 {SessionId} 종료: {reason}");

        try { OnDisconnected(reason); }
        catch (Exception e) { SafeOnException(e); }

        if (_jobsOwned)
            Volatile.Read(ref _jobs)?.Close();

        try { _ssl?.Dispose(); } catch (Exception) { }
        _recvBuffer.Release();

        _service?.OnSessionClosed(Interlocked.Read(ref _bytesReceived), Interlocked.Read(ref _bytesSent));
        _service?.RemoveSession(this);
    }

    private void SafeOnException(Exception exception)
    {
        try { OnException(exception); }
        catch (Exception) { }
    }
}

/// <summary>TLS 스트림에서 헤더 기반으로 패킷 경계를 잘라 OnRecvPacket 으로 넘겨주는 세션(PacketSession 과 같은 규약).</summary>
public abstract class TlsPacketSession : TlsSession
{
    protected TlsPacketSession(int recvBufferSize = NetConstants.BufferSizeDefault)
        : base(recvBufferSize)
    {
        int capacity = RecvBuffer.ComputeCapacity(recvBufferSize);
        if (capacity < PacketHeader.MaxPacketSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recvBufferSize),
                $"수신 버퍼 용량({capacity})이 최대 패킷 크기 {PacketHeader.MaxPacketSize} 보다 작습니다.");
        }
    }

    /// <param name="packet">헤더를 포함한 전체 패킷. 반환 이후에는 무효.</param>
    protected abstract void OnRecvPacket(ReadOnlySpan<byte> packet);

    protected sealed override int OnRecv(ReadOnlySpan<byte> buffer)
    {
        int processed = 0;

        while (true)
        {
            int dataSize = buffer.Length - processed;
            if (dataSize < PacketHeader.HeaderSize)
                break;

            int packetSize = PacketHeader.ReadSize(buffer.Slice(processed));
            if (packetSize < PacketHeader.HeaderSize)
                return -1;

            if (dataSize < packetSize)
                break;

            OnRecvPacket(buffer.Slice(processed, packetSize));
            processed += packetSize;
        }

        return processed;
    }
}

/// <summary>TLS 서비스 공통 기반: 세션 관리, 슬롯 예약, 통계, 유휴 감시, 종료.</summary>
public abstract class TlsService : IDisposable, IAsyncDisposable
{
    private readonly Func<TlsSession> _sessionFactory;
    private readonly ConcurrentDictionary<long, TlsSession> _sessions = new ConcurrentDictionary<long, TlsSession>();
    private readonly TaskCompletionSource<bool> _allClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _idleLock = new object();
    private Timer? _idleTimer;
    private long _lastSessionId;
    private int _closing;
    private int _sessionSlots;

    private long _totalConnected;
    private long _totalDisconnected;
    private long _totalRejected;
    private long _closedBytesReceived;
    private long _closedBytesSent;

    protected TlsService(IPEndPoint endPoint, Func<TlsSession> sessionFactory, TlsServiceOptions options)
    {
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IPEndPoint EndPoint { get; }

    public TlsServiceOptions Options { get; }

    /// <summary>현재 관리 중인 세션 수(핸드셰이크 중인 연결 포함).</summary>
    public int SessionCount => _sessions.Count;

    public bool IsClosing => Volatile.Read(ref _closing) == 1;

    /// <summary>sessionId 로 세션을 찾는다.</summary>
    public bool TryGetSession(long sessionId, out TlsSession? session) => _sessions.TryGetValue(sessionId, out session);

    /// <summary>연결된 모든 세션에 같은 SendBuffer 를 보낸다.</summary>
    public void Broadcast(SendBuffer buffer)
    {
        foreach (var pair in _sessions)
            pair.Value.Send(buffer);
    }

    public NetStatsSnapshot GetStats()
    {
        int connected = 0;
        long received = 0, sent = 0, pending = 0;

        foreach (var pair in _sessions)
        {
            TlsSession session = pair.Value;
            if (!session.IsConnected)
                continue;

            connected++;
            received += session.BytesReceived;
            sent += session.BytesSent;
            pending += session.PendingSendBytes;
        }

        return new NetStatsSnapshot(
            connected,
            Interlocked.Read(ref _totalConnected),
            Interlocked.Read(ref _totalDisconnected),
            Interlocked.Read(ref _totalRejected),
            received + Interlocked.Read(ref _closedBytesReceived),
            sent + Interlocked.Read(ref _closedBytesSent),
            pending);
    }

    /// <summary>서비스를 닫고 모든 세션이 정리될 때까지 기다린다.</summary>
    /// <returns>제한 시간 안에 모두 정리되었으면 true.</returns>
    public bool Close(int timeoutMs = NetConstants.DefaultCloseTimeoutMs)
    {
        BeginClose();
        return _allClosed.Task.Wait(timeoutMs);
    }

    public async Task<bool> CloseAsync(int timeoutMs = NetConstants.DefaultCloseTimeoutMs, CancellationToken cancellationToken = default)
    {
        BeginClose();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timeout = Task.Delay(timeoutMs, cts.Token);
        Task finished = await Task.WhenAny(_allClosed.Task, timeout).ConfigureAwait(false);
        cts.Cancel();

        cancellationToken.ThrowIfCancellationRequested();
        return finished == _allClosed.Task;
    }

    public void Dispose() => Close();

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    /// <summary>파생 클래스가 신규 접속/연결 수립을 멈추는 단계.</summary>
    protected abstract void StopAcceptingConnections();

    private void BeginClose()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
            return;

        StopAcceptingConnections();
        StopIdleMonitor();

        foreach (var pair in _sessions)
            pair.Value.Disconnect(CloseReason.ServiceClosing);

        if (_sessions.IsEmpty)
            _allClosed.TrySetResult(true);
    }

    //****************************************************************
    // 파생 클래스/세션용
    //****************************************************************

    internal bool TryReserveSlot()
    {
        int max = Options.MaxSessionCount;

        while (true)
        {
            int current = Volatile.Read(ref _sessionSlots);
            if (current >= max)
                return false;

            if (Interlocked.CompareExchange(ref _sessionSlots, current + 1, current) == current)
                return true;
        }
    }

    internal void ReleaseSlot() => Interlocked.Decrement(ref _sessionSlots);

    internal long CountRejected() => Interlocked.Increment(ref _totalRejected);

    internal void OnSessionStarted() => Interlocked.Increment(ref _totalConnected);

    internal void OnSessionClosed(long bytesReceived, long bytesSent)
    {
        Interlocked.Add(ref _closedBytesReceived, bytesReceived);
        Interlocked.Add(ref _closedBytesSent, bytesSent);
        Interlocked.Increment(ref _totalDisconnected);
    }

    /// <summary>슬롯을 이미 예약한 연결로 세션을 만들어 목록에 넣는다. 실패하면 슬롯을 반환하고 null.</summary>
    internal TlsSession? CreateAndAddSession(Socket socket)
    {
        TlsSession session;
        try
        {
            ConfigureSocket(socket);
            session = _sessionFactory();
            session.Attach(this, socket, Interlocked.Increment(ref _lastSessionId));
        }
        catch (Exception e)
        {
            ReleaseSlot();
            Log(NetLogLevel.Error, "TLS 세션 생성 실패", e);
            return null;
        }

        if (!_sessions.TryAdd(session.SessionId, session))
        {
            ReleaseSlot();
            return null;
        }

        // Add 직전에 닫기가 시작되었다면 BeginClose 의 열거에서 빠졌을 수 있으므로 직접 끊는다.
        if (IsClosing)
            session.Disconnect(CloseReason.ServiceClosing);

        return session;
    }

    internal void RemoveSession(TlsSession session)
    {
        if (!_sessions.TryRemove(session.SessionId, out _))
            return;

        ReleaseSlot();

        if (IsClosing && _sessions.IsEmpty)
            _allClosed.TrySetResult(true);
    }

    internal void ConfigureSocket(Socket socket) => SocketUtils.SetNoDelay(socket, Options.NoDelay);

    protected void StartIdleMonitor()
    {
        int timeout = Options.IdleTimeoutMs;
        if (timeout <= 0)
            return;

        lock (_idleLock)
        {
            if (_idleTimer != null)
                return;

            int period = Math.Max(100, timeout / 4);
            _idleTimer = new Timer(_ => SweepIdle(), null, period, period);
        }
    }

    private void StopIdleMonitor()
    {
        lock (_idleLock)
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
        }
    }

    private void SweepIdle()
    {
        try
        {
            long now = Environment.TickCount64;
            foreach (var pair in _sessions)
                pair.Value.CheckIdle(now, Options.IdleTimeoutMs);
        }
        catch (Exception e)
        {
            Log(NetLogLevel.Error, "TLS 유휴 검사 예외", e);
        }
    }

    internal bool IsLogEnabled(NetLogLevel level)
    {
        INetLogger? logger = Options.Logger;
        if (logger == null)
            return false;

        try { return logger.IsEnabled(level); }
        catch (Exception) { return false; }
    }

    internal void Log(NetLogLevel level, string message, Exception? exception = null)
    {
        INetLogger? logger = Options.Logger;
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

/// <summary>TLS 서버. 접속을 받아 핸드셰이크 후 세션을 시작한다. ServerCertificate 옵션이 필요하다.</summary>
public sealed class TlsServerService : TlsService
{
    private Socket? _listenSocket;
    private Task _acceptTask = Task.CompletedTask;
    private int _started;

    public TlsServerService(IPEndPoint endPoint, Func<TlsSession> sessionFactory, TlsServiceOptions options)
        : base(endPoint, sessionFactory, options)
    {
        if (options.ServerCertificate == null)
            throw new ArgumentException("서버는 ServerCertificate 옵션이 필요합니다.", nameof(options));
    }

    /// <summary>실제 리슨 주소(포트 0 으로 바인드했다면 할당된 포트).</summary>
    public IPEndPoint? LocalEndPoint => _listenSocket?.LocalEndPoint as IPEndPoint;

    public void Start()
    {
        if (IsClosing)
            throw new ObjectDisposedException(nameof(TlsServerService));

        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("이미 시작되었습니다.");

        Socket? socket = SocketUtils.CreateListenSocket(EndPoint, Options.Backlog);
        if (socket == null)
            throw new SocketException((int)SocketError.AddressAlreadyInUse);

        _listenSocket = socket;
        Log(NetLogLevel.Info, $"TLS 리슨 시작: {LocalEndPoint}");

        StartIdleMonitor();
        _acceptTask = AcceptLoopAsync(socket);
    }

    protected override void StopAcceptingConnections()
    {
        SocketUtils.CloseSocket(_listenSocket);
        Log(NetLogLevel.Info, "TLS 리슨 종료");
    }

    private async Task AcceptLoopAsync(Socket listenSocket)
    {
        while (!IsClosing)
        {
            Socket accepted;
            try
            {
                accepted = await listenSocket.AcceptAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException e)
            {
                if (IsClosing || SocketUtils.IsListenClosed(e.SocketErrorCode))
                    break;

                Log(NetLogLevel.Warning, $"TLS Accept 실패: {SocketUtils.GetErrorText(e.SocketErrorCode)}");
                await Task.Delay(NetConstants.AcceptRetryDelayMs).ConfigureAwait(false);
                continue;
            }

            if (IsClosing || !TryReserveSlot())
            {
                if (!IsClosing)
                {
                    long rejected = CountRejected();
                    if (rejected == 1 || rejected % 1000 == 0)
                        Log(NetLogLevel.Warning, $"TLS 접속 거부: MaxSessionCount={Options.MaxSessionCount} 도달 (누적 {rejected}건)");
                }

                SocketUtils.CloseSocket(accepted);
                continue;
            }

            // 핸드셰이크는 연결마다 독립적으로 진행해 느린 상대가 다른 접속 수락을 막지 못하게 한다.
            _ = HandshakeAsync(accepted);
        }
    }

    private async Task HandshakeAsync(Socket socket)
    {
        string remote = socket.SafeRemote();
        TlsSession? session = CreateAndAddSession(socket);
        if (session == null)
        {
            SocketUtils.CloseSocket(socket);
            return;
        }

        var authOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = Options.ServerCertificate,
            ClientCertificateRequired = Options.ClientCertificateRequired,
            EnabledSslProtocols = Options.EnabledSslProtocols,
            RemoteCertificateValidationCallback = Options.RemoteCertificateValidation,
        };

        try
        {
            await session.StartAsync((ssl, token) => ssl.AuthenticateAsServerAsync(authOptions, token), Options.HandshakeTimeoutMs)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // 서비스 종료로 인한 취소는 정상 흐름이다. 그 외(인증서 거부, 평문 접속, 시간 초과 등)는 경고로 남긴다.
            if (!IsClosing)
                Log(NetLogLevel.Warning, $"TLS 핸드셰이크 실패 ({remote}): {e.GetType().Name} {e.Message}");
        }
    }
}

/// <summary>TLS 클라이언트. ConnectAsync 로 서버에 접속해 핸드셰이크가 끝난 세션을 돌려준다. TargetHost 옵션이 필요하다.</summary>
public sealed class TlsClientService : TlsService
{
    private readonly CancellationTokenSource _closeCts = new CancellationTokenSource();

    public TlsClientService(IPEndPoint endPoint, Func<TlsSession> sessionFactory, TlsServiceOptions options)
        : base(endPoint, sessionFactory, options)
    {
        if (string.IsNullOrEmpty(options.TargetHost))
            throw new ArgumentException("클라이언트는 TargetHost 옵션이 필요합니다.", nameof(options));
    }

    protected override void StopAcceptingConnections()
    {
        try { _closeCts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// 서버에 접속하고 TLS 핸드셰이크까지 끝낸 뒤 세션을 돌려준다.
    /// 실패하면 SocketException/AuthenticationException/TimeoutException 등을 던지고 자원은 정리된다.
    /// </summary>
    public async Task<TlsSession> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsClosing)
            throw new InvalidOperationException("서비스가 닫히는 중입니다.");

        if (!TryReserveSlot())
            throw new InvalidOperationException($"MaxSessionCount({Options.MaxSessionCount}) 에 도달했습니다.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closeCts.Token);

        Socket socket = SocketUtils.CreateTcpSocket(EndPoint.AddressFamily);
        TlsSession? session = null;
        bool slotHandedOver = false;   // CreateAndAddSession 을 호출한 뒤에는 슬롯 반환을 서비스/세션이 책임진다

        try
        {
            await socket.ConnectAsync(EndPoint, linked.Token).ConfigureAwait(false);

            slotHandedOver = true;
            session = CreateAndAddSession(socket);
            if (session == null)
                throw new InvalidOperationException("세션을 만들 수 없습니다.");

            var authOptions = new SslClientAuthenticationOptions
            {
                TargetHost = Options.TargetHost,
                EnabledSslProtocols = Options.EnabledSslProtocols,
                ClientCertificates = Options.ClientCertificates,
                RemoteCertificateValidationCallback = Options.RemoteCertificateValidation,
            };

            await session.StartAsync((ssl, token) => ssl.AuthenticateAsClientAsync(authOptions, token), Options.HandshakeTimeoutMs)
                .ConfigureAwait(false);

            return session;
        }
        catch (Exception)
        {
            // 세션이 만들어지기 전에 실패했다면 슬롯과 소켓을 직접 정리한다(세션이 있으면 StartAsync 가 정리한다).
            if (!slotHandedOver)
                ReleaseSlot();

            if (session == null)
                SocketUtils.CloseSocket(socket);

            throw;
        }
    }
}

internal static class TlsSocketExtensions
{
    public static string SafeRemote(this Socket socket)
    {
        try { return socket.RemoteEndPoint?.ToString() ?? "?"; }
        catch (Exception) { return "?"; }
    }
}
