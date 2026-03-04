# Roadmap: Window Focus Navigation

## Milestones

- ✅ **v1.0 CLI** — Phases 1-3 (shipped 2026-02-28)
- ✅ **v2.0 Overlay Preview** — Phases 4-6 (shipped 2026-03-01)
- ✅ **v3.0 Integrated Navigation** — Phases 7-9 (shipped 2026-03-02)
- ✅ **v3.1 Window Management** — Phases 10-12 (shipped 2026-03-03)
- 🚧 **v4.0 System Tray & Settings UI** — Phases 13-15 (in progress)

## Phases

<details>
<summary>✅ v1.0 CLI (Phases 1-3) — SHIPPED 2026-02-28</summary>

- [x] Phase 1: Win32 Foundation (2/2 plans) — completed 2026-02-27
- [x] Phase 2: Navigation Pipeline (2/2 plans) — completed 2026-02-27
- [x] Phase 3: Config, Strategies & Complete CLI (2/2 plans) — completed 2026-02-28

</details>

<details>
<summary>✅ v2.0 Overlay Preview (Phases 4-6) — SHIPPED 2026-03-01</summary>

- [x] Phase 4: Daemon Core (2/2 plans) — completed 2026-03-01
- [x] Phase 5: Overlay Windows (2/2 plans) — completed 2026-03-01
- [x] Phase 6: Navigation Integration (2/2 plans) — completed 2026-03-01

</details>

<details>
<summary>✅ v3.0 Integrated Navigation (Phases 7-9) — SHIPPED 2026-03-02</summary>

- [x] Phase 7: Hotkey Wiring (2/2 plans) — completed 2026-03-01
- [x] Phase 8: In-Daemon Navigation (1/1 plan) — completed 2026-03-01
- [x] Phase 9: Overlay Chaining (user-approved) — completed 2026-03-02

</details>

<details>
<summary>✅ v3.1 Window Management (Phases 10-12) — SHIPPED 2026-03-03</summary>

- [x] Phase 10: Grid Infrastructure and Modifier Wiring (3/3 plans) — completed 2026-03-02
- [x] Phase 11: Move and Resize (Single Monitor) (3/3 plans) — completed 2026-03-02
- [x] Phase 12: Cross-Monitor and Overlay Integration (2/2 plans) — completed 2026-03-02

</details>

### 🚧 v4.0 System Tray & Settings UI (In Progress)

**Milestone Goal:** Polish the daemon's system tray presence with a custom icon, informative context menu with live daemon status and restart capability, and a WinForms settings UI for key configuration values.

- [x] **Phase 13: Tray Identity** (1 plan) - Custom icon embedded as assembly resource, tooltip set, tray presence established — completed 2026-03-04
- [ ] **Phase 14: Context Menu and Daemon Lifecycle** - Enriched right-click menu with live status labels, restart action, and daemon status tracking
- [ ] **Phase 15: Settings Form** - WinForms settings window with navigation config, overlay config, grid config, and About section

## Phase Details

### Phase 13: Tray Identity
**Goal**: The daemon has a distinct, polished presence in the system tray with a custom icon and correct tooltip
**Depends on**: Phase 12 (v3.1 shipped)
**Requirements**: ICON-01, ICON-02, ICON-03
**Success Criteria** (what must be TRUE):
  1. Hovering the tray icon shows "Focus — Navigation Daemon" as the tooltip text
  2. The tray icon displays a custom multi-size icon (visually distinct from the generic Windows application icon) at all DPI levels
  3. The .ico file is embedded in the assembly — no external file is required at runtime
**Plans**: 1 plan
Plans:
- [x] 13-01-PLAN.md -- Icon generator, assembly embedding, and tray integration (completed 2026-03-04)

### Phase 14: Context Menu and Daemon Lifecycle
**Goal**: Right-clicking the tray icon shows live daemon status and actionable menu items, and "Restart Daemon" correctly replaces the running process
**Depends on**: Phase 13
**Requirements**: MENU-01, MENU-02, MENU-03, MENU-04, MENU-05, LIFE-01, LIFE-02, LIFE-03
**Success Criteria** (what must be TRUE):
  1. Right-clicking the tray icon shows non-clickable status labels for hook status, uptime, and last action — values are current on every menu open
  2. Menu items are visually grouped by separators (status block / action block / exit)
  3. Clicking "Settings..." opens the settings window (placeholder acceptable before Phase 15 ships)
  4. Clicking "Restart Daemon" terminates the current process and starts a fresh daemon instance with no ghost processes left behind
**Plans**: 1 plan
Plans:
- [ ] 14-01-PLAN.md -- DaemonStatus wiring, context menu expansion, restart and settings handlers

### Phase 15: Settings Form
**Goal**: Users can view and edit all key configuration values through a WinForms settings window accessible from the tray menu
**Depends on**: Phase 14
**Requirements**: SETS-01, SETS-02, SETS-03, SETS-04, SETS-05, SETS-06, SETS-07, SETS-08
**Success Criteria** (what must be TRUE):
  1. Clicking "Settings..." in the tray menu opens the settings form; clicking it again when the form is already open brings the existing window to front instead of opening a second instance
  2. User can change navigation strategy, grid fractions, snap tolerance, overlay colors, and overlay delay — and save successfully writes the config file so the next keypress picks up the new values
  3. The settings form shows current keybinding reference and an About section with the project name, version, and a clickable GitHub link
  4. Saving config uses atomic write (temp file then replace) — a daemon keypress during save never produces a parse error
**Plans**: TBD

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Win32 Foundation | v1.0 | 2/2 | Complete | 2026-02-27 |
| 2. Navigation Pipeline | v1.0 | 2/2 | Complete | 2026-02-27 |
| 3. Config, Strategies & Complete CLI | v1.0 | 2/2 | Complete | 2026-02-28 |
| 4. Daemon Core | v2.0 | 2/2 | Complete | 2026-03-01 |
| 5. Overlay Windows | v2.0 | 2/2 | Complete | 2026-03-01 |
| 6. Navigation Integration | v2.0 | 2/2 | Complete | 2026-03-01 |
| 7. Hotkey Wiring | v3.0 | 2/2 | Complete | 2026-03-01 |
| 8. In-Daemon Navigation | v3.0 | 1/1 | Complete | 2026-03-01 |
| 9. Overlay Chaining | v3.0 | 0/0 | Complete | 2026-03-02 |
| 10. Grid Infrastructure and Modifier Wiring | v3.1 | 3/3 | Complete | 2026-03-02 |
| 11. Move and Resize (Single Monitor) | v3.1 | 3/3 | Complete | 2026-03-02 |
| 12. Cross-Monitor and Overlay Integration | v3.1 | 2/2 | Complete | 2026-03-02 |
| 13. Tray Identity | v4.0 | Complete    | 2026-03-04 | 2026-03-04 |
| 14. Context Menu and Daemon Lifecycle | v4.0 | 0/1 | Planned | - |
| 15. Settings Form | v4.0 | 0/? | Not started | - |

---
*Full milestone details: See `.planning/milestones/` archives*
