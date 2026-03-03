# Pitfalls Research

**Domain:** Win32 window management — grid-snapped move/resize added to existing keyboard hook daemon (v3.1)
**Researched:** 2026-03-02
**Confidence:** HIGH (majority verified against official Microsoft documentation and existing codebase inspection)

---

## About This Document

This document covers pitfalls specific to **v3.1: adding window move and resize**. It supersedes the prior v2.0 edition by preserving all previously documented pitfalls (Section C) and adding the new v3.1-specific section (Section B).

Pitfalls already solved in the codebase are marked "(already mitigated)" and retained for regression-test reference only.

---

## Section A: v3.1 New Pitfalls — Window Move/Resize, New Modifiers, Grid, Overlay Updates

---

### Pitfall A-1: MoveWindow/SetWindowPos Ignores Requests on Maximized Windows — No Error Returned

**What goes wrong:**
Calling `MoveWindow` or `SetWindowPos` on a maximized window succeeds (returns nonzero) but the window stays maximized and does not move. The call is silently discarded. If the move/resize code does not detect the maximized state before calling, the user presses CAPS+TAB+direction and nothing visibly happens, creating a confusing "it silently failed" experience.

**Why it happens:**
Windows treats maximized windows specially: their position is owned by the window manager's maximize placement logic. Sending a `MoveWindow` call while `IsZoomed` is true triggers an internal path that ignores the coordinates. The function returns success because no error occurred — the request was simply overridden by the window's own state. `MoveWindow` documentation says "desktop apps only" and does not document that maximized state blocks the call.

**How to avoid:**
Before moving or resizing, detect maximized state:

```csharp
// Check IsZoomed (maximized) and SW_SHOWMINIMIZED (minimized) — both block MoveWindow
SHOW_WINDOW_CMD placement = PInvoke.GetWindowPlacement(hwnd, out var wp) ? wp.showCmd : SHOW_WINDOW_CMD.SW_NORMAL;

if (placement == SHOW_WINDOW_CMD.SW_MAXIMIZE || placement == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED)
{
    // Option A: restore first, then move.
    PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
    // ... now call MoveWindow/SetWindowPos with new coordinates
}
else if (placement == SHOW_WINDOW_CMD.SW_MINIMIZE || placement == SHOW_WINDOW_CMD.SW_SHOWMINIMIZED)
{
    // Silently no-op — moving a minimized window is not useful for this feature
    return;
}
```

For "move maximized window" semantics, restore-then-move is the correct pattern. Confirm the move completed by reading back `GetWindowRect` and comparing.

**Warning signs:**
- CAPS+TAB+direction on a maximized window produces no visible change and no error log
- MoveWindow returns `true` but the window position is unchanged

**Phase to address:**
Phase 1 (Window move implementation). Add maximized/minimized state detection as the first guard in the move handler, before any grid math.

---

### Pitfall A-2: Using DwmGetWindowAttribute Bounds for MoveWindow Coordinates — Window Shrinks on Each Move

**What goes wrong:**
The existing codebase correctly uses `DWMWA_EXTENDED_FRAME_BOUNDS` (DWM visible bounds) for overlay positioning and navigation scoring. However, those bounds **cannot** be directly passed to `MoveWindow`. `DWMWA_EXTENDED_FRAME_BOUNDS` strips the invisible 8px resize shadow from each edge. If those coordinates are used as the new position in `MoveWindow`, the window loses 8px on each side every time it is moved, visually shrinking by ~16px per move operation. The effect is progressive: repeated moves cause the window to drift and shrink.

**Why it happens:**
`GetWindowRect` returns the full window rect including the invisible shadow border. `DWMWA_EXTENDED_FRAME_BOUNDS` returns the visible content rect (shadow excluded). `MoveWindow` and `SetWindowPos` operate in `GetWindowRect` coordinate space — they expect the full rect including shadow. If you feed them the trimmed DWM bounds, you are specifying a position and size that excludes the shadow the window will still render, causing it to appear at the wrong position with the wrong size.

**How to avoid:**
Use `GetWindowRect` for the current position when computing move/resize deltas. Use `DWMWA_EXTENDED_FRAME_BOUNDS` only for display (overlay positioning, navigation scoring). Maintain the delta between the two at measurement time:

```csharp
RECT fullRect = default;
PInvoke.GetWindowRect(hwnd, out fullRect);

RECT visibleRect = default;
PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, ...);

// Shadow offsets (typically ~8px on each side on Windows 10/11):
int shadowLeft   = visibleRect.left   - fullRect.left;   // positive value, e.g., 7
int shadowTop    = visibleRect.top    - fullRect.top;     // 0 on Windows 11 with rounded corners
int shadowRight  = fullRect.right     - visibleRect.right;
int shadowBottom = fullRect.bottom    - visibleRect.bottom;

// For a grid move of +gridStep in X, the new full rect left = fullRect.left + gridStep
// Do NOT use visibleRect.left + gridStep as the new X for MoveWindow
```

**Warning signs:**
- Window appears to shrink slightly after each CAPS+TAB+direction press
- After several moves, window is noticeably smaller than it started
- Window position drifts relative to expected grid alignment

**Phase to address:**
Phase 1 (Window move implementation). Establish which rect is used for what purpose in a code comment at the top of the move handler before writing any grid math.

---

### Pitfall A-3: GetWindowRect Coordinate Space Differs from SetWindowPos Coordinate Space for External Windows

**What goes wrong:**
When the daemon calls `GetWindowRect` on the foreground window to get current position, the returned coordinates are in the coordinate space of the caller (the daemon process), not the target window. If the daemon is declared `PerMonitorV2` but the target window is DPI-unaware or System-DPI-aware, Windows virtualizes the coordinates returned to the daemon. The daemon receives scaled coordinates, computes a grid step, then calls `SetWindowPos` with coordinates in the daemon's coordinate space — but the target window's message handler receives the values in its own space. The window ends up at the wrong position.

**Why it happens:**
`GetWindowRect` returns coordinates that are DPI-adjusted based on the calling process's DPI awareness context, not the window's own context. A PerMonitorV2 process calling `GetWindowRect` on a System-DPI-aware window on a 150% monitor will get physical pixel coordinates. But `SetWindowPos` targeting an external window sends coordinates to the window's own WM_WINDOWPOSCHANGING handler, which may receive virtualized values. This is one of the most confusing DPI edge cases in Win32.

**How to avoid:**
Use `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` before calling `GetWindowRect` to ensure you always get physical pixel coordinates. Then use `LogicalToPhysicalPointForPerMonitorDPI` if you need to convert. Alternatively, use `SetWindowPos` with `SWP_ASYNCWINDOWPOS` flag which posts the message asynchronously so the target window processes it in its own DPI context.

The safest pattern for this daemon: always work in physical pixels (the daemon is already PerMonitorV2). Call `GetWindowRect` and `SetWindowPos` from the same STA thread with unchanged DPI context. Verify on a dual-monitor setup with mixed DPI using a DPI-unaware target (e.g., older 32-bit apps, legacy dialogs).

**Warning signs:**
- Window moves correctly on 100% DPI monitor but ends up at wrong position on 150% DPI monitor
- Move distance is 1.5x or 0.67x expected amount on secondary monitor
- Effect is consistent (always wrong by the same factor)

**Phase to address:**
Phase 1 (Window move implementation). Include a multi-DPI integration test scenario in the acceptance criteria.

---

### Pitfall A-4: UIPI Blocks SetWindowPos on Elevated and UIAccess Windows — Silent Failure

**What goes wrong:**
Calling `SetWindowPos` or `MoveWindow` on a window owned by an elevated process (Task Manager, administrator-run applications, UAC consent dialog) fails with `ERROR_ACCESS_DENIED` (5) when the daemon runs at medium integrity level (the default). The function returns `false`, `GetLastError` returns 5, but without checking the return value, the failure is invisible. The user presses CAPS+TAB+direction on an elevated window and nothing happens.

**Why it happens:**
Windows Vista+ enforces User Interface Privilege Isolation (UIPI). Processes at lower integrity levels cannot send certain messages or call window-manipulation functions on higher-integrity windows. `SetWindowPos` internally sends `WM_WINDOWPOSCHANGING` to the target window; UIPI blocks this cross-integrity message delivery. The same restriction applies to `MoveWindow`, which is a wrapper around `SetWindowPos`.

UWP apps (running in the `ApplicationFrameHost` container) may also silently ignore external `SetWindowPos` calls even from processes at the same integrity level, because the `CoreWindow` inside the frame is managed by the UWP compositor.

**How to avoid:**
Detect elevated windows before attempting move/resize. Use `OpenProcess` + `GetTokenInformation` to check the target window's process integrity level:

```csharp
bool IsWindowElevated(nint hwnd)
{
    uint pid;
    PInvoke.GetWindowThreadProcessId(new HWND((nint)hwnd), out pid);
    using var hProcess = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
    // ... GetTokenInformation with TokenIntegrityLevel
    // Return true if level >= SECURITY_MANDATORY_HIGH_RID
}
```

