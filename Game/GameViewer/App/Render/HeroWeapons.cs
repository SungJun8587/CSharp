//***************************************************************************
// HeroWeapons.cs : 이미지 영웅(전사/궁수/마법사/창기사)가 공격/스킬 때 휘두르는 무기.
//   이미지에는 무기 동작 프레임이 없어 몸만 움직이므로, 손 위치에 무기를 따로 그려 몸동작과 같은 시각에 휘두른다.
//   - 시간 t(ms, 공격 시작 후)로 자세를 계산하는 순수 함수(Pose*)와 그리는 함수(Draw*)를 나눈다.
//   - 잔상: 같은 무기를 몇 프레임 전 자세로 흐리게 겹쳐 그려 속도감을 만든다.
//   - 스킬(AttackKind 1)은 크기와 광채, 타격 불꽃이 더 크다. 시전 중(CastProgress)에는 모으는 자세를 유지한다.
//   코드로 그린 영웅은 몸 그림 안에 무기 동작이 있어 여기서 그리지 않는다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public static class HeroWeapons
    {
        public const float TotalMs = 520f;
        private const float StrikeEndMs = 90f, HoldEndMs = 150f;

        // ------------------------------------------------------------------ 자세 계산 (순수)

        /// <summary>내지름 정도 0~1: 모은 자세(0) -> 빠르게 내지름(StrikeEndMs) -> 유지 -> 천천히 복귀.</summary>
        public static float Swing(float t)
        {
            if (t < 0f) return 0f;
            if (t < StrikeEndMs) { float u = t / StrikeEndMs; return 1f - (1f - u) * (1f - u) * (1f - u); }
            if (t < HoldEndMs) return 1f;
            float r = (t - HoldEndMs) / (TotalMs - HoldEndMs);
            r = r < 0f ? 0f : (r > 1f ? 1f : r);
            return 1f - r * r * (3f - 2f * r);
        }

        /// <summary>무기의 투명도: 앞부분은 또렷하고 뒤로 갈수록 사라진다.</summary>
        public static float Opacity(float t)
        {
            if (t < 300f) return 1f;
            float r = (t - 300f) / (TotalMs - 300f);
            return r >= 1f ? 0f : 1f - r;
        }

        /// <summary>
        /// 몸과 따로 그린 무기의 자세 번호(0..poses-2, 공격 자세 사이). 휘두르는 중이면 내지름 정도(Swing)에 맞춰 고르고, 모으는 중이면 첫 자세.
        /// 공격도 시전도 아니면 false(대기/걷기 자세).
        /// </summary>
        public static bool PoseOf(Entity e, long now, int poses, out int k)
        {
            k = 0;
            float t = now - e.AttackAnimMs;
            if (e.AttackAnimMs > 0 && t >= 0f && t < TotalMs) { k = (int)Math.Round(Swing(t) * (poses - 2)); return true; }
            return AttackPose.CastProgress(e, now) >= 0f;
        }

        // ------------------------------------------------------------------ 진입점

        public static void Draw(Graphics g, Entity e, ViewState v, Look look, float bx, float by, long now)
        {
            if (e.IsMonster || e.Dead) return;
            float t = now - e.AttackAnimMs;
            float cast = AttackPose.CastProgress(e, now);
            bool striking = e.AttackAnimMs > 0 && t >= 0f && t < TotalMs;
            if (!striking && cast < 0f) return;
            bool skill = e.AttackKind == 1;

            float fl = (float)Math.Sqrt(e.FaceX * e.FaceX + e.FaceY * e.FaceY);
            float fx = fl > 0.001f ? e.FaceX / fl : 1f, fy = fl > 0.001f ? e.FaceY / fl : 0f;
            float side = fx >= 0f ? 1f : -1f;
            float baseAng = (float)(Math.Atan2(fy * 0.6f, fx) * 180.0 / Math.PI);
            float u = v.Px(1.0f);
            float hx = bx + fx * u * 0.6f, hy = by - v.Px(look.H * 0.46f) + fy * u * 0.25f;
            float charge = striking ? 0f : cast;
            float tt = striking ? t : -1f;
            GraphicsState gs = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 몸과 따로 그린 무기(에셋 영웅)는 무기 자체가 그림이다. 여기서는 궤적/불꽃/화살 같은 효과만 얹는다
            AssetSet a = AssetSprites.Find(false, e.TemplateId);
            if (a != null && a.Weapon != null && a.Weapon.Poses > 2) SpriteFx(g, a, e, v, bx, by, tt, charge, skill, fx, fy);
            else
                switch (e.TemplateId)
                {
                    case 1: Warrior(g, hx, hy, u, baseAng, side, tt, charge, skill); break;
                    case 2: Archer(g, hx, hy, u, fx, fy, tt, charge, skill, e.AttackKind == 2 || skill); break;
                    case 3: Mage(g, hx, hy, u, baseAng, side, tt, charge, skill); break;
                }
            g.Restore(gs);
        }

        // ------------------------------------------------------------------ 전사: 큰 검

        private static void Warrior(Graphics g, float hx, float hy, float u, float baseAng, float side, float t, float charge, bool skill)
        {
            float len = u * (skill ? 2.8f : 2.45f);
            Color glow = skill ? Color.FromArgb(120, 190, 255) : Color.FromArgb(200, 225, 255);
            const float RestRaise = -128f;                          // 어깨 뒤로 젖힌 각도
            Func<float, float> ang = delegate (float tm) { return baseAng + side * (tm < 0f ? RestRaise - 22f * charge : RestRaise + 168f * Swing(tm)); };
            float op = t < 0f ? 1f : Opacity(t);

            if (t >= 0f)
            {
                // 호 모양 잔광 + 몇 프레임 전 자세의 잔상
                float a0 = baseAng + side * RestRaise, a1 = ang(t);
                ArcGlow(g, hx, hy, len * 0.9f, a0, a1, glow, op * (skill ? 1.15f : 1f), u * 0.55f);
                for (int k = 4; k >= 1; --k)
                {
                    float tm = t - k * 14f;
                    if (tm < 0f) continue;
                    Sword(g, hx, hy, ang(tm), len, u, op * 0.16f * (5 - k) / 4f, glow, false);
                }
                if (skill && t < 340f) Halo(g, hx, hy, ang(t), len, u, glow, op * 0.5f);
            }
            Sword(g, hx, hy, ang(t), len, u, op, glow, true);
            if (t >= StrikeEndMs - 10f && t < StrikeEndMs + 230f)
            {
                float a = ang(StrikeEndMs); double r = a * Math.PI / 180.0;
                Sparks(g, hx + (float)Math.Cos(r) * len, hy + (float)Math.Sin(r) * len, u, (t - (StrikeEndMs - 10f)) / 240f, Color.FromArgb(255, 245, 200), skill ? 10 : 6);
            }
            if (t < 0f && charge > 0f) ChargeGlow(g, hx + (float)Math.Cos(ang(t) * Math.PI / 180) * len * 0.9f, hy + (float)Math.Sin(ang(t) * Math.PI / 180) * len * 0.9f, u * 0.8f, charge, glow);
        }

        /// <summary>검 한 자루: 테이퍼진 날, 가운데 홈, 윗날 반사광, 십자 날밑, 손잡이, 장식 꼭지.</summary>
        private static void Sword(Graphics g, float hx, float hy, float angDeg, float len, float u, float opacity, Color glow, bool detail)
        {
            if (opacity < 0.03f) return;
            double r = angDeg * Math.PI / 180.0;
            float dx = (float)Math.Cos(r), dy = (float)Math.Sin(r), nx = -dy, ny = dx;
            Func<float, float, PointF> P = delegate (float along, float across) { return new PointF(hx + dx * along + nx * across, hy + dy * along + ny * across); };
            float guard = len * 0.2f, w = u * 0.34f;
            PointF[] blade = { P(guard, -w), P(len * 0.82f, -w * 0.82f), P(len, 0f), P(len * 0.82f, w * 0.82f), P(guard, w) };
            int A = (int)(255 * Math.Min(1f, opacity));
            using (SolidBrush body = new SolidBrush(Color.FromArgb(A, 214, 226, 242)))
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(A, 150, 168, 196)))
            using (Pen edge = new Pen(Color.FromArgb(A, 48, 58, 84), Math.Max(1.2f, u * 0.07f)) { LineJoin = LineJoin.Round })
            {
                g.FillPolygon(body, blade);
                g.FillPolygon(shade, new PointF[] { P(guard, 0f), P(len * 0.97f, 0f), P(len * 0.82f, w * 0.82f), P(guard, w) });     // 아랫날 그늘
                g.DrawPolygon(edge, blade);
                if (detail)
                {
                    using (Pen hi = new Pen(Color.FromArgb(A, 255, 255, 255), Math.Max(1f, u * 0.05f))) g.DrawLine(hi, P(guard * 1.3f, -w * 0.45f), P(len * 0.9f, -w * 0.2f));
                    using (Pen fuller = new Pen(Color.FromArgb(A / 2, 70, 84, 120), Math.Max(1f, u * 0.05f))) g.DrawLine(fuller, P(guard * 1.4f, 0f), P(len * 0.7f, 0f));
                }
            }
            if (!detail) return;
            using (Pen cg = new Pen(Color.FromArgb(A, 232, 190, 70), u * 0.17f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen cgEdge = new Pen(Color.FromArgb(A, 90, 62, 20), u * 0.25f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen grip = new Pen(Color.FromArgb(A, 98, 62, 38), u * 0.18f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(cgEdge, P(guard, -u * 0.62f), P(guard, u * 0.62f)); g.DrawLine(cg, P(guard, -u * 0.62f), P(guard, u * 0.62f));
                g.DrawLine(grip, P(-len * 0.14f, 0f), P(guard, 0f));
            }
            using (SolidBrush pom = new SolidBrush(Color.FromArgb(A, 232, 190, 70)))
            using (Pen po = new Pen(Color.FromArgb(A, 90, 62, 20), 1.2f))
            { PointF q = P(-len * 0.16f, 0f); g.FillEllipse(pom, q.X - u * 0.12f, q.Y - u * 0.12f, u * 0.24f, u * 0.24f); g.DrawEllipse(po, q.X - u * 0.12f, q.Y - u * 0.12f, u * 0.24f, u * 0.24f); }
        }

        // ------------------------------------------------------------------ 궁수: 활과 화살

        private static void Archer(Graphics g, float hx, float hy, float u, float fx, float fy, float t, float charge, bool skill, bool ranged)
        {
            float bowX = hx + fx * u * 0.95f, bowY = hy + fy * u * 0.5f;
            float rel = t < 0f ? -1f : Swing(t);
            float op = t < 0f ? 1f : Opacity(t);
            float draw = t < 0f ? Math.Min(1f, 0.2f + charge * 0.9f) : (t < 40f ? 1f : 0f);          // 시위를 당긴 정도. 놓으면 0
            float vib = t >= 0f && t < 260f ? (float)Math.Sin(t * 0.55) * (1f - t / 260f) * u * 0.28f : 0f;   // 놓은 뒤 시위 떨림
            float hh = u * 0.95f, bend = u * (0.34f + 0.28f * draw);
            int A = (int)(255 * Math.Min(1f, op));
            float perpX = 0f;

            PointF top = new PointF(bowX, bowY - hh), bot = new PointF(bowX, bowY + hh);
            PointF mid = new PointF(bowX + fx * bend, bowY);
            PointF nock = new PointF(bowX - fx * u * (0.15f + 1.0f * draw) + vib * fx, bowY - fy * u * 0.4f * draw);
            using (GraphicsPath limb = new GraphicsPath())
            {
                limb.AddCurve(new PointF[] { top, new PointF(bowX + fx * bend * 0.8f, bowY - hh * 0.5f), mid, new PointF(bowX + fx * bend * 0.8f, bowY + hh * 0.5f), bot }, 0.5f);
                using (Pen o = new Pen(Color.FromArgb(A, 58, 36, 18), u * 0.27f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(o, limb);
                using (Pen w = new Pen(Color.FromArgb(A, 168, 118, 62), u * 0.17f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(w, limb);
                using (Pen hi = new Pen(Color.FromArgb(A / 2, 236, 196, 130), u * 0.05f)) g.DrawPath(hi, limb);
            }
            using (Pen s = new Pen(Color.FromArgb(A, 248, 248, 236), Math.Max(1f, u * 0.045f))) { g.DrawLine(s, top, nock); g.DrawLine(s, nock, bot); }
            using (SolidBrush grip = new SolidBrush(Color.FromArgb(A, 120, 70, 40))) g.FillEllipse(grip, mid.X - u * 0.12f, mid.Y - u * 0.3f, u * 0.24f, u * 0.6f);

            if (t < 0f && draw > 0.2f)             // 시전 중: 화살을 메겨 당긴다
            {
                Arrow(g, nock.X, nock.Y, mid.X + fx * u * 0.55f, mid.Y + fy * u * 0.3f, u, A, skill);
                if (charge > 0.5f) ChargeGlow(g, mid.X + fx * u * 0.4f, mid.Y, u * 0.6f, charge, Color.FromArgb(180, 255, 160));
            }
            if (t >= 0f)
            {
                // 발사: 화살이 날아가며 속도선을 남긴다 (스킬이면 부채꼴로 세 발)
                float f = Math.Max(0f, Math.Min(1f, (t - 30f) / 300f));
                int shots = skill ? 3 : 1;
                for (int i = 0; i < shots; ++i)
                {
                    float spread = shots == 1 ? 0f : (i - 1) * 0.2f;
                    float cs = (float)Math.Cos(spread), sn = (float)Math.Sin(spread);
                    float dx = fx * cs - fy * sn, dy = fx * sn + fy * cs;
                    float d0 = u * 0.8f + u * 10f * f * f * (3f - 2f * f) * 0.5f + u * 7f * f;
                    float ox = bowX + dx * d0, oy = bowY + dy * d0 * 0.55f;
                    int fa = (int)(255 * (1f - f * f) * op);
                    using (Pen tr = new Pen(Color.FromArgb(fa / 3, 220, 255, 190), u * 0.14f) { StartCap = LineCap.Round }) g.DrawLine(tr, ox - dx * u * 3.2f * f, oy - dy * u * 1.8f * f, ox, oy);
                    Arrow(g, ox - dx * u * 1.3f, oy - dy * u * 0.7f, ox, oy, u, fa, skill);
                }
                if (t < 200f) Sparks(g, mid.X + fx * u * 0.2f, mid.Y, u * 0.6f, t / 200f, Color.FromArgb(230, 255, 200), 6);
            }
        }

        private static void Arrow(Graphics g, float x0, float y0, float x1, float y1, float u, int alpha, bool glow)
        {
            if (alpha < 10) return;
            alpha = Math.Min(255, alpha);
            float dx = x1 - x0, dy = y1 - y0, l = (float)Math.Sqrt(dx * dx + dy * dy); if (l < 1f) return;
            dx /= l; dy /= l; float nx = -dy, ny = dx;
            if (glow) using (Pen gl = new Pen(Color.FromArgb(alpha / 3, 170, 255, 150), u * 0.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(gl, x0, y0, x1, y1);
            using (Pen shaft = new Pen(Color.FromArgb(alpha, 226, 200, 150), Math.Max(1.5f, u * 0.075f))) g.DrawLine(shaft, x0, y0, x1, y1);
            using (SolidBrush head = new SolidBrush(Color.FromArgb(alpha, 236, 240, 248)))
                g.FillPolygon(head, new PointF[] { new PointF(x1 + dx * u * 0.38f, y1 + dy * u * 0.38f), new PointF(x1 + nx * u * 0.12f, y1 + ny * u * 0.12f), new PointF(x1 - nx * u * 0.12f, y1 - ny * u * 0.12f) });
            using (SolidBrush fl = new SolidBrush(Color.FromArgb(alpha, 210, 70, 70)))
            {
                g.FillPolygon(fl, new PointF[] { new PointF(x0, y0), new PointF(x0 + dx * u * 0.4f + nx * u * 0.16f, y0 + dy * u * 0.4f + ny * u * 0.16f), new PointF(x0 + dx * u * 0.4f, y0 + dy * u * 0.4f) });
                g.FillPolygon(fl, new PointF[] { new PointF(x0, y0), new PointF(x0 + dx * u * 0.4f - nx * u * 0.16f, y0 + dy * u * 0.4f - ny * u * 0.16f), new PointF(x0 + dx * u * 0.4f, y0 + dy * u * 0.4f) });
            }
        }

        // ------------------------------------------------------------------ 마법사: 지팡이

        private static void Mage(Graphics g, float hx, float hy, float u, float baseAng, float side, float t, float charge, bool skill)
        {
            float op = t < 0f ? 1f : Opacity(t);
            float s = t < 0f ? 0f : Swing(t);
            // 위를 향한 자세에서 앞으로 내민다
            float ang = baseAng + side * (-100f + 80f * s) + (t < 0f ? side * -8f * charge : 0f);
            float len = u * (skill ? 2.6f : 2.3f);
            double r = ang * Math.PI / 180.0;
            float dx = (float)Math.Cos(r), dy = (float)Math.Sin(r), nx = -dy, ny = dx;
            PointF tail = new PointF(hx - dx * len * 0.22f, hy - dy * len * 0.22f), tip = new PointF(hx + dx * len, hy + dy * len);
            int A = (int)(255 * op);
            using (Pen o = new Pen(Color.FromArgb(A, 44, 28, 16), u * 0.27f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(o, tail, tip);
            using (Pen w = new Pen(Color.FromArgb(A, 140, 96, 56), u * 0.17f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(w, tail, tip);
            using (Pen gold = new Pen(Color.FromArgb(A, 232, 190, 70), u * 0.1f)) { for (int i = 0; i < 3; ++i) { float k = 0.45f + i * 0.1f; g.DrawLine(gold, hx + dx * len * k - nx * u * 0.13f, hy + dy * len * k - ny * u * 0.13f, hx + dx * len * k + nx * u * 0.13f, hy + dy * len * k + ny * u * 0.13f); } }

            // 머리 장식: 금빛 고리 + 구슬. 모을수록 밝아지고 내지르면 폭발하듯 커진다.
            float glow = t < 0f ? 0.5f + charge * 1.3f : 1.1f + 1.0f * s * (t < 200f ? 1f : 0f) + (t < 200f ? 0f : 0.6f * (1f - (t - 200f) / (TotalMs - 200f)));
            if (glow < 0.4f) glow = 0.4f;
            float orb = u * 0.2f * glow;
            using (Pen ring = new Pen(Color.FromArgb(A, 232, 190, 70), u * 0.1f)) g.DrawEllipse(ring, tip.X - u * 0.3f, tip.Y - u * 0.3f, u * 0.6f, u * 0.6f);
            Color core = Color.FromArgb(255, 250, 215);
            using (SolidBrush h2 = new SolidBrush(Color.FromArgb(Math.Min(120, (int)(50 * glow)), 255, 140, 50))) g.FillEllipse(h2, tip.X - orb * 3.4f, tip.Y - orb * 3.4f, orb * 6.8f, orb * 6.8f);
            using (SolidBrush h1 = new SolidBrush(Color.FromArgb(Math.Min(200, (int)(110 * glow)), 255, 170, 70))) g.FillEllipse(h1, tip.X - orb * 1.9f, tip.Y - orb * 1.9f, orb * 3.8f, orb * 3.8f);
            using (SolidBrush c1 = new SolidBrush(Color.FromArgb(A, 255, 196, 100))) g.FillEllipse(c1, tip.X - orb, tip.Y - orb, orb * 2f, orb * 2f);
            using (SolidBrush c2 = new SolidBrush(Color.FromArgb(A, core))) g.FillEllipse(c2, tip.X - orb * 0.5f, tip.Y - orb * 0.5f, orb, orb);

            if (t < 0f && charge > 0f)             // 모으는 동안 불씨가 구슬로 빨려든다
            {
                for (int i = 0; i < 7; ++i)
                {
                    double th = i * Math.PI * 2 / 7 + charge * 5.0;
                    float rr = u * (1.6f - 1.3f * charge) + u * 0.15f * i % 3;
                    using (SolidBrush p = new SolidBrush(Color.FromArgb((int)(220 * charge), 255, 200, 110))) g.FillEllipse(p, tip.X + (float)Math.Cos(th) * rr - u * 0.05f, tip.Y + (float)Math.Sin(th) * rr * 0.8f - u * 0.05f, u * 0.1f, u * 0.1f);
                }
            }
            if (t >= 60f && t < 360f)               // 방출: 충격 고리 + 앞으로 뻗는 빛줄기
            {
                float k = (t - 60f) / 300f;
                float rr = u * (0.5f + 2.6f * k);
                using (Pen sh = new Pen(Color.FromArgb((int)(220 * (1f - k)), 255, 190, 90), u * 0.14f * (1f - k) + 1f)) g.DrawEllipse(sh, tip.X - rr, tip.Y - rr * 0.7f, rr * 2f, rr * 1.4f);
                using (Pen beam = new Pen(Color.FromArgb((int)(190 * (1f - k)), 255, 220, 140), u * 0.3f * (1f - k) + 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(beam, tip.X, tip.Y, tip.X + dx * u * (1.5f + 5f * k) * (skill ? 1.4f : 1f), tip.Y + dy * u * (1.5f + 5f * k) * 0.7f * (skill ? 1.4f : 1f));
                Sparks(g, tip.X, tip.Y, u, k, Color.FromArgb(255, 225, 150), skill ? 10 : 6);
            }
        }

        // ------------------------------------------------------------------ 창기사: 창 찌르기

        /// <summary>
        /// 창 자체는 몸과 따로 그린 이미지(Creatures)이고, 여기서는 창끝 앵커를 따라가는 잔광/불꽃/기모으기 빛만 낸다.
        /// 창은 자세 사이를 건너뛰며 바뀌므로 잔광 위치는 자세 사이를 보간한 창끝으로 구한다.
        /// </summary>
        private static void SpriteFx(Graphics g, AssetSet a, Entity e, ViewState v, float bx, float by, float t, float charge, bool skill, float fx, float fy)
        {
            WeaponLayers w = a.Weapon;
            int dir = a.IndexFor(e.FaceX, e.FaceY);
            float s = (float)(v.Scale * a.Scale), u = v.Px(1.0f);
            float op = t < 0f ? 1f : Opacity(t);
            Color glow = skill ? Color.FromArgb(110, 235, 225) : Color.FromArgb(170, 235, 235);
            Func<float, PointF[]> at = delegate (float tm)
            {
                float f = tm < 0f ? 0f : Swing(tm) * (w.Poses - 2);
                int i = Math.Min((int)f, w.Poses - 3); float fr = f - i;
                PointF[] r = new PointF[2];
                for (int k = 0; k < 2; ++k)
                {
                    float[][][] arr = k == 0 ? w.Grip : w.Tip;
                    float[] p0 = arr[1 + i][dir], p1 = arr[2 + i][dir];
                    r[k] = new PointF(bx + (p0[0] + (p1[0] - p0[0]) * fr - a.Cx) * s, by + (p0[1] + (p1[1] - p0[1]) * fr - a.Foot) * s);
                }
                return r;
            };
            PointF[] cur = at(t);
            PointF grip = cur[0], tip = cur[1];
            if (e.TemplateId == 1) { SwordFx(g, at, t, op, u, skill, tip); return; }
            if (e.TemplateId == 2) { BowFx(g, grip, t, charge, op, u, fx, fy, skill); return; }
            if (e.TemplateId == 3) { StaffFx(g, tip, t, charge, op, u, fx, fy, skill); return; }
            if (t >= 0f)
            {
                for (int k = 4; k >= 1; --k)
                {
                    float tm = t - k * 14f; if (tm < 0f) continue;
                    PointF[] p = at(tm);
                    using (Pen pen = new Pen(Color.FromArgb((int)(60 * op * (5 - k) / 4f), glow), u * 0.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, p[0].X + (p[1].X - p[0].X) * 0.5f, p[0].Y + (p[1].Y - p[0].Y) * 0.5f, p[1].X, p[1].Y);
                }
                float k0 = Swing(t);
                float dx = tip.X - grip.X, dy = tip.Y - grip.Y, dl = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dl > 0.01f)
                {
                    dx /= dl; dy /= dl;
                    using (Pen p = new Pen(Color.FromArgb((int)(150 * op * k0), glow), u * 0.42f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(p, tip.X - dx * dl * 0.45f - dx * u * 1.2f * k0, tip.Y - dy * dl * 0.45f - dy * u * 1.2f * k0, tip.X, tip.Y);
                }
            }
            if (t >= StrikeEndMs - 10f && t < StrikeEndMs + 230f)
                Sparks(g, tip.X, tip.Y, u, (t - (StrikeEndMs - 10f)) / 240f, Color.FromArgb(220, 250, 245), skill ? 10 : 6);
            if (t < 0f && charge > 0f) ChargeGlow(g, tip.X, tip.Y, u * 0.7f, charge, glow);
        }

        /// <summary>검: 칼날이 지나간 자리를 메우는 면 모양 궤적(최근 7개 표본)과 타격 불꽃.</summary>
        private static void SwordFx(Graphics g, Func<float, PointF[]> at, float t, float op, float u, bool skill, PointF tip)
        {
            Color c = skill ? Color.FromArgb(255, 200, 90) : Color.FromArgb(235, 240, 255);
            if (t >= 0f)
            {
                PointF[] prev = null;
                for (int k = 6; k >= 0; --k)
                {
                    float tm = t - k * 12f; if (tm < 0f) continue;
                    PointF[] p = at(tm);
                    PointF inner = new PointF(p[0].X + (p[1].X - p[0].X) * 0.3f, p[0].Y + (p[1].Y - p[0].Y) * 0.3f);
                    if (prev != null)
                    {
                        float fade = (7 - k) / 7f;
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            gp.AddPolygon(new PointF[] { prev[0], prev[1], p[1], p[0] });
                            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(120 * fade * fade * Math.Min(1f, op)), c))) g.FillPath(b, gp);
                        }
                        using (Pen pen = new Pen(Color.FromArgb((int)(200 * fade * Math.Min(1f, op)), 255, 255, 255), Math.Max(1f, u * 0.06f))) g.DrawLine(pen, prev[1], p[1]);
                    }
                    prev = new PointF[] { inner, p[1] };
                }
            }
            if (t >= StrikeEndMs - 10f && t < StrikeEndMs + 230f)
                Sparks(g, tip.X, tip.Y, u, (t - (StrikeEndMs - 10f)) / 240f, Color.FromArgb(255, 240, 200), skill ? 10 : 6);
        }

        /// <summary>활: 활과 메긴 화살은 그림이다. 놓은 뒤 날아가는 화살과 모으는 빛만 그린다.</summary>
        private static void BowFx(Graphics g, PointF grip, float t, float charge, float op, float u, float fx, float fy, bool skill)
        {
            if (t < 0f) { if (charge > 0.4f) ChargeGlow(g, grip.X, grip.Y, u * 0.5f, charge, Color.FromArgb(180, 255, 160)); return; }
            float f = Math.Max(0f, Math.Min(1f, (t - 30f) / 300f));
            int shots = skill ? 3 : 1;
            for (int i = 0; i < shots; ++i)
            {
                float spread = shots == 1 ? 0f : (i - 1) * 0.2f;
                float cs = (float)Math.Cos(spread), sn = (float)Math.Sin(spread);
                float dx = fx * cs - fy * sn, dy = fx * sn + fy * cs;
                float d0 = u * 0.8f + u * 10f * f * f * (3f - 2f * f) * 0.5f + u * 7f * f;
                float ox = grip.X + dx * d0, oy = grip.Y + dy * d0 * 0.55f;
                int fa = (int)(255 * (1f - f * f) * Math.Min(1f, op));
                if (t < 30f) continue;
                using (Pen tr = new Pen(Color.FromArgb(fa / 3, 220, 255, 190), u * 0.14f) { StartCap = LineCap.Round }) g.DrawLine(tr, ox - dx * u * 3.2f * f, oy - dy * u * 1.8f * f, ox, oy);
                Arrow(g, ox - dx * u * 1.3f, oy - dy * u * 0.7f, ox, oy, u, fa, skill);
            }
            if (t < 200f) Sparks(g, grip.X + fx * u * 0.2f, grip.Y, u * 0.6f, t / 200f, Color.FromArgb(230, 255, 200), 6);
        }

        /// <summary>지팡이: 머리 구슬이 모일수록 밝아지고, 휘두르면 충격 고리와 빛줄기가 번진다.</summary>
        private static void StaffFx(Graphics g, PointF tip, float t, float charge, float op, float u, float fx, float fy, bool skill)
        {
            float s = t < 0f ? 0f : Swing(t);
            float glow = t < 0f ? 0.5f + charge * 1.3f : 1.1f + 1.0f * s * (t < 200f ? 1f : 0f) + (t < 200f ? 0f : 0.6f * Math.Max(0f, 1f - (t - 200f) / (TotalMs - 200f)));
            float orb = u * 0.2f * glow;
            using (SolidBrush h2 = new SolidBrush(Color.FromArgb(Math.Min(120, (int)(50 * glow)), 255, 140, 50))) g.FillEllipse(h2, tip.X - orb * 3.4f, tip.Y - orb * 3.4f, orb * 6.8f, orb * 6.8f);
            using (SolidBrush h1 = new SolidBrush(Color.FromArgb(Math.Min(200, (int)(110 * glow)), 255, 170, 70))) g.FillEllipse(h1, tip.X - orb * 1.9f, tip.Y - orb * 1.9f, orb * 3.8f, orb * 3.8f);
            if (t < 0f && charge > 0f)
                for (int i = 0; i < 7; ++i)
                {
                    double th = i * Math.PI * 2 / 7 + charge * 5.0;
                    float rr = u * (1.6f - 1.3f * charge) + u * 0.15f * (i % 3);
                    using (SolidBrush p = new SolidBrush(Color.FromArgb((int)(220 * charge), 255, 200, 110))) g.FillEllipse(p, tip.X + (float)Math.Cos(th) * rr - u * 0.05f, tip.Y + (float)Math.Sin(th) * rr * 0.8f - u * 0.05f, u * 0.1f, u * 0.1f);
                }
            if (t >= 60f && t < 360f)
            {
                float k = (t - 60f) / 300f, rr = u * (0.5f + 2.6f * k), m = skill ? 1.4f : 1f;
                using (Pen sh = new Pen(Color.FromArgb((int)(220 * (1f - k)), 255, 190, 90), u * 0.14f * (1f - k) + 1f)) g.DrawEllipse(sh, tip.X - rr, tip.Y - rr * 0.7f, rr * 2f, rr * 1.4f);
                using (Pen beam = new Pen(Color.FromArgb((int)(190 * (1f - k)), 255, 220, 140), u * 0.3f * (1f - k) + 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(beam, tip.X, tip.Y, tip.X + fx * u * (1.5f + 5f * k) * m, tip.Y + fy * u * (1.5f + 5f * k) * 0.7f * m);
                Sparks(g, tip.X, tip.Y, u, k, Color.FromArgb(255, 225, 150), skill ? 10 : 6);
            }
        }

        // ------------------------------------------------------------------ 공통 효과

        /// <summary>무기가 지나간 부채꼴 모양 잔광. a0 에서 a1 까지, 끝(최근)으로 갈수록 진하다.</summary>
        private static void ArcGlow(Graphics g, float cx, float cy, float r, float a0, float a1, Color c, float opacity, float w)
        {
            float sweep = a1 - a0;
            if (Math.Abs(sweep) < 4f || opacity < 0.04f) return;
            RectangleF rc = new RectangleF(cx - r, cy - r * 0.85f, r * 2f, r * 1.7f);
            const int N = 5;
            for (int i = 0; i < N; ++i)
            {
                float s0 = a0 + sweep * i / N, sw = sweep / N + (sweep > 0 ? 1.5f : -1.5f);
                float k = (i + 1f) / N;
                int al = (int)(210 * k * k * Math.Min(1f, opacity));
                using (Pen p = new Pen(Color.FromArgb(al / 3, c), w * 2.4f * (0.4f + 0.6f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, rc, s0, sw);
                using (Pen p = new Pen(Color.FromArgb(al, 255, 255, 255), Math.Max(1f, w * 0.5f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, rc, s0, sw);
            }
        }

        /// <summary>스킬일 때 날 둘레에 번지는 광채.</summary>
        private static void Halo(Graphics g, float hx, float hy, float ang, float len, float u, Color c, float opacity)
        {
            double r = ang * Math.PI / 180.0;
            using (Pen p = new Pen(Color.FromArgb((int)(120 * Math.Min(1f, opacity)), c), u * 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(p, hx + (float)Math.Cos(r) * len * 0.25f, hy + (float)Math.Sin(r) * len * 0.25f, hx + (float)Math.Cos(r) * len, hy + (float)Math.Sin(r) * len);
        }

        /// <summary>타격 불꽃: 중심에서 사방으로 튀는 짧은 선과 점. k 0~1.</summary>
        private static void Sparks(Graphics g, float x, float y, float u, float k, Color c, int n)
        {
            if (k < 0f || k >= 1f) return;
            int al = (int)(255 * (1f - k));
            float rr = u * (0.3f + 1.8f * k);
            using (Pen p = new Pen(Color.FromArgb(al, c), Math.Max(1f, u * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < n; ++i)
                {
                    double th = i * Math.PI * 2 / n + 0.35;
                    float c0 = (float)Math.Cos(th), s0 = (float)Math.Sin(th);
                    g.DrawLine(p, x + c0 * rr * 0.55f, y + s0 * rr * 0.45f, x + c0 * rr, y + s0 * rr * 0.8f);
                }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(al, 255, 255, 240))) { float d = u * 0.28f * (1f - k) + 1f; g.FillEllipse(b, x - d, y - d, d * 2f, d * 2f); }
        }

        /// <summary>시전 중 무기 끝에 모이는 빛.</summary>
        private static void ChargeGlow(Graphics g, float x, float y, float r, float charge, Color c)
        {
            float rr = r * (0.5f + charge);
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(70 + 120 * charge), c))) g.FillEllipse(b, x - rr, y - rr, rr * 2f, rr * 2f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(120 + 130 * charge), 255, 255, 240))) g.FillEllipse(b, x - rr * 0.4f, y - rr * 0.4f, rr * 0.8f, rr * 0.8f);
        }
    }
}
