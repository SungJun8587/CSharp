using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GameViewer.Core;
using GameViewer.Core.Audio;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Net;
using GameViewer.Core.World;

namespace GameViewer.Tests
{
    /// <summary>배경음악 / 키 설정 / 스킬 툴팁 / 서버 선택 / 타격감 세기.</summary>
    public static class Live2Tests
    {
        public static void BgmSynthTest()
        {
            int rate = 16000;
            float[] first = null;
            foreach (MapKind k in Enum.GetValues(typeof(MapKind)))
            {
                float[] calm = BgmSynth.Make(k, false, rate), battle = BgmSynth.Make(k, true, rate);
                T.Eq((int)(rate * BgmSynth.LoopSec), calm.Length, k + " loop length");
                T.Eq(calm.Length, battle.Length, k + " battle same length");
                double rc = Rms(calm), rb = Rms(battle);
                T.Check(rc > 0.05 && rc < 0.3, k + " calm level sane " + rc.ToString("0.000"));
                T.Check(rb > rc, k + " battle louder than calm");
                T.Check(Peak(calm) <= 1f && Peak(battle) <= 1f, k + " no clipping");
                T.Check(!HasNaN(calm) && !HasNaN(battle), k + " finite");
                // 반복 재생 때 이음매가 튀지 않는다: 끝과 처음의 차이가 한 샘플 사이의 일반적인 변화보다 크지 않다
                T.Check(Math.Abs(calm[0] - calm[calm.Length - 1]) <= MaxStep(calm), k + " calm loops seamlessly");
                T.Check(Math.Abs(battle[0] - battle[battle.Length - 1]) <= MaxStep(battle), k + " battle loops seamlessly");
                if (first == null) first = calm;
                else T.Check(!SameStart(first, calm), k + " differs from forest");
            }
            float[] a = BgmSynth.Make(MapKind.Snow, false, rate), b = BgmSynth.Make(MapKind.Snow, false, rate);
            T.Check(SameStart(a, b) && a[a.Length / 2] == b[b.Length / 2], "deterministic");
            bool threw = false; try { BgmSynth.Make(MapKind.Snow, false, 100); } catch (ArgumentOutOfRangeException) { threw = true; }
            T.Check(threw, "bad rate rejected");
        }

        static double Rms(float[] s) { double t = 0; for (int i = 0; i < s.Length; ++i) t += s[i] * s[i]; return Math.Sqrt(t / s.Length); }
        static float Peak(float[] s) { float p = 0; for (int i = 0; i < s.Length; ++i) p = Math.Max(p, Math.Abs(s[i])); return p; }
        static bool HasNaN(float[] s) { for (int i = 0; i < s.Length; ++i) if (float.IsNaN(s[i]) || float.IsInfinity(s[i])) return true; return false; }
        static float MaxStep(float[] s) { float m = 0; for (int i = 1; i < s.Length; ++i) m = Math.Max(m, Math.Abs(s[i] - s[i - 1])); return m; }
        static bool SameStart(float[] a, float[] b) { for (int i = 0; i < 4000; ++i) if (a[i] != b[i]) return false; return true; }

