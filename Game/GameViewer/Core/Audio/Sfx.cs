//***************************************************************************
// Sfx.cs : 효과음 합성과 "어떤 연출에 어떤 소리를 낼까" 규칙. 오디오 장치를 모르는 순수 데이터라 WinForms/Unity 어디서든 쓴다.
//   SfxSynth.Make(kind, rate)      -> 모노 float 샘플(-1..1). Unity: AudioClip.Create(name, n, 1, rate, false).SetData(samples, 0)
//   SfxSynth.ToWav16(samples,rate) -> 16비트 PCM WAV 바이트. WinForms: SoundPlayer(new MemoryStream(wav))
//   SfxRules.TryMap(...)           -> 연출(ViewEffect) 하나에 대응하는 소리 종류와 크기(거리 감쇠 포함)
// 외부 소리 파일 없이 코드로 만든 단순한 소리라서 자리표시자에 가깝다. 나중에 파일로 바꿀 때는 SfxKind 별로 교체하면 된다.
//***************************************************************************
using System;
using GameViewer.Core.World;

namespace GameViewer.Core.Audio
{
    public enum SfxKind { Hit, HitSoft, Heavy, Hurt, Slash, Whoosh, Boom, Heal, Death, Respawn, Banner, Heartbeat, Alarm, Streak }

    public static class SfxSynth
    {
        public const int DefaultRate = 22050;

        /// <summary>종류별 소리를 만든다. 같은 종류는 항상 같은 샘플이 나온다.</summary>
        public static float[] Make(SfxKind kind, int rate)
        {
            if (rate < 8000 || rate > 96000) throw new ArgumentOutOfRangeException("rate");
            switch (kind)
            {
                case SfxKind.Hit: return Thump(rate, 0.11f, 190f, 70f, 0.55f, 0.35f);
                case SfxKind.HitSoft: return Thump(rate, 0.08f, 240f, 110f, 0.35f, 0.25f);
                case SfxKind.Heavy: return Thump(rate, 0.22f, 130f, 45f, 0.75f, 0.45f);
                case SfxKind.Hurt: return Thump(rate, 0.18f, 150f, 60f, 0.6f, 0.55f);
                case SfxKind.Slash: return Sweep(rate, 0.12f, 900f, 2600f, 0.0f, 0.5f, true);
                case SfxKind.Whoosh: return Sweep(rate, 0.2f, 500f, 1500f, 0.2f, 0.4f, true);
                case SfxKind.Boom: return Thump(rate, 0.5f, 90f, 30f, 0.9f, 0.7f);
                case SfxKind.Heal: return Chime(rate, new float[] { 660f, 880f, 1100f }, 0.09f, 0.35f);
                case SfxKind.Death: return Sweep(rate, 0.45f, 420f, 90f, 0.1f, 0.5f, false);
                case SfxKind.Respawn: return Chime(rate, new float[] { 440f, 660f, 880f, 1320f }, 0.08f, 0.4f);
                case SfxKind.Banner: return Chime(rate, new float[] { 220f, 165f }, 0.3f, 0.5f);
                case SfxKind.Heartbeat: return Heartbeat(rate);
                case SfxKind.Alarm: return Alarm(rate);
                case SfxKind.Streak: return Chime(rate, new float[] { 523f, 659f, 784f, 1047f }, 0.07f, 0.45f);
            }
            return new float[0];
        }

        /// <summary>심장 박동 "쿵-쿵": 낮은 사인 두 번(두 번째는 조금 약하게).</summary>
        private static float[] Heartbeat(int rate)
        {
            float[] a = Thump(rate, 0.14f, 70f, 38f, 0.9f, 0.05f);
            float[] b = Thump(rate, 0.16f, 62f, 34f, 0.6f, 0.05f);
            int gap = (int)(rate * 0.17f);
            float[] s = new float[gap + b.Length];
            for (int i = 0; i < a.Length && i < s.Length; ++i) s[i] += a[i];
            for (int i = 0; i < b.Length; ++i) s[gap + i] += b[i];
            return s;
        }

