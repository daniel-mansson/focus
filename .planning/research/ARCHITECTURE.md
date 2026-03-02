# Architecture Research

**Domain:** Win32 Window Management — CLI + Daemon with Keyboard Hook and Overlay Rendering
**Researched:** 2026-02-28 (v1.0/v2.0), 2026-03-02 (v3.1 update)
**Confidence:** HIGH (Win32 API documentation verified from official sources; keyboard hook and layered window APIs have remained stable since Windows 2000/Vista; overlay pattern verified against multiple sources)

---

## Part 1: Existing Architecture (v1.0 — Stateless CLI)

This section documents the architecture that already exists and must not break.

### System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         CLI Entry Point                          │
│                      (Program.cs / Main)                         │
│   Parses args → loads config → orchestrates pipeline             │
├──────────────────┬──────────────────────────────────────────────┤
│  Config Layer    │  Args Layer                                   │
│  (JSON config)   │  (CLI flags override config)                  │
├──────────────────┴──────────────────────────────────────────────┤
│                     Window Enumeration Layer                      │
│   EnumWindows → IsWindowVisible → DWMWA_CLOAKED → IsIconic      │
│   WS_EX_TOOLWINDOW / WS_EX_APPWINDOW → exclude list filter      │
├─────────────────────────────────────────────────────────────────┤
│                    Window Geometry Layer                          │
│   DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) → RECT      │
│   Derives center point (x, y) for each candidate                 │
├─────────────────────────────────────────────────────────────────┤
│                    Candidate Scoring Layer                        │
│   Current window center → direction filter → scoring algorithm   │
│   Balanced | StrongAxisBias | ClosestInDirection | EdgeMatching  │
│   | EdgeProximity | AxisOnly strategies                          │
├─────────────────────────────────────────────────────────────────┤
│                    Focus Activation Layer                         │
│   SendInput(VK_MENU press) → SetForegroundWindow(hwnd)           │
│   SendInput(VK_MENU release)                                     │
├─────────────────────────────────────────────────────────────────┤
│                    Win32 Native Interop Layer                     │
│   P/Invoke via CsWin32 (NativeMethods.txt source-generated)     │
│   user32.dll, dwmapi.dll, kernel32.dll                          │
└─────────────────────────────────────────────────────────────────┘
```

### Existing Component Responsibilities

| Component | File | Responsibility |
|-----------|------|----------------|
| Entry Point | `Program.cs` | Parse CLI args with System.CommandLine, load config, orchestrate pipeline, set exit code |
| Config | `Windows/FocusConfig.cs` | JSON load/default/write; Strategy and WrapBehavior enums |
| Window Enumerator | `Windows/WindowEnumerator.cs` | EnumWindows callback → Alt+Tab filter → UWP dedup → WindowInfo list |
| Exclude Filter | `Windows/ExcludeFilter.cs` | Apply user exclude list (glob patterns) to window list |
| Navigation Service | `Windows/NavigationService.cs` | Static scoring engine; 6 strategies; GetRankedCandidates → sorted (Window, Score) list |
| Focus Activator | `Windows/FocusActivator.cs` | SendInput(Alt) + SetForegroundWindow + wrap-around logic |
| Window Info | `Windows/WindowInfo.cs` | Immutable record: HWND, ProcessName, Title, bounds (L/T/R/B), MonitorIndex |
| Monitor Helper | `Windows/MonitorHelper.cs` | EnumDisplayMonitors, MonitorFromWindow, primary monitor center fallback |
| Direction | `Windows/Direction.cs` | Direction enum + DirectionParser |

**Key design facts:**
- `NavigationService` is a `static class` with static methods — no instance, no state between calls.
- `WindowEnumerator` is an instance class (stateless; instantiated fresh per invocation).
- `FocusActivator` is a `static class`.
- The whole pipeline runs once and the process exits.
- `Program.cs` uses top-level statements — no explicit `Main()` method.
- CsWin32 generates all Win32 P/Invoke declarations from `NativeMethods.txt`.

---

## Part 2: v2.0 Target Architecture — Daemon + Overlay

### What Changes Fundamentally

The daemon mode introduces three things that do not exist in the stateless CLI:

1. **A persistent process with a Win32 message loop** — the process does not exit after one action; it runs indefinitely, pumping messages.
2. **A system-wide low-level keyboard hook (WH_KEYBOARD_LL)** — the hook fires on every keypress system-wide and must respond within ~1 second or Windows silently removes it (Windows 10 1709+ hard cap).
3. **Overlay windows** — one per direction, positioned over target windows, rendered as transparent colored borders using layered windows.

These three additions interact with each other and with the existing components in specific, constrained ways.

### v2.0 System Overview

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              Program.cs (CLI entry)                           │
│  "focus <direction>" → stateless pipeline (unchanged v1.0 path)              │
│  "focus daemon"      → DaemonHost.Run() → blocks on message loop             │
└──────────────────────────────┬───────────────────────────────────────────────┘
                               │ daemon path
                               v
┌──────────────────────────────────────────────────────────────────────────────┐
│                              DaemonHost                                       │
│  Owns: message loop thread, hook lifecycle, overlay manager lifecycle        │
│  Win32: SetWindowsHookEx(WH_KEYBOARD_LL) → message loop → UnhookWindowsHookEx│
│  Ctrl+C / SIGTERM → cleanup → exit                                           │
└──────────┬───────────────────────────────┬───────────────────────────────────┘
           │ hook fires on keypress        │ CAPSLOCK state change
           v                               v
┌──────────────────────┐        ┌────────────────────────────────────────────┐
│  KeyboardHookHandler  │        │              OverlayManager                │
│                       │        │                                            │
│  KBDLLHOOKSTRUCT:     │        │  Owns 4 overlay windows (one per dir)     │
│  vkCode = VK_CAPITAL  │        │  Show: enumerate windows, score all dirs,  │
│  flags bit 7 = 0/1   │        │       position overlays, make visible       │
│  (pressed/released)   │        │  Hide: move off-screen or destroy HWNDs   │
│                       │        │  Update: re-score on foreground change     │
│  Injects:             │------->│                                            │
│  CapsLockDown event   │        │  Uses: WindowEnumerator (unchanged)        │
│  CapsLockUp event     │        │        NavigationService (unchanged)       │
└──────────────────────┘        │        FocusConfig (unchanged)             │
                                 └──────────────┬─────────────────────────────┘
                                                │ positions + paints
                                                v
                            ┌───────────────────────────────────────┐
                            │         OverlayRenderer (per window)   │
                            │                                        │
                            │  IOverlayRenderer interface           │
                            │  DefaultBorderRenderer (colored rect) │
                            │  Per-strategy custom renderers         │
                            │                                        │
                            │  Win32: CreateWindowEx(WS_EX_LAYERED  │
                            │        | WS_EX_TOOLWINDOW | WS_EX_    │
                            │        TOPMOST | WS_EX_TRANSPARENT)   │
                            │        UpdateLayeredWindow(hWnd, ...)  │
                            └───────────────────────────────────────┘
```

---

## Part 3: Component Analysis — New vs Modified vs Unchanged

### Unchanged Components (zero modification required)

| Component | Why Unchanged |
|-----------|---------------|
| `WindowEnumerator` | Stateless. Daemon calls it the same way as CLI — fresh enumeration on demand. |
| `NavigationService` | Static, pure logic. Daemon calls `GetRankedCandidates` for all 4 directions. |
| `FocusActivator` | Not used by daemon directly — AHK still sends `focus <direction>` for actual navigation. |
| `MonitorHelper` | Utility functions, no state. |
| `ExcludeFilter` | Pure filtering logic, no state. |
| `WindowInfo` | Immutable record, no change needed. |
| `Direction` | Enum, no change needed. |

### Modified Components (extension, not replacement)

**`FocusConfig` — extend to add overlay configuration**

New fields needed:
- `OverlayColors`: per-direction color strings (ARGB hex or named colors), e.g. `{ "left": "#FF4444FF", "right": "#FF44FF44", "up": "#FFFFFF44", "down": "#FFFF8844" }`
- `OverlayBorderWidth`: integer pixel width of border, default 4
- `OverlayOpacity`: 0-255 alpha for border, default 200

These fields must have defaults so existing config files without them continue to work (JSON deserialization already ignores missing fields).

**`Program.cs` — add `daemon` subcommand**

