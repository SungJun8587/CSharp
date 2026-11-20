// RedisService.cs : 외부 모듈에 노출되는 최상위 Redis 서비스 파사드.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Net;
using Core.Util;

namespace Core.Redis;

/// <summary>
/// 노드별 커넥션 풀과 구독 연결을 묶은 서비스. 소켓 스레드에서 수신한 응답/메시지를 지정한 <see cref="JobQueue"/>로 이관해 주므로
/// 로직 스레드에서 별도 동기화 없이 안전하게 결과를 받을 수 있다.
///
/// <para><b>노드</b>: 노드 하나당 커넥션 풀을 하나씩 두고 <see cref="RedisNode.Id"/>를 키로 관리한다. 노드가 하나뿐인 목록으로 초기화해도
/// 동일하며, 노드를 지정하지 않은 호출은 항상 목록의 첫 번째 노드로 간다. 풀 테이블은 불변 스냅샷을 통째로 교체하므로 조회에 락이 없다.</para>
///
/// <para><b>lifecycle 계약</b>: Init()은 서버 시작(또는 재시작) 시, 트래픽이 흐르기 전에 호출한다. 재호출하면 이전 풀과 구독 연결을
/// 닫고 새 노드 목록으로 바꾼다(교체 자체는 한 번에 일어나지만, 이전 풀에서 처리 중이던 요청은 연결 오류로 끝난다).</para>
///
/// <para><b>구독</b>: 노드마다 구독 전용 연결이 하나 있고 그 노드에 대한 첫 구독 호출 때 만들어진다(Init()에서는 만들지 않는다).
/// 연결이 끊기면 자동으로 다시 접속해 등록된 구독을 복구하지만, 단절 중에 발행된 메시지는 받을 수 없다. 자세한 계약은 <see cref="RedisSubscriber"/>를 본다.</para>
/// </summary>
/// <example>
/// <code>
/// var jobQueue = new JobQueue();
/// using var redis = new RedisService(jobQueue);
/// redis.Init(new[] { new RedisNode(1, "127.0.0.1", 6379, dbIndex: 0) }, poolSize: 5);
/// redis.SendCommand(new[] { "HGETALL", "User:1" }, res => { /* jobQueue를 처리하는 스레드 */ });      // 첫 번째 노드
/// redis.SendCommand(nodeId: 1, new[] { "HGETALL", "User:1" }, res => { });                          // 지정한 노드
/// RedisValue res = await redis.SendCommandAsync(new[] { "GET", "key" });                           // await 방식
/// </code>
/// </example>
public sealed class RedisService : IRedisCommandSender, IDisposable
{
    private readonly JobQueue _jobQueue;

    // 노드 ID → 커넥션 풀. 불변 스냅샷을 통째로 교체한다(조회는 락 없이 읽는다).
    private Dictionary<short, RedisConnectionPool> _poolMap = new Dictionary<short, RedisConnectionPool>();
    private int _defaultNodeId;                                         // Interlocked/Volatile로만 접근

    // 구독 연결은 노드별로 첫 Subscribe() 호출 때 만들어진다(드물게 쓰이는 경로라 단일 락으로 충분).
    private readonly object _subscriberLock = new object();             // _nodes/_subscriberMap 보호
    private List<RedisNode> _nodes = new List<RedisNode>();             // Init()에 넘겨받은 노드 목록(구독 연결 생성용)
    private Dictionary<short, RedisSubscriber> _subscriberMap = new Dictionary<short, RedisSubscriber>();

    /// <param name="jobQueue">응답/메시지 콜백을 이관해 실행할 JobQueue</param>
    public RedisService(JobQueue jobQueue)
    {
        _jobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
    }

    public void Dispose()
    {
        ShutdownSubscribers();

        Dictionary<short, RedisConnectionPool> pools = Interlocked.Exchange(ref _poolMap, new Dictionary<short, RedisConnectionPool>());
        foreach (RedisConnectionPool pool in pools.Values)
            pool.Dispose();
    }

    /// <summary>노드를 지정하지 않았을 때 쓰이는 기본 노드 ID(Init()에 넘긴 목록의 첫 번째 노드).</summary>
    public short DefaultNodeId => (short)Volatile.Read(ref _defaultNodeId);

