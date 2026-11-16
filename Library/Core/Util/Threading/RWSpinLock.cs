// RWSpinLock.cs : 읽기/쓰기 스핀 락 (C++ RWSpinLock<Preset> 대응).
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Core.Util;

/// <summary>읽기/쓰기 구분. 동적으로 고르는 가드용(C++ <c>LockType</c>).</summary>
public enum LockType { Read, Write }

/// <summary>
/// 읽기 다수/쓰기 하나의 스핀 락. 재진입 불가이며, 보유 시간이 매우 짧은 구역에만 쓴다.
///
/// <para><b>상태</b>: 32비트 하나에 쓰기 보유 비트(bit 0), 읽기 보유 수(bit 1~15, 최대 32767), 쓰기 대기·보유 수(bit 16~31, 최대 65535)를 담는다.
/// 쓰기 수에는 락을 쥔 쓰기 스레드 자신도 포함되며, 풀 때 한꺼번에 뺀다.</para>
/// <para><b>쓰기 우선</b>: 쓰기를 기다리거나 쥔 스레드가 있으면 새 읽기는 들어오지 못한다. 읽기가 끊임없이 이어져 쓰기가 굶는 일을 막는다.
/// 보장하는 공정성은 여기까지이고 쓰기끼리의 도착 순서는 보장하지 않는다. 그래서 <see cref="TryEnterWrite"/>는 쓰기 대기자가 있어도
/// 읽기 보유자가 0명이면 새치기할 수 있다. 쓰기 사이의 엄격한 순서가 필요하면 <see cref="EnterWrite"/>만 쓴다.</para>
/// <para><b>한계</b>: 읽기 32767개, 쓰기 65535개를 넘기면 <see cref="InvalidOperationException"/>(카운터를 되돌린 뒤)을 던진다.
/// 읽기 보유 중에 같은 락의 읽기를 다시 얻으면(재진입) 쓰기 대기자가 끼는 순간 교착하므로 하지 않는다.</para>
/// <para><b>캐시라인</b>과 <b>대기 정책</b>은 <see cref="SpinMutex"/>와 같다. 짝이 어긋난 Exit는 <see cref="SynchronizationLockException"/>으로 드러난다.</para>
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 3 * CacheLine.Size)]
public sealed class RWSpinLock
{
    private const int WriteLocked = 0x00000001;
    private const int ReaderCountMask = 0x0000FFFE;
    private const int ReaderOne = 0x00000002;
    private const int WriterWaitingMask = unchecked((int)0xFFFF0000);
    private const int WriterOne = 0x00010000;

    [FieldOffset(CacheLine.Size)] private int _state;
    [FieldOffset(CacheLine.Size + 4)] private readonly uint _maxPause;
    [FieldOffset(CacheLine.Size + 8)] private readonly uint _maxYield;

    /// <param name="preset">대기 정책. 지정하지 않으면 <see cref="SpinPreset.Default"/>. <see cref="SpinPreset.ReadHeavy"/>, <see cref="SpinPreset.WriteContention"/>도 있다.</param>
    public RWSpinLock(SpinPreset preset = default)
    {
        preset = preset.OrDefault();
        _maxPause = preset.MaxPauseBackoff;
        _maxYield = preset.MaxYieldCount;
    }

    /// <summary>현재 읽기 보유 수(진단용 스냅샷).</summary>
    public int ReaderCount => (Volatile.Read(ref _state) & ReaderCountMask) >> 1;

    /// <summary>쓰기 보유 중인지(진단용 스냅샷).</summary>
    public bool IsWriteHeld => (Volatile.Read(ref _state) & WriteLocked) != 0;

    // ---- 읽기 ----

    /// <param name="name">데드락 프로파일러용 락 이름(<see cref="DeadLockProfiler"/>). null이면 추적하지 않는다. Exit에도 같은 이름을 준다.</param>
    public void EnterRead(string? name = null)
    {
        LockProfile.Before(name);

        var backoff = new SpinBackoff();
        while (true)
        {
            while ((Volatile.Read(ref _state) & (WriterWaitingMask | WriteLocked)) != 0)
                backoff.Wait(_maxPause, _maxYield);

            if (TryAddReader())
                return;
        }
    }

