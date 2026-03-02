---
phase: 11-move-and-resize-single-monitor
plan: "02"
subsystem: window-management
tags: [grid-snap, resize, grow, shrink, keyboard-hook, winforms, pinvoke]

# Dependency graph
requires:
  - phase: 11-move-and-resize-single-monitor
    provides: GridCalculator with bidirectional NearestGridLine, ComputeGrow/Shrink in WindowManagerService, keyboard hook with TAB/direction interception

provides:
  - NearestGridLineFloor (Math.Floor snap) for edges moving toward smaller values
  - NearestGridLineCeiling (Math.Ceiling snap) for edges moving toward larger values
  - Fixed ComputeGrow using directional snap variants to always expand outward
  - Fixed ComputeShrink with corrected edge mapping (up=bottom, down=top, left=right, right=left)
  - Post-computation no-op guard in ComputeShrink for OS minimum size protection
  - Shift-first+CapsLock activation unblocked (Shift filter removed from CapsLock handler)

affects: [UAT-phase-11, shrink-operations, grow-operations, shift-caps-activation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Directional snap pattern: NearestGridLineFloor for edges moving toward smaller values, NearestGridLineCeiling for edges moving toward larger values"
    - "Post-computation no-op guard: check if visible dimensions actually changed before calling SetWindowPos"
    - "Opposite-edge shrink mapping: shrink direction names the side that stays fixed, not the side that moves"

key-files:
  created: []
  modified:
    - focus/Windows/GridCalculator.cs
    - focus/Windows/Daemon/WindowManagerService.cs
    - focus/Windows/Daemon/KeyboardHookHandler.cs

key-decisions:
  - "NearestGridLineFloor/Ceiling added to GridCalculator alongside NearestGridLine — Move still uses NearestGridLine (bidirectional is correct for move), Grow/Shrink use directional variants"
  - "ComputeShrink edge mapping: direction names the side that does NOT move (opposite edge contracts inward) — up=bottom moves up, down=top moves down, left=right moves left, right=left moves right"
  - "Post-computation no-op guard placed after switch block, before coordinate translation — catches OS min-size clamping where grid math produces unchanged or larger rect"
  - "Shift filter removed from CapsLock section in KeyboardHookHandler — Shift+CapsLock now activates overlay, enabling Shift-first workflow for grow mode"

patterns-established:
  - "Directional snap: Ceiling for outward/rightward/downward movement, Floor for outward/leftward/upward movement"
  - "No-op guard placement: post-computation, pre-SetWindowPos for minimum-size protection"

requirements-completed: [SIZE-01, SIZE-02, SIZE-03, GRID-03]

# Metrics
duration: 2min
completed: 2026-03-02
---

# Phase 11 Plan 02: Bug Fixes for Grow/Shrink Direction and CapsLock Activation Summary

**Directional snap variants (Floor/Ceiling) in GridCalculator fix grow-down snapping upward; ComputeShrink edge inversion and post-computation no-op guard fix shrink at OS minimum; Shift filter removal unblocks Shift-first+CapsLock activation**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-02T20:33:35Z
- **Completed:** 2026-03-02T20:35:55Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Added NearestGridLineFloor and NearestGridLineCeiling to GridCalculator; ComputeGrow now always snaps the moving edge outward regardless of misalignment direction
- Fixed ComputeShrink to move the opposite edge (shrink-up moves bottom edge upward, shrink-down moves top edge downward, etc.) — the previous code moved the named edge inward which had the wrong semantic
- Added post-computation no-op guard in ComputeShrink: if grid math doesn't actually reduce visible dimensions (e.g., window at OS min-track size), return win unchanged to prevent position-only movement
- Removed Shift filter from CapsLock modifier check in KeyboardHookHandler, enabling Shift-first+CapsLock activation for the grow mode workflow

## Task Commits

Each task was committed atomically:

1. **Task 1: Add directional snap to GridCalculator and fix ComputeGrow** - `7ea1554` (feat)
2. **Task 2: Fix ComputeShrink edge inversion, add post-computation no-op guard, and remove Shift filter from CapsLock** - `4c0bbe6` (fix)

## Files Created/Modified

- `focus/Windows/GridCalculator.cs` - Added NearestGridLineFloor (Math.Floor) and NearestGridLineCeiling (Math.Ceiling) public static methods
- `focus/Windows/Daemon/WindowManagerService.cs` - ComputeGrow updated to use directional snap; ComputeShrink cases swapped to move opposite edges + directional snap + post-computation no-op guard
- `focus/Windows/Daemon/KeyboardHookHandler.cs` - Shift filter removed from CapsLock modifier check; comment updated

## Decisions Made

- **NearestGridLine stays in ComputeMove**: Move uses bidirectional snap (nearest grid line regardless of direction) which is correct — user wants to snap to nearest line when misaligned, then step from there. Grow/Shrink are directional so they need Floor/Ceiling variants.
- **Post-computation guard vs. pre-check**: The pre-check `if (visW <= stepX) return win` guards against windows smaller than one step, but can't catch the OS min-track case where the window is above one step but the math still doesn't produce shrinkage. Post-computation guard handles that gap.
- **Shift filter removal is safe**: The comment said "only bare CAPSLOCK triggers detection" but the intent of Shift-first+CapsLock is explicitly supported by the mode design. Alt and Ctrl filters remain — those prevent genuine system shortcuts (Alt+Caps = OS shortcut, Ctrl+Caps = potential shortcut).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All four UAT-diagnosed bugs are fixed: grow-down snap direction (test 3), shrink direction inversion (test 5), shrink at OS minimum (test 6), Shift-first+CapsLock activation (sub-issue from test 3)
- ComputeMove behavior is preserved (NearestGridLine unchanged on lines 133, 144)
- Ready for UAT re-run to validate fixes

---
*Phase: 11-move-and-resize-single-monitor*
*Completed: 2026-03-02*
