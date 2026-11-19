// PendingRequestsTests.cs : PendingRequests 단위 테스트.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Core.Net;

namespace Core.Demo;

internal static class PendingRequestsTests
{
    public static void Run(Action<string, bool> check)
    {
        long now = 1_000;
        Func<long> clock = () => Volatile.Read(ref now);

        // 응답: 정확히 한 번, 중복 응답은 무시
        using (var p = new PendingRequests<string>(clock: clock))
        {
            string? got = null; int calls = 0; PendingFailure? fail = null;
            long id = p.Register(1, 500, r => { got = r; calls++; }, f => fail = f);
            check("Pending: id 는 1 이상", id >= 1);
            check("Pending: 응답 연결", p.TryComplete(id, 1, "ok") && got == "ok" && calls == 1 && p.Count == 0);
            check("Pending: 중복 응답은 무시", !p.TryComplete(id, 1, "again") && calls == 1);
            now += 1000; p.Advance();
            check("Pending: 완료된 요청은 타임아웃 콜백이 없다", fail == null);
        }

        // 소유자 검증
        using (var p = new PendingRequests<int>(clock: clock))
        {
            int calls = 0;
            long id = p.Register(1, 500, _ => calls++);
            check("Pending: 다른 소유자의 응답은 거부", !p.TryComplete(id, 2, 7) && calls == 0 && p.Count == 1);
            check("Pending: 거부 뒤에도 원래 소유자는 완료 가능", p.TryComplete(id, 1, 7) && calls == 1);
            check("Pending: 없는 id", !p.TryComplete(999, 1, 0));
        }

        // 타임아웃
        using (var p = new PendingRequests<int>(clock: clock))
        {
            PendingFailure? f1 = null, f2 = null; int resp = 0;
            p.Register(1, 100, _ => resp++, f => f1 = f);
            p.Register(1, 1000, _ => resp++, f => f2 = f);
            now += 99; check("Pending: 기한 전에는 만료 없음", p.Advance() == 0 && f1 == null);
            now += 50;
            check("Pending: 기한이 지난 것만 Timeout", p.Advance() == 1 && f1 == PendingFailure.Timeout && f2 == null && p.Count == 1);
            check("Pending: 만료 뒤 늦은 응답은 거부", resp == 0);
            now += 2000; p.Advance();
            check("Pending: 두 번째도 Timeout, 테이블 비어 있음", f2 == PendingFailure.Timeout && p.Count == 0 && p.CountOf(1) == 0);
        }

        // Advance 가 한동안 없었어도 등록 시점부터 timeoutMs 를 보장
        using (var p = new PendingRequests<int>(clock: clock))
        {
            now += 10_000;                       // 휠은 이 시각까지 진행되지 않은 상태
            PendingFailure? f = null;
            p.Register(1, 500, _ => { }, x => f = x);
            now += 300; p.Advance();
            check("Pending: Advance 지연 후 등록해도 기한 전엔 만료 안 됨", f == null);
            now += 300; p.Advance();
            check("Pending: 기한이 지나면 만료", f == PendingFailure.Timeout);
        }

        // 소유자 종료, 취소, Dispose
        using (var p = new PendingRequests<int>(clock: clock))
        {
            var fs = new List<PendingFailure>();
            p.Register(1, 500, _ => { }, fs.Add);
            p.Register(1, 500, _ => { }, fs.Add);
            long other = p.Register(2, 500, _ => { }, fs.Add);
            check("Pending: CancelOwner 는 그 소유자 것만", p.CancelOwner(1) == 2 && fs.Count == 2 && fs.TrueForAll(x => x == PendingFailure.OwnerClosed) && p.Count == 1);
            check("Pending: Cancel", p.Cancel(other) && fs[^1] == PendingFailure.Cancelled && !p.Cancel(other));
            check("Pending: 없는 소유자 CancelOwner 는 0", p.CancelOwner(77) == 0);
        }
        {
            var p = new PendingRequests<int>(clock: clock);
            PendingFailure? f = null;
            p.Register(1, 500, _ => { }, x => f = x);
            p.Dispose(); p.Dispose();
            check("Pending: Dispose 는 Shutdown 으로 끝내고 등록을 막는다", f == PendingFailure.Shutdown && p.Register(1, 10, _ => { }) == 0);
        }

        // 소유자 한도
        using (var p = new PendingRequests<int>(maxPerOwner: 2, clock: clock))
        {
            long a = p.Register(1, 500, _ => { }), b = p.Register(1, 500, _ => { }), c = p.Register(1, 500, _ => { });
            check("Pending: 소유자 한도 초과는 0", a > 0 && b > 0 && c == 0 && p.Register(2, 500, _ => { }) > 0);
        }

        // 콜백 예외는 다른 요청에 영향이 없고 onError 로 전달
        {
            var errors = new List<Exception>();
            using var p = new PendingRequests<int>(clock: clock, onError: errors.Add);
            int ok = 0;
            p.Register(1, 100, _ => { }, _ => throw new InvalidOperationException("boom"));
            p.Register(1, 100, _ => { }, _ => ok++);
            now += 500;
            check("Pending: 콜백 예외가 있어도 나머지는 처리", p.Advance() == 2 && ok == 1 && errors.Count == 1);
        }

        // 콜백 안에서 재진입
        using (var p = new PendingRequests<int>(clock: clock))
        {
            long id = p.Register(1, 500, _ => { }, _ => { });
            int chained = 0;
            long id2 = p.Register(1, 500, _ => { chained = p.Count; });
            p.TryComplete(id2, 1, 0);
            check("Pending: 콜백 안에서 Count 호출 가능(락 밖 호출)", chained == 1);
            p.Cancel(id);
        }

        // RequestAsync
        using (var p = new PendingRequests<int>(clock: clock))
        {
            long sent = 0;
            Task<int> t = p.RequestAsync(1, 500, id => sent = id);
            p.TryComplete(sent, 1, 42);
            check("Pending: RequestAsync 응답", t.Wait(1000) && t.Result == 42);

            Task<int> t2 = p.RequestAsync(1, 100, id => { });
            now += 500; p.Advance();
            check("Pending: RequestAsync 타임아웃 예외", Failure(t2) == PendingFailure.Timeout);

            Task<int> t3 = p.RequestAsync(5, 5000, id => { });
            p.CancelOwner(5);
            check("Pending: RequestAsync 소유자 종료 예외", Failure(t3) == PendingFailure.OwnerClosed);

            bool thrown = false;
            try { p.RequestAsync(1, 500, id => throw new IOException()); } catch (IOException) { thrown = true; }
            check("Pending: 송신 실패는 그대로 전파하고 대기 항목을 남기지 않는다", thrown && p.Count == 0);

            using var cts = new CancellationTokenSource();
            Task<int> t4 = p.RequestAsync(1, 5000, id => { }, cts.Token);
            cts.Cancel();
            check("Pending: CancellationToken 취소", Cancelled(t4) && p.Count == 0);
        }

        // 동시성: 여러 스레드가 등록/응답/만료/종료를 섞어 호출해도 콜백은 요청당 정확히 한 번
        using (var p = new PendingRequests<int>(maxPerOwner: 100_000, clock: clock))
        {
            const int N = 20_000;
            int[] done = new int[N + 1];
            long[] ids = new long[N];
            int dup = 0;
            Parallel.For(0, N, i =>
            {
                int idx = i;
                ids[i] = p.Register(i % 8, 50,
                    _ => { if (Interlocked.Increment(ref done[idx]) != 1) Interlocked.Increment(ref dup); },
                    _ => { if (Interlocked.Increment(ref done[idx]) != 1) Interlocked.Increment(ref dup); });
            });
            Thread adv = new Thread(() => { for (int k = 0; k < 200; k++) { Interlocked.Add(ref now, 1); p.Advance(); Thread.Sleep(0); } });
            adv.Start();
            Parallel.For(0, N, i => { if (i % 3 == 0) p.TryComplete(ids[i], i % 8, 1); else if (i % 7 == 0) p.Cancel(ids[i]); });
            for (int o = 0; o < 4; o++) p.CancelOwner(o);
            adv.Join();
            Interlocked.Add(ref now, 10_000); p.Advance();
            int notDone = 0;
            for (int i = 0; i < N; i++) if (Volatile.Read(ref done[i]) != 1) notDone++;
            check("Pending: 동시 호출에서 콜백은 요청당 정확히 한 번(" + N + "건)", dup == 0 && notDone == 0 && p.Count == 0);
        }
    }

    private static PendingFailure? Failure(Task t)
    {
        try { t.Wait(1000); return null; }
        catch (AggregateException e) when (e.InnerException is PendingRequestException pe) { return pe.Failure; }
    }

    private static bool Cancelled(Task t)
    {
        try { t.Wait(1000); return false; }
        catch (AggregateException e) { return e.InnerException is TaskCanceledException; }
    }
}
