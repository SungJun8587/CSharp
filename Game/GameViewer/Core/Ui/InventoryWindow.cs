//***************************************************************************
// InventoryWindow.cs : 아이템 가방 창. 종류 탭(어둡게 걸러 보임), 칸 끌어서 이동/교환, 더블클릭/우클릭 사용, 선택 정보, 정렬, 재화 줄.
// 창 밖(월드)에 끌어 놓으면 버리기 확인을 띄운다. 모든 변경은 IInventoryBackend 로 요청한다.
//***************************************************************************
using System;
using System.Globalization;

namespace GameViewer.Core.Ui
{
    public sealed class InventoryWindow : UiWindow
    {
        public const string WindowId = "inventory";
        private static readonly string[] TabNames = { "전체", "장비", "소모품", "재료", "기타" };
        private readonly UiBag _bag;
        private readonly UiWallet _wallet;
        private readonly IInventoryBackend _backend;
        private readonly UiSlotGrid _grid;
        private readonly UiIconView _icon;
        private readonly UiLabel _name, _kind, _price, _empty;
        private readonly UiButton _use, _discard;

        public InventoryWindow(UiBag bag, UiWallet wallet, IInventoryBackend backend) : base(WindowId, "가방", 340, 466)
        {
            _bag = bag; _wallet = wallet; _backend = backend;
            UiTabBar tabs = Add(new UiTabBar(TabNames, new UiRect(16, 38, 308, 28), OnTab));
            _grid = Add(new UiSlotGrid(16, 74, 6, bag) { Cell = 48, Gap = 4 });
            _grid.Fit();
            _grid.SelectChanged = delegate { Refresh(); };
            _grid.Activate = UseSlot;
            _grid.Dropped = delegate (UiDragPayload p, int to) { if (p.Source == _grid) _backend.Move(p.Index, to); };
            _grid.DroppedOutside = OnDroppedOutside;

            UiPanel info = Add(new UiPanel(new UiRect(16, 338, 308, 84)));
            _icon = info.Add(new UiIconView(new UiRect(10, 10, 56, 56)));
            _name = info.Add(new UiLabel("", new UiRect(76, 6, 224, 20), new UiTextStyle(14, UiTheme.Text, UiAlign.Left, true)));
            _kind = info.Add(new UiLabel("", new UiRect(76, 26, 224, 16), new UiTextStyle(11, UiTheme.TextDim)));
            _price = info.Add(new UiLabel("", new UiRect(76, 42, 224, 16), new UiTextStyle(11, UiTheme.Gold)));
            _empty = info.Add(new UiLabel("칸을 눌러 아이템을 선택하세요.\n끌어서 옮기고, 더블클릭이나 우클릭으로 씁니다.", new UiRect(14, 10, 280, 60), new UiTextStyle(12, UiTheme.TextDim, UiAlign.Left, false, true)));
            _use = info.Add(new UiButton("사용", new UiRect(142, 52, 76, 26), delegate { UseSlot(_grid.Selected); }) { Style = UiButtonStyle.Primary });
            _discard = info.Add(new UiButton("버리기", new UiRect(224, 52, 76, 26), delegate { ConfirmDiscard(_grid.Selected); }));

            Add(new UiButton("정렬", new UiRect(16, 432, 64, 26), delegate { _backend.Sort(); }));
            Add(new UiLabel("", new UiRect(86, 432, 80, 26), new UiTextStyle(12, UiTheme.TextDim, UiAlign.Left)) { TextFn = delegate { return _bag.Used.ToString(CultureInfo.InvariantCulture) + " / " + _bag.Unlocked.ToString(CultureInfo.InvariantCulture); } });
            Add(new UiCurrencyBar(new UiRect(160, 432, 164, 26), delegate { return _wallet.List; }));
            _bag.Changed += Refresh;
            Refresh();
        }

        public UiSlotGrid Grid { get { return _grid; } }

        public override void OnOpen() { Refresh(); }
        public override void OnClose() { _bag.Changed -= Refresh; _bag.Filter = null; }

