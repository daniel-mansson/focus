# Feature Research

**Domain:** Directional window focus navigation tool (Windows desktop utility)
**Researched:** 2026-02-26
**Confidence:** MEDIUM-HIGH (core navigation features verified across multiple tools; Windows-specific nuances from official Win32 docs)

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Four-direction navigation (left/right/up/down) | Every tool from i3 to Komorebi to GlazeWM to AltSnap provides cardinal directions — it's the definition of the category | LOW | CLI args already planned: `focus left`, `focus right`, `focus up`, `focus down` |
| Filter out hidden/minimized/cloaked windows | Users only want to navigate to visible windows; navigating to an invisible window is disorienting | MEDIUM | DwmGetWindowAttribute DWMWA_CLOAKED needed; IsWindowVisible not sufficient on Windows 10+ |
| Accurate visible window bounds | Windows 10+ renders invisible drop-shadow borders; GetWindowRect lies by ~8px per side — wrong bounds produce wrong candidate scoring | MEDIUM | Use DWMWA_EXTENDED_FRAME_BOUNDS, not GetWindowRect |
| Multi-monitor support | Users with multiple monitors expect focus to cross monitor boundaries naturally — AltSnap, FancyWM, Komorebi, GlazeWM all support this | LOW | Virtual screen coordinates handle this automatically; no special per-monitor logic required |
| Sub-100ms response time | Hotkey tools must feel instantaneous; any perceptible lag breaks the muscle memory workflow | MEDIUM | Stateless CLI invocation avoids daemon overhead; .NET 8 AOT is viable path if needed |
| Configurable wrap-around behavior | i3 offers `yes/no/force/workspace`, Hyprland has `focus_wrapping`, GlazeWM wraps by default — every tiling WM exposes this — users have strong opinions | LOW | Already planned: `wrap / no-op / beep` options in JSON config |
| Meaningful exit codes | Script/hotkey integration requires knowing if a focus switch happened or failed — silent failure in a hotkey context is frustrating | LOW | Already planned: 0=switched, 1=no candidate, 2=error |
| Application exclude list | Desktop tools like task managers, overlays, and notification windows pollute the candidate list — every serious tool (Komorebi, FancyWM) has this | LOW | Already planned: user-configurable JSON exclude list |
| CLI flag overrides | Users want to test behavior or bind specific strategies to specific hotkeys without editing config | LOW | Already planned |
| Silent on success | Hotkey-triggered tools must produce no visible output on success — any popup, console flash, or notification is user-hostile | LOW | Already planned: silent by default |

### Differentiators (Competitive Advantage)

