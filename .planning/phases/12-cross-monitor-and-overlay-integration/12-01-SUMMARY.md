---
phase: 12-cross-monitor-and-overlay-integration
plan: "01"
subsystem: window-management
tags: [win32, multi-monitor, grid-snap, monitor-adjacency, cross-monitor]

# Dependency graph
requires:
  - phase: 11-move-and-resize-single-monitor
    provides: WindowManagerService.MoveOrResize, GridCalculator, dual-rect coordinate pattern
  - phase: 10-grid-infrastructure
    provides: GridCalculator.GetGridStep with parameterized work area dimensions
provides:
  - MonitorHelper.FindAdjacentMonitor static method with overlapping-range adjacency algorithm
  - Cross-monitor Move transition in WindowManagerService.MoveOrResize
  - Grid step recalculation from target monitor work area (XMON-02)
  - ComputeCrossMonitorPosition for first-cell snap on entry edge
affects: [12-02-overlay-integration, future-cross-monitor-testing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "TryGetCrossMonitorTarget: caller checks boundary, helper fetches adjacent monitor handle only"
    - "rcMonitor used for adjacency detection (physical edges), rcWork used for placement (excludes taskbar)"
    - "Post-ComputeMove boundary check: detects clamped position == boundary to trigger cross-monitor"

key-files:
  created: []
  modified:
    - focus/Windows/MonitorHelper.cs
    - focus/Windows/Daemon/WindowManagerService.cs
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs

key-decisions:
  - "TryGetCrossMonitorTarget removes inner boundary check: outer MoveOrResize code already verified atBoundary using post-ComputeMove visible bounds; inner check on original visRect would incorrectly miss boundary transitions from one step away"
  - "FindAdjacentMonitor uses perpendicular overlap for multi-monitor disambiguation: candidate with greatest overlap wins, ensures correct behavior in triple-monitor setups"
  - "Cross-monitor applies to Move mode only: Grow/Shrink stays on current monitor by design"
  - "OnModeEntered fixed to accept WindowMode parameter: pre-existing signature mismatch (DaemonCommand already passed mode, OverlayOrchestrator hadn't been updated)"

patterns-established:
  - "Adjacent monitor lookup: EnumDisplayMonitors + MONITORENUMPROC local delegate + MONITORINFO pattern reused from EnumerateMonitors"
  - "Entry-edge snap: targetWork.left + stepX (right entry), targetWork.right - stepX - clampedW (left entry), analogous for vertical"
  - "Size clamping on cross-monitor: Math.Min(visW, targetWork.width) prevents resize, only repositions"

requirements-completed: [XMON-01, XMON-02]

# Metrics
duration: 3min
completed: 2026-03-03
---

# Phase 12 Plan 01: Cross-Monitor Window Transition Summary

**Cross-monitor Move transition using overlapping-range adjacency detection in MonitorHelper with grid step recalculated from target monitor work area dimensions (XMON-01, XMON-02)**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-02T23:48:03Z
- **Completed:** 2026-03-02T23:51:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Added `MonitorHelper.FindAdjacentMonitor` using overlapping-range adjacency with 2px edge tolerance for non-flush monitor arrangements and perpendicular overlap tie-breaking for triple-monitor setups
- Extended `WindowManagerService.MoveOrResize` with cross-monitor transition that fires when ComputeMove clamps to boundary; jumps window to first grid cell from entry edge on target monitor
- Grid step correctly recalculated from target monitor's `rcWork` dimensions (not the source monitor's) satisfying XMON-02
- Perpendicular axis position preserved, clamped to target work area; oversized windows clamped to target work area without resizing
- Grow mode is entirely unaffected by cross-monitor logic

## Task Commits

Each task was committed atomically:

1. **Task 1: Add FindAdjacentMonitor to MonitorHelper** - `bb682b3` (feat)
2. **Task 2: Add cross-monitor transition to WindowManagerService.MoveOrResize** - `f8b7c67` (feat)

## Files Created/Modified

