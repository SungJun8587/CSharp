using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class MxTests
    {
        public static void All()
        {
            T.Check(Mx.Smooth(0f, 1f, -1f) == 0f && Mx.Smooth(0f, 1f, 2f) == 1f && System.Math.Abs(Mx.Smooth(0f, 1f, 0.5f) - 0.5f) < 1e-6f, "smooth range");
            T.Check(System.Math.Abs(Mx.Smooth(2f, 4f, 3f) - 0.5f) < 1e-6f, "smooth with offset range");
            T.Check(Mx.Ease(0f) == 0f && Mx.Ease(1f) == 1f && Mx.Ease(0.5f) == 0.75f, "ease out");
            T.Check(Mx.Clamp01(-3f) == 0f && Mx.Clamp01(7f) == 1f && Mx.Clamp01(0.25f) == 0.25f, "clamp01");
            T.Eq(0x000000, Mx.MixRgb(0x000000, 0xFFFFFF, 0f), "mix at 0");
            T.Eq(0xFEFEFE, Mx.MixRgb(0x000000, 0xFFFFFF, 0.999f) & 0xFFFFFF, "mix near 1");
            T.Eq(0xFFFFFF, Mx.MixRgb(0x000000, 0xFFFFFF, 5f), "mix clamps t");
            float r = 0, g = 100, b = 200;
            Mx.MixInto(ref r, ref g, ref b, 100, 200, 0, 0.5f);
            T.Check(r == 50f && g == 150f && b == 100f, "mix into");
        }
    }
}
