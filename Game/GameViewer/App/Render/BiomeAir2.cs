//***************************************************************************
// BiomeAir2.cs : 폐허(먼지) / 해안(바닷바람과 갈매기) / 수정 동굴(어둠과 빛 알갱이) / 천상(빛줄기와 깃털) / 암흑(보랏빛 안개와 혼불).
// 전부 시간의 함수(결정적).
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
        private void DrawAir2(Graphics g, ViewState v, long now, MapKind kind)
        {
            float W = v.Width, H = v.Height; double t = now / 1000.0;
            switch (kind)
            {
                case MapKind.Ruins:
                    g.FillRectangle(_res.Brush(18, 150, 140, 120), 0, 0, W, H);
                    using (LinearGradientBrush tb = new LinearGradientBrush(new RectangleF(0, 0, W, H * 0.45f + 1), Color.FromArgb(34, 255, 238, 190), Color.FromArgb(0, 255, 238, 190), LinearGradientMode.Vertical)) g.FillRectangle(tb, 0, 0, W, H * 0.45f);
                    for (int i = 0; i < 3; ++i) { double p = (t * 0.03 + i / 3.0) % 1.0; Band(g, (float)(-W * 0.5 + p * W * 2.0) - W * 0.5f, H * (0.2f + i * 0.3f), W, 80, Color.FromArgb(200, 190, 170), 22); }
                    for (int i = 0; i < 50; ++i) { uint h = Noise.Hash(i, 4, 701); double period = 6000 + (h & 1023) * 6; double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period; float x = ((h >> 4) & 1023) / 1023f * W + (float)(p * 40) + (float)Math.Sin(p * 10 + i) * 10f, y = ((h >> 14) & 1023) / 1023f * H + (float)Math.Sin(p * 8 + i * 2) * 12f; float s = 1.2f + ((h >> 20) & 3) * 0.6f; g.FillEllipse(_res.Brush(110, 230, 216, 190), x, y, s, s); }
                    break;
                case MapKind.Coast:
                    g.FillRectangle(_res.Brush(14, 255, 230, 160), 0, 0, W, H);
                    using (LinearGradientBrush tb = new LinearGradientBrush(new RectangleF(0, 0, W, H * 0.5f + 1), Color.FromArgb(46, 255, 250, 210), Color.FromArgb(0, 255, 250, 210), LinearGradientMode.Vertical)) g.FillRectangle(tb, 0, 0, W, H * 0.5f);
                    for (int i = 0; i < 24; ++i) { uint h = Noise.Hash(i, 5, 711); float tw = 0.5f + 0.5f * (float)Math.Sin(t * (1.2 + (h & 7) * 0.3) + i * 2.1); if (tw < 0.55f) continue; float x = ((h >> 4) & 1023) / 1023f * W, y = ((h >> 14) & 1023) / 1023f * H; g.DrawLine(_res.Pen((int)(150 * tw), 255, 255, 255, 1.2f), x - 4, y, x + 4, y); g.DrawLine(_res.Pen((int)(150 * tw), 255, 255, 255, 1.2f), x, y - 4, x, y + 4); }
                    for (int i = 0; i < 26; ++i) { uint h = Noise.Hash(i, 6, 713); double period = 2600 + (h & 1023) * 4; double p = ((now + (long)((h >> 10) & 0xFFFF) * 5) % (long)period) / period; float y = ((h >> 4) & 1023) / 1023f * H, len = 40 + ((h >> 14) & 127), x = -len + (float)p * (W + len * 2f); g.DrawLine(_res.Pen((int)(54 * Math.Sin(p * Math.PI)), 255, 246, 214, 1.4f), x, y, x + len, y + len * 0.05f); }
                    for (int i = 0; i < 3; ++i)      // 갈매기: 천천히 가로지르는 V 자
                    {
                        uint h = Noise.Hash(i, 7, 717); double period = 16000 + (h & 1023) * 10; double p = ((now + (long)((h >> 10) & 0xFFFF) * 11) % (long)period) / period;
                        float x = -40 + (float)p * (W + 80), y = H * (0.15f + ((h >> 4) & 255) / 255f * 0.5f) + (float)Math.Sin(p * 18 + i) * 6f, fl = (float)Math.Sin(now / 160.0 + i * 2) * 3f;
                        g.DrawCurve(_res.Pen(150, 60, 70, 80, 1.8f), new PointF[] { new PointF(x - 9, y - fl), new PointF(x, y), new PointF(x + 9, y - fl) });
                    }
                    break;
                case MapKind.Crystal:
                    g.FillRectangle(_res.Brush(46, 6, 8, 44), 0, 0, W, H);
                    for (int i = 0; i < 4; ++i) { float cx = (i + 0.5f) / 4f * W + (float)Math.Sin(t * 0.13 + i * 1.9) * W * 0.04f; Color c = i % 2 == 0 ? Color.FromArgb(110, 220, 255) : Color.FromArgb(255, 130, 230); using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(cx - 60, 0, 120, H), Color.FromArgb(0, c), Color.FromArgb(0, c), LinearGradientMode.Horizontal)) { ColorBlend cb = new ColorBlend(3); cb.Colors = new Color[] { Color.FromArgb(0, c), Color.FromArgb((int)(16 + 8 * Math.Sin(t * 0.5 + i)), c), Color.FromArgb(0, c) }; cb.Positions = new float[] { 0, 0.5f, 1 }; lb.InterpolationColors = cb; g.FillRectangle(lb, cx - 60, 0, 120, H); } }
                    for (int i = 0; i < 46; ++i)
                    {
                        uint h = Noise.Hash(i, 8, 721); float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(t * (0.2 + (h & 3) * 0.07) + i) * 40f, y = ((h >> 14) & 1023) / 1023f * H + (float)Math.Cos(t * (0.17 + ((h >> 2) & 3) * 0.06) + i * 1.7) * 30f;
                        float tw = 0.5f + 0.5f * (float)Math.Sin(t * (1.2 + (h & 7) * 0.2) + i * 2.1); int a = (int)(190 * tw * tw); if (a < 6) continue;
                        Color c = i % 2 == 0 ? Color.FromArgb(130, 240, 255) : Color.FromArgb(255, 150, 236);
                        g.FillEllipse(_res.Brush(a / 4, c.R, c.G, c.B), x - 8, y - 8, 16, 16); g.FillEllipse(_res.Brush(a, 255, 255, 255), x - 1.6f, y - 1.6f, 3.2f, 3.2f);
                    }
                    break;
                case MapKind.Sky:
                    g.FillRectangle(_res.Brush(16, 255, 240, 200), 0, 0, W, H);
                    for (int i = 0; i < 5; ++i) { float cx = (i + 0.5f) / 5f * W + (float)Math.Sin(t * 0.1 + i * 1.7) * W * 0.04f; float lean = H * 0.3f; PointF[] q = { new PointF(cx - 30 + lean * 0.4f, -20), new PointF(cx + 30 + lean * 0.4f, -20), new PointF(cx + 90 - lean * 0.6f, H), new PointF(cx - 90 - lean * 0.6f, H) }; using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(0, 0, W, H), Color.FromArgb((int)(34 + 14 * Math.Sin(t * 0.4 + i)), 255, 244, 200), Color.FromArgb(0, 255, 244, 200), LinearGradientMode.Vertical)) g.FillPolygon(lb, q); }
                    for (int i = 0; i < 3; ++i) { double p = (t * 0.025 + i / 3.0) % 1.0; Band(g, (float)(-W * 0.5 + p * W * 2.0) - W * 0.5f, H * (0.15f + i * 0.3f), W, 90, Color.White, 40); }
                    for (int i = 0; i < 30; ++i)
                    {
                        uint h = Noise.Hash(i, 9, 731); double period = 9000 + (h & 1023) * 8; double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period;
                        float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(p * 12 + i) * 26f, y = -20f + (float)p * (H + 40f), s = 2.4f + ((h >> 14) & 7) * 0.4f;
                        GraphicsState st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform((float)(p * 400) + i * 37f);
                        if (i % 3 == 0) g.FillEllipse(_res.Brush(230, 255, 255, 255), -s * 1.6f, -s * 0.35f, s * 3.2f, s * 0.7f); else g.FillEllipse(_res.Brush(220, 255, 206, 222), -s, -s * 0.5f, s * 2f, s);
                        g.Restore(st);
                    }
                    for (int i = 0; i < 18; ++i) { uint h = Noise.Hash(i, 10, 733); float tw = 0.5f + 0.5f * (float)Math.Sin(t * (1.3 + (h & 7) * 0.2) + i * 2.1); if (tw < 0.5f) continue; float x = ((h >> 4) & 1023) / 1023f * W, y = ((h >> 14) & 1023) / 1023f * H; g.FillEllipse(_res.Brush((int)(210 * tw), 255, 232, 150), x - 1.6f, y - 1.6f, 3.2f, 3.2f); }
                    break;
                default:
                    g.FillRectangle(_res.Brush(44, 30, 8, 70), 0, 0, W, H);
                    for (int i = 0; i < 5; ++i) { double p = ((t * (0.014 + i * 0.004) + i * 0.21) % 1.0); Band(g, (float)(-W * 0.6 + p * W * 2.2) - W * 0.6f, H * (0.1f + i * 0.2f) + (float)Math.Sin(t * 0.3 + i * 1.3) * 18f - 55, W * 1.2f, 110, Color.FromArgb(150, 100, 210), 38); }
                    for (int i = 0; i < 28; ++i)
                    {
                        uint h = Noise.Hash(i, 11, 741); double period = 6000 + (h & 1023) * 6; double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period;
                        float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(p * 9 + i) * 18f, y = H + 10f - (float)p * (H * 1.05f); int a = (int)(220 * Math.Sin(p * Math.PI)); if (a < 6) continue;
                        g.FillEllipse(_res.Brush(a / 4, 150, 120, 255), x - 9, y - 9, 18, 18); g.FillEllipse(_res.Brush(a, 236, 226, 255), x - 2f, y - 3.5f, 4f, 7f);
                    }
                    for (int i = 0; i < 26; ++i) { uint h = Noise.Hash(i, 12, 743); double period = 7000 + (h & 1023) * 6; double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period; float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(p * 12 + i) * 14f, y = -10f + (float)p * (H + 20f); g.FillEllipse(_res.Brush(110, 60, 40, 80), x, y, 2.2f, 2.2f); }
                    break;
            }
        }
    }
}
