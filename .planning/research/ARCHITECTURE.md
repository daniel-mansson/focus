# Architecture Research: Installer Integration

**Domain:** Inno Setup installer integration with existing .NET 8 daemon architecture
**Researched:** 2026-03-05 (v5.0 Installer milestone)
**Confidence:** HIGH (Inno Setup documentation verified, schtasks syntax verified against Microsoft Learn, dotnet publish behavior verified against official docs)

---

## Existing Architecture (What the Installer Must Integrate With)

Before defining new components, here is what already exists and must not break.

### Runtime Architecture

```
focus.exe daemon
    |
    +-- DaemonCommand.Run()
    |     |
    |     +-- FocusConfig.Load()             reads %AppData%/focus/config.json
    |     +-- ElevateOnStartup check         re-launches as admin via "runas" verb
    |     +-- DaemonMutex.AcquireOrReplace() named mutex "Global\focus-daemon"
    |     +-- KeyboardHookHandler            WH_KEYBOARD_LL via SetWindowsHookEx
    |     +-- CapsLockMonitor                consumer task on thread pool
    |     +-- STA Thread                     WinForms Application.Run()
    |           +-- DaemonApplicationContext
    |                 +-- NotifyIcon          system tray
    |                 +-- OverlayOrchestrator overlay windows + WinEvent hook
    |                 +-- SettingsForm        WinForms settings UI
    |
    +-- Console.CancelKeyPress handler       Ctrl+C ordered shutdown
    +-- DaemonMutex.Release()                cleanup
```

### Key Files and Paths

| Artifact | Location | Owner |
|----------|----------|-------|
| Executable | Currently wherever user places it | User |
| Config file | `%AppData%\focus\config.json` | FocusConfig.cs |
| Named mutex | `Global\focus-daemon` | DaemonMutex.cs |
| Process name | `focus` (from AssemblyName) | focus.csproj |

### Existing Elevation Model

The daemon already has a self-elevation mechanism in `DaemonCommand.cs`:

1. Load config early
2. If `elevateOnStartup` is true AND process is not elevated, re-launch self with `Verb = "runas"` (triggers UAC prompt)
3. If UAC cancelled, fall through and run non-elevated

This is important because the Task Scheduler approach for elevated startup replaces the UAC prompt with a pre-authorized scheduled task, providing a smoother user experience.

### Build Output (Current)

The project uses `net8.0-windows` TFM with framework-dependent deployment:

```
bin/Release/net8.0-windows/
    focus.exe                    (~153 KB) native host
    focus.dll                    (~160 KB) managed assembly
    focus.deps.json
    focus.runtimeconfig.json
    Microsoft.Extensions.FileSystemGlobbing.dll
    System.CommandLine.dll
    cs/, de/, es/, fr/, it/, ja/, ko/, pl/, pt-BR/, ru/, tr/, zh-Hans/, zh-Hant/
        System.CommandLine.resources.dll (localization satellites)
```

---

## New Components Introduced by the Installer

### Component Inventory

| Component | Type | New/Modified | Purpose |
|-----------|------|-------------|---------|
| `installer/focus-setup.iss` | Inno Setup script | **NEW** | Defines the installer |
| `build.ps1` (or build target) | PowerShell script | **NEW** | Automates publish + compile installer |
| Publish profile / csproj props | MSBuild | **MODIFIED** | Self-contained publish config |
| Scheduled task XML | Template | **NEW** | Task Scheduler logon trigger definition |
| Uninstall cleanup logic | Inno Setup Pascal | **NEW** | Remove scheduled task on uninstall |

### What Does NOT Change

- `FocusConfig.cs` -- config path stays `%AppData%\focus\config.json`, untouched by installer
- `DaemonMutex.cs` -- mutex name unchanged
- `DaemonCommand.cs` -- no code changes needed; elevation now handled by Task Scheduler rather than UAC prompt
- All C# source files -- zero modifications to existing code

---

## Architecture: Installer Integration

### Data Flow: Install

