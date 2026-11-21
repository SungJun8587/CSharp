//***************************************************************************
// LiveHud.cs : 라이브 서비스 느낌의 화면 요소 - 상태 효과 아이콘, 콤보 표시, 접속/재접속 오버레이, 존 입장 연출, 지도 색.
// 규칙(시간표/분류/좌표)은 Core/Logic 에 있고 여기서는 그리기만 한다.
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
    /// <summary>존 종류별 지도 색(미니맵과 월드맵이 같이 쓴다).</summary>
    public struct MapColors
    {
        public Color Bg, Tree, Rock, Pool, Accent;
    }

    public static class MapPalette
    {
        public static MapColors For(MapKind mk)
        {
            MapColors c = new MapColors();
            switch (mk)
            {
                case MapKind.Desert: c.Bg = Color.FromArgb(214, 200, 166, 108); c.Tree = Color.FromArgb(70, 120, 50); c.Rock = Color.FromArgb(150, 100, 60); c.Pool = Color.FromArgb(70, 150, 200); c.Accent = Color.FromArgb(232, 190, 110); break;
                case MapKind.Snow: c.Bg = Color.FromArgb(214, 214, 230, 246); c.Tree = Color.FromArgb(40, 100, 90); c.Rock = Color.FromArgb(120, 136, 168); c.Pool = Color.FromArgb(120, 200, 236); c.Accent = Color.FromArgb(170, 214, 255); break;
                case MapKind.Volcano: c.Bg = Color.FromArgb(214, 52, 38, 40); c.Tree = Color.FromArgb(120, 100, 110); c.Rock = Color.FromArgb(96, 80, 90); c.Pool = Color.FromArgb(255, 120, 30); c.Accent = Color.FromArgb(255, 140, 70); break;
                case MapKind.Swamp: c.Bg = Color.FromArgb(208, 40, 56, 36); c.Tree = Color.FromArgb(24, 70, 40); c.Rock = Color.FromArgb(104, 110, 84); c.Pool = Color.FromArgb(40, 100, 90); c.Accent = Color.FromArgb(150, 214, 140); break;
                case MapKind.Ruins: c.Bg = Color.FromArgb(214, 126, 120, 108); c.Tree = Color.FromArgb(70, 110, 60); c.Rock = Color.FromArgb(70, 66, 62); c.Pool = Color.FromArgb(50, 130, 120); c.Accent = Color.FromArgb(206, 196, 170); break;
                case MapKind.Coast: c.Bg = Color.FromArgb(214, 226, 206, 150); c.Tree = Color.FromArgb(50, 130, 70); c.Rock = Color.FromArgb(150, 110, 70); c.Pool = Color.FromArgb(60, 190, 210); c.Accent = Color.FromArgb(120, 220, 235); break;
                case MapKind.Crystal: c.Bg = Color.FromArgb(220, 26, 22, 48); c.Tree = Color.FromArgb(110, 230, 255); c.Rock = Color.FromArgb(80, 72, 112); c.Pool = Color.FromArgb(60, 120, 220); c.Accent = Color.FromArgb(130, 230, 255); break;
                case MapKind.Sky: c.Bg = Color.FromArgb(220, 236, 240, 252); c.Tree = Color.FromArgb(240, 180, 200); c.Rock = Color.FromArgb(200, 170, 100); c.Pool = Color.FromArgb(150, 210, 250); c.Accent = Color.FromArgb(255, 226, 150); break;
                case MapKind.Dark: c.Bg = Color.FromArgb(222, 30, 22, 42); c.Tree = Color.FromArgb(120, 80, 160); c.Rock = Color.FromArgb(86, 70, 104); c.Pool = Color.FromArgb(170, 90, 240); c.Accent = Color.FromArgb(190, 130, 255); break;
                default: c.Bg = Color.FromArgb(200, 28, 54, 30); c.Tree = Color.FromArgb(20, 90, 36); c.Rock = Color.FromArgb(140, 140, 150); c.Pool = Color.FromArgb(70, 150, 200); c.Accent = Color.FromArgb(160, 226, 130); break;
            }
            return c;
        }
    }

    public sealed class LiveHud
    {
        private readonly Resources _res;
        private readonly StringFormat _left = new StringFormat();
        private readonly StringFormat _right = new StringFormat { Alignment = StringAlignment.Far };
        private readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center };
        public LiveHud(Resources res) { _res = res; }

        // ------------------------------------------------------------------ 상태 효과 아이콘

        private static Color HueColor(int hue)
        {
            switch (hue)
            {
                case 0: return Color.FromArgb(120, 190, 255);
                case 1: return Color.FromArgb(140, 230, 160);
                case 2: return Color.FromArgb(255, 210, 120);
                case 3: return Color.FromArgb(220, 160, 255);
                case 4: return Color.FromArgb(255, 160, 190);
                default: return Color.FromArgb(150, 230, 230);
            }
        }

        /// <summary>엔티티의 상태 효과를 아이콘 줄로 그린다(이로운 것 먼저, 남은 시간 짧은 순). 그린 폭을 돌려준다.</summary>
        public float DrawStatusRow(IGfx g, GameData data, Entity e, float x, float y, float size, int max, long now, bool rightAlign)
        {
            if (e == null || e.Statuses.Count == 0) return 0f;
            List<StatusInstance> list = new List<StatusInstance>(e.Statuses.Values);
            list.Sort(delegate (StatusInstance a, StatusInstance b)
            {
                bool da = StatusLook.IsDebuff(data.Status(a.StatusId)), db = StatusLook.IsDebuff(data.Status(b.StatusId));
                if (da != db) return da ? 1 : -1;
                return a.ExpireMs.CompareTo(b.ExpireMs);
            });
            int n = Math.Min(max, list.Count);
            float pitch = size + 4f, total = n * pitch - 4f + (list.Count > n ? 26f : 0f);
            float x0 = rightAlign ? x - total : x;
            for (int i = 0; i < n; ++i) DrawStatusChip(g, data, list[i], x0 + i * pitch, y, size, now);
            if (list.Count > n) _res.OutlinedText(g, "+" + (list.Count - n), _res.Small, Color.FromArgb(230, 230, 230), x0 + n * pitch, y + size / 2 - 8, _left);
            return total;
        }

        private void DrawStatusChip(IGfx g, GameData data, StatusInstance st, float x, float y, float size, long now)
        {
            StatusRow row = data.Status(st.StatusId);
            bool bad = StatusLook.IsDebuff(row);
            long left = Math.Max(0, st.ExpireMs - now);
            Color frame = bad ? Color.FromArgb(232, 96, 100) : Color.FromArgb(110, 210, 150);
            Color tint = HueColor(StatusLook.Hue(st.StatusId));
            using (GraphicsPath p = P.RoundPath(x, y, size, size, 6))
            {
                using (LinearGradientBrush b = GfxBrush.Linear(new RectangleF(x, y, size, size + 1), Color.FromArgb(235, tint.R / 2 + 20, tint.G / 2 + 20, tint.B / 2 + 20), Color.FromArgb(235, 16, 18, 26), LinearGradientMode.Vertical))
                    g.FillPath(b, p);
                _res.OutlinedText(g, StatusLook.Glyph(row, st.StatusId), _res.Bold, tint, x + size / 2, y + size / 2 - 10, _center);
                if (row != null && row.DurationMs > 0 && row.DurationMs <= 120000)      // 아주 긴 효과(격노 등)는 쓸어내림 없이 숫자만
                {
                    float frac = Math.Max(0f, Math.Min(1f, left / (float)row.DurationMs));
                    GfxState gs = g.Save();
                    g.SetClip(p, CombineMode.Intersect);
                    g.FillPie(_res.Brush(120, 0, 0, 0), x - size * 0.2f, y - size * 0.2f, size * 1.4f, size * 1.4f, -90 + 360 * frac, 360 * (1f - frac));
                    g.Restore(gs);
                }
                bool blink = left < 2500 && ((now / 180) & 1) == 0;
                g.DrawPath(_res.Pen(blink ? Color.FromArgb(255, 255, 255, 255) : frame, 1.8f), p);
            }
            if (size >= 22f) _res.OutlinedText(g, left > 120000 ? "-" : StatusLook.Remaining(left), _res.Small, Color.White, x + size / 2, y + size - 2, _center);
        }

        // ------------------------------------------------------------------ 콤보

        public void DrawCombo(IGfx g, ViewerSession s, ViewState v, long now)
        {
            ComboCounter c = s.World.Combo;
            int n = c.Active(now);
            if (n < 2) return;
            int tier = ComboCounter.Tier(n);
            float pop = 1f + 0.35f * c.Pop(now);
            Color col = tier == 0 ? Color.FromArgb(255, 238, 170) : (tier == 1 ? Color.FromArgb(255, 196, 96) : (tier == 2 ? Color.FromArgb(255, 124, 76) : Color.FromArgb(255, 92, 210)));
            float x = v.Width - 36f, y = v.Height * 0.40f;
            GfxState gs = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(pop, pop);
            _res.OutlinedText(g, n.ToString(), _res.Huge, col, -62f, -18f, _right);
            _res.OutlinedText(g, "HIT", _res.Bold, Color.FromArgb(255, 245, 225), -56f, -2f, _left);
            g.Restore(gs);
            long dmg = c.TotalDamage(now);
            _res.OutlinedText(g, "합계 " + dmg.ToString("N0"), _res.Small, Color.FromArgb(240, 230, 210), x, y + 22f, _right);
            float rem = c.Remaining(now), bw = 120f;
            g.FillRectangle(_res.Brush(120, 10, 10, 10), x - bw, y + 40f, bw, 4f);
            g.FillRectangle(_res.Brush(Color.FromArgb(230, col)), x - bw * rem, y + 40f, bw * rem, 4f);
        }

        // ------------------------------------------------------------------ 접속 / 재접속 오버레이

        private static readonly string[] StepNames = { "서버 접속", "로그인", "존 입장" };

        public void DrawConnection(IGfx g, ViewerSession s, ViewState v, long now)
        {
            if (s.State == SessionState.InZone) return;
            bool failed = s.State == SessionState.Failed || s.State == SessionState.Disconnected;
            bool waiting = s.Reconnect.Waiting;
            bool hasWorld = s.Reconnecting;
            g.FillRectangle(_res.Brush(hasWorld ? 150 : 120, 0, 0, 0), 0, 0, v.Width, v.Height);

            float w = 460f, h = failed ? 226f : 196f;
            float x = (v.Width - w) / 2f, y = (v.Height - h) / 2f;
            Color accent = failed ? Color.FromArgb(236, 110, 100) : Color.FromArgb(222, 188, 112);
            Ui.Frame(g, _res, x, y, w, h, 14, accent);

            string title;
            if (failed) title = s.State == SessionState.Failed ? "접속하지 못했습니다" : "연결이 끊어졌습니다";
            else title = hasWorld || s.Reconnect.Attempt > 0 ? "다시 접속하는 중" : "서버에 접속하는 중";
            _res.OutlinedText(g, title, _res.Big, accent, x + w / 2, y + 16, _center);
            g.DrawString(s.Config.Host + ":" + s.Config.Port + "   존 " + s.Config.ZoneId, _res.Small, _res.Brush(Color.FromArgb(180, 190, 180)), new RectangleF(x, y + 44, w, 16), _center);

            // 단계 표시: 접속 -> 로그인 -> 입장
            int step = s.ConnectStep;
            float sx0 = x + 40f, sw = (w - 80f) / 3f, sy = y + 78f;
            for (int i = 0; i < 3; ++i)
            {
                float cx = sx0 + sw * i + sw / 2f;
                bool done = step > i, cur = step == i;
                if (i < 2) g.DrawLine(_res.Pen(done ? 200 : 70, 222, 188, 112, 2f), cx + 16, sy + 10, cx + sw - 16, sy + 10);
                Color dot = done ? Color.FromArgb(120, 220, 150) : (cur ? Color.FromArgb(255, 226, 130) : Color.FromArgb(90, 96, 104));
                float r = cur ? 9f + 1.5f * (float)Math.Sin(now / 150.0) : 8f;
                g.FillEllipse(_res.Brush(dot), cx - r, sy + 10 - r, r * 2, r * 2);
                g.DrawEllipse(_res.Pen(160, 255, 255, 255, 1f), cx - r, sy + 10 - r, r * 2, r * 2);
                if (done) _res.OutlinedText(g, "v", _res.Small, Color.FromArgb(20, 60, 30), cx, sy + 3, _center);
                g.DrawString(StepNames[i], _res.Small, _res.Brush(done || cur ? Color.White : Color.FromArgb(130, 138, 146)), new RectangleF(cx - 50, sy + 26, 100, 16), _center);
            }

            float ty = y + 128f;
            if (failed)
            {
                g.DrawString(s.StatusText, _res.Normal, _res.Brush(Color.FromArgb(255, 200, 190)), new RectangleF(x + 16, ty, w - 32, 36), _center);
                if (waiting)
                {
                    long rem = s.Reconnect.RemainingMs(now);
                    long total = ReconnectPolicy.DelaysMs[Math.Max(0, Math.Min(ReconnectPolicy.DelaysMs.Length - 1, s.Reconnect.Attempt - 1))];
                    float fr = total > 0 ? 1f - Math.Min(1f, rem / (float)total) : 1f;
                    g.DrawString("자동 재접속 " + s.Reconnect.Attempt + "/" + s.Reconnect.MaxAttempts + "   " + (rem / 1000.0).ToString("0.0") + "초 후", _res.Bold, _res.Brush(Color.FromArgb(255, 232, 160)), new RectangleF(x, ty + 34, w, 18), _center);
                    g.FillRectangle(_res.Brush(110, 10, 10, 10), x + 60, ty + 58, w - 120, 6f);
                    g.FillRectangle(_res.Brush(235, 222, 188, 112), x + 60, ty + 58, (w - 120) * fr, 6f);
                }
                else
                {
                    bool gaveUp = s.Reconnect.Exhausted;
                    g.DrawString(gaveUp ? "자동 재접속을 포기했습니다" : "자동 재접속이 꺼져 있습니다", _res.Small, _res.Brush(Color.FromArgb(200, 190, 180)), new RectangleF(x, ty + 34, w, 16), _center);
                }
                g.DrawString("F5: 지금 다시 접속", _res.Small, _res.Brush(Color.White), new RectangleF(x, y + h - 24, w, 16), _center);
            }
            else
            {
                long el = Math.Max(0, now - s.ConnectStartedMs);
                int dots = (int)((now / 350) % 4);
                g.DrawString(s.StatusText.TrimEnd('.') + new string('.', dots) + "   " + (el / 1000.0).ToString("0.0") + "초", _res.Normal, _res.Brush(Color.FromArgb(235, 240, 235)), new RectangleF(x + 16, ty, w - 32, 22), _center);
                if (hasWorld) g.DrawString("마지막 화면을 유지하고 있습니다", _res.Small, _res.Brush(Color.FromArgb(170, 180, 170)), new RectangleF(x, ty + 28, w, 16), _center);
            }
        }

        // ------------------------------------------------------------------ 존 입장 연출

        public void DrawIntro(IGfx g, ViewerSession s, ViewState v, long now)
        {
            if (v.IntroStartMs < 0 || !ZoneIntro.Active(v.IntroStartMs, now)) return;
            float black = ZoneIntro.BlackAlpha(v.IntroStartMs, now);
            if (black > 0.004f) g.FillRectangle(_res.Brush((int)(255 * black), 0, 0, 0), 0, 0, v.Width, v.Height);
            float a = v.IntroBanner ? ZoneIntro.BannerAlpha(v.IntroStartMs, now) : 0f;
            if (a <= 0.004f) return;
            float slide = ZoneIntro.BannerSlide(v.IntroStartMs, now);
            MapKind kind = ZoneTheme.Current;
            Color accent = MapPalette.For(kind).Accent;
            float cy = v.Height * 0.36f;
            float off = (1f - slide) * 60f;
            int ia = Resources.Clamp((int)(255 * a));
            // 가는 띠
            float bandH = 92f * (0.35f + 0.65f * slide);
            g.FillRectangle(_res.Brush((int)(120 * a), 0, 0, 0), 0, cy - bandH / 2, v.Width, bandH);
            float lineW = Math.Min(v.Width * 0.5f, 520f) * slide;
            g.DrawLine(_res.Pen(Color.FromArgb(ia, accent), 2f), v.Width / 2f - lineW / 2, cy - bandH / 2, v.Width / 2f + lineW / 2, cy - bandH / 2);
            g.DrawLine(_res.Pen(Color.FromArgb(ia, accent), 2f), v.Width / 2f - lineW / 2, cy + bandH / 2, v.Width / 2f + lineW / 2, cy + bandH / 2);
            string zone = "ZONE " + s.ZoneId;
            _res.OutlinedText(g, zone, _res.Bold, Color.FromArgb(ia, accent), v.Width / 2f + off, cy - 34f, _center);
            _res.OutlinedText(g, ZoneTheme.NameOf(s.ZoneId), _res.Huge, Color.FromArgb(ia, 255, 248, 232), v.Width / 2f + off, cy - 16f, _center);
            _res.OutlinedText(g, ZoneIntro.Tagline(kind), _res.Normal, Color.FromArgb(ia, 226, 226, 220), v.Width / 2f + off, cy + 24f, _center);
        }
    }
}
