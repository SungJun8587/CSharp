// TokenBucket.cs : 토큰 버킷(단일 스레드용/스레드 안전).
using System;

namespace Core.Util;

/// <summary>
/// 토큰 버킷. 초당 refillPerSec개씩 채워지고 최대 capacity개까지 쌓인다(버스트 허용량).
/// 락이 없다: 한 스레드에서만 쓰거나(존 스레드) 바깥에서 동기화한다. 여러 스레드가 쓰려면 <see cref="ConcurrentTokenBucket"/>.
/// 시간은 호출자가 ms로 넘긴다(단조 시계 권장).
/// </summary>
public struct TokenBucket
{
    private double _tokens;
    private long _lastMs;
    private double _capacity;
    private double _refillPerMs;

    public TokenBucket(double capacity, double refillPerSec, long nowMs)
    {
        if (capacity <= 0 || refillPerSec < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _refillPerMs = refillPerSec / 1000.0;
        _tokens = capacity;
        _lastMs = nowMs;
    }

    public readonly double Capacity => _capacity;

    /// <summary>지금 남은 토큰(채우기 반영).</summary>
    public readonly double Peek(long nowMs)
    {
        double t = _tokens + Math.Max(0, nowMs - _lastMs) * _refillPerMs;
        return t > _capacity ? _capacity : t;
    }

    /// <summary>cost만큼 쓸 수 있으면 쓰고 true. 모자라면 아무것도 빼지 않고 false.</summary>
    public bool TryTake(long nowMs, double cost = 1.0)
    {
        if (!(cost >= 0))
            throw new ArgumentOutOfRangeException(nameof(cost));    // 음수나 NaN은 토큰을 늘리거나 판정을 망친다
        double t = Peek(nowMs);
        _lastMs = nowMs > _lastMs ? nowMs : _lastMs;
        if (t < cost)
        {
            _tokens = t;
            return false;
        }
        _tokens = t - cost;
        return true;
    }

    /// <summary>cost가 모일 때까지 걸리는 시간(ms). 지금 쓸 수 있으면 0, 영영 못 채우면(refill 0) -1.</summary>
    public readonly long WaitMs(long nowMs, double cost = 1.0)
    {
        double t = Peek(nowMs);
        if (t >= cost) return 0;
        if (_refillPerMs <= 0) return -1;
        return (long)Math.Ceiling((cost - t) / _refillPerMs);
    }
}

/// <summary>여러 스레드가 함께 쓰는 토큰 버킷(내부 락).</summary>
public sealed class ConcurrentTokenBucket
{
    private TokenBucket _bucket;
    private readonly object _lock = new object();

    public ConcurrentTokenBucket(double capacity, double refillPerSec, long nowMs) => _bucket = new TokenBucket(capacity, refillPerSec, nowMs);

    public bool TryTake(long nowMs, double cost = 1.0)
    {
        lock (_lock)
            return _bucket.TryTake(nowMs, cost);
    }

    public long WaitMs(long nowMs, double cost = 1.0)
    {
        lock (_lock)
            return _bucket.WaitMs(nowMs, cost);
    }
}
