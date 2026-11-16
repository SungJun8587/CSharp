// BossSystem.cs : 데이터(Boss/BossPhase/BossAction)로 정의한 보스 전투를 구동하는 시스템. MonsterSystem 위에 얹는다.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Core.Util;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>보스 개체 하나의 전투 진행 상태. 템플릿(불변)은 가리키기만 하고 개체별 상태만 가진다.</summary>
public sealed class BossRuntime
{
    public BossTemplate Template = null!;           // 보스 정의
    public int PhaseIdx;                            // 현재 페이즈 위치 (Template.Phases 인덱스)
    public bool Engaged;                            // 전투 중인지 (첫 추적 사고에서 켜지고, 복귀/사망/부활에서 꺼짐)
    public bool Enraged;                            // 격노가 걸렸는지
    public ulong EngagedAtMs;                       // 전투를 시작한 존 논리 시각
    public int ActionIdx;                           // 현재 페이즈에서 다음에 할 행동 위치
    public ulong NextActionMs;                      // 이 시각 전에는 행동하지 않는다
    public ulong BlockedSinceMs;                    // 현재 행동이 조건을 못 채워 기다리기 시작한 시각 (0이면 막히지 않음)
    internal readonly Dictionary<uint, ulong> ReadyAtMs = new Dictionary<uint, ulong>();    // 스킬 ID -> 다시 쓸 수 있는 시각
    internal readonly List<ulong> Adds = new List<ulong>();     // 이 보스가 소환한 몬스터의 엔티티 ID

    public int AddCount => Adds.Count;
}

/// <summary>
/// 데이터로 정의한 보스 전투를 구동하는 시스템. 보스는 평범한 몬스터(Monster)이며, 템플릿 ID가 보스 데이터에 있으면 보스 규칙이 추가로 적용된다.
/// 추적/기본 공격/어그로/복귀는 MonsterSystem이 그대로 하고, 이 시스템은 추적 사고마다 다음을 한다.
///   1) 격노: 전투 시작 후 정해진 시간이 지나면 상태 효과를 건다
///   2) 페이즈: 체력이 다음 페이즈의 기준 % 이하가 되면 한 사고에 한 단계씩 올라간다 (상태 효과, 회복, 첫 행동 지연)
///   3) 행동: 때가 된 행동을 하나 실행한다 (스킬 시전 / 소환 / 어그로 교체). 조건이 안 맞으면 행동 대기 한도까지 기다린 뒤 건너뛴다
/// 시전 시간이 있는 스킬을 쓰는 동안 보스는 멈추고 기본 공격도 하지 않는다. 범위 스킬의 중심은 시전 시작 때 정해지므로 플레이어가 피할 수 있다.
/// 보스가 복귀하거나 죽거나 부활하면 페이즈/격노/소환수가 초기화된다. 모든 메서드는 존 스레드에서만 호출한다.
/// </summary>
public sealed class BossSystem
{
    public const uint ActionTimeoutMsDefault = 5000;    // 행동 조건을 못 채울 때 기다리는 기본 최대 시간. 지나면 그 행동을 건너뛴다
    public const uint AddCorpseMs = 3000;               // 소환수가 죽은 뒤 시체가 남는 시간 (MonsterSystem이 사용)
    public const int AggroBoostPercent = 125;           // Aggro 행동이 대상의 위협을 올리는 목표 (현재 최대 위협의 %)

    private enum Outcome : byte { Done, Retry, Blocked }    // 행동 시도 결과

