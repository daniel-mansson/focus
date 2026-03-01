---
phase: 04-daemon-core
plan: 02
subsystem: daemon
tags: [winforms, notifyicon, applicationcontext, sta-thread, system-commandline, wm-powerbroadcast, cancellationtoken, channel]

# Dependency graph
requires:
  - phase: 04-01
    provides: "KeyboardHookHandler, CapsLockMonitor, DaemonMutex, KeyEvent — all four daemon building blocks"
provides:
  - "DaemonApplicationContext: NotifyIcon tray icon, Exit context menu, WM_POWERBROADCAST wake recovery via PowerBroadcastWindow inner NativeWindow"
  - "DaemonCommand.Run: full daemon lifecycle — mutex, channel, STA thread, consumer task, Ctrl+C cancellation, ordered cleanup"
  - "focus daemon CLI subcommand with --background (FreeConsole detach) and --verbose/-v options"
  - "Complete working daemon: focus daemon starts, hooks CAPSLOCK, shows tray icon, replaces prior instance, cleans up on exit"
affects: [05-overlay-rendering, 06-overlay-wiring]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "STA thread + Application.Run(ApplicationContext): message pump on dedicated thread, hook installs in ApplicationContext constructor"
    - "CancellationToken.Register for cross-thread shutdown: cts.Token.Register(() => Application.ExitThread()) ensures STA pump exits on Ctrl+C"
    - "PowerBroadcastWindow inner NativeWindow: CreateHandle(new CreateParams()) + WndProc override for WM_POWERBROADCAST without WndProc on ApplicationContext"
    - "staThread.Join before hook.Uninstall: wait for message pump to exit before cleaning up hooks to avoid use-after-free"

key-files:
  created:
    - "focus/Windows/Daemon/TrayIcon.cs — DaemonApplicationContext with NotifyIcon, Exit handler, and PowerBroadcastWindow for wake recovery"
    - "focus/Windows/Daemon/DaemonCommand.cs — daemon lifecycle orchestrator: mutex, channel, STA thread, consumer task, cleanup"
  modified:
    - "focus/Program.cs — added 'daemon' subcommand with --background and --verbose/-v options calling DaemonCommand.Run"

key-decisions:
  - "Hook installed in DaemonApplicationContext constructor (not after Application.Run): message pump starts immediately after constructor returns, so hook is ready when first messages arrive"
  - "cts.Token.Register(() => Application.ExitThread()) registered after STA thread starts: handles both Ctrl+C path (cancellation triggers ExitThread) and tray Exit path (onExit callback cancels cts, Register also runs)"
  - "Shutdown timeouts reduced from 3s to 500ms (post-verification fix): 3s felt sluggish during human testing; 500ms is sufficient for consumer task and STA join"
  - "FreeConsole called after startup message print: console output may silently fail after detach; user sees confirmation before detach"

patterns-established:
  - "Inner NativeWindow pattern: create private sealed class inheriting NativeWindow, call CreateHandle(new CreateParams()) in constructor, override WndProc — use when ApplicationContext/Form WndProc not available"
  - "Ordered cleanup sequence: staThread.Join -> hook.Uninstall+Dispose -> channel.Writer.Complete -> consumerTask.Wait(500ms) -> mutex.Release"

requirements-completed: [DAEMON-01, DAEMON-05]

# Metrics
duration: 16min
completed: 2026-03-01
---

# Phase 04 Plan 02: Daemon Wiring Summary

**DaemonApplicationContext (NotifyIcon + WM_POWERBROADCAST wake recovery) and DaemonCommand orchestrator wired together into a fully working `focus daemon` CLI command — all 8 verification checks passed by human tester**

## Performance

- **Duration:** 16 min
- **Started:** 2026-03-01T07:37:44Z
- **Completed:** 2026-03-01T07:52:52Z
- **Tasks:** 3 (2 auto + 1 human-verify checkpoint)
- **Files modified:** 3

