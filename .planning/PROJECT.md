# Window Focus Navigation

## What This Is

A lightweight C# command-line tool that enables directional window focus navigation on Windows, inspired by Hyprland's arrow-key-based window switching. Invoked via AutoHotkey hotkeys (e.g., `focus left`), it finds the best candidate window in the given direction and switches focus to it — bringing Hyprland-style spatial navigation to Windows without requiring a full tiling window manager.

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

### Active

- [ ] Background daemon mode (`focus daemon`) with low-level keyboard hook
- [ ] Detect CAPSLOCK held → show overlay, released → dismiss overlay
- [ ] Overlay renders colored borders on target windows (one per direction)
- [ ] Per-direction colors, configurable in JSON config
- [ ] Pluggable overlay renderer — default style (colored borders) plus per-strategy custom renderers
- [ ] Overlay windows excluded from navigation enumeration (WS_EX_TOOLWINDOW)
- [ ] Overlay updates per current active strategy

### Out of Scope

- Window tiling or layout management — this is focus navigation only
- GUI or system tray — CLI tool only
- Linux/macOS support — Windows-specific by design
- Window resizing or moving — only focus switching
- Background service/daemon mode — invoked per-call via hotkeys

## Current Milestone: v2.0 Overlay Preview

**Goal:** Add a persistent background daemon that renders directional overlay previews on target windows while the navigation modifier key (CAPSLOCK) is held.

**Target features:**
- Background daemon mode with low-level keyboard hook (WH_KEYBOARD_LL)
- Visual overlay showing which windows will be targeted per direction (colored borders)
- Pluggable renderer system — default overlay style plus per-strategy custom visualizations
- Per-direction configurable colors in existing JSON config

## Context

- Designed to be triggered by AutoHotkey hotkeys for seamless integration into existing workflows
- Windows' foreground activation restrictions require the Alt keypress workaround (keybd_event) before SetForegroundWindow
- DwmGetWindowAttribute with DWMWA_EXTENDED_FRAME_BOUNDS gives accurate visible bounds (unlike GetWindowRect which includes invisible borders on Windows 10+)
- Virtual screen coordinates handle multi-monitor naturally — no special per-monitor logic needed
- The weighting algorithm is the core UX differentiator and needs to be tunable:
  - **Balanced**: considers both distance and alignment roughly equally
  - **Strong axis bias**: heavily favors the movement direction axis
  - **Closest in direction**: nearest window in the general direction wins, even if off-axis
- v2.0: Daemon mode is a fundamental shift from stateless CLI — the process persists, manages a keyboard hook, and renders overlays. AHK still handles CAPSLOCK+Arrow → `focus <direction>` for actual navigation.

## Constraints

- **Runtime**: .NET 10 — in use since v1.0 (dev machine has .NET 10 available)
- **Performance**: Must complete in <100ms for hotkey responsiveness
- **Dependencies**: Minimal — Win32 API via P/Invoke only, no third-party native dependencies
- **Invocation**: Stateless CLI tool — no persistent process, no inter-process communication

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| C# with .NET 8 | Good Win32 interop via P/Invoke, fast startup with AOT potential, user preference | — Pending |
| JSON config file | Simple, human-editable, standard format for settings | — Pending |
| Configurable weighting strategies | Core UX needs tuning — different strategies suit different workflows | — Pending |
| Configurable wrap-around | User preference varies — some want wrap, some want boundary stop | — Pending |
| SetForegroundWindow + Alt keypress | Standard workaround for Windows foreground activation restriction | — Pending |

| Daemon mode for overlay preview | CAPSLOCK hold triggers visual preview — requires persistent process with keyboard hook | — Pending |
| Pluggable overlay renderers | Default renderer + per-strategy custom renderers — keeps visual system extensible | — Pending |

---
*Last updated: 2026-02-28 after milestone v2.0 start*
