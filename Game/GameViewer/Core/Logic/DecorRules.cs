using System;
using System.Collections.Generic;
using GameViewer.Core.Data;

namespace GameViewer.Core.Logic
{
    public sealed class DecorItem { public string Name; public int Var; public float X, Y, Width; public bool Flip; }

    /// <summary>
    /// 맵 장식(바위/덤불/그루터기 등) 배치 규칙. 지역 종류별 후보 표에서 격자 칸마다 결정적으로 뽑고,
    /// 충돌 블록(나무/바위/물) 위와 월드 가장자리는 피한다. 같은 지역이면 언제나 같은 배치(테스트 가능).
    /// </summary>
    public static class DecorRules
    {
        public const float Cell = 9f;
        public const int Variants = 3;

        public struct Kind { public string Name; public int Weight; public float Width; public Kind(string n, int w, float wd) { Name = n; Weight = w; Width = wd; } }

        public static Kind[] Table(MapKind k)
        {
            switch (k)
            {
                case MapKind.Desert: return new[] { new Kind("rock_sand", 5, 2.2f), new Kind("bush_dry", 4, 1.9f), new Kind("mushrooms", 0, 1f) };
                case MapKind.Snow: return new[] { new Kind("rock_snow", 5, 2.2f), new Kind("bush_snow", 4, 1.9f), new Kind("stump", 1, 1.3f) };
                case MapKind.Volcano: return new[] { new Kind("rock_lava", 7, 2.2f), new Kind("rock_dark", 3, 1.8f) };
                case MapKind.Swamp: return new[] { new Kind("reeds", 6, 1.5f), new Kind("bush_swamp", 4, 1.9f), new Kind("mushrooms", 3, 1.1f), new Kind("stump", 2, 1.3f) };
                case MapKind.Ruins: return new[] { new Kind("pillar", 4, 1.7f), new Kind("rock_grey", 4, 2.0f), new Kind("bush_dry", 2, 1.7f) };
                case MapKind.Coast: return new[] { new Kind("rock_grey", 4, 2.1f), new Kind("reeds", 4, 1.4f), new Kind("bush_green", 2, 1.8f) };
                case MapKind.Crystal: return new[] { new Kind("crystal_blue", 5, 1.5f), new Kind("crystal_purple", 5, 1.5f), new Kind("rock_dark", 2, 1.7f) };
                case MapKind.Sky: return new[] { new Kind("flowers", 6, 1.3f), new Kind("bush_green", 2, 1.7f), new Kind("rock_grey", 1, 1.8f) };
                case MapKind.Dark: return new[] { new Kind("rock_dark", 6, 2.0f), new Kind("crystal_purple", 2, 1.4f), new Kind("pillar", 2, 1.6f) };
                default: return new[] { new Kind("rock_grey", 4, 2.1f), new Kind("bush_green", 5, 1.9f), new Kind("stump", 3, 1.3f), new Kind("flowers", 4, 1.2f), new Kind("mushrooms", 3, 1.1f) };
            }
        }

        private static float H(uint zone, int i, int j, int salt)
        {
            unchecked
            {
                uint h = zone * 2654435761u ^ (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>격자 점마다 해시를 두고 부드럽게 이어 붙인 값 노이즈(0..1). 군집과 공터의 바탕.</summary>
        public static float Field(uint zone, float x, float y, float scale, int salt)
        {
            float fx = x / scale, fy = y / scale;
            int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
            float tx = fx - i, ty = fy - j;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = H(zone, i, j, salt), b = H(zone, i + 1, j, salt), c = H(zone, i, j + 1, salt), d = H(zone, i + 1, j + 1, salt);
            return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
        }

        public static bool Blocked(IList<BlockRow> blocks, float x, float y, float pad)
        {
            for (int i = 0; i < blocks.Count; ++i)
            {
                BlockRow b = blocks[i];
                if (b.IsCircle) { float dx = x - b.X, dy = y - b.Y, r = b.R + pad; if (dx * dx + dy * dy < r * r) return true; }
                else if (x > b.X - pad && x < b.X + b.W + pad && y > b.Y - pad && y < b.Y + b.H + pad) return true;
            }
            return false;
        }

        public static List<DecorItem> Generate(MapKind kind, uint zone, float x0, float y0, float x1, float y1, IList<BlockRow> blocks, float density)
        {
            List<DecorItem> res = new List<DecorItem>();
            Kind[] tab = Table(kind);
            int total = 0; for (int i = 0; i < tab.Length; ++i) total += tab[i].Weight;
            if (total <= 0) return res;
            int ci0 = (int)Math.Floor(x0 / Cell), cj0 = (int)Math.Floor(y0 / Cell), ci1 = (int)Math.Floor(x1 / Cell), cj1 = (int)Math.Floor(y1 / Cell);
            for (int j = cj0; j <= cj1; ++j)
                for (int i = ci0; i <= ci1; ++i)
                {
                    float x = (i + 0.1f + 0.8f * H(zone, i, j, 2)) * Cell, y = (j + 0.1f + 0.8f * H(zone, i, j, 3)) * Cell;
                    // 군집: 넓은 노이즈가 높은 곳은 빽빽하게, 낮은 곳은 공터. 평균 밀도는 비슷하게 유지한다.
                    float cl = Field(zone, x, y, 70f, 11);
                    float mul = cl > 0.58f ? 2.4f : (cl < 0.38f ? 0.12f : 0.7f);
                    if (H(zone, i, j, 1) > density * mul) continue;
                    if (x < x0 + 2f || x > x1 - 2f || y < y0 + 2f || y > y1 - 2f) continue;
                    int pick = (int)(H(zone, i, j, 4) * total), ti = 0;
                    while (ti < tab.Length - 1 && pick >= tab[ti].Weight) { pick -= tab[ti].Weight; ++ti; }
                    // 지역마다 대표 종류가 있어서(덤불 숲, 바위 지대 ...) 군집 안에서는 같은 종류가 모인다
                    if (H(zone, i, j, 8) < 0.65f) { int fav = (int)(Field(zone, x, y, 55f, 21) * tab.Length * 0.999f); if (tab[fav].Weight > 0) ti = fav; }
                    if (tab[ti].Weight <= 0) continue;
                    float w = tab[ti].Width * (0.8f + 0.45f * H(zone, i, j, 5));
                    if (Blocked(blocks, x, y, w * 0.6f)) continue;
                    DecorItem d = new DecorItem();
                    d.Name = tab[ti].Name; d.X = x; d.Y = y; d.Width = w; d.Var = (int)(H(zone, i, j, 6) * Variants) % Variants; d.Flip = H(zone, i, j, 7) < 0.5f;
                    res.Add(d);
                }
            res.Sort(delegate (DecorItem a, DecorItem b) { return a.Y.CompareTo(b.Y); });
            return res;
        }
    }
}
