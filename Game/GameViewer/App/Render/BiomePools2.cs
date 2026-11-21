//***************************************************************************
// BiomePools2.cs : 폐허의 분수 / 해안의 조수 웅덩이 / 수정 동굴의 빛나는 지하 호수 / 천상의 빛의 샘 / 암흑의 저주받은 웅덩이.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed partial class BiomePools
    {
        private void Pool2(IGfx g, MapKind kind, float cx, float cy, float rx, float ry, uint seed, int index, double t, float x0, float y0, float w, float h)
        {
            Color bank, rim, wc, we, halo; float wob; int haloA;
            switch (kind)
            {
                case MapKind.Ruins: bank = Color.FromArgb(60, 0, 0, 0); rim = Color.FromArgb(150, 144, 132); wc = Color.FromArgb(34, 90, 84); we = Color.FromArgb(76, 140, 110); halo = Color.Empty; wob = 0.08f; haloA = 0; break;
                case MapKind.Coast: bank = Color.FromArgb(60, 100, 70, 30); rim = Color.FromArgb(170, 146, 104); wc = Color.FromArgb(40, 168, 190); we = Color.FromArgb(120, 226, 214); halo = Color.Empty; wob = 0.24f; haloA = 0; break;
                case MapKind.Crystal: bank = Color.FromArgb(120, 0, 0, 0); rim = Color.FromArgb(34, 28, 56); wc = Color.FromArgb(14, 30, 74); we = Color.FromArgb(40, 120, 190); halo = Color.FromArgb(110, 230, 255); wob = 0.22f; haloA = 22; break;
                case MapKind.Sky: bank = Color.FromArgb(50, 90, 100, 150); rim = Color.FromArgb(248, 248, 255); wc = Color.FromArgb(210, 240, 255); we = Color.FromArgb(150, 200, 246); halo = Color.FromArgb(255, 232, 150); wob = 0.06f; haloA = 20; break;
                default: bank = Color.FromArgb(130, 0, 0, 0); rim = Color.FromArgb(26, 18, 36); wc = Color.FromArgb(40, 10, 64); we = Color.FromArgb(110, 50, 170); halo = Color.FromArgb(170, 100, 255); wob = 0.24f; haloA = 26; break;
            }
            if (haloA > 0) for (int k = 3; k >= 1; --k) g.FillEllipse(_res.Brush(haloA / k + 4, halo.R, halo.G, halo.B), cx - rx * (1f + k * 0.3f), cy - ry * (1f + k * 0.36f), rx * 2 * (1f + k * 0.3f), ry * 2 * (1f + k * 0.36f));
            using (GraphicsPath p = new GraphicsPath()) { p.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.14f, wob)); g.FillPath(_res.Brush(bank), p); }
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.05f, wob));
                g.FillPath(_res.Brush(rim), p);
                g.DrawPath(_res.Pen(150, 20, 20, 30, 1.6f), p);
                if (kind == MapKind.Ruins) { GfxState s0 = g.Save(); g.SetClip(p, CombineMode.Intersect); for (int k = 0; k < 30; ++k) { double a = k * Math.PI * 2 / 30; g.DrawLine(_res.Pen(120, 50, 48, 44, 1f), cx + (float)Math.Cos(a) * rx * 0.9f, cy + (float)Math.Sin(a) * ry * 0.9f, cx + (float)Math.Cos(a) * rx * 1.12f, cy + (float)Math.Sin(a) * ry * 1.12f); } g.Restore(s0); }
                if (kind == MapKind.Sky) g.DrawPath(_res.Pen(230, 236, 200, 110, 2.4f), p);
            }
            using (GraphicsPath water = new GraphicsPath())
            {
                water.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 0.9f, wob));
                using (PathGradientBrush pg = new PathGradientBrush(water))
                {
                    pg.CenterColor = wc; pg.SurroundColors = new Color[] { we };
                    pg.CenterPoint = new PointF(cx + rx * 0.06f, cy + ry * 0.08f);
                    g.FillPath(pg, water);
                }
                GfxState gs = g.Save();
                g.SetClip(water, CombineMode.Intersect);
                using (LinearGradientBrush lb = GfxBrush.Linear(new RectangleF(x0, y0, w, h * 0.6f + 1), Color.FromArgb(kind == MapKind.Sky ? 150 : 80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical)) g.FillRectangle(lb, x0, y0, w, h * 0.6f);
                // 공통: 퍼지는 물결
                for (int k = 0; k < 3; ++k)
                {
                    double ph = (t * 0.22 + k / 3.0 + seed * 0.13) % 1.0; float pr = (float)ph;
                    float ox = cx + (Noise.Hash01(index, k, 601) - 0.5f) * rx * 0.9f, oy = cy + (Noise.Hash01(index, k, 603) - 0.5f) * ry * 0.7f, rr = 0.12f + pr * 0.5f;
                    g.DrawEllipse(_res.Pen((int)(110 * (1 - pr)), 235, 250, 255, 1.3f), ox - rx * rr, oy - ry * rr * 0.8f, rx * rr * 2, ry * rr * 1.6f);
                }
                switch (kind)
                {
                    case MapKind.Ruins:
                        for (int k = 0; k < 4; ++k) { float px = cx + (Noise.Hash01(index, k, 611) - 0.5f) * rx * 1.2f, py = cy + (Noise.Hash01(index, k, 613) - 0.5f) * ry, r = Math.Max(5f, Math.Min(rx, ry) * 0.11f), bob = (float)Math.Sin(t * 0.9 + k * 1.7) * 0.8f; g.FillEllipse(_res.Brush(Color.FromArgb(70, 152, 70)), px - r, py - r * 0.5f + bob, r * 2, r); g.FillEllipse(_res.Brush(Color.FromArgb(118, 196, 96)), px - r * 0.7f, py - r * 0.42f + bob, r * 1.1f, r * 0.45f); }
                        break;
                    case MapKind.Coast:
                        for (int k = 0; k < 6; ++k) { float px = cx + (Noise.Hash01(index, k, 621) - 0.5f) * rx * 1.3f + (float)Math.Sin(t * 0.6 + k) * 3f, py = cy + (Noise.Hash01(index, k, 623) - 0.5f) * ry * 1.2f, r = Math.Min(rx, ry) * 0.25f; g.DrawArc(_res.Pen(70, 255, 255, 255, 1.4f), px - r, py - r * 0.4f, r * 2, r * 0.8f, 200, 120); }
                        for (int k = 0; k < 3; ++k) { float px = cx + (Noise.Hash01(index, k, 631) - 0.5f) * rx, py = cy + (Noise.Hash01(index, k, 633) - 0.5f) * ry * 0.8f, R = Math.Max(4f, Math.Min(rx, ry) * 0.12f); PointF[] sp = new PointF[10]; for (int i = 0; i < 10; ++i) { double a = i * Math.PI / 5 + k; float rr = i % 2 == 0 ? R : R * 0.45f; sp[i] = new PointF(px + (float)Math.Cos(a) * rr, py + (float)Math.Sin(a) * rr * 0.6f); } g.FillPolygon(_res.Brush(235, 255, 150, 90), sp); }
                        break;
                    case MapKind.Crystal:
                        for (int k = 0; k < 14; ++k) { double ph = (t * 0.12 + Noise.Hash01(index, k, 641)) % 1.0; float px = cx + (Noise.Hash01(index, k, 643) - 0.5f) * rx * 1.6f + (float)Math.Sin(ph * 9 + k) * 6f, py = cy + ry * 0.9f - (float)ph * ry * 1.8f; int a = (int)(220 * Math.Sin(ph * Math.PI)); Color c = k % 2 == 0 ? Color.FromArgb(130, 240, 255) : Color.FromArgb(255, 140, 236); g.FillEllipse(_res.Brush(a / 4, c.R, c.G, c.B), px - 6, py - 6, 12, 12); g.FillEllipse(_res.Brush(a, 255, 255, 255), px - 1.5f, py - 1.5f, 3f, 3f); }
                        break;
                    case MapKind.Sky:
                        for (int k = 0; k < 10; ++k) { double ph = (t * 0.2 + Noise.Hash01(index, k, 651)) % 1.0; float px = cx + (Noise.Hash01(index, k, 653) - 0.5f) * rx * 1.5f, py = cy + ry * 0.8f - (float)ph * ry * 1.7f; int a = (int)(240 * Math.Sin(ph * Math.PI)); g.FillEllipse(_res.Brush(a, 255, 240, 170), px - 2, py - 2, 4, 4); g.DrawLine(_res.Pen(a / 2, 255, 250, 210, 1f), px - 5, py, px + 5, py); g.DrawLine(_res.Pen(a / 2, 255, 250, 210, 1f), px, py - 5, px, py + 5); }
                        for (int k = 0; k < 4; ++k) { float tw = (float)Math.Sin(t * (1.1 + k * 0.3) + seed + k); if (tw < 0.3f) continue; float gx = cx + (Noise.Hash01(index, k, 661) - 0.5f) * rx * 1.4f, gy = cy + (Noise.Hash01(index, k, 663) - 0.5f) * ry * 1.2f; g.FillEllipse(_res.Brush((int)(200 * tw), 255, 255, 255), gx - 3, gy - 1, 6, 2); }
                        break;
                    default:
                        for (int k = 0; k < 6; ++k) { double ph = (t * 0.4 + Noise.Hash01(index, k, 671) * 3) % 1.0; float bx = cx + (Noise.Hash01(index, k, 673) - 0.5f) * rx * 1.4f, by = cy + (Noise.Hash01(index, k, 675) - 0.5f) * ry * 1.2f, s = (float)(2 + ph * 6); g.DrawEllipse(_res.Pen((int)(200 * (1 - ph)), 220, 170, 255, 1.4f), bx - s, by - s * 0.6f, s * 2, s * 1.2f); }
                        for (int k = 0; k < 5; ++k) { double ph = (t * 0.18 + Noise.Hash01(index, k, 681)) % 1.0; float px = cx + (Noise.Hash01(index, k, 683) - 0.5f) * rx * 1.4f + (float)Math.Sin(ph * 8 + k) * 5f, py = cy + ry * 0.7f - (float)ph * ry * 1.6f; int a = (int)(230 * Math.Sin(ph * Math.PI)); g.FillEllipse(_res.Brush(a / 3, 150, 120, 255), px - 7, py - 7, 14, 14); g.FillEllipse(_res.Brush(a, 236, 226, 255), px - 2, py - 3, 4, 6); }
                        break;
                }
                g.Restore(gs);
                g.DrawPath(_res.Pen(120, 10, 20, 30, 2f), water);
            }
            // 둘레의 장식
            for (int k = 0; k < 8; ++k)
            {
                double a = Noise.Hash01(index, k, 691) * Math.PI * 2;
                float sx = cx + (float)Math.Cos(a) * rx * 1.08f, sy = cy + (float)Math.Sin(a) * ry * 1.08f, s = Math.Max(3f, Math.Min(rx, ry) * (0.07f + Noise.Hash01(index, k, 693) * 0.07f));
                if (kind == MapKind.Crystal)
                {
                    Color c = k % 2 == 0 ? Color.FromArgb(120, 240, 255) : Color.FromArgb(255, 130, 236);
                    g.FillEllipse(_res.Brush(40, c.R, c.G, c.B), sx - s * 2, sy - s * 2.2f, s * 4, s * 3f);
                    PointF[] cs = { new PointF(sx - s * 0.6f, sy), new PointF(sx - s * 0.1f, sy - s * 2.4f), new PointF(sx + s * 0.7f, sy) };
                    P.Poly(g, cs, Col.Light(c, 0.55f), Col.Dark(c, 0.3f), Color.FromArgb(200, 10, 20, 40), 1f);
                }
                else if (kind == MapKind.Dark)
                {
                    for (int i = -1; i <= 1; ++i) g.DrawLine(_res.Pen(240, 12, 8, 18, Math.Max(1.6f, s * 0.35f)), sx, sy, sx + i * s * 0.9f, sy - s * (1.6f + (i == 0 ? 0.7f : 0f)));
                }
                else if (kind == MapKind.Sky)
                {
                    g.FillEllipse(_res.Brush(60, 120, 130, 190), sx - s * 1.1f, sy - s * 0.2f, s * 2.4f, s * 1.1f);
                    g.FillEllipse(_res.Brush(255, 255, 255, 255), sx - s * 1.2f, sy - s * 0.6f, s * 2.4f, s * 1.1f);
                }
                else if (kind == MapKind.Coast)
                {
                    g.FillEllipse(_res.Brush(60, 0, 0, 0), sx - s + 1, sy - s * 0.4f + 2, s * 2, s);
                    g.FillEllipse(_res.Brush(Color.FromArgb(178, 170, 156)), sx - s, sy - s * 0.7f, s * 2, s * 1.4f);
                    g.FillEllipse(_res.Brush(90, 255, 255, 250), sx - s * 0.5f, sy - s * 0.5f, s, s * 0.5f);
                }
                else
                {
                    g.FillEllipse(_res.Brush(70, 0, 0, 0), sx - s + 1, sy - s * 0.4f + 2, s * 2, s);
                    PointF[] rock = { new PointF(sx - s, sy), new PointF(sx - s * 0.7f, sy - s * 0.9f), new PointF(sx + s * 0.2f, sy - s * 1.1f), new PointF(sx + s, sy - s * 0.3f), new PointF(sx + s * 0.8f, sy) };
                    P.Poly(g, rock, Color.FromArgb(176, 170, 156), Color.FromArgb(104, 98, 90), Color.FromArgb(200, 24, 22, 20), 1.2f);
                }
            }
        }
    }
}
