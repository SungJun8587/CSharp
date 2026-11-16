// SpinPreset.cs : 스핀 락 대기 정책(프리셋)과 단계적 백오프.
using System;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 스핀 락의 대기 정책. 1단계에서 CPU 일시 정지(pause)를 1, 2, 4, ... 번씩 늘려 가며 <see cref="MaxPauseBackoff"/>까지 쉬고,
/// 그래도 못 얻으면 2단계에서 스레드 양보(Yield)를 <see cref="MaxYieldCount"/>번, 그다음 3단계에서 실제로 잠든 뒤 2단계로 돌아간다.
/// C++의 <c>SpinLockPreset</c>/<c>RWSpinLockPreset</c> 대응.
/// </summary>
public readonly struct SpinPreset
{
    public uint MaxPauseBackoff { get; }
    public uint MaxYieldCount { get; }

    public SpinPreset(uint maxPauseBackoff, uint maxYieldCount)
    {
        MaxPauseBackoff = maxPauseBackoff;
        MaxYieldCount = maxYieldCount;
    }

    // SpinLockPreset
    public static SpinPreset LightWeight => new SpinPreset(256, 16);
    public static SpinPreset Default => new SpinPreset(1024, 64);
    public static SpinPreset HeavyContention => new SpinPreset(4096, 128);
    /// <summary>스레드 수가 코어 수보다 훨씬 많을 때: 빨리 양보한다.</summary>
    public static SpinPreset OverSubscribed => new SpinPreset(32, 8);

    // RWSpinLockPreset
    public static SpinPreset ReadHeavy => new SpinPreset(512, 32);
    public static SpinPreset WriteContention => new SpinPreset(2048, 128);

    // default(SpinPreset)(= 0, 0)은 "지정 안 함"으로 보고 기본값을 쓴다.
    internal SpinPreset OrDefault() => MaxPauseBackoff == 0 && MaxYieldCount == 0 ? Default : this;
}

/// <summary>
/// 대기 한 번마다 <see cref="Wait"/>를 부르는 단계적 백오프(지역 변수로 쓴다). 단계는 <see cref="SpinPreset"/> 설명 참고.
/// pause 간격이 상한을 넘기면 그 호출부터 양보 단계로 넘어간다. 코어가 하나면 스핀해도 상대가 달리지 못하므로 바로 양보한다.
/// 3단계 잠은 OS가 줄 수 있는 최소 단위(.NET은 1ms)다. 락 보유자가 선점당했을 가능성이 큰 시점이라 잠으로 CPU를 비워 준다.
/// </summary>
internal struct SpinBackoff
{
    private static readonly bool s_multiCore = Environment.ProcessorCount > 1;

    private uint _backoff;      // 0이면 아직 시작 전(= 1)
    private uint _yields;

    public void Wait(uint maxPause, uint maxYield)
    {
        if (_backoff == 0)
            _backoff = 1;

        if (s_multiCore && _backoff <= maxPause)
        {
            Thread.SpinWait((int)_backoff);
            _backoff <<= 1;         // 상한을 넘기면 다음 호출부터 아래 단계로 간다
        }
        else if (_yields < maxYield)
        {
            Thread.Yield();
            ++_yields;
        }
        else
        {
            Thread.Sleep(1);
            _yields = 0;
        }
    }
}
