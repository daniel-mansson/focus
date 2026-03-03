# Feature Research

**Domain:** System tray polish — custom icon, enhanced context menu, WinForms settings window, daemon restart (v4.0 milestone)
**Researched:** 2026-03-03
**Confidence:** HIGH (Windows tray icon and WinForms patterns are stable, well-documented Win32 and .NET 8 APIs; verified against official Microsoft docs, established real-world app patterns, and WinForms API references)

> **Scope note:** This file covers v4.0 features only — system tray polish for the already-shipped daemon. All prior features (navigation, overlays, grid move/resize) are shipped in v1.0–v3.1. This research focuses on: expected UX for tray icon context menus, inline status display conventions, settings window behavior, color/config editing UX, and daemon self-restart.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features users of Windows background daemon tools assume exist. Missing these = the daemon feels unfinished or untrustworthy.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Custom tray icon (recognizable, not generic) | `SystemIcons.Application` (the generic Windows gear) signals "unfinished software" to every experienced Windows user. Any tool that lives in the tray permanently needs a distinct icon. | LOW | Load from embedded .ico resource via `new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream(...))`. Icon must be provided at both 16x16 and 32x32 (HiDPI). Use `LoadIconMetric` via P/Invoke or load both sizes in the .ico file. WinForms `NotifyIcon.Icon` accepts `System.Drawing.Icon`. |
| Hover tooltip showing app name | The tray tooltip (shown on hover) should identify what the app is. `NotifyIcon.Text` defaults to empty — users hovering over an unidentified icon can't tell which process it belongs to. | LOW | `NotifyIcon.Text` max 63 chars (Win32 NOTIFYICONDATA.szTip limit). Set to "Focus — Navigation Daemon" or similar. Can include brief status ("Focus — Running"). |
| Right-click opens a context menu | Every tray icon user right-clicks to get options. No context menu = the icon appears non-functional. The existing single-item ("Exit") menu meets the minimum bar; this milestone expands it. | LOW | `NotifyIcon.ContextMenuStrip` (ContextMenuStrip is the modern .NET replacement for the deprecated ContextMenu class). Attach before setting `Visible = true`. |
| Exit menu item | Every tray app must provide a way to quit. Not having Exit forces users to Task Manager. | LOW | Already exists. Keep as the last item, separated by a separator from other menu items. |
| Settings menu item that opens a settings window | Users expect a configuration surface reachable from the tray. Without it, configuration requires manually editing JSON — acceptable for power users, unacceptable as the only path. | MEDIUM | `ToolStripMenuItem` labeled "Settings". Opens a WinForms Form. If window already exists and is open, bring it to the foreground (`form.BringToFront()`, `form.Activate()`) rather than opening a second instance. |

### Differentiators (Competitive Advantage)

