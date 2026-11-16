//***************************************************************************
// Pond.cs : 숲의 연못. 울퉁불퉁한 물가, 깊이에 따른 물색, 퍼지는 물결, 반짝임, 수련잎과 부들.
// 전부 좌표/시간의 함수라 상태를 갖지 않는다. 장애물(Rect)의 자리를 그대로 쓰되 가장자리는 타원에 가깝게 그린다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;

namespace GameViewer.App.Render
{
    public sealed class PondRenderer
    {
        private readonly Resources _res;
        public PondRenderer(Resources res) { _res = res; }

        private static PointF[] Outline(float cx, float cy, float rx, float ry, uint seed, float grow)
        {
            const int N = 40;
            PointF[] pts = new PointF[N];
            for (int i = 0; i < N; ++i)
            {
                double a = i * Math.PI * 2 / N;
                float wob = 0.90f + Noise.Value((float)Math.Cos(a) * 1.6f + seed * 0.37f, (float)Math.Sin(a) * 1.6f + seed * 0.11f, 131) * 0.16f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rx * wob * grow, cy + (float)Math.Sin(a) * ry * wob * grow);
            }
            return pts;
        }

        public void Draw(Graphics g, ViewState v, BlockRow b, int index, long now)
        {
            float x0 = v.ToScreenX(b.X), y0 = v.ToScreenY(b.Y), w = v.Px(b.W), h = v.Px(b.H);
            if (x0 > v.Width + 40 || y0 > v.Height + 40 || x0 + w < -40 || y0 + h < -40) return;
            float cx = x0 + w / 2, cy = y0 + h / 2, rx = w / 2, ry = h / 2;
            uint seed = (uint)index * 13 + 5;
            double t = now / 1000.0;

            // 물가: 젖은 흙 -> 진흙 -> 물
            using (GraphicsPath bank = new GraphicsPath())
            {
                bank.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.07f));
                g.FillPath(_res.Brush(70, 24, 40, 20), bank);
            }
            using (GraphicsPath mud = new GraphicsPath())
            {
                mud.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.0f));
                g.FillPath(_res.Brush(150, 84, 66, 44), mud);
            }
            using (GraphicsPath water = new GraphicsPath())
            {
                water.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 0.93f));
                using (PathGradientBrush pg = new PathGradientBrush(water))
                {
                    pg.CenterColor = Color.FromArgb(246, 22, 84, 110);
                    pg.SurroundColors = new Color[] { Color.FromArgb(240, 72, 164, 160) };
                    pg.CenterPoint = new PointF(cx + rx * 0.08f, cy + ry * 0.1f);
                    g.FillPath(pg, water);
                }
                GraphicsState gs = g.Save();
                g.SetClip(water, CombineMode.Intersect);
                // 하늘 반사: 위쪽에 밝은 띠
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x0, y0, w, h * 0.7f + 1), Color.FromArgb(86, 220, 244, 250), Color.FromArgb(0, 220, 244, 250), LinearGradientMode.Vertical))
                    g.FillRectangle(lb, x0, y0, w, h * 0.7f);
                // 퍼지는 물결
                for (int k = 0; k < 3; ++k)
                {
                    double ph = (t * 0.22 + k / 3.0 + seed * 0.13) % 1.0;
                    float pr = (float)ph;
                    float ox = cx + (Noise.Hash01(index, k, 141) - 0.5f) * rx * 0.9f, oy = cy + (Noise.Hash01(index, k, 143) - 0.5f) * ry * 0.7f;
                    float rr = (0.12f + pr * 0.5f);
                    g.DrawEllipse(_res.Pen((int)(120 * (1 - pr)), 235, 250, 255, 1.4f), ox - rx * rr, oy - ry * rr * 0.8f, rx * rr * 2, ry * rr * 1.6f);
                }
                // 반짝임
                for (int k = 0; k < 7; ++k)
                {
                    float tw = (float)Math.Sin(t * (1.3 + k * 0.37) + seed + k * 2.1);
                    if (tw < 0.3f) continue;
                    float gx = cx + (Noise.Hash01(index, k, 151) - 0.5f) * rx * 1.5f, gy = cy + (Noise.Hash01(index, k, 153) - 0.5f) * ry * 1.3f;
                    float s = 2f + tw * 2.4f;
                    g.FillEllipse(_res.Brush((int)(210 * tw), 255, 255, 250), gx - s, gy - s * 0.35f, s * 2, s * 0.7f);
                }
                // 수련잎
                int pads = 3 + (int)(seed % 4);
                for (int k = 0; k < pads; ++k)
                {
                    float px = cx + (Noise.Hash01(index, k, 161) - 0.5f) * rx * 1.2f, py = cy + (Noise.Hash01(index, k, 163) - 0.5f) * ry * 1.1f;
                    float r = Math.Max(5f, Math.Min(rx, ry) * (0.1f + Noise.Hash01(index, k, 165) * 0.08f));
                    float bob = (float)Math.Sin(t * 0.9 + k * 1.7) * 0.8f;
                    g.FillEllipse(_res.Brush(60, 4, 30, 40), px - r + 2, py - r * 0.5f + 2 + bob, r * 2, r);
                    g.FillEllipse(_res.Brush(Color.FromArgb(70, 152, 70)), px - r, py - r * 0.5f + bob, r * 2, r);
                    g.FillEllipse(_res.Brush(Color.FromArgb(118, 196, 96)), px - r * 0.7f, py - r * 0.42f + bob, r * 1.1f, r * 0.45f);
                    g.FillPie(_res.Brush(Color.FromArgb(22, 70, 90)), px - r, py - r * 0.5f + bob, r * 2, r, -10, 30);
                    if (Noise.Hash(index, k, 167) % 3 == 0)
                    {
                        g.FillEllipse(_res.Brush(Color.FromArgb(255, 170, 200)), px - r * 0.34f, py - r * 0.5f + bob, r * 0.68f, r * 0.5f);
                        g.FillEllipse(_res.Brush(Color.FromArgb(255, 238, 150)), px - r * 0.12f, py - r * 0.36f + bob, r * 0.24f, r * 0.2f);
                    }
                }
                g.Restore(gs);
                g.DrawPath(_res.Pen(110, 20, 60, 70, 2f), water);
                g.DrawPath(_res.Pen(70, 220, 250, 250, 1f), water);
            }
            // 물가의 부들(갈대)과 조약돌
            int reeds = 6 + (int)(seed % 5);
            for (int k = 0; k < reeds; ++k)
            {
                double a = Noise.Hash01(index, k, 171) * Math.PI * 2;
                float bx = cx + (float)Math.Cos(a) * rx * 1.02f, by = cy + (float)Math.Sin(a) * ry * 1.02f;
                float len = Math.Max(10f, Math.Min(rx, ry) * (0.28f + Noise.Hash01(index, k, 173) * 0.2f));
                float sway = (float)Math.Sin(t * 1.3 + k * 1.9) * len * 0.07f;
                for (int s = -1; s <= 1; ++s)
                {
                    float ox = s * len * 0.12f, hh = len * (s == 0 ? 1f : 0.78f);
                    g.DrawLine(_res.Pen(235, 40, 96, 44, Math.Max(1.4f, len * 0.07f)), bx + ox, by, bx + ox * 1.6f + sway, by - hh);
                    g.DrawLine(_res.Pen(180, 128, 196, 84, 1f), bx + ox, by, bx + ox * 1.6f + sway, by - hh);
                }
                if (k % 2 == 0) g.FillRectangle(_res.Brush(Color.FromArgb(116, 72, 40)), bx + sway - len * 0.05f, by - len * 1.12f, len * 0.1f, len * 0.3f);
            }
            for (int k = 0; k < 8; ++k)
            {
                double a = Noise.Hash01(index, k, 181) * Math.PI * 2;
                float sx = cx + (float)Math.Cos(a) * rx * 1.08f, sy = cy + (float)Math.Sin(a) * ry * 1.08f;
                float s = Math.Max(2.5f, Math.Min(rx, ry) * (0.04f + Noise.Hash01(index, k, 183) * 0.05f));
                g.FillEllipse(_res.Brush(60, 0, 0, 0), sx - s + 1, sy - s * 0.4f + 2, s * 2, s);
                g.FillEllipse(_res.Brush(Color.FromArgb(150, 150, 146)), sx - s, sy - s * 0.7f, s * 2, s * 1.4f);
                g.FillEllipse(_res.Brush(90, 255, 255, 250), sx - s * 0.5f, sy - s * 0.5f, s, s * 0.5f);
            }
        }
    }
}
