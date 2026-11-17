// GatewayServerMain.cs : 게이트웨이 서버 파사드. 클라이언트 접속과 존 서버 접속을 각각 받아 중계/라우팅한다.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Core.Net;
using Core.Util;
using Game.Generated;
using Game.ZoneCommon;

namespace Game.GatewayServer;

/// <summary>
/// 역할:
///  1. 클라이언트용/존 서버용 두 개의 서버 서비스 구동
///  2. 로그인(임시), 존 입장/퇴장 처리와 존 정원 관리(ZoneRouter의 슬롯)
///  3. 클라이언트 패킷(2000 이상)을 현재 존 서버로 중계, 존 서버가 보낸 패킷을 클라이언트로 중계
///  4. 존 서버 등록/하트비트 처리, 하트비트 타임아웃 감시, 존 서버 연결 종료 시 해당 존의 클라이언트 정리
/// 종료 순서: 감시 스레드 → 클라이언트 서비스(클라이언트 퇴장이 존 서버로 전달돼야 하므로 먼저) → 존 서버 서비스.
/// </summary>
public sealed class GatewayServerMain : Game.ZoneCommon.IServerHost
{
    private const int MaintenanceTickMs = 500;      // 감시 스레드 틱 간격

    [ThreadStatic] private static List<GatewayClientSession>? t_recipients;

    private readonly ZoneRouter _router = new ZoneRouter();
    private readonly ConcurrentDictionary<ulong, GatewayClientSession> _clients = new ConcurrentDictionary<ulong, GatewayClientSession>();      // 클라이언트 세션 ID -> 세션
    private readonly ConcurrentDictionary<ulong, ulong> _chars = new ConcurrentDictionary<ulong, ulong>();    // 로그인 중인 캐릭터 ID -> 클라이언트 세션 ID (중복 로그인 차단)
    private readonly object _maintenanceLock = new object();
    private GatewayOptions _options = new GatewayOptions();
    private ServerService? _clientService;
    private ServerService? _zoneService;
    private Thread? _maintenanceThread;
    private bool _maintenanceStop;

    public ZoneRouter Router => _router;

    /// <summary>클라이언트용 서비스의 실제 바인드 주소(포트 0으로 시작했을 때 확인용).</summary>
    public IPEndPoint? ClientEndPoint => _clientService?.LocalEndPoint;

    /// <summary>존 서버용 서비스의 실제 바인드 주소.</summary>
    public IPEndPoint? ZoneEndPoint => _zoneService?.LocalEndPoint;

    /// <summary>현재 접속 중인 클라이언트 수.</summary>
    public int ClientCount => _clients.Count;

    public void Dispose() => Stop();

    // ------------------------------------------------------------------------------------------------
    // 시작/정지
    // ------------------------------------------------------------------------------------------------

    /// <summary>존 서버용 서비스를 먼저 시작해 존 서버가 등록할 수 있게 한 뒤, 클라이언트용 서비스를 시작한다.</summary>
    public bool Start(GatewayOptions options)
    {
        _options = options;

        // 서버 간/클라이언트 연결 모두 작은 패킷을 자주 주고받으므로 Nagle을 끄고, 조용히 끊긴 연결을 keep-alive로 감지한다.
        if (!IPAddress.TryParse(options.ZoneBindIp, out IPAddress? zoneIp) || !IPAddress.TryParse(options.ClientBindIp, out IPAddress? clientIp))
        {
            LogManager.Error("[GatewayServerMain] invalid bind address. client={0}, zone={1}", options.ClientBindIp, options.ZoneBindIp);
            return false;
        }

        ServerService zone = new ServerService(new IPEndPoint(zoneIp, options.ZonePort), () => new GatewayZoneSession(this),
            new NetServiceOptions { MaxSessionCount = options.MaxZoneSessions, NoDelay = true, KeepAlive = true, Logger = new LogManagerNetLogger(NetLogLevel.Warning, "GwZone") });
        if (!zone.Start())
            return false;
        _zoneService = zone;

        ServerService client = new ServerService(new IPEndPoint(clientIp, options.ClientPort), () => new GatewayClientSession(this),
            new NetServiceOptions { MaxSessionCount = options.MaxClientSessions, NoDelay = true, KeepAlive = true, MaxSendQueueBytes = options.ClientMaxSendQueueBytes, Logger = new LogManagerNetLogger(NetLogLevel.Warning, "GwClient") });
        if (!client.Start())
        {
            Stop();
            return false;
        }
        _clientService = client;

        lock (_maintenanceLock)
            _maintenanceStop = false;
        _maintenanceThread = new Thread(MaintenanceLoop) { Name = "GwMaintenance", IsBackground = true };
        _maintenanceThread.Start();
        return true;
    }

