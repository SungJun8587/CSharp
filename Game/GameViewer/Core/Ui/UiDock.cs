//***************************************************************************
// UiDock.cs : 전투 화면 한쪽에 늘 떠 있는 창 열기 단추 줄(가방/스킬/우편/상점/설정). 단축키 없이 클릭으로 창을 연다.
// 창이 아니라 관리자가 직접 그리고 입력을 받는다(창 위로는 클릭이 가지 않고, 모달 대화상자가 떠 있으면 눌리지 않는다).
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.Core.Ui
{
    public sealed class UiDockItem
    {
        public string Label;
        public UiIconKind Icon; public uint IconId;
        public Func<string> Hotkey;            // 키 설정을 따라가도록 그릴 때마다 읽는다(없으면 표시 안 함)
        public Action Click;
        public Func<bool> IsOpen;              // 열려 있으면 강조
        public Func<int> Badge;                // 0 보다 크면 우상단에 숫자(안 읽은 우편 등)
    }

    public sealed class UiDock
    {
        public const float Size = 40, Gap = 6, Margin = 12;
        public readonly List<UiDockItem> Items = new List<UiDockItem>();
        public float Top = 10;                 // 화면 위에서의 거리(맨 위 줄. 재화와 도움말 칩은 이 아래에 쌓는다: HudRenderer.WalletTop)
        public bool Visible = true;
        public int Hover = -1;                 // 마우스가 올라간 단추(없으면 -1)
        internal int Down = -1;

        public UiDockItem Add(string label, UiIconKind icon, Action click, Func<string> hotkey = null, Func<bool> isOpen = null, Func<int> badge = null, uint iconId = 0)
        {
            UiDockItem it = new UiDockItem { Label = label, Icon = icon, IconId = iconId, Click = click, Hotkey = hotkey, IsOpen = isOpen, Badge = badge };
            Items.Add(it);
            return it;
        }

        public UiRect RectOf(int i, float screenWidth)
        {
            float total = Items.Count * Size + (Items.Count - 1) * Gap;
            return new UiRect(screenWidth - Margin - total + i * (Size + Gap), Top, Size, Size);
        }

        public int IndexAt(float x, float y, float screenWidth)
        {
            if (!Visible) return -1;
            for (int i = 0; i < Items.Count; ++i) if (RectOf(i, screenWidth).Contains(x, y)) return i;
            return -1;
        }

        internal void Draw(IUiCanvas c, float screenWidth)
        {
            if (!Visible) return;
            for (int i = 0; i < Items.Count; ++i)
            {
                UiDockItem it = Items[i];
                UiRect r = RectOf(i, screenWidth);
                bool open = it.IsOpen != null && it.IsOpen(), hot = i == Hover, down = i == Down && hot;
                c.Fill(r, down ? unchecked((int)0xE0383E4C) : (hot ? unchecked((int)0xE0262C3A) : unchecked((int)0xC0141820)), 7);
                c.Stroke(r, open ? UiTheme.Gold : (hot ? UiTheme.WithAlpha(UiTheme.Gold, 170) : unchecked((int)0x60FFFFFF)), open ? 2 : 1.2f, 7);
                c.Icon(it.Icon, it.IconId, r.Inset(6, 5, 6, 9), 0);
                string hk = it.Hotkey != null ? it.Hotkey() : null;
                if (!string.IsNullOrEmpty(hk)) c.Text(hk, new UiRect(r.X, r.Bottom - 13, r.W, 12), new UiTextStyle(10, UiTheme.TextDim, UiAlign.Center, true));
                int b = it.Badge != null ? it.Badge() : 0;
                if (b > 0)
                {
                    UiRect br = new UiRect(r.Right - 12, r.Y - 5, 18, 16);
                    c.Fill(br, UiTheme.Bad, 8);
                    c.Text(b > 99 ? "99+" : b.ToString(), br, new UiTextStyle(10, unchecked((int)0xFFFFFFFF), UiAlign.Center, true));
                }
            }
            if (Hover >= 0 && Hover < Items.Count)
            {
                string label = Items[Hover].Label;
                float w = UiTextMetrics.Width(label, 12) + 16;
                UiRect r = RectOf(Hover, screenWidth);
                UiRect tr = new UiRect(Math.Max(4, r.CenterX - w / 2), r.Bottom + 6, w, 22);
                c.Fill(tr, unchecked((int)0xE60C1016), 5);
                c.Stroke(tr, UiTheme.WithAlpha(UiTheme.Gold, 140), 1, 5);
                c.Text(label, tr, new UiTextStyle(12, UiTheme.Text, UiAlign.Center));
            }
        }
    }
}
