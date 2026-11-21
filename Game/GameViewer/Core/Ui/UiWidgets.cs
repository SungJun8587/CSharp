//***************************************************************************
// UiWidgets.cs : 기본 위젯 - 글자, 판, 버튼, 탭, 체크박스, 슬라이더, 수량 조절, 재화 표시.
// 목록/격자처럼 데이터가 많은 위젯은 UiLists.cs.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameViewer.Core.Ui
{
    public sealed class UiLabel : UiWidget
    {
        public string Text = "";
        public Func<string> TextFn;
        public UiTextStyle Style = new UiTextStyle();
        public UiLabel() { }
        public UiLabel(string text, UiRect bounds, UiTextStyle style = null) { Text = text; Bounds = bounds; if (style != null) Style = style; }
        protected override void DrawSelf(IUiCanvas c, UiContext ctx) { c.Text(TextFn != null ? TextFn() : Text, AbsBounds, Style); }
    }

    /// <summary>배경만 있는 판(묶음 표시용).</summary>
    public sealed class UiPanel : UiWidget
    {
        public int Fill = unchecked((int)0x40000000);
        public int Border = unchecked((int)0x30FFFFFF);
        public float Radius = 6;
        public UiPanel(UiRect bounds) { Bounds = bounds; }
        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            UiRect r = AbsBounds;
            c.Fill(r, Fill, Radius);
            if (Border != 0) c.Stroke(r, Border, 1, Radius);
        }
    }

    public enum UiButtonStyle { Normal, Primary, Danger, Flat }

    public sealed class UiButton : UiWidget
    {
        public string Text;
        public Action Click;
        public UiButtonStyle Style = UiButtonStyle.Normal;
        public UiButton(string text, Action click) { Text = text; Click = click; }
        public UiButton(string text, UiRect bounds, Action click) { Text = text; Bounds = bounds; Click = click; }
        public override bool HitSelf { get { return true; } }

        public override bool OnPointerDown(float ax, float ay, int button) { return button == 0; }
        public override void OnPointerUp(float ax, float ay, int button)
        {
            if (button == 0 && Enabled && AbsBounds.Contains(ax, ay) && Click != null) Click();
        }

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            UiRect r = AbsBounds;
            bool hover = IsHover(ctx) && Enabled, down = IsPressed(ctx) && hover;
            int top, bot, border, txt = UiTheme.Text;
            switch (Style)
            {
                case UiButtonStyle.Primary: top = unchecked((int)0xFFC9A04A); bot = unchecked((int)0xFF7A5A20); border = unchecked((int)0xFFF4D98A); txt = unchecked((int)0xFFFFFFFF); break;
                case UiButtonStyle.Danger: top = unchecked((int)0xFFB4483C); bot = unchecked((int)0xFF6A201A); border = unchecked((int)0xFFE88A7E); txt = unchecked((int)0xFFFFFFFF); break;
                case UiButtonStyle.Flat: top = hover ? unchecked((int)0x40FFFFFF) : 0; bot = top; border = 0; break;
                default: top = unchecked((int)0xFF3C4552); bot = unchecked((int)0xFF222831); border = unchecked((int)0xFF68727E); break;
            }
            if (!Enabled) { top = unchecked((int)0xFF2A2F36); bot = unchecked((int)0xFF1C2026); border = unchecked((int)0xFF3A3F46); txt = UiTheme.Disabled; }
            else if (down) { top = Darken(top); bot = Darken(bot); }
            else if (hover && Style != UiButtonStyle.Flat) { top = Lighten(top); bot = Lighten(bot); }
            if ((top >> 24) != 0) c.Gradient(r, top, bot, 5);
            if (border != 0) c.Stroke(r, border, 1, 5);
            if (hover && !down && Style != UiButtonStyle.Flat) c.Stroke(r.Inflate(-1), unchecked((int)0x50FFFFFF), 1, 4);
            c.Text(Text, down ? r.Offset(0, 1) : r, new UiTextStyle(Style == UiButtonStyle.Flat ? 14 : 13, txt, UiAlign.Center, Style != UiButtonStyle.Normal && Style != UiButtonStyle.Flat));
        }

        internal static int Lighten(int argb) { return Mix(argb, 255, 0.18f); }
        internal static int Darken(int argb) { return Mix(argb, 0, 0.25f); }
        private static int Mix(int argb, int to, float t)
        {
            int a = (argb >> 24) & 255, r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
            r += (int)((to - r) * t); g += (int)((to - g) * t); b += (int)((to - b) * t);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }
    }

    public sealed class UiTabBar : UiWidget
    {
        public string[] Tabs;
        public int Selected;
        public Action<int> Changed;
        /// <summary>탭마다 붙는 알림 개수(0 이면 안 보임). 예: 안 읽은 우편 수.</summary>
        public Func<int, int> Badge;
        public UiTabBar(string[] tabs, UiRect bounds, Action<int> changed) { Tabs = tabs; Bounds = bounds; Changed = changed; }
        public override bool HitSelf { get { return true; } }

        public int TabAt(float ax)
        {
            if (Tabs == null || Tabs.Length == 0) return -1;
            float w = Bounds.W / Tabs.Length;
            int i = (int)((ax - AbsX) / w);
            return i < 0 || i >= Tabs.Length ? -1 : i;
        }

        public void Select(int i)
        {
            if (Tabs == null || i < 0 || i >= Tabs.Length || i == Selected) return;
            Selected = i;
            if (Changed != null) Changed(i);
        }

        public override bool OnPointerDown(float ax, float ay, int button) { return button == 0 && TabAt(ax) >= 0; }
        public override void OnPointerUp(float ax, float ay, int button) { if (button == 0 && AbsBounds.Contains(ax, ay)) { int i = TabAt(ax); if (i >= 0) Select(i); } }
        public override bool OnKeyDown(int vk)
        {
            if (Tabs == null) return false;
            if (vk == 9) { Select((Selected + 1) % Tabs.Length); return true; }                       // Tab (방향키는 창 안의 목록/격자가 쓴다)
            return false;
        }

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            if (Tabs == null) return;
            UiRect r = AbsBounds;
            float w = r.W / Tabs.Length;
            int hoverTab = IsHover(ctx) ? TabAt(ctx.PointerX) : -1;
            for (int i = 0; i < Tabs.Length; ++i)
            {
                UiRect t = new UiRect(r.X + i * w + 1, r.Y, w - 2, r.H);
                bool sel = i == Selected;
                if (sel) c.Gradient(t, UiTheme.WithAlpha(UiTheme.Accent, 170), UiTheme.WithAlpha(UiTheme.Accent, 40), 5);
                else c.Fill(t, hoverTab == i ? unchecked((int)0x28FFFFFF) : unchecked((int)0x14FFFFFF), 5);
                if (sel) c.Line(t.X + 4, t.Bottom - 1, t.Right - 4, t.Bottom - 1, UiTheme.Gold, 2);
                c.Text(Tabs[i], t, new UiTextStyle(13, sel ? unchecked((int)0xFFFFFFFF) : UiTheme.TextDim, UiAlign.Center, sel));
                int n = Badge != null ? Badge(i) : 0;
                if (n > 0)
                {
                    UiRect b = new UiRect(t.Right - 18, t.Y + 2, 16, 14);
                    c.Fill(b, unchecked((int)0xFFD8453A), 7);
                    c.Text(n > 99 ? "99" : n.ToString(CultureInfo.InvariantCulture), b, new UiTextStyle(10, unchecked((int)0xFFFFFFFF), UiAlign.Center, true));
                }
            }
        }
    }

    public sealed class UiCheckbox : UiWidget
    {
        public string Text;
        public bool Checked;
        public Action<bool> Changed;
        public UiCheckbox(string text, UiRect bounds, bool value, Action<bool> changed) { Text = text; Bounds = bounds; Checked = value; Changed = changed; }
        public override bool HitSelf { get { return true; } }
        public override bool OnPointerDown(float ax, float ay, int button) { return button == 0; }
        public override void OnPointerUp(float ax, float ay, int button)
        {
            if (button != 0 || !Enabled || !AbsBounds.Contains(ax, ay)) return;
            Checked = !Checked;
            if (Changed != null) Changed(Checked);
        }

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            UiRect r = AbsBounds;
            UiRect box = new UiRect(r.X, r.Y + (r.H - 18) / 2, 18, 18);
            c.Fill(box, UiTheme.SlotBg, 4);
            c.Stroke(box, IsHover(ctx) ? UiTheme.Gold : UiTheme.SlotBorder, 1.5f, 4);
            if (Checked)
            {
                c.Line(box.X + 4, box.Y + 9, box.X + 8, box.Y + 13, UiTheme.Gold, 2.5f);
                c.Line(box.X + 8, box.Y + 13, box.X + 14, box.Y + 5, UiTheme.Gold, 2.5f);
            }
            c.Text(Text, new UiRect(r.X + 26, r.Y, r.W - 26, r.H), new UiTextStyle(13, Enabled ? UiTheme.Text : UiTheme.Disabled));
        }
    }

    /// <summary>값 슬라이더. 라벨 | 트랙 | 값 글자. 끄는 동안 Changed 가 계속 불린다.</summary>
    public sealed class UiSlider : UiWidget
    {
        public string Text;
        public int Min, Max, Step = 1, Value;
        public Action<int> Changed;
        public Func<int, string> Format;
        public float LabelW = 110, ValueW = 52;
        private bool _drag;
        public UiSlider(string text, UiRect bounds, int min, int max, int value, Action<int> changed) { Text = text; Bounds = bounds; Min = min; Max = max; Value = value; Changed = changed; }
        public override bool HitSelf { get { return true; } }

        private UiRect Track { get { UiRect r = AbsBounds; return new UiRect(r.X + LabelW, r.Y, Math.Max(10, r.W - LabelW - ValueW), r.H); } }

        public void SetValue(int v, bool notify)
        {
            if (Step > 1) v = Min + (int)Math.Round((v - Min) / (double)Step) * Step;
            v = Math.Max(Min, Math.Min(Max, v));
            if (v == Value) return;
            Value = v;
            if (notify && Changed != null) Changed(v);
        }

        private void FromX(float ax)
        {
            UiRect t = Track;
            float f = (ax - (t.X + 6)) / Math.Max(1, t.W - 12);
            SetValue(Min + (int)Math.Round(Math.Max(0, Math.Min(1, f)) * (Max - Min)), true);
        }

        public override bool OnPointerDown(float ax, float ay, int button)
        {
            if (button != 0 || !Enabled) return false;
            _drag = true; FromX(ax); return true;
        }
        public override void OnPointerMove(float ax, float ay) { if (_drag) FromX(ax); }
        public override void OnPointerUp(float ax, float ay, int button) { _drag = false; }
        public override bool OnWheel(float ax, float ay, int delta) { if (!Enabled) return false; SetValue(Value + (delta > 0 ? Step : -Step), true); return true; }

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            UiRect r = AbsBounds, t = Track;
            c.Text(Text, new UiRect(r.X, r.Y, LabelW, r.H), new UiTextStyle(13, Enabled ? UiTheme.Text : UiTheme.Disabled));
            float cy = r.CenterY;
            UiRect bar = new UiRect(t.X + 6, cy - 3, t.W - 12, 6);
            c.Fill(bar, UiTheme.SlotBg, 3);
            float f = Max > Min ? (Value - Min) / (float)(Max - Min) : 0;
            if (f > 0) c.Gradient(new UiRect(bar.X, bar.Y, Math.Max(4, bar.W * f), bar.H), UiTheme.Gold, UiTheme.WithAlpha(UiTheme.Accent, 255), 3);
            c.Stroke(bar, UiTheme.SlotBorder, 1, 3);
            float kx = bar.X + bar.W * f;
            bool hot = _drag || IsHover(ctx);
            c.Fill(new UiRect(kx - 7, cy - 7, 14, 14), hot ? unchecked((int)0xFFFFE9A8) : unchecked((int)0xFFE6D29A), 7);
            c.Stroke(new UiRect(kx - 7, cy - 7, 14, 14), unchecked((int)0xFF6E521C), 1.5f, 7);
            c.Text(Format != null ? Format(Value) : Value.ToString(CultureInfo.InvariantCulture), new UiRect(r.Right - ValueW, r.Y, ValueW, r.H), new UiTextStyle(13, UiTheme.TextDim, UiAlign.Right));
        }
    }

    /// <summary>수량 조절: [−] 값 [+] [최대]. Min..Max 로 자른다.</summary>
    public sealed class UiStepper : UiWidget
    {
        public int Min = 1, Max = 99, Value = 1;
        public Action<int> Changed;
        private readonly UiLabel _label;
        public UiStepper(float x, float y, Action<int> changed)
        {
            Bounds = new UiRect(x, y, 150, 26);
            Changed = changed;
            Add(new UiButton("−", new UiRect(0, 0, 26, 26), delegate { Set(Value - 1); }));
            _label = Add(new UiLabel("1", new UiRect(28, 0, 40, 26), new UiTextStyle(14, UiTheme.Text, UiAlign.Center, true)));
            Add(new UiButton("+", new UiRect(70, 0, 26, 26), delegate { Set(Value + 1); }));
            Add(new UiButton("최대", new UiRect(100, 0, 50, 26), delegate { Set(Max); }));
        }

        public void SetRange(int min, int max) { Min = min; Max = Math.Max(min, max); Set(Value, false); }
        public void Set(int v) { Set(v, true); }
        private void Set(int v, bool notify)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            bool changed = v != Value;
            Value = v;
            _label.Text = v.ToString(CultureInfo.InvariantCulture);
            if (changed && notify && Changed != null) Changed(v);
        }
    }

    /// <summary>아이콘 하나를 칸 모양으로 보여 준다(선택 정보, 상점 상세).</summary>
    public sealed class UiIconView : UiWidget
    {
        public UiIconKind Kind = UiIconKind.Item; public uint Id; public int Grade;
        public UiIconView(UiRect bounds) { Bounds = bounds; }
        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            UiRect r = AbsBounds;
            c.Fill(r, UiTheme.SlotBg, 6);
            int border = Id != 0 ? UiTheme.Grade(Grade) : UiTheme.SlotBorder;
            if (Id != 0) c.Gradient(r.Inset(1, 1, 1, 1), UiTheme.WithAlpha(border, 56), UiTheme.WithAlpha(border, 8), 5);
            c.Stroke(r, Id != 0 ? UiTheme.WithAlpha(border, 210) : border, 1.5f, 6);
            if (Id != 0) c.Icon(Kind, Id, r.Inset(6, 6, 6, 6), Grade);
        }
    }

    /// <summary>재화 줄: 보유 재화를 오른쪽 정렬로 "아이콘 숫자" 칩으로 그린다.</summary>
    public sealed class UiCurrencyBar : UiWidget
    {
        public Func<IList<KeyValuePair<uint, ulong>>> Source;
        public UiCurrencyBar(UiRect bounds, Func<IList<KeyValuePair<uint, ulong>>> source) { Bounds = bounds; Source = source; }

        public static string Group(ulong v) { return v.ToString("N0", CultureInfo.InvariantCulture); }

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            IList<KeyValuePair<uint, ulong>> list = Source != null ? Source() : null;
            if (list == null) return;
            UiRect r = AbsBounds;
            UiTextStyle st = new UiTextStyle(13, UiTheme.Text, UiAlign.Right, true);
            float x = r.Right;
            for (int i = list.Count - 1; i >= 0; --i)
            {
                string s = Group(list[i].Value);
                float w = UiTextMetrics.Width(s, 13) + 6;
                c.Text(s, new UiRect(x - w, r.Y, w, r.H), st);
                x -= w + 2;
                c.Icon(UiIconKind.Currency, list[i].Key, new UiRect(x - 18, r.Y + (r.H - 18) / 2, 18, 18), 0);
                x -= 26;
            }
        }
    }
}
