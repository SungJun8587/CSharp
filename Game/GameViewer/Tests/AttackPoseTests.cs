using System;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;

namespace GameViewer.Tests
{
    /// <summary>공격/시전/피격 몸동작 계산.</summary>
    public static class AttackPoseTests
    {
        const long Now = 100000;

        static Entity Mon(uint tpl, float faceX)
        {
            Entity e = new Entity(); e.Id = 0x8000000000000001UL; e.Type = (byte)EntityType.Monster; e.TemplateId = tpl; e.FaceX = faceX; e.FaceY = 0; e.Hp = e.MaxHp = 100;
            return e;
        }
        static Entity Hero(float faceX) { Entity e = Mon(1, faceX); e.Id = 1; e.Type = (byte)EntityType.Player; return e; }

        public static void Strike()
        {
            Entity e = Mon(2, 1);
            T.Check(AttackPose.Compute(e, Now).IsIdentity, "idle is identity");
            e.AttackAnimMs = Now;
            BodyPose p0 = AttackPose.Compute(e, Now);
            BodyPose pk = AttackPose.Compute(e, Now + AttackPose.StrikeMs);
            BodyPose pe = AttackPose.Compute(e, Now + AttackPose.DurationMs(e) + 5);
            T.Check(Math.Abs(p0.OffX) < 0.01f, "starts at rest");
            T.Check(pk.OffX > 0.4f && pk.Lean > 5f, "peak lunges forward and leans forward");
            T.Check(pk.ScaleX > 1f && pk.ScaleY < 1f, "peak stretches along the strike");
            T.Check(pe.IsIdentity, "returns to rest when finished");
            // 복귀는 단조 감소
            float prev = pk.OffX; bool mono = true;
            for (int t = AttackPose.StrikeMs + AttackPose.HoldMs; t < AttackPose.DurationMs(e); t += 10)
            {
                float x = AttackPose.Compute(e, Now + t).OffX;
                if (x > prev + 0.0001f) mono = false;
                prev = x;
            }
            T.Check(mono, "recovery never swings back out");
            // 왼쪽을 보면 반대로
            Entity l = Mon(2, -1); l.AttackAnimMs = Now;
            BodyPose pl = AttackPose.Compute(l, Now + AttackPose.StrikeMs);
            T.Check(pl.OffX < -0.4f && pl.Lean < -5f, "facing left mirrors");
        }

        public static void Styles()
        {
            T.Check(AttackPose.StyleOf(Mon(1, 1)) == AttackStyle.Hop, "slime hops");
            T.Check(AttackPose.StyleOf(Mon(2, 1)) == AttackStyle.Lunge, "wolf lunges");
            T.Check(AttackPose.StyleOf(Mon(8, 1)) == AttackStyle.Slam, "heavy slams");
            T.Check(AttackPose.StyleOf(Hero(1)) == AttackStyle.Lunge, "hero lunges");
            Entity h = Mon(1, 1); h.AttackAnimMs = Now;
            float maxLift = 0; for (int t = 0; t < 440; t += 10) maxLift = Math.Max(maxLift, AttackPose.Compute(h, Now + t).Lift);
            T.Check(maxLift > 0.5f, "hop leaves the ground");
            Entity s = Mon(8, 1); s.AttackAnimMs = Now;
            BodyPose ps = AttackPose.Compute(s, Now + AttackPose.StrikeMs);
            T.Check(ps.ScaleY < 0.9f && ps.ScaleX > 1.05f, "slam squashes");
            Entity b = Mon(8, 1); b.Boss = new GameViewer.Core.Data.BossRow(); b.AttackAnimMs = Now;
            T.Check(AttackPose.Compute(b, Now + AttackPose.StrikeMs).OffX > ps.OffX, "boss reaches further");
            T.Check(AttackPose.DurationMs(b) > AttackPose.DurationMs(s), "boss moves slower");
        }

