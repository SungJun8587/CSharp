//***************************************************************************
// MaterialFx.cs : 재질별 타격 파편과 사망 파열. 슬라임은 물방울, 해골은 뼛조각, 골렘은 돌조각, 나무는 나뭇잎과 조각,
//   폭탄/기사는 금속 불꽃, 불꽃은 불씨, 유령은 영기 방울이 튄다. 상태를 두지 않고 시작 시각 + 해시 난수로 매 프레임 같은 궤적을 계산한다.
//***************************************************************************
using System;
using System.Drawing;
using GameViewer.Core;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class MaterialFx
    {
        private readonly Resources _res;
        private readonly EffectRenderer _fx;
        private readonly PointF[] _q = new PointF[4];

        public MaterialFx(Resources res, EffectRenderer fx) { _res = res; _fx = fx; }

        public static uint Seed(GameViewer.Core.World.ViewEffect fx) { return (uint)((fx.StartMs & 0x7FFF) | (long)((fx.EntityId & 0xFF) << 15)); }
        private static int QA(int a) { return Math.Max(0, Math.Min(255, (a + 8) & ~15)); }
        private SolidBrush Br(int rgb, int a) { return _res.Brush(QA(a), (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        private static float Rn(uint seed, int i) { return Noise.Hash01(i * 7 + (int)(seed & 0x7FFF), i * 3 + (int)((seed >> 15) & 0xFF), 0x6Du); }
        private static int Dark(int rgb, float k) { int r = (int)(((rgb >> 16) & 255) * k), g = (int)(((rgb >> 8) & 255) * k), b = (int)((rgb & 255) * k); return (r << 16) | (g << 8) | b; }

        /// <summary>조각 하나. 재질에 따라 모양이 다르다(각진 조각 / 물방울 / 잎 / 금속 불꽃 / 불씨 / 영기).</summary>
        private void Piece(Graphics g, HitMat m, int c1, int c2, float x, float y, float r, float rot, float vx, float vy, int a, float rnd)
        {
            int col = rnd < 0.5f ? c1 : c2;
            switch (m)
            {
                case HitMat.Gel:
                    g.FillEllipse(Br(Dark(col, 0.8f), a), x - r, y - r * 0.9f, r * 2, r * 2f);
                    g.FillEllipse(Br(col, a), x - r * 0.85f, y - r * 0.95f, r * 1.7f, r * 1.7f);
                    g.FillEllipse(Br(0xFFFFFF, a * 3 / 4), x - r * 0.45f, y - r * 0.6f, r * 0.55f, r * 0.45f);
                    break;
                case HitMat.Metal:
                {
                    float l = (float)Math.Sqrt(vx * vx + vy * vy) + 0.001f;
                    float tx = vx / l * r * 2.2f, ty = vy / l * r * 2.2f;
                    g.DrawLine(_res.Pen(QA(a), 255, 236, 160, 1.5f), x - tx, y - ty, x + tx, y + ty);
                    g.FillEllipse(Br(0xFFFFFF, a), x - r * 0.35f, y - r * 0.35f, r * 0.7f, r * 0.7f);
                    if (rnd < 0.4f) Chunk(g, Dark(0xC8CEDC, 0.9f), x, y, r * 1.1f, rot, a);
                    break;
                }
                case HitMat.Fire:
                    _fx.Glow(g, Color.FromArgb(255, (col >> 16) & 255, (col >> 8) & 255, col & 255), x, y, r * 3f, r * 3f, a / 255f);
                    g.FillEllipse(Br(0xFFF0B0, a), x - r * 0.5f, y - r * 0.5f, r, r);
                    break;
                case HitMat.Spirit:
                    _fx.Glow(g, Color.FromArgb(255, (col >> 16) & 255, (col >> 8) & 255, col & 255), x, y, r * 3.4f, r * 3.4f, a * 0.8f / 255f);
                    break;
                case HitMat.Plant:
                case HitMat.Wood:
                    if (m == HitMat.Plant || rnd > 0.45f) { Leaf(g, rnd < 0.5f ? 0x78BE50 : (m == HitMat.Plant ? 0xD8483C : 0x5E8A3C), x, y, r * 1.5f, rot, a); break; }
                    Chunk(g, col, x, y, r, rot, a);
                    break;
                case HitMat.Flesh:
                    g.FillEllipse(Br(col, a / 2), x - r * 1.5f, y - r * 1.2f, r * 3f, r * 2.4f);        // 먼지/털뭉치
                    break;
                default:
                    Chunk(g, col, x, y, r, rot, a);
                    break;
            }
        }

        private void Chunk(Graphics g, int rgb, float x, float y, float r, float rot, int a)
        {
            float c = (float)Math.Cos(rot), s = (float)Math.Sin(rot);
            // 찌그러진 사각형: 두 변 길이를 다르게 해 돌/뼈 조각처럼 보이게 한다
            float[] ax = { -1.0f, 0.9f, 1.1f, -0.7f }, ay = { -0.6f, -0.9f, 0.7f, 0.9f };
            for (int k = 0; k < 4; ++k) _q[k] = new PointF(x + (ax[k] * c - ay[k] * s) * r, y + (ax[k] * s + ay[k] * c) * r);
            g.FillPolygon(Br(Dark(rgb, 0.72f), a), _q);
            for (int k = 0; k < 4; ++k) _q[k] = new PointF(x + (ax[k] * 0.6f * c - ay[k] * 0.6f * s) * r - r * 0.12f, y + (ax[k] * 0.6f * s + ay[k] * 0.6f * c) * r - r * 0.12f);
            g.FillPolygon(Br(rgb, a), _q);
        }

        private void Leaf(Graphics g, int rgb, float x, float y, float r, float rot, int a)
        {
            float c = (float)Math.Cos(rot), s = (float)Math.Sin(rot);
            _q[0] = new PointF(x - c * r, y - s * r); _q[1] = new PointF(x - s * r * 0.45f, y + c * r * 0.45f);
            _q[2] = new PointF(x + c * r, y + s * r); _q[3] = new PointF(x + s * r * 0.45f, y - c * r * 0.45f);
            g.FillPolygon(Br(rgb, a), _q);
        }

        /// <summary>맞았을 때 튀는 재질 조각. dirA 는 맞은 방향(NaN 이면 사방), p 는 연출 진행도 0~1.</summary>
        public void Debris(Graphics g, HitMat m, uint seed, float sx, float cy, float dirA, bool heavy, float p, ViewState v)
        {
            int c1, c2; HitMaterial.Colors(m, out c1, out c2);
            int n = heavy ? 9 : 5;
            float fade = 1f - p * p;
            for (int k = 0; k < n; ++k)
            {
                float a = float.IsNaN(dirA) ? Rn(seed, k) * 6.2832f : dirA + (Rn(seed, k) - 0.5f) * 2.4f;
                float spd = v.Px((1.0f + 2.2f * Rn(seed, k + 20)) * (heavy ? 1.3f : 1f));
                float vx = (float)Math.Cos(a) * spd, vy = (float)Math.Sin(a) * spd * 0.7f - v.Px(1.4f);
                float x = sx + vx * p, y = cy + vy * p + v.Px(4.2f) * p * p;
                float r = Math.Max(1.5f, v.Px(0.11f + 0.09f * Rn(seed, k + 40)));
                Piece(g, m, c1, c2, x, y, r, p * 9f * (Rn(seed, k + 60) - 0.5f) + k, vx, vy + v.Px(8.4f) * p, (int)(255 * fade), Rn(seed, k + 80));
            }
        }

        /// <summary>죽을 때 터지는 재질 파열. size 는 몸 크기(월드 단위, 대략 키).</summary>
        public void Burst(Graphics g, HitMat m, uint seed, float sx, float sy, float p, ViewState v, float size)
        {
            if (m == HitMat.Flesh) return;                       // 살점 몬스터는 기존 연기만
            int c1, c2; HitMaterial.Colors(m, out c1, out c2);
            float s = Math.Max(0.8f, Math.Min(2.4f, size * 0.45f));
            float fade = 1f - p * p;
            int n = 16;
            if (m == HitMat.Gel)
            {
                float pr = v.Px(0.9f * s) * (0.4f + 0.7f * Math.Min(1f, p * 2.2f));                  // 퍼지는 웅덩이
                g.FillEllipse(Br(0x4CBF5C, (int)(110 * fade)), sx - pr, sy - pr * 0.4f, pr * 2, pr * 0.8f);
            }
            for (int k = 0; k < n; ++k)
            {
                float a = Rn(seed, k) * 6.2832f;
                float spd = v.Px((0.9f + 2.4f * Rn(seed, k + 20)) * s);
                float vx = (float)Math.Cos(a) * spd, vy = -v.Px((1.6f + 2.2f * Rn(seed, k + 90)) * s);
                if (m == HitMat.Fire || m == HitMat.Spirit) vy = -v.Px((1.2f + 2.6f * Rn(seed, k + 90)) * s) * (0.4f + p);
                float x = sx + vx * p * 0.8f;
                float y = (m == HitMat.Fire || m == HitMat.Spirit) ? sy - v.Px(0.8f * s) + vy * p : sy - v.Px(0.6f * s) + vy * p + v.Px(5.0f * s) * p * p;
                float r = Math.Max(2f, v.Px((0.13f + 0.12f * Rn(seed, k + 40)) * s));
                Piece(g, m, c1, c2, x, y, r, p * 10f * (Rn(seed, k + 60) - 0.5f) + k, vx, vy, (int)(255 * fade), Rn(seed, k + 80));
            }
        }
    }
}
