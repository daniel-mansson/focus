# Phase 4: Daemon Core - Research

**Researched:** 2026-03-01
**Domain:** Win32 WH_KEYBOARD_LL global keyboard hook, Windows daemon process lifecycle, System.Threading.Channels producer/consumer, NotifyIcon tray icon
**Confidence:** HIGH (core Win32 hook API verified via official docs; CsWin32 interop pattern verified via GitHub issue #245; Channel<T> verified via official .NET docs)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Daemon lifecycle:**
- Support both foreground (default) and background (`--background` flag) modes
- System tray icon with Exit option for daemon lifecycle management (overrides earlier out-of-scope decision — user explicitly chose this)
- Brief one-line confirmation on startup (e.g., "Focus daemon started. Listening for CAPSLOCK."), then silent
- Ctrl+C stops foreground mode; system tray Exit stops background mode
- Second instance kills the existing daemon and starts fresh (no error-and-exit)

**CAPSLOCK hold detection:**
- Suppress CAPSLOCK toggle behavior entirely while daemon is running — CAPSLOCK becomes a pure modifier key
- Quick taps are silently swallowed — CAPSLOCK does nothing unless held
- Only bare CAPSLOCK triggers hold detection — Ctrl+CAPSLOCK, Shift+CAPSLOCK, Alt+CAPSLOCK are ignored
- LLKHF_INJECTED events (AHK-synthesized) are filtered out — no overlay flicker from AHK
- This phase: hold/release events trigger debug log lines only (no overlay rendering)

**Logging & debug output:**
- Log to stdout/stderr only — no dedicated log file
- Silent by default after startup confirmation — consistent with v1's silent-by-default design
- `--verbose` flag enables CAPSLOCK event logging (reuses v1's existing flag pattern)
- Verbose log format: timestamped plain text (e.g., `[12:34:56.789] CAPSLOCK held`)

**Error handling:**
- Hook installation failure: print clear error message, exit immediately (no retries)
- Sleep/wake: auto-recover by detecting system events and reinstalling hook on wake
- No crash recovery or watchdog — if it crashes, user restarts manually

### Claude's Discretion
- Message pump implementation details (Win32 message loop vs Application.Run)
- Exact system tray icon design and tooltip text
- Background mode detach mechanism
- Hook reinstallation strategy on wake (polling vs system event subscription)
- Mutex naming convention

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| DAEMON-01 | User can start a background daemon via `focus daemon` that persists until explicitly stopped | System.CommandLine subcommand pattern; ApplicationContext + Application.Run message pump keeps process alive |
| DAEMON-02 | Daemon installs WH_KEYBOARD_LL hook and detects CAPSLOCK held/released state | WH_KEYBOARD_LL + KBDLLHOOKSTRUCT.vkCode == VK_CAPITAL (0x14); WM_KEYDOWN/WM_KEYUP distinguish press/release |
| DAEMON-04 | Daemon enforces single instance via named mutex (second launch kills existing and starts fresh) | System.Threading.Mutex with "Global\\focus-daemon" name; Process.GetProcessesByName + Kill pattern for replace semantics |
| DAEMON-05 | Daemon cleans up overlay windows and unhooks keyboard hook on exit/crash | UnhookWindowsHookEx in Console.CancelKeyPress handler + AppDomain.CurrentDomain.ProcessExit |
| DAEMON-06 | Daemon filters LLKHF_INJECTED key events to prevent AHK-triggered overlay flicker | KBDLLHOOKSTRUCT.flags & LLKHF_INJECTED (0x00000010) check in hook callback |
</phase_requirements>

## Summary

Phase 4 implements a persistent Windows daemon that installs a WH_KEYBOARD_LL global keyboard hook, detects CAPSLOCK hold/release, enforces single instance via named mutex, provides a system tray icon for lifecycle management, and cleans up on exit. This phase has no overlay rendering — it only logs CAPSLOCK events.

The central architectural challenge is that WH_KEYBOARD_LL requires a Windows message pump running on the hook-installing thread. A console app does not have one by default. The chosen solution is to use WinForms `Application.Run(ApplicationContext)` on a dedicated STA thread, which provides the message pump and enables both the keyboard hook callbacks and the NotifyIcon system tray icon. The hook callback itself must return within 1000ms (Windows 10 1709+ hard limit) — all work beyond raw event detection must be offloaded to a `Channel<T>` worker thread.

CsWin32 has a known interop wrinkle for `SetWindowsHookEx`: `GetModuleHandle(null)` returns an `nint` that must be wrapped in a `FreeLibrarySafeHandle(handle, false)` for the third parameter. The HOOKPROC delegate must be stored in a static field to prevent GC collection after the hook-installing method returns.

**Primary recommendation:** Use WinForms ApplicationContext + Application.Run on a dedicated STA thread for the message pump; install WH_KEYBOARD_LL from that thread; post key events to a Channel<KeyEvent> for processing on a worker thread; use System.Threading.Mutex("Global\\focus-daemon") for single-instance enforcement.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Windows.CsWin32 | 0.3.269 (already in project) | SetWindowsHookEx, UnhookWindowsHookEx, CallNextHookEx, GetModuleHandle, KBDLLHOOKSTRUCT | Already used; type-safe P/Invoke generation |
| System.Windows.Forms | Built into .NET 8 Windows | Application.Run, ApplicationContext, NotifyIcon | Provides message pump + tray icon in one package |
| System.Threading.Channels | Built into .NET 8 | Channel<KeyEvent> producer/consumer between hook thread and worker | Official .NET async queue; lock-free; built into runtime |
| System.Threading.Mutex | Built into .NET 8 | Global named mutex for single-instance enforcement | BCL standard; cross-process visibility with "Global\\" prefix |
| System.CommandLine | 2.0.3 (already in project) | `focus daemon` subcommand and `--background`, `--verbose` flags | Already used for CLI |
| System.Diagnostics.Process | Built into .NET 8 | Kill existing daemon instance when second launch detected | BCL standard |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Runtime.InteropServices | Built into .NET 8 | FreeLibrarySafeHandle wrapper for SetWindowsHookEx hmod parameter | Required for CsWin32 interop workaround |
| System.Drawing | Built into .NET 8 Windows | Icon loading for NotifyIcon | Required for tray icon image |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| WinForms Application.Run | Raw Win32 GetMessage/DispatchMessage loop | Raw loop avoids WinForms dependency but requires more P/Invoke plumbing; WinForms also gives NotifyIcon for free |
| System.Threading.Channels | BlockingCollection<T> | Channels are lower-allocation, async-native, and the official modern pattern; BlockingCollection is older and sync-oriented |
| Process.Kill for second-instance replace | Error-and-exit | User decision: replace, not error; Kill is the correct tool |

**Installation:**
```bash
# All required packages already in focus.csproj:
# Microsoft.Windows.CsWin32 0.3.269
# System.CommandLine 2.0.3
# Add to focus.csproj for WinForms:
# <UseWindowsForms>true</UseWindowsForms>
# System.Threading.Channels is inbox in .NET 8 — no NuGet needed
```

## Architecture Patterns

### Recommended Project Structure

```
focus/
├── Program.cs                          # Add "daemon" subcommand to RootCommand
├── Windows/
│   ├── (existing v1 files...)
│   ├── Daemon/
│   │   ├── DaemonCommand.cs            # Subcommand handler: single-instance check, startup, teardown
│   │   ├── KeyboardHookHandler.cs      # WH_KEYBOARD_LL install/uninstall, HOOKPROC, Channel<KeyEvent> producer
│   │   ├── KeyEvent.cs                 # Record: VkCode, IsKeyDown, IsInjected, Timestamp
│   │   ├── CapsLockMonitor.cs          # Channel consumer: hold/release state machine, verbose logging
│   │   ├── TrayIcon.cs                 # NotifyIcon setup, Exit menu item, ApplicationContext subclass
│   │   └── DaemonMutex.cs             # Named mutex acquire/release, kill-existing logic
```

### Pattern 1: WH_KEYBOARD_LL on a Dedicated STA Thread with Message Pump

**What:** The hook-installing thread must run a Windows message pump. WinForms `Application.Run(ApplicationContext)` provides this. The hook is installed after the message pump starts (from within the ApplicationContext constructor or OnLoad equivalent).

**When to use:** Required — WH_KEYBOARD_LL callbacks are delivered via the message pump of the thread that called SetWindowsHookEx. Without a pump, callbacks never fire.

**Example:**
```csharp
// Source: Microsoft Learn - LowLevelKeyboardProc
// https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc

// In DaemonCommand.cs
var thread = new Thread(() =>
{
    Application.Run(new DaemonApplicationContext(channel, verbose));
});
thread.SetApartmentState(ApartmentState.STA);
thread.IsBackground = false; // Keep process alive
thread.Start();
thread.Join();  // Block main thread until tray icon exits

// In DaemonApplicationContext : ApplicationContext
public DaemonApplicationContext(Channel<KeyEvent> channel, bool verbose)
{
    _trayIcon = new TrayIcon(this);
    _hook = new KeyboardHookHandler(channel);
    _hook.Install();  // Safe here — message pump starts immediately after ctor
}
```

### Pattern 2: CsWin32 SetWindowsHookEx with FreeLibrarySafeHandle

**What:** CsWin32's generated binding for `SetWindowsHookEx` takes a `SafeHandle` for hMod, but `GetModuleHandle(null)` returns `nint`. Wrap the handle with `FreeLibrarySafeHandle(handle, false)` — the `false` prevents it from calling `FreeLibrary` on the handle.

**When to use:** Always, for WH_KEYBOARD_LL global hooks from C# with CsWin32.

**Example:**
```csharp
// Source: CsWin32 GitHub Issue #245
// https://github.com/microsoft/CsWin32/issues/245

// CRITICAL: Store delegate in static field — GC collects local variables after method returns
// even while the unmanaged hook is still active, causing ExecutionEngineException crash
private static HOOKPROC? s_hookProc;

public void Install()
{
    s_hookProc = HookCallback;  // Must be static field, not local variable
    var hmod = new FreeLibrarySafeHandle(PInvoke.GetModuleHandle((string?)null), false);
    _hookHandle = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, s_hookProc, hmod, 0);
    if (_hookHandle.IsInvalid)
        throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
}
```

NativeMethods.txt additions needed:
```
SetWindowsHookEx
UnhookWindowsHookEx
CallNextHookEx
GetModuleHandle
```

### Pattern 3: HOOKPROC Callback — CAPSLOCK Detection and Key Suppression

**What:** The LowLevelKeyboardProc callback receives `nCode`, `wParam` (message type), and `lParam` (pointer to KBDLLHOOKSTRUCT). Return `CallNextHookEx` to pass through, return 1 (non-zero) to suppress the key. CAPSLOCK suppression requires returning 1 on both WM_KEYDOWN and WM_KEYUP for VK_CAPITAL.

**When to use:** Core of DAEMON-02 and DAEMON-06 requirements.

**Example:**
```csharp
// Source: Microsoft Learn - LowLevelKeyboardProc + KBDLLHOOKSTRUCT
// https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
// https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct

private const uint VK_CAPITAL = 0x14;
private const uint LLKHF_INJECTED = 0x00000010;
private const uint LLKHF_ALTDOWN = 0x00000020;
private const uint WM_KEYDOWN = 0x0100;
private const uint WM_KEYUP = 0x0101;
private const uint WM_SYSKEYDOWN = 0x0104;
private const uint WM_SYSKEYUP = 0x0105;

private unsafe LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode < 0)
        return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

    var kbd = (KBDLLHOOKSTRUCT*)lParam.Value;

    // DAEMON-06: Filter AHK-injected events
    if ((kbd->flags & LLKHF_INJECTED) != 0)
        return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

    // Only bare CAPSLOCK (no Alt held)
    if (kbd->vkCode == VK_CAPITAL && (kbd->flags & LLKHF_ALTDOWN) == 0)
    {
        bool isKeyDown = (uint)wParam == WM_KEYDOWN || (uint)wParam == WM_SYSKEYDOWN;

        // Post to worker thread via Channel — MUST be fire-and-forget, not awaited
        // Hook callback has a 1000ms total budget on Windows 10 1709+
        _channel.Writer.TryWrite(new KeyEvent(VK_CAPITAL, isKeyDown, kbd->time));

        // DAEMON-02: Suppress CAPSLOCK toggle — return non-zero to eat the event
        return (LRESULT)1;
    }

    return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);
}
```

### Pattern 4: Channel<KeyEvent> Producer/Consumer

**What:** Hook callback (producer) posts events to an unbounded channel via `TryWrite`. A background worker thread (consumer) reads events with `ReadAllAsync` and runs the CAPSLOCK state machine.

**When to use:** Required — the 1000ms hook timeout means NO blocking work in the callback.

**Example:**
```csharp
// Source: Microsoft Learn - Channels .NET
// https://learn.microsoft.com/en-us/dotnet/core/extensions/channels

// Creation (in DaemonCommand or DaemonApplicationContext)
var channel = Channel.CreateUnbounded<KeyEvent>(new UnboundedChannelOptions
{
    SingleWriter = true,   // Only hook callback writes
    SingleReader = true,   // Only one consumer worker
    AllowSynchronousContinuations = false  // Don't run continuations on hook thread
});

// Consumer (in CapsLockMonitor, running on Task.Run worker thread)
await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
{
    if (evt.IsKeyDown)
        OnCapsLockHeld(evt.Timestamp);
    else
        OnCapsLockReleased(evt.Timestamp);
}

// Producer in hook callback (fire-and-forget)
_channel.Writer.TryWrite(new KeyEvent(vkCode, isKeyDown, time));
// TryWrite always succeeds on unbounded channels (until Complete() is called)
```

### Pattern 5: Single-Instance Mutex with Replace Semantics

**What:** Try to acquire a named mutex. If another instance owns it, kill that process by name (filtering out own PID), wait for it to exit, then try again. Use `"Global\focus-daemon"` for cross-session visibility.

**When to use:** DAEMON-04 — second instance replaces first.

**Example:**
```csharp
// Source: System.Threading.Mutex docs + Process.GetProcessesByName pattern
// https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex

private const string MutexName = @"Global\focus-daemon";

public static Mutex AcquireOrReplace()
{
    var mutex = new Mutex(false, MutexName, out bool createdNew);

    if (!createdNew)
    {
        // Another instance is running — kill it
        int ownPid = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("focus"))
        {
            if (p.Id == ownPid) continue;
            try
            {
                p.Kill();
                p.WaitForExit(3000);
            }
            catch { /* Process may have already exited */ }
        }
        // Re-acquire mutex after old instance is gone
        mutex.Dispose();
        mutex = new Mutex(true, MutexName, out _);
    }
    else
    {
        mutex.WaitOne(0); // Take ownership
    }
    return mutex;  // Caller must keep reference alive (store in static or long-lived field)
}
```

### Pattern 6: Sleep/Wake Hook Recovery via WM_POWERBROADCAST

**What:** Register for `WM_POWERBROADCAST` on the message window. On `PBT_APMRESUMEAUTOMATIC` (0x0012), unhook and reinstall the keyboard hook. The hidden window from WinForms ApplicationContext receives this message automatically.

**When to use:** Required by user decision — auto-recover after sleep/wake.

**Example:**
```csharp
// Source: Microsoft Learn - WM_POWERBROADCAST
// https://learn.microsoft.com/en-us/windows/win32/power/wm-powerbroadcast

private const int WM_POWERBROADCAST = 0x0218;
private const int PBT_APMRESUMEAUTOMATIC = 0x0012;

// Override in a Form subclass or use NativeWindow + AssignHandle
protected override void WndProc(ref Message m)
{
    if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_APMRESUMEAUTOMATIC)
    {
        _hook.Uninstall();
        _hook.Install();
    }
    base.WndProc(ref m);
}
```

### Pattern 7: NotifyIcon Tray Icon with ApplicationContext

**What:** Subclass `ApplicationContext` and create a `NotifyIcon` with a context menu containing "Exit". Calling `Application.ExitThread()` in the Exit handler tears down the message pump, which triggers cleanup.

**When to use:** Required by user decision — system tray icon for background mode.

**Example:**
```csharp
// Source: Microsoft Learn - NotifyIcon + ApplicationContext pattern
// https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.applicationcontext

public class DaemonApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;

    public DaemonApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Application.ExitThread();
        });

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,  // Or load from embedded resource
            ContextMenuStrip = menu,
            Text = "Focus Daemon",
            Visible = true
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _trayIcon.Dispose();
        base.Dispose(disposing);
    }
}
```

### Pattern 8: System.CommandLine Subcommand for `focus daemon`

**What:** Add a `Command("daemon")` subcommand to the existing `RootCommand`. Options `--background` and `--verbose` are local to the daemon subcommand.

**When to use:** DAEMON-01 — `focus daemon` CLI entry point.

**Example:**
```csharp
// Source: Microsoft Learn - System.CommandLine define-commands
// https://learn.microsoft.com/en-us/dotnet/standard/commandline/define-commands

var daemonCommand = new Command("daemon", "Run the focus daemon (persistent keyboard hook)");
var backgroundOption = new Option<bool>("--background") { Description = "Detach from console and run as background process" };
var verboseOption = new Option<bool>("--verbose", "-v") { Description = "Log CAPSLOCK hold/release events to stdout" };
daemonCommand.Options.Add(backgroundOption);
daemonCommand.Options.Add(verboseOption);

daemonCommand.SetAction(parseResult =>
{
    bool background = parseResult.GetValue(backgroundOption);
    bool verbose = parseResult.GetValue(verboseOption);
    return DaemonCommand.Run(background, verbose);
});

rootCommand.Subcommands.Add(daemonCommand);
```

### Pattern 9: Graceful Cleanup on Ctrl+C and Process Exit

**What:** Wire `Console.CancelKeyPress` (Ctrl+C in foreground) and `AppDomain.CurrentDomain.ProcessExit` to the same cleanup path. In background mode, cleanup fires from `Application.ExitThread()` in the tray Exit handler. The cleanup path calls `UnhookWindowsHookEx` and closes the mutex.

**When to use:** DAEMON-05 — no orphaned hooks after exit.

**Example:**
```csharp
// Source: Console.CancelKeyPress docs
// https://learn.microsoft.com/en-us/dotnet/api/system.console.cancelkeypress

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;  // Prevent immediate termination
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

// Channel consumer loop exits when token cancelled
// Then cleanup:
_hook.Uninstall();   // UnhookWindowsHookEx
_mutex.ReleaseMutex();
_mutex.Dispose();
_trayIcon.Visible = false;
_trayIcon.Dispose();
Application.ExitThread();
```

### Anti-Patterns to Avoid

- **Doing work in the hook callback:** Any delay beyond a TryWrite risks the 1000ms timeout. On Windows 10 1709+, the hook is silently removed with no notification. Delegate ALL processing to the Channel consumer.
- **Storing HOOKPROC in a local variable:** The GC can collect the delegate after the method returns, invalidating the unmanaged pointer and crashing with an `ExecutionEngineException`. Always store in a `static` field.
- **Thread.Sleep instead of a message pump:** The hook thread must run `GetMessage`/`DispatchMessage` (or `Application.Run`). `Thread.Sleep` does not pump messages.
- **Using `new HINSTANCE(0)` for hmod:** CsWin32 generated binding does not accept this — use `FreeLibrarySafeHandle(GetModuleHandle(null), false)`.
- **Not resetting CAPSLOCK state on wake:** After unhook/reinstall, the CAPSLOCK monitor's held-state must be reset to avoid a stuck-held condition.
- **Modifying keyboard toggle state:** The daemon suppresses the CAPSLOCK toggle via hook return value (1). Do NOT also send a synthetic key event to "undo" the toggle — that synthetic event will arrive back in the hook with LLKHF_INJECTED set and will be filtered, but it adds unnecessary complexity.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Message pump | Custom GetMessage/DispatchMessage loop | `Application.Run(ApplicationContext)` | WinForms provides this and adds NotifyIcon support at no extra cost |
| Thread-safe key event queue | Manual lock/queue | `Channel<KeyEvent>` | Lock-free, async-native, cancellation-aware, built into .NET 8 |
| Process-level exclusivity | PID file or registry key | `System.Threading.Mutex("Global\\focus-daemon")` | OS-managed; cleans up automatically on process crash |
| Tray icon from scratch | Win32 Shell_NotifyIcon manually | `System.Windows.Forms.NotifyIcon` | Shell_NotifyIcon has 30+ years of edge cases; NotifyIcon wraps them correctly |
| Wake detection | Polling thread checking system time | `WM_POWERBROADCAST` on message window | Event-driven; zero CPU overhead between sleep/wake cycles |

**Key insight:** The Win32 message loop ecosystem (hooks, power events, tray icons) has decades of edge cases. WinForms wraps the most dangerous parts correctly. The cost of `<UseWindowsForms>true</UseWindowsForms>` is a slightly larger binary — acceptable for a developer tool.

## Common Pitfalls

### Pitfall 1: Hook Callback Returns After 1000ms Timeout — Hook Silently Removed
**What goes wrong:** On Windows 10 version 1709+, if the HOOKPROC does not return within 1000ms, Windows silently removes the hook with no notification. The daemon appears to run but CAPSLOCK events stop firing — potentially hours or days after startup.
**Why it happens:** Any blocking operation (I/O, sleep, channel write that blocks, lock contention) in the hook callback will exceed the timeout under load.
**How to avoid:** Use `Channel<T>.Writer.TryWrite()` (never `WriteAsync`, never `WaitToWriteAsync`) in the hook callback. TryWrite on an UnboundedChannel is always synchronous and never blocks.
**Warning signs:** CAPSLOCK events stop firing after an idle period or under system load; no error logged.

### Pitfall 2: GC Collects HOOKPROC Delegate — ExecutionEngineException
**What goes wrong:** The hook callback crashes the process with an unhandled `ExecutionEngineException` — typically minutes or hours after startup, not immediately.
**Why it happens:** The CLR doesn't know the unmanaged hook holds a reference to the delegate. If stored in a local or instance variable that goes out of scope, GC collects it, invalidating the function pointer Windows holds.
**How to avoid:** Declare `private static HOOKPROC? s_hookProc` as a static field on the handler class. Assign before calling `SetWindowsHookEx`.
**Warning signs:** Random crashes during low-memory periods or after GC pressure; not reproducible on first run.

### Pitfall 3: FreeLibrarySafeHandle Ownership Confusion
**What goes wrong:** Passing `new FreeLibrarySafeHandle(handle, true)` (ownsHandle: true) causes `FreeLibrary` to be called on the module handle when the SafeHandle is disposed, potentially unloading a system DLL.
**Why it happens:** The second parameter to FreeLibrarySafeHandle controls whether `FreeLibrary` is called on dispose. For a module handle obtained from the current process (not loaded by us), we do NOT own it.
**How to avoid:** Always use `false` for the ownsHandle parameter: `new FreeLibrarySafeHandle(PInvoke.GetModuleHandle((string?)null), false)`.
**Warning signs:** AccessViolationException or strange behavior after the first GC cycle.

### Pitfall 4: CAPSLOCK State Left Toggled After Hook Removal
**What goes wrong:** When the daemon exits while CAPSLOCK is held, or when the hook is reinstalled after sleep, the keyboard toggle state may be in the wrong position — the user's CAPSLOCK indicator light might be stuck on or off.
**Why it happens:** The daemon suppresses the toggle by eating the key event. If the hook is removed mid-hold, the CAPSLOCK key state is inconsistent.
**How to avoid:** On hook removal/daemon exit, check `Console.CapsLock` (.NET 8 property). If it differs from expected state (should be OFF since the daemon suppresses toggle), synthesize a CAPSLOCK keypress via SendInput with KEYEVENTF_KEYUP to restore the toggle state.
**Warning signs:** User reports CAPSLOCK stuck on after closing the daemon.

### Pitfall 5: Named Mutex Garbage Collected
**What goes wrong:** The named mutex is released immediately, allowing a second instance to start before the first is finished.
**Why it happens:** If the Mutex object isn't stored in a long-lived field (static or in a class that lives as long as the process), GC finalizes it, which releases the mutex handle.
**How to avoid:** Store the `Mutex` in a static field or pass it to the `DaemonApplicationContext` and keep it alive for the process lifetime. Call `GC.KeepAlive(mutex)` if in doubt.
**Warning signs:** Single-instance check fails intermittently; two daemon processes run simultaneously.

### Pitfall 6: Hook Fires on Non-Physical CAPSLOCK (Modifier Combinations)
**What goes wrong:** The daemon triggers CAPSLOCK hold detection when the user presses Ctrl+CapsLock or Alt+CapsLock.
**Why it happens:** The hook fires for all CAPSLOCK events. The user decision requires bare CAPSLOCK only.
**How to avoid:** Check `LLKHF_ALTDOWN` in the flags field. For Ctrl/Shift detection, check `GetKeyState(VK_CONTROL)` / `GetKeyState(VK_SHIFT)` — but note: `GetKeyState` in the hook callback returns the key state as it was before the hook was called, not the async state. Use `GetKeyState`, not `GetAsyncKeyState` (the docs warn that async state is not updated when the hook fires).
**Warning signs:** Overlay flicker when using Ctrl+CapsLock shortcuts.

### Pitfall 7: Missing OutputType Configuration for WinForms
**What goes wrong:** Adding WinForms to the project shows a console window when the daemon runs in background mode.
**Why it happens:** The default `<OutputType>Exe</OutputType>` always allocates a console window.
**How to avoid:** The daemon supports both foreground (console needed) and background (no console). Use `FreeConsole()` P/Invoke when `--background` is specified, rather than changing `OutputType`. This way the foreground mode still has console output, and background mode detaches it.
**Warning signs:** A console window flashes briefly when starting the daemon with `--background`.

## Code Examples

Verified patterns from official sources:

### KBDLLHOOKSTRUCT Flags Reference
```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct
// All flags verified from official docs
internal static class KbdllFlags
{
    public const uint LLKHF_EXTENDED = 0x00000001;           // Extended key (function key, numpad)
    public const uint LLKHF_LOWER_IL_INJECTED = 0x00000002; // Injected from lower integrity process
    public const uint LLKHF_INJECTED = 0x00000010;          // Injected from ANY process (DAEMON-06 filter)
    public const uint LLKHF_ALTDOWN = 0x00000020;           // ALT key held (context code)
    public const uint LLKHF_UP = 0x00000080;                // Key being released (transition state)
}

internal static class VirtualKeys
{
    public const uint VK_CAPITAL = 0x14;  // CAPSLOCK
    public const uint VK_SHIFT   = 0x10;
    public const uint VK_CONTROL = 0x11;
}

internal static class WinMessages
{
    public const uint WM_KEYDOWN    = 0x0100;
    public const uint WM_KEYUP      = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP   = 0x0105;
    public const int  WM_POWERBROADCAST = 0x0218;
    public const int  PBT_APMRESUMEAUTOMATIC = 0x0012;
}
```

### Minimal Daemon Thread Skeleton
```csharp
// Shows how foreground vs background mode differs in thread structure
public static int Run(bool background, bool verbose)
{
    // 1. Single-instance check (kill existing if present)
    using var mutex = DaemonMutex.AcquireOrReplace();

    // 2. Create the event channel
    var channel = Channel.CreateUnbounded<KeyEvent>(new UnboundedChannelOptions
    {
        SingleWriter = true, SingleReader = true,
        AllowSynchronousContinuations = false
    });

    // 3. Print startup confirmation (user decision)
    Console.WriteLine("Focus daemon started. Listening for CAPSLOCK.");

    // 4. Detach console if background mode
    if (background)
        PInvoke.FreeConsole();  // Add FreeConsole to NativeMethods.txt

    // 5. Run STA message pump thread (installs hook inside ApplicationContext ctor)
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

    // Consumer task on thread pool
    var consumerTask = Task.Run(() =>
        new CapsLockMonitor(channel.Reader, verbose).RunAsync(cts.Token));

    // STA thread with message pump — blocks until Application.ExitThread() called
    var staThread = new Thread(() =>
        Application.Run(new DaemonApplicationContext(channel, cts)));
    staThread.SetApartmentState(ApartmentState.STA);
    staThread.Start();
    staThread.Join();

    // Signal consumer to stop
    channel.Writer.Complete();
    consumerTask.GetAwaiter().GetResult();

    return 0;
}
```

### UnhookWindowsHookEx Cleanup Pattern
```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unhookwindowshookex
public void Uninstall()
{
    if (_hookHandle is { IsInvalid: false })
    {
        PInvoke.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = null;
        s_hookProc = null;  // Allow GC to collect delegate now that hook is removed
    }
}
```

### NativeMethods.txt Additions for Phase 4
```
SetWindowsHookEx
UnhookWindowsHookEx
CallNextHookEx
GetModuleHandle
FreeConsole
GetKeyState
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Arbitrary hook timeout | Hard 1000ms max on Windows 10 1709+ | Windows 10 1709 (2017) | Hook callback must be nearly instantaneous |
| WH_KEYBOARD (thread-specific) for global hooks | WH_KEYBOARD_LL (process-local, no DLL injection needed) | Windows 2000 | WH_KEYBOARD_LL does not require DLL injection; works in .NET natively |
| SetWindowsHookEx with DLL for global hooks | WH_KEYBOARD_LL with null module handle | Windows 2000 | Low-level hooks are special: hMod can be null for LL hooks even in global scope |

**Deprecated/outdated:**
- `WH_KEYBOARD` (non-LL): Requires DLL injection for global hooks — impossible in .NET without a native DLL. Use WH_KEYBOARD_LL instead.
- `keybd_event`: Deprecated; use `SendInput` (already in NativeMethods.txt from v1).
- WinForms `Application.DoEvents()`: Terrible — tight loop that burns CPU. Not needed; `Application.Run` handles the pump properly.

## Open Questions

1. **FreeConsole + console output in background mode**
   - What we know: `FreeConsole()` detaches the console; subsequent writes to `Console.Out` after detach may throw or silently fail.
   - What's unclear: Does the startup message print before `FreeConsole()` is called? (It should if we print first, then detach.) Do we need `AllocConsole()` for re-attach capability?
   - Recommendation: Print startup confirmation BEFORE calling `FreeConsole()`. No re-attach needed — verbose output is only useful in foreground mode anyway.

2. **GetKeyState for Ctrl/Shift detection in hook callback**
   - What we know: The docs say `GetAsyncKeyState` is unreliable in the hook callback (async state not yet updated). `GetKeyState` returns the state at message time, which is safe.
   - What's unclear: Does `GetKeyState(VK_CONTROL)` reliably detect Ctrl held when processing the CAPSLOCK event in the low-level hook?
   - Recommendation: Test in a spike. If unreliable, instead check the wParam: Ctrl+CAPSLOCK generates `WM_SYSKEYDOWN` (not `WM_KEYDOWN`) which provides a simpler filter — skip on `WM_SYSKEYDOWN`.

3. **WM_POWERBROADCAST delivery to ApplicationContext hidden window**
   - What we know: WM_POWERBROADCAST is sent to all top-level windows. ApplicationContext uses a hidden message-only window internally.
   - What's unclear: Whether the internal ApplicationContext message window receives WM_POWERBROADCAST, or whether we need to create an explicit NativeWindow with WndProc override.
   - Recommendation: Create an explicit hidden Form (or NativeWindow) to receive WM_POWERBROADCAST rather than relying on ApplicationContext internals. This is safer and more explicit.

4. **CsWin32 HOOKPROC spike (flagged in STATE.md)**
   - What we know: CsWin32 Issue #245 documents the FreeLibrarySafeHandle workaround. STATE.md flags this as needing a spike before full implementation.
   - Recommendation: The spike should verify: (a) FreeLibrarySafeHandle pattern compiles, (b) HOOKPROC fires on real keystrokes, (c) static field prevents GC crash. Plan Wave 0 as a 30-minute spike task before KeyboardHookHandler is fully built.

## Sources

### Primary (HIGH confidence)
- [LowLevelKeyboardProc - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — callback signature, nCode, wParam, lParam, timeout behavior, silent removal warning
- [KBDLLHOOKSTRUCT - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) — all flag values including LLKHF_INJECTED (0x10), LLKHF_ALTDOWN, LLKHF_UP
- [Channels - .NET Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) — Channel.CreateUnbounded, UnboundedChannelOptions, TryWrite, ReadAllAsync patterns
- [System.Threading.Mutex - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex?view=net-8.0) — Global\\ prefix, named mutex cross-process semantics
- [System.CommandLine define-commands - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/commandline/define-commands) — Subcommands.Add, Command class, SetAction pattern
- [WM_POWERBROADCAST - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/power/wm-powerbroadcast) — PBT_APMRESUMEAUTOMATIC constant
- [NotifyIcon Class - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-8.0) — NotifyIcon API
- [ApplicationContext Class - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.applicationcontext) — ApplicationContext + Application.Run pattern

### Secondary (MEDIUM confidence)
- [CsWin32 Issue #245 - GitHub](https://github.com/microsoft/CsWin32/issues/245) — FreeLibrarySafeHandle workaround for SetWindowsHookEx hmod parameter; verified against CsWin32 Discussion #248
- [CsWin32 Discussion #248 - GitHub](https://github.com/microsoft/CsWin32/discussions/248) — Message pump requirement for console apps using WH_KEYBOARD_LL

### Tertiary (LOW confidence)
- Various community sources on keyboard hook GC pitfalls — consistent with official docs; no single authoritative source

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — CsWin32 already in project; System.Windows.Forms and System.Threading.Channels are inbox .NET 8 BCL; all verified via official docs
- Architecture: HIGH — WH_KEYBOARD_LL + message pump requirement verified in official docs; CsWin32 interop pattern verified in GitHub issue by maintainer
- Pitfalls: HIGH for hook timeout (official docs) and GC delegate (official docs + CsWin32 issue); MEDIUM for CAPSLOCK state restoration (logical deduction, not official source)

**Research date:** 2026-03-01
**Valid until:** 2026-09-01 (stable Win32 API, stable .NET 8 BCL — 6 months before revalidation needed)
