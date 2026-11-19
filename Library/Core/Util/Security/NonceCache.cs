// NonceCache.cs : 토큰 재사용 방지용 nonce 기억.
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Util;

/// <summary>
/// 쓴 nonce를 만료 시각까지 기억해 토큰 재사용을 막는다. 스레드 안전하다. 만료된 항목은 호출 중에 가끔 청소한다.
/// 서버를 여러 대 두면 서버마다 따로 기억하므로, 서버 사이의 재사용까지 막으려면 Redis 등 공유 저장소가 필요하다.
/// </summary>
public sealed class NonceCache
{
    private readonly object _lock = new object();
    private readonly Dictionary<string, long> _used = new Dictionary<string, long>(StringComparer.Ordinal);
    private readonly int _maxEntries;
    private long _nextSweepMs;
    private long _lastSweepMs = long.MinValue / 2;

    public NonceCache(int maxEntries = 100_000) => _maxEntries = Math.Max(16, maxEntries);

    public int Count { get { lock (_lock) return _used.Count; } }

    /// <summary>처음 보는 nonce면 기록하고 true, 이미 썼으면 false. expireAtMs가 지나면 기록은 지워진다.</summary>
    public bool TryUse(string nonce, long expireAtMs, long nowMs)
    {
        lock (_lock)
        {
            // 가득 찼을 때 호출마다 O(n) 청소를 하지 않도록 청소 간격은 최소 1초.
            if (nowMs >= _nextSweepMs || (_used.Count >= _maxEntries && nowMs - _lastSweepMs >= 1000))
                Sweep(nowMs);
            if (_used.Count >= _maxEntries)
                return false;               // 가득 차면 안전한 쪽(거절)으로
            return _used.TryAdd(nonce, expireAtMs);
        }
    }

    private void Sweep(long nowMs)
    {
        List<string>? dead = null;
        foreach (KeyValuePair<string, long> kv in _used)
            if (nowMs >= kv.Value)
                (dead ??= new List<string>()).Add(kv.Key);
        if (dead != null)
            foreach (string k in dead)
                _used.Remove(k);
        _lastSweepMs = nowMs;
        _nextSweepMs = nowMs + 10_000;
    }
}
