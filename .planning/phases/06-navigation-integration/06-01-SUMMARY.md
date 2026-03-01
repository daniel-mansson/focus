---
phase: 06-navigation-integration
plan: 01
subsystem: overlay
tags: [csharp, winforms, cswin32, setwineventhook, overlay, capslock]

# Dependency graph
requires:
  - phase: 05-overlay-windows
    provides: OverlayManager, OverlayWindow, IOverlayRenderer, BorderRenderer
  - phase: 04-daemon-core
    provides: CapsLockMonitor, KeyboardHookHandler, DaemonApplicationContext
  - phase: 02-navigation-pipeline
    provides: NavigationService.GetRankedCandidates, WindowEnumerator, ExcludeFilter
provides:
  - ForegroundMonitor: SetWinEventHook(EVENT_SYSTEM_FOREGROUND) wrapper with Install/Uninstall/Dispose
  - OverlayOrchestrator: central coordinator connecting CapsLockMonitor + ForegroundMonitor + OverlayManager
  - CapsLockMonitor: optional onHeld/onReleased Action callbacks via constructor
  - FocusConfig.OverlayDelayMs: int property defaulting to 0 (CFG-06)
  - OverlayManager.ShowOverlay(Direction, RECT, uint): color-override overload for solo-window dim
  - NativeMethods: SetWinEventHook, UnhookWinEvent, keybd_event added (59 total entries)
affects:
  - 06-02: plan 02 will wire OverlayOrchestrator into DaemonCommand lifecycle and add fade animation

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Control.Invoke for cross-thread STA dispatch from worker thread to STA (CapsLockMonitor -> OverlayOrchestrator)
    - GCHandle.Alloc to pin WinEvent delegate preventing GC collection while Win32 holds pointer
    - volatile bool _shutdownRequested guard before Invoke calls to prevent ObjectDisposedException during shutdown
    - WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS for out-of-process foreground change notifications

key-files:
  created:
    - focus/Windows/Daemon/Overlay/ForegroundMonitor.cs
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
  modified:
    - focus/NativeMethods.txt
    - focus/Windows/FocusConfig.cs
    - focus/Windows/Daemon/CapsLockMonitor.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs

key-decisions:
  - "ForegroundMonitor uses WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS — callback fires on STA thread via message pump, no extra thread marshaling needed"
  - "HWINEVENTHOOK is in Windows.Win32.UI.Accessibility namespace (not Windows.Win32 despite the generated file name)"
  - "SoloDimColor 0x30AAAAAA (~19% opacity neutral gray) for solo-window 'daemon alive' indicator"
  - "_fadeProgress and _fadingIn fields declared in Plan 01 with CS0169 pragma suppression — reserved for Plan 02 fade animation"
  - "ShowOverlaysForCurrentForeground calls GetForegroundWindow on STA thread after Invoke completes — safe because foreground state is settled by then (not inside WinEvent callback)"

patterns-established:
  - "Cross-thread STA dispatch: volatile _shutdownRequested check -> try { _staDispatcher.Invoke(action); } catch (ObjectDisposedException, InvalidOperationException) { }"
  - "WinEvent delegate pinning: _proc = Callback; _procHandle = GCHandle.Alloc(_proc); — stored as instance fields"
  - "Solo-window special case: all-directions zero candidates -> dim overlay on foreground window bounds"

requirements-completed: [OVERLAY-01, OVERLAY-03, OVERLAY-04, OVERLAY-05, DAEMON-03, CFG-06]

# Metrics
duration: 3min
completed: 2026-03-01
---

# Phase 6 Plan 01: Navigation Integration Summary

**OverlayOrchestrator + ForegroundMonitor API surface wiring CapsLock hold/release to directional navigation scoring and overlay display via SetWinEventHook + Control.Invoke**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-01T16:27:46Z
- **Completed:** 2026-03-01T16:31:00Z
- **Tasks:** 3
- **Files modified:** 6 (2 new, 4 modified)

