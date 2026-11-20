// ZoneRuleTable.cs : 존별 규칙(GameData/ZoneRule.json: World.xlsx의 ZoneRule 시트) 읽기.
using System.Collections.Generic;
using System.IO;
using Core.Util;

namespace Game.ZoneServer;

/// <summary>존 하나의 규칙. 파일에 행이 없는 존은 서버 옵션의 기본값을 쓴다.</summary>
public sealed class ZoneRuleEntry
{
    /// <summary>플레이어끼리 아무나 공격할 수 있는지(자유 PvP). 꺼져 있어도 결투 상대끼리는 싸울 수 있다.</summary>
    public bool AllowPvp;

    /// <summary>결투 신청을 받을 수 있는지.</summary>
    public bool AllowDuel;

    /// <summary>이 존이 쓸 지형(장애물)의 존 번호. 0 이면 자기 번호. 시험장(0 번 존)이 1~10 번 존의 지형 중 하나를 빌려 쓸 때 쓴다.</summary>
    public uint Terrain;

    /// <summary>0 이 아니면 Monster 표의 일반 몬스터를 모두 이 마릿수씩 세운다(Spawn 표에 행을 적지 않아도 된다). 칸 배치는 AllSpawnLayout.</summary>
    public uint AllMonsterCount;

    /// <summary>0 이 아니면 Boss 표의 보스를 모두 이 마릿수씩 세운다.</summary>
    public uint AllBossCount;
}

public static class ZoneRuleTable
{
    // 파일 형식. 다른 게임 데이터와 같은 엄격한 읽기(JsonTableLoader)를 쓴다.
    private sealed class RuleFile
    {
        public List<RuleRow> ZoneRule { get; set; } = new List<RuleRow>();
    }

    private sealed class RuleRow
    {
        public uint ZoneId { get; set; }
        public bool AllowPvp { get; set; }
        public bool AllowDuel { get; set; }
        public uint Terrain { get; set; }
        public uint AllMonsterCount { get; set; }
        public uint AllBossCount { get; set; }
    }

    /// <summary>
    /// 이 존의 규칙을 읽는다. 행은 {"ZoneId","AllowPvp","AllowDuel","Terrain","AllMonsterCount","AllBossCount"}이며 ZoneId가 zoneId인 것만 쓴다.
    /// 파일이 없거나 이 존의 행이 없으면 성공이고 rule은 null(기본값 사용)이다. 형식이 틀리거나 같은 존의 행이 둘 이상이면 false.
    /// </summary>
    public static bool TryLoad(string dir, uint zoneId, out ZoneRuleEntry? rule)
    {
        rule = null;
        string path = Path.Combine(dir, "ZoneRule.json");
        if (!File.Exists(path))
            return true;

        if (!JsonTableLoader.TryLoadFile(path, out RuleFile? file, out string error))
            return Fail(path, error);

        foreach (RuleRow r in file!.ZoneRule)
        {
            if (r.ZoneId != zoneId)
                continue;
            if (rule != null)
                return Fail(path, "ZoneId " + zoneId + " 행이 둘 이상이다");
            rule = new ZoneRuleEntry { AllowPvp = r.AllowPvp, AllowDuel = r.AllowDuel, Terrain = r.Terrain, AllMonsterCount = r.AllMonsterCount, AllBossCount = r.AllBossCount };
        }
        return true;
    }

    private static bool Fail(string path, string why)
    {
        LogManager.Error("[ZoneRuleTable] {0}: {1}", path, why);
        return false;
    }
}
