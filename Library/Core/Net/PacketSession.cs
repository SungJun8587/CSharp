// PacketSession.cs : implementation of the PacketSession and PacketHeader classes.
using System;
using System.Buffers.Binary;

namespace Core.Net;

/// <summary>
/// 패킷 헤더 유틸. 헤더는 [size:ushort][id:ushort] (리틀 엔디안)이며 size 는 헤더를 포함한 전체 패킷 크기이다.
/// </summary>
public static class PacketHeader
{
    /// <summary>헤더 크기(바이트).</summary>
    public const int HeaderSize = 4;

    /// <summary>패킷 최대 크기(ushort 한계).</summary>
    public const int MaxPacketSize = ushort.MaxValue;

    public static ushort ReadSize(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet);

    public static ushort ReadId(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2));

    /// <summary>packet 의 앞 4바이트에 헤더를 기록한다.</summary>
    public static void Write(Span<byte> packet, ushort size, ushort id)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(packet, size);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.Slice(2), id);
    }
}

/// <summary>
/// TCP 스트림에서 헤더 기반으로 패킷 경계를 잘라 OnRecvPacket 으로 넘겨주는 세션.
/// </summary>
public abstract class PacketSession : Session
{
    protected PacketSession(int recvBufferSize = NetConstants.BufferSizeDefault)
        : base(recvBufferSize)
    {
        // 수신 버퍼가 최대 패킷(ushort 한계)보다 작으면 큰 패킷이 올 때 이유 없이 끊기므로 생성 시점에 막는다.
        int capacity = RecvBuffer.ComputeCapacity(recvBufferSize);
        if (capacity < PacketHeader.MaxPacketSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recvBufferSize),
                $"수신 버퍼 용량({capacity})이 최대 패킷 크기 {PacketHeader.MaxPacketSize} 보다 작습니다.");
        }
    }

    /// <summary>
    /// 완성된 패킷이 있을 때마다 호출된다. 이 구간에서 예외가 나면 연결을 끊는다.
    /// </summary>
    /// <param name="packet">헤더를 포함한 전체 패킷. 반환 이후에는 무효이므로 보관하지 말 것.</param>
    protected abstract void OnRecvPacket(ReadOnlySpan<byte> packet);

    protected sealed override int OnRecv(ReadOnlySpan<byte> buffer)
    {
        int processed = 0;

        while (true)
        {
            int dataSize = buffer.Length - processed;
            if (dataSize < PacketHeader.HeaderSize)
                break;

            int packetSize = PacketHeader.ReadSize(buffer.Slice(processed));
            if (packetSize < PacketHeader.HeaderSize)
                return -1; // 잘못된 헤더

            if (dataSize < packetSize)
                break; // 패킷이 아직 다 도착하지 않음

            OnRecvPacket(buffer.Slice(processed, packetSize));
            processed += packetSize;
        }

        return processed;
    }
}
