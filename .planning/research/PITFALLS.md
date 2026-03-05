# Domain Pitfalls

**Domain:** Adding Inno Setup installer with Task Scheduler startup to a .NET 8 daemon (keyboard hooks, system tray, elevation support)
**Researched:** 2026-03-05
**Confidence:** HIGH (Inno Setup official docs verified, Microsoft schtasks docs verified, codebase inspected)

---

## About This Document

This document covers pitfalls specific to **v5.0: adding an Inno Setup installer with Task Scheduler startup registration** to the existing Focus daemon. It focuses on integration pitfalls -- where the installer interacts with the daemon's keyboard hooks, system tray, elevation logic, and user config.

---

## Critical Pitfalls

Mistakes that cause broken installs, data loss, or unusable daemon after update.

---

### Pitfall 1: Installer Replaces Files While Daemon Is Running (Locked EXE / Corrupt State)

**What goes wrong:** The daemon process (`focus.exe`) holds the executable file locked. If the installer writes new files over it, the install fails or leaves partial files. Because this daemon uses `WH_KEYBOARD_LL` -- a system-wide keyboard hook -- a corrupted running process can freeze keyboard input system-wide until Windows kills the hook timeout.

**Why it happens:** Developers test fresh installs but skip testing upgrades where the daemon is already running in the background (tray icon, no visible window). Easy to forget the process is alive.

**Consequences:** Failed file copy (installer error dialog), partially updated install (some files new, some old), or worst case: daemon crash mid-hook-callback causing brief keyboard input freeze.

**Prevention:**
1. Set `CloseApplications=yes` in the Inno Setup `[Setup]` section to use the Windows Restart Manager API. This auto-detects processes using files being updated and prompts the user to close them.
2. Set `AppMutex=Global\focus-daemon` matching the existing mutex name in `DaemonMutex.cs`. Inno Setup will detect the running daemon and prompt before proceeding.
3. As a fallback in `PrepareToInstall()` Pascal Script event, run `taskkill /f /im focus.exe` to force-kill any stale process.
4. Add a brief `Sleep(500)` after the kill to let the OS release file handles before file copy begins.

**Detection:** Test the upgrade path: install v4.0, start daemon, then run v5.0 installer. If the installer does not prompt about the running process, this pitfall is active.

**Phase:** Must be addressed in the very first phase (installer scaffolding), before any file packaging.

---

### Pitfall 2: Task Scheduler Elevated Task Creates Non-Interactive Session (Invisible Tray Icon)

**What goes wrong:** When a Task Scheduler task is configured with "Run with highest privileges" AND "Run whether user is logged on or not," Windows launches the process in Session 0 (a non-interactive service session). The daemon starts, the keyboard hook installs, but the system tray icon is invisible, the settings form cannot render, and overlay windows never appear. The daemon appears to "not start" even though the process is running.

**Why it happens:** There are two independent settings in Task Scheduler. "Run with highest privileges" is needed for UIPI bypass (interacting with admin windows). "Run only when user is logged on" is needed for interactive desktop access. Developers commonly set "Run whether user is logged on or not" thinking it makes the task more reliable, which instead pushes it into a non-interactive session.

**Consequences:** Daemon runs invisibly -- no tray icon, no overlay, no settings. Users think the install is broken. The only clue is `focus.exe` appearing in Task Manager.

**Prevention:**
1. When creating the scheduled task, always specify the logged-on user combined with "highest privileges" and a logon trigger. Never use `/ru SYSTEM`.
2. The task XML must specify `<LogonType>InteractiveToken</LogonType>` -- this guarantees the process runs in the user's interactive desktop session where the tray icon and overlays live.
3. Alternatively, use PowerShell's `Register-ScheduledTask` with `New-ScheduledTaskPrincipal -LogonType Interactive -RunLevel Highest` for more reliable task creation.
4. Add a startup delay of 15-30 seconds (`<Delay>PT15S</Delay>` in the trigger) so Explorer's shell and notification area are fully initialized before the tray icon registers.

**Detection:** Create the task, log off and log on. Check: (a) tray icon visible, (b) keyboard hook functional, (c) overlay appears on CAPS hold.

**Phase:** Must be addressed in the Task Scheduler registration phase. Get the XML/schtasks invocation right from the start.

---

### Pitfall 3: Elevation Mismatch Between Installer Context and User Context (Wrong Paths)

