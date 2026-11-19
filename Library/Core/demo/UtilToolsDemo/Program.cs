// Program.cs : Core.Util 공용 유틸(풀, 타이머, ID, 제한기, 지표, 토큰, 재시도, 난수, 캐시, 해시 링, 설정, 비트, 명령줄, 이벤트, 상태기계, 공간해시, 압축) 검증.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;

namespace Core.Demo;

internal static class Program
{
    private static int s_failures;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
        if (!ok)
            s_failures++;
    }

    private enum S { Idle, Chase, Attack, Dead }
    private sealed class Cfg { public int Port { get; set; } public string Name { get; set; } = ""; }
    private readonly record struct Ev(int V);

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "probe") { Probe.Run(); return 0; }
        if (args.Length > 0 && args[0] == "soak")
            return Soak.Run(args.Length > 1 ? int.Parse(args[1]) : 120);
        if (args.Length > 0 && args[0] == "bench")
        {
            Bench();
            return 0;
        }

        // ---- Pooling
        int made = 0;
        var pool = new ObjectPool<List<int>>(() => { ++made; return new List<int>(); }, 2, l => l.Clear());
        var a = pool.Rent(); a.Add(1); pool.Return(a);
        var b = pool.Rent();
        Check("ObjectPool 재사용+reset", ReferenceEquals(a, b) && b.Count == 0 && made == 1);
        var c1 = pool.Rent(); var c2 = pool.Rent(); var c3 = pool.Rent();
        pool.Return(c1); pool.Return(c2); pool.Return(c3); // 용량 2 → 하나는 버려진다
        Check("ObjectPool 용량 초과는 버림", made == 4);
        using (var lease = ListPool<int>.Lease()) { lease.Value.Add(5); }
        var l2 = ListPool<int>.Rent();
        Check("ListPool 반납 시 비워짐", l2.Count == 0);
        var big = new List<int>(ListPool<int>.MaxRetainedCapacity + 1);
        ListPool<int>.Return(big);
        Check("ListPool 큰 리스트 안 보관", !ReferenceEquals(ListPool<int>.Rent(), big));
        var sb = StringBuilderPool.Rent(); sb.Append("ab");
        Check("StringBuilderPool", StringBuilderPool.ToStringAndReturn(sb) == "ab" && StringBuilderPool.Rent().Length == 0);

        // ---- TimerWheel
        var tw = new TimerWheel(0, 10);
        var fired = new List<string>();
        tw.Schedule(100, () => fired.Add("a100"));
        tw.Schedule(35, () => fired.Add("b35"));
        tw.Schedule(5000, () => fired.Add("c5000"));
        tw.Schedule(70000, () => fired.Add("d70000"));
        var hc = tw.Schedule(50, () => fired.Add("cancelled"));
        Check("Timer 취소", tw.Cancel(hc) && !tw.Cancel(hc));
        tw.Advance(30);
        Check("Timer 이른 실행 없음", fired.Count == 0);
        tw.Advance(40);
        Check("Timer 35ms", fired.Count == 1 && fired[0] == "b35");
        tw.Advance(100);
        Check("Timer 100ms", fired.Count == 2 && fired[1] == "a100");
        tw.Advance(4990);
        Check("Timer 5000ms 직전 미실행", fired.Count == 2);
        tw.Advance(5000);
        Check("Timer 5000ms(상위 단 캐스케이드)", fired.Count == 3 && fired[2] == "c5000");
        tw.Advance(69990);
        Check("Timer 70000ms 직전 미실행", fired.Count == 3);
        tw.Advance(70000);
        Check("Timer 70000ms", fired.Count == 4 && tw.Count == 0);
        int rep = 0;
        var tw2 = new TimerWheel(1000, 10);
        var rh = tw2.ScheduleRepeating(100, () => ++rep);
        for (long t = 1000; t <= 1550; t += 10) tw2.Advance(t);
        Check("Timer 반복 5회", rep == 5);
        tw2.Cancel(rh);
        tw2.Advance(2000);
        Check("Timer 반복 취소", rep == 5 && tw2.Count == 0);
        var tw3 = new TimerWheel(0, 10);
        int chain = 0;
        tw3.Schedule(10, () => { ++chain; tw3.Schedule(10, () => ++chain); });
        tw3.Advance(10);
        Check("Timer 콜백 안 예약은 다음 틱", chain == 1);
        tw3.Advance(20);
        Check("Timer 콜백 안 예약 실행", chain == 2);
        int errs = 0;
        var tw4 = new TimerWheel(0, 10, _ => ++errs);
        int after = 0;
        tw4.Schedule(10, () => throw new InvalidOperationException());
        tw4.Schedule(10, () => ++after);
        tw4.Advance(10);
        Check("Timer 콜백 예외 격리", errs == 1 && after == 1);
        var tw5 = new TimerWheel(0, 10);
        TimerHandle self = default;
        bool selfCancel = true;
        self = tw5.ScheduleRepeating(10, () => { if (!tw5.Cancel(self)) selfCancel = false; });
        tw5.Advance(10); tw5.Advance(100);
        Check("Timer 반복 콜백 안 자기 취소", selfCancel && tw5.Count == 0);

        // TimerWheel: 단순 구현과 무작위 비교(지연 0~3천만 ms, 취소, 큰 시간 점프, 시각 되돌림 없음)
        {
            var prng = new FastRandom(2026);
            bool diffOk = true;
            string diffMsg = "";
            for (int round = 0; round < 40 && diffOk; ++round)
            {
                long start = 1000;
                var wheel = new TimerWheel(start, 10);
                var model = new List<(int Id, long DueTick, bool Cancelled)>();
                var handles = new List<TimerHandle>();
                var firedNow = new List<int>();
                long maxNow = start;
                int nextId = 0;
                for (int step = 0; step < 300 && diffOk; ++step)
                {
                    int ops = prng.Next(0, 6);
                    for (int o = 0; o < ops; ++o)
                    {
                        if (prng.Chance(0.75) || model.Count == 0)
                        {
                            long delay = prng.Chance(0.5) ? prng.Next(0, 3000) : prng.Chance(0.7) ? prng.Next(0, 400_000) : prng.Next(0, 30_000_000);
                            int id = nextId++;
                            long dueTick = (maxNow - start) / 10 + (delay <= 0 ? 1 : (delay + 9) / 10);
                            handles.Add(wheel.Schedule(delay, () => firedNow.Add(id)));
                            model.Add((id, dueTick, false));
                        }
                        else
                        {
                            int k = prng.Next(model.Count);
                            bool wasPending = !model[k].Cancelled && model[k].DueTick > (maxNow - start) / 10;
                            bool got = wheel.Cancel(handles[k]);
                            if (got != wasPending && !(got == false && !wasPending)) { diffOk = false; diffMsg = "cancel mismatch"; }
                            model[k] = (model[k].Id, model[k].DueTick, true);
                        }
                    }
                    long adv = prng.Chance(0.15) ? prng.Next(10_000, 6_000_000) : prng.Next(1, 800);
                    maxNow += adv;
                    firedNow.Clear();
                    wheel.Advance(maxNow);
                    long targetTick = (maxNow - start) / 10;
                    var expect = new List<int>();
                    for (int i = 0; i < model.Count; ++i)
                        if (!model[i].Cancelled && model[i].DueTick <= targetTick) { expect.Add(model[i].Id); model[i] = (model[i].Id, model[i].DueTick, true); }
                    var gotIds = new List<int>(firedNow); gotIds.Sort(); expect.Sort();
                    if (gotIds.Count != expect.Count) { diffOk = false; diffMsg = $"round {round} step {step}: fired {gotIds.Count} expected {expect.Count}"; break; }
                    for (int i = 0; i < gotIds.Count; ++i) if (gotIds[i] != expect[i]) diffOk = false;
                }
                if (diffOk)
                {
                    int pending = 0;
                    foreach (var m in model) if (!m.Cancelled) ++pending;
                    if (wheel.Count != pending) { diffOk = false; diffMsg = "Count mismatch"; }
                }
            }
            if (!diffOk) Console.WriteLine("  " + diffMsg);
            Check("TimerWheel 무작위 비교(점프·캐스케이드·취소 포함)", diffOk);
        }
        {
            var wj = new TimerWheel(0, 10);
            int reps = 0;
            wj.ScheduleRepeating(100, () => ++reps);
            wj.Advance(3_600_000);
            Check("TimerWheel 오래 쉰 뒤 반복 타이머는 몰아 실행하지 않음", reps == 1);
            wj.Advance(3_600_100);
            Check("TimerWheel 위상 유지", reps == 2);
            bool reentry = false;
            var wr = new TimerWheel(0, 10);
            wr.Schedule(10, () => { try { wr.Advance(1000); } catch (InvalidOperationException) { reentry = true; } });
            wr.Advance(10);
            Check("TimerWheel Advance 재진입 거절", reentry);
        }

        // ---- Snowflake
        long fake = 1_800_000_000_000;
        var gen = new SnowflakeIdGenerator(7, null, () => fake);
        var seen = new HashSet<ulong>();
        ulong prev = 0; bool mono = true;
        for (int i = 0; i < 10000; ++i)
        {
            ulong id = gen.Next();
            if (!seen.Add(id) || id <= prev) mono = false;
            prev = id;
            if (i == 5000) fake -= 5;       // 시계 역행
        }
        Check("Snowflake 유일+단조(같은 ms 4096 초과, 시계 역행 포함)", mono);
        var parsed = gen.Parse(gen.Next());
        Check("Snowflake Parse 노드", parsed.NodeId == 7);
        var gen2 = new SnowflakeIdGenerator(8, null, () => fake);
        Check("Snowflake 노드별 다름", gen.Next() != gen2.Next());
        bool threw = false;
        try { _ = new SnowflakeIdGenerator(1024); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check("Snowflake 노드 범위", threw);
        var tasks = new List<Task>();
        var bag = new System.Collections.Concurrent.ConcurrentBag<ulong>();
        var gen3 = new SnowflakeIdGenerator(1);
        for (int i = 0; i < 8; ++i) tasks.Add(Task.Run(() => { for (int k = 0; k < 20000; ++k) bag.Add(gen3.Next()); }));
        Task.WaitAll(tasks.ToArray());
        Check("Snowflake 멀티스레드 유일", new HashSet<ulong>(bag).Count == 160000);

        // ---- RateLimiting
        var tb = new TokenBucket(5, 10, 0);
        int ok = 0;
        for (int i = 0; i < 8; ++i) if (tb.TryTake(0)) ++ok;
        Check("TokenBucket 버스트 5", ok == 5);
        Check("TokenBucket 대기 시간", tb.WaitMs(0) == 100);
        Check("TokenBucket 100ms 후 1개", tb.TryTake(100) && !tb.TryTake(100));
        Check("TokenBucket 상한 캡", tb.Peek(1_000_000) == 5);
        var sw = new SlidingWindowCounter(3, 1000, 10);
        Check("SlidingWindow 3회 허용", sw.TryAcquire(0) && sw.TryAcquire(100) && sw.TryAcquire(200) && !sw.TryAcquire(300));
        Check("SlidingWindow 창 밖 회복", sw.TryAcquire(1100) && sw.Count(1100) >= 1);
        Check("SlidingWindow 오래 뒤 초기화", sw.Count(1_000_000) == 0);
        var kr = new KeyedRateLimiter<int>(2, 0);
        Check("Keyed 키별 독립", kr.TryTake(1, 0) && kr.TryTake(1, 0) && !kr.TryTake(1, 0) && kr.TryTake(2, 0));
        Check("Keyed Sweep", kr.Sweep(100000, 1000) == 2 && kr.KeyCount == 0);

        // ---- Logging
        long now = 0;
        Check("LogThrottle 첫 통과", LogThrottle.TryAcquire("k", 1000, now, out long sup) && sup == 0);
        Check("LogThrottle 간격 내 차단", !LogThrottle.TryAcquire("k", 1000, 500, out _) && !LogThrottle.TryAcquire("k", 1000, 900, out _));
        Check("LogThrottle 이후 통과+억제 횟수", LogThrottle.TryAcquire("k", 1000, 1000, out sup) && sup == 2);

        // ---- Metrics
        var reg = new MetricRegistry();
        var cnt = reg.Counter("pkt_total", "패킷");
        cnt.Inc(); cnt.Inc(4);
        reg.Gauge("players").Set(12.5);
        var hist = reg.Histogram("tick_ms", "", new[] { 1.0, 5.0, 10.0 });
        foreach (double v in new[] { 0.5, 3, 3, 7, 50 }) hist.Observe(v);
        Check("Counter", cnt.Value == 5 && ReferenceEquals(cnt, reg.Counter("pkt_total")));
        Check("Histogram 개수/합", hist.Count == 5 && Math.Abs(hist.Sum - 63.5) < 1e-9);
        Check("Histogram 분위수", hist.Quantile(0.5) == 5 && hist.Quantile(0.99) == 10 && hist.Quantile(0.1) == 1);
        string prom = reg.ExportPrometheus();
        Check("Prometheus 출력", prom.Contains("pkt_total 5") && prom.Contains("players 12.5") && prom.Contains("tick_ms_bucket{le=\"5\"} 3")
            && prom.Contains("tick_ms_bucket{le=\"+Inf\"} 5") && prom.Contains("tick_ms_count 5") && prom.Contains("# TYPE tick_ms histogram"));
        bool kindClash = false, badName = false;
        try { reg.Gauge("pkt_total"); } catch (InvalidOperationException) { kindClash = true; }
        try { reg.Counter("1bad"); } catch (ArgumentException) { badName = true; }
        Check("Metric 종류 충돌/이름 검증", kindClash && badName);
        var cc = reg.Counter("par");
        Parallel.For(0, 8, _ => { for (int i = 0; i < 100000; ++i) cc.Inc(); });
        Check("Counter 멀티스레드", cc.Value == 800000);

        // ---- Security
        var signer = new AuthTokenSigner("0123456789abcdef-secret");
        var nonces = new NonceCache();
        string tok = signer.Issue("acct1", 10_000);
        Check("토큰 정상", signer.Verify(tok, 1000, out string acc) == TokenStatus.Ok && acc == "acct1");
        Check("토큰 만료", signer.Verify(tok, 10_000, out _) == TokenStatus.Expired);
        Check("토큰 변조", signer.Verify(tok.Substring(0, tok.Length - 3) + "AAA", 1000, out _) == TokenStatus.BadSignature);
        Check("토큰 형식 오류", signer.Verify("garbage", 1000, out _) == TokenStatus.Malformed && signer.Verify("a.b", 1000, out _) != TokenStatus.Ok);
        Check("토큰 다른 키", new AuthTokenSigner("another-secret-key-xyz").Verify(tok, 1000, out _) == TokenStatus.BadSignature);
        Check("토큰 재사용 방지", signer.Verify(tok, 1000, out _, nonces) == TokenStatus.Ok && signer.Verify(tok, 1001, out _, nonces) == TokenStatus.Replayed);
        Check("NonceCache 만료 후 청소", nonces.TryUse("zz", 5, 0) && nonces.TryUse("yy", 100_000_000, 20_000) && nonces.Count == 1 && nonces.TryUse("zz", 100_000_000, 20_001));
        bool shortKey = false;
        try { _ = new AuthTokenSigner("short"); } catch (ArgumentException) { shortKey = true; }
        Check("토큰 짧은 키 거절", shortKey);

        // ---- Resilience
        var rp = new RetryPolicy(3, 1, 4, 2.0, 0);
        int tries = 0;
        int rv = rp.Execute(() => { if (++tries < 3) throw new IOException(); return 9; });
        Check("Retry 성공까지 재시도", rv == 9 && tries == 3);
        tries = 0;
        bool lastThrown = false;
        try { rp.Execute<int>(() => { ++tries; throw new IOException(); }); } catch (IOException) { lastThrown = true; }
        Check("Retry 마지막 예외 전달", lastThrown && tries == 3);
        tries = 0;
        try { rp.Execute<int>(() => { ++tries; throw new InvalidOperationException(); }, e => e is IOException); } catch (InvalidOperationException) { }
        Check("Retry 조건 불일치 즉시 중단", tries == 1);
        Check("Retry 지연 증가/상한", rp.DelayAfter(1).TotalMilliseconds == 1 && rp.DelayAfter(2).TotalMilliseconds == 2 && rp.DelayAfter(10).TotalMilliseconds == 4);
        int at = 0;
        int av = rp.ExecuteAsync<int>(async _ => { await Task.Yield(); if (++at < 2) throw new IOException(); return 1; }).GetAwaiter().GetResult();
        Check("Retry 비동기", av == 1 && at == 2);

        long cbNow = 0;
        var cb = new CircuitBreaker(3, 1000, () => cbNow);
        for (int i = 0; i < 3; ++i) { Check("CB 닫힘 허용 " + i, cb.TryAllow()); cb.OnFailure(); }
        Check("CB 열림", cb.State == CircuitState.Open && !cb.TryAllow());
        cbNow = 1000;
        Check("CB 반열림 시험 한 건", cb.State == CircuitState.HalfOpen && cb.TryAllow() && !cb.TryAllow());
        cb.OnFailure();
        Check("CB 시험 실패 → 다시 열림", cb.State == CircuitState.Open && !cb.TryAllow());
        cbNow = 2000;
        Check("CB 시험 성공 → 닫힘", cb.TryAllow() && Run(() => cb.OnSuccess()) && cb.State == CircuitState.Closed && cb.TryAllow());
        bool openEx = false;
        var cb2 = new CircuitBreaker(1, 1000, () => cbNow);
        try { cb2.Execute<int>(() => throw new IOException()); } catch (IOException) { }
        try { cb2.Execute(() => 1); } catch (CircuitOpenException) { openEx = true; }
        Check("CB Execute 열림 예외", openEx);

        // ---- Random
        var r1 = new FastRandom(42); var r2 = new FastRandom(42);
        bool same = true;
        for (int i = 0; i < 100; ++i) if (r1.NextUInt64() != r2.NextUInt64()) same = false;
        Check("FastRandom 같은 시드 재현", same);
        var rr = new FastRandom(1);
        var hit = new int[10];
        bool inRange = true;
        for (int i = 0; i < 100000; ++i) { int v = rr.Next(10); if (v < 0 || v >= 10) inRange = false; else hit[v]++; }
        bool uniform = true;
        foreach (int h in hit) if (h < 9000 || h > 11000) uniform = false;
        Check("FastRandom Next 범위/균등", inRange && uniform);
        int mn = int.MaxValue, mx = int.MinValue;
        for (int i = 0; i < 10000; ++i) { int v = rr.Next(-5, 5); mn = Math.Min(mn, v); mx = Math.Max(mx, v); }
        Check("FastRandom 음수 범위", mn == -5 && mx == 4);
        double dsum = 0; bool dok = true;
        for (int i = 0; i < 100000; ++i) { double d = rr.NextDouble(); if (d < 0 || d >= 1) dok = false; dsum += d; }
        Check("FastRandom NextDouble", dok && Math.Abs(dsum / 100000 - 0.5) < 0.01);
        var wc = new int[3];
        double[] ws = { 1, 0, 3 };
        for (int i = 0; i < 40000; ++i) wc[rr.PickWeighted(ws)]++;
        Check("PickWeighted 비율/0 제외", wc[1] == 0 && wc[2] > wc[0] * 2.7 && wc[2] < wc[0] * 3.3 && rr.PickWeighted(new double[] { 0, -1 }) == -1);
        var wt = new WeightedTable(new double[] { 1, 2, 7 });
        var wtc = new int[3];
        for (int i = 0; i < 100000; ++i) wtc[wt.Pick(rr)]++;
        Check("WeightedTable(alias) 비율", Math.Abs(wtc[0] - 10000) < 1000 && Math.Abs(wtc[1] - 20000) < 1500 && Math.Abs(wtc[2] - 70000) < 2000);
        var sh = new List<int>(); for (int i = 0; i < 50; ++i) sh.Add(i);
        rr.Shuffle(sh);
        var sorted = new List<int>(sh); sorted.Sort();
        bool perm = true; for (int i = 0; i < 50; ++i) if (sorted[i] != i) perm = false;
        Check("Shuffle 순열 보존+섞임", perm && (sh[0] != 0 || sh[1] != 1));
        Check("FastRandom.Shared 스레드별", Task.Run(() => FastRandom.Shared).Result != FastRandom.Shared);

        // ---- Collections
        var lru = new LruCache<int, string>(2);
        var evicted = new List<int>();
        lru.OnEvicted = (k, _) => evicted.Add(k);
        lru.Set(1, "a"); lru.Set(2, "b");
        lru.TryGet(1, out _);
        lru.Set(3, "c");
        Check("LRU 가장 오래된 것 제거", evicted.Count == 1 && evicted[0] == 2 && lru.Contains(1) && lru.Contains(3) && !lru.Contains(2));
        lru.Set(1, "A");
        Check("LRU 갱신", lru.TryGet(1, out string v1) && v1 == "A" && lru.Count == 2);
        Check("LRU Remove/Clear", lru.Remove(1) && !lru.Remove(1) && Run(() => lru.Clear()) && lru.Count == 0);
        var ttl = new TtlCache<string, int>(1000);
        ttl.Set("x", 1, 0); ttl.Set("y", 2, 0, 5000);
        Check("TTL 만료 전", ttl.TryGet("x", 999, out int tv) && tv == 1);
        Check("TTL 만료 후", !ttl.TryGet("x", 1000, out _) && ttl.Count == 1);
        Check("TTL 개별 TTL/Sweep", ttl.TryGet("y", 4999, out _) && ttl.Sweep(5000) == 1 && ttl.Count == 0);

        // ---- Distribution
        var ring = new HashRing<string>(100);
        Check("HashRing 빈 링", !ring.TryGetNode("k", out _));
        foreach (string n in new[] { "gw1", "gw2", "gw3", "gw4" }) ring.AddNode(n);
        var assign = new Dictionary<int, string>();
        var load = new Dictionary<string, int>();
        for (int i = 0; i < 20000; ++i)
        {
            ring.TryGetNode((long)i, out string n);
            assign[i] = n;
            load[n] = load.GetValueOrDefault(n) + 1;
        }
        bool balanced = true;
        foreach (int cntv in load.Values) if (cntv < 3000 || cntv > 7500) balanced = false;
        Check("HashRing 분산 균형", load.Count == 4 && balanced);
        ring.RemoveNode("gw4");
        int moved = 0, wrongMoved = 0;
        for (int i = 0; i < 20000; ++i)
        {
            ring.TryGetNode((long)i, out string n);
            if (n != assign[i]) { ++moved; if (assign[i] != "gw4") ++wrongMoved; }
        }
        Check("HashRing 노드 제거 시 해당 노드 키만 이동", wrongMoved == 0 && moved == load["gw4"]);
        ring.TryGetNode("some-key", out string s1); ring.TryGetNode("some-key", out string s2);
        Check("HashRing 결정적", s1 == s2 && HashRing<string>.HashKey("abc") == HashRing<string>.HashKey("abc") && !ring.AddNode("gw1"));

        {
            var ring2 = new HashRing<string>(100);
            foreach (string n in new[] { "a", "b", "c", "d" }) ring2.AddNode(n);
            var cnts = new Dictionary<string, int>();
            for (ulong id = 1; id <= 20000; ++id) { ring2.TryGetNodeByHash(HashRing<string>.HashKey(id), out string n); cnts[n] = cnts.GetValueOrDefault(n) + 1; }
            bool bal = cnts.Count == 4;
            foreach (int v in cnts.Values) if (v < 3000 || v > 7500) bal = false;
            Check("HashRing HashKey(ulong) 경유 분산", bal);
            Check("HashRing 긴 문자열 키", HashRing<string>.HashKey(new string('x', 1000)) != HashRing<string>.HashKey(new string('x', 1001)));
        }

        // ---- 리뷰 수정 확인
        {
            var h = new Histogram(new[] { 1.0, 2.0 });
            h.Observe(double.NaN); h.Observe(1.5); h.Observe(double.PositiveInfinity);
            Check("Histogram NaN 무시/무한대는 합계 제외", h.Count == 2 && h.Sum == 1.5 && h.Snapshot()[2] == 1);
            var arr = new[] { 1.0, 2.0 };
            var h2 = new Histogram(arr); arr[0] = 100;
            Check("Histogram 경계 복사", h2.Bounds[0] == 1.0);
            bool nanBound = false;
            try { _ = new Histogram(new[] { 1.0, double.NaN }); } catch (ArgumentException) { nanBound = true; }
            Check("Histogram NaN 경계 거절", nanBound);

            // SpatialHash: 아주 큰 반경, 비정상 값
            var sp2 = new SpatialHash<int>(1);
            for (int i = 0; i < 100; ++i) sp2.Add(i, i * 3, -i * 2);
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            var bigC = new List<int>();
            sp2.QueryCircle(0, 0, 1e9f, bigC);
            var bigRect = new List<int>();
            sp2.QueryRect(-1e9f, -1e9f, 1e9f, 1e9f, bigRect);
            var inf = new List<int>();
            sp2.QueryCircle(0, 0, float.PositiveInfinity, inf);
            sw0.Stop();
            Check("SpatialHash 아주 큰 반경도 즉시(저장 항목만 순회)", bigC.Count == 100 && bigRect.Count == 100 && inf.Count == 100 && sw0.ElapsedMilliseconds < 500);
            var none = new List<int>();
            sp2.QueryCircle(float.NaN, 0, 5, none); sp2.QueryCircle(0, 0, float.NaN, none); sp2.QueryCircle(0, 0, -1, none);
            bool addThrows = false;
            try { sp2.Add(500, float.NaN, 0); } catch (ArgumentOutOfRangeException) { addThrows = true; }
            Check("SpatialHash 비정상 값 거절", none.Count == 0 && addThrows && !sp2.Move(1, float.PositiveInfinity, 0) && sp2.TryGetPosition(1, out float gx, out float gy) && gx == 3 && gy == -2);
            sp2.Add(900, 3e9f, 3e9f);
            var edge = new List<int>(); sp2.QueryRect(2.9e9f, 2.9e9f, float.MaxValue, float.MaxValue, edge);
            Check("SpatialHash 큰 좌표 가장자리", edge.Contains(900));

            // CircuitBreaker: 시험 호출자 증발, 취소는 실패 아님
            long t0 = 0;
            var cbk = new CircuitBreaker(1, 1000, () => t0);
            cbk.TryAllow(); cbk.OnFailure();
            t0 = 1000;
            bool p1 = cbk.TryAllow(), p2 = cbk.TryAllow();
            t0 = 2000;
            Check("CB 시험 호출자가 사라져도 openMs 뒤 재시도 허용", p1 && !p2 && cbk.TryAllow());
            cbk.OnSuccess();
            var cbc = new CircuitBreaker(1, 1000, () => t0);
            try { cbc.Execute<int>(() => throw new OperationCanceledException()); } catch (OperationCanceledException) { }
            Check("CB 취소는 실패로 세지 않음", cbc.State == CircuitState.Closed);
            try { cbc.Execute<int>(() => throw new ArgumentException(), ex => !(ex is ArgumentException)); } catch (ArgumentException) { }
            Check("CB isFailure 조건", cbc.State == CircuitState.Closed);
            try { cbc.Execute<int>(() => throw new IOException()); } catch (IOException) { }
            Check("CB 일반 예외는 실패", cbc.State == CircuitState.Open);

            // NonceCache: 가득 찬 상태에서 호출마다 청소하지 않음
            var nc = new NonceCache(100);
            for (int i = 0; i < 100; ++i) nc.TryUse("n" + i, 1_000_000, 0);
            var swn = System.Diagnostics.Stopwatch.StartNew();
            bool anyOk = false;
            for (int i = 0; i < 200000; ++i) if (nc.TryUse("x" + i, 1_000_000, 10)) anyOk = true;
            swn.Stop();
            Check("NonceCache 가득 찬 뒤 거절(청소는 간격 제한)", !anyOk && swn.ElapsedMilliseconds < 1500);
            Check("NonceCache 만료 후 다시 수용", nc.TryUse("fresh", 3_000_000, 2_000_000));

            // LogThrottle: 동적 키 폭주에도 항목 수 상한(이미 있던 정적 키는 그대로 동작)
            LogThrottle.TryAcquire("static-key", 10_000_000, 0, out _);
            for (int i = 0; i < 5000; ++i) LogThrottle.TryAcquire("dyn-" + i, 100000, 1000 + i, out _);
            Check("LogThrottle 동적 키 폭주에도 항목 수 상한", LogThrottle.KeyCount <= LogThrottle.MaxKeys + 1);
            Check("LogThrottle 기존 정적 키 동작 유지", !LogThrottle.TryAcquire("static-key", 10_000_000, 500, out _) && LogThrottle.TryAcquire("static-key", 1000, 20_000_000, out long ss) && ss == 1);

            // CommandLine: 값 누락
            bool missing = false;
            try { new CommandLine(new[] { "--port" }).GetInt("port", 1); } catch (FormatException) { missing = true; }
            Check("Cli 값 누락은 예외", missing && new CommandLine(new[] { "--verbose" }).GetBool("verbose"));

            // BitWriter: 경계 걸침/64비트/마스킹
            byte[] bb = new byte[16];
            var w3 = new BitWriter(bb);
            w3.Write(0x7, 3); w3.Write(ulong.MaxValue, 64); w3.Write(0xFFFF, 5); w3.Write(0x12345, 17);
            var r3 = new BitReader(bb);
            r3.Read(3, out ulong q1); r3.Read(64, out ulong q2); r3.Read(5, out ulong q3); r3.Read(17, out ulong q4);
            Check("BitWriter 바이트 경계 걸침/64비트/상위 비트 마스킹", q1 == 7 && q2 == ulong.MaxValue && q3 == 31 && q4 == 0x12345);
            bool rbOk = true;
            var pr = new FastRandom(11);
            for (int trial = 0; trial < 500; ++trial)
            {
                byte[] buf = new byte[64];
                var w = new BitWriter(buf);
                var specs = new List<(ulong V, int B)>();
                while (true)
                {
                    int nb = pr.Next(1, 65);
                    ulong v = pr.NextUInt64(); if (nb < 64) v &= (1UL << nb) - 1;
                    if (!w.Write(v, nb)) break;
                    specs.Add((v, nb));
                }
                var rd = new BitReader(buf);
                foreach (var spc in specs) { rd.Read(spc.B, out ulong got); if (got != spc.V) rbOk = false; }
            }
            Check("BitWriter/Reader 무작위 왕복", rbOk);
        }

        // ---- HotConfig 구독자 예외
        {
            string d3 = Path.Combine(Path.GetTempPath(), "cfg2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d3);
            string p3 = Path.Combine(d3, "b.json");
            File.WriteAllText(p3, "{\"port\": 1}");
            using (var hc2 = new HotConfig<Cfg>(p3, 50))
            {
                hc2.Changed += _ => throw new InvalidOperationException("subscriber boom");
                File.WriteAllText(p3, "{\"port\": 2}");
                bool re = hc2.Reload();
                Check("HotConfig 구독자 예외가 새지 않음", re && hc2.Current.Port == 2);
            }
            try { Directory.Delete(d3, true); } catch { }
        }

        // ---- Config
        string dir = Path.Combine(Path.GetTempPath(), "cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string cfgPath = Path.Combine(dir, "a.json");
        File.WriteAllText(cfgPath, "{ // 주석\n \"port\": 7000, \"Name\": \"x\", }");
        var loaded = ConfigLoader.Load<Cfg>(cfgPath);
        Check("Config 주석/후행쉼표/대소문자", loaded.Port == 7000 && loaded.Name == "x");
        bool unk = false;
        try { ConfigLoader.Parse<Cfg>("{\"portt\":1}"); } catch (InvalidDataException) { unk = true; }
        Check("Config 알 수 없는 속성 거절", unk);
        Check("Config TryLoad 없는 파일", !ConfigLoader.TryLoad<Cfg>(Path.Combine(dir, "none.json"), out var dflt) && dflt.Port == 0);
        using (var hot = new HotConfig<Cfg>(cfgPath, 50))
        {
            int changed = 0, failed = 0;
            hot.Changed += _ => Interlocked.Increment(ref changed);
            hot.LoadFailed += _ => Interlocked.Increment(ref failed);
            File.WriteAllText(cfgPath, "{\"port\": 7001, \"name\": \"y\"}");
            SpinWait.SpinUntil(() => hot.Current.Port == 7001, 3000);
            Check("HotConfig 변경 감지", hot.Current.Port == 7001 && hot.Version >= 1 && changed >= 1);
            File.WriteAllText(cfgPath, "{ broken");
            SpinWait.SpinUntil(() => failed > 0, 3000);
            Check("HotConfig 잘못된 파일은 기존값 유지", hot.Current.Port == 7001 && failed > 0);
            File.WriteAllText(cfgPath, "{\"port\": 7002}");
            Check("HotConfig 수동 Reload", SpinWait.SpinUntil(() => hot.Current.Port == 7002, 3000) || (hot.Reload() && hot.Current.Port == 7002));
        }
        try { Directory.Delete(dir, true); } catch { }

        // ---- Binary
        Check("BitUtil 2의 거듭제곱", BitUtil.IsPowerOfTwo(64) && !BitUtil.IsPowerOfTwo(65) && BitUtil.NextPowerOfTwo(65) == 128 && BitUtil.NextPowerOfTwo(64) == 64 && BitUtil.NextPowerOfTwo(0) == 1);
        ulong f = BitUtil.Set(0, 5); f = BitUtil.Set(f, 63);
        Check("BitUtil 플래그", BitUtil.Test(f, 5) && BitUtil.Test(f, 63) && !BitUtil.Test(f, 4) && BitUtil.PopCount(f) == 2 && BitUtil.LowestSetBit(f) == 5 && BitUtil.Clear(f, 5) == (1UL << 63));
        Check("BitUtil 필드", BitUtil.GetBits(0xABCD, 4, 8) == 0xBC && BitUtil.SetBits(0xABCD, 4, 8, 0x12) == 0xA12D && BitUtil.GetBits(ulong.MaxValue, 0, 64) == ulong.MaxValue);
        byte[] bits = new byte[8];
        var bw = new BitWriter(bits);
        bw.Write(5, 3); bw.Write(1000, 10); bw.WriteBool(true); bw.Write(0x1FFFFFFFFUL, 33);
        var br = new BitReader(bits);
        br.Read(3, out ulong x1); br.Read(10, out ulong x2); br.ReadBool(out bool x3); br.Read(33, out ulong x4);
        Check("BitWriter/Reader 왕복", x1 == 5 && x2 == 1000 && x3 && x4 == 0x1FFFFFFFFUL && bw.BitLength == 47 && bw.ByteLength == 6);
        byte[] tiny = new byte[1];
        var bw2 = new BitWriter(tiny);
        Check("BitWriter 공간 초과 거절", bw2.Write(0xFF, 8) && !bw2.Write(1, 1));
        var br2 = new BitReader(tiny);
        br2.Read(8, out _);
        Check("BitReader 범위 초과 거절", !br2.Read(1, out _));

        // ---- Cli
        var cl = new CommandLine(new[] { "--zone-id", "5", "--port=7000", "--verbose", "-n", "-5", "pos1", "--ratio", "0.5", "--", "--notopt" });
        Check("Cli 값/등호/플래그", cl.GetInt("zone-id", 0) == 5 && cl.GetInt("PORT", 0) == 7000 && cl.GetBool("verbose") && cl.GetInt("n", 0) == -5);
        Check("Cli 위치 인자/--", cl.Positional.Count == 2 && cl.Positional[0] == "pos1" && cl.Positional[1] == "--notopt");
        Check("Cli 기본값/실수", cl.GetInt("none", 9) == 9 && cl.GetDouble("ratio", 0) == 0.5 && !cl.GetBool("none") && cl.GetBool("none2", true));
        bool badInt = false;
        try { new CommandLine(new[] { "--n", "abc" }).GetInt("n", 0); } catch (FormatException) { badInt = true; }
        Check("Cli 잘못된 정수는 예외", badInt);
        var cl2 = new CommandLine(new[] { "--a", "1", "--typo", "2" });
        cl2.GetInt("a", 0);
        Check("Cli 알 수 없는 옵션 찾기", cl2.Unknown().Count == 1 && cl2.Unknown()[0] == "typo");

        // ---- Events
        var bus = new EventBus();
        var order = new List<int>();
        var sub1 = bus.Subscribe<Ev>(e => order.Add(e.V));
        bus.Subscribe<Ev>(e => throw new Exception("boom"));
        bus.Subscribe<Ev>(e => order.Add(e.V * 10));
        int busErr = 0;
        bus.OnError = _ => ++busErr;
        bus.Publish(new Ev(2));
        Check("EventBus 순서+예외 격리", order.Count == 2 && order[0] == 2 && order[1] == 20 && busErr == 1);
        sub1.Dispose(); sub1.Dispose();
        order.Clear();
        bus.Publish(new Ev(3));
        Check("EventBus 해제", order.Count == 1 && order[0] == 30 && bus.SubscriberCount<Ev>() == 2);
        bus.Publish("no-subscriber");
        IDisposable? selfSub = null; int selfCnt = 0;
        selfSub = bus.Subscribe<int>(_ => { ++selfCnt; selfSub!.Dispose(); });
        bus.Publish(1); bus.Publish(1);
        Check("EventBus 발행 중 해제 안전", selfCnt == 1);
        var sig = new Signal<int>(); int sg = 0;
        var conn = sig.Connect(v => sg += v);
        sig.Emit(2); conn.Dispose(); sig.Emit(2);
        Check("Signal", sg == 2 && sig.Count == 0);

        // ---- Fsm
        var log = new List<string>();
        var sm = new StateMachine<S>(S.Idle, 0)
            .Allow(S.Idle, S.Chase).Allow(S.Chase, S.Attack, S.Idle).Allow(S.Attack, S.Dead, S.Chase)
            .OnEnter(S.Chase, p => log.Add("enter chase from " + p))
            .OnExit(S.Idle, n => log.Add("exit idle to " + n))
            .OnUpdate(S.Chase, t => log.Add("update " + t));
        Check("FSM 허용되지 않은 전이", !sm.TryChange(S.Attack, 10) && sm.Current == S.Idle && !sm.TryChange(S.Idle, 10));
        Check("FSM 전이/훅 순서", sm.TryChange(S.Chase, 100) && log.Count == 2 && log[0] == "exit idle to Chase" && log[1] == "enter chase from Idle");
        sm.Update(150);
        Check("FSM Update/머문 시간", log[2] == "update 150" && sm.TimeInState(150) == 50 && sm.Previous == S.Idle);
        var sm2 = new StateMachine<S>(S.Idle);
        var seq = new List<S>();
        sm2.OnEnter(S.Chase, _ => sm2.TryChange(S.Attack)).OnEnter(S.Attack, _ => seq.Add(S.Attack));
        sm2.TryChange(S.Chase);
        Check("FSM Enter 안 전이 처리", sm2.Current == S.Attack && seq.Count == 1);

        // ---- Spatial
        var sp = new SpatialHash<int>(10);
        var rnd = new FastRandom(7);
        var pos = new Dictionary<int, (float, float)>();
        for (int i = 0; i < 2000; ++i) { float px = rnd.NextFloat() * 400 - 200, py = rnd.NextFloat() * 400 - 200; sp.Add(i, px, py); pos[i] = (px, py); }
        for (int i = 0; i < 500; ++i) { float px = rnd.NextFloat() * 400 - 200, py = rnd.NextFloat() * 400 - 200; sp.Move(i, px, py); pos[i] = (px, py); }
        for (int i = 1500; i < 1600; ++i) { sp.Remove(i); pos.Remove(i); }
        bool qok = true;
        for (int q = 0; q < 50; ++q)
        {
            float cx = rnd.NextFloat() * 400 - 200, cy = rnd.NextFloat() * 400 - 200, rad = rnd.NextFloat() * 60 + 1;
            var got = new List<int>(); sp.QueryCircle(cx, cy, rad, got);
            var exp = new List<int>();
            foreach (var kv in pos) { float dx = kv.Value.Item1 - cx, dy = kv.Value.Item2 - cy; if (dx * dx + dy * dy <= rad * rad) exp.Add(kv.Key); }
            got.Sort(); exp.Sort();
            if (got.Count != exp.Count) { qok = false; break; }
            for (int i = 0; i < got.Count; ++i) if (got[i] != exp[i]) qok = false;
            var gr = new List<int>(); sp.QueryRect(cx - rad, cy - rad, cx + rad, cy + rad, gr);
            int expR = 0;
            foreach (var kv in pos) if (kv.Value.Item1 >= cx - rad && kv.Value.Item1 <= cx + rad && kv.Value.Item2 >= cy - rad && kv.Value.Item2 <= cy + rad) ++expR;
            if (gr.Count != expR) qok = false;
        }
        Check("SpatialHash 원/사각 질의가 브루트포스와 일치(이동/삭제 포함)", qok && sp.Count == pos.Count);
        sp.Add(99999, -0.5f, -0.5f);
        var neg = new List<int>(); sp.QueryCircle(0, 0, 1, neg);
        Check("SpatialHash 음수 좌표/칸 경계", neg.Contains(99999));
        bool moved2 = sp.Move(99999, -0.4f, -0.4f, out bool cellChanged);
        Check("SpatialHash 같은 칸 이동", moved2 && !cellChanged && !sp.Move(-1, 0, 0));
        sp.Clear();
        Check("SpatialHash Clear", sp.Count == 0);

        // ---- Compression
        byte[] raw = Encoding.UTF8.GetBytes(new string('A', 3000) + "tail");
        foreach (CompressionKind k in new[] { CompressionKind.Brotli, CompressionKind.Deflate })
        {
            byte[]? comp = CompressionUtil.Compress(raw, k, 4);
            bool good = comp != null && comp.Length < raw.Length;
            byte[] outBuf = new byte[raw.Length];
            bool dec = comp != null && CompressionUtil.TryDecompress(comp, outBuf, k, out int w) && w == raw.Length && outBuf.AsSpan().SequenceEqual(raw);
            Check("압축 왕복 " + k, good && dec);
            byte[] small = new byte[raw.Length - 1];
            Check("압축 해제 출력 한도 초과 거절 " + k, comp != null && !CompressionUtil.TryDecompress(comp, small, k, out _));
        }
        Check("압축 작은 입력은 생략", CompressionUtil.Compress(new byte[50], CompressionKind.Brotli) == null);
        byte[] rnd2 = new byte[1000]; new FastRandom(3).Shuffle(rnd2);
        var rbytes = new byte[1000]; var rg = new FastRandom(5); for (int i = 0; i < rbytes.Length; ++i) rbytes[i] = (byte)rg.Next(256);
        Check("압축 이득 없으면 null", CompressionUtil.Compress(rbytes, CompressionKind.Brotli) == null);
        Check("압축 손상 데이터 거절", !CompressionUtil.TryDecompress(new byte[] { 1, 2, 3, 4, 5 }, new byte[100], CompressionKind.Deflate, out _));

        // ---- 추가 수정 확인
        {
            var keys = new Dictionary<string, byte[]> { ["old"] = Encoding.UTF8.GetBytes("old-secret-0123456789"), ["new"] = Encoding.UTF8.GetBytes("new-secret-0123456789") };
            var oldSigner = new AuthTokenSigner(new Dictionary<string, byte[]> { ["old"] = keys["old"] }, "old");
            var rotating = new AuthTokenSigner(keys, "new");
            string oldTok = oldSigner.Issue("u1", 10_000), newTok = rotating.Issue("u2", 10_000);
            Check("토큰 키 교체: 새 서명기가 옛 키 토큰도 검증", rotating.Verify(oldTok, 1, out string ua) == TokenStatus.Ok && ua == "u1" && rotating.Verify(newTok, 1, out ua) == TokenStatus.Ok && ua == "u2");
            Check("토큰 키 교체: 옛 서명기는 새 키 토큰을 모른다", oldSigner.Verify(newTok, 1, out _) == TokenStatus.UnknownKey);
            Check("토큰 키 교체: 키ID 바꿔치기 거절", rotating.Verify(oldTok, 1, out _) == TokenStatus.Ok && new AuthTokenSigner(new Dictionary<string, byte[]> { ["old"] = keys["new"] }, "old").Verify(oldTok, 1, out _) == TokenStatus.BadSignature);
            bool badKid = false;
            try { _ = new AuthTokenSigner(keys, "none"); } catch (ArgumentException) { badKid = true; }
            Check("토큰 현재 키ID 검증", badKid);

            byte[] data = Encoding.UTF8.GetBytes(new string('Z', 5000));
            byte[] frame = CompressionFrame.Pack(data);
            Check("압축 프레임 왕복", frame.Length < data.Length && CompressionFrame.Unpack(frame, 10000)!.AsSpan().SequenceEqual(data));
            Check("압축 프레임 상한 초과는 할당 전 거절", CompressionFrame.Unpack(frame, 4999) == null);
            byte[] tinyFrame = CompressionFrame.Pack(new byte[] { 1, 2, 3 });
            Check("압축 프레임 작은 데이터는 원본 그대로", tinyFrame[0] == 0 && CompressionFrame.Unpack(tinyFrame, 10)!.Length == 3);
            byte[] lie = (byte[])frame.Clone(); lie[1] = 0; lie[2] = 0; lie[3] = 1; lie[4] = 0; // 원본 길이를 거짓으로
            Check("압축 프레임 길이 거짓/손상/잘못된 종류 거절", CompressionFrame.Unpack(lie, 1 << 20) == null && CompressionFrame.Unpack(new byte[] { 9, 0, 0, 0, 0 }, 10) == null && CompressionFrame.Unpack(new byte[2], 10) == null);

            bool negInc = false;
            try { new Counter().Inc(-1); } catch (ArgumentOutOfRangeException) { negInc = true; }
            Check("Counter 음수 거절", negInc);
            var twh = new TimerWheel(0, 10);
            int farFired = 0;
            twh.Schedule(long.MaxValue, () => ++farFired);
            twh.ScheduleRepeating(long.MaxValue / 2, () => ++farFired);
            twh.Advance(1000);
            Check("TimerWheel 아주 먼 시각도 넘치지 않음", farFired == 0 && twh.Count == 2);
        }

        // ---- 동시성 스트레스
        {
            // ObjectPool: 동시에 같은 객체를 둘이 들고 있으면 안 된다
            var op = new ObjectPool<int[]>(() => new int[1], 8);
            int dup = 0;
            Parallel.For(0, 8, _ =>
            {
                for (int i = 0; i < 100000; ++i)
                {
                    int[] o = op.Rent();
                    if (Interlocked.Exchange(ref o[0], 1) != 0) Interlocked.Increment(ref dup);
                    Thread.SpinWait(5);
                    Volatile.Write(ref o[0], 0);
                    op.Return(o);
                }
            });
            Check("ObjectPool 동시 임대에 같은 객체 중복 없음", dup == 0);

            // NonceCache: 같은 nonce는 정확히 한 번만 성공
            var ncs = new NonceCache(1_000_000);
            int wins = 0;
            Parallel.For(0, 8, _ => { for (int i = 0; i < 20000; ++i) if (ncs.TryUse("n" + i, long.MaxValue, 0)) Interlocked.Increment(ref wins); });
            Check("NonceCache 동시 사용 시 nonce당 정확히 한 번", wins == 20000);

            // CircuitBreaker: 반열림 시험 호출은 동시에 한 건
            long cnow = 0;
            var ccb = new CircuitBreaker(1, 1000, () => Interlocked.Read(ref cnow));
            ccb.TryAllow(); ccb.OnFailure();
            Interlocked.Exchange(ref cnow, 1000);
            int probes = 0;
            Parallel.For(0, 16, _ => { if (ccb.TryAllow()) Interlocked.Increment(ref probes); });
            Check("CircuitBreaker 반열림 동시 시험 호출 1건", probes == 1);

            // KeyedRateLimiter: Sweep과 TryTake가 동시에 돌아도 한도(용량) 안에서만 통과
            var krl = new KeyedRateLimiter<int>(5, 0);
            int passed = 0;
            using (var cts = new CancellationTokenSource())
            {
                var sweeper = Task.Run(() => { while (!cts.IsCancellationRequested) krl.Sweep(long.MaxValue / 2, long.MaxValue / 4); });
                Parallel.For(0, 4, _ => { for (int i = 0; i < 20000; ++i) if (krl.TryTake(i % 50, 0)) Interlocked.Increment(ref passed); });
                cts.Cancel(); sweeper.Wait();
            }
            Check("KeyedRateLimiter Sweep 동시 실행에도 크래시 없음", passed >= 50 * 5 && passed <= 20000 * 4);

            // HashRing: 쓰기 중 읽기
            var hr = new HashRing<int>(50);
            hr.AddNode(0);
            bool readFail = false;
            using (var cts2 = new CancellationTokenSource())
            {
                var writer = Task.Run(() => { int n = 1; while (!cts2.IsCancellationRequested) { hr.AddNode(n); hr.RemoveNode(n); n = n % 20 + 1; } });
                Parallel.For(0, 4, _ => { for (int i = 0; i < 100000; ++i) if (!hr.TryGetNode((long)i, out _)) readFail = true; });
                cts2.Cancel(); writer.Wait();
            }
            Check("HashRing 노드 변경 중에도 읽기 성공(노드 0 유지)", !readFail);

            // MetricRegistry: 같은 이름 동시 등록은 같은 객체
            var mr = new MetricRegistry();
            var objs = new System.Collections.Concurrent.ConcurrentBag<Counter>();
            Parallel.For(0, 8, _ => { for (int i = 0; i < 1000; ++i) objs.Add(mr.Counter("m" + (i % 10))); });
            var distinct = new HashSet<Counter>(objs);
            Check("MetricRegistry 동시 등록 시 이름당 하나", distinct.Count == 10);

            // LogThrottle: 동시 호출에서 통과 횟수가 간격당 1회
            LogThrottle.Clear();       // 앞선 시험이 키 표를 채워 두었으면 일부 스레드만 넘침 키로 갈 수 있다
            int lt = 0;
            Parallel.For(0, 8, _ => { for (int i = 0; i < 50000; ++i) if (LogThrottle.TryAcquire("par-key", 1_000_000, 50_000_000, out _)) Interlocked.Increment(ref lt); });
            Check("LogThrottle 동시 호출 시 간격당 1회 통과", lt == 1);

            // Snowflake/ListPool 동시
            var lp = new System.Collections.Concurrent.ConcurrentBag<int>();
            Parallel.For(0, 8, _ => { for (int i = 0; i < 50000; ++i) { using var l = ListPool<int>.Lease(); l.Value.Add(i); if (l.Value.Count != 1) lp.Add(1); } });
            Check("ListPool 동시 임대 시 항상 빈 리스트", lp.IsEmpty);
        }

        // ---- 재분석 수정 확인
        {
            var errs2 = 0; var after2 = 0;
            var tw6 = new TimerWheel(0, 10, _ => { ++errs2; throw new InvalidOperationException("handler throws too"); });
            tw6.Schedule(10, () => throw new Exception("a"));
            tw6.Schedule(10, () => ++after2);
            tw6.Schedule(10, () => ++after2);
            tw6.Advance(10);
            Check("TimerWheel 오류 처리기가 던져도 나머지 타이머 실행/개수 유지", errs2 == 1 && after2 == 2 && tw6.Count == 0);

            bool negCost = false;
            try { new TokenBucket(5, 1, 0).TryTake(0, -1); } catch (ArgumentOutOfRangeException) { negCost = true; }
            Check("TokenBucket 음수 비용 거절", negCost);
            Check("Histogram 경계 없음 분위수", Run(() => { var h0 = new Histogram(Array.Empty<double>()); h0.Observe(3); }) && new Histogram(Array.Empty<double>()).Quantile(0.5) == 0);

            // CircuitBreaker: 열린 뒤 늦게 도착한 결과
            long cn = 0;
            var cs = new CircuitBreaker(2, 1000, () => cn);
            cs.TryAllow(); cs.TryAllow(); cs.OnFailure(); cs.OnFailure();      // 열림(시각 0)
            cn = 500; cs.OnSuccess();                                           // 열리기 전에 시작한 호출의 늦은 성공
            Check("CB 열린 뒤 늦은 성공은 닫지 않음", cs.State == CircuitState.Open && !cs.TryAllow());
            cn = 900; cs.OnFailure();                                           // 늦은 실패가 열림 시각을 연장하면 안 됨
            cn = 1000;
            Check("CB 늦은 실패는 열림 시간을 연장하지 않음", cs.State == CircuitState.HalfOpen);

            bool inf = false;
            try { _ = new WeightedTable(new[] { 1.0, double.PositiveInfinity }); } catch (ArgumentException) { inf = true; }
            Check("WeightedTable 무한대 가중치 거절", inf);

            bool b1 = false, b2 = false, b3 = false;
            try { BitUtil.Set(0, 64); } catch (ArgumentOutOfRangeException) { b1 = true; }
            try { BitUtil.GetBits(0, 60, 8); } catch (ArgumentOutOfRangeException) { b2 = true; }
            try { BitUtil.Test(0, -1); } catch (ArgumentOutOfRangeException) { b3 = true; }
            Check("BitUtil 범위 밖 비트/필드 거절", b1 && b2 && b3);

            var cf = new CommandLine(new[] { "--verbose", "file.txt", "--n", "3" }, new[] { "verbose" });
            Check("Cli 선언된 플래그는 뒤 토큰을 삼키지 않음", cf.GetBool("verbose") && cf.Positional.Count == 1 && cf.Positional[0] == "file.txt" && cf.GetInt("n", 0) == 3);
        }

        // ---- 퍼징 / 모델 비교
        {
            var fz = new FastRandom(99);
            var signer2 = new AuthTokenSigner("fuzz-secret-0123456789");
            string good = signer2.Issue("acct", long.MaxValue);
            bool noThrow = true, noFalseOk = true;
            for (int i = 0; i < 200000; ++i)
            {
                string tkn;
                int mode = fz.Next(4);
                if (mode == 0) { var chars = new char[fz.Next(0, 80)]; for (int k = 0; k < chars.Length; ++k) chars[k] = (char)fz.Next(0, 300); tkn = new string(chars); }
                else if (mode == 1) { var cs2 = good.ToCharArray(); cs2[fz.Next(cs2.Length)] = (char)fz.Next(32, 127); tkn = new string(cs2); }
                else if (mode == 2) { tkn = good.Substring(0, fz.Next(good.Length)); }
                else { tkn = good + new string('A', fz.Next(0, 2000)); }
                try
                {
                    var st = signer2.Verify(tkn, 1, out _);
                    if (st == TokenStatus.Ok && tkn != good) { if (noFalseOk) Console.WriteLine("  false-OK sample: mode=" + mode + " len=" + tkn.Length + " diffAt=" + FirstDiff(tkn, good)); noFalseOk = false; }
                }
                catch (Exception ex) { if (noThrow) Console.WriteLine("  throw: " + ex.GetType().Name + " " + ex.Message); noThrow = false; }
            }
            Check("퍼징: AuthToken.Verify 예외 없음/변조 통과 없음(20만 건)", noThrow && noFalseOk);

            byte[] plain = Encoding.UTF8.GetBytes(new string('Q', 4000) + "end");
            byte[] fr = CompressionFrame.Pack(plain);
            bool frOk = true;
            var allocBefore = GC.GetTotalAllocatedBytes(true);
            for (int i = 0; i < 20000; ++i)
            {
                byte[] f2;
                int mode = fz.Next(3);
                if (mode == 0) { f2 = new byte[fz.Next(0, 64)]; for (int k = 0; k < f2.Length; ++k) f2[k] = (byte)fz.Next(256); }
                else if (mode == 1) { f2 = (byte[])fr.Clone(); for (int k = 0; k < 3; ++k) f2[fz.Next(f2.Length)] = (byte)fz.Next(256); }
                else { f2 = fr.AsSpan(0, fz.Next(fr.Length)).ToArray(); }
                try
                {
                    byte[]? res = CompressionFrame.Unpack(f2, 8192);
                    if (res != null && res.Length > 8192) frOk = false;
                }
                catch (Exception) { frOk = false; }
            }
            long allocated = GC.GetTotalAllocatedBytes(true) - allocBefore;
            Check("퍼징: CompressionFrame.Unpack 예외 없음/출력 상한 준수/할당 폭주 없음", frOk && allocated < 20000L * 40_000);

            bool cliOk = true;
            for (int i = 0; i < 20000; ++i)
            {
                var argv = new List<string>();
                int n = fz.Next(0, 8);
                string[] argPool = { "--a", "--b=1", "-x", "-5", "--", "pos", "--n", "abc", "", "--=", "-", "---z", "--c=" };
                for (int k = 0; k < n; ++k) argv.Add(argPool[fz.Next(argPool.Length)]);
                try
                {
                    var c = new CommandLine(argv);
                    c.Has("a"); c.GetString("b"); c.GetBool("x", false); _ = c.Positional; _ = c.Unknown();
                    try { c.GetInt("n", 0); } catch (FormatException) { }
                    try { c.GetDouble("a", 0); } catch (FormatException) { }
                }
                catch (FormatException) { }
                catch (Exception) { cliOk = false; }
            }
            Check("퍼징: CommandLine 예상 밖 예외 없음", cliOk);

            bool cfgOk = true;
            string[] frags = { "{", "}", "\"port\"", ":", "1", ",", "\"name\"", "\"x\"", "[", "]", "null", "//c\n", "1e999", "-", "\"", "9999999999999", "true" };
            for (int i = 0; i < 20000; ++i)
            {
                var sbj = new StringBuilder();
                int n = fz.Next(0, 12);
                for (int k = 0; k < n; ++k) sbj.Append(frags[fz.Next(frags.Length)]);
                try { ConfigLoader.Parse<Cfg>(sbj.ToString()); }
                catch (InvalidDataException) { }
                catch (Exception) { cfgOk = false; }
            }
            Check("퍼징: ConfigLoader 잘못된 JSON은 InvalidDataException만", cfgOk);

            bool bitOk = true;
            for (int i = 0; i < 20000; ++i)
            {
                byte[] raw2 = new byte[fz.Next(0, 9)]; for (int k = 0; k < raw2.Length; ++k) raw2[k] = (byte)fz.Next(256);
                var rd = new BitReader(raw2);
                try { while (rd.Read(fz.Next(-1, 70), out _)) { } }
                catch (Exception) { bitOk = false; }
            }
            Check("퍼징: BitReader 어떤 입력에도 예외 없음", bitOk);

            // LruCache vs 단순 모델
            var lru2 = new LruCache<int, int>(8);
            var model = new List<KeyValuePair<int, int>>();     // 앞이 가장 최근
            bool lruOk = true;
            for (int i = 0; i < 200000 && lruOk; ++i)
            {
                int key = fz.Next(20), op = fz.Next(4);
                if (op == 0)
                {
                    int val = fz.Next(1000);
                    lru2.Set(key, val);
                    model.RemoveAll(kv => kv.Key == key);
                    model.Insert(0, new KeyValuePair<int, int>(key, val));
                    if (model.Count > 8) model.RemoveAt(model.Count - 1);
                }
                else if (op == 1)
                {
                    bool got = lru2.TryGet(key, out int v2);
                    int mi = model.FindIndex(kv => kv.Key == key);
                    if (got != (mi >= 0) || (got && v2 != model[mi].Value)) lruOk = false;
                    if (mi >= 0) { var kv = model[mi]; model.RemoveAt(mi); model.Insert(0, kv); }
                }
                else if (op == 2)
                {
                    bool removed = lru2.Remove(key);
                    int mi = model.FindIndex(kv => kv.Key == key);
                    if (removed != (mi >= 0)) lruOk = false;
                    if (mi >= 0) model.RemoveAt(mi);
                }
                else if (lru2.Contains(key) != model.Exists(kv => kv.Key == key) || lru2.Count != model.Count) lruOk = false;
            }
            Check("LruCache 단순 모델과 20만 연산 일치", lruOk);

            // Snowflake: 무작위 시계 점프(앞뒤)에도 유일 + 단조
            long clk = 1_800_000_000_000;
            var gx = new SnowflakeIdGenerator(5, null, () => clk);
            var seenIds = new HashSet<ulong>(); ulong prevId = 0; bool sfOk = true;
            for (int i = 0; i < 300000 && sfOk; ++i)
            {
                int m = fz.Next(100);
                if (m < 5) clk -= fz.Next(1, 5000);
                else if (m < 20) clk += fz.Next(1, 3);
                else if (m < 22) clk += fz.Next(1000, 100000);
                ulong id = gx.Next();
                if (!seenIds.Add(id) || id <= prevId) sfOk = false;
                prevId = id;
            }
            Check("Snowflake 무작위 시계 앞뒤 점프에도 유일+단조(30만)", sfOk);

            // SlidingWindowCounter: 한도를 절대 넘지 않음(창 내 허용 수 <= limit), 충분히 떨어지면 회복
            var swx = new SlidingWindowCounter(5, 1000, 10);
            var accT = new List<long>(); long tnow = 0; bool swOk = true;
            for (int i = 0; i < 100000 && swOk; ++i)
            {
                tnow += fz.Next(0, 60);
                if (swx.TryAcquire(tnow)) accT.Add(tnow);
                int inWin = 0;
                for (int k = accT.Count - 1; k >= 0 && tnow - accT[k] < 900; --k) ++inWin;     // 칸 하나 오차를 뺀 보수적 창
                if (inWin > 5) swOk = false;
            }
            Check("SlidingWindowCounter 창 안 허용 수가 한도를 넘지 않음", swOk);

            // TokenBucket: 구간 허용량 <= capacity + rate*시간
            var tbx = new TokenBucket(10, 20, 0);
            long tt = 0; int allowed = 0;
            for (int i = 0; i < 100000; ++i) { tt += fz.Next(0, 30); if (tbx.TryTake(tt)) ++allowed; }
            Check("TokenBucket 총 허용량이 capacity + rate*시간 이하", allowed <= 10 + 20.0 * tt / 1000.0 + 1e-6);

            // StateMachine: 허용되지 않은 전이는 절대 일어나지 않음
            var smz = new StateMachine<S>(S.Idle).Allow(S.Idle, S.Chase).Allow(S.Chase, S.Attack, S.Idle).Allow(S.Attack, S.Dead, S.Chase);
            bool smOk = true; S curS = S.Idle;
            for (int i = 0; i < 100000; ++i)
            {
                S to = (S)fz.Next(4);
                bool allowedT = (curS == S.Idle && to == S.Chase) || (curS == S.Chase && (to == S.Attack || to == S.Idle)) || (curS == S.Attack && (to == S.Dead || to == S.Chase));
                bool res = smz.TryChange(to);
                if (res != allowedT) smOk = false;
                if (res) curS = to;
                if (smz.Current != curS) smOk = false;
            }
            Check("StateMachine 무작위 전이가 전이 표와 일치", smOk);

            // HashRing: 노드 추가 시 기존 키는 새 노드로만 이동
            var hz = new HashRing<int>(100);
            for (int i = 0; i < 5; ++i) hz.AddNode(i);
            var before = new int[10000];
            for (int i = 0; i < before.Length; ++i) hz.TryGetNode((long)i, out before[i]);
            hz.AddNode(99);
            int bad = 0, toNew = 0;
            for (int i = 0; i < before.Length; ++i) { hz.TryGetNode((long)i, out int nn); if (nn != before[i]) { if (nn == 99) ++toNew; else ++bad; } }
            Check("HashRing 노드 추가 시 기존 노드끼리의 이동 없음", bad == 0 && toNew > 0 && toNew < before.Length / 3);
        }

        // ---- 추가 퍼징: TimerWheel 반복/단발 혼합, EventBus 발행 중 변경, HotConfig 빠른 재작성
        {
            var fz = new FastRandom(4242);

            // TimerWheel 모델: 반복 타이머는 Advance 한 번에 최대 한 번, 밀린 횟수는 위상을 유지하며 건너뜀
            bool twOk = true; string twMsg = "";
            for (int round = 0; round < 40 && twOk; ++round)
            {
                var w = new TimerWheel(0, 10);
                var firedMap = new Dictionary<int, int>();
                var m = new List<(int Id, long Due, long Interval, bool Cancelled)>();
                var hs = new List<TimerHandle>();
                long nowT = 0; int ids = 0;
                for (int step = 0; step < 250 && twOk; ++step)
                {
                    long curTick = nowT / 10;
                    for (int o = fz.Next(0, 4); o > 0; --o)
                    {
                        int kind = fz.Next(5);
                        if (kind <= 1)
                        {
                            long d = fz.Chance(0.7) ? fz.Next(0, 2000) : fz.Next(0, 3_000_000);
                            int id = ids++;
                            long dt = d <= 0 ? 1 : (d + 9) / 10;
                            hs.Add(w.Schedule(d, () => firedMap[id] = firedMap.GetValueOrDefault(id) + 1));
                            m.Add((id, curTick + dt, 0, false));
                        }
                        else if (kind == 2)
                        {
                            long iv = fz.Chance(0.7) ? fz.Next(1, 500) : fz.Next(1, 200_000);
                            int id = ids++;
                            long it = (iv + 9) / 10;
                            hs.Add(w.ScheduleRepeating(iv, () => firedMap[id] = firedMap.GetValueOrDefault(id) + 1));
                            m.Add((id, curTick + it, it, false));
                        }
                        else if (m.Count > 0)
                        {
                            int k = fz.Next(m.Count);
                            w.Cancel(hs[k]);
                            m[k] = (m[k].Id, m[k].Due, m[k].Interval, true);
                        }
                    }
                    nowT += fz.Chance(0.1) ? fz.Next(5000, 900_000) : fz.Next(1, 300);
                    firedMap.Clear();
                    w.Advance(nowT);
                    long target = nowT / 10;
                    for (int i = 0; i < m.Count; ++i)
                    {
                        var it = m[i];
                        if (it.Cancelled || it.Due > target) { if (firedMap.ContainsKey(it.Id) && !it.Cancelled) { twOk = false; twMsg = "early fire"; } continue; }
                        if (firedMap.GetValueOrDefault(it.Id) != 1) { twOk = false; twMsg = $"round {round} step {step}: id {it.Id} fired {firedMap.GetValueOrDefault(it.Id)} (interval {it.Interval})"; break; }
                        if (it.Interval == 0) m[i] = (it.Id, it.Due, 0, true);
                        else
                        {
                            long nx = it.Due + it.Interval;
                            if (nx <= target) nx += ((target - nx) / it.Interval + 1) * it.Interval;
                            m[i] = (it.Id, nx, it.Interval, false);
                        }
                    }
                    int expectCancelledFired = 0;
                    foreach (var kv in firedMap) { int idx = m.FindIndex(x => x.Id == kv.Key); if (idx < 0) expectCancelledFired++; }
                    if (expectCancelledFired != 0) { twOk = false; twMsg = "unknown id fired"; }
                }
                if (twOk)
                {
                    int pending = 0;
                    foreach (var x in m) if (!x.Cancelled) ++pending;
                    if (w.Count != pending) { twOk = false; twMsg = $"Count {w.Count} != {pending}"; }
                }
            }
            if (!twOk) Console.WriteLine("  " + twMsg);
            Check("퍼징: TimerWheel 반복/단발/취소/점프 혼합이 모델과 일치", twOk);

            // EventBus: 발행 중 구독/해제 — 발행 시작 시점의 구독자 전원이 구독 순서대로 호출된다
            bool ebOk = true;
            for (int round = 0; round < 200 && ebOk; ++round)
            {
                var ebus = new EventBus();
                var live = new List<(int Id, IDisposable Sub)>();
                var subOrder = new List<int>();          // 모델: 구독 순서
                var calls = new List<int>();
                var pending = new List<Action>();
                int nid = 0;
                Action<int> addSub = null!;
                addSub = id =>
                {
                    IDisposable sub = ebus.Subscribe<Ev>(_ =>
                    {
                        calls.Add(id);
                        int r = fz.Next(6);
                        if (r == 0 && live.Count > 0) { int k = fz.Next(live.Count); var tgt = live[k]; tgt.Sub.Dispose(); pending.Add(() => { subOrder.Remove(tgt.Id); }); live.RemoveAt(k); }
                        else if (r == 1) { int nidv = nid++; addSub(nidv); pending.Add(() => subOrder.Add(nidv)); }
                    });
                    live.Add((id, sub));
                };
                for (int i = 0; i < 5; ++i) { int id = nid++; addSub(id); subOrder.Add(id); }
                for (int pub = 0; pub < 30 && ebOk; ++pub)
                {
                    var expected = new List<int>(subOrder);
                    calls.Clear(); pending.Clear();
                    ebus.Publish(new Ev(pub));
                    if (calls.Count != expected.Count) { ebOk = false; break; }
                    for (int i = 0; i < calls.Count; ++i) if (calls[i] != expected[i]) ebOk = false;
                    foreach (var act in pending) act();
                    if (ebus.SubscriberCount<Ev>() != subOrder.Count) ebOk = false;
                }
            }
            Check("퍼징: EventBus 발행 중 구독/해제 모델 일치", ebOk);

            // HotConfig: 빠른 재작성(유효/깨진/원자적 교체 섞임) 뒤 최종 값 수렴, 깨진 값은 절대 적용되지 않음
            string dh = Path.Combine(Path.GetTempPath(), "cfg3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dh);
            string ph = Path.Combine(dh, "c.json");
            File.WriteAllText(ph, "{\"port\": 1}");
            var written = new HashSet<int> { 1 };
            var applied = new List<int>();
            bool hotOk = true;
            using (var hot2 = new HotConfig<Cfg>(ph, 30))
            {
                hot2.Changed += c => { lock (applied) applied.Add(c.Port); };
                int last = 1;
                for (int i = 0; i < 300; ++i)
                {
                    int mode = fz.Next(4);
                    if (mode == 0) File.WriteAllText(ph, "{ broken " + i);
                    else
                    {
                        int port = 1000 + i;
                        string json = "{\"port\": " + port + "}";
                        if (mode == 1) File.WriteAllText(ph, json);
                        else
                        {
                            string tmp = ph + ".tmp";
                            File.WriteAllText(tmp, json);
                            File.Move(tmp, ph, true);
                        }
                        written.Add(port); last = port;
                    }
                    if (fz.Chance(0.5)) Thread.Sleep(fz.Next(0, 15));
                }
                File.WriteAllText(ph, "{\"port\": 77777}"); written.Add(77777); last = 77777;
                bool converged = SpinWait.SpinUntil(() => hot2.Current.Port == last, 8000) || (hot2.Reload() && hot2.Current.Port == last);
                if (!converged) hotOk = false;
                lock (applied) foreach (int p in applied) if (!written.Contains(p)) hotOk = false;
            }
            try { Directory.Delete(dh, true); } catch { }
            Check("퍼징: HotConfig 빠른 재작성 후 최종 값 수렴/깨진 값 미적용", hotOk);
        }

        GameUtilTests.Run(Check);
        JobQueueTests.Run(Check);
        ImportedUtilTests.Run(Check);
        NamedTimersTests.Run(Check);

        Console.WriteLine(s_failures == 0 ? "\nALL PASS" : $"\n{s_failures} FAILED");
        return s_failures == 0 ? 0 : 1;
    }


    private static void Bench()
    {
        static void Run(string name, int iters, Action<int> body)
        {
            body(Math.Min(iters, 1000));   // 워밍업
            var sw = System.Diagnostics.Stopwatch.StartNew();
            body(iters);
            sw.Stop();
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / iters;
            Console.WriteLine($"{name,-42} {ns,10:F1} ns/op   {iters / sw.Elapsed.TotalSeconds / 1e6,8:F2} Mops/s");
        }

        Console.WriteLine("(Release 빌드 권장) 단일 스레드");
        var rng = new FastRandom(1);
        long sink = 0;
        Run("FastRandom.Next(100)", 20_000_000, n => { for (int i = 0; i < n; ++i) sink += rng.Next(100); });
        Run("System.Random.Next(100) (비교)", 20_000_000, n => { var r = new Random(1); for (int i = 0; i < n; ++i) sink += r.Next(100); });
        var tb = new TokenBucket(1e9, 1e9, 0);
        Run("TokenBucket.TryTake", 20_000_000, n => { for (int i = 0; i < n; ++i) if (tb.TryTake(i)) ++sink; });
        var swc = new SlidingWindowCounter(int.MaxValue, 1000);
        Run("SlidingWindowCounter.TryAcquire", 20_000_000, n => { for (int i = 0; i < n; ++i) if (swc.TryAcquire(i / 50)) ++sink; });
        var pool = new ObjectPool<object>(() => new object(), 64);
        Run("ObjectPool Rent+Return", 20_000_000, n => { for (int i = 0; i < n; ++i) { var o = pool.Rent(); pool.Return(o); } });
        Run("ListPool Lease(using)", 10_000_000, n => { for (int i = 0; i < n; ++i) { using var l = ListPool<int>.Lease(); l.Value.Add(i); } });
        Run("new List<int>+Add (비교)", 10_000_000, n => { for (int i = 0; i < n; ++i) { var l = new List<int>(); l.Add(i); sink += l.Count; } });

        var tw = new TimerWheel(0, 10);
        Run("TimerWheel Schedule+Cancel", 5_000_000, n => { for (int i = 0; i < n; ++i) tw.Cancel(tw.Schedule(1000 + i % 5000, static () => { })); });
        int fired = 0;
        var tw2 = new TimerWheel(0, 10);
        Run("TimerWheel Schedule+Advance(실행)", 2_000_000, n => { for (int i = 0; i < n; ++i) { tw2.Schedule(20, () => ++fired); tw2.Advance(i * 10L); } });

        var gen = new SnowflakeIdGenerator(1);
        Run("Snowflake.Next", 10_000_000, n => { for (int i = 0; i < n; ++i) sink += (long)gen.Next(); });
        var lru = new LruCache<int, int>(10000);
        Run("LruCache Set/TryGet(1만 용량)", 10_000_000, n => { for (int i = 0; i < n; ++i) { lru.Set(i % 20000, i); lru.TryGet((i * 7) % 20000, out _); } });
        var ring = new HashRing<int>(160);
        for (int i = 0; i < 16; ++i) ring.AddNode(i);
        Run("HashRing.TryGetNode(long)", 10_000_000, n => { for (int i = 0; i < n; ++i) { ring.TryGetNode((long)i, out int node); sink += node; } });
        Run("HashRing.TryGetNode(string)", 5_000_000, n => { for (int i = 0; i < n; ++i) { ring.TryGetNode("player-12345", out int node); sink += node; } });

        var sp = new SpatialHash<int>(20);
        var prng = new FastRandom(3);
        for (int i = 0; i < 5000; ++i) sp.Add(i, prng.NextFloat() * 1000, prng.NextFloat() * 1000);
        var res = new List<int>();
        Run("SpatialHash 5000개, 반경 30 질의", 500_000, n => { for (int i = 0; i < n; ++i) { res.Clear(); sp.QueryCircle(prng.NextFloat() * 1000, prng.NextFloat() * 1000, 30, res); sink += res.Count; } });
        Run("SpatialHash Move(칸 이동 포함)", 5_000_000, n => { for (int i = 0; i < n; ++i) sp.Move(i % 5000, prng.NextFloat() * 1000, prng.NextFloat() * 1000); });

        byte[] bits = new byte[64];
        Run("BitWriter 20필드(각 13비트) 쓰기", 5_000_000, n => { for (int i = 0; i < n; ++i) { var w = new BitWriter(bits); for (int k = 0; k < 20; ++k) w.Write((ulong)(i + k) & 0x1FFF, 13); } });
        Run("BitReader 20필드 읽기", 5_000_000, n => { for (int i = 0; i < n; ++i) { var r = new BitReader(bits); for (int k = 0; k < 20; ++k) { r.Read(13, out ulong v); sink += (long)v; } } });

        var reg = new MetricRegistry();
        var c = reg.Counter("c"); var h = reg.Histogram("h");
        Run("Counter.Inc", 50_000_000, n => { for (int i = 0; i < n; ++i) c.Inc(); });
        Run("Histogram.Observe", 20_000_000, n => { for (int i = 0; i < n; ++i) h.Observe(i % 300); });

        var signer = new AuthTokenSigner("0123456789abcdef-secret");
        string tok = signer.Issue("account-1", long.MaxValue);
        Run("AuthToken.Verify", 200_000, n => { for (int i = 0; i < n; ++i) if (signer.Verify(tok, 1, out _) == TokenStatus.Ok) ++sink; });

        var ev = new EventBus();
        ev.Subscribe<Ev>(e => sink += e.V);
        Run("EventBus.Publish(구독 1)", 20_000_000, n => { for (int i = 0; i < n; ++i) ev.Publish(new Ev(i)); });

        Console.WriteLine("(sink=" + sink + ")");
    }

    private static int FirstDiff(string a, string b) { int n = Math.Min(a.Length, b.Length); for (int i = 0; i < n; ++i) if (a[i] != b[i]) return i; return n; }

    private static bool Run(Action a) { a(); return true; }
}
