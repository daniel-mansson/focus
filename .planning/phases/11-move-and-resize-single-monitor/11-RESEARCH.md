# Phase 11: Move and Resize (Single Monitor) - Research

**Researched:** 2026-03-02
**Domain:** Win32 window manipulation (SetWindowPos, GetWindowRect, IsZoomed), coordinate systems, UIPI guards, grid snap math
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Resize edge selection
- **Directional edge model:** The edge in the pressed direction is the one that moves
  - CAPS+LSHIFT+Right → right edge moves rightward (grow)
  - CAPS+LCTRL+Right → right edge moves leftward (shrink)
  - Same principle for all four directions — each direction key controls its own edge
- Grow and shrink are mirror operations on the same edge — same direction key, same edge, opposite movement direction

#### Boundary behavior (grow)
- Growing stops at the monitor work area boundary — if the edge is already at the boundary, the operation is a no-op
- Do NOT push the opposite edge when hitting the boundary
- Work area boundary = rcWork (excludes taskbar), not rcMonitor

#### Minimum size (shrink)
- Shrink stops when the window dimension would go below one grid cell (1 grid step)
- Do not allow sub-cell window sizes
- This is a hard stop — pressing shrink at minimum size is a silent no-op

#### Snap-first behavior
- Per success criteria: a misaligned window snaps to the nearest grid line on the first operation, then steps by one grid cell on subsequent presses
- GridCalculator.IsAligned with configurable snapTolerancePercent (default 10%) determines if snap is needed

#### Guarded windows
- Per success criteria: maximized and elevated (admin) windows produce no visible error and no window change — silent no-op

### Claude's Discretion
- Whether to restore a maximized window before moving/resizing, or simply refuse (success criteria says "no window change" so refuse is the safe read)
- Snap-first implementation details: whether snap-only counts as a "press" or is an invisible pre-step
- Overlay indicators for move/resize modes (OVRL-01/02/03 in requirements — may be deferred to a separate phase or included here)
- Internal architecture: whether to create a new WindowManagerService class or extend OverlayOrchestrator

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| MOVE-01 | User can move the foreground window by one grid step in any direction (CAPS+TAB+direction) | SetWindowPos with SWP_NOACTIVATE\|SWP_NOZORDER\|SWP_NOOWNERZORDER; GetWindowRect for input rect; dual-rect pattern for invisible-border correction |
| MOVE-02 | User can press direction keys repeatedly for consecutive grid steps while CAPS+TAB held | Existing CapsLockMonitor _directionKeysHeld repeat suppression already handles this; each keydown fires once per physical press |
| MOVE-03 | Window position clamped to monitor work area boundaries | rcWork from GetMonitorInfo; clamp newLeft to [rcWork.left, rcWork.right - width], newTop to [rcWork.top, rcWork.bottom - height] |
| SIZE-01 | User can grow a window edge outward by one grid step (CAPS+LSHIFT+direction) | SetWindowPos targeting only the affected edge; same dual-rect and clamp pattern as MOVE |
| SIZE-02 | User can shrink a window edge inward by one grid step (CAPS+LCTRL+direction) | Same SetWindowPos pattern; min-size guard replaces clamp-to-boundary check |
| SIZE-03 | Shrink stops at minimum window size | Minimum = 1 grid step per axis; check computed dimension before calling SetWindowPos; silent no-op if already at minimum |
| SIZE-04 | Grow stops at monitor work area boundary | Clamp the moving edge to rcWork boundary before calling SetWindowPos |
</phase_requirements>

## Summary

Phase 11 replaces the "not yet implemented" placeholder in `OverlayOrchestrator.OnDirectionKeyDown` (lines 131–149) with a `WindowManagerService` (or equivalent inline method) that performs grid-snapped move and resize operations on the foreground window. All infrastructure built in Phase 10 — `GridCalculator`, `FocusConfig.GridFractionX/Y/SnapTolerancePercent`, `WindowMode` enum routing, `MonitorHelper.GetMonitorInfo` patterns — is ready to consume.

