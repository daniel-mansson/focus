---
phase: quick-6
plan: "01"
subsystem: CLI / Debug Commands
tags: [debug, overlay, navigation, cli]
dependency_graph:
  requires: [NavigationService.GetRankedCandidates, OverlayManager.ShowOverlay, OverlayColors.GetArgb]
  provides: [focus --debug all]
  affects: [focus/Program.cs]
tech_stack:
  added: []
  patterns: [DoEvents + ManualResetEventSlim message pump, foreach Direction enum iteration]
key_files:
  created: []
  modified:
    - focus/Program.cs
decisions:
  - "--debug all reuses the same DoEvents + ManualResetEventSlim pattern from --debug overlay for message pump + keypress handling"
  - "No direction argument required for --debug all — it iterates all 4 directions internally"
  - "Top candidate marked with asterisk in console output to clearly indicate which window gets the overlay"
metrics:
  duration: "54s"
  completed: "2026-03-01"
  tasks_completed: 1
  files_modified: 1
---

# Quick Task 6: Add --debug all Command

One-liner: `focus --debug all` shows colored overlay borders on the top-ranked candidate window for all 4 directions simultaneously, with per-direction scoring output to the console.

## What Was Built

Added a new `--debug all` handler to `focus/Program.cs` that combines the visual output of `--debug overlay` (colored border overlays) with the scoring output of `--debug score`, but applied to all four directions at once. This gives a comprehensive at-a-glance visualization of where focus would move in every direction from the current foreground window.

### Behavior

Running `focus --debug all`:

1. Prints the foreground window bounds and active strategy
2. Iterates Left, Right, Up, Down in order
3. For each direction:
   - Calls `NavigationService.GetRankedCandidates` with the configured strategy
   - Prints `--- Direction ---` header
   - If no candidates: prints `(no candidates)` and skips
   - If candidates exist: prints up to 5 candidates ranked by score, marking #1 with an asterisk
   - Shows `OverlayManager.ShowOverlay` on the top-ranked candidate's bounds using the direction's color
4. After all directions: shows "Press any key to dismiss..."
5. Uses `Application.DoEvents + Thread.Sleep(16)` message pump with a background `ManualResetEventSlim` keypress thread
6. On keypress: calls `HideAll()` and exits cleanly

### Console output format

```
Debug ALL directions from foreground window
  Bounds: 100,200,900,800
  Strategy: Balanced

--- Left ---
  #1* score=150.0  "Visual Studio Code"  (Code)  [0,200,98,800]
  #2  score=300.0  "Terminal"  (WindowsTerminal)  [0,0,98,150]

--- Right ---
  #1* score=200.0  "Chrome"  (chrome)  [902,200,1920,800]

--- Up ---
  (no candidates)

--- Down ---
  #1* score=100.0  "Taskbar"  (explorer)  [0,802,1920,1080]

Overlays shown on top candidates. Press any key to dismiss...
Overlays dismissed.
```

### Overlay colors (from OverlayColors defaults)

- Left: muted blue (`#BF4488CC`)
- Right: muted red (`#BFCC4444`)
- Up: muted green (`#BF44AA66`)
- Down: muted amber (`#BFCCAA33`)

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add --debug all handler to Program.cs | a8fbe38 | focus/Program.cs |

## Deviations from Plan

None - plan executed exactly as written.

## Self-Check

### Created files exist

No new files created.

### Commits exist

- a8fbe38: FOUND

## Self-Check: PASSED
