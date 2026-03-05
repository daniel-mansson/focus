# Feature Landscape

**Domain:** Installer/distribution for a C#/.NET 8 desktop daemon (Inno Setup)
**Researched:** 2026-03-05
**Confidence:** HIGH (Inno Setup documentation is stable and authoritative; Task Scheduler via schtasks is well-documented Win32 tooling; .NET publish options verified against official Microsoft docs)

> **Scope note:** This file covers v5.0 features only -- packaging Focus as an Inno Setup installer with Task Scheduler startup registration. All prior features (navigation, overlays, grid move/resize, tray icon, settings UI) are shipped in v1.0-v4.0 and are treated as existing dependencies.

---

## Table Stakes

Features users expect from any desktop app installer. Missing these = product feels broken or amateur.

| Feature | Why Expected | Complexity | Dependencies | Notes |
|---------|--------------|------------|--------------|-------|
| **Install to user-chosen directory** | Standard installer UX; users expect to pick install location | Low | None | Default to `%LocalAppData%\Focus` via Inno Setup `{localappdata}\Focus` constant. Per-user path avoids UAC for basic install. |
| **Single setup.exe output** | Users expect one file to download and run | Low | Build pipeline | Inno Setup compiles `.iss` script into a single compressed `.exe` installer. |
| **Uninstall via Add/Remove Programs** | Windows standard; users expect system-level uninstall | Low | None | Inno Setup auto-registers in Add/Remove Programs by default. No extra config needed. |
| **Clean uninstall (files + scheduled task)** | Leftover files or startup tasks after uninstall feel like malware | Med | Task Scheduler cleanup code | Must remove: installed files (automatic), Start Menu shortcut (automatic), scheduled task (manual Pascal code). |
| **Upgrade in-place** | Users re-run installer for new version; must overwrite old files without breaking config | Med | Running daemon detection | Use `ignoreversion` flag on all app files in `[Files]` section. Config in `%AppData%` is untouched. |
| **Stop running daemon before install/upgrade** | Daemon holds file locks; installer cannot overwrite locked `.exe` | Med | Existing mutex `Global\focus-daemon` | Use `AppMutex=Global\focus-daemon` in `[Setup]` -- Inno Setup detects the mutex and prompts user. Fallback: `CloseApplications=force`. |
| **Start Menu shortcut** | Standard discoverability for tray apps | Low | None | Shortcut should launch `focus.exe daemon` (not bare `focus.exe` which shows CLI help). |
| **Startup registration (run at logon)** | Daemon must survive reboots; user should not manually launch after every logon | High | Task Scheduler, elevation choice | Core value proposition of this milestone. Detailed analysis below. |

---

## Differentiators

Features that elevate the installer beyond a bare "copy files" approach. Not strictly expected, but meaningful for a developer tool.