The existing top-level statements handle `focus <direction>` and `focus --debug`. A `daemon` subcommand must be added to System.CommandLine that calls `DaemonHost.Run()` instead of the stateless pipeline.

System.CommandLine supports subcommands via `Command` class — this is additive, the existing root command handler is not modified.

### New Components

**`DaemonHost`** — process lifecycle manager

Responsibilities:
- Install WH_KEYBOARD_LL hook via `SetWindowsHookEx`
- Run a Win32 message loop (`GetMessage` / `DispatchMessage`) on a dedicated STA thread
- Respond to CAPSLOCK key events from `KeyboardHookHandler`
- Manage `OverlayManager` lifecycle (create on startup, show/hide per hook events)
- Handle process shutdown: `Console.CancelKeyPress` + hook cleanup via `UnhookWindowsHookEx`

Critical constraint: **The hook-owning thread must run a message loop.** The hook callback is invoked via a message sent to the thread that installed the hook. A console app has no message loop by default; DaemonHost must create one explicitly.

```csharp
// Pseudocode — DaemonHost message loop on dedicated thread
Thread hookThread = new Thread(() =>
{
    _hookHandle = PInvoke.SetWindowsHookEx(
        WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
        _hookCallback,
        default,   // current module (LL hooks don't use hInstance)
        0);        // thread ID 0 = global hook

    MSG msg = default;
    while (PInvoke.GetMessage(out msg, default, 0, 0))
    {
        PInvoke.TranslateMessage(ref msg);
        PInvoke.DispatchMessage(ref msg);
    }

    PInvoke.UnhookWindowsHookEx(_hookHandle);
});
hookThread.SetApartmentState(ApartmentState.STA);
hookThread.IsBackground = false;
hookThread.Start();
```

**`KeyboardHookHandler`** — KBDLLHOOKSTRUCT decoder

Responsibilities:
- Decode `KBDLLHOOKSTRUCT` from the `lParam` pointer in the hook callback
- Detect CAPSLOCK pressed (`vkCode == VK_CAPITAL`, flags bit 7 == 0 → key down)
- Detect CAPSLOCK released (`vkCode == VK_CAPITAL`, flags bit 7 == 1 → key up = `LLKHF_UP`)
- Pass CAPSLOCK state change events to `OverlayManager`
- Call `CallNextHookEx` — always, to not break other hooks
- Return in under 1 second — never block; delegate work to OverlayManager immediately

CAPSLOCK detection specifics:
- `wParam` (message) = `WM_KEYDOWN` (0x0100) when pressed, `WM_KEYUP` (0x0101) when released
- `KBDLLHOOKSTRUCT.vkCode` = `VK_CAPITAL` (0x14)
- `KBDLLHOOKSTRUCT.flags` bit 7 (`LLKHF_UP`, value 0x80) is set when key is released
- WM_KEYDOWN also fires for key-repeat when held; use state tracking to avoid re-triggering show on auto-repeat

**`OverlayManager`** — overlay lifecycle coordinator

Responsibilities:
- On `CapsLockDown`: enumerate windows, call `NavigationService.GetRankedCandidates` for all 4 directions, position and show 4 overlay windows (one per direction, over the top-ranked candidate in each direction)
- On `CapsLockUp`: hide/destroy overlay windows
- On foreground window change (optional): re-score and update overlays while CAPSLOCK is held
- Exclude own overlay windows from enumeration (WindowEnumerator already excludes WS_EX_TOOLWINDOW windows by Alt+Tab filter logic — overlays will carry WS_EX_TOOLWINDOW)
- Hold references to 4 `OverlayWindow` instances (or null if no candidate in that direction)

Key design: overlays are pre-created at daemon startup (or lazily on first CAPSLOCK) and reused across CAPSLOCK events. Destroy + recreate on each CAPSLOCK is expensive; prefer hide (move off-screen or `ShowWindow(SW_HIDE)`) / show (`ShowWindow(SW_SHOWNOACTIVATE)`) instead.

**`OverlayWindow`** — single layered window

Responsibilities:
- Own one `HWND` created with `CreateWindowEx(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT, ...)`
- Expose `Position(RECT targetBounds, Direction dir, uint color, int borderWidth)` — moves and repaints
- Expose `Show()` / `Hide()` — `SetWindowPos` with `HWND_TOPMOST` or `ShowWindow`
- Delegate rendering to `IOverlayRenderer`
- Manage GDI DIB section for `UpdateLayeredWindow` — the bitmap that defines the window's visual

Window class registration: the overlay window requires a `WNDCLASS` registration. Use a distinctive class name (e.g. `"FocusOverlay"`) so `WindowEnumerator` can identify and skip these windows in addition to WS_EX_TOOLWINDOW filtering.

**`IOverlayRenderer`** — rendering abstraction

```csharp
internal interface IOverlayRenderer
{
    // Draw into provided DIB section DC, sized to match the target window bounds.
    // Returns true if content changed and UpdateLayeredWindow should be called.
    void Render(
        HDC hdc,
        int width,
        int height,
        Direction direction,
        OverlayRenderContext context);
}

internal record OverlayRenderContext(
    uint BorderColor,    // ARGB
    int BorderWidth,
    Strategy ActiveStrategy,
    double Score,
    WindowInfo Target);
```

**`DefaultBorderRenderer`** — colored rectangle border

- Renders a solid-color rectangular border (top/left/right/bottom strips, leaving center transparent/alpha=0)
- Uses GDI `FillRect` calls into the DIB section's HDC
- Per-pixel alpha: the DIB section must be a 32bpp ARGB bitmap with pre-multiplied alpha channels
- `UpdateLayeredWindow` is called with `ULW_ALPHA` flag

Per-direction colors come from `FocusConfig.OverlayColors`.

---

## Part 4: Win32 Technical Constraints

### WH_KEYBOARD_LL Constraints (HIGH confidence — official docs)

