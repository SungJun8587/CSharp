// PlayerStatePackets.cs : 존 이동/재접속 때 플레이어 상태를 존 서버와 Gateway 사이에서 주고받는 서버 간 패킷.
// Packet.xlsx(생성 코드)가 아니라 손으로 쓴 파일이다. 타입 번호는 생성된 ZoneServerPacketType(1001~1010)과 겹치지 않게 1100번대를 쓴다.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Game.Generated;

namespace Game.ZoneCommon;

/// <summary>존 이동 상태 패킷의 타입 번호 (Gateway &lt;-&gt; ZoneServer 대역 1000~1999).</summary>
public static class PlayerStatePacketType
{
    /// <summary>Zone -> Gateway: 플레이어가 존을 떠날 때의 상태. Gateway가 잠시 보관한다.</summary>
    public const ushort Save = 1100;

    /// <summary>Gateway -> Zone: 보관해 둔 상태. 바로 뒤에 오는 ZoneEnterReq의 입장에 적용된다.</summary>
    public const ushort Restore = 1101;
}

/// <summary>존을 넘어 가져가는 플레이어 상태. 체력/MP, 레벨/경험치, 재화, 가방, 관문 도착 지점이다.</summary>
public sealed class PlayerState
{
    public const int MaxItems = 24;     // GameConsts.MaxInventorySlots 이상이어야 한다
    public const int MaxCurrencies = 4; // 한 캐릭터가 가질 수 있는 재화 종류 수
    public const int MaxSkills = 32;    // 한 캐릭터가 가질 수 있는 스킬 수(영웅 4종 x 8)
    public const int MaxEquipped = 8;   // 장착 칸 수(1~6 고정 칸, 7~8 상점 칸)

    public ulong CharId;
    public uint HeroId;
    public bool HasVitals;              // false면 체력/MP를 가져가지 않는다(죽어서 나간 경우 등)
    public int Hp;
    public int Mp;
    public uint ArrivalZone;            // 0이면 도착 지점 없음. 관문 안쪽에서 나갔다면 그 관문이 이어지는 존 번호 + 1 (0 번 존(시험장)과 '없음'을 구별하려고 1을 더한다)
    public float ArrivalX;
    public float ArrivalY;
    public uint Level;                  // 0이면 저장된 레벨 없음(1로 취급)
    public uint Exp;                    // 현재 레벨에서 쌓은 경험치
    public readonly List<KeyValuePair<uint, uint>> Wallet = new List<KeyValuePair<uint, uint>>();      // (재화 ID, 보유량)
    public readonly List<KeyValuePair<uint, uint>> Items = new List<KeyValuePair<uint, uint>>();
    public readonly List<KeyValuePair<uint, byte>> Skills = new List<KeyValuePair<uint, byte>>();   // 보유 스킬 (스킬 ID, 장착 칸 1~MaxEquipped, 0 이면 장착 안 함). 칸 번호는 영웅별로 센다
}

/// <summary>Zone -> Gateway 상태 저장 통지.</summary>
public struct ZoneStateSave : IPacket
{
    public PlayerState State;

    public static int PacketSize => PlayerStateWire.Size;

    public readonly int Write(Span<byte> packet) => PlayerStateWire.Write(packet, PlayerStatePacketType.Save, State);

    public static bool TryRead(ReadOnlySpan<byte> packet, out ZoneStateSave v)
    {
        bool ok = PlayerStateWire.TryRead(packet, out PlayerState? s);
        v = new ZoneStateSave { State = s! };
        return ok;
    }
}

/// <summary>Gateway -> Zone 상태 복원 요청.</summary>
public struct ZoneStateRestore : IPacket
{
    public PlayerState State;

    public static int PacketSize => PlayerStateWire.Size;

    public readonly int Write(Span<byte> packet) => PlayerStateWire.Write(packet, PlayerStatePacketType.Restore, State);

    public static bool TryRead(ReadOnlySpan<byte> packet, out ZoneStateRestore v)
    {
        bool ok = PlayerStateWire.TryRead(packet, out PlayerState? s);
        v = new ZoneStateRestore { State = s! };
        return ok;
    }
}

