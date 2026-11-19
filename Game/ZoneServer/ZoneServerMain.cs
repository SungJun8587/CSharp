// ZoneServerMain.cs : 존 서버 본체. 전투 존을 띄우고 Gateway에 접속해 등록/하트비트/입장/중계를 처리한다.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Core.Net;
using Core.Util;
using Game.Generated;
using Game.ZoneCommon;
using Game.ZoneCore;

namespace Game.ZoneServer;

/// <summary>
/// 시작 순서: 데이터 로드/검증 → 존 생성 → 존 스레드 시작 → 링크 스레드 시작 → Gateway 접속.
/// Gateway가 아직 떠 있지 않아도 Start()는 성공하며, 링크 스레드가 연결될 때까지 지수 백오프로 재접속한다.
/// 정지 순서: 링크 스레드 → 클라이언트 서비스(세션 종료까지 대기) → 존. 서비스를 존보다 먼저 닫아야 Gateway 패킷이 정지한 존으로 Post되지 않는다.
/// </summary>
public sealed class ZoneServerMain : IZoneLink, Game.ZoneCommon.IServerHost
{
    private const int GatewayResolveTimeoutMs = 3000;   // Gateway 주소 이름 해석 제한 시간
    private const int LinkTickMs = 200;         // 링크 스레드 틱 간격
    private const int ReconnectMinMs = 500;     // 재접속 최소 대기
    private const int ReconnectMaxMs = 5000;    // 재접속 최대 대기(지수 백오프 상한)
    private const int ReconnectJitterMs = 250;  // 게이트웨이 재시작 뒤 여러 존 서버가 같은 순간에 몰리지 않게 대기에 더하는 무작위 폭

    private const int RelayJobPoolMax = 4096;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<RelayJob> s_relayJobs = new System.Collections.Concurrent.ConcurrentQueue<RelayJob>();
    private static int s_relayJobCount;     // 풀에 든 작업 수 (ConcurrentQueue.Count는 락을 잡아 비싸므로 따로 센다)

    private readonly object _linkLock = new object();
    private ZoneServerOptions _options = new ZoneServerOptions();
    private BattleZone? _zone;
    private ClientService? _client;
    private Thread? _linkThread;
    private volatile ZoneLinkSession? _link;    // 현재 연결. 쓰기는 _linkLock 안에서, 읽기(송신 경로)는 락 없이
    private bool _needReconnect;                // 재접속이 필요하다 (_linkLock으로 보호)
    private readonly BackoffState _reconnectBackoff = new BackoffState(ReconnectMinMs, ReconnectMaxMs, jitterMs: ReconnectJitterMs);   // (_linkLock으로 보호)
    private bool _stopRequested;                // (_linkLock으로 보호)
    private ulong _lastRejectedJobs;           // 링크 스레드 전용
    private ulong _lastFailedJobs;
    private int _registered;
    private readonly PendingRequests<bool> _registerPending = new PendingRequests<bool>(maxPerOwner: 1);   // 등록 응답 대기(연결당 하나). 링크 스레드가 Advance()로 만료시킨다
    private long _registerRequestId;
    private long _duplicateSinceMs;            // 처음 DuplicateZone 으로 거부된 시각(0이면 없음). 등록에 성공하면 비운다
    private int _registerRejected;
    private int _heartbeatIntervalMs = (int)ProtocolConst.DefaultHeartbeatIntervalMs;

    /// <summary>Gateway가 등록을 거부했는가(중복 존 ID, 프로토콜 버전 불일치 등). 거부되면 프로세스를 종료해야 한다.</summary>
    public bool IsRegisterRejected => Volatile.Read(ref _registerRejected) != 0;

    /// <summary>Gateway에 등록이 끝났는가.</summary>
    public bool IsRegistered => Volatile.Read(ref _registered) != 0;

    /// <summary>전투 존 (Start() 성공 후). 테스트/모니터링용.</summary>
    public BattleZone? Zone => _zone;

    public void Dispose() => Stop();

