//***************************************************************************
// StartScreen.cs : 클라이언트 시작 화면. 로딩바 -> 영웅 선택 -> 게임 시작 버튼.
// System.Drawing 만 쓰므로 PNG 스냅샷 도구도 같은 코드를 쓴다. 입력 처리는 GameCanvas 가 하고, 여기는 그리기와 판정 영역만 맡는다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GameViewer.App.Forest;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Model3D;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;
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
                new float[] { 0.82f, 0, 0, 0, 0 }, new float[] { 0, 0.82f, 0, 0, 0 }, new float[] { 0, 0, 0.86f, 0, 0 },
                new float[] { 0, 0, 0, 0.92f, 0 }, new float[] { 0, 0, 0, 0, 1 } });
            GfxAttr.SetColorMatrix(_dim, m);
        }

        public void Dispose() { _dim.Dispose(); }

        // ------------------------------------------------------------------ 판정 영역

        private const float CardGap = 16f;

        /// <summary>3D 숲이 켜져 있으면 왼쪽 일부를 비워 고른 영웅이 숲 길에 서 있게 한다(카드, 시작 버튼, 서버 칸은 나머지 오른쪽 영역에 놓는다). 숲이 없으면 0.</summary>
        public static float Inset(int w)
        {
            if (!ForestBackdrop.Enabled || !ForestBackdrop.IsReady || w < 900) return 0f;
            return Math.Min(w * 0.30f, 440f);
        }

        private static float CardW(int w, int n) { return Math.Max(120f, Math.Min(230f, (w - 80f - (n - 1) * CardGap) / Math.Max(1, n))); }
        private static float CardH(int h) { return Math.Max(270f, Math.Min(420f, h * 0.56f)); }
        private static float CardTop(int h) { return Math.Max(90f, h * 0.22f); }

        /// <summary>영웅 카드 i 의 화면 영역.</summary>
        public static RectangleF CardRect(int w, int h, int n, int i)
        {
            float ox = Inset(w), aw = w - ox;
            float cw = CardW((int)aw, n), total = n * cw + (n - 1) * CardGap;
            return new RectangleF(ox + (aw - total) / 2f + i * (cw + CardGap), CardTop(h), cw, CardH(h));
        }

        private const float StartShiftLeft = 100f;

        /// <summary>"게임 시작" 버튼 영역.</summary>
        public static RectangleF StartRect(int w, int h)
        {
            float y = Math.Min(h - 96f, CardTop(h) + CardH(h) + 28f);
            float ox = Inset(w);
            float x = Math.Max(ox + 8f, ox + (w - ox - 240f) / 2f - StartShiftLeft);       // 가운데에서 왼쪽으로 StartShiftLeft 만큼 옮긴다(서버 칸과 맵 칸도 이 버튼을 따라간다)
            return new RectangleF(x, y, 240f, 50f);
        }

        /// <summary>맵(존) 선택 칸 i: "게임 시작" 버튼 오른쪽에 나란히 놓는다.</summary>
        public static RectangleF ZoneRect(int w, int h, int i)
        {
            RectangleF b = StartRect(w, h);
            float x0 = Math.Max(b.Right + 4f, Math.Min(b.Right + 28f, w - 8f - (6 * 78f - 6f)));
            int perRow = Math.Max(2, Math.Min(6, (int)((w - 8f - x0 + 6f) / 78f)));        // 창이 좁으면 줄을 바꾼다
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

        private bool _forestDrawn;
        private static uint _bigHero;           // 이번 그리기에서 숲 위에 얹을 3D 영웅(0 없음)

        private void Background(IGfx g, int w, int h, int dark)
        {
            _forestDrawn = false;
            // 배경 우선순위: 3D 숲(OpenGL) > Assets/backdrop 이미지 > 초록 그라데이션. 숲을 만드는 동안(Pending)과 GL 을 못 쓸 때(Unavailable)는 다음 것으로 넘어간다.
            if (ForestBackdrop.Draw(g, w, h) == ForestResult.Drawn) { g.FillRectangle(_res.Brush(Math.Max(40, dark - 90), 4, 10, 6), 0, 0, w, h); _forestDrawn = true; }
            else if (Backdrop.Draw(g, w, h)) g.FillRectangle(_res.Brush(dark, 4, 10, 6), 0, 0, w, h);
            else
            {
                using (LinearGradientBrush b = GfxBrush.Linear(new Rectangle(0, 0, w, h + 1), Color.FromArgb(22, 54, 34), Color.FromArgb(6, 14, 10), LinearGradientMode.Vertical))
                    g.FillRectangle(b, 0, 0, w, h);
            }
        }

        private void Title(IGfx g, int w, int h)
        {
            RectangleF r = new RectangleF(0, Math.Max(14f, h * 0.05f), w, 50);
            _res.OutlinedText(g, "GAME VIEWER", _res.Huge, Color.FromArgb(255, 235, 150), w / 2f, r.Y, TitleFmt(w));
            g.DrawString("MMORPG 필드 전투 뷰어", _res.Normal, _res.Brush(Color.FromArgb(210, 225, 210)), new RectangleF(0, r.Y + 42, w, 22), _res.Center);
        }

        /// <summary>3D 숲을 쓰지 못할 때(사용 안 함/오류) 원인을 화면 왼쪽 아래에 작게 적는다. 정상이면 아무것도 그리지 않는다.</summary>
        private void ForestNote(IGfx g, int w, int h)
        {
            string st = ForestBackdrop.Status;
            if (!ForestBackdrop.Enabled || ForestBackdrop.IsReady || st == null || !st.StartsWith("사용 안 함", StringComparison.Ordinal)) return;
            g.DrawString("3D 숲 " + st + " (ForestGL.log)", _res.Small, _res.Brush(Color.FromArgb(220, 255, 190, 150)), 10, h - 22);
        }

        private StringFormat _titleFmt;
        private StringFormat TitleFmt(int w)
        {
            if (_titleFmt == null) { _titleFmt = new StringFormat(); _titleFmt.Alignment = StringAlignment.Center; }
            return _titleFmt;
        }

        // ------------------------------------------------------------------ 로딩

        /// <summary>로딩 화면. shown 은 화면에 보이는 진행률(0..1), label 은 지금 읽는 것.</summary>
        public void DrawLoading(IGfx g, int w, int h, float shown, string label, long now)
        {
            shown = Math.Max(0f, Math.Min(1f, shown));
            Background(g, w, h, 150);
            Title(g, w, h);
            ForestNote(g, w, h);
            float bw = Math.Min(560f, w - 80f), bh = 18f, x = (w - bw) / 2f, y = h * 0.60f;
            g.DrawString(label.Length > 0 ? "불러오는 중 - " + label : (shown >= 1f ? "준비 완료" : "불러오는 중"), _res.Normal, _res.Brush(Color.FromArgb(235, 240, 235)), x, y - 30);
            g.DrawString((int)(shown * 100f) + "%", _res.Bold, _res.Brush(Color.FromArgb(255, 235, 150)), new RectangleF(x, y - 30, bw, 22), RightFmt());
            g.FillRectangle(_res.Brush(170, 8, 12, 10), x - 3, y - 3, bw + 6, bh + 6);
            g.DrawRectangle(_res.Pen(200, 200, 220, 190, 1.2f), x - 3, y - 3, bw + 6, bh + 6);
            float fw = bw * shown;
            if (fw >= 1f)
            {
                using (LinearGradientBrush b = GfxBrush.Linear(new RectangleF(x, y, Math.Max(2f, fw), bh), Color.FromArgb(150, 230, 120), Color.FromArgb(60, 160, 90), LinearGradientMode.Vertical))
                    g.FillRectangle(b, x, y, fw, bh);
                g.FillRectangle(_res.Brush(60, 255, 255, 255), x, y, fw, 5);
                // 지나가는 빛줄기
                float sx = x + (float)((now % 1400) / 1400.0) * (fw + 60f) - 40f;
                GfxState old = g.Save();
                g.SetClip(new RectangleF(x, y, fw, bh));
                g.FillRectangle(_res.Brush(55, 255, 255, 255), sx, y, 26, bh);
                g.Restore(old);
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

        /// <summary>선택된 영웅을 이번 그리기의 3D 숲 위에 세우도록 부탁해 둔다(Background 보다 먼저). 영웅 템플릿 1~4 = 전사/궁수/마법사/창기사, 그 밖은 영웅 없이 숲만 보인다.</summary>
        private static void RequestHero(List<uint> ids, int sel, int w)
        {
            _bigHero = 0;
            float ox = Inset(w);
            if (ox <= 0f || ids.Count == 0) return;
            uint id = ids[Math.Max(0, Math.Min(ids.Count - 1, sel))];
            if (id < 1 || id > 4) return;
            ModelSpec spec = MonsterModels.Get(false, id);
            bool body3d = spec != null && MonsterGl.Usable;                        // glTF 영웅을 쓸 수 있으면 처음부터 절차적 몸은 빼고 마법진만(읽는 동안은 빈 숲)
            if (spec != null) { _bigHero = id; _bigHeroSeen = true; }
            ForestBackdrop.ShowHero((int)id - 1, (ox * 0.5f) / Math.Max(1, w) * 2f - 1f, body3d);
        }

        // ------------------------------------------------------------------ 왼쪽 영웅 회전 (마우스로 끌면 360도)

        private const float DragRadPerPx = (float)(2 * Math.PI) / 540f;       // 540px 끌면 한 바퀴
        private float _heroYaw; private bool _heroDrag; private int _heroDragX;
        public bool HeroDragging { get { return _heroDrag; } }

        /// <summary>왼쪽 영웅이 서 있는 자리(숲이 비워 둔 왼쪽 띠)인지. 3D 영웅이 보일 때만 true.</summary>
        public static bool HeroViewHit(int w, int h, float x, float y)
        {
            float ox = Inset(w);
            return ox > 0f && x >= 0f && x < ox && y > h * 0.05f && y < h * 0.98f && _bigHeroSeen;
        }
        private static bool _bigHeroSeen;      // 3D 영웅이 한 번이라도 그려졌는지(그리는 사이 잠깐 비는 일 없이 판정하려고 따로 둔다)
        /// <summary>마우스 휠로도 돌린다(한 칸 = 15도).</summary>
        public void RotateHero(int wheelDelta) { _heroYaw += (wheelDelta / 120f) * (float)(Math.PI / 12.0); }
        public void BeginHeroDrag(int x) { _heroDrag = true; _heroDragX = x; }
        public void HeroDrag(int x) { if (!_heroDrag) return; _heroYaw += (x - _heroDragX) * DragRadPerPx; _heroDragX = x; }
        public void EndHeroDrag() { _heroDrag = false; }

        private readonly Dictionary<uint, Entity> _bigEnt = new Dictionary<uint, Entity>();
        private long _bigNext; private uint _bigLast;

        /// <summary>숲 길에 선 자리(마법진 한가운데)에 glTF 영웅을 얹는다. 크기와 발 위치는 숲 카메라 투영에서 얻고, 5초 남짓마다 공격 동작(마법진 충격 고리와 같은 박자)을 한다.</summary>
        private void DrawBigHero(IGfx g, int w, int h, long now)
        {
            if (_bigHero == 0 || !_forestDrawn) return;
            uint id = _bigHero;
            AssetSet s = AssetSprites.Find(false, id); ModelSpec spec = MonsterModels.Get(false, id);
            float fx, fy, hy;
            if (s == null || spec == null || !ForestBackdrop.HeroScreen(out fx, out fy, out hy)) return;
            Entity e;
            if (!_bigEnt.TryGetValue(id, out e)) { e = new Entity(); e.Id = 0xF1000000UL + id; e.TemplateId = id; e.Type = (byte)EntityType.Player; _bigEnt[id] = e; }
            if (_bigLast != id) { _bigLast = id; _heroYaw = 0f; _bigNext = now + (long)(HeroShowcase.AtkStart * 1000f); }
            if (now >= _bigNext) { e.AttackAnimMs = now; _bigNext = now + (long)(HeroShowcase.Period * 1000f); }          // 마법진 충격 고리와 같은 박자로 한 바퀴마다 한 번
            float ya = 0.25f + _heroYaw; e.FaceX = (float)Math.Sin(ya); e.FaceY = (float)Math.Cos(ya);                                                                                         // 카메라를 거의 정면으로 바라본다
            float hPx = (fy - hy) * h;                                                                                              // 모델 키 3 단위가 화면에서 차지하는 높이(px)
            if (hPx < 40f) return;
            float H = Math.Max(0.5f, s.Look.H);
            double ppu = hPx * 1.4 / H;                                                                                           // 영웅마다 키가 달라도(2.8~3.6) 화면에서 같은 높이로 맞춘다(머리가 잘리지 않게 위에 여유)
            g.FillEllipse(_res.Brush(60, 0, 0, 0), fx * w - hPx * 0.2f, fy * h - hPx * 0.035f, hPx * 0.4f, hPx * 0.07f);          // 발 밑 그림자
            Bitmap bmp = MonsterGl.Frame(e, spec, H, ppu, now, false);
            if (bmp == null) return;
            float cw = MonsterCamera.CellWorld(H) * (float)ppu;
            InterpolationMode im = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, fx * w - cw / 2f, fy * h - cw * MonsterCamera.FootRowFrac, cw, cw);
            g.InterpolationMode = im;
        }

        /// <summary>영웅 선택 화면. sel 은 선택된 카드, hover 는 마우스가 올라간 카드(-1 없음), target 은 접속 대상 설명.</summary>
        public void DrawSelect(IGfx g, int w, int h, GameData data, int sel, int hover, string target, long now) { DrawSelect(g, w, h, data, sel, hover, target, now, uint.MaxValue); }

        /// <summary>zoneId 가 uint.MaxValue(존 없음)가 아니면 시작 버튼 옆에 맵 선택 칸을 그린다. hover: -2 시작 버튼, -3-i 맵 칸 i.</summary>
        public void DrawSelect(IGfx g, int w, int h, GameData data, int sel, int hover, string target, long now, uint zoneId)
        {
            List<uint> ids = data.HeroIds();
            ComputeMaxes(data, ids);
            RequestHero(ids, sel, w);
            Background(g, w, h, 130);
            DrawBigHero(g, w, h, now);
            Title(g, w, h);
            ForestNote(g, w, h);
            if (ids.Count == 0)
            {
                g.DrawString("GameData 에 Hero 시트가 없어 서버 기본 영웅으로 시작합니다", _res.Normal, _res.Brush(Color.FromArgb(255, 200, 170)), new RectangleF(0, h * 0.4f, w, 24), _res.Center);
            }
            else
            {
                g.DrawString("영웅을 선택하세요", _res.Big, _res.Brush(Color.FromArgb(240, 245, 235)), new RectangleF(Inset(w), CardTop(h) - 38, w - Inset(w), 30), _res.Center);
                sel = Math.Max(0, Math.Min(ids.Count - 1, sel));
                for (int i = 0; i < ids.Count; ++i) DrawCard(g, w, h, data, ids, i, i == sel, i == hover, now);
            }
            RectangleF b = StartRect(w, h);
            bool hot = hover == -2;
            using (LinearGradientBrush br = GfxBrush.Linear(b, hot ? Color.FromArgb(150, 235, 130) : Color.FromArgb(110, 200, 100), hot ? Color.FromArgb(70, 170, 90) : Color.FromArgb(50, 140, 70), LinearGradientMode.Vertical))
                g.FillRectangle(br, b.X, b.Y, b.Width, b.Height);
            g.DrawRectangle(_res.Pen(230, 230, 255, 200, 1.6f), b.X, b.Y, b.Width, b.Height);
            g.DrawString("게임 시작", _res.Big, _res.Brush(Color.FromArgb(14, 30, 16)), b, _res.Center);
            if (Servers != null && Servers.List.Count > 0) DrawServers(g, w, h, now);
            if (zoneId != uint.MaxValue)
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
                    using (LinearGradientBrush br = GfxBrush.Linear(zr, on ? c1 : Color.FromArgb(60, 70, 64), on ? c2 : Color.FromArgb(26, 32, 28), LinearGradientMode.Vertical))
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
        private void StatBar(IGfx g, float x, float y, float w, string label, string value, float ratio, Color c)
        {
            g.DrawString(label, _res.Small, _res.Brush(Color.FromArgb(190, 205, 192)), x, y);
            float bx = x + 34f, bw = w - 34f - 44f;
            g.FillRectangle(_res.Brush(160, 6, 10, 8), bx, y + 4f, bw, 7f);
            g.FillRectangle(_res.Brush(c), bx, y + 4f, bw * Math.Max(0.04f, Math.Min(1f, ratio)), 7f);
            g.FillRectangle(_res.Brush(70, 255, 255, 255), bx, y + 4f, bw * Math.Max(0.04f, Math.Min(1f, ratio)), 2f);
            g.DrawString(value, _res.Small, _res.Brush(Color.FromArgb(232, 240, 232)), new RectangleF(x + w - 44f, y, 44f, 16f), RightFmt());
        }

        private void DrawCard(IGfx g, int w, int h, GameData data, List<uint> ids, int i, bool sel, bool hover, long now)
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

        private void DrawServers(IGfx g, int w, int h, long now)
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

        private readonly Dictionary<uint, Entity> _heroEnt = new Dictionary<uint, Entity>();

        /// <summary>영웅 카드의 3D 모습. 카드마다 가짜 엔티티 하나로 MonsterGl 에 맡긴다(대기 동작, 선택된 카드는 살짝 좌우로 돈다). 모델을 읽는 중이면 false(색 원).</summary>
        private bool Draw3DHero(IGfx g, uint id, AssetSet s, RectangleF art, float footY, bool sel, long now)
        {
            ModelSpec spec = MonsterModels.Get(false, id);
            if (spec == null) return false;
            Entity e;
            if (!_heroEnt.TryGetValue(id, out e)) { e = new Entity(); e.Id = 0xF0000000UL + id; e.TemplateId = id; e.Type = (byte)EntityType.Player; _heroEnt[id] = e; }
            e.FaceX = sel ? 0.55f * (float)Math.Sin(now / 700.0) : 0f; e.FaceY = 1f;
            float h = Math.Max(0.5f, s.Look.H);
            double ppu = (art.Height - 16f) / h;
            Bitmap bmp = MonsterGl.Frame(e, spec, h, ppu, now, false);
            if (bmp == null) return false;
            g.FillEllipse(_res.Brush(90, 0, 0, 0), art.X + art.Width * 0.22f, footY - 7, art.Width * 0.56f, 14);          // 발 밑 그림자(모습이 나올 때만)
            float cw = MonsterCamera.CellWorld(h) * (float)ppu;
            RectangleF dst = new RectangleF(art.X + (art.Width - cw) / 2f, footY - cw * MonsterCamera.FootRowFrac, cw, cw);
            InterpolationMode im = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (sel) g.DrawImage(bmp, dst.X, dst.Y, dst.Width, dst.Height);
            else g.DrawImage(bmp, dst, new RectangleF(0, 0, bmp.Width, bmp.Height), GraphicsUnit.Pixel, _dim);
            g.InterpolationMode = im;
            return true;
        }

        private void DrawHeroArt(IGfx g, uint id, RectangleF art, bool sel, long now)
        {
            AssetSet s = AssetSprites.Find(false, id);
            float footY = art.Bottom - 10;
            if (s != null && s.Frames == null && MonsterModels.Has(false, id) && MonsterGl.Usable) { Draw3DHero(g, id, s, art, footY, sel, now); return; }       // 3D 전용 영웅: OpenGL 로 그린다(읽는 동안은 아무것도 그리지 않는다: 색 원/그림자 없음)
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
