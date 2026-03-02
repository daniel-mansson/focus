# Stack Research

**Domain:** Windows background daemon — grid-snapped window move and resize
**Researched:** 2026-03-02
**Confidence:** HIGH (all APIs verified against official Microsoft Learn documentation)

---

## Scope of This Document

This document covers **additions required for v3.1 only**. The existing validated stack (.NET 8 `net8.0-windows`, CsWin32 0.3.269, WinForms message pump, WH_KEYBOARD_LL hook, GDI layered overlays, DwmGetWindowAttribute, MonitorFromWindow, GetMonitorInfo) is unchanged. Every recommendation integrates with the existing CsWin32 P/Invoke setup. No new NuGet packages are required.

**DPI awareness status:** The app.manifest already declares `PerMonitorV2, PerMonitor` — the process is per-monitor DPI aware. This is the correct mode for grid calculations and window positioning across monitors with different DPI settings.

---

## New APIs Required

### Window Positioning (Primary)

| Win32 API | NativeMethods.txt Entry | Signature | Purpose |
|-----------|------------------------|-----------|---------|
| `SetWindowPos` | Already in NativeMethods.txt | `BOOL SetWindowPos(HWND hWnd, HWND hWndInsertAfter, int X, int Y, int cx, int cy, SET_WINDOW_POS_FLAGS uFlags)` | Move and/or resize a window. The single correct API for both operations. `X, Y` are virtual-screen coordinates (same space as GetWindowRect). Pass `SWP_NOZORDER \| SWP_NOACTIVATE` to reposition without changing Z-order or stealing focus. |
| `GetWindowRect` | **New addition** | `BOOL GetWindowRect(HWND hWnd, out RECT lpRect)` | Get the window's current position in virtual-screen coordinates. **This is the correct source for SetWindowPos target coordinates.** Returns coordinates that include invisible DWM resize borders — those borders are intentional and must be included when round-tripping through SetWindowPos. |

`SetWindowPos` is already declared in NativeMethods.txt (used by overlay windows). Only `GetWindowRect` is a new entry.

### DPI Query APIs

| Win32 API | NativeMethods.txt Entry | Signature | DLL | Purpose |
|-----------|------------------------|-----------|-----|---------|
| `GetDpiForWindow` | **New addition** | `UINT GetDpiForWindow(HWND hwnd)` | User32.dll | Returns DPI of the monitor where the given window is currently displayed. Use this (not GetDpiForMonitor) when the process is per-monitor DPI aware — returns the window's actual DPI, not the system DPI. Minimum Windows 10 1607. |
| `GetDpiForMonitor` | **New addition (optional)** | `HRESULT GetDpiForMonitor(HMONITOR hmonitor, MONITOR_DPI_TYPE dpiType, out UINT dpiX, out UINT dpiY)` | Shcore.dll | Returns DPI for a monitor handle. Use MDT_EFFECTIVE_DPI for the user-visible scaling factor. Requires separate Shcore.lib link — CsWin32 handles this automatically. Useful when computing grid size for a target monitor before the window has moved there (cross-monitor movement). |

**Which to use:** `GetDpiForWindow` for a window's current monitor. `GetDpiForMonitor` for computing grid size on the destination monitor during cross-monitor movement before calling SetWindowPos. Both are available via CsWin32 NativeMethods.txt.

### Window State Query APIs

| Win32 API | NativeMethods.txt Entry | Signature | Purpose |
|-----------|------------------------|-----------|---------|
| `IsZoomed` | **New addition** | `BOOL IsZoomed(HWND hWnd)` | Detect if window is maximized. Skip move/resize if true — operating on a maximized window produces unintended results (Windows restores it automatically on the next interaction). |
| `IsIconic` | Already in NativeMethods.txt | `BOOL IsIconic(HWND hWnd)` | Already used in WindowEnumerator to skip minimized windows during enumeration. Reuse here: skip move/resize if window is minimized. |

### MONITOR_DPI_TYPE Enum (needed for GetDpiForMonitor)

CsWin32 generates `MONITOR_DPI_TYPE` automatically when `GetDpiForMonitor` is added to NativeMethods.txt. Use `MDT_EFFECTIVE_DPI` (value 0) — this returns the DPI that matches the display scaling factor the user has set in Windows Settings. `MDT_ANGULAR_DPI` and `MDT_RAW_DPI` are hardware values that do not reflect user scaling choices.

---

## NativeMethods.txt Additions

Append to the existing NativeMethods.txt:

```
GetWindowRect
GetDpiForWindow
GetDpiForMonitor
IsZoomed
```

(`SetWindowPos`, `IsIconic`, `MonitorFromWindow`, `GetMonitorInfo` are already present.)

---

## Coordinate System: The Critical Distinction

This is the most important detail for correct window positioning.

### Two rect types, two coordinate spaces