For elevated windows, either silently skip the operation or show a brief overlay notification that move/resize is not available for this window. Do not raise an exception or log an error — this is expected behavior.

For UWP specifically, `IsWindowElevated` will return false (UWP is medium integrity) but `SetWindowPos` will still fail. Detect UWP by checking if the window class is `ApplicationFrameWindow` and handle accordingly.

**Warning signs:**
- `SetWindowPos` returns `false` with `GetLastError` == 5 (ERROR_ACCESS_DENIED)
- Move/resize silently fails only on Task Manager, elevated command prompts, or Windows settings windows
- Move/resize works on regular apps but not on apps launched with "Run as administrator"

**Phase to address:**
Phase 1 (Window move implementation). Add return-value checking with `GetLastError` logging from the start. Add elevated-window detection as a guard.

---

### Pitfall A-5: TAB Key (VK_TAB = 0x09) Has System-Level Focus Traversal Semantics — Suppression Breaks App Navigation

**What goes wrong:**
CAPS+TAB is planned as the move modifier. TAB is VK_TAB (0x09). The existing hook already intercepts CAPSLOCK+direction. When TAB is added to the suppressed key set while CAPSLOCK is held, every application that relies on TAB for internal focus traversal (dialog boxes, IDE tab switching, terminal input, web browser tab navigation) will have TAB suppressed from receiving it. The suppression will silently break intra-app navigation in the foreground application while CAPSLOCK is held.

**Why it happens:**
The current hook suppresses all intercepted keys by returning `(LRESULT)1` — the key never reaches the focused window. Direction keys (arrows, WASD) are acceptable to suppress because they are unambiguously navigation keys. TAB is different: virtually every interactive application uses TAB for focus traversal, form navigation, or input. Suppressing it while CAPSLOCK is held means the user cannot TAB through a form or IDE while in navigation mode.

**How to avoid:**
TAB must only be suppressed when CAPSLOCK is actually held AND the user is actively using the move-mode combo (i.e., TAB is being held down as part of the CAPS+TAB+direction chord). The suppression logic must track:

1. CAPSLOCK is held (already tracked in `_capsLockHeld`)
2. TAB was pressed while CAPSLOCK was held (track `_tabHeld` boolean)
3. A direction key was pressed while both CAPSLOCK and TAB were held → suppress direction, trigger move
4. TAB keyup → clear `_tabHeld`; do not suppress the TAB keyup itself

Critically: do not suppress TAB's keydown event itself. TAB down should set `_tabHeld = true` and be passed through (CallNextHookEx). Only direction keys are suppressed. This way, if the user presses CAPS+TAB but then releases TAB without pressing a direction, the TAB keydown has already been forwarded and the app receives normal focus traversal.

**Warning signs:**
- Dialog boxes cannot be navigated with TAB while the daemon is running
- Browser tab switching (Ctrl+TAB) is broken when CAPSLOCK is held
- IDE editor loses ability to insert tabs in code while CAPSLOCK is held

**Phase to address:**
Phase 1 (Modifier tracking and key dispatch). This is the most important UX correctness decision for the new modifier scheme. Design the suppression policy for TAB before writing hook logic.

---

### Pitfall A-6: GetKeyState vs VK_LSHIFT/VK_LCONTROL — Right-Side Modifier Keys Incorrectly Trigger Move Mode

**What goes wrong:**
The feature spec uses LSHIFT (left Shift) and LCTRL (left Ctrl) as the grow/shrink modifiers. If the hook checks `VK_SHIFT` (0x10) instead of `VK_LSHIFT` (0xA0), pressing right Shift also enters grow mode. This is unexpected: a user typing with right Shift while CAPSLOCK is briefly held would accidentally trigger grow/shrink instead of typing. Similarly, right Ctrl would trigger shrink mode.

**Why it happens:**
`GetKeyState(VK_SHIFT)` returns true for either left or right Shift. The existing code already uses `VK_SHIFT` (0x10) for modifier detection (line 141 in `KeyboardHookHandler.cs`). For the navigation use case this was acceptable — direction keys suppressed regardless of modifier. For LSHIFT specifically as a mode selector, the left/right distinction matters.

`WM_KEYDOWN` messages do not carry left/right distinction directly — the VK code for both left and right Shift is `VK_SHIFT`. However, in the `WH_KEYBOARD_LL` hook's `KBDLLHOOKSTRUCT`, the `vkCode` field **does** distinguish: `VK_LSHIFT` (0xA0) vs `VK_RSHIFT` (0xA1). The LL hook receives the actual left/right VK code, unlike `WM_KEYDOWN`.

**How to avoid:**
Track LSHIFT and LCTRL state by intercepting their specific VK codes in the hook callback:

```csharp
private const uint VK_LSHIFT   = 0xA0;
private const uint VK_RSHIFT   = 0xA1;
private const uint VK_LCONTROL = 0xA2;
private const uint VK_RCONTROL = 0xA3;
private const uint VK_TAB      = 0x09;

// In HookCallback, track these while CAPSLOCK is held:
if (kbd->vkCode == VK_LSHIFT)   _lShiftHeld = capsIsKeyDown;
if (kbd->vkCode == VK_LCONTROL) _lCtrlHeld  = capsIsKeyDown;
if (kbd->vkCode == VK_TAB)      _tabHeld    = capsIsKeyDown;
```

Do not use `GetKeyState(VK_SHIFT)` for mode selection — it does not distinguish left from right.

**Warning signs:**
- Pressing right Shift while CAPSLOCK is held enters grow mode instead of typing normally
- `GetKeyState(VK_LSHIFT) & 0x8000` correctly distinguishes — but only call it from the hook callback, not from a worker thread
- Right Ctrl accidentally triggers shrink mode during normal editing

**Phase to address:**
Phase 1 (Modifier tracking). Add VK_LSHIFT and VK_LCONTROL constants from the start. Do not use the generic VK_SHIFT/VK_CONTROL for mode selection.

---

### Pitfall A-7: Modifier State Desync After CAPSLOCK Release — Sticky Modifier Problem

**What goes wrong:**
The user presses CAPSLOCK, then LSHIFT, then releases CAPSLOCK. In the current design, CAPSLOCK release causes the overlay to dismiss and tracking state to reset. However, if LSHIFT or TAB are still physically held when CAPSLOCK releases, their `_lShiftHeld` / `_tabHeld` flags remain `true` in the hook state machine. On the next CAPSLOCK press, the system may immediately think LSHIFT is held (grow mode is active) even though the user only pressed CAPSLOCK.

**Why it happens:**
The hook state machine tracks modifier state via boolean flags that are set on keydown and cleared on keyup. If CAPSLOCK releases before LSHIFT (a rapid chord), the LSHIFT-up event is still forwarded but the consumer (CapsLockMonitor) has already seen the CAPSLOCK release and dismissed overlays. The `_lShiftHeld` flag set by the hook thread is not automatically cleared when CAPSLOCK releases.

**How to avoid:**
On CAPSLOCK release (or on `ResetState()` which is already called on sleep/wake), clear all auxiliary modifier flags:

```csharp
public void ResetState()
{
    _isHeld = false;
    _lShiftHeld = false;
    _lCtrlHeld = false;
    _tabHeld = false;
    _directionKeysHeld.Clear();
}
```

Additionally, re-read the actual physical key state when CAPSLOCK is pressed (not when it is released) using `GetKeyState(VK_LSHIFT)` to initialize the flag from ground truth at chord-start time, rather than relying purely on the tracked flag value.

**Warning signs:**
- After a rapid CAPS+SHIFT release sequence, the next CAPS hold immediately enters grow mode without pressing SHIFT
- Overlay shows grow-mode indicator without the user pressing LSHIFT

**Phase to address:**
Phase 1 (Modifier state machine). Add `ResetState` extension to clear the new modifier flags alongside the existing `_directionKeysHeld.Clear()`.

---

### Pitfall A-8: Grid Step Calculation Using Monitor Work Area vs Full Monitor Area — Taskbar Exclusion Error

**What goes wrong:**
Grid step size is calculated as a fraction of the monitor area (e.g., 1/16th of screen width). If the code uses `rcMonitor` (full physical monitor rect) instead of `rcWork` (work area excluding taskbar), windows moved to the bottom row of the grid may be positioned partially behind the taskbar. The grid does not align to what the user perceives as the "usable" screen.

Additionally, if grid calculations use the full monitor area but the taskbar is at the top or left, the grid origin is off — windows moved to the "top-left grid cell" will be partially behind the taskbar.

**Why it happens:**
`GetMonitorInfo` returns both `rcMonitor` (full hardware screen bounds) and `rcWork` (work area after subtracting taskbar/docked toolbars). Applications that manage windows should use `rcWork` for usable-area calculations. Using `rcMonitor` gives a larger grid but positions windows in physically inaccessible areas.

