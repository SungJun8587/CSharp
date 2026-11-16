// TimerHeap.cs : implementation of the TimerHeap class.
using System;
using System.Collections.Generic;

namespace Game.ZoneCore;

/// <summary>
/// 지연 작업 힙. 만료 시각이 이른 순으로, 같은 시각이면 예약한 순서(ID 순)로 꺼낸다. 취소는 지연 삭제(lazy)이며 O(1)이다.
/// 스레드 안전하지 않다. 존 스레드 전용이다.
/// </summary>
public sealed class TimerHeap<T> where T : class
{
    private readonly PriorityQueue<ulong, (ulong Due, ulong Id)> _heap = new PriorityQueue<ulong, (ulong Due, ulong Id)>();
    private readonly Dictionary<ulong, (ulong Due, T Job)> _live = new Dictionary<ulong, (ulong Due, T Job)>();
    private ulong _nextId = 1;

    /// <summary>실행 대기 중인(취소되지 않은) 타이머 수.</summary>
    public int Count => _live.Count;

    /// <summary>작업을 예약한다.</summary>
    /// <param name="dueMs">만료 시각(논리 시간, ms)</param>
    /// <returns>타이머 ID (1 이상)</returns>
    public ulong Schedule(ulong dueMs, T job)
    {
        ulong id = _nextId++;
        _live.Add(id, (dueMs, job));
        _heap.Enqueue(id, (dueMs, id));
        return id;
    }

    /// <summary>아직 실행되지 않은 타이머를 취소한다.</summary>
    public bool Cancel(ulong id)
    {
        if (!_live.Remove(id))
            return false;

        // 취소는 힙에서 바로 빼지 않으므로(지연 삭제), 오래 걸린 타이머를 자주 취소하면 죽은 항목이 쌓인다. 살아 있는 수보다 많이 쌓이면 한 번에 정리한다.
        if (_heap.Count > CompactMinHeapSize && _heap.Count > 4 * _live.Count)
            Compact();
        return true;
    }

    private const int CompactMinHeapSize = 1024;

    /// <summary>힙을 살아 있는 타이머만으로 다시 만든다. 순서 키(만료 시각, ID)는 그대로라 실행 순서는 바뀌지 않는다.</summary>
    private void Compact()
    {
        _heap.Clear();
        foreach (KeyValuePair<ulong, (ulong Due, T Job)> kv in _live)
            _heap.Enqueue(kv.Key, (kv.Value.Due, kv.Key));
    }

    /// <summary>만료 시각이 nowMs 이하인 타이머를 순서대로 실행한다. 실행 중 예약된 타이머는 이번 호출에서 실행하지 않는다(만료가 더 늦은 경우).</summary>
    public void RunDue(ulong nowMs, Action<T> run)
    {
        while (_heap.TryPeek(out ulong id, out (ulong Due, ulong Id) key) && key.Due <= nowMs)
        {
            _heap.Dequeue();
            if (_live.Remove(id, out (ulong Due, T Job) entry))
                run(entry.Job);
        }
    }

    /// <summary>모든 타이머를 버린다.</summary>
    public void Clear()
    {
        _heap.Clear();
        _live.Clear();
    }
}
