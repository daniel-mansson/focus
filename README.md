# Focus

Keyboard-driven directional window navigation for Windows.

## What it does

- **Spatial navigation** -- Hold CAPSLOCK to see colored border overlays on nearby windows, press direction keys (arrows or WASD) to switch focus instantly
- **Chain moves** -- Keep holding CAPSLOCK and press multiple directions to navigate across your entire desktop without releasing
- **Window management** -- Move windows by grid steps (CAPS+TAB+direction), grow edges (CAPS+LSHIFT+direction), shrink edges (CAPS+LCTRL+direction) -- all grid-snapped with per-monitor calculation
- **Number keys** -- Press number keys while overlays are visible to jump directly to any numbered window
- **CLI mode** -- Also works as a stateless CLI (`focus left`) for scripting and external launchers
- **System tray** -- Runs in the background with a tray icon, right-click for settings UI, restart, or exit

## Installation

### Installer (recommended)

Download **Focus-Setup.exe** from the [latest release](https://github.com/daniel-mansson/focus/releases/latest) and run it.

- Per-user install -- no admin required for installation itself
- Self-contained -- no .NET runtime needed
- Optionally registers for startup via Task Scheduler (configurable during install)
- Upgrade by re-running the installer with a newer version

### Build from source

See [SETUP.md](SETUP.md) for build instructions. Requires .NET 8 SDK and Inno Setup.

## Quick start

After installing:

1. **Focus daemon starts automatically** -- it runs in the system tray
2. **Hold CAPSLOCK** to see overlay previews on candidate windows, then press arrow keys or WASD to navigate
3. **Right-click the tray icon** for settings, restart daemon, or exit
4. Configuration is stored in `%APPDATA%\focus\config.json`

## Hotkey reference

| Hotkey | Action |
|---|---|
| CAPS + arrow / WASD | Navigate to window in that direction |
| CAPS + number key | Jump to numbered window |
| CAPS + TAB + direction | Move window by one grid step |
| CAPS + LSHIFT + direction | Grow window edge outward |
| CAPS + LCTRL + direction | Shrink window edge inward |

All window management operations are grid-snapped with per-monitor grid calculation. Cross-monitor transitions are handled automatically.

## Configuration

Focus reads settings from `%APPDATA%\focus\config.json`. Run `focus --init-config` to create the default config file.

You can also configure everything through the **Settings UI** -- right-click the tray icon and select Settings.

### Scoring strategies

The scoring strategy determines which window gets focus when multiple candidates exist in a direction.

| Strategy | Description |
|---|---|
| `balanced` | Weights distance and alignment equally (default) |
| `strong-axis-bias` | Heavily favors alignment on the movement axis -- grid-like navigation |
| `closest-in-direction` | Picks the nearest window by center-to-center distance |
| `edge-matching` | Compares far edges of source and candidate windows |
| `edge-proximity` | Compares near edges facing the movement direction |
| `axis-only` | Pure 1D distance along the movement axis |

Use `focus --debug score <direction>` to compare how all strategies rank windows in your current layout.

### Other settings

| Setting | Description |
|---|---|
| `wrap` | Behavior when no window found: `no-op` (default), `wrap`, or `beep` |
| `exclude` | Glob patterns for process names to exclude from navigation |
| `gridFractionX` / `gridFractionY` | Grid divisions per monitor (default 16x12 for near-square cells on 16:9) |
| `overlayDelayMs` | Milliseconds before overlays appear after CAPSLOCK hold |

## CLI usage

```
focus <direction> [options]
```

Examples:

```bash
# Navigate focus to the right (uses config defaults)
focus right

# Use a specific strategy for this invocation
focus up --strategy closest-in-direction

# Enable wrap-around
focus left --wrap wrap

# Show all navigable windows
focus --debug enumerate

# Compare strategy scores for a direction
focus --debug score right

# Start the daemon
focus daemon
```

Run `focus --help` for the full reference.

## Requirements

- **Windows 10 or later**
- No .NET runtime needed if using the installer (self-contained)
- .NET 8 SDK required only for building from source

## License

[MIT](LICENSE)
