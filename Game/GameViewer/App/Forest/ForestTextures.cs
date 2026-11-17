//***************************************************************************
// ForestTextures.cs : 숲 장면에 쓰는 그림(잎 뭉치, 풀 한 포기, 땅 무늬)을 코드로 그린다. 이미지 파일이 필요 없다.
// 모두 RGBA 바이트 배열이다. 투명한 칸의 색도 잎 색으로 채워 두어서 밉맵에서 가장자리가 검게 번지지 않는다.
//***************************************************************************
using System;

namespace GameViewer.App.Forest
{
    public sealed class TexImage
    {
        public int W, H;
        public byte[] Rgba;
        public TexImage(int w, int h) { W = w; H = h; Rgba = new byte[w * h * 4]; }
        public float Coverage()
        {
            int n = 0;
            for (int i = 3; i < Rgba.Length; i += 4) if (Rgba[i] >= 128) ++n;
            return n / (float)(W * H);
        }
    }

    public static class ForestTextures
    {
        private static byte B(float v) { return (byte)(v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f)); }

        private static void Fill(TexImage t, byte r, byte g, byte b)
        {
            for (int i = 0; i < t.Rgba.Length; i += 4) { t.Rgba[i] = r; t.Rgba[i + 1] = g; t.Rgba[i + 2] = b; t.Rgba[i + 3] = 0; }
        }

        /// <summary>잎사귀 백서른 장이 흩어진 뭉치. 가운데에 몰려 있고 가장자리는 투명하다.</summary>
        public static TexImage LeafCluster(int size)
        {
            TexImage t = new TexImage(size, size);
            Fill(t, 52, 78, 30);
            Rng rng = new Rng(555);
            for (int k = 0; k < 130; ++k)
            {
                float rad = 0.40f * (float)Math.Sqrt(rng.Float()), pa = rng.Float() * 6.2831853f;
                float cx = 0.5f + (float)Math.Cos(pa) * rad, cy = 0.5f + (float)Math.Sin(pa) * rad;
                float ang = rng.Float() * 6.2831853f, L = rng.Range(0.05f, 0.08f), Wd = L * rng.Range(0.4f, 0.52f);
                float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
                float shade = rng.Range(0.7f, 1.25f);
                float br = (48f + rng.Float() * 34f) * shade, bg = (78f + rng.Float() * 40f) * shade, bb = (22f + rng.Float() * 22f) * shade;
                float top = 0.85f + 0.3f * (1f - cy);
                int x0 = Math.Max(0, (int)((cx - L) * size)), x1 = Math.Min(size - 1, (int)((cx + L) * size) + 1);
                int y0 = Math.Max(0, (int)((cy - L) * size)), y1 = Math.Min(size - 1, (int)((cy + L) * size) + 1);
                for (int y = y0; y <= y1; ++y)
                    for (int x = x0; x <= x1; ++x)
                    {
                        float dx = (x + 0.5f) / size - cx, dy = (y + 0.5f) / size - cy;
                        float a = dx * ca + dy * sa, c = -dx * sa + dy * ca;
                        float q = 1f - (a / L) * (a / L);
                        if (q <= 0f) continue;
                        float w = Wd * (float)Math.Pow(q, 0.6) * 1.15f;
                        float f = w - Math.Abs(c);                          // 잎 안쪽으로 얼마나 들어왔는지(uv 단위)
                        if (f <= 0f) continue;
                        float alpha = Math.Min(1f, f * size / 1.5f);
                        int o = (y * size + x) * 4;
                        if (alpha * 255f < t.Rgba[o + 3]) continue;
                        float light = 1f + 0.25f * (1f - Math.Abs(c) / Math.Max(1e-5f, w));
                        if (Math.Abs(c) < Wd * 0.06f) light *= 1.18f;      // 잎맥
                        if (Math.Abs(c) > 0.8f * w) light *= 0.85f;        // 가장자리
                        light *= top;
                        t.Rgba[o] = B(br * light); t.Rgba[o + 1] = B(bg * light); t.Rgba[o + 2] = B(bb * light); t.Rgba[o + 3] = B(alpha * 255f);
                    }
            }
            return t;
        }

