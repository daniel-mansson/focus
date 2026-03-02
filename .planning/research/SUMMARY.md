# Project Research Summary

**Project:** focus — Windows keyboard window navigation daemon (v3.1: grid-snapped move/resize)
**Domain:** Win32 daemon — keyboard hook, overlay rendering, window move/resize
**Researched:** 2026-03-02
**Confidence:** HIGH

## Executive Summary

The v3.1 milestone adds keyboard-driven, grid-snapped window move and resize to an already-shipped daemon (v3.0). The existing stack — .NET 8, CsWin32 P/Invoke source generator, WinForms message pump, WH_KEYBOARD_LL hook, GDI layered overlays — is the correct and sufficient foundation. Zero new NuGet packages are needed. The only additions are four new Win32 API entries in NativeMethods.txt (`GetWindowRect`, `GetDpiForWindow`, `GetDpiForMonitor`, `IsZoomed`) and a new static service class (`WindowManagerService`). The core implementation challenge is not "what technology" but "what coordinate space" — there are two distinct rect types in play, and mixing them silently produces a window that shrinks by approximately 8px on every move.

The recommended approach is a two-phase implementation: Phase 1 establishes the move/resize mechanics with all critical guards (maximized window detection, coordinate space separation, UIPI handling, correct modifier tracking), and Phase 2 layers in cross-monitor support and overlay mode integration. Overlay mode indicators can be added with the existing `IOverlayRenderer` interface by adding an optional `OverlayMode` enum parameter — a backwards-compatible change that does not force rewrites of existing renderers. Non-modal modifier combos (CAPS+TAB+dir = move, CAPS+LSHIFT+dir = grow, CAPS+LCTRL+dir = shrink) are the correct design; modal resize-mode state would conflict with the existing navigation bindings.

The highest-risk area is the coordinate system: `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` excludes the invisible DWM resize border (~7-8px per side on Windows 10/11) and is correct for overlay positioning and navigation scoring but must never be passed to `SetWindowPos`. `GetWindowRect` includes those borders and is the mandatory source for `SetWindowPos` inputs. Every move and resize operation must establish both rects, compute the border offsets once, and maintain the distinction throughout. The second major risk is UIPI: the daemon runs at medium integrity and will silently fail to move elevated windows (Task Manager, admin processes). Return-value checking on every `SetWindowPos` call is non-negotiable.

## Key Findings

### Recommended Stack

The existing stack is unchanged and fully sufficient. All new Win32 APIs are standard Win32 system DLLs accessible through the existing CsWin32 setup; the only work is adding four entries to `NativeMethods.txt`. The app.manifest already declares `PerMonitorV2, PerMonitor` DPI awareness, which means all Win32 coordinates are physical pixels — this simplifies grid math because no DPI conversion is needed. Grid step should be expressed as a fraction of the monitor's physical dimensions (default: 1/16th), not in DPI-scaled logical units.

`SetWindowPos` with `SWP_NOZORDER | SWP_NOACTIVATE` is the single correct API for both move and resize. `MoveWindow` is a weaker alternative and should not be used. All `SetWindowPos` calls run on the STA thread via the existing `_staDispatcher.Invoke()` pattern — no threading model changes are needed.

**Core technologies:**
- `.NET 8 / net8.0-windows`: runtime — unchanged, no update required
- `CsWin32 0.3.269`: P/Invoke source generator — add 4 new entries to NativeMethods.txt only
- `WinForms Application.Run` (STA message pump): hook thread — unchanged
- `SetWindowPos (User32.dll)`: move and resize — already in NativeMethods.txt
- `GetWindowRect (User32.dll)`: read current frame rect for SetWindowPos inputs — new NativeMethods.txt entry; round-trip safe with SetWindowPos
- `DwmGetWindowAttribute / DWMWA_EXTENDED_FRAME_BOUNDS`: visible bounds for overlay and grid snap baseline — already in codebase; must NOT be passed to SetWindowPos
- `GetDpiForWindow (User32.dll)`: per-monitor DPI query — new entry, Windows 10 1607+; preferred over GetDpiForMonitor when querying by window
- `GetDpiForMonitor (Shcore.dll)`: DPI for destination monitor during cross-monitor move — new entry, Windows 8.1+
- `IsZoomed (User32.dll)`: maximized state guard — new entry; universal Win32

