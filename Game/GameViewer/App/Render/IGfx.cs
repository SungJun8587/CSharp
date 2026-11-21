//***************************************************************************
// IGfx.cs : 렌더러가 쓰는 그리기 호출의 얇은 경계. 지금 구현은 GDI+ 를 그대로 감싼 GdiGfx 하나이고,
// 나중에 SkiaSharp(GPU) 구현을 같은 인터페이스로 끼워 렌더러를 한 번에 바꾸지 않고 옮기기 위한 자리다.
//
// 설계:
//  - 인터페이스에는 핵심 호출만 둔다(구현체가 채울 일이 적도록). int/Rectangle/Point 같은 편의 오버로드는 GfxExt 확장 메서드가
//    핵심 호출로 바꿔 준다(그래서 기존 g.FillRectangle(brush, rect) 같은 호출이 고치지 않고 그대로 컴파일된다).
//  - 펜/브러시/경로/글꼴/이미지는 아직 System.Drawing 형식이다(값에 가까운 설명자). SkiaSharp 구현은 이것들을 읽어
//    SKPaint/SKPath 로 바꾸고(색/두께/그라디언트 정보는 속성으로 읽을 수 있다), 같은 객체는 캐시해 둔다.
//  - 비트맵으로 그리는 쪽(스프라이트 굽기)은 GdiGfx.FromImage(bmp) 로 IGfx 를 얻는다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace GameViewer.App.Render
{
    /// <summary>Save/Restore 가 주고받는 표식. 안쪽 값(Token)은 구현마다 다르다(GDI+ 는 GraphicsState, Skia 는 저장 깊이).</summary>
    public sealed class GfxState
    {
        public readonly object Token;
        public GfxState(object token) { Token = token; }
    }

    /// <summary>
    /// 선형 그라디언트 브러시 생성기. System.Drawing.LinearGradientBrush 는 만든 뒤에 방향(모드/각도)을 읽을 수 없어서,
    /// 만들 때 그라디언트 선(시작점 -> 끝점)을 따로 기록해 두고 다른 구현(Skia)이 읽는다. 렌더러는 new LinearGradientBrush 대신 이것을 쓴다.
    /// </summary>
    public static class GfxBrush
    {
        public struct Line { public float X1, Y1, X2, Y2; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LinearGradientBrush, object> Lines = new System.Runtime.CompilerServices.ConditionalWeakTable<LinearGradientBrush, object>();

        public static LinearGradientBrush Linear(RectangleF r, Color a, Color b, LinearGradientMode mode)
        {
            LinearGradientBrush br = new LinearGradientBrush(r, a, b, mode);
            Line l;
            switch (mode)
            {
                case LinearGradientMode.Horizontal: l = new Line { X1 = r.Left, Y1 = r.Top, X2 = r.Right, Y2 = r.Top }; break;
                case LinearGradientMode.ForwardDiagonal: l = new Line { X1 = r.Left, Y1 = r.Top, X2 = r.Right, Y2 = r.Bottom }; break;
                case LinearGradientMode.BackwardDiagonal: l = new Line { X1 = r.Right, Y1 = r.Top, X2 = r.Left, Y2 = r.Bottom }; break;
                default: l = new Line { X1 = r.Left, Y1 = r.Top, X2 = r.Left, Y2 = r.Bottom }; break;
            }
            Lines.Add(br, l);
            return br;
        }
        public static LinearGradientBrush Linear(Rectangle r, Color a, Color b, LinearGradientMode mode) { return Linear((RectangleF)r, a, b, mode); }

        public static LinearGradientBrush Linear(PointF p1, PointF p2, Color a, Color b)
        {
            LinearGradientBrush br = new LinearGradientBrush(p1, p2, a, b);
            Lines.Add(br, new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y });
            return br;
        }
        public static LinearGradientBrush Linear(Point p1, Point p2, Color a, Color b) { return Linear(new PointF(p1.X, p1.Y), new PointF(p2.X, p2.Y), a, b); }

        /// <summary>각도(도, x 축에서 시계 방향) 형태. 사각형을 그 방향으로 투영한 길이가 그라디언트 길이다.</summary>
        public static LinearGradientBrush Linear(RectangleF r, Color a, Color b, float angle)
        {
            LinearGradientBrush br = new LinearGradientBrush(r, a, b, angle);
            Lines.Add(br, AngleLine(r, angle));
            return br;
        }
        public static LinearGradientBrush Linear(RectangleF r, Color a, Color b, float angle, bool isAngleScalable)
        {
            LinearGradientBrush br = new LinearGradientBrush(r, a, b, angle, isAngleScalable);
            Lines.Add(br, AngleLine(r, angle));
            return br;
        }
        public static LinearGradientBrush Linear(Rectangle r, Color a, Color b, float angle) { return Linear((RectangleF)r, a, b, angle); }

        private static Line AngleLine(RectangleF r, float angle)
        {
            double rad = angle * Math.PI / 180.0, dx = Math.Cos(rad), dy = Math.Sin(rad);
            double len = Math.Abs(r.Width * dx) + Math.Abs(r.Height * dy);
            double cx = r.Left + r.Width / 2.0, cy = r.Top + r.Height / 2.0;
            return new Line { X1 = (float)(cx - dx * len / 2), Y1 = (float)(cy - dy * len / 2), X2 = (float)(cx + dx * len / 2), Y2 = (float)(cy + dy * len / 2) };
        }

        /// <summary>브러시를 만들 때 기록한 그라디언트 선. 기록이 없으면(직접 new 한 브러시) 사각형 위->아래 선으로 본다.</summary>
        public static Line LineOf(LinearGradientBrush b)
        {
            object o;
            if (Lines.TryGetValue(b, out o)) return (Line)o;
            RectangleF r = b.Rectangle;
            return new Line { X1 = r.Left, Y1 = r.Top, X2 = r.Left, Y2 = r.Bottom };
        }
    }

    /// <summary>비트맵 내용이 바뀐 횟수. GPU/다른 구현이 올려 둔 사본을 다시 만들어야 하는지 알아내는 데 쓴다.
    /// GdiGfx.FromImage 로 그리거나 LockBits 로 직접 쓴 뒤에는 Touch 를 불러야 한다(만들 때 한 번 채우고 안 바뀌는 스프라이트는 필요 없다).</summary>
    public static class GfxImages
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, System.Runtime.CompilerServices.StrongBox<int>> Versions = new System.Runtime.CompilerServices.ConditionalWeakTable<Image, System.Runtime.CompilerServices.StrongBox<int>>();
        public static void Touch(Image image) { if (image == null) return; lock (Versions) { Versions.GetOrCreateValue(image).Value++; } }
        public static int Version(Image image) { lock (Versions) { System.Runtime.CompilerServices.StrongBox<int> b; return Versions.TryGetValue(image, out b) ? b.Value : 0; } }
    }

    /// <summary>ImageAttributes 는 설정한 색 행렬을 읽을 수 없어서, 설정할 때 같이 기록한다(Skia 구현이 색 필터로 바꾼다).</summary>
    public static class GfxAttr
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageAttributes, float[]> Matrices = new System.Runtime.CompilerServices.ConditionalWeakTable<ImageAttributes, float[]>();

        public static void SetColorMatrix(ImageAttributes ia, ColorMatrix m)
        {
            ia.SetColorMatrix(m);
            // GDI+ 는 행 벡터 [R G B A 1] x M, Skia 는 출력 채널마다 한 줄(4x5, 마지막 열은 0..1 이동)이라 전치해서 기록한다
            float[] k = new float[20];
            for (int outCh = 0; outCh < 4; ++outCh)
                for (int inCh = 0; inCh < 5; ++inCh) k[outCh * 5 + inCh] = m[inCh, outCh];
            lock (Matrices) { Matrices.Remove(ia); Matrices.Add(ia, k); }
        }

        /// <summary>SetColorMatrix 로 기록된 4x5 행렬(없으면 null).</summary>
        public static float[] MatrixOf(ImageAttributes ia)
        {
            if (ia == null) return null;
            float[] k;
            lock (Matrices) return Matrices.TryGetValue(ia, out k) ? k : null;
        }
    }

    public interface IGfx
    {
        // ---- 품질 상태
        SmoothingMode SmoothingMode { get; set; }
        InterpolationMode InterpolationMode { get; set; }
        CompositingMode CompositingMode { get; set; }
        PixelOffsetMode PixelOffsetMode { get; set; }
        TextRenderingHint TextRenderingHint { get; set; }

        // ---- 변환 / 클리핑 / 상태 저장
        /// <summary>현재 변환 행렬의 여섯 값 {m11, m12, m21, m22, dx, dy} (Matrix.Elements 와 같은 순서).</summary>
        float[] GetTransformElements();
        /// <summary>변환과 클리핑을 저장한다. Restore 에 그대로 돌려 준다(구현마다 안쪽 표현이 다르다).</summary>
        GfxState Save();
        void Restore(GfxState state);
        void ResetTransform();
        void TranslateTransform(float dx, float dy);
        void RotateTransform(float degrees);
        void ScaleTransform(float sx, float sy);
        void SetClip(RectangleF rect, CombineMode mode);
        void SetClip(GraphicsPath path, CombineMode mode);

        // ---- 도형
        void Clear(Color color);
        void DrawLine(Pen pen, float x1, float y1, float x2, float y2);
        void DrawLines(Pen pen, PointF[] points);
        void DrawRectangle(Pen pen, float x, float y, float w, float h);
        void FillRectangle(Brush brush, float x, float y, float w, float h);
        void DrawEllipse(Pen pen, float x, float y, float w, float h);
        void FillEllipse(Brush brush, float x, float y, float w, float h);
        void DrawArc(Pen pen, float x, float y, float w, float h, float startAngle, float sweepAngle);
        void FillPie(Brush brush, float x, float y, float w, float h, float startAngle, float sweepAngle);
        void DrawPolygon(Pen pen, PointF[] points);
        void FillPolygon(Brush brush, PointF[] points);
        void DrawPath(Pen pen, GraphicsPath path);
        void FillPath(Brush brush, GraphicsPath path);
        void DrawCurve(Pen pen, PointF[] points, float tension);
        void DrawBezier(Pen pen, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);

        // ---- 글자
        void DrawString(string text, Font font, Brush brush, RectangleF layout, StringFormat format);
        void DrawString(string text, Font font, Brush brush, float x, float y, StringFormat format);
        SizeF MeasureString(string text, Font font);

        // ---- 이미지
        void DrawImage(Image image, RectangleF dest, RectangleF src, GraphicsUnit unit, ImageAttributes attributes);
        /// <summary>이미지를 평행사변형(왼위, 오른위, 왼아래 세 점)으로 그린다(회전/기울임).</summary>
        void DrawImage(Image image, PointF[] destParallelogram);
    }

    /// <summary>GDI+ Graphics 를 그대로 감싼 구현. Graphics 를 빌려 쓰면(생성자) 닫지 않고, FromImage 로 만든 것은 닫을 때 함께 닫는다.</summary>
    public sealed class GdiGfx : IGfx, IDisposable
    {
        private readonly Graphics _g;
        private readonly bool _owns;
        private readonly Image _image;           // FromImage 로 만든 경우 그 이미지(닫을 때 변경 표시)

        public GdiGfx(Graphics g) { _g = g; _owns = false; }
        private GdiGfx(Graphics g, Image image) { _g = g; _owns = true; _image = image; }
        public static GdiGfx FromImage(Image image) { GfxImages.Touch(image); return new GdiGfx(Graphics.FromImage(image), image); }
        /// <summary>GDI+ 원본(마우스 좌표계 변환, 화면 복사 같은 GDI+ 전용 작업용). SkiaSharp 구현에는 없다.</summary>
        public Graphics Native { get { return _g; } }
        public void Dispose() { if (_owns) { _g.Dispose(); GfxImages.Touch(_image); } }

        public SmoothingMode SmoothingMode { get { return _g.SmoothingMode; } set { _g.SmoothingMode = value; } }
        public InterpolationMode InterpolationMode { get { return _g.InterpolationMode; } set { _g.InterpolationMode = value; } }
        public CompositingMode CompositingMode { get { return _g.CompositingMode; } set { _g.CompositingMode = value; } }
        public PixelOffsetMode PixelOffsetMode { get { return _g.PixelOffsetMode; } set { _g.PixelOffsetMode = value; } }
        public TextRenderingHint TextRenderingHint { get { return _g.TextRenderingHint; } set { _g.TextRenderingHint = value; } }
        public float[] GetTransformElements() { using (Matrix m = _g.Transform) return m.Elements; }
        public GfxState Save() { return new GfxState(_g.Save()); }
        public void Restore(GfxState state) { _g.Restore((GraphicsState)state.Token); }
        public void ResetTransform() { _g.ResetTransform(); }
        public void TranslateTransform(float dx, float dy) { _g.TranslateTransform(dx, dy); }
        public void RotateTransform(float degrees) { _g.RotateTransform(degrees); }
        public void ScaleTransform(float sx, float sy) { _g.ScaleTransform(sx, sy); }
        public void SetClip(RectangleF rect, CombineMode mode) { _g.SetClip(rect, mode); }
        public void SetClip(GraphicsPath path, CombineMode mode) { _g.SetClip(path, mode); }

        public void Clear(Color color) { _g.Clear(color); }
        public void DrawLine(Pen pen, float x1, float y1, float x2, float y2) { _g.DrawLine(pen, x1, y1, x2, y2); }
        public void DrawLines(Pen pen, PointF[] points) { _g.DrawLines(pen, points); }
        public void DrawRectangle(Pen pen, float x, float y, float w, float h) { _g.DrawRectangle(pen, x, y, w, h); }
        public void FillRectangle(Brush brush, float x, float y, float w, float h) { _g.FillRectangle(brush, x, y, w, h); }
        public void DrawEllipse(Pen pen, float x, float y, float w, float h) { _g.DrawEllipse(pen, x, y, w, h); }
        public void FillEllipse(Brush brush, float x, float y, float w, float h) { _g.FillEllipse(brush, x, y, w, h); }
        public void DrawArc(Pen pen, float x, float y, float w, float h, float startAngle, float sweepAngle) { _g.DrawArc(pen, x, y, w, h, startAngle, sweepAngle); }
        public void FillPie(Brush brush, float x, float y, float w, float h, float startAngle, float sweepAngle) { _g.FillPie(brush, x, y, w, h, startAngle, sweepAngle); }
        public void DrawPolygon(Pen pen, PointF[] points) { _g.DrawPolygon(pen, points); }
        public void FillPolygon(Brush brush, PointF[] points) { _g.FillPolygon(brush, points); }
        public void DrawPath(Pen pen, GraphicsPath path) { _g.DrawPath(pen, path); }
        public void FillPath(Brush brush, GraphicsPath path) { _g.FillPath(brush, path); }
        public void DrawCurve(Pen pen, PointF[] points, float tension) { _g.DrawCurve(pen, points, tension); }
        public void DrawBezier(Pen pen, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4) { _g.DrawBezier(pen, x1, y1, x2, y2, x3, y3, x4, y4); }
        public void DrawString(string text, Font font, Brush brush, RectangleF layout, StringFormat format) { _g.DrawString(text, font, brush, layout, format); }
        public void DrawString(string text, Font font, Brush brush, float x, float y, StringFormat format) { _g.DrawString(text, font, brush, x, y, format); }
        public SizeF MeasureString(string text, Font font) { return _g.MeasureString(text, font); }
        public void DrawImage(Image image, PointF[] destParallelogram) { _g.DrawImage(image, destParallelogram); }
        public void DrawImage(Image image, RectangleF dest, RectangleF src, GraphicsUnit unit, ImageAttributes attributes)
        {
            if (attributes == null) _g.DrawImage(image, dest, src, unit);
            else _g.DrawImage(image, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, unit, attributes);
        }
    }

    /// <summary>GDI+ Graphics 가 제공하던 편의 오버로드. 전부 IGfx 핵심 호출로 바꾼다.</summary>
    public static class GfxExt
    {
        public static void SetClip(this IGfx g, RectangleF r) { g.SetClip(r, CombineMode.Replace); }
        public static void SetClip(this IGfx g, Rectangle r) { g.SetClip((RectangleF)r, CombineMode.Replace); }
        public static void SetClip(this IGfx g, Rectangle r, CombineMode m) { g.SetClip((RectangleF)r, m); }
        public static void SetClip(this IGfx g, GraphicsPath p) { g.SetClip(p, CombineMode.Replace); }

        public static void DrawLine(this IGfx g, Pen p, PointF a, PointF b) { g.DrawLine(p, a.X, a.Y, b.X, b.Y); }
        public static void DrawLine(this IGfx g, Pen p, Point a, Point b) { g.DrawLine(p, a.X, a.Y, b.X, b.Y); }
        public static void DrawRectangle(this IGfx g, Pen p, Rectangle r) { g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
        public static void DrawRectangle(this IGfx g, Pen p, RectangleF r) { g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
        public static void FillRectangle(this IGfx g, Brush b, Rectangle r) { g.FillRectangle(b, r.X, r.Y, r.Width, r.Height); }
        public static void FillRectangle(this IGfx g, Brush b, RectangleF r) { g.FillRectangle(b, r.X, r.Y, r.Width, r.Height); }
        public static void DrawEllipse(this IGfx g, Pen p, Rectangle r) { g.DrawEllipse(p, r.X, r.Y, r.Width, r.Height); }
        public static void DrawEllipse(this IGfx g, Pen p, RectangleF r) { g.DrawEllipse(p, r.X, r.Y, r.Width, r.Height); }
        public static void FillEllipse(this IGfx g, Brush b, Rectangle r) { g.FillEllipse(b, r.X, r.Y, r.Width, r.Height); }
        public static void FillEllipse(this IGfx g, Brush b, RectangleF r) { g.FillEllipse(b, r.X, r.Y, r.Width, r.Height); }
        public static void DrawArc(this IGfx g, Pen p, RectangleF r, float s, float sw) { g.DrawArc(p, r.X, r.Y, r.Width, r.Height, s, sw); }
        public static void DrawArc(this IGfx g, Pen p, Rectangle r, float s, float sw) { g.DrawArc(p, r.X, r.Y, r.Width, r.Height, s, sw); }
        public static void FillPie(this IGfx g, Brush b, Rectangle r, float s, float sw) { g.FillPie(b, r.X, r.Y, r.Width, r.Height, s, sw); }
        public static void DrawLines(this IGfx g, Pen p, Point[] pts) { g.DrawLines(p, ToF(pts)); }
        public static void DrawPolygon(this IGfx g, Pen p, Point[] pts) { g.DrawPolygon(p, ToF(pts)); }
        public static void FillPolygon(this IGfx g, Brush b, Point[] pts) { g.FillPolygon(b, ToF(pts)); }
        public static void DrawCurve(this IGfx g, Pen p, PointF[] pts) { g.DrawCurve(p, pts, 0.5f); }
        public static void DrawBezier(this IGfx g, Pen p, PointF a, PointF b, PointF c, PointF d) { g.DrawBezier(p, a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y); }

        public static void DrawString(this IGfx g, string s, Font f, Brush b, float x, float y) { g.DrawString(s, f, b, x, y, null); }
        public static void DrawString(this IGfx g, string s, Font f, Brush b, PointF p) { g.DrawString(s, f, b, p.X, p.Y, null); }
        public static void DrawString(this IGfx g, string s, Font f, Brush b, PointF p, StringFormat fmt) { g.DrawString(s, f, b, p.X, p.Y, fmt); }
        public static void DrawString(this IGfx g, string s, Font f, Brush b, RectangleF r) { g.DrawString(s, f, b, r, null); }

        public static void DrawImage(this IGfx g, Image i, float x, float y, float w, float h) { g.DrawImage(i, new RectangleF(x, y, w, h), new RectangleF(0, 0, i.Width, i.Height), GraphicsUnit.Pixel, null); }
        public static void DrawImage(this IGfx g, Image i, RectangleF r) { g.DrawImage(i, r.X, r.Y, r.Width, r.Height); }
        public static void DrawImage(this IGfx g, Image i, Rectangle r) { g.DrawImage(i, r.X, r.Y, r.Width, r.Height); }
        public static void DrawImageUnscaled(this IGfx g, Image i, int x, int y) { g.DrawImage(i, x, y, i.Width, i.Height); }
        public static void DrawImage(this IGfx g, Image i, float x, float y) { g.DrawImage(i, x, y, i.Width, i.Height); }
        public static void DrawImage(this IGfx g, Image i, PointF p) { g.DrawImage(i, p.X, p.Y, i.Width, i.Height); }
        public static void DrawImage(this IGfx g, Image i, Point p) { g.DrawImage(i, p.X, p.Y, i.Width, i.Height); }
        public static void DrawImage(this IGfx g, Image i, RectangleF dest, RectangleF src, GraphicsUnit u) { g.DrawImage(i, dest, src, u, null); }
        public static void DrawImage(this IGfx g, Image i, Rectangle dest, float sx, float sy, float sw, float sh, GraphicsUnit u, ImageAttributes a) { g.DrawImage(i, dest, new RectangleF(sx, sy, sw, sh), u, a); }
        public static void DrawImage(this IGfx g, Image i, Rectangle dest, float sx, float sy, float sw, float sh, GraphicsUnit u) { g.DrawImage(i, dest, new RectangleF(sx, sy, sw, sh), u, null); }

        private static PointF[] ToF(Point[] pts)
        {
            PointF[] r = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; ++i) r[i] = new PointF(pts[i].X, pts[i].Y);
            return r;
        }
    }
}