Features that set the product apart. Not required, but valued.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Multiple named weighting strategies | The directional focus algorithm is the core UX. Tools like AltSnap and right-window use fixed geometric algorithms; Hyprland/i3 use tree-based approaches. Exposing `balanced`, `strong-axis-bias`, and `closest-in-direction` strategies lets users tune to their mental model | MEDIUM | Planned as the core differentiator. Allows adapting to different physical layouts (ultrawide, grid of windows, stacked terminal panes) |
| Verbose/debug mode with scored candidate list | No existing tool in this space exposes its scoring logic. Users troubleshooting "why did it pick that window?" currently have no recourse — they have to infer from behavior | LOW | `--verbose` flag logging window enumeration, score per candidate, and winner selection is a significant DX win |
| Predictable geometric algorithm (not Z-order or tree-based) | right-window's issue tracker documents user frustration with Z-order/history-based fallbacks. An algorithm that always picks the visually closest window (no hidden state) is inherently more predictable | MEDIUM | Key to adoption: users must be able to develop muscle memory. See right-window issue #1 discussion on edge-following algorithm |
| .NET 8 native binary potential (AOT) | Competing tools like AltSnap (C/C++), bug.n (AHK), Komorebi (Rust) have sub-20ms startup; .NET 8 AOT can match this for CLI tools | MEDIUM | Not required for v1 (JIT is fast enough), but relevant for power users noticing any warmup lag |
| JSON config (human-readable, diff-able) | Most competing Windows tools use INI or registry (AltSnap uses INI, FancyWM uses its own format). JSON is more composable and integrates with dotfiles workflows | LOW | Already planned |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem good but create problems.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| GUI/System tray | Users familiar with AltSnap or FancyWM may expect a systray icon for configuration | Contradicts the core design: this is a stateless CLI tool invoked per-hotkey. A GUI adds persistent process, IPC complexity, and maintenance burden entirely orthogonal to the focus navigation value | Rely on a regular text editor for JSON config. Verbose flag gives immediate feedback |
| Background daemon/service mode | Reduces per-invocation startup overhead; enables features like focus-follows-mouse | Converts a simple stateless tool into a system service with install/uninstall lifecycle, crash recovery, IPC protocol, and elevated privilege concerns. Startup latency on .NET 8 is already acceptable for hotkey use | If startup latency proves a real problem in benchmarks, target AOT compilation instead |
| Window tiling/layout management | Natural extension if users already have a hotkey setup for focus | Entirely different problem domain. Tiling requires persistent state (layout tree), automatic re-layout on window open/close, and deep Win32 hooks. This scope expansion risks the core focus algorithm quality | Recommend pairing with FancyWM, Komorebi, or GlazeWM for layout; this tool handles navigation only |
| Focus-follows-mouse | Requested by users coming from Linux tiling WMs (i3, Hyprland support it; GlazeWM supports it) | Requires a persistent background process with mouse hook. Incompatible with stateless CLI design. Changes the interaction model from intentional hotkey to implicit cursor-tracking | Achievable via separate AHK script watching WM_MOUSEMOVE if the user wants it |
| Virtual desktop awareness (filter to current desktop only) | Users with multiple virtual desktops may want navigation to stay on the active desktop | IVirtualDesktopManager COM interface is undocumented and brittle — Microsoft changes its internal API between Windows builds, breaking tools that depend on it (documented pattern in komorebi and bug.n) | Document the limitation. Users can work around it with AHK's `WinGet` filtering if critical. Track the API stability before committing |
| Window title/class-based focus (e.g., focus to "the VS Code window") | Requested by keyboard power users who want to jump to a specific named app | This is a different tool category (window switcher by name, not by direction). Combining the two muddies the model and creates conflicting behavior when both a named window and a directional candidate exist | Recommend AutoHotkey `WinActivate` for name-based jumping; keep this tool strictly directional |
| Remember last focus history per direction | Hyprland tracks focus history per-workspace; right-window uses Z-order as a tiebreaker | History-based tiebreaking is unpredictable from the user's visual perspective (right-window issue #1 documents this frustration explicitly). Hidden state = unreproducible behavior | Use geometric tiebreaking (Z-order only as last resort when windows are exactly equidistant) |

## Feature Dependencies

```
[Window enumeration (EnumWindows)]
    └──required by──> [Window candidate filtering (hidden/minimized/cloaked)]
                          └──required by──> [Directional candidate scoring]
                                                └──required by──> [Focus switching (SetForegroundWindow)]

[Accurate bounds (DWMWA_EXTENDED_FRAME_BOUNDS)]
    └──required by──> [Directional candidate scoring]

[JSON config file]
    └──enables──> [Default strategy selection]
    └──enables──> [Default wrap-around behavior]
    └──enables──> [Application exclude list]

[CLI flag overrides]
    └──enhances──> [JSON config file] (per-invocation override without editing config)

[Verbose/debug flag]
    └──enhances──> [Directional candidate scoring] (exposes internals for debugging)

[Multi-monitor (virtual screen coordinates)]
    └──required by──> [Directional candidate scoring] (coordinates must be in same space)

[SetForegroundWindow + Alt keypress workaround]
    └──required by──> [Focus switching] (Windows foreground lock bypass)
```

### Dependency Notes

