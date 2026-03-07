# Phase 18: Settings UI Startup Controls - Research

**Researched:** 2026-03-07
**Domain:** WinForms CheckBox controls, C# Process.Start with schtasks.exe, UAC elevation via "runas" verb
**Confidence:** HIGH

## Summary

This phase adds two checkboxes to the existing WinForms SettingsForm -- "Run at startup" and "Request elevated permissions" -- that manage the FocusDaemon scheduled task at runtime. The C# implementation mirrors the installer's Pascal Script logic (BuildTaskXml, DetectExistingTask) but uses `System.Diagnostics.Process` instead of Inno Setup's Exec/ShellExec functions. The core patterns are well-established: `Process.Start` with `UseShellExecute = true` and `Verb = "runas"` for UAC elevation, `RedirectStandardOutput` (with `UseShellExecute = false`) for querying task state, and `Win32Exception` with `NativeErrorCode == 1223` for detecting UAC cancellation.

The main architectural constraint is that `UseShellExecute = true` (required for the "runas" verb to trigger UAC) is mutually exclusive with `RedirectStandardOutput = true`. This means task creation/deletion (which needs elevation) cannot capture stdout, while task querying (which needs stdout capture) runs without elevation. This maps cleanly to the design: query is read-only (no UAC), create/delete needs admin (UAC, no stdout needed).

The second constraint is that `Process.WaitForExit()` blocks the calling thread. Since SettingsForm runs on the STA/UI thread, schtasks operations should be dispatched to a background thread via `Task.Run` with `await` to keep the UI responsive during the UAC prompt and schtasks execution.

**Primary recommendation:** Use `Process.Start` with `UseShellExecute = true, Verb = "runas"` for task creation/deletion (elevated), and `Process.Start` with `UseShellExecute = false, RedirectStandardOutput = true` for task querying (non-elevated). Wrap blocking operations in `Task.Run` to avoid freezing the UI.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- New "Startup" GroupBox at the bottom of the settings form, after Keybindings
- GroupBox title is just "Startup" -- no mention of Task Scheduler mechanism
- Two checkboxes: "Run at startup" and "Request elevated permissions"
- Brief explanatory note under the elevation checkbox: "Required to navigate between admin windows" (matching installer text)
- Increase form ClientSize height from 700 to ~780 to fit the new section (no reliance on scroll)
- UAC fires immediately on toggle change -- matches auto-save pattern of all other settings
- Creating a task (toggling "Run at startup" ON) always triggers UAC via ShellExec runas (ONLOGON tasks require admin)
- Toggling elevation ON/OFF always triggers UAC (recreates task with different RunLevel)
- Deleting a task (toggling "Run at startup" OFF): try non-elevated first, fallback to UAC only if needed
- If user cancels UAC prompt: silently revert toggle to previous state, no error dialog
- "Request elevated permissions" is grayed out (disabled) when "Run at startup" is unchecked
- Unchecking "Run at startup" auto-unchecks "Request elevated permissions" (task is deleted, elevation state gone)
- On form open: always query schtasks to detect if FocusDaemon task exists and its run level -- no caching in config.json
- Toggle state reflects actual system state, not stored preferences
- Success: silent -- toggle staying in new position IS the feedback (matches auto-save pattern)
- UAC cancel: silently revert toggle
- Unexpected error (schtasks failure): revert toggle + show MessageBox with error details
- During operation: disable both checkboxes while schtasks runs, re-enable when done (prevents double-clicks)
- Use currently-running executable's path for task creation (works for both installed and dev/portable scenarios)
- Task name: "FocusDaemon" (matching Phase 17 convention)
- Task XML structure matches what the installer creates (ONLOGON trigger, no execution time limit, --background flag)

