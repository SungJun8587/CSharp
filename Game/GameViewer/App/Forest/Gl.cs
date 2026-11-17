//***************************************************************************
// Gl.cs : 필요한 OpenGL 함수만 직접 불러 쓰는 얇은 바인딩(외부 라이브러리 없음).
//   - GL 1.1 함수는 opengl32.dll 에서 바로, 그 이후 함수는 wglGetProcAddress 로 가져온다
//     (컨텍스트가 현재 상태여야 하므로 GlContext 가 만든 뒤 Load() 를 부른다).
//   - GLboolean 인자는 byte 로 받는다(bool 로 선언하면 4바이트로 넘어가 잘못 읽힌다).
//***************************************************************************
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GameViewer.App.Forest
{
    internal static class Gl
    {
        // ---- 상수
        public const uint TEXTURE_2D = 0x0DE1, TRIANGLES = 0x0004, POINTS = 0x0000, UNSIGNED_INT = 0x1405, UNSIGNED_BYTE = 0x1401, FLOAT = 0x1406;
        public const uint DEPTH_TEST = 0x0B71, CULL_FACE = 0x0B44, BLEND = 0x0BE2, LESS = 0x0201, LEQUAL = 0x0203;
        public const uint SRC_ALPHA = 0x0302, ONE = 1, ONE_MINUS_SRC_ALPHA = 0x0303;
        public const uint COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x0100;
        public const uint RGBA = 0x1908, BGRA = 0x80E1, RGBA8 = 0x8058, DEPTH_COMPONENT = 0x1902, DEPTH_COMPONENT24 = 0x81A6;
        public const uint TEXTURE_MIN_FILTER = 0x2801, TEXTURE_MAG_FILTER = 0x2800, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
        public const int NEAREST = 0x2600, LINEAR = 0x2601, LINEAR_MIPMAP_LINEAR = 0x2703, CLAMP_TO_EDGE = 0x812F, REPEAT = 0x2901;
        public const uint TEXTURE_MAX_ANISOTROPY = 0x84FE, MAX_TEXTURE_MAX_ANISOTROPY = 0x84FF;
        public const uint TEXTURE0 = 0x84C0;
        public const uint ARRAY_BUFFER = 0x8892, ELEMENT_ARRAY_BUFFER = 0x8893, STATIC_DRAW = 0x88E4;
        public const uint VERTEX_SHADER = 0x8B31, FRAGMENT_SHADER = 0x8B30, COMPILE_STATUS = 0x8B81, LINK_STATUS = 0x8B82, INFO_LOG_LENGTH = 0x8B84;
        public const uint FRAMEBUFFER = 0x8D40, READ_FRAMEBUFFER = 0x8CA8, DRAW_FRAMEBUFFER = 0x8CA9, RENDERBUFFER = 0x8D41;
        public const uint COLOR_ATTACHMENT0 = 0x8CE0, DEPTH_ATTACHMENT = 0x8D00, FRAMEBUFFER_COMPLETE = 0x8CD5;
        public const uint PACK_ALIGNMENT = 0x0D05, NONE = 0;
        public const uint FRONT = 0x0404, BACK = 0x0405, CCW = 0x0901, CW = 0x0900;
        public const uint MAX_SAMPLES = 0x8D57, SAMPLE_ALPHA_TO_COVERAGE = 0x809E, PROGRAM_POINT_SIZE = 0x8642;
        public const uint VERSION = 0x1F02, RENDERER = 0x1F01, MAJOR_VERSION = 0x821B, MINOR_VERSION = 0x821C;

        // ---- GL 1.1 (opengl32.dll)
        private const string Lib = "opengl32.dll";
        [DllImport(Lib, EntryPoint = "glClear")] public static extern void Clear(uint mask);
        [DllImport(Lib, EntryPoint = "glClearColor")] public static extern void ClearColor(float r, float g, float b, float a);
        [DllImport(Lib, EntryPoint = "glViewport")] public static extern void Viewport(int x, int y, int w, int h);
        [DllImport(Lib, EntryPoint = "glEnable")] public static extern void Enable(uint cap);
        [DllImport(Lib, EntryPoint = "glDisable")] public static extern void Disable(uint cap);
        [DllImport(Lib, EntryPoint = "glDepthFunc")] public static extern void DepthFunc(uint f);
        [DllImport(Lib, EntryPoint = "glDepthMask")] public static extern void DepthMask(byte on);
        [DllImport(Lib, EntryPoint = "glBlendFunc")] public static extern void BlendFunc(uint s, uint d);
        [DllImport(Lib, EntryPoint = "glGenTextures")] public static extern void GenTextures(int n, out uint tex);
        [DllImport(Lib, EntryPoint = "glDeleteTextures")] public static extern void DeleteTextures(int n, ref uint tex);
        [DllImport(Lib, EntryPoint = "glBindTexture")] public static extern void BindTexture(uint target, uint tex);
        [DllImport(Lib, EntryPoint = "glTexImage2D")] public static extern void TexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, IntPtr data);
        [DllImport(Lib, EntryPoint = "glTexParameteri")] public static extern void TexParameteri(uint target, uint pname, int v);
        [DllImport(Lib, EntryPoint = "glTexParameterf")] public static extern void TexParameterf(uint target, uint pname, float v);
        [DllImport(Lib, EntryPoint = "glReadPixels")] public static extern void ReadPixels(int x, int y, int w, int h, uint format, uint type, IntPtr data);
        [DllImport(Lib, EntryPoint = "glPixelStorei")] public static extern void PixelStorei(uint pname, int v);
        [DllImport(Lib, EntryPoint = "glDrawArrays")] public static extern void DrawArrays(uint mode, int first, int count);
        [DllImport(Lib, EntryPoint = "glDrawElements")] public static extern void DrawElements(uint mode, int count, uint type, IntPtr indices);
        [DllImport(Lib, EntryPoint = "glGetError")] public static extern uint GetError();
        [DllImport(Lib, EntryPoint = "glGetIntegerv")] public static extern void GetIntegerv(uint pname, out int v);
        [DllImport(Lib, EntryPoint = "glGetFloatv")] public static extern void GetFloatv(uint pname, out float v);
        [DllImport(Lib, EntryPoint = "glGetString")] private static extern IntPtr GetStringRaw(uint name);
        [DllImport(Lib, EntryPoint = "glReadBuffer")] public static extern void ReadBuffer(uint mode);
        [DllImport(Lib, EntryPoint = "glFinish")] public static extern void Finish();
        [DllImport(Lib, EntryPoint = "glCullFace")] public static extern void CullFace(uint mode);
        [DllImport(Lib, EntryPoint = "glFrontFace")] public static extern void FrontFace(uint mode);
        public static string GetString(uint name) { IntPtr p = GetStringRaw(name); return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p); }

        // ---- GL 2.0 이상 (wglGetProcAddress)
        [DllImport(Lib, EntryPoint = "wglGetProcAddress")] private static extern IntPtr wglGetProcAddress(string name);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate uint CreateShaderD(uint type);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void ShaderSourceD(uint shader, int count, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] src, int[] length);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void UintD(uint a);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void UintUintD(uint a, uint b);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void GetivD(uint obj, uint pname, out int v);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void GetLogD(uint obj, int max, out int len, StringBuilder log);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate uint CreateProgramD();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate int GetLocD(uint prog, [MarshalAs(UnmanagedType.LPStr)] string name);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void Uniform1iD(int loc, int v);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void Uniform1fD(int loc, float v);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void Uniform2fD(int loc, float a, float b);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void Uniform3fD(int loc, float a, float b, float c);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void UniformMat4D(int loc, int count, byte transpose, [In] float[] v);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void GenD(int n, out uint id);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void DelD(int n, ref uint id);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void BindD(uint target, uint id);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void BufferDataD(uint target, IntPtr size, IntPtr data, uint usage);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void EnableAttribD(uint index);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void AttribPointerD(uint index, int size, uint type, byte normalized, int stride, IntPtr offset);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void AttribDivisorD(uint index, uint divisor);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void DrawElementsInstancedD(uint mode, int count, uint type, IntPtr indices, int instances);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void ActiveTextureD(uint unit);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void GenerateMipmapD(uint target);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void FramebufferTexture2DD(uint target, uint attachment, uint textarget, uint tex, int level);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void FramebufferRenderbufferD(uint target, uint attachment, uint rbtarget, uint rb);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void RenderbufferStorageMsD(uint target, int samples, uint format, int w, int h);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate uint CheckFramebufferStatusD(uint target);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void BlitFramebufferD(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, uint mask, uint filter);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void DrawBuffersD(int n, [In] uint[] bufs);

        public static CreateShaderD CreateShader; public static ShaderSourceD ShaderSource; public static UintD CompileShader, DeleteShader, DeleteProgram, LinkProgram, UseProgram, BindVertexArray;
        public static GetivD GetShaderiv, GetProgramiv; public static GetLogD GetShaderInfoLog, GetProgramInfoLog; public static CreateProgramD CreateProgram; public static UintUintD AttachShader;
        public static GetLocD GetUniformLocation; public static Uniform1iD Uniform1i; public static Uniform1fD Uniform1f; public static Uniform2fD Uniform2f; public static Uniform3fD Uniform3f; public static UniformMat4D UniformMatrix4fv;
        public static GenD GenVertexArrays, GenBuffers, GenFramebuffers, GenRenderbuffers; public static DelD DeleteVertexArrays, DeleteBuffers, DeleteFramebuffers, DeleteRenderbuffers;
        public static BindD BindBuffer, BindFramebuffer, BindRenderbuffer; public static BufferDataD BufferData; public static EnableAttribD EnableVertexAttribArray; public static AttribPointerD VertexAttribPointer; public static AttribDivisorD VertexAttribDivisor;
        public static DrawElementsInstancedD DrawElementsInstanced; public static ActiveTextureD ActiveTexture; public static GenerateMipmapD GenerateMipmap;
        public static FramebufferTexture2DD FramebufferTexture2D; public static FramebufferRenderbufferD FramebufferRenderbuffer; public static RenderbufferStorageMsD RenderbufferStorageMultisample;
        public static CheckFramebufferStatusD CheckFramebufferStatus; public static BlitFramebufferD BlitFramebuffer; public static DrawBuffersD DrawBuffers;

        private static T Get<T>(string name) where T : class
        {
            IntPtr p = wglGetProcAddress(name);
            long v = p.ToInt64();
            if (v == 0 || v == 1 || v == 2 || v == 3 || v == -1) throw new InvalidOperationException("OpenGL 함수를 찾지 못했습니다: " + name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        /// <summary>컨텍스트가 현재 상태일 때 한 번 호출. 필요한 함수가 하나라도 없으면 예외(=그래픽 드라이버가 GL 3.3 미만).</summary>
        public static void Load()
        {
            CreateShader = Get<CreateShaderD>("glCreateShader"); ShaderSource = Get<ShaderSourceD>("glShaderSource"); CompileShader = Get<UintD>("glCompileShader");
            DeleteShader = Get<UintD>("glDeleteShader"); GetShaderiv = Get<GetivD>("glGetShaderiv"); GetShaderInfoLog = Get<GetLogD>("glGetShaderInfoLog");
            CreateProgram = Get<CreateProgramD>("glCreateProgram"); AttachShader = Get<UintUintD>("glAttachShader"); LinkProgram = Get<UintD>("glLinkProgram");
            GetProgramiv = Get<GetivD>("glGetProgramiv"); GetProgramInfoLog = Get<GetLogD>("glGetProgramInfoLog"); UseProgram = Get<UintD>("glUseProgram"); DeleteProgram = Get<UintD>("glDeleteProgram");
            GetUniformLocation = Get<GetLocD>("glGetUniformLocation"); Uniform1i = Get<Uniform1iD>("glUniform1i"); Uniform1f = Get<Uniform1fD>("glUniform1f");
            Uniform2f = Get<Uniform2fD>("glUniform2f"); Uniform3f = Get<Uniform3fD>("glUniform3f"); UniformMatrix4fv = Get<UniformMat4D>("glUniformMatrix4fv");
            GenVertexArrays = Get<GenD>("glGenVertexArrays"); BindVertexArray = Get<UintD>("glBindVertexArray"); DeleteVertexArrays = Get<DelD>("glDeleteVertexArrays");
            GenBuffers = Get<GenD>("glGenBuffers"); BindBuffer = Get<BindD>("glBindBuffer"); BufferData = Get<BufferDataD>("glBufferData"); DeleteBuffers = Get<DelD>("glDeleteBuffers");
            EnableVertexAttribArray = Get<EnableAttribD>("glEnableVertexAttribArray"); VertexAttribPointer = Get<AttribPointerD>("glVertexAttribPointer"); VertexAttribDivisor = Get<AttribDivisorD>("glVertexAttribDivisor");
            DrawElementsInstanced = Get<DrawElementsInstancedD>("glDrawElementsInstanced"); ActiveTexture = Get<ActiveTextureD>("glActiveTexture"); GenerateMipmap = Get<GenerateMipmapD>("glGenerateMipmap");
            GenFramebuffers = Get<GenD>("glGenFramebuffers"); BindFramebuffer = Get<BindD>("glBindFramebuffer"); DeleteFramebuffers = Get<DelD>("glDeleteFramebuffers");
            GenRenderbuffers = Get<GenD>("glGenRenderbuffers"); BindRenderbuffer = Get<BindD>("glBindRenderbuffer"); DeleteRenderbuffers = Get<DelD>("glDeleteRenderbuffers");
            FramebufferTexture2D = Get<FramebufferTexture2DD>("glFramebufferTexture2D"); FramebufferRenderbuffer = Get<FramebufferRenderbufferD>("glFramebufferRenderbuffer");
            RenderbufferStorageMultisample = Get<RenderbufferStorageMsD>("glRenderbufferStorageMultisample"); CheckFramebufferStatus = Get<CheckFramebufferStatusD>("glCheckFramebufferStatus");
            BlitFramebuffer = Get<BlitFramebufferD>("glBlitFramebuffer"); DrawBuffers = Get<DrawBuffersD>("glDrawBuffers");
        }

        // ---- 편의 함수
        public static uint GenBuffer() { uint id; GenBuffers(1, out id); return id; }
        public static uint GenVao() { uint id; GenVertexArrays(1, out id); return id; }
        public static uint GenFbo() { uint id; GenFramebuffers(1, out id); return id; }
        public static uint GenRbo() { uint id; GenRenderbuffers(1, out id); return id; }
        public static uint GenTexture() { uint id; GenTextures(1, out id); return id; }
        public static void DeleteTex(uint id) { if (id != 0) DeleteTextures(1, ref id); }
        public static void DeleteFbo(uint id) { if (id != 0) DeleteFramebuffers(1, ref id); }
        public static void DeleteRbo(uint id) { if (id != 0) DeleteRenderbuffers(1, ref id); }
        public static void DeleteBuf(uint id) { if (id != 0) DeleteBuffers(1, ref id); }
        public static void DeleteVao(uint id) { if (id != 0) DeleteVertexArrays(1, ref id); }

        public static void UploadBuffer(uint target, uint buf, Array data, int bytes)
        {
            BindBuffer(target, buf);
            GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { BufferData(target, (IntPtr)bytes, h.AddrOfPinnedObject(), STATIC_DRAW); } finally { h.Free(); }
        }

        public static void Mat(int loc, float[] m) { UniformMatrix4fv(loc, 1, 0, m); }
    }
}
