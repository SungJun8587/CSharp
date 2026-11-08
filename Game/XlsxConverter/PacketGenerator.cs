using System;
using System.Collections.Generic;
using System.Globalization;

namespace XlsxConverter
{
    /// <summary>패킷 스키마 -> C++/C# 프로토콜 코드 (타입 표는 PacketSchema.cs).</summary>
    public static class PacketGenerator
    {
        private const string Divider = "//***************************************************************************\n";

        private static string Inv(long v) => v.ToString(CultureInfo.InvariantCulture);

        // "@brief ... / @details ..." 블록 주석. 둘째 줄부터는 태그 뒤 글자에 맞춰 들여쓴다.
        private static string DocBlock(string brief, string detail)
        {
            if (brief.Length == 0 && detail.Length == 0) return string.Empty;     // 빈 설명 블록(구분선만 두 줄)을 만들지 않는다
            CodeBuilder s = Divider;
            void Emit(string tag, string text, int indent)
            {
                bool first = true;
                foreach (string l in CodeGenCommon.Lines(text))
                {
                    s += first ? "// " + tag + " " + l : "//" + new string(' ', indent) + l;
                    s += "\n";
                    first = false;
                }
            }
            if (brief.Length > 0) Emit("@brief", brief, 8);
            if (detail.Length > 0) Emit("@details", detail, 10);
            return s + Divider;
        }

        // SPacketField.type은 ParsePacketSheets가 PacketTypeInfo.Find로 검증한 값이므로 항상 표에 있다.
        private static PacketTypeInfo TypeOf(PacketField f) => PacketTypeInfo.Find(f.Type)!;

        private static string CppFieldType(PacketField f) => TypeOf(f).CppType;

        private static string CppConstType(string t)
        {
            if (t == "uint16") return "std::uint16_t";
            if (t == "uint32") return "std::uint32_t";
            return "std::size_t";
        }

        private static string CsConstType(string t)
        {
            if (t == "uint16") return "ushort";
            if (t == "uint32") return "uint";
            return "int";
        }

        // 생성 C++ 코드에 이름 그대로 들어가는 것들(상수, enum과 멤버, 패킷 번호 enum, 패킷, 필드)이 C++ 키워드나 Windows 매크로와 겹치는지 확인합니다.
        // 겹치면 변환은 성공하는데 컴파일에서 터지므로 여기서 막는다. (Enum 시트는 RowGenerator가 같은 규칙으로 검사한다)
        private static void ValidateCppNames(PacketSchema schema, List<string> errors)
        {
            void Check(string what, string name)
            {
                if (CodeGenCommon.IsCppReservedName(name))
                    errors.Add(what + ": '" + name + "'은(는) C++ 키워드이거나 Windows 헤더가 매크로로 정의하는 이름이라 생성 코드에 쓸 수 없습니다");
            }
            foreach (PacketConst c in schema.Consts) Check("PacketConst " + c.Name, c.Name);
            foreach (EnumDef e in schema.Enums)
            {
                Check("PacketEnum " + e.Name, e.Name);
                foreach (EnumMember m in e.Members) Check("PacketEnum " + e.Name + "." + m.Name, m.Name);
            }
            foreach (PacketGroup g in schema.Groups) Check("PacketGroup " + g.Name + " Enum", g.EnumName);
            foreach (PacketDef p in schema.Packets)
            {
                Check("Packet " + p.Name, p.Name);
                foreach (PacketField f in p.Fields) Check("PacketField " + p.Name + "." + f.Name, f.Name);
            }
        }

        private static string Header(string file, string desc) => CodeGenCommon.CppBanner(file, CodeGenCommon.SourceNote(desc));

        // 같은 입력이면 같은 출력: 정렬 없이 시트 순서 그대로 쓴다.

