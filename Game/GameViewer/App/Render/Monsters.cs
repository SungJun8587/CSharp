//***************************************************************************
// Monsters.cs : 이미지 에셋(Assets/sprites.json)이 없는 몬스터를 위한 기본형 그림 하나(말랑한 눈 달린 덩어리).
// Creatures.cs 의 영웅과 같은 규칙(단위 좌표, 발 밑 (0,0), 프레임 0~7 걷기, 8 대기). 몬스터 종류별 그림은 모두 이미지 에셋이 맡는다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GameViewer.App.Render
{
    internal static class Monsters
    {
        private static readonly Color Eyewhite = Color.FromArgb(252, 252, 250);
        private static readonly Color Pupil = Color.FromArgb(44, 40, 54);

        public static void Paint(IGfx g, uint tpl, int frame)
        {
            float sn = frame < CreatureRenderer.FrameIdle ? (float)Math.Sin(frame / 8f * 6.2832f) : 0f;
            Color c = WorldRenderer.MonsterColor(tpl);
            Blob(g, sn, Col.Light(c, 0.2f), Col.Dark(c, 0.4f));
        }

        private static Color Edge(Color c) { return Col.Alpha(Col.Dark(c, 0.55f), 175); }

        /// <summary>크고 동그란 눈: 흰자 + 가로 줄 동공. look 은 -1..1.</summary>
        private static void GoogleEye(IGfx g, float cx, float cy, float r, float look, Color pupil)
        {
            P.Ball(g, cx, cy, r, r * 1.06f, Eyewhite, Color.FromArgb(206, 210, 222), Color.FromArgb(160, 70, 70, 96), Math.Max(0.02f, r * 0.08f));
            P.Fill(g, cx + look * r * 0.32f, cy + r * 0.14f, r * 0.5f, r * 0.15f, pupil);
        }

        /// <summary>벌린 입 (안쪽 + 혀). w, h 는 반폭/반높이.</summary>
        private static void Mouth(IGfx g, float cx, float cy, float w, float h)
        {
            P.Fill(g, cx, cy, w, h, Color.FromArgb(120, 26, 44));
            P.Fill(g, cx, cy + h * 0.42f, w * 0.55f, h * 0.42f, Color.FromArgb(255, 124, 140));
        }

        private static void Blob(IGfx g, float sn, Color light, Color dark)
        {
            const float s = 1f;
            float sq = sn * 0.07f;
            float w = 0.95f * s * (1f + sq), h = 0.92f * s * (1f - sq * 1.4f);
            P.Fill(g, 0f, -0.03f * s, w * 1.0f, h * 0.2f, Color.FromArgb(70, 10, 20, 20));
            P.Ball(g, 0f, -h * 0.95f, w, h, Col.Alpha(Col.Light(light, 0.25f), 245), Col.Alpha(dark, 240), Edge(dark), 0.04f * s);
            P.Ball(g, w * 0.06f, -h * 0.74f, w * 0.55f, h * 0.42f, Col.Alpha(Col.Light(light, 0.35f), 90), Col.Alpha(light, 10), Color.Transparent, 0f);
            P.Fill(g, -w * 0.42f, -h * 1.55f, w * 0.2f, h * 0.1f, Color.FromArgb(160, 255, 255, 255));
            GoogleEye(g, -w * 0.34f, -h * 1.16f, 0.24f * s, 0f, Pupil);
            GoogleEye(g, w * 0.34f, -h * 1.16f, 0.24f * s, 0f, Pupil);
            Mouth(g, 0f, -h * 0.74f, w * 0.2f, h * 0.13f);
        }
    }
}
