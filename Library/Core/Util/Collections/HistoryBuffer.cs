// HistoryBuffer.cs : 일련번호(틱 번호)로 찾는 최근 값 보관 버퍼.
using System;

namespace Core.Util;

/// <summary>
/// 일련번호 seq(0 이상)마다 값을 하나씩 저장하고, 가장 최근 Capacity개만 번호로 다시 찾는다. 틱마다 위치/상태 스냅샷을 쌓아 두고
/// "N틱 전 상태"를 찾는 렉 보상이나 롤백에 쓴다. 용량은 2의 거듭제곱으로 올림한다.
/// <para>
/// 슬롯마다 저장된 번호를 함께 기억하므로, 아직 쓰지 않은 번호나 이미 덮어써진 옛 번호를 찾으면 오래된 값을 잘못 돌려주지 않고 실패한다.
/// 번호가 건너뛰어 쓰여도 된다. 단일 스레드용이다(락 없음).
/// </para>
/// </summary>
public sealed class HistoryBuffer<T>
{
    private readonly T[] _items;
    private readonly long[] _seqs;
    private readonly int _mask;
    private long _newest = -1;

    public HistoryBuffer(int minCapacity)
    {
        if (minCapacity < 1) throw new ArgumentOutOfRangeException(nameof(minCapacity));
        int cap = BitUtil.NextPowerOfTwo(minCapacity);
        _items = new T[cap];
        _seqs = new long[cap];
        Array.Fill(_seqs, -1L);
        _mask = cap - 1;
    }

    /// <summary>보관하는 번호 개수(2의 거듭제곱).</summary>
    public int Capacity => _items.Length;

    /// <summary>지금까지 쓴 가장 큰 번호. 아무것도 쓰지 않았으면 -1.</summary>
    public long Newest => _newest;

    /// <summary>번호 seq에 값을 쓴다. 이미 덮어써질 만큼 오래된 번호(Newest - Capacity 이하)면 쓰지 않고 false.</summary>
    public bool Set(long seq, T value)
    {
        if (seq < 0) throw new ArgumentOutOfRangeException(nameof(seq));
        if (_newest >= 0 && seq <= _newest - _items.Length) return false;

        int i = (int)(seq & _mask);
        _items[i] = value;
        _seqs[i] = seq;
        if (seq > _newest) _newest = seq;
        return true;
    }

    /// <summary>번호 seq의 값을 찾는다. 쓰지 않았거나 덮어써졌으면 false.</summary>
    public bool TryGet(long seq, out T value)
    {
        if (seq >= 0 && seq > _newest - _items.Length)      // 최근 Capacity개 창 안만 보인다(건너뛰어 쓴 뒤 슬롯에 남은 옛 값은 숨긴다)
        {
            int i = (int)(seq & _mask);
            if (_seqs[i] == seq)
            {
                value = _items[i];
                return true;
            }
        }
        value = default!;
        return false;
    }

    public bool Contains(long seq) => TryGet(seq, out _);

    /// <summary>seq 이하에서 보관 중인 가장 가까운(큰) 번호의 값을 찾는다. 번호가 건너뛰어 쓰였을 때 "그 시점의 상태"를 구하는 데 쓴다. 최악의 경우 Capacity번 본다.</summary>
    public bool TryGetAtOrBefore(long seq, out long foundSeq, out T value)
    {
        long from = Math.Min(seq, _newest);
        long to = Math.Max(0, _newest - _items.Length + 1);
        for (long s = from; s >= to; --s)
        {
            if (TryGet(s, out value))
            {
                foundSeq = s;
                return true;
            }
        }
        foundSeq = -1;
        value = default!;
        return false;
    }

    /// <summary>모두 비운다(참조 형식 값도 놓아 준다).</summary>
    public void Clear()
    {
        Array.Clear(_items);
        Array.Fill(_seqs, -1L);
        _newest = -1;
    }
}
