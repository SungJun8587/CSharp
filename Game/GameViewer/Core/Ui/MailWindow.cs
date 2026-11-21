//***************************************************************************
// MailWindow.cs : 우편함. 목록(안 읽음 점, 첨부 아이콘, 남은 기간), 본문(스크롤), 첨부 칸, 수령/모두 수령/읽은 우편 삭제.
// 첨부 수령은 전부 받거나 전부 못 받는다(가방 공간 부족이면 사유를 알리고 아무것도 바꾸지 않는다). 모든 변경은 IMailBackend 로 요청한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameViewer.Core.Ui
{
    public sealed class MailWindow : UiWindow
    {
        public const string WindowId = "mail";
        private readonly UiMailbox _box;
        private readonly ItemCatalog _catalog;
        private readonly IMailBackend _backend;
        private readonly UiScrollList _list, _body;
        private readonly UiLabel _title, _meta, _empty, _attachLabel;
        private readonly UiSlotGrid _attach;
        private readonly UiButton _claim, _claimAll, _deleteRead;
        private readonly MailAttachSource _src = new MailAttachSource();
        private List<string> _lines = new List<string>();
        private uint _selId;

        public MailWindow(UiMailbox box, ItemCatalog catalog, IMailBackend backend) : base(WindowId, "우편함", 640, 452)
        {
            _box = box; _catalog = catalog; _backend = backend;
            _list = Add(new UiScrollList(new UiRect(16, 42, 236, 356), 58, delegate { return _box.Mails.Count; }));
            _list.DrawRow = DrawRow;
            _list.SelectChanged = OnSelect;

            UiPanel det = Add(new UiPanel(new UiRect(264, 42, 360, 356)));
            _title = det.Add(new UiLabel("", new UiRect(14, 10, 332, 24), new UiTextStyle(16, UiTheme.TextTitle, UiAlign.Left, true)));
            _meta = det.Add(new UiLabel("", new UiRect(14, 36, 332, 16), new UiTextStyle(11, UiTheme.TextDim)));
            _body = det.Add(new UiScrollList(new UiRect(14, 62, 332, 168), 19, delegate { return _lines.Count; }) { Plain = true });
            _body.DrawRow = delegate (IUiCanvas c, int i, UiRect r, bool s, bool h) { c.Text(_lines[i], r, new UiTextStyle(13, UiTheme.Text)); };
            _attachLabel = det.Add(new UiLabel("첨부", new UiRect(14, 240, 100, 16), new UiTextStyle(11, UiTheme.TextDim)));
            _attach = det.Add(new UiSlotGrid(14, 260, 5, _src) { Cell = 44, Gap = 4 });
            _attach.Fit();
            _claim = det.Add(new UiButton("첨부 받기", new UiRect(226, 312, 120, 32), DoClaim) { Style = UiButtonStyle.Primary });
            _empty = det.Add(new UiLabel("우편을 선택하세요.", new UiRect(0, 140, 360, 24), new UiTextStyle(13, UiTheme.TextDim, UiAlign.Center)));

            _claimAll = Add(new UiButton("모두 받기", new UiRect(16, 410, 110, 28), delegate { _backend.ClaimAll(Report); }) { Style = UiButtonStyle.Primary });
            _deleteRead = Add(new UiButton("읽은 우편 삭제", new UiRect(134, 410, 118, 28), delegate { ConfirmDeleteRead(); }));
            Add(new UiLabel("", new UiRect(264, 410, 360, 28), new UiTextStyle(12, UiTheme.TextDim, UiAlign.Right))
            { TextFn = delegate { return "우편 " + _box.Mails.Count + "통 · 안 읽음 " + _box.Unread + " · 받을 첨부 " + _box.Unclaimed; } });
            _box.Changed += OnBoxChanged;
            Detail();
        }

        public UiScrollList List { get { return _list; } }
        public UiButton ClaimButton { get { return _claim; } }
        public UiButton ClaimAllButton { get { return _claimAll; } }
        public UiSlotGrid Attach { get { return _attach; } }

        public override void OnOpen() { Detail(); }
        public override void OnClose() { _box.Changed -= OnBoxChanged; }

        private UiMail Selected { get { return _list.Selected >= 0 && _list.Selected < _box.Mails.Count ? _box.Mails[_list.Selected] : null; } }

        private void OnSelect(int i)
        {
            UiMail m = Selected;
            _selId = m != null ? m.Id : 0;
            _body.ScrollTo(0, true);
            if (m != null && !m.Read) _backend.Read(m.Id);
            Detail();
        }

        private void OnBoxChanged()
        {
            // 목록이 바뀌어도(삭제 등) 같은 우편을 계속 선택한다
            int idx = _selId == 0 ? -1 : _box.Mails.FindIndex(x => x.Id == _selId);
            _list.Selected = idx;
            if (idx < 0) _selId = 0;
            _list.ScrollTo(_list.Scroll);
            Detail();
        }

        private void DrawRow(IUiCanvas c, int i, UiRect r, bool sel, bool hover)
        {
            if (i >= _box.Mails.Count) return;
            UiMail m = _box.Mails[i];
            UiIconKind ik = m.Claimable ? UiIconKind.Gift : (m.Read ? UiIconKind.MailOpen : UiIconKind.Mail);
            c.Icon(ik, 0, new UiRect(r.X + 8, r.Y + 11, 34, 34), 0);
            int col = m.Read ? UiTheme.TextDim : UiTheme.Text;
            c.Text(m.Title, new UiRect(r.X + 50, r.Y + 8, r.W - 50 - 18, 20), new UiTextStyle(13, col, UiAlign.Left, !m.Read));
            string sub = m.Sender + "  ·  " + (m.DaysLeft <= 0 ? "오늘 만료" : m.DaysLeft + "일 남음");
            c.Text(sub, new UiRect(r.X + 50, r.Y + 29, r.W - 50 - 6, 16), new UiTextStyle(11, m.DaysLeft <= 3 ? UiTheme.Bad : UiTheme.TextDim));
            if (!m.Read) c.Fill(new UiRect(r.Right - 16, r.Y + 11, 8, 8), unchecked((int)0xFFE0503F), 4);
        }

        private void Detail()
        {
            UiMail m = Selected;
            bool has = m != null;
            _empty.Visible = !has;
            _title.Visible = _meta.Visible = _body.Visible = has;
            _attachLabel.Visible = _attach.Visible = _claim.Visible = has && m.HasItems;
            _claimAll.Enabled = _box.Unclaimed > 0;
            _deleteRead.Enabled = _box.Mails.Exists(x => x.Read && !x.Claimable);
            if (!has) return;
            _title.Text = m.Title;
            _meta.Text = m.Sender + "  ·  " + m.SentText;
            _lines = UiTextMetrics.Wrap(m.Body, 13, 318);
            _src.Set(m, _catalog);
            _attach.Fit();
            _claim.Enabled = m.Claimable;
            _claim.Text = m.Claimed ? "수령 완료" : "첨부 받기";
        }

        private void Report(UiResult r)
        {
            UiManager mg = Manager;
            if (mg != null && r != null && r.Message.Length > 0) mg.Toast(r.Message, r.Ok ? UiTheme.Text : UiTheme.Bad);
        }

        private void DoClaim() { UiMail m = Selected; if (m != null && m.Claimable) _backend.Claim(m.Id, Report); }

        private void ConfirmDeleteRead()
        {
            UiManager mg = Manager;
            if (mg == null) return;
            UiDialog.Confirm(mg, "읽은 우편 삭제", "읽은 우편을 모두 삭제합니다.\n받지 않은 첨부가 있는 우편은 지워지지 않습니다.", "삭제", delegate { _backend.DeleteRead(Report); }, true);
        }

        public override bool OnKeyDown(int vk)
        {
            if (vk == 38) { _list.SelectRow(Math.Max(0, _list.Selected - 1)); return true; }
            if (vk == 40) { _list.SelectRow(_list.Selected + 1); return true; }
            if (vk == 13) { DoClaim(); return true; }
            return false;
        }

        /// <summary>선택한 우편의 첨부를 칸으로 보여 준다(재화는 재화 아이콘).</summary>
        private sealed class MailAttachSource : IUiSlotSource
        {
            private UiMail _m; private ItemCatalog _cat;
            public void Set(UiMail m, ItemCatalog cat) { _m = m; _cat = cat; }
            public int SlotCount { get { return _m == null ? 0 : Math.Max(1, _m.Items.Count); } }
            public UiSlotView GetSlot(int i)
            {
                if (_m == null || i >= _m.Items.Count) return new UiSlotView();
                UiMailItem it = _m.Items[i];
                ItemInfo info = it.IsCurrency ? null : _cat.Get(it.Id);
                return new UiSlotView { ItemId = it.Id, Count = it.Count, Grade = info != null ? info.Grade : 0, Icon = it.IsCurrency ? UiIconKind.Currency : UiIconKind.Item, Dimmed = _m.Claimed };
            }
            public UiTooltip GetSlotTooltip(int i)
            {
                if (_m == null || i >= _m.Items.Count) return null;
                UiMailItem it = _m.Items[i];
                if (it.IsCurrency) return new UiTooltip().Add(UiWallet.Name(it.Id), UiTheme.Gold, 13).Add(it.Count.ToString("N0", CultureInfo.InvariantCulture), UiTheme.Text, 12);
                return UiBag.ItemTip(_cat.Get(it.Id), it.Id, it.Count);
            }
        }
    }
}
