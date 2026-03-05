---
status: diagnosed
trigger: "Grow-down snaps upward (shrinks instead of growing)"
created: 2026-03-02T00:00:00Z
updated: 2026-03-02T00:00:00Z
---

## Current Focus

hypothesis: NearestGridLine snaps to nearest line regardless of movement direction, so it can snap backward (upward) when the bottom edge is just past a grid line — and separately, Shift+CapsLock ordering fails because Shift held before CapsLock causes CAPSLOCK to be filtered out entirely.
test: static code analysis completed
expecting: confirmed
next_action: return root cause findings to caller

## Symptoms

expected: Pressing Down in Grow mode expands the window downward (bottom edge moves down).
actual: Window shrinks upward — the bottom edge moves upward — instead of growing.
errors: none (silent wrong behavior)
reproduction: Hold CapsLock+LShift, press Down arrow.
started: Phase 11 implementation (new ComputeGrow)

## Eliminated

- hypothesis: ComputeGrow selects the wrong edge for "down" (moves top instead of bottom)
  evidence: Code at line 193-198 correctly selects vis.bottom for "down", vis.top for "up"
  timestamp: 2026-03-02T00:00:00Z

## Evidence

- timestamp: 2026-03-02T00:00:00Z
  checked: WindowManagerService.cs ComputeGrow "down" case (lines 193-198)
  found: |
    newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
        ? vis.bottom + stepY
        : GridCalculator.NearestGridLine(vis.bottom, work.top, stepY);
  implication: When NOT aligned, NearestGridLine is called. It snaps to the NEAREST grid line — which can be above vis.bottom (upward) if the bottom edge is just above the next grid line. This reduces newVisBottom, shrinking the window.

- timestamp: 2026-03-02T00:00:00Z
  checked: GridCalculator.cs NearestGridLine (lines 26-32)
  found: Uses Math.Round — rounds to nearest, bidirectional. Has no concept of "I am growing downward so snap forward".
  implication: For a grow-down, the snap should go to the NEXT grid line in the downward direction (ceiling), not the nearest. NearestGridLine can return a value less than vis.bottom, inverting the operation.

- timestamp: 2026-03-02T00:00:00Z
  checked: ComputeGrow "left" case (lines 185-190) — same pattern
  found: newVisLeft snaps to NearestGridLine(vis.left, work.left, stepX) which can snap RIGHT (inward) when the left edge is just past a grid line.
  implication: Same directional snap bug affects all four Grow edges.

- timestamp: 2026-03-02T00:00:00Z
  checked: ComputeShrink for contrast — "down" case (lines 257-263)
  found: Shrink-down also uses NearestGridLine for vis.bottom — but shrink-down is supposed to move the bottom edge UPWARD (inward), so snapping to nearest is correct there.
  implication: The snap direction semantics are opposite for Grow vs Shrink, but both call the same NearestGridLine blindly.

- timestamp: 2026-03-02T00:00:00Z
  checked: KeyboardHookHandler.cs CAPSLOCK filter (lines 186-197)
  found: |
    if (((uint)kbd->flags & LLKHF_ALTDOWN) != 0) return CallNextHookEx(...);
    if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) return CallNextHookEx(...);
    if ((GetKeyState(VK_SHIFT)   & 0x8000) != 0) return CallNextHookEx(...);
  implication: If the user holds LShift first, then presses CapsLock, the SHIFT check at line 196 passes through the CapsLock event WITHOUT suppressing it — _capsLockHeld is never set to true, so mode is never activated.

- timestamp: 2026-03-02T00:00:00Z
  checked: KeyboardHookHandler.cs OnCapsLockReleased path + OverlayOrchestrator OnReleasedSta (line 258-264)
  found: OnReleasedSta calls _overlayManager.HideAll() unconditionally. But if Shift+CapsLock put the system in a partial state (overlay shown via some path), releasing could leave overlays visible.
  implication: The "overlay stays after releasing CapsLock in certain Shift+CapsLock combinations" is a direct consequence of the Shift-first bug — _capsLockHeld is false so OnCapsLockReleased may not even be triggered, leaving any displayed overlays orphaned.

## Resolution

root_cause: |
  BUG 1 (Grow snaps in wrong direction):
  In ComputeGrow, the not-aligned branch calls NearestGridLine which rounds to the nearest grid
  line in either direction. For a Grow operation the snap must be directional:
    - Growing outward (down/right): snap must go to the NEXT grid line in the outward direction
      (i.e., ceiling toward larger values).
    - Growing outward (up/left): snap must go to the NEXT grid line in the inward direction
      (i.e., floor toward smaller values).
  When a window bottom is, say, 2px above a grid line and the user presses Grow-Down,
  NearestGridLine rounds DOWN to that grid line above — making newVisBottom < vis.bottom,
  which causes the window to shrink instead of grow.

  BUG 2 (Shift+CapsLock ordering / overlay stays visible):
  KeyboardHookHandler filters out CAPSLOCK events when any modifier (including Shift) is held.
  This is intentional for Alt and Ctrl, but the Shift filter is overly broad: it blocks
  Shift+CapsLock regardless of sequence. When the user holds Shift first then presses CapsLock,
  the CAPSLOCK keydown event passes through to the system (not suppressed, not captured),
  _capsLockHeld remains false, OnCapsLockHeld is never called, and the overlay/mode never
  activates. On CapsLock release, _capsLockHeld is still false so nothing is cleaned up —
  any partial overlay state is left visible.

fix: NOT APPLIED (goal: find_root_cause_only)
verification: NOT APPLIED
files_changed: []
