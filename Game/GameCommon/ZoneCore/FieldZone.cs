// FieldZone.cs : FieldZone<TEntity> 타입 안전 필드 존.
using System;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>
/// 엔티티 타입을 지정할 수 있는 필드 존. FieldZoneBase 위에 타입 안전 인터페이스를 얹은 얇은 제네릭이다.
/// 이 존에는 TEntity(또는 그 파생형)만 등록할 수 있으므로 FindEntity/ForEachInAoi/ForEachInRadius와
/// 이벤트 훅(OnEntity*/OnMoveRejected)이 TEntity를 그대로 전달한다. 이동/AOI/틱 로직은 모두 FieldZoneBase에 있다.
/// 스레드 규칙은 FieldZoneBase와 같다. PostMove()를 제외한 모든 메서드는 존 스레드 전용이다.
/// </summary>
/// <typeparam name="TEntity">ZoneEntity에서 파생된 엔티티 타입</typeparam>
public abstract class FieldZone<TEntity> : FieldZoneBase where TEntity : ZoneEntity
{
    protected FieldZone(FieldZoneConfig config) : base(config) { }

    /// <summary>엔티티를 존에 등록하고 AOI에 편입한다. SetSpawnPos로 위치가 설정되어 있어야 한다.</summary>
    /// <returns>등록된 엔티티, 실패(ID 중복/영역 밖/무효 존) 시 null</returns>
    public TEntity? AddEntity(TEntity entity) => (TEntity?)AddEntityBase(entity);

    /// <summary>ID로 엔티티를 찾는다. 존 스레드에서만 호출해야 한다.</summary>
    public TEntity? FindEntity(ulong id) => (TEntity?)FindEntityBase(id);

    /// <summary>엔티티의 시야 안에 있는 다른 엔티티를 순회한다. 순회 중 추가/제거/이동 호출 금지.</summary>
    public void ForEachInAoi(TEntity entity, Action<TEntity> fn) => base.ForEachInAoi(entity, e => fn((TEntity)e));

    /// <summary>원형 범위 안의 엔티티를 순회한다.</summary>
    public void ForEachInRadius(Vec2 center, float radius, Action<TEntity> fn) => base.ForEachInRadius(center, radius, e => fn((TEntity)e));

    /// <summary>엔티티의 시야 안 AOI 관찰자만 순회한다.</summary>
    public void ForEachWatcherInAoi(TEntity entity, Action<TEntity> fn) => base.ForEachWatcherInAoi(entity, e => fn((TEntity)e));

    /// <summary>원형 범위 안의 AOI 관찰자만 순회한다.</summary>
    public void ForEachWatcherInRadius(Vec2 center, float radius, Action<TEntity> fn) => base.ForEachWatcherInRadius(center, radius, e => fn((TEntity)e));

    /// <summary>원형 범위 안의 살아 있고 무적이 아닌 플레이어만 순회한다.</summary>
    public void ForEachLivePlayerInRadius(Vec2 center, float radius, Action<TEntity> fn) => base.ForEachLivePlayerInRadius(center, radius, e => fn((TEntity)e));

    // ── 타입 안전 이벤트 훅 (FieldZoneBase 문서 참고) ────────
    protected virtual void OnEntityRemoved(TEntity entity) { }
    protected virtual void OnEntityMoveStarted(TEntity entity) { }
    protected virtual void OnEntityMoveStopped(TEntity entity, bool arrived) { }
    protected virtual void OnEntityTeleported(TEntity entity) { }
    protected virtual void OnMoveRejected(TEntity entity, MoveResult reason) { }
    protected virtual void OnEntityDamaged(TEntity? attacker, TEntity victim, int damage) { }
    protected virtual void OnEntityDied(TEntity victim, TEntity? killer) { }
    protected virtual void OnEntityRespawned(TEntity entity) { }
    protected virtual void OnEntityHealed(TEntity entity, int amount) { }
    protected virtual void OnEntityHealedBy(TEntity source, TEntity target, int healed) { }
    protected virtual void OnEntityStatChanged(TEntity entity) { }
    protected virtual void OnSkillHit(TEntity caster, SkillTemplate skill, TEntity victim) { }
    protected virtual void OnAttackRejected(TEntity attacker, ulong targetId, AttackResult reason) { }
    protected virtual void OnSkillCast(TEntity caster, SkillTemplate skill, ulong targetId, Vec2 center, uint hitCount) { }
    protected virtual void OnSkillRejected(TEntity caster, SkillTemplate skill, SkillResult reason) { }
    protected virtual void OnSkillCastStarted(TEntity caster, SkillTemplate skill, ulong targetId, Vec2 center) { }
    protected virtual void OnSkillCastCancelled(TEntity caster, SkillTemplate skill, SkillCancelReason reason) { }
    protected virtual void OnProjectileLaunched(TEntity caster, SkillTemplate skill, uint projectileId, ulong targetId, Vec2 from, Vec2 to, uint flightMs) { }
    protected virtual void OnProjectileImpact(TEntity caster, SkillTemplate skill, uint projectileId, Vec2 center, uint hitCount) { }
    protected virtual void OnStatusApplied(TEntity entity, StatusTemplate status, ulong casterId) { }
    protected virtual void OnStatusEnded(TEntity entity, uint statusId, StatusEndReason reason) { }

