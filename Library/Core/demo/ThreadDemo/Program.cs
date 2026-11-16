// ThreadDemo : Core.Util 스레드 유틸(락, 큐, ClusterSpinMap, WorkerPool) 정확성/스트레스 검사.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;

internal static class Program
{
    static int s_pass, s_fail;

    static void Check(bool ok, string name)
    {
        if (ok) { s_pass++; Console.WriteLine("  ok   " + name); }
        else { s_fail++; Console.WriteLine("  FAIL " + name); }
    }

    static void RunThreads(int n, Action<int> body)
    {
        var ts = new Thread[n];
        using var start = new ManualResetEventSlim();
        for (int i = 0; i < n; ++i)
        {
            int id = i;
            ts[i] = new Thread(() => { start.Wait(); body(id); });
            ts[i].Start();
        }
        start.Set();
        foreach (var t in ts) t.Join();
    }

    static bool Throws<T>(Action a) where T : Exception
    {
        try { a(); } catch (T) { return true; } catch { return false; }
        return false;
    }

    static void Locks()
    {
        Console.WriteLine("[locks]");

        var m = new SpinMutex();
        long counter = 0;
        RunThreads(8, _ => { for (int i = 0; i < 200_000; ++i) { using (m.Lock()) counter++; } });
        Check(counter == 1_600_000, "SpinMutex mutual exclusion (8x200k)");
        Check(m.TryEnter() && !m.TryEnter() && m.IsHeld, "SpinMutex TryEnter");
        m.Exit();
        Check(Throws<SynchronizationLockException>(() => m.Exit()), "SpinMutex unmatched Exit throws");

        var rw = new RWSpinLock();
        long a = 0, b = 0; int bad = 0;
        var stop = new ManualResetEventSlim();
        var readers = Task.Run(() => RunThreads(4, _ =>
        {
            while (!stop.IsSet)
                using (rw.Read()) { if (Volatile.Read(ref a) != Volatile.Read(ref b)) Interlocked.Increment(ref bad); }
        }));
        RunThreads(2, _ => { for (int i = 0; i < 50_000; ++i) using (rw.Write()) { a++; b++; } });
        stop.Set(); readers.Wait();
        Check(a == 100_000 && b == 100_000 && bad == 0, "RWSpinLock writers exclusive, readers see consistent state");

        rw.EnterRead(); rw.EnterRead();
        bool anyWrite = false;
        for (int i = 0; i < 1000; ++i) anyWrite |= rw.TryEnterWrite();
        rw.ExitRead(); rw.ExitRead();
        Check(!anyWrite && rw.TryEnterWrite(), "RWSpinLock failed TryEnterWrite leaves state intact");
        Check(!rw.TryEnterRead(), "RWSpinLock no readers while writer held");
        rw.ExitWrite();
        Check(rw.TryEnterRead(), "RWSpinLock readers allowed after failed TryEnterWrite storm (no counter underflow)");
        rw.ExitRead();
        Check(Throws<SynchronizationLockException>(() => rw.ExitRead()) && Throws<SynchronizationLockException>(() => rw.ExitWrite()), "RWSpinLock unmatched exits throw");

        // 쓰기 우선: 읽기가 계속 이어져도 쓰기가 끝난다
        var rw2 = new RWSpinLock();
        var flood = new ManualResetEventSlim();
        var floodTask = Task.Run(() => RunThreads(4, _ => { while (!flood.IsSet) using (rw2.Read()) Thread.SpinWait(50); }));
        Thread.Sleep(50);
        var sw = Stopwatch.StartNew();
        using (rw2.Write()) { }
        long writeMs = sw.ElapsedMilliseconds;
        flood.Set(); floodTask.Wait();
        Check(writeMs < 1000, $"RWSpinLock writer not starved by reader flood ({writeMs}ms)");

        var pl = new PLock();
        long pc = 0;
        RunThreads(4, _ => { for (int i = 0; i < 100_000; ++i) using (pl.Lock()) pc++; });
        Check(pc == 400_000, "PLock");
        using var prw = new PRWLock();
        long pw = 0;
        RunThreads(4, _ => { for (int i = 0; i < 50_000; ++i) { using (prw.Write()) pw++; using (prw.Read()) { long tmp = pw; if (tmp < 0) throw new Exception(); } } });
        Check(pw == 200_000, "PRWLock");
    }

    static void LockFree()
    {
        Console.WriteLine("[LockFreeQueue]");
        var q0 = new LockFreeQueue<int>(5);
        Check(q0.Capacity == 8, "capacity rounds up to pow2");
        var q = new LockFreeQueue<int>(4);
        Check(!q.TryDequeue(out _), "empty -> TryDequeue false");
        for (int i = 0; i < 4; ++i) q.TryEnqueue(i);
        Check(!q.TryEnqueue(99) && q.SizeApprox == 4, "full -> TryEnqueue false");
        bool fifo = true;
        for (int i = 0; i < 4; ++i) fifo &= q.TryDequeue(out int v) && v == i;
        Check(fifo, "FIFO order");
        // 여러 바퀴 돌려도 유지
        bool wrap = true;
        for (int i = 0; i < 1000; ++i) { q.TryEnqueue(i); wrap &= q.TryDequeue(out int v) && v == i; }
        Check(wrap, "wraparound");

        const int P = 4, C = 4, PER = 250_000;
        var big = new LockFreeQueue<int>(1024);
        long sumIn = 0, sumOut = 0, cntOut = 0;
        var seen = new int[P * PER];
        int dups = 0;
        var consumers = Task.Run(() => RunThreads(C, _ =>
        {
            while (true)
            {
                if (!big.Dequeue(out int v, 2000)) break;
                if (v < 0) break;
                if (Interlocked.Exchange(ref seen[v], 1) != 0) Interlocked.Increment(ref dups);
                Interlocked.Add(ref sumOut, v);
                Interlocked.Increment(ref cntOut);
            }
        }));
        RunThreads(P, p =>
        {
            for (int i = 0; i < PER; ++i)
            {
                int v = p * PER + i;
                big.Enqueue(v);
                Interlocked.Add(ref sumIn, v);
            }
        });
        for (int i = 0; i < C; ++i) big.Enqueue(-1);
        consumers.Wait();
        Check(cntOut == P * PER && sumIn == sumOut && dups == 0, $"4P/4C {P * PER} items: no loss, no duplicates");

        var sw = Stopwatch.StartNew();
        var qe = new LockFreeQueue<int>(2);
        Check(!qe.Dequeue(out _, 100) && sw.ElapsedMilliseconds >= 80, "blocking Dequeue times out");
        qe.TryEnqueue(1); qe.TryEnqueue(2);
        sw.Restart();
        Check(!qe.Enqueue(3, 100) && sw.ElapsedMilliseconds >= 80, "blocking Enqueue times out when full");

        // 용량 2에서 핑퐁
        var pp = new LockFreeQueue<int>(2);
        long ppSum = 0;
        var cons = Task.Run(() => { for (int i = 0; i < 100_000; ++i) { pp.Dequeue(out int v); ppSum += v; } });
        for (int i = 1; i <= 100_000; ++i) pp.Enqueue(i);
        cons.Wait();
        Check(ppSum == 5_000_050_000L, "blocking ping-pong on capacity 2");
    }

