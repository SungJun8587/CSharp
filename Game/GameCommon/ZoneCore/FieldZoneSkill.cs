// FieldZoneSkill.cs : FieldZoneBase의 스킬/상태 효과 구현부.
//   스킬 시전(즉시/시전 시간), 투사체, 범위 형태(Single/Area/Self/Cone/Line), 치유, 상태 효과(Dot/Buff/Stun/Shield/Hot)를 다룬다.
//   전투 규칙 중 나머지(이동, 기본 공격, 피해, 사망)는 FieldZoneBase.cs에 있다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Core.Util;
using Game.Generated;

namespace Game.ZoneCore;

public abstract partial class FieldZoneBase
{
    private const float MinAimDistSq = 1e-4f;   // Cone/Line의 방향으로 삼을 수 있는 최소 거리(제곱). 시전자와 거의 같은 위치는 방향이 없다

    private struct SkillHit
    {
        public float DistSq;
        public ulong Id;
    }

    // 기준점에 가까운 순, 같으면 ID 순
    private static readonly Comparison<SkillHit> s_hitOrder =
        (a, b) => a.DistSq != b.DistSq ? a.DistSq.CompareTo(b.DistSq) : a.Id.CompareTo(b.Id);

    // CollectHits 방문 상태. 방문 콜백 안에서는 다시 CollectHits가 불리지 않으므로 필드로 둬도 안전하고, 매 시전 할당을 없앤다.
    private ZoneEntity? _collectCaster;
    private SkillTemplate? _collectSkill;
    private Vec2 _collectOrigin;
    private float _collectDirX, _collectDirY, _collectCosHalf;
    private BlockMap? _collectBlockMap;
    private List<SkillHit>? _collectHits;

    // 적중 목록 풀. 적용 중 훅에서 다른 스킬이 재진입해도 서로 다른 목록을 쓰도록 빌려 쓰고 돌려준다.
    private readonly Stack<List<SkillHit>> _hitPool = new Stack<List<SkillHit>>();

    private List<SkillHit> RentHits() => _hitPool.Count > 0 ? _hitPool.Pop() : new List<SkillHit>(16);

    private void ReturnHits(List<SkillHit> hits)
    {
        hits.Clear();
        _hitPool.Push(hits);
    }


    /// <summary>
    /// 스킬을 시전한다. 검증 순서는 RequestAttack과 같은 원칙(싸고 위조하기 쉬운 값부터)이다.
    /// 1) 같은 틱에 이미 통과한 공격/스킬 요청이 있으면 조용히 무시(Throttled, 기본 공격과 같은 게이트를 쓴다. 게이트는 검증을 모두 통과한 요청만 기록한다)
    /// 2) 시전자 생존 -> 기절 아님 -> 3) 이미 시전 중이 아님 -> 4) 재사용 대기
    /// 5) 사거리(Single은 대상 위치, Area는 지정한 중심. Self는 시전자 위치라 검사 없음. Cone/Line은 좌표가 방향일 뿐이라 영역 안인지만 보고 시전자와 같은 위치(방향 없음)는 InvalidTarget)
    /// -> 6) 시선(장애물, Single/Area) -> 7) Single은 대상이 유효한지(IsSkillTarget).
    /// 대상이 없는 경우와 사거리 밖은 같은 사유(엔티티 존재 여부 비노출).
    /// 거부된 요청(Throttled 제외)은 HandleSkillRejected로 알리고 재사용 대기를 소모하지 않는다.
    /// 통과하면 재사용 대기가 시작되고(시전 시간이 있어도 시작 시점부터) 시전자 본인의 무적이 해제된다.
    /// 시전 시간이 0이면 즉시 효과를 낸다. 시전 시간이 있으면 시전을 시작하고 이동을 멈추며, 끝나는 시점에 대상/사거리/시선을 다시 확인해 효과를 내거나 취소한다.
    /// 존 스레드에서만 호출해야 하며, caster는 이 존에 속해야 한다.
    /// </summary>
    /// <param name="targetId">Single의 대상 ID (Area는 무시)</param>
    /// <param name="center">Area의 중심 좌표 (Single은 무시)</param>
    /// <param name="readyAtMs">이 시전자의 이 스킬을 다시 쓸 수 있는 존 논리 시각. 성공하면 갱신된다</param>
    public SkillResult CastSkill(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 center, ref ulong readyAtMs)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(caster._inZone);
        Debug.Assert(!_updatingMovement, "FieldZoneBase.CastSkill called during movement update");

