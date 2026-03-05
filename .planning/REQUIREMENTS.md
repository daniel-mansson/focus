# Requirements: Window Focus Navigation

**Defined:** 2026-03-05
**Core Value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## v5.0 Requirements

Requirements for installer milestone. Each maps to roadmap phases.

### Build & Packaging

- [ ] **PKG-01**: Installer produces a single setup.exe via Inno Setup with self-contained .NET publish

### Installation

- [ ] **INST-01**: User can install Focus to a chosen directory (default: %LocalAppData%\Focus)
- [ ] **INST-02**: Installer stops running daemon before upgrading files (AppMutex detection)
- [ ] **INST-03**: Installer upgrades in-place without breaking user config
- [ ] **INST-04**: User can uninstall via Add/Remove Programs (removes files + scheduled task)
- [ ] **INST-05**: Installer offers "Launch Focus now" checkbox after install

### Startup Registration

- [ ] **SCHED-01**: Installer registers daemon to start at logon via Task Scheduler
- [ ] **SCHED-02**: User can choose to run the scheduled task elevated (admin) during install
- [ ] **SCHED-03**: Uninstall removes the scheduled task cleanly

### Settings UI

- [ ] **SETS-01**: Settings form includes "Run at startup" toggle that creates/removes the scheduled task
- [ ] **SETS-02**: Settings form includes "Request elevated permissions" toggle that updates the scheduled task run level

## Future Requirements

### Distribution Polish

- **DIST-01**: Add install directory to user PATH for CLI usage from any terminal
- **DIST-02**: Optional desktop shortcut (unchecked by default)
- **DIST-03**: Code signing to eliminate SmartScreen warnings

### Auto-Update

- **AUTO-01**: Check for new versions on GitHub Releases
- **AUTO-02**: Download and apply updates automatically

## Out of Scope

| Feature | Reason |
|---------|--------|
| Code signing | Cost ($200+/yr EV certificate), SmartScreen acceptable for dev tools on GitHub |
| Auto-update mechanism | Server infrastructure premature; users re-run installer to upgrade |
| MSIX/AppX packaging | Sandbox conflicts with WH_KEYBOARD_LL hooks and SetForegroundWindow |
| Bundled .NET runtime installer | Self-contained publish eliminates runtime dependency |
| Registry Run key for startup | Cannot run elevated; Task Scheduler is strictly better |
| Delete user config on uninstall | User may reinstall; preserving config is expected behavior |
| All-users install dialog | Per-user default with /ALLUSERS CLI override covers all cases |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| PKG-01 | — | Pending |
| INST-01 | — | Pending |
| INST-02 | — | Pending |
| INST-03 | — | Pending |
| INST-04 | — | Pending |
| INST-05 | — | Pending |
| SCHED-01 | — | Pending |
| SCHED-02 | — | Pending |
| SCHED-03 | — | Pending |
| SETS-01 | — | Pending |
| SETS-02 | — | Pending |

**Coverage:**
- v5.0 requirements: 11 total
- Mapped to phases: 0
- Unmapped: 11 (pending roadmap creation)

---
*Requirements defined: 2026-03-05*
*Last updated: 2026-03-05 after initial definition*
