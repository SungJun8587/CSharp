//***************************************************************************
// Ui.cs : HUD 공통 부품. 알약형 체력바(잔상/광택/눈금), 금속 테두리 프레임, 스킬 아이콘 문양.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;

namespace GameViewer.App.Render
{
    public static class Ui
    {
        /// <summary>알약형 체력바. top/bot 은 채움 그라데이션. ghost 는 피격 잔상 비율(없으면 ratio).</summary>
        public static void HpBar(Graphics g, Resources res, float x, float y, float w, float h, float ratio, float ghost, Color top, Color bot, bool border)
        {
            float r = Math.Min(h / 2f, 9f);
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            ghost = Math.Max(ratio, Math.Min(1f, ghost));
            using (GraphicsPath track = P.RoundPath(x, y, w, h, r))
            {
                g.FillPath(res.Brush(225, 10, 10, 14), track);
                GraphicsState gs = g.Save();
                g.SetClip(track, CombineMode.Intersect);
                if (ghost > ratio + 0.002f)
                    g.FillRectangle(res.Brush(235, 255, 244, 214), x + w * ratio, y, w * (ghost - ratio), h);
                if (ratio > 0.002f)
                {
                    float fw = Math.Max(2f, w * ratio);
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, fw + 1, h + 1), top, bot, LinearGradientMode.Vertical))
                        g.FillRectangle(b, x, y, fw, h);
                    // 윗면 광택 + 아랫면 어둠
                    using (LinearGradientBrush gl = new LinearGradientBrush(new RectangleF(x, y, fw + 1, h * 0.55f + 1), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                        g.FillRectangle(gl, x, y, fw, h * 0.55f);
                    // 채움 끝의 밝은 선
                    if (ratio < 0.995f) g.FillRectangle(res.Brush(170, 255, 255, 255), x + fw - 1.5f, y, 1.5f, h);
                }
                // 안쪽 그림자(위쪽)
                g.FillRectangle(res.Brush(70, 0, 0, 0), x, y, w, Math.Max(1f, h * 0.12f));
                g.Restore(gs);
                if (border) g.DrawPath(res.Pen(235, 8, 8, 10, 1.6f), track);
                if (border) using (GraphicsPath inner = P.RoundPath(x + 0.8f, y + 0.8f, w - 1.6f, h - 1.6f, Math.Max(0f, r - 0.8f)))
                    g.DrawPath(res.Pen(70, 255, 255, 255, 1f), inner);
            }
        }

        public static void HpColors(float ratio, out Color top, out Color bot)
        {
            Color c = WorldRenderer.HpColor(ratio);
            top = Col.Light(c, 0.30f);
            bot = Col.Dark(c, 0.38f);
        }

        /// <summary>몬스터/적 체력바 색.</summary>
        public static void FoeColors(out Color top, out Color bot)
        {
            top = Color.FromArgb(244, 96, 74); bot = Color.FromArgb(138, 24, 30);
        }

