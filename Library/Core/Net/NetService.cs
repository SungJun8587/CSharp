// NetService.cs : implementation of the NetService, ServerService and ClientService classes.
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Net;

/// <summary>
/// 서버/클라이언트 서비스의 공통 기반. 세션 팩토리, 옵션, 세션 목록과 종료 대기를 담당한다.
/// I/O 완료 처리는 .NET ThreadPool 이 맡으므로 별도의 워커 스레드 수 설정이 없다.
/// </summary>
public abstract class NetService : IDisposable, IAsyncDisposable
{
    private readonly Func<Session> _sessionFactory;
    private readonly ServiceCore _core;

    protected NetService(IPEndPoint endPoint, Func<Session> sessionFactory, NetServiceOptions? options)
    {
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        Options = options ?? new NetServiceOptions();
        _core = new ServiceCore(Options);
        Sessions = new SessionManager();
    }

    /// <summary>서버는 바인드 주소, 클라이언트는 접속 대상 주소.</summary>
    public IPEndPoint EndPoint { get; }

    public NetServiceOptions Options { get; }

    public SessionManager Sessions { get; }

    /// <summary>Close() 가 시작되었는지 여부.</summary>
    public bool IsClosing => _core.IsClosing;

    internal bool HasLogger => Options.Logger != null;

    /// <summary>로거가 있고 해당 레벨이 켜져 있는지. 로거의 예외는 삼킨다.</summary>
    internal bool IsLogEnabled(NetLogLevel level) => _core.IsLogEnabled(level);

    /// <summary>로거로 기록한다. 로거가 없거나 예외를 던져도 서비스 동작에는 영향이 없다.</summary>
    internal void Log(NetLogLevel level, string message, Exception? exception = null) => _core.Log(level, message, exception);

    /// <summary>
    /// 서비스를 종료하고 모든 세션이 정리될 때까지 기다린다.
    /// </summary>
    /// <returns>제한 시간 안에 모든 세션이 정리되었으면 true.</returns>
    public abstract bool Close(int timeoutMs = NetConstants.DefaultCloseTimeoutMs);

    /// <summary>
    /// Close() 의 비동기 버전. 스레드를 블로킹하지 않고 모든 세션이 정리되기를 기다린다.
    /// </summary>
    /// <param name="timeoutMs">정리 대기 제한 시간(ms).</param>
    /// <param name="cancellationToken">대기 취소. 취소돼도 이미 시작된 종료는 계속 진행된다.</param>
    /// <returns>제한 시간 안에 모든 세션이 정리되었으면 true.</returns>
    public abstract Task<bool> CloseAsync(int timeoutMs = NetConstants.DefaultCloseTimeoutMs, CancellationToken cancellationToken = default);

    /// <summary>Close() 를 호출한다(기본 대기 시간).</summary>
    public void Dispose() => Close();

    /// <summary>CloseAsync() 를 호출한다(기본 대기 시간).</summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }

    /// <summary>종료 상태로 전환한다. 처음 전환한 호출만 true.</summary>
    protected bool BeginClose() => _core.BeginClose();

    /// <summary>
    /// 모든 세션 종료를 요청하고 정리 완료를 기다린다.
    /// 세션 제거 시점에 이미 closing 이 true 이므로, 확인과 대기 사이에 마지막 세션이 빠져도 신호를 놓치지 않는다.
    /// </summary>
    protected bool CloseAllSessionsAndWait(int timeoutMs)
    {
        Sessions.BeginCloseAllSessions(CloseReason.ServiceClosing);

        if (Sessions.AreAllSessionsClosed)
            return true;

        return _core.WaitAllClosed(timeoutMs);
    }

    /// <summary>CloseAllSessionsAndWait 의 비동기 버전.</summary>
    protected async Task<bool> CloseAllSessionsAndWaitAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        Sessions.BeginCloseAllSessions(CloseReason.ServiceClosing);

        if (Sessions.AreAllSessionsClosed)
            return true;

        return await _core.WaitAllClosedAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>IdleTimeoutMs 가 설정된 경우 유휴 검사 타이머를 시작한다(중복 호출 허용).</summary>
    protected void StartIdleMonitor() => _core.StartIdleMonitor(now => Sessions.CheckIdle(now, Options.IdleTimeoutMs));

