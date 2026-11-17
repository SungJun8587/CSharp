//***************************************************************************
// HeroModel.cs : 영웅 4종(전사/궁수/마법사/창기사)의 3D 모습. Tools/render_heroes.py 의 인체를 그대로 옮긴 것으로,
//   타원체/상자/원뿔을 모아 만들고(외부 모델 없음), 자세 값(걸음 위상, 공격 자세)만 바꿔 다시 짜면 대기/걷기/공격이 된다.
//   GL 과 무관한 순수 계산이라 테스트할 수 있다. 모델 좌표: 발 밑 y=0, 얼굴이 보는 쪽 +z, 영웅의 왼쪽 +x, 키 약 3(그릴 때 줄인다).
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.App.Forest
{
    public enum HeroKind { Warrior, Archer, Mage, Lancer }
    public enum PrimKind { Ellipsoid = 0, Box = 1, Cone = 2 }

    /// <summary>3x3 회전 행렬(행 우선). 열 = 그 부분의 로컬 x/y/z 축이 월드에서 향하는 방향.</summary>
    public sealed class Mat3
    {
        public readonly float[] M;
        public Mat3(float[] m) { M = m; }
        public static readonly Mat3 Identity = new Mat3(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });
        public static Mat3 Rot(char axis, float a)
        {
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            if (axis == 'x') return new Mat3(new float[] { 1, 0, 0, 0, c, -s, 0, s, c });
            if (axis == 'y') return new Mat3(new float[] { c, 0, s, 0, 1, 0, -s, 0, c });
            return new Mat3(new float[] { c, -s, 0, s, c, 0, 0, 0, 1 });
        }
        /// <summary>로컬 y 축이 d 방향인 회전.</summary>
        public static Mat3 FrameY(V3 d)
        {
            V3 y = d.Normalized();
            V3 t = Math.Abs(y.X) < 0.9f ? new V3(1, 0, 0) : new V3(0, 0, 1);
            V3 x = V3.Cross(t, y).Normalized();
            V3 z = V3.Cross(x, y);
            return new Mat3(new float[] { x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z });
        }
        public V3 Col(int j) { return new V3(M[j], M[3 + j], M[6 + j]); }
        public static Mat3 FromCols(V3 x, V3 y, V3 z) { return new Mat3(new float[] { x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z }); }
    }

    /// <summary>도형 하나. 타원체/상자는 A = 반축(x,y,z), 원뿔은 A = (밑 반지름, 윗 반지름, 높이)이고 K 는 z 방향 납작함.</summary>
    public struct HeroPrim
    {
        public PrimKind Kind; public V3 C, A, Col, Sh; public Mat3 R; public float K, Spec; public bool Emis;
    }

    /// <summary>공격 자세 한 장(값 의미는 render_heroes.py 의 ATK 와 같다). 팔은 (위팔 각, 굽힘, 벌림), pitch 는 무기가 위에서 앞으로 숙은 각도(도).</summary>
    public struct HeroPose
    {
        public V3 Arm, Free, DrawArm; public float Pitch, Draw;
        public static HeroPose Lerp(HeroPose a, HeroPose b, float t)
        {
            HeroPose r; r.Arm = V3.Lerp(a.Arm, b.Arm, t); r.Free = V3.Lerp(a.Free, b.Free, t); r.DrawArm = V3.Lerp(a.DrawArm, b.DrawArm, t);
            r.Pitch = a.Pitch + (b.Pitch - a.Pitch) * t; r.Draw = a.Draw + (b.Draw - a.Draw) * t; return r;
        }
    }

    public static class HeroModel
    {
        // ------------------------------------------------------------------ 공격 자세(5장)
        static HeroPose P(float a0, float a1, float a2, float pitch) { HeroPose p = new HeroPose(); p.Arm = new V3(a0, a1, a2); p.Pitch = pitch; return p; }
        static HeroPose Pf(float a0, float a1, float a2, float pitch, float f0, float f1, float f2) { HeroPose p = P(a0, a1, a2, pitch); p.Free = new V3(f0, f1, f2); return p; }
        static HeroPose Pa(float a0, float a1, float a2, float d0, float d1, float d2, float draw) { HeroPose p = P(a0, a1, a2, 0); p.DrawArm = new V3(d0, d1, d2); p.Draw = draw; return p; }

        public static readonly HeroPose[][] Attack =
        {
            new[] { Pf(2.60f, 0.90f, 0.22f, -25f, 0.50f, 0.50f, 0.45f), Pf(2.20f, 0.55f, 0.20f, 30f, 0.30f, 0.45f, 0.50f), Pf(1.60f, 0.35f, 0.18f, 85f, -0.10f, 0.40f, 0.55f), Pf(1.15f, 0.22f, 0.16f, 125f, -0.40f, 0.40f, 0.60f), Pf(0.85f, 0.15f, 0.16f, 150f, -0.60f, 0.40f, 0.62f) },   // Warrior
            new[] { Pa(1.40f, 0.10f, 0.10f, 1.55f, 2.30f, 0.14f, 1.0f), Pa(1.40f, 0.10f, 0.10f, 1.30f, 1.90f, 0.18f, 0.45f), Pa(1.38f, 0.12f, 0.10f, 0.00f, 1.00f, 0.24f, 0.12f), Pa(1.34f, 0.15f, 0.12f, -0.35f, 0.70f, 0.28f, 0f), Pa(1.20f, 0.22f, 0.16f, -0.45f, 0.55f, 0.30f, 0f) },   // Archer
            new[] { Pf(-0.20f, 0.90f, 0.30f, -4f, 0.35f, 0.60f, 0.35f), Pf(0.20f, 0.80f, 0.26f, 10f, 0.70f, 0.55f, 0.35f), Pf(0.70f, 0.55f, 0.22f, 28f, 1.05f, 0.45f, 0.35f), Pf(1.10f, 0.35f, 0.20f, 45f, 1.30f, 0.40f, 0.35f), Pf(1.30f, 0.25f, 0.18f, 55f, 1.45f, 0.35f, 0.35f) },   // Mage
            new[] { P(-0.80f, 1.00f, 0.18f, 74f), P(0.30f, 0.75f, 0.18f, 84f), P(1.35f, 0.12f, 0.10f, 90f), P(1.28f, 0.16f, 0.10f, 89f), P(0.45f, 0.65f, 0.18f, 58f) },   // Lancer
        };

        // ------------------------------------------------------------------ 걷기 보조 (render_heroes.py 와 같은 값)
        const float WalkStep = 1.0f, WalkArm = 0.50f, WalkShift = 0.045f;
        const float Leg1 = 0.66f, Leg2 = 0.68f, Ground = 0.11f, Stance = 0.6f, LegBend = 1.30f, Lift = 0.22f, Heel = 0.10f;

        static float WeightShift(float y, float shift) { return shift * Math.Min(1f, Math.Max(0f, (y - 0.1f) / 1.2f)); }
        static float LegT(float ph, int side) { double t = ph / (2 * Math.PI) + (side > 0 ? 0.0 : 0.5); t -= Math.Floor(t); return (float)t; }

        static void FootTarget(float t, out float z, out float y)
        {
            if (t < Stance)
            {
                float s = t / Stance, k = Math.Min(1f, Math.Max(0f, (s - 0.7f) / 0.3f));
                z = WalkStep * (0.5f - s); y = Ground + Heel * k * k * (3 - 2 * k); return;
            }
            float u = (t - Stance) / (1f - Stance);
            float f = u - 0.5f * (float)Math.Sin(2 * Math.PI * u) / (float)(2 * Math.PI);
            z = WalkStep * (-0.5f + f); y = Ground + Heel * (1 - u) * (1 - u) + Lift * (float)Math.Sin(Math.PI * u);
        }

        static float PelvisHeight(float ph)
        {
            float h = 0f;
            for (int side = 1; side >= -1; side -= 2)
            {
                float t = LegT(ph, side);
                if (t < Stance) { float z, y; FootTarget(t, out z, out y); h = Math.Max(h, y + (float)Math.Sqrt(LegBend * LegBend - z * z)); }
            }
            return h;
        }

        static void Ik(float hipY, float z, float y, out float kz, out float ky, out float phi)
        {
            float dz = z, dy = hipY - y;
            float d = Math.Min((float)Math.Sqrt(dz * dz + dy * dy), (Leg1 + Leg2) * 0.999f);
            float a = (float)Math.Atan2(dz, dy);
            float cb = Math.Max(-1f, Math.Min(1f, (Leg1 * Leg1 + d * d - Leg2 * Leg2) / (2 * Leg1 * d)));
            float th = a + (float)Math.Acos(cb);
            kz = Leg1 * (float)Math.Sin(th); ky = hipY - Leg1 * (float)Math.Cos(th);
            phi = (float)Math.Atan2(z - kz, ky - y);
        }

        static void LimbPose(float ph, int side, bool moving, out V3 hip, out V3 knee, out V3 ankle, out float phi)
        {
            hip = new V3(0.13f * side, 1.45f, 0f);
            if (!moving) { knee = hip + new V3(0, -Leg1, 0); ankle = knee + new V3(0, -Leg2, 0); phi = 0f; return; }
            float h = PelvisHeight(ph), z, y; FootTarget(LegT(ph, side), out z, out y);
            float kz, ky; Ik(h, z, y, out kz, out ky, out phi);
            float bob = h - 1.45f;
            knee = new V3(hip.X, ky - bob, kz); ankle = new V3(hip.X, y - bob, z);
        }

        /// <summary>팔 한쪽(어깨, 팔꿈치, 손). fixedArm 이 있으면 그 (위팔 각, 굽힘, 벌림) 로 고정.</summary>
        static void ArmPose(float ph, int side, bool moving, V3? fixedArm, out V3 sh, out V3 elbow, out V3 hand)
        {
            sh = new V3(0.30f * side, 2.02f, 0f);
            float th, bd, ab;
            if (fixedArm.HasValue) { th = fixedArm.Value.X; bd = fixedArm.Value.Y; ab = fixedArm.Value.Z; }
            else
            {
                th = moving ? -WalkArm * (float)Math.Cos(ph - 0.3f + (side > 0 ? 0f : (float)Math.PI)) : 0f;
                bd = 0.35f + (moving ? 0.6f * Math.Max(0f, th) : 0f); ab = 0.12f;
            }
            const float L1 = 0.42f, L2 = 0.40f;
            V3 dir1 = new V3(side * (float)Math.Sin(ab), -(float)Math.Cos(ab) * (float)Math.Cos(th), (float)Math.Cos(ab) * (float)Math.Sin(th));
            elbow = sh + dir1.Normalized() * L1;
            float th2 = th + bd;
            V3 dir2 = new V3(side * (float)Math.Sin(ab) * 0.6f, -(float)Math.Cos(th2), (float)Math.Sin(th2));
            hand = elbow + dir2.Normalized() * L2;
        }

        // ------------------------------------------------------------------ 도형 쌓기
        static V3 Col(float r, float g, float b) { return new V3(r / 255f, g / 255f, b / 255f); }
        static readonly V3 ShDef = new V3(0.52f, 0.55f, 0.76f), ShSkin = new V3(0.80f, 0.58f, 0.60f);
        static readonly V3 Skin = Col(255, 218, 196), SkinL = Col(255, 190, 178);
        static V3 Gray(float v) { return new V3(v, v, v); }

        sealed class B
        {
            public readonly List<HeroPrim> P = new List<HeroPrim>();
            public void E(V3 c, V3 a, V3 col, Mat3 R = null, V3? sh = null, float spec = 0f, bool emis = false)
            { P.Add(new HeroPrim { Kind = PrimKind.Ellipsoid, C = c, A = a, Col = col, R = R ?? Mat3.Identity, Sh = sh ?? ShDef, Spec = spec, Emis = emis, K = 1f }); }
            public void Box(V3 c, V3 h, V3 col, Mat3 R = null, V3? sh = null, float spec = 0f, bool emis = false)
            { P.Add(new HeroPrim { Kind = PrimKind.Box, C = c, A = h, Col = col, R = R ?? Mat3.Identity, Sh = sh ?? ShDef, Spec = spec, Emis = emis, K = 1f }); }
            public void Cone(V3 c, float r0, float r1, float H, V3 col, float k = 1f, Mat3 R = null, V3? sh = null, float spec = 0f)
            { P.Add(new HeroPrim { Kind = PrimKind.Cone, C = c, A = new V3(r0, r1, H), Col = col, R = R ?? Mat3.Identity, Sh = sh ?? ShDef, Spec = spec, K = k }); }
            /// <summary>p0 -> p1 를 잇는 가는 타원체(n 조각으로 굵기가 바뀜).</summary>
            public void Seg(V3 p0, V3 p1, float r0, float r1, V3 col, int n = 1, float rr = -1f, V3? sh = null, float spec = 0f, bool emis = false)
            {
                V3 d = p1 - p0; float L = d.Length; if (L < 1e-6f) return;
                Mat3 R = Mat3.FrameY(d);
                for (int k = 0; k < n; ++k)
                {
                    float t = (k + 0.5f) / n; float r = r0 + (r1 - r0) * t;
                    E(p0 + d * t, new V3(r, L / n / 2 * 1.35f + r * 0.35f, rr < 0 ? r : rr), col, R, sh, spec, emis);
                }
            }
        }

        static void Head(B b, V3 hair, V3 eye)
        {
            V3 hc = new V3(0, 2.52f, 0);
            b.E(hc, new V3(0.285f, 0.30f, 0.285f), Skin, sh: ShSkin);
            b.E(hc + new V3(0, -0.12f, 0.07f), new V3(0.215f, 0.20f, 0.21f), Skin, sh: ShSkin);          // 턱
            b.E(hc + new V3(0, -0.24f, 0), new V3(0.07f, 0.11f, 0.07f), Skin, sh: ShSkin);               // 목
            for (int s = -1; s <= 1; s += 2)
            {
                b.E(hc + new V3(s * 0.285f, -0.02f, -0.02f), new V3(0.04f, 0.07f, 0.05f), Skin, sh: ShSkin);   // 귀
                float ex = s * 0.122f, ey = 2.48f, ez = 0.232f;
                b.E(new V3(ex, ey, ez), new V3(0.077f, 0.100f, 0.036f), Col(250, 248, 250), sh: Gray(0.9f));                          // 흰자
                b.E(new V3(ex, ey - 0.005f, ez + 0.012f), new V3(0.063f, 0.088f, 0.031f), eye, sh: Gray(0.8f));                      // 홍채
                b.E(new V3(ex, ey - 0.012f, ez + 0.026f), new V3(0.034f, 0.056f, 0.021f), Col(40, 20, 34), sh: Gray(0.8f));           // 동공
                b.E(new V3(ex - s * 0.022f, ey + 0.035f, ez + 0.037f), new V3(0.019f, 0.022f, 0.013f), Col(255, 255, 255), emis: true);  // 하이라이트
                b.E(new V3(ex, ey + 0.088f, ez - 0.004f), new V3(0.082f, 0.018f, 0.03f), hair * 0.55f, sh: Gray(0.8f));               // 눈썹
                b.E(new V3(s * 0.19f, 2.42f, 0.2f), new V3(0.045f, 0.025f, 0.02f), Col(255, 160, 160), sh: Gray(0.95f));              // 볼터치
            }
            b.E(new V3(0, 2.386f, 0.270f), new V3(0.030f, 0.008f, 0.020f), Col(190, 84, 96), sh: Gray(0.9f));        // 입(턱 앞면 z=0.28 보다 앞으로 나와야 보인다): 작고 단정하게
            for (int s = -1; s <= 1; s += 2) b.E(new V3(s * 0.036f, 2.391f, 0.266f), new V3(0.009f, 0.006f, 0.012f), Col(190, 84, 96), sh: Gray(0.9f));   // 입꼬리
            b.E(new V3(0, 2.44f, 0.285f), new V3(0.012f, 0.018f, 0.012f), SkinL, sh: ShSkin);                        // 코
        }

        static void HairCap(B b, V3 hair, V3 hairL)
        {
            V3 hc = new V3(0, 2.52f, 0);
            b.E(hc + new V3(0, 0.045f, -0.035f), new V3(0.325f, 0.33f, 0.33f), hair, spec: 0.28f);
            for (int s = -1; s <= 1; ++s)
                b.E(hc + new V3(s * 0.15f, 0.17f, 0.2f), new V3(0.13f, 0.085f, 0.11f), hair, Mat3.Rot('z', -s * 0.5f), spec: 0.25f);   // 앞머리
            b.E(hc + new V3(0, 0.20f, 0.2f), new V3(0.11f, 0.07f, 0.10f), hairL, spec: 0.6f);
            for (int s = -1; s <= 1; s += 2) b.E(hc + new V3(s * 0.285f, -0.12f, 0.07f), new V3(0.065f, 0.24f, 0.085f), hair, spec: 0.5f);   // 옆머리
        }

        // ------------------------------------------------------------------ 한 자세의 영웅
        /// <summary>
        /// 영웅 한 명의 도형 목록. phase 는 걸음 위상(라디안), moving 이 false 면 서 있는 자세. pose 가 있으면 공격 자세(팔과 무기).
        /// 같은 영웅이면 자세가 달라도 도형 수와 순서가 같다(그래서 자세 사이를 도형마다 섞을 수 있다).
        /// </summary>
        public static List<HeroPrim> Build(HeroKind kind, float phase, bool moving, HeroPose? pose)
        {
            B b = new B();
            float ph = phase;
            float sway = moving ? (float)Math.Sin(ph) : 0f, bob = 0f, shift = 0f;
            if (moving) { bob = PelvisHeight(ph) - 1.45f; shift = WalkShift * (float)Math.Cos(ph - 0.6 * Math.PI); }

            V3 hair, hairL, cloth, cloth2, trim, metal, boot, eye;
            switch (kind)
            {
                case HeroKind.Warrior: hair = Col(206, 70, 40); hairL = Col(255, 130, 70); cloth = Col(196, 44, 52); cloth2 = Col(52, 38, 44); trim = Col(236, 176, 70); metal = Col(180, 186, 200); boot = Col(96, 62, 50); eye = Col(70, 170, 130); break;
                case HeroKind.Archer: hair = Col(238, 220, 168); hairL = Col(255, 246, 210); cloth = Col(70, 150, 84); cloth2 = Col(236, 228, 196); trim = Col(206, 160, 70); metal = Col(170, 150, 120); boot = Col(112, 78, 54); eye = Col(80, 190, 230); break;
                case HeroKind.Lancer: hair = Col(238, 200, 98); hairL = Col(255, 236, 160); cloth = Col(184, 192, 206); cloth2 = Col(36, 124, 134); trim = Col(226, 188, 88); metal = Col(206, 214, 228); boot = Col(120, 130, 148); eye = Col(80, 150, 230); break;
                default: hair = Col(190, 186, 238); hairL = Col(236, 232, 255); cloth = Col(76, 92, 214); cloth2 = Col(40, 40, 110); trim = Col(250, 214, 110); metal = Col(200, 205, 225); boot = Col(52, 48, 100); eye = Col(170, 80, 220); break;
            }
            bool lancer = kind == HeroKind.Lancer;
            V3? pants = lancer ? (V3?)Col(74, 88, 104) : null;

            // ---- 다리
            for (int s = 1; s >= -1; s -= 2)
            {
                V3 hip, knee, ankle; float phi; LimbPose(ph, s, moving, out hip, out knee, out ankle, out phi);
                V3 up = new V3(0, bob, 0); hip = hip + up; knee = knee + up; ankle = ankle + up;
                b.Seg(hip, knee, 0.118f, 0.092f, pants ?? Skin, 3, sh: pants.HasValue ? ShDef : ShSkin);
                b.Seg(knee + (ankle - knee) * 0.02f, ankle, 0.085f, 0.075f, boot, 2, spec: 0.3f);
                b.E(knee, new V3(0.095f, 0.075f, 0.095f), boot, spec: 0.3f);
                b.E(ankle + new V3(0, -0.04f, 0.085f), new V3(0.075f, 0.062f, 0.15f), boot * 0.9f, Mat3.Rot('x', -phi * 0.6f), spec: 0.3f);
                b.E(ankle + new V3(0, -0.075f, 0.12f), new V3(0.062f, 0.03f, 0.14f), cloth2 * 0.7f, Mat3.Rot('x', -phi * 0.6f));
                b.E(knee + (ankle - knee) * 0.25f, new V3(0.095f, 0.05f, 0.095f), trim, Mat3.FrameY(ankle - knee));
            }
            int nlegs = b.P.Count;

            // ---- 몸통
            b.E(new V3(0, 1.36f + bob, 0), new V3(0.25f, 0.17f, 0.19f), cloth2);
            b.E(new V3(0, 1.62f + bob, 0), new V3(0.19f, 0.22f, 0.14f), Skin, sh: ShSkin);
            b.E(new V3(0, 1.93f + bob, 0.005f), new V3(0.235f, 0.20f, 0.17f), cloth, spec: 0.1f);
            b.E(new V3(0, 1.95f + bob, 0.115f), new V3(0.19f, 0.115f, 0.085f), cloth * 1.02f);
            b.E(new V3(0, 2.15f + bob, 0), new V3(0.17f, 0.08f, 0.12f), Skin, sh: ShSkin);
            b.E(new V3(0, 1.50f + bob, 0), new V3(0.265f, 0.05f, 0.205f), cloth2 * 0.9f, spec: 0.2f);
            b.E(new V3(0, 1.50f + bob, 0.20f), new V3(0.06f, 0.06f, 0.03f), trim, spec: 0.8f);

            // ---- 치마/로브
            if (kind == HeroKind.Mage)
            {
                b.Cone(new V3(0, 0.36f + bob, 0), 0.56f, 0.25f, 1.10f, cloth, 0.72f, spec: 0.1f);
                b.E(new V3(0, 0.36f + bob, 0), new V3(0.56f, 0.035f, 0.40f), trim, spec: 0.5f);
                b.E(new V3(0, 0.88f + bob, 0.285f), new V3(0.06f, 0.45f, 0.05f), cloth2);
                b.E(new V3(0, 0.88f + bob, 0.295f), new V3(0.015f, 0.45f, 0.03f), trim, spec: 0.8f);
            }
            else if (kind == HeroKind.Archer)
            {
                b.Cone(new V3(0, 1.08f + bob, 0), 0.385f, 0.26f, 0.40f, cloth, 0.85f, spec: 0.1f);
                b.E(new V3(0, 1.07f + bob, 0), new V3(0.385f, 0.05f, 0.325f), cloth2, spec: 0.1f);
                b.E(new V3(0, 1.20f + bob, 0.23f), new V3(0.10f, 0.19f, 0.06f), trim, spec: 0.3f);
            }
            else
            {
                b.Cone(new V3(0, 1.09f + bob, 0), 0.37f, 0.26f, 0.38f, cloth, 0.85f, spec: 0.1f);
                b.E(new V3(0, 1.09f + bob, 0), new V3(0.37f, 0.045f, 0.31f), cloth2, spec: 0.2f);
                for (int s = -1; s <= 1; s += 2) b.E(new V3(s * 0.22f, 1.13f + bob, 0.18f), new V3(0.09f, 0.17f, 0.05f), cloth2 * 1.3f, Mat3.Rot('z', s * 0.2f));
            }

            // ---- 팔과 무기
            V3 har = default(V3);                                   // 궁수: 시위를 당기는 손
            if (kind == HeroKind.Archer && pose.HasValue)
            {
                V3 s0, e0, h0; ArmPose(ph, -1, moving, pose.Value.DrawArm, out s0, out e0, out h0); har = h0 + new V3(0, bob, 0);
            }
            for (int s = 1; s >= -1; s -= 2)
            {
                V3? fixedArm = null;
                if ((kind == HeroKind.Warrior || lancer) && s == -1) fixedArm = pose.HasValue ? pose.Value.Arm : new V3(-0.35f, 0.55f, 0.20f);
                if (kind == HeroKind.Warrior && s == 1 && pose.HasValue) fixedArm = pose.Value.Free;
                if (kind == HeroKind.Archer && s == 1) fixedArm = pose.HasValue ? pose.Value.Arm : new V3(0.85f, 0.40f, 0.30f);
                if (kind == HeroKind.Archer && s == -1 && pose.HasValue) fixedArm = pose.Value.DrawArm;
                if (kind == HeroKind.Mage && s == -1) fixedArm = pose.HasValue ? pose.Value.Arm : new V3(-0.20f, 0.9f, 0.30f);
                if (kind == HeroKind.Mage && s == 1 && pose.HasValue) fixedArm = pose.Value.Free;
                V3 sh0, el0, ha0; ArmPose(ph, s, moving, fixedArm, out sh0, out el0, out ha0);
                V3 upb = new V3(0, bob, 0); V3 sh_ = sh0 + upb, el = el0 + upb, ha = ha0 + upb;
                bool mage = kind == HeroKind.Mage;
                V3 sleeve = (kind == HeroKind.Archer) ? cloth : cloth2;
                b.Seg(sh_, el, 0.072f, 0.060f, Skin, 2, sh: ShSkin);
                b.Seg(el, ha, 0.060f, 0.048f, mage ? sleeve : Skin, 2, sh: mage ? ShDef : ShSkin);
                if (kind == HeroKind.Warrior || lancer) b.Seg(el + (ha - el) * 0.25f, ha, 0.062f, 0.058f, metal, 1, spec: 0.7f);
                if (mage) b.E(ha + (el - ha) * 0.3f, new V3(0.1f, 0.12f, 0.1f), cloth, Mat3.FrameY(ha - el), spec: 0.1f);
                b.E(ha, new V3(0.060f, 0.060f, 0.060f), Skin, sh: ShSkin);
                switch (kind)
                {
                    case HeroKind.Warrior:
                        b.E(sh_ + new V3(s * 0.04f, 0.03f, 0), new V3(0.15f, 0.10f, 0.15f), cloth2, spec: 0.6f);
                        b.E(sh_ + new V3(s * 0.07f, 0.07f, 0), new V3(0.12f, 0.05f, 0.12f), metal, spec: 0.8f);
                        b.Seg(sh_ + new V3(s * 0.1f, 0.08f, 0), sh_ + new V3(s * 0.2f, 0.28f, 0), 0.035f, 0.005f, metal, spec: 0.9f);
                        b.Seg(sh_ + new V3(s * 0.12f, 0.05f, 0.1f), sh_ + new V3(s * 0.26f, 0.12f, 0.2f), 0.03f, 0.004f, metal, spec: 0.9f);
                        break;
                    case HeroKind.Lancer:
                        b.E(sh_ + new V3(s * 0.04f, 0.03f, 0), new V3(0.15f, 0.10f, 0.15f), cloth, spec: 0.8f);
                        b.E(sh_ + new V3(s * 0.07f, 0.07f, 0), new V3(0.12f, 0.05f, 0.12f), trim, spec: 0.8f);
                        break;
                    case HeroKind.Archer:
                        b.E(sh_ + new V3(s * 0.03f, 0.02f, 0), new V3(0.12f, 0.08f, 0.12f), cloth * 0.95f, spec: 0.1f);
                        break;
                    default:
                        b.E(sh_ + new V3(s * 0.04f, 0.04f, 0), new V3(0.14f, 0.09f, 0.14f), cloth2, spec: 0.3f);
                        b.E(sh_ + new V3(s * 0.06f, 0.07f, 0), new V3(0.11f, 0.035f, 0.11f), trim, spec: 0.8f);
                        break;
                }

                if (kind == HeroKind.Warrior && s == -1)
                {
                    V3 d = pose.HasValue ? DirPitch(pose.Value.Pitch, -0.30f) : new V3(-0.45f, -0.86f, 0.18f);
                    d = d.Normalized(); Mat3 R = Mat3.FrameY(d);
                    b.E(ha - d * 0.18f, new V3(0.04f, 0.2f, 0.04f), Col(70, 40, 34), R);
                    b.E(ha - d * 0.38f, new V3(0.065f, 0.065f, 0.065f), trim, R, spec: 0.8f);
                    b.Box(ha + d * 0.05f, new V3(0.2f, 0.035f, 0.05f), trim, R, spec: 0.8f);
                    b.Box(ha + d * 0.12f + d * 0.56f, new V3(0.100f, 0.56f, 0.028f), Col(214, 222, 236), R, spec: 1.0f);
                    b.Box(ha + d * 0.12f + d * 0.56f, new V3(0.032f, 0.54f, 0.034f), Col(140, 150, 176), R, spec: 1.0f);
                    b.Box(ha + d * 0.12f + d * 0.56f, new V3(0.104f, 0.52f, 0.008f), Col(255, 205, 130), R, emis: true);   // 검날 가장자리의 열기
                    b.E(ha + d * 1.24f, new V3(0.095f, 0.1f, 0.028f), Col(214, 222, 236), R, spec: 1.0f);
                }
                if (lancer && s == -1)
                {
                    float phi = pose.HasValue ? pose.Value.Pitch * (float)Math.PI / 180f : 0f;
                    V3 d = new V3(-0.06f * (float)Math.Cos(phi) + 0.10f * (float)Math.Sin(phi), (float)Math.Cos(phi), 0.10f * (float)Math.Cos(phi) + (float)Math.Sin(phi)).Normalized();
                    Mat3 R = Mat3.FrameY(d);
                    b.Seg(ha - d * 1.0f, ha + d * 1.75f, 0.032f, 0.032f, Col(120, 84, 60), 3, spec: 0.6f);
                    b.E(ha + d * 0.05f, new V3(0.05f, 0.05f, 0.05f), trim, spec: 0.9f);
                    b.E(ha + d * 1.78f, new V3(0.055f, 0.04f, 0.055f), trim, R, spec: 0.9f);
                    b.Cone(ha + d * 1.80f, 0.06f, 0f, 0.50f, Col(226, 234, 246), 0.45f, R, spec: 1.0f);
                    b.E(ha + d * 1.80f + new V3(0, 0, 0.05f), new V3(0.012f, 0.2f, 0.012f), Col(150, 160, 186), R, spec: 1.0f);
                    b.E(ha - d * 1.0f, new V3(0.045f, 0.06f, 0.045f), trim, spec: 0.8f);
                    b.E(ha + d * 1.78f, new V3(0.034f, 0.034f, 0.034f), Col(150, 240, 255), emis: true);                              // 창 마디의 보석
                    b.E(ha + d * 2.02f, new V3(0.016f, 0.20f, 0.022f), Col(170, 245, 255), R, emis: true);                            // 창날 가운데의 푸른 빛
                }
                if (kind == HeroKind.Archer && s == 1)
                {
                    float draw = pose.HasValue ? pose.Value.Draw : 0f;
                    const int K = 12;
                    for (int k = 0; k <= K; ++k)
                    {
                        float t = (k / (float)K) * 2 - 1;
                        float y = ha.Y + t * 0.9f, x = ha.X + 0.05f + (1 - t * t) * 0.30f;
                        b.E(new V3(x, y, ha.Z), new V3(0.045f, 0.095f, 0.04f), Math.Abs(t) > 0.12f ? Col(150, 100, 60) : Col(60, 38, 30), Mat3.FrameY(new V3(2 * t * 0.30f * (2f / K), 0.18f, 0)), spec: 0.6f);
                    }
                    V3 mid = new V3(ha.X + 0.05f, ha.Y, ha.Z);
                    V3 nock = draw > 0 ? mid + (har - mid) * draw : mid;
                    V3 topEnd = new V3(ha.X + 0.05f, ha.Y + 0.9f, ha.Z), botEnd = new V3(ha.X + 0.05f, ha.Y - 0.9f, ha.Z);
                    b.Seg(topEnd, nock, 0.009f, 0.009f, Col(240, 238, 226), 2, emis: true);
                    b.Seg(nock, botEnd, 0.009f, 0.009f, Col(240, 238, 226), 2, emis: true);
                    // 화살: 당겼을 때만 보이지만, 도형 수가 늘 같도록 안 보일 때도 크기 0 으로 넣어 둔다
                    bool arrow = draw > 0.5f;
                    V3 tipA = mid + new V3(0, 0, 0.95f); V3 dirA = (tipA - nock).Normalized();
                    int before = b.P.Count;
                    b.Seg(nock, tipA, 0.016f, 0.016f, Col(214, 200, 170), 2, spec: 0.3f);
                    b.Cone(tipA, 0.04f, 0f, 0.16f, Col(220, 228, 240), 0.45f, Mat3.FrameY(dirA), spec: 1.0f);
                    b.E(tipA + dirA * 0.02f, new V3(0.045f, 0.045f, 0.045f), Col(190, 255, 190), emis: true);                    // 화살촉의 빛
                    for (int sg = -1; sg <= 1; sg += 2) b.E(nock + dirA * 0.06f + new V3(0, sg * 0.03f, 0), new V3(0.03f, 0.05f, 0.012f), Col(120, 200, 120), spec: 0.1f);
                    if (!arrow) for (int i = before; i < b.P.Count; ++i) { HeroPrim p = b.P[i]; p.A = new V3(0, 0, 0); b.P[i] = p; }
                    b.E(new V3(ha.X + 0.05f, ha.Y + 0.92f, ha.Z), new V3(0.04f, 0.05f, 0.04f), trim, spec: 0.8f);
                    b.E(new V3(ha.X + 0.05f, ha.Y - 0.92f, ha.Z), new V3(0.04f, 0.05f, 0.04f), trim, spec: 0.8f);
                    b.E(new V3(ha.X + 0.05f, ha.Y + 0.92f, ha.Z), new V3(0.022f, 0.028f, 0.022f), Col(170, 255, 170), emis: true);    // 활 끝의 숲 보석
                    b.E(new V3(ha.X + 0.05f, ha.Y - 0.92f, ha.Z), new V3(0.022f, 0.028f, 0.022f), Col(170, 255, 170), emis: true);
                    b.E(new V3(ha.X + 0.12f, ha.Y, ha.Z), new V3(0.026f, 0.07f, 0.03f), Col(170, 255, 170), emis: true);               // 손잡이의 룬
                }
                if (mage && s == -1)
                {
                    V3 top, bot;
                    if (pose.HasValue)
                    {
                        V3 dm = DirPitch(pose.Value.Pitch, -0.11f).Normalized(); top = ha + dm * 1.41f; bot = ha - dm * 1.2f;
                    }
                    else { top = ha + new V3(-0.16f, 1.40f, 0.04f); bot = ha - new V3(-0.10f, 1.2f, 0.04f); }
                    b.Seg(bot, top, 0.032f, 0.032f, Col(120, 84, 60), 3, spec: 0.6f);
                    b.E(top, new V3(0.075f, 0.075f, 0.075f), trim, spec: 0.9f);
                    for (int k = 0; k < 3; ++k)
                    {
                        float a = k * 2.1f;
                        b.Seg(top, top + new V3((float)Math.Cos(a) * 0.14f, 0.2f, (float)Math.Sin(a) * 0.14f), 0.03f, 0.01f, trim, spec: 0.9f);
                    }
                    b.E(top + new V3(0, 0.19f, 0), new V3(0.075f, 0.12f, 0.075f), Col(140, 230, 255), emis: true);
                    b.E(top + new V3(0, 0.19f, 0), new V3(0.05f, 0.2f, 0.05f), Col(200, 250, 255), Mat3.Rot('z', 0.5f), emis: true);
                    for (int k = 0; k < 6; ++k)                      // 마력 고리: 구슬 둘레의 작은 별빛 6개
                    {
                        float a = k * 1.0472f;
                        b.E(top + new V3((float)Math.Cos(a) * 0.17f, 0.19f + (float)Math.Sin(a * 2f) * 0.03f, (float)Math.Sin(a) * 0.17f), new V3(0.022f, 0.022f, 0.022f), Col(220, 245, 255), emis: true);
                    }
                }
            }

            // ---- 머리와 머리카락 (걸을 때의 위아래 흔들림 bob 만큼 올린다)
            int n0 = b.P.Count;
            Head(b, hair, eye); HairCap(b, hair, hairL);
            for (int i = n0; i < b.P.Count; ++i) { HeroPrim p = b.P[i]; p.C = p.C + new V3(0, bob, 0); b.P[i] = p; }
            float sw = sway * 0.06f;
            if (kind == HeroKind.Warrior)
            {
                V3 bs = new V3(0, 2.74f + bob, -0.2f);            // 높이 묶은 포니테일
                b.E(bs + new V3(0, 0.02f, 0.03f), new V3(0.09f, 0.08f, 0.09f), trim, spec: 0.9f);
                for (int k = 0; k < 15; ++k)
                {
                    float t = k / 14.0f;
                    V3 c = bs + new V3(sw * t * 2.0f, 0.12f - 0.95f * (float)Math.Pow(t, 1.3) + 0.18f * (float)Math.Sin(t * 3.0), -0.12f - 0.42f * (float)Math.Sin(t * 2.2) - 0.05f * t);
                    float r = 0.115f * (1 - 0.6f * t);
                    b.E(c, new V3(r, 0.11f, r * 0.95f), k % 2 == 0 ? hair : hair * 0.94f, Mat3.Rot('x', 0.7f * (1 - t)), spec: 0.6f);
                }
                b.E(new V3(0, 2.62f + bob, -0.3f), new V3(0.2f, 0.22f, 0.12f), hair);
            }
            else
            {
                b.Cone(new V3(sw * 1.6f, 1.50f + bob, -0.27f), 0.12f, 0.30f, 1.12f, hair, 0.72f, Mat3.Rot('x', 0.06f), spec: 0.28f);     // 긴 생머리
                b.E(new V3(sw * 1.6f, 1.50f + bob, -0.31f), new V3(0.10f, 0.09f, 0.07f), hair, spec: 0.28f);
                for (int s = -1; s <= 1; s += 2) b.E(new V3(s * 0.26f, 2.20f + bob, 0.1f), new V3(0.055f, 0.24f, 0.07f), hair, spec: 0.6f);
            }
            if (kind == HeroKind.Archer)
            {
                for (int k = 0; k < 5; ++k)                         // 후드 망토
                {
                    float t = k / 4.0f;
                    b.E(new V3(0, 2.0f - 0.75f * t + bob, -0.2f - 0.06f * t - sway * 0.04f * t), new V3(0.27f + 0.1f * t, 0.2f, 0.05f), cloth * (0.9f - 0.1f * t), Mat3.Rot('x', 0.15f - 0.15f * t));
                }
                b.E(new V3(0, 2.2f + bob, -0.12f), new V3(0.3f, 0.1f, 0.12f), cloth * 0.9f);
                Mat3 Rq = Mat3.Rot('z', 0.35f);                      // 화살통
                b.E(new V3(0.16f, 1.95f + bob, -0.3f), new V3(0.075f, 0.33f, 0.075f), Col(120, 80, 54), Rq, spec: 0.3f);
                for (int k = 0; k < 3; ++k)
                {
                    b.Seg(new V3(0.17f + k * 0.03f, 2.25f + bob, -0.3f + k * 0.02f), new V3(0.2f + k * 0.03f, 2.5f + bob, -0.3f + k * 0.02f), 0.012f, 0.012f, Col(210, 200, 180));
                    b.E(new V3(0.2f + k * 0.03f, 2.52f + bob, -0.3f + k * 0.02f), new V3(0.03f, 0.05f, 0.012f), cloth, spec: 0.1f);
                }
                for (int s = -1; s <= 1; s += 2) b.E(new V3(s * 0.21f, 2.74f + bob, 0.15f), new V3(0.05f, 0.12f, 0.025f), Col(110, 200, 110), Mat3.Rot('z', s * 0.9f), spec: 0.4f);   // 나뭇잎 머리장식
            }
            if (lancer)
            {
                for (int k = 0; k < 6; ++k)                         // 청록색 너덜너덜한 망토
                {
                    float t = k / 5.0f;
                    b.E(new V3(0, 2.05f - 0.80f * t + bob, -0.22f - 0.08f * t - sway * 0.05f * t), new V3(0.27f + 0.10f * t, 0.2f, 0.05f), cloth2 * (0.95f - 0.15f * t), Mat3.Rot('x', 0.15f - 0.2f * t));
                }
                float[] xs = { -0.3f, -0.12f, 0.08f, 0.28f };
                for (int j = 0; j < 4; ++j)
                {
                    float L = 0.28f + 0.1f * (j % 2);
                    b.E(new V3(xs[j], 1.12f - L / 2 + bob, -0.46f - sway * 0.05f), new V3(0.07f, L, 0.04f), cloth2 * 0.78f, Mat3.Rot('x', -0.2f));
                }
                b.E(new V3(0, 2.2f + bob, -0.12f), new V3(0.3f, 0.1f, 0.12f), cloth2 * 0.9f);
                b.E(new V3(-0.3f, 2.14f + bob, 0.02f), new V3(0.1f, 0.1f, 0.08f), cloth2);
                b.E(new V3(0, 2.72f + bob, 0.1f), new V3(0.30f, 0.025f, 0.27f), trim, Mat3.Rot('x', 0.15f), spec: 0.9f);                  // 서클릿
                b.E(new V3(0, 2.74f + bob, 0.3f), new V3(0.035f, 0.05f, 0.02f), Col(90, 200, 220), emis: true);
                b.E(new V3(0, 1.98f + bob, 0.19f), new V3(0.05f, 0.06f, 0.02f), Col(90, 200, 220), emis: true);                           // 흉갑 보석
            }
            if (kind == HeroKind.Mage)
            {
                V3 hat = Col(52, 66, 170);
                b.E(new V3(0, 2.78f + bob, 0), new V3(0.50f, 0.04f, 0.47f), hat, Mat3.Rot('x', 0.1f), spec: 0.2f);
                b.E(new V3(0, 2.78f + bob, 0), new V3(0.36f, 0.05f, 0.34f), trim, Mat3.Rot('x', 0.1f), spec: 0.8f);
                b.Cone(new V3(0, 2.82f + bob, -0.01f), 0.31f, 0.03f, 0.62f, hat, 0.95f, Mat3.Rot('x', -0.32f), spec: 0.2f);
                b.E(new V3(0, 3.40f + bob, -0.21f), new V3(0.04f, 0.04f, 0.04f), trim, spec: 0.9f);
                b.E(new V3(0, 2.9f + bob, 0.21f), new V3(0.06f, 0.06f, 0.03f), Col(255, 220, 120), emis: true);
                b.E(new V3(0, 1.30f + bob, -0.24f - 0.04f * sway), new V3(0.3f, 0.7f, 0.05f), cloth2 * 0.9f, Mat3.Rot('x', 0.1f));
            }

            // ---- 체중 이동, 그리고 몸통 이하를 다리 길이만큼 올린다
            if (shift != 0f)
                for (int i = 0; i < b.P.Count; ++i) { HeroPrim p = b.P[i]; p.C = p.C + new V3(WeightShift(p.C.Y, shift), 0f, 0f); b.P[i] = p; }
            for (int i = nlegs; i < b.P.Count; ++i) { HeroPrim p = b.P[i]; p.C = p.C + new V3(0, 0.15f, 0); b.P[i] = p; }
            return b.P;
        }

        static V3 DirPitch(float pitchDeg, float x) { float pa = pitchDeg * (float)Math.PI / 180f; return new V3(x, (float)Math.Cos(pa), (float)Math.Sin(pa)); }

        // ------------------------------------------------------------------ 자세 사이 섞기
        static V3 Orth(V3 a, V3 b, V3 c, out V3 y, out V3 z)
        {
            V3 x = a.Normalized(); y = (b - x * V3.Dot(b, x)); y = y.Length < 1e-5f ? V3.Cross(c, x) : y.Normalized(); z = V3.Cross(x, y); return x;
        }

        /// <summary>같은 영웅의 두 자세(도형 수와 순서가 같음)를 t(0..1)만큼 섞는다. 이동/크기/색은 보간, 회전은 축을 보간해 다시 직교화한다.</summary>
        public static List<HeroPrim> Blend(List<HeroPrim> a, List<HeroPrim> b, float t)
        {
            if (a.Count != b.Count) return t < 0.5f ? a : b;
            List<HeroPrim> r = new List<HeroPrim>(a.Count);
            for (int i = 0; i < a.Count; ++i)
            {
                HeroPrim p = a[i], q = b[i], o = p;
                o.C = V3.Lerp(p.C, q.C, t); o.A = V3.Lerp(p.A, q.A, t); o.Col = V3.Lerp(p.Col, q.Col, t); o.Sh = V3.Lerp(p.Sh, q.Sh, t);
                o.K = p.K + (q.K - p.K) * t; o.Spec = p.Spec + (q.Spec - p.Spec) * t;
                V3 x = V3.Lerp(p.R.Col(0), q.R.Col(0), t), y = V3.Lerp(p.R.Col(1), q.R.Col(1), t), z = V3.Lerp(p.R.Col(2), q.R.Col(2), t), yy, zz;
                V3 xx = Orth(x, y, z, out yy, out zz); o.R = Mat3.FromCols(xx, yy, zz);
                r.Add(o);
            }
            return r;
        }
    }

    /// <summary>영웅 선택 화면의 연출: 서 있다가 공격 동작을 보여 주는 반복. 시간 t(초)에서의 도형 목록.</summary>
    public static class HeroShowcase
    {
        public const float Period = 5.2f;             // 한 바퀴
        public const float AtkStart = 2.2f, AtkLen = 1.1f;
        const float Blend = 0.30f;

        public static List<HeroPrim> Frame(HeroKind kind, float t)
        {
            float u = t % Period; if (u < 0) u += Period;
            // 숨쉬기: 서 있는 동안 가볍게 위아래로(걷기 도형이 아니라 몸 전체를 아주 조금 움직이는 것은 그리는 쪽이 한다)
            List<HeroPrim> idle = HeroModel.Build(kind, 0f, false, null);
            if (u < AtkStart - Blend || u > AtkStart + AtkLen + Blend) return idle;
            HeroPose[] keys = HeroModel.Attack[(int)kind];
            float a = (u - AtkStart) / AtkLen;                       // 공격 구간 -0.27 .. 1.27
            if (a < 0f) { float k = (a * AtkLen + Blend) / Blend; return HeroModel.Blend(idle, HeroModel.Build(kind, 0f, false, keys[0]), Smooth(k)); }
            if (a > 1f) { float k = ((a - 1f) * AtkLen) / Blend; return HeroModel.Blend(HeroModel.Build(kind, 0f, false, keys[4]), idle, Smooth(k)); }
            float f = a * 4f; int i = Math.Min(3, (int)f);
            return HeroModel.Build(kind, 0f, false, HeroPose.Lerp(keys[i], keys[i + 1], Smooth(f - i)));
        }

        static float Smooth(float x) { x = Math.Max(0f, Math.Min(1f, x)); return x * x * (3 - 2 * x); }
    }
}
