// JobQueue.cs : implementation of the JobQueue, JobTimer and ScheduledJob classes.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Core.Net;

/// <summary>
/// 직렬 실행 잡 큐. 여러 스레드에서 Push 한 작업을 Push 된 순서대로, 한 번에 하나씩만 ThreadPool 에서 실행한다.
/// 락 없이 객체(방, 플레이어 등)별로 상태를 보호하는 방식이며, I/O 스레드(Session 콜백)에서
/// 콘텐츠 처리를 분리할 때 쓴다. 전용 스레드를 점유하지 않고, 한 큐가 ThreadPool 을 독점하지 않도록
/// 한 번에 MaxJobsPerRun 개까지만 실행한 뒤 다시 스케줄한다.
/// </summary>
public class JobQueue
{
    private readonly ConcurrentQueue<Action> _jobs = new ConcurrentQueue<Action>();
    private readonly int _maxJobsPerRun;
    private int _pending;  // 큐에 있거나 실행 중인 작업 수. 0 -> 1 로 바뀌는 Push 가 실행을 스케줄한다.
    private volatile bool _closed;
    private volatile bool _discard;

    // 이 큐로 예약된(아직 만료/취소되지 않은) 지연 잡. Close 때 한꺼번에 취소해 타이머 힙이 큐를 붙잡지 않게 한다.
    private readonly ConcurrentDictionary<ScheduledJob, byte> _scheduled = new ConcurrentDictionary<ScheduledJob, byte>();

    /// <param name="maxJobsPerRun">ThreadPool 작업 하나가 연속으로 실행할 최대 잡 수. 넘으면 다시 스케줄해 다른 큐에 양보한다.</param>
    public JobQueue(int maxJobsPerRun = 256)
    {
        if (maxJobsPerRun <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxJobsPerRun));

        _maxJobsPerRun = maxJobsPerRun;
    }

    /// <summary>대기 중이거나 실행 중인 잡 수.</summary>
    public int PendingCount => Volatile.Read(ref _pending);

    /// <summary>Close 가 호출되었는지 여부.</summary>
    public bool IsClosed => _closed;

    /// <summary>
    /// 잡을 큐에 넣는다. 같은 큐의 잡은 동시에 실행되지 않으며 Push 순서(스레드별 순서)가 유지된다.
    /// </summary>
    /// <returns>큐가 닫혀 있어 받아들이지 않았으면 false.</returns>
    public bool Push(Action job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));

        if (_closed)
            return false;

        _jobs.Enqueue(job);

        if (Interlocked.Increment(ref _pending) == 1)
            Schedule();

        return true;
    }

    /// <summary>
    /// delayMs 뒤에 이 큐에서 실행되도록 예약한다(JobTimer.Shared 사용). 반환된 핸들로 취소할 수 있다.
    /// 큐가 이미 닫혀 있으면 예약하지 않고 취소된 핸들을 돌려준다.
    /// </summary>
    public ScheduledJob PushAfter(int delayMs, Action job)
    {
        if (_closed)
            return ScheduledJob.CreateCanceled();

        return JobTimer.Shared.Reserve(delayMs, job, this);
    }

    /// <summary>
    /// 큐를 닫는다. 이후 Push 는 거부된다. 이미 들어 있는 잡은 기본적으로 끝까지 실행되고,
    /// discardPending 이 true 이면 아직 시작하지 않은 잡을 실행하지 않고 버린다(진행 중인 잡은 끝까지 실행).
    /// 닫힌 뒤에도 Close 를 다시 호출해 discardPending 만 올릴 수 있다.
    /// </summary>
    public void Close(bool discardPending = false)
    {
        if (discardPending)
            _discard = true;

        _closed = true;

        foreach (var pair in _scheduled)
            pair.Key.Cancel();
    }

    internal void Track(ScheduledJob job)
    {
        _scheduled.TryAdd(job, 0);

        // Close 와 경합해 Close 의 열거에서 빠졌을 수 있으므로 직접 취소한다.
        if (_closed)
            job.Cancel();
    }

    internal void Untrack(ScheduledJob job) => _scheduled.TryRemove(job, out _);

    /// <summary>잡에서 예외가 발생했을 때 호출된다. 기본 구현은 Trace 로 기록하며, 이후 잡은 계속 실행된다.</summary>
    protected virtual void OnException(Exception exception)
    {
        Trace.TraceError("[JobQueue] {0}", exception);
    }

    private void Schedule()
    {
        ThreadPool.UnsafeQueueUserWorkItem(static queue => queue.Run(), this, preferLocal: false);
    }

    private void Run()
    {
        int budget = _maxJobsPerRun;

        while (true)
        {
            // _pending > 0 이면 Enqueue 가 이미 끝났으므로 곧 꺼낼 수 있다(드물게 가시성 지연만 대기).
            Action? job;
            var spin = new SpinWait();
            while (!_jobs.TryDequeue(out job))
                spin.SpinOnce();

            if (!_discard)
            {
                try
                {
                    job();
                }
                catch (Exception e)
                {
                    try { OnException(e); }
                    catch (Exception) { }
                }
            }

            if (Interlocked.Decrement(ref _pending) == 0)
                return;

            if (--budget == 0)
            {
                // 아직 잡이 남았다: ThreadPool 을 독점하지 않도록 새 작업으로 이어서 실행한다.
                Schedule();
                return;
            }
        }
    }
}

