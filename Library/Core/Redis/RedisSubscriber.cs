// RedisSubscriber.cs : Redis Pub/Sub 구독 전용 연결.
using System;
using System.Collections.Generic;
using System.Threading;
using Core.Net;
using Core.Util;

namespace Core.Redis;

/// <summary>
/// 구독 전용 연결 하나를 소유하고 관리한다. Redis는 구독 상태가 된 연결에서 일반 명령을 받지 않고 서버가 메시지를 자발적으로
/// 밀어 보내므로, 요청 하나에 응답 하나가 대응하는 <see cref="RedisConnectionPool"/>의 연결과 섞어 쓸 수 없다.
///
/// <para><b>스레드 계약</b>: 메시지 콜백은 생성자에 넘긴 <see cref="JobQueue"/>로 이관되어 호출된다(수신 스레드에서 직접 호출되지 않음).
/// 같은 연결에서 온 메시지는 수신 순서대로 쌓인다. Subscribe()/Unsubscribe()는 어느 스레드에서나 호출할 수 있다.</para>
///
/// <para><b>명령 직렬화</b>: 구독 연결은 미완료 명령을 하나만 가질 수 있다는 <see cref="RedisClient"/> 계약을 따른다.
/// SUBSCRIBE/UNSUBSCRIBE 등은 내부 큐에 쌓였다가 앞 명령의 확인 응답이 온 뒤에 하나씩 전송된다. 명령 하나에 채널 하나만
/// 담으므로 확인 응답이 명령과 1:1로 대응한다.</para>
///
/// <para><b>재연결과 재구독</b>: 연결이 끊기면 재연결 스레드가 지수 백오프로 다시 접속하고, 성공하면 등록된 모든 채널·패턴을 다시
/// 구독한다. 구독 목록은 이 클래스가 보관하므로 호출자가 다시 등록할 필요가 없다. 수신 쪽 TCP 연결이 조용히 죽는 경우(중간 장비가
/// RST 없이 연결을 버림)를 잡기 위해 유휴 시 주기적으로 PING을 보내고, 응답이 <see cref="RedisConst.SubscriberCommandTimeoutMs"/> 안에
/// 오지 않으면 연결을 끊어 재연결 경로로 넘긴다. <b>단절 중에 발행된 메시지는 받을 수 없다</b>(Pub/Sub 특성) — 유실되면 안 되는
/// 데이터는 Pub/Sub을 "알림"으로만 쓰고 원본은 DB나 Redis 자료구조에서 다시 읽도록 설계한다.</para>
/// </summary>
/// <example>
/// <code>
/// using var sub = new RedisSubscriber(jobQueue);
/// sub.Init("127.0.0.1", 16379);
/// sub.Subscribe("Chat:1:2", (channel, payload) => { /* jobQueue를 처리하는 스레드에서 호출됨 */ });
/// </code>
/// </example>
public sealed class RedisSubscriber : IDisposable
{
    private readonly JobQueue _jobQueue;                                // 메시지 콜백을 이관할 JobQueue

    private readonly object _lock = new object();                       // 아래 상태 전체를 보호(클라이언트 호출은 락 밖에서)
    private RedisClient? _client;                                       // 구독 전용 연결(Init()에서 생성)
    private bool _initialized;
    private ulong _epoch;                                               // 연결(재연결)마다 증가 — 이전 연결의 지연된 확인 응답을 무시하기 위함
    private readonly Dictionary<string, RedisMessageCallback> _channelMap = new Dictionary<string, RedisMessageCallback>();    // 구독 중인 채널 → 콜백
    private readonly Dictionary<string, RedisMessageCallback> _patternMap = new Dictionary<string, RedisMessageCallback>();    // 구독 중인 패턴 → 콜백
    private readonly Queue<string[]> _cmdQueue = new Queue<string[]>(); // 전송 대기 중인 구독 명령
    private bool _cmdInFlight;                                          // 확인 응답을 기다리는 명령이 있는지

    private readonly object _sendLock = new object();                   // PumpCommands()의 "게이트 확인 → SendCommand 호출" 구간 직렬화
    private bool _pumping;                                              // PumpCommands()가 한 스레드에서 실행 중인지(재진입 방지)

