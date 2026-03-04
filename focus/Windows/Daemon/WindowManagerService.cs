using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Focus.Windows;
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

        // Cross-monitor transition: applies to Move mode only (Grow stays on current monitor)
        if (mode == WindowMode.Move)
        {
            // Compute visible bounds of the newly computed position to check if it's at boundary
            int newVisLeft   = newWinRect.left   + borderLeft;
            int newVisTop    = newWinRect.top    + borderTop;
            int newVisRight  = newWinRect.right  - borderRight;
            int newVisBottom = newWinRect.bottom - borderBottom;

            bool atBoundary = direction switch
            {
                "right" => newVisRight  >= workArea.right,
                "left"  => newVisLeft   <= workArea.left,
                "down"  => newVisBottom >= workArea.bottom,
                "up"    => newVisTop    <= workArea.top,
                _       => false
            };

            if (atBoundary)
            {
                var crossTarget = TryGetCrossMonitorTarget(fgHwnd, direction);
                if (crossTarget.HasValue)
                {
                    var (targetWork, _) = crossTarget.Value;
                    int tw = targetWork.right  - targetWork.left;
                    int th = targetWork.bottom - targetWork.top;
                    var (tStepX, tStepY) = GridCalculator.GetGridStep(tw, th, config.GridFractionX, config.GridFractionY);

                    newWinRect = ComputeCrossMonitorPosition(
                        direction, visRect, winRect, targetWork, tStepX, tStepY,
                        borderLeft, borderTop, borderRight, borderBottom);
                }
            }
        }


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
    /// Returns the work area and monitor rect of the adjacent monitor in the given direction,
    /// or null if no adjacent monitor exists.
    /// Called only when the caller has already verified the window is at the work-area boundary.
    /// Uses rcMonitor (physical screen edges) for adjacency detection, not rcWork.
    /// </summary>
    private static unsafe (RECT work, RECT monitor)? TryGetCrossMonitorTarget(
        HWND hwnd, string direction)
    {
        // Get the current monitor's physical rect (rcMonitor) for adjacency detection
        var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (!PInvoke.GetMonitorInfo(hMon, ref mi)) return null;

        return MonitorHelper.FindAdjacentMonitor(hMon, mi.rcMonitor, direction);
    }

    /// <summary>
    /// Computes the window rect for a cross-monitor transition.
    /// Movement axis: snaps to first grid cell from the entry edge of the target monitor.
    /// Perpendicular axis: preserves current pixel position, clamped to target work area.
    /// If the window is larger than the target work area, size is clamped (not resized).
    /// Returns coords in GetWindowRect coordinate space (border offsets applied).
    /// </summary>
    private static RECT ComputeCrossMonitorPosition(
        string direction, RECT vis, RECT win, RECT targetWork,
        int tStepX, int tStepY,
        int borderL, int borderT, int borderR, int borderB)
    {
        int visW = vis.right  - vis.left;
        int visH = vis.bottom - vis.top;

        // Clamp window size to target work area — do not resize, only reposition
        int clampedW = Math.Min(visW, targetWork.right  - targetWork.left);
        int clampedH = Math.Min(visH, targetWork.bottom - targetWork.top);

        int newVisLeft, newVisTop;

        switch (direction)
        {
            case "right":
                // Enter from left edge: snap to first full grid cell from the left
                newVisLeft = targetWork.left + tStepX;
                // Preserve vertical position, clamped to target work area
                newVisTop  = Math.Clamp(vis.top, targetWork.top, targetWork.bottom - clampedH);
                break;
            case "left":
                // Enter from right edge: snap so right visible edge is one step from the right
                newVisLeft = targetWork.right - tStepX - clampedW;
                newVisTop  = Math.Clamp(vis.top, targetWork.top, targetWork.bottom - clampedH);
                break;
            case "down":
                // Enter from top edge: snap to first full grid cell from the top
                newVisTop  = targetWork.top + tStepY;
                newVisLeft = Math.Clamp(vis.left, targetWork.left, targetWork.right - clampedW);
                break;
            case "up":
                // Enter from bottom edge: snap so bottom visible edge is one step from the bottom
                newVisTop  = targetWork.bottom - tStepY - clampedH;
                newVisLeft = Math.Clamp(vis.left, targetWork.left, targetWork.right - clampedW);
                break;
            default:
                return win; // defensive no-op
        }

        // Translate visible coords back to GetWindowRect coordinate space
        return new RECT
        {
            left   = newVisLeft           - borderL,
            top    = newVisTop            - borderT,
            right  = newVisLeft + clampedW + borderR,
            bottom = newVisTop  + clampedH + borderB
        };
    }

    /// <summary>
    /// Computes the new window rect for a Grow operation (CAPS+LSHIFT+direction).
    /// Direction encodes both axis and intent:
    ///   right = grow horizontal, left = shrink horizontal,
    ///   up = grow vertical, down = shrink vertical.
    /// Resize uses a half-size grid (stepX/2, stepY/2). Size snaps to multiples of that grid.
    /// Center position is preserved unless pushed by a screen border. Minimum size = 1 resize step.
    /// </summary>
    private static RECT ComputeGrow(string direction, RECT vis, RECT win, RECT work,
        int stepX, int stepY, int tolX, int tolY,
        int borderL, int borderT, int borderR, int borderB)
    {
        int visW = vis.right  - vis.left;
        int visH = vis.bottom - vis.top;

        // Resize uses a half-size grid compared to the move grid
        int resizeStepX = Math.Max(1, stepX / 2);
        int resizeStepY = Math.Max(1, stepY / 2);
        int resizeTolX  = Math.Max(1, tolX / 2);
        int resizeTolY  = Math.Max(1, tolY / 2);

        int newVisW = visW;
        int newVisH = visH;

        switch (direction)
        {
            case "right": // Grow horizontal
                if (GridCalculator.IsAligned(visW, 0, resizeStepX, resizeTolX))
                    newVisW = GridCalculator.NearestGridLine(visW, 0, resizeStepX) + resizeStepX;
                else
                    newVisW = GridCalculator.NearestGridLineCeiling(visW, 0, resizeStepX);
                break;

            case "left": // Shrink horizontal
                if (visW <= resizeStepX) return win;
                if (GridCalculator.IsAligned(visW, 0, resizeStepX, resizeTolX))
                    newVisW = GridCalculator.NearestGridLine(visW, 0, resizeStepX) - resizeStepX;
                else
                    newVisW = GridCalculator.NearestGridLineFloor(visW, 0, resizeStepX);
                if (newVisW < resizeStepX) newVisW = resizeStepX;
                break;

            case "up": // Grow vertical
                if (GridCalculator.IsAligned(visH, 0, resizeStepY, resizeTolY))
                    newVisH = GridCalculator.NearestGridLine(visH, 0, resizeStepY) + resizeStepY;
                else
                    newVisH = GridCalculator.NearestGridLineCeiling(visH, 0, resizeStepY);
                break;

            case "down": // Shrink vertical
                if (visH <= resizeStepY) return win;
                if (GridCalculator.IsAligned(visH, 0, resizeStepY, resizeTolY))
                    newVisH = GridCalculator.NearestGridLine(visH, 0, resizeStepY) - resizeStepY;
                else
                    newVisH = GridCalculator.NearestGridLineFloor(visH, 0, resizeStepY);
                if (newVisH < resizeStepY) newVisH = resizeStepY;
                break;
        }

        // Preserve center position
        int centerX = vis.left + visW / 2;
        int centerY = vis.top  + visH / 2;

        int newVisLeft   = centerX - newVisW / 2;
        int newVisTop    = centerY - newVisH / 2;
        int newVisRight  = newVisLeft + newVisW;
        int newVisBottom = newVisTop  + newVisH;

        // Clamp to work area boundaries, pushing center only when necessary
        if (newVisLeft < work.left)
        {
            newVisLeft  = work.left;
            newVisRight = newVisLeft + newVisW;
        }
        if (newVisRight > work.right)
        {
            newVisRight = work.right;
            newVisLeft  = newVisRight - newVisW;
        }
        if (newVisTop < work.top)
        {
            newVisTop    = work.top;
            newVisBottom = newVisTop + newVisH;
        }
        if (newVisBottom > work.bottom)
        {
            newVisBottom = work.bottom;
            newVisTop    = newVisBottom - newVisH;
        }

        // Final safety: clamp if window is larger than work area
        newVisLeft   = Math.Max(newVisLeft, work.left);
        newVisTop    = Math.Max(newVisTop, work.top);
        newVisRight  = Math.Min(newVisRight, work.right);
        newVisBottom = Math.Min(newVisBottom, work.bottom);

        // For shrink: if size didn't actually decrease, no-op
        int finalW = newVisRight - newVisLeft;
        int finalH = newVisBottom - newVisTop;
        if (direction is "left" && finalW >= visW) return win;
        if (direction is "down" && finalH >= visH) return win;

        // Translate visible coords back to GetWindowRect coordinate space
        return new RECT
        {
            left   = newVisLeft   - borderL,
            top    = newVisTop    - borderT,
            right  = newVisRight  + borderR,
            bottom = newVisBottom + borderB
        };
    }
}
