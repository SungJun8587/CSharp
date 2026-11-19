// MonsterSystem.cs : 필드 존에 몬스터를 스폰하고 AI(대기/추적/공격/복귀)와 어그로를 구동하는 시스템.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Util;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>
/// 몬스터 AI 튜닝값.
/// <list type="bullet">
/// <item>ThinkIntervalMs : 몬스터 한 마리의 사고 주기(ms). 작을수록 반응이 빠르고 비용이 는다</item>
/// <item>RepathDistance : 추적 중 대상이 이만큼 움직여야 이동 명령을 다시 내린다(이동 브로드캐스트 억제)</item>
/// <item>EngageRatio : 사거리의 이 비율 안에 들어오면 이동을 멈추고 공격한다 (0 초과 1 이하)</item>
/// <item>PathMaxNodes : 우회 경로탐색 한 번에 펼칠 수 있는 셀 수 상한(1 이상)</item>
/// <item>PathSearchesPerTick : 한 틱에 할 수 있는 경로탐색 횟수(1 이상). 0이면 막힌 몬스터가 포기도 이동도 못 하므로 Validate()가 거부한다</item>
/// <item>PathRetryMs : 경로를 못 찾은 몬스터가 다시 탐색하기까지, 닿을 수 없는 대상을 포기한 뒤 다시 선공하기까지 기다리는 시간(ms)</item>
/// <item>MaxMonsters : 이 존에 스폰할 수 있는 몬스터 총 마릿수 상한. 스폰 테이블 합계가 넘으면 Validate()가 거부한다</item>
/// <item>HealThreatRange : 치유 위협을 받는 몬스터를 찾는 반경(치유받은 대상 기준). 0이면 치유 위협이 없다</item>
/// <item>HealThreatPercent : 실제 회복량 중 위협으로 쌓이는 비율(%). 치유자는 치유받은 대상이 어그로 표에 있는 몬스터마다 이만큼 위협을 얻는다</item>
/// </list>
/// </summary>
public sealed class MonsterSystemConfig
{
    public uint ThinkIntervalMs = 100;
    public float RepathDistance = 1.5f;
    public float EngageRatio = 0.9f;
    public uint PathMaxNodes = BlockMap.DefaultPathNodes;
    public uint PathSearchesPerTick = 4;
    public uint PathRetryMs = 2000;
    public uint MaxMonsters = (uint)MonsterData.MaxSpawnPerZone;
    public float HealThreatRange = 20.0f;
    public uint HealThreatPercent = 50;

    public MonsterSystemConfig Clone() => (MonsterSystemConfig)MemberwiseClone();
}

/// <summary>
/// 필드 존에 몬스터를 스폰하고 AI(대기/추적/공격/복귀)와 어그로를 구동하는 시스템.
/// 존(FieldZoneBase)을 상속하지 않고 참조로 사용하는 구성 요소이므로, 어떤 존 클래스에도 붙일 수 있다.
/// 존 클래스가 연결해야 하는 지점:
///   - 구동 전    : Validate() 후 SpawnAll()
///   - 매 틱      : OnUpdate()에서 이동 갱신 뒤 Update() (이동 갱신 중 호출 금지)
///   - 훅 전달    : OnEntityDamaged -> OnDamaged, OnEntityDied -> OnDied, OnEntityRespawned -> OnRespawned, OnEntityRemoved -> OnRemoved
///   - 정책 전달  : GetRespawnDelayMs/GetRespawnPos를 같은 이름의 존 가상 함수에서 위임
/// 모든 메서드는 존 스레드에서만 호출한다.
/// 비용 모델: 시야에 플레이어가 있는 몬스터만 활성 목록에 오르고, 활성 몬스터도 ThinkIntervalMs마다 한 번만 사고한다.
/// </summary>
public sealed class MonsterSystem
{
    private const float ArriveDistanceSq = 0.25f;       // 복귀 도착으로 보는 거리(0.5 units)의 제곱
    private const float ChaseStandRatio = 0.8f;         // 추적 목적지를 대상 앞 사거리의 이 비율 지점으로 잡는다

    private readonly FieldZoneBase _zone;               // 대상 존
    private readonly MonsterData? _data;                // 몬스터 데이터
    private readonly MonsterSystemConfig _config;       // AI 튜닝값
    private readonly Random _rng;                       // 스폰/부활 위치용 난수 (존 ID로 시드해 재현 가능)
    private ulong _serial;                              // 몬스터 ID 발급 번호
    private int _monsterCount;                          // 존에 있는 AI 몬스터 수
    private readonly List<Monster> _active = new List<Monster>(256);    // AI 활성 몬스터 목록
    private uint _pathBudget;                           // 이번 틱에 남은 경로탐색 횟수
    private BossSystem? _boss;                          // 보스 시스템 (없으면 일반 몬스터만)
    private HeroData? _skills;                          // 스킬 데이터 (몬스터 스킬 조회용)
    private Monster? _alertSource;                         // AlertAllies 방문 상태 (존 스레드 전용)
    private ulong _alertTargetId, _alertNowMs;
    private uint _alertCount;
    private readonly Action<ZoneEntity> _alertFn;
    private ZoneEntity? _healSource;                        // OnHealedBy 방문 상태 (존 스레드 전용)
    private ulong _healTargetId;
    private long _healThreat;
    private readonly Action<ZoneEntity> _healFn;
    private readonly List<Vec2> _pathScratch = new List<Vec2>();        // 경로탐색 결과 작업 공간

