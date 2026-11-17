//***************************************************************************
// ForestBackdrop.cs : 3D 숲 배경을 GDI+ 화면에 깔아 주는 창구.
//   - 첫 호출 때 백그라운드 스레드에서 숲 재료를 만들고(1~2초), 끝나면 UI 스레드에서 GL 로 올린다.
//     그동안은 Pending 을 돌려줘서 호출하는 쪽이 어두운 배경으로 버틴다.
//   - GL 을 쓸 수 없으면(드라이버 3.3 미만, 원격 데스크톱 등) 원인을 ForestGL.log 에 남기고 Unavailable 을 돌려준다. 그러면 예전 2D 화면 그대로다.
//   - 그리기는 최대 약 30fps 로 제한하고, 그 사이에는 마지막 프레임을 다시 찍는다.
//   GL 은 UI 스레드에서만 쓴다(호출하는 쪽이 UI 스레드의 OnPaint 이다).
//***************************************************************************
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GameViewer.App.Forest
{
    public enum ForestResult { Unavailable, Pending, Drawn }

    public static class ForestBackdrop
    {
        private enum Stage { Idle, Building, Ready, Failed }

        private const int MaxRenderWidth = 1280, MinRenderWidth = 96;
        private const long FrameIntervalMs = 33;

        private static Stage _stage = Stage.Idle;
        private static ForestAssets _assets;
        private static volatile bool _built;
        private static Exception _buildError;
        private static ForestRenderer _r;
        private static Bitmap _bmp;
        private static readonly Stopwatch _clock = new Stopwatch();
        private static long _lastRenderMs = -1000;
        private static int _reqKind = -1, _curKind = -1;        // 이번 Draw 에서 보여 줄 영웅(-1 없음) / 직전에 그린 영웅
        private static float _reqNdcX;
        private static long _heroSinceMs;

        /// <summary>false 로 두면 숲을 쓰지 않는다(--no-3d, 환경변수 GAMEVIEWER_NO3D).</summary>
        public static bool Enabled = Environment.GetEnvironmentVariable("GAMEVIEWER_NO3D") == null;

        /// <summary>상태 한 줄(로그/진단용). 예: "준비 중", "NVIDIA ... / OpenGL 4.6 (4x MSAA)", "사용 안 함: ..."</summary>
        public static string Status { get; private set; }

        public static bool IsReady { get { return _stage == Stage.Ready; } }

        /// <summary>재료 만들기를 미리 시작한다(안 불러도 Draw 가 처음 불릴 때 시작한다).</summary>
        public static void Prepare()
        {
            if (!Enabled || _stage != Stage.Idle) return;
            _stage = Stage.Building; Status = "숲 준비 중";
            Thread th = new Thread(delegate ()
            {
                try { _assets = ForestAssets.Build(ForestAssets.DefaultSeed); }
                catch (Exception e) { _buildError = e; }
                _built = true;
            });
            th.IsBackground = true; th.Name = "ForestBuild"; th.Priority = ThreadPriority.BelowNormal;
            th.Start();
        }

        /// <summary>
        /// 다음 Draw 한 번 동안 숲 길 위에 영웅을 세운다(영웅 선택 화면). kind: 0 전사, 1 궁수, 2 마법사, 3 창기사. 부르지 않은 Draw 는 영웅 없이 걷는 숲이다.
        /// ndcX: 영웅이 화면 가로 어디에 보일지(-1 왼쪽 .. 1 오른쪽).
        /// </summary>
        public static void ShowHero(int kind, float ndcX) { _reqKind = kind; _reqNdcX = ndcX; }

        /// <summary>(0,0)-(width,height) 전체에 숲 화면을 그린다.</summary>
        public static ForestResult Draw(Graphics g, int width, int height)
        {
            if (!Enabled || width < 8 || height < 8) return ForestResult.Unavailable;
            if (_stage == Stage.Failed) return ForestResult.Unavailable;
            if (_stage == Stage.Idle) Prepare();
            if (_stage == Stage.Building)
            {
                if (!_built) return ForestResult.Pending;
                if (_buildError != null) { Fail("재료 생성 실패", _buildError); return ForestResult.Unavailable; }
                try
                {
                    ForestRenderer r = new ForestRenderer();
                    try { r.Init(_assets); }
                    catch (Exception) { r.Dispose(); throw; }
                    _r = r; _clock.Start(); _stage = Stage.Ready;
                    _assets = null;                         // GPU 로 올렸으니 CPU 쪽 복사본은 놓아 준다(카메라/세계는 렌더러가 쥐고 있다)
                }
                catch (Exception e) { Fail("GL 초기화 실패", e); return ForestResult.Unavailable; }
            }

            int kind = _reqKind; float ndcX = _reqNdcX; _reqKind = -1;
            try
            {
                long now = _clock.ElapsedMilliseconds;
                bool modeChanged = kind != _curKind;
                if (modeChanged) { _curKind = kind; _heroSinceMs = now; }
                int rw = Math.Min(width, MaxRenderWidth), rh = (int)Math.Round((double)rw * height / width);
                rw = Math.Max(MinRenderWidth, rw & ~1); rh = Math.Max(MinRenderWidth * height / width, rh & ~1);
                bool sizeChanged = _bmp == null || _bmp.Width != rw || _bmp.Height != rh;
                if (sizeChanged || modeChanged || now - _lastRenderMs >= FrameIntervalMs)
                {
                    _r.Render((float)(now / 1000.0), rw, rh, kind, (float)((now - _heroSinceMs) / 1000.0), ndcX);
                    if (_r.LastError != null && Status.IndexOf("GL 오류", StringComparison.Ordinal) < 0) { Status = _r.LastError; Log(_r.LastError); }
                    CopyToBitmap(_r.Pixels, rw, rh);
                    _lastRenderMs = now;
                    if (Status == null || Status.StartsWith("숲", StringComparison.Ordinal)) Status = _r.Info + " (" + (_r.Samples > 1 ? _r.Samples + "x MSAA" : "MSAA 없음") + ")";
                }
                InterpolationMode old = g.InterpolationMode;
                g.InterpolationMode = (rw == width) ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                g.DrawImage(_bmp, new Rectangle(0, 0, width, height), 0, 0, _bmp.Width, _bmp.Height, GraphicsUnit.Pixel);
                g.InterpolationMode = old;
                return ForestResult.Drawn;
            }
            catch (Exception e) { Fail("그리기 실패", e); return ForestResult.Unavailable; }
        }

        private static void CopyToBitmap(byte[] bgra, int w, int h)
        {
            if (_bmp == null || _bmp.Width != w || _bmp.Height != h)
            {
                if (_bmp != null) _bmp.Dispose();
                _bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            }
            BitmapData bd = _bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int row = w * 4;
                for (int y = 0; y < h; ++y)                 // GL 은 아래 줄부터 주므로 위아래를 뒤집어 복사한다
                    Marshal.Copy(bgra, (h - 1 - y) * row, IntPtr.Add(bd.Scan0, y * bd.Stride), row);
            }
            finally { _bmp.UnlockBits(bd); }
        }

        private static void Fail(string what, Exception e)
        {
            _stage = Stage.Failed;
            Status = "사용 안 함: " + what + " - " + e.Message;
            Log(what + ": " + e);
            Release();
        }

        private static void Log(string text)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ForestGL.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + "\r\n"); }
            catch (Exception) { /* 로그를 못 남겨도 화면은 예전 방식으로 계속된다 */ }
        }

        private static void Release()
        {
            if (_r != null) { try { _r.Dispose(); } catch (Exception) { } _r = null; }
            if (_bmp != null) { _bmp.Dispose(); _bmp = null; }
            _assets = null;
        }

        /// <summary>프로그램 끝에서 부른다(GL 자원과 숨은 창 정리).</summary>
        public static void Shutdown()
        {
            Release();
            _clock.Stop();
            if (_stage != Stage.Failed) _stage = Stage.Idle;
        }
    }
}
