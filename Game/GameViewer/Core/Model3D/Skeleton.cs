//***************************************************************************
// Skeleton.cs : 뼈대 자세 계산(GL 과 무관). 클립 샘플링, 두 자세 섞기, 관절 행렬(셰이더로 보내는 팔레트) 만들기.
//   Pose 는 노드마다 이동(3)/회전 쿼터니언(4)/확대(3)를 쥔다.
//   팔레트[j] = 관절 j 노드의 전역 행렬 x inverse bind. 정점은 셰이더에서 가중 합한 팔레트로 변환한다.
//   뒤쪽 칸(JointCount ..)은 강체 메시(무기 등)가 붙은 노드의 전역 행렬 그대로(가상 관절).
//***************************************************************************
using System;

namespace GameViewer.Core.Model3D
{
    public sealed class Pose
    {
        public readonly float[] T, R, S;
        internal readonly float[] Global;       // 노드마다 4x4 (계산 중간값)
        public Pose(GltfModel m) { T = new float[m.NodeCount * 3]; R = new float[m.NodeCount * 4]; S = new float[m.NodeCount * 3]; Global = new float[m.NodeCount * 16]; Rest(m); }
        public void Rest(GltfModel m) { Array.Copy(m.RestT, T, T.Length); Array.Copy(m.RestR, R, R.Length); Array.Copy(m.RestS, S, S.Length); }
        public void CopyFrom(Pose o) { Array.Copy(o.T, T, T.Length); Array.Copy(o.R, R, R.Length); Array.Copy(o.S, S, S.Length); }
    }

    public static class Skeleton
    {
        /// <summary>기본 자세에서 시작해 클립의 시각 t(초)의 값으로 덮어쓴다. t 는 [0, Duration] 으로 자른다(반복은 부르는 쪽이 한다).</summary>
        public static void Sample(GltfModel m, AnimClip clip, float t, Pose into)
        {
            into.Rest(m);
            if (clip == null) return;
            AnimChannel[] chs = clip.Channels;
            for (int c = 0; c < chs.Length; ++c)
            {
                AnimChannel ch = chs[c];
                if (ch.Times.Length == 0) continue;
                float[] ts = ch.Times;
                int n = ts.Length, k;
                if (t <= ts[0]) k = 0; else if (t >= ts[n - 1]) k = n - 1;
                else
                {
                    int lo = 0, hi = n - 1;                       // ts[lo] <= t < ts[hi]
                    while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (ts[mid] <= t) lo = mid; else hi = mid; }
                    k = lo;
                }
                int comps = ch.IsRotation ? 4 : 3;
                float f = 0f;
                if (!ch.Step && k < n - 1 && t > ts[k]) f = (t - ts[k]) / Math.Max(ts[k + 1] - ts[k], 1e-9f);
                int a = k * comps, b = Math.Min(k + 1, n - 1) * comps;
                if (ch.IsRotation) Slerp(ch.Values, a, ch.Values, b, f, into.R, ch.Node * 4);
                else
                {
                    float[] dst = ch.IsScale ? into.S : into.T;
                    for (int i = 0; i < 3; ++i) dst[ch.Node * 3 + i] = ch.Values[a + i] + (ch.Values[b + i] - ch.Values[a + i]) * f;
                }
            }
        }

        /// <summary>a 와 b 를 w(0..1) 만큼 섞어 into 에 쓴다(into 는 a 나 b 와 같아도 된다).</summary>
        public static void Blend(Pose a, Pose b, float w, Pose into)
        {
            for (int i = 0; i < a.T.Length; ++i) into.T[i] = a.T[i] + (b.T[i] - a.T[i]) * w;
            for (int i = 0; i < a.S.Length; ++i) into.S[i] = a.S[i] + (b.S[i] - a.S[i]) * w;
            for (int i = 0; i < a.R.Length; i += 4) Slerp(a.R, i, b.R, i, w, into.R, i);
        }

