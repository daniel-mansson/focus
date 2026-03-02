---
phase: quick-4
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/FocusConfig.cs
  - SETUP.md
autonomous: true
requirements: [QUICK-4]
must_haves:
  truths:
    - "Config file accepts kebab-case strategy values identical to CLI (e.g., strong-axis-bias, closest-in-direction)"
    - "Config file accepts kebab-case wrap values identical to CLI (e.g., no-op)"
    - "Running --init-config generates a config.json with kebab-case values"
    - "SETUP.md documents kebab-case as the config format (no more camelCase/kebab-case mismatch note)"
  artifacts:
    - path: "focus/Windows/FocusConfig.cs"
      provides: "KebabCaseLower JSON naming policy for enum serialization"
      contains: "JsonNamingPolicy.KebabCaseLower"
    - path: "SETUP.md"
      provides: "Updated documentation showing kebab-case config values"
      contains: "strong-axis-bias"
  key_links:
    - from: "focus/Windows/FocusConfig.cs"
      to: "Strategy enum / WrapBehavior enum"
      via: "JsonStringEnumConverter with KebabCaseLower policy"
      pattern: "JsonNamingPolicy\\.KebabCaseLower"
---

<objective>
Make the config file (config.json) accept the same kebab-case strategy and wrap values that the CLI uses, eliminating the confusing mismatch where CLI uses `strong-axis-bias` but config requires `strongAxisBias`.

Purpose: Users should not need to remember two different naming conventions for the same values.
Output: FocusConfig.cs uses KebabCaseLower naming policy; SETUP.md updated to reflect new format.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Windows/FocusConfig.cs
@focus/Program.cs
@SETUP.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Switch FocusConfig JSON enum policy from CamelCase to KebabCaseLower</name>
  <files>focus/Windows/FocusConfig.cs</files>
  <action>
In `focus/Windows/FocusConfig.cs`, change the `JsonNamingPolicy.CamelCase` to `JsonNamingPolicy.KebabCaseLower` in both locations:

1. **Line 37 (Load method):** Change:
   ```csharp
   Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
   ```
   to:
   ```csharp
   Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
   ```

2. **Line 53 (WriteDefaults method):** Same change:
   ```csharp
   Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
   ```
   to:
   ```csharp
   Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
   ```

`JsonNamingPolicy.KebabCaseLower` is available in .NET 8+ (project targets net8.0-windows). It converts enum names like `StrongAxisBias` to `strong-axis-bias`, `ClosestInDirection` to `closest-in-direction`, `NoOp` to `no-op`, etc. — exactly matching the CLI format.

Note: `PropertyNameCaseInsensitive = true` remains in Load() so property names like "strategy", "Strategy", "STRATEGY" all work. The KebabCaseLower policy only affects enum VALUE serialization/deserialization.

Do NOT change anything else in this file. The enum definitions, property names, defaults, and error handling all stay the same.
  </action>
  <verify>
    Build the project: `cd C:/Work/windowfocusnavigation/focus && dotnet build --no-restore -c Release 2>&1 | tail -5` — should show "Build succeeded" with 0 errors.
    Then verify the change: `grep -n "KebabCaseLower" C:/Work/windowfocusnavigation/focus/Windows/FocusConfig.cs` — should show 2 matches (lines ~37 and ~53).
    Then verify no CamelCase remnants: `grep -c "JsonNamingPolicy.CamelCase" C:/Work/windowfocusnavigation/focus/Windows/FocusConfig.cs` — should return 0.
  </verify>
  <done>FocusConfig.cs uses KebabCaseLower for both Load() and WriteDefaults(). Project compiles. Config file now accepts and generates kebab-case enum values matching CLI format.</done>
</task>

<task type="auto">
  <name>Task 2: Update SETUP.md to document kebab-case config values</name>
  <files>SETUP.md</files>
  <action>
Update SETUP.md to reflect that config.json now uses the same kebab-case values as the CLI. All changes are in the config section (lines ~189-227):

1. **Default config.json block (lines 191-197):** Change `"noOp"` to `"no-op"`:
   ```json
   {
     "strategy": "balanced",
     "wrap": "no-op",
     "exclude": []
   }
   ```
   Note: `"balanced"` stays the same — it has no multi-word casing difference.

2. **Config fields table (line 203):** Change strategy values from camelCase to kebab-case:
   - Old: `balanced`, `strongAxisBias`, `closestInDirection`, `edgeMatching`, `edgeProximity`, `axisOnly`
   - New: `balanced`, `strong-axis-bias`, `closest-in-direction`, `edge-matching`, `edge-proximity`, `axis-only`

3. **Config fields table (line 204):** Change wrap values from camelCase to kebab-case:
   - Old: `noOp`, `wrap`, `beep`
   - New: `no-op`, `wrap`, `beep`

4. **camelCase/kebab-case note (line 207):** DELETE this entire line. The note said "JSON field values use camelCase... CLI flags use kebab-case..." — this distinction no longer exists. Both now use kebab-case.

5. **Wrap behavior list (lines 211-213):** Change `noOp` to `no-op`:
   - `no-op` — do nothing when no candidate exists in that direction (default)
   - `wrap` — cycle to the window at the opposite edge of the screen
   - `beep` — play the system beep sound when no candidate is found

6. **Exclude example config block (lines 220-225):** Change `"noOp"` to `"no-op"`:
   ```json
   {
     "strategy": "balanced",
     "wrap": "no-op",
     "exclude": ["explorer", "Teams", "Slack*"]
   }
   ```

Do NOT change anything outside the config section. CLI reference, debug commands, AHK examples, etc. all stay the same.
  </action>
  <verify>
    Verify no camelCase enum values remain in config section: `grep -n "noOp\|strongAxisBias\|closestInDirection\|edgeMatching\|edgeProximity\|axisOnly" C:/Work/windowfocusnavigation/SETUP.md` — should return 0 matches.
    Verify kebab-case values present: `grep -c "no-op\|strong-axis-bias\|closest-in-direction\|edge-matching\|edge-proximity\|axis-only" C:/Work/windowfocusnavigation/SETUP.md` — should return at least 5 matches.
    Verify the old "Note: JSON field values use camelCase" line is removed: `grep -c "camelCase" C:/Work/windowfocusnavigation/SETUP.md` — should return 0.
  </verify>
  <done>SETUP.md documents kebab-case for all config values. No camelCase enum values remain in the config documentation. The camelCase/kebab-case mismatch note is removed.</done>
</task>

</tasks>

<verification>
1. `dotnet build` succeeds with 0 errors in C:/Work/windowfocusnavigation/focus
2. `grep -c "KebabCaseLower" focus/Windows/FocusConfig.cs` returns 2
3. `grep -c "JsonNamingPolicy.CamelCase" focus/Windows/FocusConfig.cs` returns 0
4. `grep -c "noOp\|strongAxisBias\|closestInDirection\|edgeMatching\|edgeProximity\|axisOnly" SETUP.md` returns 0
5. `grep -c "camelCase" SETUP.md` returns 0
</verification>

<success_criteria>
- Config file accepts kebab-case values: `"strategy": "strong-axis-bias"` works in config.json
- `--init-config` generates config with `"wrap": "no-op"` (not `"noOp"`)
- CLI and config use identical value formats — no naming mismatch
- SETUP.md reflects new format with no camelCase remnants in config docs
- Project builds successfully
</success_criteria>

<output>
After completion, create `.planning/quick/4-make-config-file-accept-same-dash-separa/4-SUMMARY.md`
</output>
