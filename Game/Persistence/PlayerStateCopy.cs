// PlayerStateCopy.cs : PlayerState 복사. 저장소가 호출한 쪽의 객체를 그대로 들고 있지 않게 한다.
using System.Collections.Generic;
using Game.ZoneCommon;

namespace Game.Persistence;

public static class PlayerStateCopy
{
    public static PlayerState Clone(PlayerState s)
    {
        PlayerState c = new PlayerState
        {
            CharId = s.CharId, HeroId = s.HeroId, HasVitals = s.HasVitals, Hp = s.Hp, Mp = s.Mp,
            ArrivalZone = s.ArrivalZone, ArrivalX = s.ArrivalX, ArrivalY = s.ArrivalY, Level = s.Level, Exp = s.Exp,
        };
        foreach (KeyValuePair<uint, uint> kv in s.Wallet)
            c.Wallet.Add(kv);
        foreach (KeyValuePair<uint, uint> kv in s.Items)
            c.Items.Add(kv);
        foreach (KeyValuePair<uint, byte> kv in s.Skills)
            c.Skills.Add(kv);
        return c;
    }
}
