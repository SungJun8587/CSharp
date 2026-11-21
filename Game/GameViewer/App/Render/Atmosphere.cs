//***************************************************************************
// Atmosphere.cs : 숲의 공기. 나뭇잎 사이로 비스듬히 내리는 빛줄기와, 천천히 흔들리며 떨어지는 낙엽/꽃가루.
// 전부 시간의 함수라서 상태를 따로 갖지 않고(결정적), 프레임마다 메모리를 새로 잡지 않는다.
// 화면 좌표로 그리므로 카메라가 움직여도 하늘 쪽 빛이 따라다니지 않고 천천히 일렁이기만 한다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed partial class Atmosphere : IDisposable
    {
        private const int Beams = 5;
        private const int Leaves = 12;
        private readonly Resources _res;
        private const int Levels = 6;
        private readonly Bitmap[] _beam = new Bitmap[Levels];      // 밝기 단계별 (ColorMatrix 는 느려서 쓰지 않는다)
        private readonly PointF[] _dst = new PointF[3];
        private static readonly Color[] LeafColors = {
            Color.FromArgb(200, 176, 92, 36), Color.FromArgb(200, 200, 150, 48), Color.FromArgb(200, 138, 70, 36), Color.FromArgb(180, 120, 150, 60), Color.FromArgb(150, 255, 244, 170) };

        public Atmosphere(Resources res)
        {
            _res = res;
            // 빛줄기 한 장: 가로로는 가운데가 진하고 가장자리는 투명, 세로로는 위(나뭇잎 틈)가 진하고 아래로 사라진다.
            const int W = 32, H = 128;
            for (int lv = 0; lv < Levels; ++lv)
            {
                _beam[lv] = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                float gain = (lv + 1) / (float)Levels;
                for (int y = 0; y < H; ++y)
                {
                    float fy = (float)Math.Pow(1.0 - y / (double)(H - 1), 1.4);
                    for (int x = 0; x < W; ++x)
                    {
                        double s = Math.Sin(Math.PI * x / (W - 1));
                        _beam[lv].SetPixel(x, y, Color.FromArgb((int)(84 * s * s * fy * gain), 255, 246, 205));
                    }
                }
            }
        }

        public void Dispose() { foreach (Bitmap b in _beam) b.Dispose(); if (_beamLayer != null) _beamLayer.Dispose(); }

        public void Draw(IGfx g, ViewState v, long now)
        {
            if (ZoneTheme.Current == MapKind.Desert) { DrawDesertAir(g, v, now); return; }
            if (ZoneTheme.Current != MapKind.Forest) { DrawBiomeAir(g, v, now, ZoneTheme.Current); return; }
            DrawForestGrade(g, v, now);
            DrawBeams(g, v, now);
            DrawLeaves(g, v, now);
            DrawFireflies(g, v, now);
        }

        /// <summary>숲의 색조: 위쪽 햇빛은 따뜻하게, 아래쪽은 차갑게 눌러 분위기를 만든다.</summary>
        private void DrawForestGrade(IGfx g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            using (LinearGradientBrush t = GfxBrush.Linear(new RectangleF(0, 0, W, H + 1), Color.FromArgb(22, 255, 238, 160), Color.FromArgb(16, 20, 60, 90), LinearGradientMode.Vertical))
                g.FillRectangle(t, 0, 0, W, H);
        }

        /// <summary>반딧불/꽃가루: 천천히 떠다니며 깜박이는 노란 빛 점.</summary>
        private void DrawFireflies(IGfx g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            for (int i = 0; i < 22; ++i)
            {
                uint h = Noise.Hash(i, 9, 421);
                double t = now / 1000.0;
                float x = ((h >> 4) & 1023) / 1023f * W + (float)Math.Sin(t * (0.2 + (h & 3) * 0.07) + i) * 46f;
                float y = ((h >> 14) & 1023) / 1023f * H + (float)Math.Cos(t * (0.17 + ((h >> 2) & 3) * 0.06) + i * 1.7) * 34f;
                float tw = 0.5f + 0.5f * (float)Math.Sin(t * (1.4 + (h & 7) * 0.2) + i * 2.1);
                int a = (int)(150 * tw * tw);
                if (a < 6) continue;
                g.FillEllipse(_res.Brush(a / 4, 255, 244, 140), x - 7, y - 7, 14, 14);
                g.FillEllipse(_res.Brush(a, 255, 250, 190), x - 1.8f, y - 1.8f, 3.6f, 3.6f);
            }
        }

        /// <summary>사막의 공기: 뜨거운 햇빛의 따뜻한 색조, 위쪽의 아지랑이 빛, 바람에 쓸려 가는 모래 줄기와 먼지.</summary>
        private void DrawDesertAir(IGfx g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            g.FillRectangle(_res.Brush(16, 255, 186, 96), 0, 0, W, H);
            using (LinearGradientBrush t = GfxBrush.Linear(new RectangleF(0, 0, W, H * 0.45f + 1), Color.FromArgb(46, 255, 226, 150), Color.FromArgb(0, 255, 226, 150), LinearGradientMode.Vertical))
                g.FillRectangle(t, 0, 0, W, H * 0.45f);
            // 아지랑이: 천천히 일렁이는 밝은 띠
            for (int i = 0; i < 4; ++i)
            {
                float yy = H * (0.18f + i * 0.2f) + (float)Math.Sin(now / 1900.0 + i * 1.7) * 14f;
                using (LinearGradientBrush b = GfxBrush.Linear(new RectangleF(0, yy - 18, W, 36), Color.FromArgb(0, 255, 240, 200), Color.FromArgb(0, 255, 240, 200), LinearGradientMode.Vertical))
                {
                    ColorBlend cb = new ColorBlend(3);
                    cb.Colors = new Color[] { Color.FromArgb(0, 255, 240, 200), Color.FromArgb(18, 255, 240, 200), Color.FromArgb(0, 255, 240, 200) };
                    cb.Positions = new float[] { 0f, 0.5f, 1f };
                    b.InterpolationColors = cb;
                    g.FillRectangle(b, 0, yy - 18, W, 36);
                }
            }
            // 바람 줄기
            for (int i = 0; i < 34; ++i)
            {
                uint h = Noise.Hash(i, 5, 311);
                double period = 2600 + (h & 1023) * 4;
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 5) % (long)period) / period;
                float y = ((h >> 4) & 1023) / 1023f * H + (float)Math.Sin(p * 6.28) * 6f;
                float len = 40 + ((h >> 14) & 127);
                float x = -len + (float)p * (W + len * 2f);
                float a = (float)Math.Sin(p * Math.PI);
                g.DrawLine(_res.Pen((int)(58 * a), 255, 236, 186, 1.4f), x, y, x + len, y + len * 0.06f);
            }
            // 먼지 알갱이
            for (int i = 0; i < 40; ++i)
            {
                uint h = Noise.Hash(i, 8, 313);
                double period = 5000 + (h & 1023) * 6;
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period;
                float x = (float)p * (W + 60f) - 30f + (float)Math.Sin(p * 11 + i) * 8f;
                float y = ((h >> 4) & 1023) / 1023f * H + (float)Math.Sin(p * 8 + i * 2) * 14f;
                float s = 1.2f + ((h >> 14) & 3) * 0.6f;
                g.FillEllipse(_res.Brush(120, 255, 232, 180), x, y, s, s);
            }
        }

        // 빛줄기는 아주 천천히 움직여서 4Hz 로만 반해상도 층에 다시 그리고, 프레임마다는 그 층을 한 번 깐다.
        // (기울어진 평행사변형 DrawImage 가 프레임당 가장 비싼 공기 연출이었다)
        private Bitmap _beamLayer; private long _beamAt = long.MinValue; private int _beamW, _beamH;
        private const int BeamRefreshMs = 250;

        private void DrawBeams(IGfx g, ViewState v, long now)
        {
            int lw = Math.Max(1, v.Width / 2), lh = Math.Max(1, v.Height / 2);
            if (_beamLayer == null || _beamW != lw || _beamH != lh)
            {
                if (_beamLayer != null) _beamLayer.Dispose();
                _beamLayer = new Bitmap(lw, lh, PixelFormat.Format32bppPArgb); _beamW = lw; _beamH = lh; _beamAt = long.MinValue;
            }
            if (now - _beamAt >= BeamRefreshMs || now < _beamAt)
            {
                _beamAt = now;
                using (GdiGfx lg = GdiGfx.FromImage(_beamLayer))
                {
                    lg.Clear(Color.Transparent);
                    lg.ScaleTransform(0.5f, 0.5f);
                    PaintBeams(lg, v.Width, v.Height, now);
                }
            }
            InterpolationMode im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(_beamLayer, 0, 0, v.Width, v.Height);
            g.InterpolationMode = im;
        }

        private void PaintBeams(IGfx g, float W, float H, long now)
        {
            float lean = H * 0.34f;
            InterpolationMode im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            for (int i = 0; i < Beams; ++i)
            {
                float t = now / 1000f;
                float cx = (i + 0.5f) / Beams * W + (float)Math.Sin(t * 0.11f + i * 1.9f) * W * 0.04f + W * 0.08f;
                float w = (80 + (i % 3) * 46) * (H / 800f);
                float k = 0.5f + 0.5f * (float)Math.Sin(t * 0.23f + i * 2.3f);        // 0..1 천천히 밝아졌다 어두워짐
                float inten = 0.25f + 0.75f * k;
                int lv = (int)(inten * Levels); if (lv >= Levels) lv = Levels - 1;
                _dst[0] = new PointF(cx - w * 0.5f + lean * 0.35f, -30f);          // 왼쪽 위
                _dst[1] = new PointF(cx + w * 0.5f + lean * 0.35f, -30f);          // 오른쪽 위
                _dst[2] = new PointF(cx - w * 0.5f - lean * 0.65f, H * 0.95f);     // 왼쪽 아래
                g.DrawImage(_beam[lv], _dst);
            }
            g.InterpolationMode = im;
        }

        private void DrawLeaves(IGfx g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            GfxState st = g.Save();
            for (int i = 0; i < Leaves; ++i)
            {
                uint h = Noise.Hash(i, 3, 211);
                double period = 9000 + (h & 1023) * 8;                              // 9~17초에 한 번 화면을 가로지름
                double p = ((now + (long)((h >> 10) & 0xFFFF) * 3) % (long)period) / period;
                float x = ((h >> 4) & 1023) / 1023f * W * 1.1f - W * 0.05f + (float)(p * W * 0.10) + (float)Math.Sin(p * 6.2832 * (2 + (h & 3)) + i) * 26f;
                float y = -20f + (float)p * (H + 40f);
                float s = 2.2f + ((h >> 14) & 7) * 0.45f;
                g.ResetTransform();
                g.TranslateTransform(x, y);
                g.RotateTransform((float)(p * 540.0 * (((h & 4) == 0) ? 1 : -1)) + i * 37f);
                bool pollen = (h % 5) == 4;
                if (pollen) g.FillEllipse(_res.Brush(LeafColors[4]), -1.5f, -1.5f, 3f, 3f);
                else g.FillEllipse(_res.Brush(LeafColors[(h >> 8) % 4]), -s, -s * 0.5f, s * 2f, s);
            }
            g.Restore(st);
        }
    }
}
