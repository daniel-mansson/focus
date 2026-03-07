# Phase 18: Settings UI Startup Controls - Context

**Gathered:** 2026-03-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Add two toggles to the existing WinForms settings form -- "Run at startup" (creates/removes the FocusDaemon scheduled task) and "Request elevated permissions" (changes the task's run level between standard and elevated). This is runtime configuration of what Phase 17 established at install time. No new capabilities beyond task management from the settings UI.

</domain>

<decisions>
## Implementation Decisions

### Section placement
- New "Startup" GroupBox at the bottom of the settings form, after Keybindings
- GroupBox title is just "Startup" -- no mention of Task Scheduler mechanism
- Two checkboxes: "Run at startup" and "Request elevated permissions"
- Brief explanatory note under the elevation checkbox: "Required to navigate between admin windows" (matching installer text)
- Increase form ClientSize height from 700 to ~780 to fit the new section (no reliance on scroll)

### UAC experience
- UAC fires immediately on toggle change -- matches auto-save pattern of all other settings
- Creating a task (toggling "Run at startup" ON) always triggers UAC via ShellExec runas (ONLOGON tasks require admin)
- Toggling elevation ON/OFF always triggers UAC (recreates task with different RunLevel)
- Deleting a task (toggling "Run at startup" OFF): try non-elevated first, fallback to UAC only if needed
- If user cancels UAC prompt: silently revert toggle to previous state, no error dialog

### Toggle dependencies
- "Request elevated permissions" is grayed out (disabled) when "Run at startup" is unchecked
- Unchecking "Run at startup" auto-unchecks "Request elevated permissions" (task is deleted, elevation state gone)
- On form open: always query schtasks to detect if FocusDaemon task exists and its run level -- no caching in config.json
- Toggle state reflects actual system state, not stored preferences

### Feedback on actions
- Success: silent -- toggle staying in new position IS the feedback (matches auto-save pattern)
- UAC cancel: silently revert toggle
- Unexpected error (schtasks failure): revert toggle + show MessageBox with error details
- During operation: disable both checkboxes while schtasks runs, re-enable when done (prevents double-clicks)

### Task creation
- Use currently-running executable's path for task creation (works for both installed and dev/portable scenarios)
- Task name: "FocusDaemon" (matching Phase 17 convention)
- Task XML structure matches what the installer creates (ONLOGON trigger, no execution time limit, --background flag)

### Claude's Discretion
- C# implementation approach for schtasks invocation (Process.Start, COM interop, etc.)
- Exact task detection logic (query + XML parse vs simpler approach)
- Thread marshaling for async schtasks operations
- Checkbox layout and spacing within the GroupBox

</decisions>

<specifics>
## Specific Ideas

- Elevation checkbox note should match installer wording: "Required to navigate between admin windows"
- Phase 17 context noted: "Phase 18 must be able to both create and remove the task, and change its run level -- this phase establishes the task name and structure that Phase 18 will manage"
- Task XML should include --background flag in arguments (established in Phase 17)

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `focus/Windows/Daemon/SettingsForm.cs`: Existing settings form with FlowLayoutPanel, GroupBox pattern, auto-save via SaveConfig(), MakeGroup() and AddLabeledNumeric() helpers
- `focus/Windows/FocusActivator.cs`: Has IsCurrentProcessElevated() helper for elevation detection
- `installer/focus.iss`: Has BuildTaskXml() and DetectExistingTask() functions -- C# implementation should mirror this logic

### Established Patterns
- Auto-save on every control change (no Save/Apply button)
- GroupBox sections with MakeGroup(title, width, height) helper
- FlowLayoutPanel with TopDown flow, AutoScroll=true
- Atomic config save via File.Replace (though startup toggles write to Task Scheduler, not config.json)

### Integration Points
- `SettingsForm.BuildUi()`: Add BuildStartupGroup() call after BuildKeybindingsGroup()
- Task name "FocusDaemon" must match exactly what installer/focus.iss uses
- Task XML structure (ONLOGON trigger, ExecutionTimeLimit=PT0S, --background argument) must match installer

</code_context>

<deferred>
## Deferred Ideas

None -- discussion stayed within phase scope.

</deferred>

---

*Phase: 18-settings-ui-startup-controls*
*Context gathered: 2026-03-07*
