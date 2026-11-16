// RedisRetry.cs : 연결 오류에 한해 재시도하는 공용 헬퍼.
using System;
using System.Collections.Generic;
using Core.Net;
using Core.Util;

namespace Core.Redis;

/// <summary>
/// 명령을 보내고, 응답이 "연결 오류"(<see cref="RedisHelper.IsConnectionError"/>)이면 지정한 횟수만큼 지연을 두고 재시도한다.
///
/// <para>커넥션 풀은 끊어진 커넥션을 백그라운드에서 재연결하지만, 끊기는 순간 그 커넥션에 이미 올라타 있던 요청은 재연결을 기다리지 않고
/// 에러로 끝난다. 이런 일시적 연결 오류를 호출부가 빈 결과로 오인하지 않게 이 헬퍼가 흡수한다. WRONGTYPE/NOSCRIPT/OOM 같은 서버가 돌려준
/// 에러와 정상적인 빈 결과는 재시도하지 않는다(재시도해도 같은 결과이거나, 이미 반영된 명령이 중복될 수 있다).</para>
///
/// <para><b>지연</b>: <c>scheduleDelayed</c>가 있으면 거기에 예약하고, 없고 <c>jobQueue</c>가 있으면 <see cref="JobQueue.PushAfter"/>로 예약한다.
/// 어느 쪽이든 기한이 되면 <c>jobQueue</c>(있다면)로 되돌아와 재시도한다. 둘 다 없거나 지연이 0 이하이면 지연 없이 즉시 재시도한다.</para>
///
/// <para><b>서비스 수명</b>: 재시도 상태가 서비스를 쥐고 있고 지연된 재시도는 타이머에 남아 있을 수 있다. 서비스를 해제하기 전에
/// jobQueue를 닫거나 지연 큐를 비워, 해제된 서비스로 재시도가 나가지 않게 한다.</para>
///
/// <para><b>멱등성</b>: 멱등하지 않은 명령(INCR, LPUSH 등)을 재시도하면 서버가 이미 처리한 뒤 응답만 유실된 경우 중복 반영될 수 있다.
/// 멱등한 명령이나 스크립트에만 쓴다.</para>
/// </summary>
public static class RedisRetry
{
    private sealed class RetryState
    {
        public IRedisCommandSender Sender = null!;
        public JobQueue? JobQueue;
        public Action<int, Action>? ScheduleDelayed;
        public string[] Args = Array.Empty<string>();
        public int RetryDelayMs;
        public RedisCallback? OnResult;
    }

    /// <param name="sender">명령을 보낼 서비스(기본 노드)</param>
    /// <param name="jobQueue">재시도를 되돌려 실행할 큐(생략 가능)</param>
    /// <param name="scheduleDelayed">(지연 ms, 실행할 작업)을 받는 지연 예약 함수(생략 가능, 위 설명 참고)</param>
    /// <param name="args">Redis 명령 인자</param>
    /// <param name="maxRetry">최대 재시도 횟수(0이면 한 번만 시도)</param>
    /// <param name="retryDelayMs">재시도 전 지연(ms)</param>
    /// <param name="onResult">최종 결과 콜백 — 재시도를 다 써도 연결 오류면 마지막 에러 값이 전달된다.
    /// 접수 거부(풀 없음/대기 큐 포화)도 <see cref="RedisConst.ErrSendRejected"/> 에러 값으로 전달된다.</param>
    public static void SendCommandWithRetry(IRedisCommandSender sender, JobQueue? jobQueue, Action<int, Action>? scheduleDelayed,
        IReadOnlyList<string> args, int maxRetry, int retryDelayMs, RedisCallback? onResult)
    {
        var state = new RetryState
        {
            Sender = sender,
            JobQueue = jobQueue,
            ScheduleDelayed = scheduleDelayed,
            Args = Snapshot(args),
            RetryDelayMs = retryDelayMs,
            OnResult = onResult
        };

        AttemptSend(state, Math.Max(0, maxRetry));
    }

    private static string[] Snapshot(IReadOnlyList<string> args)
    {
        var copy = new string[args.Count];
        for (int i = 0; i < copy.Length; ++i)
            copy[i] = args[i];
        return copy;
    }

    private static void AttemptSend(RetryState state, int retryLeft)
    {
        bool queued = state.Sender.SendCommand(state.Args, res =>
        {
            if (retryLeft > 0 && RedisHelper.IsConnectionError(res))
            {
                LogManager.Error("SendCommandWithRetry: 일시적 연결 오류로 재시도 예약 (남은재시도={0}, 지연={1}ms, err={2})",
                    retryLeft, state.RetryDelayMs, res.StrVal);

                ScheduleRetry(state, retryLeft - 1);
                return;
            }

            state.OnResult?.Invoke(res);
        });

        // 접수 실패(풀 없음/대기 큐 포화) 시 콜백이 오지 않으므로 에러 응답으로 마무리한다.
        if (!queued)
            state.OnResult?.Invoke(RedisHelper.MakeError(RedisConst.ErrSendRejected));
    }

    private static void ScheduleRetry(RetryState state, int retryLeft)
    {
        void Retry() => AttemptSend(state, retryLeft);

        if (state.RetryDelayMs > 0)
        {
            if (state.ScheduleDelayed != null)
            {
                JobQueue? queue = state.JobQueue;
                state.ScheduleDelayed(state.RetryDelayMs, () =>
                {
                    if (queue != null)
                        queue.Push(Retry);
                    else
                        Retry();
                });
                return;
            }

            if (state.JobQueue != null)
            {
                state.JobQueue.PushAfter(state.RetryDelayMs, Retry);
                return;
            }
        }

        Retry();
    }
}
