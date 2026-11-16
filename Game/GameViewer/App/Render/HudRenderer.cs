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
        private readonly LiveHud _live;
        private readonly WindowsHud _win;
        private readonly TipBox _tip;
        public HudRenderer(Resources res) { _res = res; _combat = new CombatHud(res); _live = new LiveHud(res); _win = new WindowsHud(res); _tip = new TipBox(res); }

        /// <summary>스킬바 칸 i (0 = 기본 공격, 1.. = 스킬)의 화면 영역. 그리기와 툴팁 판정이 같은 값을 쓴다.</summary>
        public static RectangleF SkillSlotRect(ViewState v, int skillCount, int i)
        {
            int n = Math.Min(9, skillCount);
            float slot = 58, gap = 6, total = (n + 1) * slot + n * gap;
            return new RectangleF((float)Math.Floor((v.Width - total) / 2) + i * (slot + gap), v.Height - 82, slot, slot);
        }

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
                DrawSlotDrag(g, s, v);
            }
            _combat.Draw(g, s, v, now);
            if (s.State == SessionState.InZone) _live.DrawCombo(g, s, v, now);
           
            if (v.LogMode == 0) DrawLog(g, s, v, now);
            else if (v.LogMode == 1) _win.DrawLogPanel(g, s, v, now);
           
            if (v.ShowHelp) DrawHelp(g, v);
           
            if (v.HeroSelectOpen) DrawHeroSelect(g, s, v);
            if (v.MapOpen && s.State == SessionState.InZone) _win.DrawWorldMap(g, s, v, now);
            if (s.State == SessionState.InZone && s.World.Self != null) DrawSkillTip(g, s, v, now);
            if (v.SettingsOpen && !v.KeysOpen) _win.DrawSettings(g, v, now);
            if (v.KeysOpen) _win.DrawKeys(g, v, now);
            _live.DrawConnection(g, s, v, now);
            _live.DrawIntro(g, s, v, now);
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
            if (v.Settings.ShowFps)
                lines.Add("FPS " + v.Fps.ToString("0") + (v.FrameMs > 0 ? " (그리기 " + v.FrameMs.ToString("0.0") + "ms)" : "") + "  수신 " + (s.Net.BytesIn / 1024) + "KB  패킷 " + w.PacketCount + (w.UnknownPacketCount + w.MalformedPacketCount > 0 ? "  (미상 " + w.UnknownPacketCount + "/불량 " + w.MalformedPacketCount + ")" : ""));
            if (v.Settings.ShowFps)
            {
                PingMeter pm = w.Ping;
                string[] qn = { "좋음", "보통", "나쁨" };
                lines.Add(pm.HasSample
                    ? "핑 " + pm.LastMs + "ms  (평균 " + pm.AvgMs + " / 최대 " + pm.MaxMs + ")  " + qn[pm.Quality] + (pm.Lost > 0 ? "  손실 " + pm.Lost : "")
                    : "핑 측정 중..." + (pm.Lost > 0 ? "  손실 " + pm.Lost : ""));
            }

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
            int n = Math.Min(9, s.MySkills.Count);
            float dockW = (n + 1) * 58 + n * 6 + 24;
            float w = Math.Max(340f, dockW - 40f), h = 22, x = (v.Width - w) / 2, y = v.Height - 128;
            float ratio = e.MaxHp > 0 ? Math.Max(0f, Math.Min(1f, e.Hp / (float)e.MaxHp)) : 0f;
            float ghost = e.MaxHp > 0 ? Math.Max(ratio, Math.Min(1f, e.GhostHp / (float)e.MaxHp)) : ratio;

            // 이름표 + 비율
            _res.OutlinedText(g, s.World.NameOf(e), _res.Bold, Color.FromArgb(255, 232, 150), x + 6, y - 19, _fmtLeft);
            if (!e.Dead) _res.OutlinedText(g, (ratio * 100f).ToString("0") + "%", _res.Small, Color.FromArgb(220, 230, 220), x + w - 6, y - 17, _fmtRight);

            if (e.Casting && e.CastTimeMs > 0 && !e.Dead)
            {
                float cp = Math.Max(0f, Math.Min(1f, (now - e.CastStartMs) / (float)e.CastTimeMs));
                float cw = w * 0.7f, cx = x + (w - cw) / 2, cy = y - 44;
                Ui.HpBar(g, _res, cx, cy, cw, 12, cp, cp, Color.FromArgb(214, 170, 255), Color.FromArgb(112, 60, 190), true);
                _res.OutlinedText(g, s.Data.SkillName(e.CastSkillId), _res.Small, Color.White, x + w / 2, cy - 1, _res.Center);
            }

            Color top, bot;
            if (e.Dead) { top = Color.FromArgb(120, 120, 124); bot = Color.FromArgb(50, 50, 56); }
            else Ui.HpColors(ratio, out top, out bot);
            // 위험 구간(30% 미만)에서 바깥 테두리가 맥동
            if (!e.Dead && ratio < 0.3f)
            {
                int a = 40 + (int)(60 * (0.5 + 0.5 * Math.Sin(now / 160.0)));
                using (GraphicsPath gl = P.RoundPath(x - 4, y - 4, w + 8, h + 8, 13))
                    g.DrawPath(_res.Pen(a, 255, 60, 50, 4f), gl);
            }
            Ui.HpBar(g, _res, x, y, w, h, ratio, ghost, top, bot, true);
            _live.DrawStatusRow(g, s.Data, e, x, y - 78f, 26f, 9, now, false);
            // 25% 눈금
            for (int i = 1; i < 4; ++i) g.DrawLine(_res.Pen(55, 0, 0, 0, 1f), x + w * i / 4f, y + 3, x + w * i / 4f, y + h - 3);
            string text = e.Dead ? "사망 - 부활 대기" : e.Hp.ToString("N0") + " / " + e.MaxHp.ToString("N0");
            _res.OutlinedText(g, text, _res.Bold, Color.White, x + w / 2, y + 2, _res.Center);
        }

        private readonly Dictionary<int, long> _prevLeft = new Dictionary<int, long>();
        private readonly Dictionary<int, long> _readyAt = new Dictionary<int, long>();
        private readonly StringFormat _fmtRight = new StringFormat { Alignment = StringAlignment.Far };

        private void DrawSkillBar(Graphics g, ViewerSession s, ViewState v, long now)
        {
            List<uint> skills = s.MySkills;
            int n = Math.Min(9, skills.Count);
            float slot = 58, gap = 6;
            float total = (n + 1) * slot + n * gap;
            float x0 = (float)Math.Floor((v.Width - total) / 2), y = v.Height - 82;

            // 독(dock): 슬롯 전체를 감싸는 금속 판. 크기가 같으면 한 번 그린 것을 붙인다.
            int dw = (int)Math.Ceiling(total + 24) + 8, dh = (int)Math.Ceiling(slot + 16) + 8;
            if (_dock == null || _dock.Width != dw || _dock.Height != dh)
            {
                if (_dock != null) _dock.Dispose();
                _dock = new Bitmap(dw, dh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics dg = Graphics.FromImage(_dock)) { dg.SmoothingMode = SmoothingMode.AntiAlias; Ui.Frame(dg, _res, 4, 4, total + 24, slot + 16, 12, Color.FromArgb(206, 176, 108)); }
            }
            g.DrawImageUnscaled(_dock, (int)(x0 - 12) - 4, (int)(y - 8) - 4);

            // 선택한 대상까지의 거리: 사거리 밖인 스킬은 슬롯을 붉게 표시한다
            float dist = -1f;
            Entity tgt = s.World.Find(v.SelectedId);
            float px, py;
            if (tgt != null && !tgt.Dead && s.SelfPos(now, out px, out py)) { float tx, ty; tgt.PosAt(now, out tx, out ty); dist = (float)Math.Sqrt((tx - px) * (tx - px) + (ty - py) * (ty - py)); }
            HeroRow hero = s.Data.Hero(s.HeroId);
            DrawSlot(g, x0, y, slot, 0, KeyLabel(v, 0), "기본", null, null, 0, 0, hero != null ? "사거리 " + hero.AttackRange.ToString("0.#") : "", false, dist >= 0 && hero != null && dist > hero.AttackRange + 0.3, now);
            for (int i = 0; i < n; ++i)
            {
                SkillRow sk = s.Data.Skill(skills[i]);
                long left = s.SkillCooldownLeftMs(skills[i], now);
                long cd = sk != null ? sk.CooldownMs : 1;
                bool casting = s.World.Self.Casting && s.World.Self.CastSkillId == skills[i];
                string sub = sk == null ? "" : SkillSubLabel(sk);
                bool far = dist >= 0 && sk != null && !sk.IsSelf && !(sk.IsAlly && (tgt == null || !tgt.IsPlayer)) && !(sk.IsArea && sk.Range <= 1.5) && dist > sk.Range;
                DrawSlot(g, x0 + (i + 1) * (slot + gap), y, slot, i + 1, KeyLabel(v, i + 1), s.Data.SkillName(skills[i]), sk, SkillFxTable.For(s.Data, skills[i]), left, cd, sub, casting, far, now);
            }
        }

        /// <summary>칸을 끌고 있을 때: 원래 칸은 점선, 놓을 칸은 금색 테두리, 마우스 옆에는 스킬 이름표.</summary>
        private void DrawSlotDrag(Graphics g, ViewerSession s, ViewState v)
        {
            if (v.DragSlot <= 0 || !v.HasMouse) return;
            int count = s.MySkills.Count, n = Math.Min(9, count);
            if (v.DragSlot > n) return;
            RectangleF src = SkillSlotRect(v, count, v.DragSlot);
            using (Pen p = new Pen(Color.FromArgb(200, 220, 220, 220), 1.5f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, src.X, src.Y, src.Width, src.Height);
            for (int i = 1; i <= n; ++i)
            {
                RectangleF r = SkillSlotRect(v, count, i);
                if (i != v.DragSlot && r.Contains(v.MouseX, v.MouseY))
                    using (Pen p = new Pen(Color.FromArgb(240, 255, 210, 90), 3f)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
            string name = s.Data.SkillName(s.MySkills[v.DragSlot - 1]);
            using (Font f = new Font("Malgun Gothic", 10f, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(name, f);
                RectangleF tag = new RectangleF(v.MouseX + 12, v.MouseY - sz.Height - 6, sz.Width + 12, sz.Height + 4);
                using (Brush b = new SolidBrush(Color.FromArgb(220, 24, 22, 30))) g.FillRectangle(b, tag);
                g.DrawString(name, f, Brushes.White, tag.X + 6, tag.Y + 2);
            }
        }

        private string KeyLabel(ViewState v, int slot)
        {
            return KeyBindings.KeyName(v.Settings.Keys.Get(slot == 0 ? BindAction.BasicAttack : (BindAction)(KeyBindings.FirstSkill + slot - 1)));
        }

        /// <summary>마우스가 올라간 스킬 칸의 설명(이름/모양/사거리/재사용/피해/효과)을 칸 위에 띄운다. 다른 창이 열려 있으면 띄우지 않는다.</summary>
        private void DrawSkillTip(Graphics g, ViewerSession s, ViewState v, long now)
        {
            if (!v.HasMouse || v.SettingsOpen || v.KeysOpen || v.MapOpen || v.HeroSelectOpen || v.ChatActive) return;
            int count = s.MySkills.Count, n = Math.Min(9, count);
            for (int i = 0; i <= n; ++i)
            {
                RectangleF r = SkillSlotRect(v, count, i);
                if (!r.Contains(v.MouseX, v.MouseY)) continue;
                List<TipLine> lines;
                if (i == 0) lines = SkillTip.BuildBasic(s.Data.Hero(s.HeroId), KeyLabel(v, 0));
                else
                {
                    SkillRow sk = s.Data.Skill(s.MySkills[i - 1]);
                    if (sk == null) return;
                    lines = SkillTip.Build(s.Data, sk, KeyLabel(v, i));
                    long left = s.SkillCooldownLeftMs(sk.Id, now);
                    if (left > 0) lines.Add(new TipLine("재사용 대기 " + (left / 1000.0).ToString("0.0") + "초 남음", TipKind.Note));
                }
                _tip.Draw(g, lines, r.X + r.Width / 2f, r.Y - 14f, v.Width, v.Height, true);
                return;
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

        private Bitmap _dock;
        private const int SlotPad = 6;
        private readonly Dictionary<string, Bitmap> _slotCache = new Dictionary<string, Bitmap>();

        /// <summary>스킬 칸의 바뀌지 않는 층(배경 그라데이션, 아이콘, 이름 띠, 이름, 사거리 글자, 쿨다운 어둡게/사거리 밖 붉게)을 그린 비트맵.
        /// variant: 1 = 쿨다운 중, 2 = 사거리 밖. 칸 밖으로 나가는 글자를 위해 사방에 SlotPad 만큼 여백을 둔다.</summary>
        private Bitmap SlotLayer(int idx, SkillRow sk, SkillFxInfo fx, string name, string sub, int variant, float size)
        {
            string key = idx + "|" + (sk != null ? sk.Id : 0u) + "|" + name + "|" + sub + "|" + variant + "|" + size;
            Bitmap bmp;
            if (_slotCache.TryGetValue(key, out bmp)) return bmp;
            bool cooling = (variant & 1) != 0, outOfRange = (variant & 2) != 0;
            int n = (int)Math.Ceiling(size) + SlotPad * 2;
            bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float x = SlotPad, y = SlotPad;
                using (GraphicsPath p = P.RoundPath(x, y, size, size, 8))
                {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, size, size + 1), Color.FromArgb(240, 52, 58, 70), Color.FromArgb(240, 14, 16, 24), LinearGradientMode.Vertical))
                    g.FillPath(b, p);
                GraphicsState gs = g.Save();
                g.SetClip(p, CombineMode.Intersect);
                Bitmap art = SkillIcons.Get(idx == 0 ? 0u : (sk != null ? sk.Id : 0u));
                if (art != null)
                {
                    InterpolationMode im = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(art, x, y, size, size);
                    g.InterpolationMode = im;
                }
                else
                {
                    float is_ = size * 0.58f;
                    Ui.SkillIcon(g, _res, x + (size - is_) / 2, y + 1, is_, sk, fx, idx == 0);
                }
                if (cooling) g.FillRectangle(_res.Brush(120, 6, 8, 14), x, y, size, size);   // 쿨다운 중엔 아이콘을 눌러 어둡게
                // 이름 띠
                g.FillRectangle(_res.Brush(165, 0, 0, 0), x, y + size - 29, size, 29);
                g.Restore(gs);
                g.DrawString(name, _res.Small, _res.Brush(cooling ? Color.FromArgb(170, 176, 184) : Color.White), new RectangleF(x - 2, y + size - 29, size + 4, 14), _res.Center);
                g.DrawString(sub, _res.Small, _res.Brush(outOfRange ? Color.FromArgb(255, 150, 140) : Color.FromArgb(150, 176, 168)), new RectangleF(x - 4, y + size - 16, size + 8, 15), _res.Center);
                if (outOfRange) g.FillPath(_res.Brush(60, 230, 40, 30), p);
                }
            }
            if (_slotCache.Count > 160) { foreach (Bitmap old in _slotCache.Values) old.Dispose(); _slotCache.Clear(); }
            _slotCache[key] = bmp;
            return bmp;
        }

        private void DrawSlot(Graphics g, float x, float y, float size, int idx, string key, string name, SkillRow sk, SkillFxInfo fx, long leftMs, long cdMs, string sub, bool active, bool outOfRange, long now)
        {
            // 쿨다운이 막 끝난 순간을 잡아 번쩍임
            long prev;
            if (_prevLeft.TryGetValue(idx, out prev) && prev > 0 && leftMs <= 0) _readyAt[idx] = now;
            _prevLeft[idx] = leftMs;
            long ra; float flash = 0f;
            if (_readyAt.TryGetValue(idx, out ra)) flash = Math.Max(0f, 1f - (now - ra) / 420f);

            bool cooling = leftMs > 0 && cdMs > 0;
            Color rim = active ? Color.FromArgb(255, 226, 130) : (outOfRange ? Color.FromArgb(225, 96, 84) : (cooling ? Color.FromArgb(120, 128, 138) : Color.FromArgb(214, 190, 128)));
            if (active)
                using (GraphicsPath gp = P.RoundPath(x - 3, y - 3, size + 6, size + 6, 10))
                    g.DrawPath(_res.Pen(110 + (int)(70 * Math.Sin(now / 110.0)), 255, 214, 110, 4f), gp);

            using (GraphicsPath p = P.RoundPath(x, y, size, size, 8))
            {
                // 아이콘/이름/사거리 글자는 바뀌지 않으므로 한 번 그려 둔 그림을 붙인다(매 프레임 확대 그리기·그라데이션을 하지 않는다)
                Bitmap layer = SlotLayer(idx, sk, fx, name, sub, (cooling ? 1 : 0) | (outOfRange ? 2 : 0), size);
                g.DrawImageUnscaled(layer, (int)Math.Round(x) - SlotPad, (int)Math.Round(y) - SlotPad);
                if (cooling)
                {
                    float frac = Math.Min(1f, leftMs / (float)cdMs);
                    GraphicsState g2 = g.Save();
                    g.SetClip(p, CombineMode.Intersect);
                    g.FillPie(_res.Brush(150, 0, 0, 0), x - size * 0.2f, y - size * 0.2f, size * 1.4f, size * 1.4f, -90, 360 * frac);
                    // 쓸고 가는 경계선
                    double ang = (-90 + 360 * frac) * Math.PI / 180.0;
                    g.DrawLine(_res.Pen(150, 255, 255, 255, 1.4f), x + size / 2, y + size / 2, x + size / 2 + (float)Math.Cos(ang) * size, y + size / 2 + (float)Math.Sin(ang) * size);
                    g.Restore(g2);
                    _res.OutlinedText(g, (leftMs / 1000.0).ToString(leftMs >= 10000 ? "0" : "0.0"), _res.Big, Color.White, x + size / 2, y + (size - 29) / 2 - 10, _res.Center);
                }
                if (flash > 0f)
                {
                    GraphicsState g3 = g.Save();
                    g.SetClip(p, CombineMode.Intersect);
                    g.FillPath(_res.Brush((int)(110 * flash), 255, 246, 200), p);
                    g.Restore(g3);
                }
                g.DrawPath(_res.Pen(rim, active ? 2.5f : 2f), p);
                g.DrawPath(_res.Pen((int)(120 + 130 * flash), 255, 255, 255, 1f), P.RoundPath(x + 1.5f, y + 1.5f, size - 3, size - 3, 7));
            }
            // 키 캡
            float kw = 18, kh = 16;
            using (GraphicsPath kp = P.RoundPath(x - 3, y - 4, kw, kh, 5))
            {
                g.FillPath(_res.Brush(240, 24, 26, 34), kp);
                g.DrawPath(_res.Pen(rim, 1.4f), kp);
            }
            g.DrawString(key, _res.Bold, _res.Brush(Color.FromArgb(255, 232, 140)), new RectangleF(x - 3, y - 4, kw, kh + 1), _res.Center);
        }

        private void DrawMiniMap(Graphics g, ViewerSession s, ViewState v, long now)
        {
            GameData d = s.Data;
            float size = 170;
            float x = v.Width - size - 12, y = 12;
            float wx0 = d.WorldMinX, wy0 = d.WorldMinY, ww = d.WorldMaxX - d.WorldMinX, wh = d.WorldMaxY - d.WorldMinY;
            float k = size / Math.Max(ww, wh);

            MapColors pal = MapPalette.For(ZoneTheme.Current);
            Color mapBg = pal.Bg, mapTree = pal.Tree, mapRock = pal.Rock, mapPool = pal.Pool;
            g.FillRectangle(_res.Brush(mapBg), x, y, ww * k, wh * k);
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
                if (b.IsCircle) { float r = Math.Max(1.5f, b.R * k); g.FillEllipse(_res.Brush(mapTree), x + (b.X - wx0) * k - r, y + (b.Y - wy0) * k - r, r * 2, r * 2); }
                else g.FillRectangle(_res.Brush(ZoneTheme.IsPool(ZoneTheme.CurrentZoneId, b.X, b.Y, b.W, b.H) ? mapPool : mapRock), x + (b.X - wx0) * k, y + (b.Y - wy0) * k, Math.Max(2f, b.W * k), Math.Max(2f, b.H * k));
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
            // 보스는 맥박처럼 퍼지는 고리로 어디서든 눈에 띄게
            foreach (KeyValuePair<ulong, Entity> kv in s.World.Entities)
            {
                Entity be = kv.Value;
                if (!be.IsBoss || be.Dead) continue;
                float bx, by; be.PosAt(now, out bx, out by);
                float ph = (now % 1400) / 1400f;
                float pr = 4f + 12f * ph;
                g.DrawEllipse(_res.Pen((int)(200 * (1f - ph)), 255, 200, 60, 1.5f), x + (bx - wx0) * k - pr, y + (by - wy0) * k - pr, pr * 2, pr * 2);
            }
            // 내가 가는 곳(이동 목적지)
            Entity me = s.World.Self;
            if (me != null && !me.Dead && me.Moving)
            {
                float dx = x + (me.DestX - wx0) * k, dy = y + (me.DestY - wy0) * k;
                float pr = 3f + 2f * (float)Math.Abs(Math.Sin(now / 220.0));
                float mx, my; me.PosAt(now, out mx, out my);
                g.DrawLine(_res.Pen(150, 255, 235, 120, 1f), x + (mx - wx0) * k, y + (my - wy0) * k, dx, dy);
                g.DrawLine(_res.Pen(240, 255, 245, 140, 2f), dx - pr, dy - pr, dx + pr, dy + pr);
                g.DrawLine(_res.Pen(240, 255, 245, 140, 2f), dx - pr, dy + pr, dx + pr, dy - pr);
            }
            // 현재 화면 범위
            float vx0 = v.ToWorldX(0), vy0 = v.ToWorldY(0), vx1 = v.ToWorldX(v.Width), vy1 = v.ToWorldY(v.Height);
            g.DrawRectangle(_res.Pen(220, 255, 255, 255, 1f), x + (vx0 - wx0) * k, y + (vy0 - wy0) * k, (vx1 - vx0) * k, (vy1 - vy0) * k);
            g.DrawRectangle(_res.Pen(200, 200, 230, 200, 1.5f), x, y, ww * k, wh * k);
            _res.OutlinedText(g, ZoneTheme.NameOf(s.ZoneId), _res.Small, Color.FromArgb(230, 245, 235, 200), x + 5, y + 3, _fmtLeft);
            if (v.WeatherLabel != null) _res.OutlinedText(g, v.WeatherLabel, _res.Small, Color.FromArgb(220, 200, 225, 255), x + 5, y + wh * k - 17, _fmtLeft);
            if (v.BgmLabel != null) _res.OutlinedText(g, "♪ " + v.BgmLabel, _res.Small, Color.FromArgb(200, 215, 235, 255), x + ww * k - 4, y + wh * k - 17, _fmtRight);
        }

        private void DrawLog(Graphics g, ViewerSession s, ViewState v, long now)
        {
            List<LogLine> log = s.World.Log;
            int shown = 0;
            float x = 10, y = v.Height - 10;
            for (int i = log.Count - 1; i >= 0 && shown < 8; --i)
            {
                LogLine l = log[i];
                if (!l.Feed) continue;
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
            KeyBindings kb = v.Settings.Keys;
            Func<BindAction, string> K = delegate (BindAction a) { return KeyBindings.KeyName(kb.Get(a)); };
            string[] help = {
                "좌클릭: 땅 = 이동 / 적 = 선택·추적 공격     우클릭(누른 채): 이동     G: 가까운 관문으로 이동(데모)",
                K(BindAction.Skill1) + "~" + K(BindAction.Skill9) + ": 스킬   " + K(BindAction.BasicAttack) + ": 기본 공격   " + K(BindAction.CycleTarget) + ": 대상 전환   Esc: 취소   스킬 칸에 마우스: 설명",
                K(BindAction.AutoBattle) + ": 자동 전투   " + K(BindAction.FollowToggle) + ": 시점 고정   휠/+-: 확대   " + K(BindAction.CenterCamera) + ": 내 위치   " + K(BindAction.SelectBoss) + ": 보스 선택   미니맵 클릭: 이동",
                K(BindAction.HeroSelect) + ": 영웅 선택   " + K(BindAction.WorldMap) + ": 월드맵   " + K(BindAction.Settings) + ": 설정   " + K(BindAction.ToggleLog) + ": 로그 창   " + K(BindAction.Chat) + ": 채팅   " + K(BindAction.Screenshot) + ": 스크린샷   " + K(BindAction.Reconnect) + ": 재접속   " + K(BindAction.ToggleHelp) + ": 도움말",
            };
            float w = 0;
            for (int i = 0; i < help.Length; ++i) w = Math.Max(w, g.MeasureString(help[i], _res.Small).Width);
            float x = v.Width - w - 28;
            float top = 12 + 170 + 10;
            Panel(g, x, top, w + 16, help.Length * 15 + 8);
            for (int i = 0; i < help.Length; ++i)
                g.DrawString(help[i], _res.Small, _res.Brush(Color.FromArgb(215, 225, 215)), x + 8, top + 4 + i * 15);
        }
    }
}
