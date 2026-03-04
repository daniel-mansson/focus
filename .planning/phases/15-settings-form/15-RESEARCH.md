# Phase 15: Settings Form - Research

**Researched:** 2026-03-04
**Domain:** WinForms settings window, config I/O, atomic file write, single-instance form pattern
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- Single-pane form with GroupBox per config section
- About info at the top as a header/title area (not a GroupBox) — bold project name, attribution, GitHub link
- Section order below header: Navigation → Grid & Snapping → Overlays → Keybindings
- Fixed size form (no resize handle)
- Single Save button anchored bottom-right; form closes via X button
- Four color swatch buttons (Left, Right, Up, Down) — small colored rectangles showing current color
- Clicking a swatch opens system ColorDialog for RGB selection
- Single shared opacity slider (TrackBar or NumericUpDown, 0–100%) for all four overlay colors
- Color swatches update visually in the form as user picks colors (live preview in form)
- Config file only written on Save (no mid-edit disk writes)
- Full keybinding reference table showing all daemon bindings in a GroupBox titled "Keybindings"
- Read-only multi-line Label with monospace font (Consolas) for column alignment
- About sits at top as a header area (no GroupBox); "Focus v{X.Y}" in larger/bold font
- Attribution line "by Daniel Månsson"; clickable LinkLabel to https://github.com/daniel-mansson/focus
- Version derived from `Assembly.GetExecutingAssembly().GetName().Version` (set in .csproj)

### Claude's Discretion

- Exact form dimensions and padding
- GroupBox internal spacing and label sizing
- Tab order between controls
- Opacity slider range granularity (0–100 or 0–255)
- How color swatches render (Panel with BackColor, custom paint, etc.)
- Exact keybinding text content (read from daemon code)

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SETS-01 | Settings window opens as a non-modal WinForms form (single instance — focuses existing if already open) | Single-instance pattern: store `_settingsForm` field, check `== null \|\| _settingsForm.IsDisposed`, call `BringToFront()` |
| SETS-02 | User can select navigation strategy from a dropdown (six strategies) | `ComboBox` with `DropDownStyle = DropDownList`; populate with all six `Strategy` enum values; round-trip via `(Strategy)comboBox.SelectedItem` |
| SETS-03 | User can edit grid fractions and snap tolerance via numeric inputs | `NumericUpDown` controls; GridFractionX/Y default 16/12, SnapTolerancePercent default 10 |
| SETS-04 | User can pick overlay colors for each direction via system ColorDialog | `ColorDialog` with `FullOpen = true`; swatches are Panel controls with BackColor set |
| SETS-05 | User can edit overlay delay (overlayDelayMs) via numeric input | `NumericUpDown` with `Minimum = 0`, `Maximum = 5000` (or similar); integer milliseconds |
| SETS-06 | Settings form displays current keybindings as a reference label | Multi-line read-only `Label` with `Font = new Font("Consolas", 8f)` inside a GroupBox |
| SETS-07 | Save button writes config atomically (write .tmp, then File.Replace) | `File.WriteAllText(tmpPath, json)` then `File.Replace(tmpPath, configPath, null)` — no parse-error window |
| SETS-08 | About section shows project name, version, attribution, and GitHub link | `Assembly.GetExecutingAssembly().GetName().Version`; `LinkLabel` with `Links.Add` and `LinkClicked` opening browser via `Process.Start` |
</phase_requirements>

---

## Summary

Phase 15 builds a WinForms settings window that lets users view and edit all key config values (strategy, grid fractions, snap tolerance, overlay colors and opacity, overlay delay), see a keybinding reference, and read About information. The codebase already has full WinForms infrastructure (`UseWindowsForms = true`, STA thread, existing forms like OverlayWindow), and all config properties are fully defined in `FocusConfig.cs`. No new NuGet packages are needed.

