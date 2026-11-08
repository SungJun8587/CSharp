//***************************************************************************
// CombatHud.cs : 전투 중 화면에 덮는 정보 - 보스 체력바(페이즈 눈금/격노/시전), 대상 창, 파티 창, 위험 지역 경고,
// 페이즈 알림 배너, 화면 밖 보스 방향 표시. 서버 패킷에 없는 보스 정보는 보스 데이터(Boss/BossPhase)와 체력 비율로 추정한 것이다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class CombatHud
    {
        private readonly Resources _res;
        public CombatHud(Resources res) { _res = res; }

        /// <summary>화면에 크게 보여줄 보스. 선택한 보스가 우선, 없으면 내게 가장 가까운 살아 있는 보스(45 이내).</summary>
        public static Entity ActiveBoss(ViewerSession s, ViewState v, long now)
        {
            WorldModel w = s.World;
            Entity sel = w.Find(v.SelectedId);
            if (sel != null && sel.IsBoss && !sel.Dead) return sel;
            float sx = 0, sy = 0; bool has = s.SelfPos(now, out sx, out sy);
            Entity best = null; float bd = 45f * 45f;
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsBoss || e.Dead) continue;
                float x, y; e.PosAt(now, out x, out y);
                float d = has ? (x - sx) * (x - sx) + (y - sy) * (y - sy) : 0f;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        public void Draw(Graphics g, ViewerSession s, ViewState v, long now)
        {
            if (s.State != SessionState.InZone) return;
            Entity boss = ActiveBoss(s, v, now);
            if (boss != null) DrawBossBar(g, s, boss, v, now);
            DrawTargetFrame(g, s, v, now);
            DrawPartyFrames(g, s, v, now);
            DrawOffscreenBoss(g, s, v, now);
            DrawDanger(g, s, v, now, boss != null ? 96 : 20);
            DrawBanners(g, s, v, now);
        }

        // ------------------------------------------------------------------ 보스 체력바

        private void DrawBossBar(Graphics g, ViewerSession s, Entity boss, ViewState v, long now)
        {
            float w = Math.Min(620f, v.Width - 420f), h = 20f;
            float x = (v.Width - w) / 2, y = 34f;
            float ratio = boss.MaxHp > 0 ? Math.Max(0f, Math.Min(1f, boss.Hp / (float)boss.MaxHp)) : 0f;
            float ghost = boss.MaxHp > 0 ? Math.Max(ratio, Math.Min(1f, boss.GhostHp / (float)boss.MaxHp)) : ratio;
            bool enraged = boss.IsEnraged;
            int phases = boss.Boss != null ? Math.Max(1, boss.Boss.Phases.Count) : 1;

            // 이름 줄
            string name = "★ " + s.World.NameOf(boss);
            _res.OutlinedText(g, name, _res.Big, Color.FromArgb(255, 220, 110), x, y - 28, _left);
            if (phases > 1)
            {
                string ph = boss.BossPhase + " / " + phases + " 페이즈";
                _res.OutlinedText(g, ph, _res.Bold, Color.FromArgb(255, 245, 220), x + w, y - 24, _right);
            }
            if (enraged)
            {
                float pulse = 0.6f + 0.4f * (float)Math.Sin(now / 130.0);
                g.FillRectangle(_res.Brush((int)(210 * pulse), 220, 30, 24), x + w - 150, y - 26, 62, 20);
                g.DrawString("격노", _res.Bold, _res.Brush(Color.White), new RectangleF(x + w - 150, y - 26, 62, 20), _res.Center);
            }

            // 바 본체
            using (GraphicsPath back = P.RoundPath(x - 3, y - 3, w + 6, h + 6, 5))
            {
                g.FillPath(_res.Brush(215, 14, 12, 14), back);
                g.DrawPath(_res.Pen(enraged ? Color.FromArgb(240, 230, 60, 50) : Color.FromArgb(200, 214, 188, 120), 2f), back);
            }
            if (ghost > ratio) g.FillRectangle(_res.Brush(235, 255, 250, 235), x + w * ratio, y, w * (ghost - ratio), h);
            Color c1 = enraged ? Color.FromArgb(240, 70, 50) : Color.FromArgb(214, 70, 60);
            Color c2 = enraged ? Color.FromArgb(120, 20, 20) : Color.FromArgb(116, 24, 30);
            if (ratio > 0.001f)
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, w * ratio + 1, h + 1), c1, c2, LinearGradientMode.Vertical))
                    g.FillRectangle(b, x, y, w * ratio, h);
            g.FillRectangle(_res.Brush(50, 255, 255, 255), x, y, w * ratio, 5);
            WorldRenderer.DrawPhaseTicks(g, boss, x, y, w, h);
            string txt = boss.Hp.ToString("N0") + " / " + boss.MaxHp.ToString("N0") + "  (" + (ratio * 100f).ToString("0") + "%)";
            _res.OutlinedText(g, txt, _res.Small, Color.White, x + w / 2, y + 2, _center2);

            // 시전 중이면 어떤 스킬인지, 몇 초 남았는지
            if (boss.Casting && boss.CastTimeMs > 0)
            {
                float p = Math.Max(0f, Math.Min(1f, (now - boss.CastStartMs) / (float)boss.CastTimeMs));
                float cy = y + h + 10, cw = w * 0.62f, cx = x + (w - cw) / 2;
                g.FillRectangle(_res.Brush(215, 14, 12, 14), cx - 2, cy - 2, cw + 4, 14);
                g.FillRectangle(_res.Brush(Color.FromArgb(255, 150, 60)), cx, cy, cw * p, 10);
                g.FillRectangle(_res.Brush(60, 255, 255, 255), cx, cy, cw * p, 3);
                float left = Math.Max(0f, (boss.CastTimeMs - (now - boss.CastStartMs)) / 1000f);
                _res.OutlinedText(g, s.Data.SkillName(boss.CastSkillId) + "  " + left.ToString("0.0") + "s", _res.Normal, Color.FromArgb(255, 226, 190), x + w / 2, cy + 14, _center2);
            }
        }

        // ------------------------------------------------------------------ 대상 창 / 파티 창

        private void DrawTargetFrame(Graphics g, ViewerSession s, ViewState v, long now)
        {
            Entity t = s.World.Find(v.SelectedId);
            if (t == null) return;
            float x = 10, y = 150, w = 250, h = 62;
            Panel(g, x, y, w, h);
            string name = s.World.NameOf(t);
            Color nc = t.IsBoss ? Color.FromArgb(255, 214, 90) : (t.IsMonster ? Color.FromArgb(255, 200, 200) : Color.FromArgb(190, 225, 255));
            g.DrawString((t.IsBoss ? "★ " : "") + name + (t.Dead ? " (사망)" : ""), _res.Bold, _res.Brush(nc), x + 8, y + 4);
            float ratio = t.MaxHp > 0 ? Math.Max(0f, Math.Min(1f, t.Hp / (float)t.MaxHp)) : 0f;
            float ghost = t.MaxHp > 0 ? Math.Max(ratio, Math.Min(1f, t.GhostHp / (float)t.MaxHp)) : ratio;
            g.FillRectangle(_res.Brush(Color.FromArgb(50, 20, 20)), x + 8, y + 26, w - 16, 12);
            if (ghost > ratio) g.FillRectangle(_res.Brush(230, 255, 255, 255), x + 8 + (w - 16) * ratio, y + 26, (w - 16) * (ghost - ratio), 12);
            g.FillRectangle(_res.Brush(WorldRenderer.HpColor(ratio)), x + 8, y + 26, (w - 16) * ratio, 12);
            g.FillRectangle(_res.Brush(50, 255, 255, 255), x + 8, y + 26, (w - 16) * ratio, 3);
            _res.OutlinedText(g, t.Hp + " / " + t.MaxHp, _res.Small, Color.White, x + w / 2, y + 26, _center2);
            float sx, sy;
            if (s.SelfPos(now, out sx, out sy))
            {
                float tx, ty; t.PosAt(now, out tx, out ty);
                float d = (float)Math.Sqrt((tx - sx) * (tx - sx) + (ty - sy) * (ty - sy));
                g.DrawString("거리 " + d.ToString("0.0"), _res.Small, _res.Brush(Color.FromArgb(190, 210, 190)), x + 8, y + 42);
            }
            float ix = x + 80;
            foreach (KeyValuePair<uint, StatusInstance> st in t.Statuses)
            {
                if (ix > x + w - 40) break;
                float left = Math.Max(0f, (st.Value.ExpireMs - now) / 1000f);
                string label = s.Data.StatusName(st.Key) + " " + left.ToString("0.0");
                g.DrawString(label, _res.Small, _res.Brush(Color.FromArgb(180, 220, 255)), ix, y + 42);
                ix += g.MeasureString(label, _res.Small).Width + 4;
            }
        }

        private void DrawPartyFrames(Graphics g, ViewerSession s, ViewState v, long now)
        {
            WorldModel w = s.World;
            List<Entity> list = new List<Entity>();
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
                if (kv.Value.IsPlayer && kv.Key != w.SelfId) list.Add(kv.Value);
            if (list.Count == 0) return;
            list.Sort(delegate (Entity a, Entity b) { return a.Id.CompareTo(b.Id); });
            float x = 10, y = v.ShowLog ? 224 : 224;
            int n = Math.Min(5, list.Count);
            Panel(g, x, y, 250, 8 + n * 24);
            for (int i = 0; i < n; ++i)
            {
                Entity e = list[i];
                float yy = y + 4 + i * 24;
                float ratio = e.MaxHp > 0 ? Math.Max(0f, Math.Min(1f, e.Hp / (float)e.MaxHp)) : 0f;
                Color nc = e.Dead ? Color.FromArgb(140, 140, 140) : Color.FromArgb(190, 225, 255);
                g.DrawString(w.NameOf(e), _res.Small, _res.Brush(nc), x + 8, yy);
                g.FillRectangle(_res.Brush(Color.FromArgb(50, 20, 20)), x + 110, yy + 3, 130, 10);
                g.FillRectangle(_res.Brush(e.Dead ? Color.FromArgb(90, 90, 90) : WorldRenderer.HpColor(ratio)), x + 110, yy + 3, 130 * ratio, 10);
                if (e.Casting) g.FillRectangle(_res.Brush(Color.FromArgb(190, 120, 255)), x + 110, yy + 14, 130 * Math.Max(0f, Math.Min(1f, (now - e.CastStartMs) / (float)Math.Max(1u, e.CastTimeMs))), 3);
            }
        }

        // ------------------------------------------------------------------ 화면 밖 보스 / 위험 지역 / 배너

        private void DrawOffscreenBoss(Graphics g, ViewerSession s, ViewState v, long now)
        {
            float sx, sy;
            if (!s.SelfPos(now, out sx, out sy)) return;
            foreach (KeyValuePair<ulong, Entity> kv in s.World.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsBoss || e.Dead) continue;
                float x, y; e.PosAt(now, out x, out y);
                float px = v.ToScreenX(x), py = v.ToScreenY(y);
                if (px > 30 && px < v.Width - 30 && py > 30 && py < v.Height - 30) continue;
                float cx = v.Width / 2f, cy = v.Height / 2f;
                float dx = px - cx, dy = py - cy;
                float k = Math.Min((v.Width / 2f - 46) / Math.Max(1f, Math.Abs(dx)), (v.Height / 2f - 70) / Math.Max(1f, Math.Abs(dy)));
                float ix = cx + dx * k, iy = cy + dy * k;
                float ang = (float)Math.Atan2(dy, dx);
                PointF[] tri = {
                    new PointF(ix + (float)Math.Cos(ang) * 16, iy + (float)Math.Sin(ang) * 16),
                    new PointF(ix + (float)Math.Cos(ang + 2.5) * 12, iy + (float)Math.Sin(ang + 2.5) * 12),
                    new PointF(ix + (float)Math.Cos(ang - 2.5) * 12, iy + (float)Math.Sin(ang - 2.5) * 12) };
                int a = 170 + (int)(70 * Math.Sin(now / 200.0));
                g.FillPolygon(_res.Brush(Color.FromArgb(Math.Min(255, a), 255, 200, 70)), tri);
                g.DrawPolygon(_res.Pen(230, 60, 40, 10, 1.5f), tri);
                float d = (float)Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                _res.OutlinedText(g, "★ " + s.World.NameOf(e) + " " + d.ToString("0") + "m", _res.Small, Color.FromArgb(255, 226, 140), ix - (float)Math.Cos(ang) * 40, iy - (float)Math.Sin(ang) * 22 - 6, _center2);
            }
        }

        /// <summary>내가 보스(또는 누군가)의 시전 예고 지역 안에 있으면 화면 가장자리를 붉게 깜빡이고 경고한다.</summary>
        private void DrawDanger(Graphics g, ViewerSession s, ViewState v, long now, float textY)
        {
            WorldModel w = s.World;
            if (w.Self == null || w.Self.Dead) return;
            float sx, sy;
            if (!s.SelfPos(now, out sx, out sy)) return;
            bool danger = false;
            for (int i = 0; i < w.Effects.Count; ++i)
            {
                ViewEffect fx = w.Effects[i];
                if (fx.Kind != EffectKind.Telegraph || fx.EntityId == w.SelfId) continue;
                float dx = sx - fx.X, dy = sy - fx.Y;
                if (dx * dx + dy * dy <= fx.Radius * fx.Radius) { danger = true; break; }
            }
            if (danger)
            {
                int a = 60 + (int)(55 * (0.5 + 0.5 * Math.Sin(now / 90.0)));
                int e = 70;
                using (LinearGradientBrush t = new LinearGradientBrush(new Rectangle(0, 0, v.Width, e + 1), Color.FromArgb(a * 2, 230, 20, 20), Color.FromArgb(0, 230, 20, 20), LinearGradientMode.Vertical))
                    g.FillRectangle(t, 0, 0, v.Width, e);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, v.Height - e - 1, v.Width, e + 1), Color.FromArgb(0, 230, 20, 20), Color.FromArgb(a * 2, 230, 20, 20), LinearGradientMode.Vertical))
                    g.FillRectangle(b, 0, v.Height - e, v.Width, e);
                _res.OutlinedText(g, "위험! 범위 밖으로 벗어나세요", _res.Big, Color.FromArgb(255, 120, 100), v.Width / 2f, textY + 30, _center2);
            }
        }

        private void DrawBanners(Graphics g, ViewerSession s, ViewState v, long now)
        {
            WorldModel w = s.World;
            for (int i = 0; i < w.Effects.Count; ++i)
            {
                ViewEffect fx = w.Effects[i];
                if (fx.Kind != EffectKind.Banner) continue;
                float p = fx.Progress(now);
                float inT = Math.Min(1f, p / 0.12f), outT = p > 0.78f ? (1f - p) / 0.22f : 1f;
                float a = Math.Max(0f, Math.Min(1f, Math.Min(inT, outT)));
                float cy = v.Height * 0.30f;
                float hh = 64f * (0.4f + 0.6f * inT);
                bool warn = fx.Variant == 1;
                if (warn)
                {
                    // 경보: 붉은 띠가 깜빡이고 위아래로 경고 줄무늬
                    float blink = 0.55f + 0.45f * (float)Math.Sin(now / 70.0);
                    g.FillRectangle(_res.Brush((int)(190 * a * blink), 150, 6, 6), 0, cy - hh / 2, v.Width, hh);
                    g.DrawLine(_res.Pen((int)(230 * a), 255, 90, 70, 3f), 0, cy - hh / 2, v.Width, cy - hh / 2);
                    g.DrawLine(_res.Pen((int)(230 * a), 255, 90, 70, 3f), 0, cy + hh / 2, v.Width, cy + hh / 2);
                    for (int sx = -40 + (int)((now / 12) % 40); sx < v.Width; sx += 40)
                    {
                        g.DrawLine(_res.Pen((int)(120 * a), 255, 200, 60, 6f), sx, cy - hh / 2 - 8, sx + 14, cy - hh / 2 - 2);
                        g.DrawLine(_res.Pen((int)(120 * a), 255, 200, 60, 6f), sx, cy + hh / 2 + 2, sx + 14, cy + hh / 2 + 8);
                    }
                    _res.OutlinedText(g, "\u26A0 " + (fx.Text ?? ""), _res.Huge, Color.FromArgb(Resources.Clamp((int)(255 * a)), 255, 236, 200), v.Width / 2f, cy - 2, _center2);
                    continue;
                }
                g.FillRectangle(_res.Brush((int)(150 * a), 20, 4, 4), 0, cy - hh / 2, v.Width, hh);
                g.DrawLine(_res.Pen((int)(200 * a), 255, 210, 120, 2f), 0, cy - hh / 2, v.Width, cy - hh / 2);
                g.DrawLine(_res.Pen((int)(200 * a), 255, 210, 120, 2f), 0, cy + hh / 2, v.Width, cy + hh / 2);
                _res.OutlinedText(g, fx.Text ?? "", _res.Huge, Color.FromArgb(Resources.Clamp((int)(255 * a)), 255, 232, 170), v.Width / 2f, cy - 2, _center2);
            }
        }

        private void Panel(Graphics g, float x, float y, float w, float h)
        {
            g.FillRectangle(_res.Brush(150, 10, 16, 12), x, y, w, h);
            g.DrawRectangle(_res.Pen(120, 200, 220, 190, 1f), x, y, w, h);
        }

        private readonly StringFormat _left = new StringFormat();
        private readonly StringFormat _right = new StringFormat { Alignment = StringAlignment.Far };
        private readonly StringFormat _center2 = new StringFormat { Alignment = StringAlignment.Center };
    }
}
