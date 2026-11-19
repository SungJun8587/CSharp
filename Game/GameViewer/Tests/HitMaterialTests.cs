using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class HitMaterialTests
    {
        public static void All()
        {
            T.Check(HitMaterial.Of(1, true) == HitMat.Gel && HitMaterial.Of(101, true) == HitMat.Gel, "slime and its boss are gel");
            T.Check(HitMaterial.Of(13, true) == HitMat.Bone && HitMaterial.Of(22, true) == HitMat.Stone && HitMaterial.Of(15, true) == HitMat.Spirit, "skeleton bone, golem stone, ghost spirit");
            T.Check(HitMaterial.Of(19, true) == HitMat.Metal && HitMaterial.Of(20, true) == HitMat.Metal && HitMaterial.Of(14, true) == HitMat.Metal, "knights and bomb are metal");
            T.Check(HitMaterial.Of(5, true) == HitMat.Plant && HitMaterial.Of(9, true) == HitMat.Plant && HitMaterial.Of(122, true) == HitMat.Stone, "mushroom and cactus are plant, golem boss is stone");
            T.Check(HitMaterial.Of(3, true) == HitMat.Flesh && HitMaterial.Of(7, true) == HitMat.Flesh && HitMaterial.Of(6, true) == HitMat.Shell && HitMaterial.Of(12, true) == HitMat.Shell, "beasts are flesh, hermit and crab are shell");
            T.Check(HitMaterial.Of(1, false) == HitMat.Flesh, "players are never gel");
            for (int t = 1; t <= 22; ++t)
            {
                HitMat m = HitMaterial.Of((uint)t, true); int a, b; HitMaterial.Colors(m, out a, out b);
                T.Check(a != b && a >= 0 && b >= 0 && a <= 0xFFFFFF && b <= 0xFFFFFF, "valid colors for template " + t);
            }
        }
    }
}
