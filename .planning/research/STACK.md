# Stack Research

**Domain:** Windows background daemon with keyboard hook and transparent overlay rendering
**Researched:** 2026-02-28
**Confidence:** HIGH (keyboard hook: MEDIUM for CsWin32 interop specifics; overlay rendering: HIGH for Win32 pattern)

---

## Scope of This Document

This document covers **additions and changes** required for v2.0 only. The v1.0 stack (CsWin32 0.3.269, System.CommandLine 2.0.3, .NET 8 runtime, System.Text.Json) is validated and unchanged. Every recommendation below integrates with the existing CsWin32 P/Invoke setup. No third-party native dependencies are introduced.

Note: The project csproj currently targets `net8.0`. The milestone context mentions .NET 10 as a target framework. The daemon changes do not require a framework upgrade — both .NET 8 and .NET 10 support everything described here. Framework upgrade is a separate decision.

---

## New APIs Required

### Keyboard Hook Subsystem

| Win32 API | NativeMethods.txt Entry | Purpose |
|-----------|------------------------|---------|
| `SetWindowsHookExW` | `SetWindowsHookEx` | Install global WH_KEYBOARD_LL hook |
| `UnhookWindowsHookEx` | `UnhookWindowsHookEx` | Remove hook on daemon exit |
| `CallNextHookEx` | `CallNextHookEx` | Chain hook — mandatory to avoid breaking other apps |
| `GetMessage` | `GetMessage` | Block-wait for messages on the hook thread (the message pump) |
| `TranslateMessage` | `TranslateMessage` | Standard message loop plumbing |
| `DispatchMessage` | `DispatchMessage` | Dispatch to window proc (needed even for hook-only loops) |
| `PostThreadMessage` | `PostThreadMessage` | Signal the hook thread to shut down cleanly (WM_QUIT) |

`KBDLLHOOKSTRUCT` is generated automatically when `SetWindowsHookEx` is requested. Key fields:
- `vkCode` — check against `VK_CAPITAL` (0x14) for CAPSLOCK
- `flags` bit 7 (`LLKHF_UP`) — 0 = key pressed, 1 = key released
- `flags` bit 4 (`LLKHF_INJECTED`) — filter out injected keystrokes to avoid loops

**Critical architecture constraint:** WH_KEYBOARD_LL is global-only (cannot scope to a thread). The callback fires on the **thread that called SetWindowsHookEx**, and that thread must run a Win32 message loop (`GetMessage` / `DispatchMessage`). This is not optional — without a message pump, hook callbacks are never invoked. See message loop pattern below.

### Overlay Window Subsystem

| Win32 API | NativeMethods.txt Entry | Purpose |
|-----------|------------------------|---------|
| `RegisterClassExW` | `RegisterClassEx` | Register the overlay window class |
| `CreateWindowExW` | `CreateWindowEx` | Create layered overlay window per direction |
| `DestroyWindow` | `DestroyWindow` | Tear down overlays on CAPSLOCK release |
| `SetWindowPos` | `SetWindowPos` | Position/size overlay over target window; use `SWP_NOACTIVATE` always |
| `ShowWindow` | `ShowWindow` | Show/hide overlays |
| `GetDC` | `GetDC` | Get screen DC for UpdateLayeredWindow |
| `ReleaseDC` | `ReleaseDC` | Release screen DC |
| `CreateCompatibleDC` | `CreateCompatibleDC` | Create memory DC for bitmap rendering |
| `DeleteDC` | `DeleteDC` | Clean up memory DC |
| `CreateDIBSection` | `CreateDIBSection` | Allocate 32-bit ARGB bitmap for per-pixel alpha |
| `SelectObject` | `SelectObject` | Select bitmap into memory DC |
| `DeleteObject` | `DeleteObject` | Free GDI objects |
| `UpdateLayeredWindow` | `UpdateLayeredWindow` | Composite per-pixel-alpha bitmap onto screen |
| `DefWindowProcW` | `DefWindowProc` | Default window message handler |
| `GetModuleHandleW` | `GetModuleHandle` | Get HINSTANCE for RegisterClassEx / CreateWindowEx |

**Window style combination for overlay windows (use on CreateWindowEx):**

```
WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
```