The most important technical distinction for this phase is the **dual-rect coordinate pattern**: `DWMWA_EXTENDED_FRAME_BOUNDS` gives visible bounds for user-facing snap math and boundary comparison, but `SetWindowPos` requires input coordinates in the window's own (potentially DPI-virtualized) coordinate space — which `GetWindowRect` provides. Because this daemon runs as PerMonitorV2-aware (per app.manifest), GetWindowRect returns physical-pixel coordinates for the windows it queries. The offset between GetWindowRect and DWMWA_EXTENDED_FRAME_BOUNDS (typically ~7px per invisible border on Windows 10/11) must be preserved when computing the new position to pass to SetWindowPos — otherwise windows drift or accumulate position error across repeated operations.

The two guards (maximized via `IsZoomed`, elevated via `SetWindowPos` return value) are both silent no-ops, keeping the UX clean. UIPI prevents SetWindowPos from succeeding on admin windows from a medium-integrity process; the return value is the reliable signal. STATE.md also flags that SetWindowPos may auto-clamp to the window's minimum track size (WM_GETMINMAXINFO.ptMinTrackSize) — this means the size-03 minimum-cell guard and OS auto-clamping stack defensively without conflict.

**Primary recommendation:** Create `WindowManagerService` as a new static class in `Focus.Windows.Daemon.Overlay` (or `Focus.Windows.Daemon`). Inject it into `OverlayOrchestrator.OnDirectionKeyDown` via the existing STA-thread marshaling path. Use the dual-rect pattern for all coordinate work, `IsZoomed` for maximized guard, and treat `SetWindowPos` return value false as the elevated-window signal.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Windows.CsWin32 | 0.3.269 | GetWindowRect, IsZoomed, SetWindowPos, GetMonitorInfo bindings | Already in project; already used for SetWindowPos in OverlayWindow.cs |
| Windows.Win32.Foundation (generated) | auto | HWND, RECT types | Already used throughout |
| Windows.Win32.Graphics.Gdi (generated) | auto | HMONITOR, MONITORINFO, GetMonitorInfo | Already used in OverlayOrchestrator.ClampToMonitor |
| Windows.Win32.Graphics.Dwm (generated) | auto | DwmGetWindowAttribute, DWMWA_EXTENDED_FRAME_BOUNDS | Already used in WindowEnumerator, OverlayOrchestrator |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| GridCalculator (project) | Phase 10 | GetGridStep, NearestGridLine, IsAligned, GetSnapTolerancePx | Every move/resize operation |
| FocusConfig (project) | Phase 10 | GridFractionX, GridFractionY, SnapTolerancePercent | Load fresh at each operation call site |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| GetWindowRect for SetWindowPos input | DWMWA_EXTENDED_FRAME_BOUNDS | DWM bounds are not DPI-adjusted; GetWindowRect IS the correct coordinate space for SetWindowPos input |
| Return-value check for elevation guard | OpenProcess + token integrity level query | Return-value check is simpler, more reliable in practice, and avoids permission complexity |
| IsZoomed for maximized guard | GetWindowPlacement + SW_SHOWMAXIMIZED | IsZoomed is one call, correct for this use case |

**Installation:** No new packages. `GetWindowRect` and `IsZoomed` need to be added to `NativeMethods.txt`.

## Architecture Patterns

### Recommended Project Structure
```
focus/Windows/Daemon/
├── WindowManagerService.cs   # NEW: static class, Move/Grow/Shrink operations
focus/Windows/Daemon/Overlay/
├── OverlayOrchestrator.cs    # EDIT: replace placeholder in OnDirectionKeyDown with WindowManagerService call
focus/
└── NativeMethods.txt         # ADD: GetWindowRect, IsZoomed
```

### Pattern 1: Integration Point — OverlayOrchestrator.OnDirectionKeyDown

**What:** Replace the `mode != WindowMode.Navigate` block at lines 131–149 with a dispatch to `WindowManagerService`.

**Current code (to replace):**
```csharp
if (mode != WindowMode.Navigate)
{
    // Move/Grow/Shrink modes -- Phase 11 will implement WindowManagerService.
    // For now, log and return (no-op).
    if (_verbose)
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Mode {mode} direction {direction} -- not yet implemented");
    return;
}
```

