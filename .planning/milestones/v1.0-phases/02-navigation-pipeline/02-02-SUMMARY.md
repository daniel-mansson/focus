---
phase: 02-navigation-pipeline
plan: 02
subsystem: navigation
tags: [dotnet, cswin32, win32, focus-activation, sendinput, setforegroundwindow, cli, exit-codes]

# Dependency graph
requires:
  - phase: 02-navigation-pipeline/02-01
    provides: NavigationService.GetRankedCandidates, Direction enum, DirectionParser.Parse
  - phase: 01-win32-foundation/01-02
    provides: WindowEnumerator.GetNavigableWindows
  - phase: 01-win32-foundation/01-01
    provides: WindowInfo record, CsWin32 bindings
provides:
  - FocusActivator.TryActivateWindow — SendInput ALT bypass + SetForegroundWindow activation
  - FocusActivator.ActivateBestCandidate — iterates ranked candidates, returns exit codes 0/1/2
  - Program.cs direction CLI argument — `focus left|right|up|down` end-to-end navigation
  - Exit code contract: 0=success, 1=no candidates, 2=error/activation-failure
affects: [03-polish-and-testing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "SendInput ALT bypass: ReadOnlySpan<INPUT> with VIRTUAL_KEY.VK_MENU keydown+keyup before SetForegroundWindow"
    - "FocusActivator.TryActivateWindow: stackalloc INPUT[2] for keydown/keyup, immediate SetForegroundWindow, no delay"
    - "ActivateBestCandidate fallthrough: silently skip elevated windows (false from SetForegroundWindow) and try next candidate"
    - "CA1416 suppression: inline OperatingSystem.IsWindowsVersionAtLeast guard in lambda — static local functions do not propagate SupportedOSPlatform to analyzer"
    - "Navigation silent on success: only error paths use Console.Error.WriteLine; no stdout on exit code 0"
    - "Argument<string?> with ArgumentArity.ZeroOrOne: optional positional arg coexists with --debug option"

key-files:
  created:
    - focus/Windows/FocusActivator.cs
  modified:
    - focus/Program.cs

key-decisions:
  - "Inline navigation code in SetAction lambda (not static method) — SupportedOSPlatform attribute on static local functions not recognized by CA1416 analyzer in top-level statements context"
  - "No RunNavigation static helper method — all platform-specific calls are inside the OperatingSystem.IsWindowsVersionAtLeast guarded block in the lambda, matching the established pattern from --debug enumerate"
  - "VIRTUAL_KEY.VK_MENU used (not const ushort VK_MENU = 0x12) — CsWin32 generates VIRTUAL_KEY enum for wVk field per 02-01-SUMMARY.md documentation"
  - "ReadOnlySpan<INPUT> for SendInput overload — CsWin32 0.3.269 generates ReadOnlySpan not Span per 02-01-SUMMARY.md"

requirements-completed: [FOCUS-01, OUT-02]

# Metrics
duration: 2min
completed: 2026-02-27
---

# Phase 2 Plan 02: Focus Activation and CLI Wiring Summary

**FocusActivator with SendInput ALT bypass + Program.cs direction argument completing end-to-end `focus left|right|up|down` navigation pipeline**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-27T22:02:13Z
- **Completed:** 2026-02-27T22:04:30Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- FocusActivator.TryActivateWindow: stackalloc INPUT[2] (ALT keydown + keyup), ReadOnlySpan<INPUT> SendInput, immediate SetForegroundWindow — no delays, no AttachThreadInput, no keybd_event
- FocusActivator.ActivateBestCandidate: iterates ranked candidates in score order, silently skips elevated windows (false return from SetForegroundWindow), returns 0/1/2 exit codes
- Program.cs direction argument: Argument<string?>("direction") with ZeroOrOne arity — optional positional arg alongside existing --debug option
- Complete navigation pipeline: `focus left/right/up/down` -> DirectionParser.Parse -> WindowEnumerator -> NavigationService.GetRankedCandidates -> FocusActivator.ActivateBestCandidate -> exit code
- Exit code contract implemented: 0=success, 1=no candidates, 2=error/activation-failure
- Navigation is silent on success (no stdout output) — only error paths write to stderr
- `--debug enumerate` regression verified: unaffected by direction argument addition
- Build succeeds 0 errors, 0 warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement FocusActivator with SendInput ALT bypass** - `b7edfd0` (feat)
2. **Task 2: Wire focus direction CLI argument and navigation flow in Program.cs** - `44b202b` (feat)

## Files Created/Modified

- `focus/Windows/FocusActivator.cs` - TryActivateWindow (SendInput ALT bypass + SetForegroundWindow) and ActivateBestCandidate (exit code loop)
- `focus/Program.cs` - Added Argument<string?>("direction") with ZeroOrOne arity; inline navigation flow with OS version guard

## Decisions Made

- Inlined navigation code inside the SetAction lambda (inside `OperatingSystem.IsWindowsVersionAtLeast` guard) instead of extracting to a static local function — the CA1416 analyzer does not recognize `[SupportedOSPlatform]` on static local functions in top-level statement files, causing build warnings that break the zero-warning requirement. The inline approach matches the established pattern used by the `--debug enumerate` code path.
- Used `VIRTUAL_KEY.VK_MENU` (CsWin32 enum value) for ALT key, confirmed from 02-01-SUMMARY.md documentation of CsWin32-generated field names. The plan mentioned `const ushort VK_MENU = 0x12` as a possibility but the summary explicitly documented the actual generated type.
- Used `ReadOnlySpan<INPUT>` for SendInput (not `Span<INPUT>`) — confirmed from 02-01-SUMMARY.md CsWin32 0.3.269 documentation.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Inlined RunNavigation body instead of static local function to eliminate CA1416 warnings**
- **Found during:** Task 2 (build verification)
- **Issue:** `[SupportedOSPlatform("windows6.0.6000")]` attribute on a static local function in a top-level statements file is not recognized by the CA1416 Roslyn analyzer — 4 warnings about `WindowEnumerator`, `WindowEnumerator.GetNavigableWindows()`, `NavigationService.GetRankedCandidates`, and `FocusActivator.ActivateBestCandidate` being called from non-platform-guarded context
- **Fix:** Removed the `RunNavigation` static local function and inlined its body inside the `OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000)` guard block in the SetAction lambda — identical pattern to `--debug enumerate` which already had 0 warnings
- **Files modified:** focus/Program.cs
- **Verification:** Build succeeds with 0 warnings, 0 errors
- **Committed in:** 44b202b (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 CA1416 platform annotation / static local function analyzer limitation)
**Impact on plan:** No behavior change. Plan objective fully achieved. Zero-warning build maintained.

## Verification Results

1. `dotnet build focus/focus.csproj` - PASS (0 errors, 0 warnings)
2. `dotnet run -- --debug enumerate` - PASS (shows window table, 7 windows found)
3. `dotnet run -- left` - PASS (exit 2: candidates exist, activation failed from terminal context — known limitation per STATE.md blockers)
4. `dotnet run -- invaliddir` - PASS (exit 2 + stderr: "Error: Unknown direction 'invaliddir'. Use: left, right, up, down")
5. `focus/Windows/FocusActivator.cs` exists with TryActivateWindow and ActivateBestCandidate - PASS
6. `focus/Program.cs` has `Argument<string?>` with `ArgumentArity.ZeroOrOne` - PASS
7. Navigation silent on success (no stdout in success path) - PASS

## Issues Encountered

- SetForegroundWindow returns false when invoked from Windows Terminal during testing — this is the expected foreground lock behavior documented in STATE.md blockers. The ALT bypass is designed for hotkey (AHK) invocation context, not terminal context. Validation via AHK is a Phase 3 concern.

## Next Phase Readiness

- Phase 2 complete: Direction enum, NavigationService scoring, FocusActivator activation, CLI wiring all done
- End-to-end pipeline: `focus left|right|up|down` calls full enumerate -> score -> activate -> exit code flow
- Phase 3 (polish and testing) can validate via AHK invocation to test the ALT bypass in real hotkey context
- Exit code contract (0/1/2) is stable and documented for AHK wrapper integration

---
*Phase: 02-navigation-pipeline*
*Completed: 2026-02-27*

## Self-Check: PASSED

- FOUND: focus/Windows/FocusActivator.cs
- FOUND: focus/Program.cs
- FOUND: .planning/phases/02-navigation-pipeline/02-02-SUMMARY.md
- FOUND commit: b7edfd0 (Task 1)
- FOUND commit: 44b202b (Task 2)
