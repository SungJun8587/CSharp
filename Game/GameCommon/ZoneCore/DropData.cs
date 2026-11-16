// DropData.cs : 몬스터 드롭 테이블 데이터와 지면 아이템 엔티티 (서버 구동 시 로드되는 불변 데이터)
using System;
using System.Collections.Generic;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>지면 아이템 엔티티 ID 대역. 최상위 두 비트가 켜진 ID는 DropSystem이 발급하며, 몬스터 대역(최상위 비트)에 속하므로 플레이어(캐릭터 ID)는 쓸 수 없다.</summary>
public static class ItemIds
{
    public const ulong Base = MonsterIds.Base | 0x4000000000000000UL;
}

/// <summary>
/// 몬스터 한 종류의 드롭 항목 하나. 몬스터가 죽을 때 항목마다 독립적으로 ChancePermille(1000분율)로 굴려 성공하면
/// MinCount..MaxCount 사이 수량이 떨어진다.
/// </summary>
public sealed class DropEntry
{
    public uint ItemId;             // 아이템 종류 (0 불가)
    public uint ChancePermille;     // 드롭 확률 (1..1000)
    public uint MinCount = 1;       // 최소 수량
    public uint MaxCount = 1;       // 최대 수량 (MinCount 이상, MaxItemStack 이하)
}

/// <summary>몬스터 종류별 드롭 테이블. 기획 데이터(엑셀)의 Drop 시트에 대응하며, 로드 후에는 읽기 전용으로 공유한다.</summary>
public sealed class DropData
{
    private readonly Dictionary<uint, List<DropEntry>> _byMonster = new Dictionary<uint, List<DropEntry>>();

    /// <summary>항목 총 수.</summary>
    public int EntryCount { get; private set; }

    /// <summary>드롭 항목을 추가한다. 값 범위는 Validate가 검사한다.</summary>
    public void Add(uint monsterId, DropEntry entry)
    {
        if (!_byMonster.TryGetValue(monsterId, out List<DropEntry>? list))
        {
            list = new List<DropEntry>(2);
            _byMonster.Add(monsterId, list);
        }
        list.Add(entry);
        ++EntryCount;
    }

    /// <summary>몬스터 종류의 드롭 항목. 없으면 null.</summary>
    public IReadOnlyList<DropEntry>? Find(uint monsterId) => _byMonster.TryGetValue(monsterId, out List<DropEntry>? l) ? l : null;