**Replacement:**
```csharp
if (mode != WindowMode.Navigate)
{
    try
    {
        _staDispatcher.Invoke(() => ManageWindowSta(direction, mode));
    }
    catch (ObjectDisposedException) { }
    catch (InvalidOperationException) { }
    return;
}
```

### Pattern 2: WindowManagerService — Core Operation Method

**What:** A static service class that performs move and resize operations. Receives direction + mode, gets foreground HWND, applies guards, computes new rect, calls SetWindowPos.

```csharp
// WindowManagerService.cs
[SupportedOSPlatform("windows6.0.6000")]
internal static class WindowManagerService
{
    public static unsafe void MoveOrResize(string direction, WindowMode mode, FocusConfig config, bool verbose)
    {
        var fgHwnd = PInvoke.GetForegroundWindow();
        if (fgHwnd == default) return;

        // Guard 1: maximized window — silent no-op
        if (PInvoke.IsZoomed(fgHwnd)) return;

        // Get monitor work area for the foreground window
        var workArea = GetWorkArea(fgHwnd);
        int workWidth  = workArea.right  - workArea.left;
        int workHeight = workArea.bottom - workArea.top;

        // Compute grid step for this monitor
        var config2 = FocusConfig.Load(); // fresh load at operation time
        var (stepX, stepY) = GridCalculator.GetGridStep(workWidth, workHeight, config2.GridFractionX, config2.GridFractionY);
        int snapTolerancePxX = GridCalculator.GetSnapTolerancePx(stepX, config2.SnapTolerancePercent);
        int snapTolerancePxY = GridCalculator.GetSnapTolerancePx(stepY, config2.SnapTolerancePercent);

        // Dual-rect: GetWindowRect for SetWindowPos input coordinates
        RECT winRect = default;
        if (!PInvoke.GetWindowRect(fgHwnd, out winRect)) return;

        // DWMWA_EXTENDED_FRAME_BOUNDS for visible bounds (snap and boundary math)
        RECT visRect = default;
        var visBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref visRect, 1));
        var hr = PInvoke.DwmGetWindowAttribute(fgHwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, visBytes);
        if (!hr.Succeeded) return;

        // Border offsets: difference between GetWindowRect and visible bounds
        // These are the invisible resize borders Windows adds on left/right/bottom
        int borderLeft   = visRect.left   - winRect.left;
        int borderTop    = visRect.top    - winRect.top;
        int borderRight  = winRect.right  - visRect.right;
        int borderBottom = winRect.bottom - visRect.bottom;

        // Compute new rect in GetWindowRect coordinate space
        // All snap and boundary math uses visRect; result is translated back to winRect space
        RECT newWinRect = mode switch
        {
            WindowMode.Move   => ComputeMove(direction, visRect, winRect, workArea, stepX, stepY, snapTolerancePxX, snapTolerancePxY, borderLeft, borderTop, borderRight, borderBottom),
            WindowMode.Grow   => ComputeGrow(direction, visRect, winRect, workArea, stepX, stepY, snapTolerancePxX, snapTolerancePxY, borderLeft, borderTop, borderRight, borderBottom),
            WindowMode.Shrink => ComputeShrink(direction, visRect, winRect, workArea, stepX, stepY, snapTolerancePxX, snapTolerancePxY, borderLeft, borderTop, borderRight, borderBottom),
            _ => winRect // Navigate: no-op (should not reach here)
        };

        int newX  = newWinRect.left;
        int newY  = newWinRect.top;
        int newCx = newWinRect.right  - newWinRect.left;
        int newCy = newWinRect.bottom - newWinRect.top;

        // Guard 2: elevation / UIPI — SetWindowPos returns false for elevated windows
        bool ok = PInvoke.SetWindowPos(
            fgHwnd,
            default,                           // hWndInsertAfter ignored (SWP_NOZORDER)
            newX, newY, newCx, newCy,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);

        if (verbose && !ok)
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SetWindowPos failed (elevated window?)");
    }

    private static unsafe RECT GetWorkArea(HWND hwnd)
    {
        var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (PInvoke.GetMonitorInfo(hMon, ref mi))
            return mi.rcWork;
        return default;
    }
}
```

