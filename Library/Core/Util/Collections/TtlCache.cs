// TtlCache.cs : 만료 시간이 있는 캐시.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 만료 시간이 있는 캐시(시각은 호출자가 ms로 넘긴다). 읽을 때 만료된 항목은 없는 것으로 치고 지운다.
/// 읽히지 않는 만료 항목은 <see cref="Sweep"/>으로 청소한다. <b>크기 상한이 없다</b> — 키가 계속 늘어나는 용도라면 주기적으로 Sweep하거나 용량 제한이 있는 <see cref="LruCache{TKey, TValue}"/>를 쓴다. 스레드 안전하지 않다.
/// </summary>
public sealed class TtlCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, (TValue Value, long ExpireMs)> _map;
    private readonly long _defaultTtlMs;

    public TtlCache(long defaultTtlMs, IEqualityComparer<TKey>? comparer = null)
    {
        if (defaultTtlMs < 1) throw new ArgumentOutOfRangeException(nameof(defaultTtlMs));
        _defaultTtlMs = defaultTtlMs;
        _map = new Dictionary<TKey, (TValue, long)>(comparer);
    }

    public int Count => _map.Count;

    public void Set(TKey key, TValue value, long nowMs, long ttlMs = 0) => _map[key] = (value, nowMs + (ttlMs > 0 ? ttlMs : _defaultTtlMs));

    public bool TryGet(TKey key, long nowMs, out TValue value)
    {
        if (_map.TryGetValue(key, out var e))
        {
            if (nowMs < e.ExpireMs)
            {
                value = e.Value;
                return true;
            }
            _map.Remove(key);
        }
        value = default!;
        return false;
    }

    public bool Remove(TKey key) => _map.Remove(key);

    /// <summary>만료된 항목을 모두 지우고 지운 개수를 돌려준다(O(n), 가끔만 부른다).</summary>
    public int Sweep(long nowMs)
    {
        List<TKey>? dead = null;
        foreach (KeyValuePair<TKey, (TValue Value, long ExpireMs)> kv in _map)
            if (nowMs >= kv.Value.ExpireMs)
                (dead ??= new List<TKey>()).Add(kv.Key);
        if (dead == null) return 0;
        foreach (TKey k in dead) _map.Remove(k);
        return dead.Count;
    }
}
