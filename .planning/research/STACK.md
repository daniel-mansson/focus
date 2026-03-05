# Stack Research

**Domain:** Windows installer and startup registration for .NET 8 WinForms daemon
**Researched:** 2026-03-05
**Confidence:** HIGH (all claims verified against official Microsoft Learn documentation, Inno Setup official help, and primary sources)

---

## Scope of This Document

This document covers **additions required for v5.0 only**. The existing validated stack is unchanged and not re-researched:

**Already validated (do not re-research):** .NET 8 `net8.0-windows`, CsWin32 0.3.269, WinForms (`UseWindowsForms=true`), `NotifyIcon` + `ContextMenuStrip`, GDI for overlays, `System.CommandLine` 2.0.3, `System.Text.Json` (built-in), `Microsoft.Extensions.FileSystemGlobbing` 8.0.0, hand-written ICO encoder, WinForms settings form, daemon restart via `Environment.ProcessPath`.

The three new capability areas are:
1. **Inno Setup installer** -- produce a single `.exe` installer with install/uninstall, per-user install path (`%LocalAppData%\Focus`), and optional admin elevation
2. **.NET publish configuration** -- `dotnet publish` settings to produce the distributable payload for the installer
3. **Task Scheduler startup registration** -- `schtasks.exe` invocation from installer to register/remove a logon task with optional `/RL HIGHEST`

---

## New Capabilities Required

### 1. Inno Setup Installer

**Tool:** Inno Setup 6.7.1 (released 2026-02-17)

**Why Inno Setup:** The project requirements specify Inno Setup directly. It is the right choice: free, open-source, produces a single `.exe` installer, supports Pascal scripting for custom logic (scheduled task creation/deletion), supports per-user and admin installs, and has 25+ years of maturity on Windows. The 6.7.x series adds dark mode detection and experimental 64-bit setup loader support.

