---
phase: quick-6
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
autonomous: true
requirements: [QUICK-6]
must_haves:
  truths:
    - "Every visible window gets the same number label regardless of which window is focused"
    - "Pressing CAPS+N when N is the active window is harmless (re-activates or no-op)"
    - "Number labels still render correctly for up to 9 windows"
  artifacts:
    - path: "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs"
      provides: "Stable overlay numbering that includes the active window"
      contains: "WindowSorter.SortByPosition"
  key_links:
    - from: "ShowOverlaysForCurrentForeground (overlay display)"
      to: "ActivateByNumberSta (key handler)"
      via: "Same sorted list logic — both must include active window"
      pattern: "SortByPosition"
---

<objective>
Include the active/foreground window in overlay numbering so that window IDs stay stable when the user navigates and a different window gains focus.

Purpose: Currently, overlay numbers are assigned only to non-foreground windows. When the user presses CAPS+Arrow and a different window becomes active, all the overlay numbers shift because the filtering removes a different window. Including the active window in the sorted list means each window keeps a consistent position-based number.

Output: Modified OverlayOrchestrator.cs where both the overlay rendering path and the number-key activation path sort ALL filtered windows (including the foreground window) and assign numbers to the full set.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
@focus/Windows/WindowSorter.cs
@focus/Windows/Daemon/Overlay/OverlayManager.cs

<interfaces>
From focus/Windows/WindowSorter.cs:
```csharp
public static List<WindowInfo> SortByPosition(List<WindowInfo> windows, NumberSortStrategy strategy)
```

From focus/Windows/Daemon/Overlay/OverlayManager.cs:
```csharp
public void ShowNumberLabel(int number, RECT windowBounds, NumberOverlayPosition position)
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Include active window in overlay numbering and key selection</name>
  <files>focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs</files>
  <action>
Two methods in OverlayOrchestrator.cs exclude the foreground window from numbering. Both must be changed to include it:

**1. ShowOverlaysForCurrentForeground() — overlay label rendering (lines ~420-432):**

Current code:
```csharp
var fgHwndVal = (nint)(IntPtr)PInvoke.GetForegroundWindow();
var nonFgWindows = filtered.Where(w => w.Hwnd != fgHwndVal).ToList();
var sorted = WindowSorter.SortByPosition(nonFgWindows, config.NumberSortStrategy);
```

Change to sort ALL filtered windows (remove the foreground exclusion):
```csharp
var sorted = WindowSorter.SortByPosition(filtered, config.NumberSortStrategy);
```

Remove the `fgHwndVal` and `nonFgWindows` locals that are no longer needed. The rest of the loop (`for (int i = 0; i < Math.Min(sorted.Count, 9); i++)`) stays the same — it renders number labels on all windows including the active one.

**2. ActivateByNumberSta() — CAPS+number key handler (lines ~186-219):**

Current code:
```csharp
var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();
var candidates = filtered.Where(w => w.Hwnd != fgHwnd).ToList();
var sorted = WindowSorter.SortByPosition(candidates, config.NumberSortStrategy);
```

Change to sort ALL filtered windows (remove the foreground exclusion):
```csharp
var sorted = WindowSorter.SortByPosition(filtered, config.NumberSortStrategy);
```

Remove the `fgHwnd` and `candidates` locals. The activation path (`FocusActivator.TryActivateWindow(target.Hwnd)`) will harmlessly re-activate the already-focused window if the user presses the number corresponding to it — no special-case handling needed.

Do NOT change any other logic in the file. The directional overlay rendering, solo-window detection, wrap behavior, and foreground white border remain unchanged.
  </action>
  <verify>
    <automated>cd C:/Work/windowfocusnavigation/focus && dotnet build --no-restore -c Debug 2>&1 | tail -5</automated>
  </verify>
  <done>
    - Both ShowOverlaysForCurrentForeground and ActivateByNumberSta sort the full filtered window list without excluding the foreground window
    - No references to foreground-window exclusion remain in the number overlay or number activation code paths
    - The foreground white border logic (separate code path, unrelated) is untouched
    - Build succeeds with zero errors
  </done>
</task>

</tasks>

<verification>
1. `dotnet build -c Debug` — zero errors
2. `dotnet build -c Release` — zero errors
3. Manual: hold CAPS with 3+ windows open, note overlay numbers, press CAPS+Arrow to navigate to a different window, hold CAPS again — numbers should be the same for each window
</verification>

<success_criteria>
- Overlay number labels are assigned based on position across ALL windows, not just non-foreground windows
- Navigating to a different window does not cause number labels to shift
- CAPS+N activates the correct window matching the overlay label, even if N happens to be the currently active window
- Build passes in both Debug and Release configurations
</success_criteria>

<output>
After completion, create `.planning/quick/6-include-active-window-in-overlay-numberi/6-SUMMARY.md`
</output>
