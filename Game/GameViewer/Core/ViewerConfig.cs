//***************************************************************************
// ViewerConfig.cs : 뷰어 실행 옵션. BotClient 와 같은 방식 — JSON 파일(--config) + 명령행(명령행이 우선).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameViewer.Core.Data;
using GameViewer.Core.Net;

namespace GameViewer.Core
{
    public sealed class ViewerConfig
    {
        public string Host = "127.0.0.1";
        public int Port = 7000;
        public ulong CharId = 900001;       // 봇(100000+)과 겹치지 않는 값. 몬스터 전용 대역(최상위 비트)은 쓰지 말 것
        public bool MultiInstance = true;   // 같은 PC에서 여러 개를 띄울 수 있게, 실행할 때마다 CharId 에 빈 슬롯 번호를 더한다(첫 창은 CharId 그대로)
        public int Slot = 0;                // 이 실행이 차지한 슬롯 번호(0부터). 실행 중에 정해지며 설정 파일에는 저장하지 않는다
        public uint ZoneId = 1;
        public uint HeroId = 1;             // 0 = 서버 기본 영웅
        public string DataDir = "GameData"; // XlsxConverter 출력(Hero/Monster/Skill/Status/HeroSkill/Block/Spawn .json) 폴더
        public bool Auto = false;           // 시작하자마자 자동 전투
        public bool SkipHeroSelect = false; // true 면 시작 화면의 영웅 선택을 건너뛰고 HeroId 로 바로 접속한다
        public double PixelsPerUnit = 20;   // 화면 배율(월드 1 unit = N 픽셀)
        public int WindowWidth = 1600;
        public int WindowHeight = 960;
        public int ConnectTimeoutMs = 3000;
        public int MoveIntervalMs = 120;    // 이동 요청 최소 간격
        public bool HostPortExplicit = false;   // 명령행으로 --host/--port 를 줬는가(그러면 마지막으로 고른 서버를 쓰지 않는다)
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
  --auto                시작하자마자 자동 전투
  --skip-select         시작 화면의 영웅 선택을 건너뛰고 --hero 영웅으로 바로 시작
  --scale <n>           화면 배율 px/unit (기본 20)
  --width <n> --height <n>   창 크기
  --help                도움말";