    // ------------------------------------------------------------------------------------------------
    // 시작/정지
    // ------------------------------------------------------------------------------------------------

    public bool Start(ZoneServerOptions options)
    {
        _options = options;
        lock (_linkLock)
        {
            _stopRequested = false;
            _needReconnect = false;
            _reconnectBackoff.Reset();
        }
        _duplicateSinceMs = 0;
        Volatile.Write(ref _registered, 0);
        Volatile.Write(ref _registerRejected, 0);

        // 1. 존 생성/구동
        FieldZoneConfig zoneConfig = new FieldZoneConfig();
        zoneConfig.Zone.ZoneId = options.ZoneId;
        zoneConfig.Zone.TickIntervalMs = options.TickIntervalMs;
        zoneConfig.MinX = options.MinX;
        zoneConfig.MinY = options.MinY;
        zoneConfig.MaxX = options.MaxX;
        zoneConfig.MaxY = options.MaxY;
        zoneConfig.CellSize = options.CellSize;
        zoneConfig.RespawnDelayMs = options.RespawnDelayMs;
        // 존별 규칙: 규칙 파일(ZoneRule.json)에 이 존의 행이 있으면 그것을, 없으면 옵션의 기본값을 쓴다. 형식이 틀리면 구동하지 않는다.
        bool allowPvp = options.AllowPvp;
        bool allowDuel = options.AllowDuel;
        if (options.GameDataDir.Length != 0)
        {
            if (!ZoneRuleTable.TryLoad(options.GameDataDir, options.ZoneId, out ZoneRuleEntry? rule))
            {
                LogManager.Error("[ZoneServerMain] zone rule load failed. dir={0}", options.GameDataDir);
                return false;
            }
            if (rule != null)
            {
                allowPvp = rule.AllowPvp;
                allowDuel = rule.AllowDuel;
            }
        }
        zoneConfig.AllowPvp = allowPvp;
        zoneConfig.RespawnInvulnMs = options.RespawnInvulnMs;

        Vec2 spawnPos = new Vec2(options.SpawnX, options.SpawnY);

        // 기획 데이터. 폴더가 지정됐는데 읽기/검증에 실패하면 구동하지 않는다(데이터 없이 조용히 뜨는 것이 더 위험).
        // 폴더가 비어 있으면 몬스터 없이, 내장 기본 영웅 하나로 구동한다.
        HeroData? heroData = null;
        MonsterData? monsterData = null;
        BossData? bossData = null;
        DropData? dropData = null;
        if (options.GameDataDir.Length != 0)
        {
            heroData = GameDataLoader.LoadHeroes(options.GameDataDir);
            monsterData = GameDataLoader.LoadMonsters(options.GameDataDir);
            if (heroData != null && monsterData != null)
                bossData = GameDataLoader.LoadBosses(options.GameDataDir, monsterData, heroData);
            if (monsterData != null)
                dropData = GameDataLoader.LoadDrops(options.GameDataDir, monsterData);
            if (heroData == null || monsterData == null || bossData == null || dropData == null)
            {
                LogManager.Error("[ZoneServerMain] game data load failed. dir={0}", options.GameDataDir);
                return false;
            }

            // 장애물(Block.json). 파일이 없으면 장애물 없이 구동하고, 있는데 형식이 틀리면 구동하지 않는다.
            if (!GameDataLoader.LoadBlockMap(options.GameDataDir, options.ZoneId, zoneConfig.MinX, zoneConfig.MinY, zoneConfig.MaxX, zoneConfig.MaxY, out BlockMap? blockMap))
            {
                LogManager.Error("[ZoneServerMain] block map load failed. dir={0}", options.GameDataDir);
                return false;
            }
            zoneConfig.BlockMap = blockMap;
        }

        MonsterSystemConfig monsterConfig = new MonsterSystemConfig { ThinkIntervalMs = options.MonsterThinkIntervalMs, MaxMonsters = options.MaxMonsterCount };

        BattleZone zone;
        try
        {
            zone = new BattleZone(zoneConfig, this, spawnPos, heroData, monsterData, monsterConfig, bossData, dropData);
            zone.Capacity = options.Capacity;
            zone.GuardEnabled = options.RequestGuardEnabled;
            zone.SetRules(allowPvp, allowDuel);
        }
        catch (ArgumentException e)
        {
            LogManager.Error("[ZoneServerMain] invalid zone configuration. zoneId={0}, reason={1}", options.ZoneId, e.Message);
            return false;
        }

        // 영웅/몬스터 데이터는 존 영역/AOI 시야와 맞아야 한다(공격 사거리, 선공 거리, 입장 위치). 맞지 않으면 구동하지 않는다.
        if (!zone.ValidateMonsters(out string error))
        {
            LogManager.Error("[ZoneServerMain] game data does not fit the zone. zoneId={0}, reason={1}", options.ZoneId, error);
            zone.Shutdown();
            return false;
        }

        // 존 사이 관문(ZoneLink.json). 없으면 관문 없이 동작하고, 있는데 형식이 틀리면 구동하지 않는다.
        if (options.GameDataDir.Length != 0)
        {
            List<ZoneGateLink>? gates = ZoneLinkTable.Load(options.GameDataDir, options.ZoneId);
            if (gates == null)
            {
                LogManager.Error("[ZoneServerMain] zone link load failed. dir={0}", options.GameDataDir);
                zone.Shutdown();
                return false;
            }
            zone.SetGateLinks(gates, zoneConfig.MinX, zoneConfig.MinY, zoneConfig.MaxX, zoneConfig.MaxY);
        }

        uint spawned = zone.SpawnMonsters();
        LogManager.Info("[ZoneServerMain] monsters spawned. zoneId={0}, count={1}", options.ZoneId, spawned);

        _zone = zone;
        if (!zone.Start())
        {
            _zone = null;
            zone.Shutdown();
            return false;
        }

        // 2. Gateway 접속용 클라이언트 서비스. 세션은 1개이며, 연결이 끊기면 링크 스레드가 새 세션으로 재접속한다.
        // 이름이면 제한 시간(3초)과 동시 해석 수 상한을 두고 해석한다. IPv4 를 우선한다.
        (bool resolved, NetAddress gateway) = NetAddress.TryCreateAsync(options.GatewayIp, options.GatewayPort, GatewayResolveTimeoutMs).GetAwaiter().GetResult();
        if (!resolved)
        {
            LogManager.Error("[ZoneServerMain] invalid gateway address. ip={0}", options.GatewayIp);
            Stop();
            return false;
        }

        // 서버 간 연결은 작은 패킷을 자주 주고받으므로 Nagle을 끄고, 조용히 끊긴 연결을 keep-alive로 감지한다.
        NetServiceOptions netOptions = new NetServiceOptions
        {
            MaxSessionCount = 1,
            NoDelay = true,
            KeepAlive = true,
            Logger = new LogManagerNetLogger(NetLogLevel.Warning, "ZoneLink"),
        };
        _client = new ClientService(gateway.EndPoint, () => new ZoneLinkSession(this), netOptions);

        // 3. 링크 스레드를 먼저 시작해, Start() 직후 첫 연결이 실패해도 재접속이 이어지게 한다.
        lock (_linkLock)
            _needReconnect = true;
        _linkThread = new Thread(LinkLoop) { Name = "ZoneLink", IsBackground = true };
        _linkThread.Start();
        return true;
    }

