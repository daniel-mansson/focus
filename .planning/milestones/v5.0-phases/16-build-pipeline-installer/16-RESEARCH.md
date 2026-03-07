# Phase 16: Build Pipeline & Installer - Research

**Researched:** 2026-03-05
**Domain:** Windows installer creation (Inno Setup), .NET self-contained publish, PowerShell build automation
**Confidence:** HIGH

## Summary

Phase 16 produces a complete build-and-install pipeline: a PowerShell script (`build.ps1`) that runs `dotnet publish` to create a self-contained single-file executable, then invokes the Inno Setup command-line compiler (`ISCC.exe`) to produce `Focus-Setup.exe`. The installer handles first install, in-place upgrade (stopping a running daemon via AppMutex), and clean uninstall through Add/Remove Programs.

Inno Setup 6.7.1 is the current stable release and is well-suited for this use case. The `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog` combination provides per-user install by default (no UAC prompt) with an opt-in dialog for all-users install. The daemon's existing named mutex (`Global\focus-daemon`) maps directly to the Inno Setup `AppMutex` directive for process-in-use detection. Config at `%AppData%\focus\config.json` is completely outside the install directory and naturally preserved across upgrades.

**Primary recommendation:** Use Inno Setup 6.7.1 ISS script with ISCC command-line compilation, orchestrated by a PowerShell build script that reads the version from `focus.csproj` and passes it to ISCC via `/D` preprocessor define.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- PowerShell script (build.ps1) at repo root orchestrates dotnet publish + ISCC.exe compile
- Inno Setup .iss file and installer-specific assets live in an `installer/` directory at repo root
- ISCC.exe must be on PATH (no hardcoded default path, no param override)
- Build output (Focus-Setup.exe) goes to `installer/output/` (gitignored)
- Minimal wizard: welcome, install location, progress, finish -- no license page, no component selection
- App identity in Add/Remove Programs: name "Focus", publisher is the user's name
- Start Menu shortcut created, launching `focus daemon`
- Finish page has "Launch Focus now" checkbox, checked by default
- Inno Setup 6.7.1 with PrivilegesRequired=lowest and PrivilegesRequiredOverridesAllowed=dialog
- Self-contained single-file publish (PublishSingleFile=true + IncludeNativeLibrariesForSelfExtract=true)
- Default install path: %LocalAppData%\Focus
- Installer never touches %AppData%\focus\config.json -- config owned by daemon runtime
- AppMutex detection to stop running daemon before upgrade

### Claude's Discretion
- Exact Inno Setup section structure and scripting details
- .gitignore entries for build output
- Version bumping mechanics in build script
- Installer icon and visual polish

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| PKG-01 | Installer produces a single setup.exe via Inno Setup with self-contained .NET publish | build.ps1 runs `dotnet publish` then ISCC.exe; OutputBaseFilename controls the output name |
| INST-01 | User can install Focus to a chosen directory (default: %LocalAppData%\Focus) | DefaultDirName={localappdata}\Focus with DisableDirPage=auto; PrivilegesRequired=lowest ensures {localappdata} resolves to current user |
| INST-02 | Installer stops running daemon before upgrading files (AppMutex detection) | AppMutex=Global\focus-daemon matches existing DaemonMutex.MutexName; CloseApplications=yes uses Restart Manager to prompt graceful stop |
| INST-03 | Installer upgrades in-place without breaking user config | Config lives at %AppData%\focus\config.json -- completely outside {app}; [Files] with ignoreversion flag replaces focus.exe cleanly |
| INST-04 | User can uninstall via Add/Remove Programs (removes files + scheduled task) | Inno Setup auto-removes [Files] entries and [Icons] shortcuts on uninstall; scheduled task removal is Phase 17 (SCHED-03) |
| INST-05 | Installer offers "Launch Focus now" checkbox after install | [Run] section with postinstall+nowait flags and Description="Launch Focus now" |
</phase_requirements>

## Standard Stack

### Core
| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| Inno Setup | 6.7.1 | Windows installer compiler | De facto standard for open-source Windows installers; free, mature, well-documented |
| ISCC.exe | 6.7.1 | Command-line Inno Setup compiler | Enables scripted builds without GUI; supports /D preprocessor defines |
| dotnet publish | .NET 8 SDK | Self-contained single-file compilation | Produces focus.exe with bundled runtime (~15 MB) |
| PowerShell | 5.1+ (Windows built-in) | Build orchestration | Available on all Windows systems; native XML parsing for .csproj |

