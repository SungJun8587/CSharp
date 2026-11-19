using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;

namespace Core.Demo;

/// <summary>게임 서버용 추가 유틸(힙, 핸들 테이블, 링버퍼, 비트셋, 쿨다운, 버프, 능력치, A*, 쓰기 지연, 종료 조정, 시계)의 정확성과 모델 대조 퍼징.</summary>
internal static class GameUtilTests
{
    public static void Run(Action<string, bool> check)
    {
        MinHeapTests(check);
        HandleTableTests(check);
        RingBufferTests(check);
        BitSetTests(check);
        CooldownTests(check);
        BuffTests(check);
        StatTests(check);
        PathTests(check);
        WriteBehindTests(check);
        ShutdownTests(check);
        TimeProviderTests(check);
        BackoffTests(check);
    }

    // ------------------------------------------------------------ MinHeap
    private static void MinHeapTests(Action<string, bool> check)
    {
        var h = new MinHeap<int>();
        foreach (int v in new[] { 5, 3, 8, 1, 9, 2 }) h.Push(v);
        var outv = new List<int>();
        while (h.TryPop(out int x)) outv.Add(x);
        check("MinHeap 정렬 순서", outv.SequenceEqual(new[] { 1, 2, 3, 5, 8, 9 }));

        var st = new MinHeap<(int pri, string tag)>(Comparer<(int pri, string tag)>.Create((a, b) => a.pri.CompareTo(b.pri)));
        st.Push((1, "a")); st.Push((1, "b")); st.Push((0, "z")); st.Push((1, "c"));
        var tags = new List<string>();
        while (st.TryPop(out var t)) tags.Add(t.tag);
        check("MinHeap 같은 우선순위는 넣은 순서(안정)", string.Join("", tags) == "zabc");

        var h2 = new MinHeap<int>();
        HeapHandle a = h2.Push(10), b = h2.Push(20), c = h2.Push(30);
        check("MinHeap 핸들 Update 앞으로", h2.Update(c, 5) && h2.TryPeek(out int p1) && p1 == 5);
        check("MinHeap 핸들 Update 뒤로", h2.Update(c, 99) && h2.TryPeek(out int p2) && p2 == 10);
        check("MinHeap Remove", h2.Remove(a) && !h2.Contains(a) && !h2.Remove(a) && h2.Count == 2 && h2.TryPeek(out int p3) && p3 == 20);
        h2.TryPop(out _);
        HeapHandle d = h2.Push(1);
        check("MinHeap 재사용된 슬롯의 옛 핸들은 무효", !h2.Contains(b) && !h2.Update(b, 0) && h2.Contains(d));
        h2.Clear();
        check("MinHeap Clear 후 핸들 무효", h2.Count == 0 && !h2.Contains(d) && !h2.Contains(c));

        // 모델 대조: (값, 시퀀스) 정렬 리스트
        var rng = new FastRandom(7);
        var heap = new MinHeap<int>();
        var model = new Dictionary<HeapHandle, (int v, long seq)>();
        long seq = 0;
        bool ok = true;
        for (int i = 0; i < 60000 && ok; ++i)
        {
            int op = rng.Next(0, 10);
            if (op < 4 || model.Count == 0)
            {
                int v = rng.Next(0, 200);
                model[heap.Push(v)] = (v, seq++);
            }
            else if (op < 6)
            {
                var (hh, m) = model.ElementAt(rng.Next(0, model.Count));
                int nv = rng.Next(0, 200);
                ok &= heap.Update(hh, nv);
                model[hh] = (nv, m.seq);
            }
            else if (op < 7)
            {
                var hh = model.ElementAt(rng.Next(0, model.Count)).Key;
                ok &= heap.Remove(hh);
                model.Remove(hh);
            }
            else
            {
                var best = model.OrderBy(kv => kv.Value.v).ThenBy(kv => kv.Value.seq).First();
                ok &= heap.TryPop(out int got) && got == best.Value.v;
                model.Remove(best.Key);
            }
            ok &= heap.Count == model.Count;
        }
        check("MinHeap 퍼징: 추가/갱신/삭제/꺼내기가 정렬 모델과 일치", ok);
    }