        public static void Director()
        {
            BgmDirector d = new BgmDirector();
            T.Check(!d.Update(0, false, MapKind.Forest, false) && !d.Playing, "outside zone: silent, no change");
            T.Check(d.Update(100, true, MapKind.Forest, false) && d.Playing && !d.Battle && d.Kind == MapKind.Forest, "entering zone starts calm track");
            T.Check(!d.Update(200, true, MapKind.Forest, false), "steady: no change");
            T.Check(d.Update(300, true, MapKind.Forest, true) && d.Battle, "boss in view switches to battle at once");
            T.Check(!d.Update(400, true, MapKind.Forest, false) && d.Battle, "boss gone: still battle (delay)");
            T.Check(!d.Update(400 + BgmDirector.CalmDelayMs - 1, true, MapKind.Forest, false) && d.Battle, "still battle just before delay");
            T.Check(d.Update(400 + BgmDirector.CalmDelayMs, true, MapKind.Forest, false) && !d.Battle, "calm returns after delay");
            d.Update(10000, true, MapKind.Forest, true);
            d.Update(10100, true, MapKind.Forest, false);
            d.Update(13000, true, MapKind.Forest, true);                     // 다시 나타나면 기다리던 것이 취소된다
            T.Check(!d.Update(13000 + BgmDirector.CalmDelayMs - 500, true, MapKind.Forest, false) && d.Battle, "re-appearing boss resets the calm timer");
            T.Check(d.Update(20000, true, MapKind.Snow, false) && d.Kind == MapKind.Snow && !d.Battle, "zone change picks that zone's calm track");
            T.Check(d.Update(20100, false, MapKind.Snow, false) && !d.Playing, "leaving zone stops music");
            T.Check(d.Update(20200, true, MapKind.Snow, true) && d.Battle, "re-entering with a boss in view starts battle track");

            WorldModel w = new WorldModel(new GameData(), 1UL);
            T.Check(!BgmDirector.BossAlive(w), "no boss in empty world");
        }

        public static void Keys()
        {
            KeyBindings k = new KeyBindings();
            T.Eq(81, k.Get(BindAction.BasicAttack), "default Q");
            T.Eq(49, k.Get(BindAction.Skill1), "default skill1 = '1'");
            T.Eq(57, k.Get(BindAction.Skill9), "default skill9 = '9'");
            BindAction a;
            T.Check(k.TryActionFor(116, out a) && a == BindAction.Reconnect, "F5 -> reconnect");
            T.Check(k.TryActionFor(90, out a) && a == BindAction.ZoneSelect, "Z -> zone select");
            T.Check(!k.TryActionFor(88, out a), "unbound key has no action");

            BindAction sw;
            T.Eq(BindResult.Ok, k.Set(BindAction.BasicAttack, 88, out sw), "bind to free key");
            T.Eq(88, k.Get(BindAction.BasicAttack), "X bound");
            T.Check(!k.TryActionFor(81, out a), "old key freed");
            T.Eq(BindResult.Swapped, k.Set(BindAction.SelectBoss, 88, out sw), "taking a used key swaps");
            T.Check(sw == BindAction.BasicAttack, "swapped partner reported");
            T.Eq(88, k.Get(BindAction.SelectBoss), "boss key now X"); T.Eq(66, k.Get(BindAction.BasicAttack), "attack got boss's old key B");
            T.Eq(BindResult.Ok, k.Set(BindAction.SelectBoss, 88, out sw), "rebinding the same key is a no-op");
            T.Eq(BindResult.Reserved, k.Set(BindAction.Chat, 27, out sw), "Esc reserved");
            T.Eq(BindResult.Reserved, k.Set(BindAction.Chat, 37, out sw), "arrow reserved");
            T.Eq(BindResult.Reserved, k.Set(BindAction.Chat, 100, out sw), "numpad4 reserved (mirrors skill 4)");
            T.Eq(BindResult.Reserved, k.Set(BindAction.Chat, 17, out sw), "Ctrl reserved");
            T.Eq(13, k.Get(BindAction.Chat), "reserved attempt leaves binding");

            // 모든 동작이 서로 다른 키를 가진다
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < KeyBindings.ActionCount; ++i) T.Check(seen.Add(k.Get((BindAction)i)), "unique key for " + (BindAction)i);

            // 저장/복원
            k.Set(BindAction.Skill3, 69, out sw);                       // E
            KeyBindings r = new KeyBindings();
            Dictionary<string, object> o = MiniJson.AsObject(MiniJson.Parse(k.ToJson()));
            T.Check(r.FromObject(o), "keys json parses");
            for (int i = 0; i < KeyBindings.ActionCount; ++i) T.Eq(k.Get((BindAction)i), r.Get((BindAction)i), "round trip " + (BindAction)i);

