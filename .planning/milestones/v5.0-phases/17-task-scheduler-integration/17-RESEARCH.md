# Phase 17: Task Scheduler Integration - Research

**Researched:** 2026-03-06
**Domain:** Windows Task Scheduler via schtasks.exe, Inno Setup Pascal Script custom wizard pages
**Confidence:** HIGH

## Summary

This phase registers the Focus daemon to start automatically at Windows logon via Task Scheduler, with a user-chosen elevation level set during install. The implementation uses Inno Setup Pascal Script to create a custom wizard page with two checkboxes ("Start at logon" and "Run elevated"), then calls `schtasks.exe` to create/delete the scheduled task. On uninstall, the task is removed via [UninstallRun].

The primary technical challenge is that **schtasks /CREATE /SC ONLOGON always requires administrator privileges**, regardless of the /RL setting. This means the installer must elevate (via ShellExec with "runas" verb) when calling schtasks, even when the main installer runs with PrivilegesRequired=lowest. The secondary challenge is disabling the default 72-hour ExecutionTimeLimit, which requires the XML import approach rather than pure command-line flags, since schtasks.exe has no command-line parameter for ExecutionTimeLimit.

**Primary recommendation:** Use schtasks /CREATE with /XML for task creation (to set ExecutionTimeLimit=PT0S and RunLevel), elevating via ShellExec "runas" when needed. Use CreateInputOptionPage for the checkbox wizard page. Use [UninstallRun] with schtasks /DELETE for cleanup.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Custom wizard page "Startup Options" in Inno Setup flow: welcome > install dir > startup options > progress > finish
- Two checkboxes: "Start at logon" (checked by default) and "Run elevated (admin)" (unchecked by default)
- "Run elevated" checkbox includes explanatory note: "Required to navigate between admin windows"
- On upgrade/reinstall: detect existing scheduled task and pre-check boxes to match current state
- Inno Setup Pascal Script in [Code] section creates/removes the task via schtasks.exe
- Task name: "FocusDaemon"
- Trigger: /SC ONLOGON
- Execution time limit: disabled (daemon runs indefinitely until logoff/shutdown)
- Phase 18 settings UI will need its own C# implementation for runtime task management
- When user checks "Run elevated", task is created with /RL HIGHEST -- daemon starts as admin automatically, no UAC prompt at logon
- Creating an elevated task requires admin -- installer shows UAC prompt only when "Run elevated" is checked
- Remove existing ElevateOnStartup config from FocusConfig.cs and self-elevate code from DaemonCommand.cs -- Task Scheduler handles all elevation
- Manual `focus daemon` users can right-click "Run as admin" if needed; no in-app self-elevation path
- Phase 18 settings UI will allow changing elevation after install (SETS-02)
- schtasks /DELETE /TN FocusDaemon /F in Inno Setup [UninstallRun] section
- If elevated task requires admin to delete, uninstall prompts UAC
- Daemon stop already handled by AppMutex=Global\focus-daemon from Phase 16 -- no extra taskkill needed

### Claude's Discretion
- Exact Pascal Script implementation for schtasks calls
- Task detection logic for upgrade pre-check
- UAC elevation approach in Pascal Script (ShellExec with runas verb vs other methods)
- schtasks flags for timeout override

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SCHED-01 | Installer registers daemon to start at logon via Task Scheduler | schtasks /CREATE with /XML template containing LogonTrigger, ExecutionTimeLimit=PT0S; Pascal Script CreateInputOptionPage for checkbox UI |
| SCHED-02 | User can choose to run the scheduled task elevated (admin) during install | XML template with RunLevel=HighestAvailable or LeastPrivilege; ShellExec "runas" for UAC elevation when needed |
| SCHED-03 | Uninstall removes the scheduled task cleanly | [UninstallRun] with schtasks /DELETE /TN FocusDaemon /F; runhidden flag |
</phase_requirements>

## Standard Stack

### Core
| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| schtasks.exe | System | Create/delete/query scheduled tasks | Built into all Windows versions; no external dependencies |
| Inno Setup | 6.x | Installer with Pascal Script | Already used in Phase 16; [Code] section supports custom wizard pages |

