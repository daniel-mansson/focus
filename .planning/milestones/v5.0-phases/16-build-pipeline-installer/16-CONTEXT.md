# Phase 16: Build Pipeline & Installer - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Produce a Focus-Setup.exe via Inno Setup that handles install, upgrade, and uninstall lifecycle. User can install Focus to a chosen directory, upgrade in-place preserving config, and uninstall via Add/Remove Programs. Task Scheduler registration and settings UI toggles are separate phases (17, 18).

</domain>

<decisions>
## Implementation Decisions

### Build automation
- PowerShell script (build.ps1) at repo root orchestrates dotnet publish + ISCC.exe compile
- Inno Setup .iss file and installer-specific assets live in an `installer/` directory at repo root
- ISCC.exe must be on PATH (no hardcoded default path, no param override)
- Build output (Focus-Setup.exe) goes to `installer/output/` (gitignored)

### Installer wizard flow
- Minimal wizard: welcome, install location, progress, finish — no license page, no component selection
- App identity in Add/Remove Programs: name "Focus", publisher is the user's name
- Start Menu shortcut created, launching `focus daemon`
- Finish page has "Launch Focus now" checkbox, checked by default

### Carried forward from research
- Inno Setup 6.7.1 with PrivilegesRequired=lowest and PrivilegesRequiredOverridesAllowed=dialog
- Self-contained single-file publish (PublishSingleFile=true + IncludeNativeLibrariesForSelfExtract=true)
- Default install path: %LocalAppData%\Focus
- Installer never touches %AppData%\focus\config.json — config owned by daemon runtime
- AppMutex detection to stop running daemon before upgrade

### Claude's Discretion
- Exact Inno Setup section structure and scripting details
- .gitignore entries for build output
- Version bumping mechanics in build script
- Installer icon and visual polish

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `focus/focus.csproj`: .NET 8 WinForms project, Version=4.0.0, AssemblyName=focus, has ApplicationIcon and EmbeddedResource for focus.ico
- `focus/app.manifest`: DPI-aware manifest with Windows 10/11 compatibility — installer should not need a separate manifest
- `focus/focus.ico`: Existing app icon usable for installer branding and Add/Remove Programs
- `SETUP.md`: Documents self-contained publish command (`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`)

### Established Patterns
- Single .csproj project in `focus/` subdirectory (not repo root)
- Config stored at `%AppData%\focus\config.json` — completely separate from install directory
- Daemon uses named mutex for single-instance (AppMutex detection for installer)

### Integration Points
- Build script reads version from focus.csproj and passes to Inno Setup
- Installer output is the self-contained focus.exe from dotnet publish
- Start Menu shortcut targets `focus.exe daemon` in the install directory

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 16-build-pipeline-installer*
*Context gathered: 2026-03-05*
