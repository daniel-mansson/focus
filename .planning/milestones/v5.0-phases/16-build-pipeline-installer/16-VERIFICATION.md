---
phase: 16-build-pipeline-installer
verified: 2026-03-05T22:30:00Z
status: human_needed
score: 6/6 must-haves verified
re_verification: false
human_verification:
  - test: "Run powershell -File build.ps1 from repo root"
    expected: "Prints version, publishes focus.exe, compiles installer, reports Focus-Setup.exe size in installer/output/"
    why_human: "Requires dotnet SDK and ISCC.exe on PATH; cannot run build tools programmatically in verification"
  - test: "Run installer/output/Focus-Setup.exe and complete the wizard"
    expected: "Minimal wizard (welcome, install location defaulting to %LocalAppData%\\Focus, progress, finish). No license page. Focus installed and registered in Add/Remove Programs."
    why_human: "GUI installer requires interactive Windows session"
  - test: "Complete installation with Launch Focus now checked"
    expected: "Focus daemon starts (tray icon appears), focus.exe exists in install directory, Start Menu has Focus shortcut"
    why_human: "Visual confirmation of daemon lifecycle and tray icon"
  - test: "Run installer again while daemon is running (upgrade test)"
    expected: "Installer detects running daemon via AppMutex, prompts to close it, upgrades files in-place, %AppData%\\focus\\config.json is unchanged"
    why_human: "Requires running daemon process; interactive file replacement and config preservation check"
  - test: "Uninstall via Add/Remove Programs"
    expected: "focus.exe removed from install directory, Start Menu shortcut removed, %AppData%\\focus\\config.json is NOT deleted"
    why_human: "Interactive uninstall via Windows system UI"
---

# Phase 16: Build Pipeline & Installer Verification Report