    private readonly FieldZoneBase _zone;
    private readonly MonsterSystem _monsters;
    private readonly BossData _data;                    // 보스 데이터
    private readonly HeroData _skills;                  // 스킬/상태 효과 데이터 (영웅과 같은 시트를 쓴다)
    private readonly MonsterData _monsterData;
    private readonly Dictionary<ulong, BossRuntime> _runtimes = new Dictionary<ulong, BossRuntime>();   // 엔티티 ID -> 보스 상태
    private uint _actionTimeoutMs = ActionTimeoutMsDefault;
    private readonly Random _rng;
    private static readonly Comparison<ZoneEntity> s_byId = (a, b) => a.Id.CompareTo(b.Id);
    private Monster? _pickBoss;                                  // PickEntity 방문 상태 (존 스레드 전용)
    private readonly Action<ZoneEntity> _pickFn;
    private readonly List<ZoneEntity> _candidates = new List<ZoneEntity>(64);   // 대상 후보 작업 공간 (존 스레드 전용)

    /// <param name="zone">보스를 구동할 존 (이 객체보다 오래 살아야 함)</param>
    /// <param name="monsters">몬스터 시스템 (소환수를 만든다)</param>
    /// <param name="data">보스 데이터 (null이면 보스 없이 동작)</param>
    /// <param name="skills">스킬/상태 효과 데이터</param>
    /// <param name="monsterData">몬스터 데이터 (참조 검증용)</param>
    public BossSystem(FieldZoneBase zone, MonsterSystem monsters, BossData? data, HeroData? skills, MonsterData? monsterData)
    {
        _pickFn = PickVisit;
        _zone = zone;
        _monsters = monsters;
        _data = data ?? new BossData();
        _skills = skills ?? HeroData.MakeDefault();
        _monsterData = monsterData ?? new MonsterData();
        _rng = new Random(unchecked((int)(0x9E3779B9u ^ zone.ZoneId)));
    }

    /// <summary>행동 조건(대상/사거리/재사용)이 안 맞을 때 기다리는 최대 시간을 바꾼다. 존 구동 전에 호출한다.</summary>
    public void SetActionTimeoutMs(uint ms) => _actionTimeoutMs = ms;

    /// <summary>
    /// 보스 데이터가 유효하고 이 존의 AOI 시야 안에서 동작하는지 검사한다. Start() 전에 호출한다.
    /// 데이터 자체의 검사(BossData.Validate), 보스 스킬의 시전 거리 + 효과 반경과 소환 반경이 AOI 보장 시야 이하,
    /// 소환하는 몬스터 템플릿도 스폰 테이블의 몬스터와 같은 기준으로 검사한다(MonsterSystem.ValidateTemplate).
    /// </summary>
    public bool Validate(out string error, float opponentMaxSpeed = 0.0f)
    {
        if (!_data.Validate(_monsterData, _skills, out error))
            return false;

        float viewRange = _zone.GetGuaranteedViewRange();
        List<string> errors = new List<string>();
        HashSet<uint> checkedSummons = new HashSet<uint>();
        string view = viewRange.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        foreach (BossTemplate boss in _data.Bosses)
        {
            foreach (BossPhaseTemplate phase in boss.Phases)
            {
                foreach (BossActionTemplate act in phase.Actions)
                {
                    if (act.Type == EBossActionType.Cast)
                    {
                        SkillTemplate? skill = _skills.FindSkill(act.SkillId);
                        if (skill != null && skill.Range + skill.Radius > viewRange)
                            errors.Add($"boss {boss.Id} skill {skill.Id}: range + radius exceeds the guaranteed AOI view range {view}");
                    }
                    else if (act.Type == EBossActionType.Summon)
                    {
                        if (act.SpreadRadius > viewRange)
                            errors.Add($"boss {boss.Id}: SpreadRadius exceeds the guaranteed AOI view range {view}");

                        // 소환수는 스폰 테이블에 없어도 같은 기준을 지켜야 한다.
                        MonsterTemplate? summon = _monsterData.FindMonster(act.MonsterId);
                        if (summon != null && checkedSummons.Add(summon.Id))
                            _monsters.ValidateTemplate(summon, opponentMaxSpeed, errors);
                    }
                }
            }
        }

        return MonsterSystem.JoinErrors(errors, out error);
    }

