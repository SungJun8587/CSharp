// GameDataLoader.cs : 게임 데이터(JSON) 폴더에서 영웅/몬스터/보스/차단 맵을 읽는다.
using System;
using System.Collections.Generic;
using System.IO;
using Core.Util;
using Game.Generated;
using Game.ZoneCore;

namespace Game.ZoneServer;

/// <summary>
/// XlsxConverter가 만든 JSON을 읽어 ZoneCore 데이터 객체로 옮긴다.
/// 파일 형식(알 수 없는 키/enum 이름)은 JsonTableLoader이, 값 범위는 생성된 Table.Validate가, 서로 간의 참조는 각 데이터의 Validate가 검사한다.
/// 실패하면 원인을 로그로 남기고 null/false를 돌려준다.
/// </summary>
public static class GameDataLoader
{
    /// <summary>차단 맵 셀 한 변의 길이(서버와 봇이 같은 값을 써야 같은 판정이 된다).</summary>
    public const float BlockCellSize = BlockMap.DefaultCellSize;

    private static bool ReadTable<TTable>(string dir, string fileName, out TTable? table, Action<TTable, List<string>> validate) where TTable : class
    {
        string path = Path.Combine(dir, fileName);
        if (!JsonTableLoader.TryLoadFile(path, out table, out string error))
        {
            LogManager.Error("[GameData] load failed. {0}", error);
            return false;
        }

        List<string> errors = new List<string>();
        validate(table!, errors);
        if (errors.Count > 0)
        {
            foreach (string e in errors)
                LogManager.Error("[GameData] {0}: {1}", path, e);
            table = null;
            return false;
        }
        return true;
    }

    private enum ReadResult { Missing, Ok, Failed }

    /// <summary>파일이 없으면 Missing(오류 아님), 있는데 읽기/검증에 실패하면 Failed.</summary>
    private static ReadResult ReadOptional<TTable>(string dir, string fileName, out TTable? table, Action<TTable, List<string>> validate) where TTable : class
    {
        table = null;
        if (!File.Exists(Path.Combine(dir, fileName)))
            return ReadResult.Missing;
        return ReadTable(dir, fileName, out table, validate) ? ReadResult.Ok : ReadResult.Failed;
    }

    /// <summary>템플릿 등록 결과를 확인한다. 중복이면 로그를 남기고 false.</summary>
    private static bool Registered(bool added, string kind, uint id)
    {
        if (!added)
            LogManager.Error("[GameData] duplicate {0} id. id={1}", kind, id);
        return added;
    }

