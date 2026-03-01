---
phase: 04-daemon-core
plan: 01
subsystem: daemon
tags: [cswin32, pinvoke, keyboard-hook, wh-keyboard-ll, system-threading-channels, winforms, mutex]

# Dependency graph
requires: []
provides:
  - "KeyEvent readonly record struct for zero-allocation Channel<KeyEvent> writes"
  - "KeyboardHookHandler with WH_KEYBOARD_LL hook, LLKHF_INJECTED filter, and CAPSLOCK suppression"
  - "CapsLockMonitor with hold/release state machine, async foreach consumer, verbose logging"
  - "DaemonMutex with AcquireOrReplace replace semantics (kill existing, not error)"
affects: [04-02, 05-overlay-rendering, 06-overlay-wiring]

# Tech tracking
tech-stack:
  added:
    - "System.Windows.Forms (UseWindowsForms=true) — WinForms for message pump and NotifyIcon in Plan 02"
    - "net8.0-windows TargetFramework — required for WinForms"
    - "CsWin32: SetWindowsHookEx, UnhookWindowsHookEx, CallNextHookEx, GetModuleHandle, FreeConsole, GetKeyState, KBDLLHOOKSTRUCT"
  patterns:
    - "Static HOOKPROC field pattern — prevents GC collection of unmanaged delegate reference"
    - "FreeLibrarySafeHandle(handle, false) — wraps GetModuleHandle result without taking FreeLibrary ownership"
    - "Channel<KeyEvent> producer/consumer — TryWrite in hook callback, ReadAllAsync on worker thread"
    - "Replace semantics mutex — AcquireOrReplace kills existing process, not error-and-exit"

key-files:
  created:
    - "focus/Windows/Daemon/KeyEvent.cs — readonly record struct VkCode/IsKeyDown/Timestamp"
    - "focus/Windows/Daemon/DaemonMutex.cs — Global\\focus-daemon mutex with AcquireOrReplace/Release"
    - "focus/Windows/Daemon/KeyboardHookHandler.cs — WH_KEYBOARD_LL Install/Uninstall/HookCallback/Dispose"
    - "focus/Windows/Daemon/CapsLockMonitor.cs — RunAsync/OnCapsLockHeld/OnCapsLockReleased/ResetState"
  modified:
    - "focus/focus.csproj — added UseWindowsForms=true, changed TargetFramework to net8.0-windows"
    - "focus/NativeMethods.txt — added 7 entries: 6 API names + KBDLLHOOKSTRUCT (30 total)"

key-decisions:
  - "TargetFramework changed to net8.0-windows (from net8.0) — required by NETSDK1136 when UseWindowsForms=true"
  - "KBDLLHOOKSTRUCT added to NativeMethods.txt — CsWin32 does not generate it as a dependency of SetWindowsHookEx alone; must be explicitly listed"
  - "FreeLibrarySafeHandle with ownsHandle=false — GetModuleHandle overload already returns FreeLibrarySafeHandle with false; passing directly"
  - "UnhookWindowsHookExSafeHandle used as _hookHandle type — SetWindowsHookEx with SafeHandle hmod returns this type directly"

patterns-established:
  - "Static delegate field: private static HOOKPROC? s_hookProc — assign before SetWindowsHookEx, null after UnhookWindowsHookEx"
  - "Fire-and-forget Channel write: _channelWriter.TryWrite() — never await in hook callback"
  - "Modifier filter order: injected -> not-CAPSLOCK -> Alt flag -> Ctrl/Shift GetKeyState -> suppress and post"
  - "Stderr verbose logging: Console.Error.WriteLine with [HH:mm:ss.fff] timestamp format"

requirements-completed: [DAEMON-02, DAEMON-04, DAEMON-06]

# Metrics
duration: 3min
completed: 2026-03-01
---

# Phase 04 Plan 01: Daemon Core Components Summary

