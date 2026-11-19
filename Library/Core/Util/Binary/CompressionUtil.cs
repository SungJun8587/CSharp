// CompressionUtil.cs : Brotli/Deflate 압축 도우미(바이트 배열, 스팬).
using System;
using System.Buffers;
using System.IO.Compression;

namespace Core.Util;

public enum CompressionKind : byte
{
    None = 0,
    Brotli = 1,
    Deflate = 2,
}

/// <summary>
/// 큰 패킷/파일을 줄이는 도우미. 작은 데이터는 압축해도 오히려 커지므로 <see cref="TryCompress"/>가 이득이 있을 때만 압축 결과를 준다.
/// 압축 해제는 상한(maxOutput)을 받아 압축 폭탄(작은 입력이 수 GB로 풀리는 공격)을 막는다.
/// </summary>
public static class CompressionUtil
{
    /// <summary>이보다 작은 데이터는 압축하지 않는다.</summary>
    public const int MinCompressSize = 128;

    /// <summary>
    /// 압축한 결과가 원본보다 작을 때만 true. 속도가 중요한 실시간 패킷은 quality를 낮게(1~4), 저장용은 높게(9~11) 쓴다(Brotli 0~11, Deflate는 0~9를 단계로 환산).
    /// </summary>
    public static bool TryCompress(ReadOnlySpan<byte> src, Span<byte> dst, CompressionKind kind, int quality, out int written)
    {
        written = 0;
        if (src.Length < MinCompressSize || kind == CompressionKind.None)
            return false;

        int n;
        switch (kind)
        {
            case CompressionKind.Brotli:
                if (!BrotliEncoder.TryCompress(src, dst, out n, Math.Clamp(quality, 0, 11), 22))
                    return false;
                break;
            case CompressionKind.Deflate:
                if (!TryDeflate(src, dst, quality, out n))
                    return false;
                break;
            default:
                return false;
        }
        if (n >= src.Length)
            return false;
        written = n;
        return true;
    }

    private static bool TryDeflate(ReadOnlySpan<byte> src, Span<byte> dst, int quality, out int written)
    {
        written = 0;
        CompressionLevel level = quality <= 1 ? CompressionLevel.Fastest : quality >= 9 ? CompressionLevel.SmallestSize : CompressionLevel.Optimal;
        using var ms = new System.IO.MemoryStream(dst.Length);
        using (var ds = new DeflateStream(ms, level, leaveOpen: true))
            ds.Write(src);
        if (ms.Length > dst.Length)
            return false;
        ms.GetBuffer().AsSpan(0, (int)ms.Length).CopyTo(dst);
        written = (int)ms.Length;
        return true;
    }

    /// <summary>압축된 바이트 배열을 만든다(이득이 없으면 null).</summary>
    public static byte[]? Compress(ReadOnlySpan<byte> src, CompressionKind kind, int quality = 4)
    {
        if (src.Length < MinCompressSize || kind == CompressionKind.None)
            return null;
        int cap = kind == CompressionKind.Brotli ? BrotliEncoder.GetMaxCompressedLength(src.Length) : src.Length + 64;
        byte[] rent = ArrayPool<byte>.Shared.Rent(cap);
        try
        {
            if (!TryCompress(src, rent, kind, quality, out int n))
                return null;
            return rent.AsSpan(0, n).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rent);
        }
    }

    /// <summary>
    /// 압축을 푼다. 결과가 maxOutput바이트를 넘으면 false(잘못된 데이터이거나 압축 폭탄). dst에는 maxOutput 이상의 공간을 주지 않아도 되고,
    /// dst가 모자라면 역시 false다. Deflate 스트림은 끝 표시가 없어 잘린 입력을 오류로 알 수 없으므로(true와 짧은 written),
    /// 호출자가 written이 기대한 원본 길이와 같은지 확인해야 한다(CompressionFrame은 이를 확인한다).
    /// </summary>
    public static bool TryDecompress(ReadOnlySpan<byte> src, Span<byte> dst, CompressionKind kind, out int written)
    {
        written = 0;
        try
        {
            switch (kind)
            {
                case CompressionKind.Brotli:
                    return BrotliDecoder.TryDecompress(src, dst, out written);
                case CompressionKind.Deflate:
                    {
                        using var input = new System.IO.MemoryStream(src.ToArray(), writable: false);
                        using var ds = new DeflateStream(input, CompressionMode.Decompress);
                        int total = 0;
                        while (true)
                        {
                            if (total == dst.Length)
                            {
                                // 더 읽을 게 있는지 한 바이트만 확인: 있으면 출력이 모자란 것
                                Span<byte> probe = stackalloc byte[1];
                                if (ds.Read(probe) != 0)
                                    return false;
                                break;
                            }
                            int r = ds.Read(dst.Slice(total));
                            if (r == 0) break;
                            total += r;
                        }
                        written = total;
                        return true;
                    }
                default:
                    return false;
            }
        }
        catch (System.IO.InvalidDataException)
        {
            written = 0;
            return false;
        }
    }
}
