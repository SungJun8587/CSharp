// HeroData.cs : 영웅(플레이어 캐릭터 종류) 템플릿 데이터 (서버 구동 시 로드되는 불변 데이터)
using System;
using System.Collections.Generic;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>
/// 영웅 종류별 데이터. 기획 데이터(엑셀)의 Hero 시트 한 행에 대응한다.
/// 플레이어가 입장할 때 선택한 영웅의 전투 능력치와 이동 속도가 그 플레이어 엔티티에 적용된다.
/// 값은 서버 데이터만 쓰며 클라이언트가 보낸 능력치는 신뢰하지 않는다.
/// </summary>
public sealed class HeroTemplate
{
    public uint Id;                                     // 영웅 ID (유일, 0 불가)
    public string Name = string.Empty;                  // 이름 (UTF-8, 로그/디버깅용)
    public CombatStats Stats = CombatStats.Default;     // 전투 능력치
    public float Speed = 6.0f;                          // 이동 속도 (units/sec)
    public readonly List<uint> Skills = new List<uint>();   // 사용할 수 있는 스킬 ID (최대 MaxHeroSkills). HeroData.AssignSkill로 채운다
}

/// <summary>
/// 영웅 템플릿 모음. 로드가 끝나면 변경하지 않으며, 존들이 같은 인스턴스를 공유한다.
/// 영웅은 1개 이상이어야 한다. 선택 정보가 없는 입장(heroId 0)은 ID가 가장 작은 영웅(기본 영웅)으로 처리한다.
/// Validate()는 값 범위를 검사하며, 통과해야 서버가 구동된다.
/// 존의 AOI 시야에 의존하는 검사(사거리 등)는 BattleZone.ValidateMonsters()가 영웅마다 수행한다.
/// </summary>
public sealed class HeroData
{
    private readonly SortedDictionary<uint, SkillTemplate> _skills = new SortedDictionary<uint, SkillTemplate>();       // 스킬 ID -> 스킬
    private readonly SortedDictionary<uint, StatusTemplate> _statuses = new SortedDictionary<uint, StatusTemplate>();   // 상태 효과 ID -> 상태 효과
    private readonly SortedDictionary<uint, HeroTemplate> _heroes = new SortedDictionary<uint, HeroTemplate>();         // 영웅 ID -> 영웅 (정렬되어 있어 첫 원소가 기본 영웅)

    /// <summary>영웅 템플릿을 추가한다. ID가 0이거나 이미 있으면 false.</summary>
    public bool AddHero(HeroTemplate hero)
    {
        if (hero.Id == 0)
            return false;

        return _heroes.TryAdd(hero.Id, hero);
    }

    /// <summary>스킬 템플릿을 추가한다. ID가 0이거나 이미 있으면 false.</summary>
    public bool AddSkill(SkillTemplate skill)
    {
        if (skill.Id == 0)
            return false;

        return _skills.TryAdd(skill.Id, skill);
    }

    /// <summary>상태 효과 템플릿을 추가한다. ID가 0이거나 이미 있으면 false.</summary>
    public bool AddStatus(StatusTemplate status)
    {
        if (status.Id == 0)
            return false;

        return _statuses.TryAdd(status.Id, status);
    }

    /// <summary>영웅에게 스킬을 연결한다. 영웅이 쓸 수 있는 스킬이 된다.</summary>
    /// <returns>연결했으면 true, 영웅/스킬이 없거나 이미 연결됐거나 영웅당 상한(MaxHeroSkills)을 넘으면 false</returns>
    public bool AssignSkill(uint heroId, uint skillId)
    {
        if (!_heroes.TryGetValue(heroId, out HeroTemplate? hero) || !_skills.ContainsKey(skillId))
            return false;

        List<uint> skills = hero.Skills;
        if (skills.Count >= SkillRules.MaxHeroSkills || skills.Contains(skillId))
            return false;

        skills.Add(skillId);
        return true;
    }

    /// <summary>영웅을 ID로 찾는다. 없으면 null.</summary>
    public HeroTemplate? FindHero(uint id) => _heroes.TryGetValue(id, out HeroTemplate? h) ? h : null;

    /// <summary>스킬을 ID로 찾는다. 없으면 null.</summary>
    public SkillTemplate? FindSkill(uint id) => _skills.TryGetValue(id, out SkillTemplate? s) ? s : null;

    /// <summary>영웅이 그 스킬을 쓸 수 있는지 확인하고 스킬을 돌려준다. 영웅이 가진 스킬이 아니면 null.</summary>
    public SkillTemplate? FindHeroSkill(HeroTemplate hero, uint skillId)
    {
        foreach (uint id in hero.Skills)
            if (id == skillId)
                return FindSkill(id);
        return null;
    }