**What goes wrong:** When the Inno Setup installer runs elevated (admin privileges for Task Scheduler creation), directory constants like `{localappdata}` and `{userappdata}` resolve to the **Administrator's** profile paths, not the actual user's. Files install to `C:\Users\Administrator\AppData\Local\Focus` instead of the real user's `AppData\Local\Focus`. The scheduled task, registry entries, and shortcuts all point to the wrong profile.

**Why it happens:** The project targets `%LocalAppData%\Focus` as the install path. If the installer requires admin privileges (needed to create an elevated scheduled task), the admin context poisons all user-relative path resolution.

**Consequences:** The application installs in the wrong user profile. The daemon cannot find itself at the expected path. Config file at `%AppData%\focus\config.json` becomes unreachable because runtime user and install user are different.

**Prevention:**
1. Use `PrivilegesRequired=lowest` so the installer runs as the actual user by default. This makes `{localappdata}` resolve correctly to the real user's path.
2. Use `PrivilegesRequiredOverridesAllowed=dialog` to let the user optionally elevate if they want system-wide install.
3. For Task Scheduler creation that needs admin, use a targeted `ShellExec('runas', ...)` call within the installer to elevate just that one operation, rather than elevating the entire installer.
4. Never use the `{userappdata}` constant in an admin-mode installer for file placement. The config lives in `%AppData%\focus\` -- the daemon resolves this at runtime via `Environment.GetFolderPath(ApplicationData)`, so the installer should not touch it at all.

**Detection:** Run the installer from a non-admin user account where UAC prompts for admin. After install, verify `focus.exe` exists under the actual user's LocalAppData, not the admin's.

**Phase:** Must be addressed in the installer scaffolding phase. The `PrivilegesRequired` decision cascades to every other part.

---

### Pitfall 4: Uninstall Leaves Orphaned Scheduled Task (Daemon Auto-Starts After Uninstall)

**What goes wrong:** The uninstaller removes files but forgets to delete the scheduled task. Next logon, Task Scheduler tries to launch `focus.exe` from the deleted install path, generates error events, and may pop error dialogs. If a different application later installs to the same path, the orphaned task could launch the wrong binary.

**Why it happens:** File cleanup is automatic in Inno Setup (it tracks installed files). But scheduled tasks created via `schtasks.exe` or PowerShell are external to Inno's tracking -- the uninstaller has no automatic knowledge of them.

**Consequences:** Persistent "file not found" errors on every logon. Confusing Task Scheduler entries for uninstalled software. Potential security risk if path is reused.

**Prevention:**
1. In the `[UninstallRun]` section, add: `Filename: schtasks.exe; Parameters: "/Delete /F /TN ""Focus"""; Flags: runhidden`
2. As a belt-and-suspenders approach, also add cleanup in `CurUninstallStepChanged(usPostUninstall)` Pascal Script event.
3. Use the exact same task name constant in both install and uninstall scripts. Define it once: `#define TaskName "Focus"`.
4. Test the full cycle: install, verify task exists in Task Scheduler, uninstall, verify task is gone.

**Detection:** After uninstall, open Task Scheduler (taskschd.msc) and search for "Focus." If it is still there, this pitfall is active.

**Phase:** Must be paired with Task Scheduler creation in the same phase -- never ship task creation without task deletion.

---

### Pitfall 5: Config File Destroyed or Orphaned During Upgrade

**What goes wrong:** The user's `config.json` (in `%AppData%\focus\`) contains customized settings (strategy, grid fractions, overlay colors, elevateOnStartup). If the installer treats this as an application file and overwrites it, all customization is lost. Alternatively, if the uninstaller aggressively deletes everything, it removes config that the user might want to keep for a reinstall.

**Why it happens:** Confusion between "install directory" (`%LocalAppData%\Focus` for the binary) and "data directory" (`%AppData%\focus` for config). Developers include config templates in the installer that overwrite user config, or add `%AppData%\focus` to the uninstall cleanup.

**Consequences:** User loses all customized settings on every update.

**Prevention:**
1. The installer must NEVER write to `%AppData%\focus\config.json`. The daemon's `FocusConfig.Load()` already creates defaults when no config exists -- let it handle first-run config creation.
2. The install directory (`%LocalAppData%\Focus`) should contain ONLY the binary and supporting runtime files.
3. The uninstaller should offer an optional checkbox for whether to remove user config. Default should be to keep config (allows reinstall without reconfiguration).
4. New config fields in future versions are handled gracefully by `FocusConfig.Load()` -- JSON deserialization ignores missing fields and defaults fill in. No migration code needed.

**Detection:** Install with custom config, upgrade, verify all config values are preserved.