Features that set this daemon apart from a generic background process with a tray icon.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Inline status in context menu (non-clickable labels) | Most tray apps make you open a settings window to see daemon health. Showing hook status, uptime, and last action directly in the right-click menu gives instant confidence that the daemon is alive and working — no navigation required. Well-designed tools (Docker Desktop, Tailscale) use this pattern. | MEDIUM | Use disabled `ToolStripMenuItem` items as read-only status labels — `item.Enabled = false` renders grayed text that is non-interactive. Alternatively use `ToolStripLabel` which is inherently non-clickable. Status lines: "Hook: Active" / "Hook: FAILED", "Uptime: 2h 14m", "Last: Navigate Left". Rebuild the menu `Opening` event so values are fresh each time the menu appears. |
| Restart Daemon menu item | Power users who modify config files or notice behavioral issues expect a restart option without killing and relaunching the daemon manually. Direct competition: most background daemons require CLI or Task Manager to restart. | MEDIUM | `ToolStripMenuItem` labeled "Restart Daemon". On click: dispose `NotifyIcon`, call `Process.Start(Application.ExecutablePath, "daemon")`, then `Application.Exit()`. The existing "replace semantics" (kill existing on new daemon launch) handles the singleton concern — no extra orchestration needed. |
| WinForms settings window for live config editing | The daemon already hot-reloads config per keypress. A settings UI that writes to the same JSON file means changes take effect immediately on next keypress — no restart required. This closes the gap between "power user who edits JSON" and "user who wants a GUI." | HIGH | Form with: `ComboBox` for navigation strategy (the six enum values), `NumericUpDown` for gridFractionX/Y and overlayDelayMs, color swatch buttons (click opens `ColorDialog`) for the four overlay colors, and a Save button that writes `FocusConfig` back to the JSON file. About section with GitHub link (`LinkLabel`). |
| About section with attribution and GitHub link | Establishes provenance and gives users a path to contribute or report issues. Small but expected for any polished developer tool. | LOW | A tab or group box in the settings form labeled "About". Static labels: app name, version (`Assembly.GetName().Version`), author attribution, `LinkLabel` for GitHub URL (calls `Process.Start` with the URL to open in default browser). |
| Daemon status display (hook alive, uptime, last action) | Surfacing daemon health confirms that the low-level keyboard hook is functioning. A daemon that silently fails its hook leaves users confused when CAPSLOCK navigation stops working. Uptime and last-action log give confidence the daemon is processing keystrokes. | MEDIUM | Hook status: boolean from the daemon's hook installation result. Uptime: `DateTime.UtcNow - _startTime`. Last action: a `string _lastAction` updated on every navigation/move operation (e.g., "Navigate Left", "Grow Right"). These can be shown both in the context menu (read-only labels) and in the settings window (dedicated status panel). |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Balloon tip notifications on every navigation action | "Visual confirmation" that the daemon did something | Navigation happens 10-50 times per minute during active use. Balloon tips at that frequency are maximally disruptive — Windows 7+ even allows users to suppress all notifications from an app, risking loss of any future legitimate notifications. | Use the context menu status "Last: Navigate Left" as the confirmation surface. Zero interruptions, always fresh on demand. |
| System tray icon that animates or changes color to show state | "Quick glance" status | Animated tray icons draw attention constantly — the tray is not a status monitor. Windows guidelines explicitly state the notification area is for temporary status, not a persistent dashboard. Also: maintaining multiple icon states and a refresh timer adds complexity. | Static icon with a clear, distinct design. Hover tooltip can show one-line status if needed. |
| Settings window that auto-applies changes as you type | "Instant feedback" | The daemon reloads config per keypress already, but mid-edit state (e.g., a partially typed hex color) will be invalid JSON and crash the reload. Applying on every keystroke would cause continuous parse errors during editing. | Save button (or OK/Apply) that writes the file only when all values are valid. The daemon picks up the change on next keypress automatically — there is effectively zero delay from the user's perspective. |
| Full-featured color wheel / custom color designer in settings | "Better color control" | System `ColorDialog` (Windows common dialog) provides a full color picker including custom colors, RGB sliders, and hex input — sufficient for overlay color selection. Building a custom color picker is weeks of unneeded work. | `ColorDialog` via `new ColorDialog { Color = currentColor, FullOpen = true }`. Shows both palette and custom color input. Exactly what users expect. |
| Minimize-to-tray behavior for the settings window | "Keep settings accessible" | Settings is a modal-ish configuration surface — users open it, change settings, close it. There is no reason for it to persist. Minimize-to-tray requires overriding form close, tracking shown/hidden state, and adding a "Show Settings" tray action. | Open settings from the tray icon. When the window is closed, it is gone. Re-open from the tray. Single instance: if already open, bring to front. |
| Multi-tab settings window with pages for every config key | "Organized settings" | There are ~8-10 config keys total. Tabs add navigation overhead for trivial content. The entire config fits comfortably on one vertical form with labeled sections (Navigation, Grid, Overlay Colors, Overlay Timing). | Single-pane form with `GroupBox` sections. Tabs only if the form becomes taller than 600px. |
| "Restore defaults" button that resets to factory config | Users sometimes lose track of their changes | Factory defaults are arbitrary — what counts as the default overlay color? The config already has sensible defaults baked into `FocusConfig` initialization. Resetting to them loses intentional customization without confirmation. | If desired later: "Reset to Defaults" with a confirmation dialog. Not needed for v4.0. |

---

## Feature Dependencies

```
[Embedded .ico resource in assembly]
    └──required by──> [Custom tray icon (NotifyIcon.Icon)]
                          └──required by──> [All tray UX]

[NotifyIcon (already exists)]
    └──required by──> [Hover tooltip]
    └──required by──> [Context menu (ContextMenuStrip)]
                          └──required by──> [Status labels (disabled ToolStripMenuItem)]
                          └──required by──> [Settings menu item → settings window]
                          └──required by──> [Restart Daemon menu item]
                          └──required by──> [Exit menu item (already exists)]

[Daemon state: hook status bool, start time, last action string]
    └──required by──> [Inline status display in context menu]
    └──required by──> [Status panel in settings window]

[FocusConfig (existing JSON config class)]
    └──required by──> [Settings form fields (read values on open)]
    └──required by──> [Settings Save button (write values to JSON)]
    └──enhanced by──> [Hot reload on keypress (already ships) — settings saves are immediately live]

[Settings WinForms Form]
    └──required by──> [Navigation strategy ComboBox]
    └──required by──> [Grid fraction NumericUpDown fields]
    └──required by──> [Overlay color buttons → ColorDialog]
    └──required by──> [Overlay delay NumericUpDown]
    └──required by──> [About section with GitHub LinkLabel]
    └──required by──> [Daemon status panel]
    └──depends on──> [Single-instance form pattern: track reference, bring to front if open]

[Process.Start self-restart]
    └──required by──> [Restart Daemon menu item]
    └──depends on──> [Existing single-instance replace semantics (kill existing on launch)]
```