    // ------------------------------------------------------------ HandleTable
    private static void HandleTableTests(Action<string, bool> check)
    {
        var t = new HandleTable<string>(2, maxCount: 3);
        HandleId a = t.Add("a"), b = t.Add("b");
        check("HandleTable 조회", t.TryGet(a, out string? va) && va == "a" && t.Count == 2);
        t.Remove(a);
        HandleId c = t.Add("c");
        check("HandleTable 자리 재사용 시 옛 ID 무효", c.Index == a.Index && c != a && !t.Contains(a) && t.TryGet(c, out string? vc) && vc == "c");
        check("HandleTable 없는/기본 ID", !t.Contains(HandleId.None) && !t.Contains(new HandleId(99, 1)) && !t.Remove(a));
        t.Add("d");
        check("HandleTable 최대 개수", !t.TryAdd("e", out _) && t.Count == 3);
        check("HandleTable 64비트 왕복", HandleId.FromUInt64(b.ToUInt64()) == b);
        var seen = new List<string>();
        foreach (var kv in t) seen.Add(kv.Value);
        check("HandleTable 열거", seen.Count == 3 && seen.Contains("b") && seen.Contains("c") && seen.Contains("d"));
        t.Clear();
        check("HandleTable Clear", t.Count == 0 && !t.Contains(b) && !t.Contains(c));

        var rng = new FastRandom(11);
        var tbl = new HandleTable<int>(1);
        var live = new Dictionary<HandleId, int>();
        var dead = new List<HandleId>();
        bool ok = true;
        for (int i = 0; i < 60000 && ok; ++i)
        {
            int op = rng.Next(0, 3);
            if (op == 0 || live.Count == 0)
            {
                int v = rng.Next(0, 1_000_000);
                HandleId id = tbl.Add(v);
                ok &= !live.ContainsKey(id);
                live[id] = v;
            }
            else if (op == 1)
            {
                HandleId id = live.ElementAt(rng.Next(0, live.Count)).Key;
                ok &= tbl.Remove(id, out int got) && got == live[id];
                live.Remove(id);
                dead.Add(id);
                if (dead.Count > 200) dead.RemoveAt(0);
            }
            else
            {
                HandleId id = live.ElementAt(rng.Next(0, live.Count)).Key;
                ok &= tbl.TryGet(id, out int got) && got == live[id];
                if (dead.Count > 0) ok &= !tbl.Contains(dead[rng.Next(0, dead.Count)]);
            }
            ok &= tbl.Count == live.Count;
        }
        int enumerated = 0;
        foreach (var kv in tbl) { ok &= live.TryGetValue(kv.Key, out int v) && v == kv.Value; ++enumerated; }
        check("HandleTable 퍼징: 사전 모델과 일치, 지워진 ID는 끝까지 무효", ok && enumerated == live.Count);
    }

    // ------------------------------------------------------------ RingBuffer
    private static void RingBufferTests(Action<string, bool> check)
    {
        var r = new RingBuffer<int>(3);
        r.Push(1); r.Push(2); r.Push(3);
        check("RingBuffer Reject 모드 가득 참", !r.Push(4) && r.Count == 3 && r[0] == 1 && r[2] == 3);
        r.TryDequeue(out int d1);
        r.Push(4);
        check("RingBuffer 감싸기", d1 == 1 && r[0] == 2 && r[2] == 4 && r.TryPeekNewest(out int nw) && nw == 4);

        var o = new RingBuffer<int>(3, RingFullMode.OverwriteOldest);
        for (int i = 1; i <= 5; ++i) o.Push(i);
        var buf = new int[5];
        int n = o.CopyTo(buf);
        check("RingBuffer 덮어쓰기: 최근 3개만", n == 3 && buf[0] == 3 && buf[1] == 4 && buf[2] == 5);
        o.Clear();
        check("RingBuffer Clear", o.Count == 0 && !o.TryDequeue(out _));

        var rng = new FastRandom(3);
        var ring = new RingBuffer<int>(7, RingFullMode.OverwriteOldest);
        var model = new List<int>();
        bool ok = true;
        for (int i = 0; i < 50000 && ok; ++i)
        {
            if (rng.Next(0, 3) != 0)
            {
                ring.Push(i);
                model.Add(i);
                if (model.Count > 7) model.RemoveAt(0);
            }
            else
            {
                bool got = ring.TryDequeue(out int v);
                ok &= got == (model.Count > 0);
                if (got) { ok &= v == model[0]; model.RemoveAt(0); }
            }
            ok &= ring.Count == model.Count;
            for (int k = 0; k < model.Count && ok; ++k) ok &= ring[k] == model[k];
        }
        check("RingBuffer 퍼징: 리스트 모델과 일치", ok);
    }

