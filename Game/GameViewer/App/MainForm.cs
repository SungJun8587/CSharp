//***************************************************************************
// MainForm.cs : 뷰어 창. 타이머로 GameCanvas.Tick() 을 돌린다.
//***************************************************************************
using System;
using System.Drawing;
using System.Windows.Forms;
using GameViewer.Core;

namespace GameViewer.App
{
    public sealed class MainForm : Form
    {
        private readonly GameCanvas _canvas;
        private readonly Timer _timer = new Timer();
        private readonly ViewerConfig _cfg;
        private readonly bool _demo;

        public MainForm(ViewerConfig cfg, StartupLoader loader, bool demo)
        {
            _cfg = cfg; _demo = demo;
            Text = BuildTitle();
            {
                // 작은 화면에서는 작업 영역(작업 표시줄 제외) 안에 들어오게 줄인다.
                Rectangle wa0 = Screen.PrimaryScreen.WorkingArea;
                ClientSize = new Size(Math.Min(cfg.WindowWidth, Math.Max(640, wa0.Width - 24)), Math.Min(cfg.WindowHeight, Math.Max(480, wa0.Height - 60)));
            }
            if (cfg.Slot == 0) StartPosition = FormStartPosition.CenterScreen;
            else
            {
                // 같은 PC 에서 여러 창을 띄웠을 때 서로 가리지 않게 슬롯 번호만큼 비껴 놓는다.
                StartPosition = FormStartPosition.Manual;
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int step = 40 * (cfg.Slot % 10);
                int x = wa.Left + Math.Max(0, (wa.Width - Width) / 2) + step;
                int y = wa.Top + Math.Max(0, (wa.Height - Height) / 2) + step;
                Location = new Point(Math.Max(wa.Left, Math.Min(x, wa.Right - 200)), Math.Max(wa.Top, Math.Min(y, wa.Bottom - 120)));
            }
            BackColor = Color.Black;
            MinimumSize = new Size(640, 480);

            _canvas = new GameCanvas(cfg, loader, demo);
            _canvas.Dock = DockStyle.Fill;
            Controls.Add(_canvas);

            // Windows 의 폼 타이머는 약 15.6ms 단위로만 깨어나서 Interval 16 이면 31ms(약 32fps)로 굳는다.
            // 짧게(8ms) 걸어 두고, 너무 일찍 들어온 틱은 건너뛰어 약 16ms 간격(60fps 상한)을 지킨다.
            _timer.Interval = 8;
            _paceSw.Start();
            _timer.Tick += delegate
            {
                long t = _paceSw.ElapsedMilliseconds;
                if (t - _lastTickMs < 14) return;
                _lastTickMs = t;
                _canvas.Tick(); if ((_ticks++ & 31) == 0) Text = BuildTitle();
            };
            _timer.Start();
            Shown += delegate { _canvas.Focus(); };
        }

        private int _ticks;
        private readonly System.Diagnostics.Stopwatch _paceSw = new System.Diagnostics.Stopwatch();
        private long _lastTickMs = -100;

        private string BuildTitle()
        {
            string where = _demo ? "데모" : _cfg.Host + ":" + _cfg.Port;
            return "Game Viewer - " + where + "  존 " + _cfg.ZoneId + "  캐릭터 " + _cfg.CharId;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Stop(); _timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
