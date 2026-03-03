# Stack Research

**Domain:** Windows system tray polish — custom icon, enhanced context menu, WinForms settings UI, daemon restart
**Researched:** 2026-03-03
**Confidence:** HIGH (all claims verified against official Microsoft Learn documentation and primary sources)

---

## Scope of This Document

This document covers **additions required for v4.0 only**. The existing validated stack is unchanged and not re-researched:

**Already validated (do not re-research):** .NET 8 `net8.0-windows`, CsWin32 0.3.269, WinForms (`UseWindowsForms=true`), `NotifyIcon` + `ContextMenuStrip`, GDI for overlays, `System.CommandLine` 2.0.3, `System.Text.Json` (built-in), `Microsoft.Extensions.FileSystemGlobbing` 8.0.0.

The four new capability areas are:
1. **ICO file generation** — produce a `.ico` at build time or startup, embed as `EmbeddedResource`
2. **Enhanced context menu** — status labels (hook status, uptime, last action) and additional menu items
3. **WinForms settings window** — `Form` with `TabControl`, `TableLayoutPanel`, `ComboBox`, `NumericUpDown`, `ColorDialog`, `LinkLabel`
4. **Daemon restart** — self-restart from context menu using `Environment.ProcessPath` + `Process.Start`

---

## New Capabilities Required

### 1. ICO File Generation

**Finding:** .NET does not have a built-in ICO encoder. `System.Drawing.Image.Save(stream, ImageFormat.Icon)` produces a PNG file, not a valid ICO. The correct approach is a custom binary encoder using `BinaryWriter`.

**Approach — hand-written ICO encoder (no new dependency):**

The ICO format is straightforward binary: a 6-byte header, a 16-byte directory entry per image, then the raw PNG-encoded image data. `System.Drawing.Bitmap` (already available via `System.Drawing.Common` which ships with `UseWindowsForms=true`) can resize and PNG-encode in memory. The encoder writes the directory and concatenates the PNG blobs.

```csharp
// ICO binary format (all little-endian)
// Header: reserved(2) + type(2=1) + count(2)
// Per image: width(1) height(1) colors(1) reserved(1) planes(2) bpp(2) dataLen(4) offset(4)
// Then raw PNG bytes for each size

static void WriteIco(Bitmap source, Stream output)
{
    int[] sizes = { 256, 48, 32, 16 };
    var pngBlobs = new List<byte[]>();

    foreach (int sz in sizes)
    {
        using var resized = new Bitmap(source, new Size(sz, sz));
        using var ms = new MemoryStream();
        resized.Save(ms, ImageFormat.Png);
        pngBlobs.Add(ms.ToArray());
    }

    using var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
    w.Write((short)0);                // reserved
    w.Write((short)1);                // type = icon
    w.Write((short)sizes.Length);     // image count

    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); // 0 means 256
        w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        w.Write((byte)0);             // colors in palette
        w.Write((byte)0);             // reserved
        w.Write((short)0);            // color planes
        w.Write((short)32);           // bits per pixel
        w.Write((int)pngBlobs[i].Length);
        w.Write((int)offset);
        offset += pngBlobs[i].Length;
    }

    foreach (var blob in pngBlobs)
        w.Write(blob);
}
```

**Embedding:** Once generated, embed via `<EmbeddedResource Include="Resources\focus.ico" />` in the csproj and load at startup with `Assembly.GetExecutingAssembly().GetManifestResourceStream("Focus.Resources.focus.ico")`.

**Render the source bitmap with GDI (already in codebase):** The existing GDI DIB renderer can draw a simple geometric icon (e.g., directional arrows or a stylized "F") at 256x256 into a `Bitmap`, which is then passed to `WriteIco`. No external image editor needed.

**Replaceable at runtime:** If a `focus.ico` file exists in `%APPDATA%\focus\`, load it from disk instead of the embedded resource. This allows user customization without rebuilding.

**Loading the embedded ICO into NotifyIcon:**
```csharp
// Load from embedded resource
using var stream = Assembly.GetExecutingAssembly()
    .GetManifestResourceStream("Focus.Resources.focus.ico");