/// <summary>두 패킷이 같은 배치를 쓴다. [헤더 4][charId u64][heroId u32][hp i32][mp i32][flags u8][itemCount u8][level u16][arrivalZone u32][arrivalX f32][arrivalY f32][items 24 x (id u32, count u32)].</summary>
internal static class PlayerStateWire
{
    public const int Size = 4 + 8 + 4 + 4 + 4 + 1 + 1 + 2 + 4 + 4 + 4 + PlayerState.MaxItems * 8 + 4 + 4 + PlayerState.MaxCurrencies * 8 + 4 + PlayerState.MaxSkills * 4;      // 404 (= 272 + 스킬 블록 132) / 272 (items 뒤: exp u32, walletCount u8 + pad 3, wallet 4 x (id u32, amount u32))

    private const int WalletOffset = 44 + PlayerState.MaxItems * 8;
    private const int SkillOffset = WalletOffset + 4 + PlayerState.MaxCurrencies * 8;       // 스킬 블록: [count u8][pad 3][(skillId u16, slot u8, pad u8) x MaxSkills]

    public static int Write(Span<byte> p, ushort type, PlayerState s)
    {
        PacketIO.WriteHeader(p, Size, type);
        p.Slice(4, Size - 4).Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(4), s.CharId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(12), s.HeroId);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(16), s.Hp);
        BinaryPrimitives.WriteInt32LittleEndian(p.Slice(20), s.Mp);
        int n = Math.Min(s.Items.Count, PlayerState.MaxItems);
        p[24] = (byte)(s.HasVitals ? 1 : 0);
        p[25] = (byte)n;
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(26), (ushort)Math.Min(s.Level, ushort.MaxValue));
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(28), s.ArrivalZone);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(32), s.ArrivalX);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(36), s.ArrivalY);
        for (int i = 0; i < n; ++i)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(40 + i * 8), s.Items[i].Key);
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(44 + i * 8), s.Items[i].Value);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(40 + PlayerState.MaxItems * 8), s.Exp);
        int wn = Math.Min(s.Wallet.Count, PlayerState.MaxCurrencies);
        p[WalletOffset] = (byte)wn;
        for (int i = 0; i < wn; ++i)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(WalletOffset + 4 + i * 8), s.Wallet[i].Key);
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(WalletOffset + 8 + i * 8), s.Wallet[i].Value);
        }
        int sn = 0;
        for (int i = 0; i < s.Skills.Count && sn < PlayerState.MaxSkills; ++i)
        {
            if (s.Skills[i].Key == 0 || s.Skills[i].Key > ushort.MaxValue)
                continue;
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(SkillOffset + 4 + sn * 4), (ushort)s.Skills[i].Key);
            p[SkillOffset + 6 + sn * 4] = s.Skills[i].Value;
            ++sn;
        }
        p[SkillOffset] = (byte)sn;
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out PlayerState? s)
    {
        s = null;
        if (p.Length < Size)
            return false;
        int n = p[25];
        if (n > PlayerState.MaxItems || p[WalletOffset] > PlayerState.MaxCurrencies || p[SkillOffset] > PlayerState.MaxSkills)
            return false;
        float ax = BinaryPrimitives.ReadSingleLittleEndian(p.Slice(32)), ay = BinaryPrimitives.ReadSingleLittleEndian(p.Slice(36));
        if (!float.IsFinite(ax) || !float.IsFinite(ay))
            return false;

        PlayerState v = new PlayerState
        {
            CharId = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(4)),
            HeroId = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(12)),
            Hp = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(16)),
            Mp = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(20)),
            HasVitals = (p[24] & 1) != 0,
            ArrivalZone = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(28)),
            ArrivalX = ax,
            ArrivalY = ay,
            Level = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(26)),
            Exp = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(40 + PlayerState.MaxItems * 8)),
        };
        for (int i = 0; i < n; ++i)
            v.Items.Add(new KeyValuePair<uint, uint>(BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(40 + i * 8)), BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(44 + i * 8))));
        for (int i = 0; i < p[WalletOffset]; ++i)
            v.Wallet.Add(new KeyValuePair<uint, uint>(BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(WalletOffset + 4 + i * 8)), BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(WalletOffset + 8 + i * 8))));
        for (int i = 0; i < p[SkillOffset]; ++i)
            v.Skills.Add(new KeyValuePair<uint, byte>(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(SkillOffset + 4 + i * 4)), p[SkillOffset + 6 + i * 4]));
        s = v;
        return true;
    }
}