### Supporting
| Tool | Purpose | When to Use |
|------|---------|-------------|
| schtasks /CREATE /XML | Create task from XML definition | Required to set ExecutionTimeLimit=PT0S (no CLI flag exists) |
| schtasks /QUERY /TN | Check if task exists | Upgrade detection to pre-check wizard checkboxes |
| schtasks /DELETE /F | Force-delete task | Uninstall cleanup and task recreation |
| SaveStringToFile | Write XML to temp file | Build XML template at runtime in Pascal Script |
| ShellExec "runas" | Elevate a single command | UAC prompt for schtasks when creating ONLOGON task |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| schtasks /XML | schtasks CLI flags only | CLI has no ExecutionTimeLimit parameter; 72-hour default kills daemon |
| ShellExec "runas" | Requiring PrivilegesRequired=admin for whole installer | Breaks per-user install; user gets UAC for everything, not just task |
| Task Scheduler COM API | Direct COM from Pascal Script | Massively more complex; schtasks.exe is simpler and well-documented |

## Architecture Patterns

### Recommended Implementation Flow

```
InitializeWizard
  -> CreateInputOptionPage (after wpSelectDir)
  -> Add "Start at logon" checkbox (checked by default)
  -> Add "Run elevated (admin)" checkbox (unchecked by default)

CurStepChanged(ssPostInstall)
  -> If "Start at logon" checked:
    -> Build XML string with LogonTrigger + settings
    -> Set RunLevel based on "Run elevated" checkbox
    -> SaveStringToFile to {tmp}\FocusDaemon.xml
    -> Call schtasks /DELETE /TN FocusDaemon /F (ignore failure)
    -> Call schtasks /CREATE /XML {tmp}\FocusDaemon.xml /TN FocusDaemon /F
    -> If schtasks needs admin: ShellExec with "runas" verb
  -> If "Start at logon" unchecked:
    -> Call schtasks /DELETE /TN FocusDaemon /F (ignore failure)

[UninstallRun]
  -> schtasks /DELETE /TN FocusDaemon /F (runhidden)
```

