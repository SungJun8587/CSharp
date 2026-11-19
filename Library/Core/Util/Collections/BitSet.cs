// BitSet.cs : 고정 길이 비트 집합.
using System;
using System.Numerics;

namespace Core.Util;

/// <summary>
/// 고정 길이 비트 집합(64비트 단위 배열). 시야/방문 표시, 큰 플래그 집합, 슬롯 점유 표시에 쓴다.
/// 스레드 안전하지 않다.
/// </summary>
public sealed class BitSet : IEquatable<BitSet>
{
    private readonly ulong[] _words;

    public BitSet(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
        _words = new ulong[(length + 63) >> 6];
    }

    public int Length { get; }

    public bool Get(int index)
    {
        Check(index);
        return (_words[index >> 6] & (1UL << (index & 63))) != 0;
    }

    public void Set(int index, bool value = true)
    {
        Check(index);
        if (value) _words[index >> 6] |= 1UL << (index & 63);
        else _words[index >> 6] &= ~(1UL << (index & 63));
    }

    public void Clear(int index) => Set(index, false);

    public void Flip(int index)
    {
        Check(index);
        _words[index >> 6] ^= 1UL << (index & 63);
    }

    public void SetAll()
    {
        Array.Fill(_words, ulong.MaxValue);
        TrimTail();
    }

    public void ClearAll() => Array.Clear(_words);

    public int Count
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _words.Length; ++i) n += BitOperations.PopCount(_words[i]);
            return n;
        }
    }

    public bool Any()
    {
        for (int i = 0; i < _words.Length; ++i) if (_words[i] != 0) return true;
        return false;
    }

    public bool None() => !Any();

    /// <summary>from 이상에서 처음으로 켜진 비트 번호. 없으면 -1.</summary>
    public int NextSetBit(int from)
    {
        if (from < 0) from = 0;
        if (from >= Length) return -1;
        int w = from >> 6;
        ulong word = _words[w] & (ulong.MaxValue << (from & 63));
        while (true)
        {
            if (word != 0) return (w << 6) + BitOperations.TrailingZeroCount(word);
            if (++w >= _words.Length) return -1;
            word = _words[w];
        }
    }

    public void And(BitSet o) { SameLength(o); for (int i = 0; i < _words.Length; ++i) _words[i] &= o._words[i]; }
    public void Or(BitSet o) { SameLength(o); for (int i = 0; i < _words.Length; ++i) _words[i] |= o._words[i]; }
    public void Xor(BitSet o) { SameLength(o); for (int i = 0; i < _words.Length; ++i) _words[i] ^= o._words[i]; }
    public void AndNot(BitSet o) { SameLength(o); for (int i = 0; i < _words.Length; ++i) _words[i] &= ~o._words[i]; }

    public bool Intersects(BitSet o)
    {
        SameLength(o);
        for (int i = 0; i < _words.Length; ++i) if ((_words[i] & o._words[i]) != 0) return true;
        return false;
    }

    public void CopyFrom(BitSet o) { SameLength(o); Array.Copy(o._words, _words, _words.Length); }

    public BitSet Clone() { var c = new BitSet(Length); Array.Copy(_words, c._words, _words.Length); return c; }

    public bool Equals(BitSet? o)
    {
        if (o is null || o.Length != Length) return false;
        return _words.AsSpan().SequenceEqual(o._words);
    }

    public override bool Equals(object? obj) => Equals(obj as BitSet);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Length);
        for (int i = 0; i < _words.Length; ++i) h.Add(_words[i]);
        return h.ToHashCode();
    }

    private void Check(int index)
    {
        if ((uint)index >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(index));
    }

    private void SameLength(BitSet o)
    {
        if (o.Length != Length) throw new ArgumentException("길이가 다르다", nameof(o));
    }

    private void TrimTail()
    {
        int r = Length & 63;
        if (r != 0 && _words.Length > 0)
            _words[_words.Length - 1] &= (1UL << r) - 1;
    }
}
