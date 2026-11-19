// TimerWheel.cs : 계층형 타이머 휠(예약 O(1), 취소 O(1), 만료 처리는 지난 틱 수에 비례).
using System;

namespace Core.Util;

/// <summary>타이머 취소에 쓰는 핸들. 이미 끝났거나 취소된 타이머의 핸들로 취소해도 안전하다(아무 일도 없다).</summary>
public readonly struct TimerHandle : IEquatable<TimerHandle>
{
    internal readonly int Index;
    internal readonly uint Version;
    internal TimerHandle(int index, uint version) { Index = index; Version = version; }

    public bool IsValid => Version != 0;
    public bool Equals(TimerHandle other) => Index == other.Index && Version == other.Version;
    public override bool Equals(object? obj) => obj is TimerHandle h && Equals(h);
    public override int GetHashCode() => HashCode.Combine(Index, Version);
}

/// <summary>
/// 한 스레드(존 스레드 같은 단일 소비자)가 쓰는 타이머 휠. 락이 없으므로 <see cref="Schedule"/>/<see cref="Cancel"/>/<see cref="Advance"/>를 같은 스레드에서 부른다.
/// <para>시간은 밀리초 정수를 호출자가 넘긴다(<see cref="Advance"/>의 nowMs). 정해 둔 틱 간격(기본 10ms)으로 올림하므로 <b>요청한 시간보다 일찍 실행되지 않는다</b>.
/// 콜백 안에서 새 타이머를 예약하거나 다른 타이머를 취소해도 된다(방금 예약한 것은 최소 한 틱 뒤에 실행된다).</para>
/// <para>6단 64칸 휠이라 틱 간격이 10ms면 약 20년까지, 그 이상은 가장 먼 칸에 걸어 두고 도착하면 다시 건다.</para>
/// </summary>
public sealed class TimerWheel
{
    private const int LevelBits = 6;
    private const int SlotsPerLevel = 1 << LevelBits;
    private const int SlotMask = SlotsPerLevel - 1;
    private const int Levels = 6;
    private const long MaxDelayTicks = 1L << 50;
    private const long MaxDelta = (1L << (LevelBits * Levels)) - 1;

    private readonly long _tickMs;
    private readonly Action<Exception>? _onError;
    private readonly int[] _heads = new int[Levels * SlotsPerLevel];

    // 노드 저장(배열 + 자유 목록). 인덱스 0은 쓰지 않는다.
    private long[] _due = new long[16];
    private long[] _intervalTicks = new long[16];
    private int[] _next = new int[16];
    private int[] _prev = new int[16];
    private int[] _bucket = new int[16];        // 들어 있는 칸(-1: 대기 목록에 없음, -2: 실행 중)
    private uint[] _version = new uint[16];
    private Action?[] _callback = new Action?[16];
    private int _freeHead;
    private int _nodeCount = 1;

    private long _currentTick;                  // 지금까지 처리한 틱(시작 시각 기준)
    private readonly long _startMs;
    private int _active;

    private long _nextJumpCheckTick;
    private bool _advancing;
    private long _advanceTarget;
    private int _firingHead = -1;               // 지금 실행 중인 칸에서 떼어 낸 연결 목록

    public TimerWheel(long startMs, int tickMs = 10, Action<Exception>? onError = null)
    {
        if (tickMs < 1)
            throw new ArgumentOutOfRangeException(nameof(tickMs));
        _startMs = startMs;
        _tickMs = tickMs;
        _onError = onError;
        Array.Fill(_heads, -1);
    }

    /// <summary>아직 실행되지 않았고 취소되지도 않은 타이머 수.</summary>
    public int Count => _active;

    /// <summary>지금까지 처리한 시각(밀리초). 이 시각 이전으로 예약해도 다음 틱에 실행된다.</summary>
    public long CurrentMs => _startMs + _currentTick * _tickMs;

    /// <summary>delayMs 뒤에 한 번 실행한다(0 이하는 다음 틱).</summary>
    public TimerHandle Schedule(long delayMs, Action callback) => Add(delayMs, 0, callback);

    /// <summary>intervalMs마다 반복 실행한다. 첫 실행도 intervalMs 뒤다. <see cref="Advance"/>가 오래 쉬었다 불려도 밀린 횟수를 몰아 실행하지 않고 위상을 유지한 채 다음 간격으로 건너뛴다.</summary>
    public TimerHandle ScheduleRepeating(long intervalMs, Action callback)
    {
        if (intervalMs < 1)
            throw new ArgumentOutOfRangeException(nameof(intervalMs));
        return Add(intervalMs, intervalMs, callback);
    }

