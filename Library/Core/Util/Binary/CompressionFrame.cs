// CompressionFrame.cs : 압축 프레임(종류 + 원본 길이 + 본문), 해제 상한 검사.
using System;
using System.IO.Compression;

namespace Core.Util;

/// <summary>
/// 압축 프레임: <c>[종류 1바이트][원본 길이 u32 LE][본문]</c>. 압축이 이득이 없으면 종류 None으로 원본을 그대로 담는다.
/// 받는 쪽은 <b>원본 길이를 먼저 검사</b>(maxOutput)한 뒤에야 버퍼를 만들어 푼다 — 작은 입력이 거대한 출력으로 풀리는 공격을 할당 전에 막는다.
/// </summary>
public static class CompressionFrame
{
    public const int HeaderSize = 5;

    /// <summary>src를 프레임으로 만든다. 압축 이득이 있으면 kind로, 없으면 None으로 담는다.</summary>
    public static byte[] Pack(ReadOnlySpan<byte> src, CompressionKind kind = CompressionKind.Brotli, int quality = 4)
    {
        byte[]? comp = CompressionUtil.Compress(src, kind, quality);
        CompressionKind used = comp != null ? kind : CompressionKind.None;
        ReadOnlySpan<byte> body = comp != null ? comp : src;
        byte[] frame = new byte[HeaderSize + body.Length];
        frame[0] = (byte)used;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(1), (uint)src.Length);
        body.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    /// <summary>프레임을 푼다. 헤더가 잘못됐거나 원본 길이가 maxOutput을 넘거나 압축 데이터가 손상됐거나 길이가 안 맞으면 null.</summary>
    public static byte[]? Unpack(ReadOnlySpan<byte> frame, int maxOutput)
    {
        if (frame.Length < HeaderSize || maxOutput < 0)
            return null;
        CompressionKind kind = (CompressionKind)frame[0];
        uint len = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(1));
        if (len > (uint)maxOutput)
            return null;                                    // 할당하기 전에 거절
        ReadOnlySpan<byte> body = frame.Slice(HeaderSize);

        if (kind == CompressionKind.None)
            return body.Length == (int)len ? body.ToArray() : null;
        if (kind != CompressionKind.Brotli && kind != CompressionKind.Deflate)
            return null;

        byte[] result = new byte[len];
        if (!CompressionUtil.TryDecompress(body, result, kind, out int written) || written != (int)len)
            return null;
        return result;
    }
}
