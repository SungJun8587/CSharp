using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Core.Util;

/// <summary>임시 파일로 대상 파일을 교체한다.</summary>
public static class FileReplace
{
    /// <summary>
    /// from을 to로 옮겨 기존 파일을 덮어씁니다. 대상을 다른 프로그램(서버, 백신, 인덱서)이 잠깐 열고 있으면
    /// 실패할 수 있어 몇 번 다시 시도한다. 끝내 실패하면 예외를 그대로 던진다. (from은 지우지 않는다)
    /// </summary>
    public static void Move(string from, string to)
    {
        for (int attempt = 1; ; ++attempt)
        {
            try
            {
                File.Move(from, to, true);
                return;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (attempt >= 5) throw;
                Thread.Sleep(100);
            }
        }
    }
}

/// <summary>파일 쓰기: 임시 파일 교체, 내용이 같으면 쓰지 않기, UTF-8 변환.</summary>
public static class FileUtil
{
    public static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };
    private static long _counter;

    /// <summary>UTF-8 바이트열 (BOM 없음).</summary>
    public static byte[] Utf8(string text) => Utf8NoBom.GetBytes(text);

    /// <summary>BOM 있는 UTF-8 바이트열. (MSVC가 BOM 없는 파일을 시스템 코드 페이지로 읽는 것을 막는다)</summary>
    public static byte[] Utf8WithBom(string text)
    {
        byte[] body = Utf8NoBom.GetBytes(text);
        byte[] all = new byte[Bom.Length + body.Length];
        Bom.CopyTo(all, 0);
        body.CopyTo(all, Bom.Length);
        return all;
    }

    // 임시 파일 이름에 붙일 실행마다 다른 값. (같은 출력 경로를 동시에 쓰는 두 실행이 임시 파일을 공유하지 않게 한다)
    private static string TmpSuffixId()
        => Environment.ProcessId.ToString() + "_" + Interlocked.Increment(ref _counter) + "_" + Environment.TickCount64;

    /// <summary>
    /// 내용을 임시 파일에 쓴 뒤 같은 폴더에서 교체합니다. (읽는 쪽이 반쯤 쓰인 파일을 보지 않게 한다)
    /// 교체 대상이 잠겨 있으면 FileReplace.Move가 몇 번 다시 시도한다. 실패하면 임시 파일을 지우고 예외를 다시 던진다.
    /// </summary>
    public static void WriteAtomically(string path, byte[] content)
    {
        string tmp = path + ".tmp" + TmpSuffixId();
        try
        {
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(content, 0, content.Length);
                f.Flush();
            }
            FileReplace.Move(tmp, path);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* 임시 파일 정리 실패는 무시 */ }
            throw;
        }
    }

    // 줄바꿈의 CR(0x0D)을 건너뛰며 비교한다. (복사본을 만들지 않는다)
    private static bool EqualIgnoringCr(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int i = 0, j = 0;
        for (; ; )
        {
            while (i < a.Length && a[i] == (byte)'\r') ++i;
            while (j < b.Length && b[j] == (byte)'\r') ++j;
            if (i == a.Length || j == b.Length) return i == a.Length && j == b.Length;
            if (a[i++] != b[j++]) return false;
        }
    }

    /// <summary>파일 내용이 같은지 확인합니다. (없거나 다르면 false) 줄바꿈의 CR 차이는 같은 것으로 본다.</summary>
    public static bool SameContent(string path, byte[] content)
    {
        if (!File.Exists(path)) return false;
        byte[] current;
        try { current = File.ReadAllBytes(path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        if (current.AsSpan().SequenceEqual(content)) return true;
        // 저장소 설정(autocrlf 등)으로 줄바꿈만 바뀐 파일은 같은 것으로 본다. (그렇지 않으면 빌드마다 다시 써 재빌드를 일으킨다)
        return EqualIgnoringCr(current, content);
    }

    /// <summary>
    /// 내용이 달라졌을 때만 파일을 씁니다.
    /// 같은 내용을 다시 쓰면 수정 시각이 바뀌어 재빌드, 서버 데이터 다시 읽기, 파일 감시가 불필요하게 일어난다.
    /// </summary>
    /// <returns>실제로 썼으면 true</returns>
    public static bool WriteFileIfChanged(string path, byte[] content)
    {
        if (SameContent(path, content)) return false;
        WriteAtomically(path, content);
        return true;
    }
}
