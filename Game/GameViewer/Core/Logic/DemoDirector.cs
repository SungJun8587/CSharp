//***************************************************************************
// DemoDirector.cs : 서버 없이 화면을 확인하는 "로컬 미니 서버".
// 영웅(나 + 동료 3명)과 몬스터가 숲에서 싸우는 상황을 시뮬레이션하고, 실제 서버와 같은 형식의 패킷을 만들어
// 뷰어 세션에 주입한다. 내 조작(이동/공격/스킬)은 SendOverride 로 들어와 같은 패킷 경로로 처리한다.
// 따라서 렌더러/월드 모델/자동 전투를 서버 없이 그대로 시험할 수 있다. (서버 규칙의 완전한 재현은 아님)
//***************************************************************************
using System;
using System.Collections.Generic;
using GameViewer.Core.Data;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;

namespace GameViewer.Core.Logic
{
    public sealed class DemoDirector
    {
        private sealed class Sim
        {
            public ulong Id; public EntityType Type; public uint Tpl;
            public float X, Y, HomeX, HomeY, Speed, DestX, DestY;
            public bool Moving, Dead, Casting, IsSelf;
            public int Hp, MaxHp, Attack, Defense;
            public float AtkRange, Aggro;
            public long AtkInterval, NextAtk, RespawnAt, NextWander, InvulnUntil;
            public ulong Target;
            public BossRow Boss; public int Phase; public long NextBossAct; public int ActIdx;      // 데모 보스 (서버 BossAction 의 단순화된 흉내)
            public int CastToken;
            public List<uint> Skills = new List<uint>();
            public Dictionary<uint, long> SkillReady = new Dictionary<uint, long>();
            public Dictionary<uint, long> StatusExpire = new Dictionary<uint, long>();
            public Dictionary<uint, long> StatusNextTick = new Dictionary<uint, long>();
        }

        private sealed class Pending { public long Due; public Action Run; }

        private readonly ViewerSession _s;
        private readonly GameData _d;
        private readonly Random _rnd = new Random(20240601);
        private readonly List<Sim> _sims = new List<Sim>();
        private readonly Dictionary<ulong, Sim> _byId = new Dictionary<ulong, Sim>();
        private readonly List<Pending> _pending = new List<Pending>();
        private long _last;
        private uint _pid;
        private float _cx, _cy;
        private Sim _self;

        public float CenterX { get { return _cx; } }
        public float CenterY { get { return _cy; } }

        public DemoDirector(ViewerSession session)
        {
            _s = session; _d = session.Data;
        }

        // ------------------------------------------------------------------ 시작

        public void Start(long now)
        {
            // 장애물이 모여 있는 곳 근처에서 시작한다 (없으면 월드 중앙)
            if (_d.Blocks.Count > 0)
            {
                float sx = 0, sy = 0;
                foreach (BlockRow b in _d.Blocks) { sx += b.IsCircle ? b.X : b.X + b.W / 2; sy += b.IsCircle ? b.Y : b.Y + b.H / 2; }
                _cx = sx / _d.Blocks.Count; _cy = sy / _d.Blocks.Count;
            }
            else { _cx = 500; _cy = 500; }

            uint selfHero = _d.ResolveHeroId(_s.Config.HeroId);
            float px = _cx, py = _cy + 6;
            FindFree(ref px, ref py, 3, 1.5f);
            _s.EnterOffline(px, py, "데모 모드 (서버 없이 로컬 시뮬레이션)", now);
            _s.SendOverride = OnClientPacket;

            _self = AddHero(_s.Config.CharId, selfHero, px, py, true);
            uint[] allies = { 1, 2, 3 };
            for (int i = 0; i < allies.Length; ++i)
            {
                double ang = Math.PI * 2 * i / allies.Length + 0.6;
                float ax = _cx + (float)Math.Cos(ang) * 5f, ay = _cy + 6 + (float)Math.Sin(ang) * 4f;
                FindFree(ref ax, ref ay, 3, 1.5f);
                AddHero(9001 + (ulong)i, allies[i], ax, ay, false);
            }

            uint[] kinds = MonsterKinds();
            for (int i = 0; i < 12; ++i)
            {
                double ang = _rnd.NextDouble() * Math.PI * 2;
                float rr = 9f + (float)_rnd.NextDouble() * 9f;
                float mx = _cx + (float)Math.Cos(ang) * rr, my = _cy + 4 + (float)Math.Sin(ang) * rr * 0.8f;
                FindFree(ref mx, ref my, 3, 1.5f);
                AddMonster(0x8000000000000001UL + (ulong)i, kinds[i % kinds.Length], mx, my);
            }

            AddDemoBoss();
            for (int i = 0; i < _sims.Count; ++i) EmitSpawn(_sims[i], now);
            _last = now;
        }

