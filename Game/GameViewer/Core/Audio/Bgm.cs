//***************************************************************************
// Bgm.cs : 존별 배경음악 합성. 외부 음원 없이 코드로 만든 반복(루프) 곡이라 WinForms/Unity 어디서든 쓴다.
//   BgmSynth.Make(kind, battle, rate) -> 모노 float 샘플(-1..1). 끝과 처음이 이어져(순환) 반복 재생해도 끊김이 없다.
// 곡 구성: 낮게 깔리는 드론 + 코드 패드 + 드문 멜로디 + 존 질감(바람/파도/지저귐/기포/종/반짝임). 전투 곡은 같은 조(調)에서
// 박자(킥/햇)와 베이스·아르페지오가 붙는다. 같은 입력이면 항상 같은 샘플이 나온다.
//***************************************************************************
using System;
using GameViewer.Core.Logic;

namespace GameViewer.Core.Audio
{
    public static class BgmSynth
    {
        public const float LoopSec = 16f;

        private enum Tex { Wind, Rumble, Waves, Chirps, Bubbles, Bells, Shimmer }

        private sealed class Profile
        {
            public float Root;              // 으뜸음(Hz)
            public int[] Scale;             // 반음 간격(한 옥타브 안)
            public int[] Prog;              // 4마디 코드 진행(음계 칸 번호)
            public Tex Texture;
            public float Drone, Pad, Melody, Tex;      // 층별 크기
            public float Bright;            // 패드 배음 양(0..1)
            public int Notes;               // 평온한 곡의 멜로디 음 개수(루프당)
        }

        private static Profile ProfileOf(MapKind k)
        {
            int[] major = { 0, 2, 4, 7, 9 }, minor = { 0, 3, 5, 7, 10 }, dorian = { 0, 2, 3, 7, 9 }, lydian = { 0, 2, 6, 7, 9 }, phryg = { 0, 1, 5, 7, 8 }, whole = { 0, 2, 4, 6, 8 };
            switch (k)
            {
                case MapKind.Desert:  return new Profile { Root = 146.83f, Scale = phryg,  Prog = new[] { 0, 1, 0, 3 }, Texture = Tex.Wind,    Drone = 0.30f, Pad = 0.22f, Melody = 0.26f, Tex = 0.16f, Bright = 0.35f, Notes = 7 };
                case MapKind.Snow:    return new Profile { Root = 261.63f, Scale = lydian, Prog = new[] { 0, 2, 3, 2 }, Texture = Tex.Wind,    Drone = 0.20f, Pad = 0.24f, Melody = 0.30f, Tex = 0.20f, Bright = 0.7f,  Notes = 6 };
                case MapKind.Volcano: return new Profile { Root = 73.42f,  Scale = minor,  Prog = new[] { 0, 0, 3, 1 }, Texture = Tex.Rumble,  Drone = 0.40f, Pad = 0.20f, Melody = 0.18f, Tex = 0.26f, Bright = 0.5f,  Notes = 5 };
                case MapKind.Swamp:   return new Profile { Root = 110.0f,  Scale = dorian, Prog = new[] { 0, 3, 1, 3 }, Texture = Tex.Bubbles, Drone = 0.34f, Pad = 0.20f, Melody = 0.16f, Tex = 0.24f, Bright = 0.3f,  Notes = 5 };
                case MapKind.Ruins:   return new Profile { Root = 164.81f, Scale = minor,  Prog = new[] { 0, 2, 1, 2 }, Texture = Tex.Bells,   Drone = 0.30f, Pad = 0.22f, Melody = 0.24f, Tex = 0.22f, Bright = 0.45f, Notes = 6 };
                case MapKind.Coast:   return new Profile { Root = 196.0f,  Scale = major,  Prog = new[] { 0, 3, 1, 4 }, Texture = Tex.Waves,   Drone = 0.22f, Pad = 0.24f, Melody = 0.30f, Tex = 0.30f, Bright = 0.55f, Notes = 8 };
                case MapKind.Crystal: return new Profile { Root = 220.0f,  Scale = whole,  Prog = new[] { 0, 2, 4, 2 }, Texture = Tex.Shimmer, Drone = 0.22f, Pad = 0.22f, Melody = 0.22f, Tex = 0.26f, Bright = 0.85f, Notes = 6 };
                case MapKind.Sky:     return new Profile { Root = 293.66f, Scale = major,  Prog = new[] { 0, 3, 4, 3 }, Texture = Tex.Wind,    Drone = 0.16f, Pad = 0.28f, Melody = 0.32f, Tex = 0.14f, Bright = 0.8f,  Notes = 9 };
                case MapKind.Dark:    return new Profile { Root = 65.41f,  Scale = phryg,  Prog = new[] { 0, 1, 0, 4 }, Texture = Tex.Rumble,  Drone = 0.42f, Pad = 0.22f, Melody = 0.14f, Tex = 0.22f, Bright = 0.4f,  Notes = 4 };
                default:              return new Profile { Root = 196.0f,  Scale = major,  Prog = new[] { 0, 3, 1, 4 }, Texture = Tex.Chirps,  Drone = 0.24f, Pad = 0.22f, Melody = 0.28f, Tex = 0.22f, Bright = 0.5f,  Notes = 7 };
            }
        }