| Source | Coordinate type | DPI adjusted? | Use for |
|--------|----------------|---------------|---------|
| `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` | Physical pixels, virtual-screen space | **No** — raw physical | Reading visible bounds for overlay positioning, grid snap calculations, display to user |
| `GetWindowRect` | Physical pixels, virtual-screen space (includes invisible DWM borders) | Yes (virtualized for DPI) | **Input to SetWindowPos** — round-trip safe |

### The invisible border problem

Windows 10+ Win32 windows have invisible 7–8 px resize borders on left, right, and bottom edges (0 px on top). `GetWindowRect` includes these borders. `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` excludes them.

**Consequence for move/resize:** If you read `DwmGetWindowAttribute` (visible bounds) and pass those coordinates directly to `SetWindowPos`, the window will shift by ~7 px each operation due to the missing border offset. This accumulates across repeated keypresses.

### Correct approach: read GetWindowRect, compute in visible space, compensate

```csharp
// 1. Read current position via GetWindowRect (includes invisible borders)
PInvoke.GetWindowRect(hwnd, out RECT windowRect);  // for SetWindowPos input

// 2. Read visible bounds via DwmGetWindowAttribute (for grid snap / display)
RECT visibleRect = default;
var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref visibleRect, 1));
PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, bytes);

// 3. Border offsets (per-window, computed once)
int borderLeft   = visibleRect.left   - windowRect.left;   // typically +7
int borderTop    = visibleRect.top    - windowRect.top;    // typically 0
int borderRight  = windowRect.right   - visibleRect.right; // typically +7
int borderBottom = windowRect.bottom  - visibleRect.bottom; // typically +7

// 4. Grid snap: align visibleRect to grid, then add borders back for SetWindowPos
// Target visible position after snap:
int newVisLeft = SnapToGrid(visibleRect.left + deltaX, gridStep);
int newVisTop  = SnapToGrid(visibleRect.top  + deltaY, gridStep);

// SetWindowPos coordinates (include invisible borders):
int newLeft = newVisLeft - borderLeft;
int newTop  = newVisTop  - borderTop;
// Width/height from windowRect (unchanged for a move):
int width  = windowRect.right  - windowRect.left;
int height = windowRect.bottom - windowRect.top;

PInvoke.SetWindowPos(hwnd, HWND.Null, newLeft, newTop, width, height,
    SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
```

For a resize (grow/shrink edge), the delta is applied to the appropriate edge of the visible rect, then borders are added back to compute the final windowRect dimensions.

---

## Grid Calculation: Per-Monitor DPI

### Physical-pixel grid (recommended)

Because the process is per-monitor DPI aware (`PerMonitorV2` in app.manifest), all Win32 coordinates are **physical pixels**. The grid should be expressed in physical pixels based on the monitor's resolution.

```csharp
// Get monitor info for the foreground window's current monitor
HMONITOR hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
MONITORINFO mi = default;
mi.cbSize = (uint)sizeof(MONITORINFO);
PInvoke.GetMonitorInfo(hMon, ref mi);

// Physical monitor dimensions
int monitorWidth  = mi.rcMonitor.right  - mi.rcMonitor.left;  // e.g. 2560 at 4K
int monitorHeight = mi.rcMonitor.bottom - mi.rcMonitor.top;   // e.g. 1440 at 4K

// Grid step = 1/16th of monitor width/height (configurable)
int gridFraction = config.GridFraction;  // default 16
int gridStepX = monitorWidth  / gridFraction;  // e.g. 160 px at 2560
int gridStepY = monitorHeight / gridFraction;  // e.g. 90 px at 1440
```

**Why physical pixels, not DPI-scaled logical units:** `GetWindowRect` and `SetWindowPos` operate in physical pixels when the process is per-monitor DPI aware. Using physical pixels avoids any DPI conversion math. Grid steps are simply fractions of the physical monitor resolution.

**DPI variation across monitors:** On a mixed DPI setup (e.g., 4K at 200% DPI + 1080p at 100% DPI), each monitor has different physical dimensions. The grid step is computed from the **current window's monitor** at move time. When crossing to another monitor, recompute grid step from the destination monitor. Both `MonitorFromWindow` and `GetMonitorInfo` are already present and validated.

### Cross-monitor grid recalculation

During cross-monitor movement, the window's new grid position on the destination monitor must be computed:

```csharp
// Window has moved to destination monitor — recalculate grid for new monitor
HMONITOR destMon = PInvoke.MonitorFromPoint(newCenterPoint, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
// ... get new monitor info, compute new grid steps
// Snap window position to new monitor's grid
```

`MonitorFromPoint` is already in NativeMethods.txt.

---

## Window Size Constraints

### What to check before move/resize

