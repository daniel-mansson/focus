---
phase: 16-build-pipeline-installer
plan: 01
subsystem: infra
tags: [inno-setup, powershell, dotnet-publish, installer, build-pipeline]

# Dependency graph
requires:
  - phase: 15-settings-form
    provides: Complete v4.0 application ready for packaging
provides:
  - Inno Setup installer script (installer/focus.iss) for install/upgrade/uninstall lifecycle
  - Build orchestrator (build.ps1) producing Focus-Setup.exe from single command
  - Root .gitignore for installer build output
affects: [17-task-scheduler-integration, 18-settings-ui-startup-controls]

# Tech tracking
tech-stack:
  added: [inno-setup-6.7.1, dotnet-publish-self-contained]
  patterns: [single-file-publish, appmutex-daemon-stop, per-user-install]

key-files:
  created:
    - installer/focus.iss
    - build.ps1
    - .gitignore

key-decisions:
  - "ISCC.exe must be on PATH -- no hardcoded path or parameter override"
  - "PrivilegesRequired=lowest with PrivilegesRequiredOverridesAllowed=dialog for per-user default"
  - "DefaultDirName={localappdata}\\Focus -- per-user install directory"
  - "AppMutex=Global\\focus-daemon matches DaemonMutex.cs exactly"
  - "Parameters: daemon on both shortcut and post-install launch"
  - "Installer never touches %AppData%\\focus\\config.json"

patterns-established:
  - "Build pipeline: version from csproj XML -> dotnet publish -> ISCC compile"
  - "Per-user install with localappdata default and optional admin elevation"
  - "AppMutex for daemon stop before file replacement during upgrade"

requirements-completed: [PKG-01, INST-01, INST-02, INST-03, INST-04, INST-05]

# Metrics
duration: 8min
completed: 2026-03-05
---

# Phase 16 Plan 01: Build Pipeline & Installer Summary

**Inno Setup installer with build.ps1 orchestrator producing Focus-Setup.exe from dotnet self-contained publish, supporting install/upgrade/uninstall lifecycle with AppMutex daemon stop**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-05T21:54:00Z
- **Completed:** 2026-03-05T22:02:10Z
- **Tasks:** 3 (2 auto + 1 checkpoint)
- **Files created:** 3

## Accomplishments
- Complete Inno Setup script handling install, upgrade, and uninstall with per-user defaults
- Build orchestrator that reads version from csproj, runs dotnet publish, and invokes ISCC to produce setup.exe
- AppMutex integration ensuring running daemon is stopped before file replacement during upgrades
- Post-install "Launch Focus now" checkbox starting daemon immediately

## Task Commits

Each task was committed atomically:

1. **Task 1: Create Inno Setup script and gitignore** - `e3b22fd` (feat)
2. **Task 2: Create build.ps1 orchestrator** - `4cad905` (feat)
3. **Task 3: Verify full install lifecycle** - checkpoint:human-verify (auto-approved)

## Files Created/Modified
- `installer/focus.iss` - Inno Setup script defining complete install/upgrade/uninstall lifecycle with AppMutex, per-user install, and post-install daemon launch
- `build.ps1` - PowerShell build orchestrator: reads version from focus.csproj, runs dotnet publish (self-contained single-file), invokes ISCC.exe, verifies output
- `.gitignore` - Root gitignore for installer/output/ build artifacts

## Decisions Made
- ISCC.exe must be on PATH (no hardcoded path) -- keeps build.ps1 portable across dev machines
- Per-user install to {localappdata}\Focus with PrivilegesRequired=lowest -- no UAC prompt by default
- AppMutex=Global\focus-daemon matches DaemonMutex.cs exactly -- Inno Setup detects and prompts to close running daemon
- Parameters: "daemon" on both Start Menu shortcut and post-install launch -- without it, focus.exe shows CLI help and exits
- IncludeNativeLibrariesForSelfExtract=true for true single-file output -- prevents native DLL extraction alongside exe

## Deviations from Plan

None - plan executed exactly as written.

## User Setup Required

None - no external service configuration required. Inno Setup 6.7.1 must be installed with ISCC.exe on PATH as a build-time prerequisite.

## Next Phase Readiness
- Installer foundation complete, ready for Phase 17 (Task Scheduler Integration)
- build.ps1 can be extended to include post-install scheduled task registration
- installer/focus.iss can add [Tasks] section for "Start at logon" checkbox in Phase 17
- Blocker to validate: /SC ONLOGON with /RL LIMITED may require admin (noted in STATE.md)

## Self-Check: PASSED

All files verified present, all commit hashes found in git log.

---
*Phase: 16-build-pipeline-installer*
*Completed: 2026-03-05*