### Expected Features

The feature set is bounded and well-understood. Every keyboard window manager (i3, Hyprland, dwm, Rectangle) provides the same three primitives (move, grow, shrink) with the same edge-direction semantics. The snap-first-then-step pattern is the most important differentiator from simple tools: on first keypress, align the window to the nearest grid line; on subsequent keypresses, step by one grid cell. Without this, manually-positioned windows accumulate drift on every operation.

**Must have (table stakes):**
- CAPS+TAB+direction: move foreground window by one grid step — users of any keyboard WM expect this as baseline
- CAPS+LSHIFT+direction: grow the edge in that direction outward by one grid step — standard grow semantics
- CAPS+LCTRL+direction: shrink the edge in that direction inward by one grid step — paired with grow
- Grid step = monitor work area / `gridFraction` (default 16), computed per monitor — fraction-of-screen unit works across all DPI/resolution combinations
- Clamp move to monitor work area (hard boundary at screen edge, excluding taskbar)
- Clamp shrink to minimum window size (let SetWindowPos enforce app minimum; add floor of one grid cell)
- Smart snap: snap to nearest grid line on first press if outside tolerance, step on subsequent presses
- Cross-monitor move: when move would push window off current monitor edge, transition to adjacent monitor
- Mode-specific overlay indicators: directional arrows reflecting active mode during CAPS hold
- All operations instant via SetWindowPos with no animation
- JSON config: `gridFraction` (default 16), `gridSnapTolerance` (default 10%)

**Should have (competitive):**
- Per-monitor grid step (reuses existing MonitorFromWindow — effectively free to implement)
- Configurable snap tolerance (`snapTolerancePercent` in JSON config)
- Mode icon renderer showing directional arrows in overlay (extend IOverlayRenderer with optional OverlayMode parameter)

**Defer (v4+):**
- Per-monitor `gridFraction` override in config (low demand, adds config complexity)
- Snap-to-edge on hold vs. tap variant
- `focus snap-all` CLI command (single-shot grid alignment, no persistent state)
- Window position memory/restore per app (tiling manager territory; out of scope)
- Animated window movement (validated as worse UX than instant in v2.0 testing; do not add)
- Pixel-exact move without grid (defeats the grid discipline value proposition)

### Architecture Approach

The v3.0 codebase is well-structured for this extension. The critical integration points are: (1) `KeyboardHookHandler` gains TAB interception and a `_tabHeld` state flag; (2) `KeyEvent` gains a `TabHeld` field (non-breaking, default false); (3) `CapsLockMonitor.HandleDirectionKeyEvent` gains modifier-aware routing to a new `_onModifiedDirectionKeyDown` callback; (4) `OverlayOrchestrator` gains an `OnModifiedDirectionKeyDown` method that dispatches to a new `WindowManagerService` on the STA thread; and (5) a new `WindowManagerService` static class handles the actual move/resize logic. The new `GridCalculator` logic can live as static methods inside `WindowManagerService`.

The `IOverlayRenderer` interface should gain an optional `OverlayMode` enum parameter to support mode-specific rendering without breaking existing `BorderRenderer`. The mode-aware overlay update must avoid the existing `HideAll() + ShowAll()` pattern during move/resize operations — instead, use reposition-in-place to prevent per-keypress flicker.