**Phase:** Address in the installer scaffolding phase -- define which directories the installer owns vs. which are user data.

---

## Moderate Pitfalls

---

### Pitfall 6: SmartScreen Blocks Unsigned Installer

**What goes wrong:** Windows SmartScreen shows "Windows protected your PC - Publisher: Unknown publisher" when running an unsigned installer. Many users will not click "More info -> Run anyway." Antivirus software may quarantine the `.exe` outright.

**Why it happens:** The installer `.exe` is not code-signed. SmartScreen reputation is zero for new, unsigned binaries.

**Prevention:**
1. Accept this for initial development -- do not let code signing block the v5.0 milestone.
2. Document that users need to click "More info -> Run anyway" in install instructions.
3. For future releases, investigate Microsoft Trusted Signing or a standard code signing certificate.
4. Note: even with a certificate, reputation takes 2-8 weeks to build. EV certificates no longer provide instant SmartScreen bypass (Microsoft changed this March 2024).

**Phase:** Acknowledge in documentation. Do not block development on signing.

---

### Pitfall 7: Task Scheduler Task Creation Requires Admin but Installer Runs Per-User

**What goes wrong:** With `PrivilegesRequired=lowest`, the installer runs without elevation. But creating a scheduled task with `/rl HIGHEST` (run with highest privileges) requires admin rights. The `schtasks.exe /Create` call fails with "Access is denied."

**Why it happens:** Tension between two goals: (a) per-user install without UAC for simple file copy, and (b) elevated scheduled task for UIPI bypass when navigating admin windows.

**Consequences:** Task creation silently fails. Daemon does not auto-start on logon. Or the installer is forced to require full admin, reintroducing Pitfall 3.

**Prevention:**
1. Offer two Task Scheduler modes: "Standard" (no elevation, `/rl LIMITED`) and "Elevated" (with elevation, `/rl HIGHEST`). Map to the existing `elevateOnStartup` config.
2. For "Standard" mode: `PrivilegesRequired=lowest` works fine. Task creation with `/rl LIMITED` succeeds without admin.
3. For "Elevated" mode: use a targeted elevation -- `ShellExec('runas', 'schtasks.exe', ...)` -- to elevate just the task creation call.
4. Simplest approach: always create a standard-privilege logon task. The daemon's existing `ElevateOnStartup` config handles self-elevation at runtime via `runas` verb. This eliminates the admin requirement from the installer entirely.

**Detection:** Install with `PrivilegesRequired=lowest`, check if the scheduled task was created. Run `schtasks /query /tn "Focus"`.

**Phase:** Address in the Task Scheduler phase. Design the approach upfront.

---

### Pitfall 8: Post-Install Daemon Launch Runs as Admin (Wrong User Context)

**What goes wrong:** The `[Run]` section's "Launch Focus daemon" checkbox runs the daemon as the installer's user context. If the installer was elevated, the daemon starts elevated -- which is fine for the hook, but the tray icon may appear in an unexpected session. Config path resolution and environment variables may differ from a normal user logon launch.

**Why it happens:** `[Run]` section items inherit the installer's privilege level by default.

**Prevention:**
1. Use `Flags: runasoriginaluser nowait` on the `[Run]` entry for post-install daemon launch. The `runasoriginaluser` flag runs the program as the user who started the installer, not the elevated admin.
2. Alternatively, use `Flags: shellexec nowait` to launch through the shell in user context.
3. Test both scenarios: installer running without elevation, and installer running with elevation.

**Detection:** Install with UAC elevation, let the "Launch" checkbox run the daemon. Check Task Manager: `focus.exe` should NOT be running as admin unless the user explicitly chose elevated mode.

**Phase:** Address in post-install launch configuration.

---

### Pitfall 9: Restart Manager Fails to Stop Daemon Gracefully (Ghost Tray Icon)

**What goes wrong:** Inno Setup's Restart Manager asks the daemon to close via `WM_CLOSE`. But the daemon has no main form -- it runs via `DaemonApplicationContext` with a `NotifyIcon`. The Restart Manager expects a top-level window to send `WM_CLOSE` to, but `NotifyIcon` is not a window. The daemon may hang or leave a ghost tray icon. Restart Manager waits up to 30 seconds, then force-kills, leaving the tray icon orphaned until Explorer refreshes.

**Why it happens:** The daemon uses a custom `ApplicationContext` without a visible window. Restart Manager's window-based shutdown protocol does not work for notification-area-only applications.

**Consequences:** Ghost tray icon persisting until user hovers over it. 30-second delay on the installer's "Preparing to Install" page while Restart Manager waits.

