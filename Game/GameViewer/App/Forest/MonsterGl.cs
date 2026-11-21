//***************************************************************************
// MonsterGl.cs : 몬스터와 영웅을 OpenGL 로 실시간 3D 렌더한다(스킨 메시 + 뼈에 붙은 무기/장비 메시 glTF, 뼈대 애니메이션, GPU 스키닝).
//   흐름: 캐릭터 한 마리마다 정사각형 칸에 그려(4배 다중표본 FBO -> 해상 FBO -> glReadPixels) 비트맵으로 돌려준다.
//         호출하는 쪽(CreatureRenderer)이 그 비트맵을 기존 이미지 시트와 똑같이 y 순서대로 GDI+/Skia 화면에 찍으므로 영웅/이펙트/UI 와의 겹침 순서가 그대로다.
//   - 모델은 첫 요청 때 백그라운드 스레드에서 읽고(glTF 파싱), 끝나면 UI 스레드에서 GL 로 올린다. 그동안과 GL 을 쓸 수 없을 때는 null 을 돌려줘서 기존 이미지 시트로 그려진다.
//   - 개체마다 애니메이션 상태/방향(부드럽게 돌아봄)/마지막 그림을 쥔다. 33ms 마다 다시 그리고 그 사이는 마지막 그림을 쓴다. 한 프레임에 그리는 수는 제한한다.
//   - 칸 크기는 화면에 찍히는 크기에 맞춰 정해져(16 단위, 64..512) 늘려 찍어 흐려지지 않는다.
//   GL 은 UI 스레드에서만 쓴다. 실패하면 원인을 ForestGL.log 에 남기고 이후는 계속 null(기존 그림)이다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using GameViewer.Core.Logic;
using GameViewer.Core.Model3D;
using GameViewer.Core.World;

namespace GameViewer.App.Forest
{
    internal static class MonsterGl
    {
        private const int MaxCell = 1024, MinCell = 64, FrameMs = 33, MaxPerFrame = 24, KeepMs = 4000;
        private const float TurnRate = 14f;                  // 돌아보는 속도(rad/s)

        private enum Stage { Idle, Ready, Failed }
        private enum MState { Loading, Parsed, Uploaded, Failed }

        private sealed class GpuModel
        {
            public volatile MState State = MState.Loading;
            public GltfModel M;
            public uint Vao, Vbo, Ibo; public uint[] Tex;         // 텍스처는 이미지마다(몸/무기 ...)
            public int Indices;
            public float Hm;                                // 서 있는 높이(모델 단위)
            public float[] Palette;
        }

        private sealed class Inst
        {
            public GpuModel Model; public MonsterAnimState Anim;
            public float Yaw; public bool YawSet; public long LastCall = long.MinValue, Stamp = long.MinValue, Touched;
            public Bitmap Bmp; public bool Hurt;
        }

        /// <summary>false 로 두면 몬스터 3D 를 쓰지 않는다(--no-3d, 환경변수 GAMEVIEWER_NO3D / GAMEVIEWER_NO3DMONSTER).</summary>
        public static bool Enabled = Environment.GetEnvironmentVariable("GAMEVIEWER_NO3DMONSTER") == null && Environment.GetEnvironmentVariable("GAMEVIEWER_NO3D") == null;

        public static string Status { get; private set; }
        public static bool IsReady { get { return _stage == Stage.Ready; } }
        /// <summary>마지막으로 그린 개체 수(진단용).</summary>
        public static int DrawnLastFrame { get; private set; }

