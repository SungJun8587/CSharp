// SlidingWindowCounter.cs : 슬라이딩 윈도 카운터.
using System;

namespace Core.Util;

/// <summary>
/// 슬라이딩 윈도 카운터: 최근 windowMs 동안 최대 limit번. 윈도를 작은 칸으로 나눠 센다(오차는 칸 하나 크기). 락이 없고 단일 스레드용이다.
/// 토큰 버킷은 평균 속도를, 이쪽은 "1초에 N번을 넘으면 거부"처럼 창 단위 상한을 정확히 말할 때 쓴다.
/// </summary>
public sealed class SlidingWindowCounter
{
    private readonly int _limit;
    private readonly long _slotMs;
    private readonly int[] _counts;
    private long _currentSlot = long.MinValue;
    private int _total;

    public SlidingWindowCounter(int limit, long windowMs, int slots = 10)
    {
        if (limit < 1 || windowMs < 1 || slots < 1)
            throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
        _slotMs = Math.Max(1, windowMs / slots);
        _counts = new int[slots];
    }

    private void Roll(long nowMs)
    {
        long slot = nowMs / _slotMs;
        if (_currentSlot == long.MinValue)
        {
            _currentSlot = slot;
            return;
        }
        if (slot <= _currentSlot)
            return;

        long gap = slot - _currentSlot;
        if (gap >= _counts.Length)
        {
            Array.Clear(_counts);
            _total = 0;
        }
        else
        {
            for (long s = _currentSlot + 1; s <= slot; ++s)
            {
                int i = (int)(s % _counts.Length);
                _total -= _counts[i];
                _counts[i] = 0;
            }
        }
        _currentSlot = slot;
    }

    /// <summary>한 번 허용하고 true. 윈도 안 횟수가 상한이면 false(세지 않는다).</summary>
    public bool TryAcquire(long nowMs)
    {
        Roll(nowMs);
        if (_total >= _limit)
            return false;
        ++_counts[(int)(_currentSlot % _counts.Length)];
        ++_total;
        return true;
    }

    /// <summary>현재 윈도 안의 횟수.</summary>
    public int Count(long nowMs)
    {
        Roll(nowMs);
        return _total;
    }
}
