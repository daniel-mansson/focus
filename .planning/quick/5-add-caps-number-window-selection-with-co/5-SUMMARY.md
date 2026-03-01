---
phase: quick-5
plan: 01
subsystem: daemon/overlay/keyboard
tags: [keyboard-hook, overlay, window-selection, caps-lock, number-keys, gdi]
dependency_graph:
  requires: [KeyboardHookHandler, CapsLockMonitor, OverlayOrchestrator, OverlayManager, FocusConfig]
  provides: [CAPS+number window selection, number overlay labels, WindowSorter, NumberLabelRenderer]
  affects: [KeyboardHookHandler, CapsLockMonitor, DaemonCommand, OverlayOrchestrator, OverlayManager, FocusConfig]
tech_stack:
  added: [NumberLabelRenderer (GDI DIB text rendering), WindowSorter (horizontal sort), LOGFONTW/DrawText/SetTextColor/CreateFontIndirect PInvoke]
  patterns: [premultiplied-alpha DIB, alpha fixup for GDI ClearType text, 1-based window index from sorted list]
key_files:
  created:
    - focus/Windows/WindowSorter.cs
    - focus/Windows/Daemon/Overlay/NumberLabelRenderer.cs
  modified:
    - focus/Windows/Daemon/KeyboardHookHandler.cs
    - focus/Windows/Daemon/CapsLockMonitor.cs
    - focus/Windows/Daemon/DaemonCommand.cs
    - focus/Windows/FocusConfig.cs
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs
    - focus/NativeMethods.txt
decisions:
  - "NumberSortStrategy enum: LeftEdge (sort by Left coordinate) and Center (sort by horizontal midpoint)"
  - "Number overlay windows reuse HashSet<uint> _directionKeysHeld in CapsLockMonitor for repeat suppression — VK ranges don't overlap"
  - "CreateFontIndirect used (not CreateFont) — CsWin32 generates cleaner safe handle wrapper for it"
  - "Alpha fixup pass: GDI DrawText leaves alpha=0 on text pixels; scan for pixels where RGB!=0 and alpha==0, use max(R,G,B) as premultiplied intensity"
  - "ShowOverlaysForCurrentForeground() now loads fresh config via FocusConfig.Load() — all overlay display respects runtime config changes"
  - "Number labels rendered AFTER directional overlays so badge z-order appears on top"
metrics:
  duration: "~7 min"
  completed: "2026-03-01T22:43:23Z"
  tasks_completed: 2
  tasks_total: 3
  files_created: 2
  files_modified: 7
---

# Phase quick-5 Plan 01: CAPS+Number Window Selection Summary

**One-liner:** CAPS+1-9 activates the Nth window sorted left-to-right, with configurable numbered badge overlays rendered using GDI premultiplied-alpha DIB sections.

## What Was Built

Full CAPS+number window selection pipeline:

1. **KeyboardHookHandler** — intercepts VK 0x31-0x39 (keys 1-9) when CAPS is held; suppresses and routes to channel
2. **CapsLockMonitor** — new `onNumberKeyDown: Action<int>` parameter; `HandleNumberKeyEvent()` with repeat suppression (reuses `_directionKeysHeld` HashSet); verbose log "Number: N"
3. **DaemonCommand** — wires `onNumberKeyDown` callback; adds 3 verbose config dump lines for new properties
4. **FocusConfig** — two new enums (`NumberSortStrategy`, `NumberOverlayPosition`) and three new properties (`NumberOverlayEnabled`, `NumberOverlayPosition`, `NumberSortStrategy`)
5. **WindowSorter** — `SortByPosition()` static method with LeftEdge and Center strategies; tie-breaks by Top edge
6. **OverlayOrchestrator** — `OnNumberKeyDown()` marshals to STA thread; `ActivateByNumberSta()` enumerates, filters, sorts, picks Nth window; `ShowOverlaysForCurrentForeground()` now loads fresh config and appends number label rendering
7. **OverlayManager** — 9-window `_numberWindows` pool; `ShowNumberLabel()` delegates to `NumberLabelRenderer.PaintNumberLabel()`; `HideAll()` and `Dispose()` updated
8. **NumberLabelRenderer** — renders 28x28 rounded-rect badge with digit 1-9 using: pixel-by-pixel background fill (premultiplied), GDI DrawText for text, alpha fixup pass for ClearType pixels, `UpdateLayeredWindow` for final compositing

