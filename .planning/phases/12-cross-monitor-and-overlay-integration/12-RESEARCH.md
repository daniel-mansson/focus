# Phase 12: Cross-Monitor and Overlay Integration - Research

**Researched:** 2026-03-03
**Domain:** Win32 multi-monitor API, GDI+ layered window rendering, mode-indicator overlay design
**Confidence:** HIGH (all findings drawn from direct codebase inspection and Win32 API knowledge)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Current mode architecture:**
- Two window operation modes: Move (CAPS+LAlt+direction) and Resize (CAPS+LWin+direction)
- Shrink was removed. `ComputeGrow` handles both expand and contract by direction symmetrically
- `WindowMode` enum: `Navigate`, `Move`, `Grow` (Grow = resize)
- Navigate overlay state already works: `OnModeEntered()` hides navigate targets, `OnModeExited()` restores them
- `RefreshForegroundOverlayOnly()` already called after each move/resize to update foreground border

**Cross-monitor transition behavior:**
- Immediate jump on next keypress when window is at monitor edge — no "stick at boundary" checkpoint
- Movement axis snaps to first grid cell from the target monitor edge; perpendicular axis preserves current pixel position (no re-snap on perpendicular)
- If window is larger than target monitor work area, clamp to target work area boundaries (do not resize)
- Silent no-op when no adjacent monitor exists in the pressed direction

**Mode indicator appearance:**
- Mode-specific colors: move and resize each use a distinct color
- Prominent/high-opacity arrows — primary feedback mechanism
- OVRL-04: all transitions are instant, no animation

**Arrow layout per mode:**
- Move mode (CAPS+LAlt): 4 directional arrows as compass/cross at window center
- Resize mode (CAPS+LWin): left/right arrow at right edge center, up/down arrow at top edge center
- Arrows appear immediately on modifier hold (before any direction key)
- After each operation, arrows reposition instantly to track window's new position
- Navigate target outlines hide when mode modifier held (existing `OnModeEntered` behavior)

**Monitor adjacency and DPI:**
- Handle mixed DPI/scaling correctly — grid step recalculates using target monitor dimensions
- Primary use case is 2 monitors, but adjacency detection must work for any reasonable count
- Build DPI-aware coordinate translation even though user currently has same-DPI monitors

### Claude's Discretion

- Arrow rendering technique (GDI+ triangles, Unicode glyphs, or hybrid)
- Mode color palette — 2 distinct, accessible colors
- Arrow size relative to window dimensions
- Adjacency algorithm specifics — overlapping range vs nearest edge vs window-position-aware
- Multi-monitor target selection logic

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| XMON-01 | Moving at monitor edge transitions window to adjacent monitor | Cross-monitor detection in `ComputeMove`, adjacency via `EnumDisplayMonitors` + `GetMonitorInfo`, target snap to first grid cell from entry edge |
| XMON-02 | Grid step recalculated for target monitor's dimensions | `GridCalculator.GetGridStep` already parameterized — just pass target monitor's `rcWork` dimensions |
| OVRL-01 | Move mode shows directional arrows in window center | New arrow renderer added to `OverlayOrchestrator.OnModeEntered` / `RefreshForegroundOverlayOnly` with mode tracking |
| OVRL-02 | Grow mode shows outward-pointing arrows at edge centers | Same renderer, different geometry: arrows at right-edge center (horizontal axis) and top-edge center (vertical axis) |
| OVRL-03 | Shrink mode shows inward-pointing arrows at edge centers | CONTEXT: Shrink removed; `Grow` handles both expand and contract by direction. This requirement maps to resize mode showing inward arrows when direction indicates contraction — but the mode itself is always `Grow`. Arrow direction can be inferred from direction key if needed, or arrows always show the resize axis indicators (up/down at top, left/right at right) |
| OVRL-04 | Overlay transitions are instant (no animation) | Already enforced — `HideAll()` + immediate repaint via `UpdateLayeredWindow` is synchronous; no timer or fade logic needed |
</phase_requirements>

---

## Summary

Phase 12 has two independent workstreams that share no code: (1) cross-monitor window transition in `WindowManagerService`, and (2) mode-indicator arrows in the overlay system. Both workstreams have clear integration points already identified in `12-CONTEXT.md`.

The cross-monitor workstream requires adding adjacency detection to `MonitorHelper` and extending `ComputeMove` to detect when the clamped position equals the work-area boundary and then attempt a cross-monitor jump. The entire grid math infrastructure (`GridCalculator`) is already parameterized by work area dimensions, so XMON-02 is essentially free once XMON-01 determines the target monitor's `rcWork`.

