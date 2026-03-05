---
phase: quick-3
plan: 1
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
autonomous: true
requirements: [QUICK-3]

must_haves:
  truths:
    - "First navigation attempt to an elevated window shows a flashing red border but does NOT switch focus"
    - "Second navigation attempt to the same elevated window within 2 seconds switches focus with a flashing red border that lasts 3 seconds"
    - "If 2 seconds elapse without a second attempt, the warning state resets and the next attempt is treated as a first attempt again"
    - "Number-key activation to elevated windows follows the same two-step confirm pattern"
  artifacts:
    - path: "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs"
      provides: "Two-step elevated window navigation with flash animation"
  key_links:
    - from: "OverlayOrchestrator.NavigateSta"
      to: "ShowElevatedWarning / FocusActivator.ActivateWithWrap"
      via: "Conditional gating on _pendingElevatedHwnd state"
      pattern: "_pendingElevated"
---

<objective>
Change the admin/elevated window navigation behavior from "navigate immediately with red border warning" to a two-step confirmation: first attempt flashes red border without navigating, second attempt within 2 seconds navigates with a 3-second flashing red border.

Purpose: Prevent accidental navigation to elevated windows, which causes UIPI issues (keyboard hook stops receiving CapsLock release). The user must intentionally confirm by pressing the same direction again within 2 seconds.

Output: Modified OverlayOrchestrator.cs with two-step elevated navigation gating and flash animation.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
@focus/Windows/Daemon/Overlay/OverlayManager.cs
@focus/Windows/FocusActivator.cs
@focus/Windows/Daemon/CapsLockMonitor.cs

<interfaces>
From focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs:
```csharp
// Current elevated warning infrastructure:
private const uint ElevatedWarningColor = 0xE0FF2222;
private readonly System.Windows.Forms.Timer _elevatedWarningTimer; // 2s auto-dismiss
private bool _elevatedWarningActive;
private void ShowElevatedWarning(HWND hwnd);           // Shows red border
private void OnElevatedWarningTimerTick(...);           // Auto-dismiss
private void ForceReleaseForElevatedWindow();           // Reset caps state

// Navigation entry points that handle elevated windows:
private void NavigateSta(string direction);             // Directional nav (step 7 checks elevation)
private void ActivateByNumberSta(int number);           // Number nav (checks elevation)
```