### Claude's Discretion
- C# implementation approach for schtasks invocation (Process.Start, COM interop, etc.)
- Exact task detection logic (query + XML parse vs simpler approach)
- Thread marshaling for async schtasks operations
- Checkbox layout and spacing within the GroupBox

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SETS-01 | Settings form includes "Run at startup" toggle that creates/removes the scheduled task | CheckBox in "Startup" GroupBox; Process.Start with "runas" verb for schtasks /Create and /Delete; task detection via schtasks /Query with RedirectStandardOutput; UAC cancellation handling via Win32Exception code 1223 |
| SETS-02 | Settings form includes "Request elevated permissions" toggle that updates the scheduled task run level | Second CheckBox dependent on first; recreates task with different RunLevel in XML template (HighestAvailable vs LeastPrivilege); same UAC and error handling patterns as SETS-01 |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Diagnostics.Process | .NET 8.0 | Execute schtasks.exe | Built-in .NET API; no external dependencies; supports UseShellExecute for UAC and RedirectStandardOutput for capture |
| System.Windows.Forms | .NET 8.0 | CheckBox, GroupBox, Label controls | Already used throughout SettingsForm; project has UseWindowsForms=true in csproj |
| schtasks.exe | Windows built-in | Create/delete/query scheduled tasks | Same tool used by installer; consistent task management |

### Supporting
| Library | Purpose | When to Use |
|---------|---------|-------------|
| System.ComponentModel.Win32Exception | Detect UAC cancellation (error code 1223) | Catch block around Process.Start with Verb="runas" |
| System.IO.Path | Build temp XML file path | Writing task XML to temp directory before schtasks /Create /XML |
| System.Environment | Get ProcessPath for task Command element | Currently-running executable path for task creation |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Process.Start + schtasks.exe | Task Scheduler COM API (Microsoft.Win32.TaskScheduler) | COM API is more powerful but adds NuGet dependency, more code complexity, and COM interop overhead; schtasks.exe matches the installer approach |
| String.Contains for XML parsing | XDocument/XmlDocument | Full XML parsing is overkill for checking if "HighestAvailable" appears in the output; simple string search matches the installer's approach |
| Task.Run + WaitForExit | Process.Exited event + TaskCompletionSource | More elegant but more code; Task.Run is simpler and adequate for the brief schtasks operations |

## Architecture Patterns

### Recommended Implementation Structure

The new code is self-contained within SettingsForm.cs -- no new files needed.

```
SettingsForm.cs changes:
  - BuildStartupGroup()              # New GroupBox with two checkboxes + label
  - DetectTaskState()                 # Query schtasks, return (exists, isElevated)
  - RunSchtasksElevated(args)         # Process.Start with runas verb, returns success/fail
  - RunSchtasksNonElevated(args)      # Process.Start without elevation, returns (exitCode, stdout)
  - BuildTaskXml(exePath, elevated)   # Generate XML string matching installer template
  - OnStartupToggled(sender, e)       # Checkbox handler: create or delete task
  - OnElevationToggled(sender, e)     # Checkbox handler: recreate task with new RunLevel
  - BuildUi()                         # Add BuildStartupGroup() call after BuildKeybindingsGroup()
  - Constructor                       # Call DetectTaskState() to set initial checkbox state
```

### Pattern 1: Non-Elevated schtasks Query with Output Capture
**What:** Query whether the FocusDaemon task exists and its RunLevel using `schtasks /Query /TN "FocusDaemon" /XML` with stdout redirection.
**When to use:** On form open to set initial checkbox states. No UAC needed for query.
**Example:**
```csharp
// Source: Microsoft Learn Process.StandardOutput + schtasks /Query
private static (bool exists, bool isElevated) DetectTaskState()
{
    try
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = "/Query /TN \"FocusDaemon\" /XML",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return (false, false);

        bool isElevated = output.Contains("HighestAvailable", StringComparison.Ordinal);
        return (true, isElevated);
    }
    catch
    {
        return (false, false);
    }
}
```

### Pattern 2: Elevated schtasks with UAC via "runas" Verb
**What:** Execute schtasks with elevation for task creation/deletion. Uses `UseShellExecute = true` with `Verb = "runas"` to trigger UAC.
**When to use:** Creating task (always needs admin for ONLOGON), deleting task (fallback if non-elevated delete fails), changing RunLevel.
**Example:**
```csharp
// Source: Microsoft Learn ProcessStartInfo.Verb, ProcessStartInfo.UseShellExecute
private static bool RunSchtasksElevated(string arguments)
{
    try
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = true,   // Required for Verb="runas"
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
    {
        // User cancelled UAC prompt -- not an error
        return false;
    }
}
```

