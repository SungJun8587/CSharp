//***************************************************************************
// Effects.cs : 전투 연출(범위 예고, 폭발, 베기, 투사체, 불꽃, 사망 연기, 피해 숫자, 상태 오라).
// 파티클은 상태를 따로 두지 않고 "연출 시작 시각 + 해시 난수" 로 매번 같은 궤적을 계산한다.
// (그래서 같은 입력이면 PNG 스냅샷도 항상 같고, 객체 풀/수명 관리가 필요 없다.)
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.World;

namespace GameViewer.App.Render
{
    public sealed class EffectRenderer : IDisposable
    {
        private readonly Resources _res;
        private readonly SkillEffects _skill;
        private readonly AttackEffects _attack;
        private readonly MaterialFx _mat;
        internal AttackEffects Attack { get { return _attack; } }
        private readonly SpriteCache _glows = new SpriteCache(200);
        private readonly SpriteCache.PaintFn _glowPaint;
        private Color _gc; private int _ga; private int _gs;
        private readonly HatchBrush _hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(70, 255, 70, 50), Color.FromArgb(0, 0, 0, 0));

        public EffectRenderer(Resources res)
        {
            _res = res;
            _skill = new SkillEffects(res, this);
            _attack = new AttackEffects(res, this);
            _mat = new MaterialFx(res, this);
            _glowPaint = delegate (IGfx g) { P.Glow(g, 0, 0, _gs / 2f - 1, _gc, _ga); };
        }

        public void Dispose() { _glows.Dispose(); _hatch.Dispose(); }

        /// <summary>미리 그려 둔 광원 번짐을 찍는다. 색/세기는 몇 단계로 묶어 비트맵 종류를 제한한다.</summary>
        public void Glow(IGfx g, Color c, float cx, float cy, float rx, float ry, float strength)
        {
            int level = (int)Math.Max(0, Math.Min(7, Math.Round(strength * 7)));
            if (level == 0 || rx < 1 || ry < 1) return;
            int ga = 36 + level * 31;
            Color q = Color.FromArgb(255, c.R & 0xF8, c.G & 0xF8, c.B & 0xF8);
            _gc = q; _ga = ga; _gs = 64;
            long key = (1L << 40) | ((long)(uint)(q.ToArgb() & 0xFFFFFF) << 8) | (long)level;
            SpriteCache.Entry e = _glows.Get(key, 64, 64, 32, 32, _glowPaint);
            g.DrawImage(e.Bmp, cx - rx, cy - ry, rx * 2, ry * 2);
        }

        private static float Rnd(ViewEffect fx, int i) { return Noise.Hash01(i * 7 + (int)(fx.StartMs & 0x7FFF), (int)(fx.EntityId & 0xFF) + i, 0x51u); }

        // ------------------------------------------------------------------ 바닥