```csharp
// 1. Skip if minimized (already in WindowEnumerator; add here as guard)
if (PInvoke.IsIconic(hwnd)) return;

// 2. Skip if maximized — resize conflicts with maximized state
if (PInvoke.IsZoomed(hwnd)) return;

// 3. Skip if fullscreen (check if window covers entire monitor work area)
// Use mi.rcWork for work area (excludes taskbar) vs visible window bounds
```

### Minimum size clamping

Windows enforces a minimum window size via `WM_GETMINMAXINFO`. Apps respond to this message to declare their minimum. Since we do not own the target windows, we cannot know their minimums in advance.

**Recommended approach:** Clamp to a sensible minimum (e.g., one grid step wide/tall) before calling SetWindowPos. The window manager will silently enforce the application's own minimum if our value is smaller — the call succeeds but the window snaps to its declared minimum. This is not an error condition; the window simply ends up at its minimum size rather than the requested size.

```csharp
int minWidth  = gridStepX;  // at minimum, one grid cell wide
int minHeight = gridStepY;

int newWidth  = Math.Max(requestedWidth,  minWidth);
int newHeight = Math.Max(requestedHeight, minHeight);
```

### Monitor boundary clamping

Clamp the target rect to the monitor's work area (`mi.rcWork`, not `mi.rcMonitor`) to avoid the window being dragged behind the taskbar:

```csharp
// Clamp new visible position to work area
// (rcWork already available from GetMonitorInfo — already in MonitorHelper pattern)
```

`GetMonitorInfo` is already called in the existing `ClampToMonitor` helper in OverlayOrchestrator. Extend this pattern for work-area clamping.

---

## Integration with Existing Architecture

### Hook handler changes (KeyboardHookHandler)

The hook handler already reads `shiftHeld` and `ctrlHeld` modifier state for direction key events. For v3.1, add detection for `VK_TAB` and expand the modifier key intercept list:

```csharp
private const uint VK_TAB     = 0x09;  // CAPS+TAB+direction = move
// VK_SHIFT (0x10) and VK_CONTROL (0x11) already tracked
// CAPS+LSHIFT+direction = grow, CAPS+LCTRL+direction = shrink
```

Tab suppression when CAPSLOCK is held needs to be added to the hook callback alongside the existing direction/number key suppression.

No new Win32 APIs are needed in the hook handler itself.

### Orchestrator changes (OverlayOrchestrator)

Add a new `OnMoveWindowKeyDown` / `OnResizeWindowKeyDown` dispatch path analogous to `OnDirectionKeyDown`. The new methods marshal to the STA thread and call a `MoveWindowSta` / `ResizeWindowSta` implementation that:

1. Calls `PInvoke.GetForegroundWindow()`
2. Guards: `IsIconic`, `IsZoomed`
3. Calls `PInvoke.GetWindowRect` for current position
4. Calls `PInvoke.DwmGetWindowAttribute` for visible bounds
5. Computes border offsets
6. Gets monitor info for grid step calculation
7. Applies delta, snaps to grid, clamps to work area
8. Calls `PInvoke.SetWindowPos`

All these Win32 calls run on the STA thread via `_staDispatcher.Invoke()` — the existing pattern in `OverlayOrchestrator` is directly reusable.

### Overlay rendering for move/resize mode

Mode-specific overlay indicators (move arrows, grow/shrink edge arrows) use the existing `IOverlayRenderer` interface and `OverlayWindow` infrastructure. New renderer implementations are needed but no new Win32 APIs — the same `UpdateLayeredWindow` + GDI DIB pattern handles any visual content.

---

## Recommended Stack (Complete — v3.1 Additions Only)

### New Win32 APIs via CsWin32

| Win32 API | DLL | Purpose | Why This One |
|-----------|-----|---------|--------------|
| `GetWindowRect` | User32.dll | Read current window position (with invisible borders) | SetWindowPos round-trip requires GetWindowRect coordinates, not DwmGetWindowAttribute |
| `GetDpiForWindow` | User32.dll | Get DPI of window's current monitor | Per-monitor DPI aware version; preferred over GetDpiForMonitor when querying by window. Windows 10 1607+. |
| `GetDpiForMonitor` | Shcore.dll | Get DPI for a destination monitor (cross-monitor moves) | Needed when computing target grid before window arrives on destination monitor |
| `IsZoomed` | User32.dll | Detect maximized state | Guard: skip move/resize on maximized windows |

### No New NuGet Packages

