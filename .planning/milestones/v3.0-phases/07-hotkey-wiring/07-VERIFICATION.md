---
phase: 07-hotkey-wiring
verified: 2026-03-01T18:55:00Z
status: passed
score: 6/6 must-haves verified
human_verification:
  - test: "Hold CAPSLOCK, press W/A/S/D and all four arrow keys in a text editor"
    expected: "No character input or cursor movement reaches the focused app for any of the 8 keys"
    why_human: "Suppression of WH_KEYBOARD_LL events can only be confirmed by observing the live app — the code path (return (LRESULT)1) is correct but physical key delivery requires runtime confirmation"
  - test: "Hold a direction key (e.g., W) while CAPSLOCK is held for 2-3 seconds, observe verbose daemon log"
    expected: "Exactly one 'Direction: W -> up' line appears — no additional lines for key-repeat events"
    why_human: "Key-repeat suppression via _directionKeysHeld is a runtime state-machine behavior; correctness under OS auto-repeat timing requires human observation"
  - test: "Release CAPSLOCK, type W/A/S/D and press all four arrow keys in a text editor"
    expected: "All 8 keys pass through normally — characters appear, cursor moves"
    why_human: "Passthrough branch (CallNextHookEx when _capsLockHeld == false) requires runtime confirmation that the key actually arrives at the focused app"
  - test: "Hold CAPSLOCK + Shift + Left, then CAPSLOCK + Ctrl + A — check verbose log and editor"
    expected: "Log shows 'Direction: Shift+Left -> left' and 'Direction: Ctrl+A -> left'; no text selection or select-all in the editor"
    why_human: "Modifier-combo suppression is correct per code (direction keys are suppressed regardless of modifiers), but UI confirmation that Shift+Left does not select text requires runtime"
  - test: "Verify overlay borders appear on CAPSLOCK hold and disappear on release after this phase"
    expected: "Overlay show/hide behavior is unchanged from pre-Phase 7 behavior"
    why_human: "OnDirectionKeyDown is a no-op; overlay behavior depends on OnCapsLockHeld/OnCapsLockReleased callbacks which are unchanged — but end-to-end regression requires human eye"
---

# Phase 7: Hotkey Wiring Verification Report

**Phase Goal:** Direction keys (arrows and WASD) are intercepted and suppressed by the daemon when CAPSLOCK is held, and pass through normally when it is not
**Verified:** 2026-03-01T18:55:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Arrow keys (Up/Down/Left/Right) pressed while CAPSLOCK is held are suppressed | ? HUMAN NEEDED | Code: `IsDirectionKey` catches VK_LEFT/UP/RIGHT/DOWN; `_capsLockHeld == true` branch calls `return (LRESULT)1` (line 132, KeyboardHookHandler.cs). Runtime confirmation required. |
| 2 | WASD keys pressed while CAPSLOCK is held are suppressed | ? HUMAN NEEDED | Code: `IsDirectionKey` catches VK_W/A/S/D; same suppression branch. Runtime confirmation required. |
| 3 | Arrow keys and WASD keys pass through normally when CAPSLOCK is NOT held | ? HUMAN NEEDED | Code: `if (!_capsLockHeld) return CallNextHookEx(...)` (line 113-117, KeyboardHookHandler.cs). Runtime confirmation required. |
| 4 | Direction keys pressed with additional modifiers while CAPSLOCK is held are still suppressed | ? HUMAN NEEDED | Code: modifier flags are read only for logging; the suppression `return (LRESULT)1` is unconditional for direction keys when `_capsLockHeld == true`. Runtime confirmation required. |
| 5 | Only the initial keydown of a direction key is posted to the channel; key-repeat events are suppressed silently | ? HUMAN NEEDED | Code: `_directionKeysHeld.Contains(evt.VkCode)` guard in `HandleDirectionKeyEvent` (line 138, CapsLockMonitor.cs) skips repeats silently. Runtime confirmation required. |
| 6 | Verbose daemon log reports each intercepted direction key with its mapped direction name | VERIFIED | `Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Direction: {modifierPrefix}{keyName} -> {directionName}")` at CapsLockMonitor.cs line 147-148. Format matches spec exactly. |

