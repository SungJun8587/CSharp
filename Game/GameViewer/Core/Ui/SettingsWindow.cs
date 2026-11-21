//***************************************************************************
// SettingsWindow.cs : 환경설정 창. 소리 / 화면 / 조작(키 설정) 탭. ViewSettings 를 직접 고치고 바뀔 때마다 Changed 를 부른다(적용+저장).
// 키 설정: 줄을 누르면 "키를 누르세요" 상태가 되어 다음 키를 그 동작에 묶는다(Esc 취소, Backspace/Delete 기본값, 이미 쓰는 키는 서로 맞바꿈).
//***************************************************************************
using System;
using System.Globalization;
using GameViewer.Core.Logic;

namespace GameViewer.Core.Ui
{
    public sealed class SettingsWindow : UiWindow
    {
        public const string WindowId = "settings2";
        private static readonly string[] TabNames = { "소리", "화면", "조작" };
        private readonly ViewSettings _s;
        private readonly Action _changed;
        private readonly UiPanel[] _pages = new UiPanel[3];
        private readonly UiScrollList _keys;
        private readonly UiLabel _keyMsg;
        private readonly UiTabBar _tabs;
        private int _capture = -1;                 // 키를 기다리는 동작(BindAction), 없으면 -1
        private string _msg = "";

        public SettingsWindow(ViewSettings settings, Action changed) : base(WindowId, "환경설정", 440, 430)
        {
            _s = settings; _changed = changed;
            _tabs = Add(new UiTabBar(TabNames, new UiRect(16, 38, 408, 28), delegate (int i) { _capture = -1; ShowPage(i); }));
            for (int i = 0; i < 3; ++i)
            {
                _pages[i] = Add(new UiPanel(new UiRect(16, 76, 408, 296)) { Fill = 0, Border = 0 });
                _pages[i].Visible = i == 0;
            }

            // 소리
            UiPanel p = _pages[0];
            p.Add(new UiCheckbox("효과음 켜기", new UiRect(8, 12, 380, 26), _s.SfxOn, v => { _s.SfxOn = v; Apply(); }));
            p.Add(new UiSlider("효과음 크기", new UiRect(8, 46, 392, 28), 0, 100, _s.VolumePercent, v => { _s.VolumePercent = v; Apply(); }) { Step = 5, Format = Pct });
            p.Add(new UiCheckbox("배경음악 켜기", new UiRect(8, 94, 380, 26), _s.BgmOn, v => { _s.BgmOn = v; Apply(); }));
            p.Add(new UiSlider("배경음악 크기", new UiRect(8, 128, 392, 28), 0, 100, _s.BgmVolumePercent, v => { _s.BgmVolumePercent = v; Apply(); }) { Step = 5, Format = Pct });
            p.Add(new UiLabel("소리 장치를 쓸 수 없으면 켜도 들리지 않습니다.", new UiRect(8, 176, 392, 20), new UiTextStyle(11, UiTheme.TextDim)));

            // 화면
            p = _pages[1];
            p.Add(new UiLabel("그래픽 품질", new UiRect(8, 12, 110, 28), new UiTextStyle(13, UiTheme.Text)));
            UiButton q = p.Add(new UiButton("", new UiRect(124, 12, 120, 28), null));
            q.Text = ViewSettings.QualityName(_s.Quality);
            q.Click = delegate { _s.CycleQuality(); q.Text = ViewSettings.QualityName(_s.Quality); Apply(); };
            p.Add(new UiLabel("누를 때마다 높음 → 중간 → 낮음", new UiRect(254, 12, 150, 28), new UiTextStyle(11, UiTheme.TextDim)));
            p.Add(new UiCheckbox("FPS/통신 정보 표시", new UiRect(8, 54, 380, 26), _s.ShowFps, v => { _s.ShowFps = v; Apply(); }));
            p.Add(new UiCheckbox("피해 숫자 표시", new UiRect(8, 84, 380, 26), _s.ShowDamageNumbers, v => { _s.ShowDamageNumbers = v; Apply(); }));
            p.Add(new UiCheckbox("화면 효과(저체력 테두리, 피격 번쩍임)", new UiRect(8, 114, 380, 26), _s.ShowScreenFx, v => { _s.ShowScreenFx = v; Apply(); }));
            p.Add(new UiCheckbox("날씨와 낮밤 색조", new UiRect(8, 144, 380, 26), _s.WeatherOn, v => { _s.WeatherOn = v; Apply(); }));
            p.Add(new UiCheckbox("화면 흔들림 줄이기", new UiRect(8, 174, 380, 26), _s.ReduceShake, v => { _s.ReduceShake = v; Apply(); }));
            p.Add(new UiSlider("타격감 세기", new UiRect(8, 216, 392, 28), 0, 150, _s.FeedbackPercent, v => { _s.FeedbackPercent = v; Apply(); }) { Step = 10, Format = Pct });

            // 조작
            p = _pages[2];
            _keys = p.Add(new UiScrollList(new UiRect(0, 0, 408, 232), 30, delegate { return KeyBindings.ActionCount; }));
            _keys.DrawRow = DrawKeyRow;
            _keys.SelectChanged = delegate (int i) { _capture = i; _msg = i >= 0 ? "새로 쓸 키를 누르세요. (Esc 취소, Backspace 기본값)" : ""; };
            _keys.Activate = delegate (int i) { _capture = i; };
            _keyMsg = p.Add(new UiLabel("", new UiRect(0, 240, 408, 20), new UiTextStyle(12, UiTheme.Gold)) { TextFn = delegate { return _msg; } });
            p.Add(new UiButton("모두 기본값", new UiRect(0, 264, 120, 28), delegate { _s.Keys.ResetAll(); _msg = "모든 키를 기본값으로 되돌렸습니다"; _capture = -1; Apply(); }));
            p.Add(new UiLabel("줄을 누른 뒤 키를 누르면 바뀝니다.", new UiRect(130, 264, 278, 28), new UiTextStyle(11, UiTheme.TextDim)));

            Add(new UiButton("닫기", new UiRect(170, 384, 100, 30), Close));
        }

