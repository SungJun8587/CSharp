//***************************************************************************
// WorldRenderer.cs : 장애물(나무/바위), 엔티티(영웅/몬스터), 머리 위 표시를 그린다. 연출은 EffectRenderer 가 맡는다.
// 입력은 WorldModel(읽기 전용)과 ViewState 뿐이다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Protocol;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class WorldRenderer : IDisposable
    {
        private readonly Resources _res;
        private readonly SpriteCache _sprites = new SpriteCache(900);
        private readonly CreatureRenderer _creatures;
        private readonly Decor _decor;
        private readonly EffectRenderer _fx;
        private readonly GateRenderer _gates;
        private readonly MonsterFx _monFx;
        public readonly TreeRenderer Trees;
        public readonly DesertProps Props;
        public readonly BiomeProps Biome;
        private readonly PondRenderer _ponds;
        private readonly BiomePools _pools;
        private readonly List<Drawable> _sorted = new List<Drawable>(512);
        private static readonly Comparison<Drawable> ByY = delegate (Drawable a, Drawable b) { return a.Y.CompareTo(b.Y); };

        private struct Drawable
        {
            public float Y;
            public int Kind;        // 0 나무, 1 엔티티, 2 장식
            public int Index;
            public Entity Ent;
        }

        public WorldRenderer(Resources res)
        {
            _res = res;
            _monFx = new MonsterFx(res);
            _creatures = new CreatureRenderer(_sprites);
            _decor = new Decor(res);
            _fx = new EffectRenderer(res);
            _gates = new GateRenderer(res, _fx);
            Trees = new TreeRenderer(res);
            Props = new DesertProps(res);
            Biome = new BiomeProps(res);
            _ponds = new PondRenderer(res);
            _pools = new BiomePools(res);
        }

        public void Dispose() { _sprites.Dispose(); _fx.Dispose(); Trees.Dispose(); Props.Dispose(); Biome.Dispose(); _decor.Dispose(); }

        public int SpriteCacheCount { get { return _sprites.Count; } }

        public static Color HeroColor(uint tpl)
        {
            switch (tpl)
            {
                case 1: return Color.FromArgb(214, 70, 64);     // 전사: 붉은색
                case 2: return Color.FromArgb(70, 168, 96);     // 궁수: 녹색
                case 3: return Color.FromArgb(92, 110, 226);    // 마법사: 파란색
                case 4: return Color.FromArgb(52, 170, 176);    // 창기사: 청록색
                default: return Color.FromArgb(180, 160, 200);
            }
        }

        private static readonly Color[] MonsterPalette =
        {
            Color.FromArgb(104, 214, 120), Color.FromArgb(196, 120, 70), Color.FromArgb(150, 150, 160), Color.FromArgb(220, 190, 80),
            Color.FromArgb(180, 80, 160), Color.FromArgb(90, 170, 200), Color.FromArgb(200, 70, 70), Color.FromArgb(120, 100, 70),
            Color.FromArgb(230, 140, 60), Color.FromArgb(110, 70, 150),
        };
        public static Color MonsterColor(uint tpl) { return MonsterPalette[(int)((tpl == 0 ? 0 : tpl - 1) % (uint)MonsterPalette.Length)]; }

        // ------------------------------------------------------------------ 장면

        /// <summary>DrawScene 구간별 누적 시간(ms). 성능 확인용(--soak 이 평균을 찍는다).</summary>
        public readonly double[] SubMs = new double[8];
        public double[] EffectKindMs { get { return _fx.KindMs; } }
        public static readonly string[] SubNames = { "blocks", "fxGround", "collect+sort", "trees/props", "decor", "entities", "fxAir", "overheads" };
        private long _subT;
        private void Sub(int i) { long t = System.Diagnostics.Stopwatch.GetTimestamp(); SubMs[i] += (t - _subT) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; _subT = t; }

        public void DrawScene(Graphics g, ViewerSession s, ViewState v, long now)
        {
            _subT = System.Diagnostics.Stopwatch.GetTimestamp();
            WorldModel w = s.World;
            GameData d = s.Data;

            MapKind kind = ZoneTheme.Current;
            bool desert = kind == MapKind.Desert, biome = kind != MapKind.Forest && !desert;
            for (int i = 0; i < d.Blocks.Count; ++i)
                if (!d.Blocks[i].IsCircle)
                {
                    BlockRow rb = d.Blocks[i];
                    if (desert) Props.DrawRuin(g, v, rb, i);
                    else if (ZoneTheme.IsPool(ZoneTheme.CurrentZoneId, rb.X, rb.Y, rb.W, rb.H)) { if (biome) _pools.Draw(g, v, kind, rb, i, now); else _ponds.Draw(g, v, rb, i, now); }
                    else if (biome) Biome.DrawRuin(g, v, kind, rb, i);
                    else DrawRock(g, v, rb, i);
                }

            Sub(0);
            _gates.Draw(g, d, v, now);
            _fx.DrawGround(g, w, v, now);
            Sub(1);

            // 나무 + 엔티티를 Y 순서로 (아래쪽일수록 앞). 화면 밖은 넣지 않는다.
            _sorted.Clear();
            float margin = 6f;
            float wx0 = v.ToWorldX(-100) - margin, wx1 = v.ToWorldX(v.Width + 100) + margin, wy0 = v.ToWorldY(-100) - margin, wy1 = v.ToWorldY(v.Height + 200) + margin;
            for (int i = 0; i < d.Blocks.Count; ++i)
            {
                BlockRow b = d.Blocks[i];
                if (!b.IsCircle || b.X < wx0 || b.X > wx1 || b.Y < wy0 || b.Y > wy1) continue;
                Drawable dr = new Drawable(); dr.Y = b.Y + b.R * 0.6f; dr.Kind = 0; dr.Index = i; _sorted.Add(dr);
            }
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                Entity e = kv.Value;
                float x, y; e.PosAt(now, out x, out y);
                if (x < wx0 || x > wx1 || y < wy0 || y > wy1) continue;
                Drawable dr = new Drawable(); dr.Y = e.Dead ? y - 1000f : y; dr.Kind = 1; dr.Ent = e; _sorted.Add(dr);      // 시체는 항상 살아있는 것 뒤
            }
            _decor.Prepare(d, ZoneTheme.CurrentZoneId, kind, v.Settings.Quality);
            int df, dl; _decor.Range(wy0, wy1, out df, out dl);
            for (int i = df; i < dl; ++i)
            {
                DecorItem it = _decor.Item(i);
                if (it.X < wx0 || it.X > wx1) continue;
                Drawable dr = new Drawable(); dr.Y = it.Y; dr.Kind = 2; dr.Index = i; _sorted.Add(dr);
            }
            _sorted.Sort(ByY);
            Sub(2);

            for (int i = 0; i < _sorted.Count; ++i)
            {
                Drawable dr = _sorted[i];
                if (dr.Kind == 0)
                {
                    BlockRow b = d.Blocks[dr.Index];
                    if (desert) Props.Draw(g, v, ZoneTheme.PropOf(b.R), b.X, b.Y + b.R * 0.6f, b.R * 0.85f, Noise.Hash(dr.Index, 1, 4), false);
                    else if (biome) Biome.Draw(g, v, kind, ZoneTheme.SizeOf(b.R), b.X, b.Y + b.R * 0.6f, b.R * 0.85f, Noise.Hash(dr.Index, 1, 4));
                    else Trees.Draw(g, v, b.X, b.Y + b.R * 0.6f, b.R * 0.85f, Noise.Hash(dr.Index, 1, 4), false);
                    Sub(3);
                }
                else if (dr.Kind == 2) { _decor.Draw(g, v, _decor.Item(dr.Index)); Sub(4); }
                else { DrawEntity(g, s, dr.Ent, v, now); Sub(5); }
            }

            _fx.DrawAir(g, s, v, now);
            Sub(6);
            DrawOverheads(g, s, v, now);
            Sub(7);
        }

        // ------------------------------------------------------------------ 바위

        private void DrawRock(Graphics g, ViewState v, BlockRow b, int index)
        {
            float x0 = v.ToScreenX(b.X), y0 = v.ToScreenY(b.Y);
            float w = v.Px(b.W), h = v.Px(b.H);
            if (x0 > v.Width || y0 > v.Height || x0 + w < 0 || y0 + h < 0) return;

            g.FillRectangle(_res.Brush(60, 0, 0, 0), x0 + 3, y0 + 5, w, h);
            g.FillRectangle(_res.Brush(Color.FromArgb(96, 96, 100)), x0, y0, w, h);
            float cell = 3f;
            int nx = Math.Max(1, (int)Math.Ceiling(b.W / cell)), ny = Math.Max(1, (int)Math.Ceiling(b.H / cell));
            for (int j = 0; j < ny; ++j)
            {
                for (int i = 0; i < nx; ++i)
                {
                    uint hh = Noise.Hash(i, j, (uint)index + 21);
                    float cw = Math.Min(cell, b.W - i * cell), ch = Math.Min(cell, b.H - j * cell);
                    float rx = x0 + v.Px(i * cell), ry = y0 + v.Px(j * cell);
                    int shade = 108 + (int)(hh % 40);
                    g.FillEllipse(_res.Brush(Color.FromArgb(shade, shade, shade + 6)), rx, ry, v.Px(cw), v.Px(ch) + 2);
                    g.FillEllipse(_res.Brush(50, 255, 255, 255), rx + v.Px(cw) * 0.15f, ry + 1, v.Px(cw) * 0.5f, v.Px(ch) * 0.35f);
                    if ((hh & 3) == 0) g.FillEllipse(_res.Brush(150, 70, 112, 52), rx + v.Px(cw) * 0.2f, ry + v.Px(ch) * 0.1f, v.Px(cw) * 0.6f, v.Px(ch) * 0.3f);
                }
            }
            g.DrawRectangle(_res.Pen(160, 30, 30, 34, 2f), x0, y0, w, h);
        }

        // ------------------------------------------------------------------ 엔티티

        /// <summary>지면 아이템(드롭): 영웅/몬스터 몸 대신 바닥에서 살짝 떠서 반짝이는 보석 모양으로 그린다.</summary>
        private void DrawGroundItem(Graphics g, Entity e, ViewState v, long now, float sx, float sy)
        {
            float r = v.Px(0.55f);
            float bob = (float)Math.Sin(now / 260.0 + (e.Id & 15)) * v.Px(0.12f);
            float cy = sy - v.Px(0.55f) + bob;
            g.FillEllipse(_res.Brush(70, 0, 0, 0), sx - r * 1.1f, sy - r * 0.28f, r * 2.2f, r * 0.56f);
            _fx.Glow(g, Color.FromArgb(255, 235, 140), sx, cy, r * 2.4f, r * 2.4f, 0.55f + 0.2f * (float)Math.Sin(now / 200.0));
            PointF[] d = new PointF[] { new PointF(sx, cy - r), new PointF(sx + r * 0.8f, cy), new PointF(sx, cy + r), new PointF(sx - r * 0.8f, cy) };
            g.FillPolygon(_res.Brush(255, 255, 214, 90), d);
            g.DrawPolygon(_res.Pen(255, 140, 90, 20, 1.5f), d);
            if (e.Id == v.SelectedId) g.DrawEllipse(_res.Pen(210, 255, 232, 130, 2f), sx - r * 1.3f, sy - r * 0.5f, r * 2.6f, r);
        }

        private void DrawEntity(Graphics g, ViewerSession s, Entity e, ViewState v, long now)
        {
            float x, y; e.PosAt(now, out x, out y);
            float sx = v.ToScreenX(x), sy = v.ToScreenY(y);
            Look look = CreatureRenderer.LookOf(e);
            float pr = v.Px(1.0f);
            float reach = v.Px(Math.Max(look.H, look.HalfW)) + 40;
            if (sx < -reach || sy < -40 || sx > v.Width + reach || sy > v.Height + reach) return;

            if (e.IsItem) { DrawGroundItem(g, e, v, now, sx, sy); return; }

            bool self = e.Id == s.World.SelfId;
            bool selected = e.Id == v.SelectedId;
            float rx = v.Px(Math.Max(1.2f, look.HalfW * 0.75f));

            if (e.Dead)
            {
                // 몬스터 시체: 죽는 순간 한 번 튀고 맞은 방향으로 미끄러진 뒤, 잠시 누워 있다가 흐려지며 사라진다.
                float fade = 1f, dx = 0f, dy = 0f;
                if (e.IsMonster && e.DeadAtMs > 0)
                {
                    long age = now - e.DeadAtMs;
                    fade = AttackPose.DeathAlpha(e, age);
                    if (fade <= 0f) return;
                    if (age >= 0 && age < 320) dy -= v.Px(0.34f * (float)Math.Sin(Math.PI * age / 320.0));
                    float slide = AttackPose.DeathSlide(e, age);
                    dx = v.Px(e.HitDirX * slide); dy += v.Px(e.HitDirY * slide) * 0.6f;
                }
                g.FillEllipse(_res.Brush((int)(60 * fade), 0, 0, 0), sx - rx * 0.8f + dx, sy - rx * 0.17f, rx * 1.6f, rx * 0.4f);
                _creatures.Alpha = fade;
                _creatures.Draw(g, e, v, now, sx + dx, sy + dy, false);
                _creatures.Alpha = 1f;
                if (selected && fade > 0.3f) DrawSelectionRing(g, sx, sy, rx, now);
                return;
            }

            AssetSet bakedSet = e.IsMonster ? AssetSprites.Find(true, e.TemplateId) : null;
            BodyPose pose = AttackPose.Compute(e, now, bakedSet != null && bakedSet.AttackBody != null);
            float bx = sx + v.Px(pose.OffX), by = sy + v.Px(pose.OffY) * 0.6f - v.Px(pose.Lift);

            // 바닥 그림자: 몸이 밀린 자리(발 밑)에 두고, 뜰수록 작고 옅게. 겹친 타원 셋으로 가장자리를 풀고 발 밑은 진하게 눌러 접지감을 준다.
            float lift = pose.Lift + CreatureRenderer.HoverNow(e, now);
            DrawGroundShadow(g, bx, sy + v.Px(pose.OffY) * 0.6f, Math.Max(1.05f, look.HalfW * 0.62f), lift, v, look.H);
            DrawAttackDust(g, e, v, look, bx, sy + v.Px(pose.OffY) * 0.6f, now);
            HeroAura.Draw(g, _res, e, v, bx, sy + v.Px(pose.OffY) * 0.6f, now);
            CastLight.DrawBack(g, _fx, e, v, look, bx, sy + v.Px(pose.OffY) * 0.6f, now, ShadowStyle.Dark);
            _fx.DrawAura(g, s, e, v, now, sx, sy);
            if (e.IsBoss) DrawBossRune(g, e, v, look, sx, sy, now);
            if (e.IsBoss && BossFx.PhaseAura(e.BossPhase) > 0f)
                _fx.Glow(g, BossAccent(e, 0.45f), bx, sy - v.Px(look.H * 0.35f), v.Px(look.HalfW) * 1.5f, v.Px(look.H) * 0.6f, BossFx.PhaseAura(e.BossPhase) * (0.8f + 0.2f * (float)Math.Sin(now / 350.0)));
            if (v.HoverId == e.Id && !selected) g.DrawEllipse(_res.Pen(200, 255, 255, 255, 2f), sx - rx * 1.05f, sy - rx * 0.42f, rx * 2.1f, rx * 0.84f);
            if (selected) DrawSelectionRing(g, sx, sy, rx, now);
            if (self) g.DrawEllipse(_res.Pen(220, 255, 230, 90, 2f), sx - pr * 1.25f, sy - pr * 0.48f, pr * 2.5f, pr * 1.0f);

            bool hurt = now - e.HitFlashMs < AttackPose.FlashMs(e);
            float swipe = AttackPose.SwipeProgress(e, now);
            if (swipe >= 0f && !MonsterFx.Has(e)) DrawSwipe(g, e, v, look, sx, sy, swipe, true);
            _monFx.Draw(g, e, v, look, bx, by, now, false);
            // 보스 연출: 등장(빛기둥에서 내려옴) / 페이즈 변신(웅크렸다 부풀며 붉게)
            float entP = e.IsBoss ? BossFx.Entrance(now, e.AppearMs) : 1f;
            float phP = e.IsBoss ? BossFx.Phase(now, e.PhaseChangedMs) : -1f;
            float bossK = BossFx.PhaseScale(phP, e.BossPhase);
            bool bossFx = entP < 1f || phP >= 0f;
            if (bossFx)
            {
                if (entP < 1f)
                {
                    DrawBossPillar(g, v, bx, sy, look, entP, BossAccent(e, 0f));
                    by -= v.Px(BossFx.EntranceDrop(entP) * look.H);
                    _creatures.Alpha = BossFx.EntranceAlpha(entP);
                }
                System.Drawing.Drawing2D.GraphicsState gb = g.Save();
                g.TranslateTransform(bx, by);
                if (Math.Abs(pose.Lean) > 0.05f) g.RotateTransform(pose.Lean);
                g.ScaleTransform(pose.ScaleX * bossK, pose.ScaleY * bossK);
                _creatures.Draw(g, e, v, now, 0f, 0f, hurt || BossFx.PhaseTint(phP) > 0.5f);
                g.Restore(gb);
                if (phP >= 0f) DrawBossPhaseBurst(g, v, bx, sy, look, phP, e.BossPhase, BossAccent(e, 0.45f));
            }
            float crack = e.IsBoss ? BossFx.Crack(now, e.AppearMs) : -1f;
            if (crack >= 0f) DrawBossCracks(g, v, sx, sy, look, crack, BossAccent(e, 0f));
            else if (pose.IsIdentity) _creatures.Draw(g, e, v, now, bx, by, hurt);
            else
            {
                // 발 밑을 축으로 기울이고 눌러 늘인다 (그림은 캐시된 한 장이므로 변환만 바꾼다)
                System.Drawing.Drawing2D.GraphicsState gs = g.Save();
                g.TranslateTransform(bx, by);
                if (Math.Abs(pose.Lean) > 0.05f) g.RotateTransform(pose.Lean);
                g.ScaleTransform(pose.ScaleX, pose.ScaleY);
                _creatures.Draw(g, e, v, now, 0f, 0f, hurt);
                g.Restore(gs);
            }
            _creatures.Alpha = 1f;
            if (swipe >= 0f && !MonsterFx.Has(e)) DrawSwipe(g, e, v, look, sx, sy, swipe, false);
            _monFx.Draw(g, e, v, look, bx, sy + v.Px(pose.OffY) * 0.6f, now, true);
            if (AssetSprites.Find(false, e.TemplateId) != null) HeroWeapons.Draw(g, e, v, look, bx, by, now);        // 이미지 영웅은 무기 동작 프레임이 없어 따로 그린다
            CastLight.DrawFront(g, _fx, e, v, look, bx, by, now);
            float shot = AttackPose.ShotProgress(e, now);
            if (shot >= 0f) DrawShotFlash(g, e, v, look, sx, sy, shot);

            if (e.InvulnUntilMs > now)
            {
                int a = 90 + (int)(60 * Math.Sin(now / 90.0));
                g.DrawEllipse(_res.Pen(a, 120, 230, 255, 3f), sx - pr * 1.3f, sy - pr * 2.9f, pr * 2.6f, pr * 3.2f);
            }
            long phaseAge = now - e.PhaseChangedMs;
            if (e.IsBoss && phaseAge >= 0 && phaseAge < 900)
            {
                float p = phaseAge / 900f;
                float rr = v.Px(look.HalfW) * (0.6f + 1.6f * p);
                g.DrawEllipse(_res.Pen((int)(230 * (1f - p)), 255, 240, 200, 4f), sx - rr, sy - rr * 0.45f, rr * 2, rr * 0.9f);
            }
        }

        /// <summary>보스 등장: 하늘에서 내려오는 빛기둥과 바닥 충격 고리.</summary>
        /// <summary>보스 고유색. 새 몬스터 보스는 종류별 색, 그 밖의 보스는 기본 금색. warm 이 0 보다 크면 그 만큼 붉은 쪽으로 섞는다(변신/격노 느낌).</summary>
        private static Color BossAccent(Entity e, float warm)
        {
            int r, gg, b; BossFx.Theme(e.TemplateId, out r, out gg, out b);
            if (!BossFx.HasTheme(e.TemplateId)) { r = 255; gg = (int)(215 - 145 * warm); b = (int)(130 - 80 * warm); }
            return Color.FromArgb(255, r, gg, b);
        }

        /// <summary>보스 발 밑의 느리게 도는 마법진: 이중 타원 + 점선 고리. 페이즈가 오를수록 선명하다.</summary>
        private void DrawBossRune(Graphics g, Entity e, ViewState v, Look look, float sx, float sy, long now)
        {
            Color c = BossAccent(e, 0f);
            float rr = v.Px(Math.Max(1.6f, look.HalfW * 1.25f));
            int a = 120 + 24 * Math.Min(4, e.BossPhase);
            a = (int)(a * (0.85f + 0.15f * Math.Sin(now / 420.0)));
            g.DrawEllipse(_res.Pen(a, c.R, c.G, c.B, 3f), sx - rr, sy - rr * 0.36f, rr * 2f, rr * 0.72f);
            _fx.Glow(g, c, sx, sy, rr * 0.95f, rr * 0.36f, 0.16f);
            float r2 = rr * 0.78f;
            using (Pen p = new Pen(Color.FromArgb(Math.Min(255, a + 30), c), 2f))
            {
                p.DashPattern = new float[] { 3f, 3f };
                p.DashOffset = (now / 60) % 6;
                g.DrawEllipse(p, sx - r2, sy - r2 * 0.36f, r2 * 2f, r2 * 0.72f);
            }
            // 안쪽 오각 별(마법진 문양): 반대 방향으로 천천히 돈다
            float r3 = r2 * 0.62f; double sa = -now / 3400.0;
            PointF[] star = new PointF[5];
            for (int k = 0; k < 5; ++k) { double an = sa + k * Math.PI * 4 / 5; star[k] = new PointF(sx + (float)Math.Cos(an) * r3, sy + (float)Math.Sin(an) * r3 * 0.36f); }
            g.DrawPolygon(_res.Pen(Math.Min(255, a), c.R, c.G, c.B, 1.6f), star);
            // 안쪽 다섯 점(마법진 표식): 천천히 돈다
            double ang0 = now / 2600.0;
            for (int k = 0; k < 5; ++k)
            {
                double an = ang0 + k * Math.PI * 2 / 5;
                float px = sx + (float)Math.Cos(an) * r2 * 0.92f, py = sy + (float)Math.Sin(an) * r2 * 0.92f * 0.36f;
                g.FillEllipse(_res.Brush(Math.Min(255, a + 60), c.R, c.G, c.B), px - 2.5f, py - 1.5f, 5f, 3f);
            }
        }

        /// <summary>착지 뒤 바닥 균열: 발 밑에서 바깥으로 뻗는 갈라진 선과 안쪽에서 새어 나오는 빛. 서서히 옅어진다.</summary>
        private void DrawBossCracks(Graphics g, ViewState v, float sx, float sy, Look look, float p, Color c)
        {
            float grow = Math.Min(1f, p * 4f), fade = p < 0.45f ? 1f : 1f - (p - 0.45f) / 0.55f;
            float R = v.Px(look.HalfW) * (1.1f + 1.6f * grow);
            Random rnd = new Random(7);
            for (int k = 0; k < 9; ++k)
            {
                double an = k * Math.PI * 2 / 9 + rnd.NextDouble() * 0.5;
                float len = R * (0.55f + (float)rnd.NextDouble() * 0.45f);
                float x0 = sx + (float)Math.Cos(an) * R * 0.25f, y0 = sy + (float)Math.Sin(an) * R * 0.25f * 0.4f;
                float mx = sx + (float)Math.Cos(an + 0.18) * len * 0.6f, my = sy + (float)Math.Sin(an + 0.18) * len * 0.6f * 0.4f;
                float x1 = sx + (float)Math.Cos(an - 0.1) * len, y1 = sy + (float)Math.Sin(an - 0.1) * len * 0.4f;
                g.DrawLines(_res.Pen((int)(210 * fade), 20, 14, 10, 3f), new PointF[] { new PointF(x0, y0), new PointF(mx, my), new PointF(x1, y1) });
                g.DrawLines(_res.Pen((int)(170 * fade), c.R, c.G, c.B, 1.4f), new PointF[] { new PointF(x0, y0), new PointF(mx, my), new PointF(x1, y1) });
            }
            _fx.Glow(g, c, sx, sy, R * 0.9f, R * 0.35f, 0.35f * fade);
        }

        private void DrawBossPillar(Graphics g, ViewState v, float x, float footY, Look look, float p, Color accent)
        {
            float h = v.Px(look.H * 2.6f), w = v.Px(look.HalfW) * (1.1f - 0.5f * p);
            float a = p < 0.7f ? 1f : 1f - (p - 0.7f) / 0.3f;
            _fx.Beam(g, accent, x, footY, footY - h, w * 1.1f, a);
            _fx.Beam(g, Color.FromArgb(255, 252, 240), x, footY, footY - h, w * 0.4f, a);
            if (p > 0.6f)
            {
                float q = (p - 0.6f) / 0.4f, rr = v.Px(look.HalfW) * (0.5f + 2.2f * q);
                g.DrawEllipse(_res.Pen((int)(220 * (1f - q)), accent.R, accent.G, accent.B, 4f), x - rr, footY - rr * 0.22f, rr * 2, rr * 0.44f);
            }
        }

        /// <summary>보스 페이즈 변신: 붉은 섬광과 퍼지는 충격 고리(페이즈가 높을수록 겹이 늘어난다).</summary>
        private void DrawBossPhaseBurst(Graphics g, ViewState v, float x, float footY, Look look, float p, int phase, Color accent)
        {
            float t = BossFx.PhaseTint(p);
            _fx.Glow(g, accent, x, footY - v.Px(look.H * 0.5f), v.Px(look.HalfW) * 1.7f, v.Px(look.H) * 0.7f, 0.55f * t);
            int rings = Math.Min(3, Math.Max(1, phase));
            for (int k = 0; k < rings; ++k)
            {
                float q = p - k * 0.12f;
                if (q <= 0f || q >= 1f) continue;
                float rr = v.Px(look.HalfW) * (0.6f + 2.4f * q);
                g.DrawEllipse(_res.Pen((int)(230 * (1f - q)), accent.R, accent.G, accent.B, 4f), x - rr, footY - rr * 0.22f, rr * 2, rr * 0.44f);
            }
        }

        /// <summary>밤 광원: 밤 색조를 깐 뒤에 덧그린다. 수정/용암 바위는 스스로 빛나고, 내 영웅 둘레는 따뜻하게 밝아져 어두워도 읽힌다.</summary>
        public void DrawLights(Graphics g, ViewerSession s, ViewState v, long now, float night)
        {
            if (night <= 0.02f) return;
            float wy0 = v.ToWorldY(-100) - 6f, wy1 = v.ToWorldY(v.Height + 200) + 6f, wx0 = v.ToWorldX(-100) - 6f, wx1 = v.ToWorldX(v.Width + 100) + 6f;
            int df, dl; _decor.Range(wy0, wy1, out df, out dl);
            for (int i = df; i < dl; ++i)
            {
                DecorItem it = _decor.Item(i);
                if (it.X < wx0 || it.X > wx1) continue;
                Color c; float r;
                if (it.Name == "crystal_blue") { c = Color.FromArgb(110, 170, 255); r = 1.9f; }
                else if (it.Name == "crystal_purple") { c = Color.FromArgb(190, 120, 255); r = 1.9f; }
                else if (it.Name == "rock_lava") { c = Color.FromArgb(255, 120, 40); r = 2.2f; }
                else if (it.Name == "mushrooms") { c = Color.FromArgb(255, 170, 120); r = 0.9f; }
                else continue;
                float flick = 0.85f + 0.15f * (float)Math.Sin(now / 260.0 + it.X * 3f);
                _fx.Glow(g, c, v.ToScreenX(it.X), v.ToScreenY(it.Y) - v.Px(it.Width * 0.25f), v.Px(r) * it.Width * 0.9f, v.Px(r) * it.Width * 0.65f, Math.Min(1f, night * 0.9f) * flick);
            }
            Entity me = s.World.Self;
            if (me != null && !me.Dead)
            {
                float mx, my; me.PosAt(now, out mx, out my);
                float sx = v.ToScreenX(mx), sy = v.ToScreenY(my) - v.Px(0.8f);
                _fx.Glow(g, Color.FromArgb(255, 214, 150), sx, sy, v.Px(7.5f), v.Px(5.5f), 0.5f * night);
            }
        }

        /// <summary>발 밑 접지 그림자 + 키가 큰 몸이 빛 반대쪽(오른쪽 아래)으로 드리우는 긴 그림자. 스프라이트의 림 라이트(왼쪽 위)와 같은 방향이다.</summary>
        private void DrawGroundShadow(Graphics g, float x, float y, float half, float lift, ViewState v, float height)
        {
            float k = 1f / (1f + lift * 0.9f);               // 높이 뜰수록 작아짐
            float a = 0.55f + 0.45f * k;                      // 옅어짐
            float sw = v.Px(half) * k;
            y -= sw * 0.06f;                                  // 발 밑이 타원 한가운데 오도록(발이 그림자 위에 얹힘)
            int r, gg, b; float mul, lm; ShadowStyle.Lit(out r, out gg, out b, out mul, out lm);
            a *= mul;
            float cast = v.Px(Math.Min(3.4f, 0.25f + height * 0.2f)) * k * lm;                // 드리운 그림자 길이(키에 비례)
            if (cast > 2f)
            {
                g.FillEllipse(_res.Brush((int)(Math.Min(255f, 26f * a)), r, gg, b), x - sw * 0.9f + cast * 0.15f, y - sw * 0.34f + cast * 0.04f, sw * 1.9f + cast, sw * 0.68f);
                g.FillEllipse(_res.Brush((int)(Math.Min(255f, 38f * a)), r, gg, b), x - sw * 0.7f + cast * 0.1f, y - sw * 0.26f, sw * 1.4f + cast * 0.6f, sw * 0.52f);
            }
            g.FillEllipse(_res.Brush((int)(Math.Min(255f, 34f * a)), r, gg, b), x - sw * 1.45f, y - sw * 0.5f, sw * 2.9f, sw * 1.0f);
            g.FillEllipse(_res.Brush((int)(Math.Min(255f, 54f * a)), r, gg, b), x - sw * 1.1f, y - sw * 0.37f, sw * 2.2f, sw * 0.74f);
            g.FillEllipse(_res.Brush((int)(Math.Min(255f, 92f * a)), 0, 0, 0), x - sw * 0.72f, y - sw * 0.24f, sw * 1.44f, sw * 0.48f);
        }

        /// <summary>
        /// 몬스터 근접 공격이 땅을 때리는 순간의 먼지와 충격 고리. 달려들기/뛰어내리기는 착지에, 내려찍기는 타격 순간에 터진다.
        /// </summary>
        private void DrawAttackDust(Graphics g, Entity e, ViewState v, Look look, float x, float y, long now)
        {
            if (!e.IsMonster || e.Dead || e.AttackKind == 2) return;
            AttackStyle st = AttackPose.StyleOf(e);
            int total = AttackPose.DurationMs(e);
            float t = now - e.AttackAnimMs;
            if (t < 0f || t >= total) return;
            float t0 = st == AttackStyle.Hop ? total * 0.5f : (st == AttackStyle.Slam ? AttackPose.StrikeMs : AttackPose.StrikeMs + AttackPose.HoldMs + 100f);
            float k = (t - t0) / 260f;
            if (k < 0f || k >= 1f) return;
            float mag = e.IsBoss ? 1.7f : (st == AttackStyle.Slam ? 1.3f : 1f);
            float fl = (float)Math.Sqrt(e.FaceX * e.FaceX + e.FaceY * e.FaceY);
            float fx = fl > 0.001f ? e.FaceX / fl : 1f, fy = fl > 0.001f ? e.FaceY / fl : 0f;
            float px = x + fx * v.Px(0.5f * mag), py = y + fy * v.Px(0.5f * mag) * 0.6f;
            int al = (int)(150 * (1f - k) * (1f - k));
            for (int i = 0; i < 6; ++i)
            {
                double th = i * Math.PI / 3 + (e.Id & 7);
                float d = v.Px(0.35f * mag) + v.Px(1.1f * mag) * k;
                float r = v.Px(0.22f * mag) * (1f + 1.6f * k);
                float cx = px + (float)Math.Cos(th) * d, cy = py + (float)Math.Sin(th) * d * 0.45f - v.Px(0.25f * mag) * k;
                g.FillEllipse(_res.Brush(al, 176, 160, 130), cx - r, cy - r * 0.7f, r * 2f, r * 1.4f);
            }
            if (st == AttackStyle.Slam)
            {
                float rr = v.Px((0.6f + 3.0f * k) * mag);
                g.DrawEllipse(_res.Pen(al + 40, 230, 210, 170, Math.Max(1f, v.Px(0.18f * mag) * (1f - k))), px - rr, py - rr * 0.4f, rr * 2f, rr * 0.8f);
            }
        }

        /// <summary>
        /// 근접 공격의 휘두름 궤적. 몸 뒤(back)에서 번지는 잔광과 몸 앞에서 번쩍이는 날로 나눠 그린다.
        /// 영웅은 직업별로 다르다: 전사는 크게 내려치는 검광, 창기사는 앞으로 뻗는 직선 찌르기. 이미지 영웅은 무기 프레임이 없어 이 궤적이 무기 동작을 대신한다.
        /// </summary>
        private void DrawSwipe(Graphics g, Entity e, ViewState v, Look look, float sx, float sy, float prog, bool back)
        {
            float fl = (float)Math.Sqrt(e.FaceX * e.FaceX + e.FaceY * e.FaceY);
            float fx = fl > 0.001f ? e.FaceX / fl : 1f, fy = fl > 0.001f ? e.FaceY / fl : 0f;
            float ang = (float)(Math.Atan2(fy * 0.6f, fx) * 180.0 / Math.PI);
            float reach = v.Px(look.HalfW * 0.9f + (e.IsBoss ? 2.2f : 1.1f));
            float cy = sy - v.Px(look.H * 0.42f);
            float ex = sx + fx * v.Px(0.5f), ey = cy + fy * v.Px(0.5f) * 0.6f;
            if (e.IsMonster)
            {
                Color mc = e.IsBoss ? Color.FromArgb(255, 80, 60) : Color.FromArgb(255, 130, 100);
                ArcSlash(g, ex, ey, reach, ang - 65f, 130f, prog, mc, v.Px(e.IsBoss ? 0.55f : 0.34f), back);
                return;
            }
            float r = v.Px(1.9f), w = v.Px(0.36f);
            if (e.TemplateId == 1)
                ArcSlash(g, ex, ey - v.Px(0.5f), r * 1.15f, ang - 95f, 160f, prog, Color.FromArgb(200, 225, 255), w * 1.25f, back);
            else if (e.TemplateId == 4)
                Thrust(g, ex, ey, ang, v.Px(2.9f), prog, Color.FromArgb(170, 240, 235), w * 0.8f, back);
            else ArcSlash(g, ex, ey, r, ang - 65f, 130f, prog, Color.FromArgb(255, 240, 190), w, back);
        }

        /// <summary>창 찌르기: 몸에서 ang 방향으로 곧게 뻗는 잔광 한 줄.</summary>
        private void Thrust(Graphics g, float ex, float ey, float ang, float reach, float prog, Color c, float w, bool back)
        {
            int a = (int)(230 * (1f - prog) * (1f - prog)) & ~15;
            if (a < 16) return;
            double r = ang * Math.PI / 180.0;
            float dx = (float)Math.Cos(r), dy = (float)Math.Sin(r) * 0.8f;
            float k = AttackPose.EaseOut(prog * 2f > 1f ? 1f : prog * 2f);
            float x0 = ex + dx * reach * 0.25f * k, y0 = ey + dy * reach * 0.25f * k, x1 = ex + dx * reach * (0.3f + 0.7f * k), y1 = ey + dy * reach * (0.3f + 0.7f * k);
            if (back) g.DrawLine(_res.Pen(a / 3, c.R, c.G, c.B, w * 2.6f), x0, y0, x1, y1);
            else
            {
                g.DrawLine(_res.Pen(a, c.R, c.G, c.B, w), x0, y0, x1, y1);
                g.DrawLine(_res.Pen(a, 255, 255, 255, w * 0.35f), x0 + (x1 - x0) * 0.4f, y0 + (y1 - y0) * 0.4f, x1, y1);
            }
        }

        /// <summary>호 모양 베기 한 번. prog 0~1 동안 start 에서 sweep 도까지 뻗으며 사라진다.</summary>
        private void ArcSlash(Graphics g, float ex, float ey, float reach, float start, float sweep, float prog, Color c, float w, bool back)
        {
            float sweepEnd = sweep * AttackPose.EaseOut(prog * 1.6f);
            int a = (int)(230 * (1f - prog) * (1f - prog)) & ~15;
            if (a < 16 || Math.Abs(sweepEnd) < 4f) return;
            RectangleF r = new RectangleF(ex - reach, ey - reach * 0.8f, reach * 2f, reach * 1.6f);
            if (back) g.DrawArc(_res.Pen(a / 3, c.R, c.G, c.B, w * 2.2f), r, start, sweepEnd);
            else
            {
                g.DrawArc(_res.Pen(a, c.R, c.G, c.B, w), r, start + sweepEnd * 0.35f, sweepEnd * 0.65f);
                g.DrawArc(_res.Pen(a, 255, 255, 255, w * 0.35f), r, start + sweepEnd * 0.55f, sweepEnd * 0.45f);
            }
        }

        /// <summary>영웅의 원거리 발사 섬광: 궁수는 시위를 놓는 별빛, 마법사는 지팡이 구슬이 터지는 빛.</summary>
        private void DrawShotFlash(Graphics g, Entity e, ViewState v, Look look, float sx, float sy, float prog)
        {
            float fl = (float)Math.Sqrt(e.FaceX * e.FaceX + e.FaceY * e.FaceY);
            float fx = fl > 0.001f ? e.FaceX / fl : 1f, fy = fl > 0.001f ? e.FaceY / fl : 0f;
            float px = sx + fx * v.Px(1.1f), py = sy - v.Px(look.H * 0.5f) + fy * v.Px(1.1f) * 0.6f;
            int a = (int)(240 * (1f - prog)) & ~15;
            if (a < 16) return;
            float k = AttackPose.EaseOut(prog);
            if (e.TemplateId == 3)
            {
                float rr = v.Px(0.45f + 0.9f * k);
                g.FillEllipse(_res.Brush(a / 4, 255, 150, 60), px - rr * 1.8f, py - rr * 1.8f, rr * 3.6f, rr * 3.6f);
                g.FillEllipse(_res.Brush(a / 2, 255, 200, 100), px - rr, py - rr, rr * 2f, rr * 2f);
                g.FillEllipse(_res.Brush(a, 255, 250, 210), px - rr * 0.45f, py - rr * 0.45f, rr * 0.9f, rr * 0.9f);
            }
            else
            {
                float len = v.Px(0.5f + 1.1f * k);
                Pen pn = _res.Pen(a, 220, 255, 190, v.Px(0.1f));
                for (int i = 0; i < 6; ++i)
                {
                    double th = i * Math.PI / 3 + 0.4;
                    g.DrawLine(pn, px, py, px + (float)Math.Cos(th) * len, py + (float)Math.Sin(th) * len * 0.8f);
                }
                float rr = v.Px(0.3f * (1f - k) + 0.1f);
                g.FillEllipse(_res.Brush(a, 255, 255, 235), px - rr, py - rr, rr * 2f, rr * 2f);
            }
        }

        private void DrawSelectionRing(Graphics g, float sx, float sy, float rx, long now)
        {
            float pulse = 1f + 0.06f * (float)Math.Sin(now / 150.0);
            Pen p = _res.DashPen(Color.FromArgb(230, 255, 70, 70), 2.5f, (now / 60) % 10);
            g.DrawEllipse(p, sx - rx * 1.15f * pulse, sy - rx * 0.48f * pulse, rx * 2.3f * pulse, rx * 0.96f * pulse);
        }

        // ------------------------------------------------------------------ 머리 위 표시(이름/체력/시전/상태)

        /// <summary>머리 위 표시 하나의 화면 위치. 겹치는 표시를 위로 밀어내기 위해 먼저 모아서 자리를 정한다.</summary>
        private struct Plate
        {
            public Entity Ent;
            public float Sx, Sy, HalfW, H;
            public int Prio;        // 클수록 먼저 자리를 차지한다 (선택 > 보스 > 나 > 산 것 > 시체)
            public bool Hide;
        }
        private readonly List<Plate> _plates = new List<Plate>(128);
        private readonly List<int> _plateOrder = new List<int>(128);
        private const float PlateStep = 17f;        // 겹칠 때 한 번에 올리는 높이(px)

        /// <summary>
        /// 머리 위 표시(이름+체력바)가 서로 겹치지 않게 위로 밀어 올린다. 우선순위가 높은 것이 제자리를 지키고,
        /// 시체의 표시는 자리가 없으면 숨긴다. 몬스터가 한곳에 몰렸을 때 이름이 뭉개져 읽을 수 없던 문제를 줄인다.
        /// </summary>
        private void ResolvePlates()
        {
            int n = _plates.Count;
            _plateOrder.Clear();
            for (int i = 0; i < n; ++i) _plateOrder.Add(i);
            List<Plate> pl = _plates;
            _plateOrder.Sort(delegate (int a, int b)
            {
                int c = pl[b].Prio.CompareTo(pl[a].Prio);
                return c != 0 ? c : pl[b].Sy.CompareTo(pl[a].Sy);       // 같으면 화면 아래(가까운 것)부터
            });
            for (int oi = 0; oi < n; ++oi)
            {
                int i = _plateOrder[oi];
                Plate p = pl[i];
                int shifts = 0;
                for (; shifts < 6; ++shifts)
                {
                    bool hit = false;
                    for (int oj = 0; oj < oi; ++oj)
                    {
                        Plate q = pl[_plateOrder[oj]];
                        if (q.Hide) continue;
                        if (Math.Abs(p.Sx - q.Sx) < p.HalfW + q.HalfW && Math.Abs(p.Sy - q.Sy) < (p.H + q.H) * 0.5f) { hit = true; break; }
                    }
                    if (!hit) break;
                    p.Sy -= PlateStep;
                }
                if (shifts >= 6 && p.Prio == 0) p.Hide = true;
                pl[i] = p;
            }
        }

        private void DrawOverheads(Graphics g, ViewerSession s, ViewState v, long now)
        {
            WorldModel w = s.World;
            _plates.Clear();
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                Entity e = kv.Value;
                float x, y; e.PosAt(now, out x, out y);
                Look look = CreatureRenderer.LookOf(e);
                float sx = v.ToScreenX(x), sy = v.ToScreenY(y) - v.Px(look.H + 0.35f);
                if (sx < -100 || sy < -60 || sx > v.Width + 100 || sy > v.Height + 200) continue;
                bool self0 = e.Id == w.SelfId, selected0 = e.Id == v.SelectedId;
                if (e.Dead && (e.IsMonster ? now - e.DeadAtMs > AttackPose.DeathGoneMs(e) : now - e.DeadAtMs > 2500) && !selected0) continue;

                float bw = e.IsBoss ? Math.Max(80f, v.Px(5f)) : Math.Max(34f, v.Px(2.6f));
                Plate pt = new Plate();
                pt.Ent = e; pt.Sx = sx; pt.Sy = sy;
                pt.HalfW = Math.Max(bw, 56f) * 0.5f;
                pt.H = selected0 || self0 ? 36f : 26f;
                pt.Prio = selected0 ? 4 : (e.IsBoss ? 3 : (self0 ? 2 : (e.Dead ? 0 : 1)));
                _plates.Add(pt);
            }
            ResolvePlates();

            for (int pi = 0; pi < _plates.Count; ++pi)
            {
                Plate plate = _plates[pi];
                if (plate.Hide) continue;
                Entity e = plate.Ent;
                float sx = plate.Sx, sy = plate.Sy;

                bool self = e.Id == w.SelfId;
                bool selected = e.Id == v.SelectedId;
                string name = w.NameOf(e);
                Color nameColor = e.IsItem ? Color.FromArgb(255, 240, 150) : self ? Color.FromArgb(255, 235, 120) : (e.IsMonster ? Color.FromArgb(255, 200, 200) : Color.FromArgb(190, 225, 255));
                if (e.IsBoss) { nameColor = Color.FromArgb(255, 214, 90); name = "★ " + name; }
                if (selected) nameColor = Color.White;
                if (e.Dead) nameColor = Color.FromArgb(160, 170, 170, 170);

                float barW = e.IsBoss ? Math.Max(80f, v.Px(5f)) : (self ? Math.Max(64f, v.Px(3.6f)) : Math.Max(34f, v.Px(2.6f))), barH = e.IsBoss ? 8f : (self ? 12f : 7f);
                _res.OutlinedText(g, name, e.IsBoss ? _res.Bold : _res.Small, nameColor, sx, sy - 12, _res.Center);

                if (e.MaxHp > 0 && !e.Dead && !e.IsBoss && !e.IsItem)       // 보스는 화면 위쪽의 큰 체력바가 대신한다
                {
                    float ratio = Math.Max(0f, Math.Min(1f, e.Hp / (float)e.MaxHp));
                    float ghost = Math.Max(ratio, Math.Min(1f, e.GhostHp / (float)e.MaxHp));
                    Color ptop, pbot;
                    if (e.IsMonster) Ui.FoeColors(out ptop, out pbot); else Ui.HpColors(ratio, out ptop, out pbot);
                    if (selected)
                        using (GraphicsPath sel = P.RoundPath(sx - barW / 2 - 3, sy - 4, barW + 6, barH + 6, 6))
                            g.DrawPath(_res.Pen(210, 255, 232, 130, 2f), sel);
                    if (self)
                    {
                        Ui.HeroBar(g, _res, sx - barW / 2, sy - 1, barW, barH, ratio, ghost, ptop, pbot, now);
                        _res.OutlinedText(g, e.Hp + " / " + e.MaxHp, _res.Small, Color.White, sx, sy - 1 + barH / 2f - 7f, _res.Center);     // 숫자는 막대 안 가운데
                    }
                    else
                    {
                        Ui.HpBar(g, _res, sx - barW / 2, sy - 1, barW, barH, ratio, ghost, ptop, pbot, true);
                        if (selected) _res.OutlinedText(g, e.Hp + "/" + e.MaxHp, _res.Small, Color.White, sx, sy + barH + 4, _res.Center);
                    }
                }

                float iconY = sy + (e.IsBoss ? 4 : (selected ? barH + 20 : (self ? barH + 6 : barH + 2)));

                if (e.Casting && e.CastTimeMs > 0 && !e.Dead && e.CastSkillId >= WorldModel.BasicAttackSkillIdBase)
                {
                    // 기본 공격 준비: 머리 위에 점점 커지고 붉어지는 느낌표. 보스는 더 크다. 이 사이 사거리 밖으로 피하면 빗나간다.
                    float wp = Math.Max(0f, Math.Min(1f, (now - e.CastStartMs) / (float)e.CastTimeMs));
                    float k = e.IsBoss ? 1.5f : 1f;
                    int a = 140 + (int)(115 * wp);
                    Color wc = Color.FromArgb(Math.Min(255, a), 255, (int)(210 - 150 * wp), (int)(80 - 40 * wp));
                    GraphicsState ws = g.Save();
                    g.TranslateTransform(sx, iconY + 4f);
                    g.ScaleTransform(k * (0.8f + 0.5f * wp), k * (0.8f + 0.5f * wp));
                    _res.OutlinedText(g, "!", _res.Big, wc, 0f, 0f, _res.Center);
                    g.Restore(ws);
                    iconY += 20f * k;
                }
                else if (e.Casting && e.CastTimeMs > 0 && !e.Dead)
                {
                    float p = Math.Max(0f, Math.Min(1f, (now - e.CastStartMs) / (float)e.CastTimeMs));
                    g.FillRectangle(_res.Brush(200, 20, 20, 20), sx - barW / 2 - 1, iconY - 1, barW + 2, 8);
                    g.FillRectangle(_res.Brush(e.IsBoss ? Color.FromArgb(255, 110, 60) : Color.FromArgb(190, 120, 255)), sx - barW / 2, iconY, barW * p, 6);
                    _res.OutlinedText(g, s.Data.SkillName(e.CastSkillId), _res.Small, Color.FromArgb(230, 200, 255), sx, iconY + 8, _res.Center);
                    iconY += 22;
                }

                if (e.Statuses.Count > 0 && !e.Dead) DrawStatusIcons(g, s, e, sx, iconY, now);
            }
        }

        /// <summary>보스 체력바 위에 페이즈 경계 눈금을 그린다.</summary>
        public static void DrawPhaseTicks(Graphics g, Entity e, float x, float y, float w, float h)
        {
            if (e.Boss == null) return;
            using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), 1f))
                for (int i = 0; i < e.Boss.Phases.Count; ++i)
                {
                    uint pct = e.Boss.Phases[i].HpBelowPercent;
                    if (pct >= 100 || pct == 0) continue;
                    float tx = x + w * pct / 100f;
                    g.DrawLine(p, tx, y, tx, y + h);
                }
        }

        private void DrawStatusIcons(Graphics g, ViewerSession s, Entity e, float sx, float iconY, long now)
        {
            int n = e.Statuses.Count;
            float ix = sx - (n * 17f) / 2f;
            foreach (KeyValuePair<uint, StatusInstance> st in e.Statuses)
            {
                StatusRow row = s.Data.Status(st.Key);
                bool dot = row != null && row.IsDot;
                bool debuff = row != null && !row.IsDot && (row.AttackPercent < 0 || row.DefensePercent < 0 || row.SpeedPercent < 0);
                Color c = dot ? Color.FromArgb(230, 110, 40) : (debuff ? Color.FromArgb(150, 90, 190) : Color.FromArgb(70, 170, 230));
                long left = st.Value.ExpireMs - now;
                int a = left < 1500 && ((now / 150) % 2 == 0) ? 120 : 235;
                g.FillRectangle(_res.Brush(Color.FromArgb(a, c)), ix, iconY, 15, 15);
                if (row != null && row.DurationMs > 0 && left > 0)
                {
                    float f = Math.Min(1f, left / (float)row.DurationMs);
                    g.FillRectangle(_res.Brush(110, 0, 0, 0), ix, iconY, 15, 15 * (1f - f));       // 남은 시간만큼 아래에서 위로 차 있다
                }
                g.DrawRectangle(_res.Pen(220, 20, 20, 20, 1f), ix, iconY, 15, 15);
                string nm = s.Data.StatusName(st.Key);
                g.DrawString(nm.Length > 0 ? nm.Substring(0, 1) : "?", _res.Small, _res.Brush(Color.White), ix + 1, iconY + 1);
                ix += 17f;
            }
        }

        public static Color HpColor(float ratio)
        {
            if (ratio > 0.5f) return Color.FromArgb((int)(220 - (ratio - 0.5f) * 2 * 160), 200, 60);
            return Color.FromArgb(220, (int)(ratio * 2 * 190), 50);
        }
    }

    internal static class WorldRendererColors
    {
        public static Color Scale(Color c, float f) { return Col.Scale(c, f); }
        public static Color Flash(Color c, bool hurt)
        {
            if (!hurt) return c;
            return Color.FromArgb(c.A, Math.Min(255, c.R + 110), Math.Min(255, c.G + 110), Math.Min(255, c.B + 110));
        }
    }
}
