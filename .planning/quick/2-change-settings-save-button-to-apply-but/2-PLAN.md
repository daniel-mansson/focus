---
phase: quick
plan: 2
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/SettingsForm.cs
  - focus/Windows/Daemon/TrayIcon.cs
autonomous: true
requirements: []
must_haves:
  truths:
    - "Clicking Apply saves config to disk and restarts the daemon with new settings"
    - "Closing the settings window via X button discards unsaved changes"
    - "Button text reads Apply not Save"
  artifacts:
    - path: "focus/Windows/Daemon/SettingsForm.cs"
      provides: "Apply button that saves config and invokes restart callback"
      contains: "Apply"
    - path: "focus/Windows/Daemon/TrayIcon.cs"
      provides: "Passes restart callback to SettingsForm constructor"
      contains: "onApply"
  key_links:
    - from: "focus/Windows/Daemon/SettingsForm.cs"
      to: "focus/Windows/Daemon/TrayIcon.cs"
      via: "Action onApply constructor callback"
      pattern: "onApply"
---

<objective>
Change the settings form Save button to an Apply button that saves config and restarts the daemon, so new settings take effect immediately. Closing the window via X discards unsaved changes.

Purpose: Currently Save writes config and closes the form, but the daemon still runs with old settings until manually restarted. Apply should save + restart in one action.
Output: Modified SettingsForm.cs and TrayIcon.cs
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@focus/Windows/Daemon/SettingsForm.cs
@focus/Windows/Daemon/TrayIcon.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Convert Save button to Apply with restart callback</name>
  <files>focus/Windows/Daemon/SettingsForm.cs, focus/Windows/Daemon/TrayIcon.cs</files>
  <action>
In SettingsForm.cs:
1. Add a private readonly field `Action? _onApply` to hold the restart callback.
2. Change the constructor signature to `SettingsForm(Action? onApply = null)` and store the parameter.
3. In `BuildSaveRow()`, rename the button text from `"Save"` to `"Apply"`. Rename the method to `BuildApplyRow()` and update the call in `BuildUi()`.
4. Rename `OnSaveClicked` to `OnApplyClicked`. After the atomic config save logic (File.Replace/File.Move), instead of calling `Close()`, invoke `_onApply?.Invoke()`. The form does NOT need to call Close() itself -- the restart callback in TrayIcon will exit the entire application (Application.ExitThread), which disposes all forms.
5. Remove the `Close()` call at the end of the save handler entirely. The restart handles application teardown.

In TrayIcon.cs (DaemonApplicationContext):
1. In `OnSettingsClicked`, when creating a new SettingsForm, pass a restart action: `new SettingsForm(onApply: () => OnRestartClicked(null, EventArgs.Empty))`. This reuses the existing restart logic (which starts a new process, hides tray icon, calls _onExit, calls Application.ExitThread).
2. No other changes needed -- the existing `OnRestartClicked` method already handles the full restart lifecycle including error handling and flag inheritance.

Design notes:
- The `Action?` parameter is nullable with a default of null so SettingsForm could theoretically be used standalone (though in practice it is always created from the tray).
- Closing the form via the X button simply disposes the form without saving. Since the save logic only runs in OnApplyClicked, closing the window naturally discards changes. No FormClosing handler needed.
- The restart callback matches the exact same behavior as the "Restart Daemon" tray menu item.
  </action>
  <verify>
    <automated>cd C:/OtherWork/focus &amp;&amp; dotnet build focus/focus.csproj --no-restore 2>&amp;1 | tail -5</automated>
    <manual>Open settings form, change a value, click Apply -- daemon should restart with new settings. Open settings form, change a value, close via X -- settings should remain unchanged on next open.</manual>
  </verify>
  <done>Apply button saves config and triggers daemon restart. X button closes form without saving. Build succeeds with no errors.</done>
</task>

</tasks>

<verification>
- `dotnet build` succeeds with no errors
- SettingsForm button text reads "Apply"
- No `Close()` call in the apply handler (restart handles teardown)
- Constructor accepts `Action? onApply` parameter
- TrayIcon passes restart callback when creating SettingsForm
</verification>

<success_criteria>
- Build compiles cleanly
- Apply button text visible in code
- Restart callback wired from TrayIcon to SettingsForm
- No save-on-close behavior (closing discards changes)
</success_criteria>

<output>
After completion, create `.planning/quick/2-change-settings-save-button-to-apply-but/2-SUMMARY.md`
</output>