            // 손으로 고쳐 겹치게 한 파일은 통째로 기본값으로
            KeyBindings bad = new KeyBindings();
            Dictionary<string, object> dup = MiniJson.AsObject(MiniJson.Parse("{\"BasicAttack\": 66, \"SelectBoss\": 66}"));
            T.Check(!bad.FromObject(dup), "duplicate keys rejected");
            T.Eq(81, bad.Get(BindAction.BasicAttack), "duplicates reset to defaults");
            Dictionary<string, object> res = MiniJson.AsObject(MiniJson.Parse("{\"Chat\": 27}"));
            T.Check(!bad.FromObject(res), "reserved key in file rejected");
            T.Check(bad.FromObject(MiniJson.AsObject(MiniJson.Parse("{}"))), "missing keys fall back to defaults");

            // 기본값 복원
            k.Reset(BindAction.BasicAttack);
            T.Eq(81, k.Get(BindAction.BasicAttack), "reset restores default key");
            seen.Clear();
            for (int i = 0; i < KeyBindings.ActionCount; ++i) T.Check(seen.Add(k.Get((BindAction)i)), "still unique after reset " + (BindAction)i);
            k.ResetAll(); T.Check(k.IsDefault(BindAction.Skill3) && k.IsDefault(BindAction.SelectBoss), "reset all");

            T.Eq("Q", KeyBindings.KeyName(81), "name Q"); T.Eq("F5", KeyBindings.KeyName(116), "name F5"); T.Eq("Space", KeyBindings.KeyName(32), "name Space"); T.Eq("1", KeyBindings.KeyName(49), "name 1");
            T.Eq("스킬 4", KeyBindings.ActionName(BindAction.Skill4), "action name");

            // 설정 파일에 같이 저장된다
            ViewSettings s = new ViewSettings(); s.Keys.Set(BindAction.AutoBattle, 88, out sw);
            ViewSettings s2 = new ViewSettings();
            T.Check(s2.FromJson(s.ToJson()), "settings json with keys parses");
            T.Eq(88, s2.Keys.Get(BindAction.AutoBattle), "settings carry key bindings");
            ViewSettings old = new ViewSettings();
            T.Check(old.FromJson("{\"VolumePercent\": 40}"), "old settings file without keys loads");
            T.Eq(65, old.Keys.Get(BindAction.AutoBattle), "old file: default keys");
        }

        public static void Tips()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            T.Check(d.Skills.Count > 0, "skills loaded");
            foreach (SkillRow sk in d.Skills.Values)
            {
                List<TipLine> l = SkillTip.Build(d, sk, "3");
                T.Check(l.Count >= 3 && l[0].Kind == TipKind.Title && l[0].Text.Contains(sk.Name) && l[0].Text.Contains("[3]"), "tip title for " + sk.Name);
                bool timing = false; foreach (TipLine t in l) if (t.Text.StartsWith("재사용")) timing = true;
                T.Check(timing, "tip has cooldown for " + sk.Name);
                if (sk.DamagePercent > 0) { bool dmg = false; foreach (TipLine t in l) if (t.Text.StartsWith("피해") && t.Text.Contains(sk.DamagePercent + "%")) dmg = true; T.Check(dmg, "tip damage % for " + sk.Name); }
                if (sk.Heals) { bool heal = false; foreach (TipLine t in l) if (t.Text.StartsWith("치유")) heal = true; T.Check(heal, "tip heal for " + sk.Name); }
                if (sk.StatusId != 0 && d.Status(sk.StatusId) != null) { bool st = false; foreach (TipLine t in l) if (t.Text.StartsWith("효과 ") && t.Text.Contains(d.Status(sk.StatusId).Name)) st = true; T.Check(st, "tip status name for " + sk.Name); }
            }
            SkillRow area = new SkillRow { Id = 9, Name = "폭발", Shape = "Area", IsArea = true, Range = 6, Radius = 3, CooldownMs = 4500, CastTimeMs = 500, DamagePercent = 180, FlatDamage = 20, MaxTargets = 5 };
            List<TipLine> a = SkillTip.Build(d, area, "");
            T.Eq("폭발", a[0].Text, "no key label");
            T.Eq("범위 (지정 지점) · 적", a[1].Text, "shape line");
            T.Eq("사거리 6 · 반경 3 · 최대 5명", a[2].Text, "reach line");
            T.Eq("재사용 4.5초 · 시전 0.5초", a[3].Text, "timing line");
            T.Eq("피해 공격력의 180% + 20", a[4].Text, "damage line");
            SkillRow self = new SkillRow { Id = 10, Name = "방패", Shape = "Self", Radius = 4, CooldownMs = 20000, HealPercent = 10 };
            List<TipLine> sl = SkillTip.Build(d, self, "5");
            T.Eq("자신 중심 · 자신", sl[1].Text, "self shape"); T.Eq("반경 4", sl[2].Text, "self has no range");
            T.Eq("재사용 20초 · 즉시", sl[3].Text, "instant cast"); T.Eq("치유 최대 체력의 10%", sl[4].Text, "heal line");

