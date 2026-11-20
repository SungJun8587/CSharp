//***************************************************************************
// SceneRenderer.cs : 한 프레임 전체(바닥 -> 월드 -> HUD)를 Graphics 에 그리는 진입점.
// WinForms 캔버스와 PNG 스냅샷 도구가 같은 코드를 쓴다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using GameViewer.Core;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class SceneRenderer : IDisposable
    {
        public readonly Resources Res = new Resources();
        private readonly GroundRenderer _ground = new GroundRenderer();
        private readonly Atmosphere _air;
        private readonly WeatherLayer _weather;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        /// <summary>구간별 누적 시간(ms): 바닥, 월드 밖 나무, 월드, 대기, 날씨+광원, 비네트, 긴장 연출, HUD. 성능 확인용.</summary>
        public readonly double[] PartMs = new double[8];
        public static readonly string[] PartNames = { "ground", "trees", "scene", "air", "weather+lights", "vignette", "tension", "hud" };
        public double[] SceneSubMs { get { return _world.SubMs; } }
        public double[] EffectKindMs { get { return _world.EffectKindMs; } }
        public int GroundRebuilds { get { return _ground.Rebuilds; } }
        private void Lap(int i) { PartMs[i] += _sw.Elapsed.TotalMilliseconds; _sw.Restart(); }
        private readonly WorldRenderer _world;
        private readonly HudRenderer _hud;
        private readonly TensionHud _tension;

        public SceneRenderer()
        {
            _world = new WorldRenderer(Res);
            _air = new Atmosphere(Res);
            _weather = new WeatherLayer(Res);
            _hud = new HudRenderer(Res);
            _tension = new TensionHud(Res);
        }

        public void Draw(Graphics g, ViewerSession s, AutoPilot pilot, ViewState v, long now)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            ZoneTheme.Current = ZoneTheme.For(s.ZoneId); ZoneTheme.CurrentZoneId = ZoneTheme.TerrainId(s.ZoneId); ZoneGates.Current = s.Data.Gates;
            g.Clear(ClearColor(ZoneTheme.Current));
            float shx, shy; s.World.ShakeOffset(now, out shx, out shy);
            float shk = v.Settings.ShakeScale;
            float landA, flashA; int flashR, flashG, flashB; BossStage(s, now, out landA, out flashA, out flashR, out flashG, out flashB);
            if (landA > 0f) { shx += (float)Math.Sin(now * 0.11) * landA; shy += (float)Math.Cos(now * 0.17) * landA * 0.7f; }
            g.TranslateTransform(shx * shk, shy * shk);
            Res.TextFrame();
            _sw.Restart();
            ShadowStyle.Light = v.Settings.WeatherOn ? LightRules.Shadow(_weather.Last) : LightRules.Day;
            ShadowStyle.Dark = v.Settings.WeatherOn ? WeatherRules.LightNeed(ZoneTheme.Current, _weather.Last) : 0f;
            _ground.Draw(g, v, s.Data.WorldMinX, s.Data.WorldMinY, s.Data.WorldMaxX, s.Data.WorldMaxY); Lap(0);
            _ground.DrawBorderTrees(g, v, _world.Trees, _world.Props, _world.Biome, s.Data.WorldMinX, s.Data.WorldMinY, s.Data.WorldMaxX, s.Data.WorldMaxY); Lap(1);
            _world.DrawScene(g, s, v, now); Lap(2);
            g.ResetTransform();
            if (v.Settings.DrawAir) _air.Draw(g, v, now);
            Lap(3);
            if (v.Settings.WeatherOn) { _weather.Draw(g, v, now, ZoneTheme.Current, s.ZoneId, v.Settings.ShowScreenFx); v.WeatherLabel = WeatherRules.Label(_weather.Last); _world.DrawLights(g, s, v, now, WeatherRules.LightNeed(ZoneTheme.Current, _weather.Last)); }
            else v.WeatherLabel = null;
            Lap(4);
            if (v.Settings.DrawVignette) DrawVignette(g, v);
            Lap(5);
            _tension.Draw(g, s, v, now);
            if (flashA > 0.01f && v.Settings.ShowScreenFx)
                using (SolidBrush fb = new SolidBrush(Color.FromArgb((int)(120 * flashA), flashR, flashG, flashB))) g.FillRectangle(fb, 0, 0, v.Width, v.Height);
            Lap(6);
            _hud.Draw(g, s, pilot, v, now); Lap(7);
        }

        /// <summary>보스 등장 착지(화면 흔들림 + 번쩍임)와 페이즈 변신(번쩍임)의 합. 보스는 보통 한 마리라 전체를 훑어도 싸다.</summary>
        private static void BossStage(ViewerSession s, long now, out float land, out float flash, out int r, out int gg, out int b)
        {
            land = 0f; flash = 0f; r = 255; gg = 240; b = 220;
            foreach (System.Collections.Generic.KeyValuePair<ulong, GameViewer.Core.World.Entity> kv in s.World.Entities)
            {
                GameViewer.Core.World.Entity e = kv.Value;
                if (!e.IsBoss || e.Dead) continue;
                float l = BossFx.LandShake(now, e.AppearMs);
                float f = Math.Max(BossFx.ScreenFlash(now, e.AppearMs, BossFx.LandMs), BossFx.ScreenFlash(now, e.PhaseChangedMs, 0));
                if (l > land) land = l;
                if (f > flash)
                {
                    flash = f;
                    int tr, tg, tb; BossFx.Theme(e.TemplateId, out tr, out tg, out tb);
                    r = (255 + tr) / 2; gg = (255 + tg) / 2; b = (255 + tb) / 2;
                }
            }
        }

        private static Color ClearColor(MapKind k)
        {
            switch (k)
            {
                case MapKind.Desert: return Color.FromArgb(104, 70, 46);
                case MapKind.Snow: return Color.FromArgb(78, 96, 126);
                case MapKind.Volcano: return Color.FromArgb(52, 22, 20);
                case MapKind.Swamp: return Color.FromArgb(16, 26, 20);
                case MapKind.Ruins: return Color.FromArgb(40, 38, 42);
                case MapKind.Coast: return Color.FromArgb(40, 148, 182);
                case MapKind.Crystal: return Color.FromArgb(6, 5, 14);
                case MapKind.Sky: return Color.FromArgb(236, 240, 252);
                case MapKind.Dark: return Color.FromArgb(8, 5, 14);
                default: return Color.FromArgb(18, 40, 24);
            }
        }

        private static Color VignetteColor(MapKind k)
        {
            switch (k)
            {
                case MapKind.Desert: return Color.FromArgb(110, 64, 30, 8);
                case MapKind.Snow: return Color.FromArgb(104, 24, 44, 92);
                case MapKind.Volcano: return Color.FromArgb(150, 28, 4, 2);
                case MapKind.Swamp: return Color.FromArgb(140, 4, 14, 10);
                case MapKind.Ruins: return Color.FromArgb(110, 16, 14, 18);
                case MapKind.Coast: return Color.FromArgb(70, 20, 80, 110);
                case MapKind.Crystal: return Color.FromArgb(190, 4, 2, 16);
                case MapKind.Sky: return Color.FromArgb(70, 150, 170, 230);
                case MapKind.Dark: return Color.FromArgb(180, 10, 2, 24);
                default: return Color.FromArgb(120, 4, 14, 8);
            }
        }

        /// <summary>화면 가장자리를 살짝 어둡게 해 시선을 가운데로 모은다. 가장자리 네 줄에 그라데이션만 깔아 비용이 작다.</summary>
        private void DrawVignette(Graphics g, ViewState v)
        {
            int e = Math.Max(60, Math.Min(v.Width, v.Height) / 5);
            Color dark = VignetteColor(ZoneTheme.Current), clear = Color.FromArgb(0, dark.R, dark.G, dark.B);
            using (LinearGradientBrush t = new LinearGradientBrush(new Rectangle(0, 0, v.Width, e + 1), dark, clear, LinearGradientMode.Vertical))
                g.FillRectangle(t, 0, 0, v.Width, e);
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, v.Height - e - 1, v.Width, e + 1), clear, dark, LinearGradientMode.Vertical))
                g.FillRectangle(b, 0, v.Height - e, v.Width, e);
            using (LinearGradientBrush l = new LinearGradientBrush(new Rectangle(0, 0, e + 1, v.Height), dark, clear, LinearGradientMode.Horizontal))
                g.FillRectangle(l, 0, 0, e, v.Height);
            using (LinearGradientBrush r = new LinearGradientBrush(new Rectangle(v.Width - e - 1, 0, e + 1, v.Height), clear, dark, LinearGradientMode.Horizontal))
                g.FillRectangle(r, v.Width - e, 0, e, v.Height);
        }

        public void Dispose() { _world.Dispose(); _ground.Dispose(); _air.Dispose(); Res.Dispose(); }
    }
}
