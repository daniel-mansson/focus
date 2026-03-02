---
status: diagnosed
phase: 11-move-and-resize-single-monitor
source: 11-01-SUMMARY.md
started: 2026-03-02T00:00:00Z
updated: 2026-03-02T00:00:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Move Window One Grid Step
expected: With overlay active (Caps+Tab held), switch to Move mode, then press an arrow key. The foreground window should jump one grid step in that direction. Pressing the same arrow again should move it another grid step.
result: issue
reported: "The active window outline does not follow the window, it stays in the old position. needs to be redrawn"
severity: major

### 2. Move Clamps to Monitor Boundaries
expected: Move a window repeatedly toward a screen edge (e.g., keep pressing Right). The window should stop at the work area boundary and not go off-screen or behind the taskbar.
result: pass

### 3. Grow Window Edge Outward
expected: Switch to Grow mode and press an arrow key. The window edge in that direction should expand outward by one grid step while the opposite edge stays fixed. For example, pressing Right in Grow mode should widen the window to the right.
result: issue
reported: "1. The outline stayed after releasing capslock in some combination with shift/before/after. 2. holding shift first, then caps lock, should work but it doesnt. 3. when pressing down, the snapping snapped the size upwards, shrinking the window when growing was expected"
severity: major

### 4. Grow Stops at Monitor Boundary
expected: Keep growing a window edge toward a screen edge. Growth should stop once the edge reaches the work area boundary — pressing the key further does nothing visible.
result: pass

### 5. Shrink Window Edge Inward
expected: Switch to Shrink mode and press an arrow key. The window edge in that direction should contract inward by one grid step while the opposite edge stays fixed. For example, pressing Right in Shrink mode should pull the right edge leftward, making the window narrower.
result: issue
reported: "all directions feels inverted. pressing up when shrinking should move the bottom edge upwards"
severity: major

### 6. Shrink Stops at Minimum Size
expected: Keep shrinking a window. Once the window is about one grid step wide (or tall), further shrink presses in that axis should do nothing — the window should not collapse to zero or disappear.
result: issue
reported: "when the window reaches its OS minimum size, shrinking from the left moves the window instead of shrinking it. in this case a no-op would be expected, not moving the window"
severity: major

### 7. Maximized Window Ignored
expected: Maximize a window, then try Move/Grow/Shrink with arrow keys. Nothing should happen — the window should stay maximized. It should NOT restore-then-move.
result: pass

## Summary

total: 7
passed: 3
issues: 4
pending: 0
skipped: 0

## Gaps

- truth: "Window outline follows the window after move"
  status: failed
  reason: "User reported: The active window outline does not follow the window, it stays in the old position. needs to be redrawn"
  severity: major
  test: 1
  root_cause: "OnDirectionKeyDown returns immediately after MoveOrResize with no overlay refresh. SetWindowPos uses SWP_NOACTIVATE so no EVENT_SYSTEM_FOREGROUND fires. ShowOverlaysForCurrentForeground() is never called."
  artifacts:
    - path: "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs"
      issue: "Move/Grow/Shrink branch returns without calling ShowOverlaysForCurrentForeground()"
  missing:
    - "Call ShowOverlaysForCurrentForeground() after MoveOrResize inside _staDispatcher.Invoke lambda, guarded by _capsLockHeld"
  debug_session: ".planning/debug/overlay-no-redraw-after-move.md"

- truth: "Grow mode expands window edge outward correctly"
  status: failed
  reason: "User reported: 1. The outline stayed after releasing capslock in some combination with shift/before/after. 2. holding shift first, then caps lock, should work but it doesnt. 3. when pressing down, the snapping snapped the size upwards, shrinking the window when growing was expected"
  severity: major
  test: 3
  root_cause: "(a) NearestGridLine uses Math.Round (bidirectional) but Grow needs directional snapping — ceiling for right/down, floor for left/up. (b) KeyboardHookHandler filters out CapsLock when Shift is held, blocking Shift-first+CapsLock activation and leaving overlay stuck."
  artifacts:
    - path: "focus/Windows/GridCalculator.cs"
      issue: "NearestGridLine is direction-agnostic (Math.Round); Grow needs directional variants"
    - path: "focus/Windows/Daemon/KeyboardHookHandler.cs"
      issue: "Shift filter on CapsLock blocks Shift-first+CapsLock activation"
  missing:
    - "Add NearestGridLineFloor and NearestGridLineCeiling to GridCalculator"
    - "Use ceiling variant for Grow right/down, floor variant for Grow left/up in ComputeGrow"
    - "Remove Shift from the modifier filter that blocks CapsLock in KeyboardHookHandler"
  debug_session: ".planning/debug/grow-down-snaps-upward.md"

- truth: "Shrink mode contracts window edge inward in the pressed direction"
  status: failed
  reason: "User reported: all directions feels inverted. pressing up when shrinking should move the bottom edge upwards"
  severity: major
  test: 5
  root_cause: "ComputeShrink uses same edge mapping as ComputeGrow — pressed direction identifies which edge moves. For Shrink, pressing 'up' moves the top edge down instead of the bottom edge up. All four directions are inverted."
  artifacts:
    - path: "focus/Windows/Daemon/WindowManagerService.cs"
      issue: "ComputeShrink edge mapping inverted — up moves top edge instead of bottom edge"
  missing:
    - "Swap edge mapping in all four ComputeShrink cases: up→bottom edge, down→top edge, left→right edge, right→left edge"
  debug_session: ".planning/debug/shrink-directions-inverted.md"

- truth: "Shrink at minimum size is a no-op"
  status: failed
  reason: "User reported: when the window reaches its OS minimum size, shrinking from the left moves the window instead of shrinking it. in this case a no-op would be expected, not moving the window"
  severity: major
  test: 6
  root_cause: "Min-size guard checks visW <= stepX (grid step) not OS minimum. SetWindowPos clamps size to OS ptMinTrackSize but still honors position, causing anchor edge to move without shrink."
  artifacts:
    - path: "focus/Windows/Daemon/WindowManagerService.cs"
      issue: "ComputeShrink guard checks grid step minimum, not OS minimum; SetWindowPos moves position without shrinking"
  missing:
    - "Add post-computation no-op guard: if computed visible dimension >= current visible dimension, return win unchanged"
  debug_session: ".planning/debug/shrink-at-os-min-moves-window.md"
