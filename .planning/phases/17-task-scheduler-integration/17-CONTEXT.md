# Phase 17: Task Scheduler Integration - Context

**Gathered:** 2026-03-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Register the Focus daemon to start automatically at Windows logon via Task Scheduler, with user-chosen elevation level. Clean task removal on uninstall. Settings UI runtime toggles are Phase 18.

</domain>

<decisions>
## Implementation Decisions

### Install-time UX
- Custom wizard page "Startup Options" in Inno Setup flow: welcome > install dir > startup options > progress > finish
- Two checkboxes: "Start at logon" (checked by default) and "Run elevated (admin)" (unchecked by default)
- "Run elevated" checkbox includes explanatory note: "Required to navigate between admin windows"
- On upgrade/reinstall: detect existing scheduled task and pre-check boxes to match current state

### Task creation method
- Inno Setup Pascal Script in [Code] section creates/removes the task via schtasks.exe
- Task name: "FocusDaemon"
- Trigger: /SC ONLOGON
- Execution time limit: disabled (daemon runs indefinitely until logoff/shutdown)
- Phase 18 settings UI will need its own C# implementation for runtime task management

### Elevation model
- When user checks "Run elevated", task is created with /RL HIGHEST -- daemon starts as admin automatically, no UAC prompt at logon
- Creating an elevated task requires admin -- installer shows UAC prompt only when "Run elevated" is checked
- Remove existing ElevateOnStartup config from FocusConfig.cs and self-elevate code from DaemonCommand.cs -- Task Scheduler handles all elevation
- Manual `focus daemon` users can right-click "Run as admin" if needed; no in-app self-elevation path
- Phase 18 settings UI will allow changing elevation after install (SETS-02)

### Uninstall cleanup
- schtasks /DELETE /TN FocusDaemon /F in Inno Setup [UninstallRun] section
- If elevated task requires admin to delete, uninstall prompts UAC
- Daemon stop already handled by AppMutex=Global\focus-daemon from Phase 16 -- no extra taskkill needed

### Claude's Discretion
- Exact Pascal Script implementation for schtasks calls
- Task detection logic for upgrade pre-check
- UAC elevation approach in Pascal Script (ShellExec with runas verb vs other methods)
- schtasks flags for timeout override

</decisions>

<specifics>
## Specific Ideas

- "Run elevated" checkbox explanation should mention admin windows specifically -- users need to understand WHY they'd want elevation
- Phase 18 must be able to both create and remove the task, and change its run level -- this phase establishes the task name and structure that Phase 18 will manage

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `installer/focus.iss`: Existing Inno Setup script with minimal wizard flow, AppMutex detection, PrivilegesRequired=lowest
- `focus/Windows/FocusConfig.cs`: Has ElevateOnStartup property (to be removed in this phase)
- `focus/Windows/Daemon/DaemonCommand.cs`: Has self-elevate via runas code (to be removed in this phase)
- `focus/Windows/FocusActivator.cs`: Has IsCurrentProcessElevated() and IsWindowElevated() helpers (elevation detection stays, self-elevate removed)

### Established Patterns
- Inno Setup Pascal Script [Code] section for custom logic
- AppMutex=Global\focus-daemon for daemon lifecycle management
- Config at %AppData%\focus\config.json -- separate from install directory

### Integration Points
- `installer/focus.iss` [Setup], [Code], and [UninstallRun] sections need modification
- `focus/Windows/FocusConfig.cs` needs ElevateOnStartup property removed
- `focus/Windows/Daemon/DaemonCommand.cs` needs self-elevate block removed
- `focus/Windows/Daemon/SettingsForm.cs` has existing ElevateOnStartup checkbox (to be removed -- Phase 18 replaces with task-based toggle)

</code_context>

<deferred>
## Deferred Ideas

None -- discussion stayed within phase scope.

</deferred>

---

*Phase: 17-task-scheduler-integration*
*Context gathered: 2026-03-06*