        /// <summary>쿼터니언 보간(최단 경로). 결과는 단위 길이.</summary>
        public static void Slerp(float[] a, int ao, float[] b, int bo, float t, float[] dst, int d)
        {
            float ax = a[ao], ay = a[ao + 1], az = a[ao + 2], aw = a[ao + 3];
            float bx = b[bo], by = b[bo + 1], bz = b[bo + 2], bw = b[bo + 3];
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f) { bx = -bx; by = -by; bz = -bz; bw = -bw; dot = -dot; }
            float s0, s1;
            if (dot > 0.9995f) { s0 = 1f - t; s1 = t; }
            else
            {
                float th = (float)Math.Acos(Math.Min(1f, dot)), sn = (float)Math.Sin(th);
                s0 = (float)Math.Sin((1f - t) * th) / sn; s1 = (float)Math.Sin(t * th) / sn;
            }
            float x = ax * s0 + bx * s1, y = ay * s0 + by * s1, z = az * s0 + bz * s1, w = aw * s0 + bw * s1;
            float l = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (l < 1e-9f) { x = 0; y = 0; z = 0; w = 1; l = 1; }
            dst[d] = x / l; dst[d + 1] = y / l; dst[d + 2] = z / l; dst[d + 3] = w / l;
        }

        /// <summary>자세의 전역 행렬을 계산하고 팔레트(PaletteCount x 16, 열 우선)를 채운다.</summary>
        public static void Palette(GltfModel m, Pose p, float[] palette)
        {
            float[] g = p.Global;
            for (int oi = 0; oi < m.Order.Length; ++oi)
            {
                int i = m.Order[oi], o = i * 16;
                float x = p.R[i * 4], y = p.R[i * 4 + 1], z = p.R[i * 4 + 2], w = p.R[i * 4 + 3];
                float sx = p.S[i * 3], sy = p.S[i * 3 + 1], sz = p.S[i * 3 + 2];
                float xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
                float[] l = Local;
                l[0] = (1 - 2 * (yy + zz)) * sx; l[1] = 2 * (xy + wz) * sx; l[2] = 2 * (xz - wy) * sx; l[3] = 0;
                l[4] = 2 * (xy - wz) * sy; l[5] = (1 - 2 * (xx + zz)) * sy; l[6] = 2 * (yz + wx) * sy; l[7] = 0;
                l[8] = 2 * (xz + wy) * sz; l[9] = 2 * (yz - wx) * sz; l[10] = (1 - 2 * (xx + yy)) * sz; l[11] = 0;
                l[12] = p.T[i * 3]; l[13] = p.T[i * 3 + 1]; l[14] = p.T[i * 3 + 2]; l[15] = 1;
                int par = m.Parent[i];
                if (par < 0) Array.Copy(l, 0, g, o, 16); else Mul(g, par * 16, l, 0, g, o);
            }
            for (int j = 0; j < m.JointNodes.Length; ++j) Mul(g, m.JointNodes[j] * 16, m.InverseBind, j * 16, palette, j * 16);
            for (int k = 0; k < m.RigidNodes.Length; ++k) Array.Copy(g, m.RigidNodes[k] * 16, palette, (m.JointNodes.Length + k) * 16, 16);
        }

        [ThreadStatic] private static float[] _local;
        private static float[] Local { get { return _local ?? (_local = new float[16]); } }

        /// <summary>열 우선 4x4 곱: dst = a * b. dst 는 a/b 와 겹치면 안 된다.</summary>
        public static void Mul(float[] a, int ao, float[] b, int bo, float[] dst, int d)
        {
            for (int c = 0; c < 4; ++c)
                for (int r = 0; r < 4; ++r)
                    dst[d + c * 4 + r] = a[ao + r] * b[bo + c * 4] + a[ao + 4 + r] * b[bo + c * 4 + 1] + a[ao + 8 + r] * b[bo + c * 4 + 2] + a[ao + 12 + r] * b[bo + c * 4 + 3];
        }
    }
}
