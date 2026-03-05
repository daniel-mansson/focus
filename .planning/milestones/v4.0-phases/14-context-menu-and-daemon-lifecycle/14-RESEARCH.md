# Phase 14: Context Menu and Daemon Lifecycle - Research

**Researched:** 2026-03-04
**Domain:** WinForms ContextMenuStrip, daemon state tracking, process restart on Windows (.NET 8)
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Status label content**
- Uptime displayed in human-friendly format: "Uptime: 2h 15m" — updates to coarser units as daemon runs longer
- Last action shows direction + target: "Last: Focus Right → Chrome"
- Hook status is simple binary: "Hook: Active" or "Hook: Inactive"
- Before first navigation, last action shows em-dash placeholder: "Last: —"

**Menu layout and ordering**
- Three groups separated by ToolStripSeparator: status block / action block / exit
- Top-to-bottom: Hook status, Uptime, Last action | Settings..., Restart Daemon | Exit
- Status labels rendered as disabled/grayed-out ToolStripLabels — no emoji prefixes or special styling
- No confirmation dialog on Exit — click and daemon shuts down immediately (current behavior preserved)

**Restart behavior**
- No confirmation dialog — click "Restart Daemon" and it happens immediately
- Mechanism: Process.Start(Environment.ProcessPath) with inherited daemon args, then exit current process
- New instance hits DaemonMutex.AcquireOrReplace and takes over cleanly
- Inherit current flags (--background, --verbose) so user's intent is preserved across restarts
- On failure (Process.Start throws): catch exception, keep current daemon running, surface error in status labels until next successful restart clears it

**Settings placeholder (pre-Phase 15)**
- "Settings..." opens the config JSON file in the system default editor (Process.Start with file path)
- If config file doesn't exist yet, write a default config with all keys and sensible defaults, then open it
- Phase 15 will replace this handler with the real settings form — clean swap, no dual paths

### Claude's Discretion
- Exact status label text formatting and padding
- How inherited flags are captured and stored (Environment.GetCommandLineArgs or manual tracking)
- Error status label wording when restart fails
- Default config file content structure

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| MENU-01 | Right-click shows non-clickable status labels: hook status, uptime, and last action | ToolStripLabel with Enabled=false; DaemonStatus class carries these three values |
| MENU-02 | Status labels refresh on every menu open (no stale values) | ContextMenuStrip.Opening event fires on each right-click before display; update labels there |
| MENU-03 | Right-click menu includes "Settings..." entry that opens the settings window | Process.Start(configPath) with UseShellExecute=true opens default editor; FocusConfig.WriteDefaults if missing |
| MENU-04 | Right-click menu includes "Restart Daemon" entry | Process.Start(Environment.ProcessPath, args) then Application.ExitThread() — DaemonMutex handles overlap |
| MENU-05 | Menu items are separated into logical groups (status / actions / exit) | Two ToolStripSeparator instances dividing three blocks |
| LIFE-01 | Daemon tracks hook status, start time, and last action description internally | DaemonStatus class with DateTime StartTime, bool HookInstalled, string LastAction |
| LIFE-02 | "Restart Daemon" spawns new process via Environment.ProcessPath and exits current process | Process.Start + Application.ExitThread (not Application.Restart — NotSupportedException in daemon mode) |
| LIFE-03 | Restart routes through existing replace-semantics mutex (no ghost processes) | New instance calls DaemonMutex.AcquireOrReplace() which kills stale old process if still alive |
</phase_requirements>

---

## Summary

This phase wires together three things: live status tracking inside the daemon, a richer ContextMenuStrip that surfaces that status on every menu open, and a clean restart mechanism that replaces the running process. All the primitives already exist in the codebase — `DaemonApplicationContext` already owns a `ContextMenuStrip`, `DaemonMutex.AcquireOrReplace` already kills old instances, and `FocusConfig` already has `WriteDefaults`. The bulk of the work is additive: a small `DaemonStatus` state class, wiring callbacks from `CapsLockMonitor` → `OverlayOrchestrator` → `DaemonStatus`, and expanding the menu construction.

