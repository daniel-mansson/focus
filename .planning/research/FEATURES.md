# Feature Research

**Domain:** Grid-snapped keyboard window move/resize for Windows daemon (v3.1 milestone)
**Researched:** 2026-03-02
**Confidence:** MEDIUM-HIGH (grid step move/resize patterns verified against Hyprland dispatchers, dwm moveresize patch, Rectangle cross-monitor behavior, and Win32 API official docs; some conventions derived from i3/sway + Emacs community patterns where no single authoritative Win32 source exists)

> **Scope note:** This file covers v3.1 features only — the grid-snapped window move/resize milestone. All prior features (navigation, overlays, chaining, number selection) are already shipped in v1.0–v3.0. This research focuses on: expected behavior for grid move/resize, edge-based grow/shrink semantics, snap tolerance, cross-monitor transitions, minimum size clamping, and overlay indicators for the new modes.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features users of keyboard-driven window managers assume exist. Missing these = the move/resize feature feels broken.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Move window by grid step (CAPS+TAB+direction) | Every keyboard window manager (i3, Hyprland, dwm, Rectangle) has a move-by-step command. Users from any tiling WM background expect this as the baseline. | MEDIUM | Use `SetWindowPos` with `SWP_NOSIZE`. Step = `(monitorWidth / gridFraction)` or `(monitorHeight / gridFraction)`. Must use physical pixel coordinates — not DPI-scaled virtual coords. |
| Grow window edge outward by grid step (CAPS+LSHIFT+direction) | Edge-based grow is the standard resize idiom in all keyboard WMs (i3: `resize grow right`, dwm `moveresize` patch, Hyprland `resizeactive`). Users expect "direction = which edge expands". | MEDIUM | Direction maps to the moving edge: right = right edge moves right (width grows), left = left edge moves left (x decreases, width grows), up = top edge moves up (y decreases, height grows), down = bottom edge moves down (height grows). |
| Shrink window edge inward by grid step (CAPS+LCTRL+direction) | Paired with grow — every WM that has grow also has shrink with same direction semantics reversed. | MEDIUM | Direction maps to the retreating edge: right = right edge moves left (width shrinks), left = left edge moves right (x increases, width shrinks), up = top edge moves down (y increases, height shrinks), down = bottom edge moves up (height shrinks). |
| Grid fraction configurable (default 1/16th screen) | Users with different monitor sizes need to tune step size. Fixed pixel steps are wrong for 4K vs 1080p setups. Fraction of screen dimensions is the right unit. | LOW | JSON config key `gridFraction` (default: 16). Step = `monitorDimension / gridFraction` rounded to nearest integer. Per-monitor support means each monitor computes its own step from its own dimensions. |
| Move window stops at monitor boundary (no-op or clamp) | Users expect a hard boundary at the screen edge — dragging past it with keyboard should stop, not push the window off-screen. | LOW | Clamp computed position: `newX = Max(monitorLeft, Min(newX, monitorRight - windowWidth))`. Same logic for vertical axis. This is standard behavior in all keyboard WMs researched. |
| Minimum window size clamping for shrink | Applications define a minimum size via `WM_GETMINMAXINFO` / `MINMAXINFO.ptMinTrackSize`. Attempting to shrink below this must silently no-op rather than glitch. Additionally, a floor should prevent windows from being shrunk to zero size. | LOW | Query the target window's minimum size before shrinking. System minimum: `GetSystemMetrics(SM_CXMINTRACK)` and `SM_CYMINTRACK`. Also respect any app-defined minimum in MINMAXINFO. Clamp shrink result; no-op if already at minimum. Note: `SetWindowPos` itself may enforce the minimum — test behavior. |
| Smart snap: first-press aligns to grid, subsequent presses step | When a window is between grid lines (e.g., manually positioned), the first move/resize keypress should align it to the nearest grid boundary, not step from current off-grid position. This prevents accumulated drift. Established pattern from grid window managers (retracile.net grid WM, WindowGrid). | MEDIUM | On each operation: compute where the window "should be" if snapped, compare to current. If current is within snap tolerance (~10% of grid step) of a grid line, treat as on-grid and step. If outside tolerance, snap to nearest grid line as the operation (consuming the keypress without additional step). |
| Works only on the foreground window | All keyboard WMs operate on the active/focused window. Moving non-focused windows via keyboard creates disorientation. | LOW | Use `GetForegroundWindow()` — already established pattern in the existing daemon for all CAPSLOCK combos. No change to this behavior. |

