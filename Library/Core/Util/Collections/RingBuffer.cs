// RingBuffer.cs : 고정 크기 원형 큐.
using System;
using System.Runtime.CompilerServices;

namespace Core.Util;

public enum RingFullMode
{
    /// <summary>가득 차면 새 값을 거절한다(Push가 false).</summary>
    Reject,
    /// <summary>가득 차면 가장 오래된 값을 버리고 새 값을 넣는다.</summary>
    OverwriteOldest,
}

/// <summary>
/// 고정 크기 원형 큐. 최근 N개 보관(패킷/로그/지연 기록)이나 시퀀스 윈도우에 쓴다. 할당 없이 동작하고 스레드 안전하지 않다.
/// 인덱서 0이 가장 오래된 값이다.
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _head;
    private int _count;

    public RingBuffer(int capacity, RingFullMode fullMode = RingFullMode.Reject)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
        FullMode = fullMode;
    }

    public RingFullMode FullMode { get; }
    public int Capacity => _items.Length;
    public int Count => _count;
    public bool IsFull => _count == _items.Length;

    /// <summary>넣는다. Reject 모드에서 가득 차 있으면 false.</summary>
    public bool Push(T value)
    {
        if (_count == _items.Length)
        {
            if (FullMode == RingFullMode.Reject) return false;
            _items[_head] = value;
            _head = Next(_head);
            return true;
        }
        _items[Wrap(_head + _count)] = value;
        ++_count;
        return true;
    }

    public bool TryDequeue(out T value)
    {
        if (_count == 0) { value = default!; return false; }
        value = _items[_head];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) _items[_head] = default!;
        _head = Next(_head);
        --_count;
        return true;
    }

    public bool TryPeekOldest(out T value)
    {
        if (_count == 0) { value = default!; return false; }
        value = _items[_head];
        return true;
    }

    public bool TryPeekNewest(out T value)
    {
        if (_count == 0) { value = default!; return false; }
        value = _items[Wrap(_head + _count - 1)];
        return true;
    }

    /// <summary>0이 가장 오래된 값, Count-1이 가장 최근 값.</summary>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[Wrap(_head + index)];
        }
    }

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) Array.Clear(_items);
        _head = 0;
        _count = 0;
    }

    /// <summary>오래된 값부터 dst에 복사한다. 복사한 개수를 돌려준다.</summary>
    public int CopyTo(Span<T> dst)
    {
        int n = Math.Min(dst.Length, _count);
        for (int i = 0; i < n; ++i)
            dst[i] = _items[Wrap(_head + i)];
        return n;
    }

    private int Next(int i) => ++i == _items.Length ? 0 : i;
    private int Wrap(int i) => i >= _items.Length ? i - _items.Length : i;
}
