//***************************************************************************
// DesertProps.cs : 사막 소품. 선인장 / 고목 / 야자수 / 사암 절벽(월드 바깥) / 폐허 벽(직사각 장애물).
// 소품은 크기와 모양 종류별로 한 번 그려 SpriteCache 에 두고 찍는다 (TreeRenderer 와 같은 방식).
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class DesertProps : IDisposable
    {
        private readonly Resources _res;
        private readonly SpriteCache _cache = new SpriteCache(400);
        private readonly SpriteCache.PaintFn _paint;
        private ZoneTheme.DesertProp _pk; private bool _pmesa; private float _pr, _ppu; private uint _pseed; private bool _pdark;

        public DesertProps(Resources res) { _res = res; _paint = Paint; }
        public void Dispose() { _cache.Dispose(); }

        /// <summary>소품 한 개. (wx,wy)는 밑동의 월드 좌표, r 은 크기 기준 반지름(unit).</summary>
        public void Draw(IGfx g, ViewState v, ZoneTheme.DesertProp kind, float wx, float wy, float r, uint seed, bool dark)
        {
            Place(g, v, kind, false, wx, wy, r, seed, dark);
        }

        /// <summary>월드 바깥의 사암 절벽 한 덩이.</summary>
        public void DrawMesa(IGfx g, ViewState v, float wx, float wy, float r, uint seed)
        {
            Place(g, v, ZoneTheme.DesertProp.Cactus, true, wx, wy, r, seed, true);
        }

        private void Place(IGfx g, ViewState v, ZoneTheme.DesertProp kind, bool mesa, float wx, float wy, float r, uint seed, bool dark)
        {
            float sx = v.ToScreenX(wx), sy = v.ToScreenY(wy);
            float pr = v.Px(r);
            if (sx < -pr * 3 || sy < -pr * 4.5f || sx > v.Width + pr * 3 || sy > v.Height + pr * 2) return;
            float rq = (float)(Math.Max(1, Math.Round(r / 0.3)) * 0.3);
            float ppu = CreatureRenderer.QuantizePpu(v.Scale);
            uint variant = seed % 4;
            _pk = kind; _pmesa = mesa; _pr = rq; _ppu = ppu; _pseed = variant; _pdark = dark;
            bool palm = !mesa && kind == ZoneTheme.DesertProp.Palm;
            float wU = mesa ? 3.6f : (palm ? 4.8f : 3.4f), hU = mesa ? 3.2f : (palm ? 4.7f : 3.3f);
            int w = (int)Math.Ceiling(rq * wU * ppu), h = (int)Math.Ceiling(rq * hU * ppu);
            float ax = w / 2f, ay = h - rq * 0.3f * ppu;
            long key = (4L << 40) | ((long)(rq * 10) << 24) | ((long)variant << 16) | ((long)kind << 12) | ((mesa ? 1L : 0L) << 10) | ((dark ? 1L : 0L) << 9) | (long)(ppu * 2);
            SpriteCache.Entry e = _cache.Get(key, w, h, ax, ay, _paint);
            float k = (float)(v.Scale / ppu);
            float sw = mesa ? 1.5f : (kind == ZoneTheme.DesertProp.Palm ? 0.95f : 0.8f);
            g.FillEllipse(_res.Brush(mesa ? 50 : 64, 70, 40, 10), sx - pr * sw * 0.9f, sy - pr * 0.28f, pr * sw * 1.9f, pr * 0.8f);
            g.DrawImage(e.Bmp, sx - ax * k, sy - ay * k, w * k, h * k);
        }

        private static Color Tone(Color c, int d) { return Color.FromArgb(c.A, Col.Cl(c.R + d), Col.Cl(c.G + d), Col.Cl(c.B + d)); }

        private void Paint(IGfx g)
        {
            float r = _pr;
            g.ScaleTransform(_ppu, _ppu);
            if (_pmesa) { PaintMesa(g, r); return; }
            switch (_pk)
            {
                case ZoneTheme.DesertProp.Cactus: PaintCactus(g, r); break;
                case ZoneTheme.DesertProp.DeadTree: PaintDead(g, r); break;
                default: PaintPalm(g, r); break;
            }
        }

        private void PaintCactus(IGfx g, float r)
        {
            int tone = (int)(_pseed % 3) * 7;
            Color body = Color.FromArgb(86 + tone, 142 + tone, 72), dk = Color.FromArgb(40, 84 + tone, 44), ol = Color.FromArgb(150, 20, 44, 22);
            Color hi = Color.FromArgb(150, 200 + tone, 96);
            bool twoArms = _pseed != 1;
            // 팔: 줄기 옆으로 나왔다가 위로 꺾인다
            float[] armY = { -1.15f, -0.85f };
            for (int a = 0; a < (twoArms ? 2 : 1); ++a)
            {
                float dir = a == 0 ? -1f : 1f, y = armY[a] * r;
                P.Round(g, dir > 0 ? 0.1f * r : -0.78f * r, y - 0.12f * r, 0.68f * r, 0.3f * r, 0.15f * r, body, dk, ol, 0.03f * r);
                float ux = dir * 0.62f * r;
                P.Round(g, ux - 0.14f * r, y - 0.95f * r, 0.3f * r, 1.1f * r, 0.15f * r, body, dk, ol, 0.03f * r);
            }
            P.Round(g, -0.3f * r, -2.4f * r, 0.6f * r, 2.4f * r, 0.3f * r, Col.Mix(body, hi, 0.25f), dk, ol, 0.035f * r);
            using (Pen p = new Pen(Color.FromArgb(110, 20, 60, 26), Math.Max(0.02f, 0.02f * r)))
                for (int i = -1; i <= 1; ++i) g.DrawLine(p, i * 0.14f * r, -2.25f * r, i * 0.16f * r, -0.08f * r);
            using (Pen p = new Pen(Color.FromArgb(150, 255, 244, 200), Math.Max(0.015f, 0.012f * r)))
                for (int i = 0; i < 9; ++i) { float yy = -(0.2f + i * 0.25f) * r; g.DrawLine(p, -0.3f * r, yy, -0.38f * r, yy - 0.05f * r); g.DrawLine(p, 0.3f * r, yy - 0.1f * r, 0.38f * r, yy - 0.15f * r); }
            if (_pseed % 2 == 0)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 236, 90, 130))) g.FillEllipse(b, -0.16f * r, -2.55f * r, 0.32f * r, 0.26f * r);
        }

        private void PaintDead(IGfx g, float r)
        {
            Color a = Color.FromArgb(150, 128, 104), b = Color.FromArgb(84, 66, 52), ol = Color.FromArgb(180, 30, 20, 14);
            PointF[] trunk = { new PointF(-0.34f * r, 0), new PointF(-0.2f * r, -1.4f * r), new PointF(-0.08f * r, -2.4f * r), new PointF(0.1f * r, -2.4f * r), new PointF(0.22f * r, -1.3f * r), new PointF(0.36f * r, 0) };
            P.Poly(g, trunk, a, b, ol, 0.03f * r);
            float[,] br = { { -0.1f, -1.5f, -1.0f, -2.0f }, { 0.12f, -1.9f, 0.9f, -2.5f }, { -0.05f, -2.3f, -0.45f, -2.85f }, { 0.15f, -1.1f, 0.85f, -1.4f } };
            int n = 3 + (int)(_pseed % 2);
            for (int i = 0; i < n; ++i)
            {
                using (Pen p = new Pen(ol, 0.2f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, br[i, 0] * r, br[i, 1] * r, br[i, 2] * r, br[i, 3] * r);
                using (Pen p = new Pen(a, 0.13f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, br[i, 0] * r, br[i, 1] * r, br[i, 2] * r, br[i, 3] * r);
                float mx = (br[i, 0] + br[i, 2]) * 0.5f, my = (br[i, 1] + br[i, 3]) * 0.5f;
                using (Pen p = new Pen(b, 0.07f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, mx * r, my * r, (mx + (br[i, 2] > 0 ? 0.35f : -0.35f)) * r, (my - 0.4f) * r);
            }
            using (Pen p = new Pen(Color.FromArgb(90, 40, 28, 20), 0.03f * r)) { g.DrawLine(p, -0.12f * r, -0.2f * r, -0.08f * r, -1.8f * r); g.DrawLine(p, 0.1f * r, -0.3f * r, 0.12f * r, -1.6f * r); }
        }

        private void PaintPalm(IGfx g, float r)
        {
            float lean = ((int)_pseed - 1.5f) * 0.12f;
            PointF[] tr = { new PointF(0, 0), new PointF(0.12f * r + lean * r * 0.5f, -1.2f * r), new PointF((0.18f + lean) * r, -2.6f * r) };
            using (Pen p = new Pen(Color.FromArgb(180, 40, 26, 14), 0.4f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, tr, 0.5f);
            using (Pen p = new Pen(Color.FromArgb(150, 112, 78), 0.3f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, tr, 0.5f);
            using (Pen p = new Pen(Color.FromArgb(120, 74, 48, 28), 0.04f * r))
                for (int i = 1; i < 9; ++i) { float t = i / 9f; float yy = -2.6f * r * t, xx = (0.18f + lean) * r * t * t; g.DrawLine(p, xx - 0.14f * r, yy, xx + 0.14f * r, yy - 0.05f * r); }
            float cx = (0.18f + lean) * r, cy = -2.62f * r;
            int fr = 9;
            for (int pass = 0; pass < 2; ++pass)
                for (int i = 0; i < fr; ++i)
                {
                    float a = -(float)Math.PI + (float)Math.PI * (i + 0.5f) / fr + (pass == 0 ? 0 : 0.17f);
                    float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    float len = r * (1.5f + ((i * 7 + (int)_pseed) % 3) * 0.14f) * (pass == 0 ? 1f : 0.8f);
                    PointF p0 = new PointF(cx, cy), p1 = new PointF(cx + ca * len * 0.5f, cy + sa * len * 0.5f - 0.5f * r), p2 = new PointF(cx + ca * len, cy + sa * len * 0.2f + 0.75f * r);
                    const int N = 10;
                    PointF[] left = new PointF[N + 1], right = new PointF[N + 1], mid = new PointF[N + 1];
                    for (int k = 0; k <= N; ++k)
                    {
                        float t = k / (float)N, u = 1 - t;
                        PointF bp = new PointF(u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X, u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y);
                        PointF tg = new PointF(2 * u * (p1.X - p0.X) + 2 * t * (p2.X - p1.X), 2 * u * (p1.Y - p0.Y) + 2 * t * (p2.Y - p1.Y));
                        float tl = (float)Math.Sqrt(tg.X * tg.X + tg.Y * tg.Y) + 1e-5f;
                        float w = 0.2f * r * (float)Math.Pow(Math.Sin(Math.PI * Math.Min(1f, t * 1.05f)), 0.7) * (pass == 0 ? 1f : 0.85f) + 0.01f * r;
                        PointF nrm = new PointF(-tg.Y / tl, tg.X / tl);
                        mid[k] = bp; left[k] = new PointF(bp.X + nrm.X * w, bp.Y + nrm.Y * w); right[k] = new PointF(bp.X - nrm.X * w, bp.Y - nrm.Y * w);
                    }
                    PointF[] poly = new PointF[(N + 1) * 2];
                    for (int k = 0; k <= N; ++k) { poly[k] = left[k]; poly[N + 1 + k] = right[N - k]; }
                    Color lt = pass == 0 ? Color.FromArgb(74, 150, 70) : Color.FromArgb(128, 190, 86), dk = pass == 0 ? Color.FromArgb(26, 84, 40) : Color.FromArgb(44, 118, 54);
                    P.Poly(g, poly, lt, dk, Color.FromArgb(220, 14, 40, 20), 0.03f * r);
                    using (Pen p = new Pen(Color.FromArgb(150, 210, 240, 150), Math.Max(0.02f, 0.025f * r))) g.DrawLines(p, mid);
                    using (Pen p = new Pen(Color.FromArgb(90, 14, 50, 24), Math.Max(0.02f, 0.02f * r)))
                        for (int k = 2; k < N; k += 2) { g.DrawLine(p, mid[k], left[k]); g.DrawLine(p, mid[k], right[k]); }
                }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 82, 44))) { g.FillEllipse(b, cx - 0.2f * r, cy + 0.02f * r, 0.2f * r, 0.2f * r); g.FillEllipse(b, cx + 0.02f * r, cy + 0.06f * r, 0.2f * r, 0.2f * r); }
        }

        private void PaintMesa(IGfx g, float r)
        {
            Color[] strata = { Color.FromArgb(214, 160, 104), Color.FromArgb(186, 124, 80), Color.FromArgb(224, 178, 122), Color.FromArgb(168, 108, 70), Color.FromArgb(206, 150, 96) };
            float baseW = 1.55f * r, topW = (0.95f + (_pseed % 3) * 0.1f) * r, hh = (1.9f + (_pseed % 2) * 0.4f) * r;
            int tiers = 4;
            for (int t = 0; t < tiers; ++t)
            {
                float y0 = -hh * t / tiers, y1 = -hh * (t + 1) / tiers;
                float w0 = baseW + (topW - baseW) * t / tiers * 1.0f, w1 = baseW + (topW - baseW) * (t + 1) / tiers;
                if (t > 0) { w0 *= 0.98f; }
                Color c = strata[(t + (int)_pseed) % strata.Length]; if (_pdark) c = Col.Dark(c, 0.22f);
                PointF[] face = { new PointF(-w0, y0), new PointF(-w1, y1), new PointF(w1, y1), new PointF(w0, y0) };
                P.Poly(g, face, Col.Light(c, 0.1f), Col.Dark(c, 0.28f), Color.FromArgb(150, 60, 30, 14), 0.03f * r);
                // 왼쪽 빛, 오른쪽 그늘
                PointF[] sh = { new PointF(w0 * 0.25f, y0), new PointF(w1 * 0.25f, y1), new PointF(w1, y1), new PointF(w0, y0) };
                using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 70, 30, 10))) g.FillPolygon(b, sh);
            }
            float tw = topW;
            PointF[] cap = { new PointF(-tw, -hh), new PointF(-tw * 0.8f, -hh - 0.22f * r), new PointF(tw * 0.8f, -hh - 0.22f * r), new PointF(tw, -hh) };
            Color cc = Color.FromArgb(226, 184, 128); if (_pdark) cc = Col.Dark(cc, 0.2f);
            P.Poly(g, cap, Col.Light(cc, 0.2f), cc, Color.FromArgb(150, 60, 30, 14), 0.03f * r);
            using (Pen p = new Pen(Color.FromArgb(90, 80, 40, 18), 0.03f * r))
                for (int i = 0; i < 4; ++i) g.DrawLine(p, (-0.6f + i * 0.4f) * baseW, -hh * 0.1f - i * 0.1f * r, (-0.62f + i * 0.4f) * baseW * 0.8f, -hh * 0.9f);
        }

        /// <summary>폐허 벽 / 사암 바위. 발자국(직사각)을 뒤쪽으로 들어 올려 높이를 준다.</summary>
        public void DrawRuin(IGfx g, ViewState v, BlockRow b, int index)
        {
            float x0 = v.ToScreenX(b.X), y0 = v.ToScreenY(b.Y);
            float w = v.Px(b.W), h = v.Px(b.H);
            if (x0 > v.Width || y0 > v.Height + 80 || x0 + w < 0 || y0 + h < 0) return;
            float lift = Math.Min(v.Px(2.6f), Math.Max(10f, Math.Min(w, h) * 1.3f));
            uint seed = (uint)index * 7 + 3;
            g.FillRectangle(_res.Brush(70, 80, 44, 14), x0 + 4, y0 + h - lift * 0.1f + 3, w, lift * 0.7f);
            // 앞면
            Color front = Color.FromArgb(176, 124, 80);
            using (LinearGradientBrush lb = GfxBrush.Linear(new RectangleF(x0, y0 + h - lift - 1, w, lift + 2), Col.Light(front, 0.05f), Col.Dark(front, 0.35f), LinearGradientMode.Vertical))
                g.FillRectangle(lb, x0, y0 + h - lift, w, lift);
            float bw = Math.Max(8f, v.Px(2.2f));
            for (float bx = 0; bx < w; bx += bw)
            {
                uint hh = Noise.Hash((int)(bx / bw), index, seed);
                g.DrawLine(_res.Pen(70, 60, 30, 14, 1f), x0 + bx, y0 + h - lift, x0 + bx + ((hh & 1) == 0 ? 0 : bw * 0.5f), y0 + h);
            }
            g.DrawLine(_res.Pen(90, 60, 30, 14, 1f), x0, y0 + h - lift * 0.5f, x0 + w, y0 + h - lift * 0.5f);
            // 윗면: 사암 덩어리 무늬
            float ty = y0 - lift;
            g.FillRectangle(_res.Brush(Color.FromArgb(206, 158, 104)), x0, ty, w, h);
            float cell = Math.Max(10f, v.Px(2.6f));
            for (float cy = 0; cy < h; cy += cell)
                for (float cx = 0; cx < w; cx += cell)
                {
                    uint hh = Noise.Hash((int)(cx / cell), (int)(cy / cell), seed + 9);
                    int sh = (int)(hh % 36) - 14;
                    float cw = Math.Min(cell, w - cx), ch = Math.Min(cell, h - cy);
                    g.FillRectangle(_res.Brush(Color.FromArgb(Col.Cl(206 + sh), Col.Cl(158 + sh), Col.Cl(104 + sh))), x0 + cx + 1, ty + cy + 1, cw - 1, ch - 1);
                    if ((hh & 7) == 0) g.FillEllipse(_res.Brush(60, 70, 40, 14), x0 + cx + cw * 0.2f, ty + cy + ch * 0.2f, cw * 0.5f, ch * 0.3f);
                }
            g.DrawRectangle(_res.Pen(150, 96, 56, 28, 1.6f), x0, ty, w, h);
            g.DrawLine(_res.Pen(120, 255, 236, 190, 1.6f), x0 + 1, ty + 1, x0 + w - 1, ty + 1);
            // 모래가 쌓인 아랫모서리
            g.FillEllipse(_res.Brush(150, 226, 190, 128), x0 - 6, y0 + h - 4, w * 0.45f, 9);
            g.FillEllipse(_res.Brush(130, 226, 190, 128), x0 + w * 0.55f, y0 + h - 3, w * 0.5f, 8);
        }
    }
}
