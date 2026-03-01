---
phase: quick-6
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Program.cs
autonomous: true
requirements: [QUICK-6]

must_haves:
  truths:
    - "Running `focus --debug all` shows overlays on top-ranked candidate windows for all 4 directions simultaneously"
    - "Running `focus --debug all` prints scoring info per direction to console (similar to --debug score)"
    - "Overlays are color-coded per direction using OverlayColors (blue=left, red=right, green=up, amber=down)"
    - "Pressing any key dismisses all overlays and exits"
    - "If no candidates exist in a given direction, that direction is skipped (no overlay, console says 'no candidates')"
  artifacts:
    - path: "focus/Program.cs"
      provides: "New --debug all command handler"
      contains: "debugValue == \"all\""
  key_links:
    - from: "focus/Program.cs (debug all handler)"
      to: "NavigationService.GetRankedCandidates"
      via: "calls GetRankedCandidates for each of 4 directions"
      pattern: "GetRankedCandidates.*Direction\\."
    - from: "focus/Program.cs (debug all handler)"
      to: "OverlayManager.ShowOverlay"
      via: "shows overlay on top-ranked candidate bounds for each direction"
      pattern: "ShowOverlay.*Direction\\."
---

<objective>
Add a new `--debug all` command that shows overlays for all 4 directions simultaneously on each direction's top-ranked candidate window, and prints scoring info to the console.

Purpose: Provides a comprehensive at-a-glance visualization of where focus would move in every direction, combining the scoring output of `--debug score` with the visual overlay of `--debug overlay`. Useful for tuning strategies and understanding navigation behavior.
Output: Modified Program.cs with the new debug handler.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@focus/Program.cs
@focus/Windows/NavigationService.cs
@focus/Windows/Daemon/Overlay/OverlayManager.cs
@focus/Windows/Daemon/Overlay/OverlayColors.cs
@focus/Windows/Direction.cs
@focus/Windows/WindowInfo.cs

<interfaces>
<!-- Key types and contracts the executor needs -->

From focus/Windows/Direction.cs:
```csharp
internal enum Direction { Left, Right, Up, Down }
internal static class DirectionParser { public static Direction? Parse(string? value); }
```

From focus/Windows/NavigationService.cs:
```csharp
internal static class NavigationService
{
    // Use this overload — gets ranked candidates with the active strategy
    public static List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows, Direction direction, Strategy strategy);

    // Use this overload to also get origin info for console output
    public static List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows, Direction direction, Strategy strategy,
        out nint foregroundHwnd, out double originX, out double originY);
}
```

From focus/Windows/Daemon/Overlay/OverlayManager.cs:
```csharp
internal sealed class OverlayManager : IDisposable
{
    public OverlayManager(IOverlayRenderer renderer, OverlayColors colors);
    public void ShowOverlay(Direction direction, RECT bounds);
    public void HideAll();
    public static IOverlayRenderer CreateRenderer(string name);
}
```

From focus/Windows/Daemon/Overlay/OverlayColors.cs:
```csharp
internal class OverlayColors
{
    public uint GetArgb(Direction direction); // returns 0xAARRGGBB
}
```