        public void DrawGround(IGfx g, WorldModel w, ViewState v, long now)
        {
            for (int i = 0; i < w.Effects.Count; ++i)
            {
                ViewEffect fx = w.Effects[i];
                float sx = v.ToScreenX(fx.X), sy = v.ToScreenY(fx.Y);
                switch (fx.Kind)
                {
                    case EffectKind.Telegraph: DrawTelegraph(g, w, fx, v, now, sx, sy); break;
                    case EffectKind.Ring:
                    {
                        SkillFxInfo info = fx.SkillId != 0 ? SkillFxTable.For(w.Data, fx.SkillId) : null;
                        if (info != null && info.Style != SkillFxStyle.Default && _skill.RingGround(g, info, fx, v, now, fx.X, fx.Y, fx.Radius)) break;
                        DrawBurstGround(g, fx, v, now, sx, sy);
                        break;
                    }
                    case EffectKind.Slash:
                    {
                        if (fx.Variant != 1 || fx.SkillId == 0 || fx.Radius <= 0f) break;
                        SkillFxInfo info = SkillFxTable.For(w.Data, fx.SkillId);
                        if (info != null) _skill.SlashGround(g, info, w, fx, v, now);
                        break;
                    }
                    case EffectKind.LevelUp: DrawLevelUpGround(g, w, fx, v, now); break;
                    case EffectKind.RespawnRing:
                    {
                        float p = fx.Progress(now);
                        float r = v.Px(2.8f) * (0.3f + 0.7f * p);
                        g.DrawEllipse(_res.Pen((int)(220 * (1f - p)), 120, 240, 255, 3f), sx - r, sy - r * 0.7f, r * 2, r * 1.4f);
                        Glow(g, Color.FromArgb(120, 230, 255), sx, sy, r * 1.1f, r * 0.8f, 0.6f * (1f - p));
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ 레벨 업

        /// <summary>레벨 업을 한 영웅의 현재 화면 위치(영웅이 움직이면 연출도 따라간다). 영웅이 없으면 효과가 만들어진 자리.</summary>
        private static void LevelUpAnchor(WorldModel w, ViewEffect fx, ViewState v, long now, out float sx, out float sy)
        {
            float x = fx.X, y = fx.Y;
            Entity c = w.Find(fx.EntityId);
            if (c != null) c.PosAt(now, out x, out y);
            sx = v.ToScreenX(x); sy = v.ToScreenY(y);
        }

        /// <summary>반짝임 한 점: 십자 빛살과 중심 광점.</summary>
        private void Spark(IGfx g, float x, float y, float r, int a, int cr, int cg, int cb)
        {
            if (a <= 0 || r < 0.5f) return;
            g.DrawLine(_res.Pen(a, cr, cg, cb, Math.Max(1f, r * 0.22f)), x - r * 1.6f, y, x + r * 1.6f, y);
            g.DrawLine(_res.Pen(a, cr, cg, cb, Math.Max(1f, r * 0.22f)), x, y - r * 2.2f, x, y + r * 2.2f);
            g.FillEllipse(_res.Brush(a / 3, cr, cg, cb), x - r, y - r, r * 2, r * 2);
            g.FillEllipse(_res.Brush(a, 255, 255, 240), x - r * 0.35f, y - r * 0.35f, r * 0.7f, r * 0.7f);
        }

        /// <summary>바닥: 충격파, 금빛 고리 셋, 돌아가는 룬 원이 퍼지고 발밑이 환하게 빛난다.</summary>
        private void DrawLevelUpGround(IGfx g, WorldModel w, ViewEffect fx, ViewState v, long now)
        {
            float sx, sy; LevelUpAnchor(w, fx, v, now, out sx, out sy);
            float p = fx.Progress(now);
            float glow = p < 0.15f ? p / 0.15f : 1f - (p - 0.15f) / 0.85f;
            Glow(g, Color.FromArgb(255, 226, 120), sx, sy, v.Px(2.6f), v.Px(1.7f), Math.Max(0f, glow) * 0.95f);
            float sw = p / 0.22f;                                                  // 처음에 한 번 빠르게 퍼지는 흰 충격파
            if (sw > 0f && sw < 1f)
            {
                float e0 = 1f - (1f - sw) * (1f - sw) * (1f - sw), r0 = v.Px(0.6f + 4.6f * e0);
                g.DrawEllipse(_res.Pen((int)(230 * (1f - sw)), 255, 250, 225, 7f * (1f - sw) + 1.5f), sx - r0, sy - r0 * 0.45f, r0 * 2, r0 * 0.9f);
            }
            float rf = p < 0.7f ? 1f : 1f - (p - 0.7f) / 0.3f;                     // 돌아가는 룬 원: 이중 원, 눈금, 마름모
            if (rf > 0f)
            {
                float R = v.Px(1.9f), spin = p * 5.5f;
                g.DrawEllipse(_res.Pen((int)(210 * rf), 255, 214, 110, 2.2f), sx - R, sy - R * 0.45f, R * 2, R * 0.9f);
                g.DrawEllipse(_res.Pen((int)(130 * rf), 255, 236, 170, 1.2f), sx - R * 0.72f, sy - R * 0.72f * 0.45f, R * 1.44f, R * 1.44f * 0.9f);
                for (int k = 0; k < 12; ++k)
                {
                    float ang = spin + k * (float)Math.PI / 6f, ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
                    g.DrawLine(_res.Pen((int)(190 * rf), 255, 226, 130, 1.6f), sx + ca * R * 0.86f, sy + sa * R * 0.86f * 0.45f, sx + ca * R * 1.12f, sy + sa * R * 1.12f * 0.45f);
                    if (k % 3 == 0)
                    {
                        float dx = sx + ca * R * 1.28f, dy = sy + sa * R * 1.28f * 0.45f, d = v.Px(0.16f);
                        g.FillPolygon(_res.Brush((int)(230 * rf), 255, 244, 190), new PointF[] { new PointF(dx, dy - d), new PointF(dx + d, dy), new PointF(dx, dy + d), new PointF(dx - d, dy) });
                    }
                }
            }
            for (int k = 0; k < 3; ++k)
            {
                float q = (p - k * 0.12f) / 0.6f;
                if (q <= 0f || q >= 1f) continue;
                float e = 1f - (1f - q) * (1f - q);
                float rr = v.Px(0.8f + 2.6f * e);
                g.DrawEllipse(_res.Pen((int)(235 * (1f - q)), 255, 232 - k * 18, 130 - k * 10, 4f * (1f - q) + 1.2f), sx - rr, sy - rr * 0.45f, rr * 2, rr * 0.9f);
            }
        }

        /// <summary>공중: 하늘로 솟는 빛기둥과 위로 떠오르는 반짝임.</summary>
        private void DrawLevelUpAir(IGfx g, WorldModel w, ViewEffect fx, ViewState v, long now, float p)
        {
            float sx, sy; LevelUpAnchor(w, fx, v, now, out sx, out sy);
            float rise = p < 0.12f ? p / 0.12f : 1f;                  // 기둥이 솟는 구간
            float fade = p < 0.35f ? 1f : 1f - (p - 0.35f) / 0.65f;   // 이후 서서히 사라짐
            float ph = v.Px(7f) * rise, pw = v.Px(0.9f) * (1.15f - 0.3f * p);
            if (fade > 0f)
            {
                for (int k = 0; k < 9; ++k)                           // 발밑에서 부채꼴로 뻗는 빛살
                {
                    float ang = (k - 4f) * 0.34f + (float)Math.Sin(p * 3f + k) * 0.05f;
                    float len = v.Px(3.2f + 2.2f * Rnd(fx, k + 70)) * rise;
                    g.DrawLine(_res.Pen((int)(110 * fade), 255, 226, 140, v.Px(0.1f) + 1f), sx, sy - v.Px(0.2f), sx + (float)Math.Sin(ang) * len, sy - (float)Math.Cos(ang) * len);
                }
                Beam(g, Color.FromArgb(255, 214, 110), sx, sy, sy - ph, pw * 1.4f, fade);
                Beam(g, Color.FromArgb(255, 252, 235), sx, sy, sy - ph, pw * 0.5f, fade);
            }
            for (int s = 0; s < 2; ++s)                               // 몸을 감고 올라가는 나선 반짝임
                for (int j = 0; j < 9; ++j)
                {
                    float q = p * 1.5f - j * 0.075f;
                    if (q <= 0f || q >= 1f) continue;
                    float hx = (float)Math.Sin(q * 9f + s * Math.PI) * v.Px(0.95f * (1f - 0.35f * q));
                    float hy = -v.Px(0.2f + 5.4f * q);
                    Spark(g, sx + hx, sy + hy, v.Px(0.13f + 0.1f * (1f - q)), (int)(235 * (1f - q)), 255, 236, 150);
                }
            float fl = p / 0.25f;                                     // 시작 순간 크게 번쩍이는 별빛
            if (fl > 0f && fl < 1f) Spark(g, sx, sy - v.Px(1.1f), v.Px(1.5f) * (1f - fl * 0.5f), (int)(255 * (1f - fl)), 255, 244, 190);
            for (int k = 0; k < 14; ++k)                              // 반짝이는 불씨: 발밑에서 시작해 위로, 좌우로 살짝 흔들리며 올라간다
            {
                float q = (p * 1.6f - Rnd(fx, k + 50) * 0.5f);
                if (q <= 0f || q >= 1f) continue;
                float ox = (Rnd(fx, k) - 0.5f) * v.Px(2.2f) + (float)Math.Sin(q * 7f + k) * v.Px(0.18f);
                float oy = -v.Px(0.3f + 4.6f * q * (0.6f + Rnd(fx, k + 20) * 0.6f));
                float r = v.Px(0.1f + 0.12f * Rnd(fx, k + 30)) * (1f - 0.5f * q);
                int a = (int)(240 * (1f - q));
                g.FillEllipse(_res.Brush(a, 255, 240, 170), sx + ox - r, sy + oy - r, r * 2, r * 2);
                g.FillEllipse(_res.Brush(a / 3, 255, 220, 110), sx + ox - r * 2.2f, sy + oy - r * 2.2f, r * 4.4f, r * 4.4f);
            }
        }

        private void DrawTelegraph(IGfx g, WorldModel w, ViewEffect fx, ViewState v, long now, float sx, float sy)
        {
            float r = v.Px(fx.Radius);
            if (sx + r < 0 || sy + r < 0 || sx - r > v.Width || sy - r > v.Height) return;
            Entity c = w.Find(fx.EntityId);
            float p = 1f;
            if (c != null && c.CastTimeMs > 0) p = Math.Max(0f, Math.Min(1f, (now - c.CastStartMs) / (float)c.CastTimeMs));
            float ry = r * 0.8f;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(now / (170.0 - 90.0 * p));
            g.FillEllipse(_res.Brush((int)(34 + 34 * p + 18 * pulse), 200, 24, 20), sx - r, sy - ry, r * 2, ry * 2);
            GfxState st = g.Save();
            using (GraphicsPath clip = new GraphicsPath()) { clip.AddEllipse(sx - r, sy - ry, r * 2, ry * 2); g.SetClip(clip, CombineMode.Intersect); }
            g.TranslateTransform(sx, sy);
            g.FillRectangle(_hatch, -r, -ry, r * 2, ry * 2);
            g.Restore(st);
            float ri = r * p;
            if (ri > 1)
            {
                g.FillEllipse(_res.Brush((int)(60 + 70 * p), 255, 96, 40), sx - ri, sy - ri * 0.8f, ri * 2, ri * 1.6f);
                g.DrawEllipse(_res.Pen(200, 255, 190, 120, 2f), sx - ri, sy - ri * 0.8f, ri * 2, ri * 1.6f);
            }
            Pen ring = _res.DashPen(Color.FromArgb(240, 255, p > 0.75f && (now / 90) % 2 == 0 ? 230 : 90, 70), 3f, (now / 40) % 20);
            g.DrawEllipse(ring, sx - r, sy - ry, r * 2, ry * 2);
            if (fx.Radius >= 2.5f && c != null)
            {
                float left = Math.Max(0f, (c.CastTimeMs - (now - c.CastStartMs)) / 1000f);
                _res.OutlinedText(g, left.ToString("0.0"), _res.Big, Color.FromArgb(255, 235, 200), sx, sy, _res.Center);
            }
        }

        internal void DrawBurstGroundOnly(IGfx g, ViewEffect fx, ViewState v, long now, float sx, float sy) { DrawBurstGround(g, fx, v, now, sx, sy); }

        private void DrawBurstGround(IGfx g, ViewEffect fx, ViewState v, long now, float sx, float sy)
        {
            float p = fx.Progress(now);
            float eased = 1f - (1f - p) * (1f - p);
            float full = v.Px(fx.Radius);
            float r = full * (0.3f + 0.7f * eased);
            Glow(g, Color.FromArgb(255, 170, 70), sx, sy, r * 1.15f, r * 0.95f, 0.85f * (1f - p));
            g.DrawEllipse(_res.Pen((int)(220 * (1f - p)), 255, 240, 170, 4f * (1f - p) + 1f), sx - r, sy - r * 0.8f, r * 2, r * 1.6f);
            if (fx.Radius >= 3f)
            {
                // 큰 폭발은 바닥에 균열이 남는다 (더 오래 보이게 느리게 사라진다)
                int a = (int)(190 * (1f - p * p));
                for (int k = 0; k < 9; ++k)
                {
                    double ang = k * Math.PI * 2 / 9 + Rnd(fx, 40) * 0.6;
                    float len = full * (0.55f + 0.4f * Rnd(fx, k + 1)) * Math.Min(1f, p * 3f + 0.2f);
                    float mx = sx + (float)Math.Cos(ang) * len * 0.5f + (Rnd(fx, k + 20) - 0.5f) * full * 0.1f;
                    float my = sy + (float)Math.Sin(ang) * len * 0.4f + (Rnd(fx, k + 30) - 0.5f) * full * 0.06f;
                    Pen cp = _res.Pen(a, 30, 22, 16, 2.5f);
                    g.DrawLine(cp, sx, sy, mx, my);
                    g.DrawLine(cp, mx, my, sx + (float)Math.Cos(ang) * len, sy + (float)Math.Sin(ang) * len * 0.8f);
                }
            }
        }

        // ------------------------------------------------------------------ 공중

        /// <summary>DrawAir 의 연출 종류별 누적 시간(ms, 인덱스 = EffectKind, 마지막 칸 = 떠오르는 글자). 성능 확인용.</summary>
        public readonly double[] KindMs = new double[24];
        private static double Ms() { return System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }

        public void DrawAir(IGfx g, ViewerSession s, ViewState v, long now)
        {
            WorldModel w = s.World;
            for (int i = 0; i < w.Effects.Count; ++i)
            {
                ViewEffect fx = w.Effects[i];
                double t0 = Ms();
                try {
                float p = fx.Progress(now);
                float sx = v.ToScreenX(fx.X), sy = v.ToScreenY(fx.Y);
                switch (fx.Kind)
                {
                    case EffectKind.Projectile: DrawProjectile(g, s, fx, v, now); break;
                    case EffectKind.Slash:
                    {
                        SkillFxInfo info = fx.Variant == 1 && fx.SkillId != 0 ? SkillFxTable.For(w.Data, fx.SkillId) : null;
                        if (info != null && info.Style != SkillFxStyle.Default && _skill.Slash(g, info, w, fx, v, now)) break;
                        if (fx.Variant != 1 && _attack.MeleeSlash(g, w, fx, v, now)) break;
                        DrawSlash(g, w, fx, v, now, p, sx, sy);
                        break;
                    }
                    case EffectKind.Tracer:
                    {
                        Entity c = w.Find(fx.EntityId);
                        if (c == null) break;
                        if (_attack.Tracer(g, w, fx, v, now)) break;
                        float ax, ay; c.PosAt(now, out ax, out ay);
                        float x0 = v.ToScreenX(ax), y0 = v.ToScreenY(ay) - v.Px(1.5f), y1 = sy - v.Px(1.1f);
                        g.DrawLine(_res.Pen((int)(120 * (1f - p)), 255, 240, 170, 5f), x0, y0, sx, y1);
                        g.DrawLine(_res.Pen((int)(240 * (1f - p)), 255, 250, 210, 2f), x0, y0, sx, y1);
                        break;
                    }
                    case EffectKind.Spark:
                    {
                        SkillFxInfo info = fx.SkillId != 0 ? SkillFxTable.For(w.Data, fx.SkillId) : null;
                        if (info != null && _skill.Impact(g, info, fx, v, now, p, sx, sy)) break;
                        _attack.Hit(g, w, fx, v, p, sx, sy);
                        break;
                    }
                    case EffectKind.Ring:
                    {
                        SkillFxInfo info = fx.SkillId != 0 ? SkillFxTable.For(w.Data, fx.SkillId) : null;
                        if (info != null && info.Style != SkillFxStyle.Default && _skill.RingAir(g, info, fx, v, now, fx.X, fx.Y, fx.Radius)) break;
                        DrawBurstDust(g, fx, v, p, sx, sy);
                        break;
                    }
                    case EffectKind.DeathPuff: DrawDeath(g, w, fx, v, p, sx, sy); break;
                    case EffectKind.BossDeath: DrawBossDeath(g, w, fx, v, p, sx, sy); break;
                    case EffectKind.Heal: DrawHealSparkles(g, fx, v, w, now, p); break;
                    case EffectKind.LevelUp: DrawLevelUpAir(g, w, fx, v, now, p); break;
                    case EffectKind.Banner: break;      // HUD 에서 그린다
                }
                } finally { KindMs[(int)fx.Kind] += Ms() - t0; }
            }
            // 글자는 모든 연출 위에 (겹쳐도 읽히게 마지막에)
            double tt = Ms();
            for (int i = 0; i < w.Effects.Count; ++i)
            {
                ViewEffect fx = w.Effects[i];
                if (fx.Kind != EffectKind.Damage && fx.Kind != EffectKind.Heal && fx.Kind != EffectKind.Text) continue;
                DrawFloatingText(g, w, fx, v, fx.Progress(now), v.ToScreenX(fx.X), v.ToScreenY(fx.Y), now);
            }
            KindMs[23] += Ms() - tt;
        }

        private void DrawSlash(IGfx g, WorldModel w, ViewEffect fx, ViewState v, long now, float p, float sx, float sy)
        {
            Entity c = w.Find(fx.EntityId);
            float cx = fx.X, cy = fx.Y;
            if (c != null) c.PosAt(now, out cx, out cy);
            float ang = (float)(Math.Atan2(fx.Y - cy, fx.X - cx) * 180.0 / Math.PI);
            bool skill = fx.Variant == 1;
            float rr = v.Px(skill ? 1.9f : 1.35f);
            float ox = v.ToScreenX(cx) + (float)Math.Cos(ang * Math.PI / 180.0) * v.Px(0.6f);
            float oy = v.ToScreenY(cy) - v.Px(1.3f) + (float)Math.Sin(ang * Math.PI / 180.0) * v.Px(0.35f);
            float head = ang - 75 + 150 * (1f - (1f - p) * (1f - p));
            float fade = 1f - p;
            for (int k = 0; k < 4; ++k)
            {
                float tail = head - (70 - k * 14);
                float rk = rr * (1f - k * 0.045f);
                int a = (int)((skill ? 245 : 225) * fade * (1f - k * 0.2f));
                Pen sp = _res.Pen(a, 255, skill ? 226 : 255, skill ? 130 : 255, (skill ? 7f : 4.5f) - k * 1.1f);
                g.DrawArc(sp, ox - rk, oy - rk, rk * 2, rk * 2, tail, head - tail);
            }
            if (skill) Glow(g, Color.FromArgb(255, 220, 120), ox + (float)Math.Cos(head * Math.PI / 180.0) * rr, oy + (float)Math.Sin(head * Math.PI / 180.0) * rr, rr * 0.45f, rr * 0.45f, 0.8f * fade);
        }

        internal void DrawBurstDust(IGfx g, ViewEffect fx, ViewState v, float p, float sx, float sy)
        {
            float full = v.Px(fx.Radius);
            int n = fx.Radius >= 3f ? 16 : 8;
            for (int k = 0; k < n; ++k)
            {
                float a = Rnd(fx, k) * 6.2832f, sp = 0.45f + Rnd(fx, k + 9) * 0.55f;
                float d = full * sp * (1f - (1f - p) * (1f - p));
                float px = sx + (float)Math.Cos(a) * d, py = sy + (float)Math.Sin(a) * d * 0.8f - v.Px(0.3f + 0.9f * p * (0.4f + Rnd(fx, k + 3)));
                float r = v.Px(0.18f + 0.25f * Rnd(fx, k + 5)) * (0.6f + p);
                g.FillEllipse(_res.Brush((int)(150 * (1f - p)), 150, 130, 100), px - r, py - r, r * 2, r * 2);
            }
        }

        /// <summary>세로 빛기둥: 가운데가 밝고 양옆으로 사라지며, 위로 갈수록 옅어진다. 보스 연출에서만 쓰므로 브러시를 그때그때 만든다.</summary>
        public void Beam(IGfx g, Color c, float x, float bottomY, float topY, float halfW, float alpha)
        {
            if (alpha <= 0.01f || halfW < 1f) return;
            alpha = Math.Min(1f, alpha);
            const int Segs = 8;
            float segH = (bottomY - topY) / Segs;
            for (int i = 0; i < Segs; ++i)
                BeamSeg(g, c, x, topY + (Segs - 1 - i) * segH, segH + 1f, halfW, alpha * (1f - (float)i / Segs));      // 위로 갈수록 옅게
        }

        private static void BeamSeg(IGfx g, Color c, float x, float top, float h, float halfW, float alpha)
        {
            RectangleF r = new RectangleF(x - halfW, top, halfW * 2, h);
            using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.Transparent, Color.Transparent, 0f))
            {
                System.Drawing.Drawing2D.ColorBlend cb = new System.Drawing.Drawing2D.ColorBlend(5);
                cb.Positions = new float[] { 0f, 0.3f, 0.5f, 0.7f, 1f };
                cb.Colors = new Color[] { Color.FromArgb(0, c), Color.FromArgb((int)(150 * alpha), c), Color.FromArgb((int)(235 * alpha), c), Color.FromArgb((int)(150 * alpha), c), Color.FromArgb(0, c) };
                b.InterpolationColors = cb;
                g.FillRectangle(b, r);
            }
        }

        /// <summary>보스 사망: 몸이 터지는 섬광, 하늘로 솟는 빛기둥, 사방으로 퍼지는 충격 고리와 불씨.</summary>
        private void DrawBossDeath(IGfx g, WorldModel w, ViewEffect fx, ViewState v, float p, float sx, float sy)
        {
            Entity e = w.Find(fx.EntityId);
            float hh = e != null ? CreatureRenderer.LookOf(e).H : 3f, hw = e != null ? CreatureRenderer.LookOf(e).HalfW : 1.5f;
            float fl = BossFx.DeathFlash(p), pl = BossFx.DeathPillar(p);
            if (fl > 0f) { Glow(g, Color.FromArgb(255, 240, 200), sx, sy - v.Px(hh * 0.5f), v.Px(hw) * (1.6f + 3.0f * p), v.Px(hh) * (1.0f + 1.6f * p), Math.Min(1f, fl * 1.2f)); g.FillRectangle(_res.Brush((int)(120 * fl), 255, 245, 220), 0, 0, 4000, 4000); }
            if (pl > 0f)
            {
                float ph = v.Px(hh * 6f), pw = v.Px(hw) * (0.7f + 0.5f * pl);
                Beam(g, Color.FromArgb(255, 205, 110), sx, sy, sy - ph, pw * 1.5f, pl);
                Beam(g, Color.FromArgb(255, 252, 235), sx, sy, sy - ph, pw * 0.55f, pl);
                Glow(g, Color.FromArgb(255, 252, 235), sx, sy - ph * 0.5f, pw * 0.8f, ph * 0.5f, 1.0f * pl);
                Glow(g, Color.FromArgb(255, 240, 200), sx, sy - v.Px(hh * 0.3f), pw * 2.4f, v.Px(hh) * 0.8f, 0.8f * pl);
            }
            for (int k = 0; k < 3; ++k)
            {
                float q = (p - 0.06f - k * 0.10f) / 0.6f;
                if (q <= 0f || q >= 1f) continue;
                float rr = v.Px(hw) * (0.8f + 4.2f * q);
                g.DrawEllipse(_res.Pen((int)(230 * (1f - q)), 255, 220 - k * 30, 140 - k * 25, 5f * (1f - q) + 1f), sx - rr, sy - rr * 0.25f, rr * 2, rr * 0.5f);
            }
            for (int k = 0; k < 18; ++k)
            {
                float ang = Rnd(fx, k) * 6.2832f, sp = 0.5f + Rnd(fx, k + 40) * 0.9f;
                float q = Math.Max(0f, p - 0.08f) / 0.92f;
                float px = sx + (float)Math.Cos(ang) * v.Px(hw * 2.4f) * sp * q;
                float py = sy - v.Px(hh * 0.5f) + (float)Math.Sin(ang) * v.Px(hh * 1.2f) * sp * q - v.Px(1.2f) * q + v.Px(2f) * q * q;
                float r = v.Px(0.18f) * (1f - q);
                Glow(g, Color.FromArgb(255, 190, 90), px, py, r * 3f, r * 3f, 0.9f * (1f - q));
            }
        }

        private void DrawDeath(IGfx g, WorldModel w, ViewEffect fx, ViewState v, float p, float sx, float sy)
        {
            Entity de = w.Find(fx.EntityId);
            if (de != null && de.IsMonster) _mat.Burst(g, GameViewer.Core.Logic.HitMaterial.Of(de.TemplateId, true), MaterialFx.Seed(fx), sx, sy, p, v, de.IsBoss ? 3.5f : 2.2f);
            for (int k = 0; k < 4; ++k)
            {
                float r = v.Px(0.6f + p * (0.8f + k * 0.25f));
                float ox = (k - 1.5f) * v.Px(0.5f) * p, oy = -v.Px(0.5f + p * (0.8f + k * 0.3f));
                g.FillEllipse(_res.Brush((int)(130 * (1f - p)), 210, 210, 215), sx + ox - r / 2, sy + oy - r / 2, r, r);
            }
            for (int k = 0; k < 5; ++k)
            {
                float rise = v.Px(0.6f + 2.4f * p * (0.7f + Rnd(fx, k) * 0.6f));
                float sway = (float)Math.Sin(p * 7 + k * 2) * v.Px(0.35f) + (k - 2) * v.Px(0.28f);
                float r = v.Px(0.16f) * (1f - p * 0.5f);
                Glow(g, Color.FromArgb(190, 230, 255), sx + sway, sy - v.Px(0.8f) - rise, r * 3.2f, r * 3.2f, 0.9f * (1f - p));
            }
        }

        private void DrawHealSparkles(IGfx g, ViewEffect fx, ViewState v, WorldModel w, long now, float p)
        {
            float x = fx.X, y = fx.Y;
            Entity e = w.Find(fx.EntityId);
            if (e != null) e.PosAt(now, out x, out y);
            float sx = v.ToScreenX(x), sy = v.ToScreenY(y);
            for (int k = 0; k < 7; ++k)
            {
                float rise = v.Px(0.4f + 2.2f * p * (0.6f + Rnd(fx, k) * 0.6f));
                float dx = (Rnd(fx, k + 4) - 0.5f) * v.Px(1.8f);
                float s = Math.Max(2f, v.Px(0.17f)) * (1f - p * 0.6f);
                int a = (int)(240 * (1f - p));
                Pen pn = _res.Pen(a, 140, 255, 160, 2f);
                float px = sx + dx, py = sy - v.Px(0.3f) - rise;
                g.DrawLine(pn, px - s, py, px + s, py);
                g.DrawLine(pn, px, py - s, px, py + s);
            }
        }

        private void DrawProjectile(IGfx g, ViewerSession s, ViewEffect fx, ViewState v, long now)
        {
            float flight = Math.Max(1, fx.Value);
            float t = Math.Max(0f, Math.Min(1f, (now - fx.StartMs) / flight));
            float ex = fx.X2, ey = fx.Y2;
            SkillRow sk = s.Data.Skill(fx.SkillId);
            if (fx.TargetId != 0)
            {
                Entity target = s.World.Find(fx.TargetId);
                if (target != null) target.PosAt(now, out ex, out ey);
            }
            SkillFxInfo finfo = SkillFxTable.For(s.Data, fx.SkillId);
            if (finfo != null && _skill.Projectile(g, finfo, fx, v, now, t, ex, ey)) return;
            float h = v.Px(1.4f);
            float px = v.ToScreenX(fx.X + (ex - fx.X) * t), py = v.ToScreenY(fx.Y + (ey - fx.Y) * t) - h;
            bool arrow = sk != null && sk.ProjectileSpeed >= 50;
            if (arrow)
            {
                float t0 = Math.Max(0f, t - 0.1f);
                float qx = v.ToScreenX(fx.X + (ex - fx.X) * t0), qy = v.ToScreenY(fx.Y + (ey - fx.Y) * t0) - h;
                g.DrawLine(_res.Pen(70, 255, 245, 200, 6f), qx, qy, px, py);
                g.DrawLine(_res.Pen(255, 235, 215, 160, 2.5f), qx, qy, px, py);
                float dx = px - qx, dy = py - qy, l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l > 0.5f)
                {
                    dx /= l; dy /= l;
                    g.FillPolygon(_res.Brush(Color.FromArgb(230, 230, 240)), new PointF[] { new PointF(px + dx * 5, py + dy * 5), new PointF(px - dy * 3, py + dx * 3), new PointF(px + dy * 3, py - dx * 3) });
                }
                return;
            }
            float r = v.Px(0.5f);
            for (int k = 7; k >= 1; --k)
            {
                float tk = Math.Max(0f, t - k * 0.025f);
                float kx = v.ToScreenX(fx.X + (ex - fx.X) * tk) + ((Noise.Hash01(k, (int)(now / 50), 5) - 0.5f) * r * 0.8f);
                float ky = v.ToScreenY(fx.Y + (ey - fx.Y) * tk) - h + ((Noise.Hash01(k, (int)(now / 50), 6) - 0.5f) * r * 0.8f);
                float rk = r * (1f - k * 0.1f);
                Glow(g, Color.FromArgb(255, 110, 30), kx, ky, rk * 2.2f, rk * 2.2f, 0.7f * (1f - k * 0.12f));
            }
            Glow(g, Color.FromArgb(255, 160, 50), px, py, r * 3.4f, r * 3.4f, 0.95f);
            g.FillEllipse(_res.Brush(Color.FromArgb(255, 160, 50)), px - r, py - r, r * 2, r * 2);
            g.FillEllipse(_res.Brush(Color.FromArgb(255, 248, 200)), px - r * 0.55f, py - r * 0.55f, r * 1.1f, r * 1.1f);
        }

        // ------------------------------------------------------------------ 글자

        private void DrawFloatingText(IGfx g, WorldModel w, ViewEffect fx, ViewState v, float p, float sx, float sy, long now)
        {
            if (sx < -60 || sx > v.Width + 60 || sy < -60 || sy > v.Height + 140) return;
            if (fx.Kind == EffectKind.Damage && !v.Settings.ShowDamageNumbers) return;
            string text; Color color; Font font = _res.Bold; bool heavyTag = false;
            Entity tgt = fx.EntityId != 0 ? w.Find(fx.EntityId) : null;
            float topY = tgt != null ? CreatureRenderer.LookOf(tgt).H : 2.2f;
            if (fx.Kind == EffectKind.Damage)
            {
                text = fx.Value.ToString();
                switch (fx.Variant)
                {
                    case 1: color = Color.FromArgb(255, 170, 70); break;
                    case 2: color = Color.FromArgb(255, 80, 80); break;
                    case 3: color = Color.FromArgb(255, 235, 110); break;
                    default: color = Color.White; break;
                }
                int tier = DamageStyle.Tier(fx.Value, tgt != null ? tgt.MaxHp : 0);
                bool heavy = tier >= 2;
                heavyTag = heavy && fx.Variant == 3;
                if (tier >= 3 && fx.Variant == 3) color = Color.FromArgb(255, 150, 60);
                if (heavy) font = _res.Huge; else if (fx.Value >= 100) font = _res.Big;
                if (heavy) text = fx.Value + "!";
            }
            else if (fx.Kind == EffectKind.Heal) { text = "+" + fx.Value; color = Color.FromArgb(110, 255, 140); if (tgt != null && tgt.MaxHp > 0 && fx.Value * 100 >= tgt.MaxHp * 15) font = _res.Big; }
            else { text = fx.Text ?? ""; color = Color.FromArgb(170, 220, 255); font = _res.Small; }

            float rise = v.Px(fx.Kind == EffectKind.Text ? 1.0f : 1.8f) * (1f - (1f - p) * (1f - p));
            float jitter = ((fx.StartMs % 7) - 3) * 4f + (float)(Noise.Hash01((int)(fx.StartMs & 0xFFFF), (int)fx.Value, 9) - 0.5f) * 10f;
            if (fx.Kind == EffectKind.Damage && font == _res.Huge && p < 0.35f)
            {
                float gr = v.Px(1.8f) * (0.6f + p * 2f);
                Glow(g, fx.Variant == 2 ? Color.FromArgb(255, 70, 60) : Color.FromArgb(255, 210, 90), sx + jitter, sy - v.Px(topY * 0.75f) - rise, gr, gr, 0.9f * (1f - p / 0.35f));
            }
            int a = p < 0.65f ? 255 : (int)(255 * (1f - (p - 0.65f) / 0.35f));
            float px = sx + jitter, py = sy - v.Px(topY * 0.75f) - rise;
            if (fx.Kind == EffectKind.Heal)
            {
                float hs = DamageStyle.HealScale(fx.Value, tgt != null ? tgt.MaxHp : 0) * (1f + 0.3f * Math.Max(0f, 1f - p * 6f));
                GfxState gh = g.Save();
                g.TranslateTransform(px, py - v.Px(0.3f) * (float)Math.Sin(p * Math.PI));
                g.ScaleTransform(hs, hs);
                _res.OutlinedText(g, text, font, Color.FromArgb(Resources.Clamp(a), color), 0f, 0f, _res.Center);
                g.Restore(gh);
                return;
            }
            if (fx.Kind == EffectKind.Damage)
            {
                // 크기는 대상 최대 체력 대비 피해 비율에 따라 연속적으로 커지고, 맞는 순간 확 커졌다 줄어든다
                int dtier = DamageStyle.Tier(fx.Value, tgt != null ? tgt.MaxHp : 0);
                float scale = DamageStyle.Scale(fx.Value, tgt != null ? tgt.MaxHp : 0, dtier);
                px += DamageStyle.Shake(dtier, p) * (fx.Variant == 2 ? 1.3f : 1f);
                if (fx.Variant == 1) scale *= 0.85f;                                         // 지속 피해는 조금 작게
                scale *= 1f + 0.45f * Math.Max(0f, 1f - p * 7f);
                GfxState gs = g.Save();
                g.TranslateTransform(px, py);
                g.ScaleTransform(scale, scale);
                if (heavyTag) _res.OutlinedText(g, DamageStyle.Tag(dtier), _res.Small, Color.FromArgb(Resources.Clamp(a), 255, 190, 90), 0f, -15f, _res.Center);
                _res.OutlinedText(g, text, font, Color.FromArgb(Resources.Clamp(a), color), 0f, 0f, _res.Center);
                g.Restore(gs);
                return;
            }
            _res.OutlinedText(g, text, font, Color.FromArgb(Resources.Clamp(a), color), px, py, _res.Center);
        }

        // ------------------------------------------------------------------ 상태 오라

        /// <summary>엔티티 발 밑/몸 주변의 상태 표현(지속 피해 불씨, 버프 링, 약화 소용돌이, 보스 격노). 몸보다 먼저 그린다.</summary>
        public void DrawAura(IGfx g, ViewerSession s, Entity e, ViewState v, long now, float sx, float sy)
        {
            Look look = CreatureRenderer.LookOf(e);
            float hw = v.Px(look.HalfW * 0.55f);
            if (e.IsEnraged)
            {
                float pulse = 0.65f + 0.35f * (float)Math.Sin(now / 160.0);
                Glow(g, Color.FromArgb(255, 60, 40), sx, sy - v.Px(0.2f), hw * 1.9f, hw * 1.0f, pulse);
                for (int k = 0; k < 6; ++k)
                {
                    float ph = ((now / 700f) + k * 0.17f) % 1f;
                    float fx = sx + (Noise.Hash01(k, (int)(e.Id & 0xFF), 3) - 0.5f) * hw * 1.6f;
                    float fy = sy - ph * v.Px(look.H * 0.8f);
                    float r = v.Px(0.18f) * (1f - ph);
                    Glow(g, Color.FromArgb(255, 150, 50), fx, fy, r * 2.5f, r * 2.5f, 0.9f * (1f - ph));
                }
            }
            if (e.CastSkillId != 0)
            {
                SkillFxInfo ci = SkillFxTable.For(s.Data, e.CastSkillId);
                if (ci != null) _skill.Charge(g, ci, e, v, now, sx, sy, look.H, hw);
            }
            if (e.Statuses.Count == 0) return;
            int idx = 0;
            foreach (KeyValuePair<uint, StatusInstance> st in e.Statuses)
            {
                StatusRow row = s.Data.Status(st.Key);
                ++idx;
                if (row == null) continue;
                if (_skill.Status(g, row, v, now, idx, sx, sy, look.H, hw)) continue;
                bool dot = row.IsDot;
                bool debuff = !dot && (row.AttackPercent < 0 || row.DefensePercent < 0 || row.SpeedPercent < 0);
                if (e.IsEnraged && e.Boss != null && st.Key == e.Boss.EnrageStatusId) continue;
                if (dot)
                {
                    for (int k = 0; k < 4; ++k)
                    {
                        float ph = ((now / 600f) + k * 0.25f + idx * 0.1f) % 1f;
                        float px = sx + (Noise.Hash01(k + idx, (int)(e.Id & 0xFF), 4) - 0.5f) * hw * 1.4f;
                        float py = sy - v.Px(0.4f) - ph * v.Px(look.H * 0.7f);
                        float r = v.Px(0.13f) * (1f - ph * 0.5f);
                        int dc = SkillFxTable.DotColor(st.Key);
                        Glow(g, Color.FromArgb((dc >> 16) & 255, (dc >> 8) & 255, dc & 255), px, py, r * 2.4f, r * 2.4f, 0.9f * (1f - ph));
                    }
                }
                else if (debuff)
                {
                    float a0 = now / 300f + idx;
                    for (int k = 0; k < 3; ++k)
                    {
                        float a = a0 + k * 2.094f;
                        float px = sx + (float)Math.Cos(a) * hw * 0.9f, py = sy - v.Px(look.H * 0.5f) + (float)Math.Sin(a) * hw * 0.35f;
                        Glow(g, Color.FromArgb(170, 100, 220), px, py, v.Px(0.3f), v.Px(0.3f), 0.8f);
                    }
                }
                else
                {
                    float pulse = 0.55f + 0.3f * (float)Math.Sin(now / 220.0 + idx);
                    Glow(g, Color.FromArgb(90, 190, 255), sx, sy - v.Px(0.1f), hw * 1.5f, hw * 0.65f, pulse * 0.7f);
                    g.DrawEllipse(_res.Pen((int)(150 * pulse), 140, 220, 255, 2f), sx - hw * 1.1f, sy - hw * 0.4f, hw * 2.2f, hw * 0.8f);
                }
            }
        }
    }
}
