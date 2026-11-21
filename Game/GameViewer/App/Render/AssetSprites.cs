//***************************************************************************
// AssetSprites.cs : 이미지 파일로 된 캐릭터/몬스터 그림(회전 렌더 시트)을 읽는다.
// Assets/sprites.json 에 적힌 종류만 이미지로 그리고, 적히지 않은 종류는 코드로 그린 그림(Creatures/Monsters)을 그대로 쓴다.
//   { "sprites": [ { "kind": "monster", "template": 9, "pattern": "demon/{0:000}.png", "angles": 18, "height": 4.6 } ] }
//   kind     monster | hero          template  Monster/Hero 시트의 Id
//   pattern  이미지 경로(sprites.json 기준). {0} 에 각도(도, 정수)가 들어간다. 각도 = 번호 * 360 / angles
//   angles   방향 수(1..72). 0도는 화면 아래(카메라 쪽)를 보는 모습이고, 각도가 커지면 화면 오른쪽 -> 위 -> 왼쪽 순으로 돈다.
//   walkFrames / walkPattern (선택)  걷기 애니메이션. walkPattern 의 {0}=각도, {1}=프레임 번호(0부터), walkFrames 장(2..32). 걸을 때만 쓰고 설 때는 pattern 그림.
//   height   그림의 실제 내용(투명하지 않은 부분)이 차지할 높이(단위). 가로는 같은 비율로 정해진다.
//   kind "tree" 는 template 이 없고, Block 의 원(나무) 하나를 이 이미지로 그린다. height 는 반경 1 일 때의 높이이고 실제 높이는 반경에 비례한다.
//            방향 번호는 나무마다 다른 모양을 고르는 데 쓴다(회전 렌더 한 벌을 그대로 쓰면 된다).
//   attackFrames / attackPattern / weapon (선택)  무기를 몸과 따로 그린 영웅: 무기 없는 몸의 공격 자세와 자세별 무기 앞/뒤 레이어 + 앵커(WeaponLayers 참고). 자세한 형식은 README.
// 모든 방향의 투명하지 않은 영역을 합친 상자를 기준으로 발 밑(아래 가운데)과 크기를 잡으므로, 돌 때 흔들리거나 크기가 변하지 않는다.
// 걷기 프레임이 없으면 걷기/대기 움직임은 이미지를 위아래로 튀기고 눌러서 흉내 낸다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;

namespace GameViewer.App.Render
{
    /// <summary>방향별 이미지 한 벌.</summary>
    public sealed class AssetSet
    {
        public Bitmap[] Frames;
        public Bitmap[] Attack;     // 공격/시전 중에 쓰는 그림 [방향] (무기를 흐리게 한 것). 없으면 null
        public Bitmap[][] AttackBody;   // 무기를 뺀 몸의 공격 자세 [방향][자세]. Weapon 과 함께 쓴다. 없으면 null
        public WeaponLayers Weapon;     // 몸과 따로 그린 무기 그림. 없으면 null (무기가 몸에 그려져 있거나 코드로 그린다)
        public Bitmap[][] Walk;     // 걷기 프레임 [방향][프레임]. 없으면 null (그러면 서 있는 그림을 튀겨서 걷는 것처럼 보인다)
        public int WalkCount { get { return Walk == null || Walk.Length == 0 ? 0 : Walk[0].Length; } }
        public Bitmap[][] Idle;     // 대기 숨쉬기/눈 깜빡임 프레임 [방향][프레임] (무기 뺀 몸). 없으면 null (그러면 서 있는 그림을 눌러서 숨 쉬는 것처럼 보인다)
        public int IdleCount { get { return Idle == null || Idle.Length == 0 ? 0 : Idle[0].Length; } }
        public Bitmap[] Hit;        // 피격 그림 [방향] (무기까지 구운 한 장). 없으면 null
        public Bitmap[][] Death;    // 사망 그림 [방향][단계] (무기까지 구움: 쓰러지는 중 -> ... -> 누워 있음). 없으면 null
        public int DeathCount { get { return Death == null || Death.Length == 0 ? 0 : Death[0].Length; } }
        // 대기/피격/사망 그림은 메모리를 아끼려고 처음 쓸 때 읽는다(방향마다 한 번). 경로는 불러올 때 확인해 둔다.
        internal string[][] IdlePaths, DeathPaths, WalkPaths, AttackBodyPaths;
        internal string[] HitPaths;
        public Bitmap WalkAt(int dir, int k) { return Lazy(Walk[dir], k, WalkPaths[dir][k], Frames[dir]); }
        public Bitmap AttackBodyAt(int dir, int k) { return Lazy(AttackBody[dir], k, AttackBodyPaths[dir][k], Frames[dir]); }
        public Bitmap IdleAt(int dir, int k) { return Lazy(Idle[dir], k, IdlePaths[dir][k], Frames[dir]); }
        public Bitmap HitAt(int dir) { Bitmap[] one = Hit; Bitmap b = one[dir]; if (b == null) { b = AssetSprites.ReadLazy(HitPaths[dir], Frames[dir].Width, Frames[dir].Height); if (b == null) return Frames[dir]; one[dir] = b; Remember(one, dir); } return b; }
        public Bitmap DeathAt(int dir, int k) { return Lazy(Death[dir], k, DeathPaths[dir][k], Frames[dir]); }
        // 처음 쓸 때 읽은 그림은 스프라이트 캐시에 그려 넣는 순간에만 필요하므로(Creatures.PaintCurrent), 최근 것 일부만 두고 오래된 것부터 버린다.
        // 다시 필요하면 파일에서 다시 읽는다. 영웅 그림이 400x400 이라 전부 들고 있으면 수백 MB 가 된다.
        private const int LazyKeep = 40;
        private static readonly Queue<KeyValuePair<Bitmap[], int>> LazyOrder = new Queue<KeyValuePair<Bitmap[], int>>();
        private static void Remember(Bitmap[] row, int k)
        {
            LazyOrder.Enqueue(new KeyValuePair<Bitmap[], int>(row, k));
            while (LazyOrder.Count > LazyKeep)
            {
                KeyValuePair<Bitmap[], int> old = LazyOrder.Dequeue();
                Bitmap b = old.Key[old.Value];
                if (b != null) { old.Key[old.Value] = null; b.Dispose(); }
            }
        }
        private static Bitmap Lazy(Bitmap[] row, int k, string path, Bitmap fallback)
        {
            Bitmap b = row[k];
            if (b == null) { b = AssetSprites.ReadLazy(path, fallback.Width, fallback.Height); if (b == null) return fallback; row[k] = b; Remember(row, k); }
            return b;
        }
        public float Scale;         // 원본 픽셀 하나가 차지하는 단위
        public float Cx, Foot;      // 기준점(원본 픽셀): 가로 중심, 발 밑
        public Look Look;
        /// <summary>모든 자세를 담는 크기(Look 보다 크거나 같다). 비트맵 크기를 잡을 때만 쓴다.</summary>
        public Look Extent;
        public string Fx;           // 몬스터 타격 연출 이름(MonsterFx). 없으면 null
        public int Count { get { return Frames.Length; } }

