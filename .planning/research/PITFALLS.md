# Pitfalls Research

**Domain:** Win32 window management — directional focus navigation CLI tool + daemon overlay (v2.0)
**Researched:** 2026-02-28
**Confidence:** HIGH (majority of findings verified against official Microsoft documentation and confirmed against existing codebase)

---

## Section A: v2.0 New Pitfalls — Daemon, Keyboard Hook, and Overlay

These pitfalls are specific to the v2.0 milestone: adding `focus daemon` (persistent process with `WH_KEYBOARD_LL`) and transparent overlay windows showing directional previews.

---

### Pitfall A-1: Hook Delegate Garbage Collected — Hook Fires into Freed Memory

**What goes wrong:**
The `WH_KEYBOARD_LL` hook is installed by passing a managed delegate as an unmanaged function pointer. The .NET garbage collector has no visibility into the hook table that Windows maintains. Once the delegate object has no managed references, the GC may collect it. The next time a key is pressed, Windows tries to call the now-freed pointer, resulting in an `AccessViolationException` or a silent crash. The hook appears to "stop working" after approximately 30–60 seconds.

**Why it happens:**
When a delegate is marshaled to unmanaged code via P/Invoke, the GC cannot track the pointer held by Windows. If the delegate is created as a local variable or an anonymous lambda captured in a temporary context, it becomes eligible for collection as soon as the method returns. This is documented by the `callbackOnCollectedDelegate` MDA in .NET Framework and remains equally applicable in .NET 10.

**How to avoid:**
Store the hook delegate in a `static` field or an instance field on an object with process lifetime. Do not create the delegate as a local variable or inline lambda. The field must remain reachable through the hook's entire installed lifetime.

```csharp
// WRONG — local variable, GC-eligible after InstallHook() returns
void InstallHook()
{
    var proc = new HookProc(HookCallback); // collected after this method exits
    _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, proc, ...);
}

// CORRECT — static or long-lived instance field
private static HookProc _hookProc = new HookProc(HookCallback); // stays alive
private IntPtr _hookHandle;

void InstallHook()
{
    _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, ...);
}
```

**Warning signs:**
- Hook fires reliably immediately after startup but stops responding after 30–90 seconds
- Hook stops working under memory pressure
- `AccessViolationException` appearing from native frames in crash dumps

**Phase to address:**
Phase 1 (Daemon core — keyboard hook installation). Must be reviewed as part of hook implementation code review before any other testing occurs.

---

### Pitfall A-2: No Message Loop — Hook Never Fires

**What goes wrong:**
`WH_KEYBOARD_LL` is a "global" hook but is not injected into other processes. Instead, Windows delivers hook notifications by posting messages to the thread that called `SetWindowsHookEx`. If that thread has no Win32 message loop (no `GetMessage`/`DispatchMessage` pump), the messages never arrive and the hook callback is never called. The hook installs without error but appears completely inert.

**Why it happens:**
This is a documented requirement: "The thread that installed the hook must have a message loop." A .NET console application's main thread does not have a Win32 message loop by default — it runs a `Main()` method synchronously. Simply blocking with `Thread.Sleep`, `Console.ReadLine`, or a `CancellationToken.WaitHandle` is not sufficient; those do not process Win32 messages.

**How to avoid:**
Run the hook on a dedicated STA thread that runs a Win32 message loop. The simplest approach in a console app is a dedicated thread calling `Application.Run()` (System.Windows.Forms) or a manual `GetMessage`/`TranslateMessage`/`DispatchMessage` loop via P/Invoke. The hook callback itself must be fast (see Pitfall A-3) and post any work to a separate thread via a `ConcurrentQueue` or `Channel<T>`.

```csharp
// Minimal manual message loop pattern for a daemon thread
Thread hookThread = new Thread(() =>
{
    _hookProc = new HookProc(HookCallback); // assign to field, not local
    _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, IntPtr.Zero, 0);

    MSG msg;
    while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
    {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
    }

    UnhookWindowsHookEx(_hookHandle);
}) { IsBackground = true };
hookThread.SetApartmentState(ApartmentState.STA);
hookThread.Start();
```

**Warning signs:**
- `SetWindowsHookEx` returns a non-null handle but the callback is never invoked
- Manually pumping messages (e.g., calling `Application.DoEvents` or `Thread.Sleep` in a loop) does not work
- Hook works from a WPF/WinForms test app but not from the converted console daemon

**Phase to address:**
Phase 1 (Daemon core — message loop setup). Must be the first thing validated with a "key press received" log line before any overlay logic is added.

---

### Pitfall A-3: Hook Callback Exceeds Timeout — Hook Silently Removed

**What goes wrong:**
Windows enforces a timeout on `WH_KEYBOARD_LL` callbacks via the registry key `HKEY_CURRENT_USER\Control Panel\Desktop\LowLevelHooksTimeout` (default: 300ms, capped at 1000ms on Windows 10 1709+). If the callback exceeds this limit, Windows skips it for that keypress. On Windows 7 and later, after approximately 10 consecutive timeouts, **the hook is silently uninstalled without notification**. The daemon loses its ability to detect key presses with no log output and no error code.

