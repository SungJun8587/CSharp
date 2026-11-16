using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class HitMaterialTests
    {
        public static void All()
        {
            T.Check(HitMaterial.Of(1, true) == HitMat.Gel && HitMaterial.Of(101, true) == HitMat.Gel, "slime and its boss are gel");
            T.Check(HitMaterial.Of(17, true) == HitMat.Bone && HitMaterial.Of(18, true) == HitMat.Stone && HitMaterial.Of(19, true) == HitMat.Spirit, "skeleton bone, golem stone, ghost spirit");
            T.Check(HitMaterial.Of(11, true) == HitMat.Metal && HitMaterial.Of(13, true) == HitMat.Metal && HitMaterial.Of(4, true) == HitMat.Metal, "knights and bomb are metal");
            T.Check(HitMaterial.Of(10, true) == HitMat.Wood && HitMaterial.Of(14, true) == HitMat.Fire && HitMaterial.Of(2, true) == HitMat.Plant, "treant wood, flame fire, mushroom plant");
            T.Check(HitMaterial.Of(5, true) == HitMat.Flesh && HitMaterial.Of(26, true) == HitMat.Flesh && HitMaterial.Of(24, true) == HitMat.Shell, "beasts are flesh, hermit is shell");
            T.Check(HitMaterial.Of(1, false) == HitMat.Flesh, "players are never gel");
            for (int t = 1; t <= 26; ++t)
            {
                HitMat m = HitMaterial.Of((uint)t, true); int a, b; HitMaterial.Colors(m, out a, out b);
                T.Check(a != b && a >= 0 && b >= 0 && a <= 0xFFFFFF && b <= 0xFFFFFF, "valid colors for template " + t);
            }
        }
    }
}
