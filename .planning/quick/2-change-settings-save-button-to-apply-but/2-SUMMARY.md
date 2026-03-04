---
phase: quick-2
plan: 2
subsystem: ui
tags: [winforms, settings, tray, daemon-restart]

requires: []
provides:
  - Apply button in SettingsForm that saves config and restarts the daemon immediately
  - Closing the settings window via X discards unsaved changes (no save-on-close)
  - SettingsForm accepts Action? onApply constructor parameter for restart callback injection
affects: [SettingsForm, TrayIcon, daemon-restart]

tech-stack:
  added: []
  patterns:
    - "Callback injection: SettingsForm receives Action? onApply to decouple save-then-restart from form UI"
    - "Restart reuse: Apply button reuses existing OnRestartClicked via callback, no duplication"

key-files:
  created: []
  modified:
    - focus/Windows/Daemon/SettingsForm.cs
    - focus/Windows/Daemon/TrayIcon.cs

key-decisions:
  - "Apply button does not call Close() — the restart callback (Application.ExitThread) handles all teardown including form disposal"
  - "Action? is nullable with default null so SettingsForm could theoretically be used standalone without crashing"
  - "X button naturally discards changes — no FormClosing handler needed since save logic only runs in OnApplyClicked"

patterns-established:
  - "Apply-then-restart: config saved atomically, then daemon restarted in single user action"

requirements-completed: []

duration: 5min
completed: 2026-03-04
---

# Quick Task 2: Change Settings Save Button to Apply Summary

**Apply button in SettingsForm saves config atomically then restarts the daemon via tray callback, so new settings take effect immediately without manual restart.**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-04T11:30:00Z
- **Completed:** 2026-03-04T11:35:00Z
- **Tasks:** 1
- **Files modified:** 2

## Accomplishments

- Renamed `BuildSaveRow`/`OnSaveClicked` to `BuildApplyRow`/`OnApplyClicked`
- Changed button text from "Save" to "Apply"
- Added `Action? _onApply` field and `onApply = null` constructor parameter to `SettingsForm`
- Apply handler now invokes `_onApply?.Invoke()` after atomic config save instead of `Close()`
- `TrayIcon.OnSettingsClicked` passes `() => OnRestartClicked(null, EventArgs.Empty)` as the `onApply` callback
- Closing the form via X discards changes naturally — no `FormClosing` handler needed

## Task Commits

1. **Task 1: Convert Save button to Apply with restart callback** - `7580fa3` (feat)

## Files Created/Modified

- `focus/Windows/Daemon/SettingsForm.cs` - Apply button, onApply callback field/constructor, renamed methods, removed Close() from handler
- `focus/Windows/Daemon/TrayIcon.cs` - Passes OnRestartClicked as onApply callback when constructing SettingsForm

## Decisions Made

- Apply button does not call `Close()` — the restart callback (`OnRestartClicked`) calls `Application.ExitThread()`, which exits the STA message pump and disposes all forms automatically.
- `Action?` is nullable with a default of `null` to keep `SettingsForm` usable in isolation (e.g., designer preview) without crashing.
- No `FormClosing` handler added — X button discarding changes is the correct behavior by design.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

Build produced MSB3027/MSB3021 file-lock errors (output EXE locked by running daemon process). This is the documented operational blocker in STATE.md ("Kill daemon before rebuild"). No C# compilation errors were present — `dotnet build` C# compilation completed successfully.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Settings Apply flow complete — daemon restarts immediately on Apply
- Closing the settings window via X safely discards changes
- No further changes needed for this feature

---
*Phase: quick-2*
*Completed: 2026-03-04*