        private static Stage _stage = Stage.Idle;
        private static GlContext _ctx;
        private static uint _prog, _msFbo, _msColor, _msDepth, _resFbo, _resTex, _resDepth;
        private static int _uBones, _uMvp, _uRot, _uTex, _uFlash, _uGain, _samples;
        /// <summary>영웅 밝기 배율(1 = 몬스터와 같음). 영웅 모델 텍스처가 어두워 조금 올려 둔다.</summary>
        public static float HeroGain = 1.5f;
        private static readonly Dictionary<string, GpuModel> Models = new Dictionary<string, GpuModel>();
        private static readonly Dictionary<ulong, Inst> Insts = new Dictionary<ulong, Inst>();
        private static readonly Dictionary<int, Bitmap> Spare = new Dictionary<int, Bitmap>();
        private static byte[] _px = new byte[MaxCell * MaxCell * 4];
        private static readonly float[] _mvp = new float[16], _rot = new float[16];
        private static long _frameNow = long.MinValue, _sweepAt; private static int _budget, _drawn;
        private static bool _errLogged;

        // ------------------------------------------------------------------ 공개

        /// <summary>
        /// 몬스터 e 의 지금 모습을 그린 비트맵(PArgb, 한 변 = CellWorld(heightWorld) * ppu 쯤, 칸 한 변은 월드 CellWorld 단위). 아직 못 그리면 null.
        /// 비트맵은 개체가 쥐고 있으므로 바로 화면에 찍고 놓는다.
        /// </summary>
        public static Bitmap Frame(Entity e, ModelSpec spec, float heightWorld, double ppu, long now, bool hurt)
        {
            if (!Enabled || _stage == Stage.Failed || spec == null || heightWorld <= 0f) return null;
            try
            {
                if (_stage == Stage.Idle) Init();
                if (now != _frameNow) { _frameNow = now; _budget = MaxPerFrame; DrawnLastFrame = _drawn; _drawn = 0; if (now - _sweepAt > 1000 || now < _sweepAt) { _sweepAt = now; Sweep(now); } }

                GpuModel gm = ModelFor(spec.Path);
                if (gm.State == MState.Parsed) Upload(gm);
                if (gm.State != MState.Uploaded) return null;
                if (!gm.M.Clips.ContainsKey("Idle") && AnimRoles.Find(gm.M, AnimRole.Idle) == null) return null;

                int cell = (int)Math.Ceiling(MonsterCamera.CellWorld(heightWorld) * ppu);
                cell = Math.Max(MinCell, Math.Min(MaxCell, (cell + 15) & ~15));

                Inst it;
                if (!Insts.TryGetValue(e.Id, out it) || it.Model != gm)
                {
                    it = new Inst(); it.Model = gm; it.Anim = new MonsterAnimState(gm.M); Insts[e.Id] = it;
                }
                it.Anim.WalkStride = spec.WalkStride;
                if (e.Dead && !it.Anim.Has(AnimRole.Death)) return null;       // 사망 클립이 없으면 기존 시체 그림(눕힌 그림)으로
                it.Touched = now;

                // 방향: 바라보는 쪽으로 부드럽게 돈다
                if (e.FaceX * e.FaceX + e.FaceY * e.FaceY > 1e-4f && !e.Dead)
                {
                    float tgt = (float)Math.Atan2(e.FaceX, e.FaceY);
                    if (!it.YawSet) { it.Yaw = tgt; it.YawSet = true; }
                    else
                    {
                        float dt = it.LastCall == long.MinValue ? 0f : Math.Min(0.25f, Math.Max(0f, (now - it.LastCall) / 1000f));
                        float d = tgt - it.Yaw;
                        while (d > Math.PI) d -= (float)(2 * Math.PI); while (d < -Math.PI) d += (float)(2 * Math.PI);
                        float step = TurnRate * dt;
                        it.Yaw += Math.Abs(d) <= step ? d : (d > 0 ? step : -step);
                    }
                }
                it.LastCall = now;

                bool settled = e.Dead && e.DeadAtMs > 0 && now - e.DeadAtMs > 1500 && it.Stamp > e.DeadAtMs + 1200;     // 쓰러지는 동작이 끝난 시체는 같은 그림
                bool fresh = it.Bmp != null && it.Bmp.Width == cell && now >= it.Stamp && (settled || now - it.Stamp < FrameMs) && hurt == it.Hurt;
                if (fresh) return it.Bmp;
                if (_budget <= 0 && it.Bmp != null && it.Bmp.Width == cell) return it.Bmp;      // 이번 프레임은 이미 많이 그렸다: 지난 그림을 쓴다
                --_budget; ++_drawn;

                AnimInput ai = new AnimInput();
                ai.NowMs = now; ai.Dead = e.Dead; ai.DeadAtMs = e.DeadAtMs;
                float walked; ai.Walking = e.WalkingAt(now, out walked); ai.Walked = walked;
                ai.AttackStartMs = e.AttackAnimMs; ai.AttackMs = e.IsMonster ? AttackPose.DurationMs(e) : (int)GameViewer.App.Render.HeroWeapons.TotalMs; ai.HitStartMs = e.HitFlashMs; ai.Seed = (int)(e.Id & 0xFFFF);
                ai.Casting = e.Casting; ai.CastStartMs = e.CastStartMs; ai.CastMs = (int)e.CastTimeMs;
                it.Anim.Update(ref ai, gm.Palette);

                if (it.Bmp == null || it.Bmp.Width != cell) { if (it.Bmp != null) Release(it.Bmp); it.Bmp = Take(cell); }
                RenderTo(gm, it.Yaw, hurt, e.IsMonster ? 1f : HeroGain, cell, it.Bmp);
                it.Stamp = now; it.Hurt = hurt;
                return it.Bmp;
            }
            catch (Exception ex) { Fail("그리기 실패", ex); return null; }
        }

