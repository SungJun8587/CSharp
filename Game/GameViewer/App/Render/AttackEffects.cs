//***************************************************************************
// AttackEffects.cs : 기본 공격 연출. 무기(검/활/지팡이/창/발톱)마다 다른 궤적과, 모든 타격에 붙는 충격 연출을 그린다.
//   MeleeSlash  근접 기본 공격: 검 = 겹친 초승달 궤적, 창 = 꿰뚫는 찌르기 줄기와 속도선, 발톱 = 세 줄 할퀴기
//   Tracer      원거리 기본 공격: 활 = 날아가는 화살, 지팡이 = 마력 구슬
//   Hit         타격 충격: 섬광, 별빛, 충격 고리, 방향성 파편 선, 중력 불꽃. 큰 피해는 고리와 먼지가 한 겹 더 붙는다.
// 파티클은 상태를 두지 않고 "연출 시작 시각 + 해시 난수" 로 매 프레임 같은 궤적을 계산한다(스냅샷이 항상 같다).
//***************************************************************************
using System;
using System.Drawing;
using GameViewer.Core;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class AttackEffects
    {
        private enum Weapon { Generic, Sword, Bow, Staff, Lance, Claw }

        private readonly Resources _res;
        private readonly EffectRenderer _fx;
        private const float TwoPi = 6.2831853f;

        public AttackEffects(Resources res, EffectRenderer fx) { _res = res; _fx = fx; }

        // ------------------------------------------------------------------ 도구

        private static Color Cc(int rgb, int a) { return Color.FromArgb(a < 0 ? 0 : (a > 255 ? 255 : (a & 0xF0) | (a >> 4)), (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        private Pen Pn(int rgb, int a, float width) { return _res.Pen(Cc(rgb, a), (float)Math.Round(Math.Max(0.5f, width) * 2f) / 2f); }
        private SolidBrush Br(int rgb, int a) { return _res.Brush(Cc(rgb, a)); }
        private static float Ease(float p) { return 1f - (1f - p) * (1f - p); }
        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        private static int Mix(int a, int b, float t)
        {
            t = Clamp01(t);
            int r = (int)(((a >> 16) & 255) * (1f - t) + ((b >> 16) & 255) * t), g = (int)(((a >> 8) & 255) * (1f - t) + ((b >> 8) & 255) * t), bl = (int)((a & 255) * (1f - t) + (b & 255) * t);
            return (r << 16) | (g << 8) | bl;
        }
        private static float Rn(ViewEffect fx, int i) { return Noise.Hash01(i * 7 + (int)(fx.StartMs & 0x7FFF), (int)(fx.EntityId & 0xFF) + i * 3, 0x3Bu); }
        private void Glow(Graphics g, int rgb, float cx, float cy, float r, float k) { _fx.Glow(g, Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255), cx, cy, r, r, k); }

        private void Star(Graphics g, int rgb, int a, float cx, float cy, float r, float rot, float w)
        {
            float c = (float)Math.Cos(rot), s = (float)Math.Sin(rot);
            Pen p = Pn(rgb, a, w);
            g.DrawLine(p, cx - c * r, cy - s * r, cx + c * r, cy + s * r);
            g.DrawLine(p, cx + s * r, cy - c * r, cx - s * r, cy + c * r);
            float d = r * 0.55f, c2 = (float)Math.Cos(rot + 0.785f), s2 = (float)Math.Sin(rot + 0.785f);
            Pen q = Pn(rgb, a / 2, Math.Max(0.5f, w - 1f));
            g.DrawLine(q, cx - c2 * d, cy - s2 * d, cx + c2 * d, cy + s2 * d);
            g.DrawLine(q, cx + s2 * d, cy - c2 * d, cx - s2 * d, cy + c2 * d);
        }

        private static Weapon WeaponOf(Entity e)
        {
            if (e == null) return Weapon.Generic;
            if (e.IsMonster) return Weapon.Claw;
            if (!e.IsPlayer) return Weapon.Generic;
            switch (e.TemplateId)
            {
                case 1: return Weapon.Sword;
                case 2: return Weapon.Bow;
                case 3: return Weapon.Staff;
                case 4: return Weapon.Lance;
            }
            return Weapon.Generic;
        }

        /// <summary>무기별 색 (주색, 밝은 보조색).</summary>
        private static void Palette(Weapon w, out int c1, out int c2)
        {
            switch (w)
            {
                case Weapon.Sword: c1 = 0xFFA83C; c2 = 0xFFF0C0; break;
                case Weapon.Bow: c1 = 0x6CD8FF; c2 = 0xF2FDFF; break;
                case Weapon.Staff: c1 = 0xB07CFF; c2 = 0xF4E8FF; break;
                case Weapon.Lance: c1 = 0x34D4E0; c2 = 0xE8FFFF; break;
                case Weapon.Claw: c1 = 0xFF4638; c2 = 0xFFE0D8; break;
                default: c1 = 0xFFC060; c2 = 0xFFFFF0; break;
            }
        }

        // ------------------------------------------------------------------ 근접

        /// <summary>근접 기본 공격. 무기에 맞는 그림이 있으면 그리고 true (false 면 기존 베기를 쓴다).</summary>
        public bool MeleeSlash(Graphics g, WorldModel w, ViewEffect fx, ViewState v, long now)
        {
            Entity c = w.Find(fx.EntityId);
            Weapon wp = WeaponOf(c);
            if (wp == Weapon.Generic || wp == Weapon.Bow || wp == Weapon.Staff) wp = c != null && c.IsPlayer ? Weapon.Sword : wp;
            if (wp == Weapon.Generic) return false;
            float p = fx.Progress(now);
            float cx = fx.X, cy = fx.Y;
            if (c != null) c.PosAt(now, out cx, out cy);
            int c1, c2; Palette(wp, out c1, out c2);
            switch (wp)
            {
                case Weapon.Lance: Thrust(g, fx, v, p, cx, cy, c1, c2, 1f); return true;
                case Weapon.Claw: DrawClaw(g, fx, v, p, c1, c2); return true;
                default: DrawSword(g, fx, v, p, cx, cy, c1, c2); return true;
            }
        }

        /// <summary>검: 시전자 앞을 가르는 겹친 초승달 궤적과 끝에 맺히는 빛.</summary>
        private void DrawSword(Graphics g, ViewEffect fx, ViewState v, float p, float cx, float cy, int c1, int c2)
        {
            float ang = (float)Math.Atan2(fx.Y - cy, fx.X - cx);
            float deg = ang * 57.29578f;
            float rr = v.Px(1.9f);
            float ox = v.ToScreenX(cx) + (float)Math.Cos(ang) * v.Px(0.55f);
            float oy = v.ToScreenY(cy) - v.Px(1.25f) + (float)Math.Sin(ang) * v.Px(0.3f);
            float sweep = Clamp01(p / 0.5f), fade = 1f - Clamp01((p - 0.25f) / 0.75f);
            float head = deg - 80f + 160f * Ease(sweep);
            for (int k = 0; k < 5; ++k)
            {
                float tail = head - (92f - k * 14f);
                float rk = rr * (1f - k * 0.045f);
                g.DrawArc(Pn(Mix(c1, c2, k * 0.25f), (int)(235 * fade * (1f - k * 0.14f)), 10f - k * 1.8f), ox - rk, oy - rk * 0.9f, rk * 2, rk * 1.8f, tail, head - tail);
            }
            g.DrawArc(Pn(0xFFFFFF, (int)(250 * fade), 2.5f), ox - rr, oy - rr * 0.9f, rr * 2, rr * 1.8f, head - 46f, 46f);
            double ha = head * Math.PI / 180.0;
            float hx = ox + (float)Math.Cos(ha) * rr, hy = oy + (float)Math.Sin(ha) * rr * 0.9f;
            Glow(g, c2, hx, hy, v.Px(0.9f), 0.9f * fade);
            for (int k = 0; k < 7; ++k)
            {
                double a = (head - 8f - k * 11f) * Math.PI / 180.0;
                float d = rr * (1.0f + (Rn(fx, k) - 0.3f) * 0.22f);
                Glow(g, Mix(c1, c2, Rn(fx, k + 9)), ox + (float)Math.Cos(a) * d, oy + (float)Math.Sin(a) * d * 0.9f, v.Px(0.13f), 0.9f * fade * (1f - k * 0.1f));
            }
        }

        /// <summary>
        /// 창: 손에서 대상 너머까지 꿰뚫는 찌르기. 뒤로 흩어지는 속도선과 창끝 번쩍임, 퍼지는 고리.
        /// power 1 은 기본 공격, 1.5 이상은 스킬(줄기가 굵고 고리와 방사선이 한 겹 더 붙는다).
        /// </summary>
        public void Thrust(Graphics g, ViewEffect fx, ViewState v, float p, float cx, float cy, int c1, int c2, float power)
        {
            float ax = v.ToScreenX(cx), ay = v.ToScreenY(cy) - v.Px(1.3f);
            float hx = v.ToScreenX(fx.X), hy = v.ToScreenY(fx.Y) - v.Px(1.1f);
            float dx = hx - ax, dy = hy - ay, dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1f) { dx = 1f; dy = 0f; dist = 1f; }
            float ux = dx / dist, uy = dy / dist, nx = -uy, ny = ux;
            // 창 자체는 몸과 따로 그린 그림이므로 잔광은 창끝 앞(손에서 창 길이만큼 앞)에서 시작해 창을 가리지 않는다
            float lead = Math.Min(v.Px(1.9f), dist * 0.6f);
            ax += ux * lead; ay += uy * lead; dist -= lead;
            float total = dist + v.Px(1.1f);
            float t = Ease(Clamp01(p / 0.32f));
            float fade = 1f - Clamp01((p - 0.3f) / 0.7f);
            float tipD = total * t, tailD = total * Clamp01(t - 0.62f - 0.3f * Clamp01((p - 0.32f) / 0.4f));
            float w0 = v.Px(0.42f * power);
            PointF tip = new PointF(ax + ux * tipD, ay + uy * tipD), tail = new PointF(ax + ux * tailD, ay + uy * tailD);
            for (int k = 0; k < 3; ++k)
            {
                float wk = w0 * (1.55f - k * 0.5f);
                int col = k == 0 ? c1 : (k == 1 ? Mix(c1, c2, 0.55f) : 0xFFFFFF);
                int al = k == 0 ? 90 : (k == 1 ? 175 : 255);
                g.FillPolygon(Br(col, (int)(al * fade)), new PointF[] { tip, new PointF(tail.X + nx * wk, tail.Y + ny * wk), new PointF(tail.X - nx * wk, tail.Y - ny * wk) });
            }
            // 속도선: 창 줄기 뒤쪽으로 빠르게 늘어나는 가는 선
            for (int k = 0; k < 6; ++k)
            {
                float off = (k - 2.5f) * v.Px(0.17f) + (Rn(fx, k) - 0.5f) * v.Px(0.1f);
                float s0 = tipD * (0.25f + 0.35f * Rn(fx, k + 10)), s1 = tipD * (0.7f + 0.3f * Rn(fx, k + 20));
                g.DrawLine(Pn(c2, (int)(170 * fade), 1.5f), ax + ux * s0 + nx * off, ay + uy * s0 + ny * off, ax + ux * s1 + nx * off, ay + uy * s1 + ny * off);
            }
            Glow(g, c1, tip.X, tip.Y, v.Px(1.0f), 0.95f * fade);
            Glow(g, 0xFFFFFF, tip.X, tip.Y, v.Px(0.5f), 1f * fade);
            // 맞는 순간: 대상에서 퍼지는 고리와 별빛
            float q = Clamp01((p - 0.22f) / 0.78f);
            if (q > 0f)
            {
                float rx = v.Px(0.5f + 1.5f * Ease(q));
                g.DrawEllipse(Pn(c2, (int)(230 * (1f - q)), 3f * (1f - q) + 1f), hx - rx, hy - rx * 0.7f, rx * 2, rx * 1.4f);
                Star(g, 0xFFFFFF, (int)(240 * (1f - q)), hx, hy, v.Px(1.0f * power) * (0.4f + 0.6f * Ease(q)), 0.4f, 2.5f);
                if (power > 1.2f)
                {
                    float q2 = Clamp01((p - 0.38f) / 0.62f);
                    if (q2 > 0f)
                    {
                        float r2 = v.Px(0.4f + 2.4f * Ease(q2));
                        g.DrawEllipse(Pn(c1, (int)(200 * (1f - q2)), 4f * (1f - q2) + 1f), hx - r2, hy - r2 * 0.7f, r2 * 2, r2 * 1.4f);
                    }
                    for (int k = 0; k < 10; ++k)
                    {
                        float a = Rn(fx, k + 150) * TwoPi, d0 = v.Px(0.4f) * (0.3f + q), d1 = v.Px(0.8f + 1.6f * Rn(fx, k + 160)) * Ease(q);
                        g.DrawLine(Pn(k % 2 == 0 ? c2 : c1, (int)(230 * (1f - q)), 2f), hx + (float)Math.Cos(a) * d0, hy + (float)Math.Sin(a) * d0 * 0.8f, hx + (float)Math.Cos(a) * (d0 + d1), hy + (float)Math.Sin(a) * (d0 + d1) * 0.8f);
                    }
                }
            }
        }

        /// <summary>발톱: 대상 위에 비스듬한 세 줄이 차례로 그어진다.</summary>
        private void DrawClaw(Graphics g, ViewEffect fx, ViewState v, float p, int c1, int c2)
        {
            float hx = v.ToScreenX(fx.X), hy = v.ToScreenY(fx.Y) - v.Px(1.0f);
            float len = v.Px(1.5f), fade = 1f - Clamp01((p - 0.35f) / 0.65f);
            for (int k = 0; k < 3; ++k)
            {
                float q = Clamp01((p - k * 0.07f) / 0.3f);
                if (q <= 0f) continue;
                float off = (k - 1) * v.Px(0.34f);
                float x0 = hx - len * 0.5f + off, y0 = hy - len * 0.55f, x1 = x0 + len * 0.55f * Ease(q), y1 = y0 + len * 1.1f * Ease(q);
                g.DrawLine(Pn(c1, (int)(130 * fade), 7f), x0, y0, x1, y1);
                g.DrawLine(Pn(c2, (int)(245 * fade), 2.5f), x0, y0, x1, y1);
                Glow(g, c2, x1, y1, v.Px(0.3f), 0.9f * fade);
            }
        }

        // ------------------------------------------------------------------ 원거리

        /// <summary>원거리 기본 공격. 활은 화살, 지팡이는 마력 구슬. 그 밖에는 false (기존 선을 쓴다).</summary>
        public bool Tracer(Graphics g, WorldModel w, ViewEffect fx, ViewState v, long now)
        {
            Entity c = w.Find(fx.EntityId);
            Weapon wp = WeaponOf(c);
            if (wp != Weapon.Bow && wp != Weapon.Staff) return false;
            float p = fx.Progress(now);
            float cx, cy; c.PosAt(now, out cx, out cy);
            float x0 = v.ToScreenX(cx), y0 = v.ToScreenY(cy) - v.Px(1.5f);
            float x1 = v.ToScreenX(fx.X), y1 = v.ToScreenY(fx.Y) - v.Px(1.1f);
            int c1, c2; Palette(wp, out c1, out c2);
            float t = Ease(Clamp01(p / 0.72f)), fade = 1f - Clamp01((p - 0.6f) / 0.4f);
            float hx = x0 + (x1 - x0) * t, hy = y0 + (y1 - y0) * t;
            // 발사 순간 시전자 손에서 번쩍
            if (p < 0.4f)
            {
                float m = 1f - p / 0.4f;
                Glow(g, c1, x0, y0, v.Px(wp == Weapon.Staff ? 1.1f : 0.8f) * (0.6f + 0.6f * (1f - m)), 0.9f * m);
                if (wp == Weapon.Staff) Star(g, c2, (int)(230 * m), x0, y0, v.Px(0.9f) * (0.5f + 0.5f * (1f - m)), p * 6f, 2f);
            }
            float tt = Math.Max(0f, t - (wp == Weapon.Bow ? 0.3f : 0.4f));
            float tx = x0 + (x1 - x0) * tt, ty = y0 + (y1 - y0) * tt;
            if (wp == Weapon.Bow)
            {
                g.DrawLine(Pn(c1, (int)(70 * fade), 8f), tx, ty, hx, hy);
                g.DrawLine(Pn(c1, (int)(150 * fade), 4f), tx, ty, hx, hy);
                g.DrawLine(Pn(0xFFFFFF, (int)(255 * fade), 1.5f), tx, ty, hx, hy);
                float dx = hx - tx, dy = hy - ty, l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l > 0.5f)
                {
                    dx /= l; dy /= l;
                    g.FillPolygon(Br(0xFFFFFF, (int)(255 * fade)), new PointF[] { new PointF(hx + dx * 7, hy + dy * 7), new PointF(hx - dy * 3.5f - dx * 3, hy + dx * 3.5f - dy * 3), new PointF(hx + dy * 3.5f - dx * 3, hy - dx * 3.5f - dy * 3) });
                }
                Glow(g, c1, hx, hy, v.Px(0.6f), 0.9f * fade);
                return true;
            }
            float r = v.Px(0.3f) * (0.9f + 0.15f * (float)Math.Sin(now / 50.0));
            for (int k = 6; k >= 1; --k)
            {
                float tk = Math.Max(0f, t - k * 0.05f);
                float kx = x0 + (x1 - x0) * tk + (Noise.Hash01(k, (int)(now / 45), 11) - 0.5f) * r * 1.2f;
                float ky = y0 + (y1 - y0) * tk + (Noise.Hash01(k, (int)(now / 45), 12) - 0.5f) * r * 1.2f;
                Glow(g, Mix(c1, 0x3A1C7A, k / 7f), kx, ky, r * (2.4f - k * 0.2f), 0.8f * fade * (1f - k * 0.1f));
            }
            Glow(g, c1, hx, hy, r * 3.2f, 0.95f * fade);
            Glow(g, c2, hx, hy, r * 1.8f, fade);
            g.FillEllipse(Br(0xFFFFFF, (int)(255 * fade)), hx - r * 0.6f, hy - r * 0.6f, r * 1.2f, r * 1.2f);
            for (int k = 0; k < 4; ++k)
            {
                double a = now / 70.0 + k * 1.5708;
                Glow(g, c2, hx + (float)Math.Cos(a) * r * 1.6f, hy + (float)Math.Sin(a) * r * 1.3f, r * 0.55f, 0.9f * fade);
            }
            return true;
        }

        // ------------------------------------------------------------------ 타격 충격

        /// <summary>타격 충격. 모든 피해에 붙는다. Value = 피해량, Variant 1 = 큰 피해, TargetId = 공격자.</summary>
        public void Hit(Graphics g, WorldModel w, ViewEffect fx, ViewState v, float p, float sx, float sy)
        {
            Entity atk = fx.TargetId != 0 ? w.Find(fx.TargetId) : null;
            Weapon wp = WeaponOf(atk);
            int c1, c2; Palette(wp, out c1, out c2);
            bool heavy = fx.Variant == 1;
            float scale = heavy ? 1.45f : 1f;
            float cy = sy - v.Px(1.1f), fade = 1f - p, e = Ease(p);
            // 섬광
            if (p < 0.5f) Glow(g, 0xFFFFFF, sx, cy, v.Px(0.9f * scale) * (0.6f + 0.8f * p), (1f - p * 2f));
            Glow(g, c1, sx, cy, v.Px(1.5f * scale) * (0.45f + 0.65f * e), 0.9f * fade);
            // 별빛
            Star(g, c2, (int)(250 * fade), sx, cy, v.Px(1.15f * scale) * (0.35f + 0.65f * e), p * 2.2f + Rn(fx, 90), heavy ? 3f : 2f);
            // 충격 고리
            float rr = v.Px(0.5f * scale + 1.6f * scale * e);
            g.DrawEllipse(Pn(c2, (int)(220 * fade), 3.5f * fade + 1f), sx - rr, cy - rr * 0.75f, rr * 2, rr * 1.5f);
            if (heavy)
            {
                float r2 = v.Px(0.4f + 2.6f * Ease(Clamp01(p * 1.3f)));
                g.DrawEllipse(Pn(c1, (int)(200 * fade), 4f * fade + 1f), sx - r2, sy - r2 * 0.5f, r2 * 2, r2);
            }
            // 방향성 파편 선: 맞은 방향으로 더 길게 튄다
            Entity tgt = fx.EntityId != 0 ? w.Find(fx.EntityId) : null;
            float dirA = float.NaN;
            if (tgt != null && (tgt.HitDirX != 0f || tgt.HitDirY != 0f)) dirA = (float)Math.Atan2(tgt.HitDirY * 0.8f, tgt.HitDirX);
            int n = heavy ? 12 : 8;
            for (int k = 0; k < n; ++k)
            {
                float a;
                if (float.IsNaN(dirA)) a = k * TwoPi / n + Rn(fx, k + 100) * 0.4f;
                else a = dirA + (Rn(fx, k + 100) - 0.5f) * (k % 3 == 0 ? 3.2f : 1.7f);
                float len0 = v.Px((0.35f + 0.4f * Rn(fx, k)) * scale) * (0.3f + e), len1 = v.Px((1.1f + 1.3f * Rn(fx, k + 20)) * scale) * e;
                g.DrawLine(Pn(k % 2 == 0 ? c2 : c1, (int)(235 * fade), 2f), sx + (float)Math.Cos(a) * len0, cy + (float)Math.Sin(a) * len0 * 0.8f, sx + (float)Math.Cos(a) * (len0 + len1), cy + (float)Math.Sin(a) * (len0 + len1) * 0.8f);
            }
            // 중력 불꽃
            int sparks = heavy ? 18 : 11;
            for (int k = 0; k < sparks; ++k)
            {
                float a = float.IsNaN(dirA) ? Rn(fx, k + 40) * TwoPi : dirA + (Rn(fx, k + 40) - 0.5f) * 3.6f;
                float spd = v.Px((1.4f + 3.0f * Rn(fx, k + 60)) * scale);
                float px = sx + (float)Math.Cos(a) * spd * p, py = cy + (float)Math.Sin(a) * spd * p * 0.8f - v.Px(0.9f) * p + v.Px(2.6f) * p * p;
                Glow(g, Mix(c1, c2, Rn(fx, k + 80)), px, py, v.Px(0.15f) * (1.25f - p), 0.95f * fade);
            }
            if (heavy)
            {
                // 큰 타격은 바닥 먼지가 한 겹 더 퍼진다
                for (int k = 0; k < 7; ++k)
                {
                    float a = k * TwoPi / 7 + Rn(fx, k + 120);
                    float d = v.Px(0.5f + 1.5f * e) * (0.6f + 0.6f * Rn(fx, k + 130));
                    float r = v.Px(0.25f + 0.2f * Rn(fx, k + 140)) * (0.7f + p);
                    g.FillEllipse(Br(0x8C7E6C, (int)(110 * fade)), sx + (float)Math.Cos(a) * d - r, sy + (float)Math.Sin(a) * d * 0.5f - v.Px(0.2f + 0.5f * p) - r, r * 2, r * 2);
                }
            }
        }
    }
}