### Dependency Notes

- **Icon before anything else:** The tray icon visual identity is the first thing users see. Embed the .ico as a manifest resource before writing any other tray feature. Without it, the whole milestone still looks unpolished.
- **ContextMenuStrip replaces ContextMenu:** The old `NotifyIcon.ContextMenu` is deprecated. Use `NotifyIcon.ContextMenuStrip` (System.Windows.Forms.ContextMenuStrip). Already available in .NET 8 WinForms.
- **Status requires daemon state exposure:** The daemon currently doesn't track `_startTime` or `_lastAction` or expose hook status externally. These need to be added to the daemon class before the status display works. This is internal state tracking, not a new external dependency.
- **Settings form single-instance pattern is critical:** If the user clicks Settings while the window is already open, it must focus the existing window — not open a second copy. Track the form reference (`_settingsForm`), check `!= null && !IsDisposed`, call `BringToFront()` + `Activate()`.
- **Restart depends on existing replace semantics:** `Process.Start(Application.ExecutablePath, "daemon")` works because the daemon already kills any running instance on startup (the existing mutex replace behavior). The restart menu item just triggers a new launch and exits the current process. No extra coordination required.
- **JSON write from settings must not break hot-reload:** Hot-reload reads the config file on every CAPSLOCK keypress. If the settings form writes a partial file (e.g., truncated mid-save), the next keypress will get a parse error. Use atomic write: write to a `.tmp` file, then `File.Replace(tmpPath, configPath, null)`. Same pattern as any config file writer.

---

## MVP Definition

This milestone has one goal: make the daemon feel like a polished, installed Windows tool with an identity, accessible configuration, and operational transparency.

### Launch With (v4.0)

Minimum viable product for the system tray polish milestone.

- [ ] Custom .ico file embedded as assembly resource, loaded as `NotifyIcon.Icon`
- [ ] Hover tooltip set: "Focus — Navigation Daemon"
- [ ] `ContextMenuStrip` replacing any use of `ContextMenu`
- [ ] Status section at top of context menu (non-clickable): hook status, uptime, last action
- [ ] Separator below status
- [ ] "Settings" menu item → opens single-instance settings form
- [ ] "Restart Daemon" menu item → `Process.Start` self + `Application.Exit()`
- [ ] Separator above Exit
- [ ] "Exit" menu item (existing, moved to bottom)
- [ ] Settings form: navigation strategy ComboBox (six values from existing enum)
- [ ] Settings form: gridFractionX / gridFractionY NumericUpDown
- [ ] Settings form: overlayDelayMs NumericUpDown
- [ ] Settings form: four overlay color buttons (Left/Right/Up/Down), each opens `ColorDialog`, swatch shows current color
- [ ] Settings form: Save button writes to the JSON config file (atomic write)
- [ ] Settings form: About section — app name, version, attribution, GitHub LinkLabel
- [ ] Settings form: daemon status panel — hook status, uptime, last action (refreshes on form open or on a timer)
- [ ] Context menu rebuilds on `Opening` event to show fresh status values each time

### Add After Validation (v4.0.x)

- [ ] Tray icon tooltip updated dynamically to include status (e.g., "Focus — Hook: Active") — add if users want at-a-glance status without opening the menu
- [ ] Settings form "Cancel" / discard-changes behavior — add if users report accidentally saving while exploring
- [ ] Configurable tooltip text in About section — add if name/version conventions change

### Future Consideration (v5+)