_trayIcon.Icon = new Icon(stream!);
```

**Confidence:** HIGH — ICO binary format is a documented public format. `BinaryWriter` + `Bitmap.Save(stream, ImageFormat.Png)` is all BCL. Approach verified against [Edi Wang's ICO encoder](https://edi.wang/post/2019/11/12/generate-a-true-ico-format-image-in-net-core) and [darkfall's gist](https://gist.github.com/darkfall/1656050).

---

### 2. Enhanced Context Menu — Status Labels

**Finding:** `ToolStripMenuItem` with `Enabled = false` renders grayed-out text — that is the standard WinForms pattern for non-interactive status display in a context menu. No special type is needed. Known visual inconsistency: in some themes the disabled item does not visually gray correctly unless `ForeColor` is also set. Set `Enabled = false` on the status items; they will not fire `Click`.

**Pattern for status labels:**
```csharp
// Status label items — non-clickable, read-only
var hookStatus = new ToolStripMenuItem("Hook: active") { Enabled = false };
var uptime     = new ToolStripMenuItem("Uptime: 0d 0h 3m") { Enabled = false };
var lastAction = new ToolStripMenuItem("Last: left → Firefox") { Enabled = false };

menu.Items.Add(hookStatus);
menu.Items.Add(uptime);
menu.Items.Add(lastAction);
menu.Items.Add(new ToolStripSeparator());
menu.Items.Add("Settings", null, OnSettingsClicked);
menu.Items.Add("Restart Daemon", null, OnRestartClicked);
menu.Items.Add(new ToolStripSeparator());
menu.Items.Add("Exit", null, OnExitClicked);
```

**Updating status before the menu opens:** Subscribe to `ContextMenuStrip.Opening` event. Update `hookStatus.Text`, `uptime.Text`, `lastAction.Text` there. This fires on every right-click before the menu renders — no polling required.

```csharp
menu.Opening += (_, _) =>
{
    hookStatus.Text = $"Hook: {(_hook.IsInstalled ? "active" : "inactive")}";
    uptime.Text = $"Uptime: {FormatUptime(DateTime.UtcNow - _startTime)}";
    lastAction.Text = $"Last: {_lastActionDescription}";
};
```

**APIs involved:** All in `System.Windows.Forms` — `ContextMenuStrip`, `ToolStripMenuItem`, `ToolStripSeparator`. No new types beyond what's already used.

**Confidence:** HIGH — `ContextMenuStrip.Opening` event and `ToolStripMenuItem.Enabled` are documented BCL WinForms APIs, .NET 8 target framework.

---

### 3. WinForms Settings Window

**Finding:** A pure code-constructed `Form` (no `.resx`, no designer) with `TableLayoutPanel` for label/control alignment is the correct approach for this project (code-only, no VS designer dependency). `TabControl` organizes sections. Standard WinForms controls cover all needed settings.

**Layout strategy:**

`TabControl` with tabs: **Navigation**, **Overlay**, **Grid**, **About**

Within each tab, `TableLayoutPanel` with 2 columns (label + control) provides aligned form layout. `AutoSize = true` on the panel keeps height proportional to content.

**Controls per setting type:**

| Setting | Control | Notes |
|---------|---------|-------|
| Strategy (enum) | `ComboBox` | `DropDownStyle = DropDownList`; populate from `Enum.GetValues<Strategy>()` |
| Wrap behavior (enum) | `ComboBox` | Same pattern |
| Overlay delay (int ms) | `NumericUpDown` | `Minimum = 0`, `Maximum = 2000`, `Increment = 50` |
| Grid fraction X/Y (int) | `NumericUpDown` | `Minimum = 2`, `Maximum = 64` |
| Snap tolerance (int %) | `NumericUpDown` | `Minimum = 0`, `Maximum = 50` |
| Overlay colors (ARGB hex) | Color swatch button + `ColorDialog` | Button shows current color as `BackColor`; click opens `ColorDialog`; read `dialog.Color.ToArgb()` |
| Exclude patterns | `ListBox` + Add/Remove buttons | Multiline list; Add opens `InputBox`-style dialog or `TextBox`+OK |

**ColorDialog for overlay colors:**
```csharp
var dialog = new ColorDialog
{
    FullOpen = true,
    Color = ParseArgbHex(config.OverlayColors.Left)
};
if (dialog.ShowDialog(this) == DialogResult.OK)
{
    config.OverlayColors.Left = $"#{dialog.Color.A:X2}{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    UpdateColorSwatch(leftColorButton, dialog.Color);
}
```

**About tab — LinkLabel for GitHub:**
```csharp
var link = new LinkLabel { Text = "https://github.com/user/focus", AutoSize = true };
link.LinkClicked += (_, _) =>
    Process.Start(new ProcessStartInfo(link.Text) { UseShellExecute = true });