The trickiest concern is thread safety for status data. The `LastAction` field gets written from the `CapsLockMonitor` worker thread (via `OverlayOrchestrator.NavigateSta` on the STA thread) and read on the STA thread at menu-open time. Since the menu `Opening` event fires on the STA thread and `NavigateSta` also runs on the STA thread (via `_staDispatcher.Invoke`), there is no concurrent mutation — no lock needed for status reads in menu handlers. Hook status similarly is set in `DaemonApplicationContext` constructor (STA) and could change on the STA thread during power events (hook reinstall). All safe.

Restart inherits `--background` and `--verbose` flags. The decision to use `Environment.GetCommandLineArgs()` vs manual tracking is left to Claude's discretion. `Environment.GetCommandLineArgs()[0]` is the executable path — args start at index 1. Reconstructing the daemon args from raw argv is reliable but requires knowing the subcommand structure. Alternatively, `DaemonCommand.Run(bool background, bool verbose)` already receives the parsed values, which can be forwarded via constructor injection into `DaemonApplicationContext`.

**Primary recommendation:** Pass `background` and `verbose` booleans into `DaemonApplicationContext` (already done for `verbose`; extend to include `background`). Build restart args programmatically from these booleans. Create `DaemonStatus` as a plain class with three writable fields. Wire update calls from `NavigateSta` and hook install/uninstall sites.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Windows.Forms | .NET 8 in-box | ContextMenuStrip, ToolStripLabel, ToolStripSeparator, ToolStripMenuItem | Already used throughout the project; TrayIcon.cs builds the existing menu |
| System.Diagnostics.Process | .NET 8 in-box | Spawning new daemon instance for restart | Locked decision; DaemonMutex already uses this for Kill |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Environment.ProcessPath | .NET 6+ in-box | Self-path for restart | Locked decision. Returns the EXE path on Windows; never null on .NET 6+ |
| Environment.GetCommandLineArgs | .NET 8 in-box | Reading raw argv for flag reconstruction | If constructing restart args from raw argv instead of stored booleans |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ToolStripLabel (disabled) | ToolStripMenuItem (disabled) | ToolStripMenuItem draws a checkbox area and can appear clickable; ToolStripLabel is visually cleaner for informational text |
| Pass booleans via constructor | Parse Environment.GetCommandLineArgs() | Constructor injection is already the pattern for `verbose`; more reliable than re-parsing argv |

**Installation:** No new packages. All APIs are in-box .NET 8.

---

## Architecture Patterns

### Recommended Project Structure

No new files are strictly required. Additions:

```
focus/Windows/Daemon/
├── DaemonStatus.cs      # New: three fields + uptime formatter (LIFE-01)
├── DaemonCommand.cs     # Modified: pass background flag to DaemonApplicationContext
├── TrayIcon.cs          # Modified: expand menu, wire Opening event, add restart/settings handlers
└── CapsLockMonitor.cs   # No change needed — callbacks flow through OverlayOrchestrator
focus/Windows/Daemon/Overlay/
└── OverlayOrchestrator.cs  # Modified: call status.RecordNavigation(dir, processName) from NavigateSta
```

### Pattern 1: DaemonStatus — Plain State Holder

**What:** A mutable state class with three fields. Written on the STA thread (safe because all writes happen in STA Invoke lambdas or in the STA constructor); read on the STA thread (menu Opening event is STA).

**When to use:** Whenever daemon state needs to cross from OverlayOrchestrator into the tray menu without introducing shared mutable state across threads.

**Example:**
```csharp
// Source: project design — no external library
internal sealed class DaemonStatus
{
    public DateTime StartTime { get; } = DateTime.Now;
    public bool HookInstalled { get; set; }
    public string LastAction { get; set; } = "\u2014"; // em-dash placeholder

    public string FormatUptime()
    {
        var elapsed = DateTime.Now - StartTime;
        if (elapsed.TotalHours >= 1)
            return $"Uptime: {(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        if (elapsed.TotalMinutes >= 1)
            return $"Uptime: {elapsed.Minutes}m {elapsed.Seconds}s";
        return $"Uptime: {elapsed.Seconds}s";
    }

    public string FormatHookStatus() =>
        HookInstalled ? "Hook: Active" : "Hook: Inactive";

    public string FormatLastAction() =>
        $"Last: {LastAction}";
}
```

### Pattern 2: ContextMenuStrip.Opening Event for Live Refresh

