# Roadmap: Window Focus Navigation

## Milestones

- ✅ **v1.0 CLI** — Phases 1-3 (shipped 2026-02-28)
- ✅ **v2.0 Overlay Preview** — Phases 4-6 (shipped 2026-03-01)
- ✅ **v3.0 Integrated Navigation** — Phases 7-9 (shipped 2026-03-02)
- 🚧 **v3.1 Window Management** — Phases 10-12 (in progress)

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

### 🚧 v3.1 Window Management (In Progress)

**Milestone Goal:** Grid-snapped window move and resize via CAPS+TAB/LSHIFT/LCTRL+direction combos, with per-monitor grid, cross-monitor transitions, and mode-specific overlay indicators.

- [x] **Phase 10: Grid Infrastructure and Modifier Wiring** — Config extensions, per-monitor grid calculation, TAB/LSHIFT/LCTRL detection in keyboard hook, modifier-aware routing through CapsLockMonitor
- [x] **Phase 11: Move and Resize (Single Monitor)** — WindowManagerService with dual-rect coordinate handling, grid snap, move and grow/shrink operations, all guards (maximized, UIPI, clamp)
- [ ] **Phase 12: Cross-Monitor and Overlay Integration** — Adjacent monitor detection, cross-monitor move transitions, mode-specific overlay indicators, overlay reposition-in-place

## Phase Details

### Phase 10: Grid Infrastructure and Modifier Wiring
**Goal**: The daemon correctly detects CAPS+TAB, CAPS+LSHIFT, and CAPS+LCTRL combos and routes them as modifier-qualified direction events; the grid step is computed per monitor from work area dimensions using configurable parameters
**Depends on**: Phase 9 (v3.0 daemon architecture)
**Requirements**: MODE-01, MODE-02, MODE-03, MODE-04, GRID-01, GRID-02, GRID-03, GRID-04
**Success Criteria** (what must be TRUE):
  1. Holding CAPS+TAB and pressing a direction key does not send TAB to the focused application (TAB is forwarded via CallNextHookEx, not suppressed, but direction keys are intercepted as move commands)
  2. Holding CAPS+LSHIFT+direction fires grow mode; holding CAPS+LCTRL+direction fires shrink mode; right Shift and right Ctrl do not trigger these modes
  3. Normal TAB with CAPS not held reaches the focused application unchanged
  4. Grid step for each monitor equals that monitor's work area width (or height) divided by gridFraction (default 16), expressed in physical pixels
  5. Config file accepts gridFraction and snapTolerancePercent keys; defaults apply when keys are absent
**Plans:** 3/3 plans complete

Plans:
- [x] 10-01-PLAN.md — Type contracts (WindowMode enum, KeyEvent upgrade, FocusConfig grid properties) and GridCalculator service
- [x] 10-02-PLAN.md — TAB interception, left-modifier detection in hook, mode-qualified routing through CapsLockMonitor to OverlayOrchestrator
- [x] 10-03-PLAN.md — Gap closure: suppress TAB held log spam on key repeat (_tabHeld repeat guard in CapsLockMonitor)

### Phase 11: Move and Resize (Single Monitor)
**Goal**: Users can move the foreground window by grid steps in any direction and grow or shrink any window edge by grid steps, with correct coordinate handling, snap-first behavior, boundary clamping, and guards against maximized and elevated windows
**Depends on**: Phase 10
**Requirements**: MOVE-01, MOVE-02, MOVE-03, SIZE-01, SIZE-02, SIZE-03, SIZE-04
**Success Criteria** (what must be TRUE):
  1. CAPS+TAB+direction moves the foreground window one grid step; repeating the direction key while CAPS+TAB are held produces consecutive steps without releasing the combo
  2. A misaligned window snaps to the nearest grid line on the first operation, then steps by one grid cell on each subsequent press
  3. Moving toward the monitor edge stops at the work area boundary (window does not go behind taskbar or off screen)
  4. CAPS+LSHIFT+direction grows the window edge in that direction by one grid step; CAPS+LCTRL+direction shrinks it; shrink stops at minimum window size and does not make the window smaller than one grid cell
  5. Attempting to move or resize an elevated (admin) window or a maximized window produces no visible error and no window change
**Plans:** 1 plan

Plans:
- [x] 11-01-PLAN.md — WindowManagerService with grid-snapped move, grow, shrink operations and OverlayOrchestrator wiring

### Phase 12: Cross-Monitor and Overlay Integration
**Goal**: Moving a window at a monitor boundary transitions it to the adjacent monitor at the correct grid position, and the overlay reflects the active mode (move/grow/shrink) with correct directional arrows throughout all operations
**Depends on**: Phase 11
**Requirements**: XMON-01, XMON-02, OVRL-01, OVRL-02, OVRL-03, OVRL-04
**Success Criteria** (what must be TRUE):
  1. When CAPS+TAB+direction pushes a window past the current monitor edge, the window appears on the adjacent monitor snapped to the first grid cell from that edge (not left at the boundary of the original monitor)
  2. Grid step immediately recalculates to the target monitor's dimensions after a cross-monitor transition
  3. While in move mode (CAPS+TAB held), the overlay shows directional arrows at the window center; while in grow mode (CAPS+LSHIFT), outward arrows appear at each edge center; while in shrink mode (CAPS+LCTRL), inward arrows appear at each edge center
  4. The overlay tracks the window's actual position after each move or resize step with no visible flicker between steps
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
| 10. Grid Infrastructure and Modifier Wiring | 3/3 | Complete    | 2026-03-02 | 2026-03-02 |
| 11. Move and Resize (Single Monitor) | v3.1 | 1/1 | Complete | 2026-03-02 |
| 12. Cross-Monitor and Overlay Integration | v3.1 | 0/? | Not started | - |

---
*Full milestone details: See `.planning/milestones/` archives*