    /// <summary>상태 효과를 ID로 찾는다. 없으면 null.</summary>
    public StatusTemplate? FindStatus(uint id) => _statuses.TryGetValue(id, out StatusTemplate? s) ? s : null;

    public int StatusCount => _statuses.Count;

    /// <summary>상태 효과 중 가장 큰 이동속도 증가(%). 영웅이 낼 수 있는 최고 속도 계산(AOI 시야 검증)에 쓴다. 증가 효과가 없으면 0.</summary>
    public int GetMaxSpeedBuffPercent()
    {
        int best = 0;
        foreach (StatusTemplate s in _statuses.Values)
            if (s.Kind == EStatusKind.Buff && s.SpeedPercent > best)
                best = s.SpeedPercent;
        return best;
    }

    public int SkillCount => _skills.Count;

    /// <summary>전체 스킬 (ID 오름차순).</summary>
    public IReadOnlyDictionary<uint, SkillTemplate> Skills => _skills;

    /// <summary>영웅을 선택하지 않은 입장에 쓰는 기본 영웅(ID가 가장 작은 영웅). 영웅이 하나도 없으면 null.</summary>
    public HeroTemplate? DefaultHero
    {
        get
        {
            foreach (HeroTemplate h in _heroes.Values)
                return h;
            return null;
        }
    }

    /// <summary>heroId로 영웅을 고른다. 0이면 기본 영웅, 없는 ID이면 null.</summary>
    public HeroTemplate? Resolve(uint heroId) => heroId == 0 ? DefaultHero : FindHero(heroId);

    /// <summary>전체 영웅 (ID 오름차순).</summary>
    public IReadOnlyDictionary<uint, HeroTemplate> Heroes => _heroes;

    public int HeroCount => _heroes.Count;

    /// <summary>영웅들 중 가장 빠른 이동 속도. 몬스터 쪽 사거리 검증의 상대 속도로 쓴다. 영웅이 없으면 0.</summary>
    public float GetMaxSpeed()
    {
        float maxSpeed = 0.0f;
        foreach (HeroTemplate h in _heroes.Values)
            if (h.Speed > maxSpeed)
                maxSpeed = h.Speed;
        return maxSpeed;
    }

    private static bool IsFinitePositive(float v) => float.IsFinite(v) && v > 0.0f;

    private static bool Fail(out string error, string format, uint id)
    {
        error = string.Format(format, id);
        return false;
    }

