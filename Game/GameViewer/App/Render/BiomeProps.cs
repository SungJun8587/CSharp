//***************************************************************************
// BiomeProps.cs : 설원 / 화산 / 늪의 소품. 장애물(원형)은 반지름으로 작은 것/중간/큰 것을 고르고,
// 직사각 장애물은 폐허 벽, 월드 바깥은 산봉우리/화산/맹그로브 덩어리로 그린다.
// 소품은 크기와 모양 종류별로 한 번 그려 SpriteCache 에 두고 찍는다 (DesertProps 와 같은 방식).
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed partial class BiomeProps : IDisposable
    {
        private readonly Resources _res;
        private readonly SpriteCache _cache = new SpriteCache(600);
        private readonly SpriteCache.PaintFn _paint;
        private MapKind _pkind; private int _psize; private bool _pborder; private float _pr, _ppu; private uint _pseed;

        public BiomeProps(Resources res) { _res = res; _paint = Paint; }
        public void Dispose() { _cache.Dispose(); }

        public void Draw(Graphics g, ViewState v, MapKind kind, ZoneTheme.PropSize size, float wx, float wy, float r, uint seed)
        {
            Place(g, v, kind, (int)size, false, wx, wy, r, seed);
        }

        public void DrawBorder(Graphics g, ViewState v, MapKind kind, float wx, float wy, float r, uint seed)
        {
            Place(g, v, kind, 0, true, wx, wy, r, seed);
        }

        private void Place(Graphics g, ViewState v, MapKind kind, int size, bool border, float wx, float wy, float r, uint seed)
        {
            float sx = v.ToScreenX(wx), sy = v.ToScreenY(wy);
            float pr = v.Px(r);
            if (sx < -pr * 3.5f || sy < -pr * 5.5f || sx > v.Width + pr * 3.5f || sy > v.Height + pr * 2) return;
            float rq = (float)(Math.Max(1, Math.Round(r / 0.3)) * 0.3);
            float ppu = CreatureRenderer.QuantizePpu(v.Scale);
            uint variant = seed % 4;
            _pkind = kind; _psize = size; _pborder = border; _pr = rq; _ppu = ppu; _pseed = variant;
            float wU = border ? 4.2f : 4.4f, hU = border ? 4.4f : (size == 0 ? 3.6f : 5.2f);
            int w = (int)Math.Ceiling(rq * wU * ppu), h = (int)Math.Ceiling(rq * hU * ppu);
            float ax = w / 2f, ay = h - rq * 0.3f * ppu;
            long key = (5L << 44) | ((long)(rq * 10) << 26) | ((long)variant << 20) | ((long)kind << 16) | ((long)size << 12) | ((border ? 1L : 0L) << 10) | (long)(ppu * 2);
            SpriteCache.Entry e = _cache.Get(key, w, h, ax, ay, _paint);
            float k = (float)(v.Scale / ppu);
            float sw = border ? 1.5f : (size == 2 ? 1.25f : (size == 1 ? 0.9f : 0.7f));
            Color shadow = kind == MapKind.Snow ? Color.FromArgb(54, 60, 90, 140) : (kind == MapKind.Volcano || kind == MapKind.Dark || kind == MapKind.Crystal ? Color.FromArgb(90, 0, 0, 0) : (kind == MapKind.Sky ? Color.FromArgb(40, 90, 100, 150) : (kind == MapKind.Coast ? Color.FromArgb(60, 90, 60, 20) : (kind == MapKind.Ruins ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(80, 4, 14, 8)))));
            g.FillEllipse(_res.Brush(shadow), sx - pr * sw * 0.9f, sy - pr * 0.28f, pr * sw * 1.9f, pr * 0.8f);
            if (!border && (kind == MapKind.Volcano || kind == MapKind.Crystal || kind == MapKind.Dark || kind == MapKind.Sky))       // 빛의 맥동: 밑동 둘레 빛
            {
                float pulse = 0.55f + 0.45f * (float)Math.Sin(Environment.TickCount / 420.0 + seed * 1.7);
                Color gc = kind == MapKind.Volcano ? Color.FromArgb(255, 90, 24) : (kind == MapKind.Crystal ? ((seed & 1) == 0 ? Color.FromArgb(110, 230, 255) : Color.FromArgb(255, 120, 230)) : (kind == MapKind.Dark ? Color.FromArgb(160, 100, 255) : Color.FromArgb(255, 228, 140)));
                g.FillEllipse(_res.Brush((int)(46 * pulse), gc.R, gc.G, gc.B), sx - pr * sw * 1.2f, sy - pr * 0.4f, pr * sw * 2.4f, pr * 1.0f);
            }
            g.DrawImage(e.Bmp, sx - ax * k, sy - ay * k, w * k, h * k);
        }

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        private static Color A(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }

        private void Paint(Graphics g)
        {
            g.ScaleTransform(_ppu, _ppu);
            float r = _pr;
            if (_pborder)
            {
                if (_pkind == MapKind.Snow) PeakSnow(g, r); else if (_pkind == MapKind.Volcano) PeakVolcano(g, r); else if (_pkind == MapKind.Swamp) Mangrove(g, r); else PaintBorder2(g, r);
                return;
            }
            switch (_pkind)
            {
                case MapKind.Snow: if (_psize == 0) IceCrystals(g, r); else Spruce(g, r, _psize == 2); break;
                case MapKind.Volcano: if (_psize == 0) LavaSpire(g, r); else if (_psize == 1) CharredTree(g, r); else ObsidianPillar(g, r); break;
                case MapKind.Swamp: if (_psize == 0) Reeds(g, r); else if (_psize == 1) Willow(g, r); else BogTree(g, r); break;
                default: PaintProp2(g, r); break;
            }
        }

        // ============================================================ 설원
        private static void Tier(Graphics g, float cx, float yb, float hw, float hh, Color top, Color bot, float snow)
        {
            PointF[] pts = { new PointF(cx - hw, yb), new PointF(cx - hw * 0.55f, yb - hh * 0.08f), new PointF(cx - hw * 0.15f, yb + hh * 0.04f), new PointF(cx + hw * 0.3f, yb - hh * 0.07f), new PointF(cx + hw, yb), new PointF(cx, yb - hh) };
            P.Poly(g, pts, top, bot, A(190, 10, 28, 24), Math.Max(0.02f, hh * 0.025f));
            using (SolidBrush br = new SolidBrush(A(60, 0, 20, 30))) g.FillPolygon(br, new PointF[] { new PointF(cx + hw * 0.05f, yb - hh * 0.9f), new PointF(cx + hw, yb), new PointF(cx + hw * 0.3f, yb - hh * 0.07f), new PointF(cx, yb - hh * 0.05f) });
            if (snow <= 0) return;
            float sy = yb - hh * (1f - snow), sw = hw * snow;
            PointF[] cap = { new PointF(cx, yb - hh), new PointF(cx + sw, sy), new PointF(cx + sw * 0.55f, sy + hh * 0.05f), new PointF(cx + sw * 0.2f, sy - hh * 0.03f), new PointF(cx - sw * 0.25f, sy + hh * 0.06f), new PointF(cx - sw * 0.6f, sy - hh * 0.01f), new PointF(cx - sw, sy) };
            P.Poly(g, cap, C(255, 255, 255), C(206, 224, 244), A(120, 90, 120, 160), Math.Max(0.015f, hh * 0.015f));
        }

        private void Spruce(Graphics g, float r, bool big)
        {
            int tiers = big ? 5 : 4;
            float h = (big ? 4.4f : 3.5f) * r, w0 = (big ? 1.7f : 1.35f) * r;
            P.Round(g, -0.17f * r, -0.5f * r, 0.34f * r, 0.5f * r, 0.08f * r, C(104, 76, 54), C(58, 40, 30), A(180, 20, 12, 8), 0.02f * r);
            Color g1 = C(54 + (int)(_pseed % 3) * 6, 112, 94), g2 = C(24, 62, 56);
            for (int i = 0; i < tiers; ++i)
            {
                float t = i / (float)tiers;
                float yb = -0.45f * r - (h - 0.5f * r) * t * 0.78f;
                float hw = w0 * (1f - t * 0.82f), hh = (h - 0.4f * r) * 0.36f;
                Tier(g, 0, yb, hw, hh, Col.Light(g1, t * 0.1f), g2, 0.55f);
            }
            if (_pseed % 2 == 0) { using (SolidBrush br = new SolidBrush(A(230, 255, 255, 255))) g.FillEllipse(br, -0.07f * r, -h - 0.05f * r, 0.14f * r, 0.1f * r); }
        }

        private void IceCrystals(Graphics g, float r)
        {
            float[] xs = { -0.62f, 0.0f, 0.58f, -0.3f, 0.32f }, hs = { 1.5f, 2.3f, 1.7f, 1.1f, 1.2f }, ws = { 0.34f, 0.42f, 0.34f, 0.26f, 0.26f }, ls = { -0.16f, 0.04f, 0.2f, -0.1f, 0.12f };
            using (SolidBrush glow = new SolidBrush(A(40, 140, 230, 255))) g.FillEllipse(glow, -1.4f * r, -1.6f * r, 2.8f * r, 1.9f * r);
            int[] order = { 0, 2, 3, 4, 1 };
            for (int ii = 0; ii < 5; ++ii)
            {
                int i = order[ii];
                float x = xs[i] * r, hh = hs[i] * r * (0.9f + (_pseed % 3) * 0.08f), w = ws[i] * r, lean = ls[i] * r;
                PointF[] body = { new PointF(x - w, 0), new PointF(x - w * 0.9f + lean * 0.3f, -hh * 0.72f), new PointF(x + lean, -hh), new PointF(x + w * 0.9f + lean * 0.3f, -hh * 0.7f), new PointF(x + w, 0) };
                P.Poly(g, body, C(214, 244, 255), C(70, 150, 214), A(200, 30, 90, 140), 0.025f * r);
                using (SolidBrush br = new SolidBrush(A(110, 20, 80, 150))) g.FillPolygon(br, new PointF[] { new PointF(x + lean, -hh), new PointF(x + w * 0.9f + lean * 0.3f, -hh * 0.7f), new PointF(x + w, 0), new PointF(x + w * 0.1f, 0) });
                using (Pen p = new Pen(A(220, 255, 255, 255), 0.04f * r)) g.DrawLine(p, x - w * 0.45f, -hh * 0.12f, x - w * 0.3f + lean * 0.5f, -hh * 0.78f);
                using (Pen p = new Pen(A(90, 255, 255, 255), 0.02f * r)) g.DrawLine(p, x + lean, -hh, x + lean * 0.4f, -hh * 0.1f);
            }
            using (SolidBrush br = new SolidBrush(A(240, 255, 255, 255))) g.FillEllipse(br, -0.8f * r, -0.18f * r, 1.7f * r, 0.3f * r);
        }

        private void PeakSnow(Graphics g, float r)
        {
            float bw = 1.9f * r, hh = (2.6f + (_pseed % 3) * 0.3f) * r;
            float lean = ((int)_pseed - 1.5f) * 0.12f * r;
            PointF[] rock = { new PointF(-bw, 0), new PointF(-bw * 0.7f, -hh * 0.45f), new PointF(-bw * 0.28f + lean, -hh * 0.82f), new PointF(lean, -hh), new PointF(bw * 0.3f + lean, -hh * 0.7f), new PointF(bw * 0.72f, -hh * 0.4f), new PointF(bw, 0) };
            P.Poly(g, rock, C(128, 140, 166), C(62, 74, 102), A(200, 22, 30, 52), 0.03f * r);
            using (SolidBrush br = new SolidBrush(A(90, 14, 22, 50))) g.FillPolygon(br, new PointF[] { new PointF(lean, -hh), new PointF(bw * 0.3f + lean, -hh * 0.7f), new PointF(bw * 0.72f, -hh * 0.4f), new PointF(bw, 0), new PointF(bw * 0.1f, 0), new PointF(lean * 0.6f, -hh * 0.45f) });
            PointF[] cap = { new PointF(lean, -hh), new PointF(bw * 0.3f + lean, -hh * 0.7f), new PointF(bw * 0.18f + lean, -hh * 0.66f), new PointF(lean * 0.6f, -hh * 0.58f), new PointF(-bw * 0.12f + lean, -hh * 0.66f), new PointF(-bw * 0.28f + lean, -hh * 0.82f) };
            P.Poly(g, cap, C(255, 255, 255), C(214, 230, 250), A(110, 90, 120, 160), 0.02f * r);
            using (Pen p = new Pen(A(70, 20, 30, 60), 0.025f * r)) { g.DrawLine(p, -bw * 0.3f, -hh * 0.1f, -bw * 0.1f, -hh * 0.5f); g.DrawLine(p, bw * 0.35f, -hh * 0.05f, bw * 0.2f, -hh * 0.35f); }
        }

        // ============================================================ 화산
        private void LavaSpire(Graphics g, float r)
        {
            float[] xs = { -0.55f, 0.1f, 0.65f, -0.1f }, hs = { 1.5f, 2.4f, 1.2f, 1.2f }, ws = { 0.36f, 0.5f, 0.34f, 0.3f };
            using (SolidBrush glow = new SolidBrush(A(52, 255, 90, 20))) g.FillEllipse(glow, -1.5f * r, -0.5f * r, 3f * r, 0.9f * r);
            int[] order = { 0, 2, 3, 1 };
            for (int ii = 0; ii < 4; ++ii)
            {
                int i = order[ii];
                float x = xs[i] * r, hh = hs[i] * r * (0.9f + (_pseed % 3) * 0.08f), w = ws[i] * r;
                PointF[] b = { new PointF(x - w, 0), new PointF(x - w * 0.7f, -hh * 0.6f), new PointF(x - w * 0.1f, -hh), new PointF(x + w * 0.5f, -hh * 0.55f), new PointF(x + w, 0) };
                P.Poly(g, b, C(96, 84, 92), C(26, 20, 26), A(220, 10, 6, 10), 0.03f * r);
                using (SolidBrush br = new SolidBrush(A(100, 0, 0, 0))) g.FillPolygon(br, new PointF[] { new PointF(x - w * 0.1f, -hh), new PointF(x + w * 0.5f, -hh * 0.55f), new PointF(x + w, 0), new PointF(x + w * 0.1f, 0) });
                using (Pen p = new Pen(A(230, 255, 130, 30), 0.05f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x - w * 0.45f, -hh * 0.05f, x - w * 0.25f, -hh * 0.4f);
                using (Pen p = new Pen(A(220, 255, 200, 90), 0.025f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x - w * 0.45f, -hh * 0.05f, x - w * 0.25f, -hh * 0.4f);
            }
        }

        private void CharredTree(Graphics g, float r)
        {
            Color a = C(58, 48, 50), b = C(18, 14, 16), ol = A(230, 6, 4, 6);
            PointF[] trunk = { new PointF(-0.4f * r, 0), new PointF(-0.24f * r, -1.8f * r), new PointF(-0.1f * r, -3.2f * r), new PointF(0.12f * r, -3.2f * r), new PointF(0.26f * r, -1.7f * r), new PointF(0.42f * r, 0) };
            P.Poly(g, trunk, a, b, ol, 0.03f * r);
            float[,] br = { { -0.1f, -1.9f, -1.3f, -2.7f }, { 0.14f, -2.4f, 1.2f, -3.3f }, { -0.05f, -3.0f, -0.6f, -3.9f }, { 0.18f, -1.3f, 1.1f, -1.7f } };
            int n = 3 + (int)(_pseed % 2);
            for (int i = 0; i < n; ++i)
            {
                using (Pen p = new Pen(ol, 0.24f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, br[i, 0] * r, br[i, 1] * r, br[i, 2] * r, br[i, 3] * r);
                using (Pen p = new Pen(a, 0.15f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, br[i, 0] * r, br[i, 1] * r, br[i, 2] * r, br[i, 3] * r);
                using (SolidBrush e = new SolidBrush(A(60, 255, 110, 30))) g.FillEllipse(e, br[i, 2] * r - 0.3f * r, br[i, 3] * r - 0.3f * r, 0.6f * r, 0.6f * r);
                using (SolidBrush e = new SolidBrush(A(240, 255, 170, 60))) g.FillEllipse(e, br[i, 2] * r - 0.07f * r, br[i, 3] * r - 0.07f * r, 0.14f * r, 0.14f * r);
            }
            using (Pen p = new Pen(A(230, 255, 120, 30), 0.035f * r) { StartCap = LineCap.Round }) { g.DrawLine(p, -0.12f * r, -0.2f * r, -0.04f * r, -1.4f * r); g.DrawLine(p, 0.14f * r, -0.5f * r, 0.18f * r, -1.9f * r); }
            using (SolidBrush e = new SolidBrush(A(150, 255, 90, 20))) g.FillEllipse(e, -0.5f * r, -0.15f * r, 1.0f * r, 0.25f * r);
        }

        private void ObsidianPillar(Graphics g, float r)
        {
            float[] xs = { -0.9f, -0.35f, 0.25f, 0.8f }, ws = { 0.42f, 0.5f, 0.46f, 0.38f }, hs = { 2.3f, 3.9f, 3.0f, 2.0f };
            using (SolidBrush glow = new SolidBrush(A(52, 255, 80, 20))) g.FillEllipse(glow, -1.8f * r, -0.5f * r, 3.6f * r, 1.0f * r);
            int[] order = { 0, 3, 2, 1 };
            for (int ii = 0; ii < 4; ++ii)
            {
                int i = order[ii];
                float x = xs[i] * r, w = ws[i] * r, hh = hs[i] * r * (0.92f + (_pseed % 3) * 0.05f);
                PointF[] b = { new PointF(x - w, 0), new PointF(x - w, -hh * 0.92f), new PointF(x - w * 0.3f, -hh), new PointF(x + w * 0.9f, -hh * 0.95f), new PointF(x + w, -hh * 0.88f), new PointF(x + w, 0) };
                P.Poly(g, b, C(88, 74, 104), C(20, 14, 28), A(230, 8, 4, 12), 0.03f * r);
                using (SolidBrush br = new SolidBrush(A(110, 4, 0, 10))) g.FillRectangle(br, x + w * 0.1f, -hh * 0.9f, w * 0.9f, hh * 0.9f);
                using (Pen p = new Pen(A(170, 214, 190, 255), 0.04f * r)) g.DrawLine(p, x - w * 0.7f, -hh * 0.1f, x - w * 0.7f, -hh * 0.82f);
                float sy = -hh * (0.15f + 0.1f * i);
                using (Pen p = new Pen(A(230, 255, 110, 30), 0.06f * r)) { g.DrawLine(p, x - w * 0.5f, sy, x + w * 0.2f, sy - hh * 0.12f); g.DrawLine(p, x + w * 0.2f, sy - hh * 0.12f, x + w * 0.55f, sy - hh * 0.05f); }
                using (Pen p = new Pen(A(230, 255, 210, 90), 0.025f * r)) { g.DrawLine(p, x - w * 0.5f, sy, x + w * 0.2f, sy - hh * 0.12f); g.DrawLine(p, x + w * 0.2f, sy - hh * 0.12f, x + w * 0.55f, sy - hh * 0.05f); }
            }
        }

        private void PeakVolcano(Graphics g, float r)
        {
            float bw = 2.0f * r, hh = (2.4f + (_pseed % 3) * 0.3f) * r;
            PointF[] rock = { new PointF(-bw, 0), new PointF(-bw * 0.6f, -hh * 0.6f), new PointF(-bw * 0.22f, -hh * 0.95f), new PointF(bw * 0.22f, -hh * 0.95f), new PointF(bw * 0.6f, -hh * 0.55f), new PointF(bw, 0) };
            P.Poly(g, rock, C(70, 52, 54), C(24, 14, 16), A(220, 8, 4, 6), 0.03f * r);
            using (SolidBrush br = new SolidBrush(A(100, 0, 0, 0))) g.FillPolygon(br, new PointF[] { new PointF(0, -hh), new PointF(bw * 0.22f, -hh * 0.95f), new PointF(bw * 0.6f, -hh * 0.55f), new PointF(bw, 0), new PointF(0, 0) });
            using (SolidBrush br = new SolidBrush(A(230, 255, 120, 30))) g.FillEllipse(br, -bw * 0.24f, -hh * 1.0f, bw * 0.48f, hh * 0.1f);
            using (SolidBrush br = new SolidBrush(A(230, 255, 210, 90))) g.FillEllipse(br, -bw * 0.12f, -hh * 0.99f, bw * 0.24f, hh * 0.05f);
            using (Pen p = new Pen(A(220, 255, 100, 24), 0.07f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                PointF[] s = { new PointF(bw * 0.04f, -hh * 0.92f), new PointF(bw * 0.14f, -hh * 0.6f), new PointF(bw * 0.06f, -hh * 0.35f), new PointF(bw * 0.2f, -hh * 0.05f) };
                g.DrawCurve(p, s, 0.5f);
            }
        }

        // ============================================================ 늪
        private void Reeds(Graphics g, float r)
        {
            int n = 9 + (int)(_pseed % 3);
            for (int i = 0; i < n; ++i)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                float x0 = (t - 0.5f) * 1.5f * r, len = (1.9f + ((i * 7 + (int)_pseed) % 5) * 0.28f) * r, lean = ((t - 0.5f) * 1.2f + (((i * 3) % 5) - 2) * 0.08f) * r;
                PointF[] c = { new PointF(x0, 0), new PointF(x0 + lean * 0.4f, -len * 0.55f), new PointF(x0 + lean, -len) };
                using (Pen p = new Pen(A(230, 38, 62, 30), 0.12f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, c, 0.5f);
                using (Pen p = new Pen(A(200, 128, 158, 74), 0.045f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, c, 0.5f);
                if (i % 3 == 1)
                {
                    float hy = -len * 0.82f, hx = x0 + lean * 0.8f;
                    P.Round(g, hx - 0.09f * r, hy - 0.3f * r, 0.18f * r, 0.5f * r, 0.08f * r, C(128, 84, 48), C(58, 36, 22), A(200, 20, 12, 8), 0.015f * r);
                }
            }
            using (SolidBrush br = new SolidBrush(A(180, 28, 40, 22))) g.FillEllipse(br, -0.9f * r, -0.15f * r, 1.8f * r, 0.3f * r);
        }

        private void Willow(Graphics g, float r)
        {
            // 맹그로브: 활처럼 굽은 버팀뿌리 + 어두운 줄기 + 늘어지는 잎 덩이와 이끼
            Color bark = C(70, 56, 42), bd = C(30, 22, 16);
            for (int i = -2; i <= 2; ++i)
            {
                float x1 = i * 0.5f * r;
                PointF[] root = { new PointF(x1, 0), new PointF(x1 * 0.7f, -0.6f * r), new PointF(x1 * 0.25f, -1.3f * r) };
                using (Pen p = new Pen(A(230, 14, 10, 8), 0.2f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, root, 0.5f);
                using (Pen p = new Pen(bark, 0.12f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, root, 0.5f);
            }
            PointF[] trunk = { new PointF(-0.32f * r, -1.1f * r), new PointF(-0.24f * r, -2.3f * r), new PointF(-0.12f * r, -3.0f * r), new PointF(0.14f * r, -3.0f * r), new PointF(0.26f * r, -2.2f * r), new PointF(0.34f * r, -1.1f * r) };
            P.Poly(g, trunk, bark, bd, A(220, 12, 8, 6), 0.03f * r);
            float[,] blobs = { { -0.9f, -3.2f, 1.0f }, { 0.9f, -3.1f, 0.95f }, { 0f, -3.8f, 1.15f }, { -0.45f, -3.2f, 0.9f }, { 0.45f, -3.35f, 0.9f } };
            Color d = C(26, 52, 34), m = C(50, 92, 48), l = C(104, 138, 66);
            for (int i = 0; i < 5; ++i)
            {
                float cx = blobs[i, 0] * r, cy = blobs[i, 1] * r, rr = blobs[i, 2] * r;
                using (SolidBrush br = new SolidBrush(d)) g.FillEllipse(br, cx - rr, cy - rr * 0.7f, rr * 2, rr * 1.4f);
                using (SolidBrush br = new SolidBrush(m)) g.FillEllipse(br, cx - rr * 0.88f, cy - rr * 0.68f, rr * 1.76f, rr * 1.1f);
                using (SolidBrush br = new SolidBrush(l)) g.FillEllipse(br, cx - rr * 0.55f, cy - rr * 0.62f, rr * 0.95f, rr * 0.5f);
            }
            Hang(g, r, -1.6f, 6);
        }

        private void Hang(Graphics g, float r, float spread, int n)
        {
            for (int i = 0; i < n; ++i)
            {
                float x = (-1.5f + 3f * i / (n - 1)) * r, y0 = -2.9f * r + Math.Abs(x) * 0.12f, len = (0.7f + ((i * 5 + (int)_pseed) % 4) * 0.28f) * r;
                PointF[] c = { new PointF(x, y0), new PointF(x + 0.05f * r, y0 + len * 0.5f), new PointF(x - 0.04f * r, y0 + len) };
                using (Pen p = new Pen(A(220, 78, 112, 62), 0.07f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, c, 0.5f);
                using (Pen p = new Pen(A(150, 160, 190, 110), 0.025f * r)) g.DrawCurve(p, c, 0.5f);
            }
        }

        private void BogTree(Graphics g, float r)
        {
            Color bark = C(66, 54, 42), bd = C(26, 20, 16);
            // 굵은 줄기와 널따란 밑동
            PointF[] trunk = { new PointF(-1.0f * r, 0), new PointF(-0.5f * r, -0.8f * r), new PointF(-0.38f * r, -2.4f * r), new PointF(-0.3f * r, -3.6f * r), new PointF(0.34f * r, -3.6f * r), new PointF(0.44f * r, -2.3f * r), new PointF(0.56f * r, -0.8f * r), new PointF(1.1f * r, 0) };
            P.Poly(g, trunk, bark, bd, A(230, 10, 8, 6), 0.035f * r);
            using (SolidBrush br = new SolidBrush(A(70, 0, 0, 0))) g.FillPolygon(br, new PointF[] { new PointF(0.06f * r, -3.6f * r), new PointF(0.34f * r, -3.6f * r), new PointF(0.44f * r, -2.3f * r), new PointF(0.56f * r, -0.8f * r), new PointF(1.1f * r, 0), new PointF(0.1f * r, 0) });
            using (Pen p = new Pen(A(120, 12, 8, 6), 0.03f * r)) { g.DrawLine(p, -0.2f * r, -0.2f * r, -0.18f * r, -3.0f * r); g.DrawLine(p, 0.1f * r, -0.3f * r, 0.14f * r, -2.6f * r); }
            using (SolidBrush br = new SolidBrush(A(210, 70, 110, 52))) g.FillEllipse(br, -0.6f * r, -1.4f * r, 0.5f * r, 0.9f * r);      // 이끼
            float[,] blobs = { { -1.5f, -3.9f, 1.25f }, { 1.5f, -3.8f, 1.2f }, { 0f, -4.5f, 1.5f }, { -0.8f, -3.9f, 1.2f }, { 0.8f, -4.0f, 1.2f }, { -1.1f, -4.6f, 0.95f }, { 1.1f, -4.6f, 0.95f } };
            Color d = C(20, 42, 30), m = C(40, 76, 42), l = C(86, 118, 54);
            for (int i = 0; i < 7; ++i)
            {
                float cx = blobs[i, 0] * r, cy = blobs[i, 1] * r, rr = blobs[i, 2] * r;
                using (SolidBrush br = new SolidBrush(d)) g.FillEllipse(br, cx - rr, cy - rr * 0.7f, rr * 2, rr * 1.4f);
                using (SolidBrush br = new SolidBrush(m)) g.FillEllipse(br, cx - rr * 0.88f, cy - rr * 0.68f, rr * 1.76f, rr * 1.1f);
                using (SolidBrush br = new SolidBrush(l)) g.FillEllipse(br, cx - rr * 0.55f, cy - rr * 0.62f, rr * 0.95f, rr * 0.5f);
            }
            for (int i = 0; i < 9; ++i)
            {
                float x = (-2.2f + 4.4f * i / 8f) * r, y0 = -3.7f * r + Math.Abs(x) * 0.12f, len = (0.9f + ((i * 5 + (int)_pseed) % 4) * 0.4f) * r;
                PointF[] c = { new PointF(x, y0), new PointF(x + 0.06f * r, y0 + len * 0.5f), new PointF(x - 0.05f * r, y0 + len) };
                using (Pen p = new Pen(A(220, 74, 108, 58), 0.08f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, c, 0.5f);
                using (Pen p = new Pen(A(140, 150, 184, 100), 0.025f * r)) g.DrawCurve(p, c, 0.5f);
            }
        }

        private void Mangrove(Graphics g, float r)
        {
            Color d = C(14, 30, 22), m = C(28, 54, 34), l = C(54, 86, 46);
            float[,] blobs = { { -1.0f, -1.4f, 1.2f }, { 1.0f, -1.3f, 1.2f }, { 0f, -2.0f, 1.4f }, { -0.5f, -1.2f, 1.1f }, { 0.55f, -1.2f, 1.1f } };
            for (int i = 0; i < 5; ++i)
            {
                float cx = blobs[i, 0] * r, cy = blobs[i, 1] * r, rr = blobs[i, 2] * r;
                using (SolidBrush br = new SolidBrush(d)) g.FillEllipse(br, cx - rr, cy - rr * 0.75f, rr * 2, rr * 1.5f);
                using (SolidBrush br = new SolidBrush(m)) g.FillEllipse(br, cx - rr * 0.88f, cy - rr * 0.7f, rr * 1.76f, rr * 1.15f);
                using (SolidBrush br = new SolidBrush(l)) g.FillEllipse(br, cx - rr * 0.5f, cy - rr * 0.64f, rr * 0.85f, rr * 0.45f);
            }
            for (int i = -2; i <= 2; ++i)
                using (Pen p = new Pen(A(220, 20, 14, 10), 0.14f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, i * 0.45f * r, 0, i * 0.3f * r, -0.8f * r);
        }

        // ============================================================ 직사각 장애물: 폐허 벽 / 바위
        public void DrawRuin(Graphics g, ViewState v, MapKind kind, BlockRow b, int index)
        {
            float x0 = v.ToScreenX(b.X), y0 = v.ToScreenY(b.Y);
            float w = v.Px(b.W), h = v.Px(b.H);
            if (x0 > v.Width || y0 > v.Height + 80 || x0 + w < 0 || y0 + h < 0) return;
            float lift = Math.Min(v.Px(2.6f), Math.Max(10f, Math.Min(w, h) * 1.3f));
            uint seed = (uint)index * 7 + 3;
            Color front, top, topLo, cap, edge, mortar; int shA;
            switch (kind)
            {
                case MapKind.Snow: front = C(118, 136, 170); top = C(172, 192, 220); topLo = C(130, 150, 184); cap = C(252, 254, 255); edge = A(180, 36, 52, 86); mortar = A(80, 30, 46, 80); shA = 60; break;
                case MapKind.Volcano: front = C(60, 48, 54); top = C(92, 78, 86); topLo = C(54, 44, 50); cap = C(255, 120, 40); edge = A(220, 8, 4, 8); mortar = A(110, 255, 100, 30); shA = 90; break;
                case MapKind.Ruins: front = C(138, 132, 120); top = C(176, 170, 156); topLo = C(120, 114, 104); cap = C(110, 150, 76); edge = A(200, 30, 28, 26); mortar = A(90, 30, 28, 26); shA = 80; break;
                case MapKind.Coast: front = C(150, 112, 74); top = C(196, 156, 106); topLo = C(150, 114, 76); cap = C(240, 230, 200); edge = A(200, 70, 44, 24); mortar = A(110, 60, 36, 18); shA = 60; break;
                case MapKind.Crystal: front = C(48, 42, 70); top = C(76, 68, 104); topLo = C(40, 34, 60); cap = C(110, 240, 255); edge = A(220, 8, 6, 18); mortar = A(110, 110, 230, 255); shA = 90; break;
                case MapKind.Sky: front = C(222, 224, 240); top = C(246, 246, 255); topLo = C(206, 210, 232); cap = C(240, 204, 120); edge = A(180, 150, 160, 200); mortar = A(110, 200, 170, 90); shA = 40; break;
                case MapKind.Dark: front = C(40, 32, 52); top = C(66, 54, 84); topLo = C(30, 24, 40); cap = C(190, 120, 255); edge = A(230, 6, 4, 10); mortar = A(110, 190, 120, 255); shA = 100; break;
                default: front = C(92, 98, 80); top = C(122, 130, 98); topLo = C(80, 88, 64); cap = C(86, 132, 56); edge = A(200, 16, 22, 12); mortar = A(90, 20, 28, 16); shA = 80; break;
            }
            g.FillRectangle(_res.Brush(shA, kind == MapKind.Snow ? 60 : 0, kind == MapKind.Snow ? 80 : 0, kind == MapKind.Snow ? 130 : 0), x0 + 4, y0 + h - lift * 0.1f + 3, w, lift * 0.7f);
            using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x0, y0 + h - lift - 1, w, lift + 2), Col.Light(front, 0.06f), Col.Dark(front, 0.4f), LinearGradientMode.Vertical))
                g.FillRectangle(lb, x0, y0 + h - lift, w, lift);
            float bw = Math.Max(8f, v.Px(2.2f));
            for (float bx = 0; bx < w; bx += bw)
            {
                uint hh = Noise.Hash((int)(bx / bw), index, seed);
                g.DrawLine(_res.Pen(mortar, 1f), x0 + bx, y0 + h - lift, x0 + bx + ((hh & 1) == 0 ? 0 : bw * 0.5f), y0 + h);
            }
            g.DrawLine(_res.Pen(mortar, 1f), x0, y0 + h - lift * 0.5f, x0 + w, y0 + h - lift * 0.5f);
            float ty = y0 - lift;
            g.FillRectangle(_res.Brush(top), x0, ty, w, h);
            float cell = Math.Max(10f, v.Px(2.6f));
            for (float cy = 0; cy < h; cy += cell)
                for (float cx = 0; cx < w; cx += cell)
                {
                    uint hh = Noise.Hash((int)(cx / cell), (int)(cy / cell), seed + 9);
                    int sh = (int)(hh % 30) - 12;
                    float cw = Math.Min(cell, w - cx), ch = Math.Min(cell, h - cy);
                    Color cc = Color.FromArgb(Col.Cl(top.R + sh), Col.Cl(top.G + sh), Col.Cl(top.B + sh));
                    g.FillRectangle(_res.Brush(cc), x0 + cx + 1, ty + cy + 1, cw - 1, ch - 1);
                    if ((hh & 7) == 0) g.FillEllipse(_res.Brush(Col.Alpha(topLo, 120)), x0 + cx + cw * 0.2f, ty + cy + ch * 0.2f, cw * 0.5f, ch * 0.3f);
                    if (kind == MapKind.Volcano && (hh & 15) == 1) g.DrawLine(_res.Pen(200, 255, 110, 30, 1.6f), x0 + cx + 2, ty + cy + ch * 0.5f, x0 + cx + cw - 2, ty + cy + ch * 0.4f);
                }
            g.DrawRectangle(_res.Pen(edge, 1.6f), x0, ty, w, h);
            if (kind == MapKind.Snow)
            {
                g.FillRectangle(_res.Brush(cap), x0 - 1, ty - 2, w + 2, Math.Max(4f, h * 0.45f));
                g.FillEllipse(_res.Brush(cap), x0 - 3, ty + h * 0.2f, w * 0.4f, Math.Max(5f, h * 0.5f));
                g.FillEllipse(_res.Brush(cap), x0 + w * 0.6f, ty + h * 0.1f, w * 0.42f, Math.Max(5f, h * 0.5f));
                g.DrawLine(_res.Pen(120, 150, 180, 220, 1.2f), x0, ty + h * 0.45f, x0 + w, ty + h * 0.45f);
            }
            else if (kind == MapKind.Crystal || kind == MapKind.Dark || kind == MapKind.Sky || kind == MapKind.Ruins || kind == MapKind.Coast)
            {
                Color gl = kind == MapKind.Crystal ? C(110, 240, 255) : (kind == MapKind.Dark ? C(190, 120, 255) : (kind == MapKind.Sky ? C(240, 204, 120) : (kind == MapKind.Coast ? C(238, 226, 196) : C(110, 150, 76))));
                g.DrawLine(_res.Pen(Col.Alpha(gl, 130), 3f), x0 + 1, ty + 1, x0 + w - 1, ty + 1);
                g.DrawLine(_res.Pen(Col.Alpha(Col.Light(gl, 0.5f), 230), 1.2f), x0 + 1, ty + 1, x0 + w - 1, ty + 1);
                if (kind == MapKind.Ruins) for (float vx = x0 + 6; vx < x0 + w - 4; vx += 14) g.DrawLine(_res.Pen(190, 64, 104, 48, 1.6f), vx, ty + h, vx + ((int)vx % 3 - 1), y0 + h - lift * (0.4f + ((int)vx % 4) * 0.12f));
            }
            else if (kind == MapKind.Volcano)
            {
                g.DrawLine(_res.Pen(150, 255, 90, 20, 3f), x0 + 1, ty + 1, x0 + w - 1, ty + 1);
                g.DrawLine(_res.Pen(230, 255, 190, 80, 1.2f), x0 + 1, ty + 1, x0 + w - 1, ty + 1);
            }
            else
            {
                g.FillEllipse(_res.Brush(A(220, 72, 118, 50)), x0 - 3, ty - 3, w * 0.5f, Math.Max(5f, h * 0.5f));
                g.FillEllipse(_res.Brush(A(200, 100, 146, 62)), x0 + w * 0.5f, ty - 2, w * 0.4f, Math.Max(4f, h * 0.35f));
                for (float vx = x0 + 6; vx < x0 + w - 4; vx += 14)
                    g.DrawLine(_res.Pen(190, 60, 100, 48, 1.6f), vx, ty + h, vx + ((int)vx % 3 - 1), y0 + h - lift * (0.4f + ((int)vx % 4) * 0.12f));
            }
        }
    }
}
