using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class BossFxTests
    {
        public static void All()
        {
            T.Check(BossFx.Entrance(100, 100) == 0f, "entrance starts at 0");
            T.Check(BossFx.Entrance(100 + BossFx.EntranceMs, 100) == 1f, "entrance ends at 1");
            T.Check(BossFx.Entrance(50, 100) == 1f, "before appearing means no entrance");
            T.Check(BossFx.EntranceAlpha(0.2f) == 0f && BossFx.EntranceAlpha(1f) == 1f, "body fades in");
            T.Check(BossFx.EntranceDrop(0.1f) > BossFx.EntranceDrop(0.6f) && BossFx.EntranceDrop(0.9f) == 0f, "boss lands");
            T.Check(BossFx.Phase(0, 1000) == -1f && BossFx.Phase(1000 + BossFx.PhaseMs, 1000) == -1f, "phase effect only inside its window");
            T.Check(BossFx.PhaseScale(-1f, 2) == 1f, "no scale outside the window");
            float mx = 0f; for (int i = 0; i <= 20; ++i) mx = System.Math.Max(mx, BossFx.PhaseScale(i / 20f, 3));
            T.Check(mx > 1.1f && mx < 1.3f, "phase swell is noticeable but bounded");
            T.Check(BossFx.PhaseScale(0.999f, 2) < 1.05f, "scale returns to normal");
            T.Check(BossFx.PhaseTint(0.5f) == 1f && BossFx.PhaseTint(0f) == 0f && BossFx.PhaseTint(-1f) == 0f, "tint peaks mid-way");
            T.Check(BossFx.PhaseAura(1) == 0f && BossFx.PhaseAura(9) <= 0.35f, "permanent aura is capped");
            T.Check(BossFx.DeathFlash(0.12f) > 0.99f && BossFx.DeathFlash(0.9f) == 0f, "death flash is short");
            T.Check(BossFx.DeathPillar(0.45f) > 0.99f && BossFx.DeathPillar(0f) == 0f && BossFx.DeathPillar(1f) < 0.01f, "pillar rises and fades");
        }

        public static void Damage()
        {
            T.Check(DamageStyle.Tier(1, 1000) == 0 && DamageStyle.Tier(50, 1000) == 1 && DamageStyle.Tier(130, 1000) == 2 && DamageStyle.Tier(300, 1000) == 3, "damage tiers by ratio");
            T.Check(DamageStyle.Tier(10, 0) == 0 && DamageStyle.Tier(0, 100) == 0, "unknown max hp is plain");
            T.Check(DamageStyle.Scale(300, 1000, 3) > DamageStyle.Scale(130, 1000, 2) && DamageStyle.Scale(130, 1000, 2) > DamageStyle.Scale(5, 1000, 0), "bigger hits are bigger");
            T.Check(DamageStyle.Shake(1, 0.1f) == 0f && DamageStyle.Shake(3, 0.5f) == 0f, "only heavy hits shake, briefly");
            float mx = 0; for (int i = 0; i < 60; ++i) mx = System.Math.Max(mx, System.Math.Abs(DamageStyle.Shake(3, i / 200f)));
            T.Check(mx > 2f && mx <= 7f, "crit shake is visible and bounded");
            T.Check(DamageStyle.HealScale(500, 1000) > DamageStyle.HealScale(10, 1000), "big heals are bigger");
            T.Check(DamageStyle.Tag(3) == "치명" && DamageStyle.Tag(2) == "강타" && DamageStyle.Tag(1) == null, "tags");
        }
        public static void Stage()
        {
            int r1, g1, b1, r2, g2, b2;
            BossFx.Theme(116, out r1, out g1, out b1); BossFx.Theme(118, out r2, out g2, out b2);
            T.Check(g1 > r1 && r2 > b2, "spider boss is green, golem boss is orange");
            T.Check(BossFx.HasTheme(115) && BossFx.HasTheme(20) && BossFx.HasTheme(126) && BossFx.HasTheme(21) && !BossFx.HasTheme(7) && !BossFx.HasTheme(101), "themes only for the twelve newer monsters");
            int ra, ga, ba, rb, gb, bb;
            BossFx.Theme(121, out ra, out ga, out ba); BossFx.Theme(122, out rb, out gb, out bb);
            T.Check(ba > ra && rb > bb, "yeti boss is icy blue, lava boss is orange");
            T.Check(BossFx.LandShake(1000 + BossFx.LandMs + 10, 1000) > 5f, "shake right at landing");
            T.Check(BossFx.LandShake(1000 + BossFx.LandMs + 600, 1000) == 0f && BossFx.LandShake(1000 + 100, 1000) == 0f, "no shake before landing or long after");
            T.Check(BossFx.Crack(1000 + BossFx.LandMs - 1, 1000) < 0f && BossFx.Crack(1000 + BossFx.LandMs + 100, 1000) >= 0f && BossFx.Crack(1000 + BossFx.LandMs + BossFx.LandAfterMs + 1, 1000) < 0f, "cracks live after landing");
            T.Check(BossFx.ScreenFlash(500, 500, 0) > 0.9f && BossFx.ScreenFlash(500 + 300, 500, 0) == 0f, "flash decays quickly");
        }

    }
}
