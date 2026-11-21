//***************************************************************************
// GameCanvas.cs : 게임 화면을 그리는 더블 버퍼 컨트롤 + 마우스/키보드 입력 처리.
// 로직은 Core(ViewerSession/AutoPilot)에 있고, 이 클래스는 입력을 명령으로 바꾸고 SceneRenderer 로 그리기만 한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using GameViewer.App.Render;
using GameViewer.Core;
using GameViewer.Core.Audio;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Net;
using GameViewer.Core.Ui;
using GameViewer.Core.World;

namespace GameViewer.App
{
    public sealed class GameCanvas : Control
    {
        private readonly SceneRenderer _renderer = new SceneRenderer();
        private readonly ViewState _view = new ViewState();
        private readonly UiManager _ui = new UiManager();           // 가방/상점/우편/환경설정 같은 새 창(Core/Ui). 입력은 이쪽이 먼저 받는다.
        private readonly GdiUiCanvas _uic = new GdiUiCanvas();
        private readonly UiDemoData _uiData = new UiDemoData();     // 서버 연동 전 데모 데이터
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private ViewerSession _session;
        private AutoPilot _pilot;
        private DemoDirector _demo;
        private bool _follow = true;
        private double _targetScale;                    // 확대 목표(부드럽게 따라간다)
        private bool _rightDown;
        private int _fpsFrames; private long _fpsLast;
        private readonly ViewerConfig _cfg;
        private GameData _data;                        // 로딩이 끝나면 채운다
        private readonly StartupLoader _loader;
        private enum Phase { Loading, Select, Playing, Failed }
        private Phase _phase = Phase.Loading;
        private StartScreen _start;
        private float _loadShown;                       // 화면에 보이는 진행률(실제 진행률을 부드럽게 따라간다)
        private int _selIndex, _hoverCard = -1;         // 영웅 선택 화면: 고른 카드 / 마우스가 올라간 카드(-2 = 시작 버튼)
        private readonly bool _demoMode;
        private readonly SoundFx _sfx = new SoundFx();
        private readonly HashSet<ViewEffect> _sfxSeen = new HashSet<ViewEffect>();
        private bool _sfxPrimed;
        private readonly HeartbeatClock _heart = new HeartbeatClock();
        private readonly string _settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private long _introSeen = -1;                   // 마지막으로 연출을 시작한 입장 시각(같은 입장에 연출을 두 번 하지 않게)
        private int _reconnectsSeen;
        private readonly TextBox _chat = new TextBox();
        private readonly Stopwatch _drawSw = new Stopwatch();
        private bool _mapDrag, _mapMoved; private int _mapDownX, _mapDownY;
        private readonly BgmPlayer _bgm = new BgmPlayer();
        private readonly BgmDirector _bgmDir = new BgmDirector();
        private ServerPanel _servers;                   // 시작 화면의 서버 선택(서버 모드에서만)
        private ServerProbe _probe;

        public GameCanvas(ViewerConfig cfg, StartupLoader loader, bool demoMode)
        {
            _cfg = cfg; _loader = loader; _demoMode = demoMode;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            TabStop = true;
            _view.Scale = cfg.PixelsPerUnit; _targetScale = _view.Scale;
            if (cfg.Renderer == "skia")
            {
#if SKIA
                try { _skia = new SkiaFrame(); }
                catch (Exception ex) { Console.Error.WriteLine("Skia 를 시작하지 못해 GDI+ 로 진행합니다: " + ex.Message); }
#else
                Console.Error.WriteLine("--renderer skia 는 앱을 -p:UseSkia=true 로 빌드해야 합니다. GDI+ 로 진행합니다.");
#endif
            }
            _start = new StartScreen(_renderer.Res);
            _view.Settings = ViewSettings.Load(_settingsPath);
            ApplySettings(false);
            _uiData.Register(_ui, _view.Settings, delegate { ApplySettings(true); });
            SetupDock();
            _ui.WindowToggled += delegate { if (_sfx.Enabled) _sfx.Play(SfxKind.UiTick, 0.8f); };
            _chat.Visible = false; _chat.MaxLength = 120; _chat.BorderStyle = BorderStyle.FixedSingle;
            _chat.BackColor = Color.FromArgb(24, 30, 28); _chat.ForeColor = Color.White;
            _chat.Font = new Font(_renderer.Res.FontFamily, 9.5f);
            _chat.KeyDown += ChatKeyDown;
            _chat.LostFocus += delegate { if (_view.ChatActive) CloseChat(); };
            Controls.Add(_chat);
        }

        /// <summary>전투 화면 오른쪽 위의 창 열기 단추 줄. 단축키 표시는 키 설정을 따라간다.</summary>
        private void SetupDock()
        {
            Func<BindAction, Func<string>> hk = delegate (BindAction a) { return delegate { return KeyBindings.KeyName(_view.Settings.Keys.Get(a)); }; };
            UiDock d = _ui.Dock;
            d.Add("가방", UiIconKind.Bag, delegate { _ui.Toggle(InventoryWindow.WindowId); }, hk(BindAction.Inventory), delegate { return _ui.IsOpen(InventoryWindow.WindowId); });
            d.Add("스킬", UiIconKind.Skill, delegate { if (_view.SkillBookOpen) _view.SkillBookOpen = false; else OpenSkillBook(); }, hk(BindAction.SkillBook), delegate { return _view.SkillBookOpen; }, null, 7);
            d.Add("우편함", UiIconKind.Mail, delegate { _ui.Toggle(MailWindow.WindowId); }, hk(BindAction.Mail), delegate { return _ui.IsOpen(MailWindow.WindowId); }, delegate { return _uiData.Mailbox.Unread; });
            d.Add("상점", UiIconKind.Shop, delegate { _ui.Toggle(ShopWindow.WindowId); }, hk(BindAction.Shop), delegate { return _ui.IsOpen(ShopWindow.WindowId); });
            d.Add("환경설정", UiIconKind.Gear, delegate { _ui.Toggle(SettingsWindow.WindowId); }, null, delegate { return _ui.IsOpen(SettingsWindow.WindowId); });
        }

        public long Now { get { return _clock.ElapsedMilliseconds + 1000; } }
        public ViewerSession Session { get { return _session; } }

        public void StartSession() { StartSession(float.NaN, float.NaN); }

        /// <summary>arrivalX/Y 가 숫자이면 데모 영웅을 그 지점에서 시작한다(존 관문을 넘어온 경우).</summary>
        public void StartSession(float arrivalX, float arrivalY)
        {
            if (_session != null) { _session.Leave(); _session.Dispose(); }
            _session = new ViewerSession(_cfg, _data);
            _pilot = new AutoPilot(_session);
            _demo = null;
            _view.SelectedId = 0;
            _sfxSeen.Clear(); _sfxPrimed = false;
            _session.AutoReconnect = !_demoMode;            // 서버에 붙는 실행만 끊겼을 때 자동으로 다시 접속한다
            _introSeen = -1; _reconnectsSeen = 0; _view.IntroStartMs = -1; _view.MapOpen = false; _view.ChatActive = false; _chat.Visible = false;
            if (_demoMode)
            {
                _demo = new DemoDirector(_session);
                if (!float.IsNaN(arrivalX)) _demo.SetArrival(arrivalX, arrivalY);
                _demo.Start(Now);
            }
            else _session.Start(Now);
            if (_cfg.Auto) _pilot.SetAuto(true);
            _follow = true;
        }

        // ------------------------------------------------------------------ 시작 화면 (로딩바 -> 영웅 선택 -> 시작)

        private void TickStart()
        {
            if (_phase == Phase.Loading)
            {
                float target = _loader.Done ? 1f : Math.Min(0.99f, _loader.Progress);
                _loadShown += (target - _loadShown) * 0.2f;
                if (target - _loadShown < 0.003f) _loadShown = target;
                if (_loader.Done && _loadShown >= 1f) FinishLoading();
            }
            else if (_phase == Phase.Select)
            {
                if (!_prewarmed) PrewarmScene();
                if (_probe != null) _probe.Tick(_clock.ElapsedMilliseconds);
                ApplyBgm();
                _bgm.Update();
            }
            Invalidate();
        }

        private bool _prewarmed;