## Accomplishments
- DaemonApplicationContext wraps Plan 01 components with a NotifyIcon tray icon (SystemIcons.Application, "Focus Daemon" tooltip, Exit context menu) and PowerBroadcastWindow inner NativeWindow that reinstalls the keyboard hook and resets CapsLockMonitor on WM_POWERBROADCAST wake events
- DaemonCommand.Run implements the full daemon lifecycle: acquire single-instance mutex, print startup message, optionally FreeConsole, create Channel<KeyEvent>, instantiate hook and monitor, register CancelKeyPress handler, start consumer task and STA message pump thread, wait for cancellation, then perform ordered cleanup
- focus daemon CLI subcommand added to Program.cs with --background (detach console) and --verbose/-v (stderr event logging) options; human tester verified all 8 success criteria including CAPSLOCK suppression, modifier filtering, single-instance replacement, Ctrl+C, tray Exit, and background mode

## Task Commits

Each task was committed atomically:

1. **Task 1: TrayIcon — DaemonApplicationContext with NotifyIcon and wake recovery** - `ad346ab` (feat)
2. **Task 2: DaemonCommand orchestrator and Program.cs CLI wiring** - `a241afd` (feat)
3. **Task 3: Verify daemon functionality** - APPROVED (human verification — all 8 checks passed)

**Post-verification fix:** `f3e9262` — fix(04-02): reduce shutdown timeouts from 3s to 500ms

## Files Created/Modified
- `focus/Windows/Daemon/TrayIcon.cs` — DaemonApplicationContext: NotifyIcon with tray icon and Exit handler, PowerBroadcastWindow inner NativeWindow for sleep/wake recovery (104 lines)
- `focus/Windows/Daemon/DaemonCommand.cs` — Daemon lifecycle orchestrator: mutex acquire, channel, STA thread, consumer task, cancellation, ordered cleanup (109 lines, updated post-verification)
- `focus/Program.cs` — Added daemon subcommand with --background and --verbose/-v options

## Decisions Made
- Hook installed in DaemonApplicationContext constructor rather than after Application.Run returns: Application.Run blocks until ExitThread is called, so hook must be installed before the call — constructor is the correct place because the message pump starts immediately after the constructor returns
- cts.Token.Register(() => Application.ExitThread()) used for cross-thread STA shutdown: whether cancellation comes from Ctrl+C or tray Exit, this ensures the message pump exits cleanly without needing Invoke or thread synchronization
- Shutdown timeouts reduced from 3s to 500ms after human verification: 3s felt sluggish in practice; 500ms is reliable for both the staThread.Join and consumerTask.Wait given the simple cleanup path

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed Option<bool> alias syntax for System.CommandLine 2.0.3**
- **Found during:** Task 2 (DaemonCommand orchestrator and Program.cs CLI wiring)
- **Issue:** Plan specified `daemonVerboseOption.AddAlias("-v")` but System.CommandLine 2.0.3 API takes aliases as constructor params or via a different method signature — the exact call syntax needed adjustment to compile
- **Fix:** Used correct System.CommandLine 2.0.3 API for adding option aliases so --verbose/-v option compiled successfully
- **Files modified:** focus/Program.cs
- **Verification:** dotnet build succeeds with 0 errors
- **Committed in:** a241afd (Task 2 commit)

**2. [Rule 1 - Bug] Reduced shutdown timeouts from 3s to 500ms (post-verification)**
- **Found during:** Task 3 (human verification)
- **Issue:** 3-second timeouts for staThread.Join and consumerTask.Wait made Ctrl+C feel sluggish during human testing
- **Fix:** Reduced both timeouts to 500ms — sufficient for the simple cleanup path (no blocking I/O in consumer or STA teardown)
- **Files modified:** focus/Windows/Daemon/DaemonCommand.cs
- **Verification:** Human tester confirmed faster shutdown response; clean exit verified
- **Committed in:** f3e9262 (post-verification fix)

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug fix)
**Impact on plan:** Both fixes were necessary for correctness and usability. No scope creep.

## Issues Encountered
- None beyond the two auto-fixed deviations above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 4 success criteria fully met: daemon starts persistently, CAPSLOCK detected and suppressed, single-instance enforcement, clean shutdown via Ctrl+C and tray Exit, background mode, sleep/wake recovery
- Requirements DAEMON-01 and DAEMON-05 complete (joins DAEMON-02, DAEMON-04, DAEMON-06 from Plan 01)
- Phase 5 (Overlay Rendering) can proceed: STA thread + message pump pattern established, UseWindowsForms already enabled, WinForms ApplicationContext ready to host overlay windows
- No blockers

---
*Phase: 04-daemon-core*
*Completed: 2026-03-01*
