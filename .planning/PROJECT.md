# Window Focus Navigation

## What This Is

A lightweight C# tool enabling keyboard-driven directional window focus navigation and grid-snapped window management on Windows, inspired by Hyprland's spatial window switching. In daemon mode (`focus daemon`), holding CAPSLOCK shows colored border overlays on candidate windows, and pressing direction keys (arrows or WASD) instantly switches focus — with overlay chaining for sequential moves and number keys for direct window selection. CAPS+TAB+direction moves windows by grid steps, CAPS+LSHIFT grows edges, CAPS+LCTRL shrinks edges — all with per-monitor grid calculation and cross-monitor transitions. System tray icon with live status context menu, daemon restart, and a WinForms settings UI for all configuration values. Also works as a stateless CLI (`focus left`) for scripting and external launchers.

## Core Value

Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## Requirements

### Validated

<!-- Shipped and confirmed valuable (v1.0). -->

- ✓ Enumerate visible windows via Win32 EnumWindows API — v1.0
- ✓ Filter out hidden, cloaked, and minimized windows — v1.0
- ✓ Get accurate window positions using DwmGetWindowAttribute (visible bounds) — v1.0
- ✓ Accept direction argument (left, right, up, down) via CLI — v1.0
- ✓ Calculate weighted distances from current window's center point to candidates — v1.0
- ✓ Support multiple weighting strategies (balanced, strong-axis-bias, closest-in-direction, edge-matching, edge-proximity, axis-only) — v1.0
- ✓ Switch focus using SetForegroundWindow with SendInput Alt bypass — v1.0
- ✓ Support multi-monitor setups via virtual screen coordinates — v1.0
- ✓ Configurable wrap-around behavior when no window found in direction (wrap / no-op / beep) — v1.0
- ✓ JSON config file for default settings (strategy, wrap behavior, exclude list) — v1.0
- ✓ CLI flags to override config per invocation — v1.0
- ✓ User-configurable app exclude list in config file — v1.0
- ✓ Silent by default — no output on success — v1.0
- ✓ Verbose/debug flag to log window enumeration, scores, and chosen target — v1.0
- ✓ Meaningful exit codes (0=switched, 1=no candidate, 2=error) — v1.0

<!-- Shipped and confirmed valuable (v2.0). -->

- ✓ Background daemon mode (`focus daemon`) with low-level keyboard hook — v2.0
- ✓ Detect CAPSLOCK held → show overlay, released → dismiss overlay — v2.0
- ✓ Overlay renders colored borders on target windows (one per direction, all four simultaneously) — v2.0
- ✓ Per-direction colors, configurable in JSON config (hex ARGB) — v2.0
- ✓ Pluggable overlay renderer with IOverlayRenderer interface and config-driven selection — v2.0
- ✓ Overlay windows excluded from navigation enumeration (WS_EX_TOOLWINDOW) — v2.0
- ✓ Overlay repositions on foreground window change while modifier held — v2.0
- ✓ Single-instance daemon with replace semantics (kills existing on restart) — v2.0
- ✓ Sleep/wake recovery for keyboard hook and CAPSLOCK state — v2.0
- ✓ Configurable activation delay (overlayDelayMs) to suppress accidental taps — v2.0

<!-- Shipped and confirmed valuable (v3.0). -->

- ✓ Daemon detects CAPSLOCK + direction keys (arrows + WASD) and performs focus navigation directly — v3.0
- ✓ Direction keys suppressed from reaching focused app while CAPSLOCK held — v3.0
- ✓ Overlay stays visible during chained moves, refreshing candidates from new foreground window — v3.0
- ✓ Stateless CLI (`focus <direction>`) continues to work alongside daemon hotkeys — v3.0

<!-- Shipped and confirmed valuable (v3.1). -->

- ✓ Move foreground window by grid steps via CAPS+TAB+direction — v3.1
- ✓ Grow window edge outward by grid steps via CAPS+LSHIFT+direction — v3.1
- ✓ Shrink window edge inward by grid steps via CAPS+LCTRL+direction — v3.1
- ✓ Per-monitor grid calculation with configurable fractions (gridFractionX/Y) — v3.1
- ✓ Smart snap-to-grid with configurable tolerance (snapTolerancePercent) — v3.1
- ✓ Cross-monitor window transitions with automatic grid recalculation — v3.1
- ✓ Mode-specific overlay indicators (amber Move arrows, cyan Grow arrows) — v3.1
- ✓ Normal TAB behavior preserved when CAPS not held — v3.1

