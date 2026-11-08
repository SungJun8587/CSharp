using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace XlsxConverter
{
    /// <summary>
    /// 기획 데이터 엑셀(.xlsx) -> 시트별 JSON 변환기.
    ///
    /// 사용법:  XlsxConverter &lt;GameData 폴더 | file.xlsx&gt; --out ../GameData [--cpp-out ../Server/Generated] [--cs-out ../Client/Generated] [--sheets Monster Spawn]
    ///
    ///   - 첫 인자가 폴더이면 그 폴더 바로 아래의 모든 .xlsx를 읽어 하나의 데이터 집합으로 합친다. (기능별로 엑셀을 나눠 관리할 수 있다)
    ///       하위 폴더는 보지 않고, 엑셀이 만든 임시 파일(~$로 시작)은 건너뛴다. 파일은 이름순(대소문자 무시)으로 읽는다.
    ///       시트 이름은 파일에 상관없이 전체에서 유일해야 한다. (대소문자만 달라도 같은 이름) 같은 이름이 두 파일에 있으면 두 파일 이름을 보여 주고 실패한다.
    ///       ('_'로 시작하는 시트는 변환하지 않으므로 겹쳐도 된다)
    ///       Enum, Const, Packet* 정의 시트도 아무 파일에나 둘 수 있다. 생성 코드와 JSON의 시트 순서는 파일 이름순, 파일 안에서는 시트 탭 순서다.
    ///   - 1행은 헤더이며 각 칸은 "필드명:타입[min..max]?" (타입: int, uint, float, string, bool 또는 Enum 시트의 enum 이름).
    ///   - 2행부터 데이터. 첫 칸이 '#'로 시작하는 행과 완전히 빈 행은 건너뛴다.
    ///   - '_'로 시작하는 시트는 변환하지 않는다. (--sheets로 직접 지정하면 변환)
    ///   - 시트 이름이 곧 출력 파일 이름이고 JSON 최상위 키다. Monster 시트 -> Monster.json = {"Monster":[...]}
    ///   - 오류가 하나라도 있으면 모두 출력하고 아무 파일도 쓰지 않는다.
    ///   - 코드 생성은 출력 폴더를 준 언어만 만든다.
    ///       --cpp-out &lt;dir&gt;        &lt;시트&gt;.gen.h + GeneratedCommon.h (+ 패킷 정의 시트가 있으면 ZonePacketTypes.gen.h, ZonePacketStructs.gen.h)
    ///       --cs-out &lt;dir&gt;         &lt;시트&gt;.gen.cs (&lt;시트&gt;Row, &lt;시트&gt;Table 클래스)
    ///       --cs-namespace &lt;name&gt;  C# 네임스페이스 (기본 GameDataRows)
    ///       --proto-cs-out &lt;dir&gt;, --proto-cs-namespace &lt;name&gt;, --proto-sizes-out &lt;file&gt;  패킷 프로토콜 C# 코드와 크기 목록
    ///     생성 코드는 BOM 있는 UTF-8로 쓴다. JSON은 BOM 없는 UTF-8이다.
    ///   - --incremental: 엑셀의 수정 시각/크기가 마지막 실행 때와 같고 출력 파일이 모두 있으면 엑셀을 열지 않고 끝낸다. (Pre-Build 이벤트용)
    ///       엑셀이 다른 프로그램에 열려 읽을 수 없으면 경고만 내고 기존 출력을 그대로 쓴다. (이전 실행과 같은 인자일 때만)
    ///       상태는 &lt;--out&gt;/.XlsxConverter.stamp 에 저장한다. (버전 관리에서 제외할 것) --incremental이 아니어도 상태 파일을 쓴다. (--sheets 실행은 제외)
    ///   - 내용이 바뀌지 않은 파일은 다시 쓰지 않는다. 시트가 없어지거나 이름이 바뀌면 낡은 *.gen.* 파일과, 지난 실행이 만들었던 JSON은 지운다.
    ///
    /// 종료 코드: 0 성공, 1 변환 실패(데이터 오류), 2 사용법/파일 오류.
    /// </summary>
    public static class ConverterApp
    {
        public const int ExitOk = 0;
        public const int ExitConvertFailed = 1;
        public const int ExitUsage = 2;

        /// <summary>입력 경로에서 읽을 엑셀 파일 목록을 만듭니다. 파일이면 그 파일 하나, 폴더면 바로 아래의 .xlsx 전부 (이름순, ~$ 임시 파일 제외).</summary>
        public static List<string> ListWorkbookFiles(string input)
        {
            var files = new List<string>();
            if (!Directory.Exists(input))
            {
                files.Add(input);
                return files;
            }

            foreach (string path in Directory.EnumerateFiles(input))
            {
                string name = Path.GetFileName(path);
                if (name.Length == 0 || name.StartsWith("~$", StringComparison.Ordinal) || name[0] == '.') continue;
                if (AsciiChar.Lower(Path.GetExtension(path)) != ".xlsx") continue;
                files.Add(path);
            }
            files.Sort((a, b) =>
            {
                string x = AsciiChar.Lower(Path.GetFileName(a)), y = AsciiChar.Lower(Path.GetFileName(b));
                int c = string.CompareOrdinal(x, y);
                return c != 0 ? c : string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b));
            });
            return files;
        }

        private sealed class SheetResult
        {
            public string Name = string.Empty;
            public string Json = string.Empty;
            public int RowCount;
            public SheetSchema Schema = new SheetSchema();
        }

        /// <summary>진입점. 종료 코드(ExitOk, ExitConvertFailed, ExitUsage)를 돌려준다.</summary>
        /// <param name="argv">명령행 인자 (프로그램 이름 제외)</param>
        /// <param name="open">엑셀 파일 하나를 여는 함수 (실제 실행은 ClosedXML 어댑터)</param>
        public static int Run(string[] argv, WorkbookOpener open, TextWriter stdout, TextWriter stderr)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            if (!Options.TryParse(argv, stderr, out Options opt))
                return ExitUsage;

            string signature = Stamp.MakeSignature(opt);
            List<string> workbookFiles = ListWorkbookFiles(opt.XlsxPath);
            string sourceStamp = Stamp.SourceStamp(workbookFiles);    // 읽기 시작하기 전에 잡는다. (실행 중 엑셀이 바뀌면 다음 실행에서 다시 변환된다)
            if (opt.Incremental && Stamp.Matches(opt, signature, sourceStamp))
            {
                stdout.Write("up to date: " + opt.XlsxPath + "\n");
                return ExitOk;
            }

            bool opened = false;
            try
            {
                using var workbook = new WorkbookSet(opt.XlsxPath, workbookFiles, open);
                opened = true;

                var names = new List<string>();
                if (opt.HasSheets)
                    names.AddRange(opt.Sheets);
                else
                    foreach (string name in workbook.GetSheetNames())
                        if (name.Length == 0 || name[0] != '_')
                            names.Add(name);

                var results = new List<SheetResult>();
                var errors = new List<string>(workbook.Duplicates);
                var warnings = new List<string>();      // 변환은 되지만 알려야 할 것. 오류가 없을 때 stderr에 낸다

                // Enum, Const 시트는 형식 정의라 JSON으로 변환하지 않는다.
                // Enum은 --sheets를 줘도 항상 읽어 enum 열의 멤버 이름을 검증한다. Const는 코드 생성에만 쓴다.
                var defs = new Definitions();
                var packetSchema = new PacketSchema();
                {
                    IReadOnlyList<string> all = workbook.GetSheetNames();
                    bool Has(string n) => all.Contains(n);
                    void ReadDef(string sheet, Func<Rows, Definitions, List<string>, bool> parse)
                    {
                        if (!workbook.ReadSheet(sheet, out Rows rows, out string error)) errors.Add(error);
                        else parse(rows, defs, errors);
                    }
                    if (Has(DefinitionSheets.EnumSheet))
                        ReadDef(DefinitionSheets.EnumSheet, DefinitionSheets.ParseEnumSheet);
                    if (Has(DefinitionSheets.ConstSheet) && (opt.HasCppOut || opt.HasCsOut))
                        ReadDef(DefinitionSheets.ConstSheet, DefinitionSheets.ParseConstSheet);

                    var data = new List<string>();
                    foreach (string name in names)
                    {
                        if (name == DefinitionSheets.EnumSheet || name == DefinitionSheets.ConstSheet || DefinitionSheets.IsPacketSheet(name)) continue;
                        if (DefinitionSheets.IsReservedSheet(name)) { errors.Add(name + ": 생성 파일 이름과 겹치는 예약 시트 이름입니다 (Enum, Const, Packet*, Enums, Consts, EnumStrings, GeneratedCommon, ZonePacketTypes, ZonePacketStructs. 대소문자는 구분하지 않습니다)"); continue; }
                        data.Add(name);
                    }
                    names = data;

                    // 패킷 정의 시트: 코드 생성에만 쓴다. 하나라도 있으면 다섯 시트를 모두 읽는다.
                    if (opt.HasCppOut || opt.HasCsOut || opt.HasProtoCsOut || opt.HasProtoSizesOut)
                    {
                        var packetSheets = new Dictionary<string, Rows>(StringComparer.Ordinal);
                        foreach (string n in all)
                        {
                            if (!DefinitionSheets.IsPacketSheet(n)) continue;
                            if (!workbook.ReadSheet(n, out Rows rows, out string error)) errors.Add(error);
                            else packetSheets[n] = rows;
                        }
                        if (packetSheets.Count > 0) DefinitionSheets.ParsePacketSheets(packetSheets, packetSchema, errors);
                    }
                }

                foreach (string name in names)
                {
                    if (!workbook.ReadSheet(name, out Rows rows, out string error))
                    {
                        errors.Add(error);
                        continue;
                    }

                    var result = new SheetResult { Name = name };
                    if (SheetConverter.Convert(name, rows, errors, out string json, out int rowCount, result.Schema, defs, warnings))
                    {
                        result.Json = json;
                        result.RowCount = rowCount;
                        results.Add(result);
                    }
                }

                // 시트 스키마 목록. 코드 생성(행 구조체, 패킷 C# 형식 이름 검사)이 함께 쓴다.
                var schemas = new List<SheetSchema>();
                if (errors.Count == 0)
                    foreach (SheetResult r in results)
                        schemas.Add(r.Schema);

                var cppFiles = new List<GeneratedFile>();
                var csFiles = new List<GeneratedFile>();
                if ((opt.HasCppOut || opt.HasCsOut) && errors.Count == 0)
                {
                    if (opt.HasCppOut)
                        RowGenerator.Generate(schemas, defs, cppFiles, errors);
                    if (opt.HasCsOut)
                        RowGenerator.GenerateCs(schemas, defs, opt.CsNamespace, csFiles, errors);
                }

                var protoCsFiles = new List<GeneratedFile>();
                string protoSizes = string.Empty;
                if (errors.Count == 0 && !packetSchema.IsEmpty)
                {
                    if (opt.HasCppOut)
                        PacketGenerator.Generate(packetSchema, cppFiles, errors);
                    if (opt.HasProtoCsOut)
                    {
                        // 시트 클래스와 패킷 프로토콜이 같은 C# 네임스페이스에 만들어지면 형식 이름이 겹치지 않는지 확인한다.
                        string protoNs = opt.ProtoCsNamespace.Length == 0 ? opt.CsNamespace : opt.ProtoCsNamespace;
                        HashSet<string>? rowTypes = null;
                        if (opt.HasCsOut && protoNs == opt.CsNamespace)
                            rowTypes = RowGenerator.CsTypeNames(schemas, defs);
                        PacketGenerator.GenerateCs(packetSchema, protoNs, protoCsFiles, errors, rowTypes);
                    }
                    if (opt.HasProtoSizesOut)
                        protoSizes = PacketGenerator.Sizes(packetSchema);
                }
                if (errors.Count == 0 && packetSchema.IsEmpty && (opt.HasProtoCsOut || opt.HasProtoSizesOut))
                    errors.Add("--proto-*를 줬지만 엑셀(들)에 패킷 정의 시트(PacketEnum, PacketConst, PacketGroup, Packet, PacketField)가 없습니다");

                if (errors.Count > 0)
                {
                    stderr.Write("변환 실패 (" + errors.Count + "건):\n");
                    foreach (string e in errors)
                        stderr.Write("  " + e + "\n");
                    return ExitConvertFailed;
                }

                foreach (string w in warnings)
                    stderr.Write("warning: " + w + "\n");

                // 지난 실행이 만든 파일 목록. (이번 실행이 덮어쓰기 전에 읽어 둔다)
                var previous = new Stamp();
                if (!opt.HasSheets) previous.Load(Path.Combine(opt.OutDir, Stamp.FileName));

                Directory.CreateDirectory(opt.OutDir);
                var produced = new List<string>();          // 이번 실행의 출력 파일(절대 경로). 상태 파일에 적는다
                var producedJson = new HashSet<string>(StringComparer.Ordinal);
                foreach (SheetResult r in results)
                {
                    string path = Path.Combine(opt.OutDir, r.Name + ".json");
                    produced.Add(Stamp.AbsolutePath(path));
                    producedJson.Add(produced[produced.Count - 1]);
                    bool wrote = FileUtil.WriteFileIfChanged(path, FileUtil.Utf8(r.Json));
                    stdout.Write(path + ": " + r.RowCount + " rows" + (wrote ? "" : " (unchanged)") + "\n");
                }

                if (opt.HasCppOut)
                    FileOutput.WriteGeneratedSet(opt.CppOutDir, cppFiles, ".h", stdout);
                if (opt.HasCsOut)
                    FileOutput.WriteGeneratedSet(opt.CsOutDir, csFiles, ".cs", stdout);
                if (opt.HasProtoCsOut)
                    FileOutput.WriteGeneratedSet(opt.ProtoCsOutDir, protoCsFiles, ".cs", stdout);
                if (opt.HasProtoSizesOut)
                {
                    string? sizesDir = Path.GetDirectoryName(opt.ProtoSizesOut);
                    if (!string.IsNullOrEmpty(sizesDir))
                        Directory.CreateDirectory(sizesDir);
                    FileUtil.WriteFileIfChanged(opt.ProtoSizesOut, FileUtil.Utf8(protoSizes));
                }

                // --sheets는 일부 시트만 만들므로 낡은 파일 정리와 상태 기록은 전체 변환일 때만 한다.
                if (!opt.HasSheets)
                {
                    foreach (GeneratedFile f in cppFiles) produced.Add(Stamp.AbsolutePath(Path.Combine(opt.CppOutDir, f.Name)));
                    foreach (GeneratedFile f in csFiles) produced.Add(Stamp.AbsolutePath(Path.Combine(opt.CsOutDir, f.Name)));
                    foreach (GeneratedFile f in protoCsFiles) produced.Add(Stamp.AbsolutePath(Path.Combine(opt.ProtoCsOutDir, f.Name)));
                    if (opt.HasProtoSizesOut) produced.Add(Stamp.AbsolutePath(opt.ProtoSizesOut));

                    Stamp.RemoveStaleJson(opt, previous, producedJson, stdout);

                    string stampText = signature + "src=" + sourceStamp + "\n";
                    foreach (string p in produced) stampText += p + "\n";
                    FileUtil.WriteFileIfChanged(Path.Combine(opt.OutDir, Stamp.FileName), FileUtil.Utf8(stampText));      // 모든 출력이 끝난 뒤 마지막에 쓴다
                }
                return ExitOk;
            }
            catch (Exception e)
            {
                // 엑셀이 다른 프로그램(Excel 등)에 열려 있어 읽지 못하는 경우: 같은 인자로 만든 출력이 남아 있으면 그것을 쓴다.
                if (!opened && opt.Incremental && (File.Exists(opt.XlsxPath) || Directory.Exists(opt.XlsxPath)) && Stamp.Matches(opt, signature, null))
                {
                    stderr.Write("warning: 엑셀을 읽지 못해 이전에 생성한 파일을 그대로 사용합니다 (" + e.Message + ")\n");
                    return ExitOk;
                }

                stderr.Write("error: " + e.Message + "\n");
                return ExitUsage;
            }
        }
    }
}
