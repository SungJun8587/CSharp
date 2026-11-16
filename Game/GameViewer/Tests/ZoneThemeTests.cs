//***************************************************************************
// ZoneThemeTests.cs : 존 번호 -> 맵 분위기, 사막 소품 고르기.
//***************************************************************************
using GameViewer.Core.Logic;

namespace GameViewer.Tests
{
    public static class ZoneThemeTests
    {
        public static void Basics()
        {
            T.Eq(MapKind.Forest, ZoneTheme.For(1), "zone 1 is forest");
            T.Eq(MapKind.Desert, ZoneTheme.For(2), "zone 2 is desert");
            T.Eq(MapKind.Forest, ZoneTheme.For(99), "unknown zone falls back to forest");
            T.Eq(ZoneTheme.DesertProp.Cactus, ZoneTheme.PropOf(1.5f), "small radius is a cactus");
            T.Eq(ZoneTheme.DesertProp.DeadTree, ZoneTheme.PropOf(2.6f), "mid radius is a dead tree");
            T.Eq(ZoneTheme.DesertProp.Palm, ZoneTheme.PropOf(3.6f), "large radius is a palm");
            T.Check(ZoneTheme.IsPool(1, 690, 340, 44, 28), "forest pond rect is water");
            T.Check(!ZoneTheme.IsPool(1, 150, 150, 30, 6), "ordinary rock is not water");
            T.Check(!ZoneTheme.IsPool(2, 690, 340, 44, 28), "no ponds in the desert");
            T.Eq(MapKind.Snow, ZoneTheme.For(3), "zone 3 is snow");
            T.Eq(MapKind.Volcano, ZoneTheme.For(4), "zone 4 is volcano");
            T.Eq(MapKind.Swamp, ZoneTheme.For(5), "zone 5 is swamp");
            T.Eq("설원", ZoneTheme.NameOf(3), "zone 3 name");
            T.Eq(10, ZoneTheme.Zones.Length, "ten selectable zones");
            T.Eq(MapKind.Crystal, ZoneTheme.For(8), "zone 8 is crystal cave");
            T.Eq("암흑", ZoneTheme.NameOf(10), "zone 10 name");
            for (int z = 3; z <= 10; ++z) T.Check(ZonePools.All[(uint)z].Length >= 4, "zone " + z + " has pools");
            T.Eq(ZoneTheme.PropSize.Small, ZoneTheme.SizeOf(1.5f), "small prop");
            T.Eq(ZoneTheme.PropSize.Mid, ZoneTheme.SizeOf(2.6f), "mid prop");
            T.Eq(ZoneTheme.PropSize.Large, ZoneTheme.SizeOf(3.6f), "large prop");
        }

        public static void GateTravelSteps()
        {
            ZoneGate e = new ZoneGate { Edge = 'E', Pos = 500, Width = 60, TargetZone = 2, TargetX = 14, TargetY = 500 };
            System.Collections.Generic.List<ZoneGate> gs = new System.Collections.Generic.List<ZoneGate> { e };
            GateTravel t = new GateTravel();
            T.Eq(GateStep.None, t.Update(1000, gs, 500, 500, 0, 0, 1000, 1000, true), "far from the gate does nothing");
            T.Eq(GateStep.None, t.Update(1000, gs, 999, 500, 0, 0, 1000, 1000, false), "dead hero does not travel");
            T.Eq(GateStep.Started, t.Update(1000, gs, 999, 500, 0, 0, 1000, 1000, true), "touching the gate starts the fade");
            T.Check(t.Fade(1000) == 0f && t.Fade(1000 + GateTravel.FadeOutMs) >= 0.999f, "fade goes 0 to 1");
            T.Eq(GateStep.None, t.Update(1100, gs, 999, 500, 0, 0, 1000, 1000, true), "waits while fading");
            T.Eq(GateStep.Switch, t.Update(1000 + GateTravel.FadeOutMs, gs, 999, 500, 0, 0, 1000, 1000, true), "switch when fully dark");
            T.Check(t.Pending == e, "pending gate is the one entered");
            t.Arrived(2000);
            T.Check(t.Pending == null && t.Fade(2000) == 0f, "arrival clears the fade");
            T.Eq(GateStep.None, t.Update(2500, gs, 999, 500, 0, 0, 1000, 1000, true), "cooldown blocks bouncing back");
            T.Eq(GateStep.Started, t.Update(2000 + GateTravel.CooldownMs, gs, 999, 500, 0, 0, 1000, 1000, true), "reacts again after the cooldown");
        }

        public static void Gates()
        {
            ZoneGate e = new ZoneGate { Edge = 'E', Pos = 500, Width = 60, TargetZone = 2, TargetX = 20, TargetY = 500 };
            System.Collections.Generic.List<ZoneGate> gs = new System.Collections.Generic.List<ZoneGate> { e };
            MapKind k; bool outside;
            T.Check(ZoneGates.Weight(gs, 500, 500, 0, 0, 1000, 1000, out k, out outside) == 0f, "far from the gate is unblended");
            float w = ZoneGates.Weight(gs, 999, 500, 0, 0, 1000, 1000, out k, out outside);
            T.Check(w > 0.9f, "at the gate edge is fully blended");
            T.Eq(MapKind.Desert, k, "blends toward the target biome");
            T.Check(ZoneGates.Weight(gs, 999, 700, 0, 0, 1000, 1000, out k, out outside) == 0f, "off the gate width is unblended");
            T.Check(ZoneGates.Weight(gs, 1020, 500, 0, 0, 1000, 1000, out k, out outside) > 0f && outside, "beyond the edge is outside");
            T.Check(ZoneGates.Entered(gs, 997, 510, 0, 0, 1000, 1000, 6) == e, "edge inside gate width enters");
            T.Check(ZoneGates.Entered(gs, 997, 600, 0, 0, 1000, 1000, 6) == null, "edge outside gate width does not");
            float cx, cy; ZoneGates.CenterOf(e, 0, 0, 1000, 1000, out cx, out cy);
            T.Check(cx == 1000 && cy == 500, "gate center");
        }
    }
}
