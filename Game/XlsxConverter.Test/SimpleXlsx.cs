using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace XlsxConverter.Tests
{
    /// <summary>
    /// 의존성 없는 테스트용 xlsx 리더. (ZipArchive + XML) ClosedXML을 쓸 수 없는 환경에서 변환기 전체를 검증하는 데 쓴다.
    /// 실제 도구는 ClosedXmlWorkbookReader를 쓴다.
    /// </summary>
    public sealed class SimpleXlsxReader : IWorkbookReader
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        private readonly byte[] _bytes;
        private readonly List<(string Name, string Part)> _sheets = new List<(string, string)>();
        private readonly List<string> _shared = new List<string>();

        public SimpleXlsxReader(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                _bytes = ms.ToArray();
            }
            using var zip = new ZipArchive(new MemoryStream(_bytes), ZipArchiveMode.Read);

            XDocument wb = Load(zip, "xl/workbook.xml");
            XDocument rels = Load(zip, "xl/_rels/workbook.xml.rels");
            var targets = rels.Root!.Elements(PkgRel + "Relationship").ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
            foreach (XElement s in wb.Root!.Element(Main + "sheets")!.Elements(Main + "sheet"))
            {
                string target = targets[(string)s.Attribute(Rel + "id")!];
                string part = target.StartsWith("/") ? target.Substring(1) : "xl/" + target;
                _sheets.Add(((string)s.Attribute("name")!, part));
            }

            if (zip.GetEntry("xl/sharedStrings.xml") != null)
                foreach (XElement si in Load(zip, "xl/sharedStrings.xml").Root!.Elements(Main + "si"))
                    _shared.Add(TextOf(si));
        }

        private static XDocument Load(ZipArchive zip, string name)
        {
            ZipArchiveEntry e = zip.GetEntry(name) ?? throw new InvalidDataException("missing part: " + name);
            using Stream s = e.Open();
            return XDocument.Load(s);
        }

        private static string TextOf(XElement e)
            => string.Concat(e.Descendants(Main + "t").Where(t => t.Parent!.Name != Main + "rPh").Select(t => t.Value));

        public IReadOnlyList<string> GetSheetNames() => _sheets.Select(s => s.Name).ToList();

        public bool ReadSheet(string name, out SortedDictionary<int, Cell[]> rows, out string error)
        {
            rows = new SortedDictionary<int, Cell[]>();
            error = string.Empty;
            int idx = _sheets.FindIndex(s => s.Name == name);
            if (idx < 0) { error = name + ": no sheet"; return false; }

            using var zip = new ZipArchive(new MemoryStream(_bytes), ZipArchiveMode.Read);
            XDocument doc = Load(zip, _sheets[idx].Part);
            var found = new List<(int Row, int Col, Cell Cell)>();
            int lastCol = 0;
            foreach (XElement c in doc.Descendants(Main + "c"))
            {
                string r = (string)c.Attribute("r")!;
                int col = 0, i = 0;
                for (; i < r.Length && char.IsLetter(r[i]); ++i) col = col * 26 + (char.ToUpperInvariant(r[i]) - 'A' + 1);
                int row = int.Parse(r.Substring(i), CultureInfo.InvariantCulture);
                string t = (string?)c.Attribute("t") ?? "n";
                string v = (string?)c.Element(Main + "v") ?? string.Empty;
                Cell cell;
                switch (t)
                {
                    case "s": cell = Cell.Str(_shared[int.Parse(v, CultureInfo.InvariantCulture)]); break;
                    case "inlineStr": cell = Cell.Str(TextOf(c.Element(Main + "is")!)); break;
                    case "str": cell = Cell.Str(v); break;
                    case "e": cell = Cell.Str(v); break;
                    case "b": cell = Cell.Bool(v == "1"); break;
                    default:
                        if (v.Length == 0) continue;
                        cell = Cell.Num(CellText.FromDouble(double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture)));
                        break;
                }
                found.Add((row, col, cell));
                if (col > lastCol) lastCol = col;
            }
            foreach (var (row, col, cell) in found)
            {
                if (!rows.TryGetValue(row, out Cell[]? cells)) rows[row] = cells = new Cell[lastCol];
                cells[col - 1] = cell;
            }
            return true;
        }

        public void Dispose() { }
    }

    /// <summary>테스트용 xlsx 작성기. 문자열은 inlineStr, 숫자는 n, bool은 b로 쓴다. null 칸은 건너뛰고, 행 배열이 null이면 빈 행이다.</summary>
    public static class XlsxWriter
    {
        public static void Write(string path, params (string Name, object?[]?[] Rows)[] sheets)
        {
            var main = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            void Put(string name, string xml)
            {
                ZipArchiveEntry e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(xml);
            }

            var ct = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            var wbSheets = new StringBuilder();
            var wbRels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < sheets.Length; ++i)
            {
                int n = i + 1;
                ct.Append("<Override PartName=\"/xl/worksheets/sheet" + n + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                wbSheets.Append("<sheet name=\"" + System.Security.SecurityElement.Escape(sheets[i].Name) + "\" sheetId=\"" + n + "\" r:id=\"rId" + n + "\"/>");
                wbRels.Append("<Relationship Id=\"rId" + n + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + n + ".xml\"/>");

                var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"" + main + "\"><sheetData>");
                for (int r = 0; r < sheets[i].Rows.Length; ++r)
                {
                    object?[]? row = sheets[i].Rows[r];
                    if (row == null) continue;
                    sb.Append("<row r=\"" + (r + 1) + "\">");
                    for (int c = 0; c < row.Length; ++c)
                    {
                        object? v = row[c];
                        if (v == null) continue;
                        string col = ((char)('A' + c)).ToString();
                        string cref = col + (r + 1);
                        switch (v)
                        {
                            case string s:
                                sb.Append("<c r=\"" + cref + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + System.Security.SecurityElement.Escape(s) + "</t></is></c>");
                                break;
                            case bool b:
                                sb.Append("<c r=\"" + cref + "\" t=\"b\"><v>" + (b ? "1" : "0") + "</v></c>");
                                break;
                            default:
                                sb.Append("<c r=\"" + cref + "\"><v>" + Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) + "</v></c>");
                                break;
                        }
                    }
                    sb.Append("</row>");
                }
                sb.Append("</sheetData></worksheet>");
                Put("xl/worksheets/sheet" + n + ".xml", sb.ToString());
            }
            ct.Append("</Types>");
            wbRels.Append("</Relationships>");
            Put("[Content_Types].xml", ct.ToString());
            Put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"" + main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" + wbSheets + "</sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", wbRels.ToString());
        }
    }
}
