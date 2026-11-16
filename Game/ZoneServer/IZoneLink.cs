// IZoneLink.cs : 전투 존이 Gateway로 패킷을 내보내는 경로.
using System;

namespace Game.ZoneServer;

/// <summary>
/// 존이 쓰는 송신 경로. 구현체(ZoneServerMain)는 Gateway 연결이 없거나 끊겨 있으면 조용히 버린다.
/// 존 스레드에서 호출되므로 블로킹하면 안 된다.
/// </summary>
public interface IZoneLink
{
    /// <summary>Gateway에 서버 간 패킷(헤더 포함)을 보낸다.</summary>
    void SendToGateway(ReadOnlySpan<byte> packet);

    /// <summary>클라이언트 한 명에게 패킷(헤더 포함)을 중계한다.</summary>
    void SendToClient(ulong clientSessionId, ReadOnlySpan<byte> packet);

    /// <summary>같은 패킷을 여러 클라이언트에게 중계한다. 수신자가 많으면 구현체가 나눠 보낸다.</summary>
    void SendToClients(ReadOnlySpan<ulong> clientSessionIds, ReadOnlySpan<byte> packet);
}
