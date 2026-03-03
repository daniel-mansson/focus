# Project Research Summary

**Project:** Focus — Windows Focus Navigation Daemon
**Domain:** Win32 window management — system tray polish, GUI settings, daemon UX (v4.0)
**Researched:** 2026-03-03
**Confidence:** HIGH

## Executive Summary

Focus is a mature, already-shipped .NET 8 Windows daemon (v3.1) that manages keyboard-driven window navigation via a CAPSLOCK-activated overlay system. The v4.0 milestone is a tray polish pass: replacing the generic Windows system icon with a custom one, enriching the context menu with live daemon status, adding a WinForms settings window for config editing, and exposing a "Restart Daemon" action. This is not a net-new product — it is a finishing layer on a proven architecture that already handles the hard problems (WH_KEYBOARD_LL hook, layered window rendering, grid-based window move/resize).

The recommended implementation approach requires zero new NuGet packages. Every capability needed for v4.0 — ICO encoding, tray status display, settings form controls, daemon self-restart — is covered by APIs already present in the BCL and the `System.Windows.Forms` assembly that ships with `UseWindowsForms=true`. The only csproj change is adding an `<EmbeddedResource>` entry for the generated ICO file. The settings form is a pure code-constructed `Form` subclass; no designer files, no RESX, no WPF. The existing config hot-reload pattern (re-reads JSON on every keypress) means settings changes are live immediately — no daemon restart is required after saving.

The primary risk in this milestone is UX correctness on two fronts: atomic JSON writes (partial-file writes during hot-reload will crash the keypress handler) and single-instance settings form management (opening a second window instead of focusing the existing one is a common tray app mistake). Both risks have clear, well-documented mitigations. The Win32 and WinForms APIs involved are stable, extensively documented, and verified against official sources — this is a LOW engineering risk milestone.

---

## Key Findings

### Recommended Stack

The existing stack (`net8.0-windows`, `UseWindowsForms=true`, `CsWin32`, `System.Text.Json`) provides everything needed for v4.0 without additions. ICO generation is handled by a 30-line hand-written binary encoder using `System.IO.BinaryWriter` and `System.Drawing.Bitmap` — both already available. The ICO binary format (6-byte header, 16-byte directory per image, raw PNG blobs) is a public documented format; no third-party ICO library is warranted. For daemon self-restart, `Environment.ProcessPath` (the .NET 6+ preferred API) is used with `Process.Start`; `Application.Restart()` is explicitly ruled out because it throws `NotSupportedException` when WinForms is not the application entry point.

**Core technologies (v4.0 additions, all already in project):**
- `System.IO.BinaryWriter` + `System.Drawing.Bitmap`: ICO binary encoding — 30-line encoder, no new dependency
- `System.Windows.Forms.ContextMenuStrip.Opening` event: live status refresh in tray menu — fires on right-click, zero polling cost
- `System.Windows.Forms.TabControl` + `TableLayoutPanel` + `NumericUpDown` + `ColorDialog`: settings form layout — all BCL WinForms, `net8.0-windows`
- `System.Environment.ProcessPath` + `System.Diagnostics.Process.Start`: daemon self-restart — .NET 6+ preferred API, null-check required
- `System.Reflection.Assembly.GetManifestResourceStream`: embedded ICO loading — requires exact manifest resource name; verify with `GetManifestResourceNames()` at startup if uncertain

**Zero new NuGet packages. Zero new Win32 P/Invoke entries. Zero new CsWin32 requirements.**

### Expected Features

The v4.0 milestone has one goal: make the daemon feel like a polished, installed Windows tool with an identity, accessible configuration, and operational transparency. The reference pattern is production tools like Docker Desktop and Tailscale — both show status in the right-click menu, expose settings via a tray-accessible form, and provide a restart action.

