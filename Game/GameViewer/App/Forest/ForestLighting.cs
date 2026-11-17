//***************************************************************************
// ForestLighting.cs : 태양 방향, 안개 농도, 그림자 지도용 행렬, 태양의 화면 위치(빛줄기 기준점).
//***************************************************************************
using System;

namespace GameViewer.App.Forest
{
    public static class ForestLighting
    {
        /// <summary>태양 쪽을 가리키는 단위 벡터. 고도 약 35도.</summary>
        public static readonly V3 SunDir = new V3(0.50f, 0.58f, 0.64f).Normalized();
        public const float FogDensity = 0.020f;
        public const int ShadowSize = 2048;
        public const float ShadowExtent = 36f;            // 그림자 지도가 덮는 절반 폭(m)

        /// <summary>
        /// 카메라 앞쪽 지면을 중심으로 한 그림자 지도용 (광원 시점 x 직교 투영) 행렬.
        /// 중심을 지도 한 칸 단위로 맞춰 움직여서 카메라가 이동해도 그림자 가장자리가 일렁이지 않게 한다.
        /// </summary>
        public static float[] ShadowMatrix(V3 eye, V3 forward)
        {
            V3 flat = new V3(forward.X, 0f, forward.Z); if (flat.Length < 1e-4f) flat = new V3(1, 0, 0); flat = flat.Normalized();
            V3 center = new V3(eye.X + flat.X * 12f, eye.Y - 1.6f, eye.Z + flat.Z * 12f);
            V3 lightEye = SunDir * 150f;
            float[] view = Mat4.LookAt(lightEye, new V3(0, 0, 0), new V3(0, 1, 0));
            float w; V3 c = Mat4.Transform(view, center, out w);
            float tw = 2f * ShadowExtent / ShadowSize;
            view[12] -= (float)Math.Round(c.X / tw) * tw;
            view[13] -= (float)Math.Round(c.Y / tw) * tw;
            float[] proj = Mat4.Ortho(-ShadowExtent, ShadowExtent, -ShadowExtent, ShadowExtent, 10f, 400f);
            return Mat4.Multiply(proj, view);
        }

        /// <summary>한 점(center) 둘레 extent(m) 만큼만 덮는 촘촘한 그림자 지도용 행렬(영웅 선택 화면). 같은 방식으로 한 칸 단위로 맞춘다.</summary>
        public static float[] ShadowMatrixAt(V3 center, float extent)
        {
            V3 lightEye = center + SunDir * 70f;
            float[] view = Mat4.LookAt(lightEye, center, new V3(0, 1, 0));
            float tw = 2f * extent / ShadowSize;
            float w; V3 c = Mat4.Transform(view, center, out w);
            view[12] -= (float)Math.Round(c.X / tw) * tw;
            view[13] -= (float)Math.Round(c.Y / tw) * tw;
            float[] proj = Mat4.Ortho(-extent, extent, -extent, extent, 20f, 130f);
            return Mat4.Multiply(proj, view);
        }

        /// <summary>
        /// 태양이 화면 어디에 있는지(0~1 좌표)와 빛줄기 세기(0~1). 태양이 카메라 뒤쪽이거나 화면에서 멀수록 약하다.
        /// </summary>
        public static void SunOnScreen(float[] viewProj, V3 eye, out float u, out float v, out float strength)
        {
            float w; V3 p = Mat4.Transform(viewProj, eye + SunDir * 500f, out w);
            if (w <= 0.001f) { u = 0.5f; v = 0.5f; strength = 0f; return; }
            u = p.X / w * 0.5f + 0.5f; v = p.Y / w * 0.5f + 0.5f;
            float dx = u - 0.5f, dy = v - 0.5f;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            strength = Math.Max(0f, Math.Min(1f, 1.25f - d * 1.4f));
        }
    }
}