        /// <summary>바라보는 방향(화면 좌표 벡터)에 가장 가까운 이미지 번호.</summary>
        public int IndexFor(float fx, float fy)
        {
            if (Frames.Length == 1 || (fx * fx + fy * fy) < 0.0001f) return 0;
            double deg = Math.Atan2(fx, fy) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            return (int)Math.Round(deg * Frames.Length / 360.0) % Frames.Length;
        }
    }

    /// <summary>
    /// 몸과 따로 그린 무기. 자세(pose 0 = 대기/걷기, 1.. = 공격 자세)마다 방향별로 몸 뒤에 가려지는 부분(Back)과 앞으로 나오는 부분(Front)이 있다.
    /// 무기 그림은 몸 그림 둘레에 Pad 픽셀씩 여백을 둔 같은 카메라 그림이라, 몸 그림 왼쪽 위 (-Pad,-Pad) 에 맞춰 찍으면 겹친다.
    /// 앵커는 몸 그림 픽셀 좌표: Grip 손잡은 곳, Tip 창끝, Butt 창꼬리 [자세][방향]. WalkGrip 은 걷기 프레임별 손 위치 [방향][프레임].
    /// </summary>
    public sealed class WeaponLayers
    {
        public int Pad, Poses;
        public Bitmap[][] Back, Front;      // [자세][방향] (투명한 여백을 잘라낸 그림: 아래 BackAt/FrontAt 가 원래 그림 안에서의 왼쪽 위 위치)
        public Point[][] BackAt, FrontAt;
        public float[][][] Grip, Tip, Butt; // [자세][방향][x,y]
        public float[][][] WalkGrip;        // [방향][프레임][x,y]
        public float[][][] IdleGrip;        // 대기 프레임별 손 위치 [방향][프레임][x,y] (없으면 null)
        public int Width, Height;
    }

    public static class AssetSprites
    {
        private static Dictionary<long, AssetSet> _sets;

        private static long Key(bool monster, uint tpl) { return ((monster ? 1L : 0L) << 32) | tpl; }

        /// <summary>나무 이미지(kind "tree")가 있으면 그 세트, 없으면 null.</summary>
        public static AssetSet FindTree()
        {
            Dictionary<long, AssetSet> m = _sets;
            if (m == null) return null;
            AssetSet s;
            return m.TryGetValue(TreeKey, out s) ? s : null;
        }

        private const long TreeKey = 2L << 32;

        /// <summary>이미지가 지정된 종류면 그 세트, 아니면 null (코드로 그린 그림을 쓴다).</summary>
        public static AssetSet Find(bool monster, uint tpl)
        {
            Dictionary<long, AssetSet> m = _sets;
            if (m == null) return null;
            AssetSet s;
            return m.TryGetValue(Key(monster, tpl), out s) ? s : null;
        }

        // ---- 시트(아틀라스): 최상위 "atlases" 의 색인에 있는 경로는 개별 파일 대신 시트에서 잘라 읽는다(Tools/pack_atlas.py 가 만든다).
        // 그림은 처음 그릴 때 읽는 경우가 많아서(걷기/공격 자세) 시트 쪽은 읽어 둔 채로 두고, 다시 불러오거나 Clear 할 때 놓는다.
        /// <summary>영웅/몬스터 그림 크기 배율(sprites.json 의 height 에 곱한다). Load 전에 정한다. 나무는 영향이 없다.</summary>
        public static float SizeFactor = 1f;

        private static Dictionary<string, AtlasRef> _atlas;     // 전체 경로(대소문자 무시) -> 시트 안의 위치
        private static List<AtlasPage> _atlasPages;

        private sealed class AtlasPage
        {
            public string Path; public Bitmap Bmp;
            private readonly object _lock = new object();
            public Bitmap Get(out string why)
            {
                why = null;
                lock (_lock) { if (Bmp == null) Bmp = ReadArgbFile(Path, out why); return Bmp; }
            }
            public void Release() { lock (_lock) { if (Bmp != null) { Bmp.Dispose(); Bmp = null; } } }
        }

        /// <summary>시트 안의 한 장: 잘라 낸 상자(X,Y,W,H)와 원래 크기(Fw,Fh) 안에서의 위치(Ox,Oy).</summary>
        private sealed class AtlasRef { public AtlasPage Page; public int X, Y, W, H, Ox, Oy, Fw, Fh; }

        private static bool Exists(string path) { return (_atlas != null && _atlas.ContainsKey(path)) || File.Exists(path); }

        public static int Count { get { return _sets == null ? 0 : _sets.Count; } }

        public static void Clear()
        {
            Dictionary<long, AssetSet> m = _sets;
            _sets = null;
            if (_atlasPages != null) foreach (AtlasPage pg in _atlasPages) pg.Release();
            _atlas = null; _atlasPages = null;
            MonsterTraits.Clear();
            GameViewer.Core.Model3D.MonsterModels.Clear();
            if (m == null) return;
            foreach (AssetSet s in m.Values) Dispose(s);
        }

        /// <summary>
        /// dir/sprites.json 을 읽어 이미지를 올린다. 폴더나 파일이 없으면 0을 돌려주고(정상), 항목이 잘못되면 그 항목만 건너뛰고 warnings 에 적는다.
        /// 이전에 읽은 것은 버린다.
        /// </summary>
        public static int Load(string dir, List<string> warnings) { return Load(dir, warnings, null); }

        /// <summary>진행 알림: (0..1 진행률, 지금 읽는 종류 이름). 읽는 스레드에서 불리므로 받는 쪽이 스레드 안전해야 한다.</summary>
        public delegate void ProgressFn(float ratio, string label);
        private static ProgressFn _prog;
        private static double _done, _total;
        private static string _label = "";