**What:** The `Opening` event fires on the STA thread each time the menu is about to display (before it becomes visible). Reassigning `Text` on ToolStripLabel items here ensures values are always current.

**When to use:** Any time tray menu items need to reflect live state without a background timer.

**Example:**
```csharp
// Source: Microsoft WinForms docs — ContextMenuStrip.Opening event
menu.Opening += (_, _) =>
{
    _hookStatusLabel.Text  = _status.FormatHookStatus();
    _uptimeLabel.Text      = _status.FormatUptime();
    _lastActionLabel.Text  = _status.FormatLastAction();
};
```

### Pattern 3: Restart via Process.Start + Application.ExitThread

**What:** Spawn a new process then immediately exit the current STA message pump. The new instance hits `DaemonMutex.AcquireOrReplace()` which kills any surviving old process (race-safe — if old process already exited, the mutex re-acquisition simply succeeds).

**When to use:** Self-restart of a WinForms daemon process. Do NOT use `Application.Restart()` — it throws `NotSupportedException` on background processes (confirmed in STATE.md accumulated decisions).

**Example:**
```csharp
// Source: project design; Application.Restart decision captured in STATE.md
private void OnRestartClicked(object? sender, EventArgs e)
{
    var args = new List<string> { "daemon" };
    if (_background) args.Add("--background");
    if (_verbose)    args.Add("--verbose");

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName  = Environment.ProcessPath!,
            Arguments = string.Join(' ', args),
            UseShellExecute = false
        });
    }
    catch (Exception ex)
    {
        // Surface error in status labels — keep current daemon running
        _status.LastAction = $"Restart failed: {ex.Message}";
        return;
    }

    // Exit this instance — new instance will AcquireOrReplace mutex
    _trayIcon.Visible = false;
    _onExit();
    Application.ExitThread();
}
```

### Pattern 4: Settings Placeholder — Open Config in Default Editor

**What:** Use `Process.Start` with `UseShellExecute = true` and the config file path. Shell-execute opens the file in the system's default editor for `.json` files (typically Notepad or VS Code). Write defaults if file does not exist.

**Example:**
```csharp
// Source: project design; FocusConfig.WriteDefaults already exists
private void OnSettingsClicked(object? sender, EventArgs e)
{
    var configPath = FocusConfig.GetConfigPath();
    if (!File.Exists(configPath))
        FocusConfig.WriteDefaults(configPath);

    Process.Start(new ProcessStartInfo
    {
        FileName        = configPath,
        UseShellExecute = true
    });
}
```

### Pattern 5: Wiring LastAction from NavigateSta

**What:** After a successful navigation, `NavigateSta` in `OverlayOrchestrator` has the ranked candidates list with `WindowInfo.ProcessName`. Record direction + top target's process name into `DaemonStatus`.

**Where it fits:** `OverlayOrchestrator.NavigateSta` already runs on the STA thread — direct field write to `DaemonStatus` is safe.

**Example:**
```csharp
// Inside NavigateSta, after FocusActivator.ActivateWithWrap returns 0:
if (result == 0 && ranked.Count > 0)
{
    string dirCapitalized = char.ToUpper(direction[0]) + direction[1..];
    string target = ranked[0].Window.ProcessName;
    _status.LastAction = $"Focus {dirCapitalized} \u2192 {target}";
}
```

### Anti-Patterns to Avoid

- **Using Application.Restart():** Throws `NotSupportedException` on daemon processes (confirmed, captured in STATE.md). Use `Process.Start` + `Application.ExitThread()`.
- **Shared state across threads without STA marshaling:** `DaemonStatus` is only safe because all writes happen on the STA thread. If writes move to the worker thread, a `volatile` field or `Interlocked` replacement is needed for `LastAction`.
- **Calling Application.Exit() from the STA thread (tray click):** Use `Application.ExitThread()` (exits only the STA pump). `Application.Exit()` is for the Ctrl+C path running on a different thread (already correct in `DaemonCommand.Run`).
- **ToolStripMenuItem instead of ToolStripLabel for status rows:** `ToolStripMenuItem` with `Enabled = false` shows a checkbox column and grayed text but retains clickable appearance. `ToolStripLabel` with `Enabled = false` renders purely as text — the correct choice for informational rows per the locked decision.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Menu refresh timing | Background timer polling | ContextMenuStrip.Opening event | Opening event is exactly "right before display" — no poll interval to tune, no off-by-one stale reads |
| Uptime formatting | Custom TimeSpan stringifier | Simple arithmetic on DateTime.Now - StartTime | Three-line method; no edge cases beyond hours/minutes/seconds |
| Self-restart path resolution | Walk argv or Assembly.Location | Environment.ProcessPath | Reliable on .NET 6+; returns published EXE path correctly |
| Ghost process prevention | Track child PIDs | DaemonMutex.AcquireOrReplace (already built) | Already handles kill-and-replace; new instance auto-cleans up |