The overlay workstream requires adding a new rendering path in `OverlayOrchestrator` that activates when mode is `Move` or `Grow`. This path draws filled GDI+ triangles (arrows) on the foreground overlay window or new dedicated overlay windows, sized relative to the window being operated on. The arrows must reposition after each operation via the existing `RefreshForegroundOverlayOnly` call chain. The `_capsLockHeld` guard already prevents stale redraws.

**Primary recommendation:** Implement cross-monitor logic as a contained extension of `ComputeMove` with a new `MonitorHelper.FindAdjacentMonitor()` helper, and implement arrows as a new `ArrowRenderer` that paints directly onto the existing foreground `OverlayWindow` after the border, or onto a dedicated new `OverlayWindow` per mode indicator position.

---

## Standard Stack

### Core (already present — no new dependencies)

| Component | Version | Purpose | Why Standard |
|-----------|---------|---------|--------------|
| CsWin32 (PInvoke) | Project version | `EnumDisplayMonitors`, `GetMonitorInfo`, `MonitorFromWindow`, `MonitorFromPoint` | Already used throughout; provides type-safe Win32 P/Invoke |
| GDI+ via DIB pixel writes | N/A (Win32) | Arrow rendering (filled triangles) via pixel buffer | Exact same pattern as `BorderRenderer` — no new dependency |
| `Windows.Win32.Foundation.RECT` | CsWin32 | Monitor coordinate rectangles | Already used for all window rects |
| `Windows.Win32.Graphics.Gdi.MONITORINFO` | CsWin32 | `rcWork`, `rcMonitor` per monitor | Already used in `WindowManagerService.GetWorkArea` |

### No New NuGet Packages Required

Both workstreams extend existing Win32 API usage. No additional libraries are needed.

---

## Architecture Patterns

### Pattern 1: Cross-Monitor Adjacency Detection

**What:** `MonitorHelper.FindAdjacentMonitor(HMONITOR current, string direction)` enumerates all monitors via `EnumDisplayMonitors`, collects their `MONITORINFO`, and finds the monitor whose edge is adjacent to `current` in the given direction.

**Adjacency algorithm (recommended — overlapping-range approach):**

For direction "right": find monitors where `candidate.rcMonitor.left == current.rcMonitor.right` (or within a small tolerance for non-flush arrangements) AND `candidate.rcMonitor` overlaps vertically with `current.rcMonitor`.

For direction "left": `candidate.rcMonitor.right == current.rcMonitor.left` with vertical overlap.

For direction "up": `candidate.rcMonitor.bottom == current.rcMonitor.top` with horizontal overlap.

For direction "down": `candidate.rcMonitor.top == current.rcMonitor.bottom` with horizontal overlap.

If multiple candidates satisfy adjacency (rare — triple monitor arrangements), pick the one whose center has the most overlap with the window's perpendicular extent.

**Tolerance:** Use a small pixel tolerance (e.g., ±2 pixels) for non-flush multi-monitor arrangements where Windows reports edges that don't align exactly to 0.

```csharp
// Focus.Windows.MonitorHelper — new method
public static unsafe (HMONITOR Handle, RECT Work, RECT Monitor)? FindAdjacentMonitor(
    HMONITOR current, RECT currentMonitorRect, string direction)
{
    // Collect all monitors with their rects
    var candidates = new List<(HMONITOR h, RECT rcMon, RECT rcWork)>();
    MONITORENUMPROC cb = (hMon, _, lprcMonitor, _) =>
    {
        MONITORINFO mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (PInvoke.GetMonitorInfo(hMon, ref mi) && (nint)(IntPtr)hMon != (nint)(IntPtr)current)
            candidates.Add((hMon, mi.rcMonitor, mi.rcWork));
        return true;
    };
    PInvoke.EnumDisplayMonitors(default, (RECT?)null, cb, default);

    const int EdgeTol = 2;
    (HMONITOR h, RECT rcMon, RECT rcWork)? best = null;
    int bestOverlap = 0;

    foreach (var (h, rcMon, rcWork) in candidates)
    {
        bool adjacent = direction switch
        {
            "right" => Math.Abs(rcMon.left - currentMonitorRect.right) <= EdgeTol,
            "left"  => Math.Abs(rcMon.right - currentMonitorRect.left) <= EdgeTol,
            "down"  => Math.Abs(rcMon.top - currentMonitorRect.bottom) <= EdgeTol,
            "up"    => Math.Abs(rcMon.bottom - currentMonitorRect.top) <= EdgeTol,
            _       => false
        };
        if (!adjacent) continue;

        // Compute perpendicular overlap
        int overlap = direction is "right" or "left"
            ? Math.Max(0, Math.Min(rcMon.bottom, currentMonitorRect.bottom)
                        - Math.Max(rcMon.top,    currentMonitorRect.top))
            : Math.Max(0, Math.Min(rcMon.right, currentMonitorRect.right)
                        - Math.Max(rcMon.left,   currentMonitorRect.left));

        if (overlap > bestOverlap)
        {
            bestOverlap = overlap;
            best = (h, rcMon, rcWork);
        }
    }

    return best is null ? null : (best.Value.h, best.Value.rcWork, best.Value.rcMon);
}
```