        private static string Pct(int v) { return v.ToString(CultureInfo.InvariantCulture) + "%"; }

        private void Apply() { if (_changed != null) _changed(); }

        public UiTabBar Tabs { get { return _tabs; } }
        public UiScrollList KeyList { get { return _keys; } }
        public bool Capturing { get { return _capture >= 0 && _tabs.Selected == 2; } }

        private void ShowPage(int i) { for (int k = 0; k < 3; ++k) _pages[k].Visible = k == i; }

        private void DrawKeyRow(IUiCanvas c, int i, UiRect r, bool sel, bool hover)
        {
            BindAction a = (BindAction)i;
            c.Text(KeyBindings.ActionName(a), new UiRect(r.X + 10, r.Y, r.W - 170, r.H), new UiTextStyle(13, UiTheme.Text));
            bool cap = Capturing && _capture == i;
            UiRect chip = new UiRect(r.Right - 140, r.Y + 3, 124, r.H - 8);
            c.Fill(chip, cap ? UiTheme.WithAlpha(UiTheme.Gold, 70) : UiTheme.SlotBg, 5);
            c.Stroke(chip, cap ? UiTheme.Gold : UiTheme.SlotBorder, 1.5f, 5);
            c.Text(cap ? "키를 누르세요…" : KeyBindings.KeyName(_s.Keys.Get(a)), chip, new UiTextStyle(12, _s.Keys.IsDefault(a) || cap ? UiTheme.Text : UiTheme.Good, UiAlign.Center, true));
        }

        // 선택도 풀어야 같은 줄을 다시 눌렀을 때 다시 키 입력을 기다린다(선택이 안 바뀌면 SelectChanged 가 안 불린다).
        private void EndCapture() { _capture = -1; _keys.Selected = -1; }

        public override bool OnKeyDown(int vk)
        {
            if (!Capturing) return false;
            BindAction a = (BindAction)_capture;
            if (vk == 27) { EndCapture(); _msg = ""; return true; }
            if (vk == 8 || vk == 46)
            {
                _s.Keys.Reset(a); _msg = KeyBindings.ActionName(a) + ": 기본 키(" + KeyBindings.KeyName(_s.Keys.Get(a)) + ")로 되돌렸습니다"; EndCapture(); Apply(); return true;
            }
            BindAction other;
            BindResult r = _s.Keys.Set(a, vk, out other);
            if (r == BindResult.Reserved) { _msg = KeyBindings.KeyName(vk) + " 키는 쓸 수 없습니다(예약된 키)"; return true; }
            _msg = KeyBindings.ActionName(a) + ": " + KeyBindings.KeyName(vk) + (r == BindResult.Swapped ? "  (" + KeyBindings.ActionName(other) + " 와 키를 맞바꿨습니다)" : "");
            EndCapture();
            Apply();
            return true;
        }
    }
}