**Must have (table stakes):**
- Custom tray icon — `SystemIcons.Application` signals "unfinished software"; distinct icon is required
- Hover tooltip ("Focus — Navigation Daemon") — users hovering over an unidentified icon cannot tell which process owns it
- Status labels in context menu (hook status, uptime, last action) — non-clickable `ToolStripMenuItem { Enabled = false }`, refreshed on `Opening` event
- Settings menu item — opens single-instance WinForms form; `BringToFront()` if already open
- Settings form: strategy ComboBox, grid NumericUpDown fields, overlay color pickers (ColorDialog), overlay delay NumericUpDown, Save button (atomic write), About section with GitHub LinkLabel
- Restart Daemon menu item — `Environment.ProcessPath` + `Process.Start` + `Application.ExitThread()`
- Atomic JSON config write — write to `.tmp`, then `File.Replace`; prevents corrupt reads during hot-reload
- Single-instance settings form pattern — track `_settingsForm` reference, check `!IsDisposed`, call `BringToFront()`

**Should have (competitive advantage):**
- Daemon status panel in settings form with 500ms refresh timer — hook alive, uptime, last action surfaced in the form
- Dynamic tooltip text showing brief live status — add if users want at-a-glance status without opening the menu
- Settings form Cancel / discard-changes behavior — add if users report accidental saves

**Defer (v5+):**
- `excludeList` editor in settings form — list control UI for a low-frequency operation; direct JSON editing is sufficient for now
- Settings window keyboard shortcuts (Enter = Save, Escape = Close)
- Config file path displayed in settings for advanced users

**Anti-features (explicitly rejected):**
- Balloon tip notifications on navigation actions — at 10-50 navigations per minute, maximally disruptive
- Animated tray icon — draws constant attention; violates Windows notification area guidelines
- Settings window auto-apply on every keystroke — causes continuous JSON parse errors during editing

### Architecture Approach

The v4.0 architecture is entirely additive to the existing v3.1 system. The existing `DaemonApplicationContext` (TrayIcon.cs) gains a custom icon, enriched `ContextMenuStrip`, and references to a new `DaemonStatus` object for live status. A new `SettingsForm` class (pure code-constructed `Form` subclass, no designer) is introduced as a single-instance form managed by a `_settingsForm` reference field. The restart flow exits the current process after spawning a new one; the existing `DaemonMutex.AcquireOrReplace()` semantics handle the kill-and-replace cycle automatically.

**Major components:**
1. `DaemonApplicationContext` (TrayIcon.cs) — MODIFIED: embedded ICO, enriched context menu, `Opening` event handler, `_settingsForm` reference, restart handler
2. `SettingsForm` (new file) — pure WinForms `Form`: `TabControl` with Navigation/Overlay/Grid/About tabs, `TableLayoutPanel` layout, `ColorDialog` for overlay colors, atomic `Save` to JSON
3. `DaemonStatus` (new object or inline state on DaemonCommand.cs) — exposes hook alive bool, `_startTime`, `_lastActionDescription` for status display in both menu and settings form
4. ICO generation (startup utility method, no separate file required) — `WriteIco()` static method produces embedded multi-size ICO using `BinaryWriter` + `Bitmap.Save(stream, ImageFormat.Png)`

### Critical Pitfalls

The PITFALLS.md document covers v3.1 window move/resize in detail (14 new pitfalls, 8 previously mitigated). For the v4.0 tray polish milestone, the most relevant risks are:

1. **Atomic config write on Settings Save** — The existing hot-reload fires on every CAPSLOCK keypress. If the settings form truncates the JSON file mid-write, the next keypress gets a parse error. Mitigation: write to `config.json.tmp` then `File.Replace(tmp, config, null)`. This is a correctness requirement, not a nice-to-have.

2. **ColorDialog ARGB vs RGB** — `System.Windows.Forms.ColorDialog` returns `System.Drawing.Color` (RGB only; no alpha channel in the dialog UI). The existing config stores colors as 8-digit ARGB hex (`#AARRGGBB`). Mitigation: extract the alpha from the existing config value, apply it to the dialog-chosen RGB, reconstruct with `$"#{alpha:X2}{r:X2}{g:X2}{b:X2}"`. Do NOT use `ColorTranslator.FromHtml` — it supports `#RRGGBB` only.

