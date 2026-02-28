---
phase: 03-config-strategies-complete-cli
plan: 02
subsystem: cli
tags: [csharp, dotnet, commandline, config, wrap-around, p-invoke, strategy-pattern]

# Dependency graph
requires:
  - phase: 03-config-strategies-complete-cli
    plan: 01
    provides: FocusConfig POCO, ExcludeFilter, three-strategy NavigationService dispatch, MessageBeep binding

provides:
  - FocusActivator.ActivateWithWrap: wrap/beep/no-op behavior when no candidates in direction
  - Complete CLI surface: --strategy, --wrap, --exclude, --init-config, --debug score, --debug config
  - Config loading with CLI override merge (strategy, wrap, exclude flags override JSON file)
  - ExcludeFilter applied in all operational paths (enumerate, score, navigation)

affects: []  # Final plan — v1.0 complete

# Tech tracking
tech-stack:
  added: []
  patterns:
    - CLI override merge: FocusConfig.Load() then CLI flags overwrite fields (strategy, wrap, exclude)
    - Granular SupportedOSPlatform attributes on methods (not class) for mixed-version API callers
    - Score table: union of all three strategy candidate lists, with dash for windows filtered by strategy

key-files:
  created: []
  modified:
    - focus/Windows/FocusActivator.cs
    - focus/Program.cs

key-decisions:
  - "ActivateWithWrap/HandleWrap get [SupportedOSPlatform(windows6.0.6000)] (not class-level) — NavigationService requires Vista+; TryActivateWindow/ActivateBestCandidate keep windows5.0 class attribute"
  - "MessageBeep cast: (global::Windows.Win32.UI.WindowsAndMessaging.MESSAGEBOX_STYLE)0xFFFFFFFF — global:: alias required due to Focus.Windows namespace shadowing Windows.Win32"
  - "--exclude CLI flag replaces config.Exclude entirely (not merges) — locked decision from Phase 3 planning"
  - "PrintScoreTable shows union of all three strategy lists; windows filtered by a strategy show dash (not 0)"

patterns-established:
  - "CLI override merge: config = Load() then mutate fields based on parsed CLI values before any platform code runs"
  - "global:: alias for CsWin32 types when Focus.Windows namespace shadows Windows.Win32 namespace prefix"

requirements-completed: [CFG-02, CFG-03, FOCUS-02, OUT-01, OUT-03, DBG-02, DBG-03]

# Metrics
duration: 4min
completed: 2026-02-28
---

# Phase 3 Plan 02: Complete CLI Wiring Summary

**Complete v1 CLI with config-loading, CLI override merge, wrap/beep/no-op activation, --strategy/--wrap/--exclude/--init-config flags, --debug score (all three strategies), and --debug config**

## Performance

- **Duration:** 4 min
- **Started:** 2026-02-28T10:56:56Z
- **Completed:** 2026-02-28T11:00:44Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- FocusActivator.ActivateWithWrap: delegates to HandleWrap (reverses ranked candidates in opposite direction for wrap-around), HandleBeep (MessageBeep system beep), or returns exit 1 for NoOp
- Program.cs completely rewired: loads FocusConfig.Load(), applies CLI overrides for strategy/wrap/exclude, then runs --debug or navigation paths
- --debug config: prints resolved config (file path, exists status, strategy, wrap, exclude) without any platform dependency
- --debug score: runs all three strategies and prints a union score table with dash for windows filtered by a strategy
- --init-config: writes default config.json, warns and exits 1 if file already exists
- ExcludeFilter.Apply called in all three operational paths (enumerate, score, navigation)
- OUT-01 compliant: navigation success is fully silent (no stdout); OUT-03 compliant: --verbose output goes to stderr

## Task Commits

Each task was committed atomically:

1. **Task 1: Add ActivateWithWrap to FocusActivator** - `c1bf561` (feat)
2. **Task 2: Wire complete CLI — config loading, all options, debug modes, exclude filter** - `8073a82` (feat)

**Plan metadata:** (docs commit — see below)

## Files Created/Modified

