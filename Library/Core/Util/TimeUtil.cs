// TimeUtil.cs : 시각 유틸 (C++ ptime::NowMillis / ptime::MonotonicNowMillis 에 대응).
using System;
using System.Diagnostics;

namespace Core.Util;

/// <summary>벽시계 시각(epoch ms)과 단조 증가 시각(ms)을 제공한다.</summary>
public static class TimeUtil
{
    /// <summary>UTC 기준 Unix epoch 밀리초. 시스템 시계가 조정되면 뒤로 갈 수 있으므로 경과 시간 측정에는 쓰지 않는다.</summary>
    public static long NowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// 시스템 시계 조정과 무관하게 단조 증가하는 밀리초. 기준점은 임의이므로 같은 프로세스 안에서
    /// 두 값의 차이(타임아웃, 경과 시간)에만 쓴다.
    /// </summary>
    public static long MonotonicNowMillis() => TicksToMillis(Stopwatch.GetTimestamp(), Stopwatch.Frequency);

    /// <summary>
    /// 틱을 밀리초로 바꾼다. Linux는 틱이 부팅 후 나노초(주파수 10^9)라서 <c>ticks * 1000</c>을 먼저 곱하면 약 106일 가동 뒤 long이 넘친다.
    /// 초 단위 몫과 나머지로 나눠 계산하므로 넘치지 않는다.
    /// </summary>
    public static long TicksToMillis(long ticks, long frequency)
        => ticks / frequency * 1000L + ticks % frequency * 1000L / frequency;
}
