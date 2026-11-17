//***************************************************************************
// ForestMath.cs : 숲 배경 장면용 작은 수학 도구. 3차원 벡터와 OpenGL 규격(열 우선) 4x4 행렬. 외부 패키지를 쓰지 않는다.
//***************************************************************************
using System;

namespace GameViewer.App.Forest
{
    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) { return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static V3 operator *(V3 a, float s) { return new V3(a.X * s, a.Y * s, a.Z * s); }
        public static float Dot(V3 a, V3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static V3 Cross(V3 a, V3 b) { return new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public V3 Normalized() { float l = Length; return l < 1e-9f ? new V3(0, 1, 0) : new V3(X / l, Y / l, Z / l); }
        public static V3 Lerp(V3 a, V3 b, float t) { return a + (b - a) * t; }
        public override string ToString() { return X.ToString("0.###") + "," + Y.ToString("0.###") + "," + Z.ToString("0.###"); }
    }

    /// <summary>OpenGL 규격 행렬(float[16], 열 우선: m[열*4+행]). 점은 열 벡터로 곱한다. Multiply(a, b) = a * b (b 를 먼저 적용).</summary>
    public static class Mat4
    {
        public static float[] Identity() { float[] m = new float[16]; m[0] = m[5] = m[10] = m[15] = 1f; return m; }

        public static float[] Multiply(float[] a, float[] b)
        {
            float[] r = new float[16];
            for (int c = 0; c < 4; ++c)
                for (int row = 0; row < 4; ++row)
                {
                    float s = 0f;
                    for (int k = 0; k < 4; ++k) s += a[k * 4 + row] * b[c * 4 + k];
                    r[c * 4 + row] = s;
                }
            return r;
        }

        public static float[] Perspective(float fovY, float aspect, float zNear, float zFar)
        {
            float f = 1f / (float)Math.Tan(fovY * 0.5f);
            float[] m = new float[16];
            m[0] = f / aspect; m[5] = f;
            m[10] = (zFar + zNear) / (zNear - zFar); m[11] = -1f;
            m[14] = 2f * zFar * zNear / (zNear - zFar);
            return m;
        }

        public static float[] LookAt(V3 eye, V3 target, V3 up)
        {
            V3 f = (target - eye).Normalized();
            V3 s = V3.Cross(f, up).Normalized();
            V3 u = V3.Cross(s, f);
            float[] m = Identity();
            m[0] = s.X; m[4] = s.Y; m[8] = s.Z;
            m[1] = u.X; m[5] = u.Y; m[9] = u.Z;
            m[2] = -f.X; m[6] = -f.Y; m[10] = -f.Z;
            m[12] = -V3.Dot(s, eye); m[13] = -V3.Dot(u, eye); m[14] = V3.Dot(f, eye);
            return m;
        }

        public static float[] Ortho(float l, float r, float b, float t, float n, float f)
        {
            float[] m = new float[16];
            m[0] = 2f / (r - l); m[5] = 2f / (t - b); m[10] = -2f / (f - n);
            m[12] = -(r + l) / (r - l); m[13] = -(t + b) / (t - b); m[14] = -(f + n) / (f - n); m[15] = 1f;
            return m;
        }

        /// <summary>점을 변환한다. 돌려주는 값은 w 로 나누기 전의 (x, y, z) 이고 w 는 따로 준다.</summary>
        public static V3 Transform(float[] m, V3 p, out float w)
        {
            w = m[3] * p.X + m[7] * p.Y + m[11] * p.Z + m[15];
            return new V3(m[0] * p.X + m[4] * p.Y + m[8] * p.Z + m[12],
                          m[1] * p.X + m[5] * p.Y + m[9] * p.Z + m[13],
                          m[2] * p.X + m[6] * p.Y + m[10] * p.Z + m[14]);
        }
    }

    /// <summary>같은 입력이면 항상 같은 값을 주는 잡음/난수. 숲 배치가 실행마다 같아야 하므로 System.Random 을 쓰지 않는다.</summary>
    public static class FNoise
    {
        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }
        public static float Hash01(int x, int y, int seed) { return (Hash(x, y, seed) & 0xFFFFFF) / 16777216f; }

        public static float Smooth(float t) { return t * t * (3f - 2f * t); }
        public static float SmoothStep(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>0~1 값 잡음. 격자 점마다 해시 값을 부드럽게 이은 것.</summary>
        public static float Value(float x, float y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = Smooth(x - ix), fy = Smooth(y - iy);
            float a = Hash01(ix, iy, seed), b = Hash01(ix + 1, iy, seed), c = Hash01(ix, iy + 1, seed), d = Hash01(ix + 1, iy + 1, seed);
            return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
        }

        /// <summary>여러 주파수를 겹친 잡음(0~1, 평균 0.5 근처).</summary>
        public static float Fbm(float x, float y, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; ++i)
            {
                sum += Value(x, y, seed + i * 101) * amp; norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>가로세로로 period 칸마다 반복되는 값 잡음(이어 붙일 수 있는 무늬용).</summary>
        public static float ValueTile(float x, float y, int seed, int period)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = Smooth(x - ix), fy = Smooth(y - iy);
            int x0 = Wrap(ix, period), x1 = Wrap(ix + 1, period), y0 = Wrap(iy, period), y1 = Wrap(iy + 1, period);
            float a = Hash01(x0, y0, seed), b = Hash01(x1, y0, seed), c = Hash01(x0, y1, seed), d = Hash01(x1, y1, seed);
            float top = a + (b - a) * fx, bot = c + (d - c) * fx;
            return top + (bot - top) * fy;
        }
        private static int Wrap(int v, int p) { int r = v % p; return r < 0 ? r + p : r; }
    }

    /// <summary>작은 난수 생성기(xorshift32). 같은 씨앗이면 같은 수열.</summary>
    public sealed class Rng
    {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; Next(); Next(); }
        public uint Next() { unchecked { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; } }
        public float Float() { return (Next() & 0xFFFFFF) / 16777216f; }
        public float Range(float a, float b) { return a + (b - a) * Float(); }
        public int Int(int n) { return (int)(Next() % (uint)n); }
    }
}
