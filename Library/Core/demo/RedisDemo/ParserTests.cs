// ParserTests.cs : RedisParser 증분 스캔 검증(임의 분할 입력, 큰 배열, 깊이/상한 경계, 오류 입력).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Core.Redis;

internal static class ParserTests
{
    private static readonly string s_alphabet = "abcXYZ019 \r\n\t:$*+-한é";

    public static void Run(Action<bool, string> check)
    {
        Differential(check);
        LargeArrays(check);
        Boundaries(check);
        ErrorsWhileChunked(check);
        StateHandling(check);
    }

    // ---- 임의 값 생성기(파서와 독립된 기대값) + 인코더 ----

    private static RedisValue Gen(Random rnd, int depth)
    {
        int kind = rnd.Next(depth >= 4 ? 6 : 8);
        switch (kind)
        {
            case 0: return new RedisValue { Type = RedisType.SimpleString, StrVal = Word(rnd) };
            case 1: return new RedisValue { Type = RedisType.Error, StrVal = "ERR " + Word(rnd) };
            case 2: return new RedisValue { Type = RedisType.Integer, Int64Val = rnd.NextInt64(-1_000_000_000_000, 1_000_000_000_000) };
            case 3: return Bulk(rnd);
            case 4: return new RedisValue { Type = RedisType.BulkString, Int64Val = -1 };
            case 5: return new RedisValue { Type = RedisType.Array, Int64Val = -1 };
            default:
            {
                int n = rnd.Next(0, 6);
                var elems = new RedisValue[n];
                for (int i = 0; i < n; ++i)
                    elems[i] = Gen(rnd, depth + 1);
                return new RedisValue { Type = RedisType.Array, Int64Val = n, ArrayVal = elems };
            }
        }
    }

    private static string Word(Random rnd)
    {
        var sb = new StringBuilder();
        int n = rnd.Next(0, 12);
        for (int i = 0; i < n; ++i)
            sb.Append((char)('a' + rnd.Next(26)));
        return sb.ToString();
    }

    private static RedisValue Bulk(Random rnd)
    {
        var sb = new StringBuilder();
        int n = rnd.Next(0, 40);
        for (int i = 0; i < n; ++i)
            sb.Append(s_alphabet[rnd.Next(s_alphabet.Length)]);      // 본문에 \r\n 이 들어 있어도 길이로만 읽어야 한다
        string s = sb.ToString();
        return new RedisValue { Type = RedisType.BulkString, StrVal = s, Int64Val = Encoding.UTF8.GetByteCount(s) };
    }

    private static void Encode(RedisValue v, MemoryStream ms)
    {
        void W(string t) { byte[] b = Encoding.UTF8.GetBytes(t); ms.Write(b, 0, b.Length); }
        switch (v.Type)
        {
            case RedisType.SimpleString: W("+" + v.StrVal + "\r\n"); break;
            case RedisType.Error: W("-" + v.StrVal + "\r\n"); break;
            case RedisType.Integer: W(":" + v.Int64Val + "\r\n"); break;
            case RedisType.BulkString:
                if (v.Int64Val == -1) { W("$-1\r\n"); break; }
                W("$" + v.Int64Val + "\r\n" + v.StrVal + "\r\n");
                break;
            case RedisType.Array:
                if (v.Int64Val == -1) { W("*-1\r\n"); break; }
                W("*" + v.Int64Val + "\r\n");
                foreach (RedisValue e in v.ArrayVal) Encode(e, ms);
                break;
        }
    }

    private static bool Same(RedisValue a, RedisValue b)
    {
        if (a.Type != b.Type) return false;
        switch (a.Type)
        {
            case RedisType.SimpleString:
            case RedisType.Error: return a.StrVal == b.StrVal;
            case RedisType.Integer: return a.Int64Val == b.Int64Val;
            case RedisType.BulkString:
                return a.Int64Val == b.Int64Val && (a.IsNull || a.StrVal == b.StrVal);
            case RedisType.Array:
                if (a.Int64Val != b.Int64Val) return false;
                if (a.IsNull) return true;
                if (a.ArrayVal.Length != b.ArrayVal.Length) return false;
                for (int i = 0; i < a.ArrayVal.Length; ++i)
                    if (!Same(a.ArrayVal[i], b.ArrayVal[i])) return false;
                return true;
            default: return false;
        }
    }

