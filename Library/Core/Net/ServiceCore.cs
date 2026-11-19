// ServiceCore.cs : implementation of the ISocketServiceOptions interface and the ServiceCore class.
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Net;

/// <summary>TCP 기반 서비스(NetService, TlsService)의 옵션이 공통으로 갖는 항목. ServiceCore 와 소켓 설정이 이 항목만 본다.</summary>
internal interface ISocketServiceOptions
{
    int MaxSessionCount { get; }
    bool NoDelay { get; }
    bool KeepAlive { get; }
    int KeepAliveTimeSec { get; }
    int KeepAliveIntervalSec { get; }
    int SocketSendBufferSize { get; }
    int SocketRecvBufferSize { get; }
    int IdleTimeoutMs { get; }
    INetLogger? Logger { get; }
}

/// <summary>
/// NetService(IOCP 스타일 Session)와 TlsService(SslStream 세션)가 함께 쓰는 서비스 공통부.
/// 세션 슬롯 예약, 누적 통계, 로거 호출, 유휴 검사 타이머, 종료 대기를 한 곳에서 맡고,
/// 세션 목록과 세션 생성은 각 서비스가 맡는다.
/// </summary>
internal sealed class ServiceCore
{
    private readonly ISocketServiceOptions _options;
    private readonly TaskCompletionSource<bool> _allClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _idleLock = new object();
    private Timer? _idleTimer;
    private int _closing;
    private int _sessionSlots;

    // 통계: 종료된 세션의 누적값(연결 중인 세션은 조회 시 세션별 카운터에서 합산)
    private long _totalConnected;
    private long _totalDisconnected;
    private long _totalRejected;
    private long _closedBytesReceived;
    private long _closedBytesSent;

    public ServiceCore(ISocketServiceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsClosing => Volatile.Read(ref _closing) == 1;

    /// <summary>종료 상태로 전환한다. 처음 전환한 호출만 true.</summary>
    public bool BeginClose() => Interlocked.Exchange(ref _closing, 1) == 0;

    //****************************************************************
    // 로그
    //****************************************************************

    /// <summary>로거가 있고 해당 레벨이 켜져 있는지. 로거의 예외는 삼킨다.</summary>
    public bool IsLogEnabled(NetLogLevel level) => _options.Logger.SafeIsEnabled(level);

    /// <summary>로거로 기록한다. 로거가 없거나 예외를 던져도 서비스 동작에는 영향이 없다.</summary>
    public void Log(NetLogLevel level, string message, Exception? exception = null) => _options.Logger.SafeLog(level, message, exception);

    //****************************************************************
    // 슬롯 / 통계
    //****************************************************************

    /// <summary>
    /// 세션 슬롯 하나를 원자적으로 예약한다. 동시에 여러 접속이 몰려도 MaxSessionCount 를 넘지 않는다.
    /// 예약한 슬롯은 세션이 제거될 때 또는 세션 생성에 실패했을 때(ReleaseSlot) 반환한다.
    /// </summary>
    public bool TryReserveSlot()
    {
        int max = _options.MaxSessionCount;

        while (true)
        {
            int current = Volatile.Read(ref _sessionSlots);
            if (current >= max)
                return false;

            if (Interlocked.CompareExchange(ref _sessionSlots, current + 1, current) == current)
                return true;
        }
    }

    public void ReleaseSlot() => Interlocked.Decrement(ref _sessionSlots);

    /// <summary>거부 카운트를 올리고 누적값을 돌려준다.</summary>
    public long CountRejected() => Interlocked.Increment(ref _totalRejected);

    public void OnSessionStarted() => Interlocked.Increment(ref _totalConnected);

    public void OnSessionClosed(long bytesReceived, long bytesSent)
    {
        Interlocked.Add(ref _closedBytesReceived, bytesReceived);
        Interlocked.Add(ref _closedBytesSent, bytesSent);
        Interlocked.Increment(ref _totalDisconnected);
    }

    /// <summary>연결 중인 세션의 합계(connected, received, sent, pending)를 받아 누적값과 합쳐 스냅샷을 만든다.</summary>
    public NetStatsSnapshot MakeStats(int connected, long received, long sent, long pending)
        => new NetStatsSnapshot(
            connected,
            Interlocked.Read(ref _totalConnected),
            Interlocked.Read(ref _totalDisconnected),
            Interlocked.Read(ref _totalRejected),
            received + Interlocked.Read(ref _closedBytesReceived),
            sent + Interlocked.Read(ref _closedBytesSent),
            pending);

    //****************************************************************
    // 소켓
    //****************************************************************

    /// <summary>NoDelay, KeepAlive, 버퍼 크기 등 서비스 옵션을 소켓에 적용한다.</summary>
    public void ConfigureSocket(Socket socket)
        => SocketUtils.ConfigureTcp(socket, _options.NoDelay, _options.KeepAlive, _options.KeepAliveTimeSec, _options.KeepAliveIntervalSec,
            _options.SocketSendBufferSize, _options.SocketRecvBufferSize);

    //****************************************************************
    // 유휴 검사
    //****************************************************************

    /// <summary>IdleTimeoutMs 가 설정된 경우 유휴 검사 타이머를 시작한다(중복 호출 허용). sweep 은 현재 TickCount64 를 받는다.</summary>
    public void StartIdleMonitor(Action<long> sweep)
    {
        int timeout = _options.IdleTimeoutMs;
        if (timeout <= 0)
            return;

        lock (_idleLock)
        {
            if (_idleTimer != null)
                return;

            int period = Math.Max(100, timeout / 4);
            _idleTimer = new Timer(_ => Sweep(sweep), null, period, period);
        }
    }

    public void StopIdleMonitor()
    {
        lock (_idleLock)
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
        }
    }

    private void Sweep(Action<long> sweep)
    {
        try
        {
            sweep(Environment.TickCount64);
        }
        catch (Exception e)
        {
            // 타이머 콜백의 예외가 프로세스를 죽이지 않도록 기록만 하고 삼킨다(개별 세션 예외는 세션 내부에서 처리).
            Log(NetLogLevel.Error, "유휴 세션 검사 실패", e);
        }
    }

    //****************************************************************
    // 종료 대기
    //****************************************************************

    /// <summary>모든 세션이 정리되었음을 알린다. 세션 제거 시점에 이미 closing 이 true 이므로 신호를 놓치지 않는다.</summary>
    public void SignalAllClosed() => _allClosed.TrySetResult(true);

    /// <summary>모든 세션이 정리될 때까지 기다린다. 제한 시간 안에 정리되었으면 true.</summary>
    public bool WaitAllClosed(int timeoutMs) => _allClosed.Task.Wait(timeoutMs);

    /// <summary>WaitAllClosed 의 비동기 버전. 취소되면 OperationCanceledException(이미 시작된 종료는 계속 진행된다).</summary>
    public async Task<bool> WaitAllClosedAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delay = Task.Delay(timeoutMs, delayCts.Token);

        Task finished = await Task.WhenAny(_allClosed.Task, delay).ConfigureAwait(false);
        if (ReferenceEquals(finished, _allClosed.Task))
        {
            delayCts.Cancel();
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }
}