### Pattern 2: Cross-Monitor Transition in ComputeMove

**What:** After normal clamping, detect if the clamped position equals the work-area boundary (i.e., the window is at the edge). If so, look up the adjacent monitor and compute the transition position.

**Integration point:** `WindowManagerService.MoveOrResize` calls `ComputeMove`, which currently clamps with `Math.Clamp`. The cross-monitor check fires when the clamped result equals the boundary.

**Return value strategy:** `ComputeMove` cannot perform the transition itself because it needs the target monitor's work area to recompute grid step. The cleanest approach is to refactor `MoveOrResize` to handle this:

Option A (preferred): `MoveOrResize` detects at boundary, calls `FindAdjacentMonitor`, and if found, recomputes grid step for the target and calls a separate `ComputeCrossMonitorMove` that snaps to the first cell from the entry edge.

Option B: `ComputeMove` returns a `MoveResult` struct with a flag indicating cross-monitor is needed — caller decides. More testable but adds a record type.

**Recommendation:** Option A — keep the change contained within `MoveOrResize`. The existing static method is already private, so the refactor is self-contained.

```csharp
// In WindowManagerService.MoveOrResize, after computing newWinRect:
// Check if the result is at the boundary (cross-monitor candidate)
bool atRightBoundary  = direction == "right" && newVisLeft + visW >= workArea.right - stepX / 2;
// (similar for other directions)
// If at boundary: call FindAdjacentMonitor, recompute step, snap to entry edge
```

**Snap-to-first-cell on entry:**

When crossing to the target monitor:
- On the movement axis: snap to `targetWork.left + stepX` (for entering from left edge), i.e., first full grid cell from the entry edge
- On the perpendicular axis: preserve the current pixel position (clamp to target work area if out of bounds)

**Important:** The window's visible left/top in the perpendicular axis carries over unchanged. Only clamp it if it falls outside the target monitor's `rcWork` bounds.

### Pattern 3: Mode-Aware Overlay via OnModeEntered/RefreshForegroundOverlayOnly

**What:** `OverlayOrchestrator` needs to track the current `WindowMode` so it can draw the correct arrows. Currently `OnModeEntered` receives no mode parameter.

**Required change:** `OnModeEntered(WindowMode mode)` — pass mode from `CapsLockMonitor`, store as `_currentMode` field in `OverlayOrchestrator`, then use it in both `OnModeEntered` (initial draw) and `RefreshForegroundOverlayOnly` (after each operation).

```csharp
// OverlayOrchestrator field
private WindowMode _currentMode = WindowMode.Navigate;

// Updated signature
public void OnModeEntered(WindowMode mode) { ... }

// In RefreshForegroundOverlayOnly:
// After drawing the border, if _currentMode != Navigate, draw arrows
if (_currentMode == WindowMode.Move || _currentMode == WindowMode.Grow)
    DrawModeArrows(fgBounds, _currentMode);
```

**CapsLockMonitor** currently calls `_onModeEntered?.Invoke()` with no arguments. The callback signature must become `Action<WindowMode>?` and the mode is passed from context (`_lAltHeld ? WindowMode.Move : WindowMode.Grow`).

**OverlayOrchestrator.OnModeExited** must clear `_currentMode = WindowMode.Navigate`.

### Pattern 4: Arrow Rendering via Pixel Buffer (GDI+ DIB approach)

**What:** Extend `BorderRenderer`'s pixel-write pattern to render filled triangle arrows. The arrow renderer reuses the same `CreateDIBSection` + `UpdateLayeredWindow` pipeline already proven to work.

**Rendering options:**