- `WS_EX_LAYERED` — required for UpdateLayeredWindow; enables per-pixel alpha
- `WS_EX_TRANSPARENT` — click-through (mouse events fall through to windows beneath)
- `WS_EX_TOPMOST` — always above application windows
- `WS_EX_TOOLWINDOW` — excluded from Alt+Tab switcher AND from EnumWindows navigation filtering (already checked by existing `WindowEnumerator`)
- `WS_EX_NOACTIVATE` — prevents overlay from stealing keyboard focus from the active window

The existing `WindowEnumerator` already filters `WS_EX_TOOLWINDOW` windows from navigation candidates. No changes needed there — overlay windows will be naturally excluded.

---

## Recommended Stack Additions

### Core Technologies (New for v2.0)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Win32 `WH_KEYBOARD_LL` via CsWin32 | — (Win32 built-in) | Global low-level keyboard hook for CAPSLOCK detection | The only Win32 mechanism to intercept keyboard events globally without a foreground window. Works in .NET via P/Invoke when the hook thread runs a message loop. No library required — CsWin32 generates the bindings. |
| Win32 layered windows (`WS_EX_LAYERED` + `UpdateLayeredWindow`) via CsWin32 | — (Win32 built-in) | Transparent per-pixel-alpha overlay windows | The correct Win32 primitive for always-on-top transparent overlays. Per-pixel alpha via UpdateLayeredWindow allows precise colored borders with transparency everywhere else. Zero framework overhead. Already in the project's Win32 P/Invoke pattern. |
| Win32 GDI (`CreateDIBSection`, `SelectObject`) via CsWin32 | — (Win32 built-in) | Draw colored borders into the layered window bitmap | GDI DIB sections provide direct 32-bit ARGB pixel buffer access. Draw borders by filling RECT regions in the pixel buffer directly (4 thin rectangles per overlay = top/bottom/left/right border bands). Faster than GDI+ for simple filled rectangles. |

### Supporting Libraries (New for v2.0)

None. All new capabilities are Win32 APIs accessible via the existing CsWin32 setup. No additional NuGet packages are required.

The NativeMethods.txt file needs the new API entries listed above. CsWin32 generates everything from it.

### Development Tools (No Changes)

No new tooling required. Existing .NET 8 SDK + CsWin32 0.3.269 setup handles all new APIs.

---

## Architecture Pattern: Daemon Message Loop

The daemon subcommand (`focus daemon`) must dedicate one thread to the Win32 message pump. This thread owns the keyboard hook and processes overlay window messages.

**Reason:** WH_KEYBOARD_LL callbacks fire on the thread that called SetWindowsHookEx, dispatched through that thread's message queue. Without GetMessage running, callbacks never arrive. The 1-second hook timeout (Windows 10 1709+) means a slow callback will be bypassed by the OS — the hook thread must return from callbacks immediately.

**Pattern (no WinForms, no WPF):**

```csharp
// Daemon startup — run on dedicated STA thread
[STAThread]
static void RunDaemonThread()
{
    // 1. Install keyboard hook (hmod = null for LL hooks from same process)
    var hookHandle = PInvoke.SetWindowsHookEx(
        WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
        KeyboardHookProc,   // managed delegate — pin with GCHandle or keep field reference
        new HINSTANCE(0),   // null hmod is correct for WH_KEYBOARD_LL in-process
        0                   // dwThreadId = 0 means global
    );

    // 2. Pump messages until PostThreadMessage(WM_QUIT) is called
    MSG msg;
    while (PInvoke.GetMessage(out msg, new HWND(0), 0, 0))
    {
        PInvoke.TranslateMessage(msg);
        PInvoke.DispatchMessage(msg);
    }

    // 3. Cleanup
    PInvoke.UnhookWindowsHookEx(hookHandle);
}

// Hook callback — called on the message-pump thread
static LRESULT KeyboardHookProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0)
    {
        var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        bool isKeyUp = (kbStruct.flags & 0x80) != 0;  // LLKHF_UP = bit 7

        if (kbStruct.vkCode == 0x14)  // VK_CAPITAL
        {
            // Signal overlay manager — use thread-safe channel, not direct rendering here
            // Return immediately to stay under the 1-second hook timeout
        }
    }
    return PInvoke.CallNextHookEx(new HHOOK(0), nCode, wParam, lParam);
}
```

**Critical delegate lifetime:** The managed delegate passed to `SetWindowsHookEx` must not be garbage collected. Store it in a static field or as an instance field on a long-lived object. Assign to a local variable only is not safe — GC can collect between the call and the first callback.