    private TimerHandle Add(long delayMs, long intervalMs, Action callback)
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        // 올림 계산에서 넘치지 않도록 나눗셈으로 하고, 비현실적으로 먼 시각은 상한으로 자른다.
        long delayTicks = delayMs <= 0 ? 1 : delayMs / _tickMs + (delayMs % _tickMs != 0 ? 1 : 0);
        if (delayTicks > MaxDelayTicks)
            delayTicks = MaxDelayTicks;
        int n = AllocNode();
        _callback[n] = callback;
        _due[n] = _currentTick + delayTicks;
        _intervalTicks[n] = intervalMs <= 0 ? 0 : Math.Min(MaxDelayTicks, Math.Max(1, intervalMs / _tickMs + (intervalMs % _tickMs != 0 ? 1 : 0)));
        Insert(n);
        ++_active;
        return new TimerHandle(n, _version[n]);
    }

    /// <summary>타이머를 취소한다. 취소했으면 true, 이미 실행됐거나 취소된 것이면 false.</summary>
    public bool Cancel(TimerHandle handle)
    {
        int n = handle.Index;
        if (!handle.IsValid || n <= 0 || n >= _nodeCount || _version[n] != handle.Version || _callback[n] == null)
            return false;

        --_active;
        if (_bucket[n] >= 0)
        {
            Unlink(n);
            FreeNode(n);
        }
        else
        {
            // 실행 대기/실행 중인 목록 안에 있다: 목록에서 빼지 않고 콜백만 비워 두면 순회가 건너뛰고 거기서 해제한다.
            _callback[n] = null;
        }
        return true;
    }

    /// <summary>
    /// 시각을 nowMs까지 진행하고 기한이 된 타이머를 실행한다. 실행한 개수를 돌려준다. 콜백 안에서 다시 부르면 예외.
    /// 오래 쉰 뒤 부르면(약 4096틱 이상) 빈 구간은 틱을 하나씩 돌지 않고 다음 만료 시점으로 건너뛴다.
    /// </summary>
    public int Advance(long nowMs)
    {
        if (_advancing)
            throw new InvalidOperationException("TimerWheel.Advance를 콜백 안에서 다시 부를 수 없다");

        long target = (nowMs - _startMs) / _tickMs;
        int fired = 0;
        if (target <= _currentTick)
            return 0;

        _advancing = true;
        _advanceTarget = target;
        try
        {
            while (_currentTick < target)
            {
                if (_active == 0)
                {
                    _currentTick = target;      // 걸린 게 없으면 틱을 하나씩 돌 필요가 없다
                    break;
                }
                if (target - _currentTick > JumpThresholdTicks && _currentTick >= _nextJumpCheckTick)
                    JumpToNextDue(target);

                ++_currentTick;
                Cascade();
                fired += FireSlot((int)(_currentTick & SlotMask));
            }
        }
        finally
        {
            _advancing = false;
        }
        return fired;
    }

    private const long JumpThresholdTicks = 4096;

    // 가장 이른 만료 직전까지 현재 틱을 옮기고, 바뀐 기준에 맞게 대기 중인 타이머를 다시 배치한다(윗 단 칸의 캐스케이드 지점을 건너뛰므로).
    private void JumpToNextDue(long target)
    {
        long minDue = long.MaxValue;
        for (int n = 1; n < _nodeCount; ++n)
            if (_bucket[n] >= 0 && _callback[n] != null && _due[n] < minDue)
                minDue = _due[n];
        if (minDue == long.MaxValue)
            return;

        long jumpTo = Math.Min(target - 1, minDue - 1);     // 루프가 곧 한 틱 더 나아가므로 target을 넘지 않게 target-1까지
        if (jumpTo - _currentTick < 64)
        {
            _nextJumpCheckTick = _currentTick + 256;     // 얻을 게 적으면 한동안 노드 전체 훑기를 하지 않는다(촘촘한 타이머에서 틱마다 훑는 것을 막는다)
            if (jumpTo <= _currentTick)
                return;
        }
        _currentTick = jumpTo;
        for (int n = 1; n < _nodeCount; ++n)
        {
            if (_bucket[n] >= 0 && _callback[n] != null)
            {
                Unlink(n);
                Insert(n);
            }
        }
    }

    // 현재 틱이 64^k의 배수를 지날 때마다 윗 단의 칸을 아랫 단으로 내려 재배치한다.
    private void Cascade()
    {
        long t = _currentTick;
        for (int level = 1; level < Levels; ++level)
        {
            if ((t & ((1L << (LevelBits * level)) - 1)) != 0)
                break;
            int slot = (int)((t >> (LevelBits * level)) & SlotMask);
            int head = _heads[level * SlotsPerLevel + slot];
            _heads[level * SlotsPerLevel + slot] = -1;
            while (head != -1)
            {
                int n = head;
                head = _next[n];
                _bucket[n] = -1;
                Insert(n);
            }
        }
    }

    private int FireSlot(int slot)
    {
        int head = _heads[slot];
        if (head == -1)
            return 0;
        _heads[slot] = -1;

        // 칸에서 뗀 목록을 대기 목록으로 만든다. 순회 중 취소는 콜백을 비워 표시한다.
        for (int n = head; n != -1; n = _next[n])
            _bucket[n] = -2;
        _firingHead = head;

        int fired = 0;
        while (_firingHead != -1)
        {
            int n = _firingHead;
            _firingHead = _next[n];
            Action? cb = _callback[n];
            if (cb == null)
            {
                FreeNode(n);            // 대기 중 취소됨
                continue;
            }

            long interval = _intervalTicks[n];
            if (interval == 0)
            {
                _callback[n] = null;    // 한 번짜리: 실행 전에 비워 두면 콜백 안에서 자기 핸들로 취소해도 false가 된다
                --_active;
                FreeNode(n);
            }

            ++fired;
            try
            {
                cb();
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }


            if (interval != 0 && _callback[n] != null && _bucket[n] == -2)
            {
                // 위상을 유지하며 다음 간격으로. Advance가 오래 쉬다 한꺼번에 따라잡는 중이면 밀린 횟수는 건너뛴다.
                long next = _due[n] + interval;
                if (next <= _advanceTarget)
                    next += ((_advanceTarget - next) / interval + 1) * interval;
                _due[n] = Math.Max(next, _currentTick + 1);
                Insert(n);
            }
            else if (interval != 0 && _callback[n] == null)
            {
                FreeNode(n);            // 콜백 안에서 취소됨
            }
        }
        return fired;
    }

    // 오류 처리기가 다시 예외를 던져도 대기 중인 타이머 목록(_firingHead)이 끊기지 않게 여기서 삼킨다.
    private void ReportError(Exception ex)
    {
        try
        {
            if (_onError != null) _onError(ex);
            else LogThrottle.Error("timerwheel.callback-failed", 5000, "[TimerWheel] callback failed: {0}", ex);
        }
        catch (Exception)
        {
        }
    }

    private void Insert(int n)
    {
        long delta = _due[n] - _currentTick;
        if (delta < 0)
            delta = 0;
        int level = 0;
        long span = SlotsPerLevel;
        while (delta >= span && level < Levels - 1)
        {
            ++level;
            span <<= LevelBits;
        }

        long due = _due[n];
        if (delta > MaxDelta)
            due = _currentTick + MaxDelta;      // 범위 밖: 가장 먼 칸에 걸어 두고, 도착하면 Cascade가 다시 건다(_due는 그대로)

        int slot = (int)((due >> (LevelBits * level)) & SlotMask);
        int b = level * SlotsPerLevel + slot;
        int head = _heads[b];
        _next[n] = head;
        _prev[n] = -1;
        if (head != -1)
            _prev[head] = n;
        _heads[b] = n;
        _bucket[n] = b;
    }

    private void Unlink(int n)
    {
        int b = _bucket[n];
        int next = _next[n], prev = _prev[n];
        if (prev != -1) _next[prev] = next; else _heads[b] = next;
        if (next != -1) _prev[next] = prev;
        _bucket[n] = -1;
    }

    private int AllocNode()
    {
        int n = _freeHead;
        if (n != 0)
        {
            _freeHead = _next[n];
        }
        else
        {
            if (_nodeCount == _due.Length)
                Grow();
            n = _nodeCount++;
        }
        if (_version[n] == 0)
            _version[n] = 1;
        _bucket[n] = -1;
        return n;
    }

    private void FreeNode(int n)
    {
        _callback[n] = null;
        _bucket[n] = -1;
        unchecked { ++_version[n]; }
        if (_version[n] == 0)
            _version[n] = 1;
        _next[n] = _freeHead;
        _freeHead = n;
    }

    private void Grow()
    {
        int size = _due.Length * 2;
        Array.Resize(ref _due, size);
        Array.Resize(ref _intervalTicks, size);
        Array.Resize(ref _next, size);
        Array.Resize(ref _prev, size);
        Array.Resize(ref _bucket, size);
        Array.Resize(ref _version, size);
        Array.Resize(ref _callback, size);
    }
}