    public int BossCount => _runtimes.Count;

    /// <summary>보스 개체의 전투 상태를 조회한다 (테스트/운영 확인용). 보스가 아니면 null.</summary>
    public BossRuntime? Find(ulong entityId) => _runtimes.TryGetValue(entityId, out BossRuntime? rt) ? rt : null;

    // ── MonsterSystem이 호출하는 훅 ──────────────────────

    /// <summary>몬스터가 존에 배치된 직후 호출한다. 보스 데이터에 있는 몬스터면 전투 상태를 만들어 붙인다.</summary>
    public void OnSpawned(Monster monster)
    {
        BossTemplate? tmpl = _data.FindByMonster(monster.Template.Id);
        if (tmpl == null)
            return;

        BossRuntime rt = new BossRuntime { Template = tmpl };
        monster._boss = rt;
        _runtimes[monster.Id] = rt;
    }

    /// <summary>보스가 추적 상태에서 사고할 때마다 호출한다. 격노, 페이즈, 행동 순으로 처리한다.</summary>
    /// <returns>보스가 시전을 시작해 이번 사고에서 더 움직이거나 공격하면 안 되면 true</returns>
    public bool OnChase(Monster monster, ZoneEntity target, ulong nowMs)
    {
        BossRuntime? rt = monster._boss;
        if (rt == null)
            return false;

        if (!rt.Engaged)
        {
            rt.Engaged = true;
            rt.EngagedAtMs = nowMs;
            StartPhase(rt, monster, 0, nowMs);
        }

        CheckEnrage(rt, monster, nowMs);
        CheckPhase(rt, monster, nowMs);
        RunAction(rt, monster, target, nowMs);
        return monster.IsCasting;
    }

    /// <summary>보스가 복귀하거나 부활할 때 호출한다. 페이즈/격노/재사용 대기를 처음으로 돌리고, 건 상태 효과를 풀고, 소환수를 치운다.</summary>
    public void OnReset(Monster monster)
    {
        BossRuntime? rt = monster._boss;
        if (rt == null)
            return;

        DespawnAdds(rt);
        ResetRuntime(rt);

        if (monster.IsAlive)
            _zone.ClearStatuses(monster, StatusEndReason.Expired);
    }

    /// <summary>보스가 죽었을 때 호출한다. 소환수를 치우고 전투 상태를 초기화한다. (부활하면 처음부터 다시 시작)</summary>
    public void OnDied(Monster monster)
    {
        BossRuntime? rt = monster._boss;
        if (rt == null)
            return;

        DespawnAdds(rt);
        ResetRuntime(rt);
    }

    /// <summary>보스가 존에서 제거되기 직전에 호출한다. 상태를 지우고 소환수를 치운다.</summary>
    public void OnRemoved(Monster monster)
    {
        BossRuntime? rt = monster._boss;
        if (rt == null)
            return;

        DespawnAdds(rt);
        monster._boss = null;
        _runtimes.Remove(monster.Id);
    }

    /// <summary>페이즈에 들어간다. 진입 상태 효과와 회복을 적용하고 행동 순서를 처음으로 돌린다.</summary>
    private void StartPhase(BossRuntime rt, Monster boss, int phaseIdx, ulong nowMs)
    {
        BossPhaseTemplate phase = rt.Template.Phases[phaseIdx];

        rt.PhaseIdx = phaseIdx;
        rt.ActionIdx = 0;
        rt.NextActionMs = nowMs + phase.TransitionMs;
        rt.BlockedSinceMs = 0;

        if (phase.EnterStatusId != 0)
        {
            StatusTemplate? status = _skills.FindStatus(phase.EnterStatusId);
            if (status != null)
                _zone.ApplyStatus(boss, status, boss);
        }

        if (phase.EnterHealPercent != 0)
        {
            long amount = (long)boss.CombatStats.MaxHp * phase.EnterHealPercent / 100;
            _zone.Heal(boss, (int)amount);
        }
    }