### Pattern 3: Task XML Generation in C# (Mirroring Installer)
**What:** Generate the same XML template that the installer's BuildTaskXml() produces, but in C#.
**When to use:** Before calling schtasks /Create /XML.
**Example:**
```csharp
// Source: installer/focus.iss BuildTaskXml -- C# port
private static string BuildTaskXml(string appPath, bool runElevated)
{
    string runLevel = runElevated ? "HighestAvailable" : "LeastPrivilege";
    return $"""
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
              <RunLevel>{runLevel}</RunLevel>
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
              <Command>{appPath}</Command>
              <Arguments>daemon --background</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
}
```

### Pattern 4: Async Checkbox Handler with UI Locking
**What:** Disable both checkboxes during schtasks operations, run the process on a background thread, then re-enable or revert on the UI thread.
**When to use:** All checkbox CheckedChanged handlers that invoke schtasks.
**Example:**
```csharp
// Source: WinForms async pattern with Task.Run
private async void OnStartupToggled(object? sender, EventArgs e)
{
    bool wantStartup = _startupCheck.Checked;

    // Lock UI during operation
    _startupCheck.Enabled = false;
    _elevationCheck.Enabled = false;

    bool success = await Task.Run(() =>
    {
        if (wantStartup)
            return CreateTask(elevated: false);
        else
            return DeleteTask();
    });

    if (!success)
    {
        // Revert toggle silently (unhook handler to prevent recursion)
        _startupCheck.CheckedChanged -= OnStartupToggled;
        _startupCheck.Checked = !wantStartup;
        _startupCheck.CheckedChanged += OnStartupToggled;
    }

    // Update dependent controls
    _elevationCheck.Enabled = _startupCheck.Checked;
    if (!_startupCheck.Checked)
    {
        _elevationCheck.CheckedChanged -= OnElevationToggled;
        _elevationCheck.Checked = false;
        _elevationCheck.CheckedChanged += OnElevationToggled;
    }

    _startupCheck.Enabled = true;
}
```

### Anti-Patterns to Avoid
- **Storing startup state in config.json:** Toggle state must reflect actual system state from schtasks /Query, not a stored preference. The task could be created/deleted outside the app (e.g., by the installer or manually).
- **Using UseShellExecute=false with Verb="runas":** The runas verb is silently ignored when UseShellExecute is false. UAC will NOT be triggered.
- **Using UseShellExecute=true with RedirectStandardOutput:** Throws InvalidOperationException. These are mutually exclusive.
- **Calling WaitForExit() on the STA thread without Task.Run:** Blocks the entire UI during UAC prompt + schtasks execution. User cannot interact with other windows.
- **Re-entering CheckedChanged handler during programmatic revert:** When reverting a toggle on failure, the handler fires again creating infinite recursion. Must unhook before reverting.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Task XML template | Custom XML builder/serializer | Raw string interpolation matching installer template | Only two dynamic values (appPath, runLevel); must match installer's output exactly |
| UAC elevation | P/Invoke to ShellExecuteEx | Process.Start with Verb="runas" | .NET wraps ShellExecuteEx; Win32Exception with code 1223 handles UAC cancellation cleanly |
| Task detection | Task Scheduler COM API or WMI query | schtasks /Query /TN /XML with string search | Matches installer's detection approach; simple, reliable, no COM interop |
| Async process execution | Manual threads, BackgroundWorker | async void handler + Task.Run + await | Modern .NET pattern; cleanest WinForms async integration |

**Key insight:** The C# implementation should mirror the installer's Pascal Script approach as closely as possible. Both use schtasks.exe, both build the same XML template, both use the same detection logic (query + string search for HighestAvailable). This ensures the task structure is always consistent regardless of whether it was created by the installer or the settings UI.