    /// <summary>클라이언트 서비스를 먼저 닫는다 — 각 클라이언트 세션의 OnDisconnected가 존 서버로 퇴장을 통지하므로 존 서버 연결이 아직 살아있어야 한다.</summary>
    public void Stop()
    {
        lock (_maintenanceLock)
        {
            _maintenanceStop = true;
            Monitor.PulseAll(_maintenanceLock);
        }
        Thread? t = _maintenanceThread;
        _maintenanceThread = null;
        if (t != null && t != Thread.CurrentThread)
            t.Join();

        ServerService? client = _clientService;
        _clientService = null;
        client?.Close();

        ServerService? zone = _zoneService;
        _zoneService = null;
        zone?.Close();
    }

    // ------------------------------------------------------------------------------------------------
    // 클라이언트 세션 콜백
    // ------------------------------------------------------------------------------------------------

    /// <summary>클라이언트 연결 완료. 존 서버 응답 라우팅을 위해 세션 ID로 등록한다.</summary>
    internal void OnClientConnected(GatewayClientSession session) => _clients[session.ClientSessionId] = session;

    /// <summary>
    /// 클라이언트 연결 종료. 존에 있었다면 존 서버에 퇴장을 통지하고 슬롯을 반환한다(입장 진행 중이던 경우 포함).
    /// 그 뒤 로그인 레지스트리와 클라이언트 레지스트리에서 제거한다.
    /// </summary>
    internal void OnClientDisconnected(GatewayClientSession session)
    {
        DetachFromZone(session);
        ulong sessionId = session.ClientSessionId;
        if (session.IsLoggedIn)
            _chars.TryRemove(new KeyValuePair<ulong, ulong>(session.CharId, sessionId));
        _clients.TryRemove(new KeyValuePair<ulong, GatewayClientSession>(sessionId, session));
    }

