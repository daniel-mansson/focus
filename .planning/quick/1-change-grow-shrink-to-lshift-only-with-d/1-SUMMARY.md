---
phase: quick
plan: 1
subsystem: keyboard-hook / window-manager
tags: [keybinding, resize, grow, shrink, simplification]
dependency_graph:
  requires: []
  provides: [symmetric-grow-shrink-via-lshift]
  affects: [KeyEvent, KeyboardHookHandler, CapsLockMonitor, WindowManagerService]
tech_stack:
  added: []
  patterns: [symmetric-edge-movement, half-step-per-edge, pre-check-min-size-guard]
key_files:
  created: []
  modified:
    - focus/Windows/Daemon/KeyEvent.cs
    - focus/Windows/Daemon/KeyboardHookHandler.cs
    - focus/Windows/Daemon/CapsLockMonitor.cs
    - focus/Windows/Daemon/WindowManagerService.cs
decisions:
  - "Single modifier (LShift) for all resize; direction encodes axis AND intent (right/up=grow, left/down=shrink)"
  - "Both edges move symmetrically by half a grid step each — not one edge by a full step"
  - "LCtrlHeld field removed from KeyEvent; LCtrl no longer triggers any resize mode"
  - "ComputeShrink removed; ComputeGrow unified to handle all 4 directions"
metrics:
  duration: "3 min"
  completed_date: "2026-03-02"
  tasks_completed: 2
  files_modified: 4
---

# Quick Task 1: Change Grow/Shrink to LShift-only with Direction-encoded Intent Summary

**One-liner:** Unified grow/shrink under CAPS+LShift using direction semantics — right/up expands both edges outward symmetrically, left/down contracts both edges inward, removing LCtrl shrink mode entirely.

## What Was Built

Simplified the resize keybinding from two modifiers (LShift=grow, LCtrl=shrink) to a single modifier (LShift) where direction encodes both axis and intent. Both edges on the affected axis now move symmetrically by half a grid step each per keypress.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Remove Shrink mode from enum, hook, and monitor | 67d591c | KeyEvent.cs, KeyboardHookHandler.cs, CapsLockMonitor.cs |
| 2 | Rewrite ComputeGrow for symmetric expand/contract by direction | bc8a179 | WindowManagerService.cs |

## Key Changes

**KeyEvent.cs:**
- `WindowMode` enum reduced from 4 values (`Navigate, Move, Grow, Shrink`) to 3 (`Navigate, Move, Grow`)
- `LCtrlHeld` field removed from `KeyEvent` record struct
- Doc comment updated to describe new Grow semantics

**KeyboardHookHandler.cs:**
- `VK_LCONTROL` constant removed
- `lCtrlHeld` local variable removed from direction key block
- Mode switch simplified to `(_tabHeld, lShiftHeld)` tuple — no LCtrl branch
- `TryWrite` call updated: `new KeyEvent(..., lShiftHeld, altHeld, mode)` (no lCtrlHeld arg)

**CapsLockMonitor.cs:**
- `BuildModifierPrefix`: removed `LCtrl` branch; condition updated to `!evt.LShiftHeld && !evt.AltHeld`

**WindowManagerService.cs:**
- `WindowMode.Shrink` case removed from `MoveOrResize` dispatch
- `ComputeShrink` method deleted entirely
- `ComputeGrow` rewritten with symmetric semantics:
  - `right`: both left and right edges expand outward by `halfStepX` each
  - `left`: both left and right edges contract inward by `halfStepX` each (min-size guard + center-anchor clamp)
  - `up`: both top and bottom edges expand outward by `halfStepY` each
  - `down`: both top and bottom edges contract inward by `halfStepY` each (min-size guard + center-anchor clamp)
- Post-computation no-op guard added for shrink directions

## Verification

Build result: `0 Error(s)` — project compiles cleanly.

Manual testing required:
1. CAPS+LShift+Right — window grows horizontally from both sides
2. CAPS+LShift+Left — window shrinks horizontally from both sides
3. CAPS+LShift+Up — window grows vertically from both edges
4. CAPS+LShift+Down — window shrinks vertically from both edges
5. CAPS+LCtrl+direction — behaves as plain navigate (no resize)
6. CAPS+direction (bare) — still navigates
7. CAPS+TAB+direction — still moves

## Deviations from Plan

None - plan executed exactly as written.

## Self-Check: PASSED

Files exist:
- focus/Windows/Daemon/KeyEvent.cs: FOUND
- focus/Windows/Daemon/KeyboardHookHandler.cs: FOUND
- focus/Windows/Daemon/CapsLockMonitor.cs: FOUND
- focus/Windows/Daemon/WindowManagerService.cs: FOUND

Commits exist:
- 67d591c: feat(quick-1): remove Shrink mode from enum, hook, and monitor
- bc8a179: feat(quick-1): rewrite ComputeGrow for symmetric expand/contract by direction