        /// <summary>3D 를 쓸 수 있는 상태인지(켜져 있고 GL 초기화가 실패하지 않음). 모델을 읽는 중이어도 true.</summary>
        public static bool Usable { get { return Enabled && _stage != Stage.Failed; } }

        /// <summary>모델이 3D 로 그릴 수 있게 준비됐는지(파싱 끝, 사망 클립 있음). 준비 전이면 false.</summary>
        public static bool CanDraw(ModelSpec spec)
        {
            if (!Enabled || _stage == Stage.Failed || spec == null) return false;
            GpuModel gm;
            lock (Models) if (!Models.TryGetValue(spec.Path, out gm)) return false;
            return gm.State == MState.Uploaded;
        }

        /// <summary>프로그램 끝에서 부른다.</summary>
        public static void Shutdown()
        {
            ReleaseAll();
            if (_stage != Stage.Failed) _stage = Stage.Idle;
        }

        // ------------------------------------------------------------------ 모델 읽기/올리기

        private static GpuModel ModelFor(string path)
        {
            GpuModel gm;
            lock (Models)
            {
                if (Models.TryGetValue(path, out gm)) return gm;
                gm = new GpuModel(); Models[path] = gm;
            }
            GpuModel g = gm;
            ThreadPool.QueueUserWorkItem(delegate (object o)
            {
                try
                {
                    GltfModel m = GltfModel.Load(path);
                    g.M = m; g.Hm = Math.Max(0.2f, m.MaxY); g.Palette = new float[m.PaletteCount * 16];
                    if (m.PaletteCount > MonsterShaders.MaxBones) throw new InvalidDataException("관절이 너무 많습니다(" + m.PaletteCount + " > " + MonsterShaders.MaxBones + ")");
                    g.State = MState.Parsed;
                }
                catch (Exception ex) { Log("모델 읽기 실패 " + path + ": " + ex.Message); g.State = MState.Failed; }
            });
            return gm;
        }

