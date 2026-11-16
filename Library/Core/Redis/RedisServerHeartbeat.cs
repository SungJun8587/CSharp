// RedisServerHeartbeat.cs : 서버 실행 상태를 Redis에 등록하고 주기적으로 TTL을 갱신하는 클래스.
using System;
using System.Collections.Generic;
using System.Threading;
using Core.Util;

namespace Core.Redis;

/// <summary>
/// 서버 시작 시 Redis에 자신의 실행 상태를 등록(HSET)하고, 주기적으로 TTL을 갱신(EXPIRE)해 "살아 있음"을 알린다.
///
/// <para>등록 키: <c>{serverName}:{serverGroupId}:{serverChannelId}</c> (Redis Hash).
/// 필드: serverName, serverGroupId, serverChannelId, port, pid, sessionCount, startedAt, updatedAt.</para>
///
/// <para><b>TTL 기반 자동 정리</b>: ttl(살아 있음으로 간주할 시간)을 걸어 두고 그보다 충분히 짧은 주기마다 갱신한다. 프로세스가 비정상 종료하면
/// 갱신이 끊기고 TTL 만료로 키가 자동 소멸한다 — 디스커버리/모니터링 쪽이 별도 정리 배치 없이도 "죽은 서버" 항목이 사라진다.</para>
///
/// <para><b>등록 정보 복원</b>: 매 주기 갱신은 updatedAt/sessionCount만 쓰지 않고 등록 필드 전체를 다시 쓴다. Redis 장애/재시작이나 TTL 만료로
/// 키가 사라졌다가 복구돼도 일부 필드만 가진 불완전한 항목이 아니라 완전한 등록 정보로 되살아난다.</para>
///
/// <para><b>원자성</b>: 등록(HSET)과 TTL(EXPIRE)은 스크립트 하나(<see cref="RedisScript"/>)에서 함께 실행된다. 둘 사이에 실패나 연결 끊김이 끼어
/// TTL 없는 키가 남을 수 없다.</para>
///
/// <para><b>정상 종료 시</b>: <see cref="Stop"/>이 갱신 스레드를 즉시 멈추고 DEL로 키를 바로 지워 TTL 만료를 기다리지 않고 목록에서 빠지게 한다.
/// DEL은 응답을 기다리지 않는다 — 서비스를 곧바로 해제하면 전송 전에 끊길 수 있고, 그 경우 TTL이 정리한다.</para>
///
/// <para><b>lifecycle</b>: Start()를 Stop() 없이 두 번 호출하면 두 번째는 false를 반환한다.</para>
/// </summary>
/// <example>
/// <code>
/// var heartbeat = new RedisServerHeartbeat(redis, "GameServer", "1", "1", 7777);
/// heartbeat.SetSessionCountProvider(() =&gt; sessionManager.Count);
/// heartbeat.Start(ttlSec: 15, heartbeatIntervalSec: 5);   // TTL 15초, 5초마다 갱신
/// // ...
/// heartbeat.Stop();                                        // 서버 종료 시
/// </code>
/// </example>
public sealed class RedisServerHeartbeat : IDisposable
{
    // 등록과 TTL 갱신을 한 번에 — HSET만 성공하고 EXPIRE가 빠져 키가 TTL 없이 남는 순간이 없다.
    // KEYS[1]=등록 키, ARGV[1]=TTL(초), ARGV[2..]=필드/값 쌍. EVALSHA로 전송된다.
    private static readonly RedisScript s_registerScript = new RedisScript(
        "redis.call('HSET', KEYS[1], unpack(ARGV, 2))\n" +
        "redis.call('EXPIRE', KEYS[1], ARGV[1])\n" +
        "return 1");

    private readonly IRedisCommandSender _sender;
    private readonly string _serverName;
    private readonly string _serverGroupId;
    private readonly string _serverChannelId;
    private readonly ushort _port;

    private int _ttlSec = 15;
    private int _heartbeatIntervalSec = 5;
    private long _startedAtMs;
    private bool _started;                      // Start()가 성공해 등록 키가 있는지 — Stop()이 DEL을 보낼지 판단 (Start/Stop 호출 스레드만 접근)
    private Func<int>? _sessionCountProvider;   // 설정 안 하면 sessionCount 필드는 항상 0

    private Thread? _thread;
    private readonly object _lock = new object();   // 대기 전용 (heartbeat 로직 자체엔 동기화 불필요)
    private int _stopping;                      // Stop() 중복 호출 방지 + 루프 탈출 조건

    public RedisServerHeartbeat(IRedisCommandSender sender, string serverName, string serverGroupId, string serverChannelId, ushort port)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _serverName = serverName;
        _serverGroupId = serverGroupId;
        _serverChannelId = serverChannelId;
        _port = port;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// 매 갱신(최초 등록 + 이후 매 주기)마다 호출해 "지금 이 서버의 세션 수(동접자수)"를 물어볼 콜백을 등록한다.
    /// 콜백은 heartbeat 전용 스레드에서 호출된다. <see cref="Start"/> 전에 등록해야 최초 등록부터 값이 반영된다.
    /// </summary>
    public void SetSessionCountProvider(Func<int>? provider) => _sessionCountProvider = provider;