    public void Stop()
    {
        lock (_linkLock)
        {
            _stopRequested = true;
            Monitor.PulseAll(_linkLock);
        }

        Thread? t = _linkThread;
        _linkThread = null;
        if (t != null && t != Thread.CurrentThread)
            t.Join();

        ClientService? client = _client;
        _client = null;
        client?.Close();

        lock (_linkLock)
            _link = null;

        BattleZone? zone = _zone;
        _zone = null;
        zone?.Shutdown();
    }

    // ------------------------------------------------------------------------------------------------
    // 연결 통지 (ZoneLinkSession에서 호출)
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gateway 연결이 완료되면 호출된다. 이전 연결의 플레이어는 새 연결에서 유효하지 않으므로 등록 요청 전에 정리를 올려
    /// 새 연결의 입장 요청이 입력 큐 순서상 항상 이 정리 뒤에 처리되게 한다.
    /// </summary>
    internal void OnLinkConnected(ZoneLinkSession session)
    {
        lock (_linkLock)
        {
            _link = session;
            _needReconnect = false;     // 백오프는 여기서 되돌리지 않는다: 연결만 되고 바로 끊기는 게이트웨이에 0.5초 간격으로 계속 붙지 않게, 등록 성공 때 되돌린다
        }
        Volatile.Write(ref _registered, 0);

        PostGatewayReset("connected");
        SendRegisterReq();

        // 등록 응답이 제한 시간 안에 오지 않으면 연결을 끊는다. 끊으면 OnLinkDisconnected 가 백오프 재접속을 예약한다.
        // (연결은 받아 놓고 응답하지 않는 Gateway 에 영원히 미등록으로 붙어 있지 않게 한다. TCP keep-alive 는 응용 계층 무응답을 못 잡는다)
        if (_options.RegisterTimeoutMs == 0)
            return;     // 0 이면 응답 시간 제한을 쓰지 않는다

        long ownerId = session.SessionId;
        _registerRequestId = _registerPending.Register(ownerId, (int)Math.Min(_options.RegisterTimeoutMs, int.MaxValue),
            static _ => { },
            reason =>
            {
                if (reason != PendingFailure.Timeout)
                    return;

                LogManager.Warning("[ZoneServerMain] gateway did not answer registration. zoneId={0}, timeoutMs={1}", _options.ZoneId, _options.RegisterTimeoutMs);
                session.Disconnect(CloseReason.IdleTimeout);
            });
    }