## Common Pitfalls

### Pitfall 1: UseShellExecute and Verb="runas" Mutual Exclusivity with Redirect
**What goes wrong:** Attempting to capture schtasks output while also elevating via runas throws InvalidOperationException.
**Why it happens:** Windows shell execution (ShellExecuteEx) does not support stdout redirection. These are two fundamentally different process launch mechanisms.
**How to avoid:** Use separate methods: `RunSchtasksElevated` (UseShellExecute=true, Verb=runas, no redirect) for create/delete, and `RunSchtasksQuery` (UseShellExecute=false, RedirectStandardOutput=true) for query.
**Warning signs:** InvalidOperationException at Process.Start or exception mentioning incompatible StartInfo properties.

### Pitfall 2: Win32Exception 1223 When User Cancels UAC
**What goes wrong:** If not caught, the Win32Exception propagates and shows an unhandled exception dialog or crashes the app.
**Why it happens:** When the user clicks "No" on the UAC prompt, Windows returns ERROR_CANCELLED (1223) which .NET wraps in a Win32Exception.
**How to avoid:** Catch `Win32Exception` with `ex.NativeErrorCode == 1223` specifically. Treat it as "user declined" (silently revert toggle), not as an error.
**Warning signs:** Unhandled exception dialog after clicking "No" on UAC prompt.

### Pitfall 3: CheckedChanged Handler Recursion During Revert
**What goes wrong:** When reverting a checkbox to its previous state after UAC cancellation, the CheckedChanged handler fires again, causing infinite recursion or a second schtasks invocation.
**Why it happens:** Programmatically setting `Checked = !value` triggers the same event handler.
**How to avoid:** Detach the event handler before programmatic revert, then re-attach after: `_check.CheckedChanged -= handler; _check.Checked = value; _check.CheckedChanged += handler;`
**Warning signs:** Double UAC prompts, stack overflow, or toggle flickering.

### Pitfall 4: Blocking UI Thread During schtasks + UAC
**What goes wrong:** The settings form freezes while the UAC dialog is showing or schtasks is running. User cannot cancel, move, or close the form.
**Why it happens:** WaitForExit() blocks the calling thread. If called on the STA/UI thread, the WinForms message pump stops processing.
**How to avoid:** Wrap Process.Start + WaitForExit in `Task.Run()` and use `await` in an `async void` event handler. The UAC dialog and schtasks execution run on a background thread while the UI remains responsive.
**Warning signs:** Form title shows "(Not Responding)", unable to drag or close settings form during operation.

### Pitfall 5: Non-Elevated Delete May Fail for Admin-Created Tasks
**What goes wrong:** Toggling "Run at startup" OFF tries non-elevated delete first, but if the task was created by an admin process (or the installer ran elevated), the delete requires admin too.
**Why it happens:** Task ACLs inherit from the creator's security context. Tasks created elevated may require elevation to delete.
**How to avoid:** Per CONTEXT.md decision: try non-elevated first, then fall back to elevated (runas) if exit code is non-zero. The two-step approach minimizes unnecessary UAC prompts.
**Warning signs:** Toggle reverts even though no UAC prompt appeared (the non-elevated delete failed silently and no fallback was attempted).

### Pitfall 6: Task XML Encoding Mismatch
**What goes wrong:** schtasks /Create /XML fails with "The task XML is malformed" if the file encoding doesn't match the XML declaration.
**Why it happens:** The XML header says `encoding="UTF-16"` but File.WriteAllText defaults to UTF-8 without BOM.
**How to avoid:** Either write with `Encoding.Unicode` (UTF-16 LE with BOM) to match the header, or change the XML header to omit the encoding declaration (schtasks handles both). The installer's SaveStringToFile writes ANSI and works because all content is ASCII -- the same applies here. Safest approach: use `File.WriteAllText(path, xml, Encoding.Unicode)` to match the UTF-16 declaration.
**Warning signs:** schtasks returns exit code 1 with "malformed" error on task creation.

## Code Examples

