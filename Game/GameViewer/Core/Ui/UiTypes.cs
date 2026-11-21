//***************************************************************************
// UiTypes.cs : UI 툴킷의 기본 값 형식(사각형, 글자 모양, 색 테마, 캔버스 인터페이스).
// 렌더러(GDI+/Unity/시험용 기록 캔버스)는 IUiCanvas 만 구현하면 된다. 툴킷 코어는 그리기 라이브러리를 모른다.
//***************************************************************************
using System;

namespace GameViewer.Core.Ui
{
    public struct UiRect
    {
        public float X, Y, W, H;
        public UiRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right { get { return X + W; } }
        public float Bottom { get { return Y + H; } }
        public float CenterX { get { return X + W * 0.5f; } }
        public float CenterY { get { return Y + H * 0.5f; } }
        public bool Contains(float px, float py) { return px >= X && py >= Y && px < X + W && py < Y + H; }
        public UiRect Offset(float dx, float dy) { return new UiRect(X + dx, Y + dy, W, H); }
        public UiRect Inflate(float d) { return new UiRect(X - d, Y - d, W + d * 2, H + d * 2); }
        public UiRect Inset(float l, float t, float r, float b) { return new UiRect(X + l, Y + t, Math.Max(0, W - l - r), Math.Max(0, H - t - b)); }
        public override string ToString() { return X + "," + Y + " " + W + "x" + H; }
    }

    public enum UiAlign { Left, Center, Right }

    public sealed class UiTextStyle
    {
        public float Size = 13;
        public int Argb = UiTheme.Text;
        public UiAlign Align = UiAlign.Left;
        public bool Bold;
        public bool Wrap;                 // true 면 폭에 맞춰 줄바꿈하고 위쪽부터 채운다. false 면 한 줄이고 세로 가운데에 놓으며 넘치면 ... 로 줄인다.

        public UiTextStyle() { }
        public UiTextStyle(float size, int argb, UiAlign align = UiAlign.Left, bool bold = false, bool wrap = false) { Size = size; Argb = argb; Align = align; Bold = bold; Wrap = wrap; }
        public UiTextStyle With(int argb) { return new UiTextStyle(Size, argb, Align, Bold, Wrap); }
    }

    public enum UiIconKind { None, Item, Skill, Currency, Mail, MailOpen, Gift, Coin, Bag, Shop, Gear }

    /// <summary>색 테마(ARGB). 등급 색, 창 강조색, 글자 색을 한 곳에 둔다.</summary>
    public static class UiTheme
    {
        public const int Text = unchecked((int)0xFFE6EBE6);
        public const int TextDim = unchecked((int)0xFF9AA79F);
        public const int TextTitle = unchecked((int)0xFFFFE896);
        public const int Good = unchecked((int)0xFF96EBA0);
        public const int Bad = unchecked((int)0xFFFF7E6E);
        public const int Gold = unchecked((int)0xFFF0C860);
        public const int Accent = unchecked((int)0xFFC8A55A);
        public const int Backdrop = unchecked((int)0x96000000);
        public const int SlotBg = unchecked((int)0xFF10141A);
        public const int SlotBorder = unchecked((int)0xFF3A424C);
        public const int Hover = unchecked((int)0x30FFFFFF);
        public const int Select = unchecked((int)0xFFF0C860);
        public const int Row = unchecked((int)0x14FFFFFF);
        public const int RowAlt = unchecked((int)0x08FFFFFF);
        public const int Disabled = unchecked((int)0xFF5A626A);

        public static int ArgbOf(int a, int r, int g, int b) { return (a << 24) | (r << 16) | (g << 8) | b; }
        public static int WithAlpha(int argb, int a) { return (a << 24) | (argb & 0xFFFFFF); }

        /// <summary>아이템 등급 색: 0 일반, 1 고급, 2 희귀, 3 영웅, 4 전설.</summary>
        public static int Grade(int grade)
        {
            switch (grade)
            {
                case 1: return unchecked((int)0xFF6FD66F);
                case 2: return unchecked((int)0xFF4FA3FF);
                case 3: return unchecked((int)0xFFB86CFF);
                case 4: return unchecked((int)0xFFFF9B3C);
                default: return unchecked((int)0xFFC9D0D0);
            }
        }

        public static string GradeName(int grade)
        {
            switch (grade) { case 1: return "고급"; case 2: return "희귀"; case 3: return "영웅"; case 4: return "전설"; default: return "일반"; }
        }
    }

    /// <summary>글자 크기 추정(렌더러가 없을 때와 시험용). 한글/전각은 글자 크기만큼, 영문/숫자는 0.55배로 본다.</summary>
    public static class UiTextMetrics
    {
        public static float Width(string s, float size)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            float w = 0;
            foreach (char c in s) w += c >= 0x1100 ? size : (c == ' ' ? size * 0.3f : size * 0.55f);
            return w;
        }

        /// <summary>폭 maxWidth 에 맞춰 줄바꿈한 줄 목록(\n 도 줄바꿈). 한 글자도 안 들어가는 폭이어도 줄마다 최소 한 글자는 넣는다.</summary>
        public static System.Collections.Generic.List<string> Wrap(string s, float size, float maxWidth)
        {
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
            if (s == null) return lines;
            foreach (string para in s.Replace("\r\n", "\n").Split('\n'))
            {
                if (para.Length == 0) { lines.Add(""); continue; }
                int start = 0;
                float w = 0;
                for (int i = 0; i < para.Length; ++i)
                {
                    float cw = Width(para[i].ToString(), size);
                    if (w + cw > maxWidth && i > start) { lines.Add(para.Substring(start, i - start)); start = i; w = 0; }
                    w += cw;
                }
                lines.Add(para.Substring(start));
            }
            return lines;
        }

        public static float LineHeight(float size) { return size * 1.35f; }
        public static float WrappedHeight(string s, float size, float maxWidth) { return Wrap(s, size, maxWidth).Count * LineHeight(size); }
    }

    /// <summary>그리기 부품. 좌표는 모두 화면 절대 좌표다.</summary>
    public interface IUiCanvas
    {
        void Fill(UiRect r, int argb, float radius = 0);
        void Gradient(UiRect r, int topArgb, int bottomArgb, float radius = 0);
        void Stroke(UiRect r, int argb, float width = 1, float radius = 0);
        /// <summary>금속 테두리를 두른 어두운 판(창, 패널).</summary>
        void Panel(UiRect r, int accentArgb);
        void Line(float x1, float y1, float x2, float y2, int argb, float width = 1);
        void Text(string s, UiRect r, UiTextStyle style);
        /// <summary>글자가 차지하는 크기. wrap 이면 maxWidth 안에서 줄바꿈한 높이.</summary>
        void Measure(string s, UiTextStyle style, float maxWidth, out float width, out float height);
        void Icon(UiIconKind kind, uint id, UiRect r, int grade);
        void PushClip(UiRect r);
        void PopClip();
        void PushScale(float scale, float cx, float cy);
        void PopScale();
    }
}
