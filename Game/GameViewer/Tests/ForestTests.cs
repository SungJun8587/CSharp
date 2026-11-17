using System;
using System.Collections.Generic;
using System.Reflection;
using GameViewer.App.Forest;

namespace GameViewer.Tests
{
    /// <summary>3D 숲의 CPU 쪽 로직: 길, 카메라, 배치, 메시, 텍스처, 그림자 행렬, 셰이더 글. (GL 호출 자체는 그래픽 장치가 있어야 해서 여기서 다루지 않는다.)</summary>
    public static class ForestTests
    {
        static float Dist(V3 a, V3 b) { return (a - b).Length; }

        public static void Path()
        {
            ForestWorld w = new ForestWorld(7);
            T.Check(w.PathLength > 100f && w.PathLength < 400f, "path length is walkable-sized: " + w.PathLength);
            float x0, z0, x1, z1;
            w.PathAt(0f, out x0, out z0); w.PathAt(w.PathLength, out x1, out z1);
            T.Near(0, Math.Sqrt((x0 - x1) * (x0 - x1) + (z0 - z1) * (z0 - z1)), 0.05, "closed loop: end meets start");
            float px = x0, pz = z0, worst = 0f;
            for (float s = 0.5f; s <= w.PathLength * 2.2f; s += 0.5f)       // 두 바퀴 이상 돌아도 이어진다
            {
                float x, z; w.PathAt(s, out x, out z);
                float d = (float)Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                if (d > worst) worst = d; px = x; pz = z;
            }
            T.Check(worst < 0.7f, "no jumps along the path (largest step " + worst + " for 0.5 m)");
            float ax, az; w.PathAt(10f, out ax, out az);
            T.Near(0, w.PathDistance(ax, az), 0.05, "a path point is at distance 0");
            T.Check(w.PathDistance(ax + 40f, az + 40f) > 5f, "far point is far from the path");
        }

        public static void Camera()
        {
            ForestWorld w = new ForestWorld(7); ForestCamera cam = new ForestCamera(w);
            T.Near(0, ForestCamera.LookUp(0f), 1e-6, "walking level at start");
            T.Near(1, ForestCamera.LookUp(35f), 1e-6, "looking up at 35 s");
            T.Near(ForestCamera.LookUp(5f), ForestCamera.LookUp(5f + ForestCamera.LookCycle), 1e-6, "look-up cycle repeats");
            float maxStep = 0f, maxTurn = 0f, minUp = 1f; CamPose prev = cam.Sample(0f);
            for (float t = 1f / 30f; t < 200f; t += 1f / 30f)
            {
                CamPose p = cam.Sample(t);
                maxStep = Math.Max(maxStep, Dist(p.Eye, prev.Eye));
                maxTurn = Math.Max(maxTurn, Dist(p.Forward, prev.Forward));
                T.Near(1, p.Forward.Length, 1e-3, "forward is unit @" + t);
                minUp = Math.Min(minUp, V3.Dot(p.Up, new V3(0, 1, 0)));
                T.Near(0, V3.Dot(p.Up, p.Forward), 1e-3, "up is perpendicular to forward @" + t);
                float h = w.Height(p.Eye.X, p.Eye.Z);
                if (p.Eye.Y - h < 1.4f || p.Eye.Y - h > 1.9f) T.Check(false, "eye height above ground out of range @" + t + ": " + (p.Eye.Y - h));
                prev = p;
            }
            T.Check(maxStep < 0.08f, "eye moves smoothly per 1/30 s (max " + maxStep + ")");
            T.Check(maxTurn < 0.06f, "view turns smoothly per 1/30 s (max " + maxTurn + ")");
            T.Check(minUp > 0.5f, "camera never rolls over (min up.y " + minUp + ")");   // 위를 볼 때 up.y 는 cos(고개 각도)까지 내려간다
        }

