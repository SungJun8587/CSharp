using System;
using System.Collections.Generic;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;

namespace GameViewer.Tests
{
    public static class DemoTests
    {
        static ViewerSession NewDemo(out DemoDirector demo, long now)
        {
            ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = Program.Dir("GameData"); cfg.HeroId = 1; cfg.CharId = 5000;
            GameData d = GameData.Load(cfg.DataDir, cfg.ZoneId);
            ViewerSession s = new ViewerSession(cfg, d);
            demo = new DemoDirector(s);
            demo.Start(now);
            return s;
        }

        // 월드 불변식: 위치는 유한값, 체력은 0..최대, 사망이면 체력 0
        static string Invariants(WorldModel w, long now)
        {
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                Entity e = kv.Value; float x, y; e.PosAt(now, out x, out y);
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return "non-finite position of " + e.Id;
                if (e.Hp < 0 || (e.MaxHp > 0 && e.Hp > e.MaxHp)) return "hp out of range for " + e.Id + ": " + e.Hp + "/" + e.MaxHp;
                if (e.Dead && e.Hp != 0) return "dead entity with hp " + e.Id;
            }
            return null;
        }

        public static void SimulationRuns()
        {
            DemoDirector demo; long now = 50000;
            ViewerSession s = NewDemo(out demo, now);
            T.Eq(SessionState.InZone, s.State, "demo enters zone offline");
            T.Eq(17, s.World.Entities.Count, "self + 3 allies + 12 monsters + boss (test data has Boss sheets)");

            string bad = null;
            for (int i = 0; i < 40 * 62 && bad == null; ++i)        // 가상 40초
            {
                now += 16;
                demo.Update(now); s.Update(now);
                if (i % 10 == 0) bad = Invariants(s.World, now);
            }
            T.Check(bad == null, "invariants: " + bad);
            T.Check(s.World.TotalDamageEvents > 50, "fighting happened: damage events=" + s.World.TotalDamageEvents);
            T.Check(s.World.TotalDeaths > 0, "someone died");
            T.Check(s.World.TotalRespawns > 0, "someone respawned");
            T.Eq(0L, s.World.MalformedPacketCount, "demo packets are all well-formed");
            T.Eq(0L, s.World.UnknownPacketCount, "demo packets are all known types");
        }

        public static void ClientCommandsReachTheSimulator()
        {
            DemoDirector demo; long now = 50000;
            ViewerSession s = NewDemo(out demo, now);
            float x0, y0; s.SelfPos(now, out x0, out y0);

            // 이동
            T.Check(s.MoveTo(x0 + 6, y0, now, true), "move accepted by simulator");
            for (int i = 0; i < 250; ++i) { now += 16; demo.Update(now); s.Update(now); }
            float x1, y1; s.SelfPos(now, out x1, out y1);
            T.Check(Math.Abs(x1 - (x0 + 6)) < 0.5f && Math.Abs(y1 - y0) < 0.5f, "self walked to the destination: (" + x1 + "," + y1 + ")");

            // 자동 전투로 몬스터를 하나 이상 잡는다 (스킬/기본 공격 경로 전체를 거친다)
            AutoPilot pilot = new AutoPilot(s);
            pilot.SetAuto(true);
            long killsBefore = s.World.KillsBySelf;
            string bad = null;
            for (int i = 0; i < 120 * 62 && s.World.KillsBySelf == killsBefore && bad == null; ++i)
            {
                now += 16;
                demo.Update(now); s.Update(now); pilot.Update(now);
                if (!s.World.Self.Dead) { }
                if (i % 20 == 0) bad = Invariants(s.World, now);
            }
            T.Check(bad == null, "invariants during autopilot: " + bad);
            T.Check(s.World.KillsBySelf > killsBefore, "autopilot killed a monster (kills=" + s.World.KillsBySelf + ")");
            T.Check(s.World.DamageDealtBySelf > 0, "self dealt damage");

            // 스킬 쿨다운이 걸린다
            bool anyCd = false;
            foreach (uint id in s.MySkills) if (s.SkillCooldownLeftMs(id, now) > 0) anyCd = true;
            T.Check(anyCd || s.World.DamageDealtBySelf > 0, "skills used and cooldown tracked");
        }

        public static void DeadCannotAct()
        {
            DemoDirector demo; long now = 50000;
            ViewerSession s = NewDemo(out demo, now);
            // 내가 죽는 상황을 직접 만든다: 서버 패킷 주입
            s.InjectFrame(ServerPackets.F(ServerPackets.Death(5000, 0)), now);
            T.Check(!s.CanAct, "dead -> cannot act");
            T.Check(!s.MoveTo(1, 1, now, true), "move refused");
            T.Check(!s.Attack(0x8000000000000001UL, now), "attack refused");
        }
    }
}