        private static string GenTypes(PacketSchema schema)
        {
            CodeBuilder s = Header("ZonePacketTypes.gen.h", "PacketConst, PacketEnum, PacketGroup, Packet");
            s += "\n#ifndef ZONEPACKETTYPES_GEN_H\n#define ZONEPACKETTYPES_GEN_H\n\n#include <cstddef>\n#include <cstdint>\n";

            // 상수. Comment가 비어 있는 상수는 앞 상수의 설명 블록에 이어 붙는다.
            bool openBlock = false;
            s += "\n";
            for (int i = 0; i < schema.Consts.Count; ++i)
            {
                PacketConst c = schema.Consts[i];
                if (c.Comment.Length > 0)
                {
                    if (i > 0) s += "\n";
                    s += DocBlock(c.Comment, string.Empty);
                    openBlock = true;
                }
                else if (!openBlock && i > 0) s += "\n";
                s += "constexpr " + CppConstType(c.Type) + " " + c.Name + " = " + c.Value.ToString(CultureInfo.InvariantCulture) + ";\n";
            }

            // 결과 코드 enum
            foreach (EnumDef e in schema.Enums)
            {
                s += "\n";
                if (e.Comment.Length > 0) s += DocBlock(e.Comment, string.Empty);
                s += CodeGenCommon.CppEnum(e, string.Empty);
            }

            // 패킷 번호 열거형 (그룹마다)
            foreach (PacketGroup g in schema.Groups)
            {
                s += "\n";
                if (g.Comment.Length > 0) s += DocBlock(g.Comment, string.Empty);
                s += "enum class " + g.EnumName + " : std::uint16_t\n{\n";
                foreach (PacketDef p in schema.Packets)
                    if (p.Group == g.Name) s += "\t" + p.Name + " = " + Inv(p.Id) + "," + CodeGenCommon.CommentTail(p.Summary) + "\n";
                s += "};\n";
            }
            return s + "\n#endif // ndef ZONEPACKETTYPES_GEN_H\n";
        }

        private static string GenStructs(PacketSchema schema)
        {
            CodeBuilder s = Header("ZonePacketStructs.gen.h", "Packet, PacketField");
            s += "\n#ifndef ZONEPACKETSTRUCTS_GEN_H\n#define ZONEPACKETSTRUCTS_GEN_H\n\n#include <Network/Packet.h>\n#include \"ZonePacketTypes.gen.h\"\n\n";
            s += "#pragma pack(push, 1)\n";

            string lastGroup = string.Empty;
            foreach (PacketDef p in schema.Packets)
            {
                if (p.Group != lastGroup)
                {
                    lastGroup = p.Group;
                    PacketGroup? g = schema.FindGroup(p.Group);
                    if (g != null && g.Section.Length > 0)
                    {
                        s += "\n// ---------------------------------------------------------------------------\n";
                        foreach (string l in CodeGenCommon.Lines(g.Section)) s += "// " + l + "\n";
                        s += "// ---------------------------------------------------------------------------\n";
                    }
                }
                s += "\n" + DocBlock(p.Comment, p.Detail);
                s += "struct " + p.Name + "Packet : PacketHeader\n{\n";
                foreach (PacketField f in p.Fields)
                {
                    s += "\t" + CppFieldType(f) + "\t" + f.Name;
                    if (f.Type == "char") s += "[" + f.Len + "]";
                    s += ";";
                    string tail = f.Comment;
                    if (f.EnumName.Length > 0) tail = tail.Length == 0 ? f.EnumName : tail;      // 설명이 없으면 의미를 나타내는 enum 이름을 적는다
                    s += CodeGenCommon.CommentTail(tail) + "\n";
                }
                s += "};\n";
            }
            s += "\n#pragma pack(pop)\n\n";

            // 와이어 크기 고정: PacketField 시트와 구조체가 어긋나면 컴파일이 멈춘다.
            foreach (PacketDef p in schema.Packets)
                s += "static_assert(sizeof(" + p.Name + "Packet) == " + p.Size.ToString(CultureInfo.InvariantCulture) + ", \"" + p.Name + "Packet: wire size differs from the Packet sheet\");\n";
            return s + "\n#endif // ndef ZONEPACKETSTRUCTS_GEN_H\n";
        }

        // ---------------------------------------------------------------- C#