**Why it happens:**
The callback runs synchronously on the thread's message loop, blocking all subsequent key delivery until it returns. Anything slow in the callback — overlay window creation, `EnumWindows`, scoring logic, any I/O — can cause a timeout. With the default 300ms budget, even a single slow window enumeration (~50ms+) under load can trigger the timeout.

**How to avoid:**
The callback must do minimal work and return immediately. All rendering, enumeration, and scoring must be offloaded to a separate thread via a non-blocking queue. The callback should only check the key code and post a message to the worker thread.

```csharp
// Correct pattern: callback enqueues, worker thread processes
private static readonly Channel<KeyEvent> _keyChannel = Channel.CreateBounded<KeyEvent>(10);

IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
{
    if (nCode >= 0)
    {
        var kbData = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        _keyChannel.Writer.TryWrite(new KeyEvent((int)wParam, kbData.vkCode));
    }
    return CallNextHookEx(_hookHandle, nCode, wParam, lParam); // always call immediately
}
```

There is no way to detect that the hook has been silently removed (confirmed in official docs: "There is no way for the application to know whether the hook is removed"). Implement a heartbeat mechanism: record the last callback timestamp and alert/restart the hook if no event is received for an unexpected duration while keys are known to be pressed.

**Warning signs:**
- Hook works normally during light typing but fails during rapid key sequences
- Hook stops responding after a period of heavy UI activity
- No log entries in callback despite keys being pressed

**Phase to address:**
Phase 1 (Daemon core — keyboard hook). The worker-thread pattern must be established from the start. The overlay pipeline goes on the worker thread; the callback is strictly a pass-through.

---

### Pitfall A-4: Overlay Window Steals Focus from Target Applications

**What goes wrong:**
When an overlay window is created or shown, Windows sets it as the new foreground window by default. This means that when the daemon shows overlay borders on candidate windows while CAPSLOCK is held, it may steal focus from the current application — causing text input to be interrupted, menus to close, or modal dialogs to dismiss.

**Why it happens:**
Any window that becomes visible without explicit suppression of activation is a candidate for receiving focus. `WS_EX_TOPMOST` alone does not prevent focus theft. `ShowWindow(SW_SHOW)` on a newly created window triggers an implicit activation.

**How to avoid:**
Apply `WS_EX_NOACTIVATE` extended style to all overlay windows. This prevents them from being activated on click or show. For WPF, also set `Focusable="False"` and handle `WM_MOUSEACTIVATE` to return `MA_NOACTIVATE`. For a P/Invoke overlay window, set the style at creation time and additionally intercept `WM_ACTIVATE` to send `WM_ACTIVATE` with `WA_INACTIVE` back.

```csharp
// When creating the overlay window via CreateWindowEx:
const int WS_EX_NOACTIVATE   = 0x08000000;
const int WS_EX_TOOLWINDOW   = 0x00000080;
const int WS_EX_TOPMOST      = 0x00000008;
const int WS_EX_LAYERED      = 0x00080000;
const int WS_EX_TRANSPARENT  = 0x00000020;

int exStyle = WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT;
```

Use `SetWindowPos` with `SWP_NOACTIVATE` flag when repositioning. Never use `SetForegroundWindow` on overlay windows.

**Warning signs:**
- Current application loses focus when CAPSLOCK is pressed and overlays appear
- Text editor cursor moves away from its current position
- Menu bars in applications close when overlay is shown

**Phase to address:**
Phase 2 (Overlay window creation). Must be validated as the first manual test: show overlay while typing in Notepad, verify no interruption.

---

### Pitfall A-5: Overlay Windows Appear in EnumWindows Navigation Candidates

**What goes wrong:**
The overlay windows created for the preview display are real Win32 top-level windows. If they have visible extents and the `WS_VISIBLE` flag set, they will be returned by `EnumWindows` and may pass the Alt+Tab filter, appearing as navigation candidates alongside real user windows. When the user navigates left, the overlay border window itself may be selected as the target.

**Why it happens:**
The existing `WindowEnumerator` correctly filters `WS_EX_TOOLWINDOW` windows — but only if the overlay windows are actually created with that style. If the daemon creates overlay windows without `WS_EX_TOOLWINDOW`, or if it creates them with `WS_EX_APPWINDOW` (which overrides the toolwindow exclusion), they slip through the filter. This is a closed loop: the enumeration that drives the overlay calculation would itself be influenced by the overlay windows.

**How to avoid:**
All overlay windows must be created with `WS_EX_TOOLWINDOW` as a mandatory extended style. This is already checked by the existing `WindowEnumerator` filter at line 109: `if (isToolWindow) continue`. Verify that the overlay window class has `WS_EX_TOOLWINDOW` and does NOT have `WS_EX_APPWINDOW`. Add an assertion in the `GetNavigableWindows` path (debug mode) that verifies no window with the daemon's own process ID appears in the result set.

```csharp
// Required: WS_EX_TOOLWINDOW ensures overlay windows are excluded from Alt+Tab and focus enumeration
// Required: WS_EX_NOACTIVATE prevents focus theft
// Required: WS_EX_LAYERED enables per-pixel transparency
// WS_EX_APPWINDOW must NOT be set (it would override the toolwindow exclusion)
```

**Warning signs:**
- Debug enumeration (`focus --debug enumerate`) shows windows with the daemon's process name in the list
- Navigation selects an invisible or translucent window that is the overlay itself
- Overlay window titles appear in score debug output