    /// <param name="zone">몬스터를 배치하고 구동할 존 (이 객체보다 오래 살아야 함)</param>
    /// <param name="data">몬스터 데이터 (null이면 몬스터 없이 동작)</param>
    public MonsterSystem(FieldZoneBase zone, MonsterData? data, MonsterSystemConfig config)
    {
        _alertFn = AlertVisit;
        _healFn = HealVisit;
        _zone = zone;
        _data = data;
        _config = config.Clone();
        _rng = new Random(unchecked((int)zone.ZoneId));
        if (_config.ThinkIntervalMs < 1)
            _config.ThinkIntervalMs = 1;
    }

    /// <summary>보스 시스템을 붙인다 (null이면 뗀다). 존 구동 전에 한 번 호출한다.</summary>
    public void SetBossSystem(BossSystem? boss) => _boss = boss;

    /// <summary>몬스터 스킬(MonsterTemplate.SkillId)을 찾을 스킬 데이터를 붙인다. 스킬을 쓰는 몬스터가 있으면 존 구동 전에 반드시 호출한다(Validate가 확인).</summary>
    public void SetSkillData(HeroData? skills) => _skills = skills;

    public MonsterData? Data => _data;

    /// <summary>존에 있는 AI 몬스터 수.</summary>
    public int MonsterCount => _monsterCount;

    /// <summary>AI 활성 몬스터 수.</summary>
    public int ActiveCount => _active.Count;

    private static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>
    /// 이 존의 스폰 테이블이 존 설정과 맞는지 검사한다. (데이터 자체의 검사는 MonsterData.Validate)
    /// AI 튜닝값 범위, 스폰 중심이 존 영역 안이고 장애물 밖, 스폰 테이블에 나오는 템플릿 검사(ValidateTemplate).
    /// 첫 오류에서 멈추지 않고 모아서 한 번에 돌려준다(최대 16개).
    /// </summary>
    /// <param name="opponentMaxSpeed">몬스터와 맞붙는 상대(플레이어)가 가질 수 있는 최고 이동 속도 (units/sec). 모르면 0</param>
    public bool Validate(out string error, float opponentMaxSpeed = 0.0f)
    {
        List<string> errors = new List<string>();

        if (!(_config.EngageRatio > 0.0f && _config.EngageRatio <= 1.0f) || !(_config.RepathDistance > 0.0f))
            errors.Add("monster system config: engageRatio must be in (0, 1] and repathDistance > 0");

        if (_config.PathSearchesPerTick < 1 || _config.PathMaxNodes < 1)
            errors.Add("monster system config: pathSearchesPerTick and pathMaxNodes must be >= 1");

        if (_data != null)
        {
            ulong totalCount = 0;
            bool overLimitReported = false;
            HashSet<uint> checkedTemplates = new HashSet<uint>();

            IReadOnlyList<SpawnEntry> spawns = _data.Spawns;
            for (int i = 0; i < spawns.Count; ++i)
            {
                SpawnEntry s = spawns[i];
                if (s.ZoneId != _zone.ZoneId)
                    continue;

                totalCount += s.Count;
                if (totalCount > _config.MaxMonsters && !overLimitReported)
                {
                    overLimitReported = true;
                    errors.Add($"zone {s.ZoneId}: total spawn count exceeds the configured limit {_config.MaxMonsters} (reached at spawn[{i}])");
                }

                MonsterTemplate? tmpl = _data.FindMonster(s.MonsterId);
                if (tmpl == null)
                {
                    errors.Add($"spawn[{i}] zone {s.ZoneId}: unknown monster id {s.MonsterId}");
                    continue;
                }

                Vec2 center = new Vec2 { X = s.X, Y = s.Y };
                if (!_zone.IsInBounds(center))
                    errors.Add($"spawn[{i}] zone {s.ZoneId}: center ({F1(s.X)}, {F1(s.Y)}) of monster {s.MonsterId} is outside the zone bounds");
                else if (_zone.IsBlocked(center))
                    errors.Add($"spawn[{i}] zone {s.ZoneId}: center ({F1(s.X)}, {F1(s.Y)}) of monster {s.MonsterId} is inside an obstacle");

                if (checkedTemplates.Add(tmpl.Id))
                    ValidateTemplate(tmpl, opponentMaxSpeed, errors);
            }
        }

        return JoinErrors(errors, out error);
    }

