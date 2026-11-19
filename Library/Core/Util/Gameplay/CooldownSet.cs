// CooldownSet.cs : 스킬/아이템 쿨다운 관리.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 키(스킬 ID 등)별 쿨다운과 공용 쿨다운(GCD)을 관리한다. 시각은 호출자가 ms로 넘긴다(한 객체 안에서는 같은 시계).
/// 스레드 안전하지 않다(캐릭터 하나가 한 스레드의 틱에서만 쓰는 용도).
/// </summary>
public sealed class CooldownSet<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, long> _readyAt = new Dictionary<TKey, long>();
    private long _globalReadyAt = long.MinValue;

    public int Count => _readyAt.Count;

    /// <summary>키와 공용 쿨다운이 모두 끝났는가.</summary>
    public bool IsReady(TKey key, long nowMs)
        => nowMs >= _globalReadyAt && (!_readyAt.TryGetValue(key, out long t) || nowMs >= t);

    /// <summary>
    /// 쓸 수 있으면 쿨다운을 시작하고 true. 아니면 아무것도 바꾸지 않고 false.
    /// cooldownMs는 이 키의 쿨다운(0이면 키 쿨다운 없음), globalMs는 함께 시작할 공용 쿨다운(0이면 건드리지 않고, 이미 더 길게 남아 있으면 줄이지 않는다).
    /// </summary>
    public bool TryUse(TKey key, long nowMs, int cooldownMs, int globalMs = 0)
    {
        if (cooldownMs < 0 || globalMs < 0) throw new ArgumentOutOfRangeException(nameof(cooldownMs));
        if (!IsReady(key, nowMs)) return false;
        if (cooldownMs > 0) _readyAt[key] = TimeUtil.AddSaturated(nowMs, cooldownMs);
        else _readyAt.Remove(key);
        if (globalMs > 0) _globalReadyAt = Math.Max(_globalReadyAt, TimeUtil.AddSaturated(nowMs, globalMs));
        return true;
    }

    /// <summary>키 쿨다운과 공용 쿨다운 중 더 늦게 끝나는 쪽까지 남은 ms. 쓸 수 있으면 0.</summary>
    public long RemainingMs(TKey key, long nowMs)
    {
        long ready = _globalReadyAt;
        if (_readyAt.TryGetValue(key, out long t) && t > ready) ready = t;
        return ready > nowMs ? ready - nowMs : 0;
    }

    /// <summary>키 쿨다운을 ms만큼 줄인다(쿨감 효과). 줄인 결과가 지금 이전이면 쿨다운이 끝난 것으로 한다.</summary>
    public void Reduce(TKey key, long nowMs, int ms)
    {
        if (ms < 0) throw new ArgumentOutOfRangeException(nameof(ms));
        if (!_readyAt.TryGetValue(key, out long t)) return;
        t -= ms;
        if (t <= nowMs) _readyAt.Remove(key);
        else _readyAt[key] = t;
    }

    public bool Reset(TKey key) => _readyAt.Remove(key);

    public void ResetAll()
    {
        _readyAt.Clear();
        _globalReadyAt = long.MinValue;
    }

    /// <summary>끝난 쿨다운 항목을 지운다. 지운 개수를 돌려준다(가끔 호출하면 사전이 커지지 않는다).</summary>
    public int Sweep(long nowMs)
    {
        List<TKey>? dead = null;
        foreach (KeyValuePair<TKey, long> kv in _readyAt)
            if (nowMs >= kv.Value)
                (dead ??= new List<TKey>()).Add(kv.Key);
        if (dead == null) return 0;
        foreach (TKey k in dead) _readyAt.Remove(k);
        return dead.Count;
    }

}
