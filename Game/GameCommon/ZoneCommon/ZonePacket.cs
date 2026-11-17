// ZonePacket.cs : 존 서버 분리 구조(Gateway / ZoneServer) 패킷 도우미.
// 패킷 구조체와 열거형은 생성 파일(Packets.gen.cs, ProtocolTypes.gen.cs)에 있고, 여기에는 손으로 쓰는 도우미(IPacket, 헤더/문자열 입출력, 중계 패킷 조립)만 둔다.
// 생성 파일이 IPacket, PacketIO를 쓰므로 네임스페이스는 --cs-gen-namespace(Game.Generated)와 같게 둔다.
// 이 폴더의 Game.ZoneCommon 프로젝트가 생성 파일 Packets.gen.cs, ProtocolTypes.gen.cs도 함께 컴파일한다.
using System;
using System.Buffers.Binary;
using System.Text;

namespace Game.Generated;

/// <summary>
/// 고정 크기 패킷 구조체의 공통 모양. 구조체마다 Size(const)와 Write가 있어서, 이 인터페이스로 묶으면
/// "버퍼를 잡고(stackalloc) 쓰고 보낸다"를 제네릭 도우미 하나로 처리할 수 있다. 제네릭 인자가 구조체이므로 박싱은 없다.
/// </summary>
public interface IPacket
{
    /// <summary>헤더를 포함한 와이어 크기. 각 구조체의 Size와 같다.</summary>
    static abstract int PacketSize { get; }

    /// <summary>헤더와 필드를 packet 앞에 쓰고 쓴 바이트 수를 돌려준다. packet은 PacketSize 이상이어야 한다.</summary>
    int Write(Span<byte> packet);
}

/// <summary>
/// 프레이밍 규칙: 모든 패킷은 [size:u16][type:u16] 헤더로 시작하며 size는 헤더를 포함한 패킷 전체 크기다.
/// 중계 패킷(ZoneRelayToZone / ZoneRelayToClient)은 고정 필드 뒤에 클라이언트 패킷 원문(자체 헤더 포함)이 그대로 이어지는 가변 길이 패킷이다.
/// </summary>
public static class PacketIO
{
    public const int HeaderSize = 4;

    public static ushort ReadSize(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet);

    public static ushort ReadType(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2));

    public static void WriteHeader(Span<byte> packet, int size, ushort type)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(packet, (ushort)size);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.Slice(2), type);
    }

    /// <summary>고정 길이 UTF-8 문자열 필드를 쓴다. 남는 칸은 0으로 채우고 마지막 바이트는 항상 NUL이다. 넘치면 UTF-8 문자 경계에서 자른다.</summary>
    public static void WriteString(Span<byte> field, string? value)
    {
        field.Clear();
        if (string.IsNullOrEmpty(value) || field.Length == 0)
            return;

        // 글자 하나는 UTF-8로 한 바이트 이상이므로 앞 room(NUL 자리를 뺀 칸 수) 글자만 인코딩하면 충분하다. 임시 배열은 스택에 둔다.
        int room = field.Length - 1;
        int chars = Math.Min(value.Length, room);
        if (chars > 0 && char.IsHighSurrogate(value[chars - 1]) && chars < value.Length)
            --chars;        // 서로게이트 쌍의 앞 절반에서 자르면 대체 문자가 들어가므로 뺀다

        int maxBytes = Encoding.UTF8.GetMaxByteCount(chars);
        Span<byte> tmp = maxBytes <= 512 ? stackalloc byte[512] : new byte[maxBytes];
        int written = Encoding.UTF8.GetBytes(value.AsSpan(0, chars), tmp);
        int len = Math.Min(written, room);
        if (len < written)
        {
            while (len > 0 && (tmp[len] & 0xC0) == 0x80)  // 문자 중간이면 앞으로 물린다
                --len;
        }
        tmp.Slice(0, len).CopyTo(field);
    }

    /// <summary>고정 길이 UTF-8 문자열 필드를 읽는다. 첫 NUL까지.</summary>
    public static string ReadString(ReadOnlySpan<byte> field)
    {
        int len = field.IndexOf((byte)0);
        if (len < 0)
            len = field.Length;
        return Encoding.UTF8.GetString(field.Slice(0, len));
    }
}

/// <summary>중계 패킷(고정 헤더 + 페이로드 원문) 조립과 분해.</summary>
public static class ZonePacket
{
    /// <summary>서버 간 패킷 크기 상한 (MaxServerPacketBytes).</summary>
    public const int MaxServerPacketBytes = ProtocolConst.MaxServerPacketBytes;