<!-- Shipped and confirmed valuable (v4.0). -->

- ✓ Custom tray icon (generated .ico, multi-size, embedded as assembly resource) — v4.0
- ✓ Enhanced right-click context menu (live status labels, settings, restart, exit) — v4.0
- ✓ WinForms settings window (strategy, grid fractions, overlay colors/timing) — v4.0
- ✓ About section in settings (name, version, attribution, GitHub link) — v4.0
- ✓ Daemon status display (hook status, uptime, last action) — v4.0
- ✓ Daemon restart from context menu — v4.0

### Active

<!-- Current scope: v5.0 Installer -->

- [ ] Inno Setup installer with install/uninstall
- [ ] Task Scheduler startup registration (user-choice elevated or standard)
- [ ] Install path selection (default: %LocalAppData%\Focus)
- [ ] Clean uninstall (remove scheduled task, files, registry)

## Current Milestone: v5.0 Installer

**Goal:** Package Focus as a proper installable application with clean install/uninstall and optional startup registration via Task Scheduler.

**Target features:**
- Inno Setup installer producing a single .exe
- Task Scheduler registration for daemon autostart at logon
- User chooses install path (default: %LocalAppData%\Focus) and whether to run elevated
- Clean uninstall removing all artifacts (files, scheduled task)

## Current State

Shipped v4.0 (System Tray & Settings UI) on 2026-03-05. All 5 milestones complete (v1.0 through v4.0), 15 phases, 26 plans. Starting v5.0 Installer.

### Out of Scope

- Window tiling or layout management — move/resize are primitives, not layouts
- Linux/macOS support — Windows-specific by design (Win32 API)
- Full GUI application — daemon uses minimal WinForms (tray icon + settings form); no main window or dashboard
- Animated overlay transitions (fade in/out) — tested and rejected; instant show/hide feels better
- Window title/content preview in overlay — DwmRegisterThumbnail complexity; colored border at window position IS the preview
- Interactive/clickable overlay elements — conflicts with WS_EX_TRANSPARENT click-through; navigation is keyboard-only
- Diagonal resize (both axes simultaneously) — breaks one-edge-per-direction model; chain two operations instead
- Pixel-exact move (no grid) — defeats grid discipline; mouse exists for pixel control
- Automatic grid enforcement on focus change — hostile to manually positioned windows; snap is always user-initiated

## Context

Shipped v4.0 with ~14,000 LOC C# across ~35 source files.
Tech stack: .NET 8 (net8.0-windows), CsWin32 0.3.269 for P/Invoke, WinForms (message pump + tray icon + settings form), GDI for overlay rendering.
Two modes: stateless CLI (`focus <direction>`) for scripting, persistent daemon (`focus daemon`) for hotkey navigation + overlay previews + window management.
Six weighting strategies: balanced, strong-axis-bias, closest-in-direction, edge-matching, edge-proximity, axis-only.
Daemon handles full navigation flow: CAPSLOCK + direction keys, overlay chaining, CAPS+number window selection.
Window management: CAPS+TAB+direction (move), CAPS+LSHIFT+direction (grow), CAPS+LCTRL+direction (shrink) — all grid-snapped with per-monitor calculation.
Cross-monitor transitions with adjacent monitor detection and automatic grid recalculation.
System tray with custom icon, live status context menu, WinForms settings UI, and daemon restart capability.
AutoHotkey dependency eliminated for core navigation — daemon handles everything natively.

## Constraints