### Pattern 1: XML Template for Task Creation
**What:** Build a Task Scheduler XML document in Pascal Script, write to temp file, import via schtasks /CREATE /XML
**When to use:** Always -- this is the only way to set ExecutionTimeLimit=PT0S and control RunLevel in one step
**Example:**
```xml
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Focus daemon - window navigation</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>"C:\path\to\focus.exe"</Command>
      <Arguments>daemon</Arguments>
    </Exec>
  </Actions>
</Task>
```
Source: [Microsoft Learn - Logon Trigger Example XML](https://learn.microsoft.com/en-us/windows/win32/taskschd/logon-trigger-example--xml-), [ExecutionTimeLimit docs](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-executiontimelimit-settingstype-element)

### Pattern 2: UAC Elevation for schtasks
**What:** ONLOGON tasks always require admin to create. Use ShellExec with "runas" verb from Pascal Script to prompt UAC for just the schtasks command.
**When to use:** Always when creating or deleting the ONLOGON task
**Example (Pascal Script):**
```pascal
function CreateScheduledTask(RunElevated: Boolean): Boolean;
var
  XmlPath: String;
  ResultCode: Integer;
begin
  XmlPath := ExpandConstant('{tmp}\FocusDaemon.xml');
  // Write XML to temp file first (see Pattern 1)

  // Delete existing task (ignore errors)
  Exec('schtasks.exe', '/Delete /TN "FocusDaemon" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Create task from XML -- requires admin for ONLOGON trigger
  Result := ShellExec('runas', 'schtasks.exe',
    '/Create /XML "' + XmlPath + '" /TN "FocusDaemon" /F',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
```
Source: [Inno Setup ShellExec docs](https://jrsoftware.org/ishelp/topic_isxfunc_shellexec.htm), [Inno Setup Exec docs](https://jrsoftware.org/ishelp/topic_isxfunc_exec.htm)

### Pattern 3: Custom Wizard Page with CreateInputOptionPage
**What:** Use Inno Setup's built-in CreateInputOptionPage to add checkboxes to the installer wizard
**When to use:** For the "Startup Options" page between directory selection and install progress
**Example (Pascal Script):**
```pascal
var
  StartupPage: TInputOptionWizardPage;

procedure InitializeWizard();
begin
  StartupPage := CreateInputOptionPage(wpSelectDir,
    'Startup Options', 'Configure how Focus starts.',
    'Select startup behavior, then click Next.',
    False, False);  // False=checkboxes (not radio), False=no listbox
  StartupPage.Add('Start at logon');
  StartupPage.Add('Run elevated (admin) - Required to navigate between admin windows');
  StartupPage.Values[0] := True;   // "Start at logon" checked by default
  StartupPage.Values[1] := False;  // "Run elevated" unchecked by default
end;

// Reading values later:
// StartupPage.Values[0] = start at logon
// StartupPage.Values[1] = run elevated
```
Source: [Inno Setup CreateInputOptionPage](https://jrsoftware.org/ishelp/topic_isxfunc_createinputoptionpage.htm), [CodeDlg.iss example](https://github.com/HeliumProject/InnoSetup/blob/master/Examples/CodeDlg.iss)

### Pattern 4: Upgrade Detection via schtasks /QUERY
**What:** Check if FocusDaemon task already exists and detect its RunLevel to pre-populate wizard checkboxes
**When to use:** On upgrade/reinstall to match existing task state
**Example (Pascal Script):**
```pascal
function TaskExists(): Boolean;
var
  ResultCode: Integer;
begin
  // schtasks /QUERY /TN returns exit code 0 if task exists, non-zero otherwise
  Exec('schtasks.exe', '/Query /TN "FocusDaemon"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;
```

To detect whether the existing task is elevated, query the task XML output and check for `HighestAvailable` in the output. Alternatively, use `schtasks /Query /TN "FocusDaemon" /XML` and parse for `<RunLevel>HighestAvailable</RunLevel>`.

### Anti-Patterns to Avoid
- **Using /SC ONLOGON without admin:** schtasks will return "Access is denied" -- ONLOGON always requires admin privileges to create, even with /RL LIMITED
- **Relying on schtasks CLI flags for ExecutionTimeLimit:** No CLI parameter exists; must use /XML approach
- **Running whole installer as admin:** Breaks PrivilegesRequired=lowest per-user install model; only elevate for the schtasks command
- **Using /V1 flag with /XML:** These are incompatible parameters
- **Forgetting /F flag on /CREATE:** Without /F, schtasks prompts for confirmation if task already exists, which hangs the silent installer
- **Using Registry Run key instead of Task Scheduler:** Cannot run elevated; Task Scheduler is strictly better (documented in project's Out of Scope)

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Scheduled task creation | COM API wrapper in Pascal Script | schtasks.exe /CREATE /XML | schtasks is simpler, better documented, handles all edge cases |
| Custom wizard checkboxes | TNewCheckBox on CreateCustomPage | CreateInputOptionPage | Built-in function handles layout, scrolling, DPI scaling automatically |
| XML generation | String concatenation with manual escaping | SaveStringToFile with pre-built template | Template approach is cleaner; only dynamic parts are path and RunLevel |
| Elevation detection | Custom Windows API calls | schtasks exit code + /QUERY /XML | Exit code tells you if task exists; XML output contains RunLevel |
| Daemon auto-start | Registry Run key, Startup folder shortcut | Task Scheduler ONLOGON | Registry Run key cannot run elevated; Startup folder is user-facing clutter |

**Key insight:** The schtasks.exe XML import is the sweet spot between the limited CLI flags and the overly complex COM API. It provides full control over ExecutionTimeLimit, RunLevel, and LogonTrigger in a single, well-documented format.

## Common Pitfalls

### Pitfall 1: ONLOGON Requires Admin Even for LIMITED Tasks
**What goes wrong:** Attempting to create an ONLOGON task without admin privileges returns "ERROR: Access is denied" even when /RL LIMITED is specified.
**Why it happens:** Windows Task Scheduler requires SeCreateGlobalPrivilege for logon triggers, which is only granted to administrators.
**How to avoid:** Always use ShellExec with "runas" verb when calling schtasks /CREATE with /SC ONLOGON (or /XML with LogonTrigger).
**Warning signs:** schtasks returns exit code 1 with "Access is denied" message.

### Pitfall 2: Default 72-Hour ExecutionTimeLimit Kills Daemon
**What goes wrong:** Task Scheduler stops the daemon after 72 hours (3 days) because the default ExecutionTimeLimit applies.
**Why it happens:** schtasks.exe has no command-line parameter to disable the time limit. The default is 72:00:00 (shown as "Stop Task If Runs X Hours and X Mins: 72:0" in verbose query).
**How to avoid:** Use the /XML approach with `<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>` which sets the limit to indefinite.
**Warning signs:** Daemon mysteriously stops after 3 days; Task Scheduler shows "Task was terminated" in history.

### Pitfall 3: ShellExec "runas" Shows Separate UAC Prompt
**What goes wrong:** When the installer runs non-elevated (PrivilegesRequired=lowest) and uses ShellExec "runas" for schtasks, Windows shows a UAC prompt for schtasks.exe, which is a generic-looking "Do you want to allow this app to make changes?" dialog.
**Why it happens:** The installer process itself is non-elevated, so any child process that needs admin must trigger a separate UAC prompt.
**How to avoid:** This is expected behavior and documented in the CONTEXT.md decisions. The UAC prompt only appears when "Run elevated" is checked or when creating any ONLOGON task. Consider: if the installer was already launched elevated (user clicked "Run as administrator" or PrivilegesRequiredOverridesAllowed=dialog triggered), schtasks inherits elevation and no extra UAC prompt appears.
**Warning signs:** User sees two UAC prompts -- one for installer, one for schtasks. This happens only if user elevated the installer AND the schtasks call also prompts. Solution: check if already elevated before using "runas".

### Pitfall 4: Task Deletion Requires Admin Too
**What goes wrong:** Uninstall cannot delete the scheduled task if it was created with admin privileges and the uninstaller runs non-elevated.
**Why it happens:** Tasks created with admin are owned by an admin security context; deleting them requires the same privilege level.
**How to avoid:** The [UninstallRun] entry for schtasks /DELETE will fail silently if non-elevated. Since Inno Setup with PrivilegesRequired=lowest does not auto-elevate uninstall, the task may be orphaned. Workaround: use ShellExec "runas" in the uninstall [Code] section, or accept that the uninstall UAC prompt is needed (per CONTEXT.md: "If elevated task requires admin to delete, uninstall prompts UAC").
**Warning signs:** Orphaned FocusDaemon task after uninstall when user declined UAC.

### Pitfall 5: schtasks /CREATE Without /F Hangs on Existing Task
**What goes wrong:** If the FocusDaemon task already exists, schtasks /CREATE without /F prompts "WARNING: The task name already exists. Do you want to replace it (Y/N)?" which hangs the installer.
**Why it happens:** Default schtasks behavior requires confirmation for overwrites.
**How to avoid:** Always use /F flag to force overwrite without prompting. Alternatively, delete first then create.
**Warning signs:** Installer appears to freeze during post-install step.

### Pitfall 6: Encoding for XML Temp File
**What goes wrong:** SaveStringToFile writes ANSI by default; Task Scheduler XML expects UTF-16 (or UTF-8 without BOM).
**Why it happens:** Inno Setup's SaveStringToFile has encoding considerations.
**How to avoid:** Use `<?xml version="1.0" encoding="UTF-16"?>` in the XML header and ensure the file is written correctly, or use UTF-8 encoding. In practice, since the XML only contains ASCII characters (except possibly the install path), ANSI encoding works fine. The path is in the Command element and Windows handles ANSI paths.
**Warning signs:** schtasks /CREATE /XML fails with "The task XML is malformed."

## Code Examples

### Complete Pascal Script: Task XML Generation
```pascal
function BuildTaskXml(AppPath: String; RunElevated: Boolean): String;
var
  RunLevel: String;
begin
  if RunElevated then
    RunLevel := 'HighestAvailable'
  else
    RunLevel := 'LeastPrivilege';

  Result :=
    '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <RegistrationInfo>' + #13#10 +
    '    <Description>Focus daemon - window navigation</Description>' + #13#10 +
    '  </RegistrationInfo>' + #13#10 +
    '  <Triggers>' + #13#10 +
    '    <LogonTrigger>' + #13#10 +
    '      <Enabled>true</Enabled>' + #13#10 +
    '    </LogonTrigger>' + #13#10 +
    '  </Triggers>' + #13#10 +
    '  <Principals>' + #13#10 +
    '    <Principal id="Author">' + #13#10 +
    '      <LogonType>InteractiveToken</LogonType>' + #13#10 +
    '      <RunLevel>' + RunLevel + '</RunLevel>' + #13#10 +
    '    </Principal>' + #13#10 +
    '  </Principals>' + #13#10 +
    '  <Settings>' + #13#10 +
    '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' + #13#10 +
    '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' + #13#10 +
    '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' + #13#10 +
    '    <AllowHardTerminate>true</AllowHardTerminate>' + #13#10 +
    '    <StartWhenAvailable>false</StartWhenAvailable>' + #13#10 +
    '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' + #13#10 +
    '    <AllowStartOnDemand>true</AllowStartOnDemand>' + #13#10 +
    '    <Enabled>true</Enabled>' + #13#10 +
    '    <Hidden>false</Hidden>' + #13#10 +
    '    <RunOnlyIfIdle>false</RunOnlyIfIdle>' + #13#10 +
    '    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>' + #13#10 +
    '    <Priority>7</Priority>' + #13#10 +
    '  </Settings>' + #13#10 +
    '  <Actions Context="Author">' + #13#10 +
    '    <Exec>' + #13#10 +
    '      <Command>' + AppPath + '</Command>' + #13#10 +
    '      <Arguments>daemon</Arguments>' + #13#10 +
    '    </Exec>' + #13#10 +
    '  </Actions>' + #13#10 +
    '</Task>';
end;
```
Source: [Microsoft Learn - Logon Trigger Example XML](https://learn.microsoft.com/en-us/windows/win32/taskschd/logon-trigger-example--xml-), [ExecutionTimeLimit Element](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-executiontimelimit-settingstype-element)

### Complete Pascal Script: Elevated schtasks Execution
```pascal
function RunSchtasksElevated(Params: String): Boolean;
var
  ErrorCode: Integer;
begin
  Result := ShellExec('runas', 'schtasks.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
end;

function RunSchtasks(Params: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('schtasks.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := Result and (ResultCode = 0);
end;

function IsInstallerElevated(): Boolean;
begin
  Result := IsAdminInstallMode();
end;

procedure CreateOrDeleteTask(Create: Boolean; RunElevated: Boolean);
var
  XmlPath, AppPath, Xml, CreateParams: String;
  ResultCode: Integer;
begin
  if not Create then
  begin
    // Delete task -- needs admin; try non-elevated first, then elevated
    if not RunSchtasks('/Delete /TN "FocusDaemon" /F') then
      RunSchtasksElevated('/Delete /TN "FocusDaemon" /F');
    Exit;
  end;

  // Build and write XML
  AppPath := ExpandConstant('{app}\focus.exe');
  XmlPath := ExpandConstant('{tmp}\FocusDaemon.xml');
  Xml := BuildTaskXml(AppPath, RunElevated);
  SaveStringToFile(XmlPath, Xml, False);

  // Delete existing task first (ignore failure)
  Exec('schtasks.exe', '/Delete /TN "FocusDaemon" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Create task -- ONLOGON always requires admin
  CreateParams := '/Create /XML "' + XmlPath + '" /TN "FocusDaemon" /F';
  if IsInstallerElevated() then
    RunSchtasks(CreateParams)
  else
    RunSchtasksElevated(CreateParams);
end;
```
Source: [Inno Setup ShellExec](https://jrsoftware.org/ishelp/topic_isxfunc_shellexec.htm), [Inno Setup Exec](https://jrsoftware.org/ishelp/topic_isxfunc_exec.htm)

### Upgrade Detection: Pre-populating Checkboxes
```pascal
function DetectExistingTask(var IsElevated: Boolean): Boolean;
var
  ResultCode: Integer;
  TmpFile, Output: String;
begin
  Result := False;
  IsElevated := False;

  // Check if task exists
  Exec('schtasks.exe', '/Query /TN "FocusDaemon"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then Exit;

  Result := True;

  // Query XML to detect RunLevel
  // schtasks /Query /TN "FocusDaemon" /XML writes XML to stdout
  // Use Exec with output redirection to capture
  TmpFile := ExpandConstant('{tmp}\FocusDaemonQuery.xml');
  Exec('>', 'schtasks.exe /Query /TN "FocusDaemon" /XML > "' + TmpFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if LoadStringFromFile(TmpFile, Output) then
    IsElevated := Pos('HighestAvailable', Output) > 0;
end;
```
Note: Parsing schtasks /XML output for HighestAvailable is a simple string search. No full XML parser needed since the string is unique in the output.

### Uninstall Cleanup
```ini
[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""FocusDaemon"" /F"; Flags: runhidden
```
Note: This runs with the uninstaller's privilege level. If the task was created elevated, deletion may fail silently. Per CONTEXT.md, the uninstaller should prompt UAC if needed. To handle this robustly, add a CurUninstallStepChanged handler in [Code] that uses ShellExec "runas" for the delete if the simple Exec fails.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Registry Run key (HKCU\...\Run) | Task Scheduler ONLOGON | Windows Vista+ | Run key cannot run elevated; Task Scheduler supports elevation natively |
| schtasks CLI flags only | schtasks /XML import | Windows Vista+ | XML gives full control over ExecutionTimeLimit, RunLevel, settings |
| Self-elevate in app code (runas verb) | Task Scheduler /RL HIGHEST | Windows Vista+ | No UAC prompt at logon; cleaner user experience |

**Deprecated/outdated:**
- `ElevateOnStartup` config property in FocusConfig.cs: Being removed in this phase; Task Scheduler handles elevation
- Self-elevate code in DaemonCommand.cs: Being removed in this phase; replaced by Task Scheduler RunLevel
- "Run elevated" checkbox in SettingsForm.cs: Being removed in this phase; Phase 18 will add task-based toggle

## Open Questions

1. **ShellExec "runas" for schtasks during uninstall**
   - What we know: [UninstallRun] runs with uninstaller privileges; if non-elevated, schtasks /DELETE may fail for admin-created tasks
   - What's unclear: Whether Inno Setup's CurUninstallStepChanged can reliably use ShellExec "runas" during uninstall
   - Recommendation: Try [UninstallRun] first (runhidden); add Pascal Script fallback with ShellExec "runas" in CurUninstallStepChanged if needed. Test both elevated and non-elevated uninstall paths.

2. **SaveStringToFile encoding for UTF-16 XML**
   - What we know: Task Scheduler XML header says UTF-16, but SaveStringToFile writes ANSI by default
   - What's unclear: Whether schtasks /CREATE /XML tolerates ANSI-encoded XML with UTF-16 declaration
   - Recommendation: Test with ANSI first (all content is ASCII). If schtasks rejects it, either change XML declaration to omit encoding or use UTF-8. Since paths contain only ASCII in typical installs, this is LOW risk.

3. **Capturing schtasks /Query /XML output in Pascal Script**
   - What we know: Exec cannot capture stdout directly; would need redirection via cmd.exe
   - What's unclear: Whether `Exec('>', 'cmd /C schtasks ...')` redirection works reliably in Inno Setup
   - Recommendation: Use `Exec(ExpandConstant('{cmd}'), '/C schtasks.exe /Query /TN "FocusDaemon" /XML > "' + TmpFile + '"', ...)` for stdout capture. Alternatively, just check task existence with exit code (simpler) and assume RunLevel state from previous install.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | Manual testing (installer + Task Scheduler) |
| Config file | N/A -- no automated test framework for installer integration |
| Quick run command | `schtasks /Query /TN "FocusDaemon" /V` |
| Full suite command | Build installer, run install, verify task, run uninstall, verify cleanup |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SCHED-01 | Task created at logon trigger | manual + smoke | `schtasks /Query /TN "FocusDaemon"` (exit code 0 = exists) | N/A |
| SCHED-01 | ExecutionTimeLimit disabled | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check PT0S) | N/A |
| SCHED-02 | Elevated task has HighestAvailable | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check RunLevel) | N/A |
| SCHED-02 | Non-elevated task has LeastPrivilege | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check RunLevel) | N/A |
| SCHED-03 | Uninstall removes task | manual | `schtasks /Query /TN "FocusDaemon"` (exit code non-zero = removed) | N/A |

### Sampling Rate
- **Per task commit:** Build installer (`iscc installer/focus.iss`), inspect .iss changes
- **Per wave merge:** Full install/uninstall cycle with task verification
- **Phase gate:** Manual test matrix covering both checkbox combinations

### Wave 0 Gaps
- [ ] `installer/test-scheduler.ps1` -- PowerShell script to automate verification: build installer, run silent install, query task, verify XML, run uninstall, verify cleanup
- Note: Full automated testing of installer wizard pages requires interactive UI which cannot be automated in CI. The PowerShell script can verify post-install state via schtasks /Query.

*(Existing Phase 16 test script `installer/test-installer.ps1` can be extended or a new script created.)*

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn - schtasks create](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create) -- Full parameter reference including /XML, /F, /RL, /SC ONLOGON
- [Microsoft Learn - Logon Trigger Example XML](https://learn.microsoft.com/en-us/windows/win32/taskschd/logon-trigger-example--xml-) -- Complete XML structure for logon trigger tasks
- [Microsoft Learn - ExecutionTimeLimit Element](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-executiontimelimit-settingstype-element) -- PT0S for indefinite execution, ISO 8601 duration format
- [Inno Setup - CreateInputOptionPage](https://jrsoftware.org/ishelp/topic_isxfunc_createinputoptionpage.htm) -- Checkbox/radio wizard page creation
- [Inno Setup - ShellExec](https://jrsoftware.org/ishelp/topic_isxfunc_shellexec.htm) -- Verb, params, wait, error code semantics
- [Inno Setup - Exec](https://jrsoftware.org/ishelp/topic_isxfunc_exec.htm) -- Direct execution with ResultCode
- [Inno Setup - Custom Wizard Pages](https://jrsoftware.org/ishelp/topic_scriptpages.htm) -- Page positioning with AfterID

### Secondary (MEDIUM confidence)
- [Inno Setup CodeDlg.iss example](https://github.com/HeliumProject/InnoSetup/blob/master/Examples/CodeDlg.iss) -- Verified CreateInputOptionPage usage patterns
- [SS64 schtasks reference](https://ss64.com/nt/schtasks.html) -- Cross-referenced CLI flags

### Tertiary (LOW confidence)
- [GitHub Gist - Scheduled task no execution time limit](https://gist.github.com/PlagueHO/15120718e869c0f5f281e43f378bc5b0) -- PowerShell approach (confirmed PT0S pattern)
- Multiple forum posts confirming ONLOGON requires admin -- validated against official docs

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- schtasks.exe and Inno Setup Pascal Script are well-documented, stable APIs
- Architecture (XML approach): HIGH -- ExecutionTimeLimit=PT0S is documented in official Microsoft schema; XML import is the documented approach
- Architecture (UAC elevation): HIGH -- ShellExec "runas" is a standard Inno Setup pattern; ONLOGON requiring admin is confirmed by multiple sources and official docs
- Pitfalls: HIGH -- All pitfalls verified against official documentation (ExecutionTimeLimit default, ONLOGON admin requirement, /F flag behavior)
- Code examples: MEDIUM -- Pascal Script examples are synthesized from documented APIs; not copy-pasted from running code. Exact encoding behavior of SaveStringToFile with XML needs runtime validation.

**Research date:** 2026-03-06
**Valid until:** 2026-04-06 (stable APIs, 30-day validity)
