using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace XlsxConverter
{
    /// <summary>
    /// Cell/Rows 모델을 ClosedXML로 새 .xlsx에 쓰는 라이터. (IWorkbookWriter 구현, 검증 규칙은 WorkbookWriterRules)
    /// 셀 타입을 보존해서 쓴다: Num은 숫자 셀, Str은 문자열 셀('='로 시작해도 문자열), Bool은 불리언 셀.
    /// 정수 원문은 정수 그대로, 그 외는 double로 쓴다. ClosedXML은 double을 전체 정밀도로 저장하며
    /// 2^53을 넘는 정수는 double로 반올림되므로 큰 ID는 숫자 셀 대신 문자열(Str)로 쓸 것.
    /// 한 객체는 한 스레드에서만 사용한다.
    /// </summary>
    public sealed class ClosedXmlWorkbookWriter : IWorkbookWriter
    {
        private readonly XLWorkbook _book = new XLWorkbook();
        private readonly List<string> _names = new List<string>();

        public int SheetCount => _names.Count;

        public void AddSheet(string name, SortedDictionary<int, Cell[]> rows)
        {
            // 쓰기 전에 값을 모두 검증한다. 도중에 실패해도 통합 문서에는 반쯤 쓴 시트가 남지 않는다.
            WorkbookWriterRules.ValidateSheet(name, _names, rows);

            IXLWorksheet ws = _book.Worksheets.Add(name);
            try
            {
                foreach (KeyValuePair<int, Cell[]> kv in rows)
                {
                    Cell[] row = kv.Value;
                    for (int col = 0; col < row.Length; ++col)
                    {
                        Cell c = row[col];
                        if (WorkbookWriterRules.IsSkipped(c)) continue;

                        IXLCell cell = ws.Cell(kv.Key, col + 1);
                        switch (c.Kind)
                        {
                            case CellKind.Num:
                                WorkbookWriterRules.TryParseNumber(c.Text, out bool isInt, out long iv, out double dv);   // 위에서 검증을 마쳤다
                                if (isInt) cell.Value = iv;
                                else cell.Value = dv;
                                break;
                            case CellKind.Bool:
                                cell.Value = c.Boolean;
                                break;
                            case CellKind.Str:
                                cell.Value = c.Text;            // string은 항상 텍스트 값이다. (수식으로 해석하지 않는다)
                                break;
                        }
                    }
                }
            }
            catch
            {
                ws.Delete();                                    // 실패하면 호출 전 상태로 되돌린다
                throw;
            }
            _names.Add(name);
        }

        public void Save(string filePath)
        {
            if (_names.Count == 0)
                throw new InvalidOperationException("저장할 시트가 없습니다");

            string tmp = filePath + ".tmp";
            try
            {
                _book.SaveAs(tmp);
                FileReplace.Move(tmp, filePath);                // 기존 파일을 덮어쓴다 (잠겨 있으면 몇 번 다시 시도)
            }
            catch (Exception e)
            {
                try { File.Delete(tmp); } catch { /* 임시 파일 정리 실패는 무시 */ }
                if (e is IOException || e is UnauthorizedAccessException)
                    throw new InvalidOperationException("파일을 저장할 수 없습니다: " + e.Message, e);
                throw;
            }
        }

        public void Dispose() => _book.Dispose();
    }
}
