---
phase: 17-task-scheduler-integration
plan: "01"
subsystem: installer
tags: [inno-setup, pascal-script, task-scheduler, schtasks, windows, csharp]

# Dependency graph
requires:
  - phase: 16-build-pipeline-installer
    provides: installer/focus.iss with PrivilegesRequired=lowest, AppMutex, [Files]/[Icons]/[Run] sections

provides:
  - Task Scheduler integration in Inno Setup installer with custom Startup Options wizard page
  - FocusDaemon scheduled task created via schtasks XML import with LogonTrigger and PT0S ExecutionTimeLimit
  - Upgrade detection pre-populating checkbox state from existing task RunLevel
  - Uninstall cleanup via [UninstallRun] and CurUninstallStepChanged with runas fallback
  - installer/test-scheduler.ps1 verification script
  - Removal of ElevateOnStartup from FocusConfig.cs, DaemonCommand.cs, SettingsForm.cs

affects:
  - 18-settings-runtime-task-management
  - any phase touching installer/focus.iss

# Tech tracking
tech-stack:
  added: [schtasks.exe XML import via Inno Setup Pascal Script]
  patterns:
    - ShellExec runas for UAC elevation of individual schtasks commands
    - SaveStringToFile for Task Scheduler XML template with dynamic RunLevel
    - DetectExistingTask using cmd.exe stdout redirect to temp file for XML parsing
    - LoadStringFromFile with AnsiString (not String) for Inno Setup 6 compatibility
    - Post-install launch via schtasks /Run to respect scheduled task RunLevel

key-files:
  created:
    - installer/test-scheduler.ps1
  modified:
    - installer/focus.iss
    - installer/build.ps1
    - focus/Windows/FocusConfig.cs
    - focus/Windows/Daemon/DaemonCommand.cs
    - focus/Windows/Daemon/SettingsForm.cs

key-decisions:
  - "Always use ShellExec runas for schtasks /Create -- ONLOGON tasks require admin even for LeastPrivilege RunLevel"
  - "Use XML import (not CLI flags) for task creation to set ExecutionTimeLimit=PT0S -- no CLI flag exists for this"
  - "Post-install launch via schtasks /Run instead of direct exe -- respects HighestAvailable RunLevel for elevation"
  - "LoadStringFromFile requires AnsiString parameter in Inno Setup 6 (String causes type mismatch at compile)"
  - "Added --background flag to task XML arguments to suppress console window on logon launch"
  - "ElevateOnStartup removed from C# codebase -- Task Scheduler RunLevel replaces in-app self-elevation"

patterns-established:
  - "Pattern: Task creation always via ShellExec runas for schtasks -- non-admin installer cannot create ONLOGON tasks directly"
  - "Pattern: Upgrade detection reads existing task XML via cmd /C schtasks /Query /XML redirect, checks for HighestAvailable string"
  - "Pattern: Dual-path delete (Exec then ShellExec runas on failure) for tasks that may have been created elevated"
  - "Pattern: Post-install daemon launch via schtasks /Run so it runs under the task's configured RunLevel"

requirements-completed: [SCHED-01, SCHED-02, SCHED-03]

# Metrics
duration: ~45min
completed: 2026-03-07
---

# Phase 17 Plan 01: Task Scheduler Integration Summary

**Inno Setup installer extended with Task Scheduler integration: custom Startup Options wizard page, schtasks XML import for ONLOGON task creation with configurable RunLevel and PT0S ExecutionTimeLimit, upgrade detection via XML query, and removal of the obsolete in-app ElevateOnStartup self-elevation mechanism from C# source -- verified via full install/uninstall lifecycle test.**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-03-07T07:45:13Z
- **Completed:** 2026-03-07
- **Tasks:** 3 of 3 complete (2 auto + 1 human-verify checkpoint, approved)
- **Files modified:** 6

## Accomplishments

- Added `[UninstallRun]` section and full `[Code]` Pascal Script block to `installer/focus.iss` with five functions: `BuildTaskXml`, `DetectExistingTask`, `InitializeWizard`, `CurStepChanged`, and `CurUninstallStepChanged`
- Created `installer/test-scheduler.ps1` with automated checks covering task existence, ExecutionTimeLimit, LogonTrigger, RunLevel, Command path, Arguments, LogonType, and MultipleInstancesPolicy
- Removed `ElevateOnStartup` from all three C# files (`FocusConfig.cs`, `DaemonCommand.cs`, `SettingsForm.cs`); project compiles cleanly
- Verified full install/uninstall lifecycle: Startup Options page appears, task created with correct trigger and RunLevel, upgrade detection pre-populates checkboxes, uninstall removes task
- Updated `installer/build.ps1` with ISCC.exe detection and actionable install instructions

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Task Scheduler integration to Inno Setup installer** - `ab431e5` (feat)
2. **Task 2: Remove obsolete ElevateOnStartup from C# source** - `a611dbb` (feat)
3. **Task 3: Verify full install/uninstall lifecycle (checkpoint + fixes)** - `5f079d4` (fix)

## Files Created/Modified

- `installer/focus.iss` - Added [UninstallRun] section and [Code] Pascal Script with full Task Scheduler integration; post-install launch changed to schtasks /Run; static [Run] section removed
- `installer/test-scheduler.ps1` - New verification script for post-install task state
- `installer/build.ps1` - Added ISCC.exe detection with download instructions
- `focus/Windows/FocusConfig.cs` - Removed ElevateOnStartup property
- `focus/Windows/Daemon/DaemonCommand.cs` - Removed self-elevate block and elevateOnStartup verbose logging
- `focus/Windows/Daemon/SettingsForm.cs` - Removed _elevateCheck field, BuildAdvancedGroup() method, ElevateOnStartup save line; adjusted form height to 700

