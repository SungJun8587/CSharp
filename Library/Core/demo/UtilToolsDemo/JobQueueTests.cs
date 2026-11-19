using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Util;

namespace Core.Demo;

/// <summary>JobQueue(직렬 실행), JobTimer(지연 실행), ScheduledJob(취소) 검증.</summary>
internal static class JobQueueTests
{
    private sealed class CountingQueue : JobQueue
    {
        public int Exceptions;
        public CountingQueue(int maxJobsPerRun = 256) : base(maxJobsPerRun) { }
        protected override void OnException(Exception exception) => Interlocked.Increment(ref Exceptions);
    }

    private static bool WaitFor(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond())
                return true;
            Thread.Sleep(10);
        }

        return cond();
    }

    public static void Run(Action<string, bool> Check)
    {
        // JobQueue: 8개 생산자 × 20000 잡 — 동시 실행 없음, 생산자별 순서 보존, 전부 실행
        {
            var queue = new JobQueue(maxJobsPerRun: 64);
            const int producers = 8, perProducer = 20000;
            int inside = 0, overlap = 0, outOfOrder = 0, executed = 0;
            var last = new int[producers];
            Array.Fill(last, -1);

            var threads = new List<Thread>();
            for (int p = 0; p < producers; p++)
            {
                int producer = p;
                var th = new Thread(() =>
                {
                    for (int i = 0; i < perProducer; i++)
                    {
                        int seq = i;
                        queue.Push(() =>
                        {
                            if (Interlocked.Increment(ref inside) != 1)
                                Interlocked.Increment(ref overlap);
                            if (last[producer] != seq - 1)
                                Interlocked.Increment(ref outOfOrder);
                            last[producer] = seq;
                            executed++;                 // 비원자: 직렬 실행이 깨지면 유실된다
                            Interlocked.Decrement(ref inside);
                        });
                    }
                });
                threads.Add(th);
                th.Start();
            }
            foreach (var th in threads) th.Join();
            WaitFor(() => queue.PendingCount == 0, 20000);
            Check("JobQueue: 직렬 실행(동시 실행 0)", overlap == 0);
            Check("JobQueue: 생산자별 순서 보존", outOfOrder == 0);
            Check("JobQueue: 전부 실행 (양보 재스케줄 경로 포함)", executed == producers * perProducer);
        }

        // JobQueue: 예외가 나도 이후 잡은 계속 실행
        {
            var queue = new CountingQueue();
            int ran = 0;
            queue.Push(() => throw new InvalidOperationException("job boom"));
            queue.Push(() => Interlocked.Increment(ref ran));
            queue.Push(() => throw new InvalidOperationException("job boom 2"));
            queue.Push(() => Interlocked.Increment(ref ran));
            WaitFor(() => queue.PendingCount == 0, 3000);
            Check("JobQueue: 예외 격리", queue.Exceptions == 2 && ran == 2);
        }

        // JobTimer: 지연 순서/시간, 취소, 큐 직렬성
        {
            var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var started = Stopwatch.StartNew();
            var elapsed = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
            var timerQueue = new JobQueue();

            timerQueue.PushAfter(300, () => { order.Enqueue("300"); elapsed["300"] = started.ElapsedMilliseconds; });
            timerQueue.PushAfter(100, () => { order.Enqueue("100"); elapsed["100"] = started.ElapsedMilliseconds; });
            ScheduledJob cancelHandle = timerQueue.PushAfter(150, () => order.Enqueue("150-canceled"));
            timerQueue.PushAfter(200, () => { order.Enqueue("200"); elapsed["200"] = started.ElapsedMilliseconds; });
            cancelHandle.Cancel();

            WaitFor(() => order.Count >= 3, 3000);
            Thread.Sleep(200);
            string seq = string.Join(",", order);
            Check("JobTimer: 지연 순서와 취소", seq == "100,200,300" && cancelHandle.IsCanceled);
            Check("JobTimer: 지연 시간 정확도", elapsed["100"] >= 90 && elapsed["100"] < 400
                  && elapsed["200"] >= 190 && elapsed["300"] >= 290 && elapsed["300"] < 600);

            // 같은 큐의 긴 잡이 끝나기 전에는 만료된 지연 잡도 시작하지 않는다
            long longEnd = 0, delayedStart = 0;
            var serial = new JobQueue();
            serial.Push(() => { Thread.Sleep(400); longEnd = Stopwatch.GetTimestamp(); });
            serial.PushAfter(50, () => delayedStart = Stopwatch.GetTimestamp());
            WaitFor(() => Volatile.Read(ref delayedStart) != 0, 3000);
            Check("JobTimer: 큐 직렬성 유지(긴 잡 뒤에 실행)", delayedStart != 0 && delayedStart >= longEnd);

            // 큐 없이 예약하면 ThreadPool 에서 실행
            int direct = 0;
            JobTimer.Shared.Reserve(50, () => Interlocked.Increment(ref direct));
            Check("JobTimer: 큐 없이 예약", WaitFor(() => Volatile.Read(ref direct) == 1, 2000));

            // Dispose 후에는 예약 불가, 대기 중이던 예약은 실행되지 않는다
            var own = new JobTimer();
            int neverRan = 0;
            own.Reserve(200, () => Interlocked.Increment(ref neverRan));
            own.Dispose();
            bool disposedThrows = false;
            try { own.Reserve(10, () => { }); }
            catch (ObjectDisposedException) { disposedThrows = true; }
            Thread.Sleep(400);
            Check("JobTimer: Dispose 후 예약 거부/버림", disposedThrows && neverRan == 0);
        }

        // JobTimer: 취소가 쌓이면 힙을 정리한다
        {
            using var own = new JobTimer();
            var handles = new List<ScheduledJob>();
            int ranKept = 0;
            for (int i = 0; i < 1000; i++)
                handles.Add(own.Reserve(60000, () => { }));
            for (int i = 0; i < 950; i++)
                handles[i].Cancel();
            for (int i = 0; i < 5; i++)
                own.Reserve(100, () => Interlocked.Increment(ref ranKept));

            Check("JobTimer: 취소 누적 시 힙 정리", own.ScheduledCount <= 200);
            Check("JobTimer: 정리 후에도 정상 예약은 실행", WaitFor(() => Volatile.Read(ref ranKept) == 5, 2000));
        }

        // JobTimer: 고해상도 모드 정확도
        {
            using var precise = new JobTimer(highResolution: true);
            var preciseWatch = Stopwatch.StartNew();
            var errors = new System.Collections.Concurrent.ConcurrentBag<long>();
            for (int i = 0; i < 20; i++)
            {
                long due = 30 + i * 7;
                precise.Reserve((int)due, () => errors.Add(preciseWatch.ElapsedMilliseconds - due));
            }
            WaitFor(() => errors.Count == 20, 3000);
            long max = errors.Count == 0 ? long.MaxValue : errors.Max();
            long min = errors.Count == 0 ? long.MinValue : errors.Min();
            Check("JobTimer(highResolution): 20개 모두 실행, 오차 -2~+20ms", errors.Count == 20 && min >= -2 && max <= 20);
        }

        // JobQueue.Close: 새 잡 거부, 대기 잡은 실행 / discard 이면 버림
        {
            var q = new JobQueue();
            int ran = 0;
            q.Push(() => Thread.Sleep(150));
            for (int i = 0; i < 5; i++) q.Push(() => Interlocked.Increment(ref ran));
            q.Close();
            bool rejected = !q.Push(() => Interlocked.Increment(ref ran));
            Check("JobQueue.Close: 새 Push 거부", rejected && q.IsClosed);
            Check("JobQueue.Close: 이미 들어간 잡은 끝까지 실행", WaitFor(() => Volatile.Read(ref ran) == 5, 3000));
            Check("JobQueue.Close: 닫힌 큐의 PushAfter 는 취소된 핸들", q.PushAfter(10, () => { }).IsCanceled);

            var q2 = new JobQueue();
            int ran2 = 0;
            q2.Push(() => Thread.Sleep(200));
            for (int i = 0; i < 5; i++) q2.Push(() => Interlocked.Increment(ref ran2));
            q2.Close(discardPending: true);
            WaitFor(() => q2.PendingCount == 0, 3000);
            Check("JobQueue.Close(discardPending): 시작 전 잡은 버림", ran2 == 0);
        }

        // JobQueue.Close 는 이 큐의 지연 예약을 취소한다
        {
            var q = new JobQueue();
            int ranDelayed = 0;
            var handles = new List<ScheduledJob>();
            for (int i = 0; i < 100; i++)
                handles.Add(q.PushAfter(200, () => Interlocked.Increment(ref ranDelayed)));
            q.Close();
            Thread.Sleep(500);
            Check("JobQueue.Close: 지연 예약 100개 취소, 실행 0", handles.All(h => h.IsCanceled) && ranDelayed == 0);
        }

        // 기본 OnException은 LogThrottle로 남기고 이후 잡은 계속 실행한다
        {
            LogThrottle.Clear();
            var q = new JobQueue();
            int ran = 0;
            q.Push(() => throw new InvalidOperationException("default handler"));
            q.Push(() => Interlocked.Increment(ref ran));
            WaitFor(() => q.PendingCount == 0, 3000);
            Check("JobQueue: 기본 예외 처리는 로그로 남기고 계속 실행", ran == 1 && LogThrottle.KeyCount >= 1);
        }

        // 해제된 JobTimer에 큐를 지정해 예약하면 예외를 던지되 큐에 항목이 남지 않는다
        {
            var t = new JobTimer();
            var q = new JobQueue();
            t.Dispose();
            bool threw = false;
            try { t.Reserve(100, () => { }, q); } catch (ObjectDisposedException) { threw = true; }
            q.Close();
            Check("JobTimer: 해제된 타이머 예약 거부(큐 추적 목록 정리)", threw);
        }
    }
}
