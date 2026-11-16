// GatewayZoneSession.cs : Gateway가 존 서버로부터 받는 서버 간 연결 세션(서버 측 세션).
using System;
using System.Threading;
using Core.Net;
using Game.Protocol;

namespace Game.GatewayServer;

/// <summary>
/// 존 서버가 ZoneRegisterReq로 등록하기 전까지는 등록 요청 외의 어떤 패킷도 처리하지 않는다.
/// 연결 종료 시 라우팅 테이블 제거와 해당 존의 클라이언트 정리를 서버 파사드에 위임한다.
/// </summary>
public sealed class GatewayZoneSession : PacketSession
{
    private readonly GatewayServerMain _server;
    private int _zoneId;
    private ZoneNode? _node;

    public GatewayZoneSession(GatewayServerMain server)
    {
        _server = server;
    }

    /// <summary>이 세션이 등록한 존 ID. 등록 전이면 0.</summary>
    public uint ZoneId => (uint)Volatile.Read(ref _zoneId);

    /// <summary>이 세션이 등록한 라우팅 노드. 등록 전이면 null.</summary>
    public ZoneNode? Node => Volatile.Read(ref _node);

    /// <summary>등록 결과를 반영한다. 등록 핸들러만 호출한다.</summary>
    internal void BindZone(uint zoneId, ZoneNode node)
    {
        Volatile.Write(ref _node, node);
        Volatile.Write(ref _zoneId, (int)zoneId);
    }

    protected override void OnDisconnected(CloseReason reason)
    {
        if (ZoneId != 0)
            _server.OnZoneSessionClosed(this);
    }

    protected override int MaxRecvPacketSize => ProtocolConst.MaxServerPacketBytes;

    protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
    {
        if (!_server.OnZonePacket(this, packet))
            Disconnect(CloseReason.ProtocolError);
    }
}
