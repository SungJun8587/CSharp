//***************************************************************************
// GroundRenderer.cs : 숲 바닥과 월드 바깥의 짙은 숲 테두리. 전부 좌표 해시/노이즈로 만들어서
// 카메라가 움직여도 무늬가 월드에 붙어 있고, 별도 텍스처 없이 결정적으로 같은 모양이 나온다.
//
// 바닥은 두 겹이다.
//   1) 바탕: 화면의 1/Step 해상도로 노이즈를 계산(풀 명암, 이끼, 낙엽층, 흙길, 나무 그늘, 햇빛 얼룩, 월드 가장자리의 어둠)해
//      부드럽게 늘려 그린다. 칸이 보이지 않고, 계산량은 화면 픽셀 수의 1/Step^2 이다.
//   2) 장식: 고사리, 덤불, 버섯, 쓰러진 통나무, 돌, 낙엽, 풀포기, 꽃. 격자 칸마다 해시로 뭘 놓을지 정한다.
// 두 겹 모두 화면보다 큰 비트맵에 한 번 그려 두고, 카메라가 여유분 밖으로 나가거나 확대가 바뀔 때만 다시 그린다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class GroundRenderer : IDisposable
    {
        private const int Step = 6;                    // 바탕 계산 해상도 (화면 px 당 1/Step)
        private const int MarginPx = 320;
        private const float DetailCell = 1.5f;         // 장식 격자 한 칸 (월드 unit)

        private static readonly Color GrassDark = Color.FromArgb(34, 78, 34), GrassLight = Color.FromArgb(104, 150, 62);
        private static readonly Color MeadowDeep = Color.FromArgb(40, 104, 56), MeadowLit = Color.FromArgb(132, 182, 68);
        private static readonly Color Moss = Color.FromArgb(46, 120, 84), Dirt = Color.FromArgb(112, 90, 60), DirtDark = Color.FromArgb(88, 70, 48);
        private static readonly Color LitterA = Color.FromArgb(124, 88, 48), LitterB = Color.FromArgb(150, 104, 44);
        private static readonly Color Shade = Color.FromArgb(8, 36, 46), Sun = Color.FromArgb(255, 244, 150);
        private static readonly Color Outside = Color.FromArgb(18, 40, 24);

        private readonly SolidBrush[] _flowers = {
            new SolidBrush(Color.FromArgb(255, 240, 120)), new SolidBrush(Color.FromArgb(250, 250, 250)),
            new SolidBrush(Color.FromArgb(255, 150, 190)), new SolidBrush(Color.FromArgb(170, 190, 255)) };
        private readonly Pen _tuftDark = new Pen(Color.FromArgb(70, 40, 100, 40), 1f);
        private readonly Pen _tuftLight = new Pen(Color.FromArgb(90, 150, 200, 90), 1f);

        private Bitmap _bmp;
        private Bitmap _low;
        private int[] _lowPx;
        private double _bmpScale;
        private float _bmpWorldX, _bmpWorldY;          // 비트맵 왼쪽 위의 월드 좌표
        public int Rebuilds;

        public void Dispose()
        {
            if (_bmp != null) _bmp.Dispose();
            if (_low != null) _low.Dispose();
            if (_next != null) _next.Dispose();
            foreach (SolidBrush b in _flowers) b.Dispose();
            _tuftDark.Dispose(); _tuftLight.Dispose();
        }

        // 다음 비트맵을 Bands 조각으로 나눠 프레임마다 한 조각씩 그린다 (한 번에 그리면 걸을 때 끊긴다).
        // 같은 월드 좌표는 항상 같은 모양이라서 조각 경계에서도 이음매가 생기지 않는다.
        private const int Bands = 16;
        private Bitmap _next;
        private float _nextWorldX, _nextWorldY;
        private int _band = -1;                        // -1: 만들고 있지 않음

        private MapKind _builtKind = MapKind.Forest;
        private uint _builtZone;

        public void Draw(IGfx g, ViewState v, float minX, float minY, float maxX, float maxY)
        {
            if (_builtKind != ZoneTheme.Current || _builtZone != ZoneTheme.CurrentZoneId) { _builtKind = ZoneTheme.Current; _builtZone = ZoneTheme.CurrentZoneId; if (_bmp != null) { _bmp.Dispose(); _bmp = null; } _band = -1; }
            int bw = v.Width + MarginPx * 2, bh = v.Height + MarginPx * 2;
            float vx0 = v.ToWorldX(0), vy0 = v.ToWorldY(0), vx1 = v.ToWorldX(v.Width), vy1 = v.ToWorldY(v.Height);
            bool must = _bmp == null || _bmp.Width != bw || _bmp.Height != bh || Math.Abs(_bmpScale - v.Scale) > 1e-6
                || vx0 < _bmpWorldX || vy0 < _bmpWorldY || vx1 > _bmpWorldX + (float)(bw / v.Scale) || vy1 > _bmpWorldY + (float)(bh / v.Scale);
            if (must)
            {
                // 화면이 이미 비트맵 밖: 한 번에 다시 그린다 (처음, 확대 변경, 순간이동)
                if (_bmp == null || _bmp.Width != bw || _bmp.Height != bh) { if (_bmp != null) _bmp.Dispose(); _bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb); }
                _band = -1;
                _bmpScale = v.Scale;
                _bmpWorldX = v.CamX - (float)(bw / 2.0 / v.Scale);
                _bmpWorldY = v.CamY - (float)(bh / 2.0 / v.Scale);
                BuildBand(_bmp, v, bw, bh, _bmpWorldX, _bmpWorldY, 0, bh, minX, minY, maxX, maxY);
                ++Rebuilds;
            }
            else
            {
                float edge = (float)(MarginPx * 0.5 / v.Scale);        // 여유분이 절반 남으면 미리 다음 것을 만들기 시작
                if (_band < 0 && (vx0 - _bmpWorldX < edge || vy0 - _bmpWorldY < edge
                    || _bmpWorldX + (float)(bw / v.Scale) - vx1 < edge || _bmpWorldY + (float)(bh / v.Scale) - vy1 < edge))
                {
                    if (_next == null || _next.Width != bw || _next.Height != bh) { if (_next != null) _next.Dispose(); _next = new Bitmap(bw, bh, PixelFormat.Format32bppArgb); }
                    _nextWorldX = v.CamX - (float)(bw / 2.0 / v.Scale);
                    _nextWorldY = v.CamY - (float)(bh / 2.0 / v.Scale);
                    _band = 0;
                }
                if (_band >= 0)
                {
                    int y0 = bh * _band / Bands, y1 = bh * (_band + 1) / Bands;
                    BuildBand(_next, v, bw, bh, _nextWorldX, _nextWorldY, y0, y1, minX, minY, maxX, maxY);
                    if (++_band >= Bands)
                    {
                        Bitmap t = _bmp; _bmp = _next; _next = t;
                        _bmpWorldX = _nextWorldX; _bmpWorldY = _nextWorldY;
                        _band = -1; ++Rebuilds;
                    }
                }
            }
            // 비트맵 중 화면에 보이는 부분만 복사한다
            int ox = (int)Math.Round(v.ToScreenX(_bmpWorldX)), oy = (int)Math.Round(v.ToScreenY(_bmpWorldY));
            int sx0 = Math.Max(0, -ox), sy0 = Math.Max(0, -oy);
            int sw = Math.Min(bw - sx0, v.Width - Math.Max(0, ox)), sh = Math.Min(bh - sy0, v.Height - Math.Max(0, oy));
            CompositingMode cm = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(_bmp, new Rectangle(ox + sx0, oy + sy0, sw, sh), sx0, sy0, sw, sh, GraphicsUnit.Pixel);
            g.CompositingMode = cm;
        }

        /// <summary>검증용: 같은 화면을 한 번에(banded=false) 또는 Bands 조각으로(true) 그려 돌려준다. 두 결과는 같아야 한다.</summary>
        public Bitmap RenderForTest(ViewState v, bool banded, float minX, float minY, float maxX, float maxY)
        {
            Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb);
            float ox = v.CamX - (float)(v.Width / 2.0 / v.Scale), oy = v.CamY - (float)(v.Height / 2.0 / v.Scale);
            int n = banded ? Bands : 1;
            for (int i = 0; i < n; ++i)
                BuildBand(bmp, v, v.Width, v.Height, ox, oy, v.Height * i / n, v.Height * (i + 1) / n, minX, minY, maxX, maxY);
            return bmp;
        }

        /// <summary>target 비트맵의 [y0,y1) 줄을 그린다. 원점은 (worldX, worldY) 월드 좌표.</summary>
        private void BuildBand(Bitmap target, ViewState v, int bw, int bh, float worldX, float worldY, int y0, int y1, float minX, float minY, float maxX, float maxY)
        {
            ViewState bv = new ViewState();
            bv.Width = bw; bv.Height = bh; bv.Scale = v.Scale;
            bv.CamX = worldX + (float)(bw / 2.0 / v.Scale); bv.CamY = worldY + (float)(bh / 2.0 / v.Scale);
            using (GdiGfx bg = GdiGfx.FromImage(target))
            {
                bg.SetClip(new Rectangle(0, y0, bw, y1 - y0));
                RenderBase(bg, bv, y0, y1, minX, minY, maxX, maxY);
                RenderDetails(bg, bv, y0, y1, minX, minY, maxX, maxY);
            }
        }

        // ------------------------------------------------------------------ 1) 바탕

        private static float Smooth(float a, float b, float x) { return Mx.Smooth(a, b, x); }

        private static void Mix(ref float r, ref float g, ref float b, Color c, float t) { Mx.MixInto(ref r, ref g, ref b, c.R, c.G, c.B, t); }

        /// <summary>월드 좌표 한 점의 바닥 색 (바탕 한 겹). 장식이 이 값을 참조해 흙길 위를 피한다.</summary>
        private static void BaseColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            BaseColorK(ZoneTheme.Current, wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT);
            if (ZoneGates.Current.Count == 0) return;
            // 존 사이 관문 근처는 건너편 존의 지형으로 서서히 바뀐다 (관문 바깥은 그 지형의 길이 이어진다)
            MapKind nk; bool outside;
            float w = ZoneGates.Weight(ZoneGates.Current, wx, wy, minX, minY, maxX, maxY, out nk, out outside);
            if (w <= 0.003f) return;
            float r2, g2, b2, d2;
            if (outside) BaseColorK(nk, wx, wy, minX - 200f, minY - 200f, maxX + 200f, maxY + 200f, out r2, out g2, out b2, out d2);
            else BaseColorK(nk, wx, wy, minX, minY, maxX, maxY, out r2, out g2, out b2, out d2);
            r += (r2 - r) * w; g += (g2 - g) * w; b += (b2 - b) * w; dirtT += (d2 - dirtT) * w;
        }

        private static void BaseColorK(MapKind kind, float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            if (kind == MapKind.Desert) { DesertColor(wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); return; }
            if (kind != MapKind.Forest) { BiomeGround.BaseAt(kind, wx, wy, minX, minY, maxX, maxY, out r, out g, out b, out dirtT); return; }
            float ex = Math.Max(Math.Max(minX - wx, wx - maxX), 0f), ey = Math.Max(Math.Max(minY - wy, wy - maxY), 0f);
            float outside = Math.Max(ex, ey);
            if (outside >= 0.8f) { r = Outside.R; g = Outside.G; b = Outside.B; return; }

            // 바탕 풀색: 햇빛 받는 연두 초원 <-> 그늘진 짙은 녹색을 큰 무늬로 섞는다
            float n = Noise.Value(wx * 0.022f, wy * 0.022f, 7) * 0.6f + Noise.Value(wx * 0.075f, wy * 0.075f, 11) * 0.4f;
            n = Smooth(0.18f, 0.82f, n);
            r = MeadowDeep.R + (MeadowLit.R - MeadowDeep.R) * n;
            g = MeadowDeep.G + (MeadowLit.G - MeadowDeep.G) * n;
            b = MeadowDeep.B + (MeadowLit.B - MeadowDeep.B) * n;

            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.09f + 1f;
            r *= grain; g *= grain; b *= grain;

            // 이끼 낀 듯한 청록 얼룩, 아주 약한 낙엽층 색
            float moss = Smooth(0.56f, 0.8f, Noise.Value(wx * 0.05f + 3f, wy * 0.05f + 8f, 23));
            if (moss > 0) Mix(ref r, ref g, ref b, Moss, moss * 0.5f);
            float litter = Smooth(0.66f, 0.84f, Noise.Value(wx * 0.09f + 50f, wy * 0.09f + 7f, 31));
            if (litter > 0) Mix(ref r, ref g, ref b, LitterA, litter * 0.22f);

            // 흙길: 구불구불 이어지는 가는 띠. 가운데는 다져진 흙, 가장자리는 풀과 섞인다.
            float ridge = 1f - Math.Abs(Noise.Value(wx * 0.0085f + 17f, wy * 0.0085f + 5f, 61) * 2f - 1f);
            ridge += (Noise.Value(wx * 0.05f, wy * 0.05f, 67) - 0.5f) * 0.012f;
            dirtT = Smooth(0.945f, 0.985f, ridge);
            if (dirtT > 0)
            {
                float dg = Noise.Value(wx * 1.1f, wy * 1.1f, 43);
                Color dc = Color.FromArgb((int)(DirtDark.R + (Dirt.R - DirtDark.R) * dg + 24), (int)(DirtDark.G + (Dirt.G - DirtDark.G) * dg + 20), (int)(DirtDark.B + (Dirt.B - DirtDark.B) * dg + 14));
                float core = Smooth(0.975f, 0.995f, ridge);
                Color rim = Color.FromArgb(Math.Max(0, dc.R - 36), Math.Max(0, dc.G - 30), Math.Max(0, dc.B - 24));
                Mix(ref r, ref g, ref b, rim, dirtT * 0.7f);
                Mix(ref r, ref g, ref b, dc, core * 0.85f);
                // 수레바퀴 자국: 길 가운데 양옆으로 두 줄 골이 파이고, 한가운데는 풀이 얇게 올라온다
                float rutD = (ridge - 0.982f) / 0.0055f;
                float rut = (float)Math.Exp(-rutD * rutD);
                if (rut > 0.01f) Mix(ref r, ref g, ref b, Color.FromArgb(58, 44, 30), rut * 0.72f * core);
                float mid = Smooth(0.9925f, 0.9985f, ridge);
                if (mid > 0f) Mix(ref r, ref g, ref b, Color.FromArgb(92, 130, 64), mid * 0.38f * core);
            }

            // 나뭇잎 사이로 비치는 햇빛 얼룩(밝고 따뜻하게)과 나무 그늘(차갑고 짙게)
            float sun = Smooth(0.60f, 0.80f, Noise.Value(wx * 0.06f + 7f, wy * 0.06f + 70f, 53));
            if (sun > 0) Mix(ref r, ref g, ref b, Sun, sun * 0.26f);
            float shade = Smooth(0.52f, 0.78f, Noise.Value(wx * 0.032f + 100f, wy * 0.032f + 20f, 41)) * 0.30f
                        + Smooth(0.64f, 0.82f, Noise.Value(wx * 0.11f + 5f, wy * 0.11f + 60f, 47)) * 0.10f;
            if (shade > 0) Mix(ref r, ref g, ref b, Shade, shade);

            // 월드 안쪽 가장자리는 점점 어두워지고 바깥 숲으로 이어진다
            float inEdge = Math.Min(Math.Min(wx - minX, maxX - wx), Math.Min(wy - minY, maxY - wy));
            inEdge += (Noise.Value(wx * 0.18f, wy * 0.18f, 83) - 0.5f) * 5f;        // 경계띠가 직선이 되지 않도록 흔든다
            if (inEdge < 7f) Mix(ref r, ref g, ref b, Shade, (1f - Math.Max(inEdge, 0f) / 7f) * 0.46f);
            if (outside > 0) Mix(ref r, ref g, ref b, Outside, outside / 0.8f);
        }

        private static readonly Color SandDark = Color.FromArgb(200, 160, 100), SandLight = Color.FromArgb(240, 212, 150);
        private static readonly Color Pan = Color.FromArgb(176, 134, 90), PanDark = Color.FromArgb(146, 106, 70), IronRed = Color.FromArgb(208, 134, 82);
        private static readonly Color CliffRock = Color.FromArgb(104, 70, 46), Crest = Color.FromArgb(254, 236, 184), Lee = Color.FromArgb(150, 108, 66);

        /// <summary>사막 바닥: 모래 바탕 + 바람이 만든 사구 능선과 잔물결 + 굳은 땅 + 붉은 철분 얼룩. 월드 바깥은 절벽 바위 색.</summary>
        private static void DesertColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float ex = Math.Max(Math.Max(minX - wx, wx - maxX), 0f), ey = Math.Max(Math.Max(minY - wy, wy - maxY), 0f);
            float outside = Math.Max(ex, ey);
            if (outside >= 0.8f) { r = CliffRock.R; g = CliffRock.G; b = CliffRock.B; return; }

            float n = Noise.Value(wx * 0.03f, wy * 0.03f, 71) * 0.6f + Noise.Value(wx * 0.12f, wy * 0.12f, 73) * 0.4f;
            r = SandDark.R + (SandLight.R - SandDark.R) * n; g = SandDark.G + (SandLight.G - SandDark.G) * n; b = SandDark.B + (SandLight.B - SandDark.B) * n;

            // 사구 능선: 바람 방향으로 길게 이어지는 띠. 마루는 밝고 그늘 쪽은 어둡다.
            float d = wx * 0.026f + wy * 0.03f + Noise.Value(wx * 0.022f, wy * 0.022f, 79) * 5.5f + Noise.Value(wx * 0.07f, wy * 0.07f, 81) * 0.9f;
            float s = (float)Math.Sin(d * 6.2832f);
            float sh = 1f + s * 0.07f;
            r *= sh; g *= sh; b *= sh;
            float crest = Smooth(0.86f, 1f, s); if (crest > 0) Mix(ref r, ref g, ref b, Crest, crest * 0.38f);
            float lee = Smooth(0.8f, 1f, -s); if (lee > 0) Mix(ref r, ref g, ref b, Lee, lee * 0.34f);
            // 잔물결
            float rip = (float)Math.Sin((wx * 0.95f + wy * 0.4f + Noise.Value(wx * 0.1f, wy * 0.1f, 83) * 7f));
            float rf = 1f + rip * 0.025f; r *= rf; g *= rf; b *= rf;
            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.10f + 1f;
            r *= grain; g *= grain; b *= grain;

            float iron = Smooth(0.60f, 0.80f, Noise.Value(wx * 0.05f + 9f, wy * 0.05f + 31f, 89));
            if (iron > 0) Mix(ref r, ref g, ref b, IronRed, iron * 0.34f);
            dirtT = Smooth(0.68f, 0.82f, Noise.Value(wx * 0.028f + 40f, wy * 0.028f + 90f, 91));
            if (dirtT > 0)
            {
                float dg = Noise.Value(wx * 1.0f, wy * 1.0f, 93);
                Color dc = Color.FromArgb((int)(PanDark.R + (Pan.R - PanDark.R) * dg), (int)(PanDark.G + (Pan.G - PanDark.G) * dg), (int)(PanDark.B + (Pan.B - PanDark.B) * dg));
                Mix(ref r, ref g, ref b, dc, dirtT * 0.88f);
            }
            // 구름 그림자가 지나간 듯한 넓은 명암
            float haze = Smooth(0.55f, 0.8f, Noise.Value(wx * 0.012f + 5f, wy * 0.012f + 77f, 97));
            if (haze > 0) Mix(ref r, ref g, ref b, Color.FromArgb(120, 76, 44), haze * 0.16f);

            float inEdge = Math.Min(Math.Min(wx - minX, maxX - wx), Math.Min(wy - minY, maxY - wy));
            if (inEdge < 6f) Mix(ref r, ref g, ref b, Lee, (1f - Math.Max(inEdge, 0f) / 6f) * 0.5f);
            if (outside > 0) Mix(ref r, ref g, ref b, CliffRock, outside / 0.8f);
        }

        private void RenderBase(IGfx g, ViewState v, int y0, int y1, float minX, float minY, float maxX, float maxY)
        {
            int lw = (v.Width + Step - 1) / Step + 1;
            int ly0 = Math.Max(0, y0 / Step - 1), ly1 = Math.Min((v.Height + Step - 1) / Step + 1, (y1 + Step - 1) / Step + 2);
            int lh = ly1 - ly0;
            if (lh <= 0) return;
            if (_low == null || _low.Width != lw || _low.Height != lh)
            {
                if (_low != null) _low.Dispose();
                _low = new Bitmap(lw, lh, PixelFormat.Format32bppArgb);
                _lowPx = new int[lw * lh];
            }
            for (int ly = 0; ly < lh; ++ly)
            {
                float wy = v.ToWorldY((ly0 + ly + 0.5f) * Step);
                for (int lx = 0; lx < lw; ++lx)
                {
                    float wx = v.ToWorldX((lx + 0.5f) * Step);
                    float r, gg, b, d;
                    BaseColor(wx, wy, minX, minY, maxX, maxY, out r, out gg, out b, out d);
                    int ri = (int)r, gi = (int)gg, bi = (int)b;
                    _lowPx[ly * lw + lx] = unchecked((int)0xFF000000) | ((ri < 0 ? 0 : ri > 255 ? 255 : ri) << 16) | ((gi < 0 ? 0 : gi > 255 ? 255 : gi) << 8) | (bi < 0 ? 0 : bi > 255 ? 255 : bi);
                }
            }
            BitmapData bd = _low.LockBits(new Rectangle(0, 0, lw, lh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (bd.Stride == lw * 4) Marshal.Copy(_lowPx, 0, bd.Scan0, lw * lh);
                else for (int y = 0; y < lh; ++y) Marshal.Copy(_lowPx, y * lw, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), lw);
            }
            finally { _low.UnlockBits(bd); GfxImages.Touch(_low); }

            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(_low, new Rectangle(0, ly0 * Step, lw * Step, lh * Step), 0, 0, lw, lh, GraphicsUnit.Pixel, ia);
            }
            g.CompositingMode = CompositingMode.SourceOver;
        }

        // ------------------------------------------------------------------ 2) 장식

        private void RenderDetails(IGfx g, ViewState v, int y0, int y1, float minX, float minY, float maxX, float maxY)
        {
            float scale = (float)v.Scale;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (ZoneTheme.Current == MapKind.Desert) { RenderDesertDetails(g, v, y0, y1, minX, minY, maxX, maxY); return; }
            if (ZoneTheme.Current != MapKind.Forest) { BiomeGround.Details(ZoneTheme.Current, g, v, y0, y1, minX, minY, maxX, maxY); return; }

            if (scale >= 5)
            {
                float wx0 = v.ToWorldX(-80), wy0 = v.ToWorldY(y0 - 100), wx1 = v.ToWorldX(v.Width + 80), wy1 = v.ToWorldY(y1 + 100);
                int cx0 = (int)Math.Floor(wx0 / DetailCell), cx1 = (int)Math.Ceiling(wx1 / DetailCell);
                int cy0 = (int)Math.Floor(wy0 / DetailCell), cy1 = (int)Math.Ceiling(wy1 / DetailCell);
                for (int cy = cy0; cy <= cy1; ++cy)
                {
                    for (int cx = cx0; cx <= cx1; ++cx)
                    {
                        uint h = Noise.Hash(cx, cy, 78);
                        float wx = (cx + ((h >> 8) & 255) / 255f) * DetailCell, wy = (cy + ((h >> 16) & 255) / 255f) * DetailCell;
                        if (wx < minX + 0.5f || wy < minY + 0.5f || wx > maxX - 0.5f || wy > maxY - 0.5f) continue;
                        float sx = v.ToScreenX(wx), sy = v.ToScreenY(wy);
                        float r0, g0, b0, dirtT;
                        BaseColor(wx, wy, minX, minY, maxX, maxY, out r0, out g0, out b0, out dirtT);
                        uint kind = (h >> 24) % 100;
                        float patch = Noise.Value(wx * 0.045f + 11f, wy * 0.045f + 3f, 101);      // 꽃밭/버섯 무리가 모이는 곳
                        if (dirtT > 0.5f) { if (kind < 30) Pebbles(g, sx, sy, scale, h); continue; }
                        if (dirtT > 0.03f)       // 길 가장자리: 풀이 닳아 끊기며 돌멩이와 풀포기가 몰린다
                        {
                            if (kind < 38) GrassClump(g, sx, sy, scale * 1.1f, h);
                            else if (kind < 56) Pebbles(g, sx, sy, scale, h);
                            else if (kind < 60 && scale >= 6) Fern(g, sx, sy, scale, h);
                            continue;
                        }
                        if (kind < 21) GrassClump(g, sx, sy, scale, h);
                        else if (kind < 25) { if (scale >= 6) Fern(g, sx, sy, scale, h); }
                        else if (kind < 28) { if (scale >= 8) Bush(g, sx, sy, scale, h); }
                        else if (kind < 34) { if (patch > 0.55f) FlowerPatch(g, sx, sy, scale, h, patch); }
                        else if (kind < 36) { if (scale >= 9 && patch < 0.42f && (h & 1) == 0) Mushrooms(g, sx, sy, scale, h); }
                        else if (kind < 37) { if (scale >= 8 && (h & 7) == 0) Log(g, sx, sy, scale, h); }
                        else if (kind < 40) MossRock(g, sx, sy, scale, h);
                        else if (kind < 46) Pebbles(g, sx, sy, scale, h);
                        else if (kind < 52) Leaves(g, sx, sy, scale, h);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ 2') 사막 장식

        private void RenderDesertDetails(IGfx g, ViewState v, int y0, int y1, float minX, float minY, float maxX, float maxY)
        {
            float scale = (float)v.Scale;
            if (scale < 5) return;
            float wx0 = v.ToWorldX(-80), wy0 = v.ToWorldY(y0 - 100), wx1 = v.ToWorldX(v.Width + 80), wy1 = v.ToWorldY(y1 + 100);
            int cx0 = (int)Math.Floor(wx0 / DetailCell), cx1 = (int)Math.Ceiling(wx1 / DetailCell);
            int cy0 = (int)Math.Floor(wy0 / DetailCell), cy1 = (int)Math.Ceiling(wy1 / DetailCell);
            for (int cy = cy0; cy <= cy1; ++cy)
                for (int cx = cx0; cx <= cx1; ++cx)
                {
                    uint h = Noise.Hash(cx, cy, 178);
                    float wx = (cx + ((h >> 8) & 255) / 255f) * DetailCell, wy = (cy + ((h >> 16) & 255) / 255f) * DetailCell;
                    if (wx < minX + 0.5f || wy < minY + 0.5f || wx > maxX - 0.5f || wy > maxY - 0.5f) continue;
                    float sx = v.ToScreenX(wx), sy = v.ToScreenY(wy);
                    float r0, g0, b0, dirtT;
                    BaseColor(wx, wy, minX, minY, maxX, maxY, out r0, out g0, out b0, out dirtT);
                    uint kind = (h >> 24) % 100;
                    if (kind < 7) { if (dirtT < 0.6f) DryTuft(g, sx, sy, scale, h); }
                    else if (kind < 10) SandRocks(g, sx, sy, scale, h);
                    else if (kind < 11) { if (scale >= 7) Bones(g, sx, sy, scale, h); }
                    else if (kind < 12) { if (scale >= 8 && dirtT < 0.5f && (h & 3) == 0) Barrel(g, sx, sy, scale, h); }
                    else if (kind < 20) { if (dirtT > 0.35f) Cracks(g, sx, sy, scale, h); }
                    else if (kind < 32) DesertPebbles(g, sx, sy, scale, h);
                    else if (kind < 46) RippleMarks(g, sx, sy, scale, h);
                    else if (kind < 48) { if (scale >= 7) Shrub(g, sx, sy, scale, h); }
                }
        }

        private static void DShadow(IGfx g, float x, float y, float rx, float ry, int alpha)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 90, 54, 20))) g.FillEllipse(b, x - rx, y - ry, rx * 2, ry * 2);
        }

        private static void DryTuft(IGfx g, float x, float y, float scale, uint h)
        {
            float len = scale * (0.35f + Rnd(h, 2) * 0.3f);
            DShadow(g, x, y + len * 0.05f, len * 0.5f, len * 0.14f, 40);
            int n = 6 + (int)(h % 4);
            for (int i = 0; i < n; ++i)
            {
                float a = -(float)Math.PI * (0.12f + 0.76f * i / (n - 1)) + (Rnd(h, 5 + i) - 0.5f) * 0.2f;
                float l = len * (0.7f + Rnd(h, 9 + i) * 0.5f);
                PointF[] pts = { new PointF(x, y), new PointF(x + (float)Math.Cos(a) * l * 0.5f, y + (float)Math.Sin(a) * l * 0.55f), new PointF(x + (float)Math.Cos(a) * l * 1.05f, y + (float)Math.Sin(a) * l * 0.7f + l * 0.12f) };
                using (Pen p = new Pen(Color.FromArgb(200, 120, 90, 46), Math.Max(1f, scale * 0.045f))) g.DrawCurve(p, pts, 0.5f);
                using (Pen p = new Pen(Color.FromArgb(150, 214, 184, 112), Math.Max(1f, scale * 0.025f))) g.DrawCurve(p, pts, 0.5f);
            }
        }

        private static void SandRocks(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 1 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.6f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.3f;
                float s = scale * (0.14f + Rnd(h, 7 + i * 2) * 0.14f);
                DShadow(g, px + s * 0.3f, py + s * 0.35f, s * 1.3f, s * 0.5f, 60);
                PointF[] pts = { new PointF(px - s, py), new PointF(px - s * 0.7f, py - s * 0.8f), new PointF(px + s * 0.2f, py - s * 1.0f), new PointF(px + s * 0.95f, py - s * 0.4f), new PointF(px + s, py) };
                int sh = (int)(Rnd(h, 11 + i) * 30) - 10;
                P.Poly(g, pts, Color.FromArgb(Col.Cl(204 + sh), Col.Cl(160 + sh), Col.Cl(110 + sh)), Color.FromArgb(Col.Cl(150 + sh), Col.Cl(106 + sh), Col.Cl(70 + sh)), Color.FromArgb(150, 70, 40, 18), Math.Max(1f, s * 0.12f));
            }
        }

        private static void Bones(IGfx g, float x, float y, float scale, uint h)
        {
            float s = scale * 0.5f;
            GfxState st = g.Save(); g.TranslateTransform(x, y); g.RotateTransform((Rnd(h, 3) - 0.5f) * 60f);
            DShadow(g, 0, s * 0.12f, s * 0.7f, s * 0.15f, 50);
            Color bone = Color.FromArgb(236, 228, 206), edge = Color.FromArgb(150, 90, 70, 50);
            using (Pen p = new Pen(edge, Math.Max(1.6f, s * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(p, -s * 0.5f, 0, s * 0.5f, 0); }
            using (Pen p = new Pen(bone, Math.Max(1f, s * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(p, -s * 0.5f, 0, s * 0.5f, 0); }
            for (int i = -2; i <= 2; ++i)
            {
                using (Pen p = new Pen(edge, Math.Max(1.4f, s * 0.08f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawArc(p, i * s * 0.2f - s * 0.12f, -s * 0.3f, s * 0.24f, s * 0.6f, 270, 180); }
                using (Pen p = new Pen(bone, Math.Max(1f, s * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawArc(p, i * s * 0.2f - s * 0.12f, -s * 0.3f, s * 0.24f, s * 0.6f, 270, 180); }
            }
            using (SolidBrush b = new SolidBrush(edge)) g.FillEllipse(b, s * 0.5f - 1, -s * 0.16f, s * 0.38f, s * 0.32f);
            using (SolidBrush b = new SolidBrush(bone)) g.FillEllipse(b, s * 0.5f + 1, -s * 0.13f, s * 0.32f, s * 0.26f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 50, 36, 26))) { g.FillEllipse(b, s * 0.62f, -s * 0.08f, s * 0.07f, s * 0.08f); g.FillEllipse(b, s * 0.72f, -s * 0.08f, s * 0.07f, s * 0.08f); }
            g.Restore(st);
        }

        private static void Barrel(IGfx g, float x, float y, float scale, uint h)
        {
            float r = scale * (0.2f + Rnd(h, 2) * 0.14f);
            DShadow(g, x, y + r * 0.2f, r * 1.1f, r * 0.4f, 60);
            P.Ball(g, x, y - r * 0.8f, r, r * 0.85f, Color.FromArgb(132, 186, 92), Color.FromArgb(58, 108, 56), Color.FromArgb(150, 20, 50, 24), Math.Max(1f, r * 0.1f));
            using (Pen p = new Pen(Color.FromArgb(120, 28, 70, 32), Math.Max(1f, r * 0.08f))) for (int i = -1; i <= 1; ++i) g.DrawArc(p, x - r * 0.4f + i * r * 0.4f, y - r * 1.6f, r * 0.8f, r * 1.5f, 80, 100);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 238, 110, 150))) g.FillEllipse(b, x - r * 0.25f, y - r * 1.8f, r * 0.5f, r * 0.34f);
        }

        private static void Cracks(IGfx g, float x, float y, float scale, uint h)
        {
            using (Pen p = new Pen(Color.FromArgb(150, 70, 44, 24), Math.Max(1f, scale * 0.03f)) { LineJoin = LineJoin.Round })
            {
                float len = scale * (0.8f + Rnd(h, 2) * 0.8f);
                PointF cur = new PointF(x, y); float a = Rnd(h, 5) * 6.28f;
                for (int i = 0; i < 5; ++i)
                {
                    a += (Rnd(h, 8 + i) - 0.5f) * 1.4f;
                    PointF nx = new PointF(cur.X + (float)Math.Cos(a) * len * 0.22f, cur.Y + (float)Math.Sin(a) * len * 0.12f);
                    g.DrawLine(p, cur, nx);
                    if (i == 2) g.DrawLine(p, nx, new PointF(nx.X + (float)Math.Cos(a + 1.1f) * len * 0.2f, nx.Y + (float)Math.Sin(a + 1.1f) * len * 0.1f));
                    cur = nx;
                }
            }
        }

        private static void DesertPebbles(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 2 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.7f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.4f;
                float s = scale * (0.05f + Rnd(h, 7 + i * 2) * 0.06f);
                int sh = (int)(Rnd(h, 9 + i) * 40) - 20;
                DShadow(g, px + s * 0.2f, py + s * 0.35f, s * 1.1f, s * 0.5f, 50);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(Col.Cl(176 + sh), Col.Cl(140 + sh), Col.Cl(100 + sh)))) g.FillEllipse(b, px - s, py - s * 0.7f, s * 2f, s * 1.4f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(90, 255, 240, 200))) g.FillEllipse(b, px - s * 0.5f, py - s * 0.5f, s * 0.8f, s * 0.4f);
            }
        }

        /// <summary>바람이 모래에 남긴 물결무늬(작은 호 몇 줄).</summary>
        private static void RippleMarks(IGfx g, float x, float y, float scale, uint h)
        {
            float w = scale * (0.9f + Rnd(h, 2) * 0.9f);
            int n = 2 + (int)(h % 3);
            using (Pen dk = new Pen(Color.FromArgb(46, 120, 76, 36), Math.Max(1f, scale * 0.035f)))
            using (Pen lt = new Pen(Color.FromArgb(70, 255, 238, 190), Math.Max(1f, scale * 0.03f)))
                for (int i = 0; i < n; ++i)
                {
                    float yy = y + i * scale * 0.16f, ww = w * (1f - i * 0.12f);
                    g.DrawArc(dk, x - ww / 2, yy, ww, scale * 0.22f, 190, 160);
                    g.DrawArc(lt, x - ww / 2, yy - scale * 0.04f, ww, scale * 0.22f, 190, 160);
                }
        }

        private static void Shrub(IGfx g, float x, float y, float scale, uint h)
        {
            float r = scale * (0.3f + Rnd(h, 2) * 0.2f);
            DShadow(g, x, y + r * 0.1f, r * 1.1f, r * 0.35f, 55);
            for (int i = 0; i < 9; ++i)
            {
                float a = -(float)Math.PI * (0.05f + 0.9f * i / 8f);
                float l = r * (0.7f + Rnd(h, 3 + i) * 0.5f);
                using (Pen p = new Pen(Color.FromArgb(220, 108, 82, 44), Math.Max(1f, scale * 0.035f)) { StartCap = LineCap.Round }) g.DrawLine(p, x, y, x + (float)Math.Cos(a) * l, y + (float)Math.Sin(a) * l * 0.9f);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 150, 140, 70))) for (int i = 0; i < 6; ++i) g.FillEllipse(b, x + (Rnd(h, 5 + i) - 0.5f) * r * 1.6f - 1.5f, y - r * (0.2f + Rnd(h, 6 + i) * 0.7f), 3f, 2.5f);
        }

        /// <summary>풀 무더기: 휘어진 풀잎 여러 장을 어두운색 -> 밝은색 순서로 겹쳐 입체감을 낸다.</summary>
        private static void GrassClump(IGfx g, float x, float y, float scale, uint h)
        {
            float len = scale * (0.42f + Rnd(h, 2) * 0.34f);
            int blades = 7 + (int)(h % 5);
            Shadow(g, x, y + len * 0.04f, len * 0.5f, len * 0.13f, 44);
            int tone = (int)(Rnd(h, 4) * 36f);
            Color[] cols = { Color.FromArgb(28, 82 + tone / 2, 40), Color.FromArgb(52, 128 + tone / 2, 52), Color.FromArgb(120, 184 + tone / 3, 72) };
            for (int pass = 0; pass < 3; ++pass)
                using (Pen p = new Pen(cols[pass], Math.Max(1.1f, scale * (0.07f - pass * 0.018f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = pass; i < blades; i += 3 - (pass == 2 ? 1 : 0))
                    {
                        float a = -(float)Math.PI * (0.16f + 0.68f * i / Math.Max(1, blades - 1)) + (Rnd(h, 5 + i) - 0.5f) * 0.25f;
                        float l = len * (0.62f + Rnd(h, 9 + i) * 0.5f) * (pass == 2 ? 0.8f : 1f);
                        float bend = (Rnd(h, 13 + i) - 0.5f) * l * 0.5f;
                        PointF[] pts = { new PointF(x + (i - blades / 2f) * len * 0.04f, y), new PointF(x + (float)Math.Cos(a) * l * 0.55f + bend * 0.4f, y + (float)Math.Sin(a) * l * 0.62f), new PointF(x + (float)Math.Cos(a) * l * 0.9f + bend, y + (float)Math.Sin(a) * l * 0.85f + l * 0.08f) };
                        g.DrawCurve(p, pts, 0.5f);
                    }
        }

        private static readonly Color[] PetalCols = {
            Color.FromArgb(255, 238, 120), Color.FromArgb(252, 252, 250), Color.FromArgb(255, 150, 190), Color.FromArgb(170, 190, 255), Color.FromArgb(255, 170, 90) };

        /// <summary>꽃밭: 같은 색 계열 꽃이 무리 지어 핀다(꽃밭 노이즈 값으로 색을 고른다).</summary>
        private static void FlowerPatch(IGfx g, float x, float y, float scale, uint h, float patch)
        {
            Color petal = PetalCols[(int)(patch * 977f) % PetalCols.Length];
            int n = 3 + (int)(h % 4);
            for (int i = 0; i < n; ++i)
            {
                float fx = x + (Rnd(h, 3 + i * 2) - 0.5f) * scale * 0.9f, fy = y + (Rnd(h, 4 + i * 2) - 0.5f) * scale * 0.5f;
                float s = Math.Max(1.6f, scale * (0.07f + Rnd(h, 8 + i) * 0.04f));
                using (Pen p = new Pen(Color.FromArgb(200, 40, 110, 44), Math.Max(1f, scale * 0.03f))) g.DrawLine(p, fx, fy, fx, fy + s * 1.6f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 90, 44))) g.FillEllipse(b, fx - s * 0.9f, fy + s * 1.1f, s * 1.8f, s * 0.7f);
                using (SolidBrush b = new SolidBrush(petal))
                    for (int k = 0; k < 5; ++k)
                    {
                        double a = k * Math.PI * 2 / 5 + h * 0.1;
                        g.FillEllipse(b, fx + (float)Math.Cos(a) * s * 0.62f - s * 0.38f, fy + (float)Math.Sin(a) * s * 0.55f - s * 0.34f, s * 0.76f, s * 0.68f);
                    }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 226, 90))) g.FillEllipse(b, fx - s * 0.26f, fy - s * 0.24f, s * 0.52f, s * 0.48f);
            }
        }

        /// <summary>이끼 낀 바위.</summary>
        private static void MossRock(IGfx g, float x, float y, float scale, uint h)
        {
            if (scale < 6) return;
            float s = scale * (0.18f + Rnd(h, 2) * 0.16f);
            Shadow(g, x + s * 0.2f, y + s * 0.15f, s * 1.5f, s * 0.5f, 70);
            PointF[] pts = { new PointF(x - s * 1.1f, y), new PointF(x - s * 0.9f, y - s * 0.7f), new PointF(x - s * 0.2f, y - s * 1.15f), new PointF(x + s * 0.7f, y - s * 0.9f), new PointF(x + s * 1.15f, y - s * 0.2f), new PointF(x + s * 1.0f, y) };
            P.Poly(g, pts, Color.FromArgb(172, 172, 164), Color.FromArgb(82, 86, 84), Color.FromArgb(170, 24, 30, 28), Math.Max(1f, s * 0.1f));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(215, 74, 140, 60))) g.FillEllipse(b, x - s * 0.9f, y - s * 1.1f, s * 1.5f, s * 0.6f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 116, 184, 78))) g.FillEllipse(b, x - s * 0.6f, y - s * 1.08f, s * 0.7f, s * 0.26f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 250))) g.FillEllipse(b, x - s * 0.1f, y - s * 0.7f, s * 0.7f, s * 0.3f);
        }

        private static float Rnd(uint h, int shift) { return ((h >> shift) & 255) / 255f; }

        private static void Shadow(IGfx g, float x, float y, float rx, float ry, int alpha)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 6, 20, 12))) g.FillEllipse(b, x - rx, y - ry, rx * 2, ry * 2);
        }

        private static void Fern(IGfx g, float x, float y, float scale, uint h)
        {
            float len = scale * (0.55f + Rnd(h, 2) * 0.45f);
            int fronds = 6 + (int)(h % 3);
            Shadow(g, x, y + len * 0.05f, len * 0.5f, len * 0.16f, 46);
            Color dark = Color.FromArgb(30, 84 + (int)(h % 24), 34), light = Color.FromArgb(92, 160 + (int)(h % 30), 70);
            for (int i = 0; i < fronds; ++i)
            {
                float a = -(float)Math.PI + (float)Math.PI * (i + 0.5f) / fronds + (Rnd(h, 5 + i) - 0.5f) * 0.25f;     // 위쪽 반원으로 퍼짐
                float l = len * (0.7f + Rnd(h, 9 + i) * 0.4f);
                float ex = x + (float)Math.Cos(a) * l, ey = y + (float)Math.Sin(a) * l * 0.75f;
                float mx = x + (float)Math.Cos(a) * l * 0.5f, my = y + (float)Math.Sin(a) * l * 0.75f * 0.5f - l * 0.22f;
                PointF[] pts = { new PointF(x, y), new PointF(mx, my), new PointF(ex, ey + l * 0.12f) };
                using (Pen p = new Pen(Color.FromArgb(220, dark), Math.Max(1.2f, scale * 0.07f))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawCurve(p, pts, 0.6f); }
                using (Pen p = new Pen(Color.FromArgb(150, light), Math.Max(1f, scale * 0.035f))) g.DrawCurve(p, pts, 0.6f);
            }
        }

        private static void Bush(IGfx g, float x, float y, float scale, uint h)
        {
            float r = scale * (0.42f + Rnd(h, 2) * 0.3f);
            Shadow(g, x, y + r * 0.2f, r * 1.25f, r * 0.45f, 60);
            int tone = (int)(h % 3) * 8;
            Color deep = Color.FromArgb(22, 66 + tone, 32), mid = Color.FromArgb(46, 112 + tone, 50), light = Color.FromArgb(98, 160 + tone, 70), ol = Color.FromArgb(90, 10, 30, 16);
            float[,] blobs = { { -0.6f, -0.1f, 0.55f }, { 0.6f, -0.12f, 0.52f }, { 0f, -0.55f, 0.6f }, { -0.25f, -0.2f, 0.5f }, { 0.25f, -0.25f, 0.5f } };
            using (SolidBrush bd = new SolidBrush(deep), bm = new SolidBrush(mid), bl = new SolidBrush(light))
            {
                for (int i = 0; i < 5; ++i)
                {
                    float cx = x + blobs[i, 0] * r, cy = y + blobs[i, 1] * r, rx = blobs[i, 2] * r, ry = rx * 0.85f;
                    g.FillEllipse(bd, cx - rx, cy - ry, rx * 2, ry * 2);
                    g.FillEllipse(bm, cx - rx * 0.9f, cy - ry * 0.95f, rx * 1.8f, ry * 1.6f);
                    g.FillEllipse(bl, cx - rx * 0.62f, cy - ry * 0.85f, rx * 1.0f, ry * 0.8f);
                }
            }
            if ((h & 7) == 0)       // 열매
                for (int i = 0; i < 5; ++i)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 190, 40, 50)))
                        g.FillEllipse(b, x + (Rnd(h, 3 + i * 2) - 0.5f) * r * 1.4f - 1.5f, y - r * (0.2f + Rnd(h, 4 + i * 2) * 0.6f) - 1.5f, 3f, 3f);
        }

        private static void Mushrooms(IGfx g, float x, float y, float scale, uint h)
        {
            bool red = (h & 1) == 0;
            Color cap = red ? Color.FromArgb(200, 60, 52) : Color.FromArgb(196, 150, 96);
            int n = 1 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float mx = x + (i - (n - 1) * 0.5f) * scale * 0.32f + (Rnd(h, 3 + i) - 0.5f) * scale * 0.1f, my = y + (Rnd(h, 11 + i) - 0.5f) * scale * 0.15f;
                float s = scale * (0.16f + Rnd(h, 6 + i) * 0.12f);
                Shadow(g, mx, my, s * 1.1f, s * 0.35f, 60);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(236, 232, 214))) g.FillRectangle(b, mx - s * 0.2f, my - s * 0.9f, s * 0.4f, s * 0.9f);
                using (SolidBrush b = new SolidBrush(cap)) g.FillPie(b, mx - s, my - s * 1.8f, s * 2f, s * 1.8f, 180, 180);
                if (red) using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 250, 244, 230)))
                {
                    g.FillEllipse(b, mx - s * 0.5f, my - s * 1.2f, s * 0.3f, s * 0.24f);
                    g.FillEllipse(b, mx + s * 0.1f, my - s * 1.4f, s * 0.26f, s * 0.2f);
                }
            }
        }

        private static void Log(IGfx g, float x, float y, float scale, uint h)
        {
            float len = scale * (1.4f + Rnd(h, 2) * 1.2f), rad = scale * (0.2f + Rnd(h, 5) * 0.08f);
            float ang = (Rnd(h, 11) - 0.5f) * 50f;
            GfxState st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(ang);
            Shadow(g, 0, rad * 0.8f, len * 0.55f, rad * 0.8f, 70);
            P.Round(g, -len * 0.5f, -rad, len, rad * 2f, rad * 0.9f, Color.FromArgb(124, 92, 60), Color.FromArgb(70, 50, 32), Color.FromArgb(150, 30, 20, 14), Math.Max(1f, rad * 0.12f));
            using (Pen p = new Pen(Color.FromArgb(70, 40, 26, 16), Math.Max(1f, rad * 0.08f)))
                for (int i = -1; i <= 1; ++i) g.DrawLine(p, -len * 0.42f, i * rad * 0.45f, len * 0.42f, i * rad * 0.45f + (i == 0 ? 0 : 0.4f));
            // 단면 + 나이테
            using (SolidBrush b1 = new SolidBrush(Color.FromArgb(172, 128, 80)), b2 = new SolidBrush(Color.FromArgb(214, 176, 120))) { float ex = len * 0.5f - rad * 0.2f; g.FillEllipse(b1, ex - rad * 0.55f, -rad * 0.95f, rad * 1.1f, rad * 1.9f); g.FillEllipse(b2, ex - rad * 0.45f, -rad * 0.8f, rad * 0.9f, rad * 1.5f); }
            using (Pen p = new Pen(Color.FromArgb(120, 110, 76, 44), 1f))
            {
                g.DrawEllipse(p, len * 0.5f - rad * 0.2f - rad * 0.3f, -rad * 0.55f, rad * 0.6f, rad * 1.1f);
                g.DrawEllipse(p, len * 0.5f - rad * 0.2f - rad * 0.12f, -rad * 0.2f, rad * 0.24f, rad * 0.4f);
            }
            // 이끼
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 70, 128, 56))) g.FillEllipse(b, -len * 0.3f, -rad * 1.05f, len * 0.4f, rad * 0.7f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 96, 156, 70))) g.FillEllipse(b, -len * 0.22f, -rad * 1.0f, len * 0.18f, rad * 0.35f);
            g.Restore(st);
        }

        private static void Pebbles(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 2 + (int)(h % 3);
            for (int i = 0; i < n; ++i)
            {
                float px = x + (Rnd(h, 3 + i * 3) - 0.5f) * scale * 0.7f, py = y + (Rnd(h, 5 + i * 3) - 0.5f) * scale * 0.4f;
                float s = scale * (0.07f + Rnd(h, 7 + i * 2) * 0.07f);
                int gray = 112 + (int)(Rnd(h, 9 + i) * 40);
                Shadow(g, px + s * 0.2f, py + s * 0.35f, s * 1.1f, s * 0.5f, 55);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(gray, gray - 4, gray - 14))) g.FillEllipse(b, px - s, py - s * 0.7f, s * 2f, s * 1.4f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(90, 255, 255, 240))) g.FillEllipse(b, px - s * 0.5f, py - s * 0.5f, s * 0.8f, s * 0.4f);
            }
        }

        private static readonly Color[] LeafColors = {
            Color.FromArgb(214, 176, 92, 36), Color.FromArgb(214, 200, 150, 48), Color.FromArgb(214, 138, 70, 36), Color.FromArgb(214, 160, 60, 40), Color.FromArgb(200, 110, 120, 44) };

        private static void Leaves(IGfx g, float x, float y, float scale, uint h)
        {
            int n = 2 + (int)(h % 3);
            GfxState st = g.Save();
            for (int i = 0; i < n; ++i)
            {
                float lx = x + (Rnd(h, 3 + i * 2) - 0.5f) * scale * 0.9f, ly = y + (Rnd(h, 4 + i * 2) - 0.5f) * scale * 0.5f;
                float s = scale * (0.07f + Rnd(h, 6 + i) * 0.06f);
                g.ResetTransform();
                g.TranslateTransform(lx, ly);
                g.RotateTransform(Rnd(h, 8 + i) * 360f);
                using (SolidBrush b = new SolidBrush(LeafColors[(h >> (2 + i)) % (uint)LeafColors.Length])) g.FillEllipse(b, -s * 1.5f, -s * 0.7f, s * 3f, s * 1.4f);
                using (Pen p = new Pen(Color.FromArgb(120, 60, 34, 16), 1f)) g.DrawLine(p, -s * 1.4f, 0, s * 1.4f, 0);
            }
            g.Restore(st);
        }

        /// <summary>월드 바깥의 빽빽한 나무 (그려지는 건 뷰포트에 걸린 칸뿐).</summary>
        public void DrawBorderTrees(IGfx g, ViewState v, TreeRenderer trees, DesertProps props, BiomeProps biome, float minX, float minY, float maxX, float maxY)
        {
            bool desert = ZoneTheme.Current == MapKind.Desert;
            float step = 4f;
            float wx0 = v.ToWorldX(-80), wy0 = v.ToWorldY(-80), wx1 = v.ToWorldX(v.Width + 80), wy1 = v.ToWorldY(v.Height + 80);
            // 월드 안쪽만 보이면 건너뛴다
            if (wx0 >= minX - 1 && wx1 <= maxX + 1 && wy0 >= minY - 1 && wy1 <= maxY + 1) return;
            int cx0 = (int)Math.Floor(wx0 / step), cx1 = (int)Math.Ceiling(wx1 / step);
            int cy0 = (int)Math.Floor(wy0 / step), cy1 = (int)Math.Ceiling(wy1 / step);
            for (int cy = cy0; cy <= cy1; ++cy)
            {
                for (int cx = cx0; cx <= cx1; ++cx)
                {
                    float wx = cx * step + Noise.Hash01(cx, cy, 5) * step, wy = cy * step + Noise.Hash01(cx, cy, 6) * step;
                    if (wx >= minX - 1 && wx <= maxX + 1 && wy >= minY - 1 && wy <= maxY + 1) continue;
                    if (ZoneGates.Current.Count > 0) { MapKind gk; bool go; if (ZoneGates.Weight(ZoneGates.Current, wx, wy, minX, minY, maxX, maxY, out gk, out go) > 0.05f && go) continue; }     // 관문 바깥 길에는 나무를 세우지 않는다
                    float r = 1.8f + Noise.Hash01(cx, cy, 8) * 1.6f;
                    if (desert) props.DrawMesa(g, v, wx, wy, r * 1.25f, Noise.Hash(cx, cy, 9));
                    else if (ZoneTheme.Current == MapKind.Coast) { if (Noise.Hash01(cx, cy, 11) < 0.12f) biome.DrawBorder(g, v, ZoneTheme.Current, wx, wy, r * 1.25f, Noise.Hash(cx, cy, 9)); }
                    else if (ZoneTheme.Current != MapKind.Forest) biome.DrawBorder(g, v, ZoneTheme.Current, wx, wy, r * 1.25f, Noise.Hash(cx, cy, 9));
                    else trees.Draw(g, v, wx, wy, r, Noise.Hash(cx, cy, 9), true);
                }
            }
        }
    }
}
