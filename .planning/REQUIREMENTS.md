# Requirements: Window Focus Navigation

**Defined:** 2026-03-01
**Core Value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## v3.0 Requirements

Requirements for v3.0 Integrated Navigation. Each maps to roadmap phases.

### Hotkey Detection

- [ ] **HOTKEY-01**: Daemon detects arrow key presses (Up/Down/Left/Right) while CAPSLOCK is held
- [ ] **HOTKEY-02**: Daemon detects WASD key presses while CAPSLOCK is held (W=up, A=left, S=down, D=right)
- [ ] **HOTKEY-03**: Direction keys (arrows + WASD) are suppressed from reaching the focused app while CAPSLOCK is held
- [ ] **HOTKEY-04**: Direction keys pass through normally when CAPSLOCK is not held

### Focus Navigation

- [ ] **NAV-01**: CAPSLOCK + direction triggers focus switch to the best candidate window in that direction
- [ ] **NAV-02**: Navigation uses the same scoring engine and config (strategy, wrap behavior) as the CLI
- [ ] **NAV-03**: Navigation works independently of overlay display (pure hotkey mode)

### Overlay Chaining

- [ ] **CHAIN-01**: Overlay stays visible after a focus move while CAPSLOCK remains held
- [ ] **CHAIN-02**: Overlay refreshes to show new directional candidates from the newly focused window
- [ ] **CHAIN-03**: User can chain multiple directional moves in sequence while holding CAPSLOCK

## Future Requirements

Deferred to future releases.

- **HOTKEY-05**: Configurable modifier key (use key other than CAPSLOCK)
- **HOTKEY-06**: Configurable direction key mappings (custom key bindings beyond arrows/WASD)

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Vim-style hjkl bindings | WASD + arrows cover both gaming and standard conventions; hjkl adds config complexity |
| Remappable modifier key | CAPSLOCK is established from v2.0; future milestone if needed |
| Mouse-based navigation | Keyboard-only tool by design |
| AutoHotkey integration/bridge | Goal is to eliminate AHK dependency, not enhance it |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| HOTKEY-01 | Phase 7 | Pending |
| HOTKEY-02 | Phase 7 | Pending |
| HOTKEY-03 | Phase 7 | Pending |
| HOTKEY-04 | Phase 7 | Pending |
| NAV-01 | Phase 8 | Pending |
| NAV-02 | Phase 8 | Pending |
| NAV-03 | Phase 8 | Pending |
| CHAIN-01 | Phase 9 | Pending |
| CHAIN-02 | Phase 9 | Pending |
| CHAIN-03 | Phase 9 | Pending |

**Coverage:**
- v3.0 requirements: 10 total
- Mapped to phases: 10
- Unmapped: 0

---
*Requirements defined: 2026-03-01*
*Last updated: 2026-03-01 after roadmap creation (phases 7-9)*
