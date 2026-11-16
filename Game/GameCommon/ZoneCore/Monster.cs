// Monster.cs : AI로 움직이는 몬스터 엔티티.
using System;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>몬스터 엔티티 ID 대역. 최상위 비트가 켜진 ID는 MonsterSystem이 발급하는 몬스터 전용이며, 플레이어(캐릭터 ID)는 이 비트를 쓰지 않아야 한다.</summary>
public static class MonsterIds
{
    public const ulong Base = 0x8000000000000000UL;
}

/// <summary>몬스터 AI 상태.</summary>
public enum MonsterState : byte
{
    Idle,       // 제자리 대기. 주변 플레이어를 감시하다 선공 대상을 찾거나 피격되면 Chase
    Chase,      // 어그로 대상을 추적하고 사거리 안이면 공격
    Return,     // 추적을 포기하고 스폰 지점으로 복귀. 도착하면 체력을 회복하고 Idle. 복귀 중에는 어그로를 받지 않는다
}

/// <summary>어그로 표의 한 항목.</summary>
internal struct AggroEntry
{
    public ulong EntityId;      // 위협을 준 엔티티 ID
    public long Threat;         // 누적 위협 수치
}

/// <summary>
/// AI로 움직이는 몬스터 엔티티. 데이터(템플릿)는 불변 공유 객체를 가리키고, 개체별 상태만 가진다.
/// 시야(AOI)에 플레이어가 한 명도 없는 몬스터는 AI 목록에 올라가지 않아 틱 비용이 0이다.
/// 상태를 바꾸는 로직은 모두 MonsterSystem이 가지며, 이 클래스는 상태 저장과 어그로 표 관리만 한다.
/// </summary>
public class Monster : ZoneEntity
{
    public const int MaxAggro = 8;      // 어그로 표 최대 항목 수 (가장 낮은 위협부터 밀려난다)

    private readonly MonsterTemplate _tmpl;                 // 몬스터 종류 데이터
    private readonly MonsterSystem? _system;                // AI 시스템 (활성화 요청 대상)
    private readonly Vec2 _anchor;                          // 스폰 중심점
    private readonly float _spawnRadius;                    // 스폰 반경

    internal Vec2 _home;                                    // 복귀 지점 (마지막 스폰 위치)
    internal MonsterState _state = MonsterState.Idle;       // AI 상태
    internal uint _nearPlayers;                             // 시야 안 플레이어 수
    internal bool _active;                                  // AI 목록에 올라 있는지
    internal int _activeIdx;                                // AI 목록 내 위치
    internal ulong _nextThinkMs;                            // 다음 사고 시각 (존 논리 시간)
    internal ulong _targetId;                               // 현재 추적/공격 대상 (0이면 없음)
    internal Vec2 _lastTargetPos;                           // 마지막으로 이동 명령을 내릴 때의 대상 위치 (재경로 판단용)
    internal ulong _skillReadyAtMs;                         // 몬스터 스킬을 다시 쓸 수 있는 존 논리 시각
    internal ulong _noAggroUntilMs;                         // 이 시각 전에는 근접 선공으로 새 대상을 잡지 않는다
    internal ulong _tauntId;                                // 도발한 엔티티 ID (_tauntUntilMs 전까지 이 대상을 우선 추적)
    internal ulong _tauntUntilMs;                           // 도발이 끝나는 존 논리 시각 (0이면 도발 중 아님)
    internal ulong _noPathUntilMs;                          // 이 시각 전에는 경로탐색을 다시 하지 않는다
    internal readonly AggroEntry[] _aggro = new AggroEntry[MaxAggro];   // 어그로 표
    internal int _aggroCount;                               // 어그로 표 항목 수
    internal BossRuntime? _boss;                            // 보스 전투 상태 (보스가 아니면 null. 소유는 BossSystem)
    internal bool _summoned;                                // 보스가 소환한 몬스터인지. 소환수는 부활하지 않고 죽으면 시체를 치운다
    internal ulong _ownerId;                                // 소환한 보스의 엔티티 ID (소환수만)

    /// <param name="id">몬스터 엔티티 ID (MonsterIds.Base 대역)</param>
    /// <param name="tmpl">몬스터 템플릿</param>
    /// <param name="system">AI 시스템</param>
    /// <param name="anchor">스폰 중심점</param>
    /// <param name="spawnRadius">스폰 반경</param>
    public Monster(ulong id, MonsterTemplate tmpl, MonsterSystem? system, Vec2 anchor, float spawnRadius)
        : base(id, EntityType.Monster)
    {
        _tmpl = tmpl;
        _system = system;
        _anchor = anchor;
        _spawnRadius = spawnRadius;
        SetCombatStats(tmpl.Stats);
        SetSpeed(tmpl.Speed);
    }

