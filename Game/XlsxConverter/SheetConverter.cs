using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XlsxConverter
{
    /// <summary>
    /// 시트 하나를 {"시트이름": [ {...}, ... ]} 형태의 JSON 문자열로 변환한다.
    /// 1행은 "필드명:타입[min..max]?" 헤더이며 2행부터 데이터다.
    /// 타입: int, uint, float, string, bool.  [min..max]: int/uint 값의 허용 범위(한쪽 생략 가능).
    /// enum: 타입 자리에 Enum 시트에 정의한 enum 이름을 쓴다. (JSON에는 멤버 이름 문자열이 들어간다)
    /// ?: 선택 열. 빈 칸이면 오류 대신 그 행의 JSON 필드를 생략한다.
    /// 첫 칸이 '#'로 시작하는 행과 완전히 빈 행은 건너뛴다. 오류는 멈추지 않고 모두 수집한다.
    /// </summary>
    public static class SheetConverter
    {
        // 범위 한쪽 끝("-1000", "")을 읽습니다.
        private static bool ParseBound(string text, out bool has, out long value)
        {
            has = false;
            value = 0;
            if (text.Length == 0) return true;
            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v)) return false;
            has = true;
            value = v;
            return true;
        }

        // 헤더 칸 하나("Name:type[min..max]?")를 해석합니다.
        private static bool ParseHeaderCell(string text, Definitions? defs, FieldDef def, out string why)
        {
            why = string.Empty;
            int colon = text.IndexOf(':');
            if (colon < 0) { why = "'Name:type' 형식이어야 합니다"; return false; }

            def.Name = TextUtil.Trim(text.Substring(0, colon));
            string rest = TextUtil.Trim(text.Substring(colon + 1));

            if (rest.Length > 0 && rest[rest.Length - 1] == '?')
            {
                def.Optional = true;
                rest = TextUtil.Trim(rest.Substring(0, rest.Length - 1));
            }

            string range = string.Empty;
            bool hasRange = false;
            int open = rest.IndexOf('[');
            if (open >= 0)
            {
                if (rest[rest.Length - 1] != ']') { why = "범위는 [min..max] 형식이어야 합니다"; return false; }
                range = rest.Substring(open + 1, rest.Length - open - 2);
                rest = TextUtil.Trim(rest.Substring(0, open));
                hasRange = true;
            }
            def.Type = rest;

            TypeInfo? info = TypeInfo.Find(def.Type);
            if (def.Name.Length == 0) { why = "필드명이 비어 있습니다"; return false; }
            if (info == null)
            {
                // 기본 타입이 아니면 Enum 시트의 enum 이름이어야 한다. (범위는 못 쓴다)
                if (!hasRange && defs != null && defs.FindEnum(def.Type) != null)
                {
                    def.EnumName = def.Type;
                    def.Type = "enum";
                    return true;
                }
                why = "타입은 int, uint, float, string, bool 또는 Enum 시트에 정의한 enum 이름이어야 합니다 ('" + def.Type + "')";
                return false;
            }

            if (!hasRange) return true;
            if (def.Type != "int" && def.Type != "uint") { why = "범위 [min..max]는 int, uint에만 쓸 수 있습니다"; return false; }

            int dots = range.IndexOf("..", StringComparison.Ordinal);
            if (dots < 0) { why = "범위는 [min..max] 형식이어야 합니다 (한쪽은 생략 가능)"; return false; }
            bool okMin = ParseBound(TextUtil.Trim(range.Substring(0, dots)), out bool hasMin, out long minV);
            bool okMax = ParseBound(TextUtil.Trim(range.Substring(dots + 2)), out bool hasMax, out long maxV);
            if (!okMin || !okMax)
            {
                why = "범위 끝은 정수여야 합니다";
                return false;
            }
            def.HasMin = hasMin; def.MinV = minV;
            def.HasMax = hasMax; def.MaxV = maxV;

            long lo = info.MinV;
            long hi = info.MaxV;
            if ((def.HasMin && (def.MinV < lo || def.MinV > hi)) || (def.HasMax && (def.MaxV < lo || def.MaxV > hi)))
            {
                why = "범위가 " + def.Type + "의 표현 범위(" + lo + ".." + hi + ")를 벗어났습니다";
                return false;
            }
            if (def.HasMin && def.HasMax && def.MinV > def.MaxV) { why = "범위의 min이 max보다 큽니다"; return false; }
            return true;
        }

        /// <summary>
        /// 셀 하나를 필드 정의의 타입(범위 포함)으로 변환해 JSON 값 문자열로 만듭니다. (Const 시트도 같은 규칙을 쓴다)
        /// </summary>
        /// <param name="defs">enum 필드의 멤버 이름 검사에 쓰는 정의. enum이 아니면 null 가능</param>
        public static bool ConvertCellValue(Cell cell, FieldDef def, Definitions? defs, out string json, out string why)
        {
            json = string.Empty;
            why = string.Empty;
            string type = def.Type;
            if (IsBlank(cell)) { why = "빈 칸"; return false; }

            if (type == "string") { json = TextUtil.JsonString(ValueFormat.CellToString(cell)); return true; }

            if (type == "enum")
            {
                // JSON에는 멤버 이름을 그대로 넣는다. 오타는 여기서 잡는다.
                string name = TextUtil.Trim(ValueFormat.CellToString(cell));
                EnumDef? e = defs?.FindEnum(def.EnumName);
                if (e == null) { why = "enum 정의를 찾을 수 없음: " + def.EnumName; return false; }
                if (e.HasMember(name)) { json = TextUtil.JsonString(name); return true; }

                var allowed = new StringBuilder();
                foreach (EnumMember m in e.Members)
                    allowed.Append(allowed.Length == 0 ? "" : ", ").Append(m.Name);
                why = def.EnumName + "의 멤버가 아님: " + TextUtil.Quote(name) + " (허용: " + allowed + ")";
                return false;
            }

            if (type == "bool")
            {
                if (cell.Kind == CellKind.Bool) { json = cell.Boolean ? "true" : "false"; return true; }
                string s = ValueFormat.Lower(TextUtil.Trim(ValueFormat.CellToString(cell)));
                if (s == "1" || s == "true" || s == "yes" || s == "y") { json = "true"; return true; }
                if (s == "0" || s == "false" || s == "no" || s == "n") { json = "false"; return true; }
                why = "bool이 아님: " + (cell.Kind == CellKind.Str ? TextUtil.Quote(cell.Text) : ValueFormat.CellToString(cell));
                return false;
            }

            if (cell.Kind == CellKind.Bool) { why = "숫자가 아님: " + ValueFormat.CellToString(cell); return false; }

            if (!ValueFormat.ParseNumber(cell, out double num, out why)) return false;

            if (type == "float") { json = ValueFormat.FloatRepr(num); return true; }

            // 오류 메시지에 인용할 원래 값 (성공 경로에서는 만들지 않는다)
            string Shown() => cell.Kind == CellKind.Str ? TextUtil.Quote(cell.Text) : ValueFormat.CellToString(cell);
            if (num != Math.Floor(num)) { why = "정수가 아님: " + Shown(); return false; }
            if (type == "uint" && num < 0) { why = "음수는 uint가 될 수 없음: " + Shown(); return false; }

            // 헤더에 범위가 없어도 C++ 행 구조체(int32/uint32)에 담기는 값이어야 한다.
            TypeInfo info = TypeInfo.Find(type)!;
            double lo = def.HasMin ? def.MinV : info.MinV;
            double hi = def.HasMax ? def.MaxV : info.MaxV;
            if (num < lo || num > hi)
            {
                why = Shown() + "은(는) 허용 범위 " + ((long)lo).ToString(CultureInfo.InvariantCulture) + ".." + ((long)hi).ToString(CultureInfo.InvariantCulture) + "를 벗어남";
                return false;
            }

            json = num == 0 ? "0" : ((long)num).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>비어 있는 셀인지 확인합니다. (값이 없거나 공백뿐인 문자열)</summary>
        public static bool IsBlank(in Cell cell)
            => cell.Kind == CellKind.Empty || (cell.Kind == CellKind.Str && TextUtil.Trim(cell.Text).Length == 0);

        /// <summary>주석 행인지 확인합니다. (첫 칸이 문자열이고 '#'로 시작)</summary>
        public static bool IsCommentRow(Cell[] row)
        {
            if (row.Length == 0 || row[0].Kind != CellKind.Str) return false;
            string t = TextUtil.Trim(row[0].Text);
            return t.Length > 0 && t[0] == '#';
        }

        private static bool AllBlank(Cell[] row, int from = 0)
        {
            for (int i = from; i < row.Length; ++i)
                if (!IsBlank(row[i])) return false;
            return true;
        }

        /// <summary>변환하지 않는 행인지 확인합니다. (첫 칸이 '#'로 시작하는 주석 행, 완전히 빈 행)</summary>
        public static bool IsSkipRow(Cell[] row) => IsCommentRow(row) || AllBlank(row);

        /// <summary>오류 메시지의 행 위치 접두사 "시트!행번호행 "을 만듭니다. (행 번호는 엑셀의 1-base 번호)</summary>
        public static string RowLabel(string sheet, int row) => sheet + "!" + row.ToString(CultureInfo.InvariantCulture) + "행 ";

        /// <summary>시트를 변환합니다.</summary>
        /// <param name="errors">오류 메시지 목록. 이 시트의 오류를 뒤에 덧붙인다</param>
        /// <param name="jsonOut">변환 결과 JSON (성공 시)</param>
        /// <param name="rowCount">변환된 데이터 행 수 (성공 시)</param>
        /// <param name="schemaOut">헤더에서 읽은 스키마 (성공 시). 필요 없으면 null</param>
        /// <param name="defs">enum 열의 멤버 이름 검사에 쓰는 정의. enum 열이 없으면 null 가능</param>
        /// <param name="warnings">변환은 되지만 알려야 할 내용(주석으로 보이지 않는 '#' 행)을 덧붙인다. 필요 없으면 null</param>
        /// <returns>오류가 하나도 없으면 true</returns>
        public static bool Convert(string sheet, Rows rows, List<string> errors, out string jsonOut, out int rowCount,
            SheetSchema? schemaOut = null, Definitions? defs = null, List<string>? warnings = null)
        {
            jsonOut = string.Empty;
            rowCount = 0;
            bool hasHeader = rows.TryGetValue(1, out Cell[]? header);
            var fields = new List<FieldDef>();
            int errBefore = errors.Count;

            if (!hasHeader || AllBlank(header!))
            {
                errors.Add(!hasHeader && rows.Count == 0 ? sheet + ": 시트가 비어 있습니다" : sheet + ": 헤더가 비어 있습니다");
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < header!.Length; ++i)
            {
                if (IsBlank(header[i])) continue;
                string text = TextUtil.Trim(ValueFormat.CellToString(header[i]));
                string where = sheet + "!" + (i + 1).ToString(CultureInfo.InvariantCulture) + "열 헤더 '" + text + "'";

                var def = new FieldDef();
                if (!ParseHeaderCell(text, defs, def, out string why)) { errors.Add(where + ": " + why); continue; }
                if (!seen.Add(def.Name)) { errors.Add(where + ": 필드명 중복"); continue; }
                def.Col = i;
                fields.Add(def);
            }
            if (fields.Count == 0)
            {
                if (errors.Count == errBefore) errors.Add(sheet + ": 헤더가 비어 있습니다");
                return false;
            }

            // 필드마다 JSON 키 부분("      \"이름\": ")은 한 번만 만든다.
            var keys = new List<string>(fields.Count);
            foreach (FieldDef f in fields)
                keys.Add("      " + TextUtil.JsonString(f.Name) + ": ");

            // 본문은 sb에 바로 쓴다. 실패하면 버린다.
            var sb = new StringBuilder();
            sb.Append("{\n  ").Append(TextUtil.JsonString(sheet)).Append(": ");
            int count = 0;
            Cell emptyCell = default;
            foreach (KeyValuePair<int, Cell[]> kv in rows)
            {
                if (kv.Key < 2) continue;
                Cell[] row = kv.Value;
                if (IsSkipRow(row))
                {
                    // 첫 칸만 '#'이고 나머지는 비어 있어야 주석이다. 다른 칸에 값이 있으면 데이터를 주석으로 잘못 본 것일 수 있다.
                    if (warnings != null && IsCommentRow(row) && !AllBlank(row, 1))
                        warnings.Add(RowLabel(sheet, kv.Key) + "첫 칸이 '#'로 시작해 주석으로 건너뜁니다. 다른 칸에도 값이 있으니 데이터가 맞는지 확인하세요");
                    continue;
                }

                sb.Append(count > 0 ? ",\n    {\n" : "[\n    {\n");
                bool first = true;
                for (int i = 0; i < fields.Count; ++i)
                {
                    FieldDef f = fields[i];
                    Cell cell = f.Col < row.Length ? row[f.Col] : emptyCell;
                    if (f.Optional && IsBlank(cell)) continue;

                    if (!ConvertCellValue(cell, f, defs, out string json, out string why))
                    {
                        errors.Add(RowLabel(sheet, kv.Key) + f.Name + ": " + why);
                        continue;
                    }
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append(keys[i]);
                    sb.Append(json);
                }
                sb.Append("\n    }");
                ++count;
            }

            if (errors.Count != errBefore) return false;
            sb.Append(count > 0 ? "\n  ]" : "[]");
            sb.Append("\n}\n");
            jsonOut = sb.ToString();
            rowCount = count;
            if (schemaOut != null)
            {
                schemaOut.Name = sheet;
                schemaOut.Fields = fields;
            }
            return true;
        }
    }
}