**Major components:**
1. `KeyboardHookHandler` (modify) — add VK_TAB (0x09) intercept and `_tabHeld` tracking; use VK_LSHIFT (0xA0) and VK_LCONTROL (0xA2) directly for left-side modifier distinction
2. `CapsLockMonitor` (modify) — modifier-aware dispatch via single `_onModifiedDirectionKeyDown(modifier, direction)` callback; existing `_onDirectionKeyDown` path unchanged
3. `OverlayOrchestrator` (modify) — add `OnModifiedDirectionKeyDown`, `_activeMode` field, `_moveOrResizeInProgress` guard for ForegroundMonitor conflict
4. `WindowManagerService` (new, static class) — `MoveWindow`, `GrowWindow`, `ShrinkWindow`; reads both `GetWindowRect` and `DwmGetWindowAttribute` to maintain border offsets; calls `SetWindowPos` with compensated coordinates
5. `GridCalculator` (new, static methods in WindowManagerService) — `GetMonitorWorkArea`, `GetGridStep`, `SnapToGrid`; uses `rcWork` exclusively for all grid boundaries
6. `FocusConfig` (modify) — add `GridDivisions` (int, default 16), `GridSnap` (bool, default true), `GridSnapTolerance` (double, default 0.1)
7. `IOverlayRenderer` / `OverlayManager` (minor modify) — add optional `OverlayMode` enum parameter; implement `ModeIconRenderer` or extend `BorderRenderer` with mode awareness
8. `MonitorHelper` (modify) — add `FindAdjacentMonitor(HMONITOR current, Direction dir)` for cross-monitor moves

### Critical Pitfalls

1. **DWM bounds used as SetWindowPos input — window shrinks ~8px per side per move** — Always use `GetWindowRect` as the source for `SetWindowPos` coordinates. Use `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` only for overlay positioning and grid snap baseline. Compute the border offset (`visibleRect - windowRect`) once per operation and add it back when building the SetWindowPos arguments. This is the most common and most invisible failure mode — the window appears to move correctly but slowly shrinks over repeated presses.

2. **TAB unconditionally suppressed while CAPS held — breaks dialog, browser, and IDE navigation** — Do NOT return `(LRESULT)1` on TAB keydown. Set `_tabHeld = true` and forward the TAB keydown via `CallNextHookEx`. Only suppress direction key events (arrow keys) when `_tabHeld` is true. The suppression policy for TAB must be decided before writing any hook logic — retrofitting it after the fact is error-prone.

3. **VK_SHIFT used instead of VK_LSHIFT for mode selection — right Shift triggers grow mode** — In the WH_KEYBOARD_LL hook, `KBDLLHOOKSTRUCT.vkCode` distinguishes VK_LSHIFT (0xA0) from VK_RSHIFT (0xA1). Use these specific codes. The existing hook uses generic VK_SHIFT — that path must be updated for grow/shrink mode selection.

4. **SetWindowPos called on maximized window without guard — silent no-op, no user feedback** — Check `IsZoomed` before every move/resize call. Either restore-then-move (for move mode) or silently skip (for grow/shrink mode). SetWindowPos returns true on maximized windows but makes no change.

5. **UIPI blocks SetWindowPos on elevated windows — ERROR_ACCESS_DENIED (5) silently swallowed** — Check the return value of every `SetWindowPos` call. Error code 5 (elevated target) and UWP windows (class `ApplicationFrameWindow`) must be detected and silently skipped. Do not log as error or propagate as exception.

6. **Overlay not refreshed after move — shows pre-move window position** — After every `SetWindowPos` call, read back the actual new bounds via `GetWindowRect` and immediately reposition the overlay to the actual post-move rect. Never use the computed intended position — UIPI, min/max constraints, or snap may have changed the actual result.

7. **Grid calculated from `rcMonitor` instead of `rcWork` — windows end up behind taskbar** — Use `GetMonitorInfo.rcWork` (work area excluding taskbar) as the grid origin and boundary for all grid math. Use `rcMonitor` only for monitor identity checks during cross-monitor detection.

## Implications for Roadmap

Based on the combined research, the work naturally splits into two phases with a clear dependency boundary: Phase 1 establishes all single-monitor move/resize mechanics with correct guards, and Phase 2 adds cross-monitor support and overlay mode integration. A third optional phase covers mode-icon rendering if it was deferred.

### Phase 1: Core Move/Resize Mechanics (Single Monitor)

**Rationale:** All Phase 2 work depends on the core move/resize pipeline being correct. The coordinate system pitfalls (A-2, A-3), modifier tracking pitfalls (A-5, A-6, A-7), and grid math pitfalls (A-8, A-9) must be resolved before layering in cross-monitor complexity or overlay integration. This phase carries the highest technical risk — it involves the most non-obvious Win32 behavior and the most consequences if gotten wrong.

