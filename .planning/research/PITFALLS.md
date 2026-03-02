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
