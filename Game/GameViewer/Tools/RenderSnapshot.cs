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
            uint[] mons = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 101, 105, 114 };
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
    /// <summary>몬스터 6종의 공격 연출(구운 자세 + MonsterFx)을 시간 순서로 늘어놓은 그림. --monfx out.png [scale] [faceX faceY]</summary>
    /// <summary>--views dataDir outPrefix [scale]: SNAP_ZONE 존의 지정 위치를 사진으로 찍는다(환경변수 VIEWS="x,y;x,y").</summary>
    private static int Views(string dataDir, string prefix, double scale)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        long now = 100000; demo.Start(now);
        for (int i = 0; i < 30; ++i) { now += 16; s.Update(now); demo.Update(now); }
        AutoPilot pilot = new AutoPilot(s);
        string[] spots = (Environment.GetEnvironmentVariable("VIEWS") ?? "500,540;330,300;30,30").Split(';');
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(1280, 800, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            for (int i = 0; i < spots.Length; ++i)
            {
                string[] xy = spots[i].Split(',');
                ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = scale;
                v.CamX = float.Parse(xy[0]); v.CamY = float.Parse(xy[1]);
                if (Environment.GetEnvironmentVariable("VIEWMAP") == "1")        // 월드맵 창 + 이동 목적지 마커 확인용
                {
                    v.MapOpen = true;
                    Entity me = s.World.Self;
                    if (me != null) s.MoveTo(me.X + 30f, me.Y + 18f, now, false);
                }
                for (int k = 0; k < 20; ++k) r.Draw(g, s, pilot, v, now + k * 16);
                bmp.Save(prefix + i + ".png", ImageFormat.Png);
                Console.WriteLine("saved " + prefix + i + ".png");
            }
        }
        return 0;
    }

    private static int IconSheet(string outPath, string dataDir)
    {
        GameData data = GameData.Load(dataDir, 1);
        Resources res = new Resources();
        int n = 28, cols = 7, cell = 96;
        using (Bitmap bmp = new Bitmap(cols * cell, ((n + cols - 1) / cols) * cell))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(30, 36, 44));
            for (int i = 0; i < n; ++i)
            {
                float x = (i % cols) * cell + 10, y = (i / cols) * cell + 6, s = 58;
                using (System.Drawing.Drawing2D.GraphicsPath p = P.RoundPath(x, y, s, s, 8))
                {
                    g.FillPath(new SolidBrush(Color.FromArgb(40, 46, 58)), p);
                    System.Drawing.Drawing2D.GraphicsState gs = g.Save(); g.SetClip(p, System.Drawing.Drawing2D.CombineMode.Intersect);
                    SkillRow sk = i == 0 ? null : data.Skill((uint)i);
                    Ui.SkillIcon(g, res, x + (s - s * 0.58f) / 2, y + 1, s * 0.58f, sk, i == 0 ? null : SkillFxTable.For(data, (uint)i), i == 0);
                    g.Restore(gs);
                }
                g.DrawString(i == 0 ? "basic" : i + " " + data.SkillName((uint)i), res.Small, Brushes.White, x - 6, y + 62);
            }
            bmp.Save(outPath);
        }
        return 0;
    }

    /// <summary>--matfx out.png assetsDir: 재질별 타격 파편(위 3칸)과 사망 파열(아래 3칸)을 진행 순서대로 늘어놓은 그림.</summary>
    private static int MatFxSheet(string outPath, string assets)
    {
        string[] names = { "slime", "mushroom", "treant", "skeleton", "golem", "ironknight", "crab", "flame", "ghost", "wolf" };
        HitMat[] mats = { HitMat.Gel, HitMat.Plant, HitMat.Wood, HitMat.Bone, HitMat.Stone, HitMat.Metal, HitMat.Shell, HitMat.Fire, HitMat.Spirit, HitMat.Flesh };
        float[] hp = { 0.15f, 0.35f, 0.65f }, dp = { 0.15f, 0.4f, 0.75f };
        int cw = 200, ch = 200, cols = 6;
        using (Resources res = new Resources())
        using (EffectRenderer er = new EffectRenderer(res))
        using (Bitmap bmp = new Bitmap(cols * cw, names.Length * ch, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            MaterialFx mf = new MaterialFx(res, er);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(70, 100, 70));
            ViewState v = new ViewState(); v.Scale = 40;
            for (int r = 0; r < names.Length; ++r)
            {
                using (Bitmap spr = new Bitmap(System.IO.Path.Combine(assets, names[r] + "/000.png")))
                for (int c = 0; c < cols; ++c)
                {
                    float x0 = c * cw, y0 = r * ch, sx = x0 + cw / 2f, sy = y0 + ch * 0.72f;
                    g.FillEllipse(new SolidBrush(Color.FromArgb(60, 0, 0, 0)), sx - 42, sy - 9, 84, 18);
                    if (c < 3) { g.DrawImage(spr, sx - 80, sy - 150, 160, 160); mf.Debris(g, mats[r], (uint)(1000 + r * 31 + c), sx, sy - 70, 0.3f, c == 2, hp[c], v); }
                    else { if (dp[c - 3] < 0.4f) g.DrawImage(spr, sx - 80, sy - 150, 160, 160); mf.Burst(g, mats[r], (uint)(2000 + r * 17), sx, sy, dp[c - 3], v, 2.2f); }
                }
            }
            bmp.Save(outPath);
        }
        return 0;
    }

    private static int MonFxSheet(string outPath, double scale, float faceX, float faceY)
    {
        using (SpriteCache cache = new SpriteCache(2000))
        using (Resources res = new Resources())
        {
            CreatureRenderer cr = new CreatureRenderer(cache);
            MonsterFx fxr = new MonsterFx(res);
            ViewState v = new ViewState(); v.Scale = scale;
            uint[] tpls = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114 };
            string tplEnv = Environment.GetEnvironmentVariable("SNAP_TPLS");        // 예: "115,116,117" 이면 그 템플릿만 그린다
            if (!string.IsNullOrEmpty(tplEnv)) { string[] tp = tplEnv.Split(','); tpls = new uint[tp.Length]; for (int q = 0; q < tp.Length; ++q) tpls[q] = uint.Parse(tp[q]); }
            long[] ts = { 0, 50, 90, 140, 200, 260, 340, 440 };
            int cellW = (int)(scale * 9), cellH = (int)(scale * 8);
            using (Bitmap bmp = new Bitmap(cellW * ts.Length, cellH * tpls.Length, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font f = new Font("Arial", 9f))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(74, 118, 56));
                for (int r = 0; r < tpls.Length; ++r)
                    for (int c = 0; c < ts.Length; ++c)
                    {
                        long now = 100000;
                        Entity e = new Entity();
                        e.Type = (byte)EntityType.Monster; e.TemplateId = tpls[r]; e.Id = (ulong)(r * 20 + c + 1);
                        e.FaceX = faceX; e.FaceY = faceY; e.Hp = e.MaxHp = 100;
                        if (tpls[r] > 100) e.Boss = new BossRow();
                        e.AttackAnimMs = now - ts[c];
                        AssetSet bs = AssetSprites.Find(true, e.TemplateId);
                        Look look = new Look(bs != null ? 1.3f : 1.2f, 3f);
                        BodyPose pose = AttackPose.Compute(e, now, bs != null && bs.AttackBody != null);
                        float sx = c * cellW + cellW / 2f, sy = r * cellH + cellH - (float)scale * 1.2f;
                        g.FillEllipse(new SolidBrush(Color.FromArgb(70, 0, 0, 0)), sx - (float)scale * 1.2f, sy - (float)scale * 0.25f, (float)scale * 2.4f, (float)scale * 0.55f);
                        float bx = sx + (float)scale * pose.OffX, by = sy + (float)scale * pose.OffY * 0.6f - (float)scale * pose.Lift;
                        fxr.Draw(g, e, v, look, bx, by, now, false);
                        System.Drawing.Drawing2D.GraphicsState gs = g.Save();
                        g.TranslateTransform(bx, by);
                        g.ScaleTransform(pose.ScaleX, pose.ScaleY);
                        cr.Draw(g, e, v, now, 0f, 0f, false);
                        g.Restore(gs);
                        fxr.Draw(g, e, v, look, bx, sy + (float)scale * pose.OffY * 0.6f, now, true);
                        g.DrawString(ts[c] + "ms", f, Brushes.White, c * cellW + 4, r * cellH + 2);
                    }
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("saved " + outPath);
            }
        }
        return 0;
    }

    private static int AtkSheet(string outPath, double scale)
    {
        using (SpriteCache cache = new SpriteCache(2000))
        {
            CreatureRenderer cr = new CreatureRenderer(cache);
            ViewState v = new ViewState(); v.Scale = scale;
            // (영웅/몬스터, 종류)
            object[][] rows = {
                new object[] { false, 1u, "hero" }, new object[] { true, 5u, "wolf" }, new object[] { true, 1u, "slime" },
                new object[] { true, 114u, "boss" }, new object[] { true, 11u, "knight" } };
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
                        AssetSet bs = e.IsMonster ? AssetSprites.Find(true, e.TemplateId) : null;
                        BodyPose pose = AttackPose.Compute(e, now, bs != null && bs.AttackBody != null);
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
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000 + long.Parse(Environment.GetEnvironmentVariable("SNAP_T0") ?? "0"); demo.Start(now);
        bool skillMode = Environment.GetEnvironmentVariable("SNAP_SKILL") == "1";
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
                foreach (Entity e in heroes) { e.AttackAnimMs = now - times[ti]; e.AttackKind = (byte)(skillMode ? 1 : (e.TemplateId == 2 || e.TemplateId == 3 ? 2 : 0)); }
                r.Draw(g, s, pilot, v, now); r.Draw(g, s, pilot, v, now);
                og.DrawImage(bmp, new Rectangle(0, ti * cropH, cropW, cropH), new Rectangle(0, v.Height / 2 - 150, cropW, cropH), GraphicsUnit.Pixel);
            }
            outb.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved " + outPath);
        }
        return 0;
    }

    /// <summary>
    /// 영웅 4종의 상태별 그림: 대기(숨쉬기/눈 깜빡임), 피격, 사망 단계, 스킬 연출(발밑), 시전 중. 줄 하나가 한 상태, 가로로 영웅 4종.
    /// </summary>
    private static int HeroPoses(string dataDir, string outPath)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
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
            e.X = e.DestX = cx + (h - 2.5f) * 6.2f; e.Y = e.DestY = cy; e.FaceX = 0.4f; e.FaceY = 1f; e.MoveStartMs = now;
            s.World.Entities[e.Id] = e; heroes.Add(e);
        }
        string[] rows = { "idle0", "idle6(blink)", "hit", "death0", "death1", "death2", "skill", "cast" };
        int cropW = 1280, cropH = 250;
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        using (Bitmap outb = new Bitmap(cropW, cropH * rows.Length, PixelFormat.Format32bppArgb))
        using (Graphics og = Graphics.FromImage(outb))
        {
            for (int ri = 0; ri < rows.Length; ++ri)
            {
                long t = now + 100000;
                if (ri == 0) t = (now / 260 + 1) * 260 * 8 + 0 * 260 + 8 * 260 * 100;     // 프레임 번호 0 근처
                foreach (Entity e in heroes)
                {
                    e.Dead = false; e.DeadAtMs = 0; e.HitFlashMs = -100000; e.AttackAnimMs = -100000; e.AttackKind = 0; e.CastSkillId = 0; e.Hp = 100;
                }
                long tt = t;
                if (ri == 0) { tt = (t / (260 * 8)) * (260 * 8); }
                if (ri == 1) { tt = (t / (260 * 8)) * (260 * 8) + 260 * 6 - (900 + 0) * 0; }
                foreach (Entity e in heroes)
                {
                    long off = (long)(e.Id & 7) * 3 * 260;           // 영웅마다 프레임 시작이 다르다(Creatures.Draw 와 같은 식)
                    long baseT = tt - (off % (260 * 8));
                    if (ri == 0 || ri == 1) { }
                    if (ri == 2) e.HitFlashMs = tt - 150;
                    if (ri >= 3 && ri <= 5) { e.Dead = true; e.Hp = 0; e.DeadAtMs = tt - (ri == 3 ? 20 : (ri == 4 ? 210 : 800)); }
                    if (ri == 6) { e.AttackKind = 1; e.AttackAnimMs = tt - 260; }
                    if (ri == 7) { e.CastSkillId = 5; e.CastTimeMs = 1000; e.CastStartMs = tt - 650; }
                }
                r.Draw(g, s, pilot, v, tt);
                og.DrawImage(bmp, new Rectangle(0, ri * cropH, cropW, cropH), new Rectangle(0, v.Height / 2 - 195, cropW, cropH), GraphicsUnit.Pixel);
            }
            outb.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved " + outPath);
        }
        return 0;
    }

    /// <summary>몬스터 피격/사망 자세 시트: 서 있기, 피격, 사망 0~2 단계를 몬스터 여러 종류로 한 장에 그린다.</summary>
    private static int MonPoses(string dataDir, string outPath)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now);
        for (int i = 0; i < 60; ++i) { now += 16; s.Update(now); demo.Update(now); }
        ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = 46;
        float cx, cy; s.SelfPos(now, out cx, out cy); v.CamX = cx; v.CamY = cy;
        uint[] tpls = { 1, 2, 3, 4, 9, 12, 14 };
        string envT = Environment.GetEnvironmentVariable("MONTPLS");       // 예: MONTPLS=5,6,10 으로 다른 몬스터를 본다
        if (!string.IsNullOrEmpty(envT)) { string[] ps = envT.Split(','); tpls = new uint[ps.Length]; for (int i = 0; i < ps.Length; ++i) tpls[i] = uint.Parse(ps[i]); }
        System.Collections.Generic.List<Entity> mons = new System.Collections.Generic.List<Entity>();
        for (int i = 0; i < tpls.Length; ++i)
        {
            Entity e = new Entity(); e.Id = 800 + (uint)i; e.Type = (byte)EntityType.Monster; e.TemplateId = tpls[i]; e.Hp = e.MaxHp = 100;
            e.X = e.DestX = cx + (i - 3) * 3.6f; e.Y = e.DestY = cy; e.FaceX = 0.4f; e.FaceY = 1f; e.MoveStartMs = now;
            s.World.Entities[e.Id] = e; mons.Add(e);
        }
        int rows = 8, cropW = 1280, cropH = 230;
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        using (Bitmap outb = new Bitmap(cropW, cropH * rows, PixelFormat.Format32bppArgb))
        using (Graphics og = Graphics.FromImage(outb))
        {
            for (int ri = 0; ri < rows; ++ri)
            {
                long tt = now + 100000;
                foreach (Entity e in mons)
                {
                    e.Dead = false; e.DeadAtMs = 0; e.HitFlashMs = -100000; e.AttackAnimMs = -100000; e.Hp = 100;
                    if (ri == 1) e.HitFlashMs = tt - 120;
                    e.Moving = false; e.Speed = 0f;
                    if (ri >= 5)       // 걷는 중: 걸음 시작 후 0.3/0.7/1.1 초
                    {
                        float bx0 = e.X; e.Moving = true; e.Speed = 2f; e.DestX = e.X + 40f; e.DestY = e.Y; e.MoveStartMs = tt - (ri == 5 ? 300 : (ri == 6 ? 700 : 1100));
                        e.X = bx0; e.FaceX = 1f; e.FaceY = 0f;
                    }
                    if (ri >= 2 && ri < 5) { e.Dead = true; e.Hp = 0; e.DeadAtMs = tt - (ri == 2 ? 20 : (ri == 3 ? 210 : 800)); }
                }
                r.Draw(g, s, pilot, v, tt);
                og.DrawImage(bmp, new Rectangle(0, ri * cropH, cropW, cropH), new Rectangle(0, v.Height / 2 - 150, cropW, cropH), GraphicsUnit.Pixel);
            }
            outb.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved " + outPath);
        }
        return 0;
    }

    /// <summary>긴박감 연출 장면: 저체력 맥동 + 화면 밖 위협 화살표 + 연속 처치 알림 + 보스 경고 배너.</summary>
    private static int TensionScene(string dataDir, string outPath)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
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

    /// <summary>시작 화면: 판정 영역, 진행률 보고, 모든 상태/해상도에서 그려지는지.</summary>
    private static void StartTests(GameData data)
    {
        // 진행률: 0..1 로 오르기만 하고(후퇴 없음) 마지막은 1
        float last = -1f; bool mono = true; int calls = 0; float end = -1f;
        System.Collections.Generic.List<string> warn = new System.Collections.Generic.List<string>();
        AssetSprites.Load(AssetsDir(), warn, delegate (float r, string label) { ++calls; if (r < last - 1e-6f || r < 0f || r > 1f) mono = false; last = r; end = r; });
        Check(mono && calls >= 2 && Math.Abs(end - 1f) < 1e-6f, "start: 로드 진행률이 단조 증가하고 1 로 끝남 (호출 " + calls + ", 끝 " + end + ")");
        int n = data.HeroIds().Count;
        int[][] sizes = { new int[] { 640, 480 }, new int[] { 1280, 800 }, new int[] { 1920, 1080 }, new int[] { 3840, 2160 } };
        using (Resources res = new Resources())
        using (StartScreen ss = new StartScreen(res))
            foreach (int[] sz in sizes)
            {
                int w = sz[0], h = sz[1];
                bool ok = true; string why = "";
                for (int i = 0; i < n; ++i)
                {
                    RectangleF c = StartScreen.CardRect(w, h, n, i);
                    if (c.Left < 0 || c.Right > w || c.Top < 0 || c.Bottom > h) { ok = false; why += " 카드" + i + "가 화면 밖 " + c; }
                    if (i > 0 && c.Left < StartScreen.CardRect(w, h, n, i - 1).Right) { ok = false; why += " 카드" + i + "가 앞 카드와 겹침"; }
                    if (StartScreen.CardAt(w, h, n, c.X + c.Width / 2, c.Y + c.Height / 2) != i) { ok = false; why += " 카드" + i + " 판정이 다름"; }
                    if (c.IntersectsWith(StartScreen.StartRect(w, h))) { ok = false; why += " 카드" + i + "가 시작 버튼과 겹침 " + c + " / " + StartScreen.StartRect(w, h); }
                }
                RectangleF b = StartScreen.StartRect(w, h);
                if (b.Left < 0 || b.Right > w || b.Bottom > h) { ok = false; why += " 시작 버튼이 화면 밖"; }
                Check(ok, "start: 카드/버튼 영역이 화면 안이고 겹치지 않음 (" + w + "x" + h + ")" + why);
                bool ok2 = true;
                for (int si = 0; si < StartScreen.ServerRows(w, h); ++si)
                {
                    RectangleF sr = StartScreen.ServerRect(w, h, si);
                    if (sr.Left < 0 || sr.Right > b.Left || sr.Bottom > h) ok2 = false;
                    if (StartScreen.ServerAt(w, h, StartScreen.MaxServerRows, sr.X + 3, sr.Y + 3) != si) ok2 = false;
                    for (int zi2 = 0; zi2 < ZoneTheme.Zones.Length; ++zi2) if (sr.IntersectsWith(StartScreen.ZoneRect(w, h, zi2))) ok2 = false;
                }
                Check(ok2, "start: 서버 칸이 화면 안이고 시작 버튼/맵 칸과 겹치지 않음 (" + w + "x" + h + ")");
                bool ok3 = true;
                for (int zi2 = 0; zi2 < ZoneTheme.Zones.Length; ++zi2) { RectangleF zr = StartScreen.ZoneRect(w, h, zi2); if (zr.Right > w || zr.IntersectsWith(b)) ok3 = false; }
                Check(ok3, "start: 맵 칸이 화면 안이고 시작 버튼과 겹치지 않음 (" + w + "x" + h + ")");
                bool ok4 = true;
                for (int ci = 0; ci < n; ++ci)
                {
                    int sk = Math.Min(StartScreen.MaxCardSkills, data.SkillsOf(data.HeroIds()[ci]).Count);
                    RectangleF cr = StartScreen.CardRect(w, h, n, ci);
                    for (int ki = 0; ki < sk; ++ki)
                    {
                        RectangleF ir = StartScreen.SkillIconRect(w, h, n, ci, ki, sk);
                        int hc, hk;
                        if (ir.Left < cr.Left || ir.Right > cr.Right || ir.Bottom > cr.Bottom || ir.Width < 14f) ok4 = false;
                        if (!StartScreen.SkillIconAt(w, h, data, ir.X + ir.Width / 2, ir.Y + ir.Height / 2, out hc, out hk) || hc != ci || hk != ki) ok4 = false;
                    }
                }
                Check(ok4, "start: 영웅 카드의 스킬 아이콘이 카드 안이고 판정이 맞음 (" + w + "x" + h + ")");
                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    try
                    {
                        foreach (float p in new float[] { 0f, 0.5f, 1f }) ss.DrawLoading(g, w, h, p, "warrior", 1234);
                        for (int sel = 0; sel < Math.Max(1, n); ++sel) ss.DrawSelect(g, w, h, data, sel, sel == 1 ? -2 : sel, "서버", 5000 + sel * 100);
                        ss.Servers = new ServerPanel(); ss.Servers.List.Add(new GameViewer.Core.Net.ServerEntry("서버 1", "127.0.0.1", 1)); ss.Servers.List.Add(new GameViewer.Core.Net.ServerEntry("서버 2", "127.0.0.1", 2));
                        ss.HoverSkillCard = 0; ss.HoverSkill = 0; ss.HoverServer = 1;
                        ss.DrawSelect(g, w, h, data, 0, 0, "서버", 6000, 2);
                        ss.Servers = null; ss.HoverSkillCard = -1; ss.HoverSkill = -1; ss.HoverServer = -1;
                    }
                    catch (Exception e) { Check(false, "start: 그리기 예외 " + e.Message); }
                }
            }
    }

    private static int StartShots(string dataDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        GameData data = GameData.Load(dataDir, 1);
        int w = 1280, h = 800;
        using (Resources res = new Resources())
        using (StartScreen ss = new StartScreen(res))
        {
            float[] ps = { 0.05f, 0.45f, 1f };
            foreach (float p in ps)
                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    ss.DrawLoading(g, w, h, p, p < 0.3f ? "warrior" : "demon", 900);
                    bmp.Save(Path.Combine(outDir, "load_" + (int)(p * 100) + ".png"), ImageFormat.Png);
                }
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                ss.DrawSelect(g, w, h, data, 1, 2, "서버 127.0.0.1:7000   존 2", 560, 2);
                bmp.Save(Path.Combine(outDir, "select.png"), ImageFormat.Png);
            }
            // 서버 선택 + 영웅 미리보기(스킬 아이콘 툴팁). 서버 접속 측정은 실제로 한다(로컬 리스너 한 개 + 닫힌 포트 한 개).
            System.Net.Sockets.TcpListener lst = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); lst.Start();
            int okPort = ((System.Net.IPEndPoint)lst.LocalEndpoint).Port;
            System.Threading.Thread acc = new System.Threading.Thread(delegate () { try { while (true) lst.AcceptTcpClient().Close(); } catch { } }); acc.IsBackground = true; acc.Start();
            System.Net.Sockets.TcpListener dead = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); dead.Start(); int deadPort = ((System.Net.IPEndPoint)dead.LocalEndpoint).Port; dead.Stop();
            ServerPanel sp = new ServerPanel();
            sp.List.Add(new GameViewer.Core.Net.ServerEntry("서울 1서버", "127.0.0.1", okPort));
            sp.List.Add(new GameViewer.Core.Net.ServerEntry("서울 2서버", "127.0.0.1", deadPort));
            sp.List.Add(new GameViewer.Core.Net.ServerEntry("테스트 서버", "127.0.0.1", okPort));
            sp.Probe = new GameViewer.Core.Net.ServerProbe(sp.List); sp.Selected = 0;
            sp.Probe.Tick(0);
            for (int i = 0; i < 60 && (sp.Probe.Get(0).State == GameViewer.Core.Net.ProbeState.Probing || sp.Probe.Get(1).State == GameViewer.Core.Net.ProbeState.Probing); ++i) System.Threading.Thread.Sleep(50);
            ss.Servers = sp; ss.HoverServer = 2; ss.HoverSkillCard = Math.Min(1, data.HeroIds().Count - 1); ss.HoverSkill = 1;
            foreach (int[] sz in new int[][] { new int[] { 1280, 800 }, new int[] { 900, 620 } })
                using (Bitmap bmp = new Bitmap(sz[0], sz[1], PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    ss.DrawSelect(g, sz[0], sz[1], data, 1, 1, "서버 127.0.0.1:" + okPort + "   존 1", 560, 1);
                    bmp.Save(Path.Combine(outDir, "select_servers_" + sz[0] + ".png"), ImageFormat.Png);
                }
            lst.Stop();
        }
        Console.WriteLine("saved " + outDir);
        return 0;
    }

    private static int SelfTest(string dataDir)
    {
        StartTests(GameData.Load(dataDir, 1));
        // 1) 영웅 3종 + 몬스터 10종 + 기본형: 걷기/대기/공격 프레임이 모두 그려지고 시체/번쩍임도 그려진다
        using (SpriteCache cache = new SpriteCache(2000))
        {
            CreatureRenderer cr = new CreatureRenderer(cache);
            Color bg = Color.FromArgb(74, 118, 56);
            ViewState v = new ViewState(); v.Scale = 24;
            for (int pass = 0; pass < 2; ++pass)
            {
                uint[] tpls = pass == 0 ? new uint[] { 1, 2, 5, 11, 101 } : new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 101, 105, 114 };
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
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
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
                for (int i = 0; i < 62; ++i) { now += 16; s.Update(now); demo.Update(now); pilot.Update(now);
                    foreach (Entity be in s.World.Entities.Values) if (be.IsBoss && !be.Dead && pilot.TargetId != be.Id) pilot.ChaseTarget(be.Id); }
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
                vv.CamX = 900; vv.CamY = 900; r.Draw(g, s, pilot, vv, now);          // 앞 장면에서 미리 만들던 바닥이 남지 않게 멀리서 한 번 새로 그린다
                vv.CamX = 500; vv.CamY = 500; r.Draw(g, s, pilot, vv, now);
                int b0 = r.GroundRebuilds;
                for (int i = 0; i < 40; ++i) { vv.CamX += 0.15f; r.Draw(g, s, pilot, vv, now + i * 16); }
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

    /// <summary>
    /// 라이브 UI 장면용 환경변수: SNAP_MAP=1(월드맵; SNAP_MAPZOOM=배율, SNAP_MAPCAM=x,y), SNAP_SETTINGS=1(SNAP_SETIDX=줄), SNAP_LOG=0..3(탭 로그 창),
    /// SNAP_INTRO=경과ms(존 입장 연출), SNAP_QUALITY=0..2, SNAP_FX=1(콤보/상태 효과/피격 번쩍임), SNAP_NOFPS=1.
    /// </summary>
    private static GameViewer.Core.Logic.BindAction _snapSwap;

    private static void ApplyLiveEnv(ViewerSession s, ViewState v, long now)
    {
        System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
        v.MapOpen = Environment.GetEnvironmentVariable("SNAP_MAP") == "1";
        string z = Environment.GetEnvironmentVariable("SNAP_MAPZOOM"); if (!string.IsNullOrEmpty(z)) v.MapZoom = float.Parse(z, ci);
        string mc = Environment.GetEnvironmentVariable("SNAP_MAPCAM"); if (!string.IsNullOrEmpty(mc)) { string[] p = mc.Split(','); v.MapCx = float.Parse(p[0], ci); v.MapCy = float.Parse(p[1], ci); }
        v.SettingsOpen = Environment.GetEnvironmentVariable("SNAP_SETTINGS") == "1";
        string si = Environment.GetEnvironmentVariable("SNAP_SETIDX"); if (!string.IsNullOrEmpty(si)) v.SettingsIndex = int.Parse(si);
        string lg = Environment.GetEnvironmentVariable("SNAP_LOG"); if (!string.IsNullOrEmpty(lg)) { v.LogMode = 1; v.LogTabSel = (GameViewer.Core.Logic.LogTab)int.Parse(lg); }
        string intro = Environment.GetEnvironmentVariable("SNAP_INTRO"); if (!string.IsNullOrEmpty(intro)) { v.IntroStartMs = now - long.Parse(intro); v.IntroBanner = true; }
        if (Environment.GetEnvironmentVariable("SNAP_KEYS") == "1")
        {
            v.SettingsOpen = true; v.KeysOpen = true; v.KeysIndex = int.Parse(Environment.GetEnvironmentVariable("SNAP_KEYIDX") ?? "2");
            v.KeysCapture = Environment.GetEnvironmentVariable("SNAP_KEYCAP") == "1";
            v.Settings.Keys.Set(GameViewer.Core.Logic.BindAction.BasicAttack, 90, out _snapSwap);
            v.Settings.Keys.Set(GameViewer.Core.Logic.BindAction.Skill2, 69, out _snapSwap);
            v.KeysMsg = "E 키는 '스킬 3' 이 쓰고 있어 서로 맞바꿨습니다 (스킬 3 = 2)";
        }
        string tipn = Environment.GetEnvironmentVariable("SNAP_TIP");
        if (!string.IsNullOrEmpty(tipn))
        {
            RectangleF tr = HudRenderer.SkillSlotRect(v, s.MySkills.Count, int.Parse(tipn));
            v.HasMouse = true; v.MouseX = (int)(tr.X + tr.Width / 2); v.MouseY = (int)(tr.Y + tr.Height / 2);
        }
        if (Environment.GetEnvironmentVariable("SNAP_BGM") == "1") v.BgmLabel = "숲 (전투)";
        string fbp = Environment.GetEnvironmentVariable("SNAP_FEEDBACK"); if (!string.IsNullOrEmpty(fbp)) v.Settings.FeedbackPercent = int.Parse(fbp);
        string q = Environment.GetEnvironmentVariable("SNAP_QUALITY"); if (!string.IsNullOrEmpty(q)) v.Settings.Quality = (GameViewer.Core.Logic.GfxQuality)int.Parse(q);
        if (Environment.GetEnvironmentVariable("SNAP_NOFPS") == "1") v.Settings.ShowFps = false;
        if (Environment.GetEnvironmentVariable("SNAP_FX") == "1" && s.World.Self != null)
        {
            if (s.World.Combo.Active(now) < 2) for (int i = 0; i < 14; ++i) s.World.Combo.Hit(now - 900 + i * 50, 120 + i * 13);
            Entity me = s.World.Self;
            s.World.SelfHitMs = now - 70; s.World.SelfHitRatio = 0.3f; me.HitDirX = -1f; me.HitDirY = 0.2f;
            uint[] ids = { 3, 1, 4 };
            foreach (uint id in ids)
            {
                StatusInstance st = new StatusInstance(); st.StatusId = id; st.ExpireMs = now + 2000 + id * 1500;
                me.Statuses[id] = st;
            }
            Entity tgt = s.World.Find(v.SelectedId);
            if (tgt != null) { StatusInstance st2 = new StatusInstance(); st2.StatusId = 1; st2.ExpireMs = now + 3200; tgt.Statuses[1] = st2; StatusInstance st3 = new StatusInstance(); st3.StatusId = 4; st3.ExpireMs = now + 1500; tgt.Statuses[4] = st3; }
        }
    }

    /// <summary>--conn outDir: 접속/재접속 오버레이를 상태별로 찍는다(서버 없이 상태만 만든다).</summary>
    private static int ConnShots(string dataDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        System.Reflection.PropertyInfo st = typeof(ViewerSession).GetProperty("State");
        System.Reflection.PropertyInfo tx = typeof(ViewerSession).GetProperty("StatusText");
        long now = 100000;
        using (SceneRenderer r = new SceneRenderer())
        {
            string[] names = { "1_connecting", "2_logging_in", "3_failed_retry", "4_gave_up", "5_reconnecting_world", "6_disconnected_retry_world" };
            for (int i = 0; i < names.Length; ++i)
            {
                ViewerSession s = new ViewerSession(cfg, data);
                s.AutoReconnect = true;
                if (i >= 4)
                {
                    DemoDirector d = new DemoDirector(s); d.Start(now);
                    for (int k = 0; k < 60; ++k) { now += 16; s.Update(now); d.Update(now); }
                }
                SessionState state; string text;
                switch (i)
                {
                    case 0: state = SessionState.Connecting; text = "127.0.0.1:7000 접속 중..."; break;
                    case 1: state = SessionState.LoggingIn; text = "로그인 중..."; break;
                    case 2: state = SessionState.Failed; text = "접속 실패: connect timeout (127.0.0.1:7000)"; break;
                    case 3: state = SessionState.Disconnected; text = "연결 끊김: server closed the connection"; break;
                    case 4: state = SessionState.Entering; text = "존 1 입장 중..."; break;
                    default: state = SessionState.Disconnected; text = "연결 끊김: server closed the connection"; break;
                }
                st.GetSetMethod(true).Invoke(s, new object[] { state });
                tx.GetSetMethod(true).Invoke(s, new object[] { text });
                typeof(ViewerSession).GetField("ConnectStartedMs").SetValue(s, now - 2300);
                if (i == 2 || i == 5) { s.Reconnect.OnFailure(now - 400); s.Reconnect.OnFailure(now); }
                if (i == 3) { for (int k = 0; k < 6; ++k) s.Reconnect.OnFailure(now); s.Reconnect.TryTake(now + 999999); }
                if (i == 5) { }
                ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = 24;
                using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    r.Draw(g, s, new AutoPilot(s), v, now + 600);
                    string path = Path.Combine(outDir, "conn_" + names[i] + ".png");
                    bmp.Save(path, ImageFormat.Png); Console.WriteLine("saved " + path);
                }
                s.Dispose();
            }
        }
        return 0;
    }

    /// <summary>SNAP_ZONE=2 로 사막 존을 그려 본다.</summary>
    private static uint EnvZone() { uint z; return uint.TryParse(Environment.GetEnvironmentVariable("SNAP_ZONE"), out z) && z > 0 ? z : 1u; }

    private static int Bench(string dataDir, int frames)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
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

    /// <summary>보스 연출 시험: 등장/변신/사망의 여러 순간을 한 장에 모은다. SNAP_ZONE 로 지역을 고른다.</summary>
    private static int BossShots(string dataDir, string outPng)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now);
        for (int i = 0; i < 80; ++i) { now += 16; s.Update(now); demo.Update(now); }
        Entity boss = null;
        foreach (Entity e in s.World.Entities.Values) if (e.IsBoss && !e.Dead) boss = e;
        if (boss == null) { Console.WriteLine("no boss"); return 1; }
        float bx, by; boss.PosAt(now, out bx, out by);
        string[] labels = { "entrance 0.25", "entrance 0.55", "entrance 0.85", "phase 0.2", "phase 0.45", "phase 0.75", "death 0.12", "death 0.35", "death 0.7" };
        string tplEnv = Environment.GetEnvironmentVariable("SNAP_BOSSTPL");      // 지정하면 그 몬스터 템플릿으로 보스를 그리고, 아래 세 칸은 착지 직후/균열/평상시로 바꾼다
        bool landMode = !string.IsNullOrEmpty(tplEnv);
        if (landMode) { boss.TemplateId = uint.Parse(tplEnv); labels[6] = "landing +150ms"; labels[7] = "cracks +550ms"; labels[8] = "idle (rune)"; }
        int W = 640, Hh = 400;
        using (Bitmap sheet = new Bitmap(W * 3, Hh * 3))
        using (Graphics sg = Graphics.FromImage(sheet))
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(1280, 800, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            for (int k = 0; k < 9; ++k)
            {
                ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = cfg.PixelsPerUnit * 1.2f; v.CamX = bx; v.CamY = by + 2f;
                long t = now + 5000 + k * 10000;
                s.World.Effects.RemoveAll(delegate (ViewEffect f) { return f.Kind == EffectKind.BossDeath; });
                boss.Dead = false; boss.Hp = boss.MaxHp; boss.AppearMs = -100000; boss.PhaseChangedMs = -100000; boss.BossPhase = 2; boss.Moving = false;
                boss.X = bx; boss.Y = by; boss.StopAt(bx, by);
                if (k < 3) boss.AppearMs = t - (long)(new float[] { 0.25f, 0.55f, 0.85f }[k] * BossFx.EntranceMs);
                else if (k < 6) boss.PhaseChangedMs = t - (long)(new float[] { 0.2f, 0.45f, 0.75f }[k - 3] * BossFx.PhaseMs);
                else if (landMode) { if (k == 6) boss.AppearMs = t - (BossFx.LandMs + 150); else if (k == 7) boss.AppearMs = t - (BossFx.LandMs + 550); }
                else
                {
                    float p = new float[] { 0.12f, 0.35f, 0.7f }[k - 6];
                    ViewEffect fx = new ViewEffect(); fx.Kind = EffectKind.BossDeath; fx.StartMs = t - (long)(p * BossFx.DeathMs); fx.DurMs = BossFx.DeathMs; fx.X = bx; fx.Y = by; fx.EntityId = boss.Id;
                    s.World.Effects.Add(fx);
                    if (p > 0.12f) { boss.Dead = true; boss.DeadAtMs = fx.StartMs; }
                }
                r.Draw(g, s, pilot, v, t);
                sg.DrawImage(bmp, new Rectangle((k % 3) * W, (k / 3) * Hh, W, Hh));
                sg.DrawString(labels[k], new Font("Sans", 14, FontStyle.Bold), Brushes.White, (k % 3) * W + 8, (k / 3) * Hh + 8);
            }
            sheet.Save(outPng, ImageFormat.Png);
        }
        Console.WriteLine("saved " + outPng);
        return 0;
    }

    /// <summary>존 관문 통과 순서를 GameCanvas 없이 따라간다: 관문 앞 -> 어두워지는 중 -> 건너편 존 도착(입장 연출). 세 장을 prefix0~2.png 로 저장한다.</summary>
    private static int GateTravelShots(string dataDir, string prefix)
    {
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = 1;
        GameData data = GameData.Load(cfg.DataDir, 1);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        long now = 100000; demo.Start(now);
        for (int i = 0; i < 30; ++i) { now += 16; s.Update(now); demo.Update(now); }
        ZoneGate east = null; foreach (ZoneGate gt in data.Gates) if (gt.Edge == 'E') east = gt;
        if (east == null) { Console.WriteLine("no east gate"); return 1; }
        Entity me = s.World.Self;
        me.X = data.WorldMaxX - 6f; me.Y = east.Pos; me.Moving = false; me.StopAt(me.X, me.Y);
        GateTravel gate = new GateTravel(); AutoPilot pilot = new AutoPilot(s);
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(1280, 800, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = cfg.PixelsPerUnit * 1.3f; v.CamX = me.X - 6f; v.CamY = me.Y;
            for (int k = 0; k < 4; ++k) r.Draw(g, s, pilot, v, now + k * 16);
            bmp.Save(prefix + "0.png", ImageFormat.Png);
            me.X = data.WorldMaxX - 2f; me.StopAt(me.X, me.Y);
            GateStep st = gate.Update(now, data.Gates, me.X, me.Y, data.WorldMinX, data.WorldMinY, data.WorldMaxX, data.WorldMaxY, true);
            if (st != GateStep.Started) { Console.WriteLine("gate did not start: " + st); return 1; }
            long t = now + (long)(GateTravel.FadeOutMs * 0.75f);
            r.Draw(g, s, pilot, v, t);
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * gate.Fade(t)), 0, 0, 0))) g.FillRectangle(b, 0, 0, 1280, 800);
            bmp.Save(prefix + "1.png", ImageFormat.Png);
            t = now + GateTravel.FadeOutMs;
            if (gate.Update(t, data.Gates, me.X, me.Y, data.WorldMinX, data.WorldMinY, data.WorldMaxX, data.WorldMaxY, true) != GateStep.Switch) { Console.WriteLine("no switch"); return 1; }
            ZoneGate to = gate.Pending;
            ViewerConfig c2 = new ViewerConfig(); c2.DataDir = dataDir; c2.HeroId = 1; c2.ZoneId = to.TargetZone;
            GameData d2 = GameData.Load(dataDir, to.TargetZone);
            ViewerSession s2 = new ViewerSession(c2, d2); DemoDirector dm2 = new DemoDirector(s2); dm2.SetArrival(to.TargetX, to.TargetY);
            long n2 = t + 100; dm2.Start(n2);
            for (int i = 0; i < 40; ++i) { n2 += 16; s2.Update(n2); dm2.Update(n2); }
            gate.Arrived(n2);
            Entity m2 = s2.World.Self;
            ViewState v2 = new ViewState(); v2.Width = 1280; v2.Height = 800; v2.Scale = cfg.PixelsPerUnit * 1.3f; v2.CamX = m2.X + 6f; v2.CamY = m2.Y;
            v2.IntroStartMs = s2.InZoneSinceMs; v2.IntroBanner = true;
            AutoPilot p2 = new AutoPilot(s2);
            r.Draw(g, s2, p2, v2, n2);
            bmp.Save(prefix + "2.png", ImageFormat.Png);
            Console.WriteLine("hero arrived at " + m2.X.ToString("0.0") + "," + m2.Y.ToString("0.0") + " zone " + s2.ZoneId + "; cooldown blocks re-entry: " + (gate.Update(n2, d2.Gates, m2.X, m2.Y, d2.WorldMinX, d2.WorldMinY, d2.WorldMaxX, d2.WorldMaxY, true) == GateStep.None));
        }
        return 0;
    }

    /// <summary>메모리 구성 점검: 단계마다 프로세스 메모리를 찍는다(시작 / 에셋 로드 / 첫 그리기 / 구동 후).</summary>
    private static int MemProbe(string dataDir)
    {
        Console.WriteLine("start rss=" + RssMb().ToString("0") + "MB");
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        Console.WriteLine("assets loaded rss=" + RssMb().ToString("0") + "MB  (AssetSprites sets are read here; see Count below)");
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s); AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now); pilot.SetAuto(true);
        ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = cfg.PixelsPerUnit;
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            Console.WriteLine("renderer created rss=" + RssMb().ToString("0") + "MB");
            int[] marks = { 1, 60, 600, 3000, 9000 }; int mi = 0;
            for (int i = 1; i <= 9000 && mi < marks.Length; ++i)
            {
                now += 16; s.Update(now); demo.Update(now); pilot.Update(now);
                float sx, sy; if (s.SelfPos(now, out sx, out sy)) { v.CamX = sx; v.CamY = sy; }
                r.Draw(g, s, pilot, v, now);
                if (i == marks[mi]) { Console.WriteLine("frame " + i + " rss=" + RssMb().ToString("0") + "MB managed=" + (GC.GetTotalMemory(false) / 1048576.0).ToString("0") + "MB"); ++mi; }
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Console.WriteLine("after full GC rss=" + RssMb().ToString("0") + "MB managed=" + (GC.GetTotalMemory(true) / 1048576.0).ToString("0") + "MB");
        }
        return 0;
    }

    private static double RssMb()
    {
        try { foreach (string l in File.ReadAllLines("/proc/self/status")) if (l.StartsWith("VmRSS:")) return long.Parse(l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[1]) / 1024.0; }
        catch (Exception) { }
        return -1;
    }

    /// <summary>장시간 구동 점검: 실제 시계로 N분 동안 데모를 가능한 빨리 그리며 지역을 돌아가고(캐시 교체 확인), 1분마다 프레임 시간/메모리를 찍는다.</summary>
    private static int Soak(string dataDir, double minutes)
    {
        uint[] zones = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        int zi = 0;
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dataDir; cfg.HeroId = 1; cfg.ZoneId = zones[0];
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000; demo.Start(now); pilot.SetAuto(true);
        ViewState v = new ViewState(); v.Width = 1280; v.Height = 800; v.Scale = cfg.PixelsPerUnit;
        System.Diagnostics.Stopwatch wall = System.Diagnostics.Stopwatch.StartNew(), frame = new System.Diagnostics.Stopwatch();
        long nextLog = 60000, nextZone = 150000;
        long frames = 0, winFrames = 0; double winTotal = 0, winWorst = 0; int over33 = 0, winOver33 = 0;
        System.Collections.Generic.List<double> rss = new System.Collections.Generic.List<double>(), man = new System.Collections.Generic.List<double>();
        using (SceneRenderer r = new SceneRenderer())
        using (Bitmap bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            while (wall.Elapsed.TotalMinutes < minutes)
            {
                now += 16; s.Update(now); demo.Update(now); pilot.Update(now);
                float sx, sy; if (s.SelfPos(now, out sx, out sy)) { v.CamX = sx; v.CamY = sy; }
                v.SelectedId = pilot.TargetId;
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                frame.Restart(); r.Draw(g, s, pilot, v, now); frame.Stop();
                double ms = frame.Elapsed.TotalMilliseconds; ++frames;
                if (ms > 80) Console.WriteLine("  hitch " + ms.ToString("0") + "ms zone=" + zones[zi] + " frame=" + frames + " gc0+" + (GC.CollectionCount(0) - g0) + " gc1+" + (GC.CollectionCount(1) - g1) + " gc2+" + (GC.CollectionCount(2) - g2) + " fx=" + s.World.Effects.Count + " ent=" + s.World.Entities.Count); ++winFrames; winTotal += ms; if (ms > winWorst) winWorst = ms; if (ms > 33.3) { ++over33; ++winOver33; }
                if (wall.ElapsedMilliseconds >= nextZone)
                {
                    nextZone += 150000; zi = (zi + 1) % zones.Length;
                    cfg.ZoneId = zones[zi]; data = GameData.Load(cfg.DataDir, cfg.ZoneId);
                    s = new ViewerSession(cfg, data); demo = new DemoDirector(s); pilot = new AutoPilot(s); demo.Start(now); pilot.SetAuto(true);
                }
                if (wall.ElapsedMilliseconds >= nextLog)
                {
                    nextLog += 60000;
                    double mb = GC.GetTotalMemory(false) / 1048576.0, rs = RssMb();
                    rss.Add(rs); man.Add(mb);
                    Console.WriteLine("t=" + (wall.Elapsed.TotalMinutes).ToString("0.0") + "min zone=" + zones[zi] + " sim=" + ((now - 100000) / 60000.0).ToString("0.0") + "min frames=" + winFrames + " avg=" + (winTotal / winFrames).ToString("0.00") + "ms worst=" + winWorst.ToString("0.0") + "ms >33ms=" + winOver33 + " managed=" + mb.ToString("0") + "MB rss=" + rs.ToString("0") + "MB gen2=" + GC.CollectionCount(2) + " ent=" + s.World.Entities.Count + " fx=" + s.World.Effects.Count);
                    { string pm = "  parts(ms/frame)"; for (int q = 0; q < r.PartMs.Length; ++q) { pm += " " + SceneRenderer.PartNames[q] + "=" + (r.PartMs[q] / winFrames).ToString("0.00"); r.PartMs[q] = 0; } Console.WriteLine(pm); string sm = "  scene(ms/frame)"; for (int q = 0; q < r.SceneSubMs.Length; ++q) { sm += " " + WorldRenderer.SubNames[q] + "=" + (r.SceneSubMs[q] / winFrames).ToString("0.00"); r.SceneSubMs[q] = 0; } Console.WriteLine(sm); string fm = "  fxAir by kind(ms/frame)"; for (int q = 0; q < r.EffectKindMs.Length; ++q) { if (r.EffectKindMs[q] > 0) fm += " " + (q == 23 ? "floatingText" : ((GameViewer.Core.World.EffectKind)q).ToString()) + "=" + (r.EffectKindMs[q] / winFrames).ToString("0.00"); r.EffectKindMs[q] = 0; } Console.WriteLine(fm); }
                    winFrames = 0; winTotal = 0; winWorst = 0; winOver33 = 0;
                }
            }
        }
        Console.WriteLine("total frames=" + frames + " (>33ms: " + over33 + ")");
        if (rss.Count >= 4)
        {
            int h = rss.Count / 2; double a1 = 0, a2 = 0; for (int i = 0; i < h; ++i) a1 += rss[i]; for (int i = rss.Count - h; i < rss.Count; ++i) a2 += rss[i];
            Console.WriteLine("rss first-half avg=" + (a1 / h).ToString("0") + "MB last-half avg=" + (a2 / h).ToString("0") + "MB  max=" + System.Linq.Enumerable.Max(rss).ToString("0") + "MB");
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

    /// <summary>외곽선 글자 캐시가 직접 그린 결과와 거의 같은 자리·크기에 찍히는지(정렬 3종 x 배율 3종), 회전은 직접 그리기로 빠지는지.</summary>
    private static void TextCacheTests()
    {
        using (Resources res = new Resources())
        {
            StringFormat[] fmts = { res.Center, new StringFormat(), new StringFormat() };
            fmts[2].Alignment = StringAlignment.Far;
            float[] scales = { 1f, 1.5f, 2f };
            string[] names = { "center", "near", "far" };
            for (int fi = 0; fi < fmts.Length; ++fi)
                for (int si = 0; si < scales.Length; ++si)
                {
                    int[] box = new int[8]; int[] cnt = new int[2];
                    for (int mode = 0; mode < 2; ++mode)
                    {
                        res.TextCacheOn = mode == 0;
                        using (Bitmap bmp = new Bitmap(320, 160, PixelFormat.Format32bppArgb))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(60, 90, 50));
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.TranslateTransform(160, 80); g.ScaleTransform(scales[si], scales[si]);
                            res.OutlinedText(g, "폭탄 해골 126/150", res.Bold, Color.White, 0f, 0f, fmts[fi]);
                            int x0 = 999, y0 = 999, x1 = -1, y1 = -1, n = 0;
                            Color bgc = Color.FromArgb(60, 90, 50);
                            for (int y = 0; y < bmp.Height; ++y) for (int x = 0; x < bmp.Width; ++x)
                            {
                                Color c = bmp.GetPixel(x, y);
                                if (Math.Abs(c.R - bgc.R) + Math.Abs(c.G - bgc.G) + Math.Abs(c.B - bgc.B) > 60) { ++n; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                            }
                            box[mode * 4] = x0; box[mode * 4 + 1] = y0; box[mode * 4 + 2] = x1; box[mode * 4 + 3] = y1; cnt[mode] = n;
                        }
                    }
                    string tag = "text cache " + names[fi] + " x" + scales[si];
                    Check(cnt[0] > 50 && cnt[1] > 50, tag + " draws something");
                    int tol = 3 + (int)scales[si];
                    Check(Math.Abs(box[0] - box[4]) <= tol && Math.Abs(box[1] - box[5]) <= tol && Math.Abs(box[2] - box[6]) <= tol && Math.Abs(box[3] - box[7]) <= tol, tag + " bounding box matches direct drawing (" + box[0] + "," + box[1] + "," + box[2] + "," + box[3] + " vs " + box[4] + "," + box[5] + "," + box[6] + "," + box[7] + ")");
                    Check(Math.Abs(cnt[0] - cnt[1]) <= cnt[1] * 0.25, tag + " ink amount similar (" + cnt[0] + " vs " + cnt[1] + ")");
                }
            // 회전 변환은 직접 그리기로 빠져도 예외 없이 그려진다
            res.TextCacheOn = true;
            using (Bitmap bmp = new Bitmap(200, 100, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TranslateTransform(100, 50); g.RotateTransform(20f);
                res.OutlinedText(g, "rot", res.Bold, Color.White, 0f, 0f, res.Center);
            }
            // 캐시는 상한을 넘지 않는다
            using (Bitmap bmp = new Bitmap(100, 40, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
                for (int i = 0; i < 1500; ++i) { res.TextFrame(); res.OutlinedText(g, "n" + i, res.Small, Color.White, 5f, 5f, null); }
            Check(res.TextCacheCount <= 700, "text cache stays bounded (" + res.TextCacheCount + ")");
        }
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
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = dir; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
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
        if (args.Length >= 2 && args[0] == "--selftest") { AssetTests(); GroundTests(); BackdropTests(); TextCacheTests(); }
        LoadAssets();
        if (args.Length >= 3 && args[0] == "--tension") return TensionScene(args[1], args[2]);
        if (args.Length >= 3 && args[0] == "--conn") return ConnShots(args[1], args[2]);
        if (args.Length >= 3 && args[0] == "--heroscene") return HeroScene(args[1], args[2]);
        if (args.Length >= 3 && args[0] == "--heropose") return HeroPoses(args[1], args[2]);
        if (args.Length >= 3 && args[0] == "--monpose") return MonPoses(args[1], args[2]);
        if (args.Length >= 2 && args[0] == "--herosheet") return HeroSheet(args[1], args.Length > 2 ? double.Parse(args[2]) : 40);
        if (args.Length >= 3 && args[0] == "--views") return Views(args[1], args[2], args.Length > 3 ? double.Parse(args[3]) : 26);
        if (args.Length >= 2 && args[0] == "--icons") return IconSheet(args[1], args.Length > 2 ? args[2] : "GameData");
        if (args.Length >= 3 && args[0] == "--matfx") return MatFxSheet(args[1], args[2]);
        if (args.Length >= 2 && args[0] == "--monfx") return MonFxSheet(args[1], args.Length > 2 ? double.Parse(args[2]) : 24, args.Length > 3 ? float.Parse(args[3]) : 1f, args.Length > 4 ? float.Parse(args[4]) : 0f);
        if (args.Length >= 2 && args[0] == "--atksheet") return AtkSheet(args[1], args.Length > 2 ? double.Parse(args[2]) : 24);
        if (args.Length >= 3 && args[0] == "--start") return StartShots(args[1], args[2]);
        if (args.Length >= 2 && args[0] == "--sheet") return Sheet(args[1], args.Length > 2 ? double.Parse(args[2]) : 28);
        if (args.Length >= 2 && args[0] == "--selftest") return SelfTest(args[1]);
        if (args.Length >= 3 && args[0] == "--gatetravel") return GateTravelShots(args[1], args[2]);
        if (args.Length >= 3 && args[0] == "--bossfx") return BossShots(args[1], args[2]);
        if (args.Length >= 2 && args[0] == "--memprobe") return MemProbe(args[1]);
        if (args.Length >= 2 && args[0] == "--soak") return Soak(args[1], args.Length > 2 ? double.Parse(args[2]) : 10);
        if (args.Length >= 2 && args[0] == "--bench") return Bench(args[1], args.Length > 2 ? int.Parse(args[2]) : 300);
        if (args.Length >= 4 && args[0] == "--atkfx") return AtkFx(args[1], args[2], args[3]);
        if (args.Length >= 3 && args[0] == "--fxsheet") return FxSheet(args[1], args[2], args.Length > 3 ? double.Parse(args[3]) : 0.4);
        if (args.Length < 2) { Console.WriteLine("usage: RenderSnapshot <GameDataDir> <outDir> [seconds...]"); return 2; }
        Directory.CreateDirectory(args[1]);
        ViewerConfig cfg = new ViewerConfig(); cfg.DataDir = args[0]; cfg.HeroId = 1; cfg.ZoneId = EnvZone();
        GameData data = GameData.Load(cfg.DataDir, cfg.ZoneId);
        foreach (string w in data.Warnings) Console.WriteLine("warn: " + w);

        ViewerSession s = new ViewerSession(cfg, data);
        DemoDirector demo = new DemoDirector(s);
        AutoPilot pilot = new AutoPilot(s);
        long now = 100000 + long.Parse(Environment.GetEnvironmentVariable("SNAP_T0") ?? "0");       // 날씨/낮밤 장면을 고르기 위한 시계 오프셋(ms)
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
                ApplyLiveEnv(s, v, now);
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