- `focus/Windows/FocusActivator.cs` - Added ActivateWithWrap, HandleWrap, HandleBeep methods with appropriate SupportedOSPlatform attributes
- `focus/Program.cs` - Complete rewrite of SetAction lambda: all new CLI options, config loading, CLI overrides, all debug modes, exclude filter in all paths, PrintScoreTable helper

## Decisions Made

- ActivateWithWrap and HandleWrap annotated with `[SupportedOSPlatform("windows6.0.6000")]` as individual method attributes rather than updating the class-level `[SupportedOSPlatform("windows5.0")]` — preserves backward compatibility for TryActivateWindow and ActivateBestCandidate
- MessageBeep P/Invoke requires `MESSAGEBOX_STYLE` enum cast: used `(global::Windows.Win32.UI.WindowsAndMessaging.MESSAGEBOX_STYLE)0xFFFFFFFF` — the `global::` prefix is required because the `Focus.Windows` namespace shadows the `Windows` prefix for type resolution
- `--exclude` CLI flag replaces `config.Exclude` entirely (locked decision from planning) — not merged

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed CA1416: HandleWrap calling NavigationService requires Vista+**
- **Found during:** Task 1 (Add ActivateWithWrap to FocusActivator)
- **Issue:** FocusActivator class has `[SupportedOSPlatform("windows5.0")]` but HandleWrap calls NavigationService which is `[SupportedOSPlatform("windows6.0.6000")]` — CA1416 warning promoted to error by project settings
- **Fix:** Added `[SupportedOSPlatform("windows6.0.6000")]` on ActivateWithWrap and HandleWrap individually, `[SupportedOSPlatform("windows5.1.2600")]` on HandleBeep — class attribute stays windows5.0
- **Files modified:** focus/Windows/FocusActivator.cs
- **Verification:** Build passed with 0 errors, 0 warnings
- **Committed in:** c1bf561 (Task 1 commit)

**2. [Rule 1 - Bug] Fixed MessageBeep type mismatch: uint vs MESSAGEBOX_STYLE**
- **Found during:** Task 1 (HandleBeep method)
- **Issue:** `PInvoke.MessageBeep(0xFFFFFFFF)` failed — CsWin32 generates signature taking `MESSAGEBOX_STYLE` enum, not `uint`. Additionally, `Windows.Win32.UI.WindowsAndMessaging.MESSAGEBOX_STYLE` resolved incorrectly due to `Focus.Windows` namespace shadowing `Windows` prefix
- **Fix:** Cast to `(global::Windows.Win32.UI.WindowsAndMessaging.MESSAGEBOX_STYLE)0xFFFFFFFF` using `global::` alias
- **Files modified:** focus/Windows/FocusActivator.cs
- **Verification:** Build passed with 0 errors, 0 warnings
- **Committed in:** c1bf561 (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (2 Rule 1 - Bug)
**Impact on plan:** Both fixes necessary for compilation. CA1416 fix is the correct OS version annotation pattern. MESSAGEBOX_STYLE fix is the correct CsWin32 P/Invoke calling convention. No scope creep.

## Issues Encountered

None beyond the two auto-fixed compilation issues above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 3 Plan 02 is the final plan — focus v1.0 is now feature complete
- All requirements addressed: CFG-02, CFG-03, FOCUS-02, OUT-01, OUT-03, DBG-02, DBG-03
- Tool is ready for real-world AHK hotkey binding and user validation

---
*Phase: 03-config-strategies-complete-cli*
*Completed: 2026-02-28*

## Self-Check: PASSED

- FOUND: focus/Windows/FocusActivator.cs
- FOUND: focus/Program.cs
- FOUND: .planning/phases/03-config-strategies-complete-cli/03-02-SUMMARY.md
- FOUND: c1bf561 (Task 1 commit — ActivateWithWrap)
- FOUND: 8073a82 (Task 2 commit — complete CLI wiring)
- Key link verified: ActivateWithWrap present in FocusActivator.cs
- Key link verified: strategyOption, FocusConfig.Load, ExcludeFilter.Apply, ActivateWithWrap call all present in Program.cs
- Key link verified: GetRankedCandidates called with config.Strategy (line 228-230, Program.cs)
- Build: 0 errors, 0 warnings
