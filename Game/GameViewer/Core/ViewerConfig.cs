//***************************************************************************
// ViewerConfig.cs : 뷰어 실행 옵션. BotClient 와 같은 방식 — JSON 파일(--config) + 명령행(명령행이 우선).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameViewer.Core.Net;
using CommandLine = global::Core.Util.CommandLine;
using ConfigLoader = global::Core.Util.ConfigLoader;

namespace GameViewer.Core
{
    public sealed class ViewerConfig
    {
        public string Host = "127.0.0.1";
        public int Port = 7000;
        public ulong CharId = 900001;       // 봇(100000+)과 겹치지 않는 값. 몬스터 전용 대역(최상위 비트)은 쓰지 말 것
        public bool MultiInstance = true;   // 같은 PC에서 여러 개를 띄울 수 있게, 실행할 때마다 CharId 에 빈 슬롯 번호를 더한다(첫 창은 CharId 그대로)
        [JsonIgnore] public int Slot = 0;                // 이 실행이 차지한 슬롯 번호(0부터). 실행 중에 정해지며 설정 파일에는 저장하지 않는다
        public uint ZoneId = 1;
        public uint HeroId = 1;             // 0 = 서버 기본 영웅
        public string DataDir = "GameData"; // XlsxConverter 출력(Hero/Monster/Skill/Status/HeroSkill/Block/Spawn .json) 폴더
        public bool Auto = true;            // 입장하면 자동 전투(기본 켬. 끄려면 GameViewer.json 의 "Auto": false)
        public bool SkipHeroSelect = false; // true 면 시작 화면의 영웅 선택을 건너뛰고 HeroId 로 바로 접속한다
        public double PixelsPerUnit = 24;   // 화면 배율(월드 1 unit = N 픽셀)
        public string Renderer = "gdi";     // 그리기 구현: "gdi"(GDI+, 기본) 또는 "skia"(SkiaSharp. 앱을 -p:UseSkia=true 로 빌드했을 때만 쓸 수 있다)
        public double EntityScale = 1.45;   // 영웅/몬스터 그림 크기 배율(1 = sprites.json 의 height 그대로). 지형과 화면 배율은 그대로다
        public int WindowWidth = 1490;
        public int WindowHeight = 814;
        public int ConnectTimeoutMs = 3000;
        public int MoveIntervalMs = 120;    // 이동 요청 최소 간격
        [JsonIgnore] public bool HostPortExplicit = false;   // 명령행으로 --host/--port 를 줬는가(그러면 마지막으로 고른 서버를 쓰지 않는다)
        public List<ServerEntry> Servers = new List<ServerEntry>();     // 시작 화면에서 고를 서버 목록(비어 있으면 Host:Port 하나)

        /// <summary>시작 화면의 서버 목록. 설정의 목록에 지금 Host:Port 가 없으면(명령행으로 지정) "직접 지정" 으로 끝에 덧붙인다. 목록이 비면 Host:Port 하나.</summary>
        public List<ServerEntry> ServerEntries()
        {
            List<ServerEntry> l = new List<ServerEntry>(Servers);
            bool has = false;
            for (int i = 0; i < l.Count; ++i) if (l[i].Host == Host && l[i].Port == Port) has = true;
            if (!has) l.Add(new ServerEntry(l.Count == 0 ? "기본 서버" : "직접 지정", Host, Port));
            return l;
        }

        public const string Usage =
@"GameViewer.App.exe [옵션...]
  --config <file>       JSON 설정 파일을 읽음 (명령행 옵션이 파일 값보다 우선)
  --dump-config <file>  기본값 설정 파일 템플릿을 저장하고 종료
  --host <addr>         Gateway 클라이언트 포트 주소 (기본 127.0.0.1)
  --port <n>            Gateway 클라이언트 포트 (기본 7000)
  --char-id <n>         캐릭터 ID (기본 900001). 지정하면 이 값 그대로 쓴다(다중 실행 번호를 더하지 않음)
  --single              다중 실행 번호를 더하지 않고 설정의 CharId 를 그대로 쓴다
  --zone <id>           입장할 존 ID (기본 1)
  --hero <id>           영웅 ID, 0=서버 기본 영웅 (기본 1)
  --data <dir>          GameData JSON 폴더 (기본 GameData)
  --auto                입장하자마자 자동 전투(기본값)
  --no-auto             자동 전투 없이 시작
  --skip-select         시작 화면의 영웅 선택을 건너뛰고 --hero 영웅으로 바로 시작
  --scale <n>           화면 배율 px/unit (기본 24)
  --renderer <gdi|skia> 그리기 구현 (기본 gdi, skia 는 UseSkia 빌드에서만)
  --width <n> --height <n>   창 크기
  --help                도움말";

