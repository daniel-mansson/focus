---
phase: quick
plan: 4
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/FocusConfig.cs
  - focus/Windows/NavigationService.cs
  - focus/Program.cs
  - SETUP.md
autonomous: true
requirements: [QUICK-04]
must_haves:
  truths:
    - "User can pass --strategy edge-proximity on CLI and navigation uses near-edge scoring"
    - "User can set strategy to edgeProximity in config.json and it is honored"
    - "debug score table shows EDGE-PROX column alongside existing four columns"
    - "Edge-proximity selects the candidate whose near-facing edge is closest to the source near-facing edge"
  artifacts:
    - path: "focus/Windows/FocusConfig.cs"
      provides: "EdgeProximity enum member in Strategy"
      contains: "EdgeProximity"
    - path: "focus/Windows/NavigationService.cs"
      provides: "ScoreEdgeProximity scoring function"
      contains: "ScoreEdgeProximity"
    - path: "focus/Program.cs"
      provides: "CLI parsing for edge-proximity and five-column score table"
      contains: "edge-proximity"
    - path: "SETUP.md"
      provides: "Documentation of edge-proximity strategy"
      contains: "edge-proximity"
  key_links:
    - from: "focus/Program.cs"
      to: "focus/Windows/FocusConfig.cs"
      via: "Strategy.EdgeProximity enum value"
      pattern: "Strategy\\.EdgeProximity"
    - from: "focus/Windows/NavigationService.cs"
      to: "ScoreEdgeProximity"
      via: "strategy switch dispatch"
      pattern: "Strategy\\.EdgeProximity\\s*=>\\s*ScoreEdgeProximity"
---

<objective>
Add an "edge-proximity" navigation strategy that uses near-edge-to-near-edge comparison. Unlike
the existing "edge-matching" strategy (which uses the far edge of the source window), this strategy
uses the NEAR edge. For a LEFT move: source.Left vs candidate.Left — closest candidate whose left
edge is still to the left of source's left edge wins. Same pattern for right/up/down.

Purpose: Gives users a fifth strategy that feels natural when windows are stacked near each other —
"which window's near edge is closest to my near edge in that direction."

Output: Strategy.EdgeProximity enum, ScoreEdgeProximity function, CLI wiring, debug score column,
documentation in SETUP.md.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Windows/FocusConfig.cs
@focus/Windows/NavigationService.cs
@focus/Program.cs
@SETUP.md

<interfaces>
<!-- Existing patterns the executor must follow exactly -->

From focus/Windows/FocusConfig.cs:
```csharp
internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching }
```

From focus/Windows/NavigationService.cs — scoring function signature (all four follow this):
```csharp
Func<double, double, WindowInfo, Direction, double> scoreFn = strategy switch { ... };
```

ScoreEdgeMatching reuses `_fgBoundsCache` static field — new strategy must do the same:
```csharp
private static (nint Hwnd, int Left, int Top, int Right, int Bottom) _fgBoundsCache;
```

From focus/Program.cs — strategy CLI parsing pattern:
```csharp
"edge-matching" => Strategy.EdgeMatching,
```

PrintScoreTable accepts four strategy result lists — must be extended to five.
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add EdgeProximity enum and scoring function</name>
  <files>focus/Windows/FocusConfig.cs, focus/Windows/NavigationService.cs</files>
  <action>
1. In FocusConfig.cs, add `EdgeProximity` to the Strategy enum after `EdgeMatching`:
   ```csharp
   internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching, EdgeProximity }
   ```

2. In NavigationService.cs, add `ScoreEdgeProximity` — a new internal static unsafe method with the
   standard scoring signature `(double originX, double originY, WindowInfo candidate, Direction direction) -> double`.

   Algorithm — near-edge-to-near-edge comparison:
   - Reuse `_fgBoundsCache` exactly as ScoreEdgeMatching does (same cache-populate pattern, same fallback to ScoreCandidate on failure).
   - Direction mapping (NEAR edge = the edge facing the direction of movement):
     - **Left**: foreground.Left vs candidate.Left — include if candidate.Left < foreground.Left. Score = foreground.Left - candidate.Left.
     - **Right**: foreground.Right vs candidate.Right — include if candidate.Right > foreground.Right. Score = candidate.Right - foreground.Right.
     - **Up**: foreground.Top vs candidate.Top — include if candidate.Top < foreground.Top. Score = foreground.Top - candidate.Top.
     - **Down**: foreground.Bottom vs candidate.Bottom — include if candidate.Bottom > foreground.Bottom. Score = candidate.Bottom - foreground.Bottom.
   - Use strict inequality (< not <=) consistent with all other strategies.
   - Return double.MaxValue for candidates not in direction.
   - Perpendicular axis is completely ignored (1D strategy, same as EdgeMatching).

   Add XML doc comment explaining the near-edge concept and how it differs from EdgeMatching (far-edge).