    /// <summary>Hero.json(필수) + Status.json/Skill.json/HeroSkill.json(있으면)을 읽는다.</summary>
    public static HeroData? LoadHeroes(string dir)
    {
        if (!ReadTable(dir, HeroRow.FileName, out HeroTable? heroes, (t, e) => t.Validate(e)))
            return null;

        HeroData data = new HeroData();
        foreach (HeroRow r in heroes!.Hero)
        {
            HeroTemplate h = new HeroTemplate
            {
                Id = r.Id,
                Name = r.Name,
                Speed = (float)r.Speed,
            };
            h.Stats.MaxHp = r.MaxHp;
            h.Stats.Attack = r.Attack;
            h.Stats.Defense = r.Defense;
            h.Stats.AttackRange = (float)r.AttackRange;
            h.Stats.AttackIntervalMs = r.AttackIntervalMs;
            h.Stats.MaxMp = r.MaxMp;
            h.Stats.HpRegen = r.HpRegen;
            h.Stats.MpRegen = r.MpRegen;
            if (!Registered(data.AddHero(h), "hero", r.Id))
                return null;
        }

        ReadResult statusRead = ReadOptional(dir, StatusRow.FileName, out StatusTable? statuses, (t, e) => t.Validate(e));
        if (statusRead == ReadResult.Failed)
            return null;
        if (statusRead == ReadResult.Ok)
        {
            foreach (StatusRow r in statuses!.Status)
            {
                StatusTemplate t = new StatusTemplate
                {
                    Id = r.Id,
                    Name = r.Name,
                    Kind = r.Kind,
                    Target = r.Target,
                    DurationMs = r.DurationMs,
                    TickMs = r.TickMs,
                    DamagePercent = r.DamagePercent,
                    FlatDamage = r.FlatDamage,
                    AttackPercent = r.AttackPercent,
                    DefensePercent = r.DefensePercent,
                    SpeedPercent = r.SpeedPercent,
                    HealPercent = r.HealPercent,
                    FlatHeal = r.FlatHeal,
                };
                if (!Registered(data.AddStatus(t), "status", r.Id))
                    return null;
            }
        }

        ReadResult skillRead = ReadOptional(dir, SkillRow.FileName, out SkillTable? skills, (t, e) => t.Validate(e));
        if (skillRead == ReadResult.Failed)
            return null;
        if (skillRead == ReadResult.Ok)
        {
            foreach (SkillRow r in skills!.Skill)
            {
                SkillTemplate s = new SkillTemplate
                {
                    Id = r.Id,
                    Name = r.Name,
                    Shape = r.Shape,
                    DamagePercent = r.DamagePercent,
                    FlatDamage = r.FlatDamage,
                    Range = (float)r.Range,
                    Radius = (float)r.Radius,
                    CooldownMs = r.CooldownMs,
                    MaxTargets = r.MaxTargets,
                    CastTimeMs = r.CastTimeMs,
                    ProjectileSpeed = (float)r.ProjectileSpeed,
                    StatusId = r.StatusId,
                    Target = r.Target,
                    HealPercent = r.HealPercent,
                    FlatHeal = r.FlatHeal,
                    Angle = (float)r.Angle,
                    MpCost = r.MpCost,
                    ThreatBonusPercent = r.ThreatBonusPercent,
                    TauntMs = r.TauntMs,
                    KnockbackDistance = (float)r.KnockbackDistance,
                };
                if (!Registered(data.AddSkill(s), "skill", r.Id))
                    return null;
            }

            ReadResult linkRead = ReadOptional(dir, HeroSkillRow.FileName, out HeroSkillTable? links, (t, e) => t.Validate(e));
            if (linkRead == ReadResult.Failed)
                return null;
            if (linkRead == ReadResult.Ok)
            {
                foreach (HeroSkillRow r in links!.HeroSkill)
                {
                    if ((r.Price > 0 && r.PriceCurrencyId == 0) || !data.AssignSkill(r.HeroId, r.SkillId, r.UnlockLevel, r.PriceCurrencyId, r.Price, r.Slot))
                    {
                        LogManager.Error("[GameData] hero-skill link rejected (unknown hero/skill, duplicate, more than {0} skills, a price without a currency, a shop skill with a fixed slot, or a bad/duplicate fixed slot). heroId={1}, skillId={2}",
                            GameConsts.MaxHeroSkills, r.HeroId, r.SkillId);
                        return null;
                    }
                }
            }
            else
            {
                LogManager.Warning("[GameData] {0} has skills but {1} is missing: no hero can use them.", SkillRow.FileName, HeroSkillRow.FileName);
            }
        }

        if (!data.Validate(out string error))
        {
            LogManager.Error("[GameData] hero validation failed. {0}", error);
            return null;
        }

        LogManager.Info("[GameData] loaded. heroes={0}, skills={1}, statuses={2}", data.HeroCount, data.SkillCount, data.StatusCount);
        return data;
    }