| Technique | Pros | Cons | Verdict |
|-----------|------|------|---------|
| Filled triangles via pixel loop | Same pattern as BorderRenderer, no new dependencies, crisp at any size | Some code to write | Recommended |
| Unicode glyphs (GDI DrawText) | Simple code | Fuzzy at small sizes, font dependency, encoding in DIB is tricky | Avoid |
| GDI FillPolygon | Standard | Requires GDI Polygon call, harder to premultiply correctly | Acceptable fallback |

**Filled triangle pixel loop (recommended):**

For a triangle defined by 3 vertices, iterate over bounding box pixels and test if each point is inside the triangle using the sign of cross-products. Apply premultiplied alpha same as `BorderRenderer`.

**Arrow geometry — Move mode (4 compass arrows at window center):**

Each arrow is a filled isoceles triangle pointing outward. Center point is `(fgBounds.left + fgBounds.right) / 2, (fgBounds.top + fgBounds.bottom) / 2`. Arrow size: ~20px base, ~30px height (or relative to min(width, height) / 8, clamped 16..40px).

**Arrow geometry — Resize mode (resize axis indicators):**

Right edge center: left/right double arrows (or two triangles point-to-point) centered at `(fgBounds.right, (fgBounds.top + fgBounds.bottom) / 2)`.

Top edge center: up/down arrows centered at `((fgBounds.left + fgBounds.right) / 2, fgBounds.top)`.

**Implementation choice:** Use a new `ArrowRenderer` static class (following the same pattern as `BorderRenderer`). Do NOT implement `IOverlayRenderer` — arrows are not directional navigation indicators, they are mode indicators. They paint directly onto the foreground `OverlayWindow` HWND (same window as the border) or a new dedicated `OverlayWindow`.

**Recommended approach:** Use the existing foreground `OverlayWindow` HWND. Paint the border + arrows in a single `UpdateLayeredWindow` call by extending `PaintFullBorder` or creating a new method `PaintBorderWithArrows`. This eliminates Z-order issues between border and arrows.

**Alternative (separate overlay windows):** Add 4 arrow overlay windows to `OverlayManager`. More code but cleaner separation. The existing `OverlayWindow` class makes adding new windows trivial. Consider this if painting everything in one DIB proves complex for the resize-mode layout (right edge + top edge at different positions means the DIB must cover both, which is the entire window anyway — so a single DIB works fine).

### Pattern 5: Mode Color Assignment

Two distinct colors needed. Recommendations based on accessible contrast on dark/light backgrounds:

| Mode | Recommended Color | ARGB | Rationale |
|------|------------------|------|-----------|
| Move | Bright amber/orange | `0xE0FF9900` | High visibility, neutral (not red=danger) |
| Resize (Grow/Shrink) | Bright cyan/teal | `0xE000CCBB` | Immediately distinct from amber, familiar "resize" association |

These are at ~88% opacity (`0xE0`) — same as `ForegroundBorderColor`. The border itself should adopt the mode color when a mode is active, replacing the neutral white `0xE0FFFFFF`. This gives the user immediate color feedback that they're in Move vs Resize mode even before looking at arrows.

### Anti-Patterns to Avoid

- **Calling SetWindowPos to cross monitor without first computing target position:** Do all math before any Win32 call. Compute the full target rect, then call SetWindowPos once.
- **Mixing rcMonitor and rcWork coordinates:** All window placement math uses rcWork. Adjacency detection uses rcMonitor edges (physical screen edges, which include taskbar area). Keep these distinct.
- **Passing the wrong monitor to GridCalculator after cross-monitor transition:** After the window moves to the target monitor, the next keypress must use the target monitor's work area — not the origin monitor's. `MoveOrResize` calls `GetWorkArea(fgHwnd)` at the start of each operation, so this is naturally correct IF the window position is already updated before the next call.
- **Forgetting to update `_currentMode` in OverlayOrchestrator.OnModeExited:** Leaving stale mode causes arrows to persist after mode exit.
- **Rendering arrows at absolute screen coordinates:** Arrow positions are relative to the window's `DWMWA_EXTENDED_FRAME_BOUNDS` (the visible rect), not `GetWindowRect`. Use the same DWM bounds that `RefreshForegroundOverlayOnly` already retrieves.
- **Using a separate OverlayWindow per arrow:** Four new overlay windows for 4 arrows creates Z-order complexity. One overlay window covering the window bounds can paint all arrows in one `UpdateLayeredWindow` call.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Monitor enumeration | Custom EnumDisplayMonitors wrapper | Extend `MonitorHelper.EnumerateMonitors()` | Already exists with correct GC-safe delegate pattern |
| Per-monitor work area | Custom GetMonitorInfo wrapper | `WindowManagerService.GetWorkArea(hwnd)` extended to accept HMONITOR | Already proven; just needs an HMONITOR overload |
| Grid step computation | New grid math | `GridCalculator.GetGridStep(targetWork.right - targetWork.left, ...)` | Already parameterized by dimensions |
| Arrow hit-test | Custom polygon library | Inline point-in-triangle (3 cross-products) | 10 lines, no dependency needed |
| Alpha blending | System.Drawing / WinForms | Direct DIB pixel writes (premultiplied) | Matches existing BorderRenderer pattern exactly |

