// BossData.cs : 보스 전투 데이터 (서버 구동 시 로드되는 불변 데이터)
using System;
using System.Collections.Generic;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>보스 데이터 상한.</summary>
public static class BossRules
{
    public const uint MaxBossAdds = GameConsts.MaxBossAdds;                         // 보스 한 마리가 동시에 데리고 있을 수 있는 소환 몬스터 수 (Const 시트)
    public const uint MaxBossActionsPerPhase = GameConsts.MaxBossActionsPerPhase;   // 페이즈 하나의 행동 수 상한 (Const 시트)
    public const uint MaxBossPhases = 8;                                            // 보스 하나의 페이즈 수 상한
    public const uint MaxBossDelayMs = 600000;                                      // 행동 뒤 대기 시간 상한
    public const uint MaxBossTransitionMs = 60000;                                  // 페이즈 전환 지연 상한
    public const uint MaxBossEnrageMs = 86400000;                                   // 격노 시간 상한 (24시간)
    public const uint MinBossLoopMs = 500;                                          // 반복 행동 한 바퀴의 최소 길이. 이보다 짧으면 매 틱 행동하는 보스가 된다
    public const float MaxBossSpreadRadius = 30.0f;                                 // 소환 위치 반경 상한
}

/// <summary>
/// 보스 행동 하나. 기획 데이터의 BossAction 시트 한 행에 대응한다.
/// <list type="bullet">
/// <item>Cast   : SkillId의 스킬을 Target 쪽으로 시전한다. 대상 후보는 스킬 사거리 안의 플레이어이며 Self는 보스 위치다. 시전 시간이 있는 Area 스킬은 시전을 시작할 때 중심이 정해져, 맞는 쪽이 피할 시간을 갖는다.</item>
/// <item>Summon : MonsterId 몬스터를 Count마리, 보스 주변 SpreadRadius 안에 소환한다. 보스의 현재 대상에게 어그로를 둔다.</item>
/// <item>Aggro  : Target으로 고른 플레이어를 보스의 어그로 1순위로 올린다. SkillId가 있으면 그 사거리를, 없으면 보스 선공 거리를 후보 범위로 쓴다.</item>
/// </list>
/// 행동이 끝나면(시전 시간 포함) DelayMs 뒤에 다음 행동으로 넘어간다.
/// </summary>
public sealed class BossActionTemplate
{
    public uint Seq;                                    // 같은 (페이즈, trigger) 안의 실행 순서 (1부터, 중복 불가)
    public EBossTrigger Trigger = EBossTrigger.Loop;
    public EBossActionType Type = EBossActionType.Cast;
    public EBossTarget Target = EBossTarget.Current;
    public uint SkillId;
    public uint MonsterId;
    public uint Count;
    public float SpreadRadius;
    public uint DelayMs;
}

/// <summary>
/// 보스 페이즈 하나. 기획 데이터의 BossPhase 시트 한 행과 그 페이즈의 BossAction들.
/// Actions는 OnEnter 행동(Seq 순)이 앞에, Loop 행동(Seq 순)이 뒤에 온다. LoopStart는 첫 Loop 행동의 위치다.
/// </summary>
public sealed class BossPhaseTemplate
{
    public uint Phase;                  // 1부터 빠짐없이
    public uint HpBelowPercent = 100;   // 체력이 이 % 이하가 되면 진입 (1페이즈는 100)
    public uint EnterStatusId;          // 진입 때 보스에게 거는 Self 상태 효과 (0이면 없음)
    public uint EnterHealPercent;       // 진입 때 최대 체력의 이 %만큼 회복
    public uint TransitionMs;           // 진입 후 첫 행동까지의 지연
    public readonly List<BossActionTemplate> Actions = new List<BossActionTemplate>();
    public int LoopStart;
}

/// <summary>보스 한 종류. 기획 데이터의 Boss 시트 한 행과 그 페이즈들.</summary>
public sealed class BossTemplate
{
    public uint Id;                     // 보스 ID (유일)
    public uint MonsterId;              // Monster 시트의 몬스터. 이 몬스터는 어디에 스폰되든 보스 규칙을 따른다 (몬스터당 하나)
    public string Name = string.Empty;
    public uint EnrageAfterMs;          // 전투 시작 후 이 시간이 지나면 격노 (0이면 없음)
    public uint EnrageStatusId;         // 격노 때 보스에게 거는 Self 상태 효과
    public readonly List<BossPhaseTemplate> Phases = new List<BossPhaseTemplate>();     // phase 오름차순, 1개 이상
}

/// <summary>
/// 보스 템플릿 모음. 로드가 끝나면 변경하지 않으며, 존들이 같은 인스턴스를 공유한다.
/// Validate()는 값 범위와 테이블 간 참조(몬스터, 스킬, 상태 효과)를 검사하며, 통과해야 서버가 구동된다.
/// 존의 AOI 시야에 의존하는 검사는 BossSystem.Validate()가 한다.
/// </summary>
public sealed class BossData
{
    private readonly List<BossTemplate> _bosses = new List<BossTemplate>();
    private readonly Dictionary<uint, int> _byMonster = new Dictionary<uint, int>();    // 몬스터 ID -> _bosses 위치

