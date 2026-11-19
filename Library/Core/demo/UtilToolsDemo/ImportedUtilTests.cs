using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;

namespace Core.Demo;

/// <summary>외부 코드에서 가져와 다듬은 유틸(달력, 폴더 해시, 열거형 원자 연산, 백그라운드 작업, 이력 버퍼)의 시험.</summary>
internal static class ImportedUtilTests
{
    private enum B : byte { A = 1, B = 2, C = 200 }
    private enum S : short { A = -5, B = 7 }
    private enum I : int { A = -1, B = 2, C = 3 }
    private enum L : long { A = long.MinValue, B = 5 }
    private enum U : uint { A = 0, B = uint.MaxValue }

    public static void Run(Action<string, bool> check)
    {
        CalendarTests(check);
        DirectoryHashTests(check);
        AtomicEnumTests(check);
        BackgroundJobTests(check);
        HistoryBufferTests(check);
    }

    // ---------------------------------------------------------------------------------------------
    private static long Ms(int y, int mo, int d, int h, int mi, int s, TimeZoneInfo tz)
        => GameCalendar.ToUnixMs(new DateTime(y, mo, d, h, mi, s), tz);

    private static void CalendarTests(Action<string, bool> check)
    {
        TimeZoneInfo kst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
        TimeZoneInfo ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        TimeZoneInfo utc = TimeZoneInfo.Utc;

        // 변환 왕복과 기준 시각
        check("달력: unix 0 = 1970-01-01 00:00 UTC", GameCalendar.ToUnixMs(new DateTime(1970, 1, 1), utc) == 0);
        check("달력: KST 09:00 = UTC 00:00", Ms(2026, 11, 19, 9, 0, 0, kst) == Ms(2026, 11, 19, 0, 0, 0, utc));
        long t = Ms(2026, 11, 19, 15, 33, 20, kst);
        check("달력: ToLocal 왕복", GameCalendar.ToLocal(t, kst) == new DateTime(2026, 11, 19, 15, 33, 20));

        // 한국 시간 새벽: UTC 날짜는 전날이다. 이 구간에서 "오늘"이 현지 날짜여야 한다(원본 코드가 틀리던 곳).
        long kst0330 = Ms(2026, 11, 19, 3, 30, 0, kst);
        check("달력: KST 03:30의 하루 시작은 같은 날 00:00", GameCalendar.StartOfDayMs(kst0330, kst) == Ms(2026, 11, 19, 0, 0, 0, kst));
        check("달력: KST 03:30 기준 다음 05:00 초기화는 같은 날 05:00", GameCalendar.NextDailyMs(kst0330, 5, 0, 0, kst) == Ms(2026, 11, 19, 5, 0, 0, kst));
        check("달력: KST 06:00 기준 다음 05:00 초기화는 다음 날", GameCalendar.NextDailyMs(Ms(2026, 11, 19, 6, 0, 0, kst), 5, 0, 0, kst) == Ms(2026, 11, 20, 5, 0, 0, kst));
        long at5 = Ms(2026, 11, 19, 5, 0, 0, kst);
        check("달력: 정확히 초기화 시각이면 Next는 다음 날, Prev는 지금", GameCalendar.NextDailyMs(at5, 5, 0, 0, kst) == Ms(2026, 11, 20, 5, 0, 0, kst) && GameCalendar.PrevDailyMs(at5, 5, 0, 0, kst) == at5);
        check("달력: Prev는 아직 안 온 시각이면 전날", GameCalendar.PrevDailyMs(kst0330, 5, 0, 0, kst) == Ms(2026, 11, 18, 5, 0, 0, kst));

        // 게임 날짜(초기화 5시)
        check("달력: 04:59는 전날, 05:00부터 새 날", GameCalendar.GameDayIndex(Ms(2026, 11, 19, 4, 59, 59, kst), 5, kst) + 1 == GameCalendar.GameDayIndex(at5, 5, kst));
        check("달력: IsSameGameDay(05:00 ~ 다음 날 04:59:59)", GameCalendar.IsSameGameDay(at5, Ms(2026, 11, 20, 4, 59, 59, kst), 5, kst) && !GameCalendar.IsSameGameDay(at5, Ms(2026, 11, 20, 5, 0, 0, kst), 5, kst));
        check("달력: 자정 초기화(0)는 달력 날짜", GameCalendar.GameDayIndex(Ms(2026, 11, 19, 0, 0, 0, kst), 0, kst) == (long)(new DateTime(2026, 11, 19) - new DateTime(1970, 1, 1)).TotalDays);

        // 주간
        long wed = Ms(2026, 11, 18, 12, 0, 0, kst);   // 2026-11-18은 수요일
        check("달력: 수요일 12시 기준 다음 월요일 05:00", GameCalendar.NextWeeklyMs(wed, DayOfWeek.Monday, 5, 0, 0, kst) == Ms(2026, 11, 23, 5, 0, 0, kst));
        check("달력: 같은 요일이지만 시각이 지났으면 다음 주", GameCalendar.NextWeeklyMs(wed, DayOfWeek.Wednesday, 5, 0, 0, kst) == Ms(2026, 11, 25, 5, 0, 0, kst));
        check("달력: 같은 요일, 시각이 안 지났으면 오늘", GameCalendar.NextWeeklyMs(wed, DayOfWeek.Wednesday, 18, 0, 0, kst) == Ms(2026, 11, 18, 18, 0, 0, kst));

        // 하루 중 구간
        TimeSpan h(int hh, int mm = 0) => new TimeSpan(hh, mm, 0);
        long Kst(int hh, int mm) => Ms(2026, 11, 19, hh, mm, 0, kst);
        check("달력: 구간 [10:00,12:00) 안/경계/밖", GameCalendar.IsInDailyWindow(Kst(10, 0), h(10), h(12), kst) && GameCalendar.IsInDailyWindow(Kst(11, 59), h(10), h(12), kst)
            && !GameCalendar.IsInDailyWindow(Kst(12, 0), h(10), h(12), kst) && !GameCalendar.IsInDailyWindow(Kst(9, 59), h(10), h(12), kst));
        check("달력: 자정을 넘는 구간 [23:00,02:00)", GameCalendar.IsInDailyWindow(Kst(23, 30), h(23), h(2), kst) && GameCalendar.IsInDailyWindow(Kst(1, 59), h(23), h(2), kst)
            && !GameCalendar.IsInDailyWindow(Kst(2, 0), h(23), h(2), kst) && !GameCalendar.IsInDailyWindow(Kst(12, 0), h(23), h(2), kst));
        check("달력: start==end는 빈 구간", !GameCalendar.IsInDailyWindow(Kst(10, 0), h(10), h(10), kst));
        check("달력: 구간은 UTC가 아니라 현지 시각 기준", GameCalendar.IsInDailyWindow(Ms(2026, 11, 19, 1, 0, 0, utc), h(10), h(11), kst));   // UTC 01:00 = KST 10:00

        // 서머타임(뉴욕): 2026-03-08 02:00→03:00 건너뜀, 2026-11-01 02:00→01:00 되돌림
        long nyNoonBefore = Ms(2026, 3, 7, 12, 0, 0, ny);
        long nyNext0230 = GameCalendar.NextDailyMs(nyNoonBefore, 2, 30, 0, ny);
        check("달력(DST): 건너뛰는 날 02:30 초기화는 구간이 끝난 직후(03:00 현지)", GameCalendar.ToLocal(GameCalendar.NextDailyMs(Ms(2026, 3, 8, 0, 0, 0, ny), 2, 30, 0, ny), ny) == new DateTime(2026, 3, 8, 3, 0, 0));
        check("달력(DST): 02:30 초기화 다음 값은 항상 단조 증가", nyNext0230 > nyNoonBefore);
        long dstDayStart = GameCalendar.StartOfDayMs(Ms(2026, 3, 8, 12, 0, 0, ny), ny);
        long dstDayEnd = GameCalendar.StartOfDayMs(Ms(2026, 3, 9, 12, 0, 0, ny), ny);
        check("달력(DST): 봄에 하루 길이는 23시간", dstDayEnd - dstDayStart == 23L * 3600_000);
        long fallStart = GameCalendar.StartOfDayMs(Ms(2026, 11, 1, 12, 0, 0, ny), ny);
        long fallEnd = GameCalendar.StartOfDayMs(Ms(2026, 11, 2, 12, 0, 0, ny), ny);
        check("달력(DST): 가을에 하루 길이는 25시간", fallEnd - fallStart == 25L * 3600_000);
        // 두 번 나타나는 01:30은 첫 번째(EDT, UTC-4) = 05:30Z
        check("달력(DST): 두 번 나타나는 01:30은 첫 번째", GameCalendar.ToUnixMs(new DateTime(2026, 11, 1, 1, 30, 0), ny) == Ms(2026, 11, 1, 5, 30, 0, utc));
        // 01:30 일일 초기화: 두 번째 01:30(EST)에 서 있으면 다음 초기화는 다음 날(첫 번째 01:30은 이미 지났다)
        long secondOccurrence = Ms(2026, 11, 1, 6, 30, 0, utc);   // 01:30 EST
        check("달력(DST): 되돌림 날 두 번째 01:30에서 다음 01:30 초기화는 다음 날", GameCalendar.ToLocal(GameCalendar.NextDailyMs(secondOccurrence, 1, 30, 0, ny), ny) == new DateTime(2026, 11, 2, 1, 30, 0));

        // 모델 대조: 임의 시각에서 Next/Prev의 성질
        Random rnd = new Random(7);
        bool ok = true;
        foreach (TimeZoneInfo tz in new[] { kst, ny, utc })
        {
            for (int i = 0; i < 4000 && ok; ++i)
            {
                long now = 1_700_000_000_000L + (long)(rnd.NextDouble() * 3 * 365 * 86_400_000.0);
                int hh = rnd.Next(24), mm = rnd.Next(60);
                long nx = GameCalendar.NextDailyMs(now, hh, mm, 0, tz);
                long pv = GameCalendar.PrevDailyMs(now, hh, mm, 0, tz);
                ok = nx > now && pv <= now && nx - pv <= 25L * 3600_000 + 1 && nx - now <= 25L * 3600_000 + 1 && now - pv < 25L * 3600_000 + 1;
                long wk = GameCalendar.NextWeeklyMs(now, (DayOfWeek)rnd.Next(7), hh, mm, 0, tz);
                ok &= wk > now && wk - now <= 7L * 86_400_000 + 3600_000;
            }
        }
        check("달력: Next/Prev 성질 퍼징(KST/NY/UTC 12000회)", ok);
        check("달력: 잘못된 인자는 예외", Throws(() => GameCalendar.NextDailyMs(0, 24, 0, 0, kst)) && Throws(() => GameCalendar.NextDailyMs(0, 0, 60, 0, kst))
            && Throws(() => GameCalendar.GameDayIndex(0, 24, kst)) && Throws(() => GameCalendar.IsInDailyWindow(0, TimeSpan.FromDays(1), TimeSpan.Zero, kst)));
    }

