# Phase 10: Grid Infrastructure and Modifier Wiring - Research

**Researched:** 2026-03-02
**Domain:** Win32 low-level keyboard hook extension, C# daemon architecture, grid math
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### TAB key behavior
- TAB is suppressed (eaten) when CAPS is held — not forwarded to the focused application
- Bare TAB (CAPS not held) passes through to the app unchanged (MODE-04)
- Releasing CAPS exits all modes (move/grow/shrink) regardless of whether TAB/SHIFT/CTRL are still held — CAPS is always the master switch
- If user is navigating (CAPS+direction) and presses TAB mid-stream, smoothly transition to move mode — navigation state resets, move mode activates without needing to release and re-hold CAPS

#### Grid step defaults
- Separate fractions per axis: `gridFractionX` (default 16) and `gridFractionY` (default 12)
- This gives nearly square grid cells on typical 16:9/16:10 monitors (~120x87px on 1080p)
- `snapTolerancePercent` default 10 — windows within 10% of a grid step from a grid line are considered "close enough"
- Grid computed per-monitor from that monitor's work area (GRID-02)

#### Snap-first behavior
- First operation snaps the window to the nearest grid line on the axis of operation
- Exception: if the window is within the snap tolerance (10%), snap AND step in one press — avoids imperceptible micro-moves
- Snap-first applies to ALL operations: move, grow, and shrink (consistent across modes)
- Move operations: only snap the movement axis (pressing Right snaps X only, not Y)
- Resize operations: only snap the affected edge (growing right edge only snaps that edge to grid)

#### Modifier detection
- Left Shift (VK_LSHIFT) triggers grow mode; Right Shift does NOT
- Left Ctrl (VK_LCONTROL) triggers shrink mode; Right Ctrl does NOT
- TAB (VK_TAB) triggers move mode when CAPS is held

### Claude's Discretion
- How to wire TAB interception into the existing KeyboardHookHandler (follow existing direction key pattern)
- How to propagate mode information through CapsLockMonitor callbacks (new callback signatures or mode enum)
- GridCalculator service design (pure computation — work area dimensions + fractions)
- Config schema evolution (how to add gridFractionX/Y alongside existing config properties)

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| MODE-01 | User holds CAPS+TAB to activate window move mode | TAB interception pattern in KeyboardHookHandler; _tabHeld state flag; VK_TAB = 0x09 |
| MODE-02 | User holds CAPS+LSHIFT to activate window grow mode | GetKeyState(VK_LSHIFT = 0xA0) in hook callback; already done for generic VK_SHIFT — upgrade to left-side check |
| MODE-03 | User holds CAPS+LCTRL to activate window shrink mode | GetKeyState(VK_LCONTROL = 0xA2) in hook callback; same upgrade pattern as MODE-02 |
| MODE-04 | Normal TAB key behavior preserved when CAPS is not held | Existing `_capsLockHeld` guard on TAB block (same guard as direction keys) |
| GRID-01 | Grid step is 1/Nth of monitor dimension (configurable gridFraction, default 16) | GridCalculator pure function: stepX = rcWork.Width / gridFractionX; stepY = rcWork.Height / gridFractionY |
| GRID-02 | Grid computed per-monitor from that monitor's work area | GetMonitorInfo.rcWork (not rcMonitor) — already used in existing codebase pattern |
| GRID-03 | Misaligned windows snap to nearest grid line on first operation | Snap math: nearestLine = round(pos / step) * step; snap-first with tolerance check |
| GRID-04 | Snap tolerance configurable (snapTolerancePercent, default 10) | snapTolerancePercent stored in FocusConfig; tolerance = step * snapTolerancePercent / 100 |
</phase_requirements>

## Summary

Phase 10 extends the existing Win32 keyboard hook pipeline with two new key intercepts (TAB and left-side modifiers) and adds a pure-math GridCalculator service. The work breaks into three independent clusters: (1) TAB interception in `KeyboardHookHandler`, (2) modifier-qualified mode routing in `CapsLockMonitor`, and (3) the `GridCalculator` service plus `FocusConfig` schema additions.

The existing architecture is a near-perfect template. TAB interception follows the same `IsDirectionKey` / `_capsLockHeld` guard pattern already used for direction keys and number keys. The only new conceptual addition is a `WindowMode` enum that replaces the current unqualified `onDirectionKeyDown(string)` callback with a mode-qualified signature. The `GridCalculator` is a stateless pure function that takes work area dimensions and config fractions and returns step values — no Win32 state needed at calculation time, only at call time (fetching the HMONITOR → rcWork).

