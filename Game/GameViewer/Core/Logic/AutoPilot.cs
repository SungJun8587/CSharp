//***************************************************************************
// AutoPilot.cs : 뷰어 캐릭터를 자동으로 싸우게 하는 단순 AI (UI 비의존).
//  Off   : 아무것도 안 함
//  Chase : 지정한 대상(TargetId)을 쫓아가 기본 공격 + 스킬 (대상이 죽거나 사라지면 Off)
//  Auto  : 가장 가까운 살아있는 몬스터를 골라 Chase, 없으면 스폰 지점을 순회하며 탐색
// 서버가 권위이므로 이 AI 는 "요청"만 보낸다 (사거리/쿨다운/시야는 서버가 다시 검증한다).
//***************************************************************************
using System;
using System.Collections.Generic;
using GameViewer.Core.Data;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;

namespace GameViewer.Core.Logic
{
    public enum PilotMode { Off, Chase, Auto }

    public sealed class AutoPilot
    {
        private readonly ViewerSession _s;
        private readonly Dictionary<ulong, long> _blacklist = new Dictionary<ulong, long>();
        private long _nextThinkMs;
        private long _lastMoveMs = -100000;
        private long _lastAttackMs = -100000;
        private float _lastDestX = float.NaN, _lastDestY = float.NaN;
        private int _spawnIndex;
        private long _roamSinceMs;
        private int _blockedHits;
        private long _seenCorrectMs;

        public PilotMode Mode = PilotMode.Off;
        public ulong TargetId;
        public string Note = "";            // 화면 표시용 현재 행동

        public AutoPilot(ViewerSession session) { _s = session; }

        public void SetAuto(bool on)
        {
            Mode = on ? PilotMode.Auto : PilotMode.Off;
            if (!on) TargetId = 0;
        }

        public void ChaseTarget(ulong id)
        {
            TargetId = id;
            Mode = id != 0 ? PilotMode.Chase : PilotMode.Off;
            _blockedHits = 0;
        }

        public void Update(long now)
        {
            if (Mode == PilotMode.Off) { Note = ""; return; }
            if (now < _nextThinkMs) return;
            _nextThinkMs = now + 100;

            if (!_s.CanAct) { Note = "대기(사망/미접속)"; return; }
            WorldModel w = _s.World;
            Entity self = w.Self;
            if (self.Casting) { Note = "시전 중"; return; }
            if (_s.SkillPending(now)) { Note = "시전 응답 대기"; return; }

            ObserveCorrections(now, w);

            Entity target = ResolveTarget(now, w);
            float sx, sy; self.PosAt(now, out sx, out sy);

            if (target == null)
            {
                if (Mode == PilotMode.Chase) { Mode = PilotMode.Off; TargetId = 0; Note = ""; return; }
                Roam(now, sx, sy);
                return;
            }

            float tx, ty; target.PosAt(now, out tx, out ty);
            float dx = tx - sx, dy = ty - sy;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            HeroRow hero = _s.Data.Hero(_s.HeroId);
            float atkRange = hero != null ? (float)hero.AttackRange : 2.5f;
            long atkInterval = hero != null && hero.AttackIntervalMs > 0 ? hero.AttackIntervalMs : 800;

            // 1) 스킬: 쿨다운이 끝난 것 중 사거리에 들어온 첫 번째
            List<uint> skills = _s.MySkills;
            for (int i = 0; i < skills.Count; ++i)
            {
                SkillRow sk = _s.Data.Skill(skills[i]);
                if (sk == null || _s.SkillCooldownLeftMs(sk.Id, now) > 0) continue;
                if (!SkillWanted(sk, self, dist)) continue;
                // 시전 시간이 있는 스킬은 서버가 시전을 시작하면서 이동을 멈춘다. 여기서 따로 멈춤 요청을 보내면 그 요청이 시전 직전·직후에 닿아 시전을 취소할 수 있다.
                SkillAimResult aim = SkillAim.Resolve(sk, w, sk.IsAlly ? null : target, false, 0f, 0f, now);
                if (!aim.Ok) continue;
                if (_s.UseSkill(sk.Id, aim.TargetId, aim.X, aim.Y, now)) { Note = "스킬 " + sk.Name; return; }
            }

            // 2) 기본 공격 사거리 안이면 멈추고 공격
            if (dist <= atkRange - 0.2f)
            {
                if (self.Moving) _s.StopMoving(now);
                if (now - _lastAttackMs >= atkInterval && _s.Attack(target.Id, now)) _lastAttackMs = now;
                Note = "공격 " + w.NameOf(target);
                return;
            }

            // 3) 접근: 사거리의 85% 지점까지 간다
            float stop = Math.Max(0.5f, atkRange * 0.85f);
            float k = dist > 0.001f ? (dist - stop) / dist : 0f;
            MoveIfNeeded(now, sx + dx * k, sy + dy * k, 1.0f);
            Note = "추적 " + w.NameOf(target) + " (" + dist.ToString("0.0") + ")";
        }