From focus/Windows/WindowInfo.cs:
```csharp
internal record WindowInfo(nint Hwnd, string ProcessName, string Title,
    int Left, int Top, int Right, int Bottom,
    int MonitorIndex, bool IsTopmost, bool IsUwpFrame);
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add --debug all handler to Program.cs</name>
  <files>focus/Program.cs</files>
  <action>
Add a new `if (debugValue == "all")` block in Program.cs, placed after the existing `if (debugValue == "overlay")` block and before the "Unknown --debug value" error line. This new block should:

1. **Update the debug option description** on line 11 to include "all":
   `Description = "Debug mode: enumerate | score | config | overlay | all"`

2. **Update the unknown debug error** message near the end to include "all":
   `"Unknown --debug value: {debugValue}. Use: enumerate, score, config, overlay, all"`

3. **Implement the handler block** that does the following:

   a. Platform check (same pattern as overlay block — `OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000)`)

   b. Load config, create renderer, get foreground window bounds (same setup as existing overlay handler):
      ```csharp
      var allConfig = FocusConfig.Load();
      var allRenderer = OverlayManager.CreateRenderer(allConfig.OverlayRenderer);
      ```

   c. Enumerate windows and apply exclude filter:
      ```csharp
      var enumerator = new WindowEnumerator();
      var (windows, _) = enumerator.GetNavigableWindows();
      var filtered = ExcludeFilter.Apply(windows, allConfig.Exclude);
      ```

   d. Get foreground window bounds for display info:
      ```csharp
      var fgHwnd = PInvoke.GetForegroundWindow();
      RECT fgBounds = default;
      PInvoke.DwmGetWindowAttribute(fgHwnd,
          DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
          System.Runtime.InteropServices.MemoryMarshal.AsBytes(
              System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref fgBounds, 1)));
      ```

   e. Print header:
      ```
      Console.WriteLine($"Debug ALL directions from foreground window");
      Console.WriteLine($"  Bounds: {fgBounds.left},{fgBounds.top},{fgBounds.right},{fgBounds.bottom}");
      Console.WriteLine($"  Strategy: {allConfig.Strategy}");
      Console.WriteLine();
      ```

   f. Create the OverlayManager and iterate all 4 directions. For each direction (`Direction.Left`, `Direction.Right`, `Direction.Up`, `Direction.Down`):
      - Call `NavigationService.GetRankedCandidates(filtered, dir, allConfig.Strategy)` to get ranked candidates
      - Print direction header: `Console.WriteLine($"--- {dir} ---");`
      - If candidates list is empty: print `Console.WriteLine($"  (no candidates)");` and continue to next direction
      - If candidates exist: print the top 5 candidates (or fewer) with rank, score, title (truncated to 30 chars), process name (truncated to 12 chars), and bounds. Format each line like:
        `Console.WriteLine($"  #{i+1}  score={score:F1}  \"{Truncate(w.Title, 30)}\"  ({w.ProcessName})  [{w.Left},{w.Top},{w.Right},{w.Bottom}]");`
      - Mark the #1 candidate with an asterisk: `Console.WriteLine($"  #{i+1}* score=...` (the asterisk indicates which window gets the overlay)
      - For the top-ranked candidate (#1), create a RECT from the candidate's bounds and call `overlayManager.ShowOverlay(dir, candidateRect)`:
        ```csharp
        var topWindow = ranked[0].Window;
        var targetBounds = new RECT { left = topWindow.Left, top = topWindow.Top,
                                       right = topWindow.Right, bottom = topWindow.Bottom };
        overlayManager.ShowOverlay(dir, targetBounds);
        ```
      - Print an empty line after each direction's candidates

   g. After iterating all 4 directions, print:
      ```
      Console.WriteLine("Overlays shown on top candidates. Press any key to dismiss...");
      ```

   h. Use the same DoEvents + background keypress pattern from the existing overlay handler:
      ```csharp
      var exitEvent = new ManualResetEventSlim(false);
      var keyThread = new Thread(() => { Console.ReadKey(true); exitEvent.Set(); });
      keyThread.IsBackground = true;
      keyThread.Start();
      while (!exitEvent.IsSet)
      {
          Application.DoEvents();
          Thread.Sleep(16);
      }
      overlayManager.HideAll();
      Console.WriteLine("Overlays dismissed.");
      ```

   i. Return 0.

   Important: The entire block should be wrapped in a `using var overlayManager = new OverlayManager(allRenderer, allConfig.OverlayColors);` so the overlay windows are properly disposed.

   Important: Use `RECT` from `global::Windows.Win32.Foundation` (already imported at top of file). The RECT struct has fields `left`, `top`, `right`, `bottom` (lowercase).
  </action>
  <verify>
    Run `dotnet build focus/focus.csproj` and confirm it compiles without errors.
    Run `dotnet run --project focus/focus.csproj -- --debug all` and confirm it:
    - Prints direction headers for all 4 directions
    - Shows scoring info for each direction
    - Displays colored overlays on candidate windows
    - Dismisses on keypress
  </verify>
  <done>
    `focus --debug all` compiles and runs. It prints per-direction scoring info to console (direction header, ranked candidates with scores) and shows colored overlay borders on the top-ranked candidate window for each direction simultaneously. All overlays dismiss on any keypress.
  </done>
</task>

</tasks>

<verification>
- `dotnet build focus/focus.csproj` succeeds with no errors
- `focus --debug all` shows 4 direction sections in console output
- Overlay windows appear on top-ranked candidates (up to 4 overlays visible)
- Pressing any key dismisses all overlays
- `focus --help` shows "all" in the --debug description
</verification>

<success_criteria>
The `--debug all` command works as a combined view: console output shows scoring rankings for all 4 directions (like `--debug score` but for all directions at once), and colored overlay borders appear on the top-ranked candidate window in each direction (like `--debug overlay` but for all directions simultaneously). Pressing any key cleanly dismisses everything.
</success_criteria>

<output>
After completion, create `.planning/quick/6-add-debug-command-showing-all-directions/6-SUMMARY.md`
</output>
