//***************************************************************************
// ViewEffect.cs : 화면 연출 데이터(렌더러가 그리기만 한다). 월드 모델이 패킷에서 만들고 시간이 지나면 지운다.
// 렌더러는 이 데이터만 보면 되므로 WinForms/Unity 어디서든 같은 방식으로 그릴 수 있다.
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.World
{
    public enum EffectKind
    {
        Damage,         // 떠오르는 피해 숫자 (Value, Variant: 0 일반 / 1 지속피해 / 2 내가 받은 피해 / 3 내가 준 피해)
        Heal,           // 떠오르는 회복 숫자
        Ring,           // 범위 스킬 폭발 링 (X,Y 중심, Radius)
        Telegraph,      // 시전 중 범위 예고 (X,Y 중심, Radius, 시전 끝나면 사라짐)
        Projectile,     // 날아가는 투사체 (X,Y -> X2,Y2, TargetId 가 있으면 대상 추적)
        Slash,          // 근접 베기 (EntityId 시전자 -> X,Y)
        Tracer,         // 원거리 기본 공격 궤적 (EntityId -> X,Y)
        Spark,          // 피격 불꽃 (X,Y)
        DeathPuff,      // 사망 연기 (X,Y)
        RespawnRing,    // 부활 링 (X,Y)
        Text,           // 상태 효과 등 짧은 문구 (Text)
        Banner,         // 화면 중앙 알림 (보스 페이즈 돌입 등. Text, EntityId, Value=페이즈, Variant 1 = 경고(붉은 경보))
        Streak,         // 연속 처치 알림 (Value=연속 횟수, Text=칭호)
    }

    public sealed class ViewEffect
    {
        public EffectKind Kind;
        public long StartMs;
        public int DurMs;
        public float X, Y, X2, Y2, Radius;
        public ulong EntityId, TargetId;
        public uint SkillId, ProjectileId;
        public int Value;
        public int Variant;
        public string Text;

        public bool Expired(long now) { return now >= StartMs + DurMs; }

        /// <summary>0..1 진행도.</summary>
        public float Progress(long now)
        {
            if (DurMs <= 0) return 1f;
            float t = (now - StartMs) / (float)DurMs;
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }
    }

    public sealed class LogLine
    {
        public long Ms;
        public int Level;       // 0 정보, 1 경고(거부/보정), 2 오류
        public string Text;
    }
}