| Feature | Value Proposition | Complexity | Dependencies | Notes |
|---------|-------------------|------------|--------------|-------|
| **Task Scheduler with elevation choice** | Users who need admin-window navigation get `HIGHEST` run level; others avoid UAC prompt on every boot. Existing `ElevateOnStartup` config is respected. | High | Existing `ElevateOnStartup` config value, admin context for elevated task creation | Two schtasks variants: standard (`/RL LIMITED`) and elevated (`/RL HIGHEST`). Installer checkbox synced with existing config. |
| **Per-user install (no admin required for standard mode)** | Developer tool installs without IT approval; `%LocalAppData%` is writable by current user | Med | `PrivilegesRequired=lowest` | Only the "run elevated" Task Scheduler option needs admin. Base install should not require elevation. |
| **Preserve user config across upgrades** | Config in `%AppData%\focus\config.json` is NOT in install directory; installer should never touch it | Low | Config path already outside install dir | Do not include `%AppData%\focus\` in `[Files]` or `[UninstallDelete]`. Config survives naturally. |
| **Launch daemon after install** | Immediate usability; user does not have to reboot or manually find the app | Low | None | `[Run]` section with `postinstall` and `nowait` flags: `focus.exe daemon`. Checkbox "Launch Focus now" defaulting to checked. |
| **Add to user PATH** | CLI usage (`focus left`, `focus right`) works from any terminal without full path | Low | None | Inno Setup `[Registry]` section appending `{app}` to `HKCU\Environment\Path`. Enables both daemon and CLI use. |
| **Desktop shortcut (optional)** | Some users want desktop access, most do not for a tray-only app | Low | None | `[Tasks]` section with unchecked default. Points to `focus.exe daemon`. |

---

## Anti-Features

Features to explicitly NOT build for the installer.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Bundled .NET runtime installer** | Focus should publish as self-contained. Requiring .NET 8 Desktop Runtime adds download complexity, version conflicts, and a second UAC prompt. | Publish with `--self-contained true` + `PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`. All runtime files bundled into installer payload. |
| **Registry Run key for startup** | `HKCU\...\Run` cannot run with elevated privileges. Also provides no "stop if running too long" protection or any scheduling control. | Use Task Scheduler exclusively. It supports elevation via `/RL HIGHEST`, is the modern Windows approach, and is cleanly removable. |
| **MSIX/AppX packaging** | Adds Store packaging complexity, sandboxing constraints that conflict with `WH_KEYBOARD_LL` hooks and `SetForegroundWindow`, and certificate requirements. | Stick with Inno Setup `.exe` installer. Simple, well-understood, no sandbox restrictions. |
| **Auto-update mechanism** | Significant complexity (update server, delta downloads, signature verification, rollback). Premature for a developer tool with GitHub releases. | Ship new installer versions on GitHub Releases. Users re-run installer to upgrade. |
| **MSI format (WiX)** | WiX/MSI toolchain is significantly more complex than Inno Setup for no meaningful benefit. No enterprise deployment requirement exists. | Inno Setup `.exe` is sufficient for a single-user developer tool. |
| **Delete user config on uninstall** | User may reinstall later and expect their settings preserved. Deleting config is hostile and violates principle of least surprise. | Leave `%AppData%\focus\` untouched on uninstall. |
| **Multiple install configurations (typical/custom/full)** | Only one component to install. Multiple configurations add UI confusion for zero benefit. | Single install path with optional checkboxes for shortcuts and startup task. |
| **Code signing** | Requires purchasing an EV certificate ($200-400/year minimum). SmartScreen warning is acceptable for a developer tool distributed via GitHub. | Skip for v5.0. Document the SmartScreen bypass ("More info" then "Run anyway") in release notes. Add signing later if user base grows. |
| **All-users/per-user install dialog** | Adds a wizard page asking a question most users cannot answer. Per-user install is correct for a single-user developer tool. | Default to per-user with `PrivilegesRequired=lowest`. Power users can force all-users via `/ALLUSERS` command-line switch if needed. |
| **Startup folder shortcut** | Same limitation as Registry Run key -- cannot run elevated. Also less controllable than Task Scheduler (no timeout settings, no run level). | Task Scheduler handles all startup scenarios. |

---

## Detailed Feature Analysis

### Startup Registration via Task Scheduler

This is the highest-complexity feature in the milestone and the primary reason the installer exists.

**Why Task Scheduler over alternatives:**

| Approach | Elevation Support | Clean Removal | Controllable | Verdict |
|----------|-------------------|---------------|-------------|---------|
| Registry Run key (`HKCU\...\Run`) | No (always standard user) | Yes | Limited | Insufficient -- cannot run elevated |
| Startup folder shortcut | No | Yes | None | Same limitation as Registry |
| Task Scheduler | Yes (`/RL HIGHEST`) | Yes (`schtasks /Delete`) | Full (triggers, conditions, settings) | Correct choice |
| Windows Service | Yes | Complex | Complex | Overkill for user-facing GUI daemon |

**Implementation approach using schtasks.exe:**

Standard (non-elevated) task:
```
schtasks /Create /TN "Focus" /TR "\"{app}\focus.exe\" daemon" /SC ONLOGON /RL LIMITED /F
```

Elevated (admin) task:
```
schtasks /Create /TN "Focus" /TR "\"{app}\focus.exe\" daemon" /SC ONLOGON /RU "%USERNAME%" /RL HIGHEST /F
```

**Critical implementation details:**
- `/SC ONLOGON` -- triggers at interactive user logon, not system boot
- `/RL HIGHEST` -- grants full admin token; only works for admin-group users
- `/RU "%USERNAME%"` -- required with `HIGHEST` to associate the correct user
- `/F` -- forces overwrite if task already exists (enables clean upgrade path)
- Task name `"Focus"` must be identical in install, upgrade, and uninstall code paths
- **Default "Stop task if running longer than 3 days" must be overridden** -- schtasks creates tasks with a 72-hour timeout by default, but the daemon runs indefinitely. Either use an XML task definition (imported via `schtasks /Create /XML`) or disable the timeout via PowerShell `Set-ScheduledTask` after creation.

**Installer UI integration:**
- Checkbox in `[Tasks]` section: "Start Focus automatically at logon" (default: checked)
- Sub-checkbox: "Run with administrator privileges (required for navigating admin windows)" (default: matches existing `ElevateOnStartup` config value)
- The "administrator" sub-option should only be actionable when the installer is running with admin context

### Stopping the Running Daemon

The daemon creates a named mutex `Global\focus-daemon` and has replace semantics (new instance kills old). For the installer:

1. **`AppMutex=Global\focus-daemon`** (primary) -- Inno Setup detects the mutex at startup and shows: "Setup has detected that Focus is currently running. Please close all instances of it now, then click OK to continue, or Cancel to exit."
2. **`CloseApplications=force`** (secondary) -- Uses Windows Restart Manager API to request graceful close. Note: the daemon does NOT register with Restart Manager, so this may not work. Include as belt-and-suspenders.
3. **`taskkill /f /im focus.exe`** (fallback) -- In `[Code]` `CurStepChanged(ssInstall)` callback. Nuclear option if the above fail.

**AppMutex is the right primary approach** because the mutex already exists in the codebase and Inno Setup natively supports it.

### Self-Contained Publish

The `dotnet publish` command for the installer payload:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**Key considerations:**
- **.NET 8 breaking change**: `RuntimeIdentifier` no longer implies `SelfContained` -- must set explicitly
- `IncludeNativeLibrariesForSelfExtract=true` is needed because WinForms depends on native DLLs
- Output: single `focus.exe` (~60-80MB self-contained) -- acceptable for an installer payload (Inno Setup compresses well)
- Use CLI `dotnet publish`, NOT Visual Studio publish dialog (known reliability issues with VS for WinForms single-file: [dotnet/winforms#11473](https://github.com/dotnet/winforms/issues/11473))
- First-run extraction delay is minimal and only occurs once

### Per-User Install (No Admin)

| Aspect | Per-User (Recommended Default) | All-Users (Optional Override) |
|--------|-------------------------------|-------------------------------|
| Install path | `%LocalAppData%\Focus` | `%ProgramFiles%\Focus` |
| Admin required for install | No | Yes |
| Config path | `%AppData%\focus\config.json` (unchanged) | Same (already per-user) |
| Task Scheduler | Works (standard or elevated) | Works |
| Inno Setup directive | `PrivilegesRequired=lowest` | `/ALLUSERS` command line |

**Recommendation:** Use `PrivilegesRequired=lowest` with `PrivilegesRequiredOverridesAllowed=commandline`. Default is per-user (no UAC), power users can force all-users with `/ALLUSERS` CLI switch. Do NOT use the interactive dialog (`PrivilegesRequiredOverridesAllowed=dialog`) as it adds confusion for a single-user tool.

**Exception:** If the user checks "Run with administrator privileges" for the startup task, the installer will need to prompt for elevation to create a `HIGHEST` scheduled task. This is a targeted elevation, not a full admin install.

### Clean Uninstall

Artifacts to remove on uninstall:

| Artifact | Location | Removal Method | Automatic? |
|----------|----------|----------------|------------|
| Application files | `{app}\` | Inno Setup file removal | Yes |
| Start Menu shortcut | `{group}\` | Inno Setup shortcut removal | Yes |
| Desktop shortcut | `{userdesktop}\` | Inno Setup shortcut removal (if created) | Yes |
| Scheduled task | Task Scheduler `Focus` | `schtasks /Delete /TN "Focus" /F` in `CurUninstallStepChanged` | No -- manual Pascal code |
| Uninstall registry entry | ARP | Inno Setup auto-removal | Yes |
| PATH entry | `HKCU\Environment\Path` | Inno Setup `[Registry]` with `uninsdeletevalue` | Yes (if configured) |
| User config | `%AppData%\focus\` | **DO NOT REMOVE** | N/A -- intentionally preserved |

The `CurUninstallStepChanged` Pascal callback for scheduled task removal:

```pascal
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Exec('schtasks.exe', '/Delete /TN "Focus" /F',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
```

---

## Feature Dependencies

```
Self-contained publish ──> Installer payload (files to package)
                              |
                              v
                    Inno Setup .iss script
                              |
                    +---------+---------+
                    v         v         v
              File install  Shortcuts  Task Scheduler
                    |                      |
                    v                      v
              AppMutex          Elevation choice UI
              (stop daemon)     (checkbox in [Tasks])
                    |                      |
                    v                      v
              Upgrade path      Uninstall cleanup
              (overwrite)       (schtasks /Delete)
```

**Ordering constraints:**
1. **Publish config** must be finalized before Inno Setup script references output paths
2. **AppMutex** name must match existing `Global\focus-daemon` constant in `DaemonMutex.cs`
3. **Task name** (`"Focus"`) must be identical in create (install) and delete (uninstall) paths
4. **Elevation checkbox** depends on installer running with admin context (non-elevated installer cannot create `HIGHEST` tasks)
5. **PATH registration** should use the same `{app}` constant as file installation

**Dependency on existing codebase:**
- Named mutex `Global\focus-daemon` in `DaemonMutex.cs` -- used by `AppMutex` directive
- Config path `%AppData%\focus\config.json` in `FocusConfig.cs` -- install must not conflict
- `ElevateOnStartup` config property -- installer reads this to default the elevation checkbox
- Executable name `focus.exe` -- hardcoded in `DaemonMutex.cs` process kill logic; installer must install under this name
- App manifest `app.manifest` -- no `requestedExecutionLevel` override (runs as invoker); elevation via Task Scheduler or `runas` verb

---

## MVP Recommendation

**Must ship (table stakes for v5.0):**
1. Self-contained publish pipeline (`dotnet publish` with correct flags)
2. Inno Setup `.iss` script with file installation to `%LocalAppData%\Focus`
3. `AppMutex`-based daemon shutdown before install/upgrade
4. Task Scheduler registration with standard/elevated choice
5. Clean uninstall (files + scheduled task removal)
6. Start Menu shortcut to `focus.exe daemon`
7. Post-install launch option ("Launch Focus now" checkbox)

**Include (low effort, high polish):**
8. Add `{app}` to user PATH for CLI usage
9. Optional desktop shortcut (unchecked by default)

**Defer:**
- **Code signing**: Cost and certificate management. SmartScreen warning is acceptable for dev tools on GitHub.
- **Auto-update**: Server infrastructure. Users re-run installer for updates.
- **MSIX packaging**: Sandbox conflicts with keyboard hooks.
- **All-users install dialog**: Per-user default with `/ALLUSERS` CLI override covers all cases.

---

## Complexity Assessment

| Feature | Complexity | Rationale |
|---------|------------|-----------|
| Inno Setup `.iss` script (basic) | Low | Well-documented `[Setup]`/`[Files]`/`[Icons]` sections; patterns are cookie-cutter |
| Self-contained publish command | Low | Single `dotnet publish` invocation with known flags |
| `AppMutex` daemon stop | Low | One directive in `[Setup]`; mutex already exists in code |
| File installation with `ignoreversion` | Low | Standard `[Files]` section pattern |
| Start Menu + desktop shortcuts | Low | Standard `[Icons]` and `[Tasks]` sections |
| Post-install launch | Low | Standard `[Run]` section with `postinstall` flag |
| PATH registration | Low | `[Registry]` section with `HKCU\Environment\Path` |
| Task Scheduler create (standard) | Med | `schtasks` invocation in `[Run]` or `[Code]`; must handle 72-hour timeout override |
| Task Scheduler create (elevated) | Med-High | Needs admin context, `/RU` with username, conditional logic based on installer checkbox |
| Task Scheduler remove on uninstall | Med | Pascal `[Code]` callback; must handle task-not-found gracefully |
| Elevation choice UI in installer | Med | Custom `[Tasks]` checkbox with conditional `[Run]`/`[Code]` logic |
| Upgrade handling (full cycle) | Med | Combine `AppMutex` + `ignoreversion` + task recreation; test matrix of old-to-new scenarios |

**Overall milestone complexity: MEDIUM.** Inno Setup is well-documented with extensive examples. The main complexity is Task Scheduler integration (elevation variants, 72-hour timeout override, clean uninstall) and testing the upgrade path end-to-end.

---

## Sources

- [Inno Setup official documentation](https://jrsoftware.org/ishelp/) -- HIGH confidence
- [Inno Setup AppMutex directive](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm) -- HIGH confidence
- [Inno Setup CloseApplications directive](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm) -- HIGH confidence
- [Inno Setup PrivilegesRequired directive](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) -- HIGH confidence
- [Inno Setup PrivilegesRequiredOverridesAllowed directive](https://jrsoftware.org/ishelp/topic_setup_privilegesrequiredoverridesallowed.htm) -- HIGH confidence
- [Inno Setup Files section (ignoreversion flag)](https://jrsoftware.org/ishelp/topic_filessection.htm) -- HIGH confidence
- [Inno Setup Tasks section](https://jrsoftware.org/ishelp/topic_taskssection.htm) -- HIGH confidence
- [Inno Setup Run and UninstallRun sections](https://jrsoftware.org/ishelp/topic_runsection.htm) -- HIGH confidence
- [.NET single-file deployment overview (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) -- HIGH confidence
- [.NET 8 breaking change: RuntimeIdentifier no longer implies SelfContained](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/runtimespecific-app-default) -- HIGH confidence
- [WinForms single-file publish issue (dotnet/winforms#11473)](https://github.com/dotnet/winforms/issues/11473) -- MEDIUM confidence (workaround confirmed via CLI, VS dialog issue unresolved)
- [Inno Setup Knowledge Base: Start with Windows](https://jrsoftware.org/iskb.php?startwithwindows=) -- HIGH confidence
- [InnoDependencyInstaller (NOT recommended for self-contained apps)](https://github.com/DomGries/InnoDependencyInstaller) -- HIGH confidence (exists but not applicable)
- [Creating a Non-Admin Installer with Inno Setup](https://kinook.com/blog2/inno-setup.html) -- MEDIUM confidence (practitioner guide, patterns verified against official docs)

---

*Feature research for: installer/distribution -- Inno Setup packaging with Task Scheduler startup (v5.0 milestone)*
*Researched: 2026-03-05*
