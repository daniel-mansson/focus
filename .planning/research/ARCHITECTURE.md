# Architecture Research

**Domain:** Win32 Window Management — CLI + Daemon with Keyboard Hook and Overlay Rendering
**Researched:** 2026-02-28
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

## Sources

- [LowLevelKeyboardProc — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — HIGH confidence, official API documentation (updated 2025-07)
- [KBDLLHOOKSTRUCT — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) — HIGH confidence, official API documentation
- [Hooks Overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks) — HIGH confidence, official documentation on hook types and thread requirements
- [UpdateLayeredWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) — HIGH confidence, official API documentation
- [SetLayeredWindowAttributes — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes) — HIGH confidence, official API documentation
- [Window Features (Layered Windows) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) — HIGH confidence, official documentation on layered window patterns
- [Extended Window Styles — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) — HIGH confidence, official WS_EX_TOOLWINDOW/WS_EX_LAYERED/WS_EX_TRANSPARENT/WS_EX_TOPMOST documentation
- [Using Messages and Message Queues — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/using-messages-and-message-queues) — HIGH confidence, official documentation on message loop structure
- [CsWin32 — Microsoft GitHub](https://github.com/microsoft/CsWin32) — MEDIUM confidence, P/Invoke source generator used by project

---
*Architecture research for: Win32 directional window focus navigation — daemon + overlay preview mode*
*Researched: 2026-02-28*
