// SwapQueues.cs : 한 번에 여러 개를 꺼내 가는 단순 락 큐 두 가지 (ChunkedSwapQueue, SpinLockQueue).
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 소비자가 한 번에 가져가는 개수를 제한(청킹)하는 큐. 한 틱에 너무 많이 처리해 지연이 커지는 일을 막는다.
/// 생산자/소비자 모두 여러 스레드여도 안전하다(단일 락). 락 보유 중에는 원소 이동만 한다.
/// </summary>
public sealed class ChunkedSwapQueue<T>
{
    private readonly object _gate = new object();
    private readonly Queue<T> _queue = new Queue<T>();

    public int Count
    {
        get { lock (_gate) return _queue.Count; }
    }

    public void Push(T item)
    {
        lock (_gate)
            _queue.Enqueue(item);
    }

    public void PushBatch(IReadOnlyList<T> items)
    {
        lock (_gate)
        {
            for (int i = 0; i < items.Count; ++i)
                _queue.Enqueue(items[i]);
        }
    }

    /// <summary>최대 maxItems개를 dest에 덧붙인다(dest는 비우지 않는다). 가져온 개수를 돌려준다. 기다리지 않는다.</summary>
    public int SwapChunk(List<T> dest, int maxItems)
    {
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems));

        lock (_gate)
        {
            int n = Math.Min(maxItems, _queue.Count);
            for (int i = 0; i < n; ++i)
                dest.Add(_queue.Dequeue());
            return n;
        }
    }
}

/// <summary>
/// 스핀 락(<see cref="SpinMutex"/>) 기반 MPMC 큐. 임계 구역이 원소 하나의 이동뿐이라 경합이 낮을 때 Monitor보다 가볍다.
/// <see cref="PopAll"/>은 전체를 한 번에 꺼낸다. 소비자가 느려 락 보유 시간이 길어질 수 있는 구조에는 <see cref="ChunkedSwapQueue{T}"/>를 쓴다.
/// </summary>
public sealed class SpinLockQueue<T>
{
    private readonly SpinMutex _lock = new SpinMutex();
    private Queue<T> _queue = new Queue<T>();
    private Queue<T>? _spare = new Queue<T>();      // PopAll이 교환해 두는 빈 큐 (재사용)

    public int Count
    {
        get { using (_lock.Lock()) return _queue.Count; }
    }

    public void Push(T item)
    {
        using (_lock.Lock())
            _queue.Enqueue(item);
    }

    public bool TryPop(out T item)
    {
        using (_lock.Lock())
        {
            if (_queue.Count > 0)
            {
                item = _queue.Dequeue();
                return true;
            }
        }

        item = default!;
        return false;
    }

    /// <summary>
    /// 전체를 dest에 덧붙인다(dest는 비우지 않는다). 가져온 개수를 돌려준다.
    /// 스핀 락 안에서는 큐 참조만 맞바꾸고 원소 복사는 락 밖에서 하므로, 쌓인 양이 많아도 생산자를 오래 붙잡지 않는다.
    /// 여러 스레드가 동시에 불러도 각 원소는 한 호출에만 전달된다.
    /// </summary>
    public int PopAll(List<T> dest)
    {
        Queue<T> taken;
        using (_lock.Lock())
        {
            if (_queue.Count == 0)
                return 0;

            taken = _queue;
            _queue = _spare ?? new Queue<T>();      // 다른 PopAll이 spare를 가져가 복사 중이면 새로 만든다
            _spare = null;
        }

        int n = taken.Count;
        while (taken.Count > 0)
            dest.Add(taken.Dequeue());

        using (_lock.Lock())
            _spare = taken;

        return n;
    }
}