        /// <summary>음계 칸 번호(0.. 위로 계속 이어짐)의 주파수. 5음 음계라 칸 5개가 한 옥타브다.</summary>
        private static float Degree(Profile p, int d)
        {
            int n = p.Scale.Length, oct = d >= 0 ? d / n : -((-d + n - 1) / n);
            int idx = d - oct * n;
            return p.Root * (float)Math.Pow(2.0, (p.Scale[idx] + 12 * oct) / 12.0);
        }

        public static float[] Make(MapKind kind, bool battle, int rate)
        {
            if (rate < 8000 || rate > 48000) throw new ArgumentOutOfRangeException("rate");
            Profile p = ProfileOf(kind);
            int n = (int)(rate * LoopSec);
            float[] s = new float[n];
            uint seed = 9001u + (uint)kind * 7919u + (battle ? 31337u : 0u);
            float beat = battle ? 0.5f : 1f;                 // 초(戰 120bpm / 평온 60bpm)
            float barSec = beat * 4f;
            int bars = (int)Math.Round(LoopSec / barSec);

            Drone(s, rate, p, battle ? 0.7f : 1f);
            Pad(s, rate, p, barSec, bars, battle);
            Texture(s, rate, p, seed, battle);
            Melody(s, rate, p, seed, battle, beat, bars);
            if (battle) Drums(s, rate, p, seed, beat, bars);

            // 마무리: 평균 크기를 맞추고(전투 곡이 더 크다) 부드럽게 눌러 담는다. 점별 함수라 루프 이음매는 그대로 유지된다
            double sum = 0; for (int i = 0; i < n; ++i) sum += s[i] * s[i];
            double rms = Math.Sqrt(sum / n);
            float k2 = rms > 1e-9 ? (float)((battle ? 0.20 : 0.15) / rms) : 1f;
            for (int i = 0; i < n; ++i) s[i] = (float)Math.Tanh(s[i] * k2) * 0.85f;
            return s;
        }

        private static uint Next(ref uint st) { st = st * 1664525u + 1013904223u; return st >> 8; }
        private static float Rand01(ref uint st) { return (Next(ref st) & 0xFFFF) / 65536f; }

        /// <summary>루프 길이에 정수 번 도는 주파수로 맞춘다(끝과 처음에서 위상이 이어진다).</summary>
        private static double Snap(double f) { return Math.Max(1.0, Math.Round(f * LoopSec)) / LoopSec; }

        private static void Drone(float[] s, int rate, Profile p, float mix)
        {
            int n = s.Length;
            double f1 = Snap(p.Root * 0.5), f2 = Snap(p.Root * 0.5 * 1.5);
            double lfo = 2 * Math.PI * 2.0 / LoopSec;         // 루프당 두 번 숨쉬듯 커졌다 줄어든다
            for (int i = 0; i < n; ++i)
            {
                double t = i / (double)rate;
                double breathe = 0.65 + 0.35 * Math.Sin(lfo * t);
                s[i] += (float)((Math.Sin(2 * Math.PI * f1 * t) + 0.55 * Math.Sin(2 * Math.PI * f2 * t)) * p.Drone * 0.5 * breathe * mix);
            }
        }

        private static void Pad(float[] s, int rate, Profile p, float barSec, int bars, bool battle)
        {
            int n = s.Length;
            float amp = p.Pad * (battle ? 0.65f : 1f);
            for (int b = 0; b < bars; ++b)
            {
                int root = p.Prog[b % p.Prog.Length];
                float centre = (b + 0.5f) * barSec, half = barSec * 0.75f;      // 마디 길이의 1.5 배로 겹쳐 이어진다
                int[] chord = { root, root + 2, root + 4 };
                for (int c = 0; c < chord.Length; ++c)
                {
                    double f = Degree(p, chord[c]) * 0.5, f2 = f * 1.004;        // 살짝 어긋난 두 사인: 코러스
                    int i0 = (int)((centre - half) * rate), i1 = (int)((centre + half) * rate);
                    for (int i = i0; i < i1; ++i)
                    {
                        double t = i / (double)rate, w = (t - (centre - half)) / (2 * half);
                        float env = (float)Math.Sin(Math.PI * w); env *= env;
                        int idx = ((i % n) + n) % n;
                        double ph = 2 * Math.PI * t;
                        double v = Math.Sin(ph * f) + Math.Sin(ph * f2) + p.Bright * 0.35 * Math.Sin(ph * f * 2.0) + p.Bright * 0.15 * Math.Sin(ph * f * 3.0);
                        s[idx] += (float)(v * env * amp * 0.22);
                    }
                }
            }
        }

