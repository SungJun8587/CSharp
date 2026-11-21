//***************************************************************************
// GdiUiCanvas.cs : Core/Ui 의 IUiCanvas 를 IGfx(현재 GDI+) 로 그리는 어댑터. 프레임마다 Begin(g) 으로 IGfx 를 받아 쓴다.
// 글자 크기 계산은 Core 의 UiTextMetrics 와 같은 식을 쓴다(레이아웃이 그리는 쪽과 어긋나지 않게).
// 아이콘은 코드로 그린다: 아이템/스킬은 ID 로 색과 모양을 정하고, 재화는 Assets/currencyicons 가 있으면 그림을 쓴다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GameViewer.Core.Ui;

namespace GameViewer.App.Render
{
    public sealed class GdiUiCanvas : IUiCanvas, IDisposable
    {
        private IGfx _g;
        private readonly Stack<GfxState> _states = new Stack<GfxState>();
        private readonly Dictionary<long, Font> _fonts = new Dictionary<long, Font>();
        private readonly StringFormat _fmt = new StringFormat(StringFormat.GenericTypographic);
        private const string FontName = "Malgun Gothic";

        public GdiUiCanvas()
        {
            _fmt.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            _fmt.Trimming = StringTrimming.EllipsisCharacter;
        }

        public void Begin(IGfx g)
        {
            _g = g;
            _states.Clear();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }

        public void End()
        {
            while (_states.Count > 0) _g.Restore(_states.Pop());      // 짝이 안 맞게 끝나도 다음 그리기에 변환/클리핑이 남지 않게
            _g = null;
        }

        public void Dispose()
        {
            foreach (Font f in _fonts.Values) f.Dispose();
            _fonts.Clear(); _fmt.Dispose();
        }

        private static Color C(int argb) { return Color.FromArgb(argb); }
        private static RectangleF R(UiRect r) { return new RectangleF(r.X, r.Y, r.W, r.H); }

        private Font FontOf(float size, bool bold)
        {
            long key = ((long)Math.Round(size * 4)) << 1 | (bold ? 1L : 0L);
            Font f;
            if (!_fonts.TryGetValue(key, out f)) { f = new Font(FontName, Math.Max(5f, size) * 0.92f, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); _fonts[key] = f; }
            return f;
        }

        private static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0.5f || r.Width < 2 * rad || r.Height < 2 * rad) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------ 도형

        public void Fill(UiRect r, int argb, float radius = 0)
        {
            if (r.W <= 0 || r.H <= 0) return;
            using (SolidBrush b = new SolidBrush(C(argb)))
            {
                if (radius <= 0.5f) _g.FillRectangle(b, R(r));
                else using (GraphicsPath p = Round(R(r), radius)) _g.FillPath(b, p);
            }
        }

        public void Gradient(UiRect r, int topArgb, int bottomArgb, float radius = 0)
        {
            if (r.W <= 0 || r.H <= 0) return;
            RectangleF rf = R(r); rf.Inflate(0, 0.5f);
            using (LinearGradientBrush b = new LinearGradientBrush(rf, C(topArgb), C(bottomArgb), LinearGradientMode.Vertical))
            using (GraphicsPath p = Round(R(r), radius)) _g.FillPath(b, p);
        }

        public void Stroke(UiRect r, int argb, float width = 1, float radius = 0)
        {
            if (r.W <= 0 || r.H <= 0) return;
            RectangleF rf = R(r); rf.Inflate(-width / 2, -width / 2);
            using (Pen pen = new Pen(C(argb), width))
            using (GraphicsPath p = Round(rf, Math.Max(0, radius - width / 2))) _g.DrawPath(pen, p);
        }

