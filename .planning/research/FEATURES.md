# Feature Research

**Domain:** Overlay preview daemon for directional window focus navigation (Windows desktop utility)
**Researched:** 2026-02-28
**Confidence:** MEDIUM-HIGH (overlay window patterns verified via Win32 official docs, PowerToys source behavior, Komorebi border docs, and multiple Win32 API references; daemon/hook lifecycle patterns from Windows official docs)

> **Scope note:** This file covers v2.0 features only — the overlay preview daemon milestone. The v1.0 navigation feature landscape is documented in the prior FEATURES.md from 2026-02-26. All v1.0 features (navigation, strategies, config, exit codes, etc.) are already shipped and validated.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist given the milestone goal. Missing these = the overlay feels broken or incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Overlay appears while modifier is held | Any "preview while held" system works exactly this way (PowerToys Shortcut Guide: hold Win key → overlay; FancyZones: hold Shift while dragging → zone preview). Users will assume hold = show. | MEDIUM | Requires WH_KEYBOARD_LL hook in a persistent daemon process. CAPSLOCK is a toggle key — key-down must track held state, key-up dismisses. See Pitfalls: CAPSLOCK toggle vs. hold distinction. |
| Overlay dismisses on modifier release | Same pattern as PowerToys Shortcut Guide. An overlay that stays on screen after releasing the key is disorienting and blocks the desktop. | LOW | WM_KEYUP / WM_SYSKEYUP from the hook signals dismiss. Must handle process termination cleanup (destroy overlay windows on exit). |
| Colored border on target windows per direction | Komorebi does this for focused/unfocused state. Users navigating directionally need a spatial cue — a colored border on the target window identifies where the arrow will take them. Border at window boundary = clear visual affordance. | HIGH | Requires per-direction layered overlay windows (WS_EX_LAYERED + WS_EX_TOPMOST + WS_EX_TRANSPARENT + WS_EX_TOOLWINDOW) positioned to match target window bounds. One overlay window per direction (up to 4 simultaneously). |
| Overlay windows excluded from navigation candidates | If the overlay windows appear in the window enumeration, the navigation system would try to focus them — completely breaking the UX. Komorebi's managed windows are excluded from its own focus logic by design. | LOW | Set WS_EX_TOOLWINDOW on all overlay windows. NavigationService already filters these out via WindowEnumerator's existing hidden/toolwindow filtering. Verify this is covered in WindowEnumerator. |
| Overlay tracks active window changes | If the user switches foreground window while CAPSLOCK is held (via mouse click or other means), the overlay targets should update to reflect the new foreground context. Showing stale targets is confusing. | MEDIUM | Poll foreground window on a short interval (~100ms) while modifier is held, or hook WinEvent via SetWinEventHook (EVENT_SYSTEM_FOREGROUND). Update overlay positions when foreground changes. |
| Overlay windows are click-through | A colored border overlay that captures mouse clicks is user-hostile — it would block interaction with the actual target window underneath. | LOW | WS_EX_TRANSPARENT on overlay windows passes all mouse events through to underlying windows. Combine with WS_EX_LAYERED for rendering. |
| No taskbar entry for overlay windows | Overlay windows appearing in the taskbar or Alt+Tab switcher pollutes the window management experience. | LOW | WS_EX_TOOLWINDOW suppresses taskbar and Alt+Tab appearance. Required alongside WS_EX_LAYERED. |
| Daemon starts reliably and cleans up on exit | A background daemon that leaves orphaned overlay windows or hung hooks on crash/kill is unacceptable for a desktop utility. PowerToys invests heavily in process lifecycle management for this reason. | MEDIUM | Use Console.CancelKeyPress and AppDomain.ProcessExit handlers. Destroy all overlay HWNDs before exit. WH_KEYBOARD_LL hooks are automatically removed when the registering process exits (Win32 guarantee). |

### Differentiators (Competitive Advantage)

