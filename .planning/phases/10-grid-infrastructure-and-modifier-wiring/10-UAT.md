---
status: diagnosed
phase: 10-grid-infrastructure-and-modifier-wiring
source: 10-01-SUMMARY.md, 10-02-SUMMARY.md
started: 2026-03-02T15:10:00Z
updated: 2026-03-02T15:15:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Existing CAPS+Direction Navigation
expected: With CapsLock toggled on (entering overlay mode), pressing arrow keys still navigates between windows as before. No regression from Phase 10 changes.
result: pass

### 2. CAPS+TAB Suppression
expected: While in CapsLock overlay mode, pressing TAB does NOT switch focus to another control/window (TAB is suppressed). The key press is swallowed by the hook.
result: pass

### 3. Bare TAB Passthrough
expected: When CapsLock mode is NOT active (overlay not showing), pressing TAB behaves normally — standard tab navigation in whatever app has focus.
result: pass

### 4. Mode Detection in Verbose Output
expected: Run the daemon with --verbose. While in CapsLock mode: (a) press a direction key with no TAB held — logs show "Navigate" mode. (b) Hold TAB then press a direction key — logs show "Move" mode or "unimplemented mode" no-op message. (c) Hold TAB+LShift then press direction — logs show "Grow". (d) Hold TAB+LCtrl then press direction — logs show "Shrink".
result: issue
reported: "It works, but spams TAB held -> Move mode on every key repeat event while TAB is held down"
severity: minor

### 5. No Stuck Move Mode on CAPS Release
expected: While in CapsLock mode, hold TAB (entering Move mode), then release CapsLock before releasing TAB. Next time you enter CapsLock mode and press a direction key without TAB, it should be Navigate mode — TAB state was reset when CapsLock was released.
result: pass

## Summary

total: 5
passed: 4
issues: 1
pending: 0
skipped: 0

## Gaps

- truth: "TAB held log should not spam on key repeat"
  status: failed
  reason: "User reported: It works, but spams TAB held -> Move mode on every key repeat event while TAB is held down"
  severity: minor
  test: 4
  root_cause: "CapsLockMonitor TAB handler logs on every WM_KEYDOWN repeat with no guard — unlike CAPSLOCK and direction handlers which suppress repeats"
  artifacts:
    - path: "focus/Windows/Daemon/CapsLockMonitor.cs"
      issue: "TAB keydown block (lines 123-136) missing repeat guard — logs on every auto-repeat cycle"
  missing:
    - "Add _tabHeld bool field to CapsLockMonitor, guard log with if (!_tabHeld), clear on key-up"
  debug_session: ".planning/debug/tab-held-log-spam.md"