**How to avoid:**
```csharp
MONITORINFO mi = default;
mi.cbSize = (uint)sizeof(MONITORINFO);
PInvoke.GetMonitorInfo(hMon, ref mi);

// Use rcWork for grid boundaries — excludes taskbar
RECT workArea = mi.rcWork;
int gridWidth  = workArea.right  - workArea.left;
int gridHeight = workArea.bottom - workArea.top;

int stepX = gridWidth  / gridDivisor;  // e.g., gridDivisor = 16
int stepY = gridHeight / gridDivisor;

// Grid origin is workArea.left, workArea.top — not 0,0 and not rcMonitor.left, rcMonitor.top
```

Use `rcWork` for grid cell boundaries. Use `rcMonitor` only for cross-monitor detection (checking which monitor a window belongs to).

**Warning signs:**
- Windows moved to the bottom of the screen partially hide behind the taskbar
- Windows moved to the top overlap with a top-docked taskbar
- Grid appears correct when taskbar is on the primary monitor but wrong when it is on a secondary monitor

**Phase to address:**
Phase 1 (Grid calculation). Establish the `rcWork` pattern before any grid step math is written.

---

### Pitfall A-9: Integer Rounding in Grid Step Calculations Causes Cumulative Drift

**What goes wrong:**
Grid step is computed as `monitorWidth / gridDivisor`. If `monitorWidth` is not evenly divisible (e.g., 1920 / 16 = 120 exactly, but 2560 / 16 = 160 exactly — these divide cleanly; however 3440 / 16 = 215.0, and 1366 / 16 = 85.375), integer division truncates the remainder. When snapping is applied, a window snapped to position `k * stepX` will be at `k * 85` pixels, not `k * 85.375`. Over multiple moves, the accumulated error is small but the grid cells are not uniform.

A more serious problem: the "smart snap with tolerance (~10% of grid step)" may fail to snap a window that is just outside tolerance because the snap target position was calculated with truncated step size while the window's actual position was based on a different rounding.

**Why it happens:**
Win32 window coordinates are integers. Grid steps must be integers. Floating-point intermediate calculations lose precision when cast back to `int`. If the snap target and the window position are computed with different rounding (one uses `Math.Round`, one uses integer truncation), they can differ by 1–2 pixels, causing snap to miss.

**How to avoid:**
Use a consistent rounding policy throughout: choose `(int)Math.Round(monitorWidth / (double)gridDivisor)` for step calculation and use the same rounding everywhere. When computing snap targets, snap to `(int)Math.Round(position / step) * step` rather than `(position / step) * step` (integer division loses remainder). Test with 3440x1440 (16:9 ultrawide), 2560x1440, and 1366x768 to confirm consistent behavior.

Alternatively, avoid snap-to-grid entirely if the move itself always moves by exactly `step` pixels from the snapped starting position — but only if the initial position is already snapped.

**Warning signs:**
- Window does not snap to expected grid position after move — ends up 1–2 pixels off
- Snap tolerance test passes in unit tests but fails on real monitors with odd resolutions
- Grid cells visually appear unequal in width

**Phase to address:**
Phase 1 (Grid calculation). Write a unit test for step calculation with non-divisible resolutions before integrating into the move handler.

---

### Pitfall A-10: Cross-Monitor Move — Window Jumps to Wrong Position When Crossing Monitor Boundary

**What goes wrong:**
When a window is moved off the right edge of monitor A onto monitor B, the grid step for monitor B differs (different resolution). If the code calculates the new position as `currentLeft + stepA` but monitor B starts at an offset in virtual screen coordinates, the window ends up at an arbitrary position on monitor B rather than at the leftmost grid cell. The cross-monitor boundary is invisible to the grid math.

**Why it happens:**
Each monitor has its own work area and its own grid. Virtual screen coordinates are a single continuous coordinate space, but the "grid" is monitor-local. When a window crosses from monitor A to monitor B, the correct behavior is: detect the boundary crossing, place the window at the first grid cell of monitor B that is logically "adjacent" to where it was on monitor A. Without this detection, the math simply continues adding `stepA` past the monitor boundary, landing at a non-grid-aligned position on monitor B.

**How to avoid:**
Detect monitor transition during move:

```csharp
// After computing the proposed new position:
HMONITOR newMon = PInvoke.MonitorFromRect(proposedRect, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

if (newMon != currentMon)
{
    // Window is crossing to a new monitor — snap to the adjacent grid cell on the new monitor
    MONITORINFO newMi = GetMonitorInfo(newMon);
    RECT newWork = newMi.rcWork;
    int newStepX = (newWork.right - newWork.left) / gridDivisor;
    int newStepY = (newWork.bottom - newWork.top) / gridDivisor;

    // For a rightward move: snap to left edge of new monitor's first column
    proposedRect.left = newWork.left;
    proposedRect.right = proposedRect.left + windowWidth; // preserve window width
}
```

**Warning signs:**
- Moving a window past the right edge of monitor A places it at an arbitrary position on monitor B
- Moving back left from monitor B places it in a different column on monitor A than expected
- Cross-monitor move is "lossy" — information about grid alignment is lost on each crossing

**Phase to address:**
Phase 2 (Cross-monitor move support). Implement same-monitor move first, then layer in cross-monitor detection separately.

---

### Pitfall A-11: Overlay Flicker During Rapid Move/Resize Keystrokes — HideAll + ShowAll on Every KeyDown

**What goes wrong:**
The existing `ShowOverlaysForCurrentForeground` calls `HideAll()` at the start (to clear stale positions) then rebuilds and shows all overlays. For navigation this is acceptable — overlays update once per CAPSLOCK hold. For move/resize, the overlay must update after **every keypress** (every CAPS+TAB+direction shows the new position). With the current pattern, each keypress causes a HideAll (all overlays disappear for one frame) followed by ShowAll (overlays reappear in new positions). At 60Hz, this is a 1-frame flicker visible as a brief blink.

**Why it happens:**
The hide-then-show sequence is not atomic. Between the `HideAll()` call and the subsequent `ShowOverlay()` calls, the STA message pump may process a WM_PAINT, causing a rendered frame where no overlays are visible. On a 16ms frame budget, even a few microseconds of "all hidden" state can produce a visible flash.

**How to avoid:**
For move/resize overlay updates, use the `Reposition` + `Paint` + `Show` pattern that updates position and content without first hiding. Since the overlay bounds change every keypress, update each overlay window in-place:

```csharp
// Instead of:
_overlayManager.HideAll();
// ... compute new bounds ...
_overlayManager.ShowOverlay(direction, newBounds);

// Do:
// Only hide overlays that are no longer needed; reposition+repaint those that remain
_overlayManager.UpdateOverlay(direction, newBounds); // Reposition + Paint without Hide
```

Or: accept the flicker as a known UX tradeoff and document it. For a single-pixel border on a fast monitor, the flicker may be imperceptible in practice.

Alternatively, use `BeginDeferWindowPos` / `DeferWindowPos` / `EndDeferWindowPos` to batch multiple overlay repositions atomically within a single compositor frame.

**Warning signs:**
- Overlays visibly blink during rapid CAPS+TAB+direction key presses
- Flicker is more noticeable on high-refresh-rate monitors (where a single dropped frame is more visible)
- User reports overlays "stuttering" during move mode

**Phase to address:**
Phase 2 (Overlay update during move/resize). Start with the simple HideAll+ShowAll pattern. Profile and address flicker only if user testing confirms it is noticeable.

---

### Pitfall A-12: Overlay Does Not Track Window During Move — Shows Pre-Move Position

**What goes wrong:**
After calling `SetWindowPos` to move the foreground window, the overlay still shows the window's original position (pre-move). The overlay was positioned based on the bounds read before the move command. The overlay must be repositioned to the new bounds after each move step.

**Why it happens:**
The existing overlay update flow is: `ForegroundMonitor` detects foreground change → overlay refreshes. A move command does not change the foreground window — the same window remains foreground. The `ForegroundMonitor`-based refresh does not fire. The overlay stays at the old position until CAPSLOCK is released and re-held.

**How to avoid:**
After each successful `SetWindowPos` call, immediately re-read the window's new bounds and refresh the overlay:

```csharp
void MoveWindowBySta(HWND hwnd, Direction direction)
{
    // 1. Read current bounds
    // 2. Compute new bounds
    // 3. SetWindowPos
    // 4. Read back actual new bounds (in case the move was partially constrained)
    PInvoke.GetWindowRect(hwnd, out RECT actualNewRect);
    // 5. Update overlays to new position
    UpdateMoveOverlay(actualNewRect);
}
```

Step 4 (read-back) is important because the window may not have moved to exactly the requested position — UIPI restrictions, min/max size constraints, or snap resistance may have constrained the move. Always work from the actual post-move bounds.

**Warning signs:**
- Overlay shows old window position while the window has moved to a new position
- Overlay "catches up" to window position only when CAPSLOCK is released and re-held
- Mode indicator (move arrows) is visible at the wrong screen position

**Phase to address:**
Phase 2 (Overlay integration for move/resize). The post-move overlay refresh must be part of the same STA-thread operation as the move itself.

---

### Pitfall A-13: Window Min/Max Size Constraints Silently Clamp Resize Operations

