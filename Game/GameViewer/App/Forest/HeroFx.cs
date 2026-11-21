using System;
using System.Collections.Generic;

namespace GameViewer.App.Forest
{
    /// <summary>
    /// 영웅 선택 화면의 화려한 연출: 발밑 마법진(돌아가는 룬 고리), 위로 피어오르는 불씨, 공격 때 퍼지는 충격 고리.
    /// 모두 빛나는(Emis) 도형이라 기존 도형 목록 뒤에 그대로 붙여 한 번에 굽는다. 좌표는 영웅 모델 좌표(발 밑 중심, 키 약 3).
    /// </summary>
    public static class HeroFx
    {
        /// <summary>직업별 대표 빛깔(마법진, 불씨, 림 라이트가 같이 쓴다).</summary>
        public static V3 Aura(HeroKind k)
        {
            switch (k)
            {
                case HeroKind.Warrior: return new V3(1.00f, 0.62f, 0.18f);
                case HeroKind.Archer: return new V3(0.45f, 1.00f, 0.50f);
                case HeroKind.Mage: return new V3(0.62f, 0.50f, 1.00f);
                default: return new V3(0.40f, 0.85f, 1.00f);
            }
        }

        static readonly V3 Sh = new V3(1f, 1f, 1f);
        const float Y0 = 0.07f;                                           // 땅에서 살짝 띄워 겹침 깜빡임을 막는다

        static void Rune(List<HeroPrim> o, float r, float a, V3 half, V3 col)
        {
            o.Add(new HeroPrim { Kind = PrimKind.Box, C = new V3(r * (float)Math.Sin(a), Y0, r * (float)Math.Cos(a)), A = half, Col = col, R = Mat3.Rot('y', a), Sh = Sh, Spec = 0f, Emis = true, K = 1f });
        }

        static void Dot(List<HeroPrim> o, V3 c, float r, V3 col)
        {
            o.Add(new HeroPrim { Kind = PrimKind.Ellipsoid, C = c, A = new V3(r, r, r), Col = col, R = Mat3.Identity, Sh = Sh, Spec = 0f, Emis = true, K = 1f });
        }