        ulong nowMs = NowMs;
        if (!caster._attackGate.CanPass(nowMs, 1))
            return SkillResult.Throttled;

        SkillResult Reject(SkillResult reason)
        {
            HandleSkillRejected(caster, skill, reason);
            return reason;
        }

        if (caster._dead)
            return Reject(SkillResult.CasterDead);

        if (IsStunned(caster))
            return Reject(SkillResult.Stunned);

        if (caster._cast != null)
            return Reject(SkillResult.Casting);

        if (nowMs + TickIntervalMs < readyAtMs)
            return Reject(SkillResult.Cooldown);

        if (skill.MpCost > 0 && (long)caster._mp < (long)skill.MpCost)
            return Reject(SkillResult.NotEnoughMp);

        bool single = skill.Shape == ESkillShape.Single;
        bool aimed = skill.Shape == ESkillShape.Cone || skill.Shape == ESkillShape.Line;
        ZoneEntity? target = null;
        Vec2 mid;
        float moveSpeed = caster._moving ? caster._speed : 0.0f;
        if (single)
        {
            target = FindEntityBase(targetId);
            if (target == null)
                return Reject(SkillResult.OutOfRange);
            mid = target._pos;
            if (target._moving)
                moveSpeed += target._speed;
        }
        else if (skill.Shape == ESkillShape.Self)
        {
            mid = caster._pos;
        }
        else
        {
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !IsInBounds(center))
                return Reject(SkillResult.OutOfRange);
            mid = center;
        }

        // Self는 시전자 위치, Cone/Line은 방향만 정하므로 사거리/시선 검사 대상이 아니다.
        if (single || skill.Shape == ESkillShape.Area)
        {
            float reach = skill.Range + _fieldConfig.PosToleranceBase + moveSpeed * (float)_fieldConfig.MaxLatencyMs * 0.001f;
            if (!(Vec2.DistanceSq(caster._pos, mid) <= reach * reach))
                return Reject(SkillResult.OutOfRange);

            if (_fieldConfig.BlockMap != null && !_fieldConfig.BlockMap.IsSegmentClear(caster._pos, mid))
                return Reject(SkillResult.NoLineOfSight);
        }
        else if (aimed && !(Vec2.DistanceSq(caster._pos, mid) >= MinAimDistSq))
        {
            return Reject(SkillResult.InvalidTarget);
        }

        if (single && !IsSkillTarget(caster, skill, target!))
            return Reject(SkillResult.InvalidTarget);

        caster._attackGate.TryPass(nowMs, 1);
        readyAtMs = (readyAtMs > nowMs ? readyAtMs : nowMs) + skill.CooldownMs;
        caster._invulnerableUntilMs = 0;

        if (skill.MpCost > 0)
        {
            caster._mp -= (int)Math.Min(skill.MpCost, (uint)caster._mp);
            HandleStatChanged(caster);
        }

        ulong effectTarget = single ? targetId : 0;
        if (skill.CastTimeMs > 0 && BeginCast(caster, skill, effectTarget, mid))
            return SkillResult.Ok;

        ExecuteSkill(caster, skill, effectTarget, mid);
        return SkillResult.Ok;
    }

    /// <summary>시전을 시작한다. 완료 타이머를 걸고 이동 중이면 멈춘다.</summary>
    /// <returns>시전을 시작했으면 true, 타이머를 걸 수 없으면 false (호출자가 즉시 발동으로 대신한다)</returns>
    private bool BeginCast(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 mid)
    {
        ulong casterId = caster.Id;
        ulong timerId = ScheduleTimer(skill.CastTimeMs, zone => ((FieldZoneBase)zone).FinishCast(casterId));
        if (timerId == 0)
        {
            LogManager.Error("[FieldZoneBase] cast timer failed; casting immediately. zoneId={0}, entityId={1}, skillId={2}", ZoneId, casterId, skill.Id);
            return false;
        }

        if (caster._moving)
            StopMove(casterId);

        SkillTemplate copy = skill.Clone();
        caster._cast = new CastState
        {
            Skill = copy,
            TargetId = targetId,
            Center = mid,
            EndMs = NowMs + skill.CastTimeMs,
            TimerId = timerId,
        };

        HandleSkillCastStarted(caster, copy, targetId, mid);
        return true;
    }

    /// <summary>
    /// 시전 완료 타이머의 처리. 시전 중 달라진 것이 없는지 다시 확인한 뒤 효과를 낸다.
    /// Single은 대상이 여전히 공격 가능하고 사거리/시선 안이어야 하며, 대상의 현재 위치가 효과 중심이 된다.
    /// Area는 시작할 때 정한 중심에서 터진다(시전자가 움직이면 이미 취소되었다).
    /// </summary>
    private void FinishCast(ulong casterId)
    {
        ZoneEntity? caster = FindEntityBase(casterId);
        if (caster == null || caster._cast == null)
            return;

        // 타이머는 이미 소진되었으므로 취소하지 않고 상태만 비운다.
        CastState cast = caster._cast;
        caster._cast = null;
        if (caster._dead)
            return;

        SkillTemplate skill = cast.Skill;
        Vec2 mid = cast.Center;

        if (skill.Shape == ESkillShape.Single)
        {
            ZoneEntity? target = FindEntityBase(cast.TargetId);
            if (target == null || !IsSkillTarget(caster, skill, target))
            {
                HandleSkillCastCancelled(caster, skill, SkillCancelReason.InvalidTarget);
                return;
            }

            mid = target._pos;
            float moveSpeed = target._moving ? target._speed : 0.0f;
            float reach = skill.Range + _fieldConfig.PosToleranceBase + moveSpeed * (float)_fieldConfig.MaxLatencyMs * 0.001f;
            if (!(Vec2.DistanceSq(caster._pos, mid) <= reach * reach))
            {
                HandleSkillCastCancelled(caster, skill, SkillCancelReason.OutOfRange);
                return;
            }

            if (_fieldConfig.BlockMap != null && !_fieldConfig.BlockMap.IsSegmentClear(caster._pos, mid))
            {
                HandleSkillCastCancelled(caster, skill, SkillCancelReason.NoLineOfSight);
                return;
            }
        }

        ExecuteSkill(caster, skill, skill.Shape == ESkillShape.Single ? cast.TargetId : 0, mid);
    }

    /// <summary>진행 중인 시전을 취소한다. 완료 타이머를 해제하고 취소 훅을 호출한다. 재사용 대기는 돌려주지 않는다. 존 스레드에서만 호출해야 한다.</summary>
    /// <returns>취소했으면 true, 시전 중이 아니면 false</returns>
    public bool CancelCast(ZoneEntity caster, SkillCancelReason reason)
    {
        if (caster._cast == null)
            return false;

        CastState cast = caster._cast;
        caster._cast = null;
        CancelTimer(cast.TimerId);

        HandleSkillCastCancelled(caster, cast.Skill, reason);
        return true;
    }

    /// <summary>검증을 마친 스킬의 효과를 낸다. 즉시 스킬은 바로 피해를 주고, 투사체 스킬은 발사만 한다. 시전 알림은 피해 알림보다 먼저 나간다.</summary>
    private void ExecuteSkill(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 mid)
    {
        if (skill.ProjectileSpeed > 0.0f)
        {
            LaunchProjectile(caster, skill, targetId, mid);
            return;
        }

        ZoneEntity? target = skill.Shape == ESkillShape.Single ? FindEntityBase(targetId) : null;
        List<SkillHit> hits = RentHits();
        try
        {
            CollectHits(caster, skill, target, mid, hits);

            HandleSkillCast(caster, skill, targetId, mid, (uint)hits.Count);

            ApplyHits(caster, skill, hits, caster.Attack, skill.Shape == ESkillShape.Area ? mid : caster._pos);
        }
        finally
        {
            ReturnHits(hits);
        }
        ApplySelfStatus(caster, skill);
    }

    /// <summary>
    /// 스킬이 이 엔티티에게 효과를 낼 수 있는지 확인한다. 사망한 엔티티는 항상 제외한다.
    /// Enemy: 시전자가 아니고 공격 가능(CanAttack)하며 무적이 아닌 엔티티.
    /// Ally : 시전자 본인이거나, 전투 가능한 엔티티 중 공격할 수 없는 쪽(같은 편). 무적이어도 치유/버프는 받을 수 있다.
    /// </summary>
    private bool IsSkillTarget(ZoneEntity caster, SkillTemplate skill, ZoneEntity e)
    {
        if (e._dead)
            return false;

        if (skill.Target == ESkillTarget.Ally)
            return ReferenceEquals(e, caster) || (caster.IsCombatant && e.IsCombatant && !CanAttack(caster, e));

        return !ReferenceEquals(e, caster) && CanAttack(caster, e) && !IsInvulnerable(e);
    }

    /// <summary>
    /// 맞는 대상을 모은다. 모양별 규칙:
    /// Single : 대상이 여전히 유효할 때만 하나.
    /// Area   : mid 중심 radius 원. 폭발은 중심에서 가려진 대상에게 닿지 않는다.
    /// Self   : 시전자 위치 중심 radius 원.
    /// Cone   : 시전자에서 mid 방향으로 range 이내, 방향과의 각도가 angle/2 이내.
    /// Line   : 시전자에서 mid 방향으로 길이 range, 좌우 radius 이내.
    /// Self/Cone/Line은 시전자 위치에서 벽에 가려진 대상을 뺀다. 결과는 기준점에 가까운 순(같으면 ID 순)으로 MaxTargets까지 남긴다.
    /// </summary>
    private void CollectHits(ZoneEntity caster, SkillTemplate skill, ZoneEntity? target, Vec2 mid, List<SkillHit> hits)
    {
        hits.Clear();
        if (skill.Shape == ESkillShape.Single)
        {
            if (target != null && IsSkillTarget(caster, skill, target))
                hits.Add(new SkillHit { DistSq = 0.0f, Id = target.Id });
            return;
        }

        bool fromCaster = skill.Shape != ESkillShape.Area;
        Vec2 origin = fromCaster ? caster._pos : mid;

        float queryRadius = skill.Radius;
        float dirX = 0.0f, dirY = 0.0f;
        float cosHalf = -1.0f;
        if (skill.Shape == ESkillShape.Cone || skill.Shape == ESkillShape.Line)
        {
            float ddx = mid.X - origin.X, ddy = mid.Y - origin.Y;
            float len = MathF.Sqrt(ddx * ddx + ddy * ddy);
            if (!(len * len >= MinAimDistSq))
                return;     // 방향이 없다
            dirX = ddx / len;
            dirY = ddy / len;
            if (skill.Shape == ESkillShape.Cone)
            {
                queryRadius = skill.Range;
                cosHalf = MathF.Cos(skill.Angle * 0.5f * MathF.PI / 180.0f);
            }
            else
            {
                queryRadius = MathF.Sqrt(skill.Range * skill.Range + skill.Radius * skill.Radius);
            }
        }

        _collectCaster = caster;
        _collectSkill = skill;
        _collectOrigin = origin;
        _collectDirX = dirX;
        _collectDirY = dirY;
        _collectCosHalf = cosHalf;
        _collectBlockMap = _fieldConfig.BlockMap;
        _collectHits = hits;
        try
        {
            _aoi.ForEachInRadius(origin, queryRadius, _collectFn);
        }
        finally
        {
            _collectCaster = null;
            _collectSkill = null;
            _collectBlockMap = null;
            _collectHits = null;
        }

        hits.Sort(s_hitOrder);
        if (hits.Count > skill.MaxTargets)
            hits.RemoveRange((int)skill.MaxTargets, hits.Count - (int)skill.MaxTargets);
    }

    /// <summary>CollectHits의 범위 방문 콜백. 상태는 _collect* 필드로 받는다.</summary>
    private void CollectVisit(ZoneEntity e)
    {
        ZoneEntity caster = _collectCaster!;
        SkillTemplate skill = _collectSkill!;
        Vec2 origin = _collectOrigin;

        if (!IsSkillTarget(caster, skill, e))
            return;

        float dx = e._pos.X - origin.X, dy = e._pos.Y - origin.Y;
        float distSq = dx * dx + dy * dy;
        if (skill.Shape == ESkillShape.Cone)
        {
            if (distSq > skill.Range * skill.Range)
                return;
            if (distSq > 1e-8f)
            {
                float along = dx * _collectDirX + dy * _collectDirY;
                if (!(along >= _collectCosHalf * MathF.Sqrt(distSq)))
                    return;
            }
        }
        else if (skill.Shape == ESkillShape.Line)
        {
            float along = dx * _collectDirX + dy * _collectDirY;
            float side = dx * _collectDirY - dy * _collectDirX;
            if (along < 0.0f || along > skill.Range || MathF.Abs(side) > skill.Radius)
                return;
        }

        if (_collectBlockMap != null && !_collectBlockMap.IsSegmentClear(origin, e._pos))
            return;     // 벽에 가려진 대상에게는 닿지 않는다

        _collectHits!.Add(new SkillHit { DistSq = distSq, Id = e.Id });
    }

    /// <summary>모은 대상에게 피해와 상태 효과를 적용한다. 앞선 피해의 훅에서 사망/퇴장한 대상은 건너뛴다.</summary>
    /// <param name="attack">피해 계산에 쓰는 시전자 공격력 (즉시 스킬은 현재 값, 투사체는 발사 시점 값)</param>
    /// <param name="origin">밀치기/끌어당기기의 기준점 (Area는 중심, 그 외는 시전자 위치)</param>
    private void ApplyHits(ZoneEntity caster, SkillTemplate skill, List<SkillHit> hits, int attack, Vec2 origin)
    {
        StatusTemplate? status = skill.StatusId != 0 ? FindStatusTemplate(skill.StatusId) : null;
        bool statusOnHit = status != null && status.Target == EStatusTarget.Hit;
        bool damaging = SkillRules.SkillHasDamage(skill);
        bool healing = SkillRules.SkillHasHeal(skill);
        int healAmount = healing ? SkillRules.CalcSkillHeal(skill, attack) : 0;

        for (int i = 0; i < hits.Count; ++i)
        {
            ZoneEntity? victim = FindEntityBase(hits[i].Id);
            if (victim == null || victim._dead)
                continue;

            if (damaging)
            {
                // 피해 훅(HandleDamaged)이 이 값으로 위협을 보정한다. 훅이 예외를 던져도 다음 피해에 남지 않게 복원한다.
                _hitThreatPercent = Math.Max(0, 100 + skill.ThreatBonusPercent);
                try
                {
                    DealDamage(victim, caster, SkillRules.CalcSkillDamage(skill, attack, victim.Defense, _fieldConfig.MinDamage));
                }
                finally
                {
                    _hitThreatPercent = 100;
                }
            }

            if (healing && !victim._dead)
                Heal(victim, healAmount, caster);

            if (statusOnHit && !victim._dead)
                ApplyStatus(victim, status!, caster);

            if (victim._dead)
                continue;

            if (skill.KnockbackDistance != 0.0f)
                Knockback(victim, origin, skill.KnockbackDistance);

            HandleSkillHit(caster, skill, victim);
        }
    }

    /// <summary>
    /// 대상을 기준점에서 밀어내거나(distance &gt; 0) 끌어당긴다(distance &lt; 0). 서버 주도 이동이라 기절/속박과 무관하게 적용되고,
    /// 이동과 시전을 끊으며 순간이동과 같은 통지가 나간다. 장애물에 막히면 막히기 직전까지만 간다.
    /// 끌어당기기는 기준점에서 PullStopDistance 안으로는 오지 않는다. 보스는 면역이다.
    /// 기준점과 겹쳐 방향이 없으면(밀치기) 움직이지 않는다.
    /// </summary>
    private void Knockback(ZoneEntity victim, Vec2 origin, float distance)
    {
        if (_updatingMovement || KnockbackImmune(victim))
            return;

        float dx = victim._pos.X - origin.X;
        float dy = victim._pos.Y - origin.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (!(dist > 1e-3f))
            return;

        float ux = dx / dist, uy = dy / dist;
        float move = distance;
        if (distance < 0.0f)
        {
            // 당기기: 기준점을 넘지 않고 PullStopDistance 앞에서 멈춘다.
            float room = dist - SkillRules.PullStopDistance;
            if (!(room > 0.0f))
                return;
            move = -Math.Min(-distance, room);
        }

        Vec2 want = new Vec2 { X = victim._pos.X + ux * move, Y = victim._pos.Y + uy * move };
        want = ClampToBounds(want);

        Vec2 dest = want;
        if (_fieldConfig.BlockMap != null)
            dest = _fieldConfig.BlockMap.ClampMove(victim._pos, want, out _);

        // 거의 못 움직였으면(벽에 붙어 있음) 통지를 쏟아내지 않는다.
        const float kMinMove = 0.05f;
        if (Vec2.DistanceSq(dest, victim._pos) < kMinMove * kMinMove)
            return;

        MoveTo(victim, dest, true);
    }

    /// <summary>밀치기/끌어당기기에 면역인 엔티티인지. 기본은 보스만 면역이며, 파생 존이 재정의해 규칙을 바꿀 수 있다.</summary>
    protected virtual bool KnockbackImmune(ZoneEntity entity) => entity is Monster { IsBoss: true };

    /// <summary>스킬이 시전자 본인에게 거는 상태 효과(Target == Self)를 적용한다.</summary>
    private void ApplySelfStatus(ZoneEntity caster, SkillTemplate skill)
    {
        if (skill.StatusId == 0 || caster._dead)
            return;

        StatusTemplate? status = FindStatusTemplate(skill.StatusId);
        if (status != null && status.Target == EStatusTarget.Self)
            ApplyStatus(caster, status, caster);
    }

    /// <summary>
    /// 투사체를 발사한다. 별도 엔티티 없이 착탄 시각에 타이머로 판정한다.
    /// 비행 시간은 거리/속도(틱 간격 이상, MaxProjectileFlightMs 이하). 시전 알림(적중 수 0)과 발사 알림이 나가고,
    /// 시전자 본인에게 거는 상태 효과는 발사 시점에 적용한다.
    /// </summary>
    private void LaunchProjectile(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 mid)
    {
        HandleSkillCast(caster, skill, targetId, mid, 0);
        ApplySelfStatus(caster, skill);

        Vec2 from = caster._pos;
        float dist = MathF.Sqrt(Vec2.DistanceSq(from, mid));
        double flight = Math.Ceiling((double)dist / (double)skill.ProjectileSpeed * 1000.0);
        flight = Math.Max(flight, (double)TickIntervalMs);
        flight = Math.Min(flight, (double)SkillRules.MaxProjectileFlightMs);
        uint flightMs = (uint)flight;

        if (++_nextProjectileId == 0)
            ++_nextProjectileId;
        uint projectileId = _nextProjectileId;

        HandleProjectileLaunched(caster, skill, projectileId, targetId, from, mid, flightMs);

        ulong casterId = caster.Id;
        int attack = caster.Attack;
        SkillTemplate copy = skill.Clone();
        ulong timerId = ScheduleTimer(flightMs, zone =>
            ((FieldZoneBase)zone).LandProjectile(casterId, copy, projectileId, targetId, from, mid, attack));

        // 타이머를 걸 수 없으면 허공에서 사라지지 않도록 바로 착탄시킨다.
        if (timerId == 0)
        {
            LogManager.Error("[FieldZoneBase] projectile timer failed; landing immediately. zoneId={0}, entityId={1}, skillId={2}", ZoneId, casterId, skill.Id);
            LandProjectile(casterId, copy, projectileId, targetId, from, mid, attack);
        }
    }

    /// <summary>
    /// 투사체 착탄 처리. 시전자가 존을 떠났으면 사라진다(알림 없음).
    /// Single은 대상을 쫓아가 착탄 시점의 위치에서 맞으며, 대상이 사라졌거나 무적/공격 불가이거나 발사 지점과의 사이에 장애물이 생겼으면 빗나간다(적중 0으로 착탄 알림).
    /// Area는 발사 때 정한 지점에서 터진다.
    /// </summary>
    /// <param name="attack">발사 시점의 시전자 공격력</param>
    private void LandProjectile(ulong casterId, SkillTemplate skill, uint projectileId, ulong targetId, Vec2 from, Vec2 mid, int attack)
    {
        ZoneEntity? caster = FindEntityBase(casterId);
        if (caster == null)
            return;

        ZoneEntity? target = null;
        Vec2 center = mid;
        if (skill.Shape == ESkillShape.Single)
        {
            target = FindEntityBase(targetId);
            if (target != null)
            {
                center = target._pos;
                if (_fieldConfig.BlockMap != null && !_fieldConfig.BlockMap.IsSegmentClear(from, center))
                    target = null;
            }
        }

        List<SkillHit> hits = RentHits();
        try
        {
            CollectHits(caster, skill, target, center, hits);

            HandleProjectileImpact(caster, skill, projectileId, center, (uint)hits.Count);

            // 투사체의 밀치기 기준점: Area는 터진 중심, Single은 착탄 시점의 시전자 위치
            ApplyHits(caster, skill, hits, attack, skill.Shape == ESkillShape.Area ? center : caster._pos);
        }
        finally
        {
            ReturnHits(hits);
        }
    }

    /// <summary>
    /// 상태 효과를 건다. 같은 ID가 이미 걸려 있으면 지속시간을 새로 시작하고 값을 갱신한다(중첩 없음).
    /// Dot의 틱 피해는 건 순간의 시전자 공격력으로 정해진다. 엔티티당 MaxStatusesPerEntity개까지이며 가득 차면 새 효과는 걸리지 않는다.
    /// 죽은 엔티티에는 걸리지 않는다. 존 스레드에서만 호출해야 한다.
    /// </summary>
    /// <param name="caster">건 엔티티 (없으면 null. Dot 피해는 공격력 0 기준으로 계산되고 피해의 공격자도 없다)</param>
    /// <returns>걸었거나 갱신했으면 true</returns>
    public bool ApplyStatus(ZoneEntity target, StatusTemplate status, ZoneEntity? caster)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(target._inZone);

        if (target._dead || status.Id == 0)
            return false;

        ActiveStatus? slot = null;
        for (int i = 0; i < target._statuses.Count; ++i)
        {
            if (target._statuses[i].Id == status.Id)
            {
                slot = target._statuses[i];
                break;
            }
        }

        if (slot == null)
        {
            if (target._statuses.Count >= SkillRules.MaxStatusesPerEntity)
                return false;

            if (target._statuses.Count == 0)
            {
                target._statusIdx = _statusHolders.Count;
                _statusHolders.Add(target);
            }
            slot = new ActiveStatus();
            target._statuses.Add(slot);
        }

        ulong nowMs = NowMs;
        slot.Id = status.Id;
        slot.Kind = status.Kind;
        slot.CasterId = caster != null ? caster.Id : 0;
        slot.EndMs = nowMs + status.DurationMs;
        slot.TickMs = status.TickMs;
        slot.NextTickMs = nowMs + status.TickMs;
        int casterAttack = caster != null ? caster.Attack : 0;
        switch (status.Kind)
        {
            case EStatusKind.Dot:
                slot.Amount = SkillRules.CalcDotDamage(status, casterAttack);
                break;
            case EStatusKind.Hot:
            case EStatusKind.Shield:
                slot.Amount = SkillRules.CalcStatusAmount(status, casterAttack);
                break;
            default:
                slot.Amount = 0;
                break;
        }
        slot.AttackPercent = status.AttackPercent;
        slot.DefensePercent = status.DefensePercent;
        slot.SpeedPercent = status.SpeedPercent;

        RecomputeStatMods(target);

        // 기절은 걸리는 순간 시전을 끊고 이동을 멈춘다.
        if (status.Kind == EStatusKind.Stun)
        {
            CancelCast(target, SkillCancelReason.Stunned);
            if (!_updatingMovement && target._moving)
                StopMove(target.Id);
        }

        // 속박은 걸리는 순간 이동만 멈춘다. 시전은 끊지 않는다.
        if (status.Kind == EStatusKind.Root && !_updatingMovement && target._moving)
            StopMove(target.Id);

        HandleStatusApplied(target, status, slot.CasterId);
        return true;
    }

    /// <summary>엔티티의 상태 효과를 모두 해제한다. 해제마다 훅을 호출하며, 능력치 보정도 원래대로 돌아온다.</summary>
    public void ClearStatuses(ZoneEntity entity, StatusEndReason reason)
    {
        if (entity._statuses.Count == 0)
            return;

        uint[] ids = new uint[entity._statuses.Count];
        for (int i = 0; i < ids.Length; ++i)
            ids[i] = entity._statuses[i].Id;

        entity._statuses.Clear();
        RemoveStatusHolder(entity);
        RecomputeStatMods(entity);

        for (int i = 0; i < ids.Length; ++i)
            HandleStatusEnded(entity, ids[i], reason);
    }

    /// <summary>엔티티를 상태 효과 보유 목록에서 O(1)로 뺀다. (마지막 원소와 교체 후 pop)</summary>
    private void RemoveStatusHolder(ZoneEntity entity)
    {
        int idx = entity._statusIdx;
        if (idx == ZoneEntity.InvalidIndex)
            return;

        ZoneEntity last = _statusHolders[_statusHolders.Count - 1];
        _statusHolders[idx] = last;
        last._statusIdx = idx;
        _statusHolders.RemoveAt(_statusHolders.Count - 1);

        entity._statusIdx = ZoneEntity.InvalidIndex;
    }

    /// <summary>
    /// 걸려 있는 Buff의 증감(%)을 합산해 공격력/방어력/이동속도 보정을 다시 계산한다.
    /// 합계는 MinStatPercent/MaxStatPercent로 묶고, 이동속도는 MaxSpeedBuffPercent로 묶는다.
    /// (AOI 시야 검증이 이 상한을 전제로 최고 속도를 계산한다.)
    /// </summary>
    private static void RecomputeStatMods(ZoneEntity entity)
    {
        int attack = 0, defense = 0, speed = 0;
        for (int i = 0; i < entity._statuses.Count; ++i)
        {
            ActiveStatus s = entity._statuses[i];
            if (s.Kind != EStatusKind.Buff)
                continue;
            attack += s.AttackPercent;
            defense += s.DefensePercent;
            speed += s.SpeedPercent;
        }

        entity._attackPct = Math.Min(Math.Max(attack, SkillRules.MinStatPercent), SkillRules.MaxStatPercent);
        entity._defensePct = Math.Min(Math.Max(defense, SkillRules.MinStatPercent), SkillRules.MaxStatPercent);
        entity._speedPct = Math.Min(Math.Max(speed, SkillRules.MinStatPercent), SkillRules.MaxSpeedBuffPercent);
        entity._speed = entity._baseSpeed * (float)(100 + entity._speedPct) * 0.01f;
    }

    /// <summary>존에서 제거되는 엔티티의 시전/상태 효과를 훅 없이 정리한다. 시전 타이머를 해제하고 보유 목록에서 뺀다.</summary>
    private void DetachSkillState(ZoneEntity entity)
    {
        if (entity._cast != null)
        {
            CancelTimer(entity._cast.TimerId);
            entity._cast = null;
        }

        RemoveStatusHolder(entity);
        entity._statuses.Clear();
    }

    /// <summary>
    /// 매 틱 상태 효과를 갱신한다. 보유 엔티티만 순회한다.
    /// Dot/Hot은 틱 시각이 된 만큼 처리하고(틱 간격이 지나치게 밀려도 한 번에 상태당 4틱까지만 처리하고 나머지는 다음 틱에 이어서 한다),
    /// 지속시간이 끝나면 마지막 틱까지 마친 뒤 해제한다. 만료 처리를 피해보다 먼저 해서, 피해 훅이 어떤 일을 하든 목록이 일관되게 유지된다.
    /// </summary>
    private void UpdateStatuses()
    {
        if (_statusHolders.Count == 0)
            return;

        const int MaxTicksPerStatus = 4;
        ulong nowMs = NowMs;

        _statusScratch.Clear();
        for (int i = 0; i < _statusHolders.Count; ++i)
            _statusScratch.Add(_statusHolders[i].Id);

        Span<(int Amount, ulong CasterId, bool Heal)> ticks = stackalloc (int, ulong, bool)[(int)SkillRules.MaxStatusesPerEntity * MaxTicksPerStatus];
        Span<uint> ended = stackalloc uint[(int)SkillRules.MaxStatusesPerEntity];

        for (int idx = 0; idx < _statusScratch.Count; ++idx)
        {
            ZoneEntity? entity = FindEntityBase(_statusScratch[idx]);
            if (entity == null || entity._statuses.Count == 0)
                continue;

            int tickCount = 0;
            for (int i = 0; i < entity._statuses.Count; ++i)
            {
                ActiveStatus s = entity._statuses[i];
                if ((s.Kind != EStatusKind.Dot && s.Kind != EStatusKind.Hot) || s.TickMs == 0)
                    continue;
                for (int n = 0; n < MaxTicksPerStatus && s.NextTickMs <= nowMs && s.NextTickMs <= s.EndMs; ++n)
                {
                    ticks[tickCount++] = (s.Amount, s.CasterId, s.Kind == EStatusKind.Hot);
                    s.NextTickMs += s.TickMs;
                }
            }

            int endedCount = 0;
            for (int i = 0; i < entity._statuses.Count;)
            {
                ActiveStatus s = entity._statuses[i];
                bool ticksLeft = (s.Kind == EStatusKind.Dot || s.Kind == EStatusKind.Hot) && s.TickMs != 0 && s.NextTickMs <= s.EndMs;
                if (s.EndMs <= nowMs && !ticksLeft)
                {
                    ended[endedCount++] = s.Id;
                    entity._statuses.RemoveAt(i);
                }
                else
                {
                    ++i;
                }
            }

            if (endedCount != 0)
            {
                if (entity._statuses.Count == 0)
                    RemoveStatusHolder(entity);
                RecomputeStatMods(entity);
            }

            for (int i = 0; i < endedCount; ++i)
                HandleStatusEnded(entity, ended[i], StatusEndReason.Expired);

            for (int i = 0; i < tickCount && !entity._dead; ++i)
            {
                if (ticks[i].Heal)
                    Heal(entity, ticks[i].Amount, FindEntityBase(ticks[i].CasterId));
                else
                    DealDamage(entity, FindEntityBase(ticks[i].CasterId), ticks[i].Amount);
            }
        }
    }
}