### Pattern 3: Move Operation

**What:** Snap-first, then step. Only the movement axis (X for left/right, Y for up/down) is snapped. The perpendicular axis is unchanged.

```csharp
private static RECT ComputeMove(string direction, RECT vis, RECT win, RECT work,
    int stepX, int stepY, int tolX, int tolY,
    int borderL, int borderT, int borderR, int borderB)
{
    // Window visible dimensions (constant for move)
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
                ? vis.left + sign * stepX        // already aligned → step
                : GridCalculator.NearestGridLine(vis.left, work.left, stepX);  // not aligned → snap
            // Clamp: keep visible window within work area
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

    // Translate visible → GetWindowRect space
    return new RECT
    {
        left   = newVisLeft - borderL,
        top    = newVisTop  - borderT,
        right  = newVisLeft + visW + borderR,
        bottom = newVisTop  + visH + borderB
    };
}
```

### Pattern 4: Grow Operation

**What:** Only the edge in the pressed direction moves outward. The opposite edge is fixed. Clamp the moving edge to the work area boundary (SIZE-04).

```csharp
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
            newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                ? vis.right + stepX
                : GridCalculator.NearestGridLine(vis.right, work.left, stepX);
            newVisRight = Math.Min(newVisRight, work.right);   // clamp to work area
            break;
        case "left":
            newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                ? vis.left - stepX
                : GridCalculator.NearestGridLine(vis.left, work.left, stepX);
            newVisLeft = Math.Max(newVisLeft, work.left);      // clamp to work area
            break;
        case "down":
            newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                ? vis.bottom + stepY
                : GridCalculator.NearestGridLine(vis.bottom, work.top, stepY);
            newVisBottom = Math.Min(newVisBottom, work.bottom);
            break;
        case "up":
            newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                ? vis.top - stepY
                : GridCalculator.NearestGridLine(vis.top, work.top, stepY);
            newVisTop = Math.Max(newVisTop, work.top);
            break;
    }

    return new RECT
    {
        left   = newVisLeft - borderL,
        top    = newVisTop  - borderT,
        right  = newVisRight + borderR,
        bottom = newVisBottom + borderB
    };
}
```

### Pattern 5: Shrink Operation

**What:** The edge in the pressed direction moves inward. Minimum size = 1 grid step in the axis of movement (SIZE-03). At minimum, the operation is a silent no-op.

```csharp
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
        case "right":   // right edge moves leftward (shrink width)
            if (visW <= stepX) return win; // minimum size — no-op (return unchanged win rect)
            newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                ? vis.right - stepX
                : GridCalculator.NearestGridLine(vis.right, work.left, stepX);
            newVisRight = Math.Max(newVisRight, newVisLeft + stepX);  // min-size clamp
            break;
        case "left":    // left edge moves rightward (shrink width)
            if (visW <= stepX) return win;
            newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                ? vis.left + stepX
                : GridCalculator.NearestGridLine(vis.left, work.left, stepX);
            newVisLeft = Math.Min(newVisLeft, newVisRight - stepX);
            break;
        case "down":    // bottom edge moves upward (shrink height)
            if (visH <= stepY) return win;
            newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                ? vis.bottom - stepY
                : GridCalculator.NearestGridLine(vis.bottom, work.top, stepY);
            newVisBottom = Math.Max(newVisBottom, newVisTop + stepY);
            break;
        case "up":      // top edge moves downward (shrink height)
            if (visH <= stepY) return win;
            newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                ? vis.top + stepY
                : GridCalculator.NearestGridLine(vis.top, work.top, stepY);
            newVisTop = Math.Min(newVisTop, newVisBottom - stepY);
            break;
    }

    return new RECT
    {
        left   = newVisLeft - borderL,
        top    = newVisTop  - borderT,
        right  = newVisRight + borderR,
        bottom = newVisBottom + borderB
    };
}
```

### Pattern 6: Snap Direction for Grow/Shrink Edges

