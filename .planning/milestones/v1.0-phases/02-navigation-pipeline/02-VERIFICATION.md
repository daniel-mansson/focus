---
phase: 02-navigation-pipeline
verified: 2026-02-27T22:30:00Z
status: human_needed
score: 12/13 must-haves verified
re_verification: false
human_verification:
  - test: "Invoke `focus left` (or right/up/down) from an AutoHotkey hotkey while two windows are side-by-side"
    expected: "Focus switches to the most geometrically appropriate window in that direction; process exits with code 0"
    why_human: "SetForegroundWindow restriction is only bypassed correctly from a real hotkey context (AHK). The SUMMARY documents that terminal invocation returns exit 2 (activation fails), which is expected. Programmatic verification cannot confirm the ALT bypass works in AHK context."
  - test: "Open windows on two monitors and invoke `focus right` from a window on the primary monitor"
    expected: "Focus switches to a window on the secondary monitor; multi-monitor virtual screen geometry is traversed correctly"
    why_human: "Cross-monitor geometry requires a live multi-monitor environment to confirm virtual screen coordinate math produces correct rankings."
---

# Phase 2: Navigation Pipeline Verification Report

**Phase Goal:** A working focus switcher — invoke `focus <direction>` from AutoHotkey and the correct window receives focus, with proper exit codes
**Verified:** 2026-02-27T22:30:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | NavigationService.GetRankedCandidates returns windows sorted by score, lowest first, for any of the four directions | VERIFIED | NavigationService.cs lines 45-64: `result.Sort()` ascending with tie-breaking; all four Direction cases handled in ScoreCandidate |
| 2 | Windows behind the origin (wrong direction) are eliminated and never appear in results | VERIFIED | NavigationService.cs lines 134-144: strict directional filter returns `double.MaxValue`; line 40: `score < double.MaxValue` guards all insertions |
| 3 | Scoring uses nearest-edge distance to target bounds, not center-to-center | VERIFIED | NavigationService.cs lines 103-110: `NearestPoint` uses `Math.Clamp(px, left, right)` and `Math.Clamp(py, top, bottom)`; candidate.Left/Right/Top/Bottom consumed on line 129 |
| 4 | When no foreground window exists, origin falls back to center of primary monitor | VERIFIED | NavigationService.cs line 96: `return MonitorHelper.GetPrimaryMonitorCenter()` in `GetOriginPoint` when foregroundHwnd == 0 or DWM fails; MonitorHelper.cs lines 55-69: full implementation using MonitorFromPoint(MONITOR_DEFAULTTOPRIMARY) |
| 5 | The currently focused window is always excluded from candidates | VERIFIED | NavigationService.cs lines 36-37: `if (window.Hwnd == fgHwnd) continue;` before scoring |
| 6 | Multi-monitor windows score purely by virtual screen geometry with no same-monitor preference | VERIFIED | ScoreCandidate has no monitor index check; NearestPoint uses raw Left/Top/Right/Bottom virtual screen coords from WindowInfo |
| 7 | Running `focus left` (or right/up/down) switches focus to the best candidate window in that direction | HUMAN NEEDED | Code path is fully wired (Program.cs → NavigationService → FocusActivator); ALT bypass activation from AHK context requires live hotkey test |
| 8 | Exit code is 0 when a window is successfully activated | VERIFIED | FocusActivator.cs line 74: `return 0;` when TryActivateWindow returns true |
| 9 | Exit code is 1 when no candidates exist in the given direction | VERIFIED | FocusActivator.cs line 69: `return 1;` when rankedCandidates.Count == 0 |
| 10 | Exit code is 2 when candidates exist but all activation attempts fail | VERIFIED | FocusActivator.cs line 78: `return 2;` after exhausting all candidates |
| 11 | Exit code is 2 when an invalid direction string is provided | VERIFIED | Program.cs lines 60-65: DirectionParser.Parse returns null → `return 2` with stderr message |
| 12 | SetForegroundWindow is preceded by SendInput ALT keydown+keyup bypass | VERIFIED | FocusActivator.cs line 50: `PInvoke.SendInput(inputs, ...)` immediately followed by line 53: `return PInvoke.SetForegroundWindow(targetHwnd)` with no delay |
| 13 | If the best candidate fails to activate (elevated window), the next-best is tried | VERIFIED | FocusActivator.cs lines 71-76: foreach loop continues silently on false TryActivateWindow return |

