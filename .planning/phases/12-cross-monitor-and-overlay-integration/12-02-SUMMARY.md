---
phase: 12-cross-monitor-and-overlay-integration
plan: "02"
subsystem: ui
tags: [overlay, arrows, dib, layered-window, win32, gdi, window-mode]

# Dependency graph
requires:
  - phase: 11-move-and-resize-single-monitor
    provides: OverlayOrchestrator with RefreshForegroundOverlayOnly, WindowMode enum, CapsLockMonitor mode detection
provides:
  - ArrowRenderer static class with PaintMoveArrows and PaintResizeArrows via DIB pixel-write pipeline
  - Mode-colored borders (amber for Move, cyan for Grow) in OverlayOrchestrator
  - _modeArrowWindow overlay window in OverlayManager with ShowModeArrows
  - WindowMode passed through CapsLockMonitor -> DaemonCommand -> OverlayOrchestrator
affects:
  - 12-cross-monitor-and-overlay-integration

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "DIB pixel-write triangle rasterizer: bounding-box scan + cross-product sign test for filled triangles"
    - "Mode tracking field set before _staDispatcher.Invoke (worker thread sets, STA thread reads inside Invoke)"
    - "Premultiplied ARGB pixel formula: pr = (r * alpha) / 255"

key-files:
  created:
    - focus/Windows/Daemon/Overlay/ArrowRenderer.cs
  modified:
    - focus/Windows/Daemon/CapsLockMonitor.cs
    - focus/Windows/Daemon/DaemonCommand.cs
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs

key-decisions:
  - "_currentMode set before _staDispatcher.Invoke: worker thread writes before synchronous Invoke, STA reads inside lambda — no concurrent mutation, no lock needed"
  - "ArrowRenderer DIB covers full window bounds (same as BorderRenderer) — arrow positions are DIB-local coordinates not screen coordinates"
  - "GetArrowSize clamps to [16, 48] pixels relative to min(width,height)/8 — scales to window size"
  - "PaintResizeArrows uses slightly smaller triangle (3/4 size) for back-to-back pair so both triangles fit"
  - "ShowOverlaysForActivation reads GetKeyState when CAPS held to detect pre-held modifiers and sets _currentMode accordingly"
  - "OnReleasedSta clears _currentMode = Navigate before HideAll so any teardown reads Navigate"

patterns-established:
  - "Triangle rasterization: compute bounding box, scan pixels, IsInsideTriangle cross-product test, write premultiplied ARGB"
  - "Shared BlitToLayeredWindow helper avoids DIB pipeline duplication between Paint methods"
  - "Mode colors: Move = 0xE0FF9900 amber, Grow = 0xE000CCBB cyan, Navigate = 0xE0FFFFFF white"

requirements-completed: [OVRL-01, OVRL-02, OVRL-03, OVRL-04]

# Metrics
duration: 5min
completed: 2026-03-03
---

# Phase 12 Plan 02: Cross-Monitor and Overlay Integration Summary

**Mode-aware overlay system: amber/cyan borders and DIB-rasterized arrow indicators appear instantly on LAlt (Move) or LWin (Grow) hold, tracking the window through every operation**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-02T23:48:00Z
- **Completed:** 2026-03-03T00:00:00Z
- **Tasks:** 2
- **Files modified:** 5 (4 modified + 1 created)

## Accomplishments

- CapsLockMonitor now passes WindowMode.Move or WindowMode.Grow through the onModeEntered callback, wired through DaemonCommand to OverlayOrchestrator.OnModeEntered(mode)
- New ArrowRenderer static class renders 4 compass arrows (Move mode) and axis-indicator pairs at right/top edges (Grow mode) using direct DIB pixel writes with cross-product triangle rasterization
- OverlayOrchestrator tracks _currentMode field and uses it to select amber (0xE0FF9900) or cyan (0xE000CCBB) borders; RefreshForegroundOverlayOnly calls ShowModeArrows after every move/resize
- OverlayManager gained _modeArrowWindow overlay window with ShowModeArrows, HideAll, and Dispose fully wired

## Task Commits

Each task was committed atomically:

1. **Task 1: Mode plumbing and ArrowRenderer** - `d3f7a9c` (feat)
2. **Task 2: Wire mode-colored border and arrows into OverlayOrchestrator and OverlayManager** - `c2bc0ff` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `focus/Windows/Daemon/Overlay/ArrowRenderer.cs` - New static class: PaintMoveArrows (4 compass), PaintResizeArrows (axis pairs), FillTriangle, IsInsideTriangle, BlitToLayeredWindow — all DIB pixel-write pipeline
- `focus/Windows/Daemon/CapsLockMonitor.cs` - _onModeEntered changed to Action<WindowMode>?, Invoke passes WindowMode.Move or WindowMode.Grow
- `focus/Windows/Daemon/DaemonCommand.cs` - onModeEntered wiring changed to (mode) => orchestrator?.OnModeEntered(mode)
- `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` - Added _currentMode field, MoveModeColor/GrowModeColor constants, updated OnModeEntered/OnModeExited/ShowOverlaysForActivation/OnReleasedSta/RefreshForegroundOverlayOnly
- `focus/Windows/Daemon/Overlay/OverlayManager.cs` - Added _modeArrowWindow field, ShowModeArrows method, HideAll and Dispose updated

## Decisions Made

- `_currentMode` is set on the worker thread before the synchronous `_staDispatcher.Invoke` call. Since Invoke blocks until the STA completes, the field is written before the STA reads it. No concurrent mutation is possible, so no lock is needed.
- ArrowRenderer DIB covers full window bounds (same as the border overlay). Arrow positions are expressed in DIB-local coordinates (0,0 = window top-left), not screen coordinates.
- `GetArrowSize` returns `Math.Clamp(Math.Min(width, height) / 8, 16, 48)` — scales proportionally to window size, clamped to reasonable bounds.
- `ShowOverlaysForActivation` reads `GetKeyState` at CAPS-hold time to detect pre-existing modifier state and sets `_currentMode` accordingly, so the correct arrows appear even if the modifier was pressed before CAPS.
- `OnReleasedSta` sets `_currentMode = WindowMode.Navigate` before `HideAll()` for safe teardown.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Mode indicator overlays complete: amber border + 4 compass arrows for Move, cyan border + axis pairs for Grow
- Arrows appear immediately on modifier hold, reposition after every move/resize via RefreshForegroundOverlayOnly
- Navigate mode restores white border with no arrows (existing behavior preserved)
- All transitions are instant (OVRL-04) — no animation, no timers, synchronous DIB writes
- Ready for cross-monitor work in subsequent plans

---
*Phase: 12-cross-monitor-and-overlay-integration*
*Completed: 2026-03-03*
