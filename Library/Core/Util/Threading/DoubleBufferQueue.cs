// DoubleBufferQueue.cs : 더블 버퍼 방식의 MPSC 큐.
using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 여러 스레드가 쓰고(Push) 한 스레드가 틱마다 통째로 가져가는(<see cref="SwapInto"/>) 큐. I/O 스레드 → 로직 스레드 전달에 쓴다.
///
/// <para>소비자는 매 틱 자기 리스트를 건네고 채워진 리스트를 받는다. 두 리스트가 서로 오갈 뿐 원소를 복사하지 않으므로
/// 소비자의 처리 시간이 길어도 생산자는 락을 오래 기다리지 않는다(락은 참조 교환 순간에만 잡는다).</para>
/// <para>소비자는 하나여야 한다. 두 스레드가 동시에 SwapInto를 부르면 <see cref="InvalidOperationException"/>이 난다(조용히 원소를 잃는 대신 위반을 드러낸다).</para>
/// </summary>
public sealed class DoubleBufferQueue<T>
{
    private readonly object _gate = new object();
    private List<T> _write;
    private int _swapping;

    public DoubleBufferQueue(int initialCapacity = 256) => _write = new List<T>(initialCapacity);

    /// <summary>쌓인 개수(근삿값 아님: 락 안에서 읽음).</summary>
    public int Count
    {
        get { lock (_gate) return _write.Count; }
    }

    public void Push(T item)
    {
        lock (_gate)
            _write.Add(item);
    }

    public void PushBatch(IReadOnlyList<T> items)
    {
        lock (_gate)
        {
            for (int i = 0; i < items.Count; ++i)
                _write.Add(items[i]);
        }
    }

    /// <summary>
    /// 쌓인 원소가 담긴 리스트를 buffer로 받고, 호출 전의 buffer(비워서)는 새 쓰기 버퍼가 된다.
    /// 반환값은 받은 원소 수이며, buffer에는 그 원소만 들어 있다. 처리가 끝난 buffer를 다음 틱에 그대로 다시 넘기면 된다.
    /// </summary>
    public int SwapInto(ref List<T> buffer)
    {
        if (Interlocked.CompareExchange(ref _swapping, 1, 0) != 0)
            throw new InvalidOperationException("DoubleBufferQueue.SwapInto: 소비자는 하나여야 합니다 (동시 호출)");

        try
        {
            buffer.Clear();
            lock (_gate)
            {
                (_write, buffer) = (buffer, _write);
            }
            return buffer.Count;
        }
        finally
        {
            Volatile.Write(ref _swapping, 0);
        }
    }
}
