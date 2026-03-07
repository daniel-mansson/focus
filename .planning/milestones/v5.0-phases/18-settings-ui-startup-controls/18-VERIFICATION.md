---
phase: 18-settings-ui-startup-controls
verified: 2026-03-07T12:00:00Z
status: human_needed
score: 9/9 must-haves verified (automated)
must_haves:
  truths:
    - "Settings form shows a Run at startup checkbox reflecting whether FocusDaemon scheduled task currently exists"
    - "Checking Run at startup triggers UAC and creates the FocusDaemon scheduled task with ONLOGON trigger"
    - "Unchecking Run at startup deletes the FocusDaemon scheduled task (non-elevated first, UAC fallback)"
    - "Settings form shows a Request elevated permissions checkbox reflecting the current task RunLevel"
    - "Checking Request elevated permissions triggers UAC and recreates the task with HighestAvailable RunLevel"
    - "Unchecking Request elevated permissions triggers UAC and recreates the task with LeastPrivilege RunLevel"
    - "Request elevated permissions is grayed out when Run at startup is unchecked"
    - "Cancelling a UAC prompt silently reverts the toggle to its previous state"
    - "Both checkboxes are disabled during schtasks operations to prevent double-clicks"
  artifacts:
    - path: "focus/Windows/Daemon/SettingsForm.cs"
      provides: "Startup GroupBox with two checkboxes, schtasks helper methods, async event handlers"
      contains: "BuildStartupGroup"
  key_links:
    - from: "SettingsForm.BuildStartupGroup()"
      to: "SettingsForm.BuildUi()"
      via: "root.Controls.Add(BuildStartupGroup()) call after BuildKeybindingsGroup()"
      pattern: "BuildStartupGroup"
    - from: "OnStartupToggled / OnElevationToggled"
      to: "CreateTask / DeleteTask / RunSchtasksElevated"
      via: "async event handlers calling schtasks infrastructure methods"
      pattern: "OnStartupToggled|OnElevationToggled"
    - from: "DetectTaskState()"
      to: "schtasks /Query /TN FocusDaemon /XML"
      via: "Process.Start with RedirectStandardOutput to detect task existence and RunLevel"
      pattern: "DetectTaskState"
human_verification:
  - test: "Toggle 'Run at startup' ON, approve UAC, verify task created via schtasks /Query /TN FocusDaemon"
    expected: "Task exists with ONLOGON trigger and daemon --background arguments"
    why_human: "Requires live UAC prompt interaction and system-level schtasks state"
  - test: "Toggle 'Request elevated permissions' ON, approve UAC, verify RunLevel via schtasks /Query /TN FocusDaemon /XML"
    expected: "Task XML shows HighestAvailable RunLevel"
    why_human: "Requires live UAC prompt interaction"
  - test: "Toggle 'Request elevated permissions' OFF, approve UAC, verify RunLevel reverts"
    expected: "Task XML shows LeastPrivilege RunLevel"
    why_human: "Requires live UAC prompt interaction"
  - test: "Toggle 'Run at startup' OFF, verify task deleted"
    expected: "schtasks /Query /TN FocusDaemon returns error (task not found)"
    why_human: "Requires live UI interaction and schtasks state verification"
  - test: "Toggle 'Run at startup' ON, cancel UAC prompt"
    expected: "Checkbox silently reverts to unchecked, no error dialog"
    why_human: "Requires live UAC prompt cancellation and visual confirmation"
  - test: "Close and reopen Settings form, verify checkboxes reflect actual task state"
    expected: "Checkboxes match schtasks query output"
    why_human: "Requires running app and comparing UI to system state"
---

# Phase 18: Settings UI Startup Controls Verification Report

