using System;

namespace GameViewer.Core.Logic
{
    public enum WeatherKind { Clear, Fog, Rain, Storm }

    /// <summary>한 순간의 날씨/시간대 강도. 모두 0..1.</summary>
    public struct WeatherState
    {
        public float Rain, Fog, Storm, Night, Dusk;
        public bool Any { get { return Rain > 0.01f || Fog > 0.01f || Night > 0.01f || Dusk > 0.01f; } }
    }

    /// <summary>
    /// 지역 종류와 시각만으로 정해지는 날씨와 낮밤. 서버 정보가 없으므로 뷰어가 결정적으로 만든다
    /// (같은 지역/같은 시각이면 항상 같은 결과라서 테스트할 수 있고, 구간이 바뀔 때는 서서히 섞인다).
    /// </summary>
    public static class WeatherRules
    {
        public const int SlotMs = 150000;       // 날씨 한 구간 (2분 30초)
        public const int DayMs = 720000;        // 낮밤 한 바퀴 (12분)
        public const int BlendMs = 25000;       // 구간 경계에서 섞이는 시간
        public const long StartOffsetMs = 90000;    // 시작은 한낮이 지난 오후 쪽

        public static bool Allows(MapKind k, WeatherKind w)
        {
            if (w == WeatherKind.Clear) return true;
            switch (k)
            {
                case MapKind.Forest: case MapKind.Swamp: case MapKind.Coast: return true;
                case MapKind.Ruins: return true;
                case MapKind.Snow: return w == WeatherKind.Fog;
                case MapKind.Desert: return w == WeatherKind.Fog;           // 모래 먼지로 그린다
                case MapKind.Sky: return w == WeatherKind.Fog || w == WeatherKind.Rain;
                default: return false;                                       // 화산/수정 동굴/어둠 지역은 날씨 없음
            }
        }

        /// <summary>그 지역에서 밤이 얼마나 어두워지는가(0 이면 시간대 없음: 동굴/어둠).</summary>
        public static float NightAmount(MapKind k)
        {
            switch (k)
            {
                case MapKind.Forest: case MapKind.Ruins: case MapKind.Coast: return 1f;
                case MapKind.Desert: case MapKind.Swamp: return 0.8f;
                case MapKind.Snow: return 0.7f;
                case MapKind.Sky: return 0.5f;
                case MapKind.Volcano: return 0.25f;
                default: return 0f;
            }
        }

        private static float H(uint zone, long slot, int salt)
        {
            unchecked
            {
                uint h = (uint)(zone * 2654435761u) ^ (uint)(slot * 40503) ^ (uint)(salt * 83492791);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        public static WeatherKind Pick(MapKind k, uint zone, long slot)
        {
            float r = H(zone, slot, 1);
            WeatherKind w = r < 0.50f ? WeatherKind.Clear : (r < 0.72f ? WeatherKind.Fog : (r < 0.92f ? WeatherKind.Rain : WeatherKind.Storm));
            return Allows(k, w) ? w : WeatherKind.Clear;
        }

        private static float Smooth(float t) { t = t < 0f ? 0f : (t > 1f ? 1f : t); return t * t * (3f - 2f * t); }

        private static void Add(ref WeatherState s, WeatherKind w, float amt)
        {
            if (w == WeatherKind.Fog) s.Fog += amt;
            else if (w == WeatherKind.Rain) s.Rain += amt;
            else if (w == WeatherKind.Storm) { s.Rain += amt; s.Storm += amt; }
        }

        public static WeatherState At(MapKind k, uint zone, long nowMs)
        {
            WeatherState s = new WeatherState();
            long t = nowMs + StartOffsetMs;
            long slot = t / SlotMs; long into = t % SlotMs;
            float cur = Smooth(into / (float)BlendMs);
            Add(ref s, Pick(k, zone, slot), cur);
            if (cur < 1f) Add(ref s, Pick(k, zone, slot - 1), 1f - cur);
            s.Rain = Math.Min(1f, s.Rain); s.Fog = Math.Min(1f, s.Fog); s.Storm = Math.Min(1f, s.Storm);
            s.Fog = Math.Min(1f, s.Fog * 0.85f);

            float amt = NightAmount(k);
            if (amt > 0f)
            {
                float phase = (t % DayMs) / (float)DayMs;           // 0 한낮 → 0.5 한밤
                float dist = phase < 0.5f ? phase : 1f - phase;      // 0..0.5: 한낮과의 거리
                s.Night = Smooth((dist - 0.30f) / 0.14f) * amt;
                float dusk = 1f - Math.Abs(dist - 0.30f) / 0.10f;
                s.Dusk = (dusk < 0f ? 0f : dusk) * amt * (1f - s.Night * 0.5f);
            }
            return s;
        }

        /// <summary>광원이 얼마나 필요한가 0..1. 밤에는 밤의 정도, 해가 없는 동굴/어둠 지역은 늘 어둡다.</summary>
        public static float LightNeed(MapKind k, WeatherState s)
        {
            float baseDark = (k == MapKind.Crystal || k == MapKind.Dark) ? 0.7f : 0f;
            return Math.Max(baseDark, s.Night);
        }

        /// <summary>지도 아래에 적는 짧은 상태 문구. 아무 것도 없으면 null.</summary>
        public static string Label(WeatherState s)
        {
            string tod = s.Night > 0.5f ? "밤" : (s.Dusk > 0.35f ? "해질녘" : null);
            string wx = s.Storm > 0.5f ? "폭풍" : (s.Rain > 0.5f ? "비" : (s.Fog > 0.5f ? "안개" : null));
            if (tod == null) return wx;
            return wx == null ? tod : tod + " · " + wx;
        }

        /// <summary>천둥 번개 섬광 세기 0..1. 폭풍일 때만, 몇 초에 한 번씩 짧게.</summary>
        public static float Lightning(float storm, uint zone, long nowMs)
        {
            if (storm < 0.5f) return 0f;
            long win = nowMs / 6000; long into = nowMs % 6000;
            if (H(zone, win, 7) > 0.55f) return 0f;
            long at = 600 + (long)(H(zone, win, 8) * 3500);
            long d = into - at;
            if (d < 0 || d > 420) return 0f;
            float f = 1f - d / 420f;
            if (d > 120 && d < 190) f *= 0.35f;                     // 두 번 번쩍
            return f * storm;
        }
    }
}