        /// <summary>금속 테두리를 두른 어두운 판. accent 는 테두리 색조.</summary>
        public static void Frame(Graphics g, Resources res, float x, float y, float w, float h, float r, Color accent)
        {
            using (GraphicsPath p = P.RoundPath(x, y, w, h, r))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, w, h + 1), Color.FromArgb(225, 34, 38, 46), Color.FromArgb(235, 12, 14, 20), LinearGradientMode.Vertical))
                    g.FillPath(b, p);
                using (LinearGradientBrush e = new LinearGradientBrush(new RectangleF(x, y, w, h + 1), Col.Light(accent, 0.35f), Col.Dark(accent, 0.45f), LinearGradientMode.Vertical))
                using (Pen pen = new Pen(e, 2f))
                    g.DrawPath(pen, p);
            }
            using (GraphicsPath inner = P.RoundPath(x + 2.5f, y + 2.5f, w - 5f, h - 5f, Math.Max(0f, r - 2f)))
                g.DrawPath(res.Pen(60, 255, 255, 255, 1f), inner);
        }

        public static Color FromRgb(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        // ---- 스킬 아이콘: 연출 양식(SkillFxStyle)마다 고유 문양. 단위 좌표(-1..1)로 그린다.
        private sealed class IconCtx
        {
            public Graphics G; public float Cx, Cy, U; public Color C1, C2, Dk;
            public PointF Pt(float a, float b) { return new PointF(Cx + a * U, Cy + b * U); }
            public PointF[] Pts(params float[] v) { PointF[] r = new PointF[v.Length / 2]; for (int i = 0; i < r.Length; ++i) r[i] = Pt(v[i * 2], v[i * 2 + 1]); return r; }
            public void Line(Color c, float w, float x1, float y1, float x2, float y2)
            {
                PointF a = Pt(x1, y1), b = Pt(x2, y2);
                using (Pen o = new Pen(Color.FromArgb(190, 0, 0, 0), w + 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) G.DrawLine(o, a, b);
                using (Pen p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) G.DrawLine(p, a, b);
            }
            public void Shape(GraphicsPath path, Color top, Color bot)
            {
                RectangleF r = path.GetBounds();
                using (Pen o = new Pen(Color.FromArgb(200, 0, 0, 0), 2.6f) { LineJoin = LineJoin.Round }) G.DrawPath(o, path);
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y, Math.Max(1f, r.Width), Math.Max(1f, r.Height) + 1f), top, bot, LinearGradientMode.Vertical)) G.FillPath(b, path);
                using (Pen e = new Pen(Color.FromArgb(150, 255, 255, 255), 1f) { LineJoin = LineJoin.Round }) G.DrawPath(e, path);
            }
            public void Poly(PointF[] pts, Color top, Color bot) { using (GraphicsPath p = new GraphicsPath()) { p.AddPolygon(pts); Shape(p, top, bot); } }
            public void Disc(float x, float y, float r, Color top, Color bot) { using (GraphicsPath p = new GraphicsPath()) { p.AddEllipse(Cx + (x - r) * U, Cy + (y - r) * U, 2 * r * U, 2 * r * U); Shape(p, top, bot); } }
            public void Arc(Color c, float w, float x, float y, float r, float a0, float sweep)
            {
                using (Pen o = new Pen(Color.FromArgb(190, 0, 0, 0), w + 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) G.DrawArc(o, Cx + (x - r) * U, Cy + (y - r) * U, 2 * r * U, 2 * r * U, a0, sweep);
                using (Pen p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) G.DrawArc(p, Cx + (x - r) * U, Cy + (y - r) * U, 2 * r * U, 2 * r * U, a0, sweep);
            }
            public PointF[] Star(float x, float y, int n, float ro, float ri, double rot)
            {
                PointF[] r = new PointF[n * 2];
                for (int i = 0; i < n * 2; ++i) { double a = rot + i * Math.PI / n; float rr = (i & 1) == 0 ? ro : ri; r[i] = Pt(x + (float)Math.Cos(a) * rr, y + (float)Math.Sin(a) * rr); }
                return r;
            }
        }

        /// <summary>스킬 아이콘: 연출 양식과 연출색으로 만든 문양. 사각 (x,y,s,s) 안에 그린다.</summary>
        public static void SkillIcon(Graphics g, Resources res, float x, float y, float s, SkillRow sk, SkillFxInfo fx, bool basic)
        {
            Color c1 = fx != null ? FromRgb(fx.Color1) : Color.FromArgb(230, 200, 120);
            Color c2 = fx != null ? FromRgb(fx.Color2) : Color.FromArgb(255, 244, 210);
            if (basic) { c1 = Color.FromArgb(190, 198, 214); c2 = Color.White; }
            SkillFxStyle st = fx != null ? fx.Style : SkillFxStyle.Default;
            float cx = x + s / 2f, cy = y + s / 2f, u = s / 2f;
            // 바탕 후광
            using (GraphicsPath hp = new GraphicsPath())
            {
                hp.AddEllipse(cx - u * 1.25f, cy - u * 1.25f, u * 2.5f, u * 2.5f);
                using (PathGradientBrush pg = new PathGradientBrush(hp))
                {
                    pg.CenterColor = Color.FromArgb(150, c1);
                    pg.SurroundColors = new Color[] { Color.FromArgb(0, c1) };
                    g.FillPath(pg, hp);
                }
            }
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            IconCtx k = new IconCtx { G = g, Cx = cx, Cy = cy, U = u, C1 = c1, C2 = c2, Dk = Col.Dark(c1, 0.45f) };
            Color lo = Col.Dark(c1, 0.35f);
            float lw = Math.Max(2.2f, s * 0.1f);
            if (basic || st == SkillFxStyle.Default || st == SkillFxStyle.Pierce)
            {
                // 검: 대각선 칼날 + 날밑 + 손잡이
                k.Poly(k.Pts(0.78f, -0.88f, 0.9f, -0.76f, -0.1f, 0.34f, -0.3f, 0.14f), c2, c1);
                k.Line(Color.FromArgb(230, 70, 56, 40), lw, -0.62f, 0.62f, -0.34f, 0.34f);
                k.Line(c2, lw * 0.8f, -0.62f, 0.2f, -0.2f, 0.62f);
                k.Disc(-0.72f, 0.72f, 0.14f, c2, lo);
            }
            else if (st == SkillFxStyle.Blade)
            {
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddBezier(k.Pt(-0.9f, 0.62f), k.Pt(-0.5f, -0.95f), k.Pt(0.4f, -1.05f), k.Pt(0.95f, -0.5f));
                    p.AddBezier(k.Pt(0.95f, -0.5f), k.Pt(0.35f, -0.52f), k.Pt(-0.45f, -0.3f), k.Pt(-0.9f, 0.62f));
                    k.Shape(p, c2, c1);
                }
                k.Line(c2, lw * 0.6f, 0.15f, 0.3f, 0.75f, 0.85f);
                k.Line(c2, lw * 0.5f, 0.35f, 0.1f, 0.9f, 0.45f);
            }
            else if (st == SkillFxStyle.Whirl)
            {
                k.Arc(c1, lw * 1.2f, 0, 0, 0.7f, 200, 140);
                k.Arc(c1, lw * 1.2f, 0, 0, 0.7f, 20, 140);
                k.Arc(c2, lw * 0.5f, 0, 0, 0.7f, 205, 120);
                k.Arc(c2, lw * 0.5f, 0, 0, 0.7f, 25, 120);
                k.Poly(k.Pts(0.18f, -0.95f, 0.5f, -0.62f, 0.02f, -0.6f), c2, c1);
                k.Poly(k.Pts(-0.18f, 0.95f, -0.5f, 0.62f, -0.02f, 0.6f), c2, c1);
                k.Disc(0, 0, 0.16f, c2, lo);
            }
            else if (st == SkillFxStyle.Arrow || st == SkillFxStyle.Lance)
            {
                bool spear = st == SkillFxStyle.Lance;
                k.Line(Color.FromArgb(255, 210, 190, 150), lw * (spear ? 1.1f : 0.8f), -0.85f, 0.85f, 0.4f, -0.4f);
                if (!spear) { k.Line(c2, lw * 0.7f, -0.85f, 0.85f, -0.55f, 0.85f); k.Line(c2, lw * 0.7f, -0.85f, 0.85f, -0.85f, 0.55f); k.Line(c2, lw * 0.7f, -0.62f, 0.62f, -0.32f, 0.62f); k.Line(c2, lw * 0.7f, -0.62f, 0.62f, -0.62f, 0.32f); }
                else { k.Line(c1, lw * 0.9f, -0.55f, 0.55f, -0.7f, 0.2f); k.Line(c1, lw * 0.9f, -0.55f, 0.55f, -0.2f, 0.7f); }
                k.Poly(k.Pts(0.95f, -0.95f, 0.18f, -0.62f, 0.62f, -0.18f), c2, c1);
            }
            else if (st == SkillFxStyle.Volley)
            {
                float[] xs = { -0.6f, 0f, 0.6f }, ys = { -0.15f, 0.1f, -0.1f };
                for (int i = 0; i < 3; ++i)
                {
                    k.Line(Color.FromArgb(255, 220, 200, 160), lw * 0.7f, xs[i], ys[i] - 0.7f, xs[i], ys[i] + 0.45f);
                    k.Poly(k.Pts(xs[i], ys[i] + 0.9f, xs[i] - 0.2f, ys[i] + 0.42f, xs[i] + 0.2f, ys[i] + 0.42f), c2, c1);
                    k.Line(c2, lw * 0.5f, xs[i], ys[i] - 0.7f, xs[i] - 0.14f, ys[i] - 0.9f);
                    k.Line(c2, lw * 0.5f, xs[i], ys[i] - 0.7f, xs[i] + 0.14f, ys[i] - 0.9f);
                }
            }
            else if (st == SkillFxStyle.Fireball)
            {
                k.Poly(k.Pts(-0.3f, -0.2f, 0.9f, -0.95f, 0.15f, -0.78f, 0.55f, -0.3f, 0.3f, 0.0f), c1, Color.FromArgb(180, c1));
                k.Disc(-0.12f, 0.2f, 0.58f, c2, c1);
                k.Disc(-0.2f, 0.12f, 0.28f, Color.White, c2);
            }
            else if (st == SkillFxStyle.Blast || st == SkillFxStyle.Nova)
            {
                k.Poly(k.Star(0, 0, st == SkillFxStyle.Nova ? 12 : 8, 0.98f, st == SkillFxStyle.Nova ? 0.5f : 0.42f, -Math.PI / 2), c1, lo);
                k.Poly(k.Star(0, 0, st == SkillFxStyle.Nova ? 12 : 8, 0.58f, 0.28f, -Math.PI / 2 + 0.2), c2, c1);
                k.Disc(0, 0, 0.16f, Color.White, c2);
            }
            else if (st == SkillFxStyle.Roar)
            {
                k.Poly(k.Pts(-0.9f, -0.22f, -0.5f, -0.22f, -0.05f, -0.6f, -0.05f, 0.6f, -0.5f, 0.22f, -0.9f, 0.22f), c2, c1);
                k.Arc(c1, lw * 0.9f, -0.05f, 0, 0.45f, -50, 100);
                k.Arc(c1, lw * 0.9f, -0.05f, 0, 0.75f, -45, 90);
                k.Arc(c2, lw * 0.7f, -0.05f, 0, 1.0f, -38, 76);
            }
            else if (st == SkillFxStyle.Quake)
            {
                k.Poly(k.Pts(-0.95f, 0.25f, -0.2f, 0.12f, 0f, 0.95f, -0.95f, 0.95f), c1, lo);
                k.Poly(k.Pts(0.95f, 0.25f, 0.2f, 0.12f, 0.05f, 0.95f, 0.95f, 0.95f), c1, lo);
                k.Line(c2, lw * 0.8f, -0.2f, 0.12f, 0.08f, 0.5f);
                k.Poly(k.Pts(-0.5f, -0.3f, -0.2f, -0.75f, 0.05f, -0.25f), c2, c1);
                k.Poly(k.Pts(0.25f, -0.55f, 0.5f, -0.9f, 0.78f, -0.4f), c2, c1);
            }
            else if (st == SkillFxStyle.Poison)
            {
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddPolygon(k.Pts(0f, -0.95f, -0.52f, 0.05f, 0.52f, 0.05f));
                    p.AddEllipse(cx - 0.6f * u, cy - 0.12f * u, 1.2f * u, 1.0f * u);
                    k.Shape(p, c2, c1);
                }
                k.Disc(-0.2f, 0.3f, 0.12f, Color.White, c2);
                k.Disc(0.75f, -0.4f, 0.12f, c2, c1);
                k.Disc(-0.8f, -0.25f, 0.08f, c2, c1);
            }
            else if (st == SkillFxStyle.Heal)
            {
                if (sk != null && sk.Id == 17)
                {
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddBezier(k.Pt(-0.8f, -0.8f), k.Pt(-0.3f, -0.7f), k.Pt(0.3f, -0.7f), k.Pt(0.8f, -0.8f));
                        p.AddBezier(k.Pt(0.8f, -0.8f), k.Pt(0.9f, 0.3f), k.Pt(0.4f, 0.7f), k.Pt(0f, 0.95f));
                        p.AddBezier(k.Pt(0f, 0.95f), k.Pt(-0.4f, 0.7f), k.Pt(-0.9f, 0.3f), k.Pt(-0.8f, -0.8f));
                        k.Shape(p, c2, c1);
                    }
                    k.Line(lo, lw * 0.8f, 0, -0.55f, 0, 0.6f);
                }
                else
                {
                    float a = 0.62f, t = 0.24f;
                    k.Poly(k.Pts(-t, -a, t, -a, t, -t, a, -t, a, t, t, t, t, a, -t, a, -t, t, -a, t, -a, -t, -t, -t), c2, c1);
                    k.Poly(k.Star(0.72f, -0.72f, 4, 0.26f, 0.06f, -Math.PI / 2), Color.White, c2);
                    k.Poly(k.Star(-0.74f, 0.7f, 4, 0.2f, 0.05f, -Math.PI / 2), Color.White, c2);
                }
            }
            else
            {
                k.Disc(0, 0, 0.6f, c2, c1);
            }
            g.SmoothingMode = old;
        }
    }
}