**What:** For grow/shrink, the snap check applies to the EDGE being moved, not the window origin. The snap origin is always `work.left` (for X) and `work.top` (for Y) — grid lines are at `work.origin + N*step`.

- Growing the RIGHT edge: check IsAligned(vis.right, work.left, stepX, tolX)
- Shrinking the RIGHT edge: same check, opposite step direction
- Growing the LEFT edge: check IsAligned(vis.left, work.left, stepX, tolX)

This is consistent with move mode where vis.left is checked against work.left as origin.

### Anti-Patterns to Avoid

- **Using DWMWA_EXTENDED_FRAME_BOUNDS as SetWindowPos input:** The DWM bounds are NOT DPI-adjusted and do not include invisible borders. SetWindowPos expects the window's own (GetWindowRect) coordinate space. Mixing them produces slow drift.
- **Using rcMonitor instead of rcWork for boundary clamping:** rcMonitor includes the taskbar. Windows positioned against rcMonitor can slide behind the taskbar. Always use rcWork.
- **Not preserving border offsets across repeated operations:** If border offsets are recomputed per call but rounded differently, rounding errors accumulate. Compute once per operation from the pair (GetWindowRect, DwmExtendedFrameBounds).
- **Calling SetWindowPos without SWP_NOACTIVATE:** Would steal focus from the target window on every grid step, making multi-step operations unusable.
- **Calling SetWindowPos without SWP_NOZORDER:** Would change the window's Z-order stacking on every move.
- **Not calling SWP_NOOWNERZORDER:** On windows with owner relationships, omitting this flag can reorder owned/owner windows unexpectedly.
- **Attempting to detect elevation before calling SetWindowPos:** OpenProcess+token queries are complex and can themselves fail due to UIPI. The SetWindowPos return value is the authoritative signal — use it as the guard.
- **Checking IsZoomed after restore:** The user decision is "refuse" for maximized windows, not "restore then move." Check IsZoomed first and return immediately.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Invisible border size | Hard-coded 7px constant | GetWindowRect - DwmGetWindowAttribute difference per window | Border size varies by DPI, window style, and Windows version |
| Elevation detection | OpenProcess + OpenProcessToken + GetTokenInformation | Check SetWindowPos return value | UIPI blocks the operation silently on the WinAPI side; return value is authoritative |
| Grid step | Custom formula | GridCalculator.GetGridStep (Phase 10) | Already tested, handles edge cases (step <= 0) |
| Monitor work area | Custom monitor enumeration | MonitorHelper pattern + GetMonitorInfo.rcWork | Already in codebase, already correct |
| Snap math | Custom rounding | GridCalculator.NearestGridLine / IsAligned | Already handles multi-monitor origin offset correctly |
| Min-size OS clamp | Custom ptMinTrackSize lookup | Let SetWindowPos handle it + explicit per-axis grid-step minimum | STATE.md confirms OS auto-clamps to ptMinTrackSize; explicit minimum-step guard gives predictable UX |

**Key insight:** The dual-rect pattern (GetWindowRect for coordinates, DwmExtendedFrameBounds for visible position) is the established Windows 10/11 pattern for any process that moves other processes' windows. It is not optional — skipping it causes drift.

## Common Pitfalls

### Pitfall 1: Coordinate Space Mismatch (Dual-Rect)
**What goes wrong:** Using DWMWA_EXTENDED_FRAME_BOUNDS coordinates directly as SetWindowPos arguments. Window moves to a slightly wrong position on each operation and drifts further with each press.
**Why it happens:** DWMWA_EXTENDED_FRAME_BOUNDS returns the visible window rect excluding invisible DWM resize borders (~7px on Windows 10/11 sides and bottom). SetWindowPos expects coordinates in the window's own space (GetWindowRect), which includes those borders. The two rects differ by the border widths.
**How to avoid:** Always use dual-rect: (1) GetWindowRect → winRect for SetWindowPos input coordinates, (2) DwmGetWindowAttribute → visRect for all user-visible math (snap, boundary check). Compute border offsets = `visRect.edge - winRect.edge` once per operation and apply in the final rect translation.
**Warning signs:** Window position slowly drifts rightward/downward across multiple move operations, or window snaps to unexpected positions that are 7px off grid lines.