```

**ShowDialog from context menu (STA thread):**
The `ContextMenuStrip.Opening` handler and `ToolStripMenuItem.Click` handlers run on the STA thread (they are dispatched through the WinForms message pump). Calling `settingsForm.ShowDialog(owner)` directly from the click handler is safe — no additional thread needed.

```csharp
private SettingsForm? _settingsForm;

private void OnSettingsClicked(object? sender, EventArgs e)
{
    // Reuse if already open; bring to front
    if (_settingsForm is { IsDisposed: false })
    {
        _settingsForm.BringToFront();
        return;
    }
    _settingsForm = new SettingsForm(_config);
    _settingsForm.Show();  // Non-blocking — allows tray icon to remain responsive
}
```

**Saving config:** On "Save" button click in `SettingsForm`, serialize back to `%APPDATA%\focus\config.json` via the existing `System.Text.Json` path. The daemon's existing "fresh config load per keypress" pattern (`FocusConfig.Load()` on each event) means changes are live immediately — no daemon restart required for config changes.

**Confidence:** HIGH — `TabControl`, `TableLayoutPanel`, `NumericUpDown`, `ComboBox`, `ColorDialog`, `LinkLabel`, `Form.Show()` are all documented BCL WinForms controls available in `net8.0-windows`. `Process.Start` with `UseShellExecute = true` is required in .NET 8 (defaults to `false` unlike .NET Framework — confirmed via [official docs](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-8.0)).

---

### 4. Daemon Restart

**Finding:** The existing `DaemonMutex.AcquireOrReplace()` already kills any running daemon instance and takes ownership of the mutex. A "restart" from the tray is therefore: spawn a new `focus daemon` process, then exit the current one. The new process's `AcquireOrReplace()` handles killing the old process automatically.

**Self-path:** Use `Environment.ProcessPath` (introduced in .NET 6, available in .NET 8). This is the preferred modern API over `Process.GetCurrentProcess().MainModule.FileName` — no Process allocation, no dispose requirement.

```csharp
private void OnRestartClicked(object? sender, EventArgs e)
{
    var exePath = Environment.ProcessPath;
    if (exePath is null) return;

    // Spawn new instance — it will kill this one via AcquireOrReplace()
    Process.Start(new ProcessStartInfo(exePath, "daemon --background")
    {
        UseShellExecute = false  // No shell needed for self-restart
    });

    // Exit this instance — the new process will acquire the mutex after killing us
    _trayIcon.Visible = false;
    _onExit();
    Application.ExitThread();
}
```

**Why not `Application.Restart()`:** `Application.Restart()` throws `NotSupportedException` when the application is not a pure WinForms application (i.e., when `Application.Run()` was not the entry point). This daemon uses `Application.Run()` only on the STA thread, with the main thread blocking on `cts.Token.WaitHandle.WaitOne()`. `Application.Restart()` is unreliable in this topology — confirmed by [official docs](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.restart?view=windowsdesktop-8.0): "Throws NotSupportedException: Your code is not a Windows Forms application."

**Confidence:** HIGH — `Environment.ProcessPath` is documented .NET 6+ API. `Process.Start(ProcessStartInfo)` is documented. The existing `DaemonMutex` replace-semantics already handle the kill-and-replace cycle.

---

## Recommended Stack (v4.0 Additions)

### No New NuGet Packages Required

All capabilities are covered by existing dependencies:

| Capability | Provided By | Already a Dependency? |
|------------|-------------|----------------------|
| ICO binary encoding | `System.IO.BinaryWriter` (BCL) | Yes |
| Bitmap resize + PNG encode | `System.Drawing.Bitmap` (via `UseWindowsForms`) | Yes |
| Embedded resource loading | `System.Reflection.Assembly` (BCL) | Yes |
| Status labels in context menu | `System.Windows.Forms.ToolStripMenuItem` | Yes |
| Menu `Opening` event for live updates | `System.Windows.Forms.ContextMenuStrip` | Yes |
| Settings form layout | `System.Windows.Forms.TabControl`, `TableLayoutPanel` | Yes |
| Enum picker | `System.Windows.Forms.ComboBox` | Yes |
| Integer settings | `System.Windows.Forms.NumericUpDown` | Yes |
| Color picker | `System.Windows.Forms.ColorDialog` | Yes |
| GitHub link | `System.Windows.Forms.LinkLabel` | Yes |
| Open browser | `System.Diagnostics.Process.Start` (BCL) | Yes |
| Self-restart path | `System.Environment.ProcessPath` (.NET 6+) | Yes |
| Spawn new process | `System.Diagnostics.Process.Start` (BCL) | Yes |

**Zero new NuGet packages.** All required types are in the BCL or already-referenced WinForms assembly.

---

## csproj Changes

### Embed the ICO file

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\focus.ico" />
</ItemGroup>
```