    // ------------------------------------------------------------ BitSet / FlagSet64
    private static void BitSetTests(Action<string, bool> check)
    {
        var b = new BitSet(130);
        b.Set(0); b.Set(63); b.Set(64); b.Set(129);
        check("BitSet 기본/경계", b.Get(63) && b.Get(64) && b.Get(129) && !b.Get(1) && b.Count == 4);
        check("BitSet NextSetBit", b.NextSetBit(0) == 0 && b.NextSetBit(1) == 63 && b.NextSetBit(65) == 129 && b.NextSetBit(130) == -1);
        b.SetAll();
        check("BitSet SetAll은 끝 비트를 넘지 않는다", b.Count == 130);
        bool threw = false;
        try { b.Get(130); } catch (ArgumentOutOfRangeException) { threw = true; }
        check("BitSet 범위 검사", threw);

        var rng = new FastRandom(5);
        bool ok = true;
        for (int round = 0; round < 300 && ok; ++round)
        {
            int len = rng.Next(1, 300);
            var x = new BitSet(len); var y = new BitSet(len);
            var mx = new bool[len]; var my = new bool[len];
            for (int i = 0; i < len; ++i)
            {
                if (rng.Next(0, 2) == 0) { x.Set(i); mx[i] = true; }
                if (rng.Next(0, 2) == 0) { y.Set(i); my[i] = true; }
            }
            var z = x.Clone(); z.And(y);
            var o = x.Clone(); o.Or(y);
            var xo = x.Clone(); xo.Xor(y);
            var an = x.Clone(); an.AndNot(y);
            int bits = 0; bool inter = false;
            for (int i = 0; i < len; ++i)
            {
                ok &= z.Get(i) == (mx[i] && my[i]) && o.Get(i) == (mx[i] || my[i]) && xo.Get(i) == (mx[i] ^ my[i]) && an.Get(i) == (mx[i] && !my[i]);
                if (mx[i]) ++bits;
                inter |= mx[i] && my[i];
            }
            ok &= x.Count == bits && x.Intersects(y) == inter && x.Equals(x.Clone());
            var set = new List<int>();
            for (int i = x.NextSetBit(0); i >= 0; i = x.NextSetBit(i + 1)) set.Add(i);
            ok &= set.SequenceEqual(Enumerable.Range(0, len).Where(i => mx[i]));
        }
        check("BitSet 퍼징: bool[] 모델과 일치(AND/OR/XOR/AndNot/열거)", ok);

        FlagSet64 f = FlagSet64.Of(1, 5, 63);
        check("FlagSet64 기본", f.Has(5) && !f.Has(2) && f.Count == 3 && f.With(2).Has(2) && !f.Without(5).Has(5) && f.Toggle(1).Has(1) == false);
        check("FlagSet64 집합 연산", f.HasAll(FlagSet64.Of(1, 5)) && f.HasAny(FlagSet64.Of(5, 9)) && !f.HasAny(FlagSet64.Of(9)) && (f | FlagSet64.Of(2)).Count == 4 && (f & FlagSet64.Of(5, 9)).Count == 1);
        check("FlagSet64 NextSet", f.NextSet(0) == 1 && f.NextSet(2) == 5 && f.NextSet(6) == 63 && f.NextSet(64) == -1 && FlagSet64.Empty.NextSet(0) == -1);
        threw = false;
        try { f.Has(64); } catch (ArgumentOutOfRangeException) { threw = true; }
        check("FlagSet64 범위 검사", threw);
    }

    // ------------------------------------------------------------ CooldownSet
    private static void CooldownTests(Action<string, bool> check)
    {
        var cd = new CooldownSet<int>();
        check("Cooldown 첫 사용", cd.TryUse(1, 1000, 500, 100));
        check("Cooldown 공용 쿨다운이 다른 스킬도 막음", !cd.TryUse(2, 1050, 300) && cd.IsReady(2, 1100) && cd.RemainingMs(2, 1050) == 50);
        check("Cooldown 키 쿨다운", !cd.TryUse(1, 1400, 500) && cd.RemainingMs(1, 1400) == 100 && cd.TryUse(1, 1500, 500));
        check("Cooldown 실패는 상태를 바꾸지 않음", cd.RemainingMs(1, 1600) == 400);
        cd.Reduce(1, 1600, 300);
        check("Cooldown 쿨감", cd.RemainingMs(1, 1600) == 100);
        cd.Reduce(1, 1600, 1000);
        check("Cooldown 쿨감으로 끝남", cd.IsReady(1, 1600) && cd.Count == 0);
        cd.TryUse(3, 2000, 100); cd.TryUse(4, 2000, 5000);
        check("Cooldown Sweep", cd.Sweep(2200) == 1 && cd.Count == 1);
        cd.ResetAll();
        check("Cooldown ResetAll", cd.IsReady(4, 2000) && cd.Count == 0);

        var rng = new FastRandom(9);
        var cs = new CooldownSet<int>();
        var ready = new Dictionary<int, long>();
        long gready = long.MinValue, now = 0;
        bool ok = true;
        for (int i = 0; i < 40000 && ok; ++i)
        {
            now += rng.Next(0, 80);
            int key = rng.Next(0, 12);
            int cdm = rng.Next(0, 400), gcd = rng.Next(0, 3) == 0 ? rng.Next(0, 150) : 0;
            bool expect = now >= gready && (!ready.TryGetValue(key, out long t) || now >= t);
            bool got = cs.TryUse(key, now, cdm, gcd);
            ok &= got == expect;
            if (expect)
            {
                if (cdm > 0) ready[key] = now + cdm; else ready.Remove(key);
                if (gcd > 0) gready = Math.Max(gready, now + gcd);
            }
            long mr = Math.Max(gready, ready.TryGetValue(key, out long tt) ? tt : long.MinValue);
            ok &= cs.RemainingMs(key, now) == Math.Max(0, mr - now);
        }
        check("Cooldown 퍼징: 단순 모델과 일치", ok);
    }