        /// <summary>경보: 높낮이가 번갈아 울리는 사이렌 두 번.</summary>
        private static float[] Alarm(int rate)
        {
            int n = (int)(rate * 0.6f);
            float[] s = new float[n];
            double phase = 0;
            for (int i = 0; i < n; ++i)
            {
                float t = i / (float)n;
                double f = ((int)(t * 4f) & 1) == 0 ? 520.0 : 390.0;
                phase += 2 * Math.PI * f / rate;
                float env = Math.Min(1f, i / 120f) * Math.Min(1f, (n - i) / 400f);
                float sq = (float)Math.Sign(Math.Sin(phase)) * 0.35f + (float)Math.Sin(phase) * 0.65f;
                s[i] = sq * 0.45f * env;
            }
            return s;
        }

        // 잡음(결정적): 같은 입력이면 같은 소리
        private static float Noise(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return ((state >> 8) & 0xFFFF) / 32768f - 1f;
        }

        /// <summary>쿵/퍽: 내려가는 사인파 + 잡음 한 번에 감쇠.</summary>
        private static float[] Thump(int rate, float sec, float f0, float f1, float amp, float noiseMix)
        {
            int n = (int)(rate * sec);
            float[] s = new float[n];
            uint st = 12345u + (uint)(f0 * 7);
            double phase = 0;
            for (int i = 0; i < n; ++i)
            {
                float t = i / (float)n;
                double f = f0 + (f1 - f0) * Math.Sqrt(t);
                phase += 2 * Math.PI * f / rate;
                float env = (float)Math.Pow(1.0 - t, 2.2) * Math.Min(1f, i / 24f);
                float v = (float)Math.Sin(phase) * (1f - noiseMix) + Noise(ref st) * noiseMix * (float)Math.Pow(1.0 - t, 3.0);
                s[i] = v * env * amp;
            }
            return s;
        }

        /// <summary>쉭/휘익: 주파수가 변하는 잡음 섞인 소리.</summary>
        private static float[] Sweep(int rate, float sec, float f0, float f1, float noiseMix, float amp, bool bell)
        {
            int n = (int)(rate * sec);
            float[] s = new float[n];
            uint st = 777u + (uint)f0;
            double phase = 0;
            for (int i = 0; i < n; ++i)
            {
                float t = i / (float)n;
                double f = f0 + (f1 - f0) * t;
                phase += 2 * Math.PI * f / rate;
                float env = bell ? (float)Math.Sin(Math.PI * t) : (float)Math.Pow(1.0 - t, 1.5);
                float v = (float)Math.Sin(phase) * (1f - noiseMix) + Noise(ref st) * noiseMix;
                s[i] = v * env * amp * Math.Min(1f, i / 16f);
            }
            return s;
        }

        /// <summary>띠링: 음을 차례로 겹쳐 울린다.</summary>
        private static float[] Chime(int rate, float[] notes, float step, float amp)
        {
            float tail = 0.25f;
            int n = (int)(rate * (step * notes.Length + tail));
            float[] s = new float[n];
            for (int k = 0; k < notes.Length; ++k)
            {
                int start = (int)(rate * step * k);
                for (int i = start; i < n; ++i)
                {
                    float t = (i - start) / (float)rate;
                    float env = (float)Math.Exp(-t * 9.0) * Math.Min(1f, (i - start) / 20f);
                    s[i] += (float)Math.Sin(2 * Math.PI * notes[k] * t) * env * amp * 0.6f;
                }
            }
            for (int i = 0; i < n; ++i) s[i] = Math.Max(-1f, Math.Min(1f, s[i]));
            return s;
        }

