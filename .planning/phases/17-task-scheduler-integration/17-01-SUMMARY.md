---
phase: 17-task-scheduler-integration
plan: 01
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

key-files:
  created:
    - installer/test-scheduler.ps1
  modified:
    - installer/focus.iss
    - focus/Windows/FocusConfig.cs
    - focus/Windows/Daemon/DaemonCommand.cs
    - focus/Windows/Daemon/SettingsForm.cs

key-decisions:
  - "Always use ShellExec runas for schtasks /Create -- ONLOGON tasks require admin even for LeastPrivilege RunLevel"
  - "Use XML import (not CLI flags) for task creation to set ExecutionTimeLimit=PT0S -- no CLI flag exists for this"
  - "Capture schtasks /Query /XML output via cmd.exe redirection to temp file -- Exec cannot capture stdout directly"
  - "ANSI encoding for SaveStringToFile XML is safe -- all content is ASCII (install path contains only ASCII chars)"
  - "ElevateOnStartup removed from C# codebase -- Task Scheduler RunLevel replaces in-app self-elevation"

patterns-established:
  - "Pattern: Task creation always via ShellExec runas for schtasks -- non-admin installer cannot create ONLOGON tasks directly"
  - "Pattern: Upgrade detection reads existing task XML via cmd /C schtasks /Query /XML redirect, checks for HighestAvailable string"
  - "Pattern: Dual-path delete (Exec then ShellExec runas on failure) for tasks that may have been created elevated"

requirements-completed: [SCHED-01, SCHED-02, SCHED-03]

# Metrics
duration: 3min
completed: 2026-03-07
---

# Phase 17 Plan 01: Task Scheduler Integration Summary

**Inno Setup installer extended with Task Scheduler integration: custom Startup Options wizard page, schtasks XML import for ONLOGON task creation with configurable RunLevel and PT0S ExecutionTimeLimit, upgrade detection via XML query, and removal of the obsolete in-app ElevateOnStartup self-elevation mechanism from C# source.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-07T07:45:13Z
- **Completed:** 2026-03-07T07:49:00Z
- **Tasks:** 2 of 3 automated tasks complete (Task 3 is human-verify checkpoint)
- **Files modified:** 5

## Accomplishments

- Added `[UninstallRun]` section and full `[Code]` Pascal Script block to `installer/focus.iss` with five functions: `BuildTaskXml`, `DetectExistingTask`, `InitializeWizard`, `CurStepChanged`, and `CurUninstallStepChanged`
- Created `installer/test-scheduler.ps1` with 8 automated checks covering task existence, ExecutionTimeLimit, LogonTrigger, RunLevel, Command path, Arguments, LogonType, and MultipleInstancesPolicy
- Removed `ElevateOnStartup` from all three C# files (`FocusConfig.cs`, `DaemonCommand.cs`, `SettingsForm.cs`); project compiles cleanly

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Task Scheduler integration to Inno Setup installer** - `ab431e5` (feat)
2. **Task 2: Remove obsolete ElevateOnStartup from C# source** - `a611dbb` (feat)
3. **Task 3: Verify full install/uninstall lifecycle** - Awaiting human verification (checkpoint)

## Files Created/Modified

- `installer/focus.iss` - Added [UninstallRun] section and [Code] Pascal Script with full Task Scheduler integration
- `installer/test-scheduler.ps1` - New verification script with 8 checks for post-install task state
- `focus/Windows/FocusConfig.cs` - Removed ElevateOnStartup property
- `focus/Windows/Daemon/DaemonCommand.cs` - Removed self-elevate block and elevateOnStartup verbose logging
- `focus/Windows/Daemon/SettingsForm.cs` - Removed _elevateCheck field, BuildAdvancedGroup() method, ElevateOnStartup save line; adjusted form height to 700

## Decisions Made

- Used `ShellExec('runas', 'schtasks.exe', ...)` for task creation in non-admin install mode -- ONLOGON tasks require admin regardless of RunLevel. The installer runs as lowest-privilege per Phase 16 design, so only the schtasks command gets UAC-elevated.
- Used XML import approach for task creation rather than CLI flags -- `ExecutionTimeLimit=PT0S` cannot be set via schtasks command-line flags; XML is the only way.
- Captured schtasks XML output via `{cmd}` with `/C` redirect to temp file (not `Exec` directly, which cannot capture stdout).
- Kept ANSI encoding for SaveStringToFile XML -- all content in the XML (including install path) is ASCII in typical Windows installs, so UTF-16 BOM is not required.
- Task deletion uses dual-path: try Exec first (works for non-elevated tasks), then ShellExec runas on failure (for elevated tasks).

## Deviations from Plan

None - plan executed exactly as written.

The `DetectExistingTask` function in RESEARCH.md had an incorrect example (`Exec('>', ...)`) which was corrected in the implementation to use `Exec(ExpandConstant('{cmd}'), '/C schtasks.exe /Query ...')`. This was corrected during implementation without issue.

## Issues Encountered

- `dotnet build` failed with file-lock error (MSB3021/MSB3027) because the Focus daemon was already running. There were no C# compilation errors (verified with `dotnet build ... | grep "error CS"`). The build output confirmed zero `error CS` results.

## Next Phase Readiness

- Task Scheduler integration is complete; installer is ready for full install/uninstall lifecycle testing (Task 3 checkpoint)
- Phase 18 (Settings Runtime Task Management) can reference: task name `FocusDaemon`, RunLevel values `HighestAvailable`/`LeastPrivilege`, XML structure established in `BuildTaskXml` in `installer/focus.iss`
- `ElevateOnStartup` is fully removed from C# -- no residual references remain in any `.cs` file

---
*Phase: 17-task-scheduler-integration*
*Completed: 2026-03-07*
