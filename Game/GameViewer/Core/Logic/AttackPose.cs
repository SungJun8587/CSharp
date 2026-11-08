//***************************************************************************
// AttackPose.cs : 공격/시전/피격 때의 "몸동작". 그림 한 장을 변형(밀기, 기울이기, 눌러 늘이기, 뛰기)해서 움직임을 만든다.
//  - 시전 중: 뒤로 젖히며 힘을 모은다(몬스터 종류별로 웅크림/치켜듦). 시전 시간이 곧 예비동작이라 피할 타이밍이 눈에 보인다.
//  - 타격: 빠르게 내지르고(약 70ms) 잠깐 멈췄다가 천천히 돌아온다. 스킬은 모은 자세에서 더 크게 튀어나온다.
//  - 원거리: 발사 반동으로 살짝 뒤로 물러난다.
//  - 피격: 공격자 반대쪽으로 밀리며 눌린다.
// System.Drawing 에 의존하지 않는 순수 계산이라 테스트할 수 있다. 화면 변환은 WorldRenderer 가 한다.
//***************************************************************************
using System;
using GameViewer.Core.World;

namespace GameViewer.Core.Logic
{
    public enum AttackStyle { Lunge, Hop, Slam }

    /// <summary>한 순간의 몸동작. 단위는 월드 단위(unit), 각도는 화면 시계 방향이 +.</summary>
    public struct BodyPose
    {
        public float OffX, OffY;        // 월드 평면에서 밀린 만큼
        public float Lift;              // 위로 뜬 높이 (+ 위)
        public float Lean;              // 발 밑을 축으로 기울인 각도(도)
        public float ScaleX, ScaleY;    // 발 밑을 기준으로 한 늘임/눌림

        public static BodyPose Identity { get { BodyPose p = new BodyPose(); p.ScaleX = 1f; p.ScaleY = 1f; return p; } }

        public bool IsIdentity
        {
            get
            {
                return Math.Abs(OffX) < 0.002f && Math.Abs(OffY) < 0.002f && Math.Abs(Lift) < 0.002f && Math.Abs(Lean) < 0.05f
                    && Math.Abs(ScaleX - 1f) < 0.002f && Math.Abs(ScaleY - 1f) < 0.002f;
            }
        }
    }

    public static class AttackPose
    {
        public const int StrikeMs = 70, HoldMs = 40;

        /// <summary>몬스터 종류별 동작 스타일. 슬라임/알은 뛰어오르고, 덩치 큰 것(7~10)은 내려찍고, 나머지는 달려들어 문다.</summary>
        public static AttackStyle StyleOf(Entity e)
        {
            if (!e.IsMonster) return AttackStyle.Lunge;
            if (e.IsBoss) return AttackStyle.Slam;
            switch (e.TemplateId)
            {
                case 1: case 3: return AttackStyle.Hop;
                case 7: case 8: case 9: case 10: return AttackStyle.Slam;
                default: return AttackStyle.Lunge;
            }
        }

        /// <summary>타격 한 번의 전체 길이(ms).</summary>
        public static int DurationMs(Entity e)
        {
            AttackStyle st = StyleOf(e);
            int d = st == AttackStyle.Hop ? 440 : (st == AttackStyle.Slam ? 480 : (e.IsMonster ? 360 : 320));
            return e.IsBoss ? d + 120 : d;
        }

        /// <summary>시전 진행도 0~1 (시전 중이 아니면 -1).</summary>
        public static float CastProgress(Entity e, long now)
        {
            if (!e.Casting || e.CastTimeMs == 0) return -1f;
            float c = (now - e.CastStartMs) / (float)e.CastTimeMs;
            return c < 0f ? 0f : (c > 1f ? 1f : c);
        }

        public static float EaseOut(float u) { u = Clamp01(u); return 1f - (1f - u) * (1f - u); }
        public static float EaseInOut(float u) { u = Clamp01(u); return u * u * (3f - 2f * u); }
        private static float Clamp01(float u) { return u < 0f ? 0f : (u > 1f ? 1f : u); }

        /// <summary>
        /// 내지름 정도 f: 시작값 f0 에서 1 까지 StrikeMs 동안 빠르게, HoldMs 동안 유지, 이후 복귀 구간에서 0 으로 내려온다.
        /// t 는 공격 시작 후 경과 ms. 끝나면 0.
        /// </summary>
        public static float Strike(float t, float f0, int totalMs)
        {
            if (t < 0f) return 0f;
            if (t < StrikeMs) return f0 + (1f - f0) * EaseOut(t / StrikeMs);
            if (t < StrikeMs + HoldMs) return 1f;
            float rec = totalMs - StrikeMs - HoldMs;
            if (rec <= 1f) return 0f;
            float u = (t - StrikeMs - HoldMs) / rec;
            if (u >= 1f) return 0f;
            float k = 1f - EaseInOut(u);
            return k;
        }

