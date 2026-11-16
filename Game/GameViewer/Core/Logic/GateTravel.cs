//***************************************************************************
// GateTravel.cs : 존 관문을 넘는 순서(UI 비의존). 영웅이 관문 안쪽 가장자리에 닿으면 화면을 어둡게 하고(FadeOutMs),
// 다 어두워지면 건너편 존으로 바꾸라고 알려 준다. 도착 뒤에는 CooldownMs 동안 관문이 다시 반응하지 않는다(되돌아가기 방지).
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.Logic
{
    public enum GateStep { None, Started, Switch }

    public sealed class GateTravel
    {
        public const float Depth = 3f;
        public const int FadeOutMs = 450;
        public const int CooldownMs = 3000;

        public ZoneGate Pending { get; private set; }
        private long _startMs, _cooldownUntil;

        /// <summary>한 틱: 관문에 닿으면 Started, 어두워지기가 끝나면 Switch(그 뒤 Arrived 를 불러야 한다).</summary>
        public GateStep Update(long now, IList<ZoneGate> gates, float x, float y, float minX, float minY, float maxX, float maxY, bool alive)
        {
            if (Pending != null) return now - _startMs >= FadeOutMs ? GateStep.Switch : GateStep.None;
            if (!alive || now < _cooldownUntil) return GateStep.None;
            ZoneGate g = ZoneGates.Entered(gates, x, y, minX, minY, maxX, maxY, Depth);
            if (g == null) return GateStep.None;
            Pending = g; _startMs = now;
            return GateStep.Started;
        }

        /// <summary>건너편 존으로 바꾼 뒤(또는 바꾸지 못하고 포기한 뒤) 호출: 대기를 끝내고 잠시 쉰다.</summary>
        public void Arrived(long now) { Pending = null; _cooldownUntil = now + CooldownMs; }

        /// <summary>화면을 덮는 검정의 세기 0..1 (대기 중이 아니면 0). 끝으로 갈수록 빠르게 어두워진다.</summary>
        public float Fade(long now)
        {
            if (Pending == null) return 0f;
            float t = (now - _startMs) / (float)FadeOutMs;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t;
        }
    }
}
