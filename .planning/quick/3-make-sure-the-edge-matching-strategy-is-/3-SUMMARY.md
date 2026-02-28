---
phase: quick-3
plan: 01
subsystem: documentation
tags: [docs, cli, edge-matching, strategy]
dependency_graph:
  requires: [quick-02]
  provides: [edge-matching-docs]
  affects: [focus/Program.cs, SETUP.md]
tech_stack:
  added: []
  patterns: []
key_files:
  created: []
  modified:
    - focus/Program.cs
    - SETUP.md
decisions:
  - "No code logic changed — only a string literal in the --strategy Description; build verified clean"
metrics:
  duration: "~3 min"
  completed: "2026-02-28T20:46:46Z"
  tasks_completed: 2
  files_modified: 2
---

# Quick Task 3: Make Sure Edge-Matching Strategy Is Documented Summary

**One-liner:** Added edge-matching to the --strategy help text and all SETUP.md strategy value lists so users can discover the fourth strategy.

## What Was Done

Edge-matching was added as a strategy in quick-02 but was absent from two user-facing surfaces: the `--help` output and SETUP.md. This task surgically added `edge-matching` / `edgeMatching` to every strategy value list so no documentation omits the fourth strategy.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Add edge-matching to --strategy help text in Program.cs | ad87a81 | focus/Program.cs |
| 2 | Add edge-matching to all strategy value lists in SETUP.md | 5381cb3 | SETUP.md |

## Changes Made

### focus/Program.cs (line 22)

Updated the `Description` string for the `--strategy` option:

**Before:**
```
"Scoring strategy: balanced | strong-axis-bias | closest-in-direction"
```

**After:**
```
"Scoring strategy: balanced | strong-axis-bias | closest-in-direction | edge-matching"
```

### SETUP.md (3 locations)

1. **Config fields table (line 203)** — Added `edgeMatching` as a valid `strategy` value.

2. **camelCase/kebab-case note (line 207)** — Added `edgeMatching` and `--strategy edge-matching` examples.

3. **CLI reference table (line 266)** — Added `edge-matching` as a valid `--strategy` value.

## Verification

- `dotnet build focus/focus.csproj` compiled without errors (0 warnings, 0 errors)
- `grep -n "edge-matching" focus/Program.cs` shows matches at lines 22, 88, and 93
- `grep -n "edgeMatching\|edge-matching" SETUP.md` shows matches at lines 203, 207, and 266

## Deviations from Plan

None - plan executed exactly as written.

## Self-Check: PASSED

- focus/Program.cs modified: FOUND
- SETUP.md modified: FOUND
- Commit ad87a81: FOUND
- Commit 5381cb3: FOUND