    /// <summary>
    /// 영웅 데이터의 값 범위를 검사한다. 첫 번째 오류만 설명으로 돌려준다.
    /// 검사 항목: 영웅 1개 이상, MaxHp >= 1, Attack/Defense >= 0, 사거리/속도 유한하고 > 0, 공격 간격 >= 1, 스킬/상태 효과 규칙.
    /// </summary>
    public bool Validate(out string error)
    {
        error = string.Empty;

        if (_heroes.Count == 0)
        {
            error = "at least one hero is required";
            return false;
        }

        foreach (HeroTemplate h in _heroes.Values)
        {
            uint id = h.Id;

            if (h.Stats.MaxHp < 1) return Fail(out error, "hero {0}: maxHp must be >= 1", id);
            if (h.Stats.Attack < 0) return Fail(out error, "hero {0}: attack must be >= 0", id);
            if (h.Stats.Defense < 0) return Fail(out error, "hero {0}: defense must be >= 0", id);
            if (!IsFinitePositive(h.Stats.AttackRange)) return Fail(out error, "hero {0}: attackRange must be > 0", id);
            if (h.Stats.AttackIntervalMs < 1) return Fail(out error, "hero {0}: attackIntervalMs must be >= 1", id);
            if (!IsFinitePositive(h.Speed)) return Fail(out error, "hero {0}: speed must be > 0", id);
            if (h.Stats.MaxMp < 0 || h.Stats.HpRegen < 0 || h.Stats.MpRegen < 0) return Fail(out error, "hero {0}: maxMp/hpRegen/mpRegen must be >= 0", id);

            foreach (uint skillId in h.Skills)
            {
                SkillTemplate? owned = FindSkill(skillId);
                if (owned != null && owned.MpCost > (uint)h.Stats.MaxMp)
                {
                    error = $"hero {id} skill {skillId}: mpCost {owned.MpCost} exceeds the hero's maxMp {h.Stats.MaxMp}";
                    return false;
                }
            }
        }

        foreach (SkillTemplate s in _skills.Values)
        {
            uint id = s.Id;

            bool shapeKnown = s.Shape == ESkillShape.Single || s.Shape == ESkillShape.Area || s.Shape == ESkillShape.Self
                || s.Shape == ESkillShape.Cone || s.Shape == ESkillShape.Line;
            if (!shapeKnown) return Fail(out error, "skill {0}: unknown shape", id);
            if (s.Target != ESkillTarget.Enemy && s.Target != ESkillTarget.Ally) return Fail(out error, "skill {0}: unknown target", id);
            if (s.HealPercent > SkillRules.MaxSkillDamagePercent || s.FlatHeal < 0) return Fail(out error, "skill {0}: heal out of range", id);
            if (SkillRules.SkillHasHeal(s) && s.Target != ESkillTarget.Ally) return Fail(out error, "skill {0}: heal needs target Ally", id);
            if (SkillRules.SkillHasDamage(s) && s.Target == ESkillTarget.Ally) return Fail(out error, "skill {0}: Ally skill cannot deal damage", id);
            if (s.DamagePercent > SkillRules.MaxSkillDamagePercent) return Fail(out error, "skill {0}: damagePercent is too large", id);
            if (s.FlatDamage < 0) return Fail(out error, "skill {0}: flatDamage must be >= 0", id);
            if (!SkillRules.SkillHasDamage(s) && !SkillRules.SkillHasHeal(s) && s.StatusId == 0) return Fail(out error, "skill {0}: needs damage, heal or statusId", id);
            if (s.CastTimeMs > SkillRules.MaxCastTimeMs) return Fail(out error, "skill {0}: castTimeMs is too large", id);
            if (!float.IsFinite(s.ProjectileSpeed) || s.ProjectileSpeed < 0.0f || s.ProjectileSpeed > SkillRules.MaxProjectileSpeed)
                return Fail(out error, "skill {0}: projectileSpeed must be 0..1000", id);
            if (s.StatusId != 0 && FindStatus(s.StatusId) == null) return Fail(out error, "skill {0}: unknown statusId", id);
            if (s.Shape == ESkillShape.Self)
            {
                if (s.Range != 0.0f) return Fail(out error, "skill {0}: Self skill must have range 0", id);
            }
            else if (!IsFinitePositive(s.Range)) return Fail(out error, "skill {0}: range must be > 0", id);
            if (s.Shape == ESkillShape.Cone)
            {
                if (!float.IsFinite(s.Angle) || s.Angle <= 0.0f || s.Angle > 360.0f) return Fail(out error, "skill {0}: Cone angle must be 0..360", id);
            }
            else if (s.Angle != 0.0f) return Fail(out error, "skill {0}: only Cone uses angle", id);
            if (s.ProjectileSpeed > 0.0f && s.Shape != ESkillShape.Single && s.Shape != ESkillShape.Area)
                return Fail(out error, "skill {0}: projectile needs Single or Area", id);
            if (s.CooldownMs < 1) return Fail(out error, "skill {0}: cooldownMs must be >= 1", id);
            if (s.ThreatBonusPercent < -100 || s.ThreatBonusPercent > 1000) return Fail(out error, "skill {0}: threatBonusPercent must be -100..1000", id);
            if (s.TauntMs > SkillRules.MaxTauntMs) return Fail(out error, "skill {0}: tauntMs is too large", id);
            if (!float.IsFinite(s.KnockbackDistance) || Math.Abs(s.KnockbackDistance) > GameConsts.MaxKnockbackDistance)
                return Fail(out error, "skill {0}: knockbackDistance must be within +-MaxKnockbackDistance", id);
            if (s.Target == ESkillTarget.Ally && (s.TauntMs != 0 || s.KnockbackDistance != 0.0f || s.ThreatBonusPercent != 0))
                return Fail(out error, "skill {0}: Ally skill cannot taunt, knock back or adjust threat", id);
            if (s.Shape == ESkillShape.Single)
            {
                if (s.Radius != 0.0f) return Fail(out error, "skill {0}: Single skill must have radius 0", id);
                if (s.MaxTargets != 1) return Fail(out error, "skill {0}: Single skill must have maxTargets 1", id);
            }
            else
            {
                if (s.Shape == ESkillShape.Cone)
                {
                    if (s.Radius != 0.0f) return Fail(out error, "skill {0}: Cone skill must have radius 0", id);
                }
                else if (!IsFinitePositive(s.Radius)) return Fail(out error, "skill {0}: Area/Self/Line skill needs radius > 0", id);
                if (s.MaxTargets < 1 || s.MaxTargets > SkillRules.MaxSkillTargets) return Fail(out error, "skill {0}: maxTargets must be 1..32", id);
            }

            // 비행 시간이 상한을 넘는 조합은 투사체가 도착하기 전에 시야 밖으로 사라지고 타이머가 오래 남는다.
            if (s.ProjectileSpeed > 0.0f && (s.Range + s.Radius) / s.ProjectileSpeed * 1000.0f > (float)SkillRules.MaxProjectileFlightMs)
                return Fail(out error, "skill {0}: projectile flight time exceeds the limit", id);
        }

        foreach (StatusTemplate t in _statuses.Values)
        {
            uint id = t.Id;

            bool kindKnown = t.Kind == EStatusKind.Dot || t.Kind == EStatusKind.Buff || t.Kind == EStatusKind.Stun
                || t.Kind == EStatusKind.Shield || t.Kind == EStatusKind.Hot || t.Kind == EStatusKind.Root;
            if (!kindKnown) return Fail(out error, "status {0}: unknown kind", id);
            if (t.HealPercent > SkillRules.MaxSkillDamagePercent || t.FlatHeal < 0) return Fail(out error, "status {0}: amount out of range", id);
            if (t.Kind != EStatusKind.Hot && t.Kind != EStatusKind.Shield && (t.HealPercent != 0 || t.FlatHeal != 0))
                return Fail(out error, "status {0}: only Hot/Shield use healPercent/flatHeal", id);
            if (t.Target != EStatusTarget.Hit && t.Target != EStatusTarget.Self) return Fail(out error, "status {0}: unknown target", id);
            if (t.DurationMs < 1 || t.DurationMs > SkillRules.MaxStatusDurationMs) return Fail(out error, "status {0}: durationMs must be 1..600000", id);
            if (t.Kind == EStatusKind.Dot)
            {
                if (t.TickMs < SkillRules.MinStatusTickMs || t.TickMs > t.DurationMs) return Fail(out error, "status {0}: Dot tickMs must be 100..durationMs", id);
                if (t.DamagePercent > SkillRules.MaxSkillDamagePercent || t.FlatDamage < 0) return Fail(out error, "status {0}: Dot damage out of range", id);
                if (t.DamagePercent == 0 && t.FlatDamage == 0) return Fail(out error, "status {0}: Dot needs damagePercent or flatDamage", id);
                if (t.AttackPercent != 0 || t.DefensePercent != 0 || t.SpeedPercent != 0) return Fail(out error, "status {0}: Dot must not have stat percents", id);
            }
            else if (t.Kind == EStatusKind.Hot)
            {
                if (t.TickMs < SkillRules.MinStatusTickMs || t.TickMs > t.DurationMs) return Fail(out error, "status {0}: Hot tickMs must be 100..durationMs", id);
                if (t.HealPercent == 0 && t.FlatHeal == 0) return Fail(out error, "status {0}: Hot needs healPercent or flatHeal", id);
                if (t.DamagePercent != 0 || t.FlatDamage != 0 || t.AttackPercent != 0 || t.DefensePercent != 0 || t.SpeedPercent != 0)
                    return Fail(out error, "status {0}: Hot must not have damage or stat percents", id);
            }
            else if (t.Kind == EStatusKind.Shield)
            {
                if (t.HealPercent == 0 && t.FlatHeal == 0) return Fail(out error, "status {0}: Shield needs healPercent or flatHeal", id);
                if (t.TickMs != 0 || t.DamagePercent != 0 || t.FlatDamage != 0 || t.AttackPercent != 0 || t.DefensePercent != 0 || t.SpeedPercent != 0)
                    return Fail(out error, "status {0}: Shield must not have tick, damage or stat percents", id);
            }
            else if (t.Kind == EStatusKind.Stun || t.Kind == EStatusKind.Root)
            {
                if (t.TickMs != 0 || t.DamagePercent != 0 || t.FlatDamage != 0 || t.AttackPercent != 0 || t.DefensePercent != 0 || t.SpeedPercent != 0)
                    return Fail(out error, "status {0}: Stun/Root must not have tick, damage or stat percents", id);
            }
            else
            {
                if (t.TickMs != 0 || t.DamagePercent != 0 || t.FlatDamage != 0) return Fail(out error, "status {0}: Buff must not have tick or damage", id);
                if (t.AttackPercent == 0 && t.DefensePercent == 0 && t.SpeedPercent == 0) return Fail(out error, "status {0}: Buff needs a stat percent", id);
                if (t.AttackPercent < SkillRules.MinStatPercent || t.AttackPercent > SkillRules.MaxStatPercent
                    || t.DefensePercent < SkillRules.MinStatPercent || t.DefensePercent > SkillRules.MaxStatPercent
                    || t.SpeedPercent < SkillRules.MinStatPercent || t.SpeedPercent > SkillRules.MaxSpeedBuffPercent)
                    return Fail(out error, "status {0}: stat percent out of range", id);
            }
        }

        return true;
    }

    /// <summary>내장 기본 영웅 하나만 가진 데이터를 만든다. 게임 데이터 폴더 없이 구동할 때(개발/테스트) 쓴다. 값은 CombatStats/HeroTemplate의 기본값이다.</summary>
    public static HeroData MakeDefault()
    {
        HeroData data = new HeroData();

        HeroTemplate hero = new HeroTemplate
        {
            Id = 1,
            Name = "Default",
        };
        data.AddHero(hero);

        return data;
    }
}
