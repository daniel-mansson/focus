---
status: resolved
trigger: "TAB held -> Move mode log spams on every key repeat during CapsLock overlay mode"
created: 2026-03-02T00:00:00Z
updated: 2026-03-02T00:00:00Z
---

## Current Focus

hypothesis: confirmed
test: read hook handler and monitor consumer
expecting: log guard missing before _tabHeld is already true
next_action: report root cause (no fix mode)

## Symptoms

expected: "TAB held -> Move mode" logs once on initial TAB keydown
actual: the message logs on every Windows key-repeat event while TAB is held
errors: none (functional behavior is correct)
reproduction: hold CapsLock, then hold TAB — watch verbose stderr output
started: always; no regression

## Eliminated

- hypothesis: hook handler logs the message
  evidence: searched codebase — log is in CapsLockMonitor.cs line 129, not KeyboardHookHandler.cs
  timestamp: 2026-03-02

## Evidence

- timestamp: 2026-03-02
  checked: KeyboardHookHandler.cs lines 122-133
  found: on every WM_KEYDOWN for VK_TAB while CAPS held, handler sets `_tabHeld = isKeyDown`
         and writes a KeyEvent(vkCode=TAB, isKeyDown=true) to the channel unconditionally.
         Windows auto-repeat fires WM_KEYDOWN repeatedly while key is held, so multiple
         KeyEvents with IsKeyDown=true are sent to CapsLockMonitor.
  implication: the channel receives one TAB-down event per repeat cycle

- timestamp: 2026-03-02
  checked: CapsLockMonitor.cs lines 123-136
  found: TAB event handler checks `evt.IsKeyDown` and logs immediately with no guard
         for whether the TAB was already held. Compare with HandleCapsLockEvent (line 151)
         and HandleDirectionKeyEvent (line 173) which both have explicit repeat-suppression
         guards (`if (_isHeld) return` and `if (_directionKeysHeld.Contains(evt.VkCode)) return`).
  implication: TAB handler is the only event handler in CapsLockMonitor that lacks a
               "already held" guard — it logs on every repeated keydown event

## Resolution

root_cause: >
  CapsLockMonitor.cs TAB event handler (lines 126-129) logs "TAB held -> Move mode"
  on every IsKeyDown event with no guard for repeat — every Windows auto-repeat
  WM_KEYDOWN fires a new channel write in KeyboardHookHandler (line 128) and a new
  log line in CapsLockMonitor (line 129), because unlike the CAPSLOCK and direction-key
  handlers there is no "_tabHeld already true" early-return check.

fix: >
  Add a local bool field (e.g. `_tabHeld`) in CapsLockMonitor, mirroring the pattern
  used by `_isHeld` for CAPSLOCK. Before logging, check if `_tabHeld` is already true;
  if so, skip the log. Set `_tabHeld = true` on first keydown log, clear it on keyup.

files_changed: []
