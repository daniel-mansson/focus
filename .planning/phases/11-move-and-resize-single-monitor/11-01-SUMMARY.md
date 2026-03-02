---
phase: 11-move-and-resize-single-monitor
plan: "01"
subsystem: window-management
tags: [window-move, window-resize, grid-snap, win32, cswin32, dual-rect]
dependency_graph:
  requires:
    - 10-01-SUMMARY.md  # GridCalculator (GetGridStep, NearestGridLine, IsAligned, GetSnapTolerancePx)
    - 10-02-SUMMARY.md  # FocusConfig.GridFractionX/Y/SnapTolerancePercent
    - 10-03-SUMMARY.md  # WindowMode enum, OverlayOrchestrator.OnDirectionKeyDown wiring, _tabHeld
  provides:
    - WindowManagerService.MoveOrResize (public static, called via STA dispatch)
    - ComputeMove with snap-first grid-step and boundary clamp (MOVE-01/03)
    - ComputeGrow with snap-first on moving edge, work area boundary clamp (SIZE-01/04)
    - ComputeShrink with snap-first on moving edge, minimum-size guard (SIZE-02/03)
    - NativeMethods.txt GetWindowRect and IsZoomed bindings
  affects:
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs (mode dispatch wired)
tech_stack:
  added: []
  patterns:
    - dual-rect coordinate pattern (GetWindowRect for SetWindowPos input; DwmGetWindowAttribute for visible snap/boundary math)
    - snap-first: check IsAligned on moving edge; step if aligned, snap to nearest grid line if not
    - silent no-op guards: IsZoomed (maximized) and SetWindowPos return value (elevated)
    - STA dispatch via Control.Invoke with ObjectDisposedException/InvalidOperationException catch
key_files:
  created:
    - focus/Windows/Daemon/WindowManagerService.cs
  modified:
    - focus/NativeMethods.txt
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
decisions:
  - "Refuse (no-op) for maximized windows via IsZoomed — do NOT restore before moving (locked decision)"
  - "border offsets computed per-call from GetWindowRect vs DWMWA_EXTENDED_FRAME_BOUNDS difference — never hard-coded 7px"
  - "MoveOrResize takes verbose bool (not FocusConfig) — simplifies call site; FocusConfig.Load() called inside"
  - "ComputeGrow/Shrink snap applies to the moving EDGE, not window origin — consistent with Phase 10 snap-first semantics"
  - "Minimum size guard for shrink is a pre-check on visW/visH <= stepX/stepY returning win unchanged — no SetWindowPos call"
metrics:
  duration: "6 min"
  completed: "2026-03-02"
  tasks_completed: 2
  tasks_total: 2
  files_created: 1
  files_modified: 2
---

# Phase 11 Plan 01: WindowManagerService Move and Resize Summary

**One-liner:** Grid-snapped move/grow/shrink via dual-rect SetWindowPos with IsZoomed guard and snap-first GridCalculator math wired into OverlayOrchestrator STA dispatch.

## What Was Built

`WindowManagerService` is a new static class (`Focus.Windows.Daemon` namespace) that replaces the Phase 10 "not yet implemented" placeholder in `OverlayOrchestrator.OnDirectionKeyDown`. It handles all three non-navigate window modes (Move, Grow, Shrink) for single-monitor operation.

### Key Implementation Details

**Dual-rect pattern (anti-drift):**
- `GetWindowRect` provides coordinates in the window's own space — used as SetWindowPos input
- `DWMWA_EXTENDED_FRAME_BOUNDS` provides visible bounds (excludes ~7px invisible DWM borders) — used for all user-facing snap and boundary math
- Border offsets computed once per call from the pair; never hard-coded

**Guard sequence:**
1. `GetForegroundWindow` returns default → return silently
2. `IsZoomed` → return silently (no restore attempt — locked decision)
3. `GetWindowRect` returns false → return silently
4. `DwmGetWindowAttribute` fails → return silently
5. `SetWindowPos` returns false + verbose → log "SetWindowPos failed (elevated window?)"

**ComputeMove (MOVE-01/03):**
Snap-first on movement axis only; perpendicular axis unchanged. `IsAligned` checks window vis.left (X) or vis.top (Y) against work area origin with tolerance. If aligned: step by `sign * step`; if not: snap to `NearestGridLine`. Final clamp to work area keeps window in bounds.

