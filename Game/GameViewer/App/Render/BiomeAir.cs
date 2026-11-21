//***************************************************************************
// BiomeAir.cs : 설원 / 화산 / 늪의 공기. 눈보라, 솟는 불씨와 재, 흐르는 안개와 포자.
// 전부 시간의 함수(결정적). 화면 좌표로 그리므로 카메라가 움직여도 하늘 쪽 효과가 따라다니지 않는다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed partial class Atmosphere
    {
        private void DrawBiomeAir(IGfx g, ViewState v, long now, MapKind kind)
        {
            if (kind == MapKind.Snow) DrawSnowAir(g, v, now);
            else if (kind == MapKind.Volcano) DrawVolcanoAir(g, v, now);
            else if (kind == MapKind.Swamp) DrawSwampAir(g, v, now);
            else DrawAir2(g, v, now, kind);
        }

        private void Band(IGfx g, float x, float y, float w, float h, Color c, int peak)
        {
            using (LinearGradientBrush b = GfxBrush.Linear(new RectangleF(x, y, w, h + 1), Color.FromArgb(0, c), Color.FromArgb(0, c), LinearGradientMode.Vertical))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Color.FromArgb(0, c), Color.FromArgb(peak, c), Color.FromArgb(0, c) };
                cb.Positions = new float[] { 0f, 0.5f, 1f };
                b.InterpolationColors = cb;
                g.FillRectangle(b, x, y, w, h);
            }
        }

        // ---------------------------------------------------------------- 설원: 차가운 푸른 색조, 비스듬히 날리는 눈, 바람에 쓸리는 눈 안개
        private void DrawSnowAir(IGfx g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            double t = now / 1000.0;
            g.FillRectangle(_res.Brush(18, 150, 190, 255), 0, 0, W, H);
            using (LinearGradientBrush tb = GfxBrush.Linear(new RectangleF(0, 0, W, H * 0.4f + 1), Color.FromArgb(40, 230, 244, 255), Color.FromArgb(0, 230, 244, 255), LinearGradientMode.Vertical))
                g.FillRectangle(tb, 0, 0, W, H * 0.4f);
            // 바람에 쓸려 가는 눈 안개
            for (int i = 0; i < 3; ++i)
            {
                double p = ((t * 0.04 + i / 3.0) % 1.0);
                float x = (float)(-W * 0.5 + p * W * 2.0), yy = H * (0.2f + i * 0.28f) + (float)Math.Sin(t * 0.5 + i) * 12f;
                Band(g, x - W * 0.5f, yy - 40, W, 80, Color.FromArgb(240, 248, 255), 26);
            }
            // 눈송이: 바람에 날려 비스듬히, 가까운 것은 크고 빠르다
            for (int i = 0; i < 90; ++i)
            {
                uint h = Noise.Hash(i, 4, 511);
                float depth = 0.4f + ((h >> 3) & 7) / 7f * 0.9f;            // 0.4~1.3
                double period = 5200 / depth + (h & 1023) * 2;
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 7) % (long)period) / period;
                float x = ((h >> 4) & 1023) / 1023f * (W + 240) - 120f + (float)(p * W * 0.34f * depth) + (float)Math.Sin(p * 18 + i) * 8f * depth;
                float y = -20f + (float)p * (H + 40f);
                float s = 1.2f + depth * 1.9f;
                g.FillEllipse(_res.Brush((int)(120 + 110 * (depth - 0.4f)), 255, 255, 255), x, y, s, s);
                if (depth > 1.0f) g.FillEllipse(_res.Brush(40, 255, 255, 255), x - s, y - s, s * 3, s * 3);
            }
            // 가끔 지나가는 강한 바람 줄기
            for (int i = 0; i < 12; ++i)
            {
                uint h = Noise.Hash(i, 5, 521);
                double period = 2200 + (h & 1023) * 3;
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 5) % (long)period) / period;
                float y = ((h >> 4) & 1023) / 1023f * H, len = 60 + ((h >> 14) & 127);
                float x = -len + (float)p * (W + len * 2f);
                g.DrawLine(_res.Pen((int)(46 * Math.Sin(p * Math.PI)), 255, 255, 255, 1.4f), x, y, x + len, y + len * 0.18f);
            }
        }

        // ---------------------------------------------------------------- 화산: 붉은 열기, 솟는 불씨, 떨어지는 재, 일렁이는 아지랑이
        private void DrawVolcanoAir(IGfx g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            double t = now / 1000.0;
            float pulse = 0.65f + 0.35f * (float)Math.Sin(t * 0.8);
            g.FillRectangle(_res.Brush((int)(22 + 10 * pulse), 255, 70, 20), 0, 0, W, H);
            using (LinearGradientBrush bb = GfxBrush.Linear(new RectangleF(0, H * 0.55f, W, H * 0.45f + 1), Color.FromArgb(0, 255, 80, 20), Color.FromArgb((int)(52 * pulse + 10), 255, 80, 20), LinearGradientMode.Vertical))
                g.FillRectangle(bb, 0, H * 0.55f, W, H * 0.45f);
            // 아지랑이
            for (int i = 0; i < 4; ++i)
            {
                float yy = H * (0.2f + i * 0.2f) + (float)Math.Sin(t * 0.9 + i * 1.7) * 14f;
                Band(g, 0, yy - 20, W, 40, Color.FromArgb(255, 170, 110), 14);
            }
            // 연기 띠
            for (int i = 0; i < 3; ++i)
            {
                double p = ((t * 0.03 + i / 3.0) % 1.0);
                float x = (float)(-W * 0.4 + p * W * 1.8), yy = H * (0.15f + i * 0.3f) + (float)Math.Sin(t * 0.4 + i) * 14f;
                Band(g, x - W * 0.5f, yy - 50, W, 100, Color.FromArgb(30, 24, 26), 40);
            }
            // 솟는 불씨
            for (int i = 0; i < 55; ++i)
            {
                uint h = Noise.Hash(i, 6, 531);
                double period = 3600 + (h & 1023) * 4;
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period;
                float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(p * 9 + i) * 16f + (float)(p * 30);
                float y = H + 10f - (float)p * (H * 0.9f);
                float s = 1.4f + ((h >> 14) & 3) * 0.7f;
                float f = (float)(1 - p);
                g.FillEllipse(_res.Brush((int)(46 * f), 255, 120, 30), x - s * 2.4f, y - s * 2.4f, s * 5f, s * 5f);
                g.FillEllipse(_res.Brush((int)(240 * f), 255, (int)(150 + 90 * f), 60), x - s * 0.5f, y - s * 0.5f, s, s);
            }
            // 떨어지는 재
            for (int i = 0; i < 45; ++i)
            {
                uint h = Noise.Hash(i, 7, 541);
                double period = 7000 + (h & 1023) * 6;
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period;
                float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(p * 12 + i) * 14f - (float)(p * 40);
                float y = -10f + (float)p * (H + 20f);
                float s = 1.4f + ((h >> 14) & 3) * 0.6f;
                g.FillEllipse(_res.Brush(140, 170, 160, 160), x, y, s * 1.5f, s);
            }
        }

        // ---------------------------------------------------------------- 늪: 탁한 녹색 색조, 흐르는 안개, 떠다니는 포자와 반딧불
        private void DrawSwampAir(IGfx g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            double t = now / 1000.0;
            g.FillRectangle(_res.Brush(26, 40, 90, 50), 0, 0, W, H);
            using (LinearGradientBrush tb = GfxBrush.Linear(new RectangleF(0, 0, W, H * 0.5f + 1), Color.FromArgb(40, 170, 200, 120), Color.FromArgb(0, 170, 200, 120), LinearGradientMode.Vertical))
                g.FillRectangle(tb, 0, 0, W, H * 0.5f);
            using (LinearGradientBrush bb = GfxBrush.Linear(new RectangleF(0, H * 0.6f, W, H * 0.4f + 1), Color.FromArgb(0, 190, 220, 190), Color.FromArgb(40, 190, 220, 190), LinearGradientMode.Vertical))
                g.FillRectangle(bb, 0, H * 0.6f, W, H * 0.4f);
            // 안개 띠: 느리게 흐른다
            for (int i = 0; i < 5; ++i)
            {
                double p = ((t * (0.012 + i * 0.004) + i * 0.21) % 1.0);
                float x = (float)(-W * 0.6 + p * W * 2.2), yy = H * (0.1f + i * 0.2f) + (float)Math.Sin(t * 0.3 + i * 1.3) * 18f;
                Band(g, x - W * 0.6f, yy - 55, W * 1.2f, 110, Color.FromArgb(206, 226, 200), 34);
            }
            // 반딧불과 포자
            for (int i = 0; i < 30; ++i)
            {
                uint h = Noise.Hash(i, 8, 551);
                float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(t * (0.2 + (h & 3) * 0.07) + i) * 44f;
                float y = ((h >> 14) & 1023) / 1023f * H + (float)Math.Cos(t * (0.17 + ((h >> 2) & 3) * 0.06) + i * 1.7) * 30f;
                float tw = 0.5f + 0.5f * (float)Math.Sin(t * (1.2 + (h & 7) * 0.2) + i * 2.1);
                bool green = i % 3 != 0;
                int a = (int)(170 * tw * tw);
                if (a < 6) continue;
                if (green) { g.FillEllipse(_res.Brush(a / 4, 150, 255, 150), x - 8, y - 8, 16, 16); g.FillEllipse(_res.Brush(a, 200, 255, 190), x - 1.8f, y - 1.8f, 3.6f, 3.6f); }
                else { g.FillEllipse(_res.Brush(a / 3, 120, 240, 220), x - 7, y - 7, 14, 14); g.FillEllipse(_res.Brush(a, 180, 255, 240), x - 1.5f, y - 1.5f, 3f, 3f); }
            }
            // 날벌레 떼처럼 흩날리는 작은 점
            for (int i = 0; i < 24; ++i)
            {
                uint h = Noise.Hash(i, 9, 561);
                double period = 6000 + (h & 1023) * 5;
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period;
                float x = (float)p * (W + 60f) - 30f + (float)Math.Sin(p * 15 + i) * 10f, y = ((h >> 4) & 1023) / 1023f * H + (float)Math.Sin(p * 11 + i * 2) * 16f;
                g.FillEllipse(_res.Brush(90, 210, 230, 170), x, y, 1.6f, 1.6f);
            }
        }
    }
}