    /// <summary>유휴 검사 타이머를 멈춘다.</summary>
    protected void StopIdleMonitor() => _core.StopIdleMonitor();

    internal Session CreateSession() => _sessionFactory();

    /// <summary>
    /// 현재 통계를 읽는다. 세션 수에 비례하는 비용(열거)이 들므로 주기적 모니터링 용도로 쓴다.
    /// </summary>
    public NetStatsSnapshot GetStats()
    {
        int connected = 0;
        long received = 0, sent = 0, pending = 0;
        Sessions.Accumulate(ref connected, ref received, ref sent, ref pending);
        return _core.MakeStats(connected, received, sent, pending);
    }

    internal void OnSessionStarted() => _core.OnSessionStarted();

    internal void OnSessionClosed(long bytesReceived, long bytesSent) => _core.OnSessionClosed(bytesReceived, bytesSent);

    /// <summary>거부 카운트를 올리고 누적값을 돌려준다.</summary>
    internal long CountRejected() => _core.CountRejected();

    /// <summary>세션 슬롯 하나를 원자적으로 예약한다(MaxSessionCount 초과 방지). 예약한 슬롯은 RemoveSession/ReleaseSlot 으로 반환한다.</summary>
    internal bool TryReserveSlot() => _core.TryReserveSlot();

    internal void ReleaseSlot() => _core.ReleaseSlot();

    internal void RemoveSession(Session session)
    {
        if (!Sessions.Remove(session))
            return;

        ReleaseSlot();

        if (IsClosing && Sessions.AreAllSessionsClosed)
            _core.SignalAllClosed();
    }

    /// <summary>NoDelay, KeepAlive, 버퍼 크기 등 서비스 옵션을 소켓에 적용한다.</summary>
    internal void ConfigureSocket(Socket socket) => _core.ConfigureSocket(socket);
}

/// <summary>
/// 접속을 받는 서버 서비스.
/// Close() 순서: 1) 리슨 소켓을 먼저 닫아 신규 접속을 차단 → 2) 모든 세션 종료 요청 → 3) 정리 완료 대기.
/// </summary>
public sealed class ServerService : NetService
{
    private Listener? _listener;

    public ServerService(IPEndPoint endPoint, Func<Session> sessionFactory, NetServiceOptions? options = null)
        : base(endPoint, sessionFactory, options)
    {
    }

    /// <summary>리슨을 시작한다. 바인드/리슨에 실패하면 false.</summary>
    public bool Start()
    {
        if (_listener != null || IsClosing)
            return false;

        var listener = new Listener(this, EndPoint, Options);
        if (!listener.Start())
        {
            Log(NetLogLevel.Error, $"리슨 실패: {EndPoint}");
            return false;
        }

        _listener = listener;
        StartIdleMonitor();
        Log(NetLogLevel.Info, $"리슨 시작: {listener.LocalEndPoint}");
        return true;
    }

    /// <summary>실제 바인드된 로컬 주소. 포트 0 으로 시작했을 때 할당된 포트를 확인할 수 있다.</summary>
    public IPEndPoint? LocalEndPoint => _listener?.LocalEndPoint;

    public override bool Close(int timeoutMs = NetConstants.DefaultCloseTimeoutMs)
    {
        BeginClose();
        StopIdleMonitor();
        _listener?.Stop();
        Log(NetLogLevel.Info, $"서버 종료 시작: 세션 {Sessions.Count}개");

        bool closed = CloseAllSessionsAndWait(timeoutMs);
        if (!closed)
            Log(NetLogLevel.Warning, $"서버 종료 대기 시간 초과: 세션 {Sessions.Count}개 남음");

        return closed;
    }

    public override async Task<bool> CloseAsync(int timeoutMs = NetConstants.DefaultCloseTimeoutMs, CancellationToken cancellationToken = default)
    {
        BeginClose();
        StopIdleMonitor();

        if (_listener != null)
            await _listener.StopAsync().ConfigureAwait(false);

        Log(NetLogLevel.Info, $"서버 종료 시작: 세션 {Sessions.Count}개");

        bool closed = await CloseAllSessionsAndWaitAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        if (!closed)
            Log(NetLogLevel.Warning, $"서버 종료 대기 시간 초과: 세션 {Sessions.Count}개 남음");

        return closed;
    }

