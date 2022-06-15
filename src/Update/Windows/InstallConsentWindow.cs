using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Squirrel.SimpleSplat;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.Macros;
using static Vanara.PInvoke.SHCore;
using static Vanara.PInvoke.ShowWindowCommand;
using static Vanara.PInvoke.User32;
using static Vanara.PInvoke.User32.MonitorFlags;
using static Vanara.PInvoke.User32.SetWindowPosFlags;
using static Vanara.PInvoke.User32.SPI;
using static Vanara.PInvoke.User32.WindowClassStyles;
using static Vanara.PInvoke.User32.WindowMessage;
using static Vanara.PInvoke.User32.WindowStyles;
using static Vanara.PInvoke.User32.WindowStylesEx;

namespace Squirrel.Update.Windows
{
    internal unsafe class InstallConsentWindow : WindowBase
    {
        private const int Width = 375;
        private const int Height = 300;

        private SafeHWND _hwnd;
        private Exception _error;
        private Thread _thread;
        private uint _threadId;
        private double _uizoom = 1d;

        private readonly ManualResetEvent _signal;
        private readonly Icon _icon;

        private const int OPERATION_TIMEOUT = 5000;
        private const string WINDOW_CLASS_NAME = "SquirrelInstallConsentWindow";

        public override IntPtr Handle => _hwnd != null ? _hwnd.DangerousGetHandle() : IntPtr.Zero;

        public InstallConsentWindow(string appName, byte[] iconBytes, byte[] bitmapBytes)
            : base(appName)
        {
            _signal = new ManualResetEvent(false);

            try {
                // we only accept a byte array and convert to memorystream because
                // gdi needs to seek and get length which is not supported in DeflateStream
                if (iconBytes?.Length > 0) _icon = new Icon(new MemoryStream(iconBytes));
                //if (bitmapBytes?.Length > 0) _img = (Bitmap) Bitmap.FromStream(new MemoryStream(bitmapBytes));
            } catch (Exception ex) {
                this.Log().WarnException("Unable to load images", ex);
            }

            _thread = new Thread(ThreadProc);
            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_signal.WaitOne(OPERATION_TIMEOUT)) {
                if (_error != null) throw _error;
                else throw new Exception("Timeout waiting for install consent window to open");
            }
            if (_error != null) throw _error;
        }

        private void ThreadProc()
        {
            try {
                // this is also set in the manifest, but this won't hurt anything and can help if the manifest got replaced with something else.
                ThreadDpiScalingContext.SetCurrentThreadScalingMode(ThreadScalingMode.PerMonitorV2Aware);
                _threadId = GetCurrentThreadId();
                CreateWindow();
            } catch (Exception ex) {
                _error = ex;
                _signal.Set();
            }
        }

        private void CreateWindow()
        {
            var instance = GetModuleHandle(null);

            WNDCLASS wndClass = new WNDCLASS {
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = WndProc,
                hInstance = instance,
                //hbrBackground = COLOR_WINDOW
                hCursor = LoadCursor(HINSTANCE.NULL, IDC_APPSTARTING),
                lpszClassName = WINDOW_CLASS_NAME,
                hIcon = _icon != null ? new HICON(_icon.Handle) : LoadIcon(instance, IDI_APPLICATION),
            };

            if (RegisterClass(wndClass) == 0) {
                var clhr = GetLastError();
                if (clhr != 0x00000582) // already registered
                    throw clhr.GetException("Unable to register install consent window class");
            }

            int x, y, w, h;
            try {
                // try to find monitor where mouse is
                GetCursorPos(out var point);
                var hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                MONITORINFO mi = new MONITORINFO { cbSize = 40 /*sizeof(MONITORINFO)*/ };
                if (!GetMonitorInfo(hMonitor, ref mi)) throw new Win32Exception();
                GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_DEFAULT, out var dpiX, out var dpiY).ThrowIfFailed();

                _uizoom = dpiX / 96d; // ui ignores image dpi, just takes screen dpi

                // calculate scaling factor for image. If the image does not have embedded dpi information, we default to 96
                double dpiRatioX = dpiX / 96d;
                double dpiRatioY = dpiY / 96d;

                // CS: this is ideal for allowing people to embed high-res images, but I don't 
                // know that people use this, and it's likely to cause issues when some
                // programs inevitably fuck this up and developers don't know what's going on.
                // I should probably just allow a --highDpiSplash option or something.
                //var embeddedDpi = _img.PropertyIdList.Any(p => p == PropertyTagPixelPerUnitX || p == PropertyTagPixelPerUnitY);
                //if (embeddedDpi) {
                //    dpiRatioX = dpiX / _img.HorizontalResolution;
                //    dpiRatioY = dpiY / _img.VerticalResolution;
                //}

                // calculate ideal window position & size, adjusted for image DPI and screen DPI
                w = (int) Math.Round(Width * dpiRatioX);
                h = (int) Math.Round(Height * dpiRatioY);
                x = (mi.rcWork.Width - w) / 2;
                y = (mi.rcWork.Height - h) / 2;
            } catch (Exception ex) {
                this.Log().WarnException("Unable to calculate install consent dpi scaling", ex);
                RECT rcArea = default;
                SystemParametersInfo(SPI_GETWORKAREA, 0, new IntPtr(&rcArea), 0);
                w = Width;
                h = Height;
                x = (rcArea.Width - w) / 2;
                y = (rcArea.Height - h) / 2;
            }