    // 재연결 스레드가 접속을 시작해서 RebuildSubscriptions()가 끝날 때까지 true. 이 동안 PumpCommands()는 명령을 보내지 않는다 —
    // 접속 직후의 AUTH 응답 대기나 재구독 명령과 같은 연결에 명령이 겹치면 "미완료 명령 하나" 계약 위반으로 구독 명령이 유실되기 때문이다.
    // 쌓인 명령은 재구독이 대신한다.
    private bool _resyncing;

    private long _cmdSentMs;                                            // 진행 중인 명령을 보낸 시각(단조 시계 ms, 응답 무응답 감지용)
    private long _lastPingMs;                                           // 마지막으로 PING을 큐에 넣은 시각

    private string _host = string.Empty;
    private ushort _port;
    private string _userId = string.Empty;
    private string _password = string.Empty;
    private int _reconnectIntervalSec = RedisConst.SubscriberReconnectIntervalSec;

    private Thread? _reconnectThread;
    private readonly object _reconnectLock = new object();              // 재연결 주기 대기 전용
    private volatile bool _stopping;

    /// <param name="jobQueue">메시지 콜백을 이관해 실행할 JobQueue</param>
    public RedisSubscriber(JobQueue jobQueue)
    {
        _jobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
    }

    public void Dispose() => Shutdown();

    /// <summary>
    /// 구독 연결을 만들고 재연결 스레드를 시작한다. 첫 접속이 실패해도 true를 반환하고 재연결 스레드가 백오프로 계속 시도한다 —
    /// 서버 기동 순서와 무관하게 Subscribe()를 미리 등록해 둘 수 있고, 접속되는 순간 등록된 구독이 모두 서버에 반영된다.
    /// Pub/Sub은 논리 DB와 무관하게 서버 전체에서 공유되므로 DB 인덱스를 받지 않는다.
    /// </summary>
    /// <param name="reconnectIntervalSec">재연결·생존 점검 스레드가 깨어나는 주기(초, 0 이하면 기본값)</param>
    /// <param name="userId">AUTH 사용자(Redis 6 ACL). 비어 있고 password만 있으면 <c>AUTH &lt;password&gt;</c></param>
    /// <param name="password">AUTH 비밀번호. 둘 다 비어 있으면 AUTH를 보내지 않는다.</param>
    /// <returns>false: 이미 초기화됨 또는 잘못된 인자</returns>
    public bool Init(string host, ushort port, int reconnectIntervalSec = RedisConst.SubscriberReconnectIntervalSec,
        string userId = "", string password = "")
    {
        if (string.IsNullOrEmpty(host) || port == 0)
        {
            LogManager.Error("RedisSubscriber.Init: invalid argument");
            return false;
        }

        lock (_lock)
        {
            if (_initialized)
            {
                LogManager.Error("RedisSubscriber.Init: already initialized");
                return false;
            }
        }

        var client = new RedisClient();
        client.SetPushCallback(OnPush);

        // 첫 접속 실패는 치명적이지 않다 — 재연결 스레드가 백오프로 계속 시도하고, 접속되면 등록된 구독을 반영한다.
        if (!client.Connect(host, port, 0, RedisConst.ConnectTimeoutMs, userId, password))
            LogManager.Error("RedisSubscriber.Init: initial connect failed, will keep retrying. host({0}), port({1})", host, port);

        lock (_lock)
        {
            if (_initialized)
            {
                // 동시에 Init()이 호출된 경우 먼저 들어온 쪽이 이긴다.
                client.Disconnect();
                return false;
            }

            _client = client;
            _host = host;
            _port = port;
            _userId = userId ?? string.Empty;
            _password = password ?? string.Empty;
            _reconnectIntervalSec = reconnectIntervalSec > 0 ? reconnectIntervalSec : RedisConst.SubscriberReconnectIntervalSec;
            _epoch++;
            _cmdQueue.Clear();
            _cmdInFlight = false;
            _resyncing = false;
            _initialized = true;
        }

        _stopping = false;
        _reconnectThread = new Thread(ReconnectLoop) { IsBackground = true, Name = "RedisSubscriberReconnect" };
        _reconnectThread.Start();
        return true;
    }

