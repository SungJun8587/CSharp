using System;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;
using Pk = GameViewer.Core.Protocol.ServerPackets;

namespace GameViewer.Tests
{
    /// <summary>스킬 모양(Single/Area/Self/Cone/Line, 적/아군)별 요청 값과 연출.</summary>
    public static class SkillAimTests
    {
        const ulong Me = 100, Mon = 0x8000000000000001UL, Ally = 101;

        static WorldModel World()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            WorldModel w = new WorldModel(d, Me);
            w.InitSelf(10, 10, 1, 0);
            return w;
        }

        static Entity Add(WorldModel w, ulong id, byte type, float x, float y)
        {
            Entity e = new Entity(); e.Id = id; e.Type = type; e.X = x; e.Y = y; e.DestX = x; e.DestY = y; e.Hp = e.MaxHp = 100;
            w.Entities[id] = e; return e;
        }

        public static void DataShapes()
        {
            GameData d = GameData.Load(Program.Dir("GameData"), 1);
            SkillRow heal = d.Skill(12), pulse = d.Skill(16), fan = d.Skill(14), beam = d.Skill(15);
            T.Check(heal != null && heal.IsSingle && heal.IsAlly && heal.Heals, "skill 12: single ally heal");
            T.Check(pulse != null && pulse.IsSelf && pulse.IsBurst && !pulse.IsArea && pulse.Radius > 4, "skill 16: self burst");
            T.Check(fan != null && fan.IsCone && fan.IsAimed && fan.Angle == 90.0, "skill 14: cone with angle");
            T.Check(beam != null && beam.IsLine && beam.IsAimed && beam.Radius == 1.0, "skill 15: line with width");
            T.Check(d.Skill(2).IsArea && d.Skill(2).IsBurst && !d.Skill(2).IsAlly, "area stays area");
        }

