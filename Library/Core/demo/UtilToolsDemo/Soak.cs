// Soak.cs : 장시간 혼합 부하에서 메모리/항목 수가 늘어나지 않는지 확인한다. 사용: dotnet UtilToolsDemo.dll soak [초]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Core.Util;

namespace Core.Demo;

internal static class Soak
{
    private readonly record struct Ev(int V);

    public static int Run(int seconds)
    {
        var rng = new FastRandom(1234);
        long start = TimeUtil.MonotonicNowMillis();
        long simNow = 0;                                    // 가상 시각(ms): 실제보다 빠르게 흘러 만료 경로를 많이 탄다

        var wheel = new TimerWheel(0, 10);
        var handles = new List<TimerHandle>();
        long timerFires = 0;
        var lru = new LruCache<int, byte[]>(5000);
        var ttl = new TtlCache<int, int>(2000);
        var sp = new SpatialHash<int>(25);
        int nextEnt = 0;
        var ents = new List<int>();
        var krl = new KeyedRateLimiter<int>(10, 5);
        var nonces = new NonceCache(200_000);
        var signer = new AuthTokenSigner("soak-secret-0123456789");
        var reg = new MetricRegistry();
        var hist = reg.Histogram("soak_ms");
        var ctr = reg.Counter("soak_total");
        var pool = new ObjectPool<List<int>>(() => new List<int>(), 32, l => l.Clear());
        var ring = new HashRing<int>(64);
        for (int i = 0; i < 8; ++i) ring.AddNode(i);
        var bus = new EventBus();
        var subs = new List<IDisposable>();
        long busCalls = 0;
        var gen = new SnowflakeIdGenerator(3, null, () => 1_800_000_000_000 + simNow);
        int ringNext = 100;
        var heap2 = new MinHeap<long>();
        var heapHandles = new List<HeapHandle>();
        var htab = new HandleTable<int>();
        var hids = new List<HandleId>();
        var cds = new CooldownSet<int>();
        var buffs = new BuffTimerSet<int>();
        var wbq = new WriteBehindQueue<int, int>(50_000);
        var wbOut = new List<KeyValuePair<int, int>>();
        var stats = new StatModifierSet(8);
        var statChanged = new List<int>();
        var rb = new RingBuffer<int>(256, RingFullMode.OverwriteOldest);
        var pf = new GridPathfinder(96, 96);
        for (int i = 0; i < 96 * 96; ++i) pf.SetBlocked(i % 96, i / 96, rng.Chance(0.2));
        var pfPath = new List<GridPoint>();

        var samples = new List<(long Sec, long HeapMb, int Timers, int LruN, int TtlN, int Ents, int KeyN, int NonceN, int LogKeys)>();
        long nextReport = 10_000;
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"soak {seconds}s 시작");

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            for (int batch = 0; batch < 2000; ++batch)
            {
                simNow += rng.Next(0, 4);       // 평균 1.5ms씩 가상 시각 진행

                switch (rng.Next(26))
                {
                    case 0:
                        handles.Add(wheel.Schedule(rng.Next(0, 5000), () => ++timerFires));
                        if (handles.Count > 20000) handles.RemoveRange(0, 10000);
                        break;
                    case 1:
                        if (handles.Count > 0) wheel.Cancel(handles[rng.Next(handles.Count)]);
                        break;
                    case 2:
                        if (wheel.Count < 5000) handles.Add(wheel.ScheduleRepeating(rng.Next(20, 2000), () => ++timerFires));
                        break;
                    case 3:
                        wheel.Advance(simNow);
                        break;
                    case 4:
                        lru.Set(rng.Next(100000), new byte[64]);
                        lru.TryGet(rng.Next(100000), out _);
                        break;
                    case 5:
                        ttl.Set(rng.Next(1_000_000), 1, simNow);
                        if (rng.Next(500) == 0) ttl.Sweep(simNow);
                        break;
                    case 6:
                        if (ents.Count < 5000 || rng.Chance(0.3))
                        {
                            int id = nextEnt++;
                            sp.Add(id, rng.NextFloat() * 2000, rng.NextFloat() * 2000);
                            ents.Add(id);
                        }
                        if (ents.Count > 5000)
                        {
                            int k = rng.Next(ents.Count);
                            sp.Remove(ents[k]);
                            ents[k] = ents[ents.Count - 1];
                            ents.RemoveAt(ents.Count - 1);
                        }
                        break;
                    case 7:
                        if (ents.Count > 0) sp.Move(ents[rng.Next(ents.Count)], rng.NextFloat() * 2000, rng.NextFloat() * 2000);
                        break;
                    case 8:
                        {
                            var l = pool.Rent();
                            sp.QueryCircle(rng.NextFloat() * 2000, rng.NextFloat() * 2000, 40, l);
                            pool.Return(l);
                            break;
                        }
                    case 9:
                        krl.TryTake(rng.Next(200000), simNow);
                        if (rng.Next(2000) == 0) krl.Sweep(simNow, 5000);
                        break;
                    case 10:
                        {
                            string t = signer.Issue("a" + rng.Next(1000), simNow + 3000, out _);
                            signer.Verify(t, simNow, out _, nonces);
                            break;
                        }
                    case 11:
                        hist.Observe(rng.NextDouble() * 100);
                        ctr.Inc();
                        reg.Counter("dyn_" + rng.Next(50)).Inc();
                        break;
                    case 12:
                        if (rng.Chance(0.5)) { ring.AddNode(ringNext++); }
                        else if (ringNext > 108) { ring.RemoveNode(--ringNext); }
                        ring.TryGetNode((long)rng.NextUInt64(), out _);
                        break;
                    case 13:
                        if (subs.Count < 200 && rng.Chance(0.5)) subs.Add(bus.Subscribe<Ev>(_ => ++busCalls));
                        else if (subs.Count > 0) { int k = rng.Next(subs.Count); subs[k].Dispose(); subs.RemoveAt(k); }
                        bus.Publish(new Ev(1));
                        break;
                    case 14:
                        LogThrottle.TryAcquire("soak-" + rng.Next(5000), 1000, simNow, out _);
                        break;
                    case 16:
                        heapHandles.Add(heap2.Push(simNow + rng.Next(0, 5000)));
                        if (heapHandles.Count > 5000) { for (int k = 0; k < 2500; ++k) heap2.Remove(heapHandles[k]); heapHandles.RemoveRange(0, 2500); }
                        if (rng.Chance(0.5)) heap2.TryPop(out _);
                        break;
                    case 17:
                        hids.Add(htab.Add(rng.Next(int.MaxValue)));
                        if (hids.Count > 3000) { for (int k = 0; k < 1500; ++k) htab.Remove(hids[k]); hids.RemoveRange(0, 1500); }
                        break;
                    case 18:
                        cds.TryUse(rng.Next(0, 3000), simNow, rng.Next(0, 2000), 50);
                        if (rng.Chance(0.01)) cds.Sweep(simNow);
                        break;
                    case 19:
                        buffs.Apply(rng.Next(0, 4000), simNow, rng.Next(10, 3000), 1, 5, (BuffRefresh)rng.Next(0, 5));
                        buffs.Tick(simNow);
                        break;
                    case 20:
                        wbq.Enqueue(rng.Next(0, 100_000), rng.Next(int.MaxValue), simNow);
                        if (rng.Chance(0.02)) { wbOut.Clear(); wbq.Drain(wbOut, simNow, 500, 500); }
                        break;
                    case 21:
                        stats.Add(rng.Next(0, 8), (ModKind)rng.Next(0, 3), rng.Next(1, 10), rng.Next(0, 50));
                        if (rng.Chance(0.2)) stats.RemoveSource(rng.Next(0, 50));
                        if (rng.Chance(0.05)) { statChanged.Clear(); stats.DrainChanged(statChanged); }
                        break;
                    case 22:
                        rb.Push(rng.Next(int.MaxValue));
                        break;
                    case 23:
                        if (rng.Chance(0.02)) pf.FindPath(rng.Next(0, 96), rng.Next(0, 96), rng.Next(0, 96), rng.Next(0, 96), pfPath, true, 4000);
                        break;
                    default:
                        gen.Next();
                        break;
                }
            }