```csharp
// Safe: keep delegate alive in a field, not a local
private static HOOKPROC _hookProcDelegate = KeyboardHookProc;
// Then pass _hookProcDelegate to SetWindowsHookEx
```

**Shutdown:** Call `PostThreadMessage(hookThreadId, WM_QUIT, 0, 0)` from the main thread to exit the message loop cleanly. `GetMessage` returns 0 on WM_QUIT, breaking the loop.

---

## Architecture Pattern: Overlay Rendering

Each directional overlay is a separate topmost layered window positioned exactly over the target window's DwmGetWindowAttribute bounds. Render colored borders using a 32-bit ARGB DIB section.

**Border drawing approach — direct pixel writes into DIB buffer:**

```csharp
// Create 32-bit ARGB bitmap matching target window size
BITMAPINFO bmi = new();
bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
bmi.bmiHeader.biWidth = width;
bmi.bmiHeader.biHeight = -height;  // top-down
bmi.bmiHeader.biPlanes = 1;
bmi.bmiHeader.biBitCount = 32;
bmi.bmiHeader.biCompression = (uint)BI_COMPRESSION.BI_RGB;

var hBitmap = PInvoke.CreateDIBSection(hdc, bmi, DIB_USAGE.DIB_RGB_COLORS, out var bits, null, 0);

// Fill border bands directly in pixel buffer (premultiplied ARGB)
// Example: top border, borderThickness rows
uint color = PremultiplyAlpha(r, g, b, alpha);
for (int y = 0; y < borderThickness; y++)
    for (int x = 0; x < width; x++)
        ((uint*)bits)[y * width + x] = color;
// Repeat for bottom, left, right bands; leave interior = 0 (fully transparent)

// Composite onto screen via UpdateLayeredWindow
var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_ALPHA
PInvoke.UpdateLayeredWindow(overlayHwnd, screenDC, &dstPoint, &size, memDC, &srcPoint, 0, &blend, 2); // ULW_ALPHA
```

**Premultiplied alpha is required by UpdateLayeredWindow.** Pixels that are `(r, g, b, a)` must be stored as `(r*a/255, g*a/255, b*a/255, a)`.

**Interior pixels must be zero (fully transparent).** A zero ARGB value (0x00000000) in the DIB = completely invisible. This achieves the colored border + transparent interior effect without any window region clipping.

---

## CsWin32 Integration Notes

### NativeMethods.txt Additions (append to existing file)

```
SetWindowsHookEx
UnhookWindowsHookEx
CallNextHookEx
GetMessage
TranslateMessage
DispatchMessage
PostThreadMessage
RegisterClassEx
CreateWindowEx
DestroyWindow
SetWindowPos
ShowWindow
GetDC
ReleaseDC
CreateCompatibleDC
DeleteDC
CreateDIBSection
SelectObject
DeleteObject
UpdateLayeredWindow
DefWindowProc
GetModuleHandle
PostQuitMessage
```

### Known CsWin32 Friction Points

**SetWindowsHookEx hmod parameter:** For WH_KEYBOARD_LL with in-process callbacks and `dwThreadId = 0`, `hmod` must technically be non-null per MSDN (an error "may occur" when null). In practice, most implementations pass null successfully. The correct safe approach for CsWin32:

```csharp
// Option A: Use null (works in practice for WH_KEYBOARD_LL)
new HINSTANCE(0)

// Option B: Use current module handle (fully correct per MSDN)
var hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(DaemonRunner).Module));
```

Use Option B (MEDIUM confidence Option A works; HIGH confidence Option B is correct).

**RegisterClassEx HINSTANCE:** Must use `Marshal.GetHINSTANCE(typeof(YourType).Module)` — passing `new HINSTANCE(0)` to RegisterClassEx causes error 87 (The parameter is incorrect). This is a known friction point documented in CsWin32 Discussion #750.

**HOOKPROC delegate type:** CsWin32 generates a `HOOKPROC` delegate type. The managed callback signature must match. Keep a static reference to avoid GC collection.

**GetMessage null HWND:** Pass `new HWND(0)` not `HWND.Null` to receive all messages on the thread (including WM_QUIT from PostThreadMessage).

---