- [ ] Full `excludeList` editor in settings form (add/remove app names) — deferred because it requires a list control UI and is low frequency usage; editing JSON directly is sufficient for now
- [ ] Settings window keyboard shortcuts (Enter = Save, Escape = Close) — polish iteration after core UX validated
- [ ] Config file path displayed in settings (so users can find and edit directly if they prefer) — low priority, advanced user feature

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Custom tray icon | HIGH | LOW | P1 |
| Hover tooltip | HIGH | LOW | P1 |
| Context menu status labels | HIGH | MEDIUM | P1 |
| Settings menu item | HIGH | LOW | P1 |
| Settings form: strategy, grid, timing | HIGH | MEDIUM | P1 |
| Settings form: overlay color pickers | HIGH | MEDIUM | P1 |
| Settings form: About + GitHub link | MEDIUM | LOW | P1 |
| Settings form: daemon status panel | MEDIUM | LOW | P1 |
| Restart Daemon menu item | MEDIUM | LOW | P1 |
| Context menu rebuilds on Opening | HIGH | LOW | P1 |
| Atomic JSON config write | HIGH | LOW | P1 (correctness requirement) |
| Single-instance settings form | HIGH | LOW | P1 (correctness requirement) |
| Tooltip showing live status | LOW | LOW | P2 |
| Settings form Cancel/discard | LOW | LOW | P2 |
| excludeList editor in settings | LOW | HIGH | P3 |

**Priority key:**
- P1: Must have for v4.0 launch
- P2: Should have, add when possible
- P3: Nice to have, future milestone

---

## UX Conventions Research

### Context Menu Structure (Standard Windows Pattern)

The established pattern for daemon/agent tray apps (Docker Desktop, Tailscale, Dropbox, OneDrive) is:

```
[App name or status header — non-clickable]
─────────────────────────────────────────
Hook: Active
Uptime: 2h 14m
Last: Navigate Left
─────────────────────────────────────────
Settings...
─────────────────────────────────────────
Restart Daemon
─────────────────────────────────────────
Exit
```

Key conventions from official Windows UX guidelines and common practice:
- Status items at the **top** of the menu, above actions — users see state before they see commands
- **Separators** (`ToolStripSeparator`) group related items; status / primary actions / destructive actions are three natural groups
- Status items use **disabled state** (`Enabled = false`) to signal non-interactivity — visually grayed, not clickable
- "Settings..." with ellipsis by convention (opens a window); "Exit" without ellipsis (immediate action)
- "Restart Daemon" without ellipsis if immediate; add ellipsis only if a confirmation dialog is shown
- **Rebuild on `Opening`** event — static menu text showing stale status is worse than no status

### Settings Window Conventions

WinForms settings dialogs for background tools follow these patterns:
- **Save / Close buttons** (not OK/Cancel) — "Save" writes to file, "Close" dismisses without saving; this is clearer than OK/Cancel for persistent config
- Alternatively: **OK (save + close) with Cancel** — valid if the user expects modal dialog semantics
- For this tool: **Save button** writes JSON, form stays open so user can verify; **Close button** (or window X) dismisses. No cancel needed because the form doesn't hold uncommitted state once saved
- Color swatches: a `Button` or `Panel` with `BackColor` set to the current color; clicking opens `ColorDialog`. After dialog confirms, update `BackColor` as live preview
- Group related settings with `GroupBox` labels: "Navigation", "Grid", "Overlay Colors", "Overlay Timing", "About"
- Daemon status in a read-only section at the bottom or a dedicated group box; use a `System.Windows.Forms.Timer` (500ms interval) to refresh uptime and last-action text while the form is open

### Restart Pattern

`Application.Restart()` has known issues with non-ClickOnce deployments in .NET and can throw `InvalidOperationException` (tracked WinForms issue). The reliable pattern for self-restart in a WinForms daemon context:

```csharp
string exePath = Application.ExecutablePath;
Process.Start(new ProcessStartInfo(exePath, "daemon") { UseShellExecute = true });
Application.Exit();
```

The existing replace semantics handle the race: the new instance will find and kill the old instance's mutex holder, so there is no need to delay or coordinate. `UseShellExecute = true` avoids common path quoting issues on Windows.

### Icon Generation

For v4.0, a hand-crafted or programmatically generated .ico file is required. Approach options (from high to low effort):
1. **Design in Inkscape/Figma and export as multi-size .ico** — cleanest, fully custom
2. **Generate programmatically at startup using GDI+** — draw a letter "F" (or focus arrows) on a `Bitmap` using `Graphics`, convert to `Icon` via `Icon.FromHandle(bitmap.GetHicon())` — no .ico file needed
3. **Use a free icon from a public domain source (e.g., Phosphor Icons, Tabler Icons)** — fast but less distinctive

The daemon already has GDI+ expertise (overlay rendering). Option 2 (programmatic generation at startup) is the fastest path with no external assets needed, and produces a result immediately. A proper .ico file can replace it later as a pure asset swap.

---

## Edge Case Coverage

### ColorDialog and ARGB Hex Format

