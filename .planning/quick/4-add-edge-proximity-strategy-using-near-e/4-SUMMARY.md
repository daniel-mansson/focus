---
phase: quick
plan: 4
subsystem: navigation
tags: [csharp, dotnet, strategy, scoring, edge-proximity, cli]

# Dependency graph
requires:
  - phase: quick-02
    provides: EdgeMatching strategy and _fgBoundsCache pattern that EdgeProximity reuses
provides:
  - Strategy.EdgeProximity enum member in FocusConfig.cs
  - ScoreEdgeProximity near-edge-to-near-edge scoring function in NavigationService.cs
  - CLI parsing for --strategy edge-proximity in Program.cs
  - Five-column debug score table (adds EDGE-PROX column)
  - Documentation of edge-proximity in SETUP.md
affects: [quick, navigation-strategies]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "EdgeProximity reuses _fgBoundsCache static field — same populate-once-per-cycle pattern as EdgeMatching"
    - "Near-edge comparison: Left vs Left for Left direction, Right vs Right for Right, etc."
    - "Strict inequality (< not <=) consistent with all other strategies"
    - "Fallback to ScoreCandidate (Balanced) when foreground bounds unavailable"

key-files:
  created: []
  modified:
    - focus/Windows/FocusConfig.cs
    - focus/Windows/NavigationService.cs
    - focus/Program.cs
    - SETUP.md

key-decisions:
  - "EdgeProximity uses NEAR edge (facing direction) vs EdgeMatching which uses FAR edge — Left compares Left edges, Right compares Right edges"
  - "Perpendicular axis ignored entirely — pure 1D strategy identical in structure to EdgeMatching"
  - "Reuses _fgBoundsCache from ScoreEdgeMatching — cache is reset at GetRankedCandidates entry so both strategies share same foreground snapshot per cycle"

patterns-established:
  - "Near-edge-to-near-edge: score = |fgNearEdge - candidateNearEdge|, discard if candidate not strictly past foreground near edge"

requirements-completed: [QUICK-04]

# Metrics
duration: 3min
completed: 2026-02-28
---

# Quick Task 4: Add EdgeProximity Strategy Summary

**EdgeProximity navigation strategy using near-edge-to-near-edge 1D comparison, wired end-to-end through enum, scoring, CLI, debug table, and SETUP.md**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-02-28T20:58:09Z
- **Completed:** 2026-02-28T21:00:39Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added `EdgeProximity` to Strategy enum in FocusConfig.cs (fifth strategy)
- Implemented `ScoreEdgeProximity` in NavigationService.cs — near-edge-to-near-edge 1D comparison reusing `_fgBoundsCache` pattern from EdgeMatching
- Wired `Strategy.EdgeProximity => ScoreEdgeProximity` in GetRankedCandidates strategy switch
- Extended `--debug score` table from four to five columns with EDGE-PROX header and active-strategy marker
- Updated CLI parsing, error message, and `--strategy` description in Program.cs
- Documented edge-proximity in all relevant SETUP.md sections: config table, camelCase note, CLI reference, strategy descriptions

## Task Commits

Each task was committed atomically:

1. **Task 1: Add EdgeProximity enum and scoring function** - `9fe900f` (feat)
2. **Task 2: Wire CLI parsing and extend debug score table to five columns** - `8c3e540` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `focus/Windows/FocusConfig.cs` - Added EdgeProximity to Strategy enum
- `focus/Windows/NavigationService.cs` - Added ScoreEdgeProximity method and dispatch case
- `focus/Program.cs` - Added CLI parsing case, updated error message, --strategy description, extended PrintScoreTable to five columns
- `SETUP.md` - Updated config table, camelCase note, CLI reference, added edge-proximity strategy description

## Decisions Made

- EdgeProximity compares the NEAR edge (facing the direction of movement) rather than the FAR edge used by EdgeMatching — Left move compares Left edges, Right move compares Right edges, Up compares Top edges, Down compares Bottom edges
- Reuses `_fgBoundsCache` static field exactly as ScoreEdgeMatching — no additional caching infrastructure needed
- Falls back to ScoreCandidate (Balanced) when DwmGetWindowAttribute fails — consistent with EdgeMatching graceful degradation

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- EdgeProximity strategy is fully wired and usable via `--strategy edge-proximity` CLI flag or `"edgeProximity"` in config.json
- Debug score table now shows all five strategies side by side for comparison
- SETUP.md documents the distinction between edge-matching (far-edge) and edge-proximity (near-edge)

---
*Phase: quick-04*
*Completed: 2026-02-28*