        public static void Layout()
        {
            ForestWorld w = new ForestWorld(7);
            ForestLayout a = w.Generate(), b = new ForestWorld(7).Generate();
            T.Eq(a.TreeCount, b.TreeCount, "same seed -> same tree count");
            T.Eq(a.Grass.Count, b.Grass.Count, "same seed -> same grass count");
            T.Check(a.TreeCount >= 400 && a.TreeCount <= 800, "tree count reasonable: " + a.TreeCount);
            T.Check(a.Grass.Count > 10000 && a.Grass.Count <= 60000, "grass count within budget: " + a.Grass.Count);
            T.Check(a.Motes != null && a.Motes.Length % 4 == 0 && a.Motes.Length > 0, "motes are x,y,z,seed quads");
            List<Inst> all = new List<Inst>();
            for (int v = 0; v < ForestLayout.TreeVariants; ++v) all.AddRange(a.Trees[v]);
            T.Eq(a.Trees[0][0].X, b.Trees[0][0].X, "same seed -> same first tree");
            T.Check(a.Trees[3].Count > 0, "pale birch variant is used");
            bool inside = true, clear = true; float minGap = 1e9f;
            foreach (Inst t in all)
            {
                if (Math.Sqrt(t.X * t.X + t.Z * t.Z) > ForestWorld.TreeRadius + 1f) inside = false;
                if (w.PathDistance(t.X, t.Z) < 1.9f) clear = false;
            }
            for (int i = 0; i < all.Count; ++i)
                for (int j = i + 1; j < all.Count; ++j)
                {
                    float dx = all[i].X - all[j].X, dz = all[i].Z - all[j].Z;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz); if (d < minGap) minGap = d;
                }
            T.Check(inside, "all trees inside the planting radius");
            T.Check(clear, "no tree on the walking path");
            T.Check(minGap >= 2.2f, "trees keep their spacing (min gap " + minGap + ")");
            foreach (Inst g in a.Grass) { if (Math.Abs(g.X) > ForestWorld.TerrainHalf || Math.Abs(g.Z) > ForestWorld.TerrainHalf) { T.Check(false, "grass inside terrain"); break; } }
        }

        static void MeshOk(MeshData m, string name)
        {
            T.Check(m != null && m.V.Length > 0 && m.I.Length > 0 && m.I.Length % 3 == 0 && m.V.Length % MeshData.Stride == 0, name + ": shape");
            uint max = 0; bool finite = true;
            foreach (uint i in m.I) if (i > max) max = i;
            foreach (float f in m.V) if (float.IsNaN(f) || float.IsInfinity(f)) finite = false;
            T.Check(max < (uint)m.VertexCount, name + ": indices in range");
            T.Check(finite, name + ": finite values");
            int bad = 0;
            for (int i = 0; i < m.VertexCount; ++i)
            {
                float nx = m.V[i * 9 + 3], ny = m.V[i * 9 + 4], nz = m.V[i * 9 + 5];
                float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (Math.Abs(l - 1f) > 0.02f) ++bad;
            }
            T.Check(bad == 0, name + ": unit normals (" + bad + " bad)");
        }

        public static void Meshes()
        {
            ForestWorld w = new ForestWorld(7);
            for (int v = 0; v < ForestLayout.TreeVariants; ++v)
            {
                MeshData t, l; ForestMeshes.BuildTree(v, out t, out l);
                MeshOk(t, "trunk" + v); MeshOk(l, "leaves" + v);
                T.Check(t.TriangleCount < 1200 && l.TriangleCount < 500, "tree " + v + " stays light (" + t.TriangleCount + "+" + l.TriangleCount + " tris)");
            }
            for (int v = 0; v < ForestLayout.RockVariants; ++v) MeshOk(ForestMeshes.BuildRock(v), "rock" + v);
            MeshOk(ForestMeshes.BuildTerrain(w, 60), "terrain"); MeshOk(ForestMeshes.BuildGrassTuft(), "grass tuft");
        }

