//***************************************************************************
// SkillBookHud.cs : 스킬 창(K). 내 영웅의 스킬 목록과 장착 칸 8개(1~6 레벨로 지급되는 고정 칸, 7~8 상점 스킬 칸), 구매/장착 단추.
// 보유/장착/결제는 모두 서버가 정한다. 이 창은 요청만 보내고 서버가 알려 준 현황(WorldModel.SkillSlots)을 그린다.
// 창의 배치는 static 함수로 열어 두어 GameCanvas 가 같은 식으로 클릭을 판정한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;

namespace GameViewer.App.Render
{
    public enum SkillBookAction { None, Buy, Equip, Unequip }

    public sealed class SkillBookHud
    {
        private const float PanelW = 660f, SlotSize = 52f, SlotGap = 8f, RowH = 50f, BtnW = 150f;
        private readonly Resources _res;
        private readonly StringFormat _right = new StringFormat { Alignment = StringAlignment.Far };
        private readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly StringFormat _clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        public SkillBookHud(Resources res) { _res = res; }

        // ------------------------------------------------------------------ 배치

        public static RectangleF Window(ViewState v, int skillCount)
        {
            float h = 56f + SlotSize + 44f + skillCount * RowH + 40f;
            return new RectangleF((v.Width - PanelW) / 2f, Math.Max(10f, (v.Height - h) / 2f), PanelW, h);
        }

        /// <summary>장착 칸 i(0~5)의 영역.</summary>
        public static RectangleF SlotRect(ViewState v, int skillCount, int i)
        {
            RectangleF w = Window(v, skillCount);
            float total = ViewerSession.MaxEquipped * SlotSize + (ViewerSession.MaxEquipped - 1) * SlotGap;
            return new RectangleF(w.X + (w.Width - total) / 2f + i * (SlotSize + SlotGap), w.Y + 56f, SlotSize, SlotSize);
        }

        public static RectangleF RowRect(ViewState v, int skillCount, int i)
        {
            RectangleF w = Window(v, skillCount);
            return new RectangleF(w.X + 14f, w.Y + 56f + SlotSize + 34f + i * RowH, w.Width - 28f, RowH - 6f);
        }

        public static RectangleF ButtonRect(ViewState v, int skillCount, int i)
        {
            RectangleF r = RowRect(v, skillCount, i);
            return new RectangleF(r.Right - BtnW - 8f, r.Y + 7f, BtnW, r.Height - 14f);
        }

        // ------------------------------------------------------------------ 판정 (그리기와 클릭이 같은 규칙을 쓴다)

        /// <summary>이 스킬 줄의 단추가 지금 하는 일. 살 수 없는 상태(레벨 부족 등)면 None 이다.</summary>
        public static SkillBookAction ActionFor(ViewerSession s, uint skillId)
        {
            SkillUnlockRow u = s.Data.UnlockOf(s.HeroId, skillId);
            if (s.Owns(skillId)) return u.IsFree ? SkillBookAction.None : (s.SlotOf(skillId) != 0 ? SkillBookAction.Unequip : SkillBookAction.Equip);
            if (u.IsFree || s.World.MyLevel < u.UnlockLevel) return SkillBookAction.None;
            return SkillBookAction.Buy;
        }

        /// <summary>단추를 눌렀을 때 장착할 상점 칸(7·8). 고른 칸이 열려 있으면 그 칸, 아니면 열려 있는 첫 빈 칸, 없으면 0.</summary>
        public static byte TargetSlot(ViewerSession s, ViewState v)
        {
            if (v.SkillBookTarget > ViewerSession.FixedSlots && v.SkillBookTarget <= ViewerSession.MaxEquipped && s.SlotLockLevel(v.SkillBookTarget) == 0) return (byte)v.SkillBookTarget;
            for (byte i = ViewerSession.FixedSlots + 1; i <= ViewerSession.MaxEquipped; ++i)
            {
                if (s.SlotLockLevel(i) != 0) continue;
                bool used = false;
                foreach (uint id in s.Data.SkillsOf(s.HeroId)) if (s.SlotOf(id) == i) used = true;
                if (!used) return i;
            }
            return 0;
        }

