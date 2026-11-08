//***************************************************************************
// Creatures.cs : 영웅/몬스터를 그리는 곳. 그림 하나(종류 x 동작 프레임 x 방향)를 셰이딩해서 비트맵으로 한 번 그려 두고(SpriteCache),
// 매 프레임에는 그 비트맵을 찍기만 한다. 외부 이미지 파일 없이 코드로 그리므로 배포물이 늘지 않는다.
// 좌표는 "단위(unit)" 이고 발 밑 중앙이 (0,0), 위쪽이 -y 다. 걷기 8프레임 / 대기 1 / 공격 5프레임.
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    /// <summary>그림의 크기(단위). 비트맵 크기, 머리 위 표시 높이, 클릭 판정에 쓴다.</summary>
    public struct Look
    {
        public float HalfW, H;
        public Look(float halfW, float h) { HalfW = halfW; H = h; }
    }

    public sealed class CreatureRenderer : IDisposable
    {
        public const int AttackSprite = 255;
        public const int AttackBodyFrame0 = 230;    // 무기를 뺀 몸의 공격 자세 프레임 번호 시작(+자세 번호)        // 이미지 영웅의 공격 그림(무기를 흐리게 한 것) 프레임 번호
        public const int FrameIdle = 8, FrameAttack0 = 9, AttackFrames = 5, WalkFrames = 8;
        private const float PadTop = 0.25f, PadBottom = 0.55f;

        private readonly SpriteCache _cache;
        private readonly SpriteCache.PaintFn _paintFn;
        private bool _pMonster; private uint _pTpl; private int _pFrame; private bool _pLeft; private float _ppu;
        private AssetSet _pAsset; private int _pDir;

        public CreatureRenderer(SpriteCache cache)
        {
            _cache = cache;
            _paintFn = PaintCurrent;
        }

        public void Dispose() { }

        // ------------------------------------------------------------------ 크기

        public static Look LookOf(bool monster, uint tpl)
        {
            AssetSet a = AssetSprites.Find(monster, tpl);
            if (a != null) return a.Look;
            if (!monster) return new Look(1.9f, 3.6f);
            switch (tpl)
            {
                case 1: return new Look(1.1f, 2.1f);
                case 2: return new Look(1.3f, 2.3f);
                case 3: return new Look(1.9f, 1.8f);
                case 4: return new Look(1.8f, 2.9f);
                case 5: return new Look(1.4f, 2.8f);
                case 6: return new Look(2.4f, 2.0f);
                case 7: return new Look(1.8f, 3.2f);
                case 8: return new Look(3.0f, 3.1f);
                case 9: return new Look(3.3f, 5.4f);
                case 10: return new Look(2.6f, 4.6f);
                default: return new Look(1.1f, 1.7f);
            }
        }
        public static Look LookOf(Entity e) { return LookOf(e.IsMonster, e.TemplateId); }

        /// <summary>대기 중에도 계속 움직이는 종류(슬라임, 박쥐)인지.</summary>
        private static bool AnimatesIdle(bool monster, uint tpl) { return monster && (tpl == 1 || tpl == 4 || tpl == 10 || tpl > 10); }

        // ------------------------------------------------------------------ 찍기

        /// <summary>
        /// (sx, sy) 는 발 밑 화면 좌표. 번쩍임(hurt)과 시체(dead)는 파생 그림을 쓴다.
        /// </summary>
        public void Draw(Graphics g, Entity e, ViewState v, long now, float sx, float sy, bool hurt)
        {
            bool monster = e.IsMonster;
            uint tpl = e.TemplateId;
            Look look = LookOf(monster, tpl);
            AssetSet asset = AssetSprites.Find(monster, tpl);

            float walked;
            bool walking = e.WalkingAt(now, out walked);
            int frame;
            long atkAge = now - e.AttackAnimMs;
            if (!monster && atkAge >= 0 && atkAge < 280) frame = FrameAttack0 + Math.Min(AttackFrames - 1, (int)(atkAge * AttackFrames / 280));
            else if (walking) frame = ((int)(walked / (monster ? 0.34f : 0.4f)) & 7);
            else if (AnimatesIdle(monster, tpl)) frame = (int)((now / 120 + (long)(e.Id & 7)) & 7);
            else frame = FrameIdle;
            if (e.Dead) frame = FrameIdle;

            bool left = e.FaceX < -0.05f;
            float ppuQ = QuantizePpu(v.Scale);
            int dir = 0;
            bool walkAnim = false;
            int weaponPose = 0;
            if (asset != null)
            {
                dir = asset.IndexFor(e.FaceX, e.FaceY); left = false; frame = 0;
                // 걷기 프레임이 있으면 걸은 거리에 맞춰 넘긴다 (frame 0 은 서 있는 그림이라 1부터)
                if (walking && !e.Dead && asset.WalkCount > 0) { frame = 1 + ((int)(walked / (monster ? 0.34f : HeroStridePx * asset.Scale)) % asset.WalkCount); walkAnim = true; }
                // 공격/시전 중인 영웅: 코드로 그린 무기(HeroWeapons)가 휘둘러지는 동안 몸에 그려진 무기는 흐리게 한 그림으로 바꾼다
                if (!monster && !e.Dead && asset.Weapon != null)
                {
                    int ak;
                    if (HeroWeapons.PoseOf(e, now, asset.Weapon.Poses, out ak)) { frame = AttackBodyFrame0 + ak; walkAnim = false; weaponPose = ak + 1; }
                }
                else if (!monster && !e.Dead && asset.Attack != null && (e.Casting || (atkAge >= 0 && atkAge < HeroWeapons.TotalMs))) { frame = AttackSprite; walkAnim = false; }
            }

            int w = (int)Math.Ceiling((look.HalfW + 0.25f) * 2 * ppuQ), h = (int)Math.Ceiling((look.H + PadTop + PadBottom) * ppuQ);
            float ax = w / 2f, ay = h - PadBottom * ppuQ;
            long key = ((monster ? 1L : 0L) << 40) | ((long)(tpl & 0xFF) << 32) | ((long)frame << 24) | ((left ? 1L : 0L) << 20) | (long)(ppuQ * 2);
            if (asset != null) key |= (1L << 48) | ((long)dir << 50);       // 이미지 그림은 방향 번호로 구분
            _pMonster = monster; _pTpl = tpl; _pFrame = frame; _pLeft = left; _ppu = ppuQ; _pAsset = asset; _pDir = dir;
            SpriteCache.Entry en = _cache.Get(key, w, h, ax, ay, _paintFn);

            float k = (float)(v.Scale / ppuQ);
            if (e.Dead)
            {
                SpriteCache.Entry body = _cache.GetDerived(key | (2L << 44), en, 2);
                g.DrawImage(body.Bmp, sx - ax * k * 1.08f, sy - ay * k * 0.34f, w * k * 1.08f, h * k * 0.34f);
                return;
            }
            if (hurt) en = _cache.GetDerived(key | (1L << 44), en, 1);
            if (asset != null)
            {
                // 프레임 애니메이션이 없으면 걸을 땐 튀고, 설 땐 숨 쉬듯 눌러서 움직임을 흉내 낸다. 발 밑은 고정.
                float hop = walking && !walkAnim ? Math.Abs((float)Math.Sin(walked * 3.4f)) * 0.16f : 0f;
                // 날아다니는 몬스터(박쥐, 날개 해골 기사)는 땅에서 떠서 위아래로 일렁인다. 그림자는 땅에 남는다.
                if (monster && (tpl == 4 || tpl == 11)) hop += 0.45f + 0.14f * (float)Math.Sin(now / 230.0 + (e.Id & 15));
                float sq = walking ? 1f : 1f + (float)Math.Sin(now / 380.0 + (e.Id & 15)) * 0.018f;
                float dh = h * k * sq, dy = sy - ay * k * sq - hop * (float)v.Scale;
                if (asset.Weapon != null && !monster)
                {
                    GraphicsState gs = g.Save();
                    g.TranslateTransform(sx, sy - hop * (float)v.Scale);
                    g.ScaleTransform(1f, sq);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    DrawWeapon(g, asset, dir, weaponPose, weaponPose == 0 && walkAnim ? frame - 1 : -1, (float)(v.Scale * asset.Scale), true);
                    g.Restore(gs);
                    g.DrawImage(en.Bmp, sx - ax * k, dy, w * k, dh);
                    gs = g.Save();
                    g.TranslateTransform(sx, sy - hop * (float)v.Scale);
                    g.ScaleTransform(1f, sq);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    DrawWeapon(g, asset, dir, weaponPose, weaponPose == 0 && walkAnim ? frame - 1 : -1, (float)(v.Scale * asset.Scale), false);
                    g.Restore(gs);
                    return;
                }
                g.DrawImage(en.Bmp, sx - ax * k, dy, w * k, dh);
                return;
            }
            float breathe = (!walking && !monster) ? (float)Math.Sin(now / 420.0 + (e.Id & 15)) * 0.025f * (float)v.Scale : 0f;
            g.DrawImage(en.Bmp, sx - ax * k, sy - ay * k + breathe, w * k, h * k);
        }

        /// <summary>영웅 걷기 프레임 한 장이 넘어가는 걸음 길이(단위). 걷기 그림의 발 움직임이 땅 위를 미끄러지지 않도록 한 걸음 거리를 프레임 수로 나눈 값이다.</summary>
        private const float HeroStridePx = 20f;   // 영웅 걷기 한 프레임에 발이 땅에서 밀리는 스프라이트 픽셀 수(render_heroes.py: WALK_STEP*96/(STANCE*8))

        /// <summary>
        /// 몸과 따로 그린 무기 한 겹(back = 몸에 가려지는 부분, 아니면 앞으로 나온 부분)을 발 밑 원점(0,0) 기준으로 찍는다. s = 원본 픽셀 하나의 화면 크기.
        /// 걷기 프레임(walkFrame >= 0)이면 그 프레임의 손 위치로 대기 자세 무기를 옮긴다.
        /// </summary>
        private static void DrawWeapon(Graphics g, AssetSet a, int dir, int pose, int walkFrame, float s, bool back)
        {
            WeaponLayers w = a.Weapon;
            float ox = 0f, oy = 0f;
            if (pose == 0 && walkFrame >= 0 && w.WalkGrip != null)
            {
                float[] wg = w.WalkGrip[dir][walkFrame % w.WalkGrip[dir].Length], g0 = w.Grip[0][dir];
                ox = wg[0] - g0[0]; oy = wg[1] - g0[1];
            }
            Bitmap src = back ? w.Back[pose][dir] : w.Front[pose][dir];
            g.DrawImage(src, new RectangleF((ox - w.Pad - a.Cx) * s, (oy - w.Pad - a.Foot) * s, src.Width * s, src.Height * s),
                new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
        }

        /// <summary>확대 배율을 2px 단위로 맞춰 비트맵 종류가 늘어나지 않게 한다.</summary>
        public static float QuantizePpu(double scale)
        {
            float q = (float)(Math.Round(scale / 2.0) * 2.0);
            return q < 4f ? 4f : q;
        }

        private void PaintCurrent(Graphics g)
        {
            g.ScaleTransform(_ppu, _ppu);
            if (_pAsset != null)
            {
                AssetSet a = _pAsset;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                Bitmap src = (_pFrame >= AttackBodyFrame0 && _pFrame < AttackSprite && a.AttackBody != null) ? a.AttackBody[_pDir][_pFrame - AttackBodyFrame0] : (_pFrame == AttackSprite && a.Attack != null) ? a.Attack[_pDir] : (_pFrame > 0 && a.WalkCount > 0) ? a.Walk[_pDir][(_pFrame - 1) % a.WalkCount] : a.Frames[_pDir];
                g.DrawImage(src, new RectangleF(-a.Cx * a.Scale, -a.Foot * a.Scale, src.Width * a.Scale, src.Height * a.Scale),
                    new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
                return;
            }
            if (_pLeft) g.ScaleTransform(-1f, 1f);
            if (_pMonster) Monsters.Paint(g, _pTpl, _pFrame);
            else Heroes.Paint(g, _pTpl, _pFrame);
        }
    }

    // ====================================================================== 영웅
    internal static class Heroes
    {
        private static readonly Color Ol = Color.FromArgb(215, 22, 18, 28);
        private static readonly Color Skin = Color.FromArgb(246, 216, 182), SkinD = Color.FromArgb(196, 150, 118);
        private static readonly Color Steel = Color.FromArgb(214, 220, 234), SteelD = Color.FromArgb(98, 106, 128);
        private static readonly Color Leather = Color.FromArgb(132, 90, 52), LeatherD = Color.FromArgb(70, 44, 26);
        private static readonly Color Gold = Color.FromArgb(246, 206, 90), GoldD = Color.FromArgb(170, 120, 36);

        public static void Paint(Graphics g, uint tpl, int frame)
        {
            float phase = frame < CreatureRenderer.FrameIdle ? frame / 8f * 6.2832f : 0f;
            float sn = frame < CreatureRenderer.FrameIdle ? (float)Math.Sin(phase) : 0f;
            float cs = frame < CreatureRenderer.FrameIdle ? (float)Math.Cos(phase) : 1f;
            float bob = frame < CreatureRenderer.FrameIdle ? -Math.Abs(cs) * 0.1f : 0f;
            float atk = frame >= CreatureRenderer.FrameAttack0 ? (frame - CreatureRenderer.FrameAttack0) / (float)(CreatureRenderer.AttackFrames - 1) : -1f;
            switch (tpl)
            {
                case 1: Warrior(g, sn, cs, bob, atk); break;
                case 2: Archer(g, sn, cs, bob, atk); break;
                case 3: Mage(g, sn, cs, bob, atk); break;
                case 4: Lancer(g, sn, cs, bob, atk); break;
                default: Adventurer(g, sn, cs, bob, atk); break;
            }
        }

        private static void Legs(Graphics g, float sn, float cs, float bob, Color cloth, Color boot)
        {
            float lx = -0.24f + sn * 0.2f, rx = 0.24f - sn * 0.2f;
            float ly = -0.1f - Math.Max(0f, cs) * 0.14f, ry = -0.1f - Math.Max(0f, -cs) * 0.14f;
            P.Limb(g, -0.22f, -0.95f + bob, lx, ly - 0.12f, 0.34f, cloth, Ol);
            P.Limb(g, 0.22f, -0.95f + bob, rx, ry - 0.12f, 0.34f, cloth, Ol);
            P.Ball(g, lx + 0.03f, ly, 0.24f, 0.13f, Col.Light(boot, 0.25f), boot, Ol, 0.05f);
            P.Ball(g, rx + 0.03f, ry, 0.24f, 0.13f, Col.Light(boot, 0.25f), boot, Ol, 0.05f);
        }

        private static void Head(Graphics g, float bob, float cx = 0f)
        {
            P.Ball(g, cx, -2.1f + bob, 0.36f, 0.38f, Skin, SkinD, Ol, 0.05f);
            P.Fill(g, cx - 0.13f, -2.08f + bob, 0.045f, 0.06f, Color.FromArgb(40, 30, 30));
            P.Fill(g, cx + 0.13f, -2.08f + bob, 0.045f, 0.06f, Color.FromArgb(40, 30, 30));
            P.Fill(g, cx, -1.93f + bob, 0.08f, 0.025f, Color.FromArgb(150, 90, 80));
        }

        private static void Warrior(Graphics g, float sn, float cs, float bob, float atk)
        {
            Color tunic = Color.FromArgb(206, 66, 58), tunicD = Color.FromArgb(118, 28, 34);
            // 망토
            P.Poly(g, new PointF[] { new PointF(-0.55f, -1.7f + bob), new PointF(0.55f, -1.7f + bob), new PointF(0.72f + sn * 0.05f, -0.45f), new PointF(-0.72f + sn * 0.05f, -0.45f) },
                Color.FromArgb(150, 34, 40), Color.FromArgb(84, 18, 26), Ol, 0.05f);
            Legs(g, sn, cs, bob, Color.FromArgb(70, 60, 76), LeatherD);
            // 몸통 + 흉갑 + 허리띠
            P.Round(g, -0.56f, -1.82f + bob, 1.12f, 0.98f, 0.26f, tunic, tunicD, Ol, 0.05f);
            P.Poly(g, new PointF[] { new PointF(-0.42f, -1.72f + bob), new PointF(0.42f, -1.72f + bob), new PointF(0.34f, -1.12f + bob), new PointF(-0.34f, -1.12f + bob) }, Steel, SteelD, Ol, 0.04f);
            P.Line(g, -0.5f, -0.95f + bob, 0.5f, -0.95f + bob, 0.12f, Leather);
            P.Fill(g, 0f, -0.95f + bob, 0.1f, 0.08f, Gold);
            // 왼팔 + 방패
            P.Limb(g, -0.6f, -1.62f + bob, -0.82f, -1.16f + bob, 0.3f, Col.Dark(tunic, 0.1f), Ol);
            P.Round(g, -1.18f, -1.72f + bob, 0.62f, 1.0f, 0.28f, Color.FromArgb(70, 110, 190), Color.FromArgb(28, 50, 112), Ol, 0.06f);
            P.Ball(g, -0.87f, -1.26f + bob, 0.15f, 0.15f, Gold, GoldD, Ol, 0.03f);
            P.Round(g, -1.18f, -1.72f + bob, 0.62f, 0.12f, 0.05f, Steel, SteelD, Ol, 0.03f);
            // 오른팔 + 검
            float th = atk < 0 ? -72f : -150f + 190f * atk;
            float rad = th * 0.017453f;
            float dx = (float)Math.Cos(rad), dy = (float)Math.Sin(rad);
            float hx = 0.62f + dx * 0.34f, hy = -1.6f + bob + dy * 0.34f + 0.3f * (atk < 0 ? 1f : 0.5f);
            P.Limb(g, 0.6f, -1.62f + bob, hx, hy, 0.3f, Col.Dark(tunic, 0.1f), Ol);
            float nx = -dy, ny = dx;
            if (atk >= 0.15f) Trail(g, hx, hy, Math.Max(-150f, th - 75f), th, 0.45f, 1.42f, Color.FromArgb(200, 225, 255), (int)(170 * (1f - atk * 0.55f)));
            PointF[] blade = {
                new PointF(hx + dx * 0.12f + nx * 0.075f, hy + dy * 0.12f + ny * 0.075f),
                new PointF(hx + dx * 1.18f + nx * 0.05f, hy + dy * 1.18f + ny * 0.05f),
                new PointF(hx + dx * 1.36f, hy + dy * 1.36f),
                new PointF(hx + dx * 1.18f - nx * 0.05f, hy + dy * 1.18f - ny * 0.05f),
                new PointF(hx + dx * 0.12f - nx * 0.075f, hy + dy * 0.12f - ny * 0.075f) };
            P.Poly(g, blade, Color.White, Color.FromArgb(150, 158, 178), Ol, 0.03f);
            P.Limb(g, hx + dx * 0.12f + nx * 0.28f, hy + dy * 0.12f + ny * 0.28f, hx + dx * 0.12f - nx * 0.28f, hy + dy * 0.12f - ny * 0.28f, 0.09f, Gold, Ol);
            P.Ball(g, hx, hy, 0.13f, 0.13f, Skin, SkinD, Ol, 0.03f);
            // 어깨 보호대
            P.Ball(g, -0.62f, -1.7f + bob, 0.27f, 0.21f, Steel, SteelD, Ol, 0.04f);
            P.Ball(g, 0.62f, -1.7f + bob, 0.27f, 0.21f, Steel, SteelD, Ol, 0.04f);
            // 머리 + 투구
            Head(g, bob);
            using (GraphicsPath dome = new GraphicsPath())
            {
                dome.AddArc(-0.41f, -2.52f + bob, 0.82f, 0.8f, 180, 180);
                dome.CloseFigure();
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, -2.52f + bob), new PointF(0, -2.12f + bob), Steel, SteelD)) g.FillPath(b, dome);
                using (Pen p = new Pen(Ol, 0.05f)) g.DrawPath(p, dome);
            }
            P.Line(g, 0f, -2.2f + bob, 0f, -1.98f + bob, 0.07f, SteelD);
            P.Poly(g, new PointF[] { new PointF(-0.07f, -2.5f + bob), new PointF(0.07f, -2.5f + bob), new PointF(0.3f, -2.92f + bob - sn * 0.05f), new PointF(-0.05f, -2.7f + bob) }, Color.FromArgb(230, 70, 60), Color.FromArgb(150, 30, 36), Ol, 0.03f);
        }

        private static void Archer(Graphics g, float sn, float cs, float bob, float atk)
        {
            Color cloak = Color.FromArgb(70, 152, 90), cloakD = Color.FromArgb(28, 80, 50);
            Color tunic = Color.FromArgb(104, 178, 100), tunicD = Color.FromArgb(48, 110, 62);
            // 화살통(등 뒤) + 망토
            P.Round(g, 0.34f, -1.95f + bob, 0.3f, 0.95f, 0.1f, Leather, LeatherD, Ol, 0.04f);
            for (int i = 0; i < 3; ++i) P.Line(g, 0.4f + i * 0.09f, -1.95f + bob, 0.38f + i * 0.09f, -2.2f + bob, 0.04f, Color.FromArgb(240, 230, 200));
            P.Poly(g, new PointF[] { new PointF(-0.55f, -1.72f + bob), new PointF(0.55f, -1.72f + bob), new PointF(0.78f + sn * 0.05f, -0.4f), new PointF(-0.78f + sn * 0.05f, -0.4f) }, cloak, cloakD, Ol, 0.05f);
            Legs(g, sn, cs, bob, Color.FromArgb(86, 74, 52), LeatherD);
            P.Round(g, -0.5f, -1.8f + bob, 1.0f, 0.92f, 0.24f, tunic, tunicD, Ol, 0.05f);
            P.Line(g, -0.46f, -0.98f + bob, 0.46f, -0.98f + bob, 0.1f, Leather);
            // 활 (왼손). 공격 때 시위를 당겼다 놓는다
            float pull = atk < 0 ? 0f : (atk < 0.5f ? atk * 2f : Math.Max(0f, 1f - (atk - 0.5f) * 5f));
            float bx = -1.0f, by = -1.45f + bob;
            using (Pen bow = new Pen(Color.FromArgb(150, 98, 54), 0.13f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen bowO = new Pen(Ol, 0.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawBezier(bowO, bx + 0.12f, by - 0.85f, bx - 0.62f, by - 0.5f, bx - 0.62f, by + 0.5f, bx + 0.12f, by + 0.85f);
                g.DrawBezier(bow, bx + 0.12f, by - 0.85f, bx - 0.62f, by - 0.5f, bx - 0.62f, by + 0.5f, bx + 0.12f, by + 0.85f);
            }
            float sxp = bx + 0.12f + pull * 0.55f;
            P.Line(g, bx + 0.12f, by - 0.85f, sxp, by, 0.025f, Color.FromArgb(240, 240, 225));
            P.Line(g, sxp, by, bx + 0.12f, by + 0.85f, 0.025f, Color.FromArgb(240, 240, 225));
            if (atk >= 0 && atk < 0.55f) { P.Line(g, sxp, by, bx - 0.45f, by, 0.05f, Color.FromArgb(224, 214, 180)); P.Line(g, sxp, by, sxp + 0.12f, by, 0.07f, Color.FromArgb(220, 80, 70)); }
            P.Limb(g, -0.55f, -1.6f + bob, bx + 0.05f, by + 0.05f, 0.28f, Col.Dark(tunic, 0.1f), Ol);
            P.Limb(g, 0.52f, -1.6f + bob, sxp - 0.05f, by + 0.1f, 0.28f, Col.Dark(tunic, 0.1f), Ol);
            P.Ball(g, bx + 0.05f, by + 0.05f, 0.12f, 0.12f, Skin, SkinD, Ol, 0.03f);
            P.Ball(g, sxp - 0.05f, by + 0.1f, 0.12f, 0.12f, Skin, SkinD, Ol, 0.03f);
            // 후드
            P.Ball(g, 0f, -2.14f + bob, 0.46f, 0.44f, cloak, cloakD, Ol, 0.05f);
            P.Ball(g, 0f, -2.06f + bob, 0.3f, 0.32f, Skin, SkinD, Ol, 0.04f);
            P.Fill(g, -0.11f, -2.06f + bob, 0.04f, 0.055f, Color.FromArgb(30, 60, 40));
            P.Fill(g, 0.11f, -2.06f + bob, 0.04f, 0.055f, Color.FromArgb(30, 60, 40));
            using (GraphicsPath hood = new GraphicsPath())
            {
                hood.AddArc(-0.4f, -2.62f + bob, 0.8f, 0.66f, 180, 180); hood.CloseFigure();
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, -2.62f + bob), new PointF(0, -2.2f + bob), Col.Light(cloak, 0.2f), cloakD)) g.FillPath(b, hood);
                using (Pen p = new Pen(Ol, 0.05f)) g.DrawPath(p, hood);
            }
            P.Poly(g, new PointF[] { new PointF(-0.12f, -2.58f + bob), new PointF(0.1f, -2.58f + bob), new PointF(0.42f, -2.82f + bob), new PointF(0.12f, -2.5f + bob) }, Color.FromArgb(230, 90, 70), Color.FromArgb(170, 50, 50), Ol, 0.03f);
        }

        private static void Mage(Graphics g, float sn, float cs, float bob, float atk)
        {
            Color robe = Color.FromArgb(94, 112, 232), robeD = Color.FromArgb(34, 42, 130);
            // 신발은 로브 밑으로 번갈아 보인다
            P.Ball(g, -0.26f + sn * 0.18f, -0.08f - Math.Max(0f, cs) * 0.12f, 0.24f, 0.12f, Leather, LeatherD, Ol, 0.04f);
            P.Ball(g, 0.26f - sn * 0.18f, -0.08f - Math.Max(0f, -cs) * 0.12f, 0.24f, 0.12f, Leather, LeatherD, Ol, 0.04f);
            P.Poly(g, new PointF[] { new PointF(-0.5f, -1.78f + bob), new PointF(0.5f, -1.78f + bob), new PointF(0.88f + sn * 0.1f, -0.14f), new PointF(-0.88f + sn * 0.1f, -0.14f) }, robe, robeD, Ol, 0.05f);
            P.Poly(g, new PointF[] { new PointF(-0.12f, -1.7f + bob), new PointF(0.12f, -1.7f + bob), new PointF(0.2f + sn * 0.05f, -0.16f), new PointF(-0.2f + sn * 0.05f, -0.16f) }, Color.FromArgb(150, 168, 255), Color.FromArgb(70, 88, 190), Color.FromArgb(0, 0, 0, 0), 0f);
            P.Line(g, -0.85f + sn * 0.1f, -0.2f, 0.85f + sn * 0.1f, -0.2f, 0.1f, Gold);
            P.Line(g, -0.46f, -1.0f + bob, 0.46f, -1.0f + bob, 0.1f, Gold);
            // 지팡이 (오른손). 공격 때 앞으로 기울며 구슬이 번쩍인다
            float tilt = atk < 0 ? 0f : (float)Math.Sin(atk * Math.PI) * 0.7f;
            float hx = 0.98f, hy = -1.25f + bob;
            float tx = hx - (float)Math.Sin(tilt) * 1.8f, ty = hy - (float)Math.Cos(tilt) * 1.7f;
            float bx0 = hx + (float)Math.Sin(tilt) * 1.1f, by0 = hy + (float)Math.Cos(tilt) * 1.2f;
            P.Limb(g, bx0, by0, tx, ty, 0.11f, Leather, Ol);
            float orbR = atk < 0 ? 0.2f : 0.2f + (float)Math.Sin(atk * Math.PI) * 0.12f;
            P.Glow(g, tx, ty, orbR * (atk < 0 ? 3.2f : 4.2f), Color.FromArgb(255, 170, 70), atk < 0 ? 120 : 230);
            P.Ball(g, tx, ty, orbR, orbR, Color.FromArgb(255, 250, 200), Color.FromArgb(255, 130, 40), Ol, 0.03f);
            P.Limb(g, 0.52f, -1.62f + bob, hx, hy, 0.3f, robe, Ol);
            P.Limb(g, -0.52f, -1.62f + bob, -0.78f, -1.12f + bob, 0.3f, robe, Ol);
            P.Ball(g, hx, hy, 0.12f, 0.12f, Skin, SkinD, Ol, 0.03f);
            P.Ball(g, -0.78f, -1.12f + bob, 0.12f, 0.12f, Skin, SkinD, Ol, 0.03f);
            // 머리, 수염, 모자
            Head(g, bob);
            P.Poly(g, new PointF[] { new PointF(-0.26f, -1.98f + bob), new PointF(0.26f, -1.98f + bob), new PointF(0.04f, -1.45f + bob) }, Color.FromArgb(246, 246, 250), Color.FromArgb(190, 192, 205), Ol, 0.03f);
            P.Ball(g, 0f, -2.3f + bob, 0.66f, 0.17f, Col.Light(robe, 0.1f), robeD, Ol, 0.05f);
            P.Poly(g, new PointF[] { new PointF(-0.4f, -2.3f + bob), new PointF(0.4f, -2.3f + bob), new PointF(0.3f + sn * 0.05f, -2.95f + bob), new PointF(0.12f + sn * 0.1f, -3.4f + bob), new PointF(-0.12f, -3.0f + bob) }, robe, robeD, Ol, 0.05f);
            P.Line(g, -0.38f, -2.38f + bob, 0.38f, -2.38f + bob, 0.1f, Gold);
        }

        /// <summary>
        /// 휘두른 궤적: (cx,cy) 를 중심으로 th0 -> th1 도 사이를 r0..r1 반지름의 반달 모양으로 채운다(칼날 잔상).
        /// 바깥쪽이 가장 진하고 안쪽으로 갈수록 투명하다.
        /// </summary>
        private static void Trail(Graphics g, float cx, float cy, float th0, float th1, float r0, float r1, Color c, int alpha)
        {
            if (th1 - th0 < 2f || alpha < 6) return;
            const int N = 8;
            PointF[] pts = new PointF[(N + 1) * 2];
            for (int i = 0; i <= N; ++i)
            {
                float a = (th0 + (th1 - th0) * i / N) * 0.017453f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1);
                pts[(N + 1) * 2 - 1 - i] = new PointF(cx + (float)Math.Cos(a) * r0, cy + (float)Math.Sin(a) * r0);
            }
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddPolygon(pts);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha / 2, c))) g.FillPath(b, gp);
                // 날 끝이 지나간 가장자리
                using (Pen p = new Pen(Color.FromArgb(Math.Min(255, alpha + 40), 255, 255, 255), 0.05f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLines(p, new PointF[] { pts[0], pts[N / 2], pts[N] }); }
            }
        }

        /// <summary>창 한 자루: 손(hx,hy)에서 thDeg 방향으로 len 길이(뒤로 조금 더 자루가 나온다).</summary>
        private static void Spear(Graphics g, float hx, float hy, float thDeg, float len)
        {
            float rad = thDeg * 0.017453f;
            float dx = (float)Math.Cos(rad), dy = (float)Math.Sin(rad), nx = -dy, ny = dx;
            P.Line(g, hx - dx * len * 0.3f, hy - dy * len * 0.3f, hx + dx * len * 0.78f, hy + dy * len * 0.78f, 0.07f, Color.FromArgb(142, 100, 70));
            PointF[] blade = {
                new PointF(hx + dx * len * 0.78f + nx * 0.07f, hy + dy * len * 0.78f + ny * 0.07f),
                new PointF(hx + dx * len * 0.9f + nx * 0.1f, hy + dy * len * 0.9f + ny * 0.1f),
                new PointF(hx + dx * len, hy + dy * len),
                new PointF(hx + dx * len * 0.9f - nx * 0.1f, hy + dy * len * 0.9f - ny * 0.1f),
                new PointF(hx + dx * len * 0.78f - nx * 0.07f, hy + dy * len * 0.78f - ny * 0.07f) };
            P.Poly(g, blade, Color.White, Color.FromArgb(150, 170, 190), Ol, 0.03f);
            P.Ball(g, hx, hy, 0.12f, 0.12f, Skin, SkinD, Ol, 0.03f);
        }

        private static void Lancer(Graphics g, float sn, float cs, float bob, float atk)
        {
            Color armor = Color.FromArgb(184, 192, 206), armorD = Color.FromArgb(104, 114, 134);
            Color cape = Color.FromArgb(36, 124, 134), capeD = Color.FromArgb(20, 70, 84);
            // 청록 망토(아랫단이 갈라지고 휘날림)
            P.Poly(g, new PointF[] { new PointF(-0.5f, -1.72f + bob), new PointF(0.5f, -1.72f + bob), new PointF(0.66f + sn * 0.12f, -0.55f), new PointF(0.3f + sn * 0.12f, -0.8f), new PointF(0f + sn * 0.12f, -0.5f), new PointF(-0.3f + sn * 0.12f, -0.8f), new PointF(-0.66f + sn * 0.12f, -0.55f) },
                cape, capeD, Ol, 0.05f);
            Legs(g, sn, cs, bob, Color.FromArgb(74, 88, 104), LeatherD);
            P.Round(g, -0.5f, -1.8f + bob, 1.0f, 0.92f, 0.24f, armor, armorD, Ol, 0.05f);
            P.Line(g, -0.46f, -0.98f + bob, 0.46f, -0.98f + bob, 0.11f, cape);
            P.Fill(g, 0.05f, -0.98f + bob, 0.09f, 0.07f, Gold);
            // 오른손: 창. 대기: 비스듬히 세워 듦, 공격: 앞으로 찌름
            float ra = atk < 0 ? 0f : Math.Min(1f, atk / 0.6f);
            float th = atk < 0 ? -70f : -30f + 30f * ra;
            float hxp = 0.6f + (atk < 0 ? 0f : 0.35f * ra), hyp = -1.35f + bob;
            P.Limb(g, -0.58f, -1.62f + bob, -0.62f, -1.1f + bob, 0.28f, Col.Dark(armor, 0.05f), Ol);
            P.Limb(g, 0.58f, -1.62f + bob, hxp, hyp, 0.28f, Col.Dark(armor, 0.05f), Ol);
            if (atk >= 0.1f && ra > 0.05f && ra < 1f) Trail(g, hxp, hyp, th - 20f, th + 5f, 0.3f, 1.9f, Color.FromArgb(170, 240, 235), 190);
            Spear(g, hxp, hyp, th, 2.0f);
            // 머리: 긴 금발 + 서클릿
            Color hair = Color.FromArgb(238, 200, 98), hairD = Color.FromArgb(176, 134, 52);
            P.Poly(g, new PointF[] { new PointF(-0.34f, -2.3f + bob), new PointF(0.34f, -2.3f + bob), new PointF(0.4f - sn * 0.1f, -1.2f), new PointF(-0.4f - sn * 0.1f, -1.2f) }, hair, hairD, Ol, 0.04f);
            P.Ball(g, 0f, -2.1f + bob, 0.36f, 0.38f, Skin, SkinD, Ol, 0.05f);
            P.Fill(g, -0.12f, -2.12f + bob, 0.05f, 0.04f, Color.FromArgb(250, 250, 255));
            P.Fill(g, 0.12f, -2.12f + bob, 0.05f, 0.04f, Color.FromArgb(250, 250, 255));
            P.Fill(g, -0.12f, -2.12f + bob, 0.025f, 0.035f, Color.FromArgb(60, 110, 200));
            P.Fill(g, 0.12f, -2.12f + bob, 0.025f, 0.035f, Color.FromArgb(60, 110, 200));
            using (GraphicsPath fringe = new GraphicsPath())
            {
                fringe.AddArc(-0.4f, -2.58f + bob, 0.8f, 0.7f, 180, 180); fringe.CloseFigure();
                using (SolidBrush hb = new SolidBrush(hair)) g.FillPath(hb, fringe);
                using (Pen pp = new Pen(Ol, 0.05f)) g.DrawPath(pp, fringe);
            }
            P.Fill(g, -0.3f, -2.36f + bob, 0.6f, 0.05f, Gold);
        }

        private static void Adventurer(Graphics g, float sn, float cs, float bob, float atk)
        {
            Color c = Color.FromArgb(176, 120, 210), d = Color.FromArgb(80, 50, 130);
            Legs(g, sn, cs, bob, Color.FromArgb(70, 60, 76), LeatherD);
            P.Round(g, -0.54f, -1.8f + bob, 1.08f, 0.96f, 0.26f, c, d, Ol, 0.05f);
            P.Limb(g, -0.58f, -1.6f + bob, -0.76f, -1.1f + bob, 0.28f, c, Ol);
            P.Limb(g, 0.58f, -1.6f + bob, 0.76f, -1.1f + bob + (atk >= 0 ? -0.3f : 0f), 0.28f, c, Ol);
            Head(g, bob);
        }
    }
}
