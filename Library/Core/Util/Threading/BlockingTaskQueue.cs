// BlockingTaskQueue.cs : 락 기반 MPMC 블로킹 큐 (생산 종료·정지 신호, 선택적 용량 제한).
using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 여러 생산자/여러 소비자용 블로킹 큐. 파일 탐색·병렬 복사처럼 한쪽이 작업을 쌓고 여러 워커가 가져가는 구조에 쓴다.
///
/// <para><b>종료 신호</b>: 생산자는 더 넣을 것이 없으면 <see cref="SetProducerDone"/>을 부른다. 소비자는 남은 것을 모두 꺼낸 뒤
/// <see cref="Pop"/>/<see cref="PopChunk"/>가 false/0을 돌려받아 종료한다. <see cref="Stop"/>은 남은 것을 버리고 즉시 모두 깨운다.</para>
/// <para><b>깨움 유실 방지</b>: 두 신호 모두 큐 락 안에서 상태를 바꾼 뒤 알린다. 락 밖에서 바꾸면 소비자가 조건을 검사한 직후 잠들기
/// 직전에 신호가 지나가 영원히 깨어나지 못할 수 있다.</para>
/// <para><b>용량 제한</b>: maxQueueSize &gt; 0이면 가득 찼을 때 <see cref="Push"/>가 자리가 날 때까지 기다린다(백프레셔).
/// <see cref="PushBatch"/>는 배치 전체가 들어갈 자리를 확보한 뒤 한 번에 넣는다(배치가 용량보다 크면 큐가 빌 때 통째로 넣는다).</para>
/// <para>Push/PushBatch는 정지 또는 생산 종료 뒤에는 false를 돌려준다.</para>
/// </summary>
public sealed class BlockingTaskQueue<T>
{
    private readonly Queue<T> _queue = new Queue<T>();
    private readonly object _gate = new object();
    private readonly int _maxQueueSize;
    private bool _producerDone;
    private bool _stopped;
    private int _consumersWaiting;      // _gate 안에서만 읽고 쓴다
    private int _producersWaiting;

    /// <param name="maxQueueSize">0 이하면 무제한</param>
    public BlockingTaskQueue(int maxQueueSize = 0) => _maxQueueSize = maxQueueSize > 0 ? maxQueueSize : 0;

    public int Count
    {
        get { lock (_gate) return _queue.Count; }
    }

    public bool IsStopped
    {
        get { lock (_gate) return _stopped; }
    }

    public bool Push(T item)
    {
        lock (_gate)
        {
            if (!WaitForSpace(1))
                return false;

            _queue.Enqueue(item);
            WakeConsumers(1);
            return true;
        }
    }

    public bool PushBatch(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
            return true;

        lock (_gate)
        {
            if (!WaitForSpace(items.Count))
                return false;

            for (int i = 0; i < items.Count; ++i)
                _queue.Enqueue(items[i]);
            WakeConsumers(items.Count);
            return true;
        }
    }

    /// <summary>하나 꺼낸다. 큐가 비어 있으면 기다린다. 생산 종료 후 비었거나 정지되면 false.</summary>
    public bool Pop(out T item, int timeoutMs = Timeout.Infinite)
    {
        lock (_gate)
        {
            if (!WaitForItem(timeoutMs))
            {
                item = default!;
                return false;
            }

            item = _queue.Dequeue();
            WakeProducers();
            return true;
        }
    }

    /// <summary>
    /// 최대 maxItems개를 dest에 덧붙인다(dest는 비우지 않는다). 큐가 비어 있으면 기다린다.
    /// 반환값은 가져온 개수이며, 0이면 생산 종료 후 비었거나 정지되었거나 시간이 지난 것이다.
    /// </summary>
    public int PopChunk(List<T> dest, int maxItems, int timeoutMs = Timeout.Infinite)
    {
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems));

        lock (_gate)
        {
            if (!WaitForItem(timeoutMs))
                return 0;

            int n = Math.Min(maxItems, _queue.Count);
            for (int i = 0; i < n; ++i)
                dest.Add(_queue.Dequeue());

            WakeProducers();
            return n;
        }
    }

    /// <summary>더 넣을 것이 없음을 알린다. 남은 것은 소비자가 모두 꺼낼 수 있다.</summary>
    public void SetProducerDone()
    {
        lock (_gate)
        {
            _producerDone = true;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>남은 것을 버리고 정지한다. 대기 중인 모든 스레드가 깨어난다.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _queue.Clear();
            Monitor.PulseAll(_gate);
        }
    }

    // _gate를 쥔 채 호출. count개를 넣을 자리가 생길 때까지 기다린다. 정지/생산 종료면 false.
    private bool WaitForSpace(int count)
    {
        while (true)
        {
            if (_stopped || _producerDone)
                return false;

            if (_maxQueueSize == 0)
                return true;

            // 배치가 용량보다 크면 빌 때까지 기다린 뒤 통째로 넣는다.
            int need = Math.Min(count, _maxQueueSize);
            if (_queue.Count + need <= _maxQueueSize || _queue.Count == 0)
                return true;

            ++_producersWaiting;
            try { Monitor.Wait(_gate); }
            finally { --_producersWaiting; }
        }
    }

    // 소비자 대기자를 깨운다. 같은 _gate에서 생산자도 기다리고 있으면 Pulse가 엉뚱한 쪽을 깨울 수 있어 PulseAll,
    // 기다리는 쪽이 소비자뿐이고 원소가 하나 늘었으면 한 명만 깨워 불필요한 깨어남(thundering herd)을 피한다. _gate를 쥔 채 호출.
    private void WakeConsumers(int added)
    {
        if (_consumersWaiting == 0)
            return;

        if (added > 1 || _producersWaiting != 0)
        {
            Monitor.PulseAll(_gate);
            return;
        }

        Monitor.Pulse(_gate);
    }

    // 용량 제한 큐에서 자리를 기다리는 생산자가 있을 때만 깨운다. _gate를 쥔 채 호출.
    private void WakeProducers()
    {
        if (_producersWaiting != 0)
            Monitor.PulseAll(_gate);
    }

    // _gate를 쥔 채 호출. 꺼낼 것이 생기면 true. 정지/종료+빔/시간 초과면 false.
    private bool WaitForItem(int timeoutMs)
    {
        long deadline = timeoutMs < 0 ? long.MaxValue : TimeUtil.MonotonicNowMillis() + timeoutMs;

        while (true)
        {
            if (_stopped)
                return false;

            if (_queue.Count > 0)
                return true;

            if (_producerDone)
                return false;

            ++_consumersWaiting;
            try
            {
                if (deadline == long.MaxValue)
                {
                    Monitor.Wait(_gate);
                }
                else
                {
                    long left = deadline - TimeUtil.MonotonicNowMillis();
                    if (left <= 0)
                        return false;
                    Monitor.Wait(_gate, (int)Math.Min(left, int.MaxValue));
                }
            }
            finally
            {
                --_consumersWaiting;
            }
        }
    }
}