**What goes wrong:**
When shrinking a window with CAPS+LCTRL+direction, the window may hit its minimum allowed size (set via `WM_GETMINMAXINFO` or `SetWindowPos` constraints). `SetWindowPos` returns true but the window does not shrink further. If the code re-uses the requested size for subsequent calculations, each shrink step appears to take effect (size decrements by `step` in the stored value) but the window stays at min-size. Eventually the stored size and the actual window size diverge.

**Why it happens:**
Windows enforces minimum/maximum window dimensions silently. The `WM_GETMINMAXINFO` message handler in the target window's WndProc sets minimum dimensions; `SetWindowPos` respects these without informing the caller. The caller receives a success return but the actual position/size clamped by the target.

**How to avoid:**
Always read back the actual window dimensions after calling `SetWindowPos` (same pattern as A-12). Use the actual post-call rect for all subsequent calculations, not the requested rect:

```csharp
PInvoke.SetWindowPos(hwnd, HWND.Null, newLeft, newTop, newWidth, newHeight,
    SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

// Always read back the actual result
PInvoke.GetWindowRect(hwnd, out RECT actualRect);
// Use actualRect, not newLeft/newTop/newWidth/newHeight, for overlay positioning
```

If the read-back dimensions match the pre-call dimensions (no change occurred), treat this as "at minimum/maximum size" and suppress further requests in that direction until a reverse operation is attempted.

**Warning signs:**
- Shrink mode appears to work for first few key presses but stops visually at some point
- The overlay shows a size smaller than the actual window
- Repeated shrink presses make no visual change after a certain point

**Phase to address:**
Phase 1 (Resize implementation). Establish the post-call read-back pattern for all move/resize operations from the start.

---

### Pitfall A-14: WINEVENT_OUTOFCONTEXT ForegroundMonitor Fires During Move — Causes Overlay Refresh Loop

**What goes wrong:**
The `ForegroundMonitor` fires an event via `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)` when the foreground window changes. During a window move, if the daemon's own move code calls `SetWindowPos` on the target window, Windows may send `EVENT_SYSTEM_FOREGROUND` again (depending on whether focus was re-asserted). This triggers a full overlay refresh from `ShowOverlaysForCurrentForeground` at the same time the move handler is also updating overlays — resulting in two concurrent overlay update sequences on the STA thread.

**Why it happens:**
Both paths (ForegroundMonitor callback and move-complete overlay update) run on the STA thread, so they do not race. However, if the ForegroundMonitor fires during a move and triggers `ShowOverlaysForCurrentForeground`, it calls `HideAll()` first — which hides the move-mode overlay mid-operation. The result is overlay flicker and potentially inconsistent overlay state (move-mode indicator disappears, then navigation-mode indicator appears briefly).

**How to avoid:**
Add a `_moveOrResizeInProgress` flag on the STA thread. In `ForegroundMonitor`'s callback, skip the full overlay refresh if a move/resize is in progress:

```csharp
private void OnForegroundChanged(HWND hwnd)
{
    if (!_capsLockHeld) return;
    if (_moveOrResizeInProgress) return; // skip — move handler owns overlay during move
    ShowOverlaysForCurrentForeground();
}
```

Set `_moveOrResizeInProgress = true` before the first `SetWindowPos` call and `= false` after the post-call overlay update completes.

**Warning signs:**
- Overlay flickers between "move mode" and "navigation mode" indicator during move
- `ShowOverlaysForCurrentForeground` logs show during move operations
- Overlay appears to briefly reset to navigation mode between move steps

**Phase to address:**
Phase 2 (Overlay integration for move/resize). Add the `_moveOrResizeInProgress` guard when connecting the move handler to the STA dispatch path.

---

## Section B: v3.0 Pitfalls (Retained from Previous Research — Still Applicable)

The following pitfalls were documented for v2.0 and v3.0. They remain applicable throughout v3.1 development. All have been addressed in the existing codebase; retain for regression testing.

---

### Pitfall B-1: Hook Delegate Garbage Collected — Hook Fires into Freed Memory

**(Already mitigated — `static s_hookProc` field in `KeyboardHookHandler.cs`)**

The `WH_KEYBOARD_LL` delegate must be stored in a static or long-lived instance field. GC can collect a delegate held only in a local variable after the method returns, crashing the process.

**Phase to address:** Already addressed. Regression-test after any refactoring of `KeyboardHookHandler`.

---

### Pitfall B-2: No Message Loop — Hook Never Fires

**(Already mitigated — WinForms `Application.Run` on STA thread in daemon)**

`WH_KEYBOARD_LL` requires a Win32 message loop on the installing thread. The daemon uses WinForms `Application.Run` for this purpose.

**Phase to address:** Already addressed.

---

### Pitfall B-3: Hook Callback Exceeds 300ms/1000ms Timeout — Silently Removed

**(Already mitigated — `TryWrite` to Channel; all work on worker thread)**

The hook callback must return in under the `LowLevelHooksTimeout` registry value. All non-trivial work is on the worker thread.

**Phase to address:** Already addressed. Verify that the new move/resize dispatch path (`_staDispatcher.Invoke`) does not block the STA message pump for long enough to back up hook callbacks. Move/resize operations must complete within ~80ms on the STA thread.

---

### Pitfall B-4: Overlay Window Steals Focus

**(Already mitigated — `WS_EX_NOACTIVATE`, `SWP_NOACTIVATE` on all overlay operations)**

**Phase to address:** Already addressed. New move-mode overlay windows (if any are added) must also carry `WS_EX_NOACTIVATE`.

---

### Pitfall B-5: AHK Injected Keys Trigger Hook — Overlay Flickers

**(Already mitigated — `LLKHF_INJECTED` filter at hook callback entry)**

**Phase to address:** Already addressed. Verify the new TAB/LSHIFT/LCTRL paths also filter injected keys.

---

### Pitfall B-6: Layered Window API Modes Mutually Exclusive

**(Already mitigated — `UpdateLayeredWindow` + premultiplied alpha DIB used exclusively)**

**Phase to address:** Already addressed. Any new overlay content for move/resize mode must use the same `UpdateLayeredWindow` path, not `SetLayeredWindowAttributes`.

---

### Pitfall B-7: DPI Mismatch Between DWMWA_EXTENDED_FRAME_BOUNDS and Overlay Positioning

**(Already mitigated — PerMonitorV2 manifest; STA thread DPI context unchanged)**

**Phase to address:** Already addressed. See also new Pitfall A-3 for the new wrinkle introduced by moving external windows.

---

### Pitfall B-8: CAPSLOCK State Ambiguity and Toggle Preservation

**(Already mitigated — CAPSLOCK is suppressed; bare CAPSLOCK toggle also suppressed)**

**Phase to address:** Already addressed. The new modifier keys (TAB, LSHIFT, LCTRL) must NOT be unconditionally suppressed — only direction keys during active move/resize mode.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Using DWM extended bounds for MoveWindow coordinates | Same variable already computed | Window shrinks by ~8px per side on each move | Never |
| Skipping post-SetWindowPos read-back | Simpler code | Stored size diverges from actual; overlay at wrong position | Never |
| Not checking SetWindowPos return value | One less error path | Silent failures on elevated windows invisible; no diagnostic | Never |
| Using VK_SHIFT instead of VK_LSHIFT for mode selection | Simpler VK code | Right Shift accidentally activates grow mode | Never |
| Suppressing TAB unconditionally while CAPSLOCK held | Simpler hook logic | All dialog/browser/IDE tab navigation broken during CAPS hold | Never |
| Computing grid step from rcMonitor instead of rcWork | Simpler GetMonitorInfo usage | Windows moved to bottom/top row partially behind taskbar | Never |
| Skipping cross-monitor transition detection | Less geometry code | Window placed at arbitrary position when crossing monitor boundary | Acceptable for MVP if cross-monitor is deferred to Phase 2 |
| HideAll+ShowAll pattern for overlay during move | Reuses existing refresh path | 1-frame overlay flicker per keypress during rapid move | Acceptable if user testing confirms imperceptible |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| SetWindowPos + DWM bounds | Using DWMWA_EXTENDED_FRAME_BOUNDS coordinates in MoveWindow | Use GetWindowRect for move targets; DWM bounds only for overlays and navigation scoring |
| SetWindowPos + maximized window | Calling MoveWindow without checking IsZoomed | Detect ShowWindowCmd state; call SW_RESTORE before moving if maximized |
| UIPI + elevated window | Ignoring SetWindowPos return value | Check return + GetLastError; silently skip move on ERROR_ACCESS_DENIED (5) |
| GetKeyState + left/right modifiers | Using VK_SHIFT (0x10) for LSHIFT detection | Use VK_LSHIFT (0xA0) directly in LL hook vkCode checks; GetKeyState(VK_LSHIFT) from hook callback |
| TAB interception + app focus traversal | Suppressing TAB keydown while CAPSLOCK held | Only suppress direction keys; TAB sets _tabHeld flag and is forwarded through CallNextHookEx |
| Overlay update + window move | ForegroundMonitor triggering full refresh during move | Gate ForegroundMonitor refresh on !_moveOrResizeInProgress flag |
| Grid calculation + work area | Using rcMonitor for grid boundaries | Use rcWork from GetMonitorInfo for all grid cell and origin calculations |
| Min/max size + shrink mode | Trusting requested size after SetWindowPos | Read back actual size with GetWindowRect; treat no-change as "at limit" |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| STA Invoke for move/resize blocking hook callbacks | Hook timeout → silent removal after ~10 consecutive slow callbacks | Keep STA move handler under 50ms total; use async dispatch if window has cross-thread ownership | Any move operation taking >200ms on STA thread |
| Full overlay HideAll+ShowAll on every keypress | Visible blink per move step at >4 keys/sec | Batch overlay updates; use Reposition+Paint in-place | Rapid move mode (held direction key with key repeat) |
| GetWindowRect called repeatedly in grid snap loop | Minor but unnecessary API overhead | Call once, store result, compute all grid values from one snapshot | Unlikely to matter; cosmetic concern only |
| Reading config fresh on every move step | File I/O adds 1-5ms per step | Cache grid config for duration of CAPSLOCK hold; reload only on new CAPSLOCK press | High-frequency moves with config read on each step |

