// RedisConnectionPool.cs : 여러 RedisClient 연결을 분배/관리하는 커넥션 풀.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;

namespace Core.Redis;

/// <summary>
/// 놀고 있는(Free) 커넥션을 대여해 명령을 전송하고, 응답 콜백이 끝나면 커넥션을 자동 반납하는 풀.
///
/// <para><b>격리와 재연결</b>: 반납 시점에 연결이 끊어져 있으면 Free 큐가 아니라 격리 큐로 보낸다. 전용 스레드가 격리 큐를 훑어
/// 클라이언트별 지수 백오프(+지터)로 재연결을 시도하고, 성공한 커넥션만 다시 돌려보낸다. 죽은 커넥션이 Free 큐에 섞여
/// 요청을 계속 실패시키는 일이 없고, 서버가 오래 죽어 있는 동안 무의미하게 두드리지도 않는다.</para>
///
/// <para><b>대기 큐</b>: 모든 커넥션이 사용 중이면 요청을 대기 큐에 넣고 true를 반환한다. 커넥션이 반납되면 Free 큐를 거치지 않고
/// 곧바로 대기 요청에 태운다. 대기 큐가 상한(<see cref="RedisConst.MaxPendingCommands"/>)을 넘으면 그때만 거부(false, 콜백 미호출)하고,
/// 기한(<see cref="RedisConst.PendingTimeoutMs"/>)을 넘긴 요청은 연결 오류로 끝낸다. true를 반환한 요청의 콜백은 항상 호출된다.</para>
///
/// <para><b>응답 무응답 감지</b>: 명령 응답이 <see cref="RedisConst.PoolCommandTimeoutMs"/>를 넘도록 오지 않는 커넥션은 끊고 재연결한다.
/// 이 풀 경로에는 KEYS나 수 초를 넘기는 스크립트처럼 오래 걸리는 명령을 쓰지 않는다는 전제의 값이다.</para>
///
/// <para><b>세대 가드</b>: <see cref="Clear"/>/재<see cref="Init"/>마다 세대 번호가 올라간다. 옛 세대에서 대여해 간 커넥션의 응답이
/// 뒤늦게 도착해도 새로 초기화된 풀의 큐에는 섞여 들어가지 않는다.</para>
///
/// <para><b>lifecycle 계약</b>: Init()/Clear()는 서버 시작/종료·재초기화 단계에서만 호출하고, 정상 트래픽이 흐르는 도중에는 호출하지 않는다.</para>
/// </summary>
/// <example>
/// <code>
/// var pool = new RedisConnectionPool();
/// pool.Init("127.0.0.1", 6379, dbIndex: 0, poolSize: 10);   // 커넥션 10개 생성, 재연결 스레드 자동 시작
/// pool.SendCommand(new[] { "INCR", "VisitorCount" }, res => { /* 완료 처리 */ });
/// </code>
/// </example>
public sealed class RedisConnectionPool : IDisposable
{
    private const byte ConnectNotTried = 0;     // 시도하지 못함(종료 요청 등)
    private const byte ConnectOk = 1;
    private const byte ConnectFailed = 2;

    private sealed class PendingCommand
    {
        public string[] Args = Array.Empty<string>();
        public RedisCallback? Callback;
        public long EnqueuedMs;                 // 대기 시작 시각(단조 시계) — 오래 기다린 요청을 에러로 끝내는 기준
    }

    private readonly object _lock = new object();                       // 풀 동기화 (_allClients/_free/_broken/_pending 공용)
    private List<RedisClient> _allClients = new List<RedisClient>();
    private readonly Queue<RedisClient> _free = new Queue<RedisClient>();       // 사용 가능한(연결된) 클라이언트
    private readonly Queue<RedisClient> _broken = new Queue<RedisClient>();     // 연결이 끊겨 재연결을 기다리는 격리 큐
    private readonly Queue<PendingCommand> _pending = new Queue<PendingCommand>();  // 풀이 바닥났을 때 대기 중인 요청

