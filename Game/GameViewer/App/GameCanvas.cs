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
using GameViewer.Core.World;

namespace GameViewer.App
{
    public sealed class GameCanvas : Control
    {
        private readonly SceneRenderer _renderer = new SceneRenderer();
        private readonly ViewState _view = new ViewState();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private ViewerSession _session;
        private AutoPilot _pilot;
        private DemoDirector _demo;
        private bool _follow = true;
        private double _targetScale;                    // 확대 목표(부드럽게 따라간다)
        private bool _rightDown;
        private int _fpsFrames; private long _fpsLast;
        private readonly ViewerConfig _cfg;
        private readonly GameData _data;
        private readonly bool _demoMode;
        private readonly SoundFx _sfx = new SoundFx();
        private readonly HashSet<ViewEffect> _sfxSeen = new HashSet<ViewEffect>();
        private bool _sfxPrimed;
        private readonly HeartbeatClock _heart = new HeartbeatClock();

        public GameCanvas(ViewerConfig cfg, GameData data, bool demoMode)
        {
            _cfg = cfg; _data = data; _demoMode = demoMode;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            TabStop = true;
            _view.Scale = cfg.PixelsPerUnit; _targetScale = _view.Scale;
            StartSession();
        }

        public long Now { get { return _clock.ElapsedMilliseconds + 1000; } }
        public ViewerSession Session { get { return _session; } }

        public void StartSession()
        {
            if (_session != null) { _session.Leave(); _session.Dispose(); }
            _session = new ViewerSession(_cfg, _data);
            _pilot = new AutoPilot(_session);
            _demo = null;
            _view.SelectedId = 0;
            _sfxSeen.Clear(); _sfxPrimed = false;
            if (_demoMode)
            {
                _demo = new DemoDirector(_session);
                _demo.Start(Now);
            }
            else _session.Start(Now);
            if (_cfg.Auto) _pilot.SetAuto(true);
            _follow = true;
        }

        /// <summary>한 프레임 진행: 패킷 처리 -> AI -> 카메라.</summary>
        public void Tick()
        {
            long now = Now;
            _session.Update(now);
            if (_demo != null) _demo.Update(now);
            _pilot.Update(now);
            UpdateSeekBoss(now);
            PlayEffectSounds(now);

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
            try
            {
                using (Graphics g = Graphics.FromImage(_frame)) _renderer.Draw(g, _session, _pilot, _view, Now);
            }
            catch (Exception ex)
            {
                // 그리는 중 예외가 나도 창이 멈추거나 빈 화면으로 굳지 않도록 한 번만 로그에 남기고 계속 진행한다
                if (!_paintFailed) { _paintFailed = true; _session.World.AddLog(Now, 2, "그리기 오류: " + ex.Message); Console.Error.WriteLine(ex); }
            }
            e.Graphics.DrawImageUnscaled(_frame, 0, 0);
        }
        private Bitmap _frame;
        private bool _paintFailed;

        protected override void OnPaintBackground(PaintEventArgs pevent) { /* 전체를 직접 그린다 */ }

        // ------------------------------------------------------------------ 마우스

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            UpdateMouse(e.X, e.Y);
            long now = Now;
            if (_view.HeroSelectOpen)
            {
                if (e.Button == MouseButtons.Left && !ClickHeroSelect(e.X, e.Y)) _view.HeroSelectOpen = false;
                return;
            }
            if (e.Button == MouseButtons.Left && ClickMiniMap(e.X, e.Y, now)) return;
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
            UpdateMouse(e.X, e.Y);
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

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) _rightDown = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _view.HasMouse = false; _rightDown = false; base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
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
            return k == Keys.Tab || k == Keys.Space || k == Keys.Escape || k == Keys.Enter || (k >= Keys.D1 && k <= Keys.D9) || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            long now = Now;
            Keys k = e.KeyCode;
            if (_view.HeroSelectOpen) { HeroSelectKey(k); e.Handled = true; return; }
            if (k == Keys.H) { OpenHeroSelect(); e.Handled = true; return; }
            if (k >= Keys.D1 && k <= Keys.D9) { UseSkillSlot(k - Keys.D1, now); e.Handled = true; }
            else if (k >= Keys.NumPad1 && k <= Keys.NumPad9) { UseSkillSlot(k - Keys.NumPad1, now); e.Handled = true; }
            else switch (k)
                {
                    case Keys.Q: BasicAttack(now); break;
                    case Keys.B: SelectBoss(now); break;
                    case Keys.F12: SaveScreenshot(); break;
                    case Keys.A: _pilot.SetAuto(_pilot.Mode != PilotMode.Auto); break;
                    case Keys.F: _follow = !_follow; break;
                    case Keys.Space: _follow = true; break;
                    case Keys.Tab: CycleTarget(now, e.Shift); e.Handled = true; break;
                    case Keys.Escape: _pilot.SetAuto(false); _view.SelectedId = 0; _session.StopMoving(now); break;
                    case Keys.F1: _view.ShowHelp = !_view.ShowHelp; break;
                    case Keys.L: _view.ShowLog = !_view.ShowLog; break;
                    case Keys.M:
                        _sfx.Enabled = !_sfx.Enabled;
                        _session.World.AddLog(now, 0, _sfx.Failed ? "효과음을 재생할 수 없습니다(오디오 장치 확인)" : (_sfx.Enabled ? "효과음 켜짐" : "효과음 꺼짐"));
                        break;
                    case Keys.F5: StartSession(); break;
                    case Keys.Oemplus: case Keys.Add: ZoomBy(1.15); break;
                    case Keys.OemMinus: case Keys.Subtract: ZoomBy(1 / 1.15); break;
                    case Keys.Left: PanCamera(-4, 0); break;
                    case Keys.Right: PanCamera(4, 0); break;
                    case Keys.Up: PanCamera(0, -4); break;
                    case Keys.Down: PanCamera(0, 4); break;
                }
            base.OnKeyDown(e);
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
            if (k == Keys.Escape || k == Keys.H) { _view.HeroSelectOpen = false; return; }
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
                    using (Graphics g = Graphics.FromImage(bmp)) _renderer.Draw(g, _session, _pilot, _view, Now);
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
            if (sk == null) return;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_session != null) { _session.Leave(); _session.Dispose(); }
                if (_frame != null) _frame.Dispose();
                _sfx.Dispose();
                _renderer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
