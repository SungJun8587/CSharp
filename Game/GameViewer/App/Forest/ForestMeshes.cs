//***************************************************************************
// ForestMeshes.cs : 나무, 바위, 땅, 풀 한 뭉치의 모양을 코드로 만든다. 모델 파일이 필요 없다.
// 정점 하나는 float 9개: 위치(3) 법선(3) 텍스처 좌표(2) 보조값(1). 인덱스는 uint.
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.App.Forest
{
    public sealed class MeshData
    {
        public const int Stride = 9;
        public float[] V;
        public uint[] I;
        public int VertexCount { get { return V.Length / Stride; } }
        public int TriangleCount { get { return I.Length / 3; } }
    }

    public sealed class MeshBuilder
    {
        private readonly List<float> _v = new List<float>();
        private readonly List<uint> _i = new List<uint>();
        public int Count { get { return _v.Count / MeshData.Stride; } }

        public int Add(V3 p, V3 n, float u, float v, float aux)
        {
            _v.Add(p.X); _v.Add(p.Y); _v.Add(p.Z); _v.Add(n.X); _v.Add(n.Y); _v.Add(n.Z); _v.Add(u); _v.Add(v); _v.Add(aux);
            return Count - 1;
        }
        public void Tri(int a, int b, int c) { _i.Add((uint)a); _i.Add((uint)b); _i.Add((uint)c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
        public MeshData ToMesh() { MeshData m = new MeshData(); m.V = _v.ToArray(); m.I = _i.ToArray(); return m; }
    }

    public static class ForestMeshes
    {
        // ------------------------------------------------------------------ 나무

        /// <summary>variant 0~2: 갈색 줄기 나무, 3: 흰 줄기 나무. 같은 번호면 항상 같은 모양.</summary>
        public static void BuildTree(int variant, out MeshData trunk, out MeshData leaves)
        {
            bool birch = variant == 3;
            Rng rng = new Rng((uint)(1000 + variant * 977));
            MeshBuilder tb = new MeshBuilder(), lb = new MeshBuilder();

            float H = birch ? rng.Range(12f, 14f) : rng.Range(9.5f, 13.5f);
            float r0 = birch ? rng.Range(0.16f, 0.2f) : rng.Range(0.30f, 0.42f);
            float leanX = rng.Range(-0.9f, 0.9f) * (birch ? 0.7f : 1f), leanZ = rng.Range(-0.9f, 0.9f) * (birch ? 0.7f : 1f);
            float ph = rng.Range(0f, 6.28f);
            const int rings = 14;
            V3[] c = new V3[rings]; float[] r = new float[rings];
            for (int i = 0; i < rings; ++i)
            {
                float t = i / (float)(rings - 1), y = H * t;
                c[i] = new V3(leanX * t * t + 0.12f * (float)Math.Sin(t * 5f + ph), y, leanZ * t * t + 0.12f * (float)Math.Cos(t * 4f + ph));
                r[i] = Math.Max(0.03f, r0 * (1f - 0.85f * (float)Math.Pow(t, 0.75)) * (1f + 0.35f * (float)Math.Exp(-y * 2.2f)));
            }
            AddTube(tb, c, r, birch ? 7 : 8, 0.35f);

            // 가지 + 가지 끝 잎뭉치
            int nb = 6 + rng.Int(5);
            for (int k = 0; k < nb; ++k)
            {
                float t0 = rng.Range(birch ? 0.4f : 0.45f, birch ? 0.85f : 0.9f);
                float fi = t0 * (rings - 1); int i0 = Math.Min(rings - 2, (int)fi); float ff = fi - i0;
                V3 start = V3.Lerp(c[i0], c[i0 + 1], ff); float rs = (r[i0] + (r[i0 + 1] - r[i0]) * ff) * 0.42f;
                float phi = k * 2.399f + rng.Range(-0.4f, 0.4f), elev = rng.Range(0.5f, 1.1f);
                V3 dir = new V3((float)(Math.Cos(phi) * Math.Cos(elev)), (float)Math.Sin(elev), (float)(Math.Sin(phi) * Math.Cos(elev)));
                float len = rng.Range(2.2f, 3.8f) * (1.15f - t0 * 0.5f) * (birch ? 0.8f : 1f);
                const int bn = 5;
                V3[] bc = new V3[bn]; float[] br = new float[bn];
                V3 p = start;
                for (int i = 0; i < bn; ++i)
                {
                    bc[i] = p; br[i] = Math.Max(0.02f, rs * (1f - i / (float)(bn - 1) * 0.9f));
                    p = p + dir * (len / (bn - 1));
                    dir.Y += 0.12f; dir = dir.Normalized();
                }
                AddTube(tb, bc, br, 5, 0.5f);
                AddLeafCluster(lb, bc[bn - 1], rng.Range(2.6f, 3.4f), 4, rng);
                if (rng.Float() < 0.7f) AddLeafCluster(lb, bc[2], rng.Range(1.9f, 2.5f), 3, rng);
            }

            // 가지 사이를 메우는 잎 뭉치(수관)
            V3 top = c[rings - 1];
            AddLeafCluster(lb, top + new V3(0, 0.8f, 0), 3.4f, 4, rng);
            int puffs = 6 + rng.Int(4);
            float spread = birch ? 0.6f : 1f;
            for (int k = 0; k < puffs; ++k)
            {
                float a = rng.Float() * 6.2831853f, rad = rng.Range(0.8f, 2.6f) * spread, hy = H * rng.Range(0.55f, 1.0f) + 0.5f;
                float t = Math.Min(1f, hy / H); int ii = Math.Min(rings - 2, (int)(t * (rings - 1))); V3 axis = V3.Lerp(c[ii], c[ii + 1], t * (rings - 1) - ii);
                AddLeafCluster(lb, new V3(axis.X + (float)Math.Cos(a) * rad, hy, axis.Z + (float)Math.Sin(a) * rad), rng.Range(2.4f, 3.4f), 4, rng);
            }
            trunk = tb.ToMesh(); leaves = lb.ToMesh();
        }

        /// <summary>중심선(centers)과 반지름(radii)을 따라 가늘어지는 관을 만든다. 법선은 바깥쪽.</summary>
        public static void AddTube(MeshBuilder b, V3[] centers, float[] radii, int sides, float vPerMeter)
        {
            int n = centers.Length;
            int[] first = new int[n];
            float vAcc = 0f;
            for (int i = 0; i < n; ++i)
            {
                V3 d = (centers[Math.Min(n - 1, i + 1)] - centers[Math.Max(0, i - 1)]).Normalized();
                V3 reference = Math.Abs(d.Y) < 0.9f ? new V3(0, 1, 0) : new V3(1, 0, 0);
                V3 n1 = V3.Cross(reference, d).Normalized(), n2 = V3.Cross(d, n1);
                if (i > 0) vAcc += (centers[i] - centers[i - 1]).Length * vPerMeter;
                for (int j = 0; j <= sides; ++j)
                {
                    float a = j / (float)sides * 6.2831853f;
                    V3 nn = n1 * (float)Math.Cos(a) + n2 * (float)Math.Sin(a);
                    int idx = b.Add(centers[i] + nn * radii[i], nn, j / (float)sides, vAcc, 0f);
                    if (j == 0) first[i] = idx;
                }
            }
            for (int i = 0; i + 1 < n; ++i)
                for (int j = 0; j < sides; ++j)
                    b.Quad(first[i] + j, first[i] + j + 1, first[i + 1] + j + 1, first[i + 1] + j);
        }

        /// <summary>잎 그림이 입혀진 사각형 count 장을 한 뭉치로 모은다. 법선은 뭉치 중심에서 바깥쪽이라 둥글게 음영이 진다.</summary>
        public static void AddLeafCluster(MeshBuilder b, V3 center, float size, int count, Rng rng)
        {
            for (int k = 0; k < count; ++k)
            {
                V3 off = new V3(rng.Range(-1f, 1f), rng.Range(-0.7f, 0.7f), rng.Range(-1f, 1f)) * (size * 0.22f);
                V3 cc = center + off;
                V3 nrm = new V3(rng.Range(-1f, 1f), rng.Range(-0.6f, 1f), rng.Range(-1f, 1f)).Normalized();
                V3 a1 = V3.Cross(nrm, new V3(0, 1, 0)); if (a1.Length < 0.1f) a1 = new V3(1, 0, 0);
                a1 = a1.Normalized(); V3 a2 = V3.Cross(nrm, a1).Normalized();
                float mat = rng.Float();
                float hw = size * 0.5f, hh = size * 0.45f;
                V3[] cor = { cc - a1 * hw - a2 * hh, cc + a1 * hw - a2 * hh, cc + a1 * hw + a2 * hh, cc - a1 * hw + a2 * hh };
                float[] us = { 0f, 1f, 1f, 0f }, vs = { 0f, 0f, 1f, 1f };
                int[] id = new int[4];
                for (int i = 0; i < 4; ++i)
                {
                    V3 sn = ((cor[i] - center) * (1f / size) * 2f + new V3(0, 0.45f, 0)).Normalized();
                    id[i] = b.Add(cor[i], sn, us[i], vs[i], mat);
                }
                b.Quad(id[0], id[1], id[2], id[3]);
            }
        }

        // ------------------------------------------------------------------ 바위

        /// <summary>각진 바위. 면마다 법선이 달라 면이 도드라진다. 바닥은 y=0 근처.</summary>
        public static MeshData BuildRock(int variant)
        {
            const int seg = 10, ring = 7;
            V3[,] g = new V3[ring + 1, seg + 1];
            float minY = float.MaxValue;
            for (int i = 0; i <= ring; ++i)
                for (int j = 0; j <= seg; ++j)
                {
                    float pol = i / (float)ring * (float)Math.PI, az = (j % seg) / (float)seg * 6.2831853f;
                    V3 d = new V3((float)(Math.Sin(pol) * Math.Cos(az)), (float)Math.Cos(pol), (float)(Math.Sin(pol) * Math.Sin(az)));
                    float rr = 0.55f * (0.75f + 0.5f * FNoise.Fbm(d.X * 1.7f + variant * 5f, d.Z * 1.7f + d.Y * 1.3f, 91 + variant, 3));
                    g[i, j] = new V3(d.X * rr, d.Y * rr * 0.65f, d.Z * rr);
                    if (g[i, j].Y < minY) minY = g[i, j].Y;
                }
            MeshBuilder b = new MeshBuilder();
            for (int i = 0; i < ring; ++i)
                for (int j = 0; j < seg; ++j)
                {
                    V3 p00 = g[i, j] + new V3(0, -minY - 0.05f, 0), p01 = g[i, j + 1] + new V3(0, -minY - 0.05f, 0);
                    V3 p10 = g[i + 1, j] + new V3(0, -minY - 0.05f, 0), p11 = g[i + 1, j + 1] + new V3(0, -minY - 0.05f, 0);
                    AddFlat(b, p00, p10, p11); AddFlat(b, p00, p11, p01);
                }
            return b.ToMesh();
        }

        private static void AddFlat(MeshBuilder b, V3 a, V3 c, V3 d)
        {
            V3 n = V3.Cross(c - a, d - a);
            if (n.Length < 1e-9f) return;
            n = n.Normalized();
            int i0 = b.Add(a, n, 0f, 0f, 0f), i1 = b.Add(c, n, 1f, 0f, 0f), i2 = b.Add(d, n, 1f, 1f, 0f);
            b.Tri(i0, i1, i2);
        }

        // ------------------------------------------------------------------ 땅

        /// <summary>[-TerrainHalf, TerrainHalf] 를 덮는 격자 땅. 길 둘레는 평평하다.</summary>
        public static MeshData BuildTerrain(ForestWorld w, int cells)
        {
            int n = cells + 1; float half = ForestWorld.TerrainHalf, step = 2f * half / cells;
            float[] h = new float[n * n];
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) h[j * n + i] = w.Height(-half + i * step, -half + j * step);
            MeshBuilder b = new MeshBuilder();
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i)
                {
                    float x = -half + i * step, z = -half + j * step;
                    float hl = h[j * n + Math.Max(0, i - 1)], hr = h[j * n + Math.Min(n - 1, i + 1)];
                    float hd = h[Math.Max(0, j - 1) * n + i], hu = h[Math.Min(n - 1, j + 1) * n + i];
                    V3 nn = new V3((hl - hr) / (2f * step), 1f, (hd - hu) / (2f * step)).Normalized();
                    b.Add(new V3(x, h[j * n + i], z), nn, x / (2f * ForestWorld.GroundMapHalf) + 0.5f, z / (2f * ForestWorld.GroundMapHalf) + 0.5f, 0f);
                }
            for (int j = 0; j < cells; ++j) for (int i = 0; i < cells; ++i) b.Quad(j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i);
            return b.ToMesh();
        }

        // ------------------------------------------------------------------ 풀

        /// <summary>풀 한 포기: 서로 60도씩 어긋난 세 장의 판. 폭 1, 높이 1, 바닥 중앙이 원점.</summary>
        public static MeshData BuildGrassTuft()
        {
            MeshBuilder b = new MeshBuilder();
            for (int k = 0; k < 3; ++k)
            {
                float a = k * (float)Math.PI / 3f, cx = (float)Math.Cos(a) * 0.5f, cz = (float)Math.Sin(a) * 0.5f;
                V3 up = new V3(0, 1, 0);
                int i0 = b.Add(new V3(-cx, 0, -cz), up, 0f, 1f, 0f), i1 = b.Add(new V3(cx, 0, cz), up, 1f, 1f, 0f);
                int i2 = b.Add(new V3(cx, 1, cz), up, 1f, 0f, 0f), i3 = b.Add(new V3(-cx, 1, -cz), up, 0f, 0f, 0f);
                b.Quad(i0, i1, i2, i3);
            }
            return b.ToMesh();
        }
    }
}