    static void Blocking()
    {
        Console.WriteLine("[BlockingTaskQueue]");
        var q = new BlockingTaskQueue<int>();
        q.Push(1); q.PushBatch(new[] { 2, 3 });
        q.SetProducerDone();
        bool ok = q.Pop(out int a) && q.Pop(out int b) && q.Pop(out int c) && a + b + c == 6 && !q.Pop(out _);
        Check(ok, "drain after SetProducerDone then Pop false");
        Check(!q.Push(9), "Push after producer done rejected");

        var q2 = new BlockingTaskQueue<int>();
        var blocked = Task.Run(() => q2.Pop(out _));
        Thread.Sleep(100);
        q2.Stop();
        Check(blocked.Wait(2000) && !blocked.Result, "Stop wakes blocked Pop");

        var q3 = new BlockingTaskQueue<int>();
        var sw = Stopwatch.StartNew();
        Check(!q3.Pop(out _, 100) && sw.ElapsedMilliseconds >= 80, "Pop timeout");

        var q4 = new BlockingTaskQueue<int>();
        q4.PushBatch(new[] { 1, 2, 3, 4, 5 });
        var dest = new List<int>();
        Check(q4.PopChunk(dest, 3) == 3 && dest.Count == 3 && q4.Count == 2, "PopChunk honors max");

        // 백프레셔
        var q5 = new BlockingTaskQueue<int>(4);
        for (int i = 0; i < 4; ++i) q5.Push(i);
        int pushed = 0;
        var prod = Task.Run(() => { q5.Push(100); Volatile.Write(ref pushed, 1); });
        Thread.Sleep(150);
        bool wasBlocked = Volatile.Read(ref pushed) == 0;
        q5.Pop(out _);
        Check(wasBlocked && prod.Wait(2000), "bounded queue blocks producer until space");

        // 깨움 유실 스트레스: 소비자가 대기에 들어가는 순간과 SetProducerDone을 반복해서 겹친다
        int hung = 0;
        for (int iter = 0; iter < 500; ++iter)
        {
            var qq = new BlockingTaskQueue<int>();
            var tasks = new Task[4];
            for (int i = 0; i < tasks.Length; ++i) tasks[i] = Task.Run(() => { while (qq.Pop(out _)) { } });
            if ((iter & 1) == 0) Thread.Yield();
            qq.SetProducerDone();
            if (!Task.WaitAll(tasks, 3000)) hung++;
        }
        Check(hung == 0, "no lost wakeup on SetProducerDone (500 races)");

        // MPMC 정합성
        var q6 = new BlockingTaskQueue<int>(64);
        long sIn = 0, sOut = 0;
        var workers = Task.Run(() => RunThreads(4, _ =>
        {
            var chunk = new List<int>();
            while (true)
            {
                chunk.Clear();
                if (q6.PopChunk(chunk, 16) == 0) break;
                foreach (int v in chunk) Interlocked.Add(ref sOut, v);
            }
        }));
        RunThreads(2, p => { for (int i = 1; i <= 100_000; ++i) { q6.Push(i); Interlocked.Add(ref sIn, i); } });
        q6.SetProducerDone();
        workers.Wait();
        Check(sIn == sOut && sIn > 0, "MPMC with PopChunk and backpressure: sums match");
    }

    static void Swaps()
    {
        Console.WriteLine("[DoubleBuffer / Swap queues]");
        var dq = new DoubleBufferQueue<int>();
        long sIn = 0, sOut = 0; int total = 0;
        var done = new ManualResetEventSlim();
        var cons = Task.Run(() =>
        {
            var buf = new List<int>();
            while (!done.IsSet || dq.Count > 0)
            {
                int n = dq.SwapInto(ref buf);
                foreach (int v in buf) sOut += v;
                total += n;
                if (n == 0) Thread.Yield();
            }
        });
        RunThreads(4, _ => { for (int i = 1; i <= 100_000; ++i) { dq.Push(i); Interlocked.Add(ref sIn, i); } });
        done.Set(); cons.Wait();
        Check(total == 400_000 && sIn == sOut, "DoubleBufferQueue 4P/1C no loss");

        var cq = new ChunkedSwapQueue<int>();
        cq.PushBatch(new[] { 1, 2, 3, 4, 5 });
        var d = new List<int>();
        Check(cq.SwapChunk(d, 2) == 2 && cq.Count == 3 && d[0] == 1 && d[1] == 2, "ChunkedSwapQueue chunk limit and order");

        var sq = new SpinLockQueue<int>();
        sq.Push(1); sq.Push(2); sq.Push(3);
        var all = new List<int>();
        Check(sq.TryPop(out int one) && one == 1 && sq.PopAll(all) == 2 && all.Count == 2 && sq.Count == 0, "SpinLockQueue TryPop/PopAll");
    }

