using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Squirrel.SimpleSplat;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.Macros;
using static Vanara.PInvoke.Gdi32;
using static Vanara.PInvoke.User32;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32.HitTestValues;
using static Vanara.PInvoke.User32.MonitorFlags;
using static Vanara.PInvoke.User32.SetWindowPosFlags;
using static Vanara.PInvoke.User32.SPI;
using static Vanara.PInvoke.User32.WindowClassStyles;
using static Vanara.PInvoke.User32.WindowMessage;
using static Vanara.PInvoke.User32.WindowStyles;
using static Vanara.PInvoke.User32.WindowStylesEx;
using static Vanara.PInvoke.SHCore;
using static Vanara.PInvoke.ComCtl32;

namespace Squirrel.Update.Windows
{
    internal unsafe class InstallConsentWindow : WindowBase
    {
        public delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("Comctl32.dll", SetLastError = false)]
        public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("Comctl32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private const int PropertyTagFrameDelay = 0x5100;
        private const int PropertyTagPixelUnit = 0x5110;
        private const int PropertyTagPixelPerUnitX = 0x5111;
        private const int PropertyTagPixelPerUnitY = 0x5112;
        private const int leftPadding = 30;
        private const int WindowWidth = 485;
        private const int WindowHeight = 475;
        private const string WINDOW_CLASS_NAME = "SquirrelInstallConsentWindow";

        private readonly Bitmap _img;
        private readonly Icon _icon;
        private readonly ManualResetEvent _signal = new ManualResetEvent(initialState: false);
        private readonly Thread _thread;
        private readonly WindowProc _windowProcDelegate;
        private readonly string _eulaUrl;
        private readonly string _termsAndConditionsUrl;
        private readonly string _privacyPolicyUrl;


        private uint _threadId;
        private double _uizoom = 1d;

        private SafeHWND _hwnd;
        private SafeHWND _installButtonHwnd;
        private SafeHWND _cancelButtonHwnd;
        private SafeHWND _eulaLinkHwnd;
        private SafeHWND _privacyPolicyLinkHwnd;
        private SafeHWND _termsAndConditionsLinkHwnd;

        public bool Result { get; private set; }

        public InstallConsentWindow(string appName, byte[] iconBytes, byte[] logoBytes,
            string eulaUrl, string termsAndConditionsUrl, string privacyPolicyUrl)
            : base(appName)
        {
            _windowProcDelegate = new WindowProc(this.WndProc);

            _eulaUrl = eulaUrl;
            _termsAndConditionsUrl = termsAndConditionsUrl;
            _privacyPolicyUrl = privacyPolicyUrl;

            if (logoBytes is { Length: > 0 }) {
                using var stream = new MemoryStream(logoBytes);
                _img = new Bitmap(stream);
            }

            if (iconBytes is { Length: > 0 }) {
                using var stream = new MemoryStream(iconBytes);
                _icon = new Icon(stream);
            }

            _thread = new Thread(ThreadProc) {
                Name = "Window Thread"
            };
        }

        private void ThreadProc()
        {
            try
            {
                ThreadDpiScalingContext.SetCurrentThreadScalingMode(ThreadScalingMode.PerMonitorV2Aware);
                _threadId = GetCurrentThreadId();
                Create();

                PeekMessage(out var msg, _hwnd, 0, 0, 0); // invoke creating message queue

                while ((GetMessage(out msg, HWND.NULL, 0, 0)) != false)
                {
                    if (msg.message == (uint)WM_QUIT)
                        break;

                    TranslateMessage(msg);
                    DispatchMessage(msg);
                }

                DestroyWindow(_hwnd);
            }
            catch (Exception ex)
            {
                this.Log().WarnException(ex.Message, ex);
                _signal.Set();
            }
        }

        private void Create()
        {
            using var instance = GetModuleHandle(null);
            if (instance == null) {
                var lastError = GetLastError();
                lastError.ThrowIfFailed("Create Window Failed");
            }

            COLORREF rgbWhite = 0xFFFFFFFF;

            var wndClass = new WNDCLASS {
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = _windowProcDelegate,
                hInstance = instance,
                hbrBackground = CreateSolidBrush(rgbWhite),
                hCursor = LoadCursor(HINSTANCE.NULL, IDC_APPSTARTING),
                lpszClassName = WINDOW_CLASS_NAME,
                hIcon = _icon != null ? new HICON(_icon.Handle) : LoadIcon(instance, IDI_APPLICATION),
            };

            if (RegisterClass(wndClass) == 0) {
                var clhr = GetLastError();
                if (clhr != 0x00000582) {
                    // already registered
                    throw clhr.GetException("Unable to register install consent window class");
                }
            }

            int x, y, w, h;
            double dpiRatioX = 1, dpiRatioY = 1;
            try {
                // try to find monitor where mouse is
                GetCursorPos(out var point);

                var hMonitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                MONITORINFO mi = new MONITORINFO { cbSize = 40 /*sizeof(MONITORINFO)*/ };

                if (!GetMonitorInfo(hMonitor, ref mi)) {
                    throw new Win32Exception();
                }

                if (GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_DEFAULT, out var dpiX, out var dpiY) != HRESULT.S_OK) {
                    dpiX = dpiY = 96;
                }

                // calculate scaling factor. default to 96
                dpiRatioX = dpiX / 96d;
                dpiRatioY = dpiY / 96d;

                _uizoom = dpiX / 96d; // ui ignores image dpi, just takes screen dpi

                // calculate ideal window position & size, adjusted for image DPI and screen DPI
                //w = (int)Math.Round(_img.Width * dpiRatioX);
                //h = (int)Math.Round(_img.Height * dpiRatioY);

                w = (int) Math.Round(WindowWidth * dpiRatioX);
                h = (int) Math.Round(WindowHeight * dpiRatioY);
                x = (mi.rcWork.Width - w) / 2;
                y = (mi.rcWork.Height - h) / 2;

                //Log.Info($"Image dpi is {_img.HorizontalResolution} ({(embeddedDpi ? "embedded" : "default")}), screen dpi is {dpiX}. Rendering image at [{x},{y},{w},{h}]");
            } catch (Exception ex) {
                //Log.WarnException("Unable to calculate splash dpi scaling", ex);
                RECT rcArea = default;
                SystemParametersInfo(SPI_GETWORKAREA, 0, new IntPtr(&rcArea), 0);
                w = 485;//_img.Width;
                h = 475;//_img.Height;
                x = (rcArea.Width - w) / 2;
                y = (rcArea.Height - h) / 2;
            }

            _hwnd = CreateWindowEx(
                WS_EX_TOPMOST,
                WINDOW_CLASS_NAME,
                $"{AppName} Setup",
                WS_CLIPCHILDREN | WS_SYSMENU | WS_CAPTION | WS_BORDER | WS_ICONIC,
                x, y, w, h,
                HWND.NULL,
                HMENU.NULL,
                instance,
                IntPtr.Zero);

            if (_hwnd.IsInvalid) {
                throw new Win32Exception();
            }

            var buttonStyle = (WindowStyles) ((int) WS_CHILD | (int) WS_VISIBLE | (int) WS_TABSTOP | (int) ButtonStyle.BS_FLAT);

            _installButtonHwnd = CreateWindow("BUTTON",  // Predefined class; Unicode assumed 
                         "Install",      // Button text 
                         buttonStyle,
                         (int) Math.Round(245 * dpiRatioX),         // x position 
                         (int) Math.Round(380 * dpiRatioY),         // y position 
                         (int) Math.Round(100 * dpiRatioX),        // Button width
                         (int) Math.Round(40 * dpiRatioY),        // Button height
                         _hwnd,     // Parent window
                         HMENU.NULL,       // No menu.
                         instance,
                         IntPtr.Zero);      // Pointer not needed.

            _cancelButtonHwnd = CreateWindow("BUTTON",  // Predefined class; Unicode assumed 
                         "Cancel",      // Button text 
                         buttonStyle,
                         (int) Math.Round(355 * dpiRatioX),         // x position 
                         (int) Math.Round(380 * dpiRatioY),         // y position 
                         (int) Math.Round(100 * dpiRatioX),        // Button width
                         (int) Math.Round(40 * dpiRatioY),        // Button height
                         _hwnd,     // Parent window
                         HMENU.NULL,       // No menu.
                         instance,
                         IntPtr.Zero);      // Pointer not needed.

            var textStyle = (WindowStyles) ((int) WS_CHILD | (int) WS_VISIBLE | (int) (StaticStyle.SS_NOTIFY));

            var legalInfoText = CreateWindow("STATIC",
                            "By clicking on \"Install\" you are agreeing to our:",
                            WS_CHILD | WS_VISIBLE,
                            (int) Math.Round(leftPadding * dpiRatioX), (int) Math.Round(180 * dpiRatioY), (int) Math.Round(460 * dpiRatioX), (int) Math.Round(40 * dpiRatioY),
                            _hwnd,
                            HMENU.NULL,
                            instance,
                            IntPtr.Zero);

            _eulaLinkHwnd = CreateWindow("STATIC",
                                     "End User License Agreement",
                                     textStyle,
                                     (int) Math.Round(leftPadding * dpiRatioX), (int) Math.Round(230 * dpiRatioY), (int) Math.Round(400 * dpiRatioX), (int) Math.Round(30 * dpiRatioY),
                                     _hwnd,
                                     HMENU.NULL,
                                     instance,
                                     IntPtr.Zero);

            _termsAndConditionsLinkHwnd = CreateWindow("STATIC",
                                                   "Terms and Conditions",
                                                   textStyle,
                                                   (int) Math.Round(leftPadding * dpiRatioX), (int) Math.Round(270 * dpiRatioY), (int) Math.Round(400 * dpiRatioX), (int) Math.Round(30 * dpiRatioY),
                                                   _hwnd,
                                                   HMENU.NULL,
                                                   instance,
                                                   IntPtr.Zero);

            _privacyPolicyLinkHwnd = CreateWindow("STATIC",
                                             "Privacy Policy",
                                             textStyle,
                                             (int) Math.Round(leftPadding * dpiRatioX), (int) Math.Round(310 * dpiRatioY), (int) Math.Round(400 * dpiRatioX), (int) Math.Round(30 * dpiRatioY),
                                             _hwnd,
                                             HMENU.NULL,
                                             instance,
                                             IntPtr.Zero);

            var result = SetWindowSubclass(_eulaLinkHwnd.DangerousGetHandle(), HyperlinkProc, 0, IntPtr.Zero);
            result = SetWindowSubclass(_privacyPolicyLinkHwnd.DangerousGetHandle(), HyperlinkProc, 0, IntPtr.Zero);
            result = SetWindowSubclass(_termsAndConditionsLinkHwnd.DangerousGetHandle(), HyperlinkProc, 0, IntPtr.Zero);

            ShowWindow(_hwnd, ShowWindowCommand.SW_SHOWNOACTIVATE);

            var guiFont = GetStockObject(Gdi32.StockObjectType.DEFAULT_GUI_FONT);
            var linkFont = CreateFont(24, 0, 0, 0, FW_LIGHT, false, pszFaceName: "Segoe UI", bUnderline: true);

            SendMessage(_cancelButtonHwnd, (uint) WM_SETFONT, (IntPtr) guiFont, true);
            SendMessage(_installButtonHwnd, (uint) WM_SETFONT, (IntPtr) guiFont, true);

            SendMessage(legalInfoText,
                       (uint) WM_SETFONT,
                       CreateFont(cHeight: 24, cWeight: FW_LIGHT, pszFaceName: "Segoe UI").DangerousGetHandle());

            SendMessage(_eulaLinkHwnd, (uint) WM_SETFONT, linkFont.DangerousGetHandle(), true);
            SendMessage(_termsAndConditionsLinkHwnd, (uint) WM_SETFONT, linkFont.DangerousGetHandle(), true);
            SendMessage(_privacyPolicyLinkHwnd, (uint) WM_SETFONT, linkFont.DangerousGetHandle(), true);

            DeleteObject(guiFont);
        }

