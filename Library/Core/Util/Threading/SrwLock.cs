// SrwLock.cs : Windows 네이티브 SRWLock 래퍼 (C++ CSRWLock 대응). 다른 플랫폼에서는 RWSpinLock으로 대신한다.
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Core.Util;

/// <summary>
/// 읽기(공유)/쓰기(독점) 락. Windows에서는 커널 SRWLOCK을 P/Invoke로 직접 쓰고, 그 밖의 플랫폼에서는 <see cref="RWSpinLock"/>으로 동작한다
/// (C++ <c>PlatformLock.h</c>가 Windows는 SRWLock, 그 외는 스핀 락으로 고르는 것과 같은 구도).
///
/// <para>재진입이 안 된다(같은 스레드의 중첩 잠금은 교착). 읽기에서 쓰기로 승격할 수 없다(교착).</para>
/// <para><see cref="NativeHandle"/>은 Windows 조건 변수(<c>SleepConditionVariableSRW</c>)와 함께 쓰기 위한 SRWLOCK 포인터이며 Windows에서만 쓸 수 있다.</para>
/// <para>객체가 사라지기 전에 락이 풀려 있어야 한다. Debug 빌드의 <see cref="Dispose"/>는 잠긴 채 버려지면 Assert로 알린다.</para>
/// </summary>
public sealed class SrwLock : IDisposable
{
    private readonly IntPtr _native;            // Windows: 네이티브 메모리에 둔 SRWLOCK (포인터 크기). 옮겨지면 안 되므로 관리 힙에 두지 않는다.
    private readonly RWSpinLock? _fallback;     // 그 외 플랫폼
    private bool _disposed;

    public SrwLock()
    {
        if (OperatingSystem.IsWindows())
        {
            _native = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_native, IntPtr.Zero);
            InitializeSRWLock(_native);
        }
        else
        {
            _fallback = new RWSpinLock();
        }
    }

    ~SrwLock() => FreeNative();

    /// <summary>Windows SRWLOCK 포인터(조건 변수 연동용). Windows가 아니면 <see cref="PlatformNotSupportedException"/>.</summary>
    public IntPtr NativeHandle => _native != IntPtr.Zero ? _native : throw new PlatformNotSupportedException("SrwLock.NativeHandle은 Windows에서만 쓸 수 있습니다");

    // ---- 쓰기 (Exclusive) ----

    /// <param name="name">데드락 프로파일러용 락 이름(<see cref="DeadLockProfiler"/>). null이면 추적하지 않는다. Unlock에도 같은 이름을 준다.</param>
    public void ExclusiveLock(string? name = null)
    {
        LockProfile.Before(name);
        if (_fallback == null) AcquireSRWLockExclusive(_native);
        else _fallback.EnterWrite();
    }

    public bool TryExclusiveLock(string? name = null)
    {
        bool ok = _fallback == null ? TryAcquireSRWLockExclusive(_native) : _fallback.TryEnterWrite();
        if (ok)
            LockProfile.Acquired(name);
        return ok;
    }

    public void ExclusiveUnlock(string? name = null)
    {
        if (_fallback == null) ReleaseSRWLockExclusive(_native);
        else _fallback.ExitWrite();
        LockProfile.Released(name);
    }

    // ---- 읽기 (Shared) ----

    public void SharedLock(string? name = null)
    {
        LockProfile.Before(name);
        if (_fallback == null) AcquireSRWLockShared(_native);
        else _fallback.EnterRead();
    }

    public bool TrySharedLock(string? name = null)
    {
        bool ok = _fallback == null ? TryAcquireSRWLockShared(_native) : _fallback.TryEnterRead();
        if (ok)
            LockProfile.Acquired(name);
        return ok;
    }

    public void SharedUnlock(string? name = null)
    {
        if (_fallback == null) ReleaseSRWLockShared(_native);
        else _fallback.ExitRead();
        LockProfile.Released(name);
    }

    // ---- 범위 가드 ----

    /// <summary>쓰기 범위 가드. (C++ ExclusiveLockGuard)</summary>
    public Scope Exclusive(string? name = null)
    {
        ExclusiveLock(name);
        return new Scope(this, LockType.Write, name);
    }

    /// <summary>읽기 범위 가드. (C++ SharedLockGuard)</summary>
    public Scope Shared(string? name = null)
    {
        SharedLock(name);
        return new Scope(this, LockType.Read, name);
    }

    /// <summary>한 번만 시도하는 쓰기 가드. 얻었을 때만 <see cref="Scope.IsAcquired"/>가 true. (C++ TryExclusiveLockGuard)</summary>
    public Scope TryExclusive(string? name = null) => TryExclusiveLock(name) ? new Scope(this, LockType.Write, name) : default;

    /// <summary>한 번만 시도하는 읽기 가드. 얻었을 때만 <see cref="Scope.IsAcquired"/>가 true. (C++ TrySharedLockGuard)</summary>
    public Scope TryShared(string? name = null) => TrySharedLock(name) ? new Scope(this, LockType.Read, name) : default;

    /// <summary>읽기/쓰기를 값으로 고르는 범위 가드. (C++ CSRWCustomLockGuard)</summary>
    public Scope Lock(LockType type, string? name = null) => type == LockType.Write ? Exclusive(name) : Shared(name);

    /// <summary>호출한 메서드 이름을 락 이름으로 쓰는 <see cref="Exclusive"/>. (C++ <c>SRW_WRITE_LOCK</c>의 <c>__func__</c>)</summary>
    public Scope ExclusiveHere([CallerMemberName] string? name = null) => Exclusive(name);

    /// <summary>호출한 메서드 이름을 락 이름으로 쓰는 <see cref="Shared"/>. (C++ <c>SRW_READ_LOCK</c>의 <c>__func__</c>)</summary>
    public Scope SharedHere([CallerMemberName] string? name = null) => Shared(name);

    public readonly ref struct Scope
    {
        private readonly SrwLock? _owner;
        private readonly LockType _type;
        private readonly string? _name;
        internal Scope(SrwLock owner, LockType type, string? name) { _owner = owner; _type = type; _name = name; }

        public bool IsAcquired => _owner != null;

        public void Dispose()
        {
            if (_owner == null)
                return;

            if (_type == LockType.Write) _owner.ExclusiveUnlock(_name);
            else _owner.SharedUnlock(_name);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // 잠긴 채 버려지면 안 된다: 독점으로 잡아 보면 누가 쥐고 있는지 알 수 있다 (Debug 전용 점검).
        bool notLocked = TryExclusiveLock();
        Debug.Assert(notLocked, "SrwLock disposed while locked");
        if (notLocked)
            ExclusiveUnlock();

        FreeNative();
        GC.SuppressFinalize(this);
    }

    private void FreeNative()
    {
        if (_native != IntPtr.Zero)
            Marshal.FreeHGlobal(_native);
    }

    // kernel32 (Windows에서만 호출된다. 다른 플랫폼에서는 바인딩되지 않는다)
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern void InitializeSRWLock(IntPtr srwLock);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern void AcquireSRWLockExclusive(IntPtr srwLock);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern void ReleaseSRWLockExclusive(IntPtr srwLock);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern void AcquireSRWLockShared(IntPtr srwLock);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern void ReleaseSRWLockShared(IntPtr srwLock);
    [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool TryAcquireSRWLockExclusive(IntPtr srwLock);
    [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool TryAcquireSRWLockShared(IntPtr srwLock);
}
