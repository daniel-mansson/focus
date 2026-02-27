---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: in_progress
last_updated: "2026-02-27T22:06:00Z"
progress:
  total_phases: 3
  completed_phases: 1
  total_plans: 5
  completed_plans: 4
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-26)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** Phase 2 — Navigation Pipeline

## Current Position

Phase: 2 of 3 (Navigation Pipeline)
Plan: 2 of 2 in current phase (COMPLETE)
Status: In progress
Last activity: 2026-02-27 — Plan 02-02 complete

Progress: [████░░░░░░] 30%

## Performance Metrics

**Velocity:**
- Total plans completed: 4
- Average duration: 3.5 min
- Total execution time: 14 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-win32-foundation | 2 | 10 min | 5 min |
| 02-navigation-pipeline | 2 | 4 min | 2 min |

**Recent Trend:**
- Last 5 plans: 7 min, 3 min, 2 min, 2 min
- Trend: Fast execution

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- Setup: Use CsWin32 0.3.269 for P/Invoke source generation (not DllImport — deprecated, not AOT-safe)
- Setup: Use System.CommandLine 2.0.3 for CLI parsing (stable, AOT-compatible)
- Phase 1: Embed PerMonitorV2 DPI manifest from project creation — HIGH recovery cost if retrofitted later
- Phase 1: Always use DWMWA_EXTENDED_FRAME_BOUNDS, never GetWindowRect as primary coordinate source
- Phase 1: Use SendInput ALT bypass (not keybd_event) before SetForegroundWindow
- 01-01: Use net10.0 target (not net9.0) — .NET 9 runtime not installed; .NET 10 available on dev machine
- 01-01: Use GetWindowLong in NativeMethods.txt (not GetWindowLongPtr) — CsWin32 generates 64-bit safe version from GetWindowLong
- 01-01: EmitCompilerGeneratedFiles=true in csproj — enables CsWin32 generated source inspection in obj/generated/
- 01-01: SupportedOSPlatform("windows5.0") on MonitorHelper — correct minimum version for CA1416 suppression
- 01-02: MemoryMarshal.AsBytes + CreateSpan to pass structs as Span<byte> to DwmGetWindowAttribute — avoids unsafe fixed pointer syntax
- 01-02: OperatingSystem.IsWindowsVersionAtLeast(6,0,6000) guard in Program.cs — suppresses CA1416 for top-level statement lambda where [SupportedOSPlatform] cannot be used
- 01-02: Pre-allocate stackalloc buffers before enumeration loop — required to suppress CA2014 warnings
- 02-01: SupportedOSPlatform windows6.0.6000 on NavigationService (not windows5.0) — DwmGetWindowAttribute requires Vista+; matches WindowEnumerator pattern
- 02-01: Scoring weights primaryWeight=1.0, secondaryWeight=2.0 — Claude's discretion (NAV-07); 2.0 secondary makes alignment matter without dominating
- 02-01: Strict directional filter (<, >, not <=, >=) — windows at exact origin line are ambiguous, excluded
- 02-01: CsWin32 INPUT wVk is VIRTUAL_KEY enum (not ushort) — use VIRTUAL_KEY.VK_MENU; SendInput uses ReadOnlySpan<INPUT> overload
- 02-02: Inline navigation code in SetAction lambda (not static method) — CA1416 analyzer does not recognize [SupportedOSPlatform] on static local functions in top-level statements
- 02-02: FocusActivator.ActivateBestCandidate fallthrough: silently skip elevated windows (false from SetForegroundWindow) and try next candidate

### Pending Todos

None yet.

### Blockers/Concerns

- Phase 1: UWP enumeration behavior on Windows 11 24H2 must be validated with real windows (Calculator, Settings, Windows Terminal, Store) — Raymond Chen Alt+Tab algorithm dates from 2007
- Phase 1: SendInput + ALT bypass must be validated specifically via AHK invocation (not terminal), as AHK version and foreground lock behavior may differ

## Session Continuity

Last session: 2026-02-27
Stopped at: Completed 02-navigation-pipeline/02-02-PLAN.md (FocusActivator SendInput ALT bypass, Program.cs direction CLI wiring, end-to-end navigation pipeline)
Resume file: None