    /// <summary>보스를 등록한다. 보스 ID나 몬스터 ID가 이미 있으면 거부한다. (값 검사는 Validate)</summary>
    public bool AddBoss(BossTemplate boss)
    {
        if (_byMonster.ContainsKey(boss.MonsterId))
            return false;

        foreach (BossTemplate b in _bosses)
            if (b.Id == boss.Id)
                return false;

        _byMonster[boss.MonsterId] = _bosses.Count;
        _bosses.Add(boss);
        return true;
    }

    /// <summary>몬스터 템플릿 ID로 보스 정의를 찾는다. 보스가 아니면 null.</summary>
    public BossTemplate? FindByMonster(uint monsterId) => _byMonster.TryGetValue(monsterId, out int i) ? _bosses[i] : null;

    public IReadOnlyList<BossTemplate> Bosses => _bosses;
    public int BossCount => _bosses.Count;

    /// <summary>보스 데이터 오류 문장을 만든다. (boss N phase M action K: 사유) phase 0이면 보스 전체, actionIdx -1이면 페이즈 전체.</summary>
    private static string Where(uint bossId, uint phase, int actionIdx, string what)
    {
        if (phase == 0)
            return $"boss {bossId}: {what}";
        if (actionIdx < 0)
            return $"boss {bossId} phase {phase}: {what}";
        return $"boss {bossId} phase {phase} action #{actionIdx + 1}: {what}";
    }

