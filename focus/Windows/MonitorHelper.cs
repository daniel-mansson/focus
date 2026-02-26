using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Foundation;

namespace Focus.Windows;

/// <summary>
/// Utilities for enumerating monitors and mapping HWNDs to monitor indices.
/// All methods require Windows 5.0 or later.
/// </summary>
[SupportedOSPlatform("windows5.0")]
internal static class MonitorHelper
{
    /// <summary>
    /// Enumerates all connected monitors via EnumDisplayMonitors.
    /// Returns HMONITOR handles as nint values for use with GetMonitorIndex.
    /// The delegate is stored in a local to prevent GC collection during enumeration.
    /// </summary>
    public static unsafe List<nint> EnumerateMonitors()
    {
        var monitors = new List<nint>();

        // Store delegate in local variable to prevent GC collection during unmanaged callback
        MONITORENUMPROC callback = (hMon, _, _, _) =>
        {
            monitors.Add((nint)(IntPtr)hMon);
            return true;
        };

        PInvoke.EnumDisplayMonitors(default, (RECT?)null, callback, default);

        return monitors;
    }

    /// <summary>
    /// Maps an HWND to a 1-based monitor index using MonitorFromWindow.
    /// Returns 1 as fallback if the monitor is not found in the provided list.
    /// </summary>
    public static unsafe int GetMonitorIndex(nint hwnd, List<nint> monitors)
    {
        var hWnd = new HWND((void*)(IntPtr)hwnd);
        HMONITOR hm = PInvoke.MonitorFromWindow(hWnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        nint hmAsNint = (nint)(IntPtr)hm;

        int idx = monitors.IndexOf(hmAsNint);
        return idx >= 0 ? idx + 1 : 1; // 1-based
    }
}