        /// <summary>args 를 해석한다. 오류면 null 과 메시지, 도움말/덤프면 exitNow=true.</summary>
        public static ViewerConfig FromArgs(string[] args, out string error, out bool exitNow)
        {
            error = null; exitNow = false;
            ViewerConfig c = new ViewerConfig();
            CommandLine cl = new CommandLine(args, new string[] { "auto", "no-auto", "skip-select", "single", "help", "h" });

            if (cl.Has("help") || cl.Has("h") || System.Linq.Enumerable.Contains(cl.Positional, "/?")) { Console.WriteLine(Usage); exitNow = true; return c; }
            foreach (string p in cl.Positional) { error = "unknown option: " + p; return null; }

            // 1) --config 먼저 적용, 2) 나머지 옵션이 덮어씀
            if (cl.Has("config"))
            {
                string file = cl.GetString("config");
                if (file == null) { error = "--config needs a file"; return null; }
                string e = c.LoadJson(file);
                if (e != null) { error = e; return null; }
            }

            if (cl.Has("dump-config"))
            {
                string file = cl.GetString("dump-config");
                if (file == null) { error = "--dump-config needs a file"; return null; }
                File.WriteAllText(file, new ViewerConfig().ToJson(), new UTF8Encoding(true));
                Console.WriteLine("saved: " + file); exitNow = true; return c;
            }

            try
            {
                if (cl.GetBool("auto")) c.Auto = true;
                if (cl.GetBool("no-auto")) c.Auto = false;
                if (cl.GetBool("skip-select")) c.SkipHeroSelect = true;
                if (cl.GetBool("single")) c.MultiInstance = false;
            }
            catch (FormatException e) { error = e.Message; return null; }

            long n; double d; string v;
            if (cl.Has("host")) { if (!Text(cl, "host", out v, ref error)) return null; c.Host = v; c.HostPortExplicit = true; }
            if (cl.Has("data")) { if (!Text(cl, "data", out v, ref error)) return null; c.DataDir = v; }
            if (cl.Has("port")) { if (!Num(cl, "port", 1, 65535, out n, ref error)) return null; c.Port = (int)n; c.HostPortExplicit = true; }
            if (cl.Has("zone")) { if (!Num(cl, "zone", 1, int.MaxValue, out n, ref error)) return null; c.ZoneId = (uint)n; }
            if (cl.Has("hero")) { if (!Num(cl, "hero", 0, int.MaxValue, out n, ref error)) return null; c.HeroId = (uint)n; }
            if (cl.Has("char-id")) { if (!Num(cl, "char-id", 1, long.MaxValue, out n, ref error)) return null; c.CharId = (ulong)n; c.MultiInstance = false; }
            if (cl.Has("width")) { if (!Num(cl, "width", 320, 8192, out n, ref error)) return null; c.WindowWidth = (int)n; }
            if (cl.Has("height")) { if (!Num(cl, "height", 240, 8192, out n, ref error)) return null; c.WindowHeight = (int)n; }
            if (cl.Has("scale"))
            {
                string sv = cl.GetString("scale");
                if (sv == null || !double.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d < 2 || d > 80)
                { error = "--scale must be 2..80"; return null; }
                c.PixelsPerUnit = d;
            }

            if (cl.Has("renderer"))
            {
                if (!Text(cl, "renderer", out v, ref error)) return null;
                v = v.ToLowerInvariant();
                if (v != "gdi" && v != "skia") { error = "--renderer must be gdi or skia"; return null; }
                c.Renderer = v;
            }

            foreach (string u in cl.Unknown()) { error = "unknown option: " + (u.Length == 1 ? "-" : "--") + u; return null; }
            return c;
        }

        private static bool Text(CommandLine cl, string name, out string value, ref string error)
        {
            value = cl.GetString(name);
            if (value == null) { error = "--" + name + " needs a value"; return false; }
            return true;
        }

        private static bool Num(CommandLine cl, string name, long min, long max, out long n, ref string error)
        {
            n = 0;
            string v = cl.GetString(name);
            if (v == null || !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < min || n > max)
            { error = "--" + name + " must be " + min + ".." + max; return false; }
            return true;
        }

        /// <summary>JSON 파일(주석/후행 쉼표 허용, 알 수 없는 키는 오류)을 읽어 필드를 채운다. 오류면 메시지, 정상이면 null. 파일에 없는 키는 기본값이다.</summary>
        public string LoadJson(string path)
        {
            try
            {
                ViewerConfig f = ConfigLoader.Load<ViewerConfig>(path);
                List<ServerEntry> servers = f.Servers ?? new List<ServerEntry>();
                foreach (ServerEntry e in servers)
                    if (e == null || string.IsNullOrEmpty(e.Host) || e.Port < 1 || e.Port > 65535) return path + ": Servers entry needs Host and Port 1..65535";
                foreach (ServerEntry e in servers) if (string.IsNullOrEmpty(e.Name)) e.Name = e.Host + ":" + e.Port;
                if (string.IsNullOrEmpty(f.Host)) return path + ": Host is empty";
                if (f.Port < 1 || f.Port > 65535) return path + ": Port out of range";
                if (f.PixelsPerUnit < 2 || f.PixelsPerUnit > 80) return path + ": PixelsPerUnit must be 2..80";
                if (f.EntityScale < 0.5 || f.EntityScale > 3) return path + ": EntityScale must be 0.5..3";
                if (f.WindowWidth < 320 || f.WindowHeight < 240) return path + ": window too small";

                Host = f.Host; Port = f.Port; CharId = f.CharId; MultiInstance = f.MultiInstance;
                ZoneId = f.ZoneId; HeroId = f.HeroId; DataDir = f.DataDir ?? DataDir; Auto = f.Auto; SkipHeroSelect = f.SkipHeroSelect;
                Renderer = string.Equals(f.Renderer, "skia", StringComparison.OrdinalIgnoreCase) ? "skia" : "gdi";
                PixelsPerUnit = f.PixelsPerUnit; EntityScale = f.EntityScale; WindowWidth = f.WindowWidth; WindowHeight = f.WindowHeight;
                ConnectTimeoutMs = f.ConnectTimeoutMs; MoveIntervalMs = f.MoveIntervalMs; Servers = servers;
                return null;
            }
            catch (InvalidDataException e) { return e.Message; }       // ConfigLoader 가 파일 이름을 이미 붙였다
            catch (Exception e) { return path + ": " + e.Message; }
        }

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글 서버 이름을 그대로 쓴다
        };

        /// <summary>설정 파일 내용(LoadJson 이 읽는 형식).</summary>
        public string ToJson() { return JsonSerializer.Serialize(this, WriteOptions) + Environment.NewLine; }
    }
}
