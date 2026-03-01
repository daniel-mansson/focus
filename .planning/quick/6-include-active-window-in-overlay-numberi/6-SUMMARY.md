---
phase: quick-6
plan: 01
subsystem: overlay
tags: [overlay, numbering, window-selection, stability]
dependency_graph:
  requires: [quick-5]
  provides: [stable-overlay-numbering]
  affects: [OverlayOrchestrator]
tech_stack:
  added: []
  patterns: [position-stable-numbering]
key_files:
  modified:
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
key_decisions:
  - "Include active window in sorted list for both overlay rendering and key activation — window number stability across navigation"
  - "Re-activating the already-focused window via CAPS+N is a harmless no-op — no special-case handling needed"
metrics:
  duration: ~3 min
  completed_date: "2026-03-02"
  tasks_completed: 1
  tasks_total: 1
  files_modified: 1
---

# Quick Task 6: Include Active Window in Overlay Numbering Summary

**One-liner:** Removed foreground-window exclusion from number overlay paths so all windows get position-stable numbers regardless of which one is focused.

## What Was Done

Two filtering steps in `OverlayOrchestrator.cs` previously excluded the current foreground window from number overlay assignment. This caused all overlay numbers to shift whenever the user navigated to a different window (because a different window was then excluded from the sorted list).

Both paths were simplified to sort the full `filtered` list via `WindowSorter.SortByPosition`:

1. `ShowOverlaysForCurrentForeground()` — the overlay label rendering path
2. `ActivateByNumberSta()` — the CAPS+number key handler

## Changes Made

### focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs

**ShowOverlaysForCurrentForeground (number overlay section):**

Removed:
```csharp
var fgHwndVal = (nint)(IntPtr)PInvoke.GetForegroundWindow();
var nonFgWindows = filtered.Where(w => w.Hwnd != fgHwndVal).ToList();
var sorted = WindowSorter.SortByPosition(nonFgWindows, config.NumberSortStrategy);
```

Replaced with:
```csharp
var sorted = WindowSorter.SortByPosition(filtered, config.NumberSortStrategy);
```

**ActivateByNumberSta:**

Removed:
```csharp
// Remove the current foreground window from the list (user is selecting "other" windows)
var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();
var candidates = filtered.Where(w => w.Hwnd != fgHwnd).ToList();
var sorted = WindowSorter.SortByPosition(candidates, config.NumberSortStrategy);
```

Replaced with:
```csharp
var sorted = WindowSorter.SortByPosition(filtered, config.NumberSortStrategy);
```

## Commits

| Task | Description | Commit |
|------|-------------|--------|
| 1 | Include active window in overlay numbering and key selection | 7c4e6fe |

## Deviations from Plan

None - plan executed exactly as written.

## Verification

- `dotnet build -c Debug` — compilation succeeds (only file-lock warnings because daemon binary was in use; no CS/FS errors)
- Both `ShowOverlaysForCurrentForeground` and `ActivateByNumberSta` now sort full filtered window list
- No references to foreground-window exclusion remain in the number overlay or activation code paths
- Foreground white border logic (separate code path) is untouched

## Self-Check: PASSED

- [x] `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` modified — FOUND
- [x] Commit 7c4e6fe — FOUND
