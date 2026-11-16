using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GameViewer.Core;
using GameViewer.Core.Audio;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using Pk = GameViewer.Core.Protocol.ServerPackets;
using GameViewer.Core.World;

namespace GameViewer.Tests
{
    /// <summary>로그인/입장 요청에 자동으로 답하는 가짜 Gateway. 연결을 여러 번 받는다(재접속 테스트용).</summary>
    public sealed class LoopGateway : IDisposable
    {
        readonly TcpListener _l;
        readonly Thread _t;
        readonly object _lock = new object();
        TcpClient _cur;
        public int Connections;
        public int Logins;
        public int Port { get { return ((IPEndPoint)_l.LocalEndpoint).Port; } }

        public LoopGateway()
        {
            _l = new TcpListener(IPAddress.Loopback, 0); _l.Start();
            _t = new Thread(Loop); _t.IsBackground = true; _t.Start();
        }

        void Loop()
        {
            try
            {
                while (true)
                {
                    TcpClient c = _l.AcceptTcpClient();
                    c.NoDelay = true;
                    lock (_lock) { _cur = c; ++Connections; }
                    Thread h = new Thread(delegate () { Serve(c); }); h.IsBackground = true; h.Start();
                }
            }
            catch { }
        }

        void Serve(TcpClient c)
        {
            try
            {
                NetworkStream st = c.GetStream();
                byte[] buf = new byte[4096];
                PacketFramer fr = new PacketFramer(4096);
                List<Frame> tmp = new List<Frame>();
                while (true)
                {
                    int n = st.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    tmp.Clear(); fr.Push(buf, 0, n, tmp);
                    foreach (Frame f in tmp)
                    {
                        if (f.Type == (ushort)GatewayPacketType.GwLoginReq) { lock (_lock) ++Logins; Write(st, Pk.LoginRes(true, GatewayResult.Ok, 55)); }
                        else if (f.Type == (ushort)GatewayPacketType.GwEnterZoneReq)
                        {
                            Write(st, Pk.EnterRes(true, GatewayResult.Ok, 1, 20, 20));
                            Write(st, Pk.Spawn(777, EntityType.Player, 20, 20, 20, 20, 6, false, 150, 150, false, 1));
                        }
                    }
                }
            }
            catch { }
        }

        static void Write(NetworkStream st, byte[] p) { st.Write(p, 0, p.Length); }

        public void DropClient() { lock (_lock) { try { if (_cur != null) _cur.Close(); } catch { } } }
        public void Dispose() { try { _l.Stop(); } catch { } DropClient(); }
    }

    public static class LiveTests
    {
        const ulong Self = 100;
        const ulong M1 = 0x8000000000000001UL;

        // ------------------------------------------------------------------ 설정

