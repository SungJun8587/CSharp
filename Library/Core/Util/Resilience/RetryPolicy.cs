// RetryPolicy.cs : 재시도 정책(지수 백오프 + 지터).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Util;

/// <summary>재시도 정책. 시도 간 대기는 base * factor^(n-1)을 maxDelay로 자르고, 지터로 흩뜨린다.</summary>
public sealed class RetryPolicy
{
    public int MaxAttempts { get; }
    public TimeSpan BaseDelay { get; }
    public TimeSpan MaxDelay { get; }
    public double Factor { get; }
    /// <summary>0~1. 대기의 이 비율만큼을 무작위로 줄인다(0이면 지터 없음). 여러 클라이언트가 동시에 몰려 재시도하는 것을 막는다.</summary>
    public double Jitter { get; }

    public RetryPolicy(int maxAttempts = 3, int baseDelayMs = 100, int maxDelayMs = 5000, double factor = 2.0, double jitter = 0.5)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (baseDelayMs < 0 || maxDelayMs < baseDelayMs) throw new ArgumentOutOfRangeException(nameof(baseDelayMs));
        if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor));
        MaxAttempts = maxAttempts;
        BaseDelay = TimeSpan.FromMilliseconds(baseDelayMs);
        MaxDelay = TimeSpan.FromMilliseconds(maxDelayMs);
        Factor = factor;
        Jitter = Math.Clamp(jitter, 0, 1);
    }

    /// <summary>attempt번째(1부터) 시도가 실패한 뒤 기다릴 시간.</summary>
    public TimeSpan DelayAfter(int attempt)
    {
        double ms = BaseDelay.TotalMilliseconds * Math.Pow(Factor, Math.Max(0, attempt - 1));
        ms = Math.Min(ms, MaxDelay.TotalMilliseconds);
        if (Jitter > 0)
            ms -= ms * Jitter * Random.Shared.NextDouble();
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>동기 실행(스레드를 재운다). 예외가 retryOn을 통과하면 재시도하고, 마지막 시도의 예외는 그대로 던진다.</summary>
    public T Execute<T>(Func<T> action, Func<Exception, bool>? retryOn = null)
    {
        for (int attempt = 1; ; ++attempt)
        {
            try { return action(); }
            catch (Exception ex) when (attempt < MaxAttempts && (retryOn == null || retryOn(ex)))
            {
                Thread.Sleep(DelayAfter(attempt));
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, Func<Exception, bool>? retryOn = null, CancellationToken ct = default)
    {
        for (int attempt = 1; ; ++attempt)
        {
            ct.ThrowIfCancellationRequested();
            try { return await action(ct).ConfigureAwait(false); }
            catch (Exception ex) when (attempt < MaxAttempts && !(ex is OperationCanceledException && ct.IsCancellationRequested) && (retryOn == null || retryOn(ex)))
            {
                await Task.Delay(DelayAfter(attempt), ct).ConfigureAwait(false);
            }
        }
    }
}