The main complexity points are: (1) the single-instance open/focus pattern, (2) the color swatch / ColorDialog / opacity slider interaction where ARGB hex strings must be decomposed and recomposed correctly, (3) the atomic save using `File.Replace`, and (4) wiring `OnSettingsClicked` in `DaemonApplicationContext` to replace the current "open in editor" behavior.

The form is code-constructed (no designer .resx), consistent with the rest of the project. The planner should allocate one plan for the full form since it is self-contained: construct the form in code, wire all controls, implement atomic save, replace `OnSettingsClicked`.

**Primary recommendation:** Build `SettingsForm.cs` as a code-only WinForms Form. Use Panel controls for color swatches (BackColor = swatch color). Decompose ARGB hex strings into alpha + RGB on load; recompose to `#AARRGGBB` on save. Use `File.Replace` for atomic write. Store a `SettingsForm?` reference in `DaemonApplicationContext` and check `IsDisposed` for single-instance.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Windows.Forms | net8.0-windows (built-in) | WinForms form, controls | Already used — `UseWindowsForms = true` in .csproj |
| System.Text.Json | net8.0 (built-in) | Serialize `FocusConfig` to JSON | Already used in `FocusConfig.cs` |
| System.Reflection | net8.0 (built-in) | Read assembly version | Standard .NET — no package needed |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.IO.File.Replace | net8.0 (built-in) | Atomic config write | SETS-07 — temp file then replace |
| System.Diagnostics.Process | net8.0 (built-in) | Open GitHub link in browser | `LinkLabel.LinkClicked` handler |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Code-only form construction | WinForms Designer (.resx) | Designer adds .resx complexity and is not used in this codebase; code-only is simpler and consistent |
| Panel for color swatch | Custom owner-draw Button | Panel with BackColor is simpler, fully clickable via Click event, no painting required |
| TrackBar for opacity | NumericUpDown for opacity | Both work; TrackBar gives continuous visual feedback; NumericUpDown shows exact numeric value — either is valid (Claude's discretion) |

**Installation:** No new packages required. All dependencies are already present or in the .NET 8 BCL.

---

## Architecture Patterns

### Recommended Project Structure

```
focus/Windows/Daemon/
├── TrayIcon.cs          # (unchanged name; DaemonApplicationContext lives here)
├── SettingsForm.cs      # NEW — the settings WinForms Form (code-only)
└── Overlay/
    └── OverlayColors.cs # ARGB hex string helpers — reuse GetArgb() for reading
```

### Pattern 1: Single-Instance Non-Modal Form

**What:** Store a nullable `Form` reference; on "Settings..." click, check if null or disposed; if so create + show; otherwise BringToFront.

**When to use:** Whenever a secondary window must not duplicate.

**Example:**
```csharp
// In DaemonApplicationContext:
private SettingsForm? _settingsForm;

private void OnSettingsClicked(object? sender, EventArgs e)
{
    if (_settingsForm == null || _settingsForm.IsDisposed)
    {
        _settingsForm = new SettingsForm();
        _settingsForm.Show();
    }
    else
    {
        _settingsForm.BringToFront();
        if (_settingsForm.WindowState == FormWindowState.Minimized)
            _settingsForm.WindowState = FormWindowState.Normal;
    }
}
```

**Important:** `IsDisposed` is true after the user closes the form (X button calls `Dispose` unless `FormClosing` sets `e.Cancel = true`). If the form is re-shown after being closed and disposed, create a new instance.

### Pattern 2: ARGB Hex String Decomposition / Recomposition

**What:** `OverlayColors` stores colors as `#AARRGGBB` hex strings. The settings form must read alpha + RGB separately (alpha for the shared slider, RGB for the color swatch/ColorDialog), and write them back.

**Example:**
```csharp
// Load: decompose "#BF4488CC" into alpha byte and System.Drawing.Color
static (byte alpha, Color rgb) ParseHexColor(string hex)
{
    var raw = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber);
    byte a = (byte)(raw >> 24);
    byte r = (byte)(raw >> 16);
    byte g = (byte)(raw >> 8);
    byte b = (byte)(raw);
    return (a, Color.FromArgb(r, g, b));  // alpha stored separately
}

// Save: recompose from alpha byte + ColorDialog result
static string ToHexColor(byte alpha, Color rgb)
    => $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
```

**Key insight:** `ColorDialog` works in RGB space only. The alpha is shared across all four colors and managed by the opacity slider. On save, recompose: alpha from slider + RGB from swatch → `#AARRGGBB`.

**Opacity slider mapping:**
- If using 0–100 range: `alpha = (byte)Math.Round(sliderValue / 100.0 * 255)`
- If using 0–255 range: `alpha = (byte)sliderValue`
- Recommended: 0–100 (user-facing percentage), convert to byte on save.

### Pattern 3: Atomic Config Write (SETS-07)

**What:** Write to `.tmp` file then replace the target, so a read during save never sees a partial file.

**Example:**
```csharp
static void SaveConfigAtomic(FocusConfig config)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };
    var json = JsonSerializer.Serialize(config, options);
    var configPath = FocusConfig.GetConfigPath();
    var tmpPath = configPath + ".tmp";
    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
    File.WriteAllText(tmpPath, json);
    File.Replace(tmpPath, configPath, null);  // atomic on same volume
}
```

**Note:** `File.Replace` is atomic only when source and destination are on the same volume (they are — both in `%APPDATA%/focus/`). The third argument is the backup file path — pass `null` to discard.

### Pattern 4: Assembly Version for About Section (SETS-08)

**What:** Read the assembly version at runtime.

**Example:**
```csharp
var version = Assembly.GetExecutingAssembly().GetName().Version;
string versionText = $"Focus v{version?.Major}.{version?.Minor}";
```

**Note:** The `.csproj` currently has no `<Version>` or `<AssemblyVersion>` property set. This means the assembly version defaults to `1.0.0.0`. The planner must add `<Version>4.0.0</Version>` (or appropriate value) to `focus.csproj` so the About section shows a meaningful version.

### Pattern 5: LinkLabel for GitHub Link (SETS-08)

**What:** `LinkLabel` with a `LinkClicked` handler that opens the URL via `Process.Start`.

**Example:**
```csharp
var link = new LinkLabel { Text = "https://github.com/daniel-mansson/focus" };
link.Links.Add(0, link.Text.Length, "https://github.com/daniel-mansson/focus");
link.LinkClicked += (_, e) =>
    Process.Start(new ProcessStartInfo
    {
        FileName = (string)e.Link!.LinkData!,
        UseShellExecute = true
    });
```

### Pattern 6: ComboBox for Strategy Enum (SETS-02)

**What:** Populate a `ComboBox` with all six `Strategy` enum values, using display strings.

**Example:**
```csharp
var strategyCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
foreach (Strategy s in Enum.GetValues<Strategy>())
    strategyCombo.Items.Add(s);
strategyCombo.SelectedItem = config.Strategy;
// On save:
config.Strategy = (Strategy)strategyCombo.SelectedItem!;
```

**Note:** `ToString()` on `Strategy` enum values produces Pascal-case (e.g., `Balanced`, `StrongAxisBias`). If kebab-case display is wanted, a custom `ToString` formatter or `ComboBox.FormatString` can be used — but Pascal-case is readable and acceptable.

### Pattern 7: Fixed-Size Form

**What:** Disable resize handle and maximize box.

**Example:**
```csharp
this.FormBorderStyle = FormBorderStyle.FixedDialog;
this.MaximizeBox = false;
this.MinimizeBox = false;  // optional — keep if user might want to minimize
```

### Anti-Patterns to Avoid

- **Modal `ShowDialog`:** SETS-01 requires non-modal (`Show()`, not `ShowDialog()`). Modal would block tray icon interaction.
- **Writing config in `FormClosing`:** Write only on Save button click (locked decision). `FormClosing` should only close the form (or cancel close if desired — but cancel-on-close is a future/deferred feature per SETS-F04).
- **Storing `Color` with alpha in swatch Panel:** `Panel.BackColor` uses `Color.FromArgb(a, r, g, b)`. Store alpha separately in a field; the swatch BackColor should be opaque RGB only (for visual clarity). Apply alpha only on save.
- **Calling `FocusConfig.Load()` on every paint:** Load once on form open, work with in-memory state, write on Save only.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Color picking | Custom color picker widget | `ColorDialog` (system) | Full RGB + hex input built-in; locked decision |
| Atomic file write | Read-modify-write | `File.Replace` | Handles cross-thread races, file replacement is OS-atomic on same volume |
| Version string | Hardcoded string | `Assembly.GetExecutingAssembly().GetName().Version` | Single source of truth; updates with .csproj |

**Key insight:** WinForms + System.Text.Json + File.Replace are all BCL — zero extra dependencies. The only new file is `SettingsForm.cs`.

---

## Common Pitfalls

### Pitfall 1: `SettingsForm` shown after daemon restart leaves orphaned form
**What goes wrong:** If the user opens Settings, then clicks "Restart Daemon", the old `DaemonApplicationContext` is disposed while `_settingsForm` is still visible. The form stays open pointing to stale state.
**Why it happens:** `DaemonApplicationContext.Dispose` does not close the `SettingsForm`.
**How to avoid:** In `DaemonApplicationContext.Dispose`, explicitly close `_settingsForm` if not null and not disposed: `_settingsForm?.Close()`.
**Warning signs:** Settings form lingers after daemon restart with stale data.

### Pitfall 2: Alpha byte miscalculation when converting opacity slider
**What goes wrong:** 0–100 percentage does not map linearly to 0–255 with integer division — `80 / 100 * 255 = 0` (integer division).
**Why it happens:** Operator precedence + integer division truncates to 0.
**How to avoid:** Use floating-point: `(byte)Math.Round(sliderValue / 100.0 * 255)`.
**Warning signs:** All colors appear fully transparent or fully opaque when opacity is changed.

### Pitfall 3: `File.Replace` throws when target file does not exist yet
**What goes wrong:** `File.Replace(tmpPath, configPath, null)` requires `configPath` to already exist. On a fresh install with no config file, this throws `FileNotFoundException`.
**Why it happens:** `File.Replace` replaces an existing file; it does not create one.
**How to avoid:** Check if config file exists; if not, `File.Move(tmpPath, configPath)` or call `FocusConfig.WriteDefaults` path pattern: `File.WriteAllText(tmpPath, json); if (File.Exists(configPath)) File.Replace(tmpPath, configPath, null); else File.Move(tmpPath, configPath);`
**Warning signs:** Save fails with `FileNotFoundException` on first-ever save.

### Pitfall 4: DPI scaling on PerMonitorV2 makes code-constructed form controls misaligned
**What goes wrong:** Control positions set in pixels look correct at 100% DPI but shift at 125%/150%.
**Why it happens:** The app manifest sets `PerMonitorV2` DPI awareness. WinForms code-constructed forms respect `AutoScaleMode.Dpi` if set, but require correct base sizes.
**How to avoid:** Set `this.AutoScaleMode = AutoScaleMode.Dpi` on the form. Use `TableLayoutPanel` or anchor-based layout rather than manual `Location` pixel coordinates. Test at 125% and 150% DPI (note from STATE.md: DPI virtualization is a known MEDIUM-confidence concern).
**Warning signs:** Controls overlap or have unexpected gaps at non-100% DPI.

### Pitfall 5: `Assembly.GetName().Version` returns `1.0.0.0` (default)
**What goes wrong:** About section shows "Focus v1.0" instead of a meaningful version.
**Why it happens:** `focus.csproj` has no `<Version>` element; .NET defaults to `1.0.0.0`.
**How to avoid:** Add `<Version>4.0.0</Version>` to the `<PropertyGroup>` in `focus.csproj` before implementing the About section.
**Warning signs:** About section always shows `v1.0`.

### Pitfall 6: Form not fully initialized before `Show()` is called from menu handler
**What goes wrong:** `Show()` called synchronously in `OnSettingsClicked` works fine since it runs on the STA message pump thread — no issue here.
**Why it doesn't apply:** `OnSettingsClicked` runs on the STA thread (all menu click events do), so `new SettingsForm()` + `Show()` is safe. No cross-thread creation needed.

---

## Code Examples

Verified patterns from codebase analysis and .NET 8 BCL:

### Keybinding Reference Text

Based on `KeyboardHookHandler.cs` — all recognized combos:

```
CapsLock + ←/→/↑/↓         Navigate (focus window in direction)
CapsLock + W/A/S/D          Navigate (WASD aliases)
CapsLock + LAlt + ←/→/↑/↓  Move window in direction
CapsLock + LWin + ←/→/↑/↓  Grow/shrink window in direction
CapsLock + 1–9              Jump to numbered window overlay
```

(Exact formatting is Claude's discretion — read from `KeyboardHookHandler.cs` to verify completeness.)

### Full Form Bootstrap Pattern

```csharp
// SettingsForm.cs
internal sealed class SettingsForm : Form
{
    private readonly FocusConfig _config;
    private readonly Dictionary<Direction, Color> _swatchColors = new();
    private byte _opacityAlpha;

    public SettingsForm()
    {
        _config = FocusConfig.Load();
        InitializeAlphaFromConfig();
        BuildUi();
    }

    private void InitializeAlphaFromConfig()
    {
        // Extract alpha from first color (all four share the same alpha per design)
        var raw = uint.Parse(_config.OverlayColors.Left.TrimStart('#'), NumberStyles.HexNumber);
        _opacityAlpha = (byte)(raw >> 24);

        // Extract RGB for each swatch
        foreach (var (dir, hex) in new[]
        {
            (Direction.Left,  _config.OverlayColors.Left),
            (Direction.Right, _config.OverlayColors.Right),
            (Direction.Up,    _config.OverlayColors.Up),
            (Direction.Down,  _config.OverlayColors.Down),
        })
        {
            var argb = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber);
            byte r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
            _swatchColors[dir] = Color.FromArgb(r, g, b);
        }
    }
}
```

### Atomic Save

```csharp
private void SaveConfig()
{
    // Recompose ARGB hex strings from swatch colors + shared alpha
    _config.OverlayColors.Left  = ToHexColor(_opacityAlpha, _swatchColors[Direction.Left]);
    _config.OverlayColors.Right = ToHexColor(_opacityAlpha, _swatchColors[Direction.Right]);
    _config.OverlayColors.Up    = ToHexColor(_opacityAlpha, _swatchColors[Direction.Up]);
    _config.OverlayColors.Down  = ToHexColor(_opacityAlpha, _swatchColors[Direction.Down]);

    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };
    var json = JsonSerializer.Serialize(_config, options);
    var configPath = FocusConfig.GetConfigPath();
    var tmpPath = configPath + ".tmp";
    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
    File.WriteAllText(tmpPath, json);

    if (File.Exists(configPath))
        File.Replace(tmpPath, configPath, null);
    else
        File.Move(tmpPath, configPath);
}

private static string ToHexColor(byte alpha, Color rgb)
    => $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
```

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| Open config in default text editor (current `OnSettingsClicked`) | Open WinForms settings form | SETS-01 through SETS-08 delivered |
| No version in .csproj (defaults to 1.0.0.0) | Add `<Version>4.0.0</Version>` to .csproj | SETS-08 shows meaningful version |

**Deprecated/outdated:**
- `OnSettingsClicked` current body (opens config.json in editor): Replaced entirely by single-instance form open pattern.

---

## Open Questions

1. **Opacity slider: shared alpha assumption is approximate**
   - What we know: All four colors currently have `0xBF` as their alpha byte (75%). The design decision is one shared opacity slider.
   - What's unclear: If the user previously had different alphas per direction (e.g., edited config manually), the slider will show only the Left color's alpha. On save, all four colors get the same alpha.
   - Recommendation: Document this lossy behavior. On load, read alpha from Left. On save, apply to all four. This is consistent with the single-slider design and is acceptable.

2. **Version to add to .csproj**
   - What we know: Current .csproj has no `<Version>` element; defaults to `1.0.0.0`.
   - What's unclear: What version string should appear in the About section — `4.0` (milestone), `1.0` (first release), or something else.
   - Recommendation: Planner should add `<Version>4.0.0</Version>` and `<AssemblyVersion>4.0.0.0</AssemblyVersion>` to .csproj as part of Phase 15. About will show "Focus v4.0".

3. **Tab order and form height**
   - What we know: Fixed size form; tab order is Claude's discretion.
   - What's unclear: Exact pixel height needed for all sections (Navigation, Grid & Snapping, Overlays, Keybindings, About header, Save button).
   - Recommendation: Use `AutoSize = true` or `TableLayoutPanel` for GroupBoxes and let the form auto-size based on content, then set `FormBorderStyle = FixedDialog` after layout. Or use a fixed height (e.g., 620px wide, 700px tall) and adjust during implementation.

---

## Validation Architecture

> `workflow.nyquist_validation` is not present in `.planning/config.json` — skipping this section.

---

## Sources

### Primary (HIGH confidence)

- Codebase: `/focus/Windows/FocusConfig.cs` — config properties, Load(), WriteDefaults(), GetConfigPath(), JsonStringEnumConverter usage
- Codebase: `/focus/Windows/Daemon/Overlay/OverlayColors.cs` — ARGB hex string format (#AARRGGBB), GetArgb() decomposition
- Codebase: `/focus/Windows/Daemon/TrayIcon.cs` (`DaemonApplicationContext`) — STA thread context, OnSettingsClicked stub to replace, existing form lifecycle pattern
- Codebase: `/focus/Windows/Daemon/KeyboardHookHandler.cs` — all recognized key combos for keybinding reference (SETS-06)
- Codebase: `/focus/focus.csproj` — UseWindowsForms=true, net8.0-windows target, no `<Version>` element currently set
- .NET 8 BCL: `System.IO.File.Replace` — atomic file replacement; `System.Reflection.Assembly.GetExecutingAssembly().GetName().Version` — runtime version; `System.Windows.Forms.ColorDialog` — system color picker; `System.Windows.Forms.LinkLabel` — clickable link

### Secondary (MEDIUM confidence)

- `DaemonApplicationContext` pattern: Derived from existing code structure — SettingsForm field stored and checked with `IsDisposed` (noted in STATE.md accumulated context: "Settings form: single-instance pattern via _settingsForm reference + IsDisposed check + BringToFront")
- DPI concern: STATE.md flags "DPI virtualization (MEDIUM confidence): Code-constructed WinForms forms on PerMonitorV2 setups — AutoScaleMode.Dpi behavior needs validation during Phase 15 execution"

### Tertiary (LOW confidence)

- None

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries are BCL or already present in .csproj; no new packages
- Architecture: HIGH — patterns derived directly from existing codebase code (FocusConfig, OverlayColors, DaemonApplicationContext, KeyboardHookHandler)
- Pitfalls: HIGH for File.Replace and alpha math (code-verified); MEDIUM for DPI (flagged in STATE.md but not yet validated)

**Research date:** 2026-03-04
**Valid until:** 2026-04-04 (stable BCL APIs; no external dependencies to decay)
