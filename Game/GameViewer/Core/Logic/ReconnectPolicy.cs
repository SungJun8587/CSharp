//***************************************************************************
// ReconnectPolicy.cs : 연결이 끊겼을 때 자동 재접속 시점을 정한다(지수 백오프, 최대 횟수).
// 대기 시간 계산은 라이브러리 Core.Util.BackoffState 가 맡고, 이 클래스는 최대 횟수와 "대기 중" 상태를 더한다.
// 시계를 직접 읽지 않고 now 를 인자로 받아서 테스트가 쉽다.
//***************************************************************************
using System;
using BackoffState = global::Core.Util.BackoffState;

namespace GameViewer.Core.Logic
{
    public sealed class ReconnectPolicy
    {
        private const int BaseMs = 1000, CapMs = 10000;

        /// <summary>시도 순번별 대기 시간(1, 2, 4, 8, 10초). 표시용이며 BackoffState 와 같은 식으로 구한다.</summary>
        public static readonly int[] DelaysMs = MakeDelays(5);

        private readonly BackoffState _backoff = new BackoffState(BaseMs, CapMs);

        public int Attempt { get { return _backoff.FailCount; } }       // 지금까지 시도 예약한 횟수
        public long DueAtMs { get { return _backoff.NextRetryMs; } }    // 다음 시도 시각(대기 중일 때만 의미)
        public bool Waiting { get; private set; }

        public int MaxAttempts { get { return DelaysMs.Length; } }
        public bool Exhausted { get { return Attempt >= MaxAttempts && !Waiting; } }

        private static int[] MakeDelays(int count)
        {
            int[] d = new int[count];
            for (int i = 0; i < count; ++i) d[i] = (int)BackoffState.ComputeDelayMs(i + 1, BaseMs, CapMs, 30);
            return d;
        }

        /// <summary>연결 실패/끊김이 났다. 시도가 남았으면 다음 시각을 예약하고 true, 다 썼으면 false.</summary>
        public bool OnFailure(long now)
        {
            if (Attempt >= MaxAttempts) { Waiting = false; return false; }
            _backoff.OnFail(now);
            Waiting = true;
            return true;
        }

        /// <summary>대기 시간이 지났다면 true 를 돌려주고 대기를 끝낸다(호출자가 이때 실제 재접속을 시작한다).</summary>
        public bool TryTake(long now)
        {
            if (!Waiting || now < DueAtMs) return false;
            Waiting = false;
            return true;
        }

        public long RemainingMs(long now) { return Waiting ? Math.Max(0, DueAtMs - now) : 0; }

        /// <summary>접속에 성공했다 — 횟수를 되돌린다.</summary>
        public void Reset() { _backoff.Reset(); Waiting = false; }

        /// <summary>사용자가 직접 재접속을 눌렀다(F5): 처음부터 다시 센다.</summary>
        public void Manual() { Reset(); }
    }
}