    /// <summary>몬스터 템플릿.</summary>
    public MonsterTemplate Template => _tmpl;

    /// <summary>스폰 중심점. 부활 위치는 이 점을 중심으로 한 반경 안에서 정해진다.</summary>
    public Vec2 Anchor => _anchor;

    /// <summary>스폰 반경.</summary>
    public float SpawnRadius => _spawnRadius;

    /// <summary>복귀 지점(마지막으로 스폰된 위치).</summary>
    public Vec2 Home => _home;

    public MonsterState State => _state;
    public ulong TargetId => _targetId;
    public uint NearPlayers => _nearPlayers;
    public bool IsActive => _active;
    public bool IsBoss => _boss != null;
    public bool IsSummoned => _summoned;
    public ulong OwnerId => _ownerId;
    public int AggroCount => _aggroCount;

    /// <summary>어그로 표에서 엔티티의 누적 위협. 표에 없으면 0.</summary>
    public long GetThreat(ulong entityId)
    {
        for (int i = 0; i < _aggroCount; ++i)
            if (_aggro[i].EntityId == entityId)
                return _aggro[i].Threat;
        return 0;
    }

    /// <summary>
    /// 어그로 표에 위협을 더한다. 표가 가득 차면 가장 낮은 위협 항목을 밀어내되, 새 위협이 그보다 클 때만 들어간다.
    /// </summary>
    /// <param name="amount">더할 위협 수치 (0 이하는 무시)</param>
    public void AddThreat(ulong entityId, long amount)
    {
        if (amount <= 0)
            return;

        for (int i = 0; i < _aggroCount; ++i)
        {
            if (_aggro[i].EntityId == entityId)
            {
                // 오버플로 방지: 한계를 넘으면 포화시킨다.
                _aggro[i].Threat = amount > long.MaxValue - _aggro[i].Threat ? long.MaxValue : _aggro[i].Threat + amount;
                return;
            }
        }

        if (_aggroCount < MaxAggro)
        {
            _aggro[_aggroCount].EntityId = entityId;
            _aggro[_aggroCount].Threat = amount;
            ++_aggroCount;
            return;
        }

        int lowest = 0;
        for (int i = 1; i < _aggroCount; ++i)
        {
            if (_aggro[i].Threat < _aggro[lowest].Threat)
                lowest = i;
        }

        if (amount > _aggro[lowest].Threat)
        {
            _aggro[lowest].EntityId = entityId;
            _aggro[lowest].Threat = amount;
        }
    }

    /// <summary>어그로 표에서 항목을 제거한다. 대상이 사라졌거나 공격할 수 없을 때 호출한다.</summary>
    public void RemoveThreat(ulong entityId)
    {
        for (int i = 0; i < _aggroCount; ++i)
        {
            if (_aggro[i].EntityId == entityId)
            {
                _aggro[i] = _aggro[_aggroCount - 1];
                --_aggroCount;

                if (_targetId == entityId)
                    _targetId = 0;
                return;
            }
        }
    }

    /// <summary>어그로 표와 현재 대상을 비운다.</summary>
    public void ClearAggro()
    {
        _aggroCount = 0;
        _targetId = 0;
        _tauntId = 0;
        _tauntUntilMs = 0;
    }

    /// <summary>시야에 엔티티가 들어왔을 때 호출된다. 플레이어면 카운트를 올리고, 첫 플레이어가 보이면 AI를 깨운다.</summary>
    protected internal override void OnAoiEnter(ZoneEntity other)
    {
        if (other.Type != EntityType.Player)
            return;

        if (++_nearPlayers == 1 && IsAlive && _system != null)
            _system.Activate(this);
    }

    /// <summary>시야에서 엔티티가 벗어났을 때 호출된다. 비활성화는 다음 사고에서 대기 상태일 때 이루어진다.</summary>
    protected internal override void OnAoiLeave(ZoneEntity other)
    {
        if (other.Type == EntityType.Player && _nearPlayers > 0)
            --_nearPlayers;
    }

    /// <summary>몬스터는 시야 안 플레이어 수만 세므로, 다른 몬스터와는 AOI 통지를 주고받지 않는다.</summary>
    protected internal override bool IsAoiWatcher() => false;
}
