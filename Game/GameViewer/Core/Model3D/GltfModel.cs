//***************************************************************************
// GltfModel.cs : 스킨 메시 glTF 2.0(.gltf, 데이터 URI 또는 외부 .bin/이미지)을 읽는다. GL/System.Drawing 에 의존하지 않는다.
//   메시: 위치/법선/UV/뼈 번호(4)/뼈 가중치(4)/삼각형 인덱스를 한 덩어리로 합친다(메시 노드가 여럿이어도 같은 스킨이면 합친다).
//         스킨 없는 메시(무기, 투구, 어깨 갑옷처럼 뼈 노드에 붙은 강체)는 그 노드를 "가상 관절"로 삼아 같은 방식으로 그린다:
//         팔레트 = [스킨 관절 ... , 강체 노드 ...], 강체 정점은 가상 관절 하나에 가중치 1.
//         프리미티브마다 재질의 텍스처가 다를 수 있어 Parts(인덱스 범위 + 텍스처 번호)로 나눈다.
//   뼈대: 노드 계층(부모, 위상 순서), 기본 자세(이동/회전/확대), 스킨의 관절 노드와 inverse bind 행렬.
//   애니메이션: 이름별 클립. 채널은 translation / rotation(쿼터니언) / scale, 보간은 LINEAR 와 STEP.
//   텍스처: PNG/JPEG 바이트를 그대로 돌려준다(디코딩은 그리는 쪽이 한다).
// 행렬은 전부 열 우선 float[16] (glTF/OpenGL 과 같은 순서).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameViewer.Core.Model3D
{
    public sealed class AnimChannel
    {
        public int Node;
        public bool IsRotation, IsScale, Step;     // 아니면 이동
        public float[] Times;
        public float[] Values;                     // 키마다 3(이동/확대) 또는 4(회전) 개
    }

    public sealed class AnimClip
    {
        public string Name;
        public float Duration;
        public AnimChannel[] Channels;
    }

    public sealed class GltfModel
    {
        public string Name;
        public int VertexCount, IndexCount;
        public float[] Positions, Normals, Uvs;     // 3, 3, 2
        public float[] JointIds, Weights;           // 정점마다 4개(뼈 번호는 셰이더에서 float 로 받는다)
        public uint[] Indices;
        public byte[] TextureBytes;                 // 첫 이미지 파일 바이트(없으면 null). 호환용: Textures[0]
        public string TextureName;
        public byte[][] Textures = new byte[0][];   // 이미지 파일 바이트(PNG/JPEG)
        public struct Part { public int IndexStart, IndexCount, Texture; }
        public Part[] Parts;                        // 그리는 단위: 인덱스 범위와 Textures 번호
        public int[] RigidNodes = new int[0];       // 가상 관절이 가리키는 노드(팔레트 번호 = JointCount + 순번)

        public int NodeCount;
        public int[] Parent;                        // 없으면 -1
        public int[] Order;                         // 부모가 항상 자식보다 먼저
        public float[] RestT, RestR, RestS;         // 노드마다 3, 4, 3
        public int[] JointNodes;                    // 스킨의 관절 -> 노드
        public float[] InverseBind;                 // 관절마다 16
        public readonly Dictionary<string, AnimClip> Clips = new Dictionary<string, AnimClip>();

        public int JointCount { get { return JointNodes.Length; } }
        /// <summary>셰이더로 보내는 팔레트 행렬 수(스킨 관절 + 강체 가상 관절).</summary>
        public int PaletteCount { get { return JointNodes.Length + RigidNodes.Length; } }

        /// <summary>기본 자세에서의 위/아래 범위(모델 좌표 y). 크기 맞춤에 쓴다.</summary>
        public float MinY, MaxY;
        /// <summary>기본 자세의 가로 반경(원점에서 xz 평면으로 가장 먼 정점).</summary>
        public float RadiusXZ;

        /// <summary>GL 정점 버퍼용 인터리브 배열: 정점마다 위치3 법선3 uv2 관절4(float) 가중치4 = VertexStride 개.</summary>
        public const int VertexStride = 16;
        public float[] BuildVertexBuffer()
        {
            float[] vb = new float[VertexCount * VertexStride];
            for (int v = 0; v < VertexCount; ++v)
            {
                int o = v * VertexStride;
                Array.Copy(Positions, v * 3, vb, o, 3); Array.Copy(Normals, v * 3, vb, o + 3, 3); Array.Copy(Uvs, v * 2, vb, o + 6, 2);
                Array.Copy(JointIds, v * 4, vb, o + 8, 4); Array.Copy(Weights, v * 4, vb, o + 12, 4);
            }
            return vb;
        }

        public AnimClip Clip(string name) { AnimClip c; return name != null && Clips.TryGetValue(name, out c) ? c : null; }

        // ------------------------------------------------------------------ 읽기

        public static GltfModel Load(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            GltfModel m = Parse(File.ReadAllText(path), delegate (string uri) { return File.ReadAllBytes(Path.Combine(dir, uri)); });
            m.Name = Path.GetFileNameWithoutExtension(path);
            return m;
        }

        /// <summary>glTF JSON 을 읽는다. external 은 data: 가 아닌 uri(.bin, 이미지)를 바이트로 돌려주는 함수.</summary>
        public static GltfModel Parse(string json, Func<string, byte[]> external)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement g = doc.RootElement;
                List<byte[]> bins = new List<byte[]>();
                foreach (JsonElement b in g.GetProperty("buffers").EnumerateArray()) bins.Add(Uri(b.GetProperty("uri").GetString(), external));
                Reader r = new Reader { G = g, Bins = bins };

                GltfModel m = new GltfModel();
                // ---- 노드
                JsonElement nodes = g.GetProperty("nodes");
                int nn = nodes.GetArrayLength();
                m.NodeCount = nn; m.Parent = new int[nn]; m.RestT = new float[nn * 3]; m.RestR = new float[nn * 4]; m.RestS = new float[nn * 3];
                for (int i = 0; i < nn; ++i) m.Parent[i] = -1;
                int idx = 0;
                foreach (JsonElement n in nodes.EnumerateArray())
                {
                    m.RestR[idx * 4 + 3] = 1f; m.RestS[idx * 3] = m.RestS[idx * 3 + 1] = m.RestS[idx * 3 + 2] = 1f;
                    JsonElement p;
                    if (n.TryGetProperty("matrix", out p)) Decompose(Floats(p), m, idx);
                    if (n.TryGetProperty("translation", out p)) Copy(p, m.RestT, idx * 3, 3);
                    if (n.TryGetProperty("rotation", out p)) Copy(p, m.RestR, idx * 4, 4);
                    if (n.TryGetProperty("scale", out p)) Copy(p, m.RestS, idx * 3, 3);
                    if (n.TryGetProperty("children", out p)) foreach (JsonElement c in p.EnumerateArray()) m.Parent[c.GetInt32()] = idx;
                    ++idx;
                }
                List<int> order = new List<int>(); bool[] seen = new bool[nn];
                for (int i = 0; i < nn; ++i) Visit(i, m.Parent, seen, order);
                m.Order = order.ToArray();

                // ---- 이미지 / 재질 -> 이미지 번호
                List<byte[]> images = new List<byte[]>(); JsonElement imgs;
                if (g.TryGetProperty("images", out imgs))
                    foreach (JsonElement im in imgs.EnumerateArray())
                    {
                        JsonElement bv, u; byte[] bytes = null;
                        if (im.TryGetProperty("bufferView", out bv)) bytes = r.ViewBytes(bv.GetInt32());
                        else if (im.TryGetProperty("uri", out u)) bytes = Uri(u.GetString(), external);
                        images.Add(bytes);
                    }
                m.Textures = images.ToArray();
                m.TextureBytes = images.Count > 0 ? images[0] : null;
                if (imgs.ValueKind == JsonValueKind.Array && imgs.GetArrayLength() > 0) { JsonElement nm0; m.TextureName = imgs[0].TryGetProperty("name", out nm0) ? nm0.GetString() : null; }

                // ---- 메시: 스킨 메시 + 뼈 노드에 붙은 강체 메시
                int skin = -1;
                foreach (JsonElement n in nodes.EnumerateArray())
                {
                    JsonElement sk;
                    if (!n.TryGetProperty("mesh", out _) || !n.TryGetProperty("skin", out sk)) continue;
                    if (skin < 0) skin = sk.GetInt32(); else if (skin != sk.GetInt32()) throw new InvalidDataException("서로 다른 스킨을 쓰는 메시는 지원하지 않습니다.");
                }
                if (skin < 0) throw new InvalidDataException("스킨 메시가 없습니다.");
                JsonElement skinEl = g.GetProperty("skins")[skin];
                List<int> joints = new List<int>();
                foreach (JsonElement j in skinEl.GetProperty("joints").EnumerateArray()) joints.Add(j.GetInt32());
                m.JointNodes = joints.ToArray();
                m.InverseBind = r.Floats(skinEl.GetProperty("inverseBindMatrices").GetInt32(), 16);

                List<float> P = new List<float>(), N = new List<float>(), T = new List<float>(), J = new List<float>(), W = new List<float>();
                List<uint> I = new List<uint>(); List<Part> parts = new List<Part>(); List<int> rigid = new List<int>();
                int nodeIdx = -1;
                foreach (JsonElement n in nodes.EnumerateArray())
                {
                    ++nodeIdx;
                    JsonElement me;
                    if (!n.TryGetProperty("mesh", out me)) continue;
                    bool skinned = n.TryGetProperty("skin", out _);
                    int vjoint = -1;
                    if (!skinned) { vjoint = joints.Count + rigid.Count; rigid.Add(nodeIdx); }
                    foreach (JsonElement prim in g.GetProperty("meshes")[me.GetInt32()].GetProperty("primitives").EnumerateArray())
                    {
                        JsonElement at = prim.GetProperty("attributes");
                        float[] pos = r.Floats(at.GetProperty("POSITION").GetInt32(), 3);
                        int vc = pos.Length / 3, baseV = P.Count / 3;
                        P.AddRange(pos);
                        N.AddRange(r.Floats(at.GetProperty("NORMAL").GetInt32(), 3));
                        T.AddRange(r.Floats(at.GetProperty("TEXCOORD_0").GetInt32(), 2));
                        if (skinned)
                        {
                            J.AddRange(r.Floats(at.GetProperty("JOINTS_0").GetInt32(), 4));
                            W.AddRange(r.Floats(at.GetProperty("WEIGHTS_0").GetInt32(), 4));
                        }
                        else
                            for (int v = 0; v < vc; ++v) { J.Add(vjoint); J.Add(0); J.Add(0); J.Add(0); W.Add(1f); W.Add(0f); W.Add(0f); W.Add(0f); }
                        uint[] ind = r.Uints(prim.GetProperty("indices").GetInt32());
                        Part part; part.IndexStart = I.Count; part.IndexCount = ind.Length; part.Texture = TextureOf(g, prim, m.Textures.Length);
                        for (int k = 0; k < ind.Length; ++k) I.Add(ind[k] + (uint)baseV);
                        parts.Add(part);
                    }
                }
                m.Positions = P.ToArray(); m.Normals = N.ToArray(); m.Uvs = T.ToArray(); m.JointIds = J.ToArray(); m.Weights = W.ToArray(); m.Indices = I.ToArray();
                m.Parts = parts.ToArray(); m.RigidNodes = rigid.ToArray();
                m.VertexCount = m.Positions.Length / 3; m.IndexCount = m.Indices.Length;
                for (int v = 0; v < m.VertexCount; ++v)       // 가중치 합을 1 로
                {
                    float s = m.Weights[v * 4] + m.Weights[v * 4 + 1] + m.Weights[v * 4 + 2] + m.Weights[v * 4 + 3];
                    if (s > 1e-6f) for (int k = 0; k < 4; ++k) m.Weights[v * 4 + k] /= s;
                }

                // ---- 범위: 기본 자세로 스킨한 정점 중 스킨 메시(몸)만. 강체(무기 등)는 몸 크기에 넣지 않는다.
                {
                    Pose rest = new Pose(m); float[] pal = new float[m.PaletteCount * 16];
                    Skeleton.Palette(m, rest, pal);
                    m.MinY = float.MaxValue; m.MaxY = float.MinValue;
                    for (int v = 0; v < m.VertexCount; ++v)
                    {
                        if ((int)m.JointIds[v * 4] >= m.JointNodes.Length) continue;
                        float x = 0, y = 0, z = 0, px = m.Positions[v * 3], py = m.Positions[v * 3 + 1], pz = m.Positions[v * 3 + 2];
                        for (int k = 0; k < 4; ++k)
                        {
                            float wt = m.Weights[v * 4 + k]; if (wt == 0f) continue;
                            int b0 = (int)m.JointIds[v * 4 + k] * 16;
                            x += wt * (pal[b0] * px + pal[b0 + 4] * py + pal[b0 + 8] * pz + pal[b0 + 12]);
                            y += wt * (pal[b0 + 1] * px + pal[b0 + 5] * py + pal[b0 + 9] * pz + pal[b0 + 13]);
                            z += wt * (pal[b0 + 2] * px + pal[b0 + 6] * py + pal[b0 + 10] * pz + pal[b0 + 14]);
                        }
                        if (y < m.MinY) m.MinY = y; if (y > m.MaxY) m.MaxY = y;
                        float rr = (float)Math.Sqrt(x * x + z * z); if (rr > m.RadiusXZ) m.RadiusXZ = rr;
                    }
                    if (m.MinY > m.MaxY) { m.MinY = 0; m.MaxY = 1; }
                }

                // ---- 애니메이션
                JsonElement an;
                if (g.TryGetProperty("animations", out an))
                    foreach (JsonElement a in an.EnumerateArray())
                    {
                        AnimClip c = new AnimClip(); JsonElement nm;
                        c.Name = a.TryGetProperty("name", out nm) ? nm.GetString() : "anim" + m.Clips.Count;
                        JsonElement sams = a.GetProperty("samplers");
                        List<AnimChannel> chs = new List<AnimChannel>();
                        foreach (JsonElement ch in a.GetProperty("channels").EnumerateArray())
                        {
                            JsonElement tg = ch.GetProperty("target"), sm = sams[ch.GetProperty("sampler").GetInt32()];
                            string path = tg.GetProperty("path").GetString();
                            if (path != "translation" && path != "rotation" && path != "scale") continue;
                            JsonElement node; if (!tg.TryGetProperty("node", out node)) continue;
                            AnimChannel c2 = new AnimChannel();
                            c2.Node = node.GetInt32(); c2.IsRotation = path == "rotation"; c2.IsScale = path == "scale";
                            JsonElement ip; string interp = sm.TryGetProperty("interpolation", out ip) ? ip.GetString() : "LINEAR";
                            if (interp == "CUBICSPLINE") throw new InvalidDataException("CUBICSPLINE 보간은 지원하지 않습니다.");
                            c2.Step = interp == "STEP";
                            c2.Times = r.Floats(sm.GetProperty("input").GetInt32(), 1);
                            c2.Values = r.Floats(sm.GetProperty("output").GetInt32(), c2.IsRotation ? 4 : 3);
                            if (c2.Times.Length > 0 && c2.Times[c2.Times.Length - 1] > c.Duration) c.Duration = c2.Times[c2.Times.Length - 1];
                            chs.Add(c2);
                        }
                        c.Channels = chs.ToArray();
                        m.Clips[c.Name] = c;
                    }

                return m;
            }
        }

        // ------------------------------------------------------------------ 내부

        /// <summary>프리미티브의 재질 -> baseColorTexture -> 텍스처 -> 이미지 번호. 못 찾으면 0.</summary>
        private static int TextureOf(JsonElement g, JsonElement prim, int imageCount)
        {
            JsonElement mi, mats, tx;
            if (!prim.TryGetProperty("material", out mi) || !g.TryGetProperty("materials", out mats)) return 0;
            JsonElement mat = mats[mi.GetInt32()], pbr, bc, ti;
            if (!mat.TryGetProperty("pbrMetallicRoughness", out pbr) || !pbr.TryGetProperty("baseColorTexture", out bc) || !bc.TryGetProperty("index", out ti)) return 0;
            if (!g.TryGetProperty("textures", out tx)) return 0;
            JsonElement src; int img = tx[ti.GetInt32()].TryGetProperty("source", out src) ? src.GetInt32() : 0;
            return img >= 0 && img < imageCount ? img : 0;
        }

        private static void Visit(int i, int[] parent, bool[] seen, List<int> order)
        {
            if (seen[i]) return;
            if (parent[i] >= 0) Visit(parent[i], parent, seen, order);
            seen[i] = true; order.Add(i);
        }

        private static byte[] Uri(string uri, Func<string, byte[]> external)
        {
            if (uri.StartsWith("data:", StringComparison.Ordinal)) return Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
            return external(Uri_Unescape(uri));
        }
        private static string Uri_Unescape(string s) { return System.Uri.UnescapeDataString(s); }

        private static float[] Floats(JsonElement a) { float[] f = new float[a.GetArrayLength()]; int i = 0; foreach (JsonElement e in a.EnumerateArray()) f[i++] = (float)e.GetDouble(); return f; }
        private static void Copy(JsonElement a, float[] dst, int off, int n) { int i = 0; foreach (JsonElement e in a.EnumerateArray()) { if (i >= n) break; dst[off + i++] = (float)e.GetDouble(); } }

        /// <summary>node.matrix(열 우선)를 이동/회전/확대로 푼다(전단 없는 행렬만).</summary>
        private static void Decompose(float[] mt, GltfModel m, int i)
        {
            m.RestT[i * 3] = mt[12]; m.RestT[i * 3 + 1] = mt[13]; m.RestT[i * 3 + 2] = mt[14];
            float sx = (float)Math.Sqrt(mt[0] * mt[0] + mt[1] * mt[1] + mt[2] * mt[2]), sy = (float)Math.Sqrt(mt[4] * mt[4] + mt[5] * mt[5] + mt[6] * mt[6]), sz = (float)Math.Sqrt(mt[8] * mt[8] + mt[9] * mt[9] + mt[10] * mt[10]);
            m.RestS[i * 3] = sx; m.RestS[i * 3 + 1] = sy; m.RestS[i * 3 + 2] = sz;
            float m00 = mt[0] / sx, m10 = mt[1] / sx, m20 = mt[2] / sx, m01 = mt[4] / sy, m11 = mt[5] / sy, m21 = mt[6] / sy, m02 = mt[8] / sz, m12 = mt[9] / sz, m22 = mt[10] / sz;
            float tr = m00 + m11 + m22, qx, qy, qz, qw;
            if (tr > 0f) { float s = (float)Math.Sqrt(tr + 1f) * 2f; qw = 0.25f * s; qx = (m21 - m12) / s; qy = (m02 - m20) / s; qz = (m10 - m01) / s; }
            else if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f; qw = (m21 - m12) / s; qx = 0.25f * s; qy = (m01 + m10) / s; qz = (m02 + m20) / s; }
            else if (m11 > m22) { float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f; qw = (m02 - m20) / s; qx = (m01 + m10) / s; qy = 0.25f * s; qz = (m12 + m21) / s; }
            else { float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f; qw = (m10 - m01) / s; qx = (m02 + m20) / s; qy = (m12 + m21) / s; qz = 0.25f * s; }
            m.RestR[i * 4] = qx; m.RestR[i * 4 + 1] = qy; m.RestR[i * 4 + 2] = qz; m.RestR[i * 4 + 3] = qw;
        }

        private sealed class Reader
        {
            public JsonElement G; public List<byte[]> Bins;

            public byte[] ViewBytes(int view)
            {
                JsonElement bv = G.GetProperty("bufferViews")[view]; JsonElement o;
                int off = bv.TryGetProperty("byteOffset", out o) ? o.GetInt32() : 0, len = bv.GetProperty("byteLength").GetInt32();
                byte[] b = new byte[len]; Buffer.BlockCopy(Bins[bv.GetProperty("buffer").GetInt32()], off, b, 0, len); return b;
            }

            /// <summary>접근자를 float 로 읽는다(정규화된 정수는 0..1 / -1..1 로). comps 는 기대하는 성분 수.</summary>
            public float[] Floats(int acc, int comps)
            {
                JsonElement a = G.GetProperty("accessors")[acc];
                int n = a.GetProperty("count").GetInt32(), ct = a.GetProperty("componentType").GetInt32(), nc = Comps(a.GetProperty("type").GetString());
                if (nc != comps) throw new InvalidDataException("접근자 성분 수가 다릅니다.");
                bool norm = a.TryGetProperty("normalized", out JsonElement nz) && nz.GetBoolean();
                float[] f = new float[n * nc];
                int csz = Size(ct);
                byte[] buf; int start, stride;
                Locate(a, nc * csz, out buf, out start, out stride);
                for (int i = 0; i < n; ++i)
                    for (int k = 0; k < nc; ++k)
                    {
                        int o = start + i * stride + k * csz;
                        float v;
                        switch (ct)
                        {
                            case 5126: v = BitConverter.ToSingle(buf, o); break;
                            case 5121: v = buf[o]; if (norm) v /= 255f; break;
                            case 5120: v = (sbyte)buf[o]; if (norm) v = Math.Max(v / 127f, -1f); break;
                            case 5123: v = BitConverter.ToUInt16(buf, o); if (norm) v /= 65535f; break;
                            case 5122: v = BitConverter.ToInt16(buf, o); if (norm) v = Math.Max(v / 32767f, -1f); break;
                            case 5125: v = BitConverter.ToUInt32(buf, o); break;
                            default: throw new InvalidDataException("지원하지 않는 성분 형식 " + ct);
                        }
                        f[i * nc + k] = v;
                    }
                return f;
            }

            public uint[] Uints(int acc)
            {
                JsonElement a = G.GetProperty("accessors")[acc];
                int n = a.GetProperty("count").GetInt32(), ct = a.GetProperty("componentType").GetInt32(), csz = Size(ct);
                uint[] u = new uint[n];
                byte[] buf; int start, stride;
                Locate(a, csz, out buf, out start, out stride);
                for (int i = 0; i < n; ++i)
                {
                    int o = start + i * stride;
                    u[i] = ct == 5125 ? BitConverter.ToUInt32(buf, o) : ct == 5123 ? BitConverter.ToUInt16(buf, o) : buf[o];
                }
                return u;
            }

            private void Locate(JsonElement a, int elemSize, out byte[] buf, out int start, out int stride)
            {
                JsonElement bvi;
                if (!a.TryGetProperty("bufferView", out bvi)) throw new InvalidDataException("bufferView 없는 접근자(sparse)는 지원하지 않습니다.");
                JsonElement bv = G.GetProperty("bufferViews")[bvi.GetInt32()], o, s;
                buf = Bins[bv.GetProperty("buffer").GetInt32()];
                start = (bv.TryGetProperty("byteOffset", out o) ? o.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out o) ? o.GetInt32() : 0);
                stride = bv.TryGetProperty("byteStride", out s) && s.GetInt32() > 0 ? s.GetInt32() : elemSize;
            }

            private static int Comps(string t) { switch (t) { case "SCALAR": return 1; case "VEC2": return 2; case "VEC3": return 3; case "VEC4": return 4; case "MAT4": return 16; } throw new InvalidDataException("지원하지 않는 접근자 형식 " + t); }
            private static int Size(int ct) { switch (ct) { case 5120: case 5121: return 1; case 5122: case 5123: return 2; case 5125: case 5126: return 4; } throw new InvalidDataException("지원하지 않는 성분 형식 " + ct); }
        }
    }
}
