//***************************************************************************
// GateRenderer.cs : 존 사이 관문(ZoneLink)을 그린다. 존 가장자리의 두 돌기둥, 바닥에 번지는 빛, 바깥쪽을 가리키는 화살표, 건너편 존 이름.
// 바닥 지형이 건너편 존 색으로 섞이는 것은 GroundRenderer 가 ZoneGates 로 한다.
//***************************************************************************
using System;
using System.Drawing;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class GateRenderer
    {
        private readonly Resources _res;
        private readonly EffectRenderer _fx;
        public GateRenderer(Resources res, EffectRenderer fx) { _res = res; _fx = fx; }

        private static Color Accent(MapKind k)
        {
            switch (k)
            {
                case MapKind.Desert: return Color.FromArgb(255, 214, 120);
                case MapKind.Snow: return Color.FromArgb(170, 225, 255);
                case MapKind.Volcano: return Color.FromArgb(255, 140, 70);
                case MapKind.Swamp: return Color.FromArgb(170, 230, 120);
                case MapKind.Ruins: return Color.FromArgb(230, 200, 150);
                case MapKind.Coast: return Color.FromArgb(120, 230, 240);
                case MapKind.Crystal: return Color.FromArgb(240, 140, 255);
                case MapKind.Sky: return Color.FromArgb(210, 230, 255);
                case MapKind.Dark: return Color.FromArgb(190, 130, 255);
                default: return Color.FromArgb(130, 230, 130);
            }
        }

        public void Draw(IGfx g, GameData d, ViewState v, long now)
        {
            if (d.Gates.Count == 0) return;
            for (int i = 0; i < d.Gates.Count; ++i)
            {
                ZoneGate gt = d.Gates[i];
                float cx, cy; ZoneGates.CenterOf(gt, d.WorldMinX, d.WorldMinY, d.WorldMaxX, d.WorldMaxY, out cx, out cy);
                float sx = v.ToScreenX(cx), sy = v.ToScreenY(cy);
                if (sx < -260 || sx > v.Width + 260 || sy < -260 || sy > v.Height + 260) continue;
                float ox = gt.Edge == 'E' ? 1f : gt.Edge == 'W' ? -1f : 0f, oy = gt.Edge == 'S' ? 1f : gt.Edge == 'N' ? -1f : 0f;     // 바깥 방향
                float px = -oy, py = ox;                                                                                           // 가장자리를 따르는 방향
                MapKind nk = ZoneTheme.For(gt.TargetZone); Color ac = Accent(nk);
                float pulse = 0.65f + 0.35f * (float)Math.Sin(now / 380.0 + i);
                float half = gt.Width * 0.5f;
                // 바닥에 번지는 빛
                _fx.Glow(g, ac, sx, sy, v.Px(half * 1.35f), v.Px(half * 0.8f), 0.55f * pulse);
                // 바깥으로 흐르는 화살표
                for (int k = 0; k < 4; ++k)
                {
                    float ph = (float)(((now / 900.0) + k * 0.25) % 1.0);
                    float dist = -9f + ph * 14f;                       // 안쪽 9 에서 바깥 5 까지 흘러간다
                    float ax = cx + ox * dist, ay = cy + oy * dist;
                    float al = (float)Math.Sin(ph * Math.PI);
                    float asz = 1.5f;
                    PointF p0 = new PointF(v.ToScreenX(ax - ox * asz - px * asz), v.ToScreenY(ay - oy * asz - py * asz));
                    PointF p1 = new PointF(v.ToScreenX(ax + ox * asz), v.ToScreenY(ay + oy * asz));
                    PointF p2 = new PointF(v.ToScreenX(ax - ox * asz + px * asz), v.ToScreenY(ay - oy * asz + py * asz));
                    Pen pn = _res.Pen(Color.FromArgb((int)(220 * al), ac), 3f);
                    g.DrawLine(pn, p0, p1); g.DrawLine(pn, p1, p2);
                }
                // 두 돌기둥
                for (int s = -1; s <= 1; s += 2)
                {
                    float bx = v.ToScreenX(cx + px * half * s), by = v.ToScreenY(cy + py * half * s);
                    float w = v.Px(0.55f), h = v.Px(2.9f);
                    g.FillEllipse(_res.Brush(60, 0, 0, 0), bx - w * 1.3f, by - w * 0.35f, w * 2.6f, w * 0.7f);
                    using (System.Drawing.Drawing2D.LinearGradientBrush lb = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(bx - w, by - h, w * 2, h), Color.FromArgb(150, 150, 160), Color.FromArgb(78, 78, 92), 0f))
                        g.FillRectangle(lb, bx - w, by - h, w * 2, h);
                    g.DrawRectangle(_res.Pen(Color.FromArgb(150, 20, 20, 30), 1.5f), bx - w, by - h, w * 2, h);
                    g.FillRectangle(_res.Brush(255, 118, 118, 130), bx - w * 1.25f, by - h - w * 0.45f, w * 2.5f, w * 0.55f);        // 머릿돌
                    _fx.Glow(g, ac, bx, by - h - w * 0.8f, v.Px(0.9f) * (0.8f + 0.3f * pulse), v.Px(0.9f) * (0.8f + 0.3f * pulse), 0.9f);   // 기둥 위 빛 구슬
                    g.FillEllipse(_res.Brush(255, ac.R, ac.G, ac.B), bx - w * 0.38f, by - h - w * 1.15f, w * 0.76f, w * 0.76f);
                }
                // 건너편 존 이름
                string label = ZoneTheme.NameOf(gt.TargetZone) + (gt.Edge == 'E' ? " →" : gt.Edge == 'W' ? "← " : gt.Edge == 'N' ? " ↑" : " ↓");
                _res.OutlinedText(g, label, _res.Bold, ac, sx, sy - v.Px(4.0f), _res.Center);
            }
        }
    }
}
