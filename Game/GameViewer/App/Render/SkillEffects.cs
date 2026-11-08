//***************************************************************************
// SkillEffects.cs : 스킬별 화려한 연출. Core/Data/SkillFx.cs 의 양식 표에 따라 그린다.
// EffectRenderer 의 기존 연출(범위 예고, 피해 숫자 등) 위에 얹히며, 양식이 Default 인 스킬은 기존 그림을 그대로 쓴다.
// 파티클은 상태를 두지 않고 "연출 시작 시각 + 해시 난수" 로 매 프레임 같은 궤적을 계산한다(스냅샷이 항상 같다).
// 펜/브러시는 알파와 굵기를 단계로 묶어 캐시가 무한히 늘지 않게 한다.
//***************************************************************************
using System;
using System.Drawing;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class SkillEffects
    {
        private readonly Resources _res;
        private readonly EffectRenderer _fx;
        private const float TwoPi = 6.2831853f;

        public SkillEffects(Resources res, EffectRenderer fx) { _res = res; _fx = fx; }

        // ------------------------------------------------------------------ 도구

        private static Color Cc(int rgb, int a) { return Color.FromArgb(a < 0 ? 0 : (a > 255 ? 255 : (a & 0xF0) | (a >> 4)), (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        private Pen Pn(int rgb, int a, float width) { return _res.Pen(Cc(rgb, a), (float)Math.Round(Math.Max(0.5f, width) * 2f) / 2f); }
        private SolidBrush Br(int rgb, int a) { return _res.Brush(Cc(rgb, a)); }
        private static int Mix(int a, int b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            int r = (int)(((a >> 16) & 255) * (1f - t) + ((b >> 16) & 255) * t), g = (int)(((a >> 8) & 255) * (1f - t) + ((b >> 8) & 255) * t), bl = (int)((a & 255) * (1f - t) + (b & 255) * t);
            return (r << 16) | (g << 8) | bl;
        }
        private static float Rn(ViewEffect fx, int i) { return Noise.Hash01(i * 7 + (int)(fx.StartMs & 0x7FFF), (int)(fx.EntityId & 0xFF) + i * 3, 0x77u); }
        private static float Ease(float p) { return 1f - (1f - p) * (1f - p); }
        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        private void Glow(Graphics g, int rgb, float cx, float cy, float r, float k) { _fx.Glow(g, Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255), cx, cy, r, r, k); }

        /// <summary>네 갈래 반짝이.</summary>
        private void Star(Graphics g, int rgb, int a, float cx, float cy, float r, float rot, float w)
        {
            float c = (float)Math.Cos(rot), s = (float)Math.Sin(rot);
            Pen p = Pn(rgb, a, w);
            g.DrawLine(p, cx - c * r, cy - s * r, cx + c * r, cy + s * r);
            g.DrawLine(p, cx + s * r, cy - c * r, cx - s * r, cy + c * r);
            float d = r * 0.55f; float c2 = (float)Math.Cos(rot + 0.785f), s2 = (float)Math.Sin(rot + 0.785f);
            Pen q = Pn(rgb, a / 2, Math.Max(0.5f, w - 1f));
            g.DrawLine(q, cx - c2 * d, cy - s2 * d, cx + c2 * d, cy + s2 * d);
            g.DrawLine(q, cx + s2 * d, cy - c2 * d, cx - s2 * d, cy + c2 * d);
        }

        // ------------------------------------------------------------------ 범위 (바닥)

        /// <summary>범위 폭발의 바닥 부분. 처리했으면 true (false 면 기존 그림을 쓴다).</summary>
        public bool RingGround(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, long now, float cx, float cy, float radius)
        {
            float p = fx.Progress(now);
            float sx = v.ToScreenX(cx), sy = v.ToScreenY(cy);
            float full = v.Px(radius);
            if (sx + full * 1.6f < 0 || sy + full * 1.6f < 0 || sx - full * 1.6f > v.Width || sy - full * 1.6f > v.Height) return true;
            int c1 = info.Color1, c2 = info.Color2;
            float e = Ease(p), fade = 1f - p;
            switch (info.Style)
            {
                case SkillFxStyle.Whirl:
                    {
                        float R = Math.Max(full, v.Px(2.6f));
                        Glow(g, c1, sx, sy, R * 1.15f, 0.55f * fade);
                        g.DrawEllipse(Pn(c1, (int)(150 * fade), 2f), sx - R, sy - R * 0.8f, R * 2, R * 1.6f);
                        for (int k = 0; k < 3; ++k)
                        {
                            float rad = R * (0.5f + 0.2f * k) * (0.75f + 0.25f * e);
                            float start = p * 900f * (k % 2 == 0 ? 1 : -1) + k * 120f;
                            int a = (int)(235 * fade);
                            g.DrawArc(Pn(Mix(c1, c2, k * 0.35f), a, 7f - k * 1.5f), sx - rad, sy - rad * 0.8f, rad * 2, rad * 1.6f, start, 85f);
                            g.DrawArc(Pn(c2, a, 2f), sx - rad, sy - rad * 0.8f, rad * 2, rad * 1.6f, start + 25f, 55f);
                        }
                        return true;
                    }
                case SkillFxStyle.Blast:
                    {
                        float R = Math.Max(full, v.Px(3f));
                        float scorch = Clamp01(p * 6f);
                        g.FillEllipse(Br(0x201208, (int)(150 * (1f - p * p) * scorch)), sx - R * 0.78f * (0.6f + 0.4f * scorch), sy - R * 0.62f * (0.6f + 0.4f * scorch), R * 1.56f * (0.6f + 0.4f * scorch), R * 1.24f * (0.6f + 0.4f * scorch));
                        Glow(g, c1, sx, sy, R * (0.8f + 0.7f * e), 0.95f * fade);
                        float r1 = R * e;
                        g.DrawEllipse(Pn(c2, (int)(240 * fade), 6f * fade + 1.5f), sx - r1, sy - r1 * 0.8f, r1 * 2, r1 * 1.6f);
                        float q = Clamp01((p - 0.14f) / 0.86f);
                        if (q > 0f) { float r2 = R * Ease(q) * 0.85f; g.DrawEllipse(Pn(c1, (int)(190 * (1f - q)), 4f * (1f - q) + 1f), sx - r2, sy - r2 * 0.8f, r2 * 2, r2 * 1.6f); }
                        for (int k = 0; k < 14; ++k)
                        {
                            float a = k * TwoPi / 14f + Rn(fx, 50) * 0.5f;
                            float rr = R * e * (0.55f + 0.4f * Rn(fx, k + 60));
                            Glow(g, Mix(c1, c2, Rn(fx, k + 80)), sx + (float)Math.Cos(a) * rr, sy + (float)Math.Sin(a) * rr * 0.8f, v.Px(0.55f) * (1.1f - p), 0.85f * fade);
                        }
                        return true;
                    }
                case SkillFxStyle.Roar:
                    {
                        float R = Math.Max(full, v.Px(3.4f));
                        Glow(g, c1, sx, sy, R * 0.9f, 0.5f * fade);
                        for (int k = 0; k < 3; ++k)
                        {
                            float q = (p - k * 0.15f) / (1f - k * 0.15f);
                            if (q <= 0f) continue;
                            float r = R * Ease(q);
                            g.DrawEllipse(Pn(Mix(c1, c2, k * 0.4f), (int)(220 * (1f - q)), 5f * (1f - q) + 1f), sx - r, sy - r * 0.8f, r * 2, r * 1.6f);
                        }
                        return true;
                    }
                case SkillFxStyle.Poison:
                    {
                        float R = Math.Max(full, v.Px(2.8f));
                        float f = p < 0.15f ? p / 0.15f : 1f - (p - 0.15f) / 0.85f;
                        g.FillEllipse(Br(c1, (int)(70 * f)), sx - R, sy - R * 0.8f, R * 2, R * 1.6f);
                        g.DrawEllipse(Pn(c2, (int)(120 * f), 2f), sx - R, sy - R * 0.8f, R * 2, R * 1.6f);
                        for (int k = 0; k < 9; ++k)
                        {
                            float a = k * 0.7f + p * 1.1f, d = R * (0.15f + 0.65f * Rn(fx, k));
                            Glow(g, Mix(c1, c2, 0.25f * (k % 3)), sx + (float)Math.Cos(a) * d, sy + (float)Math.Sin(a) * d * 0.8f, R * 0.42f, 0.55f * f);
                        }
                        return true;
                    }
                case SkillFxStyle.Volley:
                    {
                        float R = Math.Max(full, v.Px(3f));
                        g.DrawEllipse(Pn(c1, (int)(110 * fade), 2f), sx - R, sy - R * 0.8f, R * 2, R * 1.6f);
                        int n = VolleyCount(radius);
                        for (int k = 0; k < n; ++k)
                        {
                            float tk = 0.12f + 0.55f * Rn(fx, k);
                            if (p < tk) continue;
                            float u = Clamp01((p - tk) / 0.3f);
                            float a = Rn(fx, k + 100) * TwoPi, d = R * (float)Math.Sqrt(Rn(fx, k + 200)) * 0.92f;
                            float lx = sx + (float)Math.Cos(a) * d, ly = sy + (float)Math.Sin(a) * d * 0.8f;
                            float r = v.Px(0.45f) * (0.3f + u);
                            g.DrawEllipse(Pn(c2, (int)(180 * (1f - u)), 2f), lx - r, ly - r * 0.6f, r * 2, r * 1.2f);
                        }
                        return true;
                    }
                case SkillFxStyle.Nova:
                    {
                        float R = Math.Max(full, v.Px(3.2f));
                        Glow(g, c1, sx, sy, R * (0.6f + 0.5f * e), 0.8f * fade);
                        float r = R * e;
                        g.DrawEllipse(Pn(c1, (int)(150 * fade), 14f * fade + 2f), sx - r, sy - r * 0.8f, r * 2, r * 1.6f);
                        g.DrawEllipse(Pn(c2, (int)(250 * fade), 5f * fade + 1.5f), sx - r, sy - r * 0.8f, r * 2, r * 1.6f);
                        for (int k = 0; k < 16; ++k)
                        {
                            float a = k * TwoPi / 16f + 0.2f;
                            float r0 = r * 0.35f, r1 = r * 0.98f;
                            g.DrawLine(Pn(c2, (int)(170 * fade), 2f), sx + (float)Math.Cos(a) * r0, sy + (float)Math.Sin(a) * r0 * 0.8f, sx + (float)Math.Cos(a) * r1, sy + (float)Math.Sin(a) * r1 * 0.8f);
                        }
                        return true;
                    }
                case SkillFxStyle.Heal:
                    {
                        float R = Math.Max(full, v.Px(2.2f)) * (0.55f + 0.45f * e);
                        float f = p < 0.2f ? p / 0.2f : 1f - (p - 0.2f) / 0.8f;
                        Glow(g, c1, sx, sy, R * 1.1f, 0.7f * f);
                        g.DrawEllipse(_res.DashPen(Cc(c1, (int)(230 * f)), 3f, now / 25f % 24f), sx - R, sy - R * 0.7f, R * 2, R * 1.4f);
                        float r2 = R * 0.62f;
                        g.DrawEllipse(Pn(c2, (int)(200 * f), 2f), sx - r2, sy - r2 * 0.7f, r2 * 2, r2 * 1.4f);
                        return true;
                    }
                case SkillFxStyle.Quake:
                    Glow(g, c1, sx, sy, Math.Max(full, v.Px(3f)) * 0.9f, 0.55f * fade);
                    return false;       // 균열은 기존 그림
            }
            return false;
        }

        private static int VolleyCount(float radius) { return Math.Min(30, 10 + (int)(radius * 3)); }

        // ------------------------------------------------------------------ 범위 (공중)

        /// <summary>범위 폭발의 공중 부분. 처리했으면 true (false 면 기존 흙먼지를 쓴다).</summary>
        public bool RingAir(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, long now, float cx, float cy, float radius)
        {
            float p = fx.Progress(now);
            float sx = v.ToScreenX(cx), sy = v.ToScreenY(cy);
            float full = v.Px(radius);
            if (sx + full * 1.6f < 0 || sy + full * 2f < 0 || sx - full * 1.6f > v.Width || sy - full * 1.6f > v.Height + 200) return true;
            int c1 = info.Color1, c2 = info.Color2;
            float e = Ease(p), fade = 1f - p;
            switch (info.Style)
            {
                case SkillFxStyle.Whirl:
                    {
                        float R = Math.Max(full, v.Px(2.6f));
                        for (int k = 0; k < 3; ++k)
                        {
                            float rad = R * (0.5f + 0.2f * k) * (0.75f + 0.25f * e);
                            double a = (p * 900f * (k % 2 == 0 ? 1 : -1) + k * 120f + 85f) * Math.PI / 180.0;
                            float hx = sx + (float)Math.Cos(a) * rad, hy = sy + (float)Math.Sin(a) * rad * 0.8f - v.Px(0.5f);
                            Glow(g, c2, hx, hy, v.Px(0.7f), 0.9f * fade);
                            Star(g, c2, (int)(220 * fade), hx, hy, v.Px(0.45f), p * 12f + k, 2f);
                        }
                        for (int k = 0; k < 8; ++k)
                        {
                            float a = Rn(fx, k) * TwoPi + p * 3f, d = R * (0.3f + 0.7f * Rn(fx, k + 9)) * e;
                            float up = v.Px(0.3f + 1.4f * p * (0.4f + Rn(fx, k + 3)));
                            Glow(g, c1, sx + (float)Math.Cos(a) * d, sy + (float)Math.Sin(a) * d * 0.8f - up, v.Px(0.2f), 0.8f * fade);
                        }
                        return true;
                    }
                case SkillFxStyle.Blast:
                    {
                        float R = Math.Max(full, v.Px(3f));
                        if (p < 0.6f)
                        {
                            float q = p / 0.6f;
                            float r = R * 0.6f * (0.35f + 0.65f * Ease(q)), h = v.Px(1.2f * Ease(q));
                            Glow(g, c1, sx, sy - h, r * 2.1f, 0.85f * (1f - q * 0.6f));
                            Glow(g, c2, sx, sy - h, r * 1.3f, 1f - q);
                            g.FillEllipse(Br(0xFFFFFF, (int)(240 * (1f - q))), sx - r * 0.45f, sy - h - r * 0.4f, r * 0.9f, r * 0.8f);
                            for (int k = 0; k < 7; ++k)
                            {
                                float a = (k - 3) * 0.32f + (Rn(fx, k) - 0.5f) * 0.2f;
                                float len = v.Px(1.4f + 2.2f * Rn(fx, k + 20)) * (float)Math.Sin(Math.PI * Clamp01(q));
                                float bx = sx + (float)Math.Sin(a) * R * 0.55f, by = sy - v.Px(0.2f);
                                g.FillPolygon(Br(Mix(c1, c2, Rn(fx, k + 40) * 0.6f), (int)(200 * (1f - q))), new PointF[] { new PointF(bx - v.Px(0.35f), by), new PointF(bx + v.Px(0.35f), by), new PointF(bx + (float)Math.Sin(a) * v.Px(0.3f), by - len) });
                            }
                        }
                        for (int k = 0; k < 6; ++k)
                        {
                            float q = Clamp01((p - 0.15f - k * 0.04f) / 0.85f);
                            if (q <= 0f) continue;
                            float px = sx + (Rn(fx, k + 5) - 0.5f) * R * 1.0f, py = sy - v.Px(0.8f + 2.8f * q);
                            float r = v.Px(0.6f + 0.9f * q);
                            Glow(g, 0x4A423C, px, py, r * 1.5f, 0.6f * (1f - q));
                        }
                        for (int k = 0; k < 18; ++k)
                        {
                            float a = Rn(fx, k + 100) * TwoPi, spd = (0.5f + Rn(fx, k + 130)) * R * 1.1f, up = v.Px(2.6f + 3.2f * Rn(fx, k + 160));
                            float t = p;
                            float px = sx + (float)Math.Cos(a) * spd * t, py = sy + (float)Math.Sin(a) * spd * t * 0.5f - up * t + v.Px(5f) * t * t;
                            Glow(g, Mix(c1, c2, Rn(fx, k + 190)), px, py, v.Px(0.16f) * (1.2f - p), 0.95f * fade);
                        }
                        return true;
                    }
                case SkillFxStyle.Quake:
                    {
                        _fx.DrawBurstDust(g, fx, v, p, sx, sy);
                        float R = Math.Max(full, v.Px(3f));
                        for (int k = 0; k < 10; ++k)
                        {
                            float a = Rn(fx, k) * TwoPi, d = R * (0.2f + 0.7f * Rn(fx, k + 20)) * Math.Min(1f, p * 4f);
                            float t = p, up = v.Px(2.2f + 2.6f * Rn(fx, k + 40));
                            float px = sx + (float)Math.Cos(a) * d, py = sy + (float)Math.Sin(a) * d * 0.7f - 4f * up * t * (1f - t);
                            float s = v.Px(0.18f + 0.18f * Rn(fx, k + 60)), rot = p * 8f + k;
                            PointF[] poly = { new PointF(px + (float)Math.Cos(rot) * s, py + (float)Math.Sin(rot) * s), new PointF(px + (float)Math.Cos(rot + 2.1f) * s, py + (float)Math.Sin(rot + 2.1f) * s), new PointF(px + (float)Math.Cos(rot + 4.2f) * s * 0.8f, py + (float)Math.Sin(rot + 4.2f) * s * 0.8f) };
                            g.FillPolygon(Br(0x5A4430, (int)(250 * (p < 0.8f ? 1f : (1f - p) / 0.2f))), poly);
                            g.DrawPolygon(Pn(c2, (int)(120 * fade), 1f), poly);
                        }
                        return true;
                    }
                case SkillFxStyle.Roar:
                    {
                        float R = Math.Max(full, v.Px(3.4f));
                        for (int k = 0; k < 12; ++k)
                        {
                            float a = k * TwoPi / 12f + 0.15f;
                            float r0 = R * (0.25f + 0.6f * e), r1 = r0 + R * 0.28f * (1f - p);
                            float hy = v.Px(0.9f);
                            g.DrawLine(Pn(c2, (int)(220 * fade), 3f), sx + (float)Math.Cos(a) * r0, sy + (float)Math.Sin(a) * r0 * 0.8f - hy, sx + (float)Math.Cos(a) * r1, sy + (float)Math.Sin(a) * r1 * 0.8f - hy);
                        }
                        Glow(g, c1, sx, sy - v.Px(1.0f), R * 0.5f * (1f - p * 0.5f), 0.7f * fade);
                        return true;
                    }
                case SkillFxStyle.Poison:
                    {
                        float R = Math.Max(full, v.Px(2.8f));
                        float f = p < 0.15f ? p / 0.15f : 1f - (p - 0.15f) / 0.85f;
                        for (int k = 0; k < 12; ++k)
                        {
                            float ph = (p * 1.4f + Rn(fx, k)) % 1f;
                            float px = sx + (Rn(fx, k + 30) - 0.5f) * R * 1.7f, py = sy + (Rn(fx, k + 60) - 0.5f) * R * 0.9f - v.Px(2.4f * ph);
                            float r = v.Px(0.16f + 0.14f * Rn(fx, k + 90));
                            g.DrawEllipse(Pn(c2, (int)(190 * f * (1f - ph * 0.6f)), 1.5f), px - r, py - r, r * 2, r * 2);
                            g.FillEllipse(Br(c2, (int)(120 * f)), px - r * 0.35f - r * 0.2f, py - r * 0.35f - r * 0.2f, r * 0.5f, r * 0.5f);
                        }
                        if (info.Shake > 0f) _fx.DrawBurstDust(g, fx, v, p, sx, sy);
                        return true;
                    }
                case SkillFxStyle.Volley:
                    {
                        float R = Math.Max(full, v.Px(3f));
                        int n = VolleyCount(radius);
                        float len = v.Px(1.7f), hgt = v.Px(9f), slant = v.Px(1.8f);
                        float nl = (float)Math.Sqrt(slant * slant + hgt * hgt);
                        for (int k = 0; k < n; ++k)
                        {
                            float tk = 0.12f + 0.55f * Rn(fx, k);
                            float a = Rn(fx, k + 100) * TwoPi, d = R * (float)Math.Sqrt(Rn(fx, k + 200)) * 0.92f;
                            float lx = sx + (float)Math.Cos(a) * d, ly = sy + (float)Math.Sin(a) * d * 0.8f;
                            if (p >= tk - 0.15f && p < tk)
                            {
                                float u = (p - (tk - 0.15f)) / 0.15f;
                                float tx = lx - (1f - u) * slant, ty = ly - (1f - u) * hgt;
                                float ux = slant / nl, uy = hgt / nl;
                                g.DrawLine(Pn(c1, 90, 5f), tx - ux * len, ty - uy * len, tx, ty);
                                g.DrawLine(Pn(0xFFFFFF, 240, 1.5f), tx - ux * len, ty - uy * len, tx, ty);
                                Glow(g, c2, tx, ty, v.Px(0.28f), 0.8f);
                            }
                            else if (p >= tk && p < tk + 0.28f)
                            {
                                float u = (p - tk) / 0.28f;
                                g.DrawLine(Pn(c1, (int)(230 * (1f - u)), 2f), lx, ly, lx - v.Px(0.2f), ly - v.Px(1.0f));
                                g.DrawLine(Pn(0xFFFFFF, (int)(200 * (1f - u)), 1f), lx - v.Px(0.2f), ly - v.Px(1.0f), lx - v.Px(0.25f), ly - v.Px(1.35f));
                                if (u < 0.35f) Glow(g, c2, lx, ly - v.Px(0.1f), v.Px(0.55f) * (1f - u), 0.8f * (1f - u * 2.5f));
                            }
                        }
                        return true;
                    }
                case SkillFxStyle.Nova:
                    {
                        float R = Math.Max(full, v.Px(3.2f));
                        if (p < 0.3f) Glow(g, c2, sx, sy - v.Px(0.9f), v.Px(1.8f) * (0.5f + p * 3f), 1f - p / 0.3f);
                        for (int k = 0; k < 10; ++k)
                        {
                            float a = k * TwoPi / 10f + 0.4f, r = R * e * 0.95f;
                            Glow(g, c2, sx + (float)Math.Cos(a) * r, sy + (float)Math.Sin(a) * r * 0.8f - v.Px(0.4f), v.Px(0.3f), 0.9f * fade);
                        }
                        return true;
                    }
                case SkillFxStyle.Heal:
                    {
                        float f = p < 0.2f ? p / 0.2f : 1f - (p - 0.2f) / 0.8f;
                        float colW = v.Px(1.05f);
                        for (int j = 0; j < 9; ++j)
                            Glow(g, Mix(c1, c2, j / 9f), sx, sy - j * v.Px(0.42f), colW * (1.05f - j * 0.07f), 0.5f * f);
                        for (int k = 0; k < 14; ++k)
                        {
                            float ph = (p * 1.3f + Rn(fx, k)) % 1f;
                            float px = sx + (Rn(fx, k + 20) - 0.5f) * colW * 2.4f, py = sy - v.Px(0.2f) - v.Px(3.6f) * ph;
                            float s = v.Px(0.17f) * (0.6f + 0.6f * Rn(fx, k + 40));
                            int a = (int)(240 * f * Math.Sin(Math.PI * ph));
                            g.DrawLine(Pn(c2, a, 2f), px - s, py, px + s, py);
                            g.DrawLine(Pn(c2, a, 2f), px, py - s, px, py + s);
                        }
                        return true;
                    }
            }
            return false;
        }

        // ------------------------------------------------------------------ 베기 (단일 대상)

        /// <summary>스킬 베기(Variant 1)를 양식에 맞게 그린다. 처리했으면 true.</summary>
        public bool Slash(Graphics g, SkillFxInfo info, WorldModel w, ViewEffect fx, ViewState v, long now)
        {
            float p = fx.Progress(now);
            Entity c = w.Find(fx.EntityId);
            float cx = fx.X, cy = fx.Y;
            if (c != null) c.PosAt(now, out cx, out cy);
            float tx = fx.X, ty = fx.Y;
            if (fx.Value > 0 && fx.X2 > 0f) { DrawFan(g, info, fx, v, p, cx, cy, tx, ty); return true; }        // 부채꼴
            if (fx.Value > 0) { DrawBeam(g, info, fx, v, p, cx, cy, tx, ty); return true; }                  // 직선
            switch (info.Style)
            {
                case SkillFxStyle.Blade: DrawBlade(g, info, fx, v, p, cx, cy, tx, ty); return true;
                case SkillFxStyle.Pierce: DrawPierce(g, info, fx, v, p, cx, cy, tx, ty); return true;
                case SkillFxStyle.Lance: _fx.Attack.Thrust(g, fx, v, p, cx, cy, info.Color1, info.Color2, 1.7f); return true;
                case SkillFxStyle.Whirl:
                case SkillFxStyle.Roar:
                case SkillFxStyle.Nova:
                    return RingAir(g, info, fx, v, now, cx, cy, fx.Radius > 0 ? fx.Radius : 3.5f);
                case SkillFxStyle.Heal:
                case SkillFxStyle.Blast:
                case SkillFxStyle.Quake:
                case SkillFxStyle.Poison:
                case SkillFxStyle.Volley:
                    return RingAir(g, info, fx, v, now, tx, ty, fx.Radius > 0 ? fx.Radius : 3.5f);
            }
            return false;
        }

        /// <summary>단일 대상 형태로 쓰인 범위 양식의 바닥 부분.</summary>
        public bool SlashGround(Graphics g, SkillFxInfo info, WorldModel w, ViewEffect fx, ViewState v, long now)
        {
            if (fx.Radius <= 0f) return false;
            Entity c = w.Find(fx.EntityId);
            float cx = fx.X, cy = fx.Y;
            if (c != null) c.PosAt(now, out cx, out cy);
            switch (info.Style)
            {
                case SkillFxStyle.Whirl:
                case SkillFxStyle.Roar:
                case SkillFxStyle.Nova:
                    return RingGround(g, info, fx, v, now, cx, cy, fx.Radius);
                case SkillFxStyle.Heal:
                case SkillFxStyle.Blast:
                case SkillFxStyle.Quake:
                case SkillFxStyle.Poison:
                case SkillFxStyle.Volley:
                    return RingGround(g, info, fx, v, now, fx.X, fx.Y, fx.Radius);
            }
            return false;
        }

        /// <summary>월드 좌표 (시전자 기준 각도, 거리) 를 화면 좌표로. 몸 높이쯤 띄워 그린다.</summary>
        private static PointF Polar(ViewState v, float cx, float cy, float ang, float dist, float lift)
        {
            return new PointF(v.ToScreenX(cx + (float)Math.Cos(ang) * dist), v.ToScreenY(cy + (float)Math.Sin(ang) * dist) - lift);
        }

        /// <summary>부채꼴 스킬: 시전자에서 겹겹이 퍼져 나가는 불꽃 부채.</summary>
        private void DrawFan(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, float p, float cx, float cy, float tx, float ty)
        {
            int c1 = info.Color1, c2 = info.Color2;
            float R = fx.Value / 100f, half = fx.X2 * 0.5f * 0.0174533f;
            float ang = (float)Math.Atan2(ty - cy, tx - cx);
            float grow = Ease(Clamp01(p / 0.35f)), fade = 1f - Clamp01((p - 0.3f) / 0.7f);
            float lift = v.Px(0.45f);
            const int N = 16;
            for (int k = 0; k < 4; ++k)
            {
                float rr = R * grow * (1f - k * 0.16f);
                PointF[] pts = new PointF[N + 2];
                pts[0] = Polar(v, cx, cy, ang, 0f, lift);
                for (int i = 0; i <= N; ++i) pts[i + 1] = Polar(v, cx, cy, ang - half + 2f * half * i / N, rr, lift);
                g.FillPolygon(Br(Mix(c1, c2, k / 3f), (int)((50 + 34 * k) * fade)), pts);
            }
            float outer = R * grow;
            PointF[] arc = new PointF[N + 1];
            for (int i = 0; i <= N; ++i) arc[i] = Polar(v, cx, cy, ang - half + 2f * half * i / N, outer, lift);
            g.DrawLines(Pn(c2, (int)(240 * fade), 4f), arc);
            g.DrawLine(Pn(c2, (int)(200 * fade), 2f), Polar(v, cx, cy, ang - half, 0f, lift), arc[0]);
            g.DrawLine(Pn(c2, (int)(200 * fade), 2f), Polar(v, cx, cy, ang + half, 0f, lift), arc[N]);
            for (int k = 0; k < 10; ++k)
            {
                float a = ang + (Rn(fx, k) - 0.5f) * 2f * half * 0.92f;
                float d0 = R * (0.12f + 0.2f * Rn(fx, k + 10)) * grow, d1 = R * (0.55f + 0.45f * Rn(fx, k + 20)) * grow;
                g.DrawLine(Pn(c2, (int)(210 * fade), 2f), Polar(v, cx, cy, a, d0, lift), Polar(v, cx, cy, a, d1, lift));
                PointF tip = Polar(v, cx, cy, a, d1, lift);
                Glow(g, Mix(c1, c2, Rn(fx, k + 30)), tip.X, tip.Y, v.Px(0.5f), 0.85f * fade);
            }
            for (int k = 0; k < 14; ++k)
            {
                float a = ang + (Rn(fx, k + 40) - 0.5f) * 2f * half;
                float d = R * Clamp01(0.2f + p * (0.9f + 0.5f * Rn(fx, k + 50)));
                PointF e = Polar(v, cx, cy, a, d, lift + v.Px(1.0f * p * (0.3f + Rn(fx, k + 60))));
                Glow(g, c1, e.X, e.Y, v.Px(0.2f), 0.9f * fade);
            }
            Glow(g, c2, v.ToScreenX(cx), v.ToScreenY(cy) - lift, v.Px(1.4f) * (1f - p * 0.5f), 0.9f * fade);
        }

        /// <summary>직선 스킬: 시전자에서 사거리 끝까지 뻗는 광선.</summary>
        private void DrawBeam(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, float p, float cx, float cy, float tx, float ty)
        {
            int c1 = info.Color1, c2 = info.Color2;
            float R = fx.Value / 100f, W = Math.Max(0.5f, fx.Y2);
            float ang = (float)Math.Atan2(ty - cy, tx - cx);
            float reach = Ease(Clamp01(p / 0.18f)), fade = 1f - Clamp01((p - 0.15f) / 0.85f);
            float lift = v.Px(1.2f);
            PointF a = Polar(v, cx, cy, ang, 0.4f, lift), b = Polar(v, cx, cy, ang, R * reach, lift);
            float wpx = v.Px(W);
            g.DrawLine(Pn(c1, (int)(60 * fade), wpx * 1.9f), a, b);
            g.DrawLine(Pn(c1, (int)(130 * fade), wpx * 1.0f), a, b);
            g.DrawLine(Pn(c2, (int)(230 * fade), Math.Max(3f, wpx * 0.45f)), a, b);
            g.DrawLine(Pn(0xFFFFFF, (int)(255 * fade), Math.Max(1.5f, wpx * 0.18f)), a, b);
            Glow(g, c2, a.X, a.Y, v.Px(1.1f), 0.9f * fade);
            Glow(g, c2, b.X, b.Y, v.Px(1.3f) * (0.6f + 0.6f * p), 0.95f * fade);
            for (int k = 0; k < 14; ++k)
            {
                float d = R * reach * Rn(fx, k);
                float off = (Rn(fx, k + 20) - 0.5f) * W * 1.6f;
                float px = v.ToScreenX(cx + (float)Math.Cos(ang) * d - (float)Math.Sin(ang) * off), py = v.ToScreenY(cy + (float)Math.Sin(ang) * d + (float)Math.Cos(ang) * off) - lift - v.Px(0.6f * p * Rn(fx, k + 40));
                Glow(g, Mix(c1, c2, Rn(fx, k + 60)), px, py, v.Px(0.16f), 0.9f * fade);
            }
            Star(g, 0xFFFFFF, (int)(240 * fade), b.X, b.Y, v.Px(0.9f) * (0.4f + 0.6f * reach), p * 5f, 2f);
            if (info.Style == SkillFxStyle.Lance)
            {
                // 창끝 쐐기: 광선 끝에서 앞으로 뾰족하게, 옆으로 번지는 두 날개
                float ux = b.X - a.X, uy = b.Y - a.Y, ul = (float)Math.Sqrt(ux * ux + uy * uy);
                if (ul > 1f)
                {
                    ux /= ul; uy /= ul;
                    float nx = -uy, ny = ux, hw = Math.Max(5f, wpx * 0.5f), hl = v.Px(1.3f);
                    g.FillPolygon(Br(c2, (int)(235 * fade)), new PointF[] { new PointF(b.X + ux * hl, b.Y + uy * hl), new PointF(b.X + nx * hw, b.Y + ny * hw), new PointF(b.X - ux * hl * 0.5f, b.Y - uy * hl * 0.5f), new PointF(b.X - nx * hw, b.Y - ny * hw) });
                    // 사거리를 따라 달리는 충격 고리 세 개
                    for (int k = 0; k < 3; ++k)
                    {
                        float q = Clamp01(p * 2.2f - k * 0.28f);
                        if (q <= 0f || q >= 1f) continue;
                        PointF c = Polar(v, cx, cy, ang, R * (0.25f + 0.7f * q), lift);
                        float rw = v.Px(0.5f) * (0.6f + 0.6f * q), rh = Math.Max(wpx * 0.55f, v.Px(0.9f));
                        g.DrawEllipse(Pn(c2, (int)(210 * (1f - q)), 2.5f), c.X - rw, c.Y - rh, rw * 2, rh * 2);
                    }
                    // 광선 양옆으로 흩어지는 속도선
                    for (int k = 0; k < 8; ++k)
                    {
                        float off = (Rn(fx, k + 80) - 0.5f) * W * 1.9f, d0 = R * reach * Rn(fx, k + 90) * 0.7f, d1 = d0 + R * 0.25f;
                        PointF s0 = new PointF(v.ToScreenX(cx + (float)Math.Cos(ang) * d0 - (float)Math.Sin(ang) * off), v.ToScreenY(cy + (float)Math.Sin(ang) * d0 + (float)Math.Cos(ang) * off) - lift);
                        PointF s1 = new PointF(v.ToScreenX(cx + (float)Math.Cos(ang) * d1 - (float)Math.Sin(ang) * off), v.ToScreenY(cy + (float)Math.Sin(ang) * d1 + (float)Math.Cos(ang) * off) - lift);
                        g.DrawLine(Pn(c2, (int)(170 * fade), 1.5f), s0, s1);
                    }
                }
            }
        }

        private void DrawBlade(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, float p, float cx, float cy, float tx, float ty)
        {
            int c1 = info.Color1, c2 = info.Color2;
            float ang = (float)Math.Atan2(ty - cy, tx - cx);
            float rr = v.Px(2.2f);
            float ox = v.ToScreenX(cx) + (float)Math.Cos(ang) * v.Px(0.5f), oy = v.ToScreenY(cy) - v.Px(1.3f) + (float)Math.Sin(ang) * v.Px(0.3f);
            float deg = ang * 57.29578f;
            float sweep = Clamp01(p / 0.45f), fade = 1f - Clamp01((p - 0.2f) / 0.8f);
            float head = deg - 85f + 170f * Ease(sweep);
            for (int k = 0; k < 5; ++k)
            {
                float tail = head - (95f - k * 13f);
                float rk = rr * (1f - k * 0.05f);
                g.DrawArc(Pn(Mix(c1, c2, k * 0.25f), (int)(240 * fade * (1f - k * 0.15f)), 11f - k * 2f), ox - rk, oy - rk * 0.9f, rk * 2, rk * 1.8f, tail, head - tail);
            }
            g.DrawArc(Pn(0xFFFFFF, (int)(250 * fade), 2.5f), ox - rr, oy - rr * 0.9f, rr * 2, rr * 1.8f, head - 40f, 40f);
            double ha = head * Math.PI / 180.0;
            Glow(g, c2, ox + (float)Math.Cos(ha) * rr, oy + (float)Math.Sin(ha) * rr * 0.9f, v.Px(0.9f), 0.9f * fade);
            // 타격: 베기가 끝나갈 때 대상에서 섬광과 파편
            float q = Clamp01((p - 0.22f) / 0.78f);
            if (q > 0f)
            {
                float hx = v.ToScreenX(tx), hy = v.ToScreenY(ty) - v.Px(1.1f);
                Glow(g, c2, hx, hy, v.Px(1.6f) * (0.5f + 0.7f * q), 1f - q);
                Star(g, 0xFFFFFF, (int)(250 * (1f - q)), hx, hy, v.Px(1.4f) * (0.4f + 0.6f * Ease(q)), 0.3f, 3f);
                for (int k = 0; k < 10; ++k)
                {
                    float a = Rn(fx, k) * TwoPi, s = v.Px(1.0f + 2.2f * Rn(fx, k + 10)) * q;
                    Glow(g, Mix(c1, c2, Rn(fx, k + 20)), hx + (float)Math.Cos(a) * s, hy + (float)Math.Sin(a) * s * 0.8f + v.Px(1.2f) * q * q, v.Px(0.13f), 0.95f * (1f - q));
                }
            }
        }

        private void DrawPierce(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, float p, float cx, float cy, float tx, float ty)
        {
            int c1 = info.Color1, c2 = info.Color2;
            float ax = v.ToScreenX(cx), ay = v.ToScreenY(cy) - v.Px(1.1f);
            float hx = v.ToScreenX(tx), hy = v.ToScreenY(ty) - v.Px(1.1f);
            // 돌진 선: 처음 25% 동안 시전자에서 대상까지 번쩍
            float dash = Clamp01(p / 0.2f);
            if (p < 0.5f)
            {
                float a = 1f - Clamp01((p - 0.1f) / 0.4f);
                float ex = ax + (hx - ax) * Ease(dash), ey = ay + (hy - ay) * Ease(dash);
                g.DrawLine(Pn(c1, (int)(110 * a), 12f), ax, ay, ex, ey);
                g.DrawLine(Pn(c2, (int)(240 * a), 3f), ax, ay, ex, ey);
            }
            // 교차하는 두 칼날
            float L = v.Px(1.6f);
            for (int k = 0; k < 2; ++k)
            {
                float start = 0.14f + k * 0.14f;
                float q = Clamp01((p - start) / 0.22f);
                if (q <= 0f) continue;
                float dir = k == 0 ? -0.7f : 0.7f, s = (float)Math.Sin(dir), c = (float)Math.Cos(dir);
                float grow = Ease(Clamp01(q * 2.5f)), a = 1f - Clamp01((q - 0.4f) / 0.6f);
                float x0 = hx - c * L * grow, y0 = hy - s * L * grow, x1 = hx + c * L * grow, y1 = hy + s * L * grow;
                g.DrawLine(Pn(c1, (int)(150 * a), 9f), x0, y0, x1, y1);
                g.DrawLine(Pn(c2, (int)(250 * a), 2.5f), x0, y0, x1, y1);
            }
            float f = Clamp01((p - 0.14f) / 0.5f);
            if (f > 0f)
            {
                Glow(g, c1, hx, hy, v.Px(1.5f), 0.9f * (1f - f));
                for (int k = 0; k < 14; ++k)
                {
                    float a = Rn(fx, k) * TwoPi, s = v.Px(0.8f + 2.4f * Rn(fx, k + 10)) * f;
                    float px = hx + (float)Math.Cos(a) * s, py = hy + (float)Math.Sin(a) * s * 0.8f + v.Px(1.8f) * f * f;
                    float r = v.Px(0.07f + 0.07f * Rn(fx, k + 20));
                    g.FillEllipse(Br(k % 3 == 0 ? c2 : c1, (int)(250 * (1f - f))), px - r, py - r, r * 2, r * 2);
                }
            }
        }

        // ------------------------------------------------------------------ 투사체

        public bool Projectile(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, long now, float t, float ex, float ey)
        {
            if (info.Style != SkillFxStyle.Arrow && info.Style != SkillFxStyle.Fireball) return false;
            int c1 = info.Color1, c2 = info.Color2;
            float h = v.Px(1.4f);
            float px = v.ToScreenX(fx.X + (ex - fx.X) * t), py = v.ToScreenY(fx.Y + (ey - fx.Y) * t) - h;
            float t0 = Math.Max(0f, t - (info.Style == SkillFxStyle.Arrow ? 0.18f : 0.12f));
            float qx = v.ToScreenX(fx.X + (ex - fx.X) * t0), qy = v.ToScreenY(fx.Y + (ey - fx.Y) * t0) - h;
            float dx = px - qx, dy = py - qy, l = (float)Math.Sqrt(dx * dx + dy * dy);
            float ux = l > 0.5f ? dx / l : 1f, uy = l > 0.5f ? dy / l : 0f;
            if (info.Style == SkillFxStyle.Arrow)
            {
                g.DrawLine(Pn(c1, 50, 14f), qx, qy, px, py);
                g.DrawLine(Pn(c1, 130, 7f), qx, qy, px, py);
                g.DrawLine(Pn(c2, 250, 2.5f), qx, qy, px, py);
                for (int k = 1; k <= 5; ++k)
                {
                    float tk = Math.Max(0f, t - k * 0.035f);
                    float kx = v.ToScreenX(fx.X + (ex - fx.X) * tk) + (Noise.Hash01(k, (int)(now / 40), 7) - 0.5f) * v.Px(0.35f);
                    float ky = v.ToScreenY(fx.Y + (ey - fx.Y) * tk) - h + (Noise.Hash01(k, (int)(now / 40), 8) - 0.5f) * v.Px(0.35f);
                    Glow(g, c1, kx, ky, v.Px(0.17f), 0.8f * (1f - k * 0.16f));
                }
                Glow(g, c2, px, py, v.Px(0.8f), 0.95f);
                g.FillPolygon(Br(0xFFFFFF, 255), new PointF[] { new PointF(px + ux * 7, py + uy * 7), new PointF(px - uy * 3.5f - ux * 3, py + ux * 3.5f - uy * 3), new PointF(px + uy * 3.5f - ux * 3, py - ux * 3.5f - uy * 3) });
                return true;
            }
            float r = v.Px(0.55f) * (0.92f + 0.12f * (float)Math.Sin(now / 55.0));
            for (int k = 9; k >= 1; --k)
            {
                float tk = Math.Max(0f, t - k * 0.022f);
                float kx = v.ToScreenX(fx.X + (ex - fx.X) * tk) + (Noise.Hash01(k, (int)(now / 45), 5) - 0.5f) * r * 0.9f;
                float ky = v.ToScreenY(fx.Y + (ey - fx.Y) * tk) - h + (Noise.Hash01(k, (int)(now / 45), 6) - 0.5f) * r * 0.9f - k * 0.5f;
                float rk = r * (1f - k * 0.085f);
                Glow(g, k > 5 ? 0x7A2A10 : (k > 2 ? c1 : Mix(c1, c2, 0.4f)), kx, ky, rk * 2.4f, 0.8f * (1f - k * 0.09f));
            }
            Glow(g, c1, px, py, r * 3.8f, 0.9f);
            Glow(g, c2, px, py, r * 2.1f, 1f);
            g.FillEllipse(Br(c2, 255), px - r, py - r, r * 2, r * 2);
            g.FillEllipse(Br(0xFFFFFF, 255), px - r * 0.5f, py - r * 0.5f, r, r);
            for (int k = 0; k < 6; ++k)
            {
                double a = now / 85.0 + k * 1.047;
                float orb = r * (1.15f + 0.3f * (float)Math.Sin(now / 130.0 + k));
                Glow(g, c2, px + (float)Math.Cos(a) * orb, py + (float)Math.Sin(a) * orb * 0.8f, r * 0.45f, 0.95f);
            }
            return true;
        }

        // ------------------------------------------------------------------ 적중 (투사체 착탄)

        public bool Impact(Graphics g, SkillFxInfo info, ViewEffect fx, ViewState v, long now, float p, float sx, float sy)
        {
            if (info.Style != SkillFxStyle.Arrow && info.Style != SkillFxStyle.Fireball) return false;
            int c1 = info.Color1, c2 = info.Color2;
            float cy = sy - v.Px(1.1f), fade = 1f - p, e = Ease(p);
            if (info.Style == SkillFxStyle.Arrow)
            {
                Glow(g, c2, sx, cy, v.Px(1.5f) * (0.5f + 0.5f * e), fade);
                Star(g, 0xFFFFFF, (int)(250 * fade), sx, cy, v.Px(1.3f) * (0.4f + 0.6f * e), p * 1.5f, 2.5f);
                float r = v.Px(1.0f) * e;
                g.DrawEllipse(Pn(c1, (int)(200 * fade), 2.5f), sx - r, cy - r * 0.7f, r * 2, r * 1.4f);
                return true;
            }
            Glow(g, c1, sx, cy, v.Px(3.0f) * (0.4f + 0.6f * e), 0.95f * fade);
            if (p < 0.5f) Glow(g, c2, sx, cy, v.Px(1.8f) * (0.4f + 0.8f * (p / 0.5f)), 1f - p * 2f);
            float rr = v.Px(2.6f) * e;
            g.DrawEllipse(Pn(c2, (int)(230 * fade), 4f * fade + 1f), sx - rr, cy - rr * 0.8f, rr * 2, rr * 1.6f);
            for (int k = 0; k < 16; ++k)
            {
                float a = Rn(fx, k) * TwoPi, spd = v.Px(1.2f + 3.2f * Rn(fx, k + 20));
                float px = sx + (float)Math.Cos(a) * spd * p, py = cy + (float)Math.Sin(a) * spd * p * 0.8f - v.Px(1.8f) * p + v.Px(3.2f) * p * p;
                Glow(g, Mix(c1, c2, Rn(fx, k + 40)), px, py, v.Px(0.17f) * (1.2f - p), 0.95f * fade);
            }
            for (int k = 0; k < 5; ++k)
            {
                float q = Clamp01((p - 0.1f - k * 0.04f) / 0.9f);
                if (q <= 0f) continue;
                float px = sx + (Rn(fx, k + 60) - 0.5f) * v.Px(1.6f), py = cy - v.Px(0.4f + 1.8f * q);
                float r = v.Px(0.4f + 0.7f * q);
                Glow(g, 0x4A423C, px, py, r * 1.5f, 0.55f * (1f - q));
            }
            return true;
        }

        // ------------------------------------------------------------------ 시전 중 기 모으기

        /// <summary>시전 중인 엔티티 몸으로 빛이 빨려 들어간다.</summary>
        public void Charge(Graphics g, SkillFxInfo info, Entity e, ViewState v, long now, float sx, float sy, float bodyH, float halfW)
        {
            if (info.Style == SkillFxStyle.Default || e.CastTimeMs <= 0) return;
            float p = Clamp01((now - e.CastStartMs) / (float)e.CastTimeMs);
            int c1 = info.Color1, c2 = info.Color2;
            float cy = sy - v.Px(bodyH * 0.5f);
            Glow(g, c1, sx, sy - v.Px(0.1f), halfW * (1.2f + 0.8f * p), 0.35f + 0.45f * p);
            g.DrawEllipse(_res.DashPen(Cc(c2, (int)(120 + 110 * p)), 2f, now / 30f % 24f), sx - halfW * 1.5f, sy - halfW * 0.6f, halfW * 3f, halfW * 1.2f);
            for (int k = 0; k < 12; ++k)
            {
                float ph = (now / 520f + k / 12f) % 1f;
                double a = k * 0.5236 + now / 380.0;
                float d = v.Px(2.4f) * (1f - ph);
                float px = sx + (float)Math.Cos(a) * d, py = cy + (float)Math.Sin(a) * d * 0.7f;
                Glow(g, k % 3 == 0 ? c2 : c1, px, py, v.Px(0.2f) * (0.4f + ph), 0.95f * ph);
            }
            if (p > 0.6f) Glow(g, c2, sx, cy, v.Px(1.2f) * (p - 0.4f), (p - 0.6f) * 2f);
        }

        // ------------------------------------------------------------------ 상태 효과

        /// <summary>기절/보호막/지속 치유 표현. 처리했으면 true.</summary>
        public bool Status(Graphics g, StatusRow row, ViewState v, long now, int idx, float sx, float sy, float bodyH, float halfW)
        {
            switch (row.Kind)
            {
                case "Stun":
                    {
                        float hy = sy - v.Px(bodyH) - v.Px(0.1f);
                        for (int k = 0; k < 3; ++k)
                        {
                            double a = now / 260.0 + k * 2.094;
                            float depth = (float)Math.Sin(a);
                            float px = sx + (float)Math.Cos(a) * halfW * 0.95f, py = hy + depth * halfW * 0.3f;
                            float s = v.Px(0.2f) * (0.8f + 0.35f * depth);
                            Star(g, 0xFFE070, 240, px, py, s, (float)a * 2f, 2f);
                            Glow(g, 0xFFE070, px, py, s * 1.6f, 0.7f);
                        }
                        return true;
                    }
                case "Shield":
                    {
                        float pulse = 0.5f + 0.5f * (float)Math.Sin(now / 240.0 + idx);
                        float top = sy - v.Px(bodyH * 1.08f), bot = sy + v.Px(0.15f);
                        float w = halfW * 1.35f;
                        g.FillEllipse(Br(0x78C8FF, (int)(34 + 22 * pulse)), sx - w, top, w * 2, bot - top);
                        g.DrawEllipse(Pn(0xBEE6FF, (int)(120 + 80 * pulse), 2f), sx - w, top, w * 2, bot - top);
                        g.DrawArc(Pn(0xFFFFFF, 190, 2f), sx - w * 0.8f, top + (bot - top) * 0.1f, w * 1.6f, (bot - top) * 0.8f, 200f, 50f);
                        float swirl = now / 700f % 1f;
                        g.DrawArc(Pn(0xDCF2FF, (int)(160 * pulse + 40), 1.5f), sx - w, top, w * 2, bot - top, swirl * 360f, 60f);
                        return true;
                    }
                case "Hot":
                    {
                        for (int k = 0; k < 5; ++k)
                        {
                            float ph = ((now / 650f) + k * 0.2f + idx * 0.07f) % 1f;
                            float px = sx + (Noise.Hash01(k + idx, 11, 4) - 0.5f) * halfW * 1.7f;
                            float py = sy - v.Px(0.3f) - ph * v.Px(bodyH * 0.9f);
                            float s = v.Px(0.13f) * (1f - ph * 0.4f);
                            int a = (int)(240 * Math.Sin(Math.PI * ph));
                            g.DrawLine(Pn(0x8CFFA8, a, 2f), px - s, py, px + s, py);
                            g.DrawLine(Pn(0x8CFFA8, a, 2f), px, py - s, px, py + s);
                        }
                        Glow(g, 0x78FF9E, sx, sy - v.Px(0.1f), halfW * 1.3f, 0.35f);
                        return true;
                    }
            }
            return false;
        }
    }
}
