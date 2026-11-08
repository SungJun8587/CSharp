using System;
using System.Collections.Generic;

namespace XlsxConverter
{
    /// <summary>기본 타입(int, uint, float, string, bool) 하나에 대한 모든 매핑. 타입을 추가/변경할 때는 이 표만 고친다.</summary>
    public sealed class TypeInfo
    {
        public string Name { get; }              // 엑셀 헤더/Const 시트에서 쓰는 이름
        public string CppType { get; }           // 행 구조체 멤버 타입
        public string CppConstType { get; }      // constexpr 상수 타입
        public string CsType { get; }
        public string Getter { get; }            // rapidjson 값 접근자
        public string SpecType { get; }          // EFieldType 열거자 (정수 계열은 범위와 함께 쓴다)
        public string CppDefault { get; }        // 멤버 기본 초기화 (빈 문자열이면 초기화 없음)
        public bool IsInteger { get; }           // 범위 [min..max]를 쓸 수 있는 타입
        public long MinV { get; }                // IsInteger일 때 표현 범위
        public long MaxV { get; }

        public TypeInfo(string name, string cppType, string cppConstType, string csType, string getter, string specType, string cppDefault, bool isInteger, long minV, long maxV)
        {
            Name = name; CppType = cppType; CppConstType = cppConstType; CsType = csType; Getter = getter;
            SpecType = specType; CppDefault = cppDefault; IsInteger = isInteger; MinV = minV; MaxV = maxV;
        }

        private static readonly TypeInfo[] Table =
        {
            //           name      cppType     cppConstType        csType    getter          specType  cppDefault     isInteger  min                   max
            new TypeInfo("int",    "int32",    "std::int32_t",     "int",    "GetInt()",     "Int",    " = 0",        true,      -2147483647L - 1,     2147483647L),
            new TypeInfo("uint",   "uint32",   "std::uint32_t",    "uint",   "GetUint()",    "UInt",   " = 0",        true,      0,                    4294967295L),
            new TypeInfo("float",  "double",   "double",           "double", "GetDouble()",  "Number", " = 0",        false,     0,                    0),
            new TypeInfo("string", "_tstring", "const TCHAR*",     "string", "GetString()",  "String", "",            false,     0,                    0),
            new TypeInfo("bool",   "bool",     "bool",             "bool",   "GetBool()",    "Bool",   " = false",    false,     0,                    0),
        };

        public static TypeInfo? Find(string name)
        {
            foreach (TypeInfo t in Table)
                if (name == t.Name) return t;
            return null;
        }
    }

    /// <summary>Enum 기반형(Base 열) 하나에 대한 매핑.</summary>
    public sealed class BaseInfo
    {
        public string Name { get; }
        public string CppType { get; }
        public string CsType { get; }
        public long MinV { get; }
        public long MaxV { get; }

        private BaseInfo(string name, string cppType, string csType, long minV, long maxV)
        {
            Name = name; CppType = cppType; CsType = csType; MinV = minV; MaxV = maxV;
        }

        private static readonly BaseInfo[] Table =
        {
            new BaseInfo("uint8",  "std::uint8_t",  "byte",   0,                 255),
            new BaseInfo("uint16", "std::uint16_t", "ushort", 0,                 65535),
            new BaseInfo("uint32", "std::uint32_t", "uint",   0,                 4294967295L),
            new BaseInfo("int32",  "std::int32_t",  "int",    -2147483647L - 1,  2147483647L),
        };

        public static BaseInfo? Find(string name)
        {
            foreach (BaseInfo b in Table)
                if (name == b.Name) return b;
            return null;
        }
    }

    /// <summary>C++/C# 식별자 판별.</summary>
    public static class Identifier
    {
        /// <summary>C++/C# 식별자로 쓸 수 있는 이름인지 확인합니다. (영문/숫자/_, 숫자로 시작 불가)</summary>
        public static bool IsPlain(string s)
        {
            if (s.Length == 0 || AsciiChar.IsDigit(s[0])) return false;
            foreach (char c in s)
                if (!(AsciiChar.IsUpper(c) || AsciiChar.IsLower(c) || AsciiChar.IsDigit(c) || c == '_')) return false;
            return true;
        }
    }

    /// <summary>헤더 칸 하나("Name:type[min..max]?")의 해석 결과.</summary>
    public sealed class FieldDef
    {
        public int Col { get; set; }                      // 0-base 열 번호
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;  // int, uint, float, string, bool, enum
        public string EnumName { get; set; } = string.Empty;  // type이 enum일 때 Enum 시트에 정의된 enum 이름
        public bool Optional { get; set; }                // '?' 접미사. 빈 칸이면 JSON 필드를 생략한다
        public bool HasMin { get; set; }                  // [min..max]의 min 지정 여부 (int, uint만)
        public bool HasMax { get; set; }
        public long MinV { get; set; }
        public long MaxV { get; set; }
    }

    /// <summary>시트 하나의 스키마. 시트 이름이 곧 행 구조체 이름(S&lt;Name&gt;Row)과 JSON 파일 이름이 된다.</summary>
    public sealed class SheetSchema
    {
        public string Name { get; set; } = string.Empty;
        public List<FieldDef> Fields { get; set; } = new List<FieldDef>();
    }

    /// <summary>Enum 시트의 한 행.</summary>
    public sealed class EnumMember
    {
        public string Name { get; set; } = string.Empty;
        public long Value { get; set; }
        public string Comment { get; set; } = string.Empty;
    }

    /// <summary>Enum 시트에서 같은 Enum 이름을 가진 행들. 멤버 순서는 시트의 행 순서다.</summary>
    public sealed class EnumDef
    {
        public string Name { get; set; } = string.Empty;
        public string Base { get; set; } = "uint8";        // uint8, uint16, uint32, int32
        public string Comment { get; set; } = string.Empty; // enum 설명 (PacketEnum 시트의 EnumComment 열)
        public List<EnumMember> Members { get; } = new List<EnumMember>();

        private HashSet<string>? _names;                  // 멤버 이름 조회용. 멤버 수가 달라지면 다시 만든다
        private int _namesCount;

        /// <summary>멤버 이름이 있는지 확인합니다. (행마다 부르므로 해시로 찾는다)</summary>
        public bool HasMember(string name)
        {
            if (_names == null || _namesCount != Members.Count)
            {
                _names = new HashSet<string>(StringComparer.Ordinal);
                foreach (EnumMember m in Members) _names.Add(m.Name);
                _namesCount = Members.Count;
            }
            return _names.Contains(name);
        }
    }

    /// <summary>Const 시트의 한 행.</summary>
    public sealed class ConstDef
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;   // int, uint, float, string, bool
        public string Literal { get; set; } = string.Empty; // 값의 JSON 표기 (문자열은 따옴표 포함)
        public string Comment { get; set; } = string.Empty;
    }

    /// <summary>데이터 시트와 별개로 엑셀이 정의하는 형식들. (Enum, Const 시트)</summary>
    public sealed class Definitions
    {
        public List<EnumDef> Enums { get; } = new List<EnumDef>();
        public List<ConstDef> Consts { get; } = new List<ConstDef>();

        public EnumDef? FindEnum(string name)
        {
            foreach (EnumDef e in Enums)
                if (e.Name == name) return e;
            return null;
        }
    }

    /// <summary>생성된 파일 하나. 이름은 출력 폴더 기준이고 내용에는 BOM이 없다. (쓸 때 붙인다)</summary>
    public sealed class GeneratedFile
    {
        public string Name { get; }
        public string Content { get; }
        public GeneratedFile(string name, string content) { Name = name; Content = content; }
    }
}