    /// <summary>전투 시작 후 정해진 시간이 지났으면 격노 상태 효과를 한 번 건다.</summary>
    private void CheckEnrage(BossRuntime rt, Monster boss, ulong nowMs)
    {
        if (rt.Enraged || rt.Template.EnrageAfterMs == 0 || nowMs - rt.EngagedAtMs < rt.Template.EnrageAfterMs)
            return;

        rt.Enraged = true;
        StatusTemplate? status = _skills.FindStatus(rt.Template.EnrageStatusId);
        if (status != null)
            _zone.ApplyStatus(boss, status, boss);
    }

    /// <summary>
    /// 체력이 다음 페이즈의 기준 % 이하면 한 단계 올린다. 한 사고에 한 단계씩이라 건너뛴 페이즈의 진입 효과도 빠짐없이 적용된다.
    /// 보스가 시전 중일 때는 사고가 건너뛰어지므로 전환은 시전이 끝난 다음 사고에서 일어난다 (시전은 중간에 끊지 않는다).
    /// </summary>
    private void CheckPhase(BossRuntime rt, Monster boss, ulong nowMs)
    {
        if (rt.PhaseIdx + 1 >= rt.Template.Phases.Count)
            return;

        BossPhaseTemplate next = rt.Template.Phases[rt.PhaseIdx + 1];
        long maxHp = boss.CombatStats.MaxHp;
        if ((long)boss.Hp * 100 <= (long)next.HpBelowPercent * maxHp)
            StartPhase(rt, boss, rt.PhaseIdx + 1, nowMs);
    }

    /// <summary>때가 된 행동 하나를 실행한다. 조건(대상, 사거리, 재사용)이 안 맞으면 다음 사고에서 다시 시도하고, 대기 한도를 넘게 막히면 그 행동을 건너뛴다.</summary>
    private void RunAction(BossRuntime rt, Monster boss, ZoneEntity current, ulong nowMs)
    {
        if (nowMs < rt.NextActionMs)
            return;

        BossPhaseTemplate phase = rt.Template.Phases[rt.PhaseIdx];
        if (rt.ActionIdx >= phase.Actions.Count)
        {
            if (phase.LoopStart >= phase.Actions.Count)
                return;     // 반복 행동이 없는 페이즈: 진입 행동을 마치면 기본 공격만 한다
            rt.ActionIdx = phase.LoopStart;
        }

        BossActionTemplate act = phase.Actions[rt.ActionIdx];

        switch (act.Type)
        {
            case EBossActionType.Cast:
            {
                uint waitMs = act.DelayMs;
                switch (DoCast(rt, boss, current, act, ref waitMs))
                {
                    case Outcome.Done:
                        Advance(rt, phase, nowMs, waitMs);
                        break;
                    case Outcome.Retry:
                        break;
                    case Outcome.Blocked:
                        if (rt.BlockedSinceMs == 0)
                            rt.BlockedSinceMs = nowMs;
                        else if (nowMs - rt.BlockedSinceMs >= _actionTimeoutMs)
                            Advance(rt, phase, nowMs, act.DelayMs);     // 건너뛴다
                        break;
                }
                break;
            }
            case EBossActionType.Summon:
                DoSummon(rt, boss, current, act);
                Advance(rt, phase, nowMs, act.DelayMs);
                break;
            case EBossActionType.Aggro:
                DoAggro(boss, current, act);
                Advance(rt, phase, nowMs, act.DelayMs);
                break;
        }
    }

    /// <summary>다음 행동으로 넘어간다. 반복 행동 끝에서는 첫 반복 행동으로 돌아간다.</summary>
    private static void Advance(BossRuntime rt, BossPhaseTemplate phase, ulong nowMs, uint waitMs)
    {
        ++rt.ActionIdx;
        if (rt.ActionIdx >= phase.Actions.Count && phase.LoopStart < phase.Actions.Count)
            rt.ActionIdx = phase.LoopStart;

        rt.NextActionMs = nowMs + waitMs;
        rt.BlockedSinceMs = 0;
    }