    /// <summary>
    /// 몬스터 템플릿 하나가 이 존의 AOI 시야 안에서 동작하는지 검사하고, 문제를 errors에 덧붙인다.
    /// 선공 감지 거리/도움 요청 거리/기본 공격 판정 거리(사거리 + 허용 오차 + 이동 지연 보정)가 AOI 최소 보장 시야 이하여야 하고,
    /// 몬스터 스킬은 스킬 데이터가 있고 적 대상의 비투사체 스킬이며 시전 거리 + 효과 반경이 시야 안이어야 한다.
    /// </summary>
    /// <returns>문제가 없으면 true</returns>
    public bool ValidateTemplate(MonsterTemplate tmpl, float opponentMaxSpeed, List<string> errors)
    {
        int before = errors.Count;
        float viewRange = _zone.GetGuaranteedViewRange();
        float buffMul = MaxSpeedBuffMultiplier();
        float reachSpeed = Math.Max(tmpl.Speed * buffMul, opponentMaxSpeed);

        if (tmpl.AggroRange > viewRange)
            errors.Add($"monster {tmpl.Id}: aggroRange {F1(tmpl.AggroRange)} exceeds the guaranteed AOI view range {F1(viewRange)}");

        if (!_zone.IsAttackRangeSupported(tmpl.Stats.AttackRange, reachSpeed))
            errors.Add($"monster {tmpl.Id}: attack reach (range {F1(tmpl.Stats.AttackRange)}) exceeds the guaranteed AOI view range {F1(viewRange)}");

        if (tmpl.AssistRange > viewRange)
            errors.Add($"monster {tmpl.Id}: assistRange {F1(tmpl.AssistRange)} exceeds the guaranteed AOI view range {F1(viewRange)}");

        if (tmpl.SkillId != 0)
        {
            SkillTemplate? skill = _skills?.FindSkill(tmpl.SkillId);
            if (skill == null)
                errors.Add($"monster {tmpl.Id}: unknown skill id {tmpl.SkillId} (skill data missing?)");
            else if (skill.Target != ESkillTarget.Enemy || skill.ProjectileSpeed > 0.0f)
                errors.Add($"monster {tmpl.Id}: skill {tmpl.SkillId} must target enemies and must not be a projectile");
            else if (skill.MpCost != 0)
                errors.Add($"monster {tmpl.Id}: skill {tmpl.SkillId} must not cost MP (monsters have no MP)");
            else if (!_zone.IsAttackRangeSupported(skill.Range + skill.Radius, reachSpeed))
                errors.Add($"monster {tmpl.Id}: skill {tmpl.SkillId} reach (range + radius {F1(skill.Range + skill.Radius)}) exceeds the guaranteed AOI view range {F1(viewRange)}");
        }

        return errors.Count == before;
    }

    /// <summary>오류 목록을 줄바꿈으로 이어 붙인다. 많으면 앞의 16개만 적고 나머지 수를 덧붙인다.</summary>
    /// <returns>목록이 비었으면 true (검증 통과)</returns>
    public static bool JoinErrors(List<string> errors, out string error)
    {
        error = string.Empty;
        if (errors.Count == 0)
            return true;

        const int MaxReportedErrors = 16;
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < errors.Count && i < MaxReportedErrors; ++i)
        {
            if (i != 0)
                sb.Append('\n');
            sb.Append(errors[i]);
        }

        if (errors.Count > MaxReportedErrors)
            sb.Append("\n(+").Append(errors.Count - MaxReportedErrors).Append(" more)");

