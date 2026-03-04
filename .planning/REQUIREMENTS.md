# Requirements: Window Focus Navigation

**Defined:** 2026-03-04
**Core Value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## v4.0 Requirements

Requirements for milestone v4.0 System Tray & Settings UI. Each maps to roadmap phases.

### Tray Identity

- [ ] **ICON-01**: Daemon displays a custom multi-size .ico icon in the system tray (16, 20, 24, 32px)
- [ ] **ICON-02**: Tray icon tooltip shows "Focus — Navigation Daemon" on hover
- [ ] **ICON-03**: Custom .ico is embedded as assembly resource and replaceable by swapping the file

### Context Menu

- [ ] **MENU-01**: Right-click shows non-clickable status labels: hook status, uptime, and last action
- [ ] **MENU-02**: Status labels refresh on every menu open (no stale values)
- [ ] **MENU-03**: Right-click menu includes "Settings..." entry that opens the settings window
- [ ] **MENU-04**: Right-click menu includes "Restart Daemon" entry
- [ ] **MENU-05**: Menu items are separated into logical groups (status / actions / exit)

### Settings Form

- [ ] **SETS-01**: Settings window opens as a non-modal WinForms form (single instance — focuses existing if already open)
- [ ] **SETS-02**: User can select navigation strategy from a dropdown (six strategies)
- [ ] **SETS-03**: User can edit grid fractions (gridFractionX, gridFractionY) and snap tolerance via numeric inputs
- [ ] **SETS-04**: User can pick overlay colors for each direction via system ColorDialog
- [ ] **SETS-05**: User can edit overlay delay (overlayDelayMs) via numeric input
- [ ] **SETS-06**: Settings form displays current keybindings as a reference label
- [ ] **SETS-07**: Save button writes config atomically (write .tmp, then File.Replace)
- [ ] **SETS-08**: About section shows project name, version, attribution, and GitHub link

### Daemon Lifecycle

- [ ] **LIFE-01**: Daemon tracks hook status, start time, and last action description internally
- [ ] **LIFE-02**: "Restart Daemon" spawns new process via Environment.ProcessPath and exits current process
- [ ] **LIFE-03**: Restart routes through existing replace-semantics mutex (no ghost processes)

## Future Requirements

### Settings Enhancements

- **SETS-F01**: Exclude list editor in settings form (list control for app exclusions)
- **SETS-F02**: Settings window keyboard shortcuts (Enter = Save, Escape = Close)
- **SETS-F03**: Config file path displayed in settings for advanced users
- **SETS-F04**: Cancel / discard-changes behavior on settings form close

### Tray Enhancements

- **ICON-F01**: Dynamic tooltip text showing brief live status
- **MENU-F01**: Daemon status panel in settings form with auto-refresh timer

## Out of Scope

| Feature | Reason |
|---------|--------|
| Balloon tip notifications on navigation | At 10-50 navigations/minute, maximally disruptive |
| Animated/color-changing tray icon | Draws constant attention; violates Windows notification area guidelines |
| Auto-apply settings on every keystroke | Causes continuous JSON parse errors during mid-edit |
| Custom color wheel/picker | System ColorDialog provides full RGB + hex input — sufficient |
| Minimize-to-tray for settings window | Settings is open-edit-close; no reason to persist |
| Multi-tab settings with every config key | ~10 config keys total; single pane with sections is sufficient |
| Restore defaults button | Factory defaults are arbitrary; not needed for v4.0 |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| ICON-01 | Phase 13 | Pending |
| ICON-02 | Phase 13 | Pending |
| ICON-03 | Phase 13 | Pending |
| MENU-01 | Phase 14 | Pending |
| MENU-02 | Phase 14 | Pending |
| MENU-03 | Phase 14 | Pending |
| MENU-04 | Phase 14 | Pending |
| MENU-05 | Phase 14 | Pending |
| SETS-01 | Phase 15 | Pending |
| SETS-02 | Phase 15 | Pending |
| SETS-03 | Phase 15 | Pending |
| SETS-04 | Phase 15 | Pending |
| SETS-05 | Phase 15 | Pending |
| SETS-06 | Phase 15 | Pending |
| SETS-07 | Phase 15 | Pending |
| SETS-08 | Phase 15 | Pending |
| LIFE-01 | Phase 14 | Pending |
| LIFE-02 | Phase 14 | Pending |
| LIFE-03 | Phase 14 | Pending |

**Coverage:**
- v4.0 requirements: 19 total
- Mapped to phases: 19
- Unmapped: 0

---
*Requirements defined: 2026-03-04*
*Last updated: 2026-03-04 after roadmap creation*