    /// <summary>
    /// 스킬 시전 행동을 실행한다. 대상은 스킬 사거리 안에서 고르며, Self는 보스 위치를 중심으로 한다.
    /// Done: 시전함 / Retry: 다음 사고에 다시 (같은 틱 중복, 이미 시전 중) / Blocked: 조건이 안 맞음 (대상 없음, 사거리 밖, 재사용 대기 등)
    /// </summary>
    /// <param name="waitMs">성공했을 때 다음 행동까지의 대기 (시전 시간 + DelayMs)</param>
    private Outcome DoCast(BossRuntime rt, Monster boss, ZoneEntity current, BossActionTemplate act, ref uint waitMs)
    {
        SkillTemplate? skill = _skills.FindSkill(act.SkillId);
        if (skill == null)
            return Outcome.Done;        // 검증을 통과한 데이터에서는 일어나지 않는다. 걸리면 행동만 건너뛴다

        Vec2 center = boss.Pos;
        ulong targetId = 0;
        if (act.Target != EBossTarget.Self)
        {
            ZoneEntity? target = PickEntity(boss, current, act.Target, skill.Range);
            if (target == null)
                return Outcome.Blocked;

            center = target.Pos;
            if (skill.Shape == ESkillShape.Single)
                targetId = target.Id;
        }

        ref ulong readyAt = ref CollectionsMarshal.GetValueRefOrAddDefault(rt.ReadyAtMs, skill.Id, out _);
        switch (_zone.CastSkill(boss, skill, targetId, center, ref readyAt))
        {
            case SkillResult.Ok:
                waitMs = skill.CastTimeMs + act.DelayMs;
                return Outcome.Done;
            case SkillResult.Throttled:
            case SkillResult.Casting:
            case SkillResult.CasterDead:
            case SkillResult.Stunned:
                return Outcome.Retry;
            default:
                return Outcome.Blocked;
        }
    }

    /// <summary>소환 행동을 실행한다. 동시에 데리고 있을 수 있는 수(MaxBossAdds)까지만 소환하며, 소환수는 보스의 현재 대상에게 어그로를 둔다.</summary>
    private void DoSummon(BossRuntime rt, Monster boss, ZoneEntity current, BossActionTemplate act)
    {
        PruneAdds(rt);

        int room = rt.Adds.Count < BossRules.MaxBossAdds ? (int)BossRules.MaxBossAdds - rt.Adds.Count : 0;
        int count = (int)Math.Min(act.Count, (uint)room);

        for (int i = 0; i < count; ++i)
        {
            Monster? add = _monsters.SpawnSummon(act.MonsterId, boss.Pos, act.SpreadRadius, boss.Id, current.Id);
            if (add != null)
                rt.Adds.Add(add.Id);
        }
    }

    /// <summary>어그로 교체 행동을 실행한다. 고른 대상의 위협을 현재 최대 위협의 AggroBoostPercent%로 올려 다음 사고에서 대상이 바뀌게 한다.</summary>
    private void DoAggro(Monster boss, ZoneEntity current, BossActionTemplate act)
    {
        SkillTemplate? skill = act.SkillId != 0 ? _skills.FindSkill(act.SkillId) : null;
        float radius = skill != null ? skill.Range : boss.Template.AggroRange;

        ZoneEntity? target = PickEntity(boss, current, act.Target, radius);
        if (target == null)
            return;

        long maxThreat = 0;
        for (int i = 0; i < boss._aggroCount; ++i)
            maxThreat = Math.Max(maxThreat, boss._aggro[i].Threat);
        long currentThreat = boss.GetThreat(target.Id);

        if (maxThreat > long.MaxValue / AggroBoostPercent)
            return;

        long wanted = maxThreat * AggroBoostPercent / 100 + 1;
        if (wanted > currentThreat)
            boss.AddThreat(target.Id, wanted - currentThreat);
    }