    /// <summary>
    /// 연결이 끊기거나 연결 시도가 실패하면 호출된다. 현재 연결이 이 세션일 때만 연결을 비운다.
    /// 이미 새 연결이 맺어진 뒤에 늦게 도착한 이전 세션의 종료 통지는 재접속을 일으키지 않는다.
    /// </summary>
    internal void OnLinkDisconnected(ZoneLinkSession session)
    {
        bool lost = false;
        bool stopping;
        lock (_linkLock)
        {
            if (ReferenceEquals(_link, session))
            {
                _registerPending.CancelOwner(session.SessionId);
                _link = null;
                Volatile.Write(ref _registered, 0);
                LogManager.Warning("[ZoneServerMain] gateway link lost. zoneId={0}", _options.ZoneId);
                lost = true;
            }
            stopping = _stopRequested;
            if (_link == null && !stopping)
                _needReconnect = true;
        }

        // 연결이 없는 동안 남은 플레이어는 응답을 받을 곳이 없으므로 바로 내보낸다. (재접속 시 한 번 더 정리하므로 누락돼도 안전하다)
        if (lost && !stopping)
            PostGatewayReset("link lost");
    }

    private void PostGatewayReset(string reason)
    {
        BattleZone? zone = _zone;
        if (zone == null)
            return;
        uint zoneId = _options.ZoneId;
        zone.Post(z =>
        {
            uint removed = ((BattleZone)z).HandleGatewayReset();
            if (removed != 0)
                LogManager.Warning("[ZoneServerMain] players removed after gateway link reset. zoneId={0}, count={1}, reason={2}", zoneId, removed, reason);
        });
    }

    // ------------------------------------------------------------------------------------------------
    // Gateway 패킷 처리
    // ------------------------------------------------------------------------------------------------