        /// <summary>영웅 선택 화면에 처음 들어왔을 때 한 번: 버려 둘 데모 장면을 화면 밖에 몇 프레임 그려
        /// JIT 와 첫 스프라이트 읽기, 글자/펜/붓 캐시를 미리 데운다(실제 첫 프레임이 수백 ms 걸리던 것을 선택 화면 뒤로 옮긴다).</summary>
        private void PrewarmScene()
        {
            _prewarmed = true;
            try
            {
                ViewerConfig wc = new ViewerConfig(); wc.DataDir = _cfg.DataDir; wc.HeroId = _cfg.HeroId; wc.ZoneId = _cfg.ZoneId;
                using (ViewerSession s = new ViewerSession(wc, _data))
                {
                    DemoDirector demo = new DemoDirector(s);
                    long now = 100000; demo.Start(now);
                    for (int i = 0; i < 30; ++i) { now += 16; s.Update(now); demo.Update(now); }
                    AutoPilot pilot = new AutoPilot(s);
                    ViewState v = new ViewState(); v.Width = Math.Max(320, Math.Min(1280, _view.Width)); v.Height = Math.Max(200, Math.Min(720, _view.Height)); v.Scale = _view.Scale;
                    Entity me = s.World.Self;
                    if (me != null) { v.CamX = me.X; v.CamY = me.Y; }
                    using (Bitmap bmp = new Bitmap(v.Width, v.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                    using (GdiGfx g = GdiGfx.FromImage(bmp))
                        for (int k = 0; k < 3; ++k) _renderer.Draw(g, s, pilot, v, s.World.RenderTime(now + k * 16));
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("prewarm: " + ex.Message); }
        }

        /// <summary>읽기가 끝났을 때: 오류 처리 후 영웅 선택 화면으로(선택을 건너뛰는 옵션이면 바로 시작).</summary>
        private void FinishLoading()
        {
            _phase = Phase.Failed;                  // 아래 대화상자가 떠 있는 동안 타이머가 다시 들어오지 못하게
            if (_loader.Error != null || _loader.Data == null)
            {
                MessageBox.Show("시작 중 오류가 났습니다.\r\n\r\n" + (_loader.Error != null ? _loader.Error.Message : "GameData 를 읽지 못했습니다"), "GameViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseForm(); return;
            }
            _data = _loader.Data;
            List<string> assetWarn = _loader.AssetWarnings;
            foreach (string w in assetWarn) Console.Error.WriteLine("assets: " + w);
            if (assetWarn.Count > 0)
            {
                int shown = Math.Min(assetWarn.Count, 8);
                string msg = string.Join("\r\n", assetWarn.GetRange(0, shown).ToArray()) + (assetWarn.Count > shown ? "\r\n... 외 " + (assetWarn.Count - shown) + "건" : "");
                MessageBox.Show("이미지 에셋을 읽는 중 문제가 있습니다 (해당 이미지는 코드로 그린 기본 그림으로 대체됩니다).\r\n\r\n" + msg, "GameViewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (_data.Warnings.Count > 0 && _data.Heroes.Count == 0)
            {
                DialogResult r = MessageBox.Show("GameData 를 읽지 못했습니다 (이름/사거리/스킬 정보 없이 실행됩니다).\r\n\r\n폴더: " + _loader.DataDir + "\r\n" + string.Join("\r\n", _data.Warnings.ToArray()) + "\r\n\r\n계속할까요?",
                    "GameViewer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) { CloseForm(); return; }
            }
            List<uint> ids = _data.HeroIds();
            _selIndex = Math.Max(0, ids.IndexOf(_cfg.HeroId));
            SetupServers();
            if (_cfg.SkipHeroSelect || ids.Count == 0) BeginGame();
            else { _phase = Phase.Select; ApplyBgm(); }
        }

        private void CloseForm() { Form f = FindForm(); if (f != null) f.BeginInvoke(new MethodInvoker(f.Close)); }

        /// <summary>시작 화면을 마치고 게임(접속 또는 데모)을 시작한다.</summary>
        private void BeginGame()
        {
            _phase = Phase.Playing; Cursor = Cursors.Default; _start.EndHeroDrag();
            _hoverCard = -1; _start.HoverServer = -1; _start.HoverSkillCard = -1;
            if (_probe != null) _probe.Stop();
            StartSession();
        }

        /// <summary>시작 화면에서 고른 영웅으로 시작한다.</summary>
        private void StartWithSelected()
        {
            List<uint> ids = _data.HeroIds();
            if (ids.Count > 0) _cfg.HeroId = ids[Math.Max(0, Math.Min(ids.Count - 1, _selIndex))];
            BeginGame();
        }

        private void DrawStart(IGfx g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (_phase == Phase.Select)
            {
                string target = _demoMode ? "데모 모드 - 서버 없이 로컬 시뮬레이션" : "서버 " + _cfg.Host + ":" + _cfg.Port + "   존 " + _cfg.ZoneId;
                _start.Keys = _view.Settings.Keys;
                _start.DrawSelect(g, _view.Width, _view.Height, _data, _selIndex, _hoverCard, target, Now, _cfg.ZoneId);
            }
            else _start.DrawLoading(g, _view.Width, _view.Height, _loadShown, _phase == Phase.Loading ? _loader.Label : "", Now);
        }

        private void StartMouseDown(MouseEventArgs e)
        {
            if (_phase != Phase.Select || e.Button != MouseButtons.Left) return;
            int n = _data.HeroIds().Count;
            if (StartScreen.StartRect(_view.Width, _view.Height).Contains(e.X, e.Y)) { StartWithSelected(); return; }
            if (StartScreen.HeroViewHit(_view.Width, _view.Height, e.X, e.Y)) { _start.BeginHeroDrag(e.X); Capture = true; return; }          // 왼쪽 영웅: 끌어서 회전
            int zi = StartScreen.ZoneAt(_view.Width, _view.Height, e.X, e.Y);
            if (zi >= 0) { SelectZone(ZoneTheme.Zones[zi]); return; }
            if (_servers != null)
            {
                int si = StartScreen.ServerAt(_view.Width, _view.Height, _servers.List.Count, e.X, e.Y);
                if (si >= 0) { SelectServer(si); return; }
            }
            int c = StartScreen.CardAt(_view.Width, _view.Height, n, e.X, e.Y);
            if (c < 0) return;
            _selIndex = c;
            if (e.Clicks >= 2) StartWithSelected();
        }

        private void StartMouseMove(MouseEventArgs e)
        {
            if (_phase != Phase.Select) return;
            if ((e.Button & MouseButtons.Left) != 0 && (_start.HeroDragging || StartScreen.HeroViewHit(_view.Width, _view.Height, e.X, e.Y)))
            {
                if (!_start.HeroDragging) { _start.BeginHeroDrag(e.X); Capture = true; }        // 누른 이벤트를 놓쳐도 왼쪽 띠에서 끌면 바로 돈다
                _start.HeroDrag(e.X); return;
            }
            if (_start.HeroDragging) { _start.EndHeroDrag(); Capture = false; }
            Cursor = StartScreen.HeroViewHit(_view.Width, _view.Height, e.X, e.Y) ? Cursors.SizeWE : Cursors.Default;
            int n = _data.HeroIds().Count;
            int zi = StartScreen.ZoneAt(_view.Width, _view.Height, e.X, e.Y);
            _hoverCard = StartScreen.StartRect(_view.Width, _view.Height).Contains(e.X, e.Y) ? -2 : (zi >= 0 ? -3 - zi : StartScreen.CardAt(_view.Width, _view.Height, n, e.X, e.Y));
            _start.HoverServer = _servers != null ? StartScreen.ServerAt(_view.Width, _view.Height, _servers.List.Count, e.X, e.Y) : -1;
            int hc, hk;
            if (StartScreen.SkillIconAt(_view.Width, _view.Height, _data, e.X, e.Y, out hc, out hk)) { _start.HoverSkillCard = hc; _start.HoverSkill = hk; }
            else { _start.HoverSkillCard = -1; _start.HoverSkill = -1; }
        }

        // ------------------------------------------------------------------ 서버 선택 (시작 화면)

        /// <summary>서버 모드에서만 서버 목록을 만들고 각 서버의 접속 상태를 재기 시작한다. 처음 고르는 서버: 명령행으로 준 주소 > 마지막으로 고른 서버 > 설정의 Host:Port.</summary>
        private void SetupServers()
        {
            if (_demoMode) { _servers = null; _start.Servers = null; return; }
            ServerPanel sp = new ServerPanel();
            sp.List = _cfg.ServerEntries();
            int sel = -1;
            if (!_cfg.HostPortExplicit && _view.Settings.LastServer.Length > 0)
                for (int i = 0; i < sp.List.Count; ++i) if (sp.List[i].Name == _view.Settings.LastServer) sel = i;
            if (sel < 0) for (int i = 0; i < sp.List.Count; ++i) if (sp.List[i].Host == _cfg.Host && sp.List[i].Port == _cfg.Port) sel = i;
            sp.Selected = Math.Max(0, sel);
            _cfg.Host = sp.List[sp.Selected].Host; _cfg.Port = sp.List[sp.Selected].Port;
            _probe = new ServerProbe(sp.List);
            sp.Probe = _probe;
            _servers = sp; _start.Servers = sp;
        }

        private void SelectServer(int i)
        {
            if (_servers == null || i < 0 || i >= _servers.List.Count) return;
            _servers.Selected = i;
            _cfg.Host = _servers.List[i].Host; _cfg.Port = _servers.List[i].Port;
            _view.Settings.LastServer = _servers.List[i].Name;
            ApplySettings(true);
            if (_sfx.Enabled) _sfx.Play(SfxKind.UiTick, 0.6f);
        }

        private void StartKey(KeyEventArgs e)
        {
            if (_phase != Phase.Select) return;
            Keys k = e.KeyCode;
            int n = _data.HeroIds().Count;
            if (n > 0)
            {
                if (k == Keys.Left || k == Keys.Up) _selIndex = (_selIndex + n - 1) % n;
                else if (k == Keys.Right || k == Keys.Down) _selIndex = (_selIndex + 1) % n;
                else if (k >= Keys.D1 && k <= Keys.D9 && k - Keys.D1 < n) _selIndex = k - Keys.D1;
                else if (k >= Keys.NumPad1 && k <= Keys.NumPad9 && k - Keys.NumPad1 < n) _selIndex = k - Keys.NumPad1;
            }
            if (k == Keys.S && _servers != null && _servers.List.Count > 1) { SelectServer((_servers.Selected + 1) % Math.Min(StartScreen.ServerRows(ClientSize.Width, ClientSize.Height), _servers.List.Count)); return; }
            if (k == Keys.Tab || k == Keys.Z)
            {
                int cur = Array.IndexOf(ZoneTheme.Zones, _cfg.ZoneId);
                SelectZone(ZoneTheme.Zones[(cur + 1 + ZoneTheme.Zones.Length) % ZoneTheme.Zones.Length]);
            }
            if (k == Keys.Enter || k == Keys.Return || k == Keys.Space) StartWithSelected();
        }

        /// <summary>시작 화면에서 맵(존)을 바꾼다. 존마다 스폰/장애물이 달라 GameData 를 그 존 기준으로 다시 읽는다.</summary>
        private void SelectZone(uint zoneId)
        {
            if (zoneId == _cfg.ZoneId) return;
            GameData d = GameData.Load(_loader.DataDir, zoneId);
            if (d.Heroes.Count == 0 && _data.Heroes.Count > 0) return;      // 읽기 실패: 그대로 둔다
            _cfg.ZoneId = zoneId;
            _data = d;
            ZoneTheme.Current = ZoneTheme.For(zoneId);
        }

        private int _tickErrors;

        /// <summary>한 프레임 진행: 패킷 처리 -> AI -> 카메라. 어느 단계에서 예외가 나도 타이머가 죽지 않게 감싸고,
        /// 첫 오류와 이후 10초(600틱)마다 한 번씩만 로그에 남긴다(그리기 오류 처리와 같은 방식).</summary>
        public void Tick()
        {
            try { TickCore(); }
            catch (Exception ex)
            {
                ++_tickErrors;
                if (_tickErrors == 1 || _tickErrors % 600 == 0)
                {
                    Console.Error.WriteLine(ex);
                    if (_session != null && _session.World != null) _session.World.AddLog(Now, 2, "틱 오류(" + _tickErrors + "회): " + ex.GetType().Name + " " + ex.Message);
                }
                Invalidate();
            }
        }

        private void TickCore()
        {
            if (_phase != Phase.Playing) { TickStart(); return; }
            long now = Now;
            _ui.Update(now);
            _session.Update(now);
            if (_view.SkillBookOpen && _session.State != SessionState.InZone) _view.SkillBookOpen = false;       // 연결이 끊기면 창을 닫는다
            if (_demo != null) _demo.Update(now);
            ApplyServerTerrain(now);
            _pilot.Update(now);
            UpdateSeekBoss(now);
            PlayEffectSounds(now);
            UpdateGateTravel(now);
            UpdateIntro();
            _session.World.HitStopScale = _view.Settings.FeedbackScale;
            if (_bgmDir.Update(now, _session.State == SessionState.InZone, ZoneTheme.For(_session.ZoneId), BgmDirector.BossAlive(_session.World))) ApplyBgm();
            _bgm.Update();
            _view.BgmLabel = _view.Settings.BgmGain > 0f && _bgmDir.Playing ? _bgm.Now : null;

            if (_follow || _session.World.Self == null)
            {
                float x, y;
                if (_session.SelfPos(now, out x, out y))
                {
                    // 부드럽게 따라가되 너무 멀어지면 즉시 붙는다 (보정/순간이동 대응)
                    float dx = x - _view.CamX, dy = y - _view.CamY;
                    float k = (dx * dx + dy * dy) > 900f ? 1f : 0.18f;
                    _view.CamX += dx * k; _view.CamY += dy * k;
                }
            }
            if (_session.World.Find(_view.SelectedId) == null) _view.SelectedId = 0;
            if (Math.Abs(_view.Scale - _targetScale) > 0.01) _view.Scale += (_targetScale - _view.Scale) * 0.25;     // 부드러운 확대/축소
            else _view.Scale = _targetScale;

            ++_fpsFrames;
            if (now - _fpsLast >= 500) { _view.Fps = _fpsFrames * 1000.0 / (now - _fpsLast); _fpsFrames = 0; _fpsLast = now; }
            Invalidate();
        }

        /// <summary>이번 틱에 새로 생긴 연출마다 규칙(SfxRules)에 따라 효과음을 낸다. 접속 직후 이미 있던 연출은 소리 없이 넘긴다.</summary>
        private void PlayEffectSounds(long now)
        {
            WorldModel w = _session.World;
            float sx = 0, sy = 0;
            bool hasSelf = _session.SelfPos(now, out sx, out sy);
            if (!hasSelf) { sx = _view.CamX; sy = _view.CamY; }
            foreach (ViewEffect fx in w.Effects)
            {
                if (!_sfxSeen.Add(fx) || !_sfxPrimed || !_sfx.Enabled) continue;
                SfxKind kind; float gain;
                if (SfxRules.TryMap(fx, w, sx, sy, out kind, out gain)) _sfx.Play(kind, gain);
            }
            _sfxPrimed = true;
            // 저체력 심장 박동: 체력이 낮을수록 빨라진다. 죽었거나 안전하면 멈춘다.
            Entity me = w.Self;
            bool alive = me != null && !me.Dead && _session.State == SessionState.InZone;
            if (_heart.Tick(now, alive ? Tension.Level(me.Hp, me.MaxHp) : 0f, alive) && _sfx.Enabled)
                _sfx.Play(SfxKind.Heartbeat, 0.4f + 0.6f * Tension.Level(me.Hp, me.MaxHp));
            if (_sfxSeen.Count > 512)           // 지난 연출 참조가 쌓이지 않게 현재 것만 남긴다
            {
                _sfxSeen.Clear();
                foreach (ViewEffect fx in w.Effects) _sfxSeen.Add(fx);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _view.Width = Math.Max(1, ClientSize.Width);
            _view.Height = Math.Max(1, ClientSize.Height);
            // 화면 밖 비트맵에 한 프레임을 다 그린 뒤 한 번에 복사한다. 컨트롤의 Graphics 는 프레임 사이에 상태(클리핑/변환)가
            // 남을 수 있어서(특히 Mono/GDI+ 구현 차이) 렌더러가 그 영향을 받지 않게 하고, 깜빡임도 없앤다.
            if (_frame == null || _frame.Width != _view.Width || _frame.Height != _view.Height)
            {
                if (_frame != null) _frame.Dispose();
                _frame = new Bitmap(_view.Width, _view.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            }
            if (_phase != Phase.Playing)
            {
                try { using (GdiGfx g = GdiGfx.FromImage(_frame)) DrawStart(g); }
                catch (Exception ex) { if (!_paintFailed) { _paintFailed = true; Console.Error.WriteLine(ex); } }
                e.Graphics.DrawImageUnscaled(_frame, 0, 0);
                return;
            }
            try
            {
                _drawSw.Restart();
#if SKIA
                if (_skia != null)
                {
                    DrawPlayingFrame(_skia.Begin(_view.Width, _view.Height));
                    _skia.CopyTo(_frame);
                }
                else
#endif
                using (GdiGfx g = GdiGfx.FromImage(_frame)) DrawPlayingFrame(g);
                _view.FrameMs = _view.FrameMs * 0.9 + _drawSw.Elapsed.TotalMilliseconds * 0.1;
            }
            catch (Exception ex)
            {
#if SKIA
                if (_skia != null)      // Skia 로 그리다 실패하면(네이티브 라이브러리 문제 등) 이 실행은 GDI+ 로 계속한다
                {
                    _skia.Dispose(); _skia = null;
                    _session.World.AddLog(Now, 2, "Skia 그리기 오류, GDI+ 로 전환합니다: " + ex.Message); Console.Error.WriteLine(ex);
                    e.Graphics.DrawImageUnscaled(_frame, 0, 0);
                    return;
                }
#endif
                // 그리는 중 예외가 나도 창이 멈추거나 빈 화면으로 굳지 않도록 한 번만 로그에 남기고 계속 진행한다
                if (!_paintFailed) { _paintFailed = true; _session.World.AddLog(Now, 2, "그리기 오류: " + ex.Message); Console.Error.WriteLine(ex); }
            }
            e.Graphics.DrawImageUnscaled(_frame, 0, 0);
        }
        private Bitmap _frame;
        private bool _paintFailed;
#if SKIA
        private SkiaFrame _skia;            // --renderer skia 일 때만 만든다
#endif

        /// <summary>한 프레임(월드 -> 새 UI 창 -> 관문 페이드)을 g 에 그린다. GDI+ 와 Skia 가 같은 순서를 쓴다.</summary>
        private void DrawPlayingFrame(IGfx g)
        {
            _renderer.Draw(g, _session, _pilot, _view, _session.World.RenderTime(Now));
            _ui.Resize(_view.Width, _view.Height);
            _uic.Begin(g);
            try { _ui.Draw(_uic); } finally { _uic.End(); }
            DrawGateFade(g);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent) { /* 전체를 직접 그린다 */ }

        // ------------------------------------------------------------------ 마우스

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (_phase != Phase.Playing) { StartMouseDown(e); return; }
            UpdateMouse(e.X, e.Y);
            long now = Now;
            if (_ui.PointerDown(e.X, e.Y, UiBtn(e.Button))) return;
            if (_view.KeysOpen) { if (e.Button == MouseButtons.Left) ClickKeys(e.X, e.Y); return; }
            if (_view.SettingsOpen) { if (e.Button == MouseButtons.Left && !ClickSettings(e.X, e.Y)) { } return; }
            if (_view.MapOpen)
            {
                if (e.Button == MouseButtons.Left) { _mapDrag = true; _mapMoved = false; _mapDownX = e.X; _mapDownY = e.Y; }
                return;
            }
            if (e.Button == MouseButtons.Left && _view.LogMode == 1 && ClickLogTab(e.X, e.Y)) return;
            if (_view.HeroSelectOpen)
            {
                if (e.Button == MouseButtons.Left && !ClickHeroSelect(e.X, e.Y)) _view.HeroSelectOpen = false;
                return;
            }
            if (_view.ZoneSelectOpen)
            {
                if (e.Button == MouseButtons.Left && !ClickZoneSelect(e.X, e.Y)) _view.ZoneSelectOpen = false;
                return;
            }
            if (_view.SkillBookOpen)
            {
                if (e.Button == MouseButtons.Left && !ClickSkillBook(e.X, e.Y)) _view.SkillBookOpen = false;
                return;
            }
            if (e.Button == MouseButtons.Left && ClickMiniMap(e.X, e.Y, now)) return;
            if (e.Button == MouseButtons.Left) { int ss = SkillSlotAt(e.X, e.Y); if (ss > 0) { if (CanDragSlot(ss)) _view.DragSlot = ss; else if (_session.World.SkillBookKnown && _session.MySkills[ss - 1] == 0 && ss > ViewerSession.FixedSlots && _session.SlotLockLevel(ss) == 0) OpenSkillBook(); return; } }
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                Entity hit = PickEntity(e.X, e.Y, now);
                if (hit != null && hit.Id != _session.World.SelfId)
                {
                    _view.SelectedId = hit.Id;
                    if (hit.IsMonster && !hit.Dead) _pilot.ChaseTarget(hit.Id);
                }
                else
                {
                    if (e.Button == MouseButtons.Left) _view.SelectedId = 0;
                    _pilot.SetAuto(false);
                    _seekBoss = false; _session.MoveTo(_view.MouseWorldX, _view.MouseWorldY, now, true);
                }
                if (e.Button == MouseButtons.Right) _rightDown = true;
            }
            base.OnMouseDown(e);
        }

        /// <summary>미니맵을 클릭하면 그 위치로 이동한다 (자동 전투는 끈다).</summary>
        private bool ClickMiniMap(int sx, int sy, long now)
        {
            RectangleF r = HudRenderer.MiniMapRect(_data, _view);
            if (!r.Contains(sx, sy)) return false;
            float wx = _data.WorldMinX + (sx - r.X) / r.Width * (_data.WorldMaxX - _data.WorldMinX);
            float wy = _data.WorldMinY + (sy - r.Y) / r.Height * (_data.WorldMaxY - _data.WorldMinY);
            _pilot.SetAuto(false);
            _seekBoss = false; _session.MoveTo(wx, wy, now, true);
            return true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_phase != Phase.Playing) { StartMouseMove(e); return; }
            UpdateMouse(e.X, e.Y);
            if (_ui.PointerMove(e.X, e.Y)) { _view.HoverId = 0; return; }
            if (_view.MapOpen) { DragMap(e.X, e.Y); return; }
            Entity hover = PickEntity(e.X, e.Y, Now);
            _view.HoverId = hover != null ? hover.Id : 0;
            // 우클릭을 누르고 있을 때만 커서 쪽으로 이동한다(마우스를 그냥 움직일 때 이동 요청이 나가면 서버가 시전을 취소한다).
            if (_rightDown && _pilot.Mode == PilotMode.Off)
            {
                _seekBoss = false;
                _session.MoveTo(_view.MouseWorldX, _view.MouseWorldY, Now, false);
            }
            base.OnMouseMove(e);
        }

        /// <summary>스킬바에서 (sx,sy) 아래에 있는 스킬 칸 번호(1..), 없거나 기본 공격 칸이면 0.</summary>
        private int SkillSlotAt(int sx, int sy)
        {
            int count = _session.MySkills.Count, n = Math.Min(9, count);
            for (int i = 1; i <= n; ++i) if (HudRenderer.SkillSlotRect(_view, count, i).Contains(sx, sy)) return i;
            return 0;
        }

        /// <summary>끌기를 시작할 수 있는 칸인가: 스킬이 있는 칸이고, 서버 현황이 있으면 상점 칸(7·8)만 옮길 수 있다.</summary>
        private bool CanDragSlot(int slot)
        {
            List<uint> sk = _session.MySkills;
            if (slot < 1 || slot > sk.Count || sk[slot - 1] == 0) return false;
            return !_session.World.SkillBookKnown || slot > ViewerSession.FixedSlots;
        }

        private static int UiBtn(MouseButtons b) { return b == MouseButtons.Left ? 0 : (b == MouseButtons.Right ? 1 : 2); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_start.HeroDragging) { _start.EndHeroDrag(); Capture = false; return; }
            if (_phase == Phase.Playing && _ui.PointerUp(e.X, e.Y, UiBtn(e.Button))) return;
            if (_view.DragSlot > 0 && e.Button == MouseButtons.Left)
            {
                int to = SkillSlotAt(e.X, e.Y);
                if (to > 0 && to != _view.DragSlot && _session.SwapSkillSlots(_view.DragSlot - 1, to - 1) && _sfx.Enabled) _sfx.Play(SfxKind.UiTick, 0.8f);
                _view.DragSlot = -1;
                return;
            }
            if (e.Button == MouseButtons.Right) _rightDown = false;
            if (_mapDrag && e.Button == MouseButtons.Left)
            {
                _mapDrag = false;
                if (!_mapMoved) ClickMap(e.X, e.Y);
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _ui.PointerMove(-1000, -1000); _view.HasMouse = false; _view.DragSlot = -1; _rightDown = false; base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_phase == Phase.Select) { if (StartScreen.HeroViewHit(_view.Width, _view.Height, e.X, e.Y)) _start.RotateHero(e.Delta); return; }
            if (_phase != Phase.Playing) return;
            if (_ui.Wheel(e.X, e.Y, e.Delta)) return;
            if (_view.SettingsOpen || _view.KeysOpen) return;
            if (_view.MapOpen)
            {
                MapProjection m = WindowsHud.MapProj(_data, _view);
                if (m.Contains(e.X, e.Y)) { m.ZoomAt(e.X, e.Y, e.Delta > 0 ? 1.25f : 0.8f); _view.MapZoom = m.Zoom; _view.MapCx = m.CenterX; _view.MapCy = m.CenterY; }
                return;
            }
            if (_view.LogMode == 1 && WindowsHud.LogPanel(_view, _session.MySkills.Count).Contains(e.X, e.Y))
            {
                int total = LogFilter.Count(_session.World.Log, _view.LogTabSel);
                _view.LogScroll = Math.Max(0, Math.Min(Math.Max(0, total - WindowsHud.LogPanelLines), _view.LogScroll + (e.Delta > 0 ? 2 : -2)));
                return;
            }
            ZoomBy(e.Delta > 0 ? 1.12 : 1 / 1.12);
            base.OnMouseWheel(e);
        }

        private void ZoomBy(double f)
        {
            _targetScale = Math.Max(5, Math.Min(60, _targetScale * f));
        }

        private void UpdateMouse(int x, int y)
        {
            _view.HasMouse = true;
            _view.MouseX = x; _view.MouseY = y;
            _view.MouseWorldX = _view.ToWorldX(x);
            _view.MouseWorldY = _view.ToWorldY(y);
        }

        private Entity PickEntity(int sx, int sy, long now)
        {
            float wx = _view.ToWorldX(sx), wy = _view.ToWorldY(sy);
            Entity best = null; float bd = 99f;
            foreach (KeyValuePair<ulong, Entity> kv in _session.World.Entities)
            {
                Entity e = kv.Value;
                if (e.Id == _session.World.SelfId) continue;
                Look look = CreatureRenderer.LookOf(e);
                float x, y; e.PosAt(now, out x, out y);
                y -= look.H * 0.4f;                             // 몸통 중심(발 위)
                float hit = Math.Max(1.1f, look.HalfW * 0.6f);  // 큰 몬스터는 클릭 범위도 넓다
                float dx = x - wx, dy = y - wy;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (e.Dead) d += 0.8f;                           // 시체보다 산 것을 우선
                if (d < hit && d < bd) { bd = d; best = e; }
            }
            return best;
        }

        // ------------------------------------------------------------------ 키보드

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down) return true;       // 방향키는 화면 이동/창 조작에 쓰므로 포커스 이동에 빼앗기지 않게
            return k == Keys.Tab || k == Keys.Space || k == Keys.Escape || k == Keys.Enter || (k >= Keys.D1 && k <= Keys.D9) || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_phase != Phase.Playing) { StartKey(e); e.Handled = true; return; }
            long now = Now;
            Keys k = e.KeyCode;
            KeyBindings kb = _view.Settings.Keys;
            if (_ui.KeyDown((int)k)) { e.Handled = true; return; }
            if (k == Keys.F10) { _ui.Toggle(SettingsWindow.WindowId); e.Handled = true; return; }
            if (_view.KeysOpen) { KeysKey(k); e.Handled = true; return; }
            if (_view.SettingsOpen) { SettingsKey(k); e.Handled = true; return; }
            if (_view.MapOpen) { MapKey(k); e.Handled = true; return; }
            if (_view.HeroSelectOpen) { HeroSelectKey(k); e.Handled = true; return; }
            if (_view.ZoneSelectOpen) { ZoneSelectKey(k); e.Handled = true; return; }
            if (_view.SkillBookOpen) { SkillBookKey(k); e.Handled = true; return; }
            if (k == Keys.M && e.Control)
            {
                _view.Settings.SfxOn = !_view.Settings.SfxOn; ApplySettings(true);
                _session.World.AddLog(now, 0, _sfx.Failed ? "효과음을 재생할 수 없습니다(오디오 장치 확인)" : (_view.Settings.SfxOn ? "효과음 켜짐" : "효과음 꺼짐"));
                e.Handled = true; return;
            }
            if (k >= Keys.NumPad1 && k <= Keys.NumPad9) { UseSkillSlot(k - Keys.NumPad1, now); e.Handled = true; return; }
            BindAction act;
            if (!e.Control && !e.Alt && kb.TryActionFor((int)k, out act)) { DoAction(act, e, now); e.Handled = true; return; }
            switch (k)
            {
                case Keys.G: if (_demoMode) WalkToNearestGate(now); break;
                case Keys.F6: CycleTestTerrain(e.Shift, now); break;       // 시험장(존 0) 지형 치트(고정 키, 키 설정에는 없다)
                case Keys.Escape: _pilot.SetAuto(false); _view.SelectedId = 0; _session.StopMoving(now); break;
                case Keys.Oemplus: case Keys.Add: ZoomBy(1.15); break;
                case Keys.OemMinus: case Keys.Subtract: ZoomBy(1 / 1.15); break;
                case Keys.Left: PanCamera(-4, 0); break;
                case Keys.Right: PanCamera(4, 0); break;
                case Keys.Up: PanCamera(0, -4); break;
                case Keys.Down: PanCamera(0, 4); break;
            }
            base.OnKeyDown(e);
        }

        // ------------------------------------------------------------------ 시험장 지형 치트 (F6)

        /// <summary>
        /// 시험장(존 0)의 지형을 다음(Shift 면 이전) 지형으로 바꾼다. 서버에 붙었으면 서버에 요청해 장애물/충돌까지 바뀌고
        /// (결과는 ZcTerrainNotify 로 와서 ApplyServerTerrain 이 화면에 반영한다), 데모는 지형을 다시 골라 시험장을 다시 시작한다.
        /// </summary>
        private void CycleTestTerrain(bool back, long now)
        {
            if (_cfg.ZoneId != ZoneTheme.TestZone)
            {
                _session.World.AddLog(now, 1, "지형 치트는 시험장(존 0)에서만 쓸 수 있습니다");
                return;
            }
            _pilot.SetAuto(true);                                      // 지형을 바꾸면 곧바로 자동 전투로 시험한다(지형이 바뀌어도 자동 전투는 유지된다)
            uint[] ts = ZoneTheme.TerrainZones;
            int i = Array.IndexOf(ts, ZoneTheme.TestTerrain);
            if (i < 0) i = 0;
            uint next = ts[(i + (back ? ts.Length - 1 : 1)) % ts.Length];
            if (_demoMode) { ApplyZone(ZoneTheme.TestZone, next); return; }
            if (_session.SendTerrainCheat(next))
            {
                _terrainReqMs = now;
                _session.World.AddLog(now, 0, "지형 변경 요청: " + ZoneTheme.TerrainName(next));
            }
            else _session.World.AddLog(now, 1, "지형 변경 요청을 보내지 못했습니다(존에 입장한 상태에서만 가능)");
        }

        private long _terrainReqMs;     // 지형 변경을 요청한 시각(응답을 기다리는 동안만 0 이 아니다)

        /// <summary>지형 변경 요청에 서버가 2.5초 안에 답하지 않으면 원인을 짚어 알려 준다(치트가 꺼진 서버/오래된 빌드는 요청을 조용히 무시한다).</summary>
        private void CheckTerrainReply(long now)
        {
            if (_terrainReqMs == 0 || now - _terrainReqMs < 2500) return;
            _terrainReqMs = 0;
            _session.World.AddLog(now, 1, "서버가 지형 변경에 답하지 않습니다. 존 0 서버가 최신 빌드로 떠 있는지, 서버 콘솔에 'terrain cheat' 로그가 있는지 확인하세요");
        }

        /// <summary>서버가 알려 준 시험장 지형(치트로 바뀐 값, 입장 때 받은 값 포함)을 GameData 와 화면에 반영한다. 세션은 다시 시작하지 않는다.</summary>
        private void ApplyServerTerrain(long now)
        {
            CheckTerrainReply(now);
            uint terrain = _session.World.ServerTerrain;
            if (terrain == 0) return;
            _session.World.ServerTerrain = 0;
            _terrainReqMs = 0;
            if (_cfg.ZoneId != ZoneTheme.TestZone || terrain == ZoneTheme.TestTerrain) return;
            GameData d = GameData.Load(_loader.DataDir, ZoneTheme.TestZone, terrain);
            if (d.Heroes.Count == 0 && _data.Heroes.Count > 0) return;      // 읽기 실패: 그대로 둔다
            _data = d;
            ZoneTheme.Current = ZoneTheme.For(ZoneTheme.TestZone);
            ApplyBgm();
            _session.World.AddLog(now, 0, "시험장 지형: " + ZoneTheme.TerrainName(terrain));
        }

        /// <summary>키 설정에 묶인 동작을 실행한다.</summary>
        private void DoAction(BindAction a, KeyEventArgs e, long now)
        {
            if (a == BindAction.SkillBook) { OpenSkillBook(); return; }
            if (a == BindAction.InfoPanel) { _view.ShowInfo = !_view.ShowInfo; return; }
            if (a == BindAction.Inventory) { _ui.Toggle(InventoryWindow.WindowId); return; }         // 새 창 3종은 스킬 칸 번호 판정보다 먼저 처리한다
            if (a == BindAction.Mail) { _ui.Toggle(MailWindow.WindowId); return; }
            if (a == BindAction.Shop) { _ui.Toggle(ShopWindow.WindowId); return; }
            if ((int)a >= KeyBindings.FirstSkill) { UseSkillSlot((int)a - KeyBindings.FirstSkill, now); return; }
            switch (a)
            {
                case BindAction.BasicAttack: BasicAttack(now); break;
                case BindAction.SelectBoss: SelectBoss(now); break;
                case BindAction.AutoBattle: _pilot.SetAuto(_pilot.Mode != PilotMode.Auto); break;
                case BindAction.FollowToggle: _follow = !_follow; break;
                case BindAction.CenterCamera: _follow = true; break;
                case BindAction.CycleTarget: CycleTarget(now, e.Shift); break;
                case BindAction.ToggleHelp: _view.ShowHelp = !_view.ShowHelp; break;
                case BindAction.ToggleLog: _view.LogMode = (_view.LogMode + 1) % 3; _view.LogScroll = 0; break;
                case BindAction.Chat: OpenChat(); break;
                case BindAction.Settings: OpenSettings(); break;
                case BindAction.WorldMap: _view.MapOpen = true; _mapDrag = false; break;
                case BindAction.HeroSelect: OpenHeroSelect(); break;
                case BindAction.ZoneSelect: OpenZoneSelect(); break;
                case BindAction.Screenshot: SaveScreenshot(); break;
                case BindAction.Reconnect:
                    if (!_demoMode && (_session.State == SessionState.Failed || _session.State == SessionState.Disconnected)) _session.ReconnectNow(now);
                    else StartSession();
                    break;
            }
        }

        // ------------------------------------------------------------------ 설정 / 월드맵 / 로그 창 / 채팅 / 입장 연출

        /// <summary>설정값을 소리/표시에 반영하고, save 면 파일에도 저장한다.</summary>
        private void ApplySettings(bool save)
        {
            ViewSettings st = _view.Settings;
            _sfx.SetGain(st.Gain);
            _sfx.Enabled = st.Gain > 0f;
            _bgm.SetGain(st.BgmGain);
            if (st.BgmGain <= 0f) _bgm.Stop();
            else ApplyBgm();
            _view.BgmLabel = st.BgmGain > 0f ? _bgm.Now : null;
            _start.Keys = st.Keys;
            if (_session != null) _session.World.HitStopScale = st.FeedbackScale;
            if (save) st.Save(_settingsPath);
        }

        /// <summary>지금 상황(시작 화면/존 안)에 맞는 곡을 틀거나 끈다.</summary>
        private void ApplyBgm()
        {
            if (_view.Settings.BgmGain <= 0f) return;
            if (_phase == Phase.Select) { _bgm.Play(ZoneTheme.For(_cfg.ZoneId), false); return; }
            if (_phase == Phase.Playing && _bgmDir.Playing) _bgm.Play(_bgmDir.Kind, _bgmDir.Battle);
            else if (_phase != Phase.Loading) _bgm.Stop();
            _view.BgmLabel = _bgm.Now;
        }

        private void OpenSettings() { _view.SettingsOpen = true; _view.SettingsIndex = 0; }

        private void SettingsKey(Keys k)
        {
            int n = WindowsHud.SettingNames.Length;
            if (k == Keys.Escape || (int)k == _view.Settings.Keys.Get(BindAction.Settings)) { _view.SettingsOpen = false; return; }
            if (k == Keys.Up) _view.SettingsIndex = (_view.SettingsIndex + n - 1) % n;
            else if (k == Keys.Down) _view.SettingsIndex = (_view.SettingsIndex + 1) % n;
            else if (k == Keys.Left) ChangeSetting(_view.SettingsIndex, -1);
            else if (k == Keys.Right || k == Keys.Enter || k == Keys.Space) ChangeSetting(_view.SettingsIndex, 1);
        }

        private void ChangeSetting(int i, int dir)
        {
            ViewSettings st = _view.Settings;
            switch (i)
            {
                case WindowsHud.RowQuality: if (dir > 0) st.CycleQuality(); else { st.CycleQuality(); st.CycleQuality(); } break;
                case WindowsHud.RowSfx: st.SfxOn = !st.SfxOn; break;
                case WindowsHud.RowSfxVol: st.StepVolume(dir * 10); break;
                case WindowsHud.RowBgm: st.BgmOn = !st.BgmOn; break;
                case WindowsHud.RowBgmVol: st.StepBgm(dir * 10); break;
                case WindowsHud.RowFps: st.ShowFps = !st.ShowFps; break;
                case WindowsHud.RowDmg: st.ShowDamageNumbers = !st.ShowDamageNumbers; break;
                case WindowsHud.RowFx: st.ShowScreenFx = !st.ShowScreenFx; break;
                case WindowsHud.RowShake: st.ReduceShake = !st.ReduceShake; break;
                case WindowsHud.RowWeather: st.WeatherOn = !st.WeatherOn; break;
                case WindowsHud.RowFeedback: st.StepFeedback(dir * 25); break;
                case WindowsHud.RowKeys: _view.KeysOpen = true; _view.KeysIndex = 0; _view.KeysCapture = false; _view.KeysMsg = ""; return;
            }
            ApplySettings(true);
            if (_sfx.Enabled) _sfx.Play(SfxKind.UiTick, 1f);
        }

        private bool ClickSettings(int sx, int sy)
        {
            for (int i = 0; i < WindowsHud.SettingNames.Length; ++i)
            {
                RectangleF r = WindowsHud.SettingsRow(_view, i);
                if (!r.Contains(sx, sy)) continue;
                _view.SettingsIndex = i;
                int smax, sstep;
                if (WindowsHud.IsSlider(i, out smax, out sstep))
                {
                    RectangleF t = WindowsHud.SliderTrack(_view, i);
                    if (sx >= t.X - 6 && sx <= t.Right + 6)
                    {
                        int val = (int)Math.Round(Math.Max(0f, Math.Min(1f, (sx - t.X) / t.Width)) * smax / sstep) * sstep;
                        ViewSettings st = _view.Settings;
                        if (i == WindowsHud.RowSfxVol) { st.VolumePercent = val; st.SfxOn = val > 0; }
                        else if (i == WindowsHud.RowBgmVol) { st.BgmVolumePercent = val; st.BgmOn = val > 0; }
                        else st.FeedbackPercent = val;
                        st.Normalize(); ApplySettings(true);
                        if (_sfx.Enabled) _sfx.Play(SfxKind.UiTick, 1f);
                        return true;
                    }
                }
                ChangeSetting(i, 1);
                return true;
            }
            if (!WindowsHud.SettingsWindow(_view).Contains(sx, sy)) _view.SettingsOpen = false;
            return true;
        }

        // ------------------------------------------------------------------ 키 설정 창

        private void KeysKey(Keys k)
        {
            KeyBindings kb = _view.Settings.Keys;
            BindAction a = (BindAction)_view.KeysIndex;
            if (_view.KeysCapture)
            {
                if (k == Keys.Escape) { _view.KeysCapture = false; _view.KeysMsg = ""; return; }
                if (k == Keys.Back || k == Keys.Delete) { kb.Reset(a); _view.KeysCapture = false; _view.KeysMsg = KeyBindings.ActionName(a) + ": 기본 키(" + KeyBindings.KeyName(kb.Get(a)) + ")로 되돌렸습니다"; ApplySettings(true); return; }
                BindAction other;
                BindResult r = kb.Set(a, (int)k, out other);
                if (r == BindResult.Reserved) { _view.KeysMsg = "이 키는 쓸 수 없습니다 (Esc / 방향키 / Shift·Ctrl·Alt / 숫자패드 1~9 는 예약)"; return; }
                _view.KeysCapture = false;
                _view.KeysMsg = r == BindResult.Swapped
                    ? KeyBindings.KeyName((int)k) + " 키는 '" + KeyBindings.ActionName(other) + "' 가 쓰고 있어 서로 맞바꿨습니다 (" + KeyBindings.ActionName(other) + " = " + KeyBindings.KeyName(kb.Get(other)) + ")"
                    : KeyBindings.ActionName(a) + " = " + KeyBindings.KeyName((int)k);
                ApplySettings(true);
                if (_sfx.Enabled) _sfx.Play(SfxKind.UiTick, 1f);
                return;
            }
            int n = KeyBindings.ActionCount, left = 15;
            if (k == Keys.Escape || k == Keys.F2) { _view.KeysOpen = false; _view.KeysMsg = ""; return; }
            if (k == Keys.Up) _view.KeysIndex = (_view.KeysIndex + n - 1) % n;
            else if (k == Keys.Down) _view.KeysIndex = (_view.KeysIndex + 1) % n;
            else if (k == Keys.Left || k == Keys.Right) _view.KeysIndex = _view.KeysIndex < left ? Math.Min(n - 1, _view.KeysIndex + left) : _view.KeysIndex - left;
            else if (k == Keys.Enter || k == Keys.Space) { _view.KeysCapture = true; _view.KeysMsg = ""; }
            else if (k == Keys.Delete || k == Keys.Back) { kb.Reset(a); _view.KeysMsg = KeyBindings.ActionName(a) + ": 기본 키(" + KeyBindings.KeyName(kb.Get(a)) + ")로 되돌렸습니다"; ApplySettings(true); }
            else if (k == Keys.R) { kb.ResetAll(); _view.KeysMsg = "모든 키를 기본값으로 되돌렸습니다"; ApplySettings(true); }
        }

        private void ClickKeys(int sx, int sy)
        {
            if (_view.KeysCapture) { _view.KeysCapture = false; return; }
            for (int i = 0; i < KeyBindings.ActionCount; ++i)
                if (WindowsHud.KeyRow(_view, i).Contains(sx, sy)) { _view.KeysIndex = i; _view.KeysCapture = true; _view.KeysMsg = ""; return; }
            if (WindowsHud.KeysResetButton(_view).Contains(sx, sy)) { _view.Settings.Keys.ResetAll(); _view.KeysMsg = "모든 키를 기본값으로 되돌렸습니다"; ApplySettings(true); return; }
            if (!WindowsHud.KeysWindow(_view).Contains(sx, sy)) { _view.KeysOpen = false; _view.KeysMsg = ""; }
        }

        private void MapKey(Keys k)
        {
            if (k == Keys.Escape || (int)k == _view.Settings.Keys.Get(BindAction.WorldMap)) { _view.MapOpen = false; _mapDrag = false; return; }
            MapProjection m = WindowsHud.MapProj(_data, _view);
            RectangleF a = WindowsHud.MapArea(_view);
            if (k == Keys.Oemplus || k == Keys.Add) { m.ZoomAt(a.X + a.Width / 2, a.Y + a.Height / 2, 1.25f); }
            else if (k == Keys.OemMinus || k == Keys.Subtract) { m.ZoomAt(a.X + a.Width / 2, a.Y + a.Height / 2, 0.8f); }
            else if (k == Keys.Space) { m.Zoom = 1f; float sx, sy; if (_session.SelfPos(Now, out sx, out sy)) { m.CenterX = sx; m.CenterY = sy; } m.Clamp(); }
            else
            {
                float step = 60f / m.Scale * 0.5f;
                if (k == Keys.Left) m.CenterX -= step; else if (k == Keys.Right) m.CenterX += step;
                else if (k == Keys.Up) m.CenterY -= step; else if (k == Keys.Down) m.CenterY += step; else return;
                m.Clamp();
            }
            _view.MapZoom = m.Zoom; _view.MapCx = m.CenterX; _view.MapCy = m.CenterY;
        }

        private void DragMap(int x, int y)
        {
            if (!_mapDrag) return;
            if (!_mapMoved && Math.Abs(x - _mapDownX) + Math.Abs(y - _mapDownY) < 5) return;
            _mapMoved = true;
            MapProjection m = WindowsHud.MapProj(_data, _view);
            m.CenterX -= (x - _mapDownX) / m.Scale; m.CenterY -= (y - _mapDownY) / m.Scale;
            m.Clamp();
            _view.MapCx = m.CenterX; _view.MapCy = m.CenterY;
            _mapDownX = x; _mapDownY = y;
        }

        /// <summary>월드맵을 클릭하면 그 지점으로 이동한다(자동 전투는 끈다).</summary>
        private void ClickMap(int sx, int sy)
        {
            MapProjection m = WindowsHud.MapProj(_data, _view);
            if (!m.Contains(sx, sy)) return;
            float wx = m.ToWorldX(sx), wy = m.ToWorldY(sy);
            if (wx < _data.WorldMinX || wx > _data.WorldMaxX || wy < _data.WorldMinY || wy > _data.WorldMaxY) return;
            _pilot.SetAuto(false); _seekBoss = false;
            _session.MoveTo(wx, wy, Now, true);
            _session.World.AddLog(Now, 0, "지도에서 (" + wx.ToString("0") + ", " + wy.ToString("0") + ") 로 이동");
            _view.MapOpen = false;
        }

        private bool ClickLogTab(int sx, int sy)
        {
            int skills = _session.MySkills.Count;
            for (int i = 0; i < 4; ++i)
                if (WindowsHud.LogTabRect(_view, skills, i).Contains(sx, sy)) { _view.LogTabSel = (LogTab)i; _view.LogScroll = 0; if (_sfx.Enabled) _sfx.Play(SfxKind.UiTick, 0.5f); return true; }
            return false;
        }

        private void OpenChat()
        {
            if (_session.State != SessionState.InZone) return;
            _view.LogMode = 1; _view.LogTabSel = LogTab.Chat; _view.LogScroll = 0;
            RectangleF r = WindowsHud.ChatInputRect(_view, _session.MySkills.Count);
            _chat.SetBounds((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            _chat.Text = ""; _chat.Visible = true; _view.ChatActive = true;
            _chat.Focus();
        }

        private void CloseChat()
        {
            _view.ChatActive = false; _chat.Visible = false;
            if (!IsDisposed) Focus();
        }

        private void ChatKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseChat(); }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string t = _chat.Text.Trim();
                if (t.Length > 0 && _session != null) _session.World.AddLog(Now, 0, "[나] " + t, LogCat.Chat, true);
                CloseChat();
            }
        }

        /// <summary>존에 새로 들어오면(처음 입장 또는 재접속) 입장 연출과 효과음을 시작한다.</summary>
        // ------------------------------------------------------------------ 존 관문 통과 (데모 모드)

        private readonly GateTravel _gate = new GateTravel();

        /// <summary>영웅이 관문 안쪽 가장자리에 닿으면 화면을 어둡게 하고, 다 어두워지면 건너편 존(GameData ZoneLink)으로 바꿔 도착 지점에서 다시 시작한다.
        /// 서버 모드는 존 이동을 서버가 정하므로 하지 않는다.</summary>
        private void UpdateGateTravel(long now)
        {
            if (!_demoMode || _demo == null || _session == null) return;
            Entity me = _session.World.Self;
            float x = me != null ? me.X : 0f, y = me != null ? me.Y : 0f;
            GateStep st = _gate.Update(now, _data.Gates, x, y, _data.WorldMinX, _data.WorldMinY, _data.WorldMaxX, _data.WorldMaxY, me != null && !me.Dead);
            if (st == GateStep.Started)
            {
                _session.StopMoving(now);
                if (_sfx.Enabled) _sfx.Play(SfxSynth.ZoneSfx(ZoneTheme.For(_gate.Pending.TargetZone)), 0.8f);
            }
            else if (st == GateStep.Switch) TravelThroughGate(now);
        }

        private void TravelThroughGate(long now)
        {
            ZoneGate g = _gate.Pending;
            GameData d = GameData.Load(_loader.DataDir, g.TargetZone);
            if (d.Heroes.Count == 0 && _data.Heroes.Count > 0) { _gate.Arrived(now); return; }       // 읽기 실패: 그대로 둔다
            bool auto = _pilot != null && _pilot.Mode == PilotMode.Auto;
            _cfg.ZoneId = g.TargetZone;
            _data = d;
            ZoneTheme.Current = ZoneTheme.For(g.TargetZone);
            StartSession(g.TargetX, g.TargetY);
            if (auto) _pilot.SetAuto(true);
            _gate.Arrived(Now);
            ApplyBgm();
        }

        private void DrawGateFade(IGfx g)
        {
            float t = _gate.Fade(Now);
            if (t <= 0f) return;
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * t), 0, 0, 0))) g.FillRectangle(b, 0, 0, _view.Width, _view.Height);
        }

        /// <summary>G: 가장 가까운 관문으로 걸어간다(데모 모드).</summary>
        private void WalkToNearestGate(long now)
        {
            Entity me = _session.World.Self;
            if (me == null || _data.Gates.Count == 0) { _session.World.AddLog(now, 1, "이 존에는 관문이 없습니다"); return; }
            ZoneGate best = null; float bd = float.MaxValue, bx = 0, by = 0;
            foreach (ZoneGate gt in _data.Gates)
            {
                float cx, cy; ZoneGates.CenterOf(gt, _data.WorldMinX, _data.WorldMinY, _data.WorldMaxX, _data.WorldMaxY, out cx, out cy);
                float dd = (cx - me.X) * (cx - me.X) + (cy - me.Y) * (cy - me.Y);
                if (dd < bd) { bd = dd; best = gt; bx = cx; by = cy; }
            }
            float k = GateTravel.Depth * 0.5f;
            switch (best.Edge) { case 'W': bx += k; break; case 'E': bx -= k; break; case 'N': by += k; break; default: by -= k; break; }
            _pilot.SetAuto(false); _seekBoss = false;
            _session.MoveTo(bx, by, now, true);
            _session.World.AddLog(now, 0, "관문으로 이동: " + ZoneTheme.NameOf(best.TargetZone));
        }

        private void UpdateIntro()
        {
            long since = _session.InZoneSinceMs;
            if (since < 0 || since == _introSeen) return;
            bool again = _introSeen >= 0;
            _introSeen = since;
            _view.IntroStartMs = since; _view.IntroBanner = !again;
            _view.MapOpen = false;
            if (again && _session.ReconnectCount > _reconnectsSeen)
            {
                _reconnectsSeen = _session.ReconnectCount;
                if (_sfx.Enabled) _sfx.Play(SfxKind.Reconnect, 1f);
            }
            else if (_sfx.Enabled) _sfx.Play(SfxSynth.ZoneSfx(ZoneTheme.For(_session.ZoneId)), 1f);
        }

        // ------------------------------------------------------------------ 존 변경

        private void OpenZoneSelect()
        {
            int cur = Array.IndexOf(ZoneTheme.Zones, _cfg.ZoneId);
            _view.ZoneSelectIndex = cur < 0 ? 0 : cur;
            _view.ZoneTerrainPick = false;
            _view.ZoneSelectOpen = true;
        }

        private void ZoneSelectKey(Keys k)
        {
            uint[] zs = _view.ZoneTerrainPick ? ZoneTheme.TerrainZones : ZoneTheme.Zones;
            int n = zs.Length;
            if (k == Keys.Escape || (int)k == _view.Settings.Keys.Get(BindAction.ZoneSelect)) { if (_view.ZoneTerrainPick) { _view.ZoneTerrainPick = false; _view.ZoneSelectIndex = ZoneTheme.Zones.Length - 1; } else _view.ZoneSelectOpen = false; return; }
            if (k == Keys.Up) _view.ZoneSelectIndex = (_view.ZoneSelectIndex + n - 1) % n;
            else if (k == Keys.Down) _view.ZoneSelectIndex = (_view.ZoneSelectIndex + 1) % n;
            else if (k >= Keys.D1 && k <= Keys.D9 && k - Keys.D1 < n) _view.ZoneSelectIndex = k - Keys.D1;
            else if (k >= Keys.NumPad1 && k <= Keys.NumPad9 && k - Keys.NumPad1 < n) _view.ZoneSelectIndex = k - Keys.NumPad1;
            else if (k == Keys.Enter || k == Keys.Return) PickZone(zs[Math.Max(0, Math.Min(n - 1, _view.ZoneSelectIndex))]);
        }

        /// <summary>목록에서 고른 값을 처리한다. 데모에서 시험장(0)을 고르면 지형 고르기로 넘어가고, 지형 단계면 그 지형으로 시험장을 다시 시작한다.
        /// 서버에 붙을 때는 지형을 고르지 않는다(서버가 ZoneRule.Terrain 으로 정한 지형을 같은 GameData 에서 읽는다).</summary>
        private void PickZone(uint id)
        {
            if (_view.ZoneTerrainPick)
            {
                _view.ZoneTerrainPick = false;
                ApplyZone(ZoneTheme.TestZone, id);
                return;
            }
            if (id == ZoneTheme.TestZone && _demoMode)       // 서버에 붙을 때 지형은 서버(ZoneRule.Terrain)가 정한다. 데모만 고른다
            {
                _view.ZoneTerrainPick = true;
                _view.ZoneSelectIndex = (int)ZoneTheme.TestTerrain - 1;
                return;
            }
            ApplyZone(id);
        }

        /// <summary>고른 존으로 바꿔 다시 시작한다. 존마다 스폰/장애물이 달라 GameData 를 그 존 기준으로 다시 읽는다.
        /// 서버 모드는 Gateway 에 그 존으로 다시 입장을 요청한다(같은 존이면 다시 접속하지 않는다).</summary>
        private void ApplyZone(uint zoneId, uint terrain = 0)
        {
            _view.ZoneSelectOpen = false;
            if (zoneId == _cfg.ZoneId && _session.State == SessionState.InZone && terrain == 0) return;
            GameData d = GameData.Load(_loader.DataDir, zoneId, terrain);
            if (d.Heroes.Count == 0 && _data.Heroes.Count > 0) { _session.World.AddLog(Now, 1, "존 " + zoneId + " 데이터를 읽지 못했습니다"); return; }
            bool auto = _pilot != null && _pilot.Mode == PilotMode.Auto;
            _cfg.ZoneId = zoneId;
            _data = d;
            ZoneTheme.Current = ZoneTheme.For(zoneId);
            StartSession();
            if (auto) _pilot.SetAuto(true);
            ApplyBgm();
        }

        private bool ClickZoneSelect(int sx, int sy)
        {
            uint[] zs = _view.ZoneTerrainPick ? ZoneTheme.TerrainZones : ZoneTheme.Zones;
            for (int i = 0; i < zs.Length; ++i)
                if (HudRenderer.ZoneRowRect(_view, zs.Length, i).Contains(sx, sy)) { _view.ZoneSelectIndex = i; PickZone(zs[i]); return true; }
            return false;
        }

        // ------------------------------------------------------------------ 스킬 창

        private void OpenSkillBook()
        {
            if (_session.State != SessionState.InZone) return;
            _view.SkillBookTarget = 0;
            _view.SkillBookOpen = true;
        }

        private void SkillBookKey(Keys k)
        {
            if (k == Keys.Escape || (int)k == _view.Settings.Keys.Get(BindAction.SkillBook)) _view.SkillBookOpen = false;
        }

        /// <summary>스킬 창 클릭. 창 안이면 true(닫지 않는다), 밖이면 false.</summary>
        private bool ClickSkillBook(int sx, int sy)
        {
            List<uint> all = _session.Data.SkillsOf(_session.HeroId);
            int n = all.Count;
            if (!SkillBookHud.Window(_view, n).Contains(sx, sy)) return false;
            if (!_session.World.SkillBookKnown) return true;
            for (int i = ViewerSession.FixedSlots; i < ViewerSession.MaxEquipped; ++i)
                if (SkillBookHud.SlotRect(_view, n, i).Contains(sx, sy)) { _view.SkillBookTarget = _view.SkillBookTarget == i + 1 ? 0 : i + 1; return true; }
            for (int i = 0; i < n; ++i)
            {
                if (!SkillBookHud.ButtonRect(_view, n, i).Contains(sx, sy)) continue;
                uint id = all[i];
                switch (SkillBookHud.ActionFor(_session, id))
                {
                    case SkillBookAction.Buy: _session.BuySkill(id); break;
                    case SkillBookAction.Unequip: _session.EquipSkill(id, 0); break;
                    case SkillBookAction.Equip:
                    {
                        byte slot = SkillBookHud.TargetSlot(_session, _view);
                        if (slot == 0) _session.World.AddLog(Now, 1, "열려 있는 빈 상점 칸(7·8)이 없습니다. 칸을 눌러 고르거나 먼저 스킬을 해제해 주세요");
                        else _session.EquipSkill(id, slot);
                        break;
                    }
                }
                return true;
            }
            return true;
        }

        // ------------------------------------------------------------------ 영웅 선택

        private void OpenHeroSelect()
        {
            List<uint> ids = _data.HeroIds();
            if (ids.Count == 0) { _session.World.AddLog(Now, 1, "영웅 데이터가 없습니다"); return; }
            int cur = ids.IndexOf(_session.HeroId);
            _view.HeroSelectIndex = cur < 0 ? 0 : cur;
            _view.HeroSelectOpen = true;
        }

        private void HeroSelectKey(Keys k)
        {
            List<uint> ids = _data.HeroIds();
            int n = ids.Count;
            if (k == Keys.Escape || (int)k == _view.Settings.Keys.Get(BindAction.HeroSelect)) { _view.HeroSelectOpen = false; return; }
            if (n == 0) return;
            if (k == Keys.Up) _view.HeroSelectIndex = (_view.HeroSelectIndex + n - 1) % n;
            else if (k == Keys.Down) _view.HeroSelectIndex = (_view.HeroSelectIndex + 1) % n;
            else if (k >= Keys.D1 && k <= Keys.D9 && k - Keys.D1 < n) _view.HeroSelectIndex = k - Keys.D1;
            else if (k >= Keys.NumPad1 && k <= Keys.NumPad9 && k - Keys.NumPad1 < n) _view.HeroSelectIndex = k - Keys.NumPad1;
            else if (k == Keys.Enter || k == Keys.Return) ApplyHero(ids[Math.Max(0, Math.Min(n - 1, _view.HeroSelectIndex))]);
        }

        /// <summary>선택한 영웅으로 세션을 다시 시작한다. (서버 모드는 같은 영웅이면 다시 접속하지 않는다)</summary>
        private void ApplyHero(uint heroId)
        {
            _view.HeroSelectOpen = false;
            if (heroId == _session.HeroId && _session.State == SessionState.InZone) return;
            _cfg.HeroId = heroId;
            StartSession();
        }

        private bool ClickHeroSelect(int sx, int sy)
        {
            List<uint> ids = _data.HeroIds();
            for (int i = 0; i < ids.Count; ++i)
                if (HudRenderer.HeroRowRect(_view, ids.Count, i).Contains(sx, sy)) { _view.HeroSelectIndex = i; ApplyHero(ids[i]); return true; }
            return false;
        }

        private bool _seekBoss;             // 시야에 보스가 없어 보스 출현 위치로 이동 중
        private float _seekX, _seekY;

        /// <summary>시야 안의 살아 있는 보스 중 가장 가까운 것을 고른다. 서버는 시야(AOI) 밖 엔티티를 알려주지 않으므로 없을 수 있다.</summary>
        private Entity FindBoss(long now)
        {
            float sx, sy;
            if (!_session.SelfPos(now, out sx, out sy)) return null;
            Entity best = null; float bd = float.MaxValue;
            foreach (KeyValuePair<ulong, Entity> kv in _session.World.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsBoss || e.Dead) continue;
                float x, y; e.PosAt(now, out x, out y);
                float d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        /// <summary>B: 보스를 선택한다. 시야에 없으면 데이터(Spawn)의 가장 가까운 보스 출현 위치로 이동하고, 보스가 보이면 자동으로 선택한다.</summary>
        private void SelectBoss(long now)
        {
            Entity best = FindBoss(now);
            if (best != null) { _seekBoss = false; _view.SelectedId = best.Id; return; }
            float sx, sy;
            if (!_session.SelfPos(now, out sx, out sy)) return;
            SpawnRow near = null; float bd = float.MaxValue;
            foreach (SpawnRow sp in _data.Spawns)
            {
                if (!_data.Bosses.ContainsKey(sp.MonsterId)) continue;
                float d = (sp.X - sx) * (sp.X - sx) + (sp.Y - sy) * (sp.Y - sy);
                if (d < bd) { bd = d; near = sp; }
            }
            if (near == null) { _session.World.AddLog(now, 1, "이 존에는 보스가 없습니다 (Spawn 데이터에 보스 몬스터가 없음)"); return; }
            _seekBoss = true; _seekX = near.X; _seekY = near.Y;
            _session.MoveTo(near.X, near.Y, now, true);
            _session.World.AddLog(now, 0, _data.Bosses[near.MonsterId].Name + " 출현 위치 (" + near.X.ToString("0") + ", " + near.Y.ToString("0") + ") 로 이동합니다 - 거리 " + ((float)Math.Sqrt(bd)).ToString("0"));
        }

        /// <summary>보스 찾아 이동 중이면 보스가 시야에 들어오는 순간 선택하고, 도착했는데 없으면 멈춘다.</summary>
        private void UpdateSeekBoss(long now)
        {
            if (!_seekBoss) return;
            Entity b = FindBoss(now);
            if (b != null) { _seekBoss = false; _view.SelectedId = b.Id; _session.World.AddLog(now, 0, "보스 선택: " + (b.Boss != null ? b.Boss.Name : "")); return; }
            float sx, sy;
            if (!_session.SelfPos(now, out sx, out sy)) return;
            if ((sx - _seekX) * (sx - _seekX) + (sy - _seekY) * (sy - _seekY) < 9f)
            {
                _seekBoss = false;
                _session.World.AddLog(now, 1, "출현 위치에 도착했지만 보스가 보이지 않습니다 (이미 처치되었거나 재출현 대기 중)");
            }
        }

        /// <summary>현재 화면을 PNG 로 저장한다 (실행 폴더의 screenshots).</summary>
        private void SaveScreenshot()
        {
            try
            {
                string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "shot_" + _cfg.CharId + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                using (Bitmap bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)))
                {
                    using (GdiGfx g = GdiGfx.FromImage(bmp)) _renderer.Draw(g, _session, _pilot, _view, Now);
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                _session.World.AddLog(Now, 0, "스크린샷 저장: " + path);
            }
            catch (Exception ex) { _session.World.AddLog(Now, 2, "스크린샷 실패: " + ex.Message); }
        }

        private void PanCamera(float dx, float dy) { _follow = false; _view.CamX += dx; _view.CamY += dy; }

        private void BasicAttack(long now)
        {
            Entity t = _session.World.Find(_view.SelectedId);
            if (t == null || t.Dead) t = NearestEnemy(now);
            if (t == null) return;
            _view.SelectedId = t.Id;
            _pilot.ChaseTarget(t.Id);       // 사거리 밖이면 접근 후 공격 (스킬도 함께 사용)
        }

        private void UseSkillSlot(int index, long now)
        {
            List<uint> skills = _session.MySkills;
            if (index < 0 || index >= skills.Count) return;
            SkillRow sk = _data.Skill(skills[index]);
            if (sk == null) { if (_session.World.SkillBookKnown) _session.World.AddLog(now, 1, _session.SlotLockLevel(index + 1) > 0 ? "아직 열리지 않은 칸입니다 (Lv " + _session.SlotLockLevel(index + 1) + ")" : "비어 있는 칸입니다"); return; }
            SkillAimResult a = SkillAim.Resolve(sk, _session.World, _session.World.Find(_view.SelectedId), _view.HasMouse, _view.MouseWorldX, _view.MouseWorldY, now);
            if (!a.Ok)
            {
                _session.World.AddLog(now, 1, a.WhyNot);
                if (a.Target != null && a.Target.IsMonster && !a.Target.Dead) { _view.SelectedId = a.Target.Id; _pilot.ChaseTarget(a.Target.Id); }     // 사거리 밖이면 다가간다
                return;
            }
            if (a.Target != null && a.Target.IsMonster) _view.SelectedId = a.Target.Id;
            _session.UseSkill(sk.Id, a.TargetId, a.X, a.Y, now);
        }

        private Entity NearestEnemy(long now)
        {
            float sx, sy;
            if (!_session.SelfPos(now, out sx, out sy)) return null;
            Entity best = null; float bd = float.MaxValue;
            foreach (KeyValuePair<ulong, Entity> kv in _session.World.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsMonster || e.Dead) continue;
                float x, y; e.PosAt(now, out x, out y);
                float d = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        private void CycleTarget(long now, bool reverse)
        {
            float sx, sy;
            if (!_session.SelfPos(now, out sx, out sy)) return;
            List<KeyValuePair<float, Entity>> list = new List<KeyValuePair<float, Entity>>();
            foreach (KeyValuePair<ulong, Entity> kv in _session.World.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsMonster || e.Dead) continue;
                float x, y; e.PosAt(now, out x, out y);
                list.Add(new KeyValuePair<float, Entity>((x - sx) * (x - sx) + (y - sy) * (y - sy), e));
            }
            if (list.Count == 0) return;
            list.Sort(delegate (KeyValuePair<float, Entity> a, KeyValuePair<float, Entity> b) { return a.Key.CompareTo(b.Key); });
            int cur = -1;
            for (int i = 0; i < list.Count; ++i) if (list[i].Value.Id == _view.SelectedId) cur = i;
            int next = reverse ? (cur <= 0 ? list.Count - 1 : cur - 1) : (cur + 1) % list.Count;
            _view.SelectedId = list[next].Value.Id;
            if (_pilot.Mode == PilotMode.Chase) _pilot.ChaseTarget(_view.SelectedId);
        }

        private bool _formHooked;

        /// <summary>창이 비활성(다른 창으로 전환)이 되면 배경음악을 멈추고, 돌아오면 이어서 튼다.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Form f = FindForm();
            if (f != null && !_formHooked)
            {
                _formHooked = true;
                f.Deactivate += delegate { _bgm.Pause(); };
                f.Activated += delegate { _bgm.Resume(); };
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_probe != null) _probe.Stop();
                _bgm.Dispose();
                if (_session != null) { _session.Leave(); _session.Dispose(); }
                if (_frame != null) _frame.Dispose();
                _uic.Dispose();
#if SKIA
                if (_skia != null) _skia.Dispose();
#endif
                _sfx.Dispose();
                _chat.Dispose();
                _start.Dispose();
                _renderer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
