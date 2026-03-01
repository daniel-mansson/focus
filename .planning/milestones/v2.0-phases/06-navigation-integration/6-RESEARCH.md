# Phase 6: Navigation Integration - Research

**Researched:** 2026-03-01
**Domain:** Win32 WinEvent hooks, foreground window detection, overlay animation, daemon wiring
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **Activation feel:** No intentional activation delay — overlays appear as soon as CAPSLOCK hold is detected. If detection requires polling, maximum poll interval is 100ms. The `overlayDelayMs` config key should still exist for user tuning, but default to 0. CAPSLOCK taps (quick press-and-release) are fully suppressed — no toggle behavior at all. On daemon startup, force CAPSLOCK toggle state OFF so users never end up typing in caps.
- **Overlay transitions:** Show: Quick fade-in (~100ms) from transparent to full opacity when overlays first appear. Dismiss: Quick fade-out (~80ms) when CAPSLOCK is released. Reposition (foreground change): Instant — old overlays vanish, new overlays appear at new positions immediately (no cross-fade). Live tracking: No — overlays only recompute when the foreground window changes, not when target windows resize/move mid-hold.
- **Multi-direction overlap:** If one window is the best candidate for multiple directions, show ALL borders — one overlay window per direction, stacked on the same target. Separate overlay windows per direction (reuse existing OverlayWindow architecture). No special handling at corners. No limit on overlays per target window.
- **No-candidate indication:** When a direction has no reachable window: show nothing (silent absence). Special case — when ALL four directions have zero candidates (solo window): show a dim/muted border on all edges of the source (foreground) window, confirming the daemon is active. This dim source border ONLY appears in the "totally alone" case.

### Claude's Discretion

- Exact dim border color/opacity for the solo-window indicator
- Internal architecture for wiring CapsLockMonitor state changes to OverlayManager
- Timer/animation mechanism for fade-in/fade-out (WinForms Timer, async Task.Delay, etc.)
- How to efficiently detect foreground window changes (WinEventHook vs polling)

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| OVERLAY-01 | Overlay renders colored borders on the top-ranked target window for each of the 4 directions simultaneously | NavigationService.GetRankedCandidates (existing) + OverlayManager.ShowOverlay (existing) + per-direction loop pattern |
| OVERLAY-03 | Overlay dismisses immediately when CAPSLOCK is released | CapsLockMonitor.OnCapsLockReleased hook + OverlayManager.HideAll on STA thread |
| OVERLAY-04 | Overlay updates target positions when foreground window changes while CAPSLOCK is held | SetWinEventHook EVENT_SYSTEM_FOREGROUND — callbacks delivered on STA thread with message pump |
| OVERLAY-05 | Overlay gracefully handles directions with no candidate (no overlay rendered for that direction) | NavigationService returns empty list — skip ShowOverlay for directions with no ranked[0] |
| DAEMON-03 | Daemon debounces CAPSLOCK hold with configurable activation delay before showing overlay | `overlayDelayMs` field in FocusConfig (default 0) + optional WinForms Timer delay before ShowOverlay |
| CFG-06 | Activation delay configurable in JSON config (overlayDelayMs, default ~150ms — overridden to 0) | FocusConfig property `OverlayDelayMs` (int, default 0) per user decision |
</phase_requirements>

## Summary

Phase 6 wires the three completed subsystems — keyboard hook (Phase 4), overlay rendering (Phase 5), and navigation scoring (Phase 2) — into a single user-facing experience. The daemon already has `CapsLockMonitor` with empty `OnCapsLockHeld` / `OnCapsLockReleased` stubs explicitly marked "Phase 6 will hook overlay show/hide logic here." This phase fills those stubs.

The two core engineering problems are: (1) routing overlay show/hide calls from the worker thread (where `CapsLockMonitor` runs) onto the STA thread (where `OverlayManager` must operate), and (2) detecting foreground window changes while CAPSLOCK is held so overlays reposition to reflect the new source window. Both problems have well-established Win32 solutions. `System.Windows.Forms.Timer` fires on the STA thread making it the natural mechanism for fade animation steps. `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` delivers foreground-change callbacks on the thread that installed the hook — which must be the STA thread — making it the preferred detection mechanism over polling.