**Phase to address:**
Phase 2 (Overlay window creation). Add a debug-mode assertion in `WindowEnumerator` that verifies the current daemon process ID never appears in results. Run this assertion during overlay development.

---

### Pitfall A-6: DPI Mismatch Between DWMWA_EXTENDED_FRAME_BOUNDS and Overlay Window Positioning

**What goes wrong:**
The existing code correctly reads target window bounds via `DWMWA_EXTENDED_FRAME_BOUNDS`, which returns physical pixel coordinates. However, when creating and positioning overlay windows with `SetWindowPos`, Windows applies DPI scaling to the coordinates based on the DPI context of the thread calling `SetWindowPos`. If the calling thread has a different DPI context than the physical pixel space used by `DWMWA_EXTENDED_FRAME_BOUNDS`, the overlay window will appear offset — often by a consistent pixel amount proportional to the DPI scale factor.

**Why it happens:**
`DWMWA_EXTENDED_FRAME_BOUNDS` is documented as returning values "not adjusted for DPI" — meaning it always returns physical (raw pixel) coordinates. `SetWindowPos`, in contrast, operates in logical coordinates relative to the calling thread's DPI context. When the process declares `PerMonitorV2` awareness (as this project does via `app.manifest`), `SetWindowPos` accepts physical coordinates on the primary monitor but may apply per-monitor scaling adjustments on secondary monitors. The exact behavior depends on which monitor the overlay is being placed on.

**How to avoid:**
When calling `SetWindowPos` to position an overlay, use `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` on the thread that creates the overlay windows. Since the project already declares `PerMonitorV2` in the manifest, coordinates from `DWMWA_EXTENDED_FRAME_BOUNDS` should align with `SetWindowPos` on all monitors — but this must be verified explicitly on a multi-monitor setup with different DPI scales per monitor (e.g., primary at 100%, secondary at 150%).

The existing `app.manifest` already sets `PerMonitorV2` for the process. Ensure overlay window creation threads do not change the DPI context with `SetThreadDpiAwarenessContext` in a way that diverges from the process manifest.

**Warning signs:**
- Overlay borders are offset from target windows, especially on secondary monitors
- Offset is proportional to the DPI scaling percentage of the secondary monitor
- Overlay aligns correctly on the primary monitor but not on monitors with different DPI settings

**Phase to address:**
Phase 2 (Overlay window creation + positioning). Test on a two-monitor configuration with unequal DPI (100% primary, 150% secondary) before declaring overlay positioning complete.

---

### Pitfall A-7: CAPSLOCK Key State Ambiguity — Hook Sees Both Down and Toggle-Released Events

**What goes wrong:**
CAPSLOCK is a toggle key. When handling it with `WH_KEYBOARD_LL`, the daemon receives `WM_KEYDOWN` when the key is pressed and `WM_KEYUP` when it is released — identical to any other key. However, if the daemon suppresses the key (returns non-zero from the callback instead of calling `CallNextHookEx`), the toggle state of the CAPSLOCK LED and the system caps lock state may become inconsistent. Additionally, `GetAsyncKeyState` cannot be used inside the hook callback to check the current toggle state (official docs: "the asynchronous state of the key cannot be determined by calling `GetAsyncKeyState` from within the callback function").

**Why it happens:**
CAPSLOCK has dual semantics: it is a momentary physical key press (`WM_KEYDOWN`/`WM_KEYUP`) and a toggle that changes system state. Suppressing the key prevents the toggle state from changing, but the keyboard LED may still flip. Users relying on CAPSLOCK for typing will find their typing state unpredictable if the daemon suppresses CAPSLOCK unconditionally.

**How to avoid:**
Do not suppress CAPSLOCK. Let `CallNextHookEx` propagate the key normally. Track the held/released state in the hook callback using a boolean field toggled on `WM_KEYDOWN` (`VK_CAPITAL`) and cleared on `WM_KEYUP` (`VK_CAPITAL`). Show the overlay on keydown, hide it on keyup. This approach preserves the toggle behavior for users who actually use CAPSLOCK for typing.

If suppression is required for a future requirement, use `GetKeyState(VK_CAPITAL)` (not `GetAsyncKeyState`) on the worker thread to query the toggle state after the callback returns, as `GetKeyState` reads the thread's message-queue state (which is updated before the callback is called).

**Warning signs:**
- Users report that CAPSLOCK stops toggling uppercase after the daemon runs
- The daemon shows the overlay on first CAPSLOCK press but not on subsequent presses
- Overlay appears stuck (shown when it should be hidden, or vice versa)

**Phase to address:**
Phase 1 (Daemon core — CAPSLOCK detection). Define the key state tracking pattern before overlay integration to avoid debugging compound problems.

---

### Pitfall A-8: AHK's SendInput Suppresses the Daemon's Keyboard Hook

**What goes wrong:**
AutoHotkey uses `SendInput` to simulate key presses when executing hotkey actions. When AHK sends the CAPSLOCK+Arrow combination or sends the ALT key (as used by `focus.exe` for `SetForegroundWindow`), the `WH_KEYBOARD_LL` hook in the daemon will also receive these synthesized events — flagged with `LLKHF_INJECTED` in `KBDLLHOOKSTRUCT.flags`. Additionally, AHK temporarily removes its own low-level hooks during `SendInput` to allow uninterrupted injection, but this does not affect other processes' hooks. The daemon will see the AHK-injected keystrokes and may incorrectly interpret them as real user input.