    /// <summary>
    /// 재연결 스레드를 멈추고 연결을 닫는다. 먼저 초기화 플래그를 내려 새 Subscribe()를 막고(이 호출이 종료 권한을 갖는다),
    /// 재연결 스레드를 join한 뒤에 연결을 끊고 구독 목록을 비운다. 호출 후에는 Init()으로 다시 시작할 수 있다.
    /// </summary>
    public void Shutdown()
    {
        lock (_lock)
        {
            if (!_initialized)
                return;

            _initialized = false;
        }

        _stopping = true;
        lock (_reconnectLock)
            Monitor.PulseAll(_reconnectLock);

        _reconnectThread?.Join();
        _reconnectThread = null;

        RedisClient? client;
        lock (_lock)
        {
            client = _client;
            _client = null;
            _channelMap.Clear();
            _patternMap.Clear();
            _cmdQueue.Clear();
            _cmdInFlight = false;
            _resyncing = false;
            _epoch++;
        }

        // 대기 중이던 명령 콜백이 에러로 호출되지만, 에폭이 바뀌었으므로 무시된다.
        client?.Disconnect();
    }

    /// <summary>
    /// 채널을 구독하고 메시지 콜백을 등록한다. 같은 채널을 다시 구독하면 콜백이 교체된다. 연결이 끊긴 상태에서 호출해도
    /// true를 반환하며, 구독 목록에 기록되어 재연결 후 서버에 반영된다 — true는 "등록됨"이지 "서버가 확인함"이 아니다.
    /// </summary>
    /// <returns>false: 초기화 전, 빈 채널, null 콜백</returns>
    public bool Subscribe(string channel, RedisMessageCallback callback) => AddSubscription(_channelMap, "SUBSCRIBE", channel, callback);

    /// <summary>채널 구독을 해제한다.</summary>
    /// <returns>해제할 구독이 있었는지 여부(false: 등록되지 않은 채널)</returns>
    public bool Unsubscribe(string channel) => RemoveSubscription(_channelMap, "UNSUBSCRIBE", channel);

    /// <summary>
    /// 글롭 패턴(예: "Chat:*")으로 채널을 구독하고 메시지 콜백을 등록한다. 콜백의 channel에는 패턴이 아니라
    /// 메시지가 발행된 실제 채널 이름이 온다. 그 외 규칙은 <see cref="Subscribe"/>와 같다.
    /// </summary>
    public bool PSubscribe(string pattern, RedisMessageCallback callback) => AddSubscription(_patternMap, "PSUBSCRIBE", pattern, callback);

    /// <summary>패턴 구독을 해제한다.</summary>
    public bool PUnsubscribe(string pattern) => RemoveSubscription(_patternMap, "PUNSUBSCRIBE", pattern);

    /// <summary>현재 구독 연결이 살아 있는지 확인한다.</summary>
    public bool IsConnected
    {
        get
        {
            lock (_lock)
                return _initialized && _client != null && _client.IsConnected;
        }
    }

    private bool AddSubscription(Dictionary<string, RedisMessageCallback> map, string command, string name, RedisMessageCallback callback)
    {
        if (string.IsNullOrEmpty(name) || callback == null)
            return false;

        lock (_lock)
        {
            if (!_initialized)
                return false;

            map[name] = callback;
            _cmdQueue.Enqueue(new[] { command, name });
        }

        PumpCommands();
        return true;
    }

    private bool RemoveSubscription(Dictionary<string, RedisMessageCallback> map, string command, string name)
    {
        lock (_lock)
        {
            if (!_initialized)
                return false;

            if (!map.Remove(name))
                return false;

            _cmdQueue.Enqueue(new[] { command, name });
        }

        PumpCommands();
        return true;
    }

