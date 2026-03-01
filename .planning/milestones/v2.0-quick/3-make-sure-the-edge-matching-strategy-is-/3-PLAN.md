---
phase: quick-3
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Program.cs
  - SETUP.md
autonomous: true
requirements: [QUICK-3]

must_haves:
  truths:
    - "Running focus --help shows edge-matching as a valid strategy value"
    - "SETUP.md config fields table lists edgeMatching as a valid strategy value"
    - "SETUP.md CLI reference table lists edge-matching as a valid --strategy value"
  artifacts:
    - path: "focus/Program.cs"
      provides: "--strategy option description with edge-matching"
      contains: "edge-matching"
    - path: "SETUP.md"
      provides: "Documentation of edge-matching strategy in both config and CLI sections"
      contains: "edgeMatching"
  key_links: []
---

<objective>
Add edge-matching to the --strategy option help text and all SETUP.md strategy value lists.

Purpose: The edge-matching strategy was added in quick-02 but its CLI help description and SETUP.md documentation were not updated to include it. Users running `focus --help` or reading the setup guide would not know the strategy exists.

Output: Updated Program.cs --strategy description and SETUP.md strategy value lists.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Program.cs
@SETUP.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add edge-matching to --strategy help text in Program.cs</name>
  <files>focus/Program.cs</files>
  <action>
On line 22 of Program.cs, update the Description string for the --strategy option from:

  "Scoring strategy: balanced | strong-axis-bias | closest-in-direction"

to:

  "Scoring strategy: balanced | strong-axis-bias | closest-in-direction | edge-matching"

This is the only change needed in Program.cs. The error message on line 93 already includes edge-matching.
  </action>
  <verify>grep "edge-matching" focus/Program.cs should match both line 22 (description) and line 88 (switch case) and line 93 (error message)</verify>
  <done>The --strategy option description includes edge-matching alongside the other three strategies.</done>
</task>

<task type="auto">
  <name>Task 2: Add edge-matching to all strategy value lists in SETUP.md</name>
  <files>SETUP.md</files>
  <action>
Update three locations in SETUP.md:

1. Line 203 (Config fields table) — change the Values column for the strategy row from:
   `balanced`, `strongAxisBias`, `closestInDirection`
   to:
   `balanced`, `strongAxisBias`, `closestInDirection`, `edgeMatching`

2. Line 207 (camelCase/kebab-case note) — update to include the edge-matching mapping. Change to:
   "Note: JSON field values use camelCase (e.g., `strongAxisBias`, `closestInDirection`, `edgeMatching`, `noOp`). CLI flags use kebab-case (e.g., `--strategy strong-axis-bias`, `--strategy edge-matching`)."

3. Line 266 (CLI reference table) — change the Values column for --strategy from:
   `balanced`, `strong-axis-bias`, `closest-in-direction`
   to:
   `balanced`, `strong-axis-bias`, `closest-in-direction`, `edge-matching`

Do NOT change any other content. These are surgical additions of the missing value to existing lists.
  </action>
  <verify>grep -c "edgeMatching\|edge-matching" SETUP.md should return at least 3 (the three locations updated)</verify>
  <done>SETUP.md documents edge-matching in the config fields table, the camelCase note, and the CLI reference table.</done>
</task>

</tasks>

<verification>
- `dotnet build focus/focus.csproj` compiles without errors (no code logic changed, only a string literal)
- `grep -n "edge-matching" focus/Program.cs` shows the strategy in the --strategy Description
- `grep -n "edgeMatching\|edge-matching" SETUP.md` shows all three updated locations
</verification>

<success_criteria>
All user-facing strategy value lists (--help description, config docs, CLI reference) include edge-matching / edgeMatching. No documentation or help output omits the fourth strategy.
</success_criteria>

<output>
After completion, create `.planning/quick/3-make-sure-the-edge-matching-strategy-is-/3-SUMMARY.md`
</output>