**Score:** 12/13 truths verified (1 requires human verification)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `focus/Windows/Direction.cs` | Direction enum and string parser | VERIFIED | Lines 6-21: `enum Direction { Left, Right, Up, Down }` + `DirectionParser.Parse` case-insensitive switch expression |
| `focus/Windows/NavigationService.cs` | Directional scoring engine with candidate ranking | VERIFIED | 195 lines; GetRankedCandidates, GetOriginPoint, ScoreCandidate, NearestPoint all implemented substantively |
| `focus/Windows/MonitorHelper.cs` | GetPrimaryMonitorCenter fallback method | VERIFIED | Lines 55-69: full implementation via MonitorFromPoint(MONITOR_DEFAULTTOPRIMARY) + GetMonitorInfo |
| `focus/NativeMethods.txt` | CsWin32 bindings for GetForegroundWindow, SetForegroundWindow, SendInput, MonitorFromPoint | VERIFIED | Lines 19-22: all four entries present |
| `focus/Windows/FocusActivator.cs` | SendInput ALT bypass + SetForegroundWindow activation with fallthrough | VERIFIED | 80 lines; TryActivateWindow and ActivateBestCandidate fully implemented |
| `focus/Program.cs` | Direction argument wiring + navigation flow + exit code return | VERIFIED | Lines 9-13: Argument<string?> with ZeroOrOne arity; lines 57-90: complete navigation pipeline inline |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `NavigationService.cs` | `MonitorHelper.cs` | `GetPrimaryMonitorCenter` fallback | WIRED | NavigationService.cs line 96: `return MonitorHelper.GetPrimaryMonitorCenter();` |
| `NavigationService.cs` | `WindowInfo.cs` | `candidate.Left/Right/Top/Bottom` nearest-edge scoring | WIRED | NavigationService.cs line 129: `candidate.Left, candidate.Top, candidate.Right, candidate.Bottom` passed to NearestPoint |
| `NavigationService.cs` | `PInvoke.GetForegroundWindow` | Gets foreground HWND for origin + exclusion | WIRED | NavigationService.cs line 26: `var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();` |
| `Program.cs` | `NavigationService.cs` | `NavigationService.GetRankedCandidates` | WIRED | Program.cs line 80: `var ranked = NavigationService.GetRankedCandidates(windows, direction.Value);` |
| `Program.cs` | `FocusActivator.cs` | `FocusActivator.ActivateBestCandidate` (which calls TryActivateWindow internally) | WIRED | Program.cs line 83: `return FocusActivator.ActivateBestCandidate(ranked);`; FocusActivator.cs line 73: `TryActivateWindow(window.Hwnd)` |
| `FocusActivator.cs` | `PInvoke.SendInput` + `PInvoke.SetForegroundWindow` | ALT keydown+keyup before SetForegroundWindow | WIRED | FocusActivator.cs line 50: `PInvoke.SendInput(inputs, ...)` immediately before line 53: `PInvoke.SetForegroundWindow(targetHwnd)` |
| `Program.cs` | `Direction.cs` | `DirectionParser.Parse` | WIRED | Program.cs line 60: `var direction = DirectionParser.Parse(directionValue);` |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| NAV-01 | 02-01-PLAN | User can navigate focus left | SATISFIED | DirectionParser handles "left" → Direction.Left; ScoreCandidate Direction.Left case implemented; Program.cs direction flow wired |
| NAV-02 | 02-01-PLAN | User can navigate focus right | SATISFIED | DirectionParser handles "right" → Direction.Right; ScoreCandidate Direction.Right case implemented |
| NAV-03 | 02-01-PLAN | User can navigate focus up | SATISFIED | DirectionParser handles "up" → Direction.Up; ScoreCandidate Direction.Up case implemented |
| NAV-04 | 02-01-PLAN | User can navigate focus down | SATISFIED | DirectionParser handles "down" → Direction.Down; ScoreCandidate Direction.Down case implemented |
| NAV-05 | 02-01-PLAN | Navigation works across multiple monitors via virtual screen coordinates | SATISFIED | WindowInfo.Left/Right/Top/Bottom are virtual screen coords (from Phase 1 DWM bounds); no per-monitor filtering in ScoreCandidate |
| NAV-07 | 02-01-PLAN | Tool supports "balanced" weighting strategy | SATISFIED | NavigationService.cs lines 164-167: `primaryWeight = 1.0`, `secondaryWeight = 2.0`; formula `primaryWeight * primaryDist + secondaryWeight * secondaryDist` |
| FOCUS-01 | 02-02-PLAN | Tool switches focus using SetForegroundWindow with SendInput ALT bypass | SATISFIED (code) / HUMAN for runtime | FocusActivator.cs implements full ALT bypass + SetForegroundWindow; AHK runtime verification is human item |
| OUT-02 | 02-02-PLAN | Tool returns meaningful exit codes (0=switched, 1=no candidate, 2=error) | SATISFIED | Exit code 0: FocusActivator.cs line 74; exit code 1: FocusActivator.cs line 69; exit code 2: FocusActivator.cs line 78, Program.cs line 65 (invalid direction) |