**Phase Goal:** User can manage startup registration and elevation from within the running application
**Verified:** 2026-03-07T12:00:00Z
**Status:** human_needed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Settings form shows a Run at startup checkbox reflecting whether FocusDaemon scheduled task currently exists | VERIFIED | `BuildStartupGroup()` calls `DetectTaskState()` to query schtasks before setting `_startupCheck.Checked = exists` (line 369-370). Handler wired AFTER initial state set (line 375). |
| 2 | Checking Run at startup triggers UAC and creates the FocusDaemon scheduled task with ONLOGON trigger | VERIFIED | `OnStartupToggled` (line 526) calls `CreateTask(elevated: _elevationCheck.Checked)` which calls `RunSchtasksElevated` with `Verb = "runas"` (line 424). `BuildTaskXml` includes `<LogonTrigger>` (line 447-449). |
| 3 | Unchecking Run at startup deletes the FocusDaemon scheduled task (non-elevated first, UAC fallback) | VERIFIED | `OnStartupToggled` calls `DeleteTask()` when `!wantStartup` (line 542). `DeleteTask` (line 503) tries non-elevated first (line 508-518), then falls back to `RunSchtasksElevated` (line 523). |
| 4 | Settings form shows a Request elevated permissions checkbox reflecting the current task RunLevel | VERIFIED | `DetectTaskState` checks `output.Contains("HighestAvailable")` (line 405) and `BuildStartupGroup` sets `_elevationCheck.Checked = isElevated` (line 371). |
| 5 | Checking Request elevated permissions triggers UAC and recreates the task with HighestAvailable RunLevel | VERIFIED | `OnElevationToggled` (line 576) calls `CreateTask(elevated: wantElevated)` (line 587). `BuildTaskXml` uses `"HighestAvailable"` when `runElevated=true` (line 440). `CreateTask` deletes existing task first then creates new one (lines 492-495). |
| 6 | Unchecking Request elevated permissions triggers UAC and recreates the task with LeastPrivilege RunLevel | VERIFIED | Same flow as truth 5 but with `wantElevated=false`, producing `"LeastPrivilege"` in XML (line 440). |
| 7 | Request elevated permissions is grayed out when Run at startup is unchecked | VERIFIED | Initial state: `_elevationCheck.Enabled = exists` (line 372). After toggle: `_elevationCheck.Enabled = _startupCheck.Checked` (line 565). On uncheck, elevation checkbox is also unchecked (lines 566-571). |
| 8 | Cancelling a UAC prompt silently reverts the toggle to its previous state | VERIFIED | `RunSchtasksElevated` catches `Win32Exception` with `NativeErrorCode == 1223` (line 431) and returns false. Both handlers unhook-revert-rehook on failure: `OnStartupToggled` (lines 558-561), `OnElevationToggled` (lines 602-605). |
| 9 | Both checkboxes are disabled during schtasks operations to prevent double-clicks | VERIFIED | Both handlers disable both checkboxes at start: `_startupCheck.Enabled = false; _elevationCheck.Enabled = false;` (lines 531-532 and 581-582). Re-enabled after operation completes (lines 565-573 and 609-610). |

**Score:** 9/9 truths verified (code-level)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `focus/Windows/Daemon/SettingsForm.cs` | Startup GroupBox with two checkboxes, schtasks helper methods, async event handlers | VERIFIED | 729 lines. Contains `BuildStartupGroup`, `DetectTaskState`, `RunSchtasksElevated`, `BuildTaskXml`, `CreateTask`, `DeleteTask`, `OnStartupToggled`, `OnElevationToggled`. No stubs or placeholders. Compiles with 0 errors. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `BuildStartupGroup()` | `BuildUi()` | `root.Controls.Add(BuildStartupGroup())` | WIRED | Line 126: called after `BuildKeybindingsGroup()`, exactly as planned |
| `OnStartupToggled` / `OnElevationToggled` | `CreateTask` / `DeleteTask` / `RunSchtasksElevated` | async event handlers calling schtasks infrastructure | WIRED | Handlers subscribed at lines 375-376. `OnStartupToggled` calls `CreateTask`/`DeleteTask` (lines 540-542). `OnElevationToggled` calls `CreateTask` (line 587). Both use `Task.Run` for background execution. |
| `DetectTaskState()` | `schtasks /Query /TN FocusDaemon /XML` | `Process.Start` with `RedirectStandardOutput` | WIRED | Lines 384-412: Process.Start with `UseShellExecute = false`, `RedirectStandardOutput = true`, reads stdout and checks exit code. Called at line 369 from `BuildStartupGroup()`. |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| SETS-01 | 18-01-PLAN | Settings form includes "Run at startup" toggle that creates/removes the scheduled task | SATISFIED | `_startupCheck` checkbox in `BuildStartupGroup()`. `OnStartupToggled` handler creates task via `CreateTask()` or deletes via `DeleteTask()`. Initial state from `DetectTaskState()` query. UAC cancel revert via unhook-set-rehook. |
| SETS-02 | 18-01-PLAN | Settings form includes "Request elevated permissions" toggle that updates the scheduled task run level | SATISFIED | `_elevationCheck` checkbox in `BuildStartupGroup()`. `OnElevationToggled` handler recreates task with new RunLevel via `CreateTask(elevated: wantElevated)`. Disabled when startup unchecked (`_elevationCheck.Enabled = exists`). |

