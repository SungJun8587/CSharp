//***************************************************************************
// ReconnectPolicy.cs : 연결이 끊겼을 때 자동 재접속 시점을 정한다(지수 백오프, 최대 횟수).
// 시계를 직접 읽지 않고 now 를 인자로 받아서 테스트가 쉽다.
//***************************************************************************
using System;

namespace GameViewer.Core.Logic
{
    public sealed class ReconnectPolicy
    {
        public static readonly int[] DelaysMs = { 1000, 2000, 4000, 8000, 10000 };

        public int Attempt { get; private set; }            // 지금까지 시도 예약한 횟수
        public long DueAtMs { get; private set; }           // 다음 시도 시각(대기 중일 때만 의미)
        public bool Waiting { get; private set; }

        public int MaxAttempts { get { return DelaysMs.Length; } }
        public bool Exhausted { get { return Attempt >= MaxAttempts && !Waiting; } }

        /// <summary>연결 실패/끊김이 났다. 시도가 남았으면 다음 시각을 예약하고 true, 다 썼으면 false.</summary>
        public bool OnFailure(long now)
        {
            if (Attempt >= MaxAttempts) { Waiting = false; return false; }
            DueAtMs = now + DelaysMs[Attempt];
            ++Attempt;
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
        public void Reset() { Attempt = 0; Waiting = false; DueAtMs = 0; }

        /// <summary>사용자가 직접 재접속을 눌렀다(F5): 처음부터 다시 센다.</summary>
        public void Manual() { Reset(); }
    }
}
