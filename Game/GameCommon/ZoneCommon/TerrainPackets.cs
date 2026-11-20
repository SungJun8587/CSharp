// TerrainPackets.cs : 시험장(존 0) 지형 치트 패킷. Packet.xlsx(생성 코드)가 아니라 손으로 쓴 파일이다.
// 타입 번호는 결투(2030~2031), 경험치/재화(2032~2033) 다음인 2034~2035를 쓴다. Gateway는 2000 이상을 해석하지 않고 중계한다.
using System;
using System.Buffers.Binary;
using Game.Generated;

namespace Game.ZoneCommon;

public static class TerrainPacketType
{
    /// <summary>Client -> Zone: 시험장의 지형을 바꿔 달라는 요청(치트).</summary>
    public const ushort Req = 2034;

    /// <summary>Zone -> Client: 시험장의 현재 지형(바뀔 때 존 전원에게, 입장 때 본인에게).</summary>
    public const ushort Notify = 2035;
}

/// <summary>지형 변경 요청. 시험장 서버만 받고, 다른 존이나 치트를 켜지 않은 서버는 무시한다. [헤더 4][terrain u32]</summary>
public struct ZcTerrainReq : IPacket
{
    public const int Size = 8;
    static int IPacket.PacketSize => Size;
    public uint Terrain;        // 장애물을 가져올 존 번호(1~10)

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, TerrainPacketType.Req);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(4), Terrain);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcTerrainReq v)
    {
        if (p.Length < Size) { v = default; return false; }
        v = new ZcTerrainReq { Terrain = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4)) };
        return true;
    }
}

/// <summary>시험장의 현재 지형 통지. [헤더 4][terrain u32]</summary>
public struct ZcTerrainNotify : IPacket
{
    public const int Size = 8;
    static int IPacket.PacketSize => Size;
    public uint Terrain;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, TerrainPacketType.Notify);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(4), Terrain);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcTerrainNotify v)
    {
        if (p.Length < Size) { v = default; return false; }
        v = new ZcTerrainNotify { Terrain = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4)) };
        return true;
    }
}
