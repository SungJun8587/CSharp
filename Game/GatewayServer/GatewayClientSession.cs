// GatewayClientSession.cs : 게임 클라이언트가 Gateway로 맺는 연결 세션(서버 측 세션).
using System;
using System.Threading;
using Core.Net;
using Core.Util;
using Game.Generated;

namespace Game.GatewayServer;

/// <summary>
/// 로그인 상태와 현재 존 정보를 이 세션 안에서만 관리하고, 로그인/입장/퇴장/중계 처리는 GatewayServerMain에 위임한다.
/// 존 서버 세션의 스레드가 입장 응답을 처리하며 존 상태를 바꿀 수 있으므로 존 관련 상태는 모두 원자적으로 다룬다.
/// ZoneId의 의미: 0이면 존 없음, 0이 아니면 "그 존의 정원 슬롯을 점유 중"(입장 진행 중 포함). IsEnterConfirmed가 true여야 입장 확정이다.
/// ZoneSeq는 입장한 존 노드의 등록 순번이다. 같은 zoneId의 존 서버가 빠르게 재시작해 노드가 교체돼도 이전 노드의 클라이언트만 골라 정리한다.
/// </summary>
public sealed class GatewayClientSession : PacketSession
{
    private readonly GatewayServerMain _server;
    private int _loggedIn;
    private long _charId;
    private int _zoneId;
    private int _enterConfirmed;
    private long _zoneSeq;
    private GatewayZoneSession? _zoneSession;       // 입장한 존 서버 연결 세션 (입장 처리 후 중계 경로에서 읽는다)
    private SlidingWindowCounter? _pktLimiter;         // 수신 속도 제한 (수신 처리 전용, 처음 필요할 때 만든다)

    public GatewayClientSession(GatewayServerMain server) : base(RecvUnitBytes)
    {
        _server = server;
    }

    /// <summary>존 서버와 주고받는 클라이언트 식별자(Gateway 세션 ID).</summary>
    public ulong ClientSessionId => (ulong)SessionId;

    public bool IsLoggedIn => Volatile.Read(ref _loggedIn) != 0;

    /// <summary>로그인한 캐릭터 ID. 로그인 전이면 0.</summary>
    public ulong CharId => (ulong)Interlocked.Read(ref _charId);

    /// <summary>로그인 완료 상태를 반영한다. 로그인 핸들러만 호출한다.</summary>
    internal void MarkLoggedIn(ulong charId)
    {
        Interlocked.Exchange(ref _charId, (long)charId);
        Volatile.Write(ref _loggedIn, 1);
    }

    /// <summary>점유 중인 존 ID. 0이면 존에 없음.</summary>
    public uint ZoneId => (uint)Volatile.Read(ref _zoneId);

    /// <summary>입장한 존 노드의 등록 순번. 존에 없으면 0.</summary>
    public ulong ZoneSeq => (ulong)Interlocked.Read(ref _zoneSeq);

    public bool IsEnterConfirmed => Volatile.Read(ref _enterConfirmed) != 0;

    internal void SetEnterConfirmed(bool confirmed) => Volatile.Write(ref _enterConfirmed, confirmed ? 1 : 0);

    /// <summary>입장한 존 노드의 등록 순번과 연결 세션을 기록한다. 입장 요청을 처리하는 수신 스레드에서만 호출한다.</summary>
    internal void SetZoneRoute(ulong seq, GatewayZoneSession session)
    {
        Volatile.Write(ref _zoneSession, session);
        Interlocked.Exchange(ref _zoneSeq, (long)seq);
    }

    internal GatewayZoneSession? GetZoneSession() => Volatile.Read(ref _zoneSession);

    /// <summary>존 슬롯 점유를 시작한다. 이미 존에 있거나 입장이 진행 중이면 실패한다.</summary>
    internal bool TryAssignZone(uint zoneId)
    {
        if (Interlocked.CompareExchange(ref _zoneId, (int)zoneId, 0) != 0)
            return false;
        Volatile.Write(ref _enterConfirmed, 0);
        Interlocked.Exchange(ref _zoneSeq, 0);
        return true;
    }

    /// <summary>존 점유를 해제하고 해제 전 존 ID를 돌려준다(0이면 점유 중이 아니었음).</summary>
    internal uint ClearZone()
    {
        uint zoneId = (uint)Interlocked.Exchange(ref _zoneId, 0);
        Volatile.Write(ref _enterConfirmed, 0);
        Interlocked.Exchange(ref _zoneSeq, 0);
        return zoneId;
    }

    /// <summary>점유 중인 존이 zoneId와 같을 때만 점유를 해제한다.</summary>
    internal bool ClearZoneIf(uint zoneId)
    {
        if (Interlocked.CompareExchange(ref _zoneId, 0, (int)zoneId) != (int)zoneId)
            return false;
        Volatile.Write(ref _enterConfirmed, 0);
        Interlocked.Exchange(ref _zoneSeq, 0);
        return true;
    }

    /// <summary>패킷 한 건을 처리해도 되는지 판정한다(최근 1초 슬라이딩 윈도우에 maxPerSec건, 0이면 제한 없음). 수신 처리는 세션당 직렬이라 동기화가 필요 없다.</summary>
    internal bool AllowPacket(uint maxPerSec)
    {
        if (maxPerSec == 0)
            return true;

        _pktLimiter ??= new SlidingWindowCounter((int)Math.Min(maxPerSec, int.MaxValue), 1000);
        return _pktLimiter.TryAcquire(Environment.TickCount64);
    }

    protected override void OnConnected() => _server.OnClientConnected(this);

    protected override void OnDisconnected(CloseReason reason) => _server.OnClientDisconnected(this);

    protected override int MaxRecvPacketSize => ProtocolConst.MaxClientPacketBytes;

    private const int RecvUnitBytes = 1024;     // 수신 버퍼 단위(용량 16KB >= 클라이언트 패킷 상한). 세션 수천 개가 계속 쥐어도 부담이 작고 풀 대여/반납이 없다


    protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
    {
        if (!_server.OnClientPacket(this, packet))
            Disconnect(CloseReason.ProtocolError);
    }
}