**Orphaned requirements check:** REQUIREMENTS.md Traceability table maps NAV-01 through NAV-07, FOCUS-01, and OUT-02 to Phase 2. All eight are claimed by the plans (NAV-01–NAV-05, NAV-07 in 02-01; FOCUS-01, OUT-02 in 02-02). No orphaned requirements.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None found | - | - | - | - |

No TODO/FIXME/HACK/PLACEHOLDER comments, no stub returns (return null/empty), no empty handlers, no unconnected state found in any modified file.

**Note on exit code on "No command specified" path:** Program.cs line 93 returns exit code 1 when no direction and no --debug is provided (`return 1`). This is a minor inconsistency — OUT-02 defines exit code 1 as "no candidate in direction", but a no-argument invocation also returns 1. This is an edge case outside the defined exit code contract (the contract only applies when a direction is provided) and does not block the phase goal.

### Human Verification Required

#### 1. Focus Switch from AutoHotkey Hotkey Context

**Test:** Create an AHK script with `^Left::Run focus.exe left` (or equivalent), open two windows side by side, and trigger the hotkey from the left window.
**Expected:** Focus switches to the right window and the process exits with code 0. The `--debug enumerate` command shows the right window as a candidate before the hotkey is triggered.
**Why human:** `SetForegroundWindow` has a foreground lock restriction that only allows focus-setting from the current foreground process or when a hotkey event is in-flight. The SendInput ALT bypass is designed specifically for this context. The SUMMARY documents that terminal invocation produces exit code 2 (SetForegroundWindow returns false), which is the expected fallback. Only an AHK invocation can confirm the bypass works end-to-end. This is the primary delivery criterion from the phase goal.

#### 2. Multi-Monitor Focus Switching

**Test:** With windows distributed across two monitors, invoke `focus right` from a window on the primary (left) monitor.
**Expected:** Focus switches to a window on the secondary (right) monitor; the selected window is the geometrically nearest one in that direction based on virtual screen coordinates.
**Why human:** Requires a multi-monitor setup. Virtual screen coordinate math is verified in code, but cross-monitor geometric correctness requires a live environment to confirm the ranking produces the expected result.

### Gaps Summary

No gaps found. All artifacts are substantive and fully wired. The code path for `focus <direction>` correctly chains:
1. `DirectionParser.Parse` (direction string → Direction enum)
2. `WindowEnumerator.GetNavigableWindows` (Phase 1 pipeline, unmodified)
3. `NavigationService.GetRankedCandidates` (nearest-edge scoring, 1.0/2.0 balanced weights)
4. `FocusActivator.ActivateBestCandidate` (SendInput ALT bypass → SetForegroundWindow → exit code)

The only open item is runtime validation of the ALT bypass in AHK context, which requires human testing. All programmatically verifiable aspects of the phase goal are confirmed.

**Build status:** 0 errors, 0 warnings (verified via `dotnet build focus/focus.csproj --no-incremental`).

**Commits verified:** 9a9969e (NativeMethods + Direction), 47acb9d (NavigationService + MonitorHelper), b7edfd0 (FocusActivator), 44b202b (Program.cs wiring) — all present in git log.

---

_Verified: 2026-02-27T22:30:00Z_
_Verifier: Claude (gsd-verifier)_