    private string _host = string.Empty;
    private ushort _port;
    private int _dbIndex;
    private string _userId = string.Empty;
    private string _password = string.Empty;                            // 로그에 남기지 않는다
    private int _reconnectIntervalSec = RedisConst.DefaultReconnectIntervalSec;

    private long _generation;                                           // Interlocked/Volatile로만 접근
    private volatile bool _initialized;
    private long _lastRejectLogMs;                                      // 대기 큐 포화 로그를 마지막으로 남긴 시각 — 로그 폭주 방지
    private long _rejectedSinceLog;

    // 재연결 스레드와 신호. 락 순서는 항상 _lock -> _reconnectLock.
    private Thread? _reconnectThread;
    private readonly object _reconnectLock = new object();
    private bool _reconnectWake;                                        // 커넥션이 격리 큐로 들어와 즉시 점검이 필요함 — _reconnectLock으로 보호
    private volatile bool _stopping;

    // 클라이언트별 백오프 상태. 재연결 스레드 전용이며, Clear()는 그 스레드를 정지·조인한 뒤에만 접근한다.
    private readonly Dictionary<RedisClient, ReconnectBackoff> _backoffMap = new Dictionary<RedisClient, ReconnectBackoff>();

    public void Dispose() => Clear();

    // ===================================================================================
    // 초기화 / 정리
    // ===================================================================================

    /// <summary>
    /// poolSize만큼 클라이언트를 생성·연결하고 재연결 스레드를 시작한다. 이미 초기화된 풀에 다시 호출해도 안전하다
    /// (먼저 <see cref="Clear"/>로 기존 커넥션과 스레드를 완전히 정리한 뒤 처음부터 다시 채운다).
    /// 일부만 연결돼도 풀은 열리며 나머지는 재연결 스레드가 이어서 붙인다. 전부 실패하면(주소/인증 설정 오류 가능성이 큰 경우) 실패한다.
    /// </summary>
    /// <param name="dbIndex">각 커넥션이 접속 직후 SELECT로 고정할 논리 DB(0이면 생략). 재연결된 커넥션에도 같이 적용된다.</param>
    /// <param name="reconnectIntervalSec">격리 큐를 훑어 재연결을 시도하는 주기(초)</param>
    /// <param name="userId">ACL 사용자 이름(비어 있으면 <c>AUTH &lt;password&gt;</c>)</param>
    /// <param name="password">비밀번호(비어 있으면 인증 생략)</param>
    public bool Init(string host, ushort port, int dbIndex, int poolSize, int reconnectIntervalSec = RedisConst.DefaultReconnectIntervalSec,
        string userId = "", string password = "")
    {
        // 풀 크기가 0 이하인 채로 "성공"하면 커넥션이 0개라 모든 요청이 대기 큐에만 쌓이고 아무도 처리해 주지 않는다.
        if (poolSize <= 0)
        {
            LogManager.Error("RedisConnectionPool.Init: poolSize({0})는 0보다 커야 합니다", poolSize);
            return false;
        }

        if (string.IsNullOrEmpty(host))
        {
            LogManager.Error("RedisConnectionPool.Init: host가 비어 있습니다");
            return false;
        }

        // 재초기화에 대비해 먼저 완전히 정리한다. 처음 호출이면 사실상 아무 일도 하지 않는다.
        Clear();

        lock (_lock)
        {
            _host = host;
            _port = port;
            _dbIndex = dbIndex;
            _userId = userId ?? string.Empty;
            _password = password ?? string.Empty;
            _reconnectIntervalSec = reconnectIntervalSec > 0 ? reconnectIntervalSec : RedisConst.DefaultReconnectIntervalSec;
        }

        // 블로킹 작업인 접속은 락 밖에서 한다. 풀을 채우는 동안 다른 스레드의 SendCommand()/반납이 이 락에 막히지 않게 한다.
        var clients = new List<RedisClient>(poolSize);
        for (int i = 0; i < poolSize; ++i)
            clients.Add(new RedisClient());

        var results = new byte[poolSize];
        ConnectClientsParallel(clients, results, host, port, dbIndex, _userId, _password, null);

        int connected = 0;
        lock (_lock)
        {
            for (int i = 0; i < poolSize; ++i)
            {
                _allClients.Add(clients[i]);
                if (results[i] == ConnectOk)
                {
                    _free.Enqueue(clients[i]);
                    ++connected;
                }
                else
                {
                    _broken.Enqueue(clients[i]);
                }
            }
        }

        if (connected == 0)
        {
            Clear();
            return false;
        }

        if (connected < poolSize)
            LogManager.Error("RedisConnectionPool.Init: {0}/{1} 커넥션만 연결됨 - 나머지는 재연결 스레드가 이어서 시도", connected, poolSize);

        _initialized = true;
        StartReconnectLoop();
        return true;
    }

