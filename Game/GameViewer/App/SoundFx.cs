//***************************************************************************
// SoundFx.cs : 효과음 재생(WinForms). 소리 합성/규칙은 Core/Audio 에 있고, 여기서는 WAV 를 SoundPlayer 로 틀기만 한다.
// 같은 종류가 겹쳐 울리도록 종류마다 플레이어 몇 개를 돌려 쓰고, 너무 자주 울리면 건너뛴다. 장치가 없거나 재생이 실패하면 조용히 꺼진다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using GameViewer.Core.Audio;

namespace GameViewer.App
{
    public sealed class SoundFx : IDisposable
    {
        private const int PoolSize = 3;
        private const long MinGapMs = 45;                // 같은 종류의 소리 사이 최소 간격
        private readonly Dictionary<int, SoundPlayer[]> _pool = new Dictionary<int, SoundPlayer[]>();
        private readonly Dictionary<int, int> _next = new Dictionary<int, int>();
        private readonly Dictionary<int, long> _lastMs = new Dictionary<int, long>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _failed;

        public bool Enabled = true;
        public bool Failed { get { return _failed; } }

        /// <summary>gain 이 크면 원래 소리, 작으면 줄인 소리(플레이어는 볼륨을 못 바꾸므로 두 단계를 따로 만든다).</summary>
        public void Play(SfxKind kind, float gain)
        {
            if (!Enabled || _failed) return;
            int level = gain >= 0.7f ? 0 : 1;
            int key = (int)kind * 2 + level;
            long now = _clock.ElapsedMilliseconds;
            long last;
            if (_lastMs.TryGetValue(key, out last) && now - last < MinGapMs) return;
            _lastMs[key] = now;
            try
            {
                SoundPlayer[] players;
                if (!_pool.TryGetValue(key, out players))
                {
                    float[] samples = SfxSynth.Make(kind, SfxSynth.DefaultRate);
                    if (level == 1) for (int i = 0; i < samples.Length; ++i) samples[i] *= 0.4f;
                    byte[] wav = SfxSynth.ToWav16(samples, SfxSynth.DefaultRate);
                    players = new SoundPlayer[PoolSize];
                    for (int i = 0; i < PoolSize; ++i) { players[i] = new SoundPlayer(new MemoryStream(wav)); players[i].Load(); }
                    _pool[key] = players; _next[key] = 0;
                }
                int idx = _next[key]; _next[key] = (idx + 1) % PoolSize;
                players[idx].Play();
            }
            catch (Exception)
            {
                _failed = true;         // 오디오 장치가 없거나 재생 실패: 이후로는 시도하지 않는다
            }
        }

        public void Dispose()
        {
            foreach (SoundPlayer[] ps in _pool.Values) foreach (SoundPlayer p in ps) if (p != null) p.Dispose();
            _pool.Clear();
        }
    }
}
