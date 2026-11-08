using System;
using System.Collections.Generic;
using GameViewer.Core.Data;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;
using Pk = GameViewer.Core.Protocol.ServerPackets;

namespace GameViewer.Tests
{
    public static class WorldTests
    {
        const ulong Self = 100;
        const ulong M1 = 0x8000000000000001UL;

        static WorldModel NewWorld()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            WorldModel w = new WorldModel(d, Self);
            w.InitSelf(10, 10, 1, 0);
            return w;
        }

        static void Apply(WorldModel w, byte[] pkt, long now) { w.Apply(Pk.F(pkt), now); }

        static int CountFx(WorldModel w, EffectKind k)
        {
            int n = 0; foreach (ViewEffect f in w.Effects) if (f.Kind == k) ++n; return n;
        }

        public static void MoveAndSpawn()
        {
            WorldModel w = NewWorld();
            T.Check(w.Self != null && w.Self.Hp == 150, "self init from hero data");
            Apply(w, Pk.Spawn(M1, EntityType.Monster, 50, 50, 50, 50, 3, false, 100, 100, false, 1), 0);
            T.Eq(2, w.Entities.Count, "two entities");
            T.Eq("슬라임", w.NameOf(w.Find(M1)), "monster name");

            Apply(w, Pk.Move(M1, 50, 50, 60, 50, 5), 1000);
            float x, y;
            w.Find(M1).PosAt(2000, out x, out y);
            T.Near(55, x, 1e-3, "interpolated x after 1s"); T.Near(50, y, 1e-3, "interpolated y");
            w.Find(M1).PosAt(9000, out x, out y);
            T.Near(60, x, 1e-3, "clamped at dest");
            w.Find(M1).PosAt(500, out x, out y);
            T.Near(50, x, 1e-3, "not before start");

            Apply(w, Pk.Stop(M1, 57, 50, false), 3000);
            w.Find(M1).PosAt(5000, out x, out y);
            T.Near(57, x, 1e-3, "stopped position"); T.Check(!w.Find(M1).Moving, "stopped flag");

            // 이동 중인 스폰(시야 진입)
            Apply(w, Pk.Spawn(2, EntityType.Player, 0, 0, 10, 0, 5, true, 50, 50, false, 2), 4000);
            w.Find(2).PosAt(5000, out x, out y);
            T.Near(5, x, 1e-3, "spawn while moving");

            Apply(w, Pk.Correct(CorrectPosReason.Blocked, 33, 44), 6000);
            w.Self.PosAt(7000, out x, out y);
            T.Near(33, x, 1e-3, "correct pos x"); T.Near(44, y, 1e-3, "correct pos y");
            T.Eq(CorrectPosReason.Blocked, w.LastCorrectReason, "correct reason");

            Apply(w, Pk.Despawn(M1), 7000);
            T.Check(w.Find(M1) == null, "despawned");

            // 알 수 없는 타입 / 깨진 패킷은 세고 무시
            w.Apply(new Frame(2999, new byte[8]), 0);
            T.Eq(1L, w.UnknownPacketCount, "unknown counted");
            w.Apply(new Frame((ushort)ZonePacketType.ZcSpawnNotify, new byte[6]), 0);
            T.Eq(1L, w.MalformedPacketCount, "malformed counted");
        }

        public static void DamageDeathRespawn()
        {
            WorldModel w = NewWorld();
            Apply(w, Pk.Spawn(M1, EntityType.Monster, 50, 50, 50, 50, 3, false, 100, 100, false, 1), 0);

            Apply(w, Pk.Damage(Self, M1, 30, 70, 100), 1000);
            T.Eq(70, w.Find(M1).Hp, "monster hp after damage");
            T.Eq(30L, w.DamageDealtBySelf, "dealt counter");
            T.Eq(1, CountFx(w, EffectKind.Tracer), "far basic attack draws a tracer");
            ViewEffect dmgFx = null; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Damage) dmgFx = f;
            T.Check(dmgFx != null && dmgFx.Value == 30 && dmgFx.Variant == 3, "damage number by self (variant 3)");