    /// <summary>
    /// 재연결 스레드를 정지시키고 모든 커넥션을 끊어 풀 리소스를 정리한다. 대기 큐에 남은 요청은 연결 오류로 완료시킨다.
    /// </summary>
    public void Clear()
    {
        // 재연결 스레드가 _lock을 필요로 하므로, _lock을 잡기 전에 먼저 정지/조인한다(그렇지 않으면 서로 기다리는 교착).
        StopReconnectLoop();

        List<RedisClient> toDisconnect;
        var pendingToFail = new List<PendingCommand>();
        lock (_lock)
        {
            // 세대 변경 + 큐 비우기 + _initialized=false를 하나의 풀 상태 전이로 묶는다.
            Interlocked.Increment(ref _generation);

            _free.Clear();
            _broken.Clear();

            // 대기 중이던 요청은 락 밖에서 연결 오류로 완료시킨다 — 콜백이 영원히 오지 않는 요청이 남지 않게 한다.
            // 이 풀로 다시 보내는 재시도는 풀이 비초기화 상태라 거부된다.
            while (_pending.Count > 0)
                pendingToFail.Add(_pending.Dequeue());

            _backoffMap.Clear();

            // 락을 쥔 채 Disconnect()하면 대기 콜백 -> 반납 -> 이 락 재진입으로 교착이 날 수 있어 목록만 옮겨 온다.
            toDisconnect = _allClients;
            _allClients = new List<RedisClient>();

            _initialized = false;
        }

        foreach (RedisClient client in toDisconnect)
            client.Disconnect();

        foreach (PendingCommand pending in pendingToFail)
            pending.Callback?.Invoke(RedisHelper.MakeError(RedisConst.ErrPoolCleared));
    }

    // ===================================================================================
    // 명령 전송
    // ===================================================================================