        public static void Settings()
        {
            ViewSettings s = new ViewSettings();
            T.Eq(GfxQuality.High, s.Quality, "default quality high");
            T.Check(s.DrawAir && s.DrawVignette, "high draws everything");
            s.Quality = GfxQuality.Medium; T.Check(!s.DrawAir && s.DrawVignette, "medium skips air only");
            s.Quality = GfxQuality.Low; T.Check(!s.DrawAir && !s.DrawVignette, "low skips air and vignette");

            s.CycleQuality(); T.Eq(GfxQuality.High, s.Quality, "cycle low -> high");
            s.CycleQuality(); T.Eq(GfxQuality.Medium, s.Quality, "cycle high -> medium");

            s.VolumePercent = 50; s.SfxOn = true; T.Near(0.5, s.Gain, 1e-6, "gain follows volume");
            s.SfxOn = false; T.Near(0.0, s.Gain, 1e-6, "muted gain 0");
            s.StepVolume(10); T.Check(s.SfxOn && s.VolumePercent == 60, "raising volume unmutes");
            s.StepVolume(-500); T.Eq(0, s.VolumePercent, "volume clamps at 0");
            s.StepVolume(900); T.Eq(100, s.VolumePercent, "volume clamps at 100");

            s.Quality = GfxQuality.Low; s.VolumePercent = 35; s.ShowFps = false; s.ReduceShake = true; s.ShowScreenFx = false;
            ViewSettings r = new ViewSettings();
            T.Check(r.FromJson(s.ToJson()), "json round trip parses");
            T.Eq(GfxQuality.Low, r.Quality, "quality kept"); T.Eq(35, r.VolumePercent, "volume kept");
            T.Check(!r.ShowFps && r.ReduceShake && !r.ShowScreenFx, "flags kept");
            T.Near(0.35, r.ShakeScale, 1e-6, "reduced shake scale");

            ViewSettings bad = new ViewSettings();
            T.Check(!bad.FromJson("{ not json"), "broken json rejected");
            T.Eq(100, bad.VolumePercent, "broken json keeps defaults");
            ViewSettings odd = new ViewSettings();
            odd.FromJson("{\"VolumePercent\": 900, \"Quality\": 7}");
            T.Eq(100, odd.VolumePercent, "out of range volume clamped"); T.Eq(GfxQuality.High, odd.Quality, "out of range quality reset");

            string path = Path.Combine(Path.GetTempPath(), "gv_settings_test_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                T.Check(s.Save(path), "saved");
                T.Eq(35, ViewSettings.Load(path).VolumePercent, "loaded from file");
            }
            finally { try { File.Delete(path); } catch { } }
            T.Eq(100, ViewSettings.Load(Path.Combine(Path.GetTempPath(), "gv_no_such_file.json")).VolumePercent, "missing file gives defaults");
            T.Check(!s.Save(Path.Combine(Path.GetTempPath(), "gv_no_such_dir_xyz", "a.json")), "unwritable path reports failure instead of throwing");
        }

        // ------------------------------------------------------------------ 재접속 정책

        public static void ReconnectPolicyTest()
        {
            ReconnectPolicy p = new ReconnectPolicy();
            T.Check(!p.Waiting && !p.TryTake(0), "idle");
            T.Check(p.OnFailure(1000), "first failure schedules");
            T.Eq(1, p.Attempt, "attempt 1"); T.Eq(2000L, 1000L + p.RemainingMs(1000), "due after 1s");
            T.Check(!p.TryTake(1500), "not yet due"); T.Check(p.TryTake(2000), "due");
            T.Check(!p.Waiting, "waiting cleared once taken");
            long t = 2000;
            for (int i = 2; i <= p.MaxAttempts; ++i) { T.Check(p.OnFailure(t), "failure " + i + " schedules"); T.Eq(ReconnectPolicy.DelaysMs[i - 1], (int)p.RemainingMs(t), "backoff step " + i); t += 100000; p.TryTake(t); }
            T.Check(!p.OnFailure(t), "gives up after the last attempt"); T.Check(p.Exhausted, "exhausted");
            p.Manual(); T.Eq(0, p.Attempt, "manual retry restarts the count"); T.Check(p.OnFailure(0), "schedules again");
            p.Reset(); T.Check(!p.Waiting && p.Attempt == 0, "reset clears");
        }