**Prevention:**
1. Rely on `AppMutex=Global\focus-daemon` as the primary detection mechanism. This prompts the user to close the daemon manually, which triggers clean shutdown through the tray's "Exit" menu.
2. As a fallback, use `taskkill /f /im focus.exe` in `PrepareToInstall()` Pascal Script.
3. Optionally, add a hidden message-only window to the daemon that handles `WM_CLOSE` and triggers `CancellationTokenSource.Cancel()` for cleaner Restart Manager integration.
4. For `RestartApplications` to relaunch the daemon after install, call `RegisterApplicationRestart` Win32 API during daemon startup.

**Detection:** Start daemon, run installer. If the "Preparing to Install" page says "The following applications are using files..." and takes >10 seconds to resolve, graceful shutdown is not working.

**Phase:** Address after basic installer works -- this is polish for the upgrade flow.

---

### Pitfall 10: AppId Mismatch Between Versions (Duplicate Add/Remove Programs Entries)

**What goes wrong:** If the `AppId` value in the `[Setup]` section changes between versions, Windows treats each installer version as a separate application. The user gets multiple "Focus" entries in "Apps & Features." Uninstalling one does not affect the other.

**Why it happens:** Developers change the `AppId` between versions, or forget to set one (Inno defaults it from `AppName`).

**Consequences:** Multiple uninstall entries. User confusion. Files from different versions coexist or conflict.

**Prevention:**
1. Set a permanent `AppId` in the first `.iss` file and never change it: `AppId={{A-GUID-HERE}` (double braces to escape the `{`).
2. Document the AppId in a comment: `; DO NOT CHANGE -- this links all versions for upgrade detection`.
3. Set `AppVersion` using a preprocessor define tied to the `.csproj` version.

**Detection:** Install v5.0.0, then install v5.0.1. Check Add/Remove Programs -- should show only one entry with the newer version.

**Phase:** Must be set in the very first phase (installer scaffolding). Cannot be changed later without breaking upgrade chains.

---

## Minor Pitfalls

---

### Pitfall 11: Missing Startup Delay Causes Tray Icon Registration Failure

**What goes wrong:** Task Scheduler fires the logon trigger before Explorer has fully initialized the notification area. The daemon calls `NotifyIcon` but the shell is not ready. The tray icon silently fails to appear.

**Prevention:**
1. Add `<Delay>PT15S</Delay>` to the Task Scheduler logon trigger.
2. Consider adding retry logic in the daemon's tray icon setup: if `Shell_NotifyIcon` returns false, retry after 5 seconds (up to 3 attempts).

**Phase:** Task Scheduler configuration phase.

---

### Pitfall 12: Uninstaller Does Not Stop Daemon Before File Deletion

**What goes wrong:** The uninstaller tries to delete `focus.exe` while the daemon is running. Deletion fails, leaving files behind. The uninstall "completes" but the install directory still exists with locked files.

**Prevention:**
1. Add to `[UninstallRun]` (runs BEFORE file deletion): `Filename: schtasks.exe; Parameters: "/Delete /F /TN ""Focus"""; Flags: runhidden` followed by `Filename: taskkill.exe; Parameters: "/F /IM focus.exe"; Flags: runhidden`
2. Order matters: delete scheduled task first (prevents restart), then kill process, then Inno deletes files.

**Phase:** Uninstall cleanup phase.

---

### Pitfall 13: Single-File Publish Produces Extra Files

**What goes wrong:** `dotnet publish` with `PublishSingleFile=true` for WinForms on .NET 8 may still produce additional files (`.deps.json`, native DLLs, `createdump.exe`). The Inno Setup script references only `focus.exe` and misses companion files, producing a broken install.

**Prevention:**
1. Use `PublishSingleFile=true` + `SelfContained=true` + `IncludeNativeLibrariesForSelfExtract=true` in the publish profile.
2. In the `.iss` file, use `Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs` to capture the entire publish output directory.
3. After `dotnet publish`, list the output directory and verify contents. Adjust the `.iss` accordingly.

**Detection:** Publish, then check the output folder. If there are files beyond `focus.exe`, the installer must include them all.

**Phase:** Build/publish configuration phase.

---

### Pitfall 14: Daemon Self-Elevation Conflicts with Task Scheduler Elevation

