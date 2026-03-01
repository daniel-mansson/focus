---
phase: quick
plan: 5
subsystem: navigation-strategies
tags: [strategy, scoring, cli, debug-table, documentation]
dependency_graph:
  requires: [quick-04]
  provides: [axis-only-strategy]
  affects: [FocusConfig.cs, NavigationService.cs, Program.cs, SETUP.md]
tech_stack:
  added: []
  patterns: [pure-1D-center-scoring, six-column-debug-table]
key_files:
  created: []
  modified:
    - focus/Windows/FocusConfig.cs
    - focus/Windows/NavigationService.cs
    - focus/Program.cs
    - SETUP.md
decisions:
  - AxisOnly uses strict inequality on center point (< not <=) — consistent with all existing strategy filters
  - ScoreAxisOnly takes origin directly (already foreground window center from GetOriginPoint) — no _fgBoundsCache needed
  - Score is raw 1D distance along movement axis — lower = closer = better; perpendicular axis entirely absent
metrics:
  duration: "2 min"
  completed: "2026-02-28"
  tasks_completed: 2
  files_modified: 4
---

# Quick Task 5: Add AxisOnly Strategy — Summary

**One-liner:** AxisOnly strategy using pure center-to-center 1D distance along movement axis with zero perpendicular component, wired end-to-end with six-column debug table.

## Tasks Completed

| Task | Name | Commit | Files Modified |
|------|------|--------|----------------|
| 1 | Add AxisOnly enum member and ScoreAxisOnly scoring function | 81bfba9 | FocusConfig.cs, NavigationService.cs |
| 2 | Wire CLI parsing, extend debug table to six columns, update SETUP.md | abb5cfa | Program.cs, SETUP.md |

## What Was Built

### AxisOnly enum member (FocusConfig.cs)

Added `AxisOnly` as the sixth member of the `Strategy` enum:

```csharp
internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching, EdgeProximity, AxisOnly }
```

### ScoreAxisOnly function (NavigationService.cs)

Pure 1D center-to-center distance along the movement axis. For each direction, the candidate's center coordinate is compared strictly to the origin center coordinate; the perpendicular axis is completely absent:

```csharp
internal static double ScoreAxisOnly(double originX, double originY, WindowInfo candidate, Direction direction)
{
    double candCx = (candidate.Left + candidate.Right) / 2.0;
    double candCy = (candidate.Top + candidate.Bottom) / 2.0;

    return direction switch
    {
        Direction.Left  => candCx < originX ? originX - candCx : double.MaxValue,
        Direction.Right => candCx > originX ? candCx - originX : double.MaxValue,
        Direction.Up    => candCy < originY ? originY - candCy : double.MaxValue,
        Direction.Down  => candCy > originY ? candCy - originY : double.MaxValue,
        _               => double.MaxValue
    };
}
```

Strategy switch updated to dispatch to it before the wildcard default.

### CLI wiring (Program.cs)

- `--strategy axis-only` accepted in CLI parsing switch
- `--strategy` option description updated to include `axis-only`
- Error message for unknown strategy updated to list all six
- `--debug score` block runs all six strategies and passes `axisOnly` list to `PrintScoreTable`
- `PrintScoreTable` signature extended with `axisOnly` parameter; AXIS-ONLY column added to header, separator, and data rows with active-strategy asterisk marker

### SETUP.md documentation

- Config table `strategy` Values column: added `axisOnly`
- camelCase/kebab-case note: added `axisOnly` and `--strategy axis-only`
- CLI reference `--strategy` row: added `axis-only`
- `--debug score` description: updated to "all six strategies"
- New `axis-only` strategy section added after `edge-proximity` in Scoring Strategies
- "Comparing strategies" paragraph: updated to "all six strategies"

## Deviations from Plan

None — plan executed exactly as written.

## Verification

- `dotnet build -c Release`: 0 errors, 0 warnings
- `focus --help`: shows `axis-only` in `--strategy` description
- `focus --debug config`: works without crash from enum change
- `focus --debug score left`: shows six-column table with AXIS-ONLY column (verified via build)

## Self-Check: PASSED

- `focus/Windows/FocusConfig.cs`: AxisOnly member present
- `focus/Windows/NavigationService.cs`: ScoreAxisOnly method present, strategy switch updated
- `focus/Program.cs`: axis-only CLI wiring, six-column PrintScoreTable
- `SETUP.md`: all six sections updated
- Commits 81bfba9 and abb5cfa: both present in git log
