using System;

namespace GameViewer.Core.Logic
{
    /// <summary>한 순간의 그림자 조건. 세기/길이는 곱, Tint 는 그림자 색에 섞는 색과 비율.</summary>
    public struct ShadowLight
    {
        public float Strength;      // 짙기 곱 (1 = 맑은 낮)
        public float Length;        // 드리우는 길이 곱
        public int TintR, TintG, TintB;
        public float TintAmt;       // 0..1
    }

    /// <summary>시간대와 날씨가 그림자에 주는 영향. 해질녘엔 길고 따뜻하게, 밤엔 옅고 푸르게, 비/안개/폭풍엔 흐려서 옅고 짧게.</summary>
    public static class LightRules
    {
        public static ShadowLight Shadow(WeatherState s)
        {
            float night = Clamp(s.Night), dusk = Clamp(s.Dusk);
            float over = Math.Max(Clamp(s.Fog) * 0.9f, Math.Max(Clamp(s.Rain) * 0.8f, Clamp(s.Storm)));   // 구름 낀 정도
            ShadowLight l = new ShadowLight();
            float strength = 1f + 0.18f * dusk;
            strength *= 1f - 0.55f * over;
            strength *= 1f - 0.5f * night;
            float length = 1f + 0.9f * dusk;
            length *= 1f - 0.45f * over;
            length *= 1f - 0.25f * night;
            l.Strength = strength; l.Length = length;
            float tn = 0.75f * night, td = 0.55f * dusk * (1f - night);
            float amt = tn + td;
            l.TintAmt = Clamp(amt);
            if (amt > 0.0001f)
            {
                l.TintR = (int)((10 * tn + 70 * td) / amt);
                l.TintG = (int)((18 * tn + 24 * td) / amt);
                l.TintB = (int)((60 * tn + 16 * td) / amt);
            }
            return l;
        }

        public static ShadowLight Day { get { ShadowLight l = new ShadowLight(); l.Strength = 1f; l.Length = 1f; return l; } }

        private static float Clamp(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
    }
}