The fade-in/fade-out feature adds minor complexity: it requires adjusting the per-pixel alpha values on each tick of the fade animation, scaling the base color's alpha by a progress factor (0.0 → 1.0 for fade-in, 1.0 → 0.0 for fade-out) before calling `BorderRenderer.Paint`. The solo-window dim indicator is the single novel rendering case: when all four directions return empty candidate lists, one overlay per direction should be shown on the foreground window itself with a muted color (Claude's discretion for exact color/opacity).

**Primary recommendation:** Add `OverlayDelayMs` to `FocusConfig`, expand `CapsLockMonitor` to accept action callbacks injected at construction, use `Control.Invoke` (or a WinForms control on the STA thread) to marshal overlay calls, install `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` on the STA thread inside `DaemonApplicationContext`, and use `System.Windows.Forms.Timer` for fade steps.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| CsWin32 | 0.3.269 (already in project) | `SetWinEventHook`, `UnhookWinEvent` P/Invoke | Already established; generates all required Win32 wrappers |
| System.Windows.Forms | net8.0-windows (already in project) | STA message pump, `Timer`, `Control.Invoke` for thread marshaling | Already running; overlay HWNDs live on this thread |
| System.CommandLine | 2.0.3 (already in project) | CLI — no changes needed | Established |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Windows.Forms.Timer | (built-in) | Fade-in/fade-out animation ticks on STA thread | Each ~16ms tick scales overlay alpha |
| GCHandle (System.Runtime.InteropServices) | (built-in) | Pin the `WINEVENTPROC` delegate | Required: GC must not move delegate while Win32 holds a pointer |

### New NativeMethods.txt Entries

```
SetWinEventHook
UnhookWinEvent
keybd_event
```

- `SetWinEventHook` — installs the foreground-change hook (OVERLAY-04)
- `UnhookWinEvent` — cleanup on daemon shutdown
- `keybd_event` — force CAPSLOCK toggle state OFF at startup (DAEMON-02 startup invariant)

**Note:** `WINEVENTPROC` delegate type is generated by CsWin32 when `SetWinEventHook` is in NativeMethods.txt. `EVENT_SYSTEM_FOREGROUND` (0x0003) and `WINEVENT_OUTOFCONTEXT` (0x0000) are constants that must be defined manually in C# code — they are NOT generated by CsWin32.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SetWinEventHook | Polling GetForegroundWindow every 100ms | Polling works but wastes CPU and has up to 100ms latency; WinEvent hook is instant and zero-cost when idle |
| WinForms Timer for fade | `Task.Delay` + async loop | Task.Delay requires `Control.Invoke` on every tick to reach STA; Timer fires directly on STA — no marshaling needed |
| WinForms Timer for fade | `System.Threading.Timer` | Threading.Timer fires on thread pool — needs Invoke on every tick |

## Architecture Patterns

### Recommended Project Structure

No new folders needed. All new files belong in existing structure:

```
focus/
├── Windows/
│   └── Daemon/
│       ├── CapsLockMonitor.cs       # MODIFY: add callbacks, activation delay, fade state
│       ├── DaemonCommand.cs         # MODIFY: inject OverlayManager + ForegroundMonitor
│       ├── DaemonApplicationContext.cs  # MODIFY: create ForegroundMonitor, install WinEvent hook
│       ├── ForegroundMonitor.cs     # NEW: wraps SetWinEventHook for EVENT_SYSTEM_FOREGROUND
│       └── Overlay/
│           ├── OverlayManager.cs    # MODIFY: add ShowAllDirections, ShowSoloWindow, fade support
│           └── OverlayOrchestrator.cs  # NEW: coordinates CapsLockMonitor + OverlayManager + ForegroundMonitor
├── FocusConfig.cs                   # MODIFY: add OverlayDelayMs (int, default 0)
```

### Pattern 1: Cross-Thread Marshaling via ApplicationContext NativeWindow

