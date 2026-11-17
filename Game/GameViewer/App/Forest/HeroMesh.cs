//***************************************************************************
// HeroMesh.cs : HeroModel 의 도형 목록을 삼각형 한 덩어리로 굽는다(GL 과 무관, 테스트 가능).
//   정점 하나는 float 14개: 위치(3) 법선(3) 색(3) 그늘색(3) 반짝임(1) 스스로 빛남(1).
//   위치는 월드 좌표다(발 위치/방향/크기를 굽는 쪽에서 적용). 그래서 셰이더는 카메라 행렬만 곱하면 된다.
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.App.Forest
{
    public sealed class HeroMeshData
    {
        public const int Stride = 14;
        public float[] V = new float[0];
        public int VertexCount;
        public uint[] I = new uint[0];
        public int IndexCount;
    }

    public static class HeroMesh
    {
        private const int Lon = 12, Lat = 8;                       // 타원체 분할
        private const int ConeSeg = 14;

        // 단위 구(위도 Lat x 경도 Lon) 한 번만 계산
        private static readonly float[] SphereU = BuildSphere();
        private static float[] BuildSphere()
        {
            float[] u = new float[(Lat + 1) * (Lon + 1) * 3];
            int n = 0;
            for (int i = 0; i <= Lat; ++i)
            {
                double th = Math.PI * i / Lat; float y = (float)Math.Cos(th), r = (float)Math.Sin(th);
                for (int j = 0; j <= Lon; ++j)
                {
                    double ph = 2 * Math.PI * j / Lon;
                    u[n++] = r * (float)Math.Cos(ph); u[n++] = y; u[n++] = r * (float)Math.Sin(ph);
                }
            }
            return u;
        }

        private sealed class W
        {
            public float[] V; public int Nv; public uint[] I; public int Ni;
            public V3 Origin; public float Cos, Sin, Scale;
            public W(int vcap, int icap) { V = new float[vcap * HeroMeshData.Stride]; I = new uint[icap]; }
            void Grow(int needV, int needI)
            {
                if ((Nv + needV) * HeroMeshData.Stride > V.Length) Array.Resize(ref V, Math.Max(V.Length * 2, (Nv + needV) * HeroMeshData.Stride));
                if (Ni + needI > I.Length) Array.Resize(ref I, Math.Max(I.Length * 2, Ni + needI));
            }
            /// <summary>모델 좌표 p, 법선 n(이미 모델 좌표계의 단위 벡터)을 월드로 보내 정점 하나를 넣는다.</summary>
            public int Vert(V3 p, V3 n, ref HeroPrim pr)
            {
                // 모델 -> 월드: y 축 둘레로 yaw, 균일 확대, 이동
                float x = (Cos * p.X + Sin * p.Z) * Scale, z = (-Sin * p.X + Cos * p.Z) * Scale, y = p.Y * Scale;
                float nx = Cos * n.X + Sin * n.Z, nz = -Sin * n.X + Cos * n.Z;
                int o = Nv * HeroMeshData.Stride;
                V[o] = Origin.X + x; V[o + 1] = Origin.Y + y; V[o + 2] = Origin.Z + z;
                V[o + 3] = nx; V[o + 4] = n.Y; V[o + 5] = nz;
                V[o + 6] = pr.Col.X; V[o + 7] = pr.Col.Y; V[o + 8] = pr.Col.Z;
                V[o + 9] = pr.Sh.X; V[o + 10] = pr.Sh.Y; V[o + 11] = pr.Sh.Z;
                V[o + 12] = pr.Spec; V[o + 13] = pr.Emis ? 1f : 0f;
                return Nv++;
            }
            public void Tri(int a, int b, int c) { I[Ni++] = (uint)a; I[Ni++] = (uint)b; I[Ni++] = (uint)c; }
            public void Reserve(int v, int i) { Grow(v, i); }
        }

        static V3 Mul(Mat3 R, V3 v)
        {
            float[] m = R.M;
            return new V3(m[0] * v.X + m[1] * v.Y + m[2] * v.Z, m[3] * v.X + m[4] * v.Y + m[5] * v.Z, m[6] * v.X + m[7] * v.Y + m[8] * v.Z);
        }

        /// <summary>
        /// prims 를 굽는다. origin = 발 밑 중심의 월드 위치, yaw = 모델 +z 가 월드에서 향할 방향각(라디안; +z 를 (sin yaw, 0, cos yaw) 로 보낸다),
        /// scale = 모델 한 단위가 월드 몇 m 인지. 크기가 0 인 도형(숨긴 화살 등)은 건너뛴다.
        /// </summary>
        public static HeroMeshData Bake(List<HeroPrim> prims, V3 origin, float yaw, float scale, HeroMeshData reuse)
        {
            W w = new W(Math.Max(1024, prims.Count * 60), Math.Max(2048, prims.Count * 150));
            w.Origin = origin; w.Cos = (float)Math.Cos(yaw); w.Sin = (float)Math.Sin(yaw); w.Scale = scale;
            for (int k = 0; k < prims.Count; ++k)
            {
                HeroPrim p = prims[k];
                if (p.A.X == 0f && p.A.Y == 0f && p.A.Z == 0f) continue;
                switch (p.Kind)
                {
                    case PrimKind.Ellipsoid: Ellipsoid(w, ref p); break;
                    case PrimKind.Box: Box(w, ref p); break;
                    default: Cone(w, ref p); break;
                }
            }
            HeroMeshData m = reuse ?? new HeroMeshData();
            m.V = w.V; m.VertexCount = w.Nv; m.I = w.I; m.IndexCount = w.Ni;
            return m;
        }

        static void Ellipsoid(W w, ref HeroPrim p)
        {
            w.Reserve((Lat + 1) * (Lon + 1), Lat * Lon * 6);
            int b = w.Nv;
            for (int q = 0; q < SphereU.Length; q += 3)
            {
                V3 u = new V3(SphereU[q], SphereU[q + 1], SphereU[q + 2]);
                V3 local = new V3(u.X * p.A.X, u.Y * p.A.Y, u.Z * p.A.Z);
                V3 nl = new V3(u.X / Math.Max(p.A.X, 1e-5f), u.Y / Math.Max(p.A.Y, 1e-5f), u.Z / Math.Max(p.A.Z, 1e-5f));
                V3 pos = p.C + Mul(p.R, local), nrm = Mul(p.R, nl).Normalized();
                w.Vert(pos, nrm, ref p);
            }
            for (int i = 0; i < Lat; ++i)
                for (int j = 0; j < Lon; ++j)
                {
                    int a = b + i * (Lon + 1) + j, c = a + Lon + 1;
                    w.Tri(a, a + 1, c); w.Tri(a + 1, c + 1, c);
                }
        }

        static readonly V3[] BoxN = { new V3(1, 0, 0), new V3(-1, 0, 0), new V3(0, 1, 0), new V3(0, -1, 0), new V3(0, 0, 1), new V3(0, 0, -1) };

        static void Box(W w, ref HeroPrim p)
        {
            w.Reserve(24, 36);
            for (int f = 0; f < 6; ++f)
            {
                V3 n = BoxN[f];
                V3 t1 = Math.Abs(n.Y) > 0.5f ? new V3(1, 0, 0) : new V3(0, 1, 0);
                V3 t2 = V3.Cross(n, t1); t1 = V3.Cross(t2, n);
                V3 c = new V3(n.X * p.A.X, n.Y * p.A.Y, n.Z * p.A.Z);
                V3 e1 = new V3(t1.X * p.A.X, t1.Y * p.A.Y, t1.Z * p.A.Z), e2 = new V3(t2.X * p.A.X, t2.Y * p.A.Y, t2.Z * p.A.Z);
                V3 nw = Mul(p.R, n);
                int a = w.Vert(p.C + Mul(p.R, c - e1 - e2), nw, ref p), b = w.Vert(p.C + Mul(p.R, c + e1 - e2), nw, ref p),
                    d = w.Vert(p.C + Mul(p.R, c + e1 + e2), nw, ref p), e = w.Vert(p.C + Mul(p.R, c - e1 + e2), nw, ref p);
                // 면 바깥을 보는 순서(반시계)로: t1 x t2 = -n 일 수 있어 법선과 맞춰 뒤집는다
                V3 geo = V3.Cross(e1, e2);
                if (V3.Dot(geo, c) >= 0f) { w.Tri(a, b, d); w.Tri(a, d, e); } else { w.Tri(a, d, b); w.Tri(a, e, d); }
            }
        }

        static void Cone(W w, ref HeroPrim p)
        {
            float r0 = p.A.X, r1 = p.A.Y, H = p.A.Z, k = p.K;
            float slope = (r1 - r0) / Math.Max(H, 1e-5f);
            w.Reserve(2 * (ConeSeg + 1), ConeSeg * 6);
            int b = w.Nv;
            for (int ring = 0; ring < 2; ++ring)
            {
                float r = ring == 0 ? r0 : r1, y = ring == 0 ? 0f : H;
                for (int j = 0; j <= ConeSeg; ++j)
                {
                    double ph = 2 * Math.PI * j / ConeSeg; float cx = (float)Math.Cos(ph), sz = (float)Math.Sin(ph);
                    V3 local = new V3(r * cx, y, k * r * sz);
                    V3 tTheta = new V3(-sz, 0f, k * cx), tY = new V3(slope * cx, 1f, k * slope * sz);       // 둘레 방향 / 높이 방향 접선(r 로 나눈 형태)
                    V3 nl = V3.Cross(tY, tTheta);
                    if (nl.Length < 1e-6f) nl = new V3(cx, 0f, sz);
                    // 바깥(+반지름) 쪽을 향하게
                    if (V3.Dot(nl, new V3(cx, 0f, sz)) < 0f) nl = nl * -1f;
                    w.Vert(p.C + Mul(p.R, local), Mul(p.R, nl.Normalized()), ref p);
                }
            }
            for (int j = 0; j < ConeSeg; ++j)
            {
                int a = b + j, c = b + ConeSeg + 1 + j;
                w.Tri(a, c, a + 1); w.Tri(a + 1, c, c + 1);      // 링을 아래에서 위로 쌓으므로 구와 감는 방향이 반대다
            }
        }
    }
}