        /// <summary>보스 데이터가 있으면 데모 숲 북쪽에 보스 한 마리를 둔다. 체력은 데모에서 페이즈를 볼 수 있게 줄인다.</summary>
        private void AddDemoBoss()
        {
            BossRow best = null;
            foreach (BossRow b in _d.Bosses.Values)
                if (_d.Monster(b.MonsterId) != null && (best == null || b.Id < best.Id)) best = b;
            if (best == null || BossSkills().Count == 0) return;
            float bx = _cx, by = _cy - 9;
            FindFree(ref bx, ref by, 3, 2.5f);
            AddMonster(0x8000000000000100UL, best.MonsterId, bx, by);
            Sim m = _byId[0x8000000000000100UL];
            m.Boss = best; m.Phase = 1;
            m.MaxHp = Math.Min(m.MaxHp, 1500); m.Hp = m.MaxHp;
            m.Aggro = Math.Max(m.Aggro, 16f);
        }

        private List<SkillRow> BossSkills()
        {
            List<SkillRow> l = new List<SkillRow>();
            foreach (SkillRow sk in _d.Skills.Values)
                if (sk.IsArea && sk.CastTimeMs > 0 && sk.Radius > 0 && (sk.DamagePercent > 0 || sk.FlatDamage > 0)) l.Add(sk);
            l.Sort(delegate (SkillRow a, SkillRow b) { return a.Id.CompareTo(b.Id); });
            return l;
        }

        private uint[] MonsterKinds()
        {
            List<uint> ids = new List<uint>(_d.Monsters.Keys);
            ids.Sort();
            ids.RemoveAll(delegate (uint id) { return _d.BossOf(id) != null; });      // 보스는 따로 둔다
            if (ids.Count == 0) return new uint[] { 1 };
            if (ids.Count > 4) ids.RemoveRange(4, ids.Count - 4);       // 약한 앞쪽 몇 종류만
            return ids.ToArray();
        }

        private Sim AddHero(ulong id, uint tpl, float x, float y, bool self)
        {
            HeroRow h = _d.Hero(tpl);
            Sim m = new Sim();
            m.Id = id; m.Type = EntityType.Player; m.Tpl = tpl; m.IsSelf = self;
            m.X = x; m.Y = y; m.HomeX = x; m.HomeY = y;
            m.MaxHp = h != null ? h.MaxHp : 150; m.Hp = m.MaxHp;
            m.Attack = h != null ? h.Attack : 12; m.Defense = h != null ? h.Defense : 2;
            m.AtkRange = h != null ? (float)h.AttackRange : 2.5f; m.Speed = h != null ? (float)h.Speed : 6f;
            m.AtkInterval = h != null && h.AttackIntervalMs > 0 ? h.AttackIntervalMs : 900;
            m.Skills.AddRange(_d.SkillsOf(tpl));
            _sims.Add(m); _byId[id] = m;
            return m;
        }