            StatusRow dot = new StatusRow { Id = 1, Name = "화상", IsDot = true, Kind = "Dot", TickMs = 1000, DamagePercent = 15, DurationMs = 5000 };
            T.Eq("1초마다 공격력의 15% 피해", SkillTip.StatusDetail(dot), "dot detail");
            StatusRow buff = new StatusRow { Id = 2, Name = "강화", Kind = "Buff", AttackPercent = 20, DefensePercent = -10, SpeedPercent = 30 };
            T.Eq("공격 +20%, 방어 -10%, 이동 +30%", SkillTip.StatusDetail(buff), "buff detail");

            List<TipLine> b = SkillTip.BuildBasic(new HeroRow { Id = 1, Name = "전사", Attack = 55, AttackRange = 2.5, AttackIntervalMs = 800 }, "Q");
            T.Check(b[0].Text.Contains("기본 공격") && b[0].Text.Contains("[Q]"), "basic title");
            T.Eq("사거리 2.5 · 간격 0.8초", b[2].Text, "basic range line");
            T.Eq(2, SkillTip.BuildBasic(null, "").Count, "basic without hero");
        }

        public static void Servers()
        {
            ViewerConfig c = new ViewerConfig();
            List<ServerEntry> l = c.ServerEntries();
            T.Eq(1, l.Count, "empty list gives the Host:Port server"); T.Eq("기본 서버", l[0].Name, "default name");
            string tmp = Path.Combine(Path.GetTempPath(), "gv_cfg_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(tmp, "{\"Host\":\"10.0.0.2\",\"Port\":7001,\"Servers\":[{\"Name\":\"A\",\"Host\":\"10.0.0.2\",\"Port\":7001},{\"Name\":\"B\",\"Host\":\"10.0.0.3\",\"Port\":7002}]}");
                ViewerConfig f = new ViewerConfig();
                T.Check(f.LoadJson(tmp) == null, "servers json loads");
                T.Eq(2, f.ServerEntries().Count, "listed servers, current one included");
                f.Port = 9999;
                List<ServerEntry> g = f.ServerEntries();
                T.Eq(3, g.Count, "command-line host:port not in the list is appended"); T.Eq("직접 지정", g[2].Name, "appended name");
                ViewerConfig r = new ViewerConfig();
                File.WriteAllText(tmp, f.ToJson());
                T.Check(r.LoadJson(tmp) == null && r.Servers.Count == 2 && r.Servers[1].Name == "B" && r.Servers[1].Port == 7002, "servers survive ToJson/LoadJson");
                File.WriteAllText(tmp, "{\"Servers\":[{\"Name\":\"X\",\"Host\":\"\",\"Port\":1}]}");
                T.Check(new ViewerConfig().LoadJson(tmp) != null, "server without host rejected");
                File.WriteAllText(tmp, "{\"Servers\":[{\"Name\":\"X\",\"Host\":\"h\",\"Port\":70000}]}");
                T.Check(new ViewerConfig().LoadJson(tmp) != null, "server with bad port rejected");
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static void Probe()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0); l.Start();
            int okPort = ((IPEndPoint)l.LocalEndpoint).Port;
            Thread acc = new Thread(delegate () { try { while (true) l.AcceptTcpClient().Close(); } catch { } }); acc.IsBackground = true; acc.Start();
            TcpListener dead = new TcpListener(IPAddress.Loopback, 0); dead.Start();
            int deadPort = ((IPEndPoint)dead.LocalEndpoint).Port; dead.Stop();
            try
            {
                List<ServerEntry> servers = new List<ServerEntry>();
                servers.Add(new ServerEntry("up", "127.0.0.1", okPort));
                servers.Add(new ServerEntry("down", "127.0.0.1", deadPort));
                ServerProbe p = new ServerProbe(servers);
                T.Eq(ProbeState.Unknown, p.Get(0).State, "before first tick: unknown");
                p.Tick(0);
                for (int i = 0; i < 100 && (p.Get(0).State == ProbeState.Probing || p.Get(1).State == ProbeState.Probing); ++i) Thread.Sleep(50);
                ProbeResult up = p.Get(0), down = p.Get(1);
                T.Eq(ProbeState.Online, up.State, "listening server online"); T.Check(up.Ms >= 0 && up.Ms < ServerProbe.TimeoutMs, "latency measured");
                T.Eq(ProbeState.Offline, down.State, "closed port offline"); T.Check(!string.IsNullOrEmpty(down.Error), "offline reason kept");
                p.Tick(1000);                                                 // 갱신 주기 전: 다시 재지 않는다
                T.Check(p.Get(0).State == ProbeState.Online, "no re-probe before refresh interval");
                T.Eq("쾌적", ServerProbe.Grade(20), "grade good"); T.Eq("보통", ServerProbe.Grade(100), "grade ok"); T.Eq("느림", ServerProbe.Grade(400), "grade slow");
            }
            finally { l.Stop(); }
        }

        public static void FeedbackScale()
        {
            WorldModel w = new WorldModel(new GameData(), 1UL);
            w.HitStop(1000, 100);
            T.Eq(100L, w.HitStopMs, "full hit stop at 100%");
            w.HitStopStartMs = 0; w.HitStopMs = 0;
            w.HitStopScale = 0.5f; w.HitStop(5000, 100);
            T.Eq(50L, w.HitStopMs, "half hit stop at 50%");
            w.HitStopStartMs = 0; w.HitStopMs = 0;
            w.HitStopScale = 0f; w.HitStop(9000, 100);
            T.Eq(0L, w.HitStopMs, "no hit stop at 0%");
            T.Eq(9000L, w.RenderTime(9000), "render time unaffected when scale is 0");

            ViewSettings s = new ViewSettings();
            T.Near(1.0, s.FeedbackScale, 1e-6, "feedback default 100%");
            s.FeedbackPercent = 150; T.Near(1.5, s.ShakeScale, 1e-6, "shake follows feedback");
            s.ReduceShake = true; T.Near(0.525, s.ShakeScale, 1e-6, "reduce shake multiplies");
            s.StepFeedback(500); T.Eq(150, s.FeedbackPercent, "feedback clamps at 150"); s.StepFeedback(-900); T.Eq(0, s.FeedbackPercent, "feedback clamps at 0");
            s.BgmVolumePercent = 40; s.BgmOn = true; T.Near(0.4, s.BgmGain, 1e-6, "bgm gain");
            s.BgmOn = false; T.Near(0.0, s.BgmGain, 1e-6, "bgm off gain 0");
            s.StepBgm(10); T.Check(s.BgmOn && s.BgmVolumePercent == 50, "raising bgm volume turns it on");
            s.LastServer = "서버 \"1\""; s.FeedbackPercent = 75;
            ViewSettings r = new ViewSettings();
            T.Check(r.FromJson(s.ToJson()), "round trip parses");
            T.Eq("서버 \"1\"", r.LastServer, "server name with quotes survives"); T.Eq(75, r.FeedbackPercent, "feedback kept"); T.Eq(50, r.BgmVolumePercent, "bgm volume kept"); T.Check(r.BgmOn, "bgm on kept");
        }
    }
}
