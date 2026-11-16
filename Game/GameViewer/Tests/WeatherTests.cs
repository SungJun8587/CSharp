using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class WeatherTests
    {
        public static void All()
        {
            T.Check(WeatherRules.At(MapKind.Forest, 1, 123456).Rain == WeatherRules.At(MapKind.Forest, 1, 123456).Rain, "deterministic");
            // 날씨 없는 지역은 비/안개가 없다
            for (long t = 0; t < 3600000; t += 5000)
            {
                WeatherState c = WeatherRules.At(MapKind.Crystal, 8, t), v = WeatherRules.At(MapKind.Volcano, 4, t), s = WeatherRules.At(MapKind.Snow, 3, t);
                T.Check(c.Rain == 0f && c.Fog == 0f && c.Night == 0f && c.Dusk == 0f, "crystal cave has no weather or day/night");
                T.Check(v.Rain == 0f && v.Fog == 0f, "volcano has no rain/fog");
                T.Check(s.Rain == 0f && s.Storm == 0f, "snow zone does not rain");
            }
            // 값 범위와 부드러운 변화 (5초 사이 변화가 크지 않다)
            bool sawRain = false, sawFog = false, sawNight = false, sawClear = false;
            WeatherState prev = WeatherRules.At(MapKind.Forest, 1, 0);
            float maxJump = 0f;
            for (long t = 1000; t < 7200000; t += 1000)
            {
                WeatherState w = WeatherRules.At(MapKind.Forest, 1, t);
                T.Check(w.Rain >= 0f && w.Rain <= 1f && w.Fog >= 0f && w.Fog <= 1f && w.Night >= 0f && w.Night <= 1f && w.Dusk >= 0f && w.Dusk <= 1f && w.Storm <= w.Rain + 0.001f, "values in range");
                maxJump = System.Math.Max(maxJump, System.Math.Max(System.Math.Abs(w.Rain - prev.Rain), System.Math.Abs(w.Night - prev.Night)));
                if (w.Rain > 0.5f) sawRain = true; if (w.Fog > 0.5f) sawFog = true; if (w.Night > 0.8f) sawNight = true; if (!w.Any) sawClear = true;
                prev = w;
            }
            T.Check(sawRain && sawFog && sawNight && sawClear, "forest sees rain, fog, night and clear skies over two hours");
            T.Check(maxJump < 0.12f, "weather changes gradually");
            T.Check(WeatherRules.At(MapKind.Forest, 1, 0).Night < 0.05f, "game starts in daylight");
            T.Check(WeatherRules.At(MapKind.Forest, 1, 0).Night <= 1f && WeatherRules.NightAmount(MapKind.Dark) == 0f, "dark zones have no day/night");
            T.Check(WeatherRules.LightNeed(MapKind.Crystal, new WeatherState()) > 0.5f && WeatherRules.LightNeed(MapKind.Forest, new WeatherState()) == 0f, "caves are always dark, daylight needs no lamps");
            WeatherState nn = new WeatherState(); nn.Night = 0.9f;
            T.Check(WeatherRules.LightNeed(MapKind.Forest, nn) == 0.9f, "night needs lights");
            // 번개는 폭풍에서만
            float lm = 0f; for (long t = 0; t < 600000; t += 20) lm = System.Math.Max(lm, WeatherRules.Lightning(1f, 1, t));
            T.Check(lm > 0.9f, "storm lightning flashes");
            T.Check(WeatherRules.Lightning(0.2f, 1, 1000) == 0f, "no lightning without a storm");
        }
    }
}