### Supporting
| Tool | Version | Purpose | When to Use |
|------|---------|---------|-------------|
| Inno Setup Preprocessor (ISPP) | Built into 6.7.1 | Compile-time variable substitution | Pass version from build script to ISS via `/DMyAppVersion=X.Y.Z` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Inno Setup | WiX Toolset | More powerful but vastly more complex XML; overkill for single-exe app |
| Inno Setup | NSIS | Less maintained; Inno Setup has better modern Windows support |
| PowerShell build script | MSBuild targets | Tighter integration but harder to read/debug; PowerShell is more transparent |

## Architecture Patterns

### Recommended Project Structure
```
windowfocusnavigation/
+-- build.ps1                    # Build orchestrator (repo root)
+-- focus/                       # Existing .NET project
|   +-- focus.csproj
|   +-- focus.ico
|   +-- ...
+-- installer/                   # Inno Setup assets
|   +-- focus.iss                # Inno Setup script
|   +-- output/                  # Build output (gitignored)
|   |   +-- Focus-Setup.exe      # Final installer
```

### Pattern 1: Version Flow from csproj to Installer
**What:** build.ps1 reads `<Version>` from focus.csproj, passes it to ISCC via `/D` define, ISS uses `#define` to set AppVersion.
**When to use:** Every build.
**Example:**
```powershell
# build.ps1 - Read version from csproj
[xml]$csproj = Get-Content "focus/focus.csproj"
$version = $csproj.Project.PropertyGroup.Version
# Pass to ISCC
& ISCC.exe /DMyAppVersion="$version" installer/focus.iss
```
```iss
; focus.iss - Receive version
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
[Setup]
AppVersion={#MyAppVersion}
```
**Source:** [Inno Setup Preprocessor - ISCC Extended CLI](https://jrsoftware.org/ishelp/topic_isppcc.htm)

### Pattern 2: AppMutex for Daemon Detection
**What:** Inno Setup checks for the daemon's named mutex before installing/uninstalling. If the mutex exists (daemon running), the user is prompted to close it.
**When to use:** Every install and uninstall.
**Example:**
```iss
[Setup]
; Must match DaemonMutex.MutexName exactly (case-sensitive)
AppMutex=Global\focus-daemon
CloseApplications=yes
```
**Source:** [Inno Setup AppMutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm), verified against `focus/Windows/Daemon/DaemonMutex.cs` line 7: `private const string MutexName = @"Global\focus-daemon";`

**Critical detail:** The `\` in `Global\focus-daemon` does NOT need escaping in the ISS AppMutex directive. Backslash is a literal character in Inno Setup directives (only commas need escaping with backslash). The mutex name comparison is case-sensitive on Windows.

### Pattern 3: Per-User Install with Opt-In Admin
**What:** PrivilegesRequired=lowest installs to user profile by default; PrivilegesRequiredOverridesAllowed=dialog lets user choose all-users install with a suppressible dialog.
**When to use:** Setup section configuration.
**Example:**
```iss
[Setup]
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Focus
```
**Source:** [PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [PrivilegesRequiredOverridesAllowed](https://jrsoftware.org/ishelp/topic_setup_privilegesrequiredoverridesallowed.htm)

**Key behavior:** When PrivilegesRequired=lowest, `{localappdata}` resolves to the current user's `%LocalAppData%` folder. The "dialog" value also implicitly enables "commandline" (the `/ALLUSERS` and `/CURRENTUSER` CLI params).

### Pattern 4: Postinstall Launch Checkbox
**What:** [Run] section entry with postinstall flag creates a checkbox on the "Setup Complete" wizard page.
**When to use:** Finish page.
**Example:**
```iss
[Run]
Filename: "{app}\focus.exe"; Parameters: "daemon"; \
  Description: "Launch Focus now"; \
  Flags: postinstall nowait skipifsilent
```
**Source:** [Inno Setup Run Section](https://jrsoftware.org/ishelp/topic_runsection.htm)

**Flags explained:**
- `postinstall` -- shows checkbox on finish page
- `nowait` -- setup exits without waiting for daemon to finish (daemon is long-running)
- `skipifsilent` -- skip when running in silent mode (`/SILENT` or `/VERYSILENT`)
- Checkbox is checked by default (no `unchecked` flag)

### Pattern 5: Self-Contained Publish Command
**What:** dotnet publish produces a single focus.exe with bundled .NET runtime.
**When to use:** Build step before ISCC.
**Example:**
```powershell
dotnet publish focus/focus.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```
**Source:** [Microsoft .NET Single-File Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), verified against existing SETUP.md

**Output location:** `focus/bin/Release/net8.0/win-x64/publish/focus.exe`

### Anti-Patterns to Avoid
- **Hardcoding ISCC path:** User decided ISCC must be on PATH. Never hardcode `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`.
- **Using {autopf} as DefaultDirName:** With PrivilegesRequired=lowest, use `{localappdata}\Focus` instead of `{autopf}\Focus`. The `{autopf}` constant maps to `{userpf}` in non-admin mode which is still a Program Files location that may cause permission issues.
- **Touching config directory during uninstall:** The `%AppData%\focus\` directory is NOT owned by the installer. Never add [UninstallDelete] entries for it. The requirements explicitly say "Delete user config on uninstall" is out of scope.
- **Using CloseApplications=force:** This risks data loss by force-killing processes. Use `CloseApplications=yes` which prompts the user.
- **Omitting ignoreversion on [Files]:** Without `ignoreversion`, Inno Setup applies version comparison logic. Since focus.exe is a self-contained .NET app, it has version info, but `ignoreversion` is simpler and more reliable for the single-exe case.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Process-in-use detection | Custom mutex check code | Inno Setup AppMutex + CloseApplications | Restart Manager integration handles edge cases (locked files, DLL in use) |
| Add/Remove Programs registration | Manual registry writes | Inno Setup automatic uninstall registration | AppId generates registry key automatically; handles both HKCU and HKLM |
| File replacement during upgrade | Custom file copy logic | Inno Setup [Files] with ignoreversion | Handles locked files, restartreplace, rollback on failure |
| Start Menu shortcut creation | Manual IShellLink COM | Inno Setup [Icons] section | Handles both per-user and all-users modes automatically |
| Installer wizard UI | Custom WinForms installer | Inno Setup wizard pages | Mature, accessible, DPI-aware, supports dark mode |
| Version parsing from csproj | Regex string parsing | PowerShell [xml] cast + XPath | XML parsing is built into PowerShell; handles edge cases |

**Key insight:** Inno Setup handles the entire install/upgrade/uninstall lifecycle. The only custom code needed is the build.ps1 orchestration script. Everything else is ISS configuration.

## Common Pitfalls

### Pitfall 1: AppMutex Name Mismatch
**What goes wrong:** Installer cannot detect running daemon, proceeds with install, file replacement fails because focus.exe is locked.
**Why it happens:** Case sensitivity mismatch between ISS AppMutex value and C# mutex name, or forgetting the `Global\` prefix.
**How to avoid:** Copy the exact string from `DaemonMutex.cs`: `Global\focus-daemon`. Verify with SysInternals Process Explorer (Ctrl+H to view handles, filter for "focus-daemon").
**Warning signs:** "The file is in use" errors during upgrade, or the daemon not being detected as running.

### Pitfall 2: {localappdata} Resolving to Wrong User
**What goes wrong:** When an admin user runs the installer with elevation (UAC), `{localappdata}` could resolve to the admin account's profile instead of the logged-in user's.
**Why it happens:** Inno Setup resolves constants in the context of the running process. With PrivilegesRequired=lowest, the installer runs as the current user (no elevation), so this is NOT a problem. But if the user overrides to admin via the dialog, the constant resolution changes.
**How to avoid:** PrivilegesRequired=lowest ensures `{localappdata}` always resolves correctly for the current user. The `PrivilegesRequiredOverridesAllowed=dialog` option lets users choose, but at their own risk.
**Warning signs:** Install going to `C:\Windows\System32\config\systemprofile\AppData\Local\` instead of user profile.

### Pitfall 3: Missing IncludeNativeLibrariesForSelfExtract
**What goes wrong:** `dotnet publish` with `PublishSingleFile=true` produces focus.exe PLUS separate native DLL files alongside it, defeating the single-file purpose.
**Why it happens:** By default, native libraries are excluded from the single-file bundle and extracted alongside the exe.
**How to avoid:** Always include `-p:IncludeNativeLibrariesForSelfExtract=true` in the publish command.
**Warning signs:** Multiple files in the publish output directory instead of just focus.exe.

### Pitfall 4: Forgetting nowait on Postinstall Launch
**What goes wrong:** Setup wizard hangs after user clicks Finish because it waits for the daemon process to exit (which it never does -- it's a long-running daemon).
**Why it happens:** Default [Run] behavior is `waituntilterminated`.
**How to avoid:** Always use `Flags: postinstall nowait` for the daemon launch entry.
**Warning signs:** Setup window stays open indefinitely after clicking Finish with "Launch Focus now" checked.

### Pitfall 5: ISS File Encoding
**What goes wrong:** Inno Setup compiler fails or produces garbled strings.
**Why it happens:** ISS files must be saved in UTF-8 with BOM for Unicode support, or plain ANSI.
**How to avoid:** Save the .iss file as UTF-8 with BOM. Most editors handle this, but verify if strings appear garbled.
**Warning signs:** ISCC compilation errors mentioning invalid characters, or garbled text in wizard.

### Pitfall 6: Start Menu Shortcut Missing "daemon" Parameter
**What goes wrong:** Start Menu shortcut launches focus.exe without arguments, which shows CLI help and exits immediately.
**Why it happens:** Forgetting to add `Parameters: "daemon"` to the [Icons] entry.
**How to avoid:** The [Icons] entry MUST include `Parameters: "daemon"` since the exe is a CLI tool that needs the `daemon` subcommand.
**Warning signs:** Clicking Start Menu shortcut briefly shows a console window then closes.

## Code Examples

Verified patterns from official sources:

### Complete ISS Script Structure
```iss
; Source: Inno Setup official docs + project-specific configuration
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "Focus"
#define MyAppExeName "focus.exe"

[Setup]
AppId=Focus
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={username}
DefaultDirName={localappdata}\Focus
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=output
OutputBaseFilename=Focus-Setup
SetupIconFile=..\focus\focus.ico
UninstallDisplayIcon={app}\focus.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Global\focus-daemon
CloseApplications=yes
ArchitecturesInstallIn64BitMode=x64compatible
; No license page, no component selection (minimal wizard)
LicenseFile=
InfoBeforeFile=
InfoAfterFile=

[Files]
Source: "..\focus\bin\Release\net8.0\win-x64\publish\focus.exe"; \
  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Focus"; Filename: "{app}\{#MyAppExeName}"; \
  Parameters: "daemon"; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "daemon"; \
  Description: "Launch Focus now"; \
  Flags: postinstall nowait skipifsilent
```

### Complete build.ps1 Script Structure
```powershell
# build.ps1 - Build Focus and create installer
$ErrorActionPreference = "Stop"

# 1. Read version from csproj
[xml]$csproj = Get-Content "focus/focus.csproj"
$version = $csproj.Project.PropertyGroup.Version
Write-Host "Building Focus v$version"

# 2. Publish self-contained single-file
Write-Host "Publishing self-contained executable..."
dotnet publish focus/focus.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 3. Verify single-file output
$publishDir = "focus/bin/Release/net8.0/win-x64/publish"
$exePath = Join-Path $publishDir "focus.exe"
if (-not (Test-Path $exePath)) { throw "focus.exe not found at $exePath" }
$fileCount = (Get-ChildItem $publishDir -File).Count
if ($fileCount -ne 1) {
    Write-Warning "Expected 1 file in publish dir, found $fileCount"
}

# 4. Compile installer
Write-Host "Compiling installer..."
ISCC.exe /DMyAppVersion="$version" installer/focus.iss
if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed" }

# 5. Report result
$setupPath = "installer/output/Focus-Setup.exe"
if (Test-Path $setupPath) {
    $size = [math]::Round((Get-Item $setupPath).Length / 1MB, 1)
    Write-Host "Success: $setupPath ($size MB)"
} else {
    throw "Focus-Setup.exe not created"
}
```

### .gitignore Additions
```gitignore
# Installer build output
installer/output/
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Inno Setup classic wizard style | WizardStyle=modern (white background, no bevel) | Inno Setup 6.1 | Looks native on Windows 10/11 |
| Manual {pf} vs {userpf} selection | {localappdata} with PrivilegesRequired=lowest | Inno Setup 6.0 | Per-user install is first-class |
| Custom process detection code | CloseApplications=yes + Restart Manager | Inno Setup 5.5+ | OS-level in-use detection |
| Separate native DLLs with PublishSingleFile | IncludeNativeLibrariesForSelfExtract=true | .NET 6+ | True single-file output |
| WizardStyle=classic | WizardStyle=modern dynamic | Inno Setup 6.7.0 | Auto dark/light mode support |

**Current and relevant:**
- Inno Setup 6.7.1 released 2026-02-18 (latest stable)
- ArchitecturesInstallIn64BitMode=x64compatible (newer than the older x64 value; both work)

## Open Questions

1. **AppPublisher value**
   - What we know: User decided "publisher is the user's name" for Add/Remove Programs
   - What's unclear: Whether to hardcode a specific name or use a build-time parameter
   - Recommendation: Hardcode in ISS as a `#define`. The user can change it if needed. Use the repo owner's name.

2. **DisableDirPage behavior**
   - What we know: Minimal wizard with install location page
   - What's unclear: Whether DisableDirPage=no (always show) or DisableDirPage=auto (show only if overridable)
   - Recommendation: Use DisableDirPage=auto. This shows the page when appropriate (first install) and respects UsePreviousAppDir on upgrades.

3. **Version bump workflow**
   - What we know: Version lives in focus.csproj `<Version>4.0.0</Version>`
   - What's unclear: Whether to bump to 5.0.0 for this milestone
   - Recommendation: Keep version reading mechanical in build.ps1; version bump is a manual edit to focus.csproj by the developer.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | Manual testing (installer lifecycle is inherently interactive) |
| Config file | N/A |
| Quick run command | `powershell -File build.ps1` |
| Full suite command | Build + install + verify + uninstall cycle |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PKG-01 | build.ps1 produces Focus-Setup.exe | smoke | `powershell -File build.ps1 && Test-Path installer/output/Focus-Setup.exe` | Wave 0 |
| INST-01 | Install to chosen directory, Add/Remove Programs entry | manual-only | Run Focus-Setup.exe, verify files at install path, check Apps & Features | N/A -- interactive wizard |
| INST-02 | Stops running daemon before upgrade | manual-only | Start daemon, run installer, verify daemon stops | N/A -- requires running daemon |
| INST-03 | Upgrade preserves config | manual-only | Install, modify config, reinstall, verify config unchanged | N/A -- multi-step manual |
| INST-04 | Uninstall removes files and shortcuts | manual-only | Uninstall via Apps & Features, verify files removed | N/A -- interactive |
| INST-05 | "Launch Focus now" checkbox works | manual-only | Install with checkbox checked, verify daemon starts | N/A -- interactive wizard |

### Sampling Rate
- **Per task commit:** `powershell -File build.ps1` (verifies build pipeline works)
- **Per wave merge:** Full install/upgrade/uninstall cycle (manual)
- **Phase gate:** All 6 requirements manually verified before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `installer/focus.iss` -- Inno Setup script (core deliverable)
- [ ] `build.ps1` -- Build orchestration script (core deliverable)
- [ ] `.gitignore` update -- Add `installer/output/` entry
- [ ] Inno Setup 6.7.1 must be installed on build machine with ISCC.exe on PATH

*(Note: Most requirements are manual-only validation. The build script itself is the primary automatable check.)*

## Sources

### Primary (HIGH confidence)
- [Inno Setup AppMutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm) - Mutex detection behavior and syntax
- [Inno Setup CloseApplications](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm) - Restart Manager integration
- [Inno Setup PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) - Per-user install mode
- [Inno Setup PrivilegesRequiredOverridesAllowed](https://jrsoftware.org/ishelp/topic_setup_privilegesrequiredoverridesallowed.htm) - Dialog override option
- [Inno Setup Run Section](https://jrsoftware.org/ishelp/topic_runsection.htm) - Postinstall launch with nowait
- [Inno Setup Files Section](https://jrsoftware.org/ishelp/topic_filessection.htm) - File replacement and ignoreversion
- [Inno Setup Icons Section](https://jrsoftware.org/ishelp/topic_iconssection.htm) - Start Menu shortcuts with parameters
- [Inno Setup ISPP CLI](https://jrsoftware.org/ishelp/topic_isppcc.htm) - /D preprocessor defines from command line
- [Inno Setup Constants](https://jrsoftware.org/ishelp/topic_consts.htm) - {localappdata}, {app}, {group} resolution
- [Inno Setup DefaultDirName](https://jrsoftware.org/ishelp/topic_setup_defaultdirname.htm) - Default directory configuration
- [Inno Setup UninstallDelete](https://jrsoftware.org/ishelp/topic_uninstalldeletesection.htm) - Uninstall cleanup behavior
- [Microsoft .NET Single-File Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) - PublishSingleFile + IncludeNativeLibrariesForSelfExtract
- Verified against source: `focus/Windows/Daemon/DaemonMutex.cs` (mutex name `Global\focus-daemon`)
- Verified against source: `focus/focus.csproj` (Version=4.0.0, AssemblyName=focus)

### Secondary (MEDIUM confidence)
- [Inno Setup Downloads](https://jrsoftware.org/isdl.php) - Version 6.7.1 confirmed current (2026-02-18)
- [Chocolatey InnoSetup 6.7.1](https://community.chocolatey.org/packages/InnoSetup) - Install method reference

### Tertiary (LOW confidence)
- None -- all findings verified with official Inno Setup documentation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Inno Setup is well-documented; all directives verified against official docs
- Architecture: HIGH - Build pipeline is straightforward; version flow pattern is standard
- Pitfalls: HIGH - All pitfalls derived from official docs or verified project source code

**Research date:** 2026-03-05
**Valid until:** 2026-04-05 (Inno Setup is stable; .NET 8 is LTS)