    /// <summary>한 번만 시도한다. 쓰기 보유 중이거나 쓰기 대기자가 있으면 false.</summary>
    public bool TryEnterRead(string? name = null)
    {
        if ((Volatile.Read(ref _state) & (WriterWaitingMask | WriteLocked)) != 0 || !TryAddReader())
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    // 읽기 수를 먼저 올리고 그 사이 쓰기가 끼지 않았는지 확인한다. 끼었으면 되돌리고 false.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAddReader()
    {
        int prev = Interlocked.Add(ref _state, ReaderOne) - ReaderOne;
        if ((prev & (WriterWaitingMask | WriteLocked)) == 0)
        {
            if (unchecked(prev + ReaderOne & ReaderCountMask) == 0)
            {
                Interlocked.Add(ref _state, -ReaderOne);
                throw new InvalidOperationException("RWSpinLock: 읽기 보유 수가 한계(32767)를 넘었습니다");
            }
            return true;
        }

        Interlocked.Add(ref _state, -ReaderOne);
        return false;
    }

    public void ExitRead(string? name = null)
    {
        // 읽기 수가 0이거나 쓰기 보유 중이면 짝이 어긋난 호출이다 (그대로 감소시키면 상태가 깨진다).
        while (true)
        {
            int s = Volatile.Read(ref _state);
            if ((s & ReaderCountMask) == 0 || (s & WriteLocked) != 0)
                throw new SynchronizationLockException("RWSpinLock.ExitRead: 읽기를 보유하지 않았습니다");

            if (Interlocked.CompareExchange(ref _state, s - ReaderOne, s) == s)
            {
                LockProfile.Released(name);
                return;
            }
        }
    }

    // ---- 쓰기 ----

    public void EnterWrite(string? name = null)
    {
        LockProfile.Before(name);

        // 먼저 쓰기 대기를 알려 새 읽기를 막고, 기존 읽기가 빠지길 기다린다.
        int prev = Interlocked.Add(ref _state, WriterOne) - WriterOne;
        if (unchecked(prev + WriterOne & WriterWaitingMask) == 0)
        {
            Interlocked.Add(ref _state, -WriterOne);
            throw new InvalidOperationException("RWSpinLock: 쓰기 대기 수가 한계(65535)를 넘었습니다");
        }

        var backoff = new SpinBackoff();
        while (true)
        {
            int s = Volatile.Read(ref _state);
            if ((s & (ReaderCountMask | WriteLocked)) == 0)
            {
                if (Interlocked.CompareExchange(ref _state, s | WriteLocked, s) == s)
                    return;
            }
            else
            {
                backoff.Wait(_maxPause, _maxYield);
            }
        }
    }

    /// <summary>한 번만 시도한다. 읽기/쓰기 보유자가 없을 때만 성공한다(쓰기 대기자는 보지 않는다 — 클래스 설명 참고). CAS가 지면 재시도 없이 false.</summary>
    public bool TryEnterWrite(string? name = null)
    {
        int s = Volatile.Read(ref _state);
        if ((s & (ReaderCountMask | WriteLocked)) != 0)
            return false;

        if ((s & WriterWaitingMask) == WriterWaitingMask)
            throw new InvalidOperationException("RWSpinLock: 쓰기 대기 수가 한계(65535)를 넘었습니다");

        // 쓰기 수는 여러 비트의 카운터라 OR가 아니라 덧셈으로 올린다 (다른 쓰기 대기자가 있어도 카운트가 뭉개지지 않는다).
        if (Interlocked.CompareExchange(ref _state, s + WriterOne + WriteLocked, s) != s)
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    public void ExitWrite(string? name = null)
    {
        // 쓰기 비트와 쓰기 수 하나를 함께 뺀다 (EnterWrite/TryEnterWrite가 둘 다 올렸다).
        while (true)
        {
            int s = Volatile.Read(ref _state);
            if ((s & WriteLocked) == 0 || (s & WriterWaitingMask) == 0)
                throw new SynchronizationLockException("RWSpinLock.ExitWrite: 쓰기를 보유하지 않았습니다");

            if (Interlocked.CompareExchange(ref _state, s - (WriterOne + WriteLocked), s) == s)
            {
                LockProfile.Released(name);
                return;
            }
        }
    }

    // ---- 범위 가드 ----

    /// <summary>읽기 락 범위 가드. (C++ ReadLockGuard)</summary>
    public Scope Read(string? name = null)
    {
        EnterRead(name);
        return new Scope(this, LockType.Read, name);
    }

    /// <summary>쓰기 락 범위 가드. (C++ WriteLockGuard)</summary>
    public Scope Write(string? name = null)
    {
        EnterWrite(name);
        return new Scope(this, LockType.Write, name);
    }

    /// <summary>읽기/쓰기를 값으로 고르는 범위 가드. (C++ CustomLockGuard)</summary>
    public Scope Lock(LockType type, string? name = null) => type == LockType.Write ? Write(name) : Read(name);

    /// <summary>호출한 메서드 이름을 락 이름으로 쓰는 <see cref="Read"/>. (C++ <c>RWSPIN_READ_LOCK</c>의 <c>__func__</c>)</summary>
    public Scope ReadHere([CallerMemberName] string? name = null) => Read(name);

    /// <summary>호출한 메서드 이름을 락 이름으로 쓰는 <see cref="Write"/>. (C++ <c>RWSPIN_WRITE_LOCK</c>의 <c>__func__</c>)</summary>
    public Scope WriteHere([CallerMemberName] string? name = null) => Write(name);

    /// <summary>한 번만 시도하는 읽기 가드. 얻었을 때만 <see cref="Scope.IsAcquired"/>가 true이고 Dispose에서 푼다.</summary>
    public Scope TryRead(string? name = null) => TryEnterRead(name) ? new Scope(this, LockType.Read, name) : default;

    /// <summary>한 번만 시도하는 쓰기 가드. 얻었을 때만 <see cref="Scope.IsAcquired"/>가 true이고 Dispose에서 푼다.</summary>
    public Scope TryWrite(string? name = null) => TryEnterWrite(name) ? new Scope(this, LockType.Write, name) : default;

    public readonly ref struct Scope
    {
        private readonly RWSpinLock? _owner;
        private readonly LockType _type;
        private readonly string? _name;
        internal Scope(RWSpinLock owner, LockType type, string? name) { _owner = owner; _type = type; _name = name; }

        public bool IsAcquired => _owner != null;

        public void Dispose()
        {
            if (_owner == null)
                return;

            if (_type == LockType.Write) _owner.ExitWrite(_name);
            else _owner.ExitRead(_name);
        }
    }
}