**WH_KEYBOARD_LL keyboard hook with CAPSLOCK detection/suppression, Channel<KeyEvent> producer/consumer, and single-instance mutex — all four daemon building blocks ready for Plan 02 wiring**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-01T07:31:01Z
- **Completed:** 2026-03-01T07:34:10Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments
- KeyboardHookHandler installs WH_KEYBOARD_LL via CsWin32 with static HOOKPROC field (GC-safe), filters LLKHF_INJECTED events, checks Ctrl/Shift/Alt modifiers, suppresses CAPSLOCK toggle via (LRESULT)1, and TryWrites events to Channel
- CapsLockMonitor consumes Channel with ReadAllAsync, tracks held/released state with repeat-key-down filtering, logs to stderr with millisecond timestamps, and exposes ResetState() for sleep/wake recovery
- DaemonMutex acquires Global\\focus-daemon with replace semantics: kills existing daemon process by name (filtering own PID), then re-creates the mutex
- Project configured for WinForms message pump (UseWindowsForms + net8.0-windows TargetFramework)

## Task Commits

Each task was committed atomically:

1. **Task 1: Project setup, KeyEvent record, and DaemonMutex** - `51ff6f3` (feat)
2. **Task 2: KeyboardHookHandler** - `b251b54` (feat)
3. **Task 3: CapsLockMonitor** - `00690c6` (feat)

## Files Created/Modified
- `focus/focus.csproj` — UseWindowsForms=true, TargetFramework net8.0-windows
- `focus/NativeMethods.txt` — 7 new entries (30 total): SetWindowsHookEx, UnhookWindowsHookEx, CallNextHookEx, GetModuleHandle, FreeConsole, GetKeyState, KBDLLHOOKSTRUCT
- `focus/Windows/Daemon/KeyEvent.cs` — Immutable readonly record struct for Channel writes
- `focus/Windows/Daemon/DaemonMutex.cs` — Single-instance enforcement with kill-existing semantics
- `focus/Windows/Daemon/KeyboardHookHandler.cs` — WH_KEYBOARD_LL hook with all CAPSLOCK filters
- `focus/Windows/Daemon/CapsLockMonitor.cs` — Async Channel consumer with hold/release state machine

## Decisions Made
- TargetFramework changed from net8.0 to net8.0-windows: required by NETSDK1136 error when UseWindowsForms=true; this affects all platform checks in the codebase (now Windows-only build)
- KBDLLHOOKSTRUCT explicitly added to NativeMethods.txt: CsWin32 generates it only when listed directly; it is not auto-generated as a dependency of SetWindowsHookEx
- UnhookWindowsHookExSafeHandle used as _hookHandle field type: the SetWindowsHookEx overload that takes SafeHandle hmod returns UnhookWindowsHookExSafeHandle (not HHOOK), with Dispose() calling UnhookWindowsHookEx

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Changed TargetFramework to net8.0-windows**
- **Found during:** Task 1 (Project setup)
- **Issue:** `dotnet build` failed with NETSDK1136: target platform must be set to Windows when using Windows Forms
- **Fix:** Changed `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net8.0-windows</TargetFramework>`
- **Files modified:** focus/focus.csproj
- **Verification:** `dotnet build` succeeds with 0 errors
- **Committed in:** 51ff6f3 (Task 1 commit)

**2. [Rule 3 - Blocking] Added KBDLLHOOKSTRUCT to NativeMethods.txt**
- **Found during:** Task 2 (KeyboardHookHandler)
- **Issue:** CS0246 error: KBDLLHOOKSTRUCT not found — CsWin32 does not generate it as a transitive dependency of SetWindowsHookEx
- **Fix:** Added KBDLLHOOKSTRUCT as an explicit entry in NativeMethods.txt
- **Files modified:** focus/NativeMethods.txt
- **Verification:** `dotnet build` succeeds with 0 errors; NativeMethods.txt now has 30 entries
- **Committed in:** b251b54 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both auto-fixes were necessary to unblock compilation. No scope creep. NativeMethods.txt now has 30 entries (23 + 7) vs the plan's expected 29 (23 + 6) due to the additional KBDLLHOOKSTRUCT entry.

## Issues Encountered
- CsWin32 type generation is lazy: structs used only via pointer cast (KBDLLHOOKSTRUCT*) do not get generated unless listed in NativeMethods.txt

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All four daemon building blocks are ready: KeyEvent, KeyboardHookHandler, CapsLockMonitor, DaemonMutex
- Plan 02 can wire these into DaemonCommand (System.CommandLine subcommand), TrayIcon (NotifyIcon + ApplicationContext), and the STA thread message pump
- No blockers

---
*Phase: 04-daemon-core*
*Completed: 2026-03-01*
