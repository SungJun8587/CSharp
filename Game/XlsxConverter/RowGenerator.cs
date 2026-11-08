using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace XlsxConverter
{
    /// <summary>시트 스키마 -> C++ 행 구조체 헤더, C# 행/테이블 클래스.</summary>
    public static class RowGenerator
    {
        private static string Inv(long v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 필드 이름에서 C++ 멤버 이름을 만듭니다. 앞쪽 대문자 묶음을 소문자로 바꾸고(HPMax -> hpMax), C++ 예약어면 '_'를 붙입니다.
        /// </summary>
        public static string MemberName(string fieldName)
        {
            char[] s = fieldName.ToCharArray();
            int run = 0;
            while (run < s.Length && AsciiChar.IsUpper(s[run])) ++run;

            // 대문자 묶음 뒤에 소문자가 이어지면 마지막 대문자는 다음 단어의 시작이므로 남긴다. (HPMax -> hpMax)
            int lower = run;
            if (run > 1 && run < s.Length && AsciiChar.IsLower(s[run])) lower = run - 1;
            for (int i = 0; i < lower; ++i)
                s[i] = AsciiChar.ToLower(s[i]);

            string r = new string(s);
            if (CodeGenCommon.IsCppKeyword(r)) r += '_';
            return r;
        }

        // 시트 클래스와 상수 클래스 이름. enum 이름과 겹치는지는 호출하는 쪽이 따로 검사한다.
        private static HashSet<string> CsSheetTypeNames(IEnumerable<SheetSchema> schemas)
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { "GameConsts" };
            foreach (SheetSchema s in schemas)
            {
                names.Add(s.Name + "Row");
                names.Add(s.Name + "Table");
            }
            return names;
        }

        /// <summary>C# 쪽에서 만들어지는 형식 이름들(시트 클래스, GameConsts, Enum 시트의 enum). 패킷 프로토콜 형식과 겹치는지 검사할 때 쓴다.</summary>
        public static HashSet<string> CsTypeNames(IEnumerable<SheetSchema> schemas, Definitions defs)
        {
            HashSet<string> names = CsSheetTypeNames(schemas);
            foreach (EnumDef e in defs.Enums) names.Add(e.Name);
            return names;
        }

        // ------------------------------------------------------------------ C++

        private static string CppType(string type) => TypeInfo.Find(type)!.CppType;

        private static string CppFieldType(FieldDef f) => f.Type == "enum" ? "GameDataEnums::" + f.EnumName : CppType(f.Type);

        private static string DefaultInit(FieldDef f, Definitions defs)
        {
            if (f.Type == "enum")
            {
                EnumDef? e = defs.FindEnum(f.EnumName);
                return " = GameDataEnums::" + f.EnumName + "::" + (e != null && e.Members.Count > 0 ? e.Members[0].Name : string.Empty);
            }
            return TypeInfo.Find(f.Type)!.CppDefault;
        }

        // int64 리터럴. INT32_MIN처럼 부호 있는 최솟값도 안전하게 쓴다.
        private static string Literal(long v) => Inv(v) + "ll";

        private static string SpecLine(FieldDef f)
        {
            string name = "_T(\"" + f.Name + "\")";
            string line = "{ " + name + ", ";

            // enum은 JSON에 멤버 이름 문자열로 저장된다.
            TypeInfo info = f.Type == "enum" ? TypeInfo.Find("string")! : TypeInfo.Find(f.Type)!;
            if (info.IsInteger)
            {
                long lo = f.HasMin ? f.MinV : info.MinV;
                long hi = f.HasMax ? f.MaxV : info.MaxV;
                line += "EFieldType::" + info.SpecType + ", " + Literal(lo) + ", " + Literal(hi);
            }
            else
            {
                line += "EFieldType::" + info.SpecType;
                if (f.Optional)
                    line += ", (std::numeric_limits<int64>::min)(), (std::numeric_limits<int64>::max)()";
            }

            if (f.Optional) line += ", false";
            return line + " },";
        }

        private static string ToJsonLine(FieldDef f, string member)
        {
            string key = "_T(\"" + f.Name + "\")";
            if (f.Type == "string")
                return "value.AddMember(" + key + ", _tValue(this->" + member + ".c_str(), allocator), allocator);";
            if (f.Type == "enum")
                return "value.AddMember(" + key + ", _tValue(GameDataEnums::ToString(this->" + member + "), allocator), allocator);";
            return "value.AddMember(" + key + ", this->" + member + ", allocator);";
        }

        private static string FromJsonLine(FieldDef f, string member)
        {
            string key = "_T(\"" + f.Name + "\")";
            if (f.Type == "enum")
            {
                // 알 수 없는 이름이면 enumOk를 내린다. (JSON을 손으로 고친 경우를 로더가 잡을 수 있게)
                string parse = "!GameDataEnums::TryParse(value[" + key + "].GetString(), this->" + member + ")";
                return f.Optional ? "if( value.HasMember(" + key + ") && " + parse + " ) this->enumOk = false;"
                    : "if( " + parse + " ) this->enumOk = false;";
            }
            string getter = TypeInfo.Find(f.Type)!.Getter;
            if (!f.Optional)
                return "this->" + member + " = value[" + key + "]." + getter + ";";

            // 선택 열: 없으면 멤버의 기본값을 그대로 둔다.
            return "if( value.HasMember(" + key + ") ) this->" + member + " = value[" + key + "]." + getter + ";";
        }

        private static string CppConstType(string type) => TypeInfo.Find(type)!.CppConstType;

        // 값의 JSON 표기를 C++ 리터럴로. 문자열만 _T()로 감싼다.
        private static string CppConstLiteral(ConstDef c) => c.Type == "string" ? "_T(" + c.Literal + ")" : c.Literal;

        // Enum/Const 시트 이름이 C++ 규칙에 맞는지 확인합니다.
        private static bool ValidateDefinitionsForCpp(Definitions defs, List<string> errors)
        {
            int before = errors.Count;
            foreach (EnumDef e in defs.Enums)
            {
                if (CodeGenCommon.IsCppReservedName(e.Name)) errors.Add("Enum " + e.Name + ": C++ 키워드나 Windows 매크로 이름은 enum 이름으로 쓸 수 없습니다");
                foreach (EnumMember m in e.Members)
                    if (CodeGenCommon.IsCppReservedName(m.Name)) errors.Add("Enum " + e.Name + "." + m.Name + ": C++ 키워드나 Windows 매크로 이름은 멤버 이름으로 쓸 수 없습니다 (ERROR, DELETE, IN, OUT 등은 Error처럼 바꾸세요)");
            }
            return errors.Count == before;
        }

        // 의존성 없는 enum 정의 헤더(Enums.gen.h). SkillData.h처럼 JSON과 무관한 곳에서도 쓴다.
        private static string CppEnumsHeader(Definitions defs)
        {
            CodeBuilder t = string.Empty;
            t += "\n" + CodeGenCommon.CppBanner("Enums.gen.h", CodeGenCommon.SourceNote("Enum") + " 다른 헤더에 의존하지 않습니다.") + "\n";
            t += "#ifndef GAMEDATAENUMS_GEN_H\n#define GAMEDATAENUMS_GEN_H\n\n#include <cstdint>\n\n";
            t += "namespace GameDataEnums\n{\n";
            bool first = true;
            foreach (EnumDef e in defs.Enums)
            {
                if (!first) t += "\n";
                first = false;
                t += CodeGenCommon.CppEnum(e, "\t");
            }
            t += "}\n\n#endif // ndef GAMEDATAENUMS_GEN_H\n";
            return t;
        }

        // enum <-> 문자열 변환 헤더(EnumStrings.gen.h). TCHAR, _T, _tcscmp가 필요하다. (pch가 제공)
        private static string CppEnumStringsHeader(Definitions defs)
        {
            CodeBuilder t = string.Empty;
            t += "\n" + CodeGenCommon.CppBanner("EnumStrings.gen.h", "enum 멤버 이름 <-> 문자열 변환. JSON에는 멤버 이름이 그대로 저장된다. TCHAR, _T, _tcscmp가 필요하다.") + "\n";
            t += "#ifndef GAMEDATAENUMSTRINGS_GEN_H\n#define GAMEDATAENUMSTRINGS_GEN_H\n\n#include \"Enums.gen.h\"\n\n";
            t += "namespace GameDataEnums\n{\n";
            bool first = true;
            foreach (EnumDef e in defs.Enums)
            {
                if (!first) t += "\n";
                first = false;
                t += "\tinline const TCHAR* ToString(" + e.Name + " v)\n\t{\n\t\tswitch( v )\n\t\t{\n";
                foreach (EnumMember m in e.Members)
                    t += "\t\tcase " + e.Name + "::" + m.Name + ": return _T(\"" + m.Name + "\");\n";
                t += "\t\t}\n\t\treturn _T(\"\");\n\t}\n\n";
                t += "\t// 이름이 정확히 일치하면 out을 채우고 true. 아니면 out은 그대로 두고 false.\n";
                t += "\tinline bool TryParse(const TCHAR* s, " + e.Name + "& out)\n\t{\n\t\tif( s == nullptr )\n\t\t\treturn false;\n";
                foreach (EnumMember m in e.Members)
                    t += "\t\tif( _tcscmp(s, _T(\"" + m.Name + "\")) == 0 ) { out = " + e.Name + "::" + m.Name + "; return true; }\n";
                t += "\t\treturn false;\n\t}\n";
            }
            t += "}\n\n#endif // ndef GAMEDATAENUMSTRINGS_GEN_H\n";
            return t;
        }

        private static string CppConstsHeader(Definitions defs)
        {
            CodeBuilder t = string.Empty;
            t += "\n" + CodeGenCommon.CppBanner("Consts.gen.h", CodeGenCommon.SourceNote("Const") + " 문자열 상수가 있으면 TCHAR, _T가 필요하다. (pch가 제공)") + "\n";
            t += "#ifndef GAMEDATACONSTS_GEN_H\n#define GAMEDATACONSTS_GEN_H\n\n#include <cstdint>\n\n";
            t += "namespace GameDataConsts\n{\n";
            foreach (ConstDef c in defs.Consts)
                t += "\tconstexpr " + CppConstType(c.Type) + " k" + c.Name + " = " + CppConstLiteral(c) + ";" + CodeGenCommon.CommentTail(c.Comment) + "\n";
            t += "}\n\n#endif // ndef GAMEDATACONSTS_GEN_H\n";
            return t;
        }

        /// <summary>시트별 C++ 행 구조체 헤더(&lt;시트&gt;.gen.h), Enums/EnumStrings/Consts 헤더, 대표 헤더(GeneratedCommon.h)를 만듭니다.</summary>
        /// <returns>오류가 없으면 true. 실패하면 output은 건드리지 않는다</returns>
        public static bool Generate(IReadOnlyList<SheetSchema> schemas, Definitions defs, List<GeneratedFile> output, List<string> errors)
        {
            int errBefore = errors.Count;
            var files = new List<GeneratedFile>();

            // 형식 정의(Enum, Const)가 먼저 와야 대표 헤더가 이 순서로 포함한다.
            if (!ValidateDefinitionsForCpp(defs, errors)) return false;
            if (defs.Enums.Count > 0)
            {
                files.Add(new GeneratedFile("Enums.gen.h", CppEnumsHeader(defs)));
                files.Add(new GeneratedFile("EnumStrings.gen.h", CppEnumStringsHeader(defs)));
            }
            if (defs.Consts.Count > 0)
                files.Add(new GeneratedFile("Consts.gen.h", CppConstsHeader(defs)));

            var sheetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (SheetSchema s in schemas)
            {
                if (!Identifier.IsPlain(s.Name) || !sheetNames.Add(s.Name))
                {
                    errors.Add(s.Name + ": 시트 이름은 C++ 식별자(영문/숫자/_, 숫자로 시작 불가)여야 행 구조체를 만들 수 있습니다");
                    continue;
                }

                var members = new List<string>();
                var used = new HashSet<string>(StringComparer.Ordinal) { "kFile", "kTable", "Specs", "ToJSON", "FromJSON", "enumOk" };    // 정적 멤버와 겹치면 안 된다
                bool ok = true;
                foreach (FieldDef f in s.Fields)
                {
                    string m = Identifier.IsPlain(f.Name) ? MemberName(f.Name) : string.Empty;
                    if (m.Length == 0 || !used.Add(m))
                    {
                        errors.Add(s.Name + "." + f.Name + ": 필드명이 C++ 식별자가 아니거나 다른 필드와 멤버 이름이 겹칩니다 (" + (m.Length == 0 ? f.Name : m) + ")");
                        ok = false;
                        continue;
                    }
                    members.Add(m);
                }
                if (!ok) continue;

                string guard = "GAMEDATAROWS_";
                foreach (char c in s.Name)
                    guard += AsciiChar.ToUpper(c);
                guard += "_GEN_H";

                string st = "S" + s.Name + "Row";
                CodeBuilder text = string.Empty;
                text += "\n" + CodeGenCommon.CppBanner(s.Name + ".gen.h", CodeGenCommon.SourceNote(s.Name) + " 시트를 고치려면 엑셀을 고치고 XlsxConverter를 다시 실행하세요.\n" + st + "는 kFile/kTable/Specs/ToJSON/FromJSON을 갖습니다.") + "\n";
                text += "#ifndef " + guard + "\n";
                text += "#define " + guard + "\n";
                text += "\n";
                text += "#include <initializer_list>\n";
                text += "#include <limits>\n";
                bool hasEnum = s.Fields.Any(f => f.Type == "enum");
                if (hasEnum)
                {
                    text += "\n";
                    text += "#include \"Enums.gen.h\"\n";
                    text += "#include \"EnumStrings.gen.h\"\n";
                }
                text += "\n";
                text += "namespace GameDataRows\n";
                text += "{\n";
                text += "\t// " + s.Name + ".json의 한 행.\n";
                text += "\tstruct " + st + "\n";
                text += "\t{\n";
                for (int i = 0; i < s.Fields.Count; ++i)
                {
                    FieldDef f = s.Fields[i];
                    text += "\t\t" + CppFieldType(f) + "\t" + members[i] + DefaultInit(f, defs) + ";\n";
                }
                if (hasEnum)
                    text += "\t\tbool\tenumOk = true;\t\t// FromJSON에서 enum 이름을 모르는 값을 만나면 false\n";
                text += "\n";
                text += "\t\tstatic constexpr const TCHAR* kFile = _T(\"" + s.Name + ".json\");\n";
                text += "\t\tstatic constexpr const TCHAR* kTable = _T(\"" + s.Name + "\");\n";
                text += "\n";
                text += "\t\tstatic std::initializer_list<SFieldSpec> Specs()\n";
                text += "\t\t{\n";
                text += "\t\t\tstatic const std::initializer_list<SFieldSpec> specs =\n";
                text += "\t\t\t{\n";
                foreach (FieldDef f in s.Fields)
                    text += "\t\t\t\t" + SpecLine(f) + "\n";
                text += "\t\t\t};\n";
                text += "\t\t\treturn specs;\n";
                text += "\t\t}\n";
                text += "\n";
                text += "\t\tvoid ToJSON(_tValue& value, _tDocument::AllocatorType& allocator) const\n";
                text += "\t\t{\n";
                text += "\t\t\tvalue.SetObject();\n";
                for (int i = 0; i < s.Fields.Count; ++i)
                    text += "\t\t\t" + ToJsonLine(s.Fields[i], members[i]) + "\n";
                text += "\t\t}\n";
                text += "\n";
                text += "\t\tvoid FromJSON(const _tValue& value)\n";
                text += "\t\t{\n";
                for (int i = 0; i < s.Fields.Count; ++i)
                    text += "\t\t\t" + FromJsonLine(s.Fields[i], members[i]) + "\n";
                text += "\t\t}\n";
                text += "\t};\n";
                text += "}\n";
                text += "\n";
                text += "#endif // ndef " + guard + "\n";

                files.Add(new GeneratedFile(s.Name + ".gen.h", text));
            }

            // 시트별 헤더를 한 번에 포함하는 대표 헤더. 사용하는 쪽은 이 파일만 include하면 되고, 시트가 늘거나 줄어도 include를 고치지 않는다.
            if (errors.Count == errBefore)
            {
                CodeBuilder text = string.Empty;
                text += "\n" + CodeGenCommon.CppBanner("GeneratedCommon.h", "XlsxConverter가 만든 시트별 행 구조체 헤더(<시트>.gen.h)를 한곳에서 포함합니다.") + "\n";
                text += "#ifndef GAMEDATAROWS_GENERATEDCOMMON_H\n";
                text += "#define GAMEDATAROWS_GENERATEDCOMMON_H\n";
                text += "\n";
                foreach (GeneratedFile f in files)
                    text += "#include \"" + f.Name + "\"\n";
                text += "\n";
                text += "#endif // ndef GAMEDATAROWS_GENERATEDCOMMON_H\n";
                files.Add(new GeneratedFile("GeneratedCommon.h", text));
            }

            if (errors.Count != errBefore) return false;
            output.Clear();
            output.AddRange(files);
            return true;
        }

        // ------------------------------------------------------------------ C#

        private static string CsType(string type) => TypeInfo.Find(type)!.CsType;

        private static string CsFieldType(FieldDef f) => f.Type == "enum" ? f.EnumName : CsType(f.Type);

        private static string CsLiteral(string type, long v) => Inv(v) + (type == "uint" ? "u" : "");

        // 범위 검사식. 헤더에 명시한 쪽만 만든다. (명시하지 않은 쪽은 int/uint 자체가 보장)
        private static string CsCheck(FieldDef f, string prop)
        {
            string cond = string.Empty;
            if (f.HasMin) cond += "this." + prop + " < " + CsLiteral(f.Type, f.MinV);
            if (f.HasMax) cond += (cond.Length == 0 ? "" : " || ") + "this." + prop + " > " + CsLiteral(f.Type, f.MaxV);
            if (cond.Length == 0) return string.Empty;

            string range = (f.HasMin ? Inv(f.MinV) : string.Empty) + ".." + (f.HasMax ? Inv(f.MaxV) : string.Empty);
            return "if( " + cond + " ) errors.Add(where + \"." + f.Name + "=\" + this." + prop + " + \" (허용 범위 " + range + ")\");";
        }

        /// <summary>Enums.gen.cs, Consts.gen.cs, 시트마다 &lt;시트&gt;.gen.cs(&lt;시트&gt;Row, &lt;시트&gt;Table 클래스)를 만듭니다.</summary>
        /// <returns>오류가 없으면 true. 실패하면 output은 건드리지 않는다</returns>
        public static bool GenerateCs(IReadOnlyList<SheetSchema> schemas, Definitions defs, string ns, List<GeneratedFile> output, List<string> errors)
        {
            int errBefore = errors.Count;

            // 네임스페이스: 점으로 구분된 식별자들
            if (!CodeGenCommon.IsValidNamespace(ns)) { errors.Add("--cs-namespace '" + ns + "': C# 네임스페이스 형식이 아닙니다"); return false; }

            // 같은 네임스페이스에 만들어지는 형식 이름끼리 겹치면 C# 컴파일이 깨지므로 미리 막는다.
            {
                HashSet<string> typeNames = CsSheetTypeNames(schemas);
                foreach (EnumDef e in defs.Enums)
                    if (!typeNames.Add(e.Name))
                        errors.Add("Enum " + e.Name + ": C# 형식 이름이 시트 클래스(<시트>Row, <시트>Table)나 GameConsts와 겹칩니다");
                foreach (ConstDef c in defs.Consts)
                    if (c.Name == "GameConsts")
                        errors.Add("Const " + c.Name + ": 상수 이름이 GameConsts 클래스 이름과 같을 수 없습니다");
                if (errors.Count != errBefore) return false;
            }

            var files = new List<GeneratedFile>();

            string Header(string file, string what)
                => CodeGenCommon.CsBanner(file, CodeGenCommon.SourceNote(what)) + "\nnamespace " + ns + "\n{\n";

            if (defs.Enums.Count > 0)
            {
                CodeBuilder t = Header("Enums.gen.cs", "Enum");
                bool first = true;
                foreach (EnumDef e in defs.Enums)
                {
                    if (!first) t += "\n";
                    first = false;
                    t += CodeGenCommon.CsEnum(e, CodeGenCommon.CsIdent(e.Name), "\t");
                }
                t += "}\n";
                files.Add(new GeneratedFile("Enums.gen.cs", t));
            }
            if (defs.Consts.Count > 0)
            {
                CodeBuilder t = Header("Consts.gen.cs", "Const");
                t += "\tpublic static class GameConsts\n\t{\n";
                foreach (ConstDef c in defs.Consts)
                    t += "\t\tpublic const " + CsType(c.Type) + " " + CodeGenCommon.CsIdent(c.Name) + " = " + c.Literal + ";" + CodeGenCommon.CommentTail(c.Comment) + "\n";
                t += "\t}\n}\n";
                files.Add(new GeneratedFile("Consts.gen.cs", t));
            }

            var sheetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (SheetSchema s in schemas)
            {
                if (!Identifier.IsPlain(s.Name) || !sheetNames.Add(s.Name))
                {
                    errors.Add(s.Name + ": 시트 이름은 C# 식별자(영문/숫자/_, 숫자로 시작 불가)여야 클래스를 만들 수 있습니다");
                    continue;
                }

                string row = s.Name + "Row", table = s.Name + "Table";
                var reserved = new HashSet<string>(StringComparer.Ordinal) { "FileName", "TableKey", "Validate", row, table };
                bool ok = true;
                foreach (FieldDef f in s.Fields)
                {
                    if (!Identifier.IsPlain(f.Name) || !reserved.Add(f.Name))
                    {
                        errors.Add(s.Name + "." + f.Name + ": 필드명이 C# 식별자가 아니거나 예약 이름(FileName, TableKey, Validate, " + row + ")과 겹칩니다");
                        ok = false;
                    }
                }
                if (!ok) continue;

                CodeBuilder text = string.Empty;
                text += CodeGenCommon.CsBanner(s.Name + ".gen.cs", CodeGenCommon.SourceNote(s.Name) + " 시트를 고치려면 엑셀을 고치고 XlsxConverter를 다시 실행하세요.\n" + row + "(행)와 " + table + "(JSON 최상위 객체)가 있습니다. 속성 이름이 JSON 키와 같아 별도 특성이 필요 없습니다.");
                text += "\n";
                text += "using System.Collections.Generic;\n";
                text += "\n";
                text += "namespace " + ns + "\n";
                text += "{\n";
                text += "\t/// <summary>" + s.Name + ".json의 한 행.</summary>\n";
                text += "\tpublic sealed class " + row + "\n";
                text += "\t{\n";
                text += "\t\tpublic const string FileName = \"" + s.Name + ".json\";\n";
                text += "\t\tpublic const string TableKey = \"" + s.Name + "\";\n";
                text += "\n";
                foreach (FieldDef f in s.Fields)
                {
                    text += "\t\tpublic " + CsFieldType(f) + " " + CodeGenCommon.CsIdent(f.Name) + " { get; set; }";
                    if (f.Type == "string") text += " = string.Empty;";
                    if (f.Type == "enum")
                    {
                        EnumDef? e = defs.FindEnum(f.EnumName);
                        if (e != null && e.Members.Count > 0) text += " = " + f.EnumName + "." + CodeGenCommon.CsIdent(e.Members[0].Name) + ";";
                    }
                    text += "\n";
                }
                text += "\n";
                text += "\t\t/// <summary>헤더의 [min..max]를 벗어난 값을 errors에 덧붙인다.</summary>\n";
                text += "\t\tpublic void Validate(string where, List<string> errors)\n";
                text += "\t\t{\n";
                foreach (FieldDef f in s.Fields)
                {
                    if (f.Type == "enum" || !TypeInfo.Find(f.Type)!.IsInteger) continue;
                    string c = CsCheck(f, CodeGenCommon.CsIdent(f.Name));
                    if (c.Length > 0) text += "\t\t\t" + c + "\n";
                }
                text += "\t\t}\n";
                text += "\t}\n";
                text += "\n";
                text += "\t/// <summary>" + s.Name + ".json의 최상위 객체. {\"" + s.Name + "\": [ ... ]}</summary>\n";
                text += "\tpublic sealed class " + table + "\n";
                text += "\t{\n";
                text += "\t\tpublic List<" + row + "> " + CodeGenCommon.CsIdent(s.Name) + " { get; set; } = new List<" + row + ">();\n";
                text += "\n";
                text += "\t\tpublic void Validate(List<string> errors)\n";
                text += "\t\t{\n";
                text += "\t\t\tfor( int i = 0; i < this." + CodeGenCommon.CsIdent(s.Name) + ".Count; ++i )\n";
                text += "\t\t\t\tthis." + CodeGenCommon.CsIdent(s.Name) + "[i].Validate(\"" + s.Name + "[\" + i + \"]\", errors);\n";
                text += "\t\t}\n";
                text += "\t}\n";
                text += "}\n";

                files.Add(new GeneratedFile(s.Name + ".gen.cs", text));
            }

            if (errors.Count != errBefore) return false;
            output.Clear();
            output.AddRange(files);
            return true;
        }
    }
}