- **Runtime**: .NET 8 (net8.0-windows) — WinForms required for daemon message pump
- **Performance**: Must complete in <100ms for hotkey responsiveness
- **Dependencies**: Minimal — Win32 API via CsWin32 P/Invoke, System.CommandLine, FileSystemGlobbing
- **Invocation**: CLI tool + optional persistent daemon for overlay preview

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| C# with .NET 8 | Good Win32 interop via P/Invoke, fast startup with AOT potential, user preference | ✓ Good — CsWin32 interop works well, 22 files / 3.3k LOC is compact |
| JSON config file | Simple, human-editable, standard format for settings | ✓ Good — clean separation of defaults, file, and CLI overrides |
| Configurable weighting strategies | Core UX needs tuning — different strategies suit different workflows | ✓ Good — six strategies shipped, user can switch per workflow |
| Configurable wrap-around | User preference varies — some want wrap, some want boundary stop | ✓ Good — wrap/no-op/beep all work |
| SetForegroundWindow + Alt keypress | Standard workaround for Windows foreground activation restriction | ✓ Good — reliable from AHK invocation |
| Daemon mode for overlay preview | CAPSLOCK hold triggers visual preview — requires persistent process with keyboard hook | ✓ Good — daemon is stable, hook survives sleep/wake |
| Pluggable overlay renderers | Default renderer + per-strategy custom renderers — keeps visual system extensible | ✓ Good — IOverlayRenderer interface clean, config-driven selection works |
| WH_KEYBOARD_LL + CAPSLOCK suppression | System-wide hook intercepts CAPSLOCK before it toggles LED state | ✓ Good — no CAPSLOCK toggle flicker, LLKHF_INJECTED filter prevents AHK interference |
| Win32 GDI layered windows for overlays | UpdateLayeredWindow + premultiplied alpha DIB — no WPF/WinUI dependency | ✓ Good — lightweight, accurate positioning, click-through |
| Fade animation removed (instant show/hide) | User tested both, preferred instant transitions over 100ms fade | ✓ Good — eliminated all timer/alpha machinery, simpler code |
| Replace semantics for daemon mutex | Kill existing daemon on restart rather than error — smoother UX | ✓ Good — user never sees "already running" errors |
| Fresh config load per keypress | Runtime config changes without daemon restart | ✓ Good — zero-restart config workflow |
| STA thread navigation dispatch | All Win32 APIs run on STA thread via Invoke | ✓ Good — consistent with overlay dispatch pattern |
| Silent no-op on no candidates | No beep/log when no window in direction | ✓ Good — clean, unobtrusive experience |
| CAPS+number overlay labels | GDI text rendering on layered windows with alpha fixup | ✓ Good — position-stable numbering across navigation |
| Overlay chaining via existing architecture | No new code needed — existing show/refresh already chains | ✓ Good — v2.0 architecture already supported it |
| Dual-rect coordinate pattern | GetWindowRect for SetWindowPos inputs; DwmGetWindowAttribute for overlay positioning only | ✓ Good — prevents border offset errors across all move/resize operations |
| GridCalculator Win32-free | Takes explicit int params, no RECT structs — trivially testable | ✓ Good — clean separation of math from platform |
| Per-axis grid fractions (16x12) | Separate GridFractionX/Y for near-square cells on 16:9 monitors | ✓ Good — grid cells feel uniform across aspect ratios |
| Directional snap variants (Floor/Ceiling) | NearestGridLineFloor for leftward/upward, Ceiling for rightward/downward | ✓ Good — grow/shrink always expand/contract correctly |
| Maximized window guard (refuse) | IsZoomed returns silently — never restore before moving | ✓ Good — prevents unexpected window state changes |
| rcMonitor vs rcWork separation | rcMonitor for adjacency detection, rcWork for placement math | ✓ Good — cross-monitor transitions avoid taskbar overlap |
| DIB pixel-write arrow renderer | Bounding-box scan + cross-product sign test for filled triangles | ✓ Good — no GDI path dependency, consistent premultiplied alpha |
| Mode-at-event-time pattern | WindowMode derived from snapshot of _tabHeld + modifiers at direction keydown | ✓ Good — eliminates race condition between modifier release and event processing |
| Hand-written ICO encoder with PNG frames | 30-line BinaryWriter — zero new dependencies for icon generation | ✓ Good — produces valid multi-size ICO with full ARGB transparency |
| EmbeddedResource with LogicalName | Eliminates namespace-prefix guessing for GetManifestResourceStream | ✓ Good — clean runtime icon load |
| DaemonStatus as plain mutable STA-thread-only class | All reads/writes on STA thread — no locking needed | ✓ Good — simple, no concurrency bugs |
| ContextMenuStrip.Opening for live status | Refresh labels on every menu open | ✓ Good — no stale data ever displayed |
| FlowLayoutPanel for settings form | Better DPI scaling than absolute pixel positioning | ✓ Good — handles multi-DPI setups |
| Atomic config save (File.Replace) | Temp-file swap prevents parse errors during mid-write | ✓ Good — daemon keypress during save never fails |

---
*Last updated: 2026-03-05 after v5.0 milestone start*
