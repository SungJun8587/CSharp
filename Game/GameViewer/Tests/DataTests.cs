using System;
using System.Collections.Generic;
using System.IO;
using GameViewer.Core;
using GameViewer.Core.Data;

namespace GameViewer.Tests
{
    public static class DataTests
    {
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

        /// <summary>Core 는 UI/엔진에 의존하지 않아야 Unity 에서 그대로 쓸 수 있다. 참조하는 어셈블리로 확인한다.</summary>
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

        public static void GameDataLoads()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            T.Eq(0, d.Warnings.Count, "no warnings: " + string.Join("; ", d.Warnings.ToArray()));
            T.Eq(4, d.Heroes.Count, "heroes");
            T.Eq("전사", d.HeroName(1), "hero name (Korean)");
            T.Eq(10, d.Monsters.Count, "monsters");
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
            T.Eq(11, d.Spawns.Count, "spawns of zone 1 only (fixture has 14 rows over zones 1 and 2)");
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

            File.WriteAllText(tmp, "{ \"Port\": 0 }");
            c = ViewerConfig.FromArgs(new string[] { "--config", tmp }, out err, out exit);
            T.Check(c == null, "bad port in file rejected");
            File.Delete(tmp);
        }
    }
}
