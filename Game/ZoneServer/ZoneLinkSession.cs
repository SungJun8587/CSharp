// ZoneLinkSession.cs : 존 서버가 Gateway로 맺는 서버 간 연결 세션(클라이언트 측 세션).
using System;
using System.Net.Sockets;
using Core.Net;
using Game.Generated;

namespace Game.ZoneServer;

/// <summary>
/// 프레이밍만 알고, 연결/종료/패킷 처리는 ZoneServerMain에 위임한다.
/// 패킷 처리가 false(프로토콜 위반)를 돌려주면 연결을 끊고 링크 스레드가 재접속한다.
/// </summary>
public sealed class ZoneLinkSession : PacketSession
{
    private readonly ZoneServerMain _server;

    public ZoneLinkSession(ZoneServerMain server)
    {
        _server = server;
    }

    protected override void OnConnected() => _server.OnLinkConnected(this);

    protected override void OnDisconnected(CloseReason reason) => _server.OnLinkDisconnected(this);

    protected override void OnConnectFailed(SocketError error) => _server.OnLinkDisconnected(this);

    protected override int MaxRecvPacketSize => ProtocolConst.MaxServerPacketBytes;

    protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
    {
        if (!_server.OnGatewayPacket(packet))
            Disconnect(CloseReason.ProtocolError);
    }
}