    // 스트림을 chunk 바이트씩 넣으며 완성되는 값을 모두 모은다. 오류가 나면 그 결과를 돌려준다.
    private static RedisParseResult Drain(RedisParser p, byte[] data, int chunk, List<RedisValue> output)
    {
        for (int pos = 0; pos < data.Length;)
        {
            int n = Math.Min(chunk, data.Length - pos);
            if (!p.Feed(data.AsSpan(pos, n)))
                return RedisParseResult.ProtocolError;
            pos += n;

            while (true)
            {
                RedisParseResult r = p.TryParse(out RedisValue? v);
                if (r == RedisParseResult.Success) { output.Add(v!); continue; }
                if (r == RedisParseResult.ProtocolError) return r;
                break;
            }
        }
        return RedisParseResult.NeedMoreData;
    }

    private static void Differential(Action<bool, string> check)
    {
        var rnd = new Random(20261119);
        int[] chunks = { 1, 2, 3, 5, 7, 16, 64, 1000, int.MaxValue };
        int streams = 0, failures = 0;

        for (int iter = 0; iter < 400 && failures == 0; ++iter)
        {
            int count = rnd.Next(1, 5);
            var expected = new List<RedisValue>();
            var ms = new MemoryStream();
            for (int i = 0; i < count; ++i)
            {
                RedisValue v = Gen(rnd, 0);
                expected.Add(v);
                Encode(v, ms);
            }
            byte[] data = ms.ToArray();

            foreach (int chunk in chunks)
            {
                var got = new List<RedisValue>();
                RedisParseResult r = Drain(new RedisParser(), data, chunk, got);
                bool ok = r == RedisParseResult.NeedMoreData && got.Count == expected.Count;
                for (int i = 0; ok && i < got.Count; ++i)
                    ok = Same(expected[i], got[i]);
                ++streams;
                if (!ok)
                {
                    ++failures;
                    Console.WriteLine($"    differential mismatch iter={iter} chunk={chunk} result={r} got={got.Count}/{expected.Count}");
                    break;
                }
            }
        }
        check(failures == 0, $"parser: 임의 값 {streams}개 스트림이 어떤 분할에서도 기대값과 같다");
    }

    // ---- 큰 배열: 조각마다 처음부터 다시 파싱하지 않는다 ----

