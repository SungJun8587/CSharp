//***************************************************************************
// BiomePools.cs : 설원의 얼음 호수 / 화산의 용암 웅덩이 / 늪의 탁한 물웅덩이.
// 숲의 연못(PondRenderer)과 같이 장애물(Rect)의 자리를 쓰되 가장자리는 울퉁불퉁한 타원으로 그린다. 상태 없이 좌표/시간의 함수.
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
        private readonly Resources _res;
        public BiomePools(Resources res) { _res = res; }

        /// <summary>물가 땅: 물 둘레로 젖어 어두워진 흙이 번져 나가며 바탕 풀/모래 색과 섞인다(바깥으로 갈수록 투명).</summary>
        private void WetBank(Graphics g, MapKind kind, float cx, float cy, float rx, float ry, uint seed)
        {
            Color wet;
            switch (kind)
            {
                case MapKind.Snow: wet = Color.FromArgb(120, 140, 170); break;       // 눈 녹은 질척한 회청색
                case MapKind.Volcano: wet = Color.FromArgb(22, 10, 8); break;        // 그을린 땅
                case MapKind.Swamp: wet = Color.FromArgb(40, 52, 36); break;
                case MapKind.Desert: wet = Color.FromArgb(120, 92, 56); break;
                case MapKind.Crystal: case MapKind.Dark: wet = Color.FromArgb(24, 20, 44); break;
                default: wet = Color.FromArgb(52, 66, 40); break;
            }
            using (GraphicsPath halo = new GraphicsPath())
            {
                halo.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.42f, 0.22f));
                using (PathGradientBrush pg = new PathGradientBrush(halo))
                {
                    pg.CenterColor = Color.FromArgb(150, wet);
                    pg.SurroundColors = new Color[] { Color.FromArgb(0, wet) };
                    pg.FocusScales = new PointF(0.55f, 0.55f);
                    g.FillPath(pg, halo);
                }
            }
        }

        private static PointF[] Outline(float cx, float cy, float rx, float ry, uint seed, float grow, float wobble)
        {
            const int N = 44;
            PointF[] pts = new PointF[N];
            for (int i = 0; i < N; ++i)
            {
                double a = i * Math.PI * 2 / N;
                float wob = 1f - wobble * 0.5f + Noise.Value((float)Math.Cos(a) * 1.7f + seed * 0.37f, (float)Math.Sin(a) * 1.7f + seed * 0.11f, 131) * wobble;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rx * wob * grow, cy + (float)Math.Sin(a) * ry * wob * grow);
            }
            return pts;
        }

        public void Draw(Graphics g, ViewState v, MapKind kind, BlockRow b, int index, long now)
        {
            float x0 = v.ToScreenX(b.X), y0 = v.ToScreenY(b.Y), w = v.Px(b.W), h = v.Px(b.H);
            if (x0 > v.Width + 60 || y0 > v.Height + 60 || x0 + w < -60 || y0 + h < -60) return;
            float cx = x0 + w / 2, cy = y0 + h / 2, rx = w / 2, ry = h / 2;
            uint seed = (uint)index * 13 + 5;
            double t = now / 1000.0;
            WetBank(g, kind, cx, cy, rx, ry, seed);
            if (kind == MapKind.Snow) Ice(g, cx, cy, rx, ry, seed, index, t, x0, y0, w, h);
            else if (kind == MapKind.Volcano) Lava(g, cx, cy, rx, ry, seed, index, t, x0, y0, w, h);
            else if (kind == MapKind.Swamp) Murk(g, cx, cy, rx, ry, seed, index, t, x0, y0, w, h);
            else Pool2(g, kind, cx, cy, rx, ry, seed, index, t, x0, y0, w, h);
        }

        // ------------------------------------------------------------ 얼음 호수
        private void Ice(Graphics g, float cx, float cy, float rx, float ry, uint seed, int index, double t, float x0, float y0, float w, float h)
        {
            using (GraphicsPath bank = new GraphicsPath())
            {
                bank.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.12f, 0.2f));
                g.FillPath(_res.Brush(70, 70, 100, 150), bank);
                g.FillPath(_res.Brush(235, 244, 250, 255), bank);                    // 호숫가에 쌓인 눈
            }
            using (GraphicsPath rim = new GraphicsPath())
            {
                rim.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.0f, 0.16f));
                g.FillPath(_res.Brush(255, 140, 190, 224), rim);                     // 얼음 가장자리(두꺼워 불투명한 푸른색)
            }
            using (GraphicsPath ice = new GraphicsPath())
            {
                ice.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 0.92f, 0.16f));
                using (PathGradientBrush pg = new PathGradientBrush(ice))
                {
                    pg.CenterColor = Color.FromArgb(255, 204, 240, 252);
                    pg.SurroundColors = new Color[] { Color.FromArgb(255, 106, 172, 218) };
                    pg.CenterPoint = new PointF(cx - rx * 0.12f, cy - ry * 0.15f);
                    g.FillPath(pg, ice);
                }
                GraphicsState gs = g.Save();
                g.SetClip(ice, CombineMode.Intersect);
                // 하늘빛 반사 띠
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x0, y0, w, h * 0.65f + 1), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    g.FillRectangle(lb, x0, y0, w, h * 0.65f);
                // 얼음 속 기포와 결
                for (int k = 0; k < 14; ++k)
                {
                    float bx = cx + (Noise.Hash01(index, k, 241) - 0.5f) * rx * 1.7f, by = cy + (Noise.Hash01(index, k, 243) - 0.5f) * ry * 1.6f, s = 1.2f + Noise.Hash01(index, k, 245) * 2.6f;
                    g.FillEllipse(_res.Brush(150, 255, 255, 255), bx - s, by - s * 0.8f, s * 2, s * 1.6f);
                    g.DrawEllipse(_res.Pen(90, 70, 130, 180, 1f), bx - s, by - s * 0.8f, s * 2, s * 1.6f);
                }
                // 갈라진 금: 중심에서 뻗는 꺾인 선
                int cracks = 5 + (int)(seed % 3);
                for (int k = 0; k < cracks; ++k)
                {
                    double a = Noise.Hash01(index, k, 251) * Math.PI * 2;
                    float px = cx + (Noise.Hash01(index, k, 253) - 0.5f) * rx * 0.5f, py = cy + (Noise.Hash01(index, k, 255) - 0.5f) * ry * 0.5f;
                    for (int s = 0; s < 6; ++s)
                    {
                        a += (Noise.Hash01(index, k * 11 + s, 257) - 0.5) * 0.9;
                        float len = Math.Min(rx, ry) * (0.2f + Noise.Hash01(index, k * 11 + s, 259) * 0.25f);
                        float nx = px + (float)Math.Cos(a) * len * 1.3f, ny = py + (float)Math.Sin(a) * len * 0.9f;
                        g.DrawLine(_res.Pen(150, 40, 100, 150, 1.6f), px, py, nx, ny);
                        g.DrawLine(_res.Pen(210, 255, 255, 255, 1f), px, py - 1, nx, ny - 1);
                        px = nx; py = ny;
                    }
                }
                // 반짝임
                for (int k = 0; k < 6; ++k)
                {
                    float tw = (float)Math.Sin(t * (1.2 + k * 0.3) + seed + k * 2.3);
                    if (tw < 0.4f) continue;
                    float gx = cx + (Noise.Hash01(index, k, 261) - 0.5f) * rx * 1.5f, gy = cy + (Noise.Hash01(index, k, 263) - 0.5f) * ry * 1.3f, s = 3f + tw * 4f;
                    g.DrawLine(_res.Pen((int)(230 * tw), 255, 255, 255, 1.4f), gx - s, gy, gx + s, gy);
                    g.DrawLine(_res.Pen((int)(230 * tw), 255, 255, 255, 1.4f), gx, gy - s, gx, gy + s);
                }
                g.Restore(gs);
                g.DrawPath(_res.Pen(150, 50, 100, 150, 2f), ice);
            }
            // 가장자리에 솟은 얼음 조각과 눈 더미
            for (int k = 0; k < 9; ++k)
            {
                double a = Noise.Hash01(index, k, 271) * Math.PI * 2;
                float sx = cx + (float)Math.Cos(a) * rx * 1.04f, sy = cy + (float)Math.Sin(a) * ry * 1.04f;
                float s = Math.Max(3f, Math.Min(rx, ry) * (0.07f + Noise.Hash01(index, k, 273) * 0.07f));
                if (k % 3 == 0)
                {
                    PointF[] sh = { new PointF(sx - s * 0.6f, sy), new PointF(sx - s * 0.2f, sy - s * 1.8f), new PointF(sx + s * 0.5f, sy - s * 1.1f), new PointF(sx + s * 0.7f, sy) };
                    P.Poly(g, sh, Color.FromArgb(226, 248, 255), Color.FromArgb(100, 170, 220), Color.FromArgb(170, 40, 100, 150), 1f);
                }
                else
                {
                    g.FillEllipse(_res.Brush(60, 70, 100, 150), sx - s * 1.1f, sy - s * 0.2f, s * 2.4f, s * 1.1f);
                    g.FillEllipse(_res.Brush(255, 250, 252, 255), sx - s * 1.2f, sy - s * 0.6f, s * 2.4f, s * 1.1f);
                }
            }
        }

        // ------------------------------------------------------------ 용암 웅덩이
        private void Lava(Graphics g, float cx, float cy, float rx, float ry, uint seed, int index, double t, float x0, float y0, float w, float h)
        {
            float pulse = 0.6f + 0.4f * (float)Math.Sin(t * 1.6 + seed);
            // 바깥으로 번지는 열기(바닥을 붉게 물들임)
            for (int k = 3; k >= 1; --k)
                g.FillEllipse(_res.Brush((int)(20 * pulse + 6), 255, 90, 20), cx - rx * (1f + k * 0.28f), cy - ry * (1f + k * 0.34f), rx * 2 * (1f + k * 0.28f), ry * 2 * (1f + k * 0.34f));
            using (GraphicsPath crust = new GraphicsPath())
            {
                crust.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.1f, 0.26f));
                g.FillPath(_res.Brush(255, 28, 20, 24), crust);
                g.DrawPath(_res.Pen(220, 6, 4, 8, 2f), crust);
            }
            using (GraphicsPath rim = new GraphicsPath())
            {
                rim.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.0f, 0.22f));
                g.FillPath(_res.Brush(255, 120, 34, 18), rim);                      // 식어 가는 붉은 가장자리
            }
            using (GraphicsPath lava = new GraphicsPath())
            {
                lava.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 0.9f, 0.2f));
                using (PathGradientBrush pg = new PathGradientBrush(lava))
                {
                    pg.CenterColor = Color.FromArgb(255, 255, (int)(206 + 30 * pulse), 100);
                    pg.SurroundColors = new Color[] { Color.FromArgb(255, 226, 78, 18) };
                    pg.CenterPoint = new PointF(cx + rx * 0.05f, cy);
                    g.FillPath(pg, lava);
                }
                GraphicsState gs = g.Save();
                g.SetClip(lava, CombineMode.Intersect);
                // 천천히 떠다니는 검은 굳은 껍질 조각
                for (int k = 0; k < 6; ++k)
                {
                    double ph = t * (0.05 + k * 0.012) + Noise.Hash01(index, k, 281) * 6.28;
                    float px = cx + (float)Math.Cos(ph) * rx * (0.3f + Noise.Hash01(index, k, 283) * 0.45f), py = cy + (float)Math.Sin(ph * 1.3) * ry * (0.25f + Noise.Hash01(index, k, 285) * 0.4f);
                    float s = Math.Min(rx, ry) * (0.2f + Noise.Hash01(index, k, 287) * 0.25f);
                    GraphicsState s2 = g.Save();
                    g.TranslateTransform(px, py); g.RotateTransform((float)(ph * 20));
                    PointF[] plate = { new PointF(-s, -s * 0.2f), new PointF(-s * 0.3f, -s * 0.55f), new PointF(s * 0.6f, -s * 0.35f), new PointF(s, s * 0.1f), new PointF(s * 0.2f, s * 0.5f), new PointF(-s * 0.7f, s * 0.35f) };
                    g.FillPolygon(_res.Brush(235, 40, 28, 30), plate);
                    g.DrawPolygon(_res.Pen(230, 255, 110, 30, 1.6f), plate);
                    g.Restore(s2);
                }
                // 부글거리는 거품: 커졌다 터진다
                for (int k = 0; k < 6; ++k)
                {
                    double ph = (t * 0.55 + Noise.Hash01(index, k, 291) * 3) % 1.0;
                    float bx = cx + (Noise.Hash01(index, k, 293) - 0.5f) * rx * 1.4f, by = cy + (Noise.Hash01(index, k, 295) - 0.5f) * ry * 1.3f;
                    float s = (float)(2 + ph * 7);
                    int a = (int)(220 * (1 - ph * ph));
                    g.DrawEllipse(_res.Pen(a, 255, 236, 150, 1.6f), bx - s, by - s * 0.6f, s * 2, s * 1.2f);
                    if (ph > 0.9) g.FillEllipse(_res.Brush(200, 255, 250, 200), bx - 3, by - 2, 6, 4);
                }
                g.Restore(gs);
            }
            // 둘레의 굳은 현무암 덩어리와 불씨
            for (int k = 0; k < 8; ++k)
            {
                double a = Noise.Hash01(index, k, 301) * Math.PI * 2;
                float sx = cx + (float)Math.Cos(a) * rx * 1.1f, sy = cy + (float)Math.Sin(a) * ry * 1.1f;
                float s = Math.Max(3f, Math.Min(rx, ry) * (0.07f + Noise.Hash01(index, k, 303) * 0.08f));
                g.FillEllipse(_res.Brush(90, 0, 0, 0), sx - s + 1, sy - s * 0.3f + 2, s * 2, s);
                PointF[] rock = { new PointF(sx - s, sy), new PointF(sx - s * 0.7f, sy - s * 0.9f), new PointF(sx + s * 0.2f, sy - s * 1.1f), new PointF(sx + s, sy - s * 0.3f), new PointF(sx + s * 0.8f, sy) };
                P.Poly(g, rock, Color.FromArgb(84, 72, 80), Color.FromArgb(26, 20, 26), Color.FromArgb(210, 8, 4, 8), 1.2f);
                g.DrawLine(_res.Pen(180, 255, 110, 30, 1.2f), sx - s * 0.6f, sy - s * 0.1f, sx + s * 0.5f, sy - s * 0.15f);
            }
            for (int k = 0; k < 7; ++k)
            {
                double p = (t * 0.28 + Noise.Hash01(index, k, 311)) % 1.0;
                float ex = cx + (Noise.Hash01(index, k, 313) - 0.5f) * rx * 1.6f + (float)Math.Sin(p * 9 + k) * 5f, ey = cy - (float)p * ry * 2.4f;
                g.FillEllipse(_res.Brush((int)(230 * (1 - p)), 255, 170, 50), ex - 1.6f, ey - 1.6f, 3.2f, 3.2f);
            }
        }

        // ------------------------------------------------------------ 늪 웅덩이
        private void Murk(Graphics g, float cx, float cy, float rx, float ry, uint seed, int index, double t, float x0, float y0, float w, float h)
        {
            using (GraphicsPath bank = new GraphicsPath())
            {
                bank.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.14f, 0.26f));
                g.FillPath(_res.Brush(90, 10, 22, 12), bank);
            }
            using (GraphicsPath mud = new GraphicsPath())
            {
                mud.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 1.04f, 0.24f));
                g.FillPath(_res.Brush(235, 62, 50, 32), mud);
            }
            using (GraphicsPath water = new GraphicsPath())
            {
                water.AddClosedCurve(Outline(cx, cy, rx, ry, seed, 0.94f, 0.2f));
                using (PathGradientBrush pg = new PathGradientBrush(water))
                {
                    pg.CenterColor = Color.FromArgb(252, 16, 38, 34);
                    pg.SurroundColors = new Color[] { Color.FromArgb(246, 52, 82, 56) };
                    pg.CenterPoint = new PointF(cx + rx * 0.1f, cy + ry * 0.1f);
                    g.FillPath(pg, water);
                }
                GraphicsState gs = g.Save();
                g.SetClip(water, CombineMode.Intersect);
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x0, y0, w, h * 0.6f + 1), Color.FromArgb(60, 190, 220, 200), Color.FromArgb(0, 190, 220, 200), LinearGradientMode.Vertical))
                    g.FillRectangle(lb, x0, y0, w, h * 0.6f);
                // 녹조: 번져 있는 연두색 얼룩
                for (int k = 0; k < 7; ++k)
                {
                    float ax = cx + (Noise.Hash01(index, k, 321) - 0.5f) * rx * 1.5f + (float)Math.Sin(t * 0.2 + k) * 2f, ay = cy + (Noise.Hash01(index, k, 323) - 0.5f) * ry * 1.4f;
                    float s = Math.Min(rx, ry) * (0.2f + Noise.Hash01(index, k, 325) * 0.3f);
                    g.FillEllipse(_res.Brush(110, 120, 156, 56), ax - s * 1.3f, ay - s * 0.6f, s * 2.6f, s * 1.2f);
                    g.FillEllipse(_res.Brush(90, 170, 190, 80), ax - s * 0.8f, ay - s * 0.4f, s * 1.4f, s * 0.6f);
                }
                // 올라오는 기포
                for (int k = 0; k < 5; ++k)
                {
                    double ph = (t * 0.35 + Noise.Hash01(index, k, 331) * 3) % 1.0;
                    float bx = cx + (Noise.Hash01(index, k, 333) - 0.5f) * rx * 1.4f, by = cy + (Noise.Hash01(index, k, 335) - 0.5f) * ry * 1.2f;
                    float s = (float)(1.5 + ph * 5);
                    g.DrawEllipse(_res.Pen((int)(170 * (1 - ph)), 200, 230, 190, 1.2f), bx - s, by - s * 0.55f, s * 2, s * 1.1f);
                }
                // 수초 잎
                int pads = 3 + (int)(seed % 4);
                for (int k = 0; k < pads; ++k)
                {
                    float px = cx + (Noise.Hash01(index, k, 341) - 0.5f) * rx * 1.2f, py = cy + (Noise.Hash01(index, k, 343) - 0.5f) * ry * 1.1f;
                    float r = Math.Max(5f, Math.Min(rx, ry) * (0.09f + Noise.Hash01(index, k, 345) * 0.07f)), bob = (float)Math.Sin(t * 0.8 + k * 1.7) * 0.8f;
                    g.FillEllipse(_res.Brush(70, 0, 10, 8), px - r + 2, py - r * 0.5f + 2 + bob, r * 2, r);
                    g.FillEllipse(_res.Brush(Color.FromArgb(58, 104, 52)), px - r, py - r * 0.5f + bob, r * 2, r);
                    g.FillEllipse(_res.Brush(Color.FromArgb(104, 150, 66)), px - r * 0.7f, py - r * 0.42f + bob, r * 1.1f, r * 0.45f);
                    g.FillPie(_res.Brush(Color.FromArgb(16, 40, 36)), px - r, py - r * 0.5f + bob, r * 2, r, -10, 30);
                }
                for (int k = 0; k < 2; ++k)
                {
                    double ph = (t * 0.2 + k / 2.0 + seed * 0.13) % 1.0; float pr = (float)ph;
                    float ox = cx + (Noise.Hash01(index, k, 351) - 0.5f) * rx * 0.8f, oy = cy + (Noise.Hash01(index, k, 353) - 0.5f) * ry * 0.6f, rr = 0.12f + pr * 0.45f;
                    g.DrawEllipse(_res.Pen((int)(90 * (1 - pr)), 200, 230, 200, 1.2f), ox - rx * rr, oy - ry * rr * 0.8f, rx * rr * 2, ry * rr * 1.6f);
                }
                g.Restore(gs);
                g.DrawPath(_res.Pen(150, 8, 20, 12, 2f), water);
            }
            // 물가의 갈대와 썩은 가지, 수면 위로 솟은 나뭇가지
            int reeds = 7 + (int)(seed % 5);
            for (int k = 0; k < reeds; ++k)
            {
                double a = Noise.Hash01(index, k, 361) * Math.PI * 2;
                float bx = cx + (float)Math.Cos(a) * rx * 1.04f, by = cy + (float)Math.Sin(a) * ry * 1.04f;
                float len = Math.Max(10f, Math.Min(rx, ry) * (0.3f + Noise.Hash01(index, k, 363) * 0.22f)), sway = (float)Math.Sin(t * 1.2 + k * 1.9) * len * 0.07f;
                for (int s = -1; s <= 1; ++s)
                {
                    float ox = s * len * 0.14f, hh = len * (s == 0 ? 1f : 0.76f);
                    g.DrawLine(_res.Pen(235, 40, 66, 34, Math.Max(1.4f, len * 0.07f)), bx + ox, by, bx + ox * 1.7f + sway, by - hh);
                    g.DrawLine(_res.Pen(170, 136, 164, 80, 1f), bx + ox, by, bx + ox * 1.7f + sway, by - hh);
                }
                if (k % 2 == 0) g.FillRectangle(_res.Brush(Color.FromArgb(104, 66, 38)), bx + sway - len * 0.05f, by - len * 1.12f, len * 0.1f, len * 0.3f);
            }
            for (int k = 0; k < 3; ++k)
            {
                double a = Noise.Hash01(index, k, 371) * Math.PI * 2;
                float bx = cx + (float)Math.Cos(a) * rx * 0.55f, by = cy + (float)Math.Sin(a) * ry * 0.5f, l = Math.Min(rx, ry) * 0.5f;
                g.DrawLine(_res.Pen(230, 40, 30, 22, 2.6f), bx, by, bx + l * 0.2f, by - l);
                g.DrawLine(_res.Pen(230, 40, 30, 22, 1.8f), bx + l * 0.1f, by - l * 0.55f, bx + l * 0.5f, by - l * 0.85f);
            }
        }
    }
}
