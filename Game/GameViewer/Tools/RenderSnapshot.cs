//***************************************************************************
// RenderSnapshot : 서버/창 없이 데모 전투를 시뮬레이션하며 장면을 PNG 로 저장한다. (렌더러 회귀 확인용)
//   RenderSnapshot.exe <GameData폴더> <출력폴더> [초...]     예) RenderSnapshot.exe GameData out 4 9 15
//   RenderSnapshot.exe --sheet <출력폴더> [배율]               영웅/몬스터 그림 모음(걷기/공격 프레임 포함) 한 장
//   RenderSnapshot.exe --selftest <GameData폴더>               렌더러 점검: 모든 그림이 그려지는지, 확대 범위 전체/보스전 장면이 예외 없이 그려지는지
//   RenderSnapshot.exe --bench <GameData폴더> [프레임수]       데모 장면을 그리는 평균 시간(ms) 측정
// 환경변수: SNAP_BOSS=1 영웅이 보스를 쫓음, SNAP_CAM=x,y 카메라 고정, SNAP_HEROSEL=1 영웅 선택 창, SNAP_ASSETS=폴더 이미지 에셋 폴더
// 시간은 가상 시계(16ms 단위)로 진행하므로 실제로 기다리지 않는다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using GameViewer.App.Render;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;

public static class RenderSnapshot
{
    /// <summary>그림 모음: 각 종류를 걷기 4프레임 + 대기 + 공격(영웅)으로 늘어놓는다.</summary>
    private static int Sheet(string outDir, double scale)
    {
        Directory.CreateDirectory(outDir);
        using (SpriteCache cache = new SpriteCache(2000))
        {
            CreatureRenderer cr = new CreatureRenderer(cache);
            int cols = 8;
            int cellW = (int)(scale * 7.2), cellH = (int)(scale * 5.6);
            uint[] heroes = { 1, 2, 3, 4 };
            uint[] mons = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            int rows = heroes.Length + mons.Length;
            ViewState v = new ViewState(); v.Scale = scale;
            using (Bitmap bmp = new Bitmap(cellW * cols, cellH * rows, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(74, 118, 56));
                int row = 0;
                for (int pass = 0; pass < 2; ++pass)
                {
                    uint[] list = pass == 0 ? heroes : mons;
                    foreach (uint tpl in list)
                    {
                        for (int c = 0; c < cols; ++c)
                        {
                            Entity e = new Entity();
                            e.Type = (byte)(pass == 0 ? EntityType.Player : EntityType.Monster); e.TemplateId = tpl; e.Id = (ulong)(c + 1);
                            long now = 100000;
                            // c: 0..3 걷기, 4 대기, 5..7 공격(영웅) / 걷기(몬스터) 
                            if (c <= 3 || (pass == 1 && c >= 5))
                            {
                                e.X = 0; e.Y = 0; e.DestX = 100; e.DestY = 0; e.Speed = 4f; e.Moving = true; e.MoveStartMs = now - 1;
                                float stride = pass == 0 ? 0.4f : 0.34f;
                                int fr = (c <= 3 ? c * 2 : (c - 4) * 2 + 1);
                                e.MoveStartMs = now - (long)((fr * stride + 0.01f) / 4f * 1000f);
                            }
                            else if (pass == 0 && c >= 5) { e.AttackAnimMs = now - (c - 5) * 90 - 20; e.X = 0; e.Y = 0; }
                            e.FaceX = (c % 2 == 0 || c >= 4) ? 1 : -1;
                            if (c == 4) e.FaceX = 1;
                            float sx = c * cellW + cellW / 2f, sy = row * cellH + cellH - (float)scale * 0.6f;
                            g.FillEllipse(new SolidBrush(Color.FromArgb(70, 0, 0, 0)), sx - (float)scale * 1.2f, sy - (float)scale * 0.25f, (float)scale * 2.4f, (float)scale * 0.55f);
                            cr.Draw(g, e, v, now, sx, sy, false);
                        }
                        ++row;
                    }
                }
                string path = Path.Combine(outDir, "sheet.png");
                bmp.Save(path, ImageFormat.Png);
                Console.WriteLine("saved " + path + " cache=" + cache.Count);
            }
        }
        return 0;
    }

