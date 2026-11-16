//***************************************************************************
// Trees.cs : 나무. 크기/모양/명암 종류별로 한 번 그려 SpriteCache 에 두고 찍는다 (월드 바깥 숲에 수백 그루가 보여도 부담이 없다).
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GameViewer.App.Render
{
    public sealed class TreeRenderer : IDisposable
    {
        private readonly Resources _res;
        private readonly SpriteCache _cache = new SpriteCache(600);
        private readonly SpriteCache.PaintFn _paint;
        private float _pr, _ppu; private uint _pseed; private bool _pdark;
        private AssetSet _pAsset;

        public TreeRenderer(Resources res) { _res = res; _paint = PaintTree; }
        public void Dispose() { _cache.Dispose(); }

        /// <summary>나무 한 그루. (wx,wy)는 줄기 밑동의 월드 좌표, r 은 수관 반경(unit).</summary>
        public void Draw(Graphics g, ViewState v, float wx, float wy, float r, uint seed, bool dark)
        {
            r *= 0.86f + ((seed >> 5) & 15) / 15f * 0.34f;          // 같은 데이터 반지름이라도 보이는 크기는 조금씩 다르게
            float sx = v.ToScreenX(wx), sy = v.ToScreenY(wy);
            float pr = v.Px(r);
            if (sx < -pr * 2 || sy < -pr * 3.2f || sx > v.Width + pr * 2 || sy > v.Height + pr * 2) return;

            AssetSet asset = AssetSprites.FindTree();
            if (asset != null && seed % 3 != 0) { DrawAsset(g, v, asset, sx, sy, pr, r, seed, dark); return; }       // 3그루 중 1그루는 활엽수(아래 그림)

            // 반경은 0.3 단위, 확대는 2px 단위로 묶어 비트맵 종류를 제한한다
            float rq = (float)(Math.Max(1, Math.Round(r / 0.3)) * 0.3);
            float ppu = CreatureRenderer.QuantizePpu(v.Scale);
            uint variant = seed % 5;
            _pr = rq; _ppu = ppu; _pseed = variant; _pdark = dark; _pAsset = null;
            int w = (int)Math.Ceiling(rq * 2.7f * ppu), h = (int)Math.Ceiling(rq * 3.4f * ppu);
            float ax = w / 2f, ay = h - rq * 0.35f * ppu;
            long key = (2L << 40) | ((long)(rq * 10) << 24) | (variant << 16) | ((dark ? 1L : 0L) << 12) | (long)(ppu * 2);
            SpriteCache.Entry e = _cache.Get(key, w, h, ax, ay, _paint);
            float k = (float)(v.Scale / ppu);
            DrawShadow(g, sx, sy, pr, dark);
            g.DrawImage(e.Bmp, sx - ax * k, sy - ay * k, w * k, h * k);
        }

        /// <summary>이미지 나무. 반경에 비례해 키우고, 모양은 seed 로 고르며, 월드 바깥(dark)은 색을 어둡게 한 파생 그림을 쓴다.</summary>
        private void DrawAsset(Graphics g, ViewState v, AssetSet a, float sx, float sy, float pr, float r, uint seed, bool dark)
        {
            float rq = (float)(Math.Max(1, Math.Round(r / 0.3)) * 0.3);
            float ppu = CreatureRenderer.QuantizePpu(v.Scale);
            int variant = (int)(seed % (uint)a.Count);
            _pr = rq; _ppu = ppu; _pseed = (uint)variant; _pdark = false; _pAsset = a;
            int w = (int)Math.Ceiling((a.Look.HalfW * 2 * rq + 0.3f * rq) * ppu), h = (int)Math.Ceiling((a.Look.H + 0.2f) * rq * ppu);
            float ax = w / 2f, ay = h - 0.1f * rq * ppu;
            long key = (3L << 40) | ((long)(rq * 10) << 24) | ((long)variant << 16) | (long)(ppu * 2);
            SpriteCache.Entry e = _cache.Get(key, w, h, ax, ay, _paint);
            if (dark) e = _cache.GetDerived(key | (3L << 44), e, 3);
            else
            {
                uint tone = (seed >> 9) % 8;
                if (tone < 2) e = _cache.GetDerived(key | (4L << 44), e, 4);          // 단풍
                else if (tone == 2) e = _cache.GetDerived(key | (5L << 44), e, 5);    // 새순
            }
            float k = (float)(v.Scale / ppu);
            DrawShadow(g, sx, sy, pr, dark);
            g.DrawImage(e.Bmp, sx - ax * k, sy - ay * k, w * k, h * k);
        }

        /// <summary>나무 밑 그림자: 바깥은 옅게, 안쪽은 진하게 두 겹으로 가장자리를 부드럽게 한다.</summary>
        private void DrawShadow(Graphics g, float sx, float sy, float pr, bool dark)
        {
            // 키가 큰 나무라 오른쪽 아래로 길게 드리운다(반경의 약 1.3배). 색과 짙기는 존 분위기를 따른다.
            ShadowStyle.Object(g, _res, sx, sy, pr, pr * 1.3f, dark ? 30f : 44f);
        }

        private void PaintTree(Graphics g)
        {
            float r = _pr;
            g.ScaleTransform(_ppu, _ppu);
            if (_pAsset != null)
            {
                AssetSet a = _pAsset;
                float s = a.Scale * r;
                Bitmap src = a.Frames[(int)_pseed];
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new RectangleF(-a.Cx * s, -a.Foot * s, src.Width * s, src.Height * s), new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
                return;
            }
            int tone = (int)(_pseed % 3) * 8;
            Color deep = _pdark ? Color.FromArgb(18, 56 + tone, 30) : Color.FromArgb(26, 78 + tone, 36);
            Color mid = _pdark ? Color.FromArgb(34, 88 + tone, 46) : Color.FromArgb(48, 120 + tone, 54);
            Color light = _pdark ? Color.FromArgb(58, 118 + tone, 62) : Color.FromArgb(104, 170 + tone, 78);
            if (!_pdark && _pseed == 4) { deep = Color.FromArgb(128, 58, 26); mid = Color.FromArgb(206, 112, 40); light = Color.FromArgb(246, 184, 72); }       // 단풍든 활엽수
            else if (!_pdark && _pseed == 3) { deep = Color.FromArgb(40, 104, 44); mid = Color.FromArgb(104, 164, 52); light = Color.FromArgb(176, 214, 84); }   // 새순 돋은 활엽수
            Color ol = Color.FromArgb(120, 10, 30, 16);
            // 줄기
            P.Poly(g, new PointF[] { new PointF(-0.15f * r, -1.0f * r), new PointF(0.15f * r, -1.0f * r), new PointF(0.24f * r, 0f), new PointF(-0.24f * r, 0f) }, Color.FromArgb(110, 80, 52), Color.FromArgb(58, 38, 24), Color.FromArgb(140, 30, 20, 14), 0.03f * r);
            // 수관: 뒤쪽 어두운 덩이 -> 앞쪽 밝은 덩이 순서
            float[,] blobs = { { -0.55f, -1.15f, 0.62f }, { 0.55f, -1.15f, 0.62f }, { 0f, -1.75f, 0.68f }, { -0.4f, -1.6f, 0.52f }, { 0.4f, -1.55f, 0.52f }, { 0f, -1.2f, 0.7f } };
            for (int i = 0; i < blobs.GetLength(0); ++i)
            {
                float t = i / (float)(blobs.GetLength(0) - 1);
                float jx = ((_pseed + i * 3) % 5 - 2) * 0.03f;
                P.Ball(g, (blobs[i, 0] + jx) * r, blobs[i, 1] * r, blobs[i, 2] * r, blobs[i, 2] * r * 0.92f, Col.Mix(mid, light, t * 0.7f), Col.Mix(deep, mid, t * 0.5f), ol, 0.03f * r);
            }
            if (!_pdark)
            {
                for (int i = 0; i < 9; ++i)
                {
                    float a = i * 0.7f + _pseed, d = 0.15f + (i % 4) * 0.2f;
                    P.Fill(g, (float)Math.Cos(a) * d * r - 0.1f * r, -1.5f * r + (float)Math.Sin(a) * d * r * 0.8f, 0.08f * r, 0.06f * r, Color.FromArgb(110, 190, 235, 120));
                }
                P.Glow(g, -0.3f * r, -1.85f * r, 0.5f * r, Color.FromArgb(255, 255, 200), 70);
            }
        }
    }
}
