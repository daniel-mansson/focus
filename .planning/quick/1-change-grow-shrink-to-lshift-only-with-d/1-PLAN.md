---
phase: quick
plan: 1
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/KeyEvent.cs
  - focus/Windows/Daemon/KeyboardHookHandler.cs
  - focus/Windows/Daemon/CapsLockMonitor.cs
  - focus/Windows/Daemon/WindowManagerService.cs
autonomous: true
requirements: [QUICK-01]
must_haves:
  truths:
    - "CAPS+LShift+Right grows window horizontally (both left and right edges expand outward by half a grid step each)"
    - "CAPS+LShift+Left shrinks window horizontally (both left and right edges contract inward by half a grid step each)"
    - "CAPS+LShift+Up grows window vertically (both top and bottom edges expand outward by half a grid step each)"
    - "CAPS+LShift+Down shrinks window vertically (both top and bottom edges contract inward by half a grid step each)"
    - "CAPS+LCtrl+direction no longer triggers any resize mode (LCtrl is inert)"
    - "Navigate, Move modes are unaffected"
  artifacts:
    - path: "focus/Windows/Daemon/KeyEvent.cs"
      provides: "WindowMode enum without Shrink variant"
    - path: "focus/Windows/Daemon/KeyboardHookHandler.cs"
      provides: "Mode derivation without LCtrl branch"
    - path: "focus/Windows/Daemon/WindowManagerService.cs"
      provides: "ComputeGrow with symmetric expand/contract by direction"
  key_links:
    - from: "KeyboardHookHandler.cs"
      to: "KeyEvent.cs"
      via: "WindowMode enum used in mode switch"
      pattern: "WindowMode\\.(Navigate|Move|Grow)"
    - from: "WindowManagerService.cs"
      to: "KeyEvent.cs"
      via: "WindowMode enum in MoveOrResize dispatch"
      pattern: "WindowMode\\.Grow"
---

<objective>
Change grow/shrink to use only CAPS+LShift (remove LCtrl shrink mode). Direction keys now encode axis AND intent: right=grow horizontal, left=shrink horizontal, up=grow vertical, down=shrink vertical. Both edges on the affected axis move symmetrically (each by half a grid step).

Purpose: Simplify the resize keybinding to a single modifier (LShift) with more intuitive direction semantics.
Output: Modified KeyEvent.cs, KeyboardHookHandler.cs, CapsLockMonitor.cs, WindowManagerService.cs
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@focus/Windows/Daemon/KeyEvent.cs
@focus/Windows/Daemon/KeyboardHookHandler.cs
@focus/Windows/Daemon/CapsLockMonitor.cs
@focus/Windows/Daemon/WindowManagerService.cs

<interfaces>
<!-- Current WindowMode enum (to be modified): -->
From focus/Windows/Daemon/KeyEvent.cs:
```csharp
internal enum WindowMode { Navigate, Move, Grow, Shrink }

internal readonly record struct KeyEvent(
    uint VkCode, bool IsKeyDown, uint Timestamp,
    bool LShiftHeld = false, bool LCtrlHeld = false, bool AltHeld = false,
    WindowMode Mode = WindowMode.Navigate);
```

From focus/Windows/Daemon/KeyboardHookHandler.cs (mode derivation, line 177):
```csharp
WindowMode mode = (_tabHeld, lShiftHeld, lCtrlHeld) switch
{
    (true, _, _)  => WindowMode.Move,
    (_, true, _)  => WindowMode.Grow,
    (_, _, true)  => WindowMode.Shrink,
    _             => WindowMode.Navigate
};
```

From focus/Windows/Daemon/WindowManagerService.cs (dispatch, line 67):
```csharp
RECT newWinRect = mode switch
{
    WindowMode.Move   => ComputeMove(...),
    WindowMode.Grow   => ComputeGrow(...),
    WindowMode.Shrink => ComputeShrink(...),
    _                 => winRect
};
```

