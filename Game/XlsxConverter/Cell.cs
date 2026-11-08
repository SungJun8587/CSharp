using System;
using System.Collections.Generic;
using System.Globalization;

namespace XlsxConverter
{
    /// <summary>셀 값의 종류.</summary>
    public enum CellKind
    {
        Empty,
        Str,
        Num,
        Bool,
    }

    /// <summary>
    /// 엑셀 셀 하나의 값. 숫자는 값이 아니라 표기 문자열(Text)로 들고 있다. (읽는 쪽이 정한 최단 왕복 표기)
    /// 기본값(default)은 빈 셀이다.
    /// </summary>
    public readonly struct Cell
    {
        private readonly string? _text;

        public CellKind Kind { get; }
        public string Text => _text ?? string.Empty;
        public bool Boolean { get; }

        private Cell(CellKind kind, string? text, bool boolean)
        {
            Kind = kind;
            _text = text;
            Boolean = boolean;
        }

        public static Cell Str(string text) => new Cell(CellKind.Str, text, false);
        public static Cell Num(string text) => new Cell(CellKind.Num, text, false);
        public static Cell Bool(bool value) => new Cell(CellKind.Bool, null, value);
    }

    /// <summary>
    /// 엑셀 파일 하나를 읽는 리더. 이 인터페이스 뒤에 ClosedXML 어댑터(app 프로젝트)와 테스트용 리더가 있다.
    /// 시트의 행은 1부터 시작하는 엑셀 행 번호를 키로 한다. 값이 있는 칸이 하나도 없는 행은 넣지 않는다.
    /// </summary>
    public interface IWorkbookReader : IDisposable
    {
        /// <summary>시트 이름들 (탭 순서).</summary>
        IReadOnlyList<string> GetSheetNames();

        /// <summary>시트를 읽습니다. 실패하면 false와 사유.</summary>
        bool ReadSheet(string name, out SortedDictionary<int, Cell[]> rows, out string error);
    }

    /// <summary>엑셀 파일 경로로 리더를 여는 함수. 열지 못하면 예외를 던진다.</summary>
    public delegate IWorkbookReader WorkbookOpener(string path);

    /// <summary>리더들이 숫자 셀의 표기를 같은 규칙으로 만들도록 한곳에 둔다.</summary>
    public static class CellText
    {
        /// <summary>
        /// 숫자 셀의 표기. 2^53 이하의 정수 값은 지수 없이 자릿수 그대로, 그 밖에는 최단 왕복 표기(지수가 생길 수 있음)다.
        /// </summary>
        public static string FromDouble(double v)
        {
            if (v == Math.Floor(v) && Math.Abs(v) <= 9007199254740992.0 && !(v == 0 && double.IsNegative(v)))
                return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
