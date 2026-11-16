//***************************************************************************
// StartScreen.cs : 클라이언트 시작 화면. 로딩바 -> 영웅 선택 -> 게임 시작 버튼.
// System.Drawing 만 쓰므로 PNG 스냅샷 도구도 같은 코드를 쓴다. 입력 처리는 GameCanvas 가 하고, 여기는 그리기와 판정 영역만 맡는다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Net;

namespace GameViewer.App.Render
{
    /// <summary>시작 화면의 서버 선택 칸에 쓰는 데이터: 서버 목록, 접속 측정 결과, 고른 서버.</summary>
    public sealed class ServerPanel
    {
        public List<ServerEntry> List = new List<ServerEntry>();
        public ServerProbe Probe;
        public int Selected;
    }

    public sealed class StartScreen : IDisposable
    {
        private readonly Resources _res;
        private readonly ImageAttributes _dim = new ImageAttributes();
        private readonly TipBox _tip;
        public KeyBindings Keys = new KeyBindings();
        public ServerPanel Servers;                 // null 이면 서버 선택 칸을 그리지 않는다(데모)
        public int HoverServer = -1;
        public int HoverSkillCard = -1, HoverSkill = -1;        // 마우스가 올라간 영웅 카드의 스킬 아이콘(툴팁)
        public const int MaxServerRows = 6;

        private string TipText(int i)
        {
            string a = KeyBindings.KeyName(Keys.Get(BindAction.AutoBattle)), hh = KeyBindings.KeyName(Keys.Get(BindAction.HeroSelect)), b = KeyBindings.KeyName(Keys.Get(BindAction.SelectBoss));
            switch (i % 5)
            {
                case 0: return "좌클릭으로 이동하고 몬스터를 누르면 쫓아가서 공격합니다";
                case 1: return KeyBindings.KeyName(Keys.Get(BindAction.Skill1)) + "~" + KeyBindings.KeyName(Keys.Get(BindAction.Skill9)) + " 키로 영웅 스킬을 쓰고, " + a + " 키로 자동 전투를 켤 수 있습니다";
                case 2: return "게임 중 " + hh + " 키로 언제든 다른 영웅으로 다시 접속할 수 있습니다";
                case 3: return b + " 키는 가장 가까운 보스를 선택하고 출현 위치로 이동합니다";
                default: return "스킬 칸에 마우스를 올리면 사거리·재사용 시간·피해 설명이 나옵니다. 키는 설정(F2)에서 바꿀 수 있습니다";
            }
        }

        public StartScreen(Resources res)
        {
            _res = res;
            _tip = new TipBox(res);
            ColorMatrix m = new ColorMatrix(new float[][] {
                new float[] { 0.55f, 0, 0, 0, 0 }, new float[] { 0, 0.55f, 0, 0, 0 }, new float[] { 0, 0, 0.6f, 0, 0 },
                new float[] { 0, 0, 0, 0.8f, 0 }, new float[] { 0, 0, 0, 0, 1 } });
            _dim.SetColorMatrix(m);
        }

        public void Dispose() { _dim.Dispose(); }

        // ------------------------------------------------------------------ 판정 영역

        private const float CardGap = 16f;

        private static float CardW(int w, int n) { return Math.Max(120f, Math.Min(230f, (w - 80f - (n - 1) * CardGap) / Math.Max(1, n))); }
        private static float CardH(int h) { return Math.Max(270f, Math.Min(420f, h * 0.56f)); }
        private static float CardTop(int h) { return Math.Max(90f, h * 0.22f); }

        /// <summary>영웅 카드 i 의 화면 영역.</summary>
        public static RectangleF CardRect(int w, int h, int n, int i)
        {
            float cw = CardW(w, n), total = n * cw + (n - 1) * CardGap;
            return new RectangleF((w - total) / 2f + i * (cw + CardGap), CardTop(h), cw, CardH(h));
        }

        /// <summary>"게임 시작" 버튼 영역.</summary>
        public static RectangleF StartRect(int w, int h)
        {
            float y = Math.Min(h - 96f, CardTop(h) + CardH(h) + 28f);
            return new RectangleF((w - 240f) / 2f, y, 240f, 50f);
        }

