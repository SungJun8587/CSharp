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

namespace GameViewer.App.Render
{
    public sealed class GroundRenderer : IDisposable
    {
        private const int Step = 6;                    // 바탕 계산 해상도 (화면 px 당 1/Step)
        private const int MarginPx = 320;
        private const float DetailCell = 1.5f;         // 장식 격자 한 칸 (월드 unit)

        private static readonly Color GrassDark = Color.FromArgb(34, 78, 34), GrassLight = Color.FromArgb(104, 150, 62);
        private static readonly Color Moss = Color.FromArgb(62, 112, 56), Dirt = Color.FromArgb(112, 90, 60), DirtDark = Color.FromArgb(88, 70, 48);
        private static readonly Color LitterA = Color.FromArgb(124, 88, 48), LitterB = Color.FromArgb(150, 104, 44);
        private static readonly Color Shade = Color.FromArgb(10, 30, 26), Sun = Color.FromArgb(236, 232, 150);
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

        public void Draw(Graphics g, ViewState v, float minX, float minY, float maxX, float maxY)
        {
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
            using (Graphics bg = Graphics.FromImage(target))
            {
                bg.SetClip(new Rectangle(0, y0, bw, y1 - y0));
                RenderBase(bg, bv, y0, y1, minX, minY, maxX, maxY);
                RenderDetails(bg, bv, y0, y1, minX, minY, maxX, maxY);
            }
        }

        // ------------------------------------------------------------------ 1) 바탕

        private static float Smooth(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            if (t <= 0) return 0; if (t >= 1) return 1;
            return t * t * (3 - 2 * t);
        }

        private static void Mix(ref float r, ref float g, ref float b, Color c, float t)
        {
            r += (c.R - r) * t; g += (c.G - g) * t; b += (c.B - b) * t;
        }

        /// <summary>월드 좌표 한 점의 바닥 색 (바탕 한 겹). 장식이 이 값을 참조해 흙길 위를 피한다.</summary>
        private static void BaseColor(float wx, float wy, float minX, float minY, float maxX, float maxY, out float r, out float g, out float b, out float dirtT)
        {
            dirtT = 0;
            float ex = Math.Max(Math.Max(minX - wx, wx - maxX), 0f), ey = Math.Max(Math.Max(minY - wy, wy - maxY), 0f);
            float outside = Math.Max(ex, ey);
            if (outside >= 0.8f) { r = Outside.R; g = Outside.G; b = Outside.B; return; }

            float n = Noise.Value(wx * 0.045f, wy * 0.045f, 7) * 0.65f + Noise.Value(wx * 0.17f, wy * 0.17f, 11) * 0.35f;
            r = GrassDark.R + (GrassLight.R - GrassDark.R) * n;
            g = GrassDark.G + (GrassLight.G - GrassDark.G) * n;
            b = GrassDark.B + (GrassLight.B - GrassDark.B) * n;

            float grain = (Noise.Value(wx * 0.9f, wy * 0.9f, 5) - 0.5f) * 0.16f + 1f;
            r *= grain; g *= grain; b *= grain;

            float moss = Smooth(0.52f, 0.78f, Noise.Value(wx * 0.06f + 3f, wy * 0.06f + 8f, 23));
            if (moss > 0) Mix(ref r, ref g, ref b, Moss, moss * 0.55f);

            float litter = Smooth(0.60f, 0.78f, Noise.Value(wx * 0.11f + 50f, wy * 0.11f + 7f, 31));
            if (litter > 0)
            {
                Color lc = Noise.Value(wx * 1.3f, wy * 1.3f, 37) > 0.55f ? LitterB : LitterA;
                Mix(ref r, ref g, ref b, lc, litter * 0.62f);
            }

            dirtT = Smooth(0.66f, 0.80f, Noise.Value(wx * 0.03f + 40f, wy * 0.03f + 90f, 19));
            if (dirtT > 0)
            {
                float dg = Noise.Value(wx * 1.1f, wy * 1.1f, 43);
                Color dc = Color.FromArgb((int)(DirtDark.R + (Dirt.R - DirtDark.R) * dg), (int)(DirtDark.G + (Dirt.G - DirtDark.G) * dg), (int)(DirtDark.B + (Dirt.B - DirtDark.B) * dg));
                Mix(ref r, ref g, ref b, dc, dirtT * 0.9f);
            }

            // 햇빛이 드는 얼룩과 나무 그늘
            float sun = Smooth(0.64f, 0.82f, Noise.Value(wx * 0.08f + 7f, wy * 0.08f + 70f, 53));
            if (sun > 0) Mix(ref r, ref g, ref b, Sun, sun * 0.20f);
            float shade = Smooth(0.50f, 0.76f, Noise.Value(wx * 0.04f + 100f, wy * 0.04f + 20f, 41)) * 0.34f
                        + Smooth(0.62f, 0.80f, Noise.Value(wx * 0.14f + 5f, wy * 0.14f + 60f, 47)) * 0.12f;
            if (shade > 0) Mix(ref r, ref g, ref b, Shade, shade);

            // 월드 안쪽 가장자리는 점점 어두워지고 바깥 숲으로 이어진다
            float inEdge = Math.Min(Math.Min(wx - minX, maxX - wx), Math.Min(wy - minY, maxY - wy));
            if (inEdge < 5f) Mix(ref r, ref g, ref b, Shade, (1f - Math.Max(inEdge, 0f) / 5f) * 0.40f);
            if (outside > 0) Mix(ref r, ref g, ref b, Outside, outside / 0.8f);
        }

