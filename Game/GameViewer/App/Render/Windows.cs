//***************************************************************************
// Windows.cs : 게임 위에 띄우는 창 - 월드맵(M), 설정(F2), 탭 로그 창(L).
// 창의 배치(영역 계산)는 static 함수로 열어 두어 GameCanvas 가 같은 식으로 클릭을 판정한다.
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
    public sealed class WindowsHud
    {
        private readonly Resources _res;
        private readonly StringFormat _left = new StringFormat();
        private readonly StringFormat _right = new StringFormat { Alignment = StringAlignment.Far };
        private readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center };
        private readonly StringFormat _clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        private readonly List<LogLine> _tmp = new List<LogLine>(32);
        public WindowsHud(Resources res) { _res = res; }

        // ================================================================== 월드맵

        public static RectangleF MapWindow(ViewState v)
        {
            float mx = Math.Min(60f, v.Width * 0.06f), my = Math.Min(40f, v.Height * 0.06f);
            return new RectangleF(mx, my, Math.Max(200f, v.Width - mx * 2), Math.Max(160f, v.Height - my * 2));
        }

        public static RectangleF MapArea(ViewState v)
        {
            RectangleF w = MapWindow(v);
            return new RectangleF(w.X + 14, w.Y + 46, w.Width - 28, w.Height - 46 - 38);
        }

        /// <summary>현재 보기 상태(확대/중심)로 월드맵 좌표 변환기를 만든다.</summary>
        public static MapProjection MapProj(GameData d, ViewState v)
        {
            RectangleF a = MapArea(v);
            MapProjection m = new MapProjection();
            m.PanelX = a.X; m.PanelY = a.Y; m.PanelW = a.Width; m.PanelH = a.Height;
            m.WorldMinX = d.WorldMinX; m.WorldMinY = d.WorldMinY; m.WorldMaxX = d.WorldMaxX; m.WorldMaxY = d.WorldMaxY;
            m.Zoom = v.MapZoom;
            m.CenterX = float.IsNaN(v.MapCx) ? (d.WorldMinX + d.WorldMaxX) * 0.5f : v.MapCx;
            m.CenterY = float.IsNaN(v.MapCy) ? (d.WorldMinY + d.WorldMaxY) * 0.5f : v.MapCy;
            m.Clamp();
            return m;
        }

        public void DrawWorldMap(Graphics g, ViewerSession s, ViewState v, long now)
        {
            GameData d = s.Data;
            g.FillRectangle(_res.Brush(150, 0, 0, 0), 0, 0, v.Width, v.Height);
            RectangleF win = MapWindow(v);
            MapColors pal = MapPalette.For(ZoneTheme.Current);
            Ui.Frame(g, _res, win.X, win.Y, win.Width, win.Height, 12, pal.Accent);
            _res.OutlinedText(g, "월드맵  -  존 " + s.ZoneId + " " + ZoneTheme.NameOf(s.ZoneId), _res.Big, Color.FromArgb(255, 238, 190), win.X + 16, win.Y + 10, _left);

            MapProjection m = MapProj(d, v);
            RectangleF area = MapArea(v);
            string help = "휠: 확대   드래그: 이동   클릭: 그곳으로 이동   M / Esc: 닫기";
            _res.OutlinedText(g, help, _res.Small, Color.FromArgb(200, 210, 200), win.Right - 16, win.Y + 16, _right);

            GraphicsState gs = g.Save();
            g.SetClip(area, CombineMode.Replace);
            g.FillRectangle(_res.Brush(255, 8, 10, 12), area);
            float x0 = m.ToScreenX(d.WorldMinX), y0 = m.ToScreenY(d.WorldMinY), x1 = m.ToScreenX(d.WorldMaxX), y1 = m.ToScreenY(d.WorldMaxY);
            g.FillRectangle(_res.Brush(Color.FromArgb(255, pal.Bg)), x0, y0, x1 - x0, y1 - y0);
            DrawGrid(g, d, m, area);

            // 몬스터 서식지
            for (int i = 0; i < d.Spawns.Count; ++i)
            {
                SpawnRow sp = d.Spawns[i];
                float cx = m.ToScreenX(sp.X), cy = m.ToScreenY(sp.Y), r = Math.Max(4f, sp.Radius * m.Scale);
                bool boss = d.Bosses.ContainsKey(sp.MonsterId);
                g.FillEllipse(_res.Brush(boss ? 70 : 52, boss ? 255 : 230, boss ? 190 : 90, boss ? 60 : 80), cx - r, cy - r, r * 2, r * 2);
                g.DrawEllipse(_res.Pen(boss ? 150 : 90, boss ? 255 : 230, boss ? 200 : 100, boss ? 70 : 90, 1f), cx - r, cy - r, r * 2, r * 2);
                if (boss || m.Zoom >= 1.8f)
                {
                    string nm = (boss ? "★ " : "") + d.MonsterName(sp.MonsterId) + (sp.Count > 1 ? " x" + sp.Count : "");
                    _res.OutlinedText(g, nm, _res.Small, boss ? Color.FromArgb(255, 226, 140) : Color.FromArgb(255, 200, 196), cx, cy - 7, _center);
                }
            }
            // 장애물
            for (int i = 0; i < d.Blocks.Count; ++i)
            {
                BlockRow b = d.Blocks[i];
                if (b.IsCircle)
                {
                    float r = Math.Max(2f, b.R * m.Scale);
                    g.FillEllipse(_res.Brush(pal.Tree), m.ToScreenX(b.X) - r, m.ToScreenY(b.Y) - r, r * 2, r * 2);
                }
                else
                {
                    bool pool = ZoneTheme.IsPool(ZoneTheme.CurrentZoneId, b.X, b.Y, b.W, b.H);
                    float bx = m.ToScreenX(b.X), by = m.ToScreenY(b.Y);
                    g.FillRectangle(_res.Brush(pool ? pal.Pool : pal.Rock), bx, by, Math.Max(2f, b.W * m.Scale), Math.Max(2f, b.H * m.Scale));
                }
            }
            // 엔티티
            foreach (KeyValuePair<ulong, Entity> kv in s.World.Entities)
            {
                Entity e = kv.Value;
                if (e.Id == s.World.SelfId) continue;
                float ex, ey; e.PosAt(now, out ex, out ey);
                float sx = m.ToScreenX(ex), sy = m.ToScreenY(ey);
                if (e.IsBoss && !e.Dead)
                {
                    float pr = 9f + 2f * (float)Math.Sin(now / 180.0);
                    g.FillEllipse(_res.Brush(80, 255, 200, 60), sx - pr, sy - pr, pr * 2, pr * 2);
                    g.FillEllipse(_res.Brush(Color.FromArgb(255, 210, 70)), sx - 5, sy - 5, 10, 10);
                    g.DrawEllipse(_res.Pen(230, 60, 40, 10, 1.5f), sx - 5, sy - 5, 10, 10);
                    _res.OutlinedText(g, s.World.NameOf(e), _res.Small, Color.FromArgb(255, 226, 140), sx, sy + 8, _center);
                    continue;
                }
                Color c = e.IsMonster ? (e.Dead ? Color.FromArgb(120, 120, 120) : Color.FromArgb(255, 90, 90)) : Color.FromArgb(120, 190, 255);
                float rr = e.IsMonster ? 3f : 4f;
                g.FillEllipse(_res.Brush(c), sx - rr, sy - rr, rr * 2, rr * 2);
            }
            // 이동 목적지: 내 위치에서 점선으로 잇고 맥박 치는 X
            Entity meE = s.World.Self;
            if (meE != null && !meE.Dead && meE.Moving)
            {
                float mx, my; meE.PosAt(now, out mx, out my);
                float dx = m.ToScreenX(meE.DestX), dy = m.ToScreenY(meE.DestY);
                using (Pen dp = new Pen(Color.FromArgb(190, 255, 240, 130), 1.5f))
                {
                    dp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(dp, m.ToScreenX(mx), m.ToScreenY(my), dx, dy);
                }
                float pr = 5f + 3f * (float)Math.Abs(Math.Sin(now / 220.0));
                g.DrawEllipse(_res.Pen(200, 255, 245, 140, 1.5f), dx - pr * 1.6f, dy - pr * 1.6f, pr * 3.2f, pr * 3.2f);
                g.DrawLine(_res.Pen(255, 255, 245, 140, 2.5f), dx - pr, dy - pr, dx + pr, dy + pr);
                g.DrawLine(_res.Pen(255, 255, 245, 140, 2.5f), dx - pr, dy + pr, dx + pr, dy - pr);
            }
            // 현재 화면 범위
            float vx0 = m.ToScreenX(v.ToWorldX(0)), vy0 = m.ToScreenY(v.ToWorldY(0)), vx1 = m.ToScreenX(v.ToWorldX(v.Width)), vy1 = m.ToScreenY(v.ToWorldY(v.Height));
            g.DrawRectangle(_res.Pen(210, 255, 255, 255, 1f), vx0, vy0, vx1 - vx0, vy1 - vy0);
            // 나
            if (s.World.Self != null)
            {
                float px, py; s.World.Self.PosAt(now, out px, out py);
                float sx = m.ToScreenX(px), sy = m.ToScreenY(py);
                float ang = (float)Math.Atan2(s.World.Self.FaceY, s.World.Self.FaceX);
                PointF[] tri = {
                    new PointF(sx + (float)Math.Cos(ang) * 10, sy + (float)Math.Sin(ang) * 10),
                    new PointF(sx + (float)Math.Cos(ang + 2.5) * 7, sy + (float)Math.Sin(ang + 2.5) * 7),
                    new PointF(sx + (float)Math.Cos(ang - 2.5) * 7, sy + (float)Math.Sin(ang - 2.5) * 7) };
                int ring = 40 + (int)(35 * (0.5 + 0.5 * Math.Sin(now / 200.0)));
                g.FillEllipse(_res.Brush(ring, 255, 235, 90), sx - 14, sy - 14, 28, 28);
                g.FillPolygon(_res.Brush(Color.FromArgb(255, 235, 90)), tri);
                g.DrawPolygon(_res.Pen(240, 60, 50, 10, 1.4f), tri);
            }
            g.DrawRectangle(_res.Pen(210, 200, 220, 200, 1.5f), x0, y0, x1 - x0, y1 - y0);
            g.Restore(gs);
            g.DrawRectangle(_res.Pen(120, 200, 220, 200, 1f), area.X, area.Y, area.Width, area.Height);

            // 마우스 아래 정보
            if (v.MouseX >= 0 && m.Contains(v.MouseX, v.MouseY))
            {
                float wx = m.ToWorldX(v.MouseX), wy = m.ToWorldY(v.MouseY);
                string tip = "(" + wx.ToString("0") + ", " + wy.ToString("0") + ")";
                for (int i = 0; i < d.Spawns.Count; ++i)
                {
                    SpawnRow sp = d.Spawns[i];
                    float dx = wx - sp.X, dy = wy - sp.Y;
                    if (dx * dx + dy * dy <= sp.Radius * sp.Radius) { tip += "   " + d.MonsterName(sp.MonsterId) + " 서식지 (" + sp.Count + "마리)"; break; }
                }
                float tw = g.MeasureString(tip, _res.Small).Width + 12;
                float tx = Math.Min(v.MouseX + 14, area.Right - tw - 2), ty = Math.Min(v.MouseY + 16, area.Bottom - 22);
                g.FillRectangle(_res.Brush(215, 10, 14, 12), tx, ty, tw, 20);
                g.DrawRectangle(_res.Pen(150, 200, 220, 200, 1f), tx, ty, tw, 20);
                g.DrawString(tip, _res.Small, _res.Brush(Color.White), tx + 6, ty + 3);
            }

            // 범례
            float ly = win.Bottom - 28, lx = win.X + 18;
            lx = Legend(g, lx, ly, Color.FromArgb(255, 235, 90), "나") + 14;
            lx = Legend(g, lx, ly, Color.FromArgb(120, 190, 255), "플레이어") + 14;
            lx = Legend(g, lx, ly, Color.FromArgb(255, 90, 90), "몬스터") + 14;
            lx = Legend(g, lx, ly, Color.FromArgb(255, 210, 70), "보스") + 14;
            lx = Legend(g, lx, ly, Color.FromArgb(120, 230, 90, 80), "서식지") + 14;
            lx = Legend(g, lx, ly, pal.Pool, "물/용암") + 14;
            lx = Legend(g, lx, ly, pal.Rock, "장애물") + 14;
            _res.OutlinedText(g, "확대 x" + m.Zoom.ToString("0.0"), _res.Small, Color.FromArgb(210, 220, 210), win.Right - 16, ly + 2, _right);
        }

        private float Legend(Graphics g, float x, float y, Color c, string text)
        {
            g.FillEllipse(_res.Brush(c), x, y + 3, 11, 11);
            g.DrawString(text, _res.Small, _res.Brush(Color.FromArgb(225, 232, 225)), x + 15, y + 1);
            return x + 15 + g.MeasureString(text, _res.Small).Width;
        }

        private void DrawGrid(Graphics g, GameData d, MapProjection m, RectangleF area)
        {
            float step = m.Zoom >= 4f ? 25f : (m.Zoom >= 2f ? 50f : 100f);
            float gx = (float)Math.Ceiling(d.WorldMinX / step) * step;
            for (; gx <= d.WorldMaxX; gx += step)
            {
                float sx = m.ToScreenX(gx);
                if (sx < area.X || sx > area.Right) continue;
                g.DrawLine(_res.Pen(40, 255, 255, 255, 1f), sx, m.ToScreenY(d.WorldMinY), sx, m.ToScreenY(d.WorldMaxY));
                _res.OutlinedText(g, gx.ToString("0"), _res.Small, Color.FromArgb(150, 220, 220, 210), sx + 2, area.Y + 2, _left);
            }
            float gy = (float)Math.Ceiling(d.WorldMinY / step) * step;
            for (; gy <= d.WorldMaxY; gy += step)
            {
                float sy = m.ToScreenY(gy);
                if (sy < area.Y || sy > area.Bottom) continue;
                g.DrawLine(_res.Pen(40, 255, 255, 255, 1f), m.ToScreenX(d.WorldMinX), sy, m.ToScreenX(d.WorldMaxX), sy);
                _res.OutlinedText(g, gy.ToString("0"), _res.Small, Color.FromArgb(150, 220, 220, 210), area.X + 2, sy + 1, _left);
            }
        }

        // ================================================================== 설정

        public static readonly string[] SettingNames = { "그래픽 품질", "효과음", "효과음 크기", "배경음악", "배경음악 크기", "FPS / 네트워크 정보", "피해 숫자", "화면 효과 (저체력/피격)", "화면 흔들림 줄이기", "타격감 세기", "날씨 / 낮밤", "키 설정" };
        public const int RowQuality = 0, RowSfx = 1, RowSfxVol = 2, RowBgm = 3, RowBgmVol = 4, RowFps = 5, RowDmg = 6, RowFx = 7, RowShake = 8, RowFeedback = 9, RowWeather = 10, RowKeys = 11;

        /// <summary>슬라이더 줄인가, 그렇다면 최댓값과 한 칸 크기.</summary>
        public static bool IsSlider(int row, out int max, out int step)
        {
            max = row == RowFeedback ? 150 : 100; step = row == RowFeedback ? 25 : 10;
            return row == RowSfxVol || row == RowBgmVol || row == RowFeedback;
        }

        public static RectangleF SettingsWindow(ViewState v)
        {
            float w = 500f, h = 62f + SettingNames.Length * 34f + 54f;
            return new RectangleF((v.Width - w) / 2f, Math.Max(10f, (v.Height - h) / 2f), w, h);
        }

        public static RectangleF SettingsRow(ViewState v, int i)
        {
            RectangleF win = SettingsWindow(v);
            return new RectangleF(win.X + 16, win.Y + 52 + i * 34f, win.Width - 32, 30f);
        }

        /// <summary>슬라이더 줄의 막대 영역(행 오른쪽 부분).</summary>
        public static RectangleF SliderTrack(ViewState v, int i)
        {
            RectangleF r = SettingsRow(v, i);
            return new RectangleF(r.Right - 218, r.Y + 11, 150, 8);
        }

        public void DrawSettings(Graphics g, ViewState v, long now)
        {
            ViewSettings st = v.Settings;
            g.FillRectangle(_res.Brush(150, 0, 0, 0), 0, 0, v.Width, v.Height);
            RectangleF win = SettingsWindow(v);
            Ui.Frame(g, _res, win.X, win.Y, win.Width, win.Height, 12, Color.FromArgb(222, 188, 112));
            _res.OutlinedText(g, "설정", _res.Big, Color.FromArgb(255, 238, 190), win.X + 18, win.Y + 14, _left);
            for (int i = 0; i < SettingNames.Length; ++i)
            {
                RectangleF r = SettingsRow(v, i);
                bool sel = i == v.SettingsIndex;
                if (sel) { g.FillRectangle(_res.Brush(70, 120, 180, 130), r); }
                g.DrawRectangle(_res.Pen(sel ? 200 : 70, 200, 220, 190, 1f), r.X, r.Y, r.Width, r.Height);
                g.DrawString(SettingNames[i], _res.Normal, _res.Brush(Color.FromArgb(245, 245, 235)), r.X + 12, r.Y + 6);
                if (i == RowBgm && st.BgmGain > 0f && v.BgmLabel != null) g.DrawString("♪ " + v.BgmLabel, _res.Small, _res.Brush(Color.FromArgb(150, 190, 220)), r.X + 96, r.Y + 8);
                string val;
                switch (i)
                {
                    case RowQuality: val = ViewSettings.QualityName(st.Quality); break;
                    case RowSfx: val = st.SfxOn ? "켜짐" : "꺼짐"; break;
                    case RowSfxVol: val = st.VolumePercent + "%"; break;
                    case RowBgm: val = st.BgmOn ? "켜짐" : "꺼짐"; break;
                    case RowBgmVol: val = st.BgmVolumePercent + "%"; break;
                    case RowFps: val = st.ShowFps ? "표시" : "숨김"; break;
                    case RowDmg: val = st.ShowDamageNumbers ? "표시" : "숨김"; break;
                    case RowFx: val = st.ShowScreenFx ? "켜짐" : "꺼짐"; break;
                    case RowShake: val = st.ReduceShake ? "켜짐" : "꺼짐"; break;
                    case RowWeather: val = st.WeatherOn ? "켜짐" : "꺼짐"; break;
                    case RowFeedback: val = st.FeedbackPercent + "%"; break;
                    default: val = "변경..."; break;
                }
                int smax, sstep;
                if (IsSlider(i, out smax, out sstep))
                {
                    RectangleF t = SliderTrack(v, i);
                    int cur = i == RowSfxVol ? st.VolumePercent : (i == RowBgmVol ? st.BgmVolumePercent : st.FeedbackPercent);
                    bool on = i == RowSfxVol ? st.SfxOn : (i == RowBgmVol ? st.BgmOn : true);
                    g.FillRectangle(_res.Brush(150, 10, 12, 14), t);
                    float fr = cur / (float)smax;
                    g.FillRectangle(_res.Brush(on ? Color.FromArgb(235, 222, 188, 112) : Color.FromArgb(140, 120, 120, 120)), t.X, t.Y, t.Width * fr, t.Height);
                    if (i == RowFeedback) g.DrawLine(_res.Pen(160, 255, 255, 255, 1f), t.X + t.Width * (100f / smax), t.Y - 3, t.X + t.Width * (100f / smax), t.Y + t.Height + 3);       // 기본(100%) 눈금
                    g.FillEllipse(_res.Brush(Color.White), t.X + t.Width * fr - 6, t.Y + t.Height / 2 - 6, 12, 12);
                    g.DrawString(val, _res.Bold, _res.Brush(Color.FromArgb(255, 232, 150)), new RectangleF(r.Right - 44, r.Y + 5, 42, 20), _right);
                }
                else
                {
                    bool good = i == RowQuality || i == RowKeys || val == "켜짐" || val == "표시";
                    Color vc = good ? Color.FromArgb(255, 232, 150) : Color.FromArgb(170, 176, 184);
                    g.DrawString(i == RowKeys ? val + "  >" : "<  " + val + "  >", _res.Bold, _res.Brush(vc), new RectangleF(r.X, r.Y + 5, r.Width - 12, 20), _right);
                }
            }
            string q = st.Quality == GfxQuality.Low ? "낮음: 대기 연출과 화면 가장자리 어둡게 하기를 끕니다" : (st.Quality == GfxQuality.Medium ? "중간: 대기 연출(안개/먼지/눈보라 등)을 끕니다" : "높음: 모든 연출을 그립니다");
            g.DrawString(q, _res.Small, _res.Brush(Color.FromArgb(190, 205, 190)), new RectangleF(win.X + 18, win.Bottom - 46, win.Width - 36, 16), _left);
            g.DrawString("↑↓ 선택    ←→ / 클릭 변경    Enter: 키 설정 열기    F2 / Esc 닫기    (바꾼 값은 바로 저장됩니다)", _res.Small, _res.Brush(Color.FromArgb(200, 210, 200)), new RectangleF(win.X + 18, win.Bottom - 26, win.Width - 36, 16), _left);
        }

        // ================================================================== 탭 로그 창

        public static RectangleF LogPanel(ViewState v, int skillCount)
        {
            float total = (Math.Min(9, skillCount) + 1) * 58f + Math.Min(9, skillCount) * 6f;
            float dockLeft = (v.Width - total) / 2f - 14f;
            float w = Math.Max(260f, Math.Min(470f, dockLeft - 18f));
            float h = 198f;
            return new RectangleF(10f, v.Height - 10f - h, w, h);
        }

        public static RectangleF LogTabRect(ViewState v, int skillCount, int i)
        {
            RectangleF p = LogPanel(v, skillCount);
            float tw = (p.Width - 12f) / 4f;
            return new RectangleF(p.X + 6f + i * tw, p.Y + 4f, tw - 3f, 22f);
        }

        public static RectangleF ChatInputRect(ViewState v, int skillCount)
        {
            RectangleF p = LogPanel(v, skillCount);
            return new RectangleF(p.X + 6f, p.Bottom - 28f, p.Width - 12f, 22f);
        }

        public const int LogPanelLines = 8;

        public void DrawLogPanel(Graphics g, ViewerSession s, ViewState v, long now)
        {
            int skills = s.MySkills.Count;
            RectangleF p = LogPanel(v, skills);
            g.FillRectangle(_res.Brush(170, 8, 12, 10), p);
            g.DrawRectangle(_res.Pen(130, 200, 220, 190, 1f), p.X, p.Y, p.Width, p.Height);
            for (int i = 0; i < 4; ++i)
            {
                RectangleF t = LogTabRect(v, skills, i);
                bool sel = (int)v.LogTabSel == i;
                g.FillRectangle(_res.Brush(sel ? Color.FromArgb(200, 70, 110, 80) : Color.FromArgb(120, 30, 36, 34)), t);
                g.DrawRectangle(_res.Pen(sel ? 230 : 90, 200, 220, 190, 1f), t.X, t.Y, t.Width, t.Height);
                string label = LogFilter.Names[i] + (i == 3 ? "(로컬)" : "");
                g.DrawString(label, _res.Small, _res.Brush(sel ? Color.White : Color.FromArgb(170, 182, 172)), new RectangleF(t.X, t.Y + 3, t.Width, 16), _center);
            }
            LogFilter.Select(s.World.Log, v.LogTabSel, LogPanelLines, v.LogScroll, _tmp);
            float y = p.Y + 32f;
            for (int i = 0; i < _tmp.Count; ++i)
            {
                LogLine l = _tmp[i];
                Color c = l.Level == 2 ? Color.FromArgb(255, 120, 120) : (l.Level == 1 ? Color.FromArgb(255, 214, 130) : (l.Cat == LogCat.Chat ? Color.FromArgb(170, 226, 255) : (l.Cat == LogCat.Combat ? Color.FromArgb(236, 232, 214) : Color.FromArgb(206, 222, 206))));
                g.DrawString(l.Text, _res.Small, _res.Brush(c), new RectangleF(p.X + 8, y, p.Width - 22, 16), _clip);
                y += 16f;
            }
            if (_tmp.Count == 0) g.DrawString("(표시할 내용이 없습니다)", _res.Small, _res.Brush(Color.FromArgb(130, 140, 132)), p.X + 10, p.Y + 34);
            int total = LogFilter.Count(s.World.Log, v.LogTabSel);
            if (total > LogPanelLines)
            {
                float th = 8 * 16f, bh = Math.Max(12f, th * LogPanelLines / total);
                float frac = total - LogPanelLines <= 0 ? 0f : v.LogScroll / (float)(total - LogPanelLines);
                g.FillRectangle(_res.Brush(110, 220, 230, 220), p.Right - 8, p.Y + 32 + (th - bh) * (1f - Math.Max(0f, Math.Min(1f, frac))), 3, bh);
            }
            RectangleF ci = ChatInputRect(v, skills);
            if (v.ChatActive) { /* 입력 칸은 컨트롤이 위에 그린다 */ }
            else g.DrawString("Enter: 채팅 입력 (서버로 보내지 않고 이 창에만 표시)", _res.Small, _res.Brush(Color.FromArgb(150, 165, 152)), ci.X + 2, ci.Y + 4);
        }

        // ================================================================== 키 설정

        private const int KeyRowsLeft = 14;

        public static RectangleF KeysWindow(ViewState v)
        {
            float w = 660f, h = Math.Min(v.Height - 20f, 70f + KeyRowsLeft * 30f + 70f);
            return new RectangleF((v.Width - w) / 2f, Math.Max(10f, (v.Height - h) / 2f), w, h);
        }

        private static float KeyPitch(ViewState v) { return Math.Max(20f, Math.Min(30f, (KeysWindow(v).Height - 130f) / KeyRowsLeft)); }

        /// <summary>동작(BindAction 번호) i 의 줄. 왼쪽 열 14 개는 일반 동작, 오른쪽 열은 스킬 1~9.</summary>
        public static RectangleF KeyRow(ViewState v, int i)
        {
            RectangleF win = KeysWindow(v);
            float pitch = KeyPitch(v), colW = win.Width / 2f - 24f;
            bool left = i < KeyRowsLeft;
            int r = left ? i : i - KeyRowsLeft;
            return new RectangleF(left ? win.X + 16f : win.X + win.Width / 2f + 8f, win.Y + 52f + r * pitch, colW, pitch - 3f);
        }

        /// <summary>"전부 기본값" 단추(오른쪽 열 아래).</summary>
        public static RectangleF KeysResetButton(ViewState v)
        {
            RectangleF last = KeyRow(v, KeyBindings.ActionCount - 1);
            return new RectangleF(last.X, last.Bottom + 12f, last.Width, 28f);
        }

        public void DrawKeys(Graphics g, ViewState v, long now)
        {
            ViewSettings st = v.Settings;
            g.FillRectangle(_res.Brush(170, 0, 0, 0), 0, 0, v.Width, v.Height);
            RectangleF win = KeysWindow(v);
            Ui.Frame(g, _res, win.X, win.Y, win.Width, win.Height, 12, Color.FromArgb(222, 188, 112));
            _res.OutlinedText(g, "키 설정", _res.Big, Color.FromArgb(255, 238, 190), win.X + 18, win.Y + 14, _left);
            g.DrawString("스킬 1~9 는 숫자패드 1~9 로도 쓸 수 있습니다", _res.Small, _res.Brush(Color.FromArgb(160, 175, 165)), new RectangleF(win.Right - 330, win.Y + 20, 312, 16), _right);
            for (int i = 0; i < KeyBindings.ActionCount; ++i)
            {
                BindAction a = (BindAction)i;
                RectangleF r = KeyRow(v, i);
                bool sel = i == v.KeysIndex, cap = sel && v.KeysCapture;
                if (sel) g.FillRectangle(_res.Brush(cap ? Color.FromArgb(110, 214, 170, 70) : Color.FromArgb(70, 120, 180, 130)), r);
                g.DrawRectangle(_res.Pen(sel ? 210 : 60, 200, 220, 190, 1f), r.X, r.Y, r.Width, r.Height);
                g.DrawString(KeyBindings.ActionName(a), _res.Normal, _res.Brush(Color.FromArgb(240, 242, 232)), r.X + 10, r.Y + (r.Height - 17f) / 2f);
                float kw = 104f;
                RectangleF kb = new RectangleF(r.Right - kw - 6f, r.Y + 2f, kw, r.Height - 4f);
                bool blink = ((now / 350) & 1) == 0;
                g.FillRectangle(_res.Brush(cap ? Color.FromArgb(blink ? 230 : 150, 40, 30, 10) : Color.FromArgb(200, 20, 24, 30)), kb);
                g.DrawRectangle(_res.Pen(cap ? Color.FromArgb(255, 255, 210, 110) : (st.Keys.IsDefault(a) ? Color.FromArgb(120, 200, 210, 200) : Color.FromArgb(230, 120, 220, 255)), cap ? 1.8f : 1f), kb.X, kb.Y, kb.Width, kb.Height);
                g.DrawString(cap ? "키를 누르세요" : KeyBindings.KeyName(st.Keys.Get(a)), cap ? _res.Small : _res.Bold, _res.Brush(cap ? Color.FromArgb(255, 226, 150) : (st.Keys.IsDefault(a) ? Color.FromArgb(235, 240, 235) : Color.FromArgb(150, 230, 255))), kb, _center);
            }
            RectangleF rb = KeysResetButton(v);
            g.FillRectangle(_res.Brush(160, 70, 40, 36), rb);
            g.DrawRectangle(_res.Pen(180, 230, 170, 150, 1f), rb.X, rb.Y, rb.Width, rb.Height);
            g.DrawString("전부 기본값으로 (R)", _res.Normal, _res.Brush(Color.FromArgb(255, 226, 214)), rb, _center);
            if (!string.IsNullOrEmpty(v.KeysMsg)) g.DrawString(v.KeysMsg, _res.Small, _res.Brush(Color.FromArgb(255, 226, 150)), new RectangleF(win.X + 18, win.Bottom - 50, win.Width - 36, 16), _left);
            g.DrawString("쓸 수 없는 키: Esc, 방향키, Shift/Ctrl/Alt, 숫자패드 1~9.  이미 쓰는 키를 고르면 두 동작이 키를 맞바꿉니다.  Ctrl+M(효과음 끄기)은 바꿀 수 없습니다.", _res.Small, _res.Brush(Color.FromArgb(165, 180, 170)), new RectangleF(rb.X, rb.Bottom + 10f, rb.Width, 64f), _left);
            g.DrawString("↑↓←→ 선택     Enter / 클릭: 새 키 지정     Delete: 이 항목 기본값     Esc: 뒤로", _res.Small, _res.Brush(Color.FromArgb(200, 210, 200)), new RectangleF(win.X + 18, win.Bottom - 28, win.Width - 36, 18), _left);
        }
    }
}