        /// <summary>맵(존) 선택 칸 i: "게임 시작" 버튼 오른쪽에 나란히 놓는다.</summary>
        public static RectangleF ZoneRect(int w, int h, int i)
        {
            RectangleF b = StartRect(w, h);
            float x0 = Math.Max(b.Right + 4f, Math.Min(b.Right + 28f, w - 8f - (5 * 78f - 6f)));
            int perRow = Math.Max(2, Math.Min(5, (int)((w - 8f - x0 + 6f) / 78f)));        // 창이 좁으면 줄을 바꾼다
            int row = i / perRow, col = i % perRow;
            return new RectangleF(x0 + col * 78f, b.Y - 4f + row * 36f, 72f, 32f);
        }

        /// <summary>서버 선택 칸 i: "게임 시작" 버튼 왼쪽에 세로로 놓는다.</summary>
        public static RectangleF ServerRect(int w, int h, int i)
        {
            RectangleF b = StartRect(w, h);
            float wd = Math.Max(120f, Math.Min(214f, b.X - 30f - 8f));        // 좁은 창에서는 칸이 시작 버튼을 침범하지 않게 줄인다
            return new RectangleF(Math.Max(8f, b.X - 30f - wd), b.Y - 4f + i * 34f, wd, 30f);
        }

        /// <summary>화면 아래로 넘치지 않고 그릴 수 있는 서버 칸 수(1..MaxServerRows). 낮은 창에서는 줄어든다.</summary>
        public static int ServerRows(int w, int h)
        {
            RectangleF b = StartRect(w, h);
            int rows = (int)((h - (b.Y - 4f) - 8f) / 34f);
            return Math.Max(1, Math.Min(MaxServerRows, rows));
        }

        /// <summary>(x,y) 아래의 서버 칸 번호, 없으면 -1.</summary>
        public static int ServerAt(int w, int h, int count, float x, float y)
        {
            for (int i = 0; i < Math.Min(ServerRows(w, h), count); ++i) if (ServerRect(w, h, i).Contains(x, y)) return i;
            return -1;
        }

        /// <summary>영웅 카드의 스킬 아이콘 k (n 개 중). 카드 맨 아래 줄에 가운데 정렬해서 놓는다.</summary>
        public static RectangleF SkillIconRect(int w, int h, int cards, int card, int k, int n)
        {
            RectangleF rc = CardRect(w, h, cards, card);
            float size = Math.Min(30f, (rc.Width - 16f - (n - 1) * 4f) / Math.Max(1, n));
            float total = n * size + (n - 1) * 4f;
            return new RectangleF(rc.X + (rc.Width - total) / 2f + k * (size + 4f), rc.Bottom - size - 8f, size, size);
        }

        public const int MaxCardSkills = 6;

        /// <summary>(x,y) 아래의 영웅 카드 스킬 아이콘. 없으면 false.</summary>
        public static bool SkillIconAt(int w, int h, GameData d, float x, float y, out int card, out int k)
        {
            List<uint> ids = d.HeroIds();
            for (int c = 0; c < ids.Count; ++c)
            {
                int n = Math.Min(MaxCardSkills, d.SkillsOf(ids[c]).Count);
                for (int i = 0; i < n; ++i)
                    if (SkillIconRect(w, h, ids.Count, c, i, n).Contains(x, y)) { card = c; k = i; return true; }
            }
            card = -1; k = -1; return false;
        }

        /// <summary>(x,y) 아래의 맵 칸 번호, 없으면 -1.</summary>
        public static int ZoneAt(int w, int h, float x, float y)
        {
            for (int i = 0; i < ZoneTheme.Zones.Length; ++i) if (ZoneRect(w, h, i).Contains(x, y)) return i;
            return -1;
        }

        /// <summary>(x,y) 아래의 카드 번호, 없으면 -1.</summary>
        public static int CardAt(int w, int h, int n, float x, float y)
        {
            for (int i = 0; i < n; ++i) if (CardRect(w, h, n, i).Contains(x, y)) return i;
            return -1;
        }

        // ------------------------------------------------------------------ 공통