        /// <param name="t">영웅이 나타난 뒤 흐른 시간(초)</param>
        /// <param name="pop">0..1, 나타나는 순간 마법진이 펼쳐지는 정도</param>
        /// <param name="atk">0..1, 공격 동작의 진행(충격 고리). 범위 밖이면 없음.</param>
        /// <param name="gear">false 면 몸에 붙는 장식(망토/날개/보석)을 뺀다.</param>
        public static List<HeroPrim> Build(HeroKind kind, float t, float pop, float atk, bool gear = true)
        {
            List<HeroPrim> o = new List<HeroPrim>(160);
            V3 c = Aura(kind);
            V3 bright = new V3(c.X * 0.80f + 0.20f, c.Y * 0.80f + 0.20f, c.Z * 0.80f + 0.20f);
            float k = pop * pop * (3f - 2f * pop);

            // 바깥 고리: 가는 점들이 천천히 돈다
            const int NOut = 56;
            float rOut = 1.32f * k;
            for (int i = 0; i < NOut; ++i)
                Rune(o, rOut, i * (float)(2 * Math.PI / NOut) + t * 0.25f, new V3(0.025f, 0.008f, 0.045f), c);

            // 가운데 룬 고리: 굵은 막대와 짧은 막대가 번갈아, 반대로 돈다
            const int NRune = 16;
            float rMid = 1.04f * k;
            for (int i = 0; i < NRune; ++i)
            {
                float a = i * (float)(2 * Math.PI / NRune) - t * 0.45f;
                bool big = (i & 1) == 0;
                Rune(o, rMid, a, big ? new V3(0.04f, 0.010f, 0.12f) : new V3(0.028f, 0.008f, 0.07f), big ? bright : c);
            }

            // 안쪽 고리: 촘촘한 점
            const int NIn = 36;
            float rIn = 0.80f * k;
            for (int i = 0; i < NIn; ++i)
                Rune(o, rIn, i * (float)(2 * Math.PI / NIn) + t * 0.6f, new V3(0.02f, 0.007f, 0.035f), c);

            // 불씨: 나선으로 올라가며 작아진다
            const int NSpark = 22;
            for (int i = 0; i < NSpark; ++i)
            {
                float ph = (t * 0.32f + i / (float)NSpark) % 1f;
                float a = t * 1.1f + i * 2.399963f;
                float r = (0.95f + 0.25f * (float)Math.Sin(i * 1.7f + t)) * (1f - 0.35f * ph);
                float s = 0.055f * (1f - ph) * (0.6f + 0.4f * (float)Math.Sin(i * 3.1f)) * (float)Math.Min(1.0, pop * 1.5f);
                if (s < 0.004f) continue;
                Dot(o, new V3(r * (float)Math.Sin(a), 0.15f + ph * 3.4f, r * (float)Math.Cos(a)), s, (i & 3) == 0 ? bright : c);
            }

            // 공격 충격 고리: 발밑에서 바깥으로 퍼지며 사라진다
            if (atk > 0f && atk < 1f)
            {
                float rr = 0.7f + 2.0f * atk, fade = (1f - atk) * (1f - atk);
                const int NW = 40;
                for (int i = 0; i < NW; ++i)
                    Rune(o, rr, i * (float)(2 * Math.PI / NW), new V3(0.05f * fade + 0.008f, 0.012f, 0.10f * fade + 0.008f), bright);
            }
            if (!gear) return o;                                   // 3D 영웅이 몸을 맡으면 망토/날개/보석은 그리지 않는다(고정 방향이라 영웅이 돌면 따로 논다)
            int g0 = o.Count;
            Gear(o, kind, t, pop, c, bright);
            for (int i = g0; i < o.Count; ++i) { HeroPrim p = o[i]; p.C = p.C + new V3(0f, 0.15f, 0f); o[i] = p; }      // 영웅 몸통 이하가 다리 길이만큼 올라가 있으니 같이 올린다
            return o;
        }

        // ------------------------------------------------------------------ 영웅 선택 화면 전용 장식(몸통/머리에 붙어 자세와 상관없이 같은 자리)
        static void Ell(List<HeroPrim> o, V3 c, V3 a, V3 col, Mat3 r, float spec, bool emis)
        {
            o.Add(new HeroPrim { Kind = PrimKind.Ellipsoid, C = c, A = a, Col = col, R = r ?? Mat3.Identity, Sh = new V3(0.5f, 0.52f, 0.7f), Spec = spec, Emis = emis, K = 1f });
        }

        static void Bar(List<HeroPrim> o, V3 p0, V3 p1, float r0, float r1, V3 col, int n, float spec, bool emis)
        {
            V3 d = p1 - p0; float L = d.Length; if (L < 1e-5f) return;
            Mat3 R = Mat3.FrameY(d);
            for (int k = 0; k < n; ++k)
            {
                float u = (k + 0.5f) / n, r = r0 + (r1 - r0) * u;
                Ell(o, p0 + d * u, new V3(r, L / n * 0.5f * 1.3f + r * 0.3f, r * 0.55f), col, R, spec, emis);
            }
        }

        static V3 C255(float r, float g, float b) { return new V3(r / 255f, g / 255f, b / 255f); }

        /// <summary>망토: 어깨에서 늘어져 살랑이는 천(여러 조각). 끝단은 금테.</summary>
        static void Cape(List<HeroPrim> o, float t, float top, float len, float w0, float w1, V3 col, V3 trim, float k)
        {
            const int N = 12;
            for (int i = 0; i < N; ++i)
            {
                float f = i / (float)(N - 1);
                float sway = (float)Math.Sin(t * 1.5f - f * 2.2f) * 0.07f * f + (float)Math.Sin(t * 0.7f + f) * 0.03f * f;
                V3 c = new V3(0f, top - f * len, -0.185f - f * 0.09f - sway * 0.6f);
                float w = (w0 + (w1 - w0) * f) * k;
                bool hem = i == N - 1;
                Ell(o, c, new V3(w, len / N * 1.05f, 0.020f), hem ? trim : col * (1f - 0.25f * f), Mat3.Rot('x', 0.12f + sway * 0.6f), hem ? 0.6f : 0.15f, false);
            }
        }

