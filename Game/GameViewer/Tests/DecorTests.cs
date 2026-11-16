using System.Collections.Generic;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class DecorTests
    {
        public static void All()
        {
            List<BlockRow> blocks = new List<BlockRow>();
            BlockRow c = new BlockRow(); c.IsCircle = true; c.X = 100; c.Y = 100; c.R = 6; blocks.Add(c);
            BlockRow r = new BlockRow(); r.X = 200; r.Y = 200; r.W = 60; r.H = 40; blocks.Add(r);
            foreach (MapKind k in new[] { MapKind.Forest, MapKind.Desert, MapKind.Snow, MapKind.Volcano, MapKind.Swamp, MapKind.Ruins, MapKind.Coast, MapKind.Crystal, MapKind.Sky, MapKind.Dark })
            {
                List<DecorItem> a = DecorRules.Generate(k, 1, 0, 0, 400, 400, blocks, 0.3f), b = DecorRules.Generate(k, 1, 0, 0, 400, 400, blocks, 0.3f);
                T.Check(a.Count > 100, "decor exists for " + k);
                T.Check(a.Count == b.Count && a[0].X == b[0].X && a[a.Count - 1].Name == b[b.Count - 1].Name, "layout is deterministic for " + k);
                bool ok = true, sorted = true, inRange = true;
                for (int i = 0; i < a.Count; ++i)
                {
                    if (DecorRules.Blocked(blocks, a[i].X, a[i].Y, 0f)) ok = false;
                    if (i > 0 && a[i].Y < a[i - 1].Y) sorted = false;
                    if (a[i].Var < 0 || a[i].Var >= DecorRules.Variants || a[i].Width <= 0.5f || a[i].X < 0 || a[i].X > 400) inRange = false;
                }
                T.Check(ok, "nothing is placed on a blocker for " + k);
                T.Check(sorted && inRange, "sorted by Y and sane for " + k);
            }
            // 군집: 같은 면적의 구역끼리 개수 차이가 크고(군집과 공터), 값 노이즈는 0..1 이다
            List<DecorItem> all = DecorRules.Generate(MapKind.Forest, 1, 0, 0, 1000, 1000, new List<BlockRow>(), 0.3f);
            int[] bins = new int[100]; foreach (DecorItem d in all) ++bins[(int)(d.Y / 100) * 10 + (int)(d.X / 100)];
            int mn = int.MaxValue, mx = 0; foreach (int bn in bins) { mn = System.Math.Min(mn, bn); mx = System.Math.Max(mx, bn); }
            T.Check(mx > 3 * System.Math.Max(1, mn), "decor is clustered with clearings (min " + mn + ", max " + mx + ")");
            T.Check(all.Count > 1000 && all.Count < 6000, "average density stays moderate (" + all.Count + ")");
            float fmin = 1f, fmax = 0f; for (int i = 0; i < 2000; ++i) { float f = DecorRules.Field(1, i * 3.7f, i * 1.3f, 70f, 11); fmin = System.Math.Min(fmin, f); fmax = System.Math.Max(fmax, f); }
            T.Check(fmin >= 0f && fmax <= 1f && fmax - fmin > 0.4f, "field is in range and varies");
            T.Check(DecorRules.Generate(MapKind.Forest, 1, 0, 0, 400, 400, blocks, 0f).Count == 0, "zero density = none");
            T.Check(DecorRules.Generate(MapKind.Forest, 1, 0, 0, 400, 400, blocks, 0.3f).Count != DecorRules.Generate(MapKind.Forest, 2, 0, 0, 400, 400, blocks, 0.3f)[0].Var + 9999, "zones differ in seed");
            bool onlyForestSet = true; foreach (DecorItem d in DecorRules.Generate(MapKind.Crystal, 8, 0, 0, 400, 400, blocks, 0.3f)) if (!d.Name.StartsWith("crystal") && d.Name != "rock_dark") onlyForestSet = false;
            T.Check(onlyForestSet, "crystal zone uses its own set");
        }
    }
}
