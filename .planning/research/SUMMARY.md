# Project Research Summary

**Project:** Window Focus Navigation — v2.0 Overlay Preview Daemon
**Domain:** Win32 background daemon with global keyboard hook and transparent overlay rendering (.NET/C#)
**Researched:** 2026-02-28
**Confidence:** HIGH

## Executive Summary

This project extends an existing, validated v1.0 stateless CLI tool (directional window focus navigation via keyboard shortcuts) into a persistent v2.0 daemon that renders visual overlay previews when the user holds CAPSLOCK. The technical approach is well-understood and builds entirely on existing patterns: Win32 P/Invoke via CsWin32, no new NuGet dependencies, and direct reuse of the v1.0 `WindowEnumerator`, `NavigationService`, and `FocusConfig` components. The new work is additive — two new namespaces (`Daemon/` and `Overlay/`) that slot alongside the unchanged `Windows/` layer. Two files are modified: `Program.cs` gains a `daemon` subcommand, and `FocusConfig` gains overlay-specific config fields with defaults.

The recommended implementation strategy is strict separation of concerns across three subsystems: a dedicated STA thread running a Win32 message loop (the only correct way to host `WH_KEYBOARD_LL`), a lightweight keyboard hook handler that does nothing in the callback except enqueue events and chain the hook, and an overlay manager that executes all window enumeration and rendering work on a worker thread. This three-part separation is not optional — it is the mandatory architecture imposed by Win32's `LowLevelHooksTimeout` constraint (default 300ms, hard cap 1000ms on Windows 10 1709+), which silently removes hooks that block too long. The failure mode — hook vanishes with no log, no error code — is invisible and catastrophic.

Key risks cluster around three areas: GC-related hook delegate lifetime (static field required, not local variable), layered window rendering mode exclusivity (`UpdateLayeredWindow` and `SetLayeredWindowAttributes` cannot be mixed on the same HWND), and overlay window exclusion from the navigation candidate set (mandatory `WS_EX_TOOLWINDOW` on all overlay HWNDs). All three are LOW-recovery-cost mistakes if caught early and HIGH-confusion mistakes if caught late. Research confidence is HIGH across all four domains — the Win32 APIs involved have been stable since Windows 2000 and are thoroughly documented by official Microsoft sources.

## Key Findings

### Recommended Stack

The v2.0 stack requires no new NuGet packages. All new capability comes from Win32 APIs already accessible via the project's existing CsWin32 0.3.269 setup — specifically `WH_KEYBOARD_LL` (User32.dll) for the keyboard hook and the layered window API set (`CreateWindowEx`, `UpdateLayeredWindow`, `CreateDIBSection`, etc.) for transparent overlays. The only required change is appending approximately 20 API names to `NativeMethods.txt` so CsWin32 generates their bindings at build time. The existing `.NET 8` target, `System.CommandLine 2.0.3`, and `System.Text.Json` are all unchanged.

**Core technologies (new for v2.0):**
- `WH_KEYBOARD_LL` via CsWin32: global low-level keyboard hook — the only Win32 mechanism for system-wide key interception without a foreground window; requires a dedicated STA thread running `GetMessage`/`DispatchMessage`
- `WS_EX_LAYERED` + `UpdateLayeredWindow` + `CreateDIBSection` via CsWin32: per-pixel-alpha transparent overlay windows — draw directly into a 32-bit ARGB pixel buffer with premultiplied alpha; no third-party rendering library
- Win32 message loop (`GetMessage`/`DispatchMessage`/`PostThreadMessage`): explicit pump on the hook thread; not `Application.Run()`, not `Thread.Sleep`, not WinForms or WPF

**What NOT to add:** WPF, WinForms, WinUI 3, GDI+, `Microsoft.Extensions.Hosting`, Named Pipes, `Thread.Sleep` polling, `SetLayeredWindowAttributes`+`UpdateLayeredWindow` mixed on the same window. All are incompatible with the existing CLI-first no-GUI-framework constraint.

**Critical CsWin32 friction point:** `RegisterClassEx` requires `Marshal.GetHINSTANCE(typeof(YourType).Module)` as the HINSTANCE — passing `new HINSTANCE(0)` causes error 87 (parameter incorrect). Confirmed in CsWin32 Discussion #750.

### Expected Features

The v2.0 MVP has one non-negotiable goal: colored border overlays appear on the top-ranked directional candidate windows while CAPSLOCK is held, and disappear when CAPSLOCK is released. All table-stakes features flow from this, and they are fully enumerated and dependency-mapped in FEATURES.md.

**Must have (table stakes — v2.0 launch):**
- Daemon process with dedicated message loop and `WH_KEYBOARD_LL` hook — nothing else works without this
- CAPSLOCK hold detection with auto-repeat suppression (first WM_KEYDOWN only; ignore repeats until WM_KEYUP)
- 4 simultaneous overlay windows (one per direction), each positioned on the top-ranked candidate from `NavigationService`
- Colored border rendering via GDI DIB section (4 border strips; transparent interior at 0x00000000)
- Per-direction configurable colors in JSON config (additive `FocusConfig` extension, backwards-compatible defaults)
- Overlay dismissal on CAPSLOCK release
- Overlay exclusion from navigation candidates (`WS_EX_TOOLWINDOW` mandatory; no `WS_EX_APPWINDOW`)
- Click-through behavior (`WS_EX_TRANSPARENT`) and no focus theft (`WS_EX_NOACTIVATE`)
- No taskbar or Alt+Tab entry (`WS_EX_TOOLWINDOW`)
- Clean daemon shutdown (destroy overlays, unhook, exit on Ctrl+C/SIGTERM)
- Single-instance guard (named mutex: `Global\FocusDaemon-<GUID>`)
- `LLKHF_INJECTED` filtering to ignore AHK-synthesized key events
- Configurable activation delay/debounce (~150ms default, prevents accidental triggers)
- `IOverlayRenderer` interface with `DefaultBorderRenderer` — enables per-strategy renderers in v2.x without interface changes
- Foreground window change detection — update overlay while CAPSLOCK held if foreground switches

**Should have (add in v2.x):**
- Per-strategy custom renderers (edge-matching visualizer, axis-only visualizer)
- Configurable border thickness (default 4-6px is sufficient for v2.0)
- Live overlay update on window layout change (new windows opened/closed while held)

**Defer (v3+):**
- DPI-aware border thickness scaling for mixed-DPI multi-monitor setups
- Edge-matching and axis-only strategy-specific visual renderers

**Confirmed anti-features (do not build):** animated transitions, window content thumbnails, system tray icon/context menu, auto-start management, interactive overlay elements (click to focus), WPF/WinUI rendering layer.

### Architecture Approach

The architecture is a clean extension of the existing layered CLI. `Program.cs` gains a `daemon` subcommand that calls `DaemonHost.Run()` instead of the stateless pipeline. Two new folders (`Daemon/` and `Overlay/`) contain all new code; the `Windows/` folder is untouched except for additive fields on `FocusConfig`. All v1.0 components — `WindowEnumerator`, `NavigationService`, `ExcludeFilter`, `FocusActivator`, `MonitorHelper`, `WindowInfo`, `Direction` — are used unchanged by the daemon path.

**Major components (new for v2.0):**
1. `DaemonHost` — owns the STA hook thread, message loop lifecycle, and process shutdown (Ctrl+C handler calls `PostThreadMessage(hookThreadId, WM_QUIT)`; unhook runs on hook thread after GetMessage returns 0)
2. `KeyboardHookHandler` — decodes `KBDLLHOOKSTRUCT`; filters `LLKHF_INJECTED` events; tracks held state with a boolean to suppress auto-repeat; enqueues `KeyEvent` to `Channel<KeyEvent>`; returns from callback in microseconds; always calls `CallNextHookEx`
3. `OverlayManager` — worker thread consumes from `Channel<KeyEvent>`; on CapsLockDown calls `WindowEnumerator` + `ExcludeFilter` + `NavigationService.GetRankedCandidates` for all 4 directions; positions/shows pre-created `OverlayWindow` instances; on CapsLockUp hides them
4. `OverlayWindow` — owns one HWND created at daemon startup with style `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`; manages a reusable GDI DIB section; delegates painting to `IOverlayRenderer`; exposes `Position()`, `Show()`, `Hide()`
5. `DefaultBorderRenderer` — fills 4 border strips in the DIB pixel buffer with premultiplied ARGB values; interior pixels set to 0x00000000 (fully transparent); calls `UpdateLayeredWindow` with `ULW_ALPHA`

**Critical pattern:** Pre-create all 4 overlay HWNDs at daemon startup and reuse them across CAPSLOCK cycles via `ShowWindow`/`SetWindowPos`, not create/destroy on each press. `CreateWindowEx` + DIB setup takes tens of milliseconds — perceptible latency on every keypress if done cold.

### Critical Pitfalls

Pitfalls are fully catalogued in PITFALLS.md. The seven that must be addressed before any testing begins:

1. **Hook delegate GC collection** — store the `HOOKPROC` delegate in a `static` field; a local variable becomes GC-eligible after the installing method returns and the hook silently stops working after 30-90 seconds with no error
2. **No message loop on hook thread** — `WH_KEYBOARD_LL` callbacks are delivered as Win32 messages to the installing thread's queue; `GetMessage`/`DispatchMessage` is required; `Console.ReadLine`, `Thread.Sleep`, or `CancellationToken.WaitHandle` are NOT sufficient
3. **Hook callback timeout** — Windows silently uninstalls the hook after ~10 consecutive timeouts (default 300ms limit); do zero substantial work in the callback; enqueue to `Channel<KeyEvent>` and return; there is no API to detect silent removal
4. **Layered window API mode exclusivity** — `SetLayeredWindowAttributes` and `UpdateLayeredWindow` cannot be mixed on the same HWND; choose `UpdateLayeredWindow` + premultiplied-alpha DIB at window creation and never call the other; mixing produces silent failures or wrong rendering
5. **Overlay focus theft** — overlays steal focus from the active application unless `WS_EX_NOACTIVATE` is applied at creation and `SWP_NOACTIVATE` is passed to every `SetWindowPos` call; use `ShowWindow(SW_SHOWNOACTIVATE)`, not `SW_SHOW`
6. **Multiple daemon instances** — two instances install two hooks and show double-borders; a named mutex (`Global\FocusDaemon-<GUID>`) at daemon entry is mandatory; keep the `Mutex` object in a static field (GC can collect a `using`-scoped `Mutex` while the daemon is running)
7. **AHK injected keystrokes** — `SendInput` from AHK hotkey actions and from `focus.exe` itself (ALT key for `SetForegroundWindow`) both set `LLKHF_INJECTED` (bit 4) in `KBDLLHOOKSTRUCT.flags`; filter this flag in the callback to prevent overlay flickering from synthetic events

## Implications for Roadmap

Research reveals a strict dependency ordering: daemon infrastructure must exist and be validated before overlay code can be tested, and overlay window creation must be validated in isolation before the end-to-end CAPSLOCK-to-overlay flow is assembled. The build order in ARCHITECTURE.md maps cleanly to 4 phases.

### Phase 1: Daemon Infrastructure and Keyboard Hook
**Rationale:** Everything depends on a working message loop and keyboard hook. This is the highest-risk phase — Win32 message loop threading in a .NET console app is unfamiliar territory, and the failure modes (hook silently removed, callback never fires) are invisible. Validate the hook fires and is cleaned up correctly before adding any overlay code.
**Delivers:** `focus daemon` subcommand that starts, installs the hook, logs CAPSLOCK press/release to console (debug output), and shuts down cleanly on Ctrl+C. Zero overlay code. Passes "looks-done-but-isn't" checklist: hook still fires after 2+ minutes idle, hook fires under fullscreen-app focus, callback returns in under 50ms.
**Addresses features:** Daemon process with message loop, CAPSLOCK hold detection (with auto-repeat suppression), injected-key filtering, single-instance mutex, console window suppression (`FreeConsole()` at daemon entry)
**Avoids pitfalls:** A-1 (GC delegate — static field), A-2 (no message loop — dedicated STA thread), A-3 (callback timeout — Channel pattern from day one), A-7 (CAPSLOCK toggle pass-through — do not suppress), A-8 (AHK injected keys — LLKHF_INJECTED filter), A-11 (console window), A-12 (multiple instances)
**Research flag:** CsWin32-specific HOOKPROC delegate interop and hmod parameter handling have community but not official documentation. Recommend a 30-minute spike to validate hook fires on first keypress before building KeyboardHookHandler fully.

### Phase 2: Overlay Window Creation
**Rationale:** Before wiring overlays to real navigation data, validate that a layered window appears correctly — transparent, click-through, no focus theft, excluded from Alt+Tab, aligned correctly on multi-DPI monitors. These are independent of the scoring logic and must be verified in isolation.
**Delivers:** A single hardcoded overlay HWND positioned at a fixed screen rectangle with a visible colored border. Manual validation: show overlay while typing in Notepad (no interruption), click through overlay to window beneath, verify absent from Alt+Tab, verify aligned on 150% DPI secondary monitor.
**Uses:** `CreateWindowEx` + `UpdateLayeredWindow` + `CreateDIBSection` via CsWin32; `IOverlayRenderer` + `DefaultBorderRenderer`; full required style combination
**Avoids pitfalls:** A-4 (focus theft — WS_EX_NOACTIVATE + SWP_NOACTIVATE), A-5 (toolwindow exclusion — WS_EX_TOOLWINDOW mandatory), A-6 (DPI mismatch — verify on unequal-DPI multi-monitor), A-9 (layered mode exclusivity — choose UpdateLayeredWindow exclusively), A-10 (Z-order re-assertion on each show)
**Research flag:** Premultiplied alpha requirement for `UpdateLayeredWindow` is subtle; incorrect values produce wrong compositing that is not immediately obvious. A rendering spike against a known ARGB value is recommended.

### Phase 3: Overlay-Navigation Integration
**Rationale:** Wire `OverlayManager` to call `NavigationService.GetRankedCandidates` for all 4 directions and position the pre-created overlay HWNDs on the real top-ranked candidates. This reuses all v1.0 logic unchanged — the only new code is the coordination in `OverlayManager`.
**Delivers:** Full CAPSLOCK-hold → colored borders on directional candidates → CAPSLOCK-release → borders hide. Per-direction color config in JSON. Foreground window change detection (polling at ~100ms or `SetWinEventHook`). Passes: `focus --debug enumerate` shows zero rows with daemon process name while daemon is running.
**Implements:** `OverlayManager`, `FocusConfig` extension with overlay fields and defaults, foreground window change detection
**Avoids pitfalls:** A-5/A-13 (overlay in own candidates — verify with enumerate debug command), A-3 (all enumeration/scoring runs on worker thread consuming from Channel, not in hook callback)

### Phase 4: Robustness and End-to-End Validation
**Rationale:** End-to-end works but edge cases and UX details need attention before the daemon is daily-driver ready. No new APIs required — this phase is entirely about correctness under real conditions.
**Delivers:** Activation delay/debounce (~150ms), graceful handling of "no candidate in a direction" (overlay absent for that direction, no crash), complete "looks-done-but-isn't" checklist verification, AHK launch integration (`Run, focus.exe daemon,, Hide`)
**Validates:** GC delegate lifetime (2+ minute idle test), hook callback timing (debug instrumentation, must stay under 50ms), DPI overlay alignment on unequal-DPI multi-monitor setup, CAPSLOCK pass-through for typing (LED and uppercase behavior unaffected), single-instance mutex under AHK restart, kill-and-restart leaves no stuck hook remnants

### Phase Ordering Rationale

- Phase 1 before Phase 2: the hook infrastructure must exist and be validated for overlay integration to be testable in any meaningful context
- Phase 2 before Phase 3: overlay rendering correctness (focus theft, DPI, transparency, mode exclusivity) is much easier to debug in isolation than entangled with real navigation data
- Phase 3 before Phase 4: the core feature must be functionally complete before robustness hardening
- `IOverlayRenderer` and `DefaultBorderRenderer` introduced in Phase 2 so Phase 3 and beyond can add per-strategy renderers without interface changes — the interface establishes a seam that costs nothing now and prevents breaking changes later

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 1:** WH_KEYBOARD_LL + .NET console app threading has CsWin32-specific friction (HOOKPROC type, hmod parameter correctness, `new HWND(0)` vs `HWND.Null` in GetMessage). Validate hook fires before building higher-level abstractions.
- **Phase 2:** `RegisterClassEx` HINSTANCE is a documented CsWin32 friction point (Discussion #750). Premultiplied alpha compositing failure is subtle. A rendering spike recommended before full `OverlayWindow` implementation.

Phases with standard patterns (skip research-phase):
- **Phase 3:** `NavigationService.GetRankedCandidates` is proven and unchanged. `FocusConfig` JSON extension is additive. `OverlayManager` coordination is straightforward given working Phase 1 and Phase 2 primitives.
- **Phase 4:** No new APIs — tactical robustness work only.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All APIs are official Win32; CsWin32 0.3.269 is current stable (Jan 2026); no new NuGet dependencies required; MEDIUM confidence on two specific CsWin32 friction points (HOOKPROC delegate, RegisterClassEx HINSTANCE) addressed explicitly in research |
| Features | HIGH | Feature boundaries are clear and well-motivated; competitor analysis (PowerToys Shortcut Guide, Komorebi, FancyZones) confirms table-stakes set; anti-features are well-reasoned; dependency graph is explicit and complete |
| Architecture | HIGH | Component breakdown is specific with pseudocode for all critical patterns; build order is dependency-driven; Win32 threading constraints are officially documented; all integration points between new and existing components are named |
| Pitfalls | HIGH | 13 v2.0-specific pitfalls with concrete code fixes and phase assignments; 5 v1.0 pitfalls confirmed already mitigated in existing code; failure modes categorized by detection difficulty; "looks done but isn't" checklist provided |

**Overall confidence:** HIGH

### Gaps to Address

- **CsWin32 HOOKPROC delegate interop specifics:** Research rates this MEDIUM — hmod parameter behavior and delegate type details have community but not official documentation. Resolve with a focused spike (install hook, fire callback, verify on first keypress) at the start of Phase 1 before building anything else.
- **`UpdateLayeredWindow` vs `SetLayeredWindowAttributes` rendering approach:** PITFALLS.md notes `SetLayeredWindowAttributes` + color key is simpler and lower-risk for an initial implementation; STACK.md and ARCHITECTURE.md recommend `UpdateLayeredWindow` + DIB for per-pixel alpha correctness. Recommendation: choose `UpdateLayeredWindow` at Phase 2 start and commit — starting with the correct approach avoids a later migration that would require destroying and recreating overlay HWNDs.
- **Foreground window change detection method:** Two options mentioned across research (polling at ~100ms vs. `SetWinEventHook` EVENT_SYSTEM_FOREGROUND). Polling is simpler for Phase 3; `SetWinEventHook` is more efficient for long-term. Decide at Phase 3 planning based on observed CPU impact of the polling approach.
- **.NET 10 upgrade:** Currently targeting `net8.0`. Stack research confirms all v2.0 APIs work on both .NET 8 and .NET 10. If upgrading, verify `System.CommandLine 2.0.3` compatibility with .NET 10 RTM before committing.

## Sources

### Primary (HIGH confidence)
- [SetWindowsHookExA — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexa) — WH_KEYBOARD_LL constraints, hmod rules, global-only scope
- [LowLevelKeyboardProc — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — callback invocation model, nCode values, timeout behavior
- [KBDLLHOOKSTRUCT — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) — vkCode, flags bit layout (LLKHF_UP bit 7, LLKHF_INJECTED bit 4)
- [UpdateLayeredWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) — ULW_ALPHA, BLENDFUNCTION, premultiplied alpha requirement
- [SetLayeredWindowAttributes — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes) — WS_EX_LAYERED creation, mode exclusivity with UpdateLayeredWindow
- [Window Features (Layered Windows) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) — layered window rendering modes and constraints
- [Extended Window Styles — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) — WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, WS_EX_LAYERED, WS_EX_TRANSPARENT, WS_EX_TOPMOST
- [Hooks Overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks) — hook types, thread message loop requirements, hook chain behavior
- [Using Messages and Message Queues — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/using-messages-and-message-queues) — GetMessage/DispatchMessage loop structure
- [PowerToys Shortcut Guide — Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/shortcut-guide) — hold-modifier overlay pattern precedent
- [Komorebi Borders docs](https://lgug2z.github.io/komorebi/common-workflows/borders.html) — colored border per-state pattern
- [Microsoft.Windows.CsWin32 on NuGet](https://www.nuget.org/packages/Microsoft.Windows.CsWin32) — version 0.3.269 current stable (Jan 16, 2026)

### Secondary (MEDIUM confidence)
- [CsWin32 Issue #245 — SetWindowsHookEx difficulty](https://github.com/microsoft/CsWin32/issues/245) — message loop requirement confirmed by maintainer
- [CsWin32 Discussion #248 — SetWindowsHookEx](https://github.com/microsoft/CsWin32/discussions/248) — message pump architecture, delegate lifetime guidance
- [CsWin32 Discussion #750 — RegisterClassEx error 87](https://github.com/microsoft/CsWin32/discussions/750) — HINSTANCE must use Marshal.GetHINSTANCE, not zero (community-confirmed fix)
- [FancyZones — Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones) — hold-Shift zone preview pattern

---
*Research completed: 2026-02-28*
*Ready for roadmap: yes*