    static void Map()
    {
        Console.WriteLine("[ClusterSpinMap]");
        var m = new ClusterSpinMap<int, string>(8);
        Check(m.Insert(1, "a") && !m.Insert(1, "b") && m.TryGet(1, out var s) && s == "a", "Insert does not overwrite");
        m.Set(1, "c");
        Check(m.TryGet(1, out s) && s == "c", "Set overwrites");
        Check(m.Remove(1, out s) && s == "c" && !m.Remove(1) && m.Count == 0, "Remove");

        int factoryCalls = 0;
        RunThreads(8, _ => { for (int k = 0; k < 1000; ++k) m.GetOrAdd(k, key => { Interlocked.Increment(ref factoryCalls); return "v" + key; }); });
        Check(factoryCalls == 1000 && m.Count == 1000, "GetOrAdd creates each key exactly once under contention");

        RunThreads(8, t =>
        {
            for (int k = 0; k < 20_000; ++k)
            {
                int key = 10_000 + t * 20_000 + k;
                m.Insert(key, "x");
                if ((k & 1) == 0) m.Remove(key);
            }
        });
        Check(m.Count == 1000 + 8 * 10_000, "concurrent insert/remove count");

        int visited = 0;
        m.ForEach((k, v) => visited++);
        var snap = m.Snapshot();
        Check(visited == m.Count && snap.Count == m.Count, "ForEach and Snapshot cover all entries");
        m.Clear();
        Check(m.Count == 0, "Clear");

        // 서로 다른 키의 동시 읽기/쓰기 중 예외가 없고 값이 일관됨
        var m2 = new ClusterSpinMap<long, long>(16);
        for (long k = 0; k < 1000; ++k) m2.Set(k, k * 2);
        int wrong = 0;
        var stop = new ManualResetEventSlim();
        var rd = Task.Run(() => RunThreads(4, _ =>
        {
            var rnd = new Random();
            while (!stop.IsSet) { long k = rnd.Next(1000); if (m2.TryGet(k, out long v) && v != k * 2) Interlocked.Increment(ref wrong); }
        }));
        RunThreads(2, _ => { for (int i = 0; i < 200_000; ++i) { long k = i % 1000; m2.Set(k, k * 2); } });
        stop.Set(); rd.Wait();
        Check(wrong == 0, "readers never see torn values during writes");
    }

    static void Review()
    {
        Console.WriteLine("[review fixes]");

        // 부팅 후 200일, 주파수 10^9(Linux)에서도 넘치지 않는다. 기존 식은 ticks*1000에서 long을 넘친다.
        long ticks = 200L * 24 * 3600 * 1_000_000_000L;
        Check(TimeUtil.TicksToMillis(ticks, 1_000_000_000L) == 200L * 24 * 3600 * 1000, "TicksToMillis no overflow at 200 days uptime (ns ticks)");
        Check(TimeUtil.TicksToMillis(12_345_678, 10_000_000) == 1234, "TicksToMillis 10MHz");
        long m1 = TimeUtil.MonotonicNowMillis(); Thread.Sleep(30); long m2 = TimeUtil.MonotonicNowMillis();
        Check(m2 - m1 >= 20 && m2 - m1 < 500, "MonotonicNowMillis advances");

        // 무제한 큐에서 소비자 여러 명이 대기하는 동안 하나씩 넣어도 전부 소비된다 (Pulse 한 번이 한 소비자를 깨움)
        var q = new BlockingTaskQueue<int>();
        int consumed = 0;
        var cs = Task.Run(() => RunThreads(8, _ => { while (q.Pop(out _)) Interlocked.Increment(ref consumed); }));
        Thread.Sleep(100);
        for (int i = 0; i < 20_000; ++i) { q.Push(i); if ((i & 255) == 0) Thread.Sleep(1); }
        q.SetProducerDone();
        Check(cs.Wait(10_000) && consumed == 20_000, "unbounded queue: single Pulse wakes enough consumers (no stall, no loss)");

        // SpinLockQueue.PopAll 동시 호출에서도 유실/중복 없음
        var sq = new SpinLockQueue<int>();
        long sIn = 0, sOut = 0; int doneFlag = 0;
        var poppers = Task.Run(() => RunThreads(3, _ =>
        {
            var l = new List<int>();
            while (Volatile.Read(ref doneFlag) == 0 || sq.Count > 0)
            {
                l.Clear();
                sq.PopAll(l);
                long local = 0; foreach (int v in l) local += v;
                Interlocked.Add(ref sOut, local);
                Thread.Yield();
            }
        }));
        RunThreads(4, _ => { for (int i = 1; i <= 100_000; ++i) { sq.Push(i); Interlocked.Add(ref sIn, i); } });
        Volatile.Write(ref doneFlag, 1);
        poppers.Wait();
        Check(sIn == sOut, "SpinLockQueue concurrent PopAll: no loss, no duplicates");

        // ClusterSpinMap.Count는 락 없이 읽어도 정확하다 (Set 덮어쓰기는 증가시키지 않고, Clear는 일괄 감소)
        var m = new ClusterSpinMap<int, int>(8);
        m.Set(1, 1); m.Set(1, 2); m.Insert(2, 2); m.GetOrAdd(3, k => k); m.GetOrAdd(3, k => k);
        Check(m.Count == 3, "ClusterSpinMap Count exact with overwrite and GetOrAdd");
        m.Remove(2); m.Remove(2);
        Check(m.Count == 2, "ClusterSpinMap Count after Remove");
        m.Clear();
        Check(m.Count == 0, "ClusterSpinMap Count after Clear");
        var ci = new ClusterSpinMap<string, int>(4, StringComparer.OrdinalIgnoreCase);
        ci.Insert("Abc", 1);
        Check(!ci.Insert("aBC", 2) && ci.TryGet("ABC", out int cv) && cv == 1 && ci.Count == 1, "ClusterSpinMap custom comparer");
        int cnt = 0;
        var mc = new ClusterSpinMap<long, long>(16);
        RunThreads(8, t => { for (int i = 0; i < 50_000; ++i) { long k = (i * 7L + t) % 5000; if ((i & 1) == 0) mc.Set(k, i); else mc.Remove(k); } });
        mc.ForEach((k, v) => cnt++);
        Check(cnt == mc.Count, "ClusterSpinMap Count matches actual entries after concurrent churn");

        // WorkerPool onError
        Exception? seen = null;
        var wp = new WorkerPool(1, "Err", 0, e => seen = e);
        wp.Post(() => throw new InvalidOperationException("x"));
        wp.Stop(true);
        Check(seen is InvalidOperationException, "WorkerPool onError receives the exception");
        var wp2 = new WorkerPool(1, "Err2", 0, _ => throw new Exception("handler throws"));
        int after = 0;
        wp2.Post(() => throw new Exception("a"));
        wp2.Post(() => after++);
        wp2.Stop(true);
        Check(after == 1, "worker survives a throwing onError");

        // LockFreeQueue: 대기 중인 소비자가 신호로 바로 깨어난다 (10ms 안전망에 의존하지 않음)
        var lq = new LockFreeQueue<int>(8);
        var got = new ManualResetEventSlim();
        var waiter = Task.Run(() => { lq.Dequeue(out _); got.Set(); });
        Thread.Sleep(200);
        var sw = Stopwatch.StartNew();
        lq.TryEnqueue(1);
        bool woke = got.Wait(2000);
        Check(woke && sw.ElapsedMilliseconds < 8, $"LockFreeQueue wakes blocked consumer promptly ({sw.ElapsedMilliseconds}ms)");
    }