    // 서버가 보낸 message/pmessage를 구독 목록에서 찾아 콜백을 JobQueue로 이관한다. 수신 스레드에서 호출된다.
    // 형식이 맞지 않거나 이미 해제된 구독이면 버린다.
    private void OnPush(RedisValue value)
    {
        if (value.Type != RedisType.Array || value.ArrayVal.Length == 0)
            return;

        string kind = value.ArrayVal[0].StrVal;
        string channel;
        string payload;
        RedisMessageCallback? callback;

        if (kind == "message" && value.ArrayVal.Length >= 3)
        {
            channel = value.ArrayVal[1].StrVal;
            payload = value.ArrayVal[2].StrVal;

            lock (_lock)
                _channelMap.TryGetValue(channel, out callback);
        }
        else if (kind == "pmessage" && value.ArrayVal.Length >= 4)
        {
            string pattern = value.ArrayVal[1].StrVal;
            channel = value.ArrayVal[2].StrVal;
            payload = value.ArrayVal[3].StrVal;

            lock (_lock)
                _patternMap.TryGetValue(pattern, out callback);
        }
        else
        {
            return;
        }

        if (callback == null)
            return;

        _jobQueue.Push(() => callback(channel, payload));
    }

    // 구독 명령의 확인 응답(또는 에러/연결 끊김)을 처리하고 다음 명령을 보낸다.
    // epoch는 이 명령을 보낸 시점의 연결 에폭이다. 재연결 뒤에 도착한 이전 연결의 응답은 무시한다.
    private void OnCommandReply(ulong epoch, RedisValue value)
    {
        lock (_lock)
        {
            if (epoch != _epoch)
                return;

            _cmdInFlight = false;
        }

        // 연결이 끊겨서 온 에러는 재연결 스레드가 다루므로 로그를 남기지 않는다.
        if (value.Type == RedisType.Error && IsConnected)
            LogManager.Error("RedisSubscriber: command error reply - {0}", value.StrVal);

        PumpCommands();
    }

    // 대기 큐의 명령을 하나씩 전송한다. 한 시점에 미완료 명령은 하나뿐이다. 전송 실패 시 RedisClient.SendCommand()가 콜백을
    // 동기로 호출하고 그 안에서 이 함수가 다시 불리므로, _pumping으로 재진입을 막고 바깥 루프가 큐를 소진하게 한다
    // (재귀 깊이가 큐 길이에 비례하지 않게).
    private void PumpCommands()
    {
        lock (_lock)
        {
            if (_pumping)
                return;

            _pumping = true;
        }

        try
        {
            while (true)
            {
                // "게이트 확인 → 명령 꺼내기 → SendCommand 호출"을 _sendLock으로 묶는다. 재연결 스레드가 _resyncing을 켠 뒤 이 락을
                // 한 번 거치므로, 게이트를 이미 통과해 전송 중인 명령은 접속이 시작되기 전에 끝난다. SendCommand()가 콜백을 동기 호출해도
                // 콜백(OnCommandReply → PumpCommands)은 _pumping 때문에 곧바로 돌아온다.
                lock (_sendLock)
                {
                    string[] args;
                    RedisClient client;
                    ulong epoch;

                    lock (_lock)
                    {
                        if (!_initialized || _resyncing || _cmdInFlight || _cmdQueue.Count == 0 || _client == null)
                        {
                            _pumping = false;
                            return;
                        }

                        args = _cmdQueue.Dequeue();
                        _cmdInFlight = true;
                        _cmdSentMs = TimeUtil.MonotonicNowMillis();
                        client = _client;
                        epoch = _epoch;
                    }

                    client.SendCommand(args, res => OnCommandReply(epoch, res));
                }
            }
        }
        catch (Exception)
        {
            lock (_lock)
                _pumping = false;

            throw;
        }
    }

    // 재연결에 성공한 뒤 등록된 모든 채널·패턴을 다시 구독한다. 새 에폭을 시작하고(이전 연결의 지연 응답 무시),
    // 전송 대기 큐를 구독 목록 전체로 다시 채운다. 이전 연결 시절의 대기 명령은 전부 버린다.
    private void RebuildSubscriptions()
    {
        lock (_lock)
        {
            _epoch++;
            _cmdInFlight = false;
            _resyncing = false;
            _cmdQueue.Clear();

            foreach (string channel in _channelMap.Keys)
                _cmdQueue.Enqueue(new[] { "SUBSCRIBE", channel });

            foreach (string pattern in _patternMap.Keys)
                _cmdQueue.Enqueue(new[] { "PSUBSCRIBE", pattern });
        }

        PumpCommands();
    }

