//***************************************************************************
// PacketTypes.cs : 손으로 쓰는 프로토콜 상수. 나머지(상수, enum, 패킷 번호, 패킷 클래스)는
// Generated/ProtocolTypes.gen.cs, Generated/Packets.gen.cs 가 GameData.xlsx의 Packet* 시트에서 만든다.
// 서버 헤더도 같은 시트에서 만들어지므로 따로 맞출 필요가 없다. (값은 와이어 프로토콜이므로 번호를 바꾸지 말 것)
//***************************************************************************
namespace GameViewer.Core.Protocol
{
    public static partial class ProtocolConst
    {
        public const int HeaderSize = 4;                   // PacketHeader { uint16 size; uint16 type; }
    }

    // 서버 구조체에서는 uint8 로 실리는 값이라 패킷 시트의 enum이 아니다.
    public enum EntityType : byte { Player = 0, Monster = 1, Npc = 2, Item = 3 }
}