    private void PickVisit(ZoneEntity e)
    {
        if (_zone.CanAttack(_pickBoss!, e))
            _candidates.Add(e);
    }

    /// <summary>
    /// 규칙에 맞는 대상을 고른다. 후보는 보스로부터 radius 안에서 공격 가능한 살아 있는 플레이어이며,
    /// 무작위는 ID 순으로 정렬한 뒤 고르므로 같은 시드에서 재현된다. 동점은 ID가 작은 쪽.
    /// </summary>
    /// <returns>대상, 후보가 없으면 null</returns>
    private ZoneEntity? PickEntity(Monster boss, ZoneEntity current, EBossTarget rule, float radius)
    {
        Vec2 pos = boss.Pos;

        if (rule == EBossTarget.Current)
            return (current.IsAlive && Vec2.DistanceSq(pos, current.Pos) <= radius * radius && _zone.CanAttack(boss, current)) ? current : null;

        _candidates.Clear();
        _pickBoss = boss;
        try
        {
            _zone.ForEachLivePlayerInRadius(pos, radius, _pickFn);
        }
        finally
        {
            _pickBoss = null;
        }

        if (_candidates.Count == 0)
            return null;

        _candidates.Sort(s_byId);

        if (rule == EBossTarget.Random)
            return _candidates[_rng.Next(_candidates.Count)];

        ZoneEntity best = _candidates[0];
        for (int i = 0; i < _candidates.Count; ++i)
        {
            ZoneEntity e = _candidates[i];
            switch (rule)
            {
                case EBossTarget.Farthest:
                    if (Vec2.DistanceSq(pos, e.Pos) > Vec2.DistanceSq(pos, best.Pos)) best = e;
                    break;
                case EBossTarget.Nearest:
                    if (Vec2.DistanceSq(pos, e.Pos) < Vec2.DistanceSq(pos, best.Pos)) best = e;
                    break;
                case EBossTarget.LowestHp:
                    if (e.Hp < best.Hp) best = e;
                    break;
            }
        }
        return best;
    }

    /// <summary>소환수를 모두 치운다. 제거는 다음 틱에 타이머로 한다 (몬스터 사고 목록을 순회하는 중일 수 있으므로).</summary>
    private void DespawnAdds(BossRuntime rt)
    {
        for (int i = 0; i < rt.Adds.Count; ++i)
        {
            ulong id = rt.Adds[i];
            if (!_zone.ScheduleAfter(_zone.TickIntervalMs, zone => ((FieldZoneBase)zone).RemoveEntity(id)))
                LogManager.Error("[BossSystem] add removal timer failed. zoneId={0}, entityId={1}", _zone.ZoneId, id);
        }
        rt.Adds.Clear();
    }

    /// <summary>소환수 목록에서 이미 제거되었거나 죽은 것을 뺀다.</summary>
    private void PruneAdds(BossRuntime rt)
    {
        List<ulong> adds = rt.Adds;
        int kept = 0;
        for (int i = 0; i < adds.Count; ++i)
        {
            ZoneEntity? e = _zone.FindEntityBase(adds[i]);
            if (e != null && e.IsAlive)
                adds[kept++] = adds[i];
        }
        adds.RemoveRange(kept, adds.Count - kept);
    }

    /// <summary>전투 상태를 처음으로 돌린다. (소환수 목록은 호출자가 먼저 처리)</summary>
    private static void ResetRuntime(BossRuntime rt)
    {
        rt.PhaseIdx = 0;
        rt.Engaged = false;
        rt.Enraged = false;
        rt.EngagedAtMs = 0;
        rt.ActionIdx = 0;
        rt.NextActionMs = 0;
        rt.BlockedSinceMs = 0;
        rt.ReadyAtMs.Clear();
    }
}