        private IntPtr HyperlinkProc(IntPtr hwnd, uint umsg, IntPtr wparam, IntPtr lparam, uint uidsubclass, IntPtr dwrefdata)
        {
            switch (umsg) {
                case (int) WM_SETCURSOR:
                    var cursor = LoadCursor(IntPtr.Zero, IDC_HAND);
                    SetCursor(cursor);
                    return (IntPtr) 1;
                default:
                    return DefSubclassProc(hwnd, umsg, wparam, lparam);
            }
        }

        private nint WndProc(HWND hwnd, uint uMsg, IntPtr wParam, IntPtr lParam)
        {
            switch (uMsg) {
            case (uint) WM_CTLCOLORSTATIC:
                var hdc = (HDC) wParam;
                //This is how to change static text foreground and background colors
                if (lParam == _eulaLinkHwnd.DangerousGetHandle() ||
                   lParam == _privacyPolicyLinkHwnd.DangerousGetHandle() ||
                   lParam == _termsAndConditionsLinkHwnd.DangerousGetHandle()) {
                    SetTextColor(hdc, new COLORREF(0, 0, 255));
                }

                SetBkColor(hdc, new COLORREF(255, 255, 255));
                var hbrush = CreateSolidBrush(new COLORREF(r: 255, g: 255, b: 255));
                return (IntPtr) hbrush.DangerousGetHandle();

            case (uint) WM_PAINT:
                GetWindowRect(hwnd, out var r);
                int w = _img.Width;
                int h = _img.Height;

                using (var buffer = new Bitmap(w, h))
                using (var g = Graphics.FromImage(buffer))
                using (var wnd = Graphics.FromHwnd(hwnd.DangerousGetHandle())) {
                    //draw image to back buffer
                    lock (_img) g.DrawImage(_img, 0, 0, w, h);

                    //only should do a single draw operation to the window front buffer to prevent flickering
                    wnd.DrawImage(buffer, leftPadding, 0, w, h);
                }

                ValidateRect(hwnd, null);
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

            case (uint) WM_NCHITTEST:
                // any clicks in the client area should register as a click on the title bar so that the 
                // user can drag the window, and it will be properly rescaled when dragged between monitors
                nint hit = User32.DefWindowProc(hwnd, uMsg, wParam, lParam);
                if (hit == (ushort) HTCLIENT)
                    return (ushort) HTCAPTION;
                return hit;

            case (uint) WM_COMMAND when lParam == _installButtonHwnd.DangerousGetHandle():
                this.CloseWindow(install: true);
                break;

            case (uint) WM_COMMAND when lParam == _cancelButtonHwnd.DangerousGetHandle():
                this.CloseWindow(install: false);
                break;

            case (uint) WM_COMMAND when lParam == _eulaLinkHwnd.DangerousGetHandle():
                OpenUrl(_eulaUrl);
                break;

            case (uint) WM_COMMAND when lParam == _privacyPolicyLinkHwnd.DangerousGetHandle():
                OpenUrl(_privacyPolicyUrl);
                break;

            case (uint) WM_COMMAND when lParam == _termsAndConditionsLinkHwnd.DangerousGetHandle():
                OpenUrl(_termsAndConditionsUrl);
                break;

            case (uint) WM_CLOSE:
                Result = false;
                break;

            case (uint) WM_DESTROY:
                PostQuitMessage(0);
                _signal.Set();
                return 0;
            }

            return User32.DefWindowProc(hwnd, uMsg, wParam, lParam);
        }

        private void OpenUrl(string url)
        {
            try 
            {
                ShellExecute(_hwnd, "open", url, null, null, ShowWindowCommand.SW_SHOWNORMAL);
                System.Diagnostics.Process.Start(url);
            } 
            catch { }
        }

        private void CloseWindow(bool install)
        {
            this.Result = install;
            PostMessage(_hwnd, (uint) WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        #region WindowBase Members
        public override IntPtr Handle { get; }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        public override void Show()
        {
            if (_thread != null) {
                _thread.Start();
                var isSignaled = _signal.WaitOne();
                if (!isSignaled) {
                    throw new Exception("NO SIGNAL");
                }
            }
        }

        public override void Hide()
        {
            throw new NotImplementedException();
        }

        public override void SetProgressIndeterminate()
        {
            throw new NotImplementedException();
        }

        public override void SetMessage(string message)
        {
            throw new NotImplementedException();
        }

        public override void SetProgress(ulong completed, ulong total)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}