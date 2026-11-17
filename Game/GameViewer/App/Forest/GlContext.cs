//***************************************************************************
// GlContext.cs : 화면에 보이지 않는 창에 OpenGL 컨텍스트(WGL)를 만든다.
//   장면은 전부 FBO 에 그리고 glReadPixels 로 가져가 GDI+ 로 합성하므로 창 자체는 쓰이지 않는다.
//   3.3 core 컨텍스트를 우선 시도하고, 안 되면 기본 컨텍스트가 3.3 이상인지 확인한다.
//***************************************************************************
using System;
using System.Runtime.InteropServices;

namespace GameViewer.App.Forest
{
    internal sealed class GlContext : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize, nVersion; public uint dwFlags; public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift;
            public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi, EntryPoint = "CreateWindowExA")] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr dc, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr dc, int format, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr dc);
        [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr rc);
        [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr dc, IntPtr rc);
        [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentContext();
        [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string name);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateContextAttribsD(IntPtr dc, IntPtr share, int[] attribs);

        private IntPtr _hwnd, _dc, _rc;

        /// <summary>컨텍스트를 만들고 현재 스레드에 연결한다. 실패하면 예외(메시지에 원인).</summary>
        public void Create()
        {
            _hwnd = CreateWindowEx(0, "STATIC", "ForestGL", 0, 0, 0, 16, 16, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("GL 용 창을 만들지 못했습니다.");
            _dc = GetDC(_hwnd);
            if (_dc == IntPtr.Zero) throw new InvalidOperationException("GL 용 DC 를 얻지 못했습니다.");
            PIXELFORMATDESCRIPTOR pfd = new PIXELFORMATDESCRIPTOR();
            pfd.nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR)); pfd.nVersion = 1;
            pfd.dwFlags = 0x04 | 0x20 | 0x01;      // PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER
            pfd.iPixelType = 0; pfd.cColorBits = 32; pfd.cAlphaBits = 8; pfd.cDepthBits = 24; pfd.iLayerType = 0;
            int fmt = ChoosePixelFormat(_dc, ref pfd);
            if (fmt == 0 || !SetPixelFormat(_dc, fmt, ref pfd)) throw new InvalidOperationException("픽셀 포맷을 고르지 못했습니다.");

            IntPtr legacy = wglCreateContext(_dc);
            if (legacy == IntPtr.Zero) throw new InvalidOperationException("OpenGL 컨텍스트를 만들지 못했습니다(그래픽 드라이버 확인).");
            if (!wglMakeCurrent(_dc, legacy)) { wglDeleteContext(legacy); throw new InvalidOperationException("OpenGL 컨텍스트를 연결하지 못했습니다."); }

            _rc = legacy;
            IntPtr p = wglGetProcAddress("wglCreateContextAttribsARB");
            if (p != IntPtr.Zero && p.ToInt64() > 3)
            {
                try
                {
                    CreateContextAttribsD d = (CreateContextAttribsD)Marshal.GetDelegateForFunctionPointer(p, typeof(CreateContextAttribsD));
                    // WGL_CONTEXT_MAJOR/MINOR_VERSION_ARB = 3.3, WGL_CONTEXT_PROFILE_MASK_ARB = CORE
                    int[] attr = { 0x2091, 3, 0x2092, 3, 0x9126, 0x00000001, 0 };
                    IntPtr core = d(_dc, IntPtr.Zero, attr);
                    if (core != IntPtr.Zero && wglMakeCurrent(_dc, core)) { wglDeleteContext(legacy); _rc = core; }
                }
                catch (Exception) { /* 기본 컨텍스트로 계속 */ }
            }
            if (wglGetCurrentContext() != _rc) wglMakeCurrent(_dc, _rc);

            Gl.Load();
            string ver = Gl.GetString(Gl.VERSION);
            int major = 0, minor = 0;
            string[] parts = ver.Split(new[] { '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) { int.TryParse(parts[0], out major); int.TryParse(parts[1], out minor); }
            if (major < 3 || (major == 3 && minor < 3)) throw new InvalidOperationException("OpenGL 3.3 이상이 필요합니다(현재 " + ver + ").");
        }

        public string Description { get { return Gl.GetString(Gl.RENDERER) + " / OpenGL " + Gl.GetString(Gl.VERSION); } }

        /// <summary>이 스레드에 컨텍스트가 연결돼 있는지 확인하고 아니면 다시 연결한다.</summary>
        public void EnsureCurrent()
        {
            if (_rc != IntPtr.Zero && wglGetCurrentContext() != _rc) wglMakeCurrent(_dc, _rc);
        }

        public void Dispose()
        {
            if (_rc != IntPtr.Zero) { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(_rc); _rc = IntPtr.Zero; }
            if (_dc != IntPtr.Zero) { ReleaseDC(_hwnd, _dc); _dc = IntPtr.Zero; }
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }
    }
}
