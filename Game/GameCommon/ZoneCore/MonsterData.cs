// MonsterData.cs : 몬스터 템플릿/스폰 테이블 데이터 (서버 구동 시 로드되는 불변 데이터)
using System;
using System.Collections.Generic;

namespace Game.ZoneCore;

/// <summary>
/// 몬스터 종류별 데이터. 기획 데이터(엑셀)의 Monster 시트 한 행에 대응한다.
/// <list type="bullet">
/// <item>AggroRange : 이 거리 안에 들어온 플레이어를 먼저 공격 대상으로 삼는다. 0이면 선공하지 않고 맞았을 때만 반응한다. AOI 최소 보장 시야 이하여야 한다(시야 밖 플레이어는 AI가 깨어나지 않으므로).</item>
/// <item>LeashRange : 스폰 지점에서 이 거리 이상 멀어지면 추적을 포기하고 돌아가 체력을 회복한다.</item>
/// <item>RespawnMs : 사망 후 부활까지의 시간</item>
/// <item>AssistRange : 같은 존의 대기 중인 몬스터가 이 거리 안에서 선공을 받거나 시야에 플레이어를 발견하면, 근처의 대기 중인 몬스터(보스 제외)도 같은 대상을 노리고 달려든다(무리 가세). 0이면 혼자 싸운다. AOI 최소 보장 시야 이하여야 한다.</item>
/// <item>SkillId : 이 몬스터가 추적 중 대상이 스킬 사거리에 들어오면 쓰는 스킬(Skill 시트). 0이면 기본 공격만 한다. 보스는 BossAction을 쓰므로 무시한다. 적 대상 스킬이어야 하며, 시전 시간이 있으면 클라이언트에 예고가 나가므로 피할 수 있다. 재사용 대기는 스킬 데이터를 따른다.</item>
/// </list>
/// </summary>
public sealed class MonsterTemplate
{
    public uint Id;                                     // 템플릿 ID (유일)
    public string Name = string.Empty;                  // 이름 (UTF-8, 로그/디버깅용)
    public CombatStats Stats = CombatStats.Default;     // 전투 능력치
    public float Speed = 3.0f;                          // 이동 속도 (units/sec)
    public float AggroRange = 8.0f;                     // 선공 감지 거리 (units)
    public float LeashRange = 30.0f;                    // 추적 한계 거리 (units)
    public uint RespawnMs = 10000;                      // 부활 시간 (ms)
    public float AssistRange;                           // 무리 가세 거리 (units). 0이면 없음
    public uint SkillId;                                // 몬스터 스킬 ID. 0이면 없음
}

/// <summary>몬스터 스폰 지점. 기획 데이터의 Spawn 시트 한 행에 대응한다.</summary>
public struct SpawnEntry
{
    public uint ZoneId;         // 스폰될 존 ID
    public uint MonsterId;      // 몬스터 템플릿 ID
    public uint Count;          // 마릿수
    public float X;             // 스폰 중심 X
    public float Y;             // 스폰 중심 Y
    public float Radius;        // 스폰 반경. 최초 스폰과 부활 위치를 이 원 안에서 무작위로 정한다
}

/// <summary>
/// 몬스터 템플릿과 스폰 테이블 모음. 로드가 끝나면 변경하지 않으며, 존들이 같은 인스턴스를 공유한다.
/// Validate()는 값 범위와 테이블 간 참조(스폰 -> 템플릿)를 검사하며, 통과해야 서버가 구동된다.
/// </summary>
public sealed class MonsterData
{
    public const int MaxSpawnPerZone = 20000;   // 한 존의 최대 몬스터 수 (설정 실수로 메모리가 폭주하는 것을 막는다)

    private readonly Dictionary<uint, MonsterTemplate> _monsters = new Dictionary<uint, MonsterTemplate>();     // 템플릿 ID -> 템플릿
    private readonly List<SpawnEntry> _spawns = new List<SpawnEntry>();                                         // 스폰 테이블

    /// <summary>몬스터 템플릿을 추가한다. ID가 이미 있으면 false.</summary>
    public bool AddMonster(MonsterTemplate monster) => _monsters.TryAdd(monster.Id, monster);

    /// <summary>스폰 지점을 추가한다.</summary>
    public void AddSpawn(SpawnEntry spawn) => _spawns.Add(spawn);

