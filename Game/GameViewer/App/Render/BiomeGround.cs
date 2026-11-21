//***************************************************************************
// BiomeGround.cs : 설원 / 화산 / 늪의 바닥. 바탕 색(저해상도 한 겹)과 흩어진 장식(눈 뭉치, 현무암, 갈대 …).
// 숲/사막은 GroundRenderer 안에 있다. 여기 함수는 모두 월드 좌표의 함수라 조각으로 나눠 그려도 이음매가 없다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public static partial class BiomeGround
    {
        private static float Smooth(float a, float b, float x) { return Mx.Smooth(a, b, x); }
        private static void Mix(ref float r, ref float g, ref float b, Color c, float t) { Mx.MixInto(ref r, ref g, ref b, c.R, c.G, c.B, t); }
        private static float Rnd(uint h, int shift) { return ((h >> shift) & 255) / 255f; }
        private static float Fbm(float x, float y, float f, uint seed) { return Noise.Value(x * f, y * f, seed) * 0.6f + Noise.Value(x * f * 3.1f, y * f * 3.1f, seed + 2) * 0.4f; }

        public static void BaseAt(MapKind kind, float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            switch (kind)
            {
                case MapKind.Snow: SnowColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
                case MapKind.Volcano: VolcanoColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
                case MapKind.Ruins: RuinsColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
                case MapKind.Coast: CoastColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
                case MapKind.Crystal: CrystalColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
                case MapKind.Sky: SkyColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
                case MapKind.Dark: DarkColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
                default: SwampColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); break;
            }
        }

        private static void EdgeOut(float wx, float wy, float minX, float minY, float maxX, float maxY, out float outside, out float inEdge)
        {
            float ex = Math.Max(Math.Max(minX - wx, wx - maxX), 0f), ey = Math.Max(Math.Max(minY - wy, wy - maxY), 0f);
            outside = Math.Max(ex, ey);
            inEdge = Math.Min(Math.Min(wx - minX, maxX - wx), Math.Min(wy - minY, maxY - wy));
            inEdge += (Noise.Value(wx * 0.18f, wy * 0.18f, 83) - 0.5f) * 5f;
        }

        // ------------------------------------------------------------------ 설원
        private static readonly Color SnowLit = System.Drawing.Color.FromArgb(244, 248, 255), SnowShade = System.Drawing.Color.FromArgb(188, 208, 234), SnowDeep = System.Drawing.Color.FromArgb(150, 176, 214);
        private static readonly Color IceTint = System.Drawing.Color.FromArgb(150, 214, 238), IceDeep = System.Drawing.Color.FromArgb(104, 168, 214), FrozenMud = System.Drawing.Color.FromArgb(132, 132, 146);
        private static readonly Color SnowCliff = System.Drawing.Color.FromArgb(78, 96, 126);

        private static void SnowColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside >= 0.8f) { r = SnowCliff.R; g = SnowCliff.G; b = SnowCliff.B; return; }
            float n = Smooth(0.2f, 0.8f, Fbm(wx, wy, 0.02f, 201));
            r = SnowShade.R + (SnowLit.R - SnowShade.R) * n; g = SnowShade.G + (SnowLit.G - SnowShade.G) * n; b = SnowShade.B + (SnowLit.B - SnowShade.B) * n;
            // 바람이 쌓은 눈더미: 부드러운 띠. 마루는 하얗고 그늘 쪽은 푸르다.
            float d = wx * 0.021f - wy * 0.026f + Noise.Value(wx * 0.02f, wy * 0.02f, 211) * 4.5f + Noise.Value(wx * 0.07f, wy * 0.07f, 213) * 0.7f;
            float s = (float)Math.Sin(d * 6.2832f);
            float crest = Smooth(0.7f, 1f, s); if (crest > 0) Mix(ref r, ref g, ref b, System.Drawing.Color.White, crest * 0.5f);
            float lee = Smooth(0.55f, 1f, -s); if (lee > 0) Mix(ref r, ref g, ref b, SnowDeep, lee * 0.4f);
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.045f + 1f; r *= grain; g *= grain; b *= grain;
            // 얼어붙은 웅덩이 자국(푸르고 매끈한 얼음판)
            float ice = Smooth(0.66f, 0.76f, Fbm(wx + 90f, wy + 40f, 0.03f, 221));
            if (ice > 0) { Mix(ref r, ref g, ref b, IceTint, ice * 0.78f); float deepI = Smooth(0.74f, 0.84f, Fbm(wx + 90f, wy + 40f, 0.03f, 221)); if (deepI > 0) Mix(ref r, ref g, ref b, IceDeep, deepI * 0.5f); dirtT = ice * 0.7f; }
            // 눈이 쓸려 나가 드러난 얼어붙은 땅
            float mud = Smooth(0.7f, 0.8f, Fbm(wx + 33f, wy + 11f, 0.04f, 231));
            if (mud > 0) { Mix(ref r, ref g, ref b, FrozenMud, mud * 0.55f); dirtT = Math.Max(dirtT, mud); }
            // 구름 그림자
            float haze = Smooth(0.55f, 0.8f, Noise.Value(wx * 0.012f + 5f, wy * 0.012f + 77f, 97));
            if (haze > 0) Mix(ref r, ref g, ref b, SnowDeep, haze * 0.18f);
            if (inEdge < 6f) Mix(ref r, ref g, ref b, SnowDeep, (1f - Math.Max(inEdge, 0f) / 6f) * 0.5f);
            if (outside > 0) Mix(ref r, ref g, ref b, SnowCliff, outside / 0.8f);
        }

        // ------------------------------------------------------------------ 화산
        private static readonly Color Basalt = System.Drawing.Color.FromArgb(44, 38, 42), Ash = System.Drawing.Color.FromArgb(92, 80, 78), Scorch = System.Drawing.Color.FromArgb(74, 40, 30);
        private static readonly Color LavaCore = System.Drawing.Color.FromArgb(255, 214, 96), LavaMid = System.Drawing.Color.FromArgb(255, 112, 28), LavaDeep = System.Drawing.Color.FromArgb(176, 40, 16);
        private static readonly Color VolCliff = System.Drawing.Color.FromArgb(52, 22, 20);

        private static void VolcanoColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside >= 0.8f) { r = VolCliff.R; g = VolCliff.G; b = VolCliff.B; return; }
            float n = Smooth(0.15f, 0.85f, Fbm(wx, wy, 0.028f, 241));
            r = Basalt.R + (Ash.R - Basalt.R) * n; g = Basalt.G + (Ash.G - Basalt.G) * n; b = Basalt.B + (Ash.B - Basalt.B) * n;
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.16f + 1f; r *= grain; g *= grain; b *= grain;
            // 그을린 붉은 땅
            float sc = Smooth(0.55f, 0.8f, Fbm(wx + 20f, wy + 60f, 0.035f, 251));
            if (sc > 0) Mix(ref r, ref g, ref b, Scorch, sc * 0.6f);
            // 식은 용암이 만든 갈라진 틈: 능선 잡음의 마루를 가는 선으로 쓴다. 틈 속은 아직 뜨겁다.
            float f1 = 1f - Math.Abs(Fbm(wx, wy, 0.012f, 261) * 2f - 1f);
            float f2 = 1f - Math.Abs(Fbm(wx + 300f, wy - 200f, 0.02f, 263) * 2f - 1f);
            float c1 = Smooth(0.972f, 0.9995f, f1), c2 = Smooth(0.976f, 0.9995f, f2) * 0.85f;
            float crack = Math.Max(c1 * c1 * c1, c2 * c2 * c2);
            if (crack > 0)
            {
                float core = Smooth(0.5f, 1f, crack);
                Mix(ref r, ref g, ref b, LavaDeep, crack * 0.9f);
                Mix(ref r, ref g, ref b, LavaMid, core * 0.9f);
                Mix(ref r, ref g, ref b, LavaCore, Smooth(0.85f, 1f, crack) * 0.8f);
                dirtT = crack;
            }
            // 틈 둘레의 붉은 열기
            float glow = Smooth(0.982f, 0.996f, Math.Max(f1, f2 * 0.99f)) * (1f - crack);
            if (glow > 0) Mix(ref r, ref g, ref b, LavaDeep, glow * 0.28f);
            if (inEdge < 7f) Mix(ref r, ref g, ref b, Basalt, (1f - Math.Max(inEdge, 0f) / 7f) * 0.6f);
            if (outside > 0) Mix(ref r, ref g, ref b, VolCliff, outside / 0.8f);
        }

        // ------------------------------------------------------------------ 늪
        private static readonly Color BogLit = System.Drawing.Color.FromArgb(104, 118, 62), BogDeep = System.Drawing.Color.FromArgb(44, 60, 38), Mud = System.Drawing.Color.FromArgb(88, 72, 46);
        private static readonly Color Algae = System.Drawing.Color.FromArgb(142, 160, 64), Murk = System.Drawing.Color.FromArgb(30, 62, 58), BogOut = System.Drawing.Color.FromArgb(16, 26, 20);

        private static void SwampColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside >= 0.8f) { r = BogOut.R; g = BogOut.G; b = BogOut.B; return; }
            float n = Smooth(0.15f, 0.85f, Fbm(wx, wy, 0.024f, 271));
            r = BogDeep.R + (BogLit.R - BogDeep.R) * n; g = BogDeep.G + (BogLit.G - BogDeep.G) * n; b = BogDeep.B + (BogLit.B - BogDeep.B) * n;
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.12f + 1f; r *= grain; g *= grain; b *= grain;
            float mud = Smooth(0.58f, 0.76f, Fbm(wx + 14f, wy + 70f, 0.03f, 281));
            if (mud > 0) Mix(ref r, ref g, ref b, Mud, mud * 0.6f);
            float alg = Smooth(0.62f, 0.8f, Fbm(wx + 55f, wy + 5f, 0.05f, 291));
            if (alg > 0) Mix(ref r, ref g, ref b, Algae, alg * 0.4f);
            // 고인 검은 물: 얕은 웅덩이 자국(통행에는 영향 없음)
            float pud = Smooth(0.7f, 0.78f, Fbm(wx + 120f, wy + 10f, 0.045f, 301));
            if (pud > 0)
            {
                Mix(ref r, ref g, ref b, Murk, pud * 0.85f);
                float sheen = Smooth(0.5f, 1f, pud) * (0.5f + 0.5f * Noise.Value(wx * 0.4f, wy * 0.4f, 303));
                Mix(ref r, ref g, ref b, System.Drawing.Color.FromArgb(120, 150, 130), sheen * 0.14f);
                dirtT = pud;
            }
            float shade = Smooth(0.55f, 0.8f, Fbm(wx + 100f, wy + 20f, 0.03f, 311)) * 0.3f;
            if (shade > 0) Mix(ref r, ref g, ref b, BogDeep, shade);
            if (inEdge < 6f) Mix(ref r, ref g, ref b, BogOut, (1f - Math.Max(inEdge, 0f) / 6f) * 0.5f);
            if (outside > 0) Mix(ref r, ref g, ref b, BogOut, outside / 0.8f);
        }

        // ------------------------------------------------------------------ 장식
        private const float Cell = 1.5f;

        public static void Details(MapKind kind, IGfx g, ViewState v, int y0, int y1, float minX, float minY, float maxX, float maxY)
        {
            float scale = (float)v.Scale;
            if (scale < 5) return;
            float wx0 = v.ToWorldX(-80), wy0 = v.ToWorldY(y0 - 100), wx1 = v.ToWorldX(v.Width + 80), wy1 = v.ToWorldY(y1 + 100);
            int cx0 = (int)Math.Floor(wx0 / Cell), cx1 = (int)Math.Ceiling(wx1 / Cell);
            int cy0 = (int)Math.Floor(wy0 / Cell), cy1 = (int)Math.Ceiling(wy1 / Cell);
            for (int cy = cy0; cy <= cy1; ++cy)
                for (int cx = cx0; cx <= cx1; ++cx)
                {
                    uint h = Noise.Hash(cx, cy, 378u + (uint)kind);
                    float wx = (cx + ((h >> 8) & 255) / 255f) * Cell, wy = (cy + ((h >> 16) & 255) / 255f) * Cell;
                    if (wx < minX + 0.5f || wy < minY + 0.5f || wx > maxX - 0.5f || wy > maxY - 0.5f) continue;
                    float sx = v.ToScreenX(wx), sy = v.ToScreenY(wy);
                    float r0, g0, b0, dirtT;
                    BaseAt(kind, wx, wy, minX, minY, maxX, maxY, out r0, out g0, out b0, out dirtT);
                    uint k = (h >> 24) % 100;
                    if (kind == MapKind.Snow) SnowDetail(g, sx, sy, scale, h, k, dirtT);
                    else if (kind == MapKind.Volcano) VolcanoDetail(g, sx, sy, scale, h, k, dirtT);
                    else if (kind == MapKind.Swamp) SwampDetail(g, sx, sy, scale, h, k, dirtT);
                    else NewDetail(kind, g, sx, sy, scale, h, k, dirtT);
                }
        }

        private static void Sh(IGfx g, float x, float y, float rx, float ry, int a, int cr, int cg, int cb)
        {
            using (SolidBrush br = new SolidBrush(System.Drawing.Color.FromArgb(a, cr, cg, cb))) g.FillEllipse(br, x - rx, y - ry, rx * 2, ry * 2);
        }

        // ---- 설원
        private static void SnowDetail(IGfx g, float x, float y, float scale, uint h, uint k, float dirtT)
        {
            if (k < 8) { if (dirtT < 0.5f) SnowTuft(g, x, y, scale, h); }
            else if (k < 13) IceRock(g, x, y, scale, h);
            else if (k < 18) { if (dirtT < 0.5f) Mound(g, x, y, scale, h); }
            else if (k < 21) { if (scale >= 7) IceShards(g, x, y, scale, h); }
            else if (k < 23) { if (scale >= 8 && dirtT < 0.4f) Footprints(g, x, y, scale, h); }
            else if (k < 33) Sparkle(g, x, y, scale, h);
            else if (k < 40) { if (dirtT > 0.3f) FrostCracks(g, x, y, scale, h); }
            else if (k < 42) { if (scale >= 8) Twig(g, x, y, scale, h); }
        }

        private static void SnowTuft(IGfx g, float x, float y, float scale, uint h)
        {
            float len = scale * (0.3f + Rnd(h, 2) * 0.25f);
            Sh(g, x, y + len * 0.05f, len * 0.5f, len * 0.13f, 40, 80, 100, 150);
            int n = 5 + (int)(h % 4);
            for (int i = 0; i < n; ++i)
            {
                float a = -(float)Math.PI * (0.15f + 0.7f * i / (n - 1)) + (Rnd(h, 5 + i) - 0.5f) * 0.2f;
                float l = len * (0.7f + Rnd(h, 9 + i) * 0.5f);
                PointF[] pts = { new PointF(x, y), new PointF(x + (float)Math.Cos(a) * l * 0.5f, y + (float)Math.Sin(a) * l * 0.55f), new PointF(x + (float)Math.Cos(a) * l, y + (float)Math.Sin(a) * l * 0.7f + l * 0.14f) };
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(210, 120, 108, 80), Math.Max(1f, scale * 0.04f))) g.DrawCurve(p, pts, 0.5f);
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, 196, 184, 140), Math.Max(1f, scale * 0.02f))) g.DrawCurve(p, pts, 0.5f);
            }
            Sh(g, x, y - len * 0.05f, len * 0.5f, len * 0.16f, 235, 248, 252, 255);          // 풀뿌리 둘레에 쌓인 눈
            Sh(g, x - len * 0.1f, y - len * 0.09f, len * 0.3f, len * 0.07f, 255, 255, 255, 255);
        }

        private static void IceRock(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 1 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.6f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.3f;
                float s = scale * (0.13f + Rnd(h, 7 + i * 2) * 0.13f);
                Sh(g, px + s * 0.35f, py + s * 0.35f, s * 1.3f, s * 0.45f, 70, 60, 84, 120);
                PointF[] pts = { new PointF(px - s, py), new PointF(px - s * 0.7f, py - s * 0.8f), new PointF(px + s * 0.2f, py - s * 1.0f), new PointF(px + s * 0.95f, py - s * 0.4f), new PointF(px + s, py) };
                int sh = (int)(Rnd(h, 11 + i) * 24) - 8;
                P.Poly(g, pts, System.Drawing.Color.FromArgb(Col.Cl(132 + sh), Col.Cl(146 + sh), Col.Cl(170 + sh)), System.Drawing.Color.FromArgb(Col.Cl(84 + sh), Col.Cl(96 + sh), Col.Cl(124 + sh)), System.Drawing.Color.FromArgb(150, 40, 54, 80), Math.Max(1f, s * 0.12f));
                PointF[] cap = { new PointF(px - s * 0.82f, py - s * 0.45f), new PointF(px - s * 0.55f, py - s * 0.88f), new PointF(px + s * 0.2f, py - s * 1.02f), new PointF(px + s * 0.8f, py - s * 0.5f), new PointF(px + s * 0.3f, py - s * 0.6f), new PointF(px - s * 0.2f, py - s * 0.46f) };
                using (SolidBrush br = new SolidBrush(System.Drawing.Color.FromArgb(240, 246, 252, 255))) g.FillPolygon(br, cap);
            }
        }

        private static void Mound(IGfx g, float x, float y, float scale, uint h)
        {
            float rx = scale * (0.55f + Rnd(h, 2) * 0.5f), ry = rx * 0.32f;
            Sh(g, x + rx * 0.12f, y + ry * 0.3f, rx, ry, 60, 112, 140, 190);
            Sh(g, x, y, rx * 0.95f, ry * 0.9f, 255, 238, 244, 252);
            Sh(g, x - rx * 0.18f, y - ry * 0.22f, rx * 0.6f, ry * 0.5f, 200, 255, 255, 255);
        }

        private static void IceShards(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 2 + (int)(h % 3);
            Sh(g, x, y + scale * 0.02f, scale * 0.3f, scale * 0.08f, 50, 60, 90, 140);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (i - (n - 1) * 0.5f) * scale * 0.16f, hh = scale * (0.28f + Rnd(h, 4 + i * 2) * 0.28f), w = scale * 0.07f;
                float lean = (Rnd(h, 8 + i) - 0.5f) * scale * 0.12f;
                PointF[] pts = { new PointF(px - w, y), new PointF(px - w * 0.5f + lean * 0.4f, y - hh * 0.7f), new PointF(px + lean, y - hh), new PointF(px + w * 0.6f + lean * 0.4f, y - hh * 0.65f), new PointF(px + w, y) };
                P.Poly(g, pts, System.Drawing.Color.FromArgb(214, 244, 255), System.Drawing.Color.FromArgb(96, 168, 220), System.Drawing.Color.FromArgb(170, 40, 100, 150), Math.Max(1f, w * 0.18f));
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(200, 255, 255, 255), Math.Max(1f, w * 0.14f))) g.DrawLine(p, px - w * 0.25f, y - hh * 0.15f, px - w * 0.1f + lean * 0.7f, y - hh * 0.8f);
            }
        }

        private static void Footprints(IGfx g, float x, float y, float scale, uint h)
        {
            float a = Rnd(h, 3) * 6.28f; int n = 4;
            for (int i = 0; i < n; ++i)
            {
                float dx = (float)Math.Cos(a) * scale * 0.5f * i, dy = (float)Math.Sin(a) * scale * 0.5f * i * 0.6f;
                float side = (i % 2 == 0 ? 1 : -1) * scale * 0.09f;
                float px = x + dx - (float)Math.Sin(a) * side, py = y + dy + (float)Math.Cos(a) * side * 0.6f;
                Sh(g, px, py, scale * 0.09f, scale * 0.055f, 70, 120, 146, 190);
                Sh(g, px - 1, py - 1, scale * 0.07f, scale * 0.04f, 120, 255, 255, 255);
            }
        }

        private static void Sparkle(IGfx g, float x, float y, float scale, uint h)
        {
            float s = scale * (0.05f + Rnd(h, 3) * 0.05f);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(150 + (int)(Rnd(h, 6) * 100), 255, 255, 255), 1f))
            { g.DrawLine(p, x - s, y, x + s, y); g.DrawLine(p, x, y - s, x, y + s); }
        }

        private static void FrostCracks(IGfx g, float x, float y, float scale, uint h)
        {
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(130, 255, 255, 255), 1f))
            {
                float a = Rnd(h, 3) * 6.28f, px = x, py = y;
                for (int i = 0; i < 4; ++i)
                {
                    a += (Rnd(h, 5 + i * 2) - 0.5f) * 1.3f;
                    float nx = px + (float)Math.Cos(a) * scale * 0.35f, ny = py + (float)Math.Sin(a) * scale * 0.22f;
                    g.DrawLine(p, px, py, nx, ny); px = nx; py = ny;
                }
            }
        }

        private static void Twig(IGfx g, float x, float y, float scale, uint h)
        {
            float a = (Rnd(h, 3) - 0.5f) * 1.2f, l = scale * 0.5f;
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(220, 84, 66, 50), Math.Max(1.4f, scale * 0.04f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            { g.DrawLine(p, x - (float)Math.Cos(a) * l, y - (float)Math.Sin(a) * l * 0.5f, x + (float)Math.Cos(a) * l, y + (float)Math.Sin(a) * l * 0.5f); }
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(230, 250, 252, 255), Math.Max(1f, scale * 0.025f)))
            { g.DrawLine(p, x - (float)Math.Cos(a) * l * 0.8f, y - (float)Math.Sin(a) * l * 0.4f - 1, x + (float)Math.Cos(a) * l * 0.8f, y + (float)Math.Sin(a) * l * 0.4f - 1); }
        }

        // ---- 화산
        private static void VolcanoDetail(IGfx g, float x, float y, float scale, uint h, uint k, float dirtT)
        {
            if (dirtT > 0.5f) { if (k < 20) EmberSpot(g, x, y, scale, h); return; }
            if (k < 9) BasaltRock(g, x, y, scale, h);
            else if (k < 14) AshPile(g, x, y, scale, h);
            else if (k < 18) { if (scale >= 7) ObsidianShard(g, x, y, scale, h); }
            else if (k < 26) EmberSpot(g, x, y, scale, h);
            else if (k < 30) { if (scale >= 8) Scorched(g, x, y, scale, h); }
            else if (k < 40) Pebbles(g, x, y, scale, h);
            else if (k < 42) { if (scale >= 8 && (h & 3) == 0) Bones(g, x, y, scale, h); }
        }

        private static void BasaltRock(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 1 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.6f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.3f;
                float s = scale * (0.14f + Rnd(h, 7 + i * 2) * 0.15f);
                Sh(g, px + s * 0.3f, py + s * 0.35f, s * 1.3f, s * 0.5f, 90, 0, 0, 0);
                Sh(g, px, py + s * 0.05f, s * 1.25f, s * 0.5f, 34, 255, 90, 20);            // 바닥에 비친 붉은 빛
                PointF[] pts = { new PointF(px - s, py), new PointF(px - s * 0.75f, py - s * 0.8f), new PointF(px + s * 0.15f, py - s * 1.05f), new PointF(px + s * 0.95f, py - s * 0.45f), new PointF(px + s, py) };
                int sh = (int)(Rnd(h, 11 + i) * 18) - 6;
                P.Poly(g, pts, System.Drawing.Color.FromArgb(Col.Cl(82 + sh), Col.Cl(72 + sh), Col.Cl(78 + sh)), System.Drawing.Color.FromArgb(Col.Cl(30 + sh), Col.Cl(26 + sh), Col.Cl(30 + sh)), System.Drawing.Color.FromArgb(200, 12, 8, 10), Math.Max(1f, s * 0.12f));
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, 255, 110, 40), Math.Max(1f, s * 0.07f))) g.DrawLine(p, px - s * 0.5f, py - s * 0.05f, px + s * 0.55f, py - s * 0.08f);
            }
        }

        private static void AshPile(IGfx g, float x, float y, float scale, uint h)
        {
            float rx = scale * (0.4f + Rnd(h, 2) * 0.35f), ry = rx * 0.3f;
            Sh(g, x, y, rx, ry, 150, 120, 108, 104);
            Sh(g, x - rx * 0.1f, y - ry * 0.2f, rx * 0.7f, ry * 0.6f, 150, 160, 148, 142);
        }

        private static void ObsidianShard(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 2 + (int)(h % 2);
            Sh(g, x, y + 1, scale * 0.3f, scale * 0.08f, 90, 0, 0, 0);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (i - (n - 1) * 0.5f) * scale * 0.2f, hh = scale * (0.3f + Rnd(h, 4 + i * 2) * 0.3f), w = scale * 0.08f, lean = (Rnd(h, 8 + i) - 0.5f) * scale * 0.14f;
                PointF[] pts = { new PointF(px - w, y), new PointF(px - w * 0.4f + lean * 0.4f, y - hh * 0.7f), new PointF(px + lean, y - hh), new PointF(px + w * 0.5f + lean * 0.4f, y - hh * 0.6f), new PointF(px + w, y) };
                P.Poly(g, pts, System.Drawing.Color.FromArgb(78, 66, 92), System.Drawing.Color.FromArgb(14, 10, 20), System.Drawing.Color.FromArgb(220, 6, 4, 10), Math.Max(1f, w * 0.2f));
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(190, 200, 170, 255), Math.Max(1f, w * 0.14f))) g.DrawLine(p, px - w * 0.3f, y - hh * 0.2f, px - w * 0.1f + lean * 0.6f, y - hh * 0.8f);
            }
        }

        private static void EmberSpot(IGfx g, float x, float y, float scale, uint h)
        {
            float s = scale * (0.07f + Rnd(h, 3) * 0.07f);
            Sh(g, x, y, s * 3.2f, s * 1.8f, 28, 255, 100, 30);
            Sh(g, x, y, s * 1.5f, s * 0.9f, 110, 255, 130, 36);
            Sh(g, x, y, s * 0.7f, s * 0.42f, 230, 255, 220, 120);
        }

        private static void Scorched(IGfx g, float x, float y, float scale, uint h)
        {
            Sh(g, x, y, scale * 0.6f, scale * 0.2f, 90, 20, 12, 12);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(220, 28, 22, 24), Math.Max(2f, scale * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float a = (Rnd(h, 3) - 0.5f) * 0.9f, l = scale * 0.45f;
                g.DrawLine(p, x - (float)Math.Cos(a) * l, y - (float)Math.Sin(a) * l * 0.5f, x + (float)Math.Cos(a) * l, y + (float)Math.Sin(a) * l * 0.5f);
                g.DrawLine(p, x, y, x + l * 0.3f, y - l * 0.4f);
            }
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, 255, 120, 40), Math.Max(1f, scale * 0.02f))) g.DrawLine(p, x - scale * 0.2f, y, x + scale * 0.25f, y + 1);
        }

        private static void Pebbles(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 2 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.7f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.4f, s = scale * (0.05f + Rnd(h, 7 + i * 2) * 0.05f);
                Sh(g, px + s * 0.2f, py + s * 0.35f, s * 1.1f, s * 0.5f, 90, 0, 0, 0);
                int gr = 52 + (int)(Rnd(h, 9 + i) * 26);
                Sh(g, px, py, s, s * 0.7f, 255, gr, gr - 4, gr + 2);
                Sh(g, px - s * 0.3f, py - s * 0.3f, s * 0.4f, s * 0.2f, 100, 200, 190, 200);
            }
        }

        private static void Bones(IGfx g, float x, float y, float scale, uint h)
        {
            float s = scale * 0.45f;
            GfxState st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform((Rnd(h, 3) - 0.5f) * 60f);
            Color bone = System.Drawing.Color.FromArgb(196, 186, 168), edge = System.Drawing.Color.FromArgb(160, 40, 30, 28);
            using (Pen p = new Pen(edge, Math.Max(1.6f, s * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, -s * 0.5f, 0, s * 0.5f, 0);
            using (Pen p = new Pen(bone, Math.Max(1f, s * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, -s * 0.5f, 0, s * 0.5f, 0);
            for (int i = -2; i <= 2; ++i)
                using (Pen p = new Pen(bone, Math.Max(1f, s * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, i * s * 0.2f - s * 0.12f, -s * 0.3f, s * 0.24f, s * 0.6f, 270, 180);
            g.Restore(st);
        }

        // ---- 늪
        private static void SwampDetail(IGfx g, float x, float y, float scale, uint h, uint k, float dirtT)
        {
            if (dirtT > 0.5f) { if (k < 22) PuddleRing(g, x, y, scale, h); else if (k < 28 && scale >= 6) Duckweed(g, x, y, scale, h); return; }
            if (k < 12) ReedTuft(g, x, y, scale, h);
            else if (k < 18) Hummock(g, x, y, scale, h);
            else if (k < 22) { if (scale >= 8) BogMushroom(g, x, y, scale, h); }
            else if (k < 26) { if (scale >= 8 && (h & 3) == 0) RotLog(g, x, y, scale, h); }
            else if (k < 34) MudBlot(g, x, y, scale, h);
            else if (k < 40) Duckweed(g, x, y, scale, h);
            else if (k < 44) { if (scale >= 6) BogFlower(g, x, y, scale, h); }
        }

        private static void ReedTuft(IGfx g, float x, float y, float scale, uint h)
        {
            float len = scale * (0.5f + Rnd(h, 2) * 0.4f);
            Sh(g, x, y + len * 0.04f, len * 0.4f, len * 0.1f, 70, 10, 24, 14);
            int n = 5 + (int)(h % 4);
            for (int i = 0; i < n; ++i)
            {
                float a = -(float)Math.PI * (0.25f + 0.5f * i / (n - 1)) + (Rnd(h, 5 + i) - 0.5f) * 0.2f;
                float l = len * (0.6f + Rnd(h, 9 + i) * 0.6f);
                PointF[] pts = { new PointF(x + (i - n * 0.5f) * 1.2f, y), new PointF(x + (float)Math.Cos(a) * l * 0.5f, y + (float)Math.Sin(a) * l * 0.9f), new PointF(x + (float)Math.Cos(a) * l * 0.9f + (Rnd(h, 13 + i) - 0.5f) * l * 0.3f, y + (float)Math.Sin(a) * l * 1.1f) };
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(220, 52, 84, 40), Math.Max(1.2f, scale * 0.045f))) g.DrawCurve(p, pts, 0.5f);
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, 150, 176, 84), Math.Max(1f, scale * 0.02f))) g.DrawCurve(p, pts, 0.5f);
                if ((h >> i & 3) == 0) { PointF e = pts[2]; using (SolidBrush br = new SolidBrush(System.Drawing.Color.FromArgb(240, 92, 60, 36))) g.FillRectangle(br, e.X - scale * 0.03f, e.Y - scale * 0.1f, scale * 0.06f, scale * 0.16f); }
            }
        }

        private static void Hummock(IGfx g, float x, float y, float scale, uint h)
        {
            float rx = scale * (0.4f + Rnd(h, 2) * 0.35f), ry = rx * 0.42f;
            Sh(g, x + rx * 0.1f, y + ry * 0.3f, rx, ry, 70, 8, 20, 12);
            Sh(g, x, y, rx, ry, 255, 52, 82, 36);
            Sh(g, x - rx * 0.1f, y - ry * 0.25f, rx * 0.75f, ry * 0.65f, 255, 88, 126, 52);
            Sh(g, x - rx * 0.25f, y - ry * 0.4f, rx * 0.35f, ry * 0.25f, 180, 150, 184, 92);
        }

        private static void BogMushroom(IGfx g, float x, float y, float scale, uint h)
        {
            bool glow = (h & 1) == 0;
            int n = 1 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float mx = x + (i - (n - 1) * 0.5f) * scale * 0.3f, my = y + (Rnd(h, 11 + i) - 0.5f) * scale * 0.12f, s = scale * (0.14f + Rnd(h, 6 + i) * 0.1f);
                if (glow) Sh(g, mx, my - s, s * 2f, s * 1.2f, 30, 120, 255, 190);
                using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(214, 220, 190))) g.FillRectangle(b, mx - s * 0.18f, my - s * 0.9f, s * 0.36f, s * 0.9f);
                using (SolidBrush b = new SolidBrush(glow ? System.Drawing.Color.FromArgb(90, 214, 170) : System.Drawing.Color.FromArgb(188, 120, 60))) g.FillPie(b, mx - s, my - s * 1.8f, s * 2f, s * 1.8f, 180, 180);
                using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(150, 255, 255, 220))) g.FillEllipse(b, mx - s * 0.5f, my - s * 1.5f, s * 0.4f, s * 0.22f);
            }
        }

        private static void RotLog(IGfx g, float x, float y, float scale, uint h)
        {
            float len = scale * (1.2f + Rnd(h, 2) * 1f), rad = scale * (0.18f + Rnd(h, 5) * 0.07f);
            GfxState st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform((Rnd(h, 11) - 0.5f) * 50f);
            Sh(g, 0, rad * 0.8f, len * 0.55f, rad * 0.8f, 80, 6, 14, 8);
            P.Round(g, -len * 0.5f, -rad, len, rad * 2f, rad * 0.9f, System.Drawing.Color.FromArgb(94, 82, 60), System.Drawing.Color.FromArgb(44, 38, 28), System.Drawing.Color.FromArgb(170, 14, 12, 8), Math.Max(1f, rad * 0.12f));
            Sh(g, -len * 0.12f, -rad * 0.8f, len * 0.36f, rad * 0.5f, 220, 64, 108, 46);
            Sh(g, len * 0.18f, -rad * 0.7f, len * 0.16f, rad * 0.3f, 200, 100, 140, 60);
            g.Restore(st);
        }

        private static void MudBlot(IGfx g, float x, float y, float scale, uint h)
        {
            float rx = scale * (0.25f + Rnd(h, 2) * 0.3f);
            Sh(g, x, y, rx, rx * 0.35f, 90, 54, 44, 28);
            Sh(g, x - rx * 0.2f, y - rx * 0.06f, rx * 0.5f, rx * 0.12f, 80, 150, 130, 90);
        }

        private static void Duckweed(IGfx g, float x, float y, float scale, uint h)
        {
            for (int i = 0; i < 4; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.7f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.35f, s = scale * (0.05f + Rnd(h, 7 + i) * 0.05f);
                Sh(g, px, py, s, s * 0.55f, 210, 120, 168, 56);
                Sh(g, px - s * 0.2f, py - s * 0.1f, s * 0.5f, s * 0.25f, 170, 176, 210, 90);
            }
        }

        private static void PuddleRing(IGfx g, float x, float y, float scale, uint h)
        {
            float s = scale * (0.18f + Rnd(h, 3) * 0.15f);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(90, 190, 220, 200), 1f)) { g.DrawEllipse(p, x - s, y - s * 0.4f, s * 2, s * 0.8f); g.DrawEllipse(p, x - s * 0.55f, y - s * 0.22f, s * 1.1f, s * 0.44f); }
        }

        private static void BogFlower(IGfx g, float x, float y, float scale, uint h)
        {
            Color pc = (h & 1) == 0 ? System.Drawing.Color.FromArgb(236, 214, 120) : System.Drawing.Color.FromArgb(214, 150, 214);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(230, 52, 84, 40), Math.Max(1f, scale * 0.03f))) g.DrawLine(p, x, y, x, y - scale * 0.25f);
            using (SolidBrush b = new SolidBrush(pc)) g.FillEllipse(b, x - scale * 0.07f, y - scale * 0.33f, scale * 0.14f, scale * 0.1f);
            using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(240, 250, 240, 160))) g.FillEllipse(b, x - scale * 0.03f, y - scale * 0.3f, scale * 0.06f, scale * 0.04f);
        }
    }
}