        private void AddMonster(ulong id, uint tpl, float x, float y)
        {
            MonsterRow mr = _d.Monster(tpl);
            Sim m = new Sim();
            m.Id = id; m.Type = EntityType.Monster; m.Tpl = tpl;
            m.X = x; m.Y = y; m.HomeX = x; m.HomeY = y;
            m.MaxHp = mr != null ? mr.MaxHp : 120; m.Hp = m.MaxHp;
            m.Attack = mr != null ? mr.Attack : 8; m.Defense = mr != null ? mr.Defense : 0;
            m.AtkRange = mr != null ? (float)mr.AttackRange : 2f; m.Speed = mr != null ? (float)mr.Speed : 3f;
            m.Aggro = mr != null ? (float)mr.AggroRange : 8f;
            m.AtkInterval = mr != null && mr.AttackIntervalMs > 0 ? mr.AttackIntervalMs : 1500;
            m.NextWander = 0;
            _sims.Add(m); _byId[id] = m;
        }

        // ------------------------------------------------------------------ 매 프레임

        public void Update(long now)
        {
            if (now - _last < 100) return;
            long dt = now - _last; _last = now;

            for (int i = 0; i < _pending.Count; ++i)
            {
                // 실행 중 새 예약이 추가될 수 있으므로 인덱스로 돈다
                if (_pending[i].Due <= now)
                {
                    Action run = _pending[i].Run;
                    _pending.RemoveAt(i); --i;
                    run();
                }
            }

            for (int i = 0; i < _sims.Count; ++i)
            {
                Sim m = _sims[i];
                if (m.Dead)
                {
                    if (now >= m.RespawnAt) Respawn(m, now);
                    continue;
                }
                TickStatuses(m, now);
                if (m.Dead) continue;
                Step(m, dt, now);
                if (m.IsSelf) continue;
                if (m.Type == EntityType.Monster) MonsterAi(m, now); else HeroAi(m, now);
            }
        }

        private void Schedule(long due, Action a)
        {
            Pending p = new Pending(); p.Due = due; p.Run = a; _pending.Add(p);
        }

        // ------------------------------------------------------------------ 이동

        private void Step(Sim m, long dtMs, long now)
        {
            if (!m.Moving) return;
            float dx = m.DestX - m.X, dy = m.DestY - m.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            float step = m.Speed * SpeedFactor(m, now) * dtMs / 1000f;
            if (dist <= step || dist < 0.01f)
            {
                m.X = m.DestX; m.Y = m.DestY; m.Moving = false;
                Emit(ServerPackets.Stop(m.Id, m.X, m.Y, true), now);
                return;
            }
            float nx = m.X + dx / dist * step, ny = m.Y + dy / dist * step;
            if (IsBlocked(nx, ny, 0.6f))
            {
                m.Moving = false;
                Emit(ServerPackets.Stop(m.Id, m.X, m.Y, false), now);
                return;
            }
            m.X = nx; m.Y = ny;
        }

        private float SpeedFactor(Sim m, long now)
        {
            float f = 1f;
            foreach (KeyValuePair<uint, long> st in m.StatusExpire)
            {
                StatusRow row = _d.Status(st.Key);
                if (row != null && !row.IsDot && st.Value > now) f += row.SpeedPercent / 100f;
            }
            return f < 0.2f ? 0.2f : f;
        }

        private void MoveTo(Sim m, float x, float y, long now)
        {
            if (m.Casting) CancelCast(m, SkillCancelReason.Moved, now);
            if (m.Moving && Math.Abs(m.DestX - x) < 0.8f && Math.Abs(m.DestY - y) < 0.8f) return;
            m.DestX = x; m.DestY = y; m.Moving = true;
            Emit(ServerPackets.Move(m.Id, m.X, m.Y, x, y, m.Speed * SpeedFactor(m, now)), now);
        }

        private void StopNow(Sim m, long now)
        {
            if (!m.Moving) return;
            m.Moving = false;
            Emit(ServerPackets.Stop(m.Id, m.X, m.Y, false), now);
        }

        private bool IsBlocked(float x, float y, float pad)
        {
            for (int i = 0; i < _d.Blocks.Count; ++i)
            {
                BlockRow b = _d.Blocks[i];
                if (b.IsCircle)
                {
                    float dx = x - b.X, dy = y - b.Y;
                    float rr = b.R + pad;
                    if (dx * dx + dy * dy < rr * rr) return true;
                }
                else if (x > b.X - pad && x < b.X + b.W + pad && y > b.Y - pad && y < b.Y + b.H + pad) return true;
            }
            return x < _d.WorldMinX || y < _d.WorldMinY || x > _d.WorldMaxX || y > _d.WorldMaxY;
        }

