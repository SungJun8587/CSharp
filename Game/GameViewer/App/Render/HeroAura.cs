//***************************************************************************
// HeroAura.cs : 영웅이 스킬을 쓸 때 발밑에 깔리는 연출. 영웅마다 다르다.
//   전사 = 땅이 갈라지고 먼지가 튀며 주황 충격 고리, 궁수 = 뒤로 흐르는 바람 줄기와 잎, 마법사 = 회전하는 룬 서클, 창병 = 앞으로 뻗는 속도선과 푸른 고리.
// 시전 시간이 있는 스킬은 시전 중에 같은 모양이 커지며 차오르고(chargeP), 스킬이 나가는 순간(AttackKind == 1) 퍼져 나가며 사라진다.
// 상태를 따로 두지 않고 엔티티의 시각 값만으로 매번 같은 그림을 계산한다.
//***************************************************************************
using System;
using System.Drawing;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public static class HeroAura
    {
        public const int StrikeMs = 640;

        /// <summary>스킬이 나간 뒤 진행도 0..1 (아니면 -1).</summary>
        public static float StrikeProgress(Entity e, long now)
        {
            if (e.IsMonster || e.Dead || e.AttackKind != 1 || e.AttackAnimMs <= 0) return -1f;
            float t = now - e.AttackAnimMs;
            return t < 0f || t >= StrikeMs ? -1f : t / StrikeMs;
        }

        /// <summary>(bx, by) = 발 밑 화면 좌표. 몸 그림보다 먼저(바닥 위에) 그린다.</summary>
        public static void Draw(IGfx g, Resources res, Entity e, ViewState v, float bx, float by, long now)
        {
            if (e.IsMonster || e.Dead || e.TemplateId < 1 || e.TemplateId > 4) return;
            float k = StrikeProgress(e, now);
            float c = AttackPose.CastProgress(e, now);
            if (k < 0f && c < 0f) return;
            float fl = (float)Math.Sqrt(e.FaceX * e.FaceX + e.FaceY * e.FaceY);
            float fx = fl > 0.001f ? e.FaceX / fl : 1f, fy = fl > 0.001f ? e.FaceY / fl : 0f;
            float u = v.Px(1.0f);
            GfxState gs = g.Save();
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            switch (e.TemplateId)
            {
                case 1: Warrior(g, res, e, bx, by, u, fx, fy, k, c); break;
                case 2: Archer(g, res, e, bx, by, u, fx, fy, k, c); break;
                case 3: Mage(g, res, e, bx, by, u, fx, fy, k, c, now); break;
                default: Lancer(g, res, e, bx, by, u, fx, fy, k, c); break;
            }
            g.Restore(gs);
        }

        private static float Out(float p) { p = p < 0f ? 0f : (p > 1f ? 1f : p); return 1f - (1f - p) * (1f - p); }
        private static int A(float v) { return v < 0f ? 0 : (v > 255f ? 255 : (int)v); }
        private static float H(Entity e, int i) { return Noise.Hash01(i * 13 + (int)(e.AttackAnimMs & 0x7FFF), (int)(e.Id & 0xFF) + i, 0x3Bu); }

        private static void Ring(IGfx g, Resources res, float cx, float cy, float r, float alpha, int rr, int gg, int bb, float w)
        {
            if (alpha <= 1f || r < 1f) return;
            g.DrawEllipse(res.Pen(A(alpha), rr, gg, bb, w), cx - r, cy - r * 0.45f, r * 2f, r * 0.9f);
        }

        // ------------------------------------------------------------------ 전사: 갈라지는 땅
        private static void Warrior(IGfx g, Resources res, Entity e, float bx, float by, float u, float fx, float fy, float k, float c)
        {
            if (c >= 0f) { Ring(g, res, bx, by, u * (2.2f - 1.4f * c), 90 + 120 * c, 255, 170, 60, 2f); return; }
            float p = Out(k), fade = 1f - k;
            Ring(g, res, bx, by, u * (0.7f + 2.4f * p), 220 * fade, 255, 190, 80, 3f);
            Ring(g, res, bx, by, u * (0.4f + 1.6f * p), 150 * fade, 255, 240, 200, 2f);
            float ox = bx + fx * u * 0.9f, oy = by + fy * u * 0.5f;
            for (int i = 0; i < 8; ++i)
            {
                // 앞쪽으로 치우친 부채꼴로 갈라진 금: 꺾이는 선 3마디
                double a = Math.Atan2(fy * 0.6, fx) + (i - 3.5) * 0.42 + (H(e, i) - 0.5) * 0.3;
                float len = u * (1.0f + 1.6f * H(e, i + 9)) * p;
                float x0 = ox, y0 = oy;
                for (int s = 1; s <= 3; ++s)
                {
                    double aa = a + (H(e, i * 3 + s) - 0.5) * 0.6;
                    float x1 = x0 + (float)Math.Cos(aa) * len / 3f, y1 = y0 + (float)Math.Sin(aa) * len / 3f * 0.5f;
                    g.DrawLine(res.Pen(A(230 * fade), 60, 40, 28, 3.2f - s * 0.6f), x0, y0, x1, y1);
                    g.DrawLine(res.Pen(A(200 * fade), 255, 170, 70, 1.4f), x0, y0 - 1, x1, y1 - 1);
                    x0 = x1; y0 = y1;
                }
            }
            for (int i = 0; i < 9; ++i)
            {
                double th = i * 0.7 + H(e, i) * 2.0;
                float d = u * (0.5f + 1.5f * p) * (0.6f + H(e, i + 20));
                float cx = ox + (float)Math.Cos(th) * d, cy = oy + (float)Math.Sin(th) * d * 0.45f - u * 0.7f * p;
                float r = u * 0.22f * (1f + 1.4f * p);
                g.FillEllipse(res.Brush(A(150 * fade * fade), 186, 166, 134), cx - r, cy - r * 0.7f, r * 2f, r * 1.4f);
            }
        }

        // ------------------------------------------------------------------ 궁수: 바람 줄기
        private static void Archer(IGfx g, Resources res, Entity e, float bx, float by, float u, float fx, float fy, float k, float c)
        {
            if (c >= 0f) { Ring(g, res, bx, by, u * (1.9f - 1.0f * c), 80 + 120 * c, 150, 240, 170, 2f); return; }
            float p = Out(k), fade = 1f - k;
            Ring(g, res, bx, by, u * (0.6f + 1.8f * p), 170 * fade, 160, 255, 190, 2f);
            for (int i = 0; i < 9; ++i)
            {
                // 쏜 반대쪽으로 흐르는 줄기: 몸에서 멀어질수록 가늘고 옅다
                float off = (i - 4) * u * 0.16f;
                float y0 = by - u * (0.2f + 1.1f * H(e, i));
                float x0 = bx - fx * u * (0.2f + 0.5f * p) + off * -fy;
                float len = u * (0.9f + 1.4f * H(e, i + 7)) * p;
                float x1 = x0 - fx * len, y1 = y0 - fy * len * 0.5f;
                g.DrawLine(res.Pen(A(190 * fade), 235, 255, 230, 1.6f), x0, y0, x1, y1);
                g.DrawLine(res.Pen(A(110 * fade), 120, 230, 150, 3.2f), x0, y0, x1, y1);
            }
            for (int i = 0; i < 6; ++i)
            {
                float t = p * (0.6f + 0.6f * H(e, i + 30));
                float lx = bx - fx * u * (0.5f + 2.0f * t) + (H(e, i + 40) - 0.5f) * u * 1.4f;
                float ly = by - u * (0.4f + 1.2f * H(e, i + 50)) + (float)Math.Sin(t * 9 + i) * u * 0.25f;
                float r = u * 0.1f;
                g.FillEllipse(res.Brush(A(210 * fade), 120, 210, 120), lx - r * 1.6f, ly - r, r * 3.2f, r * 2f);
            }
        }

        // ------------------------------------------------------------------ 마법사: 룬 서클
        private static void Mage(IGfx g, Resources res, Entity e, float bx, float by, float u, float fx, float fy, float k, float c, long now)
        {
            float grow, al;
            if (c >= 0f) { grow = 0.5f + 0.9f * Out(c); al = 70f + 150f * c; }
            else { grow = 1.4f + 0.6f * Out(k); al = 230f * (1f - k) * (1f - k * 0.5f); }
            float spin = now / 520f;
            float r = u * 1.35f * grow;
            Ring(g, res, bx, by, r, al, 190, 150, 255, 2.4f);
            Ring(g, res, bx, by, r * 0.8f, al * 0.8f, 235, 225, 255, 1.4f);
            Ring(g, res, bx, by, r * 1.1f, al * 0.45f, 160, 130, 255, 4f);
            for (int i = 0; i < 12; ++i)         // 가장자리 룬 눈금: 길고 짧은 칸이 번갈아 있다
            {
                double a = spin + i * Math.PI / 6;
                float r0 = r * 0.8f, r1 = r * (i % 2 == 0 ? 1.0f : 0.92f);
                g.DrawLine(res.Pen(A(al), 230, 215, 255, 1.6f), bx + (float)Math.Cos(a) * r0, by + (float)Math.Sin(a) * r0 * 0.45f, bx + (float)Math.Cos(a) * r1, by + (float)Math.Sin(a) * r1 * 0.45f);
            }
            // 가운데 육망성: 삼각형 둘이 반대로 돈다
            for (int t = 0; t < 2; ++t)
            {
                PointF[] tri = new PointF[3];
                for (int i = 0; i < 3; ++i)
                {
                    double a = (t == 0 ? spin : -spin * 0.8) + i * 2.0944 + t * 3.1416;
                    tri[i] = new PointF(bx + (float)Math.Cos(a) * r * 0.62f, by + (float)Math.Sin(a) * r * 0.62f * 0.45f);
                }
                g.DrawPolygon(res.Pen(A(al * 0.9f), 205, 180, 255, 1.4f), tri);
            }
            if (c < 0f)
                for (int i = 0; i < 7; ++i)
                {
                    float t = Out(k); double a = i * 0.9 + spin;
                    float d = r * (0.3f + 0.8f * t), py = by - u * (0.2f + 1.8f * t * H(e, i)) + (float)Math.Sin(a) * 2f;
                    float rr = u * 0.08f;
                    g.FillEllipse(res.Brush(A(230 * (1f - k)), 240, 225, 255), bx + (float)Math.Cos(a) * d - rr, py - rr, rr * 2f, rr * 2f);
                }
        }

        // ------------------------------------------------------------------ 창병: 속도선
        private static void Lancer(IGfx g, Resources res, Entity e, float bx, float by, float u, float fx, float fy, float k, float c)
        {
            if (c >= 0f) { Ring(g, res, bx, by, u * (1.8f - 1.0f * c), 80 + 120 * c, 120, 200, 255, 2f); return; }
            float p = Out(k), fade = 1f - k;
            Ring(g, res, bx + fx * u * 0.8f * p, by + fy * u * 0.4f * p, u * (0.5f + 1.5f * p), 190 * fade, 130, 210, 255, 2.4f);
            for (int i = 0; i < 8; ++i)
            {
                float off = (i - 3.5f) * u * 0.2f;
                float x0 = bx - fx * u * 0.4f + off * -fy, y0 = by - u * (0.2f + 1.3f * H(e, i)) + off * fx * 0.3f;
                float len = u * (1.6f + 1.8f * H(e, i + 5)) * p;
                g.DrawLine(res.Pen(A(210 * fade), 235, 248, 255, 1.6f), x0 - fx * len * 0.2f, y0 - fy * len * 0.1f, x0 + fx * len, y0 + fy * len * 0.5f);
                g.DrawLine(res.Pen(A(120 * fade), 110, 190, 255, 3.4f), x0, y0, x0 + fx * len * 0.8f, y0 + fy * len * 0.4f);
            }
        }
    }
}