        private void OnTab(int i) { _bag.Filter = i == 0 ? (ItemKind?)null : (ItemKind)(i - 1); }

        private void Refresh()
        {
            int s = _grid.Selected;
            bool has = s >= 0 && s < _bag.SlotCount && _bag.Id[s] != 0;       // 빈 칸을 골라 둔 채로도 키보드로 이동할 수 있다(정보는 비움)
            _empty.Visible = !has;
            _icon.Visible = _name.Visible = _kind.Visible = _price.Visible = _use.Visible = _discard.Visible = has;
            if (!has) return;
            ItemInfo info = _bag.Catalog.Get(_bag.Id[s]);
            _icon.Id = _bag.Id[s]; _icon.Grade = info != null ? info.Grade : 0;
            _name.Text = info != null ? info.Name : "알 수 없는 아이템";
            _name.Style = _name.Style.With(UiTheme.Grade(_icon.Grade));
            _kind.Text = info == null ? "" : UiTheme.GradeName(info.Grade) + " · " + ItemInfo.KindName(info.Kind) + (_bag.Cnt[s] > 1 ? "   보유 " + _bag.Cnt[s].ToString(CultureInfo.InvariantCulture) : "");
            _price.Text = info != null && info.SellPrice > 0 ? "판매가 " + info.SellPrice.ToString("N0", CultureInfo.InvariantCulture) + " 골드" : "판매 불가";
            _use.Enabled = info != null && info.Usable;
            _use.Text = info != null && info.Kind == ItemKind.Equip ? "장착" : "사용";
        }

        private void Report(UiResult r)
        {
            UiManager m = Manager;
            if (m != null && r != null && r.Message.Length > 0) m.Toast(r.Message, r.Ok ? UiTheme.Text : UiTheme.Bad);
        }

        private void UseSlot(int slot)
        {
            if (slot < 0 || _bag.Id[slot] == 0) return;
            ItemInfo info = _bag.Catalog.Get(_bag.Id[slot]);
            if (info == null || !info.Usable) { Report(UiResult.Fail("사용할 수 없는 아이템입니다")); return; }
            _backend.Use(slot, Report);
        }

        private void ConfirmDiscard(int slot)
        {
            UiManager m = Manager;
            if (m == null || slot < 0 || _bag.Id[slot] == 0) return;
            ItemInfo info = _bag.Catalog.Get(_bag.Id[slot]);
            uint cnt = _bag.Cnt[slot], id = _bag.Id[slot];
            string label = (info != null ? info.Name : "아이템") + (cnt > 1 ? " " + cnt + "개" : "");
            UiDialog.Confirm(m, "아이템 버리기", label + "을(를) 버립니다.\n버린 아이템은 되찾을 수 없습니다.", "버리기",
                delegate
                {
                    if (_bag.Id[slot] == id) _backend.Discard(slot, _bag.Cnt[slot], Report);       // 그 사이 칸이 바뀌었으면 하지 않는다
                }, true);
        }

        private void OnDroppedOutside(UiDragPayload p)
        {
            UiManager m = Manager;
            if (m == null || m.IsOverWindow(m.Ctx.PointerX, m.Ctx.PointerY)) return;       // 다른 창 위에 놓은 것은 그 창이 받을 일
            ConfirmDiscard(p.Index);
        }

        public override bool OnKeyDown(int vk)
        {
            int s = _grid.Selected, cols = _grid.Columns, n = _bag.Unlocked;
            switch (vk)
            {
                case 37: _grid.SelectSlot(s < 0 ? 0 : Math.Max(0, s - 1)); return true;
                case 39: _grid.SelectSlot(s < 0 ? 0 : Math.Min(n - 1, s + 1)); return true;
                case 38: _grid.SelectSlot(s < 0 ? 0 : Math.Max(0, s - cols)); return true;
                case 40: _grid.SelectSlot(s < 0 ? 0 : Math.Min(n - 1, s + cols)); return true;
                case 13: UseSlot(s); return true;
                case 46: ConfirmDiscard(s); return true;
            }
            return false;
        }
    }
}