    /// <summary>Gateway에서 온 패킷 하나를 처리한다. 프로토콜 위반이면 false(연결 종료).</summary>
    internal bool OnGatewayPacket(ReadOnlySpan<byte> packet)
    {
        switch ((ZoneServerPacketType)PacketIO.ReadType(packet))
        {
            case ZoneServerPacketType.ZoneRegisterRes: return HandleRegisterRes(packet);
            case ZoneServerPacketType.ZoneEnterReq: return HandleEnterReq(packet);
            case ZoneServerPacketType.ZoneLeaveReq: return HandleLeaveReq(packet);
            case ZoneServerPacketType.ZoneRelayToZone: return HandleRelayToZone(packet);
            case (ZoneServerPacketType)PlayerStatePacketType.Restore: return HandleStateRestore(packet);
            default:
                LogThrottle.Error("zone.unknown-packet", 5000, "[ZoneServerMain] unknown packet type from gateway: {0}", PacketIO.ReadType(packet));
                return false;
        }
    }

    /// <summary>등록 응답. 성공하면 하트비트를 시작하고, 거부되면 프로세스 종료를 요청한다.</summary>
    private bool HandleRegisterRes(ReadOnlySpan<byte> packet)
    {
        if (!ZoneRegisterRes.TryRead(packet, out ZoneRegisterRes res))
            return false;

        // 거부든 성공이든 응답이 왔으므로 대기를 끝낸다.
        _registerPending.Cancel(Volatile.Read(ref _registerRequestId));

        if (res.Success == 0)
        {
            // 같은 존 ID 의 이전 연결이 아직 살아 있는 것으로 보이는 경우(이전 인스턴스가 연결을 닫지 못하고 사라졌고 Gateway 의 하트비트
            // 만료 전에 다시 뜬 경우)는 잠시 재시도한다. 살아 있는 인스턴스와 설정이 겹친 것이라면 제한 시간 뒤에 종료된다.
            if (res.Reason == (byte)ZoneRegisterResult.DuplicateZone && _options.DuplicateRetryMs != 0)
            {
                long now = TimeUtil.MonotonicNowMillis();
                if (_duplicateSinceMs == 0)
                    _duplicateSinceMs = now;

                if (now - _duplicateSinceMs < _options.DuplicateRetryMs)
                {
                    LogManager.Warning("[ZoneServerMain] zone id is still held by another link, retrying. zoneId={0}", _options.ZoneId);
                    _link?.Disconnect(CloseReason.Local);     // 끊으면 OnLinkDisconnected 가 백오프 재접속을 예약한다
                    return true;
                }
            }

            LogManager.Error("[ZoneServerMain] zone registration rejected. zoneId={0}, reason={1}", _options.ZoneId, res.Reason);
            Volatile.Write(ref _registerRejected, 1);
            return true;
        }

        _duplicateSinceMs = 0;

        int intervalMs = (int)Math.Min(Math.Max(res.HeartbeatIntervalMs, 200u), 30000u);
        Volatile.Write(ref _heartbeatIntervalMs, intervalMs);
        lock (_linkLock)
            _reconnectBackoff.Reset();
        Volatile.Write(ref _registered, 1);
        LogManager.Info("[ZoneServerMain] zone registered. zoneId={0}, heartbeatIntervalMs={1}", _options.ZoneId, intervalMs);
        return true;
    }

    /// <summary>입장 요청을 존 입력 큐로 넘긴다. 큐가 가득 차 넘기지 못하면 즉시 실패 응답을 보낸다.</summary>
    private bool HandleEnterReq(ReadOnlySpan<byte> packet)
    {
        if (!ZoneEnterReq.TryRead(packet, out ZoneEnterReq req))
            return false;

        ulong sessionId = req.ClientSessionId;
        ulong charId = req.CharId;
        uint heroId = req.HeroId;
        BattleZone? zone = _zone;
        bool posted = zone != null && zone.Post(z => ((BattleZone)z).HandleEnter(sessionId, charId, heroId));
        if (!posted)
        {
            ZoneEnterRes res = new ZoneEnterRes { ClientSessionId = sessionId, CharId = charId, Success = 0, Reason = (byte)ZoneEnterResult.ZoneError };
            SendPacketToGateway(res);
        }
        return true;
    }