---

## "Looks Done But Isn't" Checklist

- [ ] **Maximized window**: CAPS+TAB+direction on a maximized window — verify it either no-ops cleanly or restores then moves (not silently fails with no effect)
- [ ] **Elevated window**: CAPS+TAB+direction on Task Manager — verify ERROR_ACCESS_DENIED is detected and handled silently (no crash, no log spam)
- [ ] **UWP window**: CAPS+TAB+direction on a Windows Settings window — verify graceful no-op
- [ ] **TAB passthrough**: With daemon running and CAPSLOCK held, press Tab in a dialog box — verify focus traversal still works normally
- [ ] **Right Shift isolation**: With daemon running, hold CAPSLOCK and press right Shift + direction — verify grow mode is NOT triggered; only left Shift triggers it
- [ ] **Modifier state reset**: Rapidly press CAPS+SHIFT then release CAPS before SHIFT — verify next CAPS hold does not start in grow mode
- [ ] **DWM bounds vs GetWindowRect**: After 5 consecutive moves, verify window size matches starting size (no shrinkage from coordinate space mismatch)
- [ ] **Grid on rcWork**: Move window to bottom row — verify window does not land behind taskbar
- [ ] **Cross-monitor move**: Move window off right edge of monitor A — verify it lands at leftmost column of monitor B (not arbitrary position)
- [ ] **Min-size clamp**: Shrink window to minimum size — verify further shrink key presses are silently ignored (no divergence between overlay and actual window)
- [ ] **Overlay tracks window**: After each move step, overlay must be at new window position (not old position)
- [ ] **Post-SetWindowPos read-back**: Log actual rect after each move; confirm it matches intended position (for non-UIPI-blocked windows)

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| DWM bounds used for MoveWindow (window shrinks) | LOW | Replace DWM bounds with GetWindowRect in move handler; one call change |
| Missing SetWindowPos return-value check | LOW | Add bool result check + GetLastError logging; no logic change |
| Elevated window UIPI failure | LOW | Add process integrity level check before move; silently no-op |
| TAB incorrectly suppressed | LOW | Change hook logic: track _tabHeld without suppressing TAB keydown |
| VK_SHIFT used instead of VK_LSHIFT | LOW | Change VK constant; no architectural change |
| Modifier state desync | LOW | Add modifier flag clear to ResetState() |
| Grid uses rcMonitor instead of rcWork | LOW | One-line change in grid origin/size calculation |
| No post-move read-back | LOW | Add GetWindowRect call after SetWindowPos; use result for overlay positioning |
| ForegroundMonitor refresh loop during move | MEDIUM | Add _moveOrResizeInProgress guard; requires careful lifecycle around STA dispatch |
| Overlay flicker during rapid move | MEDIUM | Implement Reposition+Paint-in-place path in OverlayManager; does not change architecture |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Maximized window no-op (A-1) | Phase 1 — Move handler | Test on maximized Chrome, VS Code — no silent failure |
| DWM bounds vs MoveWindow coords (A-2) | Phase 1 — Move handler | Move window 5x; verify size unchanged; check GetWindowRect before and after |
| DPI context mismatch (A-3) | Phase 1 — Move handler | Two-monitor mixed-DPI setup; move across monitors — position correct on both |
| UIPI elevated window (A-4) | Phase 1 — Move handler | Attempt move on Task Manager — no crash, no error log spam |
| TAB passthrough (A-5) | Phase 1 — Modifier tracking | Tab through dialog while CAPSLOCK held — focus traversal unaffected |
| VK_LSHIFT vs VK_SHIFT (A-6) | Phase 1 — Modifier tracking | Right Shift + direction while CAPS held — grow mode NOT triggered |
| Modifier state desync (A-7) | Phase 1 — Modifier tracking | Rapid CAPS+SHIFT+release-CAPS cycle — next CAPS hold starts in neutral mode |
| Grid uses rcWork (A-8) | Phase 1 — Grid calculation | Bottom-row window — not behind taskbar |
| Integer rounding (A-9) | Phase 1 — Grid calculation | 3440x1440 ultrawide: snap to grid — all cells equal width within 1px |
| Cross-monitor move (A-10) | Phase 2 — Cross-monitor | Move off right edge — lands at monitor B column 0 |
| Overlay flicker during move (A-11) | Phase 2 — Overlay integration | Rapid move keys — no visible blink; if visible, document as known |
| Overlay tracks window (A-12) | Phase 2 — Overlay integration | Each move step — overlay immediately at new window position |
| Min/max size clamp (A-13) | Phase 1 — Resize handler | Shrink to minimum — further shrink no-ops cleanly |
| ForegroundMonitor loop during move (A-14) | Phase 2 — Overlay integration | Move window — no mode flicker between move and navigation overlays |

---

## Sources