    // ------------------------------------------------------------ BuffTimerSet
    private static void BuffTests(Action<string, bool> check)
    {
        var b = new BuffTimerSet<int>();
        check("Buff 추가", b.Apply(1, 1000, 500, 1, 3) == BuffApplyResult.Added && b.TryGet(1, out var s) && s.ExpiresAtMs == 1500 && s.Stacks == 1);
        b.Apply(1, 1200, 500, 1, 3);
        b.TryGet(1, out s);
        check("Buff 갱신: 만료 재설정 + 중첩 증가", s.ExpiresAtMs == 1700 && s.Stacks == 2 && s.AppliedAtMs == 1000);
        b.Apply(1, 1300, 100, 5, 3, BuffRefresh.KeepLongest);
        b.TryGet(1, out s);
        check("Buff KeepLongest + 최대 중첩", s.ExpiresAtMs == 1700 && s.Stacks == 3);
        b.Apply(1, 1300, 100, 1, 3, BuffRefresh.Extend);
        b.TryGet(1, out s);
        check("Buff Extend", s.ExpiresAtMs == 1800);
        check("Buff Ignore", b.Apply(1, 1300, 9999, 1, 3, BuffRefresh.Ignore) == BuffApplyResult.Ignored && b.TryGet(1, out s) && s.ExpiresAtMs == 1800);
        b.Apply(1, 1400, 200, 1, 3, BuffRefresh.Replace);
        b.TryGet(1, out s);
        check("Buff Replace", s.ExpiresAtMs == 1600 && s.Stacks == 1 && s.AppliedAtMs == 1400);

        var expired = new List<(int, int)>();
        b.Apply(2, 1400, 100, 2, 5);
        b.Apply(3, 1400, 300, 1, 1);
        int n = b.Tick(1550, (k, st) => expired.Add((k, st)));
        check("Buff Tick: 만료만 만료 순서로", n == 1 && expired.Count == 1 && expired[0] == (2, 2) && b.Has(1) && b.Has(3));
        check("Buff 다음 만료 시각", b.TryGetNextExpiry(out long next) && next == 1600);
        check("Buff RemoveStacks", b.RemoveStacks(3, 1, out bool rem) && rem && !b.Has(3));
        check("Buff Remove", b.Remove(1) && !b.Remove(1) && b.Count == 0 && !b.TryGetNextExpiry(out _));

        var re = new BuffTimerSet<int>();
        int reapplied = 0;
        re.Apply(7, 0, 100);
        re.Tick(100, (k, _) => { re.Apply(k, 100, 100); ++reapplied; });
        check("Buff 만료 콜백에서 같은 키 재적용", reapplied == 1 && re.Has(7) && re.TryGet(7, out s) && s.ExpiresAtMs == 200);

        var rng = new FastRandom(21);
        var set = new BuffTimerSet<int>();
        var model = new Dictionary<int, (long exp, int stk)>();
        long now = 0; bool ok = true;
        for (int i = 0; i < 40000 && ok; ++i)
        {
            now += rng.Next(0, 30);
            int op = rng.Next(0, 10);
            int key = rng.Next(0, 15);
            if (op < 5)
            {
                int dur = rng.Next(1, 300), add = rng.Next(1, 3);
                var pol = (BuffRefresh)rng.Next(0, 5);
                set.Apply(key, now, dur, add, 4, pol);
                long ne = now + dur;
                if (!model.TryGetValue(key, out var m)) model[key] = (ne, Math.Min(add, 4));
                else if (pol != BuffRefresh.Ignore)
                {
                    long e2 = pol == BuffRefresh.KeepLongest ? Math.Max(m.exp, ne) : pol == BuffRefresh.Extend ? m.exp + dur : ne;
                    int st = pol == BuffRefresh.Replace ? Math.Min(add, 4) : Math.Min(m.stk + add, 4);
                    model[key] = (e2, st);
                }
            }
            else if (op < 6)
            {
                ok &= set.Remove(key) == model.Remove(key);
            }
            else
            {
                var got = new List<int>();
                set.Tick(now, (k, _) => got.Add(k));
                var want = model.Where(kv => kv.Value.exp <= now).Select(kv => kv.Key).ToList();
                foreach (int k in want) model.Remove(k);
                got.Sort(); want.Sort();
                ok &= got.SequenceEqual(want);
            }
            ok &= set.Count == model.Count;
            foreach (var kv in model)
                ok &= set.TryGet(kv.Key, out var st2) && st2.ExpiresAtMs == kv.Value.exp && st2.Stacks == kv.Value.stk;
        }
        check("Buff 퍼징: 사전 모델과 일치(적용 정책/만료/삭제)", ok);
    }