The existing config stores overlay colors as hex ARGB strings (e.g., `"#FF00FF00"` for opaque green). `System.Windows.Forms.ColorDialog.Color` returns a `System.Drawing.Color`. Conversion: `Color.FromArgb(alpha, r, g, b)` and back to hex via `$"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"`. The `ColorDialog` does not natively support alpha editing — it returns RGB only. If alpha transparency for overlays is important to expose, add a separate `NumericUpDown` (0–255) for the alpha channel alongside each color button.

**Confidence:** HIGH — ColorDialog API verified in official .NET 8 docs.

### Context Menu Popup Position

Windows automatically positions context menus near the tray icon. WinForms `ContextMenuStrip` attached to `NotifyIcon` via `NotifyIcon.ContextMenuStrip` handles placement automatically — no manual coordinate calculation needed. The Win32 `CalculatePopupWindowPosition` is only needed for custom-drawn popup windows, not standard ContextMenuStrip.

**Confidence:** HIGH — confirmed in WinForms NotifyIcon documentation.

### Settings Form DPI Scaling

The settings form must render correctly on HiDPI displays. WinForms on .NET 8 supports `AutoScaleMode.Dpi` and per-monitor DPI awareness if the manifest is set. The existing daemon already runs on multi-monitor setups with mixed DPI — the settings form should inherit the same DPI configuration from the application manifest.

**Confidence:** MEDIUM — .NET 8 WinForms HiDPI support is documented; specific manifest requirements for per-monitor DPI awareness need validation against existing app manifest settings.

### Config File Permissions

The JSON config file is typically in `%APPDATA%\focus\` or alongside the executable. Writing requires filesystem write permission. If the file is read-only or in a protected location, the save will fail. The settings form should catch `IOException` and `UnauthorizedAccessException` on save, display a `MessageBox` with the error, and leave the current config intact.

**Confidence:** HIGH — standard exception handling pattern.

---

## Competitor/Reference Feature Analysis

How system tray polish is handled in comparable background daemon tools:

| Feature | Docker Desktop | Tailscale | Windows Defender (system) | This Tool (v4.0 plan) |
|---------|---------------|-----------|--------------------------|----------------------|
| Custom icon | Yes (whale) | Yes (lock) | Yes (shield) | Custom icon (to be designed/generated) |
| Status in context menu | Yes (Engine running / stopped) | Yes (Connected, IP shown) | No (action-only) | Hook status, uptime, last action |
| Settings via tray | Yes (full Settings window) | Yes (Settings menu opens window) | Yes (opens Windows Security) | WinForms settings form |
| Restart from tray | Yes (Restart Docker Desktop) | Yes (Restart Tailscale) | No | Restart Daemon menu item |
| Exit from tray | Yes | Yes | No (system component) | Yes (existing) |
| About section | Yes (in Settings window) | Yes (in Settings window) | No | In settings form |
| Config editing UI | Yes (full GUI) | Yes (full GUI) | Yes | GUI for key config values; raw JSON remains available |
| Balloon notifications | Rare (updates only) | Rare (connection changes only) | For security events only | None in v4.0 (noise risk outweighs benefit) |

---

## Sources

- [Notifications and the Notification Area — Win32 docs (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area) — HIGH confidence (official Win32 API reference, updated 2025)
- [NotifyIcon — Add Application Icons to the TaskBar (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/app-icons-to-the-taskbar-with-wf-notifyicon) — HIGH confidence (official WinForms docs)
- [Application Settings Architecture — Windows Forms (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/application-settings-architecture) — HIGH confidence (official WinForms docs)
- [ColorDialog Class — System.Windows.Forms (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.colordialog?view=windowsdesktop-8.0) — HIGH confidence (official .NET 8 API reference)
- [Application.Restart — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.restart?view=windowsdesktop-9.0) — HIGH confidence (official docs, caveats noted)
- [Application.Restart throws InvalidOperationException — dotnet/winforms Issue #2769](https://github.com/dotnet/winforms/issues/2769) — HIGH confidence (official WinForms repo issue confirming non-ClickOnce limitation)
- [Creating Tray Applications in .NET: A Practical Guide — Red Gate Simple Talk](https://www.red-gate.com/simple-talk/development/dotnet-development/creating-tray-applications-in-net-a-practical-guide/) — MEDIUM confidence (practitioner guide, patterns verified against official docs)
- Windows UX guidelines: notification area best practices (referenced via Win32 docs above) — HIGH confidence (official Microsoft guidelines)

---
*Feature research for: system tray polish — custom icon, context menu, settings window, daemon restart (v4.0 milestone)*
*Researched: 2026-03-03*