## Recommended Stack (Complete — v1.0 + v2.0)

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET 8 LTS | 8.0 | Runtime | Validated. No change. .NET 10 upgrade is optional — no new daemon/overlay APIs require it. |
| C# 12 | Ships with .NET 8 | Language | Validated. No change. |
| `System.CommandLine` | 2.0.3 | CLI parsing | Validated. `focus daemon` is a new subcommand — System.CommandLine handles it naturally. |
| `Microsoft.Windows.CsWin32` | 0.3.269 | Win32 P/Invoke source generation | Validated. Add new API names to NativeMethods.txt. No version change needed — 0.3.269 (Jan 2026) supports all required APIs. |
| `System.Text.Json` | Built into .NET 8 | Config (colors, border thickness) | Validated. Per-direction color config entries extend the existing JSON config POCO. |
| Win32 GDI + layered windows | — (User32.dll, Gdi32.dll) | Overlay rendering | New for v2.0. Accessed via CsWin32-generated P/Invoke. No third-party library. |
| Win32 `WH_KEYBOARD_LL` | — (User32.dll) | Global keyboard hook | New for v2.0. Accessed via CsWin32-generated P/Invoke. Requires dedicated thread with message pump. |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| None new | — | — | All new v2.0 capabilities come from Win32 system DLLs. CsWin32 generates the bindings at build time. |

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| Raw Win32 layered windows via CsWin32 | WPF / WinUI 3 transparent window | Use WPF/WinUI if you need rich XAML rendering, animations, or data binding on overlays. For simple colored border rectangles with no interactivity, raw Win32 is 10x less code and avoids 40-80 MB runtime dependency. |
| Raw Win32 layered windows via CsWin32 | WinForms transparent form | WinForms is simpler than WPF but adds the WinForms runtime (included in .NET desktop workload). Still overkill for colored border overlays. Forces `Application.Run()` message loop which conflicts with the existing CLI architecture. |
| `WH_KEYBOARD_LL` hook | Raw Input (`RegisterRawInputDevices`) | Raw input is preferred by Microsoft for high-performance scenarios (gaming, accessibility) but is more complex and delivers keyboard events differently (WM_INPUT, not WH_KEYBOARD_LL). For a daemon that only needs CAPSLOCK down/up edge detection, WH_KEYBOARD_LL is simpler and sufficient. |
| `WH_KEYBOARD_LL` hook | AutoHotkey managing daemon state | AHK could detect CAPSLOCK and call `focus daemon --show` / `focus daemon --hide`. Simpler to implement but requires AHK script changes and adds IPC complexity. A self-contained daemon with its own hook is cleaner. |
| `UpdateLayeredWindow` + DIB | `SetLayeredWindowAttributes` (color key) | Color key transparency is simpler but creates a visible glitch if any window content shares the key color. Per-pixel alpha via UpdateLayeredWindow is correct and has no color collision risk. |
| Dedicated hook thread with `GetMessage` | `Application.Run()` (WinForms message loop) | `Application.Run()` would work but requires adding the WinForms package reference to a previously WinForms-free CLI tool. A manual GetMessage loop achieves the same result with zero additional dependencies. |

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| WPF (`WindowsBase`, `PresentationCore`, `PresentationFramework`) | Adds ~40-80 MB runtime, requires `<UseWPF>true</UseWPF>` in csproj, and changes the application model from CLI to GUI. Complete mismatch with a CLI tool invoked per hotkey. | Raw Win32 layered windows via CsWin32 |
| WinForms (`System.Windows.Forms`) | Forces `Application.Run()` message loop model, adds ~10 MB of framework, and conflicts with the existing stateless CLI invocation model. `Application.Run()` blocks the main thread permanently. | Manual `GetMessage` loop on dedicated thread |
| WinUI 3 / Windows App SDK | Requires MSIX packaging or sparse manifests, adds significant deployment complexity. Complete overkill for four colored rectangles. | Raw Win32 layered windows via CsWin32 |
| GDI+ (`System.Drawing`) | GDI+ is deprecated on non-Windows platforms and adds an interop layer over GDI with no benefit for simple filled rectangles. Direct pixel writes into the DIB section are faster and require no extra package. | Direct 32-bit pixel buffer writes into `CreateDIBSection` buffer |
| `Microsoft.Extensions.Hosting` (Generic Host) | The daemon is a foreground console process with a message pump thread, not a Windows Service. Generic Host adds DI container startup overhead (~50-100 ms, 5-15 MB) for no benefit. | Direct field/constructor wiring between DaemonRunner, KeyboardHook, and OverlayManager |
| `System.Windows.Forms.NotifyIcon` | Project.md explicitly excludes system tray. | — |
| Named pipes / sockets for IPC | The daemon process handles everything itself — hook + overlays. AHK calls `focus <direction>` for navigation (stateless, separate process). No IPC is needed between them. | Direct daemon process manages its own state |
| Thread.Sleep polling loops | Polling CAPSLOCK state with `GetKeyState` in a loop is unreliable (missed edges) and burns CPU. | Event-driven WH_KEYBOARD_LL hook with zero polling |

