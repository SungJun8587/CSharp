//***************************************************************************
// Program.cs : 진입점. 옵션은 BotClient 와 같은 방식(--config JSON + 명령행 덮어쓰기).
//   GameViewer.App.exe                      -> GameViewer.json 이 있으면 읽고, 없으면 기본값
//   GameViewer.App.exe --demo               -> 서버 없이 로컬 시뮬레이션으로 화면 확인
//   GameViewer.App.exe --host 127.0.0.1 --hero 2 --auto
//***************************************************************************
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GameViewer.App.Render;
using GameViewer.Core;
using GameViewer.Core.Data;

namespace GameViewer.App
{
    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            // 고해상도(배율 125% 이상) 화면에서 흐려지지 않도록 DPI 인식 (Windows 가 아니면 무시)
            try { SetProcessDPIAware(); } catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool demo = false;
            System.Collections.Generic.List<string> rest = new System.Collections.Generic.List<string>();
            foreach (string a in args) { if (a == "--demo") demo = true; else rest.Add(a); }

            // 옵션이 하나도 없으면 실행 파일 옆의 GameViewer.json 을 기본 설정으로 쓴다.
            if (!rest.Contains("--config") && !rest.Contains("--help") && !rest.Contains("--dump-config"))
            {
                string def = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameViewer.json");
                if (File.Exists(def)) { rest.Insert(0, def); rest.Insert(0, "--config"); }
            }

            string error; bool exitNow;
            ViewerConfig cfg = ViewerConfig.FromArgs(rest.ToArray(), out error, out exitNow);
            if (cfg == null)
            {
                MessageBox.Show(error + "\r\n\r\n" + ViewerConfig.Usage + "\r\n  --demo                서버 없이 로컬 시뮬레이션으로 실행", "GameViewer 옵션 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            if (exitNow)
            {
                MessageBox.Show(ViewerConfig.Usage + "\r\n  --demo                서버 없이 로컬 시뮬레이션으로 실행", "GameViewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            // 같은 PC에서 여러 개를 띄울 수 있게, 이미 실행 중인 뷰어와 겹치지 않는 슬롯을 잡아 CharId 에 더한다(서버는 같은 캐릭터 ID 의 중복 입장을 거부한다).
            // 데모는 서버에 접속하지 않으므로 필요 없다.
            if (!demo && cfg.MultiInstance)
            {
                int slot = InstanceSlot.Acquire(cfg.Host, cfg.Port);
                if (slot < 0)
                {
                    MessageBox.Show("실행 중인 뷰어가 너무 많습니다 (최대 " + InstanceSlot.MaxSlots + "개).", "GameViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                cfg.Slot = slot;
                cfg.CharId += (ulong)slot;
            }

            string dataDir = cfg.DataDir;
            if (!Path.IsPathRooted(dataDir)) dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dataDir);
            if (!Directory.Exists(dataDir) && Directory.Exists(cfg.DataDir)) dataDir = Path.GetFullPath(cfg.DataDir);

            GameData data = GameData.Load(dataDir, cfg.ZoneId);
            if (data.Warnings.Count > 0 && data.Heroes.Count == 0)
            {
                DialogResult r = MessageBox.Show("GameData 를 읽지 못했습니다 (이름/사거리/스킬 정보 없이 실행됩니다).\r\n\r\n폴더: " + dataDir + "\r\n" + string.Join("\r\n", data.Warnings.ToArray()) + "\r\n\r\n계속할까요?",
                    "GameViewer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return 1;
            }

            // Assets/sprites.json 에 지정된 종류는 이미지로, 나머지는 코드로 그린 그림으로 그린다.
            System.Collections.Generic.List<string> assetWarn = new System.Collections.Generic.List<string>();
            AssetSprites.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"), assetWarn);
            Backdrop.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"), assetWarn);       // 영웅 선택 화면 뒷배경 (선택)
            foreach (string w in assetWarn) Console.Error.WriteLine("assets: " + w);

            Application.Run(new MainForm(cfg, data, demo));
            return 0;
        }
    }
}