        // 순환 저역통과 잡음(처음과 끝이 이어진다): 원본을 세 바퀴 돌려 앞 두 바퀴로 필터 상태를 수렴시키고 마지막 바퀴만 쓴다.
        private static float[] LoopNoise(int n, uint seed, float alpha)
        {
            float[] x = new float[n];
            uint st = seed;
            for (int i = 0; i < n; ++i) x[i] = Rand01(ref st) * 2f - 1f;
            x = CyclicLowPass(x, alpha);
            x = CyclicLowPass(x, alpha);
            float peak = 0f; for (int i = 0; i < n; ++i) peak = Math.Max(peak, Math.Abs(x[i]));
            if (peak > 0f) for (int i = 0; i < n; ++i) x[i] /= peak;
            return x;
        }

        private static float[] CyclicLowPass(float[] raw, float alpha)
        {
            int n = raw.Length;
            float[] o = new float[n];
            float y = 0f;
            for (int pass = 0; pass < 3; ++pass)
                for (int i = 0; i < n; ++i) { y += alpha * (raw[i] - y); if (pass == 2) o[i] = y; }
            return o;
        }

        private static void Texture(float[] s, int rate, Profile p, uint seed, bool battle)
        {
            int n = s.Length;
            float amp = p.Tex * (battle ? 0.6f : 1f);
            double two = 2 * Math.PI / LoopSec;
            switch (p.Texture)
            {
                case Tex.Wind:
                case Tex.Waves:
                case Tex.Rumble:
                    {
                        float alpha = p.Texture == Tex.Rumble ? 0.006f : (p.Texture == Tex.Waves ? 0.035f : 0.02f);
                        float[] nz = LoopNoise(n, seed ^ 0xA5A5u, alpha);
                        double swellHz = p.Texture == Tex.Waves ? 2.0 : 3.0;      // 파도: 루프당 2 번 밀려옴
                        for (int i = 0; i < n; ++i)
                        {
                            double t = i / (double)rate;
                            double sw = 0.55 + 0.45 * Math.Sin(two * swellHz * t + 1.1);
                            s[i] += (float)(nz[i] * amp * sw * 1.4);
                        }
                        break;
                    }
                case Tex.Chirps:
                case Tex.Bubbles:
                case Tex.Bells:
                case Tex.Shimmer:
                    {
                        uint st = seed ^ 0x5151u;
                        int count = p.Texture == Tex.Shimmer ? 22 : (p.Texture == Tex.Bells ? 4 : 9);
                        for (int k = 0; k < count; ++k)
                        {
                            float at = (k + Rand01(ref st) * 0.8f) * LoopSec / count;
                            int start = (int)(at * rate);
                            float f0 = p.Texture == Tex.Chirps ? 2200f + Rand01(ref st) * 1800f : (p.Texture == Tex.Bubbles ? 180f + Rand01(ref st) * 160f : (p.Texture == Tex.Bells ? Degree(p, 5 + (int)(Rand01(ref st) * 5f)) : Degree(p, 10 + (int)(Rand01(ref st) * 8f))));
                            float len = p.Texture == Tex.Bells ? 2.6f : (p.Texture == Tex.Shimmer ? 1.1f : 0.16f);
                            float gain = amp * (0.6f + 0.4f * Rand01(ref st));
                            int m = (int)(len * rate);
                            for (int i = 0; i < m; ++i)
                            {
                                float u = i / (float)m;
                                double f = p.Texture == Tex.Chirps ? f0 * (1.0 + 0.5 * u) : (p.Texture == Tex.Bubbles ? f0 * (1.0 + 1.8 * u) : f0);
                                float env = (float)Math.Exp(-u * (p.Texture == Tex.Bells ? 4.5 : 6.0)) * Math.Min(1f, i / 40f);
                                double ph = 2 * Math.PI * f * (i / (double)rate);
                                float v = (float)(Math.Sin(ph) + (p.Texture == Tex.Bells ? 0.5 * Math.Sin(ph * 2.76) : 0.0));
                                s[(start + i) % n] += v * env * gain * 0.5f;
                            }
                        }
                        break;
                    }
            }
        }