The left-side modifier check is a one-line upgrade: replace `GetKeyState(VK_SHIFT)` with `GetKeyState(VK_LSHIFT)` and `GetKeyState(VK_CONTROL)` with `GetKeyState(VK_LCONTROL)`. This is fully supported by Win32 `GetKeyState` in a low-level keyboard hook context and is confirmed by official Microsoft documentation.

**Primary recommendation:** Implement TAB/modifier wiring as a direct extension of the existing hook→channel→monitor pipeline using a new `WindowMode` enum on the `KeyEvent` record, and implement `GridCalculator` as a static class with pure methods consuming `RECT rcWork` from `GetMonitorInfo`.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Windows.CsWin32 | 0.3.269 | P/Invoke for GetKeyState, GetMonitorInfo, VK constants | Already in project; generates type-safe wrappers |
| System.Threading.Channels | .NET 8 BCL | Hook → monitor async event pipeline | Already in project; established pattern |
| System.Text.Json | .NET 8 BCL | FocusConfig deserialization with kebab-case enums | Already in project; established pattern |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Windows.Win32 (generated) | auto | MONITORINFO, HMONITOR, RECT types | Every GetMonitorInfo call |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| GetKeyState in hook callback | GetAsyncKeyState | Same result in hook context; GetKeyState is canonical for hook callbacks |
| GetKeyState(VK_LSHIFT) | Checking KBDLLHOOKSTRUCT.vkCode directly on Shift keydown | Hook struct only tells you about the key being pressed NOW, not state of other keys |

**Installation:** No new packages — all required libraries are already referenced.

## Architecture Patterns

### Recommended Project Structure
```
focus/Windows/Daemon/
├── KeyboardHookHandler.cs   # Add VK_TAB, VK_LSHIFT, VK_LCONTROL constants; add _tabHeld; add TAB interception block
├── KeyEvent.cs              # Add WindowMode property (Move/Grow/Shrink/Navigate/None)
├── CapsLockMonitor.cs       # Replace onDirectionKeyDown(string) with onDirectionKeyDown(string, WindowMode)
├── DaemonCommand.cs         # Update CapsLockMonitor constructor callback signature
focus/Windows/
├── FocusConfig.cs           # Add GridFractionX, GridFractionY, SnapTolerancePercent properties
├── GridCalculator.cs        # NEW: pure static class for grid step + snap math
```

### Pattern 1: TAB Interception (mirroring direction key pattern)

**What:** Add a TAB intercept block in `HookCallback` that sets `_tabHeld` and writes a `KeyEvent` with `WindowMode.Move` when CAPS is held. When CAPS is NOT held, pass TAB through via `CallNextHookEx`.

**When to use:** Directly parallels the existing direction key interception block. Insert BEFORE the direction key check.

**Example:**
```csharp
// TAB key (VK_TAB = 0x09): suppress when CAPS held, pass through otherwise (MODE-01, MODE-04)
private const uint VK_TAB      = 0x09;
private const uint VK_LSHIFT   = 0xA0;  // Left Shift only (MODE-02)
private const uint VK_LCONTROL = 0xA2;  // Left Ctrl only (MODE-03)

// In HookCallback, after LLKHF_INJECTED filter:
if (kbd->vkCode == VK_TAB)
{
    bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;

    if (!_capsLockHeld)
        return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);  // MODE-04: bare TAB passes through

    // CAPS is held — track tab state and suppress
    _tabHeld = isKeyDown;

    // Write a KeyEvent so CapsLockMonitor can fire mode-change callbacks
    _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, WindowMode.Move));
    return (LRESULT)1;  // suppress
}
```

### Pattern 2: Left-Side Modifier Detection

**What:** In the direction key interception block, replace the generic `VK_SHIFT`/`VK_CONTROL` checks with `VK_LSHIFT` (0xA0) and `VK_LCONTROL` (0xA2). Derive `WindowMode` from the modifier state and embed it in `KeyEvent`.

**When to use:** Applied to every direction key event written to the channel when CAPS is held.