    // ===================================================================================
    // 초기화
    // ===================================================================================

    /// <summary>
    /// 노드 목록으로 노드별 커넥션 풀을 각각 초기화한다. 풀을 전부 만들어 본 뒤(하나라도 연결에 실패하면 그때까지 만든 풀을 정리하고
    /// 즉시 실패 — 일부 노드만 연결된 애매한 상태로 남기지 않는다), 전부 성공한 경우에만 노드 테이블에 반영한다.
    /// 실제 연결을 시도하기 전에 ID 중복부터 검사해 설정 오류를 먼저 걸러낸다.
    /// </summary>
    /// <param name="nodes">노드 목록(비어 있으면 실패). 서버 1대만 쓰려면 노드 하나짜리 목록을 넘긴다.</param>
    /// <param name="poolSize">노드별로 만들 커넥션 수(모든 노드에 동일하게 적용, 0 이하면 실패)</param>
    /// <returns>true: 모든 노드 성공. false: 빈 목록/잘못된 인자/중복 ID/하나라도 연결 실패</returns>
    public bool Init(IReadOnlyList<RedisNode> nodes, int poolSize)
    {
        if (nodes == null || nodes.Count == 0)
        {
            LogManager.Error("RedisService.Init: redis node list is empty");
            return false;
        }

        if (poolSize <= 0)
        {
            LogManager.Error("RedisService.Init: invalid pool size({0})", poolSize);
            return false;
        }

        // 실제 Redis 연결(블로킹, 노드당 최대 수 초)을 만들기 전에 설정 오류부터 걸러낸다. 노드는 보통 한 자릿수라 O(N^2)로 충분하다.
        for (int i = 0; i < nodes.Count; ++i)
        {
            for (int j = i + 1; j < nodes.Count; ++j)
            {
                if (nodes[i].Id == nodes[j].Id)
                {
                    LogManager.Error("RedisService.Init: duplicate node ID({0})", nodes[i].Id);
                    return false;
                }
            }
        }

        var newPools = new Dictionary<short, RedisConnectionPool>(nodes.Count);
        foreach (RedisNode node in nodes)
        {
            var pool = new RedisConnectionPool();
            if (!pool.Init(node.Host, node.Port, node.DbIndex, poolSize, RedisConst.DefaultReconnectIntervalSec, node.UserId, node.Password))
            {
                // 실패 원인을 바로 알 수 있도록 노드 정보를 남긴다. 기존 상태는 그대로 유지된다.
                LogManager.Error("RedisService.Init: failed to initialize Redis pool. {0}", node);
                pool.Dispose();
                foreach (RedisConnectionPool created in newPools.Values)
                    created.Dispose();

                return false;
            }

            newPools.Add(node.Id, pool);
        }

        // 전부 연결 성공 — 이제 테이블을 교체한다. 이전 Init()에서 만든 구독 연결은 노드 정보가 바뀔 수 있으므로 닫고,
        // 구독 연결은 첫 Subscribe() 호출 때 새로 만들어진다.
        ShutdownSubscribers();
        lock (_subscriberLock)
            _nodes = new List<RedisNode>(nodes);

        Dictionary<short, RedisConnectionPool> oldPools = Interlocked.Exchange(ref _poolMap, newPools);
        Volatile.Write(ref _defaultNodeId, nodes[0].Id);

        foreach (RedisConnectionPool old in oldPools.Values)
            old.Dispose();

        return true;
    }

    // ===================================================================================
    // 명령
    // ===================================================================================

    /// <summary>기본 노드로 Redis 명령을 비동기 실행한다. 콜백은 생성자에 넘긴 JobQueue를 처리하는 스레드에서 호출된다.</summary>
    /// <returns>전송 접수 여부(false: 초기화되지 않음, 풀 포화 등 — 이때 콜백은 호출되지 않는다)</returns>
    public bool SendCommand(IReadOnlyList<string> args, RedisCallback? callback) => SendCommand(DefaultNodeId, args, callback);

