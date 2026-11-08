//***************************************************************************
// Monsters.cs : 몬스터 10종(+그 밖의 종류용 기본형)의 그림. Creatures.cs 의 영웅과 같은 규칙(단위 좌표, 발 밑 (0,0), 프레임 0~7 걷기, 8 대기).
// 말랑한 3D 카툰 양식: 통통한 몸, 짧은 팔다리, 크고 동그란 눈(가로 줄 동공), 진한 검정 대신 몸색을 어둡게 한 얇은 테두리.
// 눈/입/몸 그리는 규칙은 Soft, GoogleEye, Mouth 한 곳에 모아 종류가 달라도 같은 모양으로 보이게 한다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GameViewer.App.Render
{
    internal static class Monsters
    {
        private static readonly Color Bone = Color.FromArgb(246, 238, 214), BoneD = Color.FromArgb(190, 172, 140);
        private static readonly Color Steel = Color.FromArgb(214, 220, 234), SteelD = Color.FromArgb(110, 118, 140);
        private static readonly Color Wood = Color.FromArgb(176, 122, 70), WoodD = Color.FromArgb(104, 66, 36);
        private static readonly Color Eyewhite = Color.FromArgb(252, 252, 250);
        private static readonly Color Pupil = Color.FromArgb(44, 40, 54);

        public static void Paint(Graphics g, uint tpl, int frame)
        {
            float ph = frame < CreatureRenderer.FrameIdle ? frame / 8f * 6.2832f : 0f;
            float sn = frame < CreatureRenderer.FrameIdle ? (float)Math.Sin(ph) : 0f;
            float cs = frame < CreatureRenderer.FrameIdle ? (float)Math.Cos(ph) : 1f;
            bool walk = frame < CreatureRenderer.FrameIdle;
            switch (tpl)
            {
                case 1: Slime(g, sn, Color.FromArgb(160, 240, 120), Color.FromArgb(40, 150, 70), 1.0f, false); break;
                case 2: Goblin(g, sn, cs, walk); break;
                case 3: Wolf(g, sn, cs, Color.FromArgb(176, 184, 206), 1.0f, false); break;
                case 4: Bat(g, sn); break;
                case 5: Skeleton(g, sn, cs, walk); break;
                case 6: Spider(g, sn, cs); break;
                case 7: Orc(g, sn, cs, walk); break;
                case 8: Wolf(g, sn, cs, Color.FromArgb(120, 124, 160), 1.55f, true); break;
                case 9: Ogre(g, sn, cs, walk); break;
                case 10: Slime(g, sn, Color.FromArgb(176, 168, 255), Color.FromArgb(78, 62, 190), 2.3f, true); break;
                default: Slime(g, sn, Col.Light(WorldRenderer.MonsterColor(tpl), 0.2f), Col.Dark(WorldRenderer.MonsterColor(tpl), 0.4f), 1.0f, false); break;
            }
        }

        // ------------------------------------------------------------------ 공통 부품

        private static Color Edge(Color c) { return Col.Alpha(Col.Dark(c, 0.55f), 175); }

        /// <summary>말랑한 공 모양 몸. 왼쪽 위 빛, 어두운 가장자리, 작은 반사광.</summary>
        private static void Soft(Graphics g, float cx, float cy, float rx, float ry, Color c)
        {
            P.Ball(g, cx, cy, rx, ry, Col.Light(c, 0.4f), Col.Dark(c, 0.24f), Edge(c), 0.035f);
            P.Fill(g, cx - rx * 0.4f, cy - ry * 0.56f, rx * 0.2f, ry * 0.11f, Color.FromArgb(120, 255, 255, 255));
        }

        /// <summary>같은 몸색으로 그린 짧고 통통한 팔다리.</summary>
        private static void Stub(Graphics g, float x0, float y0, float x1, float y1, float w, Color c)
        {
            P.Limb(g, x0, y0, x1, y1, w, c, Edge(c));
        }

        /// <summary>크고 동그란 눈: 흰자 + 가로 줄 동공. look 은 -1..1.</summary>
        private static void GoogleEye(Graphics g, float cx, float cy, float r, float look, Color pupil)
        {
            P.Ball(g, cx, cy, r, r * 1.06f, Eyewhite, Color.FromArgb(206, 210, 222), Color.FromArgb(160, 70, 70, 96), Math.Max(0.02f, r * 0.08f));
            P.Fill(g, cx + look * r * 0.32f, cy + r * 0.14f, r * 0.5f, r * 0.15f, pupil);
        }

        /// <summary>벌린 입 (안쪽 + 혀). w, h 는 반폭/반높이.</summary>
        private static void Mouth(Graphics g, float cx, float cy, float w, float h)
        {
            P.Fill(g, cx, cy, w, h, Color.FromArgb(120, 26, 44));
            P.Fill(g, cx, cy + h * 0.42f, w * 0.55f, h * 0.42f, Color.FromArgb(255, 124, 140));
        }

        private static void Tooth(Graphics g, float x, float y, float w, float h)
        {
            P.Poly(g, new PointF[] { new PointF(x - w, y), new PointF(x + w, y), new PointF(x, y + h) }, Color.White, Bone, Color.Transparent, 0f);
        }

        private static void Feet(Graphics g, float x, float hipY, float sn, float cs, float w, float len, Color c)
        {
            float lx = -x + sn * 0.2f * len, rx = x - sn * 0.2f * len;
            float ly = -Math.Max(0f, cs) * 0.12f * len, ry = -Math.Max(0f, -cs) * 0.12f * len;
            Stub(g, -x, hipY, lx, ly - 0.08f, w, c);
            Stub(g, x, hipY, rx, ry - 0.08f, w, c);
            Soft(g, lx + 0.04f, ly - 0.05f, w * 0.85f, w * 0.5f, Col.Dark(c, 0.08f));
            Soft(g, rx + 0.04f, ry - 0.05f, w * 0.85f, w * 0.5f, Col.Dark(c, 0.08f));
        }

        // ------------------------------------------------------------------ 슬라임 / 왕 슬라임
        private static void Slime(Graphics g, float sn, Color light, Color dark, float s, bool crown)
        {
            float sq = sn * 0.07f;
            float w = 0.95f * s * (1f + sq), h = 0.92f * s * (1f - sq * 1.4f);
            P.Fill(g, 0f, -0.03f * s, w * 1.0f, h * 0.2f, Color.FromArgb(70, 10, 20, 20));
            P.Ball(g, 0f, -h * 0.95f, w, h, Col.Alpha(Col.Light(light, 0.25f), 245), Col.Alpha(dark, 240), Edge(dark), 0.04f * s);
            P.Ball(g, w * 0.06f, -h * 0.74f, w * 0.55f, h * 0.42f, Col.Alpha(Col.Light(light, 0.35f), 90), Col.Alpha(light, 10), Color.Transparent, 0f);
            P.Fill(g, -w * 0.42f, -h * 1.55f, w * 0.2f, h * 0.1f, Color.FromArgb(160, 255, 255, 255));
            P.Fill(g, w * 0.46f, -h * 0.42f, 0.06f * s, 0.06f * s, Color.FromArgb(110, 255, 255, 255));
            GoogleEye(g, -w * 0.34f, -h * 1.16f, 0.24f * s, 0f, Pupil);
            GoogleEye(g, w * 0.34f, -h * 1.16f, 0.24f * s, 0f, Pupil);
            Mouth(g, 0f, -h * 0.74f, w * 0.2f, h * 0.13f);
            if (crown)
            {
                float cy = -h * 1.9f, cw = 0.62f * s;
                P.Poly(g, new PointF[] { new PointF(-cw, cy + 0.34f * s), new PointF(-cw, cy - 0.1f * s), new PointF(-cw * 0.5f, cy + 0.12f * s), new PointF(0f, cy - 0.28f * s), new PointF(cw * 0.5f, cy + 0.12f * s), new PointF(cw, cy - 0.1f * s), new PointF(cw, cy + 0.34f * s) },
                    Color.FromArgb(255, 234, 120), Color.FromArgb(214, 150, 40), Color.FromArgb(150, 110, 30, 10), 0.04f);
                P.Ball(g, 0f, cy + 0.14f * s, 0.08f * s, 0.08f * s, Color.FromArgb(255, 150, 150), Color.FromArgb(200, 40, 60), Color.Transparent, 0f);
            }
        }

        // ------------------------------------------------------------------ 고블린
        private static void Goblin(Graphics g, float sn, float cs, bool walk)
        {
            Color sk = Color.FromArgb(150, 224, 72);
            float bob = walk ? -Math.Abs(cs) * 0.07f : 0f;
            Feet(g, 0.24f, -0.62f + bob, sn, cs, 0.26f, 0.7f, Col.Dark(sk, 0.12f));
            // 귀 (머리 뒤)
            P.Poly(g, new PointF[] { new PointF(-0.42f, -1.55f + bob), new PointF(-1.18f, -1.95f + bob), new PointF(-0.5f, -1.2f + bob) }, Col.Light(sk, 0.2f), Col.Dark(sk, 0.15f), Edge(sk), 0.035f);
            P.Poly(g, new PointF[] { new PointF(0.42f, -1.55f + bob), new PointF(1.18f, -1.95f + bob), new PointF(0.5f, -1.2f + bob) }, Col.Light(sk, 0.2f), Col.Dark(sk, 0.15f), Edge(sk), 0.035f);
            Soft(g, 0f, -0.82f + bob, 0.46f, 0.42f, sk);
            P.Poly(g, new PointF[] { new PointF(-0.4f, -0.66f + bob), new PointF(0.4f, -0.66f + bob), new PointF(0.3f, -0.36f), new PointF(0f, -0.28f), new PointF(-0.3f, -0.36f) }, Wood, WoodD, Edge(WoodD), 0.03f);
            Stub(g, -0.4f, -0.95f + bob, -0.64f - sn * 0.05f, -0.62f + bob, 0.24f, sk);
            float cx = 0.68f + sn * 0.04f;
            Stub(g, 0.4f, -0.95f + bob, cx, -0.66f + bob, 0.24f, sk);
            Stub(g, cx, -0.66f + bob, cx + 0.12f, -1.55f + bob, 0.1f, Wood);
            Soft(g, cx + 0.14f, -1.66f + bob, 0.22f, 0.28f, Wood);
            Soft(g, 0f, -1.5f + bob, 0.64f, 0.54f, sk);
            GoogleEye(g, -0.25f, -1.58f + bob, 0.21f, 0f, Pupil);
            GoogleEye(g, 0.25f, -1.58f + bob, 0.21f, 0f, Pupil);
            P.Fill(g, 0f, -1.36f + bob, 0.05f, 0.04f, Color.FromArgb(90, 40, 100, 20));
            Mouth(g, 0f, -1.2f + bob, 0.18f, 0.1f);
            Tooth(g, -0.1f, -1.3f + bob, 0.04f, 0.07f); Tooth(g, 0.1f, -1.3f + bob, 0.04f, 0.07f);
        }

        // ------------------------------------------------------------------ 늑대 / 늑대 우두머리 (오른쪽을 본다)
        private static void Wolf(Graphics g, float sn, float cs, Color fur, float s, bool alpha)
        {
            Color furD = Col.Dark(fur, 0.22f);
            float bob = -Math.Abs(cs) * 0.05f * s;
            using (GraphicsPath tail = new GraphicsPath())
            {
                tail.AddBezier(-0.85f * s, -1.0f * s + bob, -1.3f * s, -1.0f * s, -1.45f * s, -1.4f * s + sn * 0.1f, -1.7f * s, -1.3f * s + sn * 0.1f);
                using (Pen o = new Pen(Edge(fur), 0.34f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(o, tail);
                using (Pen p = new Pen(Col.Mix(fur, furD, 0.2f), 0.28f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(p, tail);
            }
            float[] lxs = { -0.6f, -0.35f, 0.5f, 0.75f };
            float[] off = { 1f, -1f, -1f, 1f };
            for (int i = 0; i < 4; ++i)
            {
                float fx = lxs[i] * s + sn * off[i] * 0.3f * s;
                float fy = -Math.Max(0f, cs * off[i]) * 0.14f * s;
                Color lc = i % 2 == 0 ? furD : Col.Mix(fur, furD, 0.3f);
                Stub(g, lxs[i] * s, -0.66f * s + bob, fx, fy - 0.06f, 0.28f * s, lc);
                Soft(g, fx + 0.04f * s, fy - 0.04f, 0.2f * s, 0.12f * s, Col.Light(lc, 0.1f));
            }
            Soft(g, 0f, -0.98f * s + bob, 1.0f * s, 0.54f * s, fur);
            P.Ball(g, 0f, -0.78f * s + bob, 0.7f * s, 0.26f * s, Col.Alpha(Color.White, 200), Col.Alpha(fur, 0), Color.Transparent, 0f);
            if (alpha)
                for (int i = 0; i < 5; ++i)
                    P.Poly(g, new PointF[] { new PointF((0.55f - i * 0.32f) * s, -1.46f * s + bob), new PointF((0.4f - i * 0.32f) * s, -1.9f * s + bob), new PointF((0.25f - i * 0.32f) * s, -1.44f * s + bob) }, Col.Light(fur, 0.5f), fur, Edge(fur), 0.03f);
            float hx = 0.95f * s, hy = -1.22f * s + bob;
            P.Poly(g, new PointF[] { new PointF(hx - 0.2f * s, hy - 0.28f * s), new PointF(hx - 0.32f * s, hy - 0.72f * s), new PointF(hx + 0.08f * s, hy - 0.36f * s) }, fur, furD, Edge(fur), 0.035f);
            Soft(g, hx, hy, 0.52f * s, 0.46f * s, fur);
            Soft(g, hx + 0.52f * s, hy + 0.1f * s, 0.3f * s, 0.2f * s, Col.Light(fur, 0.15f));
            P.Fill(g, hx + 0.78f * s, hy + 0.02f * s, 0.08f * s, 0.06f * s, Pupil);
            P.Poly(g, new PointF[] { new PointF(hx + 0.12f * s, hy - 0.34f * s), new PointF(hx + 0.3f * s, hy - 0.76f * s), new PointF(hx + 0.42f * s, hy - 0.3f * s) }, Col.Light(fur, 0.1f), furD, Edge(fur), 0.035f);
            Color pc = alpha ? Color.FromArgb(220, 40, 40) : Pupil;
            if (alpha) P.Glow(g, hx + 0.32f * s, hy - 0.08f * s, 0.5f * s, Color.FromArgb(255, 60, 40), 130);
            GoogleEye(g, hx + 0.12f * s, hy - 0.1f * s, 0.2f * s, 0.6f, pc);
            GoogleEye(g, hx + 0.46f * s, hy - 0.1f * s, 0.17f * s, 0.6f, pc);
            Mouth(g, hx + 0.46f * s, hy + 0.27f * s, 0.14f * s, 0.07f * s);
            if (alpha) P.Line(g, hx - 0.1f * s, hy - 0.36f * s, hx + 0.1f * s, hy - 0.04f * s, 0.045f * s, Color.FromArgb(190, 210, 70, 70));
        }

        // ------------------------------------------------------------------ 박쥐
        private static void Bat(Graphics g, float sn)
        {
            float cy = -1.3f + sn * 0.1f;
            float flap = sn * 0.85f;
            Color m = Color.FromArgb(166, 98, 214), mD = Color.FromArgb(96, 52, 150);
            for (int side = -1; side <= 1; side += 2)
            {
                float sx = side;
                PointF[] wing = {
                    new PointF(sx * 0.2f, cy - 0.1f), new PointF(sx * 0.8f, cy - 0.55f - flap * 0.8f), new PointF(sx * 1.7f, cy - 0.3f - flap * 1.0f),
                    new PointF(sx * 1.4f, cy + 0.0f - flap * 0.55f), new PointF(sx * 1.15f, cy + 0.14f - flap * 0.3f), new PointF(sx * 0.85f, cy + 0.0f - flap * 0.2f), new PointF(sx * 0.5f, cy + 0.2f) };
                P.Poly(g, wing, Color.FromArgb(214, 170, 255), mD, Edge(mD), 0.04f);
                P.Line(g, sx * 0.2f, cy - 0.1f, sx * 1.7f, cy - 0.3f - flap * 1.0f, 0.06f, m);
                P.Line(g, sx * 0.3f, cy, sx * 1.4f, cy - flap * 0.55f, 0.045f, m);
            }
            P.Poly(g, new PointF[] { new PointF(-0.34f, cy - 0.34f), new PointF(-0.5f, cy - 0.88f), new PointF(-0.08f, cy - 0.48f) }, m, mD, Edge(mD), 0.035f);
            P.Poly(g, new PointF[] { new PointF(0.34f, cy - 0.34f), new PointF(0.5f, cy - 0.88f), new PointF(0.08f, cy - 0.48f) }, m, mD, Edge(mD), 0.035f);
            Soft(g, 0f, cy + 0.02f, 0.52f, 0.5f, m);
            P.Fill(g, 0f, cy + 0.2f, 0.28f, 0.2f, Color.FromArgb(226, 196, 255));
            GoogleEye(g, -0.2f, cy - 0.1f, 0.2f, 0f, Pupil);
            GoogleEye(g, 0.2f, cy - 0.1f, 0.2f, 0f, Pupil);
            Mouth(g, 0f, cy + 0.22f, 0.12f, 0.08f);
            Tooth(g, -0.09f, cy + 0.16f, 0.035f, 0.08f); Tooth(g, 0.09f, cy + 0.16f, 0.035f, 0.08f);
        }

        // ------------------------------------------------------------------ 해골 병사
        private static void Skeleton(Graphics g, float sn, float cs, bool walk)
        {
            float bob = walk ? -Math.Abs(cs) * 0.06f : 0f;
            Feet(g, 0.22f, -0.72f + bob, sn, cs, 0.22f, 0.6f, Bone);
            Soft(g, 0f, -1.0f + bob, 0.38f, 0.38f, Bone);
            for (int i = 0; i < 3; ++i) P.Line(g, -0.22f, -1.12f + i * 0.1f + bob, 0.22f, -1.12f + i * 0.1f + bob, 0.045f, BoneD);
            Stub(g, -0.36f, -1.1f + bob, -0.6f - sn * 0.05f, -0.78f + bob, 0.18f, Bone);
            float cx = 0.62f + sn * 0.04f;
            Stub(g, 0.36f, -1.1f + bob, cx, -0.82f + bob, 0.18f, Bone);
            Stub(g, cx, -0.82f + bob, cx + 0.1f, -1.7f + bob, 0.06f, Wood);
            P.Poly(g, new PointF[] { new PointF(cx + 0.02f, -1.75f + bob), new PointF(cx + 0.2f, -1.75f + bob), new PointF(cx + 0.11f, -2.55f + bob) }, Steel, SteelD, Edge(SteelD), 0.03f);
            P.Line(g, cx - 0.1f, -1.74f + bob, cx + 0.32f, -1.74f + bob, 0.07f, WoodD);
            Soft(g, 0f, -1.78f + bob, 0.64f, 0.56f, Bone);
            Soft(g, 0f, -1.4f + bob, 0.4f, 0.2f, Col.Dark(Bone, 0.04f));
            GoogleEye(g, -0.25f, -1.86f + bob, 0.22f, 0f, Pupil);
            GoogleEye(g, 0.25f, -1.86f + bob, 0.22f, 0f, Pupil);
            P.Fill(g, 0f, -1.62f + bob, 0.05f, 0.07f, BoneD);
            for (int i = -2; i <= 2; ++i) P.Fill(g, i * 0.09f, -1.38f + bob, 0.04f, 0.07f, Color.White);
            P.Line(g, -0.22f, -1.44f + bob, 0.22f, -1.44f + bob, 0.025f, BoneD);
        }

        // ------------------------------------------------------------------ 거미
        private static void Spider(Graphics g, float sn, float cs)
        {
            Color b = Color.FromArgb(158, 100, 220);
            Color lg = Col.Dark(b, 0.22f);
            float bob = -Math.Abs(cs) * 0.04f;
            for (int pass = 0; pass < 2; ++pass)
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 4; ++i)
                    {
                        if ((i < 2) != (pass == 0)) continue;
                        float ph = ((i % 2) * 3.1416f) + (side > 0 ? 3.1416f : 0f);
                        float wig = (float)Math.Sin(Math.Asin(Math.Max(-1f, Math.Min(1f, sn))) + ph) * 0.16f;
                        float rootX = side * (0.35f + i * 0.04f), rootY = -0.85f + bob;
                        float kneeX = side * (1.05f + i * 0.14f), kneeY = -1.45f + bob + i * 0.1f;
                        float footX = side * (1.35f + i * 0.2f) + wig * side, footY = -0.04f;
                        Stub(g, rootX, rootY, kneeX, kneeY, 0.16f, lg);
                        Stub(g, kneeX, kneeY, footX, footY, 0.14f, lg);
                        Soft(g, footX, footY - 0.02f, 0.11f, 0.07f, Col.Dark(lg, 0.1f));
                    }
            Soft(g, 0f, -0.98f + bob, 0.82f, 0.62f, b);
            P.Ball(g, 0f, -0.72f + bob, 0.5f, 0.2f, Col.Alpha(Color.White, 130), Col.Alpha(b, 0), Color.Transparent, 0f);
            GoogleEye(g, -0.3f, -1.1f + bob, 0.24f, 0f, Pupil);
            GoogleEye(g, 0.3f, -1.1f + bob, 0.24f, 0f, Pupil);
            GoogleEye(g, -0.58f, -0.92f + bob, 0.13f, 0f, Pupil);
            GoogleEye(g, 0.58f, -0.92f + bob, 0.13f, 0f, Pupil);
            Mouth(g, 0f, -0.68f + bob, 0.17f, 0.1f);
            Tooth(g, -0.1f, -0.76f + bob, 0.04f, 0.09f); Tooth(g, 0.1f, -0.76f + bob, 0.04f, 0.09f);
        }

        // ------------------------------------------------------------------ 오크
        private static void Orc(Graphics g, float sn, float cs, bool walk)
        {
            Color sk = Color.FromArgb(142, 190, 110);
            float bob = walk ? -Math.Abs(cs) * 0.08f : 0f;
            Feet(g, 0.4f, -0.82f + bob, sn, cs, 0.4f, 0.9f, Col.Dark(sk, 0.1f));
            Stub(g, -0.72f, -1.7f + bob, -1.0f - sn * 0.06f, -1.0f + bob, 0.38f, sk);
            float ax = 1.0f + sn * 0.05f;
            Stub(g, 0.72f, -1.7f + bob, ax, -1.1f + bob, 0.38f, sk);
            Stub(g, ax, -1.1f + bob, ax + 0.1f, -2.4f + bob, 0.12f, Wood);
            P.Poly(g, new PointF[] { new PointF(ax - 0.02f, -2.3f + bob), new PointF(ax - 0.5f, -2.5f + bob), new PointF(ax - 0.44f, -2.9f + bob), new PointF(ax + 0.12f, -2.7f + bob) }, Steel, SteelD, Edge(SteelD), 0.035f);
            Soft(g, 0f, -1.35f + bob, 0.82f, 0.74f, sk);
            P.Round(g, -0.64f, -0.95f + bob, 1.28f, 0.2f, 0.08f, Wood, WoodD, Edge(WoodD), 0.03f);
            P.Fill(g, 0f, -0.85f + bob, 0.09f, 0.08f, Color.FromArgb(240, 200, 80));
            for (int i = -2; i <= 2; ++i)
                P.Poly(g, new PointF[] { new PointF(i * 0.14f - 0.07f, -2.78f + bob), new PointF(i * 0.14f, -3.1f + bob + Math.Abs(i) * 0.07f), new PointF(i * 0.14f + 0.07f, -2.78f + bob) }, Color.FromArgb(220, 90, 70), Color.FromArgb(150, 50, 40), Edge(Color.FromArgb(150, 50, 40)), 0.025f);
            Soft(g, 0f, -2.32f + bob, 0.66f, 0.56f, sk);
            GoogleEye(g, -0.27f, -2.4f + bob, 0.22f, 0f, Pupil);
            GoogleEye(g, 0.27f, -2.4f + bob, 0.22f, 0f, Pupil);
            Mouth(g, 0f, -2.0f + bob, 0.28f, 0.12f);
            Tooth(g, -0.22f, -2.1f + bob, 0.05f, -0.16f); Tooth(g, 0.22f, -2.1f + bob, 0.05f, -0.16f);
        }

        // ------------------------------------------------------------------ 오우거
        private static void Ogre(Graphics g, float sn, float cs, bool walk)
        {
            Color sk = Color.FromArgb(214, 160, 110);
            float bob = walk ? -Math.Abs(cs) * 0.1f : 0f;
            Feet(g, 0.7f, -1.3f + bob, sn, cs, 0.62f, 1.3f, Col.Dark(sk, 0.1f));
            Stub(g, -1.25f, -3.0f + bob, -1.7f - sn * 0.08f, -1.7f + bob, 0.66f, sk);
            float ax = 1.75f + sn * 0.06f;
            Stub(g, 1.25f, -3.0f + bob, ax, -1.8f + bob, 0.66f, sk);
            Soft(g, ax, -1.5f + bob, 0.46f, 0.4f, Col.Light(sk, 0.05f));
            Stub(g, ax, -1.9f + bob, ax + 0.2f, -4.1f + bob, 0.2f, Wood);
            Soft(g, ax + 0.22f, -4.3f + bob, 0.5f, 0.62f, Wood);
            Soft(g, 0f, -2.45f + bob, 1.5f, 1.3f, sk);
            P.Ball(g, 0f, -2.1f + bob, 1.0f, 0.55f, Col.Alpha(Color.White, 90), Col.Alpha(sk, 0), Color.Transparent, 0f);
            P.Poly(g, new PointF[] { new PointF(-1.35f, -1.85f + bob), new PointF(1.35f, -1.85f + bob), new PointF(1.0f, -1.15f), new PointF(0f, -1.0f), new PointF(-1.0f, -1.15f) }, Wood, WoodD, Edge(WoodD), 0.04f);
            for (int side = -1; side <= 1; side += 2)
                P.Poly(g, new PointF[] { new PointF(side * 0.5f, -4.55f + bob), new PointF(side * 0.95f, -5.35f + bob), new PointF(side * 0.82f, -4.35f + bob) }, Color.FromArgb(250, 244, 220), BoneD, Edge(BoneD), 0.035f);
            Soft(g, 0f, -4.0f + bob, 1.0f, 0.86f, sk);
            GoogleEye(g, -0.4f, -4.12f + bob, 0.34f, 0f, Pupil);
            GoogleEye(g, 0.4f, -4.12f + bob, 0.34f, 0f, Pupil);
            P.Line(g, -0.75f, -4.6f + bob, -0.12f, -4.45f + bob, 0.09f, Col.Dark(sk, 0.4f));
            P.Line(g, 0.75f, -4.6f + bob, 0.12f, -4.45f + bob, 0.09f, Col.Dark(sk, 0.4f));
            P.Fill(g, 0f, -3.72f + bob, 0.1f, 0.08f, Col.Dark(sk, 0.35f));
            Mouth(g, 0f, -3.42f + bob, 0.46f, 0.2f);
            Tooth(g, -0.3f, -3.58f + bob, 0.07f, 0.14f); Tooth(g, 0.3f, -3.58f + bob, 0.07f, 0.14f);
        }
    }
}
