//***************************************************************************
// ZoneIntro.cs : 존에 들어갈 때의 연출 시간표 — 검은 화면에서 밝아지고, 존 이름 배너가 나타났다 사라진다.
// 그리기는 렌더러가 하고, 여기서는 now 에 대한 값(0..1)만 계산한다.
//***************************************************************************
using System;

namespace GameViewer.Core.Logic
{
    public static class ZoneIntro
    {
        public const int HoldBlackMs = 250;       // 처음 잠깐은 완전히 검다
        public const int FadeMs = 900;            // 이후 서서히 밝아진다
        public const int BannerDelayMs = 450;
        public const int BannerInMs = 450;
        public const int BannerHoldMs = 1900;
        public const int BannerOutMs = 700;
        public const int TotalMs = BannerDelayMs + BannerInMs + BannerHoldMs + BannerOutMs;

        /// <summary>시작 시각 startMs 기준, now 의 검은 막 불투명도 0..1 (startMs &lt; 0 이면 연출 없음 = 0).</summary>
        public static float BlackAlpha(long startMs, long now)
        {
            if (startMs < 0) return 0f;
            long age = now - startMs;
            if (age < 0) return 1f;
            if (age < HoldBlackMs) return 1f;
            if (age >= HoldBlackMs + FadeMs) return 0f;
            float t = (age - HoldBlackMs) / (float)FadeMs;
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>존 이름 배너의 불투명도 0..1.</summary>
        public static float BannerAlpha(long startMs, long now)
        {
            if (startMs < 0) return 0f;
            long age = now - startMs - BannerDelayMs;
            if (age < 0 || age >= BannerInMs + BannerHoldMs + BannerOutMs) return 0f;
            if (age < BannerInMs) return age / (float)BannerInMs;
            if (age < BannerInMs + BannerHoldMs) return 1f;
            return 1f - (age - BannerInMs - BannerHoldMs) / (float)BannerOutMs;
        }

        /// <summary>배너 글자가 오른쪽에서 미끄러져 들어오는 정도 0..1(1 = 제자리).</summary>
        public static float BannerSlide(long startMs, long now)
        {
            long age = now - startMs - BannerDelayMs;
            if (startMs < 0 || age < 0) return 0f;
            if (age >= BannerInMs) return 1f;
            float t = age / (float)BannerInMs;
            return 1f - (1f - t) * (1f - t);
        }

        public static bool Active(long startMs, long now) { return startMs >= 0 && now - startMs < TotalMs; }

        /// <summary>존 소개 문구(배너 아래 한 줄).</summary>
        public static string Tagline(MapKind k)
        {
            switch (k)
            {
                case MapKind.Forest: return "속삭이는 숲이 길을 연다";
                case MapKind.Desert: return "뜨거운 모래바람이 불어온다";
                case MapKind.Snow: return "끝없는 눈보라가 시야를 가린다";
                case MapKind.Volcano: return "용암이 대지를 가르고 흐른다";
                case MapKind.Swamp: return "안개 낀 늪이 발목을 붙잡는다";
                case MapKind.Ruins: return "잊힌 제국의 돌 위를 걷는다";
                case MapKind.Coast: return "파도가 부서지는 해안선";
                case MapKind.Crystal: return "수정이 어둠 속에서 빛난다";
                case MapKind.Sky: return "구름 위, 천상의 정원";
                case MapKind.Dark: return "빛이 닿지 않는 저주의 땅";
                default: return "";
            }
        }
    }
}