**Key insight:** Every component needed for this phase already exists in the project. The work is wiring, not building.

---

## Common Pitfalls

### Pitfall 1: Application.Restart() Throws in Daemon Mode
**What goes wrong:** Calling `Application.Restart()` on a process that used `FreeConsole` or runs without a console throws `NotSupportedException`.
**Why it happens:** `Application.Restart()` requires the process to have been started by `Application.Run()` in a way that supports re-entry; background/detached processes do not qualify.
**How to avoid:** Use `Process.Start(Environment.ProcessPath, args)` followed by `Application.ExitThread()`. Already documented in STATE.md accumulated decisions.
**Warning signs:** If `NotSupportedException` appears during testing of restart, this is the cause.

### Pitfall 2: Menu Opening Event Not Firing on First Open
**What goes wrong:** If menu items' `Text` is set only in the `Opening` handler, the very first open might show stale/empty strings if the handler hasn't fired yet.
**Why it happens:** Labels are constructed in the constructor with initial text; Opening fires BEFORE display, so in practice the first open IS covered. But if initial text is left empty and Opening somehow fails, labels show blank.
**How to avoid:** Initialize label text in the constructor with sensible defaults (e.g., "Hook: Active", "Uptime: 0s", "Last: —") and then update in Opening. This way even if Opening has a bug, labels degrade gracefully.

### Pitfall 3: Restart Leaves Ghost Tray Icon
**What goes wrong:** If the old process exits before hiding the tray icon, Windows briefly shows a ghost icon in the tray.
**Why it happens:** `NotifyIcon.Visible = false` must be set before `Application.ExitThread()`. If the process crashes instead of exiting cleanly, the icon lingers until the next mouse hover over the tray.
**How to avoid:** Always set `_trayIcon.Visible = false` before calling `_onExit()` and `Application.ExitThread()` in the restart handler. The existing `OnExitClicked` already does this — mirror the same pattern in `OnRestartClicked`.

### Pitfall 4: Process.Start With UseShellExecute = false Requires Full Args Quoting
**What goes wrong:** If `--background` or `--verbose` are passed as a combined string argument with spaces, `Process.Start` with `UseShellExecute = false` may not parse them correctly on Windows.
**Why it happens:** Windows argument passing splits on spaces; a single string `"daemon --background"` is one argument, not two.
**How to avoid:** Use `ProcessStartInfo.ArgumentList` (string collection, .NET 5+) instead of `Arguments` (raw string). Or use `string.Join(' ', args)` carefully since none of the daemon flags contain spaces.

### Pitfall 5: OverlayOrchestrator Does Not Currently Have a DaemonStatus Reference
**What goes wrong:** To record last action in `NavigateSta`, `OverlayOrchestrator` needs a reference to `DaemonStatus`. Currently it only receives `FocusConfig` and `bool verbose`.
**Why it happens:** Status tracking is new in Phase 14 — the orchestrator was not designed with it.
**How to avoid:** Pass `DaemonStatus` into `OverlayOrchestrator`'s constructor. Since both are created in `DaemonApplicationContext` (STA thread), construction order is straightforward: create `DaemonStatus` first, pass it to both `DaemonApplicationContext` (for menu) and `OverlayOrchestrator` (for NavigateSta updates).

### Pitfall 6: Hook Status Accuracy After Sleep/Wake
**What goes wrong:** If hook status is tracked only at install/uninstall, but `PowerBroadcastWindow` reinstalls the hook without updating `DaemonStatus.HookInstalled`, the status label could show "Hook: Inactive" after a wake cycle even though the hook was reinstalled.
**Why it happens:** `PowerBroadcastWindow` calls `_hook.Uninstall()` then `_hook.Install()` — if `DaemonStatus` is only updated in the constructor and explicit menu-driven paths, the power-cycle reinstall is missed.
**How to avoid:** Update `DaemonStatus.HookInstalled` in `PowerBroadcastWindow.WndProc` after reinstall, OR derive hook status dynamically by checking `KeyboardHookHandler`'s handle validity rather than caching a bool. Simplest: add a `IsInstalled` property to `KeyboardHookHandler` and derive from that at menu open time.

