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
        public float MouseWorldX, MouseWorldY;
        public bool ShowHelp = true;
        public bool ShowLog = true;
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
        public void OutlinedText(Graphics g, string text, Font font, Color color, float x, float y, StringFormat fmt)
        {
            SolidBrush shadow = Brush(Color.FromArgb(Math.Min(220, (int)color.A), 0, 0, 0));
            g.DrawString(text, font, shadow, x - 1, y, fmt);
            g.DrawString(text, font, shadow, x + 1, y, fmt);
            g.DrawString(text, font, shadow, x, y - 1, fmt);
            g.DrawString(text, font, shadow, x, y + 1, fmt);
            g.DrawString(text, font, Brush(color), x, y, fmt);
        }

        public void Dispose()
        {
            Small.Dispose(); Normal.Dispose(); Bold.Dispose(); Big.Dispose(); Huge.Dispose(); Center.Dispose();
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