The embedded resource name becomes `Focus.Resources.focus.ico` (assembly default namespace + folder path + filename). Verify with `Assembly.GetExecutingAssembly().GetManifestResourceNames()` at startup if the name is uncertain.

### ApplicationIcon (optional, for taskbar/exe icon)

```xml
<PropertyGroup>
  <ApplicationIcon>Resources\focus.ico</ApplicationIcon>
</PropertyGroup>
```

This embeds the icon into the PE header for Windows Explorer / taskbar display. Separate from the `EmbeddedResource` entry used for `NotifyIcon` at runtime. Both entries can coexist.

---

## Integration Points

### TrayIcon.cs (DaemonApplicationContext)

- Replace `SystemIcons.Application` with loaded ICO from embedded resource
- Add status `ToolStripMenuItem` items (disabled) + `menu.Opening` handler
- Add "Settings" and "Restart Daemon" menu items
- Inject `DaemonStatus` object (hook state, start time, last action) into `DaemonApplicationContext`

### SettingsForm (new file)

- Pure code-constructed `Form` subclass — no `.resx`, no designer
- Constructor accepts `FocusConfig` (the current loaded config)
- "Save" button: serialize to `%APPDATA%\focus\config.json` via `FocusConfig.WriteDefaults` (or an updated `FocusConfig.Save()` variant)
- "Cancel" button: close without saving

### DaemonCommand.cs

- Thread start time for uptime calculation
- Last action description string (updated by keyboard handler callbacks)
- Restart handler calls `Environment.ProcessPath` + `Process.Start` before signaling exit

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Third-party ICO libraries (BluwolfIcons, etc.) | Zero value over a 30-line hand-written encoder; adds a dependency to a project with a "minimal dependencies" constraint | `BinaryWriter` + `Bitmap.Save(stream, ImageFormat.Png)` |
| `Application.Restart()` | Throws `NotSupportedException` when WinForms is not the entry point — this daemon uses a hybrid STA/main-thread topology | `Environment.ProcessPath` + `Process.Start` |
| `System.Drawing.Icon.FromHandle(bitmap.GetHicon())` for ICO generation | `GetHicon()` produces a single-size HICON in memory; cannot serialize a multi-size `.ico` file this way. Also requires `DestroyIcon` cleanup | Binary ICO encoder with multiple PNG blobs |
| WPF / WinUI for settings window | No WPF/WinUI dependency exists; adding one for a settings form is disproportionate and conflicts with the existing WinForms message pump | `System.Windows.Forms.Form` (already available) |
| `ToolStripLabel` in `ContextMenuStrip` | `ToolStripLabel` is designed for `ToolStrip`/`StatusStrip`, not `ContextMenuStrip`; adding it to a `ContextMenuStrip` is unsupported and may produce layout artifacts | `ToolStripMenuItem` with `Enabled = false` |
| `ColorTranslator.FromHtml` for ARGB parsing | `ColorTranslator.FromHtml` supports only RGB hex (`#RRGGBB`), not ARGB (`#AARRGGBB`). The existing config uses 8-digit ARGB hex. | Manual parse: `Color.FromArgb(Convert.ToInt32(hex.TrimStart('#'), 16))` |
| `System.Windows.Forms.ColorDialog` with `AllowFullOpen = false` | Restricts to basic colors only — insufficient for ARGB overlay color selection | `ColorDialog { FullOpen = true }` |
| Polling timer for status updates | Wastes CPU; status only needs to be current when the menu opens | `ContextMenuStrip.Opening` event |

