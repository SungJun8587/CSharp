// AllSpawnLayout.cs : ZoneRule 의 AllMonsterCount / AllBossCount 로 "모든 몬스터/보스를 한 종류씩 칸에 나눠 세우는" 스폰 행을 만든다.
// 시험장(0 번 존)처럼 모든 종류를 한 번에 보려는 존용이다. 뷰어(GameData)가 같은 규칙으로 같은 칸을 계산한다. 바꾸면 양쪽을 함께 바꾼다.
using System.Collections.Generic;
using Game.ZoneCore;

namespace Game.ZoneServer;

public static class AllSpawnLayout
{
    public const float OriginX = 46f, OriginY = 40f, Step = 24f, Radius = 6f;
    public const int Columns = 7;

    /// <summary>보스(Boss 표에 있는 몬스터)는 첫 줄부터, 일반 몬스터는 그 다음 줄부터 ID 순으로 칸을 채운다. 반환: 더한 스폰 행 수.</summary>
    public static int Add(MonsterData monsters, BossData bosses, uint zoneId, uint monsterCount, uint bossCount)
    {
        List<uint> boss = new List<uint>(), regular = new List<uint>();
        foreach (MonsterTemplate m in monsters.Monsters)
            (bosses.FindByMonster(m.Id) != null ? boss : regular).Add(m.Id);
        boss.Sort(); regular.Sort();

        int added = 0;
        int bossRows = (boss.Count + Columns - 1) / Columns;
        if (bossCount > 0)
            for (int i = 0; i < boss.Count; ++i, ++added)
                monsters.AddSpawn(Cell(zoneId, boss[i], bossCount, i, 0));
        if (monsterCount > 0)
            for (int i = 0; i < regular.Count; ++i, ++added)
                monsters.AddSpawn(Cell(zoneId, regular[i], monsterCount, i, bossRows));
        return added;
    }

    private static SpawnEntry Cell(uint zoneId, uint monsterId, uint count, int index, int rowOffset)
    {
        return new SpawnEntry
        {
            ZoneId = zoneId, MonsterId = monsterId, Count = count,
            X = OriginX + Step * (index % Columns), Y = OriginY + Step * (index / Columns + rowOffset), Radius = Radius,
        };
    }
}