    // ------------------------------------------------------------ StatModifierSet
    private static void StatTests(Action<string, bool> check)
    {
        var s = new StatModifierSet(4);
        s.SetBase(0, 100);
        s.Add(0, ModKind.Flat, 20, source: 1);
        s.Add(0, ModKind.PercentAdd, 0.1, 2);
        s.Add(0, ModKind.PercentAdd, 0.2, 3);
        s.Add(0, ModKind.Multiply, 2, 4);
        check("Stat 계산식: (기본+가산)×(1+비율합)×배수", Math.Abs(s.Get(0) - 120 * 1.3 * 2) < 1e-9);
        s.RemoveSource(4);
        check("Stat 출처별 제거", Math.Abs(s.Get(0) - 156) < 1e-9 && s.ModifierCount == 3);
        s.Add(0, ModKind.Override, 7, 5);
        check("Stat Override", s.Get(0) == 7);
        s.RemoveSource(5);
        s.SetClamp(0, 0, 150);
        check("Stat 상하한", s.Get(0) == 150);
        s.Add(1, ModKind.PercentAdd, -5, 1);
        s.SetBase(1, 10);
        check("Stat 비율 합이 -100% 이하여도 음수로 뒤집히지 않음", s.Get(1) == 0);

        var changed = new List<int>();
        var t = new StatModifierSet(3);
        t.SetBase(0, 10);
        t.DrainChanged(changed);
        check("Stat 변경 목록: 값이 달라진 것만", changed.SequenceEqual(new[] { 0 }));
        changed.Clear();
        t.Add(0, ModKind.Flat, 5, 1); t.RemoveSource(1);           // 결과가 원래대로
        t.Add(1, ModKind.Flat, 3, 1);
        t.DrainChanged(changed);
        check("Stat 변경 목록: 제자리로 돌아온 값은 제외", changed.SequenceEqual(new[] { 1 }));

        bool threw = false;
        try { t.SetBase(0, double.NaN); } catch (ArgumentOutOfRangeException) { threw = true; }
        check("Stat NaN 거절", threw);

        // 모델: 매번 처음부터 계산
        var rng = new FastRandom(31);
        const int N = 5;
        var set = new StatModifierSet(N);
        var bas = new double[N];
        var mods = new List<(int stat, ModKind k, double v, int src)>();
        bool ok = true;
        var reported = new double[N];
        for (int i = 0; i < 30000 && ok; ++i)
        {
            int op = rng.Next(0, 6);
            int stat = rng.Next(0, N);
            if (op == 0) { double v = rng.Next(0, 500); set.SetBase(stat, v); bas[stat] = v; }
            else if (op <= 3)
            {
                var k = (ModKind)rng.Next(0, 4);
                double v = k == ModKind.Multiply ? rng.Next(5, 30) / 10.0 : k == ModKind.PercentAdd ? rng.Next(-5, 10) / 10.0 : rng.Next(-50, 100);
                int src = rng.Next(0, 8);
                set.Add(stat, k, v, src);
                mods.Add((stat, k, v, src));
            }
            else if (op == 4) { int src = rng.Next(0, 8); set.RemoveSource(src); mods.RemoveAll(m => m.src == src); }
            else
            {
                changed.Clear();
                set.DrainChanged(changed);
                var want = new List<int>();
                for (int st = 0; st < N; ++st) { double v = Model(st); if (v != reported[st]) { want.Add(st); reported[st] = v; } }
                changed.Sort();
                ok &= changed.SequenceEqual(want);
            }
            for (int st = 0; st < N && ok; ++st) ok &= Math.Abs(set.Get(st) - Model(st)) < 1e-6;
        }
        check("Stat 퍼징: 처음부터 다시 계산한 모델과 일치(변경 목록 포함)", ok);

        double Model(int st)
        {
            double flat = 0, pct = 0, mul = 1, ov = 0; bool has = false;
            foreach (var m in mods)
            {
                if (m.stat != st) continue;
                switch (m.k)
                {
                    case ModKind.Flat: flat += m.v; break;
                    case ModKind.PercentAdd: pct += m.v; break;
                    case ModKind.Multiply: mul *= m.v; break;
                    default: ov = m.v; has = true; break;
                }
            }
            return has ? ov : (bas[st] + flat) * Math.Max(0, 1 + pct) * mul;
        }
    }

