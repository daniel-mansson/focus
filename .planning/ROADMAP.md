# Roadmap: Window Focus Navigation

## Milestones

- ✅ **v1.0 CLI** — Phases 1-3 (shipped 2026-02-28)
- ✅ **v2.0 Overlay Preview** — Phases 4-6 (shipped 2026-03-01)
- **v3.0 Integrated Navigation** — Phases 7-9 (active)

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

### v3.0 Integrated Navigation (Phases 7-9)

- [x] **Phase 7: Hotkey Wiring** — Daemon detects and suppresses direction keys while CAPSLOCK is held (completed 2026-03-01)
- [x] **Phase 8: In-Daemon Navigation** — Direction hotkeys trigger focus switching directly from the daemon (completed 2026-03-01)
- [ ] **Phase 9: Overlay Chaining** — Overlay persists through sequential moves and refreshes to new candidates

## Phase Details

### Phase 7: Hotkey Wiring
**Goal**: Direction keys (arrows and WASD) are intercepted and suppressed by the daemon when CAPSLOCK is held, and pass through normally when it is not
**Depends on**: Phase 6 (existing daemon keyboard hook infrastructure)
**Requirements**: HOTKEY-01, HOTKEY-02, HOTKEY-03, HOTKEY-04
**Success Criteria** (what must be TRUE):
  1. Pressing arrow keys while CAPSLOCK is held does not produce any input in the focused application (e.g., text cursor does not move, no scroll occurs)
  2. Pressing WASD while CAPSLOCK is held does not produce any character input in the focused application
  3. Pressing arrow keys or WASD when CAPSLOCK is not held works exactly as normal in any application
  4. The daemon log (verbose mode) reports each intercepted direction key with its mapped direction (e.g., W → up, D → right)
**Plans**: 2 plans
Plans:
- [x] 07-01-PLAN.md — Expand keyboard hook to intercept and suppress direction keys while CAPSLOCK held
- [ ] 07-02-PLAN.md — Wire direction callback into orchestrator + human verification

### Phase 8: In-Daemon Navigation
**Goal**: Users can navigate window focus using CAPSLOCK + direction keys directly from the daemon, without AutoHotkey or any external launcher
**Depends on**: Phase 7
**Requirements**: NAV-01, NAV-02, NAV-03
**Success Criteria** (what must be TRUE):
  1. Pressing CAPSLOCK + left arrow (or CAPSLOCK + A) moves focus to the best candidate window to the left, matching the behavior of `focus left` from the CLI
  2. Navigation uses the same strategy and wrap settings from config.json as the CLI does (same scoring engine, same candidates)
  3. Running `focus left` from a terminal while the daemon is active produces the same result as pressing CAPSLOCK + left from within the daemon
  4. The stateless CLI (`focus <direction>`) continues to work independently when the daemon is not running
**Plans**: 1 plan
Plans:
- [ ] 08-01-PLAN.md — Implement OnDirectionKeyDown navigation + human verification

### Phase 9: Overlay Chaining
**Goal**: Users can chain multiple directional focus moves in sequence while holding CAPSLOCK, with the overlay continuously showing the next available candidates from the current foreground window
**Depends on**: Phase 8
**Requirements**: CHAIN-01, CHAIN-02, CHAIN-03
**Success Criteria** (what must be TRUE):
  1. After pressing CAPSLOCK + a direction, the overlay borders remain visible while CAPSLOCK stays held (overlay does not flicker off and back on)
  2. Immediately after a focus move, the overlay borders update to show candidates from the newly focused window — not the previous one
  3. A user can press CAPSLOCK + left, then CAPSLOCK + up, then CAPSLOCK + right in sequence without releasing CAPSLOCK, and focus moves correctly with each keypress
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
| 7. Hotkey Wiring | 2/2 | Complete   | 2026-03-01 | — |
| 8. In-Daemon Navigation | 1/1 | Complete   | 2026-03-01 | — |
| 9. Overlay Chaining | v3.0 | 0/? | Not started | — |

---
*Full milestone details: See `.planning/milestones/` archives*
