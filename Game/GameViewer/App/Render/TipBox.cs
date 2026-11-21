//***************************************************************************
// TipBox.cs : 툴팁 상자 하나를 그린다(스킬바/영웅 카드 공용). 줄 내용은 Core/Logic/SkillTip 이 만든다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class TipBox
    {
        private readonly Resources _res;
        public TipBox(Resources res) { _res = res; }

        private static Color ColorOf(TipLine l)
        {
            switch (l.Kind)
            {
                case TipKind.Title: return Color.FromArgb(255, 232, 150);
                case TipKind.Effect:
                    if (l.Text.StartsWith("치유")) return Color.FromArgb(150, 235, 160);
                    if (l.Text.StartsWith("효과")) return Color.FromArgb(205, 176, 255);
                    return Color.FromArgb(255, 176, 130);
                case TipKind.Note: return Color.FromArgb(168, 180, 172);
                default: return Color.FromArgb(226, 234, 226);
            }
        }

        /// <summary>(ax, ay) 를 기준으로 상자를 놓는다. above 면 ay 위쪽에, 아니면 아래쪽에 놓고 화면 안으로 밀어 넣는다.</summary>
        public void Draw(IGfx g, List<TipLine> lines, float ax, float ay, int sw, int sh, bool above)
        {
            if (lines == null || lines.Count == 0) return;
            float w = 0;
            for (int i = 0; i < lines.Count; ++i)
                w = Math.Max(w, g.MeasureString(lines[i].Text, lines[i].Kind == TipKind.Title ? _res.Bold : _res.Small).Width);
            w += 22f;
            float h = 12f + lines.Count * 17f + (lines.Count > 1 ? 3f : 0f);
            float x = Math.Max(6f, Math.Min(sw - w - 6f, ax - w / 2f));
            float y = above ? ay - h - 8f : ay + 8f;
            y = Math.Max(6f, Math.Min(sh - h - 6f, y));
            g.FillRectangle(_res.Brush(238, 10, 14, 18), x, y, w, h);
            g.DrawRectangle(_res.Pen(210, 214, 190, 120, 1.2f), x, y, w, h);
            float ty = y + 6f;
            for (int i = 0; i < lines.Count; ++i)
            {
                bool title = lines[i].Kind == TipKind.Title;
                g.DrawString(lines[i].Text, title ? _res.Bold : _res.Small, _res.Brush(ColorOf(lines[i])), x + 11f, ty);
                ty += 17f + (title ? 3f : 0f);
            }
        }
    }
}