From focus/Windows/Daemon/CapsLockMonitor.cs (modifier prefix logging, line 79):
```csharp
private static string BuildModifierPrefix(KeyEvent evt)
{
    if (!evt.LShiftHeld && !evt.LCtrlHeld && !evt.AltHeld)
        return string.Empty;
    var parts = new List<string>(3);
    if (evt.LCtrlHeld)  parts.Add("LCtrl");
    if (evt.AltHeld)    parts.Add("Alt");
    if (evt.LShiftHeld) parts.Add("LShift");
    return string.Join("+", parts) + "+";
}
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Remove Shrink mode from enum, hook, and monitor</name>
  <files>focus/Windows/Daemon/KeyEvent.cs, focus/Windows/Daemon/KeyboardHookHandler.cs, focus/Windows/Daemon/CapsLockMonitor.cs</files>
  <action>
**KeyEvent.cs:**
1. Remove `Shrink` from the `WindowMode` enum. New enum: `internal enum WindowMode { Navigate, Move, Grow }`
2. Update the XML doc comment on the enum to describe the new semantics:
   - Navigate = bare CAPS+direction (existing behavior, default)
   - Move = CAPS+TAB then direction (move window)
   - Grow = CAPS+LShift then direction (right/up = expand both edges symmetrically, left/down = contract both edges symmetrically)
3. Remove the `LCtrlHeld` field from the `KeyEvent` record struct. New signature:
   ```csharp
   internal readonly record struct KeyEvent(
       uint VkCode, bool IsKeyDown, uint Timestamp,
       bool LShiftHeld = false, bool AltHeld = false,
       WindowMode Mode = WindowMode.Navigate);
   ```
4. Update the XML doc on KeyEvent to remove LCtrlHeld references.

**KeyboardHookHandler.cs:**
1. Remove the `VK_LCONTROL` constant (line 34). Keep `VK_CONTROL` (line 31) since it is still used for the CAPS modifier filter on line 204.
2. In the direction key interception block (around line 170-187):
   - Remove `bool lCtrlHeld` line (line 172).
   - Simplify the mode switch to remove the LCtrl branch:
     ```csharp
     WindowMode mode = (_tabHeld, lShiftHeld) switch
     {
         (true, _)  => WindowMode.Move,
         (_, true)  => WindowMode.Grow,
         _          => WindowMode.Navigate
     };
     ```
   - Update the TryWrite call to remove `lCtrlHeld` from the KeyEvent constructor:
     ```csharp
     _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, lShiftHeld, altHeld, mode));
     ```
3. Update comments: MODE-03 references to LCtrl/Shrink should be removed from the mode derivation comment.

**CapsLockMonitor.cs:**
1. In `BuildModifierPrefix` (line 79-89): Remove the `if (evt.LCtrlHeld) parts.Add("LCtrl");` line. Update the condition to `if (!evt.LShiftHeld && !evt.AltHeld)`.
2. No other changes needed — CapsLockMonitor does not reference `WindowMode.Shrink` directly.
  </action>
  <verify>
    <automated>cd C:/Work/focus && dotnet build focus/Windows/Daemon/Daemon.csproj --no-restore 2>&1 | tail -5</automated>
  </verify>
  <done>WindowMode enum has 3 values (Navigate, Move, Grow). LCtrlHeld removed from KeyEvent. Hook no longer reads VK_LCONTROL for mode. CapsLockMonitor logging no longer references LCtrl. Project compiles with zero errors.</done>
</task>

<task type="auto">
  <name>Task 2: Rewrite ComputeGrow for symmetric expand/contract by direction</name>
  <files>focus/Windows/Daemon/WindowManagerService.cs</files>
  <action>
1. **Remove `ComputeShrink` method entirely** (lines 226-290). It is no longer needed.

2. **Remove `WindowMode.Shrink` from the mode switch** in `MoveOrResize` (line 71). New dispatch:
   ```csharp
   RECT newWinRect = mode switch
   {
       WindowMode.Move => ComputeMove(...),
       WindowMode.Grow => ComputeGrow(...),
       _               => winRect
   };
   ```

3. **Rewrite `ComputeGrow`** with new semantics. Each direction key now means:
   - **right** = grow horizontal: BOTH left and right edges expand outward symmetrically. Left edge moves left by half a step, right edge moves right by half a step. Each edge snaps independently. Clamp each edge to work area boundary.
   - **left** = shrink horizontal: BOTH left and right edges contract inward symmetrically. Left edge moves right by half a step, right edge moves left by half a step. Each edge snaps independently. Minimum width = 1 grid step (if visW <= stepX, return win unchanged as no-op).
   - **up** = grow vertical: BOTH top and bottom edges expand outward symmetrically. Top edge moves up by half a step, bottom edge moves down by half a step. Each edge snaps independently. Clamp each edge to work area boundary.
   - **down** = shrink vertical: BOTH top and bottom edges contract inward symmetrically. Top edge moves down by half a step, bottom edge moves up by half a step. Each edge snaps independently. Minimum height = 1 grid step (if visH <= stepY, return win unchanged as no-op).

   Implementation for "half a step": use `stepX / 2` and `stepY / 2` for the per-edge delta (integer division is fine — the grid is even-denominator by design). Each edge is snapped independently using the existing Floor/Ceiling helpers:

   ```csharp
   private static RECT ComputeGrow(string direction, RECT vis, RECT win, RECT work,
       int stepX, int stepY, int tolX, int tolY,
       int borderL, int borderT, int borderR, int borderB)
   {
       int newVisLeft   = vis.left;
       int newVisTop    = vis.top;
       int newVisRight  = vis.right;
       int newVisBottom = vis.bottom;
       int visW = vis.right  - vis.left;
       int visH = vis.bottom - vis.top;

       int halfStepX = stepX / 2;
       int halfStepY = stepY / 2;

       switch (direction)
       {
           case "right":
               // Grow horizontal: both edges expand outward
               newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                   ? vis.left - halfStepX
                   : GridCalculator.NearestGridLineFloor(vis.left, work.left, stepX);
               newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                   ? vis.right + halfStepX
                   : GridCalculator.NearestGridLineCeiling(vis.right, work.left, stepX);
               // Clamp to work area
               newVisLeft  = Math.Max(newVisLeft, work.left);
               newVisRight = Math.Min(newVisRight, work.right);
               break;

           case "left":
               // Shrink horizontal: both edges contract inward
               if (visW <= stepX) return win;  // minimum size guard
               newVisLeft = GridCalculator.IsAligned(vis.left, work.left, stepX, tolX)
                   ? vis.left + halfStepX
                   : GridCalculator.NearestGridLineCeiling(vis.left, work.left, stepX);
               newVisRight = GridCalculator.IsAligned(vis.right, work.left, stepX, tolX)
                   ? vis.right - halfStepX
                   : GridCalculator.NearestGridLineFloor(vis.right, work.left, stepX);
               // Minimum size clamp
               if (newVisRight - newVisLeft < stepX)
               {
                   int center = (vis.left + vis.right) / 2;
                   newVisLeft  = center - stepX / 2;
                   newVisRight = center + stepX / 2;
               }
               break;

           case "up":
               // Grow vertical: both edges expand outward
               newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                   ? vis.top - halfStepY
                   : GridCalculator.NearestGridLineFloor(vis.top, work.top, stepY);
               newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                   ? vis.bottom + halfStepY
                   : GridCalculator.NearestGridLineCeiling(vis.bottom, work.top, stepY);
               // Clamp to work area
               newVisTop    = Math.Max(newVisTop, work.top);
               newVisBottom = Math.Min(newVisBottom, work.bottom);
               break;

           case "down":
               // Shrink vertical: both edges contract inward
               if (visH <= stepY) return win;  // minimum size guard
               newVisTop = GridCalculator.IsAligned(vis.top, work.top, stepY, tolY)
                   ? vis.top + halfStepY
                   : GridCalculator.NearestGridLineCeiling(vis.top, work.top, stepY);
               newVisBottom = GridCalculator.IsAligned(vis.bottom, work.top, stepY, tolY)
                   ? vis.bottom - halfStepY
                   : GridCalculator.NearestGridLineFloor(vis.bottom, work.top, stepY);
               // Minimum size clamp
               if (newVisBottom - newVisTop < stepY)
               {
                   int center = (vis.top + vis.bottom) / 2;
                   newVisTop    = center - stepY / 2;
                   newVisBottom = center + stepY / 2;
               }
               break;
       }

       // Translate visible coords back to GetWindowRect coordinate space
       return new RECT
       {
           left   = newVisLeft  - borderL,
           top    = newVisTop   - borderT,
           right  = newVisRight + borderR,
           bottom = newVisBottom + borderB
       };
   }
   ```

4. **Update XML doc comments** on `ComputeGrow` to describe the new symmetric behavior:
   - right/up = grow (both edges expand outward by half a grid step each)
   - left/down = shrink (both edges contract inward by half a grid step each)
   - Minimum size = 1 grid step on the affected axis

5. **Post-computation no-op guard for shrink cases** (left/down): After the switch block but before the RECT return, add the same guard that was in ComputeShrink:
   ```csharp
   // For shrink directions (left/down): if visible dimension did not actually shrink, no-op
   int newVisW = newVisRight - newVisLeft;
   int newVisH = newVisBottom - newVisTop;
   if (direction is "left" && newVisW >= visW) return win;
   if (direction is "down" && newVisH >= visH) return win;
   ```
   Place this AFTER the switch and BEFORE the RECT construction.
  </action>
  <verify>
    <automated>cd C:/Work/focus && dotnet build focus/Windows/Daemon/Daemon.csproj --no-restore 2>&1 | tail -5</automated>
  </verify>
  <done>ComputeShrink removed. ComputeGrow handles all four directions with symmetric edge movement (half step per edge). Right/Up = expand, Left/Down = contract. Minimum size guard active for shrink directions. Build succeeds with zero errors.</done>
</task>

</tasks>

<verification>
1. `dotnet build` succeeds with zero errors and zero warnings related to these changes.
2. Manual test: Run the daemon, hold CAPS+LShift+Right on a centered window -- window should grow horizontally from both sides.
3. Manual test: Hold CAPS+LShift+Left -- window should shrink horizontally from both sides.
4. Manual test: Hold CAPS+LShift+Up -- window should grow vertically from both edges.
5. Manual test: Hold CAPS+LShift+Down -- window should shrink vertically from both edges.
6. Manual test: CAPS+LCtrl+direction should now behave as plain navigate (no resize).
7. Manual test: CAPS+direction (no modifier) still navigates. CAPS+TAB+direction still moves.
</verification>

<success_criteria>
- Build compiles cleanly (zero CS errors)
- WindowMode enum has exactly 3 values: Navigate, Move, Grow
- LCtrl no longer triggers any resize mode
- CAPS+LShift+Right/Up expands window symmetrically on the axis
- CAPS+LShift+Left/Down contracts window symmetrically on the axis
- Existing Navigate and Move modes work unchanged
</success_criteria>

<output>
After completion, create `.planning/quick/1-change-grow-shrink-to-lshift-only-with-d/1-SUMMARY.md`
</output>
