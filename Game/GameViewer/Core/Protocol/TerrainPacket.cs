//***************************************************************************
// TerrainPacket.cs : 시험장(존 0) 지형 치트 패킷. 서버의 TerrainPackets.cs 와 같은 레이아웃이다.
// 생성 코드(Packet.xlsx)가 아니라 손으로 쓴 패킷이라 ZonePacketType 열거형에는 없고 번호(2034/2035)만 쓴다.
// [헤더 4][terrain u32]
//***************************************************************************
using System;

namespace GameViewer.Core.Protocol
{
    /// <summary>지형 변경 요청(Client -> Zone, 2034). 시험장 서버만 받는다.</summary>
    public static class ZcTerrainReq
    {
        public const ushort Type = 2034;
        public const int Size = 8;

        public static byte[] ToBytes(uint terrain)
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U32(terrain);
            return w.Finish();
        }
    }

    /// <summary>시험장의 현재 지형(Zone -> Client, 2035). 바뀔 때 존 전원에게, 입장 때 본인에게 온다.</summary>
    public sealed class ZcTerrainNotify
    {
        public const ushort Type = 2035;
        public const int Size = 8;

        public uint Terrain;

        public static ZcTerrainNotify Parse(byte[] d)
        {
            PacketReader r = new PacketReader(d, ProtocolConst.HeaderSize);
            return new ZcTerrainNotify { Terrain = r.U32() };
        }

        public byte[] ToBytes()
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U32(Terrain);
            return w.Finish();
        }
    }
}