    /// <summary>값 범위와 몬스터 참조를 검사한다. 존재하지 않는 몬스터를 가리키는 항목은 오류다.</summary>
    public bool Validate(MonsterData? monsters, out string error)
    {
        foreach (KeyValuePair<uint, List<DropEntry>> pair in _byMonster)
        {
            if (monsters == null || monsters.FindMonster(pair.Key) == null)
            {
                error = $"drop: unknown monster id {pair.Key}";
                return false;
            }

            foreach (DropEntry e in pair.Value)
            {
                if (e.ItemId == 0)
                {
                    error = $"drop monster {pair.Key}: itemId must be >= 1";
                    return false;
                }
                if (e.ChancePermille < 1 || e.ChancePermille > 1000)
                {
                    error = $"drop monster {pair.Key} item {e.ItemId}: chancePermille must be 1..1000";
                    return false;
                }
                if (e.MinCount < 1 || e.MaxCount < e.MinCount || e.MaxCount > GameConsts.MaxItemStack)
                {
                    error = $"drop monster {pair.Key} item {e.ItemId}: counts must satisfy 1 <= minCount <= maxCount <= {GameConsts.MaxItemStack}";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }
}

/// <summary>
/// 바닥에 떨어진 아이템 하나. 존에 엔티티로 등록되어 AOI로 주변 플레이어에게 보인다.
/// 체력(MaxHp)이 수량을 겸한다: 클라이언트로 가는 스폰 패킷의 hp/maxHp가 곧 수량이다. 공격 대상이 아니다(전투 참여 종류가 아님).
/// 소유권: 드롭 직후 OwnerUntilMs까지는 OwnerId(0이면 모두)만 주울 수 있다. 수명이 다하면 사라진다.
/// </summary>
public sealed class GroundItem : ZoneEntity
{
    internal ulong _expireTimerId;      // 소멸 타이머 ID (0이면 없음)

    public GroundItem(ulong id, uint itemId, uint count, ulong ownerId, ulong ownerUntilMs)
        : base(id, EntityType.Item)
    {
        ItemId = itemId;
        Count = count;
        OwnerId = ownerId;
        OwnerUntilMs = ownerUntilMs;
        CombatStats stats = CombatStats.Default;
        stats.MaxHp = (int)Math.Min(Math.Max(count, 1u), (uint)int.MaxValue);
        SetCombatStats(stats);
    }

    public uint ItemId { get; }
    public uint Count { get; }
    public ulong OwnerId { get; }
    public ulong OwnerUntilMs { get; }

    /// <summary>다른 엔티티의 등장/퇴장에 반응할 필요가 없으므로 관찰자가 아니다 (플레이어에게는 그대로 보인다).</summary>
    protected internal override bool IsAoiWatcher() => false;
}

/// <summary>줍기 검증 결과.</summary>
public enum PickupResult : byte
{
    Ok,
    NotFound,       // 없거나 아이템이 아님
    OutOfRange,     // 줍기 거리 밖
    NotOwner,       // 소유권 시간 중이고 내 것이 아님
    Dead,           // 줍는 쪽이 사망 상태
}

/// <summary>
/// 몬스터 드롭과 지면 아이템 관리. 존(FieldZoneBase)을 상속하지 않고 참조로 쓰는 구성 요소다.
/// 존 클래스가 연결해야 하는 지점: 몬스터 사망 훅 -> OnMonsterDied(), 엔티티 제거 훅 -> OnRemoved(), 줍기 요청 -> ValidatePickup() 후 Consume().
/// 존 스레드에서만 호출한다.
/// 드롭하지 않는 경우: 소환수, 드롭 테이블에 없는 몬스터, 지면 아이템이 MaxGroundItems에 이른 경우(건너뛴 수는 SkippedFull).
/// </summary>
public sealed class DropSystem
{
    private readonly FieldZoneBase _zone;
    private readonly DropData? _data;
    private readonly Random _rng;
    private ulong _serial;
    private int _groundCount;

    public DropSystem(FieldZoneBase zone, DropData? data)
    {
        _zone = zone;
        _data = data;
        _rng = new Random(unchecked((int)zone.ZoneId * 31 + 7));
    }

    /// <summary>드롭 직후 처치자만 주울 수 있는 시간(ms). 기본은 DropOwnerMs. 구동 전에 조정한다.</summary>
    public uint OwnerMs = GameConsts.DropOwnerMs;

    /// <summary>지면 아이템이 사라지기까지의 시간(ms). 기본은 DropLifetimeMs. 구동 전에 조정한다.</summary>
    public uint LifetimeMs = GameConsts.DropLifetimeMs;

    /// <summary>지금 바닥에 있는 아이템 수.</summary>
    public int GroundCount => _groundCount;

    /// <summary>시작 후 떨어뜨린 아이템 수 (항목 단위).</summary>
    public ulong Dropped { get; private set; }

    /// <summary>지면 아이템이 가득 차서 건너뛴 드롭 수.</summary>
    public ulong SkippedFull { get; private set; }

    /// <summary>
    /// 몬스터가 죽었을 때 호출한다. 드롭 항목마다 확률을 굴려 몬스터 주변에 아이템을 떨어뜨린다.
    /// 처치자가 플레이어면 DropOwnerMs 동안 그 플레이어만 주울 수 있다(그 외에는 처음부터 누구나).
    /// </summary>
    public void OnMonsterDied(Monster monster, ZoneEntity? killer)
    {
        if (_data == null || monster.IsSummoned)
            return;

        IReadOnlyList<DropEntry>? entries = _data.Find(monster.Template.Id);
        if (entries == null)
            return;

        ulong ownerId = killer != null && killer.Type == EntityType.Player ? killer.Id : 0;
        for (int i = 0; i < entries.Count; ++i)
        {
            DropEntry e = entries[i];
            if (_rng.Next(1000) >= (int)e.ChancePermille)
                continue;

            uint count = e.MinCount == e.MaxCount ? e.MinCount : e.MinCount + (uint)_rng.Next((int)(e.MaxCount - e.MinCount + 1));
            SpawnItem(e.ItemId, count, monster.Pos, ownerId);
        }
    }

    /// <summary>
    /// 지면 아이템을 만든다. 위치는 center 주변 DropScatterRadius 안의 걸을 수 있는 지점이며 못 찾으면 center를 쓴다.
    /// 가득 찼거나 둘 곳이 없으면 null.
    /// </summary>
    public GroundItem? SpawnItem(uint itemId, uint count, Vec2 center, ulong ownerId)
    {
        if (itemId == 0 || count == 0)
            return null;

        if (_groundCount >= (int)GameConsts.MaxGroundItems)
        {
            ++SkippedFull;
            return null;
        }

        Vec2 pos = center;
        for (int tries = 0; tries < 8; ++tries)
        {
            const double TwoPi = 6.283185307179586;
            double r = GameConsts.DropScatterRadius * Math.Sqrt(_rng.NextDouble());
            double th = TwoPi * _rng.NextDouble();
            Vec2 p = new Vec2 { X = center.X + (float)(r * Math.Cos(th)), Y = center.Y + (float)(r * Math.Sin(th)) };
            if (_zone.IsWalkable(p) && (_zone.BlockMap == null || _zone.BlockMap.IsSegmentClear(center, p)))
            {
                pos = p;
                break;
            }
        }

        ulong nowMs = _zone.NowMs;
        GroundItem item = new GroundItem(ItemIds.Base | ++_serial, itemId, count, ownerId, ownerId != 0 ? nowMs + OwnerMs : 0);
        item.SetSpawnPos(pos);
        if (_zone.AddEntityBase(item) == null)
            return null;

        ++_groundCount;
        ++Dropped;

        ulong id = item.Id;
        item._expireTimerId = _zone.ScheduleTimer(LifetimeMs, zone =>
        {
            FieldZoneBase z = (FieldZoneBase)zone;
            if (z.FindEntityBase(id) is GroundItem g && ReferenceEquals(g, item))
            {
                g._expireTimerId = 0;
                z.RemoveEntity(id);
            }
        });
        return item;
    }

    /// <summary>
    /// 줍기 요청을 검증한다. 사망 -> 존재/종류 -> 소유권 -> 거리 순이다(싼 검사 먼저).
    /// 거리는 PickupRange에 위치 허용 오차(기본 오차 + 속도 x 최대 지연)를 더해 본다.
    /// </summary>
    public PickupResult ValidatePickup(ZoneEntity picker, ulong entityId, out GroundItem? item)
    {
        item = null;
        if (!picker.IsAlive)
            return PickupResult.Dead;

        if (_zone.FindEntityBase(entityId) is not GroundItem g)
            return PickupResult.NotFound;

        if (g.OwnerId != 0 && g.OwnerId != picker.Id && _zone.NowMs < g.OwnerUntilMs)
            return PickupResult.NotOwner;

        FieldZoneConfig cfg = _zone.FieldConfig;
        float reach = (float)GameConsts.PickupRange + cfg.PosToleranceBase + (picker.IsMoving ? picker.Speed : 0.0f) * (float)cfg.MaxLatencyMs * 0.001f;
        if (!(Vec2.DistanceSq(picker.Pos, g.Pos) <= reach * reach))
            return PickupResult.OutOfRange;

        item = g;
        return PickupResult.Ok;
    }

    /// <summary>검증을 통과한 아이템을 바닥에서 치운다. AOI 이탈 통지로 주변 플레이어에게 디스폰이 나간다.</summary>
    public void Consume(GroundItem item) => _zone.RemoveEntity(item.Id);

    /// <summary>엔티티가 존에서 제거되기 직전에 호출한다. 지면 아이템이면 소멸 타이머를 취소하고 개수를 줄인다.</summary>
    public void OnRemoved(ZoneEntity entity)
    {
        if (entity is not GroundItem g)
            return;

        if (g._expireTimerId != 0)
        {
            _zone.CancelTimer(g._expireTimerId);
            g._expireTimerId = 0;
        }
        if (_groundCount > 0)
            --_groundCount;
    }
}
