//***************************************************************************
// MonsterFx.cs : 몬스터 공격의 타격 연출. 몸 그림은 휘어 구운 공격 자세(AssetSprites attackFrames)가 맡고,
//                여기서는 몬스터마다 다른 궤적/충격/파편을 타격 순간에 맞춰 얹는다.
//                입력은 엔티티와 시각뿐이고 상태를 갖지 않는다(같은 시각이면 같은 그림).
//***************************************************************************
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class MonsterFx
    {
        private readonly Resources _res;
        public MonsterFx(Resources res) { _res = res; }

        /// <summary>이 몬스터의 타격 연출 이름(sprites.json 의 "fx": slime, spores, claws, sonic, slam, blade). 없으면 null.</summary>
        public static string FxOf(Entity e)
        {
            AssetSet a = e.IsMonster ? AssetSprites.Find(true, e.TemplateId) : null;
            return a == null ? null : a.Fx;
        }

        /// <summary>이 몬스터가 전용 타격 연출을 가졌는지(가지면 범용 호 모양 베기는 그리지 않는다).</summary>
        public static bool Has(Entity e)
        {
            if (!e.IsMonster || e.AttackKind == 2) return false;
            return FxOf(e) != null;
        }

        /// <summary>타격 순간(공격 시작 후 ms). 뛰는 몬스터는 착지, 나머지는 StrikeMs.</summary>
        private static float HitMs(Entity e)
        {
            return AttackPose.StyleOf(e) == AttackStyle.Hop ? AttackPose.DurationMs(e) * 0.62f : AttackPose.StrikeMs;
        }

        /// <summary>front=false 는 몸 뒤(속도선/잔상), true 는 몸 위(궤적/충격).</summary>
        public void Draw(IGfx g, Entity e, ViewState v, Look look, float sx, float sy, long now, bool front)
        {
            if (!Has(e) || e.Dead) return;
            float t = now - e.AttackAnimMs, total = AttackPose.DurationMs(e);
            if (t < 0f || t >= total + 200f) return;
            float fl = (float)Math.Sqrt(e.FaceX * e.FaceX + e.FaceY * e.FaceY);
            float fx = fl > 0.001f ? e.FaceX / fl : 1f, fy = fl > 0.001f ? e.FaceY / fl : 0f;
            float sc = e.IsBoss ? 1.6f : 1f;
            float cy = sy - v.Px(look.H * 0.45f);
            float reach = v.Px(look.HalfW * 0.8f + 0.5f * sc);
            float px = sx + fx * reach, py = cy + fy * reach * 0.6f;          // 타격 지점: 몸 앞 가슴 높이
            float k = (t - HitMs(e)) / 300f;                                     // 타격 후 경과(0~1)
            float lead = (t - (HitMs(e) - 70f)) / 70f;                           // 타격 직전 70ms 동안 0~1
            SmoothingMode sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!front) Trail(g, e, v, sx, cy, fx, fy, lead, sc);
            else
            {
                string fxn = FxOf(e);
                switch (fxn)
                {
                    case "slime": Slime(g, v, sx, sy, px, py, k, sc, (uint)e.Id); break;
                    case "spores": Spores(g, v, px, py, k, sc, (uint)e.Id); break;
                    case "claws": Claws(g, v, px, py, fx, fy, k, sc); break;
                    case "sonic": Sonic(g, v, px, py, fx, fy, k, sc); break;
                    case "slam": Slam(g, v, sx, sy, px, py, k, sc, (uint)e.Id); break;
                    case "blade": Blade(g, v, px, py, fx, fy, k, sc); break;
                    case "bossbat": BossBat(g, v, sx, sy, px, py, fx, fy, k, sc); break;
                    case "bossvenom": BossVenom(g, v, sx, sy, px, py, k, sc, (uint)e.Id); break;
                    case "bossbone": BossBone(g, v, sx, sy, k, sc, (uint)e.Id); break;
                    case "bossquake": BossQuake(g, v, sx, sy, k, sc, (uint)e.Id); break;
                    case "bosswail": BossWail(g, v, sx, sy, px, py, k, sc, (uint)e.Id); break;
                    case "bossfrost": BossFrost(g, v, sx, sy, k, sc, (uint)e.Id); break;
                    case "bosslava": BossLava(g, v, sx, sy, k, sc, (uint)e.Id); break;
                    case "bosstide": BossTide(g, v, sx, sy, k, sc, (uint)e.Id); break;
                    case "bosscrystal": BossCrystal(g, v, sx, sy, px, py, fx, fy, k, sc, (uint)e.Id); break;
                    case "bossgale": BossGale(g, v, sx, sy, px, py, fx, fy, k, sc, (uint)e.Id); break;
                    case "bossstinger": BossStinger(g, v, sx, sy, px, py, fx, fy, k, sc, (uint)e.Id); break;
                }
                Flash(g, v, px, py, k, sc, e.TemplateId, fxn);
            }
            g.SmoothingMode = sm;
        }

        // ------------------------------------------------------------------ 공통
        private static int Q(float a) { return ((int)Math.Max(0f, Math.Min(255f, a))) & ~15; }

        /// <summary>타격 직전 몸 뒤로 늘어서는 속도선(몸이 앞으로 튀어 나가는 느낌).</summary>
        private void Trail(IGfx g, Entity e, ViewState v, float sx, float cy, float fx, float fy, float lead, float sc)
        {
            if (lead < 0f || lead > 1.6f) return;
            if (AttackPose.StyleOf(e) == AttackStyle.Hop) return;
            float a = 170f * (1f - Math.Min(1f, lead / 1.6f));
            for (int i = -1; i <= 1; ++i)
            {
                float oy = i * v.Px(0.55f * sc), len = v.Px((1.6f + 1.2f * Math.Min(1f, lead)) * sc);
                float x1 = sx - fx * v.Px(0.9f * sc), y1 = cy + oy - fy * v.Px(0.5f) * 0.6f;
                g.DrawLine(_res.Pen(Q(a * (i == 0 ? 1f : 0.6f)), 255, 255, 255, Math.Max(1f, v.Px(0.07f * sc))), x1, y1, x1 - fx * len, y1 - fy * len * 0.6f);
            }
        }

        /// <summary>타격 순간 번쩍이는 충격 섬광 + 퍼지는 고리.</summary>
        private void Flash(IGfx g, ViewState v, float px, float py, float k, float sc, uint tpl, string fx)
        {
            if (k < 0f || k >= 0.55f) return;
            float p = k / 0.55f, r = v.Px((0.35f + 0.9f * p) * sc);
            Color c = FlashColor(fx);
            int a = Q(220f * (1f - p));
            g.FillEllipse(_res.Brush(a / 6, c.R, c.G, c.B), px - r * 1.7f, py - r * 1.7f, r * 3.4f, r * 3.4f);
            // 뾰족한 별 모양 섬광(긴 가시와 짧은 가시가 번갈아)
            PointF[] star = new PointF[16];
            for (int i = 0; i < 16; ++i)
            {
                double th = i * Math.PI / 8 + 0.2; float rad = (i & 1) == 0 ? r * (1.5f + 0.5f * Hash((uint)(tpl * 7 + 3), i)) : r * 0.42f;
                star[i] = new PointF(px + (float)Math.Cos(th) * rad, py + (float)Math.Sin(th) * rad * 0.8f);
            }
            g.FillPolygon(_res.Brush(Q(a * 0.8f), c.R, c.G, c.B), star);
            for (int i = 0; i < 16; ++i) { star[i].X = px + (star[i].X - px) * 0.55f; star[i].Y = py + (star[i].Y - py) * 0.55f; }
            g.FillPolygon(_res.Brush(a, 255, 255, 245), star);
            g.DrawEllipse(_res.Pen(Q(a * 0.8f), c.R, c.G, c.B, Math.Max(1f, v.Px(0.1f * sc) * (1f - p))), px - r, py - r * 0.7f, r * 2f, r * 1.4f);
        }

        private static Color FlashColor(string fx)
        {
            switch (fx)
            {
                case "sonic": case "bossbat": return Color.FromArgb(200, 150, 255);
                case "slime": case "bossvenom": return Color.FromArgb(170, 255, 140);
                case "bossbone": return Color.FromArgb(200, 225, 255);
                case "bossquake": return Color.FromArgb(255, 170, 90);
                case "bosswail": return Color.FromArgb(150, 245, 245);
                case "bossstinger": return Color.FromArgb(255, 210, 80);
                case "bossfrost": return Color.FromArgb(190, 235, 255);
                case "bosslava": return Color.FromArgb(255, 150, 60);
                case "bosstide": return Color.FromArgb(150, 225, 255);
                case "bosscrystal": return Color.FromArgb(230, 170, 255);
                case "bossgale": return Color.FromArgb(200, 235, 255);
                default: return Color.FromArgb(255, 210, 140);
            }
        }

        private void Streak(IGfx g, ViewState v, float x0, float y0, float x1, float y1, float w, Color c, float a)
        {
            if (a < 16f) return;
            g.DrawLine(_res.Pen(Q(a * 0.35f), c.R, c.G, c.B, w * 3.2f), x0, y0, x1, y1);
            g.DrawLine(_res.Pen(Q(a), c.R, c.G, c.B, w * 1.2f), x0, y0, x1, y1);
            g.DrawLine(_res.Pen(Q(a), 255, 255, 255, Math.Max(1f, w * 0.45f)), x0, y0, x1, y1);
        }

        private static float Hash(uint id, int i) { uint h = (uint)(id * 2654435761u) ^ (uint)(i * 40503); h ^= h >> 13; h *= 1274126177u; return (h & 1023) / 1023f; }

        // ------------------------------------------------------------------ 늑대: 세 줄 발톱 자국
        private void Claws(IGfx g, ViewState v, float px, float py, float fx, float fy, float k, float sc)
        {
            float p = (k + 0.1f) / 0.7f; if (p < 0f || p >= 1f) return;
            float ext = AttackPose.EaseOut(p * 1.8f), a = 255f * (1f - p) * (1f - p);
            float dx = fx >= 0 ? 1f : -1f; if (Math.Abs(fx) < 0.3f) dx = 0.5f;
            float L = v.Px(2.0f * sc);
            // 위 앞에서 아래 뒤로 긋는 대각선, 세 줄이 나란히
            for (int i = -1; i <= 1; ++i)
            {
                float ox = i * v.Px(0.42f * sc) * 0.8f, oy = i * v.Px(0.42f * sc) * 0.6f;
                float x0 = px - dx * L * 0.35f + ox, y0 = py - L * 0.5f - oy;
                float x1 = x0 + dx * L * 0.7f * ext, y1 = y0 + L * 1.0f * ext;
                Streak(g, v, x0, y0, x1, y1, Math.Max(1.2f, v.Px(0.13f * sc)), Color.FromArgb(255, 120, 90), a);
            }
        }

        // ------------------------------------------------------------------ 날개 해골 기사: 초승달 칼날
        private void Blade(IGfx g, ViewState v, float px, float py, float fx, float fy, float k, float sc)
        {
            float p = (k + 0.15f) / 0.75f; if (p < 0f || p >= 1f) return;
            float sweep = 150f * AttackPose.EaseOut(p * 1.7f), a = 255f * (1f - p) * (1f - p);
            float R = v.Px(1.9f * sc), ang = (float)(Math.Atan2(fy * 0.6f, fx) * 180.0 / Math.PI);
            RectangleF r = new RectangleF(px - R, py - R * 0.75f, R * 2f, R * 1.5f);
            float st = ang - 80f;
            g.DrawArc(_res.Pen(Q(a * 0.35f), 160, 230, 255, v.Px(0.5f * sc)), r, st, sweep);
            g.DrawArc(_res.Pen(Q(a), 190, 240, 255, v.Px(0.24f * sc)), r, st + sweep * 0.3f, sweep * 0.7f);
            g.DrawArc(_res.Pen(Q(a), 255, 255, 255, Math.Max(1f, v.Px(0.09f * sc))), r, st + sweep * 0.5f, sweep * 0.5f);
            // 칼끝에 별빛
            double te = (st + sweep) * Math.PI / 180.0;
            float tx = px + (float)Math.Cos(te) * R, ty = py + (float)Math.Sin(te) * R * 0.75f, d = v.Px(0.35f * sc) * (1f - p) + 1f;
            g.FillEllipse(_res.Brush(Q(a), 255, 255, 255), tx - d, ty - d, d * 2f, d * 2f);
        }

        // ------------------------------------------------------------------ 박쥐: 초음파 고리
        private void Sonic(IGfx g, ViewState v, float px, float py, float fx, float fy, float k, float sc)
        {
            for (int i = 0; i < 3; ++i)
            {
                float p = (k - i * 0.14f) / 0.7f; if (p < 0f || p >= 1f) continue;
                float r = v.Px((0.5f + 2.0f * p) * sc), a = 220f * (1f - p);
                float ox = fx * v.Px(0.6f * p * sc), oy = fy * v.Px(0.6f * p * sc) * 0.6f;
                g.DrawEllipse(_res.Pen(Q(a), 190, 140, 255, Math.Max(1.2f, v.Px(0.16f * sc) * (1f - p))), px + ox - r * 0.7f, py + oy - r, r * 1.4f, r * 2f);
            }
        }

        // ------------------------------------------------------------------ slam: 지면 균열 충격과 불씨 (보스/덩치 큰 몬스터)
        private void Slam(IGfx g, ViewState v, float sx, float sy, float px, float py, float k, float sc, uint id)
        {
            if (k < 0f || k >= 1f) return;
            float gy = sy, ex = px, ey = gy;                                     // 충격은 발 밑 땅에서
            float rr = v.Px((0.8f + 3.4f * AttackPose.EaseOut(k)) * sc), a = 230f * (1f - k) * (1f - k);
            g.DrawEllipse(_res.Pen(Q(a), 255, 170, 90, Math.Max(1.5f, v.Px(0.24f * sc) * (1f - k))), ex - rr, ey - rr * 0.42f, rr * 2f, rr * 0.84f);
            g.DrawEllipse(_res.Pen(Q(a * 0.6f), 255, 90, 50, Math.Max(1f, v.Px(0.5f * sc) * (1f - k))), ex - rr * 0.7f, ey - rr * 0.3f, rr * 1.4f, rr * 0.6f);
            for (int i = 0; i < 9; ++i)                                           // 땅에서 갈라져 뻗는 균열 빛
            {
                double th = (i + Hash(id, i) * 0.6) * Math.PI * 2 / 9; float len = rr * (0.6f + 0.5f * Hash(id, i + 9)) * AttackPose.EaseOut(k * 2f);
                g.DrawLine(_res.Pen(Q(a), 255, 160, 70, Math.Max(1f, v.Px(0.1f * sc))), ex, ey, ex + (float)Math.Cos(th) * len, ey + (float)Math.Sin(th) * len * 0.42f);
            }
            for (int i = 0; i < 12; ++i)                                          // 위로 튀는 불씨
            {
                float h0 = Hash(id, i + 20), sp = 0.4f + h0, x = ex + (Hash(id, i + 40) - 0.5f) * rr * 1.4f;
                float yy = ey - v.Px(2.8f * sp * sc) * (float)Math.Sin(Math.PI * Math.Min(1f, k * 1.2f)) * 0.9f - v.Px(0.2f * sc);
                float d = Math.Max(1f, v.Px(0.1f * sc) * (1f - k));
                g.FillEllipse(_res.Brush(Q(a), 255, 190 - (int)(h0 * 90), 60), x - d, yy - d, d * 2f, d * 2f);
            }
        }

        // ------------------------------------------------------------------ slime: 착지에 튀는 점액
        private void Slime(IGfx g, ViewState v, float sx, float sy, float px, float py, float k, float sc, uint id)
        {
            if (k < 0f || k >= 1f) return;
            float a = 230f * (1f - k) * (1f - k);
            float pw = v.Px((0.9f + 1.4f * AttackPose.EaseOut(k)) * sc);                      // 바닥에 퍼진 웅덩이
            g.FillEllipse(_res.Brush(Q(a * 0.45f), 90, 220, 90), sx - pw, sy - pw * 0.22f, pw * 2f, pw * 0.44f);
            for (int i = 0; i < 9; ++i)                                                        // 포물선으로 튀는 방울
            {
                float ang = (i + Hash(id, i) * 0.7f) / 9f * (float)(Math.PI * 2), sp = 0.7f + Hash(id, i + 9);
                float dist = v.Px(1.8f * sp * sc) * AttackPose.EaseOut(k);
                float x = sx + (float)Math.Cos(ang) * dist, y = sy + (float)Math.Sin(ang) * dist * 0.4f - v.Px(1.5f * sp * sc) * 4f * k * (1f - k);
                float d = Math.Max(1f, v.Px((0.2f - 0.12f * k) * sc));
                g.FillEllipse(_res.Brush(Q(a), 120, 235, 110), x - d, y - d, d * 2f, d * 2f);
                g.FillEllipse(_res.Brush(Q(a), 230, 255, 220), x - d * 0.4f, y - d * 0.6f, d * 0.6f, d * 0.6f);
            }
        }

        // ------------------------------------------------------------------ 버섯 전사: 포자 구름
        private void Spores(IGfx g, ViewState v, float px, float py, float k, float sc, uint id)
        {
            if (k < 0f || k >= 1f) return;
            float a = 200f * (1f - k) * (1f - k);
            for (int i = 0; i < 8; ++i)
            {
                float h = Hash(id, i), ang = h * (float)(Math.PI * 2);
                float dist = v.Px((0.4f + 1.5f * h) * sc) * AttackPose.EaseOut(k), rise = v.Px(1.2f * sc) * k * (0.5f + h);
                float x = px + (float)Math.Cos(ang) * dist, y = py + (float)Math.Sin(ang) * dist * 0.55f - rise;
                float r = v.Px((0.25f + 0.55f * h) * sc) * (0.6f + 0.9f * k);
                g.FillEllipse(_res.Brush(Q(a * 0.55f), 200, 220, 110), x - r, y - r, r * 2f, r * 2f);
            }
            for (int i = 0; i < 12; ++i)                                                        // 반짝이는 홀씨
            {
                float h = Hash(id, i + 30), ang = h * (float)(Math.PI * 2), dist = v.Px((0.6f + 2.0f * Hash(id, i + 50)) * sc) * AttackPose.EaseOut(k);
                float x = px + (float)Math.Cos(ang) * dist, y = py + (float)Math.Sin(ang) * dist * 0.55f - v.Px(1.6f * sc) * k * (0.5f + h);
                float d = Math.Max(1f, v.Px(0.07f * sc));
                g.FillEllipse(_res.Brush(Q(a), 255, 250, 170), x - d, y - d, d * 2f, d * 2f);
            }
        }

        // ------------------------------------------------------------------ 보스 전용 타격 (sprites.json fx: bossbat, bossvenom, bossbone, bossquake, bosswail, bossstinger)

        /// <summary>박쥐 보스: 앞쪽으로 퍼지는 초음파 고리 다섯 겹과 발 밑에 번지는 보랏빛 파문.</summary>
        private void BossBat(IGfx g, ViewState v, float sx, float sy, float px, float py, float fx, float fy, float k, float sc)
        {
            for (int i = 0; i < 5; ++i)
            {
                float p = (k - i * 0.1f) / 0.75f; if (p < 0f || p >= 1f) continue;
                float r = v.Px((0.6f + 3.6f * p) * sc), a = 230f * (1f - p);
                float ox = fx * v.Px(1.4f * p * sc), oy = fy * v.Px(1.4f * p * sc) * 0.6f;
                g.DrawEllipse(_res.Pen(Q(a * 0.4f), 150, 90, 255, Math.Max(2f, v.Px(0.34f * sc) * (1f - p))), px + ox - r * 0.7f, py + oy - r, r * 1.4f, r * 2f);
                g.DrawEllipse(_res.Pen(Q(a), 205, 160, 255, Math.Max(1.2f, v.Px(0.14f * sc) * (1f - p))), px + ox - r * 0.7f, py + oy - r, r * 1.4f, r * 2f);
            }
            if (k >= 0f && k < 1f)
            {
                float rr = v.Px((0.8f + 3.0f * AttackPose.EaseOut(k)) * sc), a = 190f * (1f - k) * (1f - k);
                g.DrawEllipse(_res.Pen(Q(a), 170, 110, 255, Math.Max(1.5f, v.Px(0.2f * sc))), sx - rr, sy - rr * 0.42f, rr * 2f, rr * 0.84f);
            }
        }

        /// <summary>독거미 보스: 바닥에 퍼지는 독 웅덩이, 거미줄 방사선, 튀는 독방울.</summary>
        private void BossVenom(IGfx g, ViewState v, float sx, float sy, float px, float py, float k, float sc, uint id)
        {
            if (k < 0f || k >= 1f) return;
            float a = 235f * (1f - k) * (1f - k * 0.5f);
            float pw = v.Px((1.2f + 2.6f * AttackPose.EaseOut(k)) * sc);
            g.FillEllipse(_res.Brush(Q(a * 0.5f), 70, 190, 60), sx - pw, sy - pw * 0.3f, pw * 2f, pw * 0.6f);
            g.FillEllipse(_res.Brush(Q(a * 0.45f), 150, 255, 110), sx - pw * 0.6f, sy - pw * 0.18f, pw * 1.2f, pw * 0.36f);
            for (int i = 0; i < 8; ++i)                                                    // 거미줄 방사선
            {
                double th = i * Math.PI / 4 + 0.2; float len = pw * 1.15f * AttackPose.EaseOut(k * 2f);
                g.DrawLine(_res.Pen(Q(a * 0.7f), 235, 255, 225, Math.Max(1f, v.Px(0.05f * sc))), sx, sy, sx + (float)Math.Cos(th) * len, sy + (float)Math.Sin(th) * len * 0.4f);
            }
            for (int i = 0; i < 10; ++i)                                                   // 튀는 독방울
            {
                float h = Hash(id, i), ang = h * (float)(Math.PI * 2), sp = 0.6f + Hash(id, i + 9);
                float dist = v.Px(2.6f * sp * sc) * AttackPose.EaseOut(k);
                float x = sx + (float)Math.Cos(ang) * dist, y = sy + (float)Math.Sin(ang) * dist * 0.4f - v.Px(2.2f * sp * sc) * 4f * k * (1f - k);
                float d = Math.Max(1f, v.Px((0.26f - 0.16f * k) * sc));
                g.FillEllipse(_res.Brush(Q(a), 130, 240, 90), x - d, y - d, d * 2f, d * 2f);
                g.FillEllipse(_res.Brush(Q(a), 235, 255, 215), x - d * 0.4f, y - d * 0.6f, d * 0.6f, d * 0.6f);
            }
        }

        /// <summary>해골 보스: 발 밑에서 둥글게 솟는 뼈창 일곱 개.</summary>
        private void BossBone(IGfx g, ViewState v, float sx, float sy, float k, float sc, uint id)
        {
            if (k < -0.05f || k >= 1f) return;
            float rise = AttackPose.EaseOut(Math.Max(0f, k) * 3f), a = 255f * (k < 0.55f ? 1f : 1f - (k - 0.55f) / 0.45f);
            float ring = v.Px(2.4f * sc);
            g.DrawEllipse(_res.Pen(Q(a * 0.5f), 190, 215, 245, Math.Max(1.5f, v.Px(0.16f * sc))), sx - ring, sy - ring * 0.4f, ring * 2f, ring * 0.8f);
            for (int i = 0; i < 7; ++i)
            {
                double th = (i + Hash(id, i) * 0.4) * Math.PI * 2 / 7;
                float bx = sx + (float)Math.Cos(th) * ring * 0.85f, by = sy + (float)Math.Sin(th) * ring * 0.85f * 0.4f;
                float h = v.Px((0.9f + 0.8f * Hash(id, i + 9)) * sc) * rise, w = v.Px(0.2f * sc);
                if (FxSprites.DrawUp(g, FxSprites.BoneSpike(), bx, by, h * 1.1f, a / 255f)) continue;
                PointF[] spike = { new PointF(bx - w, by), new PointF(bx + w, by), new PointF(bx + w * 0.15f, by - h) };
                g.FillPolygon(_res.Brush(Q(a), 225, 232, 240), spike);
                g.DrawPolygon(_res.Pen(Q(a), 120, 135, 160, 1.2f), spike);
            }
        }

        /// <summary>골렘 보스: 두 겹 충격파, 사방으로 튀는 바위 조각, 먼지.</summary>
        private void BossQuake(IGfx g, ViewState v, float sx, float sy, float k, float sc, uint id)
        {
            Slam(g, v, sx, sy, sx, sy, k, sc * 1.25f, id);
            if (k < 0f || k >= 1f) return;
            float a = 240f * (1f - k) * (1f - k);
            for (int i = 0; i < 10; ++i)
            {
                float h = Hash(id, i + 60), ang = h * (float)(Math.PI * 2), sp = 0.5f + Hash(id, i + 70);
                float dist = v.Px(3.4f * sp * sc) * AttackPose.EaseOut(k);
                float x = sx + (float)Math.Cos(ang) * dist, y = sy + (float)Math.Sin(ang) * dist * 0.42f - v.Px(2.6f * sp * sc) * 4f * k * (1f - k);
                float d = Math.Max(1.5f, v.Px((0.3f + 0.2f * h) * sc) * (1f - k * 0.4f));
                if (FxSprites.Rock(i) != null) { FxSprites.DrawA(g, FxSprites.Rock(i), x, y, d * 2.6f, (k * 420f + i * 50f) % 360f, a / 255f); continue; }
                PointF[] rock = { new PointF(x - d, y), new PointF(x - d * 0.3f, y - d), new PointF(x + d, y - d * 0.4f), new PointF(x + d * 0.5f, y + d * 0.6f) };
                g.FillPolygon(_res.Brush(Q(a), 120, 105, 92), rock);
                g.DrawPolygon(_res.Pen(Q(a), 60, 52, 46, 1.2f), rock);
            }
            float dr = v.Px((1.5f + 4.0f * AttackPose.EaseOut(k)) * sc);
            g.FillEllipse(_res.Brush(Q(a * 0.25f), 190, 170, 140), sx - dr, sy - dr * 0.3f, dr * 2f, dr * 0.6f);
        }

        /// <summary>유령 보스: 사방으로 퍼지는 청록 울음 고리와 위로 흩날리는 영혼 불꽃.</summary>
        private void BossWail(IGfx g, ViewState v, float sx, float sy, float px, float py, float k, float sc, uint id)
        {
            for (int i = 0; i < 3; ++i)
            {
                float p = (k - i * 0.16f) / 0.7f; if (p < 0f || p >= 1f) continue;
                float rr = v.Px((0.8f + 4.4f * p) * sc), a = 220f * (1f - p);
                g.DrawEllipse(_res.Pen(Q(a * 0.4f), 90, 220, 220, Math.Max(2f, v.Px(0.4f * sc) * (1f - p))), sx - rr, sy - rr * 0.42f, rr * 2f, rr * 0.84f);
                g.DrawEllipse(_res.Pen(Q(a), 190, 255, 255, Math.Max(1.2f, v.Px(0.12f * sc) * (1f - p))), sx - rr, sy - rr * 0.42f, rr * 2f, rr * 0.84f);
            }
            if (k < 0f || k >= 1f) return;
            float aa = 230f * (1f - k);
            for (int i = 0; i < 9; ++i)
            {
                float h = Hash(id, i + 80), ang = h * (float)(Math.PI * 2), dist = v.Px((1.0f + 2.8f * Hash(id, i + 90)) * sc) * AttackPose.EaseOut(k);
                float x = sx + (float)Math.Cos(ang) * dist, y = sy + (float)Math.Sin(ang) * dist * 0.42f - v.Px(3.2f * sc) * k * (0.4f + h);
                float d = Math.Max(1.5f, v.Px((0.22f + 0.2f * h) * sc) * (1f - k * 0.6f));
                g.FillEllipse(_res.Brush(Q(aa * 0.4f), 120, 235, 235), x - d * 1.8f, y - d * 1.8f, d * 3.6f, d * 3.6f);
                g.FillEllipse(_res.Brush(Q(aa), 225, 255, 255), x - d, y - d * 1.3f, d * 2f, d * 2.6f);
            }
        }

        /// <summary>전갈 보스: 위에서 내리꽂는 꼬리 침의 궤적과 번지는 독방울.</summary>
        private void BossStinger(IGfx g, ViewState v, float sx, float sy, float px, float py, float fx, float fy, float k, float sc, uint id)
        {
            float p = (k + 0.12f) / 0.62f;
            if (p >= 0f && p < 1f)
            {
                float e = AttackPose.EaseOut(Math.Min(1f, p * 1.6f)), a = 255f * (1f - p) * (1f - p);
                float h = v.Px(3.4f * sc);
                float x0 = px - fx * v.Px(0.8f * sc), y0 = py - h, x1 = px, y1 = py;
                float mx = (x0 + x1) * 0.5f - fx * v.Px(1.0f * sc), my = (y0 + y1) * 0.5f;
                float cx = x0 + (x1 - x0) * e, cy = y0 + (y1 - y0) * e;
                PointF[] curve = { new PointF(x0, y0), new PointF(mx + (cx - mx) * 0.3f, my + (cy - my) * 0.3f), new PointF(cx, cy) };
                g.DrawLines(_res.Pen(Q(a * 0.35f), 255, 190, 60, Math.Max(3f, v.Px(0.5f * sc))), curve);
                g.DrawLines(_res.Pen(Q(a), 255, 225, 120, Math.Max(1.6f, v.Px(0.2f * sc))), curve);
                g.DrawLines(_res.Pen(Q(a), 255, 255, 240, Math.Max(1f, v.Px(0.07f * sc))), curve);
            }
            if (k < 0f || k >= 1f) return;
            float aa = 230f * (1f - k) * (1f - k);
            float pw = v.Px((0.6f + 1.4f * AttackPose.EaseOut(k)) * sc);
            g.FillEllipse(_res.Brush(Q(aa * 0.5f), 150, 200, 50), px - pw, py - pw * 0.3f, pw * 2f, pw * 0.6f);
            for (int i = 0; i < 8; ++i)
            {
                float h = Hash(id, i + 100), ang = (i + h * 0.6f) / 8f * (float)(Math.PI * 2), sp = 0.6f + Hash(id, i + 110);
                float dist = v.Px(1.8f * sp * sc) * AttackPose.EaseOut(k);
                float x = px + (float)Math.Cos(ang) * dist, y = py + (float)Math.Sin(ang) * dist * 0.4f - v.Px(1.6f * sp * sc) * 4f * k * (1f - k);
                float d = Math.Max(1f, v.Px((0.2f - 0.1f * k) * sc));
                g.FillEllipse(_res.Brush(Q(aa), 200, 240, 70), x - d, y - d, d * 2f, d * 2f);
            }
        }

        /// <summary>설인 보스: 땅에서 솟는 얼음 기둥 고리와 서리 충격파, 흩날리는 눈가루.</summary>
        private void BossFrost(IGfx g, ViewState v, float sx, float sy, float k, float sc, uint id)
        {
            if (k < -0.05f || k >= 1f) return;
            float kk = Math.Max(0f, k), rise = AttackPose.EaseOut(kk * 3f), a = 255f * (kk < 0.5f ? 1f : 1f - (kk - 0.5f) / 0.5f);
            float rr = v.Px((0.8f + 3.6f * AttackPose.EaseOut(kk)) * sc);
            g.FillEllipse(_res.Brush(Q(a * 0.18f), 190, 235, 255), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
            g.DrawEllipse(_res.Pen(Q(a), 210, 240, 255, Math.Max(1.5f, v.Px(0.2f * sc) * (1f - kk))), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
            for (int i = 0; i < 6; ++i)
            {
                double th = (i + Hash(id, i) * 0.4) * Math.PI * 2 / 6; float R = v.Px(2.2f * sc);
                float bx = sx + (float)Math.Cos(th) * R * (0.5f + 0.5f * Hash(id, i + 9)), by = sy + (float)Math.Sin(th) * R * 0.4f;
                float h = v.Px((0.9f + 1.0f * Hash(id, i + 20)) * sc) * rise, w = v.Px(0.22f * sc);
                if (FxSprites.DrawUp(g, FxSprites.IceSpike(i), bx, by, h * 0.9f, a * 0.95f / 255f)) continue;
                PointF[] ice = { new PointF(bx - w, by), new PointF(bx + w, by), new PointF(bx + w * 0.2f, by - h) };
                g.FillPolygon(_res.Brush(Q(a * 0.85f), 180, 228, 255), ice);
                g.DrawPolygon(_res.Pen(Q(a), 240, 250, 255, 1.2f), ice);
            }
            for (int i = 0; i < 14; ++i)
            {
                float h0 = Hash(id, i + 40), x = sx + (Hash(id, i + 60) - 0.5f) * rr * 1.8f, y = sy - v.Px(2.4f * sc) * kk * (0.3f + h0) + v.Px(0.8f * sc) * (1f - h0);
                float d = Math.Max(1f, v.Px(0.07f * sc));
                g.FillEllipse(_res.Brush(Q(a), 245, 250, 255), x - d, y - d, d * 2f, d * 2f);
            }
        }

        /// <summary>용암 도마뱀 보스: 발 밑 용암 고리, 갈라지는 불길, 솟구치는 불덩이.</summary>
        private void BossLava(IGfx g, ViewState v, float sx, float sy, float k, float sc, uint id)
        {
            if (k < 0f || k >= 1f) return;
            float a = 235f * (1f - k) * (1f - k), rr = v.Px((0.8f + 3.8f * AttackPose.EaseOut(k)) * sc);
            g.FillEllipse(_res.Brush(Q(a * 0.30f), 255, 100, 30), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
            g.DrawEllipse(_res.Pen(Q(a), 255, 190, 70, Math.Max(1.5f, v.Px(0.26f * sc) * (1f - k))), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
            for (int i = 0; i < 10; ++i)
            {
                double th = (i + Hash(id, i) * 0.6) * Math.PI * 2 / 10; float len = rr * (0.5f + 0.5f * Hash(id, i + 9));
                g.DrawLine(_res.Pen(Q(a * 0.6f), 255, 90, 30, Math.Max(2f, v.Px(0.34f * sc))), sx, sy, sx + (float)Math.Cos(th) * len, sy + (float)Math.Sin(th) * len * 0.4f);
                g.DrawLine(_res.Pen(Q(a), 255, 220, 120, Math.Max(1f, v.Px(0.1f * sc))), sx, sy, sx + (float)Math.Cos(th) * len, sy + (float)Math.Sin(th) * len * 0.4f);
            }
            for (int i = 0; i < 10; ++i)
            {
                float h0 = Hash(id, i + 20), x = sx + (Hash(id, i + 40) - 0.5f) * rr * 1.5f, up = v.Px((2.0f + 2.4f * h0) * sc) * (float)Math.Sin(Math.PI * Math.Min(1f, k * 1.1f));
                float d = Math.Max(1.5f, v.Px((0.30f - 0.18f * k) * sc));
                g.FillEllipse(_res.Brush(Q(a * 0.5f), 255, 120, 40), x - d * 1.8f, sy - up - d * 1.8f, d * 3.6f, d * 3.6f);
                g.FillEllipse(_res.Brush(Q(a), 255, 210, 90), x - d, sy - up - d, d * 2f, d * 2f);
            }
        }

        /// <summary>소라게 보스: 밀려오는 물결 고리 두 겹과 튀는 물방울, 조개 파편.</summary>
        private void BossTide(IGfx g, ViewState v, float sx, float sy, float k, float sc, uint id)
        {
            if (k < 0f || k >= 1f) return;
            for (int i = 0; i < 2; ++i)
            {
                float p = (k - i * 0.16f) / 0.84f; if (p < 0f || p >= 1f) continue;
                float rr = v.Px((0.8f + 4.4f * AttackPose.EaseOut(p)) * sc), a = 230f * (1f - p);
                g.FillEllipse(_res.Brush(Q(a * 0.16f), 90, 190, 240), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
                g.DrawEllipse(_res.Pen(Q(a * 0.5f), 70, 170, 230, Math.Max(2f, v.Px(0.4f * sc) * (1f - p))), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
                g.DrawEllipse(_res.Pen(Q(a), 225, 248, 255, Math.Max(1.2f, v.Px(0.1f * sc) * (1f - p))), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
            }
            float aa = 230f * (1f - k);
            for (int i = 0; i < 12; ++i)
            {
                float h0 = Hash(id, i), ang = h0 * (float)(Math.PI * 2), sp = 0.6f + Hash(id, i + 9), dist = v.Px(3.0f * sp * sc) * AttackPose.EaseOut(k);
                float x = sx + (float)Math.Cos(ang) * dist, y = sy + (float)Math.Sin(ang) * dist * 0.4f - v.Px(2.4f * sp * sc) * 4f * k * (1f - k);
                float d = Math.Max(1f, v.Px((0.2f - 0.1f * k) * sc));
                if (i % 4 == 0) { PointF[] sh = { new PointF(x - d, y), new PointF(x, y - d * 1.4f), new PointF(x + d, y), new PointF(x, y + d) }; g.FillPolygon(_res.Brush(Q(aa), 244, 220, 196), sh); }
                else g.FillEllipse(_res.Brush(Q(aa), 190, 235, 255), x - d, y - d, d * 2f, d * 2f);
            }
        }

        /// <summary>수정 사마귀 보스: 앞으로 내리긋는 수정 낫 궤적과 땅에서 튀어나오는 수정 조각.</summary>
        private void BossCrystal(IGfx g, ViewState v, float sx, float sy, float px, float py, float fx, float fy, float k, float sc, uint id)
        {
            float p = (k + 0.15f) / 0.7f;
            if (p >= 0f && p < 1f)
            {
                float sweep = 160f * AttackPose.EaseOut(p * 1.7f), a = 255f * (1f - p) * (1f - p);
                float R = v.Px(2.6f * sc), ang = (float)(Math.Atan2(fy * 0.6f, fx) * 180.0 / Math.PI);
                RectangleF r = new RectangleF(px - R, py - R * 0.75f, R * 2f, R * 1.5f);
                g.DrawArc(_res.Pen(Q(a * 0.35f), 220, 130, 240, v.Px(0.6f * sc)), r, ang - 85f, sweep);
                g.DrawArc(_res.Pen(Q(a), 160, 235, 255, v.Px(0.26f * sc)), r, ang - 85f + sweep * 0.3f, sweep * 0.7f);
                g.DrawArc(_res.Pen(Q(a), 255, 255, 255, Math.Max(1f, v.Px(0.09f * sc))), r, ang - 85f + sweep * 0.5f, sweep * 0.5f);
            }
            if (k < 0f || k >= 1f) return;
            float aa = 255f * (k < 0.5f ? 1f : 1f - (k - 0.5f) / 0.5f), rise = AttackPose.EaseOut(k * 3f);
            for (int i = 0; i < 7; ++i)
            {
                double th = (i + Hash(id, i) * 0.5) * Math.PI * 2 / 7; float R2 = v.Px(2.0f * sc);
                float bx = sx + (float)Math.Cos(th) * R2, by = sy + (float)Math.Sin(th) * R2 * 0.4f;
                float h = v.Px((0.8f + 0.9f * Hash(id, i + 9)) * sc) * rise, w = v.Px(0.2f * sc);
                bool pink = (i & 1) == 1;
                if (FxSprites.DrawUp(g, FxSprites.Crystal(pink ? 1 : 0), bx, by, h * 1.1f, aa / 255f)) continue;
                PointF[] cr = { new PointF(bx - w, by), new PointF(bx - w * 0.3f, by - h), new PointF(bx + w, by - h * 0.7f), new PointF(bx + w * 0.8f, by) };
                g.FillPolygon(_res.Brush(Q(aa * 0.9f), pink ? 245 : 150, pink ? 150 : 230, pink ? 230 : 255), cr);
                g.DrawPolygon(_res.Pen(Q(aa), 255, 255, 255, 1.2f), cr);
            }
        }

        /// <summary>폭풍 독수리 보스: 앞으로 쏘아지는 바람 소용돌이 줄기와 번개 불꽃.</summary>
        private void BossGale(IGfx g, ViewState v, float sx, float sy, float px, float py, float fx, float fy, float k, float sc, uint id)
        {
            if (k < -0.05f || k >= 1f) return;
            float kk = Math.Max(0f, k), a = 240f * (1f - kk) * (1f - kk * 0.4f);
            for (int i = 0; i < 6; ++i)
            {
                float off = (i - 2.5f) * v.Px(0.5f * sc), len = v.Px((2.0f + 4.4f * AttackPose.EaseOut(kk)) * sc);
                float x0 = px - fx * v.Px(0.6f * sc), y0 = py + off * 0.6f - fy * v.Px(0.3f * sc);
                float x1 = x0 + fx * len, y1 = y0 + fy * len * 0.6f + (float)Math.Sin(kk * 7 + i) * v.Px(0.35f * sc);
                g.DrawLine(_res.Pen(Q(a * 0.35f), 150, 210, 255, Math.Max(2f, v.Px(0.34f * sc))), x0, y0, x1, y1);
                g.DrawLine(_res.Pen(Q(a), 225, 245, 255, Math.Max(1f, v.Px(0.1f * sc))), x0, y0, x1, y1);
            }
            for (int i = 0; i < 3; ++i)
            {
                float t0 = (kk + i * 0.3f) % 1f, x = px + fx * v.Px(5.0f * sc) * t0, y = py + fy * v.Px(5.0f * sc) * t0 * 0.6f;
                float r = v.Px(0.9f * sc) * (1f - t0);
                if (r < 1f) continue;
                PointF[] zz = { new PointF(x - r, y - r), new PointF(x + r * 0.2f, y - r * 0.2f), new PointF(x - r * 0.3f, y + r * 0.1f), new PointF(x + r, y + r) };
                g.DrawLines(_res.Pen(Q(a), 255, 240, 130, Math.Max(1.5f, v.Px(0.12f * sc))), zz);
            }
            float rr = v.Px((0.8f + 3.0f * AttackPose.EaseOut(kk)) * sc);
            g.DrawEllipse(_res.Pen(Q(a * 0.7f), 190, 230, 255, Math.Max(1.5f, v.Px(0.18f * sc))), sx - rr, sy - rr * 0.4f, rr * 2f, rr * 0.8f);
        }
    }
}
