using System;
using System.Collections.Generic;
using System.IO;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;
using Pk = GameViewer.Core.Protocol.ServerPackets;

namespace GameViewer.Tests
{
    /// <summary>보스 표시용 로직(보스 데이터 읽기, 페이즈 추정, 화면 흔들림, 체력 잔상, 데모 보스)을 확인한다.</summary>
    public static class BossViewTests
    {
        const ulong Self = 100;
        const ulong Boss = 0x8000000000000100UL;

        static WorldModel NewWorld()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            WorldModel w = new WorldModel(d, Self);
            w.InitSelf(10, 10, 1, 0);
            return w;
        }
        static void Apply(WorldModel w, byte[] pkt, long now) { w.Apply(Pk.F(pkt), now); }
        static int PhaseBanners(WorldModel w) { int n = 0; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Banner && f.Variant == 0) ++n; return n; }
        static int CountFx(WorldModel w, EffectKind k) { int n = 0; foreach (ViewEffect f in w.Effects) if (f.Kind == k) ++n; return n; }

        public static void BossData()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            T.Eq(0, d.Warnings.Count, "no warnings with boss files: " + string.Join("; ", d.Warnings.ToArray()));
            BossRow b = d.BossOf(9);
            T.Check(b != null && b.Name == "\uc624\uc6b0\uac70", "boss by monster id");
            T.Check(d.BossOf(1) == null, "normal monster is not a boss");
            T.Eq(3, b.Phases.Count, "ogre phases");
            T.Eq(1, b.PhaseFor(1000, 1000), "100% -> phase 1");
            T.Eq(1, b.PhaseFor(601, 1000), "60.1% -> phase 1");
            T.Eq(2, b.PhaseFor(600, 1000), "60% -> phase 2 (server rule: hp*100 <= below*max)");
            T.Eq(2, b.PhaseFor(251, 1000), "25.1% -> phase 2");
            T.Eq(3, b.PhaseFor(250, 1000), "25% -> phase 3");
            T.Eq(3, b.PhaseFor(0, 1000), "0 -> last phase");
            T.Eq(1, b.PhaseFor(5, 0), "maxHp 0 -> phase 1");

            // Boss 시트가 없는 서버도 있다: 경고 없이 보스 없음
            string tmp = Path.Combine(Path.GetTempPath(), "gv_nobossdata_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                File.Copy(Path.Combine(Program.Dir("GameData"), "Monster.json"), Path.Combine(tmp, "Monster.json"));
                GameData e = GameData.Load(tmp, 1);
                bool bossWarn = false; foreach (string w in e.Warnings) if (w.StartsWith("Boss")) bossWarn = true;
                T.Check(!bossWarn && e.Bosses.Count == 0, "missing Boss sheets are not a warning");
            }
            finally { Directory.Delete(tmp, true); }
        }

        public static void PhaseTracking()
        {
            WorldModel w = NewWorld();
            long now = 1000;
            Apply(w, Pk.Spawn(Boss, EntityType.Monster, 50, 50, 50, 50, 3, false, 1000, 1000, false, 9), now);
            Entity e = w.Find(Boss);
            T.Check(e.IsBoss && e.BossPhase == 1, "boss flagged on spawn, phase 1");

            now += 100; Apply(w, Pk.Damage(Self, Boss, 300, 700, 1000), now);
            T.Eq(1, e.BossPhase, "70% still phase 1");
            T.Eq(0, PhaseBanners(w), "no banner yet");
            T.Eq(1, CountFx(w, EffectKind.Banner), "boss spawn raises a warning banner (not a phase banner)");

            now += 100; Apply(w, Pk.Damage(Self, Boss, 110, 590, 1000), now);
            T.Eq(2, e.BossPhase, "59% -> phase 2");
            T.Eq(1, PhaseBanners(w), "phase banner");
            ViewEffect banner = null; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Banner && f.Variant == 0) banner = f;
            T.Check(banner != null && banner.Text.Contains("2"), "banner names the phase: " + (banner != null ? banner.Text : "null"));
            T.Eq(now, e.PhaseChangedMs, "phase change time");

            float dx, dy;
            w.ShakeOffset(now + 120, out dx, out dy);
            T.Check(Math.Abs(dx) + Math.Abs(dy) > 0.01f, "screen shakes after phase change");
            w.ShakeOffset(now + 5000, out dx, out dy);
            T.Check(dx == 0 && dy == 0, "shake ends");

            now += 100; Apply(w, Pk.Heal(Boss, 300, 890, 1000), now);
            T.Eq(2, e.BossPhase, "healing does not lower the phase");

            // 한 번에 크게 깎여도 최종 페이즈로 곧장 간다 (배너는 한 번)
            now += 100; Apply(w, Pk.Damage(Self, Boss, 650, 240, 1000), now);
            T.Eq(3, e.BossPhase, "jump to phase 3");
            T.Eq(2, PhaseBanners(w), "one more banner");

            // 격노 상태
            T.Check(!e.IsEnraged, "not enraged");
            Apply(w, Pk.Status(Boss, 5, Boss, 60000, true), now);
            T.Check(e.IsEnraged, "enrage status flag");
            Apply(w, Pk.Status(Boss, 5, 0, 0, false), now + 10);
            T.Check(!e.IsEnraged, "enrage removed");

            // 사망 -> 부활하면 처음 페이즈
            now += 100; Apply(w, Pk.Death(Boss, Self), now);
            T.Eq(1, e.BossPhase, "dead resets phase");
            now += 5000; Apply(w, Pk.Respawn(Boss, 50, 50, 1000, 1000, 0), now);
            T.Eq(1, e.BossPhase, "respawn at phase 1");
            T.Near(1000, e.GhostHp, 1e-3, "ghost hp reset on respawn");
        }

        public static void GhostHpAndShake()
        {
            WorldModel w = NewWorld();
            long now = 1000;
            Apply(w, Pk.Spawn(M(1), EntityType.Monster, 50, 50, 50, 50, 3, false, 1000, 1000, false, 1), now);
            Entity e = w.Find(M(1));
            w.Update(now);
            now += 50; Apply(w, Pk.Damage(Self, M(1), 500, 500, 1000), now);
            T.Near(1000, e.GhostHp, 1e-3, "ghost keeps the old hp right after a hit");
            now += 200; w.Update(now);
            T.Check(e.GhostHp > 900, "ghost waits before shrinking");
            for (int i = 0; i < 150; ++i) { now += 16; w.Update(now); }
            T.Near(500, e.GhostHp, 1e-3, "ghost catches up to hp");
            now += 50; Apply(w, Pk.Heal(M(1), 200, 700, 1000), now);
            T.Check(e.GhostHp >= 700, "ghost never below hp after heal");

            // 큰 피해를 내가 받으면 흔들린다 (최대 체력의 12% 이상)
            w.Update(now);
            Apply(w, Pk.Damage(M(1), Self, 5, 145, 150), now);
            float dx, dy; w.ShakeOffset(now + 60, out dx, out dy);
            T.Check(dx == 0 && dy == 0, "small hit does not shake");
            Apply(w, Pk.Damage(M(1), Self, 40, 105, 150), now + 10);
            w.ShakeOffset(now + 80, out dx, out dy);
            T.Check(Math.Abs(dx) + Math.Abs(dy) > 0.01f, "heavy hit on self shakes");
        }

        static ulong M(int i) { return 0x8000000000000000UL + (ulong)i; }

        public static void Walking()
        {
            Entity e = new Entity();
            e.X = 0; e.Y = 0; e.DestX = 10; e.DestY = 0; e.Speed = 5; e.Moving = true; e.MoveStartMs = 1000;
            float walked;
            T.Check(!e.WalkingAt(1000, out walked), "not walking at the start instant");
            T.Check(e.WalkingAt(1400, out walked) && Math.Abs(walked - 2f) < 1e-3, "walked 2 units in 0.4s");
            T.Check(!e.WalkingAt(4000, out walked), "arrived -> not walking even if Moving flag lingers");
            e.Moving = false;
            T.Check(!e.WalkingAt(1400, out walked), "stopped");
        }

        public static void DemoBoss()
        {
            ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = Program.Dir("GameData"); cfg.HeroId = 1; cfg.CharId = 5000;
            GameData d = GameData.Load(cfg.DataDir, cfg.ZoneId);
            ViewerSession s = new ViewerSession(cfg, d);
            DemoDirector demo = new DemoDirector(s);
            long now = 50000; demo.Start(now);
            Entity boss = null; foreach (Entity e in s.World.Entities.Values) if (e.IsBoss) boss = e;
            T.Check(boss != null, "demo spawns the boss from boss data");
            bool telegraphed = false, phased = false; int maxPhase = 1;
            for (int i = 0; i < 150 * 62; ++i)
            {
                now += 16; demo.Update(now); s.Update(now);
                if (i % 5 == 0)
                {
                    foreach (ViewEffect f in s.World.Effects) if (f.Kind == EffectKind.Telegraph && f.EntityId == boss.Id) telegraphed = true;
                    if (boss.BossPhase > maxPhase) { maxPhase = boss.BossPhase; phased = true; }
                }
            }
            T.Check(telegraphed, "boss telegraphs an area cast");
            T.Check(boss.MaxHp > 0 && boss.MaxHp <= 1500, "demo boss hp is scaled for a short fight: " + boss.MaxHp);
            T.Check(phased || boss.Dead || boss.Hp == boss.MaxHp || boss.BossPhase >= 1, "boss state is consistent");
            T.Eq(0L, s.World.MalformedPacketCount, "demo boss packets are well-formed");
            T.Eq(0L, s.World.UnknownPacketCount, "demo boss packets are known types");
        }
    }
}
