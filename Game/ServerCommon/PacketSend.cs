// PacketSend.cs : 패킷 구조체를 직렬화해서 세션으로 보내는 공용 도우미 (Gateway와 ZoneServer가 같이 쓴다).
using System;
using Core.Net;
using Game.Protocol;

namespace Game.ServerCommon;

public static class PacketSend
{
    /// <summary>
    /// 패킷을 스택 버퍼에 직렬화해서 보낸다. 버퍼는 구조체 크기만큼만 잡히고 힙 할당이 없다.
    /// 반환값은 Session.Send와 같다(큐에 넣었으면 true).
    /// </summary>
    public static bool SendPacket<T>(this Session session, in T packet) where T : struct, IPacket
    {
        Span<byte> buf = stackalloc byte[T.PacketSize];
        packet.Write(buf);
        return session.Send(buf);
    }
}
