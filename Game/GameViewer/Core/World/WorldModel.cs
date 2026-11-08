//***************************************************************************
// WorldModel.cs : 서버 패킷을 받아 엔티티/연출/로그를 갱신하는 클라이언트 월드 상태 (UI 비의존).
// 한 스레드(UI/게임 루프)에서만 호출한다. 시간은 호출자가 넘기는 단조 증가 ms(now)를 쓴다.
//***************************************************************************
using System;
using System.Collections.Generic;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;

namespace GameViewer.Core.World
{
    public sealed class WorldModel
    {
        public const int MaxLogLines = 200;

        public readonly Dictionary<ulong, Entity> Entities = new Dictionary<ulong, Entity>();
        public readonly List<ViewEffect> Effects = new List<ViewEffect>();
        public readonly List<LogLine> Log = new List<LogLine>();
        public GameData Data;

        public ulong SelfId;
        public Entity Self;

        // 통계(화면 표시용)
        public long PacketCount, UnknownPacketCount, MalformedPacketCount;
        public long DamageDealtBySelf, DamageTakenBySelf, KillsBySelf, SelfDeaths;
        public readonly KillStreak Streak = new KillStreak();
        private readonly HashSet<ulong> _bossWarned = new HashSet<ulong>();      // 이미 경고를 낸 보스(죽으면 빠져서 다시 나오면 또 경고)
        public long TotalDeaths, TotalRespawns, TotalDamageEvents;
        public float LastCorrectX, LastCorrectY;
        public long LastCorrectMs = -100000;
        public long ShakeStartMs = -100000; public int ShakeMs; public float ShakeAmp;      // 화면 흔들림 (px)
        public CorrectPosReason LastCorrectReason;

        /// <summary>내가 스킬을 쓴 것이 확인되면 호출된다(쿨다운 표시용). 인자: skillId, castStartedNow(시전 시작이면 true), now.</summary>
        public event Action<uint, bool, long> SelfSkillUsed;

        /// <summary>내 스킬 요청에 대한 서버의 답이 왔다(시전 시작/발동/투사체 발사/거부/취소). 요청이 처리 중인 동안 다른 요청을 보내지 않기 위한 신호다.</summary>
        public event Action<long> SelfSkillResponded;

        private readonly Dictionary<uint, ViewEffect> _projectiles = new Dictionary<uint, ViewEffect>();
        private readonly Dictionary<ulong, long> _lastSkillMs = new Dictionary<ulong, long>();

        public WorldModel(GameData data, ulong selfId)
        {
            Data = data ?? new GameData();
            SelfId = selfId;
        }

        // ------------------------------------------------------------------ 외부 API

        /// <summary>존 입장 응답(스폰 위치) 이후 내 엔티티를 만든다. 곧 ZcSpawnNotify 가 오면 서버 값으로 덮인다.</summary>
        public void InitSelf(float x, float y, uint heroId, long now)
        {
            Entity e = new Entity();
            e.Id = SelfId; e.Type = (byte)EntityType.Player; e.TemplateId = heroId;
            e.X = x; e.Y = y; e.DestX = x; e.DestY = y;
            HeroRow h = Data.Hero(heroId);
            if (h != null) { e.MaxHp = h.MaxHp; e.Hp = h.MaxHp; e.Speed = (float)h.Speed; }
            Entities[SelfId] = e;
            Self = e;
            AddLog(now, 0, "입장: 스폰 (" + x.ToString("0.0") + ", " + y.ToString("0.0") + ")");
        }

        public void Clear()
        {
            Entities.Clear(); Effects.Clear(); _projectiles.Clear(); _lastSkillMs.Clear(); Self = null;
        }

        public void AddLog(long now, int level, string text)
        {
            LogLine l = new LogLine(); l.Ms = now; l.Level = level; l.Text = text;
            Log.Add(l);
            if (Log.Count > MaxLogLines) Log.RemoveRange(0, Log.Count - MaxLogLines);
        }

        /// <summary>화면 흔들림을 건다. 더 센 흔들림이 진행 중이면 덮어쓰지 않는다.</summary>
        public void Shake(long now, int ms, float amp)
        {
            if (amp < ShakeAmp * Math.Max(0f, 1f - (now - ShakeStartMs) / (float)Math.Max(1, ShakeMs))) return;
            ShakeStartMs = now; ShakeMs = ms; ShakeAmp = amp;
        }