    /// <summary>
    /// Redis 명령을 비동기 실행한다. 놀고 있는 커넥션이 없으면 대기 큐에 넣고 true를 반환한다("접수됨" — 실제 전송은 커넥션이 반납되는 대로 이어진다).
    /// 호출 후 args 목록을 바꾸지 않는다(즉시 전송 경로는 호출 안에서 인코딩이 끝나지만, 대기 큐에 들어가는 경우는 사본을 보관한다).
    /// </summary>
    /// <returns>요청이 접수됐는지. false면 콜백은 호출되지 않는다(풀 미초기화 또는 대기 큐 포화).</returns>
    public bool SendCommand(IReadOnlyList<string> args, RedisCallback? callback)
    {
        if (!_initialized)
            return false;

        RedisClient? client = null;
        lock (_lock)
        {
            // 유휴 중에 서버나 중간 장비가 끊어 버린 커넥션은 반납 경로를 거치지 않아 Free 큐에 그대로 남아 있다. 그런 커넥션에 명령을
            // 실으면 요청 하나가 에러로 실패하므로, 꺼내는 시점에 연결 상태를 확인해 끊긴 것은 격리하고 다음 커넥션을 고른다.
            while (_free.Count > 0)
            {
                RedisClient candidate = _free.Dequeue();
                if (candidate.IsConnected)
                {
                    client = candidate;
                    break;
                }

                _broken.Enqueue(candidate);
                WakeReconnect();
            }

            // "Free에서 꺼내기 OR 대기 큐에 넣기"를 하나의 락 구간에서 결정한다 — 둘이 나뉘면 그 사이에 반납된 커넥션을
            // 아무도 쓰지 못한 채 대기 요청이 방치되는 경합이 생긴다.
            if (client == null)
            {
                if (_pending.Count >= RedisConst.MaxPendingCommands)
                {
                    // 포화 상태에서는 요청마다 로그를 남기면 로그 자체가 병목이 된다 — 초당 한 번만 남기고 그 사이 거부된 건수를 함께 적는다.
                    Interlocked.Increment(ref _rejectedSinceLog);
                    long nowMs = TimeUtil.MonotonicNowMillis();
                    long lastMs = Interlocked.Read(ref _lastRejectLogMs);
                    if (nowMs - lastMs >= 1000 && Interlocked.CompareExchange(ref _lastRejectLogMs, nowMs, lastMs) == lastMs)
                    {
                        long rejected = Interlocked.Exchange(ref _rejectedSinceLog, 0);
                        LogManager.Error("RedisConnectionPool.SendCommand: 대기 큐 포화(상한 {0}) — 요청 거부(최근 {1}건)", RedisConst.MaxPendingCommands, rejected);
                    }
                    return false;
                }

                var argsCopy = new string[args.Count];
                for (int i = 0; i < argsCopy.Length; ++i)
                    argsCopy[i] = args[i];

                _pending.Enqueue(new PendingCommand { Args = argsCopy, Callback = callback, EnqueuedMs = TimeUtil.MonotonicNowMillis() });
                return true;
            }
        }

        DispatchOnClient(client, args, callback);
        return true;
    }

    // client 하나에 실제로 명령을 실어 보낸다(즉시 전송 경로와, 반납된 커넥션에 대기 요청을 바로 태우는 경로 공용).
    // 명령을 보내는 시점의 세대를 캡처해 두었다가, 완료 후 반납할 때 그 사이 풀이 새로 초기화됐으면 옛 세대로 걸러낸다.
    private void DispatchOnClient(RedisClient client, IReadOnlyList<string> args, RedisCallback? callback)
    {
        long generation = Interlocked.Read(ref _generation);

        // 사용자 콜백이 예외를 던져도 커넥션 반납만은 항상 보장한다. 그렇지 않으면 이 커넥션이 풀로 영원히 돌아오지 못하고 사라진다.
        void Wrapped(RedisValue res)
        {
            try
            {
                callback?.Invoke(res);
            }
            catch (Exception e)
            {
                LogManager.Error("RedisConnectionPool: 콜백에서 예외 발생 - {0}", e.Message);
            }

            PushConnection(client, generation);
        }

        // RedisClient.SendCommand()는 반환값과 무관하게 콜백을 정확히 한 번 호출한다 — 성공하면 실제 응답으로, 실패하면 에러 값으로.
        // 그래서 반환값을 보고 또 콜백이나 반납을 하면 안 된다(같은 커넥션이 Free 큐에 중복으로 들어가 두 명령에 동시에 대여될 수 있다).
        client.SendCommand(args, Wrapped);
    }