    /// <summary>Monster.json + Spawn.json(둘 다 필수)을 읽는다.</summary>
    public static MonsterData? LoadMonsters(string dir)
    {
        if (!ReadTable(dir, MonsterRow.FileName, out MonsterTable? monsters, (t, e) => t.Validate(e)))
            return null;
        if (!ReadTable(dir, SpawnRow.FileName, out SpawnTable? spawns, (t, e) => t.Validate(e)))
            return null;

        MonsterData data = new MonsterData();
        foreach (MonsterRow r in monsters!.Monster)
        {
            MonsterTemplate t = new MonsterTemplate
            {
                Id = r.Id,
                Name = r.Name,
                Speed = (float)r.Speed,
                AggroRange = (float)r.AggroRange,
                LeashRange = (float)r.LeashRange,
                RespawnMs = r.RespawnMs,
                AssistRange = (float)r.AssistRange,
                SkillId = r.SkillId,
            };
            t.Stats.MaxHp = r.MaxHp;
            t.Stats.Attack = r.Attack;
            t.Stats.Defense = r.Defense;
            t.Stats.AttackRange = (float)r.AttackRange;
            t.Stats.AttackIntervalMs = r.AttackIntervalMs;
            if (!Registered(data.AddMonster(t), "monster", r.Id))
                return null;
        }

        foreach (SpawnRow r in spawns!.Spawn)
        {
            data.AddSpawn(new SpawnEntry
            {
                ZoneId = r.ZoneId,
                MonsterId = r.MonsterId,
                Count = r.Count,
                X = (float)r.X,
                Y = (float)r.Y,
                Radius = (float)r.Radius,
            });
        }

        if (!data.Validate(out string error))
        {
            LogManager.Error("[GameData] validation failed. {0}", error);
            return null;
        }

        LogManager.Info("[GameData] loaded. monsters={0}, spawns={1}", data.MonsterCount, data.Spawns.Count);
        return data;
    }

    /// <summary>Drop.json을 읽는다. 파일이 없으면 드롭 없는 데이터(빈 테이블)를 돌려준다. 있는데 형식이나 몬스터 참조가 틀리면 null.</summary>
    public static DropData? LoadDrops(string dir, MonsterData monsters)
    {
        DropData data = new DropData();
        ReadResult read = ReadOptional(dir, DropRow.FileName, out DropTable? drops, (t, e) => t.Validate(e));
        if (read == ReadResult.Missing)
            return data;
        if (read == ReadResult.Failed)
            return null;

        foreach (DropRow r in drops!.Drop)
            data.Add(r.MonsterId, new DropEntry { ItemId = r.ItemId, ChancePermille = r.ChancePermille, MinCount = r.MinCount, MaxCount = r.MaxCount });

        if (!data.Validate(monsters, out string error))
        {
            LogManager.Error("[GameData] drop validation failed. {0}", error);
            return null;
        }

        LogManager.Info("[GameData] loaded. drops={0}", data.EntryCount);
        return data;
    }

    /// <summary>
    /// Level.json(레벨 표)과 MonsterExp.json(몬스터 경험치)을 읽는다. 둘 다 선택이며, 없으면 레벨 없는 데이터(MaxLevel 0)를 돌려준다.
    /// 몬스터 경험치의 몬스터 ID가 Monster 표에 없으면 실패한다. 형식이 틀리면 null.
    /// </summary>
    public static LevelData? LoadLevels(string dir, MonsterData monsters)
    {
        LevelData data = new LevelData();
        ReadResult lv = ReadOptional(dir, LevelRow.FileName, out LevelTable? levels, (t, e) => { });
        if (lv == ReadResult.Failed)
            return null;
        if (lv == ReadResult.Ok)
        {
            foreach (LevelRow r in levels!.Level)
            {
                if (!data.AddLevel(new LevelData.Row { Level = r.Level, ExpToNext = r.ExpToNext, StatPercent = r.StatPercent }))
                {
                    LogManager.Error("[GameData] level rows must be consecutive from 1. level={0}", r.Level);
                    return null;
                }
            }
            if (!data.Validate(out string error))
            {
                LogManager.Error("[GameData] level validation failed. {0}", error);
                return null;
            }
        }

        ReadResult ex = ReadOptional(dir, MonsterExpRow.FileName, out MonsterExpTable? exps, (t, e) => t.Validate(e));
        if (ex == ReadResult.Failed)
            return null;
        if (ex == ReadResult.Ok)
        {
            foreach (MonsterExpRow r in exps!.MonsterExp)
            {
                if (monsters.FindMonster(r.MonsterId) == null)
                {
                    LogManager.Error("[GameData] MonsterExp: unknown monster id. id={0}", r.MonsterId);
                    return null;
                }
                if (!data.AddMonsterExp(r.MonsterId, r.Exp))
                {
                    LogManager.Error("[GameData] MonsterExp: duplicate monster id. id={0}", r.MonsterId);
                    return null;
                }
            }
        }

        LogManager.Info("[GameData] loaded. levels={0}, monsterExp={1}", data.MaxLevel, data.MonsterExpCount);
        return data;
    }

