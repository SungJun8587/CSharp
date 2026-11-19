// ZoneEntity.cs : implementation of the ZoneEntity class.
using System;
using System.Collections.Generic;

namespace Game.ZoneCore;

/// <summary>존에 배치되는 엔티티 종류.</summary>
public enum EntityType : byte
{
    Player,
    Monster,
    Npc,
    Item,
}

/// <summary>
/// 엔티티의 전투 능력치. 서버가 정한 값만 쓰며 클라이언트가 보낸 값은 신뢰하지 않는다.
/// SetCombatStats()가 범위를 보정하므로 잘못된 값이 들어와도 전투 계산이 깨지지 않는다.
/// </summary>
public struct CombatStats
{
    public int MaxHp;                   // 최대 체력 (1 이상)
    public int Attack;                  // 공격력 (0 이상)
    public int Defense;                 // 방어력 (0 이상)
    public float AttackRange;           // 기본 공격 사거리 (units)
    public uint AttackIntervalMs;       // 기본 공격 최소 간격 (ms, 1 이상)
    public int MaxMp;                   // 최대 MP (0이면 MP 시스템을 쓰지 않는다. MpCost가 있는 스킬은 쓸 수 없다)
    public int HpRegen;                 // 초당 체력 자연 회복량 (0이면 없음. 전투 후 RegenCombatDelayMs가 지나야 회복한다)
    public int MpRegen;                 // 초당 MP 자연 회복량 (0이면 없음)

    /// <summary>기본값(체력 100, 공격력 10, 방어력 0, 사거리 2, 간격 1000ms)을 가진 능력치.</summary>
    public static CombatStats Default => new CombatStats
    {
        MaxHp = 100,
        Attack = 10,
        Defense = 0,
        AttackRange = 2.0f,
        AttackIntervalMs = 1000,
    };
}

/// <summary>
/// 클라이언트 요청(이동/공격 등)의 최소 간격 검사기. 존 논리 시간 기준이며 엔티티마다 요청 종류별로 하나씩 둔다.
/// 통과한 요청만 시각을 갱신하므로, 거부된 요청이 간격을 계속 밀어내지 않는다.
/// </summary>
public struct ReqGate
{
    private ulong _lastMs;      // 마지막으로 통과한 요청의 존 논리 시각
    private bool _has;          // 통과한 요청이 있는지

    /// <summary>요청을 통과시킬지 판정하고, 통과하면 시각을 기록한다.</summary>
    /// <param name="nowMs">현재 존 논리 시각</param>
    /// <param name="minIntervalMs">요청 최소 간격(ms). 0이면 제한 없음</param>
    public bool TryPass(ulong nowMs, uint minIntervalMs)
    {
        if (!CanPass(nowMs, minIntervalMs))
            return false;

        _has = true;
        _lastMs = nowMs;
        return true;
    }

    /// <summary>
    /// 통과할 수 있는지만 판정한다. 시각은 기록하지 않는다.
    /// 검증을 모두 통과한 요청만 TryPass로 기록하려는 호출자가 쓴다(거부된 요청이 같은 틱의 다른 요청을 막지 않게 한다).
    /// </summary>
    public readonly bool CanPass(ulong nowMs, uint minIntervalMs)
    {
        return !(minIntervalMs != 0 && _has && nowMs - _lastMs < minIntervalMs);
    }

    public void Reset()
    {
        _has = false;
        _lastMs = 0;
    }
}

/// <summary>
/// 진행 중인 스킬 시전. 시전 시간이 있는 스킬만 만들어지며, 시전자 한 명당 하나다. FieldZoneBase 전용.
/// 스킬 데이터는 복사해 둔다. 시전 중에 데이터 객체가 바뀌어도 이 시전은 시작할 때의 값으로 끝난다.
/// </summary>
internal sealed class CastState
{
    public SkillTemplate Skill = null!;     // 시전 중인 스킬 (사본)
    public ulong TargetId;                  // Single의 대상 ID
    public Vec2 Center;                     // 효과 중심 (Single은 시작 시점의 대상 위치)
    public ulong EndMs;                     // 시전이 끝나는 존 논리 시각
    public ulong TimerId;                   // 완료 타이머 ID
}