        public static void Resolve()
        {
            WorldModel w = World();
            GameData d = w.Data;
            Entity mon = Add(w, Mon, (byte)EntityType.Monster, 14, 10);
            Entity ally = Add(w, Ally, (byte)EntityType.Player, 12, 12);

            // 적 단일: 선택이 없으면 가장 가까운 적
            SkillAimResult r = SkillAim.Resolve(d.Skill(3), w, null, false, 0, 0, 0);
            T.Check(r.Ok && r.TargetId == Mon, "single enemy: nearest monster when nothing selected");
            r = SkillAim.Resolve(d.Skill(1), w, null, false, 0, 0, 0);          // 사거리 3 인데 4 떨어져 있다: 서버가 조용히 거부하므로 미리 이유를 알린다
            T.Check(!r.Ok && r.WhyNot != null && r.WhyNot.Contains("사거리") && r.TargetId == Mon, "single enemy: out of range -> reason, target kept for chase");
            Entity close = Add(w, 9001, (byte)EntityType.Monster, 11.5f, 10);
            r = SkillAim.Resolve(d.Skill(1), w, close, false, 0, 0, 0);
            T.Check(r.Ok && r.TargetId == 9001, "single enemy: in range ok");
            w.Entities.Remove(9001);
            // 자기 중심 범위 스킬(전투 함성 등, 사거리 1)은 적을 선택해 둬도 자기 위치를 중심으로 보낸다
            r = SkillAim.Resolve(d.Skill(7), w, mon, false, 0, 0, 0);
            T.Check(r.Ok && r.X == 10 && r.Y == 10, "self-centered area: ignores selected enemy");
            // 아군 단일(치유): 아군을 고르지 않으면 자기 자신, 적을 골라도 적에게 쓰지 않는다
            r = SkillAim.Resolve(d.Skill(12), w, null, false, 0, 0, 0);
            T.Check(r.Ok && r.TargetId == Me, "ally single: self by default");
            r = SkillAim.Resolve(d.Skill(12), w, mon, false, 0, 0, 0);
            T.Check(r.Ok && r.TargetId == Me, "ally single: ignores selected enemy");
            r = SkillAim.Resolve(d.Skill(12), w, ally, false, 0, 0, 0);
            T.Check(r.Ok && r.TargetId == Ally, "ally single: selected ally");
            // Self: 값이 필요 없다 (적이 없어도 가능)
            w.Entities.Remove(Mon);
            r = SkillAim.Resolve(d.Skill(16), w, null, false, 0, 0, 0);
            T.Check(r.Ok && r.TargetId == 0, "self: no target needed");
            r = SkillAim.Resolve(d.Skill(1), w, null, false, 0, 0, 0);
            T.Check(!r.Ok && r.WhyNot != null, "single enemy: no target -> reason");
            // Cone/Line: 방향점은 시전자에서 1칸 이상 떨어져야 한다 (서버는 같은 위치를 InvalidTarget 으로 거부)
            Entity mon2 = Add(w, Mon, (byte)EntityType.Monster, 10.2f, 10);
            r = SkillAim.Resolve(d.Skill(14), w, mon2, false, 0, 0, 0);
            T.Check(r.Ok && Math.Abs(r.X - 10) + Math.Abs(r.Y - 10) >= 0.99, "cone: aim point is at least 1 away");
            w.Entities.Remove(Mon);
            r = SkillAim.Resolve(d.Skill(15), w, null, true, 30, 10, 0);
            T.Check(r.Ok && r.X == 30 && r.Y == 10, "line: aims at the mouse when no enemy");
            r = SkillAim.Resolve(d.Skill(15), w, null, false, 0, 0, 0);
            T.Check(r.Ok && (r.X != 10 || r.Y != 10), "line: falls back to facing direction");
            // Area: 선택한 적 위치 / 사거리가 긴 스킬은 마우스 / 짧으면 자기 위치
            mon = Add(w, Mon, (byte)EntityType.Monster, 16, 10);
            r = SkillAim.Resolve(d.Skill(4), w, mon, true, 0, 0, 0);
            T.Check(r.Ok && r.X == 16 && r.Y == 10, "area: selected enemy position");
            r = SkillAim.Resolve(d.Skill(4), w, null, true, 18, 11, 0);
            T.Check(r.Ok && r.X == 18 && r.Y == 11, "area: mouse for long range");
            r = SkillAim.Resolve(d.Skill(7), w, null, true, 18, 11, 0);
            T.Check(r.Ok && r.X == 10 && r.Y == 10, "area: self position for short range");
        }

        public static void Effects()
        {
            WorldModel w = World();
            Add(w, Mon, (byte)EntityType.Monster, 14, 10);
            // Self(충격파): 시전자 중심 폭발 링
            Apply(w, Pk.Skill(Me, 16, 0, 10, 10, 2), 1000);
            int rings = 0; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Ring && f.SkillId == 16 && f.Radius > 4f) ++rings;
            T.Eq(1, rings, "self skill draws a ring at the caster");
            // Cone(화염 부채): 부채꼴 정보가 실린 베기
            w.Effects.Clear();
            Apply(w, Pk.Skill(Me, 14, 0, 18, 10, 1), 2000);
            ViewEffect fan = null; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Slash && f.SkillId == 14) fan = f;
            T.Check(fan != null && fan.Value == 800 && fan.X2 == 90f && fan.Y2 == 0f, "cone effect carries range and angle");
            // Line(관통 사격): 폭이 실린 베기
            w.Effects.Clear();
            Apply(w, Pk.Skill(Me, 15, 0, 18, 10, 1), 3000);
            ViewEffect beam = null; foreach (ViewEffect f in w.Effects) if (f.Kind == EffectKind.Slash && f.SkillId == 15) beam = f;
            T.Check(beam != null && beam.Value == 1400 && beam.X2 == 0f && beam.Y2 == 1f, "line effect carries range and width");
        }

        static void Apply(WorldModel w, byte[] pkt, long now) { w.Apply(Pk.F(pkt), now); }
    }
}