Zero new dependencies. All new APIs are Win32 system DLLs exposed through the existing CsWin32 source-generator setup.

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| `SetWindowPos` | `MoveWindow` | `MoveWindow` always repaints the window (`bRepaint` parameter is misleading — the WM_SIZE/WM_MOVE messages are always sent). `SetWindowPos` with `SWP_NOZORDER \| SWP_NOACTIVATE` gives cleaner semantics. Both ultimately call the same internal window manager code. Use SetWindowPos — it's already in the codebase. |
| `GetWindowRect` for SetWindowPos input | `DwmGetWindowAttribute` for SetWindowPos input | DwmGetWindowAttribute returns visible-only bounds (excludes invisible DWM borders). Passing these to SetWindowPos causes a ~7px drift per operation. GetWindowRect returns the full rect that SetWindowPos expects. |
| Physical-pixel grid (fraction of monitor resolution) | DPI-logical-unit grid | Since the process is per-monitor DPI aware, all Win32 coordinates are physical pixels. No DPI conversion math needed. Logical units would require `MulDiv(value, dpi, 96)` conversions at every boundary. Physical pixels are simpler and direct. |
| `IsZoomed` guard before resize | `GetWindowPlacement` | `GetWindowPlacement` provides more detail (WINDOWPLACEMENT.showCmd) but is heavier. IsZoomed is a single bool check sufficient for the guard. |

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `MoveWindow` | Redundant with SetWindowPos; less flexible flags; already have SetWindowPos in codebase | `SetWindowPos` with `SWP_NOZORDER \| SWP_NOACTIVATE` |
| `SetWindowPlacement` | Designed for save/restore window state across sessions. Overkill for grid moves. Its coordinates are workspace-relative, adding conversion complexity. | `SetWindowPos` with GetWindowRect-derived coordinates |
| `WM_GETMINMAXINFO` / subclassing target windows | Would require SetWindowSubclass, a WndProc callback, and per-window teardown logic. The window manager already enforces min size silently when SetWindowPos is called with a too-small rect. | Clamp to one grid step minimum before SetWindowPos; let window manager enforce app minimum |
| Any animation/transition API (`AnimateWindow`, DWM transitions) | Project decision: instant moves only, no animation. | Direct SetWindowPos with no SWP_ASYNCWINDOWPOS |
| `SWP_ASYNCWINDOWPOS` flag | Defers the position change to the target window's thread — adds a frame of latency and makes the move non-atomic. Unnecessary for a keyboard hotkey operation. | Synchronous SetWindowPos |
| WinRT / Windows.UI.ViewManagement APIs | UWP/WinRT window management does not apply to Win32 HWNDs. | Win32 SetWindowPos |
| New rendering libraries for move/resize indicators | Existing IOverlayRenderer + UpdateLayeredWindow + GDI DIB handles any visual content | Extend existing overlay renderer infrastructure |

---

## Version Compatibility

| API / Package | Windows Requirement | Notes |
|---------------|--------------------|----|
| `GetWindowRect` | Windows 2000+ | Universal. Already generated by CsWin32 for many projects; just add to NativeMethods.txt. |
| `GetDpiForWindow` | Windows 10, version 1607+ | Already satisfied by the project's supported OS (app.manifest). Returns per-monitor DPI when process is per-monitor aware. |
| `GetDpiForMonitor` | Windows 8.1+ | Project targets Windows 10+ per app.manifest — satisfied. Use `MDT_EFFECTIVE_DPI` (0). |
| `IsZoomed` | Windows 2000+ | Universal. |
| `SetWindowPos` | Windows 2000+ | Already in NativeMethods.txt. `SWP_NOACTIVATE` is the critical flag. |
| `CsWin32 0.3.269` | — | All new APIs (GetWindowRect, GetDpiForWindow, GetDpiForMonitor, IsZoomed) are in Win32Metadata. No version bump needed. |

---

## Sources

- [SetWindowPos — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) — exact parameter types, SWP_ flags, coordinate system, no-activate semantics — HIGH confidence
- [GetWindowRect — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect) — "GetWindowRect is virtualized for DPI", invisible borders included, round-trip safe with SetWindowPos — HIGH confidence
- [GetWindowRect remarks on invisible borders — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect) — "In Windows Vista and later, the Window Rect now may include invisible resize borders. To get visible bounds, use DwmGetWindowAttribute" — HIGH confidence
- [GetDpiForWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow) — Windows 10 1607+ requirement, User32.dll, returns per-monitor DPI when DPI_AWARENESS_PER_MONITOR_AWARE — HIGH confidence
- [GetDpiForMonitor — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor) — Shcore.dll, MDT_EFFECTIVE_DPI, Windows 8.1+, HRESULT return — HIGH confidence
- [High DPI Desktop Application Development — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) — per-monitor v2 awareness, physical pixel coordinate model — HIGH confidence
- [WM_DPICHANGED — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged) — DPI change notification, GetDpiForWindow usage in move scenarios — HIGH confidence
- [MONITORINFO / GetMonitorInfo — already validated in v2.0 STACK.md] — rcWork for work area, rcMonitor for full physical bounds — HIGH confidence (existing validated code)

---
*Stack research for: Window focus navigation v3.1 — grid-snapped window move and resize*
*Researched: 2026-03-02*