        public static void CastAndKinds()
        {
            Entity e = Mon(2, 1);
            e.CastSkillId = 31; e.CastStartMs = Now; e.CastTimeMs = 600;
            BodyPose early = AttackPose.Compute(e, Now + 100), late = AttackPose.Compute(e, Now + 540);
            T.Check(early.OffX < 0f && late.OffX < early.OffX - 0.05f, "casting pulls back more over time");
            T.Check(late.Lean < -5f, "casting leans back");
            Entity sl = Mon(8, 1); sl.CastSkillId = 36; sl.CastStartMs = Now; sl.CastTimeMs = 900;
            T.Check(AttackPose.Compute(sl, Now + 700).Lift > 0.15f, "heavy rears up while casting");
            Entity sq = Mon(1, 1); sq.CastSkillId = 30; sq.CastStartMs = Now; sq.CastTimeMs = 700;
            T.Check(AttackPose.Compute(sq, Now + 650).ScaleY < 0.85f, "slime crouches while casting");

            // 스킬은 모은 자세에서 더 크게 튀어나온다
            Entity basic = Mon(2, 1); basic.AttackAnimMs = Now; basic.AttackKind = 0;
            Entity skill = Mon(2, 1); skill.AttackAnimMs = Now; skill.AttackKind = 1;
            T.Check(AttackPose.Compute(skill, Now + AttackPose.StrikeMs).OffX > AttackPose.Compute(basic, Now + AttackPose.StrikeMs).OffX, "skill strikes harder");
            T.Check(AttackPose.Compute(skill, Now).OffX < -0.05f, "skill release starts from the wound-up pose");
            // 원거리는 반동
            Entity arch = Hero(1); arch.AttackAnimMs = Now; arch.AttackKind = 2;
            BodyPose pa = AttackPose.Compute(arch, Now + AttackPose.StrikeMs);
            T.Check(pa.OffX < -0.1f && pa.Lean < 0f, "ranged shot kicks back");
        }

        public static void HitReaction()
        {
            Entity e = Mon(2, -1);
            e.HitFlashMs = Now; e.HitDirX = 1f; e.HitDirY = 0f;       // 오른쪽으로 맞았다
            BodyPose p = AttackPose.Compute(e, Now + 10);
            T.Check(p.OffX > 0.1f && p.Lean > 3f && p.ScaleY < 1f, "pushed away from the attacker");
            T.Check(AttackPose.Compute(e, Now + 400).IsIdentity, "recoil ends");
            e.HitDirX = 0; T.Check(AttackPose.Compute(e, Now + 10).IsIdentity, "unknown direction: flash only");
            Entity b = Mon(8, 1); b.Boss = new GameViewer.Core.Data.BossRow(); b.HitFlashMs = Now; b.HitDirX = 1;
            Entity m = Mon(8, 1); m.HitFlashMs = Now; m.HitDirX = 1;
            T.Check(AttackPose.Compute(b, Now + 10).OffX < AttackPose.Compute(m, Now + 10).OffX, "boss barely flinches");
            Entity d = Mon(2, 1); d.Dead = true; d.AttackAnimMs = Now; d.HitFlashMs = Now; d.HitDirX = 1;
            T.Check(AttackPose.Compute(d, Now + 30).IsIdentity, "dead bodies do not move");
        }

        public static void SwipeAndWorld()
        {
            Entity e = Mon(2, 1); e.AttackAnimMs = Now; e.AttackKind = 0;
            T.Check(AttackPose.SwipeProgress(e, Now + 50) > 0f && AttackPose.SwipeProgress(e, Now + 500) < 0f, "swipe window");
            e.AttackKind = 1; T.Check(AttackPose.SwipeProgress(e, Now + 50) < 0f, "skills draw their own effects");

            // 월드: 적중 알림으로 공격/피격 정보가 채워진다
            GameViewer.Core.Data.GameData d = GameViewer.Core.Data.GameData.Load(Program.Dir("GameData"), 1);
            WorldModel w = new WorldModel(d, 100);
            w.InitSelf(10, 10, 1, 0);
            Entity a = new Entity(); a.Id = 0x8000000000000001UL; a.Type = (byte)EntityType.Monster; a.TemplateId = 2; a.X = a.DestX = 10; a.Y = a.DestY = 14; a.Hp = a.MaxHp = 50;
            w.Entities[a.Id] = a;
            w.Apply(GameViewer.Core.Protocol.ServerPackets.F(GameViewer.Core.Protocol.ServerPackets.Damage(a.Id, 100, 5, 90, 100)), Now);
            Entity me = w.Entities[100];
            T.Check(a.AttackAnimMs == Now && a.AttackKind == 0, "attacker animates, melee");
            T.Check(me.HitDirY < -0.9f, "victim recoils away from the attacker (up)");
        }
    }
}
