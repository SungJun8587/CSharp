using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace XlsxConverter
{
    /// <summary>
    /// ClosedXML로 엑셀 파일 하나를 읽는 리더. (IWorkbookReader 구현)
    ///
    /// 셀 값 규칙
    ///   빈 칸            -> Empty
    ///   텍스트           -> Str (그대로)
    ///   숫자             -> Num (CellText.FromDouble: 2^53 이하 정수는 자릿수 그대로, 그 외는 최단 왕복 표기)
    ///   불리언           -> Bool
    ///   날짜/시간        -> Str (ISO 8601: 자정이면 yyyy-MM-dd, 아니면 yyyy-MM-ddTHH:mm:ss. 원본 xlnt 리더와 같은 방침)
    ///   기간(TimeSpan)   -> Num (일 단위 일련번호)
    ///   오류값(#N/A 등)  -> Str (오류 표기 그대로, 숫자/불리언 열에서 "숫자가 아님"으로 드러난다)
    ///   수식             -> 저장된 계산 결과 값. 계산 결과가 없으면 ClosedXML이 계산한다.
    ///
    /// 파일은 읽기 전용으로 다른 프로그램(Excel)이 열고 있어도 읽을 수 있게 FileShare.ReadWrite | Delete로 연다.
    /// 시트가 숨김이어도 읽는다. (차트 시트처럼 셀이 없는 시트는 워크시트 목록에 없으므로 이름도 나오지 않는다)
    /// </summary>
    public sealed class ClosedXmlWorkbookReader : IWorkbookReader
    {
        private readonly XLWorkbook _book;
        private readonly List<string> _names = new List<string>();

        /// <exception cref="Exception">열 수 없는 파일(없음, 잠김, .xlsx가 아님 등)</exception>
        public ClosedXmlWorkbookReader(string path)
        {
            // ClosedXML이 스트림을 닫은 뒤에도 쓰지 않도록 메모리로 복사해서 연다. (읽는 동안 파일 핸들을 잡고 있지 않는다)
            var buffer = new MemoryStream();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                fs.CopyTo(buffer);
            buffer.Position = 0;
            _book = new XLWorkbook(buffer);

            foreach (IXLWorksheet ws in _book.Worksheets)
                _names.Add(ws.Name);
        }

        public IReadOnlyList<string> GetSheetNames() => _names;

        public bool ReadSheet(string name, out SortedDictionary<int, Cell[]> rows, out string error)
        {
            rows = new SortedDictionary<int, Cell[]>();
            error = string.Empty;

            if (!_book.TryGetWorksheet(name, out IXLWorksheet? ws) || ws == null)
            {
                error = "시트 '" + name + "'가 없습니다";
                return false;
            }

            try
            {
                // 값이 있는 칸만 훑는다. (서식만 있는 칸과 빈 행은 건너뛰므로 큰 시트도 칸 수에 비례해 읽는다)
                var found = new List<(int Row, int Col, Cell Cell)>();
                int lastCol = 0;
                foreach (IXLCell xl in ws.CellsUsed())
                {
                    Cell cell = ToCell(xl);
                    if (cell.Kind == CellKind.Empty) continue;
                    int col = xl.Address.ColumnNumber;
                    found.Add((xl.Address.RowNumber, col, cell));
                    if (col > lastCol) lastCol = col;
                }

                foreach (var (row, col, cell) in found)
                {
                    if (!rows.TryGetValue(row, out Cell[]? cells))
                    {
                        cells = new Cell[lastCol];
                        rows[row] = cells;
                    }
                    cells[col - 1] = cell;
                }
                return true;
            }
            catch (Exception e)
            {
                rows = new SortedDictionary<int, Cell[]>();
                error = name + ": " + e.Message;
                return false;
            }
        }

        private static Cell ToCell(IXLCell xl)
        {
            XLCellValue v = xl.Value;
            if (v.IsBlank) return default;
            if (v.IsText) return Cell.Str(v.GetText());
            if (v.IsBoolean) return Cell.Bool(v.GetBoolean());
            if (v.IsDateTime)
            {
                DateTime d = v.GetDateTime();
                return Cell.Str(d.TimeOfDay == TimeSpan.Zero
                    ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            }
            if (v.IsNumber || v.IsTimeSpan) return Cell.Num(CellText.FromDouble(v.GetUnifiedNumber()));
            return Cell.Str(xl.GetString());                // 오류값(#N/A 등)은 오류 표기 그대로
        }

        public void Dispose() => _book.Dispose();
    }
}