**Score:** 6/6 truths structurally verified (5 require human runtime confirmation for full confidence)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `focus/Windows/Daemon/KeyEvent.cs` | Extended record struct with VkCode, IsKeyDown, Timestamp, ShiftHeld, CtrlHeld, AltHeld | VERIFIED | All 6 fields present with correct defaults (ShiftHeld/CtrlHeld/AltHeld = false). Existing 3-arg writes remain compatible. |
| `focus/Windows/Daemon/KeyboardHookHandler.cs` | Direction key interception, suppression, and event posting when CAPSLOCK held — contains all 8 VK codes | VERIFIED | VK_LEFT/UP/RIGHT/DOWN/W/A/S/D all defined (lines 31-40). `IsDirectionKey()` switch covers all 8 (lines 91-96). `_capsLockHeld` field updated on CAPSLOCK events (line 154). Direction intercept block at lines 111-133. |
| `focus/Windows/Daemon/CapsLockMonitor.cs` | Direction key event consumption with verbose logging including direction names | VERIFIED | `GetDirectionName()`, `BuildModifierPrefix()`, `GetKeyDisplayName()`, `HandleDirectionKeyEvent()`, `_directionKeysHeld` HashSet, `_onDirectionKeyDown` callback — all present. `ResetState()` clears both `_isHeld` and `_directionKeysHeld`. |
| `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` | OnDirectionKeyDown hook point for Phase 8 | VERIFIED | `public void OnDirectionKeyDown(string direction)` at lines 112-116. Correctly documented as Phase 7 no-op / Phase 8 hook point. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `KeyboardHookHandler.cs` | `Channel<KeyEvent>` | `_channelWriter.TryWrite(new KeyEvent(...))` for direction keys when CAPSLOCK held | VERIFIED | Line 129: `_channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, shiftHeld, ctrlHeld, altHeld))` — inside the `_capsLockHeld == true` branch of the direction key block |
| `CapsLockMonitor.cs` | `KeyEvent.VkCode` direction name mapping | `GetDirectionName(evt.VkCode)` returns non-null for direction VK codes | VERIFIED | Line 100: `string? directionName = GetDirectionName(evt.VkCode)` used in `RunAsync` loop to dispatch to `HandleDirectionKeyEvent` |
| `CapsLockMonitor.cs` | `OverlayOrchestrator.cs` | `onDirectionKeyDown` callback closure in DaemonCommand.Run | VERIFIED | DaemonCommand.cs line 67: `onDirectionKeyDown: (dir) => orchestrator?.OnDirectionKeyDown(dir)` — follows identical null-conditional late-binding pattern as `onHeld`/`onReleased` |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| HOTKEY-01 | 07-01, 07-02 | Daemon detects arrow key presses while CAPSLOCK is held | SATISFIED | VK_LEFT/UP/RIGHT/DOWN intercepted in `IsDirectionKey()`, events posted to channel, direction names mapped in `GetDirectionName()` |
| HOTKEY-02 | 07-01, 07-02 | Daemon detects WASD key presses while CAPSLOCK is held | SATISFIED | VK_W/A/S/D intercepted in `IsDirectionKey()`, mapped to "up"/"left"/"down"/"right" in `GetDirectionName()` |
| HOTKEY-03 | 07-01, 07-02 | Direction keys suppressed from reaching focused app while CAPSLOCK is held | SATISFIED (runtime pending) | `return (LRESULT)1` on line 132 of KeyboardHookHandler.cs — unconditional suppression when `_capsLockHeld == true` for any direction key |
| HOTKEY-04 | 07-01, 07-02 | Direction keys pass through normally when CAPSLOCK is not held | SATISFIED (runtime pending) | `return PInvoke.CallNextHookEx(null, nCode, wParam, lParam)` on line 116 — passthrough branch when `_capsLockHeld == false` |

All 4 HOTKEY requirements are structurally satisfied. HOTKEY-03 and HOTKEY-04 require human runtime confirmation as they depend on OS-level key delivery behavior.