        public static string PriceText(ViewerSession s, SkillUnlockRow u)
        {
            return u.Price.ToString("N0") + (u.PriceCurrencyId == ZcCurrencyNotify.Gold ? " G" : " #" + u.PriceCurrencyId);
        }

        // ------------------------------------------------------------------ 그리기

        public void Draw(Graphics g, ViewerSession s, ViewState v, long now)
        {
            List<uint> all = s.Data.SkillsOf(s.HeroId);
            int n = all.Count;
            g.FillRectangle(_res.Brush(150, 0, 0, 0), 0, 0, v.Width, v.Height);
            RectangleF w = Window(v, n);
            g.FillRectangle(_res.Brush(240, 16, 20, 30), w.X, w.Y, w.Width, w.Height);
            g.DrawRectangle(_res.Pen(210, 206, 176, 108, 1.5f), w.X, w.Y, w.Width, w.Height);
            g.DrawString("스킬  -  " + s.Data.HeroName(s.HeroId), _res.Big, _res.Brush(Color.FromArgb(255, 235, 150)), w.X + 16, w.Y + 10);
            string gold = "골드 " + s.World.Gold.ToString("N0");
            g.DrawString(gold, _res.Bold, _res.Brush(Color.FromArgb(255, 226, 140)), w.Right - 16, w.Y + 16, _right);

            if (!s.World.SkillBookKnown)
            {
                g.DrawString("서버에 접속한 상태에서만 스킬을 사고 장착할 수 있습니다.", _res.Normal, _res.Brush(Color.FromArgb(200, 210, 200)), w.X + 16, w.Y + 70);
                return;
            }

            // 장착 칸 8개: 1~6 은 레벨이 되면 자동으로 채워지는 고정 칸, 7~8 은 상점에서 산 스킬을 끼우는 칸
            for (int i = 0; i < ViewerSession.MaxEquipped; ++i)
            {
                RectangleF r = SlotRect(v, n, i);
                uint id = 0;
                foreach (uint k in all) if (s.SlotOf(k) == i + 1) id = k;
                uint lockLv = s.SlotLockLevel(i + 1);
                bool sel = v.SkillBookTarget == i + 1 && i >= ViewerSession.FixedSlots;
                g.FillRectangle(_res.Brush(200, 30, 36, 52), r.X, r.Y, r.Width, r.Height);
                if (id != 0) DrawIcon(g, s, id, r.X + 4, r.Y + 4, r.Width - 8);
                if (lockLv > 0)
                {
                    uint des = s.DesignatedSkill(i + 1);
                    if (des != 0) DrawIcon(g, s, des, r.X + 4, r.Y + 4, r.Width - 8);
                    g.FillRectangle(_res.Brush(170, 8, 10, 16), r.X, r.Y, r.Width, r.Height);
                    g.DrawString("Lv " + lockLv, _res.Small, _res.Brush(Color.FromArgb(255, 226, 140)), r, _center);
                }
                else if (id == 0 && i >= ViewerSession.FixedSlots) g.DrawString("+", _res.Big, _res.Brush(Color.FromArgb(220, 255, 232, 160)), r, _center);
                g.DrawRectangle(_res.Pen(sel ? 255 : 150, sel ? 255 : 150, sel ? 220 : 160, sel ? 120 : 170, sel ? 2.5f : 1f), r.X, r.Y, r.Width, r.Height);
                g.DrawString((i + 1).ToString(), _res.Small, _res.Brush(Color.FromArgb(230, 230, 230, 200)), r.X + 3, r.Y + 1);
            }
            g.DrawString("칸 1~6: 레벨이 되면 스킬을 자동으로 얻습니다(고정).  칸 7~8: 상점에서 산 스킬을 끼우는 칸입니다. 7·8 칸을 눌러 두면 그 칸에 장착합니다(차 있으면 자리 교환).", _res.Small, _res.Brush(Color.FromArgb(170, 190, 180)), new RectangleF(w.X + 16, w.Y + 56f + SlotSize + 6f, w.Width - 32, 28f));

            for (int i = 0; i < n; ++i)
            {
                uint id = all[i];
                SkillRow sk = s.Data.Skill(id);
                SkillUnlockRow u = s.Data.UnlockOf(s.HeroId, id);
                RectangleF r = RowRect(v, n, i);
                bool owned = s.Owns(id);
                byte slot = s.SlotOf(id);
                g.FillRectangle(_res.Brush(owned ? 120 : 70, 40, 48, 70), r.X, r.Y, r.Width, r.Height);
                g.DrawRectangle(_res.Pen(owned ? 160 : 80, 180, 190, 210, 1f), r.X, r.Y, r.Width, r.Height);
                DrawIcon(g, s, id, r.X + 6, r.Y + 5, r.Height - 10);
                Brush nameBrush = _res.Brush(owned ? Color.FromArgb(255, 245, 215) : Color.FromArgb(170, 175, 185));
                g.DrawString(s.Data.SkillName(id), _res.Bold, nameBrush, r.X + r.Height + 6, r.Y + 4);
                string info = sk == null ? "" : (sk.MpCost > 0 ? "MP " + sk.MpCost + "   " : "") + "재사용 " + (sk.CooldownMs / 1000.0).ToString("0.#") + "초   사거리 " + sk.Range.ToString("0.#");
                g.DrawString(info, _res.Small, _res.Brush(Color.FromArgb(175, 190, 185)), new RectangleF(r.X + r.Height + 6, r.Y + 24, r.Width - r.Height - BtnW - 30, 18), _clip);

                RectangleF b = ButtonRect(v, n, i);
                SkillBookAction act = ActionFor(s, id);
                string label; Color fill, edge = Color.FromArgb(200, 210, 210, 210);
                if (act == SkillBookAction.Unequip) { label = "장착 중 [" + slot + "]  해제"; fill = Color.FromArgb(200, 50, 90, 60); }
                else if (act == SkillBookAction.Equip) { label = "장착"; fill = Color.FromArgb(200, 50, 70, 110); }
                else if (act == SkillBookAction.Buy)
                {
                    bool can = Wallet(s, u) >= u.Price;
                    label = "구매  " + PriceText(s, u);
                    fill = can ? Color.FromArgb(220, 90, 70, 20) : Color.FromArgb(200, 70, 40, 40);
                    if (can) edge = Color.FromArgb(255, 236, 190, 90);
                }
                else if (owned && u.IsFree) { label = "칸 " + slot + " 고정"; fill = Color.FromArgb(150, 40, 60, 50); edge = Color.FromArgb(120, 150, 190, 160); }
                else { label = u.IsFree ? "Lv " + u.UnlockLevel + " 에 개방" : "Lv " + u.UnlockLevel + " 부터 구매"; fill = Color.FromArgb(120, 40, 40, 48); edge = Color.FromArgb(90, 150, 150, 150); }
                g.FillRectangle(_res.Brush(fill), b.X, b.Y, b.Width, b.Height);
                g.DrawRectangle(_res.Pen(edge, 1f), b.X, b.Y, b.Width, b.Height);
                g.DrawString(label, _res.Small, _res.Brush(act == SkillBookAction.None ? Color.FromArgb(170, 175, 180) : Color.White), b, _center);
            }

            string msg = "";
            if (s.World.LastSkillResult != SkillOpResult.Ok && now - s.World.LastSkillOpMs < 4000) msg = SkillOpResult.Text(s.World.LastSkillResult);
            if (msg.Length > 0) g.DrawString(msg, _res.Bold, _res.Brush(Color.FromArgb(255, 150, 130)), w.X + 16, w.Bottom - 30);
            g.DrawString(KeyBindings.KeyName(v.Settings.Keys.Get(BindAction.SkillBook)) + " / Esc: 닫기", _res.Small, _res.Brush(Color.FromArgb(170, 190, 180)), w.Right - 16, w.Bottom - 26, _right);
        }

        private static uint Wallet(ViewerSession s, SkillUnlockRow u) { uint a; s.World.Wallet.TryGetValue(u.PriceCurrencyId, out a); return a; }

        private void DrawIcon(Graphics g, ViewerSession s, uint id, float x, float y, float size)
        {
            Bitmap b = SkillIcons.Get(id);
            if (b != null) g.DrawImage(b, x, y, size, size);
            else Ui.SkillIcon(g, _res, x, y, size, s.Data.Skill(id), SkillFxTable.For(s.Data, id), false);
        }
    }
}
