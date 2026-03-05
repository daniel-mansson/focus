# Project Research Summary

**Project:** Window Focus Navigation v5.0 -- Installer & Startup Registration
**Domain:** Windows desktop application packaging (Inno Setup installer + Task Scheduler integration for .NET 8 WinForms daemon)
**Researched:** 2026-03-05
**Confidence:** HIGH

## Executive Summary

Focus v5.0 packages an existing .NET 8 WinForms daemon as a proper installable application. The research is unanimous on the core approach: an Inno Setup installer wrapping a self-contained `dotnet publish` output, with Task Scheduler handling logon startup (replacing Registry Run keys, which cannot support the existing elevated-startup feature). No new NuGet packages are needed. The only new external tool is Inno Setup 6.7.1, a build-time dependency. The existing codebase requires zero C# modifications -- the installer integrates entirely through external tooling (Inno Setup script, `schtasks.exe`, `dotnet publish` flags) and a build-time csproj property group.

The recommended approach is a two-step build pipeline: (1) `dotnet publish` with self-contained and ReadyToRun flags producing the deployable payload, then (2) Inno Setup compiling that payload into a single `Focus-Setup.exe`. The installer defaults to per-user install at `%LocalAppData%\Focus`, offers a checkbox for Task Scheduler logon startup with an optional elevation sub-choice, and handles clean uninstall including scheduled task deletion. The daemon's existing `AppMutex` (`Global\focus-daemon`) enables the installer to detect a running instance during upgrades.

The primary risks center on the elevation model: tension between per-user install (no UAC) and elevated Task Scheduler tasks (requires admin). The research identifies 15 specific pitfalls, with 5 rated critical. The most dangerous are (a) locked-file upgrades when the daemon is running, (b) Task Scheduler creating a non-interactive session that hides the tray icon, (c) elevation context causing path resolution to the wrong user profile, and (d) orphaned scheduled tasks surviving uninstall. All have well-documented mitigations. The overall milestone complexity is MEDIUM -- Inno Setup is mature tooling with 25+ years of documentation, and the integration surface with the existing daemon is narrow.

## Key Findings

### Recommended Stack

No new runtime dependencies. The v5.0 additions are entirely build-time tooling.

**Core technologies:**
- **Inno Setup 6.7.1**: Installer authoring -- free, open-source, produces single `.exe`, Pascal scripting for Task Scheduler integration, supports per-user and admin install modes
- **`dotnet publish` (self-contained)**: Deployment model -- bundles .NET 8 runtime so users need no prerequisites; ~60-80 MB uncompressed, ~25-35 MB after LZMA2 compression
- **`PublishReadyToRun`**: Startup optimization -- pre-JIT compilation critical for <100ms hotkey response time
- **`schtasks.exe`**: Task Scheduler CLI -- Windows built-in, no NuGet package needed, handles create/delete of logon tasks with elevation control
- **Zero new NuGet packages**: All capabilities covered by existing stack + Windows built-ins + Inno Setup

**Critical version note:** .NET 8 changed `RuntimeIdentifier` to no longer imply `SelfContained=true`. Must explicitly pass `--self-contained true`.

**Resolved disagreement -- PublishSingleFile:** STACK.md recommends it; ARCHITECTURE.md recommends against it. **Recommendation: Use PublishSingleFile=true with IncludeNativeLibrariesForSelfExtract=true.** Rationale: a single `focus.exe` in the install directory is cleaner for users who browse their AppData, and Inno Setup's `[Files]` section becomes trivial. The "double compression" concern from ARCHITECTURE.md is minor -- disable `EnableCompressionInSingleFile` and let Inno Setup handle all compression. The virus scanner concern is speculative and not validated.

**Resolved disagreement -- PrivilegesRequired:** STACK.md says `admin`; FEATURES.md and ARCHITECTURE.md say `lowest`. **Recommendation: Use `PrivilegesRequired=lowest` with `PrivilegesRequiredOverridesAllowed=dialog`.** Rationale: per-user install without UAC is the right default for a developer tool. If the user wants elevated Task Scheduler startup, the dialog override lets them elevate. For the standard (non-elevated) startup task, a `/RL LIMITED` task with `/SC ONLOGON` still requires admin -- so the targeted elevation via `ShellExec('runas', 'schtasks.exe', ...)` approach from Pitfalls research is the cleanest solution for that specific operation.

