// WriteBehindQueue.cs : 같은 키를 합쳐 모았다가 배치로 내보내는 쓰기 지연 큐.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// DB 저장 같은 쓰기를 늦춰 모으는 큐. 같은 키에 여러 번 넣으면 하나로 합쳐지고(기본은 마지막 값, merge로 합성 가능),
/// 키가 처음 들어온 시각부터 minAge가 지난 것만 처음 들어온 순서로 꺼낸다. 자주 바뀌는 값(위치, 경험치)을 매번 저장하지 않게 한다.
/// 넣는 쪽과 꺼내는 쪽이 다른 스레드여도 된다(내부 락). merge 함수는 락 안에서 불리므로 짧고 다른 락을 잡지 않아야 한다. 시각은 단조 증가하는 ms를 넘긴다.
/// 저장이 실패하면 Requeue로 되돌린다(그사이 같은 키의 새 값이 들어왔다면 새 값이 우선이고 되돌린 값은 버려진다).
/// </summary>
public sealed class WriteBehindQueue<TKey, TValue> where TKey : notnull
{
    private sealed class Entry
    {
        public TValue Value = default!;
        public long FirstMs;
        public bool Dead;
    }

    private readonly object _lock = new object();
    private readonly Dictionary<TKey, Entry> _map = new Dictionary<TKey, Entry>();
    private Queue<KeyValuePair<TKey, Entry>> _order = new Queue<KeyValuePair<TKey, Entry>>();
    private readonly int _maxPending;
    private readonly Func<TValue, TValue, TValue>? _merge;

    /// <param name="maxPending">대기 키 수 상한. 넘으면 새 키는 거절한다(이미 있는 키의 갱신은 항상 받는다).</param>
    /// <param name="merge">(기존 값, 새 값) → 합친 값. null이면 새 값이 기존 값을 대체한다.</param>
    public WriteBehindQueue(int maxPending = int.MaxValue, Func<TValue, TValue, TValue>? merge = null)
    {
        if (maxPending < 1) throw new ArgumentOutOfRangeException(nameof(maxPending));
        _maxPending = maxPending;
        _merge = merge;
    }

    public int Count { get { lock (_lock) return _map.Count; } }

    /// <summary>넣거나 합친다. 새 키인데 가득 차 있으면 false(호출자가 직접 저장하거나 잠시 뒤 재시도).</summary>
    public bool Enqueue(TKey key, TValue value, long nowMs)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out Entry? e))
            {
                e.Value = _merge != null ? _merge(e.Value, value) : value;
                return true;
            }
            if (_map.Count >= _maxPending) return false;
            Add(key, value, nowMs);
            return true;
        }
    }

    /// <summary>대기 중인 키의 현재 값을 꺼내 보지 않고 읽는다(큐에서 빼지 않는다). 없으면 false.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out Entry? e))
            {
                value = e.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    /// <summary>대기 중인 값을 저장하지 않고 버린다.</summary>
    public bool Discard(TKey key)
    {
        lock (_lock)
        {
            if (!_map.Remove(key, out Entry? e)) return false;
            e.Dead = true;
            if (_order.Count > 2 * _map.Count + 64)
                Compact();
            return true;
        }
    }

    /// <summary>
    /// 처음 들어온 지 minAgeMs 이상 지난 항목을 오래된 순으로 최대 max개 output에 추가한다. 추가한 개수를 돌려준다.
    /// </summary>
    public int Drain(List<KeyValuePair<TKey, TValue>> output, long nowMs, long minAgeMs, int max = int.MaxValue)
    {
        lock (_lock)
            return DrainCore(output, nowMs, minAgeMs, max, false);
    }

    /// <summary>나이와 상관없이 모두(최대 max개) 꺼낸다. 종료 때 남김없이 저장할 때 쓴다.</summary>
    public int DrainAll(List<KeyValuePair<TKey, TValue>> output, int max = int.MaxValue)
    {
        lock (_lock)
            return DrainCore(output, 0, 0, max, true);
    }

    /// <summary>저장에 실패한 항목을 되돌린다. 같은 키의 더 새로운 값이 이미 있으면 false(되돌린 값은 버린다).</summary>
    public bool Requeue(TKey key, TValue value, long nowMs)
    {
        lock (_lock)
        {
            if (_map.ContainsKey(key)) return false;
            if (_map.Count >= _maxPending) return false;
            Add(key, value, nowMs);
            return true;
        }
    }

    private void Add(TKey key, TValue value, long nowMs)
    {
        var e = new Entry { Value = value, FirstMs = nowMs };
        _map.Add(key, e);
        _order.Enqueue(new KeyValuePair<TKey, Entry>(key, e));
    }

    private int DrainCore(List<KeyValuePair<TKey, TValue>> output, long nowMs, long minAgeMs, int max, bool ignoreAge)
    {
        int n = 0;
        while (n < max && _order.Count > 0)
        {
            KeyValuePair<TKey, Entry> head = _order.Peek();
            Entry e = head.Value;
            if (e.Dead) { _order.Dequeue(); continue; }
            if (!ignoreAge && nowMs - e.FirstMs < minAgeMs) break;
            _order.Dequeue();
            _map.Remove(head.Key);
            output.Add(new KeyValuePair<TKey, TValue>(head.Key, e.Value));
            ++n;
        }
        return n;
    }

    private void Compact()
    {
        var fresh = new Queue<KeyValuePair<TKey, Entry>>(_map.Count);
        foreach (KeyValuePair<TKey, Entry> kv in _order)
            if (!kv.Value.Dead)
                fresh.Enqueue(kv);
        _order = fresh;
    }
}