        /// <summary>지금 엔티티가 어떤 동작 중인지 보고 몸동작을 계산한다.</summary>
        public static BodyPose Compute(Entity e, long now)
        {
            BodyPose p = BodyPose.Identity;
            if (e.Dead) return p;

            float fl = (float)Math.Sqrt(e.FaceX * e.FaceX + e.FaceY * e.FaceY);
            float fx = fl > 0.001f ? e.FaceX / fl : 1f, fy = fl > 0.001f ? e.FaceY / fl : 0f;
            AttackStyle st = StyleOf(e);
            float mag = e.IsBoss ? 1.9f : (st == AttackStyle.Slam ? 1.25f : 1f);
            float lean = e.IsMonster ? 1f : 0.85f;

            float c = CastProgress(e, now);
            if (c >= 0f)
            {
                // 예비동작: 뒤로 젖히며 모은다. 끝 15% 는 튀어나가기 직전의 웅크림.
                float rear = EaseOut(c / 0.85f);
                float coil = c > 0.85f ? (c - 0.85f) / 0.15f : 0f;
                if (st == AttackStyle.Hop)
                {
                    p.ScaleY *= 1f - 0.26f * EaseOut(c); p.ScaleX *= 1f + 0.16f * EaseOut(c);
                }
                else if (st == AttackStyle.Slam)
                {
                    p.Lift += 0.35f * mag * rear; p.ScaleY *= 1f + 0.10f * rear - 0.12f * coil;
                    Push(ref p, fx, fy, -0.25f * mag * rear, -16f * rear * lean);
                }
                else
                {
                    Push(ref p, fx, fy, -0.45f * rear - 0.1f * coil, -11f * rear * lean);
                    p.ScaleY *= 1f - 0.06f * coil; p.ScaleX *= 1f + 0.05f * coil;
                }
                // 힘 주느라 떠는 몸. 모을수록 세게.
                p.OffX += (float)Math.Sin(now / 19.0) * 0.05f * c * mag;
            }
            else
            {
                float t = now - e.AttackAnimMs;
                int total = DurationMs(e);
                if (t >= 0f && t < total)
                {
                    float f0 = e.AttackKind == 1 ? -0.3f : 0f;
                    float f = Strike(t, f0, total);
                    float u = t / total;
                    float skill = e.AttackKind == 1 ? 1.5f : 1f;
                    if (e.AttackKind == 2)
                    {
                        // 발사 반동
                        Push(ref p, fx, fy, -0.2f * f, -6f * f * lean);
                        p.ScaleX *= 1f - 0.03f * f;
                    }
                    else if (st == AttackStyle.Hop)
                    {
                        // 뛰어올라 덮친 뒤 착지에서 퍼진다
                        float rise = u < 0.5f ? (float)Math.Sin(Math.PI * u / 0.5) : 0f;
                        p.Lift += 0.9f * skill * (float)Math.Sin(Math.PI * Math.Min(1f, u / 0.7f));
                        Push(ref p, fx, fy, 0.9f * skill * EaseOut(u / 0.5f) * (1f - EaseInOut((u - 0.5f) / 0.5f)), 0f);
                        p.ScaleY *= 1f + 0.18f * rise;
                        p.ScaleX *= 1f - 0.08f * rise;
                        if (u > 0.5f && u < 0.85f)
                        {
                            float sq = (float)Math.Sin(Math.PI * (u - 0.5f) / 0.35f);
                            p.ScaleY *= 1f - 0.22f * sq; p.ScaleX *= 1f + 0.16f * sq;
                        }
                    }
                    else if (st == AttackStyle.Slam)
                    {
                        // 내려찍기: 짓누르고 땅이 울린다
                        Push(ref p, fx, fy, 0.35f * mag * skill * f, 9f * f * lean);
                        p.ScaleY *= 1f - 0.20f * f; p.ScaleX *= 1f + 0.12f * f;
                        if (t > StrikeMs && t < StrikeMs + 200) p.OffX += (float)Math.Sin(t / 11.0) * 0.07f * mag * (1f - (t - StrikeMs) / 200f);
                    }
                    else
                    {
                        Push(ref p, fx, fy, (e.IsMonster ? 0.55f : 0.5f) * skill * f, 13f * f * lean);
                        p.ScaleX *= 1f + 0.08f * f; p.ScaleY *= 1f - 0.05f * f;
                    }
                }
            }

            // 피격 반동
            float th = now - e.HitFlashMs;
            if (th >= 0f && th < 240f && (e.HitDirX != 0f || e.HitDirY != 0f))
            {
                float k = 1f - th / 240f; k *= k;
                float resist = e.IsBoss ? 0.3f : (e.IsMonster ? 1f : 0.8f);
                float push = 0.30f * k * resist;
                p.OffX += e.HitDirX * push; p.OffY += e.HitDirY * push;
                p.Lean += e.HitDirX * 14f * k * resist;
                p.ScaleY *= 1f - 0.09f * k * resist; p.ScaleX *= 1f + 0.05f * k * resist;
            }
            return p;
        }

        /// <summary>바라보는 방향으로 amount 만큼 밀고, 가로 성분만큼 기울인다.</summary>
        private static void Push(ref BodyPose p, float fx, float fy, float amount, float leanDeg)
        {
            p.OffX += fx * amount; p.OffY += fy * amount;
            p.Lean += leanDeg * fx;
        }

        /// <summary>영웅의 원거리 발사(활 시위/지팡이 구슬) 섬광을 그릴 시점인지. 반환값은 진행도 0~1(아니면 -1).</summary>
        public static float ShotProgress(Entity e, long now)
        {
            if (e.Dead || e.IsMonster || e.AttackKind != 2) return -1f;
            float t = now - e.AttackAnimMs;
            const float dur = 200f;
            if (t < 0f || t >= dur) return -1f;
            return t / dur;
        }

        /// <summary>근접 휘두름 궤적을 그릴 시점인지. 반환값은 진행도 0~1(아니면 -1).</summary>
        public static float SwipeProgress(Entity e, long now)
        {
            if (e.Dead || e.AttackKind != 0) return -1f;
            float t = now - e.AttackAnimMs;
            const float dur = 190f;
            if (t < 0f || t >= dur) return -1f;
            return t / dur;
        }
    }
}
