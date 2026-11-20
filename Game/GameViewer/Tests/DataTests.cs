using System;
using System.Collections.Generic;
using System.IO;
using GameViewer.Core;
using GameViewer.Core.Data;

namespace GameViewer.Tests
{
    public static class DataTests
    {
        /// <summary>Assets/sprites.json 의 몬스터 template 은 GameData/Monster.json 의 Id 와 같아야 한다(이름과 그림이 어긋나지 않게). 파일을 못 찾으면 건너뛴다.</summary>
        public static void SpritesMatchMonsters()
        {
            string dir = System.AppDomain.CurrentDomain.BaseDirectory, sprites = null, monsters = null;
            for (int i = 0; i < 8 && dir != null; ++i, dir = System.IO.Path.GetDirectoryName(dir))
            {
                string a = System.IO.Path.Combine(dir, "Assets", "sprites.json"), m = System.IO.Path.Combine(dir, "..", "GameData", "Monster.json");
                if (sprites == null && System.IO.File.Exists(a)) sprites = a;
                if (monsters == null && System.IO.File.Exists(m)) monsters = m;
            }
            if (sprites == null || monsters == null) return;
            HashSet<uint> ids = new HashSet<uint>();
            foreach (object r in MiniJson.AsArray(MiniJson.AsObject(MiniJson.Parse(System.IO.File.ReadAllText(monsters)))["Monster"]))
                ids.Add((uint)MiniJson.Num(MiniJson.AsObject(r), "Id", 0));
            HashSet<uint> seen = new HashSet<uint>();
            foreach (object r in MiniJson.AsArray(MiniJson.AsObject(MiniJson.Parse(System.IO.File.ReadAllText(sprites)))["sprites"]))
            {
                Dictionary<string, object> o = MiniJson.AsObject(r);
                if (MiniJson.Str(o, "kind", "") != "monster") continue;
                uint tpl = (uint)MiniJson.Num(o, "template", 0);
                T.Check(ids.Contains(tpl), "sprite template " + tpl + " exists in Monster.json");
                T.Check(seen.Add(tpl), "sprite template " + tpl + " is unique");
            }
            foreach (uint id in ids) T.Check(seen.Contains(id), "monster " + id + " has a sprite");
        }

        public static void MiniJsonBasics()
        {
            Dictionary<string, object> o = MiniJson.AsObject(MiniJson.Parse("﻿{ \"a\": 1.5, \"b\": [1, 2, {\"c\": \"x\\ny\\u00e9\"}], \"t\": true, \"n\": null, \"neg\": -3e2 }"));
            T.Near(1.5, MiniJson.Num(o, "a", 0), 1e-9, "number");
            T.Eq(3, MiniJson.AsArray(o["b"]).Count, "array count");
            T.Eq("x\né".Replace("é", "yé"), MiniJson.Str(MiniJson.AsObject(MiniJson.AsArray(o["b"])[2]), "c", ""), "string escapes");
            T.Check(MiniJson.Bool(o, "t", false), "bool");
            T.Check(o["n"] == null, "null");
            T.Near(-300, MiniJson.Num(o, "neg", 0), 1e-9, "exponent");
            T.Eq("한글", MiniJson.Str(MiniJson.AsObject(MiniJson.Parse("{\"k\":\"한글\"}")), "k", ""), "unicode passthrough");

            string[] bad = { "", "{", "{\"a\":}", "[1,]", "{\"a\":1,}", "{\"a\" 1}", "\"abc", "tru", "{} x" };
            foreach (string b in bad)
            {
                bool threw = false;
                try { MiniJson.Parse(b); } catch (JsonException) { threw = true; }
                T.Check(threw, "rejects: " + b);
            }
        }

