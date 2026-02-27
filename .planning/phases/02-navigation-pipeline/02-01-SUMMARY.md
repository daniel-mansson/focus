---
phase: 02-navigation-pipeline
plan: 01
subsystem: navigation
tags: [dotnet, cswin32, win32, navigation, geometry, scoring, direction, dwm]

# Dependency graph
requires:
  - phase: 01-win32-foundation/01-01
    provides: WindowInfo record, MonitorHelper utilities, CsWin32 bindings
  - phase: 01-win32-foundation/01-02
    provides: WindowEnumerator.GetNavigableWindows()
provides:
  - Direction enum (Left, Right, Up, Down) with DirectionParser.Parse (case-insensitive)
  - NavigationService.GetRankedCandidates(List<WindowInfo>, Direction) — scored candidate list
  - NavigationService.GetOriginPoint — DWM bounds center + primary monitor fallback
  - NavigationService.ScoreCandidate — nearest-edge distance with balanced 1.0/2.0 weights
  - NavigationService.NearestPoint — Math.Clamp nearest-point-on-AABB
  - MonitorHelper.GetPrimaryMonitorCenter — primary monitor center via MonitorFromPoint
  - CsWin32 bindings: SetForegroundWindow, GetForegroundWindow, SendInput, MonitorFromPoint
