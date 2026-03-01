---
phase: 03-config-strategies-complete-cli
plan: 01
subsystem: config
tags: [csharp, dotnet, json, glob, scoring, navigation, strategy-pattern]

# Dependency graph
requires:
  - phase: 02-navigation-pipeline
    provides: NavigationService.GetRankedCandidates, Direction enum, WindowInfo record

provides:
  - FocusConfig POCO with Strategy/WrapBehavior enums and Load/WriteDefaults/GetConfigPath
  - ExcludeFilter with glob-based process name filtering via FileSystemGlobbing Matcher
  - NavigationService 3-strategy dispatch: Balanced, StrongAxisBias, ClosestInDirection
  - MessageBeep P/Invoke binding via CsWin32 NativeMethods.txt
  - Microsoft.Extensions.FileSystemGlobbing 8.0.0 NuGet package

affects:
  - 03-02-PLAN (wires FocusConfig, ExcludeFilter, strategy selection into CLI)

# Tech tracking
tech-stack:
  added:
    - Microsoft.Extensions.FileSystemGlobbing 8.0.0 (in-memory glob matching for process name exclusions)
  patterns:
    - Strategy enum dispatch via Func<> delegate for scoring function selection
    - JSON config with camelCase enum serialization (JsonStringEnumConverter + JsonNamingPolicy.CamelCase)
    - Backward-compatible overload pattern: existing callers work unchanged, new callers opt into strategy param

key-files:
  created:
    - focus/Windows/FocusConfig.cs
    - focus/Windows/ExcludeFilter.cs
  modified:
    - focus/Windows/NavigationService.cs
    - focus/focus.csproj
    - focus/NativeMethods.txt

key-decisions:
  - "ScoreStrongAxisBias secondaryWeight=5.0 (vs balanced 2.0) — more aggressive lane preference for users wanting strict directional alignment"
  - "ScoreClosestInDirection uses center-to-center Euclidean distance with half-plane cone (not nearest-edge) — picks geometrically nearest window regardless of alignment"
  - "Existing GetRankedCandidates overloads (no Strategy param) delegate to Strategy.Balanced — zero-change backward compatibility"
  - "ExcludeFilter uses Matcher.Match(processName) directly — WindowInfo.ProcessName is already bare filename via Path.GetFileName"

patterns-established:
  - "Strategy dispatch: Func<double, double, WindowInfo, Direction, double> scoreFn = strategy switch { ... } pattern for extensible scoring"
  - "Config defaults: FocusConfig.Load() returns new FocusConfig() on missing file, parse error, or null deserialization"

requirements-completed: [CFG-01, CFG-04, ENUM-07, NAV-08, NAV-09]

# Metrics
duration: 2min
completed: 2026-02-28
---

# Phase 3 Plan 01: Config Infrastructure and Scoring Strategies Summary

**FocusConfig POCO with Strategy/WrapBehavior enums, ExcludeFilter glob matching, and three-strategy NavigationService dispatch (Balanced/StrongAxisBias/ClosestInDirection)**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-28T10:51:57Z
- **Completed:** 2026-02-28T10:54:04Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- FocusConfig.cs: Strategy enum, WrapBehavior enum, and FocusConfig POCO with Load (JSON with camelCase enum deserialization), WriteDefaults (for --init-config), and GetConfigPath (%APPDATA%/focus/config.json)
- ExcludeFilter.cs: case-insensitive glob pattern matching via FileSystemGlobbing Matcher — filters windows by bare process name
- NavigationService.cs: three scoring strategies — Balanced (primaryWeight=1.0, secondaryWeight=2.0), StrongAxisBias (secondaryWeight=5.0 for aggressive lane preference), ClosestInDirection (pure Euclidean center-to-center with half-plane cone)
- Existing GetRankedCandidates overloads preserved unchanged (backward compatible — delegate to Balanced)
- MessageBeep CsWin32 binding added for beep wrap behavior

## Task Commits

Each task was committed atomically:

1. **Task 1: Create FocusConfig.cs and ExcludeFilter.cs, add NuGet package and MessageBeep binding** - `b31464c` (feat)
2. **Task 2: Add strong-axis-bias and closest-in-direction strategies to NavigationService** - `3134f2f` (feat)

## Files Created/Modified

- `focus/Windows/FocusConfig.cs` - Strategy enum, WrapBehavior enum, FocusConfig POCO with Load/WriteDefaults/GetConfigPath
- `focus/Windows/ExcludeFilter.cs` - Glob-based process name filtering via FileSystemGlobbing Matcher
- `focus/Windows/NavigationService.cs` - Added ScoreStrongAxisBias, ScoreClosestInDirection, and strategy-aware GetRankedCandidates overloads
- `focus/focus.csproj` - Added Microsoft.Extensions.FileSystemGlobbing 8.0.0
- `focus/NativeMethods.txt` - Added MessageBeep binding

## Decisions Made

- ScoreStrongAxisBias uses secondaryWeight=5.0 (vs balanced 2.0) — research recommended higher value for more aggressive lane preference; 5.0 selected as Claude's discretion per NAV-08
- ScoreClosestInDirection uses center-to-center Euclidean distance with half-plane cone — locked decision, uses WindowInfo center (not nearest edge) for simpler "nearest window" semantics
- GetRankedCandidates backward compatibility: both existing overloads now delegate to strategy-aware overloads with Strategy.Balanced, ensuring Program.cs compiles unchanged

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 02 can now wire FocusConfig.Load(), ExcludeFilter.Apply(), and strategy selection into Program.cs CLI
- MessageBeep binding ready for Beep wrap behavior in CLI
- All three strategies selectable via Strategy enum — ready for --strategy CLI flag
- No blockers

---
*Phase: 03-config-strategies-complete-cli*
*Completed: 2026-02-28*

## Self-Check: PASSED

- FOUND: focus/Windows/FocusConfig.cs
- FOUND: focus/Windows/ExcludeFilter.cs
- FOUND: focus/Windows/NavigationService.cs (with ScoreStrongAxisBias, ScoreClosestInDirection)
- FOUND: b31464c (Task 1 commit)
- FOUND: 3134f2f (Task 2 commit)
- Build: 0 errors, 0 warnings
