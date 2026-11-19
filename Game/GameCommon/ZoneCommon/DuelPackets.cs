// DuelPackets.cs : 결투 요청/통지 패킷. Packet.xlsx(생성 코드)가 아니라 손으로 쓴 파일이다.
// 타입 번호는 생성된 ZonePacketType(2001~2029) 다음인 2030번대를 쓴다. Gateway는 2000 이상을 해석하지 않고 중계하므로 그대로 오간다.
using System;
using System.Buffers.Binary;
using Game.Generated;

namespace Game.ZoneCommon;

/// <summary>결투 패킷의 타입 번호 (Client &lt;-&gt; Zone 대역 2000~).</summary>
public static class DuelPacketType
{
    /// <summary>Client -> Zone: 결투 신청/수락/거절/취소(진행 중이면 기권).</summary>
    public const ushort Req = 2030;

    /// <summary>Zone -> Client: 결투 진행 상황(본인과 상대에게만).</summary>
    public const ushort Notify = 2031;
}

/// <summary>ZcDuelReq.Action 값.</summary>
public enum DuelAction : byte
{
    Request = 1,    // TargetId = 신청할 상대
    Accept = 2,     // TargetId = 신청한 사람
    Decline = 3,    // TargetId = 신청한 사람
    Cancel = 4,     // 신청/카운트다운 중이면 취소, 진행 중이면 기권(TargetId는 무시)
}

/// <summary>ZcDuelNotify.State 값.</summary>
public enum DuelState : byte
{
    Requested = 1,      // 받는 쪽: 신청이 왔다. OpponentId = 신청자, RemainMs = 응답 제한 시간
    RequestSent = 2,    // 신청한 쪽: 신청이 전달되었다. OpponentId = 상대
    Countdown = 3,      // 양쪽: 수락됨. RemainMs 뒤에 시작한다
    Started = 4,        // 양쪽: 시작. 이 순간부터 서로 공격할 수 있다
    Declined = 5,       // 신청한 쪽: 상대가 거절했다
    Cancelled = 6,      // 상대/본인이 취소했다(진행 중이면 기권, 이때 WinnerId = 남은 쪽)
    Expired = 7,        // 응답 제한 시간이 지났다 / 결투 시간이 다 되었다
    Ended = 8,          // 한쪽이 쓰러져 끝났다. WinnerId = 이긴 쪽
    OutOfRange = 9,     // 너무 멀어져 끝났다
    OpponentLeft = 10,  // 상대가 존을 떠났다
    RejectBusy = 11,    // 신청 실패: 본인이나 상대가 이미 결투 중/신청 중이다
    RejectNoTarget = 12,// 신청 실패: 상대가 없다(자기 자신 포함)
    RejectTooFar = 13,  // 신청 실패: 상대가 너무 멀다
    RejectDead = 14,    // 신청 실패: 본인이나 상대가 쓰러져 있다
    RejectNoRequest = 15,// 수락/거절할 신청이 없다
    ZoneRules = 16,     // 존 규칙 통지(입장 직후 본인에게). RemainMs의 비트: 1 = 자유 PvP, 2 = 결투 신청 가능
    RejectNotAllowed = 17,// 신청 실패: 이 존은 결투를 받지 않는다
}

/// <summary>Client -> Zone 결투 요청.</summary>
public struct ZcDuelReq : IPacket
{
    public const int Size = 13;
    static int IPacket.PacketSize => Size;
    public byte Action;
    public ulong TargetId;

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, DuelPacketType.Req);
        p[4] = Action;
        BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(5), TargetId);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcDuelReq v)
    {
        if (p.Length < Size) { v = default; return false; }
        v = new ZcDuelReq { Action = p[4], TargetId = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(5)) };
        return true;
    }
}

/// <summary>Zone -> Client 결투 진행 통지.</summary>
public struct ZcDuelNotify : IPacket
{
    public const int Size = 25;
    static int IPacket.PacketSize => Size;
    public byte State;
    public ulong OpponentId;
    public ulong WinnerId;      // Ended/Cancelled(기권)일 때만 의미 있다(0이면 승자 없음)
    public uint RemainMs;       // Requested/Countdown일 때 남은 시간

    public readonly int Write(Span<byte> p)
    {
        PacketIO.WriteHeader(p, Size, DuelPacketType.Notify);
        p[4] = State;
        BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(5), OpponentId);
        BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(13), WinnerId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(21), RemainMs);
        return Size;
    }

    public static bool TryRead(ReadOnlySpan<byte> p, out ZcDuelNotify v)
    {
        if (p.Length < Size) { v = default; return false; }
        v = new ZcDuelNotify
        {
            State = p[4],
            OpponentId = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(5)),
            WinnerId = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(13)),
            RemainMs = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(21)),
        };
        return true;
    }
}