        private static string CsFieldType(PacketField f)
        {
            if (f.EnumName.Length > 0) return CodeGenCommon.CsEnumName(f.EnumName);
            return TypeOf(f).CsType!;
        }

        private static string CsReadExpr(PacketField f)
        {
            string raw = TypeOf(f).CsRead!;
            return f.EnumName.Length == 0 ? raw : "(" + CsFieldType(f) + ")" + raw;
        }

        private static string CsWriteStmt(PacketField f, string value)
        {
            PacketTypeInfo ti = TypeOf(f);
            string pattern = (f.EnumName.Length > 0 && ti.CsWriteEnum != null) ? ti.CsWriteEnum : ti.CsWrite!;
            int pos = pattern.IndexOf("{v}", StringComparison.Ordinal);
            return pattern.Remove(pos, 3).Insert(pos, value);      // 패턴마다 {v}는 하나다. (식이 값을 두 번 평가하지 않도록 표를 만든다)
        }

        private static string CsHeader(string file, string desc) => CodeGenCommon.CsBanner(file, CodeGenCommon.SourceNote(desc));

        // C# 주석은 한 줄씩 //로 쓴다.
        private static string CsComment(string text, string indent)
        {
            CodeBuilder s = string.Empty;
            foreach (string l in CodeGenCommon.Lines(text)) s += indent + "// " + l + "\n";
            return s;
        }

        private static string GenCsTypes(PacketSchema schema, string ns)
        {
            CodeBuilder s = CsHeader("ProtocolTypes.gen.cs", "PacketConst, PacketEnum, PacketGroup, Packet");
            s += "\nnamespace " + ns + "\n{\n";

            // ProtocolConst는 partial: 시트에 없는 값(HeaderSize 등)은 손으로 쓴 파일이 같은 클래스에 더한다.
            s += "\tpublic static partial class ProtocolConst\n\t{\n";
            foreach (PacketConst c in schema.Consts)
            {
                if (c.Comment.Length > 0) s += CsComment(c.Comment, "\t\t");
                s += "\t\tpublic const " + CsConstType(c.Type) + " " + CodeGenCommon.CsConstName(c.Name) + " = " + c.Value.ToString(CultureInfo.InvariantCulture) + ";\n";
            }
            s += "\t}\n";

            foreach (EnumDef e in schema.Enums)
            {
                s += "\n";
                if (e.Comment.Length > 0) s += CsComment(e.Comment, "\t");
                s += CodeGenCommon.CsEnum(e, CodeGenCommon.CsEnumName(e.Name), "\t");
            }
            foreach (PacketGroup g in schema.Groups)
            {
                s += "\n";
                if (g.Comment.Length > 0) s += CsComment(g.Comment, "\t");
                s += "\tpublic enum " + g.CsEnum + " : ushort\n\t{\n";
                foreach (PacketDef p in schema.Packets)
                    if (p.Group == g.Name) s += "\t\t" + CodeGenCommon.CsIdent(p.Name) + " = " + Inv(p.Id) + "," + CodeGenCommon.CommentTail(p.Summary) + "\n";
                s += "\t}\n";
            }
            return s + "}\n";
        }