        /// <summary>파일 시스템 없이(Unity Resources/어드레서블처럼) 읽어도 폴더에서 읽은 것과 같은 데이터가 나온다.</summary>
        public static void GameDataLoadsWithoutFiles()
        {
            string dir = Program.Dir("GameData");
            int calls = 0;
            GameData d = GameData.Load(delegate (string table)
            {
                ++calls;
                string path = Path.Combine(dir, table + ".json");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }, 1);
            GameData f = GameData.Load(dir, 1);
            T.Check(calls >= 7, "reader is asked for each table (" + calls + ")");
            T.Eq(f.Heroes.Count, d.Heroes.Count, "same hero count");
            T.Eq(f.Monsters.Count, d.Monsters.Count, "same monster count");
            T.Eq(f.Skills.Count, d.Skills.Count, "same skill count");
            T.Eq(f.Blocks.Count, d.Blocks.Count, "same block count");
            T.Eq(f.Warnings.Count, d.Warnings.Count, "same warnings");

            GameData none = GameData.Load(delegate (string table) { return null; }, 1);
            T.Check(none.Warnings.Count > 0 && none.Heroes.Count == 0, "missing tables only warn");
            GameData broken = GameData.Load(delegate (string table) { return "{ not json"; }, 1);
            T.Check(broken.Warnings.Count > 0, "broken json only warns");
            GameData thrown = GameData.Load(delegate (string table) { throw new InvalidOperationException("io"); }, 1);
            T.Check(thrown.Warnings.Count > 0, "reader exception only warns");
        }

        public static void HeroIdsSorted()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            List<uint> ids = d.HeroIds();
            T.Eq(d.Heroes.Count, ids.Count, "all heroes listed");
            for (int i = 1; i < ids.Count; ++i) T.Check(ids[i - 1] < ids[i], "ascending hero ids");
        }

        /// <summary>Core 는 UI/엔진에 의존하지 않아야 어느 호스트에서나 쓸 수 있다. 참조하는 어셈블리로 확인한다.</summary>
        public static void CoreHasNoUiDependency()
        {
            System.Reflection.Assembly core = typeof(GameData).Assembly;
            foreach (System.Reflection.AssemblyName n in core.GetReferencedAssemblies())
            {
                string name = n.Name;
                T.Check(!name.StartsWith("System.Drawing") && !name.StartsWith("System.Windows.Forms") && !name.StartsWith("UnityEngine") && !name.StartsWith("WindowsBase"),
                    "Core must not reference " + name);
            }
        }

