//***************************************************************************
// SkillAim.cs : 스킬 한 번에 서버로 보낼 값(대상 ID, 좌표)을 모양별로 정한다. 서버 CastSkill 의 요청 규칙과 같다.
//   Single  targetId 가 필요. 적 스킬은 적(몬스터), 아군 스킬(Target=Ally)은 아군(플레이어, 없으면 자기 자신)
//   Area    좌표가 중심. 사거리 안이어야 한다
//   Self    아무것도 필요 없다 (시전자 위치가 중심)
//   Cone/Line  좌표는 방향만 정한다. 시전자와 거의 같은 위치면 거부되므로 최소 거리를 보장한다
//***************************************************************************
using System;
using GameViewer.Core.Data;
using GameViewer.Core.World;

namespace GameViewer.Core.Logic
{
    public struct SkillAimResult
    {
        public bool Ok;
        public ulong TargetId;
        public float X, Y;
        public string WhyNot;       // Ok 가 false 일 때 사용자에게 보일 사유
        public Entity Target;       // 정해진 대상 (없을 수 있음)
    }

    public static class SkillAim
    {
        /// <summary>
        /// 시전 값을 정한다.
        /// selected: 사용자가 고른 대상(없으면 null), hasMouse/mouseX/mouseY: 마우스가 가리키는 월드 좌표(조준용, 선택 사항)
        /// </summary>
        public static SkillAimResult Resolve(SkillRow sk, WorldModel w, Entity selected, bool hasMouse, float mouseX, float mouseY, long now)
        {
            SkillAimResult r = new SkillAimResult();
            Entity self = w.Self;
            if (self == null || self.Dead) { r.WhyNot = "영웅이 없습니다"; return r; }
            float sx, sy; self.PosAt(now, out sx, out sy);
            if (selected != null && selected.Dead) selected = null;

            if (sk.IsSelf) { r.Ok = true; r.X = sx; r.Y = sy; return r; }

            if (sk.IsSingle)
            {
                Entity t;
                if (sk.IsAlly)
                {
                    t = selected != null && selected.IsPlayer ? selected : self;      // 아군을 고르지 않았으면 자기 자신
                }
                else
                {
                    t = selected != null && selected.IsMonster ? selected : Nearest(w, sx, sy, now);
                    if (t == null) { r.WhyNot = "스킬 대상이 없습니다 (적을 선택하세요)"; return r; }
                }
                r.Target = t; r.TargetId = t.Id;
                float tx, ty; t.PosAt(now, out tx, out ty);
                float dist = (float)Math.Sqrt((tx - sx) * (tx - sx) + (ty - sy) * (ty - sy));
                if (t != self && dist > sk.Range + 0.3f) { r.WhyNot = sk.Name + ": 사거리 밖 (거리 " + dist.ToString("0.0") + " / 사거리 " + sk.Range.ToString("0.0") + ")"; return r; }
                r.Ok = true;
                return r;
            }

            if (sk.IsArea)
            {
                // 사거리 1.5 이하는 시전자 자신이 중심인 스킬(전투 함성, 집중 등)이다. 적을 골라 둬도 그 위치를 중심으로 보내면 사거리 밖으로 거부된다.
                if (sk.Range <= 1.5) { r.Ok = true; r.X = sx; r.Y = sy; return r; }

                float x, y;
                Entity t = selected != null && !sk.IsAlly ? selected : null;
                if (t != null) t.PosAt(now, out x, out y);
                else if (hasMouse) { x = mouseX; y = mouseY; }
                else if (!sk.IsAlly && (t = Nearest(w, sx, sy, now)) != null) t.PosAt(now, out x, out y);
                else { x = sx; y = sy; }

                // 사거리 밖이면 시전자 쪽으로 당겨 중심을 사거리 안에 둔다. 범위가 대상에 닿지 못하면 거부하고 이유를 알려 준다.
                float dx = x - sx, dy = y - sy, d = (float)Math.Sqrt(dx * dx + dy * dy);
                float maxD = (float)sk.Range - 0.3f;
                if (d > maxD)
                {
                    if (d > sk.Range + sk.Radius) { r.Target = t; r.WhyNot = sk.Name + ": 사거리 밖 (거리 " + d.ToString("0.0") + " / 사거리 " + sk.Range.ToString("0.0") + ")"; return r; }
                    float k = maxD / d; x = sx + dx * k; y = sy + dy * k;
                }
                r.Ok = true; r.Target = t; r.X = x; r.Y = y;
                return r;
            }

            // Cone / Line: 선택한 적 쪽, 없으면 마우스, 없으면 가장 가까운 적, 마지막으로 바라보는 방향
            {
                float ax, ay; bool have = false;
                Entity t = selected != null && selected.IsMonster ? selected : null;
                if (t != null) { t.PosAt(now, out ax, out ay); have = true; }
                else if (hasMouse) { ax = mouseX; ay = mouseY; have = true; }
                else { t = Nearest(w, sx, sy, now); if (t != null) { t.PosAt(now, out ax, out ay); have = true; } else { ax = 0; ay = 0; } }
                if (!have) { ax = sx + (Math.Abs(self.FaceX) + Math.Abs(self.FaceY) > 0.01f ? self.FaceX : 1f); ay = sy + (Math.Abs(self.FaceX) + Math.Abs(self.FaceY) > 0.01f ? self.FaceY : 0f); }
                float dx = ax - sx, dy = ay - sy, d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < 1f) { if (d < 0.001f) { dx = 1f; dy = 0f; d = 1f; } float k = 1f / d; ax = sx + dx * k; ay = sy + dy * k; }   // 최소 1칸은 떨어진 방향점
                r.Ok = true; r.Target = t; r.X = ax; r.Y = ay;
                return r;
            }
        }

        /// <summary>시전자에서 가장 가까운 살아 있는 몬스터.</summary>
        public static Entity Nearest(WorldModel w, float sx, float sy, long now)
        {
            Entity best = null; float bd = float.MaxValue;
            foreach (System.Collections.Generic.KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsMonster || e.Dead) continue;
                float x, y; e.PosAt(now, out x, out y);
                float d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }
    }
}