        private static string GenCsPackets(PacketSchema schema, string ns, List<string> errors)
        {
            CodeBuilder s = CsHeader("Packets.gen.cs", "Packet, PacketField");
            s += "\nusing System;\n\nnamespace " + ns + "\n{\n";

            string GroupEnum(PacketDef p) { PacketGroup? g = schema.FindGroup(p.Group); return g != null ? g.CsEnum : string.Empty; }

            // ---- Send: Build
            bool anySend = false;
            foreach (PacketDef p in schema.Packets) if (p.Cs == "Send" || p.Cs == "Both") anySend = true;
            if (anySend)
            {
                s += "\t// 클라이언트가 서버로 보내는 패킷을 만든다.\n\tpublic static class Build\n\t{\n";
                foreach (PacketDef p in schema.Packets)
                    if (p.Cs == "Send" || p.Cs == "Both") s += "\t\tpublic const int " + p.Name + "Size = " + p.Size.ToString(CultureInfo.InvariantCulture) + ";\n";
                foreach (PacketDef p in schema.Packets)
                {
                    if (p.Cs != "Send" && p.Cs != "Both") continue;
                    string args = string.Empty;
                    foreach (PacketField f in p.Fields)
                    {
                        if (CodeGenCommon.IsCsKeyword(f.Name)) { errors.Add("PacketField: " + p.Name + "." + f.Name + ": C# 예약어라 매개변수 이름으로 쓸 수 없습니다"); continue; }
                        if (f.Name == "w") { errors.Add("PacketField: " + p.Name + "." + f.Name + ": 매개변수 이름 w는 생성 코드의 PacketWriter 지역 변수와 겹칩니다"); continue; }
                        args += (args.Length == 0 ? "" : ", ") + CsFieldType(f) + " " + f.Name;
                    }
                    s += "\n\t\tpublic static byte[] " + p.Name + "(" + args + ")\n\t\t{\n";
                    s += "\t\t\tPacketWriter w = new PacketWriter((ushort)" + GroupEnum(p) + "." + p.Name + ", " + p.Name + "Size);\n";
                    foreach (PacketField f in p.Fields) s += "\t\t\t" + CsWriteStmt(f, f.Name) + "\n";
                    s += "\t\t\treturn w.Finish();\n\t\t}\n";
                }
                s += "\t}\n";
            }

            // ---- Recv: Parse / ToBytes
            foreach (PacketDef p in schema.Packets)
            {
                if (p.Cs != "Recv" && p.Cs != "Both") continue;
                var members = new HashSet<string>(StringComparer.Ordinal);
                s += "\n\tpublic sealed class " + p.Name + "\n\t{\n";
                s += "\t\tpublic const int Size = " + p.Size.ToString(CultureInfo.InvariantCulture) + ";\n";
                // 같은 타입이 이어지는 필드는 한 줄에 선언한다.
                string run = string.Empty, runType = string.Empty;
                foreach (PacketField f in p.Fields)
                {
                    string m = CodeGenCommon.Pascal(f.Name), t = CsFieldType(f);
                    if (m == p.Name || m == "Size" || m == "Parse" || m == "ToBytes" || !members.Add(m))
                    {
                        errors.Add("PacketField: " + p.Name + "." + f.Name + ": C# 멤버 이름 " + m + "이(가) 겹칩니다");
                        continue;
                    }
                    if (t != runType)
                    {
                        if (run.Length > 0) { s += "\t\tpublic " + runType + " " + run + ";\n"; run = string.Empty; }
                        runType = t;
                    }
                    run += (run.Length == 0 ? "" : ", ") + m;
                }
                if (run.Length > 0) { s += "\t\tpublic " + runType + " " + run + ";\n"; run = string.Empty; }
                s += "\n\t\tpublic static " + p.Name + " Parse(byte[] d)\n\t\t{\n";
                s += "\t\t\tPacketReader r = new PacketReader(d, ProtocolConst.HeaderSize);\n";
                s += "\t\t\t" + p.Name + " p = new " + p.Name + "();\n";
                foreach (PacketField f in p.Fields) s += "\t\t\tp." + CodeGenCommon.Pascal(f.Name) + " = " + CsReadExpr(f) + ";\n";
                s += "\t\t\treturn p;\n\t\t}\n";
                s += "\n\t\t// 서버 쪽 레이아웃으로 직렬화한다 (테스트의 가짜 서버와 오프라인 데모가 쓴다)\n\t\tpublic byte[] ToBytes()\n\t\t{\n";
                s += "\t\t\tPacketWriter w = new PacketWriter((ushort)" + GroupEnum(p) + "." + p.Name + ", Size);\n";
                foreach (PacketField f in p.Fields) s += "\t\t\t" + CsWriteStmt(f, CodeGenCommon.Pascal(f.Name)) + "\n";
                s += "\t\t\treturn w.Finish();\n\t\t}\n\t}\n";
            }
            return s + "}\n";
        }

