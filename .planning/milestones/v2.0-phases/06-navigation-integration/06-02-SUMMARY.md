---
phase: 06-navigation-integration
plan: 02
subsystem: overlay
tags: [csharp, winforms, cswin32, capslock, overlay, daemon, animation]

# Dependency graph
requires:
  - phase: 06-01
    provides: OverlayOrchestrator, ForegroundMonitor, CapsLockMonitor callbacks, OverlayManager colorOverride
  - phase: 04-daemon-core
    provides: DaemonCommand, DaemonApplicationContext, CapsLockMonitor, KeyboardHookHandler
  - phase: 05-overlay-windows
    provides: OverlayManager, OverlayWindow, BorderRenderer
provides:
  - DaemonCommand.ForceCapsLockOff: static method toggling CAPSLOCK LED off via keybd_event
  - DaemonApplicationContext: wires OverlayOrchestrator into STA thread lifecycle with out param pattern
  - OverlayOrchestrator: instant show/hide on CapsLock hold/release (fade removed per user decision)
  - Complete end-to-end overlay navigation: CAPSLOCK hold shows directional colored borders, release hides them
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Out-param pattern for STA-thread object creation — DaemonApplicationContext sets `out OverlayOrchestrator orchestrator` so DaemonCommand.Run can capture it for shutdown ordering
    - Late-binding Action closures — CapsLockMonitor receives lambdas that capture `OverlayOrchestrator?` reference populated later on STA thread
    - Reposition+Paint+Show split — OverlayWindow.Show decomposed into three steps to eliminate stale frame flash on re-activation
    - Monitor-edge clamping via MonitorFromWindow+GetMonitorInfo — overlays on partially off-screen windows pin to screen boundary

key-files:
  created: []
  modified:
    - focus/Windows/Daemon/DaemonCommand.cs
    - focus/Windows/Daemon/TrayIcon.cs
    - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
    - focus/Windows/Daemon/Overlay/OverlayManager.cs
    - focus/Windows/Daemon/Overlay/OverlayWindow.cs

key-decisions:
  - "Fade animation removed entirely — instant show/hide preferred over 100ms fade-in/80ms fade-out; all fade machinery (timer, alpha scaling, repaint tracking) removed"
  - "OverlayWindow.Show split into Reposition+Paint+Show to eliminate stale frame flash when re-activating an already-visible overlay"
  - "Left/right overlay bounds expanded by 3px in all directions to prevent visual overlap with up/down border overlays on same window"
  - "Overlay bounds clamped to monitor edges via MonitorFromWindow+GetMonitorInfo — handles partially off-screen source windows"

patterns-established:
  - "Out-param STA creation: DaemonApplicationContext constructor takes `out OverlayOrchestrator orchestrator` — fills it before returning, DaemonCommand.Run captures for ordered shutdown"
  - "Late-binding closure: `OverlayOrchestrator? orchestrator = null;` captured by `onHeld` / `onReleased` lambdas passed to CapsLockMonitor; set by DaemonApplicationContext before first CAPSLOCK event"
  - "Ordered shutdown: orchestrator.RequestShutdown() -> staThread.Join() -> orchestrator.Dispose()"

requirements-completed: [OVERLAY-01, OVERLAY-03, OVERLAY-04, OVERLAY-05, DAEMON-03, CFG-06]

# Metrics
duration: ~60min
completed: 2026-03-01
---

# Phase 6 Plan 02: Navigation Integration Summary

**Daemon lifecycle wiring delivering instant CAPSLOCK-hold directional overlay previews — OverlayOrchestrator connected to DaemonCommand via STA out-param pattern, fade removed per user preference, monitor-edge clamping and overlap prevention added**

## Performance

- **Duration:** ~60 min
- **Started:** 2026-03-01T16:31:00Z
- **Completed:** 2026-03-01T~17:30:00Z
- **Tasks:** 2 (1 auto + 1 human-verify)
- **Files modified:** 5

## Accomplishments
- DaemonCommand.Run creates OverlayOrchestrator on STA thread via DaemonApplicationContext with out-param pattern
- Late-binding Action closure connects CapsLockMonitor callbacks to OverlayOrchestrator before first keypress
- ForceCapsLockOff forces CAPSLOCK LED off at daemon startup and after sleep/wake resume
- Ordered shutdown: RequestShutdown -> staThread.Join -> orchestrator.Dispose prevents Invoke exceptions
- Overlay show/hide made instant (no fade) after user testing revealed instant transitions feel better
- OverlayWindow.Show split into Reposition+Paint+Show to eliminate stale frame flash
- Left/right overlay bounds expanded 3px to prevent overlap with up/down borders
- Monitor-edge clamping pins overlays when source window is partially off-screen
- All 8 human test scenarios passed: appearance, no-candidate, solo-window, foreground reposition, tap suppression, CAPSLOCK toggle suppression, clean shutdown, click-through

## Task Commits

Each task was committed atomically:

1. **Task 1: Daemon lifecycle wiring + ForceCapsLockOff + fade animation** - `0c60567`, `51fc9a9`, `e294078` (feat)
2. **Task 2: Verify complete overlay navigation experience** - Human-approved (no code commit)