- [MoveWindow function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-movewindow) — HIGH confidence (official docs)
- [SetWindowPos function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) — HIGH confidence (official docs)
- [DWMWINDOWATTRIBUTE — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) — HIGH confidence (official docs; DWMWA_EXTENDED_FRAME_BOUNDS "not adjusted for DPI")
- [GetWindowRect — invisible borders on Windows 10](https://www.w3tutorials.net/blog/getwindowrect-returns-a-size-including-invisible-borders/) — MEDIUM confidence (practitioner, consistent with DWM docs)
- [High DPI Desktop Application Development on Windows — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) — HIGH confidence (official docs)
- [PhysicalToLogicalPointForPerMonitorDPI — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-physicaltologicalpointforpermonitordpi) — HIGH confidence (official docs)
- [WINDOWPLACEMENT — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement) — HIGH confidence (official docs; workspace vs. screen coordinates distinction)
- [GetWindowPlacement — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement) — HIGH confidence (official docs)
- [Security Considerations for Assistive Technologies (UIPI) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview) — HIGH confidence (official docs)
- [Bring UWP app window to front and move it — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/219889/bring-uwp-app-window-to-front-and-move-it) — MEDIUM confidence (official Q&A; describes UIPI behavior)
- [Virtual-Key Codes — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes) — HIGH confidence (official docs; VK_LSHIFT=0xA0, VK_LCONTROL=0xA2)
- [GetKeyState function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeystate) — HIGH confidence (official docs)
- [Determining modifier key state when hooking keyboard input — Jon Egerton](https://jonegerton.com/dotnet/determining-the-state-of-modifier-keys-when-hooking-keyboard-input/) — MEDIUM confidence (practitioner, consistent with GetKeyState docs)
- [Positioning Objects on Multiple Display Monitors — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/gdi/positioning-objects-on-multiple-display-monitors) — HIGH confidence (official docs)
- [WM_WINDOWPOSCHANGING — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-windowposchanging) — HIGH confidence (official docs)
- [UpdateLayeredWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) — HIGH confidence (official docs; position via UpdateLayeredWindow preferred for layered windows)

---

*Pitfalls research for: Win32 grid-snapped window move/resize added to keyboard hook daemon (v3.1)*
*Researched: 2026-03-02*

---

## Section D: v4.0 New Pitfalls — System Tray, Settings UI, Context Menu, Daemon Restart

---

### Pitfall D-1: Ghost Tray Icon Persists After Daemon Kill or Abnormal Exit

**What goes wrong:**
When the daemon is killed (Task Manager, taskkill /F, or the "Restart Daemon" path kills the old process), its NotifyIcon stays visible in the system tray. The user sees two icons briefly — the dead instance ghost and the new instance icon. The ghost disappears only when the mouse is moved over it (Windows sends a test message and removes the stale entry). On slower machines the ghost can linger for several seconds.

The same problem occurs if the daemon crashes before DaemonApplicationContext.Dispose runs (uncaught exception on a background thread that tears down the process without invoking Application.Exit).

**Why it happens:**
NotifyIcon relies on Shell_NotifyIcon(NIM_DELETE) being called during disposal to remove the icon from the shell. If the process exits before DaemonApplicationContext.Dispose runs — because it was killed, crashed, or called Environment.Exit() without going through Application.ExitThread — the NIM_DELETE call never happens. Windows detects the dead window handle only when the shell tries to deliver a mouse-over message to the tray icon host window, which it can only check lazily.

**How to avoid:**
Two layers of protection:

1. Always call _trayIcon.Visible = false before _trayIcon.Dispose(). The existing DaemonApplicationContext.Dispose already does this on the clean-exit path. The problem is the crash/kill path where Dispose does not run.

2. For the "Restart Daemon" menu item: start the new instance first using "daemon --background" (so it self-replaces via DaemonMutex.AcquireOrReplace), then trigger the existing clean-exit path in the current instance. The new process will kill us if we are slow. Prefer graceful exit over forced kill so Dispose runs cleanly.

**Warning signs:**
- Two tray icons briefly visible after daemon restart
- Ghost icon persists until mouse hover
- Repeated restarts accumulate ghost icons that only clear on hover

**Phase to address:**
Phase implementing "Restart Daemon" context menu item. The ghost is cosmetic but confusing. Eliminate it for the graceful-restart path by ensuring NotifyIcon.Visible = false runs before exit.

---

### Pitfall D-2: ICO File Wrong Size Displayed in System Tray on Windows 11

**What goes wrong:**
A custom .ico file embedded as an EmbeddedResource and assigned directly to NotifyIcon.Icon displays blurry or incorrectly scaled in the system tray, especially at non-100% DPI scales (125%, 150%, 200%) on Windows 11. The icon may look fine at 100% DPI but blurry on high-DPI monitors.

**Why it happens:**
NotifyIcon does not select the correct icon size from a multi-resolution ICO file. It uses whatever size System.Drawing.Icon was constructed with (typically the default 32x32), then Windows scales it down. On Windows 11 at 125% DPI, the tray icon should be 20x20 physical pixels, but the 32x32 frame is scaled down, losing quality. On Windows 10 the tray icon matches the notifications icon; on Windows 11 they can diverge.

The confirmed workaround (dotnet/winforms issue #6955) is to explicitly construct the Icon at SystemInformation.SmallIconSize:

```csharp
// Wrong: _trayIcon.Icon = new Icon("focus.ico");
// Wrong: _trayIcon.Icon = SystemIcons.Application;

// Correct: let Windows select the right frame for the actual system tray size
_trayIcon.Icon = new Icon(iconStream, SystemInformation.SmallIconSize);
```

Loading from an embedded resource stream and sizing to SystemInformation.SmallIconSize causes the Icon constructor to select the closest matching frame from the ICO file.

**How to avoid:**
- Include ICO frames at minimum: 16x16, 20x20, 24x24, 32x32. Microsoft recommends also 48x48, 256x256 for Windows Explorer.
- When creating the NotifyIcon.Icon, always construct as new Icon(stream, SystemInformation.SmallIconSize).
- Do not assign SystemIcons.Application for the final shipped icon — it is a placeholder only.
- Test at 100%, 125%, 150%, 200% DPI settings using Windows display settings before shipping.

**Warning signs:**
- Icon appears blurry at any DPI setting other than 100%
- Icon in taskbar notification area looks different from icon in "hidden icons" popup
- Icon quality differs between Windows 10 and Windows 11 at the same DPI

**Phase to address:**
Phase implementing the custom tray icon (ICO file creation and embedding). Set the SystemInformation.SmallIconSize pattern from the start rather than discovering the blurriness after artwork is finalized.

---

### Pitfall D-3: Settings Window Opened Multiple Times from Tray — No Singleton Guard

**What goes wrong:**
Clicking "Settings" from the tray context menu multiple times opens multiple settings windows. Each window independently reads from the config file. Saving from multiple windows can overwrite each other's changes. The user may not notice the second window is behind the first.

**Why it happens:**
new SettingsForm().Show() creates a new form each time the menu item is clicked with no check for an existing open instance.

**How to avoid:**
Keep a reference to the settings window and bring it to front if already open:

```csharp
private SettingsForm? _settingsForm;

private void OnSettingsClicked(object? sender, EventArgs e)
{
    if (_settingsForm is { IsDisposed: false })
    {
        // Already open — bring to front instead of opening another
        _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.BringToFront();
        _settingsForm.Activate();
        return;
    }

    _settingsForm = new SettingsForm();
    _settingsForm.FormClosed += (_, _) => _settingsForm = null;
    _settingsForm.Show();
}
```

Use Show() (not ShowDialog()) so the tray icon remains interactive while settings are open — see Pitfall D-4.

**Warning signs:**
- Multiple settings windows open simultaneously
- Config file saved by one window is immediately overwritten by another
- User reports "Save did not work" because the second window overwrote the first

**Phase to address:**
Phase implementing the settings window. Add the singleton guard as the first thing in the click handler, before any form creation code.

---

### Pitfall D-4: Settings Window Uses ShowDialog — Blocks STA Message Pump, Stalls Keyboard Hook Dispatch

**What goes wrong:**
Opening the settings window with ShowDialog() from the STA thread blocks the Application.Run message pump. While blocked, WH_KEYBOARD_LL hook callbacks can still fire (they have their own thread affinity), but any hook callback that dispatches work to the STA thread via Control.Invoke will deadlock — the STA thread is blocked waiting for ShowDialog to return, while the Invoke call is waiting for the STA thread to process the dispatched message.

The overlay orchestrator dispatches work to the STA thread via the Control.Invoke pattern used in OverlayOrchestrator. If the user holds CAPSLOCK while the settings dialog is open (modal), and the overlay tries to show via Invoke, it will deadlock for the duration of the dialog.

**Why it happens:**
ShowDialog() is modal: it runs its own nested message loop but blocks the calling thread from returning. Control.Invoke is synchronous and waits for the target thread to process the message. Whether the nested loop correctly processes Invoke messages depends on undocumented internal WinForms behavior. Do not rely on it.

**How to avoid:**
Use Show() instead of ShowDialog() for the settings window. This is consistent with how DaemonApplicationContext already manages all its windows. ShowDialog is acceptable only for small sub-dialogs opened from within an already-showing settings form (color picker, confirmation dialogs) — those do not block the main message pump.

**Warning signs:**
- Holding CAPSLOCK while settings window is open causes overlay to not appear
- Keyboard shortcuts stop working while settings window is open
- Overlay appears with a delay after settings window is closed (queued Invoke calls finally process)

**Phase to address:**
Phase implementing the settings window. Use Show() exclusively; add a comment near the handler noting ShowDialog is forbidden for daemon-lifecycle windows.

---

### Pitfall D-5: Config File Race — Settings Window Writes While Daemon Reads the Same File

**What goes wrong:**
The daemon reads config.json fresh on every CAPSLOCK hold (by design: "fresh config load per keypress" from the key decisions table). The settings window writes to the same file when the user clicks Save. If a CAPSLOCK hold triggers FocusConfig.Load() at the exact moment the settings window is in the middle of File.WriteAllText, the read gets a partial or empty JSON payload. JsonSerializer.Deserialize fails, falls back to defaults, and the daemon silently ignores the config for that keypress.

**Why it happens:**
File.WriteAllText is not atomic on Windows: it opens the file, truncates, writes, and closes. Another process can observe the file mid-write. File.ReadAllText in that window gets an incomplete file. The existing FocusConfig.Load() has a try/catch that falls back to defaults on parse error — which means the race produces silent degradation rather than a crash. The timing window is tiny in practice but non-zero.

**How to avoid:**
Use an atomic write pattern: write to a .tmp file on the same volume, then rename over the target:

```csharp
// In settings form Save:
var configPath = FocusConfig.GetConfigPath();
var tmpPath = configPath + ".tmp";
File.WriteAllText(tmpPath, json);                     // Write to temp file
File.Move(tmpPath, configPath, overwrite: true);      // Atomic rename on NTFS same-volume
```

File.Move with overwrite: true on the same NTFS volume is an atomic operation (single rename syscall). The daemon sees either the old complete file or the new complete file — never a partial write.

**Warning signs:**
- Daemon behaves with default settings immediately after clicking Save in the settings window
- Verbose log shows "config parse error" entries coinciding with Save operations
- The problem is non-deterministic and rarely reproducible (timing-sensitive)

**Phase to address:**
Phase implementing the settings form Save action. Use the write-to-temp-then-rename pattern from the start. FocusConfig.Load() does not need to change.

---

### Pitfall D-6: Daemon Restart with CAPSLOCK Physically Held — Toggle State Leaks to OS During Hook Gap

**What goes wrong:**
The user clicks "Restart Daemon" from the tray menu while CAPSLOCK is physically held. The old daemon kills itself with CAPSLOCK suppression active. Between the old daemon hook's Uninstall() and the new daemon hook's Install(), CAPSLOCK events pass through to the OS raw. If CAPSLOCK is physically held during this gap, the OS receives the raw toggle and activates system-wide CAPS LOCK. The new daemon starts, calls ForceCapsLockOff() which corrects the toggle — but timing scenarios exist where the correction races with the key release.

**Why it happens:**
There is always a gap between hook.Uninstall() in the old daemon and hook.Install() in the new daemon startup. During this gap, raw CAPSLOCK events reach the OS. ForceCapsLockOff() in the new daemon corrects the toggle state after the fact, and is already called before hook.Install() — which is the correct ordering. The existing design handles this, but the restart path must be careful not to skip ForceCapsLockOff() by taking a non-standard exit route.

**How to avoid:**
Before initiating restart, explicitly dismiss overlays and reset state:

```csharp
private void OnRestartClicked(object? sender, EventArgs e)
{
    // Dismiss overlay immediately — prevents stale overlay if restart is slow
    _orchestrator.OnCapsLockReleased();

    // Hide tray icon before starting new process to reduce ghost icon window
    _trayIcon.Visible = false;

    // Start new daemon instance before exiting this one (minimize gap)
    var selfPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
    Process.Start(new ProcessStartInfo(selfPath, "daemon --background") { UseShellExecute = false });

    // Exit via existing clean path — DaemonMutex in new process kills us if slow
    _onExit();
    Application.ExitThread();
}
```

The existing ForceCapsLockOff() in the new daemon startup corrects any leaked toggle state. Verify explicitly by testing restart while CAPSLOCK physically held.

**Warning signs:**
- After restart, CAPSLOCK is toggled ON system-wide (all typed letters are uppercase)
- Overlays from the previous daemon instance remain visible after restart
- Restart sometimes leaves system in CAPS LOCK mode; requires manual CAPS LOCK press to clear

**Phase to address:**
Phase implementing "Restart Daemon" context menu item. Test the restart path with CAPSLOCK physically held at the moment of the restart click.

---

### Pitfall D-7: Dynamic Context Menu Status Labels Updated from Background Thread — Cross-Thread Access

**What goes wrong:**
The context menu's status items (e.g., "Hook: Active | Uptime: 2m 34s | Last action: left") must be kept current. A background timer or the CapsLockMonitor worker thread updates these strings, then directly modifies ToolStripItem.Text on a ContextMenuStrip that lives on the STA thread. This causes a cross-thread exception: InvalidOperationException: Cross-thread operation not valid.

In some cases the exception is swallowed silently — the status labels then show stale values indefinitely.

**Why it happens:**
All System.Windows.Forms controls, including ContextMenuStrip and its items, must be accessed only from the thread that created them. The STA thread in this daemon owns all WinForms objects. The CapsLockMonitor runs on a Task.Run thread pool thread. Any callback from the monitor that touches a ToolStripItem directly violates the threading contract.

**How to avoid:**
Populate status text inside the ContextMenuStrip.Opening event handler, which fires on the STA thread just before the menu appears:

```csharp
_contextMenu.Opening += (_, e) =>
{
    e.Cancel = false; // CRITICAL: must explicitly not cancel or menu will not show
    _statusItem.Text = BuildStatusText(); // Safe: Opening fires on STA thread
};
```

This avoids background-thread updates entirely. Status is read at the moment the menu opens — always accurate, zero threading complexity required. Context menus only display while open, so point-in-time reads are sufficient — no live ticker needed.

**Warning signs:**
- InvalidOperationException: Cross-thread operation not valid with stack trace pointing to ToolStripItem.set_Text
- Status labels show stale data even after daemon has been running for minutes
- Status updates correctly when debugger is attached but not in production (timing-sensitive race)

**Phase to address:**
Phase implementing the enhanced context menu. Use the Opening event pattern exclusively. Do not use a background timer to update menu item text.

---

### Pitfall D-8: ContextMenuStrip.Opening Handler Does Not Set e.Cancel = false — Menu Never Appears

**What goes wrong:**
When a ContextMenuStrip.Opening handler is added for dynamic population, the menu never appears on the first right-click. The tray icon right-click produces no visible result. The menu starts working on subsequent attempts. Or: menu appears at the top-left corner of the screen rather than near the tray icon.

**Why it happens:**
The Opening event's CancelEventArgs.Cancel can be left in an indeterminate state if the handler throws an exception before completing, or if Items.Clear() is called and the handler exits early without repopulating. If e.Cancel is true when the handler returns, the menu is suppressed for that invocation.

A related issue: the first Opening trigger after process start sometimes requires SetForegroundWindow to have been called on the tray icon's host window to correctly position the menu. Without it, the menu appears at the top-left corner of the screen. WinForms NotifyIcon handles this internally for right-click events.

**How to avoid:**
Structure the Opening handler defensively:

```csharp
_contextMenu.Opening += (_, e) =>
{
    e.Cancel = false; // Explicitly allow opening — first line, always
    try
    {
        _contextMenu.Items.Clear();
        PopulateMenuItems(); // Add status label, Settings, Restart, Exit
    }
    catch
    {
        // On failure, add a fallback item rather than leaving menu empty
        _contextMenu.Items.Add("(menu error)");
    }
    // e.Cancel remains false — menu shows regardless of what happened above
};
```

**Warning signs:**
- Right-click on tray icon produces no menu on first click; works on second click
- Menu appears at top-left corner of screen rather than near the tray icon
- Menu disappears immediately after appearing without user input

**Phase to address:**
Phase implementing the enhanced context menu. Set e.Cancel = false as the first line of the Opening handler; populate items inside a try/catch.

---

### Pitfall D-9: Settings Form Close Button Discards Unsaved Changes Silently

**What goes wrong:**
The user edits settings (e.g., changes navigation strategy) and closes the window with the X button without clicking Save. Changes are silently discarded with no prompt. The user assumes their changes were saved.

**Why it happens:**
WinForms Form.FormClosing fires when the X button is clicked but by default the form just closes. Without a dirty-flag prompt or auto-save-on-change, the user has no feedback that their edits were lost.

**How to avoid:**
Choose one of two patterns and implement it completely — do not implement both half-way:

Option A — Explicit Save with dirty-flag prompt: Track a _isDirty flag set in any control's Changed event. On FormClosing, if _isDirty, show a YesNoCancel MessageBox. Yes: save then close. No: discard and close. Cancel: prevent close.

Option B — Auto-save-on-change (recommended for this use case): Apply each setting change to the config file immediately as the user modifies controls. This matches modern settings UI conventions. The daemon rereads config fresh per keypress, so changes take effect instantly without a restart. This eliminates the dirty-flag problem entirely — there is nothing to lose because every change is immediately durable.

For the focus daemon, auto-save-on-change is the cleaner pattern: it is consistent with the existing "fresh config load per keypress" design and removes the need for a Save button entirely.

**Warning signs:**
- User reports "I changed the strategy but it did not take effect"
- Settings always revert to previous values after reopening the window
- No save confirmation is shown when closing

**Phase to address:**
Phase implementing the settings form. Decide on explicit-Save vs auto-save-on-change in the design before writing any form code. Do not leave both patterns half-implemented.

---

### Pitfall D-10: ARGB Hex Color Input Accepts Invalid Values — Daemon Falls Back to Default Color Silently

**What goes wrong:**
The settings form includes text fields for overlay colors in hex ARGB format (e.g., FF2196F3). If the user enters an invalid value (such as #2196F3 with a hash prefix, 2196F3 without alpha, or ZZZZZZZZ), the value is saved to config.json as-is. FocusConfig.Load() deserializes the string; wherever the hex string is converted to a Color for rendering, it throws or silently uses a default. The daemon uses a default color without notifying the user. The settings form shows the user-entered value but the actual overlay color is different.

**Why it happens:**
Hex ARGB string parsing typically uses Convert.ToInt32(hex, 16) which throws FormatException on invalid input. If caught at config load time by the blanket catch in FocusConfig.Load(), the entire config is replaced by defaults — not just the invalid color field. The user loses all their other settings (strategy, grid fractions) along with the invalid color.

**How to avoid:**
Validate color input at the settings form level before writing to disk:

```csharp
private static bool TryParseArgbHex(string hex, out Color color)
{
    color = Color.Empty;
    // Accept exactly 8 hex chars, no prefix — AARRGGBB format
    if (hex.Length != 8) return false;
    if (!long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out long argb))
        return false;
    color = Color.FromArgb((int)argb);
    return true;
}
```

Display inline validation errors (red border on the text field, descriptive error label). Prevent Save (or auto-save write) if any color is invalid. Do not write an invalid config to disk.

**Warning signs:**
- Overlay colors revert to defaults after saving from the settings form
- User-entered hex values appear in text fields but do not affect the overlay color
- Verbose log shows "config parse error" after settings are saved

**Phase to address:**
Phase implementing color fields in the settings form. Validate all hex fields client-side in the form before writing. Enforce the 8-hex-char AARRGGBB format. Show an inline error on invalid input.

---

### Pitfall D-11: Using Environment.Exit() in Restart Path — Skips Managed Cleanup, Leaves Ghost Icon

**What goes wrong:**
The "Restart Daemon" click handler uses Environment.Exit(0) to terminate the current process. Environment.Exit() terminates the CLR immediately without running any finalizers or IDisposable.Dispose() calls. The NotifyIcon.Dispose() call in DaemonApplicationContext.Dispose never runs. The tray icon ghost persists. The WH_KEYBOARD_LL hook is removed automatically by Windows when the process exits (OS-level cleanup), so stuck keys are not an issue — but the ghost icon is.

**Why it happens:**
Self-restart guides commonly recommend Environment.Exit() as a simple one-liner after Process.Start(). It works for cleanup-free console applications but bypasses WinForms lifecycle events and component disposal.

**How to avoid:**
For the restart path, route through the existing clean-exit mechanism (the same code path as the "Exit" menu item):

```csharp
private void OnRestartClicked(object? sender, EventArgs e)
{
    // Start new instance before exiting (minimize gap; new instance kills us via DaemonMutex if slow)
    var selfPath = Environment.ProcessPath!;
    Process.Start(new ProcessStartInfo(selfPath, "daemon --background") { UseShellExecute = false });

    // Reuse existing exit path — sets Visible = false, calls _onExit(), calls Application.ExitThread()
    OnExitClicked(sender, e);
}
```

Do not introduce a separate Environment.Exit() call in the restart handler.

**Warning signs:**
- Ghost tray icon persists after restart
- Clean restart via menu leaves ghost — indicates Environment.Exit() being used instead of ExitThread
- Keyboard hook cleanup log messages do not appear after restart (Dispose path skipped)

**Phase to address:**
Phase implementing "Restart Daemon" context menu item. Route through OnExitClicked rather than introducing new exit code. Test that no ghost icon appears after restart.

---

## Updated Technical Debt Table (v4.0 Additions)

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| ShowDialog for settings window | Modal dialog simpler to reason about | Blocks STA message pump; keyboard hook dispatches stall | Never |
| No singleton guard on settings form | One less check | Multiple windows open; config file write conflicts | Never |
| Background timer updating ToolStripItem.Text | Live uptime counter | Cross-thread exception on WinForms controls | Never — use Opening event instead |
| Environment.Exit() in restart path | Simple one-liner | Skips managed cleanup; ghost tray icon | Never — use existing OnExitClicked path |
| No dirty-flag prompt on settings form close | Less code | User silently loses edits | Acceptable only if auto-save-on-change is used instead |
| Assign SystemIcons.Application permanently | No ICO asset needed | Generic icon; blurry at non-100% DPI | Acceptable for placeholder; never for shipped build |
| Direct File.WriteAllText for config save | Simple file write | Race condition with daemon FocusConfig.Load() | Acceptable in practice (window tiny); eliminate with rename pattern |

---

## Updated Integration Gotchas (v4.0 Additions)

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| NotifyIcon disposal | Not setting Visible = false before Dispose() | Always Visible = false then Dispose(); accept ghost icon on forced kill |
| Settings form open | new SettingsForm().Show() on every click | Keep reference; BringToFront() if already open |
| Settings form modal | ShowDialog() from tray click handler | Show() only; ShowDialog forbidden for daemon-lifecycle windows |
| Context menu dynamic labels | Background thread sets ToolStripItem.Text | Populate in Opening event (STA thread); do not use background timers |
| Context menu not appearing | Forgetting e.Cancel = false in Opening | First line of handler: e.Cancel = false |
| Config save race | File.WriteAllText concurrent with File.ReadAllText | Write to .tmp then File.Move with overwrite: true |
| Restart daemon exit path | Environment.Exit() for self-termination | Reuse existing OnExitClicked path; Application.ExitThread() on STA thread |
| CAPSLOCK state at restart | Restart while CAPSLOCK held leaks toggle to OS | ForceCapsLockOff() at new daemon startup already corrects; verify explicitly |
| ICO size selection | new Icon("focus.ico") directly assigned | new Icon(stream, SystemInformation.SmallIconSize) for tray icon |
| ARGB hex validation | Accepting invalid hex strings in color fields | Validate in settings form before saving; 8-char AARRGGBB only |

---

## Updated Looks-Done-But-Isnt Checklist (v4.0 Additions)

- [ ] **Ghost icon on restart**: Click "Restart Daemon" — verify only one icon visible after restart (or ghost disappears within 2 seconds)
- [ ] **ICO at 125% DPI**: Set Windows display to 125% DPI — verify tray icon is crisp, not blurry
- [ ] **ICO at 150% DPI**: Set Windows display to 150% DPI — verify tray icon is crisp
- [ ] **Settings singleton**: Click "Settings" from context menu three times rapidly — verify only one settings window opens; repeat click brings existing window to front
- [ ] **ShowDialog check**: Open settings, hold CAPSLOCK — verify overlay appears normally (settings not blocking message pump)
- [ ] **Opening event safety**: Right-click tray — verify menu appears on first click with current daemon status
- [ ] **Config race**: Click Save in settings while rapidly holding/releasing CAPSLOCK — verify no "config parse error" in verbose log
- [ ] **Restart clean exit**: Click "Restart Daemon" — verify old tray icon disappears cleanly, new icon appears
- [ ] **CAPSLOCK after restart**: Click "Restart Daemon" — verify CAPSLOCK toggle is OFF after new daemon starts (no stuck uppercase mode)
- [ ] **Dirty settings close**: Change a setting, close with X — verify either prompt appears or auto-save applied the change
- [ ] **Invalid color input**: Enter ZZZZZZZZ in a color field, attempt save — verify inline error shown, save blocked, config file not corrupted
- [ ] **Short uptime display**: Restart daemon; open context menu within 10 seconds — verify uptime shows correctly

---

## Updated Pitfall-to-Phase Mapping (v4.0 Additions)

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Ghost tray icon on kill (D-1) | Phase implementing Restart Daemon | Restart from menu — ghost icon visible less than 2 seconds; forced kill — ghost clears on hover |
| ICO size wrong at non-100% DPI (D-2) | Phase implementing custom tray icon | 125% and 150% DPI — icon crisp, matches notification area size |
| Settings opened multiple times (D-3) | Phase implementing settings window | Three rapid Settings clicks — one window; repeat click brings it to front |
| ShowDialog blocks message pump (D-4) | Phase implementing settings window | Hold CAPSLOCK while settings open — overlay renders normally |
| Config save race (D-5) | Phase implementing settings Save | Save during rapid CAPSLOCK hold/release — no config parse error in verbose log |
| CAPSLOCK stuck at restart (D-6) | Phase implementing Restart Daemon | Restart while CAPSLOCK held — no system-wide CAPS LOCK mode after restart |
| Context menu cross-thread update (D-7) | Phase implementing dynamic context menu | Open context menu after 10 minutes uptime — current uptime shown |
| Opening e.Cancel not set (D-8) | Phase implementing context menu Opening handler | First right-click after process start — menu appears on first attempt |
| Settings close without save (D-9) | Phase implementing settings form | Change strategy, close with X — prompt or auto-save; change persists |
| Invalid hex color input (D-10) | Phase implementing color fields in settings | Enter invalid hex, attempt save — inline error shown, file not written |
| Environment.Exit() in restart (D-11) | Phase implementing Restart Daemon | Click Restart — old tray icon gone cleanly; no ghost |

---

## Updated Sources (v4.0 Additions)

- [NotifyIcon not deleted when application closes — dotnet/winforms issue #6996](https://github.com/dotnet/winforms/issues/6996) — HIGH confidence (official dotnet/winforms issue; confirmed disposal pattern)
- [NotifyIcon does not use appropriate icon size — dotnet/winforms issue #6955](https://github.com/dotnet/winforms/issues/6955) — HIGH confidence (official dotnet/winforms issue; new Icon(stream, SystemInformation.SmallIconSize) workaround confirmed)
- [Windows 11 always takes bigger PNG from ICO — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/1425442/windows-11-always-takes-a-bigger-png-from-ico) — HIGH confidence (official Microsoft Q&A; DPI-aware icon loading with multiple sizes documented)
- [Handle ContextMenuStrip Opening Event — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-handle-the-contextmenustrip-opening-event) — HIGH confidence (official docs; e.Cancel = false pattern)
- [Cross-thread operations in WinForms — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls) — HIGH confidence (official docs; Control.Invoke pattern)
- [Creating Tray Applications in .NET: A Practical Guide — Red Gate Simple Talk](https://www.red-gate.com/simple-talk/development/dotnet-development/creating-tray-applications-in-net-a-practical-guide/) — MEDIUM confidence (practitioner guide; ApplicationContext tray-centric design)
- [Form.ShowDialog Method — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.form.showdialog) — HIGH confidence (official docs; blocking behavior documented)
- [Application.Exit vs Environment.Exit — C# Corner](https://www.c-sharpcorner.com/forums/applicationexit-vs-applicationshutdown-vs-environmentexit) — MEDIUM confidence (practitioner; consistent with WinForms docs on message pump teardown)

---

*v4.0 additions: System tray polish, settings UI, dynamic context menu, daemon restart*
*Researched: 2026-03-03*