    /// <summary>등록 키: <c>{serverName}:{serverGroupId}:{serverChannelId}</c>.</summary>
    public string Key => _serverName + ":" + _serverGroupId + ":" + _serverChannelId;

    /// <summary>Redis에 최초 등록한 뒤 주기적 갱신 스레드를 시작한다.</summary>
    /// <param name="ttlSec">살아 있음으로 간주할 TTL(초)</param>
    /// <param name="heartbeatIntervalSec">갱신 주기(초). ttlSec보다 작아야 한다 — 같거나 크면 갱신 전에 TTL이 만료되어
    /// 실제로는 살아 있는데 죽은 것처럼 보이는 창이 생긴다.</param>
    /// <returns>파라미터가 유효하고 시작에 성공하면 true. 이미 실행 중이면(Stop()을 안 부르고 재호출) false.</returns>
    public bool Start(int ttlSec = 15, int heartbeatIntervalSec = 5)
    {
        if (_thread != null)
        {
            LogManager.Error("RedisServerHeartbeat.Start: already running — Stop()을 먼저 호출해야 합니다");
            return false;
        }

        if (heartbeatIntervalSec <= 0 || ttlSec <= heartbeatIntervalSec)
        {
            LogManager.Error("RedisServerHeartbeat.Start: invalid interval. ttl({0}), interval({1})", ttlSec, heartbeatIntervalSec);
            return false;
        }

        _ttlSec = ttlSec;
        _heartbeatIntervalSec = heartbeatIntervalSec;
        Volatile.Write(ref _stopping, 0);
        _startedAtMs = TimeUtil.NowMillis();

        PublishRegistration();

        _thread = new Thread(HeartbeatLoop) { IsBackground = true, Name = "RedisServerHeartbeat" };
        _thread.Start();
        _started = true;
        return true;
    }

    /// <summary>갱신 스레드를 정지시키고 Redis에서 등록 키를 즉시 삭제한다. 중복 호출에 안전하며, Start()가 성공한 적이 없으면 DEL을 보내지 않는다.</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
            return;     // 이미 정지 중이거나 정지됨 — 중복 호출 안전

        lock (_lock)
            Monitor.PulseAll(_lock);

        _thread?.Join();
        _thread = null;

        // Start()가 성공한 적이 없으면 등록한 키가 없다 — 같은 이름의 다른 인스턴스 키를 지우지 않도록 건너뛴다.
        if (!_started)
            return;

        _started = false;

        // 정상 종료 시 등록 키를 즉시 지워 TTL 만료를 기다리지 않고 디스커버리 목록에서 바로 빠지게 한다.
        _sender.SendCommand(new[] { "DEL", Key }, null);
    }

    // 등록 스크립트의 ARGV(TTL + 전체 필드/값 쌍)를 만든다. 최초 등록과 매 주기 갱신이 같은 필드 집합을 쓰므로,
    // 키가 사라진 뒤 갱신이 키를 다시 만들더라도 항상 완전한 등록 정보가 된다.
    private string[] BuildRegistrationArgv(long nowMs)
    {
        var sessionCount = _sessionCountProvider;
        return new[]
        {
            _ttlSec.ToString(),
            "serverName", _serverName,
            "serverGroupId", _serverGroupId,
            "serverChannelId", _serverChannelId,
            "port", _port.ToString(),
            "pid", Environment.ProcessId.ToString(),
            "sessionCount", (sessionCount?.Invoke() ?? 0).ToString(),
            "startedAt", _startedAtMs.ToString(),
            "updatedAt", nowMs.ToString()
        };
    }

    // 등록 정보 전체 재기록(HSET) + TTL 갱신(EXPIRE)을 스크립트 하나로 게시한다(최초 등록/주기 갱신 공통).
    // 키가 TTL 만료나 Redis 재시작으로 사라졌더라도 이 스크립트가 완전한 등록 정보로 복원한다.
    private void PublishRegistration()
        => s_registerScript.Run(_sender, new[] { Key }, BuildRegistrationArgv(TimeUtil.NowMillis()));

    // 인터벌만큼 대기하되 Stop()이 알리면 대기를 즉시 끝내고 탈출한다(Sleep과 달리 정지 요청에 즉각 반응).
    private void HeartbeatLoop()
    {
        while (true)
        {
            lock (_lock)
            {
                if (Volatile.Read(ref _stopping) != 0)
                    return;

                Monitor.Wait(_lock, _heartbeatIntervalSec * 1000);

                if (Volatile.Read(ref _stopping) != 0)
                    return;
            }

            try
            {
                PublishRegistration();
            }
            catch (Exception e)
            {
                // 세션 수 콜백 등에서 예외가 나도 갱신 스레드는 계속 돈다.
                LogManager.Error("RedisServerHeartbeat: 갱신 중 예외 발생 - {0}", e.Message);
            }
        }
    }
}