            long elapsedMs = sw.ElapsedMilliseconds;
            if (elapsedMs >= nextReport)
            {
                nextReport += 10_000;
                long heap = GC.GetTotalMemory(true) / (1024 * 1024);
                var s = (elapsedMs / 1000, heap, wheel.Count, lru.Count, ttl.Count, sp.Count, krl.KeyCount, nonces.Count, LogThrottle.KeyCount);
                samples.Add(s);
                Console.WriteLine($"  t={s.Item1,4}s heap={heap,4}MB timers={wheel.Count,6} lru={lru.Count,5} ttl={ttl.Count,6} spatial={sp.Count,5} keyedRL={krl.KeyCount,6} nonces={nonces.Count,6} logKeys={LogThrottle.KeyCount,4} timerFires={timerFires}");
                Console.WriteLine($"         heap2={heap2.Count} handleTable={htab.Count} cooldown={cds.Count} buffs={buffs.Count} writeBehind={wbq.Count}");
            }
        }

        // 판정: 앞 1/3(워밍업)을 제외하고, 뒤 1/3 평균 힙이 가운데 1/3 평균 힙보다 25%(최소 16MB) 넘게 커지지 않아야 한다.
        if (samples.Count < 6)
        {
            Console.WriteLine("soak 시간이 짧아 판정하지 않음");
            return 0;
        }
        int third = samples.Count / 3;
        double mid = 0, last = 0;
        for (int i = third; i < 2 * third; ++i) mid += samples[i].HeapMb;
        for (int i = samples.Count - third; i < samples.Count; ++i) last += samples[i].HeapMb;
        mid /= third; last /= third;
        bool ok = last <= mid * 1.25 + 16;
        Console.WriteLine($"가운데 1/3 평균 힙 {mid:F1}MB, 마지막 1/3 평균 힙 {last:F1}MB → {(ok ? "안정" : "증가 의심")}");
        Console.WriteLine($"타이머 실행 {timerFires}, 이벤트 호출 {busCalls}");
        return ok ? 0 : 1;
    }
}