        /// <summary>풀잎 열 장이 한 점에서 퍼져 나가는 풀 한 포기. 위쪽이 첫 줄.</summary>
        public static TexImage GrassTuft(int w, int h)
        {
            TexImage t = new TexImage(w, h);
            Fill(t, 40, 62, 22);
            Rng rng = new Rng(777);
            for (int k = 0; k < 10; ++k)
            {
                float x0 = 0.5f + rng.Range(-0.13f, 0.13f), lean = rng.Range(-0.38f, 0.38f), height = rng.Range(0.55f, 0.98f), w0 = rng.Range(0.032f, 0.05f);
                float shade = rng.Range(0.8f, 1.2f);
                int steps = (int)(h * 1.4f);
                for (int s = 0; s <= steps; ++s)
                {
                    float tt = s / (float)steps;
                    float cx = x0 + lean * (float)Math.Pow(tt, 1.6), cy = tt * height;
                    float hw = w0 * (float)Math.Pow(1f - tt, 0.75) + 0.004f;
                    int row = (int)((1f - cy) * (h - 1));
                    if (row < 0 || row >= h) continue;
                    int px0 = Math.Max(0, (int)((cx - hw) * w) - 1), px1 = Math.Min(w - 1, (int)((cx + hw) * w) + 1);
                    float r = (22f + 58f * tt) * shade, g = (38f + 72f * tt) * shade, b = (12f + 26f * tt) * shade;
                    for (int x = px0; x <= px1; ++x)
                    {
                        float d = hw - Math.Abs((x + 0.5f) / w - cx);
                        if (d <= 0f) continue;
                        float alpha = Math.Min(1f, d * w / 1.2f);
                        int o = (row * w + x) * 4;
                        if (alpha * 255f < t.Rgba[o + 3]) continue;
                        t.Rgba[o] = B(r); t.Rgba[o + 1] = B(g); t.Rgba[o + 2] = B(b); t.Rgba[o + 3] = B(alpha * 255f);
                    }
                }
            }
            return t;
        }

        /// <summary>땅 무늬 지도(월드 [-100, 100] 전체). R = 길까지의 거리(0~3m 를 0~255 로), G = 이끼 얼룩, B = 낙엽 얼룩.</summary>
        public static TexImage GroundMap(ForestWorld w, int size)
        {
            TexImage t = new TexImage(size, size);
            float[] dist = new float[size * size];
            for (int i = 0; i < dist.Length; ++i) dist[i] = 3f;
            float texel = 2f * ForestWorld.GroundMapHalf / size;
            int rad = (int)Math.Ceiling(3f / texel) + 1;
            for (float s = 0f; s < w.PathLength; s += 0.12f)
            {
                float x, z; w.PathAt(s, out x, out z);
                int cx = (int)((x + ForestWorld.GroundMapHalf) / texel), cz = (int)((z + ForestWorld.GroundMapHalf) / texel);
                for (int j = Math.Max(0, cz - rad); j <= Math.Min(size - 1, cz + rad); ++j)
                    for (int i = Math.Max(0, cx - rad); i <= Math.Min(size - 1, cx + rad); ++i)
                    {
                        float dx = (i + 0.5f) * texel - ForestWorld.GroundMapHalf - x, dz = (j + 0.5f) * texel - ForestWorld.GroundMapHalf - z;
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d < dist[j * size + i]) dist[j * size + i] = d;
                    }
            }
            for (int j = 0; j < size; ++j)
                for (int i = 0; i < size; ++i)
                {
                    float x = (i + 0.5f) * texel - ForestWorld.GroundMapHalf, z = (j + 0.5f) * texel - ForestWorld.GroundMapHalf;
                    int o = (j * size + i) * 4;
                    t.Rgba[o] = B(dist[j * size + i] / 3f * 255f);
                    t.Rgba[o + 1] = B(FNoise.Fbm(x * 0.05f, z * 0.05f, w.Seed + 31, 4) * 255f);
                    t.Rgba[o + 2] = B(FNoise.Fbm(x * 0.2f + 40f, z * 0.2f - 20f, w.Seed + 47, 3) * 255f);
                    t.Rgba[o + 3] = 255;
                }
            return t;
        }

        /// <summary>이어 붙일 수 있는 잡음 무늬(R, G, B 서로 다른 잡음). 땅의 세부 질감에 곱한다.</summary>
        public static TexImage GroundDetail(int size)
        {
            TexImage t = new TexImage(size, size);
            for (int j = 0; j < size; ++j)
                for (int i = 0; i < size; ++i)
                {
                    int o = (j * size + i) * 4;
                    for (int ch = 0; ch < 3; ++ch)
                    {
                        float sum = 0f, amp = 0.5f, norm = 0f; int period = 8;
                        for (int oct = 0; oct < 4; ++oct)
                        {
                            sum += FNoise.ValueTile(i / (float)size * period, j / (float)size * period, 5 + ch * 17 + oct, period) * amp;
                            norm += amp; amp *= 0.5f; period *= 2;
                        }
                        t.Rgba[o + ch] = B(sum / norm * 255f);
                    }
                    t.Rgba[o + 3] = 255;
                }
            return t;
        }
    }
}
