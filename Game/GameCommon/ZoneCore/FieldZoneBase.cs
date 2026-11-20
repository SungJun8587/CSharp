﻿// FieldZoneBase.cs : implementation of the FieldZoneBase class. (스킬/상태 효과 구현은 FieldZoneSkill.cs)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Core.Util;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>이동 요청 처리 결과.</summary>
public enum MoveResult : byte
{
    Ok,                 // 이동 시작(또는 목적지 갱신, 동일 목적지 재요청 포함)
    EntityNotFound,     // 존에 없는 엔티티
    Immobile,           // 이동 속도가 0 이하
    OutOfBounds,        // 목적지가 존 영역 밖
    PositionMismatch,   // 클라이언트 보고 위치가 서버 위치와 허용 오차 이상 차이
    Throttled,          // 요청 간격이 MinMoveIntervalMs보다 짧아 무시됨 (보정 통지 없음)
    Dead,               // 사망한 엔티티는 리스폰하기 전까지 이동할 수 없음
    Blocked,            // 장애물에 막혀 한 걸음도 갈 수 없음 (목적지까지 일부만 갈 수 있으면 막히기 직전까지로 줄여 Ok)
    Stunned,            // 기절 상태에서는 이동할 수 없음
    Rooted,             // 속박 상태에서는 이동할 수 없음
}

/// <summary>기본 공격 요청 처리 결과.</summary>
public enum AttackResult : byte
{
    Ok,                 // 공격 성공 (피해 적용됨)
    EntityNotFound,     // 존에 없는 공격자
    TargetNotFound,     // 존에 없는 대상 (퇴장했거나 다른 존, 존재하지 않는 ID)
    AttackerDead,       // 공격자가 사망 상태
    TargetDead,         // 대상이 이미 사망 상태
    TargetInvulnerable, // 대상이 무적 상태 (리스폰 직후 보호 등)
    InvalidTarget,      // 공격할 수 없는 대상 (자기 자신, PvP 금지, 같은 편 등)
    OutOfRange,         // 사거리 밖
    Cooldown,           // 공격 쿨다운 중 (거부 통지 없음)
    Throttled,          // 같은 틱에 이미 공격 요청을 접수함 (거부 통지 없음)
    NoLineOfSight,      // 사거리 안이지만 둘 사이를 장애물이 막고 있음
    Casting,            // 스킬 시전 중에는 기본 공격을 할 수 없음
    Stunned,            // 기절 상태에서는 기본 공격을 할 수 없음
}

/// <summary>스킬 시전 처리 결과.</summary>
public enum SkillResult : byte
{
    Ok,                 // 시전 성공 (피해 적용됨, 맞은 대상이 0이어도 시전은 성공)
    CasterDead,         // 시전자가 사망 상태
    Cooldown,           // 스킬 재사용 대기 중
    OutOfRange,         // 사거리 밖 (Single은 대상이 없는 경우 포함)
    NoLineOfSight,      // 사거리 안이지만 장애물이 가로막음
    InvalidTarget,      // 공격할 수 없는 대상 (사망, 자기 자신, PvP 금지, 무적 등)
    Throttled,          // 같은 틱에 이미 공격/스킬 요청을 접수함 (거부 통지 없음)
    Casting,            // 이미 다른 스킬을 시전 중
    Stunned,            // 기절 상태에서는 스킬을 쓸 수 없음
    NotEnoughMp,        // MP가 모자람
}

/// <summary>시전 시간이 있는 스킬이 시전 도중 취소된 사유.</summary>
public enum SkillCancelReason : byte
{
    Moved,              // 시전자가 이동했거나 순간이동함
    Died,               // 시전자가 사망함
    InvalidTarget,      // 완료 시점에 대상이 없어졌거나 사망/무적/공격 불가 상태가 됨
    OutOfRange,         // 완료 시점에 대상이 사거리 밖으로 벗어남
    NoLineOfSight,      // 완료 시점에 장애물이 가로막음
    Stunned,            // 시전 중 기절함
}

/// <summary>상태 효과가 끝난 사유.</summary>
public enum StatusEndReason : byte
{
    Expired,            // 지속시간이 다함
    Died,               // 걸려 있던 엔티티가 사망함
    Broken,             // 보호막을 다 써서 사라짐
}

/// <summary>
/// 필드 존 생성 설정값.
/// <list type="bullet">
/// <item>Zone : 틱 루프 설정</item>
/// <item>MinX~MaxY : 존 영역</item>
/// <item>CellSize : AOI 셀 크기</item>
/// <item>ViewCells : AOI 시야 반경(셀 단위). 최소 보장 시야 = (ViewCells - 2 * AoiHysteresis) * CellSize</item>
/// <item>AoiHysteresis : AOI 셀 변경 히스테리시스(셀 크기 대비 비율). 셀 경계 왕복 시 통지 폭주 방지</item>
/// <item>PosToleranceBase : 클라이언트 위치 보고 허용 오차 기본값</item>
/// <item>MaxLatencyMs : 허용 오차 계산에 반영할 최대 지연(ms). 허용 오차 = PosToleranceBase + speed * MaxLatencyMs / 1000</item>
/// <item>MinMoveIntervalMs : 엔티티별 이동 요청 최소 간격(존 논리 시간 기준). 이보다 짧은 요청은 무시한다. 요청 한 건이 주변 전원에게 브로드캐스트되므로 증폭 공격을 막는 장치다. 0이면 제한 없음</item>
/// <item>RespawnDelayMs : 플레이어 사망 후 자동 리스폰까지의 시간(ms). NoRespawn이면 자동 리스폰 안 함</item>
/// <item>AllowPvp : 플레이어끼리 공격 가능 여부</item>
/// <item>MinDamage : 최소 피해량(1 이상). 방어력이 공격력 이상이어도 이 값만큼은 들어간다</item>
/// <item>RespawnInvulnMs : 리스폰 직후 무적 시간(ms). 스폰 캠핑 방지. 본인이 공격하면 즉시 해제된다. 0이면 없음</item>
/// <item>BlockMap : 장애물 차단 맵(null이면 장애물 없음). 존 영역과 같은 범위로 구성해야 하며 존 생성 후 바꾸지 않는다</item>
/// </list>
/// </summary>
public sealed class FieldZoneConfig
{
    public ZoneConfig Zone = new ZoneConfig();      // 틱 루프 설정
    public float MinX;                              // 영역 최소 X
    public float MinY;                              // 영역 최소 Y
    public float MaxX = 1000.0f;                    // 영역 최대 X
    public float MaxY = 1000.0f;                    // 영역 최대 Y
    public float CellSize = 50.0f;                  // AOI 셀 크기
    public int ViewCells = 1;                       // AOI 시야 반경(셀)
    public float AoiHysteresis = 0.1f;              // AOI 셀 변경 히스테리시스
    public float PosToleranceBase = 1.0f;           // 위치 허용 오차 기본값
    public uint MaxLatencyMs = 300;                 // 허용 오차에 반영할 최대 지연(ms)
    public uint MinMoveIntervalMs = 50;             // 이동 요청 최소 간격(ms)
    public uint RespawnDelayMs = 5000;              // 플레이어 자동 리스폰 지연(ms)
    public bool AllowPvp = true;                    // 플레이어 간 공격 허용
    public int MinDamage = 1;                       // 최소 피해량
    public uint RespawnInvulnMs = 3000;             // 리스폰 직후 무적 시간(ms)
    public uint RegenIntervalMs = 1000;             // 체력/MP 자연 회복 주기(ms). 0이면 자연 회복을 하지 않는다
    public uint RegenCombatDelayMs = 5000;          // 피해를 주고받은 뒤 체력 자연 회복이 멈춰 있는 시간(ms). MP 회복은 영향받지 않는다
    public BlockMap? BlockMap;                      // 장애물 차단 맵 (읽기 전용 공유, null이면 없음)

    public FieldZoneConfig Clone()
    {
        FieldZoneConfig c = (FieldZoneConfig)MemberwiseClone();
        c.Zone = Zone.Clone();
        return c;
    }
}

