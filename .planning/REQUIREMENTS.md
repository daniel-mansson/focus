# Requirements: Window Focus Navigation

**Defined:** 2026-03-02
**Core Value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## v3.1 Requirements

Requirements for the Window Management milestone. Each maps to roadmap phases.

### Modifier Modes

- [x] **MODE-01**: User holds CAPS+TAB to activate window move mode
- [x] **MODE-02**: User holds CAPS+LSHIFT to activate window grow mode
- [x] **MODE-03**: User holds CAPS+LCTRL to activate window shrink mode
- [x] **MODE-04**: Normal TAB key behavior preserved when CAPS is not held

### Window Move

- [ ] **MOVE-01**: User can move the foreground window by one grid step in any direction (CAPS+TAB+direction)
- [ ] **MOVE-02**: User can press direction keys repeatedly for consecutive grid steps while CAPS+TAB held
- [ ] **MOVE-03**: Window position clamped to monitor work area boundaries

### Window Resize

- [ ] **SIZE-01**: User can grow a window edge outward by one grid step (CAPS+LSHIFT+direction)
- [ ] **SIZE-02**: User can shrink a window edge inward by one grid step (CAPS+LCTRL+direction)
- [ ] **SIZE-03**: Shrink stops at minimum window size
- [ ] **SIZE-04**: Grow stops at monitor work area boundary

### Grid & Snap

- [x] **GRID-01**: Grid step is 1/Nth of monitor dimension (configurable `gridFraction`, default 16)
- [x] **GRID-02**: Grid computed per-monitor from that monitor's work area
- [x] **GRID-03**: Misaligned windows snap to nearest grid line on first operation
- [x] **GRID-04**: Snap tolerance configurable (`snapTolerancePercent`, default 10)

### Cross-Monitor

- [ ] **XMON-01**: Moving at monitor edge transitions window to adjacent monitor
- [ ] **XMON-02**: Grid step recalculated for target monitor's dimensions

### Overlay Indicators

- [ ] **OVRL-01**: Move mode shows directional arrows in window center
- [ ] **OVRL-02**: Grow mode shows outward-pointing arrows at center of each edge
- [ ] **OVRL-03**: Shrink mode shows inward-pointing arrows at center of each edge
- [ ] **OVRL-04**: Overlay transitions are instant (no animation)

## Future Requirements

Deferred to future release. Tracked but not in current roadmap.

### Configuration Extensions

- **CFG-01**: Per-monitor grid fraction override in config
- **CFG-02**: Per-operation snap tolerance override (move vs grow vs shrink)
- **CFG-03**: Snap-to-edge variant (hold direction to snap to monitor edge)

### Advanced Operations

- **ADV-01**: `focus snap-all` CLI command to align all windows to grid
- **ADV-02**: Window position memory/restore for specific apps

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Animated window movement | Tested and rejected in v2.0 — instant is better UX |
| Pixel-exact move (no grid) | Defeats grid discipline; accumulated drift. Mouse exists for pixel control. |
| Window layout memory/restoration | Layout manager territory (FancyZones, Komorebi); far beyond scope |
| Diagonal resize (both axes) | Breaks one-edge-per-direction model; chain two operations instead |
| Automatic grid enforcement on focus change | Hostile to manually positioned windows; snap is always user-initiated |
| Modal resize mode (enter mode, arrows resize until Escape) | Conflicts with existing CAPS+direction navigation; non-modal combos are cleaner |
| Window tiling / auto-arrange | Full layout management system; not move/resize primitives |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| MODE-01 | Phase 10 | Complete |
| MODE-02 | Phase 10 | Complete |
| MODE-03 | Phase 10 | Complete |
| MODE-04 | Phase 10 | Complete |
| GRID-01 | Phase 10 | Complete |
| GRID-02 | Phase 10 | Complete |
| GRID-03 | Phase 10 | Complete |
| GRID-04 | Phase 10 | Complete |
| MOVE-01 | Phase 11 | Pending |
| MOVE-02 | Phase 11 | Pending |
| MOVE-03 | Phase 11 | Pending |
| SIZE-01 | Phase 11 | Pending |
| SIZE-02 | Phase 11 | Pending |
| SIZE-03 | Phase 11 | Pending |
| SIZE-04 | Phase 11 | Pending |
| XMON-01 | Phase 12 | Pending |
| XMON-02 | Phase 12 | Pending |
| OVRL-01 | Phase 12 | Pending |
| OVRL-02 | Phase 12 | Pending |
| OVRL-03 | Phase 12 | Pending |
| OVRL-04 | Phase 12 | Pending |

**Coverage:**
- v3.1 requirements: 20 total (+ 1 recovered: OVRL-03 was missing from original traceability table)
- Mapped to phases: 20/20
- Unmapped: 0

---
*Requirements defined: 2026-03-02*
*Last updated: 2026-03-02 after 10-02 completion (MODE-04 marked complete)*