```
User runs Focus-Setup.exe (Inno Setup installer)
    |
    1. [Installer UI]
    |   +-- Select install directory (default: %LocalAppData%\Focus)
    |   +-- Checkbox: "Start Focus when I log in" (checked by default)
    |   +-- Checkbox: "Run with admin privileges" (unchecked by default)
    |
    2. [CloseApplications]
    |   +-- Inno Setup's Restart Manager detects running focus.exe
    |   +-- Prompts user to close, or force-closes
    |
    3. [Files]
    |   +-- Copy self-contained publish output to {app}\
    |   +-- focus.exe + all runtime files
    |
    4. [Icons]
    |   +-- Start Menu shortcut: "Focus" -> {app}\focus.exe daemon --background
    |
    5. [Run] (post-install)
    |   +-- IF "Start at login" checked:
    |   |     schtasks /Create /F /SC ONLOGON /TN "Focus" /TR "\"{app}\focus.exe\" daemon --background"
    |   |     +-- IF "Run elevated" also checked: add /RL HIGHEST
    |   |     +-- ELSE: /RL LIMITED (default)
    |   +-- IF "Launch Focus now" checked:
    |         {app}\focus.exe daemon --background
    |
    Done.
```

### Data Flow: Uninstall

```
User runs uninstaller (via Add/Remove Programs or unins000.exe)
    |
    1. [CloseApplications]
    |   +-- Restart Manager detects and closes running focus.exe
    |
    2. [UninstallRun]
    |   +-- schtasks /Delete /TN "Focus" /F
    |   +-- (silently succeeds even if task doesn't exist due to /F)
    |
    3. [UninstallDelete]
    |   +-- Inno Setup auto-removes all files it installed in {app}\
    |   +-- Removes Start Menu shortcut
    |
    4. Config file decision:
    |   +-- %AppData%\focus\config.json is NOT deleted
    |   +-- User's settings preserved for potential reinstall
    |   +-- (This is correct behavior -- config was never installed by the installer)
    |
    Done.
```

### Data Flow: Upgrade (Re-install Over Existing)

```
User runs new Focus-Setup.exe over existing installation
    |
    1. Inno Setup detects existing install via AppId registry key
    |   +-- Uses same {app} directory
    |
    2. CloseApplications closes running daemon
    |
    3. Files overwritten in-place
    |
    4. Scheduled task re-created with /F (force-overwrite)
    |
    5. Daemon re-launched
    |
    Done. Config file untouched.
```

---

## Component Design Details

### 1. Publish Configuration

**Decision: Self-contained deployment, NOT framework-dependent.**

Rationale: Users should not need to install .NET 8 runtime separately. Self-contained bundles the runtime with the app.

**Decision: Do NOT use PublishSingleFile.**

Rationale: Single-file extraction creates temp files, complicates virus scanner false positives, and adds startup latency for extraction. Inno Setup already bundles everything into one setup.exe -- no benefit to also single-filing the app itself.

**Publish command:**

```bash
dotnet publish focus/focus.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false
```

Output lands in: `focus/bin/Release/net8.0-windows/win-x64/publish/`

Key considerations:
- `PublishTrimmed=false` -- Trimming breaks reflection-based serialization (System.Text.Json uses it for FocusConfig) and WinForms (heavy reflection use). Not worth the risk for modest size savings.
- `-r win-x64` -- Explicit runtime identifier required for self-contained.
- Output will be ~60-80 MB (full .NET runtime). This is normal for self-contained .NET 8 apps.

**Alternative considered:** Framework-dependent + .NET runtime prerequisite check. Rejected because adding InnoDependencyInstaller for .NET 8 runtime detection adds complexity and a worse first-run experience. The 60 MB size increase is acceptable for a desktop utility.

### 2. Inno Setup Script Structure

```
installer/
    focus-setup.iss        Main Inno Setup script
```

**Key [Setup] section directives:**

