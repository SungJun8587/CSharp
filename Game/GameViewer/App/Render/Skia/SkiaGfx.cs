//***************************************************************************
// SkiaGfx.cs : IGfx 의 SkiaSharp 구현. GDI+ 형식의 펜/브러시/경로/글꼴/이미지를 읽어 Skia 로 그린다.
// UseSkia=true 로 빌드할 때만 컴파일된다(GameViewer.App.csproj). 기본 빌드/렌더러는 GdiGfx 그대로다.
//
// 이 구현이 GDI+ 와 다르게 동작하는 곳(알고 있는 근사):
//  - SetClip 의 Replace 는 Intersect 로 처리한다(Skia 는 클립을 넓힐 수 없다). 현재 쓰임에서는 결과가 같다.
//  - MeasureString 은 글자 폭에 글자 크기의 0.3 배를 더한다(GDI+ 의 좌우 여백 근사).
//  - PathGradientBrush 는 경계 사각형 안의 타원형 방사 그라디언트로 근사한다.
//  - 선형 그라디언트는 GfxBrush.Linear 로 만든 것만 방향을 안다(직접 new 한 것은 위->아래로 본다).
//  - ImageAttributes 는 GfxAttr.SetColorMatrix 로 설정한 색 행렬만 적용한다. 행렬 마지막 줄(이동)은 0..1 단위로 넘긴다.
//  - 비트맵은 처음 그릴 때와 GfxImages.Touch 로 바뀐 것이 표시된 뒤에만 다시 올린다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace GameViewer.App.Render
{
    public sealed class SkiaGfx : IGfx
    {
        private SKCanvas _c;
        private readonly SKPaint _paint = new SKPaint();
        private readonly Stack<int> _saves = new Stack<int>();
        private readonly ConditionalWeakTable<Font, SKFont> _fonts = new ConditionalWeakTable<Font, SKFont>();
        private readonly Dictionary<string, SKTypeface> _faces = new Dictionary<string, SKTypeface>();
        private readonly ConditionalWeakTable<Image, ImgEntry> _images = new ConditionalWeakTable<Image, ImgEntry>();

        private sealed class ImgEntry { public SKImage Image; public int Version = -1, W, H; }

        public SkiaGfx() { SmoothingMode = SmoothingMode.AntiAlias; InterpolationMode = InterpolationMode.Bilinear; }

        /// <summary>그릴 캔버스를 정한다(프레임마다). 이전 프레임의 저장 상태는 버린다.</summary>
        public void Begin(SKCanvas canvas)
        {
            _c = canvas;
            _saves.Clear();
        }

        // ------------------------------------------------------------ 품질 상태

        public SmoothingMode SmoothingMode { get; set; }
        public InterpolationMode InterpolationMode { get; set; }
        public CompositingMode CompositingMode { get; set; }
        public PixelOffsetMode PixelOffsetMode { get; set; }
        public TextRenderingHint TextRenderingHint { get; set; }

        private bool Aa { get { return SmoothingMode == SmoothingMode.AntiAlias || SmoothingMode == SmoothingMode.HighQuality; } }

        private SKFilterQuality Quality
        {
            get
            {
                switch (InterpolationMode)
                {
                    case InterpolationMode.NearestNeighbor: return SKFilterQuality.None;
                    case InterpolationMode.HighQualityBicubic: return SKFilterQuality.High;
                    case InterpolationMode.Bicubic: case InterpolationMode.High: case InterpolationMode.HighQualityBilinear: return SKFilterQuality.Medium;
                    default: return SKFilterQuality.Low;
                }
            }
        }

        // ------------------------------------------------------------ 변환 / 클리핑

        public float[] GetTransformElements()
        {
            SKMatrix m = _c.TotalMatrix;
            return new float[] { m.ScaleX, m.SkewY, m.SkewX, m.ScaleY, m.TransX, m.TransY };
        }

        public GfxState Save() { int n = _c.Save(); return new GfxState(n); }
        public void Restore(GfxState state) { _c.RestoreToCount((int)state.Token); }
        public void ResetTransform() { _c.ResetMatrix(); }
        public void TranslateTransform(float dx, float dy) { _c.Translate(dx, dy); }
        public void RotateTransform(float degrees) { _c.RotateDegrees(degrees); }
        public void ScaleTransform(float sx, float sy) { _c.Scale(sx, sy); }

        public void SetClip(RectangleF r, CombineMode mode)
        {
            _c.ClipRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom), mode == CombineMode.Exclude ? SKClipOperation.Difference : SKClipOperation.Intersect, Aa);
        }

        public void SetClip(GraphicsPath path, CombineMode mode)
        {
            using (SKPath p = ToSkPath(path)) _c.ClipPath(p, mode == CombineMode.Exclude ? SKClipOperation.Difference : SKClipOperation.Intersect, Aa);
        }

        // ------------------------------------------------------------ 색 / 펜 / 브러시

        private static SKColor Sk(Color c) { return new SKColor(c.R, c.G, c.B, c.A); }

        private SKShader _shader;      // 직전 ApplyBrush 가 만든 셰이더(그린 뒤 닫는다)

        /// <summary>_paint 를 채우기 모양으로 만든다. 그라디언트는 셰이더로 건다.</summary>
        private SKPaint FillPaint(Brush brush)
        {
            _paint.Reset();
            _paint.IsAntialias = Aa;
            _paint.Style = SKPaintStyle.Fill;
            if (CompositingMode == CompositingMode.SourceCopy) _paint.BlendMode = SKBlendMode.Src;
            ApplyBrush(brush);
            return _paint;
        }

        private SKPaint StrokePaint(Pen pen)
        {
            _paint.Reset();
            _paint.IsAntialias = Aa;
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = pen.Width <= 0 ? 1f : pen.Width;
            if (CompositingMode == CompositingMode.SourceCopy) _paint.BlendMode = SKBlendMode.Src;
            switch (pen.StartCap) { case LineCap.Round: _paint.StrokeCap = SKStrokeCap.Round; break; case LineCap.Square: _paint.StrokeCap = SKStrokeCap.Square; break; default: _paint.StrokeCap = SKStrokeCap.Butt; break; }
            switch (pen.LineJoin) { case LineJoin.Round: _paint.StrokeJoin = SKStrokeJoin.Round; break; case LineJoin.Bevel: _paint.StrokeJoin = SKStrokeJoin.Bevel; break; default: _paint.StrokeJoin = SKStrokeJoin.Miter; _paint.StrokeMiter = Math.Max(1f, pen.MiterLimit); break; }
            if (pen.DashStyle != DashStyle.Solid)
            {
                float w = _paint.StrokeWidth;
                float[] pat;
                switch (pen.DashStyle)
                {
                    case DashStyle.Dash: pat = new float[] { 3 * w, w }; break;
                    case DashStyle.Dot: pat = new float[] { w, w }; break;
                    case DashStyle.DashDot: pat = new float[] { 3 * w, w, w, w }; break;
                    case DashStyle.DashDotDot: pat = new float[] { 3 * w, w, w, w, w, w }; break;
                    default: pat = pen.DashPattern != null && pen.DashPattern.Length >= 2 ? ScaleDash(pen.DashPattern, w) : null; break;
                }
                if (pat != null) _paint.PathEffect = SKPathEffect.CreateDash(pat, pen.DashOffset * w);
            }
            _paint.Color = Sk(pen.Color);
            return _paint;
        }

        private static float[] ScaleDash(float[] p, float w)
        {
            float[] r = new float[(p.Length / 2) * 2];
            for (int i = 0; i < r.Length; ++i) r[i] = p[i] * w;
            return r;
        }

        private void ApplyBrush(Brush brush)
        {
            SolidBrush sb = brush as SolidBrush;
            if (sb != null) { _paint.Color = Sk(sb.Color); return; }
            LinearGradientBrush lb = brush as LinearGradientBrush;
            if (lb != null) { _paint.Color = SKColors.White; _paint.Shader = LinearShader(lb); return; }
            PathGradientBrush pb = brush as PathGradientBrush;
            if (pb != null) { _paint.Color = SKColors.White; _paint.Shader = RadialShader(pb); return; }
            HatchBrush hb = brush as HatchBrush;
            _paint.Color = hb != null ? Sk(hb.ForegroundColor) : new SKColor(255, 0, 255, 128);          // 지원하지 않는 브러시는 눈에 띄는 색
        }

        /// <summary>색 블렌드가 따로 설정됐는가(설정 안 된 브러시의 기본 값은 길이 1 짜리라서 제외).</summary>
        private static bool HasBlend(ColorBlend cb)
        {
            return cb != null && cb.Colors != null && cb.Positions != null && cb.Colors.Length >= 2 && cb.Colors.Length == cb.Positions.Length;
        }

        private static SKShader LinearShader(LinearGradientBrush b)
        {
            GfxBrush.Line l = GfxBrush.LineOf(b);
            SKColor[] colors; float[] pos;
            ColorBlend cb = null;
            try { cb = b.InterpolationColors; } catch (Exception) { }
            if (HasBlend(cb)) { colors = new SKColor[cb.Colors.Length]; for (int i = 0; i < colors.Length; ++i) colors[i] = Sk(cb.Colors[i]); pos = cb.Positions; }
            else { Color[] lc = b.LinearColors; colors = new SKColor[] { Sk(lc[0]), Sk(lc[1]) }; pos = new float[] { 0f, 1f }; }
            if (Math.Abs(l.X1 - l.X2) < 1e-4f && Math.Abs(l.Y1 - l.Y2) < 1e-4f) return SKShader.CreateColor(colors[0]);
            return SKShader.CreateLinearGradient(new SKPoint(l.X1, l.Y1), new SKPoint(l.X2, l.Y2), colors, pos, SKShaderTileMode.Clamp);
        }

        private static SKShader RadialShader(PathGradientBrush b)
        {
            RectangleF r = b.Rectangle;
            PointF c = b.CenterPoint;
            float radius = Math.Max(r.Width, r.Height) / 2f;
            if (radius < 1e-3f) return SKShader.CreateColor(Sk(b.CenterColor));
            // GDI+ 의 경로 그라디언트는 위치 0 = 경계, 1 = 중심이다. Skia 방사형은 0 = 중심이라 뒤집는다.
            SKColor[] colors; float[] pos;
            ColorBlend cb = null;
            try { cb = b.InterpolationColors; } catch (Exception) { }
            if (HasBlend(cb))
            {
                int n = cb.Colors.Length;
                colors = new SKColor[n]; pos = new float[n];
                for (int i = 0; i < n; ++i) { colors[i] = Sk(cb.Colors[n - 1 - i]); pos[i] = 1f - cb.Positions[n - 1 - i]; }
            }
            else
            {
                Color[] sc = b.SurroundColors;
                Color edge = sc != null && sc.Length > 0 ? sc[0] : Color.Transparent;
                colors = new SKColor[] { Sk(b.CenterColor), Sk(edge) }; pos = new float[] { 0f, 1f };
            }
            float sx = (r.Width / 2f) / radius, sy = (r.Height / 2f) / radius;
            SKMatrix m = SKMatrix.CreateScale(sx, sy, c.X, c.Y);
            return SKShader.CreateRadialGradient(new SKPoint(c.X, c.Y), radius, colors, pos, SKShaderTileMode.Clamp, m);
        }

        /// <summary>그린 뒤에 호출해 이번 호출의 셰이더/경로 효과를 닫는다.</summary>
        private void Done()
        {
            SKShader s = _paint.Shader; if (s != null) { _paint.Shader = null; s.Dispose(); }
            SKPathEffect e = _paint.PathEffect; if (e != null) { _paint.PathEffect = null; e.Dispose(); }
            SKColorFilter f = _paint.ColorFilter; if (f != null) { _paint.ColorFilter = null; f.Dispose(); }
        }

        // ------------------------------------------------------------ 도형

        public void Clear(Color color) { _c.Clear(Sk(color)); }

        public void DrawLine(Pen pen, float x1, float y1, float x2, float y2) { _c.DrawLine(x1, y1, x2, y2, StrokePaint(pen)); Done(); }

        public void DrawLines(Pen pen, PointF[] pts)
        {
            if (pts.Length < 2) return;
            using (SKPath p = Poly(pts, false)) _c.DrawPath(p, StrokePaint(pen));
            Done();
        }

        public void DrawRectangle(Pen pen, float x, float y, float w, float h) { _c.DrawRect(new SKRect(x, y, x + w, y + h), StrokePaint(pen)); Done(); }
        public void FillRectangle(Brush brush, float x, float y, float w, float h) { _c.DrawRect(new SKRect(x, y, x + w, y + h), FillPaint(brush)); Done(); }
        public void DrawEllipse(Pen pen, float x, float y, float w, float h) { _c.DrawOval(new SKRect(x, y, x + w, y + h), StrokePaint(pen)); Done(); }
        public void FillEllipse(Brush brush, float x, float y, float w, float h) { _c.DrawOval(new SKRect(x, y, x + w, y + h), FillPaint(brush)); Done(); }
        public void DrawArc(Pen pen, float x, float y, float w, float h, float startAngle, float sweepAngle) { _c.DrawArc(new SKRect(x, y, x + w, y + h), startAngle, sweepAngle, false, StrokePaint(pen)); Done(); }
        public void FillPie(Brush brush, float x, float y, float w, float h, float startAngle, float sweepAngle) { _c.DrawArc(new SKRect(x, y, x + w, y + h), startAngle, sweepAngle, true, FillPaint(brush)); Done(); }

        public void DrawPolygon(Pen pen, PointF[] pts)
        {
            if (pts.Length < 2) return;
            using (SKPath p = Poly(pts, true)) _c.DrawPath(p, StrokePaint(pen));
            Done();
        }

        public void FillPolygon(Brush brush, PointF[] pts)
        {
            if (pts.Length < 3) return;
            using (SKPath p = Poly(pts, true)) { p.FillType = SKPathFillType.EvenOdd; _c.DrawPath(p, FillPaint(brush)); }
            Done();
        }

        public void DrawPath(Pen pen, GraphicsPath path) { using (SKPath p = ToSkPath(path)) _c.DrawPath(p, StrokePaint(pen)); Done(); }
        public void FillPath(Brush brush, GraphicsPath path) { using (SKPath p = ToSkPath(path)) _c.DrawPath(p, FillPaint(brush)); Done(); }

        public void DrawCurve(Pen pen, PointF[] pts, float tension)
        {
            if (pts.Length < 2) return;
            using (SKPath p = Curve(pts, tension)) _c.DrawPath(p, StrokePaint(pen));
            Done();
        }

        public void DrawBezier(Pen pen, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
        {
            using (SKPath p = new SKPath()) { p.MoveTo(x1, y1); p.CubicTo(x2, y2, x3, y3, x4, y4); _c.DrawPath(p, StrokePaint(pen)); }
            Done();
        }

        private static SKPath Poly(PointF[] pts, bool close)
        {
            SKPath p = new SKPath();
            p.MoveTo(pts[0].X, pts[0].Y);
            for (int i = 1; i < pts.Length; ++i) p.LineTo(pts[i].X, pts[i].Y);
            if (close) p.Close();
            return p;
        }

        /// <summary>GDI+ 의 열린 카디널 스플라인(tension)을 3차 베지어로 바꾼다.</summary>
        private static SKPath Curve(PointF[] pts, float tension)
        {
            SKPath p = new SKPath();
            int n = pts.Length;
            p.MoveTo(pts[0].X, pts[0].Y);
            float k = tension / 3f;
            for (int i = 0; i < n - 1; ++i)
            {
                PointF p0 = i > 0 ? pts[i - 1] : pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = i + 2 < n ? pts[i + 2] : pts[i + 1];
                p.CubicTo(p1.X + (p2.X - p0.X) * k, p1.Y + (p2.Y - p0.Y) * k, p2.X - (p3.X - p1.X) * k, p2.Y - (p3.Y - p1.Y) * k, p2.X, p2.Y);
            }
            return p;
        }

        /// <summary>GraphicsPath 의 점/종류 배열을 Skia 경로로 바꾼다(0 시작, 1 직선, 3 베지어(3점씩), 0x80 도형 닫기).</summary>
        private static SKPath ToSkPath(GraphicsPath gp)
        {
            SKPath p = new SKPath();
            p.FillType = gp.FillMode == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd;
            PointF[] pts = gp.PathPoints; byte[] types = gp.PathTypes;
            int n = pts.Length;
            for (int i = 0; i < n; )
            {
                int t = types[i] & 7;
                if (t == 0) { p.MoveTo(pts[i].X, pts[i].Y); if ((types[i] & 0x80) != 0) p.Close(); ++i; }
                else if (t == 1) { p.LineTo(pts[i].X, pts[i].Y); if ((types[i] & 0x80) != 0) p.Close(); ++i; }
                else if (t == 3 && i + 2 < n)
                {
                    p.CubicTo(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, pts[i + 2].X, pts[i + 2].Y);
                    if ((types[i + 2] & 0x80) != 0) p.Close();
                    i += 3;
                }
                else ++i;
            }
            return p;
        }

        // ------------------------------------------------------------ 글자

        private SKTypeface Face(string name, bool bold, bool italic)
        {
            string key = name + "|" + (bold ? "b" : "") + (italic ? "i" : "");
            SKTypeface f;
            if (!_faces.TryGetValue(key, out f))
            {
                f = SKTypeface.FromFamilyName(name, bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
                if (f == null) f = SKTypeface.Default;
                _faces[key] = f;
            }
            return f;
        }

        private SKFont FontOf(Font font)
        {
            SKFont f;
            if (_fonts.TryGetValue(font, out f)) return f;
            float px = font.Unit == GraphicsUnit.Pixel ? font.Size : (font.Unit == GraphicsUnit.Point ? font.Size * 96f / 72f : font.Size);
            f = new SKFont(Face(font.Name, (font.Style & FontStyle.Bold) != 0, (font.Style & FontStyle.Italic) != 0), px);
            f.Edging = SKFontEdging.Antialias;
            f.Subpixel = true;
            _fonts.Add(font, f);
            return f;
        }

        public SizeF MeasureString(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return new SizeF(0, 0);
            SKFont f = FontOf(font);
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            float w = 0;
            foreach (string l in lines) w = Math.Max(w, f.MeasureText(l));
            return new SizeF(w + f.Size * 0.3f, lines.Length * f.Spacing);
        }

        public void DrawString(string text, Font font, Brush brush, RectangleF layout, StringFormat format) { Text(text, font, brush, layout, format, false); }
        public void DrawString(string text, Font font, Brush brush, float x, float y, StringFormat format) { Text(text, font, brush, new RectangleF(x, y, 0, 0), format, true); }

        private void Text(string text, Font font, Brush brush, RectangleF r, StringFormat fmt, bool pointMode)
        {
            if (string.IsNullOrEmpty(text)) return;
            SKFont f = FontOf(font);
            StringAlignment ha = fmt != null ? fmt.Alignment : StringAlignment.Near, va = fmt != null ? fmt.LineAlignment : StringAlignment.Near;
            bool noWrap = pointMode || (fmt != null && (fmt.FormatFlags & StringFormatFlags.NoWrap) != 0);
            StringTrimming trim = fmt != null ? fmt.Trimming : StringTrimming.Character;
            bool ellipsis = trim == StringTrimming.EllipsisCharacter || trim == StringTrimming.EllipsisWord || trim == StringTrimming.EllipsisPath;
            float lh = f.Spacing;

            List<string> lines = new List<string>();
            foreach (string para in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (noWrap || r.Width <= 0) lines.Add(noWrap && ellipsis && r.Width > 0 ? Ellipsize(f, para, r.Width) : para);
                else Wrap(f, para, r.Width, lines);
            }

            float total = lines.Count * lh;
            float top = va == StringAlignment.Near ? r.Y : (va == StringAlignment.Center ? r.Y + (r.Height - total) / 2f : r.Bottom - total);
            bool clip = !pointMode && r.Height > 0 && total > r.Height + 0.5f;
            if (clip) { _c.Save(); _c.ClipRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom)); }

            SKPaint p = FillPaint(brush);
            float asc = -f.Metrics.Ascent;
            for (int i = 0; i < lines.Count; ++i)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                float w = f.MeasureText(line);
                float x = ha == StringAlignment.Near ? r.X : (ha == StringAlignment.Center ? r.X + (r.Width - w) / 2f : r.Right - w);
                _c.DrawText(line, x, top + i * lh + asc, f, p);
            }
            Done();
            if (clip) _c.Restore();
        }

        private static string Ellipsize(SKFont f, string s, float width)
        {
            if (f.MeasureText(s) <= width) return s;
            const string dots = "…";
            float dw = f.MeasureText(dots);
            int n = s.Length;
            while (n > 0 && f.MeasureText(s.Substring(0, n)) + dw > width) --n;
            return s.Substring(0, n) + dots;
        }

        /// <summary>폭에 맞춰 줄바꿈한다(공백에서 우선 끊고, 한 단어가 너무 길면 글자 단위로 끊는다).</summary>
        private static void Wrap(SKFont f, string para, float width, List<string> output)
        {
            if (para.Length == 0 || f.MeasureText(para) <= width) { output.Add(para); return; }
            int start = 0;
            while (start < para.Length)
            {
                int end = start, lastSpace = -1;
                while (end < para.Length && f.MeasureText(para.Substring(start, end - start + 1)) <= width)
                {
                    if (para[end] == ' ') lastSpace = end;
                    ++end;
                }
                if (end >= para.Length) { output.Add(para.Substring(start)); break; }
                if (end == start) end = start + 1;                                // 한 글자도 안 들어가도 최소 한 글자는 넣는다
                else if (lastSpace > start) end = lastSpace + 1;
                output.Add(para.Substring(start, end - start).TrimEnd());
                start = end;
            }
        }

        // ------------------------------------------------------------ 이미지

        /// <summary>System.Drawing 이미지를 Skia 이미지로 올려 두고 재사용한다. GfxImages.Touch 로 바뀐 표시가 붙으면 다시 올린다.</summary>
        private SKImage ImageOf(Image image)
        {
            Bitmap bmp = image as Bitmap;
            if (bmp == null) return null;
            ImgEntry e = _images.GetOrCreateValue(image);
            int ver = GfxImages.Version(image);
            if (e.Image != null && e.Version == ver && e.W == bmp.Width && e.H == bmp.Height) return e.Image;
            if (e.Image != null) { e.Image.Dispose(); e.Image = null; }
            int w = bmp.Width, h = bmp.Height;
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { e.Image = SKImage.FromPixelCopy(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), bd.Scan0, bd.Stride); }
            finally { bmp.UnlockBits(bd); }
            e.Version = ver; e.W = w; e.H = h;
            return e.Image;
        }

        private SKPaint ImagePaint(ImageAttributes attrs)
        {
            _paint.Reset();
            _paint.IsAntialias = Aa;
            _paint.FilterQuality = Quality;
            if (CompositingMode == CompositingMode.SourceCopy) _paint.BlendMode = SKBlendMode.Src;
            float[] m = GfxAttr.MatrixOf(attrs);
            if (m != null) _paint.ColorFilter = SKColorFilter.CreateColorMatrix(m);
            return _paint;
        }

        public void DrawImage(Image image, RectangleF dest, RectangleF src, GraphicsUnit unit, ImageAttributes attributes)
        {
            SKImage img = ImageOf(image);
            if (img == null) return;
            _c.DrawImage(img, new SKRect(src.Left, src.Top, src.Right, src.Bottom), new SKRect(dest.Left, dest.Top, dest.Right, dest.Bottom), ImagePaint(attributes));
            Done();
        }

        public void DrawImage(Image image, PointF[] p)
        {
            SKImage img = ImageOf(image);
            if (img == null || p == null || p.Length < 3) return;
            float w = image.Width, h = image.Height;
            SKMatrix m = new SKMatrix((p[1].X - p[0].X) / w, (p[2].X - p[0].X) / h, p[0].X, (p[1].Y - p[0].Y) / w, (p[2].Y - p[0].Y) / h, p[0].Y, 0, 0, 1);
            _c.Save();
            _c.Concat(ref m);
            _c.DrawImage(img, 0, 0, ImagePaint(null));
            Done();
            _c.Restore();
        }
    }
}
