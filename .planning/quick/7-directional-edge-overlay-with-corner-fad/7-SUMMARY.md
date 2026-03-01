---
phase: quick-7
plan: 1
subsystem: overlay-rendering
tags: [overlay, rendering, directional, border, fade, gdi, pixel-buffer]
dependency_graph:
  requires:
    - focus/Windows/Direction.cs
    - focus/Windows/Daemon/Overlay/OverlayWindow.cs
    - focus/Windows/Daemon/Overlay/OverlayColors.cs
  provides:
    - Directional edge overlay rendering with corner arcs and fade tails
  affects:
    - focus/Windows/Daemon/Overlay/IOverlayRenderer.cs
    - focus/Windows/Daemon/Overlay/BorderRenderer.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs
tech_stack:
  added: []
  patterns:
    - Direct pixel buffer writing with premultiplied ARGB (replaces GDI RoundRect + fixup)
    - Distance-from-arc corner detection with quadrant bounding box early-out
    - Linear gradient fade alpha over 20% of perpendicular edge dimension
key_files:
  created: []
  modified:
    - focus/Windows/Daemon/Overlay/IOverlayRenderer.cs
    - focus/Windows/Daemon/Overlay/BorderRenderer.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs
decisions:
  - Switched from GDI RoundRect to direct pixel buffer writes for full control over per-pixel alpha
  - Used GetPixelAlpha helper method with early-return ordering: primary edge > corner arc > fade tail > transparent
  - Quadrant bounding box check before arc distance calculation to avoid sqrt on most pixels
  - effectiveRadius clamping for small windows (Math.Min(CornerRadius, width/2, height/2))
metrics:
  duration: "~2 minutes"
  completed_date: "2026-03-01"
  tasks_completed: 2
  files_modified: 3
---

# Quick Task 7: Directional Edge Overlay with Corner Fade Summary

**One-liner:** Replaced full-box RoundRect overlay with directional edge-only rendering — each direction draws its primary edge, two corner arcs, and 20% fade tails on perpendicular edges using direct premultiplied pixel buffer writes.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add Direction to IOverlayRenderer.Paint and update OverlayManager caller | 9ec6fed | IOverlayRenderer.cs, OverlayManager.cs |
| 2 | Implement directional edge rendering with corner fade-out in BorderRenderer | 5621b71 | BorderRenderer.cs |

## What Was Built

### IOverlayRenderer.cs
- Added `using Focus.Windows;` import
- Updated `Paint` signature from `(HWND, RECT, uint)` to `(HWND, RECT, uint, Direction)`
- Added XML doc `<param name="direction">` entry

### OverlayManager.cs
- Updated the single `_renderer.Paint(...)` call site to pass `direction` as the 4th argument

### BorderRenderer.cs
- Removed GDI RoundRect, CreatePen, GetStockObject, SelectObject pen/brush, SetBkMode (no longer needed)
- Removed old premultiplied-alpha fixup loop (which detected GDI-drawn pixels via `(pixel & 0x00FFFFFF) != 0`)
- Added `using Focus.Windows;` import
- Updated `Paint` signature to include `Direction direction` parameter
- Added private static `GetPixelAlpha(px, py, w, h, direction, thickness, radius, fadeLenH, fadeLenV)` method
- New pixel writing loop writes premultiplied ARGB directly based on `GetPixelAlpha` return value
- Simplified cleanup (no pen/brush DeleteObject needed)

### Rendering Logic per Direction

**Direction.Left:**
- Primary: `px < thickness` → alpha 1.0
- TL corner: quadrant `px <= radius && py <= radius`, arc distance check → alpha 1.0
- BL corner: quadrant `px <= radius && py >= h-1-radius`, arc distance check → alpha 1.0
- Top fade tail: `py < thickness && px < fadeLenH` → alpha `1.0 - px/fadeLenH`
- Bottom fade tail: `py >= h-thickness && px < fadeLenH` → alpha `1.0 - px/fadeLenH`

**Direction.Right** (mirror of Left, right side):
- Primary: `px >= w-thickness`
- TR corner, BR corner arc checks
- Top/bottom fade tails on right side: `alpha = 1.0 - (w-1-px)/fadeLenH`

**Direction.Up:**
- Primary: `py < thickness`
- TL corner, TR corner arc checks
- Left/right fade tails on top: `alpha = 1.0 - py/fadeLenV`

**Direction.Down:**
- Primary: `py >= h-thickness`
- BL corner, BR corner arc checks
- Left/right fade tails on bottom: `alpha = 1.0 - (h-1-py)/fadeLenV`

## Decisions Made

1. **Direct pixel writes vs GDI:** The old approach used GDI RoundRect (draws full box) then fixed up alpha. The new approach writes directly to the pixel buffer, giving full control over which pixels get drawn and at what opacity. This was the only viable approach for selective edge rendering.

2. **GetPixelAlpha ordering:** Primary edge check first (returns immediately), then corner quadrant bounding box check (cheap), then arc distance (sqrt — only computed for pixels in corner quadrant), then fade tail. This ordering minimizes expensive operations for the majority of pixels.

3. **Fade tail does not need to skip corner pixels:** Because the primary edge check returns 1.0 first, and corner arcs also return 1.0 if on the arc, the fade tail code only runs for pixels that passed all prior checks. No explicit skip needed.

4. **effectiveRadius clamping:** Added `Math.Min(CornerRadius, Math.Min(width / 2, height / 2))` to handle small windows without corrupting corner geometry.

## Deviations from Plan

None — plan executed exactly as written. The fade extent constants, pixel loop structure, corner arc detection formula, and GDI cleanup simplification all match the plan specification.

## Build Verification

```
Build succeeded.
  1 Warning(s)  (pre-existing WFAC010 — unrelated to this task)
  0 Error(s)
```

## Self-Check: PASSED

| Item | Status |
|------|--------|
| focus/Windows/Daemon/Overlay/IOverlayRenderer.cs | FOUND |
| focus/Windows/Daemon/Overlay/BorderRenderer.cs | FOUND |
| focus/Windows/Daemon/Overlay/OverlayManager.cs | FOUND |
| .planning/quick/7-.../7-SUMMARY.md | FOUND |
| Commit 9ec6fed (Task 1) | FOUND |
| Commit 5621b71 (Task 2) | FOUND |