    static void Pool()
    {
        Console.WriteLine("[WorkerPool]");
        int ran = 0;
        var wp = new WorkerPool(4, "T");
        for (int i = 0; i < 10_000; ++i) wp.Post(() => Interlocked.Increment(ref ran));
        wp.Stop(true);
        Check(ran == 10_000, "drain Stop runs every posted task");
        Check(!wp.Post(() => { }), "Post after Stop rejected");
        wp.Stop(true);
        Check(true, "Stop idempotent");

        int ran2 = 0;
        var gate = new ManualResetEventSlim();
        var wp2 = new WorkerPool(1, "D");
        wp2.Post(() => gate.Wait());
        for (int i = 0; i < 100; ++i) wp2.Post(() => Interlocked.Increment(ref ran2));
        Thread.Sleep(50);
        var stopper = Task.Run(() => wp2.Stop(false));
        Thread.Sleep(50);
        gate.Set();
        stopper.Wait(3000);
        Check(stopper.IsCompleted && ran2 == 0, "non-drain Stop discards pending tasks");

        int after = 0;
        var wp3 = new WorkerPool(2, "E");
        wp3.Post(() => throw new InvalidOperationException("boom (expected in test)"));
        wp3.Post(() => Interlocked.Increment(ref after));
        wp3.Stop(true);
        Check(after == 1, "exception in a task does not kill the worker");

        var names = new HashSet<string>();
        var wp4 = new WorkerPool(3, "Named");
        for (int i = 0; i < 300; ++i) wp4.Post(() => { lock (names) names.Add(Thread.CurrentThread.Name ?? ""); Thread.Sleep(1); });
        wp4.Stop(true);
        Check(names.Count == 3 && names.Contains("Named-0"), "dedicated named threads");

        int max = 0, cur = 0;
        var wp5 = new WorkerPool(2, "BP", maxQueueSize: 4);
        for (int i = 0; i < 100; ++i)
            wp5.Post(() => { int c = Interlocked.Increment(ref cur); if (c > max) max = c; Thread.Sleep(1); Interlocked.Decrement(ref cur); });
        wp5.Stop(true);
        Check(max <= 2, $"bounded pool never exceeds worker count concurrency (max={max})");
    }