        public static void ReconnectSession()
        {
            using (LoopGateway gw = new LoopGateway())
            {
                ViewerConfig c = new ViewerConfig();
                c.Port = gw.Port; c.CharId = 777; c.ZoneId = 1; c.HeroId = 1; c.DataDir = Program.Dir("GameData"); c.ConnectTimeoutMs = 1500;
                ViewerSession s = new ViewerSession(c, GameData.Load(c.DataDir, 1));
                s.AutoReconnect = true;
                Stopwatch sw = Stopwatch.StartNew();
                Func<long> now = delegate { return sw.ElapsedMilliseconds + 1000; };
                s.Start(now());
                T.Check(Spin(s, now, delegate { return s.State == SessionState.InZone; }, 3000), "first entry");
                T.Check(s.InZoneSinceMs >= 0, "entry time recorded");
                T.Eq(0, s.ReconnectCount, "no reconnect yet");
                GameViewer.Core.Net.GatewayClient first = s.Net;

                gw.DropClient();
                T.Check(Spin(s, now, delegate { return s.State == SessionState.Disconnected; }, 3000), "disconnect noticed");
                T.Check(s.Reconnect.Waiting && s.Reconnect.Attempt == 1, "retry scheduled");
                T.Check(s.Reconnecting, "world kept while reconnecting");
                T.Check(s.World.Self != null, "last world still there");
                T.Check(Spin(s, now, delegate { return s.State == SessionState.InZone && s.ReconnectCount == 1; }, 4000), "reconnected and re-entered");
                T.Check(!object.ReferenceEquals(first, s.Net), "a fresh client object is used");
                T.Eq(0, s.Reconnect.Attempt, "attempt counter reset on success");
                T.Check(gw.Logins >= 2 && gw.Connections >= 2, "server saw a second login");
                bool logged = false; foreach (LogLine l in s.World.Log) if (l.Text.Contains("재접속 성공")) logged = true;
                T.Check(logged, "success is logged");
                s.Dispose();
            }

            // 자동 재접속을 끈 세션은 예전처럼 Failed/Disconnected 에서 멈춘다
            using (LoopGateway gw = new LoopGateway())
            {
                ViewerConfig c = new ViewerConfig();
                c.Port = gw.Port; c.CharId = 777; c.ZoneId = 1; c.HeroId = 1; c.DataDir = Program.Dir("GameData");
                ViewerSession s = new ViewerSession(c, GameData.Load(c.DataDir, 1));
                Stopwatch sw = Stopwatch.StartNew(); Func<long> now = delegate { return sw.ElapsedMilliseconds + 1000; };
                s.Start(now());
                Spin(s, now, delegate { return s.State == SessionState.InZone; }, 3000);
                gw.DropClient();
                Spin(s, now, delegate { return s.State == SessionState.Disconnected; }, 3000);
                T.Check(!s.Reconnect.Waiting, "no retry when auto reconnect is off");
                s.Dispose();
            }

            // 접속 자체가 계속 실패하면 정해진 횟수에서 멈춘다 (연결 거부 포트)
            {
                TcpListener l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int dead = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
                ViewerConfig c = new ViewerConfig(); c.Port = dead; c.CharId = 777; c.DataDir = Program.Dir("GameData"); c.ConnectTimeoutMs = 300;
                ViewerSession s = new ViewerSession(c, GameData.Load(c.DataDir, 1)); s.AutoReconnect = true;
                Stopwatch sw = Stopwatch.StartNew(); Func<long> now = delegate { return sw.ElapsedMilliseconds + 1000; };
                s.Start(now());
                T.Check(Spin(s, now, delegate { return s.State == SessionState.Failed && s.Reconnect.Waiting; }, 3000), "connect failure schedules a retry");
                s.Dispose();
            }
        }

