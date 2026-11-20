// SkillData.cs : 스킬/상태 효과 템플릿 데이터 (서버 구동 시 로드되는 불변 데이터)
using System;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>
/// 스킬 한 종류의 데이터. 기획 데이터(엑셀)의 Skill 시트 한 행에 대응한다.
/// 시전 시간(CastTimeMs)이 0이면 즉시 발동하고, 양수면 시전을 마친 시점에 효과를 낸다.
/// 투사체(ProjectileSpeed)가 있으면 착탄 시점에 판정하고, StatusId가 있으면 적중(또는 시전)할 때 상태 효과를 건다.
/// 피해량 = max(minDamage, 공격력 * DamagePercent / 100 + FlatDamage - 방어력).
/// Area는 중심이 시전자로부터 Range 안이어야 하고, 중심에서 Radius 안의 공격 가능한 엔티티가 MaxTargets까지 맞는다.
/// Self는 시전자 위치가 중심이고 Radius 안이 효과 범위다(지정 좌표 무시, Range 0).
/// Cone은 시전자에서 지정 좌표 방향으로 Range 길이, Angle(도) 부채꼴. Line은 같은 방향으로 Range 길이, 좌우 Radius 폭의 직선이다.
/// Cone/Line의 좌표는 방향만 정하며 거리는 보지 않는다. 투사체는 Single/Area에서만 쓸 수 있다.
/// Target이 Ally면 공격할 수 없는 대상(시전자 본인 포함)에게 효과를 낸다. 치유량 = 공격력 * HealPercent / 100 + FlatHeal.
/// Enemy 스킬은 시전자를 맞히지 않고, Ally 스킬은 피해를 줄 수 없다. 사망한 대상은 어느 쪽이든 제외한다.
/// 시전 가능한 최대 거리는 Range + Radius이므로 AOI 보장 시야 안에 들어와야 한다(BattleZone.ValidateMonsters가 검사).
/// </summary>
public sealed class SkillTemplate
{
    public uint Id;                                 // 스킬 ID (유일, 0 불가)
    public string Name = string.Empty;              // 이름 (UTF-8, 로그/디버깅용)
    public ESkillShape Shape = ESkillShape.Single;
    public uint DamagePercent = 100;                // 공격력에 곱하는 비율(%)
    public int FlatDamage;                          // 더하는 고정 피해
    public float Range = 3.0f;                      // 시전 거리 (시전자 -> 대상/중심)
    public float Radius;                            // Area의 반경 (Single은 0)
    public uint CooldownMs = 1000;                  // 재사용 대기 시간
    public uint MaxTargets = 1;                     // 한 번에 맞는 최대 수 (Single은 1)
    public uint CastTimeMs;                         // 시전 시간. 0이면 즉시 발동
    public float ProjectileSpeed;                   // 투사체 속도(units/sec). 0이면 즉시 적중
    public uint StatusId;                           // 적중(또는 시전)할 때 거는 상태 효과 ID. 0이면 없음
    public ESkillTarget Target = ESkillTarget.Enemy;
    public uint HealPercent;                        // 치유: 시전자 공격력에 곱하는 비율(%)
    public int FlatHeal;                            // 치유: 더하는 고정 회복량
    public float Angle;                             // Cone의 전체 각도(도, 0 초과 360 이하). 다른 형태는 0
    public uint MpCost;                             // 시전 시작 때 드는 MP. 0이면 없음 (시전이 취소돼도 돌려받지 않는다)
    public int ThreatBonusPercent;                  // 이 스킬 피해로 쌓이는 위협의 보정(%). 0이면 피해량 그대로, 200이면 3배, -50이면 절반
    public uint TauntMs;                            // 0보다 크면 맞은 몬스터가 이 시간(ms) 동안 시전자를 우선 공격한다 (보스는 면역)
    public float KnockbackDistance;                 // 맞은 대상을 시전자(Area는 중심)에서 밀어내는 거리(units). 음수면 끌어당긴다. 0이면 없음 (보스는 면역)

    /// <summary>시전 시작 시점의 값을 고정해 두기 위한 사본을 만든다.</summary>
    public SkillTemplate Clone() => (SkillTemplate)MemberwiseClone();
}

/// <summary>
/// 상태 효과 데이터. 기획 데이터(엑셀)의 Status 시트 한 행에 대응한다.
/// 같은 ID의 효과가 이미 걸려 있으면 지속시간을 새로 시작하고 값을 갱신한다(중첩 없음).
/// Dot의 틱 피해는 건 순간의 시전자 공격력으로 정해진다: max(1, 공격력 * DamagePercent / 100 + FlatDamage).
/// Buff는 percent 합산으로 적용되며 엔티티당 합계는 MinStatPercent/MaxStatPercent로 묶는다.
/// Stun은 지속시간 동안 이동/기본 공격/스킬 시전을 막고, 걸리는 순간 이동과 시전을 중단시킨다(수치 필드 없음).
/// Root는 지속시간 동안 이동만 막는다. 걸리는 순간 이동을 멈추지만 시전은 끊지 않고, 기본 공격과 스킬은 쓸 수 있다. 서버 주도 이동(밀치기, 순간이동)은 막지 않는다.
/// Shield는 받는 피해를 amount만큼 대신 흡수하고 다 쓰면 사라진다. Hot은 TickMs마다 amount만큼 회복한다.
/// amount = 건 순간의 시전자 공격력 * HealPercent / 100 + FlatHeal (최소 1).
/// </summary>
public sealed class StatusTemplate
{
    public uint Id;                                 // 상태 효과 ID (유일, 0 불가)
    public string Name = string.Empty;
    public EStatusKind Kind = EStatusKind.Dot;
    public EStatusTarget Target = EStatusTarget.Hit;
    public uint DurationMs = 5000;                  // 지속시간
    public uint TickMs = 1000;                      // Dot/Hot의 간격 (Buff는 0)
    public uint DamagePercent;                      // Dot: 공격력에 곱하는 비율(%)
    public int FlatDamage;                          // Dot: 더하는 고정 피해
    public int AttackPercent;                       // Buff: 공격력 증감(%)
    public int DefensePercent;                      // Buff: 방어력 증감(%)
    public int SpeedPercent;                        // Buff: 이동속도 증감(%)
    public uint HealPercent;                        // Hot/Shield: 공격력에 곱하는 비율(%)
    public int FlatHeal;                            // Hot/Shield: 더하는 고정량
}