**Example:**
```csharp
// Inside direction key interception (CAPS held path):
bool lShiftHeld = (PInvoke.GetKeyState((int)VK_LSHIFT)   & 0x8000) != 0;
bool lCtrlHeld  = (PInvoke.GetKeyState((int)VK_LCONTROL)  & 0x8000) != 0;
bool altHeld    = ((uint)kbd->flags & LLKHF_ALTDOWN) != 0;

WindowMode mode = (_tabHeld, lShiftHeld, lCtrlHeld) switch
{
    (true, _, _)  => WindowMode.Move,    // TAB overrides other modifiers
    (_, true, _)  => WindowMode.Grow,    // LSHIFT = grow
    (_, _, true)  => WindowMode.Shrink,  // LCTRL = shrink
    _             => WindowMode.Navigate // bare CAPS direction = navigate (existing behavior)
};

_channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, lShiftHeld, lCtrlHeld, altHeld, mode));
```

### Pattern 3: KeyEvent WindowMode Field

**What:** Add a `WindowMode` enum and a `Mode` field to the `KeyEvent` record with a default of `WindowMode.Navigate` to preserve backward compatibility with existing channel writes (CAPS and number key events).

**When to use:** Replacing the existing unqualified `onDirectionKeyDown(string)` with mode-qualified routing.

**Example:**
```csharp
internal enum WindowMode { Navigate, Move, Grow, Shrink }

internal readonly record struct KeyEvent(
    uint VkCode,
    bool IsKeyDown,
    uint Timestamp,
    bool ShiftHeld = false,
    bool CtrlHeld = false,
    bool AltHeld = false,
    WindowMode Mode = WindowMode.Navigate);
```

### Pattern 4: CapsLockMonitor Mode-Qualified Callback

**What:** Extend `HandleDirectionKeyEvent` to pass the `WindowMode` from the event to the direction callback. Change `Action<string>? _onDirectionKeyDown` to `Action<string, WindowMode>?`.

**When to use:** The consumer (`OverlayOrchestrator` for Phase 10, `WindowMoveService` for Phase 11) needs to know the mode to route correctly.

**Example:**
```csharp
// New callback signature in CapsLockMonitor constructor
Action<string, WindowMode>? onDirectionKeyDown = null

// In HandleDirectionKeyEvent:
_onDirectionKeyDown?.Invoke(directionName, evt.Mode);
```

### Pattern 5: GridCalculator (pure static class)

**What:** A static class with pure methods. Takes `RECT rcWork` and config fractions, returns grid step in pixels and nearest grid line for snap.

**When to use:** Called by Phase 11 window move/resize operations. Phase 10 only creates and tests the service.

**Example:**
```csharp
internal static class GridCalculator
{
    /// <summary>
    /// Computes grid step size in physical pixels for the given monitor work area.
    /// </summary>
    public static (int StepX, int StepY) GetGridStep(RECT rcWork, int gridFractionX, int gridFractionY)
    {
        int width  = rcWork.right  - rcWork.left;
        int height = rcWork.bottom - rcWork.top;
        int stepX  = Math.Max(1, width  / gridFractionX);
        int stepY  = Math.Max(1, height / gridFractionY);
        return (stepX, stepY);
    }

    /// <summary>
    /// Returns the nearest grid line for a given position offset from the work area origin.
    /// </summary>
    public static int NearestGridLine(int pos, int origin, int step)
    {
        if (step <= 0) return pos;
        int offset = pos - origin;
        int nearest = (int)Math.Round((double)offset / step) * step;
        return origin + nearest;
    }

    /// <summary>
    /// Returns true if pos is within snapTolerance pixels of the nearest grid line.
    /// </summary>
    public static bool IsAligned(int pos, int origin, int step, int snapTolerancePx)
    {
        int nearest = NearestGridLine(pos, origin, step);
        return Math.Abs(pos - nearest) <= snapTolerancePx;
    }

    /// <summary>
    /// Computes snap tolerance in pixels from a percentage of the grid step.
    /// </summary>
    public static int GetSnapTolerancePx(int step, int snapTolerancePercent)
        => Math.Max(1, step * snapTolerancePercent / 100);
}
```

### Pattern 6: GetMonitorInfo rcWork Retrieval

**What:** Call `GetMonitorInfo` to get `rcWork` (work area excluding taskbar) for the monitor containing the foreground window. `rcWork` is in virtual-screen coordinates (physical pixels for non-DPI-scaled contexts).

