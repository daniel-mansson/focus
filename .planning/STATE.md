---
gsd_state_version: 1.0
milestone: v3.1
milestone_name: Window Management
status: unknown
last_updated: "2026-03-02T20:57:51.330Z"
progress:
  total_phases: 2
  completed_phases: 2
  total_plans: 4
  completed_plans: 4
---

---
gsd_state_version: 1.0
milestone: v3.1
milestone_name: Window Management
status: in_progress
last_updated: "2026-03-02"
progress:
  total_phases: 3
  completed_phases: 1
  total_plans: 6
  completed_plans: 4
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-02)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** v3.1 Window Management — Phase 11 Plan 01 complete (WindowManagerService: Move, Grow, Shrink operations)

## Current Position

Phase: 11 of 12 (Move and Resize Single Monitor) — IN PROGRESS
Plan: 1 of 1 (plan 01 complete)
Status: Phase 11 Plan 01 complete
Last activity: 2026-03-02 — Completed 11-01 (WindowManagerService: grid-snapped Move/Grow/Shrink wired into OverlayOrchestrator)

Progress: [███░░░░░░░] 30%

## Performance Metrics

**Velocity:**
- Total plans completed: 4 (v3.1)
- Average duration: 5 min
- Total execution time: 19 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 10 - Grid Infrastructure | 3/3 | 12 min | 4 min |
| 11 - Move and Resize | 1/1 | 7 min | 7 min |

*Updated after each plan completion*

## Accumulated Context

### Key Decisions
See .planning/PROJECT.md Key Decisions table for full history.

Recent decisions affecting v3.1:
- **Dual-rect pattern**: Always read GetWindowRect for SetWindowPos inputs; use DwmGetWindowAttribute only for overlay positioning. Never mix these.
- **TAB interception placement**: TAB block placed before number key block in HookCallback — CAPS+TAB caught before number-key path.
- **_tabHeld master switch**: _tabHeld cleared on CAPS release (Pitfall 1 fix: out-of-sync if CAPS released before TAB).
- **Mode-at-event-time**: WindowMode derived at direction keydown from snapshot of _tabHeld+modifiers (Pitfall 2 fix: avoids race).
- **Left-side modifiers**: Use VK_LSHIFT (0xA0) and VK_LCONTROL (0xA2) for mode; keep VK_SHIFT/VK_CONTROL for CAPS filter (intentional: filter ANY shift/ctrl+CAPS combo).
- **rcWork for grid**: Use GetMonitorInfo.rcWork (not rcMonitor) for all grid origin and boundary math.
- **GridCalculator Win32-free**: GridCalculator takes explicit int params (not RECT structs) so the class has no Win32 type imports and is trivially testable.
- **NearestGridLine origin param**: Takes monitor origin to handle multi-monitor virtual-screen coordinates correctly.
- **WindowMode.Navigate default**: Existing CAPS+direction and number key events compile unchanged (positional args only).
- **_tabHeld repeat guard in CapsLockMonitor**: Mirrored _isHeld pattern for TAB; bool field declared alongside _isHeld, cleared in keyup handler and ResetState() — log fires exactly once per TAB press.
- **Maximized window guard (refuse)**: IsZoomed returns silently — do NOT restore before moving (locked decision from 11-01).
- **Border offsets computed per-call**: From GetWindowRect vs DWMWA_EXTENDED_FRAME_BOUNDS difference — never hard-coded 7px (from 11-01).
- **ComputeGrow/Shrink snap on moving edge**: Snap check applies to the EDGE being moved, not window origin; snap origin always work.left/work.top (from 11-01).
- **Minimum size guard is pre-check**: if (visW <= stepX) return win unchanged — hard no-op, no SetWindowPos call (from 11-01).

### Blockers/Concerns

- **DPI virtualization (MEDIUM confidence):** SetWindowPos behavior when moving a DPI-unaware target window from PerMonitorV2 daemon needs empirical validation on mixed-DPI setup. Test with legacy app (Notepad) at Phase 11 start. If position is wrong by a scaling factor, add SetThreadDpiAwarenessContext override.
- **TAB chord system interaction (LOW confidence):** Whether CAPS+TAB triggers system behavior before the hook can act is empirically unknown. Test this chord manually before relying on Move mode in production.
- **SetWindowPos min-size clamping (MEDIUM confidence):** Whether SetWindowPos auto-clamps to WM_GETMINMAXINFO ptMinTrackSize needs testing with Calculator before shipping shrink.
- **Build file-lock (OPERATIONAL):** dotnet build fails to copy output EXE when focus daemon is running (MSB3027). C# compilation succeeds (zero CS errors). Kill daemon before rebuild for testing.

## Session Continuity

Last session: 2026-03-02
Stopped at: Completed 11-01-PLAN.md (WindowManagerService: grid-snapped Move/Grow/Shrink via dual-rect pattern, wired into OverlayOrchestrator)
Resume file: None
