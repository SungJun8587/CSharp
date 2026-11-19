// NamedTimers.cs : implementation of the NamedTimers class.
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 이름으로 타이머를 관리하는 얇은 래퍼. <see cref="TimerWheel"/> 위에서 이름 → 타이머 매핑만 더한다.
/// "버프 만료", "리스폰 대기", "세션 유예 종료"처럼 같은 종류의 타이머를 이름으로 찾고, 있는지 보고, 남은 시간을 읽고, 다시 걸 때 쓴다.
/// <para>같은 이름으로 다시 만들면 기존 타이머를 취소하고 교체한다. 한 번짜리 타이머는 콜백을 부르기 전에 목록에서 빠지므로 콜백 안에서
/// 같은 이름으로 다시 걸어도 된다. 콜백 예외는 이름과 함께 onError 로 보고하고 삼키므로 다른 타이머에 영향이 없다.</para>
/// <para>시간은 ms 이며, 생성 시 startMs 와 <see cref="Advance"/> 의 nowMs 는 같은 시간축이어야 한다.
/// 만든 시각의 기준은 <see cref="CurrentMs"/>(마지막으로 Advance 한 시각, 틱 단위로 내림)이다.
/// 락이 없으므로 TimerWheel 처럼 한 스레드에서만 쓴다.</para>
/// </summary>
/// <typeparam name="TKey">타이머 이름. 문자열, enum, 구조체 키 등.</typeparam>
public sealed class NamedTimers<TKey> where TKey : notnull
{
    private sealed class Entry
    {
        public TimerHandle Handle;
        public long FirstEndMs;     // 첫 만료 시각
        public long IntervalMs;     // 0 이면 한 번짜리, 아니면 틱 단위로 올림한 반복 간격
    }

    private readonly TimerWheel _wheel;
    private readonly long _tickMs;
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly Action<TKey, Exception>? _onError;

    /// <param name="startMs">시작 시각(ms).</param>
    /// <param name="tickMs">타이머 해상도(ms). 요청한 시간보다 일찍 실행되지는 않는다.</param>
    /// <param name="onError">콜백 예외 보고처. null 이면 LogThrottle 로 남긴다.</param>
    /// <param name="comparer">이름 비교자. 문자열 키를 대소문자 무시로 쓰려면 지정한다.</param>
    public NamedTimers(long startMs, int tickMs = 10, Action<TKey, Exception>? onError = null, IEqualityComparer<TKey>? comparer = null)
    {
        _wheel = new TimerWheel(startMs, tickMs, ex => { });     // 콜백은 아래에서 직접 감싸므로 휠의 보고는 쓰지 않는다
        _tickMs = tickMs;
        _onError = onError;
        _entries = new Dictionary<TKey, Entry>(comparer);
    }

    /// <summary>지금까지 처리한 시각(ms).</summary>
    public long CurrentMs => _wheel.CurrentMs;

    /// <summary>살아 있는 타이머 수.</summary>
    public int Count => _entries.Count;

    /// <summary>delayMs 뒤에 한 번 실행한다. 같은 이름이 있으면 교체한다.</summary>
    public void Set(TKey name, long delayMs, Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        Cancel(name);
        long delayTicks = delayMs <= 0 ? 1 : Ceil(delayMs);
        var entry = new Entry { FirstEndMs = _wheel.CurrentMs + delayTicks * _tickMs };
        entry.Handle = _wheel.Schedule(delayMs, () =>
        {
            // 한 번짜리는 실행 전에 목록에서 뺀다. 그 사이 같은 이름이 교체됐다면(다른 Entry) 건드리지 않는다.
            if (_entries.TryGetValue(name, out Entry? cur) && ReferenceEquals(cur, entry))
                _entries.Remove(name);
            Run(name, callback);
        });
        _entries[name] = entry;
    }

    /// <summary>intervalMs 마다 반복 실행한다(첫 실행도 intervalMs 뒤). 같은 이름이 있으면 교체한다. <see cref="Cancel"/> 로 멈춘다.</summary>
    public void SetRepeating(TKey name, long intervalMs, Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (intervalMs < 1) throw new ArgumentOutOfRangeException(nameof(intervalMs));

        Cancel(name);
        long ticks = Ceil(intervalMs);
        var entry = new Entry { FirstEndMs = _wheel.CurrentMs + ticks * _tickMs, IntervalMs = ticks * _tickMs };
        entry.Handle = _wheel.ScheduleRepeating(intervalMs, () => Run(name, callback));
        _entries[name] = entry;
    }

    /// <summary>타이머를 취소한다. 있었으면 true.</summary>
    public bool Cancel(TKey name)
    {
        if (!_entries.Remove(name, out Entry? entry))
            return false;
        _wheel.Cancel(entry.Handle);
        return true;
    }

    /// <summary>모든 타이머를 취소한다.</summary>
    public void Clear()
    {
        foreach (Entry entry in _entries.Values)
            _wheel.Cancel(entry.Handle);
        _entries.Clear();
    }

    /// <summary>이름의 타이머가 살아 있는지(아직 실행 전이거나 반복 중인지).</summary>
    public bool Exist(TKey name) => _entries.ContainsKey(name);

    /// <summary>다음 만료 시각(ms). 반복 타이머는 CurrentMs 이후의 다음 만료 시각이다. 없으면 false.</summary>
    public bool TryGetEndMs(TKey name, out long endMs)
    {
        if (!_entries.TryGetValue(name, out Entry? entry))
        {
            endMs = 0;
            return false;
        }

        endMs = entry.FirstEndMs;
        long now = _wheel.CurrentMs;
        if (entry.IntervalMs != 0 && endMs <= now)
            endMs += ((now - endMs) / entry.IntervalMs + 1) * entry.IntervalMs;
        return true;
    }

    /// <summary>다음 만료 시각(ms). 없으면 0.</summary>
    public long EndMs(TKey name) => TryGetEndMs(name, out long end) ? end : 0;

    /// <summary>CurrentMs 기준 남은 시간(ms). 없거나 이미 지났으면 0.</summary>
    public long RemainingMs(TKey name) => RemainingMs(name, _wheel.CurrentMs);

    /// <summary>nowMs 기준 남은 시간(ms). 마지막 Advance 이후 시각을 정확히 반영하고 싶을 때 쓴다. 없거나 이미 지났으면 0.</summary>
    public long RemainingMs(TKey name, long nowMs)
        => TryGetEndMs(name, out long end) ? Math.Max(0, end - nowMs) : 0;

    /// <summary>시각을 nowMs 까지 진행하고 만료된 타이머를 실행한다.</summary>
    /// <returns>실행한 콜백 수</returns>
    public int Advance(long nowMs) => _wheel.Advance(nowMs);

    private long Ceil(long ms) => ms / _tickMs + (ms % _tickMs != 0 ? 1 : 0);

    private void Run(TKey name, Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            try
            {
                if (_onError != null) _onError(name, ex);
                else LogThrottle.Error("namedtimers.callback-failed", 5000, "[NamedTimers] '{0}' callback failed: {1}", name, ex);
            }
            catch (Exception)
            {
                // 보고 실패가 휠의 실행 목록을 끊지 않게 삼킨다.
            }
        }
    }
}