3. In NavigationService.cs, add the `Strategy.EdgeProximity => ScoreEdgeProximity` case to the strategy switch expression inside GetRankedCandidates.
  </action>
  <verify>cd focus && dotnet build --no-restore 2>&amp;1 | tail -5 — expect "0 Warning(s)" and "0 Error(s)"</verify>
  <done>Strategy enum has EdgeProximity, ScoreEdgeProximity method exists with near-edge logic, strategy switch dispatches to it, zero warnings zero errors.</done>
</task>

<task type="auto">
  <name>Task 2: Wire CLI parsing and extend debug score table to five columns</name>
  <files>focus/Program.cs, SETUP.md</files>
  <action>
1. In Program.cs, add CLI strategy parsing case:
   ```csharp
   "edge-proximity" => Strategy.EdgeProximity,
   ```
   in the `strategyValue.ToLowerInvariant() switch` block, after the `edge-matching` case.

2. Update the error message for unknown strategy to include `edge-proximity`:
   ```
   "Error: Unknown strategy '{strategyValue}'. Use: balanced, strong-axis-bias, closest-in-direction, edge-matching, edge-proximity"
   ```

3. Update `--strategy` option description to include `edge-proximity`:
   ```
   Description = "Scoring strategy: balanced | strong-axis-bias | closest-in-direction | edge-matching | edge-proximity"
   ```

4. In the `--debug score` block, add a fifth strategy call:
   ```csharp
   var edgeProx = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.EdgeProximity);
   ```
   Pass it as a fifth parameter to PrintScoreTable.

5. Update `PrintScoreTable` signature to accept a fifth list parameter `edgeProx`:
   ```csharp
   static void PrintScoreTable(
       List<(WindowInfo Window, double Score)> balanced,
       List<(WindowInfo Window, double Score)> strongBias,
       List<(WindowInfo Window, double Score)> closestDir,
       List<(WindowInfo Window, double Score)> edgeMatch,
       List<(WindowInfo Window, double Score)> edgeProx,
       Direction direction,
       Strategy activeStrategy)
   ```
   - Add `foreach (var (w, _) in edgeProx) allHwnds.TryAdd(w.Hwnd, w);` to the union set builder.
   - Add `var edgeProxScores = edgeProx.ToDictionary(x => x.Window.Hwnd, x => x.Score);` to score lookups.
   - Add header column `"EDGE-PROX"` with active marker for `Strategy.EdgeProximity`.
   - Add score column in the data row output.
   - Update footer summary line format to accommodate fifth column.

6. In SETUP.md:
   - Add `edge-proximity` to the config table strategy values row (alongside existing values).
   - Add `edgeProximity` to the camelCase JSON values note.
   - Add `edge-proximity` to the CLI reference --strategy values.
   - Add a new strategy description section after edge-matching:

     **edge-proximity**

     Uses the near edge of both source and candidate — the edge facing the direction of movement.
     For a rightward move, compares source's right edge to each candidate's right edge; the candidate
     whose right edge extends least beyond the source's right edge wins. Pure 1D comparison, ignoring
     the perpendicular axis entirely.

     This differs from edge-matching, which uses the far edge of the source (for a leftward move,
     edge-matching compares source's right edge to candidate's right edge; edge-proximity compares
     source's left edge to candidate's left edge).

     Use this when: you want navigation that feels like "which window is closest to where I am, on this
     side" rather than "which window is closest to the far side of my window."

   - Update the `--debug score` description note to say "all five strategies" instead of "all three" or "all four".
  </action>
  <verify>cd focus && dotnet build --no-restore 2>&amp;1 | tail -5 — expect "0 Warning(s)" and "0 Error(s)", then: dotnet run -- --strategy edge-proximity --debug config 2>&amp;1 | grep -i "strategy" — expect "strategy: EdgeProximity"</verify>
  <done>CLI accepts --strategy edge-proximity, debug score table shows five columns with EDGE-PROX, SETUP.md documents the new strategy in all relevant sections, config.json accepts edgeProximity.</done>
</task>

</tasks>

<verification>
1. `cd focus && dotnet build --no-restore` — 0 errors, 0 warnings
2. `cd focus && dotnet run -- --strategy edge-proximity --debug config` — shows `strategy: EdgeProximity`
3. `cd focus && dotnet run -- --debug score left` — shows five columns: BALANCED, STRONG-AXIS, CLOSEST, EDGE-MATCH, EDGE-PROX
4. `cd focus && dotnet run -- --strategy edge-proximity left --verbose` — navigates using edge-proximity (verbose output confirms strategy)
</verification>

<success_criteria>
- Strategy.EdgeProximity enum exists and is wired end-to-end (enum -> scoring -> CLI -> config -> debug)
- ScoreEdgeProximity uses near-edge comparison (Left vs Left for Left, Right vs Right for Right, etc.)
- Debug score table has five columns with correct active-strategy marker
- SETUP.md documents edge-proximity in strategy table, CLI reference, and strategy descriptions
- Zero build warnings, zero build errors
</success_criteria>

<output>
After completion, create `.planning/quick/4-add-edge-proximity-strategy-using-near-e/4-SUMMARY.md`
</output>