        private void Background(Graphics g, int w, int h, int dark)
        {
            if (Backdrop.Draw(g, w, h)) g.FillRectangle(_res.Brush(dark, 4, 10, 6), 0, 0, w, h);
            else
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, w, h + 1), Color.FromArgb(22, 54, 34), Color.FromArgb(6, 14, 10), LinearGradientMode.Vertical))
                    g.FillRectangle(b, 0, 0, w, h);
            }
        }

        private void Title(Graphics g, int w, int h)
        {
            RectangleF r = new RectangleF(0, Math.Max(14f, h * 0.05f), w, 50);
            _res.OutlinedText(g, "GAME VIEWER", _res.Huge, Color.FromArgb(255, 235, 150), w / 2f, r.Y, TitleFmt(w));
            g.DrawString("MMORPG 필드 전투 뷰어", _res.Normal, _res.Brush(Color.FromArgb(210, 225, 210)), new RectangleF(0, r.Y + 42, w, 22), _res.Center);
        }

        private StringFormat _titleFmt;
        private StringFormat TitleFmt(int w)
        {
            if (_titleFmt == null) { _titleFmt = new StringFormat(); _titleFmt.Alignment = StringAlignment.Center; }
            return _titleFmt;
        }

        // ------------------------------------------------------------------ 로딩

        /// <summary>로딩 화면. shown 은 화면에 보이는 진행률(0..1), label 은 지금 읽는 것.</summary>
        public void DrawLoading(Graphics g, int w, int h, float shown, string label, long now)
        {
            shown = Math.Max(0f, Math.Min(1f, shown));
            Background(g, w, h, 150);
            Title(g, w, h);
            float bw = Math.Min(560f, w - 80f), bh = 18f, x = (w - bw) / 2f, y = h * 0.60f;
            g.DrawString(label.Length > 0 ? "불러오는 중 - " + label : (shown >= 1f ? "준비 완료" : "불러오는 중"), _res.Normal, _res.Brush(Color.FromArgb(235, 240, 235)), x, y - 30);
            g.DrawString((int)(shown * 100f) + "%", _res.Bold, _res.Brush(Color.FromArgb(255, 235, 150)), new RectangleF(x, y - 30, bw, 22), RightFmt());
            g.FillRectangle(_res.Brush(170, 8, 12, 10), x - 3, y - 3, bw + 6, bh + 6);
            g.DrawRectangle(_res.Pen(200, 200, 220, 190, 1.2f), x - 3, y - 3, bw + 6, bh + 6);
            float fw = bw * shown;
            if (fw >= 1f)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, Math.Max(2f, fw), bh), Color.FromArgb(150, 230, 120), Color.FromArgb(60, 160, 90), LinearGradientMode.Vertical))
                    g.FillRectangle(b, x, y, fw, bh);
                g.FillRectangle(_res.Brush(60, 255, 255, 255), x, y, fw, 5);
                // 지나가는 빛줄기
                float sx = x + (float)((now % 1400) / 1400.0) * (fw + 60f) - 40f;
                Region old = g.Clip;
                g.SetClip(new RectangleF(x, y, fw, bh));
                g.FillRectangle(_res.Brush(55, 255, 255, 255), sx, y, 26, bh);
                g.Clip = old;
            }
            g.DrawString(TipText((int)((now / 3500) % 5)), _res.Small, _res.Brush(Color.FromArgb(200, 215, 200)), new RectangleF(0, y + 36, w, 20), _res.Center);
        }

        private StringFormat _rightFmt;
        private StringFormat RightFmt()
        {
            if (_rightFmt == null) { _rightFmt = new StringFormat(); _rightFmt.Alignment = StringAlignment.Far; }
            return _rightFmt;
        }

        // ------------------------------------------------------------------ 영웅 선택

        /// <summary>영웅 선택 화면. sel 은 선택된 카드, hover 는 마우스가 올라간 카드(-1 없음), target 은 접속 대상 설명.</summary>
        public void DrawSelect(Graphics g, int w, int h, GameData data, int sel, int hover, string target, long now) { DrawSelect(g, w, h, data, sel, hover, target, now, 0); }

        /// <summary>zoneId 가 0 이 아니면 시작 버튼 옆에 맵 선택 칸을 그린다. hover: -2 시작 버튼, -3-i 맵 칸 i.</summary>
        public void DrawSelect(Graphics g, int w, int h, GameData data, int sel, int hover, string target, long now, uint zoneId)
        {
            List<uint> ids = data.HeroIds();
            ComputeMaxes(data, ids);
            Background(g, w, h, 130);
            Title(g, w, h);
            if (ids.Count == 0)
            {
                g.DrawString("GameData 에 Hero 시트가 없어 서버 기본 영웅으로 시작합니다", _res.Normal, _res.Brush(Color.FromArgb(255, 200, 170)), new RectangleF(0, h * 0.4f, w, 24), _res.Center);
            }
            else
            {
                g.DrawString("영웅을 선택하세요", _res.Big, _res.Brush(Color.FromArgb(240, 245, 235)), new RectangleF(0, CardTop(h) - 38, w, 30), _res.Center);
                sel = Math.Max(0, Math.Min(ids.Count - 1, sel));
                for (int i = 0; i < ids.Count; ++i) DrawCard(g, w, h, data, ids, i, i == sel, i == hover, now);
            }
            RectangleF b = StartRect(w, h);
            bool hot = hover == -2;
            using (LinearGradientBrush br = new LinearGradientBrush(b, hot ? Color.FromArgb(150, 235, 130) : Color.FromArgb(110, 200, 100), hot ? Color.FromArgb(70, 170, 90) : Color.FromArgb(50, 140, 70), LinearGradientMode.Vertical))
                g.FillRectangle(br, b.X, b.Y, b.Width, b.Height);
            g.DrawRectangle(_res.Pen(230, 230, 255, 200, 1.6f), b.X, b.Y, b.Width, b.Height);
            g.DrawString("게임 시작", _res.Big, _res.Brush(Color.FromArgb(14, 30, 16)), b, _res.Center);
            if (Servers != null && Servers.List.Count > 0) DrawServers(g, w, h, now);
            if (zoneId != 0)
            {
                RectangleF z0 = ZoneRect(w, h, 0);
                g.DrawString("맵 (Tab)", _res.Small, _res.Brush(Color.FromArgb(200, 215, 200)), z0.X, z0.Y - 20);
                for (int i = 0; i < ZoneTheme.Zones.Length; ++i)
                {
                    uint id = ZoneTheme.Zones[i];
                    RectangleF zr = ZoneRect(w, h, i);
                    bool on = id == zoneId, zh = hover == -3 - i;
                    Color c1, c2;
                    switch (ZoneTheme.For(id))
                    {
                        case MapKind.Desert: c1 = Color.FromArgb(232, 190, 110); c2 = Color.FromArgb(170, 112, 56); break;
                        case MapKind.Snow: c1 = Color.FromArgb(236, 246, 255); c2 = Color.FromArgb(120, 164, 214); break;
                        case MapKind.Volcano: c1 = Color.FromArgb(255, 150, 60); c2 = Color.FromArgb(150, 36, 20); break;
                        case MapKind.Swamp: c1 = Color.FromArgb(150, 176, 90); c2 = Color.FromArgb(50, 80, 56); break;
                        case MapKind.Ruins: c1 = Color.FromArgb(214, 208, 192); c2 = Color.FromArgb(110, 104, 94); break;
                        case MapKind.Coast: c1 = Color.FromArgb(120, 226, 224); c2 = Color.FromArgb(220, 190, 130); break;
                        case MapKind.Crystal: c1 = Color.FromArgb(150, 130, 255); c2 = Color.FromArgb(40, 30, 90); break;
                        case MapKind.Sky: c1 = Color.FromArgb(255, 250, 220); c2 = Color.FromArgb(170, 200, 246); break;
                        case MapKind.Dark: c1 = Color.FromArgb(170, 100, 240); c2 = Color.FromArgb(34, 18, 52); break;
                        default: c1 = Color.FromArgb(110, 190, 110); c2 = Color.FromArgb(40, 110, 60); break;
                    }
                    using (LinearGradientBrush br = new LinearGradientBrush(zr, on ? c1 : Color.FromArgb(60, 70, 64), on ? c2 : Color.FromArgb(26, 32, 28), LinearGradientMode.Vertical))
                        g.FillRectangle(br, zr.X, zr.Y, zr.Width, zr.Height);
                    g.DrawRectangle(_res.Pen(on ? 255 : (zh ? 190 : 110), on ? 255 : 210, on ? 240 : 225, on ? 190 : 200, on ? 2.2f : 1f), zr.X, zr.Y, zr.Width, zr.Height);
                    g.DrawString(id + "  " + ZoneTheme.NameOf(id), _res.Bold, _res.Brush(on ? Color.FromArgb(24, 20, 10) : Color.FromArgb(215, 225, 215)), zr, _res.Center);
                }
            }
            float hintLeft = Servers != null && Servers.List.Count > 0 ? ServerRect(w, h, 0).Right + 8f : 0f;       // 서버 칸 오른쪽에서 가운데 정렬
            g.DrawString("← → / 1~9 선택     Enter 또는 더블클릭: 시작" + (Servers != null && Servers.List.Count > 1 ? "     S: 서버 바꾸기" : ""), _res.Small, _res.Brush(Color.FromArgb(215, 225, 215)), new RectangleF(hintLeft, Math.Max(h - 50f, b.Bottom + 6f), w - hintLeft, 18), _res.Center);
            g.DrawString(target, _res.Small, _res.Brush(Color.FromArgb(170, 190, 170)), new RectangleF(0, h - 24, w, 18), _res.Center);
            if (HoverSkillCard >= 0 && HoverSkillCard < ids.Count)
            {
                List<uint> sk = data.SkillsOf(ids[HoverSkillCard]);
                int n = Math.Min(MaxCardSkills, sk.Count);
                SkillRow row = HoverSkill >= 0 && HoverSkill < n ? data.Skill(sk[HoverSkill]) : null;
                if (row != null)
                {
                    RectangleF ir = SkillIconRect(w, h, ids.Count, HoverSkillCard, HoverSkill, n);
                    _tip.Draw(g, SkillTip.Build(data, row, ""), ir.X + ir.Width / 2f, ir.Y - 6f, w, h, true);
                }
            }
        }

        private int _mxHp = 1, _mxAtk = 1, _mxDef = 1; private double _mxRange = 1;
        private StringFormat _clipFmt;
        private StringFormat ClipFmt()
        {
            if (_clipFmt == null) { _clipFmt = new StringFormat(); _clipFmt.Trimming = StringTrimming.EllipsisCharacter; _clipFmt.FormatFlags = StringFormatFlags.NoWrap; }
            return _clipFmt;
        }

        private void ComputeMaxes(GameData data, List<uint> ids)
        {
            _mxHp = 1; _mxAtk = 1; _mxDef = 1; _mxRange = 1;
            for (int i = 0; i < ids.Count; ++i)
            {
                HeroRow r = data.Hero(ids[i]);
                _mxHp = Math.Max(_mxHp, r.MaxHp); _mxAtk = Math.Max(_mxAtk, r.Attack); _mxDef = Math.Max(_mxDef, r.Defense); _mxRange = Math.Max(_mxRange, r.AttackRange);
            }
        }

        /// <summary>능력치 한 줄: 이름 / 영웅들 중 가장 높은 값을 100% 로 한 막대 / 값.</summary>
        private void StatBar(Graphics g, float x, float y, float w, string label, string value, float ratio, Color c)
        {
            g.DrawString(label, _res.Small, _res.Brush(Color.FromArgb(190, 205, 192)), x, y);
            float bx = x + 34f, bw = w - 34f - 44f;
            g.FillRectangle(_res.Brush(160, 6, 10, 8), bx, y + 4f, bw, 7f);
            g.FillRectangle(_res.Brush(c), bx, y + 4f, bw * Math.Max(0.04f, Math.Min(1f, ratio)), 7f);
            g.FillRectangle(_res.Brush(70, 255, 255, 255), bx, y + 4f, bw * Math.Max(0.04f, Math.Min(1f, ratio)), 2f);
            g.DrawString(value, _res.Small, _res.Brush(Color.FromArgb(232, 240, 232)), new RectangleF(x + w - 44f, y, 44f, 16f), RightFmt());
        }

        private void DrawCard(Graphics g, int w, int h, GameData data, List<uint> ids, int i, bool sel, bool hover, long now)
        {
            RectangleF rc = CardRect(w, h, ids.Count, i);
            HeroRow r = data.Hero(ids[i]);
            g.FillRectangle(_res.Brush(sel ? 225 : (hover ? 205 : 180), 12, 20, 15), rc.X, rc.Y, rc.Width, rc.Height);
            if (sel) g.FillRectangle(_res.Brush(60, 120, 190, 130), rc.X, rc.Y, rc.Width, rc.Height);
            g.DrawRectangle(_res.Pen(sel ? 255 : (hover ? 170 : 90), 210, 230, 190, sel ? 2.4f : 1f), rc.X, rc.Y, rc.Width, rc.Height);

            // 그림: 카드 위쪽. 고른/마우스가 올라간 영웅은 걷는 모습으로 움직인다(미리보기).
            RectangleF art = new RectangleF(rc.X + 8, rc.Y + 8, rc.Width - 16, Math.Max(60f, rc.Height - 166f));
            DrawHeroArt(g, ids[i], art, sel || hover, now);

            float ty = art.Bottom + 4f, bx = rc.X + 10f, bw = rc.Width - 20f;
            g.DrawString(r.Name, _res.Big, _res.Brush(Color.FromArgb(255, 245, 210)), new RectangleF(rc.X, ty, rc.Width, 26), _res.Center);
            ty += 28f;
            StatBar(g, bx, ty, bw, "체력", r.MaxHp.ToString(), r.MaxHp / (float)_mxHp, Color.FromArgb(220, 96, 86));
            StatBar(g, bx, ty + 16f, bw, "공격", r.Attack.ToString(), r.Attack / (float)_mxAtk, Color.FromArgb(236, 170, 80));
            StatBar(g, bx, ty + 32f, bw, "방어", r.Defense.ToString(), r.Defense / (float)_mxDef, Color.FromArgb(96, 160, 230));
            StatBar(g, bx, ty + 48f, bw, "사거리", r.AttackRange.ToString("0.#"), (float)(r.AttackRange / _mxRange), Color.FromArgb(140, 214, 120));

            // 스킬 아이콘(마우스를 올리면 설명)
            List<uint> sk = data.SkillsOf(ids[i]);
            int n = Math.Min(MaxCardSkills, sk.Count);
            for (int k = 0; k < n; ++k)
            {
                RectangleF ir = SkillIconRect(w, h, ids.Count, i, k, n);
                bool hot = HoverSkillCard == i && HoverSkill == k;
                Bitmap ico = SkillIcons.Get(sk[k]);
                g.FillRectangle(_res.Brush(220, 20, 24, 30), ir);
                if (ico != null) g.DrawImage(ico, ir.X, ir.Y, ir.Width, ir.Height);
                else g.DrawString(data.SkillName(sk[k]).Substring(0, 1), _res.Bold, _res.Brush(Color.FromArgb(200, 220, 255)), ir, _res.Center);
                g.DrawRectangle(_res.Pen(hot ? 255 : 150, hot ? 255 : 210, hot ? 226 : 190, hot ? 150 : 120, hot ? 2f : 1f), ir.X, ir.Y, ir.Width, ir.Height);
            }
            g.DrawString((i + 1).ToString(), _res.Bold, _res.Brush(Color.FromArgb(200, 230, 230, 200)), rc.X + 8, rc.Y + 4);
        }

        private void DrawServers(Graphics g, int w, int h, long now)
        {
            ServerPanel sp = Servers;
            RectangleF r0 = ServerRect(w, h, 0);
            g.DrawString("서버 (S)", _res.Small, _res.Brush(Color.FromArgb(200, 215, 200)), r0.X, r0.Y - 20);
            int n = Math.Min(ServerRows(w, h), sp.List.Count);
            for (int i = 0; i < n; ++i)
            {
                RectangleF r = ServerRect(w, h, i);
                bool sel = i == sp.Selected, hot = i == HoverServer;
                ProbeResult pr = sp.Probe != null ? sp.Probe.Get(i) : new ProbeResult();
                g.FillRectangle(_res.Brush(sel ? 215 : (hot ? 195 : 165), 12, 20, 15), r);
                if (sel) g.FillRectangle(_res.Brush(55, 120, 190, 130), r);
                g.DrawRectangle(_res.Pen(sel ? 255 : (hot ? 180 : 100), 210, 230, 190, sel ? 2f : 1f), r.X, r.Y, r.Width, r.Height);
                Color dot;
                switch (pr.State)
                {
                    case ProbeState.Online: dot = Color.FromArgb(90, 224, 112); break;
                    case ProbeState.Offline: dot = Color.FromArgb(232, 82, 72); break;
                    case ProbeState.Probing: dot = ((now / 300) & 1) == 0 ? Color.FromArgb(240, 210, 90) : Color.FromArgb(150, 130, 60); break;
                    default: dot = Color.FromArgb(130, 134, 130); break;
                }
                g.FillEllipse(_res.Brush(dot), r.X + 8, r.Y + r.Height / 2f - 5f, 10, 10);
                g.DrawString(sp.List[i].Name, sel ? _res.Bold : _res.Normal, _res.Brush(Color.FromArgb(sel ? 255 : 225, 245, 230, sel ? 190 : 220)), new RectangleF(r.X + 24, r.Y + 6, r.Width - 24 - 74, 20), ClipFmt());
                string st = pr.State == ProbeState.Online ? pr.Ms + "ms " + ServerProbe.Grade(pr.Ms) : (pr.State == ProbeState.Offline ? "접속 불가" : (pr.State == ProbeState.Probing ? "확인 중" : ""));
                Color sc = pr.State == ProbeState.Online ? (pr.Ms < 60 ? Color.FromArgb(150, 235, 160) : (pr.Ms < 150 ? Color.FromArgb(235, 226, 150) : Color.FromArgb(255, 180, 130))) : (pr.State == ProbeState.Offline ? Color.FromArgb(255, 150, 140) : Color.FromArgb(180, 190, 180));
                g.DrawString(st, _res.Small, _res.Brush(sc), new RectangleF(r.X + r.Width - 82, r.Y + 8, 76, 16), RightFmt());
            }
        }

        private void DrawHeroArt(Graphics g, uint id, RectangleF art, bool sel, long now)
        {
            AssetSet s = AssetSprites.Find(false, id);
            float footY = art.Bottom - 10;
            g.FillEllipse(_res.Brush(90, 0, 0, 0), art.X + art.Width * 0.22f, footY - 7, art.Width * 0.56f, 14);          // 발 밑 그림자
            if (s == null || s.Frames == null || s.Frames.Length == 0)
            {
                Color accent = WorldRenderer.HeroColor(id);
                float d = Math.Min(art.Width, art.Height) * 0.6f;
                g.FillEllipse(_res.Brush(accent), art.X + (art.Width - d) / 2, footY - d, d, d);
                return;
            }
            int n = s.Frames.Length;
            Bitmap bmp = s.Frames[sel ? (int)((now / 140) % n) : 0];
            float halfPx = s.Look.HalfW / s.Scale, hPx = s.Look.H / s.Scale;
            RectangleF src = new RectangleF(s.Cx - halfPx, s.Foot - hPx, halfPx * 2f, hPx);
            if (src.X < 0) { src.Width += src.X; src.X = 0; }
            if (src.Right > bmp.Width) src.Width = bmp.Width - src.X;
            float k = Math.Min((art.Width - 6f) / src.Width, (art.Height - 16f) / src.Height);
            float dw = src.Width * k, dh = src.Height * k;
            RectangleF dst = new RectangleF(art.X + (art.Width - dw) / 2f, footY - dh, dw, dh);
            InterpolationMode im = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (sel) g.DrawImage(bmp, Rectangle.Round(dst), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
            else g.DrawImage(bmp, Rectangle.Round(dst), (int)src.X, (int)src.Y, (int)src.Width, (int)src.Height, GraphicsUnit.Pixel, _dim);
            g.InterpolationMode = im;
        }
    }
}
