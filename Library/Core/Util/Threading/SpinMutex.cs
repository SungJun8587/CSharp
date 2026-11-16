// SpinMutex.cs : 짧은 임계 구역용 스핀 락 (C++ SpinLock<Preset> 대응).
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 상호 배제 스핀 락. 재진입 불가이며, 보유 시간이 매우 짧은 구역(포인터 몇 개 갱신, 큐 한두 칸 이동)에만 쓴다.
/// 보유 중에 I/O, 블로킹 호출, 다른 락 획득을 하면 안 된다.
///
/// <para><b>대기</b>: <see cref="SpinPreset"/>의 단계적 백오프(pause → 양보 → 잠)로 기다린다. 락 상태가 비어 보일 때만 CAS를 시도해
/// 캐시라인을 공유 상태로 유지한다. 구역이 길어질 수 있으면 <c>lock</c>(Monitor)을 쓴다.</para>
/// <para><b>캐시라인</b>: 락 상태 앞뒤를 채워 다른 객체와 한 캐시라인을 쓰지 않게 한다(객체 하나가 약 200바이트).
/// 세션마다 락을 두는 식으로 아주 많이 만들면 이 메모리 비용을 감안한다.</para>
/// <para><b>진단</b>: <see cref="Exit"/>는 보유하지 않은 상태에서 부르면 <see cref="SynchronizationLockException"/>을 던진다.
/// 이름을 주면 <see cref="DeadLockProfiler"/>가 획득 순서를 추적한다(Debug 빌드).</para>
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 3 * CacheLine.Size)]
public sealed class SpinMutex
{
    [FieldOffset(CacheLine.Size)] private int _state;               // 0 = 비어 있음, 1 = 보유 중
    [FieldOffset(CacheLine.Size + 4)] private readonly uint _maxPause;
    [FieldOffset(CacheLine.Size + 8)] private readonly uint _maxYield;

    /// <param name="preset">대기 정책. 지정하지 않으면 <see cref="SpinPreset.Default"/></param>
    public SpinMutex(SpinPreset preset = default)
    {
        preset = preset.OrDefault();
        _maxPause = preset.MaxPauseBackoff;
        _maxYield = preset.MaxYieldCount;
    }

    /// <summary>락을 보유 중인지(진단용 스냅샷).</summary>
    public bool IsHeld => Volatile.Read(ref _state) != 0;

    /// <summary>락을 얻을 때까지 기다린다.</summary>
    /// <param name="name">데드락 프로파일러용 락 이름(<see cref="DeadLockProfiler"/>). null이면 추적하지 않는다. Exit에도 같은 이름을 준다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter(string? name = null)
    {
        LockProfile.Before(name);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            EnterSlow();
    }

    private void EnterSlow()
    {
        var backoff = new SpinBackoff();
        while (true)
        {
            // 읽기로 먼저 확인하고 비어 보일 때만 CAS를 시도한다.
            while (Volatile.Read(ref _state) != 0)
                backoff.Wait(_maxPause, _maxYield);

            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
                return;
        }
    }

    /// <summary>한 번만 시도한다. 얻으면 true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnter(string? name = null)
    {
        if (Volatile.Read(ref _state) != 0 || Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            return false;

        LockProfile.Acquired(name);
        return true;
    }

    /// <summary>락을 푼다.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit(string? name = null)
    {
        if (Interlocked.Exchange(ref _state, 0) == 0)
            throw new SynchronizationLockException("SpinMutex.Exit: 보유하지 않은 락입니다");

        LockProfile.Released(name);     // 실제로 푼 뒤에 알린다 (알림에서 예외가 나도 락이 남지 않게)
    }

    // ---- 범위 가드 ----

    /// <summary><c>using (mutex.Lock()) { ... }</c>: 범위를 벗어나면 자동으로 푼다. (C++ SpinLockGuard)</summary>
    public Scope Lock(string? name = null)
    {
        Enter(name);
        return new Scope(this, name);
    }

    /// <summary>
    /// 호출한 메서드 이름을 락 이름으로 쓰는 <see cref="Lock"/>. C++의 <c>SPIN_LOCK</c> 매크로가 <c>__func__</c>를 이름으로 넘기는 것과 같다.
    /// 락 인스턴스가 아니라 <i>메서드</i>가 프로파일러의 노드가 된다.
    /// </summary>
    public Scope LockHere([CallerMemberName] string? name = null) => Lock(name);

    /// <summary>
    /// 한 번만 시도하는 범위 가드. 얻었으면 <see cref="Scope.IsAcquired"/>가 true이고 Dispose에서 푼다. 못 얻었으면 Dispose는 아무것도 하지 않는다.
    /// (C++ TrySpinLockGuard)
    /// </summary>
    public Scope TryLock(string? name = null) => TryEnter(name) ? new Scope(this, name) : default;

    public readonly ref struct Scope
    {
        private readonly SpinMutex? _owner;
        private readonly string? _name;
        internal Scope(SpinMutex owner, string? name) { _owner = owner; _name = name; }

        /// <summary><see cref="Lock"/>은 항상 true. <see cref="TryLock"/>은 얻었을 때만 true.</summary>
        public bool IsAcquired => _owner != null;

        public void Dispose() => _owner?.Exit(_name);
    }
}