    /// <summary>공격 동작 모음: 종류별로 시전(모으기) -> 타격 -> 복귀 -> 피격 반동을 시간 순서로 늘어놓는다.</summary>
    private static int AtkSheet(string outPath, double scale)
    {
        using (SpriteCache cache = new SpriteCache(2000))
        {
            CreatureRenderer cr = new CreatureRenderer(cache);
            ViewState v = new ViewState(); v.Scale = scale;
            // (영웅/몬스터, 종류)
            object[][] rows = {
                new object[] { false, 1u, "hero" }, new object[] { true, 2u, "wolf" }, new object[] { true, 1u, "slime" },
                new object[] { true, 8u, "heavy" }, new object[] { true, 9u, "boss" }, new object[] { true, 11u, "skelwing" } };
            string[] cols = { "idle", "cast 45%", "cast 95%", "strike 0", "strike 70", "hold 110", "recover 200", "recover 300", "hit 20", "hit 120" };
            int cellW = (int)(scale * 8), cellH = (int)(scale * 7);
            using (Bitmap bmp = new Bitmap(cellW * cols.Length, cellH * rows.Length, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font f = new Font("Arial", 9f))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(74, 118, 56));
                for (int r = 0; r < rows.Length; ++r)
                    for (int c = 0; c < cols.Length; ++c)
                    {
                        long now = 100000;
                        Entity e = new Entity();
                        e.Type = (byte)((bool)rows[r][0] ? EntityType.Monster : EntityType.Player); e.TemplateId = (uint)rows[r][1]; e.Id = (ulong)(r * 20 + c + 1);
                        e.FaceX = 1; e.Hp = e.MaxHp = 100;
                        if ((string)rows[r][2] == "boss") e.Boss = new BossRow();
                        if (c == 1 || c == 2) { e.CastSkillId = 1; e.CastTimeMs = 800; e.CastStartMs = now - (c == 1 ? 360 : 760); }
                        if (c >= 3 && c <= 7) { e.AttackAnimMs = now - new long[] { 0, 70, 110, 200, 300 }[c - 3]; e.AttackKind = (byte)(c >= 3 && (string)rows[r][2] == "wolf" ? 1 : 0); }
                        if (c >= 8) { e.HitFlashMs = now - (c == 8 ? 20 : 120); e.HitDirX = 1; }
                        BodyPose pose = AttackPose.Compute(e, now);
                        float sx = c * cellW + cellW / 2f, sy = r * cellH + cellH - (float)scale * 1.0f;
                        g.FillEllipse(new SolidBrush(Color.FromArgb(70, 0, 0, 0)), sx - (float)scale * 1.2f, sy - (float)scale * 0.25f, (float)scale * 2.4f, (float)scale * 0.55f);
                        float bx = sx + (float)scale * pose.OffX, by = sy + (float)scale * pose.OffY * 0.6f - (float)scale * pose.Lift;
                        System.Drawing.Drawing2D.GraphicsState gs = g.Save();
                        g.TranslateTransform(bx, by);
                        if (Math.Abs(pose.Lean) > 0.05f) g.RotateTransform(pose.Lean);
                        g.ScaleTransform(pose.ScaleX, pose.ScaleY);
                        cr.Draw(g, e, v, now, 0f, 0f, c == 8);
                        g.Restore(gs);
                        g.DrawString(cols[c], f, Brushes.White, c * cellW + 4, r * cellH + 2);
                    }
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("saved " + outPath);
            }
        }
        return 0;
    }

    /// <summary>영웅 4종의 공격 프레임(무기 동작)을 크게 늘어놓는다: 대기 + 공격 5프레임.</summary>
    private static int HeroSheet(string outPath, double scale)
    {
        using (SpriteCache cache = new SpriteCache(500))
        {
            CreatureRenderer cr = new CreatureRenderer(cache);
            ViewState v = new ViewState(); v.Scale = scale;
            int cols = 6, cellW = (int)(scale * 5.2), cellH = (int)(scale * 4.6);
            using (Bitmap bmp = new Bitmap(cellW * cols, cellH * 4, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(74, 118, 56));
                for (uint h = 1; h <= 4; ++h)
                    for (int c = 0; c < cols; ++c)
                    {
                        long now = 100000;
                        Entity e = new Entity(); e.Type = (byte)EntityType.Player; e.TemplateId = h; e.Id = h * 10 + (ulong)c; e.FaceX = 1;
                        if (c > 0) e.AttackAnimMs = now - (c - 1) * 56 - 5;
                        float sx = c * cellW + cellW / 2f, sy = (h - 1) * cellH + cellH - (float)scale * 0.7f;
                        cr.Draw(g, e, v, now, sx, sy, false);
                    }
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("saved " + outPath);
            }
        }
        return 0;
    }

    /// <summary>실제 장면에서 영웅 4종이 공격하는 순간(몸동작 + 무기 궤적/섬광)을 시간별로 늘어놓는다.</summary>
    private static int HeroScene(string dataDir, string outPath)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1;
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now);
        for (int i = 0; i < 60; ++i) { now += 16; s.Update(now); demo.Update(now); }
        ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = 46;
        float cx, cy; s.SelfPos(now, out cx, out cy); v.CamX = cx; v.CamY = cy;
        System.Collections.Generic.List<Entity> heroes = new System.Collections.Generic.List<Entity>();
        for (uint h = 1; h <= 4; ++h)
        {
            Entity e = new Entity(); e.Id = 900 + h; e.Type = (byte)EntityType.Player; e.TemplateId = h; e.Hp = e.MaxHp = 100;
            e.X = e.DestX = cx + (h - 2.5f) * 6.2f; e.Y = e.DestY = cy; e.FaceX = 1; e.MoveStartMs = now;
            s.World.Entities[e.Id] = e; heroes.Add(e);
        }
        long[] times = { 30, 80, 130, 190 };
        int cropW = 1280, cropH = 220;
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        using (Bitmap outb = new Bitmap(cropW, cropH * times.Length, PixelFormat.Format32bppArgb))
        using (Graphics og = Graphics.FromImage(outb))
        {
            for (int ti = 0; ti < times.Length; ++ti)
            {
                foreach (Entity e in heroes) { e.AttackAnimMs = now - times[ti]; e.AttackKind = (byte)(e.TemplateId == 2 || e.TemplateId == 3 ? 2 : 0); }
                r.Draw(g, s, pilot, v, now);
                og.DrawImage(bmp, new Rectangle(0, ti * cropH, cropW, cropH), new Rectangle(0, v.Height / 2 - 150, cropW, cropH), GraphicsUnit.Pixel);
            }
            outb.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved " + outPath);
        }
        return 0;
    }

    /// <summary>긴박감 연출 장면: 저체력 맥동 + 화면 밖 위협 화살표 + 연속 처치 알림 + 보스 경고 배너.</summary>
    private static int TensionScene(string dataDir, string outPath)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1;
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now);
        for (int i = 0; i < 90; ++i) { now += 16; s.Update(now); demo.Update(now); }
        WorldModel w = s.World;
        float cx, cy; s.SelfPos(now, out cx, out cy);
        w.Self.Hp = Math.Max(1, w.Self.MaxHp / 10);
        // 화면 밖에서 달려오는 몬스터 몇 마리
        float[][] at = { new float[] { 30, 0 }, new float[] { -26, -6 }, new float[] { 4, -20 }, new float[] { 12, 17 } };
        for (int i = 0; i < at.Length; ++i)
        {
            Entity e = new Entity(); e.Id = 0x8000000000000500UL + (ulong)i; e.Type = (byte)EntityType.Monster; e.TemplateId = 2; e.Hp = e.MaxHp = 100;
            e.X = cx + at[i][0]; e.Y = cy + at[i][1]; e.DestX = cx; e.DestY = cy; e.Speed = 4f; e.Moving = true; e.MoveStartMs = now;
            w.Entities[e.Id] = e;
        }
        w.Streak.Register(now - 500); w.Streak.Register(now - 300); w.Streak.Register(now - 100);
        ViewEffect st = new ViewEffect(); st.Kind = EffectKind.Streak; st.StartMs = now - 250; st.DurMs = 1900; st.Value = 3; st.Text = KillStreak.Title(3); w.Effects.Add(st);
        ViewEffect b = new ViewEffect(); b.Kind = EffectKind.Banner; b.Variant = 1; b.StartMs = now - 800; b.DurMs = 2800; b.Text = "경고  오우거 출현!"; w.Effects.Add(b);
        ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = 40; v.CamX = cx; v.CamY = cy;
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            // 박동 정점(주기 시작)에 맞춘 시각
            int period = Tension.HeartPeriodMs(Tension.Level(w.Self.Hp, w.Self.MaxHp));
            long t = now - (now % period) + period;
            r.Draw(g, s, pilot, v, t);
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved " + outPath + " level=" + Tension.Level(w.Self.Hp, w.Self.MaxHp).ToString("0.00") + " period=" + period);
        }
        return 0;
    }

    private static int _fail;
    private static void Check(bool ok, string what) { if (!ok) { ++_fail; Console.WriteLine("  FAIL: " + what); } }

    /// <summary>화면에 그려진 픽셀이 배경과 얼마나 다른지 센다.</summary>
    private static int CountDiff(Bitmap bmp, Color bg)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                Color c = bmp.GetPixel(x, y);
                if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 24) ++n;
            }
        return n;
    }

    private static int SelfTest(string dataDir)
    {
        // 1) 영웅 3종 + 몬스터 10종 + 기본형: 걷기/대기/공격 프레임이 모두 그려지고 시체/번쩍임도 그려진다
        using (SpriteCache cache = new SpriteCache(2000))
        {
            CreatureRenderer cr = new CreatureRenderer(cache);
            Color bg = Color.FromArgb(74, 118, 56);
            ViewState v = new ViewState(); v.Scale = 24;
            for (int pass = 0; pass < 2; ++pass)
            {
                uint[] tpls = pass == 0 ? new uint[] { 1, 2, 3, 4, 9 } : new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
                foreach (uint tpl in tpls)
                    for (int frame = 0; frame < 3; ++frame)
                        foreach (int state in new int[] { 0, 1, 2 })        // 0 일반, 1 피격, 2 사망
                        {
                            using (Bitmap bmp = new Bitmap(320, 240, PixelFormat.Format32bppArgb))
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                g.Clear(bg);
                                Entity e = new Entity();
                                e.Type = (byte)(pass == 0 ? EntityType.Player : EntityType.Monster); e.TemplateId = tpl; e.Id = 5; e.FaceX = frame == 1 ? -1 : 1;
                                if (frame == 2 && pass == 0) e.AttackAnimMs = 100000 - 120;
                                if (frame == 0 && state != 2) { e.DestX = 99; e.Speed = 4; e.Moving = true; e.MoveStartMs = 100000 - 400; }
                                e.Dead = state == 2;
                                cr.Draw(g, e, v, 100000, 160, 190, state == 1);
                                int px = CountDiff(bmp, bg);
                                Check(px > (state == 2 ? 20 : 120), (pass == 0 ? "hero " : "monster ") + tpl + " frame " + frame + " state " + state + " draws pixels (" + px + ")");
                            }
                        }
            }
            Check(cache.Misses > 0 && cache.Count < 2000, "sprite cache used");
        }

        // 2) 보스전 장면: 확대 5~60배, 화면 크기 변화에서 예외 없이 그려지고 보이는 내용이 있다
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1;
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now); pilot.SetAuto(true);
        int bossShots = 0, telegraphShots = 0, bannerShots = 0;
        using (SceneRenderer r = new SceneRenderer())
        {
            double[] scales = { 20, 5, 60, 9, 33, 20 };
            int[] widths = { 1280, 640, 1920, 800, 1280, 1280 };
            for (int step = 0; step < 90; ++step)
            {
                for (int i = 0; i < 62; ++i)
                {
                    now += 16; s.Update(now); demo.Update(now); pilot.Update(now);
                    foreach (Entity be in s.World.Entities.Values) if (be.IsBoss && !be.Dead && pilot.TargetId != be.Id) pilot.ChaseTarget(be.Id);
                }
                ViewState v = new ViewState();
                v.Width = widths[step % widths.Length]; v.Height = v.Width * 5 / 8; v.Scale = scales[step % scales.Length];
                float sx, sy; if (s.SelfPos(now, out sx, out sy)) { v.CamX = sx; v.CamY = sy; }
                v.SelectedId = pilot.TargetId; v.HasMouse = true; v.HoverId = v.SelectedId;
                try
                {
                    using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        r.Draw(g, s, pilot, v, now);
                        Check(CountDiff(bmp, Color.FromArgb(18, 40, 24)) > 2000, "scene " + step + " has content");
                    }
                }
                catch (Exception e) { Check(false, "scene " + step + " threw " + e); }
                foreach (Entity be in s.World.Entities.Values) if (be.IsBoss && !be.Dead) ++bossShots;
                foreach (ViewEffect f in s.World.Effects) { if (f.Kind == EffectKind.Telegraph) ++telegraphShots; if (f.Kind == EffectKind.Banner) ++bannerShots; }
            }
            Check(bossShots > 0, "boss was in the scenes");
            Check(telegraphShots > 0, "telegraph effects were drawn at least once");
            // 같은 확대/화면 크기로 움직이는 동안 바닥을 계속 다시 그리지 않는다
            int before = r.GroundRebuilds;
            ViewState vv = new ViewState(); vv.Width = 1280; vv.Height = 800; vv.Scale = 20;
            using (Bitmap bmp = new Bitmap(1280, 800, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                vv.CamX = 500; vv.CamY = 500; r.Draw(g, s, pilot, vv, now);
                int b0 = r.GroundRebuilds;
                for (int i = 0; i < 40; ++i) { vv.CamX += 0.2f; r.Draw(g, s, pilot, vv, now + i * 16); }
                Check(r.GroundRebuilds == b0, "ground is cached while the camera moves a little (" + (r.GroundRebuilds - b0) + " rebuilds)");
                vv.CamX += 40f; r.Draw(g, s, pilot, vv, now);
                Check(r.GroundRebuilds == b0 + 1, "ground rebuilds once after a long move");
                // 영웅 선택 창: 모든 영웅 줄 위치에서 예외 없이 그려지고, 클릭 영역이 서로 겹치지 않는다
                vv.HeroSelectOpen = true;
                int heroCount = data.HeroIds().Count;
                for (int i = 0; i < heroCount; ++i) { vv.HeroSelectIndex = i; r.Draw(g, s, pilot, vv, now); }
                for (int i = 0; i + 1 < heroCount; ++i)
                    Check(!HudRenderer.HeroRowRect(vv, heroCount, i).IntersectsWith(HudRenderer.HeroRowRect(vv, heroCount, i + 1)), "hero rows do not overlap (" + i + ")");
                Check(heroCount == 0 || HudRenderer.HeroRowRect(vv, heroCount, heroCount - 1).Bottom < vv.Height, "hero rows fit in the window");
                vv.HeroSelectOpen = false;
            }
        }
        Console.WriteLine(_fail == 0 ? "selftest OK" : "selftest FAILED (" + _fail + ")");
        return _fail == 0 ? 0 : 1;
    }

    private static int Bench(string dataDir, int frames)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1;
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now); pilot.SetAuto(true);
        ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = cfg.PixelsPerUnit;
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            // 먼저 20초 진행(캐시 데우기 제외)
            for (int i = 0; i < 1250; ++i) { now += 16; s.Update(now); demo.Update(now); pilot.Update(now); }
            r.Draw(g, s, pilot, v, now);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            double worst = 0, total = 0;
            int gc0 = GC.CollectionCount(0);
            for (int i = 0; i < frames; ++i)
            {
                now += 16; s.Update(now); demo.Update(now); pilot.Update(now);
                float sx, sy; if (s.SelfPos(now, out sx, out sy)) { v.CamX = sx; v.CamY = sy; }
                v.SelectedId = pilot.TargetId;
                sw.Restart(); r.Draw(g, s, pilot, v, now); sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds; total += ms; if (ms > worst) worst = ms;
            }
            string[] names = { "ground", "border", "world", "vignette", "hud" };
            for (int i = 0; i < names.Length; ++i) Console.WriteLine("  " + names[i] + " " + (r.PartMs[i] / (frames + 1)).ToString("0.00") + "ms");
            Console.WriteLine("  ground rebuilds=" + r.GroundRebuilds);
            Console.WriteLine("  gen0 GC=" + (GC.CollectionCount(0) - gc0) + " (frames=" + frames + ")");
            Console.WriteLine("frames=" + frames + " avg=" + (total / frames).ToString("0.00") + "ms worst=" + worst.ToString("0.0") + "ms entities=" + s.World.Entities.Count + " effects=" + s.World.Effects.Count);
        }
        return 0;
    }

    private static string AssetsDir()
    {
        string d = Environment.GetEnvironmentVariable("SNAP_ASSETS");
        return string.IsNullOrEmpty(d) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets") : d;
    }

    private static void LoadAssets()
    {
        System.Collections.Generic.List<string> warn = new System.Collections.Generic.List<string>();
        int n = AssetSprites.Load(AssetsDir(), warn);
        Backdrop.Load(AssetsDir(), warn);
        foreach (string w in warn) Console.WriteLine("warn: assets: " + w);
        if (n > 0) Console.WriteLine("assets: " + n + " sprite set(s) from " + AssetsDir());
    }

    /// <summary>이미지 에셋 로더 점검: 임시 폴더에 합성 이미지를 만들어 방향 선택/그리기/잘못된 항목 거부/폴백을 확인한다.</summary>
    private static void AssetTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gv_assets_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "t"));
        try
        {
            Color[] cols = { Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Cyan, Color.Magenta, Color.White, Color.Orange };
            for (int i = 0; i < 8; ++i)
                using (Bitmap b = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.Transparent); using (Brush br = new SolidBrush(cols[i])) g.FillRectangle(br, 16, 8, 32, 48); }
                    b.Save(Path.Combine(dir, "t", (i * 45).ToString("000") + ".png"), ImageFormat.Png);
                }
            using (Bitmap odd = new Bitmap(32, 32)) odd.Save(Path.Combine(dir, "t", "odd.png"), ImageFormat.Png);
            File.WriteAllText(Path.Combine(dir, "t", "junk.png"), "RIFF....WEBPVP8X");
            string man = "{ \"sprites\": [\n" +
                "{\"kind\":\"monster\",\"template\":99,\"pattern\":\"t/{0:000}.png\",\"angles\":8,\"height\":3},\n" +
                "{\"kind\":\"monster\",\"template\":99,\"pattern\":\"t/{0:000}.png\",\"angles\":8,\"height\":3},\n" +      // 중복
                "{\"kind\":\"hero\",\"template\":5,\"pattern\":\"t/{0:000}.png\",\"angles\":9,\"height\":3},\n" +          // 파일 없음(40도)
                "{\"kind\":\"hero\",\"template\":6,\"pattern\":\"t/{0:000}.png\",\"angles\":8,\"height\":99},\n" +         // 높이 범위
                "{\"kind\":\"hero\",\"template\":7,\"pattern\":\"t/{0:000}.png\",\"angles\":0,\"height\":3},\n" +          // 방향 수
                "{\"kind\":\"boss\",\"template\":7,\"pattern\":\"x\",\"angles\":1,\"height\":3},\n" +                      // kind
                "{\"kind\":\"hero\",\"template\":8,\"pattern\":\"t/junk.png\",\"angles\":1,\"height\":3},\n" +             // 못 읽는 이미지
                "{\"kind\":\"hero\",\"template\":9,\"pattern\":\"t/{1}.png\",\"angles\":1,\"height\":3},\n" +              // 형식 오류
                "{\"kind\":\"hero\",\"template\":3,\"pattern\":\"t/{0:000}.png\",\"angles\":1,\"height\":3}]}";
            File.WriteAllText(Path.Combine(dir, "sprites.json"), man);
            System.Collections.Generic.List<string> warn = new System.Collections.Generic.List<string>();
            int n = AssetSprites.Load(dir, warn);
            Check(n == 2, "asset: 정상 2개만 읽힘 (실제 " + n + ")");
            Check(warn.Count == 7, "asset: 잘못된 항목 7개가 각각 경고 (실제 " + warn.Count + ")");
            AssetSet a = AssetSprites.Find(true, 99);
            Check(a != null && AssetSprites.Find(false, 99) == null && AssetSprites.Find(true, 5) == null, "asset: 종류 구분/없는 종류는 null");
            if (a != null)
            {
                Check(a.IndexFor(0, 1) == 0 && a.IndexFor(1, 0) == 2 && a.IndexFor(0, -1) == 4 && a.IndexFor(-1, 0) == 6 && a.IndexFor(0, 0) == 0 && a.IndexFor(-0.01f, 1) == 0,
                    "asset: 방향 -> 번호 (아래 0, 오른쪽 2, 위 4, 왼쪽 6)");
                Check(Math.Abs(a.Look.H - 3f) < 1e-4f && a.Look.HalfW > 0.9f && a.Look.HalfW < 1.2f, "asset: Look 이 내용 크기에서 나옴 (HalfW=" + a.Look.HalfW + ")");
                Check(CreatureRenderer.LookOf(true, 99).H == 3f, "asset: LookOf 가 에셋 크기를 돌려줌");
                // 방향마다 다른 색이 찍히는지
                using (SpriteCache cache = new SpriteCache(100))
                {
                    CreatureRenderer cr = new CreatureRenderer(cache);
                    ViewState v = new ViewState(); v.Scale = 24;
                    float[][] dirs = { new float[] { 0, 1 }, new float[] { 1, 0 }, new float[] { 0, -1 }, new float[] { -1, 0 } };
                    int[] idx = { 0, 2, 4, 6 };
                    for (int k = 0; k < 4; ++k)
                    {
                        Entity e = new Entity(); e.Type = (byte)EntityType.Monster; e.TemplateId = 99; e.Id = 1; e.FaceX = dirs[k][0]; e.FaceY = dirs[k][1];
                        using (Bitmap bmp = new Bitmap(200, 200, PixelFormat.Format32bppArgb))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Black);
                            cr.Draw(g, e, v, 100000, 100, 150, false);
                            Color c = bmp.GetPixel(100, 150 - (int)(1.2f * 24));      // 몸 한가운데쯤
                            Color want = cols[idx[k]];
                            Check(Math.Abs(c.R - want.R) + Math.Abs(c.G - want.G) + Math.Abs(c.B - want.B) < 40, "asset: 방향 " + k + " 에서 기대한 이미지가 그려짐 (" + c + ")");
                            // 발 밑은 기준점 위, 아래로는 번지지 않음
                            Check(bmp.GetPixel(100, 152).ToArgb() == Color.Black.ToArgb(), "asset: 발 밑 아래로 그려지지 않음");
                            e.Dead = true; g.Clear(Color.Black); cr.Draw(g, e, v, 100000, 100, 150, false);
                            Check(CountDiff(bmp, Color.Black) > 50, "asset: 시체 그림이 그려짐");
                        }
                    }
                }
            }
            AssetSprites.Clear();
            // 걷기 프레임: 정상 1개, 잘못된 3개(개수 1 / 패턴 없음 / 크기 다름)
            for (int k = 0; k < 2; ++k)
                using (Bitmap b = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.Transparent); using (Brush br = new SolidBrush(Color.Gray)) g.FillRectangle(br, 20 + k * 4, 8, 24, 48); }
                    b.Save(Path.Combine(dir, "t", "w_" + k + ".png"), ImageFormat.Png);
                }
            for (int k = 0; k < 2; ++k) using (Bitmap big = new Bitmap(80, 80, PixelFormat.Format32bppArgb)) big.Save(Path.Combine(dir, "t", "wbig_" + k + ".png"), ImageFormat.Png);
            string man2 = "{ \"sprites\": [\n" +
                "{\"kind\":\"monster\",\"template\":50,\"pattern\":\"t/000.png\",\"angles\":1,\"height\":3,\"walkFrames\":2,\"walkPattern\":\"t/w_{1}.png\"},\n" +
                "{\"kind\":\"monster\",\"template\":51,\"pattern\":\"t/000.png\",\"angles\":1,\"height\":3,\"walkFrames\":1,\"walkPattern\":\"t/w_{1}.png\"},\n" +
                "{\"kind\":\"monster\",\"template\":52,\"pattern\":\"t/000.png\",\"angles\":1,\"height\":3,\"walkFrames\":2},\n" +
                "{\"kind\":\"monster\",\"template\":53,\"pattern\":\"t/000.png\",\"angles\":1,\"height\":3,\"walkFrames\":2,\"walkPattern\":\"t/wbig_{1}.png\"}]}";
            File.WriteAllText(Path.Combine(dir, "sprites.json"), man2);
            warn.Clear();
            int n2 = AssetSprites.Load(dir, warn);
            AssetSet w = AssetSprites.Find(false, 50);
            Check(w == null, "asset: (몬스터 종류 구분) 걷기 항목 조회 확인용");
            w = AssetSprites.Find(true, 50);
            Check(n2 == 1 && w != null && w.WalkCount == 2, "asset: 걷기 프레임 정상 항목만 읽힘 (n=" + n2 + ", 경고 " + warn.Count + ")");
            Check(warn.Count == 3, "asset: 걷기 프레임 잘못된 항목 3개가 각각 경고 (실제 " + warn.Count + ")");
            AssetSprites.Clear();
            Check(AssetSprites.Find(true, 99) == null && AssetSprites.Load(Path.Combine(dir, "none"), warn) == 0, "asset: 비우기 / 폴더 없으면 0");
        }
        finally { AssetSprites.Clear(); try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    private static void GroundTests()
    {
        // 바닥을 조각으로 나눠 그린 결과가 한 번에 그린 결과와 같은지 (이음매 없음)
        for (int k = 0; k < 2; ++k)
        {
            ViewState v = new ViewState(); v.Width = 320; v.Height = 240; v.Scale = k == 0 ? 24 : 10; v.CamX = 22.3f; v.CamY = 21.7f;
            float minX = 20, minY = 20, maxX = 40, maxY = 36;     // 월드 가장자리가 화면 안에 걸리게
            using (GroundRenderer gr = new GroundRenderer())
            using (Bitmap a = gr.RenderForTest(v, false, minX, minY, maxX, maxY))
            using (Bitmap b = gr.RenderForTest(v, true, minX, minY, maxX, maxY))
            {
                int diff = 0, dark = 0;
                for (int y = 0; y < a.Height; ++y)
                    for (int x = 0; x < a.Width; ++x)
                    {
                        Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
                        int dm = Math.Max(Math.Abs(ca.R - cb.R), Math.Max(Math.Abs(ca.G - cb.G), Math.Abs(ca.B - cb.B)));
                        if (dm > 3) ++diff;                       // 늘리기 반올림 차이(1~2단계)는 허용, 그 이상은 이음매
                        if (ca.G < 50) ++dark;
                    }
                Check(diff == 0, "ground: 조각 렌더 == 한 번에 렌더 (scale " + v.Scale + ", 3단계 넘게 다른 픽셀 " + diff + ")");
                Check(dark > 100 && dark < a.Width * a.Height, "ground: 월드 바깥은 어둡고 안쪽은 밝다 (dark=" + dark + ")");
            }
        }
    }

    private static void BackdropTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gv_bd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            System.Collections.Generic.List<string> warn = new System.Collections.Generic.List<string>();
            Check(!Backdrop.Load(dir, warn) && !Backdrop.Loaded && warn.Count == 0, "backdrop: 파일이 없으면 false, 경고 없음");
            using (Bitmap bg = new Bitmap(200, 100, PixelFormat.Format32bppArgb))      // 왼쪽 빨강 / 오른쪽 파랑
            {
                using (Graphics g = Graphics.FromImage(bg)) { g.Clear(Color.Red); using (Brush b = new SolidBrush(Color.Blue)) g.FillRectangle(b, 100, 0, 100, 100); }
                bg.Save(Path.Combine(dir, "backdrop.png"), ImageFormat.Png);
            }
            Check(Backdrop.Load(dir, warn) && Backdrop.Loaded && Backdrop.SourceWidth == 200, "backdrop: png 를 읽음");
            using (Bitmap t = new Bitmap(100, 100, PixelFormat.Format32bppArgb))      // 정사각형 화면: 가운데를 덮게 잘라 양쪽 색이 다 남아야 한다
            using (Graphics g = Graphics.FromImage(t))
            {
                Check(Backdrop.Draw(g, 100, 100), "backdrop: 그림");
                Color l = t.GetPixel(10, 50), r = t.GetPixel(90, 50);
                Check(l.R > 200 && l.B < 60 && r.B > 200 && r.R < 60, "backdrop: 덮기 방식으로 가운데가 잘려 그려짐 (" + l + ", " + r + ")");
            }
            File.WriteAllText(Path.Combine(dir, "backdrop.png"), "not an image");
            Check(!Backdrop.Load(dir, warn) && !Backdrop.Loaded && warn.Count == 1, "backdrop: 깨진 파일은 false + 경고 1개");
            using (Bitmap t = new Bitmap(10, 10)) using (Graphics g = Graphics.FromImage(t)) Check(!Backdrop.Draw(g, 10, 10), "backdrop: 없으면 그리지 않음");
        }
        finally { Backdrop.Clear(); try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    /// <summary>스킬 연출 한눈에 보기: 스킬 1~27 을 격자에 놓고 같은 진행도(0..1)로 그린다. 쓰는 법: --fxsheet DataDir out.png [progress]</summary>
    static int FxSheet(string dir, string outPng, double prog)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dir; cfg.HeroId = 1;
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        long now = 100000;
        s.World.Entities.Clear(); s.World.Effects.Clear();
        ViewState v = new ViewState();
        v.Width = 1600; v.Height = 1250; v.Scale = 40;
        int cols = 5; float gx = 5.5f, gy = 5.2f;
        v.CamX = 1000 + gx * (cols - 1) / 2f; v.CamY = 1000 + gy * 2.5f;
        for (uint id = 1; id <= 27; ++id)
        {
            int c = (int)(id - 1) % cols, r = (int)(id - 1) / cols;
            float x = 1000 + c * gx, y = 1000 + r * gy;
            SkillFxInfo info = SkillFxTable.For(data, id);
            SkillRow sk = data.Skill(id);
            Entity caster = new Entity(); caster.Id = 5000 + id; caster.Type = 1; caster.X = x - 2.2f; caster.Y = y; caster.Hp = caster.MaxHp = 100; caster.TemplateId = 1;
            Entity tgt = new Entity(); tgt.Id = 6000 + id; tgt.Type = 2; tgt.X = x + 1.2f; tgt.Y = y; tgt.Hp = tgt.MaxHp = 100; tgt.TemplateId = 1;
            s.World.Entities[caster.Id] = caster; s.World.Entities[tgt.Id] = tgt;
            bool area = sk != null && sk.IsBurst;
            bool aimed = sk != null && sk.IsAimed;
            bool proj = sk != null && sk.ProjectileSpeed > 0;
            if (area || info.BurstMs > 0 && (info.Style == SkillFxStyle.Whirl || info.Style == SkillFxStyle.Nova || info.Style == SkillFxStyle.Roar || info.Style == SkillFxStyle.Heal))
            {
                ViewEffect e = new ViewEffect(); e.Kind = area ? EffectKind.Ring : EffectKind.Slash; e.Variant = area ? 0 : 1; e.DurMs = info.BurstMs > 0 ? info.BurstMs : 450;
                e.StartMs = now - (long)(e.DurMs * prog); e.X = x; e.Y = y; e.Radius = sk != null && sk.Radius > 0 ? (float)sk.Radius : 3.5f; e.SkillId = id; e.EntityId = caster.Id;
                if (!area) { e.X = x; e.Y = y; }
                s.World.Effects.Add(e);
            }
            else if (aimed)
            {
                s.World.Apply(GameViewer.Core.Protocol.ServerPackets.F(GameViewer.Core.Protocol.ServerPackets.Skill(caster.Id, id, 0, tgt.X + 3f, y, 1)), now - (long)(520 * prog));
            }
            else if (proj)
            {
                ViewEffect e = new ViewEffect(); e.Kind = EffectKind.Projectile; e.Value = 1000; e.StartMs = now - (long)(1000 * prog);
                e.X = caster.X; e.Y = caster.Y; e.X2 = tgt.X; e.Y2 = tgt.Y; e.SkillId = id; e.EntityId = caster.Id; e.DurMs = 1000;
                s.World.Effects.Add(e);
                ViewEffect i = new ViewEffect(); i.Kind = EffectKind.Spark; i.DurMs = 800; i.StartMs = now - (long)(800 * Math.Max(0, prog - 0.2)); i.X = tgt.X + 4; i.Y = y; i.SkillId = id;
                s.World.Effects.Add(i);
            }
            else
            {
                ViewEffect e = new ViewEffect(); e.Kind = EffectKind.Slash; e.Variant = 1; e.DurMs = 420; e.StartMs = now - (long)(420 * prog); e.X = tgt.X; e.Y = tgt.Y; e.SkillId = id; e.EntityId = caster.Id;
                s.World.Effects.Add(e);
            }
        }
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            AutoPilot pilot = new AutoPilot(s);
            r.Draw(g, s, pilot, v, now);
            bmp.Save(outPng, ImageFormat.Png);
        }
        return 0;
    }

    /// <summary>기본 공격/창술사 스킬 연출을 한눈에: 행 = 무기(또는 스킬), 열 = 진행도 4단계. set: basic | skill</summary>
    static int AtkFx(string dir, string outPng, string set)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dir; cfg.HeroId = 4;
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        long now = 100000;
        s.World.Entities.Clear(); s.World.Effects.Clear();
        // 행: 이름, 시전자 영웅(0이면 몬스터), 거리, 스킬 ID(0이면 기본 공격)
        object[][] rows = set == "skill"
            ? new object[][] { new object[] { "18", 4u, 2.6f, 18u }, new object[] { "24", 4u, 2.6f, 24u }, new object[] { "26", 4u, 2.6f, 26u }, new object[] { "19", 4u, 2.6f, 19u }, new object[] { "27", 4u, 2.6f, 27u }, new object[] { "25", 4u, 2.6f, 25u } }
            : new object[][] { new object[] { "sword", 1u, 2.2f, 0u }, new object[] { "lance", 4u, 3.0f, 0u }, new object[] { "claw", 0u, 1.8f, 0u }, new object[] { "bow", 2u, 8.0f, 0u }, new object[] { "staff", 3u, 8.0f, 0u } };
        double[] progs = { 0.12, 0.32, 0.55, 0.8 };
        float gx = 12f, gy = 4.6f;
        ViewState v = new ViewState();
        v.Scale = 34; v.Width = 1500; v.Height = (int)(rows.Length * gy * 34 + 20);
        v.CamX = 1000 + gx * (progs.Length - 1) / 2f - 1.5f; v.CamY = 1000 + gy * (rows.Length - 1) / 2f - 0.9f;
        ulong id = 5000;
        for (int r = 0; r < rows.Length; ++r)
            for (int c = 0; c < progs.Length; ++c)
            {
                uint hero = (uint)rows[r][1]; float dist = (float)rows[r][2]; uint skill = (uint)rows[r][3];
                float x = 1000 + c * gx - 4f, y = 1000 + r * gy;
                Entity caster = new Entity(); caster.Id = ++id; caster.Type = hero == 0 ? (byte)1 : (byte)0; caster.X = x; caster.Y = y; caster.Hp = caster.MaxHp = 100; caster.TemplateId = hero == 0 ? 1u : hero;
                Entity tgt = new Entity(); tgt.Id = ++id; tgt.Type = hero == 0 ? (byte)0 : (byte)1; tgt.X = x + dist; tgt.Y = y; tgt.Hp = tgt.MaxHp = 100; tgt.TemplateId = 1;
                s.World.Entities[caster.Id] = caster; s.World.Entities[tgt.Id] = tgt;
                caster.Face(tgt.X, tgt.Y, now);
                if (skill == 0) caster.AttackAnimMs = now - (long)(320 * progs[c]);
                if (skill != 0)
                {
                    s.World.Apply(GameViewer.Core.Protocol.ServerPackets.F(GameViewer.Core.Protocol.ServerPackets.Skill(caster.Id, skill, 0, tgt.X + (skill == 18 || skill == 27 ? 3f : 0f), y, 1)), now - (long)(500 * progs[c]));
                }
                else
                {
                    bool ranged = dist > 4f;
                    ViewEffect e = new ViewEffect(); e.Kind = ranged ? EffectKind.Tracer : EffectKind.Slash; e.DurMs = ranged ? 260 : 320; e.StartMs = now - (long)(e.DurMs * progs[c]);
                    e.X = tgt.X; e.Y = tgt.Y; e.EntityId = caster.Id; s.World.Effects.Add(e);
                    ViewEffect sp = new ViewEffect(); sp.Kind = EffectKind.Spark; sp.DurMs = 360; sp.StartMs = now - (long)(360 * Math.Max(0, progs[c] - (ranged ? 0.4 : 0.1)));
                    sp.X = tgt.X; sp.Y = tgt.Y; sp.EntityId = tgt.Id; sp.TargetId = caster.Id; sp.Variant = c == 3 ? 1 : 0; sp.Value = 30;
                    if (progs[c] > (ranged ? 0.4 : 0.1)) s.World.Effects.Add(sp);
                }
            }
        using (SceneRenderer rd = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            AutoPilot pilot = new AutoPilot(s);
            rd.Draw(g, s, pilot, v, now);
            bmp.Save(outPng, ImageFormat.Png);
        }
        return 0;
    }

    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--selftest") { AssetTests(); GroundTests(); BackdropTests(); }
        LoadAssets();
        if (args.Length >= 3 && args[0] == "--tension") return TensionScene(args[1], args[2]);
        if (args.Length >= 3 && args[0] == "--heroscene") return HeroScene(args[1], args[2]);
        if (args.Length >= 2 && args[0] == "--herosheet") return HeroSheet(args[1], args.Length > 2 ? double.Parse(args[2]) : 40);
        if (args.Length >= 2 && args[0] == "--atksheet") return AtkSheet(args[1], args.Length > 2 ? double.Parse(args[2]) : 24);
        if (args.Length >= 2 && args[0] == "--sheet") return Sheet(args[1], args.Length > 2 ? double.Parse(args[2]) : 28);
        if (args.Length >= 2 && args[0] == "--selftest") return SelfTest(args[1]);
        if (args.Length >= 2 && args[0] == "--bench") return Bench(args[1], args.Length > 2 ? int.Parse(args[2]) : 300);
        if (args.Length >= 4 && args[0] == "--atkfx") return AtkFx(args[1], args[2], args[3]);
        if (args.Length >= 3 && args[0] == "--fxsheet") return FxSheet(args[1], args[2], args.Length > 3 ? double.Parse(args[3]) : 0.4);
        if (args.Length < 2) { Console.WriteLine("usage: RenderSnapshot <GameDataDir> <outDir> [seconds...]"); return 2; }
        Directory.CreateDirectory(args[1]);
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = args[0]; cfg.HeroId = 1;
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        foreach (string w in data.Warnings) Console.WriteLine("warn: " + w);

        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000;
        demo.Start(now);
        pilot.SetAuto(true);
        bool bossMode = Environment.GetEnvironmentVariable("SNAP_BOSS") == "1";      // 내 영웅도 보스를 추적

        double[] shots = new double[Math.Max(1, args.Length - 2)];
        if (args.Length > 2) for (int i = 2; i < args.Length; ++i) shots[i - 2] = double.Parse(args[i]); else shots[0] = 6;

        ViewState v = new ViewState();
        v.Width = 1280; v.Height = 800; v.Scale = cfg.PixelsPerUnit;
        using (SceneRenderer r = new SceneRenderer())
        {
            int next = 0;
            long start = now;
            long limit = start + (long)(shots[shots.Length - 1] * 1000) + 100;
            while (now <= limit && next < shots.Length)
            {
                now += 16;
                s.Update(now); demo.Update(now); pilot.Update(now);
                float sx, sy;
                if (bossMode)
                    foreach (Entity be in s.World.Entities.Values)
                        if (be.IsBoss && !be.Dead && pilot.TargetId != be.Id) pilot.ChaseTarget(be.Id);
                if (s.SelfPos(now, out sx, out sy)) { v.CamX = sx; v.CamY = sy; }
                string cam = Environment.GetEnvironmentVariable("SNAP_CAM");        // "x,y" 이면 카메라를 그 월드 좌표에 고정
                if (!string.IsNullOrEmpty(cam)) { string[] cp = cam.Split(','); v.CamX = float.Parse(cp[0], System.Globalization.CultureInfo.InvariantCulture); v.CamY = float.Parse(cp[1], System.Globalization.CultureInfo.InvariantCulture); }
                v.SelectedId = pilot.TargetId;
                v.HeroSelectOpen = Environment.GetEnvironmentVariable("SNAP_HEROSEL") == "1";       // 영웅 선택 창을 띄운 장면
                if (now - start >= (long)(shots[next] * 1000))
                {
                    using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        r.Draw(g, s, pilot, v, now);
                        string path = Path.Combine(args[1], "shot_" + shots[next].ToString("00.0") + "s.png");
                        bmp.Save(path, ImageFormat.Png);
                        Console.WriteLine("saved " + path + "  entities=" + s.World.Entities.Count + " effects=" + s.World.Effects.Count + " note=" + pilot.Note);
                    }
                    ++next;
                }
            }
        }
        return 0;
    }
}