## Decisions Made

- `NumberSortStrategy.LeftEdge` is the default — sorts by window's Left coordinate (predictable with tiled layouts)
- `NumberOverlayPosition.TopLeft` is the default — visible for most windows
- Reused `_directionKeysHeld` HashSet in CapsLockMonitor for number key repeat suppression (VK ranges 0x25-0x57 vs 0x31-0x39 have some overlap with WASD but number-key-while-caps-held is exclusive)
- `CreateFontIndirect` preferred over `CreateFont` — CsWin32 generates a `DeleteObjectSafeHandle` overload using `in LOGFONTW` parameter, enabling `using var hFont` pattern
- `DrawText` receives `char*` via `fixed` block since CsWin32's `PCWSTR` overload doesn't accept `string` directly
- Alpha fixup: GDI writes RGB but zeroes alpha in layered windows; pixels with `alpha==0 && (R|G|B)!=0` are text pixels — premultiply white intensity as `max(R,G,B)`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] LOGFONTW faceName field is __char_32, not Span<char>**
- **Found during:** Task 2 compilation
- **Issue:** Plan used `"Segoe UI".AsSpan().CopyTo(lf.lfFaceName)` — `__char_32` has no implicit `Span<char>` conversion
- **Fix:** Used `lf.lfFaceName.AsSpan()` — `__char_32` exposes `AsSpan()` method returning `Span<char>`
- **Files modified:** `NumberLabelRenderer.cs`
- **Commit:** 5d38aa5

**2. [Rule 1 - Bug] FONT_WEIGHT enum not generated by CsWin32; byte constant overflow**
- **Found during:** Task 2 compilation
- **Issue:** Plan referenced `FONT_WEIGHT.FW_BOLD` which is not generated; also `const uint` to `byte` in constant context produced compile errors
- **Fix:** Used raw int value `700` for FW_BOLD; extracted bg color as individual `const byte` fields (`BgAlpha`, `BgR`, `BgG`, `BgB`) to avoid constant folding overflow
- **Files modified:** `NumberLabelRenderer.cs`
- **Commit:** 5d38aa5

**3. [Rule 1 - Bug] DrawText PCWSTR argument does not accept string**
- **Found during:** Task 2 compilation
- **Issue:** Plan used `DrawText(memDC, text, -1, &textRect, ...)` with `string text` — CsWin32 generates `DrawText(HDC, PCWSTR, int, RECT*, ...)` which requires `PCWSTR`, not `string`
- **Fix:** Used `fixed (char* pText = text)` and passed `pText` with known length
- **Files modified:** `NumberLabelRenderer.cs`
- **Commit:** 5d38aa5

**4. [Rule 1 - Bug] Plan referenced `hFont.Value` for cleanup but CreateFontIndirect returns DeleteObjectSafeHandle**
- **Found during:** Task 2 implementation
- **Issue:** Plan's cleanup code used `PInvoke.DeleteObject((HGDIOBJ)hFont.Value)` which doesn't match the safe handle overload
- **Fix:** Used `using var hFont = PInvoke.CreateFontIndirect(in lf)` — the safe handle is disposed automatically; old font restored via `SelectObject(memDC, oldFont)` before disposal
- **Files modified:** `NumberLabelRenderer.cs`
- **Commit:** 5d38aa5

## Build Status

- Debug build: 0 errors, 1 pre-existing warning (high DPI manifest — unrelated)
- Release build: 0 errors, 1 pre-existing warning

## Self-Check: PENDING (awaiting human verification for Task 3)

Files created:
- `focus/Windows/WindowSorter.cs` — created
- `focus/Windows/Daemon/Overlay/NumberLabelRenderer.cs` — created

Commits:
- `f8e08ad` — feat(quick-5): intercept CAPS+number keys and activate Nth window by position
- `5d38aa5` — feat(quick-5): render number labels on overlay windows using UpdateLayeredWindow