    /// <summary>지정한 노드로 Redis 명령을 비동기 실행한다.</summary>
    /// <returns>전송 접수 여부(false: 해당 노드가 없는 경우 포함)</returns>
    public bool SendCommand(short nodeId, IReadOnlyList<string> args, RedisCallback? callback)
    {
        RedisConnectionPool? pool = FindPool(nodeId);
        if (pool == null)
            return false;

        // 풀 콜백은 소켓 수신 스레드에서 불리므로 JobQueue로 넘겨 사용자 콜백을 로직 스레드에서 실행한다.
        RedisCallback? poolCallback = null;
        if (callback != null)
        {
            JobQueue jobQueue = _jobQueue;
            poolCallback = res => jobQueue.Push(() => callback(res));
        }

        return pool.SendCommand(args, poolCallback);
    }

    /// <summary>기본 노드의 채널로 메시지를 발행한다(PUBLISH). 응답은 메시지를 받은 구독자 수(Integer)다. 발행은 풀로 나가며 구독 연결과 무관하다.</summary>
    public bool Publish(string channel, string message, RedisCallback? callback = null) => Publish(DefaultNodeId, channel, message, callback);

    /// <summary>지정한 노드의 채널로 메시지를 발행한다.</summary>
    public bool Publish(short nodeId, string channel, string message, RedisCallback? callback = null)
    {
        if (string.IsNullOrEmpty(channel))
            return false;

        return SendCommand(nodeId, new[] { "PUBLISH", channel, message }, callback);
    }

    // ===================================================================================
    // 구독
    // ===================================================================================

    /// <summary>채널을 구독한다(기본 노드). 메시지 콜백은 생성자에 넘긴 JobQueue를 처리하는 스레드에서 호출된다.</summary>
    /// <returns>등록 성공 여부(false: 노드 없음, 빈 채널, null 콜백, 구독 연결 생성 실패)</returns>
    public bool Subscribe(string channel, RedisMessageCallback callback) => Subscribe(DefaultNodeId, channel, callback);

    /// <summary>채널을 구독한다(지정 노드). 노드의 구독 연결이 없으면 만든다.</summary>
    public bool Subscribe(short nodeId, string channel, RedisMessageCallback callback)
        => FindSubscriber(nodeId, true)?.Subscribe(channel, callback) ?? false;

    /// <summary>채널을 구독한다(지정 노드). 페이로드를 바이트 그대로 받는다(바이너리 메시지용).</summary>
    public bool SubscribeBinary(short nodeId, string channel, RedisBinaryMessageCallback callback)
        => FindSubscriber(nodeId, true)?.SubscribeBinary(channel, callback) ?? false;

    /// <summary>채널을 구독한다(기본 노드). 페이로드를 바이트 그대로 받는다.</summary>
    public bool SubscribeBinary(string channel, RedisBinaryMessageCallback callback) => SubscribeBinary(DefaultNodeId, channel, callback);

    /// <summary>글롭 패턴으로 구독한다(지정 노드). 페이로드를 바이트 그대로 받는다.</summary>
    public bool PSubscribeBinary(short nodeId, string pattern, RedisBinaryMessageCallback callback)
        => FindSubscriber(nodeId, true)?.PSubscribeBinary(pattern, callback) ?? false;

    /// <summary>채널 구독을 해제한다(기본 노드).</summary>
    /// <returns>해제할 구독이 있었는지 여부</returns>
    public bool Unsubscribe(string channel) => Unsubscribe(DefaultNodeId, channel);

    /// <summary>채널 구독을 해제한다(지정 노드). 구독 연결이 없으면 해제할 것도 없으므로 false.</summary>
    public bool Unsubscribe(short nodeId, string channel)
        => FindSubscriber(nodeId, false)?.Unsubscribe(channel) ?? false;

    /// <summary>글롭 패턴(예: "Chat:*")으로 구독한다(기본 노드). 콜백에는 패턴이 아니라 메시지가 발행된 실제 채널 이름이 전달된다.</summary>
    public bool PSubscribe(string pattern, RedisMessageCallback callback) => PSubscribe(DefaultNodeId, pattern, callback);

    /// <summary>글롭 패턴으로 구독한다(지정 노드).</summary>
    public bool PSubscribe(short nodeId, string pattern, RedisMessageCallback callback)
        => FindSubscriber(nodeId, true)?.PSubscribe(pattern, callback) ?? false;

    /// <summary>패턴 구독을 해제한다(기본 노드).</summary>
    public bool PUnsubscribe(string pattern) => PUnsubscribe(DefaultNodeId, pattern);