- `focus/Windows/MonitorHelper.cs` - Added `FindAdjacentMonitor(HMONITOR, RECT, string)` static method
- `focus/Windows/Daemon/WindowManagerService.cs` - Added `TryGetCrossMonitorTarget`, `ComputeCrossMonitorPosition`, and cross-monitor block in `MoveOrResize`; added `using Focus.Windows`
- `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` - Fixed `OnModeEntered` signature to accept `WindowMode` parameter

## Decisions Made

- **Post-ComputeMove boundary check used (not pre-check):** The outer boundary check uses the newly computed visible rect after `ComputeMove` (which clamps), not the original `visRect`. This correctly detects the transition: a window one step from the boundary trying to move beyond it will produce a clamped `newVisRight == workArea.right`, triggering the cross-monitor attempt.
- **`TryGetCrossMonitorTarget` simplified to no boundary check:** The inner helper originally had a redundant boundary check on the original `visRect`, which would have missed transitions from one step away. Removed it since the caller already verified boundary using post-ComputeMove bounds.
- **`rcMonitor` vs `rcWork` separation maintained:** `FindAdjacentMonitor` uses `rcMonitor` edges (physical screen edges including taskbar area) for adjacency detection. `ComputeCrossMonitorPosition` uses `rcWork` (excludes taskbar) for all window placement math.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed OnModeEntered signature mismatch preventing build**
- **Found during:** Task 1 (initial build verification after adding FindAdjacentMonitor)
- **Issue:** `DaemonCommand.cs` already called `orchestrator?.OnModeEntered(mode)` with a `WindowMode` argument but `OverlayOrchestrator.OnModeEntered()` still had no parameters, causing `CS1501: No overload for method 'OnModeEntered' takes 1 arguments`
- **Fix:** Added `WindowMode mode` parameter to `OverlayOrchestrator.OnModeEntered` (parameter unused for now; will be used by plan 02 arrow rendering)
- **Files modified:** `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs`
- **Verification:** Build compiles with zero errors
- **Committed in:** `bb682b3` (Task 1 commit)

**2. [Rule 1 - Bug] Fixed incorrect boundary-check logic in TryGetCrossMonitorTarget**
- **Found during:** Task 2 (code review before committing)
- **Issue:** Plan specified `TryGetCrossMonitorTarget` should check `vis.right >= currentWork.right` on the original visRect. But this is wrong: a window one step from the boundary gets clamped by `ComputeMove` to `newVisRight == workArea.right`. The original `vis.right < workArea.right` so the inner check would return null — the cross-monitor transition would never fire for the normal case
- **Fix:** Removed redundant inner boundary check from `TryGetCrossMonitorTarget`; outer `MoveOrResize` block already checks boundary on post-ComputeMove bounds before calling the helper
- **Files modified:** `focus/Windows/Daemon/WindowManagerService.cs`
- **Verification:** Logic traced: window at (boundary - stepX) moves right, ComputeMove clamps to boundary, outer check detects `newVisRight >= workArea.right`, helper returns adjacent monitor
- **Committed in:** `f8b7c67` (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking build error, 1 logic bug)
**Impact on plan:** Both fixes necessary for correctness. The OnModeEntered fix is a pre-existing mismatch from previous session work. The boundary-check bug fix ensures the cross-monitor transition actually fires in the normal case (window approaching boundary from one step away).

## Issues Encountered

- Pre-existing modified files in working tree from prior session work (`CapsLockMonitor.cs`, `KeyboardHookHandler.cs`, `DaemonCommand.cs`, `OverlayManager.cs`, `ArrowRenderer.cs`). These belong to plan 02 overlay arrow work. Carefully staged only plan 01 related files in each commit.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Cross-monitor Move transition is fully implemented and building cleanly
- `MonitorHelper.FindAdjacentMonitor` is available for any future callers needing adjacent monitor detection
- Plan 02 (overlay arrows for mode indicators) can proceed; pre-existing skeleton files `ArrowRenderer.cs` and `OverlayManager` updates are already in working tree from prior session
- The `OnModeEntered(WindowMode mode)` parameter is accepted but unused - plan 02 will wire the mode tracking to trigger arrow rendering

---
*Phase: 12-cross-monitor-and-overlay-integration*
*Completed: 2026-03-03*