/// <summary>스킬/상태 효과 상한과 공식.</summary>
public static class SkillRules
{
    public const uint MaxSkillTargets = 32;                         // Area 한 번에 맞을 수 있는 수의 절대 상한
    public const uint MaxHeroSkills = GameConsts.MaxHeroSkills;     // 영웅 한 명이 가질 수 있는 스킬 수 상한 (Const 시트)
    public const int FixedSlots = 6;                                // 칸 1~6: 레벨이 되면 자동 지급되는 무료 스킬의 고정 칸
    public const int TotalSlots = 8;                                // 칸 7~8: 상점에서 산 스킬을 끼우는 칸
    public static readonly uint[] ShopSlotUnlockLevels = { 25, 35 };    // 칸 7, 8 이 열리는 레벨
    public static uint SlotUnlockLevel(int slot) => slot > FixedSlots && slot <= TotalSlots ? ShopSlotUnlockLevels[slot - FixedSlots - 1] : 1u;
    public const uint MaxSkillDamagePercent = 100000;
    public const uint MaxCastTimeMs = 10000;                        // 시전 시간 상한
    public const float MaxProjectileSpeed = 1000.0f;                // 투사체 속도 상한 (units/sec)
    public const uint MaxProjectileFlightMs = 5000;                 // 투사체 비행 시간 상한
    public const uint MaxStatusesPerEntity = GameConsts.MaxStatusesPerEntity;   // 엔티티 한 명에게 동시에 걸릴 수 있는 상태 효과 수 (Const 시트)
    public const uint MaxStatusDurationMs = 600000;                 // 상태 효과 지속시간 상한
    public const uint MinStatusTickMs = 100;                        // Dot 틱 간격 하한
    public const int MinStatPercent = -90;                          // 능력치 보정 합계 하한(%). 방어/이동이 음수로 뒤집히지 않게 한다
    public const int MaxStatPercent = 500;                          // 능력치 보정 합계 상한(%)
    public const uint MaxTauntMs = 30000;                           // 도발 지속시간 상한
    public const float PullStopDistance = 1.0f;                     // 끌어당기기가 시전 기준점에 이만큼 이상 가까이 끌어오지 않는다 (겹침 방지)
    public const int MaxSpeedBuffPercent = 100;                     // 이동속도 보정 상한(%). AOI 시야 검증이 이 값을 전제로 한다

    /// <summary>스킬 피해량 공식. 64비트로 계산해 오버플로를 막는다. 결과는 minDamage 이상, int.MaxValue 이하.</summary>
    public static int CalcSkillDamage(SkillTemplate skill, int attack, int defense, int minDamage)
    {
        long damage = (long)attack * skill.DamagePercent / 100 + skill.FlatDamage - defense;
        if (damage < minDamage)
            damage = minDamage;
        if (damage > int.MaxValue)
            damage = int.MaxValue;
        return (int)damage;
    }

    /// <summary>스킬이 직접 피해를 주는지. 피해가 없는 스킬은 상태 효과만 건다.</summary>
    public static bool SkillHasDamage(SkillTemplate skill) => skill.DamagePercent != 0 || skill.FlatDamage != 0;

    /// <summary>스킬이 치유하는지.</summary>
    public static bool SkillHasHeal(SkillTemplate skill) => skill.HealPercent != 0 || skill.FlatHeal != 0;

    /// <summary>스킬 치유량. 방어력과 무관하며 결과는 0 이상이다.</summary>
    public static int CalcSkillHeal(SkillTemplate skill, int attack)
    {
        long amount = (long)attack * skill.HealPercent / 100 + skill.FlatHeal;
        if (amount < 0)
            amount = 0;
        if (amount > int.MaxValue)
            amount = int.MaxValue;
        return (int)amount;
    }

    /// <summary>Hot 한 틱 회복량 / Shield 총 흡수량. 최소 1이다.</summary>
    public static int CalcStatusAmount(StatusTemplate status, int attack)
    {
        long amount = (long)attack * status.HealPercent / 100 + status.FlatHeal;
        if (amount < 1)
            amount = 1;
        if (amount > int.MaxValue)
            amount = int.MaxValue;
        return (int)amount;
    }

    /// <summary>Dot 한 틱의 피해량. 방어력은 무시하며 최소 1이다.</summary>
    public static int CalcDotDamage(StatusTemplate status, int attack)
    {
        long damage = (long)attack * status.DamagePercent / 100 + status.FlatDamage;
        if (damage < 1)
            damage = 1;
        if (damage > int.MaxValue)
            damage = int.MaxValue;
        return (int)damage;
    }

    /// <summary>능력치에 % 보정을 적용한다. 결과는 0 이상이다. (-90이면 10%만 남고 +100이면 두 배)</summary>
    public static int ApplyStatPercent(int baseValue, int percent)
    {
        long value = (long)baseValue * (100 + (long)percent) / 100;
        if (value < 0)
            value = 0;
        if (value > int.MaxValue)
            value = int.MaxValue;
        return (int)value;
    }
}
