//***************************************************************************
// ForestRenderer.cs : 안개 낀 3D 숲을 OpenGL 로 그려 픽셀로 돌려준다.
//   패스: 그림자 깊이 -> 장면(하늘, 땅, 바위, 나무, 풀, 먼지; 4배 다중표본) -> 해상 -> 후처리(빛줄기, 비네팅, 색 보정)
//   모든 패스는 화면에 보이지 않는 FBO 에 그리고, 마지막에 glReadPixels 로 BGRA 바이트를 넘긴다(위아래 뒤집힌 순서).
//   UI 스레드에서만 호출한다(GL 컨텍스트는 만든 스레드에 묶인다).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace GameViewer.App.Forest
{
    internal sealed class ForestRenderer : IDisposable
    {
        // 텍스처 유닛 배정
        private const float HeroShadowExtent = 18f;      // 영웅 선택 화면의 그림자 지도 절반 폭(m): 약 1.8cm 칸이라 영웅 자신의 그림자도 또렷하다
        private const int UnitLeaf = 0, UnitGround = 1, UnitDetail = 2, UnitGrass = 3, UnitShadow = 5, UnitColor = 6, UnitDepth = 7;

        private sealed class Prog
        {
            public uint Id; private readonly Dictionary<string, int> _loc = new Dictionary<string, int>();
            public int this[string name]
            {
                get { int l; if (!_loc.TryGetValue(name, out l)) { l = Gl.GetUniformLocation(Id, name); _loc[name] = l; } return l; }
            }
            public void Use() { Gl.UseProgram(Id); }
            public void I(string n, int v) { Gl.Uniform1i(this[n], v); }
            public void F(string n, float v) { Gl.Uniform1f(this[n], v); }
            public void F2(string n, float a, float b) { Gl.Uniform2f(this[n], a, b); }
            public void F3(string n, V3 v) { Gl.Uniform3f(this[n], v.X, v.Y, v.Z); }
            public void M(string n, float[] m) { Gl.Mat(this[n], m); }
        }

        private sealed class GpuMesh
        {
            public uint Vao, Vbo, Ibo, Inst; public int IndexCount, InstCount; public int Mode; public V3 Bark;
        }

        private GlContext _ctx;
        private ForestAssets _a;                 // Init 동안만 쥔다(GPU 로 올린 뒤 놓는다)
        private ForestCamera _camera;
        private Prog _shadow, _mesh, _terrain, _grass, _sky, _mote, _post, _hero, _heroShadow;
        private uint _heroVao, _heroVbo, _heroIbo; private int _heroIndexCount; private HeroMeshData _heroBake;
        private uint _texLeaf, _texGrass, _texGround, _texDetail;
        private readonly List<GpuMesh> _trees = new List<GpuMesh>(), _rocks = new List<GpuMesh>();
        private GpuMesh _terrainMesh, _grassMesh;
        private uint _moteVao, _moteBuf, _emptyVao; private int _moteCount;
        private uint _shadowFbo, _shadowTex;
        private uint _msFbo, _msColor, _msDepth, _sceneFbo, _sceneColor, _sceneDepth, _postFbo, _postColor;
        private int _w, _h, _samples;
        private byte[] _pixels = new byte[0];
        private int _errorsLogged;

        public string Info { get; private set; }
        public string LastError { get; private set; }
        public int Width { get { return _w; } }
        public int Height { get { return _h; } }
        /// <summary>마지막으로 그린 화면(BGRA, 아래 줄부터). Render 가 true 를 돌려준 직후에만 유효.</summary>
        public byte[] Pixels { get { return _pixels; } }
        public int Samples { get { return _samples; } }

        public void Init(ForestAssets assets)
        {
            _a = assets;
            _ctx = new GlContext();
            _ctx.Create();
            Info = _ctx.Description;
            _shadow = Build(ForestShaders.ShadowVs, ForestShaders.ShadowFs, "shadow");
            _mesh = Build(ForestShaders.MeshVs, ForestShaders.MeshFs, "mesh");
            _terrain = Build(ForestShaders.MeshVs, ForestShaders.TerrainFs, "terrain");
            _grass = Build(ForestShaders.GrassVs, ForestShaders.GrassFs, "grass");
            _sky = Build(ForestShaders.SkyVs, ForestShaders.SkyFs, "sky");
            _mote = Build(ForestShaders.MoteVs, ForestShaders.MoteFs, "mote");
            _post = Build(ForestShaders.PostVs, ForestShaders.PostFs, "post");
            _hero = Build(ForestShaders.HeroVs, ForestShaders.HeroFs, "hero");
            _heroShadow = Build(ForestShaders.HeroShadowVs, ForestShaders.HeroShadowFs, "heroShadow");
            InitHeroBuffers();

            _texLeaf = Texture(_a.LeafTex, false); _texGrass = Texture(_a.GrassTex, false);
            _texGround = Texture(_a.GroundTex, false); _texDetail = Texture(_a.DetailTex, true);

            for (int v = 0; v < ForestLayout.TreeVariants; ++v)
            {
                V3 bark = v == 3 ? new V3(0.55f, 0.52f, 0.46f) : new V3(0.26f, 0.20f, 0.15f);
                if (_a.TreeInst[v].Length == 0) continue;
                GpuMesh t = Mesh(_a.Trunks[v], _a.TreeInst[v], 0); t.Bark = bark; _trees.Add(t);
                GpuMesh l = Mesh(_a.Leaves[v], _a.TreeInst[v], 1); l.Bark = bark; _trees.Add(l);
            }
            for (int v = 0; v < ForestLayout.RockVariants; ++v) if (_a.RockInst[v].Length > 0) _rocks.Add(Mesh(_a.Rocks[v], _a.RockInst[v], 2));
            _terrainMesh = Mesh(_a.Terrain, new float[] { 0, 0, 0, 0, 1, 1, 0, 0 }, 0);
            _grassMesh = Mesh(_a.GrassTuft, _a.GrassInst, 0);

            float[] motes = _a.Layout.Motes; _moteCount = motes.Length / 4;
            _moteVao = Gl.GenVao(); _moteBuf = Gl.GenBuffer();
            Gl.BindVertexArray(_moteVao); Gl.UploadBuffer(Gl.ARRAY_BUFFER, _moteBuf, motes, motes.Length * 4);
            Gl.EnableVertexAttribArray(0); Gl.VertexAttribPointer(0, 4, Gl.FLOAT, 0, 16, IntPtr.Zero);
            Gl.BindVertexArray(0);
            _emptyVao = Gl.GenVao();

            // 그림자 깊이 지도: 하드웨어 비교 없이 셰이더에서 직접 비교한다
            _shadowTex = Gl.GenTexture(); Gl.BindTexture(Gl.TEXTURE_2D, _shadowTex);
            Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.DEPTH_COMPONENT24, ForestLighting.ShadowSize, ForestLighting.ShadowSize, 0, Gl.DEPTH_COMPONENT, Gl.UNSIGNED_INT, IntPtr.Zero);
            SamplerParams(Gl.NEAREST, Gl.NEAREST);
            _shadowFbo = Gl.GenFbo(); Gl.BindFramebuffer(Gl.FRAMEBUFFER, _shadowFbo);
            Gl.FramebufferTexture2D(Gl.FRAMEBUFFER, Gl.DEPTH_ATTACHMENT, Gl.TEXTURE_2D, _shadowTex, 0);
            Gl.DrawBuffers(1, new uint[] { Gl.NONE }); Gl.ReadBuffer(Gl.NONE);
            if (Gl.CheckFramebufferStatus(Gl.FRAMEBUFFER) != Gl.FRAMEBUFFER_COMPLETE) throw new InvalidOperationException("그림자 FBO 가 완성되지 않았습니다.");
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, 0);
            _camera = _a.Camera; _a = null;       // CPU 쪽 메시/텍스처 복사본은 더 필요 없다
            Check("init");
        }

        // ------------------------------------------------------------------ 자원 만들기

        private static string Ascii(string s)
        {
            // 일부 드라이버는 주석의 한글(비 ASCII)도 컴파일 오류로 본다: 설명 주석은 한글이므로 비 ASCII 글자는 모두 뺀다
            StringBuilder b = new StringBuilder(s.Length);
            foreach (char c in s) if (c < 128) b.Append(c);
            return b.ToString();
        }

        private static uint Compile(uint type, string src, string what)
        {
            uint sh = Gl.CreateShader(type);
            Gl.ShaderSource(sh, 1, new[] { Ascii(src) }, null);
            Gl.CompileShader(sh);
            int ok; Gl.GetShaderiv(sh, Gl.COMPILE_STATUS, out ok);
            if (ok == 0)
            {
                int len; StringBuilder log = new StringBuilder(4096); Gl.GetShaderInfoLog(sh, log.Capacity, out len, log);
                Gl.DeleteShader(sh);
                throw new InvalidOperationException(what + " 셰이더 컴파일 실패: " + log);
            }
            return sh;
        }

        private static Prog Build(string vs, string fs, string name)
        {
            uint v = Compile(Gl.VERTEX_SHADER, vs, name + ".vs"), f = Compile(Gl.FRAGMENT_SHADER, fs, name + ".fs");
            uint p = Gl.CreateProgram(); Gl.AttachShader(p, v); Gl.AttachShader(p, f); Gl.LinkProgram(p);
            int ok; Gl.GetProgramiv(p, Gl.LINK_STATUS, out ok);
            if (ok == 0)
            {
                int len; StringBuilder log = new StringBuilder(4096); Gl.GetProgramInfoLog(p, log.Capacity, out len, log);
                throw new InvalidOperationException(name + " 프로그램 링크 실패: " + log);
            }
            Gl.DeleteShader(v); Gl.DeleteShader(f);
            Prog r = new Prog(); r.Id = p; return r;
        }

        private static void SamplerParams(int min, int mag)
        {
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MIN_FILTER, min); Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MAG_FILTER, mag);
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_S, Gl.CLAMP_TO_EDGE); Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_T, Gl.CLAMP_TO_EDGE);
        }

        private static uint Texture(TexImage img, bool repeat)
        {
            uint t = Gl.GenTexture(); Gl.BindTexture(Gl.TEXTURE_2D, t);
            GCHandle h = GCHandle.Alloc(img.Rgba, GCHandleType.Pinned);
            try { Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA8, img.W, img.H, 0, Gl.RGBA, Gl.UNSIGNED_BYTE, h.AddrOfPinnedObject()); } finally { h.Free(); }
            Gl.GenerateMipmap(Gl.TEXTURE_2D);
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MIN_FILTER, Gl.LINEAR_MIPMAP_LINEAR); Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MAG_FILTER, Gl.LINEAR);
            int wrap = repeat ? Gl.REPEAT : Gl.CLAMP_TO_EDGE;
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_S, wrap); Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_T, wrap);
            float maxA; Gl.GetFloatv(Gl.MAX_TEXTURE_MAX_ANISOTROPY, out maxA);
            if (maxA >= 2f) Gl.TexParameterf(Gl.TEXTURE_2D, Gl.TEXTURE_MAX_ANISOTROPY, Math.Min(8f, maxA));
            Gl.GetError();                       // 비등방성 확장이 없으면 생기는 오류는 무시
            return t;
        }

        private static GpuMesh Mesh(MeshData m, float[] inst, int mode)
        {
            GpuMesh g = new GpuMesh(); g.Mode = mode; g.IndexCount = m.I.Length; g.InstCount = inst.Length / 8;
            g.Vao = Gl.GenVao(); g.Vbo = Gl.GenBuffer(); g.Ibo = Gl.GenBuffer(); g.Inst = Gl.GenBuffer();
            Gl.BindVertexArray(g.Vao);
            Gl.UploadBuffer(Gl.ARRAY_BUFFER, g.Vbo, m.V, m.V.Length * 4);
            int stride = MeshData.Stride * 4;
            Gl.EnableVertexAttribArray(0); Gl.VertexAttribPointer(0, 3, Gl.FLOAT, 0, stride, IntPtr.Zero);
            Gl.EnableVertexAttribArray(1); Gl.VertexAttribPointer(1, 3, Gl.FLOAT, 0, stride, (IntPtr)12);
            Gl.EnableVertexAttribArray(2); Gl.VertexAttribPointer(2, 2, Gl.FLOAT, 0, stride, (IntPtr)24);
            Gl.EnableVertexAttribArray(3); Gl.VertexAttribPointer(3, 1, Gl.FLOAT, 0, stride, (IntPtr)32);
            Gl.UploadBuffer(Gl.ARRAY_BUFFER, g.Inst, inst, inst.Length * 4);
            Gl.EnableVertexAttribArray(4); Gl.VertexAttribPointer(4, 4, Gl.FLOAT, 0, 32, IntPtr.Zero); Gl.VertexAttribDivisor(4, 1);
            Gl.EnableVertexAttribArray(5); Gl.VertexAttribPointer(5, 4, Gl.FLOAT, 0, 32, (IntPtr)16); Gl.VertexAttribDivisor(5, 1);
            Gl.UploadBuffer(Gl.ELEMENT_ARRAY_BUFFER, g.Ibo, m.I, m.I.Length * 4);   // VAO 에 묶이는 인덱스 버퍼
            Gl.BindVertexArray(0);
            return g;
        }

        private void Targets(int w, int h)
        {
            if (w == _w && h == _h && _sceneFbo != 0) return;
            FreeTargets();
            _w = w; _h = h;
            // 장면 FBO(해상된 색 + 깊이): 후처리가 읽는다
            _sceneColor = Gl.GenTexture(); Gl.BindTexture(Gl.TEXTURE_2D, _sceneColor);
            Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA8, w, h, 0, Gl.RGBA, Gl.UNSIGNED_BYTE, IntPtr.Zero); SamplerParams(Gl.LINEAR, Gl.LINEAR);
            _sceneDepth = Gl.GenTexture(); Gl.BindTexture(Gl.TEXTURE_2D, _sceneDepth);
            Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.DEPTH_COMPONENT24, w, h, 0, Gl.DEPTH_COMPONENT, Gl.UNSIGNED_INT, IntPtr.Zero); SamplerParams(Gl.NEAREST, Gl.NEAREST);
            _sceneFbo = Gl.GenFbo(); Gl.BindFramebuffer(Gl.FRAMEBUFFER, _sceneFbo);
            Gl.FramebufferTexture2D(Gl.FRAMEBUFFER, Gl.COLOR_ATTACHMENT0, Gl.TEXTURE_2D, _sceneColor, 0);
            Gl.FramebufferTexture2D(Gl.FRAMEBUFFER, Gl.DEPTH_ATTACHMENT, Gl.TEXTURE_2D, _sceneDepth, 0);
            if (Gl.CheckFramebufferStatus(Gl.FRAMEBUFFER) != Gl.FRAMEBUFFER_COMPLETE) throw new InvalidOperationException("장면 FBO 가 완성되지 않았습니다.");
            // 다중표본 FBO(실제로 그리는 곳). 지원하지 않으면 장면 FBO 에 바로 그린다.
            _samples = 0;
            int maxS; Gl.GetIntegerv(Gl.MAX_SAMPLES, out maxS);
            int want = Math.Min(4, maxS);
            if (want >= 2)
            {
                _msColor = Gl.GenRbo(); Gl.BindRenderbuffer(Gl.RENDERBUFFER, _msColor); Gl.RenderbufferStorageMultisample(Gl.RENDERBUFFER, want, Gl.RGBA8, w, h);
                _msDepth = Gl.GenRbo(); Gl.BindRenderbuffer(Gl.RENDERBUFFER, _msDepth); Gl.RenderbufferStorageMultisample(Gl.RENDERBUFFER, want, Gl.DEPTH_COMPONENT24, w, h);
                _msFbo = Gl.GenFbo(); Gl.BindFramebuffer(Gl.FRAMEBUFFER, _msFbo);
                Gl.FramebufferRenderbuffer(Gl.FRAMEBUFFER, Gl.COLOR_ATTACHMENT0, Gl.RENDERBUFFER, _msColor);
                Gl.FramebufferRenderbuffer(Gl.FRAMEBUFFER, Gl.DEPTH_ATTACHMENT, Gl.RENDERBUFFER, _msDepth);
                if (Gl.CheckFramebufferStatus(Gl.FRAMEBUFFER) == Gl.FRAMEBUFFER_COMPLETE) _samples = want;
                else { Gl.DeleteFbo(_msFbo); Gl.DeleteRbo(_msColor); Gl.DeleteRbo(_msDepth); _msFbo = _msColor = _msDepth = 0; }
            }
            _postColor = Gl.GenTexture(); Gl.BindTexture(Gl.TEXTURE_2D, _postColor);
            Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA8, w, h, 0, Gl.RGBA, Gl.UNSIGNED_BYTE, IntPtr.Zero); SamplerParams(Gl.LINEAR, Gl.LINEAR);
            _postFbo = Gl.GenFbo(); Gl.BindFramebuffer(Gl.FRAMEBUFFER, _postFbo);
            Gl.FramebufferTexture2D(Gl.FRAMEBUFFER, Gl.COLOR_ATTACHMENT0, Gl.TEXTURE_2D, _postColor, 0);
            if (Gl.CheckFramebufferStatus(Gl.FRAMEBUFFER) != Gl.FRAMEBUFFER_COMPLETE) throw new InvalidOperationException("후처리 FBO 가 완성되지 않았습니다.");
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, 0);
            if (_pixels.Length != w * h * 4) _pixels = new byte[w * h * 4];
        }

        private void FreeTargets()
        {
            Gl.DeleteFbo(_sceneFbo); Gl.DeleteFbo(_msFbo); Gl.DeleteFbo(_postFbo);
            Gl.DeleteRbo(_msColor); Gl.DeleteRbo(_msDepth);
            Gl.DeleteTex(_sceneColor); Gl.DeleteTex(_sceneDepth); Gl.DeleteTex(_postColor);
            _sceneFbo = _msFbo = _postFbo = _msColor = _msDepth = _sceneColor = _sceneDepth = _postColor = 0;
        }

        // ------------------------------------------------------------------ 그리기

        private static void Bind(int unit, uint tex) { Gl.ActiveTexture(Gl.TEXTURE0 + (uint)unit); Gl.BindTexture(Gl.TEXTURE_2D, tex); }

        private static void Draw(GpuMesh m) { Gl.BindVertexArray(m.Vao); Gl.DrawElementsInstanced(Gl.TRIANGLES, m.IndexCount, Gl.UNSIGNED_INT, IntPtr.Zero, m.InstCount); }

        private void Common(Prog p, CamPose cam, float t, float[] lvp)
        {
            p.F3("uCam", cam.Eye); p.F3("uSunDir", ForestLighting.SunDir); p.F("uFogDensity", ForestLighting.FogDensity); p.F("uTime", t);
            p.I("uShadow", UnitShadow); p.M("uLightVP", lvp); p.F("uShadowTexel", 1f / ForestLighting.ShadowSize); p.F("uShadowOn", 1f);
        }

        /// <summary>시각 t(초)의 숲을 w x h 로 그려 Pixels 에 채운다(걷는 카메라).</summary>
        public void Render(float t, int w, int h) { Render(t, w, h, -1, 0f, 0f); }

        /// <summary>
        /// heroKind 가 0 이상이면 영웅 선택 화면: 길 위에 선 영웅(HeroKind 번호)을 도는 카메라로 그린다.
        /// heroT = 영웅이 나타난 뒤 흐른 시간(초), heroNdcX = 영웅이 화면 가로 어디에 보일지(-1 왼쪽 .. 1 오른쪽).
        /// </summary>
        /// <summary>true 면 영웅의 몸(절차적 모델)은 그리지 않고 마법진/불씨만 그린다(몸은 호출한 쪽이 glTF 로 위에 얹는다).</summary>
        public bool HeroBodyOff;
        /// <summary>마지막으로 그린 영웅의 화면 위치(0..1, 아래로 갈수록 큰 y): 발 가운데 x/y 와 머리 끝 y(영웅 키 3 모델 단위 = 1.9m). 영웅 없이 그렸으면 false.</summary>
        public bool HeroScreenValid; public float HeroFootX, HeroFootY, HeroHeadY;

        private static void Project(float[] vp, V3 p, out float sx, out float sy)
        {
            float cx = vp[0] * p.X + vp[4] * p.Y + vp[8] * p.Z + vp[12], cy = vp[1] * p.X + vp[5] * p.Y + vp[9] * p.Z + vp[13], cw = vp[3] * p.X + vp[7] * p.Y + vp[11] * p.Z + vp[15];
            if (Math.Abs(cw) < 1e-6f) cw = 1e-6f;
            sx = (cx / cw) * 0.5f + 0.5f; sy = 1f - ((cy / cw) * 0.5f + 0.5f);
        }

        public void Render(float t, int w, int h, int heroKind, float heroT, float heroNdcX)
        {
            _ctx.EnsureCurrent();
            Targets(w, h);
            float aspect = (float)w / h;
            bool hero = heroKind >= 0 && heroKind <= 3;
            CamPose cam; V3 hpos = default(V3), hdir = default(V3);
            if (hero)
            {
                _camera.HeroSpot(out hpos, out hdir);
                cam = ForestCamera.Pan(_camera.HeroSample(t), heroNdcX, aspect);
                float breathe = 0.012f * (float)Math.Sin(heroT * 2.1f);
                float pop = 1f - (float)Math.Exp(-heroT * 7f);                       // 새로 고른 영웅이 살짝 커지며 나타난다
                float yaw = (float)Math.Atan2(hdir.X, hdir.Z);
                List<HeroPrim> prims = HeroBodyOff ? new List<HeroPrim>() : HeroShowcase.Frame((HeroKind)heroKind, heroT);
                float atkU = (heroT % HeroShowcase.Period - HeroShowcase.AtkStart) / HeroShowcase.AtkLen;      // 0..1 이 공격 구간
                prims.AddRange(HeroFx.Build((HeroKind)heroKind, heroT, pop, atkU, !HeroBodyOff));
                _heroBake = HeroMesh.Bake(prims, hpos + new V3(0f, breathe, 0f), yaw, ForestCamera.HeroScale * (0.94f + 0.06f * pop), _heroBake);
                UploadHero();
            }
            else cam = _camera.Sample(t);
            float[] vp = Mat4.Multiply(Mat4.Perspective(cam.FovY, aspect, 0.15f, 220f), Mat4.LookAt(cam.Eye, cam.Eye + cam.Forward, cam.Up));
            HeroScreenValid = false;
            if (hero)
            {
                float fx, fy, hx, hy; Project(vp, hpos, out fx, out fy); Project(vp, hpos + new V3(0f, 3f * ForestCamera.HeroScale, 0f), out hx, out hy);
                HeroFootX = fx; HeroFootY = fy; HeroHeadY = hy; HeroScreenValid = true;
            }
            float[] lvp = hero ? ForestLighting.ShadowMatrixAt(hpos + new V3(0f, 1.0f, 0f), HeroShadowExtent) : ForestLighting.ShadowMatrix(cam.Eye, cam.Forward);
            float su, sv, ss; ForestLighting.SunOnScreen(vp, cam.Eye, out su, out sv, out ss);
            V3 right = V3.Cross(cam.Forward, cam.Up).Normalized();
            float tanY = (float)Math.Tan(cam.FovY * 0.5f), tanX = tanY * aspect;

            // 1) 그림자 깊이 (읽는 쪽 유닛을 비워 두어 쓰는 텍스처를 동시에 읽는 일이 없게 한다)
            Bind(UnitShadow, 0); Bind(UnitColor, 0); Bind(UnitDepth, 0);
            int sz = ForestLighting.ShadowSize;
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, _shadowFbo); Gl.Viewport(0, 0, sz, sz);
            Gl.Disable(Gl.CULL_FACE); Gl.Disable(Gl.BLEND); Gl.Enable(Gl.DEPTH_TEST); Gl.DepthFunc(Gl.LESS); Gl.DepthMask(1);
            Gl.Clear(Gl.DEPTH_BUFFER_BIT);
            _shadow.Use(); _shadow.M("uLightVP", lvp); Bind(UnitLeaf, _texLeaf); _shadow.I("uLeafTex", UnitLeaf);
            foreach (GpuMesh m in _trees) { _shadow.I("uMode", m.Mode); Draw(m); }
            if (hero && _heroIndexCount > 0) { _heroShadow.Use(); _heroShadow.M("uLightVP", lvp); DrawHero(); }

            // 2) 장면
            bool ms = _samples > 0;
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, ms ? _msFbo : _sceneFbo); Gl.Viewport(0, 0, w, h);
            Gl.ClearColor(0.5f, 0.6f, 0.6f, 1f); Gl.DepthMask(1); Gl.Clear(Gl.COLOR_BUFFER_BIT | Gl.DEPTH_BUFFER_BIT);
            Bind(UnitLeaf, _texLeaf); Bind(UnitGround, _texGround); Bind(UnitDetail, _texDetail); Bind(UnitGrass, _texGrass); Bind(UnitShadow, _shadowTex);

            Gl.DepthFunc(Gl.LEQUAL); Gl.DepthMask(0);                                   // 하늘: 깊이를 쓰지 않는다
            _sky.Use(); Common(_sky, cam, t, lvp);
            Gl.Uniform3f(_sky["uFwd"], cam.Forward.X, cam.Forward.Y, cam.Forward.Z); Gl.Uniform3f(_sky["uRight"], right.X, right.Y, right.Z); Gl.Uniform3f(_sky["uUp"], cam.Up.X, cam.Up.Y, cam.Up.Z);
            _sky.F2("uTan", tanX, tanY);
            Gl.BindVertexArray(_emptyVao); Gl.DrawArrays(Gl.TRIANGLES, 0, 3);
            Gl.DepthMask(1); Gl.DepthFunc(Gl.LESS);

            _terrain.Use(); Common(_terrain, cam, t, lvp); _terrain.M("uVP", vp); _terrain.F("uWind", 0f); _terrain.I("uGround", UnitGround); _terrain.I("uDetail", UnitDetail);
            Draw(_terrainMesh);

            float a2c = ms ? 1f : 0f;
            if (ms) Gl.Enable(Gl.SAMPLE_ALPHA_TO_COVERAGE);                             // 잎/풀 가장자리를 다중표본으로 부드럽게
            _mesh.Use(); Common(_mesh, cam, t, lvp); _mesh.M("uVP", vp); _mesh.I("uLeafTex", UnitLeaf); _mesh.F("uA2C", a2c);
            foreach (GpuMesh m in _rocks) { _mesh.I("uMode", 2); _mesh.F("uWind", 0f); Gl.Uniform3f(_mesh["uBark"], 0.3f, 0.22f, 0.16f); Draw(m); }
            foreach (GpuMesh m in _trees) { _mesh.I("uMode", m.Mode); _mesh.F("uWind", m.Mode == 1 ? 1f : 0f); Gl.Uniform3f(_mesh["uBark"], m.Bark.X, m.Bark.Y, m.Bark.Z); Draw(m); }

            _grass.Use(); Common(_grass, cam, t, lvp); _grass.M("uVP", vp); _grass.I("uGrassTex", UnitGrass); _grass.F("uA2C", a2c);
            Draw(_grassMesh);
            if (ms) Gl.Disable(Gl.SAMPLE_ALPHA_TO_COVERAGE);

            if (hero && _heroIndexCount > 0)
            {
                _hero.Use(); Common(_hero, cam, t, lvp); _hero.M("uVP", vp);
                float pxWorld = h * 0.0022f * 2f * (float)Math.Tan(cam.FovY * 0.5f) / h;          // 윤곽선 폭: 화면에서 약 h*0.0022 픽셀
                _hero.F("uOutline", pxWorld);
                V3 au = HeroFx.Aura((HeroKind)heroKind);
                Gl.Uniform3f(_hero["uAura"], au.X, au.Y, au.Z); _hero.F("uFoot", hpos.Y);
                Gl.Enable(Gl.CULL_FACE); Gl.FrontFace(Gl.CCW);
                Gl.CullFace(Gl.FRONT); _hero.F("uOutlinePass", 1f); DrawHero();                    // 뒤집은 껍질(윤곽선)
                Gl.CullFace(Gl.BACK); _hero.F("uOutlinePass", 0f); DrawHero();                     // 몸
                Gl.Disable(Gl.CULL_FACE);
            }

            Gl.Enable(Gl.BLEND); Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE); Gl.DepthMask(0); Gl.Enable(Gl.PROGRAM_POINT_SIZE);   // 먼지: 더해 그린다
            _mote.Use(); Common(_mote, cam, t, lvp); _mote.M("uVP", vp); _mote.F("uPx", h * 0.02f);
            Gl.BindVertexArray(_moteVao); Gl.DrawArrays(Gl.POINTS, 0, _moteCount);
            Gl.Disable(Gl.BLEND); Gl.DepthMask(1);

            // 3) 해상(다중표본 -> 장면 텍스처)
            if (ms)
            {
                Gl.BindFramebuffer(Gl.READ_FRAMEBUFFER, _msFbo); Gl.BindFramebuffer(Gl.DRAW_FRAMEBUFFER, _sceneFbo);
                Gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, Gl.COLOR_BUFFER_BIT | Gl.DEPTH_BUFFER_BIT, (uint)Gl.NEAREST);
            }

            // 4) 후처리
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, _postFbo); Gl.Viewport(0, 0, w, h); Gl.Disable(Gl.DEPTH_TEST);
            Bind(UnitColor, _sceneColor); Bind(UnitDepth, _sceneDepth);
            _post.Use(); _post.I("uColor", UnitColor); _post.I("uDepth", UnitDepth); _post.F2("uSunUv", su, sv); _post.F("uSunVis", ss); _post.F2("uRes", w, h); _post.F("uTime", t);
            Gl.BindVertexArray(_emptyVao); Gl.DrawArrays(Gl.TRIANGLES, 0, 3);

            // 5) 읽기
            Gl.PixelStorei(Gl.PACK_ALIGNMENT, 1);
            GCHandle hp = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
            try { Gl.ReadPixels(0, 0, w, h, Gl.BGRA, Gl.UNSIGNED_BYTE, hp.AddrOfPinnedObject()); } finally { hp.Free(); }
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, 0);
            Check("frame");
        }

        private void InitHeroBuffers()
        {
            _heroVao = Gl.GenVao(); _heroVbo = Gl.GenBuffer(); _heroIbo = Gl.GenBuffer();
            Gl.BindVertexArray(_heroVao);
            Gl.BindBuffer(Gl.ARRAY_BUFFER, _heroVbo);
            int stride = HeroMeshData.Stride * 4;
            Gl.EnableVertexAttribArray(0); Gl.VertexAttribPointer(0, 3, Gl.FLOAT, 0, stride, IntPtr.Zero);
            Gl.EnableVertexAttribArray(1); Gl.VertexAttribPointer(1, 3, Gl.FLOAT, 0, stride, (IntPtr)12);
            Gl.EnableVertexAttribArray(2); Gl.VertexAttribPointer(2, 3, Gl.FLOAT, 0, stride, (IntPtr)24);
            Gl.EnableVertexAttribArray(3); Gl.VertexAttribPointer(3, 3, Gl.FLOAT, 0, stride, (IntPtr)36);
            Gl.EnableVertexAttribArray(4); Gl.VertexAttribPointer(4, 2, Gl.FLOAT, 0, stride, (IntPtr)48);
            Gl.BindBuffer(Gl.ELEMENT_ARRAY_BUFFER, _heroIbo);
            Gl.BindVertexArray(0);
        }

        /// <summary>이번 프레임의 영웅 메시를 GPU 로 올린다(VAO 에 묶인 인덱스 버퍼 포함).</summary>
        private void UploadHero()
        {
            Gl.BindVertexArray(_heroVao);
            Gl.UploadBuffer(Gl.ARRAY_BUFFER, _heroVbo, _heroBake.V, _heroBake.VertexCount * HeroMeshData.Stride * 4);
            Gl.UploadBuffer(Gl.ELEMENT_ARRAY_BUFFER, _heroIbo, _heroBake.I, _heroBake.IndexCount * 4);
            Gl.BindVertexArray(0);
            _heroIndexCount = _heroBake.IndexCount;
        }

        private void DrawHero() { Gl.BindVertexArray(_heroVao); Gl.DrawElements(Gl.TRIANGLES, _heroIndexCount, Gl.UNSIGNED_INT, IntPtr.Zero); }

        private void Check(string where)
        {
            uint e;
            while ((e = Gl.GetError()) != 0)
            {
                if (_errorsLogged++ < 5) LastError = where + ": GL 오류 0x" + e.ToString("X");
            }
        }

        public void Dispose()
        {
            if (_ctx == null) return;
            try
            {
                _ctx.EnsureCurrent();
                FreeTargets();
                foreach (GpuMesh m in _trees) Free(m); foreach (GpuMesh m in _rocks) Free(m);
                if (_terrainMesh != null) Free(_terrainMesh); if (_grassMesh != null) Free(_grassMesh);
                Gl.DeleteVao(_moteVao); Gl.DeleteBuf(_moteBuf); Gl.DeleteVao(_emptyVao);
                Gl.DeleteFbo(_shadowFbo); Gl.DeleteTex(_shadowTex);
                Gl.DeleteTex(_texLeaf); Gl.DeleteTex(_texGrass); Gl.DeleteTex(_texGround); Gl.DeleteTex(_texDetail);
                Gl.DeleteVao(_heroVao); Gl.DeleteBuf(_heroVbo); Gl.DeleteBuf(_heroIbo);
                foreach (Prog p in new[] { _shadow, _mesh, _terrain, _grass, _sky, _mote, _post, _hero, _heroShadow }) if (p != null) Gl.DeleteProgram(p.Id);
            }
            catch (Exception) { }
            _ctx.Dispose(); _ctx = null;
        }

        private static void Free(GpuMesh m) { Gl.DeleteVao(m.Vao); Gl.DeleteBuf(m.Vbo); Gl.DeleteBuf(m.Ibo); Gl.DeleteBuf(m.Inst); }
    }
}