```ini
[Setup]
AppId={{FOCUS-WINDOW-NAV-APP}
AppName=Focus
AppVersion=5.0.0
AppPublisher=Daniel
DefaultDirName={localappdata}\Focus
DefaultGroupName=Focus
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=Focus-Setup
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=force
CloseApplicationsFilter=focus.exe
SetupIconFile=..\focus\focus.ico
UninstallDisplayIcon={app}\focus.exe
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
```

**Rationale for key choices:**

- **`PrivilegesRequired=lowest`** -- Installs per-user without UAC elevation. The app installs to `%LocalAppData%\Focus`, which is user-writable. No admin rights needed for the base install.
- **`PrivilegesRequiredOverridesAllowed=dialog`** -- If the user explicitly wants to install system-wide (e.g., `Program Files`), they can elevate via the installer UI dialog. Provides flexibility without forcing elevation.
- **`CloseApplications=force`** -- The daemon runs continuously; the Restart Manager must close it before overwriting files. `force` ensures it happens even in silent install mode.
- **`DefaultDirName={localappdata}\Focus`** -- Per-user install directory. Aligns with the project requirement for `%LocalAppData%\Focus` default.
- **`{localappdata}`** -- Inno Setup constant resolving to the current user's local AppData. Correct for per-user installs.

### 3. Task Scheduler Registration

**Decision: Use `schtasks.exe` CLI, not XML import.**

Rationale: The schtasks CLI is simpler, more portable across Windows versions, and sufficient for a single logon trigger. XML import (`schtasks /Create /XML`) adds file management complexity for no benefit.

**Task creation command (standard user):**

```
schtasks /Create /F /SC ONLOGON /TN "Focus" /TR "\"{app}\focus.exe\" daemon --background" /RL LIMITED /DELAY 0000:05
```

**Task creation command (elevated):**

```
schtasks /Create /F /SC ONLOGON /TN "Focus" /TR "\"{app}\focus.exe\" daemon --background" /RL HIGHEST /DELAY 0000:05
```

Key parameters:
- `/F` -- Force creation, overwrite if exists (idempotent for upgrades)
- `/SC ONLOGON` -- Trigger on any user logon
- `/TN "Focus"` -- Task name visible in Task Scheduler
- `/TR` -- Full quoted path to focus.exe with daemon arguments
- `/RL LIMITED` or `/RL HIGHEST` -- Run level; user's choice during install
- `/DELAY 0000:05` -- 5-second delay after logon to avoid startup storm

**Task deletion on uninstall:**

```
schtasks /Delete /TN "Focus" /F
```

The `/F` flag makes deletion succeed silently even if the task doesn't exist, so uninstall is always clean.

**Interaction with existing ElevateOnStartup:**

When the installer creates a `/RL HIGHEST` scheduled task, the Task Scheduler runs `focus.exe daemon --background` with full admin privileges -- no UAC prompt needed. The existing `ElevateOnStartup` config check in `DaemonCommand.cs` becomes redundant in this path because the process is already elevated. The two mechanisms coexist safely:

- Task Scheduler elevated task -> process starts elevated -> `ElevateOnStartup` check sees already elevated -> no re-launch -> works
- Manual launch without Task Scheduler -> `ElevateOnStartup` kicks in as before -> works

No code change needed.

### 4. Installer Task Checkboxes (Inno Setup [Tasks])

```ini
[Tasks]
Name: startup;      Description: "Start Focus when I log in";     GroupDescription: "Startup:"; Flags: unchecked
Name: startup\elevated; Description: "Run with administrator privileges (required for managing admin windows)"; Flags: unchecked
```

The `[Run]` and `[UninstallRun]` sections reference these tasks:

```ini
[Run]
; Register scheduled task (standard user)
Filename: "schtasks"; Parameters: "/Create /F /SC ONLOGON /TN ""Focus"" /TR """"""{app}\focus.exe"""" daemon --background"" /RL LIMITED /DELAY 0000:05"; \
    Tasks: startup; Flags: runhidden runascurrentuser
; Register scheduled task (elevated) -- overrides standard if both checked
Filename: "schtasks"; Parameters: "/Create /F /SC ONLOGON /TN ""Focus"" /TR """"""{app}\focus.exe"""" daemon --background"" /RL HIGHEST /DELAY 0000:05"; \
    Tasks: startup\elevated; Flags: runhidden runascurrentuser
; Launch daemon after install
Filename: "{app}\focus.exe"; Parameters: "daemon --background"; \
    Description: "Launch Focus now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "schtasks"; Parameters: "/Delete /TN ""Focus"" /F"; Flags: runhidden runascurrentuser
```