        private static void Upload(GpuModel gm)
        {
            try
            {
                _ctx.EnsureCurrent();                       // 숲 배경 등 다른 컨텍스트가 현재일 수 있다
                GltfModel m = gm.M;
                float[] vb = m.BuildVertexBuffer();         // 위치3 법선3 uv2 관절4 가중치4
                gm.Vao = Gl.GenVao(); gm.Vbo = Gl.GenBuffer(); gm.Ibo = Gl.GenBuffer();
                Gl.BindVertexArray(gm.Vao);
                Gl.UploadBuffer(Gl.ARRAY_BUFFER, gm.Vbo, vb, vb.Length * 4);
                int st = GltfModel.VertexStride * 4;
                Gl.EnableVertexAttribArray(0); Gl.VertexAttribPointer(0, 3, Gl.FLOAT, 0, st, IntPtr.Zero);
                Gl.EnableVertexAttribArray(1); Gl.VertexAttribPointer(1, 3, Gl.FLOAT, 0, st, (IntPtr)12);
                Gl.EnableVertexAttribArray(2); Gl.VertexAttribPointer(2, 2, Gl.FLOAT, 0, st, (IntPtr)24);
                Gl.EnableVertexAttribArray(3); Gl.VertexAttribPointer(3, 4, Gl.FLOAT, 0, st, (IntPtr)32);
                Gl.EnableVertexAttribArray(4); Gl.VertexAttribPointer(4, 4, Gl.FLOAT, 0, st, (IntPtr)48);
                Gl.UploadBuffer(Gl.ELEMENT_ARRAY_BUFFER, gm.Ibo, m.Indices, m.Indices.Length * 4);
                Gl.BindVertexArray(0);
                gm.Indices = m.IndexCount;
                gm.Tex = new uint[Math.Max(1, m.Textures.Length)];
                for (int i = 0; i < gm.Tex.Length; ++i) gm.Tex[i] = MakeTexture(i < m.Textures.Length ? m.Textures[i] : null);
                gm.State = MState.Uploaded;
            }
            catch (Exception ex) { Log("모델 올리기 실패: " + ex); gm.State = MState.Failed; }
        }

