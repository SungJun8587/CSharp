//***************************************************************************
// Atmosphere.cs : 숲의 공기. 나뭇잎 사이로 비스듬히 내리는 빛줄기와, 천천히 흔들리며 떨어지는 낙엽/꽃가루.
// 전부 시간의 함수라서 상태를 따로 갖지 않고(결정적), 프레임마다 메모리를 새로 잡지 않는다.
// 화면 좌표로 그리므로 카메라가 움직여도 하늘 쪽 빛이 따라다니지 않고 천천히 일렁이기만 한다.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GameViewer.App.Render
{
    public sealed class Atmosphere : IDisposable
    {
        private const int Beams = 5;
        private const int Leaves = 26;
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

        public void Dispose() { foreach (Bitmap b in _beam) b.Dispose(); }

        public void Draw(Graphics g, ViewState v, long now)
        {
            DrawBeams(g, v, now);
            DrawLeaves(g, v, now);
        }

        private void DrawBeams(Graphics g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
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

        private void DrawLeaves(Graphics g, ViewState v, long now)
        {
            float W = v.Width, H = v.Height;
            GraphicsState st = g.Save();
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
