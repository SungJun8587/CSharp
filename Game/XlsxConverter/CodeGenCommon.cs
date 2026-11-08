using System;
using System.Collections.Generic;
using System.Text;

namespace XlsxConverter
{
    /// <summary>
    /// RowGenerator / PacketGenerator가 함께 쓰는 코드 생성 도우미.
    /// C#/C++ 예약어와 이름 변환, 줄 단위 문자열 처리, enum 선언 출력, 줄 끝 주석 접미사를 한곳에 둔다.
    /// </summary>
    public static class CodeGenCommon
    {
        private static readonly HashSet<string> CsKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
            "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
            "using", "virtual", "void", "volatile", "while",
        };

        private static readonly HashSet<string> CppKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char", "char8_t",
            "char16_t", "char32_t", "class", "co_await", "co_return", "co_yield", "compl", "concept", "const", "const_cast", "consteval",
            "constexpr", "constinit", "continue", "decltype", "default", "delete", "do", "double", "dynamic_cast", "else", "enum",
            "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long", "mutable",
            "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq", "private", "protected", "public",
            "register", "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast",
            "struct", "switch", "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
            "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq",
            "min", "max", "near", "far", "interface", "small",
        };

        private static readonly HashSet<string> CppMacros = new HashSet<string>(StringComparer.Ordinal)
        {
            "ERROR", "NO_ERROR", "IN", "OUT", "OPTIONAL", "TRUE", "FALSE", "NULL", "DELETE", "CONST", "VOID", "NEAR", "FAR",
            "ABSOLUTE", "RELATIVE", "TRANSPARENT", "OPAQUE", "INFINITE", "IGNORE", "CALLBACK", "PASCAL", "CDECL", "WINAPI", "APIENTRY",
            // 문자셋에 따라 A/W 이름으로 바뀌는 API 이름
            "GetMessage", "SendMessage", "PostMessage", "DrawText", "LoadString", "CreateFile", "DeleteFile", "CopyFile", "MoveFile",
            "FindResource", "GetObject", "GetClassName", "CreateEvent", "CreateMutex",
        };

        /// <summary>C# 예약어인지 확인합니다. (식별자로 쓰려면 @ 접두가 필요하고, 매개변수 이름으로는 쓸 수 없다)</summary>
        public static bool IsCsKeyword(string s) => CsKeywords.Contains(s);

        /// <summary>식별자로 쓰려면 @ 접두가 필요한 C# 이름이면 붙여 돌려줍니다.</summary>
        public static string CsIdent(string s) => IsCsKeyword(s) ? "@" + s : s;

        /// <summary>C++ 예약어(C++20)와, Windows 헤더가 매크로로 정의하는 소문자 이름(min, max, near, far, interface, small)인지 확인합니다.</summary>
        public static bool IsCppKeyword(string s) => CppKeywords.Contains(s);

        /// <summary>Windows 헤더(windows.h)가 매크로로 정의하는 이름인지 확인합니다. (대소문자를 구분한다: ERROR는 걸리고 Error는 안 걸린다)</summary>
        public static bool IsCppMacroName(string s) => CppMacros.Contains(s);

        /// <summary>생성 C++ 코드에서 enum, 멤버, 구조체 필드, 패킷 이름으로 그대로 쓸 수 없는 이름인지 확인합니다. (키워드 또는 Windows 매크로)</summary>
        public static bool IsCppReservedName(string s) => IsCppKeyword(s) || IsCppMacroName(s);

        /// <summary>첫 글자를 대문자로 바꿉니다. (C# 멤버 이름)</summary>
        public static string Pascal(string s)
        {
            if (s.Length == 0) return s;
            return AsciiChar.ToUpper(s[0]) + s.Substring(1);
        }

        /// <summary>열거형 이름의 C# 형태: 앞의 'E' + 대문자(EGatewayResult)이면 E를 뗍니다.</summary>
        public static string CsEnumName(string name)
        {
            if (name.Length > 1 && name[0] == 'E' && AsciiChar.IsUpper(name[1])) return name.Substring(1);
            return name;
        }

        /// <summary>상수 이름의 C# 형태: 앞의 'k' + 대문자(kMaxClientPacketBytes)이면 k를 뗍니다.</summary>
        public static string CsConstName(string name)
        {
            if (name.Length > 1 && name[0] == 'k' && AsciiChar.IsUpper(name[1])) return name.Substring(1);
            return name;
        }

        /// <summary>C# 네임스페이스 형식(점으로 구분된 식별자들)인지 확인합니다.</summary>
        public static bool IsValidNamespace(string ns)
        {
            if (ns.Length == 0) return false;
            int b = 0;
            while (b <= ns.Length)
            {
                int e = ns.IndexOf('.', b);
                if (e < 0) e = ns.Length;
                if (!Identifier.IsPlain(ns.Substring(b, e - b))) return false;
                b = e + 1;
            }
            return true;
        }

        /// <summary>줄 단위로 나눕니다. (끝의 빈 줄도 한 줄로 센다: "a\nb" -> {"a", "b"}, "" -> {""})</summary>
        public static List<string> Lines(string text)
        {
            var v = new List<string>();
            int pos = 0;
            while (pos <= text.Length)
            {
                int e = text.IndexOf('\n', pos);
                if (e < 0) e = text.Length;
                v.Add(text.Substring(pos, e - pos));
                pos = e + 1;
            }
            return v;
        }

        /// <summary>생성 파일의 머리말에 들어가는 표식. 낡은 생성 파일을 지울 때 이 표식이 있는 파일만 대상으로 삼는다.</summary>
        public const string GeneratedMarker = "자동 생성 파일";

        /// <summary>"XlsxConverter가 GameData 엑셀의 &lt;what&gt; 시트로부터 만듭니다." 안내 줄.</summary>
        public static string SourceNote(string what) => "XlsxConverter가 GameData 엑셀의 " + what + " 시트로부터 만듭니다.";

        // "a\nb" -> prefix + "a\n" + prefix + "b\n"
        private static string PrefixLines(string text, string prefix)
        {
            var sb = new StringBuilder();
            foreach (string line in Lines(text))
                sb.Append(prefix).Append(line).Append('\n');
            return sb.ToString();
        }

        /// <summary>C++ 생성 파일의 머리말 블록. 구분선 사이에 "&lt;file&gt; : 자동 생성 파일 ..." 한 줄, 빈 주석 줄, 설명 줄들이 온다.</summary>
        public static string CppBanner(string file, string description)
        {
            const string divider = "//***************************************************************************\n";
            return divider + "// " + file + " : " + GeneratedMarker + " - 직접 수정하지 마세요.\n//\n" + PrefixLines(description, "// ") + divider;
        }

        /// <summary>C# 생성 파일의 머리말 블록. (&lt;auto-generated&gt; 표식)</summary>
        public static string CsBanner(string file, string description)
        {
            return "// <auto-generated>\n// " + file + " : " + GeneratedMarker + " - 직접 수정하지 마세요.\n" + PrefixLines(description, "// ") + "// </auto-generated>\n";
        }

        /// <summary>줄 끝 주석("\t// 설명")을 만듭니다. 주석이 비어 있으면 빈 문자열입니다.</summary>
        public static string CommentTail(string comment) => comment.Length == 0 ? string.Empty : "\t// " + comment;

        /// <summary>C++ enum class 선언 한 개를 만듭니다. (Enum 시트와 PacketEnum 시트가 같은 모양을 쓴다)</summary>
        public static string CppEnum(EnumDef e, string indent)
        {
            BaseInfo bi = BaseInfo.Find(e.Base)!;
            CodeBuilder s = indent + "enum class " + e.Name + " : " + bi.CppType + "\n" + indent + "{\n";
            foreach (EnumMember m in e.Members)
                s += indent + "\t" + m.Name + " = " + m.Value + "," + CommentTail(m.Comment) + "\n";
            return s + indent + "};\n";
        }

        /// <summary>C# enum 선언 한 개를 만듭니다. 멤버 이름이 C# 예약어면 @를 붙인다.</summary>
        /// <param name="csName">C# 쪽 enum 이름 (호출하는 쪽이 정한다: Enum 시트는 그대로, PacketEnum은 CsEnumName)</param>
        public static string CsEnum(EnumDef e, string csName, string indent)
        {
            BaseInfo bi = BaseInfo.Find(e.Base)!;
            CodeBuilder s = indent + "public enum " + csName + " : " + bi.CsType + "\n" + indent + "{\n";
            foreach (EnumMember m in e.Members)
                s += indent + "\t" + CsIdent(m.Name) + " = " + m.Value + "," + CommentTail(m.Comment) + "\n";
            return s + indent + "}\n";
        }
    }
    /// <summary>
    /// 생성 코드를 이어 붙이는 버퍼. `text += "..."` 문법 그대로 쓰되 문자열을 매번 복사하지 않는다.
    /// (string의 +=는 이어 붙일 때마다 전체를 복사해 패킷이 수천 개면 시간이 제곱으로 늘어난다)
    /// string과 서로 암묵 변환되므로 string을 받고 돌려주는 코드에 그대로 넘길 수 있다.
    /// </summary>
    internal sealed class CodeBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();

        public static implicit operator CodeBuilder(string initial) { var b = new CodeBuilder(); b._sb.Append(initial); return b; }
        public static implicit operator string(CodeBuilder b) => b._sb.ToString();
        public static CodeBuilder operator +(CodeBuilder b, string text) { b._sb.Append(text); return b; }
        public override string ToString() => _sb.ToString();
    }
}