        error = sb.ToString();
        return false;
    }

    /// <summary>스킬 데이터의 이동속도 버프 최대치를 배율로 바꾼다. (스킬 데이터가 없으면 1)</summary>
    private float MaxSpeedBuffMultiplier() => 1.0f + (float)(_skills != null ? _skills.GetMaxSpeedBuffPercent() : 0) * 0.01f;

    /// <summary>이 존의 스폰 테이블에 나오는 몬스터가 낼 수 있는 가장 빠른 이동 속도(템플릿 속도 + 이동속도 버프 최대치). 스폰이 없으면 0.</summary>
    public float GetMaxSpawnSpeed()
    {
        float maxSpeed = 0.0f;
        if (_data == null)
            return maxSpeed;

        float buffMul = MaxSpeedBuffMultiplier();

        IReadOnlyList<SpawnEntry> spawns = _data.Spawns;
        for (int i = 0; i < spawns.Count; ++i)
        {
            if (spawns[i].ZoneId != _zone.ZoneId)
                continue;

            MonsterTemplate? tmpl = _data.FindMonster(spawns[i].MonsterId);
            if (tmpl != null && tmpl.Speed * buffMul > maxSpeed)
                maxSpeed = tmpl.Speed * buffMul;
        }

        return maxSpeed;
    }

    /// <summary>이 존의 스폰 테이블대로 몬스터를 모두 배치한다. 존 구동 전(또는 존 스레드)에서 한 번 호출한다. 최초 위치는 스폰 중심 반경 안의 무작위 지점이다.</summary>
    /// <returns>배치된 몬스터 수</returns>
    public uint SpawnAll()
    {
        if (_data == null)
            return 0;

        uint spawned = 0;
        ulong expected = 0;
        IReadOnlyList<SpawnEntry> spawns = _data.Spawns;
        for (int n = 0; n < spawns.Count; ++n)
        {
            SpawnEntry s = spawns[n];
            if (s.ZoneId != _zone.ZoneId)
                continue;

            MonsterTemplate? tmpl = _data.FindMonster(s.MonsterId);
            if (tmpl == null)
                continue;

            expected += s.Count;

            Vec2 anchor = new Vec2 { X = s.X, Y = s.Y };

            for (uint i = 0; i < s.Count; ++i)
            {
                Vec2 pos = PickFreePoint(anchor, s.Radius);

                Monster monster = new Monster(MonsterIds.Base | ++_serial, tmpl, this, anchor, s.Radius);
                monster.SetSpawnPos(pos);
                monster._home = pos;

                if (_zone.AddEntityBase(monster) != null)
                {
                    ++spawned;
                    ++_monsterCount;

                    _boss?.OnSpawned(monster);
                }
                else
                {
                    LogManager.Error("[MonsterSystem] spawn failed. zoneId={0}, monsterId={1}, pos=({2}, {3})", _zone.ZoneId, s.MonsterId, pos.X, pos.Y);
                }
            }
        }

        if (spawned != expected)
            LogManager.Error("[MonsterSystem] spawned fewer monsters than the spawn table. zoneId={0}, expected={1}, spawned={2}", _zone.ZoneId, expected, spawned);

        return spawned;
    }

    /// <summary>
    /// 보스가 부르는 몬스터를 만들어 존에 배치한다. 소환수는 부활하지 않고(보스가 리셋/사망하면 사라짐),
    /// 스폰 지점이 곧 복귀 지점이며, 지정한 대상에게 위협을 두고 바로 사고를 시작한다.
    /// </summary>
    /// <param name="threatTargetId">처음부터 노릴 대상 (0이면 없음)</param>
    /// <returns>소환수, 템플릿이 없거나 존에 등록하지 못했으면 null</returns>
    public Monster? SpawnSummon(uint monsterId, Vec2 center, float spreadRadius, ulong ownerId, ulong threatTargetId)
    {
        MonsterTemplate? tmpl = _data?.FindMonster(monsterId);
        if (tmpl == null)
            return null;

        Vec2 pos = PickFreePoint(center, spreadRadius);

        Monster monster = new Monster(MonsterIds.Base | ++_serial, tmpl, this, pos, 0.0f);
        monster.SetSpawnPos(pos);
        monster._home = pos;
        monster._summoned = true;
        monster._ownerId = ownerId;

        if (_zone.AddEntityBase(monster) == null)
        {
            LogThrottle.Error("monster.summon-failed", 1000, "[MonsterSystem] summon failed. zoneId={0}, monsterId={1}, ownerId={2}", _zone.ZoneId, monsterId, ownerId);
            return null;
        }

        ++_monsterCount;

        if (threatTargetId != 0)
            monster.AddThreat(threatTargetId, 1);

        Activate(monster);
        monster._nextThinkMs = _zone.NowMs;
        return monster;
    }

    /// <summary>매 틱 호출한다. 활성 몬스터 중 사고 시각이 된 것만 사고한다. 이동 갱신이 끝난 뒤에 호출해야 한다.</summary>
    public void Update()
    {
        ulong nowMs = _zone.NowMs;
        _pathBudget = _config.PathSearchesPerTick;

        for (int i = 0; i < _active.Count;)
        {
            Monster monster = _active[i];
            if (nowMs < monster._nextThinkMs)
            {
                ++i;
                continue;
            }

            monster._nextThinkMs = nowMs + _config.ThinkIntervalMs;
            Think(monster, nowMs);

            // 사고 중 스스로 비활성화되면 swap-remove로 i번 자리에 다른 몬스터가 들어오므로 i를 올리지 않는다.
            if (i < _active.Count && ReferenceEquals(_active[i], monster))
                ++i;
        }
    }

    /// <summary>몬스터를 활성 목록에 올린다. 이미 올라 있거나 죽었으면 무시한다. 첫 사고 시각은 몬스터 ID로 분산해 한 틱에 사고가 몰리지 않게 한다.</summary>
    public void Activate(Monster monster)
    {
        if (monster._active || !monster.IsAlive)
            return;

        monster._active = true;
        monster._activeIdx = _active.Count;
        monster._nextThinkMs = _zone.NowMs + (monster.Id % _config.ThinkIntervalMs);
        _active.Add(monster);
    }

    private void Deactivate(Monster monster)
    {
        if (!monster._active)
            return;

        int idx = monster._activeIdx;
        Monster last = _active[_active.Count - 1];
        _active[idx] = last;
        last._activeIdx = idx;
        _active.RemoveAt(_active.Count - 1);

        monster._active = false;
        monster._activeIdx = 0;
    }

    private void Think(Monster monster, ulong nowMs)
    {
        if (!monster.IsAlive)
        {
            Deactivate(monster);
            return;
        }

        // 기절한 동안은 움직이지도 공격하지도 않는다. 이동 실패로 대상을 놓치지 않도록 사고 자체를 건너뛴다.
        if (_zone.IsStunned(monster))
            return;

        switch (monster._state)
        {
            case MonsterState.Idle: ThinkIdle(monster, nowMs); break;
            case MonsterState.Chase: ThinkChase(monster, nowMs); break;
            case MonsterState.Return: ThinkReturn(monster); break;
        }
    }

    /// <summary>대기 상태 사고. 어그로 표에 대상이 있으면 추적을 시작하고, 없으면 선공 대상을 찾는다. 시야에 플레이어가 없으면 비활성화한다.</summary>
    private void ThinkIdle(Monster monster, ulong nowMs)
    {
        ZoneEntity? target = PickTarget(monster);

        if (target == null && monster.Template.AggroRange > 0.0f && nowMs >= monster._noAggroUntilMs)
        {
            target = FindAggroCandidate(monster);
            if (target != null)
            {
                monster.AddThreat(target.Id, 1);
                AlertAllies(monster, target.Id);
            }
        }

        if (target != null)
        {
            monster._state = MonsterState.Chase;
            monster._targetId = target.Id;
            monster._lastTargetPos.X = float.PositiveInfinity;      // 첫 추적은 반드시 이동 명령을 내리게 한다
            ThinkChase(monster, nowMs);
            return;
        }

        if (monster._nearPlayers == 0)
            Deactivate(monster);
    }

    /// <summary>
    /// 추적 상태 사고. 추적 한계를 넘었거나 대상이 없으면 복귀하고, 교전 거리면 정지해 공격하며,
    /// 아니면 대상 쪽으로 이동 명령을 낸다. 대상이 크게 움직였을 때만 이동 명령을 다시 낸다.
    /// </summary>
    private void ThinkChase(Monster monster, ulong nowMs)
    {
        // 시전 중에는 움직이지도 기본 공격을 하지도 않는다 (이동하면 시전이 취소된다).
        if (monster.IsCasting)
            return;

        bool rooted = _zone.IsRooted(monster);

        MonsterTemplate tmpl = monster.Template;

        if (Vec2.DistanceSq(monster.Pos, monster._home) > tmpl.LeashRange * tmpl.LeashRange)
        {
            EnterReturn(monster);
            return;
        }

        ZoneEntity? target = PickTarget(monster);
        if (target == null)
        {
            EnterReturn(monster);
            return;
        }

        // 보스는 페이즈/행동을 먼저 처리한다. 시전을 시작했으면 이번 사고는 여기까지.
        if (monster._boss != null && _boss != null && _boss.OnChase(monster, target, nowMs))
            return;

        // 몬스터 스킬은 사거리 안이고 재사용 대기가 끝났으면 기본 공격/추적보다 먼저 쓴다.
        if (tmpl.SkillId != 0 && monster._boss == null && TryUseSkill(monster, target, nowMs))
            return;

        float distSq = Vec2.DistanceSq(monster.Pos, target.Pos);
        float engage = tmpl.Stats.AttackRange * _config.EngageRatio;

        if (distSq <= engage * engage)
        {
            if (monster.IsMoving)
                _zone.StopMove(monster.Id);

            // 쿨다운 중에는 요청 자체를 건너뛴다.
            if (nowMs >= monster.NextAttackMs)
            {
                switch (_zone.RequestAttack(monster, target.Id))
                {
                    case AttackResult.TargetDead:
                    case AttackResult.TargetNotFound:
                    case AttackResult.InvalidTarget:
                    case AttackResult.TargetInvulnerable:
                        monster.RemoveThreat(target.Id);        // 공격할 수 없는 대상은 어그로에서 뺀다
                        break;
                    case AttackResult.NoLineOfSight:
                        // 사거리 안이지만 벽이 가로막고 있다. 시선이 트이는 곳으로 돌아가 보고, 갈 길이 없으면 포기한다.
                        if (!StepToward(monster, target.Pos))
                            GiveUpTarget(monster, target, nowMs);
                        break;
                    default:
                        break;
                }
            }
            return;
        }

        // 속박 중에는 다가갈 수 없다. 대상을 놓친 것이 아니므로 포기하지 않고 풀릴 때까지 제자리에서 기다린다.
        if (rooted)
            return;

        float repath = _config.RepathDistance;
        if (monster.IsMoving && Vec2.DistanceSq(target.Pos, monster._lastTargetPos) <= repath * repath)
            return;

        // 대상 바로 위가 아니라 사거리 안쪽 지점을 목적지로 잡는다.
        Vec2 dest = target.Pos;
        float dist = MathF.Sqrt(distSq);
        if (dist > 1e-3f)
        {
            float stand = tmpl.Stats.AttackRange * ChaseStandRatio;
            dest.X += (monster.Pos.X - dest.X) / dist * stand;
            dest.Y += (monster.Pos.Y - dest.Y) / dist * stand;
        }

        dest = _zone.ClampToBounds(dest);
        if (_zone.IsBlocked(dest))
            dest = target.Pos;      // 사거리 안쪽 지점이 장애물 안이면 대상 위치로 향한다

        if (StepToward(monster, dest))
            monster._lastTargetPos = target.Pos;
        else if (!monster.IsMoving)
            GiveUpTarget(monster, target, nowMs);       // 다가갈 수 없는 대상(벽에 막힘). 이동 중 거부는 다음 사고에서 다시 본다.
    }

    /// <summary>복귀 상태 사고. 스폰 지점에 도착하면 체력을 가득 채우고 대기 상태로 돌아간다.</summary>
    private void ThinkReturn(Monster monster)
    {
        if (monster.IsMoving)
            return;

        // 속박 중에는 복귀를 미룬다 (이동 실패를 순간이동으로 마무리하지 않는다).
        if (_zone.IsRooted(monster))
            return;

        if (Vec2.DistanceSq(monster.Pos, monster._home) < ArriveDistanceSq)
        {
            _zone.Heal(monster, monster.CombatStats.MaxHp);
            monster._state = MonsterState.Idle;

            if (monster._nearPlayers == 0)
                Deactivate(monster);
            return;
        }

        // 한 걸음(경유점)씩 복귀한다. 이동 명령이 거부되면(영역 밖 설정 오류, 길이 막힘) 순간이동으로 복귀를 마무리한다.
        if (!StepToward(monster, monster._home))
            _zone.Teleport(monster.Id, monster._home);
    }

    /// <summary>복귀 상태로 전환한다. 어그로를 비우고 스폰 지점으로 이동을 시작한다.</summary>
    private void EnterReturn(Monster monster)
    {
        monster._state = MonsterState.Return;
        monster.ClearAggro();

        _boss?.OnReset(monster);

        if (!StepToward(monster, monster._home) && !_zone.IsRooted(monster))
            _zone.Teleport(monster.Id, monster._home);
    }

    /// <summary>닿을 수 없는 대상을 포기한다. 어그로에서 빼고, 한동안 근접 선공으로 다시 잡지 않아 추적-복귀 반복을 막는다.</summary>
    private void GiveUpTarget(Monster monster, ZoneEntity target, ulong nowMs)
    {
        monster.RemoveThreat(target.Id);
        monster._noAggroUntilMs = nowMs + _config.PathRetryMs;
    }

    /// <summary>
    /// 몬스터를 목표 쪽으로 한 걸음 보낸다. 직선이 비어 있으면 목표로 곧장 가고, 막혀 있으면 우회 경로의 첫 경유점으로 간다.
    /// 경유점에 도착해 멈추면 다음 사고에서 다시 호출되어 다음 경유점으로 이어진다(경로를 저장하지 않는다).
    /// 경로가 없으면 PathRetryMs 동안은 탐색하지 않고 실패를 돌려준다.
    /// </summary>
    /// <returns>이동 명령을 냈으면 true</returns>
    private bool StepToward(Monster monster, Vec2 goal)
    {
        BlockMap? blockMap = _zone.BlockMap;
        if (blockMap == null || blockMap.IsSegmentClear(monster.Pos, goal))
            return _zone.MoveEntity(monster, goal);

        ulong nowMs = _zone.NowMs;
        if (nowMs < monster._noPathUntilMs)
            return false;

        if (_pathBudget == 0)
            return true;        // 이번 틱 탐색 예산을 다 썼다. 제자리에서 다음 사고(주기 뒤)에 다시 시도한다
        --_pathBudget;

        if (!blockMap.FindPath(monster.Pos, goal, _pathScratch, _config.PathMaxNodes) || _pathScratch.Count == 0)
        {
            monster._noPathUntilMs = nowMs + _config.PathRetryMs;
            return false;
        }

        return _zone.MoveEntity(monster, _pathScratch[0]);
    }

    /// <summary>
    /// 어그로 표에서 추적할 대상을 고른다. 죽었거나 존에 없는 항목은 표에서 지운다.
    /// 위협이 가장 큰 대상을 고르되, 현재 대상보다 10%를 넘게 높을 때만 대상을 바꿔 잦은 갈아타기를 막는다.
    /// </summary>
    /// <returns>대상 엔티티, 없으면 null (이때 monster._targetId는 0)</returns>
    private ZoneEntity? PickTarget(Monster monster)
    {
        ZoneEntity? best = null;
        long bestThreat = -1;
        ZoneEntity? current = null;
        long currentThreat = 0;
        ZoneEntity? taunter = null;
        bool taunting = monster._tauntUntilMs > _zone.NowMs;

        for (int i = 0; i < monster._aggroCount;)
        {
            AggroEntry entry = monster._aggro[i];
            ZoneEntity? entity = _zone.FindEntityBase(entry.EntityId);

            if (entity == null || !entity.IsAlive)
            {
                if (monster._targetId == entry.EntityId)
                    monster._targetId = 0;

                monster._aggro[i] = monster._aggro[--monster._aggroCount];
                continue;
            }

            if (entry.EntityId == monster._targetId)
            {
                current = entity;
                currentThreat = entry.Threat;
            }

            if (taunting && entry.EntityId == monster._tauntId)
                taunter = entity;

            if (entry.Threat > bestThreat)
            {
                best = entity;
                bestThreat = entry.Threat;
            }
            ++i;
        }

        if (best == null)
        {
            monster._targetId = 0;
            return null;
        }

        if (taunter != null)
            best = taunter;     // 도발 중에는 위협 순위와 갈아타기 억제를 무시하고 도발한 대상을 쫓는다
        else if (current != null && !ReferenceEquals(current, best) && bestThreat <= currentThreat + currentThreat / 10)
            best = current;

        monster._targetId = best.Id;
        return best;
    }

    /// <summary>선공 범위 안에서 가장 가까운 공격 가능한 플레이어를 찾는다. (생존, 무적 아님) AOI 관찰자만 훑으므로 플레이어 엔티티는 IsAoiWatcher()가 true(기본값)여야 한다.</summary>
    private ZoneEntity? FindAggroCandidate(Monster monster)
        => _zone.FindNearestLivePlayer(monster.Pos, monster.Template.AggroRange);

    /// <summary>
    /// 무리 가세: source 주변의 대기 중인 몬스터가 같은 대상을 노리게 한다.
    /// source의 AssistRange 안에서, 대기 상태이고 어그로 표가 비어 있으며 보스가 아닌 살아 있는 몬스터만 깨운다.
    /// 한 번에 12마리까지만 깨워 밀집 지역에서 한 틱 비용이 튀지 않게 하며, 깨어난 몬스터는 다시 다른 몬스터를 깨우지 않는다.
    /// </summary>
    private void AlertAllies(Monster source, ulong targetId)
    {
        float range = source.Template.AssistRange;
        if (!(range > 0.0f) || source._boss != null)
            return;

        // 방문 상태를 필드로 넘겨 호출마다 클로저를 만들지 않는다. 콜백 안에서 다시 AlertAllies가 불리지 않으므로 필드로 충분하다.
        _alertSource = source;
        _alertTargetId = targetId;
        _alertNowMs = _zone.NowMs;
        _alertCount = 0;
        try
        {
            _zone.ForEachInRadius(source.Pos, range, _alertFn);
        }
        finally
        {
            _alertSource = null;
        }
    }

    private const uint MaxAlertPerCall = 12;

    private void AlertVisit(ZoneEntity e)
    {
        if (_alertCount >= MaxAlertPerCall || ReferenceEquals(e, _alertSource) || !IsAiMonster(e) || !e.IsAlive)
            return;

        Monster ally = (Monster)e;
        if (ally._boss != null || ally._state != MonsterState.Idle || ally.AggroCount != 0 || _alertNowMs < ally._noAggroUntilMs)
            return;

        ally.AddThreat(_alertTargetId, 1);
        Activate(ally);
        ally._nextThinkMs = _alertNowMs;
        ++_alertCount;
    }

    /// <summary>몬스터 스킬을 쓴다. 재사용 대기가 끝났고 대상이 스킬 사거리 안일 때만 시전한다. Single은 대상을, Area는 대상 위치를, Self는 자기 위치를, Cone/Line은 대상 쪽 방향을 겨냥한다.</summary>
    /// <returns>시전을 시작했거나 즉시 발동했으면 true</returns>
    private bool TryUseSkill(Monster monster, ZoneEntity target, ulong nowMs)
    {
        if (_skills == null || nowMs < monster._skillReadyAtMs)
            return false;

        SkillTemplate? skill = _skills.FindSkill(monster.Template.SkillId);
        if (skill == null)
            return false;

        float distSq = Vec2.DistanceSq(monster.Pos, target.Pos);
        float reach = skill.Shape == ESkillShape.Self ? skill.Radius * 0.8f : skill.Range;
        if (distSq > reach * reach)
            return false;

        Vec2 center = target.Pos;
        ulong targetId = 0;
        if (skill.Shape == ESkillShape.Self)
            center = monster.Pos;
        else if (skill.Shape == ESkillShape.Single)
            targetId = target.Id;

        return _zone.CastSkill(monster, skill, targetId, center, ref monster._skillReadyAtMs) == SkillResult.Ok;
    }

    // ── 존 훅 전달 ───────────────────────────────────────

    /// <summary>몬스터가 피해를 입었을 때 호출한다. 플레이어가 준 피해만큼 위협을 쌓고 즉시 반응하게 한다. 복귀 중에는 어그로를 받지 않는다.</summary>
    public void OnDamaged(ZoneEntity? attacker, ZoneEntity victim, int damage)
    {
        if (attacker == null || attacker.Type != EntityType.Player || !IsAiMonster(victim))
            return;

        Monster monster = (Monster)victim;
        if (monster._state == MonsterState.Return)
            return;

        // 스킬이 위협 배율을 정했으면(ThreatBonusPercent) 피해량에 곱한다. 배율이 0 이하면 위협도 반응도 없다.
        int percent = _zone.CurrentThreatPercent;
        if (percent <= 0)
            return;
        long threat = percent == 100 ? damage : Math.Max(1L, (long)damage * percent / 100);

        bool firstBlood = monster.AggroCount == 0;
        monster.AddThreat(attacker.Id, threat);
        Activate(monster);
        monster._nextThinkMs = _zone.NowMs;

        // 처음 맞은 순간에만 주변에 알린다 (맞을 때마다 알리면 비용만 든다).
        if (firstBlood)
            AlertAllies(monster, attacker.Id);
    }

    /// <summary>
    /// 스킬이 엔티티에게 적중한 직후 호출한다. TauntMs가 있는 스킬을 플레이어가 AI 몬스터에게 쓰면 도발한다.
    /// 시전자의 위협을 표의 최고치 위로 올리고, TauntMs 동안은 위협 순위와 상관없이 시전자를 대상으로 삼게 한다.
    /// 보스(자기 행동표로 대상을 정한다)와 복귀 중인 몬스터는 도발에 면역이다.
    /// </summary>
    public void OnSkillHit(ZoneEntity caster, SkillTemplate skill, ZoneEntity victim)
    {
        if (skill.TauntMs == 0 || caster.Type != EntityType.Player || !caster.IsAlive || !IsAiMonster(victim) || !victim.IsAlive)
            return;

        Monster monster = (Monster)victim;
        if (monster._boss != null || monster._state == MonsterState.Return)
            return;

        long top = 0;
        for (int i = 0; i < monster._aggroCount; ++i)
        {
            if (monster._aggro[i].EntityId != caster.Id && monster._aggro[i].Threat > top)
                top = monster._aggro[i].Threat;
        }

        long mine = monster.GetThreat(caster.Id);

        bool firstBlood = monster.AggroCount == 0;
        if (mine <= top)
            monster.AddThreat(caster.Id, top - mine + 1);

        monster._tauntId = caster.Id;
        monster._tauntUntilMs = _zone.NowMs + skill.TauntMs;
        Activate(monster);
        monster._nextThinkMs = _zone.NowMs;

        if (firstBlood)
            AlertAllies(monster, caster.Id);
    }

    /// <summary>
    /// 플레이어가 다른 엔티티를 치유해 실제로 체력이 회복됐을 때 호출한다. 치유받은 대상이 어그로 표에 있는 몬스터마다
    /// 회복량의 HealThreatPercent만큼 치유자에게 위협을 더한다(치유자도 몬스터의 표적이 된다).
    /// </summary>
    public void OnHealedBy(ZoneEntity source, ZoneEntity target, int healed)
    {
        if (source.Type != EntityType.Player || _config.HealThreatPercent == 0 || !(_config.HealThreatRange > 0.0f) || healed <= 0)
            return;

        _healSource = source;
        _healTargetId = target.Id;
        _healThreat = Math.Max(1L, (long)healed * _config.HealThreatPercent / 100);
        try
        {
            _zone.ForEachInRadius(target.Pos, _config.HealThreatRange, _healFn);
        }
        finally
        {
            _healSource = null;
        }
    }

    private void HealVisit(ZoneEntity e)
    {
        if (!IsAiMonster(e) || !e.IsAlive)
            return;

        Monster monster = (Monster)e;
        if (monster._state == MonsterState.Return)
            return;

        if (monster.GetThreat(_healTargetId) > 0)       // 표에 있는 항목의 위협은 항상 1 이상이다
            monster.AddThreat(_healSource!.Id, _healThreat);
    }

    /// <summary>몬스터가 죽었을 때 호출한다. AI 목록에서 빼고 어그로를 비운다.</summary>
    public void OnDied(ZoneEntity victim)
    {
        if (!IsAiMonster(victim))
            return;

        Monster monster = (Monster)victim;
        monster.ClearAggro();
        monster._state = MonsterState.Idle;
        Deactivate(monster);

        _boss?.OnDied(monster);

        // 소환수는 부활하지 않으므로 시체를 잠시 두었다 치운다.
        if (monster._summoned)
        {
            ulong id = monster.Id;
            _zone.ScheduleAfter(BossSystem.AddCorpseMs, zone => ((FieldZoneBase)zone).RemoveEntity(id));
        }
    }

    /// <summary>몬스터가 부활했을 때 호출한다. 복귀 지점을 새 위치로 갱신하고, 시야에 플레이어가 있으면 AI를 깨운다.</summary>
    public void OnRespawned(ZoneEntity entity)
    {
        if (!IsAiMonster(entity))
            return;

        Monster monster = (Monster)entity;
        monster._home = monster.Pos;
        monster._state = MonsterState.Idle;
        monster.ClearAggro();

        _boss?.OnReset(monster);

        if (monster._nearPlayers > 0)
            Activate(monster);
    }

    /// <summary>몬스터가 존에서 제거되기 직전에 호출한다. AI 활성 목록에서 빼서 제거된 엔티티가 남지 않게 한다.</summary>
    public void OnRemoved(ZoneEntity entity)
    {
        if (!IsAiMonster(entity))
            return;

        Monster removed = (Monster)entity;
        Deactivate(removed);

        _boss?.OnRemoved(removed);

        if (_monsterCount > 0)
            --_monsterCount;
    }

    // ── 존 정책 위임 ─────────────────────────────────────

    /// <summary>이 시스템이 발급한 AI 몬스터인지 확인한다. (엔티티 종류가 Monster이고 ID가 몬스터 대역)</summary>
    public bool IsAiMonster(ZoneEntity entity) => entity.Type == EntityType.Monster && (entity.Id & MonsterIds.Base) != 0;

    /// <summary>AI 몬스터의 부활 지연(템플릿의 RespawnMs). AI 몬스터가 아니거나 소환수면 FieldZoneBase.NoRespawn.</summary>
    public uint GetRespawnDelayMs(ZoneEntity entity)
    {
        if (!IsAiMonster(entity))
            return FieldZoneBase.NoRespawn;

        Monster monster = (Monster)entity;
        return monster._summoned ? FieldZoneBase.NoRespawn : monster.Template.RespawnMs;
    }

    /// <summary>AI 몬스터의 부활 위치를 정한다. 스폰 중심 반경 안의 무작위 지점이며 존 영역으로 보정한다.</summary>
    /// <returns>AI 몬스터면 true (아니면 pos를 건드리지 않음)</returns>
    public bool GetRespawnPos(ZoneEntity entity, ref Vec2 pos)
    {
        if (!IsAiMonster(entity))
            return false;

        Monster monster = (Monster)entity;
        pos = PickFreePoint(monster.Anchor, monster.SpawnRadius);
        return true;
    }

    /// <summary>원 안에서 균등하게 분포한 무작위 지점을 고른다. (반경 sqrt 보정)</summary>
    private Vec2 RandomPointInDisc(Vec2 center, float radius)
    {
        if (!(radius > 0.0f))
            return center;

        const double TwoPi = 6.283185307179586;
        double u1 = _rng.NextDouble();
        double u2 = _rng.NextDouble();
        double r = (double)radius * Math.Sqrt(u1);
        double theta = TwoPi * u2;

        return new Vec2
        {
            X = center.X + (float)(r * Math.Cos(theta)),
            Y = center.Y + (float)(r * Math.Sin(theta)),
        };
    }

    /// <summary>스폰 중심 반경 안에서 장애물 밖인 무작위 지점을 고른다. 영역 안으로 보정하며, 여러 번 시도해도 못 찾으면 중심을 쓴다.</summary>
    internal Vec2 PickFreePoint(Vec2 center, float radius)
    {
        const int MaxTries = 16;
        for (int i = 0; i < MaxTries; ++i)
        {
            Vec2 p = _zone.ClampToBounds(RandomPointInDisc(center, radius));
            if (_zone.IsWalkable(p))
                return p;
        }
        return _zone.ClampToBounds(center);
    }
}