    /// <summary>
    /// Currency.json(재화 정의)과 MonsterCurrency.json(몬스터 처치 보상)을 읽는다. 둘 다 선택이며, 없으면 재화 없는 데이터를 돌려준다.
    /// 보상의 몬스터 ID는 Monster 표에 있어야 한다. 형식이 틀리거나 검증에 실패하면 null.
    /// </summary>
    public static CurrencyData? LoadCurrencies(string dir, MonsterData monsters)
    {
        CurrencyData data = new CurrencyData();
        ReadResult cr = ReadOptional(dir, CurrencyRow.FileName, out CurrencyTable? currencies, (t, e) => { });
        if (cr == ReadResult.Failed)
            return null;
        if (cr == ReadResult.Ok)
        {
            foreach (CurrencyRow r in currencies!.Currency)
                if (!data.AddCurrency(new CurrencyData.Currency { Id = r.Id, Name = r.Name, MaxAmount = r.MaxAmount }))
                {
                    LogManager.Error("[GameData] Currency: id is zero or duplicated. id={0}", r.Id);
                    return null;
                }
        }

        ReadResult mr = ReadOptional(dir, MonsterCurrencyRow.FileName, out MonsterCurrencyTable? rewards, (t, e) => { });
        if (mr == ReadResult.Failed)
            return null;
        if (mr == ReadResult.Ok)
        {
            foreach (MonsterCurrencyRow r in rewards!.MonsterCurrency)
            {
                if (monsters.FindMonster(r.MonsterId) == null)
                {
                    LogManager.Error("[GameData] MonsterCurrency: unknown monster id. id={0}", r.MonsterId);
                    return null;
                }
                data.AddReward(new CurrencyData.Reward { MonsterId = r.MonsterId, CurrencyId = r.CurrencyId, MinAmount = r.MinAmount, MaxAmount = r.MaxAmount, ChancePermille = r.ChancePermille });
            }
        }

        if (!data.Validate(out string error))
        {
            LogManager.Error("[GameData] currency validation failed. {0}", error);
            return null;
        }

        LogManager.Info("[GameData] loaded. currencies={0}, monsterRewards={1}", data.CurrencyCount, data.RewardCount);
        return data;
    }

