//***************************************************************************
// SkiaFrame.cs : Skia 로 한 프레임을 그릴 CPU 래스터 표면과, 끝난 그림을 System.Drawing.Bitmap 으로 복사하는 도우미.
// GameCanvas 가 쓰던 화면 밖 비트맵(_frame)에 그대로 복사하므로 화면에 내보내는 쪽은 바뀌지 않는다.
// (GPU 로 바로 보여 주려면 이 자리에 GL 표면을 두고 SKGLControl 로 내보내는 후속 작업이 필요하다.)
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace GameViewer.App.Render
{
    public sealed class SkiaFrame : IDisposable
    {
        private SKSurface _surface;
        private int _w, _h;
        private readonly SkiaGfx _gfx = new SkiaGfx();

        /// <summary>크기가 바뀌었으면 표면을 새로 만들고, 그리기 객체를 돌려 준다.</summary>
        public SkiaGfx Begin(int width, int height)
        {
            if (_surface == null || _w != width || _h != height)
            {
                if (_surface != null) _surface.Dispose();
                _surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
                _w = width; _h = height;
            }
            _gfx.Begin(_surface.Canvas);
            return _gfx;
        }

        /// <summary>그린 결과를 target(Format32bppPArgb, 같은 크기)에 복사한다.</summary>
        public void CopyTo(Bitmap target)
        {
            if (_surface == null || target.Width != _w || target.Height != _h) return;
            using (SKPixmap pm = _surface.PeekPixels())
            {
                BitmapData bd = target.LockBits(new Rectangle(0, 0, _w, _h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    byte[] row = new byte[_w * 4];
                    IntPtr src = pm.GetPixels();
                    for (int y = 0; y < _h; ++y)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(src, y * pm.RowBytes), row, 0, row.Length);
                        System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(bd.Scan0, y * bd.Stride), row.Length);
                    }
                }
                finally { target.UnlockBits(bd); }
            }
        }

        public void Dispose() { if (_surface != null) { _surface.Dispose(); _surface = null; } }
    }
}
