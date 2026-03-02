using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon;

/// <summary>
/// Performs grid-snapped move and resize operations on the foreground window.
/// All coordinate math uses the dual-rect pattern:
///   - GetWindowRect: coordinate space for SetWindowPos input
///   - DWMWA_EXTENDED_FRAME_BOUNDS: visible bounds for snap and boundary math
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal static class WindowManagerService
{
    /// <summary>
    /// Moves or resizes the foreground window by one grid step in the given direction.
    /// Silently no-ops if the window is maximized or elevated (UIPI blocked).
    /// </summary>
    /// <param name="direction">Cardinal direction: "left", "right", "up", "down"</param>
    /// <param name="mode">Move or Grow (Grow handles both expand and contract by direction)</param>
    /// <param name="verbose">If true, logs failure details to stderr</param>
    public static unsafe void MoveOrResize(string direction, WindowMode mode, bool verbose)
    {
        var fgHwnd = PInvoke.GetForegroundWindow();
        if (fgHwnd == default) return;

        // Guard 1: maximized window — silent no-op (locked decision: refuse, do NOT restore)
        if (PInvoke.IsZoomed(fgHwnd)) return;

        // Get monitor work area (rcWork excludes taskbar; rcMonitor includes it)
        var workArea = GetWorkArea(fgHwnd);
        int workWidth  = workArea.right  - workArea.left;
        int workHeight = workArea.bottom - workArea.top;

        // Load config fresh at each operation so runtime changes take effect immediately
        var config = FocusConfig.Load();

        // Compute grid step for this monitor's work area dimensions
        var (stepX, stepY) = GridCalculator.GetGridStep(workWidth, workHeight, config.GridFractionX, config.GridFractionY);
        int tolX = GridCalculator.GetSnapTolerancePx(stepX, config.SnapTolerancePercent);
        int tolY = GridCalculator.GetSnapTolerancePx(stepY, config.SnapTolerancePercent);

        // Dual-rect Step 1: GetWindowRect — coordinate space for SetWindowPos
        RECT winRect = default;
        if (!PInvoke.GetWindowRect(fgHwnd, out winRect)) return;

        // Dual-rect Step 2: DWMWA_EXTENDED_FRAME_BOUNDS — visible bounds for snap/boundary math
        RECT visRect = default;
        var visBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref visRect, 1));
        var hr = PInvoke.DwmGetWindowAttribute(fgHwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, visBytes);
        if (!hr.Succeeded) return;

        // Border offsets: difference between GetWindowRect and visible bounds
        // Typically ~7px on left/right/bottom, 0px on top in Windows 10/11
        // Computed per-call — never hard-coded, because border size varies by DPI/style/version
        int borderLeft   = visRect.left   - winRect.left;
        int borderTop    = visRect.top    - winRect.top;
        int borderRight  = winRect.right  - visRect.right;
        int borderBottom = winRect.bottom - visRect.bottom;

        // All snap and boundary math uses visRect; result is translated back to winRect space
        RECT newWinRect = mode switch
        {
            WindowMode.Move => ComputeMove(direction, visRect, winRect, workArea, stepX, stepY, tolX, tolY, borderLeft, borderTop, borderRight, borderBottom),
            WindowMode.Grow => ComputeGrow(direction, visRect, winRect, workArea, stepX, stepY, tolX, tolY, borderLeft, borderTop, borderRight, borderBottom),
            _               => winRect // Navigate: no-op (should not reach here)
        };

        int newX  = newWinRect.left;
        int newY  = newWinRect.top;
        int newCx = newWinRect.right  - newWinRect.left;
        int newCy = newWinRect.bottom - newWinRect.top;

        // Guard 2: elevation / UIPI — SetWindowPos returns false for elevated target windows
        // SWP_NOACTIVATE: don't steal focus on each grid step
        // SWP_NOZORDER: preserve Z-order stacking
        // SWP_NOOWNERZORDER: preserve owner/owned window Z relationship
        bool ok = PInvoke.SetWindowPos(
            fgHwnd,
            default,
            newX, newY, newCx, newCy,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);

        if (verbose && !ok)
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SetWindowPos failed (elevated window?)");
    }

    /// <summary>
    /// Returns the work area (rcWork) of the monitor containing the given window.
    /// rcWork excludes the taskbar; rcMonitor includes it.
    /// </summary>
    private static unsafe RECT GetWorkArea(HWND hwnd)
    {
        var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (PInvoke.GetMonitorInfo(hMon, ref mi))
            return mi.rcWork;
        return default;
    }

    /// <summary>
    /// Computes the new window rect for a Move operation (CAPS+TAB+direction).
    /// Snap-first on the movement axis only; perpendicular axis is unchanged.
    /// Clamps to work area boundary (MOVE-03).
    /// </summary>
    private static RECT ComputeMove(string direction, RECT vis, RECT win, RECT work,
        int stepX, int stepY, int tolX, int tolY,
        int borderL, int borderT, int borderR, int borderB)
    {
        int visW = vis.right  - vis.left;
        int visH = vis.bottom - vis.top;

        int newVisLeft = vis.left;
        int newVisTop  = vis.top;

        switch (direction)
        {
            case "right":
                newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                    ? vis.left + stepX
                    : GridCalculator.NearestGridLineCeiling(vis.left, work.left, stepX);
                newVisLeft = Math.Clamp(newVisLeft, work.left, work.right - visW);
                break;
            case "left":
                newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                    ? vis.left - stepX
                    : GridCalculator.NearestGridLineFloor(vis.left, work.left, stepX);
                newVisLeft = Math.Clamp(newVisLeft, work.left, work.right - visW);
                break;
            case "down":
                newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                    ? vis.top + stepY
                    : GridCalculator.NearestGridLineCeiling(vis.top, work.top, stepY);
                newVisTop = Math.Clamp(newVisTop, work.top, work.bottom - visH);
                break;
            case "up":
                newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                    ? vis.top - stepY
                    : GridCalculator.NearestGridLineFloor(vis.top, work.top, stepY);
                newVisTop = Math.Clamp(newVisTop, work.top, work.bottom - visH);
                break;
        }

        // Translate visible coords back to GetWindowRect coordinate space
        return new RECT
        {
            left   = newVisLeft - borderL,
            top    = newVisTop  - borderT,
            right  = newVisLeft + visW + borderR,
            bottom = newVisTop  + visH + borderB
        };
    }

    /// <summary>
    /// Computes the new window rect for a Grow operation (CAPS+LSHIFT+direction).
    /// Direction encodes both axis and intent:
    ///   right = grow horizontal: both left and right edges expand outward by half a grid step each.
    ///   left  = shrink horizontal: both left and right edges contract inward by half a grid step each.
    ///   up    = grow vertical: both top and bottom edges expand outward by half a grid step each.
    ///   down  = shrink vertical: both top and bottom edges contract inward by half a grid step each.
    /// Each edge snaps independently. Expand clamps to work area boundary. Minimum size = 1 grid step.
    /// </summary>
    private static RECT ComputeGrow(string direction, RECT vis, RECT win, RECT work,
        int stepX, int stepY, int tolX, int tolY,
        int borderL, int borderT, int borderR, int borderB)
    {
        int newVisLeft   = vis.left;
        int newVisTop    = vis.top;
        int newVisRight  = vis.right;
        int newVisBottom = vis.bottom;
        int visW = vis.right  - vis.left;
        int visH = vis.bottom - vis.top;

        int halfStepX = stepX / 2;
        int halfStepY = stepY / 2;

        switch (direction)
        {
            case "right":
                // Grow horizontal: both edges expand outward
                newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                    ? vis.left - halfStepX
                    : GridCalculator.NearestGridLineFloor(vis.left, work.left, stepX);
                newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                    ? vis.right + halfStepX
                    : GridCalculator.NearestGridLineCeiling(vis.right, work.left, stepX);
                // Clamp to work area
                newVisLeft  = Math.Max(newVisLeft, work.left);
                newVisRight = Math.Min(newVisRight, work.right);
                break;

            case "left":
                // Shrink horizontal: both edges contract inward
                if (visW <= stepX) return win;  // minimum size guard
                newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                    ? vis.left + halfStepX
                    : GridCalculator.NearestGridLineCeiling(vis.left, work.left, stepX);
                newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                    ? vis.right - halfStepX
                    : GridCalculator.NearestGridLineFloor(vis.right, work.left, stepX);
                // Minimum size clamp
                if (newVisRight - newVisLeft < stepX)
                {
                    int center = (vis.left + vis.right) / 2;
                    newVisLeft  = center - stepX / 2;
                    newVisRight = center + stepX / 2;
                }
                break;

            case "up":
                // Grow vertical: both edges expand outward
                newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                    ? vis.top - halfStepY
                    : GridCalculator.NearestGridLineFloor(vis.top, work.top, stepY);
                newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                    ? vis.bottom + halfStepY
                    : GridCalculator.NearestGridLineCeiling(vis.bottom, work.top, stepY);
                // Clamp to work area
                newVisTop    = Math.Max(newVisTop, work.top);
                newVisBottom = Math.Min(newVisBottom, work.bottom);
                break;

            case "down":
                // Shrink vertical: both edges contract inward
                if (visH <= stepY) return win;  // minimum size guard
                newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                    ? vis.top + halfStepY
                    : GridCalculator.NearestGridLineCeiling(vis.top, work.top, stepY);
                newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                    ? vis.bottom - halfStepY
                    : GridCalculator.NearestGridLineFloor(vis.bottom, work.top, stepY);
                // Minimum size clamp
                if (newVisBottom - newVisTop < stepY)
                {
                    int center = (vis.top + vis.bottom) / 2;
                    newVisTop    = center - stepY / 2;
                    newVisBottom = center + stepY / 2;
                }
                break;
        }

        // For shrink directions (left/down): if visible dimension did not actually shrink, no-op
        int newVisW = newVisRight - newVisLeft;
        int newVisH = newVisBottom - newVisTop;
        if (direction is "left" && newVisW >= visW) return win;
        if (direction is "down" && newVisH >= visH) return win;

        // Translate visible coords back to GetWindowRect coordinate space
        return new RECT
        {
            left   = newVisLeft  - borderL,
            top    = newVisTop   - borderT,
            right  = newVisRight + borderR,
            bottom = newVisBottom + borderB
        };
    }
}
