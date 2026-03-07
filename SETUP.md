# Focus — Developer Setup Guide

For end-user installation, see [README.md](README.md). This guide covers building from source and contributing.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Build](#build)
- [Building the Installer](#building-the-installer)
- [Running](#running)
- [AutoHotkey Integration (optional)](#autohotkey-integration-optional)
- [Configuration](#configuration)
- [CLI Reference](#cli-reference)
- [Scoring Strategies](#scoring-strategies)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

- **Windows 10 or later**
- **.NET 8 SDK or later** — [download from Microsoft](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Git** — to clone the repository
- **Inno Setup 6** (optional) — only needed to build the installer. [Download from jrsoftware.org](https://jrsoftware.org/isdl.php)

Verify your .NET installation before building:

```
dotnet --version
```

Any version 8.0 or higher is fine.

---

## Build

Clone the repository and build in Release configuration:

```
git clone https://github.com/daniel-mansson/focus.git
cd focus
dotnet build focus/focus.csproj -c Release
```

The compiled executable is at:

```
focus/bin/Release/net8.0-windows/focus.exe
```

**Self-contained publish (no .NET runtime needed on target machine):**

```
dotnet publish focus/focus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output location:

```
focus/bin/Release/net8.0-windows/win-x64/publish/focus.exe
```

This bundles the .NET runtime into a single executable. No runtime installation required on the target machine.

---

## Building the Installer

The `build.ps1` script handles the full build pipeline — publish + Inno Setup installer:

```powershell
.\build.ps1
```

This produces `installer/output/Focus-Setup.exe`. Requires Inno Setup 6 with `ISCC.exe` in your PATH.

To add Inno Setup to PATH after installing:

```powershell
$p = [Environment]::GetEnvironmentVariable("Path", "User")
[Environment]::SetEnvironmentVariable("Path", "$p;C:\Program Files (x86)\Inno Setup 6", "User")
```

Restart your terminal after updating PATH.

---

## Running

**Daemon mode (primary):**

```
focus daemon --background
```

This starts Focus in the background with a system tray icon. Hold CAPSLOCK to see overlays, press direction keys to navigate. Right-click the tray icon for settings.

**CLI mode (single invocation):**

```
focus left
focus right --strategy strong-axis-bias
```

Useful for scripting or external launchers.

---

## AutoHotkey Integration (optional)

If you prefer custom hotkey bindings instead of the built-in CAPSLOCK daemon, you can use AutoHotkey v2. [Download from autohotkey.com](https://www.autohotkey.com/) (v2, not v1).

### Basic script — Win+Arrow keys

```ahk
#Requires AutoHotkey v2.0

; Win + Arrow keys for directional focus navigation
#Left::Run("focus left", , "Hide")
#Right::Run("focus right", , "Hide")
#Up::Run("focus up", , "Hide")
#Down::Run("focus down", , "Hide")
```

The `"Hide"` parameter suppresses the console window flash that would otherwise appear briefly each time focus.exe runs.

### Using an absolute path (if focus.exe is not in PATH)

```ahk
#Requires AutoHotkey v2.0

focusExe := "C:\Work\focus\focus\bin\Release\net8.0-windows\focus.exe"

#Left::Run(focusExe . " left", , "Hide")
#Right::Run(focusExe . " right", , "Hide")
#Up::Run(focusExe . " up", , "Hide")
#Down::Run(focusExe . " down", , "Hide")
```

### Advanced example — per-binding strategy override

```ahk
#Requires AutoHotkey v2.0

; Win + Arrow: balanced (default)
#Left::Run("focus left", , "Hide")
#Right::Run("focus right", , "Hide")
#Up::Run("focus up", , "Hide")
#Down::Run("focus down", , "Hide")

; Win + Shift + Arrow: strong-axis-bias (good for tiled layouts)
#+Left::Run("focus left --strategy strong-axis-bias", , "Hide")
#+Right::Run("focus right --strategy strong-axis-bias", , "Hide")
#+Up::Run("focus up --strategy strong-axis-bias", , "Hide")
#+Down::Run("focus down --strategy strong-axis-bias", , "Hide")
```

### Auto-start with Windows

To have the script start automatically when you log in:

1. Press Win+R, type `shell:startup`, and press Enter
2. The Startup folder opens in Explorer
3. Create a shortcut to your `.ahk` file and place it in that folder

---

## Configuration

Focus reads settings from `%APPDATA%\focus\config.json`. The config is optional — if it does not exist, Focus uses built-in defaults (balanced strategy, no-op wrap, no exclusions).

**Create the default config:**

```
focus --init-config
```

You can also configure everything through the **Settings UI** — right-click the tray icon and select Settings.

**Default config.json:**

```json
{
  "strategy": "balanced",
  "wrap": "no-op",
  "exclude": []
}
```

**Config fields:**

| Field | Type | Values | Default | Description |
|---|---|---|---|---|
| `strategy` | string | `balanced`, `strong-axis-bias`, `closest-in-direction`, `edge-matching`, `edge-proximity`, `axis-only` | `balanced` | Default scoring strategy for direction navigation |
| `wrap` | string | `no-op`, `wrap`, `beep` | `no-op` | Behavior when no window is found in the requested direction |
| `exclude` | array | glob patterns | `[]` | Process names to exclude from window enumeration |

**Wrap behavior:**

- `no-op` — do nothing when no candidate exists in that direction (default)
- `wrap` — cycle to the window at the opposite edge of the screen
- `beep` — play the system beep sound when no candidate is found

**Exclude patterns:**

The `exclude` array contains glob patterns matched against the process name (without `.exe`). For example:

```json
{
  "strategy": "balanced",
  "wrap": "no-op",
  "exclude": ["explorer", "Teams", "Slack*"]
}
```

This would exclude Windows Explorer, Microsoft Teams, and any process whose name starts with "Slack" from appearing as navigation candidates.

**CLI overrides:**

CLI flags override config file values for a single invocation and do not modify the config file. For example:

```
focus right --strategy closest-in-direction --wrap beep
```

This uses closest-in-direction and beep for that call only, regardless of what config.json contains.

The `--exclude` flag replaces the config exclude list entirely (it does not merge with it):

```
focus left --exclude "notepad" "calc"
```

---

## CLI Reference

```
focus <command> [options]
```

**Commands:**

| Command | Description |
|---|---|
| `<direction>` | Navigate focus in that direction (`left`, `right`, `up`, `down`) |
| `daemon` | Start the daemon with CAPSLOCK hotkeys and system tray |
| `daemon --background` | Start the daemon in background mode (no console window) |
| `--init-config` | Write a default config.json to `%APPDATA%\focus\config.json` |
| `--debug enumerate` | List all detected navigable windows with bounds and process info |
| `--debug score <dir>` | Show scoring comparison across all strategies for a direction |
| `--debug config` | Show the resolved configuration |

**Navigation options:**

| Flag | Values / Syntax | Description |
|---|---|---|
| `--strategy <name>` | `balanced`, `strong-axis-bias`, `closest-in-direction`, `edge-matching`, `edge-proximity`, `axis-only` | Override the scoring strategy for this invocation |
| `--wrap <behavior>` | `no-op`, `wrap`, `beep` | Override wrap-around behavior for this invocation |
| `--exclude <patterns>` | One or more glob patterns | Replace the exclude list for this invocation (does not merge with config) |
| `--verbose`, `-v` | — | Print navigation details (origin window, candidates, scores) to stderr |

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Focus successfully switched to a new window |
| `1` | No candidate found in the requested direction |
| `2` | Error (invalid argument, platform not supported, unexpected exception) |

**Examples:**

```bash
# Start the daemon in background mode
focus daemon --background

# Navigate focus right (uses config defaults)
focus right

# Navigate with a specific strategy
focus up --strategy closest-in-direction

# Navigate with wrap-around
focus left --wrap wrap

# Show all navigable windows
focus --debug enumerate

# Compare strategy scores for windows to the right
focus --debug score right

# Show resolved config
focus --debug config

# Navigate with verbose output (useful for diagnosing unexpected behavior)
focus down --verbose
```

---

## Scoring Strategies

The scoring strategy determines which window gets focus when multiple candidates exist in the requested direction. All strategies filter candidates to windows that are strictly in the requested direction — the difference is how they rank them.

**balanced** (default)

Weights distance and directional alignment roughly equally. A window that is both close and well-aligned on the movement axis scores best. Good general-purpose behavior for standard monitor layouts with varied window sizes and positions.

Use this when: you want predictable navigation that works well in most cases.

**strong-axis-bias**

Heavily favors alignment on the movement axis. A window that is perfectly aligned (e.g., directly to the right) will beat a closer window that is off-axis. Behaves similarly to how a tiling window manager routes focus in a grid layout.

Use this when: your windows are arranged in a grid or tiled pattern and you want strict row/column navigation.

**closest-in-direction**

Picks the nearest window in the general direction using center-to-center distance, regardless of alignment. A window at a 45-degree diagonal will score the same as a window directly ahead, as long as it passes the half-plane directional filter.

Use this when: your windows are scattered and you just want the nearest one in the approximate direction.

**edge-matching**

Uses the far edge of the source window as a reference. For a leftward move, compares the source's right edge to each candidate's right edge; the candidate whose right edge is closest (from the left) to the source's right edge wins. Pure 1D comparison, ignoring the perpendicular axis entirely.

Use this when: you want navigation based on how much a candidate window "extends" past a reference edge of the current window.

**edge-proximity**

Uses the near edge of both source and candidate — the edge facing the direction of movement. For a rightward move, compares source's right edge to each candidate's right edge; the candidate whose right edge extends least beyond the source's right edge wins. Pure 1D comparison, ignoring the perpendicular axis entirely.

Use this when: you want navigation that feels like "which window is closest to where I am, on this side."

**axis-only**

Uses pure center-to-center 1D distance along the movement axis. The perpendicular axis is completely ignored — no secondary weighting, no alignment scoring, just raw 1D distance.

Use this when: you want the most predictable, geometry-minimal navigation — the window whose center is closest along the movement axis always wins.

**Comparing strategies on your current layout:**

```
focus --debug score right
```

This shows a table of all candidate windows to the right, with scores from all strategies side by side.

---

## Troubleshooting

**"focus is not recognized as an internal or external command"**

focus.exe is not in your PATH. If you built from source, either add the build output directory to PATH or use the full path to the executable.

**Console window flashes briefly on each keypress (AutoHotkey)**

You are missing the `"Hide"` parameter in your AHK Run call. Change:

```ahk
#Right::Run("focus right")
```

to:

```ahk
#Right::Run("focus right", , "Hide")
```

**Focus does not switch when triggered from AutoHotkey**

This is the foreground lock issue. Windows restricts which processes can steal foreground focus. focus.exe uses a SendInput ALT bypass to work around this.

If it still fails, check elevation mismatch: focus.exe and AutoHotkey must run at the same privilege level. If one is elevated and the other isn't, Windows silently blocks the activation.

**The wrong window gets focus**

Try a different strategy. Run the score debug command to see how windows are ranked:

```
focus --debug score <direction>
```

**UWP apps (Calculator, Settings, etc.) are not detected**

UWP apps are supported and should appear in the window list. Verify with `focus --debug enumerate`. Minimized windows are intentionally excluded from navigation.

**Windows on a secondary monitor are not reachable**

Focus uses virtual screen coordinates and is DPI-aware (PerMonitorV2). Mixed DPI setups with multiple monitors at different scaling levels are handled correctly.

If windows on a secondary monitor seem unreachable, run `focus --debug enumerate` and check that their bounds are in the expected screen region.

**Config file is not being read**

Run `focus --debug config` to see the resolved configuration and the path Focus is reading from. If the file does not exist at that path, run `focus --init-config` to create it.
