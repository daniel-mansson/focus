---
phase: quick-1
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - SETUP.md
autonomous: true
requirements: []
must_haves:
  truths:
    - "A new user can follow the guide from zero to working directional focus navigation"
    - "The guide covers building the project, configuring AutoHotkey, and verifying it works"
    - "All CLI flags and config options are documented with examples"
  artifacts:
    - path: "SETUP.md"
      provides: "Complete setup guide for focus + AutoHotkey integration"
      min_lines: 100
  key_links: []
---

<objective>
Create a comprehensive setup guide (SETUP.md) in the project root that walks a user through building the focus tool, integrating it with AutoHotkey for hotkey-based directional window navigation, and configuring all available options.

Purpose: Enable anyone with a Windows machine to go from zero to working Hyprland-style window navigation using this tool and AutoHotkey.
Output: SETUP.md in the repository root.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@focus/focus.csproj
@focus/Program.cs
@focus/Windows/FocusConfig.cs

Key project facts for the guide:
- .NET 8 project (net8.0 target in csproj, but dev machine uses .NET 10 — guide should say .NET 8+)
- Build command: `dotnet build` (or `dotnet publish` for self-contained)
- Executable name: `focus.exe` (AssemblyName is "focus")
- Config path: %APPDATA%\focus\config.json
- CLI usage:
  - `focus <direction>` where direction = left | right | up | down
  - `--strategy balanced | strong-axis-bias | closest-in-direction`
  - `--wrap no-op | wrap | beep`
  - `--exclude "pattern1" "pattern2"` (replaces config exclude list)
  - `--verbose` / `-v` — show navigation details to stderr
  - `--debug enumerate` — list all detected windows
  - `--debug score <direction>` — show scoring for all strategies
  - `--debug config` — show resolved config
  - `--init-config` — write default config.json
- Exit codes: 0=switched, 1=no candidate, 2=error
- Strategies:
  - balanced: considers distance and alignment roughly equally (default)
  - strong-axis-bias: heavily favors alignment on the movement axis
  - closest-in-direction: nearest window in the general direction wins
- Wrap behaviors:
  - no-op: do nothing when no candidate (default)
  - wrap: cycle to opposite edge of screen
  - beep: play system beep sound
- Config JSON shape: { "strategy": "balanced", "wrap": "noOp", "exclude": [] }
- Exclude patterns use filesystem globbing (e.g., "explorer*", "Teams*")
- Requires Windows Vista+ (DwmGetWindowAttribute dependency)
- DPI-aware via PerMonitorV2 manifest — multi-monitor with mixed DPI works correctly
- AutoHotkey is the intended invocation mechanism (hotkeys call focus.exe)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create SETUP.md with complete setup guide</name>
  <files>SETUP.md</files>
  <action>
Create SETUP.md in the repository root directory with the following sections:

**1. Overview** (2-3 sentences)
What focus does: a lightweight CLI tool for Hyprland-style directional window focus navigation on Windows, designed to be triggered by AutoHotkey hotkeys.

