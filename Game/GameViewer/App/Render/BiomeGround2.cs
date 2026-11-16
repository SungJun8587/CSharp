//***************************************************************************
// BiomeGround2.cs : 폐허 / 해안 / 수정 동굴 / 천상 / 암흑 지역의 바닥(바탕 색 + 장식). BiomeGround 의 나머지 절반.
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
        private static Color Cc(int r, int g, int b) { return System.Drawing.Color.FromArgb(r, g, b); }

        // ------------------------------------------------------------------ 폐허: 갈라진 판석 바닥
        private static readonly Color StoneLit = Cc(168, 162, 148), StoneDeep = Cc(98, 94, 88), RuinMoss = Cc(82, 110, 58), RuinOut = Cc(40, 38, 42), Rubble = Cc(124, 112, 94);

        private static void RuinsColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside >= 0.8f) { r = RuinOut.R; g = RuinOut.G; b = RuinOut.B; return; }
            const float T = 6f;
            float fy = wy / T; int iy = (int)Math.Floor(fy);
            float fx = wx / T + ((iy & 1) == 0 ? 0f : 0.5f); int ix = (int)Math.Floor(fx);
            float ux = fx - ix, uy = fy - iy;
            float tile = Noise.Hash01(ix, iy, 401);
            float n = Smooth(0.15f, 0.85f, Fbm(wx, wy, 0.03f, 403)) * 0.6f + tile * 0.4f;
            r = StoneDeep.R + (StoneLit.R - StoneDeep.R) * n; g = StoneDeep.G + (StoneLit.G - StoneDeep.G) * n; b = StoneDeep.B + (StoneLit.B - StoneDeep.B) * n;
            float grout = Math.Min(Math.Min(ux, 1f - ux) * 1.4f, Math.Min(uy, 1f - uy)) ;
            float gl = 1f - Smooth(0.0f, 0.07f, grout); if (gl > 0) Mix(ref r, ref g, ref b, Cc(38, 36, 34), gl * 0.7f);
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.1f + 1f; r *= grain; g *= grain; b *= grain;
            float rub = Smooth(0.66f, 0.78f, Fbm(wx + 30f, wy + 9f, 0.04f, 411));
            if (rub > 0) { Mix(ref r, ref g, ref b, Rubble, rub * 0.55f); dirtT = rub; }
            float moss = Smooth(0.62f, 0.78f, Fbm(wx + 70f, wy + 40f, 0.05f, 421));
            if (moss > 0) { Mix(ref r, ref g, ref b, RuinMoss, moss * 0.55f); dirtT = Math.Max(dirtT, moss * 0.6f); }
            float cr = 1f - Math.Abs(Fbm(wx, wy, 0.02f, 431) * 2f - 1f);
            float crk = Smooth(0.988f, 0.9995f, cr); crk = crk * crk; if (crk > 0) { Mix(ref r, ref g, ref b, Cc(24, 22, 22), crk * 0.85f); dirtT = Math.Max(dirtT, crk); }
            if (inEdge < 6f) Mix(ref r, ref g, ref b, RuinOut, (1f - Math.Max(inEdge, 0f) / 6f) * 0.55f);
            if (outside > 0) Mix(ref r, ref g, ref b, RuinOut, outside / 0.8f);
        }

        // ------------------------------------------------------------------ 해안: 모래밭, 젖은 모래, 얕은 물, 월드 바깥은 바다
        private static readonly Color SandA = Cc(214, 190, 136), SandB = Cc(240, 224, 172), WetSand = Cc(170, 146, 104), Shallow = Cc(98, 206, 200), DeepSea = Cc(18, 92, 138), SeaMid = Cc(40, 148, 182);

        private static void CoastColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside > 0f)
            {
                float d = Math.Min(outside / 1.5f, 1f);
                float wave = (float)Math.Sin(wx * 0.9f + wy * 0.35f + Noise.Value(wx * 0.08f, wy * 0.08f, 441) * 8f) * 0.5f + 0.5f;
                float k = Smooth(0f, 1f, outside / 14f);
                r = SeaMid.R + (DeepSea.R - SeaMid.R) * k; g = SeaMid.G + (DeepSea.G - SeaMid.G) * k; b = SeaMid.B + (DeepSea.B - SeaMid.B) * k;
                float crest = Smooth(0.86f, 1f, wave); if (crest > 0) Mix(ref r, ref g, ref b, Cc(190, 236, 240), crest * 0.3f * (1f - k * 0.5f));
                float foam = (1f - d) * Smooth(0.3f, 0.7f, Noise.Value(wx * 0.5f, wy * 0.5f, 443)); if (foam > 0) Mix(ref r, ref g, ref b, System.Drawing.Color.White, foam * 0.85f);
                return;
            }
            float n = Smooth(0.2f, 0.8f, Fbm(wx, wy, 0.03f, 451));
            r = SandA.R + (SandB.R - SandA.R) * n; g = SandA.G + (SandB.G - SandA.G) * n; b = SandA.B + (SandB.B - SandA.B) * n;
            float rip = (float)Math.Sin(wx * 0.8f + wy * 0.5f + Noise.Value(wx * 0.1f, wy * 0.1f, 453) * 7f); float rf = 1f + rip * 0.03f; r *= rf; g *= rf; b *= rf;
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.08f + 1f; r *= grain; g *= grain; b *= grain;
            float wet = Smooth(0.6f, 0.76f, Fbm(wx + 11f, wy + 5f, 0.04f, 461));
            if (wet > 0) { Mix(ref r, ref g, ref b, WetSand, wet * 0.6f); dirtT = wet * 0.5f; }
            float veg = Smooth(0.7f, 0.82f, Fbm(wx + 90f, wy + 20f, 0.05f, 463)); if (veg > 0) Mix(ref r, ref g, ref b, Cc(150, 160, 84), veg * 0.3f);
            // 물가: 바다 쪽 가장자리는 젖은 모래 -> 얕은 물
            float w2 = Smooth(0f, 1f, 1f - Math.Max(inEdge, 0f) / 14f);
            float edgeWob = Noise.Value(wx * 0.15f, wy * 0.15f, 467) * 3.5f;
            float ie = inEdge + edgeWob - 1.5f;
            if (ie < 14f) Mix(ref r, ref g, ref b, WetSand, (1f - Math.Max(ie, 0f) / 14f) * 0.8f);
            if (ie < 5f) { float sh = 1f - Math.Max(ie, 0f) / 5f; Mix(ref r, ref g, ref b, Shallow, sh * 0.82f); Mix(ref r, ref g, ref b, System.Drawing.Color.White, Smooth(0.7f, 1f, (float)Math.Sin(wx * 1.1f + wy * 0.4f) * 0.5f + 0.5f) * sh * 0.14f); dirtT = Math.Max(dirtT, sh); }
        }

        // ------------------------------------------------------------------ 수정 동굴: 어두운 바닥 + 빛나는 광맥
        private static readonly Color CaveA = Cc(22, 20, 38), CaveB = Cc(58, 50, 84), VeinCyan = Cc(110, 240, 255), VeinMag = Cc(255, 110, 230), CaveOut = Cc(6, 5, 14);

        private static void CrystalColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside >= 0.8f) { r = CaveOut.R; g = CaveOut.G; b = CaveOut.B; return; }
            float n = Smooth(0.15f, 0.85f, Fbm(wx, wy, 0.03f, 471));
            r = CaveA.R + (CaveB.R - CaveA.R) * n; g = CaveA.G + (CaveB.G - CaveA.G) * n; b = CaveA.B + (CaveB.B - CaveA.B) * n;
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.18f + 1f; r *= grain; g *= grain; b *= grain;
            float pool = Smooth(0.6f, 0.8f, Fbm(wx + 40f, wy + 70f, 0.02f, 473));
            if (pool > 0) Mix(ref r, ref g, ref b, Cc(40, 90, 130), pool * 0.35f);
            float f1 = 1f - Math.Abs(Fbm(wx, wy, 0.013f, 481) * 2f - 1f), f2 = 1f - Math.Abs(Fbm(wx + 200f, wy + 90f, 0.017f, 483) * 2f - 1f);
            float c1 = Smooth(0.972f, 0.9995f, f1), c2 = Smooth(0.974f, 0.9995f, f2);
            c1 = c1 * c1; c2 = c2 * c2;
            float halo = Math.Max(Smooth(0.94f, 0.985f, f1), Smooth(0.94f, 0.985f, f2));
            if (halo > 0) Mix(ref r, ref g, ref b, Cc(60, 90, 170), halo * 0.35f);
            if (c1 > 0) { Mix(ref r, ref g, ref b, VeinCyan, c1 * 0.95f); dirtT = c1; }
            if (c2 > 0) { Mix(ref r, ref g, ref b, VeinMag, c2 * 0.95f); dirtT = Math.Max(dirtT, c2); }
            if (inEdge < 7f) Mix(ref r, ref g, ref b, CaveOut, (1f - Math.Max(inEdge, 0f) / 7f) * 0.65f);
            if (outside > 0) Mix(ref r, ref g, ref b, CaveOut, outside / 0.8f);
        }

        // ------------------------------------------------------------------ 천상: 흰 대리석 바닥 + 금 선, 바깥은 구름 바다
        private static readonly Color MarbleA = Cc(214, 218, 236), MarbleB = Cc(250, 250, 255), Gold = Cc(236, 200, 112), SkyBlue = Cc(142, 186, 242);

        private static void SkyColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside > 0f)
            {
                float cl = Smooth(0.35f, 0.75f, Fbm(wx + 20f, wy, 0.03f, 491));
                float k = Smooth(0f, 1f, outside / 16f);
                r = 236 + (SkyBlue.R - 236) * k; g = 240 + (SkyBlue.G - 240) * k; b = 252 + (SkyBlue.B - 252) * k;
                Mix(ref r, ref g, ref b, System.Drawing.Color.White, cl * 0.55f * (1f - k * 0.3f));
                float sh = Smooth(0.6f, 0.9f, Fbm(wx + 70f, wy + 30f, 0.05f, 493)); Mix(ref r, ref g, ref b, Cc(170, 190, 230), sh * 0.3f);
                return;
            }
            float n = Smooth(0.15f, 0.85f, Fbm(wx, wy, 0.02f, 501));
            r = MarbleA.R + (MarbleB.R - MarbleA.R) * n; g = MarbleA.G + (MarbleB.G - MarbleA.G) * n; b = MarbleA.B + (MarbleB.B - MarbleA.B) * n;
            const float T = 10f;
            float tile = Noise.Hash01((int)Math.Floor(wx / T), (int)Math.Floor(wy / T), 503); float tf = 1f + (tile - 0.5f) * 0.05f; r *= tf; g *= tf; b *= tf;
            float ux = wx / T - (float)Math.Floor(wx / T), uy = wy / T - (float)Math.Floor(wy / T);
            float line = 1f - Smooth(0.0f, 0.025f, Math.Min(Math.Min(ux, 1f - ux), Math.Min(uy, 1f - uy)));
            if (line > 0) { Mix(ref r, ref g, ref b, Gold, line * 0.45f); }
            float vein = 1f - Math.Abs(Fbm(wx, wy, 0.04f, 511) * 2f - 1f);
            float vv = Smooth(0.97f, 0.9995f, vein); vv = vv * vv * vv; if (vv > 0) Mix(ref r, ref g, ref b, Cc(150, 160, 200), vv * 0.55f);
            float glow = Smooth(0.62f, 0.82f, Fbm(wx + 33f, wy + 3f, 0.025f, 513)); if (glow > 0) { Mix(ref r, ref g, ref b, Cc(255, 244, 200), glow * 0.45f); dirtT = glow * 0.4f; }
            float cw = Smooth(0.6f, 0.85f, Fbm(wx + 9f, wy + 60f, 0.03f, 515)); if (cw > 0) Mix(ref r, ref g, ref b, Cc(190, 208, 240), cw * 0.3f);
            if (inEdge < 6f) { float t = 1f - Math.Max(inEdge, 0f) / 6f; Mix(ref r, ref g, ref b, System.Drawing.Color.White, t * 0.35f); }
        }

        // ------------------------------------------------------------------ 암흑: 저주받은 검은 땅 + 보랏빛 균열
        private static readonly Color DarkA = Cc(24, 18, 34), DarkB = Cc(64, 48, 80), Curse = Cc(180, 100, 255), CurseCore = Cc(236, 200, 255), DarkOut = Cc(8, 5, 14);

        private static void DarkColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float outside, inEdge; EdgeOut(wx, wy, minX, minY, maxX, maxY, out outside, out inEdge);
            if (outside >= 0.8f) { r = DarkOut.R; g = DarkOut.G; b = DarkOut.B; return; }
            float n = Smooth(0.15f, 0.85f, Fbm(wx, wy, 0.026f, 521));
            r = DarkA.R + (DarkB.R - DarkA.R) * n; g = DarkA.G + (DarkB.G - DarkA.G) * n; b = DarkA.B + (DarkB.B - DarkA.B) * n;
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.16f + 1f; r *= grain; g *= grain; b *= grain;
            float mist = Smooth(0.58f, 0.8f, Fbm(wx + 60f, wy + 12f, 0.02f, 523)); if (mist > 0) Mix(ref r, ref g, ref b, Cc(96, 62, 128), mist * 0.35f);
            float dead = Smooth(0.62f, 0.8f, Fbm(wx + 10f, wy + 99f, 0.04f, 525)); if (dead > 0) Mix(ref r, ref g, ref b, Cc(52, 40, 44), dead * 0.5f);
            float f1 = 1f - Math.Abs(Fbm(wx, wy, 0.014f, 531) * 2f - 1f), f2 = 1f - Math.Abs(Fbm(wx + 130f, wy - 70f, 0.02f, 533) * 2f - 1f);
            float c1 = Smooth(0.974f, 0.9995f, f1), c2 = Smooth(0.978f, 0.9995f, f2) * 0.85f; c1 = c1 * c1 * c1; c2 = c2 * c2 * c2;
            float halo = Math.Max(Smooth(0.95f, 0.99f, f1), Smooth(0.95f, 0.99f, f2 * 0.99f)); if (halo > 0) Mix(ref r, ref g, ref b, Cc(120, 60, 170), halo * 0.3f);
            float crack = Math.Max(c1, c2);
            if (crack > 0) { Mix(ref r, ref g, ref b, Curse, crack * 0.95f); Mix(ref r, ref g, ref b, CurseCore, Smooth(0.7f, 1f, crack) * 0.8f); dirtT = crack; }
            if (inEdge < 7f) Mix(ref r, ref g, ref b, DarkOut, (1f - Math.Max(inEdge, 0f) / 7f) * 0.65f);
            if (outside > 0) Mix(ref r, ref g, ref b, DarkOut, outside / 0.8f);
        }

        // ------------------------------------------------------------------ 장식
        private static void NewDetail(MapKind kind, Graphics g, float x, float y, float scale, uint h, uint k, float dirtT)
        {
            switch (kind)
            {
                case MapKind.Ruins:
                    if (k < 8) StoneBits(g, x, y, scale, h, Cc(150, 144, 132), Cc(84, 80, 76));
                    else if (k < 12) { if (scale >= 7) Brick(g, x, y, scale, h); }
                    else if (k < 17) { if (dirtT < 0.7f) Weed(g, x, y, scale, h, Cc(84, 128, 56)); }
                    else if (k < 19) { if (scale >= 8 && (h & 3) == 0) Drum(g, x, y, scale, h); }
                    else if (k < 27) Mossy(g, x, y, scale, h);
                    else if (k < 33) Crackle(g, x, y, scale, h, Cc(30, 28, 28));
                    break;
                case MapKind.Coast:
                    if (k < 6) Shell(g, x, y, scale, h);
                    else if (k < 8) { if (scale >= 7) Starfish(g, x, y, scale, h); }
                    else if (k < 10) { if (scale >= 8 && (h & 1) == 0) Driftwood(g, x, y, scale, h); }
                    else if (k < 16) { if (dirtT < 0.4f) Weed(g, x, y, scale, h, Cc(146, 158, 72)); }
                    else if (k < 22) Seaweed(g, x, y, scale, h);
                    else if (k < 25) StoneBits(g, x, y, scale, h, Cc(204, 198, 184), Cc(150, 140, 124));
                    else if (k < 44) RipplePatch(g, x, y, scale, h, Cc(255, 248, 220));
                    break;
                case MapKind.Crystal:
                    if (k < 12) GlowShards(g, x, y, scale, h);
                    else if (k < 17) { if (scale >= 7) Stalag(g, x, y, scale, h); }
                    else if (k < 22) GlowShroom(g, x, y, scale, h);
                    else if (k < 24) StoneBits(g, x, y, scale, h, Cc(70, 64, 96), Cc(26, 22, 44));
                    else if (k < 31) Mote(g, x, y, scale, h, (h & 1) == 0 ? Cc(130, 240, 255) : Cc(255, 140, 236));
                    break;
                case MapKind.Sky:
                    if (k < 8) Petals(g, x, y, scale, h);
                    else if (k < 14) Mote(g, x, y, scale, h, Cc(255, 226, 140));
                    else if (k < 18) { if (scale >= 7) Feather(g, x, y, scale, h); }
                    else if (k < 26) CloudWisp(g, x, y, scale, h);
                    else if (k < 29) { if (scale >= 7) GlowFlower(g, x, y, scale, h); }
                    else if (k < 34) Crackle(g, x, y, scale, h, Cc(170, 178, 210));
                    break;
                default:
                    if (k < 6) { if (scale >= 7) Bones(g, x, y, scale, h); }
                    else if (k < 10) { if (scale >= 7) Gravestone(g, x, y, scale, h); }
                    else if (k < 15) SoulFlame(g, x, y, scale, h);
                    else if (k < 18) { if (scale >= 8 && dirtT < 0.4f) Rune(g, x, y, scale, h); }
                    else if (k < 25) Thorns(g, x, y, scale, h);
                    else if (k < 34) Pebbles(g, x, y, scale, h);
                    else if (k < 40) Mote(g, x, y, scale, h, Cc(190, 140, 255));
                    break;
            }
        }

        private static void StoneBits(Graphics g, float x, float y, float scale, uint h, Color hi, Color lo)
        {
            int n = 1 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.6f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.3f, s = scale * (0.1f + Rnd(h, 7 + i * 2) * 0.12f);
                Sh(g, px + s * 0.3f, py + s * 0.35f, s * 1.3f, s * 0.5f, 70, 0, 0, 0);
                PointF[] pts = { new PointF(px - s, py), new PointF(px - s * 0.7f, py - s * 0.8f), new PointF(px + s * 0.2f, py - s), new PointF(px + s * 0.95f, py - s * 0.4f), new PointF(px + s, py) };
                P.Poly(g, pts, hi, lo, System.Drawing.Color.FromArgb(150, 20, 20, 20), Math.Max(1f, s * 0.12f));
            }
        }

        private static void Brick(Graphics g, float x, float y, float scale, uint h)
        {
            float w = scale * 0.3f, hh = scale * 0.14f;
            Sh(g, x, y + hh, w * 1.3f, hh * 0.7f, 70, 0, 0, 0);
            for (int i = 0; i < 3; ++i)
            {
                float bx = x - w * 0.6f + (i == 2 ? w * 0.5f : i * w * 1.05f), by = y - (i == 2 ? hh : 0f);
                P.Round(g, bx, by - hh, w, hh, hh * 0.15f, Cc(170, 120, 96), Cc(112, 76, 60), System.Drawing.Color.FromArgb(160, 30, 18, 14), 1f);
            }
        }

        private static void Weed(Graphics g, float x, float y, float scale, uint h, Color c)
        {
            float len = scale * (0.28f + Rnd(h, 2) * 0.22f);
            int n = 5 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float a = -(float)Math.PI * (0.15f + 0.7f * i / (n - 1)) + (Rnd(h, 5 + i) - 0.5f) * 0.2f, l = len * (0.7f + Rnd(h, 9 + i) * 0.5f);
                PointF[] pts = { new PointF(x, y), new PointF(x + (float)Math.Cos(a) * l * 0.5f, y + (float)Math.Sin(a) * l * 0.6f), new PointF(x + (float)Math.Cos(a) * l, y + (float)Math.Sin(a) * l * 0.7f + l * 0.14f) };
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(220, Col.Cl(c.R - 40), Col.Cl(c.G - 40), Col.Cl(c.B - 30)), Math.Max(1f, scale * 0.04f))) g.DrawCurve(p, pts, 0.5f);
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(160, Col.Cl(c.R + 40), Col.Cl(c.G + 40), Col.Cl(c.B + 30)), Math.Max(1f, scale * 0.02f))) g.DrawCurve(p, pts, 0.5f);
            }
        }

        private static void Drum(Graphics g, float x, float y, float scale, uint h)
        {
            float w = scale * 0.7f, hh = scale * 0.3f;
            GraphicsState st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform((Rnd(h, 3) - 0.5f) * 40f);
            Sh(g, 0, hh * 0.9f, w * 0.7f, hh * 0.5f, 70, 0, 0, 0);
            P.Round(g, -w * 0.5f, -hh, w, hh * 2f, hh * 0.3f, Cc(190, 184, 170), Cc(112, 106, 96), System.Drawing.Color.FromArgb(170, 30, 28, 26), 1.2f);
            for (int i = -1; i <= 1; ++i) using (Pen p = new Pen(System.Drawing.Color.FromArgb(90, 40, 38, 34), 1f)) g.DrawLine(p, -w * 0.45f, i * hh * 0.6f, w * 0.45f, i * hh * 0.6f);
            Sh(g, w * 0.5f, 0, hh * 0.4f, hh, 255, 214, 208, 194);
            g.Restore(st);
        }

        private static void Mossy(Graphics g, float x, float y, float scale, uint h)
        {
            float rx = scale * (0.3f + Rnd(h, 2) * 0.3f);
            Sh(g, x, y, rx, rx * 0.4f, 100, 70, 110, 52);
            Sh(g, x - rx * 0.2f, y - rx * 0.06f, rx * 0.55f, rx * 0.18f, 110, 110, 150, 70);
        }

        private static void Crackle(Graphics g, float x, float y, float scale, uint h, Color c)
        {
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, c), 1f))
            {
                float a = Rnd(h, 3) * 6.28f, px = x, py = y;
                for (int i = 0; i < 4; ++i)
                {
                    a += (Rnd(h, 5 + i * 2) - 0.5f) * 1.3f;
                    float nx = px + (float)Math.Cos(a) * scale * 0.33f, ny = py + (float)Math.Sin(a) * scale * 0.2f;
                    g.DrawLine(p, px, py, nx, ny); px = nx; py = ny;
                }
            }
        }

        private static void Shell(Graphics g, float x, float y, float scale, uint h)
        {
            float s = scale * (0.1f + Rnd(h, 2) * 0.06f);
            Color c = (h & 1) == 0 ? Cc(250, 214, 200) : Cc(244, 236, 214);
            Sh(g, x + s * 0.3f, y + s * 0.4f, s * 1.1f, s * 0.5f, 60, 100, 70, 40);
            P.Ball(g, x, y - s * 0.3f, s, s * 0.8f, System.Drawing.Color.White, c, System.Drawing.Color.FromArgb(160, 150, 100, 80), 1f);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(130, 160, 110, 90), 1f)) for (int i = -1; i <= 1; ++i) g.DrawLine(p, x + i * s * 0.35f, y - s * 0.9f, x + i * s * 0.15f, y);
        }

        private static void Starfish(Graphics g, float x, float y, float scale, uint h)
        {
            float R = scale * 0.16f, r = R * 0.45f; PointF[] pts = new PointF[10];
            float rot = Rnd(h, 3) * 6.28f;
            for (int i = 0; i < 10; ++i) { double a = rot + i * Math.PI / 5; float rr = i % 2 == 0 ? R : r; pts[i] = new PointF(x + (float)Math.Cos(a) * rr, y + (float)Math.Sin(a) * rr * 0.6f); }
            Sh(g, x + 1, y + R * 0.3f, R, R * 0.4f, 50, 90, 60, 30);
            P.Poly(g, pts, Cc(255, 168, 96), Cc(214, 92, 60), System.Drawing.Color.FromArgb(180, 120, 50, 30), 1f);
        }

        private static void Driftwood(Graphics g, float x, float y, float scale, uint h)
        {
            float len = scale * (0.9f + Rnd(h, 2) * 0.8f), rad = scale * 0.1f;
            GraphicsState st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform((Rnd(h, 11) - 0.5f) * 70f);
            Sh(g, 0, rad * 0.9f, len * 0.5f, rad * 0.6f, 60, 90, 60, 30);
            P.Round(g, -len * 0.5f, -rad, len, rad * 2f, rad * 0.9f, Cc(206, 190, 160), Cc(134, 116, 90), System.Drawing.Color.FromArgb(150, 70, 56, 40), 1f);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(90, 90, 74, 56), 1f)) g.DrawLine(p, -len * 0.4f, 0, len * 0.35f, 1);
            g.Restore(st);
        }

        private static void Seaweed(Graphics g, float x, float y, float scale, uint h)
        {
            for (int i = 0; i < 4; ++i)
            {
                float bx = x + (i - 1.5f) * scale * 0.08f, l = scale * (0.25f + Rnd(h, 3 + i) * 0.2f);
                PointF[] c = { new PointF(bx, y), new PointF(bx + (Rnd(h, 8 + i) - 0.5f) * l, y - l * 0.5f), new PointF(bx + (Rnd(h, 12 + i) - 0.5f) * l * 1.5f, y - l * 0.3f) };
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(220, 52, 88, 56), Math.Max(1.4f, scale * 0.04f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, c, 0.5f);
            }
        }

        private static void RipplePatch(Graphics g, float x, float y, float scale, uint h, Color c)
        {
            float w = scale * (0.3f + Rnd(h, 2) * 0.3f);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(46, c), 1f)) { g.DrawArc(p, x - w, y - w * 0.12f, w * 2, w * 0.4f, 0, 180); g.DrawArc(p, x - w * 0.6f, y + w * 0.1f, w * 1.2f, w * 0.26f, 0, 180); }
        }

        private static void GlowShards(Graphics g, float x, float y, float scale, uint h)
        {
            Color c = (h & 3) == 0 ? Cc(255, 130, 236) : ((h & 3) == 1 ? Cc(130, 250, 255) : Cc(160, 170, 255));
            Sh(g, x, y, scale * 0.45f, scale * 0.16f, 34, c.R, c.G, c.B);
            int n = 2 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (i - (n - 1) * 0.5f) * scale * 0.16f, hh = scale * (0.22f + Rnd(h, 4 + i * 2) * 0.3f), w = scale * 0.06f, lean = (Rnd(h, 8 + i) - 0.5f) * scale * 0.12f;
                PointF[] pts = { new PointF(px - w, y), new PointF(px - w * 0.5f + lean * 0.4f, y - hh * 0.7f), new PointF(px + lean, y - hh), new PointF(px + w * 0.6f + lean * 0.4f, y - hh * 0.65f), new PointF(px + w, y) };
                P.Poly(g, pts, Col.Light(c, 0.55f), Col.Dark(c, 0.25f), System.Drawing.Color.FromArgb(180, Col.Dark(c, 0.6f)), Math.Max(1f, w * 0.18f));
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(210, 255, 255, 255), Math.Max(1f, w * 0.14f))) g.DrawLine(p, px - w * 0.25f, y - hh * 0.15f, px - w * 0.1f + lean * 0.7f, y - hh * 0.75f);
            }
        }

        private static void Stalag(Graphics g, float x, float y, float scale, uint h)
        {
            float hh = scale * (0.4f + Rnd(h, 3) * 0.3f), w = scale * 0.16f;
            Sh(g, x, y + 1, w * 1.4f, w * 0.5f, 90, 0, 0, 0);
            PointF[] pts = { new PointF(x - w, y), new PointF(x - w * 0.4f, y - hh * 0.7f), new PointF(x, y - hh), new PointF(x + w * 0.4f, y - hh * 0.65f), new PointF(x + w, y) };
            P.Poly(g, pts, Cc(92, 84, 120), Cc(30, 26, 48), System.Drawing.Color.FromArgb(200, 10, 8, 20), 1f);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, 130, 210, 255), 1f)) g.DrawLine(p, x - w * 0.3f, y - 2, x - w * 0.1f, y - hh * 0.7f);
        }

        private static void GlowShroom(Graphics g, float x, float y, float scale, uint h)
        {
            Color c = (h & 1) == 0 ? Cc(110, 240, 255) : Cc(255, 130, 230);
            int n = 1 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float mx = x + (i - (n - 1) * 0.5f) * scale * 0.28f, s = scale * (0.12f + Rnd(h, 6 + i) * 0.08f);
                Sh(g, mx, y - s, s * 2.4f, s * 1.4f, 34, c.R, c.G, c.B);
                using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(220, 214, 220))) g.FillRectangle(b, mx - s * 0.16f, y - s * 0.9f, s * 0.32f, s * 0.9f);
                using (SolidBrush b = new SolidBrush(c)) g.FillPie(b, mx - s, y - s * 1.8f, s * 2f, s * 1.8f, 180, 180);
                using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(b, mx - s * 0.5f, y - s * 1.5f, s * 0.4f, s * 0.2f);
            }
        }

        private static void Mote(Graphics g, float x, float y, float scale, uint h, Color c)
        {
            float s = scale * (0.04f + Rnd(h, 3) * 0.04f);
            Sh(g, x, y, s * 3f, s * 3f, 30, c.R, c.G, c.B);
            Sh(g, x, y, s, s, 230, Col.Cl(c.R + 40), Col.Cl(c.G + 40), Col.Cl(c.B + 40));
        }

        private static void Petals(Graphics g, float x, float y, float scale, uint h)
        {
            for (int i = 0; i < 4; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.7f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.4f, s = scale * (0.04f + Rnd(h, 7 + i) * 0.03f);
                GraphicsState st = g.Save(); g.TranslateTransform(px, py); g.RotateTransform(Rnd(h, 9 + i) * 360f);
                using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(220, 255, 214, 226))) g.FillEllipse(b, -s * 1.5f, -s * 0.7f, s * 3f, s * 1.4f);
                g.Restore(st);
            }
        }

        private static void Feather(Graphics g, float x, float y, float scale, uint h)
        {
            float l = scale * 0.4f;
            GraphicsState st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform((Rnd(h, 3) - 0.5f) * 120f);
            Sh(g, 1, 2, l, l * 0.18f, 36, 80, 90, 140);
            using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(244, 255, 255, 255))) g.FillEllipse(b, -l, -l * 0.16f, l * 2f, l * 0.32f);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(160, 170, 180, 210), 1f)) g.DrawLine(p, -l, 0, l, 0);
            g.Restore(st);
        }

        private static void CloudWisp(Graphics g, float x, float y, float scale, uint h)
        {
            float rx = scale * (0.5f + Rnd(h, 2) * 0.5f), ry = rx * 0.22f;
            Sh(g, x, y, rx, ry, 70, 255, 255, 255); Sh(g, x + rx * 0.2f, y - ry * 0.3f, rx * 0.6f, ry * 0.7f, 90, 255, 255, 255);
        }

        private static void GlowFlower(Graphics g, float x, float y, float scale, uint h)
        {
            Sh(g, x, y - scale * 0.1f, scale * 0.3f, scale * 0.2f, 40, 255, 230, 150);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(220, 150, 190, 150), Math.Max(1f, scale * 0.03f))) g.DrawLine(p, x, y, x, y - scale * 0.2f);
            for (int i = 0; i < 5; ++i) { double a = i * 1.2566; using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(240, 255, 250, 240))) g.FillEllipse(b, x + (float)Math.Cos(a) * scale * 0.06f - scale * 0.045f, y - scale * 0.24f + (float)Math.Sin(a) * scale * 0.04f - scale * 0.03f, scale * 0.09f, scale * 0.06f); }
            using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(255, 255, 214, 100))) g.FillEllipse(b, x - scale * 0.025f, y - scale * 0.26f, scale * 0.05f, scale * 0.04f);
        }

        private static void Gravestone(Graphics g, float x, float y, float scale, uint h)
        {
            float w = scale * 0.2f, hh = scale * 0.38f;
            Sh(g, x + w * 0.3f, y + 2, w * 1.3f, w * 0.4f, 90, 0, 0, 0);
            GraphicsPath gp = new GraphicsPath();
            gp.AddLine(x - w, y, x - w, y - hh * 0.7f); gp.AddArc(x - w, y - hh, w * 2, w * 2, 180, 180); gp.AddLine(x + w, y - hh * 0.7f, x + w, y); gp.CloseFigure();
            using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x - w, y - hh, w * 2, hh), Cc(112, 104, 124), Cc(44, 38, 56), LinearGradientMode.Vertical)) g.FillPath(lb, gp);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(200, 14, 10, 20), 1.4f)) g.DrawPath(p, gp);
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(170, 190, 140, 255), Math.Max(1f, scale * 0.025f))) { g.DrawLine(p, x, y - hh * 0.75f, x, y - hh * 0.35f); g.DrawLine(p, x - w * 0.4f, y - hh * 0.6f, x + w * 0.4f, y - hh * 0.6f); }
            gp.Dispose();
        }

        private static void SoulFlame(Graphics g, float x, float y, float scale, uint h)
        {
            float s = scale * (0.12f + Rnd(h, 3) * 0.08f);
            Sh(g, x, y - s, s * 3.2f, s * 2.4f, 30, 150, 110, 255);
            Sh(g, x, y + 1, s * 0.8f, s * 0.25f, 100, 0, 0, 0);
            PointF[] fl = { new PointF(x - s * 0.5f, y), new PointF(x - s * 0.35f, y - s * 1.1f), new PointF(x, y - s * 2.2f), new PointF(x + s * 0.35f, y - s * 1.0f), new PointF(x + s * 0.5f, y) };
            using (GraphicsPath gp = new GraphicsPath()) { gp.AddClosedCurve(fl); using (SolidBrush b = new SolidBrush(System.Drawing.Color.FromArgb(220, 160, 120, 255))) g.FillPath(b, gp); }
            Sh(g, x, y - s * 0.7f, s * 0.3f, s * 0.6f, 240, 236, 226, 255);
        }

        private static void Rune(Graphics g, float x, float y, float scale, uint h)
        {
            float rx = scale * 0.55f, ry = rx * 0.5f;
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(120, 180, 110, 255), Math.Max(1f, scale * 0.025f))) { g.DrawEllipse(p, x - rx, y - ry, rx * 2, ry * 2); g.DrawEllipse(p, x - rx * 0.66f, y - ry * 0.66f, rx * 1.32f, ry * 1.32f); }
            using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, 210, 150, 255), Math.Max(1f, scale * 0.02f)))
                for (int i = 0; i < 5; ++i) { double a = i * 1.2566 + Rnd(h, 3); g.DrawLine(p, x + (float)Math.Cos(a) * rx * 0.66f, y + (float)Math.Sin(a) * ry * 0.66f, x + (float)Math.Cos(a + 2.5) * rx * 0.66f, y + (float)Math.Sin(a + 2.5) * ry * 0.66f); }
        }

        private static void Thorns(Graphics g, float x, float y, float scale, uint h)
        {
            Sh(g, x, y + 1, scale * 0.35f, scale * 0.08f, 80, 0, 0, 0);
            int n = 5 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float a = -(float)Math.PI * (0.1f + 0.8f * i / (n - 1)), l = scale * (0.25f + Rnd(h, 4 + i) * 0.25f);
                PointF[] t = { new PointF(x, y), new PointF(x + (float)Math.Cos(a) * l * 0.5f, y + (float)Math.Sin(a) * l * 0.6f), new PointF(x + (float)Math.Cos(a) * l, y + (float)Math.Sin(a) * l) };
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(240, 14, 10, 18), Math.Max(1.4f, scale * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawCurve(p, t, 0.5f);
                using (Pen p = new Pen(System.Drawing.Color.FromArgb(150, 150, 90, 210), Math.Max(1f, scale * 0.015f))) g.DrawCurve(p, t, 0.5f);
            }
        }
    }
}