        private static void Report(bool force)
        {
            ProgressFn p = _prog;
            if (p == null) return;
            if (!force && ((int)_done & 3) != 0) return;            // 이미지 4장마다
            p(_total <= 0 ? 1f : (float)Math.Min(1.0, _done / _total), _label);
        }

        /// <summary>항목 하나가 읽을 이미지 수(진행률 가중치). 방향 수 x (기본 + 걷기 + 공격 자세 + 무기 앞/뒤 자세).</summary>
        private static double Weight(Dictionary<string, object> o)
        {
            if (o == null) return 1;
            double n = Math.Max(1, MiniJson.Num(o, "angles", 1));
            double walk = Math.Max(0, MiniJson.Num(o, "walkFrames", 0));
            double atk = MiniJson.Num(o, "attackFrames", 0) + MiniJson.Num(o, "idleFrames", 0) + MiniJson.Num(o, "deathFrames", 0) + (MiniJson.Str(o, "hitPattern", "").Length > 0 ? 1 : 0);
            if (atk <= 0 && MiniJson.Str(o, "attackPattern", "").Length > 0) atk = 1;
            double weapon = 0;
            if (o.ContainsKey("weapon")) { Dictionary<string, object> w = MiniJson.AsObject(o["weapon"]); if (w != null) weapon = 2 * Math.Max(0, MiniJson.Num(w, "poses", 0)); }
            return n * (1 + walk + atk + weapon);
        }

        public static int Load(string dir, List<string> warnings, ProgressFn progress)
        {
            Clear();
            _prog = progress; _done = 0; _total = 0; _label = "";
            try { return LoadAll(dir, warnings); }
            finally { _label = ""; Report(true); _prog = null; }
        }

