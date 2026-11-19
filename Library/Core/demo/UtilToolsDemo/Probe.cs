using System;
using System.Collections.Generic;
using System.Diagnostics;
using Core.Util;
namespace Core.Demo;
internal static class Probe
{
    public static void Run()
    {
        // 상시 2000개 유지, 5M회 push+pop(타이머/AI 큐 전형)
        for (int rep = 0; rep < 2; ++rep)
        {
            var r0 = new FastRandom(1);
            var h = new MinHeap<int>(); for (int i = 0; i < 2000; ++i) h.Push(r0.Next(0, 1 << 30));
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5_000_000; ++i) { h.Push(r0.Next(0, 1 << 30)); h.TryPop(out _); }
            long t1 = sw.ElapsedMilliseconds;
            var r1 = new FastRandom(1);
            var pq = new PriorityQueue<int, int>(); for (int i = 0; i < 2000; ++i) { int v = r1.Next(0, 1 << 30); pq.Enqueue(v, v); }
            sw.Restart();
            for (int i = 0; i < 5_000_000; ++i) { int v = r1.Next(0, 1 << 30); pq.Enqueue(v, v); pq.TryDequeue(out _, out _); }
            Console.WriteLine($"2천 개 유지 5M push+pop: MinHeap {t1}ms, PriorityQueue {sw.ElapsedMilliseconds}ms");
        }
        // A* 처리량
        foreach (int size in new[] { 128, 512 })
        {
            var pf = new GridPathfinder(size, size);
            var r = new FastRandom(2);
            for (int y = 0; y < size; ++y) for (int x = 0; x < size; ++x) pf.SetBlocked(x, y, r.NextDouble() < 0.25);
            pf.SetBlocked(0, 0, false); pf.SetBlocked(size - 1, size - 1, false);
            var path = new List<GridPoint>();
            int ok = 0, calls = 0; long exp = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; ++i)
            {
                int sx = r.Next(0, size), sy = r.Next(0, size), gx = r.Next(0, size), gy = r.Next(0, size);
                pf.SetBlocked(sx, sy, false); pf.SetBlocked(gx, gy, false);
                ++calls; if (pf.FindPath(sx, sy, gx, gy, path)) ++ok; exp += pf.LastExpanded;
            }
            Console.WriteLine($"A* {size}x{size} 25% 장애물: {calls}회 {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalMilliseconds * 1000 / calls:F0}us/회), 성공 {ok}, 평균 확장 {exp / calls}");
        }
        // 버프/쿨다운/WriteBehind 처리량
        var bt = new BuffTimerSet<int>(); var rr = new FastRandom(3); long now = 0;
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < 2_000_000; ++i) { now += 3; bt.Apply(rr.Next(0, 5000), now, rr.Next(10, 5000), 1, 5); if ((i & 15) == 0) bt.Tick(now); }
        Console.WriteLine($"Buff 200만 Apply(+Tick): {sw2.ElapsedMilliseconds}ms, 남은 {bt.Count}");
        var wb = new WriteBehindQueue<int, int>(); var outp = new List<KeyValuePair<int, int>>();
        sw2.Restart();
        for (int i = 0; i < 2_000_000; ++i) { wb.Enqueue(rr.Next(0, 10000), i, i); if ((i & 1023) == 0) { outp.Clear(); wb.Drain(outp, i, 500); } }
        Console.WriteLine($"WriteBehind 200만 Enqueue: {sw2.ElapsedMilliseconds}ms");
    }
}