        private void FindFree(ref float x, ref float y, int tries, float pad)
        {
            for (int i = 0; i < 40 && IsBlocked(x, y, pad); ++i)
            {
                x += (float)(_rnd.NextDouble() - 0.5) * 6f; y += (float)(_rnd.NextDouble() - 0.5) * 6f;
            }
        }

        // ------------------------------------------------------------------ AI

        private void BossThink(Sim m, Sim t, long now)
        {
            int p = m.Boss.PhaseFor(m.Hp, m.MaxHp);
            if (p > m.Phase)
            {
                m.Phase = p;
                BossPhaseRow pr = m.Boss.Phases.Find(delegate (BossPhaseRow x) { return x.Phase == (uint)p; });
                StatusRow st = pr != null ? _d.Status(pr.EnterStatusId) : null;
                if (st != null && st.TargetsSelf) ApplyStatus(m, m, st, now);
                m.NextBossAct = now + 900;
                return;
            }
            if (now < m.NextBossAct) return;
            List<SkillRow> skills = BossSkills();
            if (skills.Count == 0) return;
            SkillRow sk = skills[m.ActIdx % skills.Count];
            Sim target = t;
            if (m.ActIdx % 2 == 1)
            {
                List<Sim> live = new List<Sim>();
                foreach (Sim o in _sims) if (!o.Dead && o.Type == EntityType.Player && Dist(m, o) <= 18f) live.Add(o);
                if (live.Count > 0) target = live[_rnd.Next(live.Count)];
            }
            ++m.ActIdx;
            if (Dist(m, target) > sk.Range + 3f) { m.NextBossAct = now + 300; return; }
            StopNow(m, now);
            CastSkill(m, sk, 0, target.X, target.Y, now);
            m.NextBossAct = now + sk.CastTimeMs + (m.Phase >= 3 ? 1200u : (m.Phase == 2 ? 1800u : 2600u));
        }

        private void MonsterAi(Sim m, long now)
        {
            if (m.Casting) return;
            if (m.Boss != null)
            {
                Sim bt = m.Target != 0 ? Find(m.Target) : null;
                if (bt != null && !bt.Dead) { BossThink(m, bt, now); if (m.Casting) return; }
            }
            Sim t = m.Target != 0 ? Find(m.Target) : null;
            if (t != null && (t.Dead || Dist(m, t) > m.Aggro * 2.5f)) { t = null; m.Target = 0; }
            if (t == null)
            {
                t = Nearest(m, EntityType.Player, m.Aggro);
                if (t != null) m.Target = t.Id;
            }
            if (t == null)
            {
                if (now >= m.NextWander && !m.Moving)
                {
                    float wx = m.HomeX + (float)(_rnd.NextDouble() - 0.5) * 12f, wy = m.HomeY + (float)(_rnd.NextDouble() - 0.5) * 12f;
                    if (!IsBlocked(wx, wy, 1f)) MoveTo(m, wx, wy, now);
                    m.NextWander = now + 2500 + _rnd.Next(3000);
                }
                return;
            }
            float d = Dist(m, t);
            if (d <= m.AtkRange)
            {
                StopNow(m, now);
                if (now >= m.NextAtk) { m.NextAtk = now + m.AtkInterval; BasicAttack(m, t, now); }
            }
            else MoveTo(m, t.X, t.Y, now);
        }

