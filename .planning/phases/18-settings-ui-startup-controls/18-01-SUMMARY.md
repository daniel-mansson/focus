---
phase: 18-settings-ui-startup-controls
plan: "01"
subsystem: ui
tags: [winforms, schtasks, scheduled-task, uac, settings, checkbox, process-start]

# Dependency graph
requires:
  - phase: 17-task-scheduler-integration
    provides: FocusDaemon scheduled task structure (XML template, task name, ONLOGON trigger, --background flag)

provides:
  - Runtime startup management via Settings UI (create/remove/reconfigure FocusDaemon scheduled task)
  - DetectTaskState, RunSchtasksElevated, BuildTaskXml, CreateTask, DeleteTask helper methods in SettingsForm
  - Async event handlers with UAC elevation, toggle revert on cancel, and UI locking during operations

affects:
  - any phase modifying SettingsForm.cs or scheduled task behavior

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Process.Start with Verb=runas and UseShellExecute=true for UAC-elevated schtasks operations
    - Process.Start with UseShellExecute=false and RedirectStandardOutput for non-elevated schtasks query
    - Async void event handler with Task.Run for blocking process operations on background thread
    - Unhook-set-rehook pattern to prevent CheckedChanged handler recursion during programmatic revert

key-files:
  created: []
  modified:
    - focus/Windows/Daemon/SettingsForm.cs

key-decisions:
  - "Used string concatenation for task XML instead of raw string literal for maximum readability and exact match with installer output"
  - "CreateTask always deletes existing task before creating new one to handle elevation level changes cleanly"
  - "DeleteTask tries non-elevated first, falls back to UAC elevation -- minimizes unnecessary UAC prompts"
  - "Exception-based error handling with MessageBox for unexpected schtasks failures, silent revert for UAC cancel"

patterns-established:
  - "Pattern: schtasks operations in SettingsForm use async void + Task.Run to avoid blocking UI thread during UAC"
  - "Pattern: Checkbox toggle revert uses unhook-set-rehook to prevent handler recursion"
  - "Pattern: Dependent checkboxes (elevation depends on startup) disabled/enabled based on parent state"

requirements-completed: [SETS-01, SETS-02]

# Metrics
duration: ~2min
completed: 2026-03-07
---

# Phase 18 Plan 01: Settings UI Startup Controls Summary

**WinForms Startup GroupBox with "Run at startup" and "Request elevated permissions" checkboxes that create/remove/reconfigure the FocusDaemon scheduled task via schtasks.exe with UAC elevation, async process execution, and silent toggle revert on cancel.**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-07T09:38:05Z
- **Completed:** 2026-03-07T09:39:29Z
- **Tasks:** 2 of 2 complete (1 auto + 1 human-verify auto-approved)
- **Files modified:** 1

## Accomplishments

- Added Startup GroupBox to SettingsForm with two checkboxes and explanatory label, wired into BuildUi after Keybindings
- Implemented five schtasks infrastructure methods: DetectTaskState (query), RunSchtasksElevated (UAC create/delete), BuildTaskXml (XML template matching installer), CreateTask (write XML + elevated create), DeleteTask (non-elevated with UAC fallback)
- Added async event handlers OnStartupToggled and OnElevationToggled with UI locking, background thread execution, UAC cancel handling (Win32Exception 1223), toggle revert, and dependent control state management
- Task XML structure exactly mirrors installer/focus.iss BuildTaskXml output (ONLOGON trigger, InteractiveToken, PT0S ExecutionTimeLimit, Priority 7, daemon --background arguments)

## Task Commits

Each task was committed atomically:

1. **Task 1: Add schtasks infrastructure methods and Startup UI to SettingsForm** - `22a9bb4` (feat)
2. **Task 2: Verify startup controls work end-to-end** - auto-approved (checkpoint:human-verify)

## Files Created/Modified

- `focus/Windows/Daemon/SettingsForm.cs` - Added Startup GroupBox with two checkboxes, schtasks infrastructure methods (DetectTaskState, RunSchtasksElevated, BuildTaskXml, CreateTask, DeleteTask), async event handlers (OnStartupToggled, OnElevationToggled), increased form height to 780

## Decisions Made

- Used string concatenation for BuildTaskXml rather than C# raw string literal -- ensures exact line-by-line match with installer's Pascal Script output and avoids indentation issues in the generated XML
- CreateTask always deletes existing task before creating a new one, even when just changing elevation level -- simpler and more reliable than trying to modify an existing task in place
- DeleteTask tries non-elevated delete first (Process.Start without runas) before falling back to elevated -- minimizes unnecessary UAC prompts for tasks that may not require admin to delete
- Added try/catch with MessageBox for unexpected schtasks failures (distinct from UAC cancel which silently reverts) -- matches CONTEXT.md decision on error feedback

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Settings UI startup controls are complete and compile cleanly
- FocusDaemon task management is now available from both the installer (Phase 17) and the running application (Phase 18)
- This is the final plan in the v5.0 Installer milestone

## Self-Check: PASSED

- FOUND: focus/Windows/Daemon/SettingsForm.cs
- FOUND: .planning/phases/18-settings-ui-startup-controls/18-01-SUMMARY.md
- FOUND: commit 22a9bb4

---
*Phase: 18-settings-ui-startup-controls*
*Completed: 2026-03-07*
