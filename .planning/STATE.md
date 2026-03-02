---
gsd_state_version: 1.0
milestone: v3.1
milestone_name: Window Management
status: ready_to_plan
last_updated: "2026-03-02"
progress:
  total_phases: 3
  completed_phases: 0
  total_plans: 0
  completed_plans: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-02)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** v3.1 Window Management — Phase 10 (Grid Infrastructure and Modifier Wiring)

## Current Position

Phase: 10 of 12 (Grid Infrastructure and Modifier Wiring)
Plan: —
Status: Ready to plan
Last activity: 2026-03-02 — v3.1 roadmap created (phases 10-12)

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0 (v3.1)
- Average duration: — (no v3.1 plans yet)
- Total execution time: —

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

*Updated after each plan completion*

## Accumulated Context

### Key Decisions
See .planning/PROJECT.md Key Decisions table for full history.

Recent decisions affecting v3.1:
- **Dual-rect pattern**: Always read GetWindowRect for SetWindowPos inputs; use DwmGetWindowAttribute only for overlay positioning. Never mix these.
- **TAB passthrough**: Set _tabHeld = true and forward TAB via CallNextHookEx; only suppress direction keys when _tabHeld is true.
- **Left-side modifiers**: Use VK_LSHIFT (0xA0) and VK_LCONTROL (0xA2), not generic VK_SHIFT / VK_CONTROL.
- **rcWork for grid**: Use GetMonitorInfo.rcWork (not rcMonitor) for all grid origin and boundary math.

### Blockers/Concerns

- **DPI virtualization (MEDIUM confidence):** SetWindowPos behavior when moving a DPI-unaware target window from PerMonitorV2 daemon needs empirical validation on mixed-DPI setup. Test with legacy app (Notepad) at Phase 11 start. If position is wrong by a scaling factor, add SetThreadDpiAwarenessContext override.
- **TAB chord system interaction (LOW confidence):** Whether CAPS+TAB triggers system behavior before the hook can act is empirically unknown. Test this chord manually at Phase 10 start before writing suppression logic.
- **SetWindowPos min-size clamping (MEDIUM confidence):** Whether SetWindowPos auto-clamps to WM_GETMINMAXINFO ptMinTrackSize needs testing with Calculator before shipping shrink.

## Session Continuity

Last session: 2026-03-02
Stopped at: Roadmap created — ready to plan Phase 10
Resume file: None
