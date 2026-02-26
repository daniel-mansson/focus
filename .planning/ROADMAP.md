# Roadmap: Window Focus Navigation

## Overview

Three phases deliver a complete directional window focus navigator for Windows. Phase 1 establishes the Win32 interop layer and window enumeration pipeline — the foundation everything else depends on. Phase 2 builds the navigation pipeline end-to-end: filtering, geometry, scoring, and focus activation for all four directions. Phase 3 adds the config system, all three weighting strategies, and the complete CLI surface. Each phase ends with a verifiable, runnable tool that can be tested via AutoHotkey before building the next layer.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Win32 Foundation** - Project scaffold, CsWin32 interop, window enumeration and filtering pipeline, DPI awareness manifest, enumerate debug command
- [ ] **Phase 2: Navigation Pipeline** - All four directions, geometry resolution, balanced scoring, focus activation, multi-monitor support, exit codes
- [ ] **Phase 3: Config, Strategies & Complete CLI** - JSON config, CLI argument parsing and overrides, all three weighting strategies, wrap-around behavior, exclude list, output behavior, score and config debug commands

## Phase Details

### Phase 1: Win32 Foundation
**Goal**: A runnable tool that correctly enumerates and filters all user-navigable windows, with debug output to validate the pipeline
**Depends on**: Nothing (first phase)
**Requirements**: ENUM-01, ENUM-02, ENUM-03, ENUM-04, ENUM-05, ENUM-06, NAV-06, DBG-01
**Success Criteria** (what must be TRUE):
  1. Running `focus --debug enumerate` prints a list of all detected windows with hwnd, title, bounds, and cloaked status — no phantom or minimized windows appear
  2. Windows on mixed-DPI monitors have accurate physical pixel bounds (not DPI-scaled logical coordinates)
  3. UWP/Store apps (Calculator, Settings, Microsoft Store) appear as single entries without duplicate HWNDs
  4. Cloaked windows (windows on other virtual desktops) are excluded from the list
**Plans:** 1/2 plans executed
- [ ] 01-01-PLAN.md — Project scaffold, CsWin32/DPI manifest, WindowInfo record, MonitorHelper
- [ ] 01-02-PLAN.md — Window enumeration pipeline (Alt+Tab filter, UWP dedup) and debug enumerate CLI command

### Phase 2: Navigation Pipeline
**Goal**: A working focus switcher — invoke `focus <direction>` from AutoHotkey and the correct window receives focus, with proper exit codes
**Depends on**: Phase 1
**Requirements**: NAV-01, NAV-02, NAV-03, NAV-04, NAV-05, NAV-07, FOCUS-01, OUT-02
**Success Criteria** (what must be TRUE):
  1. Running `focus left`, `focus right`, `focus up`, `focus down` from an AutoHotkey hotkey switches focus to the most geometrically appropriate window in that direction
  2. Focus switching works across multiple monitors using virtual screen coordinates — windows on secondary monitors are reachable
  3. Exit code is 0 when a window is found and activated, 1 when no candidate exists in the given direction, and 2 on error
  4. Focus activation succeeds when invoked from AutoHotkey (not just from a terminal) — SetForegroundWindow restriction is bypassed correctly
**Plans**: TBD

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
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Win32 Foundation | 1/2 | In Progress|  |
| 2. Navigation Pipeline | 0/? | Not started | - |
| 3. Config, Strategies & Complete CLI | 0/? | Not started | - |