    // ------------------------------------------------------------ GridPathfinder
    private static void PathTests(Action<string, bool> check)
    {
        var g = new GridPathfinder(10, 10);
        var path = new List<GridPoint>();
        check("A* 직선", g.FindPath(0, 0, 5, 0, path, diagonal: false) && path.Count == 6 && path[0] == new GridPoint(0, 0) && path[5] == new GridPoint(5, 0));
        check("A* 시작=목표", g.FindPath(3, 3, 3, 3, path) && path.Count == 1);
        for (int y = 0; y < 9; ++y) g.SetBlocked(5, y, true);
        check("A* 벽 우회", g.FindPath(0, 0, 9, 0, path, diagonal: false) && path.Count > 10 && !path.Any(p => g.IsBlocked(p.X, p.Y)));
        g.SetBlocked(5, 9, true);
        check("A* 길 없음", !g.FindPath(0, 0, 9, 0, path) && path.Count == 0);
        check("A* 막힌 목표/범위 밖", !g.FindPath(0, 0, 5, 3, path) && !g.FindPath(0, 0, 20, 20, path));
        check("A* 탐색량 제한", !g.FindPath(0, 0, 4, 9, path, maxExpand: 2));

        var c = new GridPathfinder(3, 3);
        c.SetBlocked(1, 0, true); c.SetBlocked(0, 1, true);
        check("A* 시작칸이 두 방향 모두 막히면 대각으로도 못 나감", !c.FindPath(0, 0, 2, 2, path));
        var d = new GridPathfinder(3, 3);
        d.SetBlocked(1, 0, true);
        d.FindPath(0, 0, 1, 1, path);
        check("A* 한쪽만 막혀도 대각 금지(돌아간다)", path.Count == 3 && path[1] == new GridPoint(0, 1));

        // Dijkstra 모델과 최단 비용 비교
        var rng = new FastRandom(41);
        bool ok = true, losOk = true;
        for (int round = 0; round < 400 && ok; ++round)
        {
            int w = rng.Next(2, 30), h = rng.Next(2, 30);
            var pf = new GridPathfinder(w, h);
            double density = rng.Next(0, 40) / 100.0;
            for (int y = 0; y < h; ++y) for (int x = 0; x < w; ++x) pf.SetBlocked(x, y, rng.NextDouble() < density);
            for (int q = 0; q < 5; ++q)
            {
                int sx = rng.Next(0, w), sy = rng.Next(0, h), gx = rng.Next(0, w), gy = rng.Next(0, h);
                bool dg = rng.Next(0, 2) == 0;
                bool found = pf.FindPath(sx, sy, gx, gy, path, dg);
                int best = Dijkstra(pf, sx, sy, gx, gy, dg);
                ok &= found == (best >= 0);
                if (found)
                {
                    ok &= PathCost(pf, path, dg) == best && path[0] == new GridPoint(sx, sy) && path[^1] == new GridPoint(gx, gy);
                    var sm = new List<GridPoint>(path);
                    pf.Smooth(sm);
                    losOk &= sm[0] == path[0] && sm[^1] == path[^1] && sm.Count <= path.Count;
                    for (int i = 1; i < sm.Count; ++i) losOk &= pf.HasLineOfSight(sm[i - 1].X, sm[i - 1].Y, sm[i].X, sm[i].Y);
                }
            }
        }
        check("A* 퍼징: Dijkstra 최단 비용과 일치(4/8방향, 모서리 규칙 포함)", ok);
        check("A* 경로 다듬기: 구간마다 직선 통과 가능, 끝점 유지", losOk);

        static int PathCost(GridPathfinder pf, List<GridPoint> p, bool dg)
        {
            int cost = 0;
            for (int i = 1; i < p.Count; ++i)
            {
                int dx = Math.Abs(p[i].X - p[i - 1].X), dy = Math.Abs(p[i].Y - p[i - 1].Y);
                if (dx + dy == 1) cost += 10;
                else if (dg && dx == 1 && dy == 1 && !pf.IsBlocked(p[i].X, p[i - 1].Y) && !pf.IsBlocked(p[i - 1].X, p[i].Y)) cost += 14;
                else return -2;     // 규칙에 어긋난 이동
                if (pf.IsBlocked(p[i].X, p[i].Y)) return -3;
            }
            return cost;
        }

        static int Dijkstra(GridPathfinder pf, int sx, int sy, int gx, int gy, bool dg)
        {
            if (pf.IsBlocked(sx, sy) || pf.IsBlocked(gx, gy)) return -1;
            var dist = new Dictionary<(int, int), int> { [(sx, sy)] = 0 };
            var pq = new PriorityQueue<(int, int), int>();
            pq.Enqueue((sx, sy), 0);
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (pq.TryDequeue(out var cur, out int dcur))
            {
                if (dcur > dist[cur]) continue;
                if (cur == (gx, gy)) return dcur;
                for (int k = 0; k < (dg ? 8 : 4); ++k)
                {
                    int nx = cur.Item1 + dx[k], ny = cur.Item2 + dy[k];
                    if (pf.IsBlocked(nx, ny)) continue;
                    int cost = 10;
                    if (k >= 4) { if (pf.IsBlocked(nx, cur.Item2) || pf.IsBlocked(cur.Item1, ny)) continue; cost = 14; }
                    int nd = dcur + cost;
                    if (!dist.TryGetValue((nx, ny), out int old) || nd < old) { dist[(nx, ny)] = nd; pq.Enqueue((nx, ny), nd); }
                }
            }
            return -1;
        }
    }

