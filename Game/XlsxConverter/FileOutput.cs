using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace XlsxConverter
{
    /// <summary>생성 파일 묶음 쓰기: 내용이 같은 파일은 쓰지 않고, 이번에 만들지 않은 낡은 생성 파일을 지운다.</summary>
    internal static class FileOutput
    {
        /// <summary>
        /// 생성 파일 묶음을 폴더에 쓴다. 내용이 같은 파일은 쓰지 않고, 이번에 만들지 않은 낡은 생성 파일(*.gen.&lt;ext&gt;)은 지운다.
        /// </summary>
        /// <param name="ext">".h" 또는 ".cs"</param>
        public static void WriteGeneratedSet(string dir, IReadOnlyList<GeneratedFile> files, string ext, TextWriter stdout)
        {
            Directory.CreateDirectory(dir);

            int written = 0, unchanged = 0;
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (GeneratedFile f in files)
            {
                keep.Add(f.Name);
                string path = Path.Combine(dir, f.Name);
                if (FileUtil.WriteFileIfChanged(path, FileUtil.Utf8WithBom(f.Content))) ++written;
                else ++unchanged;
            }

            // 지울 파일은 이름이 *.gen<ext>이고 생성기 표식이 있는 것만으로 한정한다. (손으로 만든 파일은 건드리지 않는다)
            int removed = 0;
            string suffix = ".gen" + ext;
            foreach (string file in Directory.EnumerateFiles(dir).ToList())
            {
                string name = Path.GetFileName(file);
                if (name.Length <= suffix.Length || !name.EndsWith(suffix, StringComparison.Ordinal) || keep.Contains(name))
                    continue;

                byte[] head = new byte[511];
                int n;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    n = fs.Read(head, 0, head.Length);
                if (!Encoding.UTF8.GetString(head, 0, n).Contains(CodeGenCommon.GeneratedMarker, StringComparison.Ordinal)) continue;

                File.Delete(file);
                ++removed;
            }

            stdout.Write(dir + ": " + written + " written, " + unchanged + " unchanged, " + removed + " removed\n");
        }
    }

    /// <summary>
    /// 증분 실행 상태 파일. 첫 부분은 실행 조건(인자와 도구 빌드), "--" 줄 다음 줄은 엑셀 상태(src=), 그 뒤는 만들어진 출력 파일 목록이다.
    /// 증분 실행이 아니어도 매번 쓴다. 출력 목록이 있어야 사라진 시트의 낡은 JSON을 지울 수 있다.
    /// </summary>
    internal sealed class Stamp
    {
        public const string FileName = ".XlsxConverter.stamp";

        public string Head { get; private set; } = string.Empty;           // 실행 조건. "--\n" 줄까지 포함
        public string Src { get; private set; } = string.Empty;            // "src=" 뒤의 엑셀 상태
        public List<string> Produced { get; } = new List<string>();        // 만들어진 파일의 절대 경로

        /// <returns>읽어서 형식이 맞으면 true</returns>
        public bool Load(string file)
        {
            string text;
            try { text = FileUtil.Utf8NoBom.GetString(File.ReadAllBytes(file)); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }

            int pos = 0;
            for (; ; )
            {
                int eol = text.IndexOf('\n', pos);
                if (eol < 0) return false;
                bool last = eol - pos == 2 && text[pos] == '-' && text[pos + 1] == '-';
                pos = eol + 1;
                if (last) break;
            }
            Head = text.Substring(0, pos);

            int e2 = text.IndexOf('\n', pos);
            if (e2 < 0 || string.CompareOrdinal(text, pos, "src=", 0, 4) != 0) return false;
            Src = text.Substring(pos + 4, e2 - pos - 4);

            Produced.Clear();
            for (pos = e2 + 1; pos < text.Length;)
            {
                e2 = text.IndexOf('\n', pos);
                if (e2 < 0) e2 = text.Length;
                if (e2 > pos) Produced.Add(text.Substring(pos, e2 - pos));
                pos = e2 + 1;
            }
            return true;
        }

        public static string AbsolutePath(string p)
        {
            try { return Path.GetFullPath(p); }
            catch (Exception) { return p; }
        }

        // 실행 파일의 수정 시각. 도구가 다시 빌드되면 같은 엑셀에서도 생성 결과가 달라질 수 있어 상태 파일에 포함한다.
        public static string ExeStamp()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (Assembly? asm in new[] { Assembly.GetEntryAssembly(), typeof(Stamp).Assembly })
                {
                    string loc = asm?.Location ?? string.Empty;
                    if (loc.Length == 0 && asm == Assembly.GetEntryAssembly()) loc = Environment.ProcessPath ?? string.Empty;
                    if (loc.Length == 0) continue;
                    if (sb.Length > 0) sb.Append('/');
                    sb.Append(File.GetLastWriteTimeUtc(loc).Ticks);
                }
                return sb.Length == 0 ? "unknown" : sb.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        // 같은 인자, 같은 도구 빌드로 만든 출력인지 가리는 문자열.
        public static string MakeSignature(Options opt)
        {
            string s = "XlsxConverter stamp v3 exe=" + ExeStamp() + "\n";
            s += "xlsx=" + AbsolutePath(opt.XlsxPath) + "\n";
            s += "out=" + AbsolutePath(opt.OutDir) + "\n";
            if (opt.HasCppOut) s += "cpp=" + AbsolutePath(opt.CppOutDir) + "\n";
            if (opt.HasCsOut) s += "cs=" + AbsolutePath(opt.CsOutDir) + "\nns=" + opt.CsNamespace + "\n";
            if (opt.HasProtoCsOut) s += "pcs=" + AbsolutePath(opt.ProtoCsOutDir) + "\npns=" + opt.ProtoCsNamespace + "\n";
            if (opt.HasProtoSizesOut) s += "psz=" + AbsolutePath(opt.ProtoSizesOut) + "\n";
            if (opt.HasSheets) foreach (string n in opt.Sheets) s += "sheet=" + n + "\n";
            return s + "--\n";
        }

        // 엑셀 파일(들)의 수정 시각과 크기. 폴더이면 파일마다 이름/수정 시각/크기를 이어 붙인다. (파일이 늘거나 줄거나 이름이 바뀌어도 달라진다)
        public static string SourceStamp(IReadOnlyList<string> files)
        {
            var all = new StringBuilder();
            foreach (string p in files)
            {
                try
                {
                    var info = new FileInfo(p);
                    if (!info.Exists) { all.Append("?;"); continue; }
                    all.Append(Path.GetFileName(p)).Append('=').Append(info.LastWriteTimeUtc.Ticks).Append('/').Append(info.Length).Append(';');
                }
                catch (Exception)
                {
                    all.Append("?;");
                }
            }
            return all.Length == 0 ? "?" : all.ToString();
        }

        /// <summary>
        /// 상태 파일이 현재 인자와 맞고 출력 파일이 모두 남아 있는지 확인합니다.
        /// </summary>
        /// <param name="sourceStamp">지금 엑셀의 상태(SourceStamp). 지난 실행 때와 같을 때만 true. null이면 엑셀 상태는 보지 않는다</param>
        public static bool Matches(Options opt, string signature, string? sourceStamp)
        {
            var stamp = new Stamp();
            if (!stamp.Load(Path.Combine(opt.OutDir, FileName)) || stamp.Head != signature) return false;

            // 엑셀의 수정 시각/크기가 지난 실행 때와 같아야 한다. (이전 버전을 시각 보존 복사로 되돌린 경우도 다시 변환하려고 '새로움'이 아니라 '같음'으로 비교)
            if (sourceStamp != null && stamp.Src != sourceStamp) return false;

            foreach (string p in stamp.Produced)
                if (!File.Exists(p) && !Directory.Exists(p)) return false;
            return true;
        }

        /// <summary>
        /// 지난 실행이 만들었지만 이번에는 만들지 않은 JSON을 지웁니다. (시트를 지우거나 이름을 바꾼 경우)
        /// 지난 실행의 상태 파일에 적힌 파일 중 출력 폴더 바로 아래의 .json만 대상으로 한다. 손으로 만든 파일은 건드리지 않는다.
        /// </summary>
        public static void RemoveStaleJson(Options opt, Stamp previous, ISet<string> currentJson, TextWriter stdout)
        {
            string outDir = Path.GetDirectoryName(AbsolutePath(Path.Combine(opt.OutDir, "x"))) ?? string.Empty;
            StringComparison cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (string p in previous.Produced)
            {
                if (!string.Equals(Path.GetExtension(p), ".json", StringComparison.OrdinalIgnoreCase) || currentJson.Contains(p)
                    || !string.Equals(Path.GetDirectoryName(p) ?? string.Empty, outDir, cmp))
                    continue;

                try
                {
                    if (!File.Exists(p)) continue;
                    File.Delete(p);
                    stdout.Write("removed stale: " + p + "\n");
                }
                catch (Exception) { /* 지우지 못하면 다음 실행에서 다시 시도한다 */ }
            }
        }
    }
}
