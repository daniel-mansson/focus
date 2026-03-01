---
phase: quick
plan: 2
subsystem: navigation-strategy
tags: [strategy, navigation, scoring, cli, debug]
dependency_graph:
  requires: []
  provides: [Strategy.EdgeMatching, ScoreEdgeMatching, edge-matching-cli]
  affects: [focus/Windows/NavigationService.cs, focus/Windows/FocusConfig.cs, focus/Program.cs]
tech_stack:
  added: []
  patterns: [static-cache-per-navigation-cycle, same-edge-1D-scoring, fallback-to-balanced]
key_files:
  created: []
  modified:
    - focus/Windows/FocusConfig.cs
    - focus/Windows/NavigationService.cs
    - focus/Program.cs
decisions:
  - "_fgBoundsCache reset at GetRankedCandidates entry (not per ScoreEdgeMatching call) — ensures all candidates in one cycle share the same foreground snapshot"
  - "ScoreEdgeMatching falls back to ScoreCandidate (Balanced) when DwmGetWindowAttribute fails — graceful degradation, no crash"
  - "Edge-matching strict inequality (< not <=) — consistent with existing strategy directional filters"
metrics:
  duration: "2 min"
  completed: "2026-02-28"
  tasks_completed: 2
  files_modified: 3
---

# Quick Task 2: Create Edge-Matching Navigation Strategy — Summary

**One-liner:** 1D same-edge comparison strategy (right edges for left moves, left edges for right, bottom for up, top for down) with static per-cycle foreground bounds cache and graceful Balanced fallback.

## What Was Built

Added a fourth navigation strategy `EdgeMatching` to the window focus navigator that ignores the perpendicular axis entirely and uses pure 1D edge-to-edge distance. Unlike the existing strategies which use origin-center-to-nearest-edge scoring, EdgeMatching compares the same-type edge of the candidate to the reference edge of the foreground window.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add EdgeMatching strategy enum and scoring function | 6d596f9 | focus/Windows/FocusConfig.cs, focus/Windows/NavigationService.cs |
| 2 | Wire CLI parsing and debug score table | 9415af0 | focus/Program.cs |

## Implementation Details

### Algorithm

Direction mapping (reference edge → candidate edge compared):
- **Left**: foreground.Right vs candidate.Right — include if candidate.Right < foreground.Right
- **Right**: foreground.Left vs candidate.Left — include if candidate.Left > foreground.Left
- **Up**: foreground.Bottom vs candidate.Bottom — include if candidate.Bottom < foreground.Bottom
- **Down**: foreground.Top vs candidate.Top — include if candidate.Top > foreground.Top

Score = absolute pixel distance between the matching edges. Lower = closer = better.

### Foreground Bounds Cache

`ScoreEdgeMatching` cannot receive foreground bounds via the existing `Func<double, double, WindowInfo, Direction, double>` signature (which only provides the center point). A static `_fgBoundsCache` field is populated on the first ScoreEdgeMatching call per navigation cycle and reset at the top of `GetRankedCandidates` before each cycle begins.

### CLI Integration

- `--strategy edge-matching` accepted and maps to `Strategy.EdgeMatching`
- Unknown strategy error message updated to list `edge-matching`
- `--debug score <dir>` shows four columns: BALANCED, STRONG-AXIS, CLOSEST, EDGE-MATCH
- `--debug config` shows `strategy: EdgeMatching` when configured
- EDGE-MATCH column carries the `*` active-strategy marker when EdgeMatching is active

## Verification Results

All success criteria confirmed:

1. `dotnet build` — 0 errors, 0 warnings
2. `dotnet run -- --strategy edge-matching --debug config` — shows `strategy: EdgeMatching`
3. `dotnet run -- --debug score left` — shows four columns including EDGE-MATCH
4. All existing strategies continue to work unchanged (backward compatible)

## Deviations from Plan

None — plan executed exactly as written.

## Self-Check: PASSED

- `focus/Windows/FocusConfig.cs` — contains `EdgeMatching` in enum
- `focus/Windows/NavigationService.cs` — contains `ScoreEdgeMatching` method and `_fgBoundsCache`
- `focus/Program.cs` — contains `edge-matching` CLI case and four-column PrintScoreTable
- Commit 6d596f9 exists: `feat(quick-02): add EdgeMatching strategy enum and scoring function`
- Commit 9415af0 exists: `feat(quick-02): wire edge-matching CLI parsing and debug score column`
