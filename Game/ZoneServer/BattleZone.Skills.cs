// BattleZone.Skills.cs : 스킬 개방/구매/장착. 보유 스킬과 장착 칸은 서버가 정하며, 클라이언트 요청은 모두 검증한다.
// 규칙: HeroSkill 표의 UnlockLevel 에 이른 무료(Price 0) 스킬은 고정 칸(1~6)으로 자동 지급, 상점 스킬은 UnlockLevel 이상에서 구매해 레벨로 열린 칸 7·8 에 장착한다.
// 장착 중(칸 1~8)인 스킬만 시전할 수 있다. 지급/구매/장착이 바뀔 때마다 상태를 Gateway 로 보내 저장한다.
using System;
using System.Collections.Generic;
using Game.ZoneCommon;
using Game.ZoneCore;

namespace Game.ZoneServer;

public sealed partial class BattleZone
{
    private const int EquipSlots = PlayerState.MaxEquipped;

    /// <summary>입장 때 저장된 스킬을 복원한다. 표에 없는 스킬은 버린다. 무료 스킬은 표의 고정 칸으로 되돌리고, 상점 스킬의 칸은 열려 있는 7·8 중 겹치지 않는 곳만 남긴다.</summary>
    private void RestoreSkills(ZonePlayer player, HeroTemplate hero, PlayerState? restore)
    {
        if (restore != null)
        {
            foreach (KeyValuePair<uint, byte> s in restore.Skills)
                if (_heroes.FindSkill(s.Key) != null && s.Value <= EquipSlots)
                    player.Skills[s.Key] = s.Value;
        }

        bool[] used = new bool[EquipSlots + 1];
        foreach (uint id in hero.Skills)
        {
            if (!player.Skills.ContainsKey(id) || !hero.Unlocks.TryGetValue(id, out SkillUnlock? u) || u.IsFree)
                continue;
            byte slot = player.Skills[id];
            bool ok = slot > SkillRules.FixedSlots && slot <= EquipSlots && player.Level >= SkillRules.SlotUnlockLevel(slot) && !used[slot];
            player.Skills[id] = ok ? slot : (byte)0;
            if (ok)
                used[slot] = true;
        }
        foreach (uint id in hero.Skills)
            if (player.Skills.ContainsKey(id) && hero.Unlocks.TryGetValue(id, out SkillUnlock? f) && f.IsFree)
                player.Skills.Remove(id);       // 무료 스킬은 레벨로만 지급한다(표가 바뀌어 레벨이 안 되는 스킬은 회수, 되는 스킬은 아래에서 다시 지급)
        GrantFreeSkills(player, hero);
    }

    /// <summary>레벨이 된 무료 스킬을 지급하고 고정 칸에 장착한다. 바뀐 게 있으면 true.</summary>
    private bool GrantFreeSkills(ZonePlayer player, HeroTemplate hero)
    {
        bool changed = false;
        foreach (uint id in hero.Skills)
        {
            if (player.Skills.ContainsKey(id) || !hero.Unlocks.TryGetValue(id, out SkillUnlock? u) || !u.IsFree || player.Level < u.UnlockLevel)
                continue;
            player.Skills[id] = u.Slot != 0 ? (byte)u.Slot : FirstFreeSlot(player, hero, 1, SkillRules.FixedSlots);
            changed = true;
        }
        return changed;
    }

    /// <summary>이 영웅의 비어 있는 장착 칸 중 [first, last] 안에서 가장 앞 번호. 없으면 0(장착 안 함).</summary>
    private static byte FirstFreeSlot(ZonePlayer player, HeroTemplate hero, int first, int last)
    {
        bool[] used = new bool[EquipSlots + 1];
        foreach (uint id in hero.Skills)
            if (player.Skills.TryGetValue(id, out byte s))
                used[s] = true;
        for (int i = first; i <= last; ++i)
            if (!used[i])
                return (byte)i;
        return 0;
    }

    /// <summary>이 스킬을 지금 쓸 수 있는지(보유 + 장착 중).</summary>
    private static bool IsEquipped(ZonePlayer player, uint skillId) => player.Skills.TryGetValue(skillId, out byte slot) && slot != 0;

    /// <summary>내 영웅 스킬의 보유 현황을 본인에게 보낸다.</summary>
    private void SendSkillBook(ZonePlayer player)
    {
        HeroTemplate? hero = _heroes.Resolve(player.HeroId);
        if (hero == null)
            return;
        ushort[] ids = new ushort[ZcSkillBookNotify.MaxEntries];
        byte[] slots = new byte[ZcSkillBookNotify.MaxEntries];
        int n = 0;
        foreach (uint id in hero.Skills)
        {
            if (n >= ids.Length || !player.Skills.TryGetValue(id, out byte slot))
                continue;
            ids[n] = (ushort)id;
            slots[n] = slot;
            ++n;
        }
        SendPacket(player, new ZcSkillBookNotify
        {
            Count = n, SkillIds = ids, Slots = slots,
            ShopSlot7Level = (byte)Math.Min(255u, SkillRules.SlotUnlockLevel(7)), ShopSlot8Level = (byte)Math.Min(255u, SkillRules.SlotUnlockLevel(8)),
        });
    }

