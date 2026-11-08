//***************************************************************************
// HudRenderer.cs : 화면 위에 덮는 정보(상태/체력/스킬바/미니맵/로그/도움말).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class HudRenderer
    {
        private readonly Resources _res;
        private readonly StringFormat _fmtLeft = new StringFormat();
        private readonly CombatHud _combat;
        public HudRenderer(Resources res) { _res = res; _combat = new CombatHud(res); }

        /// <summary>미니맵 영역(화면 좌표). 클릭 이동에도 쓴다.</summary>
        public static RectangleF MiniMapRect(GameData d, ViewState v)
        {
            float size = 170;
            float ww = d.WorldMaxX - d.WorldMinX, wh = d.WorldMaxY - d.WorldMinY;
            float k = size / Math.Max(ww, wh);
            return new RectangleF(v.Width - size - 12, 12, ww * k, wh * k);
        }

        public void Draw(Graphics g, ViewerSession s, AutoPilot pilot, ViewState v, long now)
        {
            DrawInfoPanel(g, s, pilot, v, now);
            DrawMiniMap(g, s, v, now);
            if (s.State == SessionState.InZone && s.World.Self != null)
            {
                DrawSelfBar(g, s, v, now);
                DrawSkillBar(g, s, v, now);
            }
            _combat.Draw(g, s, v, now);
            if (v.ShowLog) DrawLog(g, s, v, now);
            if (v.ShowHelp) DrawHelp(g, v);
            DrawStateOverlay(g, s, v);
            if (v.HeroSelectOpen) DrawHeroSelect(g, s, v);
        }

        // ------------------------------------------------------------------ 영웅 선택 창 (H)

        private const float HeroRowH = 74f, HeroPanelW = 520f;

        /// <summary>영웅 선택 창의 줄 i 의 화면 영역. 마우스 클릭 판정에도 쓴다.</summary>
        public static RectangleF HeroRowRect(ViewState v, int count, int i)
        {
            float h = 64 + count * HeroRowH + 34;
            float x = (v.Width - HeroPanelW) / 2, y = Math.Max(10, (v.Height - h) / 2);
            return new RectangleF(x + 12, y + 52 + i * HeroRowH, HeroPanelW - 24, HeroRowH - 6);
        }

        private void DrawHeroSelect(Graphics g, ViewerSession s, ViewState v)
        {
            List<uint> ids = s.Data.HeroIds();
            if (Backdrop.Draw(g, v.Width, v.Height)) g.FillRectangle(_res.Brush(70, 0, 0, 0), 0, 0, v.Width, v.Height);       // 배경 이미지가 있으면 살짝만 어둡게
            else g.FillRectangle(_res.Brush(150, 0, 0, 0), 0, 0, v.Width, v.Height);
            float h = 64 + ids.Count * HeroRowH + 34;
            float x = (v.Width - HeroPanelW) / 2, y = Math.Max(10, (v.Height - h) / 2);
            g.FillRectangle(_res.Brush(235, 14, 20, 17), x, y, HeroPanelW, h);
            g.DrawRectangle(_res.Pen(200, 200, 220, 190, 1.5f), x, y, HeroPanelW, h);
            g.DrawString("영웅 선택", _res.Big, _res.Brush(Color.FromArgb(255, 235, 150)), x + 16, y + 12);
            if (ids.Count == 0)
            {
                g.DrawString("GameData 에 Hero 시트가 없습니다", _res.Normal, _res.Brush(Color.FromArgb(255, 160, 150)), x + 16, y + 56);
                return;
            }
            int sel = Math.Max(0, Math.Min(ids.Count - 1, v.HeroSelectIndex));
            for (int i = 0; i < ids.Count; ++i)
            {
                HeroRow r = s.Data.Hero(ids[i]);
                RectangleF rc = HeroRowRect(v, ids.Count, i);
                bool cur = ids[i] == s.HeroId;
                if (i == sel) g.FillRectangle(_res.Brush(90, 120, 180, 130), rc.X, rc.Y, rc.Width, rc.Height);
                g.DrawRectangle(_res.Pen(i == sel ? 220 : 90, 200, 220, 190, 1f), rc.X, rc.Y, rc.Width, rc.Height);
                Color accent = WorldRenderer.HeroColor(ids[i]);
                g.FillEllipse(_res.Brush(accent), rc.X + 10, rc.Y + 12, 36, 36);
                g.DrawString((i + 1).ToString(), _res.Bold, _res.Brush(Color.White), new RectangleF(rc.X + 10, rc.Y + 12, 36, 36), _res.Center);
                g.DrawString(r.Name + (cur ? "  (현재)" : ""), _res.Bold, _res.Brush(Color.FromArgb(255, 245, 210)), rc.X + 56, rc.Y + 4);
                g.DrawString("체력 " + r.MaxHp + "  공격 " + r.Attack + "  방어 " + r.Defense + "  사거리 " + r.AttackRange.ToString("0.#") + "  속도 " + r.Speed.ToString("0.#"),
                    _res.Small, _res.Brush(Color.FromArgb(215, 225, 215)), rc.X + 56, rc.Y + 26);
                List<uint> sk = s.Data.SkillsOf(ids[i]);
                string names = "";
                for (int k = 0; k < sk.Count && k < 6; ++k) names += (k > 0 ? ", " : "") + s.Data.SkillName(sk[k]);
                g.DrawString(sk.Count > 0 ? "스킬: " + names : "스킬 없음", _res.Small, _res.Brush(Color.FromArgb(180, 200, 255)), rc.X + 56, rc.Y + 44);
            }
            g.DrawString("↑↓ / 1~9 선택     Enter 또는 클릭: 이 영웅으로 다시 접속     H / Esc: 닫기", _res.Small, _res.Brush(Color.FromArgb(200, 210, 200)), x + 16, y + h - 24);
        }

        private void Panel(Graphics g, float x, float y, float w, float h)
        {
            g.FillRectangle(_res.Brush(150, 10, 16, 12), x, y, w, h);
            g.DrawRectangle(_res.Pen(120, 200, 220, 190, 1f), x, y, w, h);
        }

        private void DrawInfoPanel(Graphics g, ViewerSession s, AutoPilot pilot, ViewState v, long now)
        {
            WorldModel w = s.World;
            int players = 0, monsters = 0, dead = 0;
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                if (kv.Value.IsMonster) ++monsters; else ++players;
                if (kv.Value.Dead) ++dead;
            }
            float x = 10, y = 10;
            List<string> lines = new List<string>();
            lines.Add(s.StatusText);
            if (w.Self != null)
            {
                float px, py; w.Self.PosAt(now, out px, out py);
                lines.Add("영웅 " + s.Data.HeroName(s.HeroId) + "  위치 (" + px.ToString("0.0") + ", " + py.ToString("0.0") + ")");
            }
            lines.Add("시야 내 플레이어 " + players + " / 몬스터 " + monsters + (dead > 0 ? " (시체 " + dead + ")" : ""));
            lines.Add("처치 " + w.KillsBySelf + "  사망 " + w.SelfDeaths + "  준 피해 " + w.DamageDealtBySelf + "  받은 피해 " + w.DamageTakenBySelf);
            lines.Add("자동 전투: " + (pilot.Mode == PilotMode.Auto ? "ON" : (pilot.Mode == PilotMode.Chase ? "추적" : "OFF")) + (pilot.Note.Length > 0 ? "  [" + pilot.Note + "]" : ""));
            lines.Add("FPS " + v.Fps.ToString("0") + "  수신 " + (s.Net.BytesIn / 1024) + "KB  패킷 " + w.PacketCount + (w.UnknownPacketCount + w.MalformedPacketCount > 0 ? "  (미상 " + w.UnknownPacketCount + "/불량 " + w.MalformedPacketCount + ")" : ""));

            float h = 8 + lines.Count * 17;
            float wd = 0;
            for (int i = 0; i < lines.Count; ++i) wd = Math.Max(wd, g.MeasureString(lines[i], _res.Normal).Width);
            Panel(g, x, y, wd + 16, h);
            for (int i = 0; i < lines.Count; ++i)
                g.DrawString(lines[i], _res.Normal, _res.Brush(i == 0 ? Color.FromArgb(255, 235, 150) : Color.FromArgb(235, 240, 235)), x + 8, y + 4 + i * 17);
        }

        private void DrawSelfBar(Graphics g, ViewerSession s, ViewState v, long now)
        {
            Entity e = s.World.Self;
            float w = 360, h = 20, x = (v.Width - w) / 2, y = v.Height - 112;
            Panel(g, x - 4, y - 4, w + 8, h + 8);
            float ratio = e.MaxHp > 0 ? Math.Max(0f, Math.Min(1f, e.Hp / (float)e.MaxHp)) : 0f;
            g.FillRectangle(_res.Brush(Color.FromArgb(50, 20, 20)), x, y, w, h);
            g.FillRectangle(_res.Brush(WorldRenderer.HpColor(ratio)), x, y, w * ratio, h);
            g.FillRectangle(_res.Brush(50, 255, 255, 255), x, y, w * ratio, 5);
            if (e.Casting && e.CastTimeMs > 0 && !e.Dead)
            {
                float cp = Math.Max(0f, Math.Min(1f, (now - e.CastStartMs) / (float)e.CastTimeMs));
                g.FillRectangle(_res.Brush(200, 20, 20, 20), x, y - 22, w, 12);
                g.FillRectangle(_res.Brush(Color.FromArgb(190, 120, 255)), x + 1, y - 21, (w - 2) * cp, 10);
                g.DrawString(s.Data.SkillName(e.CastSkillId), _res.Small, _res.Brush(Color.White), new RectangleF(x, y - 23, w, 12), _res.Center);
            }
            string text = e.Dead ? "사망 - 부활 대기" : e.Hp + " / " + e.MaxHp;
            g.DrawString(text, _res.Bold, _res.Brush(Color.White), new RectangleF(x, y, w, h), _res.Center);
        }

        private void DrawSkillBar(Graphics g, ViewerSession s, ViewState v, long now)
        {
            List<uint> skills = s.MySkills;
            int n = Math.Min(9, skills.Count);
            float slot = 58, gap = 6;
            float total = (n + 1) * slot + n * gap;
            float x0 = (v.Width - total) / 2, y = v.Height - 78;

            // 선택한 대상까지의 거리: 사거리 밖인 스킬은 슬롯을 붉게 표시한다
            float dist = -1f;
            Entity tgt = s.World.Find(v.SelectedId);
            float px, py;
            if (tgt != null && !tgt.Dead && s.SelfPos(now, out px, out py)) { float tx, ty; tgt.PosAt(now, out tx, out ty); dist = (float)Math.Sqrt((tx - px) * (tx - px) + (ty - py) * (ty - py)); }
            HeroRow hero = s.Data.Hero(s.HeroId);
            DrawSlot(g, x0, y, slot, "Q", "기본", 0, 0, hero != null ? "사거리 " + hero.AttackRange.ToString("0.#") : "", false, dist >= 0 && hero != null && dist > hero.AttackRange + 0.3);
            for (int i = 0; i < n; ++i)
            {
                SkillRow sk = s.Data.Skill(skills[i]);
                long left = s.SkillCooldownLeftMs(skills[i], now);
                long cd = sk != null ? sk.CooldownMs : 1;
                bool casting = s.World.Self.Casting && s.World.Self.CastSkillId == skills[i];
                string sub = sk == null ? "" : SkillSubLabel(sk);
                bool far = dist >= 0 && sk != null && !sk.IsSelf && !(sk.IsAlly && (tgt == null || !tgt.IsPlayer)) && !(sk.IsArea && sk.Range <= 1.5) && dist > sk.Range;
                DrawSlot(g, x0 + (i + 1) * (slot + gap), y, slot, (i + 1).ToString(), s.Data.SkillName(skills[i]), left, cd, sub, casting, far);
            }
        }

        /// <summary>슬롯 아래 줄: 모양과 사거리(Self 는 효과 반경).</summary>
        private static string SkillSubLabel(SkillRow sk)
        {
            if (sk.IsSelf) return "자신 " + sk.Radius.ToString("0.#");
            string r = "R" + sk.Range.ToString("0.#");
            if (sk.IsArea) return "범위 " + r;
            if (sk.IsCone) return "부채꼴 " + r;
            if (sk.IsLine) return "직선 " + r;
            return sk.IsAlly ? "아군 " + r : r;
        }

        private void DrawSlot(Graphics g, float x, float y, float size, string key, string name, long leftMs, long cdMs, string sub, bool active, bool outOfRange)
        {
            using (GraphicsPath p = P.RoundPath(x, y, size, size, 6))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, size, size + 1), Color.FromArgb(215, 40, 48, 44), Color.FromArgb(215, 16, 20, 18), LinearGradientMode.Vertical))
                    g.FillPath(b, p);
                if (outOfRange) g.FillPath(_res.Brush(70, 220, 40, 30), p);
                g.DrawPath(_res.Pen(active ? Color.FromArgb(255, 220, 120) : (outOfRange ? Color.FromArgb(190, 220, 90, 80) : Color.FromArgb(150, 190, 210, 190)), active ? 3f : 1.5f), p);
            }
            RectangleF r = new RectangleF(x, y + 8, size, size - 22);
            g.DrawString(name, _res.Small, _res.Brush(Color.White), r, _res.Center);
            g.DrawString(sub, _res.Small, _res.Brush(outOfRange ? Color.FromArgb(240, 150, 140) : Color.FromArgb(170, 200, 190)), new RectangleF(x, y + size - 16, size, 14), _res.Center);
            g.DrawString(key, _res.Bold, _res.Brush(Color.FromArgb(255, 230, 120)), x + 4, y + 1);
            if (leftMs > 0 && cdMs > 0)
            {
                // 남은 비율만큼 시계 방향으로 어둡게 (남은 쿨다운이 12시에서 시작해 줄어든다)
                float frac = Math.Min(1f, leftMs / (float)cdMs);
                GraphicsState gs = g.Save();
                using (GraphicsPath clip = P.RoundPath(x, y, size, size, 6)) g.SetClip(clip, CombineMode.Intersect);
                g.FillPie(_res.Brush(165, 0, 0, 0), x - size * 0.2f, y - size * 0.2f, size * 1.4f, size * 1.4f, -90, 360 * frac);
                g.Restore(gs);
                g.DrawString((leftMs / 1000.0).ToString("0.0"), _res.Bold, _res.Brush(Color.White), new RectangleF(x, y, size, size), _res.Center);
            }
        }

        private void DrawMiniMap(Graphics g, ViewerSession s, ViewState v, long now)
        {
            GameData d = s.Data;
            float size = 170;
            float x = v.Width - size - 12, y = 12;
            float wx0 = d.WorldMinX, wy0 = d.WorldMinY, ww = d.WorldMaxX - d.WorldMinX, wh = d.WorldMaxY - d.WorldMinY;
            float k = size / Math.Max(ww, wh);

            g.FillRectangle(_res.Brush(200, 28, 54, 30), x, y, ww * k, wh * k);
            // 몬스터 서식지(스폰) 표시
            for (int i = 0; i < d.Spawns.Count; ++i)
            {
                SpawnRow sp = d.Spawns[i];
                float r = Math.Max(2f, sp.Radius * k);
                g.FillEllipse(_res.Brush(60, 230, 90, 80), x + (sp.X - wx0) * k - r, y + (sp.Y - wy0) * k - r, r * 2, r * 2);
            }
            for (int i = 0; i < d.Blocks.Count; ++i)
            {
                BlockRow b = d.Blocks[i];
                if (b.IsCircle) { float r = Math.Max(1.5f, b.R * k); g.FillEllipse(_res.Brush(Color.FromArgb(20, 90, 36)), x + (b.X - wx0) * k - r, y + (b.Y - wy0) * k - r, r * 2, r * 2); }
                else g.FillRectangle(_res.Brush(Color.FromArgb(140, 140, 150)), x + (b.X - wx0) * k, y + (b.Y - wy0) * k, Math.Max(2f, b.W * k), Math.Max(2f, b.H * k));
            }
            foreach (KeyValuePair<ulong, Entity> kv in s.World.Entities)
            {
                Entity e = kv.Value;
                float ex, ey; e.PosAt(now, out ex, out ey);
                Color c = e.Id == s.World.SelfId ? Color.FromArgb(255, 235, 90) : (e.IsMonster ? (e.Dead ? Color.FromArgb(120, 120, 120) : Color.FromArgb(255, 90, 90)) : Color.FromArgb(120, 190, 255));
                float rr = e.Id == s.World.SelfId ? 3f : 2f;
                if (e.IsBoss && !e.Dead)
                {
                    float bx = x + (ex - wx0) * k, by = y + (ey - wy0) * k;
                    g.FillEllipse(_res.Brush(90, 255, 200, 60), bx - 8, by - 8, 16, 16);
                    c = Color.FromArgb(255, 210, 70); rr = 4f;
                }
                g.FillEllipse(_res.Brush(c), x + (ex - wx0) * k - rr, y + (ey - wy0) * k - rr, rr * 2, rr * 2);
            }
            // 현재 화면 범위
            float vx0 = v.ToWorldX(0), vy0 = v.ToWorldY(0), vx1 = v.ToWorldX(v.Width), vy1 = v.ToWorldY(v.Height);
            g.DrawRectangle(_res.Pen(220, 255, 255, 255, 1f), x + (vx0 - wx0) * k, y + (vy0 - wy0) * k, (vx1 - vx0) * k, (vy1 - vy0) * k);
            g.DrawRectangle(_res.Pen(200, 200, 230, 200, 1.5f), x, y, ww * k, wh * k);
        }

        private void DrawLog(Graphics g, ViewerSession s, ViewState v, long now)
        {
            List<LogLine> log = s.World.Log;
            int shown = 0;
            float x = 10, y = v.Height - 10;
            for (int i = log.Count - 1; i >= 0 && shown < 8; --i)
            {
                LogLine l = log[i];
                long age = now - l.Ms;
                if (age > 12000) break;
                int a = age > 9000 ? (int)(255 * (12000 - age) / 3000) : 255;
                Color c = l.Level == 2 ? Color.FromArgb(255, 110, 110) : (l.Level == 1 ? Color.FromArgb(255, 210, 120) : Color.FromArgb(220, 235, 220));
                y -= 17;
                _res.OutlinedText(g, l.Text, _res.Normal, Color.FromArgb(Resources.Clamp(a), c), x, y, _fmtLeft);
                ++shown;
            }
        }

        private void DrawHelp(Graphics g, ViewState v)
        {
            string[] help = {
                "좌클릭: 땅 = 이동 / 적 = 선택·추적 공격     우클릭(누른 채): 이동",
                "1~9: 스킬   Q: 기본 공격   Tab: 대상 전환   Esc: 취소",
                "A: 자동 전투   F: 시점 고정   휠/+-: 확대   Space: 내 위치   B: 보스 선택   미니맵 클릭: 이동",
                "H: 영웅 선택   M: 효과음   F12: 스크린샷   F5: 재접속   F1: 도움말   L: 로그",
            };
            float w = 0;
            for (int i = 0; i < help.Length; ++i) w = Math.Max(w, g.MeasureString(help[i], _res.Small).Width);
            float x = v.Width - w - 28;
            float top = 12 + 170 + 10;
            Panel(g, x, top, w + 16, help.Length * 15 + 8);
            for (int i = 0; i < help.Length; ++i)
                g.DrawString(help[i], _res.Small, _res.Brush(Color.FromArgb(215, 225, 215)), x + 8, top + 4 + i * 15);
        }

        private void DrawStateOverlay(Graphics g, ViewerSession s, ViewState v)
        {
            if (s.State == SessionState.InZone) return;
            Color c = (s.State == SessionState.Failed || s.State == SessionState.Disconnected) ? Color.FromArgb(255, 120, 110) : Color.FromArgb(255, 235, 150);
            g.FillRectangle(_res.Brush(110, 0, 0, 0), 0, v.Height / 2 - 60, v.Width, 120);
            g.DrawString(s.StatusText, _res.Huge, _res.Brush(c), new RectangleF(0, v.Height / 2 - 40, v.Width, 50), _res.Center);
            if (s.State == SessionState.Failed || s.State == SessionState.Disconnected)
                g.DrawString("F5 키로 다시 접속합니다", _res.Normal, _res.Brush(Color.White), new RectangleF(0, v.Height / 2 + 10, v.Width, 30), _res.Center);
        }
    }
}