/// <summary>엔티티에 걸려 있는 상태 효과 하나. FieldZoneBase 전용.</summary>
internal sealed class ActiveStatus
{
    public uint Id;                         // 상태 효과 ID
    public Game.Generated.EStatusKind Kind = Game.Generated.EStatusKind.Dot;
    public ulong CasterId;                  // 건 엔티티 ID (퇴장했을 수 있다)
    public ulong EndMs;                     // 끝나는 존 논리 시각
    public ulong NextTickMs;                // 다음 Dot/Hot 틱 시각
    public uint TickMs;                     // 틱 간격
    public int Amount;                      // Dot: 한 틱 피해, Hot: 한 틱 회복량, Shield: 남은 흡수량 (걸 때 정해진다)
    public int AttackPercent;
    public int DefensePercent;
    public int SpeedPercent;
}

/// <summary>AOI 격자 안에서의 위치를 기록해 두는 노드. 엔티티 하나가 한 격자에만 속하므로 O(1)로 제거/이동할 수 있다.</summary>
internal struct AoiNode
{
    public const uint InvalidSlot = uint.MaxValue;

    public int CellIdx;         // 소속 셀 인덱스 (-1: 미소속)
    public uint Slot;           // 소속 셀 안에서의 위치
    public bool Watcher;        // 등록 시점의 IsAoiWatcher 값. 셀의 어느 목록에 있는지 결정한다

    public static AoiNode Unassigned => new AoiNode { CellIdx = -1, Slot = InvalidSlot, Watcher = false };
}

/// <summary>
/// 존에 배치되는 모든 객체(플레이어/몬스터/NPC/아이템)의 기반 클래스.
/// 위치/이동/AOI 소속 정보는 FieldZoneBase와 GridAOI만 변경하며(internal), 모든 접근은 존 스레드에서만 이루어지므로 별도의 락이 없다.
/// AOI 진입/이탈 통지(OnAoiEnter/OnAoiLeave)는 파생 클래스가 재정의하여 스폰/디스폰 패킷 전송 등에 사용한다.
/// 통지 콜백 안에서는 AOI 구조를 변경(Add/Remove/Update)하거나 엔티티를 제거해서는 안 된다.
/// </summary>
public class ZoneEntity
{
    internal const int InvalidIndex = -1;

    private readonly ulong _id;                 // 엔티티 ID
    private readonly EntityType _type;          // 엔티티 종류

    internal Vec2 _pos;                         // 현재 위치 (서버 권위)
    internal Vec2 _dest;                        // 이동 목적지
    internal float _speed;                      // 이동 속도 (units/sec)
    internal bool _moving;                      // 이동 중 여부
    internal bool _inZone;                      // 존에 등록되었는지 여부
    internal Vec2 _spawnPos;                    // 리스폰 위치 (SetSpawnPos로 설정)

    internal CombatStats _stats;                // 전투 능력치
    internal int _hp;                           // 현재 체력
    internal bool _dead;                        // 사망 여부
    internal ulong _nextAttackMs;               // 다음 기본 공격이 가능한 존 논리 시각
    internal ulong _invulnerableUntilMs;        // 무적 종료 시각 (존 논리 시간, 0이면 무적 아님)
    internal ReqGate _attackGate;               // 공격 요청 간격 검사
    internal int _mp;                           // 현재 MP
    internal ulong _combatUntilMs;              // 이 시각 전까지는 전투 중으로 보고 체력을 자연 회복하지 않는다 (존 논리 시간)
    internal int _regenIdx = InvalidIndex;      // 자연 회복 대상 목록 내 위치
    internal int _hpRegenRemain, _mpRegenRemain;    // 자연 회복에서 정수로 못 낸 나머지 (천분의 1 단위. 주기가 1초보다 짧아도 초당 회복량이 맞게 한다)
    internal ulong _respawnTimerId;             // 예약된 자동 리스폰 타이머 ID (0이면 없음)
    internal float _baseSpeed;                  // 상태 효과를 반영하기 전의 이동 속도 (SetSpeed가 정한 값)
    internal int _attackPct;                    // 상태 효과 합산 공격력 증감(%)
    internal int _defensePct;                   // 상태 효과 합산 방어력 증감(%)
    internal int _speedPct;                     // 상태 효과 합산 이동속도 증감(%)
    internal CastState? _cast;                  // 진행 중인 시전 (없으면 null)
    internal readonly List<ActiveStatus> _statuses = new List<ActiveStatus>();   // 걸려 있는 상태 효과
    internal int _statusIdx = InvalidIndex;     // 상태 효과 보유 목록 내 위치

