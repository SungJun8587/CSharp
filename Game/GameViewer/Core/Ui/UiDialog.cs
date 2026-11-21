//***************************************************************************
// UiDialog.cs : 확인 대화상자(모달). Enter = 확인, Esc = 취소.
//***************************************************************************
using System;

namespace GameViewer.Core.Ui
{
    public sealed class UiDialog : UiWindow
    {
        private readonly Action _yes;

        private UiDialog(string title, string message, string yesText, Action yes, bool danger, string noText)
            : base("dialog", title, 340, 150)
        {
            Modal = true; Accent = danger ? unchecked((int)0xFFC8584A) : UiTheme.Accent;
            _yes = yes;
            float h = UiTextMetrics.WrappedHeight(message, 13, 300) + 8;
            SetSize(340, UiWindow.TitleH + 20 + h + 20 + 30 + 18);
            Add(new UiLabel(message, new UiRect(20, UiWindow.TitleH + 20, 300, h), new UiTextStyle(13, UiTheme.Text, UiAlign.Left, false, true)));
            float by = Bounds.H - 18 - 30;
            Add(new UiButton(yesText, new UiRect(Bounds.W / 2 - 128, by, 120, 30), Accept) { Style = danger ? UiButtonStyle.Danger : UiButtonStyle.Primary });
            Add(new UiButton(noText, new UiRect(Bounds.W / 2 + 8, by, 120, 30), Close));
        }

        private void Accept() { Action y = _yes; Close(); if (y != null) y(); }

        public override bool OnKeyDown(int vk)
        {
            if (vk == 13) { Accept(); return true; }
            return false;
        }

        public static UiDialog Confirm(UiManager m, string title, string message, string yesText, Action onYes, bool danger = false, string noText = "취소")
        {
            UiDialog d = new UiDialog(title, message, yesText, onYes, danger, noText);
            m.Show(d);
            return d;
        }
    }
}