affects: [02-02-focus-activation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Nearest-edge scoring: Math.Clamp(px, left, right) / Math.Clamp(py, top, bottom) for correct AABB nearest point"
    - "Balanced scoring formula: primaryWeight(1.0) * primaryDist + secondaryWeight(2.0) * secondaryDist"
    - "Strict directional filter: nearX < originX (not <=) — windows at the exact origin line are ambiguous, excluded"
    - "CsWin32 INPUT struct: union field Anonymous (INPUT._Anonymous_e__Union), keyboard field ki (KEYBDINPUT)"
    - "VIRTUAL_KEY.VK_MENU = 18 for ALT key; KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP for key-up"
    - "SendInput overload: ReadOnlySpan<INPUT> (not Span<INPUT>) in CsWin32 0.3.269"
    - "SupportedOSPlatform windows6.0.6000 on NavigationService — matches WindowEnumerator pattern for DWM dependency"

key-files:
  created:
    - focus/Windows/Direction.cs
    - focus/Windows/NavigationService.cs
  modified:
    - focus/NativeMethods.txt
    - focus/Windows/MonitorHelper.cs

key-decisions:
  - "SupportedOSPlatform windows6.0.6000 on NavigationService (not windows5.0) — DwmGetWindowAttribute requires Vista+; matches WindowEnumerator pattern"
  - "Scoring weights: primaryWeight=1.0, secondaryWeight=2.0 — Claude's discretion (NAV-07); 2.0 secondary makes alignment matter without dominating"
  - "Strict directional filter (<, >, not <=, >=) — windows whose nearest edge is exactly at origin are ambiguous and should not match"
  - "Tie-breaking: smaller secondary distance first, then smaller primary distance — prefer aligned window over off-axis one when scores are equal within 1e-6"
  - "CsWin32 INPUT field names confirmed: Anonymous union, ki keyboard field, VIRTUAL_KEY.VK_MENU for ALT, ReadOnlySpan<INPUT> SendInput overload"

requirements-completed: [NAV-01, NAV-02, NAV-03, NAV-04, NAV-05, NAV-07]

# Metrics
duration: 2min
completed: 2026-02-27
---

# Phase 2 Plan 01: Navigation Scoring Engine Summary

**Direction enum + NavigationService with nearest-edge balanced scoring (primaryWeight=1.0, secondaryWeight=2.0) + MonitorHelper.GetPrimaryMonitorCenter + 4 new CsWin32 API bindings**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-27T21:57:00Z
- **Completed:** 2026-02-27T21:59:32Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Direction enum with Left/Right/Up/Down and DirectionParser.Parse (case-insensitive) in Focus.Windows namespace
- NavigationService.GetRankedCandidates: GetForegroundWindow for origin + exclusion, scored/sorted candidate list
- Scoring uses nearest-edge distance (Math.Clamp AABB nearest point) — not center-to-center
- Balanced scoring formula: 1.0 * primaryDist + 2.0 * secondaryDist (NAV-07 compliant, tunable constants)
- Strict directional filter: windows whose nearest edge is exactly at origin line are excluded (ambiguous case)
- Tie-breaking: smaller secondary distance first, then smaller primary distance when scores within 1e-6
- MonitorHelper.GetPrimaryMonitorCenter: MonitorFromPoint(MONITOR_DEFAULTTOPRIMARY) + GetMonitorInfo fallback
- 4 new CsWin32 bindings: SetForegroundWindow, GetForegroundWindow, SendInput, MonitorFromPoint
- CsWin32 INPUT struct field names documented for Plan 02-02: Anonymous union, ki field, VIRTUAL_KEY.VK_MENU, ReadOnlySpan<INPUT> SendInput overload
- Build succeeds 0 errors, 0 warnings; existing `dotnet run -- --debug enumerate` unaffected

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Phase 2 Win32 APIs to NativeMethods.txt and create Direction enum** - `9a9969e` (feat)
2. **Task 2: Implement NavigationService scoring engine and MonitorHelper.GetPrimaryMonitorCenter** - `47acb9d` (feat)

## Files Created/Modified

- `focus/Windows/Direction.cs` - Direction enum (Left/Right/Up/Down) + DirectionParser.Parse (case-insensitive)
- `focus/Windows/NavigationService.cs` - GetRankedCandidates, GetOriginPoint, ScoreCandidate, NearestPoint; [SupportedOSPlatform("windows6.0.6000")]
- `focus/NativeMethods.txt` - Added SetForegroundWindow, GetForegroundWindow, SendInput, MonitorFromPoint
- `focus/Windows/MonitorHelper.cs` - Added GetPrimaryMonitorCenter method

## Decisions Made

- Used `windows6.0.6000` (not `windows5.0`) on NavigationService: `DwmGetWindowAttribute` (used in `GetOriginPoint`) requires Vista+. The plan specified `windows5.0` but the DWM API is a blocker; matching `WindowEnumerator` pattern was the correct fix.
- Scoring weights 1.0/2.0 chosen at Claude's discretion (NAV-07): 2.0 secondary weight means a window directly ahead scores better than one diagonally offset. Empirical tuning can adjust in later plans.
- Strict inequality in directional filter: a window at exactly the same X or Y coordinate as the origin is ambiguous — excluded rather than included.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Changed SupportedOSPlatform from windows5.0 to windows6.0.6000 on NavigationService**
- **Found during:** Task 2 (build verification)
- **Issue:** CA1416 warning: `PInvoke.DwmGetWindowAttribute(HWND, DWMWINDOWATTRIBUTE, Span<byte>)` is only supported on `windows6.0.6000` and later, but NavigationService was annotated as `windows5.0`
- **Fix:** Changed `[SupportedOSPlatform("windows5.0")]` to `[SupportedOSPlatform("windows6.0.6000")]` on NavigationService class — matches the WindowEnumerator pattern which uses the same DWM API
- **Files modified:** focus/Windows/NavigationService.cs
- **Verification:** Build succeeds with 0 warnings, 0 errors
- **Committed in:** 47acb9d (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking CA1416 platform annotation)
**Impact on plan:** Required for clean compilation. No scope creep.

## CsWin32 INPUT Struct Documentation (for Plan 02-02)

Inspected generated files in `focus/obj/Debug/net8.0/generated/`:

| Item | Generated Name | Notes |
|------|---------------|-------|
| INPUT union field | `Anonymous` (type `INPUT._Anonymous_e__Union`) | Used as `Anonymous = new INPUT._Anonymous_e__Union { ... }` |
| Keyboard member | `ki` (type `KEYBDINPUT`) | Field on `_Anonymous_e__Union` |
| `wVk` type | `VIRTUAL_KEY` enum | Use `VIRTUAL_KEY.VK_MENU` (= 18) for ALT |
| `dwFlags` type | `KEYBD_EVENT_FLAGS` | Use `KEYEVENTF_KEYUP` for key-up event |
| `INPUT_TYPE` | `INPUT_KEYBOARD = 1U` | Set `type` field on INPUT |
| SendInput overload | `ReadOnlySpan<INPUT>` | NOT `Span<INPUT>` — CsWin32 0.3.269 generates ReadOnlySpan |
| Namespace | `Windows.Win32.UI.Input.KeyboardAndMouse` | All INPUT types live here |

## Issues Encountered

- `VIRTUAL_KEY` is used for `wVk` in KEYBDINPUT, not `ushort` as the research suggested. Must use `VIRTUAL_KEY.VK_MENU` (generated enum value 18) rather than the `const ushort VK_MENU = 0x12` pattern from RESEARCH.md.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Plan 02-01 complete: Direction enum, NavigationService scoring engine, MonitorHelper.GetPrimaryMonitorCenter all ready
- Plan 02-02 (focus activation) can call `NavigationService.GetRankedCandidates(windows, direction)` directly
- CsWin32 INPUT struct field names documented above — Plan 02-02 can implement SendInput ALT bypass without guessing
- All 4 new CsWin32 bindings compiled and verified (SetForegroundWindow, GetForegroundWindow, SendInput, MonitorFromPoint)

---
*Phase: 02-navigation-pipeline*
*Completed: 2026-02-27*

## Self-Check: PASSED

- FOUND: focus/Windows/Direction.cs
- FOUND: focus/Windows/NavigationService.cs
- FOUND: focus/Windows/MonitorHelper.cs
- FOUND: .planning/phases/02-navigation-pipeline/02-01-SUMMARY.md
- FOUND commit: 9a9969e (Task 1)
- FOUND commit: 47acb9d (Task 2)