        static bool Spin(ViewerSession s, Func<long> now, Func<bool> cond, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                s.Update(now());
                if (cond()) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        // ------------------------------------------------------------------ 콤보 / 히트 스톱

        public static void Combo()
        {
            ComboCounter c = new ComboCounter();
            T.Eq(0, c.Active(0), "no combo");
            c.Hit(1000, 10); c.Hit(1500, 20); int n = c.Hit(2000, 30);
            T.Eq(3, n, "three hits"); T.Eq(60L, c.TotalDamage(2100), "damage summed");
            T.Check(c.Remaining(2000) > 0.99f, "full right after a hit"); T.Check(c.Remaining(2000 + ComboCounter.WindowMs / 2) < 0.6f, "drains");
            T.Eq(0, c.Active(2000 + ComboCounter.WindowMs + 1), "expires");
            T.Eq(1, c.Hit(9000, 5), "restarts after expiry"); T.Eq(5L, c.TotalDamage(9001), "damage restarts too");
            T.Eq(3, c.Best, "best kept");
            T.Check(c.Pop(9000) > 0.9f && c.Pop(9300) == 0f, "pop animation window");
            T.Eq(0, ComboCounter.Tier(9), "tier 0"); T.Eq(1, ComboCounter.Tier(10), "tier 1"); T.Eq(2, ComboCounter.Tier(25), "tier 2"); T.Eq(3, ComboCounter.Tier(50), "tier 3");
            c.Reset(); T.Eq(0, c.Active(9001), "reset");
        }

        public static void WorldFeedback()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            WorldModel w = new WorldModel(d, Self); w.InitSelf(10, 10, 1, 0);
            w.Apply(Pk.F(Pk.Spawn(M1, EntityType.Monster, 12, 10, 12, 10, 3, false, 1000, 1000, false, 1)), 0);

            w.Apply(Pk.F(Pk.Damage(Self, M1, 20, 980, 1000)), 1000);          // 2% : 작은 타격
            T.Eq(1, w.Combo.Active(1000), "combo counts my hit");
            T.Eq(1000L, w.RenderTime(1000), "small hit does not freeze");
            w.Apply(Pk.F(Pk.Damage(Self, M1, 300, 680, 1000)), 1300);         // 30% : 큰 타격
            T.Eq(2, w.Combo.Active(1300), "second hit continues the combo");
            T.Eq(1300L, w.RenderTime(1310), "big hit freezes the render clock at the hit");
            T.Eq(1400L, w.RenderTime(1400), "freeze is short");
            T.Check(w.HitStopMs > 0, "hit stop length recorded");

            // 남이 맞은 것/내가 맞은 것은 내 콤보를 올리지 않는다
            int before = w.Combo.Active(1500);
            w.Apply(Pk.F(Pk.Damage(M1, Self, 5, 145, 150)), 1500);
            T.Eq(before, w.Combo.Active(1500), "being hit does not add to my combo");
            w.Apply(Pk.F(Pk.Damage(0, M1, 5, 675, 1000)), 1600);
            T.Eq(before, w.Combo.Active(1600), "damage over time does not add to my combo");

            // 상세 전투 줄은 전투 탭에만 들어가고 화면 아래 떠오르는 줄에는 나오지 않는다
            int combat = 0, feedCombat = 0;
            foreach (LogLine l in w.Log) if (l.Cat == LogCat.Combat) { ++combat; if (l.Feed) ++feedCombat; }
            T.Check(combat >= 3, "combat lines recorded: " + combat);
            T.Eq(0, feedCombat, "per-hit lines are not shown in the floating feed");

            // 내가 죽으면 콤보가 끊기고, 죽음은 전투 탭 + 떠오르는 줄로 보인다
            w.Apply(Pk.F(Pk.Death(Self, M1)), 1700);
            T.Eq(0, w.Combo.Active(1700), "death resets the combo");
            bool deathFeed = false; foreach (LogLine l in w.Log) if (l.Text == "사망" && l.Cat == LogCat.Combat && l.Feed) deathFeed = true;
            T.Check(deathFeed, "death line is a combat line shown in the feed");
        }

        // ------------------------------------------------------------------ 입장 연출