    internal AoiNode _aoiNode = AoiNode.Unassigned;     // 소속 AOI 셀 정보
    internal int _movingIdx = InvalidIndex;     // 이동 목록 내 위치
    internal ReqGate _moveGate;                 // 이동 요청 간격 검사

    /// <param name="id">존 내에서 유일한 엔티티 ID</param>
    /// <param name="type">엔티티 종류</param>
    public ZoneEntity(ulong id, EntityType type)
    {
        _id = id;
        _type = type;
        _stats = CombatStats.Default;
        _hp = _stats.MaxHp;
    }

    /// <summary>엔티티 ID. 존 내에서 유일하다.</summary>
    public ulong Id => _id;

    /// <summary>엔티티 종류.</summary>
    public EntityType Type => _type;

    /// <summary>서버가 보유한 현재 위치.</summary>
    public Vec2 Pos => _pos;

    /// <summary>이동 중인 경우의 목적지.</summary>
    public Vec2 Dest => _dest;

    /// <summary>현재 이동 속도. 상태 효과(이동속도 버프/디버프)가 반영된 값이다.</summary>
    public float Speed => _speed;

    /// <summary>상태 효과를 반영한 공격력. 전투 계산은 CombatStats의 기본값이 아니라 이 값을 쓴다.</summary>
    public int Attack => SkillRules.ApplyStatPercent(_stats.Attack, _attackPct);

    /// <summary>상태 효과를 반영한 방어력.</summary>
    public int Defense => SkillRules.ApplyStatPercent(_stats.Defense, _defensePct);

    /// <summary>스킬 시전 중인지. 시전 시간이 있는 스킬만 해당한다.</summary>
    public bool IsCasting => _cast != null;

    /// <summary>걸려 있는 상태 효과 수.</summary>
    public int StatusCount => _statuses.Count;

    /// <summary>특정 상태 효과를 보유 중인지.</summary>
    public bool HasStatus(uint statusId)
    {
        for (int i = 0; i < _statuses.Count; ++i)
            if (_statuses[i].Id == statusId)
                return true;
        return false;
    }

    /// <summary>
    /// 이동 속도를 설정한다. 속도는 서버 스탯으로만 결정되며 클라이언트 값은 신뢰하지 않는다.
    /// NaN/무한대/음수는 0(이동 불가)으로 보정한다. 이동 중에 0 이하가 되면 다음 틱에 그 자리에 멈춘다.
    /// </summary>
    public void SetSpeed(float speed)
    {
        _baseSpeed = (float.IsFinite(speed) && speed > 0.0f) ? speed : 0.0f;
        _speed = _baseSpeed * (float)(100 + _speedPct) * 0.01f;
    }

    /// <summary>이동 중인지.</summary>
    public bool IsMoving => _moving;

    /// <summary>전투 능력치.</summary>
    public CombatStats CombatStats => _stats;

    /// <summary>현재 체력. 사망 상태면 0이다.</summary>
    public int Hp => _hp;

    /// <summary>현재 MP.</summary>
    public int Mp => _mp;

    /// <summary>최대 MP. 0이면 MP 시스템을 쓰지 않는다.</summary>
    public int MaxMp => _stats.MaxMp;

    /// <summary>살아 있는지. 사망한 엔티티는 리스폰하기 전까지 이동/공격/피격 대상이 될 수 없다.</summary>
    public bool IsAlive => !_dead;

    /// <summary>전투에 참여하는 종류(플레이어/몬스터)인지.</summary>
    public bool IsCombatant => _type == EntityType.Player || _type == EntityType.Monster;

    /// <summary>다음 기본 공격이 가능한 존 논리 시각. AI가 쿨다운 중 불필요한 공격 요청을 건너뛰는 데 쓴다.</summary>
    public ulong NextAttackMs => _nextAttackMs;

    /// <summary>무적이 끝나는 존 논리 시각. 0이면 무적이 아니다.</summary>
    public ulong InvulnerableUntilMs => _invulnerableUntilMs;

    /// <summary>리스폰 위치. SetSpawnPos()로 정한 위치다.</summary>
    public Vec2 SpawnPos => _spawnPos;