        private void HeroAi(Sim m, long now)
        {
            if (m.Casting) return;
            Sim t = m.Target != 0 ? Find(m.Target) : null;
            if (t != null && t.Dead) { t = null; m.Target = 0; }
            if (t == null)
            {
                // 보스가 있으면 동료들은 보스를 먼저 노린다 (보스전 화면 확인용)
                foreach (Sim o in _sims) if (!o.Dead && o.Boss != null && Dist(m, o) <= 30f) { t = o; break; }
                if (t == null) t = Nearest(m, EntityType.Monster, 30f);
                if (t != null) m.Target = t.Id;
            }
            if (t == null)
            {
                if (now >= m.NextWander && !m.Moving)
                {
                    float wx = _cx + (float)(_rnd.NextDouble() - 0.5) * 16f, wy = _cy + 6 + (float)(_rnd.NextDouble() - 0.5) * 12f;
                    if (!IsBlocked(wx, wy, 1f)) MoveTo(m, wx, wy, now);
                    m.NextWander = now + 3000 + _rnd.Next(3000);
                }
                return;
            }
            float d = Dist(m, t);
            for (int i = 0; i < m.Skills.Count; ++i)
            {
                SkillRow sk = _d.Skill(m.Skills[i]);
                if (sk == null || !SkillReadyAt(m, sk, now) || d > sk.Range - 0.3f) continue;
                if (sk.TargetsSelfOnly(_d)) continue;
                StopNow(m, now);
                CastSkill(m, sk, t.Id, t.X, t.Y, now);
                return;
            }
            if (d <= m.AtkRange - 0.2f)
            {
                StopNow(m, now);
                if (now >= m.NextAtk) { m.NextAtk = now + m.AtkInterval; BasicAttack(m, t, now); }
            }
            else
            {
                float stop = Math.Max(0.5f, m.AtkRange * 0.85f);
                float k = (d - stop) / d;
                MoveTo(m, m.X + (t.X - m.X) * k, m.Y + (t.Y - m.Y) * k, now);
            }
        }

        // ------------------------------------------------------------------ 전투

        private void BasicAttack(Sim a, Sim t, long now)
        {
            int dmg = RollDamage(a.Attack * 100, t, now);
            Hurt(a, t, dmg, now);
        }

        private int RollDamage(int attackPercentTimes100, Sim target, long now)
        {
            double raw = attackPercentTimes100 / 100.0 * (0.85 + _rnd.NextDouble() * 0.3) - target.Defense;
            return raw < 1 ? 1 : (int)raw;
        }

        /// <summary>피해 적용 + 알림. 사망하면 Death 까지.</summary>
        private void Hurt(Sim attacker, Sim t, int dmg, long now)
        {
            if (t.Dead || t.InvulnUntil > now) return;
            t.Hp = Math.Max(0, t.Hp - dmg);
            Emit(ServerPackets.Damage(attacker != null ? attacker.Id : 0, t.Id, dmg, t.Hp, t.MaxHp), now);
            if (t.Type == EntityType.Monster && attacker != null && t.Target == 0) t.Target = attacker.Id;
            if (t.Hp <= 0) Kill(t, attacker, now);
        }

        private void Kill(Sim t, Sim killer, long now)
        {
            t.Dead = true; t.Moving = false; t.Casting = false; t.CastToken++;
            t.StatusExpire.Clear(); t.StatusNextTick.Clear();
            t.Target = 0;
            t.RespawnAt = now + (t.Type == EntityType.Monster ? 7000 : 4500);
            Emit(ServerPackets.Death(t.Id, killer != null ? killer.Id : 0), now);
        }

        private void Respawn(Sim m, long now)
        {
            m.Dead = false; m.Hp = m.MaxHp; m.Target = 0; m.Phase = 1; m.NextBossAct = 0; m.ActIdx = 0;
            m.X = m.HomeX; m.Y = m.HomeY; m.Moving = false;
            m.InvulnUntil = m.Type == EntityType.Player ? now + 2000 : 0;
            Emit(ServerPackets.Respawn(m.Id, m.X, m.Y, m.Hp, m.MaxHp, m.Type == EntityType.Player ? 2000u : 0u), now);
        }

        private bool SkillReadyAt(Sim m, SkillRow sk, long now)
        {
            long ready;
            return !m.SkillReady.TryGetValue(sk.Id, out ready) || now >= ready;
        }