No orphaned requirements: REQUIREMENTS.md assigns HOTKEY-01 through HOTKEY-04 exclusively to Phase 7, and both plans claim all four IDs.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `OverlayOrchestrator.cs` | 112-116 | `OnDirectionKeyDown` is a no-op method body | INFO | Intentional: documented Phase 7 / Phase 8 hook point. The suppression goal is fully achieved in `KeyboardHookHandler` — `OverlayOrchestrator` receiving the event is wiring for Phase 8 navigation, not required for Phase 7 goal. |

No blocker or warning anti-patterns found. The no-op `OnDirectionKeyDown` is the correct design for Phase 7: key interception and suppression does not require orchestrator action.

### Human Verification Required

#### 1. Direction Key Suppression (HOTKEY-01, HOTKEY-02, HOTKEY-03)

**Test:** Run `dotnet run --project focus -- daemon --verbose`, open a text editor, hold CAPSLOCK, press each of W/A/S/D/Up/Down/Left/Right.
**Expected:** No characters appear in the editor, cursor does not move. Verbose log shows one line per key press: e.g., `[HH:mm:ss.fff] Direction: W -> up`.
**Why human:** OS key delivery via WH_KEYBOARD_LL suppression (`return (LRESULT)1`) is correct per code but requires runtime confirmation that the key does not reach the focused app. Automated grep cannot simulate this.

#### 2. Key Repeat Suppression

**Test:** Hold CAPSLOCK, then press and hold W for 2-3 seconds. Observe verbose log line count.
**Expected:** Exactly one `Direction: W -> up` line appears regardless of how long W is held.
**Why human:** The `_directionKeysHeld` HashSet guard is a runtime state machine. OS auto-repeat event timing means the repeat count is only observable during live execution.

#### 3. Passthrough When CAPSLOCK Not Held (HOTKEY-04)

**Test:** With CAPSLOCK released, type W/A/S/D and press arrow keys in a text editor.
**Expected:** Characters appear normally; cursor moves normally.
**Why human:** The `CallNextHookEx` passthrough branch is correct per code, but confirmation that the key actually arrives at the focused application requires runtime observation.

#### 4. Modifier Combo Suppression

**Test:** Hold CAPSLOCK + Shift + Left arrow. Then hold CAPSLOCK + Ctrl + A.
**Expected:** Verbose log shows `Direction: Shift+Left -> left` and `Direction: Ctrl+A -> left`. No text is selected in the editor (Shift+Left normally selects), Ctrl+A does not select-all (A is intercepted as a direction key).
**Why human:** The modifier log format can be verified in code (present), but the side-effect (no text selection) requires a live editor.

#### 5. Overlay Regression Check

**Test:** Hold CAPSLOCK — observe overlay borders appear on candidate windows. Release CAPSLOCK — observe overlay borders disappear.
**Expected:** Overlay show/hide behavior is unchanged from before Phase 7.
**Why human:** `OnDirectionKeyDown` is a no-op; the overlay callbacks (`onHeld`/`onReleased`) are structurally unchanged. However, a regression to the visual overlay experience requires a human to confirm no interference from the new callback wiring.

### Gaps Summary

No gaps. All automated checks pass:

- Build: 0 errors (1 pre-existing WFAC010 DPI warning, unrelated to this phase)
- All 4 required artifacts are substantive and wired
- All 3 key links verified with exact pattern matches in source
- All 4 HOTKEY requirements are satisfied by implementation evidence
- No blocker anti-patterns
- Commits fde7bc0, 4b4ccbf, 0768513 present in git log

5 of 6 truths require human runtime confirmation — the code is structurally complete and correct, but the phase goal ("keys are suppressed / pass through") is a behavioral guarantee that only a running daemon can demonstrate. This matches the plan's own design (Plan 02, Task 2 is a `checkpoint:human-verify` gate).

The Plan 02 SUMMARY states human tester approved all 5 test scenarios. If that approval is taken as sufficient, the phase is fully complete. These items are surfaced here for the record.

---

_Verified: 2026-03-01T18:55:00Z_
_Verifier: Claude (gsd-verifier)_