    /// <summary>Gateway가 돌려준 보관 상태(체력/MP/가방/도착 지점)를 존 입력 큐로 넘긴다. 바로 뒤의 입장 요청에 쓰인다.</summary>
    private bool HandleStateRestore(ReadOnlySpan<byte> packet)
    {
        if (!ZoneStateRestore.TryRead(packet, out ZoneStateRestore req) || req.State.CharId == 0)
            return false;

        PlayerState state = req.State;
        BattleZone? zone = _zone;
        if (zone == null || !zone.Post(z => ((BattleZone)z).HandleRestore(state)))
            LogThrottle.Warning("zoneserver.restore-dropped", 1000, "[ZoneServerMain] state restore dropped (zone inbox full or stopped). charId={0}", state.CharId);
        return true;
    }

    /// <summary>퇴장 요청을 존 입력 큐로 넘긴다. 큐가 가득 차 넘기지 못하면 엔티티가 존에 남을 수 있으므로 경고를 남긴다.</summary>
    private bool HandleLeaveReq(ReadOnlySpan<byte> packet)
    {
        if (!ZoneLeaveReq.TryRead(packet, out ZoneLeaveReq req))
            return false;

        ulong sessionId = req.ClientSessionId;
        ulong charId = req.CharId;
        BattleZone? zone = _zone;
        bool posted = zone != null && zone.Post(z => ((BattleZone)z).HandleLeave(sessionId, charId));
        if (!posted)
            LogThrottle.Warning("zoneserver.leave-dropped", 1000, "[ZoneServerMain] leave request dropped (zone inbox full or stopped). charId={0}", charId);
        return true;
    }

    /// <summary>
    /// 클라이언트 패킷 중계를 존 입력 큐로 넘긴다. 수신 버퍼는 반환 후 재사용되므로 원문을 복사해 넘기되,
    /// 패킷마다 배열/람다를 새로 만들지 않도록 작업 객체와 버퍼를 풀링한다.
    /// </summary>
    private bool HandleRelayToZone(ReadOnlySpan<byte> packet)
    {
        if (!ZonePacket.TryParseRelay(packet, out ulong sessionId, out ReadOnlySpan<byte> payload))
            return false;
        if (payload.Length < PacketIO.HeaderSize)
            return false;
        // 원문 헤더의 size가 실제 페이로드 크기와 맞아야 한다.
        if (PacketIO.ReadSize(payload) != payload.Length)
            return false;

        BattleZone? zone = _zone;
        if (zone == null)
            return true;

        RelayJob? job;
        if (s_relayJobs.TryDequeue(out job))
            Interlocked.Decrement(ref s_relayJobCount);
        else
            job = new RelayJob();
        job.Set(sessionId, payload);
        if (!zone.Post(job.RunFn))
            job.Release();          // 큐가 가득 찼거나 정지 중
        return true;
    }

    /// <summary>존 스레드에서 실행되는 클라이언트 패킷 전달 작업. 풀에서 재사용한다.</summary>
    private sealed class RelayJob
    {
        public readonly Action<Zone> RunFn;
        private byte[] _buf = Array.Empty<byte>();
        private int _len;
        private ulong _sessionId;

        public RelayJob()
        {
            RunFn = Run;
        }

        public void Set(ulong sessionId, ReadOnlySpan<byte> payload)
        {
            _sessionId = sessionId;
            _len = payload.Length;
            _buf = System.Buffers.ArrayPool<byte>.Shared.Rent(payload.Length);
            payload.CopyTo(_buf);
        }

        private void Run(Zone zone)
        {
            try
            {
                ((BattleZone)zone).HandleClientPacket(_sessionId, new ReadOnlySpan<byte>(_buf, 0, _len));
            }
            finally
            {
                Release();
            }
        }

