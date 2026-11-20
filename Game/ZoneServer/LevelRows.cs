// LevelRows.cs : Level.json / MonsterExp.json 의 행 형식. 엑셀 변환기가 만드는 파일(Generated)이 아니라 손으로 쓴 파일이다.
// 시트 헤더: Level(Level:uint[1..], ExpToNext:uint[0..], StatPercent:uint[100..100000]), MonsterExp(MonsterId:uint[1..], Exp:uint[0..]).
using System.Collections.Generic;

namespace Game.ZoneServer;

public sealed class LevelRow
{
    public const string FileName = "Level.json";

    public uint Level { get; set; }
    public uint ExpToNext { get; set; }
    public uint StatPercent { get; set; }
}

public sealed class LevelTable
{
    public List<LevelRow> Level { get; set; } = new List<LevelRow>();
}

public sealed class MonsterExpRow
{
    public const string FileName = "MonsterExp.json";

    public uint MonsterId { get; set; }
    public uint Exp { get; set; }

    public void Validate(string where, List<string> errors)
    {
        if (MonsterId < 1u) errors.Add(where + ".MonsterId=" + MonsterId + " (허용 범위 1..)");
    }
}

public sealed class MonsterExpTable
{
    public List<MonsterExpRow> MonsterExp { get; set; } = new List<MonsterExpRow>();

    public void Validate(List<string> errors)
    {
        for (int i = 0; i < MonsterExp.Count; ++i)
            MonsterExp[i].Validate("MonsterExp[" + i + "]", errors);
    }
}