**What goes wrong:** The daemon already has `ElevateOnStartup` logic that calls `Process.Start` with `Verb = "runas"`. If the Task Scheduler also launches the daemon with `/rl HIGHEST`, the self-elevation code detects it is already elevated and skips the re-launch -- which is correct. But if the Task Scheduler launches at standard privilege and `ElevateOnStartup` is true, the daemon shows a UAC prompt on every logon -- terrible UX for an auto-start app.

**Prevention:**
1. When creating an elevated scheduled task, ensure `elevateOnStartup` is set to `false` in the config (the task handles elevation, making the config setting redundant).
2. Best approach: always create a standard-privilege logon task. If the user wants elevation, the daemon's existing `ElevateOnStartup` config handles it at runtime. But note this produces a UAC prompt on each logon.
3. For truly silent elevated startup: create the task with `/rl HIGHEST` and disable `ElevateOnStartup`. This requires the installer to run elevated for the `schtasks` call.

**Phase:** Task Scheduler + config integration phase. Design the interaction upfront.

---

### Pitfall 15: Inno Setup Version Check Omission (Ancient Windows)

**What goes wrong:** Inno Setup's `MinVersion` defaults to `6.1sp1` (Windows 7 SP1). The daemon requires Windows 10+ (DPI awareness, DwmGetWindowAttribute behavior). Installing on Windows 7/8/8.1 would produce a non-functional app.

**Prevention:**
1. Set `MinVersion=10.0` in the `[Setup]` section to match the daemon's actual requirement.

**Phase:** Installer scaffolding.

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Installer scaffolding (.iss creation) | Pitfall 3 (elevation context), Pitfall 10 (AppId), Pitfall 15 (MinVersion) | Set `PrivilegesRequired=lowest`, permanent `AppId`, and `MinVersion=10.0` immediately |
| Build/publish integration | Pitfall 13 (not truly single file) | Verify publish output, include all files |
| File packaging + install path | Pitfall 5 (config destruction) | Never write to `%AppData%\focus\`, only to `{app}` |
| Daemon stop/start during upgrade | Pitfall 1 (locked files), Pitfall 9 (ghost tray) | `AppMutex` + `CloseApplications` + taskkill fallback |
| Task Scheduler registration | Pitfall 2 (non-interactive session), Pitfall 7 (admin vs per-user) | `InteractiveToken` logon type + logon trigger + startup delay |
| Task Scheduler + elevation config | Pitfall 14 (double elevation) | Coordinate task privilege level with `elevateOnStartup` config |
| Post-install launch | Pitfall 8 (wrong user context) | `runasoriginaluser` flag |
| Uninstall cleanup | Pitfall 4 (orphaned task), Pitfall 12 (locked files) | Delete task, kill process, then delete files -- in that order |
| Distribution / release | Pitfall 6 (SmartScreen) | Document workaround, defer code signing |

---

## Sources

- [Inno Setup: CloseApplications](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm) -- official documentation (HIGH confidence)
- [Inno Setup: RestartApplications](https://jrsoftware.org/ishelp/topic_setup_restartapplications.htm) -- official documentation (HIGH confidence)
- [Inno Setup: AppMutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm) -- official documentation (HIGH confidence)
- [Inno Setup: PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm) -- official documentation (HIGH confidence)
- [Inno Setup: Non Administrative Install Mode](https://jrsoftware.org/ishelp/topic_admininstallmode.htm) -- official documentation (HIGH confidence)
- [Inno Setup: Run and UninstallRun sections](https://jrsoftware.org/ishelp/topic_runsection.htm) -- official documentation (HIGH confidence)
- [schtasks create reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create) -- Microsoft official (HIGH confidence)
- [.NET single-file publish overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) -- Microsoft official (HIGH confidence)
- [Task Scheduler elevated logon trigger](https://www.tenforums.com/general-support/79984-how-set-up-elevated-process-run-user-logon.html) -- community verified (MEDIUM confidence)
- [SmartScreen and code signing changes](https://www.advancedinstaller.com/prevent-smartscreen-from-appearing.html) -- vendor documentation (MEDIUM confidence)
- [Inno Setup admin context path mismatch](https://www.w3tutorials.net/blog/installing-application-for-currently-logged-in-user-from-inno-setup-installer-running-as-administrator/) -- community tutorial (MEDIUM confidence)
- [Close running daemon before install](https://www.domador.net/extras/inno-setup-close-a-program-before-reinstalling-it/) -- community tutorial (MEDIUM confidence)
- [Task Scheduler interactive desktop for tray apps](https://windowsforum.com/threads/show-app-ui-at-logon-with-windows-11-task-scheduler.392418/) -- community forum (MEDIUM confidence)