---

## Stack Patterns by Variant

**If targeting .NET 8 (current, validated):**
- All v2.0 features work as described. No changes to csproj.
- `net8.0` target framework, CsWin32 0.3.269, all Win32 APIs available.

**If upgrading to .NET 10 (optional):**
- Change `<TargetFramework>` to `net10.0`.
- CsWin32 0.3.269 supports .NET Standard 1.0+, compatible with any .NET version.
- No daemon/overlay API differences between .NET 8 and .NET 10 for Win32 P/Invoke.
- Potential benefit: .NET 10 performance improvements to Marshal and P/Invoke infrastructure.
- Risk: System.CommandLine 2.0.3 compatibility with .NET 10 should be verified.

**If adding more than 4 overlay directions (e.g., diagonal):**
- Pattern scales — one overlay window per direction, all using same CreateWindowEx + UpdateLayeredWindow approach.
- Performance: each layered window is tiny (covers only the target window). 4-8 windows is negligible.

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| `Microsoft.Windows.CsWin32 0.3.269` | .NET 8, .NET 10, C# 12/13 | All v2.0 Win32 APIs (SetWindowsHookEx, UpdateLayeredWindow, CreateDIBSection, RegisterClassEx) are in Win32Metadata. Add to NativeMethods.txt — no version upgrade needed. |
| Win32 `WH_KEYBOARD_LL` | Windows 2000+ | Available on all supported Windows versions. 1-second hook timeout enforced since Windows 10 1709 — callback must return fast. |
| Win32 layered windows (`WS_EX_LAYERED`) | Windows 2000+ for top-level; Windows 8+ for child windows | All overlays are top-level — no child window restriction applies. |
| Win32 `UpdateLayeredWindow` | Windows 2000+ (Windows 8.1+ API set) | Available on all relevant Windows 10/11 versions. |
| `System.CommandLine 2.0.3` | .NET 8, .NET 10 | Validated for .NET 8. .NET 10 compatibility: the package targets netstandard2.0 so it is compatible, but verify against .NET 10 RTM if upgrading. |

---

## Sources

- [SetWindowsHookExA — Microsoft Learn (Win32 API docs)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexa) — WH_KEYBOARD_LL value (13), hmod null rules, global-only scope, architecture matching requirements — HIGH confidence
- [KBDLLHOOKSTRUCT — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) — vkCode, flags bit layout (bit 7 = LLKHF_UP for key release) — HIGH confidence
- [UpdateLayeredWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) — ULW_ALPHA, BLENDFUNCTION, premultiplied alpha requirement — HIGH confidence
- [SetLayeredWindowAttributes — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes) — WS_EX_LAYERED creation requirement — HIGH confidence
- [CsWin32 Issue #245 — SetWindowsHookEx difficulty](https://github.com/microsoft/CsWin32/issues/245) — message loop requirement confirmed by maintainer, closed as completed — MEDIUM confidence (issue, not official docs)
- [CsWin32 Discussion #248 — SetWindowsHookEx](https://github.com/microsoft/CsWin32/discussions/248) — message pump architecture guidance, delegate lifetime — MEDIUM confidence (community discussion)
- [CsWin32 Discussion #750 — RegisterClassEx error 87](https://github.com/microsoft/CsWin32/discussions/750) — HINSTANCE must use Marshal.GetHINSTANCE, not zero — MEDIUM confidence (community confirmed fix)
- [Microsoft.Windows.CsWin32 on NuGet](https://www.nuget.org/packages/Microsoft.Windows.CsWin32) — version 0.3.269 is current stable (Jan 16 2026) — HIGH confidence
- [LowLevelKeyboardProc callback — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — callback invocation model, nCode values — HIGH confidence
- [Extended Window Styles — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) — WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, WS_EX_LAYERED, WS_EX_TRANSPARENT definitions — HIGH confidence

---
*Stack research for: Window focus navigation v2.0 — daemon mode with keyboard hook and transparent overlay rendering*
*Researched: 2026-02-28*
