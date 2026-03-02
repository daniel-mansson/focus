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
    /// <param name="mode">Move, Grow, or Shrink</param>
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
            WindowMode.Move   => ComputeMove(direction, visRect, winRect, workArea, stepX, stepY, tolX, tolY, borderLeft, borderTop, borderRight, borderBottom),
            WindowMode.Grow   => ComputeGrow(direction, visRect, winRect, workArea, stepX, stepY, tolX, tolY, borderLeft, borderTop, borderRight, borderBottom),
            WindowMode.Shrink => ComputeShrink(direction, visRect, winRect, workArea, stepX, stepY, tolX, tolY, borderLeft, borderTop, borderRight, borderBottom),
            _                 => winRect // Navigate: no-op (should not reach here)
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
            case "left":
            case "right":
            {
                int sign = direction == "right" ? 1 : -1;
                newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                    ? vis.left + sign * stepX                                        // aligned → step
                    : GridCalculator.NearestGridLine(vis.left, work.left, stepX);   // not aligned → snap
                // Clamp: keep visible window within work area boundaries (MOVE-03)
                newVisLeft = Math.Clamp(newVisLeft, work.left, work.right - visW);
                break;
            }
            case "up":
            case "down":
            {
                int sign = direction == "down" ? 1 : -1;
                newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                    ? vis.top + sign * stepY
                    : GridCalculator.NearestGridLine(vis.top, work.top, stepY);
                newVisTop = Math.Clamp(newVisTop, work.top, work.bottom - visH);
                break;
            }
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
    /// The edge in the pressed direction moves outward; the opposite edge is fixed.
    /// Snap-first on the moving edge. Clamps the moving edge to the work area boundary (SIZE-04).
    /// When the edge is already at the boundary, the operation is a silent no-op.
    /// </summary>
    private static RECT ComputeGrow(string direction, RECT vis, RECT win, RECT work,
        int stepX, int stepY, int tolX, int tolY,
        int borderL, int borderT, int borderR, int borderB)
    {
        int newVisLeft   = vis.left;
        int newVisTop    = vis.top;
        int newVisRight  = vis.right;
        int newVisBottom = vis.bottom;

        switch (direction)
        {
            case "right":
                // Right edge moves outward (rightward); left edge stays fixed
                newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                    ? vis.right + stepX
                    : GridCalculator.NearestGridLineCeiling(vis.right, work.left, stepX);  // snap rightward (outward)
                newVisRight = Math.Min(newVisRight, work.right);   // clamp to work area (SIZE-04)
                break;

            case "left":
                // Left edge moves outward (leftward); right edge stays fixed
                newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                    ? vis.left - stepX
                    : GridCalculator.NearestGridLineFloor(vis.left, work.left, stepX);     // snap leftward (outward)
                newVisLeft = Math.Max(newVisLeft, work.left);      // clamp to work area (SIZE-04)
                break;

            case "down":
                // Bottom edge moves outward (downward); top edge stays fixed
                newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                    ? vis.bottom + stepY
                    : GridCalculator.NearestGridLineCeiling(vis.bottom, work.top, stepY);  // snap downward (outward)
                newVisBottom = Math.Min(newVisBottom, work.bottom); // clamp to work area (SIZE-04)
                break;

            case "up":
                // Top edge moves outward (upward); bottom edge stays fixed
                newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                    ? vis.top - stepY
                    : GridCalculator.NearestGridLineFloor(vis.top, work.top, stepY);       // snap upward (outward)
                newVisTop = Math.Max(newVisTop, work.top);          // clamp to work area (SIZE-04)
                break;
        }

        // Translate visible coords back to GetWindowRect coordinate space
        return new RECT
        {
            left   = newVisLeft  - borderL,
            top    = newVisTop   - borderT,
            right  = newVisRight + borderR,
            bottom = newVisBottom + borderB
        };
    }

    /// <summary>
    /// Computes the new window rect for a Shrink operation (CAPS+LCTRL+direction).
    /// The edge in the pressed direction moves inward. Minimum size = 1 grid step (SIZE-03).
    /// At minimum size, the operation is a silent no-op (returns unchanged win rect).
    /// Snap-first on the moving edge.
    /// </summary>
    private static RECT ComputeShrink(string direction, RECT vis, RECT win, RECT work,
        int stepX, int stepY, int tolX, int tolY,
        int borderL, int borderT, int borderR, int borderB)
    {
        int newVisLeft   = vis.left;
        int newVisTop    = vis.top;
        int newVisRight  = vis.right;
        int newVisBottom = vis.bottom;
        int visW = vis.right  - vis.left;
        int visH = vis.bottom - vis.top;

        switch (direction)
        {
            case "up":
                // Shrink-up: BOTTOM edge moves upward (inward); top edge stays fixed
                if (visH <= stepY) return win;  // minimum size guard — silent no-op (SIZE-03)
                newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                    ? vis.bottom - stepY
                    : GridCalculator.NearestGridLineFloor(vis.bottom, work.top, stepY);  // snap upward (inward)
                newVisBottom = Math.Max(newVisBottom, newVisTop + stepY); // min-size clamp (SIZE-03)
                break;

            case "down":
                // Shrink-down: TOP edge moves downward (inward); bottom edge stays fixed
                if (visH <= stepY) return win;
                newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                    ? vis.top + stepY
                    : GridCalculator.NearestGridLineCeiling(vis.top, work.top, stepY);  // snap downward (inward)
                newVisTop = Math.Min(newVisTop, newVisBottom - stepY); // min-size clamp (SIZE-03)
                break;

            case "left":
                // Shrink-left: RIGHT edge moves leftward (inward); left edge stays fixed
                if (visW <= stepX) return win;
                newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                    ? vis.right - stepX
                    : GridCalculator.NearestGridLineFloor(vis.right, work.left, stepX);  // snap leftward (inward)
                newVisRight = Math.Max(newVisRight, newVisLeft + stepX); // min-size clamp (SIZE-03)
                break;

            case "right":
                // Shrink-right: LEFT edge moves rightward (inward); right edge stays fixed
                if (visW <= stepX) return win;
                newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                    ? vis.left + stepX
                    : GridCalculator.NearestGridLineCeiling(vis.left, work.left, stepX);  // snap rightward (inward)
                newVisLeft = Math.Min(newVisLeft, newVisRight - stepX); // min-size clamp (SIZE-03)
                break;
        }

        // Post-computation no-op guard: if the visible dimension did not actually shrink,
        // SetWindowPos would only move the window (OS clamps to ptMinTrackSize). Return unchanged.
        int newVisW = newVisRight - newVisLeft;
        int newVisH = newVisBottom - newVisTop;
        if (newVisW >= visW && newVisH >= visH) return win;

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