### Complete Task Detection (Form Initialization)
```csharp
// Source: Mirrors installer/focus.iss DetectExistingTask(), adapted for C#
// Called in SettingsForm constructor to set initial checkbox state
private static (bool exists, bool isElevated) DetectTaskState()
{
    try
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = "/Query /TN \"FocusDaemon\" /XML",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return (false, false);

        bool isElevated = output.Contains("HighestAvailable", StringComparison.Ordinal);
        return (true, isElevated);
    }
    catch
    {
        return (false, false); // If schtasks fails entirely, assume no task
    }
}
```

### Complete Elevated schtasks Execution
```csharp
// Source: Microsoft Learn ProcessStartInfo.Verb + Win32Exception error code 1223
// Returns: true = operation succeeded, false = user cancelled UAC or schtasks failed
private static bool RunSchtasksElevated(string arguments)
{
    try
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
    {
        return false; // UAC cancelled
    }
}
```

### Complete Task Creation Flow
```csharp
// Source: Mirrors installer/focus.iss CurStepChanged task creation logic
private bool CreateTask(bool elevated)
{
    string exePath = Environment.ProcessPath!;
    string xml = BuildTaskXml(exePath, elevated);
    string xmlPath = Path.Combine(Path.GetTempPath(), "FocusDaemon.xml");

    File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

    try
    {
        // Delete existing task first (ignore failure)
        RunSchtasksElevated("/Delete /TN \"FocusDaemon\" /F");

        // Create task from XML -- ONLOGON always requires admin
        return RunSchtasksElevated($"/Create /XML \"{xmlPath}\" /TN \"FocusDaemon\" /F");
    }
    finally
    {
        try { File.Delete(xmlPath); } catch { }
    }
}
```