    // ------------------------------------------------------------ WriteBehindQueue
    private static void WriteBehindTests(Action<string, bool> check)
    {
        var q = new WriteBehindQueue<int, int>();
        q.Enqueue(1, 10, 0); q.Enqueue(2, 20, 100); q.Enqueue(1, 11, 150);
        var outp = new List<KeyValuePair<int, int>>();
        check("WriteBehind 합치기: 같은 키는 마지막 값 하나", q.Count == 2);
        check("WriteBehind 나이가 안 되면 안 나옴", q.Drain(outp, 150, 200) == 0);
        check("WriteBehind 처음 들어온 시각 기준(재갱신해도 늦춰지지 않음)", q.Drain(outp, 250, 200) == 1 && outp[0].Key == 1 && outp[0].Value == 11);
        check("WriteBehind 남은 것은 아직", q.Drain(outp, 250, 200) == 0 && q.Count == 1);
        q.Discard(2);
        check("WriteBehind Discard", q.Count == 0 && q.DrainAll(outp) == 0);

        var m = new WriteBehindQueue<string, int>(merge: (a, b) => a + b);
        m.Enqueue("exp", 5, 0); m.Enqueue("exp", 7, 1);
        outp.Clear();
        var o2 = new List<KeyValuePair<string, int>>();
        m.DrainAll(o2);
        check("WriteBehind 합성 함수", o2.Count == 1 && o2[0].Value == 12);

        var cap = new WriteBehindQueue<int, int>(maxPending: 2);
        check("WriteBehind 상한: 새 키 거절, 기존 키 갱신은 허용", cap.Enqueue(1, 1, 0) && cap.Enqueue(2, 2, 0) && !cap.Enqueue(3, 3, 0) && cap.Enqueue(1, 9, 0));

        var rq = new WriteBehindQueue<int, int>();
        rq.Enqueue(1, 5, 0);
        outp.Clear(); rq.DrainAll(outp);
        rq.Enqueue(1, 6, 10);
        check("WriteBehind Requeue: 더 새 값이 있으면 되돌린 값을 버림", !rq.Requeue(1, 5, 20) && rq.Requeue(2, 8, 20) && rq.Count == 2);

        // 폐기/재입력이 반복돼도 내부 순서 큐가 무한히 커지지 않는다
        var churn = new WriteBehindQueue<int, int>();
        churn.Enqueue(0, 0, 0);
        for (int i = 0; i < 100000; ++i) { churn.Enqueue(1, i, 1); churn.Discard(1); }
        check("WriteBehind 폐기 반복 후에도 정상", churn.Count == 1);

        // 동시성: 생산자 4개가 키별로 증가하는 값을 넣고, 소비자가 계속 비운다. 끝나면 키별 마지막 값이 나왔는지(손실 없음) 확인.
        var cq = new WriteBehindQueue<int, long>();
        const int keys = 50, perProducer = 40000;
        var last = new long[keys];
        var seenLast = new long[keys];
        Array.Fill(seenLast, -1);
        bool stop = false;
        var consumer = Task.Run(() =>
        {
            var batch = new List<KeyValuePair<int, long>>();
            while (true)
            {
                bool done = Volatile.Read(ref stop);
                batch.Clear();
                cq.Drain(batch, long.MaxValue / 2, 0, 64);
                foreach (var kv in batch)
                {
                    if (kv.Value < seenLast[kv.Key]) { Interlocked.Exchange(ref seenLast[kv.Key], long.MinValue); }       // 키별 값이 거꾸로 나오면 표시
                    else seenLast[kv.Key] = kv.Value;
                }
                if (done && batch.Count == 0) break;
            }
        });
        var producers = new Task[4];
        for (int p = 0; p < producers.Length; ++p)
        {
            int pid = p;
            producers[p] = Task.Run(() =>
            {
                for (int i = 1; i <= perProducer; ++i)
                {
                    int key = pid * 10 + (i % 10);          // 생산자마다 자기 키 10개만 쓴다(키별 단일 생산자 → 값이 증가)
                    long v = i;
                    cq.Enqueue(key, v, 0);
                    Interlocked.Exchange(ref last[key], v);
                }
            });
        }
        Task.WaitAll(producers);
        Volatile.Write(ref stop, true);
        consumer.Wait();
        bool conc = cq.Count == 0;
        for (int p = 0; p < 4; ++p)
            for (int k = 0; k < 10; ++k)
            {
                int key = p * 10 + k;
                conc &= seenLast[key] == last[key];
            }
        check("WriteBehind 동시성: 생산자 4 + 소비자 1, 키별 마지막 값 손실 없음·역순 없음", conc);
    }

    // ------------------------------------------------------------ ShutdownCoordinator
    private static void ShutdownTests(Action<string, bool> check)
    {
        var order = new List<string>();
        var sc = new ShutdownCoordinator(5000);
        sc.Register("late", () => order.Add("late"), order: 20);
        sc.Register("early", () => order.Add("early"), order: 0);
        sc.Register("early2", () => order.Add("early2"), order: 0);
        sc.Register("async", async ct => { await Task.Delay(30, CancellationToken.None); order.Add("async"); }, order: 10);
        check("Shutdown 요청 전엔 토큰 꺼짐", !sc.IsRequested && !sc.Token.IsCancellationRequested);
        var results = sc.Run();
        check("Shutdown 실행 순서(order, 등록 순)", string.Join(",", order) == "early,early2,async,late" && results.All(r => r.Outcome == HookOutcome.Completed));
        check("Shutdown 토큰/요청 상태", sc.IsRequested && sc.WhenRequested.IsCompleted);
        check("Shutdown 두 번째 호출은 같은 결과(재실행 없음)", ReferenceEquals(sc.RunAsync(), sc.RunAsync()) && order.Count == 4);
        bool threw = false;
        try { sc.Register("x", () => { }); } catch (InvalidOperationException) { threw = true; }
        check("Shutdown 시작 후 등록 거절", threw);

        var sc2 = new ShutdownCoordinator(10_000);
        bool afterRan = false, cancelSeen = false;
        sc2.Register("boom", () => throw new InvalidOperationException("x"), 0);
        sc2.Register("hang", async ct => { try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { cancelSeen = true; } }, 1, timeoutMs: 100);
        sc2.Register("after", () => afterRan = true, 2);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r2 = sc2.Run();
        sw.Stop();
        check("Shutdown 예외는 기록하고 계속", r2[0].Outcome == HookOutcome.Failed && r2[0].Error is InvalidOperationException);
        check("Shutdown 훅 시간 초과 시 취소 신호 후 계속", r2[1].Outcome == HookOutcome.TimedOut && afterRan && r2[2].Outcome == HookOutcome.Completed && sw.ElapsedMilliseconds < 3000);
        Thread.Sleep(50);
        check("Shutdown 시간 초과 훅은 취소 토큰을 받음", cancelSeen);

        var sc3 = new ShutdownCoordinator(150);
        sc3.Register("slow", () => Thread.Sleep(400), 0, timeoutMs: 10_000);
        sc3.Register("skipped", () => { }, 1);
        var r3 = sc3.Run();
        check("Shutdown 전체 제한: 남은 훅 건너뜀", r3[0].Outcome == HookOutcome.TimedOut && r3[1].Outcome == HookOutcome.Skipped);

        var sc4 = new ShutdownCoordinator();
        var waiter = Task.Run(async () => { await sc4.WhenRequested; return true; });
        sc4.RequestShutdown();
        check("Shutdown WhenRequested", waiter.Wait(2000) && sc4.Token.IsCancellationRequested);
        sc4.Dispose();
    }

