# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-26)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** Phase 1 — Win32 Foundation

## Current Position

Phase: 1 of 3 (Win32 Foundation)
Plan: 1 of 5 in current phase
Status: In progress
Last activity: 2026-02-27 — Plan 01-01 complete

Progress: [█░░░░░░░░░] 7%

## Performance Metrics

**Velocity:**
- Total plans completed: 1
- Average duration: 7 min
- Total execution time: 7 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-win32-foundation | 1 | 7 min | 7 min |

**Recent Trend:**
- Last 5 plans: 7 min
- Trend: Baseline established

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

### Pending Todos

None yet.

### Blockers/Concerns

- Phase 1: UWP enumeration behavior on Windows 11 24H2 must be validated with real windows (Calculator, Settings, Windows Terminal, Store) — Raymond Chen Alt+Tab algorithm dates from 2007
- Phase 1: SendInput + ALT bypass must be validated specifically via AHK invocation (not terminal), as AHK version and foreground lock behavior may differ

## Session Continuity

Last session: 2026-02-27
Stopped at: Completed 01-win32-foundation/01-01-PLAN.md (project scaffold + data models)
Resume file: None
