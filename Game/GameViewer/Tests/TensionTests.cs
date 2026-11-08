using System;
using System.Collections.Generic;
using GameViewer.Core;
using GameViewer.Core.Audio;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;
using Pk = GameViewer.Core.Protocol.ServerPackets;

namespace GameViewer.Tests
{
    /// <summary>긴박감 연출 규칙: 저체력 긴장도/박동, 연속 처치, 화면 밖 위협, 보스 경고, 효과음 대응.</summary>
    public static class TensionTests
    {
        const ulong Self = 100;
        const ulong Mon = 0x8000000000000001UL, Boss = 0x8000000000000100UL;

        static WorldModel NewWorld()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            WorldModel w = new WorldModel(d, Self);
            w.InitSelf(10, 10, 1, 0);
            return w;
        }
        static void Apply(WorldModel w, byte[] pkt, long now) { w.Apply(Pk.F(pkt), now); }
        static int Count(WorldModel w, EffectKind k, int variant) { int n = 0; foreach (ViewEffect f in w.Effects) if (f.Kind == k && f.Variant == variant) ++n; return n; }

        public static void LowHp()
        {
            T.Near(0, Tension.Level(100, 100), 1e-6, "full hp: calm");
            T.Near(0, Tension.Level(40, 100), 1e-6, "40% is the threshold");
            T.Check(Tension.Level(39, 100) > 0f && Tension.Level(39, 100) < 0.1f, "just under the threshold: barely");
            T.Near(1, Tension.Level(5, 100), 1e-6, "5% is the maximum");
            T.Near(1, Tension.Level(1, 100), 1e-6, "below 5% stays at the maximum");
            T.Near(0, Tension.Level(0, 100), 1e-6, "dead: calm (no heartbeat)");
            T.Near(0, Tension.Level(10, 0), 1e-6, "unknown max hp: calm");
            T.Eq(1000, Tension.HeartPeriodMs(0f), "60 bpm when calm");
            T.Eq(430, Tension.HeartPeriodMs(1f), "about 140 bpm at the worst");
            T.Check(Tension.HeartPeriodMs(0.3f) > Tension.HeartPeriodMs(0.6f), "lower hp beats faster");
            // 박동: 시작 지점이 가장 세고, 두 번째 쿵은 더 약하며, 사이에서 가라앉는다
            int period = 800;
            float lub = Tension.Pulse(0, period), valley = Tension.Pulse(100, period), dub = Tension.Pulse((long)(period * 0.2f), period), tail = Tension.Pulse(700, period);
            T.Check(lub > 0.99f, "lub at the start");
            T.Check(valley < 0.4f && dub > valley && dub < lub, "dub after a short dip, weaker than lub");
            T.Check(tail < 0.2f, "settles before the next beat");
            T.Check(Tension.Pulse(-300, period) >= 0f, "negative time is safe");
            T.Near(0, Tension.Pulse(5, 0), 1e-6, "zero period is safe");
        }

        public static void Heartbeat()
        {
            HeartbeatClock c = new HeartbeatClock();
            T.Check(!c.Tick(1000, 0f, true), "silent when healthy");
            T.Check(!c.Tick(1000, 0.5f, false), "silent when dead");
            T.Check(c.Tick(1000, 0.5f, true), "first beat is immediate");
            T.Check(!c.Tick(1100, 0.5f, true), "no double beat");
            int period = Tension.HeartPeriodMs(0.5f);
            T.Check(c.Tick(1000 + period, 0.5f, true), "next beat after one period");
            // 더 위험해지면 간격이 짧아진다
            long t = 1000 + period; int beats = 0;
            for (long now = t + 1; now <= t + 3000; now += 10) if (c.Tick(now, 1f, true)) ++beats;
            T.Check(beats >= 5, "fast beats at max tension: " + beats);
            T.Check(!c.Tick(9000, 0.5f, false), "death stops it");
            T.Check(c.Tick(9010, 0.5f, true), "restarts right away after respawn");
        }