3. **`Application.Restart()` throws `NotSupportedException`** — This daemon uses a hybrid STA/main-thread topology where `Application.Run()` is called on the STA thread but is not the program entry point. `Application.Restart()` detects this and throws. Mitigation: use `Environment.ProcessPath` + `Process.Start(new ProcessStartInfo(exePath, "daemon --background") { UseShellExecute = false })` then `Application.ExitThread()`.

4. **`ProcessStartInfo.UseShellExecute` defaults to `false` in .NET Core/.NET 5+** — When opening the GitHub link in the About section via `Process.Start`, `UseShellExecute = true` is mandatory. The default `false` causes the URL to be treated as an executable path, throwing an error. Mitigation: always set `UseShellExecute = true` explicitly for URL opening.

5. **Settings form multi-instance** — If `OnSettingsClicked` creates a new `SettingsForm` without checking whether one already exists, users can open multiple settings windows. Each holds its own in-memory copy of config values; the last one to save wins. Mitigation: check `_settingsForm is { IsDisposed: false }`, call `BringToFront()`, return early.

---

## Implications for Roadmap

The v4.0 milestone has clear internal dependencies that dictate phase order. The icon must exist before any tray UX is shipped. The context menu structure must be finalized before the status labels or Settings entry can be wired. The settings form is independent of the restart action. All phases are small and well-bounded — this is a polish milestone, not a feature milestone.

### Phase 1: Custom Icon and Tray Identity

**Rationale:** The icon is the visual foundation. Every other tray feature assumes a distinct, identified icon is present. Shipping with `SystemIcons.Application` while adding a status menu makes the rest of the UX feel inconsistent. This is the lowest-complexity, highest-visibility change in the milestone.

**Delivers:** Custom multi-size `.ico` embedded as assembly resource; `NotifyIcon.Icon` set from embedded resource; `NotifyIcon.Text` set to "Focus — Navigation Daemon"; csproj updated with `<EmbeddedResource>` and `<ApplicationIcon>`.

**Addresses:** Custom tray icon (P1), hover tooltip (P1).

**Avoids:** Third-party ICO libraries (zero value over a 30-line hand-written encoder); `Icon.FromHandle(bitmap.GetHicon())` (produces a single-size HICON, cannot serialize a multi-size `.ico`).

### Phase 2: Enriched Context Menu

**Rationale:** Once the icon is settled, the context menu can be expanded. Status labels, Settings item, and Restart item are all tightly coupled to the `ContextMenuStrip` and the new `DaemonStatus` object. Implementing them together in one phase is more efficient than building each in isolation and avoids partial-state shipping.

**Delivers:** `ToolStripMenuItem` status labels (Hook / Uptime / Last Action) refreshed on `Opening` event; "Settings" menu item (wired to placeholder initially); "Restart Daemon" menu item; correct separator structure matching Windows tray conventions; `DaemonStatus` object injected into `DaemonApplicationContext`.

**Addresses:** Inline status in context menu (P1), Restart Daemon item (P1), Context menu rebuilds on Opening (P1).

**Avoids:** Polling timer for status (use `Opening` event — zero cost); `ToolStripLabel` in `ContextMenuStrip` (unsupported — use `ToolStripMenuItem { Enabled = false }` instead); static menu text showing stale status.

### Phase 3: WinForms Settings Form

**Rationale:** The settings form is the most complex piece of this milestone. It depends on Phase 2's "Settings" menu item being in place as the entry point, and on `DaemonStatus` being available for the status panel. Building the form after the menu scaffolding reduces integration friction and allows the Settings entry point to be tested (opens placeholder) before the full form is ready.