        public static void Intro()
        {
            T.Near(0.0, ZoneIntro.BlackAlpha(-1, 100), 1e-6, "no intro = no black");
            T.Near(1.0, ZoneIntro.BlackAlpha(1000, 1100), 1e-6, "black right after entry");
            float a1 = ZoneIntro.BlackAlpha(1000, 1000 + ZoneIntro.HoldBlackMs + ZoneIntro.FadeMs / 2);
            T.Check(a1 > 0.3f && a1 < 0.7f, "half faded in the middle: " + a1);
            T.Near(0.0, ZoneIntro.BlackAlpha(1000, 1000 + ZoneIntro.HoldBlackMs + ZoneIntro.FadeMs), 1e-6, "clear after fade");
            float prev = 1f; bool mono = true;
            for (long t = 1000; t < 3000; t += 20) { float a = ZoneIntro.BlackAlpha(1000, t); if (a > prev + 1e-6f) mono = false; prev = a; }
            T.Check(mono, "fade never gets darker again");

            T.Near(0.0, ZoneIntro.BannerAlpha(1000, 1000), 1e-6, "banner waits");
            T.Near(1.0, ZoneIntro.BannerAlpha(1000, 1000 + ZoneIntro.BannerDelayMs + ZoneIntro.BannerInMs + 100), 1e-6, "banner fully visible");
            T.Near(0.0, ZoneIntro.BannerAlpha(1000, 1000 + ZoneIntro.TotalMs), 1e-6, "banner gone at the end");
            T.Check(ZoneIntro.Active(1000, 1000 + ZoneIntro.TotalMs - 1) && !ZoneIntro.Active(1000, 1000 + ZoneIntro.TotalMs), "active window");
            T.Check(ZoneIntro.BannerSlide(1000, 1000 + ZoneIntro.BannerDelayMs + 100) < 1f, "slides in");
            foreach (uint z in ZoneTheme.Zones) T.Check(ZoneIntro.Tagline(ZoneTheme.For(z)).Length > 0, "tagline for zone " + z);

            HashSet<int> lens = new HashSet<int>();
            foreach (uint z in ZoneTheme.Zones)
            {
                SfxKind k = SfxSynth.ZoneSfx(ZoneTheme.For(z));
                float[] s = SfxSynth.Make(k, SfxSynth.DefaultRate);
                T.Check(s.Length > 1000, "zone sting not empty for " + z);
                float peak = 0; foreach (float f in s) peak = Math.Max(peak, Math.Abs(f));
                T.Check(peak > 0.05f && peak <= 1f, "zone sting peak sane for " + z + ": " + peak);
                lens.Add(s.Length);
            }
            T.Check(lens.Count >= 3, "zone stings differ from each other");
            T.Check(SfxSynth.Make(SfxKind.UiTick, SfxSynth.DefaultRate).Length > 100, "ui tick");
            T.Check(SfxSynth.Make(SfxKind.Reconnect, SfxSynth.DefaultRate).Length > 100, "reconnect chime");
        }

        // ------------------------------------------------------------------ 월드맵 좌표

        public static void MapProj()
        {
            MapProjection m = new MapProjection();
            m.PanelX = 100; m.PanelY = 50; m.PanelW = 800; m.PanelH = 400;
            m.WorldMinX = 0; m.WorldMinY = 0; m.WorldMaxX = 1000; m.WorldMaxY = 1000;
            m.Zoom = 1; m.CenterX = 500; m.CenterY = 500;
            T.Near(0.4, m.FitScale, 1e-6, "fit uses the smaller side");
            T.Near(500, m.ToScreenX(500), 1e-3, "center maps to panel center x"); T.Near(250, m.ToScreenY(500), 1e-3, "center y");
            T.Near(300, m.ToScreenX(0), 1e-3, "left edge of the world");
            for (int i = 0; i < 5; ++i)
            {
                float wx = 100 + i * 170, wy = 900 - i * 190;
                T.Near(wx, m.ToWorldX(m.ToScreenX(wx)), 1e-3, "round trip x"); T.Near(wy, m.ToWorldY(m.ToScreenY(wy)), 1e-3, "round trip y");
            }
            T.Check(m.Contains(500, 250) && !m.Contains(50, 250), "contains");

            float wx0 = m.ToWorldX(520), wy0 = m.ToWorldY(200);
            m.ZoomAt(520, 200, 4f);
            T.Near(4.0, m.Zoom, 1e-6, "zoomed");
            T.Near(wx0, m.ToWorldX(520), 0.5, "point under the cursor stays put x"); T.Near(wy0, m.ToWorldY(200), 0.5, "y");
            m.ZoomAt(420, 180, 0.01f); T.Near(1.0, m.Zoom, 1e-6, "cannot zoom out past fit");
            m.ZoomAt(420, 180, 1000f); T.Near(8.0, m.Zoom, 1e-6, "zoom capped");
            m.CenterX = -5000; m.CenterY = 99999; m.Clamp();
            T.Check(m.ToWorldX(m.PanelX) >= -0.5f && m.ToWorldY(m.PanelY + m.PanelH) <= 1000.5f, "pan stays inside the world");
            m.Zoom = 1; m.CenterX = 10; m.Clamp();
            T.Near(500, m.CenterX, 1e-3, "at fit zoom the world is centered on the long axis");
        }

