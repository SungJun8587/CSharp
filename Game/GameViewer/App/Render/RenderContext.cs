//***************************************************************************
// RenderContext.cs : 한 프레임을 그리는 데 쓰는 공용 상태(카메라/좌표 변환/폰트/브러시 캐시).
// 렌더러는 System.Drawing 만 쓰고 폼/컨트롤을 모른다 — 그래서 PNG 로 찍는 테스트도 가능하다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GameViewer.App.Render
{
    public sealed class ViewState
    {
        public float CamX = 500, CamY = 500;
        public double Scale = 20;               // px per world unit
        public int Width = 1280, Height = 800;
        public ulong SelectedId;
        public bool HasMouse;
        public int MouseX = -1, MouseY = -1;     // 화면 좌표의 마우스(창 안의 호버 표시용)
        public float MouseWorldX, MouseWorldY;
        public bool ShowHelp = true;
        public int LogMode;                     // 0 떠오르는 줄, 1 탭 로그 창, 2 끔 (L 키로 돈다)
        public bool ShowLog { get { return LogMode == 0; } set { LogMode = value ? 0 : 2; } }
        public GameViewer.Core.Logic.LogTab LogTabSel = GameViewer.Core.Logic.LogTab.All;
        public int LogScroll;                   // 로그 창에서 위로 거슬러 올라간 줄 수
        public int DragSlot = -1;               // 스킬바에서 끌고 있는 칸(1.. = 스킬, -1 없음)
        public bool ChatActive;                 // 채팅 입력 중(입력 칸은 컨트롤이 그린다)
        public string ChatDraft = "";
        public GameViewer.Core.Logic.ViewSettings Settings = new GameViewer.Core.Logic.ViewSettings();
        public bool SettingsOpen; public int SettingsIndex;
        public bool KeysOpen;                   // 키 설정 창
        public int KeysIndex;                   // 키 설정 창에서 가리키는 동작(BindAction 번호)
        public bool KeysCapture;                // 새 키를 기다리는 중
        public string KeysMsg = "";             // 키 설정 창 아래 안내(맞바꿈/예약 키 등)
        public string WeatherLabel;             // 지도 밑에 적는 날씨/시간대 문구(없으면 null)
        public string BgmLabel;                 // 지금 흐르는 배경음악 이름(없으면 null)
        public bool IntroBanner = true;         // 존 이름 배너를 보일지(재접속은 검은 화면 페이드만)
        public long IntroStartMs = -1;          // 존 입장 연출을 시작한 시각(-1 이면 없음)
        public bool MapOpen;                    // 월드맵 창
        public float MapZoom = 1f, MapCx = float.NaN, MapCy = float.NaN;   // NaN = 월드 가운데
        public double FrameMs;                  // 마지막 프레임을 그리는 데 걸린 시간
        public ulong HoverId;                   // 마우스가 올라가 있는 엔티티
        public bool HeroSelectOpen;             // 영웅 선택 창이 열려 있는가 (열려 있으면 게임 입력은 막는다)
        public int HeroSelectIndex;             // 영웅 선택 창에서 가리키는 줄
        public double Fps;

        public float ToScreenX(float wx) { return (float)((wx - CamX) * Scale + Width * 0.5); }
        public float ToScreenY(float wy) { return (float)((wy - CamY) * Scale + Height * 0.5); }
        public float ToWorldX(float sx) { return (float)((sx - Width * 0.5) / Scale + CamX); }
        public float ToWorldY(float sy) { return (float)((sy - Height * 0.5) / Scale + CamY); }
        public float Px(float units) { return (float)(units * Scale); }
    }

    /// <summary>프레임마다 새로 만들지 않도록 폰트/브러시/펜을 모아둔다.</summary>
    public sealed class Resources : IDisposable
    {
        public readonly string FontFamily;
        public readonly Font Small, Normal, Bold, Big, Huge;
        private readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        private readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        private readonly Dictionary<long, Pen> _dashPens = new Dictionary<long, Pen>();
        public readonly StringFormat Center = new StringFormat();

        public Resources()
        {
            FontFamily = PickFont(new string[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Noto Sans CJK KR", "Segoe UI", "Arial" });
            Small = new Font(FontFamily, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            Normal = new Font(FontFamily, 10f, FontStyle.Regular, GraphicsUnit.Point);
            Bold = new Font(FontFamily, 10f, FontStyle.Bold, GraphicsUnit.Point);
            Big = new Font(FontFamily, 14f, FontStyle.Bold, GraphicsUnit.Point);
            Huge = new Font(FontFamily, 22f, FontStyle.Bold, GraphicsUnit.Point);
            Center.Alignment = StringAlignment.Center;
            Center.LineAlignment = StringAlignment.Center;
        }

        private static string PickFont(string[] names)
        {
            HashSet<string> have = new HashSet<string>();
            foreach (FontFamily f in System.Drawing.FontFamily.Families) have.Add(f.Name);
            foreach (string n in names) if (have.Contains(n)) return n;
            return System.Drawing.FontFamily.GenericSansSerif.Name;
        }

        public SolidBrush Brush(Color c)
        {
            int key = c.ToArgb();
            SolidBrush b;
            if (!_brushes.TryGetValue(key, out b)) { b = new SolidBrush(c); _brushes[key] = b; }
            return b;
        }
        public SolidBrush Brush(int a, int r, int g, int bl) { return Brush(Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(bl))); }

        public Pen Pen(Color c, float width)
        {
            long key = ((long)c.ToArgb() << 16) ^ (long)(width * 10);
            Pen p;
            if (!_pens.TryGetValue(key, out p)) { p = new Pen(c, width); p.LineJoin = LineJoin.Round; _pens[key] = p; }
            return p;
        }
        /// <summary>점선 펜(캐시). 점선이 흘러가는 효과는 offset 으로 준다.</summary>
        public Pen DashPen(Color c, float width, float offset)
        {
            long key = ((long)c.ToArgb() << 16) ^ (long)(width * 10);
            Pen p;
            if (!_dashPens.TryGetValue(key, out p)) { p = new Pen(c, width); p.DashStyle = DashStyle.Dash; p.LineJoin = LineJoin.Round; _dashPens[key] = p; }
            p.DashOffset = offset;
            return p;
        }

        public Pen Pen(int a, int r, int g, int b, float width) { return Pen(Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(b)), width); }

        public static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        /// <summary>글자에 검은 외곽선을 둘러 어떤 배경에서도 읽히게 그린다.</summary>
        /// <remarks>
        /// 외곽선 글자는 DrawString 을 5번 부르는 비싼 그림이라, (글자, 글꼴, 색, 정렬, 배율)마다 한 번만 비트맵으로 그려 두고 그 비트맵을 찍는다.
        /// 변환이 회전/비균일 배율이면 예전처럼 직접 그린다. 투명도는 찍을 때 곱한다(색의 알파 = 페이드).
        /// </remarks>
        public void OutlinedText(Graphics g, string text, Font font, Color color, float x, float y, StringFormat fmt)
        {
            if (!TextCacheOn || string.IsNullOrEmpty(text) || text.Length > 80 || color.A < 4) { if (!string.IsNullOrEmpty(text) && color.A >= 4) OutlinedTextDirect(g, text, font, color, x, y, fmt); return; }
            float m11, m22, dx, dy;
            using (Matrix m = g.Transform)
            {
                float[] e = m.Elements;
                if (Math.Abs(e[1]) > 0.001f || Math.Abs(e[2]) > 0.001f || Math.Abs(e[0] - e[3]) > 0.01f) { OutlinedTextDirect(g, text, font, color, x, y, fmt); return; }
                m11 = e[0]; m22 = e[3]; dx = e[4]; dy = e[5];
            }
            if (m11 < 0.5f || m11 > 4f) { OutlinedTextDirect(g, text, font, color, x, y, fmt); return; }
            int qs = Math.Abs(m11 - 1f) < 0.01f ? 8 : (int)Math.Round(m11 * 8f);          // 배율은 1/8 단위로 묶는다
            StringAlignment ha = fmt != null ? fmt.Alignment : StringAlignment.Near, va = fmt != null ? fmt.LineAlignment : StringAlignment.Near;
            int rgb = color.ToArgb() & 0xFFFFFF;
            string key = qs + "|" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(font) + "|" + (int)ha + (int)va + "|" + rgb.ToString("x6") + "|" + text;
            TextEntry te;
            if (!_text.TryGetValue(key, out te))
            {
                if (_text.Count >= TextCacheMax) PurgeText();
                te = MakeText(g, text, font, qs / 8f, Color.FromArgb(255, color), ha, va);
                _text[key] = te;
            }
            te.Used = _textTick;
            float px = m11 * x + dx, py = m22 * y + dy;        // 화면(변환 후) 기준점
            float left = ha == StringAlignment.Near ? px - TextPad : (ha == StringAlignment.Center ? px - te.Bmp.Width / 2f : px - te.Bmp.Width + TextPad);
            float top = va == StringAlignment.Near ? py - TextPad : (va == StringAlignment.Center ? py - te.Bmp.Height / 2f : py - te.Bmp.Height + TextPad);
            GraphicsState st = g.Save();
            g.ResetTransform();
            System.Drawing.Imaging.ImageAttributes ia = color.A >= 252 ? null : AlphaAttr(color.A / 255f);
            Rectangle dst = new Rectangle((int)Math.Round(left), (int)Math.Round(top), te.Bmp.Width, te.Bmp.Height);
            if (ia == null) g.DrawImage(te.Bmp, dst.X, dst.Y, te.Bmp.Width, te.Bmp.Height);
            else g.DrawImage(te.Bmp, dst, 0, 0, te.Bmp.Width, te.Bmp.Height, GraphicsUnit.Pixel, ia);
            g.Restore(st);
        }

        /// <summary>캐시 없이 예전 방식(5번 그리기)으로 그린다.</summary>
        public void OutlinedTextDirect(Graphics g, string text, Font font, Color color, float x, float y, StringFormat fmt)
        {
            SolidBrush shadow = Brush(Color.FromArgb(Math.Min(220, (int)color.A), 0, 0, 0));
            g.DrawString(text, font, shadow, x - 1, y, fmt);
            g.DrawString(text, font, shadow, x + 1, y, fmt);
            g.DrawString(text, font, shadow, x, y - 1, fmt);
            g.DrawString(text, font, shadow, x, y + 1, fmt);
            g.DrawString(text, font, Brush(color), x, y, fmt);
        }

        // ------------------------------------------------------------------ 외곽선 글자 캐시
        public bool TextCacheOn = Environment.GetEnvironmentVariable("GV_NOTEXTCACHE") != "1";        // 비교/문제 확인용으로 끌 수 있다
        private const int TextPad = 3, TextCacheMax = 600;
        private sealed class TextEntry { public Bitmap Bmp; public long Used; }
        private readonly Dictionary<string, TextEntry> _text = new Dictionary<string, TextEntry>();
        private readonly Dictionary<long, Font> _scaledFonts = new Dictionary<long, Font>();
        private readonly System.Drawing.Imaging.ImageAttributes[] _alphaAttr = new System.Drawing.Imaging.ImageAttributes[17];
        private Bitmap _measBmp; private Graphics _meas;
        private long _textTick;
        public int TextCacheCount { get { return _text.Count; } }
        public int TextCacheMisses;

        /// <summary>프레임마다 한 번 부른다(오래 안 쓴 글자를 비우는 기준 시각).</summary>
        public void TextFrame() { ++_textTick; }

        private System.Drawing.Imaging.ImageAttributes AlphaAttr(float a)
        {
            int k = (int)Math.Round(Math.Max(0f, Math.Min(1f, a)) * 16);
            if (_alphaAttr[k] == null)
            {
                System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(); cm.Matrix33 = k / 16f;
                System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes(); ia.SetColorMatrix(cm); _alphaAttr[k] = ia;
            }
            return _alphaAttr[k];
        }

        private Font ScaledFont(Font f, int qs)
        {
            if (qs == 8) return f;
            long key = ((long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(f) << 8) | (uint)qs;
            Font sf;
            if (!_scaledFonts.TryGetValue(key, out sf)) { sf = new Font(f.FontFamily, f.Size * qs / 8f, f.Style, f.Unit); _scaledFonts[key] = sf; }
            return sf;
        }

        private TextEntry MakeText(Graphics like, string text, Font font, float scale, Color color, StringAlignment ha, StringAlignment va)
        {
            ++TextCacheMisses;
            Font f = ScaledFont(font, (int)Math.Round(scale * 8f));
            if (_meas == null) { _measBmp = new Bitmap(4, 4); _meas = Graphics.FromImage(_measBmp); }
            _meas.TextRenderingHint = like.TextRenderingHint;
            SizeF sz = _meas.MeasureString(text, f);
            int bw = (int)Math.Ceiling(sz.Width) + TextPad * 2 + 2, bh = (int)Math.Ceiling(sz.Height) + TextPad * 2;
            Bitmap bmp = new Bitmap(bw, bh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics lg = Graphics.FromImage(bmp))
            using (StringFormat sf = new StringFormat())
            {
                lg.TextRenderingHint = like.TextRenderingHint; lg.SmoothingMode = like.SmoothingMode;
                sf.Alignment = ha; sf.LineAlignment = va; sf.FormatFlags = StringFormatFlags.NoWrap;
                int off = Math.Max(1, (int)Math.Round(scale));
                RectangleF r = new RectangleF(TextPad, TextPad, bw - TextPad * 2, bh - TextPad * 2);
                SolidBrush shadow = Brush(Color.FromArgb(220, 0, 0, 0));
                lg.DrawString(text, f, shadow, new RectangleF(r.X - off, r.Y, r.Width, r.Height), sf);
                lg.DrawString(text, f, shadow, new RectangleF(r.X + off, r.Y, r.Width, r.Height), sf);
                lg.DrawString(text, f, shadow, new RectangleF(r.X, r.Y - off, r.Width, r.Height), sf);
                lg.DrawString(text, f, shadow, new RectangleF(r.X, r.Y + off, r.Width, r.Height), sf);
                lg.DrawString(text, f, Brush(color), r, sf);
            }
            TextEntry te = new TextEntry(); te.Bmp = bmp; te.Used = _textTick;
            return te;
        }

        /// <summary>가득 차면 한동안 안 쓴 글자를 비운다. 그래도 많으면 절반 이상을 비운다.</summary>
        private void PurgeText()
        {
            List<string> dead = new List<string>();
            long cutoff = _textTick - 120;
            foreach (KeyValuePair<string, TextEntry> kv in _text) if (kv.Value.Used < cutoff) dead.Add(kv.Key);
            if (dead.Count < _text.Count / 4)
            {
                long mid = _textTick - 20; dead.Clear();
                foreach (KeyValuePair<string, TextEntry> kv in _text) if (kv.Value.Used < mid) dead.Add(kv.Key);
            }
            foreach (string k in dead) { _text[k].Bmp.Dispose(); _text.Remove(k); }
        }

        public void Dispose()
        {
            Small.Dispose(); Normal.Dispose(); Bold.Dispose(); Big.Dispose(); Huge.Dispose(); Center.Dispose();
            foreach (TextEntry te in _text.Values) te.Bmp.Dispose();
            foreach (Font sf in _scaledFonts.Values) sf.Dispose();
            foreach (System.Drawing.Imaging.ImageAttributes ia in _alphaAttr) if (ia != null) ia.Dispose();
            if (_meas != null) { _meas.Dispose(); _measBmp.Dispose(); }
            foreach (SolidBrush b in _brushes.Values) b.Dispose();
            foreach (Pen p in _pens.Values) p.Dispose();
            foreach (Pen p in _dashPens.Values) p.Dispose();
        }
    }

    public static class Noise
    {
        /// <summary>정수 격자 해시 (결정적, 0..uint.MaxValue).</summary>
        public static uint Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }
        public static float Hash01(int x, int y, uint seed) { return (Hash(x, y, seed) & 0xFFFFFF) / 16777216f; }

        /// <summary>부드러운 값 노이즈 (0..1).</summary>
        public static float Value(float x, float y, uint seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Hash01(xi, yi, seed), b = Hash01(xi + 1, yi, seed);
            float c = Hash01(xi, yi + 1, seed), d = Hash01(xi + 1, yi + 1, seed);
            float top = a + (b - a) * tx, bot = c + (d - c) * tx;
            return top + (bot - top) * ty;
        }
    }
}