## Accomplishments
- ForegroundMonitor wraps SetWinEventHook(EVENT_SYSTEM_FOREGROUND) with proper GCHandle.Alloc delegate pinning
- OverlayOrchestrator coordinates CapsLock hold/release (worker thread) with overlay show/hide (STA thread) using Control.Invoke dispatch
- ShowOverlaysForCurrentForeground scores all four directions via NavigationService and calls ShowOverlay/HideOverlay per result
- Solo-window dim indicator (SoloDimColor) shows when all four directions have zero candidates
- ForegroundMonitor integration triggers instant overlay reposition when foreground window changes while CapsLock held
- CapsLockMonitor, FocusConfig, and OverlayManager modified without breaking existing callers

## Task Commits

Each task was committed atomically:

1. **Task 1: NativeMethods + FocusConfig + CapsLockMonitor + OverlayManager modifications** - `99b72ea` (feat)
2. **Task 2: ForegroundMonitor — SetWinEventHook wrapper** - `4dd4925` (feat)
3. **Task 3: OverlayOrchestrator — central coordination class** - `bc2d2dd` (feat)

## Files Created/Modified
- `focus/NativeMethods.txt` - Added SetWinEventHook, UnhookWinEvent, keybd_event (59 entries total)
- `focus/Windows/FocusConfig.cs` - Added OverlayDelayMs property (int, default 0, CFG-06)
- `focus/Windows/Daemon/CapsLockMonitor.cs` - Added optional onHeld/onReleased Action callbacks via constructor
- `focus/Windows/Daemon/Overlay/OverlayManager.cs` - Added ShowOverlay(Direction, RECT, uint colorOverride) overload
- `focus/Windows/Daemon/Overlay/ForegroundMonitor.cs` - NEW: SetWinEventHook wrapper (96 lines)
- `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` - NEW: Central coordinator (257 lines)

## Decisions Made
- HWINEVENTHOOK is in `Windows.Win32.UI.Accessibility` namespace (not `Windows.Win32` as the generated file name suggests) — discovered during compilation
- GCHandle.Alloc required for the WINEVENTPROC delegate to prevent GC collection while Win32 holds an unmanaged pointer
- SoloDimColor chosen as 0x30AAAAAA (~19% opacity neutral gray) — subtle enough to not distract but visible enough to confirm daemon activity
- _fadeProgress and _fadingIn declared now with CS0169 pragma suppression to avoid Plan 02 needing to add fields to an already-wired class

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] HWINEVENTHOOK namespace correction**
- **Found during:** Task 2 (ForegroundMonitor compilation)
- **Issue:** Plan specified `global::Windows.Win32.HWINEVENTHOOK` but the actual namespace is `Windows.Win32.UI.Accessibility.HWINEVENTHOOK`
- **Fix:** Used `HWINEVENTHOOK` directly (available via the existing `using global::Windows.Win32.UI.Accessibility` import)
- **Files modified:** focus/Windows/Daemon/Overlay/ForegroundMonitor.cs
- **Verification:** dotnet build 0 errors
- **Committed in:** 4dd4925 (Task 2 commit)

**2. [Rule 2 - Missing Critical] CS0169 pragma suppress for Plan 02 reserved fields**
- **Found during:** Task 3 (OverlayOrchestrator compilation)
- **Issue:** _fadeProgress and _fadingIn fields produced CS0169 "never used" warnings — warnings should be clean
- **Fix:** Added `#pragma warning disable/restore CS0169` around the two reserved fields with explanatory comment
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
- **Verification:** dotnet build 1 Warning(s) 0 Error(s) (remaining warning is pre-existing DPI manifest warning)
- **Committed in:** bc2d2dd (Task 3 commit)

---

**Total deviations:** 2 auto-fixed (1 bug — namespace, 1 warning suppression for reserved fields)
**Impact on plan:** Both fixes necessary for correct compilation. No scope creep.

## Issues Encountered
None — all issues resolved via auto-fix.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Complete orchestration API surface in place: ForegroundMonitor, OverlayOrchestrator, modified CapsLockMonitor, OverlayManager with colorOverride overload
- Plan 02 needs to: wire OverlayOrchestrator into DaemonCommand.Run lifecycle, inject callbacks into CapsLockMonitor constructor, add fade animation via _fadeTimer Tick handler
- All types compile clean; Plan 02 can import and use them without modification to Plan 01 files

---
*Phase: 06-navigation-integration*
*Completed: 2026-03-01*