**Important elevation nuance:** Creating a `/RL HIGHEST` task with `schtasks /Create` from a non-elevated installer will succeed -- but the task itself will prompt UAC when it triggers at logon (defeating the purpose). For true silent elevated startup, the installer must be run as admin, OR the user must have previously granted admin access. The `PrivilegesRequiredOverridesAllowed=dialog` directive lets the user elevate the installer if they choose the elevated startup option.

**Recommended UX flow:** If the user checks "Run with administrator privileges", show an informational note that the installer needs admin rights for this to work silently, and the `PrivilegesRequiredOverridesAllowed` dialog will handle the escalation naturally.

### 5. Start Menu and Shell Integration

```ini
[Icons]
Name: "{group}\Focus"; Filename: "{app}\focus.exe"; Parameters: "daemon --background"; \
    IconFilename: "{app}\focus.exe"; Comment: "Focus — directional window navigation"
Name: "{group}\Uninstall Focus"; Filename: "{uninstallexe}"
```

Minimal shell integration -- just a Start Menu group. No desktop shortcut (daemon apps shouldn't have desktop icons). No PATH modification (CLI users can add it themselves).

### 6. Config File Handling

**The installer does NOT touch `%AppData%\focus\config.json`.**

Rationale:
- Config file is created lazily by `focus --init-config` or by SettingsForm save
- The daemon gracefully handles missing config (`FocusConfig.Load()` returns defaults)
- Installing a default config would overwrite user customizations on upgrade
- The config directory `%AppData%\focus\` is separate from the install directory `%LocalAppData%\Focus\`

On uninstall, the config file is intentionally preserved. Users who want a complete clean removal can delete `%AppData%\focus\` manually.

---

## Build Pipeline

### Build Flow

```
[Developer machine or CI]
    |
    1. dotnet publish focus/focus.csproj
    |     -c Release
    |     -r win-x64
    |     --self-contained true
    |     Output: focus/bin/Release/net8.0-windows/win-x64/publish/
    |
    2. iscc installer/focus-setup.iss
    |     Reads publish output from step 1
    |     Output: dist/Focus-Setup.exe
    |
    Done.
```

### Build Script (`build.ps1`)

A simple PowerShell script orchestrating both steps:

```powershell
# Publish .NET app
dotnet publish focus/focus.csproj -c Release -r win-x64 --self-contained true

# Compile installer (requires Inno Setup installed, iscc.exe on PATH)
iscc installer/focus-setup.iss
```

The Inno Setup script references the publish output directory via a `#define`:

```ini
#define PublishDir "..\focus\bin\Release\net8.0-windows\win-x64\publish"
#define AppExe "focus.exe"
#define AppVersion "5.0.0"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
```

---

## Directory Layout After Install

```
%LocalAppData%\Focus\              (install directory, chosen by user)
    focus.exe                       native host executable
    focus.dll                       managed assembly
    focus.deps.json                 dependency manifest
    focus.runtimeconfig.json        runtime config
    focus.ico                       (embedded, but also in install dir for uninstall icon)
    *.dll                           .NET runtime + framework + dependencies
    ...                             (satellite assemblies in locale subdirs)

%AppData%\focus\                   (config directory, NOT managed by installer)
    config.json                     user configuration (if created)

Task Scheduler:
    \Focus                          logon trigger task (if registered)
        -> runs: "%LocalAppData%\Focus\focus.exe" daemon --background
        -> trigger: on logon
        -> run level: LIMITED or HIGHEST

Start Menu:
    Focus\
        Focus.lnk                   -> {app}\focus.exe daemon --background
        Uninstall Focus.lnk         -> unins000.exe
```

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Installing config files
**What:** Placing a default config.json in the install directory or %AppData%
**Why bad:** Overwrites user customizations on upgrade. The daemon already handles missing config gracefully with defaults.
**Instead:** Let the daemon create config on first `--init-config` or settings save.

### Anti-Pattern 2: Registry Run key for startup
**What:** Using `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for auto-start
**Why bad:** Cannot launch with elevated privileges. Task Scheduler supports `/RL HIGHEST` for elevation without UAC prompt (when the task was registered by an admin). The existing `ElevateOnStartup` feature requires admin privileges to function with elevated windows.
**Instead:** Use Task Scheduler with ONLOGON trigger.

### Anti-Pattern 3: PublishSingleFile for installer-distributed app
**What:** Setting `PublishSingleFile=true` and then bundling in Inno Setup
**Why bad:** Double compression (single-file bundles, then Inno compresses again). Extraction to temp on first run adds latency. Virus scanner false positives increase with single-file bundles. Inno Setup already provides the "single installer file" experience.
**Instead:** Normal self-contained publish, let Inno Setup handle compression.

### Anti-Pattern 4: PublishTrimmed with WinForms + System.Text.Json
**What:** Enabling IL trimming to reduce output size
**Why bad:** WinForms uses extensive reflection that trimmer cannot analyze. System.Text.Json serialization with `JsonStringEnumConverter` relies on reflection for enum names. Both break at runtime with cryptic `MissingMethodException` or silent data loss.
**Instead:** Accept the ~60-80 MB self-contained size. After Inno Setup LZMA2 compression, the installer will be ~25-35 MB.

### Anti-Pattern 5: Modifying PATH environment variable
**What:** Adding the install directory to the user's PATH
**Why bad:** The primary use case is daemon mode (auto-start at login). CLI users who want `focus left` in their shell can add the path themselves. Modifying PATH without asking is intrusive and complicates uninstall.
**Instead:** Document in the installer's finishing page or README that CLI users can add `%LocalAppData%\Focus` to their PATH.

---

## Scalability Considerations

Not applicable for this milestone -- the installer is a single-user desktop tool with no server component. However, some forward-looking notes:

| Concern | Current (v5.0) | Future |
|---------|----------------|--------|
| Auto-update | Not included | Could add update check in daemon + download new installer |
| Silent install | Supported via `Focus-Setup.exe /SILENT` | Works today with Inno Setup built-in flags |
| Winget/Chocolatey | Not packaged | Inno Setup .exe is compatible with both package managers |
| ARM64 | Not built | Add `-r win-arm64` publish + separate installer, or fat installer |

---

## Sources

- [Inno Setup Documentation -- CloseApplications](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm) -- HIGH confidence
- [Inno Setup Documentation -- Constants](https://jrsoftware.org/ishelp/topic_consts.htm) -- HIGH confidence
- [Inno Setup Documentation -- PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) -- HIGH confidence
- [Microsoft Learn -- schtasks /create](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create) -- HIGH confidence
- [Microsoft Learn -- Logon Trigger XML Example](https://learn.microsoft.com/en-us/windows/win32/taskschd/logon-trigger-example--xml-) -- HIGH confidence
- [Microsoft Learn -- RunLevel element](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-runlevel-principaltype-element) -- HIGH confidence
- [Microsoft Learn -- dotnet publish](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) -- HIGH confidence
- [Microsoft Learn -- Self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/) -- HIGH confidence
- [Microsoft Learn -- Single file publishing (WinForms issues)](https://github.com/dotnet/winforms/issues/11473) -- MEDIUM confidence
- [Creating a Non-Admin Installer with Inno Setup](https://kinook.com/blog2/inno-setup.html) -- MEDIUM confidence
- Existing codebase analysis: `DaemonCommand.cs`, `FocusConfig.cs`, `DaemonMutex.cs`, `focus.csproj` -- HIGH confidence (direct code reading)
