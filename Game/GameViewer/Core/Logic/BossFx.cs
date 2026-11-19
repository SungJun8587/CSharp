using System;

namespace GameViewer.Core.Logic
{
    /// <summary>보스 연출의 순수 규칙: 등장(빛기둥에서 내려옴), 페이즈 변신(커졌다 돌아오며 붉게), 사망 폭발.</summary>
    public static class BossFx
    {
        public const int EntranceMs = 1400;
        public const int PhaseMs = 1100;
        public const int DeathMs = 1800;

        /// <summary>등장 진행도 0..1 (끝났거나 시작 전이면 1).</summary>
        public static float Entrance(long now, long appearMs)
        {
            long age = now - appearMs;
            if (age < 0 || age >= EntranceMs) return 1f;
            return age / (float)EntranceMs;
        }

        /// <summary>등장 중 몸 투명도: 앞 35% 는 보이지 않고 이후 빠르게 나타난다.</summary>
        public static float EntranceAlpha(float p)
        {
            if (p >= 1f) return 1f;
            float a = (p - 0.35f) / 0.4f;
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }

        /// <summary>등장 중 위에서 떨어지는 높이(몸 높이 배수). 0.35~0.75 구간에서 빠르게 착지한다.</summary>
        public static float EntranceDrop(float p)
        {
            if (p >= 0.75f) return 0f;
            if (p <= 0.35f) return 1.2f;
            float t = (p - 0.35f) / 0.4f;
            return 1.2f * (1f - t * t);
        }

        /// <summary>페이즈 변신 진행도 0..1. 해당 없으면 -1.</summary>
        public static float Phase(long now, long changedMs)
        {
            long age = now - changedMs;
            if (age < 0 || age >= PhaseMs) return -1f;
            return age / (float)PhaseMs;
        }

        /// <summary>변신 중 몸 크기 배율: 웅크렸다 부풀어 오르고 1.0 으로 돌아온다. 페이즈가 높을수록 조금 더 커진다.</summary>
        public static float PhaseScale(float p, int phase)
        {
            if (p < 0f) return 1f;
            float amp = 0.08f + 0.03f * Math.Min(4, Math.Max(1, phase));
            if (p < 0.25f) return 1f - 0.05f * (p / 0.25f);
            float t = (p - 0.25f) / 0.75f;
            return 1f + amp * (float)Math.Sin(Math.PI * Math.Min(1f, t * 1.15f)) * (1f - 0.3f * t);
        }

        /// <summary>변신 중 붉은 색조 세기 0..1.</summary>
        public static float PhaseTint(float p)
        {
            if (p < 0f) return 0f;
            return p < 0.5f ? p * 2f : (1f - p) * 2f;
        }

        /// <summary>페이즈에 따른 영구 색조 세기 (1페이즈 0, 이후 단계마다 조금씩). 격노와 겹치지 않게 최대 0.35.</summary>
        public static float PhaseAura(int phase)
        {
            return Math.Min(0.35f, Math.Max(0, phase - 1) * 0.12f);
        }

        /// <summary>사망 폭발 단계: 0..0.25 수축 후 섬광, 이후 빛기둥과 파편.</summary>
        public static float DeathFlash(float p)
        {
            if (p < 0.12f) return p / 0.12f;
            if (p < 0.4f) return 1f - (p - 0.12f) / 0.28f;
            return 0f;
        }

        public static float DeathPillar(float p)
        {
            if (p < 0.1f) return 0f;
            if (p < 0.45f) return (p - 0.1f) / 0.35f;
            return 1f - (p - 0.45f) / 0.55f;
        }

        public const int LandMs = 1050;           // 등장 시작 후 착지하는 시각(EntranceDrop 이 0 이 되는 지점)
        public const int LandAfterMs = 1300;      // 착지 뒤 바닥 균열이 남아 있는 시간

        /// <summary>보스 종류별 고유색(빛기둥, 마법진, 변신 섬광). 종류마다 다른 색이 있고(Id%100 기준), 그 밖의 보스는 금빛/붉은빛이다.</summary>
        public static void Theme(uint tpl, out int r, out int g, out int b)
        {
            switch (HasTheme(tpl) ? (int)(tpl % 100) : 0)
            {
                case 2: r = 150; g = 200; b = 70; break;       // 두꺼비: 늪 연두
                case 3: r = 170; g = 110; b = 255; break;      // 박쥐: 보라
                case 4: r = 255; g = 120; b = 40; break;       // 도마뱀: 용암 주황
                case 6: r = 90; g = 190; b = 240; break;       // 소라게: 바다색
                case 8: r = 120; g = 235; b = 90; break;       // 독거미: 독초록
                case 10: r = 255; g = 200; b = 60; break;      // 전갈: 호박색
                case 11: r = 235; g = 150; b = 235; break;     // 사마귀: 수정 분홍
                case 13: r = 170; g = 215; b = 255; break;     // 해골: 창백한 청백
                case 15: r = 100; g = 235; b = 235; break;     // 유령: 청록
                case 21: r = 180; g = 225; b = 255; break;     // 설인: 얼음색
                case 22: r = 255; g = 150; b = 60; break;      // 골렘: 주황(용암빛)
                default: r = 255; g = 215; b = 130; break;
            }
        }

        public static bool HasTheme(uint tpl)
        {
            switch ((int)(tpl % 100)) { case 2: case 3: case 4: case 6: case 8: case 10: case 11: case 13: case 15: case 21: case 22: return true; default: return false; }
        }

        /// <summary>착지 순간의 화면 흔들림 세기(px). 착지 후 450ms 동안 줄어든다.</summary>
        public static float LandShake(long now, long appearMs)
        {
            long age = now - appearMs - LandMs;
            if (age < 0 || age >= 450) return 0f;
            float k = 1f - age / 450f;
            return 11f * k * k;
        }

        /// <summary>착지 뒤 바닥 균열 진행도 0..1 (해당 없으면 -1).</summary>
        public static float Crack(long now, long appearMs)
        {
            long age = now - appearMs - LandMs;
            if (age < 0 || age >= LandAfterMs) return -1f;
            return age / (float)LandAfterMs;
        }

        /// <summary>착지/변신 순간 화면 전체가 번쩍이는 세기 0..1 (짧게 감쇠).</summary>
        public static float ScreenFlash(long now, long startMs, long delayMs)
        {
            long age = now - startMs - delayMs;
            if (age < 0 || age >= 260) return 0f;
            float k = 1f - age / 260f;
            return k * k;
        }
    }
}
