//***************************************************************************
// SkillBookPacket.cs : 스킬 보유/구매/장착 패킷. 서버의 SkillBookPackets.cs 와 같은 레이아웃이다(번호 2036~2039).
// 생성 코드(Packet.xlsx)가 아니라 손으로 쓴 패킷이라 ZonePacketType 열거형에는 없다.
//***************************************************************************
using System;

namespace GameViewer.Core.Protocol
{
    /// <summary>스킬 구매/장착 결과 사유(서버 SkillOpResult 와 같은 값).</summary>
    public static class SkillOpResult
    {
        public const byte Ok = 0, Unknown = 1, NotOwned = 2, AlreadyOwned = 3, LevelTooLow = 4, NotEnough = 5, BadSlot = 6, NotForSale = 7, Dead = 8, FixedSlot = 9, SlotLocked = 10;

        public static string Text(byte r)
        {
            switch (r)
            {
                case Ok: return "";
                case Unknown: return "이 영웅의 스킬이 아닙니다";
                case NotOwned: return "보유하지 않은 스킬입니다";
                case AlreadyOwned: return "이미 가지고 있습니다";
                case LevelTooLow: return "레벨이 모자랍니다";
                case NotEnough: return "재화가 모자랍니다";
                case BadSlot: return "장착할 수 없는 칸입니다";
                case NotForSale: return "레벨이 되면 자동으로 얻는 스킬입니다";
                case Dead: return "사망 중에는 바꿀 수 없습니다";
                case FixedSlot: return "레벨로 얻는 스킬은 고정 칸입니다";
                case SlotLocked: return "아직 열리지 않은 칸입니다";
            }
            return "실패(" + r + ")";
        }
    }

    /// <summary>내 영웅 스킬의 보유 현황(Zone -> Client, 2036). [헤더 4][count u8][pad 3][(skillId u16, slot u8, pad u8) x 8]</summary>
    public sealed class ZcSkillBookNotify
    {
        public const ushort Type = 2036;
        public const int MaxEntries = 8;
        public const int Size = 8 + MaxEntries * 4;

        public byte ShopSlot7Level, ShopSlot8Level;         // 상점 칸 7, 8 이 열리는 레벨
        public ushort[] SkillIds = new ushort[0];
        public byte[] Slots = new byte[0];

        public static ZcSkillBookNotify Parse(byte[] d)
        {
            PacketReader r = new PacketReader(d, ProtocolConst.HeaderSize);
            int n = r.U8();
            if (n > MaxEntries) throw new PacketFormatException("skill book count " + n);
            byte l7 = r.U8(), l8 = r.U8(); r.U8();
            ZcSkillBookNotify p = new ZcSkillBookNotify { SkillIds = new ushort[n], Slots = new byte[n], ShopSlot7Level = l7, ShopSlot8Level = l8 };
            for (int i = 0; i < n; ++i) { p.SkillIds[i] = r.U16(); p.Slots[i] = r.U8(); r.U8(); }
            return p;
        }

        public byte[] ToBytes()
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U8((byte)SkillIds.Length); w.U8(ShopSlot7Level); w.U8(ShopSlot8Level); w.U8(0);
            for (int i = 0; i < MaxEntries; ++i)
            {
                if (i < SkillIds.Length) { w.U16(SkillIds[i]); w.U8(Slots[i]); w.U8(0); }
                else w.U32(0);
            }
            return w.Finish();
        }
    }

    /// <summary>스킬 구매 요청(Client -> Zone, 2037). [헤더 4][skillId u32]</summary>
    public static class ZcSkillBuyReq
    {
        public const ushort Type = 2037;
        public const int Size = 8;

        public static byte[] ToBytes(uint skillId)
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U32(skillId);
            return w.Finish();
        }
    }

    /// <summary>스킬 장착 요청(Client -> Zone, 2038). slot 0 이면 해제. [헤더 4][skillId u32][slot u8][pad 3]</summary>
    public static class ZcSkillEquipReq
    {
        public const ushort Type = 2038;
        public const int Size = 12;

        public static byte[] ToBytes(uint skillId, byte slot)
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U32(skillId); w.U8(slot); w.U8(0); w.U16(0);
            return w.Finish();
        }
    }

    /// <summary>구매/장착 결과(Zone -> Client, 2039). [헤더 4][op u8][result u8][pad 2][skillId u32]</summary>
    public sealed class ZcSkillOpResNotify
    {
        public const ushort Type = 2039;
        public const int Size = 12;
        public const byte OpBuy = 1, OpEquip = 2;

        public byte Op, Result;
        public uint SkillId;

        public static ZcSkillOpResNotify Parse(byte[] d)
        {
            PacketReader r = new PacketReader(d, ProtocolConst.HeaderSize);
            ZcSkillOpResNotify p = new ZcSkillOpResNotify();
            p.Op = r.U8(); p.Result = r.U8(); r.U16(); p.SkillId = r.U32();
            return p;
        }

        public byte[] ToBytes()
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U8(Op); w.U8(Result); w.U16(0); w.U32(SkillId);
            return w.Finish();
        }
    }
}