No orphaned requirements found. REQUIREMENTS.md maps SETS-01 and SETS-02 to Phase 18; both are claimed by plan 18-01.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | - | - | - | No TODO/FIXME/PLACEHOLDER/stub patterns found |

**Build status:** Compiles with 0 errors, 2 pre-existing warnings (unrelated to phase 18).

### Cross-Reference: Task XML Structure

The C# `BuildTaskXml` (lines 438-479) was compared line-by-line against the installer's Pascal Script `BuildTaskXml` (focus.iss lines 49-95). All XML elements match in structure, order, and values:

- XML declaration: `<?xml version="1.0" encoding="UTF-16"?>`
- Task namespace and version: identical
- RegistrationInfo/Description: "Focus daemon - window navigation"
- LogonTrigger with `<Enabled>true</Enabled>`
- Principal with InteractiveToken and dynamic RunLevel
- All 12 Settings elements in identical order
- ExecutionTimeLimit: PT0S
- Priority: 7
- Actions: `daemon --background` arguments

File encoding: `File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode)` -- UTF-16 LE matching XML declaration.

### Human Verification Required

All automated code-level checks pass. The following require human testing because they involve live UAC prompts, WinForms UI interaction, and system-level Task Scheduler state that cannot be verified through static analysis.

### 1. Create Scheduled Task via Settings UI

**Test:** Run the daemon, open Settings, check "Run at startup", approve UAC prompt
**Expected:** Checkbox stays checked; `schtasks /Query /TN "FocusDaemon" /V` shows ONLOGON trigger and `daemon --background` arguments
**Why human:** Requires live UAC prompt interaction and Task Scheduler system state

### 2. Toggle Elevation via Settings UI

**Test:** With task existing, check "Request elevated permissions", approve UAC
**Expected:** `schtasks /Query /TN "FocusDaemon" /XML` output contains "HighestAvailable"
**Why human:** Requires live UAC prompt and XML output inspection

### 3. Revert Elevation via Settings UI

**Test:** Uncheck "Request elevated permissions", approve UAC
**Expected:** `schtasks /Query /TN "FocusDaemon" /XML` output shows "LeastPrivilege"
**Why human:** Requires live UAC prompt

### 4. Delete Task via Settings UI

**Test:** Uncheck "Run at startup"
**Expected:** `schtasks /Query /TN "FocusDaemon"` returns error (task not found); "Request elevated permissions" becomes grayed out and unchecked
**Why human:** Requires live UI interaction and system state verification

### 5. UAC Cancellation Revert

**Test:** Check "Run at startup", click "No" on UAC prompt
**Expected:** Checkbox silently reverts to unchecked; no error dialog shown
**Why human:** Requires deliberately cancelling a UAC prompt

### 6. Form State Persistence

**Test:** With task existing, close and reopen Settings form
**Expected:** Checkboxes reflect actual task state from schtasks query (not cached values)
**Why human:** Requires closing/reopening Settings form and comparing to system state

### Gaps Summary

No code-level gaps found. All 9 observable truths are verified at the code level: the artifact exists, is substantive (729 lines, no stubs), and is fully wired into the SettingsForm lifecycle. The task XML structure matches the installer exactly. Both requirement IDs (SETS-01, SETS-02) are satisfied by the implementation.

The remaining verification items are human-only because they involve live UAC prompt interaction, WinForms UI behavior, and Task Scheduler system state -- none of which can be verified through static code analysis.

---

_Verified: 2026-03-07T12:00:00Z_
_Verifier: Claude (gsd-verifier)_
