---
phase: 05-overlay-windows
plan: 02
subsystem: ui
tags: [overlay, winforms, gdi, layered-window, premultiplied-alpha, debug-command]

# Dependency graph
requires:
  - phase: 05-overlay-windows/05-01
    provides: IOverlayRenderer, OverlayWindow, BorderRenderer, OverlayManager, OverlayColors, FocusConfig.OverlayColors

provides:
  - focus --debug overlay <direction> command in Program.cs — standalone visual test for overlay pipeline
  - Human-verified overlay rendering: click-through, Alt+Tab exclusion, no focus steal, correct ARGB colors
  - Fix for GDI alpha detection in BorderRenderer premultiplied-alpha pass

affects: [06-overlay-wiring]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Application.DoEvents + Thread.Sleep(16) message pump for single-threaded overlay test commands"
    - "Background keypress thread + ManualResetEventSlim for non-blocking Console.ReadKey with message pump"
    - "GDI pixel detection via RGB mask (0x00FFFFFF) not alpha — GDI never sets alpha bytes in DIBs"

key-files:
  created: []
  modified:
    - focus/Program.cs
    - focus/Windows/Daemon/Overlay/BorderRenderer.cs

key-decisions:
  - "GDI RoundRect draws RGB but leaves alpha at 0x00 — detect drawn pixels via (pixel & 0x00FFFFFF) != 0, not pixAlpha != 0"
  - "Use Application.DoEvents + Thread.Sleep(16) as message pump for debug overlay command (no full WinForms app required)"
  - "Background thread with ManualResetEventSlim handles Console.ReadKey without blocking the message pump thread"

patterns-established:
  - "GDI-into-DIB alpha pattern: always check RGB presence, never alpha channel, when detecting GDI-drawn pixels"

requirements-completed: [OVERLAY-02, RENDER-02, CFG-05]

# Metrics
duration: ~30min
completed: 2026-03-01
---

# Phase 5 Plan 02: Debug Overlay Command and Visual Verification Summary

**`focus --debug overlay <direction>` command wired end-to-end — GDI alpha detection bug found and fixed during verification, overlay now renders correct semi-transparent rounded-corner borders on all four directions**

## Performance

- **Duration:** ~30 min
- **Started:** 2026-03-01T09:00:00Z
- **Completed:** 2026-03-01T09:41:00Z
- **Tasks:** 2 (1 auto + 1 checkpoint:human-verify)
- **Files modified:** 2

## Accomplishments

- Added `focus --debug overlay <direction>` debug command to Program.cs — loads config, gets foreground window bounds via DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS), creates OverlayManager, shows overlay, waits for keypress via background thread + ManualResetEventSlim, pumps messages with Application.DoEvents
- Discovered and fixed GDI alpha detection bug in BorderRenderer: premultiplied-alpha pass was checking `if (pixAlpha != 0)` but GDI's RoundRect never sets alpha bytes (leaves them 0x00), so no pixels were processed; fixed to `if ((pixel & 0x00FFFFFF) != 0)`
- Human-verified all overlay properties: correct ARGB colors per direction, semi-transparent borders, rounded Win11 corners, click-through behavior, no focus steal, no Alt+Tab entry, clean dismissal on keypress

## Task Commits

Each task was committed atomically:

1. **Task 1: Add focus --debug overlay command to Program.cs** - `deb654f` (feat)
2. **Task 2: Fix GDI alpha detection bug in BorderRenderer** - `e0ba035` (fix — deviation, found during verification)

**Plan metadata:** _(docs commit — in progress)_

## Files Created/Modified

- `focus/Program.cs` - Added `--debug overlay <direction>` handler with message pump loop and background keypress thread
- `focus/Windows/Daemon/Overlay/BorderRenderer.cs` - Fixed premultiplied-alpha pixel detection: `if ((pixel & 0x00FFFFFF) != 0)` instead of `if (pixAlpha != 0)`

## Decisions Made

- Used `Application.DoEvents() + Thread.Sleep(16)` as message pump for the debug command — avoids requiring a full WinForms Application.Run and keeps the command simple
- Background keypress thread with `ManualResetEventSlim` avoids blocking the message pump on `Console.ReadKey`
- GDI-into-DIB rule established: always detect drawn pixels via RGB mask (0x00FFFFFF), never via alpha — GDI never writes alpha bytes into DIB sections

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed GDI alpha detection in BorderRenderer premultiplied pass**
- **Found during:** Task 2 (visual verification of overlay rendering)
- **Issue:** `BorderRenderer.Paint()` was checking `if (pixAlpha != 0)` to find GDI-drawn pixels before applying premultiplied alpha. GDI's `RoundRect` writes only RGB values into the DIB — the alpha channel of every pixel remains 0x00 after drawing. This meant the premultiplied-alpha pass found zero eligible pixels, so `UpdateLayeredWindow` received a DIB with all-zero alpha and rendered nothing visible.
- **Fix:** Changed condition to `if ((pixel & 0x00FFFFFF) != 0)` — detects drawn pixels by checking any non-zero RGB component instead of alpha.
- **Files modified:** `focus/Windows/Daemon/Overlay/BorderRenderer.cs`
- **Verification:** Human-verified overlay visually — correct semi-transparent colored border appeared on the foreground window
- **Committed in:** `e0ba035` (standalone fix commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — bug in premultiplied alpha detection)
**Impact on plan:** Essential correctness fix — without it the overlay was completely invisible. No scope creep.

## Issues Encountered

- GDI-into-DIB alpha behavior: RoundRect does not write alpha channel values. This is a documented GDI behavior but easy to miss when writing the premultiplied-alpha compositing pass. The fix is deterministic and correct.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Overlay rendering pipeline fully verified end-to-end: IOverlayRenderer -> BorderRenderer -> OverlayWindow -> OverlayManager -> Program.cs debug command
- All OVERLAY-02 properties confirmed: click-through, Alt+Tab exclusion, no focus steal, correct ARGB colors per direction
- GDI alpha detection pattern established and documented — Phase 6 must maintain the `(pixel & 0x00FFFFFF) != 0` check
- Phase 6 (Overlay Wiring) can integrate OverlayManager directly into the daemon without further rendering work

---
*Phase: 05-overlay-windows*
*Completed: 2026-03-01*

## Self-Check: PASSED

- FOUND: .planning/phases/05-overlay-windows/05-02-SUMMARY.md
- FOUND: focus/Windows/Daemon/Overlay/BorderRenderer.cs
- FOUND: focus/Program.cs
- FOUND commit deb654f: feat(05-02): add focus --debug overlay command to Program.cs
- FOUND commit e0ba035: fix(05-02): fix GDI alpha detection in BorderRenderer premultiplied pass