        private static uint MakeTexture(byte[] bytes)
        {
            uint t = Gl.GenTexture();
            Gl.BindTexture(Gl.TEXTURE_2D, t);
            if (bytes == null)
            {
                byte[] white = { 255, 255, 255, 255 };
                GCHandle hw = GCHandle.Alloc(white, GCHandleType.Pinned);
                try { Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA8, 1, 1, 0, Gl.BGRA, Gl.UNSIGNED_BYTE, hw.AddrOfPinnedObject()); } finally { hw.Free(); }
            }
            else
            {
                using (Bitmap bmp = new Bitmap(new MemoryStream(bytes)))
                {
                    BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        // 위 줄부터(glTF 의 uv 원점이 왼쪽 위라 뒤집지 않고 그대로 올린다). Stride 가 폭*4 와 다를 수 있어 줄 단위로 모은다.
                        byte[] buf = new byte[bmp.Width * bmp.Height * 4];
                        for (int y = 0; y < bmp.Height; ++y) Marshal.Copy(IntPtr.Add(bd.Scan0, y * bd.Stride), buf, y * bmp.Width * 4, bmp.Width * 4);
                        GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
                        try { Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA8, bmp.Width, bmp.Height, 0, Gl.BGRA, Gl.UNSIGNED_BYTE, h.AddrOfPinnedObject()); } finally { h.Free(); }
                    }
                    finally { bmp.UnlockBits(bd); }
                }
            }
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MIN_FILTER, Gl.LINEAR_MIPMAP_LINEAR); Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MAG_FILTER, Gl.LINEAR);
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_S, Gl.CLAMP_TO_EDGE); Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_T, Gl.CLAMP_TO_EDGE);
            Gl.GenerateMipmap(Gl.TEXTURE_2D);
            return t;
        }

        // ------------------------------------------------------------------ 초기화/그리기

        private static void Init()
        {
            _ctx = new GlContext();
            _ctx.Create();
            _prog = Build(MonsterShaders.Vs, MonsterShaders.Fs);
            _uBones = Gl.GetUniformLocation(_prog, "uBones[0]"); _uMvp = Gl.GetUniformLocation(_prog, "uMvp"); _uRot = Gl.GetUniformLocation(_prog, "uRot");
            _uTex = Gl.GetUniformLocation(_prog, "uTex"); _uFlash = Gl.GetUniformLocation(_prog, "uFlash"); _uGain = Gl.GetUniformLocation(_prog, "uGain");

            _resTex = Gl.GenTexture(); Gl.BindTexture(Gl.TEXTURE_2D, _resTex);
            Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA8, MaxCell, MaxCell, 0, Gl.RGBA, Gl.UNSIGNED_BYTE, IntPtr.Zero);
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MIN_FILTER, Gl.NEAREST); Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MAG_FILTER, Gl.NEAREST);
            _resFbo = Gl.GenFbo(); Gl.BindFramebuffer(Gl.FRAMEBUFFER, _resFbo);
            Gl.FramebufferTexture2D(Gl.FRAMEBUFFER, Gl.COLOR_ATTACHMENT0, Gl.TEXTURE_2D, _resTex, 0);

            int maxS; Gl.GetIntegerv(Gl.MAX_SAMPLES, out maxS);
            int want = Math.Min(4, maxS);
            _samples = 0;
            if (want >= 2)
            {
                _msColor = Gl.GenRbo(); Gl.BindRenderbuffer(Gl.RENDERBUFFER, _msColor); Gl.RenderbufferStorageMultisample(Gl.RENDERBUFFER, want, Gl.RGBA8, MaxCell, MaxCell);
                _msDepth = Gl.GenRbo(); Gl.BindRenderbuffer(Gl.RENDERBUFFER, _msDepth); Gl.RenderbufferStorageMultisample(Gl.RENDERBUFFER, want, Gl.DEPTH_COMPONENT24, MaxCell, MaxCell);
                _msFbo = Gl.GenFbo(); Gl.BindFramebuffer(Gl.FRAMEBUFFER, _msFbo);
                Gl.FramebufferRenderbuffer(Gl.FRAMEBUFFER, Gl.COLOR_ATTACHMENT0, Gl.RENDERBUFFER, _msColor);
                Gl.FramebufferRenderbuffer(Gl.FRAMEBUFFER, Gl.DEPTH_ATTACHMENT, Gl.RENDERBUFFER, _msDepth);
                if (Gl.CheckFramebufferStatus(Gl.FRAMEBUFFER) == Gl.FRAMEBUFFER_COMPLETE) _samples = want;
                else { Gl.DeleteFbo(_msFbo); Gl.DeleteRbo(_msColor); Gl.DeleteRbo(_msDepth); _msFbo = _msColor = _msDepth = 0; }
            }
            if (_samples == 0)
            {
                // 다중표본을 못 쓰면 해상 FBO 에 바로 그린다(깊이 버퍼를 붙인다)
                _resDepth = Gl.GenRbo(); Gl.BindRenderbuffer(Gl.RENDERBUFFER, _resDepth);
                Gl.RenderbufferStorageMultisample(Gl.RENDERBUFFER, 0, Gl.DEPTH_COMPONENT24, MaxCell, MaxCell);
                Gl.BindFramebuffer(Gl.FRAMEBUFFER, _resFbo);
                Gl.FramebufferRenderbuffer(Gl.FRAMEBUFFER, Gl.DEPTH_ATTACHMENT, Gl.RENDERBUFFER, _resDepth);
            }
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, _resFbo);
            if (Gl.CheckFramebufferStatus(Gl.FRAMEBUFFER) != Gl.FRAMEBUFFER_COMPLETE) throw new InvalidOperationException("몬스터 해상 FBO 가 완성되지 않았습니다.");
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, 0);
            _stage = Stage.Ready;
            Status = _ctx.Description + " (" + (_samples > 1 ? _samples + "x MSAA" : "MSAA 없음") + ")";
            Log("몬스터 3D 사용: " + Status);
        }

        private static void RenderTo(GpuModel gm, float yaw, bool hurt, float gain, int cell, Bitmap dst)
        {
            _ctx.EnsureCurrent();
            uint drawFbo = _samples > 1 ? _msFbo : _resFbo;
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, drawFbo);
            Gl.Viewport(0, 0, cell, cell);
            Gl.ClearColor(0f, 0f, 0f, 0f);
            Gl.DepthMask(1);
            Gl.Enable(Gl.DEPTH_TEST); Gl.DepthFunc(Gl.LESS);
            Gl.Enable(Gl.CULL_FACE); Gl.CullFace(Gl.BACK); Gl.FrontFace(Gl.CCW);
            Gl.Disable(Gl.BLEND);
            Gl.Clear(Gl.COLOR_BUFFER_BIT | Gl.DEPTH_BUFFER_BIT);

            MonsterCamera.Build(yaw, gm.Hm, _mvp, _rot);
            Gl.UseProgram(_prog);
            Gl.UniformMatrix4fv(_uBones, gm.M.PaletteCount, 0, gm.Palette);
            Gl.Mat(_uMvp, _mvp); Gl.Mat(_uRot, _rot);
            Gl.Uniform1f(_uFlash, hurt ? 1f : 0f); Gl.Uniform1f(_uGain, gain);
            Gl.ActiveTexture(Gl.TEXTURE0); Gl.Uniform1i(_uTex, 0);
            Gl.BindVertexArray(gm.Vao);
            GltfModel.Part[] parts = gm.M.Parts;
            for (int i = 0; i < parts.Length; ++i)             // 프리미티브마다 텍스처가 다를 수 있다(몸/무기)
            {
                Gl.BindTexture(Gl.TEXTURE_2D, gm.Tex[Math.Min(parts[i].Texture, gm.Tex.Length - 1)]);
                Gl.DrawElements(Gl.TRIANGLES, parts[i].IndexCount, Gl.UNSIGNED_INT, (IntPtr)(parts[i].IndexStart * 4));
            }
            Gl.BindVertexArray(0);

            if (_samples > 1)
            {
                Gl.BindFramebuffer(Gl.READ_FRAMEBUFFER, _msFbo); Gl.BindFramebuffer(Gl.DRAW_FRAMEBUFFER, _resFbo);
                Gl.BlitFramebuffer(0, 0, cell, cell, 0, 0, cell, cell, Gl.COLOR_BUFFER_BIT, (uint)Gl.NEAREST);
            }
            Gl.BindFramebuffer(Gl.READ_FRAMEBUFFER, _resFbo);
            Gl.ReadBuffer(Gl.COLOR_ATTACHMENT0);
            Gl.PixelStorei(Gl.PACK_ALIGNMENT, 1);
            GCHandle h = GCHandle.Alloc(_px, GCHandleType.Pinned);
            try { Gl.ReadPixels(0, 0, cell, cell, Gl.BGRA, Gl.UNSIGNED_BYTE, h.AddrOfPinnedObject()); } finally { h.Free(); }
            Gl.BindFramebuffer(Gl.FRAMEBUFFER, 0);
            if (!_errLogged) { uint err = Gl.GetError(); if (err != 0) { _errLogged = true; Log("GL 오류 0x" + err.ToString("X") + " (몬스터 그리기)"); } }

            BitmapData bd = dst.LockBits(new Rectangle(0, 0, cell, cell), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int row = cell * 4;
                for (int y = 0; y < cell; ++y)                      // GL 은 아래 줄부터 주므로 뒤집어 복사
                    Marshal.Copy(_px, (cell - 1 - y) * row, IntPtr.Add(bd.Scan0, y * bd.Stride), row);
            }
            finally { dst.UnlockBits(bd); GameViewer.App.Render.GfxImages.Touch(dst); }
        }

        // ------------------------------------------------------------------ 보조

        private static Bitmap Take(int cell)
        {
            Bitmap b;
            if (Spare.TryGetValue(cell, out b)) { Spare.Remove(cell); return b; }
            return new Bitmap(cell, cell, PixelFormat.Format32bppPArgb);
        }

        private static void Release(Bitmap b)
        {
            Bitmap old;
            if (Spare.TryGetValue(b.Width, out old)) { old.Dispose(); }
            Spare[b.Width] = b;
        }

        private static void Sweep(long now)
        {
            List<ulong> gone = null;
            foreach (KeyValuePair<ulong, Inst> kv in Insts)
                if (now - kv.Value.Touched > KeepMs || now < kv.Value.Touched) { if (gone == null) gone = new List<ulong>(); gone.Add(kv.Key); }
            if (gone == null) return;
            foreach (ulong id in gone) { Inst it = Insts[id]; if (it.Bmp != null) Release(it.Bmp); Insts.Remove(id); }
        }

        private static uint Build(string vs, string fs)
        {
            uint v = Compile(Gl.VERTEX_SHADER, vs, "monster.vs"), f = Compile(Gl.FRAGMENT_SHADER, fs, "monster.fs");
            uint p = Gl.CreateProgram(); Gl.AttachShader(p, v); Gl.AttachShader(p, f); Gl.LinkProgram(p);
            int ok; Gl.GetProgramiv(p, Gl.LINK_STATUS, out ok);
            if (ok == 0)
            {
                int len; StringBuilder log = new StringBuilder(4096); Gl.GetProgramInfoLog(p, log.Capacity, out len, log);
                throw new InvalidOperationException("monster 프로그램 링크 실패: " + log);
            }
            Gl.DeleteShader(v); Gl.DeleteShader(f);
            return p;
        }

        private static uint Compile(uint type, string src, string name)
        {
            uint s = Gl.CreateShader(type);
            Gl.ShaderSource(s, 1, new[] { src }, null);
            Gl.CompileShader(s);
            int ok; Gl.GetShaderiv(s, Gl.COMPILE_STATUS, out ok);
            if (ok == 0)
            {
                int len; StringBuilder log = new StringBuilder(4096); Gl.GetShaderInfoLog(s, log.Capacity, out len, log);
                throw new InvalidOperationException(name + " 컴파일 실패: " + log);
            }
            return s;
        }

        private static void Fail(string what, Exception e)
        {
            _stage = Stage.Failed;
            Status = "사용 안 함: " + what + " - " + e.Message;
            Log(what + ": " + e);
            ReleaseAll();
        }

        private static void ReleaseAll()
        {
            foreach (Inst it in Insts.Values) if (it.Bmp != null) it.Bmp.Dispose();
            Insts.Clear();
            foreach (Bitmap b in Spare.Values) b.Dispose();
            Spare.Clear();
            try
            {
                if (_ctx != null)
                {
                    _ctx.EnsureCurrent();
                    lock (Models)
                        foreach (GpuModel gm in Models.Values) { Gl.DeleteVao(gm.Vao); Gl.DeleteBuf(gm.Vbo); Gl.DeleteBuf(gm.Ibo); if (gm.Tex != null) foreach (uint t in gm.Tex) Gl.DeleteTex(t); }
                    Gl.DeleteFbo(_msFbo); Gl.DeleteFbo(_resFbo); Gl.DeleteRbo(_msColor); Gl.DeleteRbo(_msDepth); Gl.DeleteRbo(_resDepth); Gl.DeleteTex(_resTex);
                    if (_prog != 0) Gl.DeleteProgram(_prog);
                }
            }
            catch (Exception) { /* 종료 중 오류는 무시 */ }
            lock (Models) Models.Clear();
            _msFbo = _resFbo = _msColor = _msDepth = _resDepth = _resTex = _prog = 0;
            if (_ctx != null) { try { _ctx.Dispose(); } catch (Exception) { } _ctx = null; }
        }

        private static void Log(string text)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ForestGL.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + "\r\n"); }
            catch (Exception) { /* 로그를 못 남겨도 기존 그림으로 계속된다 */ }
        }
    }
}