**Delivers:** CAPS+TAB+direction moves the foreground window. CAPS+LSHIFT+direction grows an edge. CAPS+LCTRL+direction shrinks an edge. All operations are grid-snapped, clamped to work area, guarded against maximized/minimized/elevated windows, and tested on a single monitor.

**Addresses features:** All P1 must-haves except cross-monitor move; grid config keys; smart snap; minimum window size clamping; all operations instant.

**Avoids pitfalls:** A-1 (maximized guard), A-2 (coordinate space separation — establish dual-rect pattern before any grid math), A-3 (DPI context — verify on mixed-DPI setup as acceptance criterion), A-4 (UIPI — return-value check from day one), A-5 (TAB passthrough policy — decide before writing hook logic), A-6 (VK_LSHIFT specificity), A-7 (modifier state reset on CAPSLOCK release), A-8 (rcWork grid boundaries), A-9 (integer rounding — unit test with non-divisible resolutions), A-13 (min/max clamp with post-call read-back)

**Build order within phase:**
1. Extend `FocusConfig` with `GridDivisions`, `GridSnap`, `GridSnapTolerance`
2. Add `TabHeld` to `KeyEvent` record (non-breaking default)
3. Extend `KeyboardHookHandler`: VK_TAB intercept, `_tabHeld` state, VK_LSHIFT/VK_LCONTROL specificity
4. Add modifier routing to `CapsLockMonitor` (Option A: single callback with modifier string parameter)
5. Implement `GridCalculator` static methods with unit tests covering non-divisible resolutions
6. Implement `WindowManagerService.MoveWindow` (single monitor, all guards)
7. Implement `WindowManagerService.GrowWindow` and `ShrinkWindow`
8. Wire `OverlayOrchestrator.OnModifiedDirectionKeyDown`

### Phase 2: Cross-Monitor Support and Overlay Integration

**Rationale:** Cross-monitor move requires `MonitorHelper.FindAdjacentMonitor`, which depends on the core move pipeline being proven correct first. Overlay mode integration (reposition-in-place, ForegroundMonitor guard, mode indicators) requires the STA dispatch path to be stable before adding the `_moveOrResizeInProgress` flag and mode-aware render paths.

**Delivers:** Move across monitor boundaries lands window at the correct grid cell on the adjacent monitor. Overlays track window position after each move step without flicker. Mode-specific overlay icons (move arrows, grow/shrink edge indicators) reflect the active modifier.

**Addresses features:** Cross-monitor move transition (P1 feature); mode-specific overlay indicators (P1 differentiator).

**Avoids pitfalls:** A-10 (cross-monitor jump — detect monitor transition and snap to first grid cell of new monitor), A-11 (overlay flicker — use reposition-in-place, not HideAll+ShowAll), A-12 (overlay tracks window after each step), A-14 (ForegroundMonitor refresh loop during move — add `_moveOrResizeInProgress` guard)

**Build order within phase:**
1. Add `MonitorHelper.FindAdjacentMonitor`
2. Extend `WindowManagerService.MoveWindow` for cross-monitor detection
3. Add `_moveOrResizeInProgress` guard to `OverlayOrchestrator`
4. Implement overlay reposition-in-place (avoid HideAll+ShowAll per keypress)
5. Add `OverlayMode` enum to `IOverlayRenderer.Paint` as optional parameter
6. Implement `ModeIconRenderer` or extend `BorderRenderer` with mode awareness

### Phase 3: Configuration Extensions (Optional)

**Rationale:** Power-user features that add value but do not block core functionality. Defer until Phase 1 and Phase 2 are validated against real usage.

**Delivers:** Per-monitor grid fraction overrides in config; snap-to-edge-on-hold variant; `focus snap-all` CLI command.

**Addresses features:** P2 features from FEATURES.md prioritization matrix; future considerations.

### Phase Ordering Rationale

