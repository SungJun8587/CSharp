//***************************************************************************
// ForestWorld.cs : 숲 장면의 "무엇이 어디에 있는가". 걷는 길(닫힌 곡선), 땅 높이, 나무/풀/바위 배치.
// OpenGL 과 무관한 순수 계산이라 콘솔 테스트로 확인한다. 같은 seed 면 항상 같은 숲이 나온다.
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.App.Forest
{
    /// <summary>인스턴스 하나(나무, 풀, 바위)의 배치값. GPU 에 8개 float 로 그대로 올린다: (x, y, z, 회전) (크기, 색 변화, 보조1, 보조2).</summary>
    public struct Inst
    {
        public float X, Y, Z, Rot, Scale, Tint, A, B;
    }

    public sealed class ForestLayout
    {
        public const int TreeVariants = 4, RockVariants = 2;
        public readonly List<Inst>[] Trees = new List<Inst>[TreeVariants];
        public readonly List<Inst>[] Rocks = new List<Inst>[RockVariants];
        public readonly List<Inst> Grass = new List<Inst>();
        /// <summary>먼지 입자 씨앗(x, y, z, 난수). 카메라 둘레에서 되돌아 감기며 움직인다.</summary>
        public float[] Motes;
        public ForestLayout()
        {
            for (int i = 0; i < Trees.Length; ++i) Trees[i] = new List<Inst>();
            for (int i = 0; i < Rocks.Length; ++i) Rocks[i] = new List<Inst>();
        }
        public int TreeCount { get { int n = 0; foreach (List<Inst> l in Trees) n += l.Count; return n; } }
    }

    public sealed class ForestWorld
    {
        public const float TreeRadius = 78f;             // 나무를 심는 반경
        public const float TerrainHalf = 90f;            // 땅 메시가 덮는 절반 폭
        public const float GroundMapHalf = 100f;         // 땅 무늬 텍스처가 덮는 절반 폭
        public const float PathHalfWidth = 0.9f;         // 흙길 절반 폭(대략)

        public readonly int Seed;
        private readonly float[] _px, _pz;               // 길 꺾은선의 점
        private readonly float[] _cum;                   // 점 i 까지의 누적 길이
        private readonly int _n;
        public readonly float PathLength;

        public ForestWorld(int seed)
        {
            Seed = seed;
            // 닫힌 곡선의 조절점: 반지름이 조금씩 다른 아홉 점을 Catmull-Rom 으로 잇는다.
            const int cp = 9;
            float[] cx = new float[cp], cz = new float[cp];
            for (int i = 0; i < cp; ++i)
            {
                float a = (float)(i * 2.0 * Math.PI / cp) + (FNoise.Hash01(i, 1, seed) - 0.5f) * 0.25f;
                float r = 25f + (FNoise.Hash01(i, 2, seed) * 2f - 1f) * 7f;
                cx[i] = (float)Math.Cos(a) * r; cz[i] = (float)Math.Sin(a) * r;
            }
            const int per = 80;
            _n = cp * per;
            _px = new float[_n]; _pz = new float[_n]; _cum = new float[_n + 1];
            for (int i = 0; i < cp; ++i)
            {
                int i0 = (i + cp - 1) % cp, i1 = i, i2 = (i + 1) % cp, i3 = (i + 2) % cp;
                for (int k = 0; k < per; ++k)
                {
                    float t = k / (float)per, t2 = t * t, t3 = t2 * t;
                    _px[i * per + k] = CatmullRom(cx[i0], cx[i1], cx[i2], cx[i3], t, t2, t3);
                    _pz[i * per + k] = CatmullRom(cz[i0], cz[i1], cz[i2], cz[i3], t, t2, t3);
                }
            }
            for (int i = 0; i < _n; ++i)
            {
                int j = (i + 1) % _n;
                float dx = _px[j] - _px[i], dz = _pz[j] - _pz[i];
                _cum[i + 1] = _cum[i] + (float)Math.Sqrt(dx * dx + dz * dz);
            }
            PathLength = _cum[_n];
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t, float t2, float t3)
        {
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>길 위 s 미터 지점의 (x, z). s 는 한 바퀴 길이로 되돌려 쓴다.</summary>
        public void PathAt(float s, out float x, out float z)
        {
            s %= PathLength; if (s < 0f) s += PathLength;
            int lo = 0, hi = _n;                          // _cum[lo] <= s < _cum[hi]
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_cum[mid] <= s) lo = mid; else hi = mid; }
            int j = (lo + 1) % _n;
            float seg = _cum[lo + 1] - _cum[lo];
            float f = seg > 1e-6f ? (s - _cum[lo]) / seg : 0f;
            x = _px[lo] + (_px[j] - _px[lo]) * f; z = _pz[lo] + (_pz[j] - _pz[lo]) * f;
        }

        /// <summary>(x, z) 에서 길까지의 가장 짧은 거리(미터).</summary>
        public float PathDistance(float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _n; ++i)
            {
                int j = i + 1 == _n ? 0 : i + 1;
                float ax = _px[i], az = _pz[i], bx = _px[j] - ax, bz = _pz[j] - az;
                float l2 = bx * bx + bz * bz;
                float t = l2 > 1e-9f ? ((x - ax) * bx + (z - az) * bz) / l2 : 0f;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                float dx = x - (ax + bx * t), dz = z - (az + bz * t);
                float d2 = dx * dx + dz * dz;
                if (d2 < best) best = d2;
            }
            return (float)Math.Sqrt(best);
        }

        /// <summary>땅 높이. 길 위는 평평하고 멀어질수록 완만하게 굽이친다.</summary>
        public float Height(float x, float z)
        {
            float big = (FNoise.Fbm(x * 0.028f + 17f, z * 0.028f - 9f, Seed + 11, 4) - 0.5f) * 2f;
            float small = (FNoise.Fbm(x * 0.12f, z * 0.12f, Seed + 23, 3) - 0.5f) * 2f;
            float k = FNoise.SmoothStep(0.4f, 5.5f, PathDistance(x, z));
            return (big * 1.8f + small * 0.25f) * k;
        }

        // ------------------------------------------------------------------ 배치

        public ForestLayout Generate()
        {
            ForestLayout L = new ForestLayout();
            Rng rng = new Rng((uint)(Seed * 7919 + 17));

            // 나무: 길에서 2m 이상, 서로 2.3m 이상 떨어지게 무작위로 심는다.
            List<float> tx = new List<float>(), tz = new List<float>();
            for (int tries = 0; tries < 9000 && tx.Count < 650; ++tries)
            {
                float r = TreeRadius * (float)Math.Sqrt(rng.Float()), a = rng.Float() * 6.2831853f;
                float x = (float)Math.Cos(a) * r, z = (float)Math.Sin(a) * r;
                if (PathDistance(x, z) < 2.0f) continue;
                bool ok = true;
                for (int i = 0; i < tx.Count && ok; ++i) { float dx = tx[i] - x, dz = tz[i] - z; if (dx * dx + dz * dz < 2.3f * 2.3f) ok = false; }
                if (!ok) continue;
                tx.Add(x); tz.Add(z);
                Inst t = new Inst();
                t.X = x; t.Z = z; t.Y = Height(x, z) - 0.05f;
                t.Rot = rng.Float() * 6.2831853f; t.Scale = rng.Range(0.85f, 1.35f); t.Tint = rng.Range(0.85f, 1.15f);
                int v = rng.Float() < 0.18f ? 3 : rng.Int(3);          // 3번은 흰 줄기 나무(자작나무 느낌)
                L.Trees[v].Add(t);
            }

            // 바위: 길가에 몇 개
            for (int tries = 0; tries < 400 && L.Rocks[0].Count + L.Rocks[1].Count < 30; ++tries)
            {
                float s = rng.Float() * PathLength, ax, az, bx, bz;
                PathAt(s, out ax, out az); PathAt(s + 0.5f, out bx, out bz);
                float dx = bx - ax, dz = bz - az, dl = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dl < 1e-6f) continue;
                float off = (rng.Float() < 0.5f ? -1f : 1f) * rng.Range(1.7f, 8f);
                float x = ax + (-dz / dl) * off, z = az + (dx / dl) * off;
                bool ok = true;
                for (int i = 0; i < tx.Count && ok; ++i) { float ex = tx[i] - x, ez = tz[i] - z; if (ex * ex + ez * ez < 1.1f * 1.1f) ok = false; }
                if (!ok) continue;
                Inst r = new Inst();
                r.X = x; r.Z = z; r.Y = Height(x, z) - 0.08f;
                r.Rot = rng.Float() * 6.2831853f; r.Scale = rng.Range(0.5f, 1.5f); r.Tint = rng.Range(0.8f, 1.2f);
                L.Rocks[rng.Int(ForestLayout.RockVariants)].Add(r);
            }

            // 풀: 길 가까이는 빽빽하게, 멀리는 성기게. 길 한복판은 비운다.
            for (int tries = 0; tries < 100000 && L.Grass.Count < 60000; ++tries)
            {
                float r = 80f * (float)Math.Sqrt(rng.Float()), a = rng.Float() * 6.2831853f;
                float x = (float)Math.Cos(a) * r, z = (float)Math.Sin(a) * r;
                float pd = PathDistance(x, z);
                float dens = pd < 0.6f ? 0f : pd < 1.5f ? (pd - 0.6f) / 0.9f * 0.7f : pd < 32f ? 1f : 0.2f;
                if (rng.Float() >= dens) continue;
                Inst g = new Inst();
                g.X = x; g.Z = z; g.Y = Height(x, z) - 0.03f;
                g.Rot = rng.Float() * 6.2831853f; g.Scale = rng.Range(0.7f, 1.4f); g.Tint = rng.Range(0.8f, 1.2f); g.A = rng.Float() * 6.2831853f;
                L.Grass.Add(g);
            }

            // 먼지 입자 씨앗
            L.Motes = new float[500 * 4];
            for (int i = 0; i < 500; ++i)
            {
                L.Motes[i * 4] = rng.Range(-14f, 14f); L.Motes[i * 4 + 1] = rng.Range(-3f, 5f); L.Motes[i * 4 + 2] = rng.Range(-14f, 14f); L.Motes[i * 4 + 3] = rng.Float();
            }
            return L;
        }
    }
}
