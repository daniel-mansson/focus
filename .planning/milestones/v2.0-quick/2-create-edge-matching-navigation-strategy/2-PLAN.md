---
phase: quick
plan: 2
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/FocusConfig.cs
  - focus/Windows/NavigationService.cs
  - focus/Program.cs
autonomous: true
requirements: [QUICK-02]

must_haves:
  truths:
    - "focus left --strategy edge-matching activates the window whose right edge is the closest right edge to the left of the current window's right edge"
    - "focus right --strategy edge-matching activates the window whose left edge is the closest left edge to the right of the current window's left edge"
    - "focus up --strategy edge-matching activates the window whose bottom edge is the closest bottom edge above the current window's bottom edge"
    - "focus down --strategy edge-matching activates the window whose top edge is the closest top edge below the current window's top edge"
    - "focus --debug score <dir> shows EdgeMatching column alongside existing strategies"
    - "focus --debug config shows edge-matching when configured"
  artifacts:
    - path: "focus/Windows/FocusConfig.cs"
      provides: "Strategy.EdgeMatching enum value"
      contains: "EdgeMatching"
    - path: "focus/Windows/NavigationService.cs"
      provides: "ScoreEdgeMatching scoring function"
      exports: ["ScoreEdgeMatching"]
    - path: "focus/Program.cs"
      provides: "CLI parsing and debug table for edge-matching"
      contains: "edge-matching"
  key_links:
    - from: "focus/Program.cs"
      to: "Strategy.EdgeMatching"
      via: "strategy switch parsing"
      pattern: "edge-matching.*EdgeMatching"
    - from: "focus/Windows/NavigationService.cs"
      to: "ScoreEdgeMatching"
      via: "strategy switch in GetRankedCandidates"
      pattern: "Strategy\\.EdgeMatching.*ScoreEdgeMatching"
---

<objective>
Add a new "edge-matching" navigation strategy that uses same-edge comparison for directional navigation.

Purpose: Provide an alternative strategy that ignores the perpendicular axis entirely and uses pure edge-to-edge distance on the movement axis. For a left move, compare right edges; for right, left edges; for up, bottom edges; for down, top edges. The origin is the relevant edge of the foreground window, and candidates are ranked by how close their same-type edge is in the movement direction.

Output: Strategy.EdgeMatching enum value, ScoreEdgeMatching scoring function, CLI integration with --strategy edge-matching, and debug score table column.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Windows/NavigationService.cs
@focus/Windows/FocusConfig.cs
@focus/Program.cs
@focus/Windows/WindowInfo.cs

<interfaces>
<!-- Key types and contracts the executor needs -->

From focus/Windows/FocusConfig.cs:
```csharp
internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection }
internal enum WrapBehavior { NoOp, Wrap, Beep }
```

From focus/Windows/NavigationService.cs:
```csharp
// Scoring function signature used by all strategies:
// Func<double, double, WindowInfo, Direction, double> scoreFn
// Returns double.MaxValue to eliminate, lower = better candidate
// Origin point (originX, originY) is center of foreground window

// Strategy switch at line 64:
Func<double, double, WindowInfo, Direction, double> scoreFn = strategy switch
{
    Strategy.Balanced           => ScoreCandidate,
    Strategy.StrongAxisBias     => ScoreStrongAxisBias,
    Strategy.ClosestInDirection => ScoreClosestInDirection,
    _                           => ScoreCandidate
};
```

From focus/Windows/WindowInfo.cs:
```csharp
internal record WindowInfo(
    nint Hwnd, string ProcessName, string Title,
    int Left, int Top, int Right, int Bottom,
    int MonitorIndex, bool IsTopmost, bool IsUwpFrame);
```

From focus/Program.cs (strategy CLI parsing, line 83):
```csharp
var parsed = strategyValue.ToLowerInvariant() switch
{
    "balanced"              => (Strategy?)Strategy.Balanced,
    "strong-axis-bias"      => Strategy.StrongAxisBias,
    "closest-in-direction"  => Strategy.ClosestInDirection,
    _                       => null
};
```

