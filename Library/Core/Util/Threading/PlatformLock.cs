// PlatformLock.cs : 운영체제 동기화 객체를 감싼 PLock / PRWLock.
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 일반 상호 배제 락 (<see cref="Monitor"/> 기반, 재진입 가능). 보유 시간이 길거나 블로킹 호출이 낄 수 있는 구역용이다.
/// 경합이 없으면 사용자 모드에서 끝나고, 경합이 오래가면 스레드를 재워 CPU를 쓰지 않는다. 짧은 구역은 <see cref="SpinMutex"/>가 더 가볍다.
/// C++의 <c>PLock</c>(Windows는 SRWLock, 그 외는 스핀 락)에 해당하며, .NET에서는 이식성과 조건 대기(<see cref="Wait"/>)를 위해 Monitor를 쓴다.
/// </summary>
public sealed class PLock
{
    private readonly object _gate = new object();

    // name: 데드락 프로파일러용 락 이름(DeadLockProfiler). null이면 추적하지 않는다. Exit에도 같은 이름을 준다.
    // 재진입이 가능하므로 같은 이름의 중첩은 자기 대기로 보지 않는다. Wait 중에는 락이 풀려 있지만 보유 스택에는 남아 있다(보수적).
    public void Enter(string? name = null)
    {
        LockProfile.Before(name, recursive: true);
        Monitor.Enter(_gate);
    }

    public bool TryEnter(string? name = null)
    {
        if (!Monitor.TryEnter(_gate))
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    public bool TryEnter(int timeoutMs, string? name = null)
    {
        if (!Monitor.TryEnter(_gate, timeoutMs))
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    public void Exit(string? name = null)
    {
        Monitor.Exit(_gate);
        LockProfile.Released(name);
    }

    /// <summary>Monitor 조건 대기용: 락을 보유한 상태에서 호출한다.</summary>
    public bool Wait(int timeoutMs = Timeout.Infinite) => Monitor.Wait(_gate, timeoutMs);
    public void Pulse() => Monitor.Pulse(_gate);
    public void PulseAll() => Monitor.PulseAll(_gate);

    /// <summary><c>using (plock.Lock()) { ... }</c> (C++ PLockGuard)</summary>
    public Scope Lock(string? name = null)
    {
        Enter(name);
        return new Scope(this, name);
    }

    /// <summary>호출한 메서드 이름을 락 이름으로 쓰는 <see cref="Lock"/>. (C++ 매크로의 <c>__func__</c>)</summary>
    public Scope LockHere([CallerMemberName] string? name = null) => Lock(name);

    /// <summary>한 번만 시도하는 범위 가드. 얻었을 때만 <see cref="Scope.IsAcquired"/>가 true이고 Dispose에서 푼다.</summary>
    public Scope TryLock(string? name = null) => TryEnter(name) ? new Scope(this, name) : default;

    public readonly ref struct Scope
    {
        private readonly PLock? _owner;
        private readonly string? _name;
        internal Scope(PLock owner, string? name) { _owner = owner; _name = name; }

        public bool IsAcquired => _owner != null;
        public void Dispose() => _owner?.Exit(_name);
    }
}

/// <summary>
/// 읽기/쓰기 락 (<see cref="ReaderWriterLockSlim"/> 기반, 재진입 불가). 읽기가 압도적으로 많고 구역이 긴 경우용이다.
/// 구역이 아주 짧으면 <see cref="RWSpinLock"/>이 더 가볍다. 다 쓴 뒤에는 Dispose로 내부 핸들을 해제한다.
/// Windows 네이티브 SRWLock이 필요하면 <see cref="SrwLock"/>.
/// </summary>
public sealed class PRWLock : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

    // name: 데드락 프로파일러용 락 이름(DeadLockProfiler). null이면 추적하지 않는다. Exit에도 같은 이름을 준다.
    public void EnterRead(string? name = null)
    {
        LockProfile.Before(name);
        _lock.EnterReadLock();
    }

    public bool TryEnterRead(int timeoutMs = 0, string? name = null)
    {
        if (!_lock.TryEnterReadLock(timeoutMs))
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    public void ExitRead(string? name = null)
    {
        _lock.ExitReadLock();
        LockProfile.Released(name);
    }

    public void EnterWrite(string? name = null)
    {
        LockProfile.Before(name);
        _lock.EnterWriteLock();
    }

    public bool TryEnterWrite(int timeoutMs = 0, string? name = null)
    {
        if (!_lock.TryEnterWriteLock(timeoutMs))
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    public void ExitWrite(string? name = null)
    {
        _lock.ExitWriteLock();
        LockProfile.Released(name);
    }

    /// <summary>읽기 범위 가드. (C++ PRReadLockGuard)</summary>
    public Scope Read(string? name = null)
    {
        EnterRead(name);
        return new Scope(this, LockType.Read, name);
    }

    /// <summary>쓰기 범위 가드. (C++ PRWriteLockGuard)</summary>
    public Scope Write(string? name = null)
    {
        EnterWrite(name);
        return new Scope(this, LockType.Write, name);
    }

    public Scope Lock(LockType type, string? name = null) => type == LockType.Write ? Write(name) : Read(name);
    public Scope ReadHere([CallerMemberName] string? name = null) => Read(name);
    public Scope WriteHere([CallerMemberName] string? name = null) => Write(name);

    /// <summary>한 번만 시도하는 읽기 가드(대기 없음). 얻었을 때만 <see cref="Scope.IsAcquired"/>가 true.</summary>
    public Scope TryRead(string? name = null) => TryEnterRead(0, name) ? new Scope(this, LockType.Read, name) : default;

    /// <summary>한 번만 시도하는 쓰기 가드(대기 없음). 얻었을 때만 <see cref="Scope.IsAcquired"/>가 true.</summary>
    public Scope TryWrite(string? name = null) => TryEnterWrite(0, name) ? new Scope(this, LockType.Write, name) : default;

    public void Dispose() => _lock.Dispose();

    public readonly ref struct Scope
    {
        private readonly PRWLock? _owner;
        private readonly LockType _type;
        private readonly string? _name;
        internal Scope(PRWLock owner, LockType type, string? name) { _owner = owner; _type = type; _name = name; }

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
