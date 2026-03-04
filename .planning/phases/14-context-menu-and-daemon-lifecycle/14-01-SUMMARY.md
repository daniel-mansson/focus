---
phase: 14-context-menu-and-daemon-lifecycle
plan: 01
subsystem: ui
tags: [winforms, tray-icon, system-tray, context-menu, daemon-lifecycle, csharp]

# Dependency graph
requires:
  - phase: 13-tray-identity
    provides: Custom focus-bracket tray icon embedded as EmbeddedResource in focus.ico
provides:
  - DaemonStatus class with FormatUptime/FormatLastAction for tray menu display
  - KeyboardHookHandler.IsInstalled property derived from handle validity
  - Three-group tray context menu with live status labels, settings, restart, and exit
  - Last-action recording from navigation pipeline into DaemonStatus
affects:
  - 14-02 and beyond: DaemonStatus wired through the system; constructor signatures updated

# Tech tracking
tech-stack:
  added: []
  patterns:
    - DaemonStatus as plain mutable STA-thread-only state holder (no locking needed)
    - Hook status derived dynamically from handle validity (avoids staleness after sleep/wake)
    - ContextMenuStrip.Opening event for live label refresh on every open

key-files:
  created:
    - focus/Windows/Daemon/DaemonStatus.cs
  modified:
    - focus/Windows/Daemon/KeyboardHookHandler.cs
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
    - focus/Windows/Daemon/DaemonCommand.cs
    - focus/Windows/Daemon/TrayIcon.cs

key-decisions:
  - "DaemonStatus is a plain mutable class with no locking — all reads/writes on STA thread"
  - "IsInstalled derives from _hookHandle validity rather than a bool field to avoid staleness"
  - "ContextMenuStrip.Opening event refreshes labels on every open (no stale data)"
  - "OnRestartClicked: no confirmation dialog, inherits --background and --verbose flags"
  - "Restart failure surfaces in LastAction field, keeps current daemon alive"
  - "Settings handler writes defaults if config missing before opening in default editor"

patterns-established:
  - "Status holder pattern: plain class with formatted string methods for tray display"
  - "Live hook status: derive from handle validity in IsInstalled property, not cached bool"
  - "Ghost icon prevention: set _trayIcon.Visible = false before exit/restart"

requirements-completed: [MENU-01, MENU-02, MENU-03, MENU-04, MENU-05, LIFE-01, LIFE-02, LIFE-03]

# Metrics
duration: 8min
completed: 2026-03-04
---

# Phase 14 Plan 01: Context Menu and Daemon Lifecycle Summary

**Three-group tray context menu with live hook/uptime/last-action status labels, Settings opener, and Restart with inherited flag forwarding via DaemonStatus wiring through the navigation pipeline**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-04T10:18:56Z
- **Completed:** 2026-03-04T10:26:00Z
- **Tasks:** 2
- **Files modified:** 5 (4 modified + 1 created)

## Accomplishments
- Created `DaemonStatus` class with `FormatUptime()` and `FormatLastAction()` for tray display
- Added `KeyboardHookHandler.IsInstalled` property that derives hook status from handle validity, eliminating staleness after sleep/wake
- Wired `DaemonStatus` through `OverlayOrchestrator` to record last action from both `NavigateSta` and `ActivateByNumberSta`
- Expanded tray context menu from a single Exit item to a three-group menu with live status labels, Settings, and Restart Daemon

## Task Commits

Each task was committed atomically:

1. **Task 1: Create DaemonStatus, add IsInstalled, wire into OverlayOrchestrator and DaemonCommand** - `5735ca4` (feat)
2. **Task 2: Expand tray context menu with status labels, restart, and settings handlers** - `c135a62` (feat)

## Files Created/Modified
- `focus/Windows/Daemon/DaemonStatus.cs` - New: plain mutable state holder with StartTime, LastAction, FormatUptime, FormatLastAction
- `focus/Windows/Daemon/KeyboardHookHandler.cs` - Added `IsInstalled` property derived from `_hookHandle` validity
- `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` - Added `DaemonStatus _status` field; records last action in NavigateSta and ActivateByNumberSta
- `focus/Windows/Daemon/DaemonCommand.cs` - Creates `DaemonStatus` before STA thread; passes `background` and `status` to `DaemonApplicationContext`
- `focus/Windows/Daemon/TrayIcon.cs` - Updated constructor signature; three-group menu with ToolStripLabels, Opening event, OnSettingsClicked, OnRestartClicked

## Decisions Made
- `DaemonStatus` is a plain mutable class with no locking — all reads and writes occur on the STA thread (confirmed by plan research)
- Hook status uses `IsInstalled` (derived from handle validity) rather than a cached bool in `DaemonStatus` to avoid staleness after sleep/wake reinstalls (Pitfall 6 from research)
- `ContextMenuStrip.Opening` event refreshes all three labels on every menu open, ensuring no stale data is ever displayed
- Restart: no confirmation dialog, inherits `--background` and `--verbose` flags from current process state
- Restart failure: surfaces error message in `LastAction`, keeps current daemon alive (locked decision)
- Settings: writes defaults if config file missing before opening in system default editor

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Killed running daemon before build**
- **Found during:** Task 2 (build verification)
- **Issue:** `focus.exe` was running and held a file lock on the output binary (MSB3027 error), preventing the build from copying the updated EXE
- **Fix:** Killed the running `focus.exe` process (PID 18148) via `taskkill //IM focus.exe //F`
- **Files modified:** None (operational fix)
- **Verification:** Build succeeded with 0 errors and 0 warnings after process termination
- **Committed in:** Not committed (operational step only)

---

**Total deviations:** 1 auto-fixed (1 blocking - pre-existing operational issue documented in STATE.md)
**Impact on plan:** Operational fix only, no scope change or code modification.

## Issues Encountered
- Daemon was running during build verification, causing the known MSB3027 file-lock error documented in STATE.md blockers. Resolved by terminating the running daemon before rebuild.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Context menu fully functional with live status labels, settings access, and restart capability
- DaemonStatus is wired through the full navigation pipeline and ready for further extension
- No blockers for Phase 14 subsequent plans

---
*Phase: 14-context-menu-and-daemon-lifecycle*
*Completed: 2026-03-04*
