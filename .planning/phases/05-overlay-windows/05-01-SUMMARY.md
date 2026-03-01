---
phase: 05-overlay-windows
plan: 01
subsystem: ui
tags: [win32, gdi, layered-window, overlay, cswin32, pinvoke, alpha-compositing]

# Dependency graph
requires:
  - phase: 04-daemon-core
    provides: STA thread, DaemonApplicationContext pattern, CsWin32 P/Invoke infrastructure

provides:
  - IOverlayRenderer interface contract (Name + Paint)
  - OverlayWindow: layered HWND lifecycle (create/show/hide/destroy)
  - BorderRenderer: GDI RoundRect border into premultiplied-alpha DIB via UpdateLayeredWindow
  - OverlayManager: four directional overlay windows with show/hide/paint orchestration
  - OverlayColors: per-direction ARGB hex defaults with GetArgb parser
  - FocusConfig: OverlayColors and OverlayRenderer config fields

affects:
  - 05-02 (debug command wiring — uses OverlayManager.ShowOverlay/HideOverlay)
  - 06-overlay-wiring (daemon CAPSLOCK lifecycle — uses OverlayManager)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - CsWin32 raw PCWSTR overload with HINSTANCE for CreateWindowEx/UnregisterClass (avoids SafeHandle conflict)
    - per-instance window class name (FocusOverlay_XXXXXXXX) to avoid class registration conflicts
    - premultiplied alpha: GdiFlush + NativeMemory.Clear then pixel-level alpha multiplication
    - HBITMAP/HPEN to HGDIOBJ via implicit cast for SelectObject/DeleteObject
    - Marshal.SizeOf<WNDCLASSEXW> instead of unsafe sizeof for managed struct

key-files:
  created:
    - focus/Windows/Daemon/Overlay/IOverlayRenderer.cs
    - focus/Windows/Daemon/Overlay/OverlayColors.cs
    - focus/Windows/Daemon/Overlay/OverlayWindow.cs
    - focus/Windows/Daemon/Overlay/BorderRenderer.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs
  modified:
    - focus/NativeMethods.txt
    - focus/Windows/FocusConfig.cs

key-decisions:
  - "Use raw PCWSTR overload of CreateWindowEx/UnregisterClass (not string/SafeHandle overload) — HINSTANCE is not a SafeHandle so raw overload required"
  - "Per-instance unique class name (FocusOverlay_XXXXXXXX) rather than a single shared name — avoids class registration conflicts if OverlayWindow is instantiated multiple times"
  - "Marshal.SizeOf<WNDCLASSEXW> for cbSize — unsafe sizeof on managed struct causes CS8500 warning"
  - "HBITMAP/HPEN have implicit conversion to HGDIOBJ — use explicit cast (HGDIOBJ)hBitmap for SelectObject/DeleteObject"
  - "DefWindowProc added to NativeMethods.txt — not auto-generated as transitive dependency of RegisterClassEx/CreateWindowEx"

patterns-established:
  - "WndProc handles WM_PAINT with BeginPaint+EndPaint to prevent infinite repaint loop"
  - "GdiFlush before any direct pixel buffer access — required for DIB coherency"
  - "GDI resource cleanup in reverse order: SelectObject(old) then DeleteObject"
  - "SWP_NOACTIVATE on every SetWindowPos — overlays must never steal focus"

requirements-completed: [OVERLAY-02, RENDER-01, RENDER-02, RENDER-03, CFG-05, CFG-07]

# Metrics
duration: 7min
completed: 2026-03-01
---

# Phase 5 Plan 01: Overlay Rendering Infrastructure Summary

**Win32 layered window overlay stack: IOverlayRenderer interface, OverlayWindow HWND wrapper, GDI RoundRect BorderRenderer with premultiplied-alpha DIB, and OverlayManager orchestrating four directional windows**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-01T08:30:31Z
- **Completed:** 2026-03-01T08:37:00Z
- **Tasks:** 3
- **Files modified:** 7 (5 created, 2 modified)

## Accomplishments
- Built complete overlay rendering stack: interface, HWND wrapper, renderer, manager — all compile-clean
- OverlayWindow creates WS_EX_LAYERED|TRANSPARENT|TOOLWINDOW|NOACTIVATE|TOPMOST HWND with WM_PAINT handler preventing 100% CPU
- BorderRenderer draws rounded-corner borders via GDI RoundRect into premultiplied-alpha DIB, composited via UpdateLayeredWindow (ULW_ALPHA)
- OverlayManager manages 4 directional windows with ShowOverlay/HideOverlay/HideAll and a CreateRenderer factory

