// DirectoryHash.cs : 폴더 내용 해시.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Core.Util;

/// <summary>
/// 폴더 안의 파일(하위 폴더 포함)로 SHA-256 해시를 만든다. 서버끼리, 또는 서버와 클라이언트가 같은 데이터 폴더(GameData 등)를
/// 쓰는지 확인하는 데 쓴다.
/// <para>
/// 해시에는 폴더 기준 상대 경로('/' 구분, 대소문자 그대로)와 파일 길이, 내용이 들어가므로 폴더를 어디에 두든 같은 내용이면 같은 값이다.
/// 파일 순서는 상대 경로의 서수(Ordinal) 정렬로 고정한다. 파일 이름이나 내용이 하나라도 다르거나 파일이 늘고 줄면 값이 달라진다.
/// 파일은 조각으로 읽어 해시하므로 큰 파일도 메모리를 많이 쓰지 않는다.
/// </para>
/// </summary>
public static class DirectoryHash
{
    private const int ChunkSize = 64 * 1024;

    /// <summary>폴더의 해시를 소문자 16진수 64자로 돌려준다.</summary>
    /// <param name="directory">대상 폴더. 없으면 <see cref="DirectoryNotFoundException"/>.</param>
    /// <param name="include">폴더 기준 상대 경로('/' 구분)를 받아 해시에 넣을지 정한다. null이면 모두 넣는다(예: p => p.EndsWith(".json")).</param>
    /// <exception cref="IOException">해시하는 도중 파일 길이가 바뀐 경우.</exception>
    public static string Compute(string directory, Func<string, bool>? include = null)
    {
        if (directory == null) throw new ArgumentNullException(nameof(directory));
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

        List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
        foreach (string full in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(directory, full).Replace('\\', '/');
            if (include == null || include(rel))
                files.Add(new KeyValuePair<string, string>(rel, full));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] chunk = new byte[ChunkSize];
        Span<byte> lenBytes = stackalloc byte[8];

        foreach (KeyValuePair<string, string> f in files)
        {
            // 이름과 내용의 경계를 모호하게 하지 않도록 이름 뒤에 0바이트, 내용 앞에 길이를 넣는다.
            hash.AppendData(Encoding.UTF8.GetBytes(f.Key));
            hash.AppendData(new byte[] { 0 });

            using FileStream fs = new FileStream(f.Value, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, FileOptions.SequentialScan);
            long length = fs.Length;
            BinaryPrimitives.WriteInt64LittleEndian(lenBytes, length);
            hash.AppendData(lenBytes);

            long remain = length;
            while (remain > 0)
            {
                int n = fs.Read(chunk, 0, (int)Math.Min(chunk.Length, remain));
                if (n <= 0) throw new IOException("파일이 해시하는 도중 줄었다: " + f.Key);
                hash.AppendData(chunk, 0, n);
                remain -= n;
            }
            if (fs.ReadByte() >= 0) throw new IOException("파일이 해시하는 도중 늘었다: " + f.Key);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
