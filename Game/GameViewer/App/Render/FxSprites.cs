//***************************************************************************
// FxSprites.cs : Assets/fx/ 의 3D 렌더 이펙트 그림(화염구 핵 fireball_0..7, 화살 arrow). 없으면 null (코드로 그린 모양으로 대신한다).
//***************************************************************************
using System;
using System.Drawing;
using System.IO;

namespace GameViewer.App.Render
{
    public static class FxSprites
    {
        public static string Dir = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"), "fx");
        public const int FireFrames = 8;
        private static Bitmap[] _fire, _shard, _iceSpike, _crystal, _rock; private static Bitmap _boneSpike; private static Bitmap[] _bubble; private static Bitmap _arrow, _crescent, _spear, _dagger, _orbHeal, _orbNova, _spike; private static bool _loaded;
        private static readonly System.Drawing.Imaging.ImageAttributes[] _alpha = new System.Drawing.Imaging.ImageAttributes[17];

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true; _fire = new Bitmap[FireFrames];
            try
            {
                for (int i = 0; i < FireFrames; ++i) { string p = Path.Combine(Dir, "fireball_" + i + ".png"); if (File.Exists(p)) using (Bitmap raw = new Bitmap(p)) _fire[i] = new Bitmap(raw); }
                _arrow = One("arrow"); _crescent = One("crescent"); _spear = One("spearhead"); _dagger = One("dagger");
                _bubble = new Bitmap[3]; for (int i = 0; i < 3; ++i) _bubble[i] = One("bubble_" + i); _orbHeal = One("orb_heal"); _orbNova = One("orb_nova"); _spike = One("spike");
                _boneSpike = One("bonespike"); _iceSpike = new Bitmap[2]; _crystal = new Bitmap[2]; _rock = new Bitmap[3];
                for (int i = 0; i < 2; ++i) { _iceSpike[i] = One("icespike_" + i); _crystal[i] = One("crystal_" + i); }
                for (int i = 0; i < 3; ++i) _rock[i] = One("rock_" + i);
                _shard = new Bitmap[4]; for (int i = 0; i < 4; ++i) _shard[i] = One("shard_" + i);
            }
            catch (Exception) { }
        }

        private static Bitmap One(string name) { string p = Path.Combine(Dir, name + ".png"); if (!File.Exists(p)) return null; using (Bitmap raw = new Bitmap(p)) return new Bitmap(raw); }
        public static Bitmap Crescent() { Load(); return _crescent; }
        public static Bitmap Spear() { Load(); return _spear; }
        public static Bitmap Dagger() { Load(); return _dagger; }
        public static Bitmap Bubble(int k) { Load(); return _bubble[((k % 3) + 3) % 3]; }
        public static Bitmap OrbHeal() { Load(); return _orbHeal; }
        public static Bitmap OrbNova() { Load(); return _orbNova; }
        public static Bitmap Spike() { Load(); return _spike; }
        public static Bitmap BoneSpike() { Load(); return _boneSpike; }
        public static Bitmap IceSpike(int k) { Load(); return _iceSpike == null ? null : _iceSpike[((k % 2) + 2) % 2]; }
        public static Bitmap Crystal(int k) { Load(); return _crystal == null ? null : _crystal[((k % 2) + 2) % 2]; }
        public static Bitmap Rock(int k) { Load(); return _rock == null ? null : _rock[((k % 3) + 3) % 3]; }

        /// <summary>위로 솟은 그림(밑이 바닥)을 밑 중심 (bx,by), 높이 h 로 그린다. 그림이 없으면 false.</summary>
        public static bool DrawUp(Graphics g, Bitmap b, float bx, float by, float h, float alpha)
        {
            if (b == null || h < 1f) return false;
            DrawA(g, b, bx, by - h * 0.5f, h * b.Width / b.Height, 0f, alpha);
            return true;
        }

        public static Bitmap Shard(int k) { Load(); return _shard[((k % 4) + 4) % 4]; }