    static void Review2()
    {
        Console.WriteLine("[review 2]");

        // ClusterSpinMap.Count는 같은 키의 삽입/삭제가 겹쳐도 음수가 되지 않는다
        var m = new ClusterSpinMap<int, int>(4);
        int neg = 0;
        var stop = new ManualResetEventSlim();
        var watcher = new Thread(() => { while (!stop.IsSet) if (m.Count < 0) Interlocked.Increment(ref neg); });
        watcher.Start();
        RunThreads(4, t => { for (int i = 0; i < 200_000; ++i) { m.Insert(i & 7, i); m.Remove(i & 7); } });
        stop.Set(); watcher.Join();
        Check(neg == 0 && m.Count == 0, $"ClusterSpinMap Count never negative under insert/remove races (neg={neg})");

        m.Set(1, 10); m.Set(1, 11);
        Check(m.Count == 1 && m.TryGet(1, out int v) && v == 11, "ClusterSpinMap Set single-lookup overwrite keeps count");

        // 경계 timeout
        var q = new LockFreeQueue<int>(2);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool r = !q.Dequeue(out _, 0) && q.Enqueue(1, 0) && q.Enqueue(2, 0) && !q.Enqueue(3, 0);
        Check(r && sw.ElapsedMilliseconds < 50, "LockFreeQueue timeout 0 returns immediately");

        // 참조 슬롯은 꺼낸 뒤 비워져 GC가 객체를 붙들지 않는다
        var qr = new LockFreeQueue<object>(4);
        WeakReference wr = EnqueuedAndDequeued(qr);
        for (int i = 0; i < 3; ++i) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Check(!wr.IsAlive, "LockFreeQueue releases dequeued reference");

        // DoubleBufferQueue를 소비자 둘이 동시에 불러도 원소를 잃거나 중복하지 않는다 (겹친 호출은 예외로 거절되고 큐는 그대로다)
        var db = new DoubleBufferQueue<int>();
        const int DbN = 200_000;
        long dbIn = 0, dbOut = 0; int rejected = 0;
        var gate = new Barrier(3);
        var prod = Task.Run(() => { gate.SignalAndWait(); for (int i = 1; i <= DbN; ++i) { db.Push(i); dbIn += i; } });
        var swappers = new Task[2];
        for (int k = 0; k < 2; ++k)
            swappers[k] = Task.Run(() =>
            {
                var buf = new List<int>();
                gate.SignalAndWait();
                while (!prod.IsCompleted || db.Count > 0)
                {
                    try { db.SwapInto(ref buf); foreach (int x in buf) Interlocked.Add(ref dbOut, x); }
                    catch (InvalidOperationException) { Interlocked.Increment(ref rejected); }
                }
            });
        Task.WaitAll(swappers);
        var tail = new List<int>();
        db.SwapInto(ref tail);
        foreach (int x in tail) dbOut += x;
        Check(dbIn == dbOut && dbIn > 0, $"DoubleBufferQueue overlapping SwapInto: no loss/dup (rejected={rejected})");

        // 용량 제한 큐 + 소비자 여러 명 + 하나씩 Push: 깨움 유실 없음 (Pulse 경로)
        var bq = new BlockingTaskQueue<int>(8);
        long sum = 0; int got = 0;
        var cons = new Thread[6];
        for (int i = 0; i < cons.Length; ++i)
        {
            cons[i] = new Thread(() => { while (bq.Pop(out int x)) { Interlocked.Add(ref sum, x); Interlocked.Increment(ref got); } });
            cons[i].Start();
        }
        for (int i = 1; i <= 100_000; ++i) bq.Push(i);
        bq.SetProducerDone();
        bool joined = true;
        foreach (var t in cons) joined &= t.Join(10_000);
        Check(joined && got == 100_000 && sum == 5_000_050_000L, "bounded queue, 6 consumers, single pushes: no loss, no hang");

        // WorkerPool: 워커 안에서 Stop을 불러도 교착하지 않는다
        var wp = new WorkerPool(2, "SelfStop");
        var done = new ManualResetEventSlim();
        wp.Post(() => { wp.Stop(true); done.Set(); });
        // 워커 안의 Stop은 다른 워커만 합류시키고 돌아온다 (drain이라 이 작업 자신은 끝까지 간다)
        Check(done.Wait(5000), "WorkerPool.Stop from inside a worker returns");
        wp.Stop(true);

        // drain 정지 도중 Stop(false)로 남은 작업을 버린다
        var wp2 = new WorkerPool(1, "Esc");
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        int ran = 0;
        wp2.Post(() => { started.Set(); release.Wait(); });
        for (int i = 0; i < 50; ++i) wp2.Post(() => Interlocked.Increment(ref ran));
        started.Wait();
        var drainer = Task.Run(() => wp2.Stop(true));
        Thread.Sleep(100);
        var forcer = Task.Run(() => wp2.Stop(false));
        Thread.Sleep(100);
        release.Set();
        Check(drainer.Wait(5000) && forcer.Wait(5000) && ran == 0, $"Stop(false) during drain discards pending work (ran={ran})");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference EnqueuedAndDequeued(LockFreeQueue<object> q)
    {
        // out 인자는 주소가 노출된 지역이라 호출한 메서드가 끝날 때까지 객체를 붙든다. 그래서 꺼내는 부분도 이 메서드 안에 가둔다.
        var o = new object();
        q.TryEnqueue(o);
        q.TryDequeue(out _);
        return new WeakReference(o);
    }

    static void Profiler()
    {
        Console.WriteLine("[DeadLockProfiler]");
        var P = typeof(DeadLockProfiler);
        DeadLockProfiler.Enabled = true;
        DeadLockProfiler.Mode = DeadLockMode.Throw;
        DeadLockProfiler.StrictSelfLock = false;
        var msgs = new List<string>();
        DeadLockProfiler.ViolationHandler = m => { lock (msgs) msgs.Add(m); };
        DeadLockProfiler.ResetGraph();

        // 정상 순서는 통과하고 그래프에 남는다
        DeadLockProfiler.PushLock("A"); DeadLockProfiler.PushLock("B");
        DeadLockProfiler.PopLock("B"); DeadLockProfiler.PopLock("A");
        Check(DeadLockProfiler.DumpGraph().Contains("A -> B") && DeadLockProfiler.HeldLocks().Length == 0, "profiler records A->B, stack empty after pops");

        // 반대 순서는 락을 얻기 전에 예외, 스택은 오염되지 않는다
        DeadLockProfiler.PushLock("B");
        bool threw = Throws<LockOrderException>(() => DeadLockProfiler.PushLock("A"));
        Check(threw && DeadLockProfiler.HeldLocks().Length == 1 && msgs.Count == 1 && msgs[0].Contains("A -> B"), "reverse order rejected before acquire, stack intact, message shows cycle");
        Check(Throws<LockOrderException>(() => DeadLockProfiler.PushLock("A")) && msgs.Count == 2, "same violation is reported every time (cycle edge not recorded)");
        DeadLockProfiler.PopLock("B");

        // 3개짜리 사이클
        DeadLockProfiler.ResetGraph();
        DeadLockProfiler.PushLock("X"); DeadLockProfiler.PushLock("Y"); DeadLockProfiler.PopLock("Y"); DeadLockProfiler.PopLock("X");
        DeadLockProfiler.PushLock("Y"); DeadLockProfiler.PushLock("Z"); DeadLockProfiler.PopLock("Z"); DeadLockProfiler.PopLock("Y");
        DeadLockProfiler.PushLock("Z");
        Check(Throws<LockOrderException>(() => DeadLockProfiler.PushLock("X")), "3-lock cycle X->Y->Z->X detected");
        DeadLockProfiler.PopLock("Z");

        // 같은 이름 중첩은 판정하지 않고, Strict면 자기 대기로 본다. 재진입 가능 표시면 Strict에서도 통과
        DeadLockProfiler.PushLock("S"); DeadLockProfiler.PushLock("S");
        DeadLockProfiler.PopLock("S"); DeadLockProfiler.PopLock("S");
        DeadLockProfiler.StrictSelfLock = true;
        DeadLockProfiler.PushLock("S");
        bool selfThrew = Throws<LockOrderException>(() => DeadLockProfiler.PushLock("S"));
        DeadLockProfiler.PushLock("S", allowRecursive: true);
        DeadLockProfiler.PopLock("S"); DeadLockProfiler.PopLock("S");
        DeadLockProfiler.StrictSelfLock = false;
        Check(selfThrew && DeadLockProfiler.HeldLocks().Length == 0, "same-name nesting ignored; StrictSelfLock catches non-reentrant relock");

        // 기본(엄격): 해제 순서가 획득 순서와 다르면 INVALID_UNLOCK, 쥔 락이 없으면 MULTIPLE_UNLOCK. 어긋난 뒤에도 스택은 바로잡힌다
        msgs.Clear();
        DeadLockProfiler.PushLock("P"); DeadLockProfiler.PushLock("Q");
        bool invalid = Throws<LockOrderException>(() => DeadLockProfiler.PopLock("P"));
        string[] afterInvalid = DeadLockProfiler.HeldLocks();
        DeadLockProfiler.PopLock("Q");
        bool multiple = Throws<LockOrderException>(() => DeadLockProfiler.PopLock("Q"));
        Check(invalid && afterInvalid.Length == 1 && afterInvalid[0] == "Q" && multiple && DeadLockProfiler.HeldLocks().Length == 0
              && msgs.Count == 2 && msgs[0].StartsWith("INVALID_UNLOCK") && msgs[1].StartsWith("MULTIPLE_UNLOCK"),
              "strict unlock order: INVALID_UNLOCK / MULTIPLE_UNLOCK reported, stack repaired");

        // 엄격 모드를 끄면 다른 순서의 해제는 허용하고 쥔 락이 없는 해제만 알린다
        DeadLockProfiler.StrictUnlockOrder = false;
        DeadLockProfiler.PushLock("P"); DeadLockProfiler.PushLock("Q");
        DeadLockProfiler.PopLock("P"); DeadLockProfiler.PopLock("Q");
        Check(DeadLockProfiler.HeldLocks().Length == 0 && Throws<LockOrderException>(() => DeadLockProfiler.PopLock("P")), "non-strict: non-LIFO release OK; unheld release still reported");
        DeadLockProfiler.StrictUnlockOrder = true;

        // Log 모드는 던지지 않고 계속
        DeadLockProfiler.Mode = DeadLockMode.Log;
        int before = msgs.Count;
        DeadLockProfiler.PushLock("Q"); DeadLockProfiler.PushLock("P");   // P->Q 는 위에서 기록됨, 이쪽은 Q->P
        DeadLockProfiler.PopLock("P"); DeadLockProfiler.PopLock("Q");
        Check(msgs.Count == before + 1 && DeadLockProfiler.HeldLocks().Length == 0, "Log mode reports but continues without throwing");
        DeadLockProfiler.Mode = DeadLockMode.Throw;

        // 스레드가 달라도 순서 모순을 잡는다: 서로 다른 시점에 실행돼 실제 교착은 없지만 위험한 순서
        DeadLockProfiler.ResetGraph();
        Exception? caught = null;
        var t1 = new Thread(() => { DeadLockProfiler.PushLock("L1"); DeadLockProfiler.PushLock("L2"); DeadLockProfiler.PopLock("L2"); DeadLockProfiler.PopLock("L1"); });
        t1.Start(); t1.Join();
        var t2 = new Thread(() => { try { DeadLockProfiler.PushLock("L2"); DeadLockProfiler.PushLock("L1"); } catch (Exception e) { caught = e; } });
        t2.Start(); t2.Join();
        Check(caught is LockOrderException, "cross-thread inversion detected without an actual deadlock");

        // 일관된 순서 스트레스: 위반 없음. 반대 순서 스트레스: 교착 없이 끝나고 위반이 잡힘
        DeadLockProfiler.ResetGraph();
        int bad = 0;
        string[] names = { "N0", "N1", "N2", "N3" };
        RunThreads(8, t =>
        {
            for (int i = 0; i < 20_000; ++i)
            {
                try
                {
                    int a = i % 3;
                    DeadLockProfiler.PushLock(names[a]); DeadLockProfiler.PushLock(names[a + 1]);
                    DeadLockProfiler.PopLock(names[a + 1]); DeadLockProfiler.PopLock(names[a]);
                }
                catch { Interlocked.Increment(ref bad); }
            }
        });
        Check(bad == 0, $"consistent order under 8 threads: no false positives ({bad})");

        int inversions = 0;
        RunThreads(4, t =>
        {
            for (int i = 0; i < 5_000; ++i)
            {
                try
                {
                    DeadLockProfiler.PushLock(names[3]); DeadLockProfiler.PushLock(names[0]);   // N3->N0: N0->N1->N2->N3 가 있으므로 사이클
                    DeadLockProfiler.PopLock(names[0]); DeadLockProfiler.PopLock(names[3]);
                }
                catch (LockOrderException) { Interlocked.Increment(ref inversions); DeadLockProfiler.PopLock(names[3]); }
            }
        });
        Check(inversions == 4 * 5_000, $"inverted order always caught under concurrency ({inversions})");

        // 락 클래스 연동 (USE_DEADLOCK_PROFILER가 정의된 Debug 빌드에서만 호출이 들어 있다)
        if (!DeadLockProfiler.HooksCompiledIn)
        {
            Console.WriteLine("  skip lock-class integration (hooks not compiled in this build)");
            DeadLockProfiler.ViolationHandler = null;
            return;
        }

        DeadLockProfiler.ResetGraph();
        var ma = new SpinMutex(); var mb = new SpinMutex();
        using (ma.Lock("IA")) using (mb.Lock("IB")) { }
        bool intThrew;
        using (mb.Lock("IB"))
            intThrew = Throws<LockOrderException>(() => ma.Enter("IA"));
        Check(intThrew && !ma.IsHeld && !mb.IsHeld && DeadLockProfiler.HeldLocks().Length == 0, "SpinMutex: inverted Enter throws before acquiring; nothing left held");

        var rw = new RWSpinLock(); var pl = new PLock(); var prw = new PRWLock();
        using (pl.Lock("IP")) using (rw.Write("IR")) using (prw.Read("IW")) { }
        bool all = true;
        using (rw.Write("IR")) all &= Throws<LockOrderException>(() => pl.Enter("IP"));
        using (prw.Read("IW")) all &= Throws<LockOrderException>(() => rw.EnterRead("IR"));
        using (pl.Lock("IP")) using (pl.Lock("IP")) { }     // PLock 재진입은 Strict에서도 정상
        Check(all && !rw.IsWriteHeld && rw.ReaderCount == 0 && DeadLockProfiler.HeldLocks().Length == 0, "RWSpinLock/PLock/PRWLock integration, PLock reentry OK");
        prw.Dispose();

        using (ma.Lock())       // 이름이 없으면 추적하지 않는다
        using (mb.Lock()) { }
        Check(true, "unnamed locks are not tracked");

        DeadLockProfiler.ViolationHandler = null;
        DeadLockProfiler.ResetGraph();
    }

    static void Ported()
    {
        Console.WriteLine("[C++ port: cache line / presets / guards / SrwLock / ThreadManager / SyncValue / CriticalSection]");

        // CacheAlignment
        Check(System.Runtime.CompilerServices.Unsafe.SizeOf<PaddedInt64>() == 128 && System.Runtime.CompilerServices.Unsafe.SizeOf<PaddedInt32>() == 128, "PaddedInt32/64 occupy two cache lines");
        var pad = new PaddedInt64[8];
        RunThreads(8, t => { for (int i = 0; i < 100_000; ++i) { pad[t].Increment(); pad[0].Add(0); } });
        long padSum = 0; foreach (var p in pad) padSum += p.Load();
        Check(padSum == 800_000 && pad[3].CompareExchange(100_000, 7) == 100_000 && pad[3].Load() == 7, "PaddedInt64 per-slot atomics");

        // 프리셋별 상호 배제 + 코어 수보다 훨씬 많은 스레드(OverSubscribed)
        SpinPreset[] presets = { SpinPreset.LightWeight, SpinPreset.Default, SpinPreset.HeavyContention, SpinPreset.OverSubscribed };
        bool allExcl = true;
        foreach (var preset in presets)
        {
            var m = new SpinMutex(preset); long c = 0;
            RunThreads(Environment.ProcessorCount * 4, t => { for (int i = 0; i < 20_000; ++i) { m.Enter(); c++; m.Exit(); } });
            allExcl &= c == (long)Environment.ProcessorCount * 4 * 20_000;
        }
        Check(allExcl, "SpinMutex mutual exclusion under every preset (4x oversubscribed)");

        // 보유자가 오래 잡고 있어도 대기자는 CPU를 계속 태우지 않고 양보/잠으로 넘어가 끝까지 진행한다
        var slow = new SpinMutex(SpinPreset.OverSubscribed);
        slow.Enter();
        var waiters = new Task[Environment.ProcessorCount * 2];
        int got = 0;
        for (int i = 0; i < waiters.Length; ++i) waiters[i] = Task.Run(() => { slow.Enter(); Interlocked.Increment(ref got); slow.Exit(); });
        Thread.Sleep(300);
        slow.Exit();
        Check(Task.WaitAll(waiters, 5000) && got == waiters.Length, "waiters behind a long-held SpinMutex all proceed after release");

        // 가드: Try / Custom / 소유 여부
        var mx = new SpinMutex();
        bool tryOk;
        var outer = mx.TryLock();
        var inner = mx.TryLock();
        tryOk = outer.IsAcquired && !inner.IsAcquired && mx.IsHeld;
        inner.Dispose(); outer.Dispose();
        Check(tryOk && !mx.IsHeld, "TryLock scope: acquired only once, failed scope does not release");

        var rw = new RWSpinLock(SpinPreset.ReadHeavy);
        using (rw.Lock(LockType.Read)) { Check(rw.ReaderCount == 1 && !rw.IsWriteHeld, "RWSpinLock Lock(LockType.Read) holds a read lock"); }
        using (rw.Lock(LockType.Write)) { Check(rw.IsWriteHeld && !rw.TryEnterRead(), "RWSpinLock Lock(LockType.Write) holds the write lock"); }
        var tw = rw.TryWrite(); var tr = rw.TryRead();
        bool tryRw = tw.IsAcquired && !tr.IsAcquired; tr.Dispose(); tw.Dispose();
        Check(tryRw && !rw.IsWriteHeld && rw.ReaderCount == 0, "RWSpinLock TryWrite/TryRead scopes");

        // 읽기 수 한계(32767)에서 되돌리고 던진다
        var rwMax = new RWSpinLock();
        for (int i = 0; i < 32767; ++i) rwMax.EnterRead();
        bool overflow = Throws<InvalidOperationException>(() => rwMax.EnterRead());
        bool intact = rwMax.ReaderCount == 32767 && !rwMax.IsWriteHeld;
        for (int i = 0; i < 32767; ++i) rwMax.ExitRead();
        Check(overflow && intact && rwMax.ReaderCount == 0 && rwMax.TryEnterWrite(), "RWSpinLock reader-count overflow rolls back and throws");
        rwMax.ExitWrite();

        // TryEnterWrite는 쓰기 대기자가 있어도 읽기 보유자가 0이면 새치기할 수 있다 (설계) — 단, 상태는 일관
        var rw2 = new RWSpinLock();
        rw2.EnterRead();
        var pendingWriter = Task.Run(() => { rw2.EnterWrite(); rw2.ExitWrite(); });
        Thread.Sleep(100);                                  // 쓰기 대기 등록
        bool readerBlocked = !rw2.TryEnterRead();           // 쓰기 우선: 대기자가 있으면 새 읽기는 못 들어온다
        rw2.ExitRead();
        Check(readerBlocked && pendingWriter.Wait(5000) && rw2.ReaderCount == 0 && !rw2.IsWriteHeld, "writer-preference: new reader blocked while a writer waits; state clean after");

        // [CallerMemberName] 이름 (프로파일러 호출이 들어 있는 빌드에서만 확인)
        if (DeadLockProfiler.HooksCompiledIn)
        {
            DeadLockProfiler.ResetGraph();
            var named = new SpinMutex();
            using (named.LockHere()) { Check(DeadLockProfiler.HeldLocks().Length == 1 && DeadLockProfiler.HeldLocks()[0] == nameof(Ported), "LockHere names the lock after the calling method (__func__)"); }
        }

        // SrwLock (Windows가 아니면 RWSpinLock 대체 경로)
        var srw = new SrwLock();
        long sc = 0;
        RunThreads(4, t => { for (int i = 0; i < 50_000; ++i) { srw.ExclusiveLock(); sc++; srw.ExclusiveUnlock(); } });
        Check(sc == 200_000, "SrwLock exclusive mutual exclusion");
        using (srw.Shared())
        {
            var s2 = srw.TryShared(); var x = srw.TryExclusive();
            bool shared = s2.IsAcquired && !x.IsAcquired;
            x.Dispose(); s2.Dispose();
            Check(shared, "SrwLock: second shared OK, exclusive refused while shared held");
        }
        using (srw.Lock(LockType.Write)) { var y = srw.TryShared(); Check(!y.IsAcquired, "SrwLock: shared refused while exclusive held"); y.Dispose(); }
        if (!OperatingSystem.IsWindows()) Check(Throws<PlatformNotSupportedException>(() => { var _h = srw.NativeHandle; }), "SrwLock.NativeHandle is Windows-only");
        srw.Dispose();

        // ThreadManager
        var tm = new ThreadManager();
        int started = 0, exited = 0;
        var ids = new System.Collections.Concurrent.ConcurrentBag<uint>();
        ThreadManager.ThreadStartHook = () => Interlocked.Increment(ref started);
        ThreadManager.ThreadExitHook = () => Interlocked.Increment(ref exited);
        for (int i = 0; i < 4; ++i)
            tm.CreateThread(() => { ids.Add(ThreadManager.CurrentThreadId); while (!tm.IsShuttingDown) Thread.Sleep(1); }, "TM-" + i);
        Thread.Sleep(100);
        var idSet = new HashSet<uint>(ids);
        Check(tm.GetThreadCount() == 4 && idSet.Count == 4 && !idSet.Contains(0u) && ThreadManager.CurrentThreadId == 0, "ThreadManager creates threads with unique non-zero ids");
        tm.RequestShutdown();
        Check(!tm.CreateThread(() => { }) && !tm.ResetShutdown() && tm.GetThreadCount() == 4, "after RequestShutdown: create refused, reset refused while threads remain");
        tm.JoinLastThreads(1); tm.JoinThreadByIndex(0);
        Check(tm.GetThreadCount() == 2, "JoinLastThreads/JoinThreadByIndex remove and join");
        tm.JoinThreadByIndex(99); tm.JoinThreadById(-1);
        tm.JoinThreads();
        Check(tm.GetThreadCount() == 0 && started == 4 && exited == 4 && tm.ResetShutdown() && !tm.IsShuttingDown, "JoinThreads joins all, hooks ran per thread, reset allowed afterwards");
        int ran = 0;
        Check(tm.CreateThread(() => Interlocked.Increment(ref ran)) , "restart after ResetShutdown");
        tm.Dispose();
        Check(ran == 1, "Dispose joins");
        // ID 기준 Join과 자기 자신 Join 생략
        var tm2 = new ThreadManager();
        int tid = 0; var gate = new ManualResetEventSlim();
        tm2.CreateThread(() => { tid = Environment.CurrentManagedThreadId; gate.Set(); });
        gate.Wait(); tm2.JoinThreadById(tid);
        Check(tm2.GetThreadCount() == 0, "JoinThreadById");
        var selfJoin = new ManualResetEventSlim();
        tm2.CreateThread(() => { tm2.JoinThreads(); selfJoin.Set(); });
        Check(selfJoin.Wait(5000), "JoinThreads from a managed thread skips itself (no self-wait)");
        tm2.Dispose();
        ThreadManager.ThreadStartHook = null; ThreadManager.ThreadExitHook = null;

        // SyncValue: 두 필드 구조체가 찢어져 보이지 않는다
        var sv = new SyncValue<(long A, long B)>((0, 0));
        int torn = 0; var stopFlag = new ManualResetEventSlim();
        var rd = Task.Run(() => { while (!stopFlag.IsSet) { var v = sv.Get(); if (v.A != v.B) Interlocked.Increment(ref torn); } });
        for (long i = 1; i <= 2_000_000; ++i) sv.Set((i, i));
        stopFlag.Set(); rd.Wait();
        Check(torn == 0 && sv.Get().A == 2_000_000, $"SyncValue never shows a torn value (torn={torn})");

        // CriticalSection: 재진입 + 상호 배제 + Try
        var cs = new CriticalSection(100);
        long cc = 0;
        RunThreads(4, t => { for (int i = 0; i < 50_000; ++i) { using (cs.Lock()) using (cs.Lock()) cc++; } });
        bool csTry = false;
        using (cs.Lock()) csTry = Task.Run(() => cs.TryEnter()).Result;
        Check(cc == 200_000 && !csTry && cs.TryEnter(), "CriticalSection reentrant, exclusive, TryEnter refused from another thread while held");
        cs.Exit();
    }

    static int Main()
    {
        try
        {
            Locks();
            LockFree();
            Blocking();
            Swaps();
            Map();
            Pool();
            Review();
            Review2();
            Profiler();
            Ported();
        }
        catch (Exception e)
        {
            Console.WriteLine("EXCEPTION: " + e);
            s_fail++;
        }
        Console.WriteLine($"\npass={s_pass} fail={s_fail}");
        if (s_fail == 0) Console.WriteLine("ALL PASSED");
        return s_fail == 0 ? 0 : 1;
    }
}