    private static byte[] BigArray(int elems)
    {
        var sb = new StringBuilder();
        sb.Append('*').Append(elems).Append("\r\n");
        for (int i = 0; i < elems; ++i)
        {
            string s = "user:" + i;
            sb.Append('$').Append(s.Length).Append("\r\n").Append(s).Append("\r\n");
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void LargeArrays(Action<bool, string> check)
    {
        foreach (int elems in new[] { 10_000, 100_000 })
        {
            byte[] all = BigArray(elems);
            var p = new RedisParser();
            var got = new List<RedisValue>();

            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            RedisParseResult r = Drain(p, all, 16 * 1024, got);
            sw.Stop();
            long allocMb = (GC.GetAllocatedBytesForCurrentThread() - a0) / (1024 * 1024);

            bool ok = r == RedisParseResult.NeedMoreData && got.Count == 1 && got[0].ArrayVal.Length == elems
                      && got[0].ArrayVal[0].StrVal == "user:0" && got[0].ArrayVal[elems - 1].StrVal == "user:" + (elems - 1);
            check(ok, $"parser: 배열 원소 {elems}개({all.Length / 1024}KB)를 16KB 조각으로 받아도 완성된다");

            // 조각마다 다시 파싱하던 때는 10만 개에서 약 2000ms/549MB 였다. 증분 스캔은 입력 크기에 비례한다.
            Console.WriteLine($"    {elems} elems: {sw.ElapsedMilliseconds}ms, {allocMb}MB allocated");
            if (elems == 100_000)
                check(sw.ElapsedMilliseconds < 1000 && allocMb < 80, "parser: 큰 배열 파싱 시간과 할당이 입력 크기에 비례한다(제곱 증가 없음)");
        }

        // 최상위 큰 배열 앞뒤에 다른 값이 파이프라인으로 붙어 있어도 정확히 3개가 나온다(Feed 의 앞당기기와 스캔 위치 상관 없음).
        {
            var ms = new MemoryStream();
            byte[] head = Encoding.ASCII.GetBytes("+OK\r\n");
            byte[] big = BigArray(5000);
            byte[] tail = Encoding.ASCII.GetBytes(":7\r\n");
            ms.Write(head); ms.Write(big); ms.Write(tail);
            var got = new List<RedisValue>();
            RedisParseResult r = Drain(new RedisParser(), ms.ToArray(), 4093, got);
            check(r == RedisParseResult.NeedMoreData && got.Count == 3 && got[0].StrVal == "OK" && got[1].ArrayVal.Length == 5000 && got[2].Int64Val == 7,
                "parser: 큰 배열 앞뒤의 값이 파이프라인으로 붙어도 순서대로 나온다");
        }

        // 큰 배열 안의 큰 bulk 가 여러 조각에 걸쳐도 맞다
        {
            string big = new string('x', 200_000);
            byte[] data = Encoding.ASCII.GetBytes("*3\r\n$" + big.Length + "\r\n" + big + "\r\n:1\r\n$2\r\nhi\r\n");
            var got = new List<RedisValue>();
            RedisParseResult r = Drain(new RedisParser(), data, 8192, got);
            check(r == RedisParseResult.NeedMoreData && got.Count == 1 && got[0].ArrayVal[0].StrVal.Length == 200_000 && got[0].ArrayVal[2].StrVal == "hi",
                "parser: 배열 안의 큰 bulk 가 여러 조각에 걸쳐도 맞다");
        }
    }

    // ---- 상한/깊이 경계 ----

    private static string Nested(int arrays) => string.Concat(System.Linq.Enumerable.Repeat("*1\r\n", arrays)) + ":1\r\n";

    private static void Boundaries(Action<bool, string> check)
    {
        // 한도 정확히 64단계는 허용, 65단계는 거부. 한 번에 넣는 경로와 한 바이트씩 넣는 경로가 같아야 한다.
        foreach (var (arrays, allowed) in new[] { (64, true), (65, false) })
        {
            byte[] data = Encoding.ASCII.GetBytes(Nested(arrays));
            var whole = new List<RedisValue>();
            RedisParseResult rw = Drain(new RedisParser(), data, int.MaxValue, whole);
            var bytewise = new List<RedisValue>();
            RedisParseResult rb = Drain(new RedisParser(), data, 1, bytewise);

            bool ok = allowed
                ? rw == RedisParseResult.NeedMoreData && whole.Count == 1 && rb == RedisParseResult.NeedMoreData && bytewise.Count == 1
                : rw == RedisParseResult.ProtocolError && rb == RedisParseResult.ProtocolError;
            check(ok, $"parser: 중첩 {arrays}단계는 {(allowed ? "허용" : "거부")}(일괄 입력과 한 바이트씩 입력이 같다)");
        }

        // 큰 배열 선언: 새 상한은 4194304, 넘으면 거부. 선언만으로 큰 할당이 일어나지 않는다.
        {
            var p = new RedisParser();
            p.Feed(Encoding.ASCII.GetBytes("*5000\r\n"));
            check(p.TryParse(out _) == RedisParseResult.NeedMoreData, "parser: 원소 5000개 배열(예전 상한 초과)은 정상 진행");

            var pmax = new RedisParser();
            pmax.Feed(Encoding.ASCII.GetBytes("*4194304\r\n:1\r\n"));
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            RedisParseResult rmax = pmax.TryParse(out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - a0;
            check(rmax == RedisParseResult.NeedMoreData && allocated < 1_000_000, $"parser: 최대 원소 수를 선언해도 대량 할당이 없다({allocated}B)");

            var pover = new RedisParser();
            pover.Feed(Encoding.ASCII.GetBytes("*4194305\r\n"));
            check(pover.TryParse(out _) == RedisParseResult.ProtocolError, "parser: 상한을 넘는 배열 선언은 거부");
        }
    }

    // ---- 조각으로 들어오는 오류 입력 ----

    private static void ErrorsWhileChunked(Action<bool, string> check)
    {
        RedisParseResult Run(string wire, int chunk)
        {
            var got = new List<RedisValue>();
            return Drain(new RedisParser(), Encoding.ASCII.GetBytes(wire), chunk, got);
        }

        foreach (int chunk in new[] { 1, 3, int.MaxValue })
        {
            check(Run("*2\r\n:1\r\n$3\r\nabcXY\r\n", chunk) == RedisParseResult.ProtocolError, $"parser: 배열 안 bulk 뒤 CRLF 불일치 거부(조각 {(chunk == int.MaxValue ? "일괄" : chunk.ToString())})");
            check(Run("*3\r\n:1\r\n?x\r\n:2\r\n", chunk) == RedisParseResult.ProtocolError, $"parser: 배열 중간의 알 수 없는 접두 거부(조각 {(chunk == int.MaxValue ? "일괄" : chunk.ToString())})");
            check(Run("*2\r\n:12x\r\n:2\r\n", chunk) == RedisParseResult.ProtocolError, $"parser: 정수 형식 오류 거부(조각 {(chunk == int.MaxValue ? "일괄" : chunk.ToString())})");
            check(Run("*2\r\n$-5\r\n:2\r\n", chunk) == RedisParseResult.ProtocolError, $"parser: 음수 bulk 길이 거부(조각 {(chunk == int.MaxValue ? "일괄" : chunk.ToString())})");
        }

        // 아직 오지 않은 데이터를 기다리는 정상 상태는 오류가 아니다
        check(Run("*2\r\n:1\r\n$5\r\nhel", 1) == RedisParseResult.NeedMoreData, "parser: 미완성 입력은 NeedMoreData 로 남는다");
    }

    // ---- 상태 관리 ----

    private static void StateHandling(Action<bool, string> check)
    {
        // 스캔 중에 Reset 하면 다음 값은 깨끗하게 파싱된다
        var p = new RedisParser();
        byte[] big = BigArray(2000);
        p.Feed(big.AsSpan(0, 5000));
        bool needMore = p.TryParse(out _) == RedisParseResult.NeedMoreData;
        p.Reset();
        p.Feed(Encoding.ASCII.GetBytes("+OK\r\n"));
        bool ok = p.TryParse(out RedisValue? v) == RedisParseResult.Success && v!.StrVal == "OK" && p.PendingBytes == 0;
        check(needMore && ok, "parser: 스캔 도중 Reset 뒤에는 새 값부터 정상 파싱");

        // 스캔 중 오류가 나면 상태가 정리되어 Reset 후 재사용 가능
        var pe = new RedisParser();
        pe.Feed(Encoding.ASCII.GetBytes("*2\r\n:1\r\n"));
        bool first = pe.TryParse(out _) == RedisParseResult.NeedMoreData;
        pe.Feed(Encoding.ASCII.GetBytes("?\r\n"));
        bool err = pe.TryParse(out _) == RedisParseResult.ProtocolError;
        pe.Reset();
        pe.Feed(Encoding.ASCII.GetBytes(":5\r\n"));
        check(first && err && pe.TryParse(out RedisValue? v5) == RedisParseResult.Success && v5!.Int64Val == 5, "parser: 오류 뒤 Reset 하면 재사용 가능");

        // 완성된 값을 꺼낸 뒤 스캔 상태가 남지 않아 다음 값이 곧바로 일반 경로로 파싱된다
        var pc = new RedisParser();
        pc.Feed(Encoding.ASCII.GetBytes("*2\r\n:1\r\n"));
        pc.TryParse(out _);
        pc.Feed(Encoding.ASCII.GetBytes(":2\r\n+NEXT\r\n"));
        bool arr = pc.TryParse(out RedisValue? va) == RedisParseResult.Success && va!.ArrayVal.Length == 2;
        bool next = pc.TryParse(out RedisValue? vn) == RedisParseResult.Success && vn!.StrVal == "NEXT";
        check(arr && next && pc.PendingBytes == 0, "parser: 스캔으로 완성된 값 뒤의 값도 정상");
    }
}
