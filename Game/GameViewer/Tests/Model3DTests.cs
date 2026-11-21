//***************************************************************************
// Model3DTests.cs : glTF 로더 / 뼈대 계산 / 몬스터 애니메이터 시험. 합성 glTF(관절 2개짜리 막대)를 코드에서 만들어 쓴다(파일 필요 없음).
//   Assets/models 의 실제 모델이 있으면 전부 읽어 구조(정점/관절/클립)까지 확인한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameViewer.Core.Model3D;

namespace GameViewer.Tests
{
    public static class Model3DTests
    {
        private static readonly string Inv = System.Globalization.CultureInfo.InvariantCulture.Name;

        /// <summary>
        /// 노드 0(관절 0, 원점) -> 노드 1(관절 1, (0,1,0)). 정점 3개: v0(0,0,0)=관절0, v1(1,1,0)=관절1, v2(0,2,0)=관절0/1 반반.
        /// 클립 Idle/Walk/Punch/HitReact/Death 는 관절 1 을 z 축으로 0 -> 90도 돌린다(1초). 클립 이름은 names 로 정한다.
        /// </summary>
        public static string BuildGltf(string[] names, bool withSkin = true)
        {
            MemoryStream ms = new MemoryStream(); BinaryWriter w = new BinaryWriter(ms);
            List<int[]> views = new List<int[]>();            // offset, length
            Action<int> mark = delegate (int start) { views.Add(new[] { start, (int)ms.Position - start }); while (ms.Position % 4 != 0) w.Write((byte)0); };
            int s0 = (int)ms.Position; foreach (float f in new float[] { 0, 0, 0, 1, 1, 0, 0, 2, 0 }) w.Write(f); mark(s0);                          // 0 위치
            s0 = (int)ms.Position; foreach (float f in new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }) w.Write(f); mark(s0);                              // 1 법선
            s0 = (int)ms.Position; foreach (float f in new float[] { 0, 0, 1, 0, 0, 1 }) w.Write(f); mark(s0);                                       // 2 UV
            s0 = (int)ms.Position; foreach (ushort u in new ushort[] { 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0 }) w.Write(u); mark(s0);                   // 3 관절 번호
            s0 = (int)ms.Position; foreach (float f in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 0.5f, 0.5f, 0, 0 }) w.Write(f); mark(s0);              // 4 가중치
            s0 = (int)ms.Position; foreach (ushort u in new ushort[] { 0, 1, 2 }) w.Write(u); mark(s0);                                               // 5 인덱스
            s0 = (int)ms.Position;
            foreach (float f in new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }) w.Write(f);                                          // 6 inverse bind: 관절0 = 단위
            foreach (float f in new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1 }) w.Write(f); mark(s0);                              //   관절1 = 이동(0,-1,0)
            s0 = (int)ms.Position; w.Write(0f); w.Write(1f); mark(s0);                                                                                // 7 시각
            s0 = (int)ms.Position; foreach (float f in new float[] { 0, 0, 0, 1, 0, 0, 0.70710678f, 0.70710678f }) w.Write(f); mark(s0);             // 8 회전 키
            string b64 = Convert.ToBase64String(ms.ToArray());

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0" + (withSkin ? "" : "") + "]}],");
            sb.Append("\"nodes\":[{\"name\":\"root\",\"children\":[1]},{\"name\":\"arm\",\"translation\":[0,1,0]},{\"mesh\":0" + (withSkin ? ",\"skin\":0" : "") + "}],");
            sb.Append("\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"TEXCOORD_0\":2,\"JOINTS_0\":3,\"WEIGHTS_0\":4},\"indices\":5}]}],");
            sb.Append("\"skins\":[{\"joints\":[0,1],\"inverseBindMatrices\":6}],");
            sb.Append("\"animations\":[");
            for (int i = 0; i < names.Length; ++i)
                sb.Append((i > 0 ? "," : "") + "{\"name\":\"" + names[i] + "\",\"samplers\":[{\"input\":7,\"output\":8,\"interpolation\":\"LINEAR\"}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":1,\"path\":\"rotation\"}}]}");
            sb.Append("],");
            sb.Append("\"accessors\":[");
            string[] acc = {
                "{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}",
                "{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}",
                "{\"bufferView\":2,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"}",
                "{\"bufferView\":3,\"componentType\":5123,\"count\":3,\"type\":\"VEC4\"}",
                "{\"bufferView\":4,\"componentType\":5126,\"count\":3,\"type\":\"VEC4\"}",
                "{\"bufferView\":5,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}",
                "{\"bufferView\":6,\"componentType\":5126,\"count\":2,\"type\":\"MAT4\"}",
                "{\"bufferView\":7,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\"}",
                "{\"bufferView\":8,\"componentType\":5126,\"count\":2,\"type\":\"VEC4\"}" };
            sb.Append(string.Join(",", acc)); sb.Append("],\"bufferViews\":[");
            for (int i = 0; i < views.Count; ++i) sb.Append((i > 0 ? "," : "") + "{\"buffer\":0,\"byteOffset\":" + views[i][0] + ",\"byteLength\":" + views[i][1] + "}");
            sb.Append("],\"buffers\":[{\"byteLength\":" + ms.Length + ",\"uri\":\"data:application/octet-stream;base64," + b64 + "\"}]}");
            return sb.ToString();
        }

        private static GltfModel Make(params string[] names)
        {
            return GltfModel.Parse(BuildGltf(names.Length == 0 ? new[] { "Idle", "Walk", "Punch", "HitReact", "Death" } : names), delegate (string u) { throw new IOException(u); });
        }

        private static void Near3(float[] p, int v, float x, float y, float z, string what)
        {
            T.Near(x, p[v * 3], 1e-4, what + ".x"); T.Near(y, p[v * 3 + 1], 1e-4, what + ".y"); T.Near(z, p[v * 3 + 2], 1e-4, what + ".z");
        }

        /// <summary>CPU 스키닝(셰이더가 하는 일과 같다): 팔레트 x 정점, 가중 합.</summary>
        public static float[] Skin(GltfModel m, float[] pal)
        {
            float[] o = new float[m.VertexCount * 3];
            for (int v = 0; v < m.VertexCount; ++v)
            {
                float x = m.Positions[v * 3], y = m.Positions[v * 3 + 1], z = m.Positions[v * 3 + 2];
                for (int k = 0; k < 4; ++k)
                {
                    float wt = m.Weights[v * 4 + k]; if (wt == 0f) continue;
                    int b = (int)m.JointIds[v * 4 + k] * 16;
                    o[v * 3] += wt * (pal[b] * x + pal[b + 4] * y + pal[b + 8] * z + pal[b + 12]);
                    o[v * 3 + 1] += wt * (pal[b + 1] * x + pal[b + 5] * y + pal[b + 9] * z + pal[b + 13]);
                    o[v * 3 + 2] += wt * (pal[b + 2] * x + pal[b + 6] * y + pal[b + 10] * z + pal[b + 14]);
                }
            }
            return o;
        }

        public static void Loader()
        {
            GltfModel m = Make();
            T.Eq(3, m.VertexCount, "정점 수"); T.Eq(3, m.IndexCount, "인덱스 수"); T.Eq(2, m.JointCount, "관절 수"); T.Eq(3, m.NodeCount, "노드 수");
            T.Eq(5, m.Clips.Count, "클립 수"); T.Near(1.0, m.Clip("Idle").Duration, 1e-6, "클립 길이");
            T.Eq(-1, m.Parent[0], "루트 부모"); T.Eq(0, m.Parent[1], "자식 부모");
            T.Check(m.Order[0] == 0 && Array.IndexOf(m.Order, 1) > 0, "위상 순서: 부모 먼저");
            T.Near(0.0, m.MinY, 1e-6, "범위 MinY"); T.Near(2.0, m.MaxY, 1e-6, "범위 MaxY");
            T.Near(1.0, m.Weights[8] + m.Weights[9] + m.Weights[10] + m.Weights[11], 1e-6, "가중치 합 1");
            T.Check(m.Clip("없는클립") == null && m.Clip(null) == null, "없는 클립은 null");
            bool threw = false;
            try { GltfModel.Parse(BuildGltf(new[] { "Idle" }, false), delegate (string u) { return null; }); } catch (InvalidDataException) { threw = true; }
            T.Check(threw, "스킨 없는 메시는 거부");
        }

        public static void Skinning()
        {
            GltfModel m = Make(); Pose p = new Pose(m); float[] pal = new float[m.PaletteCount * 16];
            Skeleton.Sample(m, null, 0f, p); Skeleton.Palette(m, p, pal);
            for (int j = 0; j < 2; ++j)
                for (int i = 0; i < 16; ++i) T.Near(i % 5 == 0 ? 1.0 : 0.0, pal[j * 16 + i], 1e-5, "기본 자세 팔레트는 단위행렬 j" + j + "[" + i + "]");
            float[] v0 = Skin(m, pal);
            Near3(v0, 0, 0, 0, 0, "기본 v0"); Near3(v0, 1, 1, 1, 0, "기본 v1"); Near3(v0, 2, 0, 2, 0, "기본 v2");

            Skeleton.Sample(m, m.Clip("Idle"), 1f, p); Skeleton.Palette(m, p, pal);      // 관절 1 을 z 로 90도: (1,1,0) -> (0,2,0)
            float[] v1 = Skin(m, pal);
            Near3(v1, 0, 0, 0, 0, "90도 v0 (관절0)"); Near3(v1, 1, 0, 2, 0, "90도 v1"); Near3(v1, 2, -0.5f, 1.5f, 0, "90도 v2 (반반)");

            Skeleton.Sample(m, m.Clip("Idle"), 0.5f, p); Skeleton.Palette(m, p, pal);      // 45도
            float[] vh = Skin(m, pal);
            float c = (float)Math.Cos(Math.PI / 4);
            Near3(vh, 1, c, 1f + c, 0, "45도 v1");
        }

        public static void Sampling()
        {
            GltfModel m = Make(); Pose p = new Pose(m); float[] pal = new float[m.PaletteCount * 16];
            Skeleton.Sample(m, m.Clip("Idle"), 0.5f, p);
            float c45 = (float)Math.Sin(Math.PI / 8), w45 = (float)Math.Cos(Math.PI / 8);
            T.Near(c45, p.R[1 * 4 + 2], 1e-5, "t=0.5 회전 z (22.5도 반각)"); T.Near(w45, p.R[1 * 4 + 3], 1e-5, "t=0.5 회전 w");
            Skeleton.Sample(m, m.Clip("Idle"), 5f, p); T.Near(0.70710678, p.R[1 * 4 + 2], 1e-5, "끝을 넘으면 마지막 키");
            Skeleton.Sample(m, m.Clip("Idle"), -3f, p); T.Near(0.0, p.R[1 * 4 + 2], 1e-5, "처음 앞이면 첫 키");
            Skeleton.Sample(m, m.Clip("Idle"), 1f, p); T.Near(0.0, p.R[0 * 4 + 3] - 1.0, 1e-6, "채널 없는 노드는 기본 자세");

            // 섞기
            Pose a = new Pose(m), b = new Pose(m), o = new Pose(m);
            Skeleton.Sample(m, null, 0f, a); Skeleton.Sample(m, m.Clip("Idle"), 1f, b);
            Skeleton.Blend(a, b, 0.5f, o);
            T.Near(c45, o.R[1 * 4 + 2], 1e-5, "섞기 0.5 = 45도의 반각"); T.Near(1.0, Math.Sqrt(o.R[4] * o.R[4] + o.R[5] * o.R[5] + o.R[6] * o.R[6] + o.R[7] * o.R[7]), 1e-5, "섞은 쿼터니언은 단위");
            Skeleton.Blend(a, b, 0f, o); T.Near(0.0, o.R[6], 1e-6, "섞기 0 = a"); Skeleton.Blend(a, b, 1f, o); T.Near(0.70710678, o.R[6], 1e-5, "섞기 1 = b");
            Skeleton.Blend(a, b, 0.25f, b); T.Near(Math.Sin(Math.PI / 16), b.R[6], 1e-5, "제자리 섞기(into==b)");

            // 쿼터니언 최단 경로: -q 와 q 는 같은 회전
            float[] q1 = { 0, 0, 0.70710678f, 0.70710678f }, q2 = { 0, 0, -0.70710678f, -0.70710678f }, d = new float[4];
            Skeleton.Slerp(q1, 0, q2, 0, 0.5f, d, 0);
            T.Near(0.70710678, d[2], 1e-5, "반대 부호 쿼터니언은 최단 경로로"); T.Near(0.70710678, d[3], 1e-5, "반대 부호 w");

            // 팔레트 재계산이 같은 값을 준다(작업 버퍼를 재사용해도 오염 없음)
            Skeleton.Sample(m, m.Clip("Idle"), 0.3f, p); Skeleton.Palette(m, p, pal); float[] copy = (float[])pal.Clone();
            Skeleton.Palette(m, p, pal); for (int i = 0; i < pal.Length; ++i) T.Near(copy[i], pal[i], 1e-7, "팔레트 재현 " + i);
        }

        private static AnimInput In(long now) { AnimInput s = new AnimInput(); s.NowMs = now; s.AttackStartMs = s.HitStartMs = s.DeadAtMs = -100000; s.AttackMs = 360; return s; }

        public static void Animator()
        {
            GltfModel m = Make(); MonsterAnimState st = new MonsterAnimState(m); float[] pal = new float[m.PaletteCount * 16];
            AnimInput s = In(1000);
            T.Eq(AnimRole.Idle, st.Choose(ref s), "기본은 대기");
            s.Walking = true; T.Eq(AnimRole.Walk, st.Choose(ref s), "걷기");
            s.HitStartMs = 900; T.Eq(AnimRole.Hit, st.Choose(ref s), "피격이 걷기보다 우선");
            s.AttackStartMs = 800; T.Eq(AnimRole.Attack, st.Choose(ref s), "공격이 피격보다 우선");
            s.AttackStartMs = 500; T.Eq(AnimRole.Hit, st.Choose(ref s), "공격 끝나면(500+360<1000) 피격");
            s.HitStartMs = 500; T.Eq(AnimRole.Walk, st.Choose(ref s), "피격 끝나면(420ms) 걷기");
            s.Dead = true; s.DeadAtMs = 900; T.Eq(AnimRole.Death, st.Choose(ref s), "사망이 최우선");

            // 시각
            s = In(1000); s.Walked = 0.8f; st.WalkStride = 1.6f; T.Near(0.5, st.TimeOf(AnimRole.Walk, ref s), 1e-5, "걸은 거리 -> 걷기 위상");
            s.Walked = 1.6f * 3 + 0.4f; T.Near(0.25, st.TimeOf(AnimRole.Walk, ref s), 1e-4, "걷기는 반복");
            s.AttackStartMs = 1000 - 180; T.Near(0.5, st.TimeOf(AnimRole.Attack, ref s), 1e-5, "공격 클립을 공격 길이에 맞춰 늘임");
            s.HitStartMs = 1000 - 100; T.Near(0.1, st.TimeOf(AnimRole.Hit, ref s), 1e-5, "피격은 본래 속도");
            s.DeadAtMs = 1000 - 300; T.Near(0.3, st.TimeOf(AnimRole.Death, ref s), 1e-5, "사망 진행");
            s.DeadAtMs = 1000 - 5000; T.Near(1.0, st.TimeOf(AnimRole.Death, ref s), 1e-5, "사망은 마지막 자세에서 멈춤");
            s.DeadAtMs = 0; T.Near(1.0, st.TimeOf(AnimRole.Death, ref s), 1e-5, "이미 죽어 있던 개체는 바로 마지막 자세");
            AnimInput a = In(250), b = In(250); a.Seed = 0; b.Seed = 3;
            T.Check(Math.Abs(st.TimeOf(AnimRole.Idle, ref a) - st.TimeOf(AnimRole.Idle, ref b)) > 0.05f, "개체마다 대기 위상이 다르다");

            // 크로스페이드: 대기 -> 공격. 섞는 동안 대기(0도)와 공격(공격 중 시각의 회전) 사이.
            MonsterAnimState f = new MonsterAnimState(m);
            s = In(0); s.Seed = 0; f.Update(ref s, pal);                                      // 대기 t=0 -> 회전 0도
            float[] rest = Skin(m, pal); Near3(rest, 1, 1, 1, 0, "대기 시작은 기본 자세");
            s = In(10); s.AttackStartMs = 10; s.AttackMs = 400; T.Eq(AnimRole.Attack, f.Update(ref s, pal), "공격으로 전환");
            float[] atk0 = Skin(m, pal); Near3(atk0, 1, 1, 1, 0, "전환 직후는 거의 이전 자세");
            s = In(10 + 35); s.AttackStartMs = 10; s.AttackMs = 400; f.Update(ref s, pal);
            float[] mid = Skin(m, pal); T.Check(mid[3] < 1f && mid[3] > 0.2f && mid[4] > 1f, "섞는 도중은 중간 자세 x=" + mid[3]);
            s = In(10 + 300); s.AttackStartMs = 10; s.AttackMs = 400; f.Update(ref s, pal);   // 공격 75% = 67.5도
            float[] end = Skin(m, pal); double ang = 0.75 * Math.PI / 2;
            T.Near(Math.Cos(ang), end[3], 1e-4, "공격 75% 시점 v1.x"); T.Near(1.0 + Math.Sin(ang), end[4], 1e-4, "공격 75% 시점 v1.y");

            // 없는 클립이면 대기로 대신하고 동작이 흔들리지 않는다
            GltfModel only = Make("Idle"); MonsterAnimState g = new MonsterAnimState(only);
            s = In(100); s.Walking = true; s.Dead = true; s.AttackStartMs = 90;
            T.Eq(AnimRole.Idle, g.Choose(ref s), "클립이 없으면 대기로"); T.Check(!g.Has(AnimRole.Death) && g.Has(AnimRole.Idle), "Has");
            // 다른 이름의 클립(날아다니는 묶음 / 곰 묶음)
            GltfModel fly = Make("Flying_Idle", "Fast_Flying", "Headbutt", "HitReact", "Death"); MonsterAnimState h = new MonsterAnimState(fly);
            T.Check(h.Has(AnimRole.Idle) && h.Has(AnimRole.Walk) && h.Has(AnimRole.Attack) && h.Has(AnimRole.Hit) && h.Has(AnimRole.Death), "날아다니는 묶음 이름");
            GltfModel blob = Make("Idle", "Walk", "Bite_Front", "HitRecieve", "Death"); MonsterAnimState k = new MonsterAnimState(blob);
            T.Check(k.Has(AnimRole.Attack) && k.Has(AnimRole.Hit), "블롭 묶음 이름");
        }

        /// <summary>카메라 행렬이 정해 둔 화면 위치 식(MonsterCamera)과 numpy 계산값과 같은지.</summary>
        public static void Camera()
        {
            float hm = 2.5f; float[] mvp = new float[16], rot = new float[16];
            double el = MonsterCamera.ElevDeg * Math.PI / 180.0;
            float[][] pts = { new float[] { 0, 0, 0 }, new float[] { 0, hm, 0 }, new float[] { 1, 1, 0 }, new float[] { 0, 0, 1 }, new float[] { -0.7f, 1.3f, 0.9f } };
            foreach (double deg in new double[] { 0, 90, 180, 270, 37 })
            {
                MonsterCamera.Build((float)(deg * Math.PI / 180.0), hm, mvp, rot);
                foreach (float[] p in pts)
                {
                    double yw = deg * Math.PI / 180.0, c = Math.Cos(yw), s = Math.Sin(yw);
                    double x = c * p[0] + s * p[2], z = -s * p[0] + c * p[2], y = p[1];
                    double sc = 100.0 / hm;                                      // 칸 200px, 모델 높이 = 100px
                    double sx = 100 + sc * x, sy = 160 - sc * (y * Math.Cos(el) - z * Math.Sin(el)), dep = z * Math.Cos(el) + y * Math.Sin(el);
                    double cx = mvp[0] * p[0] + mvp[4] * p[1] + mvp[8] * p[2] + mvp[12], cy = mvp[1] * p[0] + mvp[5] * p[1] + mvp[9] * p[2] + mvp[13], cz = mvp[2] * p[0] + mvp[6] * p[1] + mvp[10] * p[2] + mvp[14];
                    T.Near((sx - 100) / 100.0, cx, 1e-4, "yaw " + deg + " x"); T.Near((160 - sy) / 100.0 - 0.6, cy, 1e-4, "yaw " + deg + " y");
                    T.Near(-dep / (4 * hm), cz, 1e-4, "yaw " + deg + " 깊이");
                }
            }
            // 방향: yaw 0 이면 모델 +z 가 카메라 쪽(화면 아래), 90 이면 화면 오른쪽
            MonsterCamera.Build(0f, hm, mvp, rot);             float[] fwd = { 0, 0, 1 };
            MonsterCamera.Build((float)(Math.PI / 2), hm, mvp, rot);
            T.Check(mvp[8] * fwd[2] > 0.1f, "yaw 90: 앞(+z)이 화면 오른쪽(+x)");
            MonsterCamera.Build(0f, hm, mvp, rot);
            T.Check(mvp[9] * fwd[2] < -0.01f, "yaw 0: 앞(+z)은 화면 아래쪽");
            T.Near(1.0, Math.Sqrt(rot[0] * rot[0] + rot[1] * rot[1] + rot[2] * rot[2]), 1e-5, "회전 열은 단위 길이");
            T.Near(MonsterCamera.CellModels * 3f, MonsterCamera.CellWorld(3f), 1e-6, "칸 크기");
        }

        public static void Registry()
        {
            MonsterModels.Clear();
            T.Check(!MonsterModels.Has(true, 5) && MonsterModels.Get(true, 5) == null, "빈 표");
            MonsterModels.Set(true, 5, new ModelSpec { Path = "models/Mushnub.gltf", WalkStride = 1.2f });
            T.Check(MonsterModels.Has(true, 5), "등록"); T.Eq("models/Mushnub.gltf", MonsterModels.Get(true, 5).Path, "경로"); T.Near(1.2, MonsterModels.Get(true, 5).WalkStride, 1e-6, "걸음 길이");
            T.Check(!MonsterModels.Has(false, 5) && MonsterModels.Get(false, 5) == null, "같은 번호의 영웅은 따로");
            MonsterModels.Set(false, 5, new ModelSpec { Path = "models/Hero.gltf" }); T.Eq("models/Hero.gltf", MonsterModels.Get(false, 5).Path, "영웅 경로"); T.Eq("models/Mushnub.gltf", MonsterModels.Get(true, 5).Path, "몬스터는 그대로");
            MonsterModels.Clear(); T.Check(!MonsterModels.Has(true, 5) && !MonsterModels.Has(false, 5), "비우기");
        }

        /// <summary>sprites.json 의 "model3d" 가 가리키는 파일이 있고, 몬스터/영웅 모두 이미지 시트 없이(3D 전용)인지.</summary>
        public static void Manifest()
        {
            string dir = null;
            for (string d = AppDomain.CurrentDomain.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
                if (File.Exists(Path.Combine(d, "Assets", "sprites.json"))) { dir = Path.Combine(d, "Assets"); break; }
            if (dir == null) { Console.WriteLine("  (Assets/sprites.json 없음: 건너뜀)"); return; }
            int n = 0;
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "sprites.json"))))
                foreach (System.Text.Json.JsonElement e in doc.RootElement.GetProperty("sprites").EnumerateArray())
                {
                    System.Text.Json.JsonElement m3;
                    if (!e.TryGetProperty("model3d", out m3)) continue;
                    ++n; string path = Path.Combine(dir, m3.GetString()), who = "template " + e.GetProperty("template").GetInt32();
                    T.Check(File.Exists(path), who + ": 모델 파일 " + m3.GetString());
                    { string kd = e.GetProperty("kind").GetString(); T.Check(kd == "monster" || kd == "hero", who + ": 몬스터/영웅 항목"); }
                    if (e.GetProperty("kind").GetString() == "monster") T.Check(!e.TryGetProperty("pattern", out _), who + ": 3D 전용 몬스터(이미지 시트 없음)");
                    else T.Check(!e.TryGetProperty("pattern", out _), who + ": 3D 전용 영웅(이미지 시트 없음)");
                    T.Check(e.GetProperty("height").GetDouble() > 0.5, who + ": height(월드 단위 높이)");
                }
            T.Check(n >= 10, "3D 모델 항목 수 " + n);
        }

        public static void VertexBuffer()
        {
            GltfModel m = Make(); float[] vb = m.BuildVertexBuffer();
            T.Eq(m.VertexCount * GltfModel.VertexStride, vb.Length, "정점 버퍼 길이");
            // 정점 1: 위치(1,1,0) 법선(0,0,1) uv(1,0) 관절(1,0,0,0) 가중치(1,0,0,0)
            float[] want = { 1, 1, 0, 0, 0, 1, 1, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
            for (int i = 0; i < 16; ++i) T.Near(want[i], vb[16 + i], 1e-6, "정점 1 [" + i + "]");
        }

        /// <summary>Assets/models 가 있으면 전부 읽는다(없으면 건너뜀).</summary>
        public static void RealModels()
        {
            string dir = null;
            for (string d = AppDomain.CurrentDomain.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
                if (Directory.Exists(Path.Combine(d, "Assets", "models"))) { dir = Path.Combine(d, "Assets", "models"); break; }
            if (dir == null) { Console.WriteLine("  (Assets/models 없음: 건너뜀)"); return; }
            foreach (string f in Directory.GetFiles(dir, "*.gltf"))
            {
                GltfModel m = GltfModel.Load(f); string n = Path.GetFileName(f);
                T.Check(m.VertexCount > 100 && m.IndexCount % 3 == 0 && m.JointCount > 0 && m.JointCount <= 64, n + ": 정점/관절 수 (" + m.VertexCount + "/" + m.JointCount + ")");
                T.Check(m.TextureBytes != null && m.TextureBytes.Length > 100, n + ": 텍스처");
                T.Check(AnimRoles.Find(m, AnimRole.Idle) != null, n + ": 대기 클립");
                T.Check(m.MaxY - m.MinY > 0.2f, n + ": 높이");
                MonsterAnimState st = new MonsterAnimState(m); float[] pal = new float[m.PaletteCount * 16];
                AnimInput s = In(500); s.Walking = true; s.Walked = 0.7f; st.Update(ref s, pal);
                bool finite = true; for (int i = 0; i < pal.Length; ++i) if (float.IsNaN(pal[i]) || float.IsInfinity(pal[i])) finite = false;
                T.Check(finite, n + ": 팔레트가 유한");
                float[] sk = Skin(m, pal); float top = float.MinValue; for (int v = 0; v < m.VertexCount; ++v) top = Math.Max(top, sk[v * 3 + 1]);
                T.Check(top > 0.2f && top < 20f, n + ": 스킨된 높이 " + top);
                int cov = 0; foreach (GltfModel.Part pt in m.Parts) { cov += pt.IndexCount; T.Check(pt.Texture >= 0 && pt.Texture < Math.Max(1, m.Textures.Length), n + ": 파트 텍스처 번호"); }
                T.Eq(m.IndexCount, cov, n + ": 파트가 인덱스 전체를 덮음");
                T.Check(m.PaletteCount <= 64, n + ": 팔레트 수 " + m.PaletteCount);
                if (m.RigidNodes.Length > 0)    // 영웅: 강체 가상 관절 -> 팔레트 칸이 노드 전역 행렬과 같다
                {
                    T.Check(AnimRoles.Find(m, AnimRole.Attack) != null && AnimRoles.Find(m, AnimRole.Death) != null, n + ": 영웅 공격/사망 클립");
                    T.Check(m.Textures.Length >= 1, n + ": 영웅 텍스처");
                }
            }
        }
    }
}
