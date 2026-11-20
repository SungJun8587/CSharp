// BuffTimerSet.cs : 버프/디버프 지속시간과 중첩 관리.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>이미 같은 버프가 걸려 있을 때 지속시간을 다루는 방식.</summary>
public enum BuffRefresh : byte
{
    /// <summary>만료를 지금 + 지속시간으로 다시 잡는다(더 짧아질 수도 있다).</summary>
    RefreshDuration,
    /// <summary>기존 만료와 새 만료 중 더 늦은 쪽을 쓴다.</summary>
    KeepLongest,
    /// <summary>기존 만료에 지속시간을 더한다.</summary>
    Extend,
    /// <summary>지속시간과 중첩을 새 값으로 통째로 바꾼다.</summary>
    Replace,
    /// <summary>이미 걸려 있으면 아무것도 바꾸지 않는다.</summary>
    Ignore,
}

public enum BuffApplyResult : byte { Added, Refreshed, Ignored }

/// <summary>걸려 있는 버프 한 건의 상태(복사본).</summary>
public readonly record struct BuffState(long AppliedAtMs, long ExpiresAtMs, int Stacks);

/// <summary>
/// 키(버프 ID)별 만료 시각과 중첩 수를 관리한다. 만료는 최소 힙으로 관리해 Tick이 만료된 것만 처리한다(전체 훑기 없음).
/// 시각은 호출자가 ms로 넘긴다. 스레드 안전하지 않다.
/// </summary>
public sealed class BuffTimerSet<TKey> where TKey : notnull
{
    private readonly struct Due
    {
        public readonly long ExpiresAtMs;
        public readonly TKey Key;
        public Due(long e, TKey k) { ExpiresAtMs = e; Key = k; }
    }

    private sealed class DueComparer : IComparer<Due>
    {
        public int Compare(Due a, Due b) => a.ExpiresAtMs.CompareTo(b.ExpiresAtMs);
    }

    private sealed class Entry
    {
        public long AppliedAtMs;
        public long ExpiresAtMs;
        public int Stacks;
        public HeapHandle Handle;
    }

    private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>();
    private readonly MinHeap<Due> _due = new MinHeap<Due>(new DueComparer());

    public int Count => _entries.Count;

    public bool Has(TKey key) => _entries.ContainsKey(key);

    public bool TryGet(TKey key, out BuffState state)
    {
        if (_entries.TryGetValue(key, out Entry? e))
        {
            state = new BuffState(e.AppliedAtMs, e.ExpiresAtMs, e.Stacks);
            return true;
        }
        state = default;
        return false;
    }

    /// <summary>다음으로 만료될 시각. 걸린 버프가 없으면 false.</summary>
    public bool TryGetNextExpiry(out long expiresAtMs)
    {
        if (_due.TryPeek(out Due d)) { expiresAtMs = d.ExpiresAtMs; return true; }
        expiresAtMs = 0;
        return false;
    }

    /// <summary>버프를 건다. 이미 있으면 refresh 방식으로 지속시간을, addStacks만큼 중첩을 더한다(maxStacks에서 멈춘다).</summary>
    public BuffApplyResult Apply(TKey key, long nowMs, long durationMs, int addStacks = 1, int maxStacks = 1, BuffRefresh refresh = BuffRefresh.RefreshDuration)
    {
        if (durationMs <= 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        if (addStacks < 1 || maxStacks < 1) throw new ArgumentOutOfRangeException(nameof(addStacks));
        long expires = TimeUtil.AddSaturated(nowMs, durationMs);

        if (!_entries.TryGetValue(key, out Entry? e))
        {
            e = new Entry { AppliedAtMs = nowMs, ExpiresAtMs = expires, Stacks = Math.Min(addStacks, maxStacks) };
            e.Handle = _due.Push(new Due(expires, key));
            _entries.Add(key, e);
            return BuffApplyResult.Added;
        }

        if (refresh == BuffRefresh.Ignore)
            return BuffApplyResult.Ignored;

        long newExpires;
        switch (refresh)
        {
            case BuffRefresh.KeepLongest: newExpires = Math.Max(e.ExpiresAtMs, expires); break;
            case BuffRefresh.Extend: newExpires = TimeUtil.AddSaturated(e.ExpiresAtMs, durationMs); break;
            default: newExpires = expires; break;           // RefreshDuration, Replace
        }
        e.Stacks = refresh == BuffRefresh.Replace ? Math.Min(addStacks, maxStacks) : (int)Math.Min((long)e.Stacks + addStacks, maxStacks);
        if (refresh == BuffRefresh.Replace) e.AppliedAtMs = nowMs;
        if (newExpires != e.ExpiresAtMs)
        {
            e.ExpiresAtMs = newExpires;
            _due.Update(e.Handle, new Due(newExpires, key));
        }
        return BuffApplyResult.Refreshed;
    }

    /// <summary>중첩을 줄인다. 0이 되면 버프를 지우고 true를 돌려준다(만료 콜백은 부르지 않는다).</summary>
    public bool RemoveStacks(TKey key, int stacks, out bool removed)
    {
        removed = false;
        if (stacks < 0) throw new ArgumentOutOfRangeException(nameof(stacks));
        if (!_entries.TryGetValue(key, out Entry? e)) return false;
        e.Stacks -= stacks;
        if (e.Stacks <= 0)
        {
            Remove(key);
            removed = true;
        }
        return true;
    }

    public bool Remove(TKey key)
    {
        if (!_entries.Remove(key, out Entry? e)) return false;
        _due.Remove(e.Handle);
        return true;
    }

    /// <summary>만료 시각이 nowMs 이하인 버프를 만료 순서로 지우며 콜백(키, 마지막 중첩 수)을 부른다. 처리한 개수를 돌려준다.</summary>
    public int Tick(long nowMs, Action<TKey, int>? onExpire = null)
    {
        int n = 0;
        while (_due.TryPeek(out Due d) && d.ExpiresAtMs <= nowMs)
        {
            _due.TryPop(out _);
            Entry e = _entries[d.Key];
            _entries.Remove(d.Key);
            ++n;
            onExpire?.Invoke(d.Key, e.Stacks);          // 콜백이 같은 키를 다시 걸어도 안전하다
        }
        return n;
    }

    public void Clear()
    {
        _entries.Clear();
        _due.Clear();
    }
}