        public static void Streak()
        {
            KillStreak k = new KillStreak();
            T.Eq(0, k.Active(0), "none at first");
            T.Eq(1, k.Register(1000), "first kill");
            T.Eq(2, k.Register(1000 + KillStreak.WindowMs), "inside the window continues");
            T.Eq(2, k.Active(1000 + KillStreak.WindowMs), "active");
            long last = 1000 + KillStreak.WindowMs;
            T.Eq(0, k.Active(last + KillStreak.WindowMs + 1), "expired");
            T.Eq(1, k.Register(last + KillStreak.WindowMs + 1), "after the window restarts");
            T.Eq(2, k.Best, "best streak is remembered");
            k.Register(last + KillStreak.WindowMs + 100);
            T.Near(1.0, k.Remaining(last + KillStreak.WindowMs + 100), 1e-6, "full bar right after a kill");
            T.Near(0.5, k.Remaining(last + KillStreak.WindowMs + 100 + KillStreak.WindowMs / 2), 0.01, "half bar");
            k.Reset(); T.Eq(0, k.Active(last + KillStreak.WindowMs + 100), "reset");
            T.Eq("", KillStreak.Title(1), "no title for one kill");
            T.Check(KillStreak.Title(2).Contains("더블") && KillStreak.Title(3).Contains("트리플") && KillStreak.Title(4).Contains("쿼드라"), "titles 2-4");
            T.Check(KillStreak.Title(7).Contains("7") && KillStreak.Title(12).Contains("12") && KillStreak.Title(25).Contains("25"), "numbered titles");
            T.Check(KillStreak.Tier(2) == 0 && KillStreak.Tier(3) == 1 && KillStreak.Tier(5) == 2 && KillStreak.Tier(10) == 3, "tiers");
        }

        static void SpawnMon(WorldModel w, ulong id, float x, float y, float dx, float dy, long now, uint tpl = 2)
        {
            Apply(w, Pk.Spawn(id, EntityType.Monster, x, y, dx, dy, 4f, true, 100, 100, false, tpl), now);
        }

        public static void Threats()
        {
            WorldModel w = NewWorld();
            long now = 1000;
            List<Entity> list = new List<Entity>();
            // 화면 반폭 20 x 반높이 12 로 본다. 내 위치 (10,10)
            SpawnMon(w, Mon + 1, 40, 10, 12, 10, now);         // 화면 밖(30칸), 내게 달려옴 -> 위협
            SpawnMon(w, Mon + 2, 15, 10, 11, 10, now);         // 화면 안 -> 제외
            SpawnMon(w, Mon + 3, 45, 10, 80, 10, now);         // 화면 밖이지만 다른 곳으로 감 -> 제외
            SpawnMon(w, Mon + 4, 100, 10, 12, 10, now);        // 너무 멂 -> 제외
            SpawnMon(w, Mon + 5, 10, 30, 10, 12, now);         // 위에서 달려옴, 더 가까움 -> 위협(먼저)
            Apply(w, Pk.Spawn(Boss, EntityType.Monster, 38, 10, 12, 10, 4f, true, 1000, 1000, false, 9), now);   // 보스는 따로 표시하므로 제외
            Tension.Threats(w, 10, 10, now, 34f, 20f, 12f, list, 10);
            T.Eq(2, list.Count, "two real threats");
            T.Check(list.Count == 2 && list[0].Id == Mon + 5 && list[1].Id == Mon + 1, "nearest first");
            Tension.Threats(w, 10, 10, now, 34f, 20f, 12f, list, 1);
            T.Eq(1, list.Count, "capped");
            // 죽은 몬스터는 제외
            Apply(w, Pk.Death(Mon + 5, Self), now);
            Tension.Threats(w, 10, 10, now, 34f, 20f, 12f, list, 10);
            T.Check(list.Count == 1 && list[0].Id == Mon + 1, "dead ones are not threats");
        }