    // 사용이 끝난 커넥션을 반납한다. 연결이 살아 있으면 대기 큐부터 확인해 기다리던 요청에 곧바로 태우고(Free 큐 왕복 생략),
    // 없으면 Free 큐로, 끊겨 있으면 격리 큐로 보낸다.
    private void PushConnection(RedisClient client, long generation)
    {
        PendingCommand? next = null;
        lock (_lock)
        {
            // 세대 검사, 연결 검사, 큐 변경을 하나의 락 구간으로 묶어 Clear()의 "세대 증가 + 큐 비우기"와 원자적으로 순서가 정해지게 한다.
            // 세대가 다르면 이 커넥션은 이미 옛 Clear()가 정리했으므로 새 풀에는 아무것도 반영하지 않고 버린다.
            if (generation != Interlocked.Read(ref _generation))
                return;

            if (!client.IsConnected)
            {
                _broken.Enqueue(client);
                WakeReconnect();    // 다음 점검 주기를 기다리지 않고 바로 재접속을 시도하게 한다
                return;
            }

            if (_pending.Count > 0)
                next = _pending.Dequeue();
            else
                _free.Enqueue(client);
        }

        if (next != null)
            DispatchOnClient(client, next.Args, next.Callback);
    }

    // ===================================================================================
    // 재연결 스레드
    // ===================================================================================

    // 대기 큐에서 기한이 지난 요청을 연결 오류로 완료시킨다. Redis가 오래 죽어 있으면 대기 요청은 재연결될 때까지 콜백이 오지 않고,
    // 복구된 뒤에는 한참 전의 요청이 뒤늦게 실행된다. 호출부가 결과를 기다리는 시간에 상한을 두기 위해 점검마다 정리한다.
    // 이 에러 문자열은 IsConnectionError()가 연결 오류로 분류하므로 RedisRetry가 재시도한다.
    private void ExpirePendingCommands()
    {
        List<PendingCommand>? expired = null;
        lock (_lock)
        {
            long nowMs = TimeUtil.MonotonicNowMillis();

            // 대기 큐는 도착 순서(= 대기 시작 시각 순서)이므로 앞에서부터 기한이 지난 것만 꺼내면 된다.
            while (_pending.Count > 0 && nowMs - _pending.Peek().EnqueuedMs >= RedisConst.PendingTimeoutMs)
                (expired ??= new List<PendingCommand>()).Add(_pending.Dequeue());
        }

        if (expired == null)
            return;

        LogManager.Error("RedisConnectionPool: 대기 큐에서 {0}ms를 넘긴 요청 {1}건을 연결 오류로 종료", RedisConst.PendingTimeoutMs, expired.Count);

        foreach (PendingCommand pending in expired)
            pending.Callback?.Invoke(RedisHelper.MakeError(RedisConst.ErrPendingTimeout));
    }

    // 명령 응답이 PoolCommandTimeoutMs를 넘도록 오지 않는 커넥션을 끊는다. 서버가 응답 없이 멈추거나 경로가 반열림 상태가 되면
    // 콜백이 오지 않아 그 커넥션은 풀로 돌아오지 못하고 호출부도 영원히 기다린다(TCP keepalive는 유휴 연결용이라 수 분 뒤에야 감지).
    // Disconnect()하면 대기 콜백이 에러로 끝나고 커넥션은 격리 큐를 거쳐 재연결된다.
    private void DisconnectStalledClients()
    {
        // 락 안에서는 "무응답으로 멈춘 커넥션"만 골라낸다. Disconnect()는 대기 콜백 -> 반납(_lock)으로 이어지므로 락 밖에서 호출한다.
        List<RedisClient>? stalled = null;
        lock (_lock)
        {
            foreach (RedisClient client in _allClients)
            {
                if (client.GetPendingCommandAgeMs() >= RedisConst.PoolCommandTimeoutMs)
                    (stalled ??= new List<RedisClient>()).Add(client);
            }
        }

        if (stalled == null)
            return;

        foreach (RedisClient client in stalled)
        {
            LogManager.Error("RedisConnectionPool: 명령 응답 없음 {0}ms 초과 — 커넥션을 끊고 재연결합니다", RedisConst.PoolCommandTimeoutMs);
            client.Disconnect(RedisDisconnectReason.CommandTimeout);
        }
    }

