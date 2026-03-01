---
phase: quick-1
plan: 01
subsystem: ui
tags: [overlay, win32, gdi, layered-window, border-renderer]

# Dependency graph
requires:
  - phase: 08-in-daemon-navigation
    provides: OverlayOrchestrator, OverlayManager, BorderRenderer — overlay infrastructure this extends
provides:
  - BorderRenderer.PaintFullBorder static method (full-perimeter rounded-rect border on a single overlay)
  - OverlayManager._foregroundWindow (5th overlay window for focused-window highlight)
  - OverlayManager.ShowForegroundOverlay / HideForegroundOverlay
  - White border on active window rendered in ShowOverlaysForCurrentForeground
affects:
  - phase: 09-overlay-chaining — foreground border must persist and refresh through sequential moves

# Tech tracking
tech-stack:
  added: []
  patterns:
    - PaintFullBorder uses same DIB-section + UpdateLayeredWindow pattern as directional Paint()
    - GetFullBorderAlpha: corner cutout logic renders rounded-rect outline without fade tails
    - 5th OverlayWindow follows exact same lifecycle (Reposition/Show/Hide/Dispose) as the 4 directional windows
    - Solo-window variable renamed to soloFgHwnd to avoid CS0136 scope conflict with top-level fgHwnd

key-files:
  created: []
  modified:
    - focus/Windows/Daemon/Overlay/BorderRenderer.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs

key-decisions:
  - "ForegroundBorderColor = 0xE0FFFFFF (~88% opacity white) — visible but not harsh"
  - "PaintFullBorder is a static method called directly by OverlayManager, bypassing IOverlayRenderer.Paint (different contract — no direction parameter)"
  - "GetFullBorderAlpha uses corner cutout logic: pixels in corner quadrant only lit if on arc, preventing square corners"
  - "ShowForegroundOverlay called before the directional overlay loop so border is always shown even in solo-window case"
  - "solo-window fgHwnd local renamed to soloFgHwnd to resolve CS0136 variable scope conflict"

patterns-established:
  - "Full-perimeter border: call PaintFullBorder with DWMWA_EXTENDED_FRAME_BOUNDS rect for pixel-perfect window edge alignment"

requirements-completed: [QUICK-1]

# Metrics
duration: 15min
completed: 2026-03-01
---

# Quick Task 1: Add White Border Around Active Window Summary

**5th overlay window renders a 2px white rounded-rect border on the foreground window whenever CAPSLOCK is held, coexisting with directional navigation overlays**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-01T21:45:00Z
- **Completed:** 2026-03-01T21:48:00Z
- **Tasks:** 1/2 (Task 2 is checkpoint:human-verify — awaiting verification)
- **Files modified:** 3

## Accomplishments

- Added `BorderRenderer.PaintFullBorder` static method — renders all 4 edges + rounded corners at full opacity with no fade tails
- Added 5th `OverlayWindow _foregroundWindow` to `OverlayManager` with `ShowForegroundOverlay`/`HideForegroundOverlay` methods
- Wired foreground border into `OverlayOrchestrator.ShowOverlaysForCurrentForeground()` using DWM extended frame bounds (same pattern as existing solo-window code)
- `HideAll()` and `Dispose()` updated to cover the new foreground window — no resource leaks

## Task Commits

Each task was committed atomically:

1. **Task 1: Add full-perimeter border rendering and foreground overlay window** - `964df6f` (feat)

**Plan metadata:** (pending final commit after human verification)

## Files Created/Modified

- `focus/Windows/Daemon/Overlay/BorderRenderer.cs` — Added `PaintFullBorder` static method and `GetFullBorderAlpha` helper
- `focus/Windows/Daemon/Overlay/OverlayManager.cs` — Added `_foregroundWindow`, `ShowForegroundOverlay`, `HideForegroundOverlay`; updated `HideAll` and `Dispose`
- `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` — Added `ForegroundBorderColor` constant, wired foreground border into `ShowOverlaysForCurrentForeground`

## Decisions Made

- `PaintFullBorder` is static (not instance) because it doesn't need the `IOverlayRenderer` direction-based contract — it renders all 4 edges in one call
- `ForegroundBorderColor = 0xE0FFFFFF` (~88% opacity): high enough to be clearly visible against any background without being harsh white
- Corner cutout logic: pixels inside a corner quadrant (`px < radius && py < radius` etc.) are only lit if they fall on the arc, producing rounded corners
- `soloFgHwnd` rename: the solo-window section already used `fgHwnd` as a local — renamed to avoid CS0136 compiler error

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed CS0136 variable scope conflict: renamed solo-window `fgHwnd` to `soloFgHwnd`**
- **Found during:** Task 1 (first build attempt)
- **Issue:** Plan's code snippet added `var fgHwnd = PInvoke.GetForegroundWindow()` at the top of `ShowOverlaysForCurrentForeground()`, but the existing solo-window section (line 306) also declares `var fgHwnd` in the same method scope — CS0136 error
- **Fix:** Renamed the solo-window local from `fgHwnd` to `soloFgHwnd` (semantically clearer anyway)
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
- **Verification:** Second build produced no CS errors; only MSB3027 file-lock (daemon running)
- **Committed in:** 964df6f (part of Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - bug/compilation error)
**Impact on plan:** Necessary fix for compilation. No behavior change — rename is semantically equivalent.

## Issues Encountered

- Running daemon (PID 23540) held a file lock on `focus.exe` preventing the build tool from copying the output apphost. C# compilation succeeded (DLL produced in obj folder); file-lock is a deployment artifact only. Build will succeed fully once the daemon is stopped.

## Next Phase Readiness

- White border feature is complete and compiled; awaiting human verification (Task 2 checkpoint)
- After verification, Phase 9 (Overlay Chaining) can proceed — the foreground border must persist and refresh through sequential CAPSLOCK+direction moves

---
*Phase: quick-1*
*Completed: 2026-03-01*

## Self-Check: PASSED

- FOUND: focus/Windows/Daemon/Overlay/BorderRenderer.cs
- FOUND: focus/Windows/Daemon/Overlay/OverlayManager.cs
- FOUND: focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
- FOUND: .planning/quick/1-add-a-white-border-around-the-currently-/1-SUMMARY.md
- FOUND: commit 964df6f
- FOUND: BorderRenderer.PaintFullBorder at line 133
- FOUND: OverlayManager._foregroundWindow at line 21
- FOUND: OverlayOrchestrator.ForegroundBorderColor + ShowForegroundOverlay call
