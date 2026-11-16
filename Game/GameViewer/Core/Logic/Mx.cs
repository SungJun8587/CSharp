namespace GameViewer.Core.Logic
{
    /// <summary>렌더러들이 따로따로 갖고 있던 수학 도우미(S자 보간, 이징, 클램프, 0xRRGGBB 섞기)를 한곳에 모았다.</summary>
    public static class Mx
    {
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        /// <summary>a..b 구간을 0..1 로 펴서 부드럽게(S자) 만든다.</summary>
        public static float Smooth(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            if (t <= 0) return 0; if (t >= 1) return 1;
            return t * t * (3 - 2 * t);
        }

        public static float Ease(float p) { return 1f - (1f - p) * (1f - p); }

        /// <summary>0xRRGGBB 두 색을 t(0..1)로 섞는다.</summary>
        public static int MixRgb(int a, int b, float t)
        {
            t = Clamp01(t);
            int r = (int)(((a >> 16) & 255) * (1f - t) + ((b >> 16) & 255) * t), g = (int)(((a >> 8) & 255) * (1f - t) + ((b >> 8) & 255) * t), bl = (int)((a & 255) * (1f - t) + (b & 255) * t);
            return (r << 16) | (g << 8) | bl;
        }

        /// <summary>누적 중인 RGB(float)를 색(cr,cg,cb) 쪽으로 t 만큼 당긴다.</summary>
        public static void MixInto(ref float r, ref float g, ref float b, int cr, int cg, int cb, float t) { r += (cr - r) * t; g += (cg - g) * t; b += (cb - b) * t; }
    }
}