    /// <summary>Listener 가 Accept 한 소켓을 세션으로 만들어 시작한다.</summary>
    internal void OnAccepted(Socket socket)
    {
        if (IsClosing || !TryReserveSlot())
        {
            if (!IsClosing)
            {
                // 공격/폭주 시 로그가 넘치지 않도록 첫 건과 1000건마다만 기록한다.
                long rejected = CountRejected();
                if (rejected == 1 || rejected % 1000 == 0)
                    Log(NetLogLevel.Warning, $"접속 거부: MaxSessionCount={Options.MaxSessionCount} 도달 (누적 {rejected}건)");
            }

            SocketUtils.CloseSocket(socket);
            return;
        }

        Session session;
        try
        {
            ConfigureSocket(socket);

            session = CreateSession();
            session.Attach(this, socket, Sessions.GenerateSessionId());
        }
        catch (Exception)
        {
            ReleaseSlot();
            throw;
        }

        if (!Sessions.Add(session))
        {
            ReleaseSlot();
            SocketUtils.CloseSocket(socket);
            return;
        }

        if (Options.StartOnThreadPool)
            ThreadPool.UnsafeQueueUserWorkItem(static state => state.Item1.StartAccepted(state.Item2), (this, session), false);
        else
            StartAccepted(session);
    }

    private void StartAccepted(Session session)
    {
        session.Start();

        // Add 와 Start 사이에 Close() 의 일괄 종료가 지나갔다면(연결 전이라 Disconnect 가 무시됨) 여기서 직접 끊는다.
        if (IsClosing)
            session.Disconnect(CloseReason.ServiceClosing);
    }
}

/// <summary>
/// 서버에 접속하는 클라이언트 서비스. 같은 서비스로 여러 세션을 연결할 수 있다.
/// Close() 순서(서버와 반대): 세션 종료 통지를 처리할 I/O 가 살아 있는 동안 세션을 먼저 모두 정리한다.
/// </summary>
public sealed class ClientService : NetService
{
    private sealed class ConnectContext
    {
        public Session Session = null!;
        public Socket Socket = null!;
        public TaskCompletionSource<Session>? Tcs;
        public CancellationToken Token;
        public CancellationTokenRegistration CancelRegistration;
    }

    public ClientService(IPEndPoint endPoint, Func<Session> sessionFactory, NetServiceOptions? options = null)
        : base(endPoint, sessionFactory, options)
    {
    }

    /// <summary>
    /// 비동기 연결을 하나 시작한다. 반환값은 "연결 시도를 게시했는가"이며 연결 성공을 뜻하지 않는다.
    /// 실제 결과는 Session.OnConnected / OnConnectFailed 로 통지된다.
    /// </summary>
    public bool ConnectOneMoreSession() => BeginConnect(null, default);

    /// <summary>
    /// 연결을 하나 만들고, 연결이 수립되어 세션이 시작(OnConnected 호출)되면 그 세션을 돌려준다.
    /// 연결에 실패하면 SocketException, 서비스가 종료 중이거나 MaxSessionCount 에 도달했으면 InvalidOperationException,
    /// 취소되면 OperationCanceledException 으로 완료된다.
    /// </summary>
    /// <param name="cancellationToken">연결 시도 취소. 진행 중인 연결 소켓을 닫아 중단한다.</param>
    public Task<Session> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<Session>(cancellationToken);