        /// <summary>HeroSkill 의 개방 조건(레벨/가격)을 읽는다. 열이 없는 표는 처음부터 쓰는 무료 스킬로 본다.</summary>
        public static void SkillUnlocks()
        {
            GameData plain = GameData.Load(Program.Dir("GameData"), 1);
            SkillUnlockRow def = plain.UnlockOf(1, plain.SkillsOf(1)[0]);
            T.Check(def.UnlockLevel == 1 && def.IsFree, "a table without unlock columns means free from level 1");

            string dir = Path.Combine(Path.GetTempPath(), "gv_unlock_" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(Program.Dir("GameData"), "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
            File.WriteAllText(Path.Combine(dir, "HeroSkill.json"), "{\"HeroSkill\": [{\"HeroId\": 1, \"SkillId\": 1, \"UnlockLevel\": 1, \"PriceCurrencyId\": 0, \"Price\": 0}, {\"HeroId\": 1, \"SkillId\": 2, \"UnlockLevel\": 15, \"PriceCurrencyId\": 1, \"Price\": 3000}]}");
            try
            {
                GameData d = GameData.Load(dir, 1);
                SkillUnlockRow paid = d.UnlockOf(1, 2);
                T.Check(paid.UnlockLevel == 15 && paid.PriceCurrencyId == 1 && paid.Price == 3000 && !paid.IsFree, "a paid skill keeps its level and price");
                T.Check(d.UnlockOf(1, 1).IsFree && d.UnlockOf(1, 1).UnlockLevel == 1, "a free skill is free");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void GameDataLoads()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            T.Eq(0, d.Warnings.Count, "no warnings: " + string.Join("; ", d.Warnings.ToArray()));
            T.Eq(4, d.Heroes.Count, "heroes");
            T.Eq("전사", d.HeroName(1), "hero name (Korean)");
            T.Eq(5, d.Monsters.Count, "monsters");
            T.Eq(14, d.Skills.Count, "skills");
            T.Check(d.Skill(2).IsArea && d.Skill(2).Radius == 4.0, "skill 2 area radius 4");
            T.Check(!d.Skill(1).IsArea, "skill 1 single");
            T.Eq(3, d.SkillsOf(1).Count, "hero 1 skills");
            T.Eq(2, d.SkillsOf(4).Count, "hero 4 (lancer) skills");
            T.Check(d.Skill(18).Shape == "Line" && d.Skill(18).Range >= 4f && d.Skill(19).Shape == "Self" && d.Skill(19).StatusId == 4, "lancer skill 18 is a piercing line, 19 a self-centered sweep with slow");
            T.Check(d.Status(1).IsDot && !d.Status(3).IsDot, "status kinds");
            T.Check(SkillFxTable.For(d, 5).Style == SkillFxStyle.Fireball && SkillFxTable.For(d, 4).Style == SkillFxStyle.Volley, "skill fx styles by id");
            T.Check(SkillFxTable.For(d, 18).Style == SkillFxStyle.Lance && SkillFxTable.For(d, 6).Shake > 0f, "lancer thrust, boss blast shakes");
            T.Check(SkillFxTable.For(d, 9999) != null, "unknown skill still gets a style");
            T.Check(SkillFxTable.For(d, 23).Style == SkillFxStyle.Blade && SkillFxTable.For(d, 27).Shake > 0f && SkillFxTable.For(d, 21).Style == SkillFxStyle.Arrow, "new archer/rogue skills have fx");
            T.Check(SkillFxTable.DotColor(11) != SkillFxTable.DotColor(1) && SkillFxTable.DotColor(2) != SkillFxTable.DotColor(1), "poison/bleed embers differ from burn");
            T.Eq(3, d.Blocks.Count, "blocks for zone 1");
            T.Check(d.Blocks[1].IsCircle && d.Blocks[1].R == 5.0f, "circle block");
            T.Eq(6, d.Spawns.Count, "spawns of zone 1 only (fixture has 7 rows over zones 1 and 2)");
            GameData other = GameData.Load(Program.Dir("GameData"), 99);
            T.Eq(0, other.Blocks.Count, "other zone has no blocks");
            T.Eq("#77", d.SkillName(77), "unknown id fallback name");
            T.Eq(1u, d.ResolveHeroId(0), "hero 0 resolves to lowest id");

            GameData missing = GameData.Load("no_such_dir_xyz", 1);
            T.Check(missing.Warnings.Count > 0, "missing dir warns");
            T.Eq(0, missing.Heroes.Count, "missing dir empty");
        }

        public static void Config()
        {
            string err; bool exit;
            ViewerConfig c = ViewerConfig.FromArgs(new string[] { "--host", "10.0.0.5", "--port", "7100", "--hero", "2", "--auto", "--scale", "9.5" }, out err, out exit);
            T.Check(c != null && err == null, "valid args");
            T.Eq("10.0.0.5", c.Host, "host"); T.Eq(7100, c.Port, "port"); T.Eq(2u, c.HeroId, "hero"); T.Check(c.Auto, "auto"); T.Near(9.5, c.PixelsPerUnit, 1e-9, "scale");

            // 다중 실행: 기본은 슬롯 번호를 CharId 에 더하는 방식, 명시한 ID 나 --single 이면 그대로 쓴다
            c = ViewerConfig.FromArgs(new string[0], out err, out exit);
            T.Check(c != null && c.MultiInstance && c.CharId == 900001UL && c.Slot == 0, "multi instance is the default");
            c = ViewerConfig.FromArgs(new string[] { "--char-id", "123" }, out err, out exit);
            T.Check(c != null && !c.MultiInstance && c.CharId == 123UL, "explicit char id is used as is");
            c = ViewerConfig.FromArgs(new string[] { "--single" }, out err, out exit);
            T.Check(c != null && !c.MultiInstance, "--single");
            T.Check(c.ToJson().Contains("\"MultiInstance\": false"), "MultiInstance is saved");
            // 시작 화면의 영웅 선택: 기본은 보여주고, --skip-select 면 건너뛰며 설정 파일에도 저장된다
            c = ViewerConfig.FromArgs(new string[0], out err, out exit);
            T.Check(c != null && !c.SkipHeroSelect && c.ToJson().Contains("\"SkipHeroSelect\": false"), "hero select is shown by default");
            c = ViewerConfig.FromArgs(new string[] { "--skip-select", "--hero", "3" }, out err, out exit);
            T.Check(c != null && c.SkipHeroSelect && c.HeroId == 3u && c.ToJson().Contains("\"SkipHeroSelect\": true"), "--skip-select");

            c = ViewerConfig.FromArgs(new string[] { "--port", "70000" }, out err, out exit);
            T.Check(c == null && err != null, "port out of range rejected");
            c = ViewerConfig.FromArgs(new string[] { "--bogus" }, out err, out exit);
            T.Check(c == null && err != null, "unknown option rejected");
            c = ViewerConfig.FromArgs(new string[] { "--port" }, out err, out exit);
            T.Check(c == null && err != null, "missing value rejected");

            // dump -> load 왕복 + 명령행 우선
            string tmp = Path.Combine(Path.GetTempPath(), "gv_cfg_test.json");
            ViewerConfig def = new ViewerConfig();
            File.WriteAllText(tmp, def.ToJson().Replace("\"Port\": 7000", "\"Port\": 7777").Replace("\"HeroId\": 1", "\"HeroId\": 3"));
            c = ViewerConfig.FromArgs(new string[] { "--config", tmp, "--hero", "2" }, out err, out exit);
            T.Check(c != null, "config file loads: " + err);
            T.Eq(7777, c.Port, "file value used"); T.Eq(2u, c.HeroId, "command line overrides file");
            File.Delete(tmp);

            c = ViewerConfig.FromArgs(new string[] { "--port=7100", "--char-id", "5" }, out err, out exit);
            T.Check(c != null && c.Port == 7100 && c.CharId == 5UL, "--name=value form");
            c = ViewerConfig.FromArgs(new string[] { "--host", "--port", "7000" }, out err, out exit);
            T.Check(c == null && err != null, "option as a value rejected");
            File.WriteAllText(tmp, "{ // 주석\n \"Port\": 7001, }");
            c = ViewerConfig.FromArgs(new string[] { "--config", tmp }, out err, out exit);
            T.Check(c != null && c.Port == 7001, "config file allows comments and trailing commas: " + err);
            File.WriteAllText(tmp, "{ \"Prot\": 7001 }");
            c = ViewerConfig.FromArgs(new string[] { "--config", tmp }, out err, out exit);
            T.Check(c == null && err != null, "unknown key in config file rejected");
            File.Delete(tmp);

            File.WriteAllText(tmp, "{ \"EntityScale\": 9 }");
            c = ViewerConfig.FromArgs(new string[] { "--config", tmp }, out err, out exit);
            T.Check(c == null && err != null, "EntityScale out of range rejected");
            T.Near(1.15, new ViewerConfig().EntityScale, 1e-9, "default entity scale");
            File.WriteAllText(tmp, "{ \"Port\": 0 }");
            c = ViewerConfig.FromArgs(new string[] { "--config", tmp }, out err, out exit);
            T.Check(c == null, "bad port in file rejected");
            File.Delete(tmp);
        }
    }
}
