# Roadmap: Window Focus Navigation

## Milestones

- ✅ **v1.0 CLI** - Phases 1-3 (shipped 2026-02-28)
- 🚧 **v2.0 Overlay Preview** - Phases 4-6 (in progress)

## Phases

<details>
<summary>✅ v1.0 CLI (Phases 1-3) - SHIPPED 2026-02-28</summary>

### Phase 1: Win32 Foundation
**Goal**: A runnable tool that correctly enumerates and filters all user-navigable windows, with debug output to validate the pipeline
**Depends on**: Nothing (first phase)
**Requirements**: ENUM-01, ENUM-02, ENUM-03, ENUM-04, ENUM-05, ENUM-06, NAV-06, DBG-01
**Success Criteria** (what must be TRUE):
  1. Running `focus --debug enumerate` prints a list of all detected windows with hwnd, title, bounds, and cloaked status — no phantom or minimized windows appear
  2. Windows on mixed-DPI monitors have accurate physical pixel bounds (not DPI-scaled logical coordinates)
  3. UWP/Store apps (Calculator, Settings, Microsoft Store) appear as single entries without duplicate HWNDs
  4. Cloaked windows (windows on other virtual desktops) are excluded from the list
**Plans:** 2/2 plans executed

Plans:
- [x] 01-01-PLAN.md — Project scaffold, CsWin32/DPI manifest, WindowInfo record, MonitorHelper
- [x] 01-02-PLAN.md — Window enumeration pipeline (Alt+Tab filter, UWP dedup) and debug enumerate CLI command

### Phase 2: Navigation Pipeline
**Goal**: A working focus switcher — invoke `focus <direction>` from AutoHotkey and the correct window receives focus, with proper exit codes
**Depends on**: Phase 1
**Requirements**: NAV-01, NAV-02, NAV-03, NAV-04, NAV-05, NAV-07, FOCUS-01, OUT-02
**Success Criteria** (what must be TRUE):
  1. Running `focus left`, `focus right`, `focus up`, `focus down` from an AutoHotkey hotkey switches focus to the most geometrically appropriate window in that direction
  2. Focus switching works across multiple monitors using virtual screen coordinates — windows on secondary monitors are reachable
  3. Exit code is 0 when a window is found and activated, 1 when no candidate exists in the given direction, and 2 on error
  4. Focus activation succeeds when invoked from AutoHotkey (not just from a terminal) — SetForegroundWindow restriction is bypassed correctly
**Plans:** 2/2 plans executed

Plans:
- [x] 02-01-PLAN.md — Direction enum, NavigationService scoring engine, MonitorHelper primary monitor fallback, NativeMethods.txt additions
- [x] 02-02-PLAN.md — FocusActivator (SendInput ALT bypass + SetForegroundWindow), Program.cs direction argument wiring, exit codes

### Phase 3: Config, Strategies & Complete CLI
**Goal**: A fully-featured v1 tool — all three weighting strategies selectable, JSON config for persistent settings, all CLI flags working, exclude list, and the complete debug surface
**Depends on**: Phase 2
**Requirements**: CFG-01, CFG-02, CFG-03, CFG-04, ENUM-07, NAV-08, NAV-09, FOCUS-02, OUT-01, OUT-03, DBG-02, DBG-03
**Success Criteria** (what must be TRUE):
  1. Running `focus --debug config` shows the fully resolved config: hardcoded defaults merged with JSON file values merged with any CLI flag overrides
  2. Running `focus --debug score <direction>` lists all candidate windows with their computed scores for each of the three strategies without switching focus
  3. The tool is completely silent on a successful focus switch; running with `--verbose` shows scored candidates to stderr
  4. Apps listed in the config exclude list (by process name with wildcard/regex support) are invisible to the navigator
  5. Wrap-around behavior (wrap / no-op / beep) is configurable in the JSON config and overridable via CLI flag
**Plans:** 2/2 plans executed

Plans:
- [x] 03-01-PLAN.md — Config infrastructure (FocusConfig POCO, ExcludeFilter, FileSystemGlobbing NuGet, MessageBeep) + scoring strategies (strong-axis-bias, closest-in-direction) in NavigationService
- [x] 03-02-PLAN.md — FocusActivator wrap-around behavior + complete CLI wiring (--strategy, --wrap, --exclude, --init-config, --debug score, --debug config, config merge)

</details>

### 🚧 v2.0 Overlay Preview (In Progress)

**Milestone Goal:** A persistent background daemon that renders directional overlay previews (colored borders) on candidate windows while CAPSLOCK is held, using a low-level keyboard hook and layered Win32 windows.

