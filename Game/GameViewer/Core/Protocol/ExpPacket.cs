//***************************************************************************
// ExpPacket.cs : 경험치/레벨 통지(Zone -> Client, 본인에게만). 서버의 ExpPackets.cs(ZcExpNotify)와 같은 레이아웃이다.
// 생성 코드(Packet.xlsx)가 아니라 손으로 쓴 패킷이라 ZonePacketType 열거형에는 없고 번호(2032)만 쓴다.
// [헤더 4][level u16][flags u8][pad u8][exp u32][expToNext u32][gained u32]
//***************************************************************************
using System;

namespace GameViewer.Core.Protocol
{
    /// <summary>재화 보유량 통지(Zone -> Client, 본인에게만, 2033). [헤더 4][currencyId u32][amount u32][gained u32]. 손으로 쓴 패킷이다.</summary>
    public sealed class ZcCurrencyNotify
    {
        public const ushort Type = 2033;
        public const int Size = 16;
        public const uint Gold = 1;         // Currency 표에서 골드의 ID(샘플 데이터 기준). 이름은 클라이언트가 모른다

        public uint CurrencyId, Amount, Gained;

        public static ZcCurrencyNotify Parse(byte[] d)
        {
            PacketReader r = new PacketReader(d, ProtocolConst.HeaderSize);
            ZcCurrencyNotify p = new ZcCurrencyNotify();
            p.CurrencyId = r.U32(); p.Amount = r.U32(); p.Gained = r.U32();
            return p;
        }

        public byte[] ToBytes()
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U32(CurrencyId); w.U32(Amount); w.U32(Gained);
            return w.Finish();
        }
    }

    public sealed class ZcExpNotify
    {
        public const ushort Type = 2032;
        public const int Size = 20;
        public const byte FlagLevelUp = 1;
        public const byte FlagMaxLevel = 2;

        public uint Level;
        public byte Flags;
        public uint Exp, ExpToNext, Gained;

        public bool LevelUp { get { return (Flags & FlagLevelUp) != 0; } }
        public bool MaxLevel { get { return (Flags & FlagMaxLevel) != 0; } }

        public static ZcExpNotify Parse(byte[] d)
        {
            PacketReader r = new PacketReader(d, ProtocolConst.HeaderSize);
            ZcExpNotify p = new ZcExpNotify();
            p.Level = r.U16();
            p.Flags = r.U8();
            r.U8();
            p.Exp = r.U32();
            p.ExpToNext = r.U32();
            p.Gained = r.U32();
            return p;
        }

        public byte[] ToBytes()
        {
            PacketWriter w = new PacketWriter(Type, Size);
            w.U16((ushort)Level); w.U8(Flags); w.U8(0); w.U32(Exp); w.U32(ExpToNext); w.U32(Gained);
            return w.Finish();
        }
    }
}
