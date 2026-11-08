//***************************************************************************
// Backdrop.cs : 영웅 선택 화면 뒷배경 이미지. Assets/backdrop.jpg (.jpeg/.png) 가 있으면 화면을 덮게(비율 유지, 넘치는 부분은 잘라서) 그린다.
// 없으면 아무것도 그리지 않고, 호출하는 쪽이 예전처럼 어둡게만 칠한다.
// 화면 크기가 바뀔 때만 다시 늘려 두고, 평소에는 그 비트맵을 그대로 찍는다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace GameViewer.App.Render
{
    public static class Backdrop
    {
        private static readonly string[] Names = { "backdrop.jpg", "backdrop.jpeg", "backdrop.png" };
        private static Bitmap _src;
        private static Bitmap _fit;
        public static bool Loaded { get { return _src != null; } }
        public static int SourceWidth { get { return _src == null ? 0 : _src.Width; } }

        public static void Clear()
        {
            if (_src != null) _src.Dispose();
            if (_fit != null) _fit.Dispose();
            _src = null; _fit = null;
        }

        /// <summary>dir 에서 배경 이미지를 읽는다. 없으면 false(정상), 못 읽으면 warnings 에 적고 false.</summary>
        public static bool Load(string dir, List<string> warnings)
        {
            Clear();
            foreach (string n in Names)
            {
                string path = Path.Combine(dir, n);
                if (!File.Exists(path)) continue;
                try
                {
                    using (Image img = Image.FromFile(path))
                    {
                        if (img.Width > 8192 || img.Height > 8192) { warnings.Add(path + ": 이미지가 너무 큽니다(8192 초과)"); return false; }
                        Bitmap b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(b)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                        _src = b;
                    }
                    return true;
                }
                catch (Exception e) { warnings.Add(path + ": " + e.Message); return false; }
            }
            return false;
        }

        /// <summary>화면 전체를 배경으로 덮는다. 그린 경우 true.</summary>
        public static bool Draw(Graphics g, int width, int height)
        {
            if (_src == null || width <= 0 || height <= 0) return false;
            if (_fit == null || _fit.Width != width || _fit.Height != height)
            {
                if (_fit != null) _fit.Dispose();
                _fit = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                double k = Math.Max(width / (double)_src.Width, height / (double)_src.Height);       // 덮기
                float sw = (float)(width / k), sh = (float)(height / k);
                RectangleF srcRect = new RectangleF((_src.Width - sw) / 2f, (_src.Height - sh) / 2f, sw, sh);
                using (Graphics fg = Graphics.FromImage(_fit))
                {
                    fg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    fg.PixelOffsetMode = PixelOffsetMode.Half;
                    fg.DrawImage(_src, new RectangleF(0, 0, width, height), srcRect, GraphicsUnit.Pixel);
                }
            }
            CompositingMode cm = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_fit, 0, 0);
            g.CompositingMode = cm;
            return true;
        }
    }
}
