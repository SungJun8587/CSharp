using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class LightTests
    {
        public static void All()
        {
            ShadowLight day = LightRules.Shadow(new WeatherState());
            T.Check(day.Strength == 1f && day.Length == 1f && day.TintAmt == 0f, "day shadow neutral");
            WeatherState d = new WeatherState(); d.Dusk = 1f;
            ShadowLight dk = LightRules.Shadow(d);
            T.Check(dk.Length > 1.6f && dk.Strength > 1f && dk.TintR > dk.TintB, "dusk long warm");
            WeatherState n = new WeatherState(); n.Night = 1f;
            ShadowLight ni = LightRules.Shadow(n);
            T.Check(ni.Strength < 0.6f && ni.Length < 1f && ni.TintB > ni.TintR, "night faint blue");
            WeatherState r = new WeatherState(); r.Rain = 1f; r.Storm = 1f; r.Fog = 1f;
            ShadowLight st = LightRules.Shadow(r);
            T.Check(st.Strength < 0.55f && st.Length < 0.6f, "overcast soft");
            bool ok = true;
            for (int i = 0; i <= 20; i++) { WeatherState w = new WeatherState(); w.Rain = i / 20f; w.Night = 1f - i / 20f; w.Dusk = i / 40f; ShadowLight x = LightRules.Shadow(w); if (x.Strength < 0f || x.Length < 0f || x.TintAmt < 0f || x.TintAmt > 1f) ok = false; }
            T.Check(ok, "shadow light bounded");
        }
    }
}