        // ------------------------------------------------------------------

        /// <summary>이 스킬을 지금 쓸 만한지. 적 스킬은 대상이 효과 범위 안일 때, 아군 스킬(치유/보호막)은 체력이 줄었을 때만 쓴다.</summary>
        private static bool SkillWanted(SkillRow sk, Entity self, float distToTarget)
        {
            if (sk.IsAlly)
            {
                float hpRatio = self.MaxHp > 0 ? self.Hp / (float)self.MaxHp : 1f;
                if (sk.Heals) return hpRatio < 0.65f;
                return hpRatio < 0.85f && (sk.StatusId == 0 || !self.HasStatus(sk.StatusId));       // 보호막 등 상태만 거는 아군 스킬
            }
            // 자기 중심 버프(전투 함성, 집중, 그림자 질주): 피해가 없고 상태만 거는 스킬. 교전 직전에 한 번 걸고, 이미 걸려 있으면 다시 쓰지 않는다.
            if (sk.IsArea && sk.Range <= 1.5 && sk.DamagePercent == 0 && sk.FlatDamage == 0)
                return sk.StatusId != 0 && !self.HasStatus(sk.StatusId) && distToTarget <= 9.0;
            if (sk.IsSelf) return distToTarget <= sk.Radius - 0.3;
            return distToTarget <= sk.Range - 0.3;
        }

        private Entity ResolveTarget(long now, WorldModel w)
        {
            Entity t = TargetId != 0 ? w.Find(TargetId) : null;
            if (t != null && !t.Dead && !IsBlacklisted(t.Id, now)) return t;

            if (Mode == PilotMode.Chase) return null;      // 지정 대상이 사라지면 종료

            // Auto: 가장 가까운 살아있는 몬스터
            float sx, sy; w.Self.PosAt(now, out sx, out sy);
            Entity best = null; float bestD2 = float.MaxValue;
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsMonster || e.Dead || IsBlacklisted(e.Id, now)) continue;
                float ex, ey; e.PosAt(now, out ex, out ey);
                float d2 = (ex - sx) * (ex - sx) + (ey - sy) * (ey - sy);
                if (d2 < bestD2) { bestD2 = d2; best = e; }
            }
            TargetId = best != null ? best.Id : 0;
            _blockedHits = 0;
            return best;
        }

        private bool IsBlacklisted(ulong id, long now)
        {
            long until;
            if (!_blacklist.TryGetValue(id, out until)) return false;
            if (now < until) return true;
            _blacklist.Remove(id);
            return false;
        }

        // 장애물에 막혀 위치 보정이 반복되면 그 대상을 잠시 포기한다.
        private void ObserveCorrections(long now, WorldModel w)
        {
            if (w.LastCorrectMs == _seenCorrectMs) return;
            _seenCorrectMs = w.LastCorrectMs;
            if (w.LastCorrectReason != CorrectPosReason.Blocked && w.LastCorrectReason != CorrectPosReason.PositionMismatch) return;
            _lastDestX = float.NaN;
            if (++_blockedHits >= 3 && TargetId != 0)
            {
                _blacklist[TargetId] = now + 5000;
                if (Mode == PilotMode.Chase) { Mode = PilotMode.Off; }
                TargetId = 0; _blockedHits = 0;
            }
        }

        private void Roam(long now, float sx, float sy)
        {
            List<SpawnRow> spawns = _s.Data.Spawns;
            if (spawns.Count == 0) { Note = "탐색할 스폰 정보 없음"; return; }
            SpawnRow sp = spawns[_spawnIndex % spawns.Count];
            float dx = sp.X - sx, dy = sp.Y - sy;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (_roamSinceMs == 0) _roamSinceMs = now;
            if (dist < 6f || now - _roamSinceMs > 25000)
            {
                _spawnIndex++; _roamSinceMs = now; _lastDestX = float.NaN;
                return;
            }
            MoveIfNeeded(now, sp.X, sp.Y, 3.0f);
            Note = "탐색 → 몬스터 #" + sp.MonsterId + " 지역 (" + dist.ToString("0") + ")";
        }

        private void MoveIfNeeded(long now, float x, float y, float minDelta)
        {
            if (now - _lastMoveMs < 300) return;
            bool far = float.IsNaN(_lastDestX) || Math.Abs(_lastDestX - x) > minDelta || Math.Abs(_lastDestY - y) > minDelta;
            if (!far && _s.World.Self.Moving) return;
            if (_s.MoveTo(x, y, now, false)) { _lastMoveMs = now; _lastDestX = x; _lastDestY = y; }
        }
    }
}
