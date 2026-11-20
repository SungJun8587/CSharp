// ExpPackets.cs : 경험치/레벨 통지 패킷(Zone -> Client, 본인에게만).
// Packet.xlsx(생성 코드)가 아니라 손으로 쓴 파일이다. 타입 번호는 생성된 ZonePacketType(2001~2029)과 결투(2030~2031) 다음인 2032번을 쓴다.
// Gateway는 2000 이상을 해석하지 않고 중계하므로 그대로 오간다.
using System;
using System.Buffers.Binary;
using Game.Generated;

namespace Game.ZoneCommon;

public static class ExpPacketType
{
    /// <summary>Zone -> Client: 내 레벨과 경험치 현황.</summary>
    public const ushort Notify = 2032;
}

/// <summary>내 레벨/경험치. 입장 때와 경험치를 얻을 때마다 보낸다. [헤더 4][level u16][flags u8][pad u8][exp u32][expToNext u32][gained u32]</summary>
public struct ZcExpNotify : IPacket
{
    public const int Size = 20;
    public const byte FlagLevelUp = 1;      // 이번에 레벨이 올랐다
    public const byte FlagMaxLevel = 2;     // 최대 레벨이다(expToNext 는 0)

    public ushort Level;
    public byte Flags;
    public uint Exp;                        // 현재 레벨에서 쌓은 경험치
    public uint ExpToNext;                  // 다음 레벨까지 필요한 경험치(최대 레벨이면 0)
    public uint Gained;                     // 이번에 얻은 경험치(입장 때는 0)

    public static int PacketSize => Size;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, ExpPacketType.Notify);
        p.Slice(4, Size - 4).Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(4), Level);
        p[6] = Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(8), Exp);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(12), ExpToNext);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(16), Gained);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcExpNotify v)
    {
        if (p.Length < Size)
        {
            v = default;
            return false;
        }
        v = new ZcExpNotify
        {
            Level = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(4)),
            Flags = p[6],
            Exp = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(8)),
            ExpToNext = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(12)),
            Gained = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(16)),
        };
        return true;
    }
}

public static class CurrencyPacketType
{
    /// <summary>Zone -> Client: 내 재화 한 종류의 보유량.</summary>
    public const ushort Notify = 2033;
}

/// <summary>내 재화 보유량. 입장 때 정의된 재화마다 하나씩(gained 0), 재화를 얻을 때마다 보낸다. [헤더 4][currencyId u32][amount u32][gained u32]</summary>
public struct ZcCurrencyNotify : IPacket
{
    public const int Size = 16;

    public uint CurrencyId;
    public uint Amount;                     // 얻은 뒤의 보유량
    public uint Gained;                     // 이번에 얻은 양(입장 때는 0)

    public static int PacketSize => Size;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, CurrencyPacketType.Notify);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(4), CurrencyId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(8), Amount);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(12), Gained);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcCurrencyNotify v)
    {
        if (p.Length < Size)
        {
            v = default;
            return false;
        }
        v = new ZcCurrencyNotify
        {
            CurrencyId = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4)),
            Amount = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(8)),
            Gained = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(12)),
        };
        return true;
    }
}