/// <summary>
/// JobTimer 에 예약된 작업의 핸들. Cancel() 하면 아직 실행되지 않은 작업은 건너뛴다.
/// </summary>
public sealed class ScheduledJob
{
    private const int StatePending = 0;     // 타이머 힙에서 대기 중
    private const int StateCanceled = 1;    // 힙에 있는 채로 취소됨
    private const int StateDispatched = 2;  // 만료되어 큐/ThreadPool 로 넘어감
    private const int StateCanceledLate = 3;// 넘어간 뒤 취소됨(실행 직전에 걸러짐)

    private readonly Action _job;
    private readonly JobTimer? _timer;
    private int _state;

    internal ScheduledJob(Action job, JobQueue? queue, long dueMs, JobTimer? timer)
    {
        _job = job;
        _timer = timer;
        Queue = queue;
        DueMs = dueMs;

        // 타이머 스레드가 아니라 예약하는 쪽에서 한 번만 만든다.
        RunAction = Run;
    }

    internal static ScheduledJob CreateCanceled()
    {
        var job = new ScheduledJob(static () => { }, null, 0, null);
        job._state = StateCanceledLate;
        return job;
    }

    internal JobQueue? Queue { get; }

    internal long DueMs { get; }

    internal Action RunAction { get; }

    /// <summary>취소되었는지 여부.</summary>
    public bool IsCanceled
    {
        get
        {
            int state = Volatile.Read(ref _state);
            return state == StateCanceled || state == StateCanceledLate;
        }
    }

    /// <summary>아직 실행되지 않았다면 실행하지 않도록 취소한다. 이미 큐에 들어간 작업도 실행 직전에 걸러진다.</summary>
    public void Cancel()
    {
        while (true)
        {
            int state = Volatile.Read(ref _state);

            if (state == StatePending)
            {
                if (Interlocked.CompareExchange(ref _state, StateCanceled, StatePending) == StatePending)
                {
                    Queue?.Untrack(this);
                    _timer?.OnJobCanceled();
                    return;
                }
            }
            else if (state == StateDispatched)
            {
                if (Interlocked.CompareExchange(ref _state, StateCanceledLate, StateDispatched) == StateDispatched)
                    return;
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>타이머 스레드(힙 락 안)에서 호출: 만료 처리 권한을 얻으면 true, 이미 취소되었으면 false.</summary>
    internal bool TryMarkDispatched()
    {
        if (Interlocked.CompareExchange(ref _state, StateDispatched, StatePending) != StatePending)
            return false;

        Queue?.Untrack(this);
        return true;
    }

    internal bool IsCanceledInHeap => Volatile.Read(ref _state) == StateCanceled;

    private void Run()
    {
        if (!IsCanceled)
            _job();
    }
}

/// <summary>
/// 지연 실행 타이머. 우선순위 큐와 전용 스레드 하나로 모든 예약을 관리하고,
/// 만료된 작업은 지정한 JobQueue 로 넘기거나(직렬 실행) 큐가 없으면 ThreadPool 에서 실행한다.
/// 취소된 예약이 쌓이면(취소 수가 일정 수 이상이고 힙의 절반 이상) 힙을 다시 만들어 메모리를 돌려준다.
/// </summary>
public sealed class JobTimer : IDisposable
{
    // 취소된 항목이 이 개수 이상이고 힙의 절반 이상일 때 힙을 정리한다.
    private const int CompactMinCanceled = 64;

    // highResolution 모드에서 만료 직전 이 구간(ms)은 대기 대신 스핀으로 맞춘다.
    // Windows 의 기본 대기 해상도(약 15.6ms)보다 커야 한다.
    private const int SpinWindowMs = 16;

    private static readonly Lazy<JobTimer> s_shared =
        new Lazy<JobTimer>(() => new JobTimer(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly PriorityQueue<ScheduledJob, long> _heap = new PriorityQueue<ScheduledJob, long>();
    private readonly object _lock = new object();
    private readonly Thread _thread;
    private readonly bool _highResolution;
    private int _canceledInHeap;
    private int _topChanged;
    private bool _disposed;

    /// <param name="highResolution">
    /// true 이면 만료 직전 16ms 구간을 스핀으로 맞춰 ms 단위 정확도를 얻는다(타이머 스레드가 그 구간 동안 CPU 를 쓴다).
    /// false 이면 OS 대기 해상도를 따른다(Windows 는 약 15ms 단위).
    /// </param>
    public JobTimer(bool highResolution = false)
    {
        _highResolution = highResolution;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Core.JobTimer" };
        _thread.Start();
    }

    /// <summary>프로세스 공용 타이머. 백그라운드 스레드이므로 프로세스 종료를 막지 않는다.</summary>
    public static JobTimer Shared => s_shared.Value;

    /// <summary>힙에 남아 있는 예약 수(취소되었지만 아직 정리되지 않은 항목 포함).</summary>
    public int ScheduledCount
    {
        get { lock (_lock) return _heap.Count; }
    }

    /// <summary>
    /// delayMs 뒤에 job 을 실행하도록 예약한다.
    /// </summary>
    /// <param name="delayMs">지연(ms). 0 이하이면 가능한 빨리.</param>
    /// <param name="job">실행할 작업.</param>
    /// <param name="queue">지정하면 이 큐에서 직렬로 실행되고, null 이면 ThreadPool 에서 실행된다.</param>
    public ScheduledJob Reserve(int delayMs, Action job, JobQueue? queue = null)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));

        var item = new ScheduledJob(job, queue, NowMs() + Math.Max(0, delayMs), this);
        queue?.Track(item);

        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JobTimer));

            _heap.Enqueue(item, item.DueMs);

            // 새 작업이 가장 이른 만료라면 대기 중인 스레드를 깨워 대기 시간을 다시 계산하게 한다.
            if (_heap.TryPeek(out ScheduledJob? top, out _) && ReferenceEquals(top, item))
            {
                Volatile.Write(ref _topChanged, 1);
                Monitor.Pulse(_lock);
            }
        }