            Apply(w, Pk.Damage(0, M1, 5, 65, 100), 1100);
            int dots = 0; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Damage && f.Variant == 1) ++dots;
            T.Eq(1, dots, "DoT damage variant 1");
            T.Eq(1, CountFx(w, EffectKind.Tracer), "DoT draws no tracer");

            Apply(w, Pk.Damage(M1, Self, 12, 138, 150), 1200);
            T.Eq(12L, w.DamageTakenBySelf, "taken counter");
            T.Eq(138, w.Self.Hp, "self hp");

            // 상태 효과를 건 뒤 사망 -> 상태 비움, 시전 취소
            Apply(w, Pk.Status(M1, 1, Self, 6000, true), 1300);
            T.Check(w.Find(M1).HasStatus(1), "status applied");
            Apply(w, Pk.Cast(M1, 4, 0, 40, 40, 600), 1350);
            T.Check(w.Find(M1).Casting, "monster casting");
            Apply(w, Pk.Death(M1, Self), 1400);
            Entity m = w.Find(M1);
            T.Check(m.Dead && m.Hp == 0, "dead");
            T.Eq(0, m.Statuses.Count, "statuses cleared on death");
            T.Check(!m.Casting, "cast cleared on death");
            T.Eq(0, CountFx(w, EffectKind.Telegraph), "telegraph dropped on death");
            T.Eq(1L, w.KillsBySelf, "kill counted");
            T.Eq(1, CountFx(w, EffectKind.DeathPuff), "death puff");

            Apply(w, Pk.Respawn(M1, 52, 52, 100, 100, 2000), 5000);
            m = w.Find(M1);
            T.Check(!m.Dead && m.Hp == 100, "respawned");
            T.Eq(7000L, m.InvulnUntilMs, "invulnerable until");
            float x, y; m.PosAt(5000, out x, out y);
            T.Near(52, x, 1e-3, "respawn pos");

            // 회복
            Apply(w, Pk.Damage(M1, Self, 100, 38, 150), 5100);
            Apply(w, Pk.Heal(Self, 20, 58, 150), 5200);
            T.Eq(58, w.Self.Hp, "heal hp");
            T.Eq(1, CountFx(w, EffectKind.Heal), "heal number");

            // 내가 죽음
            Apply(w, Pk.Death(Self, M1), 6000);
            T.Eq(1L, w.SelfDeaths, "self death counted");

            // 시간이 지나면 연출이 정리된다
            w.Update(60000);
            T.Eq(0, w.Effects.Count, "effects pruned");
        }

        public static void Skills()
        {
            WorldModel w = NewWorld();
            List<string> used = new List<string>();
            w.SelfSkillUsed += delegate (uint id, bool cast, long now) { used.Add(id + (cast ? "c" : "n")); };
            Apply(w, Pk.Spawn(M1, EntityType.Monster, 30, 30, 30, 30, 3, false, 100, 100, false, 1), 0);

            // 시전 시간이 있는 범위 스킬(화살비: 반경 5, 시전 600ms)
            Apply(w, Pk.Cast(Self, 4, 0, 30, 30, 600), 1000);
            T.Check(w.Self.Casting && w.Self.CastSkillId == 4, "casting state");
            ViewEffect tele = null; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Telegraph) tele = f;
            T.Check(tele != null && tele.Radius == 5f, "telegraph with skill radius");
            Apply(w, Pk.Skill(Self, 4, 0, 30, 30, 1), 1600);
            T.Check(!w.Self.Casting, "cast ends on skill notify");
            T.Eq(0, CountFx(w, EffectKind.Telegraph), "telegraph removed");
            ViewEffect ring = null; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Ring) ring = f;
            T.Check(ring != null && ring.Radius == 5f, "impact ring with radius");
            T.Eq("4c", string.Join(",", used.ToArray()), "cooldown hook once (at cast start only)");

            // 피해가 스킬 직후에 오면 기본 공격 궤적을 따로 그리지 않는다
            int before = CountFx(w, EffectKind.Tracer) + CountFx(w, EffectKind.Slash);
            Apply(w, Pk.Damage(Self, M1, 50, 50, 100), 1610);
            int after = CountFx(w, EffectKind.Tracer) + CountFx(w, EffectKind.Slash);
            T.Eq(before, after, "skill damage draws no basic-attack line");

            // 시전 취소
            Apply(w, Pk.Cast(Self, 6, 0, 20, 20, 1200), 3000);
            T.Check(w.Self.Casting, "casting again");
            Apply(w, Pk.Cancel(Self, 6, SkillCancelReason.Moved), 3100);
            T.Check(!w.Self.Casting, "cancel clears cast");
            T.Eq(0, CountFx(w, EffectKind.Telegraph), "cancel drops telegraph");

            // 투사체: 발사 -> 착탄
            used.Clear();
            Apply(w, Pk.Projectile(9, Self, 3, M1, 10, 10, 30, 30, 400), 5000);
            T.Eq(1, CountFx(w, EffectKind.Projectile), "projectile in flight");
            T.Eq("3n", string.Join(",", used.ToArray()), "instant projectile skill triggers cooldown hook");
            Apply(w, Pk.Impact(9, Self, 3, 31, 29, 1), 5400);
            w.Update(5400);
            T.Eq(0, CountFx(w, EffectKind.Projectile), "projectile removed on impact");
            T.Check(CountFx(w, EffectKind.Spark) >= 1, "impact spark");

            // 타격 충격은 공격자와 피해량을 알아야 무기 색/크기를 고른다
            {
                WorldModel hw = NewWorld();
                ViewEffect hit = null;
                Apply(hw, Pk.Spawn(M1, EntityType.Monster, 50, 50, 50, 50, 3, false, 100, 100, false, 1), 0);
                Apply(hw, Pk.Damage(Self, M1, 50, 50, 100), 200);
                foreach (ViewEffect f in hw.Effects) if (f.Kind == EffectKind.Spark) hit = f;
                T.Check(hit != null && hit.TargetId == Self && hit.Value == 50 && hit.Variant == 1, "spark carries attacker, damage and heavy flag");
            }

            // 착탄 알림이 유실돼도 결국 정리된다
            Apply(w, Pk.Projectile(10, Self, 3, M1, 10, 10, 30, 30, 400), 8000);
            w.Update(8000 + 400 + 700);
            T.Eq(0, CountFx(w, EffectKind.Projectile), "orphan projectile expires");

            // 거부 알림은 로그에 남는다
            int logs = w.Log.Count;
            Apply(w, Pk.SkillReject(1, SkillRejectReason.Cooldown), 9000);
            Apply(w, Pk.AttackReject(M1, AttackRejectReason.OutOfRange), 9000);
            T.Eq(logs + 2, w.Log.Count, "rejects logged");
        }

        public static void Status()
        {
            WorldModel w = NewWorld();
            Apply(w, Pk.Spawn(M1, EntityType.Monster, 30, 30, 30, 30, 3, false, 100, 100, false, 1), 0);
            Apply(w, Pk.Status(M1, 1, Self, 6000, true), 1000);
            T.Check(w.Find(M1).HasStatus(1), "applied");
            T.Eq("화상", FirstText(w), "status name popup");
            Apply(w, Pk.Status(M1, 1, Self, 9000, true), 2000);
            T.Eq(1, w.Find(M1).Statuses.Count, "refresh keeps one entry");
            T.Eq(11000L, w.Find(M1).Statuses[1].ExpireMs, "refresh extends expiry");
            Apply(w, Pk.Status(M1, 1, 0, 0, false), 3000);
            T.Check(!w.Find(M1).HasStatus(1), "released");

            // 해제 알림이 유실돼도 만료 1초 뒤 정리
            Apply(w, Pk.Status(M1, 2, Self, 5000, true), 10000);
            w.Update(10000 + 5000 + 500);
            T.Check(w.Find(M1).HasStatus(2), "kept shortly after expiry");
            w.Update(10000 + 5000 + 1100);
            T.Check(!w.Find(M1).HasStatus(2), "stale status removed");
        }

        static string FirstText(WorldModel w)
        {
            foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Text) return f.Text;
            return null;
        }
    }
}