- Phase 1 before Phase 2: the coordinate system and modifier state machine must be correct before overlay integration can be correct. Cross-monitor behavior is hard to test without a stable same-monitor baseline.
- Guard-first within Phase 1: all pitfall mitigations (maximized check, UIPI check, coordinate space separation) must be implemented before the happy path, not bolted on afterward. Testing a simple-cases implementation and adding guards later always requires refactoring the core path.
- Config extension first within Phase 1: `FocusConfig` is a dependency of every other Phase 1 component.
- `KeyEvent` / `KeyboardHookHandler` before `CapsLockMonitor` before `OverlayOrchestrator` before `WindowManagerService`: this is the dependency chain; building out of order leaves placeholder wiring that is error-prone.
- The `OverlayMode` enum parameter is introduced in Phase 2 even if the initial implementation ignores the value — this establishes the seam at the correct time and avoids a breaking interface change in Phase 3.

### Research Flags

Phases with standard patterns that need no additional research:
- **Phase 1, steps 1-4** (config extension, KeyEvent extension, hook modifier tracking, CapsLockMonitor routing): established patterns matching existing code
- **Phase 1, steps 5-8** (GridCalculator, WindowManagerService, orchestrator wiring): all APIs verified from official Microsoft Learn docs at HIGH confidence
- **Phase 2, steps 1-2** (cross-monitor move): `MonitorHelper.EnumerateMonitors` already exists; geometry over existing monitor list

Phases that may need investigation during implementation:
- **Phase 1 — DPI context for external windows (A-3, MEDIUM confidence):** `SetWindowPos` behavior when moving a DPI-unaware target window from a PerMonitorV2 daemon process needs empirical validation on a mixed-DPI setup with a legacy DPI-unaware app. If position is wrong by a scaling factor, `SetThreadDpiAwarenessContext` override will be needed in the move handler.
- **Phase 1 — min/max size enforcement (A-13, MEDIUM confidence):** Whether `SetWindowPos` automatically clamps to external window `WM_GETMINMAXINFO` ptMinTrackSize needs testing with an app that has a known minimum size (e.g., Calculator) before shipping the shrink feature.
- **Phase 1 — TAB chord system-level interaction (A-5, LOW confidence):** Whether CAPS+TAB at the LL hook level triggers any Windows system behavior before the hook can suppress it needs empirical testing before writing suppression logic. Test this chord manually at Phase 1 start.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All new APIs verified against official Microsoft Learn docs. No new packages. CsWin32 generates all required declarations automatically. Version constraints already satisfied by existing app.manifest. |
| Features | MEDIUM-HIGH | Core move/grow/shrink patterns verified against i3, Hyprland, dwm, Rectangle, and Win32 docs. Smart snap pattern verified from FancyZones and retracile.net grid WM implementations. Cross-monitor behavior extrapolated from Rectangle; Win32 mechanics verified. Edge-direction semantics confirmed from multiple independent WM sources. |
| Architecture | HIGH | Existing architecture is documented against the live v3.0 codebase, not against a design doc. Integration points are precise: specific class names, method names, and field names are identified. No unknown components. Component status table (new/modify/extend) is explicit. |
| Pitfalls | HIGH | Majority verified against official Microsoft documentation. Coordinate space pitfall (A-2) is empirically confirmed in multiple sources and is consistent with the existing codebase's own usage pattern. UIPI (A-4) documented in official security overview. TAB passthrough (A-5) is empirically unconfirmed at LOW confidence — only gap in the pitfall set. |

**Overall confidence:** HIGH

### Gaps to Address

- **DPI virtualization for DPI-unaware target windows (A-3):** Research concludes the daemon's PerMonitorV2 context provides physical pixels, but interaction with a DPI-unaware target window's coordinate interpretation is not fully verified. Address by building a mixed-DPI test scenario (move a Notepad or 32-bit legacy app on a non-100% DPI monitor) as part of Phase 1 acceptance testing. If wrong, add `SetThreadDpiAwarenessContext` override.

- **TAB chord system-level interaction (A-5 corollary):** Whether CAPS held while TAB is pressed triggers any Windows system behavior at the keyboard hook level is empirically unknown. Address by testing this specific chord at Phase 1 start — before writing suppression logic — to confirm what the chord does without any hook intervention.