        // ---------------------------------------------------------------- 공개

        /// <summary>ZonePacketTypes.gen.h(상수, enum, 패킷 번호), ZonePacketStructs.gen.h(구조체, sizeof static_assert)를 output에 덧붙입니다.</summary>
        public static bool Generate(PacketSchema schema, List<GeneratedFile> output, List<string> errors)
        {
            if (schema.IsEmpty) return true;
            int before = errors.Count;
            ValidateCppNames(schema, errors);
            if (errors.Count != before) return false;
            output.Add(new GeneratedFile("ZonePacketTypes.gen.h", GenTypes(schema)));
            output.Add(new GeneratedFile("ZonePacketStructs.gen.h", GenStructs(schema)));
            return true;
        }

        /// <summary>ProtocolTypes.gen.cs, Packets.gen.cs(Build.&lt;패킷&gt;, &lt;패킷&gt;.Parse/ToBytes)를 output에 덧붙입니다.</summary>
        /// <param name="otherTypeNames">같은 네임스페이스에 만들어지는 다른 C# 형식 이름들(시트 클래스 등). 없으면 null</param>
        public static bool GenerateCs(PacketSchema schema, string ns, List<GeneratedFile> output, List<string> errors, ISet<string>? otherTypeNames)
        {
            if (schema.IsEmpty) return true;
            int before = errors.Count;
            if (!CodeGenCommon.IsValidNamespace(ns))
            {
                errors.Add("패킷 C# 네임스페이스 '" + ns + "': 형식이 아닙니다 (--proto-cs-namespace, 없으면 --cs-namespace)");
                return false;
            }

            // 같은 네임스페이스에 만들어지는 형식 이름끼리 겹치면 C# 컴파일이 깨지므로 미리 막는다.
            {
                bool anySend = false;
                foreach (PacketDef p in schema.Packets) if (p.Cs == "Send" || p.Cs == "Both") anySend = true;
                var names = new HashSet<string>(StringComparer.Ordinal) { "ProtocolConst" };
                if (anySend) names.Add("Build");
                void Add(string name, string what)
                {
                    if (!names.Add(name))
                        errors.Add(what + " " + name + ": C# 형식 이름이 패킷 프로토콜의 다른 형식(ProtocolConst, Build, enum, 패킷 클래스)과 겹칩니다");
                    else if (otherTypeNames != null && otherTypeNames.Contains(name))
                        errors.Add(what + " " + name + ": C# 형식 이름이 같은 네임스페이스(" + ns + ")의 시트 클래스/GameConsts/Enum 시트의 enum과 겹칩니다");
                }
                foreach (EnumDef e in schema.Enums) Add(CodeGenCommon.CsEnumName(e.Name), "PacketEnum");
                foreach (PacketGroup g in schema.Groups) Add(g.CsEnum, "PacketGroup");
                foreach (PacketDef p in schema.Packets)
                {
                    if (p.Cs == "None") continue;
                    if (CodeGenCommon.IsCsKeyword(p.Name)) errors.Add("Packet " + p.Name + ": C# 예약어라 클래스/메서드 이름으로 쓸 수 없습니다");
                    if (p.Cs == "Recv" || p.Cs == "Both") Add(p.Name, "Packet");
                }
                if (errors.Count != before) return false;
            }
            output.Add(new GeneratedFile("ProtocolTypes.gen.cs", GenCsTypes(schema, ns)));
            output.Add(new GeneratedFile("Packets.gen.cs", GenCsPackets(schema, ns, errors)));
            return errors.Count == before;
        }

        /// <summary>"&lt;구조체&gt; &lt;바이트&gt;" 목록. (C# 테스트가 C++ sizeof와 대조하는 입력)</summary>
        public static string Sizes(PacketSchema schema)
        {
            CodeBuilder s = string.Empty;
            foreach (PacketDef p in schema.Packets) s += p.Name + "Packet " + p.Size.ToString(CultureInfo.InvariantCulture) + "\n";
            return s;
        }
    }
}
