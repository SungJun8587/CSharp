//***************************************************************************
// MonsterAnimator.cs : 몬스터 한 마리의 동작 상태 -> 뼈대 자세. 어떤 클립을 어느 시각으로 재생할지 고르고, 동작이 바뀔 때 부드럽게 섞는다.
//   우선순위: 사망 > 시전 > 공격 > 피격 > 걷기 > 대기.
//   - 대기/걷기는 반복. 걷기는 걸은 거리에 맞춘다(발이 땅에서 미끄러지지 않게). 공격은 공격 길이(ms)에 클립 전체를 맞춰 늘이거나 줄인다.
//   - 피격은 클립 본래 속도, 사망은 본래 속도로 재생하고 마지막 자세에서 멈춘다.
//   - 시전은 시전 시간(ms)에 클립 전체를 맞춘다(영웅의 마법/스킬).
//   - 클립 이름은 모델 묶음마다 달라서(Walk/Fast_Flying, Punch/Bite_Front/Sword_Attack ...) Names 표에서 첫 번째로 있는 것을 쓴다. 없는 동작은 대기로 대신한다.
// 순수 계산이라 테스트할 수 있다.
//***************************************************************************
using System;

namespace GameViewer.Core.Model3D
{
    public enum AnimRole { Idle, Walk, Attack, Hit, Death, Cast }

    /// <summary>한 순간의 입력. 시각은 모두 같은 시계(ms)다. 아직 없었던 사건은 아주 작은 값(-100000)을 쓴다.</summary>
    public struct AnimInput
    {
        public long NowMs;
        public bool Walking, Dead;
        public float Walked;                 // 지금까지 걸은 거리(월드 단위)
        public long AttackStartMs, HitStartMs, DeadAtMs;
        public int AttackMs;                 // 공격 한 번의 길이
        public bool Casting;                 // 시전 중(영웅 스킬)
        public long CastStartMs;
        public int CastMs;                   // 시전 시간
        public int Seed;                     // 개체마다 다른 대기 위상
    }

    public static class AnimRoles
    {
        public static readonly string[][] Names =
        {
            new[] { "Idle", "Flying_Idle" },                       // Idle
            new[] { "Walk", "Fast_Flying", "Run" },                // Walk
            new[] { "Sword_Attack", "Staff_Attack", "Dagger_Attack", "Bow_Shoot", "Attack", "Punch", "Bite_Front", "Headbutt" },   // Attack
            new[] { "RecieveHit", "HitReact", "HitRecieve" },      // Hit
            new[] { "Death" },                                     // Death
            new[] { "Spell1", "Spell2" },                          // Cast
        };

        /// <summary>역할에 맞는 클립(없으면 null).</summary>
        public static AnimClip Find(GltfModel m, AnimRole role)
        {
            string[] ns = Names[(int)role];
            for (int i = 0; i < ns.Length; ++i) { AnimClip c = m.Clip(ns[i]); if (c != null) return c; }
            return null;
        }
    }

    public sealed class MonsterAnimState
    {
        public const int BlendMs = 140, HitMs = 420;
        public float WalkStride = 1.6f;             // 걷기 클립 한 바퀴에 걷는 거리(월드 단위)

        private readonly GltfModel _m;
        private readonly AnimClip[] _clips = new AnimClip[AnimRoles.Names.Length];
        private readonly Pose _a, _b;
        private AnimRole _role = AnimRole.Idle, _prevRole = AnimRole.Idle;
        private float _time, _prevTime;
        private long _changedMs = -100000;
        private bool _started;

        public MonsterAnimState(GltfModel m)
        {
            _m = m; _a = new Pose(m); _b = new Pose(m);
            for (int i = 0; i < _clips.Length; ++i) _clips[i] = AnimRoles.Find(m, (AnimRole)i);
        }

        public AnimRole Role { get { return _role; } }
        public bool Has(AnimRole r) { return _clips[(int)r] != null; }

        /// <summary>이번 프레임의 동작을 고른다(시간은 아직 안 쓴다).</summary>
        public AnimRole Choose(ref AnimInput s)
        {
            if (s.Dead && _clips[(int)AnimRole.Death] != null) return AnimRole.Death;
            if (!s.Dead && s.Casting && _clips[(int)AnimRole.Cast] != null) return AnimRole.Cast;
            long atk = s.NowMs - s.AttackStartMs;
            if (!s.Dead && _clips[(int)AnimRole.Attack] != null && atk >= 0 && atk < Math.Max(1, s.AttackMs)) return AnimRole.Attack;
            long hit = s.NowMs - s.HitStartMs;
            if (!s.Dead && _clips[(int)AnimRole.Hit] != null && hit >= 0 && hit < HitMs) return AnimRole.Hit;
            if (!s.Dead && s.Walking && _clips[(int)AnimRole.Walk] != null) return AnimRole.Walk;
            return AnimRole.Idle;
        }

        /// <summary>역할 role 에서 재생할 클립 시각(초).</summary>
        public float TimeOf(AnimRole role, ref AnimInput s)
        {
            AnimClip c = _clips[(int)role];
            if (c == null || c.Duration <= 0f) return 0f;
            float d = c.Duration;
            switch (role)
            {
                case AnimRole.Walk: { float ph = s.Walked / Math.Max(0.05f, WalkStride); ph -= (float)Math.Floor(ph); return ph * d; }
                case AnimRole.Attack: { float u = (s.NowMs - s.AttackStartMs) / (float)Math.Max(1, s.AttackMs); return Math.Min(1f, Math.Max(0f, u)) * d; }
                case AnimRole.Cast: { float u = s.CastMs > 0 ? (s.NowMs - s.CastStartMs) / (float)s.CastMs : (s.NowMs - s.CastStartMs) / 1000f / d; return Math.Min(1f, Math.Max(0f, u)) * d; }
                case AnimRole.Hit: return Math.Min(d, (s.NowMs - s.HitStartMs) / 1000f);
                case AnimRole.Death: return s.DeadAtMs <= 0 ? d : Math.Min(d, Math.Max(0, s.NowMs - s.DeadAtMs) / 1000f);
                default: { double sec = (s.NowMs + (long)s.Seed * 137L) / 1000.0; double ph = sec / d; return (float)((ph - Math.Floor(ph)) * d); }
            }
        }

        /// <summary>자세를 계산해 palette(관절 수 x 16)를 채운다. 돌려주는 값은 이번에 고른 동작.</summary>
        public AnimRole Update(ref AnimInput s, float[] palette)
        {
            AnimRole role = Choose(ref s);
            float t = TimeOf(role, ref s);
            if (!_started) { _started = true; _role = role; _changedMs = s.NowMs - BlendMs; }
            else if (role != _role)
            {
                // 이전 동작은 마지막으로 본 시각에서 멈춘 채 섞여 나간다. 공격/피격으로 들어갈 때는 더 빠르게.
                _prevRole = _role; _prevTime = _time; _role = role; _changedMs = s.NowMs;
            }
            _time = t;
            Skeleton.Sample(_m, _clips[(int)role], t, _a);
            int blend = (role == AnimRole.Attack || role == AnimRole.Hit) ? BlendMs / 2 : BlendMs;
            long since = s.NowMs - _changedMs;
            if (since >= 0 && since < blend && _prevRole != role)
            {
                Skeleton.Sample(_m, _clips[(int)_prevRole], _prevTime, _b);
                Skeleton.Blend(_b, _a, since / (float)blend, _a);
            }
            Skeleton.Palette(_m, _a, palette);
            return role;
        }
    }
}
