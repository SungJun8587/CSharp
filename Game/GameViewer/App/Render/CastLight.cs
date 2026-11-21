//***************************************************************************
// CastLight.cs : 영웅 스킬의 빛. 시전 중에는 몸 뒤 후광이 차오르고, 스킬이 나가는 순간 가슴께에서 섬광이 터지며,
//   발동 뒤에는 땅이 영웅 색으로 잠시 밝아진다. 어두울수록(밤/동굴) 땅 조명이 크고 진하다.
//   상태 없이 엔티티의 시각 값만으로 매번 같은 그림을 계산한다.
//***************************************************************************
using System;
using System.Drawing;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public static class CastLight
    {
        /// <summary>직업별 빛 색 (전사 주황, 궁수 연두, 마법사 보라, 창기사 푸른빛).</summary>
        public static Color ColorOf(uint templateId)
        {
            switch (templateId)
            {
                case 1: return Color.FromArgb(255, 176, 80);
                case 2: return Color.FromArgb(150, 232, 120);
                case 3: return Color.FromArgb(188, 140, 255);
                default: return Color.FromArgb(120, 196, 255);
            }
        }

        /// <summary>땅 조명 배율: 맑은 낮 1, 완전히 어두우면 1.7.</summary>
        public static float GroundBoost(float dark) { return 1f + 0.7f * (dark < 0f ? 0f : (dark > 1f ? 1f : dark)); }

        /// <summary>발동 섬광 세기 0..1. 발동 직후 가장 밝고 220ms 안에 사라진다.</summary>
        public static float Flash(Entity e, long now)
        {
            float k = HeroAura.StrikeProgress(e, now);
            if (k < 0f) return 0f;
            float t = k * HeroAura.StrikeMs;
            if (t > 220f) return 0f;
            float u = t / 220f;
            return (1f - u) * (1f - u);
        }

        private static bool Hero(Entity e) { return !e.IsMonster && !e.Dead && e.TemplateId >= 1 && e.TemplateId <= 4; }

        /// <summary>몸보다 먼저: 땅 조명과 몸 뒤 후광.</summary>
        public static void DrawBack(IGfx g, EffectRenderer fx, Entity e, ViewState v, Look look, float bx, float by, long now, float dark)
        {
            if (!Hero(e)) return;
            float c = AttackPose.CastProgress(e, now);
            float k = HeroAura.StrikeProgress(e, now);
            if (c < 0f && k < 0f) return;
            Color col = ColorOf(e.TemplateId);
            float boost = GroundBoost(dark);
            float mid = by - v.Px(look.H * 0.5f);
            if (c >= 0f)
            {
                float pulse = 0.85f + 0.15f * (float)Math.Sin(now / 70.0);
                fx.Glow(g, col, bx, by, v.Px(1.4f + 1.6f * c) * boost, v.Px(0.6f + 0.7f * c) * boost, Math.Min(1f, (0.2f + 0.45f * c) * pulse));
                fx.Glow(g, col, bx, mid, v.Px(look.HalfW) * (0.9f + 0.7f * c), v.Px(look.H) * (0.4f + 0.2f * c), (0.15f + 0.45f * c) * pulse);
            }
            else
            {
                float fade = 1f - k;
                float r = 2.6f + 1.8f * k;
                fx.Glow(g, col, bx, by, v.Px(r) * boost, v.Px(r * 0.42f) * boost, Math.Min(1f, 0.75f * fade * fade * (0.8f + 0.2f * boost)));
                float f = Flash(e, now);
                if (f > 0f) fx.Glow(g, col, bx, mid, v.Px(look.HalfW) * 1.9f, v.Px(look.H) * 0.75f, 0.9f * f);
            }
        }

        /// <summary>몸 위에: 가슴께 섬광(흰 심 + 직업색).</summary>
        public static void DrawFront(IGfx g, EffectRenderer fx, Entity e, ViewState v, Look look, float bx, float by, long now)
        {
            if (!Hero(e)) return;
            float f = Flash(e, now);
            if (f <= 0.01f) return;
            Color col = ColorOf(e.TemplateId);
            float cy = by - v.Px(look.H * 0.52f);
            fx.Glow(g, Color.FromArgb(255, 255, 250), bx, cy, v.Px(look.HalfW) * (0.7f + 0.9f * (1f - f)), v.Px(look.HalfW) * (0.7f + 0.9f * (1f - f)), 0.85f * f);
            fx.Glow(g, col, bx, cy, v.Px(look.HalfW) * 2.4f * (1.2f - 0.4f * f), v.Px(look.HalfW) * 2.0f * (1.2f - 0.4f * f), 0.55f * f);
        }
    }
}