        // ------------------------------------------------------------------ 로그 탭 / 상태 효과

        public static void LogTabs()
        {
            List<LogLine> log = new List<LogLine>();
            Func<string, LogCat, LogLine> mk = delegate (string t, LogCat c) { LogLine l = new LogLine(); l.Text = t; l.Cat = c; return l; };
            log.Add(mk("s1", LogCat.System)); log.Add(mk("c1", LogCat.Combat)); log.Add(mk("h1", LogCat.Chat));
            log.Add(mk("c2", LogCat.Combat)); log.Add(mk("s2", LogCat.System));
            List<LogLine> o = new List<LogLine>();
            LogFilter.Select(log, LogTab.All, 10, 0, o); T.Eq(5, o.Count, "all"); T.Eq("s1", o[0].Text, "oldest first"); T.Eq("s2", o[4].Text, "newest last");
            LogFilter.Select(log, LogTab.Combat, 10, 0, o); T.Eq(2, o.Count, "combat tab"); T.Eq("c1", o[0].Text, "combat order");
            LogFilter.Select(log, LogTab.Chat, 10, 0, o); T.Eq(1, o.Count, "chat tab");
            LogFilter.Select(log, LogTab.System, 10, 0, o); T.Eq(2, o.Count, "system tab");
            LogFilter.Select(log, LogTab.All, 2, 0, o); T.Eq("c2", o[0].Text, "max keeps the newest");
            LogFilter.Select(log, LogTab.All, 2, 2, o); T.Eq("c1", o[0].Text, "scrolled back"); T.Eq("h1", o[1].Text, "scrolled window");
            LogFilter.Select(log, LogTab.Combat, 5, 5, o); T.Eq(0, o.Count, "scrolled past the start");
            T.Eq(2, LogFilter.Count(log, LogTab.Combat), "count");
        }

        public static void StatusLooks()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            int bad = 0, good = 0;
            foreach (KeyValuePair<uint, StatusRow> kv in d.Statuses) { if (StatusLook.IsDebuff(kv.Value)) ++bad; else ++good; }
            T.Check(bad > 0 && good > 0, "data has both kinds: " + bad + "/" + good);
            StatusRow burn = d.Status(1);
            T.Check(burn != null && StatusLook.IsDebuff(burn), "burn is a debuff");
            StatusRow cry = d.Status(3);
            T.Check(cry != null && !StatusLook.IsDebuff(cry), "battle cry is a buff");
            StatusRow slow = d.Status(4);
            T.Check(slow != null && StatusLook.IsDebuff(slow), "slow (speed down) is a debuff even though its Kind is Buff");
            T.Check(!StatusLook.IsDebuff(null), "unknown status is not flagged");
            T.Eq("화", StatusLook.Glyph(burn, 1), "glyph"); T.Eq("?", StatusLook.Glyph(null, 1), "glyph fallback");
            T.Eq(StatusLook.Hue(7), StatusLook.Hue(7), "hue stable"); T.Check(StatusLook.Hue(3) >= 0 && StatusLook.Hue(3) < 6, "hue range");
            T.Eq("12", StatusLook.Remaining(12500), "remaining >= 10s"); T.Eq("3.5", StatusLook.Remaining(3500), "remaining < 10s"); T.Eq("0.0", StatusLook.Remaining(-5), "remaining never negative");
        }
    }
}