**Phase Goal:** User can install, upgrade, and uninstall Focus via a single setup.exe
**Verified:** 2026-03-05T22:30:00Z
**Status:** human_needed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Running build.ps1 produces installer/output/Focus-Setup.exe from a single command | VERIFIED (code) | build.ps1 reads version from csproj (line 4-5), runs dotnet publish (line 15-20), invokes ISCC.exe (line 43), verifies output exists (line 52-53). All steps have error checking via $LASTEXITCODE and throw. |
| 2 | Running Focus-Setup.exe installs focus.exe to the user-chosen directory and registers in Add/Remove Programs | VERIFIED (code) | ISS has DefaultDirName={localappdata}\Focus (line 15), DisableDirPage=auto (line 16), AppId=Focus (line 11), UninstallDisplayName={#MyAppName} (line 24). Files section copies focus.exe to {app} (line 33). |
| 3 | Running Focus-Setup.exe while daemon is running stops the daemon via AppMutex before replacing files | VERIFIED (code) | ISS has AppMutex=Global\focus-daemon (line 28) matching DaemonMutex.cs MutexName exactly. CloseApplications=yes (line 29) enables Restart Manager prompting. |
| 4 | Upgrading (re-running installer) replaces focus.exe without touching config.json | VERIFIED (code) | AppId=Focus provides stable upgrade identity. Files flag ignoreversion (line 33) forces replacement. No [UninstallDelete] or config.json references in ISS. Config at %AppData%\focus\ is outside install dir %LocalAppData%\Focus. |
| 5 | Uninstalling via Add/Remove Programs removes focus.exe, shortcuts, and uninstall registration | VERIFIED (code) | Inno Setup handles this automatically for [Files] and [Icons] entries. No [UninstallDelete] for %AppData% means config.json is preserved. UninstallDisplayIcon set (line 23). |
| 6 | After install, a checked Launch Focus now checkbox starts the daemon | VERIFIED (code) | [Run] section (line 39): Filename={app}\focus.exe, Parameters="daemon", Flags: postinstall nowait skipifsilent. nowait prevents setup from blocking on long-running daemon. |

**Score:** 6/6 truths verified at code level

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `installer/focus.iss` | Inno Setup script defining install/upgrade/uninstall lifecycle | VERIFIED | 40 lines, all 4 sections ([Setup], [Files], [Icons], [Run]), all required directives present. Contains AppMutex. |
| `build.ps1` | Build orchestrator: dotnet publish + ISCC compile | VERIFIED | 57 lines, 6-step pipeline: strict mode, version read, dotnet publish, single-file verify, ISCC compile, output report. Error handling on every external command. |
| `.gitignore` | Ignore rule for installer build output | VERIFIED | 2 lines, contains `installer/output/` |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| build.ps1 | focus/focus.csproj | XML parse for Version element | WIRED | Line 4: `[xml]$csproj = Get-Content "focus/focus.csproj"`, Line 5: `Select-Xml -Xml $csproj -XPath "//Version"`. Version element exists in csproj at line 13: `<Version>4.0.0</Version>` |
| build.ps1 | installer/focus.iss | ISCC.exe /DMyAppVersion invocation | WIRED | Line 43: `ISCC.exe /DMyAppVersion="$version" installer/focus.iss`. ISS preprocessor accepts via `#ifndef MyAppVersion` fallback (line 4-6). |
| installer/focus.iss | focus/bin/Release/net8.0/win-x64/publish/focus.exe | Files Source directive | WIRED | Line 33: `Source: "..\focus\bin\Release\net8.0\win-x64\publish\focus.exe"`. build.ps1 produces this file at line 15-20 via dotnet publish. |
| installer/focus.iss AppMutex | DaemonMutex.cs MutexName | Exact string match | WIRED | ISS line 28: `AppMutex=Global\focus-daemon`. DaemonMutex.cs line 7: `@"Global\focus-daemon"`. Exact match confirmed. |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| PKG-01 | 16-01-PLAN | Installer produces a single setup.exe via Inno Setup with self-contained .NET publish | SATISFIED | build.ps1 orchestrates dotnet publish + ISCC to produce Focus-Setup.exe. Self-contained flags: PublishSingleFile=true, IncludeNativeLibrariesForSelfExtract=true. |
| INST-01 | 16-01-PLAN | User can install Focus to a chosen directory (default: %LocalAppData%\Focus) | SATISFIED | DefaultDirName={localappdata}\Focus, DisableDirPage=auto (shows on first install), PrivilegesRequired=lowest (per-user). Registered in Add/Remove Programs via AppId=Focus. |
| INST-02 | 16-01-PLAN | Installer stops running daemon before upgrading files (AppMutex detection) | SATISFIED | AppMutex=Global\focus-daemon matches DaemonMutex.cs exactly. CloseApplications=yes triggers Restart Manager. |
| INST-03 | 16-01-PLAN | Installer upgrades in-place without breaking user config | SATISFIED | Config at %AppData%\focus\ is outside install dir %LocalAppData%\Focus. No [UninstallDelete] for config. ignoreversion flag on Files. Stable AppId for upgrade identity. |
| INST-04 | 16-01-PLAN | User can uninstall via Add/Remove Programs (removes files + shortcuts) | SATISFIED | Inno Setup automatically removes [Files] and [Icons] entries on uninstall. UninstallDisplayIcon and UninstallDisplayName configured. Config preserved (not in install dir, no UninstallDelete). |
| INST-05 | 16-01-PLAN | Installer offers "Launch Focus now" checkbox after install | SATISFIED | [Run] section with postinstall flag, Parameters: "daemon", nowait to prevent blocking on long-running process. Checkbox checked by default (no unchecked flag). |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | - | - | - | No TODO, FIXME, placeholder, or stub patterns found in any created file |

### Human Verification Required

All 6 truths are verified at the code level -- every directive, connection, and error handling path is confirmed present and correct. However, the full install/upgrade/uninstall lifecycle is inherently a runtime behavior that requires interactive testing on a Windows machine with the build toolchain.

### 1. Build Pipeline Execution

**Test:** Run `powershell -File build.ps1` from repo root
**Expected:** Prints "Building Focus v4.0.0", publishes focus.exe, compiles installer, reports Focus-Setup.exe file size. File exists at `installer/output/Focus-Setup.exe`.
**Why human:** Requires .NET 8 SDK and Inno Setup 6.7.1 (ISCC.exe on PATH) -- build tools cannot be invoked in automated verification.

### 2. Fresh Install

**Test:** Run `installer/output/Focus-Setup.exe` on a machine without Focus installed
**Expected:** Minimal wizard (welcome -> install location -> progress -> finish). Default path shows %LocalAppData%\Focus. No license page. After completion, focus.exe exists in install directory. Start Menu has "Focus" shortcut. "Focus" appears in Add/Remove Programs with version 4.0.0 and publisher "Daniel".
**Why human:** GUI installer requires interactive Windows session.

### 3. Post-Install Launch

**Test:** Complete installation with "Launch Focus now" checkbox checked
**Expected:** Focus daemon starts (tray icon appears). Setup wizard closes immediately (does not wait for daemon).
**Why human:** Visual confirmation of daemon lifecycle and tray icon appearance.

### 4. Upgrade with Running Daemon

**Test:** With daemon running, execute Focus-Setup.exe again
**Expected:** Installer detects running daemon via AppMutex, prompts user to close it. After upgrade, %AppData%\focus\config.json is unchanged (if it existed before).
**Why human:** Requires running daemon process and interactive AppMutex/Restart Manager behavior.

### 5. Uninstall

**Test:** Uninstall Focus via Add/Remove Programs
**Expected:** focus.exe removed from install directory. Start Menu shortcut removed. Uninstall entry removed from Add/Remove Programs. %AppData%\focus\config.json is NOT deleted.
**Why human:** Interactive uninstall via Windows system UI; config preservation requires filesystem inspection.

### Gaps Summary

No code-level gaps found. All artifacts are present, substantive (not stubs), and correctly wired to each other and to the existing codebase. All 6 requirement IDs (PKG-01, INST-01 through INST-05) are satisfied at the implementation level.

The only remaining verification is runtime testing: building the installer with the actual toolchain and running through the full install/upgrade/uninstall lifecycle on a Windows machine. This is flagged as human_needed because it requires interactive GUI testing with build prerequisites (dotnet SDK, Inno Setup).

---

_Verified: 2026-03-05T22:30:00Z_
_Verifier: Claude (gsd-verifier)_
