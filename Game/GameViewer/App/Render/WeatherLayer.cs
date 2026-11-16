//***************************************************************************
// WeatherLayer.cs : 날씨와 낮밤. 비 줄기, 안개(먼지), 번개 섬광, 해질녘/밤 색조.
// 값은 Core 의 WeatherRules 가 정하고 여기서는 그리기만 한다. 전부 시간의 함수라 상태가 없고 프레임마다 메모리를 잡지 않는다.
// 화면 좌표로 HUD 보다 아래에 그린다(HUD 가독성은 그대로).
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class WeatherLayer
    {
        private readonly Resources _res;
        public WeatherLayer(Resources res) { _res = res; }

        public WeatherState Last;

        public void Draw(Graphics g, ViewState v, long now, MapKind kind, uint zone, bool screenFx)
        {
            WeatherState w = WeatherRules.At(kind, zone, now);
            Last = w;
            if (!w.Any) return;
            int W = v.Width, H = v.Height;
            if (w.Night > 0.01f)
                g.FillRectangle(_res.Brush((int)(128 * w.Night), 6, 12, 54), 0, 0, W, H);
            if (w.Dusk > 0.01f)
                g.FillRectangle(_res.Brush((int)(46 * w.Dusk), 255, 130, 50), 0, 0, W, H);
            if (w.Fog > 0.01f) DrawFog(g, W, H, now, kind, w.Fog);
            if (w.Rain > 0.01f)
            {
                g.FillRectangle(_res.Brush((int)(38 * w.Rain + 26 * w.Storm), 24, 34, 54), 0, 0, W, H);
                DrawRain(g, v, W, H, now, w.Rain, w.Storm);
            }
            float lt = screenFx ? WeatherRules.Lightning(w.Storm, zone, now) : 0f;
            if (lt > 0.01f) g.FillRectangle(_res.Brush((int)(150 * lt), 235, 240, 255), 0, 0, W, H);
        }

        private static Color FogColor(MapKind k)
        {
            switch (k)
            {
                case MapKind.Desert: return Color.FromArgb(214, 176, 110);
                case MapKind.Snow: return Color.FromArgb(226, 236, 248);
                case MapKind.Swamp: return Color.FromArgb(150, 176, 140);
                case MapKind.Ruins: return Color.FromArgb(176, 176, 186);
                default: return Color.FromArgb(206, 216, 222);
            }
        }

        private void DrawFog(Graphics g, int W, int H, long now, MapKind kind, float fog)
        {
            Color c = FogColor(kind);
            g.FillRectangle(_res.Brush(Color.FromArgb(Math.Max(0, Math.Min(255, (int)(46 * fog))), c)), 0, 0, W, H);
            // 천천히 흘러가는 큰 안개 덩어리 (아래쪽일수록 짙게)
            for (int i = 0; i < 6; ++i)
            {
                float ph = (now / (52000f + i * 9000f) + i * 0.37f) % 1f;
                float cx = -W * 0.3f + ph * W * 1.6f;
                float cy = H * (0.42f + 0.1f * i) + (float)Math.Sin(now / 4300.0 + i * 1.9) * H * 0.03f;
                float rx = W * (0.32f + 0.04f * (i % 3)), ry = H * 0.17f;
                int a = (int)(54 * fog * (0.7f + 0.3f * (float)Math.Sin(now / 3100.0 + i)));
                g.FillEllipse(_res.Brush(Color.FromArgb(Math.Max(0, Math.Min(255, a)), c)), cx - rx, cy - ry, rx * 2, ry * 2);
                g.FillEllipse(_res.Brush(Color.FromArgb(Math.Max(0, Math.Min(255, a / 2)), c)), cx - rx * 0.6f, cy - ry * 1.5f, rx * 1.2f, ry * 3f);
            }
        }

        private void DrawRain(Graphics g, ViewState v, int W, int H, long now, float rain, float storm)
        {
            int n = (int)((v.Settings.Quality == GfxQuality.High ? 170 : (v.Settings.Quality == GfxQuality.Medium ? 90 : 40)) * (0.35f + 0.65f * rain) * (1f + 0.5f * storm));
            float slant = 0.22f + 0.12f * storm;
            float len = 20f + 14f * rain + 10f * storm;
            Pen pen = _res.Pen((int)(140 + 70 * rain), 200, 218, 240, 1.6f);
            long t = now;
            for (int i = 0; i < n; ++i)
            {
                float sp = 900f + (i % 5) * 140f + 400f * storm;
                float ph = ((t * sp / 1000f / (H + 60f)) + Noise.Hash01(i, 3, 11)) % 1f;
                float y = ph * (H + 60f) - 30f;
                float x = ((float)Noise.Hash01(i, 5, 12) * (W + 120f)) - 60f - slant * y * 0.4f;
                g.DrawLine(pen, x, y, x - slant * len, y + len);
            }
            // 바닥에 튀는 빗방울
            int sp2 = n / 5;
            for (int i = 0; i < sp2; ++i)
            {
                float ph = ((now / 420f) + Noise.Hash01(i, 9, 13) * 3f) % 1f;
                if (ph > 0.6f) continue;
                float q = ph / 0.6f;
                float x = (float)Noise.Hash01(i + (int)(now / 420 % 7) * 31, 2, 14) * W, y = H * (0.45f + 0.55f * (float)Noise.Hash01(i, 7, 15));
                float r = 2f + 6f * q;
                g.DrawEllipse(_res.Pen((int)(120 * (1f - q)), 210, 225, 245, 1f), x - r, y - r * 0.35f, r * 2, r * 0.7f);
            }
        }
    }
}
