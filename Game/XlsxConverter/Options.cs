using System;
using System.Collections.Generic;
using System.IO;

namespace XlsxConverter
{
    /// <summary>명령행 옵션.</summary>
    public sealed class Options
    {
        public string XlsxPath { get; set; } = string.Empty;
        public string OutDir { get; set; } = string.Empty;
        public string CppOutDir { get; set; } = string.Empty;
        public bool HasCppOut { get; set; }
        public string CsOutDir { get; set; } = string.Empty;
        public bool HasCsOut { get; set; }
        public string CsNamespace { get; set; } = "GameDataRows";
        public string CsClientProtoOutDir { get; set; } = string.Empty;       // 패킷 프로토콜 C# 출력 폴더 (--cs-out과 다른 폴더)
        public bool HasCsClientProtoOut { get; set; }
        public string CsClientProtoNamespace { get; set; } = string.Empty;    // 비어 있으면 CsNamespace
        public string CsServerProtoOutDir { get; set; } = string.Empty;   // 서버 패킷 C# 출력 폴더 (IPacket 구조체: Write/TryRead)
        public bool HasCsServerProtoOut { get; set; }
        public string CsServerProtoNamespace { get; set; } = string.Empty; // 비어 있으면 CsNamespace
        public string ProtoSizesOut { get; set; } = string.Empty;       // "<구조체> <바이트>" 목록 파일
        public bool HasProtoSizesOut { get; set; }
        public List<string> Sheets { get; } = new List<string>();
        public bool HasSheets { get; set; }
        public bool Incremental { get; set; }

        private static bool IsDoubleDash(string s) => s.Length >= 2 && s[0] == '-' && s[1] == '-';

        /// <summary>같은 폴더를 가리키는 경로인지 확인합니다. (Windows는 대소문자 무시, 끝 구분자 무시)</summary>
        public static bool SamePath(string a, string b)
        {
            static string Norm(string p)
            {
                string full = Path.GetFullPath(p);
                string root = Path.GetPathRoot(full) ?? string.Empty;
                if (full.Length > root.Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return full;
            }
            return string.Equals(Norm(a), Norm(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        /// <summary>명령행을 해석합니다. 형식이 틀리면 사용법을 stderr에 출력하고 false.</summary>
        public static bool TryParse(string[] argv, TextWriter stderr, out Options opt)
        {
            opt = new Options();
            bool hasOut = false;

            for (int i = 0; i < argv.Length; ++i)
            {
                string arg = argv[i];
                bool hasNext = i + 1 < argv.Length;

                if (arg == "--out" && hasNext) { opt.OutDir = argv[++i]; hasOut = true; }
                else if (arg == "--cpp-out" && hasNext) { opt.CppOutDir = argv[++i]; opt.HasCppOut = true; }
                else if (arg == "--cs-out" && hasNext) { opt.CsOutDir = argv[++i]; opt.HasCsOut = true; }
                else if (arg == "--cs-client-proto-out" && hasNext) { opt.CsClientProtoOutDir = argv[++i]; opt.HasCsClientProtoOut = true; }
                else if (arg == "--cs-client-proto-namespace" && hasNext) { opt.CsClientProtoNamespace = argv[++i]; }
                else if (arg == "--cs-server-proto-out" && hasNext) { opt.CsServerProtoOutDir = argv[++i]; opt.HasCsServerProtoOut = true; }
                else if (arg == "--cs-server-proto-namespace" && hasNext) { opt.CsServerProtoNamespace = argv[++i]; }
                else if (arg == "--proto-sizes-out" && hasNext) { opt.ProtoSizesOut = argv[++i]; opt.HasProtoSizesOut = true; }
                else if (arg == "--cs-namespace" && hasNext) { opt.CsNamespace = argv[++i]; }
                else if (arg == "--incremental") { opt.Incremental = true; }
                else if (arg == "--sheets")
                {
                    opt.HasSheets = true;
                    while (i + 1 < argv.Length && !IsDoubleDash(argv[i + 1]))
                        opt.Sheets.Add(argv[++i]);
                }
                else if (opt.XlsxPath.Length == 0 && !IsDoubleDash(arg)) { opt.XlsxPath = arg; }
                else
                {
                    stderr.Write("알 수 없는 인자: " + argv[i] + "\n");
                    return false;
                }
            }

            if (opt.XlsxPath.Length == 0 || !hasOut)
            {
                stderr.Write("usage: XlsxConverter <GameData folder | file.xlsx> --out <dir> [--cpp-out <dir>] [--cs-out <dir> [--cs-namespace <name>]] [--cs-client-proto-out <dir> [--cs-client-proto-namespace <name>]] [--cs-server-proto-out <dir> [--cs-server-proto-namespace <name>]] [--proto-sizes-out <file>] [--sheets Name ...] [--incremental]\n");
                return false;
            }
            if (opt.HasCsClientProtoOut && opt.HasCsOut && SamePath(opt.CsClientProtoOutDir, opt.CsOutDir))
            {
                // 폴더마다 *.gen.cs 한 묶음을 관리하므로 같은 폴더에 두 묶음이 섞이면 서로의 파일을 낡은 파일로 보고 지운다.
                stderr.Write("--cs-client-proto-out은 --cs-out과 다른 폴더여야 합니다\n");
                return false;
            }
            if (opt.HasCsServerProtoOut && ((opt.HasCsOut && SamePath(opt.CsServerProtoOutDir, opt.CsOutDir)) || (opt.HasCsClientProtoOut && SamePath(opt.CsServerProtoOutDir, opt.CsClientProtoOutDir))))
            {
                stderr.Write("--cs-server-proto-out은 --cs-out, --cs-client-proto-out과 다른 폴더여야 합니다\n");
                return false;
            }
            if (opt.Incremental && opt.HasSheets)
            {
                stderr.Write("--incremental은 --sheets와 함께 쓸 수 없습니다\n");
                return false;
            }
            if ((opt.HasCppOut || opt.HasCsOut || opt.HasCsClientProtoOut || opt.HasCsServerProtoOut || opt.HasProtoSizesOut) && opt.HasSheets)
            {
                // 일부 시트만으로 헤더를 만들면 나머지 시트의 구조체가 사라져 C++ 빌드가 깨진다.
                stderr.Write("--cpp-out/--cs-out/--cs-client-proto-out/--cs-server-proto-out/--proto-sizes-out는 --sheets와 함께 쓸 수 없습니다 (생성 코드는 모든 시트로 만들어야 합니다)\n");
                return false;
            }
            return true;
        }
    }
}