From focus/Windows/Daemon/Overlay/OverlayManager.cs:
```csharp
public void ShowForegroundOverlay(RECT bounds, uint argbColor);
public void HideAll();
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Implement two-step elevated navigation with flash animation</name>
  <files>focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs</files>
  <action>
Modify OverlayOrchestrator to implement a two-step confirmation pattern for elevated window navigation:

**New state fields (add as instance fields):**
- `private nint _pendingElevatedHwnd;` — HWND of the elevated window awaiting confirmation (0 = none pending)
- `private DateTime _pendingElevatedTimestamp;` — when the first attempt was made
- `private readonly System.Windows.Forms.Timer _flashTimer;` — drives the flash animation (~150ms interval)
- `private int _flashCount;` — tracks toggle count for the flash animation
- `private int _flashMaxCount;` — max toggles (1 flash = 2 toggles for first-attempt; ~20 toggles = 3s for confirmed nav)
- `private RECT _flashBounds;` — cached bounds of the window being flashed
- `private bool _flashVisible;` — current toggle state (on/off)

**Constructor changes:**
- Create `_flashTimer` with Interval=150, wire Tick to `OnFlashTimerTick`.

**Modify `NavigateSta` (step 7, the elevated check block):**

Replace the current logic (lines ~269-283) with:

```
if (targetIsElevated)
{
    nint targetHwnd = ranked[0].Window.Hwnd;

    // Check if this is a second attempt within 2 seconds to the SAME elevated window
    bool isConfirmedAttempt = _pendingElevatedHwnd == targetHwnd
        && (DateTime.UtcNow - _pendingElevatedTimestamp).TotalMilliseconds < 2000;

    if (!isConfirmedAttempt)
    {
        // FIRST ATTEMPT: flash red border once, do NOT navigate
        _pendingElevatedHwnd = targetHwnd;
        _pendingElevatedTimestamp = DateTime.UtcNow;
        StartFlash(new HWND((nint)(IntPtr)targetHwnd), maxFlashes: 1);
        // verbose log
        if (_verbose)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Error.WriteLine($"[{ts}] Navigate: target is elevated — flash warning, awaiting confirmation");
        }
        return; // Do NOT proceed to activation
    }

    // CONFIRMED (second attempt): clear pending state, will navigate below
    _pendingElevatedHwnd = 0;
    if (_verbose)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.Error.WriteLine($"[{ts}] Navigate: elevated target confirmed — navigating with 3s flash");
    }
    // Show the 3-second flash BEFORE activation (overlay must render above non-elevated foreground)
    StartFlash(new HWND((nint)(IntPtr)targetHwnd), maxFlashes: 10);
}
```

After this block, the existing step 8 (ActivateWithWrap) and step 9-10 continue as before for confirmed attempts only. The `return` in the first-attempt branch prevents activation.

**Modify `ActivateByNumberSta` similarly:**

Before the existing `if (targetIsElevated) ShowElevatedWarning(...)` block, apply the same two-step pattern:

```
if (targetIsElevated)
{
    nint targetHwnd = target.Hwnd;
    bool isConfirmedAttempt = _pendingElevatedHwnd == targetHwnd
        && (DateTime.UtcNow - _pendingElevatedTimestamp).TotalMilliseconds < 2000;

    if (!isConfirmedAttempt)
    {
        _pendingElevatedHwnd = targetHwnd;
        _pendingElevatedTimestamp = DateTime.UtcNow;
        StartFlash(new HWND((nint)(IntPtr)targetHwnd), maxFlashes: 1);
        if (_verbose)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Error.WriteLine($"[{ts}] Number: {number} -> elevated, awaiting confirmation");
        }
        return; // Do NOT activate
    }

    _pendingElevatedHwnd = 0;
    if (_verbose)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.Error.WriteLine($"[{ts}] Number: {number} -> elevated confirmed, navigating with 3s flash");
    }
    StartFlash(new HWND((nint)(IntPtr)targetHwnd), maxFlashes: 10);
}
```

Then let the existing activation code run (TryActivateWindow etc.).

**New method `StartFlash`:**

```csharp
private unsafe void StartFlash(HWND hwnd, int maxFlashes)
{
    _elevatedWarningActive = true;
    _flashTimer.Stop();
    _overlayManager.HideAll();

    RECT bounds = default;
    var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref bounds, 1));
    var hr = PInvoke.DwmGetWindowAttribute(
        hwnd,
        DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
        boundsBytes);

    if (!hr.Succeeded || (bounds.right - bounds.left) <= 0)
        return;

    _flashBounds = bounds;
    _flashCount = 0;
    _flashMaxCount = maxFlashes * 2; // Each flash = show + hide = 2 ticks
    _flashVisible = false;

    // Show immediately on first tick
    _overlayManager.ShowForegroundOverlay(bounds, ElevatedWarningColor);
    _flashVisible = true;
    _flashCount = 1;

    _flashTimer.Start();
}
```

**New method `OnFlashTimerTick`:**

```csharp
private void OnFlashTimerTick(object? sender, EventArgs e)
{
    _flashCount++;

    if (_flashCount >= _flashMaxCount)
    {
        _flashTimer.Stop();
        _elevatedWarningActive = false;
        _overlayManager.HideAll();
        return;
    }

    if (_flashVisible)
    {
        _overlayManager.HideAll();
        _flashVisible = false;
    }
    else
    {
        _overlayManager.ShowForegroundOverlay(_flashBounds, ElevatedWarningColor);
        _flashVisible = true;
    }
}
```

**Remove `ShowElevatedWarning` method** — replaced by `StartFlash`.

**Remove `_elevatedWarningTimer`** — replaced by `_flashTimer`. Remove from constructor, Dispose, OnReleasedSta, and the old `OnElevatedWarningTimerTick` handler.

**Update `OnReleasedSta`:**
- Replace `_elevatedWarningTimer.Stop()` with `_flashTimer.Stop()`
- Clear `_pendingElevatedHwnd = 0` on caps release

**Update `ForceReleaseForElevatedWindow`:**
- Do NOT stop `_flashTimer` — the flash should continue for the full 3-second duration even after caps release
- Still set `_capsLockHeld = false`, `_currentMode = Navigate`, stop `_delayTimer`, invoke `_onForceRelease`

**Update `Dispose`:**
- Add `_flashTimer.Stop(); _flashTimer.Dispose();`
- Remove `_elevatedWarningTimer` disposal

**Summary of timing:**
- Flash interval: 150ms per toggle (border appears for 150ms, disappears for 150ms = one flash is 300ms)
- First attempt: maxFlashes=1 => 2 ticks => border shows once (visible 150ms, then hidden) — total ~300ms
- Confirmed navigation: maxFlashes=10 => 20 ticks => border flashes 10 times over ~3 seconds (10 * 300ms = 3000ms)
- Confirmation window: 2 seconds (checked via DateTime.UtcNow comparison, NOT via a timer)
  </action>
  <verify>
    <automated>cd C:/Work/windowfocusnavigation/focus && dotnet build -c Release 2>&1 | tail -5</automated>
  </verify>
  <done>
    - Build succeeds with zero errors
    - First navigation attempt to elevated window flashes red border once without switching focus
    - Second attempt within 2 seconds to same elevated window navigates with 3-second flashing red border
    - After 2 seconds without confirmation, next attempt is treated as first attempt again
    - Number-key activation follows the same two-step pattern
    - CapsLock release clears pending state
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>Two-step elevated window navigation confirmation with flash animation</what-built>
  <how-to-verify>
    1. Run daemon: `focus daemon --verbose` (from non-admin terminal)
    2. Open an elevated/admin window (e.g., right-click Terminal -> Run as administrator)
    3. Hold CapsLock and press direction toward the admin window
    4. EXPECTED: Red border flashes once on the admin window, focus does NOT change, verbose log says "awaiting confirmation"
    5. Within 2 seconds, press the same direction again toward the admin window
    6. EXPECTED: Focus switches to admin window, red border flashes repeatedly for ~3 seconds, verbose log says "confirmed"
    7. Wait 3+ seconds for flash to stop, then repeat step 3 but wait MORE than 2 seconds before pressing again
    8. EXPECTED: The state resets — pressing again shows a single flash (first attempt) instead of navigating
    9. Test with number key activation (CapsLock + number pointing to admin window) — same two-step behavior
  </how-to-verify>
  <resume-signal>Type "approved" or describe issues</resume-signal>
</task>

</tasks>

<verification>
- `dotnet build -c Release` in focus/ directory succeeds with 0 errors
- Manual test with an elevated window confirms two-step pattern
</verification>

<success_criteria>
- Navigating to elevated windows requires two presses within 2 seconds
- First press shows single red flash, no focus change
- Second press within window navigates with 3-second flashing border
- Timeout resets to first-attempt state
- Number-key navigation follows same pattern
- Build compiles cleanly
</success_criteria>

<output>
After completion, create `.planning/quick/3-change-admin-window-navigation-flash-red/3-SUMMARY.md`
</output>
