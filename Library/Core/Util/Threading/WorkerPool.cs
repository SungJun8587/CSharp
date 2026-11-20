// WorkerPool.cs : 고정 개수의 전용 스레드가 작업 큐를 소비하는 실행기.
using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 시작 시 정해진 수의 전용 스레드를 만들어 <see cref="Post"/>한 작업을 가져다 실행한다. ThreadPool과 달리 스레드 수와 이름이 고정이라
/// 오래 걸리는 작업(파일 복사, DB 호출)이 풀 전체를 잠식하지 않고, 진단 도구에서 스레드를 알아보기 쉽다.
///
/// <para><b>순서</b>: 하나의 큐를 여러 워커가 나눠 가져가므로 작업은 큐에 넣은 순서로 <i>시작</i>되지만 서로 다른 워커에서 겹쳐 실행될 수 있다.
/// 순서가 중요하면 워커를 1개로 하거나 키별로 한 큐에 몰아넣는다.</para>
/// <para><b>예외</b>: 작업에서 난 예외는 잡아 onError(없으면 <see cref="LogManager"/>)로 알리고 워커는 계속 돈다. 호출부에 전파되지 않는다.</para>
/// <para>워커는 백그라운드 스레드라서, Stop 없이 프로세스가 끝나면 큐에 남은 작업은 실행되지 않는다.</para>
/// <para><b>주의</b>: maxQueueSize를 두었을 때 워커 안에서 같은 풀에 Post하면, 큐가 가득 찬 순간 모든 워커가 Post에서 자리를 기다려
/// 서로를 깨우지 못하는 교착이 될 수 있다. 워커가 같은 풀에 되먹이는 구조는 무제한 큐로 쓴다.</para>
/// <para><b>종료</b>: <see cref="Stop(bool)"/>(drain: true)는 이미 넣은 작업을 모두 끝낸 뒤 스레드를 합류시킨다. drain: false는 남은 작업을 버리고
/// 실행 중인 작업만 끝나길 기다린다. 두 경우 모두 Stop 이후의 Post는 false를 돌려준다.
/// drain 정지가 진행 중일 때 다른 스레드가 Stop(false)를 부르면 남은 작업을 버리는 쪽으로 바뀐다. 워커 안에서 Stop을 불러도 자기 자신은 합류하지 않는다.</para>
/// </summary>
public sealed class WorkerPool : IDisposable
{
    private readonly BlockingTaskQueue<Action> _queue;
    private readonly Thread[] _threads;
    private readonly Action<Exception>? _onError;
    private int _stopRequested;

    /// <param name="threadCount">워커 수 (1 이상)</param>
    /// <param name="name">스레드 이름 접두어</param>
    /// <param name="maxQueueSize">0 이하면 무제한. 가득 차면 Post가 자리가 날 때까지 기다린다(백프레셔).</param>
    /// <param name="onError">작업에서 예외가 났을 때 워커 스레드에서 호출된다(null이면 LogManager로 기록). 이 콜백이 던진 예외는 무시된다.</param>
    public WorkerPool(int threadCount, string name = "Worker", int maxQueueSize = 0, Action<Exception>? onError = null)
    {
        if (threadCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(threadCount));

        _onError = onError;
        _queue = new BlockingTaskQueue<Action>(maxQueueSize);
        _threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; ++i)
        {
            _threads[i] = new Thread(WorkerMain) { IsBackground = true, Name = name + "-" + i };
            _threads[i].Start();
        }
    }

    public int ThreadCount => _threads.Length;

    /// <summary>아직 시작되지 않은 작업 수.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>작업을 넣는다. 정지 후에는 false(작업은 실행되지 않는다).</summary>
    public bool Post(Action work)
    {
        if (work == null)
            throw new ArgumentNullException(nameof(work));

        return _queue.Push(work);
    }

    /// <summary>워커를 멈추고 합류시킨다. 여러 번 불러도 안전하다.</summary>
    /// <param name="drain">true면 큐에 남은 작업을 모두 실행한 뒤 종료, false면 남은 작업을 버린다.</param>
    public void Stop(bool drain = true) => Stop(drain, Timeout.Infinite);

    /// <summary>Stop 과 같지만 스레드가 끝나길 최대 timeoutMs 만 기다린다. 모두 끝났으면 true(작업 하나가 멈추면 종료도 멈추는 것을 막는다).</summary>
    public bool Stop(bool drain, int timeoutMs)
    {
        bool first = Interlocked.Exchange(ref _stopRequested, 1) == 0;
        if (!drain)
            _queue.Stop();                  // 이미 drain 정지 중이어도 남은 작업을 버리도록 바꾼다 (Stop은 여러 번 불러도 안전)
        else if (first)
            _queue.SetProducerDone();

        long deadline = timeoutMs < 0 ? long.MaxValue : TimeUtil.MonotonicNowMillis() + timeoutMs;
        bool all = true;
        foreach (Thread t in _threads)
        {
            if (t == Thread.CurrentThread)
                continue;
            if (timeoutMs < 0)
            {
                t.Join();
                continue;
            }
            long left = deadline - TimeUtil.MonotonicNowMillis();
            if (left <= 0 || !t.Join((int)Math.Min(left, int.MaxValue)))
                all = false;
        }
        return all;
    }

    public void Dispose() => Stop(true);

    private void WorkerMain()
    {
        while (_queue.Pop(out Action work))
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                ReportError(e);
            }
        }
    }

    private void ReportError(Exception e)
    {
        try
        {
            if (_onError != null)
                _onError(e);
            else
                LogManager.Error("WorkerPool 작업 예외: {0}", e);
        }
        catch
        {
            // 오류 보고 중의 예외로 워커가 죽으면 안 된다.
        }
    }
}