**When to use:** Every grid computation call needs the work area of the target monitor.

**Example:**
```csharp
// Source: MonitorHelper existing pattern + GetMonitorInfo.rcWork
private static unsafe RECT GetWorkArea(HWND hwnd)
{
    var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = default;
    mi.cbSize = (uint)sizeof(MONITORINFO);
    if (PInvoke.GetMonitorInfo(hMon, ref mi))
        return mi.rcWork;
    // Fallback: full virtual screen rect
    return new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
}
```

### Pattern 7: FocusConfig Schema Addition

**What:** Add three new properties to `FocusConfig` with defaults. JSON deserialization already handles missing keys by returning C# default values — no migration needed.

**When to use:** Properties with defaults on `FocusConfig`; read via `FocusConfig.Load()` at operation time.

**Example:**
```csharp
// In FocusConfig class
public int GridFractionX { get; set; } = 16;
public int GridFractionY { get; set; } = 12;
public int SnapTolerancePercent { get; set; } = 10;
```

JSON keys are camelCase by default with `PropertyNameCaseInsensitive = true`, so `"gridFractionX"`, `"gridFractionY"`, `"snapTolerancePercent"` all work.

### Anti-Patterns to Avoid

- **Using generic VK_SHIFT (0x10) instead of VK_LSHIFT (0xA0):** Would make Right Shift trigger grow mode, violating the locked decision. Always use the left-side VK codes.
- **Deriving mode from KBDLLHOOKSTRUCT.vkCode for modifier state:** The struct only describes the key being processed NOW, not the state of other held keys. Use `GetKeyState` for concurrent modifier detection.
- **Calling GetMonitorInfo in the hook callback:** The hook callback has a 1000ms total budget; Win32 calls must remain minimal. Defer all GetMonitorInfo calls to the worker or STA thread.
- **Using rcMonitor instead of rcWork:** rcMonitor includes the taskbar area. Grid origin and boundary math must use rcWork so windows are never moved under the taskbar.
- **Storing _tabHeld as a Channel event rather than real-time state:** TAB held state must be tracked in `_tabHeld` on `KeyboardHookHandler` in real-time (like `_capsLockHeld`) so that the NEXT direction key event reads the correct mode immediately.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Monitor work area retrieval | Custom DPI/display enumeration | GetMonitorInfo.rcWork via existing CsWin32 | Already in NativeMethods.txt; existing pattern in OverlayOrchestrator.ClampToMonitor |
| Left vs right modifier detection | Parsing scan codes or extended key flags | GetKeyState(VK_LSHIFT) / GetKeyState(VK_LCONTROL) | Official Win32 API; confirmed by Microsoft docs; already called in existing hook |
| Nearest grid line rounding | Custom integer math | `(int)Math.Round((double)offset / step) * step` | Standard banker's-rounding-avoidance pattern; one line |
| Config file schema evolution | Custom migration or versioning | Add properties with C# defaults; JSON ignores unknown | `PropertyNameCaseInsensitive = true` already handles this; missing keys return C# defaults |

**Key insight:** Every Win32 API needed in this phase is already listed in `NativeMethods.txt` and already called somewhere in the existing codebase. No new P/Invoke declarations are required.

## Common Pitfalls

### Pitfall 1: _tabHeld Out-of-Sync with _capsLockHeld Reset
**What goes wrong:** When CAPS is released, `_capsLockHeld` resets to false, but `_tabHeld` is not cleared. On the next CAPS hold, TAB appears "already held" if the physical TAB key was never released between sessions.
**Why it happens:** TAB-up event only arrives while CAPS is held (suppressed by us), so TAB-up is consumed correctly. However, if CAPS is released before TAB, the TAB-up event passes through to the app (CAPS not held = no suppression) but `_tabHeld` is never cleared in the hook.
**How to avoid:** Clear `_tabHeld = false` in the CAPS release handler (when `_capsLockHeld` is set to false). Also clear in `ResetState()` on `CapsLockMonitor` for sleep/wake recovery.
**Warning signs:** Move mode triggers on first direction key press even when TAB is not physically held.