/// <summary>
/// 필드 전투 존의 비제네릭 구현부. Zone의 틱 루프 위에 엔티티 관리, 서버 권위 이동, 그리드 AOI를 얹는다.
/// 엔티티를 ZoneEntity로 다루는 타입 소거 계층이다. 직접 상속하지 말고 타입 안전한 래퍼 FieldZone&lt;TEntity&gt;를 사용한다.
/// 제네릭을 얇게 유지해 구현은 한 번만 컴파일된다.
///
/// 이동 방식은 "이동 시작 + 목적지 + 속도"이다. 클라이언트는 자신의 현재 위치와 목적지만 보고하고,
/// 속도는 서버 스탯을 사용하며 위치 적분도 서버가 틱마다 수행한다.
/// 보고된 위치가 서버 위치와 허용 오차 이상 어긋나면 거부하고 보정 통지를 보낸다.
///
/// public 메서드는 PostMove()를 제외하고 모두 존 스레드에서만 호출해야 한다.
/// (외부 스레드는 Post()로 작업을 넘겨 그 안에서 호출한다.)
/// 파생 클래스는 Dispose에서 Stop()을 먼저 호출해, 틱 스레드가 살아 있는 동안 파생 멤버가 정리되지 않게 해야 한다.
/// </summary>
public abstract partial class FieldZoneBase : Zone
{
    /// <summary>자동 리스폰을 하지 않는 지연값.</summary>
    public const uint NoRespawn = 0xFFFFFFFFu;

    private readonly FieldZoneConfig _fieldConfig;                                  // 필드 존 설정
    private readonly bool _valid;                                                   // AOI 초기화 성공 여부
    private bool _updatingMovement;                                                 // UpdateMovement 실행 중 여부 (제거 재진입 방지)

    private readonly GridAOI<ZoneEntity> _aoi = new GridAOI<ZoneEntity>();          // 그리드 AOI
    private readonly Dictionary<ulong, ZoneEntity> _entities = new Dictionary<ulong, ZoneEntity>();     // 엔티티 테이블 (ID -> 엔티티)
    private readonly List<ZoneEntity> _moving = new List<ZoneEntity>(1024);         // 이동 중인 엔티티 목록 (매 틱 이 목록만 순회)
    private readonly List<ZoneEntity> _statusHolders = new List<ZoneEntity>(256);   // 상태 효과가 걸린 엔티티 목록 (매 틱 이 목록만 순회)
    private Vec2 _nearestCenter;                          // FindNearestLivePlayer 작업 공간 (존 스레드 전용, 재진입 없음)
    private float _nearestDistSq;
    private ZoneEntity? _nearestBest;
    private readonly Action<ZoneEntity> _nearestFn;
    private readonly Action<ZoneEntity> _collectFn;
    private readonly Action<ZoneEntity> _liveVisitFn;
    private Action<ZoneEntity>? _liveFn;
    private readonly List<ulong> _statusScratch = new List<ulong>();                // UpdateStatuses 작업 공간 (순회 중 목록이 바뀌어도 안전하게 ID로 돈다)
    private uint _nextProjectileId;                                                 // 투사체 ID 발급기 (0은 쓰지 않는다)
    private readonly List<ZoneEntity> _regenHolders = new List<ZoneEntity>(64);     // 자연 회복 대상 목록 (HpRegen/MpRegen이 있는 엔티티만. 매 회복 주기에 이 목록만 순회)
    private ulong _nextRegenMs;                                                     // 다음 자연 회복 시각 (0이면 첫 호출 때 정한다)
    private int _hitThreatPercent = 100;                                            // 지금 적용 중인 스킬 피해의 위협 배율(%). ApplyHits가 피해를 주는 동안만 100이 아니다

    /// <summary>필드 존을 생성하고 AOI 격자를 구성한다. 스레드는 Start() 호출 시 시작된다.</summary>
    protected FieldZoneBase(FieldZoneConfig config) : base(config.Zone)
    {
        _nearestFn = NearestVisit;
        _collectFn = CollectVisit;
        _liveVisitFn = LiveVisit;
        _fieldConfig = config.Clone();
        if (_fieldConfig.MinDamage < 1)
            _fieldConfig.MinDamage = 1;

        _valid = _aoi.Init(config.MinX, config.MinY, config.MaxX, config.MaxY, config.CellSize, config.ViewCells, config.AoiHysteresis);
        Debug.Assert(_valid, "FieldZoneBase: invalid AOI configuration");
    }

    /// <summary>AOI 초기화가 정상적으로 되었는지. Start() 전에 확인한다.</summary>
    public bool IsValid => _valid;

    /// <summary>필드 존 설정. 영역/AOI 값에 의존하는 시스템(몬스터 등)이 검증과 좌표 보정에 쓴다.</summary>
    public FieldZoneConfig FieldConfig => _fieldConfig;

    /// <summary>좌표가 존 영역 안인지. NaN은 false.</summary>
    public bool IsInBounds(Vec2 pos)
    {
        return pos.X >= _fieldConfig.MinX && pos.X <= _fieldConfig.MaxX
            && pos.Y >= _fieldConfig.MinY && pos.Y <= _fieldConfig.MaxY;
    }

    /// <summary>장애물 차단 맵. 없으면 null.</summary>
    public BlockMap? BlockMap => _fieldConfig.BlockMap;

    /// <summary>
    /// 장애물 차단 맵을 바꾼다(시험장 지형 치트용). 새 지형의 장애물 안에 놓인 엔티티는 가장 가까운 걸을 수 있는 자리로 옮기고
    /// (플레이어는 순간이동 훅으로 위치 보정이 나간다), 몬스터의 복귀 지점도 같은 규칙으로 옮긴다. 존 스레드에서만 호출해야 한다.
    /// 맵은 존 영역과 같은 범위로 만들어야 한다.
    /// </summary>
    /// <returns>옮긴 엔티티 수</returns>
    public int ReplaceBlockMap(BlockMap? map)
    {
        Debug.Assert(CanTouchZoneState());

        _fieldConfig.BlockMap = map;

        int moved = 0;
        foreach (ZoneEntity entity in _entities.Values)        // MoveTo 는 엔티티 테이블을 바꾸지 않으므로 순회 중에 호출해도 안전하다
        {
            if (entity is Monster monster && IsBlocked(monster._home) && TryFindWalkableNear(monster._home, out Vec2 home))
                monster._home = home;

            if (!IsBlocked(entity._pos))
                continue;
            if (TryFindWalkableNear(entity._pos, out Vec2 free) && MoveTo(entity, free, true))
                ++moved;
        }
        return moved;
    }

    /// <summary>기준점에서 가까운 순서로 고리 모양을 넓혀 가며, 영역 안이고 장애물이 아닌 첫 좌표를 찾는다.</summary>
    private bool TryFindWalkableNear(Vec2 origin, out Vec2 found)
    {
        float step = Math.Max(0.5f, _fieldConfig.BlockMap?.CellSize ?? 1.0f);
        const int MaxRings = 64;
        const int Samples = 16;
        for (int ring = 1; ring <= MaxRings; ++ring)
        {
            float r = ring * step;
            for (int i = 0; i < Samples; ++i)
            {
                double a = i * (2.0 * Math.PI / Samples);
                Vec2 p = new Vec2 { X = origin.X + (float)(Math.Cos(a) * r), Y = origin.Y + (float)(Math.Sin(a) * r) };
                if (IsWalkable(p))
                {
                    found = p;
                    return true;
                }
            }
        }
        found = origin;
        return false;
    }

    /// <summary>좌표가 장애물(차단 셀) 안인지. 차단 맵이 없으면 항상 false. 영역 검사는 하지 않는다.</summary>
    public bool IsBlocked(Vec2 pos) => _fieldConfig.BlockMap != null && _fieldConfig.BlockMap.IsBlocked(pos);

    /// <summary>엔티티를 놓을 수 있는 좌표인지. (영역 안이고 장애물이 아님) 스폰/순간이동/리스폰 위치 검증의 공용 기준이다.</summary>
    public bool IsWalkable(Vec2 pos) => IsInBounds(pos) && !IsBlocked(pos);

    /// <summary>좌표를 존 영역 안으로 보정한다. 장애물은 고려하지 않는다.</summary>
    public Vec2 ClampToBounds(Vec2 pos)
    {
        Vec2 p = pos;
        p.X = Math.Min(Math.Max(p.X, _fieldConfig.MinX), _fieldConfig.MaxX);
        p.Y = Math.Min(Math.Max(p.Y, _fieldConfig.MinY), _fieldConfig.MaxY);
        return p;
    }

