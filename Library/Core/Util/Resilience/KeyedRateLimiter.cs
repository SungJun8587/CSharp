// KeyedRateLimiter.cs : 키별 토큰 버킷 제한기.
using System;
using System.Collections.Concurrent;

namespace Core.Util;

/// <summary>
/// 키(캐릭터 ID, IP 등)마다 따로 토큰 버킷을 두는 제한기. 스레드 안전하다. 오래 쓰지 않은 키는 <see cref="Sweep"/>으로 걷어 낸다.
/// </summary>
public sealed class KeyedRateLimiter<TKey> where TKey : notnull
{
    private sealed class Entry
    {
        public readonly object Lock = new object();
        public TokenBucket Bucket;
        public long LastUsedMs;
    }

    private readonly ConcurrentDictionary<TKey, Entry> _entries = new ConcurrentDictionary<TKey, Entry>();
    private readonly double _capacity;
    private readonly double _refillPerSec;

    public KeyedRateLimiter(double capacity, double refillPerSec)
    {
        if (capacity <= 0 || refillPerSec < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _refillPerSec = refillPerSec;
    }

    public int KeyCount => _entries.Count;

    public bool TryTake(TKey key, long nowMs, double cost = 1.0)
    {
        // 이미 있는 키는 람다/할당 없이 찾는다(핫패스). 없을 때만 만든다.
        if (!_entries.TryGetValue(key, out Entry? e))
            e = _entries.GetOrAdd(key, static (_, st) => new Entry { Bucket = new TokenBucket(st.cap, st.refill, st.now), LastUsedMs = st.now }, (cap: _capacity, refill: _refillPerSec, now: nowMs));
        lock (e.Lock)
        {
            e.LastUsedMs = nowMs;
            return e.Bucket.TryTake(nowMs, cost);
        }
    }

    /// <summary>idleMs 넘게 쓰지 않은 키를 지운다(지운 키는 다음에 가득 찬 버킷으로 다시 시작한다). 지운 개수를 돌려준다.</summary>
    public int Sweep(long nowMs, long idleMs)
    {
        int removed = 0;
        foreach (System.Collections.Generic.KeyValuePair<TKey, Entry> kv in _entries)
        {
            bool idle;
            lock (kv.Value.Lock)
                idle = nowMs - kv.Value.LastUsedMs >= idleMs;
            if (idle && _entries.TryRemove(kv.Key, out _))
                ++removed;
        }
        return removed;
    }
}