        public void Panel(UiRect r, int accentArgb)
        {
            RectangleF rf = R(r);
            using (GraphicsPath p = Round(rf, 6))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(rf, Color.FromArgb(244, 30, 36, 48), Color.FromArgb(244, 18, 22, 30), LinearGradientMode.Vertical)) _g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(230, C(accentArgb)), 2f)) _g.DrawPath(pen, p);
            }
            RectangleF inner = rf; inner.Inflate(-3.5f, -3.5f);
            using (Pen pen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f)) _g.DrawRectangle(pen, inner.X, inner.Y, inner.Width, inner.Height);
        }

        public void Line(float x1, float y1, float x2, float y2, int argb, float width = 1)
        {
            using (Pen pen = new Pen(C(argb), width)) _g.DrawLine(pen, x1, y1, x2, y2);
        }

        // ------------------------------------------------------------ 글자

        public void Measure(string s, UiTextStyle st, float maxWidth, out float width, out float height)
        {
            if (st.Wrap)
            {
                List<string> lines = UiTextMetrics.Wrap(s, st.Size, maxWidth);
                width = 0; foreach (string l in lines) width = Math.Max(width, UiTextMetrics.Width(l, st.Size));
                height = lines.Count * UiTextMetrics.LineHeight(st.Size);
            }
            else { width = UiTextMetrics.Width(s, st.Size); height = UiTextMetrics.LineHeight(st.Size); }
        }

        public void Text(string s, UiRect r, UiTextStyle st)
        {
            if (string.IsNullOrEmpty(s) || r.W <= 0 || r.H <= 0) return;
            Font f = FontOf(st.Size, st.Bold);
            _fmt.Alignment = st.Align == UiAlign.Left ? StringAlignment.Near : (st.Align == UiAlign.Center ? StringAlignment.Center : StringAlignment.Far);
            using (SolidBrush b = new SolidBrush(C(st.Argb)))
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(Math.Min(150, ((st.Argb >> 24) & 255)), 0, 0, 0)))
            {
                if (st.Wrap)
                {
                    float lh = UiTextMetrics.LineHeight(st.Size), y = r.Y;
                    foreach (string line in UiTextMetrics.Wrap(s, st.Size, r.W))
                    {
                        if (y + lh > r.Bottom + 0.5f) break;
                        RectangleF lr = new RectangleF(r.X, y, r.W, lh);
                        _g.DrawString(line, f, sh, new RectangleF(lr.X + 1, lr.Y + 1, lr.Width, lr.Height), _fmt);
                        _g.DrawString(line, f, b, lr, _fmt);
                        y += lh;
                    }
                }
                else
                {
                    float lh = UiTextMetrics.LineHeight(st.Size);
                    RectangleF lr = new RectangleF(r.X, r.Y + (r.H - lh) / 2, r.W, lh);
                    _g.DrawString(s, f, sh, new RectangleF(lr.X + 1, lr.Y + 1, lr.Width, lr.Height), _fmt);
                    _g.DrawString(s, f, b, lr, _fmt);
                }
            }
        }

        // ------------------------------------------------------------ 클리핑 / 확대

        public void PushClip(UiRect r)
        {
            _states.Push(_g.Save());
            _g.SetClip(new RectangleF(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H)), CombineMode.Intersect);
        }

        public void PopClip() { if (_states.Count > 0) _g.Restore(_states.Pop()); }

        public void PushScale(float scale, float cx, float cy)
        {
            _states.Push(_g.Save());
            _g.TranslateTransform(cx, cy); _g.ScaleTransform(scale, scale); _g.TranslateTransform(-cx, -cy);
        }

        public void PopScale() { if (_states.Count > 0) _g.Restore(_states.Pop()); }

        // ------------------------------------------------------------ 아이콘

        private static Color HueColor(uint id, float s, float v)
        {
            float h = (id * 137u % 360u);
            float c = v * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c;
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
            return Color.FromArgb(255, (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        public void Icon(UiIconKind kind, uint id, UiRect r, int grade)
        {
            if (r.W < 2 || r.H < 2) return;
            RectangleF rf = R(r);
            switch (kind)
            {
                case UiIconKind.Item: DrawItem(rf, id); break;
                case UiIconKind.Skill: DrawSkill(rf, id); break;
                case UiIconKind.Currency: DrawCurrency(rf, id); break;
                case UiIconKind.Coin: DrawCurrency(rf, 1); break;
                case UiIconKind.Mail: DrawMail(rf, false); break;
                case UiIconKind.MailOpen: DrawMail(rf, true); break;
                case UiIconKind.Gift: DrawGift(rf); break;
                case UiIconKind.Bag: DrawBag(rf); break;
                case UiIconKind.Shop: DrawShop(rf); break;
                case UiIconKind.Gear: DrawGear(rf); break;
            }
        }

        private void DrawItem(RectangleF r, uint id)
        {
            Color baseC = HueColor(id, 0.55f, 0.85f);
            RectangleF b = r; b.Inflate(-r.Width * 0.12f, -r.Height * 0.12f);
            using (GraphicsPath p = Round(b, b.Width * 0.22f))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(b, Col.Light(baseC, 0.35f), Col.Dark(baseC, 0.35f), LinearGradientMode.ForwardDiagonal)) _g.FillPath(br, p);
                using (Pen pen = new Pen(Color.FromArgb(160, 20, 20, 30), 1.2f)) _g.DrawPath(pen, p);
            }
            // 모양: ID 에 따라 안쪽 무늬가 달라진다(같은 색이어도 구별되게)
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, u = Math.Min(r.Width, r.Height) * 0.2f;
            using (SolidBrush w = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            {
                switch (id % 4)
                {
                    case 0: _g.FillEllipse(w, cx - u, cy - u, u * 2, u * 2); break;
                    case 1: _g.FillPolygon(w, new PointF[] { new PointF(cx, cy - u * 1.2f), new PointF(cx + u, cy), new PointF(cx, cy + u * 1.2f), new PointF(cx - u, cy) }); break;
                    case 2: _g.FillRectangle(w, cx - u, cy - u, u * 2, u * 2); break;
                    default: _g.FillPolygon(w, new PointF[] { new PointF(cx, cy - u * 1.1f), new PointF(cx + u * 1.1f, cy + u), new PointF(cx - u * 1.1f, cy + u) }); break;
                }
            }
            using (SolidBrush hl = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                _g.FillEllipse(hl, b.X + b.Width * 0.12f, b.Y + b.Height * 0.08f, b.Width * 0.5f, b.Height * 0.3f);
        }

        private void DrawSkill(RectangleF r, uint id)
        {
            Color baseC = HueColor(id + 3, 0.6f, 0.9f);
            RectangleF b = r; b.Inflate(-1, -1);
            using (GraphicsPath p = new GraphicsPath()) { p.AddEllipse(b); using (LinearGradientBrush br = new LinearGradientBrush(b, Col.Light(baseC, 0.3f), Col.Dark(baseC, 0.4f), LinearGradientMode.Vertical)) _g.FillPath(br, p); }
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, R0 = r.Width * 0.32f, R1 = R0 * 0.42f;
            PointF[] star = new PointF[10];
            for (int i = 0; i < 10; ++i) { double a = -Math.PI / 2 + i * Math.PI / 5; float rr = i % 2 == 0 ? R0 : R1; star[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr); }
            using (SolidBrush w = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) _g.FillPolygon(w, star);
            using (Pen pen = new Pen(Color.FromArgb(150, 20, 20, 30), 1.2f)) _g.DrawEllipse(pen, b);
        }

        private void DrawCurrency(RectangleF r, uint id)
        {
            Bitmap bmp = CurrencyIcons.Get(id);
            if (bmp != null) { _g.DrawImage(bmp, r); return; }
            RectangleF b = r; b.Inflate(-r.Width * 0.1f, -r.Height * 0.1f);
            if (id == 2)       // 블루 다이아
            {
                PointF[] d = { new PointF(b.X + b.Width * 0.2f, b.Y), new PointF(b.Right - b.Width * 0.2f, b.Y), new PointF(b.Right, b.Y + b.Height * 0.38f), new PointF(b.X + b.Width / 2, b.Bottom), new PointF(b.X, b.Y + b.Height * 0.38f) };
                using (LinearGradientBrush br = new LinearGradientBrush(b, Color.FromArgb(255, 150, 215, 255), Color.FromArgb(255, 40, 110, 220), LinearGradientMode.Vertical)) _g.FillPolygon(br, d);
                using (Pen pen = new Pen(Color.FromArgb(200, 20, 50, 110), 1.2f)) _g.DrawPolygon(pen, d);
                using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f)) _g.DrawLine(pen, b.X, b.Y + b.Height * 0.38f, b.Right, b.Y + b.Height * 0.38f);
            }
            else
            {
                using (LinearGradientBrush br = new LinearGradientBrush(b, Color.FromArgb(255, 255, 230, 120), Color.FromArgb(255, 200, 140, 30), LinearGradientMode.ForwardDiagonal)) _g.FillEllipse(br, b);
                using (Pen pen = new Pen(Color.FromArgb(220, 120, 80, 10), 1.3f)) _g.DrawEllipse(pen, b);
                RectangleF inner = b; inner.Inflate(-b.Width * 0.22f, -b.Height * 0.22f);
                using (Pen pen = new Pen(Color.FromArgb(150, 140, 90, 10), 1f)) _g.DrawEllipse(pen, inner);
            }
        }

        private void DrawMail(RectangleF r, bool open)
        {
            RectangleF b = new RectangleF(r.X + r.Width * 0.08f, r.Y + r.Height * 0.22f, r.Width * 0.84f, r.Height * 0.58f);
            using (SolidBrush br = new SolidBrush(open ? Color.FromArgb(255, 170, 175, 185) : Color.FromArgb(255, 240, 232, 200))) _g.FillRectangle(br, b);
            using (Pen pen = new Pen(Color.FromArgb(220, 60, 50, 30), 1.2f))
            {
                _g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
                _g.DrawLine(pen, b.X, b.Y, b.X + b.Width / 2, b.Y + b.Height * (open ? 0.2f : 0.6f));
                _g.DrawLine(pen, b.Right, b.Y, b.X + b.Width / 2, b.Y + b.Height * (open ? 0.2f : 0.6f));
            }
            if (!open) using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 220, 70, 60))) _g.FillEllipse(br, b.X + b.Width / 2 - b.Width * 0.09f, b.Y + b.Height * 0.5f, b.Width * 0.18f, b.Width * 0.18f);
        }

        private void DrawBag(RectangleF r)
        {
            RectangleF b = new RectangleF(r.X + r.Width * 0.14f, r.Y + r.Height * 0.3f, r.Width * 0.72f, r.Height * 0.62f);
            using (Pen pen = new Pen(Color.FromArgb(255, 120, 80, 40), Math.Max(1.5f, r.Width * 0.08f))) _g.DrawArc(pen, r.X + r.Width * 0.3f, r.Y + r.Height * 0.04f, r.Width * 0.4f, r.Height * 0.5f, 180, 180);
            using (GraphicsPath p = Round(b, r.Width * 0.2f))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(b, Color.FromArgb(255, 205, 150, 80), Color.FromArgb(255, 140, 90, 40), LinearGradientMode.Vertical)) _g.FillPath(br, p);
                using (Pen pen = new Pen(Color.FromArgb(220, 70, 40, 15), 1.3f)) _g.DrawPath(pen, p);
            }
            using (Pen pen = new Pen(Color.FromArgb(200, 70, 40, 15), 1.2f)) _g.DrawLine(pen, b.X + b.Width * 0.15f, b.Y + b.Height * 0.3f, b.Right - b.Width * 0.15f, b.Y + b.Height * 0.3f);
        }

        private void DrawShop(RectangleF r)
        {
            RectangleF b = new RectangleF(r.X + r.Width * 0.14f, r.Y + r.Height * 0.4f, r.Width * 0.72f, r.Height * 0.5f);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 230, 215, 180))) _g.FillRectangle(br, b);
            float aw = b.Width / 4f;
            for (int i = 0; i < 4; ++i)
                using (SolidBrush br = new SolidBrush(i % 2 == 0 ? Color.FromArgb(255, 210, 70, 70) : Color.FromArgb(255, 245, 240, 230)))
                    _g.FillRectangle(br, b.X + i * aw - r.Width * 0.02f + 0.0f, r.Y + r.Height * 0.14f, aw + r.Width * 0.01f, r.Height * 0.28f);
            using (Pen pen = new Pen(Color.FromArgb(220, 80, 40, 30), 1.2f)) { _g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height); _g.DrawRectangle(pen, b.X - r.Width * 0.02f, r.Y + r.Height * 0.14f, b.Width + r.Width * 0.04f, r.Height * 0.28f); }
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 120, 80, 50))) _g.FillRectangle(br, b.X + b.Width * 0.38f, b.Y + b.Height * 0.3f, b.Width * 0.24f, b.Height * 0.7f);
        }

        private void DrawGear(RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, ro = Math.Min(r.Width, r.Height) * 0.42f, ri = ro * 0.74f;
            PointF[] pts = new PointF[24];
            for (int i = 0; i < 24; ++i) { double a = i * Math.PI / 12; float rr = (i / 2) % 2 == 0 ? ro : ri; pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr); }
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 170, 180, 195))) _g.FillPolygon(br, pts);
            using (Pen pen = new Pen(Color.FromArgb(220, 50, 60, 75), 1.2f)) _g.DrawPolygon(pen, pts);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 30, 36, 48))) _g.FillEllipse(br, cx - ri * 0.45f, cy - ri * 0.45f, ri * 0.9f, ri * 0.9f);
        }

        private void DrawGift(RectangleF r)
        {
            RectangleF b = new RectangleF(r.X + r.Width * 0.14f, r.Y + r.Height * 0.36f, r.Width * 0.72f, r.Height * 0.52f);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 210, 80, 90))) _g.FillRectangle(br, b);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 240, 200, 90)))
            {
                _g.FillRectangle(br, b.X + b.Width * 0.42f, b.Y - r.Height * 0.12f, b.Width * 0.16f, b.Height + r.Height * 0.12f);
                _g.FillRectangle(br, b.X - r.Width * 0.04f, b.Y - r.Height * 0.12f, b.Width + r.Width * 0.08f, r.Height * 0.14f);
            }
            using (Pen pen = new Pen(Color.FromArgb(200, 90, 30, 40), 1.2f)) _g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
        }
    }
}
