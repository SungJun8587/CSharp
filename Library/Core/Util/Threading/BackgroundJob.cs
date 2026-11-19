// BackgroundJob.cs : 결과를 기다리지 않는 작업을 스레드풀에서 실행한다(fire-and-forget).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Util;

/// <summary>
/// 호출한 쪽이 기다리지 않는 작업을 스레드풀에 올린다. 예외는 호출자에게 가지 않고 <see cref="ExceptionHandler"/>(없으면 LogThrottle 오류 로그)로 간다.
/// <para>
/// 호출 쪽의 ExecutionContext(AsyncLocal 값, 보안 문맥)는 작업으로 넘기지 않는다. 요청 단위 문맥이 오래 걸리는 작업에 따라가
/// 해제되지 않고 남는 일을 막기 위해서다. 작업이 필요로 하는 값은 인자로 캡처해서 넘긴다.
/// 순서 보장이 필요하거나 작업 수를 제한해야 하면 <see cref="JobQueue"/>/<see cref="WorkerPool"/>을 쓴다.
/// </para>
/// </summary>
public static class BackgroundJob
{
    private static volatile Action<Exception>? s_handler;

    /// <summary>작업에서 처리되지 않은 예외를 받는 곳. 이 처리기가 예외를 던져도 무시한다. null이면 로그만 남긴다.</summary>
    public static Action<Exception>? ExceptionHandler
    {
        get => s_handler;
        set => s_handler = value;
    }

    /// <summary>동기 작업을 스레드풀에서 실행한다.</summary>
    public static void Run(Action action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            try { ((Action)state!)(); }
            catch (Exception e) { Report(e); }
        }, action);
    }

    /// <summary>비동기 작업을 스레드풀에서 시작한다. 작업이 끝나기를 기다리지 않는다.</summary>
    public static void Run(Func<Task> function)
    {
        if (function == null) throw new ArgumentNullException(nameof(function));
        ThreadPool.UnsafeQueueUserWorkItem(static state => { _ = RunAsync((Func<Task>)state!); }, function);
    }

    private static async Task RunAsync(Func<Task> function)
    {
        try { await function().ConfigureAwait(false); }
        catch (Exception e) { Report(e); }
    }

    private static void Report(Exception e)
    {
        Action<Exception>? handler = s_handler;
        if (handler != null)
        {
            try { handler(e); } catch { }
            return;
        }
        LogThrottle.Error("backgroundjob.failed", 1000, "[BackgroundJob] 예외: {0}", e);
    }
}
