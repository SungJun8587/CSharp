using System;

namespace GameViewer.Core.Logic
{
    /// <summary>떠오르는 피해/회복 숫자의 등급과 연출 세기. 패킷에 치명타 표시가 없으므로 대상 최대 체력 대비 비율로 가른다.</summary>
    public static class DamageStyle
    {
        /// <summary>0 보통, 1 큼, 2 강타(12%+), 3 치명(25%+).</summary>
        public static int Tier(int value, int maxHp)
        {
            if (maxHp <= 0 || value <= 0) return 0;
            float r = value / (float)maxHp;
            if (r >= 0.25f) return 3;
            if (r >= 0.12f) return 2;
            if (r >= 0.04f) return 1;
            return 0;
        }

        public static string Tag(int tier) { return tier >= 3 ? "치명" : (tier == 2 ? "강타" : null); }

        /// <summary>글자 배율(0.85~1.6). 등급이 오를 때 한 단계씩 확실히 커진다.</summary>
        public static float Scale(int value, int maxHp, int tier)
        {
            float ratio = maxHp > 0 ? value / (float)maxHp : 0.02f;
            return 0.85f + Math.Min(0.55f, ratio * 3f) + tier * 0.05f;
        }

        /// <summary>떠오르는 동안의 좌우 떨림(px 단위 기준값). 강타 이상만 떨고 시간이 갈수록 잦아든다.</summary>
        public static float Shake(int tier, float p)
        {
            if (tier < 2 || p >= 0.3f) return 0f;
            float k = 1f - p / 0.3f;
            return (tier >= 3 ? 7f : 4f) * k * (float)Math.Sin(p * 140f);
        }

        /// <summary>회복 숫자 배율: 대상 최대 체력 대비 비율에 따라 0.9~1.5.</summary>
        public static float HealScale(int value, int maxHp)
        {
            float r = maxHp > 0 ? value / (float)maxHp : 0.05f;
            return 0.9f + Math.Min(0.6f, r * 3f);
        }
    }
}