**Delivers:** `SettingsForm` with Navigation, Overlay, Grid, and About tabs; all config fields exposed (strategy ComboBox, grid NumericUpDown, overlay color pickers with ColorDialog, overlay delay NumericUpDown); daemon status panel; atomic JSON Save; GitHub LinkLabel in About; single-instance open/focus pattern wired to the Settings menu item.

**Addresses:** Settings form (all P1 feature items), About section (P1), daemon status panel in form (P1), atomic JSON config write (P1 correctness requirement), single-instance settings form (P1 correctness requirement).

**Avoids:** Settings auto-apply on keystroke (corrupt mid-edit JSON); `ColorTranslator.FromHtml` for ARGB parsing (RGB-only limitation); `Form.ShowDialog` blocking the STA pump (use `Form.Show()` instead); alpha loss when picking overlay colors via ColorDialog.

### Phase 4: Integration and Polish

**Rationale:** After the three core phases, a dedicated integration pass validates the full tray lifecycle, catches edge cases in DPI scaling, and addresses any HiDPI rendering issues in the settings form. Also the right moment to wire up Settings form Cancel behavior and dynamic tooltip text if user feedback calls for it.

**Delivers:** End-to-end tray lifecycle validation (icon, menu, status, settings, save, restart, exit); HiDPI settings form validation on mixed-DPI setups; error handling for config file permission failures; any P2 items from the feature list (dynamic tooltip, Cancel button) that user testing confirms are needed.

**Addresses:** Settings form DPI scaling (MEDIUM confidence gap); config file permission error handling; P2 items driven by real feedback.

**Avoids:** Config file permission errors swallowed silently (catch `IOException`/`UnauthorizedAccessException` on Save, show `MessageBox`).

### Phase Ordering Rationale

- Phase 1 before Phase 2: the icon must be resolved before any other tray UX is shipped. Mixed `SystemIcons.Application` with an enriched menu would look inconsistent in intermediate states.
- Phase 2 before Phase 3: the Settings menu item is the entry point for the settings form. Having the wiring point in place first means Phase 3 only needs to build the form and connect it — the menu scaffolding is already done.
- Phase 3 is self-contained: it depends on `DaemonStatus` (from Phase 2) and the Settings menu item (from Phase 2), but its internal implementation does not block Phase 4.
- Phase 4 is a quality gate, not a feature phase: its scope is determined by what integration testing reveals.

### Research Flags

Phases with well-documented patterns (skip research-phase):
- **Phase 1:** ICO binary format is a public documented format; `BinaryWriter` + `Bitmap.Save(stream, ImageFormat.Png)` is standard BCL. Approach verified against two independent implementations with HIGH confidence.
- **Phase 2:** `ContextMenuStrip.Opening` event and `ToolStripMenuItem.Enabled` are documented BCL WinForms APIs, HIGH confidence. Pattern matches established real-world tools.
- **Phase 3:** All WinForms controls (`TabControl`, `TableLayoutPanel`, `NumericUpDown`, `ColorDialog`, `LinkLabel`) are documented .NET 8 BCL APIs with HIGH confidence. Layout strategy is well-established for code-constructed forms.

Phases likely needing deeper investigation during planning:
- **Phase 4:** HiDPI settings form rendering — code-constructed WinForms forms (no designer) on per-monitor DPI setups. The daemon is already PerMonitorV2, but `AutoScaleMode.Dpi` behavior for dynamically-constructed forms is MEDIUM confidence. Needs validation against the existing app manifest before shipping.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Zero new packages. All APIs verified against official Microsoft Learn docs. ICO encoder verified against two independent implementations. `Application.Restart` exclusion confirmed by official docs and WinForms issue tracker. |
| Features | HIGH | UX patterns verified against comparable production tools (Docker Desktop, Tailscale). Windows notification area guidelines explicitly documented. Anti-features reasoned from first principles with clear documented rationale. |
| Architecture | HIGH | Additive changes only to a proven v3.1 codebase. Component boundaries are clear and precise. No new threading models or Win32 subsystems required. Restart flow leverages existing mutex replace semantics. |
| Pitfalls | HIGH | All v4.0-specific pitfalls (atomic write, ARGB parsing, Application.Restart, UseShellExecute default, multi-instance form) verified against official docs and confirmed WinForms issues. v3.1 pitfalls retained in PITFALLS.md for regression reference. |