**Why it happens:**
`SendInput` bypasses AHK's own hooks during injection, but `WH_KEYBOARD_LL` hooks in other processes still receive all input including injected events. The `LLKHF_INJECTED` flag (bit 4 of `KBDLLHOOKSTRUCT.flags`) is set on all events generated by `keybd_event` or `SendInput` to distinguish them from real hardware input. Most hook implementations ignore this flag and treat injected events identically to real keystrokes.

**How to avoid:**
Filter events in the hook callback by checking the `LLKHF_INJECTED` flag. If the daemon's purpose is to react to real hardware key presses only (specifically the physical CAPSLOCK key), reject events where `(flags & LLKHF_INJECTED) != 0`.

```csharp
IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
{
    if (nCode >= 0)
    {
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        const int LLKHF_INJECTED = 0x10;
        bool isInjected = (data.flags & LLKHF_INJECTED) != 0;

        if (!isInjected && data.vkCode == VK_CAPITAL)
        {
            // Real physical CAPSLOCK key press
            _keyChannel.Writer.TryWrite(new KeyEvent((int)wParam, data.vkCode));
        }
    }
    return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
}
```

Note: AHK also has its own `WH_KEYBOARD_LL` hook. Both hooks coexist in a hook chain. Neither hook can "remove" the other. The order of notification depends on which process installed its hook last (last-in, first-notified). This is generally harmless for reading-only hooks like this daemon.

**Warning signs:**
- The overlay flickers when AHK executes a hotkey that involves SendInput
- The overlay appears during `focus.exe` activation (when it sends ALT key via SendInput)
- CAPSLOCK hold detection triggers on non-CAPSLOCK key events during AHK hotkey execution

**Phase to address:**
Phase 1 (Daemon core — hook filtering). Add the injected-flag check alongside the CAPSLOCK vkCode check from the start.

---

### Pitfall A-9: Layered Window API Modes Are Mutually Exclusive and Cannot Be Switched

**What goes wrong:**
Windows provides two distinct APIs for layered (transparent) window drawing: `SetLayeredWindowAttributes` (whole-window opacity or color key) and `UpdateLayeredWindow` (per-pixel alpha bitmap). Once `SetLayeredWindowAttributes` has been called on a window, subsequent calls to `UpdateLayeredWindow` silently fail. Attempting to switch modes without resetting the `WS_EX_LAYERED` style bit causes the window to draw incorrectly or not at all.

**Why it happens:**
The two APIs are documented as mutually exclusive. `SetLayeredWindowAttributes` puts the window into "attribute mode" and `UpdateLayeredWindow` puts it into "window mode." Once in one mode, the window cannot switch without explicitly clearing and re-setting `WS_EX_LAYERED` via `SetWindowLong`. The official docs state: "Once `SetLayeredWindowAttributes` has been called for a layered window, subsequent `UpdateLayeredWindow` calls will fail until the layering style bit is cleared and set again."

**How to avoid:**
Pick one approach and use it exclusively. For colored border overlays, `SetLayeredWindowAttributes` with a transparency color key is the simplest approach: paint the window with a background color (the key color) and the border color, then call `SetLayeredWindowAttributes(hwnd, keyColor, 0, LWA_COLORKEY)` so the key color becomes transparent. This avoids per-pixel alpha complexity.

For the initial implementation, use `SetLayeredWindowAttributes` only. If per-pixel compositing is needed in a later phase, create new overlay windows rather than switching modes on existing ones.

**Warning signs:**
- Overlay window appears solid black or solid colored rather than transparent
- `UpdateLayeredWindow` returns false after `SetLayeredWindowAttributes` was called at startup
- Overlay transparency works initially but breaks after a resize or repositioning

**Phase to address:**
Phase 2 (Overlay window creation). Decide the rendering approach during design. Document the chosen mode in code comments to prevent accidental mixing.

---

### Pitfall A-10: WS_EX_TOPMOST Overlay Falls Behind Other Topmost Windows

**What goes wrong:**
The overlay windows use `WS_EX_TOPMOST` to stay above normal windows. However, other applications also use `WS_EX_TOPMOST` — task managers, game overlays, screen recorders, notification systems. When another topmost window is activated, it may move above the overlay windows in the Z-order, causing the overlay borders to be hidden behind another app's UI. The overlay appears to "disappear" even though it is still logically shown.

**Why it happens:**
`WS_EX_TOPMOST` is not an absolute guarantee of being the topmost visible window. Multiple topmost windows compete based on Z-order within the topmost layer. When a topmost window is activated or brought to front, it becomes the top of the topmost stack, and the focus overlay windows drop below it.

**How to avoid:**
This is partially unavoidable by design — the operating system owns Z-order within the topmost layer. The practical mitigation is: when the CAPSLOCK hold state changes (overlay is being shown), call `SetWindowPos(hwnd, HWND_TOPMOST, ...)` with `SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE` on each overlay window to re-assert their position at the top of the topmost stack. Do this on the worker thread each time the overlay is refreshed.

Do not call `SetWindowPos` with `HWND_TOPMOST` from the hook callback itself — that must remain fast (see Pitfall A-3).