    /// <summary>Boss.json/BossPhase.json/BossAction.json을 읽는다. Boss.json이 없으면 보스 없는 데이터를 돌려준다.</summary>
    public static BossData? LoadBosses(string dir, MonsterData monsters, HeroData skills)
    {
        BossData data = new BossData();
        if (!File.Exists(Path.Combine(dir, BossRow.FileName)))
        {
            LogManager.Info("[GameData] {0} not found. no bosses. dir={1}", BossRow.FileName, dir);
            return data;
        }

        if (!ReadTable(dir, BossRow.FileName, out BossTable? bossRows, (t, e) => t.Validate(e))
            || !ReadTable(dir, BossPhaseRow.FileName, out BossPhaseTable? phaseRows, (t, e) => t.Validate(e))
            || !ReadTable(dir, BossActionRow.FileName, out BossActionTable? actionRows, (t, e) => t.Validate(e)))
            return null;

        List<BossTemplate> bosses = new List<BossTemplate>();
        foreach (BossRow r in bossRows!.Boss)
        {
            bosses.Add(new BossTemplate
            {
                Id = r.Id,
                MonsterId = r.MonsterId,
                Name = r.Name,
                EnrageAfterMs = r.EnrageAfterMs,
                EnrageStatusId = r.EnrageStatusId,
            });
        }

        BossTemplate? FindBoss(uint id)
        {
            foreach (BossTemplate b in bosses)
                if (b.Id == id)
                    return b;
            return null;
        }

        static BossPhaseTemplate? FindPhase(BossTemplate boss, uint phase)
        {
            foreach (BossPhaseTemplate p in boss.Phases)
                if (p.Phase == phase)
                    return p;
            return null;
        }

        foreach (BossPhaseRow r in phaseRows!.BossPhase)
        {
            BossTemplate? boss = FindBoss(r.BossId);
            if (boss == null)
            {
                LogManager.Error("[GameData] {0}: unknown BossId. bossId={1}, phase={2}", BossPhaseRow.FileName, r.BossId, r.Phase);
                return null;
            }
            if (FindPhase(boss, r.Phase) != null)
            {
                LogManager.Error("[GameData] {0}: duplicate phase. bossId={1}, phase={2}", BossPhaseRow.FileName, r.BossId, r.Phase);
                return null;
            }
            boss.Phases.Add(new BossPhaseTemplate
            {
                Phase = r.Phase,
                HpBelowPercent = r.HpBelowPercent,
                EnterStatusId = r.EnterStatusId,
                EnterHealPercent = r.EnterHealPercent,
                TransitionMs = r.TransitionMs,
            });
        }

        foreach (BossTemplate boss in bosses)
            boss.Phases.Sort((a, b) => a.Phase.CompareTo(b.Phase));

        foreach (BossActionRow r in actionRows!.BossAction)
        {
            BossTemplate? boss = FindBoss(r.BossId);
            BossPhaseTemplate? phase = boss != null ? FindPhase(boss, r.Phase) : null;
            if (phase == null)
            {
                LogManager.Error("[GameData] {0}: unknown BossId/Phase. bossId={1}, phase={2}, seq={3}", BossActionRow.FileName, r.BossId, r.Phase, r.Seq);
                return null;
            }
            phase.Actions.Add(new BossActionTemplate
            {
                Seq = r.Seq,
                Trigger = r.Trigger,
                Type = r.Type,
                Target = r.Target,
                SkillId = r.SkillId,
                MonsterId = r.MonsterId,
                Count = r.Count,
                SpreadRadius = (float)r.SpreadRadius,
                DelayMs = r.DelayMs,
            });
        }

        foreach (BossTemplate boss in bosses)
        {
            foreach (BossPhaseTemplate phase in boss.Phases)
            {
                // 진입 행동(OnEnter)을 앞에, 그 안에서는 Seq 순서로 둔다. List.Sort는 불안정하므로 (키, Seq, 원래 위치)로 비교한다.
                List<(BossActionTemplate a, int idx)> tmp = new List<(BossActionTemplate, int)>();
                for (int i = 0; i < phase.Actions.Count; ++i)
                    tmp.Add((phase.Actions[i], i));
                tmp.Sort((x, y) =>
                {
                    int kx = x.a.Trigger == EBossTrigger.OnEnter ? 0 : 1;
                    int ky = y.a.Trigger == EBossTrigger.OnEnter ? 0 : 1;
                    if (kx != ky) return kx.CompareTo(ky);
                    int c = x.a.Seq.CompareTo(y.a.Seq);
                    return c != 0 ? c : x.idx.CompareTo(y.idx);
                });
                for (int i = 0; i < tmp.Count; ++i)
                    phase.Actions[i] = tmp[i].a;

                phase.LoopStart = 0;
                while (phase.LoopStart < phase.Actions.Count && phase.Actions[phase.LoopStart].Trigger == EBossTrigger.OnEnter)
                    ++phase.LoopStart;
            }

            if (!data.AddBoss(boss))
            {
                LogManager.Error("[GameData] duplicate boss id or monster id. bossId={0}, monsterId={1}", boss.Id, boss.MonsterId);
                return null;
            }
        }

        if (!data.Validate(monsters, skills, out string error))
        {
            LogManager.Error("[GameData] boss validation failed. {0}", error);
            return null;
        }

        LogManager.Info("[GameData] loaded. bosses={0}", data.BossCount);
        return data;
    }

    /// <summary>Block.json에서 이 존의 차단 맵을 만든다. 파일이 없거나 이 존의 행이 없으면 map은 null이고 true.</summary>
    public static bool LoadBlockMap(string dir, uint zoneId, float minX, float minY, float maxX, float maxY, out BlockMap? map)
    {
        map = null;
        string file = Path.Combine(dir, BlockRow.FileName);
        if (!File.Exists(file))
        {
            LogManager.Info("[GameData] {0} not found. no obstacles. dir={1}", BlockRow.FileName, dir);
            return true;
        }
        return BlockMapFile.Load(file, zoneId, minX, minY, maxX, maxY, BlockCellSize, out map);
    }
}
