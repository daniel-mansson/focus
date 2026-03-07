# Roadmap: Window Focus Navigation

## Milestones

- ✅ **v1.0 CLI** — Phases 1-3 (shipped 2026-02-28)
- ✅ **v2.0 Overlay Preview** — Phases 4-6 (shipped 2026-03-01)
- ✅ **v3.0 Integrated Navigation** — Phases 7-9 (shipped 2026-03-02)
- ✅ **v3.1 Window Management** — Phases 10-12 (shipped 2026-03-03)
- ✅ **v4.0 System Tray & Settings UI** — Phases 13-15 (shipped 2026-03-05)
- 🚧 **v5.0 Installer** — Phases 16-18 (in progress)

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

<details>
<summary>✅ v4.0 System Tray & Settings UI (Phases 13-15) — SHIPPED 2026-03-05</summary>

- [x] Phase 13: Tray Identity (1/1 plan) — completed 2026-03-04
- [x] Phase 14: Context Menu and Daemon Lifecycle (1/1 plan) — completed 2026-03-04
- [x] Phase 15: Settings Form (1/1 plan) — completed 2026-03-04

</details>

### 🚧 v5.0 Installer (In Progress)

**Milestone Goal:** Package Focus as an installable application with clean install/uninstall and Task Scheduler startup registration.

- [x] **Phase 16: Build Pipeline & Installer** - Self-contained publish, Inno Setup installer with install/upgrade/uninstall lifecycle (completed 2026-03-05)
- [x] **Phase 17: Task Scheduler Integration** - Logon startup registration with elevation choice, clean removal on uninstall (completed 2026-03-07)
- [x] **Phase 18: Settings UI Startup Controls** - Runtime toggles for startup and elevation in the existing settings form (completed 2026-03-07)

## Phase Details

### Phase 16: Build Pipeline & Installer
**Goal**: User can install, upgrade, and uninstall Focus via a single setup.exe
**Depends on**: Phase 15 (v4.0 complete)
**Requirements**: PKG-01, INST-01, INST-02, INST-03, INST-04, INST-05
**Success Criteria** (what must be TRUE):
  1. Running Focus-Setup.exe installs Focus to the user-chosen directory (defaulting to %LocalAppData%\Focus) and registers in Add/Remove Programs
  2. Running Focus-Setup.exe while Focus daemon is already running stops the daemon, upgrades files in-place, and preserves existing config.json
  3. Uninstalling via Add/Remove Programs removes all installed files and shortcuts
  4. After install completes, checking "Launch Focus now" starts the daemon
**Plans:** 1/1 plans complete
Plans:
- [x] 16-01-PLAN.md — Build pipeline (build.ps1) and Inno Setup installer (focus.iss)

### Phase 17: Task Scheduler Integration
**Goal**: Daemon starts automatically at logon with user-chosen elevation level
**Depends on**: Phase 16
**Requirements**: SCHED-01, SCHED-02, SCHED-03
**Success Criteria** (what must be TRUE):
  1. When user checks "Start at logon" during install, Focus daemon launches automatically after the next Windows logon
  2. When user checks "Run elevated" during install, the scheduled task runs the daemon with admin privileges
  3. Uninstalling Focus removes the scheduled task so no orphaned logon trigger remains
**Plans:** 1/1 plans complete
Plans:
- [x] 17-01-PLAN.md — Task Scheduler wizard page, schtasks XML task creation, upgrade detection, uninstall cleanup, and C# elevation code removal

### Phase 18: Settings UI Startup Controls
**Goal**: User can manage startup registration and elevation from within the running application
**Depends on**: Phase 17
**Requirements**: SETS-01, SETS-02
**Success Criteria** (what must be TRUE):
  1. Settings form shows a "Run at startup" toggle that reflects whether a Focus scheduled task currently exists, and toggling it creates or removes the task
  2. Settings form shows a "Request elevated permissions" toggle that reflects the current task run level, and toggling it updates the scheduled task between standard and elevated
**Plans:** 1/1 plans complete
Plans:
- [ ] 18-01-PLAN.md — Startup GroupBox with schtasks-backed checkboxes for task creation/deletion and RunLevel management

## Progress

**Execution Order:**
Phases execute in numeric order: 16 → 17 → 18

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
| 13. Tray Identity | v4.0 | 1/1 | Complete | 2026-03-04 |
| 14. Context Menu and Daemon Lifecycle | v4.0 | 1/1 | Complete | 2026-03-04 |
| 15. Settings Form | v4.0 | 1/1 | Complete | 2026-03-04 |
| 16. Build Pipeline & Installer | v5.0 | 1/1 | Complete | 2026-03-05 |
| 17. Task Scheduler Integration | v5.0 | 1/1 | Complete | 2026-03-07 |
| 18. Settings UI Startup Controls | 1/1 | Complete    | 2026-03-07 | - |

---
*Full milestone details: See `.planning/milestones/` archives*