**Key insight:** The pixel-write pattern in `BorderRenderer` is already the correct approach for any GDI overlay rendering in this codebase. Do not introduce GDI+ `Graphics` objects or WPF/XAML — they bring STA threading complications and compositing overhead not present in the direct UpdateLayeredWindow path.

---

## Common Pitfalls

### Pitfall 1: DPI Virtualization on Cross-Monitor Move

**What goes wrong:** When the daemon process is DPI-aware (PerMonitorV2) but the target window is DPI-unaware, `SetWindowPos` coordinates may be interpreted in the target window's DPI context, not the daemon's. A window moved from a 100% DPI monitor to a 150% DPI monitor might land at a scaled position.

**Why it happens:** Windows applies DPI virtualization to `SetWindowPos` calls from DPI-aware processes targeting DPI-unaware windows in some configurations.

**How to avoid:** Test with a DPI-unaware app (Notepad) on a mixed-DPI setup. If positions are off by a scaling factor, apply `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` before the `SetWindowPos` call, then restore. This was flagged as a MEDIUM-confidence concern in `STATE.md`.

**Warning signs:** Window lands at position multiplied by scale factor (1.5x off = DPI scaling issue). Only manifests on mixed-DPI setups.

### Pitfall 2: Monitor Edge Coordinates Are Physical Screen Coordinates

**What goes wrong:** Virtual screen coordinates (used by Win32 for multi-monitor) include negative values for monitors to the left/above the primary. `rcMonitor.left` for a secondary monitor to the left of the primary will be negative.

**Why it happens:** Win32 uses a unified virtual screen coordinate system. The primary monitor's top-left is typically (0, 0). Secondary monitors can be at negative coordinates.

**How to avoid:** The existing code already handles this correctly — `GetWorkArea` returns `rcWork` which is in virtual screen coordinates, and `GridCalculator` takes `work.left` as origin. The cross-monitor extension must pass the TARGET monitor's `rcWork.left`/`rcWork.top` as origin to `GridCalculator`, not 0.

**Warning signs:** Window snaps to wrong grid position on secondary monitor, especially if that monitor is to the left of primary.

### Pitfall 3: Arrow DIB Must Cover Entire Window Bounds

**What goes wrong:** If the arrow overlay window is sized to only cover the arrow area (e.g., a small rect at window center), the overlay window's coordinate system doesn't match the window's coordinate system, making arrow positioning calculations complex.

**Why it happens:** Temptation to minimize overlay window size for performance.

**How to avoid:** Size the arrow overlay window (or the combined border+arrow window) to the full `DWMWA_EXTENDED_FRAME_BOUNDS` of the target window. Then arrow center positions within the DIB are simply `(width/2, height/2)` for the move-mode cross, and `(width, height/2)` and `(width/2, 0)` for resize-mode edge arrows. This matches the existing foreground overlay window pattern exactly.

**Warning signs:** Arrows appear at wrong position relative to the window; arrow overlay doesn't move with the window after an operation.

### Pitfall 4: OnModeEntered Called Without Mode Parameter

**What goes wrong:** Current `CapsLockMonitor._onModeEntered` is `Action?` (no mode parameter). If the signature is not updated, `OverlayOrchestrator` cannot know which mode was entered and cannot draw the correct arrows.

**Why it happens:** The original `OnModeEntered` was designed only to hide navigate targets — it didn't need the mode. Phase 12 requires mode-specific behavior.

**How to avoid:** Change `_onModeEntered` callback type from `Action?` to `Action<WindowMode>?` in both `CapsLockMonitor` and its callsite in `DaemonCommand`/wherever `OverlayOrchestrator.OnModeEntered` is wired. Pass mode from the `_lAltHeld`/`_lWinHeld` context in `CapsLockMonitor.HandleDirectionKeyEvent`.