    private static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }

    // ---------------------------------------------------------------------------------------------
    private static void DirectoryHashTests(Action<string, bool> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "dirhash_" + Guid.NewGuid().ToString("N"));
        string a = Path.Combine(root, "a"), b = Path.Combine(root, "deeper", "b");
        try
        {
            foreach (string d in new[] { a, b })
            {
                Directory.CreateDirectory(Path.Combine(d, "sub"));
                File.WriteAllText(Path.Combine(d, "Hero.json"), "{\"x\":1}");
                File.WriteAllText(Path.Combine(d, "sub", "Monster.json"), "[1,2,3]");
                File.WriteAllText(Path.Combine(d, "note.txt"), "hi");
            }
            string ha = DirectoryHash.Compute(a), hb = DirectoryHash.Compute(b);
            check("폴더 해시: 64자 소문자 16진수", ha.Length == 64 && ha == ha.ToLowerInvariant());
            check("폴더 해시: 위치가 달라도 내용이 같으면 같은 값", ha == hb);
            check("폴더 해시: 반복 호출 같은 값", DirectoryHash.Compute(a) == ha);

            File.WriteAllText(Path.Combine(b, "sub", "Monster.json"), "[1,2,4]");
            check("폴더 해시: 내용이 한 글자 달라지면 다른 값", DirectoryHash.Compute(b) != ha);
            File.WriteAllText(Path.Combine(b, "sub", "Monster.json"), "[1,2,3]");
            check("폴더 해시: 되돌리면 다시 같은 값", DirectoryHash.Compute(b) == ha);

            File.WriteAllText(Path.Combine(b, "extra.json"), "{}");
            check("폴더 해시: 파일이 늘면 다른 값", DirectoryHash.Compute(b) != ha);
            check("폴더 해시: include로 늘어난 파일을 빼면 같은 값", DirectoryHash.Compute(b, p => p != "extra.json") == ha);
            File.Delete(Path.Combine(b, "extra.json"));

            File.Move(Path.Combine(b, "note.txt"), Path.Combine(b, "note2.txt"));
            check("폴더 해시: 이름만 바뀌어도 다른 값", DirectoryHash.Compute(b) != ha);
            File.Move(Path.Combine(b, "note2.txt"), Path.Combine(b, "note.txt"));

            // 이름/내용 경계 모호성: ("ab" 내용 "c") 와 ("a" 내용 "bc")
            string c1 = Path.Combine(root, "c1"), c2 = Path.Combine(root, "c2");
            Directory.CreateDirectory(c1); Directory.CreateDirectory(c2);
            File.WriteAllText(Path.Combine(c1, "ab"), "c");
            File.WriteAllText(Path.Combine(c2, "a"), "bc");
            check("폴더 해시: 이름/내용 경계가 섞이지 않는다", DirectoryHash.Compute(c1) != DirectoryHash.Compute(c2));

            string empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
            string empty2 = Path.Combine(root, "empty2"); Directory.CreateDirectory(empty2);
            check("폴더 해시: 빈 폴더끼리 같고 비어 있지 않은 폴더와 다르다", DirectoryHash.Compute(empty) == DirectoryHash.Compute(empty2) && DirectoryHash.Compute(empty) != ha);

            // 큰 파일(청크 경계 넘김)
            string big1 = Path.Combine(root, "big1"), big2 = Path.Combine(root, "big2");
            Directory.CreateDirectory(big1); Directory.CreateDirectory(big2);
            byte[] data = new byte[5 * 1024 * 1024 + 123];
            new Random(3).NextBytes(data);
            File.WriteAllBytes(Path.Combine(big1, "d.bin"), data);
            File.WriteAllBytes(Path.Combine(big2, "d.bin"), data);
            string hbig = DirectoryHash.Compute(big1);
            check("폴더 해시: 5MB 파일 같은 내용 같은 값", hbig == DirectoryHash.Compute(big2));
            data[data.Length - 1] ^= 1;
            File.WriteAllBytes(Path.Combine(big2, "d.bin"), data);
            check("폴더 해시: 5MB 파일 마지막 비트 하나만 달라도 다른 값", hbig != DirectoryHash.Compute(big2));

            bool missing = false;
            try { DirectoryHash.Compute(Path.Combine(root, "none")); } catch (DirectoryNotFoundException) { missing = true; }
            check("폴더 해시: 없는 폴더는 DirectoryNotFoundException", missing);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---------------------------------------------------------------------------------------------
    private static void AtomicEnumTests(Action<string, bool> check)
    {
        // 모든 기반 형식에서 값이 그대로 돌아온다(부호 있는 음수 포함)
        AtomicEnum<B> ab = new AtomicEnum<B>(B.C);
        AtomicEnum<S> asx = new AtomicEnum<S>(S.A);
        AtomicEnum<I> ai = new AtomicEnum<I>(I.A);
        AtomicEnum<L> al = new AtomicEnum<L>(L.A);
        AtomicEnum<U> au = new AtomicEnum<U>(U.B);
        check("AtomicEnum: 초기값 왕복(byte/short/int/long/uint)", ab.Value == B.C && asx.Value == S.A && ai.Value == I.A && al.Value == L.A && au.Value == U.B);
        check("AtomicEnum: Exchange가 이전 값을 돌려준다", ab.Exchange(B.A) == B.C && ab.Value == B.A && asx.Exchange(S.B) == S.A && asx.Value == S.B);
        check("AtomicEnum: CompareExchange 성공/실패", ai.CompareExchange(I.B, I.A) == I.A && ai.Value == I.B && ai.CompareExchange(I.C, I.A) == I.B && ai.Value == I.B);
        check("AtomicEnum: TryTransition 성공/실패", al.TryTransition(L.A, L.B) && al.Value == L.B && !al.TryTransition(L.A, L.B));
        check("AtomicEnum: 기본 초기값은 0", new AtomicEnum<U>().Value == U.A && new AtomicEnum<B>().Value == 0);

        // 경합: 같은 전이를 여러 스레드가 시도하면 정확히 하나만 성공
        int rounds = 3000, threads = 8;
        bool onlyOne = true;
        for (int r = 0; r < rounds && onlyOne; ++r)
        {
            AtomicEnum<B> st = new AtomicEnum<B>(B.A);
            int wins = 0;
            using Barrier barrier = new Barrier(threads);
            Thread[] ts = new Thread[threads];
            for (int i = 0; i < threads; ++i)
            {
                ts[i] = new Thread(() => { barrier.SignalAndWait(); if (st.TryTransition(B.A, B.B)) Interlocked.Increment(ref wins); });
                ts[i].Start();
            }
            foreach (Thread t in ts) t.Join();
            onlyOne = wins == 1 && st.Value == B.B;
            if (r >= 300) break;      // 300회로 충분(스레드 생성 비용)
        }
        check("AtomicEnum: 8스레드 TryTransition은 하나만 성공(byte 열거형)", onlyOne);

        // CAS 루프 카운터(열거형을 정수처럼 올림)
        AtomicEnum<I> cnt = new AtomicEnum<I>((I)0);
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 20_000; ++i)
            {
                while (true)
                {
                    I cur = cnt.Value;
                    if (cnt.TryTransition(cur, (I)((int)cur + 1))) break;
                }
            }
        });
        check("AtomicEnum: 8스레드 CAS 루프 합계 160000", (int)cnt.Value == 160_000);

        // InterlockedEx
        I loc = I.A;
        check("InterlockedEx: int 열거형 CompareExchange/Exchange", InterlockedEx.CompareExchange(ref loc, I.B, I.A) == I.A && loc == I.B && InterlockedEx.Exchange(ref loc, I.C) == I.B && loc == I.C);
        L lloc = L.A;
        check("InterlockedEx: long 열거형", InterlockedEx.CompareExchange(ref lloc, L.B, L.A) == L.A && lloc == L.B && InterlockedEx.Exchange(ref lloc, L.A) == L.B);
        B bloc = B.A;
        bool unsupported = false;
        try { InterlockedEx.Exchange(ref bloc, B.B); } catch (NotSupportedException) { unsupported = true; }
        check("InterlockedEx: byte 열거형은 NotSupportedException", unsupported);
    }

    // ---------------------------------------------------------------------------------------------
    private static readonly AsyncLocal<int> s_ambient = new AsyncLocal<int>();

    private static void BackgroundJobTests(Action<string, bool> check)
    {
        Action<Exception>? saved = BackgroundJob.ExceptionHandler;
        try
        {
            // 동기 작업이 실행되고 호출자는 기다리지 않는다
            using ManualResetEventSlim done = new ManualResetEventSlim();
            int callerThread = Environment.CurrentManagedThreadId, jobThread = -1;
            BackgroundJob.Run(() => { jobThread = Environment.CurrentManagedThreadId; done.Set(); });
            check("BackgroundJob: 동기 작업 실행(다른 스레드)", done.Wait(2000) && jobThread != callerThread);

            // 예외는 처리기로 가고 프로세스는 계속 산다
            List<Exception> seen = new List<Exception>();
            using ManualResetEventSlim got = new ManualResetEventSlim();
            BackgroundJob.ExceptionHandler = e => { lock (seen) seen.Add(e); got.Set(); };
            BackgroundJob.Run(() => throw new InvalidOperationException("sync-boom"));
            check("BackgroundJob: 동기 예외가 처리기로 전달", got.Wait(2000) && seen.Count == 1 && seen[0].Message == "sync-boom");

            // 비동기 작업: await 뒤의 예외, 동기적으로 던지는 함수 모두 처리기로
            got.Reset(); seen.Clear();
            BackgroundJob.Run(async () => { await Task.Delay(10); throw new InvalidOperationException("async-boom"); });
            bool a1 = got.Wait(2000);
            got.Reset();
            BackgroundJob.Run((Func<Task>)(() => throw new InvalidOperationException("sync-throw-in-func")));
            bool a2 = got.Wait(2000);
            check("BackgroundJob: 비동기 예외/함수가 동기로 던진 예외도 처리기로 전달", a1 && a2 && seen.Count == 2 && seen[0].Message == "async-boom" && seen[1].Message == "sync-throw-in-func");

            // 비동기 작업이 정상 완료되고 await 이후까지 실행된다
            using ManualResetEventSlim fin = new ManualResetEventSlim();
            int steps = 0;
            BackgroundJob.Run(async () => { steps = 1; await Task.Delay(20); steps = 2; fin.Set(); });
            check("BackgroundJob: 비동기 작업이 끝까지 실행", fin.Wait(2000) && steps == 2);

            // 처리기가 예외를 던져도 무시
            got.Reset();
            BackgroundJob.ExceptionHandler = e => { got.Set(); throw new Exception("handler-fail"); };
            BackgroundJob.Run(() => throw new Exception("x"));
            check("BackgroundJob: 처리기가 던져도 프로세스는 산다", got.Wait(2000));

            // ExecutionContext(AsyncLocal)가 작업으로 넘어가지 않는다
            BackgroundJob.ExceptionHandler = null;
            s_ambient.Value = 42;
            int observed = -1;
            using ManualResetEventSlim seenCtx = new ManualResetEventSlim();
            BackgroundJob.Run(() => { observed = s_ambient.Value; seenCtx.Set(); });
            seenCtx.Wait(2000);
            check("BackgroundJob: AsyncLocal 값이 작업으로 흘러가지 않는다", observed == 0);
            s_ambient.Value = 0;

            // 이미 흐름이 막힌 곳에서 불러도 예외가 나지 않는다(원본은 SuppressFlow 중복으로 던졌다)
            bool nestedOk = true;
            using (ExecutionContext.SuppressFlow())
            {
                try { BackgroundJob.Run(() => { }); } catch { nestedOk = false; }
            }
            check("BackgroundJob: SuppressFlow 안에서 불러도 안전", nestedOk);

            // 처리기가 없으면 LogThrottle로 로그만 남기고 죽지 않는다
            LogThrottle.Clear();
            using ManualResetEventSlim after = new ManualResetEventSlim();
            BackgroundJob.Run(() => throw new Exception("logged-only"));
            BackgroundJob.Run(() => after.Set());
            check("BackgroundJob: 처리기 없이 예외가 나도 다음 작업이 돈다", after.Wait(2000));

            check("BackgroundJob: null 인자는 예외", Throws(() => BackgroundJob.Run((Action)null!)) && Throws(() => BackgroundJob.Run((Func<Task>)null!)));
        }
        finally { BackgroundJob.ExceptionHandler = saved; }
    }

    // ---------------------------------------------------------------------------------------------
    private static void HistoryBufferTests(Action<string, bool> check)
    {
        check("HistoryBuffer: 용량은 2의 거듭제곱으로 올림", new HistoryBuffer<int>(1).Capacity == 1 && new HistoryBuffer<int>(8).Capacity == 8 && new HistoryBuffer<int>(10).Capacity == 16 && new HistoryBuffer<int>(100).Capacity == 128);
        check("HistoryBuffer: 잘못된 용량/번호는 예외", Throws(() => new HistoryBuffer<int>(0)) && Throws(() => new HistoryBuffer<int>(4).Set(-1, 0)));

        HistoryBuffer<int> h = new HistoryBuffer<int>(4);
        check("HistoryBuffer: 처음엔 Newest=-1, 아무 번호도 없다", h.Newest == -1 && !h.TryGet(0, out _) && !h.Contains(0));
        for (int i = 0; i < 4; ++i) h.Set(i, i * 10);
        check("HistoryBuffer: 용량만큼 쓰면 모두 조회", h.Newest == 3 && h.TryGet(0, out int v0) && v0 == 0 && h.TryGet(3, out int v3) && v3 == 30);
        h.Set(4, 40);
        check("HistoryBuffer: 한 바퀴 돌면 가장 오래된 번호가 사라진다", !h.TryGet(0, out _) && h.TryGet(1, out int v1) && v1 == 10 && h.TryGet(4, out int v4) && v4 == 40);
        check("HistoryBuffer: 덮어써진 옛 번호에 쓰기는 거부되고 최신 값을 해치지 않는다", !h.Set(0, 999) && h.TryGet(4, out int v4b) && v4b == 40 && !h.Set(0, 1));
        check("HistoryBuffer: 아직 안 쓴 미래 번호는 없다", !h.TryGet(5, out _) && !h.TryGet(9, out _));

        // 번호 건너뜀: 같은 슬롯의 옛 값이 새 번호로 보이면 안 된다
        HistoryBuffer<string> g = new HistoryBuffer<string>(4);
        g.Set(0, "a"); g.Set(1, "b"); g.Set(7, "z");      // 7과 3은 같은 슬롯, 3은 쓴 적 없음
        check("HistoryBuffer: 건너뛴 번호는 없다(슬롯이 같아도 옛 값을 돌려주지 않는다)", !g.TryGet(3, out _) && g.TryGet(7, out string? z) && z == "z" && !g.TryGet(0, out _));
        g.Set(5, "e");
        check("HistoryBuffer: 창 안의 늦은 쓰기는 허용", g.TryGet(5, out string? e) && e == "e" && g.Newest == 7);

        // TryGetAtOrBefore
        HistoryBuffer<int> t = new HistoryBuffer<int>(8);
        t.Set(10, 100); t.Set(12, 120); t.Set(15, 150);
        check("HistoryBuffer: AtOrBefore는 가장 가까운 이전 번호", t.TryGetAtOrBefore(14, out long f, out int fv) && f == 12 && fv == 120 && t.TryGetAtOrBefore(15, out long f2, out _) && f2 == 15);
        check("HistoryBuffer: 창 밖이거나 비었으면 실패", !t.TryGetAtOrBefore(9, out _, out _) && !new HistoryBuffer<int>(4).TryGetAtOrBefore(3, out _, out _) && t.TryGetAtOrBefore(1000, out long f3, out _) && f3 == 15);
        t.Clear();
        check("HistoryBuffer: Clear 뒤 비어 있다", t.Newest == -1 && !t.TryGet(15, out _));

        // 모델 대조 퍼징: Dictionary 모델과 "최근 Capacity 안" 규칙으로 비교
        Random rnd = new Random(11);
        bool ok = true;
        foreach (int cap in new[] { 1, 2, 4, 16 })
        {
            HistoryBuffer<long> hb = new HistoryBuffer<long>(cap);
            Dictionary<long, long> model = new Dictionary<long, long>();
            long newest = -1;
            for (int i = 0; i < 20_000 && ok; ++i)
            {
                long seq = newest < 0 ? rnd.Next(0, 5) : Math.Max(0, newest + rnd.Next(-cap - 2, cap + 3));
                bool accepted = hb.Set(seq, seq * 7 + 1);
                bool modelAccepted = newest < 0 || seq > newest - cap;
                if (accepted != modelAccepted) { ok = false; break; }
                if (modelAccepted) { model[seq] = seq * 7 + 1; if (seq > newest) newest = seq; }
                for (int k = 0; k < 4 && ok; ++k)
                {
                    long q = Math.Max(0, newest + rnd.Next(-cap - 3, 4));
                    bool expectHas = model.TryGetValue(q, out long mv) && q > newest - cap;
                    bool has = hb.TryGet(q, out long gv);
                    ok = has == expectHas && (!has || gv == mv) && hb.Newest == newest;
                }
            }
        }
        check("HistoryBuffer: 모델 대조 퍼징(용량 1/2/4/16, 8만 회)", ok);
    }
}