- **SetWindowPos auto-clamp of external window min-size (A-13):** Whether calling `SetWindowPos` with dimensions smaller than an external window's `WM_GETMINMAXINFO` ptMinTrackSize silently clamps (returns true, window stays at min) or requires explicit pre-clamping is MEDIUM confidence. Address with a concrete test (shrink Calculator, compare actual vs. requested rect) before shipping the shrink feature.

- **Overlay flicker threshold (A-11):** Whether the existing HideAll+ShowAll pattern produces visible flicker during rapid move key presses is unknown without testing. The reposition-in-place solution is implemented only if flicker is confirmed user-visible during Phase 2 testing. This is an accept/defer decision, not a design gap.

## Sources

### Primary (HIGH confidence)
- [SetWindowPos — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) — coordinate system, SWP_ flags, maximized window behavior
- [GetWindowRect — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect) — invisible border inclusion, DPI virtualization behavior, round-trip safety with SetWindowPos
- [DwmGetWindowAttribute — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute) — DWMWA_EXTENDED_FRAME_BOUNDS behavior vs. GetWindowRect
- [GetDpiForWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow) — Windows 10 1607+ requirement, per-monitor DPI
- [GetDpiForMonitor — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor) — MDT_EFFECTIVE_DPI, Shcore.dll, Windows 8.1+
- [High DPI Desktop Application Development — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) — per-monitor v2 physical pixel model
- [Virtual-Key Codes — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes) — VK_LSHIFT=0xA0, VK_LCONTROL=0xA2, VK_TAB=0x09
- [LowLevelKeyboardProc — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) — hook callback constraints, 1-second timeout
- [KBDLLHOOKSTRUCT — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct) — vkCode left/right distinction, LLKHF_UP flag
- [MONITORINFO / GetMonitorInfo — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo) — rcWork vs. rcMonitor, taskbar exclusion
- [Security Considerations for Assistive Technologies (UIPI) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview) — integrity level restrictions on SetWindowPos
- [WM_GETMINMAXINFO — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-getminmaxinfo) — minimum window size enforcement
- [Hyprland Dispatchers](https://wiki.hypr.land/Configuring/Dispatchers/) — moveactive/resizeactive semantics (feature validation)
- [i3 User's Guide](https://i3wm.org/docs/userguide.html) — move/resize direction semantics (feature validation)
- [dwm moveresize patch](https://dwm.suckless.org/patches/moveresize/) — pixel-step resize pattern (feature validation)
- [Rectangle — rxhanson/Rectangle](https://github.com/rxhanson/Rectangle) — cross-monitor traverse behavior (feature validation)

### Secondary (MEDIUM confidence)
- [FancyZones — PowerToys — Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones) — snap tolerance default (10%) and zone merge behavior
- [Grid-based Tiling Window Management, Mark II — retracile.net](https://retracile.net/blog/2022/08/27/00.00) — snap-first-then-step pattern from real implementation
- [GetWindowRect — invisible borders reference](https://www.w3tutorials.net/blog/getwindowrect-returns-a-size-including-invisible-borders/) — border offset values (~7-8px), consistent with DWM docs
- [Determining modifier key state when hooking keyboard input — Jon Egerton](https://jonegerton.com/dotnet/determining-the-state-of-modifier-keys-when-hooking-keyboard-input/) — GetKeyState(VK_LSHIFT) from hook callback pattern
- [EmacsWiki: Grow Shrink Windows](https://www.emacswiki.org/emacs/GrowShrinkWindows) — edge-direction semantics confirmation
- [Moving and Resizing Windows — Sawfish WM manual](https://www.sawfish.tuxfamily.org/sawfish.html/Moving-and-Resizing-Windows.html) — keyboard resize direction semantics

### Tertiary (LOW confidence)
- TAB chord system-level behavior (CAPS+TAB at LL hook level) — no authoritative documentation found; requires empirical testing before Phase 1 implementation

---
*Research completed: 2026-03-02*
*Ready for roadmap: yes*
