// LockFreeQueue.cs : 고정 크기 MPMC lock-free 링 버퍼 큐 (Vyukov bounded queue).
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 여러 생산자/여러 소비자가 락 없이 쓰는 고정 크기 큐. 슬롯마다 순번(sequence)을 두고 CAS로 위치를 선점한다.
///
/// <para><b>순서</b>: FIFO다. 단, 한 스레드가 위치를 선점한 뒤 값을 쓰는 사이에 다른 소비자가 그 슬롯에 도달하면 값이 발행될 때까지
/// <see cref="TryDequeue"/>는 false를 돌려줄 수 있다(비어 있는 것으로 보임). 이는 정상이다.</para>
/// <para><b>용량</b>: 생성 시 2의 거듭제곱으로 올림한다(최소 2). 가득 차면 <see cref="TryEnqueue"/>가 false.</para>
/// <para><b>블로킹 변형</b>: <see cref="Enqueue"/>/<see cref="Dequeue"/>는 짧게 스핀한 뒤 조건 대기로 넘어간다. 깨움 신호가 유실돼도
/// 10ms마다 다시 확인하므로 영원히 잠들지 않는다(유실은 대기 등록과 신호가 정확히 겹칠 때만 생기며, 그때의 추가 지연이 최대 10ms다).</para>
/// <para>값 타입 T는 그대로, 참조 타입 T는 꺼낸 뒤 슬롯을 비워 GC가 객체를 붙들지 않게 한다.</para>
/// </summary>
public sealed class LockFreeQueue<T>
{
    private struct Slot
    {
        public long Sequence;
        public T Value;
    }

    private static readonly int BlockingSpinCount = Environment.ProcessorCount > 1 ? 64 : 0;     // 코어 하나면 스핀해도 상대가 못 달린다
    private const int WaitSliceMs = 10;

    private readonly Slot[] _slots;
    private readonly long _mask;
    private readonly PaddedCursor _enqueue = new PaddedCursor();
    private readonly PaddedCursor _dequeue = new PaddedCursor();

    // 소비자 대기자와 생산자 대기자를 서로 다른 객체에서 기다리게 해, 한쪽 신호가 반대쪽 대기자를 쓸데없이 깨우지 않게 한다.
    private readonly object _consumerGate = new object();
    private readonly object _producerGate = new object();
    private int _consumersWaiting;
    private int _producersWaiting;

    public LockFreeQueue(int capacity)
    {
        if (capacity < 2)
            capacity = 2;
        if (capacity > (1 << 30))
            throw new ArgumentOutOfRangeException(nameof(capacity));

        int size = (int)RoundUpPow2((uint)capacity);
        _slots = new Slot[size];
        _mask = size - 1;
        for (int i = 0; i < size; ++i)
            _slots[i].Sequence = i;
    }

    /// <summary>슬롯 수(2의 거듭제곱).</summary>
    public int Capacity => _slots.Length;

    /// <summary>대략적인 원소 수. 다른 스레드가 동시에 쓰고 있으면 근삿값이다.</summary>
    public int SizeApprox
    {
        get
        {
            long d = Volatile.Read(ref _dequeue.Value);
            long e = Volatile.Read(ref _enqueue.Value);
            long n = e - d;
            if (n < 0) return 0;
            if (n > _slots.Length) return _slots.Length;
            return (int)n;
        }
    }

    public bool TryEnqueue(T item)
    {
        if (!TryEnqueueCore(item))
            return false;

        NotifyConsumers();
        return true;
    }

    // 알림 없이 넣기만 한다. 대기용 락(_producerGate/_consumerGate)을 쥔 채로는 이것만 부르고 알림은 락을 놓은 뒤에 한다 —
    // 락을 쥔 채 반대쪽 게이트를 잡으면 생산자(producerGate -> consumerGate)와 소비자(consumerGate -> producerGate)가 서로를 기다리는 교착이 된다.
    private bool TryEnqueueCore(T item)
    {
        long pos = Volatile.Read(ref _enqueue.Value);
        while (true)
        {
            ref Slot slot = ref _slots[pos & _mask];
            long seq = Volatile.Read(ref slot.Sequence);
            long dif = seq - pos;

            if (dif == 0)
            {
                long seen = Interlocked.CompareExchange(ref _enqueue.Value, pos + 1, pos);
                if (seen == pos)
                {
                    slot.Value = item;
                    // 값이 보이는 시점. 일반 저장(Volatile.Write)은 x86에서 뒤의 대기자 수 읽기보다 늦게 보일 수 있어, 대기 등록(Interlocked.Increment)과
                    // 맞물릴 때 "생산자는 대기자 0명을 보고 건너뛰고 소비자는 값을 못 보고 잠드는" 어긋남이 생긴다. Exchange는 전체 펜스라 이를 막는다.
                    Interlocked.Exchange(ref slot.Sequence, pos + 1);
                    return true;
                }
                pos = seen;
            }
            else if (dif < 0)
            {
                return false;   // 가득 참 (한 바퀴 전 값이 아직 안 나감)
            }
            else
            {
                pos = Volatile.Read(ref _enqueue.Value);
            }
        }
    }

