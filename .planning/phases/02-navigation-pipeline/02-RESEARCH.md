# Phase 2: Navigation Pipeline - Research

**Researched:** 2026-02-27
**Domain:** Win32 directional navigation geometry, SetForegroundWindow bypass, exit codes
**Confidence:** HIGH (core Win32 APIs verified via official MS docs; scoring algorithm design based on well-established spatial navigation patterns; SendInput bypass verified by multiple sources including PowerToys production code)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Multi-monitor behavior:**
- Treat all monitors as one virtual screen — pure geometry, no same-monitor preference
- Windows scored equally regardless of which monitor they're on
- Navigating "right" from the right edge of one monitor naturally reaches the next monitor
- Use raw virtual screen coordinates as-is — ignore physical gaps between monitors
- User setup: laptop screen below main monitor (vertical arrangement), so "down" reaches laptop, "up" reaches main monitor

**Activation edge cases:**
- If the best candidate is an elevated (admin) window and activation fails, skip it and try the next-best candidate in that direction
- If ALL candidates in a direction fail to activate (e.g., all elevated), return exit code 2 (error) — distinct from exit code 1 (no candidates exist)
- Only target visible windows — minimized windows are already filtered by Phase 1 enumeration
- Always attempt focus switch even when source is a fullscreen app — if the hotkey fires, the user pressed it intentionally

**Source reference point:**
- Origin point: geometric center of the foreground window
- Target measurement: nearest edge/point on the target window's bounds (large nearby windows shouldn't be penalized for having distant centers)
- If no foreground window can be determined (desktop focused, no window has focus), fall back to the center of the primary monitor
- Always exclude the currently focused window from candidates — you're navigating away from it

### Claude's Discretion

- Navigation feel: direction cone width, angle thresholds, and scoring weights for the balanced strategy
- How to handle diagonal edge cases (windows that are partially in the requested direction)
- SetForegroundWindow bypass implementation details (SendInput ALT technique)
- Tie-breaking logic when multiple windows score equally

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| NAV-01 | User can navigate focus left | Direction enum + filter candidates left of origin center; score by axis distance + alignment; activate best |
| NAV-02 | User can navigate focus right | Same as NAV-01 for right direction |
| NAV-03 | User can navigate focus up | Same as NAV-01 for up direction |
| NAV-04 | User can navigate focus down | Same as NAV-01 for down direction |
| NAV-05 | Navigation works across multiple monitors via virtual screen coordinates | DWMWA_EXTENDED_FRAME_BOUNDS already returns virtual screen physical pixel coordinates; no per-monitor logic needed |
| NAV-07 | Tool supports "balanced" weighting strategy | Research establishes a combined axis-distance + perpendicular-penalty score formula; tunable weights |
| FOCUS-01 | Tool switches focus using SetForegroundWindow with SendInput ALT bypass | SendInput(VK_MENU down + up) before SetForegroundWindow; CsWin32 INPUT struct pattern documented |
| OUT-02 | Tool returns meaningful exit codes (0=switched, 1=no candidate, 2=error) | Exit code 0 on success; 1 when no candidates in direction; 2 when all candidates fail activation |
</phase_requirements>

---

## Summary

Phase 2 adds the entire navigation pipeline on top of Phase 1's window enumeration foundation. The work falls into three clusters: CLI entry point for the direction argument (`focus left/right/up/down`), the directional scoring engine that selects the best candidate window, and the focus activation sequence that calls SetForegroundWindow with the SendInput ALT bypass.

The directional scoring algorithm is the core design challenge. No single Win32 API provides "the window to the left" — the tool must score all candidates using geometry. The standard approach across spatial navigation implementations (W3C spatial-nav spec, xmonad WindowNavigation, twm) is a two-component score: a primary-axis distance (how far in the requested direction) and a secondary-axis penalty (how far off-center in the perpendicular direction). The locked decision to measure distance to the *nearest point* on the target bounds (not center-to-center) is correct and well-supported: it gives large nearby windows a fair score.

Focus activation via SetForegroundWindow is restricted by Windows' foreground lock. The SendInput ALT bypass (simulate VK_MENU keydown + keyup before SetForegroundWindow) is documented as 100% reliable because it causes Windows itself to unlock foreground promotion — the process does not need to meet any of the standard eligibility conditions. CsWin32 generates the INPUT struct with an anonymous union; the correct initialization pattern is documented below. The critical known limitation: UIPI blocks focus switching to elevated (admin) processes, and SetForegroundWindow returns FALSE silently. The locked decision to skip elevated targets and try the next-best is the correct approach.