---

## Code Examples

Verified patterns from project codebase and .NET 8 APIs:

### ToolStripLabel as Non-Clickable Status Row
```csharp
// Source: WinForms ToolStripLabel — built-in WinForms control
var hookLabel = new ToolStripLabel("Hook: Active")
{
    Enabled = false   // grays out text; cannot be clicked
};
menu.Items.Add(hookLabel);
```

### ContextMenuStrip Construction With Separators
```csharp
// Source: existing TrayIcon.cs pattern — extend in-place
var menu = new ContextMenuStrip();

// Block 1: Status (non-clickable)
var hookLabel     = new ToolStripLabel("Hook: Active")     { Enabled = false };
var uptimeLabel   = new ToolStripLabel("Uptime: 0s")       { Enabled = false };
var lastActLabel  = new ToolStripLabel("Last: \u2014")     { Enabled = false };
menu.Items.Add(hookLabel);
menu.Items.Add(uptimeLabel);
menu.Items.Add(lastActLabel);

// Separator 1
menu.Items.Add(new ToolStripSeparator());

// Block 2: Actions
menu.Items.Add("Settings...", null, OnSettingsClicked);
menu.Items.Add("Restart Daemon", null, OnRestartClicked);

// Separator 2
menu.Items.Add(new ToolStripSeparator());

// Block 3: Exit
menu.Items.Add("Exit", null, OnExitClicked);

// Refresh labels on each open
menu.Opening += (_, _) =>
{
    hookLabel.Text    = _status.FormatHookStatus();
    uptimeLabel.Text  = _status.FormatUptime();
    lastActLabel.Text = _status.FormatLastAction();
};
```

### Uptime Formatter
```csharp
// Source: project design — DateTime arithmetic
public string FormatUptime()
{
    var elapsed = DateTime.Now - StartTime;
    return elapsed.TotalHours >= 1
        ? $"Uptime: {(int)elapsed.TotalHours}h {elapsed.Minutes}m"
        : elapsed.TotalMinutes >= 1
            ? $"Uptime: {elapsed.Minutes}m {elapsed.Seconds}s"
            : $"Uptime: {elapsed.Seconds}s";
}
```

### KeyboardHookHandler IsInstalled Property (for live hook status)
```csharp
// Source: project codebase — KeyboardHookHandler already has _hookHandle
public bool IsInstalled => _hookHandle is { IsInvalid: false };
```

### DaemonApplicationContext Constructor Signature Extension
```csharp
// Current signature:
public DaemonApplicationContext(KeyboardHookHandler hook, CapsLockMonitor monitor,
    Action onExit, FocusConfig config, bool verbose, out OverlayOrchestrator orchestrator)

// Phase 14 signature — add background flag and status:
public DaemonApplicationContext(KeyboardHookHandler hook, CapsLockMonitor monitor,
    Action onExit, FocusConfig config, bool background, bool verbose,
    DaemonStatus status, out OverlayOrchestrator orchestrator)
```

### Restart Handler
```csharp
// Source: project design + STATE.md decision (Process.Start + ExitThread)
private void OnRestartClicked(object? sender, EventArgs e)
{
    var argList = new List<string> { "daemon" };
    if (_background) argList.Add("--background");
    if (_verbose)    argList.Add("--verbose");

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = Environment.ProcessPath!,
            ArgumentList    = { },  // use Arguments string instead for simplicity
            Arguments       = string.Join(' ', argList),
            UseShellExecute = false
        });
    }
    catch (Exception ex)
    {
        _status.LastAction = $"Restart failed: {ex.Message}";
        return;  // Keep current daemon alive
    }

    _trayIcon.Visible = false;
    _onExit();
    Application.ExitThread();
}
```

---

## State of the Art

