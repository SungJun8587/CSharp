// SkillBookPackets.cs : 스킬 보유/구매/장착 패킷. Packet.xlsx(생성 코드)가 아니라 손으로 쓴 파일이다.
// 타입 번호는 지형(2034~2035) 다음인 2036~2039를 쓴다. Gateway는 2000 이상을 해석하지 않고 중계한다.
using System;
using System.Buffers.Binary;
using Game.Generated;

namespace Game.ZoneCommon;

public static class SkillBookPacketType
{
    /// <summary>Zone -> Client: 내 영웅 스킬의 보유 현황과 장착 칸(입장 때, 바뀔 때마다 본인에게).</summary>
    public const ushort Notify = 2036;

    /// <summary>Client -> Zone: 스킬 구매 요청.</summary>
    public const ushort BuyReq = 2037;

    /// <summary>Client -> Zone: 스킬 장착/해제/자리 바꿈 요청.</summary>
    public const ushort EquipReq = 2038;

    /// <summary>Zone -> Client: 구매/장착 요청의 결과(본인에게).</summary>
    public const ushort Result = 2039;
}

public enum SkillOp : byte
{
    Buy = 1,
    Equip = 2,
}

public enum SkillOpResult : byte
{
    Ok = 0,
    Unknown = 1,        // 내 영웅의 스킬이 아니다
    NotOwned = 2,       // 보유하지 않은 스킬은 장착할 수 없다
    AlreadyOwned = 3,   // 이미 가지고 있다
    LevelTooLow = 4,    // 개방 레벨에 이르지 못했다
    NotEnough = 5,      // 재화가 모자란다
    BadSlot = 6,        // 장착 칸 번호가 범위를 벗어났다
    NotForSale = 7,     // 레벨이 되면 자동으로 지급되는 스킬이라 살 수 없다
    Dead = 8,           // 사망 중에는 바꿀 수 없다
    FixedSlot = 9,      // 레벨로 지급되는 스킬은 고정 칸이라 옮기거나 뺄 수 없다
    SlotLocked = 10,    // 그 칸이 아직 열리지 않았다(레벨 부족)
}

/// <summary>
/// 내 영웅의 스킬 보유 현황. 보유한 스킬마다 장착 칸(1~MaxEquipped, 0이면 장착 안 함)을 담는다.
/// [헤더 4][count u8][칸 7이 열리는 레벨 u8][칸 8이 열리는 레벨 u8][pad 1][(skillId u16, slot u8, pad u8) x MaxEntries]
/// </summary>
public struct ZcSkillBookNotify : IPacket
{
    public const int MaxEntries = 8;
    public const int Size = 8 + MaxEntries * 4;

    public int Count;
    public byte ShopSlot7Level, ShopSlot8Level;     // 상점 칸 7, 8 이 열리는 레벨(255 이하)
    public ushort[] SkillIds;       // Count 개
    public byte[] Slots;            // Count 개

    public static int PacketSize => Size;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, SkillBookPacketType.Notify);
        p.Slice(4, Size - 4).Clear();
        int n = Math.Min(Count, MaxEntries);
        p[4] = (byte)n;
        p[5] = ShopSlot7Level;
        p[6] = ShopSlot8Level;
        for (int i = 0; i < n; ++i)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(8 + i * 4), SkillIds[i]);
            p[10 + i * 4] = Slots[i];
        }
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcSkillBookNotify v)
    {
        v = default;
        if (p.Length < Size || p[4] > MaxEntries)
            return false;
        int n = p[4];
        v.Count = n;
        v.ShopSlot7Level = p[5];
        v.ShopSlot8Level = p[6];
        v.SkillIds = new ushort[n];
        v.Slots = new byte[n];
        for (int i = 0; i < n; ++i)
        {
            v.SkillIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(8 + i * 4));
            v.Slots[i] = p[10 + i * 4];
        }
        return true;
    }
}

/// <summary>스킬 구매 요청. [헤더 4][skillId u32]</summary>
public struct ZcSkillBuyReq : IPacket
{
    public const int Size = 8;
    static int IPacket.PacketSize => Size;
    public uint SkillId;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, SkillBookPacketType.BuyReq);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(4), SkillId);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcSkillBuyReq v)
    {
        if (p.Length < Size) { v = default; return false; }
        v = new ZcSkillBuyReq { SkillId = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4)) };
        return true;
    }
}

/// <summary>
/// 스킬 장착 요청. slot 이 0이면 해제, 1~MaxEquipped 면 그 칸에 장착한다. 그 칸에 다른 스킬이 있으면 서로 자리를 바꾼다
/// (이 스킬이 장착돼 있지 않았다면 밀려난 스킬은 해제된다). [헤더 4][skillId u32][slot u8][pad 3]
/// </summary>
public struct ZcSkillEquipReq : IPacket
{
    public const int Size = 12;
    static int IPacket.PacketSize => Size;
    public uint SkillId;
    public byte Slot;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, SkillBookPacketType.EquipReq);
        p.Slice(4, Size - 4).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(4), SkillId);
        p[8] = Slot;
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcSkillEquipReq v)
    {
        if (p.Length < Size) { v = default; return false; }
        v = new ZcSkillEquipReq { SkillId = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4)), Slot = p[8] };
        return true;
    }
}

/// <summary>구매/장착 요청의 결과. 성공하면 이어서 ZcSkillBookNotify(와 재화 통지)가 온다. [헤더 4][op u8][result u8][pad 2][skillId u32]</summary>
public struct ZcSkillOpResNotify : IPacket
{
    public const int Size = 12;
    static int IPacket.PacketSize => Size;
    public byte Op;
    public byte Result;
    public uint SkillId;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, SkillBookPacketType.Result);
        p.Slice(4, Size - 4).Clear();
        p[4] = Op;
        p[5] = Result;
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(8), SkillId);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcSkillOpResNotify v)
    {
        if (p.Length < Size) { v = default; return false; }
        v = new ZcSkillOpResNotify { Op = p[4], Result = p[5], SkillId = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(8)) };
        return true;
    }
}