    /// <summary>클라이언트 패킷 하나를 처리한다. 2000 이상은 해석하지 않고 현재 존으로 중계하고, 그 미만은 Gateway 패킷으로 처리한다.</summary>
    /// <returns>프로토콜 위반(속도 제한 초과, 알 수 없는 패킷, 크기 부족)이면 false(연결 종료)</returns>
    internal bool OnClientPacket(GatewayClientSession client, ReadOnlySpan<byte> packet)
    {
        if (!client.AllowPacket(_options.ClientMaxPacketsPerSec))
        {
            LogManager.Warning("[GatewayServerMain] client packet rate exceeded. sessionId={0}", client.ClientSessionId);
            return false;
        }

        ushort type = PacketIO.ReadType(packet);
        if (type >= ProtocolConst.ClientRelayTypeMin)
            return RelayClientPacket(client, packet);

        switch ((GatewayPacketType)type)
        {
            case GatewayPacketType.GwLoginReq: return HandleLogin(client, packet);
            case GatewayPacketType.GwEnterZoneReq: return HandleEnterZone(client, packet);
            case GatewayPacketType.GwLeaveZoneReq: return HandleLeaveZone(client);
            default:
                LogManager.Error("[GatewayServerMain] unknown client packet type: {0}", type);
                return false;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // 존 서버 세션 콜백
    // ------------------------------------------------------------------------------------------------

    /// <summary>존 서버 패킷 하나를 처리한다. 등록 전에는 등록 요청만 허용한다.</summary>
    internal bool OnZonePacket(GatewayZoneSession session, ReadOnlySpan<byte> packet)
    {
        ZoneServerPacketType type = (ZoneServerPacketType)PacketIO.ReadType(packet);
        if (session.ZoneId == 0)
            return type == ZoneServerPacketType.ZoneRegisterReq && HandleZoneRegister(session, packet);

        switch (type)
        {
            case ZoneServerPacketType.ZoneHeartbeat: return HandleZoneHeartbeat(session, packet);
            case ZoneServerPacketType.ZoneEnterRes: return HandleZoneEnterRes(session, packet);
            case ZoneServerPacketType.ZoneKickNotify: return HandleZoneKick(session, packet);
            case ZoneServerPacketType.ZoneRelayToClient: return HandleRelayToClient(session, packet);
            case ZoneServerPacketType.ZoneRelayToClients: return HandleRelayToClients(session, packet);
            default:
                LogManager.Error("[GatewayServerMain] unknown zone packet type: {0}", (ushort)type);
                return false;
        }
    }

    /// <summary>
    /// 존 서버 연결이 종료되면 호출된다. 라우팅 테이블에서 제거하고, 그 존에 있던 클라이언트에 소실을 알린다.
    /// 이미 새 세션으로 교체된 노드(존 서버가 빠르게 재시작)여도 이 세션 시절에 입장한 클라이언트는 정리해야 한다.
    /// </summary>
    internal void OnZoneSessionClosed(GatewayZoneSession session)
    {
        uint zoneId = session.ZoneId;
        if (zoneId == 0)
            return;

        ZoneNode? node = session.Node;
        ulong seq = node?.Seq ?? 0;

        if (_router.Unregister(zoneId, session))
            LogManager.Warning("[GatewayServerMain] zone server lost. zoneId={0}", zoneId);
        else
            LogManager.Warning("[GatewayServerMain] replaced zone session closed. zoneId={0}", zoneId);

        // 존 서버가 죽는 드문 이벤트라 전체 순회(O(N))를 허용한다.
        List<GatewayClientSession> affected = new List<GatewayClientSession>();
        foreach (KeyValuePair<ulong, GatewayClientSession> kv in _clients)
        {
            GatewayClientSession c = kv.Value;
            if (c.ZoneId == zoneId && (seq == 0 || c.ZoneSeq == seq))
                affected.Add(c);
        }

        foreach (GatewayClientSession c in affected)
        {
            if (!c.ClearZoneIf(zoneId))
                continue;
            SendZoneLost(c, zoneId);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // 클라이언트 패킷 핸들러 (false = 프로토콜 위반)
    // ------------------------------------------------------------------------------------------------

    private bool HandleLogin(GatewayClientSession client, ReadOnlySpan<byte> packet)
    {
        if (!GwLoginReq.TryRead(packet, out GwLoginReq req))
            return false;

        GatewayResult result = GatewayResult.Ok;
        if (client.IsLoggedIn)
        {
            result = GatewayResult.AlreadyLoggedIn;
        }
        else if (req.CharId == 0 || !AuthenticateClient(client, req.CharId))
        {
            result = GatewayResult.InvalidCharId;
        }
        else if (!_chars.TryAdd(req.CharId, client.ClientSessionId))
        {
            // 같은 캐릭터가 다른 세션으로 이미 접속 중이면 거부한다(존 엔티티 ID 충돌 방지).
            result = GatewayResult.AlreadyLoggedIn;
        }

        if (result == GatewayResult.Ok)
            client.MarkLoggedIn(req.CharId);

        GwLoginRes res = new GwLoginRes
        {
            ClientSessionId = client.ClientSessionId,
            Success = (byte)(result == GatewayResult.Ok ? 1 : 0),
            Reason = (byte)result,
        };
        client.SendPacket(res);
        return true;
    }

    /// <summary>
    /// 세션이 존 슬롯을 먼저 점유(TryAssignZone)해 중복 입장을 막고, 존 정원 슬롯을 예약한 뒤 존 서버에 입장을 요청한다.
    /// 최종 결과는 존 서버의 ZoneEnterRes로 비동기 통지된다(HandleZoneEnterRes).
    /// </summary>
    private bool HandleEnterZone(GatewayClientSession client, ReadOnlySpan<byte> packet)
    {
        if (!GwEnterZoneReq.TryRead(packet, out GwEnterZoneReq req))
            return false;

        uint zoneId = req.ZoneId;
        uint heroId = req.HeroId;       // 유효성은 존 서버가 영웅 데이터로 판정한다

        if (!client.IsLoggedIn)
        {
            SendEnterZoneRes(client, GatewayResult.NotLoggedIn, zoneId, 0.0f, 0.0f);
            return true;
        }
        if (zoneId == 0)
        {
            SendEnterZoneRes(client, GatewayResult.ZoneNotFound, zoneId, 0.0f, 0.0f);
            return true;
        }
        if (!client.TryAssignZone(zoneId))
        {
            SendEnterZoneRes(client, GatewayResult.AlreadyInZone, zoneId, 0.0f, 0.0f);
            return true;
        }

        ZoneNode? node = _router.Find(zoneId);
        GatewayZoneSession? zoneSession = node?.Session;
        if (node == null || zoneSession == null || !zoneSession.IsConnected)
        {
            client.ClearZoneIf(zoneId);
            SendEnterZoneRes(client, GatewayResult.ZoneNotFound, zoneId, 0.0f, 0.0f);
            return true;
        }

        if (!ZoneRouter.TryReserveSlot(node))
        {
            client.ClearZoneIf(zoneId);
            SendEnterZoneRes(client, GatewayResult.ZoneFull, zoneId, 0.0f, 0.0f);
            return true;
        }

        // 입장한 노드와 존 서버 세션을 기억한다. 패킷 중계가 라우터 조회 없이 이 세션을 쓰고, 노드가 교체됐을 때 식별에도 쓴다.
        client.SetZoneRoute(node.Seq, zoneSession);

        ZoneEnterReq enter = new ZoneEnterReq { ClientSessionId = client.ClientSessionId, CharId = client.CharId, HeroId = heroId };
        if (!zoneSession.SendPacket(enter))
        {
            ZoneRouter.ReleaseSlot(node);
            client.ClearZoneIf(zoneId);
            SendEnterZoneRes(client, GatewayResult.ZoneError, zoneId, 0.0f, 0.0f);
        }
        return true;
    }

    private bool HandleLeaveZone(GatewayClientSession client)
    {
        uint zoneId = DetachFromZone(client);
        GwLeaveZoneRes res = new GwLeaveZoneRes
        {
            Success = (byte)(zoneId != 0 ? 1 : 0),
            Reason = (byte)(zoneId != 0 ? GatewayResult.Ok : GatewayResult.NotInZone),
        };
        client.SendPacket(res);
        return true;
    }

    /// <summary>
    /// 클라이언트 패킷(2000 이상)을 해석하지 않고 현재 존 서버로 중계한다. 로그인하지 않았거나 입장이 확정된 존이 없으면 조용히 버린다.
    /// 입장 때 기억해 둔 존 서버 세션을 쓰므로 패킷마다 라우터 읽기 락을 잡지 않는다.
    /// 존 서버가 교체됐다면 이전 세션은 이미 끊겨 Send가 실패하며 클라이언트는 ZoneLost 통지로 정리된다.
    /// </summary>
    private bool RelayClientPacket(GatewayClientSession client, ReadOnlySpan<byte> packet)
    {
        if (!client.IsLoggedIn || !client.IsEnterConfirmed || client.ZoneId == 0)
            return true;

        GatewayZoneSession? zoneSession = client.GetZoneSession();
        if (zoneSession == null)
            return true;

        byte[] buffer = ZonePacket.ThreadBuffer;
        int total = ZonePacket.BuildRelay(ZoneServerPacketType.ZoneRelayToZone, client.ClientSessionId, packet, buffer);
        if (total > 0)
            zoneSession.Send(buffer.AsSpan(0, total));
        return true;
    }

    // ------------------------------------------------------------------------------------------------
    // 존 서버 패킷 핸들러 (false = 프로토콜 위반)
    // ------------------------------------------------------------------------------------------------

    /// <summary>등록 요청. 거부된 존 서버는 스스로 종료해야 하므로 연결은 끊지 않고 응답만 보낸다.</summary>
    private bool HandleZoneRegister(GatewayZoneSession session, ReadOnlySpan<byte> packet)
    {
        if (!ZoneRegisterReq.TryRead(packet, out ZoneRegisterReq req))
            return false;

        ZoneRegisterResult result;
        if (req.ProtocolVersion != ProtocolConst.ZoneProtocolVersion)
        {
            result = ZoneRegisterResult.VersionMismatch;
        }
        else
        {
            result = _router.Register(session, req.ZoneId, (ZoneKind)req.Kind, req.Capacity, req.ServerName, Environment.TickCount64, out ZoneNode? node);
            if (result == ZoneRegisterResult.Ok)
            {
                session.BindZone(req.ZoneId, node!);
                LogManager.Info("[GatewayServerMain] zone registered. zoneId={0}, kind={1}, capacity={2}", req.ZoneId, req.Kind, req.Capacity);
            }
            else
            {
                LogManager.Warning("[GatewayServerMain] zone registration rejected. zoneId={0}, reason={1}", req.ZoneId, (byte)result);
            }
        }

        ZoneRegisterRes res = new ZoneRegisterRes
        {
            Success = (byte)(result == ZoneRegisterResult.Ok ? 1 : 0),
            Reason = (byte)result,
            HeartbeatIntervalMs = _options.HeartbeatIntervalMs,
        };
        session.SendPacket(res);
        return true;
    }

    private bool HandleZoneHeartbeat(GatewayZoneSession session, ReadOnlySpan<byte> packet)
    {
        if (!ZoneHeartbeat.TryRead(packet, out ZoneHeartbeat hb))
            return false;
        ZoneNode? node = session.Node;
        if (node == null)
            return false;
        node.RecordHeartbeat(Environment.TickCount64, hb.UserCount, hb.LastTickUs, hb.MaxTickUs);
        return true;
    }

    /// <summary>
    /// 존 서버의 입장 응답을 클라이언트에 전달한다. 클라이언트가 이미 떠났거나 다른 존으로 옮겨 점유 존이 다르면 응답을 무시한다
    /// (떠나는 경우의 퇴장 통지는 이미 존 서버로 전달돼 있다).
    /// </summary>
    private bool HandleZoneEnterRes(GatewayZoneSession session, ReadOnlySpan<byte> packet)
    {
        if (!ZoneEnterRes.TryRead(packet, out ZoneEnterRes res))
            return false;

        uint zoneId = session.ZoneId;
        ZoneNode? enterNode = session.Node;      // 교체된 이전 세션의 늦은 응답은 새 노드에 입장한 같은 클라이언트에게 적용하지 않는다.

        GatewayClientSession? client = FindClient(res.ClientSessionId);
        if (client == null || client.ZoneId != zoneId || client.CharId != res.CharId
            || (enterNode != null && client.ZoneSeq != enterNode.Seq))
            return true;

        if (res.Success != 0)
        {
            client.SetEnterConfirmed(true);
            SendEnterZoneRes(client, GatewayResult.Ok, zoneId, res.SpawnX, res.SpawnY);
            return true;
        }

        if (client.ClearZoneIf(zoneId) && enterNode != null)
            ZoneRouter.ReleaseSlot(enterNode);
        SendEnterZoneRes(client, ToGatewayResult((ZoneEnterResult)res.Reason), zoneId, 0.0f, 0.0f);
        return true;
    }

    /// <summary>존 서버의 입장 거부 사유를 클라이언트에게 줄 결과로 바꾼다. 모르는 값은 일반 존 오류로 본다.</summary>
    private static GatewayResult ToGatewayResult(ZoneEnterResult reason) => reason switch
    {
        ZoneEnterResult.DuplicateEntity => GatewayResult.AlreadyInZone,
        ZoneEnterResult.InvalidCharId => GatewayResult.InvalidCharId,
        ZoneEnterResult.InvalidHero => GatewayResult.InvalidHero,
        ZoneEnterResult.ZoneFull => GatewayResult.ZoneFull,
        _ => GatewayResult.ZoneError,
    };

    /// <summary>존 서버가 플레이어를 내보냈을 때 클라이언트의 존 점유를 해제하고 알린다.</summary>
    private bool HandleZoneKick(GatewayZoneSession session, ReadOnlySpan<byte> packet)
    {
        if (!ZoneKickNotify.TryRead(packet, out ZoneKickNotify kick))
            return false;

        uint zoneId = session.ZoneId;
        GatewayClientSession? client = FindClient(kick.ClientSessionId);
        if (client == null || !client.ClearZoneIf(zoneId))
            return true;

        ZoneNode? node = session.Node;
        if (node != null)
            ZoneRouter.ReleaseSlot(node);
        SendZoneLost(client, zoneId);
        return true;
    }

    /// <summary>존 서버가 보낸 클라이언트용 패킷을 해당 클라이언트로 전달한다. 그 클라이언트가 지금 이 존에 있을 때만 전달한다.</summary>
    private bool HandleRelayToClient(GatewayZoneSession session, ReadOnlySpan<byte> packet)
    {
        if (!ZonePacket.TryParseRelay(packet, out ulong clientSessionId, out ReadOnlySpan<byte> payload))
            return false;
        if (payload.Length < PacketIO.HeaderSize || PacketIO.ReadSize(payload) != payload.Length)
            return false;

        GatewayClientSession? client = FindClient(clientSessionId);
        if (client != null && client.ZoneId == session.ZoneId)
            client.Send(payload);
        return true;
    }

    /// <summary>
    /// 존 서버가 보낸 멀티캐스트 패킷을 수신자 클라이언트들에게 전달한다.
    /// 수신자마다 "지금 이 존에 있는지" 검사하므로 존을 옮겼거나 끊긴 클라이언트는 조용히 건너뛴다.
    /// </summary>
    private bool HandleRelayToClients(GatewayZoneSession session, ReadOnlySpan<byte> packet)
    {
        if (!ZonePacket.TryParseMulticastRelay(packet, out int count, out ReadOnlySpan<byte> idsBytes, out ReadOnlySpan<byte> payload))
            return false;
        if (count == 0 || payload.Length < PacketIO.HeaderSize || PacketIO.ReadSize(payload) != payload.Length)
            return false;

        List<GatewayClientSession> recipients = t_recipients ??= new List<GatewayClientSession>();
        recipients.Clear();
        for (int i = 0; i < count; ++i)
        {
            ulong id = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(idsBytes.Slice(i * sizeof(ulong)));
            if (_clients.TryGetValue(id, out GatewayClientSession? c))
                recipients.Add(c);
        }

        // 수신자가 여럿이면 송신 버퍼를 한 번만 만들어 공유한다(확정 후 불변이라 여러 세션에 안전하다).
        uint zoneId = session.ZoneId;
        SendBuffer? shared = null;
        foreach (GatewayClientSession c in recipients)
        {
            if (c.ZoneId != zoneId)
                continue;
            shared ??= SendBufferManager.Create(payload);
            c.Send(shared);
        }
        recipients.Clear();     // 세션 수명을 이 호출 밖으로 끌고 가지 않는다
        return true;
    }

    // ------------------------------------------------------------------------------------------------
    // 도우미
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// 클라이언트 인증. [임시] 현재는 항상 통과시킨다. 실제 서비스에서는 로그인 서버가 발급한 인증 토큰(예: Redis의 단기 토큰)을
    /// 검증하고, 토큰에 묶인 캐릭터 ID와 charId가 일치하는지 확인해야 한다. 구현하기 전까지 클라이언트 포트를 외부에 공개하면 안 된다.
    /// </summary>
    private static bool AuthenticateClient(GatewayClientSession client, ulong charId) => true;

    /// <summary>클라이언트의 존 점유를 해제하고, 존 서버에 퇴장을 통지하며, 정원 슬롯을 반환한다.</summary>
    /// <returns>해제 전 존 ID (존에 없었으면 0)</returns>
    private uint DetachFromZone(GatewayClientSession client)
    {
        ulong seq = client.ZoneSeq;                 // ClearZone()이 지우기 전에 읽는다
        uint zoneId = client.ClearZone();
        if (zoneId == 0)
            return 0;

        ZoneNode? node = _router.Find(zoneId);
        if (node == null)
            return zoneId;

        // 입장했던 노드가 이미 새 노드로 교체됐다면 정원 슬롯도 존 서버 세션도 이 클라이언트와 무관하다.
        // (새 노드의 슬롯을 반환하면 다른 사용자의 자리를 훔치게 된다)
        if (seq != 0 && node.Seq != seq)
            return zoneId;

        ZoneRouter.ReleaseSlot(node);

        ZoneLeaveReq leave = new ZoneLeaveReq { ClientSessionId = client.ClientSessionId, CharId = client.CharId };
        node.Session.SendPacket(leave);
        return zoneId;
    }

    private GatewayClientSession? FindClient(ulong clientSessionId)
        => _clients.TryGetValue(clientSessionId, out GatewayClientSession? c) ? c : null;

    private static void SendEnterZoneRes(GatewayClientSession client, GatewayResult reason, uint zoneId, float spawnX, float spawnY)
    {
        GwEnterZoneRes res = new GwEnterZoneRes
        {
            Success = (byte)(reason == GatewayResult.Ok ? 1 : 0),
            Reason = (byte)reason,
            ZoneId = zoneId,
            SpawnX = spawnX,
            SpawnY = spawnY,
        };
        client.SendPacket(res);
    }

    private static void SendZoneLost(GatewayClientSession client, uint zoneId)
    {
        GwZoneLostNotify n = new GwZoneLostNotify { ZoneId = zoneId };
        client.SendPacket(n);
    }

    // ------------------------------------------------------------------------------------------------
    // 감시 스레드
    // ------------------------------------------------------------------------------------------------

    private void MaintenanceLoop()
    {
        while (true)
        {
            lock (_maintenanceLock)
            {
                if (_maintenanceStop)
                    return;
                Monitor.Wait(_maintenanceLock, MaintenanceTickMs);
                if (_maintenanceStop)
                    return;
            }
            CheckZoneHeartbeats();
        }
    }

    /// <summary>
    /// 하트비트가 HeartbeatTimeoutMs 이상 끊긴 존 서버의 연결을 끊는다. 연결을 끊으면 OnDisconnected → OnZoneSessionClosed 경로로
    /// 라우팅 제거와 클라이언트 정리가 이어진다. 종료 처리가 끝나기 전에 같은 세션을 반복해서 끊지 않도록 끊은 노드의 기준 시각을 갱신한다.
    /// </summary>
    internal void CheckZoneHeartbeats()
    {
        long now = Environment.TickCount64;
        foreach (ZoneNode node in _router.Snapshot())
        {
            if (now - node.LastHeartbeat <= (long)_options.HeartbeatTimeoutMs)
                continue;
            LogManager.Warning("[GatewayServerMain] zone heartbeat timeout. zoneId={0}", node.ZoneId);
            node.TouchHeartbeat(now);
            node.Session.Disconnect(CloseReason.Local);
        }
    }
}