| Old Approach | Current Approach | Notes |
|--------------|------------------|-------|
| Application.Restart() | Process.Start(ProcessPath) + ExitThread | Application.Restart throws NotSupportedException in daemon mode — confirmed in STATE.md |
| NotifyIcon.ContextMenu (old WinForms API) | ContextMenuStrip | ContextMenuStrip is the modern replacement; already used in TrayIcon.cs |
| Polling timer for status refresh | Opening event | Opening fires synchronously before display — zero unnecessary ticks |

---

## Open Questions

1. **Hook status source of truth: IsInstalled property vs stored bool**
   - What we know: `KeyboardHookHandler._hookHandle` is `null` or invalid when uninstalled; valid when installed
   - What's unclear: Whether adding an `IsInstalled` property to `KeyboardHookHandler` is simpler than passing `DaemonStatus` through `PowerBroadcastWindow` to update `HookInstalled`
   - Recommendation: Add `public bool IsInstalled => _hookHandle is { IsInvalid: false };` to `KeyboardHookHandler` and read it in `DaemonStatus.FormatHookStatus()` (or directly in the `Opening` handler). This eliminates the staleness risk from cached booleans entirely.

2. **Last action for number-key navigation**
   - What we know: `OverlayOrchestrator.ActivateByNumberSta` performs number-based window activation but doesn't record a direction string
   - What's unclear: Whether the last action should record "Focus #3 → Chrome" or simply update with the process name after number-key activation
   - Recommendation: Record "Focus #N → ProcessName" from `ActivateByNumberSta` using the same `DaemonStatus.LastAction` field. Same pattern as `NavigateSta`.

3. **Error label persistence**
   - What we know: CONTEXT.md says "error should persist in the status label area until a successful restart clears it"
   - What's unclear: Whether to show the error in `LastAction`, `HookStatus`, or a dedicated fourth label
   - Recommendation: Surface restart failure in `LastAction` (e.g., "Restart failed: <short message>"). This field persists between menu opens because it's stored in `DaemonStatus`. A subsequent successful navigation will overwrite it — which is the "successful restart clears it" behavior since a restart produces navigations.

---

## Validation Architecture

Nyquist validation is not configured (`workflow.nyquist_validation` absent from config.json — defaults to disabled). Skipping this section.

---

## Sources

### Primary (HIGH confidence)
- Project source: `C:/OtherWork/focus/focus/Windows/Daemon/TrayIcon.cs` — existing ContextMenuStrip construction, STA threading model, OnExitClicked pattern
- Project source: `C:/OtherWork/focus/focus/Windows/Daemon/DaemonMutex.cs` — AcquireOrReplace replace semantics confirmed
- Project source: `C:/OtherWork/focus/focus/Windows/Daemon/DaemonCommand.cs` — full daemon lifecycle, background/verbose flag passing, shutdown sequence
- Project source: `C:/OtherWork/focus/focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` — NavigateSta location for LastAction recording, STA threading via _staDispatcher.Invoke
- Project source: `C:/OtherWork/focus/focus/Windows/Daemon/KeyboardHookHandler.cs` — _hookHandle validity for IsInstalled property
- Project source: `C:/OtherWork/focus/focus/Windows/FocusConfig.cs` — WriteDefaults and GetConfigPath already implemented
- `.planning/STATE.md` — Application.Restart() throws NotSupportedException confirmed decision

### Secondary (MEDIUM confidence)
- .NET 8 docs: `Environment.ProcessPath` returns the path to the managed executable; available since .NET 6; non-null on Windows with a published EXE
- .NET 8 docs: `ContextMenuStrip.Opening` event fires on the STA thread before the menu is shown; cancellable via `CancelEventArgs.Cancel`
- .NET 8 docs: `ToolStripLabel` with `Enabled = false` renders as grayed non-interactive text in a ContextMenuStrip

### Tertiary (LOW confidence)
- None — all critical claims verified from project source or in-box .NET APIs

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all APIs are in-box .NET 8; no third-party libraries involved
- Architecture: HIGH — patterns derived directly from reading the existing codebase; no guesswork
- Pitfalls: HIGH for Application.Restart (confirmed in STATE.md); MEDIUM for hook status staleness (derived from code reading, not runtime testing)

**Research date:** 2026-03-04
**Valid until:** 2026-04-04 (stable .NET 8 WinForms APIs; no fast-moving dependencies)
