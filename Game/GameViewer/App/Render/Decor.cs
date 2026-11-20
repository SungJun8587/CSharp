//***************************************************************************
// Decor.cs : 맵 장식. 3D 로 렌더해 둔 작은 그림(Assets/props/<이름>_<번호>.png)을 지역별 배치 규칙(DecorRules)대로 놓는다.
// 그림이 없으면 아무것도 그리지 않는다. 배치는 지역마다 한 번만 계산해 두고, 프레임마다는 보이는 것만 그린다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    public sealed class Decor : IDisposable
    {
        public static string Dir = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"), "props");
        private readonly Resources _res;
        private readonly Dictionary<string, Bitmap> _bmp = new Dictionary<string, Bitmap>();
        private List<DecorItem> _items = new List<DecorItem>();
        private uint _zone = uint.MaxValue; private uint _terrain = uint.MaxValue; private float _density = -1f;
        private readonly PointF[] _dst = new PointF[3];

        public Decor(Resources res) { _res = res; }

        public int Count { get { return _items.Count; } }

        /// <summary>그림이 있는 지역만 목록을 만든다(바뀌었을 때만 다시 계산).</summary>
        public void Prepare(GameData d, uint zone, MapKind kind, GfxQuality q)
        {
            float density = q == GfxQuality.High ? 0.30f : (q == GfxQuality.Medium ? 0.16f : 0f);
            if (zone == _zone && d.Terrain == _terrain && density == _density) return;
            _zone = zone; _terrain = d.Terrain; _density = density;
            _items = density > 0f && Directory.Exists(Dir) ? DecorRules.Generate(kind, zone, d.WorldMinX, d.WorldMinY, d.WorldMaxX, d.WorldMaxY, d.Blocks, density) : new List<DecorItem>();
        }

        /// <summary>화면 범위 안(월드 좌표)의 장식 색인 구간을 돌려준다. 목록이 Y 순서라 이진 탐색으로 자른다.</summary>
        public void Range(float y0, float y1, out int first, out int last)
        {
            int lo = 0, hi = _items.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (_items[m].Y < y0) lo = m + 1; else hi = m; }
            first = lo;
            hi = _items.Count; int l2 = lo;
            while (l2 < hi) { int m = (l2 + hi) / 2; if (_items[m].Y <= y1) l2 = m + 1; else hi = m; }
            last = l2;
        }

        public DecorItem Item(int i) { return _items[i]; }

        private Bitmap Get(string name, int v)
        {
            string key = name + "_" + v;
            Bitmap b;
            if (_bmp.TryGetValue(key, out b)) return b;
            try
            {
                string path = Path.Combine(Dir, key + ".png");
                if (File.Exists(path)) using (Bitmap raw = new Bitmap(path)) b = new Bitmap(raw);
            }
            catch (Exception) { b = null; }
            _bmp[key] = b;
            return b;
        }

        public void Draw(Graphics g, ViewState v, DecorItem it)
        {
            Bitmap b = Get(it.Name, it.Var);
            if (b == null) return;
            float sx = v.ToScreenX(it.X), sy = v.ToScreenY(it.Y);
            float w = v.Px(it.Width), h = w * b.Height / b.Width;
            ShadowStyle.Object(g, _res, sx, sy, w * 0.44f, w * 0.18f, 40f);
            float x0 = sx - w / 2, y0 = sy - h + w * 0.04f;
            if (it.Flip) { _dst[0] = new PointF(x0 + w, y0); _dst[1] = new PointF(x0, y0); _dst[2] = new PointF(x0 + w, y0 + h); }
            else { _dst[0] = new PointF(x0, y0); _dst[1] = new PointF(x0 + w, y0); _dst[2] = new PointF(x0, y0 + h); }
            g.DrawImage(b, _dst);
        }

        public void Dispose() { foreach (Bitmap b in _bmp.Values) if (b != null) b.Dispose(); _bmp.Clear(); }
    }
}