            _hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW/* | WS_EX_TOPMOST*/,
                WINDOW_CLASS_NAME,
                AppName + " Setup",
                WS_CLIPCHILDREN | WS_POPUP,
                x, y, w, h,
                HWND.NULL,
                HMENU.NULL,
                instance,
                IntPtr.Zero);

            if (_hwnd.IsInvalid) {
                throw new Win32Exception();
            }

            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

            MSG msg;
            PeekMessage(out msg, _hwnd, 0, 0, 0); // invoke creating message queue

            _signal.Set(); // signal to calling thread that the window has been created

            bool bRet;
            while ((bRet = GetMessage(out msg, HWND.NULL, 0, 0)) != false) {
                if (msg.message == (uint) WM_QUIT)
                    break;

                TranslateMessage(msg);
                DispatchMessage(msg);
            }

            DestroyWindow(_hwnd);
        }

        private nint WndProc(HWND hwnd, uint uMsg, nint wParam, nint lParam)
        {
            switch (uMsg) {

            case (uint) WM_PAINT:
                //GetWindowRect(hwnd, out var r);
                //using (var buffer = new Bitmap(r.Width, r.Height))
                //using (var brush = new SolidBrush(Color.FromArgb(190, Color.LimeGreen)))
                //using (var g = Graphics.FromImage(buffer))
                //using (var wnd = Graphics.FromHwnd(hwnd.DangerousGetHandle())) {
                //    // draw image to back buffer
                //    lock (_img) g.DrawImage(_img, 0, 0, r.Width, r.Height);
                    
                //    // only should do a single draw operation to the window front buffer to prevent flickering
                //    wnd.DrawImage(buffer, 0, 0, r.Width, r.Height);
                //}

                //ValidateRect(hwnd, null);
                return 0;

            case (uint) WM_DPICHANGED:
                // the window DPI has changed, either because the user has changed their display 
                // settings, or the window is being dragged to a new monitor
                _uizoom = LOWORD(wParam) / 96d;
                var suggestedRect = Marshal.PtrToStructure<RECT>(lParam);
                SetWindowPos(hwnd, HWND.HWND_TOP,
                    suggestedRect.X, suggestedRect.Y, suggestedRect.Width, suggestedRect.Height,
                    SWP_NOACTIVATE | SWP_NOZORDER);
                return 0;
            }

            return DefWindowProc(hwnd, uMsg, wParam, lParam);
        }

        public override void Show()
        {
            if (_thread == null) return;
            ShowWindow(_hwnd, SW_SHOW);
        }

        public override void Hide()
        {
            if (_thread == null) return;
            ShowWindow(_hwnd, SW_HIDE);
        }

        public override void SetProgressIndeterminate()
        {
        }

        public override void SetProgress(ulong completed, ulong total)
        {
        }

        public override void SetMessage(string message)
        {
        }

        public override void Dispose()
        {
            if (_thread == null) return;
            PostThreadMessage(_threadId, (uint) WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(OPERATION_TIMEOUT);
            _thread = null;
            _error = null;
            _threadId = 0;
            _hwnd = null;
            _signal.Reset();
        }
    }
}