        /// <summary>투명도(0..1)를 17단계로 나눠 캐시한 ImageAttributes.</summary>
        private static System.Drawing.Imaging.ImageAttributes Alpha(float a)
        {
            int k = (int)Math.Round(Math.Max(0f, Math.Min(1f, a)) * 16);
            if (_alpha[k] == null)
            {
                System.Drawing.Imaging.ColorMatrix m = new System.Drawing.Imaging.ColorMatrix(); m.Matrix33 = k / 16f;
                System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes(); ia.SetColorMatrix(m); _alpha[k] = ia;
            }
            return _alpha[k];
        }

        /// <summary>그림 중심을 (cx,cy)에 두고 길이(그림 가로)를 w 로 그린다. alpha 0..1, angleDeg 회전, 그림의 +x 가 진행 방향.</summary>
        public static void DrawA(Graphics g, Bitmap b, float cx, float cy, float w, float angleDeg, float alpha)
        {
            if (b == null || alpha <= 0.02f || w < 1f) return;
            float h = w * b.Height / b.Width;
            System.Drawing.Drawing2D.GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            if (angleDeg != 0f) g.RotateTransform(angleDeg);
            g.DrawImage(b, new Rectangle((int)(-w / 2), (int)(-h / 2), (int)w, (int)h), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, Alpha(alpha));
            g.Restore(st);
        }

        private static readonly System.Collections.Generic.Dictionary<long, System.Drawing.Imaging.ImageAttributes> _tint = new System.Collections.Generic.Dictionary<long, System.Drawing.Imaging.ImageAttributes>();

        /// <summary>투명도 + 스킬 팔레트색(0xRRGGBB)으로 55% 물들여 그린다. (알파 17단계 x 색) 조합을 캐시.</summary>
        public static void DrawT(Graphics g, Bitmap b, float cx, float cy, float w, float angleDeg, float alpha, int rgb)
        {
            if (b == null || alpha <= 0.02f || w < 1f) return;
            int k = (int)Math.Round(Math.Max(0f, Math.Min(1f, alpha)) * 16);
            long key = ((long)k << 24) | (uint)(rgb & 0xFFFFFF);
            System.Drawing.Imaging.ImageAttributes ia;
            if (!_tint.TryGetValue(key, out ia))
            {
                const float t = 0.55f;
                System.Drawing.Imaging.ColorMatrix m = new System.Drawing.Imaging.ColorMatrix();
                m.Matrix00 = m.Matrix11 = m.Matrix22 = 1f - t; m.Matrix33 = k / 16f;
                m.Matrix40 = ((rgb >> 16) & 255) / 255f * t; m.Matrix41 = ((rgb >> 8) & 255) / 255f * t; m.Matrix42 = (rgb & 255) / 255f * t;
                ia = new System.Drawing.Imaging.ImageAttributes(); ia.SetColorMatrix(m); _tint[key] = ia;
            }
            float h = w * b.Height / b.Width;
            System.Drawing.Drawing2D.GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            if (angleDeg != 0f) g.RotateTransform(angleDeg);
            g.DrawImage(b, new Rectangle((int)(-w / 2), (int)(-h / 2), (int)w, (int)h), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
            g.Restore(st);
        }

        public static Bitmap Fire(long now)
        {
            Load();
            Bitmap b = _fire[(int)((now / 55) % FireFrames)];
            return b;
        }

        /// <summary>오른쪽(+x)을 향한 화살 그림. 길이/굵기 비율은 그림 그대로.</summary>
        public static Bitmap Arrow() { Load(); return _arrow; }

        /// <summary>그림 중심을 (cx,cy)에 두고 가로 w 로 그린다(비율 유지). angleDeg 만큼 회전.</summary>
        public static void Draw(Graphics g, Bitmap b, float cx, float cy, float w, float angleDeg)
        {
            if (b == null) return;
            float h = w * b.Height / b.Width;
            System.Drawing.Drawing2D.GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            if (angleDeg != 0f) g.RotateTransform(angleDeg);
            g.DrawImage(b, -w / 2, -h / 2, w, h);
            g.Restore(st);
        }
    }
}