        public static void BossWarning()
        {
            WorldModel w = NewWorld();
            long now = 1000;
            Apply(w, Pk.Spawn(Boss, EntityType.Monster, 50, 50, 50, 50, 3, false, 1000, 1000, false, 9), now);
            T.Eq(1, Count(w, EffectKind.Banner, 1), "spawn warns once");
            Apply(w, Pk.Spawn(Boss, EntityType.Monster, 50, 50, 50, 50, 3, false, 1000, 1000, false, 9), now + 500);
            T.Eq(1, Count(w, EffectKind.Banner, 1), "re-sent spawn (view re-entry) does not warn again");
            T.Check(w.Effects.Exists(delegate (ViewEffect f) { return f.Kind == EffectKind.Banner && f.Variant == 1 && f.Text.Contains("오우거"); }), "warning names the boss");
            // 처치 후 부활하면 다시 경고
            Apply(w, Pk.Death(Boss, Self), now + 1000);
            Apply(w, Pk.Respawn(Boss, 50, 50, 1000, 1000, 0), now + 5000);
            T.Eq(2, Count(w, EffectKind.Banner, 1), "respawn warns again");
            // 격노: 상태가 처음 걸릴 때만
            Entity e = w.Find(Boss);
            uint enrage = e.Boss.EnrageStatusId;
            if (enrage != 0)
            {
                Apply(w, Pk.Status(Boss, enrage, Boss, 600000, true), now + 6000);
                T.Eq(3, Count(w, EffectKind.Banner, 1), "enrage warns");
                Apply(w, Pk.Status(Boss, enrage, Boss, 600000, true), now + 6100);
                T.Eq(3, Count(w, EffectKind.Banner, 1), "refreshing enrage does not warn again");
            }
            // 일반 몬스터는 경고 없음
            WorldModel w2 = NewWorld();
            SpawnMon(w2, Mon + 1, 20, 20, 20, 20, now);
            T.Eq(0, Count(w2, EffectKind.Banner, 1), "normal monsters do not raise a warning");
        }

        public static void StreakInWorld()
        {
            WorldModel w = NewWorld();
            long now = 1000;
            for (ulong i = 1; i <= 3; ++i) SpawnMon(w, Mon + i, 20, 20, 20, 20, now);
            Apply(w, Pk.Death(Mon + 1, Self), now);
            T.Eq(0, Count(w, EffectKind.Streak, 0), "one kill: no toast");
            Apply(w, Pk.Death(Mon + 2, Self), now + 2000);
            T.Eq(1, Count(w, EffectKind.Streak, 0), "second kill: toast");
            Apply(w, Pk.Death(Mon + 3, 0), now + 2500);        // 남이 처치: 내 연속에 영향 없음
            T.Eq(1, Count(w, EffectKind.Streak, 0), "someone else's kill is not mine");
            T.Eq(2, w.Streak.Active(now + 2500), "streak count");
            Apply(w, Pk.Death(Self, Mon + 1), now + 3000);
            T.Eq(0, w.Streak.Active(now + 3000), "dying resets the streak");
            ViewEffect st = null; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Streak) st = f;
            T.Check(st != null && st.Value == 2 && st.Text.Contains("더블"), "toast carries count and title");
        }

        public static void SfxMapping()
        {
            WorldModel w = NewWorld();
            SfxKind k; float g;
            ViewEffect warn = new ViewEffect { Kind = EffectKind.Banner, Variant = 1, DurMs = 1000 };
            T.Check(SfxRules.TryMap(warn, w, 0, 0, out k, out g) && k == SfxKind.Alarm && g == 1f, "warning banner sounds the alarm");
            ViewEffect st = new ViewEffect { Kind = EffectKind.Streak, DurMs = 1000, X = 500, Y = 500 };
            T.Check(SfxRules.TryMap(st, w, 0, 0, out k, out g) && k == SfxKind.Streak && g == 1f, "streak chime ignores distance");
            float[] hb = SfxSynth.Make(SfxKind.Heartbeat, SfxSynth.DefaultRate);
            T.Check(hb.Length > SfxSynth.DefaultRate * 0.2f && hb.Length < SfxSynth.DefaultRate * 0.5f, "heartbeat is a short double thump");
        }
    }
}