The existing `DaemonApplicationContext` already creates a hidden `NativeWindow` (`PowerBroadcastWindow`). The STA thread runs `Application.Run()` inside `DaemonApplicationContext`. To marshal calls from the CapsLockMonitor worker thread to the STA thread, use a `NativeWindow`'s `Handle` with `Control.FromHandle` — but the simplest approach is to create a minimal `Control` on the STA thread and use its `Invoke` method.

**What:** CapsLockMonitor worker thread posts work to STA via `control.Invoke(action)`.
**When to use:** Any time overlay show/hide/update must happen on the STA message pump thread.

```csharp
// Source: Microsoft docs — Control.Invoke marshals action to UI thread
// Created on STA thread in DaemonApplicationContext or OverlayOrchestrator constructor:
private readonly Control _staDispatcher = new Control();
// Ensure handle is created before the control is used for Invoke:
_ = _staDispatcher.Handle;  // Force handle creation

// From worker thread (CapsLockMonitor):
_staDispatcher.Invoke(() => _overlayManager.ShowAllDirections(...));
```

**CRITICAL:** `Control.Handle` must be accessed on the STA thread before the control is used for `Invoke`. Creating the control and reading `.Handle` on the STA thread forces Win32 handle creation.

### Pattern 2: SetWinEventHook for Foreground Window Changes

**What:** Installs a global WinEvent hook that fires on the STA thread whenever any process brings a window to the foreground.
**When to use:** While CAPSLOCK is held — install on hold, uninstall on release (or keep installed always and check `_isHeld` in callback).

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
// Must be called on STA thread (the thread with a message pump)
// EVENT_SYSTEM_FOREGROUND = 0x0003
// WINEVENT_OUTOFCONTEXT = 0x0000
// WINEVENT_SKIPOWNPROCESS = 0x0002

private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

// Delegate MUST be stored in a static or instance field — never a local variable.
// GCHandle pins it to prevent GC relocation while Win32 holds the pointer.
private WINEVENTPROC? _winEventProc;
private GCHandle _winEventHandle;
private HWINEVENTHOOK _hookHandle;