    /// <summary>보스 자신에게 거는 상태 효과인지 확인한다. (없는 ID, Self가 아닌 효과는 거부)</summary>
    private static bool CheckSelfStatus(HeroData data, uint statusId, uint bossId, uint phase, string label, out string error)
    {
        error = string.Empty;
        if (statusId == 0)
            return true;

        StatusTemplate? status = data.FindStatus(statusId);
        if (status == null)
        {
            error = Where(bossId, phase, -1, label) + " unknown status id";
            return false;
        }
        if (status.Target != EStatusTarget.Self)
        {
            error = Where(bossId, phase, -1, label) + " status must have Target=Self";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 보스 데이터의 값 범위와 테이블 간 참조를 검사한다.
    /// 보스: 몬스터 존재, 격노 시간/상태 효과(Self).
    /// 페이즈: 1부터 빠짐없이, 1페이즈 100%, 체력 기준은 점점 작아짐, 진입 상태 효과(Self), 회복 0~100%, 전환 지연 상한.
    /// 행동: 개수 상한, Seq가 1부터 늘어나는 순서, 대기 시간 상한, 종류별 필수 값
    /// (Cast는 스킬 존재 + Self 대상은 Area 스킬, Summon은 보스가 아닌 몬스터 + 마릿수/반경, Aggro는 Current/Self 대상 불가).
    /// 반복 행동 한 바퀴가 MinBossLoopMs 이상이어야 한다 (매 틱 행동하는 보스 방지).
    /// </summary>
    /// <param name="monsters">몬스터 데이터 (참조 대상)</param>
    /// <param name="skills">스킬/상태 효과 데이터 (참조 대상)</param>
    public bool Validate(MonsterData monsters, HeroData skills, out string error)
    {
        error = string.Empty;

        foreach (BossTemplate boss in _bosses)
        {
            if (boss.Id == 0)
            {
                error = "boss: id must be at least 1";
                return false;
            }

            if (monsters.FindMonster(boss.MonsterId) == null)
            {
                error = Where(boss.Id, 0, 0, "unknown MonsterId");
                return false;
            }

            if (boss.EnrageAfterMs > BossRules.MaxBossEnrageMs)
            {
                error = Where(boss.Id, 0, 0, "EnrageAfterMs is too large");
                return false;
            }
            if ((boss.EnrageAfterMs == 0) != (boss.EnrageStatusId == 0))
            {
                error = Where(boss.Id, 0, 0, "EnrageAfterMs and EnrageStatusId must be set together");
                return false;
            }
            if (!CheckSelfStatus(skills, boss.EnrageStatusId, boss.Id, 0, "EnrageStatusId", out error))
                return false;

            if (boss.Phases.Count == 0 || boss.Phases.Count > BossRules.MaxBossPhases)
            {
                error = Where(boss.Id, 0, 0, "needs 1 to 8 phases");
                return false;
            }

            for (int p = 0; p < boss.Phases.Count; ++p)
            {
                BossPhaseTemplate phase = boss.Phases[p];
                uint pn = (uint)(p + 1);

                if (phase.Phase != pn)
                {
                    error = Where(boss.Id, pn, -1, "Phase numbers must start at 1 and have no gaps");
                    return false;
                }
                if (p == 0 && phase.HpBelowPercent != 100)
                {
                    error = Where(boss.Id, pn, -1, "the first phase must have HpBelowPercent=100");
                    return false;
                }
                if (p > 0 && !(phase.HpBelowPercent >= 1 && phase.HpBelowPercent < boss.Phases[p - 1].HpBelowPercent))
                {
                    error = Where(boss.Id, pn, -1, "HpBelowPercent must be smaller than the previous phase (and at least 1)");
                    return false;
                }
                if (phase.EnterHealPercent > 100 || phase.TransitionMs > BossRules.MaxBossTransitionMs)
                {
                    error = Where(boss.Id, pn, -1, "EnterHealPercent must be 0..100 and TransitionMs at most 60000");
                    return false;
                }
                if (!CheckSelfStatus(skills, phase.EnterStatusId, boss.Id, pn, "EnterStatusId", out error))
                    return false;

                if (phase.Actions.Count > BossRules.MaxBossActionsPerPhase || phase.LoopStart < 0 || phase.LoopStart > phase.Actions.Count)
                {
                    error = Where(boss.Id, pn, -1, "too many actions");
                    return false;
                }

                ulong loopMs = 0;
                uint lastSeq = 0;
                EBossTrigger lastTrigger = EBossTrigger.OnEnter;
                for (int a = 0; a < phase.Actions.Count; ++a)
                {
                    BossActionTemplate act = phase.Actions[a];
                    bool isLoop = a >= phase.LoopStart;

                    if ((act.Trigger == EBossTrigger.Loop) != isLoop)
                    {
                        error = Where(boss.Id, pn, a, "OnEnter actions must come before Loop actions");
                        return false;
                    }
                    if (a == 0 || act.Trigger != lastTrigger)
                        lastSeq = 0;
                    if (act.Seq <= lastSeq)
                    {
                        error = Where(boss.Id, pn, a, "Seq must be unique and increasing within a trigger");
                        return false;
                    }
                    lastSeq = act.Seq;
                    lastTrigger = act.Trigger;

                    if (act.DelayMs > BossRules.MaxBossDelayMs)
                    {
                        error = Where(boss.Id, pn, a, "DelayMs is too large");
                        return false;
                    }

                    uint castMs = 0;
                    switch (act.Type)
                    {
                        case EBossActionType.Cast:
                        {
                            SkillTemplate? skill = skills.FindSkill(act.SkillId);
                            if (skill == null)
                            {
                                error = Where(boss.Id, pn, a, "Cast needs a valid SkillId");
                                return false;
                            }
                            if (skill.MpCost != 0)
                            {
                                error = Where(boss.Id, pn, a, "a boss skill must not cost MP (bosses have no MP)");
                                return false;
                            }
                            if (act.Target == EBossTarget.Self && skill.Shape != ESkillShape.Area && skill.Shape != ESkillShape.Self)
                            {
                                error = Where(boss.Id, pn, a, "Target=Self needs an Area skill (or a Self-shape skill)");
                                return false;
                            }
                            if (skill.Shape == ESkillShape.Self && act.Target != EBossTarget.Self)
                            {
                                error = Where(boss.Id, pn, a, "a Self skill needs Target=Self");
                                return false;
                            }
                            if (skill.Target == ESkillTarget.Ally && act.Target != EBossTarget.Self)
                            {
                                error = Where(boss.Id, pn, a, "an Ally skill needs Target=Self (bosses only target players otherwise)");
                                return false;
                            }
                            castMs = skill.CastTimeMs;
                            break;
                        }
                        case EBossActionType.Summon:
                        {
                            MonsterTemplate? mon = monsters.FindMonster(act.MonsterId);
                            if (mon == null)
                            {
                                error = Where(boss.Id, pn, a, "Summon needs a valid MonsterId");
                                return false;
                            }
                            if (_byMonster.ContainsKey(act.MonsterId))
                            {
                                error = Where(boss.Id, pn, a, "a boss cannot be summoned");
                                return false;
                            }
                            if (act.Count < 1 || act.Count > BossRules.MaxBossAdds)
                            {
                                error = Where(boss.Id, pn, a, "Count must be 1 up to MaxBossAdds");
                                return false;
                            }
                            if (!(act.SpreadRadius >= 0.0f && act.SpreadRadius <= BossRules.MaxBossSpreadRadius))
                            {
                                error = Where(boss.Id, pn, a, "SpreadRadius must be 0..30");
                                return false;
                            }
                            if (act.Target != EBossTarget.Self)
                            {
                                error = Where(boss.Id, pn, a, "Summon must use Target=Self");
                                return false;
                            }
                            break;
                        }
                        case EBossActionType.Aggro:
                            if (act.Target == EBossTarget.Current || act.Target == EBossTarget.Self)
                            {
                                error = Where(boss.Id, pn, a, "Aggro cannot target Current or Self");
                                return false;
                            }
                            if (act.SkillId != 0 && skills.FindSkill(act.SkillId) == null)
                            {
                                error = Where(boss.Id, pn, a, "unknown SkillId");
                                return false;
                            }
                            break;
                        default:
                            error = Where(boss.Id, pn, a, "unknown action type");
                            return false;
                    }

                    if (isLoop)
                        loopMs += (ulong)castMs + act.DelayMs;
                }

                if (phase.LoopStart < phase.Actions.Count && loopMs < BossRules.MinBossLoopMs)
                {
                    error = Where(boss.Id, pn, -1, "the loop actions add up to less than 500ms per cycle");
                    return false;
                }
            }
        }

        return true;
    }
}