#### Phase 4: Daemon Core
**Goal**: Users can run `focus daemon` as a persistent background process that installs a global keyboard hook, detects CAPSLOCK held/released state, and shuts down cleanly — with no overlay code yet
**Depends on**: Phase 3
**Requirements**: DAEMON-01, DAEMON-02, DAEMON-04, DAEMON-05, DAEMON-06
**Success Criteria** (what must be TRUE):
  1. Running `focus daemon` starts a background process that persists until stopped — a second invocation exits immediately with an error message (single-instance mutex)
  2. Pressing and holding CAPSLOCK prints a debug log line; releasing CAPSLOCK prints another — hook fires under fullscreen app focus and continues firing after 2+ minutes idle
  3. AHK-synthesized key events (LLKHF_INJECTED) do not trigger CAPSLOCK hold detection — the overlay does not flicker when AHK fires `focus <direction>`
  4. Pressing Ctrl+C or terminating the process destroys all resources and unhooks the keyboard hook — no orphaned hooks remain after exit
**Plans**: 2 plans

Plans:
- [ ] 04-01-PLAN.md — Project setup (csproj WinForms, NativeMethods.txt), KeyEvent record, KeyboardHookHandler (WH_KEYBOARD_LL + CAPSLOCK suppression + LLKHF_INJECTED filter), CapsLockMonitor (Channel consumer + state machine), DaemonMutex (single-instance replace)
- [ ] 04-02-PLAN.md — TrayIcon (DaemonApplicationContext + NotifyIcon + wake recovery), DaemonCommand orchestrator (lifecycle management), Program.cs daemon subcommand wiring, manual verification

#### Phase 5: Overlay Windows
**Goal**: Users can see correctly rendered colored border overlays appear on screen — transparent, click-through, absent from Alt+Tab, and visually correct — positioned at a hardcoded test rectangle before navigation is wired
**Depends on**: Phase 4
**Requirements**: OVERLAY-02, RENDER-01, RENDER-02, RENDER-03, CFG-05, CFG-07
**Success Criteria** (what must be TRUE):
  1. A colored border overlay appears on screen with no interior fill — clicking through it reaches the window beneath, and focus does not shift from the active application when the overlay appears
  2. The overlay window does not appear in Alt+Tab or the taskbar, and `focus --debug enumerate` shows zero rows with the daemon process name while the daemon is running
  3. Per-direction colors (left/right/up/down, hex ARGB) read from JSON config appear correctly on the rendered borders — the default colors apply when not configured
  4. On a secondary monitor at a different DPI scale, the overlay border aligns accurately with the target window bounds
**Plans**: TBD

Plans:
- [ ] 05-01: TBD

#### Phase 6: Navigation Integration
**Goal**: Holding CAPSLOCK shows colored borders on the actual top-ranked directional candidate windows (all four directions simultaneously), updating when the foreground window changes, and dismissing instantly on CAPSLOCK release
**Depends on**: Phase 5
**Requirements**: OVERLAY-01, OVERLAY-03, OVERLAY-04, OVERLAY-05, DAEMON-03, CFG-06
**Success Criteria** (what must be TRUE):
  1. Holding CAPSLOCK shows up to four colored border overlays — one per direction — each positioned on the window that `focus <direction>` would navigate to from the current foreground window
  2. Releasing CAPSLOCK immediately removes all overlays — no flicker, no residual borders
  3. While CAPSLOCK is held, switching the foreground window (via Alt+Tab) causes overlays to reposition to reflect the new source window within one update cycle
  4. Directions with no candidate window (e.g., nothing to the left) show no overlay for that direction — the daemon does not crash or show a misplaced overlay
  5. The CAPSLOCK overlay does not appear for brief accidental presses — the activation delay (overlayDelayMs, default ~150ms, configurable in JSON) prevents spurious triggers
**Plans**: TBD

Plans:
- [ ] 06-01: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 4 → 5 → 6

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Win32 Foundation | v1.0 | 2/2 | Complete | 2026-02-27 |
| 2. Navigation Pipeline | v1.0 | 2/2 | Complete | 2026-02-27 |
| 3. Config, Strategies & Complete CLI | v1.0 | 2/2 | Complete | 2026-02-28 |
| 4. Daemon Core | 1/2 | In Progress|  | - |
| 5. Overlay Windows | v2.0 | 0/? | Not started | - |
| 6. Navigation Integration | v2.0 | 0/? | Not started | - |