## Decisions Made

- Used `ShellExec('runas', 'schtasks.exe', ...)` for task creation in non-admin install mode -- ONLOGON tasks require admin regardless of RunLevel. The installer runs as lowest-privilege per Phase 16 design, so only the schtasks command gets UAC-elevated.
- Used XML import approach for task creation rather than CLI flags -- `ExecutionTimeLimit=PT0S` cannot be set via schtasks command-line flags; XML is the only way.
- Changed post-install launch from direct exe invocation to `schtasks /Run /TN FocusDaemon` so the daemon starts under the task's configured RunLevel -- critical when HighestAvailable is selected.
- Added `--background` to task XML Arguments to suppress the console window that would otherwise flash briefly on logon.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] LoadStringFromFile AnsiString type mismatch in Inno Setup 6**
- **Found during:** Task 3 (verification -- installer compilation)
- **Issue:** Inno Setup 6 requires the second parameter of `LoadStringFromFile` to be `AnsiString`, not `String`. Plan's DetectExistingTask used a `String` variable, causing a compile-time type mismatch.
- **Fix:** Changed Content variable declaration from `String` to `AnsiString` in DetectExistingTask
- **Files modified:** installer/focus.iss
- **Verification:** ISCC.exe compiled without errors after fix
- **Committed in:** 5f079d4

**2. [Rule 1 - Bug] Post-install launch used direct exe instead of schtasks /Run**
- **Found during:** Task 3 (verification -- elevation behavior)
- **Issue:** CurStepChanged launched focus.exe directly after install, which ignores the scheduled task's RunLevel. When "Run elevated" was selected, the daemon still launched without elevation.
- **Fix:** Replaced direct Exec of focus.exe with `ShellExec('runas', 'schtasks.exe', '/Run /TN "FocusDaemon"', ...)` so the task runs under its configured RunLevel
- **Files modified:** installer/focus.iss
- **Verification:** Post-install daemon launches elevated when Run elevated checkbox was checked
- **Committed in:** 5f079d4

**3. [Rule 2 - Missing Critical] Added --background flag to suppress console window at logon**
- **Found during:** Task 3 (verification -- visual observation)
- **Issue:** Daemon launched at logon via scheduled task was showing a console window briefly on each login
- **Fix:** Added `--background` to the Arguments element in BuildTaskXml so the daemon suppresses its console window when launched by the scheduler
- **Files modified:** installer/focus.iss
- **Verification:** No console window visible during logon launch
- **Committed in:** 5f079d4

**4. [Rule 1 - Bug] Removed static [Run] section; launch moved entirely to CurStepChanged**
- **Found during:** Task 3 (verification)
- **Issue:** Static [Run] section entry for post-install launch conflicted with dynamic CurStepChanged logic, causing double-launch attempts
- **Fix:** Removed the static [Run] section entry; all post-install launch logic consolidated in CurStepChanged ssPostInstall
- **Files modified:** installer/focus.iss
- **Verification:** Single daemon launch after install, no duplicate start
- **Committed in:** 5f079d4

**5. [Rule 2 - Missing Critical] Added RunOnceId to [UninstallRun] entry**
- **Found during:** Task 3 (verification -- ISCC compiler warnings)
- **Issue:** [UninstallRun] schtasks entry lacked RunOnceId, triggering an Inno Setup compiler warning
- **Fix:** Added `RunOnceId: "DeleteFocusDaemonTask"` to the [UninstallRun] entry
- **Files modified:** installer/focus.iss
- **Verification:** ISCC compiles without warnings
- **Committed in:** 5f079d4

**6. [Rule 2 - Missing Critical] Added ISCC.exe detection to build.ps1**
- **Found during:** Task 3 (verification -- build script testing)
- **Issue:** build.ps1 would fail with an unhelpful error if ISCC.exe (the Inno Setup compiler) was not on PATH
- **Fix:** Added Get-Command check for ISCC.exe with a clear error message and download link
- **Files modified:** installer/build.ps1
- **Verification:** build.ps1 gives actionable error message when ISCC.exe not found
- **Committed in:** 5f079d4

---

**Total deviations:** 6 auto-fixed (2 bugs, 4 missing critical)
**Impact on plan:** All fixes were necessary for correctness and build reliability. No scope creep.

## Issues Encountered

- Inno Setup 6 `LoadStringFromFile` requires `AnsiString` (not `String`) for the output buffer -- differs from examples in older documentation. Fixed by correcting the variable type.
- ONLOGON trigger task creation requires admin even for LeastPrivilege RunLevel -- confirmed the Phase 17 research finding. ShellExec runas handles this correctly.
- `dotnet build` had a file-lock warning during Task 2 (daemon was running), but C# compilation itself had zero errors.

## User Setup Required

None - no external service configuration required. Installer handles all Task Scheduler setup automatically.

## Next Phase Readiness

- Task Scheduler integration is complete and verified via full install/uninstall lifecycle test
- FocusDaemon scheduled task creates correctly with ONLOGON trigger, PT0S time limit, and configurable RunLevel
- C# codebase is clean -- ElevateOnStartup and self-elevation are fully removed
- Installer ready for distribution; build.ps1 handles ISCC.exe detection
- Phase 18 (Settings Runtime Task Management) can reference: task name `FocusDaemon`, RunLevel values `HighestAvailable`/`LeastPrivilege`, XML structure established in `BuildTaskXml` in `installer/focus.iss`

---
*Phase: 17-task-scheduler-integration*
*Completed: 2026-03-07*