**Warning signs:** Same arrows appear in both Move and Resize mode. Compiler error if signature is changed at declaration but not at callsite.

### Pitfall 5: _capsLockHeld Guard Timing for Arrow Redraws

**What goes wrong:** After a cross-monitor transition, `RefreshForegroundOverlayOnly` is called but `_capsLockHeld` is still true (correct), so arrows are redrawn. However, if CAPS is released between the `SetWindowPos` call and the STA dispatch completing, the guard prevents the arrow redraw — which is the correct behavior, since `OnReleasedSta` calls `HideAll()`. No bug here, but the guard must remain.

**How to avoid:** Do NOT remove the `_capsLockHeld` guard in `RefreshForegroundOverlayOnly`. It was added specifically to prevent stale flashes after release (logged in `STATE.md` as the `_capsLockHeld guard on RefreshForegroundOverlayOnly` decision).

### Pitfall 6: OVRL-03 Requires Clarification (Shrink Mode Was Removed)

**What goes wrong:** REQUIREMENTS.md lists OVRL-03 as "Shrink mode shows inward-pointing arrows" but the CONTEXT.md states shrink was removed — `Grow` handles both expand and contract.

**Resolution:** OVRL-03 maps to the resize mode (Grow mode in code) when the direction implies contraction. Since both expand and contract share `WindowMode.Grow`, the overlay cannot distinguish them at mode-enter time — only when a direction key is pressed. The simplest correct implementation is: resize mode shows the resize-axis indicator arrows (left/right at right edge, up/down at top edge) regardless of whether the next action will expand or contract. The axis indicators satisfy both OVRL-02 and OVRL-03 with one arrow layout.

---

## Code Examples

### Cross-Monitor Move — Integration in MoveOrResize

```csharp
// In WindowManagerService.MoveOrResize, after ComputeMove returns newWinRect:
// Detect if window is at boundary (cross-monitor candidate for Move mode only)
if (mode == WindowMode.Move)
{
    var targetMonitor = TryGetCrossMonitorTarget(fgHwnd, direction, visRect, workArea);
    if (targetMonitor.HasValue)
    {
        var (targetWork, targetMonRect) = targetMonitor.Value;
        int targetWidth  = targetWork.right  - targetWork.left;
        int targetHeight = targetWork.bottom - targetWork.top;
        var (tStepX, tStepY) = GridCalculator.GetGridStep(
            targetWidth, targetHeight, config.GridFractionX, config.GridFractionY);

        newWinRect = ComputeCrossMonitorPosition(
            direction, visRect, win, targetWork, tStepX, tStepY,
            borderL, borderT, borderR, borderB);
        // Skip the normal clamped position, use cross-monitor result instead
        // ... SetWindowPos with newWinRect
        return; // early return after cross-monitor SetWindowPos
    }
}
```

### FindAdjacentMonitor skeleton

```csharp
// WindowManagerService — private helper
private static unsafe (RECT work, RECT monitor)? TryGetCrossMonitorTarget(
    HWND hwnd, string direction, RECT vis, RECT currentWork)
{
    var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
    MONITORINFO currentMi = default;
    currentMi.cbSize = (uint)sizeof(MONITORINFO);
    if (!PInvoke.GetMonitorInfo(hMon, ref currentMi)) return null;

    // Is the window actually at the boundary?
    int visW = vis.right - vis.left;
    int visH = vis.bottom - vis.top;
    bool atBoundary = direction switch
    {
        "right" => vis.right  >= currentWork.right,
        "left"  => vis.left   <= currentWork.left,
        "down"  => vis.bottom >= currentWork.bottom,
        "up"    => vis.top    <= currentWork.top,
        _       => false
    };
    if (!atBoundary) return null;

    return MonitorHelper.FindAdjacentMonitor(hMon, currentMi.rcMonitor, direction);
}
```

### ComputeCrossMonitorPosition skeleton

