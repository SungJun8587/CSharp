// ITimeProvider.cs : 시계 추상화(운영용 시스템 시계, 테스트용 수동 시계).
using System;

namespace Core.Util;

/// <summary>
/// 시계 추상화. 단조 시계(간격/만료 계산용)와 벽시계(저장/표시용)를 분리해서 제공한다.
/// 이 라이브러리의 유틸은 시각을 ms 값이나 Func&lt;long&gt;로 받으므로, AsMonotonicClock()으로 바로 넘길 수 있다.
/// </summary>
public interface ITimeProvider
{
    /// <summary>되돌아가지 않는 ms 시각(기준점은 임의). 쿨다운/만료/제한기에 쓴다.</summary>
    long MonotonicMs { get; }

    /// <summary>UTC Unix ms. 저장/로그/표시에 쓴다(시계 보정으로 뒤로 갈 수 있다).</summary>
    long WallMs { get; }
}

public static class TimeProviderExtensions
{
    public static Func<long> AsMonotonicClock(this ITimeProvider p) => () => p.MonotonicMs;
    public static Func<long> AsWallClock(this ITimeProvider p) => () => p.WallMs;
}

/// <summary>운영용: TimeUtil의 시스템 시계.</summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    public static SystemTimeProvider Instance { get; } = new SystemTimeProvider();

    public long MonotonicMs => TimeUtil.MonotonicNowMillis();
    public long WallMs => TimeUtil.NowMillis();
}

/// <summary>테스트/시뮬레이션용: 호출자가 직접 시각을 움직인다. 스레드 안전하다.</summary>
public sealed class ManualTimeProvider : ITimeProvider
{
    private long _mono;
    private long _wall;

    public ManualTimeProvider(long monotonicMs = 0, long wallMs = 0)
    {
        _mono = monotonicMs;
        _wall = wallMs;
    }

    public long MonotonicMs => System.Threading.Interlocked.Read(ref _mono);
    public long WallMs => System.Threading.Interlocked.Read(ref _wall);

    /// <summary>두 시계를 함께 ms만큼 앞으로 보낸다.</summary>
    public void Advance(long ms)
    {
        if (ms < 0) throw new ArgumentOutOfRangeException(nameof(ms), "단조 시계는 뒤로 갈 수 없다");
        System.Threading.Interlocked.Add(ref _mono, ms);
        System.Threading.Interlocked.Add(ref _wall, ms);
    }

    /// <summary>벽시계만 바꾼다(시계 보정/역행 시험용).</summary>
    public void SetWall(long wallMs) => System.Threading.Interlocked.Exchange(ref _wall, wallMs);
}
