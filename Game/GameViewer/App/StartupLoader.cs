//***************************************************************************
// StartupLoader.cs : 시작할 때 GameData 와 이미지 에셋을 백그라운드 스레드에서 읽으면서 진행률을 알려 준다.
// 창은 바로 뜨고(로딩바), 읽기가 끝나면 GameCanvas 가 영웅 선택 화면으로 넘어간다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GameViewer.App.Render;
using GameViewer.Core;
using GameViewer.Core.Data;

namespace GameViewer.App
{
    public sealed class StartupLoader
    {
        private readonly ViewerConfig _cfg;
        private readonly string _baseDir;
        private readonly object _lock = new object();
        private float _progress;
        private string _label = "";
        private volatile bool _done;
        private GameData _data;
        private Exception _error;
        private readonly List<string> _assetWarnings = new List<string>();
        private string _dataDir = "";

        private const float DataShare = 0.08f;          // 진행률 중 GameData 몫. 나머지는 이미지

        public StartupLoader(ViewerConfig cfg, string baseDir) { _cfg = cfg; _baseDir = baseDir; }

        public bool Done { get { return _done; } }
        public float Progress { get { lock (_lock) return _progress; } }
        public string Label { get { lock (_lock) return _label; } }
        public GameData Data { get { lock (_lock) return _data; } }
        public Exception Error { get { lock (_lock) return _error; } }
        public string DataDir { get { return _dataDir; } }
        public List<string> AssetWarnings { get { lock (_lock) return new List<string>(_assetWarnings); } }

        public void Start()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true; t.Name = "StartupLoader";
            t.Start();
        }

        /// <summary>스레드 없이 끝까지 읽는다(테스트/스냅샷용).</summary>
        public void RunSync() { Run(); }

        private void Set(float p, string label) { lock (_lock) { _progress = Math.Max(_progress, Math.Min(1f, p)); _label = label; } }

        private void Run()
        {
            try
            {
                Set(0f, "게임 데이터");
                string dataDir = _cfg.DataDir;
                if (!Path.IsPathRooted(dataDir)) dataDir = Path.Combine(_baseDir, dataDir);
                if (!Directory.Exists(dataDir) && Directory.Exists(_cfg.DataDir)) dataDir = Path.GetFullPath(_cfg.DataDir);
                _dataDir = dataDir;
                GameData d = GameData.Load(dataDir, _cfg.ZoneId);
                lock (_lock) _data = d;
                Set(DataShare, "이미지");

                List<string> warn = new List<string>();
                SkillIcons.Dir = Path.Combine(_baseDir, "Assets");
                FxSprites.Dir = Path.Combine(Path.Combine(_baseDir, "Assets"), "fx");
                Decor.Dir = Path.Combine(Path.Combine(_baseDir, "Assets"), "props");
                AssetSprites.Load(Path.Combine(_baseDir, "Assets"), warn,
                    delegate (float r, string label) { Set(DataShare + r * (1f - DataShare), label); });
                lock (_lock) _assetWarnings.AddRange(warn);
                Set(1f, "");
            }
            catch (Exception e) { lock (_lock) _error = e; }
            finally { _done = true; }
        }
    }
}