**Warning signs:**
- Overlay borders disappear when a notification popup appears
- Overlay is visible in isolation but hidden by game overlay tools when present
- Overlay is inconsistently visible depending on what other apps are running

**Phase to address:**
Phase 2 (Overlay lifecycle management). Consider this a known limitation and document it. The mitigation (re-assert HWND_TOPMOST on each refresh) is sufficient for normal use cases.

---

### Pitfall A-11: Daemon Process Has No Console Window But Allocates One Implicitly

**What goes wrong:**
The existing `focus.exe` is a console application (`OutputType: Exe` in `.csproj`). When launched in daemon mode, it will either: (a) inherit and keep the terminal window it was started from, making the daemon visually disruptive; or (b) if launched by AHK with `Run, focus.exe daemon, , Hide`, still briefly flash a console window. Additionally, the daemon mode needs to suppress all `Console.WriteLine` output that the CLI mode emits.

**Why it happens:**
.NET console applications always have a console subsystem bit set in the PE header. Even when no terminal is attached, Windows may allocate a console on launch unless the project is configured as a Windows GUI application (`OutputType: WinExe`) or the console is explicitly freed via `FreeConsole()`. The existing codebase uses `Console.Error.WriteLine` for verbose/debug output throughout.

**How to avoid:**
Two viable approaches:

Option A: Change `OutputType` to `WinExe` in the `.csproj`. This produces a GUI subsystem binary that does not allocate a console. The daemon will be completely silent on launch. CLI mode can re-attach a console with `AllocConsole()` when needed, or accept that debug output requires a separate terminal. This is the cleanest approach for a daemon.

Option B: Keep `OutputType: Exe` but call `FreeConsole()` at the start of the daemon subcommand before starting the message loop. The existing console window (from the parent terminal) is freed but the terminal that launched the daemon is not affected.

For v2.0, Option B (FreeConsole at daemon entry) is the lower-risk change because it does not require refactoring the existing CLI output paths.

**Warning signs:**
- Launching `focus daemon` from AHK shows a brief console flash
- The terminal that ran `focus daemon` is blocked / waiting for the process to exit
- `Console.ReadKey` calls in daemon code block the message loop thread

**Phase to address:**
Phase 1 (Daemon infrastructure). Must be decided before building any other daemon functionality, as it affects how the daemon is launched and tested.

---

### Pitfall A-12: Daemon Has No Single-Instance Guarantee — Multiple Daemons Fight Over the Same Hook

**What goes wrong:**
If the user launches `focus daemon` twice (e.g., AHK script restarts it on crash), two daemon instances both install `WH_KEYBOARD_LL` hooks. Both receive all key events. Both may show overlay windows at the same time, doubling the borders and causing visual corruption. Each daemon's overlay windows may interfere with the other's enumeration (see Pitfall A-5). The user has no way to know two instances are running.

**Why it happens:**
There is no mechanism preventing multiple instances. The existing CLI tool is stateless and designed to allow concurrent invocations. A daemon mode changes this constraint entirely.

**How to avoid:**
Implement a named mutex at daemon entry. Use a GUID-derived name to avoid collision with other apps:

```csharp
using var mutex = new Mutex(true, "Global\\FocusDaemon-{your-project-guid}", out bool isNewInstance);
if (!isNewInstance)
{
    // Another instance is running — exit silently or signal the existing instance
    return;
}
// Proceed with daemon setup
// Keep 'mutex' variable in scope for process lifetime
```

Use `Global\\` prefix (Global kernel namespace) so the mutex works across session boundaries if needed. Keep the `Mutex` object referenced for the full process lifetime — do not dispose it early. The GC can collect a `Mutex` even in a `using` block if the compiler optimizes it out; assign to a field if daemon is long-lived.

**Warning signs:**
- Double borders visible on target windows when CAPSLOCK is held
- System performance degrades over time (multiple hook chains)
- Two `focus.exe` processes visible in Task Manager

**Phase to address:**
Phase 1 (Daemon infrastructure). Must be in place before any end-to-end testing begins.

---

### Pitfall A-13: Overlay Not Excluded From Its Own Window Scoring

**What goes wrong:**
When the daemon re-runs the window enumeration and scoring logic (to determine which window to highlight per direction), the overlay windows may appear as candidates in the directional score results. The scoring logic then picks the overlay border window as the best candidate in a given direction, causing a nonsensical selection. Since `GetNavigableWindows` filters by `WS_EX_TOOLWINDOW`, this only fails if the overlay window is not correctly flagged.

**Why it happens:**
This is the same root cause as Pitfall A-5, framed from the scoring perspective. The overlay windows are real `HWND`s with real screen coordinates. If they pass the tool-window filter (due to a missing style flag), they will have coordinates that place them exactly on top of their host windows — exactly where a navigation candidate might legitimately be. The scoring algorithm has no concept of "this window is mine."

**How to avoid:**
Ensure all overlay HWNDs are created with `WS_EX_TOOLWINDOW`. Additionally, track the HWNDs of all created overlay windows in a `HashSet<nint>` and add an explicit exclusion pass in `GetNavigableWindows` for any HWND that belongs to the daemon process. This defense-in-depth approach catches both the style flag omission and any edge cases:

```csharp
// In WindowEnumerator.GetNavigableWindows, add after the filter pipeline:
uint currentPid = (uint)Environment.ProcessId;
PInvoke.GetWindowThreadProcessId(hwnd, out uint windowPid);
if (windowPid == currentPid) continue; // never include our own windows
```

**Warning signs:**
- Score table shows windows with the daemon process name as candidates
- Navigation targets the overlay border window instead of the real window behind it

**Phase to address:**
Phase 2 (Overlay integration with enumeration). Verify with `focus --debug enumerate` while the daemon is running; zero rows with the daemon process name must appear.

---

## Section B: Retained v1.0 Critical Pitfalls

These pitfalls from the original research remain fully applicable in v2.0. The daemon reuses the same `WindowEnumerator`, `NavigationService`, and `FocusActivator` subsystems.

---

### Pitfall B-1: DPI Virtualization Corrupts Window Coordinates

**What goes wrong:**
When the process is not declared per-monitor DPI aware, Windows virtualizes coordinates returned by `GetWindowRect`. A window that is physically 800x600px at 150% DPI on a secondary monitor may be reported as ~533x400px (scaled to 96 DPI). Comparing coordinates across monitors with different DPI settings will produce completely wrong directional distances.

**Why it happens:**
By default, .NET console apps have no DPI awareness manifest entry. Windows detects this and silently scales all coordinate values to 96 DPI logical units before returning them.

**How to avoid:**
The project already has an `app.manifest` declaring `PerMonitorV2` DPI awareness — this pitfall is **already mitigated**. Verify that the daemon mode does not create any windows on threads where `SetThreadDpiAwarenessContext` has changed the DPI context away from `PerMonitorV2`.

**Warning signs:**
- Navigation works on the developer machine but fails on machines with non-100% display scaling
- Overlay borders appear offset from their target windows on secondary monitors

**Phase to address:**
v2.0 Phase 2 (Overlay positioning) — specifically verify DPI context of the thread that positions overlay windows.

---

### Pitfall B-2: Cloaked Windows Appear Visible

**What goes wrong:**
`IsWindowVisible()` returns `TRUE` for cloaked windows (windows on other virtual desktops, UWP frames). They appear as navigation candidates and can receive unwanted focus attempts.

**How to avoid:**
Already implemented in `WindowEnumerator` — `DwmGetWindowAttribute(DWMWA_CLOAKED)` check at line 69. This pitfall is **already mitigated**.

**Warning signs:**
- Navigation switches focus to a window the user cannot see (flashes in taskbar only)

**Phase to address:**
Already addressed. Regression-test in v2.0 after any changes to `WindowEnumerator`.

---

### Pitfall B-3: SetForegroundWindow Silently Fails Due to Foreground Lock

**What goes wrong:**
`SetForegroundWindow()` returns `TRUE` even when it does not actually change focus. Windows flashes the taskbar button instead.

**How to avoid:**
Already implemented in `FocusActivator` — the `SendInput` ALT-key bypass at lines 22-50. This pitfall is **already mitigated**.

**Warning signs:**
- Focus switching works from terminal but fails via AHK hotkey invocation

**Phase to address:**
Already addressed. In daemon mode, when AHK triggers actual navigation (`focus left`, etc.), this path is unchanged.

---

### Pitfall B-4: GetWindowRect Includes Invisible 8px Shadow Borders

**What goes wrong:**
`GetWindowRect()` includes an invisible resize shadow border on Windows 10/11. Distance calculations are off by ~8px per side.

**How to avoid:**
Already implemented — `DWMWA_EXTENDED_FRAME_BOUNDS` used exclusively at line 158. This pitfall is **already mitigated**.

**Phase to address:**
Already addressed. Overlay positioning must also use `DWMWA_EXTENDED_FRAME_BOUNDS` bounds, not `GetWindowRect`.

---

### Pitfall B-5: Window Filter Misses UWP App Host Windows

**What goes wrong:**
UWP apps have two HWNDs (frame + CoreWindow). Simple filtering returns the wrong one.

**How to avoid:**
Already implemented — Raymond Chen Alt+Tab algorithm with UWP dedup at lines 116-133 of `WindowEnumerator`. This pitfall is **already mitigated**.