### Pitfall 2: Mode Determined at Direction Key Time, Not TAB Time
**What goes wrong:** If mode is read from `_tabHeld` at the moment TAB is pressed but direction key events carry no mode, the consumer must re-query live state — which is unreliable across thread boundaries.
**Why it happens:** CapsLockMonitor runs on a worker thread; by the time it processes a direction KeyEvent, hook state may have changed.
**How to avoid:** Embed `WindowMode` directly in the `KeyEvent` record at write time in the hook callback. The consumer reads mode from the immutable event, not from live hook state.
**Warning signs:** Mode flickering when TAB and direction keys are pressed in rapid succession.

### Pitfall 3: CAPS+TAB System Interaction (Empirically Unknown)
**What goes wrong:** CAPS+TAB may trigger system-level Alt+Tab-style behavior or focus cycle behavior before the hook can suppress it.
**Why it happens:** Certain chords are handled by the OS before WH_KEYBOARD_LL fires. CAPS+TAB is not a documented system shortcut but empirical testing is required.
**How to avoid:** Test CAPS+TAB manually at Phase 10 start (before writing suppression logic) to verify no system interference. STATE.md records this as a blocker concern.
**Warning signs:** Focus jumps to another window when CAPS+TAB is pressed, even before suppression code is written.

### Pitfall 4: rcWork Coordinates Are Virtual-Screen, Not Monitor-Local
**What goes wrong:** Grid math subtracts `rcWork.left`/`rcWork.top` but treats the result as if it were zero-based per-monitor. On a secondary monitor at X=1920, the work area left = 1920, not 0.
**Why it happens:** `MONITORINFO.rcWork` uses virtual screen coordinates (global coordinate space spanning all monitors). Grid lines must be computed relative to `rcWork.left` and `rcWork.top` as the origin.
**How to avoid:** Always compute `offset = windowPos - rcWork.origin` before grid arithmetic, and restore `gridLine + rcWork.origin` after. The `GridCalculator.NearestGridLine(int pos, int origin, int step)` signature enforces this.
**Warning signs:** Windows on secondary monitors snap to incorrect positions offset by the monitor's virtual-screen position.

### Pitfall 5: Snap Tolerance Percent Applied to Wrong Step Axis
**What goes wrong:** `snapTolerancePercent` is applied to a combined or wrong-axis step when computing tolerance for X vs Y separately.
**Why it happens:** `gridFractionX` and `gridFractionY` produce different step values, so tolerance in pixels differs per axis.
**How to avoid:** Compute `snapTolerancePxX = stepX * snapTolerancePercent / 100` and `snapTolerancePxY = stepY * snapTolerancePercent / 100` separately. Use axis-specific tolerance in snap checks.
**Warning signs:** Windows snapping differently depending on whether a horizontal or vertical operation is used.

## Code Examples

Verified patterns from official sources and existing codebase:

### GetKeyState for Left-Side Modifiers (Win32 Official)
```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
// VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_TAB = 0x09
private const uint VK_TAB      = 0x09;
private const uint VK_LSHIFT   = 0xA0;
private const uint VK_LCONTROL = 0xA2;

// In hook callback — identical pattern to existing VK_SHIFT/VK_CONTROL checks:
bool lShiftHeld = (PInvoke.GetKeyState((int)VK_LSHIFT)   & 0x8000) != 0;
bool lCtrlHeld  = (PInvoke.GetKeyState((int)VK_LCONTROL)  & 0x8000) != 0;
```

### GetMonitorInfo rcWork (existing codebase pattern, already in ClampToMonitor)
```csharp
// Source: existing OverlayOrchestrator.ClampToMonitor + MONITORINFO docs
// https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo
private static unsafe RECT GetWorkArea(HWND hwnd)
{
    var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = default;
    mi.cbSize = (uint)sizeof(MONITORINFO);
    if (PInvoke.GetMonitorInfo(hMon, ref mi))
        return mi.rcWork;
    return default;
}
```

### Grid Step Math
```csharp
// No external source — pure integer arithmetic, verified against 1080p example:
// 1920 / 16 = 120px, 1080 / 12 = 90px — nearly square cells on 16:9
int stepX = Math.Max(1, workAreaWidth  / config.GridFractionX);
int stepY = Math.Max(1, workAreaHeight / config.GridFractionY);
```

