//***************************************************************************
// ShadowStyle.cs : 그림자 색과 짙기를 존 분위기에 맞춘다. 캐릭터, 나무, 소품이 같은 색과 같은 방향(빛은 왼쪽 위, 그림자는 오른쪽 아래)을 쓴다.
//***************************************************************************
using System;
using System.Drawing;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public static class ShadowStyle
    {
        /// <summary>지금 프레임의 시간대/날씨가 그림자에 주는 영향. SceneRenderer 가 장면을 그리기 전에 정한다.</summary>
        public static ShadowLight Light = LightRules.Day;
        /// <summary>지금 얼마나 어두운가 0..1 (밤/동굴). 스킬 빛 같은 광원 효과의 세기에 쓴다.</summary>
        public static float Dark;

        /// <summary>존 색에 시간대/날씨를 얹은 그림자 색과 짙기, 그리고 길이 곱.</summary>
        public static void Lit(out int r, out int g, out int b, out float mul, out float lenMul)
        {
            Tone(ZoneTheme.Current, out r, out g, out b, out mul);
            ShadowLight l = Light;
            if (l.TintAmt > 0f)
            {
                r = (int)(r + (l.TintR - r) * l.TintAmt); g = (int)(g + (l.TintG - g) * l.TintAmt); b = (int)(b + (l.TintB - b) * l.TintAmt);
            }
            mul *= l.Strength; lenMul = l.Length;
        }

        /// <summary>존 분위기에 맞는 그림자 색과 짙기. 눈/하늘은 푸르고 옅게, 사막/해안은 따뜻한 갈색, 화산/어둠/수정은 짙고 검게, 숲/늪은 초록 검정.</summary>
        public static void Tone(MapKind k, out int r, out int g, out int b, out float mul)
        {
            switch (k)
            {
                case MapKind.Snow: r = 30; g = 46; b = 92; mul = 0.75f; break;
                case MapKind.Sky: r = 70; g = 84; b = 140; mul = 0.60f; break;
                case MapKind.Desert: r = 70; g = 36; b = 12; mul = 0.85f; break;
                case MapKind.Coast: r = 40; g = 44; b = 24; mul = 0.80f; break;
                case MapKind.Volcano: r = 20; g = 4; b = 2; mul = 1.15f; break;
                case MapKind.Dark: r = 6; g = 0; b = 14; mul = 1.25f; break;
                case MapKind.Crystal: r = 8; g = 4; b = 30; mul = 1.20f; break;
                case MapKind.Swamp: r = 2; g = 14; b = 8; mul = 1.10f; break;
                case MapKind.Ruins: r = 14; g = 12; b = 18; mul = 1.0f; break;
                default: r = 4; g = 16; b = 8; mul = 1.0f; break;
            }
        }

        /// <summary>바닥에 놓인 물체의 그림자: 밑동 타원 두 겹 + 오른쪽 아래로 길게 드리우는 두 겹. half 는 밑동 반폭, cast 는 드리우는 길이(둘 다 화면 픽셀), a 는 기본 알파.</summary>
        public static void Object(Graphics g, Resources res, float x, float y, float half, float cast, float a)
        {
            int r, gg, b; float mul, lm; Lit(out r, out gg, out b, out mul, out lm);
            a *= mul; cast *= lm;
            if (cast > 2f)
            {
                g.FillEllipse(res.Brush((int)Math.Min(255f, a * 0.8f), r, gg, b), x - half * 0.9f + cast * 0.15f, y - half * 0.30f + cast * 0.05f, half * 1.8f + cast, half * 0.62f);
                g.FillEllipse(res.Brush((int)Math.Min(255f, a * 1.1f), r, gg, b), x - half * 0.7f + cast * 0.1f, y - half * 0.22f, half * 1.4f + cast * 0.6f, half * 0.46f);
            }
            g.FillEllipse(res.Brush((int)Math.Min(255f, a), r, gg, b), x - half * 1.05f, y - half * 0.28f, half * 2.1f, half * 0.86f);
            g.FillEllipse(res.Brush((int)Math.Min(255f, a * 1.2f), r, gg, b), x - half * 0.6f, y - half * 0.16f, half * 1.2f, half * 0.52f);
        }
    }
}