    // 연결이 살아 있는 것으로 보일 때 실제로 응답하는지 점검한다. 진행 중인 명령이 SubscriberCommandTimeoutMs를 넘기도록 응답이 없으면
    // 연결이 조용히 죽은 것으로 보고 끊는다 — 끊으면 대기 콜백이 에러로 끝나고 다음 점검에서 재연결 경로로 넘어간다.
    // 명령이 없고 대기 큐도 비었으면 SubscriberPingIntervalMs마다 PING을 큐에 넣는다. PING도 다른 구독 명령과 같은 큐로 직렬화되므로
    // 한 연결에 미완료 명령이 둘이 되지 않는다.
    private void CheckLiveness(RedisClient client)
    {
        long nowMs = TimeUtil.MonotonicNowMillis();
        bool timedOut = false;
        bool ping = false;

        lock (_lock)
        {
            if (!_initialized)
                return;

            if (_cmdInFlight)
            {
                timedOut = nowMs - _cmdSentMs > RedisConst.SubscriberCommandTimeoutMs;
            }
            else if (_cmdQueue.Count == 0 && !_resyncing && nowMs - _lastPingMs >= RedisConst.SubscriberPingIntervalMs)
            {
                _cmdQueue.Enqueue(new[] { "PING" });
                _lastPingMs = nowMs;
                ping = true;
            }
        }

        if (timedOut)
        {
            LogManager.Error("RedisSubscriber: 명령 무응답 {0}ms 초과 — 연결을 끊고 재연결합니다", RedisConst.SubscriberCommandTimeoutMs);
            client.Disconnect();
        }
        else if (ping)
        {
            PumpCommands();
        }
    }

    // 연결 상태를 주기적으로 점검하고, 끊겨 있으면 백오프를 두고 다시 접속한다.
    private void ReconnectLoop()
    {
        string host, userId, password;
        ushort port;
        int intervalSec;
        lock (_lock)
        {
            host = _host;
            port = _port;
            intervalSec = _reconnectIntervalSec;
            userId = _userId;
            password = _password;
        }

        var backoff = new ReconnectBackoff();

        while (!_stopping)
        {
            lock (_reconnectLock)
            {
                if (!_stopping)
                    Monitor.Wait(_reconnectLock, intervalSec * 1000);

                if (_stopping)
                    break;
            }

            RedisClient? client;
            lock (_lock)
                client = _client;

            if (client == null)
                continue;

            if (client.IsConnected)
            {
                backoff.Reset();
                CheckLiveness(client);
                continue;
            }

            if (backoff.ShouldSkip(TimeUtil.MonotonicNowMillis()))
                continue;

            // 접속하는 동안(AUTH 응답 대기 포함) PumpCommands()가 같은 연결에 명령을 겹쳐 보내지 못하게 막는다.
            lock (_lock)
            {
                if (!_initialized)
                    break;

                _resyncing = true;
            }

            // 게이트를 이미 통과해 SendCommand()를 호출 중인 PumpCommands()가 있으면 끝날 때까지 기다린다.
            // 이후의 PumpCommands()는 _resyncing 때문에 명령을 보내지 않는다.
            lock (_sendLock)
            {
            }

            if (client.Connect(host, port, 0, RedisConst.ConnectTimeoutMs, userId, password))
            {
                LogManager.Info("RedisSubscriber: reconnected. host({0}), port({1})", host, port);
                backoff.Reset();
                RebuildSubscriptions();     // _resyncing 해제 + 등록된 구독 전체 재전송
            }
            else
            {
                lock (_lock)
                    _resyncing = false;

                backoff.OnFail(TimeUtil.MonotonicNowMillis());
            }
        }
    }
}
