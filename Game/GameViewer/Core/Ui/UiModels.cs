//***************************************************************************
// UiModels.cs : 화면(가방/상점/우편)이 쓰는 클라이언트 쪽 데이터 모델.
// 서버가 정답을 쥐는 구조다: 화면은 *Backend 인터페이스로 요청만 보내고, 결과(UiResult)와 갱신된 모델로 다시 그린다.
// 아래 Local* 백엔드는 서버 없이 화면을 확인하는 데모용(같은 모델을 직접 고친다). 실제 서버 연동은 같은 인터페이스를 패킷으로 구현한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameViewer.Core.Ui
{
    public sealed class UiResult
    {
        public bool Ok; public string Message;
        public static UiResult Success(string msg = "") { return new UiResult { Ok = true, Message = msg }; }
        public static UiResult Fail(string msg) { return new UiResult { Ok = false, Message = msg }; }
    }

    public enum ItemKind { Equip, Consumable, Material, Etc }

    public sealed class ItemInfo
    {
        public uint Id; public string Name; public ItemKind Kind; public int Grade;
        public uint MaxStack = 9999; public uint BuyPrice; public uint SellPrice; public string Desc = "";
        public bool Usable { get { return Kind == ItemKind.Consumable || Kind == ItemKind.Equip; } }
        public static string KindName(ItemKind k)
        {
            switch (k) { case ItemKind.Equip: return "장비"; case ItemKind.Consumable: return "소모품"; case ItemKind.Material: return "재료"; default: return "기타"; }
        }
    }

    public sealed class ItemCatalog
    {
        private readonly Dictionary<uint, ItemInfo> _items = new Dictionary<uint, ItemInfo>();
        public void Add(ItemInfo i) { _items[i.Id] = i; }
        public ItemInfo Get(uint id) { ItemInfo i; return _items.TryGetValue(id, out i) ? i : null; }
        public IEnumerable<ItemInfo> All { get { return _items.Values; } }

        /// <summary>서버 없이 화면을 확인하기 위한 가짜 아이템 목록.</summary>
        public static ItemCatalog Demo()
        {
            ItemCatalog c = new ItemCatalog();
            c.Add(new ItemInfo { Id = 1001, Name = "체력 물약", Kind = ItemKind.Consumable, Grade = 0, MaxStack = 99, BuyPrice = 50, SellPrice = 12, Desc = "마셔서 체력을 300 회복한다." });
            c.Add(new ItemInfo { Id = 1002, Name = "마나 물약", Kind = ItemKind.Consumable, Grade = 0, MaxStack = 99, BuyPrice = 60, SellPrice = 15, Desc = "마셔서 마나를 200 회복한다." });
            c.Add(new ItemInfo { Id = 1003, Name = "상급 체력 물약", Kind = ItemKind.Consumable, Grade = 1, MaxStack = 99, BuyPrice = 260, SellPrice = 65, Desc = "체력을 1200 회복한다. 사용 후 3초 동안 피해를 10% 덜 받는다." });
            c.Add(new ItemInfo { Id = 1004, Name = "귀환 주문서", Kind = ItemKind.Consumable, Grade = 1, MaxStack = 20, BuyPrice = 300, SellPrice = 75, Desc = "사용하면 5초 뒤 마을로 돌아간다. 이동하거나 공격받으면 취소된다." });
            c.Add(new ItemInfo { Id = 2001, Name = "수련자의 검", Kind = ItemKind.Equip, Grade = 0, MaxStack = 1, BuyPrice = 800, SellPrice = 200, Desc = "공격력 +12" });
            c.Add(new ItemInfo { Id = 2002, Name = "숲 수호자의 활", Kind = ItemKind.Equip, Grade = 2, MaxStack = 1, BuyPrice = 6400, SellPrice = 1600, Desc = "공격력 +48, 치명타 확률 +3%" });
            c.Add(new ItemInfo { Id = 2003, Name = "달빛 로브", Kind = ItemKind.Equip, Grade = 3, MaxStack = 1, BuyPrice = 24000, SellPrice = 6000, Desc = "마법 공격력 +85, 마나 +120" });
            c.Add(new ItemInfo { Id = 2004, Name = "불멸의 왕관", Kind = ItemKind.Equip, Grade = 4, MaxStack = 1, BuyPrice = 0, SellPrice = 50000, Desc = "모든 능력치 +25. 착용 중 사망 시 한 번 즉시 부활한다." });
            c.Add(new ItemInfo { Id = 3001, Name = "고블린의 이빨", Kind = ItemKind.Material, Grade = 0, MaxStack = 999, BuyPrice = 0, SellPrice = 4, Desc = "고블린에게서 얻는 재료." });
            c.Add(new ItemInfo { Id = 3002, Name = "철 광석", Kind = ItemKind.Material, Grade = 0, MaxStack = 999, BuyPrice = 30, SellPrice = 8, Desc = "무기 제작에 쓰는 광석." });
            c.Add(new ItemInfo { Id = 3003, Name = "푸른 정령석", Kind = ItemKind.Material, Grade = 2, MaxStack = 99, BuyPrice = 0, SellPrice = 400, Desc = "정령의 힘이 깃든 돌. 장비 강화에 쓴다." });
            c.Add(new ItemInfo { Id = 4001, Name = "오래된 열쇠", Kind = ItemKind.Etc, Grade = 1, MaxStack = 1, BuyPrice = 0, SellPrice = 0, Desc = "어딘가의 문을 여는 열쇠." });
            return c;
        }
    }

    /// <summary>재화 보유량. 순서는 처음 넣은 순서를 지킨다(골드, 블루 다이아).</summary>
    public sealed class UiWallet
    {
        private readonly List<KeyValuePair<uint, ulong>> _list = new List<KeyValuePair<uint, ulong>>();
        public IList<KeyValuePair<uint, ulong>> List { get { return _list.AsReadOnly(); } }
        public static string Name(uint id) { return id == 1 ? "골드" : (id == 2 ? "블루 다이아" : "재화 " + id.ToString(CultureInfo.InvariantCulture)); }

        public ulong Get(uint id) { for (int i = 0; i < _list.Count; ++i) if (_list[i].Key == id) return _list[i].Value; return 0; }
        public void Set(uint id, ulong v)
        {
            for (int i = 0; i < _list.Count; ++i) if (_list[i].Key == id) { _list[i] = new KeyValuePair<uint, ulong>(id, v); return; }
            _list.Add(new KeyValuePair<uint, ulong>(id, v));
        }
        public void Add(uint id, ulong v) { Set(id, Get(id) + v); }
        public bool Spend(uint id, ulong v) { ulong cur = Get(id); if (cur < v) return false; Set(id, cur - v); return true; }
    }

    /// <summary>가방 칸. 칸 위치가 있고(끌어서 옮김), 일부 칸은 잠겨 있을 수 있다(Unlocked 이후).</summary>
    public sealed class UiBag : IUiSlotSource
    {
        public readonly uint[] Id;
        public readonly uint[] Cnt;
        public int Unlocked;
        public readonly ItemCatalog Catalog;
        public ItemKind? Filter;                    // 탭 필터: 다른 종류는 어둡게 보인다
        public event Action Changed;

        public UiBag(ItemCatalog catalog, int capacity, int unlocked)
        {
            Catalog = catalog; Id = new uint[capacity]; Cnt = new uint[capacity]; Unlocked = Math.Min(unlocked, capacity);
        }

        public int SlotCount { get { return Id.Length; } }
        public int Used { get { int n = 0; for (int i = 0; i < Unlocked; ++i) if (Id[i] != 0) ++n; return n; } }
        public int Free { get { return Unlocked - Used; } }
        private void Notify() { if (Changed != null) Changed(); }

        public UiSlotView GetSlot(int i)
        {
            ItemInfo info = Id[i] != 0 ? Catalog.Get(Id[i]) : null;
            UiSlotView v = new UiSlotView { ItemId = Id[i], Count = Cnt[i], Grade = info != null ? info.Grade : 0, Locked = i >= Unlocked, Icon = UiIconKind.Item };
            v.Dimmed = Filter.HasValue && info != null && info.Kind != Filter.Value;
            return v;
        }

        public UiTooltip GetSlotTooltip(int i)
        {
            if (Id[i] == 0) return null;
            return ItemTip(Catalog.Get(Id[i]), Id[i], Cnt[i]);
        }

        public static UiTooltip ItemTip(ItemInfo info, uint id, uint count)
        {
            UiTooltip t = new UiTooltip();
            if (info == null) return t.Add("알 수 없는 아이템 (" + id + ")", UiTheme.Bad, 13);
            t.Add(info.Name, UiTheme.Grade(info.Grade), 14);
            t.Add(UiTheme.GradeName(info.Grade) + " · " + ItemInfo.KindName(info.Kind) + (count > 1 ? "   보유 " + count : ""), UiTheme.TextDim, 11);
            if (info.Desc.Length > 0) t.Add(info.Desc, UiTheme.Text, 12, true);
            if (info.SellPrice > 0) t.Add("판매가 " + info.SellPrice.ToString("N0", CultureInfo.InvariantCulture) + " 골드", UiTheme.Gold, 11);
            return t;
        }

        public uint CountOf(uint itemId) { uint n = 0; for (int i = 0; i < Unlocked; ++i) if (Id[i] == itemId) n += Cnt[i]; return n; }

        /// <summary>count 개를 넣을 공간이 있는가.</summary>
        public bool CanAdd(uint itemId, uint count)
        {
            ItemInfo info = Catalog.Get(itemId);
            uint max = info != null ? info.MaxStack : 9999;
            ulong room = 0;
            int free = 0;
            for (int i = 0; i < Unlocked; ++i)
            {
                if (Id[i] == itemId) room += max - Cnt[i];
                else if (Id[i] == 0) ++free;
            }
            room += (ulong)free * max;
            return room >= count;
        }

        /// <summary>전부 넣거나 아무것도 안 넣는다.</summary>
        public bool Add(uint itemId, uint count)
        {
            if (itemId == 0 || count == 0 || !CanAdd(itemId, count)) return false;
            ItemInfo info = Catalog.Get(itemId);
            uint max = info != null ? info.MaxStack : 9999;
            for (int i = 0; i < Unlocked && count > 0; ++i)
                if (Id[i] == itemId && Cnt[i] < max) { uint put = Math.Min(count, max - Cnt[i]); Cnt[i] += put; count -= put; }
            for (int i = 0; i < Unlocked && count > 0; ++i)
                if (Id[i] == 0) { uint put = Math.Min(count, max); Id[i] = itemId; Cnt[i] = put; count -= put; }
            Notify();
            return true;
        }

        public bool Remove(int slot, uint count)
        {
            if (slot < 0 || slot >= Unlocked || Id[slot] == 0 || count == 0 || Cnt[slot] < count) return false;
            Cnt[slot] -= count;
            if (Cnt[slot] == 0) Id[slot] = 0;
            Notify();
            return true;
        }

        /// <summary>from 을 to 로 옮긴다. 빈 칸이면 이동, 같은 아이템이면 합치고(넘치면 남김), 다르면 맞바꾼다.</summary>
        public bool Move(int from, int to)
        {
            if (from == to || from < 0 || to < 0 || from >= Unlocked || to >= Unlocked || Id[from] == 0) return false;
            if (Id[to] == 0) { Id[to] = Id[from]; Cnt[to] = Cnt[from]; Id[from] = 0; Cnt[from] = 0; }
            else if (Id[to] == Id[from])
            {
                ItemInfo info = Catalog.Get(Id[from]);
                uint max = info != null ? info.MaxStack : 9999;
                uint put = Math.Min(Cnt[from], max - Cnt[to]);
                if (put == 0) { Swap(from, to); }
                else { Cnt[to] += put; Cnt[from] -= put; if (Cnt[from] == 0) Id[from] = 0; }
            }
            else Swap(from, to);
            Notify();
            return true;
        }

        private void Swap(int a, int b) { uint i = Id[a], c = Cnt[a]; Id[a] = Id[b]; Cnt[a] = Cnt[b]; Id[b] = i; Cnt[b] = c; }

        /// <summary>종류 -> 등급 높은 순 -> 번호 순으로 모으고 같은 아이템은 합친다.</summary>
        public void Sort()
        {
            Dictionary<uint, ulong> total = new Dictionary<uint, ulong>();
            for (int i = 0; i < Unlocked; ++i) if (Id[i] != 0) { ulong v; total.TryGetValue(Id[i], out v); total[Id[i]] = v + Cnt[i]; }
            List<uint> ids = new List<uint>(total.Keys);
            ids.Sort(delegate (uint a, uint b)
            {
                ItemInfo x = Catalog.Get(a), y = Catalog.Get(b);
                int kx = x != null ? (int)x.Kind : 99, ky = y != null ? (int)y.Kind : 99;
                if (kx != ky) return kx.CompareTo(ky);
                int gx = x != null ? x.Grade : 0, gy = y != null ? y.Grade : 0;
                if (gx != gy) return gy.CompareTo(gx);
                return a.CompareTo(b);
            });
            for (int i = 0; i < Id.Length; ++i) { Id[i] = 0; Cnt[i] = 0; }
            int slot = 0;
            foreach (uint id in ids)
            {
                ItemInfo info = Catalog.Get(id);
                ulong left = total[id];
                uint max = info != null ? info.MaxStack : 9999;
                while (left > 0 && slot < Unlocked) { uint put = (uint)Math.Min(left, max); Id[slot] = id; Cnt[slot] = put; left -= put; ++slot; }
            }
            Notify();
        }
    }

    // ---------------------------------------------------------------- 백엔드 인터페이스(서버가 정답)

    public interface IInventoryBackend
    {
        void Move(int from, int to);
        void Use(int slot, Action<UiResult> done);
        void Discard(int slot, uint count, Action<UiResult> done);
        void Sort();
    }

    public sealed class UiShopEntry { public uint ItemId; public uint Price; public uint CurrencyId = 1; }

    public interface IShopBackend
    {
        void Buy(int entry, uint count, Action<UiResult> done);          // entry: 상점 목록 번호(같은 물건이 재화별로 따로 있을 수 있다)
        void Sell(int bagSlot, uint count, Action<UiResult> done);
    }

    public sealed class UiMailItem
    {
        public bool IsCurrency; public uint Id; public uint Count;      // IsCurrency 면 Id 는 재화 ID
    }

    public sealed class UiMail
    {
        public uint Id; public string Sender = ""; public string Title = ""; public string Body = ""; public string SentText = "";
        public int DaysLeft = 30; public bool Read, Claimed;
        public readonly List<UiMailItem> Items = new List<UiMailItem>();
        public bool HasItems { get { return Items.Count > 0; } }
        public bool Claimable { get { return HasItems && !Claimed; } }
    }

    public interface IMailBackend
    {
        void Read(uint mailId);
        void Claim(uint mailId, Action<UiResult> done);
        void ClaimAll(Action<UiResult> done);
        void DeleteRead(Action<UiResult> done);
    }

    public sealed class UiMailbox
    {
        public readonly List<UiMail> Mails = new List<UiMail>();
        public event Action Changed;
        public void Notify() { if (Changed != null) Changed(); }
        public int Unread { get { int n = 0; foreach (UiMail m in Mails) if (!m.Read) ++n; return n; } }
        public int Unclaimed { get { int n = 0; foreach (UiMail m in Mails) if (m.Claimable) ++n; return n; } }
        public UiMail Find(uint id) { foreach (UiMail m in Mails) if (m.Id == id) return m; return null; }
    }

    // ---------------------------------------------------------------- 데모용 로컬 백엔드

    public sealed class LocalInventoryBackend : IInventoryBackend
    {
        private readonly UiBag _bag;
        public LocalInventoryBackend(UiBag bag) { _bag = bag; }
        public void Move(int from, int to) { _bag.Move(from, to); }
        public void Sort() { _bag.Sort(); }
        public void Use(int slot, Action<UiResult> done)
        {
            ItemInfo info = _bag.Id[slot] != 0 ? _bag.Catalog.Get(_bag.Id[slot]) : null;
            if (info == null) { done(UiResult.Fail("사용할 수 없는 아이템입니다")); return; }
            if (info.Kind == ItemKind.Consumable) { _bag.Remove(slot, 1); done(UiResult.Success(info.Name + "을(를) 사용했습니다")); }
            else if (info.Kind == ItemKind.Equip) done(UiResult.Success(info.Name + "을(를) 장착했습니다 (데모: 장착 칸 없음)"));
            else done(UiResult.Fail("사용할 수 없는 아이템입니다"));
        }
        public void Discard(int slot, uint count, Action<UiResult> done)
        {
            ItemInfo info = _bag.Id[slot] != 0 ? _bag.Catalog.Get(_bag.Id[slot]) : null;
            string n = info != null ? info.Name : "아이템";
            if (_bag.Remove(slot, count)) done(UiResult.Success(n + " " + count + "개를 버렸습니다")); else done(UiResult.Fail("버릴 수 없습니다"));
        }
    }

    public sealed class LocalShopBackend : IShopBackend
    {
        private readonly UiBag _bag; private readonly UiWallet _wallet; private readonly List<UiShopEntry> _entries;
        public LocalShopBackend(UiBag bag, UiWallet wallet, List<UiShopEntry> entries) { _bag = bag; _wallet = wallet; _entries = entries; }
        public void Buy(int entry, uint count, Action<UiResult> done)
        {
            UiShopEntry e = entry >= 0 && entry < _entries.Count ? _entries[entry] : null;
            if (e == null || count == 0) { done(UiResult.Fail("판매하지 않는 물건입니다")); return; }
            ulong cost = (ulong)e.Price * count;
            uint itemId = e.ItemId;
            if (_wallet.Get(e.CurrencyId) < cost) { done(UiResult.Fail(UiWallet.Name(e.CurrencyId) + "이(가) 부족합니다")); return; }
            if (!_bag.CanAdd(itemId, count)) { done(UiResult.Fail("가방에 공간이 부족합니다")); return; }
            _wallet.Spend(e.CurrencyId, cost);
            _bag.Add(itemId, count);
            ItemInfo info = _bag.Catalog.Get(itemId);
            done(UiResult.Success((info != null ? info.Name : "아이템") + " " + count + "개를 구매했습니다"));
        }
        public void Sell(int slot, uint count, Action<UiResult> done)
        {
            if (slot < 0 || slot >= _bag.Unlocked || _bag.Id[slot] == 0 || count == 0 || _bag.Cnt[slot] < count) { done(UiResult.Fail("팔 수 없습니다")); return; }
            ItemInfo info = _bag.Catalog.Get(_bag.Id[slot]);
            if (info == null || info.SellPrice == 0) { done(UiResult.Fail("팔 수 없는 아이템입니다")); return; }
            _bag.Remove(slot, count);
            _wallet.Add(1, (ulong)info.SellPrice * count);
            done(UiResult.Success(info.Name + " " + count + "개를 판매했습니다 (+" + ((ulong)info.SellPrice * count).ToString("N0", CultureInfo.InvariantCulture) + " 골드)"));
        }
    }

    public sealed class LocalMailBackend : IMailBackend
    {
        private readonly UiMailbox _box; private readonly UiBag _bag; private readonly UiWallet _wallet;
        public LocalMailBackend(UiMailbox box, UiBag bag, UiWallet wallet) { _box = box; _bag = bag; _wallet = wallet; }
        public void Read(uint id) { UiMail m = _box.Find(id); if (m != null && !m.Read) { m.Read = true; _box.Notify(); } }

        private string TryClaim(UiMail m)
        {
            if (!m.Claimable) return null;
            // 가방 공간은 첨부 전체를 합쳐 본다(일부만 받는 일이 없게 전부 되거나 전부 안 된다).
            UiBag probe = new UiBag(_bag.Catalog, _bag.SlotCount, _bag.Unlocked);
            Array.Copy(_bag.Id, probe.Id, _bag.Id.Length); Array.Copy(_bag.Cnt, probe.Cnt, _bag.Cnt.Length);
            foreach (UiMailItem it in m.Items)
                if (!it.IsCurrency && !probe.Add(it.Id, it.Count)) return "가방에 공간이 부족합니다";
            foreach (UiMailItem it in m.Items)
            {
                if (it.IsCurrency) _wallet.Add(it.Id, it.Count); else _bag.Add(it.Id, it.Count);
            }
            m.Claimed = true; m.Read = true;
            return "";
        }

        public void Claim(uint id, Action<UiResult> done)
        {
            UiMail m = _box.Find(id);
            string r = m == null ? "우편을 찾을 수 없습니다" : TryClaim(m);
            _box.Notify();
            if (r == null) done(UiResult.Fail("받을 첨부가 없습니다")); else if (r.Length == 0) done(UiResult.Success("첨부를 받았습니다")); else done(UiResult.Fail(r));
        }

        public void ClaimAll(Action<UiResult> done)
        {
            int ok = 0; string fail = null;
            foreach (UiMail m in _box.Mails)
            {
                string r = TryClaim(m);
                if (r == null) continue;
                if (r.Length == 0) ++ok; else { fail = r; break; }
            }
            _box.Notify();
            if (ok == 0 && fail != null) done(UiResult.Fail(fail));
            else if (ok == 0) done(UiResult.Fail("받을 첨부가 없습니다"));
            else done(UiResult.Success(ok + "통의 첨부를 받았습니다" + (fail != null ? " (" + fail + ")" : "")));
        }

        public void DeleteRead(Action<UiResult> done)
        {
            int n = _box.Mails.RemoveAll(m => m.Read && !m.Claimable);
            _box.Notify();
            done(n > 0 ? UiResult.Success("읽은 우편 " + n + "통을 삭제했습니다") : UiResult.Fail("삭제할 우편이 없습니다"));
        }
    }
}