From focus/Program.cs (debug score, lines 182-184):
```csharp
var balanced   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.Balanced);
var strongBias = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.StrongAxisBias);
var closestDir = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.ClosestInDirection);
PrintScoreTable(balanced, strongBias, closestDir, scoreDirection.Value, config.Strategy);
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add EdgeMatching strategy enum and scoring function</name>
  <files>focus/Windows/FocusConfig.cs, focus/Windows/NavigationService.cs</files>
  <action>
1. In FocusConfig.cs, add `EdgeMatching` to the Strategy enum after `ClosestInDirection`:
   ```csharp
   internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching }
   ```

2. In NavigationService.cs, add a new case to the strategy switch (line 64-70):
   ```csharp
   Strategy.EdgeMatching => ScoreEdgeMatching,
   ```

3. In NavigationService.cs, add the `ScoreEdgeMatching` method. This strategy does NOT use the origin center point (originX, originY) passed into the scoring function. Instead, it needs the foreground window's bounds. Since the scoring function signature is `Func<double, double, WindowInfo, Direction, double>` and cannot be changed, the method must retrieve the foreground window bounds itself via `PInvoke.GetForegroundWindow()` + `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`. Cache the foreground bounds in a static field to avoid re-querying for every candidate.

   Algorithm for ScoreEdgeMatching:
   - Get foreground window bounds (Left, Top, Right, Bottom). Use a static cache: `private static (nint hwnd, int left, int top, int right, int bottom) _fgBoundsCache` — populate on first call per navigation cycle (check if cached hwnd matches current foreground).
   - For each direction, the "reference edge" is the edge of the foreground window, and we look for the candidate's same-type edge strictly beyond it:
     - **Left**: reference = foreground.Right. Score = foreground.Right - candidate.Right. Only include if candidate.Right < foreground.Right (strictly to the left).
     - **Right**: reference = foreground.Left. Score = candidate.Left - foreground.Left. Only include if candidate.Left > foreground.Left (strictly to the right).
     - **Up**: reference = foreground.Bottom. Score = foreground.Bottom - candidate.Bottom. Only include if candidate.Bottom < foreground.Bottom (strictly above).
     - **Down**: reference = foreground.Top. Score = candidate.Top - foreground.Top. Only include if candidate.Top > foreground.Top (strictly below).
   - Return double.MaxValue if candidate's edge is not strictly in the requested direction.
   - Return the absolute edge distance as the score (lower = closer = better).
   - The perpendicular axis is completely ignored — this is a pure 1D strategy.
   - If foreground bounds retrieval fails, fall back to using originX/originY center-based nearest-edge logic (same as Balanced) as a graceful degradation.

   Add XML doc summary explaining the edge-matching concept.
  </action>
  <verify>
    cd C:/Work/windowfocusnavigation/focus && dotnet build --no-restore 2>&1 | tail -5
  </verify>
  <done>Strategy.EdgeMatching enum exists. ScoreEdgeMatching method compiles and is wired into the strategy switch. Build succeeds with no errors.</done>
</task>

<task type="auto">
  <name>Task 2: Wire CLI parsing and debug score table</name>
  <files>focus/Program.cs</files>
  <action>
1. In Program.cs, add `"edge-matching"` to the strategy CLI parser switch (around line 83-88):
   ```csharp
   "edge-matching"         => Strategy.EdgeMatching,
   ```

2. Update the error message on unknown strategy (line 91) to include edge-matching:
   ```csharp
   Console.Error.WriteLine($"Error: Unknown strategy '{strategyValue}'. Use: balanced, strong-axis-bias, closest-in-direction, edge-matching");
   ```

3. In the `--debug score` section (around line 182), add a fourth strategy query:
   ```csharp
   var edgeMatch = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.EdgeMatching);
   ```

4. Update the `PrintScoreTable` method signature to accept the fourth list:
   ```csharp
   static void PrintScoreTable(
       List<(WindowInfo Window, double Score)> balanced,
       List<(WindowInfo Window, double Score)> strongBias,
       List<(WindowInfo Window, double Score)> closestDir,
       List<(WindowInfo Window, double Score)> edgeMatch,
       Direction direction,
       Strategy activeStrategy)
   ```

5. Update the call site to pass `edgeMatch`:
   ```csharp
   PrintScoreTable(balanced, strongBias, closestDir, edgeMatch, scoreDirection.Value, config.Strategy);
   ```

6. Inside PrintScoreTable, integrate the fourth column:
   - Add edge-matching entries to the `allHwnds` union set.
   - Create `edgeMatchScores` dictionary (same pattern as the other three).
   - Add `edgeMatchHeader` with the `*` marker when active: `"EDGE-MATCH" + (activeStrategy == Strategy.EdgeMatching ? "*" : " ")`.
   - Add the fourth column header and score data to the format strings. Maintain the same `scoreWidth = 12` for the new column.
   - Add the fourth score output in the data row loop.

7. Clear the foreground bounds cache (if using a static cache) — this is handled naturally because each `GetRankedCandidates` call starts a fresh foreground window lookup. But ensure the static cache field in NavigationService is reset at the start of `GetRankedCandidates` so consecutive calls with different foreground windows (unlikely but safe) work correctly. Add `_fgBoundsCache = default;` at the top of GetRankedCandidates before the loop.
  </action>
  <verify>
    cd C:/Work/windowfocusnavigation/focus && dotnet build --no-restore 2>&1 | tail -5 && dotnet run -- --debug score left 2>&1 | head -10
  </verify>
  <done>
    - `focus --strategy edge-matching left` is accepted (no "unknown strategy" error).
    - `focus --debug score left` shows four strategy columns including EDGE-MATCH.
    - `focus --debug config` with edge-matching configured shows "EdgeMatching" in strategy line.
    - Build succeeds with no warnings.
  </done>
</task>

</tasks>

<verification>
1. `cd C:/Work/windowfocusnavigation/focus && dotnet build` succeeds with zero errors and zero warnings.
2. `dotnet run -- --strategy edge-matching --debug config` shows `strategy: EdgeMatching`.
3. `dotnet run -- --debug score left` shows four columns: BALANCED, STRONG-AXIS, CLOSEST, EDGE-MATCH.
4. `dotnet run -- --strategy edge-matching left` runs without error (activates a window or returns exit code 1 if no candidates).
</verification>

<success_criteria>
- Strategy.EdgeMatching exists in the enum and is fully wired through the strategy switch in NavigationService, CLI parsing in Program.cs, and debug score output.
- The scoring algorithm uses same-edge comparison: left compares right edges, right compares left edges, up compares bottom edges, down compares top edges. Perpendicular axis is completely ignored.
- All existing strategies continue to work unchanged (backward compatible).
- Build produces zero errors and zero warnings.
</success_criteria>

<output>
After completion, create `.planning/quick/2-create-edge-matching-navigation-strategy/2-SUMMARY.md`
</output>
