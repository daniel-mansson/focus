using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Wraps a single Win32 layered overlay HWND.
/// Manages full lifecycle: RegisterClassEx, CreateWindowEx, Show, Hide, DestroyWindow, UnregisterClass.
///
/// OVERLAY-02: click-through, excluded from Alt+Tab/taskbar, non-focus-stealing, always-on-top.
/// Must be instantiated on the STA thread.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class OverlayWindow : IDisposable
{
    private readonly string _className;
    private readonly HINSTANCE _hInstance;
    private readonly global::Windows.Win32.UI.WindowsAndMessaging.WNDPROC _wndProc;
    private HWND _hwnd;
    private bool _disposed;

    // HWND_TOPMOST sentinel — SetWindowPos Z-order constant for always-on-top.
    private static readonly HWND HwndTopmost = new HWND(new IntPtr(-1));

    public HWND Hwnd => _hwnd;

    public OverlayWindow()
    {
        // Must use Module HINSTANCE — new HINSTANCE(0) causes error 87.
        _hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(OverlayWindow).Module));

        // Unique class name per instance to avoid conflicts.
        _className = "FocusOverlay_" + Guid.NewGuid().ToString("N")[..8];

        // Store delegate in field — GC collects locals after method returns.
        _wndProc = WndProc;

        RegisterAndCreate();
    }

    private unsafe void RegisterAndCreate()
    {
        fixed (char* pClassName = _className)
        {
            var wcex = new WNDCLASSEXW();
            wcex.cbSize      = (uint)Marshal.SizeOf<WNDCLASSEXW>();
            wcex.lpfnWndProc = _wndProc;
            wcex.hInstance   = _hInstance;
            wcex.lpszClassName = pClassName;

            var atom = PInvoke.RegisterClassEx(wcex);
            if (atom == 0)
            {
                var err = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException($"RegisterClassEx failed: error {err}");
            }

            // WS_EX_LAYERED: enables UpdateLayeredWindow
            // WS_EX_TRANSPARENT: click-through (mouse events pass through)
            // WS_EX_TOOLWINDOW: excluded from Alt+Tab and taskbar
            // WS_EX_NOACTIVATE: never steals focus
            // WS_EX_TOPMOST: always above normal windows
            const WINDOW_EX_STYLE exStyle =
                WINDOW_EX_STYLE.WS_EX_LAYERED |
                WINDOW_EX_STYLE.WS_EX_TRANSPARENT |
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                WINDOW_EX_STYLE.WS_EX_NOACTIVATE |
                WINDOW_EX_STYLE.WS_EX_TOPMOST;

            const WINDOW_STYLE style = WINDOW_STYLE.WS_POPUP;

            // Use the raw PCWSTR overload that accepts HINSTANCE directly.
            _hwnd = PInvoke.CreateWindowEx(
                exStyle,
                (PCWSTR)pClassName,
                default,
                style,
                0, 0, 0, 0,
                HWND.Null,
                default,
                _hInstance,
                null);
        }

        if (_hwnd == HWND.Null)
        {
            var err = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"CreateWindowEx failed: error {err}");
        }
    }

    private unsafe LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        const uint WM_PAINT = 0x000F;

        if (msg == WM_PAINT)
        {
            // CRITICAL: must handle WM_PAINT to prevent infinite repaint loop (100% CPU).
            PInvoke.BeginPaint(hwnd, out PAINTSTRUCT ps);
            PInvoke.EndPaint(hwnd, ps);
            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Positions and shows the overlay window at the given screen bounds.
    /// SWP_NOACTIVATE ensures focus is never stolen.
    /// </summary>
    public void Show(RECT bounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int width  = bounds.right  - bounds.left;
        int height = bounds.bottom - bounds.top;

        PInvoke.SetWindowPos(
            _hwnd,
            HwndTopmost,
            bounds.left, bounds.top,
            width, height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Hides the overlay window.
    /// </summary>
    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != HWND.Null)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }

        // Use raw PCWSTR overload with HINSTANCE.
        unsafe
        {
            fixed (char* pClassName = _className)
            {
                PInvoke.UnregisterClass((PCWSTR)pClassName, _hInstance);
            }
        }
    }
}