---

## Version Compatibility

| Type / API | Assembly | .NET Requirement | Notes |
|------------|----------|-----------------|-------|
| `Environment.ProcessPath` | `System.Runtime` | .NET 6+ | Available in .NET 8. Returns `null` if path unavailable — null-check required. |
| `TabControl`, `TableLayoutPanel`, `NumericUpDown`, `ColorDialog`, `LinkLabel` | `System.Windows.Forms` | .NET Core 3.0+ | All available in `net8.0-windows` with `UseWindowsForms=true`. |
| `ContextMenuStrip.Opening` event | `System.Windows.Forms` | .NET Core 3.0+ | Fires before menu renders on every right-click. |
| `ProcessStartInfo.UseShellExecute` | `System.Diagnostics` | All .NET versions | **Defaults to `false` in .NET Core/.NET 5+.** Must be set to `true` explicitly for URL opening. |
| `System.Drawing.Bitmap`, `BinaryWriter` | `System.Drawing.Common`, `System.Runtime` | Windows only (already constrained by `net8.0-windows`) | `System.Drawing.Common` is Windows-only from .NET 6+. Already satisfied. |
| `Assembly.GetManifestResourceStream` | `System.Runtime` | All .NET versions | Returns `null` if resource name is wrong — null-check required. |

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| Hand-written ICO binary encoder | BluwolfIcons NuGet | Never — 30-line implementation vs. a dependency for a feature used once at startup |
| `Environment.ProcessPath` | `Process.GetCurrentProcess().MainModule!.FileName` | Only if targeting .NET 5 or earlier (pre-`ProcessPath` API). .NET 8 → always prefer `ProcessPath`. |
| `Form.Show()` (non-blocking) for settings | `Form.ShowDialog()` (blocking) | `ShowDialog` blocks the STA message pump, preventing tray icon interaction while settings are open. `Show()` keeps both live. |
| `ContextMenuStrip.Opening` for status refresh | Background timer polling + Invoke | Timer approach wastes CPU when the menu is never opened; `Opening` event is precise and zero-cost. |
| GDI DIB (existing renderer) to draw the source icon bitmap | External `.ico` asset checked into repo | GDI renderer is already proven; drawing a simple icon programmatically means no binary asset to manage. Both options are valid — external asset is acceptable if a designer provides one. |

---

## Sources

- [Generate a True ICO Format Image in .NET Core — Edi Wang](https://edi.wang/post/2019/11/12/generate-a-true-ico-format-image-in-net-core) — ICO encoder approach, confirmed `System.Drawing.Image.Save(ImageFormat.Icon)` bug — HIGH confidence
- [Bitmap to ICO gist — darkfall](https://gist.github.com/darkfall/1656050) — ICO binary format structure, `BinaryWriter` pattern, PNG-per-size approach — HIGH confidence
- [Application.Restart Method — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.restart?view=windowsdesktop-8.0) — confirms `NotSupportedException` for non-WinForms-entry-point apps — HIGH confidence
- [ProcessStartInfo.UseShellExecute — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-8.0) — default `false` in .NET Core; must be `true` for URL opening — HIGH confidence
- [Environment.ProcessPath — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.environment.processpath?view=net-6.0) — .NET 6+ preferred API for current executable path — HIGH confidence
- [CA1839: Use Environment.ProcessPath — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1839) — confirms `ProcessPath` preferred over `Process.GetCurrentProcess().MainModule.FileName` — HIGH confidence
- [ContextMenuStrip — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.contextmenustrip?view=windowsdesktop-8.0) — `Opening` event documentation — HIGH confidence
- [Link to Web Page with LinkLabel — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/link-to-an-object-or-web-page-with-wf-linklabel-control) — `LinkClicked` + `Process.Start` pattern — HIGH confidence
- [TableLayoutPanel Best Practices — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/best-practices-for-the-tablelayoutpanel-control) — layout guidance for settings forms — HIGH confidence

---

*Stack research for: Window focus navigation v4.0 — system tray polish, settings UI, daemon restart*
*Researched: 2026-03-03*