        public static void Textures()
        {
            ForestWorld w = new ForestWorld(7);
            TexImage leaf = ForestTextures.LeafCluster(128), grass = ForestTextures.GrassTuft(64, 128), ground = ForestTextures.GroundMap(w, 128), detail = ForestTextures.GroundDetail(64);
            T.Eq(128 * 128 * 4, leaf.Rgba.Length, "leaf bytes"); T.Eq(64 * 128 * 4, grass.Rgba.Length, "grass bytes");
            T.Eq(128 * 128 * 4, ground.Rgba.Length, "ground bytes"); T.Eq(64 * 64 * 4, detail.Rgba.Length, "detail bytes");
            float cl = leaf.Coverage(), cg = grass.Coverage();
            T.Check(cl > 0.25f && cl < 0.75f, "leaf cluster is about half covered: " + cl);
            T.Check(cg > 0.08f && cg < 0.30f, "grass tuft is sparse: " + cg);
            T.Eq((byte)255, detail.Rgba[3], "detail map is opaque");
        }

        public static void Shadow()
        {
            ForestWorld w = new ForestWorld(7); ForestCamera cam = new ForestCamera(w);
            int outside = 0, total = 0;
            for (float t = 0f; t < 120f; t += 7f)
            {
                CamPose p = cam.Sample(t); float[] lvp = ForestLighting.ShadowMatrix(p.Eye, p.Forward);
                V3 flat = new V3(p.Forward.X, 0, p.Forward.Z).Normalized();
                for (float d = 0f; d <= 22f; d += 2f)
                    for (float s = -6f; s <= 6f; s += 3f)
                    {
                        V3 q = p.Eye + flat * d + new V3(-flat.Z, 0, flat.X) * s; q = new V3(q.X, w.Height(q.X, q.Z), q.Z);
                        float ww; V3 c = Mat4.Transform(lvp, q, out ww);
                        ++total; if (Math.Abs(c.X / ww) > 1f || Math.Abs(c.Y / ww) > 1f || c.Z / ww < -1f || c.Z / ww > 1f) ++outside;
                    }
            }
            T.Eq(0, outside, "ground in front of the camera lies inside the shadow map (" + total + " samples)");
            // 카메라가 조금 움직여도 지도 이동은 한 칸(텍셀) 단위로만: 같은 땅 점의 위치가 텍셀 격자에서 일렁이지 않는다
            CamPose a = cam.Sample(10f); V3 g = new V3(a.Eye.X + 3f, 0f, a.Eye.Z + 2f);
            float w1, w2; V3 c1 = Mat4.Transform(ForestLighting.ShadowMatrix(a.Eye, a.Forward), g, out w1);
            V3 e2 = a.Eye + new V3(0.001f, 0f, 0f); V3 c2 = Mat4.Transform(ForestLighting.ShadowMatrix(e2, a.Forward), g, out w2);
            T.Near(c1.X, c2.X, 1e-4, "tiny camera move does not shift the shadow map (x)"); T.Near(c1.Y, c2.Y, 1e-4, "tiny camera move does not shift the shadow map (y)");
            float su, sv, ss; float[] vp = Mat4.Multiply(Mat4.Perspective(1.0f, 1.6f, 0.15f, 220f), Mat4.LookAt(a.Eye, a.Eye + new V3(-ForestLighting.SunDir.X, 0, -ForestLighting.SunDir.Z).Normalized(), new V3(0, 1, 0)));
            ForestLighting.SunOnScreen(vp, a.Eye, out su, out sv, out ss); T.Near(0, ss, 1e-6, "sun behind the camera: no god rays");
            vp = Mat4.Multiply(Mat4.Perspective(1.0f, 1.6f, 0.15f, 220f), Mat4.LookAt(a.Eye, a.Eye + ForestLighting.SunDir, new V3(0, 1, 0)));
            ForestLighting.SunOnScreen(vp, a.Eye, out su, out sv, out ss); T.Check(ss > 0.9f && Math.Abs(su - 0.5f) < 0.01f, "looking straight at the sun: full rays, centered");
        }