    // 재연결 스레드를 즉시 깨워 격리 큐를 점검하게 한다. 호출 스레드가 _lock을 쥔 채여도 안전하다(_lock -> _reconnectLock 순서뿐).
    private void WakeReconnect()
    {
        lock (_reconnectLock)
        {
            _reconnectWake = true;
            Monitor.Pulse(_reconnectLock);
        }
    }

    private void StartReconnectLoop()
    {
        lock (_reconnectLock)
            _reconnectWake = false;

        _stopping = false;
        _reconnectThread = new Thread(ReconnectLoop) { IsBackground = true, Name = "RedisPoolReconnect" };
        _reconnectThread.Start();
    }

    private void StopReconnectLoop()
    {
        Thread? thread = _reconnectThread;
        if (thread == null)
        {
            _stopping = true;
            return;
        }

        _stopping = true;
        lock (_reconnectLock)
            Monitor.PulseAll(_reconnectLock);

        thread.Join();
        _reconnectThread = null;
    }

    // 격리 큐를 주기적으로 훑어 재연결을 시도하는 스레드 루프. 접속 시도(클라이언트당 최대 ConnectTimeoutMs 블로킹)는
    // 풀 락 밖에서 수행해 다른 스레드의 대여/반납을 막지 않는다.
    private void ReconnectLoop()
    {
        int intervalSec;
        string host, userId, password;
        ushort port;
        int dbIndex;
        lock (_lock)
        {
            intervalSec = _reconnectIntervalSec;
            host = _host;
            port = _port;
            dbIndex = _dbIndex;
            userId = _userId;
            password = _password;
        }

        bool recheckSoon = false;

        while (!_stopping)
        {
            // 이번 대기 시간: 점검 주기(또는 재점검 주기)와, 대기 큐에서 가장 오래된 요청의 남은 만료 시간 중 작은 값.
            // 만료 점검이 주기에만 의존하면 대기 제한이 최대 한 주기만큼 늦게 적용된다.
            long waitMs = recheckSoon ? RedisConst.ReconnectRecheckMs : (long)intervalSec * 1000;
            lock (_lock)
            {
                if (_pending.Count > 0)
                {
                    long remainMs = RedisConst.PendingTimeoutMs - (TimeUtil.MonotonicNowMillis() - _pending.Peek().EnqueuedMs);
                    waitMs = Math.Min(waitMs, Math.Max(remainMs, RedisConst.ReconnectRecheckMs));
                }
            }

            lock (_reconnectLock)
            {
                if (!_stopping && !_reconnectWake)
                    Monitor.Wait(_reconnectLock, (int)Math.Min(waitMs, int.MaxValue));

                if (_stopping)
                    break;

                _reconnectWake = false;
            }
            recheckSoon = false;

            // 재연결을 기다리며 오래 대기 중인 요청을 먼저 에러로 끝낸다.
            ExpirePendingCommands();
            DisconnectStalledClients();

            var broken = new List<RedisClient>();
            lock (_lock)
            {
                while (_broken.Count > 0)
                    broken.Add(_broken.Dequeue());
            }

            var retry = new List<RedisClient>();        // 이번 스윕에서 접속을 시도할 클라이언트
            var deferred = new List<RedisClient>();     // 이번에는 건너뛰고 격리 큐로 되돌릴 클라이언트
            long nowMs = TimeUtil.MonotonicNowMillis();

            foreach (RedisClient client in broken)
            {
                // 종료 요청이 들어온 경우 남은 클라이언트는 격리 큐로 되돌려 다음 기회에 재시도되게 한다.
                if (_stopping)
                {
                    deferred.Add(client);
                    continue;
                }

                // 아직 백오프 대기 시간이 지나지 않음 — 이번 스윕에서는 건너뛴다.
                if (!_backoffMap.TryGetValue(client, out ReconnectBackoff? backoff))
                    _backoffMap[client] = backoff = new ReconnectBackoff();

                if (backoff.ShouldSkip(nowMs))
                {
                    deferred.Add(client);
                    continue;
                }

                // 이미 연결된 상태면 접속을 시도할 필요 없이 풀로 돌려보낸다(격리 큐에 들어온 뒤 살아난 경우).
                if (client.IsConnected)
                {
                    _backoffMap.Remove(client);
                    PushConnection(client, Interlocked.Read(ref _generation));
                    continue;
                }

                retry.Add(client);
            }

            if (retry.Count > 0)
            {
                // 접속은 블로킹(수 초까지) 호출이라, 시작 전에 세대를 읽어 둔다 — 접속 도중 Clear()가 실행되는 극단적인 경우까지 방어한다.
                long reconnectGeneration = Interlocked.Read(ref _generation);

                // 순차로 시도하면 서버가 응답하지 않을 때 스윕 하나가 (끊긴 커넥션 수 x 접속 제한)만큼 걸려 그동안 대기 큐 만료 점검이 멈춘다.
                // Init()과 같이 동시에 맺는다.
                var results = new byte[retry.Count];
                ConnectClientsParallel(retry, results, host, port, dbIndex, userId, password, () => _stopping);

                long doneMs = TimeUtil.MonotonicNowMillis();
                for (int i = 0; i < retry.Count; ++i)
                {
                    RedisClient client = retry[i];

                    if (results[i] == ConnectOk)
                    {
                        _backoffMap.Remove(client);

                        // 대기 큐에 쌓여 있던 요청이 있으면 바로 처리되도록 반납 경로를 거친다.
                        PushConnection(client, reconnectGeneration);
                        continue;
                    }

                    if (results[i] == ConnectFailed)
                    {
                        // 지수 백오프 + 지터: 실패할수록 다음 시도까지 더 오래 기다려, 서버가 오래 죽어 있는 동안 무의미하게 두드리지 않는다.
                        _backoffMap[client].OnFail(doneMs, RedisConst.ReconnectBackoffJitterMs);
                    }

                    // 실패했거나 종료 요청으로 시도하지 못한 클라이언트는 격리 큐로 되돌린다.
                    deferred.Add(client);
                }

                // 접속 시도에 시간이 걸렸을 수 있으므로 대기 큐 만료를 한 번 더 점검한다.
                ExpirePendingCommands();
            }

            if (deferred.Count > 0)
            {
                lock (_lock)
                {
                    foreach (RedisClient client in deferred)
                        _broken.Enqueue(client);
                }
            }
        }
    }

