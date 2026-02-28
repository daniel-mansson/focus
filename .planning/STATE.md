---
gsd_state_version: 1.0
milestone: v2.0
milestone_name: Overlay Preview
status: defining_requirements
last_updated: "2026-02-28T22:00:00Z"
progress:
  total_phases: 0
  completed_phases: 0
  total_plans: 0
  completed_plans: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-28)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** Defining requirements for v2.0 Overlay Preview

## Current Position

Phase: Not started (defining requirements)
Plan: —
Status: Defining requirements
Last activity: 2026-02-28 — Milestone v2.0 started

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Carried from v1.0:

- Setup: Use CsWin32 0.3.269 for P/Invoke source generation (not DllImport — deprecated, not AOT-safe)
- Setup: Use System.CommandLine 2.0.3 for CLI parsing (stable, AOT-compatible)
- Phase 1: Embed PerMonitorV2 DPI manifest from project creation — HIGH recovery cost if retrofitted later
- Phase 1: Always use DWMWA_EXTENDED_FRAME_BOUNDS, never GetWindowRect as primary coordinate source
- Phase 1: Use SendInput ALT bypass (not keybd_event) before SetForegroundWindow
- 01-01: Use net10.0 target (not net9.0) — .NET 9 runtime not installed; .NET 10 available on dev machine
- 01-01: Use GetWindowLong in NativeMethods.txt (not GetWindowLongPtr) — CsWin32 generates 64-bit safe version from GetWindowLong
- 01-01: EmitCompilerGeneratedFiles=true in csproj — enables CsWin32 generated source inspection in obj/generated/
- 02-01: Scoring weights primaryWeight=1.0, secondaryWeight=2.0 — Claude's discretion (NAV-07); 2.0 secondary makes alignment matter without dominating
- 02-01: Strict directional filter (<, >, not <=, >=) — windows at exact origin line are ambiguous, excluded
- 03-01: Existing GetRankedCandidates overloads delegate to Strategy.Balanced — zero-change backward compatibility
- 03-02: --exclude CLI flag replaces config.Exclude entirely (not merged) — locked decision from planning

### Pending Todos

None yet.

### Blockers/Concerns

- v1.0: UWP enumeration behavior on Windows 11 24H2 must be validated with real windows
- v1.0: SendInput + ALT bypass must be validated specifically via AHK invocation

### Quick Tasks Completed (v1.0)

| # | Description | Date | Commit |
|---|-------------|------|--------|
| 1 | Create setup guide for project with AutoHotkey | 2026-02-28 | 933dc6a |
| 2 | Create edge-matching navigation strategy | 2026-02-28 | 9415af0 |
| 3 | Document edge-matching strategy in --help and SETUP.md | 2026-02-28 | 5381cb3 |
| 4 | Add EdgeProximity strategy (near-edge-to-near-edge) | 2026-02-28 | 8c3e540 |
| 5 | Add AxisOnly strategy (pure 1D center-to-center) | 2026-02-28 | abb5cfa |

## Session Continuity

Last session: 2026-02-28
Stopped at: Milestone v2.0 initialization
Resume file: None
