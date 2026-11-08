using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XlsxConverter
{
    /// <summary>
    /// 데이터 시트가 아니라 형식을 정의하는 예약 시트.
    ///   Enum 시트   헤더: Enum:string  Name:string  Value:int?  Base:string?  Comment:string?
    ///               같은 Enum 이름의 행들이 한 enum이다. Value를 비우면 앞 멤버 + 1(첫 멤버는 0).
    ///               Base는 기반 정수형(uint8, uint16, uint32, int32), 생략하면 uint8. enum의 첫 행에만 써도 된다.
    ///   Const 시트  헤더: Name:string  Type:string  Value  Comment:string?
    ///               Type은 int, uint, float, string, bool. Value 열 헤더는 "Value" 또는 "Value:..."
    /// 첫 칸이 '#'로 시작하는 행과 빈 행은 건너뛴다. 이 시트들은 JSON 파일로 변환하지 않는다.
    ///
    /// 패킷 프로토콜 정의 시트. 이 시트들도 JSON으로 변환하지 않고 PacketGenerator가 C++/C# 코드로 만든다.
    ///   PacketEnum   Enum 시트와 같은 열 + EnumComment:string?(enum의 첫 행에 쓰는 enum 설명)
    ///   PacketConst  Name:string  Type:string(uint16, uint32, size)  Value:uint  Comment:string?
    ///   PacketGroup  Group:string  Enum:string  CsEnum:string  Min:uint  Max:uint?  Section:string  Comment:string?
    ///   Packet       Name:string  Group:string  Id:uint  Cs:string?(None, Send, Recv, Both)  Summary:string?  Comment:string  Detail:string?
    ///   PacketField  Packet:string  Name:string  Type:string  Len:string?  Enum:string?  Comment:string?   (Packet별로 행 순서가 곧 필드 순서)
    /// </summary>
    public static class DefinitionSheets
    {
        public const string EnumSheet = "Enum";
        public const string ConstSheet = "Const";
        public const string PacketEnumSheet = "PacketEnum";
        public const string PacketConstSheet = "PacketConst";
        public const string PacketGroupSheet = "PacketGroup";
        public const string PacketSheet = "Packet";
        public const string PacketFieldSheet = "PacketField";

        private const int PacketHeaderBytes = 4;   // PacketHeader { uint16 size; uint16 type; }

        // PacketField 시트 Type 열에 쓸 수 있는 이름 목록 (오류 메시지용). PacketTypeInfo의 표와 같은 순서다.
        private const string PacketTypeNames = "uint8, bool8, uint16, uint32, int32, uint64, float, char";

        private static readonly string[] Reserved =
        {
            "enum", "const", "enums", "consts", "enumstrings", "generatedcommon", "packetenum", "packetconst", "packetgroup", "packet", "packetfield", "zonepackettypes", "zonepacketstructs",
        };

        /// <summary>예약 시트 이름인지 확인합니다. (Enum, Const, Packet*, 그리고 생성 파일 이름과 겹치는 Enums, Consts, EnumStrings, GeneratedCommon, ZonePacketTypes, ZonePacketStructs)</summary>
        public static bool IsReservedSheet(string name)
        {
            // 생성 파일 이름(Enums.gen.h 등)은 Windows에서 대소문자를 구분하지 않으므로 이름도 대소문자 없이 비교한다.
            string lower = AsciiChar.Lower(name);
            foreach (string r in Reserved)
                if (lower == r) return true;
            return false;
        }

        public static bool IsPacketSheet(string name)
            => name == PacketEnumSheet || name == PacketConstSheet || name == PacketGroupSheet || name == PacketSheet || name == PacketFieldSheet;

        // ------------------------------------------------------------------ 도우미

        // 헤더 칸에서 열 이름만 꺼냅니다. ("Value:int?" -> "Value")
        private static string ColumnName(Cell cell)
        {
            string text = TextUtil.Trim(ValueFormat.CellToString(cell));
            int colon = text.IndexOf(':');
            return TextUtil.Trim(colon < 0 ? text : text.Substring(0, colon));
        }

        // 시트 헤더를 읽어 열 이름 -> 0-base 열 번호 표를 만듭니다.
        private static bool ReadColumns(string sheet, Rows rows, ICollection<string> allowed, string[] needed,
            Dictionary<string, int> cols, List<string> errors)
        {
            int before = errors.Count;
            if (!rows.TryGetValue(1, out Cell[]? header)) { errors.Add(sheet + ": 헤더가 비어 있습니다"); return false; }

            for (int i = 0; i < header.Length; ++i)
            {
                if (SheetConverter.IsBlank(header[i])) continue;
                string name = ColumnName(header[i]);
                string at = sheet + "!" + (i + 1).ToString(CultureInfo.InvariantCulture) + "열 헤더 '" + name + "'";
                if (!allowed.Contains(name)) { errors.Add(at + ": 알 수 없는 열입니다"); continue; }
                if (!cols.TryAdd(name, i)) errors.Add(at + ": 열 이름 중복");
            }
            var sorted = new List<string>(needed);
            sorted.Sort(StringComparer.Ordinal);
            foreach (string n in sorted)
                if (!cols.ContainsKey(n)) errors.Add(sheet + ": '" + n + "' 열이 없습니다");
            return errors.Count == before;
        }

        private static Cell CellAt(Cell[] row, Dictionary<string, int> cols, string name)
            => cols.TryGetValue(name, out int idx) && idx < row.Length ? row[idx] : default;

        private readonly struct DataRow
        {
            public readonly int First;      // 엑셀의 1-base 행 번호
            public readonly Cell[] Second;
            public DataRow(int first, Cell[] second) { First = first; Second = second; }
        }

        // 헤더(1행)와 주석 행, 빈 행을 뺀 데이터 행들을 시트 순서대로 돌려줍니다.
        private static List<DataRow> DataRows(Rows rows)
        {
            var v = new List<DataRow>(rows.Count);
            foreach (KeyValuePair<int, Cell[]> kv in rows)
                if (kv.Key >= 2 && !SheetConverter.IsSkipRow(kv.Value))
                    v.Add(new DataRow(kv.Key, kv.Value));
            return v;
        }

        private static string Text(Cell c) => TextUtil.Trim(ValueFormat.CellToString(c));

        // 셀 안의 줄바꿈(\r\n, \r)을 \n 하나로 통일하고 줄마다 앞뒤 공백을 지웁니다. (여러 줄 주석용)
        private static string MultiLine(Cell cell)
        {
            string t = ValueFormat.CellToString(cell).Replace("\r", "");
            var o = new StringBuilder();
            int pos = 0;
            while (pos <= t.Length)
            {
                int e = t.IndexOf('\n', pos);
                if (e < 0) e = t.Length;
                string line = TextUtil.Trim(t.Substring(pos, e - pos));
                if (o.Length > 0 || line.Length > 0)
                {
                    if (o.Length > 0) o.Append('\n');
                    o.Append(line);
                }
                pos = e + 1;
            }
            string s = o.ToString();
            return s.TrimEnd('\n');
        }

        private static bool OneLine(string s) => s.IndexOf('\n') < 0;

        // ------------------------------------------------------------------ Enum, PacketEnum

        // Enum 계열 시트(Enum, PacketEnum)를 읽어 enums에 채웁니다.
        private static bool ParseEnumRows(string sheet, bool withEnumComment, Rows rows, List<EnumDef> enums, List<string> errors)
        {
            int before = errors.Count;

            var cols = new Dictionary<string, int>(StringComparer.Ordinal);
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "Enum", "Name", "Value", "Base", "Comment" };
            if (withEnumComment) allowed.Add("EnumComment");
            if (!ReadColumns(sheet, rows, allowed, new[] { "Enum", "Name" }, cols, errors))
                return false;

            foreach (DataRow kv in DataRows(rows))
            {
                Cell[] row = kv.Second;
                string where = SheetConverter.RowLabel(sheet, kv.First);

                string enumName = Text(CellAt(row, cols, "Enum")), memberName = Text(CellAt(row, cols, "Name"));
                if (!Identifier.IsPlain(enumName)) { errors.Add(where + "Enum: C++/C# 식별자여야 합니다 ('" + enumName + "')"); continue; }
                if (!Identifier.IsPlain(memberName)) { errors.Add(where + "Name: C++/C# 식별자여야 합니다 ('" + memberName + "')"); continue; }

                EnumDef? e = enums.Find(x => x.Name == enumName);
                if (e == null)
                {
                    e = new EnumDef { Name = enumName };
                    enums.Add(e);
                }

                if (withEnumComment)
                {
                    string ec = Text(CellAt(row, cols, "EnumComment"));
                    if (ec.Length > 0)
                    {
                        if (e.Comment.Length == 0) e.Comment = ec;
                        else errors.Add(where + "EnumComment: " + enumName + "의 설명은 첫 행에만 쓸 수 있습니다");
                    }
                }

                Cell baseCell = CellAt(row, cols, "Base");
                if (!SheetConverter.IsBlank(baseCell))
                {
                    string b = Text(baseCell);
                    if (BaseInfo.Find(b) == null) { errors.Add(where + "Base: uint8, uint16, uint32, int32 중 하나여야 합니다 ('" + b + "')"); continue; }
                    if (e.Members.Count == 0) e.Base = b;
                    else if (e.Base != b) { errors.Add(where + "Base: " + enumName + "의 기반형이 앞 행(" + e.Base + ")과 다릅니다 ('" + b + "')"); continue; }
                }

                var m = new EnumMember { Name = memberName, Comment = Text(CellAt(row, cols, "Comment")) };

                Cell valueCell = CellAt(row, cols, "Value");
                if (SheetConverter.IsBlank(valueCell))
                {
                    m.Value = e.Members.Count == 0 ? 0 : e.Members[e.Members.Count - 1].Value + 1;
                }
                else
                {
                    if (!ValueFormat.ParseInt64(valueCell, out long value, out string why)) { errors.Add(where + "Value: " + why); continue; }
                    m.Value = value;
                }

                bool dup = false;
                foreach (EnumMember other in e.Members)
                {
                    if (other.Name == m.Name) { errors.Add(where + "Name: " + enumName + "에 같은 이름의 멤버가 이미 있습니다 ('" + m.Name + "')"); dup = true; break; }
                    if (other.Value == m.Value) { errors.Add(where + "Value: " + enumName + "에 같은 값(" + m.Value.ToString(CultureInfo.InvariantCulture) + ")이 이미 있습니다 ('" + other.Name + "')"); dup = true; break; }
                }
                if (!dup) e.Members.Add(m);
            }

            // 기반형으로 표현되는지는 모든 행을 읽은 뒤에 확인한다. (Base가 첫 행에만 있어도 되므로)
            foreach (EnumDef e in enums)
            {
                BaseInfo bi = BaseInfo.Find(e.Base)!;
                long lo = bi.MinV, hi = bi.MaxV;
                foreach (EnumMember m in e.Members)
                    if (m.Value < lo || m.Value > hi)
                        errors.Add(sheet + ": " + e.Name + "." + m.Name + " 값 " + m.Value.ToString(CultureInfo.InvariantCulture) + "이(가) " + e.Base + " 범위(" + lo.ToString(CultureInfo.InvariantCulture) + ".." + hi.ToString(CultureInfo.InvariantCulture) + ")를 벗어났습니다");
            }
            return errors.Count == before;
        }

        /// <summary>Enum 시트를 읽어 defs.Enums를 채웁니다. 오류는 멈추지 않고 모두 수집합니다.</summary>
        /// <returns>오류가 하나도 없으면 true</returns>
        public static bool ParseEnumSheet(Rows rows, Definitions defs, List<string> errors)
            => ParseEnumRows(EnumSheet, false, rows, defs.Enums, errors);

        // ------------------------------------------------------------------ Const

        /// <summary>Const 시트를 읽어 defs.Consts를 채웁니다. 오류는 멈추지 않고 모두 수집합니다.</summary>
        /// <returns>오류가 하나도 없으면 true</returns>
        public static bool ParseConstSheet(Rows rows, Definitions defs, List<string> errors)
        {
            int before = errors.Count;
            const string sheet = ConstSheet;

            var cols = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!ReadColumns(sheet, rows, new HashSet<string> { "Name", "Type", "Value", "Comment" }, new[] { "Name", "Type", "Value" }, cols, errors))
                return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataRow kv in DataRows(rows))
            {
                Cell[] row = kv.Second;
                string where = SheetConverter.RowLabel(sheet, kv.First);

                var c = new ConstDef
                {
                    Name = Text(CellAt(row, cols, "Name")),
                    Type = Text(CellAt(row, cols, "Type")),
                    Comment = Text(CellAt(row, cols, "Comment")),
                };

                if (!Identifier.IsPlain(c.Name)) { errors.Add(where + "Name: C++/C# 식별자여야 합니다 ('" + c.Name + "')"); continue; }
                if (!seen.Add(c.Name)) { errors.Add(where + "Name: 이름 중복 ('" + c.Name + "')"); continue; }
                if (TypeInfo.Find(c.Type) == null)
                {
                    errors.Add(where + "Type: int, uint, float, string, bool 중 하나여야 합니다 ('" + c.Type + "')");
                    continue;
                }

                var def = new FieldDef { Type = c.Type };
                if (!SheetConverter.ConvertCellValue(CellAt(row, cols, "Value"), def, null, out string literal, out string why)) { errors.Add(where + "Value: " + why); continue; }
                c.Literal = literal;
                defs.Consts.Add(c);
            }
            return errors.Count == before;
        }

        // ------------------------------------------------------------------ Packet*

        /// <summary>
        /// 패킷 정의 시트들을 읽어 schema를 채웁니다. 오류는 멈추지 않고 모두 수집하며, 모든 시트를 읽은 뒤 서로의 참조와 크기를 검사합니다.
        /// </summary>
        /// <param name="sheets">시트 이름 -> 행. 있는 시트만 넣는다. (Packet 시트 없이 PacketField만 있는 경우 등은 오류)</param>
        /// <returns>오류가 하나도 없으면 true</returns>
        public static bool ParsePacketSheets(IReadOnlyDictionary<string, Rows> sheets, PacketSchema schema, List<string> errors)
        {
            int before = errors.Count;
            Rows? Find(string name) => sheets.TryGetValue(name, out Rows? r) ? r : null;

            // ---- PacketEnum
            {
                Rows? rows = Find(PacketEnumSheet);
                if (rows != null) ParseEnumRows(PacketEnumSheet, true, rows, schema.Enums, errors);
            }

            // ---- PacketConst
            {
                Rows? rows = Find(PacketConstSheet);
                if (rows != null)
                {
                    const string sheet = PacketConstSheet;
                    var cols = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (ReadColumns(sheet, rows, new HashSet<string> { "Name", "Type", "Value", "Comment" }, new[] { "Name", "Type", "Value" }, cols, errors))
                    {
                        foreach (DataRow kv in DataRows(rows))
                        {
                            Cell[] row = kv.Second;
                            string where = SheetConverter.RowLabel(sheet, kv.First);
                            var c = new PacketConst
                            {
                                Name = Text(CellAt(row, cols, "Name")),
                                Type = Text(CellAt(row, cols, "Type")),
                                Comment = MultiLine(CellAt(row, cols, "Comment")),
                            };
                            if (!Identifier.IsPlain(c.Name)) { errors.Add(where + "Name: 식별자여야 합니다 ('" + c.Name + "')"); continue; }
                            if (schema.FindConst(c.Name) != null) { errors.Add(where + "Name: 이름 중복 ('" + c.Name + "')"); continue; }
                            if (c.Type != "uint16" && c.Type != "uint32" && c.Type != "size") { errors.Add(where + "Type: uint16, uint32, size 중 하나여야 합니다 ('" + c.Type + "')"); continue; }
                            if (!ValueFormat.ParseInt64(CellAt(row, cols, "Value"), out long v, out string why)) { errors.Add(where + "Value: " + why); continue; }
                            long hi = c.Type == "uint16" ? 65535 : c.Type == "size" ? 2147483647L : 4294967295L;      // size는 C#에서 int로 나온다
                            if (v < 0 || v > hi) { errors.Add(where + "Value: " + c.Type + " 범위를 벗어났습니다 (" + v.ToString(CultureInfo.InvariantCulture) + ")"); continue; }
                            c.Value = (ulong)v;
                            schema.Consts.Add(c);
                        }
                    }
                }
            }

            // ---- PacketGroup
            {
                Rows? rows = Find(PacketGroupSheet);
                if (rows != null)
                {
                    const string sheet = PacketGroupSheet;
                    var cols = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (ReadColumns(sheet, rows, new HashSet<string> { "Group", "Enum", "CsEnum", "Min", "Max", "Section", "Comment" },
                        new[] { "Group", "Enum", "CsEnum", "Min", "Section" }, cols, errors))
                    {
                        foreach (DataRow kv in DataRows(rows))
                        {
                            Cell[] row = kv.Second;
                            string where = SheetConverter.RowLabel(sheet, kv.First);
                            var g = new PacketGroup
                            {
                                Name = Text(CellAt(row, cols, "Group")),
                                EnumName = Text(CellAt(row, cols, "Enum")),
                                CsEnum = Text(CellAt(row, cols, "CsEnum")),
                                Section = MultiLine(CellAt(row, cols, "Section")),
                                Comment = MultiLine(CellAt(row, cols, "Comment")),
                            };
                            if (!Identifier.IsPlain(g.Name) || !Identifier.IsPlain(g.EnumName) || !Identifier.IsPlain(g.CsEnum)) { errors.Add(where + "Group/Enum/CsEnum: 식별자여야 합니다"); continue; }
                            if (schema.FindGroup(g.Name) != null) { errors.Add(where + "Group: 이름 중복 ('" + g.Name + "')"); continue; }
                            if (schema.FindEnum(g.EnumName) != null) { errors.Add(where + "Enum: PacketEnum의 enum과 이름이 같습니다 ('" + g.EnumName + "')"); continue; }
                            if (!ValueFormat.ParseInt64(CellAt(row, cols, "Min"), out long minId, out string why)) { errors.Add(where + "Min: " + why); continue; }
                            g.MinId = minId;
                            if (!SheetConverter.IsBlank(CellAt(row, cols, "Max")))
                            {
                                if (!ValueFormat.ParseInt64(CellAt(row, cols, "Max"), out long maxId, out why)) { errors.Add(where + "Max: " + why); continue; }
                                g.MaxId = maxId;
                            }
                            if (g.MinId < 1 || g.MaxId > 65535 || g.MinId > g.MaxId) { errors.Add(where + "Min/Max: 1..65535 범위에서 Min <= Max 여야 합니다"); continue; }
                            foreach (PacketGroup o in schema.Groups)
                                if (!(g.MaxId < o.MinId || o.MaxId < g.MinId)) { errors.Add(where + "Min/Max: 그룹 '" + o.Name + "'과 번호 구간이 겹칩니다"); break; }
                            schema.Groups.Add(g);
                        }
                    }
                }
            }

            // ---- Packet
            var rejected = new HashSet<string>(StringComparer.Ordinal);     // 오류로 빠진 패킷 이름. 그 패킷의 PacketField 행마다 "없는 패킷" 연쇄 오류를 내지 않는다
            {
                Rows? rows = Find(PacketSheet);
                if (rows != null)
                {
                    const string sheet = PacketSheet;
                    var cols = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (ReadColumns(sheet, rows, new HashSet<string> { "Name", "Group", "Id", "Cs", "Summary", "Comment", "Detail" },
                        new[] { "Name", "Group", "Id", "Comment" }, cols, errors))
                    {
                        foreach (DataRow kv in DataRows(rows))
                        {
                            Cell[] row = kv.Second;
                            string where = SheetConverter.RowLabel(sheet, kv.First);
                            var p = new PacketDef
                            {
                                Name = Text(CellAt(row, cols, "Name")),
                                Group = Text(CellAt(row, cols, "Group")),
                                Cs = Text(CellAt(row, cols, "Cs")),
                            };
                            if (p.Cs.Length == 0) p.Cs = "None";
                            p.Summary = Text(CellAt(row, cols, "Summary"));
                            p.Comment = MultiLine(CellAt(row, cols, "Comment"));
                            p.Detail = MultiLine(CellAt(row, cols, "Detail"));
                            if (!Identifier.IsPlain(p.Name)) { errors.Add(where + "Name: 식별자여야 합니다 ('" + p.Name + "')"); continue; }
                            if (schema.FindPacket(p.Name) != null) { errors.Add(where + "Name: 이름 중복 ('" + p.Name + "')"); continue; }
                            if (p.Cs != "None" && p.Cs != "Send" && p.Cs != "Recv" && p.Cs != "Both") { errors.Add(where + "Cs: None, Send, Recv, Both 중 하나여야 합니다 ('" + p.Cs + "')"); rejected.Add(p.Name); continue; }
                            if (!OneLine(p.Summary)) { errors.Add(where + "Summary: 한 줄이어야 합니다"); rejected.Add(p.Name); continue; }
                            if (!ValueFormat.ParseInt64(CellAt(row, cols, "Id"), out long id, out string why)) { errors.Add(where + "Id: " + why); rejected.Add(p.Name); continue; }
                            p.Id = id;
                            schema.Packets.Add(p);
                        }
                    }
                }
            }

            // ---- PacketField (Packet 시트 뒤에 읽는다)
            {
                Rows? rows = Find(PacketFieldSheet);
                if (rows != null)
                {
                    const string sheet = PacketFieldSheet;
                    var cols = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (ReadColumns(sheet, rows, new HashSet<string> { "Packet", "Name", "Type", "Len", "Enum", "Comment" },
                        new[] { "Packet", "Name", "Type" }, cols, errors))
                    {
                        foreach (DataRow kv in DataRows(rows))
                        {
                            Cell[] row = kv.Second;
                            string where = SheetConverter.RowLabel(sheet, kv.First);
                            string pname = Text(CellAt(row, cols, "Packet"));
                            PacketDef? p = schema.FindPacket(pname);
                            if (p == null) { if (!rejected.Contains(pname)) errors.Add(where + "Packet: Packet 시트에 없는 패킷입니다 ('" + pname + "')"); continue; }

                            var f = new PacketField
                            {
                                Name = Text(CellAt(row, cols, "Name")),
                                Type = Text(CellAt(row, cols, "Type")),
                                Len = Text(CellAt(row, cols, "Len")),
                                EnumName = Text(CellAt(row, cols, "Enum")),
                                Comment = Text(CellAt(row, cols, "Comment")),
                            };
                            if (!Identifier.IsPlain(f.Name)) { errors.Add(where + "Name: 식별자여야 합니다 ('" + f.Name + "')"); continue; }
                            bool dupField = false;
                            foreach (PacketField o in p.Fields)
                                if (o.Name == f.Name) { errors.Add(where + "Name: " + pname + "에 같은 이름의 필드가 있습니다 ('" + f.Name + "')"); dupField = true; break; }
                            if (dupField) continue;
                            if (!OneLine(f.Comment)) { errors.Add(where + "Comment: 한 줄이어야 합니다"); continue; }
                            PacketTypeInfo? ti = PacketTypeInfo.Find(f.Type);
                            if (ti == null) { errors.Add(where + "Type: " + PacketTypeNames + " 중 하나여야 합니다 ('" + f.Type + "')"); continue; }
                            if (f.Type == "char")
                            {
                                if (f.Len.Length == 0) { errors.Add(where + "Len: char 배열은 길이가 필요합니다"); continue; }
                                int n;
                                bool allDigits = true;
                                foreach (char ch in f.Len) if (!AsciiChar.IsDigit(ch)) { allDigits = false; break; }
                                PacketConst? lc;
                                if (allDigits && f.Len.Length < 6) n = int.Parse(f.Len, CultureInfo.InvariantCulture);
                                else if ((lc = schema.FindConst(f.Len)) != null) n = (int)Math.Min(lc.Value, int.MaxValue);
                                else { errors.Add(where + "Len: 숫자 또는 PacketConst 이름이어야 합니다 ('" + f.Len + "')"); continue; }
                                if (n < 1 || n > 4096) { errors.Add(where + "Len: 1..4096 이어야 합니다"); continue; }
                                f.Bytes = n;
                            }
                            else
                            {
                                if (f.Len.Length > 0) { errors.Add(where + "Len: char 이외에는 쓸 수 없습니다"); continue; }
                                f.Bytes = ti.Bytes;
                            }
                            if (f.EnumName.Length > 0)
                            {
                                if (f.Type != "uint8" && f.Type != "uint16" && f.Type != "uint32") { errors.Add(where + "Enum: uint8, uint16, uint32 필드에만 쓸 수 있습니다"); continue; }
                                EnumDef? e = schema.FindEnum(f.EnumName);
                                if (e == null) { errors.Add(where + "Enum: PacketEnum에 없는 enum입니다 ('" + f.EnumName + "')"); continue; }
                                BaseInfo? eb = BaseInfo.Find(e.Base);
                                if (eb == null || eb.Name != f.Type) { errors.Add(where + "Enum: " + f.EnumName + "의 기반형(" + e.Base + ")과 필드 타입(" + f.Type + ")이 달라 와이어 크기가 어긋납니다"); continue; }
                            }
                            p.Fields.Add(f);
                        }
                    }
                }
            }

            // ---- 서로의 참조와 크기
            var byId = new Dictionary<long, List<PacketDef>>();      // 번호 -> 앞서 나온 패킷들 (같은 번호 검사를 O(n log n)으로)
            foreach (PacketDef p in schema.Packets)
            {
                if (!byId.TryGetValue(p.Id, out List<PacketDef>? list)) byId[p.Id] = list = new List<PacketDef>();
                PacketDef[] sameId = list.ToArray();
                list.Add(p);
                PacketGroup? g = schema.FindGroup(p.Group);
                if (g == null) { errors.Add(PacketSheet + ": " + p.Name + ": Group '" + p.Group + "'이(가) PacketGroup 시트에 없습니다"); continue; }
                if (p.Id < g.MinId || p.Id > g.MaxId)
                    errors.Add(PacketSheet + ": " + p.Name + ": Id " + p.Id.ToString(CultureInfo.InvariantCulture) + "이(가) 그룹 " + g.Name + " (" + g.MinId.ToString(CultureInfo.InvariantCulture) + ".." + g.MaxId.ToString(CultureInfo.InvariantCulture) + ") 범위 밖입니다");
                foreach (PacketDef o in sameId)
                    errors.Add(PacketSheet + ": " + p.Name + ": Id " + p.Id.ToString(CultureInfo.InvariantCulture) + "이(가) " + o.Name + "과 같습니다");
                int size = PacketHeaderBytes;
                foreach (PacketField f in p.Fields) size += f.Bytes;
                p.Size = size;
                if (p.Size > 65535) errors.Add(PacketSheet + ": " + p.Name + ": 패킷 크기가 65535를 넘습니다");
                if (p.Cs != "None")
                    foreach (PacketField f in p.Fields)
                        if (f.Type == "char") errors.Add(PacketFieldSheet + ": " + p.Name + "." + f.Name + ": char 배열은 C# 생성(Cs=" + p.Cs + ")을 지원하지 않습니다. Cs를 None으로 하세요");
            }
            if (schema.Packets.Count > 0 && schema.Groups.Count == 0) errors.Add(PacketGroupSheet + ": Packet 시트가 있으면 PacketGroup 시트도 있어야 합니다");
            return errors.Count == before;
        }
    }
}
