// CriticalSection.cs : 스핀 횟수를 줄 수 있는 임계 구역 (C++ CCriticalSection / CCSLockGuard 대응).
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Core.Util;

/// <summary>
/// Windows <c>CRITICAL_SECTION</c>처럼 동작하는 락: 재진입이 되고, 막히면 곧바로 재우지 않고 <c>spinCount</c>번까지 짧게 돌며 얻기를 시도한 뒤에 기다린다.
/// 보유 시간이 짧고 경합이 잦은 구역에서 컨텍스트 스위치를 줄인다. 코어가 하나면 스핀이 소용없어 건너뛴다.
/// (.NET <see cref="Monitor"/>에도 자체 적응형 스핀이 있어, 이 클래스는 C++ 코드의 스핀 횟수 인자를 그대로 옮기고 싶을 때 쓴다.)
/// </summary>
public sealed class CriticalSection
{
    private readonly object _gate = new object();
    private readonly int _spinCount;

    /// <param name="spinCount">대기 전에 돌아볼 횟수 (기본 4000, C++과 같다). 0이면 바로 기다린다.</param>
    public CriticalSection(int spinCount = 4000)
    {
        if (spinCount < 0)
            throw new ArgumentOutOfRangeException(nameof(spinCount));

        _spinCount = Environment.ProcessorCount > 1 ? spinCount : 0;
    }

    /// <param name="name">데드락 프로파일러용 락 이름(<see cref="DeadLockProfiler"/>). null이면 추적하지 않는다. Exit에도 같은 이름을 준다.</param>
    public void Enter(string? name = null)
    {
        LockProfile.Before(name, recursive: true);

        for (int i = 0; i < _spinCount; ++i)
        {
            if (Monitor.TryEnter(_gate))
                return;
            Thread.SpinWait(1);
        }

        Monitor.Enter(_gate);
    }

    public bool TryEnter(string? name = null)
    {
        if (!Monitor.TryEnter(_gate))
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    public void Exit(string? name = null)
    {
        Monitor.Exit(_gate);
        LockProfile.Released(name);
    }

    /// <summary><c>using (cs.Lock()) { ... }</c> (C++ CCSLockGuard)</summary>
    public Scope Lock(string? name = null)
    {
        Enter(name);
        return new Scope(this, name);
    }

    public Scope LockHere([CallerMemberName] string? name = null) => Lock(name);

    public readonly ref struct Scope
    {
        private readonly CriticalSection _owner;
        private readonly string? _name;
        internal Scope(CriticalSection owner, string? name) { _owner = owner; _name = name; }
        public void Dispose() => _owner.Exit(_name);
    }
}