        /// <summary>args 를 해석한다. 오류면 null 과 메시지, 도움말/덤프면 exitNow=true.</summary>
        public static ViewerConfig FromArgs(string[] args, out string error, out bool exitNow)
        {
            error = null; exitNow = false;
            ViewerConfig c = new ViewerConfig();

            // 1) --config 먼저 적용, 2) 나머지 옵션이 덮어씀
            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i] == "--config")
                {
                    if (i + 1 >= args.Length) { error = "--config needs a file"; return null; }
                    string e = c.LoadJson(args[i + 1]);
                    if (e != null) { error = e; return null; }
                }
            }

            for (int i = 0; i < args.Length; ++i)
            {
                string a = args[i];
                string v = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--config": ++i; break;
                    case "--help": case "-h": case "/?": Console.WriteLine(Usage); exitNow = true; return c;
                    case "--auto": c.Auto = true; break;
                    case "--skip-select": c.SkipHeroSelect = true; break;
                    case "--single": c.MultiInstance = false; break;
                    case "--dump-config":
                        if (v == null) { error = "--dump-config needs a file"; return null; }
                        File.WriteAllText(v, new ViewerConfig().ToJson(), new UTF8Encoding(true));
                        Console.WriteLine("saved: " + v); exitNow = true; return c;
                    case "--host": if (!Take(v, a, ref error)) return null; c.Host = v; c.HostPortExplicit = true; ++i; break;
                    case "--data": if (!Take(v, a, ref error)) return null; c.DataDir = v; ++i; break;
                    case "--port": if (!Num(v, a, 1, 65535, ref error)) return null; c.Port = int.Parse(v); c.HostPortExplicit = true; ++i; break;
                    case "--zone": if (!Num(v, a, 1, int.MaxValue, ref error)) return null; c.ZoneId = uint.Parse(v); ++i; break;
                    case "--hero": if (!Num(v, a, 0, int.MaxValue, ref error)) return null; c.HeroId = uint.Parse(v); ++i; break;
                    case "--char-id": if (!Num(v, a, 1, long.MaxValue, ref error)) return null; c.CharId = ulong.Parse(v); c.MultiInstance = false; ++i; break;
                    case "--width": if (!Num(v, a, 320, 8192, ref error)) return null; c.WindowWidth = int.Parse(v); ++i; break;
                    case "--height": if (!Num(v, a, 240, 8192, ref error)) return null; c.WindowHeight = int.Parse(v); ++i; break;
                    case "--scale":
                        double s;
                        if (v == null || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out s) || s < 2 || s > 80)
                        { error = "--scale must be 2..80"; return null; }
                        c.PixelsPerUnit = s; ++i; break;
                    default: error = "unknown option: " + a; return null;
                }
            }
            return c;
        }

        private static bool Take(string v, string name, ref string error)
        {
            if (v == null) { error = name + " needs a value"; return false; }
            return true;
        }

        private static bool Num(string v, string name, long min, long max, ref string error)
        {
            long n;
            if (v == null || !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < min || n > max)
            { error = name + " must be " + min + ".." + max; return false; }
            return true;
        }

        /// <summary>JSON 파일을 읽어 필드를 채운다. 오류면 메시지, 정상이면 null. 없는 키는 현재 값 유지.</summary>
        public string LoadJson(string path)
        {
            try
            {
                Dictionary<string, object> o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (o == null) return path + ": root must be an object";
                Host = MiniJson.Str(o, "Host", Host);
                Port = (int)MiniJson.Num(o, "Port", Port);
                CharId = (ulong)MiniJson.Num(o, "CharId", CharId);
                MultiInstance = MiniJson.Bool(o, "MultiInstance", MultiInstance);
                ZoneId = (uint)MiniJson.Num(o, "ZoneId", ZoneId);
                HeroId = (uint)MiniJson.Num(o, "HeroId", HeroId);
                DataDir = MiniJson.Str(o, "DataDir", DataDir);
                Auto = MiniJson.Bool(o, "Auto", Auto);
                SkipHeroSelect = MiniJson.Bool(o, "SkipHeroSelect", SkipHeroSelect);
                PixelsPerUnit = MiniJson.Num(o, "PixelsPerUnit", PixelsPerUnit);
                WindowWidth = (int)MiniJson.Num(o, "WindowWidth", WindowWidth);
                WindowHeight = (int)MiniJson.Num(o, "WindowHeight", WindowHeight);
                ConnectTimeoutMs = (int)MiniJson.Num(o, "ConnectTimeoutMs", ConnectTimeoutMs);
                MoveIntervalMs = (int)MiniJson.Num(o, "MoveIntervalMs", MoveIntervalMs);
                object so;
                if (o.TryGetValue("Servers", out so) && MiniJson.AsArray(so) != null)
                {
                    List<ServerEntry> list = new List<ServerEntry>();
                    foreach (object it in MiniJson.AsArray(so))
                    {
                        Dictionary<string, object> e = MiniJson.AsObject(it);
                        if (e == null) continue;
                        string h = MiniJson.Str(e, "Host", ""); int pt = (int)MiniJson.Num(e, "Port", 0);
                        if (h.Length == 0 || pt < 1 || pt > 65535) return path + ": Servers entry needs Host and Port 1..65535";
                        list.Add(new ServerEntry(MiniJson.Str(e, "Name", h + ":" + pt), h, pt));
                    }
                    Servers = list;
                }
                if (Port < 1 || Port > 65535) return path + ": Port out of range";
                if (PixelsPerUnit < 2 || PixelsPerUnit > 80) return path + ": PixelsPerUnit must be 2..80";
                if (WindowWidth < 320 || WindowHeight < 240) return path + ": window too small";
                return null;
            }
            catch (Exception e)
            {
                return path + ": " + e.Message;
            }
        }

        public string ToJson()
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"Host\": \"" + Host + "\",\r\n");
            sb.Append("  \"Port\": " + Port.ToString(ci) + ",\r\n");
            sb.Append("  \"CharId\": " + CharId.ToString(ci) + ",\r\n");
            sb.Append("  \"MultiInstance\": " + (MultiInstance ? "true" : "false") + ",\r\n");
            sb.Append("  \"ZoneId\": " + ZoneId.ToString(ci) + ",\r\n");
            sb.Append("  \"HeroId\": " + HeroId.ToString(ci) + ",\r\n");
            sb.Append("  \"DataDir\": \"" + DataDir.Replace("\\", "\\\\") + "\",\r\n");
            sb.Append("  \"Auto\": " + (Auto ? "true" : "false") + ",\r\n");
            sb.Append("  \"SkipHeroSelect\": " + (SkipHeroSelect ? "true" : "false") + ",\r\n");
            sb.Append("  \"PixelsPerUnit\": " + PixelsPerUnit.ToString(ci) + ",\r\n");
            sb.Append("  \"WindowWidth\": " + WindowWidth.ToString(ci) + ",\r\n");
            sb.Append("  \"WindowHeight\": " + WindowHeight.ToString(ci) + ",\r\n");
            sb.Append("  \"ConnectTimeoutMs\": " + ConnectTimeoutMs.ToString(ci) + ",\r\n");
            sb.Append("  \"MoveIntervalMs\": " + MoveIntervalMs.ToString(ci) + ",\r\n");
            sb.Append("  \"Servers\": [");
            for (int i = 0; i < Servers.Count; ++i)
                sb.Append((i > 0 ? ", " : "") + "{\"Name\": \"" + Servers[i].Name.Replace("\"", "\\\"") + "\", \"Host\": \"" + Servers[i].Host + "\", \"Port\": " + Servers[i].Port.ToString(ci) + "}");
            sb.Append("]\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }
    }
}
