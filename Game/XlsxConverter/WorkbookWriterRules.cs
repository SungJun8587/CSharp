using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XlsxConverter
{
    /// <summary>셀 모델(Cell/Rows)을 새 .xlsx로 쓰는 라이터. 구현은 app 프로젝트의 ClosedXmlWorkbookWriter.</summary>
    public interface IWorkbookWriter : IDisposable
    {
        /// <summary>시트를 끝에 추가하고 셀 값을 씁니다. 예외가 나도 통합 문서는 호출 전 상태 그대로다.</summary>
        /// <exception cref="InvalidOperationException">이름이 잘못됐거나 중복일 때, 숫자 셀이 숫자가 아닐 때, 문자열을 쓸 수 없을 때, 범위를 벗어났을 때</exception>
        void AddSheet(string name, SortedDictionary<int, Cell[]> rows);

        int SheetCount { get; }

        /// <summary>파일로 저장합니다. 임시 파일에 쓴 뒤 교체해서 기존 파일을 깨뜨리지 않는다. 시트가 없으면 오류.</summary>
        void Save(string filePath);
    }

    /// <summary>
    /// 라이터가 공유하는 검증 규칙. 쓰기 전에 값을 모두 검사해서 도중에 실패해도 반쯤 쓴 시트가 남지 않게 한다.
    /// 문자열은 '='로 시작해도 수식이 아니라 문자열로 저장된다. 빈 문자열(Str)과 Empty 셀은 쓰지 않는다.
    /// Num의 text는 Cell과 같은 규칙의 숫자 원문("5", "0.1", "1E-07")이어야 한다.
    /// </summary>
    public static class WorkbookWriterRules
    {
        public const int MaxCellTextBytes = 32767;      // UTF-8 바이트 기준
        public const int MaxSheetNameLen = 31;          // UTF-16 코드 유닛
        public const int MaxRow = 1048576;
        public const int MaxColumn = 16384;
        public const string InvalidSheetChars = "[]:*?/\\";

        private enum TextError { None, BadUtf8, BadChar }

        // 서로게이트 쌍이 깨졌으면 BadUtf8, XML 1.0에 쓸 수 없는 문자(탭/개행/CR 제외 제어 문자, U+FFFE/FFFF)면 BadChar
        private static TextError CheckText(string s, out int badCp)
        {
            badCp = 0;
            for (int i = 0; i < s.Length; ++i)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return TextError.BadUtf8;
                    ++i;
                    continue;
                }
                if (char.IsLowSurrogate(c)) return TextError.BadUtf8;
                if ((c < 0x20 && c != 0x09 && c != 0x0A && c != 0x0D) || c == 0xFFFE || c == 0xFFFF)
                {
                    badCp = c;
                    return TextError.BadChar;
                }
            }
            return TextError.None;
        }

        /// <summary>셀 주소("B3"). 오류 메시지용.</summary>
        public static string CellAddress(int col0, int row)
        {
            var name = new StringBuilder();
            for (int c = col0 + 1; c > 0; c = (c - 1) / 26)
                name.Insert(0, (char)('A' + (c - 1) % 26));
            return name.Append(row.ToString(CultureInfo.InvariantCulture)).ToString();
        }

        private static void ValidateCellText(string text, int col0, int row)
        {
            string where = "셀 " + CellAddress(col0, row) + ": ";
            switch (CheckText(text, out int bad))
            {
                case TextError.BadUtf8:
                    throw new InvalidOperationException(where + "UTF-8이 아닌 문자열입니다");
                case TextError.BadChar:
                    throw new InvalidOperationException(where + "xlsx에 쓸 수 없는 제어 문자가 있습니다 (U+" + bad.ToString("X4", CultureInfo.InvariantCulture) + ")");
            }
            int bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes > MaxCellTextBytes)
                throw new InvalidOperationException(where + "문자열이 " + MaxCellTextBytes + "바이트(UTF-8)를 넘습니다 (" + bytes + "바이트)");
        }

        /// <summary>시트 이름이 엑셀 규칙에 맞는지 검사합니다. 맞지 않으면 사유를 던집니다.</summary>
        public static void ValidateSheetName(string name)
        {
            if (name.Length == 0)
                throw new InvalidOperationException("시트 이름이 비어 있습니다");
            if (CheckText(name, out _) != TextError.None)
                throw new InvalidOperationException("시트 이름에 쓸 수 없는 문자가 있습니다");
            if (name.Length > MaxSheetNameLen)
                throw new InvalidOperationException("시트 이름이 31자를 넘습니다: " + name);
            if (name.IndexOfAny(InvalidSheetChars.ToCharArray()) >= 0)
                throw new InvalidOperationException("시트 이름에 쓸 수 없는 문자가 있습니다: " + name);
            if (name[0] == '\'' || name[name.Length - 1] == '\'')
                throw new InvalidOperationException("시트 이름은 작은따옴표로 시작하거나 끝날 수 없습니다: " + name);
        }

        /// <summary>Num 원문을 숫자로 해석합니다. '.', 'e', 'E'가 없으면 정수(int64)로, 그 외는 double입니다. nan/inf는 실패.</summary>
        public static bool TryParseNumber(string text, out bool isInt, out long iv, out double dv)
        {
            iv = 0; dv = 0; isInt = false;
            if (text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0
                && text.Length > 0 && text[0] != '+'
                && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out iv))
            {
                isInt = true;
                return true;
            }
            return TextUtil.ParseDouble(text, out dv) && !double.IsNaN(dv) && !double.IsInfinity(dv);
        }

        /// <summary>시트를 추가하기 전에 이름, 중복(대소문자 무시), 모든 셀 값을 검사합니다.</summary>
        public static void ValidateSheet(string name, IEnumerable<string> existingNames, SortedDictionary<int, Cell[]> rows)
        {
            ValidateSheetName(name);
            string key = AsciiChar.Lower(name);
            foreach (string n in existingNames)
                if (AsciiChar.Lower(n) == key)
                    throw new InvalidOperationException("시트 이름이 중복됩니다: " + name);

            foreach (KeyValuePair<int, Cell[]> kv in rows)
            {
                int rowNo = kv.Key;
                Cell[] row = kv.Value;
                if (rowNo < 1 || rowNo > MaxRow)
                    throw new InvalidOperationException("행 번호가 범위를 벗어났습니다: " + rowNo.ToString(CultureInfo.InvariantCulture));
                if (row.Length > MaxColumn)
                    throw new InvalidOperationException("열 수가 범위를 벗어났습니다 (행 " + rowNo.ToString(CultureInfo.InvariantCulture) + ")");

                for (int col = 0; col < row.Length; ++col)
                {
                    Cell c = row[col];
                    if (c.Kind == CellKind.Num)
                    {
                        if (!TryParseNumber(c.Text, out _, out _, out _))
                            throw new InvalidOperationException("셀 " + CellAddress(col, rowNo) + ": 숫자 셀 값이 숫자가 아닙니다: '" + c.Text + "'");
                    }
                    else if (c.Kind == CellKind.Str && c.Text.Length > 0)
                    {
                        ValidateCellText(c.Text, col, rowNo);
                    }
                }
            }
        }

        /// <summary>쓰지 않는 셀인지 확인합니다. (Empty, 빈 문자열)</summary>
        public static bool IsSkipped(Cell c) => c.Kind == CellKind.Empty || (c.Kind == CellKind.Str && c.Text.Length == 0);
    }
}