## Files Created/Modified
- `focus/Windows/Daemon/DaemonCommand.cs` - Added ForceCapsLockOff, OverlayOrchestrator creation via DaemonApplicationContext out-param, late-binding closures, ordered shutdown
- `focus/Windows/Daemon/TrayIcon.cs` - DaemonApplicationContext constructor takes FocusConfig + out orchestrator; PowerBroadcastWindow calls ForceCapsLockOff on wake
- `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` - Removed fade machinery (timer, alpha, fadingIn); OnHeldSta/OnReleasedSta call ShowAll/HideAll instantly
- `focus/Windows/Daemon/Overlay/OverlayManager.cs` - Show path updated to use Reposition+Paint+Show split
- `focus/Windows/Daemon/Overlay/OverlayWindow.cs` - Show decomposed into Reposition(bounds) + Paint + Show() to fix stale content flash

## Decisions Made
- **Fade removed:** Plan specified ~100ms fade-in and ~80ms fade-out. After human testing, user preferred instant transitions — all WinForms Timer fade machinery removed.
- **Reposition+Paint+Show split:** Calling Show(bounds) produced stale frame flash on re-activation. Decomposing into three steps (update position -> repaint with new content -> show) eliminates the artifact.
- **Left/right bounds expansion:** Without expansion, left/right overlays visually overlapped the up/down border corners on the same window. 3px expansion in all directions creates a clean separation.
- **Monitor-edge clamping:** When source window is partially off-screen, computed overlay bounds extended past the screen edge. Clamping via MonitorFromWindow+GetMonitorInfo pins them to the visible monitor area.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 4 -> Approved Change] Fade animation removed entirely**
- **Found during:** Task 1 (human verification phase)
- **Issue:** Plan specified ~100ms fade-in / ~80ms fade-out. User tested both and preferred instant show/hide.
- **Fix:** Removed all fade machinery — WinForms Timer, _fadeProgress, _fadingIn fields, RepaintAllOverlays, StartFadeIn, StartFadeOut, OnFadeTick. OnHeldSta now calls ShowAll directly; OnReleasedSta calls HideAll directly.
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
- **Verification:** Human approved instant behavior in all 8 test scenarios
- **Committed in:** e294078

**2. [Rule 1 - Bug] Stale frame flash on overlay re-activation**
- **Found during:** Task 1 (testing rapid CAPSLOCK press sequences)
- **Issue:** Re-showing an already-positioned overlay briefly displayed the previous window's content before repositioning.
- **Fix:** Split OverlayWindow.Show(bounds) into Reposition(bounds) -> Paint -> Show() so content is correct before the window becomes visible.
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayWindow.cs, focus/Windows/Daemon/Overlay/OverlayManager.cs
- **Verification:** Stale flash eliminated in rapid-press testing
- **Committed in:** 51fc9a9

**3. [Rule 2 - Missing Critical] Left/right overlay bounds expansion**
- **Found during:** Task 1 (visual inspection with multiple windows)
- **Issue:** Left/right border overlays visually overlapped the corners of up/down border overlays when all four directions had candidates on the same window.
- **Fix:** Expand left/right overlay bounds by 3px in all directions to create visual separation.
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
- **Verification:** All four directional borders render without visual overlap
- **Committed in:** 51fc9a9

**4. [Rule 2 - Missing Critical] Monitor-edge bounds clamping**
- **Found during:** Task 1 (testing with window partially off-screen)
- **Issue:** Overlay bounds extended beyond monitor edge when source window was positioned partially off-screen, causing rendering artifacts.
- **Fix:** Added MonitorFromWindow + GetMonitorInfo clamping to pin overlay bounds within the visible monitor work area.
- **Files modified:** focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
- **Verification:** Overlays stay within screen bounds for edge-positioned windows
- **Committed in:** 0c60567

---

**Total deviations:** 4 (1 user-approved change, 1 bug fix, 2 missing critical additions)
**Impact on plan:** Fade removal was intentional user preference. All other fixes necessary for correct visual output. No unrelated scope creep.

## Issues Encountered
- CAPSLOCK LED forcibly suppressed at startup via keybd_event synthetic press/release sequence — required verifying CsWin32 KEYBD_EVENT_FLAGS enum values at runtime
- STA out-param pattern required restructuring DaemonApplicationContext constructor signature — `out OverlayOrchestrator orchestrator` fills before constructor returns, allowing DaemonCommand.Run to capture it for the shutdown sequence

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 6 complete — all v2.0 overlay preview milestone requirements met
- The complete overlay navigation experience is working: CAPSLOCK hold shows directional colored borders, CAPSLOCK release hides them instantly, foreground changes reposition overlays
- REQUIREMENTS: OVERLAY-01, OVERLAY-03, OVERLAY-04, OVERLAY-05, DAEMON-03, CFG-06 all satisfied and human-verified
- v2.0 milestone (Overlay Preview) is COMPLETE

---
*Phase: 06-navigation-integration*
*Completed: 2026-03-01*