        private void CastSkill(Sim c, SkillRow sk, ulong targetId, float x, float y, long now)
        {
            c.SkillReady[sk.Id] = now + sk.CooldownMs;
            if (sk.CastTimeMs > 0)
            {
                c.Casting = true;
                int token = ++c.CastToken;
                Emit(ServerPackets.Cast(c.Id, sk.Id, sk.IsSingle ? targetId : 0, x, y, sk.CastTimeMs), now);
                Schedule(now + sk.CastTimeMs, delegate
                {
                    if (c.Dead || !c.Casting || c.CastToken != token) return;
                    c.Casting = false;
                    ResolveSkill(c, sk, targetId, x, y, _last);
                });
            }
            else ResolveSkill(c, sk, targetId, x, y, now);
        }

        private void CancelCast(Sim c, SkillCancelReason reason, long now)
        {
            if (!c.Casting) return;
            c.Casting = false; c.CastToken++;
            Emit(ServerPackets.Cancel(c.Id, 0, reason), now);
        }

        private void ResolveSkill(Sim c, SkillRow sk, ulong targetId, float x, float y, long now)
        {
            Sim t = null;
            if (sk.IsSingle)
            {
                t = Find(targetId);
                if (t == null || t.Dead) { Emit(ServerPackets.Cancel(c.Id, sk.Id, SkillCancelReason.InvalidTarget), now); return; }
                x = t.X; y = t.Y;
            }
            else if (sk.IsSelf) { x = c.X; y = c.Y; }

            if (sk.ProjectileSpeed > 0 && t != null)
            {
                float d = Dist(c.X, c.Y, x, y);
                uint flight = (uint)Math.Max(120, d / sk.ProjectileSpeed * 1000.0);
                uint pid = ++_pid;
                Emit(ServerPackets.Projectile(pid, c.Id, sk.Id, t.Id, c.X, c.Y, x, y, flight), now);
                Schedule(now + flight, delegate
                {
                    if (c.Dead) return;
                    Sim tt = Find(t.Id);
                    float ix = tt != null ? tt.X : x, iy = tt != null ? tt.Y : y;
                    bool hit = tt != null && !tt.Dead;
                    Emit(ServerPackets.Impact(pid, c.Id, sk.Id, ix, iy, hit ? 1u : 0u), _last);
                    if (hit) ApplyHit(c, sk, tt, _last);
                });
                return;
            }

            List<Sim> hits = new List<Sim>();
            if (sk.IsSingle) hits.Add(t);
            else
            {
                // 서버 CollectHits 와 같은 규칙: Area/Self 는 중심에서 반경, Cone 은 시전자에서 방향 부채꼴, Line 은 방향 직선(좌우 반경 폭)
                float ax = x - c.X, ay = y - c.Y, al = (float)Math.Sqrt(ax * ax + ay * ay);
                float ux = al > 0.0001f ? ax / al : 1f, uy = al > 0.0001f ? ay / al : 0f;
                float cosHalf = (float)Math.Cos((sk.Angle > 0 ? sk.Angle : 90.0) * 0.5 * Math.PI / 180.0);
                float fromX = sk.IsBurst ? x : c.X, fromY = sk.IsBurst ? y : c.Y;
                foreach (Sim o in _sims)
                {
                    if (o.Dead || (sk.IsAlly ? o.Type != c.Type : o.Type == c.Type)) continue;
                    float dx = o.X - c.X, dy = o.Y - c.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
                    bool inside;
                    if (sk.IsBurst) inside = Dist(o.X, o.Y, x, y) <= sk.Radius;
                    else if (sk.IsCone) inside = d <= sk.Range && (d < 0.0001f || (dx * ux + dy * uy) / d >= cosHalf);
                    else { float along = dx * ux + dy * uy, side = Math.Abs(-dx * uy + dy * ux); inside = along >= 0f && along <= sk.Range && side <= sk.Radius; }
                    if (inside) hits.Add(o);
                }
                hits.Sort(delegate (Sim a, Sim b) { return Dist(a.X, a.Y, fromX, fromY).CompareTo(Dist(b.X, b.Y, fromX, fromY)); });
                if (sk.MaxTargets > 0 && hits.Count > sk.MaxTargets) hits.RemoveRange((int)sk.MaxTargets, hits.Count - (int)sk.MaxTargets);
            }

            Emit(ServerPackets.Skill(c.Id, sk.Id, sk.IsSingle ? targetId : 0, x, y, (uint)hits.Count), now);
            for (int i = 0; i < hits.Count; ++i) ApplyHit(c, sk, hits[i], now);
            StatusRow st = _d.Status(sk.StatusId);
            if (st != null && st.TargetsSelf) ApplyStatus(c, c, st, now);
        }