        return item;
    }

    /// <summary>타이머를 멈추고 아직 실행되지 않은 예약은 버린다.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _heap.Clear();
            _canceledInHeap = 0;
            Monitor.Pulse(_lock);
        }
    }

    internal void OnJobCanceled()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _canceledInHeap++;

            if (_canceledInHeap >= CompactMinCanceled && _canceledInHeap * 2 >= _heap.Count)
                Compact();
        }
    }

    // 락을 잡은 상태에서 호출: 취소된 항목을 빼고 힙을 다시 만든다.
    private void Compact()
    {
        var alive = new List<(ScheduledJob Job, long Due)>(_heap.Count - _canceledInHeap);
        foreach ((ScheduledJob job, long due) in _heap.UnorderedItems)
        {
            if (!job.IsCanceledInHeap)
                alive.Add((job, due));
        }

        _heap.Clear();
        _heap.EnqueueRange(alive);
        _canceledInHeap = 0;
    }

    // Environment.TickCount64 는 Windows 에서 해상도가 10~16ms 라서 고해상도 타이머로 ms 를 계산한다.
    private static long NowMs() => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

    private void Loop()
    {
        var due = new List<ScheduledJob>();

        while (true)
        {
            long spinUntil = 0;

            lock (_lock)
            {
                while (true)
                {
                    if (_disposed)
                        return;

                    if (!_heap.TryPeek(out _, out long dueMs))
                    {
                        Monitor.Wait(_lock);
                        continue;
                    }

                    long wait = dueMs - NowMs();
                    if (wait <= 0)
                        break;

                    if (_highResolution)
                    {
                        if (wait <= SpinWindowMs)
                        {
                            spinUntil = dueMs;
                            Volatile.Write(ref _topChanged, 0);
                            break;
                        }

                        Monitor.Wait(_lock, (int)Math.Min(wait - SpinWindowMs, int.MaxValue));
                    }
                    else
                    {
                        Monitor.Wait(_lock, (int)Math.Min(wait, int.MaxValue));
                    }
                }

                if (spinUntil == 0)
                {
                    long now = NowMs();
                    while (_heap.TryPeek(out ScheduledJob? top, out long topDue) && topDue <= now)
                    {
                        _heap.Dequeue();

                        if (top.TryMarkDispatched())
                            due.Add(top);
                        else if (_canceledInHeap > 0)
                            _canceledInHeap--;
                    }
                }
            }

            if (spinUntil != 0)
            {
                // 락 밖에서 만료 직전까지 스핀한다. 더 이른 예약이 들어오면 즉시 다시 계산한다.
                var spin = new SpinWait();
                while (NowMs() < spinUntil && Volatile.Read(ref _topChanged) == 0)
                    spin.SpinOnce(sleep1Threshold: -1);

                continue;
            }

            // 락 밖에서 디스패치: 사용자 코드가 타이머 락을 잡고 실행되지 않게 한다.
            foreach (ScheduledJob job in due)
                Dispatch(job);

            due.Clear();
        }
    }

    private static void Dispatch(ScheduledJob job)
    {
        if (job.Queue != null)
        {
            job.Queue.Push(job.RunAction);
            return;
        }

        ThreadPool.UnsafeQueueUserWorkItem(static scheduled =>
        {
            try { scheduled.RunAction(); }
            catch (Exception e) { Trace.TraceError("[JobTimer] {0}", e); }
        }, job, preferLocal: false);
    }
}
