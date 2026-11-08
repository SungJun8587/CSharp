//***************************************************************************
// Gfx.cs : 렌더러가 공통으로 쓰는 그리기 도우미(구형 셰이딩, 둥근 사각형, 광원 번짐, 스프라이트 캐시).
// 캐릭터/나무 같은 "한 번 그려두고 계속 찍어내는" 그림은 SpriteCache 에 비트맵으로 보관해 프레임당 비용을 줄인다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GameViewer.App.Render
{
    /// <summary>색 계산.</summary>
    public static class Col
    {
        public static Color Scale(Color c, float f)
        {
            return Color.FromArgb(c.A, Cl((int)(c.R * f)), Cl((int)(c.G * f)), Cl((int)(c.B * f)));
        }
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(Cl((int)(a.A + (b.A - a.A) * t)), Cl((int)(a.R + (b.R - a.R) * t)), Cl((int)(a.G + (b.G - a.G) * t)), Cl((int)(a.B + (b.B - a.B) * t)));
        }
        public static Color Alpha(Color c, int a) { return Color.FromArgb(Cl(a), c.R, c.G, c.B); }
        public static Color Light(Color c, float t) { return Mix(c, Color.White, t); }
        public static Color Dark(Color c, float t) { return Mix(c, Color.Black, t); }
        public static int Cl(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }
    }

    /// <summary>
    /// 단위(unit) 좌표로 그리는 도형들. 호출하는 쪽이 g.ScaleTransform(ppu, ppu) 를 걸어 둔다고 가정한다.
    /// 브러시/펜은 만들자마자 해제하므로 스프라이트를 처음 그릴 때(캐시 미스)만 쓰인다 — 프레임마다 호출하지 않는다.
    /// </summary>
    public static class P
    {
        public static GraphicsPath EllipsePath(float cx, float cy, float rx, float ry)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
            return p;
        }

        /// <summary>입체감 있는 타원: 왼쪽 위에서 빛을 받는 방사형 셰이딩 + 얇은 외곽선.</summary>
        public static void Ball(Graphics g, float cx, float cy, float rx, float ry, Color light, Color dark, Color outline, float lw)
        {
            using (GraphicsPath p = EllipsePath(cx, cy, rx, ry))
            {
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterPoint = new PointF(cx - rx * 0.32f, cy - ry * 0.38f);
                    b.CenterColor = light;
                    b.SurroundColors = new Color[] { dark };
                    g.FillPath(b, p);
                }
                if (lw > 0) using (Pen pen = new Pen(outline, lw)) g.DrawPath(pen, p);
            }
        }

        public static void Poly(Graphics g, PointF[] pts, Color top, Color bottom, Color outline, float lw)
        {
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue;
            for (int i = 0; i < pts.Length; ++i) { minY = Math.Min(minY, pts[i].Y); maxY = Math.Max(maxY, pts[i].Y); minX = Math.Min(minX, pts[i].X); }
            if (maxY - minY < 0.001f) maxY = minY + 0.01f;
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(minX, minY), new PointF(minX, maxY), top, bottom))
                g.FillPolygon(b, pts);
            if (lw > 0) using (Pen pen = new Pen(outline, lw) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, pts);
        }

        public static GraphicsPath RoundPath(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            r = Math.Min(r, Math.Min(w, h) / 2f);
            if (r <= 0.001f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Round(Graphics g, float x, float y, float w, float h, float r, Color top, Color bottom, Color outline, float lw)
        {
            using (GraphicsPath p = RoundPath(x, y, w, h, r))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(x, y), new PointF(x, y + h + 0.001f), top, bottom))
                    g.FillPath(b, p);
                if (lw > 0) using (Pen pen = new Pen(outline, lw)) g.DrawPath(pen, p);
            }
        }

        /// <summary>굵은 선(팔다리/무기 자루). 어두운 외곽선 위에 본색을 덧그린다.</summary>
        public static void Limb(Graphics g, float x0, float y0, float x1, float y1, float w, Color c, Color outline)
        {
            using (Pen o = new Pen(outline, w + 0.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(o, x0, y0, x1, y1);
            using (Pen p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(p, x0, y0, x1, y1);
        }

        public static void Line(Graphics g, float x0, float y0, float x1, float y1, float w, Color c)
        {
            using (Pen p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(p, x0, y0, x1, y1);
        }

        public static void Fill(Graphics g, float cx, float cy, float rx, float ry, Color c)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - rx, cy - ry, rx * 2, ry * 2);
        }

        /// <summary>눈: 흰자 + 동공. look 은 -1..1 (바라보는 쪽).</summary>
        public static void Eye(Graphics g, float cx, float cy, float r, float look, Color pupil)
        {
            Fill(g, cx, cy, r, r * 1.1f, Color.FromArgb(250, 250, 245));
            Fill(g, cx + look * r * 0.35f, cy + r * 0.05f, r * 0.55f, r * 0.62f, pupil);
            Fill(g, cx + look * r * 0.35f - r * 0.18f, cy - r * 0.22f, r * 0.18f, r * 0.18f, Color.FromArgb(220, 255, 255, 255));
        }

        /// <summary>중심이 밝고 가장자리가 투명한 광원 번짐.</summary>
        public static void Glow(Graphics g, float cx, float cy, float r, Color c, int centerAlpha)
        {
            using (GraphicsPath p = EllipsePath(cx, cy, r, r))
            using (PathGradientBrush b = new PathGradientBrush(p))
            {
                b.CenterColor = Col.Alpha(c, centerAlpha);
                b.SurroundColors = new Color[] { Col.Alpha(c, 0) };
                g.FillPath(b, p);
            }
        }
    }

    /// <summary>
    /// 한 번 그려둔 그림(비트맵) 보관소. 키가 같으면 다시 그리지 않는다. 가득 차면 가장 오래 안 쓴 것부터 버린다.
    /// 그림은 "단위 좌표 * ppu" 로 그려지며, 기준점(ax, ay)은 비트맵 안의 픽셀 위치다.
    /// </summary>
    public sealed class SpriteCache : IDisposable
    {
        public sealed class Entry { public Bitmap Bmp; public float Ax, Ay; public long Used; }
        public delegate void PaintFn(Graphics g);

        private readonly Dictionary<long, Entry> _map = new Dictionary<long, Entry>();
        private readonly int _cap;
        private long _tick;
        public int Hits, Misses;

        public SpriteCache(int capacity) { _cap = capacity; }
        public int Count { get { return _map.Count; } }

        public Entry Get(long key, int w, int h, float ax, float ay, PaintFn paint)
        {
            Entry e;
            if (_map.TryGetValue(key, out e)) { e.Used = ++_tick; ++Hits; return e; }
            ++Misses;
            if (_map.Count >= _cap) Trim();
            w = Math.Max(2, Math.Min(2048, w)); h = Math.Max(2, Math.Min(2048, h));
            e = new Entry(); e.Ax = ax; e.Ay = ay; e.Used = ++_tick;
            e.Bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(e.Bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(ax, ay);
                paint(g);
            }
            _map[key] = e;
            return e;
        }

        /// <summary>이미 있는 항목을 바탕으로 파생 그림(번쩍임/시체)을 만든다.</summary>
        public Entry GetDerived(long key, Entry src, int mode)
        {
            Entry e;
            if (_map.TryGetValue(key, out e)) { e.Used = ++_tick; ++Hits; return e; }
            ++Misses;
            if (_map.Count >= _cap) Trim();
            e = new Entry(); e.Ax = src.Ax; e.Ay = src.Ay; e.Used = ++_tick;
            e.Bmp = Tint(src.Bmp, mode);
            _map[key] = e;
            return e;
        }

        /// <summary>mode 1: 흰색으로 번쩍임, 2: 시체(회색조로 어둡게), 3: 색을 유지한 채 어둡게.</summary>
        public static Bitmap Tint(Bitmap src, int mode)
        {
            int w = src.Width, h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData sd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dd = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int n = w * h;
                int[] px = new int[n];
                // 줄 간격(stride)이 폭과 다를 수 있으니 한 줄씩 복사한다
                for (int y = 0; y < h; ++y) Marshal.Copy(new IntPtr(sd.Scan0.ToInt64() + (long)y * sd.Stride), px, y * w, w);
                for (int i = 0; i < n; ++i)
                {
                    int p = px[i];
                    int a = (p >> 24) & 255, r = (p >> 16) & 255, gg = (p >> 8) & 255, b = p & 255;
                    if (a == 0) continue;
                    if (mode == 1) { r = Col.Cl(r + 120); gg = Col.Cl(gg + 120); b = Col.Cl(b + 120); }
                    else if (mode == 3) { r = r * 56 / 100; gg = gg * 64 / 100; b = b * 62 / 100; }      // 어둡게(월드 바깥 숲)
                    else
                    {
                        int l = (r * 30 + gg * 59 + b * 11) / 100;
                        l = l * 62 / 100;
                        r = Col.Cl(l + 6); gg = Col.Cl(l); b = Col.Cl(l - 4);
                    }
                    px[i] = (a << 24) | (r << 16) | (gg << 8) | b;
                }
                for (int y = 0; y < h; ++y) Marshal.Copy(px, y * w, new IntPtr(dd.Scan0.ToInt64() + (long)y * dd.Stride), w);
            }
            finally { src.UnlockBits(sd); dst.UnlockBits(dd); }
            return dst;
        }

        private void Trim()
        {
            // 가장 오래 안 쓴 4분의 1을 버린다 (드물게 일어나므로 정렬해도 부담 없다)
            List<KeyValuePair<long, Entry>> list = new List<KeyValuePair<long, Entry>>(_map);
            list.Sort(delegate (KeyValuePair<long, Entry> a, KeyValuePair<long, Entry> b) { return a.Value.Used.CompareTo(b.Value.Used); });
            int drop = Math.Max(1, list.Count / 4);
            for (int i = 0; i < drop; ++i) { list[i].Value.Bmp.Dispose(); _map.Remove(list[i].Key); }
        }

        public void Clear()
        {
            foreach (Entry e in _map.Values) e.Bmp.Dispose();
            _map.Clear();
        }

        public void Dispose() { Clear(); }
    }
}