        private void ApplyHit(Sim c, SkillRow sk, Sim t, long now)
        {
            if (sk.Heals)
            {
                int amt = (int)(c.Attack * (long)sk.HealPercent / 100) + sk.FlatHeal;
                if (amt > 0 && !t.Dead) { t.Hp = Math.Min(t.MaxHp, t.Hp + amt); Emit(ServerPackets.Heal(t.Id, amt, t.Hp, t.MaxHp), now); }
            }
            if (sk.DamagePercent > 0 || sk.FlatDamage > 0)
                Hurt(c, t, RollDamage(c.Attack * (int)sk.DamagePercent + sk.FlatDamage * 100, t, now), now);
            StatusRow st = _d.Status(sk.StatusId);
            if (st != null && !st.TargetsSelf && !t.Dead) ApplyStatus(c, t, st, now);
        }

        private void ApplyStatus(Sim caster, Sim t, StatusRow st, long now)
        {
            if (t.Dead) return;
            t.StatusExpire[st.Id] = now + st.DurationMs;
            if (st.IsDot) t.StatusNextTick[st.Id] = now + (st.TickMs > 0 ? st.TickMs : 1000);
            Emit(ServerPackets.Status(t.Id, st.Id, caster.Id, st.DurationMs, true), now);
        }

        private void TickStatuses(Sim m, long now)
        {
            if (m.StatusExpire.Count == 0) return;
            List<uint> ended = null;
            foreach (KeyValuePair<uint, long> kv in m.StatusExpire)
            {
                StatusRow row = _d.Status(kv.Key);
                if (row != null && row.IsDot)
                {
                    long next;
                    if (m.StatusNextTick.TryGetValue(kv.Key, out next) && now >= next && now < kv.Value)
                    {
                        m.StatusNextTick[kv.Key] = now + (row.TickMs > 0 ? row.TickMs : 1000);
                        int dmg = Math.Max(1, m.MaxHp * (int)Math.Max(row.DamagePercent, 5u) / 1000);
                        Hurt(null, m, dmg, now);
                        if (m.Dead) return;
                    }
                }
                if (now >= kv.Value) { if (ended == null) ended = new List<uint>(); ended.Add(kv.Key); }
            }
            if (ended != null)
                foreach (uint id in ended)
                {
                    m.StatusExpire.Remove(id); m.StatusNextTick.Remove(id);
                    Emit(ServerPackets.Status(m.Id, id, 0, 0, false), now);
                }
        }

        // ------------------------------------------------------------------ 내 조작(클라이언트 -> "서버")

