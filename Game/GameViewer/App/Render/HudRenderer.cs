//***************************************************************************
// HudRenderer.cs : 화면 위에 덮는 정보(상태/체력/스킬바/미니맵/로그/도움말).
//***************************************************************************
using System;
using GameViewer.App.Forest;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
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
        private readonly SkillBookHud _book;
        private readonly TipBox _tip;
        public HudRenderer(Resources res) { _res = res; _combat = new CombatHud(res); _live = new LiveHud(res); _win = new WindowsHud(res); _book = new SkillBookHud(res); _tip = new TipBox(res); }

        // 화면 위: 왼쪽 위에 경험치 막대(그 아래 정보 창), 오른쪽 위에 재화.
        // 화면 아래: 가운데 스킬 독(기본 공격 + 스킬 칸), 그 양옆에 체력 구슬(왼쪽)과 MP 구슬(오른쪽). 미니맵은 오른쪽 아래.
        private const float ExpBarX = 10f, ExpBarY = 10f, ExpBarH = 26f;
        /// <summary>경험치바와 그 아래 알림창(정보 패널)이 같이 쓰는 폭.</summary>
        public const float InfoW = 400f;
        /// <summary>오른쪽 위 재화 표시가 차지하는 폭(보스 체력바가 이 앞까지만 쓴다).</summary>
        public const float WalletReserve = 170f;
        private const float WalletRowH = ExpBarH, WalletRowGap = 6f;      // 골드 알약 높이는 경험치바와 같다

        /// <summary>보스 체력바가 놓일 가로 구간: 화면 가로 중앙을 중심으로, 왼쪽 경험치바/정보 창과 오른쪽 재화 표시에 닿지 않는 만큼.</summary>
        public static void BossArea(ViewState v, out float left, out float right)
        {
            float cx = v.Width / 2f;
            float half = Math.Max(150f, Math.Min(cx - (10f + InfoW + 24f), v.Width - 12f - WalletReserve - 10f - cx));
            left = cx - half; right = cx + half;
        }
        private const float Slot = 56f, SlotGap = 6f, BasicGap = 12f, OrbR = 44f;

        private static float StripWidth(int n) { return (n + 1) * Slot + n * SlotGap + (n > 0 ? BasicGap - SlotGap : 0f); }

        /// <summary>스킬바 칸 i (0 = 기본 공격, 1.. = 스킬)의 화면 영역. 기본 공격 칸만 조금 떨어져 있다. 그리기와 툴팁·끌기 판정이 같은 값을 쓴다.</summary>
        public static RectangleF SkillSlotRect(ViewState v, int skillCount, int i)
        {
            int n = Math.Min(9, skillCount);
            float x0 = (float)Math.Floor((v.Width - StripWidth(n)) / 2);
            return new RectangleF(x0 + i * (Slot + SlotGap) + (i > 0 ? BasicGap - SlotGap : 0f), v.Height - 80, Slot, Slot);
        }

        /// <summary>하단 독(체력 구슬 왼쪽 끝)의 x. 왼쪽 아래 로그 창이 이 앞까지만 쓴다.</summary>
        public static float DockLeft(ViewState v, int skillCount) { return SkillSlotRect(v, skillCount, 0).Left - 12f - 2f * OrbR + 16f - 6f; }

        /// <summary>사각형 네 모서리에 금빛 꺾쇠 장식을 그린다.</summary>
        public static void Corners(Graphics g, Resources res, RectangleF r, float len, Color c)
        {
            Pen p = res.Pen(c, 2f);
            g.DrawLines(p, new PointF[] { new PointF(r.X, r.Y + len), new PointF(r.X, r.Y), new PointF(r.X + len, r.Y) });
            g.DrawLines(p, new PointF[] { new PointF(r.Right - len, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Y + len) });
            g.DrawLines(p, new PointF[] { new PointF(r.X, r.Bottom - len), new PointF(r.X, r.Bottom), new PointF(r.X + len, r.Bottom) });
            g.DrawLines(p, new PointF[] { new PointF(r.Right - len, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - len) });
        }

        /// <summary>미니맵 영역(화면 좌표, 오른쪽 아래). 클릭 이동에도 쓴다.</summary>
        public static RectangleF MiniMapRect(GameData d, ViewState v)
        {
            float size = 170;
            float ww = d.WorldMaxX - d.WorldMinX, wh = d.WorldMaxY - d.WorldMinY;
            float k = size / Math.Max(ww, wh);
            return new RectangleF(v.Width - size - 12, v.Height - wh * k - 12, ww * k, wh * k);
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
            if (s.State == SessionState.InZone && s.World.Self != null) { DrawExpBar(g, s, v, now); DrawWallet(g, s, v); }
            _combat.Draw(g, s, v, now);
            if (s.State == SessionState.InZone) _live.DrawCombo(g, s, v, now);
           
            if (v.LogMode == 0) DrawLog(g, s, v, now);
            else if (v.LogMode == 1) _win.DrawLogPanel(g, s, v, now);
           
            DrawHelpHint(g, s, v);
            if (v.ShowHelp) DrawHelp(g, v);
           
            if (v.HeroSelectOpen) DrawHeroSelect(g, s, v);
            if (v.ZoneSelectOpen) DrawZoneSelect(g, s, v);
            if (v.SkillBookOpen && s.State == SessionState.InZone) _book.Draw(g, s, v, now);
            if (v.MapOpen && s.State == SessionState.InZone) _win.DrawWorldMap(g, s, v, now);
            if (s.State == SessionState.InZone && s.World.Self != null) DrawSkillTip(g, s, v, now);
            if (v.SettingsOpen && !v.KeysOpen) _win.DrawSettings(g, v, now);
            if (v.KeysOpen) _win.DrawKeys(g, v, now);
            _live.DrawConnection(g, s, v, now);
            _live.DrawIntro(g, s, v, now);
        }

        // ------------------------------------------------------------------ 영웅 선택 창 (H)

        private const float HeroRowH = 74f, HeroPanelW = 520f;

        /// <summary>3D 숲이 켜져 있으면 목록을 오른쪽으로 비켜 서서 왼쪽에 영웅이 서 있게 하고, 아니면 예전처럼 가운데에 둔다.</summary>
        private static float HeroPanelX(ViewState v)
        {
            if (!ForestBackdrop.Enabled || !ForestBackdrop.IsReady) return (v.Width - HeroPanelW) / 2;
            return Math.Max(8f, v.Width - HeroPanelW - 36f);
        }

        /// <summary>
        /// 영웅 선택 중이면 이번 그리기의 3D 숲 위에 고른 영웅을 세우도록 부탁해 둔다(ForestBackdrop.Draw 보다 먼저 부른다).
        /// 영웅 템플릿 1~4 가 전사/궁수/마법사/창기사이다. 그 밖의 번호는 영웅 없이 숲만 보인다.
        /// </summary>
        private static void RequestHeroBackdrop(ViewerSession s, ViewState v)
        {
            if (!v.HeroSelectOpen || !ForestBackdrop.IsReady) return;
            List<uint> ids = s.Data.HeroIds();
            if (ids.Count == 0) return;
            uint id = ids[Math.Max(0, Math.Min(ids.Count - 1, v.HeroSelectIndex))];
            if (id < 1 || id > 4) return;
            float cx = HeroPanelX(v) * 0.5f;                                   // 목록 왼쪽 빈 공간의 가운데
            ForestBackdrop.ShowHero((int)id - 1, cx / Math.Max(1, v.Width) * 2f - 1f);
        }

        /// <summary>영웅 선택 창의 줄 i 의 화면 영역. 마우스 클릭 판정에도 쓴다.</summary>
        public static RectangleF HeroRowRect(ViewState v, int count, int i)
        {
            float h = 64 + count * HeroRowH + 34;
            float x = HeroPanelX(v), y = Math.Max(10, (v.Height - h) / 2);
            return new RectangleF(x + 12, y + 52 + i * HeroRowH, HeroPanelW - 24, HeroRowH - 6);
        }

        private const float ZoneRowH = 30f, ZonePanelW = 380f;

        /// <summary>존 변경 창의 줄 i 의 화면 영역. 마우스 클릭 판정에도 쓴다.</summary>
        public static RectangleF ZoneRowRect(ViewState v, int count, int i)
        {
            float h = 60 + count * ZoneRowH + 34;
            float x = (v.Width - ZonePanelW) / 2, y = Math.Max(10, (v.Height - h) / 2);
            return new RectangleF(x + 12, y + 48 + i * ZoneRowH, ZonePanelW - 24, ZoneRowH - 4);
        }

        private void DrawZoneSelect(Graphics g, ViewerSession s, ViewState v)
        {
            uint[] zones = v.ZoneTerrainPick ? ZoneTheme.TerrainZones : ZoneTheme.Zones;
            g.FillRectangle(_res.Brush(150, 0, 0, 0), 0, 0, v.Width, v.Height);
            float h = 60 + zones.Length * ZoneRowH + 34;
            float x = (v.Width - ZonePanelW) / 2, y = Math.Max(10, (v.Height - h) / 2);
            g.FillRectangle(_res.Brush(235, 14, 20, 17), x, y, ZonePanelW, h);
            g.DrawRectangle(_res.Pen(200, 200, 220, 190, 1.5f), x, y, ZonePanelW, h);
            g.DrawString(v.ZoneTerrainPick ? "시험장 지형 선택" : "존 변경", _res.Big, _res.Brush(Color.FromArgb(255, 235, 150)), x + 16, y + 10);
            int sel = Math.Max(0, Math.Min(zones.Length - 1, v.ZoneSelectIndex));
            for (int i = 0; i < zones.Length; ++i)
            {
                RectangleF rc = ZoneRowRect(v, zones.Length, i);
                bool cur = v.ZoneTerrainPick ? (s.ZoneId == ZoneTheme.TestZone && zones[i] == ZoneTheme.TestTerrain) : zones[i] == s.ZoneId;
                if (i == sel) g.FillRectangle(_res.Brush(90, 120, 180, 130), rc.X, rc.Y, rc.Width, rc.Height);
                g.DrawRectangle(_res.Pen(i == sel ? 220 : 90, 200, 220, 190, 1f), rc.X, rc.Y, rc.Width, rc.Height);
                g.DrawString(v.ZoneTerrainPick ? "지형 " + zones[i] : "존 " + zones[i], _res.Bold, _res.Brush(Color.FromArgb(255, 245, 210)), rc.X + 10, rc.Y + 4);
                g.DrawString((v.ZoneTerrainPick ? ZoneTheme.TerrainName(zones[i]) : ZoneTheme.NameOf(zones[i])) + (cur ? "   (현재)" : ""), _res.Normal, _res.Brush(Color.FromArgb(215, 225, 215)), rc.X + 90, rc.Y + 5);
            }
            g.DrawString("↑↓ / 1~9 선택     Enter 또는 클릭: " + (v.ZoneTerrainPick ? "그 지형으로 시험장 입장" : "그 존으로 이동") + "     " + KeyBindings.KeyName(v.Settings.Keys.Get(BindAction.ZoneSelect)) + " / Esc: 닫기", _res.Small, _res.Brush(Color.FromArgb(200, 210, 200)), x + 16, y + h - 24);
        }

        private void DrawHeroSelect(Graphics g, ViewerSession s, ViewState v)
        {
            List<uint> ids = s.Data.HeroIds();
            // 배경 우선순위: 3D 숲(영웅이 서 있다) > 사용자가 둔 Assets/backdrop 이미지 > 어둡게만
            RequestHeroBackdrop(s, v);
            bool lit = ForestBackdrop.Draw(g, v.Width, v.Height) == ForestResult.Drawn || Backdrop.Draw(g, v.Width, v.Height);
            g.FillRectangle(_res.Brush(lit ? 40 : 150, 0, 0, 0), 0, 0, v.Width, v.Height);                                      // 배경이 있으면 살짝만 어둡게
            float h = 64 + ids.Count * HeroRowH + 34;
            float x = HeroPanelX(v), y = Math.Max(10, (v.Height - h) / 2);
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
            if (!v.ShowInfo) return;
            WorldModel w = s.World;
            int players = 0, monsters = 0, dead = 0;
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                if (kv.Value.IsMonster) ++monsters; else ++players;
                if (kv.Value.Dead) ++dead;
            }
            float x = 10, y = ExpBarY + ExpBarH + 8;
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

            float h = 10 + lines.Count * 17 + 4, wd = InfoW;
            RectangleF pr = new RectangleF(x, y, wd, h);
            using (GraphicsPath pp = P.RoundPath(x, y, wd, h, 8))
            {
                using (LinearGradientBrush bg = new LinearGradientBrush(new RectangleF(x, y - 1, wd, h + 2), Color.FromArgb(215, 22, 30, 48), Color.FromArgb(215, 8, 11, 20), 90f))
                    g.FillPath(bg, pp);
                g.DrawPath(_res.Pen(170, 214, 186, 110, 1.2f), pp);
            }
            Corners(g, _res, new RectangleF(x + 2, y + 2, wd - 4, h - 4), 8f, Color.FromArgb(230, 240, 206, 120));
            float ty = y + 5;
            for (int i = 0; i < lines.Count; ++i)
            {
                Color c = i == 0 ? Color.FromArgb(255, 226, 130) : Color.FromArgb(228, 236, 232);
                if (lines[i].StartsWith("자동 전투: ON", StringComparison.Ordinal)) c = Color.FromArgb(150, 240, 160);
                RectangleF lr = new RectangleF(x + 16, ty, wd - 24, 17);
                g.DrawString(lines[i], _res.Normal, _res.Brush(Color.FromArgb(150, 0, 0, 0)), new RectangleF(lr.X + 1, lr.Y + 1, lr.Width, lr.Height), _trim);
                g.DrawString(lines[i], _res.Normal, _res.Brush(c), lr, _trim);
                if (i == 0)
                {
                    g.FillPolygon(_res.Brush(240, 255, 226, 130), new PointF[] { new PointF(x + 8, ty + 8), new PointF(x + 12, ty + 4), new PointF(x + 16 - 0.5f, ty + 8), new PointF(x + 12, ty + 12) });
                    using (LinearGradientBrush dv = new LinearGradientBrush(new RectangleF(x + 10, ty + 19, wd - 20, 2f), Color.FromArgb(200, 240, 206, 120), Color.FromArgb(0, 240, 206, 120), 0f))
                        g.FillRectangle(dv, x + 10, ty + 20, wd - 20, 1.5f);
                    ty += 4;
                }
                ty += 17;
            }
        }

        private float _helpHintBottom = 40f;
        private readonly StringFormat _trim = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        private bool _hasMp;
        private int _mp;

        /// <summary>하단 독의 양옆 구슬(체력/MP), 시전 막대, 상태 효과 줄, 사망 안내.</summary>
        private void DrawSelfBar(Graphics g, ViewerSession s, ViewState v, long now)
        {
            Entity e = s.World.Self;
            WorldModel wm = s.World;
            int count = s.MySkills.Count, n = Math.Min(9, count);
            RectangleF first = SkillSlotRect(v, count, 0), last = SkillSlotRect(v, count, n);
            float left = first.Left, right = last.Right, mid = (left + right) / 2f, top = first.Top, cy = top + Slot / 2f;
            float ratio = e.MaxHp > 0 ? Math.Max(0f, Math.Min(1f, e.Hp / (float)e.MaxHp)) : 0f;
            float ghost = e.MaxHp > 0 ? Math.Max(ratio, Math.Min(1f, e.GhostHp / (float)e.MaxHp)) : ratio;

            if (e.Casting && e.CastTimeMs > 0 && !e.Dead)
            {
                float cp = Math.Max(0f, Math.Min(1f, (now - e.CastStartMs) / (float)e.CastTimeMs));
                float cw = Math.Max(220f, (right - left) * 0.8f), cx = mid - cw / 2f, cby = top - 34f;
                Ui.HpBar(g, _res, cx, cby, cw, 12, cp, cp, Color.FromArgb(214, 170, 255), Color.FromArgb(112, 60, 190), true);
                _res.OutlinedText(g, s.Data.SkillName(e.CastSkillId), _res.Small, Color.White, mid, cby - 12f, _res.Center);
            }
            if (e.Dead) _res.OutlinedText(g, "사망 - 부활 대기", _res.Bold, Color.FromArgb(255, 150, 140), mid, top - 22f, _res.Center);
            _live.DrawStatusRow(g, s.Data, e, left, top - 84f, 26f, 9, now, false);

            Color ct, cb;
            if (e.Dead) { ct = Color.FromArgb(120, 120, 124); cb = Color.FromArgb(50, 50, 56); }
            else Ui.HpColors(ratio, out ct, out cb);
            float hpX = left - 12f - OrbR + 16f, mpX = right + 12f + OrbR - 16f;
            DrawOrb(g, hpX, cy, OrbR, ratio, ghost, ct, cb, "HP", e.Dead ? "0" : e.Hp.ToString("N0"), !e.Dead && ratio < 0.3f, now);
            if (wm.MyMaxMp > 0)
            {
                float mr = Math.Max(0f, Math.Min(1f, wm.MyMp / (float)wm.MyMaxMp));
                DrawOrb(g, mpX, cy, OrbR, mr, mr, Color.FromArgb(126, 196, 255), Color.FromArgb(34, 74, 214), "MP", wm.MyMp.ToString("N0"), false, now);
            }
        }

        /// <summary>
        /// 라이브 게임식 구슬: 금속 테두리 안에 액체가 비율만큼 차 있고 윗면이 출렁인다. ghost(> ratio)는 방금 줄어든 만큼을 희게 잠깐 보여 준다.
        /// danger 면 바깥 테두리가 붉게 맥동한다.
        /// </summary>
        private void DrawOrb(Graphics g, float cx, float cy, float r, float ratio, float ghost, Color top, Color bot, string tag, string value, bool danger, long now)
        {
            float ri = r - 5f;
            if (danger)
            {
                int a = 50 + (int)(90 * (0.5 + 0.5 * Math.Sin(now / 160.0)));
                g.DrawEllipse(_res.Pen(a, 255, 60, 50, 6f), cx - r - 3, cy - r - 3, (r + 3) * 2, (r + 3) * 2);
            }
            RectangleF box = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (LinearGradientBrush rb = new LinearGradientBrush(new RectangleF(box.X, box.Y, box.Width, box.Height + 1), Color.FromArgb(240, 232, 202, 126), Color.FromArgb(240, 92, 66, 30), 90f))
                g.FillEllipse(rb, box);
            g.FillEllipse(_res.Brush(255, 8, 10, 16), cx - ri, cy - ri, ri * 2, ri * 2);
            GraphicsState st = g.Save();
            using (GraphicsPath clip = new GraphicsPath())
            {
                clip.AddEllipse(cx - ri, cy - ri, ri * 2, ri * 2);
                g.SetClip(clip, CombineMode.Intersect);
            }
            float bottom = cy + ri, hh = ri * 2f;
            if (ghost > ratio)
            {
                float gy = bottom - hh * ghost;
                g.FillRectangle(_res.Brush(120, 255, 255, 255), cx - ri, gy, ri * 2, bottom - gy);
            }
            if (ratio > 0f)
            {
                float ly = bottom - hh * ratio;
                RectangleF lr = new RectangleF(cx - ri, ly - 3f, ri * 2, bottom - ly + 3f);
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(lr.X, lr.Y, lr.Width, lr.Height + 1f), top, bot, 90f))
                    g.FillRectangle(lb, lr);
                PointF[] wave = new PointF[13];
                for (int i = 0; i < wave.Length; ++i)
                    wave[i] = new PointF(cx - ri + ri * 2f * i / (wave.Length - 1), ly + (float)Math.Sin(now / 260.0 + i * 0.9) * 2.2f);
                g.DrawLines(_res.Pen(170, 255, 255, 255, 2f), wave);
            }
            g.Restore(st);
            g.FillEllipse(_res.Brush(64, 255, 255, 255), cx - ri * 0.62f, cy - ri * 0.9f, ri * 1.0f, ri * 0.52f);      // 유리 광택
            g.DrawEllipse(_res.Pen(210, 26, 18, 8, 1.6f), cx - ri, cy - ri, ri * 2, ri * 2);
            g.DrawEllipse(_res.Pen(230, 255, 238, 176, 1.6f), box);
            _res.OutlinedText(g, tag, _res.Small, Color.FromArgb(235, 255, 255, 255), cx, cy - 10f, _res.Center);
            _res.OutlinedText(g, value, _res.Bold, Color.White, cx, cy + 8f, _res.Center);
        }

        /// <summary>
        /// 경험치 막대: 왼쪽 위 모서리의 고정 크기 막대(MMORPG의 플레이어 프레임 자리).
        /// 왼쪽에 레벨, 가운데에 현재/필요 경험치와 퍼센트(10% 눈금). 최대 레벨이면 금색 MAX, 레벨 표가 없는 서버(MyExpToNext 0)면 비워 둔다.
        /// 레벨 업 직후에는 막대가 잠깐 번쩍인다.
        /// </summary>
        private void DrawExpBar(Graphics g, ViewerSession s, ViewState v, long now)
        {
            WorldModel wm = s.World;
            float x = ExpBarX, y = ExpBarY, w = InfoW, h = ExpBarH;
            const float Badge = 40f;                                   // 왼쪽 레벨 원판 지름
            float bx = x + Badge * 0.5f, bw = w - Badge * 0.5f, cy = y + h / 2f;
            bool max = wm.MyMaxLevel;
            float er = max ? 1f : wm.MyExpToNext > 0 ? Math.Min(1f, wm.MyExp / (float)wm.MyExpToNext) : 0f;
            long since = now - wm.LevelUpMs;
            bool flash = since >= 0 && since < 1400;

            // 바닥 그림자 + 막대 틀
            using (GraphicsPath shadow = P.RoundPath(bx - 1, y + 3, bw + 2, h, 8))
                g.FillPath(_res.Brush(90, 0, 0, 0), shadow);
            using (GraphicsPath frame = P.RoundPath(bx, y, bw, h, 8))
            {
                using (LinearGradientBrush bg = new LinearGradientBrush(new RectangleF(bx, y - 1, bw, h + 2), Color.FromArgb(238, 8, 10, 18), Color.FromArgb(238, 28, 32, 46), 90f))
                    g.FillPath(bg, frame);
                GraphicsState st = g.Save();
                g.SetClip(frame, CombineMode.Intersect);
                if (er > 0f)
                {
                    Color ct = max ? Color.FromArgb(255, 244, 160) : Color.FromArgb(150, 226, 255), cb = max ? Color.FromArgb(226, 150, 30) : Color.FromArgb(28, 104, 226);
                    RectangleF fill = new RectangleF(bx, y, bw * er, h);
                    using (LinearGradientBrush gb = new LinearGradientBrush(new RectangleF(fill.X, fill.Y - 1, Math.Max(1f, fill.Width), fill.Height + 2), ct, cb, 90f))
                        g.FillRectangle(gb, fill);
                    g.FillRectangle(_res.Brush(85, 255, 255, 255), fill.X, fill.Y + 1, fill.Width, h * 0.38f);          // 윗면 광택
                    g.FillRectangle(_res.Brush(50, 0, 0, 0), fill.X, fill.Y + h * 0.78f, fill.Width, h * 0.22f);         // 아랫면 그늘
                    // 지나가는 반짝임 띠(2.4초 주기)
                    float sx = fill.X - 60f + (float)((now % 2400) / 2400.0) * (fill.Width + 120f);
                    using (LinearGradientBrush sh = new LinearGradientBrush(new RectangleF(sx - 30f, y, 60f, h), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
                    {
                        System.Drawing.Drawing2D.ColorBlend cbld = new System.Drawing.Drawing2D.ColorBlend(3);
                        cbld.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                        cbld.Positions = new float[] { 0f, 0.5f, 1f };
                        sh.InterpolationColors = cbld;
                        g.SetClip(new RectangleF(fill.X, fill.Y, fill.Width, fill.Height), CombineMode.Intersect);
                        g.FillRectangle(sh, sx - 30f, y, 60f, h);
                    }
                    // 채움 끝의 밝은 선
                    g.FillRectangle(_res.Brush(200, 255, 255, 255), fill.Right - 2f, fill.Y + 1, 2f, h - 2f);
                }
                g.Restore(st);
                st = g.Save();
                g.SetClip(frame, CombineMode.Intersect);
                if (flash) g.FillRectangle(_res.Brush((int)(170 * (1f - since / 1400f)), 255, 240, 170), bx, y, bw, h);
                for (int i = 1; i < 10; ++i) g.DrawLine(_res.Pen(70, 0, 0, 0, 1f), bx + bw * i / 10f, y + 3, bx + bw * i / 10f, y + h - 3);      // 10% 눈금
                g.Restore(st);
                g.DrawPath(_res.Pen(245, 214, 184, 112, 2f), frame);                                    // 금테
                using (GraphicsPath inner = P.RoundPath(bx + 2, y + 2, bw - 4, h - 4, 6))
                    g.DrawPath(_res.Pen(150, 20, 16, 8, 1f), inner);
            }

            string pct = max ? "MAX" : wm.MyExpToNext > 0 ? wm.MyExp.ToString("N0") + " / " + wm.MyExpToNext.ToString("N0") + "   " + (er * 100f).ToString("0.00") + "%" : "-";
            _res.OutlinedText(g, pct, _res.Normal, Color.White, bx + bw / 2f + Badge * 0.15f, cy, _res.Center);

            // 레벨 원판: 금속 테두리 + 어두운 속 + 레벨 숫자 (레벨 업 때 잠깐 빛난다)
            float bxc = x + Badge / 2f, r = Badge / 2f;
            if (flash) g.FillEllipse(_res.Brush((int)(150 * (1f - since / 1400f)), 255, 230, 120), bxc - r - 8, cy - r - 8, 2 * r + 16, 2 * r + 16);
            using (LinearGradientBrush rim = new LinearGradientBrush(new RectangleF(bxc - r, cy - r, 2 * r, 2 * r), Color.FromArgb(255, 246, 214, 130), Color.FromArgb(255, 150, 106, 40), 60f))
                g.FillEllipse(rim, bxc - r, cy - r, 2 * r, 2 * r);
            using (LinearGradientBrush core = new LinearGradientBrush(new RectangleF(bxc - r + 4, cy - r + 4, 2 * r - 8, 2 * r - 8), Color.FromArgb(255, 54, 62, 92), Color.FromArgb(255, 14, 16, 30), 90f))
                g.FillEllipse(core, bxc - r + 4, cy - r + 4, 2 * r - 8, 2 * r - 8);
            g.DrawEllipse(_res.Pen(200, 255, 236, 170, 1.2f), bxc - r + 4, cy - r + 4, 2 * r - 8, 2 * r - 8);
            _res.OutlinedText(g, "LV", _res.Small, Color.FromArgb(255, 214, 160), bxc, cy - 11f, _res.Center);
            _res.OutlinedText(g, wm.MyLevel.ToString(), _res.Big, Color.FromArgb(255, 244, 190), bxc, cy + 6f, _res.Center);
        }

        /// <summary>재화: 화면 오른쪽 위 끝에 알약 한 줄씩 아래로 쌓는다. 골드가 맨 오른쪽이고 항상 보인다. 다른 재화는 가진 것만 ID 순.</summary>
        private void DrawWallet(Graphics g, ViewerSession s, ViewState v)
        {
            List<uint> ids = new List<uint> { ZcCurrencyNotify.Gold };
            List<uint> others = new List<uint>();
            foreach (uint id in s.World.Wallet.Keys) if (id != ZcCurrencyNotify.Gold) others.Add(id);
            others.Sort();
            ids.AddRange(others);
            float y = ExpBarY, hh = WalletRowH;
            foreach (uint id in ids)
            {
                uint amt; s.World.Wallet.TryGetValue(id, out amt);
                bool gold = id == ZcCurrencyNotify.Gold;
                string txt = (gold ? "" : "#" + id + "  ") + amt.ToString("N0");
                float pw = Math.Min(WalletReserve, Math.Max(108f, 40f + g.MeasureString(txt, _res.Bold).Width));
                float x = v.Width - 12f - pw;
                using (GraphicsPath pill = P.RoundPath(x, y, pw, hh, hh / 2f))
                {
                    using (LinearGradientBrush pb = new LinearGradientBrush(new RectangleF(x, y, pw, hh + 1), Color.FromArgb(235, 62, 52, 34), Color.FromArgb(235, 16, 13, 9), 90f))
                        g.FillPath(pb, pill);
                    using (LinearGradientBrush gl = new LinearGradientBrush(new RectangleF(x, y, pw, hh * 0.5f + 1), Color.FromArgb(60, 255, 240, 190), Color.FromArgb(0, 255, 240, 190), 90f))
                        g.FillRectangle(gl, x + 6, y + 2, pw - 12, hh * 0.45f);
                    g.DrawPath(_res.Pen(240, 222, 188, 112, 2f), pill);
                }
                Color coin = gold ? Color.FromArgb(255, 205, 70) : Color.FromArgb(150, 210, 255);
                float cd = hh - 4f, cx0 = x + 4f, cy0 = y + (hh - cd) / 2f;
                g.FillEllipse(_res.Brush(255, 120, 80, 20), cx0, cy0, cd, cd);
                using (LinearGradientBrush cb = new LinearGradientBrush(new RectangleF(cx0 + 2, cy0 + 2, cd - 4, cd - 3), Col.Light(coin, 0.35f), Col.Dark(coin, 0.25f), 90f))
                    g.FillEllipse(cb, cx0 + 2, cy0 + 2, cd - 4, cd - 4);
                g.DrawEllipse(_res.Pen(150, 255, 245, 200, 1f), cx0 + 5, cy0 + 5, cd - 10, cd - 10);
                g.FillEllipse(_res.Brush(120, 255, 255, 255), cx0 + 7, cy0 + 5, 8, 6);
                _res.OutlinedText(g, txt, _res.Bold, gold ? Color.FromArgb(255, 230, 130) : Color.FromArgb(205, 232, 255), x + pw - 12f, y + hh / 2f, _rightMid);
                y += hh + WalletRowGap;
            }
        }

        private readonly Dictionary<int, long> _prevLeft = new Dictionary<int, long>();
        private readonly Dictionary<int, long> _readyAt = new Dictionary<int, long>();
        private readonly StringFormat _fmtRight = new StringFormat { Alignment = StringAlignment.Far };
        private readonly StringFormat _leftMid = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        private readonly StringFormat _rightMid = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        private void DrawSkillBar(Graphics g, ViewerSession s, ViewState v, long now)
        {
            List<uint> skills = s.MySkills;
            int n = Math.Min(9, skills.Count);
            RectangleF r0 = SkillSlotRect(v, skills.Count, 0);
            float slot = Slot, total = StripWidth(n), x0 = r0.X, y = r0.Y;
            _hasMp = s.World.MyMaxMp > 0; _mp = s.World.MyMp;

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
                if (skills[i] == 0) { DrawEmptySlot(g, s, i + 1, SkillSlotRect(v, skills.Count, i + 1).X, y, KeyLabel(v, i + 1)); continue; }       // 닫혔거나 비어 있는 칸
                SkillRow sk = s.Data.Skill(skills[i]);
                long left = s.SkillCooldownLeftMs(skills[i], now);
                long cd = sk != null ? sk.CooldownMs : 1;
                bool casting = s.World.Self.Casting && s.World.Self.CastSkillId == skills[i];
                string sub = sk == null ? "" : SkillSubLabel(sk);
                bool far = dist >= 0 && sk != null && !sk.IsSelf && !(sk.IsAlly && (tgt == null || !tgt.IsPlayer)) && !(sk.IsArea && sk.Range <= 1.5) && dist > sk.Range;
                DrawSlot(g, SkillSlotRect(v, skills.Count, i + 1).X, y, slot, i + 1, KeyLabel(v, i + 1), s.Data.SkillName(skills[i]), sk, SkillFxTable.For(s.Data, skills[i]), left, cd, sub, casting, far, now);
            }
        }

        private static readonly System.Drawing.Imaging.ImageAttributes DimIcon = MakeDim();
        private static System.Drawing.Imaging.ImageAttributes MakeDim()
        {
            System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(new float[][]
            {
                new float[] { 0.55f, 0, 0, 0, 0 }, new float[] { 0, 0.55f, 0, 0, 0 }, new float[] { 0, 0, 0.55f, 0, 0 }, new float[] { 0, 0, 0, 0.4f, 0 }, new float[] { 0, 0, 0, 0, 1 }
            });
            System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);
            return ia;
        }

        /// <summary>스킬바의 닫힌 칸(레벨 부족: 그 칸에 들어갈 스킬을 흐리게 + 자물쇠 + 필요 레벨)과 열렸지만 빈 상점 칸("+" 상점).</summary>
        private void DrawEmptySlot(Graphics g, ViewerSession s, int slot, float x, float y, string key)
        {
            float size = Slot;
            uint lockLv = s.SlotLockLevel(slot), des = s.DesignatedSkill(slot);
            using (GraphicsPath p = P.RoundPath(x, y, size, size, 8))
            {
                g.FillPath(_res.Brush(200, 20, 22, 30), p);
                Bitmap bmp = des != 0 ? SkillIcons.Get(des) : null;
                if (bmp != null)
                {
                    GraphicsState st = g.Save();
                    g.SetClip(p, CombineMode.Intersect);
                    g.DrawImage(bmp, new Rectangle((int)x + 3, (int)y + 3, (int)size - 6, (int)size - 6), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, DimIcon);
                    g.Restore(st);
                }
                if (lockLv > 0) g.FillPath(_res.Brush(110, 0, 0, 0), p);
                g.DrawPath(_res.Pen(lockLv > 0 ? Color.FromArgb(150, 110, 116, 130) : Color.FromArgb(200, 214, 190, 128), 2f), p);
            }
            float cx = x + size / 2f, cy = y + size / 2f - 4f;
            if (lockLv > 0)
            {
                using (Pen sp = new Pen(Color.FromArgb(235, 214, 218, 228), 2.2f)) g.DrawArc(sp, cx - 6f, cy - 15f, 12f, 14f, 180, 180);
                g.FillRectangle(_res.Brush(240, 214, 218, 228), cx - 9f, cy - 8f, 18f, 13f);
                g.FillRectangle(_res.Brush(255, 40, 44, 58), cx - 1.5f, cy - 4f, 3f, 5f);
                _res.OutlinedText(g, "Lv " + lockLv, _res.Small, Color.FromArgb(255, 226, 140), cx, y + size - 12f, _res.Center);
            }
            else
            {
                _res.OutlinedText(g, "+", _res.Big, Color.FromArgb(255, 232, 160), cx, cy, _res.Center);
                _res.OutlinedText(g, "상점", _res.Small, Color.FromArgb(220, 210, 170), cx, y + size - 12f, _res.Center);
            }
            float kw = 18, kh = 16;
            using (GraphicsPath kp = P.RoundPath(x - 3, y - 4, kw, kh, 5))
            {
                g.FillPath(_res.Brush(240, 24, 26, 34), kp);
                g.DrawPath(_res.Pen(Color.FromArgb(150, 110, 116, 130), 1.4f), kp);
            }
            g.DrawString(key, _res.Bold, _res.Brush(Color.FromArgb(200, 255, 232, 140)), new RectangleF(x - 3, y - 4, kw, kh + 1), _res.Center);
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
            string name = s.Data.SkillName(s.MySkills[v.DragSlot - 1]);       // 끌 수 있는 칸은 스킬이 있는 칸뿐이다(GameCanvas 가 시작할 때 확인)
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
            if (!v.HasMouse || v.SettingsOpen || v.KeysOpen || v.MapOpen || v.HeroSelectOpen || v.ZoneSelectOpen || v.SkillBookOpen || v.ChatActive) return;
            int count = s.MySkills.Count, n = Math.Min(9, count);
            for (int i = 0; i <= n; ++i)
            {
                RectangleF r = SkillSlotRect(v, count, i);
                if (!r.Contains(v.MouseX, v.MouseY)) continue;
                List<TipLine> lines;
                if (i == 0) lines = SkillTip.BuildBasic(s.Data.Hero(s.HeroId), KeyLabel(v, 0));
                else
                {
                    uint sid = s.MySkills[i - 1];
                    if (sid == 0 && s.World.SkillBookKnown)
                    {
                        uint lk = s.SlotLockLevel(i), des = s.DesignatedSkill(i);
                        lines = new List<TipLine>();
                        lines.Add(new TipLine(lk > 0 ? (des != 0 ? s.Data.SkillName(des) : "잠긴 칸") + "   [" + KeyLabel(v, i) + "]" : "빈 상점 칸   [" + KeyLabel(v, i) + "]", TipKind.Title));
                        lines.Add(new TipLine(lk > 0 ? "Lv " + lk + " 에 열립니다" + (i <= ViewerSession.FixedSlots ? " (레벨이 되면 자동으로 얻습니다)" : "") : "눌러서 스킬 창에서 상점 스킬을 사거나 끼웁니다", TipKind.Note));
                        _tip.Draw(g, lines, r.X + r.Width / 2f, r.Y - 14f, v.Width, v.Height, true);
                        return;
                    }
                    SkillRow sk = s.Data.Skill(sid);
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

        /// <summary>스킬 아이콘을 밝게 그리는 보정(밝기 x1.6, 올림 0.12). 어두운 원본 그림이 독 안에서 묻히지 않게 한다.</summary>
        private static readonly System.Drawing.Imaging.ImageAttributes BrightIcon = MakeBright();
        private static System.Drawing.Imaging.ImageAttributes MakeBright()
        {
            const float m = 1.6f, o = 0.12f;
            System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(new float[][]
            {
                new float[] { m, 0, 0, 0, 0 }, new float[] { 0, m, 0, 0, 0 }, new float[] { 0, 0, m, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 }, new float[] { o, o, o, 0, 1 },
            });
            System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);
            return ia;
        }
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
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, size, size + 1), Color.FromArgb(240, 128, 140, 166), Color.FromArgb(240, 64, 72, 98), LinearGradientMode.Vertical))
                    g.FillPath(b, p);
                GraphicsState gs = g.Save();
                g.SetClip(p, CombineMode.Intersect);
                Bitmap art = SkillIcons.Get(idx == 0 ? 0u : (sk != null ? sk.Id : 0u));
                if (art != null)
                {
                    InterpolationMode im = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(art, new Rectangle((int)x, (int)y, (int)Math.Ceiling(size), (int)Math.Ceiling(size)), 0, 0, art.Width, art.Height, GraphicsUnit.Pixel, BrightIcon);
                    g.InterpolationMode = im;
                }
                else
                {
                    float is_ = size * 0.58f;
                    Ui.SkillIcon(g, _res, x + (size - is_) / 2, y + 1, is_, sk, fx, idx == 0);
                }
                if (cooling) g.FillRectangle(_res.Brush(70, 6, 8, 14), x, y, size, size);   // 쿨다운 중엔 아이콘을 눌러 어둡게
                g.Restore(gs);
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
                    g.FillPie(_res.Brush(120, 0, 0, 0), x - size * 0.2f, y - size * 0.2f, size * 1.4f, size * 1.4f, -90, 360 * frac);
                    // 쓸고 가는 경계선
                    double ang = (-90 + 360 * frac) * Math.PI / 180.0;
                    g.DrawLine(_res.Pen(150, 255, 255, 255, 1.4f), x + size / 2, y + size / 2, x + size / 2 + (float)Math.Cos(ang) * size, y + size / 2 + (float)Math.Sin(ang) * size);
                    g.Restore(g2);
                    _res.OutlinedText(g, (leftMs / 1000.0).ToString(leftMs >= 10000 ? "0" : "0.0"), _res.Big, Color.White, x + size / 2, y + size / 2, _res.Center);
                }
                bool noMp = _hasMp && sk != null && sk.MpCost > (uint)Math.Max(0, _mp);
                if (noMp)
                {
                    GraphicsState g4 = g.Save();
                    g.SetClip(p, CombineMode.Intersect);
                    g.FillPath(_res.Brush(130, 10, 22, 90), p);
                    g.Restore(g4);
                }
                if (sk != null && sk.MpCost > 0)
                    _res.OutlinedText(g, sk.MpCost.ToString(), _res.Small, noMp ? Color.FromArgb(255, 130, 120) : Color.FromArgb(150, 200, 255), x + size - 4f, y + size - 11f, _rightMid);
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
            RectangleF mr = MiniMapRect(d, v);
            float x = mr.X, y = mr.Y;
            float size = 170;
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

        /// <summary>오른쪽 위의 작은 안내 칩: 도움말 단축키(기본 F1)를 알려 준다. 도움말이 열려 있으면 "닫기"로 바뀐다.</summary>
        private void DrawHelpHint(Graphics g, ViewerSession s, ViewState v)
        {
            string key = KeyBindings.KeyName(v.Settings.Keys.Get(BindAction.ToggleHelp));
            string label = v.ShowHelp ? "도움말 닫기" : "도움말";
            float kw = Math.Max(24f, g.MeasureString(key, _res.Bold).Width + 8f), lw = g.MeasureString(label, _res.Small).Width;
            bool wallet = s.State == SessionState.InZone && s.World.Self != null;
            int rows = 1; foreach (uint id in s.World.Wallet.Keys) if (id != ZcCurrencyNotify.Gold) ++rows;
            float h = 22f, w = kw + lw + 20f, x = v.Width - 12f - w, y = wallet ? ExpBarY + rows * (WalletRowH + WalletRowGap) : ExpBarY;       // 오른쪽 위 재화 표시 바로 아래
            _helpHintBottom = y + h;
            using (GraphicsPath chip = P.RoundPath(x, y, w, h, 11f))
            {
                g.FillPath(_res.Brush(150, 8, 12, 20), chip);
                g.DrawPath(_res.Pen(120, 214, 186, 110, 1f), chip);
            }
            using (GraphicsPath kb = P.RoundPath(x + 3f, y + 3f, kw, h - 6f, 6f))
            {
                using (LinearGradientBrush kg = new LinearGradientBrush(new RectangleF(x + 3f, y + 2f, kw, h - 4f), Color.FromArgb(255, 246, 214, 130), Color.FromArgb(255, 176, 128, 44), 90f))
                    g.FillPath(kg, kb);
                g.DrawPath(_res.Pen(220, 255, 240, 190, 1f), kb);
            }
            g.DrawString(key, _res.Bold, _res.Brush(Color.FromArgb(40, 26, 8)), new RectangleF(x + 3f, y + 2f, kw, h - 4f), _res.Center);
            g.DrawString(label, _res.Small, _res.Brush(Color.FromArgb(220, 228, 220)), x + kw + 10f, y + 4f);
        }

        private void DrawHelp(Graphics g, ViewState v)
        {
            KeyBindings kb = v.Settings.Keys;
            Func<BindAction, string> K = delegate (BindAction a) { return KeyBindings.KeyName(kb.Get(a)); };
            string[] help = {
                "좌클릭: 땅 = 이동 / 적 = 선택·추적 공격     우클릭(누른 채): 이동     G: 가까운 관문으로 이동(데모)     F6 (Shift+F6 = 이전): 시험장(존 0) 지형 변경(치트)",
                K(BindAction.Skill1) + "~" + K(BindAction.Skill9) + ": 스킬   " + K(BindAction.BasicAttack) + ": 기본 공격   " + K(BindAction.CycleTarget) + ": 대상 전환   Esc: 취소   스킬 칸에 마우스: 설명",
                K(BindAction.AutoBattle) + ": 자동 전투   " + K(BindAction.FollowToggle) + ": 시점 고정   휠/+-: 확대   " + K(BindAction.CenterCamera) + ": 내 위치   " + K(BindAction.SelectBoss) + ": 보스 선택   미니맵 클릭: 이동",
                K(BindAction.HeroSelect) + ": 영웅 선택   " + K(BindAction.SkillBook) + ": 스킬 창   " + K(BindAction.InfoPanel) + ": 정보 창   " + K(BindAction.ZoneSelect) + ": 존 변경   " + K(BindAction.WorldMap) + ": 월드맵   " + K(BindAction.Settings) + ": 설정   " + K(BindAction.ToggleLog) + ": 로그 창   " + K(BindAction.Chat) + ": 채팅   " + K(BindAction.Screenshot) + ": 스크린샷   " + K(BindAction.Reconnect) + ": 재접속   " + K(BindAction.ToggleHelp) + ": 도움말",
            };
            float w = 0;
            for (int i = 0; i < help.Length; ++i) w = Math.Max(w, g.MeasureString(help[i], _res.Small).Width);
            float x = v.Width - w - 28;
            float top = _helpHintBottom + 8f;
            Panel(g, x, top, w + 16, help.Length * 15 + 8);
            for (int i = 0; i < help.Length; ++i)
                g.DrawString(help[i], _res.Small, _res.Brush(Color.FromArgb(215, 225, 215)), x + 8, top + 4 + i * 15);
        }
    }
}