        private void RenderBase(Graphics g, ViewState v, int y0, int y1, float minX, float minY, float maxX, float maxY)
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
            finally { _low.UnlockBits(bd); }

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

        private void RenderDetails(Graphics g, ViewState v, int y0, int y1, float minX, float minY, float maxX, float maxY)
        {
            float scale = (float)v.Scale;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

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
                        if (kind < 8) { if (dirtT < 0.5f) Fern(g, sx, sy, scale, h); }
                        else if (kind < 12) { if (dirtT < 0.4f && scale >= 8) Bush(g, sx, sy, scale, h); }
                        else if (kind < 14) { if (scale >= 9) Mushrooms(g, sx, sy, scale, h); }
                        else if (kind < 15) { if (scale >= 6) Log(g, sx, sy, scale, h); }
                        else if (kind < 25) Pebbles(g, sx, sy, scale, h);
                        else if (kind < 45) Leaves(g, sx, sy, scale, h);
                    }
                }
            }

            // 풀포기/꽃 (확대했을 때만)
            if (scale >= 9)
            {
                float wx0 = v.ToWorldX(0), wy0 = v.ToWorldY(y0 - 40), wx1 = v.ToWorldX(v.Width), wy1 = v.ToWorldY(y1 + 40);
                int cx0 = (int)Math.Floor(wx0) - 1, cx1 = (int)Math.Ceiling(wx1) + 1, cy0 = (int)Math.Floor(wy0) - 1, cy1 = (int)Math.Ceiling(wy1) + 1;
                for (int cy = cy0; cy <= cy1; ++cy)
                {
                    for (int cx = cx0; cx <= cx1; ++cx)
                    {
                        if (cx < minX || cy < minY || cx + 1 > maxX || cy + 1 > maxY) continue;
                        uint h = Noise.Hash(cx, cy, 99);
                        float sx = v.ToScreenX(cx + ((h >> 4) & 255) / 255f);
                        float sy = v.ToScreenY(cy + ((h >> 12) & 255) / 255f);
                        if ((h & 3) == 0)
                        {
                            float s = scale * 0.35f;
                            g.DrawLine(_tuftDark, sx, sy, sx - s * 0.5f, sy - s);
                            g.DrawLine(_tuftLight, sx, sy, sx + s * 0.1f, sy - s * 1.2f);
                            g.DrawLine(_tuftDark, sx, sy, sx + s * 0.6f, sy - s * 0.9f);
                        }
                        else if (h % 97 == 0)
                        {
                            SolidBrush fb = _flowers[(h >> 8) % (uint)_flowers.Length];
                            g.FillEllipse(fb, sx - 2.5f, sy - 2.5f, 5, 5);
                            g.FillEllipse(_flowers[0], sx - 1f, sy - 1f, 2, 2);
                        }
                    }
                }
            }
        }

        private static float Rnd(uint h, int shift) { return ((h >> shift) & 255) / 255f; }

        private static void Shadow(Graphics g, float x, float y, float rx, float ry, int alpha)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 6, 20, 12))) g.FillEllipse(b, x - rx, y - ry, rx * 2, ry * 2);
        }

        private static void Fern(Graphics g, float x, float y, float scale, uint h)
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

        private static void Bush(Graphics g, float x, float y, float scale, uint h)
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

        private static void Mushrooms(Graphics g, float x, float y, float scale, uint h)
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

        private static void Log(Graphics g, float x, float y, float scale, uint h)
        {
            float len = scale * (1.4f + Rnd(h, 2) * 1.2f), rad = scale * (0.2f + Rnd(h, 5) * 0.08f);
            float ang = (Rnd(h, 11) - 0.5f) * 50f;
            GraphicsState st = g.Save();
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

        private static void Pebbles(Graphics g, float x, float y, float scale, uint h)
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

        private static void Leaves(Graphics g, float x, float y, float scale, uint h)
        {
            int n = 3 + (int)(h % 4);
            GraphicsState st = g.Save();
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
        public void DrawBorderTrees(Graphics g, ViewState v, TreeRenderer trees, float minX, float minY, float maxX, float maxY)
        {
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
                    float r = 1.8f + Noise.Hash01(cx, cy, 8) * 1.6f;
                    trees.Draw(g, v, wx, wy, r, Noise.Hash(cx, cy, 9), true);
                }
            }
        }
    }
}