        private static void Pluck(float[] s, int rate, int start, float f, float len, float gain, float bright)
        {
            int n = s.Length, m = (int)(len * rate);
            for (int i = 0; i < m; ++i)
            {
                float u = i / (float)m;
                float env = (float)Math.Exp(-u * 5.0) * Math.Min(1f, i / 30f);
                double ph = 2 * Math.PI * f * (i / (double)rate);
                float v = (float)(Math.Sin(ph) + bright * 0.45 * Math.Sin(ph * 2.0) + bright * 0.2 * Math.Sin(ph * 3.0));
                s[(start + i) % n] += v * env * gain;
            }
        }

        private static void Melody(float[] s, int rate, Profile p, uint seed, bool battle, float beat, int bars)
        {
            uint st = seed ^ 0xC0FFEEu;
            if (!battle)
            {
                // 드문 음: 4 박 단위 자리에서 코드 음 위주로 고른다
                int slots = (int)(LoopSec / beat);
                int placed = 0, last = 5;
                for (int slot = 0; slot < slots && placed < p.Notes; ++slot)
                {
                    if (Rand01(ref st) > (float)(p.Notes - placed) / (slots - slot) * 1.05f) continue;
                    int bar = (int)(slot * beat / (4f * beat)) % p.Prog.Length;
                    int chordRoot = p.Prog[bar];
                    int pick = chordRoot + 5 + (int)(Rand01(ref st) * 5f) - 1;
                    if (Math.Abs(pick - last) > 4) pick = last + (pick > last ? 3 : -3);
                    last = pick;
                    Pluck(s, rate, (int)(slot * beat * rate), Degree(p, pick), 2.2f, p.Melody * 0.45f, p.Bright);
                    ++placed;
                }
            }
            else
            {
                // 전투: 8 분음표 아르페지오(마디마다 코드 음을 오르내린다)
                float step = beat / 2f;
                int steps = (int)(LoopSec / step);
                for (int k = 0; k < steps; ++k)
                {
                    int bar = (int)(k * step / (4f * beat)) % p.Prog.Length;
                    int[] pat = { 0, 2, 4, 2, 0, 4, 2, 5 };
                    if (Rand01(ref st) < 0.18f) continue;
                    int deg = p.Prog[bar] + 5 + pat[k % pat.Length];
                    Pluck(s, rate, (int)(k * step * rate), Degree(p, deg), 0.5f, p.Melody * 0.5f, p.Bright);
                }
                // 베이스: 4 분음표마다 으뜸/5 도
                for (int k = 0; k < (int)(LoopSec / beat); ++k)
                {
                    int bar = (int)(k * beat / (4f * beat)) % p.Prog.Length;
                    int deg = p.Prog[bar] + ((k % 4) == 2 ? 2 : 0);
                    Pluck(s, rate, (int)(k * beat * rate), Degree(p, deg) * 0.5f, 0.42f, p.Melody * 0.9f, 0.2f);
                }
            }
        }

        private static void Drums(float[] s, int rate, Profile p, uint seed, float beat, int bars)
        {
            int n = s.Length;
            uint st = seed ^ 0xD00Du;
            int beats = (int)(LoopSec / beat);
            for (int k = 0; k < beats; ++k)
            {
                // 킥: 박마다. 첫 박은 더 세게
                int start = (int)(k * beat * rate);
                float g = (k % 4 == 0) ? 0.55f : 0.38f;
                int m = (int)(0.2f * rate);
                double ph = 0;
                for (int i = 0; i < m; ++i)
                {
                    float u = i / (float)m;
                    double f = 48.0 + 70.0 * Math.Pow(1.0 - u, 3.0);
                    ph += 2 * Math.PI * f / rate;
                    s[(start + i) % n] += (float)(Math.Sin(ph) * Math.Pow(1.0 - u, 2.0) * g * Math.Min(1f, i / 20f));
                }
                // 햇: 8 분음표 뒷박
                int h0 = start + (int)(beat * 0.5f * rate);
                int hm = (int)(0.05f * rate);
                float prev = 0f;
                for (int i = 0; i < hm; ++i)
                {
                    float x = Rand01(ref st) * 2f - 1f;
                    float hp = x - prev; prev = x;                   // 고역 강조(1 차 차분)
                    float env = (float)Math.Pow(1.0 - i / (float)hm, 2.0);
                    s[(h0 + i) % n] += hp * env * 0.12f;
                }
                // 스네어: 2, 4 박
                if (k % 4 == 1 || k % 4 == 3)
                {
                    int sm = (int)(0.11f * rate);
                    for (int i = 0; i < sm; ++i)
                    {
                        float x = Rand01(ref st) * 2f - 1f, env = (float)Math.Pow(1.0 - i / (float)sm, 2.2);
                        s[(start + i) % n] += x * env * 0.16f;
                    }
                }
            }
        }
    }
}