        /// <summary>now 시각의 흔들림 변위(px). 시간이 지날수록 줄어든다.</summary>
        public void ShakeOffset(long now, out float dx, out float dy)
        {
            dx = 0; dy = 0;
            long age = now - ShakeStartMs;
            if (age < 0 || age >= ShakeMs) return;
            float k = 1f - age / (float)ShakeMs;
            float a = ShakeAmp * k * k;
            dx = (float)Math.Sin(age * 0.09) * a; dy = (float)Math.Cos(age * 0.13) * a * 0.7f;
        }

        /// <summary>체력이 바뀐 뒤 호출: 보스 페이즈가 올라갔으면 알린다. 부활/사망은 처음 페이즈로 되돌린다.</summary>
        private void UpdateBossPhase(Entity e, long now, bool reset)
        {
            if (e.Boss == null) return;
            int p = e.Boss.PhaseFor(e.Hp, e.MaxHp);
            if (reset) { e.BossPhase = e.Dead ? 1 : p; return; }
            if (p <= e.BossPhase) return;
            e.BossPhase = p; e.PhaseChangedMs = now;
            float x, y; e.PosAt(now, out x, out y);
            ViewEffect b = NewFx(EffectKind.Banner, now, 2400, x, y);
            b.Text = e.Boss.Name + " - " + p + "페이즈"; b.EntityId = e.Id; b.Value = p;
            AddLog(now, 0, e.Boss.Name + " " + p + "페이즈 돌입");
            Shake(now, 600, 8f);
        }

        /// <summary>보스 경고: 붉은 경보 배너(Variant 1)와 화면 흔들림.</summary>
        private void WarnBoss(Entity e, long now, string text, int durMs)
        {
            float x, y; e.PosAt(now, out x, out y);
            ViewEffect b = NewFx(EffectKind.Banner, now, durMs, x, y);
            b.Text = text; b.EntityId = e.Id; b.Variant = 1;
            AddLog(now, 1, text);
            Shake(now, 500, 5f);
        }

        public Entity Find(ulong id)
        {
            Entity e; return Entities.TryGetValue(id, out e) ? e : null;
        }

        public string NameOf(Entity e)
        {
            if (e == null) return "?";
            if (e.IsMonster) return Data.MonsterName(e.TemplateId);
            if (e.IsPlayer) return e.Id == SelfId ? "나(" + Data.HeroName(e.TemplateId) + ")" : Data.HeroName(e.TemplateId) + "#" + e.Id;
            return "#" + e.Id;
        }

        /// <summary>시간 경과에 따른 정리(만료 연출 제거, 지난 상태 효과 제거). 매 프레임 호출.</summary>
        public void Update(long now)
        {
            foreach (KeyValuePair<ulong, Entity> kv in Entities)
            {
                Entity e = kv.Value;
                if (e.Dead || e.MaxHp <= 0) { e.GhostHp = e.Hp; continue; }
                if (e.GhostHp < e.Hp) e.GhostHp = e.Hp;
                else if (e.GhostHp > e.Hp && now - e.HitFlashMs > 450)
                {
                    long dt = now - e.GhostSinceMs; if (dt < 0 || dt > 100) dt = 16;
                    e.GhostHp = Math.Max(e.Hp, e.GhostHp - e.MaxHp * 0.35f * dt / 1000f);
                }
                e.GhostSinceMs = now;
            }
            for (int i = Effects.Count - 1; i >= 0; --i)
            {
                ViewEffect fx = Effects[i];
                if (fx.Expired(now))
                {
                    if (fx.Kind == EffectKind.Projectile) _projectiles.Remove(fx.ProjectileId);
                    Effects.RemoveAt(i);
                }
            }
            // 해제 알림이 유실돼도 화면에 영원히 남지 않도록 만료 1초 뒤에 지운다.
            List<uint> stale = null;
            foreach (KeyValuePair<ulong, Entity> kv in Entities)
            {
                Entity e = kv.Value;
                if (e.Statuses.Count == 0) continue;
                stale = null;
                foreach (KeyValuePair<uint, StatusInstance> s in e.Statuses)
                    if (now > s.Value.ExpireMs + 1000) { if (stale == null) stale = new List<uint>(); stale.Add(s.Key); }
                if (stale != null) foreach (uint id in stale) e.Statuses.Remove(id);
            }
        }

