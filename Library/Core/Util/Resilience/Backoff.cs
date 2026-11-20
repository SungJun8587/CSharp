// Backoff.cs : 지수 백오프 상태(연속 실패 횟수 + 다음 재시도 시각).
using System;

namespace Core.Util;

/// <summary>
/// 재연결/재시도 시각을 관리하는 지수 백오프 상태. 실패할 때마다 대기 시간이 base, 2×base, 4×base… 로 늘어나고 상한에서 멈춘다.
/// 여러 대상(노드, 연결)에 각각 하나씩 두고 "지금 시도해도 되는가"(ShouldSkip)를 물어 쓴다. 시각은 호출자가 단조 ms로 넘긴다.
/// 스레드 안전하지 않다 — 재연결을 맡은 스레드 하나만 만진다. 횟수 제한이 있는 호출 재시도는 <see cref="RetryPolicy"/>를 쓴다.
/// </summary>
public sealed class BackoffState
{
    private readonly long _baseMs;
    private readonly long _maxMs;
    private readonly int _maxShift;
    private readonly int _jitterMs;

    /// <param name="baseMs">첫 실패 뒤 대기 시간.</param>
    /// <param name="maxMs">대기 시간 상한(지터를 더한 값도 이 상한을 넘지 않는다).</param>
    /// <param name="maxShift">두 배로 늘리는 횟수의 상한(이 횟수 뒤로는 같은 값).</param>
    /// <param name="jitterMs">양수면 대기 시간에 0~jitterMs를 무작위로 더해 여러 대상이 동시에 몰리지 않게 한다.</param>
    public BackoffState(long baseMs, long maxMs, int maxShift = 30, int jitterMs = 0)
    {
        if (baseMs < 0 || maxMs < baseMs || maxShift < 0 || jitterMs < 0) throw new ArgumentOutOfRangeException(nameof(baseMs));
        _baseMs = baseMs;
        _maxMs = maxMs;
        _maxShift = maxShift;
        _jitterMs = jitterMs;
    }

    public int FailCount { get; private set; }
    public long NextRetryMs { get; private set; }

    /// <summary>실패한 적이 있고 다음 시도 시각이 아직 안 됐으면 true.</summary>
    public bool ShouldSkip(long nowMs) => FailCount > 0 && nowMs < NextRetryMs;

    /// <summary>실패를 기록하고 다음 시도 시각을 정한다. 이번에 정해진 대기 시간(ms)을 돌려준다.</summary>
    public long OnFail(long nowMs) => OnFail(nowMs, _jitterMs);

    /// <summary>생성자의 지터 대신 이번 호출에만 쓸 지터를 지정한다.</summary>
    public long OnFail(long nowMs, int jitterMs)
    {
        if (FailCount < int.MaxValue) ++FailCount;
        long delay = ComputeDelayMs(FailCount, _baseMs, _maxMs, _maxShift, jitterMs);
        NextRetryMs = TimeUtil.AddSaturated(nowMs, delay);
        return delay;
    }

    /// <summary>성공했으니 처음부터 다시 센다.</summary>
    public void Reset()
    {
        FailCount = 0;
        NextRetryMs = 0;
    }

    /// <summary>
    /// failCount번째 실패 뒤의 대기 시간. 1회=baseMs, 이후 두 배씩(최대 maxShift번) 늘고 maxMs에서 멈춘다.
    /// jitterMs가 양수면 0~jitterMs를 더하되 maxMs를 넘기지 않는다.
    /// </summary>
    public static long ComputeDelayMs(int failCount, long baseMs, long maxMs, int maxShift, int jitterMs = 0)
    {
        int shift = Math.Max(0, Math.Min(failCount - 1, Math.Min(maxShift, 62)));
        long delay = baseMs > (long.MaxValue >> shift) ? maxMs : Math.Min(baseMs << shift, maxMs);
        if (jitterMs > 0)
        {
            // 상한을 넘기지 않으면서 지터를 유지한다: 상한에 닿은 구간은 [max-jitter, max] 로 아래쪽에 분산한다.
            // (더한 뒤 상한으로 자르면 상한 도달 후 지터가 사라져 모든 클라이언트가 같은 간격으로 동시에 재시도한다)
            long j = Random.Shared.Next(0, jitterMs + 1);
            delay = delay + j > maxMs ? Math.Max(0, maxMs - j) : delay + j;
        }
        return delay;
    }
}