        private bool OnClientPacket(byte[] p)
        {
            long now = _last;
            if (p.Length < 4) return false;
            ushort type = (ushort)(p[2] | (p[3] << 8));
            PacketReader r = new PacketReader(p, 4);
            Sim me = _self;
            if (me == null) return false;
            if (me.Dead)
            {
                if (type == (ushort)ZonePacketType.ZcMoveReq) Emit(ServerPackets.Correct(CorrectPosReason.Dead, me.X, me.Y), now);
                return true;
            }

            switch ((ZonePacketType)type)
            {
                case ZonePacketType.ZcMoveReq:
                    {
                        r.F32(); r.F32();
                        float dx = r.F32(), dy = r.F32();
                        if (IsBlocked(dx, dy, 0.4f)) { Emit(ServerPackets.Correct(CorrectPosReason.Blocked, me.X, me.Y), now); return true; }
                        MoveTo(me, dx, dy, now);
                        return true;
                    }
                case ZonePacketType.ZcAttackReq:
                    {
                        Sim t = Find(r.U64());
                        if (t == null || t.Dead || Dist(me, t) > me.AtkRange + 0.3f) { Emit(ServerPackets.AttackReject(t != null ? t.Id : 0, AttackRejectReason.OutOfRange), now); return true; }
                        if (me.Casting) { Emit(ServerPackets.AttackReject(t.Id, AttackRejectReason.Casting), now); return true; }
                        if (now < me.NextAtk) return true;
                        me.NextAtk = now + me.AtkInterval;
                        BasicAttack(me, t, now);
                        return true;
                    }
                case ZonePacketType.ZcSkillReq:
                    {
                        uint skillId = r.U32(); ulong targetId = r.U64(); float x = r.F32(), y = r.F32();
                        SkillRow sk = _d.Skill(skillId);
                        if (sk == null || !me.Skills.Contains(skillId)) { Emit(ServerPackets.SkillReject(skillId, SkillRejectReason.UnknownSkill), now); return true; }
                        if (me.Casting) { Emit(ServerPackets.SkillReject(skillId, SkillRejectReason.Casting), now); return true; }
                        if (!SkillReadyAt(me, sk, now)) { Emit(ServerPackets.SkillReject(skillId, SkillRejectReason.Cooldown), now); return true; }
                        if (sk.IsSingle)
                        {
                            Sim t = Find(targetId);
                            if (t == null || t.Dead || (sk.IsAlly ? t.Type != me.Type : t.Type == me.Type)) { Emit(ServerPackets.SkillReject(skillId, SkillRejectReason.InvalidTarget), now); return true; }
                            if (Dist(me, t) > sk.Range) { Emit(ServerPackets.SkillReject(skillId, SkillRejectReason.OutOfRange), now); return true; }
                        }
                        else if (sk.IsAimed) { if (Dist(me.X, me.Y, x, y) < 0.01f) { Emit(ServerPackets.SkillReject(skillId, SkillRejectReason.InvalidTarget), now); return true; } }
                        else if (sk.IsArea && Dist(me.X, me.Y, x, y) > sk.Range + 0.01f && sk.Range > 1.5f) { Emit(ServerPackets.SkillReject(skillId, SkillRejectReason.OutOfRange), now); return true; }
                        if (sk.CastTimeMs > 0) StopNow(me, now);
                        CastSkill(me, sk, targetId, x, y, now);
                        return true;
                    }
                default:
                    return true;
            }
        }

        // ------------------------------------------------------------------ 도우미

        private void EmitSpawn(Sim m, long now)
        {
            Emit(ServerPackets.Spawn(m.Id, m.Type, m.X, m.Y, m.X, m.Y, m.Speed, false, m.Hp, m.MaxHp, m.Dead, m.Tpl), now);
        }

        private void Emit(byte[] pkt, long now) { _s.InjectFrame(ServerPackets.F(pkt), now); }

        private Sim Find(ulong id) { Sim s; return _byId.TryGetValue(id, out s) ? s : null; }

        private Sim Nearest(Sim from, EntityType type, float maxDist)
        {
            Sim best = null; float bd = maxDist;
            for (int i = 0; i < _sims.Count; ++i)
            {
                Sim o = _sims[i];
                if (o.Dead || o.Type != type) continue;
                float d = Dist(from, o);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        private static float Dist(Sim a, Sim b) { return Dist(a.X, a.Y, b.X, b.Y); }
        private static float Dist(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx, dy = ay - by; return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    internal static class SkillRowExt
    {
        /// <summary>자신에게만 거는 버프 스킬(범위 1 이하의 Self 상태)인지 — 전투 AI 가 적을 향해 쓰지 않도록 거른다.</summary>
        public static bool TargetsSelfOnly(this SkillRow sk, GameData d)
        {
            StatusRow st = d.Status(sk.StatusId);
            return st != null && st.TargetsSelf && sk.DamagePercent == 0 && sk.FlatDamage == 0;
        }
    }
}