    // 클라이언트들을 동시에 접속시킨다(Init()과 재연결 스윕 공용). 순차로 하면 Redis가 느리거나 응답이 없을 때 걸리는 시간이
    // (클라이언트 수 x 접속 제한)만큼 늘어난다. 워커 수는 MaxConnectWorkers로 상한을 둬 한꺼번에 소켓을 쏟지 않는다.
    private static void ConnectClientsParallel(IReadOnlyList<RedisClient> clients, byte[] results,
        string host, ushort port, int dbIndex, string userId, string password, Func<bool>? isStopping)
    {
        int count = clients.Count;
        int workers = Math.Min(count, RedisConst.MaxConnectWorkers);
        int next = -1;

        var tasks = new Task[workers];
        for (int w = 0; w < workers; ++w)
        {
            tasks[w] = Task.Run(async () =>
            {
                while (true)
                {
                    if (isStopping != null && isStopping())
                        break;

                    int i = Interlocked.Increment(ref next);
                    if (i >= count)
                        break;

                    bool ok;
                    try
                    {
                        ok = await clients[i].ConnectAsync(host, port, dbIndex, RedisConst.ConnectTimeoutMs, userId, password).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        LogManager.Error("RedisConnectionPool: 접속 중 예외 발생 - {0}", e.Message);
                        ok = false;
                    }

                    results[i] = ok ? ConnectOk : ConnectFailed;
                }
            });
        }

        Task.WaitAll(tasks);
    }
}
