---
phase: quick-4
plan: 01
subsystem: config
tags: [config, json, serialization, kebab-case, documentation]
dependency_graph:
  requires: []
  provides: [kebab-case-config-values]
  affects: [focus/Windows/FocusConfig.cs, SETUP.md]
tech_stack:
  added: []
  patterns: [JsonNamingPolicy.KebabCaseLower for enum serialization]
key_files:
  created: []
  modified:
    - focus/Windows/FocusConfig.cs
    - SETUP.md
decisions:
  - "JsonNamingPolicy.KebabCaseLower replaces CamelCase in both Load() and WriteDefaults() — no backward compat needed since camelCase values were confusing users"
  - "PropertyNameCaseInsensitive = true retained in Load() — only enum VALUE naming policy changed, not property names"
metrics:
  duration: ~1 min
  completed: "2026-03-01"
  tasks_completed: 2
  files_modified: 2
---

# Quick Task 4: Kebab-Case Config Values Summary

**One-liner:** Config file now uses `JsonNamingPolicy.KebabCaseLower` so `"strategy": "strong-axis-bias"` and `"wrap": "no-op"` work identically in both CLI and config.json.

## What Was Done

Eliminated the naming convention mismatch where CLI used kebab-case (`--strategy strong-axis-bias`) but config.json required camelCase (`"strongAxisBias"`). Both now use the same kebab-case format.

## Tasks Completed

| Task | Description | Commit | Files |
|------|-------------|--------|-------|
| 1 | Switch FocusConfig JSON enum policy from CamelCase to KebabCaseLower | 3d08a5e | focus/Windows/FocusConfig.cs |
| 2 | Update SETUP.md to document kebab-case config values | 9b4a04e | SETUP.md |

## Changes Made

### Task 1: FocusConfig.cs

Changed `JsonNamingPolicy.CamelCase` to `JsonNamingPolicy.KebabCaseLower` in two locations:

- **Load() method (line 37):** Enum deserialization now accepts kebab-case values
- **WriteDefaults() method (line 53):** `--init-config` now generates kebab-case values

`JsonNamingPolicy.KebabCaseLower` is available in .NET 8+ and converts enum names automatically:
- `StrongAxisBias` → `strong-axis-bias`
- `ClosestInDirection` → `closest-in-direction`
- `NoOp` → `no-op`
- `EdgeMatching` → `edge-matching`
- `EdgeProximity` → `edge-proximity`
- `AxisOnly` → `axis-only`
- `Balanced` → `balanced`

### Task 2: SETUP.md

Updated the Configuration section (no changes outside that section):
- Default config.json block: `"noOp"` → `"no-op"`
- Config fields table strategy values: camelCase → kebab-case
- Config fields table wrap values: `noOp` → `no-op`
- Removed the "Note: JSON field values use camelCase... CLI flags use kebab-case..." line
- Wrap behavior list: `noOp` → `no-op`
- Exclude example block: `"noOp"` → `"no-op"`

## Verification

All checks passed:
1. `dotnet build` — succeeded with 0 errors
2. `KebabCaseLower` appears 2 times in FocusConfig.cs (Load + WriteDefaults)
3. `JsonNamingPolicy.CamelCase` appears 0 times in FocusConfig.cs
4. No camelCase enum values (`noOp`, `strongAxisBias`, etc.) remain in SETUP.md
5. No `camelCase` mentions remain in SETUP.md

## Deviations from Plan

None - plan executed exactly as written.

## Self-Check: PASSED

- [x] `focus/Windows/FocusConfig.cs` — modified (2 KebabCaseLower changes)
- [x] `SETUP.md` — modified (kebab-case throughout config section, camelCase note removed)
- [x] Commit 3d08a5e — exists (`feat(quick-4): switch JSON enum policy from CamelCase to KebabCaseLower`)
- [x] Commit 9b4a04e — exists (`docs(quick-4): update SETUP.md to document kebab-case config values`)
