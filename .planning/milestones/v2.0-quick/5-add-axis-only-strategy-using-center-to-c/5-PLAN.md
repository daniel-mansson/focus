---
phase: quick
plan: 5
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/FocusConfig.cs
  - focus/Windows/NavigationService.cs
  - focus/Program.cs
  - SETUP.md
autonomous: true
requirements: [QUICK-05]
must_haves:
  truths:
    - "focus left --strategy axis-only picks the candidate whose center X is closest but still left of the origin center X"
    - "focus right --strategy axis-only picks the candidate whose center X is closest but still right of the origin center X"
    - "focus up --strategy axis-only picks the candidate whose center Y is closest but still above the origin center Y"
    - "focus down --strategy axis-only picks the candidate whose center Y is closest but still below the origin center Y"
    - "Perpendicular axis is completely ignored — Y is irrelevant for left/right, X is irrelevant for up/down"
    - "focus --debug score <dir> shows six strategy columns including AXIS-ONLY"
  artifacts:
    - path: "focus/Windows/FocusConfig.cs"
      provides: "AxisOnly enum member"
      contains: "AxisOnly"
    - path: "focus/Windows/NavigationService.cs"
      provides: "ScoreAxisOnly scoring function"
      exports: ["ScoreAxisOnly"]
    - path: "focus/Program.cs"
      provides: "CLI parsing for axis-only, six-column debug table"
      contains: "axis-only"
    - path: "SETUP.md"
      provides: "axis-only strategy documentation"
      contains: "axis-only"
  key_links:
    - from: "focus/Windows/NavigationService.cs"
      to: "Strategy.AxisOnly"
      via: "switch expression in GetRankedCandidates"
      pattern: "Strategy\\.AxisOnly\\s*=>\\s*ScoreAxisOnly"
    - from: "focus/Program.cs"
      to: "Strategy.AxisOnly"
      via: "CLI parsing switch"
      pattern: "\"axis-only\".*Strategy\\.AxisOnly"
---

<objective>
Add an "axis-only" navigation strategy that uses strictly center-to-center 1D distance along the movement axis, completely ignoring the perpendicular axis.

Purpose: Provide the simplest possible directional strategy — pure 1D center-to-center distance. For LEFT: pick the candidate whose center X is closest but still to the left. Y position is completely ignored. No secondary axis scoring at all.
Output: AxisOnly enum member, ScoreAxisOnly function, CLI wiring, six-column debug table, SETUP.md documentation.
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
<!-- Key types and contracts the executor needs. -->

From focus/Windows/FocusConfig.cs:
```csharp
internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching, EdgeProximity }
// AxisOnly will be appended here as the sixth member
```

From focus/Windows/NavigationService.cs:
```csharp
// Scoring function signature (all strategies follow this pattern):
Func<double, double, WindowInfo, Direction, double> scoreFn
// Parameters: originX, originY, candidate, direction
// Returns: double score (lower = better), double.MaxValue = eliminated

// Strategy dispatch switch in GetRankedCandidates:
Func<double, double, WindowInfo, Direction, double> scoreFn = strategy switch
{
    Strategy.Balanced           => ScoreCandidate,
    Strategy.StrongAxisBias     => ScoreStrongAxisBias,
    Strategy.ClosestInDirection => ScoreClosestInDirection,
    Strategy.EdgeMatching       => ScoreEdgeMatching,
    Strategy.EdgeProximity      => ScoreEdgeProximity,
    _                           => ScoreCandidate
};
```