        private static int LoadAll(string dir, List<string> warnings)
        {
            string manifest = Path.Combine(dir, "sprites.json");
            if (!File.Exists(manifest)) return 0;
            Dictionary<long, AssetSet> sets = new Dictionary<long, AssetSet>();
            try
            {
                Dictionary<string, object> root = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(manifest, System.Text.Encoding.UTF8)));
                List<object> list = root == null ? null : MiniJson.AsArray(root.ContainsKey("sprites") ? root["sprites"] : null);
                if (list == null) { warnings.Add(manifest + ": \"sprites\" 배열이 없습니다"); return 0; }
                LoadAtlasIndex(dir, root, warnings);
                for (int i = 0; i < list.Count; ++i) _total += Weight(MiniJson.AsObject(list[i]));
                _done = 0; Report(true);
                double acc = 0;
                for (int i = 0; i < list.Count; ++i)
                {
                    string why = null;
                    Dictionary<string, object> eo = MiniJson.AsObject(list[i]);
                    _label = eo == null ? "" : MiniJson.Str(eo, "pattern", "").Split('/')[0];
                    double wEntry = Weight(eo); _done = acc; acc += wEntry;
                    long key; AssetSet set = LoadOne(dir, MiniJson.AsObject(list[i]), out key, out why);
                    if (set == null) { warnings.Add(manifest + " [" + i + "]: " + why); continue; }
                    if (sets.ContainsKey(key)) { Dispose(set); warnings.Add(manifest + " [" + i + "]: 같은 종류가 두 번 지정됨"); continue; }
                    sets[key] = set;
                    _done = acc; Report(true);          // 추정과 실제 장수가 달라도 항목이 끝나면 맞춘다
                }
            }
            catch (Exception e) { warnings.Add(manifest + ": " + e.Message); foreach (AssetSet s in sets.Values) Dispose(s); return 0; }
            _sets = sets;
            return sets.Count;
        }

        private static void Dispose(AssetSet s)
        {
            if (s.Frames != null) foreach (Bitmap b in s.Frames) if (b != null) b.Dispose();
            if (s.AttackBody != null) foreach (Bitmap[] row in s.AttackBody) if (row != null) foreach (Bitmap b in row) if (b != null) b.Dispose();
            if (s.Weapon != null)
            {
                foreach (Bitmap[][] layer in new Bitmap[][][] { s.Weapon.Back, s.Weapon.Front })
                    if (layer != null) foreach (Bitmap[] row in layer) if (row != null) foreach (Bitmap b in row) if (b != null) b.Dispose();
            }
            if (s.Attack != null) foreach (Bitmap b in s.Attack) if (b != null) b.Dispose();
            if (s.Walk != null) foreach (Bitmap[] row in s.Walk) if (row != null) foreach (Bitmap b in row) if (b != null) b.Dispose();
            if (s.Idle != null) foreach (Bitmap[] row in s.Idle) if (row != null) foreach (Bitmap b in row) if (b != null) b.Dispose();
            if (s.Death != null) foreach (Bitmap[] row in s.Death) if (row != null) foreach (Bitmap b in row) if (b != null) b.Dispose();
            if (s.Hit != null) foreach (Bitmap b in s.Hit) if (b != null) b.Dispose();
        }

        private static AssetSet LoadModelOnly(string dir, Dictionary<string, object> o, bool monster, uint tpl, out long key, out string why)
        {
            key = 0; why = null;
            double height = MiniJson.Num(o, "height", 0);
            if (!(height >= 0.5 && height <= 12.0)) { why = "height 는 0.5..12 (단위)"; return null; }
            height *= SizeFactor;
            AttackStyle style = AttackStyle.Lunge;
            string styleName = MiniJson.Str(o, "attack", "");
            if (styleName.Length > 0 && !MonsterTraits.TryParseStyle(styleName, out style)) { why = "attack 은 lunge, hop, slam 중 하나"; return null; }
            string fxName = MiniJson.Str(o, "fx", "");
            AssetSet s = new AssetSet();
            s.Scale = 1f; s.Fx = fxName.Length > 0 ? fxName : null;
            float half = (float)MiniJson.Num(o, "halfW", 0.42 * height);
            s.Look = new Look(half, (float)height); s.Extent = new Look(half, (float)height);
            if (monster) MonsterTraits.Set(tpl, style, MiniJson.Bool(o, "flying", false), MiniJson.Bool(o, "idleAnim", false));
            GameViewer.Core.Model3D.ModelSpec ms = new GameViewer.Core.Model3D.ModelSpec();
            ms.Path = System.IO.Path.Combine(dir, MiniJson.Str(o, "model3d", "").Replace('/', System.IO.Path.DirectorySeparatorChar));
            ms.WalkStride = (float)MiniJson.Num(o, "walkStride", 1.6);
            GameViewer.Core.Model3D.MonsterModels.Set(monster, tpl, ms);
            key = Key(monster, tpl);
            return s;
        }

        private static AssetSet LoadOne(string dir, Dictionary<string, object> o, out long key, out string why)
        {
            key = 0; why = null;
            if (o == null) { why = "항목이 객체가 아닙니다"; return null; }
            string kind = MiniJson.Str(o, "kind", "");
            bool tree = kind == "tree";
            if (kind != "monster" && kind != "hero" && !tree) { why = "kind 는 monster, hero, tree 중 하나"; return null; }
            double tpl = tree ? 0 : MiniJson.Num(o, "template", -1);
            if (tpl < 0 || tpl > uint.MaxValue || tpl != Math.Floor(tpl)) { why = "template 이 없거나 잘못됨"; return null; }
            // 3D 전용 몬스터/영웅: 이미지 시트 없이 "model3d" 와 크기/성질만 적는다(pattern 이 없을 때). 이 경우 AssetSet 은 크기(Look)와 타격 연출 이름만 쥔다.
            if (!tree && (kind == "monster" || kind == "hero") && MiniJson.Str(o, "pattern", "").Length == 0 && MiniJson.Str(o, "model3d", "").Length > 0)
                return LoadModelOnly(dir, o, kind == "monster", (uint)tpl, out key, out why);
            int n = (int)MiniJson.Num(o, "angles", 0);
            if (n < 1 || n > 72) { why = "angles 는 1..72"; return null; }
            double height = MiniJson.Num(o, "height", 0);
            if (!(height >= 0.5 && height <= 12.0)) { why = "height 는 0.5..12 (단위)"; return null; }
            if (!tree) height *= SizeFactor;
            string pattern = MiniJson.Str(o, "pattern", "");
            if (pattern.Length == 0) { why = "pattern 이 없습니다"; return null; }

            // 몬스터 성질(선택): attack = lunge|hop|slam, fx = 타격 연출 이름, flying, idleAnim
            AttackStyle style = AttackStyle.Lunge;
            string styleName = MiniJson.Str(o, "attack", "");
            if (styleName.Length > 0 && !MonsterTraits.TryParseStyle(styleName, out style)) { why = "attack 은 lunge, hop, slam 중 하나"; return null; }
            string fxName = MiniJson.Str(o, "fx", "");

            // 걷기 애니메이션(선택): walkFrames 장씩, walkPattern 의 {0}=각도, {1}=프레임 번호(0부터)
            int walkN = (int)MiniJson.Num(o, "walkFrames", 0);
            string walkPattern = MiniJson.Str(o, "walkPattern", "");
            if (walkN != 0 && (walkN < 2 || walkN > 32)) { why = "walkFrames 는 2..32"; return null; }
            if (walkN > 0 && walkPattern.Length == 0) { why = "walkFrames 가 있으면 walkPattern 도 필요합니다"; return null; }

            // 공격 그림(선택): attackPattern 의 {0}=각도. 몸에 그려진 무기를 흐리게 한 그림으로, 공격/시전 동안 코드로 그린 무기와 겹쳐 보이지 않게 바꿔 쓴다.
            string attackPattern = MiniJson.Str(o, "attackPattern", "");
            Bitmap[] attack = attackPattern.Length > 0 && MiniJson.Num(o, "attackFrames", 0) == 0 ? new Bitmap[n] : null;
            Bitmap[] frames = new Bitmap[n];
            AssetSet extra = new AssetSet();
            Bitmap[][] walk = walkN > 0 ? new Bitmap[n][] : null;
            if (walkN > 0) extra.WalkPaths = new string[n][];
            int wx0 = int.MaxValue, wy0 = int.MaxValue, wx1 = -1, wy1 = -1;
            try
            {
                int w = 0, h = 0;
                for (int i = 0; i < n; ++i)
                {
                    string rel;
                    try { rel = string.Format(CultureInfo.InvariantCulture, pattern, (int)Math.Round(i * 360.0 / n)); }
                    catch (FormatException) { why = "pattern 형식이 잘못됨: " + pattern; return Fail(frames); }
                    string path = Path.GetFullPath(Path.Combine(dir, rel));
                    if (!Exists(path)) { why = "파일이 없습니다: " + rel; return Fail(frames); }
                    frames[i] = ReadArgb(path, out why);
                    if (frames[i] == null) return Fail(frames);
                    if (i == 0) { w = frames[0].Width; h = frames[0].Height; }
                    else if (frames[i].Width != w || frames[i].Height != h) { why = "이미지 크기가 서로 다릅니다: " + rel; return Fail(frames); }
                }
                if (walkN > 0)
                    for (int i = 0; i < n; ++i)
                    {
                        walk[i] = new Bitmap[walkN]; extra.WalkPaths[i] = new string[walkN];
                        for (int k = 0; k < walkN; ++k)
                        {
                            string rel;
                            try { rel = string.Format(CultureInfo.InvariantCulture, walkPattern, (int)Math.Round(i * 360.0 / n), k); }
                            catch (FormatException) { why = "walkPattern 형식이 잘못됨: " + walkPattern; return Fail(frames); }
                            string path = Path.GetFullPath(Path.Combine(dir, rel));
                            if (!Exists(path)) { why = "파일이 없습니다: " + rel; return Fail(frames); }
                            extra.WalkPaths[i][k] = path;
                            if (!o.ContainsKey("bounds"))        // 크기 계산에만 잠깐 읽고 바로 버린다(걷기 그림은 처음 그릴 때 읽는다)
                            {
                                Bitmap tb = ReadArgb(path, out why);
                                if (tb == null) return Fail(frames);
                                if (tb.Width != w || tb.Height != h) { tb.Dispose(); why = "걷기 이미지 크기가 기본 이미지와 다릅니다: " + rel; return Fail(frames); }
                                Bounds(tb, ref wx0, ref wy0, ref wx1, ref wy1); tb.Dispose();
                            }
                        }
                    }
                if (attack != null)
                    for (int i = 0; i < n; ++i)
                    {
                        string rel;
                        try { rel = string.Format(CultureInfo.InvariantCulture, attackPattern, (int)Math.Round(i * 360.0 / n)); }
                        catch (FormatException) { why = "attackPattern 형식이 잘못됨: " + attackPattern; return Fail(frames); }
                        string path = Path.GetFullPath(Path.Combine(dir, rel));
                        if (!Exists(path)) { why = "파일이 없습니다: " + rel; return Fail(frames); }
                        attack[i] = ReadArgb(path, out why);
                        if (attack[i] == null) return Fail(frames);
                        if (attack[i].Width != w || attack[i].Height != h) { why = "공격 이미지 크기가 기본 이미지와 다릅니다: " + rel; return Fail(frames); }
                    }
                if (!LoadWeapon(dir, o, n, w, h, extra, out why)) return null;
                if (!LoadPoses(dir, o, n, w, h, extra, out why)) return null;
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                foreach (Bitmap b in frames) Bounds(b, ref x0, ref y0, ref x1, ref y1);
                if (walk != null && !o.ContainsKey("bounds") && wx1 >= 0) { x0 = Math.Min(x0, wx0); y0 = Math.Min(y0, wy0); x1 = Math.Max(x1, wx1); y1 = Math.Max(y1, wy1); }      // bounds 가 있으면 걷기 그림은 거기에 이미 들어 있어 크기에 영향을 주지 않는다
                if (x1 < 0) { why = "투명하지 않은 픽셀이 없습니다"; return Fail(frames); }
                // 선택: "bounds":[x0,y0,x1,y1] = 공격/피격/사망 등 모든 자세를 합친 상자, "footPx" = 땅에 닿는 가로줄(없으면 서 있는 그림의 아래 끝).
                // 크기(Scale)와 Look(이름표/그림자/효과 위치)은 서 있는 그림 기준이고, 그림을 담는 비트맵 크기(Extent)만 모든 자세 기준이다.
                // 3D 로 렌더한 그림은 바닥 원판이 발 밑 선보다 아래로 나오고, 뛰어오르거나 쓰러진 자세가 서 있는 그림 밖으로 나간다.
                int ux0 = x0, uy0 = y0, ux1 = x1, uy1 = y1;
                List<object> bnd = o.ContainsKey("bounds") ? MiniJson.AsArray(o["bounds"]) : null;
                if (bnd != null && bnd.Count == 4)
                {
                    ux0 = Math.Min(ux0, (int)Convert.ToDouble(bnd[0], CultureInfo.InvariantCulture)); uy0 = Math.Min(uy0, (int)Convert.ToDouble(bnd[1], CultureInfo.InvariantCulture));
                    ux1 = Math.Max(ux1, (int)Convert.ToDouble(bnd[2], CultureInfo.InvariantCulture)); uy1 = Math.Max(uy1, (int)Convert.ToDouble(bnd[3], CultureInfo.InvariantCulture));
                }
                double footRow = MiniJson.Num(o, "footPx", -1);
                if (footRow < 0 || footRow > h) footRow = y1 + 1;
                AssetSet s = new AssetSet();
                s.Frames = frames; s.Walk = walk; s.WalkPaths = extra.WalkPaths; s.Attack = attack; attack = null;
                s.AttackBody = extra.AttackBody; s.AttackBodyPaths = extra.AttackBodyPaths; s.Weapon = extra.Weapon; extra.AttackBody = null; extra.Weapon = null;
                s.Idle = extra.Idle; s.Hit = extra.Hit; s.Death = extra.Death; extra.Idle = null; extra.Hit = null; extra.Death = null;
                s.IdlePaths = extra.IdlePaths; s.HitPaths = extra.HitPaths; s.DeathPaths = extra.DeathPaths;
                int contentH = (int)Math.Round(footRow) - y0;           // 땅 위로 솟은 높이(서 있는 그림 기준)
                if (contentH < 1) contentH = 1;
                s.Scale = (float)height / contentH;

                s.Fx = fxName.Length > 0 ? fxName : null;
                if (kind == "monster") MonsterTraits.Set((uint)tpl, style, MiniJson.Bool(o, "flying", false), MiniJson.Bool(o, "idleAnim", false));
                // 선택: "model3d":"models/Big_Yeti.gltf" (몬스터/영웅) 이면 이 종류는 OpenGL 로 실시간 3D 로 그린다(위 이미지 시트는 GL 을 못 쓸 때 대신 쓴다). "walkStride" = 걷기 클립 한 바퀴에 걷는 거리.
                string model3d = MiniJson.Str(o, "model3d", "");
                if ((kind == "monster" || kind == "hero") && model3d.Length > 0)
                {
                    GameViewer.Core.Model3D.ModelSpec ms = new GameViewer.Core.Model3D.ModelSpec();
                    ms.Path = System.IO.Path.Combine(dir, model3d.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    ms.WalkStride = (float)MiniJson.Num(o, "walkStride", 1.6);
                    GameViewer.Core.Model3D.MonsterModels.Set(kind == "monster", (uint)tpl, ms);
                }
                s.Cx = w / 2f; s.Foot = (float)footRow;
                float half = Math.Max(s.Cx - x0, x1 + 1 - s.Cx) * s.Scale;
                s.Look = new Look(half, (float)height);
                float halfU = Math.Max(s.Cx - ux0, ux1 + 1 - s.Cx) * s.Scale;
                s.Extent = new Look(Math.Max(half, halfU), Math.Max((float)height, ((float)footRow - uy0) * s.Scale));
                key = tree ? TreeKey : Key(kind == "monster", (uint)tpl);
                frames = null; walk = null;
                return s;
            }
            finally
            {
                if (frames != null) for (int i = 0; i < frames.Length; ++i) if (frames[i] != null) frames[i].Dispose();
                if (walk != null) foreach (Bitmap[] row in walk) if (row != null) foreach (Bitmap b in row) if (b != null) b.Dispose();
                if (attack != null) foreach (Bitmap b in attack) if (b != null) b.Dispose();
                Dispose(extra);
            }
        }

        /// <summary>
        /// 무기를 몸과 따로 그린 경우(선택). attackFrames(N) 가 있으면 attackPattern 의 {1}=자세 번호(0..N-1)로 무기 없는 몸의 공격 자세를 읽고(그러면 Attack 은 쓰지 않는다),
        /// "weapon": { "poses", "pad", "backPattern", "frontPattern", "anchors" } 가 있으면 무기 레이어를 읽는다. 패턴의 {0}=자세, {1}=각도.
        /// </summary>
        private static bool LoadWeapon(string dir, Dictionary<string, object> o, int n, int w, int h, AssetSet extra, out string why)
        {
            why = null;
            int atkN = (int)MiniJson.Num(o, "attackFrames", 0);
            string attackPattern = MiniJson.Str(o, "attackPattern", "");
            if (atkN != 0 && (atkN < 1 || atkN > 16 || attackPattern.Length == 0)) { why = "attackFrames 는 1..16 이고 attackPattern 이 필요합니다"; return false; }
            if (atkN > 0)
            {
                extra.AttackBody = new Bitmap[n][]; extra.AttackBodyPaths = new string[n][];
                for (int i = 0; i < n; ++i)
                {
                    extra.AttackBody[i] = new Bitmap[atkN]; extra.AttackBodyPaths[i] = new string[atkN];
                    for (int k = 0; k < atkN; ++k)
                    {
                        extra.AttackBodyPaths[i][k] = PathFor(dir, attackPattern, out why, (int)Math.Round(i * 360.0 / n), k);      // 처음 그릴 때 읽는다
                        if (extra.AttackBodyPaths[i][k] == null) return false;
                    }
                }
            }
            Dictionary<string, object> wo = o.ContainsKey("weapon") ? MiniJson.AsObject(o["weapon"]) : null;
            if (wo == null) return true;
            if (atkN <= 0) { why = "weapon 은 attackFrames 와 함께 써야 합니다"; return false; }
            WeaponLayers wl = new WeaponLayers();
            extra.Weapon = wl;
            wl.Poses = (int)MiniJson.Num(wo, "poses", 0);
            wl.Pad = (int)MiniJson.Num(wo, "pad", 0);
            if (wl.Poses != atkN + 1) { why = "weapon.poses 는 attackFrames + 1 (대기 자세 포함)이어야 합니다"; return false; }
            if (wl.Pad < 0 || wl.Pad > 1024) { why = "weapon.pad 는 0..1024"; return false; }
            string back = MiniJson.Str(wo, "backPattern", ""), front = MiniJson.Str(wo, "frontPattern", ""), anchors = MiniJson.Str(wo, "anchors", "");
            if (back.Length == 0 || front.Length == 0 || anchors.Length == 0) { why = "weapon 에 backPattern, frontPattern, anchors 가 필요합니다"; return false; }
            wl.Width = w + 2 * wl.Pad; wl.Height = h + 2 * wl.Pad;
            wl.Back = new Bitmap[wl.Poses][]; wl.Front = new Bitmap[wl.Poses][];
            wl.BackAt = new Point[wl.Poses][]; wl.FrontAt = new Point[wl.Poses][];
            for (int p = 0; p < wl.Poses; ++p)
            {
                wl.Back[p] = new Bitmap[n]; wl.Front[p] = new Bitmap[n];
                wl.BackAt[p] = new Point[n]; wl.FrontAt[p] = new Point[n];
                for (int i = 0; i < n; ++i)
                {
                    int deg = (int)Math.Round(i * 360.0 / n);
                    Bitmap bb = ReadPattern(dir, back, wl.Width, wl.Height, out why, p, deg);
                    if (bb == null) return false;
                    int bx, by;
                    wl.Back[p][i] = CropAlpha(bb, out bx, out by); wl.BackAt[p][i] = new Point(bx, by);
                    Bitmap ff = ReadPattern(dir, front, wl.Width, wl.Height, out why, p, deg);
                    if (ff == null) return false;
                    wl.Front[p][i] = CropAlpha(ff, out bx, out by); wl.FrontAt[p][i] = new Point(bx, by);
                }
            }
            string apath = Path.GetFullPath(Path.Combine(dir, anchors));
            if (!File.Exists(apath)) { why = "파일이 없습니다: " + anchors; return false; }
            Dictionary<string, object> ao = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(apath, System.Text.Encoding.UTF8)));
            if (ao == null) { why = anchors + ": 객체가 아닙니다"; return false; }
            wl.Grip = ReadPoints(ao, "grip", wl.Poses, n); wl.Tip = ReadPoints(ao, "tip", wl.Poses, n); wl.Butt = ReadPoints(ao, "butt", wl.Poses, n);
            int wf = extra.AttackBody == null ? 0 : (int)MiniJson.Num(o, "walkFrames", 0);
            wl.WalkGrip = wf > 0 ? ReadPoints(ao, "walkGrip", n, wf) : null;
            if (wl.Grip == null || wl.Tip == null || wl.Butt == null || (wf > 0 && wl.WalkGrip == null)) { why = anchors + ": grip/tip/butt/walkGrip 크기가 맞지 않습니다"; return false; }
            return true;
        }

        /// <summary>
        /// 영웅의 대기/피격/사망 그림(선택). idleFrames + idlePattern({0}=각도,{1}=프레임), hitPattern({0}=각도), deathFrames + deathPattern({0}=각도,{1}=단계).
        /// 대기 그림은 무기를 뺀 몸이라 무기 앵커 파일의 idleGrip([방향][프레임])이 같이 있어야 한다. 피격/사망은 무기까지 구운 그림이다.
        /// </summary>
        private static bool LoadPoses(string dir, Dictionary<string, object> o, int n, int w, int h, AssetSet extra, out string why)
        {
            why = null;
            int idleN = (int)MiniJson.Num(o, "idleFrames", 0), deathN = (int)MiniJson.Num(o, "deathFrames", 0);
            string idleP = MiniJson.Str(o, "idlePattern", ""), hitP = MiniJson.Str(o, "hitPattern", ""), deathP = MiniJson.Str(o, "deathPattern", "");
            if (idleN != 0 && (idleN < 2 || idleN > 32 || idleP.Length == 0)) { why = "idleFrames 는 2..32 이고 idlePattern 이 필요합니다"; return false; }
            if (deathN != 0 && (deathN < 1 || deathN > 8 || deathP.Length == 0)) { why = "deathFrames 는 1..8 이고 deathPattern 이 필요합니다"; return false; }
            if (idleN > 0)
            {
                if (extra.Weapon != null)
                {
                    Dictionary<string, object> wo = MiniJson.AsObject(o["weapon"]);
                    string apath = Path.GetFullPath(Path.Combine(dir, MiniJson.Str(wo, "anchors", "")));
                    Dictionary<string, object> ao = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(apath, System.Text.Encoding.UTF8)));
                    extra.Weapon.IdleGrip = ao == null ? null : ReadPoints(ao, "idleGrip", n, idleN);
                    if (extra.Weapon.IdleGrip == null) { why = "anchors 의 idleGrip 크기가 맞지 않습니다(방향 x idleFrames)"; return false; }
                }
                extra.Idle = new Bitmap[n][]; extra.IdlePaths = new string[n][];
                for (int i = 0; i < n; ++i)
                {
                    extra.Idle[i] = new Bitmap[idleN]; extra.IdlePaths[i] = new string[idleN];
                    for (int k = 0; k < idleN; ++k)
                    {
                        extra.IdlePaths[i][k] = PathFor(dir, idleP, out why, (int)Math.Round(i * 360.0 / n), k);
                        if (extra.IdlePaths[i][k] == null) return false;
                    }
                }
            }
            if (hitP.Length > 0)
            {
                extra.Hit = new Bitmap[n]; extra.HitPaths = new string[n];
                for (int i = 0; i < n; ++i)
                {
                    extra.HitPaths[i] = PathFor(dir, hitP, out why, (int)Math.Round(i * 360.0 / n));
                    if (extra.HitPaths[i] == null) return false;
                }
            }
            if (deathN > 0)
            {
                extra.Death = new Bitmap[n][]; extra.DeathPaths = new string[n][];
                for (int i = 0; i < n; ++i)
                {
                    extra.Death[i] = new Bitmap[deathN]; extra.DeathPaths[i] = new string[deathN];
                    for (int k = 0; k < deathN; ++k)
                    {
                        extra.DeathPaths[i][k] = PathFor(dir, deathP, out why, (int)Math.Round(i * 360.0 / n), k);
                        if (extra.DeathPaths[i][k] == null) return false;
                    }
                }
            }
            return true;
        }

        /// <summary>처음 쓸 때 읽는 그림. 크기가 기본 그림과 다르거나 읽지 못하면 null (호출한 쪽이 서 있는 그림으로 대신한다).</summary>
        internal static Bitmap ReadLazy(string path, int w, int h)
        {
            string why;
            Bitmap b = ReadArgb(path, out why);
            if (b == null) return null;
            if (b.Width != w || b.Height != h) { b.Dispose(); return null; }
            return b;
        }

        private static string PathFor(string dir, string pattern, out string why, params object[] args)
        {
            why = null;
            string rel;
            try { rel = string.Format(CultureInfo.InvariantCulture, pattern, args); }
            catch (FormatException) { why = "패턴 형식이 잘못됨: " + pattern; return null; }
            string path = Path.GetFullPath(Path.Combine(dir, rel));
            if (!Exists(path)) { why = "파일이 없습니다: " + rel; return null; }
            return path;
        }

        private static float[][][] ReadPoints(Dictionary<string, object> o, string key, int a, int b)
        {
            List<object> l1 = o.ContainsKey(key) ? MiniJson.AsArray(o[key]) : null;
            if (l1 == null || l1.Count != a) return null;
            float[][][] r = new float[a][][];
            for (int i = 0; i < a; ++i)
            {
                List<object> l2 = MiniJson.AsArray(l1[i]);
                if (l2 == null || l2.Count != b) return null;
                r[i] = new float[b][];
                for (int j = 0; j < b; ++j)
                {
                    List<object> pt = MiniJson.AsArray(l2[j]);
                    if (pt == null || pt.Count != 2 || !(pt[0] is double) || !(pt[1] is double)) return null;
                    r[i][j] = new float[] { (float)(double)pt[0], (float)(double)pt[1] };
                }
            }
            return r;
        }

        private static Bitmap ReadPattern(string dir, string pattern, int w, int h, out string why, params object[] args)
        {
            why = null;
            string rel;
            try { rel = string.Format(CultureInfo.InvariantCulture, pattern, args); }
            catch (FormatException) { why = "패턴 형식이 잘못됨: " + pattern; return null; }
            string path = Path.GetFullPath(Path.Combine(dir, rel));
            if (!Exists(path)) { why = "파일이 없습니다: " + rel; return null; }
            Bitmap b = ReadArgb(path, out why);
            if (b == null) return null;
            if (b.Width != w || b.Height != h) { b.Dispose(); why = "이미지 크기가 맞지 않습니다(" + w + "x" + h + " 필요): " + rel; return null; }
            return b;
        }

        private static AssetSet Fail(Bitmap[] frames) { return null; }

        private static Bitmap Crop(AtlasRef a, out string why)
        {
            why = null;
            Bitmap dst = new Bitmap(a.Fw, a.Fh, PixelFormat.Format32bppArgb);          // 새 비트맵은 완전히 투명하다
            if (a.W == 0 || a.H == 0) return dst;                                        // 완전히 비어 있던 그림
            Bitmap page = a.Page.Get(out why);
            if (page == null) { dst.Dispose(); return null; }
            if (a.X + a.W > page.Width || a.Y + a.H > page.Height) { dst.Dispose(); why = "시트 색인이 시트 크기를 벗어납니다: " + a.Page.Path; return null; }
            BitmapData sd = page.LockBits(new Rectangle(a.X, a.Y, a.W, a.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                BitmapData dd = dst.LockBits(new Rectangle(a.Ox, a.Oy, a.W, a.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int[] row = new int[a.W];
                    for (int y = 0; y < a.H; ++y)
                    {
                        Marshal.Copy(new IntPtr(sd.Scan0.ToInt64() + (long)y * sd.Stride), row, 0, a.W);
                        Marshal.Copy(row, 0, new IntPtr(dd.Scan0.ToInt64() + (long)y * dd.Stride), a.W);
                    }
                }
                finally { dst.UnlockBits(dd); }
            }
            finally { page.UnlockBits(sd); }
            return dst;
        }

        /// <summary>최상위 "atlases" 의 색인 JSON 을 읽어 전체 경로 -> 시트 위치 표를 만든다. 색인이나 시트가 잘못돼도 그 시트만 건너뛰고(개별 파일로 대신 읽음) warnings 에 적는다.
        /// 색인 형식: { "pages": ["atlas.png"], "files": { "warrior/000.png": [쪽, x, y, w, h, ox, oy, 원래폭, 원래높이] } }</summary>
        private static void LoadAtlasIndex(string dir, Dictionary<string, object> root, List<string> warnings)
        {
            _atlas = new Dictionary<string, AtlasRef>(StringComparer.OrdinalIgnoreCase);
            _atlasPages = new List<AtlasPage>();
            List<object> list = root.ContainsKey("atlases") ? MiniJson.AsArray(root["atlases"]) : null;
            if (list == null) return;
            foreach (object item in list)
            {
                string rel = item as string;
                if (string.IsNullOrEmpty(rel)) { warnings.Add("atlases: 경로가 아닌 항목이 있습니다"); continue; }
                string ipath = Path.GetFullPath(Path.Combine(dir, rel));
                try
                {
                    if (!File.Exists(ipath)) { warnings.Add("시트 색인이 없습니다(개별 파일로 대신 읽음): " + rel); continue; }
                    Dictionary<string, object> io = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(ipath, System.Text.Encoding.UTF8)));
                    List<object> pageNames = io == null ? null : MiniJson.AsArray(io.ContainsKey("pages") ? io["pages"] : null);
                    Dictionary<string, object> files = io == null || !io.ContainsKey("files") ? null : MiniJson.AsObject(io["files"]);
                    if (pageNames == null || pageNames.Count == 0 || files == null) { warnings.Add(rel + ": pages/files 가 없습니다"); continue; }
                    List<AtlasPage> pages = new List<AtlasPage>();
                    foreach (object n in pageNames)
                    {
                        string pn = n as string;
                        if (string.IsNullOrEmpty(pn)) { pages = null; break; }
                        AtlasPage ap = new AtlasPage(); ap.Path = Path.Combine(Path.GetDirectoryName(ipath), pn);
                        if (!File.Exists(ap.Path)) { warnings.Add("시트 이미지가 없습니다(개별 파일로 대신 읽음): " + pn); pages = null; break; }
                        pages.Add(ap);
                    }
                    if (pages == null) continue;
                    Dictionary<string, AtlasRef> added = new Dictionary<string, AtlasRef>(StringComparer.OrdinalIgnoreCase);
                    bool ok = true;
                    foreach (KeyValuePair<string, object> kv in files)
                    {
                        List<object> v = MiniJson.AsArray(kv.Value);
                        if (v == null || v.Count != 9) { ok = false; break; }
                        int[] r = new int[9];
                        for (int i = 0; i < 9; ++i) { if (!(v[i] is double)) { ok = false; break; } r[i] = (int)(double)v[i]; }
                        if (!ok) break;
                        // r: 쪽, x, y, w, h, ox, oy, fw, fh
                        if (r[0] < 0 || r[0] >= pages.Count || r[3] < 0 || r[4] < 0 || r[7] < 1 || r[8] < 1 || r[7] > 4096 || r[8] > 4096 || r[5] < 0 || r[6] < 0 || r[5] + r[3] > r[7] || r[6] + r[4] > r[8] || r[1] < 0 || r[2] < 0) { ok = false; break; }
                        AtlasRef a = new AtlasRef(); a.Page = pages[r[0]]; a.X = r[1]; a.Y = r[2]; a.W = r[3]; a.H = r[4]; a.Ox = r[5]; a.Oy = r[6]; a.Fw = r[7]; a.Fh = r[8];
                        added[Path.GetFullPath(Path.Combine(dir, kv.Key.Replace('\\', '/')))] = a;
                    }
                    if (!ok) { warnings.Add(rel + ": 색인 값이 잘못되어 이 시트는 쓰지 않습니다(개별 파일로 대신 읽음)"); continue; }
                    foreach (KeyValuePair<string, AtlasRef> kv in added) _atlas[kv.Key] = kv.Value;
                    _atlasPages.AddRange(pages);
                }
                catch (Exception e) { warnings.Add(rel + ": " + e.Message); }
            }
        }

        /// <summary>파일을 읽어 32비트 ARGB 비트맵으로(파일 잠금 없이) 만든다. GDI+ 가 못 읽는 형식(WebP 등)은 오류.</summary>
        private static Bitmap ReadArgb(string path, out string why)
        {
            why = null;
            _done += 1; Report(false);
            AtlasRef ar;
            if (_atlas != null && _atlas.TryGetValue(path, out ar)) return Crop(ar, out why);
            return ReadArgbFile(path, out why);
        }

        private static Bitmap ReadArgbFile(string path, out string why)
        {
            why = null;
            try
            {
                using (Image src = Image.FromFile(path))
                {
                    if (src.Width > 4096 || src.Height > 4096) { why = "이미지가 너무 큽니다(4096 초과): " + Path.GetFileName(path); return null; }
                    Bitmap dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                    using (GdiGfx g = GdiGfx.FromImage(dst))
                    {
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        g.DrawImage(src, 0, 0, src.Width, src.Height);
                    }
                    return dst;
                }
            }
            catch (Exception) { why = "이미지를 읽을 수 없습니다(PNG/JPG/GIF/BMP 만 가능, WebP 불가): " + Path.GetFileName(path); return null; }
        }

        /// <summary>
        /// 무기 그림은 몸 둘레에 큰 여백(pad)을 둔 정사각형이라 대부분이 투명하다. 메모리를 아끼려고 보이는 부분만 남기고 원래 위치를 돌려준다.
        /// 전부 투명하면 1x1 투명 그림을 돌려준다.
        /// </summary>
        private static Bitmap CropAlpha(Bitmap b, out int ox, out int oy)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            Bounds(b, ref x0, ref y0, ref x1, ref y1);
            if (x1 < 0) { b.Dispose(); ox = 0; oy = 0; return new Bitmap(1, 1, PixelFormat.Format32bppArgb); }
            x0 = Math.Max(0, x0 - 1); y0 = Math.Max(0, y0 - 1); x1 = Math.Min(b.Width - 1, x1 + 1); y1 = Math.Min(b.Height - 1, y1 + 1);
            Bitmap c = b.Clone(new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1), PixelFormat.Format32bppArgb);
            b.Dispose();
            ox = x0; oy = y0;
            return c;
        }

        /// <summary>알파가 8을 넘는 픽셀의 범위를 (x0,y0,x1,y1) 에 넓혀 담는다.</summary>
        private static void Bounds(Bitmap b, ref int x0, ref int y0, ref int x1, ref int y1)
        {
            int w = b.Width, h = b.Height;
            BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[w];
                for (int y = 0; y < h; ++y)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, w);
                    for (int x = 0; x < w; ++x)
                    {
                        if (((row[x] >> 24) & 255) <= 8) continue;
                        if (x < x0) x0 = x; if (x > x1) x1 = x;
                        if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
                }
            }
            finally { b.UnlockBits(d); }
        }
    }
}
