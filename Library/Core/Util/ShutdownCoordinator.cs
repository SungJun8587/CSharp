// ShutdownCoordinator.cs : 서버 종료 절차(순서 있는 훅, 시간 제한, 종료 신호) 관리.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Util;

public enum HookOutcome : byte { Completed, Failed, TimedOut, Skipped }

public readonly record struct HookResult(string Name, HookOutcome Outcome, long ElapsedMs, Exception? Error);

/// <summary>
/// 종료 훅을 order 오름차순(같으면 등록 순)으로 하나씩 실행한다. 예: 접속 차단(0) → 존 정리(10) → 저장 비우기(20) → 로그 닫기(100).
/// 훅마다 시간 제한이 있고(넘으면 취소 토큰을 켜고 기다리지 않고 넘어간다), 예외는 기록하고 다음 훅으로 간다.
/// 전체 제한(totalTimeoutMs)을 넘기면 남은 훅은 건너뛴다. RunAsync는 여러 번 불러도 한 번만 실행된다.
/// 사용: Register... → AttachProcessSignals() → await WhenRequested → await RunAsync().
/// </summary>
public sealed class ShutdownCoordinator : IDisposable
{
    private sealed class Hook
    {
        public string Name = "";
        public int Order;
        public int TimeoutMs;
        public Func<CancellationToken, Task> Run = null!;
        public int Seq;
    }

    private readonly object _lock = new object();
    private readonly List<Hook> _hooks = new List<Hook>();
    private readonly CancellationTokenSource _requested = new CancellationTokenSource();
    private readonly TaskCompletionSource<bool> _requestedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _totalTimeoutMs;
    private Task<IReadOnlyList<HookResult>>? _run;
    private readonly List<IDisposable> _registrations = new List<IDisposable>();
    private ConsoleCancelEventHandler? _cancelHandler;
    private EventHandler? _exitHandler;

    public ShutdownCoordinator(int totalTimeoutMs = 30_000)
    {
        if (totalTimeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(totalTimeoutMs));
        _totalTimeoutMs = totalTimeoutMs;
    }

    /// <summary>종료가 요청되면 켜지는 토큰. 메인 루프/워커가 들고 있다가 빠져나오는 데 쓴다.</summary>
    public CancellationToken Token => _requested.Token;
    public bool IsRequested => _requested.IsCancellationRequested;
    public Task WhenRequested => _requestedTcs.Task;

    public void Register(string name, Action action, int order = 0, int timeoutMs = 10_000)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        Add(name, ct => Task.Run(action), order, timeoutMs);
    }

    public void Register(string name, Func<CancellationToken, Task> action, int order = 0, int timeoutMs = 10_000)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        Add(name, ct => Task.Run(() => action(ct), CancellationToken.None), order, timeoutMs);
    }

    private void Add(string name, Func<CancellationToken, Task> run, int order, int timeoutMs)
    {
        if (timeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        lock (_lock)
        {
            if (_run != null) throw new InvalidOperationException("종료가 이미 시작됐다");
            _hooks.Add(new Hook { Name = name, Order = order, TimeoutMs = timeoutMs, Run = run, Seq = _hooks.Count });
        }
    }

    /// <summary>종료를 요청한다(훅은 아직 실행하지 않는다). 여러 번 불러도 된다.</summary>
    public void RequestShutdown()
    {
        if (!_requested.IsCancellationRequested)
        {
            try { _requested.Cancel(); } catch (ObjectDisposedException) { }
        }
        _requestedTcs.TrySetResult(true);
    }

    /// <summary>종료를 요청하고 훅을 실행한다. 이미 실행 중/완료됐으면 같은 결과를 돌려준다.</summary>
    public Task<IReadOnlyList<HookResult>> RunAsync()
    {
        RequestShutdown();
        lock (_lock)
            return _run ??= RunHooks();
    }

    public IReadOnlyList<HookResult> Run() => RunAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Ctrl+C, SIGTERM, SIGQUIT와 프로세스 종료 때 RequestShutdown을 부른다. 신호의 기본 종료 동작은 막으므로
    /// 메인이 WhenRequested를 기다렸다가 RunAsync 후 스스로 끝나야 한다. ProcessExit에서는 아직 안 돌았으면 훅을 동기로 돌린다.
    /// </summary>
    public void AttachProcessSignals()
    {
        lock (_lock)
        {
            if (_cancelHandler != null) return;
            _cancelHandler = (_, e) => { e.Cancel = true; RequestShutdown(); };
            Console.CancelKeyPress += _cancelHandler;
            foreach (PosixSignal sig in new[] { PosixSignal.SIGTERM, PosixSignal.SIGQUIT })
            {
                try { _registrations.Add(PosixSignalRegistration.Create(sig, ctx => { ctx.Cancel = true; RequestShutdown(); })); }
                catch (PlatformNotSupportedException) { }
            }
            _exitHandler = (_, _) => { try { Run(); } catch { } };
            AppDomain.CurrentDomain.ProcessExit += _exitHandler;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_cancelHandler != null) { Console.CancelKeyPress -= _cancelHandler; _cancelHandler = null; }
            if (_exitHandler != null) { AppDomain.CurrentDomain.ProcessExit -= _exitHandler; _exitHandler = null; }
            foreach (IDisposable r in _registrations) r.Dispose();
            _registrations.Clear();
        }
    }

    private async Task<IReadOnlyList<HookResult>> RunHooks()
    {
        List<Hook> hooks;
        lock (_lock)
        {
            hooks = new List<Hook>(_hooks);
        }
        hooks.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq));

        var results = new List<HookResult>(hooks.Count);
        long startTs = Stopwatch.GetTimestamp();
        foreach (Hook h in hooks)
        {
            long elapsedTotal = (long)Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            long remaining = _totalTimeoutMs - elapsedTotal;
            if (remaining <= 0)
            {
                LogManager.Warning("[Shutdown] 전체 제한 시간 초과로 건너뜀: {0}", h.Name);
                results.Add(new HookResult(h.Name, HookOutcome.Skipped, 0, null));
                continue;
            }

            int timeout = (int)Math.Min(h.TimeoutMs, remaining);
            long ts = Stopwatch.GetTimestamp();
            using var cts = new CancellationTokenSource();
            Task task = h.Run(cts.Token);
            HookOutcome outcome;
            Exception? error = null;
            using var delayCts = new CancellationTokenSource();       // 훅이 먼저 끝나면 타이머를 바로 풀어 준다
            try
            {
                Task winner = await Task.WhenAny(task, Task.Delay(timeout, delayCts.Token)).ConfigureAwait(false);
                if (winner == task)
                {
                    delayCts.Cancel();
                    await task.ConfigureAwait(false);
                    outcome = HookOutcome.Completed;
                }
                else
                {
                    cts.Cancel();
                    _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);    // 버려진 훅의 예외가 관찰되지 않은 채 남지 않게
                    outcome = HookOutcome.TimedOut;
                    LogManager.Warning("[Shutdown] 훅 시간 초과({0}ms), 기다리지 않고 진행: {1}", timeout, h.Name);
                }
            }
            catch (Exception ex)
            {
                outcome = HookOutcome.Failed;
                error = ex;
                LogManager.Error("[Shutdown] 훅 실패: {0}: {1}", h.Name, ex);
            }
            results.Add(new HookResult(h.Name, outcome, (long)Stopwatch.GetElapsedTime(ts).TotalMilliseconds, error));
        }
        return results;
    }
}