**ComputeGrow (SIZE-01/04):**
Moves the edge in the pressed direction outward; opposite edge fixed. Snap-first on the moving edge (checked against work.left/work.top as origin). Moving edge clamped to work area boundary — if already at boundary, net result is no visible change.

**ComputeShrink (SIZE-02/03):**
Moves the edge in the pressed direction inward; opposite edge fixed. Pre-check: if `visW <= stepX` (or `visH <= stepY`), return `win` unchanged — hard no-op at minimum size. Snap-first on moving edge, then min-size clamp `Math.Max/Min(newEdge, oppositeEdge ± stepX/Y)` as defensive backstop.

**OverlayOrchestrator dispatch:**
The Phase 10 placeholder is replaced with:
```csharp
try
{
    _staDispatcher.Invoke(() => WindowManagerService.MoveOrResize(direction, mode, _verbose));
}
catch (ObjectDisposedException) { }
catch (InvalidOperationException) { }
return;
```
Follows the identical STA dispatch + catch pattern used by NavigateSta on the adjacent lines.

## Tasks

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Create WindowManagerService with Move operation and wire OverlayOrchestrator | ebc546d | NativeMethods.txt, WindowManagerService.cs, OverlayOrchestrator.cs |
| 2 | Implement ComputeGrow and ComputeShrink resize operations | 130e143 | WindowManagerService.cs |

## Requirements Fulfilled

| ID | Description | Status |
|----|-------------|--------|
| MOVE-01 | Move foreground window one grid step in any direction | Done |
| MOVE-02 | Consecutive grid steps while CAPS+TAB held | Done (handled by CapsLockMonitor repeat logic — no new code) |
| MOVE-03 | Window position clamped to monitor work area boundaries | Done |
| SIZE-01 | Grow a window edge outward by one grid step | Done |
| SIZE-02 | Shrink a window edge inward by one grid step | Done |
| SIZE-03 | Shrink stops at minimum window size (1 grid step) | Done |
| SIZE-04 | Grow stops at monitor work area boundary | Done |

## Verification Results

1. Zero C# compiler errors — confirmed by `grep "error CS" build output` returning empty
   (MSB3027 file-lock error is pre-existing: running daemon process has focus.exe binary locked)
2. `WindowManagerService.cs` exists with MoveOrResize (public), GetWorkArea (private), ComputeMove, ComputeGrow, ComputeShrink
3. `NativeMethods.txt` contains `GetWindowRect` and `IsZoomed` entries
4. `OverlayOrchestrator.OnDirectionKeyDown` dispatches non-Navigate modes to `WindowManagerService.MoveOrResize` via `_staDispatcher.Invoke`
5. Dual-rect pattern used: `GetWindowRect` for SetWindowPos input, never DWM bounds
6. SetWindowPos called with `SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER`
7. `IsZoomed` guard returns silently for maximized windows — no restore attempt
8. All grid math delegates to `GridCalculator` (23 call sites) — zero hand-rolled grid math
9. Boundary clamp uses `mi.rcWork` (not `rcMonitor`)
10. Shrink minimum-size guard uses `if (visW <= stepX) return win` pre-check

## Deviations from Plan

None — plan executed exactly as written.

The build file-lock (MSB3027) is not a deviation: it is a pre-existing operational constraint (running daemon holds focus.exe binary). The C# compilation phase succeeds with zero errors.

## Open Items / Deferred

- DPI virtualization on mixed-DPI setups: flagged in STATE.md as MEDIUM confidence blocker — empirical validation needed with legacy Notepad on a mixed-DPI machine before shipping
- SetWindowPos min-size clamping interaction with app's own WM_GETMINMAXINFO: documented in research as expected acceptable behavior — app-enforced minimums override the grid-step floor, which is correct

## Self-Check: PASSED

Files exist:
- focus/Windows/Daemon/WindowManagerService.cs: FOUND
- focus/NativeMethods.txt (GetWindowRect, IsZoomed): FOUND

Commits exist:
- ebc546d: FOUND (feat(11-01): create WindowManagerService...)
- 130e143: FOUND (feat(11-01): implement ComputeGrow and ComputeShrink...)
