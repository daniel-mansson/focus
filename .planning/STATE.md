---
gsd_state_version: 1.0
milestone: v3.1
milestone_name: Window Management
status: unknown
last_updated: "2026-03-02T23:58:54.552Z"
progress:
  total_phases: 3
  completed_phases: 3
  total_plans: 8
  completed_plans: 8
---

---
gsd_state_version: 1.0
milestone: v3.1
milestone_name: Window Management
status: in_progress
last_updated: "2026-03-03"
progress:
  total_phases: 3
  completed_phases: 1
  total_plans: 8
  completed_plans: 7
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-02)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** v3.1 Window Management — Phase 12 Plan 02 complete (mode-aware overlay: amber/cyan borders and DIB-rasterized arrow indicators for Move/Grow modes)

## Current Position

Phase: 12 of 12 (Cross-Monitor and Overlay Integration) — COMPLETE
Plan: 2 of 2 (plan 02 complete)
Status: Phase 12 complete — all plans done
Last activity: 2026-03-03 - Completed 12-02: mode-aware overlay indicators (OVRL-01, OVRL-02, OVRL-03, OVRL-04)

Progress: [████████░░] 80%

## Performance Metrics

**Velocity:**
- Total plans completed: 5 (v3.1)
- Average duration: 4 min
- Total execution time: 20 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 10 - Grid Infrastructure | 3/3 | 12 min | 4 min |
| 11 - Move and Resize | 3/5 | 8 min | 3 min |
| 12 - Cross-Monitor and Overlay | 2/2 | 8 min | 4 min |

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
- **NearestGridLineFloor/Ceiling for directional snap**: GridCalculator gains Floor (for leftward/upward snap) and Ceiling (for rightward/downward snap); ComputeMove keeps bidirectional NearestGridLine (from 11-02).
- **ComputeShrink edge mapping corrected**: shrink direction names the side that stays FIXED; opposite edge contracts — shrink-up moves bottom edge upward, shrink-down moves top edge downward, etc. (from 11-02).
- **Post-computation no-op guard in ComputeShrink**: if newVisW >= visW && newVisH >= visH return win unchanged — prevents position-only SetWindowPos call at OS min-track size (from 11-02).
- **Shift filter removed from CapsLock handler**: Shift+CapsLock now activates overlay, enabling Shift-first workflow for grow mode; Alt and Ctrl filters remain (from 11-02).
- **RefreshForegroundOverlayOnly vs ShowOverlaysForCurrentForeground (from 11-03)**: During Move/Grow/Shrink only the active window border should be visible; navigate targets are distracting and wrong. New method: HideAll then ShowForegroundOverlay only.
- **_capsLockHeld guard on RefreshForegroundOverlayOnly (from 11-03)**: Prevents stale overlay flash if CapsLock released between direction keydown and STA dispatch execution — OnReleasedSta already HideAll'd.
- **Post-ComputeMove boundary check for cross-monitor (from 12-01)**: Cross-monitor check uses post-ComputeMove visible bounds (not original visRect) — correctly catches transitions from one step before boundary where ComputeMove clamps to boundary.
- **TryGetCrossMonitorTarget no inner boundary check (from 12-01)**: Outer MoveOrResize already verified atBoundary using post-ComputeMove bounds; inner check on original visRect would miss normal transitions.
- **rcMonitor vs rcWork separation for cross-monitor (from 12-01)**: FindAdjacentMonitor uses rcMonitor edges for adjacency detection; ComputeCrossMonitorPosition uses rcWork for all placement math.
- **OnModeEntered accepts WindowMode parameter (from 12-01)**: Fixed pre-existing signature mismatch; DaemonCommand was already passing mode, OverlayOrchestrator now accepts it for plan 02 arrow rendering.
- **_currentMode set before _staDispatcher.Invoke (from 12-02)**: Worker thread writes _currentMode before synchronous Invoke; STA thread reads it inside lambda — no concurrent mutation, no lock needed.
- **Mode colors (from 12-02)**: Move = 0xE0FF9900 amber, Grow = 0xE000CCBB cyan, Navigate = 0xE0FFFFFF white — applied to both border and arrows.
- **DIB-local arrow coordinates (from 12-02)**: Arrow positions in ArrowRenderer use (cx=width/2, cy=height/2) in DIB-local space, not screen coordinates — same pattern as BorderRenderer.
- **ShowOverlaysForActivation reads GetKeyState at CAPS-hold time (from 12-02)**: Detects pre-existing modifier state and sets _currentMode so correct arrows appear even if modifier was pressed before CAPS.

### Blockers/Concerns

- **DPI virtualization (MEDIUM confidence):** SetWindowPos behavior when moving a DPI-unaware target window from PerMonitorV2 daemon needs empirical validation on mixed-DPI setup. Test with legacy app (Notepad) at Phase 11 start. If position is wrong by a scaling factor, add SetThreadDpiAwarenessContext override.
- **TAB chord system interaction (LOW confidence):** Whether CAPS+TAB triggers system behavior before the hook can act is empirically unknown. Test this chord manually before relying on Move mode in production.
- **SetWindowPos min-size clamping (MEDIUM confidence):** Whether SetWindowPos auto-clamps to WM_GETMINMAXINFO ptMinTrackSize needs testing with Calculator before shipping shrink.
- **Build file-lock (OPERATIONAL):** dotnet build fails to copy output EXE when focus daemon is running (MSB3027). C# compilation succeeds (zero CS errors). Kill daemon before rebuild for testing.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 1 | Change grow/shrink to LShift only with directional grow/shrink | 2026-03-02 | 69bc94c | [1-change-grow-shrink-to-lshift-only-with-d](./quick/1-change-grow-shrink-to-lshift-only-with-d/) |

## Session Continuity

Last session: 2026-03-03
Stopped at: Completed 12-02-PLAN.md (mode-aware overlay: amber/cyan borders + ArrowRenderer DIB arrows for Move/Grow modes, OVRL-01 through OVRL-04)
Resume file: None
