//***************************************************************************
// MainForm.cs : 뷰어 창. 타이머로 GameCanvas.Tick() 을 돌린다.
//***************************************************************************
using System;
using System.Drawing;
using System.Windows.Forms;
using GameViewer.Core;
using GameViewer.Core.Data;

namespace GameViewer.App
{
    public sealed class MainForm : Form
    {
        private readonly GameCanvas _canvas;
        private readonly Timer _timer = new Timer();
        private readonly ViewerConfig _cfg;
        private readonly bool _demo;

        public MainForm(ViewerConfig cfg, GameData data, bool demo)
        {
            _cfg = cfg; _demo = demo;
            Text = BuildTitle();
            ClientSize = new Size(cfg.WindowWidth, cfg.WindowHeight);
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

            _canvas = new GameCanvas(cfg, data, demo);
            _canvas.Dock = DockStyle.Fill;
            Controls.Add(_canvas);

            _timer.Interval = 16;
            _timer.Tick += delegate { _canvas.Tick(); if ((_ticks++ & 31) == 0) Text = BuildTitle(); };
            _timer.Start();
            Shown += delegate { _canvas.Focus(); };
        }

        private int _ticks;

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