- The hook callback is called **in the context of the thread that installed the hook**, via a Windows message sent to that thread. The thread **must have a running message loop** (`GetMessage`/`DispatchMessage`).
- The callback must complete within `LowLevelHooksTimeout` registry value (default: 1 second; max Windows 10 1709+ allows is 1 second). If it times out, Windows silently removes the hook with no notification.
- `WH_KEYBOARD_LL` is **not injected** into other processes — it runs in the installing process's context. This is the only global hook type usable in .NET without a separate native DLL.
- Do not make blocking calls (file I/O, Thread.Sleep, slow Win32 calls) inside the hook callback. Use `Post`/`Queue` patterns to defer work.
- Sources: [LowLevelKeyboardProc — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [Hooks Overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks)

### Layered Window Constraints (HIGH confidence — official docs)

- Create with `CreateWindowEx` using style `WS_EX_LAYERED`. Also add `WS_EX_TOOLWINDOW` (excluded from Alt+Tab, and from existing WindowEnumerator's filter), `WS_EX_TOPMOST` (renders above target windows), `WS_EX_TRANSPARENT` (clicks pass through to windows beneath).
- After creation, call `UpdateLayeredWindow` (or `SetLayeredWindowAttributes`) before the window becomes visible. Calling neither means the window stays invisible even after `ShowWindow`.
- Use `UpdateLayeredWindow` with `ULW_ALPHA` for per-pixel transparency (colored border + fully transparent center). This gives correct alpha compositing. Do **not** mix `SetLayeredWindowAttributes` and `UpdateLayeredWindow` on the same window.
- The source bitmap must be a 32bpp DIB section with **pre-multiplied alpha** (each RGB channel multiplied by alpha/255 before passing to `UpdateLayeredWindow`). Failure to pre-multiply causes incorrect compositing with DWM.
- `UpdateLayeredWindow` replaces WM_PAINT entirely — do not handle WM_PAINT for layered windows that use this function.
- Window must be top-level (not a child HWND) for `WS_EX_LAYERED` on Windows 7 and earlier; Windows 8+ supports it for child windows too. Use top-level for broadest compatibility.
- Sources: [UpdateLayeredWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow), [Window Features — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features), [SetLayeredWindowAttributes — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes)

### CAPSLOCK Held-State Detection (HIGH confidence — official docs)

In the WH_KEYBOARD_LL callback:
- `wParam` = `WM_KEYDOWN` (0x0100): key pressed (also fires for auto-repeat while held)
- `wParam` = `WM_KEYUP` (0x0101): key released
- `((KBDLLHOOKSTRUCT*)lParam)->vkCode` = `VK_CAPITAL` (0x14) for CAPSLOCK
- `((KBDLLHOOKSTRUCT*)lParam)->flags` bit 7 (`LLKHF_UP` = 0x80): set = key up; clear = key down
- To distinguish first press from auto-repeat on `WM_KEYDOWN`: track a boolean `_capsLockHeld` field; set it on first WM_KEYDOWN, ignore subsequent WM_KEYDOWN until WM_KEYUP.
- Source: [KBDLLHOOKSTRUCT — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct)

### Overlay Window Exclusion from Navigation (HIGH confidence — existing code)

The existing `WindowEnumerator` already excludes windows with `WS_EX_TOOLWINDOW` flag (step h in the Alt+Tab filter). Overlay windows created with `WS_EX_TOOLWINDOW` will be automatically excluded from all window enumeration — no changes needed to `WindowEnumerator`.

Secondary defensive measure: check window class name == `"FocusOverlay"` in the enumerator (or add to the exclude list) in case the WS_EX_TOOLWINDOW check is bypassed by future code paths.

---

## Part 5: Data Flow — Daemon Mode

### CAPSLOCK Press → Overlay Show

```
[User presses CAPSLOCK]
    |
    v
[OS delivers WM_KEYDOWN to hook thread message queue]
    |
    v
[KeyboardHookHandler.HookCallback(nCode, wParam, lParam)]
    |  vkCode == VK_CAPITAL, flags & LLKHF_UP == 0 (key down), not already held
    |  → sets _capsLockHeld = true
    |  → PostMessage to DaemonHost or directly call OverlayManager.ShowAsync()
    |  → CallNextHookEx (always)
    v
[OverlayManager.Show()]
    |
    v
[WindowEnumerator.GetNavigableWindows()]  ← unchanged, same as CLI path
    |
    v
[ExcludeFilter.Apply(windows, config.Exclude)]  ← unchanged
    |
    v
[NavigationService.GetRankedCandidates(filtered, Direction.Left,  strategy)]
[NavigationService.GetRankedCandidates(filtered, Direction.Right, strategy)]
[NavigationService.GetRankedCandidates(filtered, Direction.Up,    strategy)]
[NavigationService.GetRankedCandidates(filtered, Direction.Down,  strategy)]
    |  4 ranked candidate lists (may be empty if no candidate in a direction)
    v
[OverlayWindow[Left].Position(leftCandidate.Bounds, config.OverlayColors.Left)]
[OverlayWindow[Right].Position(rightCandidate.Bounds, config.OverlayColors.Right)]
[OverlayWindow[Up].Position(upCandidate.Bounds, config.OverlayColors.Up)]
[OverlayWindow[Down].Position(downCandidate.Bounds, config.OverlayColors.Down)]
    |
    v
[OverlayWindow.Render() → IOverlayRenderer.Render(hdc, ...) → UpdateLayeredWindow]
    |
    v
[OverlayWindow.Show() → SetWindowPos(HWND_TOPMOST, SWP_SHOWWINDOW | SWP_NOACTIVATE)]
    |
    v
[Overlay borders visible on screen]
```

### CAPSLOCK Release → Overlay Hide

```
[User releases CAPSLOCK]
    |
    v
[KeyboardHookHandler.HookCallback: WM_KEYUP, vkCode == VK_CAPITAL]
    |  → sets _capsLockHeld = false
    |  → signals OverlayManager.Hide()
    |  → CallNextHookEx
    v
[OverlayManager.Hide() → foreach OverlayWindow: SetWindowPos(SWP_HIDEWINDOW)]
    |
    v
[Overlays hidden; windows beneath visible normally]
```

### Config Resolution (daemon startup)

```
[FocusConfig.Load()]  ← unchanged config loading
    |  reads existing strategy, wrap, exclude
    |  reads NEW: overlayColors, borderWidth, opacity (with defaults if absent)
    v
[DaemonHost holds reference to loaded config]
    |  config re-loaded on SIGHUP or "focus daemon --reload" (future, not v2.0)
    v
[OverlayManager and renderers use config values directly]
```

---

## Part 6: Project Structure — New Files

The existing `focus/Windows/` directory structure is preserved. New daemon/overlay files are added alongside:

```
focus/
├── Program.cs                     # MODIFIED: adds "daemon" subcommand
├── Windows/
│   ├── Direction.cs               # unchanged
│   ├── ExcludeFilter.cs           # unchanged
│   ├── FocusActivator.cs          # unchanged
│   ├── FocusConfig.cs             # MODIFIED: add overlay color/width/opacity fields
│   ├── MonitorHelper.cs           # unchanged
│   ├── NavigationService.cs       # unchanged
│   ├── WindowEnumerator.cs        # unchanged (WS_EX_TOOLWINDOW filter already excludes overlays)
│   ├── WindowInfo.cs              # unchanged
│   └── [existing files...]
├── Daemon/
│   ├── DaemonHost.cs              # NEW: message loop, hook lifecycle, Ctrl+C cleanup
│   └── KeyboardHookHandler.cs     # NEW: KBDLLHOOKSTRUCT decode, CAPSLOCK state tracking
├── Overlay/
│   ├── OverlayManager.cs          # NEW: coordinates 4 overlay windows, calls NavigationService
│   ├── OverlayWindow.cs           # NEW: owns one HWND; CreateWindowEx, UpdateLayeredWindow
│   ├── IOverlayRenderer.cs        # NEW: rendering interface
│   └── DefaultBorderRenderer.cs   # NEW: colored border with pre-multiplied alpha GDI bitmap
```

**Rationale for `Daemon/` and `Overlay/` as separate folders from `Windows/`:**
- `Windows/` contains the existing window management components that work in both CLI and daemon modes.
- `Daemon/` is daemon-lifecycle specific — not needed by the stateless CLI path.
- `Overlay/` is overlay-rendering specific — contains GDI/layered-window code not needed by CLI.
- This separation keeps the existing `Windows/` folder completely untouched for all non-daemon files.

---

## Part 7: Architectural Patterns for v2.0

### Pattern 1: Dedicated Hook Thread with Message Loop

**What:** Install WH_KEYBOARD_LL and run `GetMessage`/`DispatchMessage` on a single dedicated thread. This thread does nothing else — all work triggered by hook events is dispatched to other components.

**When to use:** Required. WH_KEYBOARD_LL callbacks are delivered as messages to the installing thread's queue. Without a message loop on that thread, callbacks never fire.

**Trade-offs:** The hook thread must stay responsive. All work (enumerate windows, score, create DIBs) happens on a different thread or is fast enough to complete within the 1-second timeout.

```csharp
// DaemonHost.cs
private void RunMessageLoop()
{
    // Install hook on THIS thread
    _hookHandle = PInvoke.SetWindowsHookEx(
        WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
        _hookCallback,
        default,
        0);

    MSG msg = default;
    while (PInvoke.GetMessage(out msg, default, 0, 0))
    {
        PInvoke.TranslateMessage(ref msg);
        PInvoke.DispatchMessage(ref msg);
    }

    PInvoke.UnhookWindowsHookEx(_hookHandle);
}
```

### Pattern 2: Pre-multiplied Alpha DIB for Layered Window

**What:** Create a 32bpp DIB section, draw into it via GDI, pre-multiply RGB by alpha, then pass to `UpdateLayeredWindow`.

**When to use:** Required for correct per-pixel transparency. Using `SetLayeredWindowAttributes` with a color key is simpler but produces aliased edges and cannot do partial transparency.

**Trade-offs:** More complex GDI setup. Once understood, re-usable across render calls by just updating the bitmap pixels.

```csharp
// OverlayWindow.cs — bitmap setup
private HDC CreateAlphaDC(int width, int height, out IntPtr bits)
{
    var bmi = new BITMAPINFO();
    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
    bmi.bmiHeader.biWidth = width;
    bmi.bmiHeader.biHeight = -height; // top-down
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;
    // bits = pointer to BGRA pixel data
    _hBitmap = PInvoke.CreateDIBSection(_screenDC, ref bmi, DIB_USAGE.DIB_RGB_COLORS,
        out bits, default, 0);
    _memDC = PInvoke.CreateCompatibleDC(_screenDC);
    PInvoke.SelectObject(_memDC, _hBitmap);
    return _memDC;
}

// Pre-multiply RGB channels by alpha before calling UpdateLayeredWindow
private unsafe void PreMultiplyAlpha(byte* pixels, int count)
{
    for (int i = 0; i < count; i += 4)
    {
        byte a = pixels[i + 3];
        pixels[i]     = (byte)(pixels[i]     * a / 255); // B
        pixels[i + 1] = (byte)(pixels[i + 1] * a / 255); // G
        pixels[i + 2] = (byte)(pixels[i + 2] * a / 255); // R
    }
}
```

### Pattern 3: Overlay Windows Are Pre-Created, Not On-Demand

**What:** Create the 4 overlay HWNDs at daemon startup (or first CAPSLOCK press). On subsequent CAPSLOCK presses, reposition and repaint the existing windows rather than creating new ones.

**When to use:** For responsive overlay show times. `CreateWindowEx` + DIB setup + `RegisterClass` takes tens of milliseconds. CAPSLOCK → overlay-visible latency should be imperceptible (<50ms target).

**Trade-offs:** Holds 4 HWNDs and 4 GDI bitmaps in memory always. Resource cost is negligible (4 small windows). The alternative — create/destroy per CAPSLOCK press — produces perceptible lag and GDI resource churn.

### Pattern 4: IOverlayRenderer Interface for Extensibility

**What:** Define `IOverlayRenderer` with a single `Render(HDC hdc, int w, int h, Direction dir, OverlayRenderContext ctx)` method. `DefaultBorderRenderer` implements colored border. Future per-strategy renderers can show score numbers, highlight edge matching, etc.

**When to use:** When the rendering logic is expected to vary per strategy. This matches the existing `Strategy` enum pattern in `NavigationService`.

**Trade-offs:** One extra indirection. Worth it given the explicit design goal of "pluggable overlay renderer" in the project requirements.

```csharp
internal interface IOverlayRenderer
{
    void Render(HDC hdc, int width, int height, Direction direction, OverlayRenderContext ctx);
}

// Registered per strategy (or default used for all):
internal static class RendererRegistry
{
    private static readonly Dictionary<Strategy, IOverlayRenderer> _renderers = new();
    private static readonly IOverlayRenderer _default = new DefaultBorderRenderer();

    public static void Register(Strategy strategy, IOverlayRenderer renderer)
        => _renderers[strategy] = renderer;

    public static IOverlayRenderer Get(Strategy strategy)
        => _renderers.TryGetValue(strategy, out var r) ? r : _default;
}
```

---

## Part 8: Integration Points

### What Existing Components the Daemon Calls

| Existing Component | How Daemon Uses It | Integration Point |
|--------------------|--------------------|-------------------|
| `FocusConfig.Load()` | Called once at daemon startup to read config (including new overlay fields) | `DaemonHost` loads config, passes to `OverlayManager` |
| `WindowEnumerator.GetNavigableWindows()` | Called on every CAPSLOCK press to get fresh window list | `OverlayManager.Show()` instantiates and calls |
| `ExcludeFilter.Apply()` | Applied to enumerated windows same as CLI path | `OverlayManager.Show()` calls after enumeration |
| `NavigationService.GetRankedCandidates()` | Called 4 times (once per direction) on every CAPSLOCK press | `OverlayManager.Show()` calls, takes first result per direction |
| `Direction` enum | Used for all overlay positioning and rendering | Unchanged |
| `WindowInfo` | The target data for positioning overlays | Unchanged record structure |

### What the Daemon Does NOT Use from Existing Components

| Existing Component | Why Not Used by Daemon |
|--------------------|-----------------------|
| `FocusActivator` | Daemon does not activate windows — AHK still handles `focus <direction>` for that |
| `System.CommandLine` root command handler | Daemon is a new subcommand; the existing handler is the CLI path |

### New Win32 APIs Required

These must be added to `NativeMethods.txt` (CsWin32 source generator):

```
SetWindowsHookEx
UnhookWindowsHookEx
CallNextHookEx
GetMessage
TranslateMessage
DispatchMessage
PostQuitMessage
CreateWindowEx      (or CreateWindowExW)
RegisterClassEx     (or RegisterClassExW)
DefWindowProc
ShowWindow
SetWindowPos
CreateCompatibleDC
CreateDIBSection
SelectObject
DeleteObject
DeleteDC
UpdateLayeredWindow
GetDC
ReleaseDC
```

Existing entries in `NativeMethods.txt` that are already present and still needed: `SetForegroundWindow`, `GetForegroundWindow`, `SendInput`, `EnumWindows`, `IsWindowVisible`, `IsIconic`, `GetWindowLong`, `GetAncestor`, `GetLastActivePopup`, `DwmGetWindowAttribute`, `GetWindowThreadProcessId`, `OpenProcess`, `QueryFullProcessImageName`, `CloseHandle`, `GetWindowTextW`, `EnumDisplayMonitors`, `MonitorFromWindow`, `GetMonitorInfo`, `GetClassNameW`, `EnumChildWindows`, `MonitorFromPoint`, `MessageBeep`.

---

## Part 9: Build Order

Build order is dictated by the dependency graph. Items later in the list depend on items earlier.

1. **`FocusConfig` modifications** — Add overlay color/width/opacity fields with defaults. No other v2.0 code can be tested until config loading provides these values. Also: regression-test that existing config files without these fields still load correctly.

2. **`Program.cs` `daemon` subcommand stub** — Add the `daemon` command to System.CommandLine with a placeholder that prints "daemon mode not yet implemented". This ensures the CLI parsing change is isolated and doesn't break existing commands.

3. **`DaemonHost` + `KeyboardHookHandler`** — Build the message loop and hook infrastructure. Test independently: run daemon, verify hook fires and CAPSLOCK presses/releases are logged. No overlay code yet. Validate that hook fires, hook is cleaned up on exit, and timeout never occurs (hook callback returns immediately).

4. **`OverlayWindow`** — Build the HWND creation + GDI DIB + `UpdateLayeredWindow` pipeline. Test with a hardcoded color and size. Verify: window appears correctly positioned, is excluded from Alt+Tab and window enumeration, clicks pass through, window is topmost.

5. **`IOverlayRenderer` + `DefaultBorderRenderer`** — Build the renderer interface and default colored-border implementation. Wire into `OverlayWindow`. Test: correct border color, correct border width, center is transparent.

6. **`OverlayManager`** — Wire together: call `WindowEnumerator`, `ExcludeFilter`, `NavigationService` 4x, position `OverlayWindow` instances, show/hide lifecycle. Test: CAPSLOCK press shows overlays on correct target windows, CAPSLOCK release hides them.

7. **`RendererRegistry` / per-strategy renderers** — Optional extensions. Default renderer used for all strategies initially; per-strategy renderers registered as needed.

8. **Full integration** — Connect `DaemonHost` → `KeyboardHookHandler` → `OverlayManager` end-to-end. Test: multiple CAPSLOCK cycles, foreground window changes while held, no candidate in a direction (overlay absent for that direction).

---

## Part 10: Anti-Patterns

### Anti-Pattern 1: Blocking Inside the Hook Callback

**What people do:** Call `WindowEnumerator.GetNavigableWindows()` directly inside the WH_KEYBOARD_LL callback.

**Why it's wrong:** Window enumeration involves multiple Win32 calls (EnumWindows iterates all HWNDs, DwmGetWindowAttribute called per window). This can take 5-30ms on a busy desktop. The hook timeout is 1 second but doing I/O in the callback is fragile. Worse: on Windows 10 1709+, if hook processing exceeds 1 second Windows silently removes the hook.

**Do this instead:** In the hook callback, only update a state variable and post a message (or invoke an async method) to have the work done on a different thread or on the next message loop cycle. The callback itself returns in microseconds.

### Anti-Pattern 2: Mixing SetLayeredWindowAttributes and UpdateLayeredWindow

**What people do:** Call `SetLayeredWindowAttributes` to set initial transparency, then switch to `UpdateLayeredWindow` for updates.

**Why it's wrong:** Microsoft explicitly states these two APIs must not be mixed on the same window. The behavior is undefined and varies by Windows version.

**Do this instead:** Choose one approach at window creation time. For per-pixel alpha borders with transparent centers, use `UpdateLayeredWindow` exclusively throughout the overlay window's lifetime.

### Anti-Pattern 3: Forgetting Alpha Pre-multiplication

**What people do:** Fill the DIB with ARGB values like 0x80FF0000 (50% transparent red) and pass directly to `UpdateLayeredWindow`.

**Why it's wrong:** `UpdateLayeredWindow` with `ULW_ALPHA` requires **pre-multiplied alpha** — each RGB channel must be multiplied by alpha/255 before passing the bitmap. Failing to do this causes incorrect compositing: colors appear washed out or wrong against the desktop.

**Do this instead:** After filling the DIB, iterate all pixels and pre-multiply: `R' = R * A / 255`, `G' = G * A / 255`, `B' = B * A / 255`. Or write pre-multiplied values directly.

### Anti-Pattern 4: Creating Overlay Windows on a Thread Without a Message Pump

**What people do:** Create the 4 overlay HWNDs on a background Task or ThreadPool thread, then show them from another thread.

**Why it's wrong:** Win32 windows belong to the thread that created them. Messages for those windows (WM_PAINT, WM_NCHITTEST, WM_MOVE, etc.) are delivered to the creating thread's message queue. If that thread has no message loop, the windows become unresponsive and can hang the entire session.

**Do this instead:** Create overlay windows on the same thread that runs the message loop (the hook thread). `DaemonHost`'s message loop thread is the correct place for all HWND creation.

### Anti-Pattern 5: Making Overlay Windows Appear in Navigation Candidates

**What people do:** Create overlay windows without `WS_EX_TOOLWINDOW`, causing them to appear in the Alt+Tab list and in `WindowEnumerator`'s results.

**Why it's wrong:** The daemon would try to draw overlays over its own overlays, creating recursive positioning. The overlays would also appear as navigation candidates.

**Do this instead:** Always create overlay windows with `WS_EX_TOOLWINDOW`. The existing `WindowEnumerator` Alt+Tab filter already excludes toolwindows. Register them with a distinctive class name as a secondary exclusion safety.

### Anti-Pattern 6: Re-rendering the Full DIB on Every CAPSLOCK Cycle Without Reuse

**What people do:** Allocate a new `HBITMAP` and `HDC` on every CAPSLOCK press.

**Why it's wrong:** GDI object allocation is a system resource. 4 windows × multiple CAPSLOCK presses = GDI handle leaks if old resources are not freed. Also, allocation time adds to CAPSLOCK-to-visible latency.

**Do this instead:** Allocate the DIB section once per `OverlayWindow` at creation. On each CAPSLOCK press, resize the DIB if the target window size changed (resize requires deallocation + reallocation), or reuse the existing bitmap and overwrite its pixels.

---

## Part 11: v3.1 Architecture — Grid-Snapped Window Move and Resize

This section documents the integration design for adding CAPS+TAB (move), CAPS+LSHIFT (grow), and CAPS+LCTRL (shrink) controls layered onto the existing v3.0 daemon architecture. Read this section in context of Parts 1-10 above which describe the already-implemented system.

### What Already Exists (v3.0 Actual Implementation)

The live codebase has evolved from the v2.0 design. The actual component structure as shipped:

```
focus/
├── Program.cs
└── Windows/
    ├── Direction.cs                           # Direction enum + DirectionParser
    ├── ExcludeFilter.cs
    ├── FocusActivator.cs
    ├── FocusConfig.cs                         # JSON config, runtime reload per keypress
    ├── MonitorHelper.cs                       # EnumDisplayMonitors, GetMonitorIndex
    ├── NavigationService.cs
    ├── WindowEnumerator.cs
    ├── WindowInfo.cs
    ├── WindowSorter.cs
    └── Daemon/
        ├── CapsLockMonitor.cs                 # Channel consumer; CAPS + direction + number state machine
        ├── DaemonCommand.cs                   # DaemonCommand.Run() — full daemon lifecycle
        ├── DaemonMutex.cs
        ├── KeyboardHookHandler.cs             # WH_KEYBOARD_LL hook; TryWrite to Channel<KeyEvent>
        ├── KeyEvent.cs                        # readonly record struct: VkCode, IsKeyDown, Timestamp, ShiftHeld, CtrlHeld, AltHeld
        ├── TrayIcon.cs
        └── Overlay/
            ├── BorderRenderer.cs              # IOverlayRenderer implementation (GDI pixel buffer)
            ├── ForegroundMonitor.cs           # SetWinEventHook for foreground window changes
            ├── IOverlayRenderer.cs
            ├── NumberLabelRenderer.cs
            ├── OverlayColors.cs
            ├── OverlayManager.cs              # Manages 4 directional + 1 foreground + 9 number OverlayWindows
            ├── OverlayOrchestrator.cs         # CAPS state + navigation dispatch; STA thread coordinator
            └── OverlayWindow.cs               # Single layered HWND lifecycle
```

**Critical v3.0 data flow details for v3.1 integration:**

The `KeyEvent` record already carries `ShiftHeld`, `CtrlHeld`, `AltHeld` modifier flags for direction key events. The `KeyboardHookHandler` already reads and transmits these modifier states through the channel. However, `CapsLockMonitor` currently discards modifier information — it maps direction keys to bare direction names and routes all to `_onDirectionKeyDown(directionName)` regardless of modifiers. This is the primary integration point.

`OverlayOrchestrator` currently exposes:
- `OnCapsLockHeld()` — triggers overlay show
- `OnCapsLockReleased()` — hides all overlays
- `OnDirectionKeyDown(string direction)` — performs focus navigation
- `OnNumberKeyDown(int number)` — activates window by number

The STA threading model is established: all Win32 calls happen on the STA thread via `Control.Invoke`. The `_staDispatcher` Control is already the cross-thread marshal point.

### v3.1 System Overview

The move/resize features add a **mode layer** between the existing keyboard event pipeline and the action dispatch layer. Three input modes are active simultaneously while CAPS is held: focus navigation (bare direction), move (TAB+direction), grow (LSHIFT+direction), shrink (LCTRL+direction).

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  KeyboardHookHandler (unchanged)                                             │
│  WH_KEYBOARD_LL → TryWrite(KeyEvent{VkCode, IsKeyDown, Shift, Ctrl, Alt})   │
│  Already intercepts TAB (VK_TAB=0x09) if CAPS held — needs wiring only      │
└─────────────────────────────┬───────────────────────────────────────────────┘
                               │ Channel<KeyEvent>
                               v
┌─────────────────────────────────────────────────────────────────────────────┐
│  CapsLockMonitor.HandleDirectionKeyEvent() — MODIFIED                        │
│                                                                              │
│  if ShiftHeld  → _onModifiedDirectionKeyDown?.Invoke("shift",   direction)  │
│  if CtrlHeld   → _onModifiedDirectionKeyDown?.Invoke("ctrl",    direction)  │
│  if TabHeld    → _onModifiedDirectionKeyDown?.Invoke("tab",     direction)  │
│  else          → _onDirectionKeyDown?.Invoke(direction)  [existing path]    │
└─────────────────────────────┬───────────────────────────────────────────────┘
                               │ callback to OverlayOrchestrator
                               v
┌─────────────────────────────────────────────────────────────────────────────┐
│  OverlayOrchestrator — MODIFIED (new method + mode-aware overlay refresh)   │
│                                                                              │
│  OnModifiedDirectionKeyDown(modifier, direction)                            │
│    → Invoke on STA thread                                                   │
│    → tab+dir    → WindowManagerService.MoveWindow(fgHwnd, dir, config)     │
│    → shift+dir  → WindowManagerService.GrowWindow(fgHwnd, dir, config)     │
│    → ctrl+dir   → WindowManagerService.ShrinkWindow(fgHwnd, dir, config)   │
│    → After action: refresh overlays for new foreground window bounds        │
│                                                                              │
│  ShowOverlaysForCurrentForeground() — MODIFIED                              │
│    → Detects active modifier from _activeMode field                         │
│    → Passes mode hint to OverlayManager for renderer selection              │
└──────────────┬──────────────────────────────────────────────────────────────┘
               │                                │
               v                                v
┌──────────────────────────┐    ┌───────────────────────────────────────────┐
│  WindowManagerService    │    │  OverlayManager — MODIFIED (mode hints)   │
│  NEW                     │    │                                            │
│  MoveWindow(hwnd,dir,cfg)│    │  ShowOverlay(dir, bounds, mode)           │
│  GrowWindow(hwnd,dir,cfg)│    │    → selects renderer by mode            │
│  ShrinkWindow(hwnd,dir,  │    │    → BorderRenderer (navigate mode)      │
│    cfg)                  │    │    → MoveArrowRenderer (move mode)       │
│                          │    │    → GrowShrinkRenderer (grow/shrink)    │
│  GridCalculator          │    │                                            │
│  NEW (static helper)     │    │  or: passes mode to existing renderer     │
│  GetGridStep(hmon, cfg)  │    │  via updated IOverlayRenderer.Paint()    │
│  SnapToGrid(coord, step, │    └───────────────────────────────────────────┘
│    tolerance)            │
│  GetMonitorWorkArea(hmon)│
│                          │
│  Win32: MoveWindow or    │
│  SetWindowPos            │
└──────────────────────────┘
```

### New and Modified Components for v3.1

#### Component: KeyboardHookHandler — minor extension

**Status:** Minor modification

The hook must intercept TAB (VK_TAB = 0x09) when CAPS is held, in addition to the existing direction and number key interception. LSHIFT (VK_LSHIFT = 0xA0) and LCTRL (VK_LCONTROL = 0xA2) are already tracked via `GetKeyState` and transmitted in `KeyEvent.ShiftHeld` and `KeyEvent.CtrlHeld`. TAB requires explicit interception and suppression (same pattern as direction keys).

Changes needed:
- Add `VK_TAB = 0x09` to the intercepted key set when CAPS is held
- Include TAB hold state in `KeyEvent` as `TabHeld` flag, OR track TAB as a modifier state similar to how shift/ctrl are tracked
- Alternative cleaner approach: track TAB hold state within `KeyboardHookHandler._capsLockHeld`-style state tracking, and report it as a modifier in direction key events (no new KeyEvent field needed — TAB is not a direction key itself, it signals which mode to use)

The cleanest approach given the existing modifier pattern: add a `_tabHeld` field to `KeyboardHookHandler` (parallel to `_capsLockHeld`), set it on TAB keydown while CAPS held, clear on TAB keyup. Populate `KeyEvent.TabHeld` when writing direction key events. This requires adding `TabHeld = false` to the `KeyEvent` record.

#### Component: KeyEvent — extend

**Status:** Extend with one field

```csharp
internal readonly record struct KeyEvent(
    uint VkCode,
    bool IsKeyDown,
    uint Timestamp,
    bool ShiftHeld = false,
    bool CtrlHeld = false,
    bool AltHeld = false,
    bool TabHeld = false);   // NEW: set when TAB is held while CAPS held
```

#### Component: CapsLockMonitor — routing logic change

**Status:** Modified — add modifier-aware dispatch

`HandleDirectionKeyEvent` currently ignores `evt.ShiftHeld`, `evt.CtrlHeld`, `evt.TabHeld` and always calls `_onDirectionKeyDown(directionName)`. For v3.1, it must route to a new callback based on which modifier is held.

Three design options for the callback API:

**Option A — Single callback with modifier parameter (recommended):**
```csharp
// New callback signature
private readonly Action<string, string>? _onModifiedDirectionKeyDown;
// modifier = "tab" | "shift" | "ctrl" | "" (bare = navigate)

// In HandleDirectionKeyEvent:
string modifier = evt.TabHeld ? "tab" : evt.ShiftHeld ? "shift" : evt.CtrlHeld ? "ctrl" : "";
if (modifier == "")
    _onDirectionKeyDown?.Invoke(directionName);   // existing path unchanged
else
    _onModifiedDirectionKeyDown?.Invoke(modifier, directionName);  // new path
```

**Option B — Separate callbacks per modifier:**
```csharp
private readonly Action<string>? _onMoveKeyDown;     // TAB+dir
private readonly Action<string>? _onGrowKeyDown;     // SHIFT+dir
private readonly Action<string>? _onShrinkKeyDown;   // CTRL+dir
```

Option A is recommended: fewer constructor parameters, easily extensible if a 4th modifier is ever needed, and the modifier string is self-documenting in verbose logs.

Option A requires one new constructor parameter in `CapsLockMonitor` and one new lambda in `DaemonCommand.Run()`.

#### Component: OverlayOrchestrator — new action dispatch method

**Status:** Modified — add `OnModifiedDirectionKeyDown` + mode tracking

New public method (mirroring the existing `OnDirectionKeyDown` pattern):

```csharp
public void OnModifiedDirectionKeyDown(string modifier, string direction)
{
    if (_shutdownRequested) return;
    try { _staDispatcher.Invoke(() => ExecuteWindowActionSta(modifier, direction)); }
    catch (ObjectDisposedException) { }
    catch (InvalidOperationException) { }
}

private void ExecuteWindowActionSta(string modifier, string direction)
{
    var dir = DirectionParser.Parse(direction);
    if (dir is null) return;

    var config = FocusConfig.Load();
    var fgHwnd = PInvoke.GetForegroundWindow();
    if (fgHwnd == default) return;

    switch (modifier)
    {
        case "tab":
            WindowManagerService.MoveWindow(fgHwnd, dir.Value, config);
            break;
        case "shift":
            WindowManagerService.GrowWindow(fgHwnd, dir.Value, config);
            break;
        case "ctrl":
            WindowManagerService.ShrinkWindow(fgHwnd, dir.Value, config);
            break;
    }

    // After moving/resizing, refresh overlays from new window position
    ShowOverlaysForCurrentForeground();
}
```

Mode tracking for overlay rendering: `OverlayOrchestrator` must track which modifier was last used while CAPS is held, so `ShowOverlaysForCurrentForeground()` can pass the active mode to `OverlayManager` for renderer selection. A simple `_activeMode` field (enum or string) suffices. It is set when a modified direction key fires and cleared when CAPS is released.

#### Component: WindowManagerService — new

**Status:** New class

This is the primary new component for v3.1. It performs the actual window manipulation.

```
Windows/Daemon/WindowManagerService.cs   (new file)
```

**Responsibilities:**
- Receive an HWND, a direction, and config
- Determine the foreground window's current bounds via `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`
- Identify which monitor the window is primarily on via `MonitorFromWindow`
- Calculate the grid step for that monitor (monitor work area / config.GridDivisions)
- Snap the current position to the nearest grid point (within tolerance)
- Compute the new position based on direction and operation (move, grow, shrink)
- Apply cross-monitor logic if the window would move off the current monitor's edge
- Call `SetWindowPos` with the computed bounds

**Static design:** Like `NavigationService` and `FocusActivator`, this should be a `static class` with static methods. No instance state is needed between calls — all inputs come from parameters.

```csharp
internal static class WindowManagerService
{
    // Move foreground window one grid step in direction
    public static void MoveWindow(HWND hwnd, Direction dir, FocusConfig config) { ... }

    // Grow window edge outward by one grid step in direction
    public static void GrowWindow(HWND hwnd, Direction dir, FocusConfig config) { ... }

    // Shrink window edge inward by one grid step in direction
    public static void ShrinkWindow(HWND hwnd, Direction dir, FocusConfig config) { ... }
}
```

#### Component: GridCalculator — new (static helper or nested in WindowManagerService)

**Status:** New static methods (can live inside WindowManagerService or as a sibling class)

**Responsibilities:**
- `GetMonitorWorkArea(HWND hwnd)` — returns the usable work area rect (excludes taskbar) via `GetMonitorInfo.rcWork`
- `GetGridStep(RECT workArea, int divisions)` — computes (width/divisions, height/divisions) as the grid cell size
- `SnapToGrid(int coordinate, int step, int tolerance)` — snaps to nearest grid line if within tolerance, else returns coordinate unchanged

The snap tolerance (default ~10% of grid step) ensures that windows already near a grid line snap cleanly rather than jumping a full step. Without this, a window 5px off-grid would need two keystrokes to reach the next intended position.

```csharp
// GridCalculator logic (can be static methods on WindowManagerService)
static int SnapToGrid(int coord, int step, double toleranceFraction = 0.1)
{
    int tolerance = (int)(step * toleranceFraction);
    int remainder = coord % step;
    if (remainder <= tolerance) return coord - remainder;           // snap down
    if (remainder >= step - tolerance) return coord + (step - remainder); // snap up
    return coord;  // no snap
}
```

#### Component: FocusConfig — extend with grid settings

**Status:** Modified — add grid configuration fields

New fields:
```csharp
public int GridDivisions { get; set; } = 16;   // 1/16th of screen per step
public bool GridSnap { get; set; } = true;      // whether to snap to grid on action
public double GridSnapTolerance { get; set; } = 0.1;  // fraction of step = snap zone
```

These fields follow the existing pattern (defaults provided, missing fields in config files get defaults via JSON deserialization).

#### Component: IOverlayRenderer / OverlayManager — mode-aware rendering

**Status:** Decision point — two valid approaches

**Approach A (extend IOverlayRenderer.Paint with mode parameter):**

Add an `OverlayMode` enum parameter to `IOverlayRenderer.Paint()`. `BorderRenderer` ignores it (renders the same border for focus navigation). A new `ModeIconRenderer` uses it to draw directional arrows.

Downside: changes the interface signature, requiring `BorderRenderer` to update its method signature.

**Approach B (new overlay windows for mode icons, separate from directional overlays):**

Keep the existing 4 directional overlay windows unchanged. Add a separate set of overlay windows (or a single HUD window) that appears in move/grow/shrink modes and shows the appropriate icon. `OverlayManager` shows/hides them based on the active mode.

Downside: more overlay windows to manage.

**Recommendation: Approach A**, specifically by making mode an optional parameter with a default (so `BorderRenderer.Paint` implementation does not change):

```csharp
// Updated interface — backwards compatible via default parameter
internal interface IOverlayRenderer
{
    string Name { get; }
    void Paint(HWND hwnd, RECT bounds, uint argbColor, Direction direction,
               OverlayMode mode = OverlayMode.Navigate);  // NEW optional param
}

internal enum OverlayMode { Navigate, Move, Grow, Shrink }
```

`BorderRenderer.Paint` already matches this signature (default param = no change needed in calling code). A new `ModeIconRenderer` implements the interface and draws arrows or edge indicators based on mode and direction.

Alternatively, the simplest first implementation uses the existing `BorderRenderer` for all modes (just the border, no arrows) and defers mode-specific icons to a later phase. The mode infrastructure (enum, parameter) should be added now so the overlay refresh path passes mode correctly, even if the initial renderer ignores it.

### Data Flow — v3.1 Window Move

```
[User: CAPS held + TAB + Right arrow]
    |
    v
[KeyboardHookHandler]
    | VK_TAB keydown while CAPS held → sets _tabHeld = true
    | VK_RIGHT keydown while CAPS held
    | → TryWrite(KeyEvent{ VkCode=VK_RIGHT, IsKeyDown=true, TabHeld=true })
    | → suppresses both keys (returns LRESULT 1)
    v
[Channel<KeyEvent>]
    |
    v
[CapsLockMonitor.HandleDirectionKeyEvent]
    | evt.TabHeld == true, directionName = "right"
    | → _onModifiedDirectionKeyDown?.Invoke("tab", "right")
    v
[OverlayOrchestrator.OnModifiedDirectionKeyDown("tab", "right")]
    | → _staDispatcher.Invoke(ExecuteWindowActionSta)
    v
[ExecuteWindowActionSta on STA thread]
    | modifier = "tab", dir = Direction.Right
    | fgHwnd = GetForegroundWindow()
    | → WindowManagerService.MoveWindow(fgHwnd, Right, config)
    v
[WindowManagerService.MoveWindow]
    | 1. DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) → currentBounds
    | 2. MonitorFromWindow(fgHwnd) → hMonitor
    | 3. GetMonitorInfo(hMonitor).rcWork → workArea
    | 4. step = workArea.Width / config.GridDivisions
    | 5. snappedLeft = SnapToGrid(currentBounds.left, step, tolerance)
    | 6. newLeft = snappedLeft + step
    | 7. Cross-monitor check: if newLeft > workArea.right → find monitor to the right
    | 8. SetWindowPos(fgHwnd, NULL, newLeft, newTop, width, height, SWP_NOZORDER|SWP_NOACTIVATE)
    v
[Back in ExecuteWindowActionSta]
    | → ShowOverlaysForCurrentForeground()
    |   (overlays reposition to reflect new window bounds)
    v
[Window is now one grid step to the right; overlays updated]
```

### Data Flow — v3.1 Grow Window Edge

```
[User: CAPS held + LSHIFT + Down arrow]
    |
    v
[KeyboardHookHandler]
    | LSHIFT already tracked via GetKeyState as ShiftHeld
    | VK_DOWN keydown: TryWrite(KeyEvent{ VkCode=VK_DOWN, IsKeyDown=true, ShiftHeld=true })
    v
[CapsLockMonitor → _onModifiedDirectionKeyDown("shift", "down")]
    v
[OverlayOrchestrator → WindowManagerService.GrowWindow(fgHwnd, Down, config)]
    v
[WindowManagerService.GrowWindow]
    | currentBounds via DwmGetWindowAttribute
    | step = GetGridStep(workArea, config.GridDivisions).Height
    | snappedBottom = SnapToGrid(currentBounds.bottom, step, tolerance)
    | newBottom = snappedBottom + step       [grow bottom edge downward]
    | Clamp to monitor work area bottom
    | SetWindowPos(fgHwnd, left, top, width, newBottom - top, ...)
    v
[Window bottom edge moved one grid step down]
```

### Mode Switching Design

Mode switching is implicit and stateless from the user's perspective: whichever modifier key is held when a direction key is pressed determines the action. There is no "enter move mode" command. The daemon's state machine only needs to track:

1. Whether CAPS is held (existing `_capsLockHeld` / `_isHeld`)
2. Which modifier was last used (for overlay rendering hint — `_activeMode` in `OverlayOrchestrator`)

The `_activeMode` field in `OverlayOrchestrator` is updated on each modified direction key event and used by `ShowOverlaysForCurrentForeground()` to pass the correct `OverlayMode` to the renderer. On CAPS release, it is reset to `OverlayMode.Navigate`.

This design means the overlay display transitions instantly between modes as the user holds/releases TAB, SHIFT, or CTRL while CAPS is held — no timer, no confirmation state.

Modifier priority (if multiple modifiers somehow held simultaneously): TAB > SHIFT > CTRL. In practice, simultaneous modifier conflict is unlikely given the physical keyboard layout.

### Cross-Monitor Movement

When moving a window rightward and it would cross the right edge of its current monitor's work area, the service must:
1. Find the monitor to the right (enumerate monitors, find the one whose left edge == current monitor's right edge, or the nearest)
2. Compute a new position in the target monitor's coordinate space
3. Apply the move in virtual screen coordinates (Win32 uses a single virtual screen coordinate system spanning all monitors)

`MonitorHelper.EnumerateMonitors()` already exists and returns all monitor HMONITORs. A new `MonitorHelper.FindAdjacentMonitor(HMONITOR current, Direction dir)` method can search the list for the monitor adjacent in the given direction.

If no adjacent monitor exists, the window snaps to the edge of its current monitor's work area (does not wrap). This matches the no-op wrap behavior for navigation.

### Win32 APIs for Window Management

`SetWindowPos` and `MoveWindow` are the primary APIs. `SetWindowPos` is preferred because it provides `SWP_NOZORDER` (preserve stacking order) and `SWP_NOACTIVATE` (do not steal focus from the foreground window being moved — critical: the window IS the foreground window, but we want to avoid any focus flicker).

For resize (grow/shrink), `SetWindowPos` with new width/height is the correct API. Do not use `MoveWindow` for resize-only operations because `MoveWindow` always repaints; `SetWindowPos` with `SWP_NOMOVE` skips the move calculation.

`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` is already used throughout the codebase to get accurate visible bounds (excludes DWM drop shadows). This is the correct source for current window position in the grid calculator.

For reading back the new position after `SetWindowPos` (needed to refresh overlays), call `DwmGetWindowAttribute` again or use the computed bounds directly. The computed bounds approach is simpler and avoids an extra Win32 round-trip.

All `SetWindowPos` calls must be on the STA thread (consistent with all other Win32 calls in the codebase).

### Suggested Build Order for v3.1

Dependencies flow as follows: config must precede everything; the hook extension must precede the monitor routing change; the grid calculator must precede `WindowManagerService`; `WindowManagerService` must precede the orchestrator wiring; overlay mode must precede the renderer updates.

**Step 1 — Extend `FocusConfig`**
Add `GridDivisions`, `GridSnap`, `GridSnapTolerance` fields. Verify existing config files deserialize without error. Confirm defaults produce 1/16th screen steps with 10% snap tolerance.

**Step 2 — Extend `KeyEvent` with `TabHeld` field**
Add `bool TabHeld = false` to the record struct. This is a non-breaking change (default value, positional parameter with default). All existing `KeyEvent` construction is unchanged.

**Step 3 — Extend `KeyboardHookHandler` for TAB interception**
Add `VK_TAB` to the interception list when CAPS is held. Add `_tabHeld` tracking field. Populate `TabHeld` in direction key `KeyEvent` writes. Test: verify TAB + direction while CAPS held produces a `KeyEvent` with `TabHeld = true` and that TAB is suppressed (does not reach apps).

**Step 4 — Add modifier routing to `CapsLockMonitor`**
Add `_onModifiedDirectionKeyDown` callback parameter. Update `HandleDirectionKeyEvent` to route by modifier. Existing `_onDirectionKeyDown` path is unchanged (bare direction keys still navigate). Test: CAPS+direction still navigates; CAPS+SHIFT+direction calls new callback; CAPS+CTRL+direction calls new callback; CAPS+TAB+direction calls new callback.

**Step 5 — Implement `GridCalculator` logic**
Write static grid calculation methods: `GetMonitorWorkArea`, `GetGridStep`, `SnapToGrid`. These have no Win32 side effects beyond read-only `GetMonitorInfo`. Unit-test with hardcoded rect values to verify snap math.

**Step 6 — Implement `WindowManagerService.MoveWindow`**
Wire `GetForegroundWindow` bounds → grid calculator → `SetWindowPos`. Single-monitor only first. Test: CAPS+TAB+direction moves the foreground window by one grid step. Verify snap behavior with windows that start off-grid.

**Step 7 — Implement `WindowManagerService.GrowWindow` and `ShrinkWindow`**
Same pattern but modify only one edge per direction. Grow expands the edge in the given direction; shrink contracts it. Clamp to minimum window size (avoid zero or negative dimensions). Test: edge grows/shrinks correctly; size does not go below a minimum.

**Step 8 — Wire `OverlayOrchestrator.OnModifiedDirectionKeyDown`**
Add the method and `ExecuteWindowActionSta`. Add the lambda in `DaemonCommand.Run()` that connects `CapsLockMonitor` to the new orchestrator method. Test full flow: CAPS+TAB+direction moves window, overlay refreshes to new position.

**Step 9 — Cross-monitor movement**
Add `MonitorHelper.FindAdjacentMonitor`. Update `MoveWindow` to use it when the new position crosses a monitor boundary. Test with two-monitor setup.

**Step 10 — Mode-aware overlay rendering** (can be deferred to a separate phase)
Add `OverlayMode` enum to `IOverlayRenderer.Paint` as optional parameter. Implement `ModeIconRenderer` or extend `BorderRenderer` to show directional arrows when mode is non-navigate. Test: move mode shows arrow overlays; release TAB reverts to standard border overlays.

### Integration Summary Table

| Component | Status | Change Type | Primary Integration Point |
|-----------|--------|-------------|--------------------------|
| `KeyEvent` | Modify | Add `TabHeld` field | `KeyboardHookHandler` writes it |
| `KeyboardHookHandler` | Modify | Intercept VK_TAB, track TAB hold state | Already intercepts direction keys; add TAB same pattern |
| `CapsLockMonitor` | Modify | Modifier-aware dispatch | `HandleDirectionKeyEvent` routing logic |
| `DaemonCommand` | Modify | Wire new callback lambda | `new CapsLockMonitor(...)` constructor call |
| `FocusConfig` | Modify | Add grid config fields | `FocusConfig.Load()` — no callers change |
| `OverlayOrchestrator` | Modify | Add `OnModifiedDirectionKeyDown`, `_activeMode` | Called from `CapsLockMonitor` callback; calls `WindowManagerService` |
| `OverlayManager` | Modify (minor) | Accept mode hint for renderer selection | Called by `OverlayOrchestrator.ShowOverlaysForCurrentForeground()` |
| `IOverlayRenderer` | Modify (minor) | Optional `OverlayMode` parameter | `BorderRenderer.Paint` signature change |
| `WindowManagerService` | New | Move/grow/shrink logic | Called by `OverlayOrchestrator.ExecuteWindowActionSta` |
| `GridCalculator` | New (or in WindowManagerService) | Grid step and snap math | Called by `WindowManagerService` |
| `MonitorHelper` | Modify | Add `FindAdjacentMonitor` | Called by `WindowManagerService` for cross-monitor moves |
| `ModeIconRenderer` | New (optional) | Arrow icon overlay painting | Registered in `OverlayManager.CreateRenderer()` |

### Anti-Patterns Specific to v3.1

#### Anti-Pattern 7: Using GetWindowRect Instead of DwmGetWindowAttribute for Grid Baseline

**What people do:** Call `GetWindowRect(hwnd, &rect)` to get the current window position before computing the grid-snapped new position.

**Why it's wrong:** `GetWindowRect` returns the window's frame rect including the invisible DWM resize border (8px on each side on Windows 11). Positioning based on this rect causes visual drift — windows appear to move less than one grid step because the frame extends outside the visible bounds. The rest of the codebase uses `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` for accurate bounds.

**Do this instead:** Use `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` to read current visible bounds. When calling `SetWindowPos`, account for the frame inset to position the visible edge at the desired grid coordinate.

**Frame inset calculation:**
```csharp
// Get both rects to compute inset
RECT windowRect;   // GetWindowRect — frame rect
RECT frameBounds;  // DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) — visible rect

int insetLeft   = frameBounds.left - windowRect.left;
int insetTop    = frameBounds.top  - windowRect.top;
int insetRight  = windowRect.right  - frameBounds.right;
int insetBottom = windowRect.bottom - frameBounds.bottom;

// Then SetWindowPos uses frame rect coordinates:
// newFrameLeft = desiredVisibleLeft - insetLeft
```

#### Anti-Pattern 8: Calling SetWindowPos Without SWP_NOZORDER

**What people do:** `SetWindowPos(hwnd, HWND_TOP, x, y, w, h, 0)` — using HWND_TOP (value 0) as the z-order sentinel.

**Why it's wrong:** This moves the window to the top of the z-order (same as `BringWindowToTop`), reordering all windows. For a move/resize operation, the window's stacking order should not change.

**Do this instead:** Pass `SWP_NOZORDER` flag and use `null` (HWND_NULL) as the hWndInsertAfter parameter. This preserves the window's current z-order.

#### Anti-Pattern 9: Forgetting to Handle Already-Maximized Windows

**What people do:** Call `SetWindowPos` unconditionally on the foreground window.

**Why it's wrong:** If the window is maximized, `SetWindowPos` will not move or resize it (the OS ignores position/size for maximized windows). The user gets no feedback that the operation did nothing.

**Do this instead:** Before calling `SetWindowPos`, check if the window is maximized via `GetWindowPlacement` or `IsZoomed`. If maximized, either restore it first (`ShowWindow(hwnd, SW_RESTORE)`) then move, or silently no-op. The correct behavior depends on user expectation — for v3.1, a silent no-op is the safest initial choice (consistent with navigation's silent no-op when no candidate exists).

---

## Sources

- [LowLevelKeyboardProc — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — HIGH confidence, official API documentation (updated 2025-07)
- [KBDLLHOOKSTRUCT — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) — HIGH confidence, official API documentation
- [Hooks Overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks) — HIGH confidence, official documentation on hook types and thread requirements
- [UpdateLayeredWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) — HIGH confidence, official API documentation
- [SetLayeredWindowAttributes — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes) — HIGH confidence, official API documentation
- [Window Features (Layered Windows) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) — HIGH confidence, official documentation on layered window patterns
- [Extended Window Styles — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) — HIGH confidence, official WS_EX_TOOLWINDOW/WS_EX_LAYERED/WS_EX_TRANSPARENT/WS_EX_TOPMOST documentation
- [Using Messages and Message Queues — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/using-messages-and-message-queues) — HIGH confidence, official documentation on message loop structure
- [SetWindowPos — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) — HIGH confidence, official API for window move/resize
- [DwmGetWindowAttribute — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute) — HIGH confidence, official API for accurate visible bounds
- [MONITORINFO — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo) — HIGH confidence, rcWork field for usable monitor area excluding taskbar
- [CsWin32 — Microsoft GitHub](https://github.com/microsoft/CsWin32) — MEDIUM confidence, P/Invoke source generator used by project

---
*Architecture research for: Win32 directional window focus navigation — daemon + overlay preview mode*
*Parts 1-10 researched: 2026-02-28*
*Part 11 (v3.1 window management) researched: 2026-03-02*