        public static void ShaderText()
        {
            int n = 0;
            foreach (FieldInfo f in typeof(ForestShaders).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!f.IsLiteral || !(f.Name.EndsWith("Vs") || f.Name.EndsWith("Fs"))) continue;
                string s = (string)f.GetRawConstantValue(); ++n;
                T.Check(s.StartsWith("#version 330 core"), f.Name + ": version line");
                T.Check(s.Contains("void main()"), f.Name + ": has main");
                int depth = 0, min = 0; foreach (char c in s) { if (c == '{') ++depth; else if (c == '}') { --depth; if (depth < min) min = depth; } }
                T.Check(depth == 0 && min == 0, f.Name + ": braces balance");
                if (f.Name.EndsWith("Fs") && f.Name != "ShadowFs" && f.Name != "HeroShadowFs") T.Check(s.Contains("out vec4"), f.Name + ": writes a color");
                int paren = 0; foreach (char c in s) { if (c == '(') ++paren; else if (c == ')') --paren; }
                T.Eq(0, paren, f.Name + ": parentheses balance");
            }
            T.Eq(17, n, "all seventeen shader stages are present");
        }

        public static void Assets()
        {
            ForestAssets a = ForestAssets.Build(ForestAssets.DefaultSeed);
            int sum = 0;
            for (int v = 0; v < ForestLayout.TreeVariants; ++v) { T.Eq(0, a.TreeInst[v].Length % 8, "tree instances are 8 floats each"); sum += a.TreeInst[v].Length / 8; }
            T.Eq(a.Layout.TreeCount, sum, "all trees reach the instance arrays");
            T.Eq(a.Layout.Grass.Count * 8, a.GrassInst.Length, "grass instance array size");
            T.Check(a.Camera != null && a.LeafTex != null && a.GroundTex != null && a.Terrain != null && a.GrassTuft != null, "everything is built");
            T.Eq(1024 * 1024 * 4, a.GroundTex.Rgba.Length, "ground map is 1024x1024");
            Inst i0 = a.Layout.Trees[0][0]; float[] flat = ForestAssets.Flatten(new List<Inst> { i0 });
            T.Check(flat[0] == i0.X && flat[2] == i0.Z && flat[4] == i0.Scale && flat[7] == i0.B, "instance packing order x,y,z,rot,scale,tint,a,b");
        }

        // ------------------------------------------------------------------ 영웅 3D

        /// <summary>모델 좌표(발 밑 원점, 크기 1)로 구운 메시의 실제 경계.</summary>
        static void Bounds(List<HeroPrim> ps, out V3 lo, out V3 hi)
        {
            HeroMeshData m = HeroMesh.Bake(ps, new V3(0, 0, 0), 0f, 1f, null); int S = HeroMeshData.Stride;
            lo = new V3(1e9f, 1e9f, 1e9f); hi = new V3(-1e9f, -1e9f, -1e9f);
            for (int v = 0; v < m.VertexCount; ++v)
            {
                float x = m.V[v * S], y = m.V[v * S + 1], z = m.V[v * S + 2];
                lo = new V3(Math.Min(lo.X, x), Math.Min(lo.Y, y), Math.Min(lo.Z, z)); hi = new V3(Math.Max(hi.X, x), Math.Max(hi.Y, y), Math.Max(hi.Z, z));
            }
        }

        public static void HeroModels()
        {
            string[] names = { "warrior", "archer", "mage", "lancer" };
            for (int k = 0; k < 4; ++k)
            {
                HeroKind kind = (HeroKind)k;
                List<HeroPrim> idle = HeroModel.Build(kind, 0f, false, null);
                T.Check(idle.Count > 80 && idle.Count < 600, names[k] + ": primitive count sane (" + idle.Count + ")");
                // 자세가 달라도 도형 수가 같아야 섞을 수 있다
                int n = idle.Count; bool same = true;
                for (int i = 0; i < 5; ++i) if (HeroModel.Build(kind, 0f, false, HeroModel.Attack[k][i]).Count != n) same = false;
                T.Check(same, names[k] + ": all attack poses keep the primitive count");
                T.Eq(n, HeroModel.Build(kind, 1.3f, true, null).Count, names[k] + ": walking keeps the primitive count");
                V3 lo, hi; Bounds(idle, out lo, out hi);
                T.Check(lo.Y > -0.35f && lo.Y < 0.2f, names[k] + ": feet near the ground (" + lo.Y + ")");
                T.Check(hi.Y > 2.7f && hi.Y < 3.7f, names[k] + ": height about 3 model units (" + hi.Y + ")");
                T.Check(lo.X > -1.3f && hi.X < 1.3f && lo.Z > -1.0f && hi.Z < 1.3f, names[k] + ": fits a small footprint");
                bool fin = true; foreach (HeroPrim p in idle) { if (float.IsNaN(p.C.X + p.C.Y + p.C.Z + p.A.X + p.A.Y + p.A.Z) || p.A.X < 0f || p.A.Y < 0f || p.A.Z < 0f) fin = false; }
                T.Check(fin, names[k] + ": finite, non-negative sizes");
                // 섞기: 0 이면 앞 자세, 1 이면 뒤 자세
                List<HeroPrim> a = HeroModel.Build(kind, 0f, false, HeroModel.Attack[k][0]), b = HeroModel.Build(kind, 0f, false, HeroModel.Attack[k][4]);
                List<HeroPrim> m0 = HeroModel.Blend(a, b, 0f), m1 = HeroModel.Blend(a, b, 1f), mh = HeroModel.Blend(a, b, 0.5f);
                float d0 = 0f, d1 = 0f, dh = 0f;
                for (int i = 0; i < a.Count; ++i) { d0 += (m0[i].C - a[i].C).Length; d1 += (m1[i].C - b[i].C).Length; dh += (mh[i].C - V3.Lerp(a[i].C, b[i].C, 0.5f)).Length; }
                T.Check(d0 < 1e-3f && d1 < 1e-3f && dh < 1e-3f, names[k] + ": blend hits both ends and the middle");
                // 회전은 직교를 유지
                bool ortho = true; foreach (HeroPrim p in mh) { V3 x = p.R.Col(0), y = p.R.Col(1), z = p.R.Col(2); if (Math.Abs(x.Length - 1f) > 1e-3f || Math.Abs(V3.Dot(x, y)) > 1e-3f || Math.Abs(V3.Dot(y, z)) > 1e-3f) ortho = false; }
                T.Check(ortho, names[k] + ": blended rotations stay orthonormal");
                // 공연: 한 주기 동안 도형 수가 변하지 않고, 공격 구간에서는 자세가 실제로 달라진다
                float moved = 0f; bool steady = true;
                for (float t = 0f; t < HeroShowcase.Period; t += 0.1f)
                {
                    List<HeroPrim> f = HeroShowcase.Frame(kind, t); if (f.Count != n) steady = false;
                    for (int i = 0; i < n; ++i) moved = Math.Max(moved, (f[i].C - idle[i].C).Length);
                }
                T.Check(steady, names[k] + ": showcase keeps the primitive count over a whole cycle");
                T.Check(moved > 0.25f, names[k] + ": the attack visibly moves something (" + moved + ")");
                T.Eq(HeroShowcase.Frame(kind, 0.3f).Count, HeroShowcase.Frame(kind, 0.3f + HeroShowcase.Period).Count, names[k] + ": showcase repeats");
            }
            // 무기는 오른쪽(-x)에서 앞으로 내밀어 진다: 창기사 공격 중 창끝이 몸보다 앞으로 나간다
            List<HeroPrim> lance = HeroModel.Build(HeroKind.Lancer, 0f, false, HeroModel.Attack[3][2]); V3 l0, h0; Bounds(lance, out l0, out h0);
            T.Check(h0.Z > 1.5f, "lancer thrust reaches forward (" + h0.Z + ")");
        }

        public static void HeroMeshes()
        {
            for (int k = 0; k < 4; ++k)
            {
                List<HeroPrim> ps = HeroModel.Build((HeroKind)k, 0f, false, null);
                HeroMeshData m = HeroMesh.Bake(ps, new V3(10f, 2f, -5f), 0.7f, 0.62f, null);
                T.Check(m.VertexCount > 5000 && m.VertexCount < 60000, "hero " + k + ": vertex budget (" + m.VertexCount + ")");
                T.Check(m.IndexCount % 3 == 0 && m.IndexCount > 0, "hero " + k + ": whole triangles");
                uint mx = 0; for (int i = 0; i < m.IndexCount; ++i) mx = Math.Max(mx, m.I[i]);
                T.Check(mx < (uint)m.VertexCount, "hero " + k + ": indices in range");
                bool finite = true, unit = true; int S = HeroMeshData.Stride;
                for (int v = 0; v < m.VertexCount; ++v)
                {
                    for (int c = 0; c < S; ++c) if (float.IsNaN(m.V[v * S + c]) || float.IsInfinity(m.V[v * S + c])) finite = false;
                    float nl = (float)Math.Sqrt(m.V[v * S + 3] * m.V[v * S + 3] + m.V[v * S + 4] * m.V[v * S + 4] + m.V[v * S + 5] * m.V[v * S + 5]);
                    if (Math.Abs(nl - 1f) > 0.01f) unit = false;
                }
                T.Check(finite, "hero " + k + ": finite vertex data"); T.Check(unit, "hero " + k + ": unit normals");
                // 삼각형 앞면(반시계)이 바깥 법선과 맞는지: 거의 모두 맞아야 뒷면 제거 + 윤곽선 껍질이 제대로 나온다
                int ok = 0, tot = 0;
                for (int i = 0; i + 2 < m.IndexCount; i += 3)
                {
                    int a = (int)m.I[i] * S, b = (int)m.I[i + 1] * S, c = (int)m.I[i + 2] * S;
                    V3 pa = new V3(m.V[a], m.V[a + 1], m.V[a + 2]), pb = new V3(m.V[b], m.V[b + 1], m.V[b + 2]), pc = new V3(m.V[c], m.V[c + 1], m.V[c + 2]);
                    V3 g = V3.Cross(pb - pa, pc - pa); if (g.Length < 1e-9f) continue;
                    V3 n = new V3(m.V[a + 3] + m.V[b + 3] + m.V[c + 3], m.V[a + 4] + m.V[b + 4] + m.V[c + 4], m.V[a + 5] + m.V[b + 5] + m.V[c + 5]);
                    ++tot; if (V3.Dot(g, n) > 0f) ++ok;
                }
                T.Check(tot > 0 && ok >= tot * 0.995, "hero " + k + ": triangles face outward (" + ok + "/" + tot + ")");
                // 발 위치/크기/방향: 바닥은 origin.y 근처, 키는 약 1.9 m
                float ymin = 1e9f, ymax = -1e9f; for (int v = 0; v < m.VertexCount; ++v) { ymin = Math.Min(ymin, m.V[v * S + 1]); ymax = Math.Max(ymax, m.V[v * S + 1]); }
                T.Check(ymin > 2f - 0.3f && ymin < 2f + 0.15f, "hero " + k + ": feet on the spot (" + (ymin - 2f) + ")");
                T.Check(ymax - 2f > 1.6f && ymax - 2f < 2.4f, "hero " + k + ": about 1.9 m tall (" + (ymax - 2f) + ")");
            }
            // yaw: 모델 +z(얼굴 방향)가 (sin yaw, 0, cos yaw) 로 간다
            List<HeroPrim> one = new List<HeroPrim>();
            one.Add(new HeroPrim { Kind = PrimKind.Ellipsoid, C = new V3(0, 0, 1), A = new V3(0.1f, 0.1f, 0.1f), Col = new V3(1, 1, 1), Sh = new V3(1, 1, 1), R = Mat3.Identity, K = 1f });
            HeroMeshData y = HeroMesh.Bake(one, new V3(0, 0, 0), (float)Math.PI / 2f, 1f, null);
            float cx = 0f, cz = 0f; for (int v = 0; v < y.VertexCount; ++v) { cx += y.V[v * 14]; cz += y.V[v * 14 + 2]; }
            T.Check(cx / y.VertexCount > 0.9f && Math.Abs(cz / y.VertexCount) < 0.05f, "yaw pi/2 turns +z into +x");
            // 크기 0 도형(숨긴 화살)은 버텍스를 만들지 않는다
            one[0] = new HeroPrim { Kind = PrimKind.Cone, C = new V3(0, 0, 0), A = new V3(0, 0, 0), Col = new V3(1, 1, 1), Sh = new V3(1, 1, 1), R = Mat3.Identity, K = 1f };
            T.Eq(0, HeroMesh.Bake(one, new V3(0, 0, 0), 0f, 1f, null).VertexCount, "zero-size primitives are skipped");
        }

        public static void HeroStage()
        {
            ForestAssets a = ForestAssets.Build(ForestAssets.DefaultSeed);
            ForestCamera cam = a.Camera;
            V3 pos, dir; cam.HeroSpot(out pos, out dir);
            T.Check(Math.Abs(dir.Length - 1f) < 1e-3f && Math.Abs(dir.Y) < 1e-6f, "hero faces a horizontal unit direction");
            // 카메라가 도는 동안 어느 나무와도 겹치지 않고, 영웅은 늘 화면 안 가운데쯤에 있다
            float minTree = 1e9f, minEyeH = 1e9f, maxEyeH = -1e9f, maxOff = 0f;
            for (float t = 0f; t < 120f; t += 0.25f)
            {
                CamPose p = cam.HeroSample(t);
                foreach (List<Inst> l in a.Layout.Trees) foreach (Inst tr in l)
                { float dx = tr.X - p.Eye.X, dz = tr.Z - p.Eye.Z; minTree = Math.Min(minTree, (float)Math.Sqrt(dx * dx + dz * dz)); }
                minEyeH = Math.Min(minEyeH, p.Eye.Y - pos.Y); maxEyeH = Math.Max(maxEyeH, p.Eye.Y - pos.Y);
                V3 toHero = (pos + new V3(0, 1.0f, 0) - p.Eye); float dist = toHero.Length; toHero = toHero * (1f / dist);
                maxOff = Math.Max(maxOff, (float)Math.Acos(Math.Max(-1f, Math.Min(1f, V3.Dot(toHero, p.Forward)))));
                T.Check(dist > 2.6f && dist < 3.8f, "orbit distance stays framed (" + dist + ")");
            }
            T.Check(minTree > 0.8f, "orbit camera keeps clear of tree trunks (" + minTree + " m)");
            T.Check(minEyeH > 1.0f && maxEyeH < 1.6f, "eye stays about chest height");
            T.Check(maxOff < 0.1f, "hero stays centered while orbiting (" + maxOff + " rad)");
            // 영웅 둘레 1.2m 안에 나무가 없다(몸을 가리지 않게)
            float near = 1e9f; foreach (List<Inst> l in a.Layout.Trees) foreach (Inst tr in l) near = Math.Min(near, (float)Math.Sqrt((tr.X - pos.X) * (tr.X - pos.X) + (tr.Z - pos.Z) * (tr.Z - pos.Z)));
            T.Check(near > 1.8f, "no tree beside the hero (" + near + " m)");
            // Pan: 목표가 원하는 화면 가로 위치에 온다
            foreach (float want in new[] { -0.5f, -0.25f, 0f, 0.3f })
            {
                CamPose p = ForestCamera.Pan(cam.HeroSample(3f), want, 16f / 9f);
                float aspect = 16f / 9f; float[] vp = Mat4.Multiply(Mat4.Perspective(p.FovY, aspect, 0.15f, 220f), Mat4.LookAt(p.Eye, p.Eye + p.Forward, p.Up));
                float w; V3 q = Mat4.Transform(vp, pos + new V3(0, 1.0f, 0), out w);
                T.Near(want, q.X / w, 0.03, "pan puts the hero at ndc x " + want);
            }
            // 영웅 그림자 지도: 영웅 몸 전체가 지도 안에 들어온다
            float[] lvp = ForestLighting.ShadowMatrixAt(pos + new V3(0, 1f, 0), 18f);
            bool inside = true;
            foreach (V3 off in new[] { new V3(0, 0, 0), new V3(0, 1.9f, 0), new V3(1f, 0, 1f), new V3(-1f, 0.5f, -1f) })
            { float w; V3 q = Mat4.Transform(lvp, pos + off, out w); if (Math.Abs(q.X / w) > 0.8f || Math.Abs(q.Y / w) > 0.8f || q.Z / w < -1f || q.Z / w > 1f) inside = false; }
            T.Check(inside, "hero fits in the tight shadow map");
        }
    }
}