### Pitfall 2: Snap Origin for Non-Primary Monitors
**What goes wrong:** Grid lines computed from origin 0 instead of rcWork.left/rcWork.top. On any monitor where rcWork.left > 0 (any non-primary, or primary with left-side taskbar), snap positions are offset by the monitor's virtual-screen origin.
**Why it happens:** rcWork coordinates are in virtual-screen space. Grid lines must be `work.left + N*step`, not `N*step`.
**How to avoid:** Always pass work.left/work.top as the origin parameter to GridCalculator.NearestGridLine and IsAligned. This is already enforced by the existing GridCalculator signature.
**Warning signs:** Windows on secondary monitors snap to wrong positions; gap between snapped position and nearest grid line equals the monitor's virtual-screen offset.

### Pitfall 3: Shrink Below Minimum Without Guard
**What goes wrong:** ComputeShrink returns a rect with width or height < stepX/stepY. SetWindowPos may clamp to ptMinTrackSize (OS minimum), producing an unpredictable size, or the window could reach zero/negative size.
**Why it happens:** Shrink math subtracts stepY from visH; if visH was already equal to stepY, result is 0 or negative.
**How to avoid:** Check `if (visW <= stepX) return win;` before any shrink calculation. Return the unchanged win rect as a no-op. Do the same for the Y axis.
**Warning signs:** Window collapses to a tiny strip on rapid shrink presses; or visible size jumps erratically at the minimum.

### Pitfall 4: SWP_NOACTIVATE Omitted
**What goes wrong:** Every grid step steals focus, causing the window to activate (flash taskbar button) on each press during a multi-step move.
**Why it happens:** SetWindowPos without SWP_NOACTIVATE activates the window as a side effect.
**How to avoid:** Always include SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER in every SetWindowPos call in WindowManagerService.
**Warning signs:** Taskbar button flashes or window title bar activates on each direction key press during move/resize.

### Pitfall 5: DPI Virtualization for Mixed-DPI Setup
**What goes wrong:** Moving a DPI-unaware target window from a PerMonitorV2-aware daemon produces position offset by a DPI scale factor (e.g., 1.5x or 2x error).
**Why it happens:** When focus.exe (PerMonitorV2) calls GetWindowRect on a DPI-unaware window, Windows virtualizes the returned coordinates for the caller's DPI context. SetWindowPos input must also be in the caller's DPI context — so the virtualized GetWindowRect IS the correct input. This usually works. But on mixed-DPI setups (e.g., focus.exe on 200% monitor, target window on 100% monitor), the coordinate scaling may surprise. STATE.md flags this as a MEDIUM confidence blocker.
**How to avoid:** Test with a DPI-unaware app (legacy Notepad) at Phase 11 start on a single monitor. On a mixed-DPI setup, test with secondary monitor. If coordinates are wrong by a scale factor, add `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` before the GetWindowRect/SetWindowPos pair, then restore.
**Warning signs:** Window moves by 1.5x or 2x the expected grid step, or moves in the wrong direction by a scale amount.

### Pitfall 6: SetWindowPos Min-Size Clamping vs. App's Own WM_GETMINMAXINFO
**What goes wrong:** The user presses shrink, the phase's grid-step minimum guard allows the call, but SetWindowPos returns success yet the window is larger than expected — the app enforced a larger minimum via WM_GETMINMAXINFO.
**Why it happens:** Windows sends WM_WINDOWPOSCHANGING / WM_GETMINMAXINFO to the target window before applying the move/resize. The app can reject or adjust the requested size.
**How to avoid:** This is acceptable behavior — the window enforces its own minimum, which is always larger than our minimum. The phase 11 guard (grid-step minimum) is a lower bound. Accept that some apps may enforce a larger minimum. Document this in verification.
**Warning signs:** Shrink operation appears to stop earlier than 1 grid cell — but this is the app's minimum, not a bug.

## Code Examples

Verified patterns from official sources and existing codebase:

