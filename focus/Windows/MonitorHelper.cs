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

    /// <summary>
    /// Finds the adjacent monitor in the given direction from the current monitor.
    /// Uses overlapping-range adjacency: a candidate monitor is adjacent if its near edge is
    /// within 2px of the current monitor's far edge AND the two monitors overlap perpendicularly.
    /// If multiple candidates satisfy adjacency (e.g., triple-monitor arrangements), returns
    /// the one with the greatest perpendicular overlap.
    /// Returns null if no adjacent monitor exists in the given direction.
    /// </summary>
    /// <param name="current">HMONITOR handle of the current monitor</param>
    /// <param name="currentMonitorRect">rcMonitor rect of the current monitor (physical screen edges)</param>
    /// <param name="direction">Cardinal direction: "left", "right", "up", "down"</param>
    public static unsafe (RECT Work, RECT Monitor)? FindAdjacentMonitor(
        HMONITOR current, RECT currentMonitorRect, string direction)
    {
        const int EdgeTol = 2;

        RECT bestWork    = default;
        RECT bestMonitor = default;
        int  bestOverlap = 0;
        bool found       = false;

        // Store delegate in local variable to prevent GC collection during unmanaged callback
        MONITORENUMPROC callback = (hMon, _, _, _) =>
        {
            // Skip the current monitor (compare handles as nint)
            if ((nint)(IntPtr)hMon == (nint)(IntPtr)current)
                return true;

            MONITORINFO mi = default;
            mi.cbSize = (uint)sizeof(MONITORINFO);
            if (!PInvoke.GetMonitorInfo(hMon, ref mi))
                return true;

            RECT rcMon  = mi.rcMonitor;
            RECT rcWork = mi.rcWork;

            // Check adjacency based on direction: near edge of candidate must be within EdgeTol
            // of far edge of current monitor on the movement axis
            bool adjacent = direction switch
            {
                "right" => Math.Abs(rcMon.left   - currentMonitorRect.right)  <= EdgeTol,
                "left"  => Math.Abs(rcMon.right  - currentMonitorRect.left)   <= EdgeTol,
                "down"  => Math.Abs(rcMon.top    - currentMonitorRect.bottom) <= EdgeTol,
                "up"    => Math.Abs(rcMon.bottom - currentMonitorRect.top)    <= EdgeTol,
                _       => false
            };

            if (!adjacent)
                return true;

            // Compute perpendicular overlap (vertical overlap for left/right, horizontal for up/down)
            int overlap = direction is "right" or "left"
                ? Math.Max(0, Math.Min(rcMon.bottom, currentMonitorRect.bottom)
                              - Math.Max(rcMon.top,  currentMonitorRect.top))
                : Math.Max(0, Math.Min(rcMon.right,  currentMonitorRect.right)
                              - Math.Max(rcMon.left,  currentMonitorRect.left));

            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestWork    = rcWork;
                bestMonitor = rcMon;
                found       = true;
            }

            return true;
        };

        PInvoke.EnumDisplayMonitors(default, (RECT?)null, callback, default);

        return found ? (bestWork, bestMonitor) : null;
    }

    /// <summary>
    /// Returns the center point of the primary monitor in virtual screen coordinates.
    /// Used as origin fallback when no foreground window exists (e.g., desktop is focused).
    /// MONITORINFOF_PRIMARY is not generated by CsWin32 (issue #1004) -- use the constant directly.
    /// </summary>
    public static unsafe (double X, double Y) GetPrimaryMonitorCenter()
    {
        // MONITORINFOF_PRIMARY = 0x00000001 (stable since Windows 2000; not generated by CsWin32)
        HMONITOR hm = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (PInvoke.GetMonitorInfo(hm, ref mi))
        {
            return (
                (mi.rcMonitor.left + mi.rcMonitor.right) / 2.0,
                (mi.rcMonitor.top + mi.rcMonitor.bottom) / 2.0
            );
        }
        return (0.0, 0.0); // ultimate fallback
    }
}