        static void Gear(List<HeroPrim> o, HeroKind kind, float t, float pop, V3 aura, V3 bright)
        {
            float k = (float)Math.Min(1.0, pop * 1.4);
            V3 gold = C255(240, 190, 80);
            if (kind == HeroKind.Warrior)
            {
                // 이마 장식(금 머리띠 + 직업색 보석)
                for (int i = 0; i < 30; ++i)
                {
                    float a = (float)(Math.PI * 2 * i / 30);
                    Ell(o, new V3(0.315f * (float)Math.Sin(a), 2.68f, -0.035f + 0.315f * (float)Math.Cos(a)), new V3(0.034f, 0.016f, 0.034f), gold * 0.85f, null, 0.9f, false);
                }
                Ell(o, new V3(0f, 2.69f, 0.292f), new V3(0.045f, 0.06f, 0.03f), bright, null, 0f, true);
            }
            if (kind != HeroKind.Mage) Ell(o, new V3(0f, 2.19f, 0.01f), new V3(0.20f, 0.028f, 0.15f), gold, null, 0.9f, false);   // 금 목깃
            switch (kind)
            {
                case HeroKind.Warrior:
                    Cape(o, t, 2.12f, 1.55f, 0.30f, 0.46f, C255(176, 30, 44), gold, k);
                    Ell(o, new V3(0f, 1.98f, 0.205f), new V3(0.05f, 0.06f, 0.025f), bright, null, 0f, true);                 // 가슴 보석
                    for (int s = -1; s <= 1; s += 2) Ell(o, new V3(s * 0.30f, 2.10f, -0.17f), new V3(0.06f, 0.05f, 0.06f), gold, null, 0.9f, false);  // 망토 고정쇠
                    break;
                case HeroKind.Archer:                                     // 후드 망토와 나뭇잎 장식은 모델에 이미 있다: 가슴 보석만
                    Ell(o, new V3(0f, 1.97f, 0.205f), new V3(0.04f, 0.05f, 0.022f), bright, null, 0f, true);
                    break;
                case HeroKind.Mage:
                    // 별빛 구슬 3개가 몸 둘레를 돈다
                    for (int i = 0; i < 3; ++i)
                    {
                        float a = t * 1.3f + i * 2.0944f;
                        V3 c = new V3(0.95f * (float)Math.Sin(a), 1.75f + 0.28f * (float)Math.Sin(t * 1.7f + i * 2f), 0.95f * (float)Math.Cos(a));
                        Ell(o, c, new V3(0.085f, 0.085f, 0.085f) * k, aura, null, 0f, true);
                        Ell(o, c, new V3(0.045f, 0.045f, 0.045f) * k, new V3(1f, 1f, 1f), null, 0f, true);
                    }
                    Ell(o, new V3(0f, 1.98f, 0.205f), new V3(0.045f, 0.055f, 0.022f), bright, null, 0f, true);
                    break;
                default:
                    // 등 뒤의 깃털 날개
                    for (int s = -1; s <= 1; s += 2)
                        for (int j = 0; j < 5; ++j)
                        {
                            float a = 0.30f + j * 0.22f + 0.05f * (float)Math.Sin(t * 1.3f + j * 0.4f);
                            float len = (1.00f - j * 0.13f) * k;
                            V3 b0 = new V3(s * 0.16f, 2.05f - j * 0.02f, -0.20f);
                            V3 dir = new V3(s * (float)Math.Sin(a) * 0.95f, (float)Math.Cos(a) * 0.85f - 0.10f, -0.42f).Normalized();
                            Bar(o, b0, b0 + dir * len, 0.045f, 0.012f, C255(240, 244, 252), 3, 0.5f, false);
                            Ell(o, b0 + dir * len, new V3(0.02f, 0.02f, 0.02f), aura, null, 0f, true);
                        }
                    break;
            }
        }
    }
}