**2. Prerequisites**
- Windows 10 or later (Windows 11 works)
- .NET 8 SDK or later (link to https://dotnet.microsoft.com/download)
- AutoHotkey v2 (link to https://www.autohotkey.com/) — note v2, not v1, as the AHK script examples should use v2 syntax
- Git (to clone the repo)

**3. Build**
Step-by-step:
```
git clone <repo-url>
cd windowfocusnavigation/focus
dotnet build -c Release
```
Mention the output path: `focus/bin/Release/net8.0/focus.exe`

Optional: self-contained single-file publish for machines without .NET runtime:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Output: `focus/bin/Release/net8.0/win-x64/publish/focus.exe`

**4. Add to PATH**
Explain two options:
a) Add the build output directory to the system PATH environment variable
b) Copy focus.exe to a directory already in PATH (e.g., `C:\Tools\`)

Include a PowerShell one-liner to verify: `focus --help`

**5. AutoHotkey Integration**
Provide a complete AHK v2 script (`focus-nav.ahk`) with:
- Win+Arrow keys mapped to `focus left/right/up/down`
- Example uses `Run` with the focus.exe path
- Show both variants: (a) focus.exe in PATH, (b) absolute path to focus.exe
- Include a `*` prefix on the hotkeys to allow them to fire even if another modifier is held

Full example script:
```ahk
#Requires AutoHotkey v2.0

; Win + Arrow keys for directional focus navigation
#Left::Run("focus left", , "Hide")
#Right::Run("focus right", , "Hide")
#Up::Run("focus up", , "Hide")
#Down::Run("focus down", , "Hide")
```

Explain the "Hide" parameter (suppresses console window flash).

Also show an advanced example with strategy override:
```ahk
; Win+Shift+Arrow for strong-axis-bias navigation
#+Left::Run("focus left --strategy strong-axis-bias", , "Hide")
```

Explain how to auto-start the script: place a shortcut in `shell:startup` folder.

**6. Configuration**
- Run `focus --init-config` to create the default config at `%APPDATA%\focus\config.json`
- Show the default config JSON content:
```json
{
  "strategy": "balanced",
  "wrap": "noOp",
  "exclude": []
}
```
- Explain each field:
  - strategy: balanced | strongAxisBias | closestInDirection (camelCase in JSON)
  - wrap: noOp | wrap | beep
  - exclude: array of glob patterns matching process names (e.g., ["explorer*", "Teams*"])
- Note: CLI flags override config file values per invocation

**7. CLI Reference**
A table or list of all commands and flags:
- `focus <direction>` — navigate focus (left, right, up, down)
- `--strategy <name>` — override scoring strategy
- `--wrap <behavior>` — override wrap behavior
- `--exclude <patterns>` — override exclude list (replaces config, does not merge)
- `--verbose` / `-v` — show navigation details to stderr
- `--debug enumerate` — list all detected windows with bounds and process info
- `--debug score <direction>` — compare scoring across all three strategies
- `--debug config` — show resolved configuration
- `--init-config` — create default config file
- Exit codes: 0 = focus switched, 1 = no candidate found, 2 = error

**8. Scoring Strategies**
Brief description of each with when to use it:
- **balanced** (default): Good general-purpose. Weights distance and alignment equally. Best for standard monitor layouts.
- **strong-axis-bias**: Strongly prefers windows aligned on the movement axis. Best for grid-like window arrangements (e.g., tiled layouts).
- **closest-in-direction**: Picks the nearest window in the general direction regardless of alignment. Best for scattered window layouts.

Tip: Use `focus --debug score <direction>` to compare how each strategy scores your current window arrangement.

**9. Troubleshooting**
- "focus is not recognized": focus.exe not in PATH — verify with `where focus`
- Console window flashes: Use "Hide" parameter in AHK Run command
- Focus does not switch when invoked from AHK: This is the foreground lock issue. The tool uses SendInput ALT bypass to handle this. If it still fails, ensure focus.exe is not running as administrator when AHK is running as a standard user (or vice versa). Both must run at the same elevation level.
- Wrong window gets focus: Try a different strategy. Use `--debug score <direction>` to see how windows are scored.
- UWP apps (Calculator, Settings) not detected: Should work out of the box. Run `focus --debug enumerate` to verify they appear in the window list.
- Multi-monitor issues: The tool uses virtual screen coordinates and is DPI-aware (PerMonitorV2). Mixed DPI setups are supported. If windows on a secondary monitor are unreachable, run `--debug enumerate` to check bounds.

Do NOT include emojis anywhere in the document. Use plain markdown formatting throughout.
  </action>
  <verify>
Test that the file exists, is valid markdown, and covers all required sections:
```
test -f SETUP.md && head -5 SETUP.md && echo "---" && grep -c "^##" SETUP.md
```
Expected: file exists, starts with a heading, has 9+ second-level headings (one per section).
  </verify>
  <done>
SETUP.md exists in the repo root with all 9 sections covering prerequisites, build instructions, AutoHotkey v2 integration script, configuration reference, CLI reference, strategy explanations, and troubleshooting. A new user can follow the guide from zero to working directional focus navigation.
  </done>
</task>

</tasks>

<verification>
- SETUP.md exists in repository root
- Contains build instructions (dotnet build command)
- Contains complete AutoHotkey v2 script example with Win+Arrow bindings
- Contains configuration section with JSON example and field descriptions
- Contains CLI reference with all flags documented
- Contains troubleshooting section
- No emojis in the file
</verification>

<success_criteria>
A Windows user unfamiliar with this project can follow SETUP.md from start to finish to get working Hyprland-style directional window focus navigation using AutoHotkey hotkeys.
</success_criteria>

<output>
After completion, create `.planning/quick/1-create-setup-guide-for-project-with-auto/1-SUMMARY.md`
</output>
