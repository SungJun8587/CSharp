// ZoneMetrics.cs : 존 서버 관측용 카운터. 존 스레드만 쓰고, 다른 스레드(하트비트/로그)는 Snapshot()으로 읽는다.
using System;
using System.Text;
using System.Threading;

namespace Game.ZoneServer;

/// <summary>카운터 스냅샷. 두 스냅샷의 차로 구간 속도를 구한다.</summary>
public readonly struct MetricsSnapshot
{
    public ulong InPackets { get; init; }               // 받은 클라이언트 패킷 (빈도 제한 전)
    public ulong InBytes { get; init; }
    public ulong[] InByKind { get; init; }              // RequestKind 순서
    public ulong OutPackets { get; init; }              // 보낸 패킷 수 (수신자별로 센 논리 수)
    public ulong OutBytes { get; init; }                // 보낸 바이트 (수신자별로 센 논리 크기)
    public ulong OutRelays { get; init; }               // Gateway로 실제 내려간 중계 메시지 수 (멀티캐스트는 수신자 수와 상관없이 하나)
    public ulong Broadcasts { get; init; }              // 멀티캐스트 횟수
    public ulong BroadcastRecipients { get; init; }     // 멀티캐스트 수신자 합계
    public ulong MaxRecipients { get; init; }           // 한 번의 멀티캐스트 최대 수신자 수
    public ulong Dropped { get; init; }                 // 빈도 제한으로 버린 요청
    public ulong Malformed { get; init; }               // 읽을 수 없거나 비정상 값이어서 버린 요청
    public ulong Kicks { get; init; }                   // 위반 누적으로 추방한 수
    public ulong ItemsPicked { get; init; }             // 줍기 성공
    public ulong PickupRejects { get; init; }
    public ulong Chats { get; init; }                   // 전달한 채팅 요청
    public ulong ChatRejects { get; init; }             // 거부한 채팅 요청           // 줍기 거부

    /// <summary>멀티캐스트 한 번당 평균 수신자 수.</summary>
    public double AvgRecipients => Broadcasts == 0 ? 0.0 : (double)BroadcastRecipients / Broadcasts;
}

/// <summary>존 하나의 송수신/위반 카운터.</summary>
public sealed class ZoneMetrics
{
    private ulong _inPackets, _inBytes, _outPackets, _outBytes, _outRelays;
    private ulong _broadcasts, _broadcastRecipients, _maxRecipients;
    private ulong _dropped, _malformed, _kicks, _itemsPicked, _pickupRejects, _chats, _chatRejects;
    private readonly ulong[] _inByKind = new ulong[RequestGuard.KindCount];

    internal void CountIn(RequestKind kind, int bytes)
    {
        Volatile.Write(ref _inPackets, _inPackets + 1);
        Volatile.Write(ref _inBytes, _inBytes + (ulong)bytes);
        Volatile.Write(ref _inByKind[(int)kind], _inByKind[(int)kind] + 1);
    }

    /// <summary>수신자 한 명에게 보낸 패킷.</summary>
    internal void CountOut(int bytes)
    {
        Volatile.Write(ref _outPackets, _outPackets + 1);
        Volatile.Write(ref _outBytes, _outBytes + (ulong)bytes);
        Volatile.Write(ref _outRelays, _outRelays + 1);
    }

    /// <summary>같은 패킷을 여러 수신자에게 보낸 멀티캐스트 하나.</summary>
    internal void CountBroadcast(int recipients, int bytes)
    {
        Volatile.Write(ref _outPackets, _outPackets + (ulong)recipients);
        Volatile.Write(ref _outBytes, _outBytes + (ulong)recipients * (ulong)bytes);
        Volatile.Write(ref _outRelays, _outRelays + 1);
        Volatile.Write(ref _broadcasts, _broadcasts + 1);
        Volatile.Write(ref _broadcastRecipients, _broadcastRecipients + (ulong)recipients);
        if ((ulong)recipients > _maxRecipients)
            Volatile.Write(ref _maxRecipients, (ulong)recipients);
    }

