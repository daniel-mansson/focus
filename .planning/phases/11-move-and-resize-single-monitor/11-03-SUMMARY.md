---
phase: 11-move-and-resize-single-monitor
plan: "03"
subsystem: ui
tags: [overlay, winforms, csharp, win32, STA-thread]

# Dependency graph
requires:
  - phase: 11-move-and-resize-single-monitor
    provides: OverlayOrchestrator wired with OnDirectionKeyDown dispatching MoveOrResize

provides:
  - RefreshForegroundOverlayOnly method in OverlayOrchestrator
  - Overlay redraws around active window after every Move/Grow/Shrink step
  - Navigate-target outlines hidden during Move/Grow/Shrink modes

affects:
  - 11-move-and-resize-single-monitor
  - UAT retesting

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Block lambda with post-MoveOrResize overlay refresh guarded by _capsLockHeld
    - RefreshForegroundOverlayOnly — HideAll then ShowForegroundOverlay (no navigate targets)

key-files:
  created: []
  modified:
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs

key-decisions:
  - "RefreshForegroundOverlayOnly instead of ShowOverlaysForCurrentForeground: During Move/Grow/Shrink only the active window border should be visible; navigate targets are distracting and wrong"
  - "_capsLockHeld guard on RefreshForegroundOverlayOnly: prevents stale overlay flash if CapsLock released between direction keydown and STA dispatch execution"
  - "Synchronous refresh inside STA dispatch: no timer or delay added — overlay update is instant within the same Invoke call as MoveOrResize"

patterns-established:
  - "Block lambda pattern: _staDispatcher.Invoke(() => { operation(); if (_capsLockHeld) overlay_refresh(); })"

requirements-completed:
  - MOVE-01
  - SIZE-01
  - SIZE-02

# Metrics
duration: 1min
completed: 2026-03-02
---

# Phase 11 Plan 03: Overlay Refresh After Move/Grow/Shrink Summary

**Synchronous active-window overlay refresh after every MoveOrResize call, with navigate-target outlines suppressed during Move/Grow/Shrink modes**

## Performance

- **Duration:** 1 min
- **Started:** 2026-03-02T21:33:41Z
- **Completed:** 2026-03-02T21:34:35Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Block lambda added to non-Navigate branch of OnDirectionKeyDown — after MoveOrResize, calls RefreshForegroundOverlayOnly if CapsLock is still held
- New private method RefreshForegroundOverlayOnly: calls HideAll then ShowForegroundOverlay via DwmGetWindowAttribute — only the active window border is drawn
- UAT test 1 gap (overlay not following window after move) is addressed — overlay now tracks the window's new bounds immediately after each step
- Navigate-target outlines (surrounding window indicators) are suppressed during Move/Grow/Shrink — cleaner UX while repositioning

## Task Commits

Each task was committed atomically:

1. **Task 1: Add overlay refresh after MoveOrResize in OverlayOrchestrator** - `54597ee` (feat)

## Files Created/Modified

- `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` — Block lambda in OnDirectionKeyDown non-Navigate branch; new RefreshForegroundOverlayOnly private method

## Decisions Made

- Used `RefreshForegroundOverlayOnly` instead of `ShowOverlaysForCurrentForeground` to avoid drawing navigate-target outlines during Move/Grow/Shrink modes — only the active window border should be visible while repositioning
- Added `_capsLockHeld` guard to prevent a race condition: if CapsLock is released between keydown event and STA dispatch execution, OnReleasedSta already called HideAll — without the guard the refresh would re-show overlays briefly after they should be hidden
- Refresh is synchronous within the same `_staDispatcher.Invoke` call as MoveOrResize — no timer or async delay, matching the instant grid-snap feel

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## Next Phase Readiness

- Overlay refresh gap (UAT test 1) is closed — active window border follows the window after every Move/Grow/Shrink step
- Navigate-target outline gap (UAT test, hide during move) is also closed by this same change
- Remaining UAT gaps (shrink directions inverted, shrink at OS min moves window, grow-down snaps upward) are addressed in plans 04+
- Build compiles with zero C# errors

---
*Phase: 11-move-and-resize-single-monitor*
*Completed: 2026-03-02*