        var tcs = new TaskCompletionSource<Session>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!BeginConnect(tcs, cancellationToken))
        {
            return Task.FromException<Session>(
                new InvalidOperationException("서비스가 종료 중이거나 MaxSessionCount 에 도달해 연결을 시작할 수 없습니다."));
        }

        return tcs.Task;
    }

    /// <summary>연결을 count 개 시도하고, 게시에 성공한 개수를 반환한다.</summary>
    public int ConnectSessions(int count)
    {
        int posted = 0;
        for (int i = 0; i < count; i++)
        {
            if (ConnectOneMoreSession())
                posted++;
        }

        return posted;
    }

    public override bool Close(int timeoutMs = NetConstants.DefaultCloseTimeoutMs)
    {
        BeginClose();
        StopIdleMonitor();
        Log(NetLogLevel.Info, $"클라이언트 종료 시작: 세션 {Sessions.Count}개");

        bool closed = CloseAllSessionsAndWait(timeoutMs);
        if (!closed)
            Log(NetLogLevel.Warning, $"클라이언트 종료 대기 시간 초과: 세션 {Sessions.Count}개 남음");

        return closed;
    }

    public override async Task<bool> CloseAsync(int timeoutMs = NetConstants.DefaultCloseTimeoutMs, CancellationToken cancellationToken = default)
    {
        BeginClose();
        StopIdleMonitor();
        Log(NetLogLevel.Info, $"클라이언트 종료 시작: 세션 {Sessions.Count}개");

        bool closed = await CloseAllSessionsAndWaitAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        if (!closed)
            Log(NetLogLevel.Warning, $"클라이언트 종료 대기 시간 초과: 세션 {Sessions.Count}개 남음");

        return closed;
    }

    /// <summary>
    /// 연결 시도를 게시한다. tcs 가 있으면 연결 결과(성공/실패/취소)를 그 태스크로 완료시킨다.
    /// </summary>
    /// <returns>
    /// 사전 조건 실패(종료 중, 상한 도달)면 false. 일단 게시한 뒤에는 콜백 방식(tcs == null)에서는 게시 성공 여부를,
    /// 태스크 방식에서는 항상 true 를 반환한다(실패는 태스크로 전달).
    /// </returns>
    private bool BeginConnect(TaskCompletionSource<Session>? tcs, CancellationToken token)
    {
        if (IsClosing || !TryReserveSlot())
            return false;

        StartIdleMonitor();

        Socket socket;
        Session session;
        try
        {
            socket = SocketUtils.CreateTcpSocket(EndPoint.AddressFamily);
            ConfigureSocket(socket);

            session = CreateSession();
            session.Attach(this, socket, Sessions.GenerateSessionId());
        }
        catch (Exception)
        {
            ReleaseSlot();
            throw;
        }

        // 연결 완료 전에 미리 추적 목록에 등록해 Close() 가 연결 중인 세션도 기다리게 한다.
        Sessions.Add(session);

        var context = new ConnectContext { Session = session, Socket = socket, Tcs = tcs, Token = token };
        var args = new SocketAsyncEventArgs { RemoteEndPoint = EndPoint, UserToken = context };
        args.Completed += OnConnectCompleted;

        // 취소되면 연결 중인 소켓을 닫아 ConnectAsync 를 OperationAborted 로 끝낸다.
        if (tcs != null && token.CanBeCanceled)
            context.CancelRegistration = token.Register(static state => SocketUtils.CloseSocket(((ConnectContext)state!).Socket), context);

        bool isPending;
        try
        {
            isPending = socket.ConnectAsync(args);
        }
        catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
        {
            args.Dispose();
            CompleteConnect(context, SocketError.SocketError);
            return tcs != null;
        }

        if (!isPending)
            OnConnectCompleted(socket, args);

        return true;
    }

    private void OnConnectCompleted(object? sender, SocketAsyncEventArgs args)
    {
        var context = (ConnectContext)args.UserToken!;
        SocketError error = args.SocketError;
        args.Dispose();

        CompleteConnect(context, error);
    }

    private void CompleteConnect(ConnectContext context, SocketError error)
    {
        context.CancelRegistration.Dispose();

        Session session = context.Session;
        bool cancelled = context.Tcs != null && context.Token.IsCancellationRequested;

        if (error != SocketError.Success || IsClosing || cancelled)
        {
            SocketError reported = error == SocketError.Success ? SocketError.OperationAborted : error;
            session.FailConnect(reported);

            if (context.Tcs != null)
            {
                if (cancelled)
                    context.Tcs.TrySetCanceled(context.Token);
                else if (error == SocketError.Success)
                    context.Tcs.TrySetException(new InvalidOperationException("서비스가 종료되어 연결을 취소했습니다."));
                else
                    context.Tcs.TrySetException(new SocketException((int)reported));
            }

            return;
        }

        session.Start();

        if (IsClosing)
            session.Disconnect(CloseReason.ServiceClosing);

        context.Tcs?.TrySetResult(session);
    }
}