## Task Commits

Each task was committed atomically:

1. **Task 1: NativeMethods, IOverlayRenderer, OverlayColors, FocusConfig extension** - `b7f5be6` (feat)
2. **Task 2: OverlayWindow HWND wrapper and BorderRenderer GDI implementation** - `db07eb4` (feat)
3. **Task 3: OverlayManager — directional overlay orchestrator** - `cb1886b` (feat)

## Files Created/Modified
- `focus/NativeMethods.txt` - Added 26 Win32 API/struct entries (overlay + DefWindowProc)
- `focus/Windows/FocusConfig.cs` - Added OverlayColors and OverlayRenderer properties
- `focus/Windows/Daemon/Overlay/IOverlayRenderer.cs` - Renderer contract: Name + Paint(HWND, RECT, uint)
- `focus/Windows/Daemon/Overlay/OverlayColors.cs` - Per-direction ARGB hex defaults, GetArgb parser
- `focus/Windows/Daemon/Overlay/OverlayWindow.cs` - Layered HWND lifecycle, Show/Hide/Dispose
- `focus/Windows/Daemon/Overlay/BorderRenderer.cs` - GDI RoundRect border, premultiplied-alpha DIB
- `focus/Windows/Daemon/Overlay/OverlayManager.cs` - Four directional windows, ShowOverlay/HideOverlay/HideAll/CreateRenderer

## Decisions Made
- Raw PCWSTR overload for CreateWindowEx/UnregisterClass: HINSTANCE is not a SafeHandle, so the string/SafeHandle overload cannot accept it directly
- Per-instance unique class name: avoids registration conflicts if multiple OverlayWindow instances are created
- Marshal.SizeOf instead of unsafe sizeof: WNDCLASSEXW is a managed partial struct, taking its address causes CS8500 warning
- DefWindowProc must be listed explicitly in NativeMethods.txt: not transitively generated from RegisterClassEx/CreateWindowEx

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added DefWindowProc to NativeMethods.txt**
- **Found during:** Task 2 (OverlayWindow implementation)
- **Issue:** DefWindowProc not generated by CsWin32 as a transitive dependency — PInvoke.DefWindowProc undefined
- **Fix:** Added DefWindowProc to NativeMethods.txt, rebuilt
- **Files modified:** focus/NativeMethods.txt (included in Task 2 commit)
- **Verification:** Build succeeded with 0 errors

**2. [Rule 1 - Bug] Used raw PCWSTR overload for CreateWindowEx/UnregisterClass**
- **Found during:** Task 2 (OverlayWindow implementation)
- **Issue:** CS1503: HINSTANCE cannot be converted to SafeHandle for the string-overload of CreateWindowEx
- **Fix:** Used raw PCWSTR overload (fixed char* pClassName) that accepts HINSTANCE directly; same for UnregisterClass
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayWindow.cs
- **Verification:** Build succeeded with 0 errors

**3. [Rule 1 - Bug] Used Marshal.SizeOf instead of unsafe sizeof for WNDCLASSEXW.cbSize**
- **Found during:** Task 2 (OverlayWindow implementation)
- **Issue:** CS8500 warning — sizeof on managed partial struct (WNDCLASSEXW has InlineArray)
- **Fix:** Replaced `(uint)sizeof(WNDCLASSEXW)` with `(uint)Marshal.SizeOf<WNDCLASSEXW>()`
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayWindow.cs
- **Verification:** No more CS8500 warning

---

**Total deviations:** 3 auto-fixed (Rule 1 - Bug)
**Impact on plan:** All fixes required to resolve CsWin32 interop specifics not predictable from plan spec. No scope creep.

## Issues Encountered
- CsWin32 generates two overloads of CreateWindowEx: one with string/SafeHandle (convenient but incompatible with HINSTANCE) and one with PCWSTR/HINSTANCE. Using the raw pointer overload required fixed char* within the RegisterAndCreate method.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- OverlayManager.ShowOverlay(Direction, RECT) and HideOverlay/HideAll are ready for Plan 02 debug command wiring
- OverlayManager.CreateRenderer(string) factory ready for config-driven renderer selection
- All four HWNDs must be created on STA thread — Plan 02 will instantiate OverlayManager in DaemonApplicationContext constructor

## Self-Check: PASSED

All 8 files confirmed present. All 3 task commits confirmed: b7f5be6, db07eb4, cb1886b.

---
*Phase: 05-overlay-windows*
*Completed: 2026-03-01*