    // ------------------------------------------------------------ ITimeProvider
    private static void TimeProviderTests(Action<string, bool> check)
    {
        var t = new ManualTimeProvider(1000, 5_000_000);
        var clock = t.AsMonotonicClock();
        var breaker = new CircuitBreaker(2, 1000, clock);
        breaker.OnFailure(); breaker.OnFailure();
        check("ManualTimeProvider: 서킷 브레이커 시간 주입", breaker.State == CircuitState.Open);
        t.Advance(1000);
        check("ManualTimeProvider Advance", breaker.State == CircuitState.HalfOpen && t.WallMs == 5_001_000);
        t.SetWall(1);
        check("벽시계만 되돌릴 수 있고 단조 시계는 유지", t.WallMs == 1 && t.MonotonicMs == 2000);
        bool threw = false;
        try { t.Advance(-1); } catch (ArgumentOutOfRangeException) { threw = true; }
        check("단조 시계는 뒤로 못 감", threw);
        check("TimeUtil.AddSaturated: 넘치면 상한에서 멈춤", TimeUtil.AddSaturated(long.MaxValue - 1, 10) == long.MaxValue && TimeUtil.AddSaturated(5, 7) == 12);
        var ob = new BuffTimerSet<int>();
        ob.Apply(1, long.MaxValue - 5, long.MaxValue / 2);
        check("Buff 만료 시각 오버플로 방지", ob.TryGet(1, out var obs) && obs.ExpiresAtMs == long.MaxValue);
        long m = SystemTimeProvider.Instance.MonotonicMs;
        check("SystemTimeProvider", SystemTimeProvider.Instance.MonotonicMs >= m && Math.Abs(SystemTimeProvider.Instance.WallMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < 2000);
    }

    // ------------------------------------------------------------ BackoffState
    private static void BackoffTests(Action<string, bool> check)
    {
        var b = new BackoffState(500, 30000, 6);
        check("Backoff 처음엔 건너뛰지 않음", !b.ShouldSkip(0) && b.FailCount == 0);
        long d1 = b.OnFail(1000), d2 = b.OnFail(1000), d3 = b.OnFail(1000);
        check("Backoff 두 배씩 증가", d1 == 500 && d2 == 1000 && d3 == 2000 && b.NextRetryMs == 3000);
        check("Backoff 시각 전까지 건너뜀", b.ShouldSkip(2999) && !b.ShouldSkip(3000));
        for (int i = 0; i < 20; ++i) b.OnFail(0);
        check("Backoff maxShift/상한에서 멈춤", b.OnFail(0) == 30000 && BackoffState.ComputeDelayMs(7, 500, 30000, 6) == 30000 && BackoffState.ComputeDelayMs(7, 500, 1_000_000, 6) == 32000);
        b.Reset();
        check("Backoff Reset", b.FailCount == 0 && b.NextRetryMs == 0 && !b.ShouldSkip(0));
        check("Backoff 오버플로 방지", BackoffState.ComputeDelayMs(100, long.MaxValue / 3, long.MaxValue / 2, 62) == long.MaxValue / 2);
        var j = new BackoffState(100, 400, 10, jitterMs: 50);
        bool ok = true;
        for (int i = 0; i < 2000; ++i) { j.Reset(); long d = j.OnFail(0); ok &= d >= 100 && d <= 150; }
        for (int i = 0; i < 40; ++i) { long d = j.OnFail(0); ok &= d <= 400; }
        check("Backoff 지터는 0~jitter, 지터를 더해도 상한 이내", ok);
        bool threw = false;
        try { new BackoffState(100, 50); } catch (ArgumentOutOfRangeException) { threw = true; }
        check("Backoff 잘못된 인자 거절", threw);
    }
}