Features that set this tool apart. Not table stakes for the category, but valuable additions.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Per-direction configurable colors | Komorebi uses a single color per state (focused/unfocused). Having separate colors per direction (left=blue, right=green, up=yellow, down=red) gives immediate spatial orientation — users glance and know which border is the left target vs right target without reading labels. No other tool in this space does this. | LOW | Config schema extension: add `overlayColors: { left, right, up, down }` to existing JSON config. Hex string format (#RRGGBB or #AARRGGBB). Default to a sensible per-direction color set. |
| Pluggable renderer system (default + per-strategy custom) | Navigation strategies produce different conceptual targets (e.g., axis-only ignores perpendicular completely; edge-matching uses different anchor edges). A pluggable renderer allows strategy-aware visualizations — e.g., an edge-matching renderer draws lines connecting matching edges, not just target borders. No comparable tool offers this. | HIGH | Define an IOverlayRenderer interface. Default renderer: colored border. Strategy renderers: optional, registered by strategy name. Renderer selection mirrors strategy selection from FocusConfig. Renderer receives: foreground window bounds, candidate list, direction, config. |
| Overlay updates live while modifier held | If the user has multiple strategies configured per-hotkey (AHK context-sensitive), the overlay always reflects the currently active strategy. More practically, it reflects foreground window changes without requiring key re-press. PowerToys Shortcut Guide is static — it shows the same info until dismissed. | MEDIUM | Re-run scoring on foreground window change event. This is the same GetRankedCandidates call used for actual navigation — reuse NavigationService directly. |
| Overlay shows all 4 directions simultaneously | Most window manager previews show one target at a time (triggered per action). Showing all 4 directional targets simultaneously — while the modifier is held — gives a spatial map of the reachable windows. This matches how Hyprland's window overview plugins (hyprview, hycov) work but for directional context. | HIGH | Requires 4 overlay windows (one per direction) rendered simultaneously. Must handle cases where fewer than 4 candidates exist (gracefully omit overlay for missing direction). |
| Configurable activation delay (debounce before showing overlay) | PowerToys Shortcut Guide defaults to 900ms hold before showing — prevents accidental triggers during fast key combinations. A short delay (150-300ms default) prevents overlay flicker when CAPSLOCK is tapped as part of another shortcut. | LOW | Track key-down timestamp. Show overlay only after hold duration threshold. Make threshold configurable in JSON config (e.g., `overlayDelayMs: 150`). |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem reasonable but create disproportionate complexity or conflict with design goals.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Animated overlay transitions (fade in/out) | Overlays that appear/disappear suddenly feel jarring; smooth animations feel polished | Animation requires a message loop with per-frame alpha updates via SetLayeredWindowAttributes. This adds complexity to the daemon's main loop and increases CPU usage on every key press/release. The UX gain is minimal for a utility overlay that should be instant. | Use a very short fixed alpha (fully opaque, no animation). If transition smoothing is later requested, add as optional config flag. Start without it. |
| Overlay shows window title or content preview | Users might want to see "which window is VS Code" rather than just a colored border | Window content thumbnails require DwmRegisterThumbnail (DWM composition thumbnail API) or capture via BitBlt. Complex to position, creates DWM thumbnail lifetime management. Borders are sufficient for spatial navigation — the user already knows their window layout. | The colored border at the actual window position IS the preview — it highlights the window itself, not a miniature of it. |
| CAPSLOCK toggle state feedback on the overlay | CAPSLOCK's toggle state (on/off) changes with each press. Showing toggle state in the overlay creates user confusion about whether the overlay is showing "CAPSLOCK is on" or "I am holding CAPSLOCK". | CAPSLOCK is being used as a modifier (hold = activate), not a toggle in this tool. The overlay should suppress the CAPSLOCK toggle entirely while the daemon is running (consume the key event before it toggles the indicator). | Suppress CAPSLOCK state change in the hook callback by not passing to CallNextHookEx, OR accept that CAPSLOCK state toggles but the overlay behavior is hold-based regardless. Document clearly in README. |
| Overlay windows with drag handles or interactive elements | Some users might want to click on an overlay to jump to that window (instead of pressing the arrow key) | Adding interaction to overlay windows requires removing WS_EX_TRANSPARENT, which causes overlay windows to appear in z-order and intercept clicks to underlying content. This conflicts with the "click-through overlay" table stake. | Keep overlay windows strictly visual (WS_EX_TRANSPARENT). Navigation is via keyboard (CAPSLOCK + Arrow). Mouse interaction = different UX paradigm, out of scope. |
| System tray icon or context menu for daemon | Users want a tray icon to start/stop the daemon, see status, change config | System tray requires a NotifyIcon (Windows.Forms) or Shell_NotifyIcon P/Invoke, message loop management, and WM_USER message handling. This adds a GUI layer to what is intentionally a CLI-first tool. | Expose daemon lifecycle via CLI: `focus daemon start/stop/status`. AHK can call these. Status via exit codes. No GUI required. |
| Auto-start on login | Users want the daemon to persist across reboots without manual start | Auto-start via Windows Startup folder or Task Scheduler requires installer-level changes, UAC interaction, and a documented uninstall path. This is a deployment/packaging problem, not a feature problem. | Document how to add to AHK script startup or Windows Startup folder manually. The daemon is just a process; standard Windows mechanisms handle auto-start without the tool needing to manage it. |
| Overlay rendering via WPF or WinUI | Richer visual rendering via managed UI frameworks | WPF and WinUI bring significant binary size and runtime dependencies. WPF in particular requires presenting windows through a WPF Application object, which conflicts with the existing Console app host model. The existing P/Invoke-only approach must be preserved. Win32 GDI/GDI+ is sufficient for colored borders. | Use Win32 P/Invoke directly: CreateWindowEx, WM_PAINT with GDI+ or direct GDI for border drawing. Same P/Invoke approach as v1.0's window enumeration and focus activation. |

---

## Feature Dependencies

```
[Daemon process (background message loop)]
    └──required by──> [WH_KEYBOARD_LL keyboard hook]
                          └──required by──> [CAPSLOCK hold detection]
                                                └──required by──> [Overlay show/hide trigger]

[CAPSLOCK hold detection]
    └──required by──> [Activation delay debounce]
                          └──required by──> [Overlay window creation]
                                                └──required by──> [Per-direction colored border rendering]

[NavigationService (existing v1.0)]
    └──required by──> [Scoring candidates for overlay positions]
                          └──required by──> [Overlay window positioning]
                                                └──required by──> [Per-direction configurable colors]

[FocusConfig (existing v1.0, extended)]
    └──required by──> [Active strategy selection for overlay]
    └──required by──> [Per-direction color config]
    └──required by──> [Activation delay config]

[IOverlayRenderer interface]
    └──required by──> [Default border renderer]
    └──required by──> [Per-strategy custom renderers]
    └──depends on──> [NavigationService scoring output]

[WS_EX_TOOLWINDOW on overlay windows]
    └──required by──> [Overlay exclusion from navigation candidates]
    └──required by──> [No taskbar/Alt+Tab entry]

[Foreground window change detection]
    └──enhances──> [Overlay window positioning] (live update while modifier held)
```

### Dependency Notes

- **Daemon required before everything:** The keyboard hook (WH_KEYBOARD_LL) requires a message loop in a persistent process. The daemon is the prerequisite for all other overlay features. This is the first thing to build.
- **NavigationService is directly reusable:** The scoring logic in v1.0's NavigationService takes window list + direction + strategy and returns ranked candidates. The overlay uses the exact same output — positions overlay windows on the top-ranked candidate per direction. No new scoring logic needed.
- **FocusConfig extension is additive:** Per-direction colors, activation delay, and renderer selection are new JSON keys. Existing config deserialization handles unknown keys gracefully (System.Text.Json ignores extras by default). Backward compatible.
- **WS_EX_TOOLWINDOW is a hard dependency for correctness:** If overlay windows appear in navigation candidates, the system will try to focus itself. This must be set at window creation time, not added later. WindowEnumerator's existing filter must be verified to exclude WS_EX_TOOLWINDOW windows.
- **IOverlayRenderer interface enables phasing:** Default renderer ships in milestone v2.0. Per-strategy custom renderers can be added in subsequent milestones without changing the interface contract.

---

## MVP Definition

This milestone has one goal: a daemon with working visual overlay while CAPSLOCK is held.

### Launch With (v2.0)

Minimum viable product for the overlay daemon milestone.

- [ ] Background daemon process with message loop — no daemon = no hook
- [ ] WH_KEYBOARD_LL keyboard hook registering CAPSLOCK key-down / key-up events
- [ ] CAPSLOCK hold detection (key-down → set held flag; key-up → clear flag)
- [ ] Activation delay (configurable, default ~150ms) to debounce accidental triggers
- [ ] Call NavigationService.GetRankedCandidates for all 4 directions on activation
- [ ] Create 4 overlay windows (one per direction) — WS_EX_LAYERED + WS_EX_TOPMOST + WS_EX_TRANSPARENT + WS_EX_TOOLWINDOW
- [ ] Position overlay windows to match top-ranked candidate bounds per direction
- [ ] Render colored border on each overlay window (GDI, WM_PAINT)
- [ ] Per-direction configurable colors in JSON config (left/right/up/down, ARGB hex strings)
- [ ] IOverlayRenderer interface with default colored-border implementation
- [ ] Dismiss overlays on CAPSLOCK key-up (destroy or hide overlay windows)
- [ ] Foreground window change detection — update overlay when foreground changes while CAPSLOCK held
- [ ] Overlay windows excluded from navigation (WS_EX_TOOLWINDOW + verification in WindowEnumerator)
- [ ] Clean daemon shutdown: destroy overlays, unhook keyboard hook, exit

### Add After Validation (v2.x)

- [ ] Per-strategy custom renderers — add once default renderer is stable and users express need for strategy-aware visualization
- [ ] Live overlay update on window layout change (new windows opened/closed while modifier held) — add if users report staleness as a real frustration
- [ ] Configurable overlay border thickness — add when users request; default thickness of 4-6px is sufficient for v2.0

### Future Consideration (v3+)

- [ ] Edge-matching strategy renderer that draws connecting lines between matching edges — visually explains the strategy's scoring logic
- [ ] Axis-only strategy renderer that draws a 1D axis line with candidate positions — explains the pure 1D scoring model
- [ ] Multi-monitor per-display overlay rendering adjustments (DPI-aware scaling of border thickness) — address if users on mixed-DPI setups report visual artifacts

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Daemon process with keyboard hook | HIGH | MEDIUM | P1 |
| CAPSLOCK hold detection | HIGH | LOW | P1 |
| Overlay windows (WS_EX_LAYERED + WS_EX_TOOLWINDOW) | HIGH | MEDIUM | P1 |
| Colored border rendering (GDI WM_PAINT) | HIGH | MEDIUM | P1 |
| Per-direction color config | HIGH | LOW | P1 |
| Overlay dismissal on key-up | HIGH | LOW | P1 |
| Foreground window change tracking | HIGH | LOW | P1 |
| Overlay exclusion from navigation candidates | HIGH | LOW | P1 |
| IOverlayRenderer interface | MEDIUM | LOW | P1 (enables pluggability without cost) |
| Activation delay (debounce) | MEDIUM | LOW | P1 |
| Default border renderer implementation | HIGH | MEDIUM | P1 |
| Clean daemon shutdown handling | MEDIUM | LOW | P1 |
| Per-strategy custom renderers | MEDIUM | HIGH | P2 |
| Configurable border thickness | LOW | LOW | P2 |
| Live overlay update on layout change | LOW | MEDIUM | P2 |
| DPI-aware border thickness scaling | LOW | MEDIUM | P3 |
| Edge-matching visual renderer | LOW | HIGH | P3 |
| Axis-only visual renderer | LOW | HIGH | P3 |

**Priority key:**
- P1: Must have for v2.0 launch
- P2: Should have, add when possible in v2.x
- P3: Nice to have, future milestone

---

## Competitor Feature Analysis

How overlay preview is handled in comparable tools:

| Feature | PowerToys Shortcut Guide | Komorebi | FancyZones | This Tool (v2.0 plan) |
|---------|--------------------------|----------|------------|----------------------|
| Trigger mechanism | Hold Win key (900ms default) | Not applicable (borders always on) | Hold Shift while dragging | Hold CAPSLOCK key |
| Visual style | Full-screen semi-transparent overlay with text | Colored border around managed windows | Zone highlight on drag hover | Colored border on target windows only |
| Per-direction indication | No (shows all shortcuts at once) | No (one focused/unfocused state) | No (highlights zone being hovered) | Yes — 4 simultaneous per-direction colored borders |
| Color configuration | Light/Dark theme only | Per-state color (focused/unfocused/stack/monocle) | Zone color configurable | Per-direction (left/right/up/down) |
| Click-through | Not applicable (blocks input) | Not applicable (real window borders) | Not applicable (drag mode) | Yes — WS_EX_TRANSPARENT |
| Taskbar/Alt+Tab presence | No (WS_EX_TOOLWINDOW) | No (not overlay windows) | No | No (WS_EX_TOOLWINDOW) |
| Activation delay | Configurable (default 900ms) | Always on | Shift held | Configurable (default ~150ms) |
| Pluggable renderer | No | No | No | Yes — IOverlayRenderer interface |
| Strategy-aware rendering | No | No | No | Planned (v2.x) |
| Persistent daemon required | Yes (PowerToys background process) | Yes (Komorebi service) | Yes (PowerToys background process) | Yes (new for v2.0) |

---

## Sources

- [PowerToys Shortcut Guide — Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/shortcut-guide) — HIGH confidence (official docs, updated 2025-08-20)
- [Extended Window Styles (WS_EX_TOOLWINDOW, WS_EX_LAYERED, WS_EX_TRANSPARENT, WS_EX_TOPMOST) — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) — HIGH confidence (official Win32 API reference)
- [SetLayeredWindowAttributes — Win32 docs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes) — HIGH confidence (official Win32 API reference)
- [Komorebi Borders docs](https://lgug2z.github.io/komorebi/common-workflows/borders.html) — HIGH confidence (official Komorebi docs; borders are per-state colored overlays on managed windows)
- [FancyZones — Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones) — HIGH confidence (zone highlight while Shift held = same hold-modifier preview pattern)
- [WH_KEYBOARD_LL hook — Medium overview](https://medium.com/@adarshpandey180/keylogging-with-wh-keyboard-and-wh-keyboard-ll-368926599395) — MEDIUM confidence (corroborates Win32 docs behavior for low-level keyboard hooks)
- [Click-through layered windows — Stack Overflow / Windows Hexerror](https://windows-hexerror.linestarve.com/q/so55202379-how-to-properly-allow-click-through-areas-of-transparent-sections-of-topmost-window) — MEDIUM confidence (community-verified Win32 behavior)
- [Hyprview — GitHub](https://github.com/yz778/hyprview) — MEDIUM confidence (directional border coloring from active/inactive window color as precedent for per-window color indication)
- [Hycov — GitHub](https://github.com/DreamMaoMao/hycov) — MEDIUM confidence (directional focus calculation in overview mode, nearest window per direction)
- [FancyWM — GitHub](https://github.com/FancyWM/fancywm) — HIGH confidence (directional focus via CLI, no overlay preview)

---
*Feature research for: overlay preview daemon for directional window focus navigation (Windows, v2.0 milestone)*
*Researched: 2026-02-28*