### Expected Features

**Must have (table stakes):**
- Single setup.exe installer with install/uninstall via Add/Remove Programs
- Install to user-chosen directory (default `%LocalAppData%\Focus`)
- Stop running daemon before upgrade (`AppMutex=Global\focus-daemon`)
- Task Scheduler logon startup with standard/elevated choice
- Clean uninstall (files + scheduled task + shortcuts)
- Start Menu shortcut launching `focus.exe daemon --background`
- Post-install "Launch Focus now" checkbox

**Should have (differentiators):**
- Optional desktop shortcut (unchecked by default)
- Preserve user config across upgrades (config at `%AppData%\focus\` is untouched)
- Add `{app}` to user PATH for CLI usage from any terminal

**Defer to v6+:**
- Code signing (cost, SmartScreen reputation building takes weeks)
- Auto-update mechanism (server infrastructure, premature for GitHub-released dev tool)
- MSIX/AppX packaging (sandbox conflicts with keyboard hooks)
- Winget/Chocolatey packaging (can reuse the Inno Setup `.exe` later)
- ARM64 build (separate publish target, no user demand yet)

### Architecture Approach

The installer is a pure build-time artifact with no runtime coupling to the daemon. The integration surface is minimal: the Inno Setup script references the `dotnet publish` output directory, uses the existing daemon mutex name for upgrade detection, and invokes `schtasks.exe` for startup registration. The daemon's existing `ElevateOnStartup` self-elevation mechanism coexists safely with Task Scheduler elevation -- when the task starts the daemon elevated, the self-elevation check detects "already elevated" and becomes a no-op.

**Major components:**
1. **csproj publish properties** -- Release-only PropertyGroup adding self-contained, single-file, and ReadyToRun flags
2. **`installer/focus-setup.iss`** -- Inno Setup script defining install/uninstall flow, file packaging, shortcuts, Task Scheduler integration via Pascal `[Code]`
3. **Build script** -- Two-step pipeline: `dotnet publish` then `ISCC.exe focus-setup.iss`
4. **Task Scheduler integration** -- Pascal script `CurStepChanged`/`CurUninstallStepChanged` event handlers calling `schtasks.exe`

**Key architectural decision:** The installer does NOT touch `%AppData%\focus\config.json`. Config is owned by the daemon runtime. This separation means upgrades never destroy user settings.

### Critical Pitfalls

1. **Locked files during upgrade** -- Daemon holds `.exe` lock; installer fails or corrupts. Prevention: `AppMutex=Global\focus-daemon` + `CloseApplications=yes` + `taskkill` fallback in `PrepareToInstall()`.
2. **Non-interactive session from Task Scheduler** -- "Run whether logged on or not" pushes daemon to Session 0 with no tray icon or UI. Prevention: always use `/SC ONLOGON` trigger (interactive logon) and never use `/RU SYSTEM`.
3. **Elevation context resolves wrong user paths** -- Admin installer resolves `{localappdata}` to admin profile, not actual user. Prevention: `PrivilegesRequired=lowest` as default; targeted elevation only for `schtasks.exe` call.
4. **Orphaned scheduled task after uninstall** -- Task survives, launches deleted exe on every logon. Prevention: `schtasks /Delete /TN "Focus" /F` in `[UninstallRun]` + backup in `CurUninstallStepChanged`.
5. **AppId mismatch between versions** -- Causes duplicate Add/Remove Programs entries, broken upgrade chain. Prevention: set permanent `AppId` with a GUID in the first `.iss` file, never change it.

## Implications for Roadmap

Based on research, the milestone naturally decomposes into 4 phases ordered by dependency chain and risk.

### Phase 1: Build Pipeline & Installer Scaffolding
**Rationale:** Everything depends on a working publish output and a minimal `.iss` file. The foundational decisions (AppId, PrivilegesRequired, DefaultDirName, MinVersion) must be locked first because they cannot be changed later without breaking upgrade chains.
**Delivers:** `dotnet publish` producing self-contained single-file output; minimal Inno Setup script that installs files to `%LocalAppData%\Focus` and registers in Add/Remove Programs; build script orchestrating both steps.
**Addresses:** Table stakes -- single setup.exe, install to chosen directory, clean file install, Start Menu shortcut.
**Avoids:** Pitfall 3 (elevation path mismatch -- set `PrivilegesRequired=lowest` from day one), Pitfall 5 (config destruction -- establish that installer never touches `%AppData%\focus\`), Pitfall 10 (AppId mismatch -- set permanent GUID), Pitfall 15 (MinVersion -- set `10.0` immediately).

### Phase 2: Upgrade Handling & Daemon Lifecycle
**Rationale:** Before adding Task Scheduler complexity, the upgrade path must work -- it is the most common real-world operation after initial install and touches locked files, running processes, and file versioning.
**Delivers:** `AppMutex` detection of running daemon; `CloseApplications` integration; `taskkill` fallback; `ignoreversion` on all files; post-install daemon launch with `runasoriginaluser` flag.
**Addresses:** Table stakes -- stop running daemon, upgrade in-place, launch after install.
**Avoids:** Pitfall 1 (locked exe during upgrade), Pitfall 8 (post-install launch in wrong context), Pitfall 9 (Restart Manager failure with tray-only app), Pitfall 12 (uninstaller not stopping daemon).

### Phase 3: Task Scheduler Integration
**Rationale:** This is the highest-complexity feature and the primary reason the installer exists. It depends on Phase 1 (install path must be known) and Phase 2 (daemon lifecycle must work for testing).
**Delivers:** `[Tasks]` checkboxes for startup registration with elevation sub-option; `schtasks.exe` invocation in `CurStepChanged(ssPostInstall)` for task creation; `schtasks /Delete` in `[UninstallRun]` and `CurUninstallStepChanged` for cleanup; startup delay to avoid tray icon registration failure.
**Addresses:** Core differentiator -- Task Scheduler with elevation choice; clean uninstall of scheduled task.
**Avoids:** Pitfall 2 (non-interactive session -- use ONLOGON trigger, never "run whether logged on or not"), Pitfall 4 (orphaned task -- paired create/delete), Pitfall 7 (admin required for per-user task -- targeted elevation via ShellExec), Pitfall 11 (missing startup delay -- add `/DELAY` parameter), Pitfall 14 (double elevation -- coordinate with `elevateOnStartup` config).

### Phase 4: Polish & Distribution
**Rationale:** Final touches that depend on all previous phases working end-to-end.
**Delivers:** Optional desktop shortcut; PATH registration; version bump to 5.0.0; SmartScreen documentation in release notes; `.gitignore` updates for publish/dist output directories.
**Addresses:** Differentiators -- desktop shortcut, PATH registration, professional release packaging.
**Avoids:** Pitfall 6 (SmartScreen -- document bypass procedure rather than blocking on code signing).

### Phase Ordering Rationale

- **Phase 1 before everything** because AppId, PrivilegesRequired, and install path are foundational decisions that cascade to all other phases. Changing them later breaks upgrade chains.
- **Phase 2 before Phase 3** because Task Scheduler testing requires installing, starting the daemon, then upgrading -- all of which need the daemon lifecycle management to work.
- **Phase 3 is the core delivery** and the most complex phase. Isolating it lets the developer focus on the elevation model, startup delay tuning, and create/delete pairing without worrying about file packaging.
- **Phase 4 last** because polish items are low-risk additive changes that do not affect the core install/upgrade/uninstall/startup flow.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (Task Scheduler):** The interaction between `/SC ONLOGON` admin requirements, per-user installer context, and targeted elevation via `ShellExec` needs careful implementation. The 72-hour default timeout on scheduled tasks may require XML task definition or PowerShell override. Recommend `/gsd:research-phase` before planning.

Phases with standard patterns (skip research-phase):
- **Phase 1 (Build Pipeline & Scaffolding):** Well-documented `dotnet publish` flags and cookie-cutter Inno Setup `[Setup]`/`[Files]`/`[Icons]` sections. Official docs are sufficient.
- **Phase 2 (Upgrade & Lifecycle):** `AppMutex`, `CloseApplications`, and `ignoreversion` are standard Inno Setup patterns with extensive documentation.
- **Phase 4 (Polish):** Trivial additions (shortcuts, PATH, version bump). No research needed.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All claims verified against Microsoft Learn and Inno Setup official docs. Zero ambiguity on publish flags or Inno Setup directives. |
| Features | HIGH | Feature landscape is narrow and well-understood. Inno Setup has 25+ years of community patterns for exactly this type of installer. |
| Architecture | HIGH | Integration surface is minimal (3 touch points: publish output, mutex name, schtasks CLI). No architectural risk. |
| Pitfalls | HIGH | 15 pitfalls identified from official docs and verified community sources. Critical pitfalls have concrete prevention steps. |

**Overall confidence:** HIGH

### Gaps to Address

- **72-hour task timeout**: `schtasks.exe` creates tasks with a default "Stop task if running longer than 3 days" setting. The daemon runs indefinitely. FEATURES.md flags this but no research file provides a verified `schtasks` CLI flag to disable it. May need XML task definition (`schtasks /Create /XML`) or post-creation PowerShell `Set-ScheduledTask` call. Validate during Phase 3 planning.
- **`/SC ONLOGON` admin requirement for `/RL LIMITED`**: STACK.md states that even `ONLOGON` with `LIMITED` requires admin. If true, the targeted-elevation approach in Phase 3 is required even for standard (non-elevated) startup tasks. Validate with a test on the target Windows version before finalizing the Phase 3 approach.
- **Restart Manager interaction with tray-only daemon**: Pitfall 9 identifies that `WM_CLOSE` may not reach the daemon because it has no top-level window. The `AppMutex` + `taskkill` fallback should work, but the Restart Manager timeout (up to 30 seconds) could make upgrades feel slow. Consider adding a hidden message-only window to the daemon if this becomes a UX problem, but defer unless testing reveals it.
- **PublishSingleFile disagreement resolution**: The recommendation to use single-file publish is based on install directory cleanliness. If virus scanner false positives become a real issue during testing, revisit this decision and switch to multi-file publish (ARCHITECTURE.md's recommendation). Inno Setup handles both transparently.

## Sources

### Primary (HIGH confidence)
- [Inno Setup Official Documentation](https://jrsoftware.org/ishelp/) -- Setup directives, Pascal scripting, Tasks/Run/Files sections, constants, event functions
- [Inno Setup 6.7.1 Release](https://jrsoftware.org/isdl.php) -- Current version, 64-bit loader support
- [Microsoft Learn -- schtasks create](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create) -- /SC ONLOGON, /RL, /TN, /TR, /F parameters
- [Microsoft Learn -- schtasks delete](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-delete) -- Uninstall cleanup
- [Microsoft Learn -- Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) -- PublishSingleFile, IncludeNativeLibrariesForSelfExtract, API compatibility
- [Microsoft Learn -- .NET application publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/) -- Self-contained vs framework-dependent
- [Microsoft Learn -- .NET 8 RuntimeIdentifier breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/runtimespecific-app-default) -- Must explicitly set SelfContained
- [Microsoft Learn -- IL trimming incompatibility](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained) -- WinForms cannot be trimmed
- Existing codebase analysis: `DaemonCommand.cs`, `FocusConfig.cs`, `DaemonMutex.cs`, `focus.csproj` -- Integration points verified by direct code reading

### Secondary (MEDIUM confidence)
- [WinForms single-file publish issue (dotnet/winforms#11473)](https://github.com/dotnet/winforms/issues/11473) -- Use CLI publish, not VS dialog
- [SmartScreen and code signing changes](https://www.advancedinstaller.com/prevent-smartscreen-from-appearing.html) -- EV certificates no longer provide instant bypass
- Community tutorials on Inno Setup admin context, daemon detection, Task Scheduler interactive desktop -- Patterns verified against official docs

---
*Research completed: 2026-03-05*
*Ready for roadmap: yes*