```csharp
private static RECT ComputeCrossMonitorPosition(
    string direction, RECT vis, RECT win, RECT targetWork,
    int tStepX, int tStepY,
    int borderL, int borderT, int borderR, int borderB)
{
    int visW = vis.right - vis.left;
    int visH = vis.bottom - vis.top;

    // Clamp window size to target work area (do not resize)
    int clampedW = Math.Min(visW, targetWork.right - targetWork.left);
    int clampedH = Math.Min(visH, targetWork.bottom - targetWork.top);

    int newVisLeft, newVisTop;

    switch (direction)
    {
        case "right":
            // Enter from left: snap to first grid cell from left edge
            newVisLeft = targetWork.left + tStepX;
            // Preserve perpendicular (vertical) position, clamped to target
            newVisTop  = Math.Clamp(vis.top, targetWork.top, targetWork.bottom - clampedH);
            break;
        case "left":
            // Enter from right: snap so right vis edge is at work.right - stepX
            newVisLeft = targetWork.right - tStepX - clampedW;
            newVisTop  = Math.Clamp(vis.top, targetWork.top, targetWork.bottom - clampedH);
            break;
        case "down":
            // Enter from top: snap to first grid cell from top
            newVisTop  = targetWork.top + tStepY;
            newVisLeft = Math.Clamp(vis.left, targetWork.left, targetWork.right - clampedW);
            break;
        case "up":
            // Enter from bottom: snap so bottom vis edge is at work.bottom - stepY
            newVisTop  = targetWork.bottom - tStepY - clampedH;
            newVisLeft = Math.Clamp(vis.left, targetWork.left, targetWork.right - clampedW);
            break;
        default:
            return new RECT { left = win.left, top = win.top, right = win.right, bottom = win.bottom };
    }

    return new RECT
    {
        left   = newVisLeft - borderL,
        top    = newVisTop  - borderT,
        right  = newVisLeft + clampedW + borderR,
        bottom = newVisTop  + clampedH + borderB
    };
}
```

### Mode Arrow Overlay — OverlayOrchestrator changes

```csharp
// New field
private WindowMode _currentMode = WindowMode.Navigate;

// Updated OnModeEntered signature
public void OnModeEntered(WindowMode mode)
{
    _currentMode = mode;
    if (_shutdownRequested) return;
    try
    {
        _staDispatcher.Invoke(() =>
        {
            if (_capsLockHeld)
                RefreshForegroundOverlayOnly(); // now draws arrows too
        });
    }
    catch (ObjectDisposedException) { }
    catch (InvalidOperationException) { }
}

// OnModeExited — clear mode
public void OnModeExited()
{
    _currentMode = WindowMode.Navigate;
    // existing body: invoke ShowOverlaysForCurrentForeground
}

// RefreshForegroundOverlayOnly extension
private unsafe void RefreshForegroundOverlayOnly()
{
    _overlayManager.HideAll();
    var fgHwnd = PInvoke.GetForegroundWindow();
    if (fgHwnd == default) return;

    RECT fgBounds = default;
    // ... DWM fetch as existing ...

    if (hr.Succeeded && (fgBounds.right - fgBounds.left) > 0)
    {
        uint borderColor = _currentMode switch
        {
            WindowMode.Move => MoveModeColor,   // 0xE0FF9900 amber
            WindowMode.Grow => GrowModeColor,   // 0xE000CCBB cyan
            _               => ForegroundBorderColor // 0xE0FFFFFF neutral white
        };
        _overlayManager.ShowForegroundOverlay(fgBounds, borderColor);

        if (_currentMode == WindowMode.Move)
            _overlayManager.ShowMoveArrows(fgBounds);
        else if (_currentMode == WindowMode.Grow)
            _overlayManager.ShowResizeArrows(fgBounds);
    }
}
```

### Point-in-Triangle helper for arrow rendering

