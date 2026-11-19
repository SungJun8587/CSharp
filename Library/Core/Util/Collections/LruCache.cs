// LruCache.cs : 용량 제한 LRU 캐시.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 가장 오래 안 쓴 항목부터 밀어내는 용량 제한 캐시. O(1). 스레드 안전하지 않다(바깥에서 동기화하거나 한 스레드에서만 쓴다).
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private sealed class Node
    {
        public TKey Key = default!;
        public TValue Value = default!;
        public Node? Prev, Next;
    }

    private readonly Dictionary<TKey, Node> _map;
    private readonly int _capacity;
    private Node? _head;        // 가장 최근
    private Node? _tail;        // 가장 오래됨

    /// <summary>항목이 밀려날 때 불린다(용량 초과 제거에만, Remove/Clear에는 불리지 않는다).</summary>
    public Action<TKey, TValue>? OnEvicted;

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _map = new Dictionary<TKey, Node>(Math.Min(capacity, 1024), comparer);
    }

    public int Count => _map.Count;
    public int Capacity => _capacity;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out Node? n))
        {
            MoveToFront(n);
            value = n.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>값을 읽지 않고 있는지만 본다(최근 사용 순서를 바꾸지 않는다).</summary>
    public bool Contains(TKey key) => _map.ContainsKey(key);

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out Node? n))
        {
            n.Value = value;
            MoveToFront(n);
            return;
        }
        n = new Node { Key = key, Value = value };
        _map.Add(key, n);
        PushFront(n);
        if (_map.Count > _capacity)
        {
            Node old = _tail!;
            Unlink(old);
            _map.Remove(old.Key);
            OnEvicted?.Invoke(old.Key, old.Value);
        }
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        if (TryGet(key, out TValue v))
            return v;
        v = factory(key);
        Set(key, v);
        return v;
    }

    public bool Remove(TKey key)
    {
        if (!_map.TryGetValue(key, out Node? n))
            return false;
        Unlink(n);
        _map.Remove(key);
        return true;
    }

    public void Clear()
    {
        _map.Clear();
        _head = _tail = null;
    }

    private void MoveToFront(Node n)
    {
        if (_head == n) return;
        Unlink(n);
        PushFront(n);
    }

    private void PushFront(Node n)
    {
        n.Prev = null;
        n.Next = _head;
        if (_head != null) _head.Prev = n;
        _head = n;
        _tail ??= n;
    }

    private void Unlink(Node n)
    {
        if (n.Prev != null) n.Prev.Next = n.Next; else _head = n.Next;
        if (n.Next != null) n.Next.Prev = n.Prev; else _tail = n.Prev;
        n.Prev = n.Next = null;
    }
}