From focus/Program.cs:
```csharp
// CLI parsing switch for --strategy:
var parsed = strategyValue.ToLowerInvariant() switch
{
    "balanced"              => (Strategy?)Strategy.Balanced,
    "strong-axis-bias"      => Strategy.StrongAxisBias,
    "closest-in-direction"  => Strategy.ClosestInDirection,
    "edge-matching"         => Strategy.EdgeMatching,
    "edge-proximity"        => Strategy.EdgeProximity,
    _                       => null
};

// PrintScoreTable currently takes five strategy lists (balanced, strongBias, closestDir, edgeMatch, edgeProx)
// and shows five score columns. Must extend to six.
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add AxisOnly enum member and ScoreAxisOnly scoring function</name>
  <files>focus/Windows/FocusConfig.cs, focus/Windows/NavigationService.cs</files>
  <action>
1. In `focus/Windows/FocusConfig.cs`, add `AxisOnly` to the Strategy enum after EdgeProximity:
   ```csharp
   internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching, EdgeProximity, AxisOnly }
   ```

2. In `focus/Windows/NavigationService.cs`, add `Strategy.AxisOnly => ScoreAxisOnly` to the strategy switch in GetRankedCandidates (line ~77, before the `_` default case):
   ```csharp
   Strategy.AxisOnly           => ScoreAxisOnly,
   ```

3. In `focus/Windows/NavigationService.cs`, add the `ScoreAxisOnly` method after `ScoreEdgeProximity`. This is a pure center-to-center 1D strategy — NO foreground bounds cache needed (unlike EdgeMatching/EdgeProximity). It uses the origin point (already the foreground center) directly:

   ```csharp
   /// <summary>
   /// Scores a candidate window using pure center-to-center 1D distance along the movement axis.
   /// Ignores the perpendicular axis entirely — Y is irrelevant for left/right, X is irrelevant for up/down.
   ///
   /// For LEFT:  score = originX - candidateCenterX  (candidate center must be strictly left of origin center)
   /// For RIGHT: score = candidateCenterX - originX  (candidate center must be strictly right of origin center)
   /// For UP:    score = originY - candidateCenterY  (candidate center must be strictly above origin center)
   /// For DOWN:  score = candidateCenterY - originY  (candidate center must be strictly below origin center)
   ///
   /// No secondary axis, no edge comparison, no Euclidean distance — pure 1D.
   /// </summary>
   internal static double ScoreAxisOnly(
       double originX, double originY,
       WindowInfo candidate,
       Direction direction)
   {
       double candCx = (candidate.Left + candidate.Right) / 2.0;
       double candCy = (candidate.Top + candidate.Bottom) / 2.0;

       return direction switch
       {
           Direction.Left  => candCx < originX ? originX - candCx : double.MaxValue,
           Direction.Right => candCx > originX ? candCx - originX : double.MaxValue,
           Direction.Up    => candCy < originY ? originY - candCy : double.MaxValue,
           Direction.Down  => candCy > originY ? candCy - originY : double.MaxValue,
           _               => double.MaxValue
       };
   }
   ```

   Key design notes:
   - Uses strict inequality (< not <=) consistent with all other strategies
   - Uses origin point directly (which is already the foreground window center from GetOriginPoint) — no need for _fgBoundsCache
   - Score is simply the 1D distance along the movement axis — lower = closer = better
   - Perpendicular axis is completely absent from the calculation

4. Build to verify: `dotnet build -c Release` from the focus/ directory.
  </action>
  <verify>
    <automated>cd C:/Work/windowfocusnavigation/focus && dotnet build -c Release 2>&1 | tail -5</automated>
  </verify>
  <done>Strategy enum has six members including AxisOnly, ScoreAxisOnly method exists in NavigationService, strategy switch dispatches to it, project compiles with zero errors.</done>
</task>

<task type="auto">
  <name>Task 2: Wire CLI parsing, extend debug score table to six columns, and update SETUP.md</name>
  <files>focus/Program.cs, SETUP.md</files>
  <action>
1. In `focus/Program.cs`, update the `--strategy` option description (line ~23) to include `axis-only`:
   ```csharp
   Description = "Scoring strategy: balanced | strong-axis-bias | closest-in-direction | edge-matching | edge-proximity | axis-only"
   ```

2. Add `"axis-only"` case to the CLI parsing switch (around line 89, before the `_ => null`):
   ```csharp
   "axis-only"             => Strategy.AxisOnly,
   ```

3. Update the error message for unknown strategy (around line 94) to include `axis-only`:
   ```csharp
   Console.Error.WriteLine($"Error: Unknown strategy '{strategyValue}'. Use: balanced, strong-axis-bias, closest-in-direction, edge-matching, edge-proximity, axis-only");
   ```

4. In the `--debug score` block, add a sixth strategy call after edgeProx (around line 188):
   ```csharp
   var axisOnly   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.AxisOnly);
   ```
   Update the PrintScoreTable call to pass six lists:
   ```csharp
   PrintScoreTable(balanced, strongBias, closestDir, edgeMatch, edgeProx, axisOnly, scoreDirection.Value, config.Strategy);
   ```

5. Update `PrintScoreTable` signature to accept six lists — add parameter `List<(WindowInfo Window, double Score)> axisOnly` after `edgeProx`. Update the method body:
   - Add `foreach (var (w, _) in axisOnly) allHwnds.TryAdd(w.Hwnd, w);` to the union set
   - Add `var axisOnlyScores = axisOnly.ToDictionary(x => x.Window.Hwnd, x => x.Score);` to score lookups
   - Add header: `string axisOnlyHeader = "AXIS-ONLY" + (activeStrategy == Strategy.AxisOnly ? "*" : " ");`
   - Add the sixth column to the header Console.WriteLine (append `{axisOnlyHeader,scoreWidth}`)
   - Add sixth separator `{new string('-', scoreWidth)}`
   - Add score lookup: `string axisOnlyScore = axisOnlyScores.TryGetValue(hwnd, out var aos) ? aos.ToString("F1") : "-";`
   - Add sixth column to each data row: `{axisOnlyScore,scoreWidth}`

6. In `SETUP.md`:
   - Update the config table row for `strategy` to include `axisOnly` in the Values column
   - Update the camelCase note to include `axisOnly`
   - Update the CLI reference `--strategy` row to include `axis-only`
   - Update `--debug score` description to say "all six strategies"
   - Add an **axis-only** section after the edge-proximity description in the Scoring Strategies section:
     ```
     **axis-only**

     Uses pure center-to-center 1D distance along the movement axis. For a leftward move, compares the source center X to each candidate's center X and picks the one closest to the left. The perpendicular axis (Y for left/right, X for up/down) is completely ignored — no secondary weighting, no alignment scoring, just raw 1D distance.

     This is the simplest strategy: whichever window's center is nearest along the movement axis wins. It does not consider window edges, sizes, or perpendicular offset.

     Use this when: you want the most predictable, geometry-minimal navigation — the window whose center is closest along the movement axis always wins, regardless of vertical or horizontal offset.
     ```
   - Update "Comparing strategies" text to say "all six strategies"

7. Build to verify: `dotnet build -c Release` from the focus/ directory.
  </action>
  <verify>
    <automated>cd C:/Work/windowfocusnavigation/focus && dotnet build -c Release 2>&1 | tail -5</automated>
  </verify>
  <done>CLI accepts `--strategy axis-only`, error message lists all six strategies, `--debug score` shows six columns including AXIS-ONLY with active-strategy marker, SETUP.md documents axis-only in config table, CLI reference, and strategy descriptions.</done>
</task>

</tasks>

<verification>
- `dotnet build -c Release` succeeds with zero errors and zero warnings
- `focus --help` shows axis-only in --strategy description
- `focus --debug config` works (no crash from enum change)
- `focus --debug score left` shows six-column table with AXIS-ONLY column
</verification>

<success_criteria>
- AxisOnly is the sixth member of the Strategy enum
- ScoreAxisOnly uses center-to-center 1D distance with no perpendicular axis component
- `--strategy axis-only` CLI flag works end-to-end (navigation and debug)
- Debug score table has six columns (BALANCED, STRONG-AXIS, CLOSEST, EDGE-MATCH, EDGE-PROX, AXIS-ONLY)
- SETUP.md documents the new strategy in all relevant sections
- Project compiles cleanly
</success_criteria>

<output>
After completion, create `.planning/quick/5-add-axis-only-strategy-using-center-to-c/5-SUMMARY.md`
</output>