```csharp
// Used by arrow renderer to fill triangle pixels
private static bool IsInsideTriangle(
    int px, int py,
    int x0, int y0, int x1, int y1, int x2, int y2)
{
    int d1 = Sign(px, py, x0, y0, x1, y1);
    int d2 = Sign(px, py, x1, y1, x2, y2);
    int d3 = Sign(px, py, x2, y2, x0, y0);
    bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
    bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
    return !(hasNeg && hasPos);
}

private static int Sign(int px, int py, int x1, int y1, int x2, int y2)
    => (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|-----------------|--------------|--------|
| `ComputeMove` clamps to work area boundary | Phase 12: detect at boundary, jump to adjacent monitor | Phase 12 | Cross-monitor requires replacing clamp-only with jump logic |
| `OnModeEntered()` only hides navigate targets | Phase 12: draws mode-specific arrows too | Phase 12 | Mode indicator becomes visual, not just "less info" |
| `RefreshForegroundOverlayOnly` draws border only | Phase 12: draws border + arrows based on `_currentMode` | Phase 12 | Two rendering paths share one overlay window |
| `Action?` callback for OnModeEntered | Phase 12: `Action<WindowMode>?` | Phase 12 | CapsLockMonitor must pass mode to orchestrator |

**Deprecated/outdated in this phase:**
- REQUIREMENTS.md MODE-03 and SIZE-02 (Shrink was removed in Phase 11 quick task). OVRL-03 as stated in requirements is outdated — it maps to Grow mode's contraction direction in current code.

---

## Open Questions

1. **DPI virtualization empirical test**
   - What we know: DPI-unaware windows under PerMonitorV2 daemon may receive scaled coordinates from SetWindowPos (MEDIUM confidence from STATE.md)
   - What's unclear: Whether the daemon's DPI awareness context is already set correctly; whether the project's `app.manifest` declares PerMonitorV2
   - Recommendation: Check the manifest before writing cross-monitor code. If the daemon is DPI-aware, test cross-monitor with a legacy app on different-DPI monitors. Add `SetThreadDpiAwarenessContext` override if positions are wrong.

2. **Arrow size calibration**
   - What we know: Arrows should be "prominent enough to serve as the primary mode indicator"
   - What's unclear: Exact pixel size that reads well across window sizes from 200px to full-screen
   - Recommendation: Use `Math.Clamp(Math.Min(windowWidth, windowHeight) / 8, 16, 48)` as arrow height. Planner should treat this as implementation detail, not a blocker.

3. **Single DIB vs separate OverlayWindow for arrows**
   - What we know: Painting border + arrows in one DIB is possible; separate window avoids sizing/coordinate complexity
   - What's unclear: Whether combining border + arrows in one PaintFullBorderWithArrows method is cleaner than two separate overlay windows
   - Recommendation: Start with one combined DIB (extend PaintFullBorder). Separate window only if it turns out the arrow positions relative to the full border DIB are awkward.

4. **OVRL-03 clarification for requirements traceability**
   - What we know: "Shrink mode" no longer exists; Grow mode handles both expand and contract
   - What's unclear: Whether the planner should mark OVRL-03 as satisfied by the resize-mode arrow layout, or whether to add a direction-key-aware arrow flip
   - Recommendation: The resize-mode arrow layout (axis indicators, not directional arrows) satisfies both OVRL-02 and OVRL-03. Mark both as satisfied by the Grow mode arrow implementation. Do not add direction-key-aware logic — it adds complexity with no visible benefit since the axis indicator already tells the user what happens.

---

## Sources

### Primary (HIGH confidence)

- Direct codebase inspection: `focus/Windows/Daemon/WindowManagerService.cs` — complete understanding of MoveOrResize, ComputeMove, ComputeGrow, GetWorkArea
- Direct codebase inspection: `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` — complete understanding of mode lifecycle, RefreshForegroundOverlayOnly, OnModeEntered/Exited
- Direct codebase inspection: `focus/Windows/Daemon/Overlay/BorderRenderer.cs` — pixel-write DIB pattern for arrow rendering
- Direct codebase inspection: `focus/Windows/MonitorHelper.cs` — EnumerateMonitors, GetMonitorIndex (no adjacency yet)
- Direct codebase inspection: `focus/Windows/GridCalculator.cs` — GetGridStep parameterization
- Direct codebase inspection: `focus/Windows/Daemon/CapsLockMonitor.cs` — mode callback signatures
- Direct codebase inspection: `.planning/phases/12-cross-monitor-and-overlay-integration/12-CONTEXT.md` — all locked decisions
- Direct codebase inspection: `.planning/STATE.md` — accumulated key decisions and pitfalls

### Secondary (MEDIUM confidence)

- Win32 multi-monitor API: `EnumDisplayMonitors`, `GetMonitorInfo`, `MonitorFromWindow` — standard Windows APIs, behavior well-established
- Point-in-triangle algorithm: standard computational geometry, widely verified

---

## Metadata

**Confidence breakdown:**
- Cross-monitor architecture: HIGH — integration points are clear from code; Win32 APIs are well-understood
- Arrow rendering approach: HIGH — pixel-write DIB pattern is already proven in BorderRenderer; only geometry differs
- Mode plumbing (OnModeEntered signature change): HIGH — straightforward refactor, all call sites visible
- DPI behavior on mixed setups: MEDIUM — empirical validation needed (noted in STATE.md)
- Arrow size calibration: LOW — aesthetic judgment, needs runtime feedback

**Research date:** 2026-03-03
**Valid until:** 2026-04-03 (stable domain — Win32 API, internal codebase)