    /// <summary>존에 등록된 엔티티 수.</summary>
    public int EntityCount => _entities.Count;

    // ── 엔티티 관리 (존 스레드 전용) ──────────────────────

    /// <summary>
    /// 엔티티를 존에 등록하고 AOI에 편입한다(시야 안 엔티티와 양방향 진입 통지). 존 스레드에서만 호출해야 한다.
    /// </summary>
    /// <returns>등록된 엔티티, 실패(ID 중복/영역 밖/무효 존) 시 null</returns>
    internal ZoneEntity? AddEntityBase(ZoneEntity entity)
    {
        Debug.Assert(CanTouchZoneState());

        if (!_valid || entity == null || !IsWalkable(entity._pos))
            return null;

        ulong id = entity.Id;
        if (_entities.ContainsKey(id))
            return null;

        entity._dest = entity._pos;
        entity._moving = false;
        entity._inZone = true;
        entity._moveGate.Reset();
        entity._attackGate.Reset();
        entity._invulnerableUntilMs = 0;
        entity._dead = false;
        // 다른 존에서 이어받은 체력/MP(RestoreVitals)가 있으면 그 값으로, 아니면 가득 채워 시작한다.
        entity._hp = entity._restoreHp >= 1 ? Math.Min(entity._restoreHp, entity._stats.MaxHp) : entity._stats.MaxHp;
        entity._nextAttackMs = NowMs;
        entity._respawnTimerId = 0;
        entity._mp = entity._restoreMp >= 0 ? Math.Min(entity._restoreMp, entity._stats.MaxMp) : entity._stats.MaxMp;
        entity._restoreHp = -1;
        entity._restoreMp = -1;
        entity._combatUntilMs = 0;

        _entities.Add(id, entity);
        _aoi.Add(entity);

        if (entity._stats.HpRegen > 0 || entity._stats.MpRegen > 0)
        {
            entity._regenIdx = _regenHolders.Count;
            _regenHolders.Add(entity);
        }
        return entity;
    }

    /// <summary>
    /// 엔티티를 존에서 제거한다. 이동 중이면 중단하고, AOI에서 양방향 이탈 통지를 보낸 뒤 해제한다.
    /// 엔티티를 빼기 직전에 HandleEntityRemoved()를 호출해, 엔티티를 가리키는 외부 목록을 정리할 수 있게 한다.
    /// 이동 갱신/AOI 콜백 안에서는 호출할 수 없다. 존 스레드에서만 호출해야 한다.
    /// </summary>
    public bool RemoveEntity(ulong id)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(!_updatingMovement, "FieldZoneBase.RemoveEntity called during movement update");

        if (!_entities.TryGetValue(id, out ZoneEntity? entity))
            return false;

        if (entity._moving)
            EndMoving(entity);

        if (entity._respawnTimerId != 0)
        {
            CancelTimer(entity._respawnTimerId);
            entity._respawnTimerId = 0;
        }

        DetachSkillState(entity);
        RemoveRegenHolder(entity);

        HandleEntityRemoved(entity);

