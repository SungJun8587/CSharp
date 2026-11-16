//***************************************************************************
// BgmPlayer.cs : 배경음악 재생(WinForms). 곡 합성은 Core/Audio/Bgm.cs 에 있고, 여기서는 만든 WAV 를 임시 폴더에 쓰고
// Windows 멀티미디어(MCI, winmm.dll)로 반복 재생한다. 효과음(SoundPlayer)과는 장치 인스턴스가 달라 서로 끊지 않는다.
// 크기는 곡 샘플에 곱해 파일로 만든다(5% 단위로 파일을 따로 두고 다시 쓰지 않는다). 곡 합성은 0.5초 안팎이라 작업 스레드에서 한다.
// Windows 가 아니거나 재생이 실패하면 소리 없이 상태만 유지한다(Failed). 같은 존에서 평온<->전투 곡을 바꿀 때는 곡 안의 위치를 이어받는다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using GameViewer.Core.Audio;
using GameViewer.Core.Logic;

namespace GameViewer.App
{
    public sealed class BgmPlayer : IDisposable
    {
        private const int Rate = 22050;
        private const string Alias = "gvbgm";
        private static readonly bool Win = Environment.OSVersion.Platform == PlatformID.Win32NT;

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr hwnd);

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "GameViewer_bgm");
        private readonly Dictionary<int, float[]> _samples = new Dictionary<int, float[]>();      // 곡(키) -> 샘플
        private readonly HashSet<int> _building = new HashSet<int>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _lock = new object();
        private int _want = -1, _playing = -1, _playingLevel = -1;
        private long _loopStartMs;                  // 현재 곡을 틀기 시작한 시각(곡 안의 위치 계산용)
        private int _gainLevel = 12;                // 0..20 (5% 단위)
        private bool _paused, _open;

        public bool Failed { get; private set; }
        /// <summary>지금 틀고 있는(또는 틀어야 하는) 곡의 설명. 정보 표시용.</summary>
        public string Now { get; private set; }
        public bool Playing { get { return _playing >= 0 && !_paused && _gainLevel > 0; } }
        public bool Audible { get { return Win && !Failed; } }

        private static int Key(MapKind k, bool battle) { return (int)k * 2 + (battle ? 1 : 0); }

        public void SetGain(float g)
        {
            int level = (int)Math.Round(Math.Max(0f, Math.Min(1f, g)) * 20f);
            if (level == _gainLevel) return;
            _gainLevel = level;
            Apply();
        }

        /// <summary>이 곡을 틀어 달라고 요청한다(같은 곡이면 아무 일 없음). 합성이 끝나면 Update 에서 재생이 시작된다.</summary>
        public void Play(MapKind kind, bool battle)
        {
            int key = Key(kind, battle);
            Now = ZoneTheme.NameOf(ZoneTheme.Zones[(int)kind]) + (battle ? " (전투)" : "");
            if (key == _want) return;
            _want = key;
            EnsureBuilt(key);
            Apply();
        }

        public void Stop()
        {
            _want = -1; Now = null;
            Close();
        }

        public void Pause() { if (_paused) return; _paused = true; if (_open) Mci("pause " + Alias); }
        public void Resume() { if (!_paused) return; _paused = false; if (_open) Mci("resume " + Alias); else Apply(); }

        /// <summary>매 프레임: 합성이 끝난 곡이 있으면 재생을 시작한다.</summary>
        public void Update() { if (_want >= 0 && _want != _playing) Apply(); }

        private void EnsureBuilt(int key)
        {
            lock (_lock) { if (_samples.ContainsKey(key) || !_building.Add(key)) return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    float[] s = BgmSynth.Make((MapKind)(key / 2), (key & 1) == 1, Rate);
                    lock (_lock) _samples[key] = s;
                }
                catch (Exception) { Failed = true; }
                finally { lock (_lock) _building.Remove(key); }
            });
        }

        private string PathFor(int key, int level) { return Path.Combine(_dir, "bgm_" + key + "_" + level + ".wav"); }

        /// <summary>요청한 곡/크기에 맞춰 재생 상태를 맞춘다.</summary>
        private void Apply()
        {
            if (_want < 0 || _gainLevel <= 0) { Close(); return; }
            float[] s;
            lock (_lock) _samples.TryGetValue(_want, out s);
            if (s == null) return;                                     // 아직 합성 중
            if (_playing == _want && _playingLevel == _gainLevel) return;
            try
            {
                string path = PathFor(_want, _gainLevel);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(_dir);
                    float[] scaled = new float[s.Length];
                    float k = _gainLevel / 20f;
                    for (int i = 0; i < s.Length; ++i) scaled[i] = s[i] * k;
                    File.WriteAllBytes(path, SfxSynth.ToWav16(scaled, Rate));
                }
                long now = _clock.ElapsedMilliseconds;
                long pos = 0;
                bool sameTrackPos = _playing >= 0;                       // 이전 곡이 있었으면 곡 안의 위치를 이어받는다(평온<->전투, 크기 변경)
                if (sameTrackPos) pos = (now - _loopStartMs) % (long)(BgmSynth.LoopSec * 1000f);
                _loopStartMs = now - pos;
                _playing = _want; _playingLevel = _gainLevel;
                if (!Win) return;                                        // 소리는 Windows 에서만. 나머지 환경은 상태만 유지
                Close(false);
                if (Mci("open \"" + path + "\" type waveaudio alias " + Alias) != 0) { Failed = true; return; }
                _open = true;
                if (pos > 0) Mci("seek " + Alias + " to " + pos);
                if (Mci("play " + Alias + " repeat") != 0) { Failed = true; return; }
                if (_paused) Mci("pause " + Alias);
            }
            catch (Exception) { Failed = true; }
        }

        private void Close() { Close(true); }

        private void Close(bool forgetTrack)
        {
            if (_open) { Mci("stop " + Alias); Mci("close " + Alias); _open = false; }
            if (forgetTrack) { _playing = -1; _playingLevel = -1; }
        }

        private static int Mci(string cmd)
        {
            if (!Win) return 0;
            try { return mciSendString(cmd, null, 0, IntPtr.Zero); }
            catch (Exception) { return -1; }
        }

        public void Dispose()
        {
            _want = -1;
            Close();
            try { if (Directory.Exists(_dir)) foreach (string f in Directory.GetFiles(_dir, "bgm_*.wav")) { try { File.Delete(f); } catch { } } } catch { }
        }
    }
}