**Phase to address:**
Already addressed. Verify overlays render on `ApplicationFrameWindow` bounds, not `CoreWindow` bounds.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Hook delegate as local variable | Simpler code | GC collection causes hook to stop after 30-60s | Never |
| Doing work in hook callback | Avoids worker thread complexity | Hook timeout → silent removal after 10 consecutive timeouts | Never |
| Not checking `LLKHF_INJECTED` | Simpler callback logic | Overlay flickers when AHK sends synthetic keys | Never for this project |
| `SetLayeredWindowAttributes` and `UpdateLayeredWindow` mixed | Access to both APIs | `UpdateLayeredWindow` silently fails; mode switch requires style reset | Never |
| Omitting `WS_EX_NOACTIVATE` on overlay | Easier window creation | Overlay steals focus from user's active application | Never |
| Omitting `WS_EX_TOOLWINDOW` on overlay | One less style flag | Overlay appears in navigation candidates; self-referential enumeration | Never |
| Omitting single-instance mutex | No daemon coordination code | Two daemons fight over the same hook; double-overlays shown | Never |
| Using `GetWindowRect` instead of `DWMWA_EXTENDED_FRAME_BOUNDS` | Simpler P/Invoke | 8px coordinate errors on Windows 10/11 | Never |
| Skipping cloaked-window check | Fewer API calls | Selects invisible windows on virtual desktops | Never |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| AHK + Daemon | AHK hotkeys send keys via SendInput which the hook receives as injected events | Check `LLKHF_INJECTED` flag in hook callback; only respond to physical hardware events |
| AHK + Daemon | Running `focus.exe daemon` without Hide flag creates a visible console window | Launch with `Run, focus.exe daemon,, Hide` or change output type to WinExe |
| AHK + Daemon | No mechanism to restart daemon if it crashes | Use AHK `OnExit` or a watchdog script to detect and restart the daemon process |
| WH_KEYBOARD_LL + Console App | Console app has no message loop; hook never fires | Dedicated STA thread with `GetMessage`/`DispatchMessage` loop required |
| Overlay + EnumWindows | Overlay HWNDs appear in navigation candidates | `WS_EX_TOOLWINDOW` required on all overlay windows; verify with `focus --debug enumerate` |
| Overlay + DPI | `SetWindowPos` on secondary monitor with different DPI places overlay at wrong position | Use physical pixel coordinates throughout; verify on unequal DPI multi-monitor setup |
| SetForegroundWindow + Daemon's SendInput | The daemon sends ALT via SendInput for focus switching, which its own hook will receive | Filter `LLKHF_INJECTED` in hook callback |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Window enumeration in hook callback | Hook timeout → silent removal after ~10 timeouts | Move all enumeration to worker thread; callback only enqueues key events | First keypress when enumeration takes >300ms |
| Creating overlay windows on CAPSLOCK press (cold path) | Visible lag between CAPSLOCK press and overlay appearing | Pre-create overlay windows at daemon startup; hide/show them rather than create/destroy | Every CAPSLOCK press if windows are created lazily |
| Calling `GetWindowText` in enumeration for all windows | Hangs for 2-5 seconds when a hung app is open | Only call for windows that pass all other filters | Any session with a hung application |
| Full re-enumeration on every overlay refresh | CPU spike; overlay flickers | Cache window list and invalidate only on shell notifications (WinEventHook for window create/destroy) | Sessions with rapidly changing window sets |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Injecting simulated ALT key globally without cleanup | Sticky ALT key state if daemon crashes between key-down and key-up | Use try/finally to always send ALT-up; send ALT-up before `SetForegroundWindow` |
| Named mutex without Global\\ prefix | Multiple daemon instances across user sessions can coexist | Use `Global\\` prefix in mutex name |
| Accepting arbitrary window class names in exclude config without validation | Config injection causes unexpected behavior | Validate config values as simple strings; do not pass to Win32 format strings |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Overlay shows on CAPSLOCK press but CAPSLOCK is used for typing | Typing is interrupted every time CAPSLOCK is used | Pass through CAPSLOCK normally (do not suppress); overlay appears while held, disappears on release — minimal disruption to toggle behavior |
| Overlay appears with a frame delay (enumeration latency) | CAPSLOCK pressed but overlay appears 100-200ms later | Pre-enumerate and cache on a background thread; update cache on shell events |
| Overlay border covers window content obscuring context | User cannot see what is in the overlaid window | Border only (outer 4-6px); interior must be fully transparent via `WS_EX_TRANSPARENT` |
| Navigation direction selected after overlay shown is wrong direction | User releases CAPSLOCK and presses arrow expecting overlay's highlighted window | Overlay must update in real time as scoring changes (e.g., foreground window changes) |
| Selecting wrong window due to off-axis bias | User presses "right" and diagonal window is selected | Configurable axis bias strategies (already implemented in v1.0) |

---

## "Looks Done But Isn't" Checklist