        public void Release()
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(_buf);
            _buf = Array.Empty<byte>();
            if (Volatile.Read(ref s_relayJobCount) < RelayJobPoolMax)
            {
                Interlocked.Increment(ref s_relayJobCount);
                s_relayJobs.Enqueue(this);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    // 송신 (IZoneLink)
    // ------------------------------------------------------------------------------------------------

    public void SendToGateway(ReadOnlySpan<byte> packet)
    {
        _link?.Send(packet);
    }

    private void SendPacketToGateway<T>(in T packet) where T : struct, IPacket => _link?.SendPacket(packet);

    public void SendToClient(ulong clientSessionId, ReadOnlySpan<byte> packet)
    {
        byte[] buffer = ZonePacket.ThreadBuffer;
        int total = ZonePacket.BuildRelay(ZoneServerPacketType.ZoneRelayToClient, clientSessionId, packet, buffer);
        if (total > 0)
            SendToGateway(buffer.AsSpan(0, total));
    }

    public void SendToClients(ReadOnlySpan<ulong> clientSessionIds, ReadOnlySpan<byte> packet)
    {
        if (clientSessionIds.Length == 0)
            return;
        if (clientSessionIds.Length == 1)
        {
            SendToClient(clientSessionIds[0], packet);
            return;
        }

        int perPacket = ZonePacket.MaxMulticastRecipients(packet.Length, ProtocolConst.MaxServerPacketBytes);
        if (perPacket <= 0)
            return;

        byte[] buffer = ZonePacket.ThreadBuffer;
        for (int offset = 0; offset < clientSessionIds.Length;)
        {
            int n = Math.Min(clientSessionIds.Length - offset, perPacket);
            int total = ZonePacket.BuildMulticastRelay(clientSessionIds.Slice(offset, n), packet, buffer);
            if (total <= 0)
                return;
            SendToGateway(buffer.AsSpan(0, total));
            offset += n;
        }
    }

    private void SendRegisterReq()
    {
        ZoneRegisterReq req = new ZoneRegisterReq
        {
            ProtocolVersion = ProtocolConst.ZoneProtocolVersion,
            ZoneId = _options.ZoneId,
            Kind = (byte)_options.Kind,
            Capacity = _options.Capacity,
            ServerName = _options.ServerName,
        };
        SendPacketToGateway(req);
    }

    private MetricsSnapshot? _prevMetrics;      // 직전 메트릭 로그 시점의 값 (링크 스레드 전용)
    private ulong[]? _prevBuckets;
    private ulong _prevTicks;
    private long _prevMetricsAtMs;

    /// <summary>
    /// 구간 메트릭 한 줄을 로그로 남긴다 (MetricsIntervalMs마다, 링크 스레드). 틱 처리 시간 평균/최대/분포, 틱 이월(overrun),
    /// 송수신 속도, 멀티캐스트 팬아웃, 버린 요청/추방 수, 지면 아이템 수를 보여 준다. 첫 호출은 시작 후 누적 구간이다.
    /// </summary>
    private void LogMetrics(long nowMs)
    {
        BattleZone? zone = _zone;
        if (zone == null)
            return;

        ZoneStats stats = zone.GetStats();
        MetricsSnapshot cur = zone.Metrics.Snapshot();
        double seconds = _prevMetricsAtMs == 0 ? 0.0 : (nowMs - _prevMetricsAtMs) / 1000.0;

        ulong[] buckets = stats.TickBuckets ?? new ulong[Game.ZoneCore.Zone.TickBucketCount];
        ulong[] delta = new ulong[buckets.Length];
        for (int i = 0; i < delta.Length; ++i)
            delta[i] = buckets[i] - (_prevBuckets != null ? _prevBuckets[i] : 0);

        ulong ticks = stats.TickCount - _prevTicks;
        ulong avgUs = ticks == 0 ? 0 : (stats.TotalTickUs - _prevTotalTickUs) / ticks;

        LogManager.Info("[Metrics] zoneId={0} users={1} monsters={2} ground={3} | tick avg={4}us maxEver={5}us overrun={6} ({7}) | {8}",
            _options.ZoneId, zone.PlayerCount, zone.MonsterCount, zone.Drops.GroundCount, avgUs, stats.MaxTickUs, stats.OverrunTicks,
            ZoneMetrics.FormatTickBuckets(delta), ZoneMetrics.Format(cur, _prevMetrics, seconds));

        _prevMetrics = cur;
        _prevBuckets = buckets;
        _prevTicks = stats.TickCount;
        _prevTotalTickUs = stats.TotalTickUs;
        _prevMetricsAtMs = nowMs;
    }

    private ulong _prevTotalTickUs;

    private void SendHeartbeat()
    {
        BattleZone? zone = _zone;
        if (zone == null)
            return;

        ZoneStats stats = zone.GetStats();
        if (stats.RejectedJobs != _lastRejectedJobs || stats.FailedJobs != _lastFailedJobs)
        {
            LogManager.Warning("[ZoneServerMain] zone jobs dropped. zoneId={0}, rejected={1} (+{2}), failed={3} (+{4})",
                _options.ZoneId, stats.RejectedJobs, stats.RejectedJobs - _lastRejectedJobs, stats.FailedJobs, stats.FailedJobs - _lastFailedJobs);
            _lastRejectedJobs = stats.RejectedJobs;
            _lastFailedJobs = stats.FailedJobs;
        }
        ZoneHeartbeat hb = new ZoneHeartbeat
        {
            ZoneId = _options.ZoneId,
            UserCount = zone.PlayerCount,
            LastTickUs = (uint)Math.Min(stats.LastTickUs, (ulong)uint.MaxValue),
            // 시작 후 누적 최대가 아니라 직전 하트비트 이후 구간의 최대값을 보고한다.
            MaxTickUs = (uint)Math.Min(zone.ConsumeMaxTickUs(), (ulong)uint.MaxValue),
        };
        SendPacketToGateway(hb);
    }

    // ------------------------------------------------------------------------------------------------
    // 링크 스레드
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// 짧은 틱마다 (1) 연결이 없으면 지수 백오프로 재접속하고, (2) 등록이 끝났으면 주기적으로 하트비트를 보낸다.
    /// ConnectOneMoreSession()/SendHeartbeat()는 락을 잡거나 연결 실패 통지로 OnLinkDisconnected()를 부를 수 있으므로 락을 푼 상태에서 호출한다.
    /// </summary>
    private void LinkLoop()
    {
        long nextReconnect = Environment.TickCount64;
        long nextHeartbeat = Environment.TickCount64;
        long nextMetrics = Environment.TickCount64 + _options.MetricsIntervalMs;

        while (true)
        {
            bool reconnect = false;
            int backoffMs = 0;
            long now;

            lock (_linkLock)
            {
                if (_stopRequested)
                    return;
                Monitor.Wait(_linkLock, LinkTickMs);
                if (_stopRequested)
                    return;

                now = Environment.TickCount64;
                if (_needReconnect && now >= nextReconnect)
                {
                    _needReconnect = false;
                    backoffMs = (int)_reconnectBackoff.OnFail(now);
                    reconnect = true;
                }
            }

            _registerPending.Advance();

            if (reconnect)
            {
                ClientService? client = _client;
                bool posted = client != null && client.ConnectOneMoreSession();
                lock (_linkLock)
                {
                    // 세션 게시 자체가 실패한 경우에만 여기서 다시 예약한다.
                    // 게시는 됐지만 연결이 실패한 경우는 OnLinkDisconnected()가 예약한다.
                    if (!posted && !_stopRequested)
                        _needReconnect = true;
                }
                nextReconnect = Environment.TickCount64 + backoffMs;
            }

            if (_options.MetricsIntervalMs != 0 && now >= nextMetrics)
            {
                nextMetrics = now + _options.MetricsIntervalMs;
                LogMetrics(now);
            }

            if (IsRegistered && now >= nextHeartbeat)
            {
                nextHeartbeat = now + Volatile.Read(ref _heartbeatIntervalMs);
                SendHeartbeat();
            }
        }
    }
}
