//***************************************************************************
// UiDemoData.cs : 서버 연동 전에 화면을 보기 위한 데모 데이터(가방/지갑/상점/우편). 실제 연동 때는 서버가 주는 값으로 같은 모델을 채운다.
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.Ui
{
    public sealed class UiDemoData
    {
        public readonly ItemCatalog Catalog = ItemCatalog.Demo();
        public readonly UiBag Bag;
        public readonly UiWallet Wallet = new UiWallet();
        public readonly UiMailbox Mailbox = new UiMailbox();
        public readonly List<UiShopEntry> Goods = new List<UiShopEntry>();
        public readonly IInventoryBackend InventoryBackend;
        public readonly IShopBackend ShopBackend;
        public readonly IMailBackend MailBackend;

        public UiDemoData()
        {
            Bag = new UiBag(Catalog, 30, 24);
            Bag.Add(1001, 12); Bag.Add(1002, 5); Bag.Add(1003, 3); Bag.Add(1004, 2);
            Bag.Add(2001, 1); Bag.Add(2002, 1); Bag.Add(2003, 1);
            Bag.Add(3001, 130); Bag.Add(3002, 40); Bag.Add(3003, 4);
            Wallet.Set(1, 123456); Wallet.Set(2, 320);
            Goods.AddRange(new UiShopEntry[]
            {
                new UiShopEntry { ItemId = 1001, Price = 50 }, new UiShopEntry { ItemId = 1002, Price = 60 }, new UiShopEntry { ItemId = 1003, Price = 260 },
                new UiShopEntry { ItemId = 1004, Price = 300 }, new UiShopEntry { ItemId = 2001, Price = 800 }, new UiShopEntry { ItemId = 2002, Price = 6400 },
                new UiShopEntry { ItemId = 2003, Price = 24000 }, new UiShopEntry { ItemId = 3002, Price = 30 },
                new UiShopEntry { ItemId = 1003, Price = 30, CurrencyId = 2 },
            });
            UiMail a = new UiMail { Id = 1, Sender = "운영팀", Title = "오픈 기념 선물", Body = "서버 오픈을 축하드립니다.\n작은 선물을 보내 드리니 즐거운 시간 보내세요!", SentText = "2026-11-20", DaysLeft = 29 };
            a.Items.Add(new UiMailItem { Id = 1003, Count = 5 }); a.Items.Add(new UiMailItem { IsCurrency = true, Id = 1, Count = 5000 }); a.Items.Add(new UiMailItem { IsCurrency = true, Id = 2, Count = 30 });
            UiMail b = new UiMail { Id = 2, Sender = "경매장", Title = "판매 대금 정산", Body = "아이템이 판매되었습니다.", SentText = "2026-11-19", DaysLeft = 2 };
            b.Items.Add(new UiMailItem { IsCurrency = true, Id = 1, Count = 5120 });
            UiMail c = new UiMail { Id = 3, Sender = "길드장 아르벤", Title = "내일 레이드 공지", Body = "내일 저녁 9시에 보스 레이드가 있습니다.", SentText = "2026-11-18", DaysLeft = 12, Read = true };
            Mailbox.Mails.AddRange(new UiMail[] { a, b, c });
            InventoryBackend = new LocalInventoryBackend(Bag);
            ShopBackend = new LocalShopBackend(Bag, Wallet, Goods);
            MailBackend = new LocalMailBackend(Mailbox, Bag, Wallet);
        }

        /// <summary>창 공장을 관리자에 등록한다(열 때마다 새 창을 만들고 모델은 공유한다).</summary>
        public void Register(UiManager m, Logic.ViewSettings settings, System.Action settingsChanged)
        {
            m.Register(InventoryWindow.WindowId, () => new InventoryWindow(Bag, Wallet, InventoryBackend));
            m.Register(ShopWindow.WindowId, () => new ShopWindow("잡화 상점", Goods, Bag, Wallet, ShopBackend));
            m.Register(MailWindow.WindowId, () => new MailWindow(Mailbox, Catalog, MailBackend));
            m.Register(SettingsWindow.WindowId, () => new SettingsWindow(settings, settingsChanged));
        }
    }
}