    /// <summary>ZoneRelayToZone 또는 ZoneRelayToClient(고정 12바이트 + 원문)를 만든다.</summary>
    /// <param name="type">ZoneRelayToZone 또는 ZoneRelayToClient</param>
    /// <returns>만든 전체 크기, 원문이 비었거나 버퍼/ushort 범위를 넘으면 0</returns>
    public static int BuildRelay(ZoneServerPacketType type, ulong clientSessionId, ReadOnlySpan<byte> payload, Span<byte> outBuf)
    {
        const int fixedSize = ZoneRelayToZone.Size;     // ZoneRelayToClient와 같은 레이아웃(12)
        int total = fixedSize + payload.Length;
        if (payload.IsEmpty || total > outBuf.Length || total > ushort.MaxValue)
            return 0;

        outBuf.Slice(0, fixedSize).Clear();
        PacketIO.WriteHeader(outBuf, total, (ushort)type);
        BinaryPrimitives.WriteUInt64LittleEndian(outBuf.Slice(4), clientSessionId);
        payload.CopyTo(outBuf.Slice(fixedSize));
        return total;
    }

    /// <summary>멀티캐스트 중계 패킷 한 개에 담을 수 있는 최대 수신자 수. 원문이 너무 커서 한 명도 못 담으면 0.</summary>
    public static int MaxMulticastRecipients(int payloadSize, int capacity)
    {
        int fixedSize = ZoneRelayToClients.Size + payloadSize;
        if (fixedSize >= capacity || fixedSize > ushort.MaxValue)
            return 0;

        int byCapacity = (capacity - fixedSize) / sizeof(ulong);
        return byCapacity < ushort.MaxValue ? byCapacity : ushort.MaxValue;
    }

    /// <summary>멀티캐스트 중계 패킷(고정 6바이트 + 수신자 ID 배열 + 원문)을 만든다.</summary>
    /// <returns>만든 전체 크기, 인자가 잘못됐거나 크기가 범위를 넘으면 0</returns>
    public static int BuildMulticastRelay(ReadOnlySpan<ulong> sessionIds, ReadOnlySpan<byte> payload, Span<byte> outBuf)
    {
        if (sessionIds.IsEmpty || payload.IsEmpty || sessionIds.Length > MaxMulticastRecipients(payload.Length, outBuf.Length))
            return 0;

        int idsBytes = sessionIds.Length * sizeof(ulong);
        int total = ZoneRelayToClients.Size + idsBytes + payload.Length;

        PacketIO.WriteHeader(outBuf, total, (ushort)ZoneServerPacketType.ZoneRelayToClients);
        BinaryPrimitives.WriteUInt16LittleEndian(outBuf.Slice(4), (ushort)sessionIds.Length);
        int off = ZoneRelayToClients.Size;
        for (int i = 0; i < sessionIds.Length; ++i)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(outBuf.Slice(off), sessionIds[i]);
            off += sizeof(ulong);
        }
        payload.CopyTo(outBuf.Slice(off));
        return total;
    }

    /// <summary>ZoneRelayToClients 패킷을 분해한다. 크기/수신자 수/원문 길이가 맞지 않으면 false.</summary>
    /// <param name="idsBytes">수신자 ID 배열(8바이트 * count, 리틀 엔디안)</param>
    /// <param name="payload">클라이언트로 보낼 원문(자체 헤더 포함)</param>
    public static bool TryParseMulticastRelay(ReadOnlySpan<byte> packet, out int count, out ReadOnlySpan<byte> idsBytes, out ReadOnlySpan<byte> payload)
    {
        count = 0;
        idsBytes = default;
        payload = default;
        if (packet.Length < ZoneRelayToClients.Size)
            return false;

        count = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(4));
        int idsEnd = ZoneRelayToClients.Size + count * sizeof(ulong);
        if (count == 0 || idsEnd >= packet.Length)
            return false;

        idsBytes = packet.Slice(ZoneRelayToClients.Size, count * sizeof(ulong));
        payload = packet.Slice(idsEnd);
        return true;
    }

    /// <summary>중계 패킷(ZoneRelayToZone/ZoneRelayToClient)의 원문을 꺼낸다. 원문이 헤더 크기보다 작으면 false.</summary>
    public static bool TryParseRelay(ReadOnlySpan<byte> packet, out ulong clientSessionId, out ReadOnlySpan<byte> payload)
    {
        clientSessionId = 0;
        payload = default;
        if (packet.Length < ZoneRelayToZone.Size + PacketIO.HeaderSize)
            return false;

        clientSessionId = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(4));
        payload = packet.Slice(ZoneRelayToZone.Size);
        return true;
    }

    [ThreadStatic] private static byte[]? t_sendBuffer;

    /// <summary>
    /// 서버 간 패킷(최대 MaxServerPacketBytes)을 조립할 스레드별 재사용 버퍼. 호출한 스레드 안에서만, 다음 호출 전까지만 쓴다.
    /// 조립한 패킷은 송신 함수가 복사해 가므로 반환 후에는 다시 써도 된다.
    /// </summary>
    public static byte[] ThreadBuffer => t_sendBuffer ??= new byte[ProtocolConst.MaxServerPacketBytes];

    /// <summary>단조 증가 시각(ms). 하트비트 타임아웃 판정용.</summary>
    public static long SteadyNowMs() => Environment.TickCount64;
}