### Complete Task Deletion Flow
```csharp
// Source: Mirrors installer/focus.iss CurStepChanged task deletion logic
private static bool DeleteTask()
{
    // Try non-elevated first
    try
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = "/Delete /TN \"FocusDaemon\" /F",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        process.WaitForExit();
        if (process.ExitCode == 0) return true;
    }
    catch { }

    // Fallback to elevated
    return RunSchtasksElevated("/Delete /TN \"FocusDaemon\" /F");
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| ElevateOnStartup config property | Task Scheduler RunLevel via schtasks | Phase 17 (2026-03-07) | Config property removed; elevation managed entirely through scheduled task |
| Self-elevate in DaemonCommand.cs | Task Scheduler handles all elevation | Phase 17 (2026-03-07) | No in-app self-elevation path; simpler, more reliable |
| No runtime task management | Settings UI creates/removes tasks | This phase | Users can manage startup without re-running installer |

**Deprecated/outdated:**
- `ElevateOnStartup` config property: Removed in Phase 17; Task Scheduler RunLevel replaces it
- Self-elevate code in DaemonCommand.cs: Removed in Phase 17; no longer needed
- Any references to "Run elevated" checkbox in old SettingsForm code: Already removed in Phase 17

## Open Questions

1. **schtasks /Query permission for admin-created tasks**
   - What we know: Tasks created by the current user (even via elevated schtasks) should be queryable without elevation on the local machine. The installer creates the task using `ShellExec('runas', 'schtasks.exe', ...)` which runs in the same user context, just elevated.
   - What's unclear: Whether tasks created by the Inno Setup installer process (which may be a different user context when launched elevated) are always queryable by the non-elevated daemon.
   - Recommendation: Test both scenarios (installed + dev mode). If query fails, catch the exception and show both checkboxes as unchecked (safe default). This is LOW risk since the installer and daemon run as the same user.

2. **File.WriteAllText encoding for task XML**
   - What we know: XML header says `encoding="UTF-16"`, but the installer writes ANSI and it works because all content is ASCII.
   - What's unclear: Whether `File.WriteAllText(path, xml, Encoding.Unicode)` produces output byte-identical to what schtasks expects.
   - Recommendation: Use `Encoding.Unicode` (UTF-16 LE with BOM) to match the XML declaration. If schtasks rejects it, fall back to UTF-8 without BOM and remove encoding attribute from XML header. This should be validated during implementation.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | Manual testing + PowerShell verification script |
| Config file | `installer/test-scheduler.ps1` (existing from Phase 17) |
| Quick run command | `schtasks /Query /TN "FocusDaemon" /V` |
| Full suite command | Open settings, toggle both checkboxes through all state combinations, verify task state |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SETS-01 | Toggle ON creates FocusDaemon task | manual | `schtasks /Query /TN "FocusDaemon"` (exit code 0) | N/A |
| SETS-01 | Toggle OFF deletes FocusDaemon task | manual | `schtasks /Query /TN "FocusDaemon"` (exit code non-zero) | N/A |
| SETS-01 | Checkbox reflects actual task state on form open | manual | Open settings, compare checkbox to `schtasks /Query` output | N/A |
| SETS-01 | UAC cancel reverts toggle silently | manual | Cancel UAC prompt, verify checkbox returns to previous state | N/A |
| SETS-02 | Elevation toggle changes RunLevel to HighestAvailable | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check HighestAvailable) | N/A |
| SETS-02 | Elevation unchecked changes RunLevel to LeastPrivilege | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check LeastPrivilege) | N/A |
| SETS-02 | Elevation checkbox disabled when startup unchecked | manual | Uncheck "Run at startup", verify elevation checkbox is grayed out | N/A |

### Sampling Rate
- **Per task commit:** Build project (`dotnet build`), run daemon, open settings, verify new controls appear
- **Per wave merge:** Full manual test matrix of all toggle combinations with schtasks verification
- **Phase gate:** All 7 test behaviors verified manually with schtasks /Query confirmation

### Wave 0 Gaps
None -- existing `installer/test-scheduler.ps1` can verify task state after settings UI operations. No new test infrastructure needed since all tests are manual (WinForms UI interaction + UAC prompts cannot be automated).

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn - ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-8.0) - UseShellExecute=true required for Verb="runas"; mutually exclusive with RedirectStandardOutput
- [Microsoft Learn - Process.StandardOutput](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.standardoutput?view=net-7.0) - RequiredStandardOutput requires UseShellExecute=false
- [Microsoft Learn - ProcessStartInfo.RedirectStandardOutput](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput?view=net-9.0) - Redirect requirements and constraints
- [Microsoft Learn - schtasks query](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-query) - /TN and /XML flags for task state query
- installer/focus.iss (project source) - BuildTaskXml and DetectExistingTask reference implementations
- Phase 17 RESEARCH.md (project doc) - Task XML structure, schtasks patterns, UAC elevation via runas

### Secondary (MEDIUM confidence)
- [Cyotek - Detecting elevated process and spawning elevated](https://www.cyotek.com/blog/detecting-if-an-application-is-running-as-an-elevated-process-and-spawning-a-new-process-using-elevated-permissions) - Win32Exception code 1223 for UAC cancellation
- [Grant Winney - Using Async/Await in WinForms](https://grantwinney.com/using-async-await-and-task-to-keep-the-winforms-ui-more-responsive/) - Task.Run pattern for keeping UI responsive

### Tertiary (LOW confidence)
- Web search results on schtasks /Query permissions for non-admin users -- findings suggest current-user tasks are queryable without elevation on local machine, but edge cases exist for tasks created by different user contexts

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Process.Start, WinForms CheckBox, schtasks.exe are all well-documented, stable APIs used extensively in the project
- Architecture: HIGH - The patterns directly mirror the installer's proven Pascal Script implementation; UseShellExecute/Verb/Redirect constraints are well-documented in Microsoft Learn
- Pitfalls: HIGH - UAC cancellation (Win32Exception 1223), handler recursion, and UI blocking are all well-known WinForms patterns with documented solutions
- Code examples: HIGH - C# ports of existing installer code (BuildTaskXml, DetectExistingTask); Process.Start patterns verified against official docs

**Research date:** 2026-03-07
**Valid until:** 2026-04-07 (stable APIs, 30-day validity)
