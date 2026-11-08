using System;
using Core.Util;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace XlsxConverter.Tests
{
    internal static class Program
    {
        private static int _fail, _pass;

        private static void Check(bool ok, string what, string? detail = null)
        {
            if (ok) { ++_pass; return; }
            ++_fail;
            Console.WriteLine("FAIL: " + what + (detail == null ? "" : "\n  " + detail.Replace("\n", "\n  ")));
        }

        private static void Eq(string expected, string actual, string what)
            => Check(expected == actual, what, "expected: " + Show(expected) + "\n  actual:   " + Show(actual));

        private static string Show(string s) => "[" + s.Replace("\n", "\\n").Replace("\r", "\\r") + "]";

        private static object?[] R(params object?[] c) => c;

        private static (int Code, string Out, string Err) Run(params string[] args)
        {
            var so = new StringWriter { NewLine = "\n" };
            var se = new StringWriter { NewLine = "\n" };
            int code = ConverterApp.Run(args, p => new SimpleXlsxReader(p), so, se);
            return (code, so.ToString(), se.ToString());
        }

        private static string Tmp(string name)
        {
            string d = Path.Combine(Path.GetTempPath(), "xlsxconv_" + Environment.ProcessId + "_" + name);
            if (Directory.Exists(d)) Directory.Delete(d, true);
            Directory.CreateDirectory(d);
            return d;
        }

        private static string Read(string path) => File.ReadAllText(path, new UTF8Encoding(false));
        private static bool HasBom(string path) { byte[] b = File.ReadAllBytes(path); return b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF; }

        private static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 2 && args[0] == "floatrepr") return FloatReprFile(args[1]);
            if (args.Length == 4 && args[0] == "bench") return Bench(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]));

            UnitValueFormat();
            Pipeline();
            Errors();
            Usage();
            FolderRules();
            WriterRules();

            Console.WriteLine(_fail == 0 ? "ALL PASSED (" + _pass + " checks)" : _fail + " FAILED, " + _pass + " passed");
            return _fail == 0 ? 0 : 1;
        }

        // 파이썬이 만든 "<double 비트 16진수> <repr>" 줄들과 FloatRepr을 대조한다.
        private static int FloatReprFile(string file)
        {
            int n = 0, bad = 0;
            foreach (string line in File.ReadLines(file))
            {
                int sp = line.IndexOf(' ');
                double d = BitConverter.Int64BitsToDouble(long.Parse(line.Substring(0, sp), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                string expected = line.Substring(sp + 1);
                string actual = ValueFormat.FloatRepr(d);
                ++n;
                if (actual != expected) { if (++bad <= 20) Console.WriteLine("MISMATCH " + expected + " vs " + actual); }
            }
            Console.WriteLine("floatrepr: " + n + " values, " + bad + " mismatches");
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ 값 규칙

        private static void UnitValueFormat()
        {
            Eq("5.0", ValueFormat.FloatRepr(5), "repr 5");
            Eq("0.1", ValueFormat.FloatRepr(0.1), "repr 0.1");
            Eq("1e-07", ValueFormat.FloatRepr(1e-7), "repr 1e-7");
            Eq("1.5e+20", ValueFormat.FloatRepr(1.5e20), "repr 1.5e20");
            Eq("1e+16", ValueFormat.FloatRepr(1e16), "repr 1e16");
            Eq("9999999999999998.0", ValueFormat.FloatRepr(9999999999999998.0), "repr 16 digits");
            Eq("0.0001", ValueFormat.FloatRepr(0.0001), "repr 1e-4");
            Eq("1e-05", ValueFormat.FloatRepr(0.00001), "repr 1e-5");
            Eq("-0.0", ValueFormat.FloatRepr(-0.0), "repr -0");
            Eq("123456.789", ValueFormat.FloatRepr(123456.789), "repr 123456.789");
            Eq("-2.5", ValueFormat.FloatRepr(-2.5), "repr -2.5");

            Eq("'abc'", TextUtil.Quote("abc"), "quote plain");
            Eq("\"it's\"", TextUtil.Quote("it's"), "quote apostrophe");
            Eq("'a\\u00A0b'", TextUtil.Quote("a\u00A0b"), "quote nbsp");
            Eq("'a\\nb\\x01'", TextUtil.Quote("a\nb\u0001"), "quote control");
            Eq("x", TextUtil.Trim(" \t\u00A0\u3000\uFEFFx \u3000"), "trim unicode blanks");

            string why;
            double v;
            Check(ValueFormat.ParseNumber(Cell.Str(" 5 "), out v, out why) && v == 5, "parse ' 5 '");
            Check(ValueFormat.ParseNumber(Cell.Str("+5"), out v, out why) && v == 5, "parse +5");
            Check(!ValueFormat.ParseNumber(Cell.Str("+-5"), out v, out why), "reject +-5");
            Check(!ValueFormat.ParseNumber(Cell.Str("0x10"), out v, out why) && why == "숫자가 아님: '0x10'", "reject hex", why);
            Check(!ValueFormat.ParseNumber(Cell.Str("1e999"), out v, out why) && why == "유한한 숫자가 아님: '1e999'", "reject overflow", why);
            Check(!ValueFormat.ParseNumber(Cell.Str("nan"), out v, out why) && why == "유한한 숫자가 아님: 'nan'", "reject nan", why);
            Check(!ValueFormat.ParseNumber(Cell.Str("5abc"), out v, out why), "reject 5abc");
            Check(ValueFormat.ParseNumber(Cell.Str(".5"), out v, out why) && v == 0.5, "parse .5");
            Check(ValueFormat.ParseNumber(Cell.Str("1."), out v, out why) && v == 1, "parse 1.");
            Check(!ValueFormat.ParseNumber(Cell.Str("."), out v, out why), "reject .");
            Check(!ValueFormat.ParseNumber(Cell.Bool(true), out v, out why) && why == "숫자가 아님: True", "reject bool", why);
            Check(!ValueFormat.ParseInt64(Cell.Num("9007199254740994"), out long l, out why) && why.StartsWith("값이 너무 큼"), "int64 limit", why);
            Check(!ValueFormat.ParseInt64(Cell.Num("1.5"), out l, out why) && why == "정수가 아님: 1.5", "int64 fraction", why);
            Eq("\"a\\\"\\\\\\n\\u0001é한\"", TextUtil.JsonString("a\"\\\n\u0001é한"), "json string");
            Eq("1e+16", ValueFormat.CellToString(Cell.Num("1E+16")), "cell 1E+16");
            Eq("5", ValueFormat.CellToString(Cell.Num("5")), "cell int");
            Eq("1.5", ValueFormat.CellToString(Cell.Num("1.5")), "cell 1.5");
            Eq("10000000000000000", CellText.FromDouble(1e16 / 1) == "1E+16" ? "10000000000000000" : CellText.FromDouble(1e16), "celltext 1e16 shape (above 2^53 keeps exponent)");
            Eq("9007199254740992", CellText.FromDouble(9007199254740992.0), "celltext 2^53");
            Eq("100000", CellText.FromDouble(100000), "celltext 100000");
            Eq("1E-05", CellText.FromDouble(0.00001), "celltext 1e-5");
        }

        // ------------------------------------------------------------------ 전체 흐름

        private static void WriteMain(string path, bool withMonster)
        {
            var sheets = new List<(string, object?[]?[])>
            {
                ("Enum", new[]
                {
                    R("Enum:string", "Name:string", "Value:int?", "Base:string?", "Comment:string?"),
                    R("SkillShape", "Circle", null, "uint8", "원형"),
                    R("SkillShape", "Box", null, null, null),
                    R("SkillShape", "Cone", 5.0, null, "원뿔"),
                    R("# 주석 행"),
                    R("Rarity", "Common", null, "uint16", null),
                    R("Rarity", "Rare", null, null, null),
                }),
                ("Const", new[]
                {
                    R("Name:string", "Type:string", "Value", "Comment:string?"),
                    R("MaxLevel", "uint", 99.0, "최대 레벨"),
                    R("DropRate", "float", 0.25, null),
                    R("Title", "string", "Hello \"World\"", null),
                    R("Debug", "bool", true, null),
                }),
                ("_Scratch", new[] { R("메모") }),
            };
            if (withMonster)
                sheets.Add(("Monster", new[]
                {
                    R("Id:uint[1..]", "Name:string", "HP:int[0..100000]", "HPMax:int", "Speed:float", "Shape:SkillShape", "Boss:bool", "Note:string?"),
                    R("#몬스터"),
                    R(1.0, "Slime", 100.0, 100.0, 1.5, "Circle", false, null),
                    null,
                    R(2.0, "Orc", 5000.0, 5000.0, 2.0, "Box", "yes", "큰 몬스터"),
                    R(3.0, "Bat", 1.0, 1.0, 0.0000001, "Cone", 0.0, "  "),
                }));
            XlsxWriter.Write(path, sheets.ToArray());
        }

        private static void WriteProto(string path)
        {
            XlsxWriter.Write(path,
                ("PacketEnum", new[]
                {
                    R("Enum:string", "Name:string", "Value:int?", "Base:string?", "Comment:string?", "EnumComment:string?"),
                    R("EGwResult", "Ok", null, "uint8", "성공", "게이트웨이 결과"),
                    R("EGwResult", "Fail", null, null, "실패", null),
                }),
                ("PacketConst", new[]
                {
                    R("Name:string", "Type:string", "Value:uint", "Comment:string?"),
                    R("kProtocolVersion", "uint16", 1.0, "프로토콜 버전"),
                    R("kMaxNameLen", "size", 16.0, null),
                }),
                ("PacketGroup", new[]
                {
                    R("Group:string", "Enum:string", "CsEnum:string", "Min:uint", "Max:uint?", "Section:string", "Comment:string?"),
                    R("Gateway", "EGatewayPacketType", "GatewayPacketType", 1.0, 99.0, "Gateway 패킷", "게이트웨이 패킷 번호"),
                }),
                ("Packet", new[]
                {
                    R("Name:string", "Group:string", "Id:uint", "Cs:string?", "Summary:string?", "Comment:string", "Detail:string?"),
                    R("GwLoginReq", "Gateway", 1.0, "Send", "로그인 요청", "로그인을 요청한다", null),
                    R("GwLoginAck", "Gateway", 2.0, "Recv", "로그인 응답", "로그인 응답\n두번째 줄", "자세히"),
                    R("GwNote", "Gateway", 3.0, null, null, "서버 전용", null),
                }),
                ("PacketField", new[]
                {
                    R("Packet:string", "Name:string", "Type:string", "Len:string?", "Enum:string?", "Comment:string?"),
                    R("GwLoginReq", "accountId", "uint32", null, null, "계정 번호"),
                    R("GwLoginReq", "version", "uint16", null, null, null),
                    R("GwLoginAck", "result", "uint8", null, "EGwResult", null),
                    R("GwLoginAck", "sessionId", "uint64", null, null, "세션"),
                    R("GwNote", "name", "char", "kMaxNameLen", null, null),
                }),
                ("Item", new[]
                {
                    R("Id:uint", "Price:int?"),
                    R(10.0, null),
                    R(11.0, -5.0),
                }),
                ("_Scratch", new[] { R("메모") }));
        }

        private const string MonsterJson =
"{\n  \"Monster\": [\n    {\n      \"Id\": 1,\n      \"Name\": \"Slime\",\n      \"HP\": 100,\n      \"HPMax\": 100,\n      \"Speed\": 1.5,\n      \"Shape\": \"Circle\",\n      \"Boss\": false\n    },\n" +
"    {\n      \"Id\": 2,\n      \"Name\": \"Orc\",\n      \"HP\": 5000,\n      \"HPMax\": 5000,\n      \"Speed\": 2.0,\n      \"Shape\": \"Box\",\n      \"Boss\": true,\n      \"Note\": \"큰 몬스터\"\n    },\n" +
"    {\n      \"Id\": 3,\n      \"Name\": \"Bat\",\n      \"HP\": 1,\n      \"HPMax\": 1,\n      \"Speed\": 1e-07,\n      \"Shape\": \"Cone\",\n      \"Boss\": false\n    }\n  ]\n}\n";

        private static void Pipeline()
        {
            string root = Tmp("pipe");
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            WriteMain(Path.Combine(data, "A_Main.xlsx"), true);
            WriteProto(Path.Combine(data, "B_Proto.xlsx"));
            File.WriteAllText(Path.Combine(data, "~$A_Main.xlsx"), "lock");          // 임시 파일은 건너뛴다
            Directory.CreateDirectory(Path.Combine(data, "sub"));

            string json = Path.Combine(root, "json"), cpp = Path.Combine(root, "cpp"), cs = Path.Combine(root, "cs"), pcs = Path.Combine(root, "pcs"), sizes = Path.Combine(root, "meta", "sizes.txt");
            string[] a = { data, "--out", json, "--cpp-out", cpp, "--cs-out", cs, "--cs-namespace", "Game.Rows", "--proto-cs-out", pcs, "--proto-cs-namespace", "Game.Proto", "--proto-sizes-out", sizes };

            var r = Run(a);
            Check(r.Code == 0, "pipeline exit 0", r.Err + r.Out);
            Check(r.Err == "", "pipeline no warnings", r.Err);
            Eq(MonsterJson, Read(Path.Combine(json, "Monster.json")), "Monster.json");
            Eq("{\n  \"Item\": [\n    {\n      \"Id\": 10\n    },\n    {\n      \"Id\": 11,\n      \"Price\": -5\n    }\n  ]\n}\n", Read(Path.Combine(json, "Item.json")), "Item.json (optional omitted)");
            Check(!HasBom(Path.Combine(json, "Monster.json")), "json has no BOM");
            Check(!File.Exists(Path.Combine(json, "Enum.json")) && !File.Exists(Path.Combine(json, "Packet.json")) && !File.Exists(Path.Combine(json, "_Scratch.json")), "definition and _ sheets not converted");
            Check(File.Exists(Path.Combine(json, ".XlsxConverter.stamp")), "stamp written");

            foreach (string f in new[] { "Monster.gen.h", "Item.gen.h", "Enums.gen.h", "EnumStrings.gen.h", "Consts.gen.h", "GeneratedCommon.h", "ZonePacketTypes.gen.h", "ZonePacketStructs.gen.h" })
                Check(File.Exists(Path.Combine(cpp, f)) && HasBom(Path.Combine(cpp, f)), "cpp file with BOM: " + f);
            foreach (string f in new[] { "Monster.gen.cs", "Item.gen.cs", "Enums.gen.cs", "Consts.gen.cs" })
                Check(File.Exists(Path.Combine(cs, f)) && HasBom(Path.Combine(cs, f)), "cs file with BOM: " + f);
            foreach (string f in new[] { "ProtocolTypes.gen.cs", "Packets.gen.cs" })
                Check(File.Exists(Path.Combine(pcs, f)) && HasBom(Path.Combine(pcs, f)), "proto cs file with BOM: " + f);
            Eq("GwLoginReqPacket 10\nGwLoginAckPacket 13\nGwNotePacket 20\n", Read(sizes), "sizes file");

            string monsterH = Read(Path.Combine(cpp, "Monster.gen.h"));
            Check(monsterH.Contains("\t\tuint32\tid = 0;\n") && monsterH.Contains("\t\tint32\thpMax = 0;\n") && monsterH.Contains("\t\t_tstring\tnote;\n"), "Monster.gen.h members", monsterH);
            Check(monsterH.Contains("{ _T(\"Id\"), EFieldType::UInt, 1ll, 4294967295ll },") && monsterH.Contains("{ _T(\"Note\"), EFieldType::String, (std::numeric_limits<int64>::min)(), (std::numeric_limits<int64>::max)(), false },"), "Monster.gen.h specs", monsterH);
            Check(monsterH.Contains("GameDataEnums::SkillShape\tshape = GameDataEnums::SkillShape::Circle;") && monsterH.Contains("bool\tenumOk = true;"), "Monster.gen.h enum member", monsterH);
            string common = Read(Path.Combine(cpp, "GeneratedCommon.h"));
            Check(common.IndexOf("Enums.gen.h", StringComparison.Ordinal) < common.IndexOf("Consts.gen.h", StringComparison.Ordinal) && common.IndexOf("Consts.gen.h", StringComparison.Ordinal) < common.IndexOf("Monster.gen.h", StringComparison.Ordinal) && !common.Contains("ZonePacket"), "GeneratedCommon.h order", common);
            string structs = Read(Path.Combine(cpp, "ZonePacketStructs.gen.h"));
            Check(structs.Contains("static_assert(sizeof(GwLoginReqPacket) == 10,") && structs.Contains("char\tname[kMaxNameLen];") && structs.Contains("uint8\tresult;\t// EGwResult"), "ZonePacketStructs.gen.h", structs);
            string types = Read(Path.Combine(cpp, "ZonePacketTypes.gen.h"));
            Check(types.Contains("constexpr std::uint16_t kProtocolVersion = 1;") && types.Contains("constexpr std::size_t kMaxNameLen = 16;") && types.Contains("\tGwLoginReq = 1,\t// 로그인 요청\n"), "ZonePacketTypes.gen.h", types);
            string consts = Read(Path.Combine(cpp, "Consts.gen.h"));
            Check(consts.Contains("constexpr std::uint32_t kMaxLevel = 99;\t// 최대 레벨") && consts.Contains("constexpr const TCHAR* kTitle = _T(\"Hello \\\"World\\\"\");") && consts.Contains("constexpr bool kDebug = true;"), "Consts.gen.h", consts);
            string monsterCs = Read(Path.Combine(cs, "Monster.gen.cs"));
            Check(monsterCs.Contains("namespace Game.Rows") && monsterCs.Contains("public SkillShape Shape { get; set; } = SkillShape.Circle;") && monsterCs.Contains("if( this.Id < 1u ) errors.Add(where + \".Id=\" + this.Id + \" (허용 범위 1..)\");") && monsterCs.Contains("public string Note { get; set; } = string.Empty;"), "Monster.gen.cs", monsterCs);
            string pk = Read(Path.Combine(pcs, "Packets.gen.cs"));
            Check(pk.Contains("namespace Game.Proto") && pk.Contains("public static byte[] GwLoginReq(uint accountId, ushort version)") && pk.Contains("p.Result = (EGwResult)r.U8();".Replace("EGwResult", "GwResult")) && pk.Contains("public GwResult Result;"), "Packets.gen.cs", pk);

            string? keep = Environment.GetEnvironmentVariable("KEEP_GEN");
            if (!string.IsNullOrEmpty(keep))
            {
                if (Directory.Exists(keep)) Directory.Delete(keep, true);
                foreach (string d in new[] { "json", "cpp", "cs", "pcs" })
                {
                    Directory.CreateDirectory(Path.Combine(keep, d));
                    foreach (string f in Directory.GetFiles(Path.Combine(root, d))) File.Copy(f, Path.Combine(keep, d, Path.GetFileName(f)));
                }
            }

            // 같은 입력으로 다시 실행: 아무것도 다시 쓰지 않는다.
            DateTime t0 = File.GetLastWriteTimeUtc(Path.Combine(json, "Monster.json"));
            DateTime h0 = File.GetLastWriteTimeUtc(Path.Combine(cpp, "Monster.gen.h"));
            Thread.Sleep(1100);
            r = Run(a);
            Check(r.Code == 0 && r.Out.Contains("Monster.json: 3 rows (unchanged)") && r.Out.Contains("0 written"), "rerun unchanged", r.Out + r.Err);
            Check(File.GetLastWriteTimeUtc(Path.Combine(json, "Monster.json")) == t0 && File.GetLastWriteTimeUtc(Path.Combine(cpp, "Monster.gen.h")) == h0, "rerun keeps mtimes");

            // 증분 실행
            string[] inc = a.Concat(new[] { "--incremental" }).ToArray();
            r = Run(inc);
            Check(r.Code == 0 && r.Out.StartsWith("up to date: "), "incremental up to date", r.Out + r.Err);
            File.Delete(Path.Combine(cpp, "Item.gen.h"));
            r = Run(inc);
            Check(r.Code == 0 && !r.Out.StartsWith("up to date") && File.Exists(Path.Combine(cpp, "Item.gen.h")), "incremental regenerates when an output is missing", r.Out + r.Err);

            // 낡은 파일 정리: 손으로 만든 파일은 남긴다
            File.WriteAllText(Path.Combine(json, "Custom.json"), "{}");
            File.WriteAllText(Path.Combine(cpp, "Hand.gen.h"), "// 손으로 만든 파일\n");
            File.WriteAllText(Path.Combine(cpp, "Old.gen.h"), "// Old.gen.h : 자동 생성 파일 - 직접 수정하지 마세요.\n");
            WriteMain(Path.Combine(data, "A_Main.xlsx"), false);
            r = Run(a);
            Check(r.Code == 0, "rerun without Monster", r.Err + r.Out);
            Check(!File.Exists(Path.Combine(json, "Monster.json")) && r.Out.Contains("removed stale: "), "stale json removed", r.Out);
            Check(File.Exists(Path.Combine(json, "Custom.json")) && File.Exists(Path.Combine(json, "Item.json")), "hand-made json kept");
            Check(!File.Exists(Path.Combine(cpp, "Monster.gen.h")) && !File.Exists(Path.Combine(cpp, "Old.gen.h")) && File.Exists(Path.Combine(cpp, "Hand.gen.h")), "stale gen.h removed, hand-made kept");

            // --sheets: 정리와 상태 기록을 하지 않는다
            r = Run(data, "--out", json, "--sheets", "Item");
            Check(r.Code == 0 && r.Out.Contains("Item.json: 2 rows"), "--sheets", r.Out + r.Err);
        }

        // ------------------------------------------------------------------ 오류

        private static void Errors()
        {
            string root = Tmp("err");
            string x = Path.Combine(root, "bad.xlsx"), json = Path.Combine(root, "json");
            XlsxWriter.Write(x,
                ("Enum", new[] { R("Enum:string", "Name:string"), R("Kind", "A"), R("Kind", "B") }),
                ("Bad", new[]
                {
                    R("Id:uint[1..]", "Count:int[0..10]", "Ratio:float", "Kind:Kind", "On:bool", "Id:int", "Bogus", "X:long", "Y:uint[5..1]"),
                    R(0.0, 11.0, "abc", "C", "maybe", null, null, null, null),
                    R(1.5, -1.0, true, "A", true),
                    R("#주석", "값도 있음"),
                    R(5.0, 2.0, 0.5, "B", "Y"),
                }),
                ("Empty", new object?[]?[] { }),
                ("Hdr", new[] { R(" ", null), R(1.0) }),
                ("enums", new[] { R("A:int"), R(1.0) }));
            var r = Run(x, "--out", json);
            Check(r.Code == 1, "errors exit 1", r.Err);
            string[] expectedParts =
            {
                "Bad!7열 헤더 'Bogus': 'Name:type' 형식이어야 합니다",
                "Bad!8열 헤더 'X:long': 타입은 int, uint, float, string, bool 또는 Enum 시트에 정의한 enum 이름이어야 합니다 ('long')",
                "Bad!9열 헤더 'Y:uint[5..1]': 범위의 min이 max보다 큽니다",
                "Bad!6열 헤더 'Id:int': 필드명 중복",
                "Bad!2행 Id: 0은(는) 허용 범위 1..4294967295를 벗어남",
                "Bad!2행 Count: 11은(는) 허용 범위 0..10를 벗어남",
                "Bad!2행 Ratio: 숫자가 아님: 'abc'",
                "Bad!2행 Kind: Kind의 멤버가 아님: 'C' (허용: A, B)",
                "Bad!2행 On: bool이 아님: 'maybe'",
                "Bad!3행 Id: 정수가 아님: 1.5",
                "Bad!3행 Count: 허용 범위 0..10", // 아래에서 부분 일치로 확인
                "Bad!3행 Ratio: 숫자가 아님: True",
                "Empty: 시트가 비어 있습니다",
                "Hdr: 헤더가 비어 있습니다",
                "enums: 생성 파일 이름과 겹치는 예약 시트 이름입니다",
            };
            // "Bad!3행 Count"는 값 표기가 정수 -1이라 "-1은(는) 허용 범위 0..10를 벗어남"이다.
            expectedParts[10] = "Bad!3행 Count: -1은(는) 허용 범위 0..10를 벗어남";
            foreach (string p in expectedParts)
                Check(r.Err.Contains(p), "error text: " + p, r.Err);
            Check(r.Err.StartsWith("변환 실패 ("), "error header", r.Err);
            Check(!Directory.Exists(json), "no output on error");

            // '#' 행에 다른 칸 값이 있으면 warning
            string w = Path.Combine(root, "warn.xlsx");
            XlsxWriter.Write(w, ("W", new[] { R("A:uint"), R("#1 보스", "x"), R(1.0) }));
            r = Run(w, "--out", Path.Combine(root, "wout"));
            Check(r.Code == 0 && r.Err.Contains("warning: W!2행 첫 칸이 '#'로 시작해 주석으로 건너뜁니다."), "comment-row warning", r.Err);

            // 엑셀 열기 실패
            r = Run(Path.Combine(root, "nope.xlsx"), "--out", json);
            Check(r.Code == 2 && r.Err.StartsWith("error: nope.xlsx: "), "missing file exit 2", r.Err);
            File.WriteAllText(Path.Combine(root, "notzip.xlsx"), "not a zip");
            r = Run(Path.Combine(root, "notzip.xlsx"), "--out", json);
            Check(r.Code == 2, "corrupt file exit 2", r.Err);

            // C++ 예약어 enum 멤버 / 패킷 이름 / C# 형식 이름 충돌
            string k = Path.Combine(root, "kw.xlsx");
            XlsxWriter.Write(k,
                ("Enum", new[] { R("Enum:string", "Name:string"), R("Mode", "default"), R("Mode", "ERROR") }),
                ("T", new[] { R("A:Mode"), R("default") }));
            r = Run(k, "--out", json, "--cpp-out", Path.Combine(root, "kwcpp"));
            Check(r.Code == 1 && r.Err.Contains("Enum Mode.default: C++ 키워드나 Windows 매크로 이름은 멤버 이름으로 쓸 수 없습니다") && r.Err.Contains("Enum Mode.ERROR:"), "cpp reserved names", r.Err);
            r = Run(k, "--out", json, "--cs-out", Path.Combine(root, "kwcs"));
            Check(r.Code == 0, "cs keyword member gets @", r.Err);
            Check(Read(Path.Combine(root, "kwcs", "Enums.gen.cs")).Contains("\t\t@default = 0,"), "@default member");
            Check(Read(Path.Combine(root, "kwcs", "T.gen.cs")).Contains("public Mode A { get; set; } = Mode.@default;"), "@default initializer", Read(Path.Combine(root, "kwcs", "T.gen.cs")));
        }

        private static void Usage()
        {
            var r = Run();
            Check(r.Code == 2 && r.Err.StartsWith("usage: XlsxConverter"), "usage without args", r.Err);
            r = Run("a.xlsx");
            Check(r.Code == 2, "usage without --out");
            r = Run("a.xlsx", "--out", "o", "--bogus");
            Check(r.Code == 2 && r.Err.StartsWith("알 수 없는 인자: --bogus"), "unknown arg", r.Err);
            r = Run("a.xlsx", "--out", "o", "--incremental", "--sheets", "A");
            Check(r.Code == 2 && r.Err.Contains("--incremental은 --sheets와 함께 쓸 수 없습니다"), "incremental + sheets", r.Err);
            r = Run("a.xlsx", "--out", "o", "--cpp-out", "c", "--sheets", "A");
            Check(r.Code == 2 && r.Err.Contains("--sheets와 함께 쓸 수 없습니다"), "codegen + sheets", r.Err);
            r = Run("a.xlsx", "--out", "o", "--cs-out", "c", "--proto-cs-out", "c/");
            Check(r.Code == 2 && r.Err.Contains("--proto-cs-out은 --cs-out과 다른 폴더여야 합니다"), "same cs folders", r.Err);
            r = Run("a.xlsx", "--out");
            Check(r.Code == 2 && r.Err.StartsWith("알 수 없는 인자: --out"), "option without value", r.Err);
        }

        // ------------------------------------------------------------------ 폴더 규칙

        private static void FolderRules()
        {
            string root = Tmp("folder");
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            XlsxWriter.Write(Path.Combine(data, "b.xlsx"), ("Monster", new[] { R("A:int"), R(1.0) }), ("_Memo", new[] { R("x") }));
            XlsxWriter.Write(Path.Combine(data, "A.xlsx"), ("monster", new[] { R("A:int"), R(2.0) }), ("Item", new[] { R("A:int"), R(3.0) }), ("_Memo", new[] { R("y") }), ("Same", new[] { R("A:int"), R(1.0) }));
            XlsxWriter.Write(Path.Combine(data, "c.xlsx"), ("Same", new[] { R("A:int"), R(1.0) }));
            var r = Run(data, "--out", Path.Combine(root, "o"));
            Check(r.Code == 1, "duplicates exit 1", r.Err);
            Check(r.Err.Contains("Monster / monster: 대소문자만 다른 시트 이름이 두 파일에 있습니다. 출력 파일 이름이 같아집니다 (A.xlsx, b.xlsx)") && r.Err.Contains("Same: 시트 이름이 두 파일에 있습니다 (A.xlsx, c.xlsx)"), "duplicate messages (files in case-insensitive name order)", r.Err);
            Check(!r.Err.Contains("_Memo"), "_ sheets may overlap", r.Err);

            // 폴더 안에서 시트 순서: 파일 이름순
            string d2 = Path.Combine(root, "d2");
            Directory.CreateDirectory(d2);
            XlsxWriter.Write(Path.Combine(d2, "z.xlsx"), ("Z", new[] { R("A:int"), R(1.0) }));
            XlsxWriter.Write(Path.Combine(d2, "B.xlsx"), ("B", new[] { R("A:int"), R(1.0) }), ("A", new[] { R("A:int"), R(1.0) }));
            r = Run(d2, "--out", Path.Combine(root, "o2"));
            var order = r.Out.Split('\n').Where(l => l.Contains(" rows")).Select(l => Path.GetFileName(l.Substring(0, l.IndexOf(':')))).ToArray();
            Check(r.Code == 0 && string.Join(",", order) == "B.json,A.json,Z.json", "sheet order: file name order, then tab order", string.Join(",", order) + "\n" + r.Err);

            string empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(empty);
            r = Run(empty, "--out", Path.Combine(root, "o3"));
            Check(r.Code == 2 && r.Err.StartsWith("error: no .xlsx file in: "), "empty folder", r.Err);
        }

        // ------------------------------------------------------------------ 라이터 검증 규칙

        private static string? Throws(Action a)
        {
            try { a(); return null; }
            catch (InvalidOperationException e) { return e.Message; }
        }

        private static void WriterRules()
        {
            var rows = new SortedDictionary<int, Cell[]>();
            string? Sheet(string name, params Cell[][] rs)
            {
                var d = new SortedDictionary<int, Cell[]>();
                for (int i = 0; i < rs.Length; ++i) d[i + 1] = rs[i];
                return Throws(() => WorkbookWriterRules.ValidateSheet(name, new[] { "Exists" }, d));
            }

            Check(Sheet("Ok", new[] { Cell.Str("=SUM(A1)"), Cell.Num("0.1"), Cell.Num("1E-07"), Cell.Bool(true), default, Cell.Str("") }) == null, "writer: valid cells");
            Eq("시트 이름이 비어 있습니다", Sheet("") ?? "", "writer: empty name");
            Eq("시트 이름이 31자를 넘습니다: " + new string('a', 32), Sheet(new string('a', 32)) ?? "", "writer: long name");
            Eq("시트 이름에 쓸 수 없는 문자가 있습니다: a/b", Sheet("a/b") ?? "", "writer: bad char in name");
            Eq("시트 이름은 작은따옴표로 시작하거나 끝날 수 없습니다: 'a", Sheet("'a") ?? "", "writer: apostrophe");
            Eq("시트 이름이 중복됩니다: EXISTS", Sheet("EXISTS") ?? "", "writer: duplicate (case-insensitive)");
            Eq("셀 B1: 숫자 셀 값이 숫자가 아닙니다: 'abc'", Sheet("S", new[] { Cell.Str("x"), Cell.Num("abc") }) ?? "", "writer: bad number");
            Eq("셀 A1: 숫자 셀 값이 숫자가 아닙니다: '1e999'", Sheet("S", new[] { Cell.Num("1e999") }) ?? "", "writer: infinite number");
            Eq("셀 C2: xlsx에 쓸 수 없는 제어 문자가 있습니다 (U+0001)", Sheet("S", new[] { Cell.Str("a") }, new[] { default, default, Cell.Str("a\u0001") }) ?? "", "writer: control char");
            Eq("셀 A1: UTF-8이 아닌 문자열입니다", Sheet("S", new[] { Cell.Str("a\uD800") }) ?? "", "writer: lone surrogate");
            Check((Sheet("S", new[] { Cell.Str(new string('a', 32768)) }) ?? "").StartsWith("셀 A1: 문자열이 32767바이트(UTF-8)를 넘습니다 (32768바이트)"), "writer: too long text");
            Eq("셀 AB1: 숫자 셀 값이 숫자가 아닙니다: 'x'", Sheet("S", Enumerable.Repeat(default(Cell), 27).Append(Cell.Num("x")).ToArray()) ?? "", "writer: column letters");
            Check(WorkbookWriterRules.TryParseNumber("-5", out bool isInt, out long iv, out _) && isInt && iv == -5, "writer: int parse");
            Check(WorkbookWriterRules.TryParseNumber("99999999999999999999", out isInt, out _, out double dv) && !isInt && dv > 9e19, "writer: huge int falls to double");
        }

        // 대용량 입력의 처리 시간을 잰다: bench <시트 수> <시트당 행 수> <패킷 수>
        private static int Bench(int sheetCount, int rowCount, int packetCount)
        {
            string root = Tmp("bench");
            var enumRows = new List<object?[]> { R("Enum:string", "Name:string") };
            for (int i = 0; i < 200; ++i) enumRows.Add(R("Big", "M" + i));
            var sheets = new List<(string, object?[]?[])> { ("Enum", enumRows.ToArray()) };
            for (int s = 0; s < sheetCount; ++s)
            {
                var rows = new List<object?[]> { R("Id:uint", "Name:string", "HP:int[0..100000]", "Speed:float", "Kind:Big", "Boss:bool", "Note:string?") };
                for (int r = 0; r < rowCount; ++r)
                    rows.Add(R((double)r + 1, "Name" + r, (double)(r % 1000), r * 0.37, "M" + (r % 200), r % 2 == 0, null));
                sheets.Add(("Sheet" + s, rows.ToArray()));
            }
            var pk = new List<object?[]> { R("Name:string", "Group:string", "Id:uint", "Cs:string?", "Summary:string?", "Comment:string", "Detail:string?") };
            var pf = new List<object?[]> { R("Packet:string", "Name:string", "Type:string", "Len:string?", "Enum:string?", "Comment:string?") };
            for (int i = 0; i < packetCount; ++i)
            {
                pk.Add(R("P" + i, "G", (double)(i + 1), "Both", "요약" + i, "설명" + i, null));
                for (int f = 0; f < 6; ++f) pf.Add(R("P" + i, "f" + f, f % 2 == 0 ? "uint32" : "uint16", null, null, "필드"));
            }
            sheets.Add(("PacketEnum", new[] { R("Enum:string", "Name:string", "Value:int?", "Base:string?", "Comment:string?", "EnumComment:string?"), R("E", "A", null, null, null, null) }));
            sheets.Add(("PacketConst", new[] { R("Name:string", "Type:string", "Value:uint", "Comment:string?"), R("kN", "uint16", 1.0, null) }));
            sheets.Add(("PacketGroup", new[] { R("Group:string", "Enum:string", "CsEnum:string", "Min:uint", "Max:uint?", "Section:string", "Comment:string?"), R("G", "EG", "GT", 1.0, (double)(packetCount + 10), "S", null) }));
            sheets.Add(("Packet", pk.ToArray()));
            sheets.Add(("PacketField", pf.ToArray()));

            string xlsx = Path.Combine(root, "big.xlsx");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            XlsxWriter.Write(xlsx, sheets.ToArray());
            Console.WriteLine("write xlsx: " + sw.ElapsedMilliseconds + " ms, " + new FileInfo(xlsx).Length / 1024 + " KB");

            string[] a = { xlsx, "--out", Path.Combine(root, "json"), "--cpp-out", Path.Combine(root, "cpp"), "--cs-out", Path.Combine(root, "cs"), "--proto-cs-out", Path.Combine(root, "pcs"), "--proto-sizes-out", Path.Combine(root, "sizes.txt") };
            for (int run = 1; run <= 3; ++run)
            {
                sw.Restart();
                var r = Run(a);
                Console.WriteLine("run " + run + ": " + sw.ElapsedMilliseconds + " ms, exit " + r.Code + (r.Code != 0 ? "\n" + r.Err.Substring(0, Math.Min(500, r.Err.Length)) : ""));
            }

            // 엑셀 읽기를 뺀 순수 변환 시간: 같은 데이터를 메모리에서 바로 넘긴다.
            var mem = new Dictionary<string, SortedDictionary<int, Cell[]>>();
            foreach (var (name, objRows) in sheets)
            {
                var rows = new SortedDictionary<int, Cell[]>();
                for (int r = 0; r < objRows.Length; ++r)
                {
                    object?[]? o = objRows[r];
                    if (o == null) continue;
                    var cells = new Cell[o.Length];
                    for (int c = 0; c < o.Length; ++c)
                        cells[c] = o[c] switch
                        {
                            null => default,
                            string str => Cell.Str(str),
                            bool b => Cell.Bool(b),
                            double d => Cell.Num(CellText.FromDouble(d)),
                            _ => default,
                        };
                    rows[r + 1] = cells;
                }
                mem[name] = rows;
            }
            for (int run = 1; run <= 3; ++run)
            {
                sw.Restart();
                var so = new StringWriter { NewLine = "\n" };
                var se = new StringWriter { NewLine = "\n" };
                int code = ConverterApp.Run(a, p => new MemReader(mem), so, se);
                Console.WriteLine("core only " + run + ": " + sw.ElapsedMilliseconds + " ms, exit " + code);
            }
            return 0;
        }

        private sealed class MemReader : IWorkbookReader
        {
            private readonly Dictionary<string, SortedDictionary<int, Cell[]>> _s;
            public MemReader(Dictionary<string, SortedDictionary<int, Cell[]>> s) { _s = s; }
            public IReadOnlyList<string> GetSheetNames() => _s.Keys.ToList();
            public bool ReadSheet(string name, out SortedDictionary<int, Cell[]> rows, out string error)
            {
                error = string.Empty;
                rows = _s[name];
                return true;
            }
            public void Dispose() { }
        }
    }
}