    /// <summary>
    /// 스폰 위치를 설정한다. 존에 등록(AddEntity)되기 전에만 유효하며,
    /// 등록 이후의 위치 변경은 FieldZoneBase(이동/순간이동)를 통해서만 가능하다.
    /// </summary>
    public void SetSpawnPos(Vec2 pos)
    {
        if (_inZone)
            return;

        _pos = pos;
        _dest = pos;
        _spawnPos = pos;
    }

    /// <summary>
    /// 전투 능력치를 설정하고 체력을 가득 채운다. 존에 등록(AddEntity)되기 전에만 유효하다.
    /// 잘못된 값은 보정한다: MaxHp/AttackIntervalMs는 1 이상, Attack/Defense는 0 이상, AttackRange는 0 이상의 유한한 값(NaN/Inf는 0).
    /// </summary>
    public void SetCombatStats(CombatStats stats)
    {
        if (_inZone)
            return;

        _stats = stats;
        if (_stats.MaxHp < 1)
            _stats.MaxHp = 1;
        if (_stats.Attack < 0)
            _stats.Attack = 0;
        if (_stats.Defense < 0)
            _stats.Defense = 0;
        if (!float.IsFinite(_stats.AttackRange) || _stats.AttackRange < 0.0f)
            _stats.AttackRange = 0.0f;
        if (_stats.AttackIntervalMs < 1)
            _stats.AttackIntervalMs = 1;
        if (_stats.MaxMp < 0)
            _stats.MaxMp = 0;
        if (_stats.HpRegen < 0)
            _stats.HpRegen = 0;
        if (_stats.MpRegen < 0)
            _stats.MpRegen = 0;

        _hp = _stats.MaxHp;
        _mp = _stats.MaxMp;
    }

    internal int _restoreHp = -1;       // RestoreVitals()로 정한 입장 시 체력(없으면 -1)
    internal int _restoreMp = -1;

    /// <summary>
    /// 존에 등록(AddEntity)되기 전에, 다른 존에서 가져온 체력/MP로 현재 값을 정한다(존 이동/재접속 상태 유지).
    /// 범위를 벗어난 값은 보정한다: 체력은 1~MaxHp(죽은 채로 입장하지 않는다), MP는 0~MaxMp. SetCombatStats() 뒤에 불러야 한다.
    /// </summary>
    public void RestoreVitals(int hp, int mp)
    {
        if (_inZone)
            return;

        _hp = Math.Clamp(hp, 1, _stats.MaxHp);
        _mp = Math.Clamp(mp, 0, _stats.MaxMp);
        _restoreHp = _hp;       // AddEntity()가 입장 시 체력/MP를 가득 채우므로, 가져온 값을 그때 적용하도록 기억해 둔다
        _restoreMp = _mp;
    }

    /// <summary>
    /// 존에 등록되기 전에 시작 위치만 정한다. SetSpawnPos()와 달리 리스폰 위치(SpawnPos)는 바꾸지 않는다
    /// (관문으로 들어온 플레이어도 죽으면 존의 기본 리스폰 위치에서 부활한다).
    /// </summary>
    public void SetStartPos(Vec2 pos)
    {
        if (_inZone)
            return;

        _pos = pos;
        _dest = pos;
    }

    /// <summary>다른 엔티티가 시야(AOI)에 들어왔을 때 호출된다. 플레이어는 재정의하여 상대의 스폰 패킷을 전송한다.</summary>
    protected internal virtual void OnAoiEnter(ZoneEntity other)
    {
    }

    /// <summary>다른 엔티티가 시야(AOI)에서 벗어났을 때 호출된다. 플레이어는 재정의하여 상대의 디스폰 패킷을 전송한다.</summary>
    protected internal virtual void OnAoiLeave(ZoneEntity other)
    {
    }

    /// <summary>
    /// AOI 관찰자인지. 존에 등록(AddEntity)할 때 한 번 읽는다.
    /// 두 엔티티 중 하나라도 관찰자일 때만 서로에게 OnAoiEnter/OnAoiLeave가 전달된다. 기본은 true(모든 통지를 받음)이다.
    /// 다른 엔티티의 등장/퇴장에 반응할 필요가 없는 쪽(밀집한 몬스터 등)은 false로 재정의하면 서로 간의 통지와 순회 비용이 사라진다.
    /// 단, 관찰자인 상대(플레이어)와의 통지는 그대로 받는다.
    /// </summary>
    protected internal virtual bool IsAoiWatcher() => true;
}
