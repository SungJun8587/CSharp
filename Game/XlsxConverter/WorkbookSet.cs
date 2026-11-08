using System;
using System.Collections.Generic;
using System.IO;

namespace XlsxConverter
{
    /// <summary>여러 엑셀 파일을 시트 이름 하나로 찾을 수 있게 합칩니다.</summary>
    public sealed class WorkbookSet : IDisposable
    {
        private sealed class Owner
        {
            public int Book;
            public string Name = string.Empty;       // 원래 표기
        }

        private readonly List<IWorkbookReader> _books = new List<IWorkbookReader>();
        private readonly List<string> _fileNames = new List<string>();
        private readonly List<string> _names = new List<string>();          // 파일 순서, 파일 안에서는 탭 순서
        private readonly Dictionary<string, Owner> _owner = new Dictionary<string, Owner>(StringComparer.Ordinal);   // 소문자 시트 이름 -> 소유 정보
        private readonly List<string> _duplicates = new List<string>();

        /// <exception cref="InvalidOperationException">파일을 하나도 못 찾았거나 열지 못했을 때</exception>
        public WorkbookSet(string input, IReadOnlyList<string> files, WorkbookOpener opener)
        {
            try
            {
                if (files.Count == 0)
                    throw new InvalidOperationException("no .xlsx file in: " + input);

                foreach (string f in files)
                {
                    string fname = Path.GetFileName(f);
                    IWorkbookReader wb;
                    try { wb = opener(f); }
                    catch (Exception e) { throw new InvalidOperationException(fname + ": " + e.Message, e); }
                    _books.Add(wb);

                    foreach (string sheet in wb.GetSheetNames())
                    {
                        // 시트 이름은 출력 파일 이름(<시트>.json, <시트>.gen.h)이 되고 Windows의 파일 이름은 대소문자를 구분하지 않으므로 대소문자 없이 비교한다.
                        string key = AsciiChar.Lower(sheet);
                        if (_owner.TryGetValue(key, out Owner? o))
                        {
                            // '_' 시트는 변환하지 않으므로 겹쳐도 상관없다. (먼저 나온 것만 남긴다)
                            if (sheet.Length == 0 || sheet[0] != '_')
                            {
                                string where = " (" + (o.Book < _fileNames.Count ? _fileNames[o.Book] : fname) + ", " + fname + ")";
                                _duplicates.Add(o.Name == sheet ? sheet + ": 시트 이름이 두 파일에 있습니다" + where
                                    : sheet + " / " + o.Name + ": 대소문자만 다른 시트 이름이 두 파일에 있습니다. 출력 파일 이름이 같아집니다" + where);
                            }
                            continue;
                        }
                        _owner.Add(key, new Owner { Book = _books.Count - 1, Name = sheet });
                        _names.Add(sheet);
                    }
                    _fileNames.Add(fname);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public IReadOnlyList<string> GetSheetNames() => _names;
        public IReadOnlyList<string> Duplicates => _duplicates;
        public int FileCount => _books.Count;

        public bool ReadSheet(string name, out Rows rows, out string error)
        {
            rows = new Rows();
            error = string.Empty;
            if (!_owner.TryGetValue(AsciiChar.Lower(name), out Owner? o) || o.Name != name) { error = name + ": no sheet"; return false; }
            return _books[o.Book].ReadSheet(name, out rows, out error);
        }

        public void Dispose()
        {
            foreach (IWorkbookReader b in _books)
            {
                try { b.Dispose(); } catch { /* 닫기 실패는 무시한다 */ }
            }
            _books.Clear();
        }
    }
}