    public bool TryDequeue(out T item)
    {
        if (!TryDequeueCore(out item))
            return false;

        NotifyProducers();
        return true;
    }

    private bool TryDequeueCore(out T item)
    {
        long pos = Volatile.Read(ref _dequeue.Value);
        while (true)
        {
            ref Slot slot = ref _slots[pos & _mask];
            long seq = Volatile.Read(ref slot.Sequence);
            long dif = seq - (pos + 1);

            if (dif == 0)
            {
                long seen = Interlocked.CompareExchange(ref _dequeue.Value, pos + 1, pos);
                if (seen == pos)
                {
                    item = slot.Value;
                    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                        slot.Value = default!;
                    Interlocked.Exchange(ref slot.Sequence, pos + _mask + 1);     // 다음 바퀴의 생산자에게 슬롯을 돌려줌 (위와 같은 이유로 전체 펜스)
                    return true;
                }
                pos = seen;
            }
            else if (dif < 0)
            {
                item = default!;
                return false;   // 비어 있음
            }
            else
            {
                pos = Volatile.Read(ref _dequeue.Value);
            }
        }
    }

    /// <summary>자리가 날 때까지 기다려 넣는다. 제한 시간 안에 못 넣으면 false. timeoutMs &lt; 0이면 무한 대기.</summary>
    public bool Enqueue(T item, int timeoutMs = Timeout.Infinite)
    {
        if (TryEnqueue(item))
            return true;

        if (timeoutMs == 0)
            return false;

        long deadline = timeoutMs < 0 ? long.MaxValue : TimeUtil.MonotonicNowMillis() + timeoutMs;

        for (int i = 0; i < BlockingSpinCount; ++i)
        {
            Thread.SpinWait(20);
            if (TryEnqueue(item))
                return true;
        }

        while (true)
        {
            bool done = false, timedOut = false;

            lock (_producerGate)
            {
                Interlocked.Increment(ref _producersWaiting);   // 전체 펜스: 이후의 읽기가 이 증가보다 앞서 보이지 않게 한다
                try
                {
                    // 대기 등록 뒤 한 번 더 확인해, 등록 전에 일어난 소비를 놓치지 않는다.
                    if (TryEnqueueCore(item))
                        done = true;
                    else if (!WaitSlice(_producerGate, deadline))
                    {
                        done = TryEnqueueCore(item);
                        timedOut = !done;
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _producersWaiting);
                }
            }

            if (done)
            {
                NotifyConsumers();      // 게이트를 놓은 뒤에 알린다
                return true;
            }

            if (timedOut)
                return false;

            if (TryEnqueue(item))
                return true;
        }
    }

    /// <summary>원소가 올 때까지 기다려 꺼낸다. 제한 시간 안에 없으면 false. timeoutMs &lt; 0이면 무한 대기.</summary>
    public bool Dequeue(out T item, int timeoutMs = Timeout.Infinite)
    {
        if (TryDequeue(out item))
            return true;

        if (timeoutMs == 0)
            return false;

        long deadline = timeoutMs < 0 ? long.MaxValue : TimeUtil.MonotonicNowMillis() + timeoutMs;

        for (int i = 0; i < BlockingSpinCount; ++i)
        {
            Thread.SpinWait(20);
            if (TryDequeue(out item))
                return true;
        }

        while (true)
        {
            bool done = false, timedOut = false;

            lock (_consumerGate)
            {
                Interlocked.Increment(ref _consumersWaiting);
                try
                {
                    if (TryDequeueCore(out item))
                        done = true;
                    else if (!WaitSlice(_consumerGate, deadline))
                    {
                        done = TryDequeueCore(out item);
                        timedOut = !done;
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _consumersWaiting);
                }
            }

            if (done)
            {
                NotifyProducers();
                return true;
            }

            if (timedOut)
            {
                item = default!;
                return false;
            }

            if (TryDequeue(out item))
                return true;
        }
    }

    // gate를 쥔 채 호출한다. 마감까지 남은 시간 안에서 최대 WaitSliceMs 쉰다. 마감이 지났으면 false.
    private static bool WaitSlice(object gate, long deadline)
    {
        int slice = WaitSliceMs;
        if (deadline != long.MaxValue)
        {
            long left = deadline - TimeUtil.MonotonicNowMillis();
            if (left <= 0)
                return false;
            if (left < slice)
                slice = (int)left;
        }

        Monitor.Wait(gate, slice);
        return true;
    }

    private void NotifyConsumers()
    {
        if (Volatile.Read(ref _consumersWaiting) != 0)
            lock (_consumerGate) Monitor.Pulse(_consumerGate);      // 원소 하나가 늘었으므로 소비자 한 명만 깨운다
    }

    private void NotifyProducers()
    {
        if (Volatile.Read(ref _producersWaiting) != 0)
            lock (_producerGate) Monitor.Pulse(_producerGate);
    }

    private static uint RoundUpPow2(uint v)
    {
        v--;
        v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16;
        return v + 1;
    }
}
