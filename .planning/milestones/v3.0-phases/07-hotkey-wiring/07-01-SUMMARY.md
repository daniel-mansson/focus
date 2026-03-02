---
phase: 07-hotkey-wiring
plan: 01
subsystem: daemon-keyboard-hook
tags: [keyboard-hook, direction-keys, capslock, suppression, verbose-logging]
dependency_graph:
  requires: []
  provides: [direction-key-interception, direction-key-suppression, direction-key-logging, on-direction-key-down-callback]
  affects: [focus/Windows/Daemon/KeyboardHookHandler.cs, focus/Windows/Daemon/CapsLockMonitor.cs, focus/Windows/Daemon/KeyEvent.cs]
tech_stack:
  added: []
  patterns: [WH_KEYBOARD_LL-direction-key-suppression, channel-producer-consumer, key-repeat-suppression-via-hashset]
key_files:
  created: []
  modified:
    - focus/Windows/Daemon/KeyEvent.cs
    - focus/Windows/Daemon/KeyboardHookHandler.cs
    - focus/Windows/Daemon/CapsLockMonitor.cs
decisions:
  - "Track direction key repeats via HashSet<uint> (_directionKeysHeld) — cleared on keyup and ResetState() for sleep/wake safety"
  - "IsDirectionKey() uses switch expression for O(1) VK code lookup without heap allocation"
  - "Modifier prefix order in verbose log: Ctrl+Alt+Shift+ (control before alt before shift)"
  - "Both keydown and keyup posted to channel — keyup needed for Phase 8 repeat prevention; only keydown triggers callback/log"
  - "KeyEvent extended with optional ShiftHeld/CtrlHeld/AltHeld defaults so existing CAPSLOCK writes (3-arg) continue unchanged"
metrics:
  duration: "~2 minutes"
  completed: "2026-03-01"
  tasks_completed: 2
  files_modified: 3
---

# Phase 7 Plan 01: Hotkey Wiring — Direction Key Interception Summary

Direction key interception and suppression added to the WH_KEYBOARD_LL keyboard hook: 8 keys (Up/Down/Left/Right + WASD) are suppressed while CAPSLOCK is held, passed through normally when not held, with verbose logging of direction name and modifier context.

## What Was Built

**KeyEvent.cs** — Extended record struct with optional modifier fields:
- Added `ShiftHeld`, `CtrlHeld`, `AltHeld` parameters with defaults of `false`
- Existing 3-argument CAPSLOCK writes (`new KeyEvent(vkCode, isKeyDown, time)`) continue to compile unchanged

**KeyboardHookHandler.cs** — Direction key interception in WH_KEYBOARD_LL hook:
- Added VK constants: `VK_LEFT=0x25`, `VK_UP=0x26`, `VK_RIGHT=0x27`, `VK_DOWN=0x28`, `VK_W=0x57`, `VK_A=0x41`, `VK_S=0x53`, `VK_D=0x44`
- Added `private bool _capsLockHeld` field updated on each CAPSLOCK keydown/keyup event
- Added `private static bool IsDirectionKey(uint)` using switch expression
- Direction key interception block runs before the CAPSLOCK check:
  - If `_capsLockHeld == false`: `CallNextHookEx` (pass through — HOTKEY-04)
  - If `_capsLockHeld == true`: reads Shift/Ctrl/Alt state, `TryWrite` to channel, `return (LRESULT)1` to suppress (HOTKEY-03)
- CAPSLOCK handling unchanged except `_capsLockHeld` is now set before `TryWrite`

**CapsLockMonitor.cs** — Direction key event consumption with verbose logging:
- Added `GetDirectionName(uint)` mapping VK codes to "up"/"down"/"left"/"right"
- Added `GetKeyDisplayName(uint)` for human-readable key names in log ("W", "Left", etc.)
- Added `BuildModifierPrefix(KeyEvent)` producing "Ctrl+Alt+Shift+" style prefix
- Added `private readonly HashSet<uint> _directionKeysHeld` for repeat suppression
- Added `private readonly Action<string>? _onDirectionKeyDown` callback (Phase 8 hook point)
- `HandleDirectionKeyEvent()`: on keydown — checks repeat, logs, invokes callback; on keyup — removes from held set
- `ResetState()` now also clears `_directionKeysHeld` to prevent stuck keys after sleep/wake
- `HandleCapsLockEvent()` extracted from `RunAsync` loop for clarity (behavior unchanged)

## Verification

Build result: `Build succeeded. 0 Error(s)` (1 pre-existing WFAC010 warning about DPI manifest, unrelated to this plan).

Manual verification required (runtime) — see plan verification section:
- Hold CAPSLOCK, press W/arrows: keys suppressed, verbose log shows "Direction: W -> up" etc.
- Hold direction key while CAPSLOCK held: only one log entry (repeat suppressed)
- Press W/arrows without CAPSLOCK: pass through to focused app

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Functionality] KeyEvent.cs updated in Task 1 scope**

- **Found during:** Task 1 implementation
- **Issue:** Task 1 needed `new KeyEvent(vkCode, isKeyDown, time, shiftHeld, ctrlHeld, altHeld)` but the KeyEvent struct only had 3 parameters. The plan lists KeyEvent.cs under Task 2 files but the new constructor signature was required for Task 1 to compile.
- **Fix:** Updated KeyEvent.cs before the Task 1 build verification, adding optional ShiftHeld/CtrlHeld/AltHeld parameters with defaults of false. The 3-argument CAPSLOCK writes in KeyboardHookHandler.cs continue to work unchanged.
- **Files modified:** `focus/Windows/Daemon/KeyEvent.cs`
- **Commit:** fde7bc0 (included in Task 1 commit)

## Self-Check: PASSED

| Item | Status |
|------|--------|
| focus/Windows/Daemon/KeyboardHookHandler.cs | FOUND |
| focus/Windows/Daemon/CapsLockMonitor.cs | FOUND |
| focus/Windows/Daemon/KeyEvent.cs | FOUND |
| .planning/phases/07-hotkey-wiring/07-01-SUMMARY.md | FOUND |
| Commit fde7bc0 (Task 1) | FOUND |
| Commit 4b4ccbf (Task 2) | FOUND |