### SetWindowPos Call Site (correct flags)
```csharp
// Source: Official SetWindowPos docs + existing OverlayWindow.cs pattern
// SWP_NOZORDER: preserve Z-order; SWP_NOACTIVATE: don't steal focus; SWP_NOOWNERZORDER: preserve owner Z
bool ok = PInvoke.SetWindowPos(
    fgHwnd,
    default,   // ignored due to SWP_NOZORDER
    newX, newY, newCx, newCy,
    SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
    SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
    SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
```

### Dual-Rect Border Offset Computation
```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect
// GetWindowRect includes invisible DWM borders; DWMWA_EXTENDED_FRAME_BOUNDS excludes them
// Border offsets are per-window and per-call — always recompute from the pair
RECT winRect = default;
PInvoke.GetWindowRect(fgHwnd, out winRect);

RECT visRect = default;
var visBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref visRect, 1));
PInvoke.DwmGetWindowAttribute(fgHwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, visBytes);

// Invisible border widths (typically ~7px on left/right/bottom, 0px on top in Windows 10/11)
int borderLeft   = visRect.left   - winRect.left;
int borderTop    = visRect.top    - winRect.top;
int borderRight  = winRect.right  - visRect.right;
int borderBottom = winRect.bottom - visRect.bottom;
```

### IsZoomed Guard
```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iszoomed
// IsZoomed available via CsWin32 after adding "IsZoomed" to NativeMethods.txt
if (PInvoke.IsZoomed(fgHwnd)) return; // silent no-op for maximized window
```

### GetWorkArea (rcWork from GetMonitorInfo)
```csharp
// Source: Existing OverlayOrchestrator.ClampToMonitor pattern
private static unsafe RECT GetWorkArea(HWND hwnd)
{
    var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = default;
    mi.cbSize = (uint)sizeof(MONITORINFO);
    if (PInvoke.GetMonitorInfo(hMon, ref mi))
        return mi.rcWork;   // rcWork excludes taskbar; rcMonitor includes it
    return default;
}
```

### Snap-First Move (X axis)
```csharp
// Source: Phase 10 Research snap-first pattern, adapted for move
// Only snap the movement axis; perpendicular axis unchanged
int sign = direction == "right" ? 1 : -1;
bool aligned = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX);
int newVisLeft = aligned
    ? vis.left + sign * stepX    // aligned → step immediately
    : GridCalculator.NearestGridLine(vis.left, work.left, stepX); // → snap only (next press steps)
// Clamp: visible window cannot go outside work area
newVisLeft = Math.Clamp(newVisLeft, work.left, work.right - visW);
```

### NativeMethods.txt Additions
```
GetWindowRect
IsZoomed
```
(Both are functions CsWin32 can generate; just add the function names. SWP_* constants are already generated by the existing SetWindowPos entry.)

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hard-code border offset (7px) | Compute per-call from GetWindowRect vs DWMWA_EXTENDED_FRAME_BOUNDS difference | Windows 10 | Border size is per-window and DPI-dependent; hard-coding is wrong |
| Check window rect before/after move for elevation | Check SetWindowPos return value | Vista+ UIPI | Return value is authoritative; pre-check is unreliable and adds latency |
| Single GetWindowRect call | Dual-rect (GetWindowRect + DwmExtendedFrameBounds) | Windows Vista DWM introduction | Invisible borders make single-rect positioning wrong |

**Deprecated/outdated:**
- Using GetWindowRect as the visible-bounds source: DWM invisible borders make it wrong for user-facing math on Windows 10+
- Checking token integrity for elevation before attempting operation: SetWindowPos return value is simpler and more reliable in practice

## Open Questions

1. **DPI virtualization on mixed-DPI setups (MEDIUM confidence)**
   - What we know: focus.exe is PerMonitorV2-aware. GetWindowRect is DPI-virtualized per caller. For a PerMonitorV2 caller, GetWindowRect returns physical pixel coordinates that are correct for SetWindowPos input.
   - What's unclear: Behavior when the target window uses a different DPI context (DPI-unaware, or on a different-DPI monitor). STATE.md flags this as needing empirical validation.
   - Recommendation: Test with legacy Notepad (DPI-unaware) as first manual test before wider UAT. If coordinates are wrong by a scale factor, add SetThreadDpiAwarenessContext around the GetWindowRect/SetWindowPos pair.