    protected sealed override void HandleEntityRemoved(ZoneEntity entity) => OnEntityRemoved((TEntity)entity);
    protected sealed override void HandleMoveStarted(ZoneEntity entity) => OnEntityMoveStarted((TEntity)entity);
    protected sealed override void HandleMoveStopped(ZoneEntity entity, bool arrived) => OnEntityMoveStopped((TEntity)entity, arrived);
    protected sealed override void HandleTeleported(ZoneEntity entity) => OnEntityTeleported((TEntity)entity);
    protected sealed override void HandleMoveRejected(ZoneEntity entity, MoveResult reason) => OnMoveRejected((TEntity)entity, reason);
    protected sealed override void HandleDamaged(ZoneEntity? attacker, ZoneEntity victim, int damage) => OnEntityDamaged((TEntity?)attacker, (TEntity)victim, damage);
    protected sealed override void HandleDied(ZoneEntity victim, ZoneEntity? killer) => OnEntityDied((TEntity)victim, (TEntity?)killer);
    protected sealed override void HandleRespawned(ZoneEntity entity) => OnEntityRespawned((TEntity)entity);
    protected sealed override void HandleHealed(ZoneEntity entity, int amount) => OnEntityHealed((TEntity)entity, amount);
    protected sealed override void HandleHealedBy(ZoneEntity source, ZoneEntity target, int healed) => OnEntityHealedBy((TEntity)source, (TEntity)target, healed);
    protected sealed override void HandleStatChanged(ZoneEntity entity) => OnEntityStatChanged((TEntity)entity);
    protected sealed override void HandleSkillHit(ZoneEntity caster, SkillTemplate skill, ZoneEntity victim) => OnSkillHit((TEntity)caster, skill, (TEntity)victim);
    protected sealed override void HandleAttackRejected(ZoneEntity attacker, ulong targetId, AttackResult reason) => OnAttackRejected((TEntity)attacker, targetId, reason);
    protected sealed override void HandleSkillCast(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 center, uint hitCount) => OnSkillCast((TEntity)caster, skill, targetId, center, hitCount);
    protected sealed override void HandleSkillRejected(ZoneEntity caster, SkillTemplate skill, SkillResult reason) => OnSkillRejected((TEntity)caster, skill, reason);
    protected sealed override void HandleSkillCastStarted(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 center) => OnSkillCastStarted((TEntity)caster, skill, targetId, center);
    protected sealed override void HandleSkillCastCancelled(ZoneEntity caster, SkillTemplate skill, SkillCancelReason reason) => OnSkillCastCancelled((TEntity)caster, skill, reason);
    protected sealed override void HandleProjectileLaunched(ZoneEntity caster, SkillTemplate skill, uint projectileId, ulong targetId, Vec2 from, Vec2 to, uint flightMs) => OnProjectileLaunched((TEntity)caster, skill, projectileId, targetId, from, to, flightMs);
    protected sealed override void HandleProjectileImpact(ZoneEntity caster, SkillTemplate skill, uint projectileId, Vec2 center, uint hitCount) => OnProjectileImpact((TEntity)caster, skill, projectileId, center, hitCount);
    protected sealed override void HandleStatusApplied(ZoneEntity entity, StatusTemplate status, ulong casterId) => OnStatusApplied((TEntity)entity, status, casterId);
    protected sealed override void HandleStatusEnded(ZoneEntity entity, uint statusId, StatusEndReason reason) => OnStatusEnded((TEntity)entity, statusId, reason);
}