- **Window enumeration requires accurate bounds:** Filtering candidates by direction requires correct visible rectangles. Getting enumeration right before scoring is a strict prerequisite.
- **JSON config enhances all behavioral options:** Wrap-around, strategy, and exclude list are meaningless without a persistence mechanism. Config file must land in the same phase as these features.
- **Alt keypress workaround is non-negotiable:** SetForegroundWindow alone silently fails when the calling process doesn't own the foreground lock. The workaround (keybd_event ALT) is required for any focus switch to actually work — this is not optional polish.
- **Virtual screen coordinates simplify multi-monitor:** Using DwmGetWindowAttribute returns coordinates in virtual screen space already; no per-monitor translation needed. This makes multi-monitor a near-zero-cost feature.

## MVP Definition

### Launch With (v1)

Minimum viable product — what's needed to validate the concept.

- [ ] Enumerate visible windows, filter hidden/minimized/cloaked — core correctness
- [ ] Get accurate visible bounds via DWMWA_EXTENDED_FRAME_BOUNDS — required for correct scoring
- [ ] Directional candidate scoring with at least one working strategy (balanced) — the core value proposition
- [ ] SetForegroundWindow + Alt keypress workaround — required for focus switches to actually complete
- [ ] Multi-monitor support via virtual screen coordinates — near-zero cost, expected by any modern user
- [ ] JSON config (strategy, wrap behavior, exclude list) — required for basic usability
- [ ] CLI direction argument and flag overrides — required for AutoHotkey integration
- [ ] Configurable wrap-around behavior (wrap / no-op / beep) — user preference is strong here
- [ ] Meaningful exit codes — required for hotkey script integration
- [ ] Silent by default, verbose/debug flag — essential for troubleshooting without polluting hotkey output

### Add After Validation (v1.x)

Features to add once core is working.

- [ ] Additional weighting strategies (`strong-axis-bias`, `closest-in-direction`) — add once `balanced` is validated and users report specific layout frustrations
- [ ] Expanded exclude list patterns (regex, wildcard matching) — add when users report specific apps that can't be excluded with exact name matching
- [ ] Application exclude list by window class (not just process name) — add if exact process name matching proves insufficient for UWP apps with shared host process

### Future Consideration (v2+)

Features to defer until product-market fit is established.

- [ ] .NET 8 AOT native binary — defer until startup latency is measured as a real problem (benchmark first)
- [ ] Virtual desktop awareness — defer until IVirtualDesktopManager API stabilizes or a stable community wrapper emerges; currently brittle across Windows builds
- [ ] Custom weighting parameters (numeric weights via config) — defer; named strategies cover the common cases and are more user-friendly than raw weight tuning

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Directional candidate scoring (balanced strategy) | HIGH | MEDIUM | P1 |
| Window enumeration + filtering (hidden/cloaked) | HIGH | MEDIUM | P1 |
| Accurate bounds (DWMWA_EXTENDED_FRAME_BOUNDS) | HIGH | LOW | P1 |
| SetForegroundWindow + Alt keypress workaround | HIGH | LOW | P1 |
| Multi-monitor via virtual screen coordinates | HIGH | LOW | P1 |
| Configurable wrap-around behavior | HIGH | LOW | P1 |
| JSON config file | HIGH | LOW | P1 |
| CLI argument + flag overrides | HIGH | LOW | P1 |
| Exit codes + silent default | HIGH | LOW | P1 |
| Verbose/debug mode with scored candidates | MEDIUM | LOW | P1 |
| Additional weighting strategies | MEDIUM | MEDIUM | P2 |
| Application exclude list (process name) | MEDIUM | LOW | P1 |
| Application exclude list (window class) | LOW | MEDIUM | P2 |
| .NET 8 AOT compilation | LOW | MEDIUM | P3 |
| Virtual desktop awareness | MEDIUM | HIGH | P3 |
| Custom numeric weight parameters in config | LOW | LOW | P3 |

**Priority key:**
- P1: Must have for launch
- P2: Should have, add when possible
- P3: Nice to have, future consideration

## Competitor Feature Analysis