### Differentiators (Competitive Advantage)

Features that set this tool apart from generic keyboard WMs.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Per-monitor grid (not global pixel step) | Most simple Windows keyboard WM tools use a fixed pixel step globally (e.g., "move 50px"). A fraction-of-monitor grid means the step feels consistent regardless of monitor resolution or DPI. Useful for mixed 1080p/4K setups. | MEDIUM | Requires `MonitorFromWindow` to identify which monitor the window is currently on, then `GetMonitorInfo` to get that monitor's work area. Compute step from work area dimensions. Already have monitor info logic for the navigation system — reuse. |
| Cross-monitor move support | When a window is moved to the edge of one monitor, continuing to press move in that direction should place the window on the adjacent monitor. Most simple tools silently stop; the best tools (Rectangle's "traverse across displays") cross over. | HIGH | Detect when the step would push the window's center past the current monitor's boundary. Find the adjacent monitor in that direction (using existing virtual screen coordinate logic). Place window at the corresponding position on the new monitor, scaled for the new monitor's dimensions. Edge case: DPI difference between monitors — must recompute grid step for the new monitor. |
| Mode-specific overlay indicators | Generic move/resize tools give zero visual feedback about what mode you're in. An overlay showing mode-appropriate arrows (move arrows vs. grow/shrink edge arrows pointing outward/inward) reduces cognitive load and helps users confirm the right modifier was held. No comparable Windows tool provides this. | MEDIUM | Extend `IOverlayRenderer` interface with a mode parameter (move / grow / shrink). Add a directional arrow icon to the overlay for the active mode. Existing overlay windows already show during CAPS hold — add mode label/arrow to overlay content. |
| Configurable snap tolerance | Fixed snap tolerances don't fit all workflows. A tolerance of 10% of grid step is a reasonable default (established in FancyZones zone merge behavior), but power users with fine grid fractions may want zero (pure step, no snap) or larger tolerances (aggressive alignment). | LOW | JSON config key `snapTolerancePercent` (default: 10). Applied as: `tolerancePixels = gridStep * snapTolerancePercent / 100`. Zero disables snap-first behavior. |
| Instant operations (no animation) | The existing tool already established "instant over animated" as a key UX decision (tested and validated in v2.0 fade removal). Move/resize should be instantaneous — window jumps to new position. No slide or transition. | LOW | `SetWindowPos` with `SWP_NOACTIVATE | SWP_NOZORDER` — already instant by default in Win32. Do not add any animation machinery. |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem useful but create disproportionate complexity or conflict with design goals.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Animated window movement | Smoother UX feel during moves | Animation requires a timer loop updating window position per frame. Introduces latency, CPU usage on every move keypress, and complexity around interrupt (what if another key arrives mid-animation?). Already validated as worse UX than instant in v2.0 overlay testing. | Instant `SetWindowPos` is the right answer. Window snaps to new grid position immediately. |
| Pixel-exact move (no grid) | Maximum flexibility for precise placement | Without grid alignment, repeated moves accumulate floating-point drift. Windows end up at arbitrary pixel positions that are hard to reason about. The whole value proposition of this feature is grid discipline. If users want pixel-level control, they have the mouse. | Keep grid-only movement. Expose `gridFraction` for fine-grained control (gridFraction: 64 = 1/64th of screen ≈ very fine on 1080p). |
| Window layout memory / restoration | Restore windows to their "last managed position" after layout disruption | State tracking per window (per HWND) introduces a dictionary that must be maintained across window creation/destruction, monitor changes, and process restarts. This is layout manager territory (FancyZones, Komorebi) — far outside the scope of a focus navigation tool. | Implement a "snap all to grid" CLI command as a future consideration. Single-shot, no persistent state needed. |
| Simultaneous grow on both axes (diagonal resize) | Fewer keystrokes to resize to a corner | Diagonal resize breaks the "one edge per direction key" model and requires a two-key combo interpretation that conflicts with the existing single-direction model. The four edge-based grow/shrink operations cover all resize needs with consistent semantics. | Chain two resize operations (grow right, then grow down) for diagonal resize. |
| Automatic grid enforcement (snap on focus change) | Windows stay on grid automatically without user action | Windows that were manually positioned (by the user, or by apps themselves) getting forcibly snapped on focus change is hostile. Many apps position themselves intentionally (IDE tool windows, dialog boxes, floating panels). | Snap is always user-initiated via the CAPS+modifier+direction combo. No automatic enforcement. |
| Resize mode (modal state) | Enter a persistent "resize mode" (like i3's `$mod+r`) where arrows resize until Escape | Modal state means the daemon must track and expose a "current mode" that overrides normal navigation. This conflicts with the existing CAPS+direction = navigate binding, requiring fallback logic. The non-modal combo approach (CAPS+LSHIFT vs CAPS+LCTRL) avoids all mode state. | Non-modal: modifier keys determine the operation. CAPS+LSHIFT+direction = grow. CAPS+LCTRL+direction = shrink. CAPS+TAB+direction = move. Clear, no state required. |
| Window tiling (auto-arrange into zones) | Automatic layout enforcement | Window tiling is a full layout management system (FancyZones, Komorebi) — not move/resize primitives. Would require zone definition, layout algorithms, window assignment logic, and conflict with non-managed windows. Far outside scope. | Grid-based keyboard move/resize IS the primitive that enables manual tiling workflows without the overhead of a full tiling system. |

---

## Feature Dependencies

```
[Grid configuration (gridFraction, snapTolerancePercent in FocusConfig)]
    └──required by──> [Grid step calculation per monitor]
                          └──required by──> [Move by grid step (CAPS+TAB+direction)]
                          └──required by──> [Grow edge by grid step (CAPS+LSHIFT+direction)]
                          └──required by──> [Shrink edge by grid step (CAPS+LCTRL+direction)]

[MonitorFromWindow + GetMonitorInfo (existing multi-monitor logic)]
    └──required by──> [Per-monitor grid step]
                          └──required by──> [Cross-monitor move transition]
                                                └──requires──> [Adjacent monitor detection]

[Minimum size query (GetSystemMetrics SM_CXMINTRACK/SM_CYMINTRACK)]
    └──required by──> [Shrink clamp to minimum]

[Smart snap logic (tolerance check vs. current position)]
    └──required by──> [Snap-first-then-step behavior]
    └──depends on──> [Grid step calculation per monitor]

[CAPS+TAB detection in keyboard hook]
    └──required by──> [Move mode activation]

[CAPS+LSHIFT detection in keyboard hook]
    └──required by──> [Grow mode activation]

[CAPS+LCTRL detection in keyboard hook]
    └──required by──> [Shrink mode activation]

[IOverlayRenderer (existing v2.0 interface)]
    └──required by──> [Mode-specific overlay icons (move arrows / grow arrows / shrink arrows)]
    └──enhances──> [Move/resize operations] (visual confirmation of active mode)

[SetWindowPos Win32 call]
    └──required by──> [All move and resize operations]
    └──depends on──> [Physical pixel coordinates, not DPI-virtualized]
```

### Dependency Notes

- **Grid step before everything:** All move and resize features compute a step size from the monitor dimensions. The grid config and monitor detection logic must be in place before any move/resize feature can work. This is the first thing to implement.
- **MonitorFromWindow already available:** The navigation system already does multi-monitor work. Reuse `MonitorFromWindow` and `GetMonitorInfo` — don't reimplement. The extension needed is "find the adjacent monitor in a direction," which is new logic.
- **Smart snap depends on grid step and current position:** The snap tolerance check needs the computed grid step AND the current window position. Both are cheap — this is arithmetic, not a Win32 call.
- **Overlay mode indicators extend, not replace, existing overlay:** The mode icons (move/grow/shrink arrows) should be additions to the existing overlay, triggered only during CAPS+TAB or CAPS+LSHIFT/LCTRL holds. They reuse the existing overlay window infrastructure. Do not rebuild overlays from scratch.
- **LSHIFT/LCTRL detection is new for the hook:** The existing hook tracks CAPS and direction keys. LSHIFT and LCTRL must be added as tracked modifiers. Verify the hook callback receives these in the chord alongside CAPS. Note: LSHIFT suppresses direction key characters in some apps — verify no interference with existing CAPS+direction navigation.
- **SetWindowPos coordinates are physical pixels in virtual screen space:** When using DwmGetWindowAttribute for window bounds (existing code), coordinates are already in physical pixels. Ensure consistency — don't mix DWM-reported bounds with `GetWindowRect` if the process has DPI virtualization active. The existing code uses DWM bounds throughout — maintain that.

---

## MVP Definition

This milestone has one goal: keyboard-driven grid-snapped window move and resize, with mode feedback overlays.

### Launch With (v3.1)

Minimum viable product for the window move/resize milestone.

- [ ] Detect CAPS+TAB held as "move mode" in the keyboard hook
- [ ] Detect CAPS+LSHIFT held as "grow mode" in the keyboard hook
- [ ] Detect CAPS+LCTRL held as "shrink mode" in the keyboard hook
- [ ] Compute grid step per monitor (monitorDimension / gridFraction, default gridFraction=16)
- [ ] Config keys `gridFraction` and optional `snapTolerancePercent` (default: 10) in JSON config
- [ ] Move foreground window by grid step in direction (CAPS+TAB+direction)
- [ ] Clamp move to monitor work area — no off-screen positions
- [ ] Grow window edge outward by grid step in direction (CAPS+LSHIFT+direction) — right edge for right, top edge for up, left edge for left, bottom edge for down
- [ ] Shrink window edge inward by grid step in direction (CAPS+LCTRL+direction) — matching edge retreats
- [ ] Clamp shrink to minimum window size (system minimum + app minimum from MINMAXINFO)
- [ ] Smart snap: align to nearest grid line on first press if outside tolerance, step on subsequent presses within tolerance
- [ ] Cross-monitor move: when move would push window past current monitor boundary, transition to adjacent monitor
- [ ] Mode-specific overlay icons: show directional arrow(s) indicating move vs. grow vs. shrink during operation
- [ ] All operations are instant (no animation) via SetWindowPos

### Add After Validation (v3.1.x)

- [ ] Per-monitor grid fraction override in config — add if users with heterogeneous monitor setups report the global fraction doesn't suit all monitors
- [ ] Configurable snap tolerance override per operation mode (move vs. grow vs. shrink) — add if users report the single tolerance doesn't work well for both operations
- [ ] "Snap to edge" variant: CAPS+TAB+direction held vs. tapped — tap = step, hold-and-release = snap to monitor edge — add if users want quick edge-snap without full move sequence

### Future Consideration (v4+)

- [ ] Snap all windows to grid CLI command (`focus snap-all`) — single-shot cleanup for after monitor reconnects, no persistent state
- [ ] Window position memory / restore for specific apps — explicit opt-in in config, not automatic; very complex, deferred until there's clear user demand
- [ ] DPI-aware grid step scaling for per-monitor overlays — visual step indicators showing the actual pixel span in a DPI-mixed setup

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| CAPS+TAB+direction → move window | HIGH | MEDIUM | P1 |
| CAPS+LSHIFT+direction → grow edge | HIGH | MEDIUM | P1 |
| CAPS+LCTRL+direction → shrink edge | HIGH | MEDIUM | P1 |
| Grid step from configurable fraction | HIGH | LOW | P1 |
| Clamp move to monitor boundary | HIGH | LOW | P1 |
| Clamp shrink to minimum size | HIGH | LOW | P1 |
| Smart snap (snap-first, then step) | MEDIUM | MEDIUM | P1 |
| Mode-specific overlay icons | MEDIUM | MEDIUM | P1 |
| Per-monitor grid step | HIGH | LOW | P1 (reuses existing MonitorFromWindow) |
| JSON config for gridFraction + snapTolerancePercent | MEDIUM | LOW | P1 |
| Cross-monitor move transition | MEDIUM | HIGH | P1 |
| Per-monitor gridFraction override in config | LOW | LOW | P2 |
| Snap to monitor edge on hold | LOW | MEDIUM | P2 |
| Snap-all CLI command | LOW | MEDIUM | P3 |

**Priority key:**
- P1: Must have for v3.1 launch
- P2: Should have, add when possible in v3.1.x
- P3: Nice to have, future milestone

---

## Edge Case Coverage

### Minimum Window Size Enforcement

Win32 provides two sources for minimum window size. The system minimum is `GetSystemMetrics(SM_CXMINTRACK)` / `SM_CYMINTRACK` (typically 115×27 pixels). App-defined minimums come from `WM_GETMINMAXINFO` / `MINMAXINFO.ptMinTrackSize`. Rather than querying `WM_GETMINMAXINFO` (which requires sending a message to the target window and handling its response), the simpler approach is to let `SetWindowPos` enforce the minimum naturally — the system will clamp below ptMinTrackSize automatically — then confirm the resulting dimensions match intent. If the result is smaller than requested, it was clamped, and further shrink no-ops.

**Confidence:** MEDIUM — WM_GETMINMAXINFO is documented in Win32 docs; whether SetWindowPos auto-clamps is confirmed in Win32 window features docs. Exact behavior with external process windows needs testing.

### Cross-Monitor Move with Different DPI

When moving a window from a 96-DPI monitor to a 192-DPI monitor, the window's physical pixel size stays the same but may appear visually different. The grid step on the new monitor is computed from the new monitor's physical dimensions (not DPI-scaled). No size change is applied during cross-monitor move — window moves, position updates, size stays. The app receiving `WM_DPICHANGED` handles its own scaling if it chooses to.

**Important:** Use DwmGetWindowAttribute for position (physical pixels, DPI-unaware offset) consistently. The existing daemon uses this — do not switch to `GetWindowRect` which may return virtualized coords for some processes.

**Confidence:** MEDIUM — DwmGetWindowAttribute behavior confirmed in Win32 docs and existing project use. DPI behavior on SetWindowPos for external windows: MEDIUM confidence based on community sources.

### TAB Key Interaction with CAPS

CAPS+TAB is already a common system shortcut on Windows (reverse Alt+Tab behavior in some contexts). The existing hook suppresses CAPS, so it does not reach Alt+Tab. However, plain TAB with CAPS held may or may not interact with the low-level hook depending on whether the app processes it. The hook must consume CAPS+TAB+direction triples, not pass them to `CallNextHookEx`. Validate that holding CAPS then pressing TAB does not trigger any system-level behavior.

**Confidence:** LOW — behavior of CAPS+TAB at the keyboard hook level requires empirical testing; no authoritative documentation found.

### LSHIFT Conflict with Existing Navigation

The existing CAPS+direction navigation does NOT use LSHIFT as a modifier. CAPS+LSHIFT+direction for grow must be confirmed not to conflict. Additionally, holding LSHIFT while pressing direction keys changes the character produced in the focused app (e.g., selects text in text editors). The hook must consume CAPS+LSHIFT+direction triples. Test that LSHIFT chord is received correctly in `WH_KEYBOARD_LL` when CAPS is held.

**Confidence:** MEDIUM — WH_KEYBOARD_LL receives all keystrokes including LSHIFT; consuming them is standard. Specific chord behavior is empirically testable.

### Window at Monitor Edge with Grow

If the foreground window is already at the monitor's right edge and the user presses grow-right, the right edge has no room to expand. The expected behavior is no-op (clamp). This is symmetric with move clamping. Specifically: compute `newRight = currentRight + gridStep`; clamp to `monitorWorkAreaRight`; if `newRight == currentRight`, no-op.

**Confidence:** HIGH — arithmetic clamping, no API ambiguity.

### Window Larger Than Monitor Grid Fits

If a user grows a window beyond the monitor's work area height/width, the clamp logic prevents overshoot. Windows can technically be larger than the monitor via `SetWindowPos` (parts extend off-screen) — the clamp must prevent this by using `monitorWorkArea` as the hard limit, not `monitorBounds`.

**Confidence:** HIGH — GetMonitorInfo returns both rcMonitor (full bounds) and rcWork (work area excluding taskbar). Use rcWork for all position/size clamping.

---

## Competitor Feature Analysis

How keyboard-driven window move/resize is handled in comparable tools:

| Feature | i3/Sway | Hyprland | dwm moveresize patch | Rectangle (macOS) | This Tool (v3.1 plan) |
|---------|---------|---------|----------------------|-------------------|----------------------|
| Move trigger | Mod+Shift+Arrow | `moveactive` dispatcher | MODKEY+Arrow | Move-left/right/up/down actions | CAPS+TAB+direction |
| Resize trigger | Mod+r then Arrow (modal) | `resizeactive` dispatcher | MODKEY+Shift+Arrow | Separate actions per size | CAPS+LSHIFT/LCTRL+direction (non-modal) |
| Resize semantics | Grow/shrink in direction | Vec2 delta (x,y) | Separate x/y offset params | Discrete presets (halves, thirds, etc.) | Edge-in-direction model |
| Step size unit | Pixel increment (configurable) | Pixel or percentage delta | Pixel increment (hardcoded in config) | Fixed layout fractions (1/2, 1/3, etc.) | Fraction of monitor (1/16th default) |
| Snap on first press | No | No | No | Snaps to preset layout positions | Yes — snap-first then step |
| Cross-monitor move | Yes (Mod+Shift+Arrow on tiling layout) | Yes (follows Hyprland monitor layout) | No (monitor-local only) | Yes (next-display action) | Yes (grid-aware transition) |
| Minimum size enforcement | WM enforces internally | Hyprland enforces | SetWindowPos clamps | OS enforces | Clamp via SetWindowPos behavior |
| Visual mode feedback | None for move (tiling layout is always visible) | None (floating only) | None | None | Mode icons in overlay |
| Non-modal operation | Yes (always in layout) | Yes (dispatcher-based) | No (floating mode required) | Yes (hotkey-based) | Yes (modifier combo) |
| Config for step size | Pixels in config file | Per-bind parameter | Pixels in config.h | Not configurable (preset fractions) | `gridFraction` in JSON config |

---

## Sources

- [Hyprland Dispatchers — moveactive, resizeactive](https://wiki.hypr.land/Configuring/Dispatchers/) — HIGH confidence (official Hyprland wiki, current)
- [dwm moveresize patch](https://dwm.suckless.org/patches/moveresize/) — HIGH confidence (official suckless.org patch documentation, updated 2022)
- [i3 User's Guide — move and resize](https://i3wm.org/docs/userguide.html) — HIGH confidence (official i3 documentation)
- [Rectangle — rxhanson/Rectangle (GitHub)](https://github.com/rxhanson/Rectangle) — HIGH confidence (official README, cross-monitor traverse documented)
- [SetWindowPos — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) — HIGH confidence (official Win32 API reference)
- [WM_GETMINMAXINFO — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-getminmaxinfo) — HIGH confidence (official Win32 API reference)
- [MINMAXINFO — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-minmaxinfo) — HIGH confidence (official Win32 API reference)
- [High DPI Desktop Application Development on Windows](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) — HIGH confidence (official Win32 DPI docs)
- [WM_DPICHANGED — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged) — HIGH confidence (official Win32 API reference)
- [Grid-based Tiling Window Management, Mark II — retracile.net](https://retracile.net/blog/2022/08/27/00.00) — MEDIUM confidence (individual blog post; snap-first-then-step pattern and correction mechanism described from real implementation)
- [FancyZones — PowerToys — Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones) — HIGH confidence (official docs; snap tolerance behavior and zone merge behavior referenced)
- [EmacsWiki: Grow Shrink Windows](https://www.emacswiki.org/emacs/GrowShrinkWindows) — MEDIUM confidence (community wiki; edge direction semantics described from Emacs border-move model)
- [Moving and Resizing Windows — Sawfish WM manual](https://www.sawfish.tuxfamily.org/sawfish.html/Moving-and-Resizing-Windows.html) — MEDIUM confidence (official Sawfish WM docs; keyboard resize direction semantics)
- [Window Features — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) — HIGH confidence (official Win32 reference; minimum tracking size behavior)

---
*Feature research for: grid-snapped keyboard window move/resize (Windows daemon, v3.1 milestone)*
*Researched: 2026-03-02*