**Overall confidence:** HIGH

### Gaps to Address

- **Settings form HiDPI rendering (MEDIUM confidence):** Code-constructed WinForms forms inherit DPI awareness from the application manifest. The daemon already runs as PerMonitorV2 on multi-monitor setups, but `AutoScaleMode.Dpi` behavior for code-constructed forms (no designer) needs validation during Phase 4 against actual mixed-DPI hardware or DPI simulator. If rendering is off, set `AutoScaleMode = AutoScaleMode.Dpi` explicitly on the form constructor.

- **ICO visual design (product decision, not technical gap):** The ICO content is a programmatic GDI render of a simple geometric shape. The specific shape/design is not specified by research — this is a product decision. The generation pipeline is ready; the content needs a decision before Phase 1 implementation begins.

- **Alpha transparency for overlay colors in settings form (design decision):** `ColorDialog` exposes RGB only. The existing config ARGB format requires a design decision: expose alpha as a separate `NumericUpDown` (0-255) per color, or silently preserve the existing alpha when the user picks a new color. Neither is wrong — make this decision during Phase 3 planning and document the choice in the settings form implementation.

---

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn — Generate ICO in .NET Core (Edi Wang)](https://edi.wang/post/2019/11/12/generate-a-true-ico-format-image-in-net-core) — ICO encoder approach, `ImageFormat.Icon` bug confirmation
- [darkfall gist — Bitmap to ICO](https://gist.github.com/darkfall/1656050) — ICO binary format structure, BinaryWriter pattern
- [Microsoft Learn — Application.Restart](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.restart?view=windowsdesktop-8.0) — NotSupportedException for non-WinForms-entry-point apps
- [dotnet/winforms Issue #2769](https://github.com/dotnet/winforms/issues/2769) — Application.Restart InvalidOperationException confirmed for non-ClickOnce deployments
- [Microsoft Learn — ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-8.0) — defaults to false in .NET Core; must be true for URL opening
- [Microsoft Learn — Environment.ProcessPath](https://learn.microsoft.com/en-us/dotnet/api/system.environment.processpath?view=net-6.0) — .NET 6+ preferred API for executable path
- [Microsoft Learn — CA1839](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1839) — ProcessPath preferred over Process.GetCurrentProcess().MainModule.FileName
- [Microsoft Learn — ContextMenuStrip](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.contextmenustrip?view=windowsdesktop-8.0) — Opening event documentation
- [Microsoft Learn — LinkLabel (WinForms)](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/link-to-an-object-or-web-page-with-wf-linklabel-control) — LinkClicked + Process.Start pattern
- [Microsoft Learn — TableLayoutPanel Best Practices](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/best-practices-for-the-tablelayoutpanel-control) — settings form layout guidance
- [Microsoft Learn — ColorDialog](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.colordialog?view=windowsdesktop-8.0) — API reference, RGB-only limitation confirmed
- [Microsoft Learn — NotifyIcon (WinForms)](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/app-icons-to-the-taskbar-with-wf-notifyicon) — tray icon API
- [Microsoft Learn — Notifications and the Notification Area (Win32)](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area) — Windows UX guidelines for tray apps
- [Microsoft Learn — Application Settings Architecture](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/application-settings-architecture) — WinForms settings patterns

### Secondary (MEDIUM confidence)
- [Red Gate Simple Talk — Creating Tray Applications in .NET](https://www.red-gate.com/simple-talk/development/dotnet-development/creating-tray-applications-in-net-a-practical-guide/) — practitioner patterns for tray apps; cross-checked against official docs

---
*Research completed: 2026-03-03*
*Ready for roadmap: yes*