    /// <summary>템플릿을 ID로 찾는다. 없으면 null.</summary>
    public MonsterTemplate? FindMonster(uint id) => _monsters.TryGetValue(id, out MonsterTemplate? m) ? m : null;

    /// <summary>전체 스폰 테이블.</summary>
    public IReadOnlyList<SpawnEntry> Spawns => _spawns;

    /// <summary>등록된 템플릿 수.</summary>
    public int MonsterCount => _monsters.Count;

    /// <summary>전체 템플릿.</summary>
    public IEnumerable<MonsterTemplate> Monsters => _monsters.Values;

    private static bool IsFinitePositive(float v) => float.IsFinite(v) && v > 0.0f;
    private static bool IsFiniteNonNegative(float v) => float.IsFinite(v) && v >= 0.0f;

    private static bool Fail(out string error, string format, uint a)
    {
        error = string.Format(format, a);
        return false;
    }

    /// <summary>
    /// 데이터의 값 범위와 테이블 간 참조를 검사한다. 첫 번째 오류만 설명으로 돌려준다. 스폰 오류의 spawn[n]은 Spawn 테이블의 순번(0부터)이다.
    /// 검사 항목:
    /// 템플릿: ID != 0, MaxHp >= 1, Attack/Defense >= 0, 사거리/속도/간격 > 0, 시야 거리 유한, LeashRange >= AggroRange, RespawnMs >= 500, AssistRange >= 0.
    /// 스폰: 템플릿 존재, Count >= 1, 좌표/반경 유한, 존당 총 마릿수 &lt;= MaxSpawnPerZone.
    /// 존의 영역/AOI에 의존하는 검사는 MonsterSystem.Validate()가 맡는다.
    /// </summary>
    public bool Validate(out string error)
    {
        error = string.Empty;

        foreach (MonsterTemplate m in _monsters.Values)
        {
            uint id = m.Id;

            if (m.Id == 0) { error = "monster id must not be 0"; return false; }
            if (m.Stats.MaxHp < 1) return Fail(out error, "monster {0}: maxHp must be >= 1", id);
            if (m.Stats.Attack < 0) return Fail(out error, "monster {0}: attack must be >= 0", id);
            if (m.Stats.Defense < 0) return Fail(out error, "monster {0}: defense must be >= 0", id);
            if (!IsFinitePositive(m.Stats.AttackRange)) return Fail(out error, "monster {0}: attackRange must be > 0", id);
            if (m.Stats.AttackIntervalMs < 1) return Fail(out error, "monster {0}: attackIntervalMs must be >= 1", id);
            if (!IsFinitePositive(m.Speed)) return Fail(out error, "monster {0}: speed must be > 0", id);
            if (!IsFiniteNonNegative(m.AggroRange)) return Fail(out error, "monster {0}: aggroRange must be >= 0", id);
            if (!IsFiniteNonNegative(m.LeashRange) || m.LeashRange < m.AggroRange)
                return Fail(out error, "monster {0}: leashRange must be >= aggroRange", id);
            if (m.RespawnMs < 500) return Fail(out error, "monster {0}: respawnMs must be >= 500", id);
            if (!IsFiniteNonNegative(m.AssistRange)) return Fail(out error, "monster {0}: assistRange must be >= 0", id);
        }

        SortedDictionary<uint, ulong> perZone = new SortedDictionary<uint, ulong>();
        for (int i = 0; i < _spawns.Count; ++i)
        {
            SpawnEntry s = _spawns[i];
            uint row = (uint)i;

            if (FindMonster(s.MonsterId) == null)
                return Fail(out error, "spawn[{0}]: unknown monster id", row);
            if (s.Count < 1)
                return Fail(out error, "spawn[{0}]: count must be >= 1", row);
            if (!float.IsFinite(s.X) || !float.IsFinite(s.Y) || !IsFiniteNonNegative(s.Radius))
                return Fail(out error, "spawn[{0}]: x/y must be finite and radius >= 0", row);

            perZone.TryGetValue(s.ZoneId, out ulong total);
            total += s.Count;
            perZone[s.ZoneId] = total;
            if (total > MaxSpawnPerZone)
                return Fail(out error, "zone {0}: total spawn count exceeds the limit", s.ZoneId);
        }

        return true;
    }
}
