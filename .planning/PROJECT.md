# Window Focus Navigation

## What This Is

A lightweight C# command-line tool that enables directional window focus navigation on Windows, inspired by Hyprland's arrow-key-based window switching. Invoked via AutoHotkey hotkeys (e.g., `focus left`), it finds the best candidate window in the given direction and switches focus to it — bringing Hyprland-style spatial navigation to Windows without requiring a full tiling window manager.

## Core Value

Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Enumerate visible windows via Win32 EnumWindows API
- [ ] Filter out hidden, cloaked, and minimized windows
- [ ] Get accurate window positions using DwmGetWindowAttribute (visible bounds)
- [ ] Accept direction argument (left, right, up, down) via CLI
- [ ] Calculate weighted distances from current window's center point to candidates
- [ ] Support multiple weighting strategies (balanced, strong-axis-bias, closest-in-direction)
- [ ] Switch focus using SetForegroundWindow with simulated Alt keypress (keybd_event) to bypass foreground restrictions
- [ ] Support multi-monitor setups via virtual screen coordinates
- [ ] Configurable wrap-around behavior when no window found in direction (wrap / no-op / beep)
- [ ] JSON config file for default settings (strategy, wrap behavior, exclude list)
- [ ] CLI flags to override config per invocation
- [ ] User-configurable app exclude list in config file
- [ ] Silent by default — no output on success
- [ ] Verbose/debug flag to log window enumeration, scores, and chosen target
- [ ] Meaningful exit codes (0=switched, 1=no candidate, 2=error)
- [ ] Target .NET 8 LTS

### Out of Scope

- Window tiling or layout management — this is focus navigation only
- GUI or system tray — CLI tool only
- Linux/macOS support — Windows-specific by design
- Window resizing or moving — only focus switching
- Background service/daemon mode — invoked per-call via hotkeys

## Context

- Designed to be triggered by AutoHotkey hotkeys for seamless integration into existing workflows
- Windows' foreground activation restrictions require the Alt keypress workaround (keybd_event) before SetForegroundWindow
- DwmGetWindowAttribute with DWMWA_EXTENDED_FRAME_BOUNDS gives accurate visible bounds (unlike GetWindowRect which includes invisible borders on Windows 10+)
- Virtual screen coordinates handle multi-monitor naturally — no special per-monitor logic needed
- The weighting algorithm is the core UX differentiator and needs to be tunable:
  - **Balanced**: considers both distance and alignment roughly equally
  - **Strong axis bias**: heavily favors the movement direction axis
  - **Closest in direction**: nearest window in the general direction wins, even if off-axis

## Constraints

- **Runtime**: .NET 8 LTS — widely supported, good P/Invoke support
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

---
*Last updated: 2026-02-26 after initialization*