### Snap-First Logic
```csharp
// Snap-first with tolerance: if within tolerance, snap+step; otherwise snap only
int nearestX = GridCalculator.NearestGridLine(windowLeft, rcWork.left, stepX);
int distToSnap = Math.Abs(windowLeft - nearestX);
int tolerancePx = GridCalculator.GetSnapTolerancePx(stepX, config.SnapTolerancePercent);

if (distToSnap <= tolerancePx)
{
    // Within tolerance: snap AND step in one press
    newLeft = nearestX + stepX * directionSign;
}
else
{
    // Outside tolerance: snap only (first press aligns, second press moves)
    newLeft = nearestX;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Generic VK_SHIFT check | VK_LSHIFT (0xA0) check | This phase | Right Shift no longer triggers grow mode |
| onDirectionKeyDown(string) | onDirectionKeyDown(string, WindowMode) | This phase | Consumer knows move/grow/shrink/navigate without querying live state |
| No grid service | GridCalculator static class | This phase | Phase 11 can call grid math without Win32 state coupling |

**Deprecated/outdated:**
- `ShiftHeld`/`CtrlHeld` in `KeyEvent`: These were generic shift/ctrl flags. After this phase, they become `LShiftHeld`/`LCtrlHeld` (or the `Mode` field makes them redundant for most consumers). The planner should decide whether to rename or deprecate — either approach works as long as the hook writes left-side values.

## Open Questions

1. **CAPS+TAB system interaction (empirical unknown)**
   - What we know: CAPS+TAB is not a documented Windows system shortcut. WH_KEYBOARD_LL fires before key dispatch in most cases.
   - What's unclear: Whether any accessibility feature, Alt-Tab variant, or third-party software intercepts CAPS+TAB before our hook at high priority.
   - Recommendation: Manual test before writing suppression code (as noted in STATE.md blockers). If system interference is detected, the hook can return 1 immediately without writing to the channel to suppress the chord cleanly.

2. **`ShiftHeld`/`CtrlHeld` field naming after upgrade**
   - What we know: `KeyEvent` currently has `ShiftHeld` (generic) and `CtrlHeld` (generic).
   - What's unclear: Whether to rename to `LShiftHeld`/`LCtrlHeld` or keep generic names but change the check in the hook.
   - Recommendation: Rename to `LShiftHeld`/`LCtrlHeld` in `KeyEvent` to make the contract explicit. This is a small record change with no runtime risk.

3. **CapsLockMonitor `onDirectionKeyDown` signature change impact on OverlayOrchestrator**
   - What we know: `OverlayOrchestrator.OnDirectionKeyDown(string direction)` currently handles navigate mode only.
   - What's unclear: Whether Phase 10 should add the mode parameter to `OverlayOrchestrator` now (routing navigate to existing logic, ignoring move/grow/shrink) or defer that wiring to Phase 11.
   - Recommendation: Add the mode parameter to `OverlayOrchestrator.OnDirectionKeyDown(string, WindowMode)` in Phase 10 and route `Navigate` to existing logic, `Move`/`Grow`/`Shrink` to no-ops. This keeps the interface contract stable for Phase 11 to fill in.

## Validation Architecture

> `workflow.nyquist_validation` is not set in config.json — section skipped per instructions.

*(config.json has no `nyquist_validation` key under `workflow`; field defaults to absent/false)*

## Sources

### Primary (HIGH confidence)
- https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes — VK_TAB (0x09), VK_LSHIFT (0xA0), VK_RSHIFT (0xA1), VK_LCONTROL (0xA2), VK_RCONTROL (0xA3) confirmed with hex values
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo — rcWork vs rcMonitor distinction; rcWork excludes taskbar; both use virtual-screen coordinates
- Existing codebase (`KeyboardHookHandler.cs`, `CapsLockMonitor.cs`, `FocusConfig.cs`, `MonitorHelper.cs`, `OverlayOrchestrator.cs`) — all patterns verified by direct code read

### Secondary (MEDIUM confidence)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeystate — GetKeyState supports VK_LSHIFT/VK_LCONTROL for left-side distinction; confirmed by official docs

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all Win32 APIs already in NativeMethods.txt and project
- Architecture: HIGH — TAB/modifier patterns are direct extensions of existing, working code
- GridCalculator: HIGH — pure integer math; no Win32 edge cases
- Pitfalls: HIGH for rcWork origin (confirmed by MONITORINFO docs); MEDIUM for CAPS+TAB system interaction (empirically unknown, flagged in STATE.md)

**Research date:** 2026-03-02
**Valid until:** 2026-06-02 (stable Win32 APIs; CsWin32 version pinned in csproj)
