# Phase 14: Context Menu and Daemon Lifecycle - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Right-clicking the tray icon shows an enriched context menu with live status labels (hook status, uptime, last action), actionable items (Settings, Restart Daemon, Exit), and a restart mechanism that cleanly replaces the running process. Status tracking is internal to the daemon.

</domain>

<decisions>
## Implementation Decisions

### Status label content
- Uptime displayed in human-friendly format: "Uptime: 2h 15m" — updates to coarser units as daemon runs longer
- Last action shows direction + target: "Last: Focus Right → Chrome"
- Hook status is simple binary: "Hook: Active" or "Hook: Inactive"
- Before first navigation, last action shows em-dash placeholder: "Last: —"

### Menu layout and ordering
- Three groups separated by ToolStripSeparator: status block / action block / exit
- Top-to-bottom: Hook status, Uptime, Last action | Settings..., Restart Daemon | Exit
- Status labels rendered as disabled/grayed-out ToolStripLabels — no emoji prefixes or special styling
- No confirmation dialog on Exit — click and daemon shuts down immediately (current behavior preserved)

### Restart behavior
- No confirmation dialog — click "Restart Daemon" and it happens immediately
- Mechanism: Process.Start(Environment.ProcessPath) with inherited daemon args, then exit current process
- New instance hits DaemonMutex.AcquireOrReplace and takes over cleanly
- Inherit current flags (--background, --verbose) so user's intent is preserved across restarts
- On failure (Process.Start throws): catch exception, keep current daemon running, surface error in status labels until next successful restart clears it

### Settings placeholder (pre-Phase 15)
- "Settings..." opens the config JSON file in the system default editor (Process.Start with file path)
- If config file doesn't exist yet, write a default config with all keys and sensible defaults, then open it
- Phase 15 will replace this handler with the real settings form — clean swap, no dual paths

### Claude's Discretion
- Exact status label text formatting and padding
- How inherited flags are captured and stored (Environment.GetCommandLineArgs or manual tracking)
- Error status label wording when restart fails
- Default config file content structure

</decisions>

<specifics>
## Specific Ideas

- Restart failure error should persist in the status label area until a successful restart clears it — user needs to see something went wrong even after closing and reopening the menu
- Config file opened via "Settings..." should show the full structure with all keys so users can see what's configurable

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `TrayIcon.cs` (DaemonApplicationContext): Already has ContextMenuStrip with "Exit" — extend this menu in-place
- `DaemonMutex.AcquireOrReplace()`: Handles process replacement via named mutex + Process.Kill — restart leverages this directly
- `FocusConfig.Load()` / `FocusConfig.GetConfigPath()`: Config loading and path resolution already exist
- `KeyboardHookHandler.Install()/Uninstall()`: Hook lifecycle methods for tracking hook status

### Established Patterns
- STA thread runs Application.Run(DaemonApplicationContext) — all UI including menu must be on this thread
- Ordered shutdown sequence in DaemonCommand.Run: RequestShutdown → Uninstall hook → Complete channel → Join STA → Dispose
- DaemonApplicationContext constructor receives dependencies via constructor injection + out parameter

### Integration Points
- Menu construction happens in DaemonApplicationContext constructor (line 50-51 of TrayIcon.cs) — expand here
- Restart needs access to current process args — DaemonCommand.Run receives (bool background, bool verbose)
- Status tracking (uptime, last action, hook status) needs new internal state — likely a small DaemonStatus class
- CapsLockMonitor callbacks (onHeld, onReleased, onDirectionKeyDown) are where "last action" events originate

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 14-context-menu-and-daemon-lifecycle*
*Context gathered: 2026-03-04*