2. **SetWindowPos min-size clamping (MEDIUM confidence)**
   - What we know: STATE.md notes that SetWindowPos auto-clamps to WM_GETMINMAXINFO.ptMinTrackSize. The phase's grid-step minimum guard prevents sub-cell shrink requests.
   - What's unclear: Whether Calculator (suggested test app) or other apps enforce a minimum larger than 1 grid step, and how that interacts with the phase's guard.
   - Recommendation: Test shrink to minimum with Calculator. Accept that app-enforced minimums override the phase minimum — document as expected behavior.

3. **Architecture: New class vs. inline in OverlayOrchestrator (Claude's Discretion)**
   - What we know: OverlayOrchestrator already has all the patterns needed (GetForegroundWindow, DwmGetWindowAttribute, GetMonitorInfo, STA marshaling). Adding methods there keeps the code co-located.
   - Recommendation: Create a dedicated `WindowManagerService` static class. OverlayOrchestrator is already large; keeping window management separate follows the single-responsibility pattern established by NavigationService. Call it from `ManageWindowSta()` on the STA thread.

4. **Snap semantics for Grow/Shrink edges (Claude's Discretion)**
   - Locked decision: snap applies to the affected edge (growing right edge only snaps that edge).
   - Open: When "snap only" triggers on a grow/shrink, does it count as a visible operation? Yes — the edge snaps to grid, which IS a visible size change. This is the correct behavior per snap-first semantics.

## Validation Architecture

> `workflow.nyquist_validation` is not set in config.json — section skipped per instructions.

*(config.json has no `nyquist_validation` key under `workflow`; field defaults to absent/false)*

## Sources

### Primary (HIGH confidence)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos — SetWindowPos parameters, SWP_* flags, UIPI/Session 0 note; docs updated 2025-07-01
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect — GetWindowRect returns DPI-virtualized coordinates including invisible borders; DwmGetWindowAttribute required for visible bounds; docs updated 2025-07-01
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iszoomed — IsZoomed determines maximized state; docs updated 2025-07-01
- Existing codebase — OverlayWindow.cs (SetWindowPos with SWP_NOACTIVATE pattern), OverlayOrchestrator.cs (ClampToMonitor rcWork pattern, DwmGetWindowAttribute pattern, cross-thread STA dispatch), GridCalculator.cs (all snap math), WindowEnumerator.cs (DwmGetWindowAttribute for DWMWA_EXTENDED_FRAME_BOUNDS)

### Secondary (MEDIUM confidence)
- https://www.w3tutorials.net/blog/getwindowrect-returns-a-size-including-invisible-borders/ — Explains the GetWindowRect invisible-border problem on Windows 10 and the dual-rect correction pattern; consistent with official docs
- STATE.md Blockers section — DPI virtualization concern (MEDIUM) and SetWindowPos min-size clamping concern (MEDIUM) documented from prior project experience

### Tertiary (LOW confidence)
- Wikipedia: User Interface Privilege Isolation — background on UIPI; not used directly but confirms SetWindowPos is subject to UIPI (consistent with FocusActivator.cs comment about elevated windows)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; GetWindowRect and IsZoomed are new NativeMethods.txt entries but CsWin32 generates them trivially
- Dual-rect coordinate pattern: HIGH — confirmed by official GetWindowRect docs (DPI-virtual + invisible borders), existing codebase uses DwmExtendedFrameBounds for visible bounds already
- Architecture patterns (Move/Grow/Shrink): HIGH — direct composition of Phase 10 GridCalculator + SetWindowPos; all math is pure integer arithmetic
- UIPI guard: HIGH — SetWindowPos return value is confirmed by existing FocusActivator.cs comment and UIPI documentation
- DPI mixed-setup behavior: MEDIUM — empirical validation needed (flagged in STATE.md); single-monitor same-DPI case is HIGH confidence

**Research date:** 2026-03-02
**Valid until:** 2026-06-02 (stable Win32 APIs; CsWin32 0.3.269 pinned in csproj)