    /// <summary>패턴 구독을 해제한다(지정 노드).</summary>
    public bool PUnsubscribe(short nodeId, string pattern)
        => FindSubscriber(nodeId, false)?.PUnsubscribe(pattern) ?? false;

    /// <summary>
    /// 모든 노드의 구독 연결을 닫고 구독 목록을 비운다. 서버 종료 시 JobQueue 소비를 멈추기 전에 호출하면 닫힌 뒤에는 메시지 콜백이
    /// 더 쌓이지 않는다. <see cref="Dispose"/>에서도 호출된다.
    /// </summary>
    public void ShutdownSubscribers()
    {
        Dictionary<short, RedisSubscriber> subscribers;
        lock (_subscriberLock)
        {
            subscribers = _subscriberMap;
            _subscriberMap = new Dictionary<short, RedisSubscriber>();
        }

        // Shutdown()은 재연결 스레드를 join하므로 락 밖에서 호출한다.
        foreach (RedisSubscriber subscriber in subscribers.Values)
            subscriber.Shutdown();
    }

    // 노드의 구독 연결을 찾는다. create이면 없을 때 Init()에 보관한 노드 정보로 만든다. 연결 생성(접속)은 블로킹을 포함하므로
    // 락을 잡은 채 하지 않는다 — 동시에 두 스레드가 만들면 먼저 맵에 넣은 쪽이 이기고 나머지는 닫는다.
    private RedisSubscriber? FindSubscriber(short nodeId, bool create)
    {
        RedisNode? node = null;
        lock (_subscriberLock)
        {
            if (_subscriberMap.TryGetValue(nodeId, out RedisSubscriber? existing))
                return existing;

            if (!create)
                return null;

            foreach (RedisNode item in _nodes)
            {
                if (item.Id == nodeId)
                {
                    node = item;
                    break;
                }
            }
        }

        if (node == null)
            return null;

        var created = new RedisSubscriber(_jobQueue);
        if (!created.Init(node.Host, node.Port, RedisConst.SubscriberReconnectIntervalSec, node.UserId, node.Password))
        {
            LogManager.Error("RedisService.FindSubscriber: failed to initialize subscriber. {0}", node);
            return null;
        }

        RedisSubscriber winner;
        lock (_subscriberLock)
        {
            if (!_subscriberMap.TryGetValue(nodeId, out RedisSubscriber? other))
            {
                _subscriberMap[nodeId] = created;
                return created;
            }

            winner = other;
        }

        // 다른 스레드가 먼저 만들었다 — 이쪽은 락 밖에서 닫고 그쪽을 쓴다.
        created.Shutdown();
        return winner;
    }

    private RedisConnectionPool? FindPool(short nodeId)
    {
        Dictionary<short, RedisConnectionPool> snapshot = Volatile.Read(ref _poolMap);
        return snapshot.TryGetValue(nodeId, out RedisConnectionPool? pool) ? pool : null;
    }
}

/// <summary><see cref="IRedisCommandSender"/>에 <c>await</c> 방식 전송을 더하는 확장.</summary>
public static class RedisCommandSenderExtensions
{
    /// <summary>
    /// 기본 노드로 명령을 보내고 응답을 기다린다. 접수가 거부되면(풀 없음/대기 큐 포화) <see cref="RedisConst.ErrSendRejected"/> 에러 값으로 완료된다.
    /// 서버 에러와 연결 오류도 예외가 아니라 <see cref="RedisType.Error"/> 값으로 돌려준다(콜백 방식과 동일).
    /// </summary>
    public static Task<RedisValue> SendCommandAsync(this IRedisCommandSender sender, IReadOnlyList<string> args)
        => sender.SendCommandAsync(sender.DefaultNodeId, args);

    /// <summary>지정한 노드로 명령을 보내고 응답을 기다린다.</summary>
    public static Task<RedisValue> SendCommandAsync(this IRedisCommandSender sender, short nodeId, IReadOnlyList<string> args)
    {
        var tcs = new TaskCompletionSource<RedisValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!sender.SendCommand(nodeId, args, res => tcs.TrySetResult(res)))
            tcs.TrySetResult(RedisHelper.MakeError(RedisConst.ErrSendRejected));

        return tcs.Task;
    }
}
