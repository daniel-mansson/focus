---
phase: quick-2
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
autonomous: false
requirements:
  - QUICK-2
must_haves:
  truths:
    - "When wrap is enabled and no candidate exists in a direction, the overlay for that direction shows on the wrap target (furthest window in opposite direction)"
    - "When wrap is disabled (NoOp or Beep), no overlay appears for directions with no candidates (existing behavior preserved)"
    - "Overlay correctly updates after a wrap navigation completes (foreground change triggers refresh)"
  artifacts:
    - path: "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs"
      provides: "Wrap-aware overlay positioning in ShowOverlaysForCurrentForeground"
      contains: "WrapBehavior.Wrap"
  key_links:
    - from: "OverlayOrchestrator.ShowOverlaysForCurrentForeground"
      to: "NavigationService.GetRankedCandidates"
      via: "opposite direction scoring for wrap target"
      pattern: "GetRankedCandidates.*opposite"
---

<objective>
Fix overlay outlines not showing for wrap-targeted windows. When wrap is enabled in config and there are no candidates in a given direction (the wrap trigger condition), the overlay for that direction should show on the wrap target window (the furthest window in the opposite direction) instead of being hidden.

Purpose: Overlays should always show the user where pressing a direction key will navigate to. When wrap is the behavior, the target is the furthest window on the opposite edge — the overlay must point there.

Output: Modified OverlayOrchestrator.cs with wrap-aware overlay positioning.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
@focus/Windows/FocusActivator.cs
@focus/Windows/FocusConfig.cs

<interfaces>
<!-- Key types and contracts the executor needs -->

From focus/Windows/FocusConfig.cs:
```csharp
internal enum WrapBehavior { NoOp, Wrap, Beep }

internal class FocusConfig
{
    public WrapBehavior Wrap { get; set; } = WrapBehavior.NoOp;
    public Strategy Strategy { get; set; } = Strategy.Balanced;
    // ...
}
```

From focus/Windows/FocusActivator.cs (wrap logic to mirror):
```csharp
// HandleWrap finds the wrap target by:
// 1. Computing opposite direction (Left->Right, Right->Left, Up->Down, Down->Up)
// 2. Scoring all windows in opposite direction: GetRankedCandidates(allWindows, opposite, strategy)
// 3. Reversing the list so furthest candidate (highest score) is first
// 4. Activating the last/furthest candidate
private static int HandleWrap(List<WindowInfo> allWindows, Direction direction, Strategy strategy, bool verbose)
```

From focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs (the bug site):
```csharp
// ShowOverlaysForCurrentForeground() iterates all 4 directions.
// When ranked.Count == 0 for a direction, it hides that overlay (line 271).
// BUG: It never checks _config.Wrap — should show wrap target when Wrap is enabled.
// The _config field is available and has .Wrap property.
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add wrap-aware overlay targeting in ShowOverlaysForCurrentForeground</name>
  <files>focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs</files>
  <action>
In `ShowOverlaysForCurrentForeground()`, modify the `foreach (Direction direction ...)` loop. Currently when `ranked.Count == 0`, it unconditionally hides the overlay for that direction (line 268-271). Change this to check `_config.Wrap`:

When `ranked.Count == 0` AND `_config.Wrap == WrapBehavior.Wrap`:
1. Compute the opposite direction using the same logic as `FocusActivator.HandleWrap`:
   - Left -> Right, Right -> Left, Up -> Down, Down -> Up
2. Call `NavigationService.GetRankedCandidates(filtered, opposite, _config.Strategy)` to find candidates in the opposite direction
3. If opposite candidates exist, take the LAST candidate (furthest from origin = the wrap target). The list is sorted ascending by score (best/closest first), so the wrap target is `wrapped[wrapped.Count - 1]` — the worst score in the opposite direction, which is the furthest window on the opposite edge.
4. Build the bounds RECT, apply LeftRightInset expansion if direction is Left or Right (same as existing code), clamp to monitor, and show the overlay at that position.
5. Increment `candidatesFound` so the solo-window dim indicator is not triggered.

When `ranked.Count == 0` AND `_config.Wrap != WrapBehavior.Wrap` (NoOp or Beep):
- Keep existing behavior: `_overlayManager.HideOverlay(direction)` and continue.

Extract the opposite-direction lookup into a small static helper method `GetOppositeDirection(Direction direction)` within the class to keep the code clean (same switch expression as FocusActivator).

IMPORTANT: Do NOT change NavigateSta or any other method. Only modify `ShowOverlaysForCurrentForeground` and add the helper method.
  </action>
  <verify>
    <automated>cd C:/Work/windowfocusnavigation &amp;&amp; dotnet build focus/focus.csproj --no-restore 2>&amp;1 | tail -5</automated>
  </verify>
  <done>
    - Build succeeds with no errors
    - When wrap is enabled in config and a direction has no natural candidates, the overlay for that direction appears on the wrap target (furthest window on opposite edge)
    - When wrap is disabled, the overlay for empty directions is hidden (no behavior change)
    - Solo-window dim indicator still works correctly (candidatesFound accounts for wrap targets)
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 2: Verify wrap overlay behavior</name>
  <files>focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs</files>
  <action>Human verifies wrap-aware overlay targeting works correctly in the running daemon.</action>
  <verify>Human confirms overlays appear on wrap targets when wrap is enabled.</verify>
  <done>User approves overlay behavior for both wrap-enabled and wrap-disabled configurations.</done>
</task>

</tasks>

<verification>
- `dotnet build focus/focus.csproj` compiles without errors
- Manual verification: overlay appears on wrap target when wrap is enabled
- Manual verification: no overlay for empty directions when wrap is disabled
</verification>

<success_criteria>
- Overlay outlines appear for all 4 directions when wrap is enabled, even when natural candidates are absent in a direction
- Wrap target window is correctly identified as the furthest window in the opposite direction
- No regression: non-wrap behavior (NoOp, Beep) unchanged
- No regression: solo-window dim indicator still works
</success_criteria>

<output>
After completion, create `.planning/quick/2-fix-overlay-outlines-not-showing-for-wra/2-SUMMARY.md`
</output>
