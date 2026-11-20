// RedisCommandBuilder.cs : Redis 명령 인자를 RESP 규격으로 인코딩하는 정적 유틸리티.
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Redis;

/// <summary>
/// 인자 목록을 RESP Array 형태(<c>*N\r\n$L\r\n...</c>)로 인코딩한다. 인자는 UTF-8 바이트로 기록하며 길이는 바이트 수다.
/// <see cref="Build"/>는 byte[] 결과가 필요한 곳(로깅, 테스트)에 쓰는 간편 버전이고,
/// <see cref="CalcEncodedSize"/>/<see cref="Encode"/>는 송신 버퍼에 직접 써서 중간 사본을 만들지 않는 경로에 쓴다.
/// 자릿수는 서식 엔진 없이 정수 연산으로 직접 계산/기록한다(HMGET처럼 인자가 1000개 단위인 명령에서 오버헤드를 없앤다).
/// </summary>
/// <example>
/// <code>
/// byte[] req = RedisCommandBuilder.Build(new[] { "SET", "Player:100", "Data" });
/// // "*3\r\n$3\r\nSET\r\n$10\r\nPlayer:100\r\n$4\r\nData\r\n"
/// </code>
/// </example>
public static class RedisCommandBuilder
{
    /// <summary>명령어 인자 목록을 RESP 규격 바이트열로 변환한다. 크기 한도를 넘으면 빈 배열을 돌려준다.</summary>
    public static byte[] Build(IReadOnlyList<string> args)
    {
        int size = CalcEncodedSize(args);
        if (size == 0)
            return Array.Empty<byte>();

        byte[] result = new byte[size];
        Encode(args, result);
        return result;
    }

    /// <summary><see cref="Build"/>의 문자열 버전(로깅/디버깅용).</summary>
    public static string BuildString(IReadOnlyList<string> args) => Encoding.UTF8.GetString(Build(args));

    /// <summary>
    /// <see cref="Encode"/>가 필요로 하는 인코딩 결과의 정확한 바이트 크기를 계산한다.
    /// </summary>
    /// <returns><see cref="RedisConst.MaxCommandSize"/>를 넘으면 0(정상 명령은 최소 4바이트라 0과 겹치지 않는다)</returns>
    public static int CalcEncodedSize(IReadOnlyList<string> args)
    {
        long size = HeaderLen(args.Count);
        for (int i = 0; i < args.Count; ++i)
        {
            int byteCount = ArgByteCount(args, i);
            size += HeaderLen(byteCount) + byteCount + 2; // 헤더 + 데이터 + \r\n

            // 누적하면서 상한을 확인해, int로 되감겨 실제보다 작은 버퍼에 Encode()가 쓰는 일이 없게 한다.
            if (size > RedisConst.MaxCommandSize)
                return 0;
        }

        return (int)size;
    }

    /// <summary>명령어 인자 목록을 RESP 규격으로 dest에 직접 기록하고 기록한 바이트 수를 돌려준다.</summary>
    /// <param name="dest">CalcEncodedSize(args) 이상의 여유 공간을 가진 대상 버퍼</param>
    public static int Encode(IReadOnlyList<string> args, Span<byte> dest)
    {
        int p = WriteHeader(dest, (byte)'*', args.Count);

        for (int i = 0; i < args.Count; ++i)
        {
            int byteCount = ArgByteCount(args, i);
            p += WriteHeader(dest.Slice(p), (byte)'$', byteCount);
            byte[]? binary = (args as RedisBinaryArgs)?.BinaryAt(i);
            if (binary != null)
            {
                binary.CopyTo(dest.Slice(p));
                p += binary.Length;
            }
            else
            {
                p += Encoding.UTF8.GetBytes(args[i], dest.Slice(p));
            }
            dest[p++] = (byte)'\r';
            dest[p++] = (byte)'\n';
        }

        return p;
    }

    // 인자 i 가 차지할 바이트 수(바이너리 인자는 원본 길이, 문자열은 UTF-8 길이).
    private static int ArgByteCount(IReadOnlyList<string> args, int i)
    {
        byte[]? binary = (args as RedisBinaryArgs)?.BinaryAt(i);
        return binary != null ? binary.Length : Encoding.UTF8.GetByteCount(args[i]);
    }

    private static int CountDigits(long value)
    {
        int digits = 1;
        while (value >= 10)
        {
            value /= 10;
            ++digits;
        }
        return digits;
    }

    // "<prefix><value>\r\n" 헤더 한 줄이 차지할 바이트 수. WriteHeader()가 실제로 쓰는 바이트 수와 정확히 같아야 한다.
    private static int HeaderLen(long value) => 1 + CountDigits(value) + 2;

    private static int WriteHeader(Span<byte> dest, byte prefix, long value)
    {
        int digits = CountDigits(value);
        dest[0] = prefix;

        for (int i = digits; i >= 1; --i)
        {
            dest[i] = (byte)('0' + (int)(value % 10));
            value /= 10;
        }

        dest[digits + 1] = (byte)'\r';
        dest[digits + 2] = (byte)'\n';
        return digits + 3;
    }
}
