// MinHeap.cs : 핸들로 갱신/삭제할 수 있는 최소 힙(우선순위 큐).
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>MinHeap 안의 한 원소를 가리키는 값. 원소가 빠지면 무효가 되고, 같은 자리가 재사용돼도 옛 핸들은 맞지 않는다.</summary>
public readonly struct HeapHandle : IEquatable<HeapHandle>
{
    internal readonly int Slot;
    internal readonly int Version;
    internal HeapHandle(int slot, int version) { Slot = slot; Version = version; }

    public bool IsValid => Version != 0;
    public bool Equals(HeapHandle other) => Slot == other.Slot && Version == other.Version;
    public override bool Equals(object? obj) => obj is HeapHandle h && Equals(h);
    public override int GetHashCode() => HashCode.Combine(Slot, Version);
}

/// <summary>
/// 최소 힙. 같은 우선순위는 넣은 순서대로 나온다(안정). Push가 돌려주는 핸들로 O(log n)에 값 갱신/삭제가 가능하다.
/// 스레드 안전하지 않다(단일 스레드용: 틱 루프, 스케줄러, A* 등).
/// </summary>
public sealed class MinHeap<T>
{
    private struct Node
    {
        public T Value;
        public long Seq;
        public int Pos;         // 힙 안 위치, 비어 있으면 -1
        public int Version;
        public int NextFree;
    }

    private readonly IComparer<T> _cmp;
    private Node[] _nodes;
    private int[] _heap;        // 힙 순서대로 슬롯 번호
    private int _count;
    private int _used;
    private int _freeHead = -1;
    private long _seq;

    public MinHeap(IComparer<T>? comparer = null, int capacity = 16)
    {
        if (capacity < 1) capacity = 1;
        _cmp = comparer ?? Comparer<T>.Default;
        _nodes = new Node[capacity];
        _heap = new int[capacity];
    }

    public int Count => _count;

    public HeapHandle Push(T value)
    {
        int slot;
        if (_freeHead >= 0)
        {
            slot = _freeHead;
            _freeHead = _nodes[slot].NextFree;
        }
        else
        {
            if (_used == _nodes.Length) Array.Resize(ref _nodes, _used * 2);
            slot = _used++;
            if (_nodes[slot].Version == 0) _nodes[slot].Version = 1;
        }
        if (_count == _heap.Length) Array.Resize(ref _heap, _count * 2);

        ref Node n = ref _nodes[slot];
        n.Value = value;
        n.Seq = _seq++;
        n.Pos = _count;
        _heap[_count++] = slot;
        SiftUp(n.Pos);
        return new HeapHandle(slot, _nodes[slot].Version);
    }

    public bool TryPeek(out T value)
    {
        if (_count == 0) { value = default!; return false; }
        value = _nodes[_heap[0]].Value;
        return true;
    }

    public bool TryPop(out T value)
    {
        if (_count == 0) { value = default!; return false; }
        value = _nodes[_heap[0]].Value;
        RemoveAt(0);
        return true;
    }

    public bool Contains(HeapHandle h) => Valid(h);

    public bool TryGet(HeapHandle h, out T value)
    {
        if (!Valid(h)) { value = default!; return false; }
        value = _nodes[h.Slot].Value;
        return true;
    }

    /// <summary>핸들의 원소를 뺀다. 이미 빠졌거나 모르는 핸들이면 false.</summary>
    public bool Remove(HeapHandle h)
    {
        if (!Valid(h)) return false;
        RemoveAt(_nodes[h.Slot].Pos);
        return true;
    }

    /// <summary>핸들의 원소 값을 바꾸고 자리를 다시 잡는다. 같은 우선순위 안의 순서는 처음 넣은 순서를 유지한다.</summary>
    public bool Update(HeapHandle h, T newValue)
    {
        if (!Valid(h)) return false;
        ref Node n = ref _nodes[h.Slot];
        n.Value = newValue;
        int pos = n.Pos;
        if (!SiftUp(pos)) SiftDown(pos);
        return true;
    }

    /// <summary>모두 비운다. 이전에 받은 핸들은 전부 무효가 된다.</summary>
    public void Clear()
    {
        while (_count > 0)
            RemoveAt(_count - 1);
    }

    private bool Valid(HeapHandle h)
        => h.Version != 0 && (uint)h.Slot < (uint)_used && _nodes[h.Slot].Version == h.Version && _nodes[h.Slot].Pos >= 0;

    private bool Less(int a, int b)
    {
        int c = _cmp.Compare(_nodes[a].Value, _nodes[b].Value);
        return c < 0 || (c == 0 && _nodes[a].Seq < _nodes[b].Seq);
    }

    private void RemoveAt(int pos)
    {
        int slot = _heap[pos];
        int last = --_count;
        if (pos != last)
        {
            int moved = _heap[last];
            _heap[pos] = moved;
            _nodes[moved].Pos = pos;
            Free(slot);
            if (!SiftUp(pos)) SiftDown(pos);
        }
        else
        {
            Free(slot);
        }
    }

    private void Free(int slot)
    {
        ref Node n = ref _nodes[slot];
        n.Value = default!;
        n.Pos = -1;
        if (++n.Version == 0) n.Version = 1;
        n.NextFree = _freeHead;
        _freeHead = slot;
    }

    private bool SiftUp(int pos)
    {
        int slot = _heap[pos];
        int start = pos;
        while (pos > 0)
        {
            int parent = (pos - 1) >> 1;
            int ps = _heap[parent];
            if (!Less(slot, ps)) break;
            _heap[pos] = ps;
            _nodes[ps].Pos = pos;
            pos = parent;
        }
        _heap[pos] = slot;
        _nodes[slot].Pos = pos;
        return pos != start;
    }

    private void SiftDown(int pos)
    {
        int slot = _heap[pos];
        while (true)
        {
            int l = 2 * pos + 1;
            if (l >= _count) break;
            int c = l;
            if (l + 1 < _count && Less(_heap[l + 1], _heap[l])) c = l + 1;
            int cs = _heap[c];
            if (!Less(cs, slot)) break;
            _heap[pos] = cs;
            _nodes[cs].Pos = pos;
            pos = c;
        }
        _heap[pos] = slot;
        _nodes[slot].Pos = pos;
    }
}
