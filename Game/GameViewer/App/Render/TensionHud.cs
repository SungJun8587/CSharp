//***************************************************************************
// TensionHud.cs : 긴박감 연출 - 저체력 붉은 맥동, 화면 밖에서 달려오는 적 화살표, 연속 처치 알림.
// 규칙(언제/얼마나)은 Core/Logic/Tension.cs 에 있고 여기서는 그리기만 한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class TensionHud
    {
        private readonly Resources _res;
        private readonly List<Entity> _threats = new List<Entity>(16);
        private readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center };
        public TensionHud(Resources res) { _res = res; }

        public void Draw(Graphics g, ViewerSession s, ViewState v, long now)
        {
            if (s.State != SessionState.InZone) return;
            WorldModel w = s.World;
            if (w.Self != null && !w.Self.Dead) DrawLowHp(g, w.Self, v, now);
            DrawThreats(g, s, v, now);
            DrawStreak(g, w, v, now);
        }

        // ------------------------------------------------------------------ 저체력

        /// <summary>체력이 낮을수록 화면 가장자리가 더 넓고 진하게 붉어지고, 심장 박동에 맞춰 맥동한다.</summary>
        private void DrawLowHp(Graphics g, Entity self, ViewState v, long now)
        {
            float level = Tension.Level(self.Hp, self.MaxHp);
            if (level <= 0f) return;
            float pulse = Tension.Pulse(now, Tension.HeartPeriodMs(level));
            int a = (int)((22f + 105f * level) * (0.3f + 0.7f * pulse));
            if (a < 8) return;
            if (a > 235) a = 235;
            int e = (int)(Math.Min(v.Width, v.Height) * (0.11f + 0.09f * level + 0.04f * pulse));
            Color edge = Color.FromArgb(a, 190, 8, 12), clear = Color.FromArgb(0, 190, 8, 12);
            using (LinearGradientBrush t = new LinearGradientBrush(new Rectangle(0, 0, v.Width, e + 1), edge, clear, LinearGradientMode.Vertical))
                g.FillRectangle(t, 0, 0, v.Width, e);
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, v.Height - e - 1, v.Width, e + 1), clear, edge, LinearGradientMode.Vertical))
                g.FillRectangle(b, 0, v.Height - e, v.Width, e);
            using (LinearGradientBrush l = new LinearGradientBrush(new Rectangle(0, 0, e + 1, v.Height), edge, clear, LinearGradientMode.Horizontal))
                g.FillRectangle(l, 0, 0, e, v.Height);
            using (LinearGradientBrush r = new LinearGradientBrush(new Rectangle(v.Width - e - 1, 0, e + 1, v.Height), clear, edge, LinearGradientMode.Horizontal))
                g.FillRectangle(r, v.Width - e, 0, e, v.Height);
            if (level > 0.55f)
            {
                // 매우 위험: 박동 순간 화면 전체가 살짝 붉게 번쩍인다
                int fa = (int)(16f * (level - 0.55f) / 0.45f * pulse);
                if (fa > 3) g.FillRectangle(_res.Brush(fa, 200, 10, 10), 0, 0, v.Width, v.Height);
                _res.OutlinedText(g, "체력 위험!", _res.Big, Color.FromArgb(Resources.Clamp(150 + (int)(100 * pulse)), 255, 90, 80), v.Width / 2f, v.Height * 0.74f, _center);
            }
        }

        // ------------------------------------------------------------------ 화면 밖 위협

        /// <summary>화면 밖에서 나를 향해 달려오는 적의 방향을 가장자리 화살표로 알린다. 가까울수록 크고 진하다.</summary>
        private void DrawThreats(Graphics g, ViewerSession s, ViewState v, long now)
        {
            WorldModel w = s.World;
            float sx, sy;
            if (w.Self == null || w.Self.Dead || !s.SelfPos(now, out sx, out sy)) return;
            Tension.Threats(w, sx, sy, now, 34f, (v.Width / 2f - 20f) / (float)v.Scale, (v.Height / 2f - 20f) / (float)v.Scale, _threats, 10);
            if (_threats.Count == 0) return;
            float cx = v.Width / 2f, cy = v.Height / 2f;
            float blink = 0.5f + 0.5f * (float)Math.Sin(now / 130.0);
            for (int i = 0; i < _threats.Count; ++i)
            {
                Entity e = _threats[i];
                float x, y; e.PosAt(now, out x, out y);
                float dx = v.ToScreenX(x) - cx, dy = v.ToScreenY(y) - cy;
                float k = Math.Min((cx - 40f) / Math.Max(1f, Math.Abs(dx)), (cy - 60f) / Math.Max(1f, Math.Abs(dy)));
                float ix = cx + dx * k, iy = cy + dy * k;
                float ang = (float)Math.Atan2(dy, dx);
                float dist = (float)Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                float near = 1f - Math.Min(1f, dist / 34f);                     // 가까울수록 1
                float sz = 9f + 11f * near;
                int a = (int)((110f + 110f * near) * (0.6f + 0.4f * blink));
                PointF[] tri = {
                    new PointF(ix + (float)Math.Cos(ang) * sz * 1.3f, iy + (float)Math.Sin(ang) * sz * 1.3f),
                    new PointF(ix + (float)Math.Cos(ang + 2.5) * sz, iy + (float)Math.Sin(ang + 2.5) * sz),
                    new PointF(ix + (float)Math.Cos(ang - 2.5) * sz, iy + (float)Math.Sin(ang - 2.5) * sz) };
                g.FillPolygon(_res.Brush(Resources.Clamp(a + 30), 255, 90, 70), tri);
                g.DrawPolygon(_res.Pen(Resources.Clamp(a + 60), 255, 235, 200, 2f), tri);
            }
        }

        // ------------------------------------------------------------------ 연속 처치

        /// <summary>연속 처치 칭호(커졌다 줄어드는 알림)와, 이어지는 동안 남은 시간 막대.</summary>
        private void DrawStreak(Graphics g, WorldModel w, ViewState v, long now)
        {
            float cx = v.Width / 2f, y0 = v.Height * 0.17f;
            for (int i = 0; i < w.Effects.Count; ++i)
            {
                ViewEffect fx = w.Effects[i];
                if (fx.Kind != EffectKind.Streak || fx.Expired(now)) continue;
                float p = fx.Progress(now);
                int tier = KillStreak.Tier(fx.Value);
                float pop = 1f + (0.55f + 0.2f * tier) * Math.Max(0f, 1f - p * 7f);
                float a = p > 0.7f ? (1f - p) / 0.3f : 1f;
                Color c = tier == 0 ? Color.FromArgb(255, 236, 160) : (tier == 1 ? Color.FromArgb(255, 190, 90) : (tier == 2 ? Color.FromArgb(255, 120, 70) : Color.FromArgb(255, 80, 200)));
                float rise = p * 14f;
                System.Drawing.Drawing2D.GraphicsState gs = g.Save();
                g.TranslateTransform(cx, y0 - rise);
                g.ScaleTransform(pop, pop);
                _res.OutlinedText(g, fx.Text ?? "", _res.Huge, Color.FromArgb(Resources.Clamp((int)(255 * a)), c), 0f, 0f, _center);
                g.Restore(gs);
            }
            int n = w.Streak.Active(now);
            if (n >= 2)
            {
                float rem = w.Streak.Remaining(now);
                float bw = 150f, bx = cx - bw / 2f, by = y0 + 46f;
                _res.OutlinedText(g, "연속 x" + n, _res.Bold, Color.FromArgb(255, 226, 150), cx, by - 22f, _center);
                g.FillRectangle(_res.Brush(120, 10, 10, 10), bx, by, bw, 5f);
                g.FillRectangle(_res.Brush(230, rem > 0.3f ? 255 : 255, rem > 0.3f ? 200 : 90, 60), bx, by, bw * rem, 5f);
            }
        }
    }
}
