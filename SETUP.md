# Window Focus Navigation — Setup Guide

A lightweight CLI tool for Hyprland-style directional window focus navigation on Windows. Invoke it via AutoHotkey hotkeys (`focus left`, `focus right`, etc.) and it finds the best candidate window in the given direction and switches focus to it — spatial navigation without a tiling window manager.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Build](#build)
- [Add to PATH](#add-to-path)
- [AutoHotkey Integration](#autohotkey-integration)
- [Configuration](#configuration)
- [CLI Reference](#cli-reference)
- [Scoring Strategies](#scoring-strategies)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

- **Windows 10 or later** (Windows 11 fully supported; Windows Vista/7/8 also work)
- **.NET 8 SDK or later** — [download from Microsoft](https://dotnet.microsoft.com/download)
- **AutoHotkey v2** — [download from autohotkey.com](https://www.autohotkey.com/) — note: v2, not v1; the script examples below use v2 syntax
- **Git** — to clone the repository

Verify your .NET installation before building:

```
dotnet --version
```

Any version 8.0 or higher is fine.

---

## Build

Clone the repository and build in Release configuration:

```
git clone <repo-url>
cd windowfocusnavigation/focus
dotnet build -c Release
```

The compiled executable is at:

```
focus/bin/Release/net8.0/focus.exe
```

**Optional: self-contained publish**

If you want to run focus.exe on a machine that does not have the .NET runtime installed, publish a self-contained single-file executable:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output location:

```
focus/bin/Release/net8.0/win-x64/publish/focus.exe
```

This bundles the .NET runtime into a single executable (~15 MB). No runtime installation required on the target machine.

---

## Add to PATH

You need focus.exe to be callable from anywhere so AutoHotkey can invoke it without an absolute path.

**Option A — Add the build output directory to your system PATH**

1. Open Start, search for "Edit the system environment variables"
2. Click "Environment Variables..."
3. Under "System variables" (or "User variables"), select "Path" and click "Edit"
4. Click "New" and paste the full path to the build output directory, for example:
   ```
   C:\Work\windowfocusnavigation\focus\bin\Release\net8.0
   ```
5. Click OK on all dialogs
6. Open a new terminal and verify:
   ```
   focus --help
   ```

**Option B — Copy focus.exe to a directory already in PATH**

If you have a utilities folder (e.g., `C:\Tools\`) already in your PATH, copy the executable there:

```powershell
Copy-Item "focus\bin\Release\net8.0\focus.exe" "C:\Tools\focus.exe"
```

Verify from any terminal:

```
focus --help
```

Either option works. Option A avoids copying when you rebuild. Option B is simpler if you already have a tools directory.

---

## AutoHotkey Integration

AutoHotkey v2 is the recommended way to bind focus to keyboard shortcuts. Create a script file (e.g., `focus-nav.ahk`) with the following content.

### Basic script — Win+Arrow keys

```ahk
#Requires AutoHotkey v2.0

; Win + Arrow keys for directional focus navigation
#Left::Run("focus left", , "Hide")
#Right::Run("focus right", , "Hide")
#Up::Run("focus up", , "Hide")
#Down::Run("focus down", , "Hide")
```

The `"Hide"` parameter suppresses the console window flash that would otherwise appear briefly each time focus.exe runs. Without it, you will see a black window flicker on every keypress.

### Using an absolute path (if focus.exe is not in PATH)

If you prefer not to modify your PATH, specify the full path to focus.exe in the Run call:

```ahk
#Requires AutoHotkey v2.0

focusExe := "C:\Work\windowfocusnavigation\focus\bin\Release\net8.0\focus.exe"

#Left::Run(focusExe . " left", , "Hide")
#Right::Run(focusExe . " right", , "Hide")
#Up::Run(focusExe . " up", , "Hide")
#Down::Run(focusExe . " down", , "Hide")
```

### Advanced example — per-binding strategy override

You can pass any CLI flag in the Run command. For example, add Win+Shift+Arrow bindings that use the strong-axis-bias strategy for grid-like navigation:

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

Alternatively, you can right-click the `.ahk` file, select "Create shortcut", and move the shortcut into the Startup folder.

---

## Configuration

focus reads a JSON config file for its default settings. The config is optional — if it does not exist, focus uses built-in defaults (balanced strategy, no-op wrap, no exclusions).

**Create the default config:**

```
focus --init-config
```

This writes the config file to:

```
%APPDATA%\focus\config.json
```

For most users that expands to something like `C:\Users\YourName\AppData\Roaming\focus\config.json`.

**Default config.json:**

```json
{
  "strategy": "balanced",
  "wrap": "noOp",
  "exclude": []
}
```

**Config fields:**

| Field | Type | Values | Default | Description |
|---|---|---|---|---|
| `strategy` | string | `balanced`, `strongAxisBias`, `closestInDirection`, `edgeMatching` | `balanced` | Default scoring strategy for direction navigation |
| `wrap` | string | `noOp`, `wrap`, `beep` | `noOp` | Behavior when no window is found in the requested direction |
| `exclude` | array | glob patterns | `[]` | Process names to exclude from window enumeration |

Note: JSON field values use camelCase (e.g., `strongAxisBias`, `closestInDirection`, `edgeMatching`, `noOp`). CLI flags use kebab-case (e.g., `--strategy strong-axis-bias`, `--strategy edge-matching`).

**Wrap behavior:**

- `noOp` — do nothing when no candidate exists in that direction (default)
- `wrap` — cycle to the window at the opposite edge of the screen
- `beep` — play the system beep sound when no candidate is found

**Exclude patterns:**

The `exclude` array contains glob patterns matched against the process name (without `.exe`). For example:

```json
{
  "strategy": "balanced",
  "wrap": "noOp",
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
focus <direction> [options]
```

**Direction argument:**

| Value | Effect |
|---|---|
| `left` | Navigate focus to the left |
| `right` | Navigate focus to the right |
| `up` | Navigate focus upward |
| `down` | Navigate focus downward |

**Options:**

| Flag | Values / Syntax | Description |
|---|---|---|
| `--strategy <name>` | `balanced`, `strong-axis-bias`, `closest-in-direction`, `edge-matching` | Override the scoring strategy for this invocation |
| `--wrap <behavior>` | `no-op`, `wrap`, `beep` | Override wrap-around behavior for this invocation |
| `--exclude <patterns>` | One or more glob patterns | Replace the exclude list for this invocation (does not merge with config) |
| `--verbose`, `-v` | — | Print navigation details (origin window, candidates, scores) to stderr |
| `--debug enumerate` | — | List all detected navigable windows with bounds and process info |
| `--debug score <dir>` | `left`, `right`, `up`, `down` | Show scoring comparison across all three strategies for the given direction |
| `--debug config` | — | Show the resolved configuration (config file path, strategy, wrap, exclude list) |
| `--init-config` | — | Write a default config.json to `%APPDATA%\focus\config.json` |

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Focus successfully switched to a new window |
| `1` | No candidate found in the requested direction |
| `2` | Error (invalid argument, platform not supported, unexpected exception) |

Exit codes are useful if you want to chain behavior in scripts or AHK. For example, exit code 1 means no window was found in that direction.

**Examples:**

```
; Navigate focus right (uses config defaults)
focus right

; Navigate focus up using closest-in-direction strategy
focus up --strategy closest-in-direction

; Navigate focus left with wrap-around enabled
focus left --wrap wrap

; Show all navigable windows
focus --debug enumerate

; Compare strategy scores for windows to the right
focus --debug score right

; Show resolved config
focus --debug config

; Navigate with verbose output (useful for diagnosing unexpected behavior)
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

**Comparing strategies on your current layout:**

```
focus --debug score right
```

This command shows a table of all candidate windows to the right of the current window, with scores from all three strategies side by side. The active strategy is marked with an asterisk. Use this to pick the strategy that matches your intuition for a given layout.

---

## Troubleshooting

**"focus is not recognized as an internal or external command"**

focus.exe is not in your PATH. Verify with:

```
where focus
```

If that returns nothing, follow the [Add to PATH](#add-to-path) section. After updating PATH, open a new terminal — existing terminals do not pick up PATH changes.

**Console window flashes briefly on each keypress**

You are missing the `"Hide"` parameter in your AHK Run call. Change:

```ahk
#Right::Run("focus right")
```

to:

```ahk
#Right::Run("focus right", , "Hide")
```

The third parameter `"Hide"` tells AutoHotkey to suppress the console window.

**Focus does not switch when triggered from AutoHotkey**

This is the foreground lock issue. Windows restricts which processes can steal foreground focus. focus.exe uses a SendInput ALT bypass (simulating a brief Alt keypress before calling SetForegroundWindow) to work around this.

If it still fails, check elevation mismatch: focus.exe and AutoHotkey must run at the same privilege level. If AutoHotkey is running as a standard user and focus.exe is running as administrator (or vice versa), the activation will be silently blocked by Windows.

To check: right-click focus.exe and ensure "Run as administrator" is not checked in Properties. AutoHotkey should also be running as a standard user unless you explicitly need elevated access.

**The wrong window gets focus**

Try a different strategy. Run the score debug command to see how windows are ranked:

```
focus --debug score <direction>
```

The output shows scores for all three strategies. If a different strategy would have chosen the window you wanted, switch to it either in config.json or via `--strategy` on the relevant AHK binding.

**UWP apps (Calculator, Settings, etc.) are not detected**

UWP apps are supported and should appear in the window list. To verify:

```
focus --debug enumerate
```

If Calculator or Settings are missing, check that they are not minimized. Minimized windows are intentionally excluded from navigation.

**Windows on a secondary monitor are not reachable**

focus uses virtual screen coordinates and is DPI-aware (PerMonitorV2 manifest). Mixed DPI setups with multiple monitors at different scaling levels are handled correctly.

If windows on a secondary monitor seem unreachable, run:

```
focus --debug enumerate
```

Check that the windows appear in the list and that their bounds are in the expected screen region. If bounds look wrong (e.g., all zeros), the window may be cloaked or in a state that causes DwmGetWindowAttribute to return empty bounds.

**Config file is not being read**

Run `focus --debug config` to see the resolved configuration and the path focus is reading from. If the file does not exist at that path, run `focus --init-config` to create it.