**Primary recommendation:** Implement in three sequential units: (1) wire `focus <direction>` CLI argument in Program.cs, (2) build `NavigationService` that owns scoring and returns a ranked candidate list, (3) build `FocusActivator` that tries the ranked list and returns an exit code. This keeps each piece testable in isolation.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Windows.CsWin32 | 0.3.269 (already in project) | P/Invoke for SendInput, SetForegroundWindow, GetForegroundWindow, GetMonitorInfo | Already wired; generates type-safe INPUT struct, KEYBDINPUT |
| System.CommandLine | 2.0.3 (already in project) | Parse `focus left/right/up/down` direction argument | Already wired; add direction as positional Argument<string> |
| .NET 8 (net8.0) | net8.0 (project target) | Runtime | Already set in focus.csproj |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Math | (BCL) | Atan2, Abs, Sqrt for geometry calculations | Used in scoring; no NuGet package needed |

### New NativeMethods.txt Entries Needed

The following APIs must be added to `focus/NativeMethods.txt` for Phase 2:

```
SetForegroundWindow
GetForegroundWindow
SendInput
MonitorFromPoint
MONITORINFOF_PRIMARY
```

`GetMonitorInfo` is already in NativeMethods.txt. `MonitorFromWindow` is already present. `MONITORINFOF_PRIMARY` must be added explicitly because CsWin32 does not auto-generate constants (confirmed: CsWin32 issue #1004).

---

## Architecture Patterns

### Recommended Project Structure

```
focus/
├── focus.csproj
├── Program.cs                     # Wire focus <direction> argument + exit code return
└── Windows/
    ├── WindowEnumerator.cs        # (Phase 1 — unchanged)
    ├── WindowInfo.cs              # (Phase 1 — unchanged)
    ├── MonitorHelper.cs           # (Phase 1 — add GetPrimaryMonitorCenter)
    ├── NavigationService.cs       # (NEW) Direction scoring + candidate ranking
    └── FocusActivator.cs          # (NEW) SetForegroundWindow + SendInput bypass
```

### Pattern 1: CLI Direction Argument

**What:** Add `focus left/right/up/down` as a positional argument to the root command.

**Why this shape:** REQUIREMENTS.md and CONTEXT.md lock the invocation as `focus left` (not `focus --direction left`). In System.CommandLine 2.0, this is a positional `Argument<string>` on the root command.

```csharp
// Source: System.CommandLine 2.0 docs (updated 2025-12-18)
// In Program.cs — add to existing RootCommand setup

var directionArgument = new Argument<string?>("direction")
{
    Description = "Direction to navigate: left | right | up | down",
    Arity = ArgumentArity.ZeroOrOne   // optional so --debug enumerate still works
};

rootCommand.Arguments.Add(directionArgument);

rootCommand.SetAction(parseResult =>
{
    var direction = parseResult.GetValue(directionArgument);
    var debugValue = parseResult.GetValue(debugOption);

    if (!string.IsNullOrEmpty(debugValue))
    {
        // existing --debug handling
    }
    else if (!string.IsNullOrEmpty(direction))
    {
        return RunNavigation(direction);
    }
    // ... etc
});
```

**Valid direction values:** `"left"`, `"right"`, `"up"`, `"down"` (case-insensitive). Return exit code 2 on invalid direction string.

### Pattern 2: Direction Enum

```csharp
// In NavigationService.cs or a shared types file
internal enum Direction { Left, Right, Up, Down }

internal static class DirectionExtensions
{
    public static Direction? Parse(string value) => value.ToLowerInvariant() switch
    {
        "left"  => Direction.Left,
        "right" => Direction.Right,
        "up"    => Direction.Up,
        "down"  => Direction.Down,
        _       => null
    };
}
```

### Pattern 3: Origin Point — Foreground Window Center

```csharp
// Source: GetForegroundWindow docs (updated 2025-07-01), MonitorFromPoint + GetMonitorInfo docs
// In NavigationService.cs

[SupportedOSPlatform("windows5.0")]
internal static unsafe (double originX, double originY) GetOriginPoint(
    List<nint> windowsHwnds, nint foregroundHwnd)
{
    // Case 1: valid foreground window found
    if (foregroundHwnd != 0)
    {
        var hwnd = new HWND((void*)(IntPtr)foregroundHwnd);
        RECT bounds = default;
        var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref bounds, 1));
        var hr = PInvoke.DwmGetWindowAttribute(
            hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, boundsBytes);
        if (hr.Succeeded && (bounds.right - bounds.left) > 0)
        {
            return (
                (bounds.left + bounds.right) / 2.0,
                (bounds.top + bounds.bottom) / 2.0
            );
        }
    }

    // Case 2: GetForegroundWindow returned NULL (desktop focused) or bounds failed
    // Fall back to center of primary monitor
    return GetPrimaryMonitorCenter();
}

[SupportedOSPlatform("windows5.0")]
private static unsafe (double x, double y) GetPrimaryMonitorCenter()
{
    // Use MonitorFromPoint with MONITOR_DEFAULTTOPRIMARY to find primary monitor
    HMONITOR hm = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
    MONITORINFO mi = default;
    mi.cbSize = (uint)sizeof(MONITORINFO);
    if (PInvoke.GetMonitorInfo(hm, ref mi))
    {
        return (
            (mi.rcMonitor.left + mi.rcMonitor.right) / 2.0,
            (mi.rcMonitor.top + mi.rcMonitor.bottom) / 2.0
        );
    }
    // Ultimate fallback if even GetMonitorInfo fails
    return (0, 0);
}
```

**Key fact:** `GetForegroundWindow()` returns NULL "in certain circumstances, such as when a window is losing activation" — official docs (updated 2025-07-01). The primary monitor fallback handles this cleanly.

### Pattern 4: Nearest-Point Distance to Target Window

**What:** The locked decision is to measure from origin to the *nearest point* on the target's bounds rectangle. This correctly avoids penalizing large windows for having distant centers.

```csharp
// Nearest point on rect to a given point
internal static (double nearX, double nearY) NearestPoint(
    double px, double py,
    int left, int top, int right, int bottom)
{
    double nearX = Math.Clamp(px, left, right);
    double nearY = Math.Clamp(py, top, bottom);
    return (nearX, nearY);
}
```

### Pattern 5: Balanced Directional Scoring Algorithm (NAV-07)

**What:** The "balanced" strategy scores candidates by combining primary-axis distance with a perpendicular-distance penalty. Claude's discretion applies to weights.

**Recommended approach (derived from spatial navigation research and common implementations):**

For a direction, define:
- **Primary axis:** The axis in the navigation direction (X for left/right, Y for up/down)
- **Secondary axis:** The perpendicular axis
- **Directional filter:** Only candidates whose nearest point is strictly in the requested direction from origin

```csharp
// In NavigationService.cs
internal static double ScoreCandidate(
    double originX, double originY,
    WindowInfo candidate,
    Direction direction)
{
    var (nearX, nearY) = NearestPoint(
        originX, originY,
        candidate.Left, candidate.Top, candidate.Right, candidate.Bottom);

    double dx = nearX - originX;
    double dy = nearY - originY;

    // Step 1: Directional filter — must be in the requested direction
    // Uses the nearest point to determine if candidate is "in front"
    bool inDirection = direction switch
    {
        Direction.Left  => nearX < originX,
        Direction.Right => nearX > originX,
        Direction.Up    => nearY < originY,
        Direction.Down  => nearY > originY,
        _ => false
    };
    if (!inDirection) return double.MaxValue; // eliminated

    // Step 2: Primary axis distance — how far in the requested direction
    double primaryDist = direction switch
    {
        Direction.Left  => originX - nearX,   // positive when candidate is to the left
        Direction.Right => nearX - originX,   // positive when candidate is to the right
        Direction.Up    => originY - nearY,   // positive when candidate is above
        Direction.Down  => nearY - originY,   // positive when candidate is below
        _ => double.MaxValue
    };

    // Step 3: Secondary axis distance — perpendicular deviation
    double secondaryDist = direction switch
    {
        Direction.Left  or Direction.Right => Math.Abs(nearY - originY),
        Direction.Up    or Direction.Down  => Math.Abs(nearX - originX),
        _ => double.MaxValue
    };

    // Balanced weights: primary distance weighted equally to secondary deviation.
    // The 2.0 multiplier on secondary makes alignment matter but not dominate.
    // These are Claude's discretion values — adjust for feel during verification.
    const double primaryWeight   = 1.0;
    const double secondaryWeight = 2.0;

    return primaryWeight * primaryDist + secondaryWeight * secondaryDist;
}
```

**Scoring summary:**
- Lower score = better candidate
- `double.MaxValue` = eliminated (wrong direction or 0-size target)
- Primary weight balances how much "distance ahead" matters vs perpendicular offset
- The 2.0 secondary weight makes alignment matter — a window directly ahead beats one diagonally far off-axis
- Tie-breaking (Claude's discretion): when scores are equal within a small epsilon, prefer the window whose center is closer to the primary axis line through the origin (i.e., smallest secondary deviation)

**Design rationale for nearest-edge measurement:**
A 2000px-wide window directly to the right, touching the origin window's edge, gets a primaryDist near 0 and wins instantly. A small window far to the right gets a larger primaryDist. This matches the user's intuition: "the big window covering most of my screen to the right" should win over "a small distant window."

### Pattern 6: SetForegroundWindow + SendInput ALT Bypass (FOCUS-01)

**What:** Simulate ALT keydown + keyup via SendInput before calling SetForegroundWindow. This causes Windows to unlock foreground promotion. The process does not need to be the foreground process.

**Why this works:** Per `LockSetForegroundWindow` docs — pressing ALT causes Windows itself to enable calls to SetForegroundWindow. This is the mechanism AutoHotkey uses internally. It is described as "100% reliable" across multiple sources because the unlock is triggered by the OS on ALT key state change, not by process criteria.

```csharp
// Source: Official MS docs for SendInput + SetForegroundWindow
// Source: PowerToys PR #1282 (production use)
// Source: Gist github.com/Aetopia/1581b40f00cc0cadc93a0e8ccb65dc8c
// In FocusActivator.cs

[SupportedOSPlatform("windows5.0")]
internal static unsafe bool TryActivateWindow(nint hwnd)
{
    const ushort VK_MENU = 0x12; // ALT key

    // Build two INPUT events: ALT keydown and ALT keyup
    Span<INPUT> inputs = stackalloc INPUT[2];

    inputs[0] = new INPUT
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = VK_MENU,
                dwFlags = 0  // key down
            }
        }
    };

    inputs[1] = new INPUT
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = VK_MENU,
                dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP
            }
        }
    };

    // SendInput with CsWin32 pattern: pass span + sizeof(INPUT)
    PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());

    // Attempt to activate
    var targetHwnd = new HWND((void*)(IntPtr)hwnd);
    return PInvoke.SetForegroundWindow(targetHwnd);
}
```

**CsWin32 INPUT struct notes:**
- `INPUT_TYPE.INPUT_KEYBOARD` = 1 (generated enum)
- `INPUT._Anonymous_e__Union` is the union field name in CsWin32 generated code
- `KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP` = 0x0002 (generated enum from KEYBDINPUT docs)
- `PInvoke.SendInput(Span<INPUT>, int cbSize)` — CsWin32 generates a Span overload (Discussion #817)
- `VK_MENU = 0x12` — the virtual key code for ALT (left or right ALT); constant not generated by CsWin32, define as `const ushort`

**IMPORTANT:** Before adding `SendInput`, `SetForegroundWindow`, `GetForegroundWindow`, `MonitorFromPoint` to NativeMethods.txt, inspect the generated `.g.cs` files in `obj/Debug/net8.0/generated/` to confirm union field names. CsWin32 0.3.x may name the union differently than `Anonymous` — use the generated name.

### Pattern 7: Activation Result and Exit Codes (OUT-02)

**What:** The activation loop tries candidates in score order, falls through on failure, and produces the correct exit code.

```csharp
// In FocusActivator.cs (called from Program.cs)
internal static int ActivateBestCandidate(
    List<(WindowInfo window, double score)> rankedCandidates)
{
    if (rankedCandidates.Count == 0)
        return 1; // exit code 1: no candidates in direction

    bool atLeastOneTried = false;
    foreach (var (window, _) in rankedCandidates)
    {
        atLeastOneTried = true;
        bool activated = TryActivateWindow(window.Hwnd);
        if (activated)
            return 0; // exit code 0: success
        // else: elevated window or other failure — silently try next
    }

    // All candidates tried but none succeeded
    return atLeastOneTried ? 2 : 1;
}
```

**Exit code semantics (locked):**
- `0` — window found and focus switched successfully
- `1` — no candidates exist in the given direction
- `2` — candidates exist but all activation attempts failed (e.g., all elevated admin windows)

### Pattern 8: GetForegroundWindow and Foreground Window Exclusion

```csharp
// In NavigationService.cs
[SupportedOSPlatform("windows5.0")]
internal static unsafe List<(WindowInfo window, double score)> GetRankedCandidates(
    List<WindowInfo> allWindows,
    Direction direction)
{
    // Get origin from foreground window (or primary monitor center if null)
    var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();

    var (originX, originY) = GetOriginPoint(allWindows, fgHwnd);

    var candidates = allWindows
        // Exclude the currently focused window (locked decision)
        .Where(w => w.Hwnd != fgHwnd)
        .Select(w => (window: w, score: ScoreCandidate(originX, originY, w, direction)))
        .Where(x => x.score < double.MaxValue)    // eliminated candidates removed
        .OrderBy(x => x.score)
        .ToList();

    return candidates;
}
```

### Pattern 9: Program.cs Navigation Flow Integration

```csharp
// In Program.cs — navigation handler (full flow)
static int RunNavigation(string directionStr)
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
    {
        Console.Error.WriteLine("Error: This tool requires Windows Vista or later.");
        return 2;
    }

    var direction = DirectionExtensions.Parse(directionStr);
    if (direction is null)
    {
        Console.Error.WriteLine($"Error: Unknown direction '{directionStr}'. Use: left, right, up, down");
        return 2;
    }

    try
    {
        var enumerator = new WindowEnumerator();
        var (windows, _) = enumerator.GetNavigableWindows();

        var ranked = NavigationService.GetRankedCandidates(windows, direction.Value);
        return FocusActivator.ActivateBestCandidate(ranked);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 2;
    }
}
```

### Anti-Patterns to Avoid

- **Using `AttachThreadInput` instead of SendInput ALT bypass:** `AttachThreadInput` has known reentrancy risks and deadlock potential when the target thread is hung. The SendInput ALT approach has no such risk.
- **Center-to-center distance for large windows:** Penalizes large windows unfairly. Use nearest-edge distance as locked in CONTEXT.md.
- **Ignoring GetForegroundWindow returning NULL:** The docs explicitly state it "can be NULL in certain circumstances." Not handling NULL causes a NullReferenceException or invalid HWND.
- **Not excluding the foreground window from candidates:** Without exclusion, the tool may navigate to the current window itself.
- **Treating all SetForegroundWindow failures as error (exit 2):** When there are no candidates, return 1, not 2. Exit code 2 is only for "candidates exist but activation failed."
- **Not filtering by direction before scoring:** Scoring windows behind the origin is wasted work and may select wrong candidates. Filter to only "in-direction" candidates first.
- **Comparing double scores with ==:** Use an epsilon when tie-breaking rather than exact equality.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| INPUT struct layout for SendInput | Manual DllImport + struct definitions | CsWin32 NativeMethods.txt: `SendInput` | CsWin32 generates correct KEYBDINPUT layout including the anonymous union; manual struct layout on 64-bit requires careful padding |
| ALT-bypass logic | Custom foreground unlock mechanism | SendInput(VK_MENU) pattern | The ALT-causes-foreground-unlock behavior is documented OS-level; any other approach is less reliable |
| Distance from point to rectangle | Custom nearest-point algorithm | The 3-line `Math.Clamp` formula above | Clamp on both axes is the correct nearest-point-on-AABB formula; custom implementations often have edge case bugs on corners |

**Key insight:** The INPUT struct union layout has subtle alignment differences between 32-bit and 64-bit. CsWin32 generates the correct layout from Windows SDK metadata — never define it manually.

---

## Common Pitfalls

### Pitfall 1: INPUT Struct Union Name in CsWin32

**What goes wrong:** Code compiles but sets the wrong union member. CsWin32 0.3.x names the anonymous union `Anonymous` in the generated code for `INPUT`, but this can vary by CsWin32 version or if the metadata changes.
**Why it happens:** Win32 INPUT struct uses a `DUMMYUNIONNAME` anonymous union. CsWin32 names this differently than hand-rolled DllImport structs.
**How to avoid:** After adding `SendInput` to NativeMethods.txt, inspect the generated `.g.cs` file in `obj/Debug/net8.0/generated/`. Look for the `INPUT` struct — find the union field name and use it exactly. The pattern is `Anonymous = new INPUT._Anonymous_e__Union { ki = new KEYBDINPUT { ... } }`.
**Warning signs:** Compile error on `Anonymous` field; or the ALT keypress is not sent (SendInput returns 0 events inserted).

### Pitfall 2: SendInput UIPI Blocking (Silent Failure)

**What goes wrong:** `SendInput` returns 0 (failure) when invoked from a process at a lower integrity level than the target. Neither `GetLastError` nor the return value indicates UIPI as the cause — the failure is silent.
**Why it happens:** SendInput is subject to UIPI. Applications are only permitted to inject input into applications at an equal or lesser integrity level (official docs, updated 2025-07-01).
**How to avoid:** This situation arises when `focus.exe` runs as a standard user process and the AHK hotkey also runs as standard user (expected case). UIPI should not be an issue in normal use. If the focus tool itself runs elevated, it can inject into anything.
**Warning signs:** SendInput always returns 0 when invoked from AHK. Check process integrity levels.

### Pitfall 3: SetForegroundWindow Returns FALSE for Elevated Targets

**What goes wrong:** `SetForegroundWindow` returns FALSE when trying to bring an elevated (admin) window to the foreground from a standard process. The window does not activate.
**Why it happens:** UIPI: SetForegroundWindow has a specific exception allowing cross-integrity calls, but it may still fail for some elevated windows. (Per search results: "there is an explicit exception allowing the call to be made with no IL restrictions; it does, of course, have other restrictions.")
**How to avoid:** Check the return value of `SetForegroundWindow`. If FALSE, fall through to the next-best candidate (locked decision). Do not treat a single FALSE as exit code 2 — only return 2 if ALL candidates fail.
**Warning signs:** Navigating toward an admin-elevation window always skips it. This is expected and correct behavior.

### Pitfall 4: Foreground Window Is Not in the Enumerated List

**What goes wrong:** `GetForegroundWindow()` returns an HWND that does not appear in the `allWindows` list from Phase 1 enumeration. Exclusion logic `w.Hwnd != fgHwnd` still works (nothing to exclude), but the scoring origin may use incorrect bounds.
**Why it happens:** The foreground window might be a tool window, minimized window, or other filtered-out window (system tray, taskbar, etc.). These pass `IsWindowVisible` but fail the Alt+Tab filter.
**How to avoid:** Get bounds directly from `DwmGetWindowAttribute(GetForegroundWindow(), DWMWA_EXTENDED_FRAME_BOUNDS)` for the origin point — don't look it up in the enumerated list. If that fails, fall back to primary monitor center.
**Warning signs:** Origin point is (0, 0) unexpectedly; navigation from the desktop produces wrong results.

### Pitfall 5: MONITORINFOF_PRIMARY Not Generated by CsWin32

**What goes wrong:** Cannot check `(mi.dwFlags & MONITORINFOF_PRIMARY) != 0` because `MONITORINFOF_PRIMARY` is undefined.
**Why it happens:** CsWin32 does not auto-generate constants unless explicitly listed in NativeMethods.txt (confirmed via CsWin32 issue #1004).
**How to avoid:** Add `MONITORINFOF_PRIMARY` to NativeMethods.txt. Alternatively, define `private const uint MONITORINFOF_PRIMARY = 0x00000001u` directly — this value is stable (documented since Windows 2000).
**Warning signs:** Compile error `CS0103: The name 'MONITORINFOF_PRIMARY' does not exist in the current context`.

### Pitfall 6: No-Candidate vs All-Failed Exit Code Confusion

**What goes wrong:** Returns exit code 1 (no candidates) when candidates exist but all failed to activate. Or returns exit code 2 (error) when there are genuinely no windows in that direction.
**Why it happens:** Not tracking whether any candidates were found vs whether activation succeeded.
**How to avoid:** Check candidate list before the activation loop. If empty → return 1. If non-empty but all fail → return 2.
**Warning signs:** AutoHotkey script behaves incorrectly on boundary cases (hitting an edge of the screen, all visible windows are admin-elevated).

### Pitfall 7: AHK Invocation vs Terminal — SendInput Not Taking Effect

**What goes wrong:** `focus left` works from a terminal but fails to switch focus when invoked from AutoHotkey. The ALT bypass doesn't unlock foreground promotion.
**Why it happens:** When invoked from AHK, the process may not be the process that received the last input event. The timing between SendInput and SetForegroundWindow may also matter — the ALT events need to be processed before SetForegroundWindow is called.
**How to avoid:** The SendInput + SetForegroundWindow sequence should be synchronous in the same thread. Do not add sleeps or thread switches between the two calls. This is the STATE.md flagged validation concern — test specifically via AHK invocation.
**Warning signs:** Works in terminal, fails via AHK hotkey. Mentioned in STATE.md: "SendInput + ALT bypass must be validated specifically via AHK invocation."

---

## Code Examples

Verified patterns from official sources:

### GetForegroundWindow (returns NULL safely)

```csharp
// Source: GetForegroundWindow docs (updated 2025-07-01)
// Returns 0 (null HWND) when no window has focus (e.g., desktop is focused)
var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();
if (fgHwnd == 0)
{
    // fall back to primary monitor center
}
```

### SendInput ALT Keypress Pattern (CsWin32)

```csharp
// Source: SendInput docs, CsWin32 Discussion #817, Gist Aetopia/1581b40f00cc0cadc93a0e8ccb65dc8c
// After adding SendInput to NativeMethods.txt — inspect generated code for exact union name

const ushort VK_MENU = 0x12; // ALT — no CsWin32 constant for this

Span<INPUT> inputs = stackalloc INPUT[2];
inputs[0] = new INPUT
{
    type = INPUT_TYPE.INPUT_KEYBOARD,
    Anonymous = new INPUT._Anonymous_e__Union
    {
        ki = new KEYBDINPUT { wVk = VK_MENU, dwFlags = 0 }  // key down
    }
};
inputs[1] = new INPUT
{
    type = INPUT_TYPE.INPUT_KEYBOARD,
    Anonymous = new INPUT._Anonymous_e__Union
    {
        ki = new KEYBDINPUT { wVk = VK_MENU, dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP }
    }
};
PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());

// Immediately call SetForegroundWindow — same thread, no delay
bool activated = PInvoke.SetForegroundWindow(targetHwnd);
```

### Primary Monitor Center Fallback

```csharp
// Source: GetMonitorInfo docs (updated 2024-11-20), MonitorFromPoint docs
// MONITORINFOF_PRIMARY must be added to NativeMethods.txt
unsafe (double x, double y) GetPrimaryMonitorCenter()
{
    HMONITOR hm = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
    MONITORINFO mi = default;
    mi.cbSize = (uint)sizeof(MONITORINFO);
    if (PInvoke.GetMonitorInfo(hm, ref mi))
        return ((mi.rcMonitor.left + mi.rcMonitor.right) / 2.0,
                (mi.rcMonitor.top + mi.rcMonitor.bottom) / 2.0);
    return (0.0, 0.0);
}
```

### Nearest Point on Rectangle

```csharp
// No external source needed — Math.Clamp on AABB is a standard geometric identity
static (double x, double y) NearestPoint(double px, double py, WindowInfo w)
    => (Math.Clamp(px, w.Left, w.Right),
        Math.Clamp(py, w.Top, w.Bottom));
```

### Exit Code Flow in Program.cs

```csharp
// Source: REQUIREMENTS.md OUT-02 and locked decisions in CONTEXT.md
static int RunNavigation(string directionStr)
{
    // Parse direction
    var dir = DirectionExtensions.Parse(directionStr);
    if (dir is null) return 2;

    // Enumerate
    var enumerator = new WindowEnumerator();
    var (windows, _) = enumerator.GetNavigableWindows();

    // Score and rank
    var ranked = NavigationService.GetRankedCandidates(windows, dir.Value);
    if (ranked.Count == 0) return 1;  // no candidates in direction

    // Activate (fall through on elevated window failures)
    foreach (var (w, _) in ranked)
    {
        if (FocusActivator.TryActivateWindow(w.Hwnd))
            return 0;  // success
    }
    return 2;  // candidates existed but all failed
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `keybd_event()` for ALT bypass | `SendInput` with INPUT struct | Noted in STATE.md + PROJECT.md | `keybd_event` is documented as legacy; `SendInput` is the supported API for input synthesis |
| Center-to-center distance | Nearest-edge distance to target | CONTEXT.md locked decision | Eliminates unfair penalty for large nearby windows; better UX for multi-window layouts |
| Same-monitor-first preference | Pure virtual screen geometry | CONTEXT.md locked decision | Enables natural multi-monitor navigation; user's vertical monitor arrangement works naturally |

**Deprecated/outdated:**
- `keybd_event()`: Explicitly superseded by `SendInput` per STATE.md and PROJECT.md. CsWin32 generates `SendInput`; do not use `keybd_event`.
- `AttachThreadInput`: Works but carries deadlock and reentrancy risks. The SendInput ALT approach is cleaner and simpler.

---

## Open Questions

1. **CsWin32 INPUT union field name (MUST verify before coding)**
   - What we know: CsWin32 generates `INPUT._Anonymous_e__Union` for the union based on Discussion #817 examples; the keyboard union member is `ki`
   - What's unclear: Exact field names in CsWin32 0.3.269 for net8.0 — may differ from earlier versions
   - Recommendation: Add `SendInput` to NativeMethods.txt, build, then inspect `obj/Debug/net8.0/generated/.../Windows.Win32.PInvoke.USER32.dll.g.cs` and any INPUT struct file. Use the exact generated names. Do NOT code blindly against assumed names.

2. **SetForegroundWindow vs elevated windows — exact behavior**
   - What we know: SetForegroundWindow returns FALSE for elevated windows from standard process; UIPI has an explicit exception for SetForegroundWindow but may still fail
   - What's unclear: Whether the SendInput ALT bypass changes behavior for elevated targets specifically
   - Recommendation: Test with an elevated process (run Notepad as admin, try to navigate to it). Implement the fall-through logic regardless — it's the locked decision.

3. **`focus <direction>` argument shape in System.CommandLine 2.0**
   - What we know: System.CommandLine 2.0 supports `Argument<string>` with `ArgumentArity.ZeroOrOne`; the option `--debug` is already wired
   - What's unclear: Whether a positional argument and an option on the same root command interact cleanly in parse results
   - Recommendation: Test `focus left` and `focus --debug enumerate` both work after wiring. The `direction` argument and `--debug` option should be independent.

4. **KEYBD_EVENT_FLAGS enum in CsWin32 0.3.269**
   - What we know: `KEYEVENTF_KEYUP = 0x0002` is the documented value; CsWin32 likely generates `KEYBD_EVENT_FLAGS` enum
   - What's unclear: Exact generated enum name in CsWin32 0.3.269
   - Recommendation: After adding `SendInput` to NativeMethods.txt, check the generated code for the dwFlags type. If it's `KEYBD_EVENT_FLAGS`, use `KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP`. If it's raw `uint`, use `0x0002u` with a comment.

---

## Validation Architecture

> `workflow.nyquist_validation` is not set in `.planning/config.json` — this section is omitted.

---

## Sources

### Primary (HIGH confidence)

- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow — SetForegroundWindow restrictions; "foreground lock" conditions (updated 2025-10-06)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow — Returns NULL "in certain circumstances, such as when a window is losing activation" (updated 2025-07-01)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput — SendInput struct, UIPI blocking behavior, no GetLastError indication (updated 2025-07-01)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-input — INPUT struct layout, DUMMYUNIONNAME, INPUT_KEYBOARD = 1 (updated 2024-02-22)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-locksetforegroundwindow — "pressing the ALT key causes Windows itself to enable calls to SetForegroundWindow"
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmonitorinfoa — GetMonitorInfo, MONITORINFO.dwFlags for primary detection (updated 2024-11-20)
- https://github.com/microsoft/CsWin32/issues/1004 — MONITORINFOF_PRIMARY not generated; must add to NativeMethods.txt explicitly
- https://github.com/microsoft/CsWin32/discussions/817 — SendInput with CsWin32; `Span<INPUT>` overload pattern; `INPUT._Anonymous_e__Union` usage

### Secondary (MEDIUM confidence)

- https://gist.github.com/Aetopia/1581b40f00cc0cadc93a0e8ccb65dc8c — SendInput ALT bypass technique; described as "100% reliable"; verified consistent with MS docs on LockSetForegroundWindow
- https://github.com/microsoft/PowerToys/pull/1282 — PowerToys uses SendInput as SetForegroundWindow workaround in production code (production validation of pattern)

### Tertiary (LOW confidence)

- Training data + spatial navigation pattern analysis for scoring formula weights (1.0 primary, 2.0 secondary) — these values are Claude's discretion and need empirical validation during verification
- twm (github.com/Tom94/twm) directional scoring — source not directly inspected; confirms ecosystem uses geometry-based scoring but exact formula unknown

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — All required Win32 APIs verified via official docs; CsWin32 discussion confirms INPUT struct usage; no new NuGet packages needed
- Architecture: HIGH — Win32 API signatures verified; exit code contract from REQUIREMENTS.md; nearest-point geometry is mathematically correct
- Pitfalls: HIGH for known Win32 issues (NULL foreground, UIPI, INPUT union names); MEDIUM for specific AHK invocation behavior (STATE.md flags this as the key validation concern)
- Scoring weights: LOW (Claude's discretion) — the formula structure is solid but specific weight values (1.0, 2.0) need empirical tuning during phase verification

**Research date:** 2026-02-27
**Valid until:** 2026-05-27 (Win32 APIs stable; CsWin32 0.3.x line stable; geometry math timeless)
