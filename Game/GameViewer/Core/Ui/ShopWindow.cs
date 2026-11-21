//***************************************************************************
// ShopWindow.cs : 상점 창. 구매/판매 탭, 목록, 상세(수량 조절, 합계, 부족 사유), 처리 중 표시.
// 구매/판매는 IShopBackend 로 요청하고 결과 메시지를 토스트로 보여 준다(서버가 정답이므로 요청 중에는 버튼을 막는다).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameViewer.Core.Ui
{
    public sealed class ShopWindow : UiWindow
    {
        public const string WindowId = "shop";
        private readonly string _shopName;
        private readonly List<UiShopEntry> _entries;
        private readonly UiBag _bag;
        private readonly UiWallet _wallet;
        private readonly IShopBackend _backend;
        private readonly UiTabBar _tabs;
        private readonly UiScrollList _list;
        private readonly UiIconView _icon;
        private readonly UiLabel _name, _kind, _desc, _unit, _total, _reason, _hint;
        private readonly UiStepper _qty;
        private readonly UiButton _act;
        private readonly List<int> _sell = new List<int>();             // 판매 탭에 보이는 가방 칸 번호
        private bool _pending;
        private uint _selItem;                                           // 선택 유지용(목록이 바뀌어도 같은 물건 선택)

        public ShopWindow(string shopName, List<UiShopEntry> entries, UiBag bag, UiWallet wallet, IShopBackend backend) : base(WindowId, shopName, 600, 452)
        {
            _shopName = shopName; _entries = entries; _bag = bag; _wallet = wallet; _backend = backend;
            _tabs = Add(new UiTabBar(new string[] { "구매", "판매" }, new UiRect(16, 38, 180, 28), delegate { _list.Selected = -1; _list.ScrollTo(0, true); RebuildSell(); Detail(); }));
            Add(new UiCurrencyBar(new UiRect(300, 38, 284, 28), delegate { return _wallet.List; }));

            _list = Add(new UiScrollList(new UiRect(16, 76, 322, 330), 54, delegate { return _tabs.Selected == 0 ? _entries.Count : _sell.Count; }));
            _list.DrawRow = DrawRow;
            _list.SelectChanged = delegate { Detail(); };
            _list.Activate = delegate { if (_act.Enabled) DoAction(); };

            UiPanel det = Add(new UiPanel(new UiRect(350, 76, 234, 330)));
            _icon = det.Add(new UiIconView(new UiRect(14, 14, 60, 60)));
            _name = det.Add(new UiLabel("", new UiRect(84, 16, 142, 22), new UiTextStyle(14, UiTheme.Text, UiAlign.Left, true)));
            _kind = det.Add(new UiLabel("", new UiRect(84, 40, 142, 16), new UiTextStyle(11, UiTheme.TextDim)));
            _desc = det.Add(new UiLabel("", new UiRect(14, 84, 206, 78), new UiTextStyle(12, UiTheme.Text, UiAlign.Left, false, true)));
            _unit = det.Add(new UiLabel("", new UiRect(14, 170, 206, 18), new UiTextStyle(12, UiTheme.TextDim)));
            _qty = det.Add(new UiStepper(14, 194, delegate { Detail(); }));
            _total = det.Add(new UiLabel("", new UiRect(14, 228, 206, 22), new UiTextStyle(15, UiTheme.Gold, UiAlign.Right, true)));
            _reason = det.Add(new UiLabel("", new UiRect(14, 254, 206, 18), new UiTextStyle(12, UiTheme.Bad, UiAlign.Right)));
            _act = det.Add(new UiButton("구매", new UiRect(14, 282, 206, 36), DoAction) { Style = UiButtonStyle.Primary });
            _hint = det.Add(new UiLabel("왼쪽 목록에서 물건을 고르세요.", new UiRect(14, 14, 206, 40), new UiTextStyle(12, UiTheme.TextDim, UiAlign.Left, false, true)));
            Add(new UiLabel("더블클릭하면 한 번에 거래합니다.", new UiRect(16, 414, 322, 24), new UiTextStyle(11, UiTheme.TextDim)));
            _bag.Changed += OnModelChanged;
            RebuildSell();
            Detail();
        }

        public UiScrollList List { get { return _list; } }
        public UiTabBar Tabs { get { return _tabs; } }
        public UiStepper Qty { get { return _qty; } }
        public UiButton Action { get { return _act; } }
        public bool Pending { get { return _pending; } }

        public override void OnClose() { _bag.Changed -= OnModelChanged; }

        private bool Buying { get { return _tabs.Selected == 0; } }

        private void OnModelChanged() { RebuildSell(); Detail(); }

        private void RebuildSell()
        {
            int keep = _list != null && !Buying && _list.Selected >= 0 && _list.Selected < _sell.Count ? _sell[_list.Selected] : -1;
            _sell.Clear();
            for (int i = 0; i < _bag.Unlocked; ++i)
            {
                if (_bag.Id[i] == 0) continue;
                ItemInfo info = _bag.Catalog.Get(_bag.Id[i]);
                if (info != null && info.SellPrice > 0) _sell.Add(i);
            }
            if (_list != null && !Buying)
            {
                int idx = _sell.IndexOf(keep);
                if (idx < 0 && _list.Selected >= _sell.Count) idx = _sell.Count - 1;
                if (idx >= 0 || _list.Selected >= 0) _list.Selected = idx;
                _list.ScrollTo(_list.Scroll, false);
            }
        }

        private ItemInfo SelInfo(out uint itemId, out uint have, out uint unitPrice, out uint currency)
        {
            itemId = 0; have = 0; unitPrice = 0; currency = 1;
            int sel = _list.Selected;
            if (sel < 0) return null;
            if (Buying)
            {
                if (sel >= _entries.Count) return null;
                itemId = _entries[sel].ItemId; unitPrice = _entries[sel].Price; currency = _entries[sel].CurrencyId;
                have = _bag.CountOf(itemId);
            }
            else
            {
                if (sel >= _sell.Count) return null;
                int slot = _sell[sel];
                itemId = _bag.Id[slot]; have = _bag.Cnt[slot];
                ItemInfo si = _bag.Catalog.Get(itemId);
                unitPrice = si != null ? si.SellPrice : 0;
            }
            return _bag.Catalog.Get(itemId);
        }

        private void DrawRow(IUiCanvas c, int i, UiRect r, bool sel, bool hover)
        {
            uint id, have, price, cur;
            uint itemId; ItemInfo info;
            if (Buying) { if (i >= _entries.Count) return; itemId = _entries[i].ItemId; price = _entries[i].Price; cur = _entries[i].CurrencyId; have = _bag.CountOf(itemId); }
            else
            {
                if (i >= _sell.Count) return;
                int slot = _sell[i]; itemId = _bag.Id[slot]; have = _bag.Cnt[slot]; cur = 1;
                ItemInfo si = _bag.Catalog.Get(itemId); price = si != null ? si.SellPrice : 0;
            }
            id = itemId;
            info = _bag.Catalog.Get(itemId);
            int g = info != null ? info.Grade : 0;
            UiRect ic = new UiRect(r.X + 6, r.Y + 5, 40, 40);
            c.Fill(ic, UiTheme.SlotBg, 5);
            c.Stroke(ic, UiTheme.WithAlpha(UiTheme.Grade(g), 200), 1.5f, 5);
            c.Icon(UiIconKind.Item, id, ic.Inset(4, 4, 4, 4), g);
            c.Text(info != null ? info.Name : "알 수 없음", new UiRect(r.X + 54, r.Y + 6, r.W - 54 - 96, 18), new UiTextStyle(13, UiTheme.Grade(g), UiAlign.Left, true));
            string sub = info != null ? ItemInfo.KindName(info.Kind) : "";
            if (Buying) { if (have > 0) sub += "  · 보유 " + have; } else sub += "  · " + have + "개";
            c.Text(sub, new UiRect(r.X + 54, r.Y + 25, r.W - 54 - 96, 16), new UiTextStyle(11, UiTheme.TextDim));
            bool afford = !Buying || _wallet.Get(cur) >= price;
            string ps = price.ToString("N0", CultureInfo.InvariantCulture);
            float w = UiTextMetrics.Width(ps, 13) + 4;
            c.Text(ps, new UiRect(r.Right - 8 - w, r.Y, w, r.H - 2), new UiTextStyle(13, afford ? UiTheme.Text : UiTheme.Bad, UiAlign.Right, true));
            c.Icon(UiIconKind.Currency, cur, new UiRect(r.Right - 8 - w - 22, r.Y + (r.H - 2 - 18) / 2, 18, 18), 0);
        }

        private void Detail()
        {
            uint itemId, have, unit, cur;
            ItemInfo info = SelInfo(out itemId, out have, out unit, out cur);
            bool has = info != null;
            _hint.Visible = !has;
            _icon.Visible = _name.Visible = _kind.Visible = _desc.Visible = _unit.Visible = _qty.Visible = _total.Visible = _reason.Visible = _act.Visible = has;
            if (!has) return;
            _icon.Id = itemId; _icon.Grade = info.Grade;
            _name.Text = info.Name; _name.Style = _name.Style.With(UiTheme.Grade(info.Grade));
            _kind.Text = UiTheme.GradeName(info.Grade) + " · " + ItemInfo.KindName(info.Kind);
            _desc.Text = info.Desc;
            _unit.Text = (Buying ? "개당 " : "개당 판매가 ") + unit.ToString("N0", CultureInfo.InvariantCulture) + " " + UiWallet.Name(cur);

            ulong funds = _wallet.Get(cur);
            int maxQ;
            if (Buying)
            {
                ulong afford = unit == 0 ? 99 : funds / unit;
                maxQ = (int)Math.Max(1, Math.Min(Math.Min(afford, 99), info.MaxStack));
            }
            else maxQ = (int)Math.Max(1, have);
            _qty.SetRange(1, maxQ);

            ulong total = (ulong)unit * (ulong)_qty.Value;
            _total.Text = total.ToString("N0", CultureInfo.InvariantCulture) + " " + UiWallet.Name(cur);
            string reason = "";
            if (_pending) reason = "처리 중…";
            else if (Buying)
            {
                if (funds < total) reason = UiWallet.Name(cur) + "이(가) 부족합니다";
                else if (!_bag.CanAdd(itemId, (uint)_qty.Value)) reason = "가방에 공간이 부족합니다";
            }
            _total.Style = _total.Style.With(Buying && funds < total ? UiTheme.Bad : UiTheme.Gold);
            _reason.Text = reason;
            _reason.Style = _reason.Style.With(_pending ? UiTheme.TextDim : UiTheme.Bad);
            _act.Text = _pending ? "처리 중…" : (Buying ? "구매" : "판매");
            _act.Enabled = !_pending && reason.Length == 0;
        }

        private void DoAction()
        {
            uint itemId, have, unit, cur;
            ItemInfo info = SelInfo(out itemId, out have, out unit, out cur);
            if (info == null || _pending || !_act.Enabled) return;
            uint count = (uint)_qty.Value;
            _pending = true;
            Detail();
            Action<UiResult> done = delegate (UiResult r)
            {
                _pending = false;
                UiManager m = Manager;
                if (m != null && r.Message.Length > 0) m.Toast(r.Message, r.Ok ? UiTheme.Text : UiTheme.Bad);
                if (r.Ok) _qty.Set(1);
                RebuildSell();
                Detail();
            };
            if (Buying) _backend.Buy(_list.Selected, count, done);
            else _backend.Sell(_sell[_list.Selected], count, done);
        }

        public override bool OnKeyDown(int vk)
        {
            if (vk == 38) { _list.SelectRow(Math.Max(0, _list.Selected - 1)); return true; }
            if (vk == 40) { _list.SelectRow(_list.Selected + 1); return true; }
            if (vk == 37) { _qty.Set(_qty.Value - 1); return true; }
            if (vk == 39) { _qty.Set(_qty.Value + 1); return true; }
            if (vk == 13) { if (_act.Visible && _act.Enabled) DoAction(); return true; }
            return false;
        }
    }
}