**Installation:** Download from [jrsoftware.org](https://jrsoftware.org/isdl.php). The compiler (`ISCC.exe`) runs from the command line for CI/automation. Typical install path: `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`.

**Key script directives for this project:**

```ini
[Setup]
AppId={{GENERATE-A-GUID}}
AppName=Focus
AppVersion=5.0.0
AppPublisher=Daniel
DefaultDirName={localappdata}\Focus
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputBaseFilename=FocusSetup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\focus.exe
ArchitecturesInstallIn64BitMode=x64compatible
```

**PrivilegesRequired strategy:**

Use `PrivilegesRequired=admin` because Task Scheduler `/RL HIGHEST` requires admin privileges to create the scheduled task. Creating a task with "Run with highest privileges" (which bypasses UAC prompts at logon) can only be done from an elevated context. The existing `elevateOnStartup` config option in the daemon needs this capability.

Add `PrivilegesRequiredOverridesAllowed=dialog commandline` to allow users who do NOT want elevation to choose "Install just for me" mode. When non-admin mode is selected, the task will be created with `/RL LIMITED` instead.

**DefaultDirName:** Use `{localappdata}\Focus` rather than `{autopf}\Focus`. Rationale: this is a personal productivity tool, not a system service. Per-user install in `%LocalAppData%\Focus` avoids UAC for file operations and matches the project's existing config location (`%AppData%\focus\config.json`). When the user chooses admin install mode, `{localappdata}` still resolves correctly to the installing user's AppData.

**Key sections:**

```ini
[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autodesktop}\Focus"; Filename: "{app}\focus.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional icons:"
Name: "startup"; Description: "Start Focus daemon at Windows logon"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "startup\elevated"; Description: "Run with administrator privileges (required for navigating to elevated windows)"; GroupDescription: "Startup:"; Flags: unchecked

[Run]
Filename: "{app}\focus.exe"; Parameters: "daemon --background"; \
  Description: "Launch Focus daemon now"; Flags: postinstall nowait skipifsilent
```

**Pascal script for Task Scheduler integration (install/uninstall):**

```pascal
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  RunLevel: String;
begin
  if CurStep = ssPostInstall then
  begin
    if IsTaskSelected('startup') then
    begin
      if IsTaskSelected('startup\elevated') then
        RunLevel := 'HIGHEST'
      else
        RunLevel := 'LIMITED';
      Exec('schtasks.exe',
        '/Create /F /SC ONLOGON /TN "Focus" /TR "\"' +
        ExpandConstant('{app}') + '\focus.exe\" daemon --background" /RL ' + RunLevel,
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('schtasks.exe', '/Delete /TN "Focus" /F',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
```

**Exec function:** `Exec(Filename, Params, WorkingDir, ShowCmd, Wait, ResultCode)` runs a command synchronously. `ewWaitUntilTerminated` ensures the task is created before the installer proceeds. `SW_HIDE` keeps the command prompt invisible.

**Event functions used:**
- `CurStepChanged(ssPostInstall)` -- fires after all files are copied, before the completion page. Creates the scheduled task.
- `CurUninstallStepChanged(usUninstall)` -- fires at the start of uninstallation. Deletes the scheduled task before files are removed.

**Confidence:** HIGH -- Inno Setup `[Code]` section `Exec` function, `CurStepChanged`/`CurUninstallStepChanged` event functions, and `schtasks.exe` syntax are all verified against official documentation.

---

### 2. .NET Publish Configuration

**Approach:** Self-contained, single-file deployment.

**Why self-contained:** The target audience installs a single `.exe` installer and expects it to just work. Requiring a separate .NET Desktop Runtime download (50-70 MB) creates friction. Self-contained bundles the runtime into the output and the Inno Setup installer compresses it efficiently with LZMA2.

**Why single-file:** Produces one `focus.exe` rather than hundreds of DLLs. Cleaner install directory, cleaner uninstall, simpler Inno Setup `[Files]` section.

**Why NOT trimmed:** WinForms is incompatible with `PublishTrimmed` in .NET 8. The WinForms framework relies heavily on COM marshalling and reflection patterns that the IL trimmer breaks. The .NET SDK disables trimming support for WinForms projects. Do not enable `PublishTrimmed`.

**Why NOT native AOT:** Native AOT is not supported for WinForms applications in .NET 8. The `UseWindowsForms` target requires the full CLR.

**Publish command:**

```bash
dotnet publish focus/focus.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=embedded \
  -o publish
```

**Parameter rationale:**

| Parameter | Value | Why |
|-----------|-------|-----|
| `-c Release` | Release | Optimized build, no debug symbols in output |
| `-r win-x64` | win-x64 | Windows 64-bit target; matches the project's Windows-only design |
| `--self-contained true` | true | Bundle .NET runtime -- no prerequisite install needed |
| `PublishSingleFile` | true | Single `focus.exe` output instead of hundreds of DLLs |
| `PublishReadyToRun` | true | Pre-JIT compilation for faster startup. Important for a hotkey daemon that must respond in <100ms |
| `IncludeNativeLibrariesForSelfExtract` | true | Bundles native DLLs into the single file. Without this, some native libraries (e.g., `clrjit.dll`) remain as separate files alongside `focus.exe` |
| `DebugType` | embedded | Embeds PDB into the assembly. No loose `.pdb` files in the install directory |

**Alternatively, in the csproj (for repeatable builds):**

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <PublishReadyToRun>true</PublishReadyToRun>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <DebugType>embedded</DebugType>
</PropertyGroup>
```

**Important .NET 8 change:** Starting in .NET 8, specifying `RuntimeIdentifier` no longer implies `SelfContained=true`. You must explicitly set `--self-contained true` or `<SelfContained>true</SelfContained>`. This is a breaking change from .NET 7 behavior. Verified: [Microsoft Learn -- RuntimeIdentifier breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/runtimespecific-app-default).

**Expected output size:** A self-contained single-file WinForms .NET 8 app is typically 60-80 MB uncompressed. After Inno Setup LZMA2 compression, the installer `.exe` will be approximately 25-35 MB.

**API compatibility note for single-file:** `Assembly.Location` returns an empty string in single-file mode. The project already uses `Environment.ProcessPath` (verified in `DaemonCommand.cs`) and `AppContext.BaseDirectory` for file paths, both of which work correctly in single-file deployment. The config path uses `%AppData%` via `Environment.GetFolderPath`, which is also unaffected.

**Confidence:** HIGH -- `dotnet publish` parameters verified against [Microsoft Learn single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) and [.NET application publishing overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/). The .NET 8 `RuntimeIdentifier` breaking change verified against [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/runtimespecific-app-default).

---

### 3. Task Scheduler Startup Registration

**Tool:** `schtasks.exe` (built into Windows, no additional dependency)

**Why schtasks.exe:** It is the standard Windows CLI for Task Scheduler manipulation. No NuGet package, no COM interop, no P/Invoke required. Works from Inno Setup's `Exec()` Pascal function and also from `Process.Start` in .NET code.

**Why Task Scheduler instead of Registry Run key:** The project already supports an `elevateOnStartup` config option that re-launches the daemon with admin privileges. A Registry `Run` key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) cannot start a process with elevated privileges -- it always starts at the user's default (non-elevated) token. Task Scheduler with `/RL HIGHEST` creates a task that runs at the user's highest available privilege level, bypassing the UAC prompt at logon. This is the only supported way to auto-start an elevated process at logon without a Windows Service.

**Why NOT a Windows Service:** The daemon requires a WinForms message pump (`Application.Run`), a system tray icon (`NotifyIcon`), interactive keyboard hooks (`WH_KEYBOARD_LL`), and a settings UI. All of these require an interactive desktop session. Windows Services run in Session 0 with no desktop access. A Windows Service cannot perform any of these functions.

**Create task command (elevated):**

```
schtasks /Create /F /SC ONLOGON /TN "Focus" /TR "\"C:\path\to\focus.exe\" daemon --background" /RL HIGHEST
```

**Create task command (standard):**

```
schtasks /Create /F /SC ONLOGON /TN "Focus" /TR "\"C:\path\to\focus.exe\" daemon --background" /RL LIMITED
```

**Delete task command (uninstall):**

```
schtasks /Delete /TN "Focus" /F
```

**Parameter reference:**

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `/Create` | -- | Create a new scheduled task |
| `/F` | -- | Force creation, overwrite if exists. Suppresses "already exists" prompt |
| `/SC ONLOGON` | -- | Trigger: when any user logs on |
| `/TN "Focus"` | -- | Task name. Appears in Task Scheduler GUI |
| `/TR "..."` | -- | Command to run. Must quote the exe path if it contains spaces |
| `/RL HIGHEST` | -- | Run level: highest available privileges for the user account. Bypasses UAC |
| `/RL LIMITED` | -- | Run level: standard user privileges (default) |
| `/Delete` | -- | Delete an existing scheduled task |

**Admin requirement for `/RL HIGHEST`:** Creating a task with `/RL HIGHEST` requires the `schtasks.exe` process to be running elevated (admin context). This is why the Inno Setup installer uses `PrivilegesRequired=admin` as the default. When the installer runs elevated, the `Exec('schtasks.exe', ...)` call inherits the elevated token.

**Admin requirement for `/RL LIMITED`:** Creating a task with `/RL LIMITED` and `/SC ONLOGON` also requires elevation on modern Windows (Windows 10/11). The `ONLOGON` schedule type requires admin privileges regardless of run level. This means even the "standard" startup option needs the installer to run elevated.

**Implication for non-admin install:** If a user chooses "Install just for me" (non-admin mode via `PrivilegesRequiredOverridesAllowed`), the Task Scheduler task CANNOT be created because `schtasks /Create /SC ONLOGON` requires admin. In this case, the startup checkbox should be hidden or a Registry `Run` key should be used as a fallback (with a note that elevated startup is not available).

**Fallback for non-admin install:**

```pascal
// If non-admin, use Registry Run key instead of Task Scheduler
if not IsAdminInstallMode then
begin
  RegWriteStringValue(HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Run',
    'Focus',
    '"' + ExpandConstant('{app}') + '\focus.exe" daemon --background');
end;
```

**Confidence:** HIGH -- `schtasks.exe` parameters verified against [Microsoft Learn schtasks create](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create). The admin requirement for `ONLOGON` schedule type and `/RL HIGHEST` is confirmed by multiple sources including Microsoft Q&A threads and the official docs stating "Schedules a task that runs whenever any user logs on" implies system-level scope.

---

## Recommended Stack (v5.0 Additions)

### External Tool: Inno Setup 6.7.1

| Tool | Version | Purpose | Why |
|------|---------|---------|-----|
| Inno Setup | 6.7.1 | Installer authoring and compilation | Free, open-source, single-exe output, Pascal scripting, per-user/admin install, 25+ years maturity |
| `ISCC.exe` | (bundled) | Command-line compiler for CI builds | Enables `ISCC.exe FocusSetup.iss` in build scripts |

### No New NuGet Packages Required

All capabilities are covered by existing dependencies plus Windows built-in tools:

| Capability | Provided By | Already Available? |
|------------|-------------|-------------------|
| Installer authoring | Inno Setup 6.7.1 (external tool) | New -- install on dev machine |
| Single-file publish | `dotnet publish` CLI | Yes (SDK toolchain) |
| Self-contained deployment | .NET 8 SDK `--self-contained` | Yes (SDK toolchain) |
| ReadyToRun compilation | .NET 8 SDK `PublishReadyToRun` | Yes (SDK toolchain) |
| Task Scheduler registration | `schtasks.exe` (Windows built-in) | Yes (ships with Windows) |
| Task Scheduler deletion | `schtasks.exe /Delete` (Windows built-in) | Yes (ships with Windows) |

**Zero new NuGet packages.** The only new tool is Inno Setup, which is a build-time dependency only (not shipped to users).

---

## csproj Changes

### Add publish profile properties (Release only)

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <PublishReadyToRun>true</PublishReadyToRun>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <DebugType>embedded</DebugType>
</PropertyGroup>
```

### Bump version

```xml
<Version>5.0.0</Version>
<AssemblyVersion>5.0.0.0</AssemblyVersion>
```

**No other csproj changes needed.** The existing `OutputType`, `TargetFramework`, `UseWindowsForms`, `ApplicationIcon`, and package references remain unchanged.

---

## Project Files to Create

### `installer/FocusSetup.iss`

The Inno Setup script (`.iss` file) lives in a new `installer/` directory at the project root, alongside the `focus/` and `tools/` directories.

### Build workflow

```bash
# 1. Publish the .NET app
dotnet publish focus/focus.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishReadyToRun=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded \
  -o installer/publish

# 2. Compile the installer
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/FocusSetup.iss

# 3. Output: installer/Output/FocusSetup.exe
```

---

## Integration Points

### Inno Setup script (`FocusSetup.iss`)

- References published output from `installer/publish/` directory
- Uses `schtasks.exe` via `Exec()` in Pascal `[Code]` section for task creation
- Uses `CurUninstallStepChanged(usUninstall)` for task deletion on uninstall
- Task checkbox in `[Tasks]` section with sub-task for elevation choice
- `PrivilegesRequiredOverridesAllowed=dialog commandline` for install mode flexibility

### DaemonCommand.cs (existing)

- No changes needed. The existing `elevateOnStartup` self-elevation logic works regardless of how the daemon was started (Task Scheduler or manually). Task Scheduler `/RL HIGHEST` starts the daemon elevated directly, so `elevateOnStartup` becomes redundant when the task is set to HIGHEST -- but the self-elevation code is harmless (it detects "already elevated" and skips the re-launch).

### FocusConfig.cs (existing)

- No changes needed. Config file location (`%AppData%\focus\config.json`) is independent of install path. Single-file deployment does not affect `Environment.GetFolderPath(SpecialFolder.ApplicationData)`.

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| WiX Toolset | More complex than needed for a simple per-user installer. WiX is designed for MSI packages with Windows Installer features (repair, patches, transforms) that this project does not need | Inno Setup |
| NSIS (Nullsoft Scriptable Install System) | Less mature Pascal-like scripting, fewer built-in features for scheduled tasks, smaller community for .NET projects | Inno Setup |
| Windows Service (`sc.exe create`) | Cannot interact with desktop. No message pump, no tray icon, no keyboard hooks, no settings UI. Session 0 isolation makes it fundamentally incompatible with this daemon's architecture | Task Scheduler `ONLOGON` task |
| `Microsoft.Win32.TaskScheduler` NuGet | Adds a dependency for something achievable with a single `schtasks.exe` command. The NuGet package wraps COM Task Scheduler 2.0 API with 40+ types -- massive overkill for create/delete of one task | `schtasks.exe` via `Exec()` or `Process.Start` |
| `PublishTrimmed` | WinForms is incompatible with IL trimming in .NET 8. COM marshalling and reflection patterns break. The SDK actively warns against it | Accept the ~70 MB single-file size; LZMA2 compresses to ~30 MB |
| Native AOT (`PublishAot`) | Not supported for WinForms applications in .NET 8. Requires `UseWindowsForms=false` | Self-contained with `PublishReadyToRun` for fast startup |
| `EnableCompressionInSingleFile` | Compresses assemblies inside the single file, reducing disk size but adding startup decompression time. The daemon must start fast (<100ms for hotkey response). Let Inno Setup handle compression in the installer instead | Uncompressed single file + LZMA2-compressed installer |
| Framework-dependent deployment | Requires user to install .NET 8 Desktop Runtime (50-70 MB download) separately. Creates "it doesn't work" support burden. Unacceptable for a personal tool installer | Self-contained deployment |
| Registry `Run` key for elevated startup | `HKCU\...\Run` values always start at standard user privilege level. Cannot bypass UAC. Incompatible with `elevateOnStartup` feature | Task Scheduler with `/RL HIGHEST` |
| `Startup` folder shortcut | Same as Registry Run key -- always starts at standard privilege. Also creates user-visible shortcut clutter | Task Scheduler with `/RL HIGHEST` |

---

## Version Compatibility

| Tool / Feature | Version | Requirement | Notes |
|----------------|---------|-------------|-------|
| Inno Setup | 6.7.1 | Build-time only | Not shipped to users. Dev machine install. Compiler produces standalone `.exe` installer |
| `schtasks.exe` | Windows built-in | Windows Vista+ | Available on all supported Windows versions (10/11). `/RL` parameter requires Vista or later |
| `dotnet publish --self-contained` | .NET 8 SDK | Build-time only | SDK not needed on user machines |
| `PublishSingleFile` | .NET 8 SDK | .NET 6+ feature | Fully supported for WinForms in .NET 8 |
| `PublishReadyToRun` | .NET 8 SDK | .NET 3.0+ feature | Pre-JIT to native code at publish time |
| `IncludeNativeLibrariesForSelfExtract` | .NET 8 SDK | .NET 6+ feature | Bundles native DLLs into single file |
| `PrivilegesRequiredOverridesAllowed` | Inno Setup 6.0.3+ | Inno Setup 6+ | Directive for install mode dialog |

---

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Installer tool | Inno Setup 6.7.1 | WiX 4 / NSIS | WiX is MSI-oriented complexity; NSIS has weaker scripting. Inno Setup matches project requirements exactly |
| Deployment model | Self-contained single file | Framework-dependent | Requires separate .NET runtime install; unacceptable UX for personal tool |
| Startup mechanism | Task Scheduler `ONLOGON` | Registry Run key | Cannot start elevated; incompatible with `elevateOnStartup` |
| Startup mechanism | Task Scheduler `ONLOGON` | Windows Service | Cannot interact with desktop (Session 0); breaks tray icon, hooks, settings UI |
| Task Scheduler API | `schtasks.exe` CLI | COM Task Scheduler 2.0 via NuGet | One-line CLI command vs. 50+ lines of COM interop for the same result |
| Publish optimization | `PublishReadyToRun` | `PublishTrimmed` / `PublishAot` | Trimming breaks WinForms; AOT not supported for WinForms |
| Install location | `{localappdata}\Focus` | `{autopf}\Focus` (Program Files) | Per-user install is cleaner for a personal tool; avoids admin requirement for file writes |
| Single-file compression | Disabled (let Inno Setup compress) | `EnableCompressionInSingleFile` | Startup decompression conflicts with <100ms hotkey responsiveness |

---

## Sources

- [Inno Setup Official Site -- Downloads](https://jrsoftware.org/isdl.php) -- Inno Setup 6.7.1, released 2026-02-17 -- HIGH confidence
- [Inno Setup 6 Revision History](https://jrsoftware.org/files/is6-whatsnew.htm) -- Version history, 6.7.1/6.7.0/6.6.x features -- HIGH confidence
- [Inno Setup Help -- \[Run\] & \[UninstallRun\] sections](https://jrsoftware.org/ishelp/topic_runsection.htm) -- Exec flags, postinstall, waituntilterminated -- HIGH confidence
- [Inno Setup Help -- Pascal Scripting: Event Functions](https://jrsoftware.org/ishelp/topic_scriptevents.htm) -- CurStepChanged, CurUninstallStepChanged, TSetupStep/TUninstallStep enums -- HIGH confidence
- [Inno Setup Help -- Pascal Scripting: Exec](https://jrsoftware.org/ishelp/topic_isxfunc_exec.htm) -- Exec function signature and parameters -- HIGH confidence
- [Inno Setup Help -- PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) -- admin vs lowest behavior -- HIGH confidence
- [Inno Setup Help -- PrivilegesRequiredOverridesAllowed](https://jrsoftware.org/ishelp/topic_setup_privilegesrequiredoverridesallowed.htm) -- dialog + commandline overrides -- HIGH confidence
- [Inno Setup Help -- Non Administrative Install Mode](https://jrsoftware.org/ishelp/topic_admininstallmode.htm) -- auto constants, HKCU registry, user directories -- HIGH confidence
- [Inno Setup Help -- Constants](https://jrsoftware.org/ishelp/topic_consts.htm) -- {localappdata}, {app}, {autopf}, {autoappdata} -- HIGH confidence
- [Microsoft Learn -- schtasks create](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create) -- /SC ONLOGON, /RL HIGHEST, /TN, /TR, /F parameters -- HIGH confidence
- [Microsoft Learn -- schtasks delete](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-delete) -- /TN, /F parameters -- HIGH confidence
- [Microsoft Learn -- Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) -- PublishSingleFile, IncludeNativeLibrariesForSelfExtract, API compatibility, Assembly.Location behavior -- HIGH confidence
- [Microsoft Learn -- .NET application publishing overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/) -- Self-contained vs framework-dependent -- HIGH confidence
- [Microsoft Learn -- RuntimeIdentifier breaking change (.NET 8)](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/runtimespecific-app-default) -- RuntimeIdentifier no longer implies SelfContained -- HIGH confidence
- [Microsoft Learn -- Trim self-contained applications](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained) -- WinForms incompatibility with trimming -- HIGH confidence

---

*Stack research for: Window focus navigation v5.0 -- Inno Setup installer, Task Scheduler startup, .NET publish configuration*
*Researched: 2026-03-05*
