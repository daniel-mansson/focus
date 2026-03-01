using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Accessibility;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Detects foreground window changes via SetWinEventHook(EVENT_SYSTEM_FOREGROUND).
/// Must be installed and uninstalled on the STA thread (the thread with the message pump).
/// Callback fires on the STA thread when WINEVENT_OUTOFCONTEXT is used.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class ForegroundMonitor : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private readonly Action<HWND> _onChanged;

    // Delegate MUST be stored in an instance field AND pinned with GCHandle.Alloc.
    // A local variable would be collected by GC after Install() returns.
    private WINEVENTPROC? _proc;
    private GCHandle _procHandle;
    private HWINEVENTHOOK _hook;
    private bool _disposed;

    /// <summary>
    /// Creates a ForegroundMonitor. Call Install() on the STA thread to activate it.
    /// </summary>
    /// <param name="onForegroundChanged">
    /// Callback invoked on the STA thread when the foreground window changes.
    /// The HWND parameter is the new foreground window handle.
    /// </param>
    public ForegroundMonitor(Action<HWND> onForegroundChanged)
    {
        _onChanged = onForegroundChanged;
    }

    /// <summary>
    /// Installs the WinEvent hook. Must be called on the STA thread.
    /// </summary>
    public void Install()
    {
        _proc = Callback;
        _procHandle = GCHandle.Alloc(_proc);
        _hook = PInvoke.SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null,
            _proc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>
    /// Uninstalls the WinEvent hook. Must be called on the same STA thread as Install().
    /// </summary>
    public void Uninstall()
    {
        if (_hook != default)
        {
            PInvoke.UnhookWinEvent(_hook);
            _hook = default;
        }

        if (_procHandle.IsAllocated)
            _procHandle.Free();

        _proc = null;
    }

    private void Callback(
        HWINEVENTHOOK hook,
        uint eventType,
        HWND hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        // Use the hwnd parameter directly — do NOT call GetForegroundWindow() here.
        // (Pitfall 4: foreground may have changed again by the time a second call completes.)
        _onChanged(hwnd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
    }
}