| Feature | Hyprland / i3 / sway (Linux) | Komorebi / GlazeWM (Windows tiling) | AltSnap (Windows utility) | right-window (X11 utility) | Our Approach |
|---------|-------------------------------|---------------------------------------|---------------------------|----------------------------|--------------|
| Directional focus (4 directions) | Yes — `movefocus l/r/u/d` (Hyprland), `focus left/right/up/down` (i3) | Yes — `komorebic focus left/right/up/down`; `focus --direction left` (GlazeWM) | Yes — FocusL/FocusT/FocusR/FocusB (config actions) | Yes — `-f left/right/up/down` | Yes — `focus left/right/up/down` CLI arg |
| Focus algorithm | Tree-based (layout-aware) | Tree-based (layout-aware) | Unclear (geometry-based assumed) | Geometric: filter by direction, prioritize overlap, nearest border, Z-order tiebreak | Geometry-based with multiple named strategies; predictable and layout-agnostic |
| Multiple strategies | No (single algorithm per WM) | No (single algorithm per WM) | No | No | Yes — `balanced`, `strong-axis-bias`, `closest-in-direction` (differentiator) |
| Wrap-around config | Yes (i3: yes/no/force/workspace; Hyprland: focus_wrapping) | Yes (Komorebi: cross_boundary_behaviour; GlazeWM: wraps by default) | Unknown | No configurable wrap | Yes — wrap / no-op / beep |
| Multi-monitor | Yes (virtual screen / workspace per monitor) | Yes (monitor-aware boundary crossing) | Yes | Yes | Yes — virtual screen coordinates |
| Application exclude | Yes (window rules by class/title) | Yes (window rules) | Yes (blacklist) | No | Yes — JSON config exclude list |
| Verbose/debug mode | No user-facing debug output | No user-facing debug output | No | No | Yes — scores all candidates to stderr |
| Floating window handling | Tiling WMs: complex floating/tiling distinction | Tiling WMs: managed floating | N/A | No special handling | Windows floating-only: all windows treated as floating |
| Standalone / no daemon | No — requires running WM | No — requires running WM | Yes (systray persistent) | Yes — stateless binary | Yes — stateless CLI, no daemon |
| AutoHotkey integration | Via IPC (i3-msg, hyprctl) | Via CLI (komorebic, GlazeWM CLI) | Via config + keyboard | Via shell | Via CLI — direct AHK RunWait/Run |
| Config format | Text (HCL-like / i3 config) | YAML / JSON | INI | None | JSON |

## Sources

- [Hyprland movefocus dispatcher issue #2321](https://github.com/hyprwm/Hyprland/issues/2321) — MEDIUM confidence (community discussion)
- [Hyprland focus wrapping issue #1312](https://github.com/hyprwm/Hyprland/issues/1312) — MEDIUM confidence
- [Hyprland Dispatchers Wiki](https://wiki.hypr.land/Configuring/Dispatchers/) — HIGH confidence (official docs)
- [i3 User's Guide](https://i3wm.org/docs/userguide.html) — HIGH confidence (official docs)
- [Komorebi Focusing Windows docs](https://lgug2z.github.io/komorebi/usage/focusing-windows.html) — HIGH confidence (official docs)
- [GlazeWM GitHub](https://github.com/glzr-io/glazewm) — HIGH confidence (official repo)
- [GlazeWM cheatsheet](https://www.nulldocs.com/windows/glazewm-cheatsheet/) — MEDIUM confidence
- [AltSnap GitHub](https://github.com/RamonUnch/AltSnap) — HIGH confidence (official repo)
- [right-window GitHub](https://github.com/ntrrgc/right-window) — HIGH confidence (official repo)
- [right-window algorithm discussion issue #1](https://github.com/ntrrgc/right-window/issues/1) — MEDIUM confidence (community)
- [FancyWM GitHub](https://github.com/FancyWM/fancywm) — HIGH confidence (official repo)
- [bug.n GitHub](https://github.com/fuhsjr00/bug.n) — HIGH confidence (official repo)
- [SetForegroundWindow Win32 docs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) — HIGH confidence (Microsoft official)
- [Windows WICG Spatial Navigation algorithm](https://github.com/WICG/spatial-navigation/wiki/Heuristic-focus-navigation-algorithm-in-blink) — HIGH confidence (W3C WICG spec)

---
*Feature research for: directional window focus navigation (Windows)*
*Researched: 2026-02-26*
