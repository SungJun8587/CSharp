using System;
using System.Collections.Generic;

namespace XlsxConverter
{
    /// <summary>
    /// PacketField 시트의 Type 하나에 대한 모든 매핑. 타입을 추가/변경할 때는 이 표만 고친다. (와이어 크기, C++/C# 타입, C# 읽기/쓰기 식)
    /// CsWrite/CsWriteEnum의 {v}는 값 식으로 치환된다. CsWriteEnum은 enum으로 선언한 필드의 쓰기 식이며 null이면 CsWrite와 같다.
    /// char 배열은 C# 생성을 지원하지 않으므로 C# 항목이 null이다.
    /// </summary>
    public sealed class PacketTypeInfo
    {
        public string Name { get; }
        public int Bytes { get; }
        public string CppType { get; }
        public string? CsType { get; }
        public string? CsRead { get; }
        public string? CsWrite { get; }
        public string? CsWriteEnum { get; }

        private PacketTypeInfo(string name, int bytes, string cppType, string? csType, string? csRead, string? csWrite, string? csWriteEnum)
        {
            Name = name; Bytes = bytes; CppType = cppType; CsType = csType; CsRead = csRead; CsWrite = csWrite; CsWriteEnum = csWriteEnum;
        }

        private static readonly PacketTypeInfo[] Table =
        {
            //                       name      bytes cpp       cs        read              write                              writeEnum
            new PacketTypeInfo("uint8",  1, "uint8",  "byte",   "r.U8()",         "w.U8((byte){v});",                null),
            new PacketTypeInfo("bool8",  1, "uint8",  "bool",   "r.U8() != 0",    "w.U8({v} ? (byte)1 : (byte)0);",  null),
            new PacketTypeInfo("uint16", 2, "uint16", "ushort", "r.U16()",        "w.U16((ushort){v});",             null),
            new PacketTypeInfo("uint32", 4, "uint32", "uint",   "r.U32()",        "w.U32({v});",                     "w.U32((uint){v});"),
            new PacketTypeInfo("int32",  4, "int32",  "int",    "r.I32()",        "w.U32(unchecked((uint){v}));",    null),
            new PacketTypeInfo("uint64", 8, "uint64", "ulong",  "r.U64()",        "w.U64({v});",                     null),
            new PacketTypeInfo("float",  4, "float",  "float",  "r.F32()",        "w.F32({v});",                     null),
            new PacketTypeInfo("char",   1, "char",   null,     null,             null,                              null),
        };

        public static PacketTypeInfo? Find(string name)
        {
            foreach (PacketTypeInfo t in Table)
                if (name == t.Name) return t;
            return null;
        }
    }

    /// <summary>PacketConst 시트의 한 행. 프로토콜 전역 상수(버전, 크기 상한, 이름 필드 길이 등).</summary>
    public sealed class PacketConst
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;   // uint16, uint32, size
        public ulong Value { get; set; }
        public string Comment { get; set; } = string.Empty; // 비우면 앞 상수의 설명에 이어서 쓴다 (여러 줄은 \n)
    }

    /// <summary>PacketGroup 시트의 한 행. 패킷 번호 구간 하나와 그 구간의 번호 열거형.</summary>
    public sealed class PacketGroup
    {
        public string Name { get; set; } = string.Empty;      // Packet 시트의 Group 열이 가리키는 이름
        public string EnumName { get; set; } = string.Empty;  // C++ 패킷 번호 열거형 이름 (예: EGatewayPacketType)
        public string CsEnum { get; set; } = string.Empty;    // C# 열거형 이름
        public long MinId { get; set; }                       // 이 구간의 번호 범위
        public long MaxId { get; set; } = 65535;
        public string Section { get; set; } = string.Empty;   // 구조체 파일에서 이 구간을 시작하는 구분 주석
        public string Comment { get; set; } = string.Empty;   // 열거형 설명
    }

    /// <summary>PacketField 시트의 한 행. 패킷 본문의 필드 하나(PacketHeader 다음부터 시트 순서대로 이어 붙는다).</summary>
    public sealed class PacketField
    {
        public string Name { get; set; } = string.Empty;      // C++ 멤버 이름 (C#에서는 첫 글자만 대문자로)
        public string Type { get; set; } = string.Empty;      // PacketTypeInfo의 이름
        public string Len { get; set; } = string.Empty;       // char 배열 길이: 숫자 또는 PacketConst 이름
        public string EnumName { get; set; } = string.Empty;  // 값의 의미를 나타내는 PacketEnum 이름
        public string Comment { get; set; } = string.Empty;
        public int Bytes { get; set; }                        // 계산된 크기
    }

    /// <summary>Packet 시트의 한 행과 그 패킷의 필드들.</summary>
    public sealed class PacketDef
    {
        public string Name { get; set; } = string.Empty;      // 예: GwLoginReq (C++ 구조체는 &lt;name&gt;Packet)
        public string Group { get; set; } = string.Empty;
        public long Id { get; set; }
        public string Cs { get; set; } = string.Empty;        // None(기본), Send, Recv, Both
        public string Summary { get; set; } = string.Empty;   // 패킷 번호 열거형의 멤버 설명
        public string Comment { get; set; } = string.Empty;   // 구조체 @brief (여러 줄은 \n)
        public string Detail { get; set; } = string.Empty;    // 구조체 @details (선택, 여러 줄은 \n)
        public List<PacketField> Fields { get; } = new List<PacketField>();
        public int Size { get; set; }                         // 헤더를 포함한 전체 크기 (계산됨)
    }

    /// <summary>패킷 프로토콜 정의 전체. 시트가 하나도 없으면 IsEmpty다.</summary>
    public sealed class PacketSchema
    {
        public List<EnumDef> Enums { get; } = new List<EnumDef>();
        public List<PacketConst> Consts { get; } = new List<PacketConst>();
        public List<PacketGroup> Groups { get; } = new List<PacketGroup>();
        public List<PacketDef> Packets { get; } = new List<PacketDef>();

        public bool IsEmpty => Enums.Count == 0 && Consts.Count == 0 && Groups.Count == 0 && Packets.Count == 0;

        public PacketGroup? FindGroup(string name)
        {
            foreach (PacketGroup g in Groups) if (g.Name == name) return g;
            return null;
        }

        public EnumDef? FindEnum(string name)
        {
            foreach (EnumDef e in Enums) if (e.Name == name) return e;
            return null;
        }

        public PacketConst? FindConst(string name)
        {
            foreach (PacketConst c in Consts) if (c.Name == name) return c;
            return null;
        }

        private readonly Dictionary<string, PacketDef> _packetIndex = new Dictionary<string, PacketDef>(StringComparer.Ordinal);
        private int _packetsIndexed;

        /// <summary>이름으로 패킷을 찾습니다. 행마다 부르므로 색인을 쓴다. (Packets에 새로 추가된 것만 색인에 더하고, 같은 이름이면 먼저 것을 돌려준다)</summary>
        public PacketDef? FindPacket(string name)
        {
            for (; _packetsIndexed < Packets.Count; ++_packetsIndexed)
                _packetIndex.TryAdd(Packets[_packetsIndexed].Name, Packets[_packetsIndexed]);
            return _packetIndex.TryGetValue(name, out PacketDef? p) ? p : null;
        }
    }
}
