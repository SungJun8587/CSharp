using System.Collections.Generic;
using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class SkillBarTests
    {
        public static void Order()
        {
            SkillBarOrder o = new SkillBarOrder();
            List<uint> raw = new List<uint> { 10, 20, 30, 40 };
            T.Eq(raw, o.Apply(1, raw), "no saved order keeps the original list");
            T.Check(o.Swap(1, raw, 0, 2), "swap slots");
            List<uint> a = o.Apply(1, raw);
            T.Check(a[0] == 30 && a[1] == 20 && a[2] == 10 && a[3] == 40, "slots swapped");
            T.Check(o.Apply(2, raw)[0] == 10, "other hero is unaffected");
            T.Check(!o.Swap(1, raw, 1, 1) && !o.Swap(1, raw, -1, 2) && !o.Swap(1, raw, 0, 9), "invalid swaps are refused");
            List<uint> grown = new List<uint> { 10, 20, 30, 40, 50 };
            List<uint> g = o.Apply(1, grown);
            T.Check(g.Count == 5 && g[0] == 30 && g[4] == 50, "new skill is appended");
            List<uint> shrunk = new List<uint> { 10, 30 };
            List<uint> sh = o.Apply(1, shrunk);
            T.Check(sh.Count == 2 && sh[0] == 30 && sh[1] == 10, "removed skill is dropped");
            o.Reset(1);
            T.Eq(raw, o.Apply(1, raw), "reset restores the original order");
        }
    }
}