- [ ] **Hook delegate lifetime:** Hook installed and fires on first key — verify it still fires after 2+ minutes of no input (GC test)
- [ ] **Message loop:** Hook callback fires in all keyboard focus contexts — verify it fires when a game or fullscreen app has focus
- [ ] **Hook timeout:** Callback returns < 50ms in all cases — add timing instrumentation in debug builds
- [ ] **Focus theft:** Overlay appears — verify active window (via `GetForegroundWindow`) is unchanged before and after overlay display
- [ ] **Enumeration exclusion:** `focus --debug enumerate` run while daemon is running — verify zero rows with daemon process name
- [ ] **Single instance:** Start two daemon instances — verify second exits immediately via mutex
- [ ] **DPI overlay alignment:** Overlay borders align with target window visual edges — verify on 150% DPI secondary monitor
- [ ] **CAPSLOCK pass-through:** CAPSLOCK toggles normally for typing — verify caps lock LED and uppercase behavior unaffected by daemon
- [ ] **AHK injected key filtering:** AHK SendInput during hotkey action — verify overlay does not flicker or trigger spuriously
- [ ] **Daemon shutdown:** Kill daemon via Task Manager — verify no stuck keyboard hook remnants; re-install daemon restarts cleanly
- [ ] **Layered window mode:** Overlay renders transparently — verify `UpdateLayeredWindow` is never called if `SetLayeredWindowAttributes` was used (or vice versa)

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Hook delegate GC-collected | LOW | Add `static` or instance-field storage for delegate; no logic change |
| No message loop — hook never fires | MEDIUM | Add dedicated STA hook thread with GetMessage loop; refactor callback to use Channel |
| Hook timeout causing silent removal | HIGH | Redesign callback to be pass-through only; move all logic to worker thread |
| Overlay steals focus | LOW | Add `WS_EX_NOACTIVATE` to overlay creation; add `SWP_NOACTIVATE` to SetWindowPos calls |
| Overlay in navigation candidates | LOW | Add `WS_EX_TOOLWINDOW` to overlay; add process-ID exclusion guard in WindowEnumerator |
| DPI offset on overlay | MEDIUM | Audit DPI context of overlay-positioning thread; verify on multi-DPI monitor setup |
| Layered window mode mixing | LOW | Choose one mode (recommend SetLayeredWindowAttributes); never call both on same HWND |
| Multiple daemon instances | LOW | Add named mutex check at daemon entry |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Hook delegate GC (A-1) | Phase 1 — Hook installation | Run daemon for 5 minutes of inactivity; verify hook still fires |
| No message loop (A-2) | Phase 1 — Hook installation | Confirm first keypress triggers callback before any other work |
| Hook timeout (A-3) | Phase 1 — Hook installation | Time callback execution; must be < 10ms |
| Overlay focus theft (A-4) | Phase 2 — Overlay creation | Type in Notepad while holding CAPSLOCK; no focus interruption |
| Overlay in enumeration (A-5, A-13) | Phase 2 — Overlay creation | `focus --debug enumerate` while daemon running; daemon process absent |
| DPI mismatch on positioning (A-6) | Phase 2 — Overlay creation | Two-monitor 100%+150% DPI setup; borders align with windows on both |
| CAPSLOCK state ambiguity (A-7) | Phase 1 — Hook installation | CAPSLOCK still toggles normally; overlay tracks hold not toggle |
| AHK injected keys (A-8) | Phase 1 — Hook installation | AHK hotkeys fire; overlay does not flicker during hotkey execution |
| Layered window mode mixing (A-9) | Phase 2 — Overlay rendering | Overlay is transparent; no white/black fill visible |
| Topmost Z-order competition (A-10) | Phase 2 — Overlay lifecycle | Overlay visible with Task Manager open; acceptable degradation documented |
| Console window suppression (A-11) | Phase 1 — Daemon infrastructure | Launch from AHK with Hide; no console flash |
| Single instance (A-12) | Phase 1 — Daemon infrastructure | Two launches; second exits; one set of overlays |
| DPI virtualization (B-1) | Already addressed (app.manifest) | Verify overlay thread does not change DPI context |
| Cloaked windows (B-2) | Already addressed | Regression-test after WindowEnumerator changes |
| SetForegroundWindow (B-3) | Already addressed | Navigation via AHK hotkey still works in daemon mode |
| GetWindowRect shadow border (B-4) | Already addressed | Overlay position uses DWMWA_EXTENDED_FRAME_BOUNDS, not GetWindowRect |
| UWP HWND confusion (B-5) | Already addressed | Overlay renders on ApplicationFrameWindow for UWP apps |

---

## Sources

- [LowLevelKeyboardProc callback function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — HIGH confidence (official docs, updated 2025-07-14)
- [callbackOnCollectedDelegate MDA — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/framework/debug-trace-profile/callbackoncollecteddelegate-mda) — HIGH confidence (official docs)
- [SetLayeredWindowAttributes function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes) — HIGH confidence (official docs)
- [UpdateLayeredWindow function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) — HIGH confidence (official docs)
- [SetForegroundWindow function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) — HIGH confidence (official docs)
- [High DPI Desktop Application Development on Windows — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) — HIGH confidence (official docs)
- [Window Features — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) — HIGH confidence (official docs)
- [Hooks Overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks) — HIGH confidence (official docs)
- [LowLevelKeyboardProc — registry timeout behavior, Windows 10 1709 1000ms cap](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — HIGH confidence (official docs)
- [Understanding SendInput and keyboard hooks — AutoHotkey Community](https://www.autohotkey.com/boards/viewtopic.php?t=127074) — MEDIUM confidence (community-verified, consistent with LLKHF_INJECTED docs)
- [Ouch! CallbackOnCollectedDelegate was detected — dzimchuk.net](https://dzimchuk.net/ouch-callbackoncollecteddelegate-was-detected/) — MEDIUM confidence (practitioner account, consistent with MDA docs)
- [Low Level Global Keyboard Hook / Sink in C# .NET — Dylan's Web](https://www.dylansweb.com/2014/10/low-level-global-keyboard-hook-sink-in-c-net/) — MEDIUM confidence (practitioner, corroborated by official docs)
- [Foreground activation — Raymond Chen, The Old New Thing](https://devblogs.microsoft.com/oldnewthing/20090220-00/?p=19083) — HIGH confidence (official Microsoft blog)
- [Transparent Windows in WPF — Microsoft Learn (Dwayne Need)](https://learn.microsoft.com/en-us/archive/blogs/dwayneneed/transparent-windows-in-wpf) — MEDIUM confidence (official blog, WPF-specific but principles apply)
- [DWMWINDOWATTRIBUTE — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) — HIGH confidence (official docs)
- [EnumWindows function — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows) — HIGH confidence (official docs)

---

*Pitfalls research for: Win32 window management — directional focus navigation with daemon overlay*
*Researched: 2026-02-28*