        // ------------------------------------------------------------------ 패킷 적용

        /// <summary>존 패킷(2001~) 한 개를 적용한다. 알 수 없거나 깨진 패킷은 세고 무시한다.</summary>
        public void Apply(Frame f, long now)
        {
            ++PacketCount;
            try
            {
                switch ((ZonePacketType)f.Type)
                {
                    case ZonePacketType.ZcSpawnNotify: OnSpawn(ZcSpawnNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcDespawnNotify: OnDespawn(ZcDespawnNotify.Parse(f.Data)); break;
                    case ZonePacketType.ZcMoveNotify: OnMove(ZcMoveNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcStopNotify: OnStop(ZcStopNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcCorrectPosNotify: OnCorrect(ZcCorrectPosNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcDamageNotify: OnDamage(ZcDamageNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcDeathNotify: OnDeath(ZcDeathNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcRespawnNotify: OnRespawn(ZcRespawnNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcHealNotify: OnHeal(ZcHealNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcAttackRejectNotify: OnAttackReject(ZcAttackRejectNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcSkillNotify: OnSkill(ZcSkillNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcSkillRejectNotify: OnSkillReject(ZcSkillRejectNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcSkillCastNotify: OnSkillCast(ZcSkillCastNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcSkillCancelNotify: OnSkillCancel(ZcSkillCancelNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcProjectileNotify: OnProjectile(ZcProjectileNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcSkillImpactNotify: OnImpact(ZcSkillImpactNotify.Parse(f.Data), now); break;
                    case ZonePacketType.ZcStatusNotify: OnStatus(ZcStatusNotify.Parse(f.Data), now); break;
                    default: ++UnknownPacketCount; break;
                }
            }
            catch (PacketFormatException)
            {
                ++MalformedPacketCount;
            }
        }

        private void OnSpawn(ZcSpawnNotify p, long now)
        {
            Entity e;
            if (!Entities.TryGetValue(p.EntityId, out e)) { e = new Entity(); e.Id = p.EntityId; Entities[p.EntityId] = e; }
            e.Type = p.EntityType; e.TemplateId = p.TemplateId;
            e.X = p.PosX; e.Y = p.PosY; e.DestX = p.DestX; e.DestY = p.DestY; e.Speed = p.Speed;
            e.Moving = p.Moving; e.MoveStartMs = now;
            e.Hp = p.Hp; e.MaxHp = p.MaxHp; e.Dead = p.Dead;
            e.Boss = e.IsMonster ? Data.BossOf(e.TemplateId) : null;
            e.GhostHp = e.Hp;
            UpdateBossPhase(e, now, true);
            if (e.Boss != null && !e.Dead && _bossWarned.Add(e.Id)) WarnBoss(e, now, "경고  " + e.Boss.Name + " 출현!", 2800);
            if (p.Dead && e.DeadAtMs == 0) e.DeadAtMs = now;
            if (p.Moving) e.Face(p.DestX, p.DestY, now);
            if (p.EntityId == SelfId) Self = e;
        }

        private void OnDespawn(ZcDespawnNotify p)
        {
            Entities.Remove(p.EntityId);
            DropTelegraphs(p.EntityId);
            if (p.EntityId == SelfId) Self = null;
        }

        private void OnMove(ZcMoveNotify p, long now)
        {
            Entity e = Find(p.EntityId);
            if (e == null) return;
            e.X = p.StartX; e.Y = p.StartY; e.DestX = p.DestX; e.DestY = p.DestY; e.Speed = p.Speed;
            e.Moving = true; e.MoveStartMs = now;
            e.Face(p.DestX, p.DestY, now);
        }

        private void OnStop(ZcStopNotify p, long now)
        {
            Entity e = Find(p.EntityId);
            if (e == null) return;
            e.StopAt(p.PosX, p.PosY);
            e.MoveStartMs = now;
        }

        private void OnCorrect(ZcCorrectPosNotify p, long now)
        {
            LastCorrectX = p.PosX; LastCorrectY = p.PosY; LastCorrectMs = now; LastCorrectReason = p.Reason;
            if (Self != null) { Self.StopAt(p.PosX, p.PosY); Self.MoveStartMs = now; }
            AddLog(now, 1, "위치 보정: " + p.Reason);
        }

        private void OnDamage(ZcDamageNotify p, long now)
        {
            Entity target = Find(p.TargetId);
            Entity attacker = p.AttackerId != 0 ? Find(p.AttackerId) : null;
            int dmg = p.Damage < 0 ? 0 : p.Damage;
            ++TotalDamageEvents;

            float tx = 0, ty = 0;
            if (target != null)
            {
                target.Hp = p.Hp < 0 ? 0 : p.Hp; target.MaxHp = p.MaxHp;
                target.HitFlashMs = now;
                SetHitDir(target, attacker, now);
                UpdateBossPhase(target, now, false);
                if (p.TargetId == SelfId && target.MaxHp > 0 && dmg * 100 >= target.MaxHp * 12) Shake(now, 260, 4f);
                else if (p.AttackerId == SelfId && target.MaxHp > 0 && dmg * 100 >= target.MaxHp * 12) Shake(now, 150, 2.5f);      // 내가 큰 타격을 줄 때 손맛
                target.PosAt(now, out tx, out ty);
            }
            else if (attacker != null) attacker.PosAt(now, out tx, out ty);

            if (p.TargetId == SelfId) DamageTakenBySelf += dmg;
            if (p.AttackerId == SelfId) DamageDealtBySelf += dmg;

            if (target != null)
            {
                int variant = p.AttackerId == 0 ? 1 : (p.TargetId == SelfId ? 2 : (p.AttackerId == SelfId ? 3 : 0));
                ViewEffect d = NewFx(EffectKind.Damage, now, 900, tx, ty);
                d.Value = dmg; d.Variant = variant; d.EntityId = p.TargetId;
                bool heavyHit = target.MaxHp > 0 && dmg * 100 >= target.MaxHp * 12;
                ViewEffect s = NewFx(EffectKind.Spark, now, heavyHit ? 480 : 360, tx, ty);
                s.EntityId = p.TargetId; s.TargetId = p.AttackerId; s.Value = dmg; s.Variant = heavyHit ? 1 : 0;
            }

            if (attacker != null && target != null)
            {
                attacker.Face(tx, ty, now);
                long lastSkill;
                bool fromSkill = _lastSkillMs.TryGetValue(p.AttackerId, out lastSkill) && now - lastSkill < 400;
                // 스킬의 동작은 시전/발사 알림 때 이미 시작했다. 적중 알림(투사체는 한참 뒤)으로 다시 시작하지 않는다.
                if (!fromSkill) attacker.AttackAnimMs = now;
                if (!fromSkill)
                {
                    float ax, ay; attacker.PosAt(now, out ax, out ay);
                    float dx = tx - ax, dy = ty - ay;
                    bool ranged = dx * dx + dy * dy > 4.0f * 4.0f;
                    attacker.AttackKind = (byte)(ranged ? 2 : 0);
                    ViewEffect t = NewFx(ranged ? EffectKind.Tracer : EffectKind.Slash, now, ranged ? 260 : 320, tx, ty);
                    t.EntityId = p.AttackerId;
                }
            }
        }

        /// <summary>피격 방향(공격자 -> 피격자)을 기록한다. 공격자를 모르면(지속 피해 등) 0 으로 두어 반동 없이 번쩍임만 보인다.</summary>
        private static void SetHitDir(Entity target, Entity attacker, long now)
        {
            target.HitDirX = 0; target.HitDirY = 0;
            if (attacker == null || attacker == target) return;
            float ax, ay, tx, ty; attacker.PosAt(now, out ax, out ay); target.PosAt(now, out tx, out ty);
            float dx = tx - ax, dy = ty - ay, l = (float)Math.Sqrt(dx * dx + dy * dy);
            if (l > 0.01f) { target.HitDirX = dx / l; target.HitDirY = dy / l; }
        }

        private void OnDeath(ZcDeathNotify p, long now)
        {
            Entity e = Find(p.EntityId);
            if (e == null) return;
            float x, y; e.PosAt(now, out x, out y);
            e.StopAt(x, y);
            ++TotalDeaths;
            e.Hp = 0; e.Dead = true; e.DeadAtMs = now;
            UpdateBossPhase(e, now, true);
            if (e.Boss != null) { Shake(now, 700, 7f); AddLog(now, 0, e.Boss.Name + " 처치"); _bossWarned.Remove(e.Id); }
            e.Statuses.Clear();                 // 사망 시 해제 알림은 오지 않으므로 여기서 비운다
            EndCast(e);
            NewFx(EffectKind.DeathPuff, now, 700, x, y);
            if (p.EntityId == SelfId) { ++SelfDeaths; Streak.Reset(); AddLog(now, 1, "사망"); }
            else if (p.KillerId == SelfId)
            {
                ++KillsBySelf; AddLog(now, 0, NameOf(e) + " 처치");
                int n = Streak.Register(now);
                if (n >= 2) { ViewEffect st = NewFx(EffectKind.Streak, now, 1900, x, y); st.Value = n; st.Text = KillStreak.Title(n); }
            }
        }

        private void OnRespawn(ZcRespawnNotify p, long now)
        {
            Entity e = Find(p.EntityId);
            if (e == null) return;
            e.StopAt(p.PosX, p.PosY); e.MoveStartMs = now;
            ++TotalRespawns;
            e.Hp = p.Hp; e.MaxHp = p.MaxHp; e.Dead = false; e.DeadAtMs = 0; e.GhostHp = e.Hp;
            UpdateBossPhase(e, now, true);
            e.InvulnUntilMs = p.InvulnerableMs > 0 ? now + p.InvulnerableMs : 0;
            NewFx(EffectKind.RespawnRing, now, 800, p.PosX, p.PosY);
            if (e.Boss != null && _bossWarned.Add(e.Id)) WarnBoss(e, now, "경고  " + e.Boss.Name + " 재출현!", 2800);
            if (p.EntityId == SelfId) AddLog(now, 0, "부활 (무적 " + p.InvulnerableMs + "ms)");
        }

        private void OnHeal(ZcHealNotify p, long now)
        {
            Entity e = Find(p.EntityId);
            if (e == null) return;
            e.Hp = p.Hp; e.MaxHp = p.MaxHp;
            if (e.GhostHp < e.Hp) e.GhostHp = e.Hp;
            float x, y; e.PosAt(now, out x, out y);
            ViewEffect fx = NewFx(EffectKind.Heal, now, 900, x, y);
            fx.Value = p.Amount < 0 ? 0 : p.Amount; fx.EntityId = p.EntityId;
        }

        private void OnAttackReject(ZcAttackRejectNotify p, long now)
        {
            AddLog(now, 1, "공격 거부: " + p.Reason);
        }

        private void OnSkillReject(ZcSkillRejectNotify p, long now)
        {
            if (SelfSkillResponded != null) SelfSkillResponded(now);
            AddLog(now, 1, "스킬 거부(" + Data.SkillName(p.SkillId) + "): " + p.Reason);
        }

        private void OnSkillCast(ZcSkillCastNotify p, long now)
        {
            Entity c = Find(p.CasterId);
            if (c == null) return;
            EndCast(c);
            c.CastSkillId = p.SkillId; c.CastStartMs = now; c.CastTimeMs = p.CastTimeMs;
            c.CastTargetId = p.TargetId; c.CastX = p.X; c.CastY = p.Y;
            c.Face(p.X, p.Y, now);

            SkillRow sk = Data.Skill(p.SkillId);
            if (sk != null && sk.IsBurst)
            {
                ViewEffect t = NewFx(EffectKind.Telegraph, now, (int)p.CastTimeMs + 300, p.X, p.Y);
                t.Radius = (float)sk.Radius; t.EntityId = p.CasterId; t.SkillId = p.SkillId;
            }
            if (p.CasterId == SelfId && SelfSkillResponded != null) SelfSkillResponded(now);
            if (p.CasterId == SelfId && SelfSkillUsed != null) SelfSkillUsed(p.SkillId, true, now);
        }

        private void OnSkillCancel(ZcSkillCancelNotify p, long now)
        {
            Entity c = Find(p.CasterId);
            if (c != null) EndCast(c);
            if (p.CasterId == SelfId) AddLog(now, 1, "시전 취소(" + Data.SkillName(p.SkillId) + "): " + p.Reason);
            if (p.CasterId == SelfId && SelfSkillResponded != null) SelfSkillResponded(now);
        }

        private void OnSkill(ZcSkillNotify p, long now)
        {
            Entity c = Find(p.CasterId);
            _lastSkillMs[p.CasterId] = now;
            if (c != null)
            {
                EndCast(c);
                c.AttackAnimMs = now; c.AttackKind = 1;
                c.Face(p.X, p.Y, now);
            }
            SkillRow sk = Data.Skill(p.SkillId);
            SkillFxInfo fxi = SkillFxTable.For(Data, p.SkillId);
            if (sk != null && sk.IsBurst)
            {
                ViewEffect r = NewFx(EffectKind.Ring, now, fxi.BurstMs > 0 ? fxi.BurstMs : 450, p.X, p.Y);
                r.Radius = (float)sk.Radius; r.SkillId = p.SkillId; r.EntityId = p.CasterId;
                if (c != null && c.Boss != null) { Shake(now, 450, 5f); r.DurMs = Math.Max(r.DurMs, 1100); }          // 보스의 범위 스킬 폭발은 균열이 오래 남는다
                ShakeForSkill(fxi, c, p.X, p.Y, now);
            }
            else
            {
                // 베기 계열은 짧게, 범위형 양식(치유/충격파 등)을 단일 대상 형태로 쓰는 스킬은 그 양식의 길이만큼 남긴다.
                bool burst = fxi.BurstMs > 0;
                ViewEffect s = NewFx(EffectKind.Slash, now, burst ? fxi.BurstMs : (fxi.Style == SkillFxStyle.Blade || fxi.Style == SkillFxStyle.Pierce || fxi.Style == SkillFxStyle.Lance ? 460 : 260), p.X, p.Y);
                s.EntityId = p.CasterId; s.SkillId = p.SkillId; s.Variant = 1;
                if (burst) s.Radius = sk != null && sk.Radius > 0 ? (float)sk.Radius : 3.5f;
                if (sk != null && sk.IsAimed)
                {
                    // 부채꼴/직선: 시전자에서 (p.X, p.Y) 방향으로 사거리만큼. Value=사거리*100, X2=부채꼴 각도(도), Y2=직선 폭(반경)
                    s.DurMs = Math.Max(s.DurMs, 520); s.Radius = 0f;
                    s.Value = (int)Math.Round(sk.Range * 100.0); s.X2 = sk.IsCone ? (float)(sk.Angle > 0 ? sk.Angle : 90.0) : 0f; s.Y2 = sk.IsLine ? (float)Math.Max(0.5, sk.Radius) : 0f;
                }
                ShakeForSkill(fxi, c, p.X, p.Y, now);
            }
            if (p.CasterId == SelfId && SelfSkillResponded != null) SelfSkillResponded(now);
            if (p.CasterId == SelfId && SelfSkillUsed != null && (sk == null || sk.CastTimeMs == 0))
                SelfSkillUsed(p.SkillId, false, now);
        }

        /// <summary>양식에 정해진 화면 흔들림을 건다. 내가 시전했거나 내 가까이에서 터질 때만 흔든다.</summary>
        private void ShakeForSkill(SkillFxInfo info, Entity caster, float x, float y, long now)
        {
            if (info.Shake <= 0f) return;
            Entity me = Self;
            if (me == null) return;
            float mx, my; me.PosAt(now, out mx, out my);
            float dx = mx - x, dy = my - y;
            if (caster != me && dx * dx + dy * dy > 14f * 14f) return;
            Shake(now, 320, info.Shake);
        }

        private void OnProjectile(ZcProjectileNotify p, long now)
        {
            Entity c = Find(p.CasterId);
            _lastSkillMs[p.CasterId] = now;
            if (c != null) { EndCast(c); c.AttackAnimMs = now; c.AttackKind = 2; c.Face(p.X1, p.Y1, now); }
            ViewEffect fx = NewFx(EffectKind.Projectile, now, (int)p.FlightMs + 600, p.X0, p.Y0);
            fx.X2 = p.X1; fx.Y2 = p.Y1; fx.EntityId = p.CasterId; fx.TargetId = p.TargetId;
            fx.SkillId = p.SkillId; fx.ProjectileId = p.ProjectileId; fx.Value = (int)p.FlightMs;
            _projectiles[p.ProjectileId] = fx;
            SkillRow sk = Data.Skill(p.SkillId);
            if (p.CasterId == SelfId && SelfSkillResponded != null) SelfSkillResponded(now);
            if (p.CasterId == SelfId && SelfSkillUsed != null && (sk == null || sk.CastTimeMs == 0))
                SelfSkillUsed(p.SkillId, false, now);
        }

        private void OnImpact(ZcSkillImpactNotify p, long now)
        {
            ViewEffect pr;
            if (_projectiles.TryGetValue(p.ProjectileId, out pr)) { pr.DurMs = 0; _projectiles.Remove(p.ProjectileId); }
            _lastSkillMs[p.CasterId] = now;
            SkillRow sk = Data.Skill(p.SkillId);
            SkillFxInfo fxi = SkillFxTable.For(Data, p.SkillId);
            if (sk != null && sk.IsBurst)
            {
                ViewEffect r = NewFx(EffectKind.Ring, now, fxi.BurstMs > 0 ? fxi.BurstMs : 450, p.X, p.Y);
                r.Radius = (float)sk.Radius; r.SkillId = p.SkillId; r.EntityId = p.CasterId;
                ShakeForSkill(fxi, Find(p.CasterId), p.X, p.Y, now);
            }
            else
            {
                ViewEffect s = NewFx(EffectKind.Spark, now, fxi.Style == SkillFxStyle.Fireball ? 800 : (fxi.Style == SkillFxStyle.Arrow ? 420 : 260), p.X, p.Y);
                s.SkillId = p.SkillId;
                if (fxi.Style == SkillFxStyle.Fireball) Shake(now, 220, 2.5f);
            }
        }

        private void OnStatus(ZcStatusNotify p, long now)
        {
            Entity e = Find(p.EntityId);
            if (e == null) return;
            float x, y; e.PosAt(now, out x, out y);
            if (p.Applied)
            {
                StatusInstance s;
                bool enrageStart = e.Boss != null && e.Boss.EnrageStatusId != 0 && p.StatusId == e.Boss.EnrageStatusId && !e.Statuses.ContainsKey(p.StatusId);
                if (enrageStart) WarnBoss(e, now, "경고  " + e.Boss.Name + " 격노!", 2400);
                if (!e.Statuses.TryGetValue(p.StatusId, out s)) { s = new StatusInstance(); s.StatusId = p.StatusId; e.Statuses[p.StatusId] = s; }
                s.CasterId = p.CasterId; s.ExpireMs = now + p.DurationMs;
                ViewEffect t = NewFx(EffectKind.Text, now, 1000, x, y);
                t.Text = Data.StatusName(p.StatusId); t.EntityId = p.EntityId; t.Variant = 0;
            }
            else
            {
                e.Statuses.Remove(p.StatusId);
            }
        }

        // ------------------------------------------------------------------ 내부 도우미

        private ViewEffect NewFx(EffectKind kind, long now, int durMs, float x, float y)
        {
            ViewEffect fx = new ViewEffect();
            fx.Kind = kind; fx.StartMs = now; fx.DurMs = durMs; fx.X = x; fx.Y = y;
            Effects.Add(fx);
            return fx;
        }

        private void EndCast(Entity e)
        {
            if (e.CastSkillId == 0) return;
            e.CastSkillId = 0; e.CastTimeMs = 0; e.CastTargetId = 0;
            DropTelegraphs(e.Id);
        }

        private void DropTelegraphs(ulong casterId)
        {
            for (int i = Effects.Count - 1; i >= 0; --i)
                if (Effects[i].Kind == EffectKind.Telegraph && Effects[i].EntityId == casterId)
                    Effects.RemoveAt(i);
        }
    }
}