public void Install()
{
    _winEventProc = OnForegroundChanged;
    _winEventHandle = GCHandle.Alloc(_winEventProc);  // Pin delegate

    _hookHandle = PInvoke.SetWinEventHook(
        EVENT_SYSTEM_FOREGROUND,
        EVENT_SYSTEM_FOREGROUND,
        HMODULE.Null,  // null for WINEVENT_OUTOFCONTEXT
        _winEventProc,
        0,   // all processes
        0,   // all threads
        WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
}

public void Uninstall()
{
    if (_hookHandle != default)
    {
        PInvoke.UnhookWinEvent(_hookHandle);
        _hookHandle = default;
    }
    if (_winEventHandle.IsAllocated)
        _winEventHandle.Free();
    _winEventProc = null;
}

// Called on STA thread — safe to call OverlayManager directly
private void OnForegroundChanged(HWINEVENTHOOK hook, uint eventType,
    HWND hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
{
    if (!_capsLockHeld) return;
    _onForegroundChanged?.Invoke(hwnd);  // action injected from orchestrator
}
```

**CRITICAL constraints:**
1. `SetWinEventHook` must be called on the STA thread (the thread with the message pump)
2. `UnhookWinEvent` must be called from the same thread that called `SetWinEventHook`
3. Delegate must be stored in a field and pinned with `GCHandle.Alloc()` — not a local variable
4. Use `WINEVENT_OUTOFCONTEXT` — avoids DLL injection, works across 32/64-bit boundaries

### Pattern 3: Fade Animation via WinForms Timer

**What:** `System.Windows.Forms.Timer` fires on STA thread, allowing direct calls to `BorderRenderer.Paint` with scaled alpha.
**When to use:** Fade-in (CAPSLOCK held) and fade-out (CAPSLOCK released).

```csharp
// Source: Microsoft docs — System.Windows.Forms.Timer fires on UI/STA thread
private readonly System.Windows.Forms.Timer _fadeTimer = new();
private float _fadeProgress; // 0.0 = transparent, 1.0 = full opacity
private bool _fadingIn;      // true=fade in, false=fade out

private void StartFadeIn()
{
    _fadingIn = true;
    _fadeProgress = 0.0f;
    _fadeTimer.Interval = 16;  // ~60fps
    _fadeTimer.Tick += OnFadeTick;
    _fadeTimer.Start();
}

private void OnFadeTick(object? sender, EventArgs e)
{
    float step = _fadingIn
        ? (16f / 100f)   // 100ms fade-in: 100ms / 16ms ≈ 6-7 ticks to reach 1.0
        : (16f / 80f);   // 80ms fade-out: 80ms / 16ms ≈ 5 ticks to reach 0.0

    _fadeProgress = _fadingIn
        ? Math.Min(1.0f, _fadeProgress + step)
        : Math.Max(0.0f, _fadeProgress - step);

    // Re-paint all visible overlays with scaled alpha
    RepaintAllOverlays(_fadeProgress);

    bool done = _fadingIn ? _fadeProgress >= 1.0f : _fadeProgress <= 0.0f;
    if (done)
    {
        _fadeTimer.Stop();
        if (!_fadingIn)
            _overlayManager.HideAll(); // Now truly hide after fade-out completes
    }
}
```

**Alpha scaling in Paint call:** Pass `(uint)(argbColor & 0x00FFFFFF | ((uint)(baseAlpha * fadeProgress) << 24))` to `OverlayManager.ShowOverlay`. Or add an `alphaScale` parameter to `ShowOverlay`.

### Pattern 4: Force CAPSLOCK Toggle State OFF at Startup

**What:** Send a synthetic VK_CAPITAL keydown+keyup via `keybd_event` if the current toggle state is ON (low-order bit of `GetKeyState(VK_CAPITAL)` is set).
**When to use:** Once at daemon startup, before the keyboard hook is installed.

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-keybd_event
// VK_CAPITAL = 0x14
// KEYEVENTF_KEYUP = 0x0002
private static void ForceCapsLockOff()
{
    const int VK_CAPITAL = 0x14;
    // GetKeyState low-order bit = 1 means toggle is ON
    if ((PInvoke.GetKeyState(VK_CAPITAL) & 0x0001) != 0)
    {
        // Toggle it off with a synthetic down+up
        PInvoke.keybd_event(VK_CAPITAL, 0x45, 0, 0);         // key down
        PInvoke.keybd_event(VK_CAPITAL, 0x45, 0x0002, 0);    // key up (KEYEVENTF_KEYUP)
    }
}
```

**Note:** The keyboard hook suppresses the CAPSLOCK toggle change during normal operation (returns 1 to eat the event). The `ForceCapsLockOff` call at startup handles the case where the daemon starts while CAPSLOCK is already toggled ON (e.g., the user had it on before launching).

### Pattern 5: ShowAllDirections Orchestration

**What:** For each of the four directions, score candidates and show/hide the overlay.
**When to use:** On CAPSLOCK hold and on foreground window change.

```csharp
// Called on STA thread only
private void ShowOverlaysForCurrentForeground()
{
    var enumerator = new WindowEnumerator();
    var (windows, _) = enumerator.GetNavigableWindows();
    var filtered = ExcludeFilter.Apply(windows, _config.Exclude);

    int candidatesFound = 0;

    foreach (var dir in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
    {
        var ranked = NavigationService.GetRankedCandidates(filtered, dir, _config.Strategy);
        if (ranked.Count == 0)
        {
            _overlayManager.HideOverlay(dir);  // OVERLAY-05: no candidate = no overlay
            continue;
        }

        candidatesFound++;
        var top = ranked[0].Window;
        var bounds = new RECT { left = top.Left, top = top.Top, right = top.Right, bottom = top.Bottom };
        _overlayManager.ShowOverlay(dir, bounds);
    }

    // Solo-window case: all directions empty — show dim border on source window
    if (candidatesFound == 0)
    {
        ShowSoloWindowIndicator(filtered);
    }
}
```

### Pattern 6: Solo-Window Indicator

**What:** When zero candidates exist in all four directions, show a dim overlay on the foreground window itself.
**When to use:** Only when all four directions have empty candidate lists.

```csharp
// Claude's discretion: dim color — same hue as the direction but at ~25% opacity (0x40 alpha)
// Recommended: use a neutral gray-white 0x40AAAAAA or direction-colored dim border
private void ShowSoloWindowIndicator(List<WindowInfo> filtered)
{
    // Get foreground window bounds
    var fgHwnd = PInvoke.GetForegroundWindow();
    RECT fgBounds = default;
    var hr = PInvoke.DwmGetWindowAttribute(fgHwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, ...);
    if (hr.Failed) return;

    // Show all four overlays on the same window with dim color (e.g., 0x30AAAAAA = ~19% alpha gray)
    foreach (var dir in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
    {
        _overlayManager.ShowOverlay(dir, fgBounds, dimColor: 0x30AAAAAA);
    }
}
```

**Note:** `OverlayManager.ShowOverlay` currently uses the configured direction color from `OverlayColors`. For the solo indicator, an override color parameter is needed, or a separate `ShowOverlaySolo` method.

### Anti-Patterns to Avoid

- **Calling OverlayManager from worker thread:** OverlayWindow HWNDs are created on the STA thread. Win32 window operations (SetWindowPos, UpdateLayeredWindow, ShowWindow) must be called from the thread that owns the HWND. Always marshal via `Control.Invoke`.
- **Storing WINEVENTPROC delegate in a local variable:** GC will collect it after the method returns. Must be a field.
- **Calling UnhookWinEvent from a different thread:** Must be called from the same thread that called SetWinEventHook — i.e., the STA thread.
- **Running NavigationService.GetRankedCandidates on the STA thread for long periods:** Enumeration is fast (~1-5ms typically) but should be measured. If it causes jank, move to worker thread and post results back to STA. For now, calling on STA is acceptable given the infrequency of foreground changes.
- **Calling OverlayManager.ShowOverlay with a 0×0 RECT:** BorderRenderer.Paint already guards against zero-area bounds (`if (width <= 0 || height <= 0) return`), but the overlay window should not be shown at all.
- **Mixing fade timer with foreground-change reposition:** On foreground change, cancel any in-progress fade-in and restart it. If the fade-out timer is running (CAPSLOCK released), a foreground change should be ignored — overlays are already being dismissed.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread marshaling to STA | Custom synchronized queue | `Control.Invoke` | WinForms already provides thread-safe Invoke; custom queues reinvent work and have edge cases on shutdown |
| Foreground window polling | `while (held) { GetForegroundWindow(); Thread.Sleep(100); }` | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | Hook is instant, zero CPU when idle; polling wastes CPU and has 100ms latency |
| Fade timer | Custom `Task.Delay` loop | `System.Windows.Forms.Timer` | Timer fires on STA thread natively; Task.Delay needs Invoke on every tick |
| CAPSLOCK LED control | SetKeyboardState | `keybd_event` + VK_CAPITAL toggle | SetKeyboardState only affects calling thread's state, not system-wide LED state |

**Key insight:** All the threading and messaging infrastructure already exists in the WinForms STA loop. The pattern is: install hooks on STA, receive callbacks on STA, update overlays on STA. Worker threads (CapsLockMonitor) post work to STA via Control.Invoke.

## Common Pitfalls

### Pitfall 1: WINEVENTPROC Delegate Garbage Collected
**What goes wrong:** `SetWinEventHook` succeeds, returns non-null handle, but the callback never fires or the process crashes with an access violation.
**Why it happens:** The delegate was stored in a local variable. After the method returns, GC collects the delegate (no managed references remain), and Win32 later calls the freed memory address.
**How to avoid:** Store the delegate in an instance field AND call `GCHandle.Alloc(delegate)` to pin it. Free the GCHandle in `Uninstall()`.
**Warning signs:** Callback fires once or twice, then stops; or process crashes after some time.

### Pitfall 2: UnhookWinEvent Called from Wrong Thread
**What goes wrong:** `UnhookWinEvent` silently fails (returns false), hook remains installed.
**Why it happens:** Cleanup code calls UnhookWinEvent from the main thread or a thread pool thread, but the hook was installed on the STA thread.
**How to avoid:** Always call UnhookWinEvent from the STA thread. In `DaemonApplicationContext.Dispose` or in the STA thread's cleanup path.
**Warning signs:** Overlays continue appearing after daemon shutdown; foreground change callbacks still fire.

### Pitfall 3: OverlayManager Called from Worker Thread
**What goes wrong:** `SetWindowPos` or `UpdateLayeredWindow` fails with ERROR_ACCESS_DENIED or silently does nothing.
**Why it happens:** Win32 window operations on an HWND must be called from the thread that created the HWND. The STA thread created the OverlayWindow HWNDs.
**How to avoid:** Always use `_staDispatcher.Invoke(...)` to marshal overlay calls to the STA thread. Never call `overlayManager.ShowOverlay` directly from `CapsLockMonitor.OnCapsLockHeld`.
**Warning signs:** Overlays never appear even though `ShowOverlay` is being called; GetLastError returns thread-related errors.

### Pitfall 4: GetKeyState Returns Stale State in WinEvent Callback
**What goes wrong:** Foreground change fires, but `GetForegroundWindow()` still returns the old window.
**Why it happens:** `WINEVENT_OUTOFCONTEXT` delivers events asynchronously. The `hwnd` parameter passed to the callback IS the new foreground window — use it directly instead of calling `GetForegroundWindow()` inside the callback.
**How to avoid:** Use the `hwnd` parameter of the `WINEVENTPROC` callback directly to get the new foreground window's bounds. Do not call `GetForegroundWindow()` inside the callback.
**Warning signs:** Overlays position themselves on the wrong window when the foreground changes rapidly.

### Pitfall 5: WINEVENT_OUTOFCONTEXT Constant Not in CsWin32 Enums
**What goes wrong:** Build error — `WINEVENT_OUTOFCONTEXT` not found.
**Why it happens:** CsWin32 generates the `SetWinEventHook` function but may not emit all associated constants as named enum values.
**How to avoid:** Define these constants manually in the consuming class: `private const uint WINEVENT_OUTOFCONTEXT = 0x0000; private const uint EVENT_SYSTEM_FOREGROUND = 0x0003; private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;`
**Warning signs:** Compilation errors referencing missing constants.

### Pitfall 6: CAPSLOCK Toggle State Mismatch on Sleep/Wake
**What goes wrong:** After sleep/wake, CAPSLOCK LED is ON even though the daemon has been suppressing it.
**Why it happens:** The existing `PowerBroadcastWindow` already reinstalls the keyboard hook on wake. However, if the CAPSLOCK toggle state was restored to ON by the OS during sleep, it needs to be forced off again.
**How to avoid:** Call `ForceCapsLockOff()` in `PowerBroadcastWindow.WndProc` after reinstalling the hook (Phase 4's PowerBroadcastWindow already calls `_hook.Install()` and `_monitor.ResetState()` — add `ForceCapsLockOff()` call there).
**Warning signs:** After system sleep/wake, users find themselves typing in caps.

### Pitfall 7: Fade Timer Still Running When CAPSLOCK Released During Fade-In
**What goes wrong:** User taps CAPSLOCK quickly — holds, releases before fade-in completes. Overlays stay visible partially faded.
**Why it happens:** Fade-in timer is running. Release signal arrives. If fade-out is started while fade-in is mid-progress, they conflict.
**How to avoid:** On CAPSLOCK release, always stop any in-progress fade-in timer and transition directly to fade-out (starting from current `_fadeProgress`). The fade-out should start from wherever the fade-in left off.
**Warning signs:** Overlays remain partially visible after quick press-and-release sequences.

## Code Examples

### Adding OverlayDelayMs to FocusConfig

```csharp
// Source: existing FocusConfig.cs pattern
internal class FocusConfig
{
    // ... existing fields ...
    public int OverlayDelayMs { get; set; } = 0;  // CFG-06: default 0 per user decision
}
```

### ForegroundMonitor Class Skeleton

```csharp
// Source: SetWinEventHook official docs + CsWin32 discussion #1162
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class ForegroundMonitor : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private readonly Action<HWND> _onChanged;
    private WINEVENTPROC? _proc;
    private GCHandle _procHandle;
    private HWINEVENTHOOK _hook;
    private bool _disposed;

    public ForegroundMonitor(Action<HWND> onForegroundChanged)
    {
        _onChanged = onForegroundChanged;
    }

    // Must be called on STA thread
    public void Install()
    {
        _proc = Callback;
        _procHandle = GCHandle.Alloc(_proc);
        _hook = PInvoke.SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    // Must be called on same STA thread that called Install()
    public void Uninstall()
    {
        if (_hook != default) { PInvoke.UnhookWinEvent(_hook); _hook = default; }
        if (_procHandle.IsAllocated) _procHandle.Free();
        _proc = null;
    }

    private void Callback(HWINEVENTHOOK hook, uint ev, HWND hwnd,
        int idObj, int idChild, uint thread, uint time)
    {
        _onChanged(hwnd);
    }

    public void Dispose() { if (!_disposed) { _disposed = true; Uninstall(); } }
}
```

### CapsLockMonitor Expansion for Callbacks

```csharp
// Expand existing CapsLockMonitor to accept injected callbacks
internal sealed class CapsLockMonitor
{
    private readonly ChannelReader<KeyEvent> _reader;
    private readonly bool _verbose;
    private bool _isHeld;

    // Phase 6: injected by orchestrator
    private readonly Action? _onHeld;
    private readonly Action? _onReleased;

    public CapsLockMonitor(ChannelReader<KeyEvent> reader, bool verbose,
        Action? onHeld = null, Action? onReleased = null)
    {
        _reader = reader;
        _verbose = verbose;
        _onHeld = onHeld;
        _onReleased = onReleased;
    }

    private void OnCapsLockHeld()
    {
        if (_verbose) Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK held");
        _onHeld?.Invoke();  // marshals to STA thread via Control.Invoke inside the action
    }

    private void OnCapsLockReleased()
    {
        if (_verbose) Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK released");
        _onReleased?.Invoke();
    }
}
```

### OverlayOrchestrator Constructor (STA Thread)

```csharp
// All members created on STA thread; overlay calls execute on STA thread
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class OverlayOrchestrator : IDisposable
{
    private readonly OverlayManager _overlayManager;
    private readonly FocusConfig _config;
    private readonly Control _staDispatcher;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly ForegroundMonitor _foregroundMonitor;
    private float _fadeProgress;
    private bool _fadingIn;
    private bool _capsLockHeld;

    // Must be constructed on STA thread
    public OverlayOrchestrator(OverlayManager overlayManager, FocusConfig config)
    {
        _overlayManager = overlayManager;
        _config = config;

        _staDispatcher = new Control();
        _ = _staDispatcher.Handle;  // Force HWND creation on STA thread

        _fadeTimer = new System.Windows.Forms.Timer();
        _fadeTimer.Interval = 16;
        _fadeTimer.Tick += OnFadeTick;

        _foregroundMonitor = new ForegroundMonitor(OnForegroundChanged);
        _foregroundMonitor.Install();  // Must be called on STA thread
    }

    // Called from worker thread (CapsLockMonitor) — marshals to STA
    public void OnCapsLockHeld() => _staDispatcher.Invoke(OnHeldSta);
    public void OnCapsLockReleased() => _staDispatcher.Invoke(OnReleasedSta);

    // These run on STA thread
    private void OnHeldSta() { _capsLockHeld = true; StartFadeIn(); ShowOverlaysForCurrentForeground(); }
    private void OnReleasedSta() { _capsLockHeld = false; StartFadeOut(); }
    private void OnForegroundChanged(HWND hwnd) { if (_capsLockHeld) ShowOverlaysForCurrentForeground(); }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Polling GetForegroundWindow in a loop | SetWinEventHook EVENT_SYSTEM_FOREGROUND | Windows 2000+ | Zero CPU overhead when idle; instant notification |
| SetLayeredWindowAttributes for alpha | UpdateLayeredWindow + premultiplied DIB | Windows 2000+ (established in Phase 5) | Per-pixel alpha; required for directional edge rendering |
| GDI RoundRect full border | Direct pixel buffer writes per direction (Phase 5 quick-7) | 2026-03-01 | Selective edge rendering with corner fade; more precise control |

**Note on REQUIREMENTS.md Out-of-Scope entry:** The requirements list "Animated overlay transitions (fade in/out)" as out-of-scope, but the CONTEXT.md decisions override this — the user explicitly decided fade-in/out is wanted. The planner should note this override in the plan.

## Open Questions

1. **Does `Control.Invoke` work correctly during daemon shutdown?**
   - What we know: `Control.Invoke` marshals to the STA message pump. During shutdown, `Application.ExitThread()` is called, which stops the pump.
   - What's unclear: If `CapsLockMonitor` tries to `Invoke` after `Application.ExitThread()` is called, it may throw `ObjectDisposedException` or block.
   - Recommendation: Wrap `_staDispatcher.Invoke(...)` calls in try/catch for `ObjectDisposedException` and `InvalidOperationException`. Add a `_shutdownRequested` volatile bool that CapsLockMonitor checks before invoking.

2. **Should ForegroundMonitor stay installed always, or only while CAPSLOCK is held?**
   - What we know: Installing/uninstalling the hook on every CAPSLOCK press/release is technically feasible. A always-on hook is simpler — just check `_capsLockHeld` in the callback.
   - What's unclear: Performance cost of always-on hook (likely negligible — EVENT_SYSTEM_FOREGROUND fires infrequently).
   - Recommendation: Keep installed always during daemon lifetime; check `_capsLockHeld` in callback. Simpler lifecycle, no race condition between install/uninstall and rapid key events.

3. **How to pass dim color for solo-window indicator without changing OverlayManager.ShowOverlay signature?**
   - What we know: `OverlayManager.ShowOverlay(Direction, RECT)` uses `_colors.GetArgb(direction)`. Adding a solo indicator requires a different color.
   - What's unclear: Best API design — override color parameter vs separate method.
   - Recommendation: Add `ShowOverlay(Direction direction, RECT bounds, uint? colorOverride = null)` overload. Pass the dim color as `colorOverride` in the solo case. Minimal signature change.

## Sources

### Primary (HIGH confidence)

- Microsoft Docs — [SetWinEventHook function](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) — hook signature, threading requirements, WINEVENT_OUTOFCONTEXT semantics
- Microsoft Docs — [WINEVENTPROC callback](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nc-winuser-wineventproc) — exact callback signature
- Existing codebase — `CapsLockMonitor.cs`, `DaemonApplicationContext.cs`, `OverlayManager.cs` — Phase 6 stubs explicitly present
- Existing codebase — `NativeMethods.txt` — current list of CsWin32-generated APIs
- Existing `DaemonCommand.cs` — `Control.Invoke` marshaling pattern via `_staDispatcher` is the established WinForms approach

### Secondary (MEDIUM confidence)

- [CsWin32 Discussion #1162](https://github.com/microsoft/CsWin32/discussions/1162) — SetWinEventHook with CsWin32: delegate must be static or stored in field; GCHandle.Alloc required
- Microsoft Docs — [System.Windows.Forms.Timer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.timer) — fires on STA/UI thread; Interval in ms; 55ms minimum accuracy (16ms interval is fine)
- Microsoft Docs — [keybd_event](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-keybd_event) — force CAPSLOCK toggle off; `GetKeyState(VK_CAPITAL) & 0x0001` to check current toggle state

### Tertiary (LOW confidence)

- None — all findings verified against official sources.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in project; only NativeMethods.txt entries are new
- Architecture: HIGH — SetWinEventHook requirements verified against official docs; threading patterns verified against WinForms documentation
- Pitfalls: HIGH — delegate GC issue verified in CsWin32 discussion #1162; thread ownership for Win32 window operations is documented Win32 behavior

**Research date:** 2026-03-01
**Valid until:** 2026-09-01 (stable Win32 APIs; WinForms APIs stable)
