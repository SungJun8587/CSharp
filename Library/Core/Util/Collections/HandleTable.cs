// HandleTable.cs : 세대 번호가 붙은 ID로 객체를 가리키는 테이블.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>HandleTable의 항목 ID. 자리 번호와 세대 번호로 이루어져 있어, 항목이 지워지고 자리가 재사용돼도 옛 ID는 맞지 않는다.</summary>
public readonly struct HandleId : IEquatable<HandleId>
{
    public readonly int Index;
    public readonly int Generation;

    public HandleId(int index, int generation) { Index = index; Generation = generation; }

    public static readonly HandleId None = default;

    public bool IsValid => Generation != 0;

    /// <summary>패킷에 싣기 좋은 64비트 값(상위 32비트 세대, 하위 32비트 자리).</summary>
    public ulong ToUInt64() => ((ulong)(uint)Generation << 32) | (uint)Index;
    public static HandleId FromUInt64(ulong v) => new HandleId((int)(uint)v, (int)(uint)(v >> 32));

    public bool Equals(HandleId other) => Index == other.Index && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is HandleId h && Equals(h);
    public override int GetHashCode() => HashCode.Combine(Index, Generation);
    public override string ToString() => IsValid ? Index + ":" + Generation : "none";
    public static bool operator ==(HandleId a, HandleId b) => a.Equals(b);
    public static bool operator !=(HandleId a, HandleId b) => !a.Equals(b);
}

/// <summary>
/// 세대 번호 핸들 테이블. 엔티티/세션 같은 객체를 ID로만 들고 있다가, 그 객체가 사라졌는지 안전하게 알 수 있다.
/// 지워진 자리가 다른 객체로 재사용돼도 옛 ID로는 조회되지 않는다(한 자리가 20억 번 넘게 재사용돼 세대가 한 바퀴 돌기 전까지). 조회/추가/삭제는 O(1)이고 스레드 안전하지 않다.
/// </summary>
public sealed class HandleTable<T>
{
    private T[] _values;
    private int[] _gen;             // 자리별 세대(1 이상)
    private bool[] _alive;
    private int[] _nextFree;
    private int _used;
    private int _freeHead = -1;
    private readonly int _maxCount;

    public HandleTable(int capacity = 16, int maxCount = int.MaxValue)
    {
        if (capacity < 1) capacity = 1;
        if (maxCount < 1) throw new ArgumentOutOfRangeException(nameof(maxCount));
        _maxCount = maxCount;
        _values = new T[capacity];
        _gen = new int[capacity];
        _alive = new bool[capacity];
        _nextFree = new int[capacity];
    }

    public int Count { get; private set; }

    public bool TryAdd(T value, out HandleId id)
    {
        if (Count >= _maxCount) { id = HandleId.None; return false; }
        int slot;
        if (_freeHead >= 0)
        {
            slot = _freeHead;
            _freeHead = _nextFree[slot];
        }
        else
        {
            if (_used == _values.Length)
            {
                int n = _used * 2;
                Array.Resize(ref _values, n);
                Array.Resize(ref _gen, n);
                Array.Resize(ref _alive, n);
                Array.Resize(ref _nextFree, n);
            }
            slot = _used++;
            _gen[slot] = 1;
        }
        _values[slot] = value;
        _alive[slot] = true;
        ++Count;
        id = new HandleId(slot, _gen[slot]);
        return true;
    }

    public HandleId Add(T value)
        => TryAdd(value, out HandleId id) ? id : throw new InvalidOperationException("HandleTable이 가득 찼다");

    public bool Contains(HandleId id)
        => id.Generation != 0 && (uint)id.Index < (uint)_used && _alive[id.Index] && _gen[id.Index] == id.Generation;

    public bool TryGet(HandleId id, out T value)
    {
        if (!Contains(id)) { value = default!; return false; }
        value = _values[id.Index];
        return true;
    }

    public bool Set(HandleId id, T value)
    {
        if (!Contains(id)) return false;
        _values[id.Index] = value;
        return true;
    }

    public bool Remove(HandleId id) => Remove(id, out _);

    public bool Remove(HandleId id, out T value)
    {
        if (!Contains(id)) { value = default!; return false; }
        int slot = id.Index;
        value = _values[slot];
        _values[slot] = default!;
        _alive[slot] = false;
        if (++_gen[slot] <= 0) _gen[slot] = 1;
        _nextFree[slot] = _freeHead;
        _freeHead = slot;
        --Count;
        return true;
    }

    public void Clear()
    {
        for (int i = 0; i < _used; ++i)
            if (_alive[i])
                Remove(new HandleId(i, _gen[i]));
    }

    public Enumerator GetEnumerator() => new Enumerator(this);

    /// <summary>살아 있는 항목을 자리 순서로 훑는다. 훑는 중에 추가/삭제하면 결과가 정해지지 않는다.</summary>
    public struct Enumerator
    {
        private readonly HandleTable<T> _t;
        private int _i;

        internal Enumerator(HandleTable<T> t) { _t = t; _i = -1; Current = default; }

        public KeyValuePair<HandleId, T> Current { get; private set; }

        public bool MoveNext()
        {
            while (++_i < _t._used)
            {
                if (_t._alive[_i])
                {
                    Current = new KeyValuePair<HandleId, T>(new HandleId(_i, _t._gen[_i]), _t._values[_i]);
                    return true;
                }
            }
            return false;
        }
    }
}
