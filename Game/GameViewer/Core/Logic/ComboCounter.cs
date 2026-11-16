//***************************************************************************
// ComboCounter.cs : 내가 적을 연속으로 때리는 횟수(콤보). 일정 시간 안에 다음 타격이 없으면 끊긴다.
// KillStreak(연속 처치)와 달리 "타격" 단위다. 화면에는 N HIT 로 보여준다.
//***************************************************************************
using System;

namespace GameViewer.Core.Logic
{
    public sealed class ComboCounter
    {
        public const long WindowMs = 2200;

        private int _count;
        private long _lastMs = -100000;
        private long _total;
        public int Best { get; private set; }

        /// <summary>타격 한 번 등록. 피해량도 콤보 합계에 쌓는다. 돌려주는 값 = 현재 콤보 수.</summary>
        public int Hit(long now, int damage)
        {
            if (now - _lastMs > WindowMs) { _count = 0; _total = 0; }
            ++_count; _total += damage; _lastMs = now;
            if (_count > Best) Best = _count;
            return _count;
        }

        /// <summary>now 시각에 살아 있는 콤보 수(끊겼으면 0).</summary>
        public int Active(long now) { return now - _lastMs <= WindowMs ? _count : 0; }
        public long TotalDamage(long now) { return Active(now) > 0 ? _total : 0; }

        /// <summary>남은 시간 비율 1..0.</summary>
        public float Remaining(long now)
        {
            long age = now - _lastMs;
            if (age < 0) return 1f;
            if (age >= WindowMs) return 0f;
            return 1f - age / (float)WindowMs;
        }

        /// <summary>마지막 타격 직후 잠깐 숫자가 커졌다 줄어드는 정도 0..1.</summary>
        public float Pop(long now)
        {
            long age = now - _lastMs;
            if (age < 0 || age > 220) return 0f;
            return 1f - age / 220f;
        }

        /// <summary>콤보 단계(표시 색/크기): 0 기본, 1 10+, 2 25+, 3 50+.</summary>
        public static int Tier(int count) { return count >= 50 ? 3 : (count >= 25 ? 2 : (count >= 10 ? 1 : 0)); }

        public void Reset() { _count = 0; _total = 0; _lastMs = -100000; }
    }
}