        _aoi.Remove(entity);
        _entities.Remove(id);
        entity._inZone = false;
        return true;
    }

    /// <summary>ID로 엔티티를 찾는다. 존 스레드에서만 호출해야 한다. 없으면 null.</summary>
    public ZoneEntity? FindEntityBase(ulong id) => _entities.TryGetValue(id, out ZoneEntity? e) ? e : null;

    // ── 이동 (존 스레드 전용) ─────────────────────────────

    /// <summary>ID로 엔티티를 찾아 이동 요청을 처리한다. 자세한 규칙은 엔티티 버전을 참고한다.</summary>
    public MoveResult RequestMove(ulong id, Vec2 clientPos, Vec2 dest)
    {
        Debug.Assert(CanTouchZoneState());

        ZoneEntity? entity = FindEntityBase(id);
        if (entity == null)
            return MoveResult.EntityNotFound;

        return RequestMove(entity, clientPos, dest);
    }

    /// <summary>
    /// 클라이언트의 이동 요청을 검증하고 이동을 시작한다. (호출자가 엔티티를 이미 알고 있는 경우의 조회 생략 버전)
    /// - 최소 요청 간격(MinMoveIntervalMs)보다 짧은 요청은 보정 통지 없이 무시한다(브로드캐스트 증폭 차단).
    /// - 속도는 서버 스탯을 사용하므로 스피드핵은 의미가 없다.
    /// - 클라이언트 보고 위치가 서버 위치와 허용 오차(기본값 + 속도*지연) 이상 어긋나면 거부하고 HandleMoveRejected()로 보정 통지를 보낸다. (텔레포트 핵 차단)
    /// - 직선 경로가 장애물(차단 맵)에 막히면 막히기 직전 지점을 목적지로 삼는다. 한 걸음도 갈 수 없으면 거부하고 보정 통지를 보낸다.
    ///   길 찾기는 하지 않으므로 우회는 클라이언트가 경유 지점을 나눠 요청한다.
    /// - 이미 같은 목적지로 이동 중이면 아무것도 하지 않는다(중복 브로드캐스트 방지).
    /// 이미 이동 중이면 목적지만 갱신한다. 존 스레드에서만 호출해야 하며, entity는 이 존에 속해야 한다.
    /// </summary>
    public MoveResult RequestMove(ZoneEntity entity, Vec2 clientPos, Vec2 dest)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(entity._inZone);

        // 스로틀을 먼저 적용한다. 죽은 클라이언트가 이동 요청을 쏟아내도 보정 통지가 요청 수만큼 나가지 않게 한다.
        if (!entity._moveGate.TryPass(NowMs, _fieldConfig.MinMoveIntervalMs))
            return MoveResult.Throttled;

        if (entity._dead)
        {
            HandleMoveRejected(entity, MoveResult.Dead);
            return MoveResult.Dead;
        }

        if (IsStunned(entity))
        {
            HandleMoveRejected(entity, MoveResult.Stunned);
            return MoveResult.Stunned;
        }

        if (IsRooted(entity))
        {
            HandleMoveRejected(entity, MoveResult.Rooted);
            return MoveResult.Rooted;
        }

        if (!(entity._speed > 0.0f))        // NaN도 이동 불가로 본다
        {
            HandleMoveRejected(entity, MoveResult.Immobile);
            return MoveResult.Immobile;
        }

        if (!IsInBounds(dest))
        {
            HandleMoveRejected(entity, MoveResult.OutOfBounds);
            return MoveResult.OutOfBounds;
        }

        float tolerance = _fieldConfig.PosToleranceBase
            + entity._speed * (float)_fieldConfig.MaxLatencyMs * 0.001f;

        // NaN/Inf 좌표는 대소 비교가 항상 false가 되어 검증을 통과해 버리므로, "허용 오차 이내"를 긍정형으로 검사한다.
        if (!(Vec2.DistanceSq(clientPos, entity._pos) <= tolerance * tolerance))
        {
            HandleMoveRejected(entity, MoveResult.PositionMismatch);
            return MoveResult.PositionMismatch;
        }

        // 장애물이 있으면 목적지를 막히기 직전 지점으로 줄인다. 한 걸음도 못 가면 거부하고 서버 위치로 보정한다.
        if (!ResolveDest(entity._pos, dest, out Vec2 resolved))
        {
            HandleMoveRejected(entity, MoveResult.Blocked);
            return MoveResult.Blocked;
        }

        OrderMove(entity, resolved);
        return MoveResult.Ok;
    }

    /// <summary>
    /// 검증을 마친 이동 명령을 실행한다. 클라이언트 요청(RequestMove)과 서버 주도 이동(MoveEntity)이 공유한다.
    /// - 이미 목적지에 있으면 이동하지 않고 정지 처리한다.
    /// - 같은 목적지로 이미 이동 중이면 알릴 변화가 없으므로 아무것도 하지 않는다(중복 브로드캐스트 방지).
    /// - 그 외에는 목적지를 갱신하고 이동 시작을 통지한다.
    /// </summary>
    private void OrderMove(ZoneEntity entity, Vec2 dest)
    {
        if (Vec2.DistanceSq(dest, entity._pos) < 1e-4f)
        {
            StopMove(entity.Id);
            return;
        }

        if (entity._moving && Vec2.DistanceSq(dest, entity._dest) < 1e-4f)
            return;

        CancelCast(entity, SkillCancelReason.Moved);

        entity._dest = dest;

        if (!entity._moving)
            BeginMoving(entity);

        HandleMoveStarted(entity);
    }

    /// <summary>
    /// 서버가 엔티티를 목적지로 걸어가게 한다. (몬스터 AI, 스크립트 등 클라이언트 요청이 아닌 이동)
    /// 클라이언트 요청과 달리 위치 보고 검증과 요청 간격 제한이 없고, 호출자가 서버 코드이므로 보정 통지도 없다.
    /// 이동 갱신 중에는 호출할 수 없고 존 스레드에서만 호출해야 한다.
    /// 직선 경로가 장애물에 막히면 막히기 직전 지점까지만 간다(RequestMove와 같은 규칙).
    /// </summary>
    /// <returns>이동을 시작했거나 이미 그 목적지로 이동 중이면 true, 사망/이동 불가/영역 밖/한 걸음도 갈 수 없으면 false</returns>
    public bool MoveEntity(ZoneEntity entity, Vec2 dest)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(entity._inZone);
        Debug.Assert(!_updatingMovement, "FieldZoneBase.MoveEntity called during movement update");

        if (entity._dead || IsStunned(entity) || IsRooted(entity) || !(entity._speed > 0.0f) || !IsInBounds(dest))
            return false;

        if (!ResolveDest(entity._pos, dest, out Vec2 resolved))
            return false;

        OrderMove(entity, resolved);
        return true;
    }

    /// <summary>이동 중인 엔티티를 현재 서버 위치에서 정지시킨다. 존 스레드에서만 호출해야 한다.</summary>
    /// <returns>이동 중이어서 정지했으면 true</returns>
    public bool StopMove(ulong id)
    {
        Debug.Assert(CanTouchZoneState());

        ZoneEntity? entity = FindEntityBase(id);
        if (entity == null || !entity._moving)
            return false;

        entity._dest = entity._pos;
        EndMoving(entity);
        HandleMoveStopped(entity, false);
        return true;
    }

    /// <summary>
    /// 엔티티를 지정 위치로 즉시 이동시킨다. (스킬, 리스폰, 서버 보정 등 서버 주도 이동)
    /// 이동 중이면 중단하며, AOI는 셀 변경 규칙에 따라 갱신된다. 존 스레드에서만 호출해야 한다.
    /// </summary>
    /// <returns>성공 시 true, 엔티티가 없거나 영역 밖이거나 장애물 안이면 false</returns>
    public bool Teleport(ulong id, Vec2 pos)
    {
        Debug.Assert(CanTouchZoneState());

        ZoneEntity? entity = FindEntityBase(id);
        if (entity == null)
            return false;

        return MoveTo(entity, pos, true);
    }

    /// <summary>엔티티를 지정 위치로 즉시 옮기는 내부 구현. 이동 중이면 중단하고 AOI를 갱신한다.</summary>
    /// <param name="notify">true면 이동 중단/순간이동 훅을 호출한다. 리스폰은 별도 훅으로 알리므로 false를 쓴다</param>
    /// <returns>성공 시 true, 영역 밖이거나 장애물 안이면 false</returns>
    private bool MoveTo(ZoneEntity entity, Vec2 pos, bool notify)
    {
        Debug.Assert(!_updatingMovement, "FieldZoneBase.MoveTo called during movement update");

        if (!IsWalkable(pos))
            return false;

        CancelCast(entity, SkillCancelReason.Moved);

        bool wasMoving = entity._moving;
        if (wasMoving)
            EndMoving(entity);

        entity._pos = pos;
        entity._dest = pos;
        _aoi.Update(entity);

        if (notify)
        {
            if (wasMoving)
                HandleMoveStopped(entity, false);

            HandleTeleported(entity);
        }
        return true;
    }

    /// <summary>외부 스레드(네트워크 워커)에서 이동 요청을 존으로 넘긴다. 스레드 안전.</summary>
    /// <returns>입력 큐 적재 성공 시 true</returns>
    public bool PostMove(ulong id, Vec2 clientPos, Vec2 dest)
    {
        return Post(zone => ((FieldZoneBase)zone).RequestMove(id, clientPos, dest));
    }

    /// <summary>매 틱 호출된다. 이동 적분과 상태 효과 갱신을 수행하며, 이후 단계의 시뮬레이션을 이어서 호출한다.</summary>
    protected override void OnUpdate(uint dtMs)
    {
        UpdateMovement(dtMs);
        UpdateStatuses();
        UpdateRegen();
    }

    // ── 순회 (존 스레드 전용) ─────────────────────────────

    /// <summary>엔티티의 시야 안에 있는 다른 엔티티를 순회한다. 패킷 브로드캐스트용. 순회 중 엔티티 추가/제거/이동을 호출하면 안 된다.</summary>
    public void ForEachInAoi(ZoneEntity entity, Action<ZoneEntity> fn) => _aoi.ForEachNeighbor(entity, fn);

    /// <summary>원형 범위 안의 엔티티를 순회한다. 범위 스킬/어그로 탐색용.</summary>
    public void ForEachInRadius(Vec2 center, float radius, Action<ZoneEntity> fn) => _aoi.ForEachInRadius(center, radius, fn);

    /// <summary>
    /// 엔티티의 시야 안 AOI 관찰자(ZoneEntity.IsAoiWatcher)만 순회한다. 패킷 수신자 수집용.
    /// 시야 안의 그 외 엔티티(몬스터 등)를 훑지 않아 밀집한 구역에서 ForEachInAoi보다 빠르다.
    /// </summary>
    public void ForEachWatcherInAoi(ZoneEntity entity, Action<ZoneEntity> fn) => _aoi.ForEachWatcherNeighbor(entity, fn);

    /// <summary>원형 범위 안의 AOI 관찰자만 순회한다. 선공 대상(플레이어) 탐색용.</summary>
    public void ForEachWatcherInRadius(Vec2 center, float radius, Action<ZoneEntity> fn) => _aoi.ForEachWatcherInRadius(center, radius, fn);

    /// <summary>
    /// 원형 범위 안의 살아 있고 무적이 아닌 플레이어만 순회한다. 몬스터 선공 대상, 보스 행동 대상 후보 탐색용.
    /// 범위 판정은 AOI가 중심과의 실제 거리로 하므로 호출자가 거리를 다시 검사할 필요가 없다.
    /// </summary>
    public void ForEachLivePlayerInRadius(Vec2 center, float radius, Action<ZoneEntity> fn)
    {
        // 델리게이트를 필드로 넘겨 호출마다 클로저를 만들지 않는다. 콜백이 다시 이 메서드를 불러도 되도록 이전 값을 복원한다.
        Action<ZoneEntity>? prev = _liveFn;
        _liveFn = fn;
        try
        {
            _aoi.ForEachWatcherInRadius(center, radius, _liveVisitFn);
        }
        finally
        {
            _liveFn = prev;
        }
    }

    private void LiveVisit(ZoneEntity e)
    {
        if (e.Type == EntityType.Player && e.IsAlive && !IsInvulnerable(e))
            _liveFn!(e);
    }

    /// <summary>
    /// 원형 범위 안에서 center에 가장 가까운 살아 있고 무적이 아닌 플레이어를 찾는다. 없으면 null.
    /// 몬스터 선공 탐색처럼 매 틱 많이 불리는 경로용으로, 델리게이트를 미리 만들어 두고 작업 공간을 재사용해 할당이 없다.
    /// 방문 콜백이 이 메서드를 다시 부르는 재진입은 지원하지 않는다.
    /// </summary>
    public ZoneEntity? FindNearestLivePlayer(Vec2 center, float radius)
    {
        _nearestCenter = center;
        _nearestDistSq = float.PositiveInfinity;
        _nearestBest = null;
        _aoi.ForEachWatcherInRadius(center, radius, _nearestFn);
        ZoneEntity? best = _nearestBest;
        _nearestBest = null;
        return best;
    }

    private void NearestVisit(ZoneEntity e)
    {
        if (e.Type != EntityType.Player || !e.IsAlive || IsInvulnerable(e))
            return;
        float d = Vec2.DistanceSq(_nearestCenter, e.Pos);
        if (d < _nearestDistSq)
        {
            _nearestDistSq = d;
            _nearestBest = e;
        }
    }

    // ── 이벤트 훅의 타입 소거 버전 ────────────────────────
    // FieldZone<TEntity>가 TEntity로 변환해 On* 훅으로 전달한다.
    // 전투 이벤트 훅 호출 중에는 엔티티 추가/제거/이동 호출이 허용되지 않는다.
    // source/killer는 공격자가 없는 피해(DoT, 환경)이거나 이미 퇴장했으면 null이다.

    /// <summary>
    /// 엔티티가 존에서 제거되기 직전에 호출된다. 엔티티는 아직 AOI와 엔티티 테이블에 남아 있다.
    /// 재정의하여 엔티티를 가리키는 외부 목록(몬스터 AI 활성 목록 등)에서 뺀다.
    /// </summary>
    protected virtual void HandleEntityRemoved(ZoneEntity entity) { }

    /// <summary>이동이 시작(또는 목적지가 갱신)되었을 때 호출된다. 재정의하여 AOI 내 플레이어에게 이동 패킷을 브로드캐스트한다.</summary>
    protected virtual void HandleMoveStarted(ZoneEntity entity) { }

    /// <summary>이동이 끝났을 때 호출된다. 이동 갱신 중 호출될 수 있으므로 RemoveEntity/Teleport는 금지된다.</summary>
    /// <param name="arrived">목적지에 도착해 끝났으면 true, 중단(StopMove/순간이동)이면 false</param>
    protected virtual void HandleMoveStopped(ZoneEntity entity, bool arrived) { }

    /// <summary>순간이동이 완료되었을 때 호출된다. 재정의하여 위치 보정 패킷을 전송한다.</summary>
    protected virtual void HandleTeleported(ZoneEntity entity) { }

    /// <summary>이동 요청이 거부되었을 때 호출된다. (Throttled는 호출되지 않음) 재정의하여 해당 클라이언트에 서버 위치로 보정하라는 패킷을 전송한다.</summary>
    protected virtual void HandleMoveRejected(ZoneEntity entity, MoveResult reason) { }

    /// <summary>피해가 적용된 직후 호출된다. 피해자의 현재 체력은 victim.Hp이다. 치명타라면 이어서 HandleDied()가 호출된다.</summary>
    /// <param name="attacker">공격자 (공격자가 없는 피해면 null)</param>
    protected virtual void HandleDamaged(ZoneEntity? attacker, ZoneEntity victim, int damage) { }

    /// <summary>엔티티가 사망했을 때 호출된다. 이미 이동은 중단된 상태다.</summary>
    protected virtual void HandleDied(ZoneEntity victim, ZoneEntity? killer) { }

    /// <summary>엔티티가 부활하여 리스폰 위치에 배치된 직후 호출된다.</summary>
    protected virtual void HandleRespawned(ZoneEntity entity) { }

    /// <summary>체력이 회복된 직후 호출된다.</summary>
    protected virtual void HandleHealed(ZoneEntity entity, int amount) { }

    /// <summary>다른 엔티티가 준 치유가 실제로 체력을 회복시킨 직후 호출된다 (자연 회복이나 출처 없는 회복은 호출되지 않는다). 치유 위협 계산용.</summary>
    protected virtual void HandleHealedBy(ZoneEntity source, ZoneEntity target, int healed) { }

    /// <summary>MP나 체력 현황이 바뀌었을 때 호출된다 (스킬 MP 소모, 자연 회복, 부활). 재정의하여 본인에게 현황 패킷을 보낸다.</summary>
    protected virtual void HandleStatChanged(ZoneEntity entity) { }

    /// <summary>스킬이 엔티티에게 적중해 피해/치유/상태 효과를 적용한 직후 호출된다 (적용 후에도 살아 있는 대상만). 도발 등 몬스터 반응용.</summary>
    protected virtual void HandleSkillHit(ZoneEntity caster, SkillTemplate skill, ZoneEntity victim) { }

    /// <summary>공격 요청이 거부되었을 때 호출된다. (Cooldown/Throttled는 호출되지 않음)</summary>
    protected virtual void HandleAttackRejected(ZoneEntity attacker, ulong targetId, AttackResult reason) { }

    /// <summary>스킬 시전 성공 훅.</summary>
    protected virtual void HandleSkillCast(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 center, uint hitCount) { }

    /// <summary>스킬 시전 거부 훅.</summary>
    protected virtual void HandleSkillRejected(ZoneEntity caster, SkillTemplate skill, SkillResult reason) { }

    /// <summary>시전 시간이 있는 스킬의 시전 시작 훅.</summary>
    protected virtual void HandleSkillCastStarted(ZoneEntity caster, SkillTemplate skill, ulong targetId, Vec2 center) { }

    /// <summary>시전 취소 훅.</summary>
    protected virtual void HandleSkillCastCancelled(ZoneEntity caster, SkillTemplate skill, SkillCancelReason reason) { }

    /// <summary>투사체 발사 훅.</summary>
    protected virtual void HandleProjectileLaunched(ZoneEntity caster, SkillTemplate skill, uint projectileId, ulong targetId, Vec2 from, Vec2 to, uint flightMs) { }

    /// <summary>투사체 착탄 훅.</summary>
    protected virtual void HandleProjectileImpact(ZoneEntity caster, SkillTemplate skill, uint projectileId, Vec2 center, uint hitCount) { }

    /// <summary>상태 효과 적용/갱신 훅.</summary>
    protected virtual void HandleStatusApplied(ZoneEntity entity, StatusTemplate status, ulong casterId) { }

    /// <summary>상태 효과 해제 훅.</summary>
    protected virtual void HandleStatusEnded(ZoneEntity entity, uint statusId, StatusEndReason reason) { }

    /// <summary>
    /// 상태 효과 데이터 조회. 스킬이 거는 효과(SkillTemplate.StatusId)를 찾는다.
    /// 기본은 효과 없음(null)이며, 데이터를 가진 파생 존이 재정의한다.
    /// </summary>
    protected virtual StatusTemplate? FindStatusTemplate(uint statusId) => null;

    // ── 전투 정책. 파생 클래스가 재정의한다. ──────────────

    /// <summary>
    /// 공격 가능한 대상인지 판정하는 기본 정책. 자기 자신은 불가, 전투 참여 종류(플레이어/몬스터)끼리만 가능하며,
    /// 플레이어끼리는 AllowPvp일 때만, 몬스터끼리는 불가하다. 재정의하여 파티/길드 등을 추가한다.
    /// </summary>
    protected internal virtual bool CanAttack(ZoneEntity attacker, ZoneEntity target)
    {
        if (ReferenceEquals(attacker, target) || !attacker.IsCombatant || !target.IsCombatant)
            return false;

        bool attackerIsPlayer = attacker.Type == EntityType.Player;
        bool targetIsPlayer = target.Type == EntityType.Player;

        if (attackerIsPlayer && targetIsPlayer)
            return _fieldConfig.AllowPvp;

        return attackerIsPlayer || targetIsPlayer;
    }

    /// <summary>사망 후 자동 리스폰까지의 지연을 반환하는 기본 정책. 플레이어만 설정값으로 부활한다. 그 외는 NoRespawn(스폰 시스템이 별도로 관리).</summary>
    protected virtual uint GetRespawnDelayMs(ZoneEntity victim)
    {
        return victim.Type == EntityType.Player ? _fieldConfig.RespawnDelayMs : NoRespawn;
    }

    /// <summary>
    /// 기본 공격의 피해량 공식. max(minDamage, 공격력 - 방어력)이며 64비트로 계산해 오버플로를 막는다.
    /// 재정의하여 관통/치명타/속성 보정 등을 추가한다.
    /// </summary>
    protected virtual int CalcDamage(ZoneEntity attacker, ZoneEntity victim)
    {
        long damage = (long)attacker.Attack - (long)victim.Defense;
        if (damage < _fieldConfig.MinDamage)
            damage = _fieldConfig.MinDamage;
        if (damage > int.MaxValue)
            damage = int.MaxValue;

        return (int)damage;
    }

    /// <summary>부활 위치를 정하는 기본 정책. SetSpawnPos()로 정한 위치를 그대로 쓴다. 영역 밖 좌표를 돌려주면 제자리에서 부활한다.</summary>
    protected virtual Vec2 GetRespawnPos(ZoneEntity entity) => entity._spawnPos;

    /// <summary>부활 직후 무적 시간을 정하는 기본 정책. 플레이어만 설정값(RespawnInvulnMs)을 쓰고 나머지는 없다.</summary>
    protected virtual uint GetRespawnInvulnMs(ZoneEntity entity)
    {
        return entity.Type == EntityType.Player ? _fieldConfig.RespawnInvulnMs : 0;
    }

    // ── 전투 (존 스레드 전용) ─────────────────────────────

    /// <summary>ID로 공격자를 찾아 기본 공격을 처리한다. 자세한 규칙은 엔티티 버전을 참고한다.</summary>
    public AttackResult RequestAttack(ulong attackerId, ulong targetId)
    {
        Debug.Assert(CanTouchZoneState());

        ZoneEntity? attacker = FindEntityBase(attackerId);
        if (attacker == null)
            return AttackResult.EntityNotFound;

        return RequestAttack(attacker, targetId);
    }

    /// <summary>
    /// 클라이언트의 기본 공격 요청을 서버 권위로 검증하고 피해를 적용한다.
    /// 검증 순서(앞 단계가 싸고 클라이언트가 위조하기 쉬운 값부터 거른다):
    /// 1) 같은 틱의 중복 요청은 조용히 무시한다(Throttled) - 요청 폭주 방지
    /// 2) 공격자 생존 -> 기절 아님 -> 시전 중 아님 -> 3) 쿨다운 -> 4) 대상 조회 -> 5) 사거리 -> 6) 시선(장애물) -> 7) 대상 생존 -> 8) 공격 가능 대상(CanAttack) -> 9) 무적.
    /// 사거리 검사를 대상 상태 검사보다 앞에 둬서, 멀리 있는 엔티티의 존재/생사 여부가 응답으로 새지 않게 한다.
    /// (대상이 없는 경우와 사거리 밖은 클라이언트에 같은 사유로 통지된다.)
    /// 쿨다운은 틱 한 개만큼 여유를 둬서 틱 경계 지터로 정상 요청이 거절되지 않게 하며, 다음 공격 시각을
    /// max(다음 시각, 지금) + 간격으로 갱신해 여유분이 누적(드리프트)되지 않게 한다.
    /// 사거리는 이동 중인 양쪽의 지연 보정(속도*MaxLatencyMs)과 위치 허용 오차를 더해 판정한다.
    /// 피해량은 CalcDamage()가 정하고 적용은 DealDamage()가 맡는다.
    /// 거부된 요청(Cooldown/Throttled 제외)은 HandleAttackRejected()로 알리고, 쿨다운은 소모하지 않는다.
    /// 공격에 성공하면 공격자 본인의 무적은 해제된다(무적 상태로 일방 공격하는 것을 막는다).
    /// 존 스레드에서만 호출해야 하며, attacker는 이 존에 속해야 한다.
    /// </summary>
    public AttackResult RequestAttack(ZoneEntity attacker, ulong targetId)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(attacker._inZone);
        Debug.Assert(!_updatingMovement, "FieldZoneBase.RequestAttack called during movement update");

        ulong nowMs = NowMs;
        // 같은 틱에 이미 통과한 요청이 있는지만 본다. 검증을 모두 통과한 공격만 아래에서 기록한다(스킬 시전과 같은 규칙).
        if (!attacker._attackGate.CanPass(nowMs, 1))
            return AttackResult.Throttled;

        if (attacker._dead)
        {
            HandleAttackRejected(attacker, targetId, AttackResult.AttackerDead);
            return AttackResult.AttackerDead;
        }

        if (IsStunned(attacker))
        {
            HandleAttackRejected(attacker, targetId, AttackResult.Stunned);
            return AttackResult.Stunned;
        }

        if (attacker._cast != null)
        {
            HandleAttackRejected(attacker, targetId, AttackResult.Casting);
            return AttackResult.Casting;
        }

        if (nowMs + TickIntervalMs < attacker._nextAttackMs)
            return AttackResult.Cooldown;

        ZoneEntity? target = FindEntityBase(targetId);
        if (target == null)
        {
            HandleAttackRejected(attacker, targetId, AttackResult.TargetNotFound);
            return AttackResult.TargetNotFound;
        }

        float moveSpeed = 0.0f;
        if (attacker._moving)
            moveSpeed += attacker._speed;
        if (target._moving)
            moveSpeed += target._speed;

        float reach = attacker._stats.AttackRange + _fieldConfig.PosToleranceBase
            + moveSpeed * (float)_fieldConfig.MaxLatencyMs * 0.001f;

        // NaN 좌표가 들어와도 거부되도록 "사거리 이내"를 긍정형으로 검사한다.
        if (!(Vec2.DistanceSq(attacker._pos, target._pos) <= reach * reach))
        {
            HandleAttackRejected(attacker, targetId, AttackResult.OutOfRange);
            return AttackResult.OutOfRange;
        }

        // 사거리 안이어도 장애물 너머로는 공격할 수 없다. 사거리 검사 뒤에 두어 먼 대상에는 비용을 쓰지 않고, 존재 여부도 새지 않는다.
        if (_fieldConfig.BlockMap != null && !_fieldConfig.BlockMap.IsSegmentClear(attacker._pos, target._pos))
        {
            HandleAttackRejected(attacker, targetId, AttackResult.NoLineOfSight);
            return AttackResult.NoLineOfSight;
        }

        if (target._dead)
        {
            HandleAttackRejected(attacker, targetId, AttackResult.TargetDead);
            return AttackResult.TargetDead;
        }

        if (!CanAttack(attacker, target))
        {
            HandleAttackRejected(attacker, targetId, AttackResult.InvalidTarget);
            return AttackResult.InvalidTarget;
        }

        if (IsInvulnerable(target))
        {
            HandleAttackRejected(attacker, targetId, AttackResult.TargetInvulnerable);
            return AttackResult.TargetInvulnerable;
        }

        attacker._attackGate.TryPass(nowMs, 1);
        attacker._nextAttackMs = (attacker._nextAttackMs > nowMs ? attacker._nextAttackMs : nowMs) + attacker._stats.AttackIntervalMs;
        attacker._invulnerableUntilMs = 0;

        DealDamage(target, attacker, CalcDamage(attacker, target));
        return AttackResult.Ok;
    }

    /// <summary>
    /// 체력 차감, 피해 훅, 사망 처리를 하는 공용 피해 적용 경로. 기본 공격, 스킬, DoT, 환경 피해가 모두 이 함수를 지난다.
    /// 죽었거나 무적인 대상, 0 이하의 피해는 무시한다. 체력은 0 아래로 내려가지 않는다.
    /// 이동 갱신 중에는 호출할 수 없고 존 스레드에서만 호출해야 한다.
    /// </summary>
    /// <param name="victim">피해자 (이 존에 등록된 엔티티)</param>
    /// <param name="source">피해를 준 엔티티. 공격자가 없거나 이미 퇴장했으면 null (DoT는 틱마다 FindEntity로 다시 조회해 넘긴다)</param>
    /// <returns>피해를 적용했으면 true</returns>
    public bool DealDamage(ZoneEntity victim, ZoneEntity? source, int amount)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(victim._inZone);
        Debug.Assert(!_updatingMovement, "FieldZoneBase.DealDamage called during movement update");

        if (amount <= 0 || victim._dead || IsInvulnerable(victim))
            return false;

        MarkCombat(victim);
        if (source != null)
            MarkCombat(source);

        // 보호막이 먼저 피해를 받는다. 전부 흡수하면 체력은 그대로이고 피해 훅도 없다(보호막 해제 훅만 나간다).
        amount = AbsorbByShields(victim, amount);
        if (amount <= 0)
            return true;

        victim._hp = amount >= victim._hp ? 0 : victim._hp - amount;

        HandleDamaged(source, victim, amount);

        if (victim._hp == 0)
            Kill(victim, source);

        return true;
    }

    /// <summary>
    /// 체력을 회복한다. 최대 체력을 넘지 않으며, 죽은 엔티티나 이미 가득 찬 엔티티는 무시한다.
    /// 이동 갱신 중에도 호출할 수 있고(위치/이동 목록을 건드리지 않음) 존 스레드에서만 호출해야 한다.
    /// </summary>
    /// <returns>실제로 회복했으면 true</returns>
    public bool Heal(ZoneEntity entity, int amount) => Heal(entity, amount, null);

    /// <summary>
    /// 체력을 회복한다. source가 있고 대상이 자기 자신이 아니면 실제 회복량이 HandleHealedBy로 전달된다(치유 위협).
    /// 그 외 규칙은 Heal(entity, amount)와 같다.
    /// </summary>
    /// <param name="source">치유를 준 엔티티 (자연 회복, 몬스터 복귀 회복 등 출처가 없으면 null)</param>
    public bool Heal(ZoneEntity entity, int amount, ZoneEntity? source)
    {
        Debug.Assert(CanTouchZoneState());
        Debug.Assert(entity._inZone);

        if (amount <= 0 || entity._dead || entity._hp >= entity._stats.MaxHp)
            return false;

        long healed = Math.Min((long)amount, (long)entity._stats.MaxHp - entity._hp);
        entity._hp += (int)healed;

        HandleHealed(entity, (int)healed);

        if (source != null && !ReferenceEquals(source, entity) && !source._dead)
            HandleHealedBy(source, entity, (int)healed);
        return true;
    }

    /// <summary>엔티티가 속박 상태인지. 아직 정리되지 않은 만료된 효과는 세지 않는다.</summary>
    public bool IsRooted(ZoneEntity entity)
    {
        if (entity._statuses.Count == 0)
            return false;

        ulong nowMs = NowMs;
        for (int i = 0; i < entity._statuses.Count; ++i)
        {
            ActiveStatus s = entity._statuses[i];
            if (s.Kind == EStatusKind.Root && s.EndMs > nowMs)
                return true;
        }
        return false;
    }

    /// <summary>이 틱에 적용 중인 스킬 피해의 위협 배율(%). 피해 훅(HandleDamaged)이 위협을 쌓을 때 읽는다. 스킬 밖의 피해는 항상 100이다.</summary>
    internal int CurrentThreatPercent => _hitThreatPercent;

    /// <summary>엔티티를 자연 회복 목록에서 O(1)로 뺀다. (마지막 원소와 교체 후 pop)</summary>
    private void RemoveRegenHolder(ZoneEntity entity)
    {
        int idx = entity._regenIdx;
        if (idx == ZoneEntity.InvalidIndex)
            return;

        ZoneEntity last = _regenHolders[_regenHolders.Count - 1];
        _regenHolders[idx] = last;
        last._regenIdx = idx;
        _regenHolders.RemoveAt(_regenHolders.Count - 1);

        entity._regenIdx = ZoneEntity.InvalidIndex;
    }

    /// <summary>
    /// 자연 회복. RegenIntervalMs마다 회복 대상(HpRegen/MpRegen이 있는 엔티티)만 순회한다.
    /// 체력은 마지막 전투 후 RegenCombatDelayMs가 지나야 회복하고, MP는 전투와 무관하게 회복한다. 사망한 엔티티는 건너뛴다.
    /// 지연이 쌓여도 한 번에 한 주기만 처리한다(따라잡지 않는다).
    /// </summary>
    private void UpdateRegen()
    {
        uint interval = _fieldConfig.RegenIntervalMs;
        if (interval == 0 || _regenHolders.Count == 0)
            return;

        ulong nowMs = NowMs;
        if (_nextRegenMs == 0)
            _nextRegenMs = nowMs + interval;
        if (nowMs < _nextRegenMs)
            return;
        _nextRegenMs = nowMs + interval;

        for (int i = 0; i < _regenHolders.Count; ++i)
        {
            ZoneEntity e = _regenHolders[i];
            if (e._dead)
                continue;

            // 체력은 전투 중이거나 가득 차 있으면 쌓지 않는다(멈춘 동안의 회복이 한꺼번에 몰리지 않게).
            int hpGain = e._hp < e._stats.MaxHp && nowMs >= e._combatUntilMs ? RegenAmount(e._stats.HpRegen, interval, ref e._hpRegenRemain) : 0;
            if (hpGain > 0)
                Heal(e, hpGain);

            int mpGain = e._mp < e._stats.MaxMp ? RegenAmount(e._stats.MpRegen, interval, ref e._mpRegenRemain) : 0;
            if (mpGain > 0)
            {
                e._mp = (int)Math.Min((long)e._stats.MaxMp, (long)e._mp + mpGain);
                HandleStatChanged(e);
            }
        }
    }

    /// <summary>초당 회복량을 주기에 맞춰 환산한다. 정수로 못 낸 나머지는 remain(천분의 1 단위)에 두었다가 다음 주기에 더한다.</summary>
    private static int RegenAmount(int perSecond, uint intervalMs, ref int remain)
    {
        if (perSecond <= 0)
            return 0;
        long total = (long)perSecond * intervalMs + remain;
        remain = (int)(total % 1000);
        return (int)Math.Min(total / 1000, (long)int.MaxValue);
    }

    /// <summary>피해를 주고받은 엔티티를 전투 중으로 표시한다. 체력 자연 회복이 RegenCombatDelayMs 동안 멈춘다.</summary>
    private void MarkCombat(ZoneEntity entity) => entity._combatUntilMs = NowMs + _fieldConfig.RegenCombatDelayMs;

    /// <summary>엔티티가 무적 상태인지.</summary>
    public bool IsInvulnerable(ZoneEntity entity) => NowMs < entity._invulnerableUntilMs;

    /// <summary>엔티티가 기절 상태인지. 아직 정리되지 않은 만료된 효과는 세지 않는다.</summary>
    public bool IsStunned(ZoneEntity entity)
    {
        if (entity._statuses.Count == 0)
            return false;

        ulong nowMs = NowMs;
        for (int i = 0; i < entity._statuses.Count; ++i)
        {
            ActiveStatus s = entity._statuses[i];
            if (s.Kind == EStatusKind.Stun && s.EndMs > nowMs)
                return true;
        }
        return false;
    }

    /// <summary>보호막이 피해를 대신 받는다. 오래된 것부터 차례로 깎고, 다 쓴 보호막은 해제한다.</summary>
    /// <returns>흡수하고 남은 피해량 (0이면 전부 흡수)</returns>
    private int AbsorbByShields(ZoneEntity victim, int amount)
    {
        if (victim._statuses.Count == 0)
            return amount;

        ulong nowMs = NowMs;
        Span<uint> broken = stackalloc uint[(int)SkillRules.MaxStatusesPerEntity];
        int brokenCount = 0;

        for (int i = 0; i < victim._statuses.Count && amount > 0; ++i)
        {
            ActiveStatus s = victim._statuses[i];
            if (s.Kind != EStatusKind.Shield || s.EndMs <= nowMs || s.Amount <= 0)
                continue;

            int take = Math.Min(s.Amount, amount);
            s.Amount -= take;
            amount -= take;
            if (s.Amount == 0)
                broken[brokenCount++] = s.Id;
        }

        if (brokenCount == 0)
            return amount;

        for (int b = 0; b < brokenCount; ++b)
        {
            for (int i = 0; i < victim._statuses.Count; ++i)
            {
                if (victim._statuses[i].Id == broken[b])
                {
                    victim._statuses.RemoveAt(i);
                    break;
                }
            }
        }

        if (victim._statuses.Count == 0)
            RemoveStatusHolder(victim);

        for (int b = 0; b < brokenCount; ++b)
            HandleStatusEnded(victim, broken[b], StatusEndReason.Broken);

        return amount;
    }

    // ── 설정 검증 ─────────────────────────────────────────

    /// <summary>AOI가 모든 방향에서 보장하는 최소 시야 거리. (ViewCells - 2 * AoiHysteresis) * CellSize. AOI 설정이 유효하지 않은 존은 0이다.</summary>
    public float GetGuaranteedViewRange() => _valid ? _aoi.GuaranteedViewRadius : 0.0f;

    /// <summary>
    /// 기본 공격 사거리가 AOI 시야 안에 들어오는지 확인한다.
    /// RequestAttack()의 실제 판정 거리의 최대치(사거리 + 위치 허용 오차 + 양쪽이 모두 최고 속도로 이동 중일 때의 지연 보정 2 * 속도 * MaxLatencyMs)가
    /// 최소 보장 시야 이하여야 한다. 시야보다 긴 판정 거리는 보이지 않는 대상을 공격하게 하고 피해 통지가 시야 밖으로 나가므로 구동 시 거부해야 한다.
    /// </summary>
    /// <param name="maxMoveSpeed">이 사거리를 쓰는 엔티티와 그 대상이 가질 수 있는 최대 이동 속도 (units/sec)</param>
    public bool IsAttackRangeSupported(float attackRange, float maxMoveSpeed)
    {
        float worstReach = attackRange + _fieldConfig.PosToleranceBase
            + 2.0f * maxMoveSpeed * (float)_fieldConfig.MaxLatencyMs * 0.001f;

        return worstReach <= GetGuaranteedViewRange();
    }

    // ── 사망/부활 ─────────────────────────────────────────

    /// <summary>엔티티를 사망 처리한다. 이동 중이면 멈추고, 사망 훅 호출 후 자동 리스폰을 예약한다.</summary>
    private void Kill(ZoneEntity victim, ZoneEntity? killer)
    {
        victim._dead = true;
        victim._hp = 0;

        CancelCast(victim, SkillCancelReason.Died);
        ClearStatuses(victim, StatusEndReason.Died);

        if (victim._moving)
        {
            victim._dest = victim._pos;
            EndMoving(victim);
            HandleMoveStopped(victim, false);
        }

        HandleDied(victim, killer);
        ScheduleRespawn(victim);
    }

    /// <summary>
    /// 정책(GetRespawnDelayMs)에 따라 자동 리스폰 타이머를 예약한다.
    /// 타이머는 엔티티 제거 시 취소되므로, 같은 ID로 재입장한 엔티티를 잘못 되살리지 않는다.
    /// </summary>
    private void ScheduleRespawn(ZoneEntity victim)
    {
        uint delayMs = GetRespawnDelayMs(victim);
        if (delayMs == NoRespawn)
            return;

        ulong id = victim.Id;
        victim._respawnTimerId = ScheduleTimer(delayMs, zone =>
        {
            FieldZoneBase self = (FieldZoneBase)zone;
            ZoneEntity? entity = self.FindEntityBase(id);
            if (entity == null || !entity._dead)
                return;

            entity._respawnTimerId = 0;
            self.Respawn(id);
        });

        // 타이머를 걸 수 없으면 영구 사망 상태로 남지 않도록 즉시 부활시킨다.
        if (victim._respawnTimerId == 0)
        {
            LogManager.Error("[FieldZoneBase] respawn timer failed; respawning immediately. zoneId={0}, entityId={1}", ZoneId, id);
            Respawn(id);
        }
    }

    /// <summary>엔티티의 전투 능력치를 존 안에서 바꾼다(레벨 업 등). fill이면 살아 있는 엔티티의 체력/MP를 가득 채운다. 바뀐 뒤 능력치 변경 훅을 부른다.
    /// 존 스레드에서만 호출한다. 엔티티가 없으면 false.</summary>
    public bool ChangeStats(ulong id, CombatStats stats, bool fill)
    {
        Debug.Assert(CanTouchZoneState());

        ZoneEntity? entity = FindEntityBase(id);
        if (entity == null)
            return false;

        entity.ReplaceStats(stats, fill);
        HandleStatChanged(entity);
        return true;
    }

    /// <summary>
    /// 사망한 엔티티를 부활시킨다. 체력을 가득 채우고 리스폰 위치로 옮긴다.
    /// 예약된 자동 리스폰이 있으면 취소한다. 존 스레드에서만 호출해야 한다.
    /// </summary>
    /// <returns>부활했으면 true, 엔티티가 없거나 살아 있으면 false</returns>
    public bool Respawn(ulong id)
    {
        Debug.Assert(CanTouchZoneState());

        ZoneEntity? entity = FindEntityBase(id);
        if (entity == null || !entity._dead)
            return false;

        if (entity._respawnTimerId != 0)
        {
            CancelTimer(entity._respawnTimerId);
            entity._respawnTimerId = 0;
        }

        entity._dead = false;
        entity._hp = entity._stats.MaxHp;
        entity._nextAttackMs = NowMs;

        uint invulnMs = GetRespawnInvulnMs(entity);
        entity._invulnerableUntilMs = invulnMs != 0 ? NowMs + invulnMs : 0;

        // 리스폰 위치가 영역 밖이면(설정 오류) 제자리에서 부활한다.
        MoveTo(entity, GetRespawnPos(entity), false);

        entity._mp = entity._stats.MaxMp;

        HandleRespawned(entity);
        HandleStatChanged(entity);
        return true;
    }

    // ── 이동 적분 ─────────────────────────────────────────

    /// <summary>
    /// 이동 목적지를 장애물에 맞춰 보정한다. 직선 경로가 차단 셀을 지나면 막히기 직전 지점으로 줄인다.
    /// </summary>
    /// <param name="dest">요청 목적지 (영역 안임이 검증된 상태)</param>
    /// <param name="result">실제 이동할 목적지</param>
    /// <returns>이동할 수 있으면 true, 막혀서 거의 한 걸음도 갈 수 없으면 false</returns>
    private bool ResolveDest(Vec2 from, Vec2 dest, out Vec2 result)
    {
        result = dest;
        if (_fieldConfig.BlockMap == null)
            return true;

        result = _fieldConfig.BlockMap.ClampMove(from, dest, out bool hit);
        if (!hit)
            return true;

        // 줄어든 목적지가 현재 위치와 거의 같으면 벽에 붙어 있는 것이다. 미세한 이동 통지를 쏟아내지 않도록 거부한다.
        const float kMinBlockedMove = 0.05f;
        return Vec2.DistanceSq(result, from) >= kMinBlockedMove * kMinBlockedMove;
    }

    /// <summary>
    /// 이동 중인 엔티티만 순회하며 한 틱만큼 위치를 적분한다.
    /// 목적지까지 남은 거리가 한 틱 이동량 이하이면 목적지에 정확히 도착시켜 오버슈트를 막는다.
    /// 위치 갱신 후 AOI를 갱신하며, 셀이 바뀐 경우에만 진입/이탈 통지가 발생한다.
    /// </summary>
    private void UpdateMovement(uint dtMs)
    {
        float dtSec = (float)dtMs * 0.001f;

        // 훅에서 예외가 나가도 플래그가 켜진 채 남지 않게 한다(Zone이 예외를 격리하고 틱을 계속 돌린다).
        _updatingMovement = true;
        try
        {
            for (int i = 0; i < _moving.Count;)
            {
                ZoneEntity entity = _moving[i];

                // 이동 도중 속도가 0 이하(또는 NaN)가 되면(속박 등) 목적지에 영원히 도착하지 못하거나 뒤로 가므로 그 자리에 멈춘다.
                if (!(entity._speed > 0.0f))
                {
                    entity._dest = entity._pos;
                    EndMoving(entity);
                    HandleMoveStopped(entity, false);
                    continue;
                }

                float stepLen = entity._speed * dtSec;
                float dx = entity._dest.X - entity._pos.X;
                float dy = entity._dest.Y - entity._pos.Y;
                float distSq = dx * dx + dy * dy;

                bool arrived = false;
                if (distSq <= stepLen * stepLen)
                {
                    entity._pos = entity._dest;
                    arrived = true;
                }
                else
                {
                    float ratio = stepLen / MathF.Sqrt(distSq);
                    entity._pos.X += dx * ratio;
                    entity._pos.Y += dy * ratio;
                }

                _aoi.Update(entity);

                if (arrived)
                {
                    // swap-remove로 i번 자리에 마지막 원소가 들어오므로 i를 증가시키지 않는다.
                    EndMoving(entity);
                    HandleMoveStopped(entity, true);
                }
                else
                {
                    ++i;
                }
            }
        }
        finally
        {
            _updatingMovement = false;
        }
    }

    /// <summary>엔티티를 이동 목록에 등록한다.</summary>
    private void BeginMoving(ZoneEntity entity)
    {
        entity._moving = true;
        entity._movingIdx = _moving.Count;
        _moving.Add(entity);
    }

    /// <summary>엔티티를 이동 목록에서 O(1)로 제거한다. (마지막 원소와 교체 후 pop)</summary>
    private void EndMoving(ZoneEntity entity)
    {
        int idx = entity._movingIdx;

        ZoneEntity last = _moving[_moving.Count - 1];
        _moving[idx] = last;
        last._movingIdx = idx;
        _moving.RemoveAt(_moving.Count - 1);

        entity._moving = false;
        entity._movingIdx = ZoneEntity.InvalidIndex;
    }
}