        /// <summary>샘플(-1..1)을 16비트 모노 PCM WAV 파일 바이트로 만든다.</summary>
        public static byte[] ToWav16(float[] samples, int rate)
        {
            int dataLen = samples.Length * 2;
            byte[] b = new byte[44 + dataLen];
            Ascii(b, 0, "RIFF"); Le32(b, 4, 36 + dataLen); Ascii(b, 8, "WAVE");
            Ascii(b, 12, "fmt "); Le32(b, 16, 16); Le16(b, 20, 1); Le16(b, 22, 1);
            Le32(b, 24, rate); Le32(b, 28, rate * 2); Le16(b, 32, 2); Le16(b, 34, 16);
            Ascii(b, 36, "data"); Le32(b, 40, dataLen);
            for (int i = 0; i < samples.Length; ++i)
            {
                float f = Math.Max(-1f, Math.Min(1f, samples[i]));
                Le16(b, 44 + i * 2, (short)Math.Round(f * 32767f));
            }
            return b;
        }

        private static void Ascii(byte[] b, int o, string s) { for (int i = 0; i < s.Length; ++i) b[o + i] = (byte)s[i]; }
        private static void Le16(byte[] b, int o, int v) { b[o] = (byte)(v & 255); b[o + 1] = (byte)((v >> 8) & 255); }
        private static void Le32(byte[] b, int o, int v) { Le16(b, o, v & 0xFFFF); Le16(b, o + 2, (v >> 16) & 0xFFFF); }
    }

    public static class SfxRules
    {
        /// <summary>이 거리(월드 단위)보다 멀리서 나는 소리는 내지 않는다.</summary>
        public const float AudibleRange = 26f;
        /// <summary>이 거리보다 가까우면 큰 소리, 멀면 작은 소리.</summary>
        public const float NearRange = 11f;

        /// <summary>
        /// 연출 하나가 소리를 낼지, 낸다면 어떤 소리를 얼마나 크게(0..1, 거리 감쇠 반영) 낼지 정한다.
        /// 피해 숫자/베기/폭발 같은 "사건"만 소리를 내고, 불꽃(Spark)처럼 사건에 딸린 연출은 중복을 피해 조용하다.
        /// </summary>
        public static bool TryMap(ViewEffect fx, WorldModel w, float selfX, float selfY, out SfxKind kind, out float gain)
        {
            kind = SfxKind.Hit; gain = 0f;
            switch (fx.Kind)
            {
                case EffectKind.Damage:
                    {
                        Entity t = fx.EntityId != 0 ? w.Find(fx.EntityId) : null;
                        bool heavy = t != null && t.MaxHp > 0 && fx.Value * 100 >= t.MaxHp * 12;
                        if (fx.Variant == 2) kind = heavy ? SfxKind.Heavy : SfxKind.Hurt;           // 내가 맞음
                        else if (fx.Variant == 3) kind = heavy ? SfxKind.Heavy : SfxKind.Hit;       // 내가 때림
                        else kind = heavy ? SfxKind.Heavy : SfxKind.HitSoft;                        // 남들끼리
                        break;
                    }
                case EffectKind.Slash: kind = SfxKind.Slash; break;
                case EffectKind.Tracer: case EffectKind.Projectile: kind = SfxKind.Whoosh; break;
                case EffectKind.Ring: kind = SfxKind.Boom; break;
                case EffectKind.Heal: kind = SfxKind.Heal; break;
                case EffectKind.DeathPuff: kind = SfxKind.Death; break;
                case EffectKind.RespawnRing: kind = SfxKind.Respawn; break;
                case EffectKind.Banner: kind = fx.Variant == 1 ? SfxKind.Alarm : SfxKind.Banner; gain = 1f; return true;      // 화면 전체 알림은 거리와 무관
                case EffectKind.Streak: kind = SfxKind.Streak; gain = 1f; return true;
                default: return false;
            }
            float dx = fx.X - selfX, dy = fx.Y - selfY;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            bool mine = fx.Variant == 2 || fx.Variant == 3;
            if (!mine && d > AudibleRange) return false;
            gain = mine || d <= NearRange ? 1f : 1f - (d - NearRange) / (AudibleRange - NearRange) * 0.7f;
            if (kind == SfxKind.HitSoft || kind == SfxKind.Whoosh || kind == SfxKind.Slash) gain *= (mine ? 1f : 0.8f);
            return true;
        }
    }
}