    internal void CountDropped() => Volatile.Write(ref _dropped, _dropped + 1);
    internal void CountMalformed() => Volatile.Write(ref _malformed, _malformed + 1);
    internal void CountKick() => Volatile.Write(ref _kicks, _kicks + 1);
    internal void CountPicked() => Volatile.Write(ref _itemsPicked, _itemsPicked + 1);
    internal void CountPickupReject() => Volatile.Write(ref _pickupRejects, _pickupRejects + 1);
    internal void CountChat() => Volatile.Write(ref _chats, _chats + 1);
    internal void CountChatReject() => Volatile.Write(ref _chatRejects, _chatRejects + 1);

    /// <summary>현재 값을 복사한다. 항목마다 따로 읽으므로 서로 한두 건 어긋날 수 있다(관측 용도).</summary>
    public MetricsSnapshot Snapshot()
    {
        ulong[] byKind = new ulong[_inByKind.Length];
        for (int i = 0; i < byKind.Length; ++i)
            byKind[i] = Volatile.Read(ref _inByKind[i]);

        return new MetricsSnapshot
        {
            InPackets = Volatile.Read(ref _inPackets),
            InBytes = Volatile.Read(ref _inBytes),
            InByKind = byKind,
            OutPackets = Volatile.Read(ref _outPackets),
            OutBytes = Volatile.Read(ref _outBytes),
            OutRelays = Volatile.Read(ref _outRelays),
            Broadcasts = Volatile.Read(ref _broadcasts),
            BroadcastRecipients = Volatile.Read(ref _broadcastRecipients),
            MaxRecipients = Volatile.Read(ref _maxRecipients),
            Dropped = Volatile.Read(ref _dropped),
            Malformed = Volatile.Read(ref _malformed),
            Kicks = Volatile.Read(ref _kicks),
            ItemsPicked = Volatile.Read(ref _itemsPicked),
            PickupRejects = Volatile.Read(ref _pickupRejects),
            Chats = Volatile.Read(ref _chats),
            ChatRejects = Volatile.Read(ref _chatRejects),
        };
    }

    /// <summary>
    /// 두 스냅샷 사이의 구간 요약 한 줄. prev가 null이면 시작 후 누적이다.
    /// 틱 분포는 존의 ZoneStats(TickBuckets)에서 따로 만든다(FormatTickBuckets).
    /// </summary>
    public static string Format(in MetricsSnapshot cur, in MetricsSnapshot? prev, double seconds)
    {
        double sec = seconds > 0.0 ? seconds : 1.0;
        MetricsSnapshot p = prev ?? default;
        double inPps = (cur.InPackets - p.InPackets) / sec;
        double outPps = (cur.OutPackets - p.OutPackets) / sec;
        double outKbps = (cur.OutBytes - p.OutBytes) / sec / 1024.0;
        double relayPps = (cur.OutRelays - p.OutRelays) / sec;

        StringBuilder sb = new StringBuilder(160);
        sb.Append("in=").Append(inPps.ToString("F0")).Append("/s out=").Append(outPps.ToString("F0")).Append("/s (")
          .Append(outKbps.ToString("F0")).Append("KB/s, relay ").Append(relayPps.ToString("F0")).Append("/s) fanout avg=")
          .Append(cur.AvgRecipients.ToString("F1")).Append(" max=").Append(cur.MaxRecipients)
          .Append(" dropped=").Append(cur.Dropped).Append(" malformed=").Append(cur.Malformed).Append(" kicks=").Append(cur.Kicks)
          .Append(" picked=").Append(cur.ItemsPicked).Append('/').Append(cur.ItemsPicked + cur.PickupRejects)
          .Append(" chat=").Append(cur.Chats).Append('/').Append(cur.Chats + cur.ChatRejects);
        return sb.ToString();
    }

    /// <summary>틱 처리 시간 분포 요약. 예: "&lt;1ms:1180 &lt;2ms:20 &lt;5ms:0 ... &gt;=50ms:0".</summary>
    public static string FormatTickBuckets(ulong[]? buckets)
    {
        if (buckets == null)
            return string.Empty;

        string[] names = { "<1ms", "<2ms", "<5ms", "<10ms", "<25ms", "<50ms", ">=50ms" };
        StringBuilder sb = new StringBuilder(80);
        for (int i = 0; i < buckets.Length && i < names.Length; ++i)
        {
            if (i != 0)
                sb.Append(' ');
            sb.Append(names[i]).Append(':').Append(buckets[i]);
        }
        return sb.ToString();
    }
}
