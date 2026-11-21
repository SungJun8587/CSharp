//***************************************************************************
// BiomeProps2.cs : 폐허 / 해안 / 수정 동굴 / 천상 / 암흑 지역의 소품 (작은 것 / 중간 / 큰 것)과 월드 바깥 덩어리.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed partial class BiomeProps
    {
        private void PaintProp2(IGfx g, float r)
        {
            switch (_pkind)
            {
                case MapKind.Ruins: if (_psize == 0) RubblePile(g, r); else if (_psize == 1) BrokenColumn(g, r); else RuinArch(g, r); break;
                case MapKind.Coast: if (_psize == 0) RockCoral(g, r); else if (_psize == 1) Palm(g, r, false); else Shipwreck(g, r); break;
                case MapKind.Crystal: if (_psize == 0) CrystalCluster(g, r, 1.0f); else if (_psize == 1) CaveStalag(g, r); else CrystalCluster(g, r, 2.2f); break;
                case MapKind.Sky: if (_psize == 0) GoldLantern(g, r); else if (_psize == 1) Blossom(g, r); else MarblePillar(g, r); break;
                default: if (_psize == 0) SpiritCandles(g, r); else if (_psize == 1) TwistedTree(g, r); else Obelisk(g, r); break;
            }
        }

        private void PaintBorder2(IGfx g, float r)
        {
            switch (_pkind)
            {
                case MapKind.Ruins: WallMass(g, r); break;
                case MapKind.Coast: SeaStack(g, r); break;
                case MapKind.Crystal: CaveRock(g, r); break;
                case MapKind.Sky: CloudPuff(g, r); break;
                default: BlackPeak(g, r); break;
            }
        }

        private static void Glow(IGfx g, float cx, float cy, float rx, float ry, Color c, int a)
        {
            using (SolidBrush br = new SolidBrush(Color.FromArgb(a, c))) g.FillEllipse(br, cx - rx, cy - ry, rx * 2, ry * 2);
        }

        private static void Blob(IGfx g, float cx, float cy, float rr, Color d, Color m, Color l)
        {
            using (SolidBrush br = new SolidBrush(d)) g.FillEllipse(br, cx - rr, cy - rr * 0.7f, rr * 2, rr * 1.4f);
            using (SolidBrush br = new SolidBrush(m)) g.FillEllipse(br, cx - rr * 0.88f, cy - rr * 0.68f, rr * 1.76f, rr * 1.1f);
            using (SolidBrush br = new SolidBrush(l)) g.FillEllipse(br, cx - rr * 0.55f, cy - rr * 0.62f, rr * 0.95f, rr * 0.5f);
        }

        // ============================================================ 폐허
        private void RubblePile(IGfx g, float r)
        {
            float[,] st = { { -0.7f, 0f, 0.55f }, { 0.1f, 0f, 0.7f }, { 0.75f, 0f, 0.48f }, { -0.3f, -0.45f, 0.5f }, { 0.35f, -0.5f, 0.45f }, { 0f, -0.9f, 0.34f } };
            for (int i = 0; i < 6; ++i)
            {
                float x = st[i, 0] * r, y = st[i, 1] * r, s = st[i, 2] * r;
                PointF[] p = { new PointF(x - s, y), new PointF(x - s * 0.8f, y - s * 0.8f), new PointF(x + s * 0.2f, y - s * 1.05f), new PointF(x + s, y - s * 0.5f), new PointF(x + s * 0.9f, y) };
                int t = (int)((_pseed + i) % 3) * 10;
                P.Poly(g, p, C(176 + t, 170 + t, 156 + t), C(96 + t, 92 + t, 86 + t), A(200, 24, 22, 20), 0.03f * r);
            }
            using (SolidBrush b = new SolidBrush(A(210, 82, 118, 56))) g.FillEllipse(b, -0.5f * r, -0.62f * r, 0.9f * r, 0.22f * r);
            P.Round(g, 0.55f * r, -0.5f * r, 0.5f * r, 0.26f * r, 0.03f * r, C(176, 120, 96), C(112, 76, 60), A(180, 30, 18, 14), 0.02f * r);
        }

        private void BrokenColumn(IGfx g, float r)
        {
            float w = 0.55f * r, h = (2.0f + (_pseed % 3) * 0.3f) * r;
            P.Round(g, -w * 1.25f, -0.35f * r, w * 2.5f, 0.35f * r, 0.05f * r, C(184, 178, 164), C(110, 104, 94), A(200, 24, 22, 20), 0.025f * r);
            PointF[] sh = { new PointF(-w, -0.3f * r), new PointF(-w, -h * 0.85f), new PointF(-w * 0.45f, -h * 0.97f), new PointF(w * 0.2f, -h * 0.8f), new PointF(w, -h), new PointF(w, -0.3f * r) };
            P.Poly(g, sh, C(204, 198, 184), C(120, 114, 104), A(210, 24, 22, 20), 0.03f * r);
            using (SolidBrush b = new SolidBrush(A(70, 20, 20, 30))) g.FillRectangle(b, w * 0.25f, -h * 0.95f, w * 0.75f, h * 0.95f);
            using (Pen p = new Pen(A(90, 40, 38, 34), 0.03f * r)) for (int i = -1; i <= 1; ++i) g.DrawLine(p, i * w * 0.5f, -0.4f * r, i * w * 0.5f, -h * 0.85f);
            for (int i = 0; i < 6; ++i) { float x = (-0.8f + i * 0.3f) * w; using (Pen p = new Pen(A(210, 66, 108, 46), 0.07f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x, -h * 0.5f, x + 0.05f * r, -h * 0.5f + (0.3f + (i % 3) * 0.2f) * r); }
            Blob(g, -0.1f * r, -h * 0.6f, 0.5f * r, C(40, 82, 36), C(70, 120, 52), C(120, 164, 80));
        }

        private void RuinArch(IGfx g, float r)
        {
            float w = 0.5f * r, gap = 1.1f * r, h = 3.5f * r;
            foreach (float sgn in new float[] { -1f, 1f })
            {
                float x = sgn * (gap + w);
                P.Round(g, x - w * 1.2f, -0.4f * r, w * 2.4f, 0.4f * r, 0.05f * r, C(190, 184, 170), C(112, 106, 96), A(200, 24, 22, 20), 0.025f * r);
                P.Round(g, x - w, -h, w * 2f, h - 0.3f * r, 0.08f * r, C(206, 200, 186), C(116, 110, 100), A(210, 24, 22, 20), 0.03f * r);
                using (Pen p = new Pen(A(90, 40, 38, 34), 0.03f * r)) { g.DrawLine(p, x - w * 0.4f, -0.5f * r, x - w * 0.4f, -h * 0.95f); g.DrawLine(p, x + w * 0.4f, -0.5f * r, x + w * 0.4f, -h * 0.95f); }
                using (SolidBrush b = new SolidBrush(A(70, 20, 20, 30))) g.FillRectangle(b, x + w * 0.2f, -h, w * 0.8f, h - 0.3f * r);
                P.Round(g, x - w * 1.25f, -h - 0.28f * r, w * 2.5f, 0.3f * r, 0.05f * r, C(214, 208, 194), C(126, 120, 108), A(200, 24, 22, 20), 0.025f * r);
            }
            PointF[] lin = { new PointF(-gap - w * 1.2f, -h - 0.28f * r), new PointF(-gap - w * 0.6f, -h - 0.9f * r), new PointF(-0.3f * r, -h - 0.7f * r), new PointF(-0.1f * r, -h - 0.28f * r) };
            P.Poly(g, lin, C(206, 200, 186), C(122, 116, 106), A(210, 24, 22, 20), 0.03f * r);
            for (int i = 0; i < 7; ++i) { float x = (-2.6f + i * 0.75f) * r; using (Pen p = new Pen(A(220, 62, 104, 44), 0.08f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x, -h - 0.1f * r, x + 0.06f * r, -h + (0.5f + (i % 3) * 0.5f) * r); }
            Blob(g, gap + w, -h * 0.55f, 0.5f * r, C(40, 82, 36), C(70, 120, 52), C(120, 164, 80));
        }

        private void WallMass(IGfx g, float r)
        {
            float bw = 1.9f * r, hh = (1.6f + (_pseed % 3) * 0.3f) * r;
            PointF[] m = { new PointF(-bw, 0), new PointF(-bw, -hh * 0.6f), new PointF(-bw * 0.7f, -hh * 0.55f), new PointF(-bw * 0.55f, -hh), new PointF(-bw * 0.1f, -hh * 0.9f), new PointF(bw * 0.1f, -hh * 0.6f), new PointF(bw * 0.5f, -hh * 0.95f), new PointF(bw, -hh * 0.5f), new PointF(bw, 0) };
            P.Poly(g, m, C(120, 114, 104), C(54, 50, 48), A(220, 16, 14, 14), 0.03f * r);
            using (Pen p = new Pen(A(110, 20, 18, 18), 0.03f * r)) { for (int i = 1; i < 4; ++i) g.DrawLine(p, -bw, -hh * i * 0.25f, bw, -hh * i * 0.25f); for (int i = -3; i <= 3; ++i) g.DrawLine(p, i * bw * 0.28f, -hh * 0.1f, i * bw * 0.28f, -hh * 0.5f); }
            using (SolidBrush b = new SolidBrush(A(200, 70, 112, 48))) g.FillEllipse(b, -bw * 0.9f, -hh * 0.62f, bw * 0.8f, hh * 0.18f);
        }

        // ============================================================ 해안
        private void RockCoral(IGfx g, float r)
        {
            float[,] st = { { -0.6f, 0f, 0.6f }, { 0.35f, 0f, 0.8f }, { -0.1f, -0.5f, 0.5f } };
            for (int i = 0; i < 3; ++i)
            {
                float x = st[i, 0] * r, y = st[i, 1] * r, s = st[i, 2] * r;
                PointF[] p = { new PointF(x - s, y), new PointF(x - s * 0.8f, y - s * 0.9f), new PointF(x + s * 0.1f, y - s * 1.1f), new PointF(x + s, y - s * 0.5f), new PointF(x + s * 0.9f, y) };
                P.Poly(g, p, C(160, 150, 136), C(84, 78, 70), A(200, 30, 26, 22), 0.03f * r);
            }
            for (int i = 0; i < 4; ++i)       // 산호 가지
            {
                float x = (-0.4f + i * 0.35f) * r;
                using (Pen p = new Pen(A(240, 240, 110, 120), 0.1f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(p, x, -0.6f * r, x - 0.1f * r, -1.3f * r); g.DrawLine(p, x - 0.05f * r, -1.0f * r, x + 0.2f * r, -1.4f * r); }
            }
            using (Pen p = new Pen(A(230, 52, 92, 56), 0.08f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(p, 0.7f * r, -0.2f * r, 0.9f * r, -1.0f * r); g.DrawLine(p, 0.8f * r, -0.2f * r, 0.7f * r, -0.8f * r); }
        }

        private void Palm(IGfx g, float r, bool big)
        {
            float lean = ((int)_pseed - 1.5f) * 0.14f;
            PointF[] tr = { new PointF(0, 0), new PointF(0.14f * r + lean * r * 0.5f, -1.4f * r), new PointF((0.22f + lean) * r, -3.0f * r) };
            using (Pen p = new Pen(A(190, 60, 40, 22), 0.46f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, tr, 0.5f);
            using (Pen p = new Pen(C(176, 138, 96), 0.34f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, tr, 0.5f);
            using (Pen p = new Pen(A(120, 90, 60, 36), 0.04f * r)) for (int i = 1; i < 9; ++i) { float t = i / 9f; float yy = -3.0f * r * t, xx = (0.22f + lean) * r * t * t; g.DrawLine(p, xx - 0.16f * r, yy, xx + 0.16f * r, yy - 0.05f * r); }
            float cx = (0.22f + lean) * r, cy = -3.0f * r;
            for (int i = 0; i < 8; ++i)
            {
                double a = -Math.PI * (0.05 + 0.9 * i / 7.0) + (i % 2) * 0.12;
                float L = (1.9f + (i % 3) * 0.25f) * r, dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                float ex = cx + dx * L, ey = cy + dy * L * 0.55f + L * 0.35f, mx = cx + dx * L * 0.55f, my = cy + dy * L * 0.62f - L * 0.05f;
                PointF[] f = { new PointF(cx, cy), new PointF(mx - dy * 0.22f * r, my + dx * 0.1f * r), new PointF(ex, ey), new PointF(mx + dy * 0.22f * r, my - dx * 0.1f * r) };
                P.Poly(g, f, C(110, 176, 70), C(36, 100, 50), A(200, 16, 52, 24), 0.025f * r);
                using (Pen p = new Pen(A(150, 20, 70, 30), 0.03f * r)) g.DrawLine(p, cx, cy, ex, ey);
            }
            for (int i = 0; i < 3; ++i) using (SolidBrush b = new SolidBrush(C(120, 84, 44))) g.FillEllipse(b, cx + (i - 1) * 0.16f * r - 0.1f * r, cy + 0.02f * r, 0.2f * r, 0.2f * r);
        }

        private void Shipwreck(IGfx g, float r)
        {
            Color wd = C(112, 80, 52), wl = C(160, 120, 80), wk = A(220, 28, 18, 10);
            PointF[] hull = { new PointF(-2.0f * r, -0.5f * r), new PointF(-1.7f * r, -1.4f * r), new PointF(1.8f * r, -1.9f * r), new PointF(2.1f * r, -1.0f * r), new PointF(1.6f * r, 0), new PointF(-1.4f * r, 0) };
            P.Poly(g, hull, wl, wd, wk, 0.04f * r);
            for (int i = 0; i < 6; ++i) { float x = (-1.5f + i * 0.65f) * r; P.Round(g, x - 0.1f * r, -2.4f * r + i * 0.08f * r, 0.2f * r, 2.0f * r - i * 0.08f * r + (i % 2) * 0.4f * r, 0.05f * r, wl, wd, wk, 0.03f * r); }
            P.Round(g, -0.1f * r, -4.0f * r, 0.26f * r, 3.0f * r, 0.05f * r, wl, wd, wk, 0.03f * r);
            PointF[] sail = { new PointF(0.16f * r, -3.9f * r), new PointF(1.5f * r, -3.4f * r), new PointF(1.1f * r, -2.8f * r), new PointF(1.4f * r, -2.4f * r), new PointF(0.16f * r, -2.3f * r) };
            P.Poly(g, sail, C(226, 218, 196), C(160, 150, 130), A(200, 70, 60, 44), 0.025f * r);
            using (Pen p = new Pen(A(220, 54, 92, 56), 0.07f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(p, -1.3f * r, -0.1f * r, -1.1f * r, -0.9f * r); g.DrawLine(p, 1.1f * r, -0.1f * r, 1.3f * r, -0.8f * r); }
        }

        private void SeaStack(IGfx g, float r)
        {
            Glow(g, 0, -0.1f * r, 2.0f * r, 0.5f * r, C(255, 255, 255), 180);
            Glow(g, 0, -0.1f * r, 1.5f * r, 0.35f * r, C(160, 226, 236), 160);
            PointF[] m = { new PointF(-1.0f * r, 0), new PointF(-0.8f * r, -1.4f * r), new PointF(-0.2f * r, -2.0f * r), new PointF(0.5f * r, -1.6f * r), new PointF(0.9f * r, -0.8f * r), new PointF(1.1f * r, 0) };
            P.Poly(g, m, C(150, 138, 124), C(70, 64, 60), A(210, 22, 20, 18), 0.03f * r);
            using (SolidBrush b = new SolidBrush(A(80, 10, 10, 20))) g.FillPolygon(b, new PointF[] { new PointF(0.1f * r, -1.9f * r), new PointF(0.5f * r, -1.6f * r), new PointF(0.9f * r, -0.8f * r), new PointF(1.1f * r, 0), new PointF(0.2f * r, 0) });
            Glow(g, 0, 0, 1.2f * r, 0.2f * r, C(255, 255, 255), 220);
        }

        // ============================================================ 수정 동굴
        private void CrystalCluster(IGfx g, float r, float k)
        {
            Color c1 = (_pseed & 1) == 0 ? C(120, 240, 255) : C(255, 130, 236), c2 = (_pseed & 1) == 0 ? C(120, 140, 255) : C(180, 120, 255);
            Glow(g, 0, -1.0f * r * k, 1.8f * r * k, 1.3f * r * k, c1, 34);
            float[] xs = { -0.7f, 0.0f, 0.6f, -0.3f, 0.35f, -1.0f, 0.95f }, hs = { 1.6f, 2.6f, 1.8f, 1.2f, 1.3f, 0.9f, 1.0f }, ws = { 0.36f, 0.46f, 0.36f, 0.28f, 0.28f, 0.24f, 0.24f }, ls = { -0.2f, 0.05f, 0.22f, -0.12f, 0.14f, -0.3f, 0.3f };
            int[] order = { 5, 6, 0, 2, 3, 4, 1 };
            for (int ii = 0; ii < (k > 1.5f ? 7 : 5); ++ii)
            {
                int i = order[k > 1.5f ? ii : ii + 2 > 6 ? 6 : ii + 2];
                float x = xs[i] * r * k * 0.85f, hh = hs[i] * r * k * (0.92f + (_pseed % 3) * 0.06f), w = ws[i] * r * k, lean = ls[i] * r * k;
                Color c = i % 2 == 0 ? c1 : c2;
                PointF[] b = { new PointF(x - w, 0), new PointF(x - w * 0.9f + lean * 0.3f, -hh * 0.72f), new PointF(x + lean, -hh), new PointF(x + w * 0.9f + lean * 0.3f, -hh * 0.7f), new PointF(x + w, 0) };
                P.Poly(g, b, Col.Light(c, 0.6f), Col.Dark(c, 0.35f), A(210, Col.Dark(c, 0.7f).R, Col.Dark(c, 0.7f).G, Col.Dark(c, 0.7f).B), 0.025f * r * k);
                using (SolidBrush br = new SolidBrush(A(100, 10, 10, 50))) g.FillPolygon(br, new PointF[] { new PointF(x + lean, -hh), new PointF(x + w * 0.9f + lean * 0.3f, -hh * 0.7f), new PointF(x + w, 0), new PointF(x + w * 0.1f, 0) });
                using (Pen p = new Pen(A(230, 255, 255, 255), 0.04f * r * k)) g.DrawLine(p, x - w * 0.45f, -hh * 0.1f, x - w * 0.3f + lean * 0.5f, -hh * 0.8f);
            }
        }

        private void CaveStalag(IGfx g, float r)
        {
            PointF[] m = { new PointF(-0.9f * r, 0), new PointF(-0.5f * r, -1.8f * r), new PointF(-0.1f * r, -3.0f * r), new PointF(0.4f * r, -1.8f * r), new PointF(0.9f * r, 0) };
            P.Poly(g, m, C(100, 92, 130), C(26, 22, 44), A(220, 8, 6, 16), 0.03f * r);
            using (SolidBrush b = new SolidBrush(A(100, 4, 2, 14))) g.FillPolygon(b, new PointF[] { new PointF(-0.1f * r, -3.0f * r), new PointF(0.4f * r, -1.8f * r), new PointF(0.9f * r, 0), new PointF(0.1f * r, 0) });
            Color c = (_pseed & 1) == 0 ? C(120, 240, 255) : C(255, 130, 236);
            for (int i = 0; i < 3; ++i)
            {
                float x = (-0.45f + i * 0.4f) * r, h = (0.7f + (i % 2) * 0.4f) * r, y = -(0.5f + i * 0.35f) * r;
                PointF[] cs = { new PointF(x - 0.12f * r, y), new PointF(x, y - h), new PointF(x + 0.12f * r, y) };
                Glow(g, x, y - h * 0.4f, 0.4f * r, 0.5f * r, c, 40);
                P.Poly(g, cs, Col.Light(c, 0.5f), Col.Dark(c, 0.3f), A(200, 10, 20, 40), 0.02f * r);
            }
        }

        private void CaveRock(IGfx g, float r)
        {
            float bw = 1.9f * r, hh = (2.2f + (_pseed % 3) * 0.4f) * r;
            PointF[] m = { new PointF(-bw, 0), new PointF(-bw * 0.8f, -hh * 0.5f), new PointF(-bw * 0.4f, -hh * 0.9f), new PointF(-bw * 0.1f, -hh * 0.6f), new PointF(bw * 0.2f, -hh), new PointF(bw * 0.7f, -hh * 0.5f), new PointF(bw, 0) };
            P.Poly(g, m, C(56, 48, 82), C(14, 10, 26), A(230, 4, 2, 10), 0.03f * r);
            using (SolidBrush b = new SolidBrush(A(110, 2, 0, 10))) g.FillPolygon(b, new PointF[] { new PointF(bw * 0.2f, -hh), new PointF(bw * 0.7f, -hh * 0.5f), new PointF(bw, 0), new PointF(0, 0) });
            if ((_pseed & 1) == 0) { Color c = C(120, 240, 255); Glow(g, -bw * 0.3f, -hh * 0.4f, 0.5f * r, 0.5f * r, c, 40); PointF[] cs = { new PointF(-bw * 0.4f, -hh * 0.3f), new PointF(-bw * 0.3f, -hh * 0.75f), new PointF(-bw * 0.2f, -hh * 0.3f) }; P.Poly(g, cs, Col.Light(c, 0.5f), Col.Dark(c, 0.3f), A(200, 10, 20, 40), 0.02f * r); }
        }

        // ============================================================ 천상
        private void GoldLantern(IGfx g, float r)
        {
            P.Round(g, -0.7f * r, -0.4f * r, 1.4f * r, 0.4f * r, 0.08f * r, C(250, 250, 255), C(190, 196, 222), A(180, 120, 130, 170), 0.02f * r);
            P.Round(g, -0.12f * r, -1.9f * r, 0.24f * r, 1.6f * r, 0.06f * r, C(246, 214, 130), C(184, 144, 70), A(200, 110, 80, 30), 0.02f * r);
            Glow(g, 0, -2.2f * r, 1.1f * r, 1.1f * r, C(255, 232, 150), 50);
            Glow(g, 0, -2.2f * r, 0.5f * r, 0.5f * r, C(255, 244, 190), 150);
            P.Ball(g, 0, -2.2f * r, 0.34f * r, 0.34f * r, C(255, 255, 240), C(255, 214, 120), A(220, 180, 130, 40), 0.02f * r);
            for (int i = 0; i < 2; ++i) { float s = i == 0 ? -1f : 1f; using (Pen p = new Pen(A(230, 236, 200, 110), 0.07f * r) { StartCap = LineCap.Round }) g.DrawArc(p, s * 0.1f * r - 0.4f * r, -1.35f * r, 0.8f * r, 0.6f * r, s < 0 ? 90 : 270, 180); }
        }

        private void Blossom(IGfx g, float r)
        {
            PointF[] tr = { new PointF(0, 0), new PointF(-0.1f * r, -1.3f * r), new PointF(0.04f * r, -2.4f * r) };
            using (Pen p = new Pen(A(220, 80, 56, 50), 0.4f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, tr, 0.5f);
            using (Pen p = new Pen(C(140, 108, 100), 0.28f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, tr, 0.5f);
            float[,] bl = { { -1.0f, -2.7f, 1.0f }, { 1.0f, -2.6f, 0.95f }, { 0f, -3.3f, 1.2f }, { -0.5f, -2.7f, 0.9f }, { 0.5f, -2.8f, 0.9f } };
            for (int i = 0; i < 5; ++i) Blob(g, bl[i, 0] * r, bl[i, 1] * r, bl[i, 2] * r, C(236, 180, 206), C(252, 214, 228), C(255, 244, 248));
            for (int i = 0; i < 7; ++i) Glow(g, (-1.6f + i * 0.5f) * r, (-1.9f + (i % 3) * 0.3f) * r, 0.06f * r, 0.05f * r, C(255, 236, 244), 220);
        }

        private void MarblePillar(IGfx g, float r)
        {
            Color m1 = C(250, 250, 255), m2 = C(184, 192, 222), gold1 = C(250, 222, 140), gold2 = C(190, 146, 66), ol = A(190, 110, 120, 160);
            P.Round(g, -1.1f * r, -0.5f * r, 2.2f * r, 0.5f * r, 0.06f * r, m1, m2, ol, 0.025f * r);
            P.Round(g, -0.8f * r, -0.9f * r, 1.6f * r, 0.42f * r, 0.05f * r, m1, m2, ol, 0.025f * r);
            P.Round(g, -0.55f * r, -4.0f * r, 1.1f * r, 3.2f * r, 0.06f * r, m1, m2, ol, 0.03f * r);
            using (SolidBrush b = new SolidBrush(A(50, 60, 70, 130))) g.FillRectangle(b, 0.05f * r, -4.0f * r, 0.5f * r, 3.2f * r);
            using (Pen p = new Pen(A(120, 150, 160, 200), 0.03f * r)) for (int i = -1; i <= 1; ++i) g.DrawLine(p, i * 0.3f * r, -1.0f * r, i * 0.3f * r, -3.9f * r);
            P.Round(g, -0.85f * r, -4.45f * r, 1.7f * r, 0.5f * r, 0.08f * r, gold1, gold2, A(220, 120, 80, 20), 0.03f * r);
            P.Round(g, -0.65f * r, -2.6f * r, 1.3f * r, 0.18f * r, 0.05f * r, gold1, gold2, A(220, 120, 80, 20), 0.02f * r);
            Glow(g, 0, -4.7f * r, 0.9f * r, 0.5f * r, C(255, 236, 170), 60);
        }

        private void CloudPuff(IGfx g, float r)
        {
            float[,] bl = { { -1.1f, -0.7f, 1.0f }, { 1.1f, -0.7f, 1.0f }, { 0f, -1.2f, 1.25f }, { -0.55f, -0.5f, 0.9f }, { 0.6f, -0.5f, 0.9f } };
            for (int i = 0; i < 5; ++i) Blob(g, bl[i, 0] * r, bl[i, 1] * r, bl[i, 2] * r, C(196, 214, 244), C(232, 240, 252), C(255, 255, 255));
        }

        // ============================================================ 암흑
        private void SpiritCandles(IGfx g, float r)
        {
            float[,] cs = { { -0.6f, 0f, 1.0f }, { 0.2f, 0f, 1.6f }, { 0.8f, 0f, 0.8f } };
            for (int i = 0; i < 3; ++i)
            {
                float x = cs[i, 0] * r, h = cs[i, 2] * r;
                Glow(g, x, -h - 0.3f * r, 0.9f * r, 0.9f * r, C(160, 110, 255), 40);
                P.Round(g, x - 0.18f * r, -h, 0.36f * r, h, 0.08f * r, C(206, 200, 214), C(110, 100, 126), A(200, 20, 14, 30), 0.02f * r);
                PointF[] fl = { new PointF(x - 0.14f * r, -h), new PointF(x - 0.1f * r, -h - 0.4f * r), new PointF(x, -h - 0.8f * r), new PointF(x + 0.1f * r, -h - 0.35f * r), new PointF(x + 0.14f * r, -h) };
                using (GraphicsPath gp = new GraphicsPath()) { gp.AddClosedCurve(fl); using (SolidBrush b = new SolidBrush(A(230, 170, 130, 255))) g.FillPath(b, gp); }
                Glow(g, x, -h - 0.3f * r, 0.07f * r, 0.2f * r, C(240, 230, 255), 240);
            }
        }

        private void TwistedTree(IGfx g, float r)
        {
            Color a = C(54, 42, 64), b = C(16, 10, 24), ol = A(235, 4, 2, 8);
            PointF[] tr = { new PointF(-0.45f * r, 0), new PointF(-0.2f * r, -1.2f * r), new PointF(-0.34f * r, -2.3f * r), new PointF(0.0f * r, -3.4f * r), new PointF(0.2f * r, -3.4f * r), new PointF(0.12f * r, -2.3f * r), new PointF(0.3f * r, -1.1f * r), new PointF(0.5f * r, 0) };
            P.Poly(g, tr, a, b, ol, 0.03f * r);
            float[,] br = { { -0.2f, -2.0f, -1.3f, -2.7f }, { 0.1f, -2.6f, 1.3f, -3.3f }, { 0.0f, -3.2f, -0.7f, -4.0f }, { 0.15f, -1.5f, 1.1f, -1.9f }, { -0.1f, -1.2f, -1.0f, -1.4f } };
            Color gl = C(170, 110, 255);
            for (int i = 0; i < 5; ++i)
            {
                using (Pen p = new Pen(ol, 0.24f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, br[i, 0] * r, br[i, 1] * r, br[i, 2] * r, br[i, 3] * r);
                using (Pen p = new Pen(a, 0.14f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, br[i, 0] * r, br[i, 1] * r, br[i, 2] * r, br[i, 3] * r);
                Glow(g, br[i, 2] * r, br[i, 3] * r, 0.3f * r, 0.3f * r, gl, 50);
                Glow(g, br[i, 2] * r, br[i, 3] * r, 0.07f * r, 0.07f * r, C(230, 200, 255), 240);
            }
            Glow(g, -0.04f * r, -2.8f * r, 0.12f * r, 0.08f * r, C(210, 150, 255), 250); Glow(g, 0.12f * r, -2.8f * r, 0.12f * r, 0.08f * r, C(210, 150, 255), 250);       // 눈
        }

        private void Obelisk(IGfx g, float r)
        {
            Color a = C(62, 50, 80), b = C(14, 10, 22), ol = A(235, 4, 2, 8);
            P.Round(g, -1.1f * r, -0.5f * r, 2.2f * r, 0.5f * r, 0.05f * r, a, b, ol, 0.03f * r);
            PointF[] ob = { new PointF(-0.7f * r, -0.5f * r), new PointF(-0.5f * r, -4.0f * r), new PointF(0f, -4.8f * r), new PointF(0.5f * r, -4.0f * r), new PointF(0.7f * r, -0.5f * r) };
            P.Poly(g, ob, a, b, ol, 0.03f * r);
            using (SolidBrush br = new SolidBrush(A(110, 2, 0, 10))) g.FillPolygon(br, new PointF[] { new PointF(0, -4.8f * r), new PointF(0.5f * r, -4.0f * r), new PointF(0.7f * r, -0.5f * r), new PointF(0.05f * r, -0.5f * r) });
            Glow(g, 0, -2.5f * r, 1.2f * r, 2.2f * r, C(160, 100, 255), 30);
            using (Pen p = new Pen(A(230, 190, 130, 255), 0.06f * r) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 5; ++i) { float y = (-1.0f - i * 0.65f) * r; g.DrawLine(p, -0.25f * r, y, 0.0f, y - 0.2f * r); g.DrawLine(p, 0.0f, y - 0.2f * r, 0.25f * r, y); g.DrawLine(p, 0, y - 0.2f * r, 0, y + 0.12f * r); }
            Glow(g, 0, -4.4f * r, 0.18f * r, 0.18f * r, C(240, 220, 255), 250);
        }

        private void BlackPeak(IGfx g, float r)
        {
            float bw = 1.9f * r, hh = (2.8f + (_pseed % 3) * 0.4f) * r;
            PointF[] m = { new PointF(-bw, 0), new PointF(-bw * 0.7f, -hh * 0.45f), new PointF(-bw * 0.5f, -hh * 0.4f), new PointF(-bw * 0.25f, -hh * 0.95f), new PointF(-bw * 0.05f, -hh * 0.6f), new PointF(bw * 0.15f, -hh), new PointF(bw * 0.5f, -hh * 0.5f), new PointF(bw * 0.7f, -hh * 0.7f), new PointF(bw, 0) };
            P.Poly(g, m, C(48, 36, 62), C(10, 6, 18), A(235, 2, 0, 6), 0.03f * r);
            using (SolidBrush b = new SolidBrush(A(110, 0, 0, 8))) g.FillPolygon(b, new PointF[] { new PointF(bw * 0.15f, -hh), new PointF(bw * 0.5f, -hh * 0.5f), new PointF(bw * 0.7f, -hh * 0.7f), new PointF(bw, 0), new PointF(0, 0) });
            using (Pen p = new Pen(A(150, 170, 100, 255), 0.04f * r) { StartCap = LineCap.Round }) g.DrawLine(p, -bw * 0.3f, -hh * 0.92f, -bw * 0.28f, -hh * 0.5f);
        }
    }
}