    private void SendSkillOpResult(ZonePlayer player, SkillOp op, SkillOpResult result, uint skillId)
        => SendPacket(player, new ZcSkillOpResNotify { Op = (byte)op, Result = (byte)result, SkillId = skillId });

    /// <summary>변경된 상태를 바로 저장하도록 Gateway 로 보낸다(구매는 접속이 끊겨도 남아야 한다). 도착 지점은 담지 않는다.</summary>
    private void SaveNow(ZonePlayer player)
    {
        PlayerState st = SnapshotOf(player);
        st.ArrivalZone = 0;
        SendPacketToGateway(new ZoneStateSave { State = st });
    }

    private void HandleSkillBuy(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (!ZcSkillBuyReq.TryRead(packet, out ZcSkillBuyReq req))
        {
            Reject(player, Limits.MalformedWeight);
            return;
        }

        HeroTemplate? hero = _heroes.Resolve(player.HeroId);
        SkillOpResult r = BuySkill(player, hero, req.SkillId);
        SendSkillOpResult(player, SkillOp.Buy, r, req.SkillId);
        if (r != SkillOpResult.Ok)
            return;

        SendSkillBook(player);
        SendCurrency(player, hero!.Unlocks[req.SkillId].PriceCurrencyId, 0);
        SaveNow(player);
    }

    private SkillOpResult BuySkill(ZonePlayer player, HeroTemplate? hero, uint skillId)
    {
        if (hero == null || !hero.Unlocks.TryGetValue(skillId, out SkillUnlock? u))
            return SkillOpResult.Unknown;
        if (!player.IsAlive)
            return SkillOpResult.Dead;
        if (player.Skills.ContainsKey(skillId))
            return SkillOpResult.AlreadyOwned;
        if (u.IsFree)
            return SkillOpResult.NotForSale;
        if (player.Level < u.UnlockLevel)
            return SkillOpResult.LevelTooLow;
        // 존 서버는 Currency 표에 있는(몬스터 보상으로 얻는) 재화만 쓴다. 결제로 얻는 유료 재화는 Gateway/DB 거래로만 바꾼다.
        if (_currencies.MaxOf(u.PriceCurrencyId) == 0 || _currencies.IsPaid(u.PriceCurrencyId))
            return SkillOpResult.Unknown;
        player.Wallet.TryGetValue(u.PriceCurrencyId, out uint have);
        if (have < u.Price)
            return SkillOpResult.NotEnough;

        player.Wallet[u.PriceCurrencyId] = have - u.Price;
        player.Skills[skillId] = FirstOpenShopSlot(player, hero);        // 열려 있는 빈 상점 칸이 있으면 바로 끼운다
        return SkillOpResult.Ok;
    }

    private void HandleSkillEquip(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (!ZcSkillEquipReq.TryRead(packet, out ZcSkillEquipReq req))
        {
            Reject(player, Limits.MalformedWeight);
            return;
        }

        HeroTemplate? hero = _heroes.Resolve(player.HeroId);
        SkillOpResult r = EquipSkill(player, hero, req.SkillId, req.Slot);
        SendSkillOpResult(player, SkillOp.Equip, r, req.SkillId);
        if (r != SkillOpResult.Ok)
            return;

        SendSkillBook(player);
        SaveNow(player);
    }

    private static byte FirstOpenShopSlot(ZonePlayer player, HeroTemplate hero)
    {
        for (int slot = SkillRules.FixedSlots + 1; slot <= EquipSlots; ++slot)
            if (player.Level >= SkillRules.SlotUnlockLevel(slot) && FirstFreeSlot(player, hero, slot, slot) != 0)
                return (byte)slot;
        return 0;
    }

    /// <summary>상점 스킬을 칸 7·8 에 장착하거나(slot 0 이면 해제) 두 칸의 스킬을 서로 바꾼다. 무료 스킬은 고정 칸이라 바꿀 수 없다.</summary>
    private SkillOpResult EquipSkill(ZonePlayer player, HeroTemplate? hero, uint skillId, byte slot)
    {
        if (hero == null || !hero.Unlocks.TryGetValue(skillId, out SkillUnlock? u))
            return SkillOpResult.Unknown;
        if (!player.Skills.TryGetValue(skillId, out byte old))
            return SkillOpResult.NotOwned;
        if (u.IsFree)
            return SkillOpResult.FixedSlot;
        if (slot != 0 && (slot <= SkillRules.FixedSlots || slot > EquipSlots))
            return SkillOpResult.BadSlot;
        if (slot != 0 && player.Level < SkillRules.SlotUnlockLevel(slot))
            return SkillOpResult.SlotLocked;
        if (!player.IsAlive)
            return SkillOpResult.Dead;
        if (slot == old)
            return SkillOpResult.Ok;

        if (slot != 0)
        {
            foreach (uint other in hero.Skills)
            {
                if (other != skillId && player.Skills.TryGetValue(other, out byte s) && s == slot)
                    player.Skills[other] = old;     // 자리를 바꾼다(이 스킬이 장착 전이었다면 old 가 0이라 밀려난 스킬은 해제된다)
            }
        }
        player.Skills[skillId] = slot;
        return SkillOpResult.Ok;
    }
}
