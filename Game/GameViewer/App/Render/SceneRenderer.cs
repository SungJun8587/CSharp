//***************************************************************************
// SceneRenderer.cs : 한 프레임 전체(바닥 -> 월드 -> HUD)를 Graphics 에 그리는 진입점.
// WinForms 캔버스와 PNG 스냅샷 도구가 같은 코드를 쓴다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using GameViewer.Core;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class SceneRenderer : IDisposable
    {
        public readonly Resources Res = new Resources();
        private readonly GroundRenderer _ground = new GroundRenderer();
        private readonly Atmosphere _air;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        /// <summary>구간별 누적 시간(ms): 바닥, 월드 밖 나무, 월드, 비네트, HUD. 성능 확인용.</summary>
        public readonly double[] PartMs = new double[5];
        public int GroundRebuilds { get { return _ground.Rebuilds; } }
        private void Lap(int i) { PartMs[i] += _sw.Elapsed.TotalMilliseconds; _sw.Restart(); }
        private readonly WorldRenderer _world;
        private readonly HudRenderer _hud;
        private readonly TensionHud _tension;

        public SceneRenderer()
        {
            _world = new WorldRenderer(Res);
            _air = new Atmosphere(Res);
            _hud = new HudRenderer(Res);
            _tension = new TensionHud(Res);
        }

        public void Draw(Graphics g, ViewerSession s, AutoPilot pilot, ViewState v, long now)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            g.Clear(Color.FromArgb(18, 40, 24));
            float shx, shy; s.World.ShakeOffset(now, out shx, out shy);
            g.TranslateTransform(shx, shy);
            _sw.Restart();
            _ground.Draw(g, v, s.Data.WorldMinX, s.Data.WorldMinY, s.Data.WorldMaxX, s.Data.WorldMaxY); Lap(0);
            _ground.DrawBorderTrees(g, v, _world.Trees, s.Data.WorldMinX, s.Data.WorldMinY, s.Data.WorldMaxX, s.Data.WorldMaxY); Lap(1);
            _world.DrawScene(g, s, v, now); Lap(2);
            g.ResetTransform();
            _air.Draw(g, v, now);
            DrawVignette(g, v);
            _tension.Draw(g, s, v, now); Lap(3);
            _hud.Draw(g, s, pilot, v, now); Lap(4);
        }

        /// <summary>화면 가장자리를 살짝 어둡게 해 시선을 가운데로 모은다. 가장자리 네 줄에 그라데이션만 깔아 비용이 작다.</summary>
        private void DrawVignette(Graphics g, ViewState v)
        {
            int e = Math.Max(60, Math.Min(v.Width, v.Height) / 5);
            Color dark = Color.FromArgb(120, 4, 14, 8), clear = Color.FromArgb(0, 4, 14, 8);
            using (LinearGradientBrush t = new LinearGradientBrush(new Rectangle(0, 0, v.Width, e + 1), dark, clear, LinearGradientMode.Vertical))
                g.FillRectangle(t, 0, 0, v.Width, e);
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, v.Height - e - 1, v.Width, e + 1), clear, dark, LinearGradientMode.Vertical))
                g.FillRectangle(b, 0, v.Height - e, v.Width, e);
            using (LinearGradientBrush l = new LinearGradientBrush(new Rectangle(0, 0, e + 1, v.Height), dark, clear, LinearGradientMode.Horizontal))
                g.FillRectangle(l, 0, 0, e, v.Height);
            using (LinearGradientBrush r = new LinearGradientBrush(new Rectangle(v.Width - e - 1, 0, e + 1, v.Height), clear, dark, LinearGradientMode.Horizontal))
                g.FillRectangle(r, v.Width - e, 0, e, v.Height);
        }

        public void Dispose() { _world.Dispose(); _ground.Dispose(); _air.Dispose(); Res.Dispose(); }
    }
}
