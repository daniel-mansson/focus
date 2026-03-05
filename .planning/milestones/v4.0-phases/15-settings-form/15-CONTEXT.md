# Phase 15: Settings Form - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

A WinForms settings window accessible from the tray "Settings..." menu item. Users can view and edit all key configuration values (navigation strategy, grid fractions, snap tolerance, overlay colors, overlay delay) and see keybinding reference and About information. Single instance, non-modal, atomic save.

</domain>

<decisions>
## Implementation Decisions

### Form layout
- Single-pane form with GroupBox per config section
- About info at the top as a header/title area (not a GroupBox) — bold project name, attribution, GitHub link
- Section order below header: Navigation → Grid & Snapping → Overlays → Keybindings
- Fixed size form (no resize handle)
- Single Save button anchored bottom-right; form closes via X button

### Color editing
- Four color swatch buttons (Left, Right, Up, Down) — small colored rectangles showing current color
- Clicking a swatch opens system ColorDialog for RGB selection
- Single shared opacity slider (TrackBar or NumericUpDown, 0-100%) for all four overlay colors
- Color swatches update visually in the form as user picks colors (live preview in form)
- Config file only written on Save (no mid-edit disk writes)

### Keybinding reference
- Full keybinding reference table showing all daemon bindings (CapsLock + Arrow = navigate, CapsLock + Shift + Arrow = move, CapsLock + Number = jump, etc.)
- Displayed in its own GroupBox titled "Keybindings"
- Read-only multi-line Label with monospace font (Consolas) for column alignment

### About section
- Sits at top of form as a header area (no GroupBox)
- "Focus v{X.Y}" in larger/bold font (version from assembly version)
- "by Daniel Månsson" as attribution line
- Clickable LinkLabel to https://github.com/daniel-mansson/focus
- Version derived from Assembly.GetExecutingAssembly().GetName().Version (set in .csproj)

### Claude's Discretion
- Exact form dimensions and padding
- GroupBox internal spacing and label sizing
- Tab order between controls
- Opacity slider range granularity (e.g. 0-100 or 0-255)
- How color swatches render (Panel with BackColor, custom paint, etc.)
- Exact keybinding text content (read from daemon code)

</decisions>

<specifics>
## Specific Ideas

- About header should feel like a title, not a settings section — distinct from the GroupBoxes below it
- Color swatches should be clearly clickable (not just colored labels)
- Keybinding table should be comprehensive — include all CapsLock modifier combos the daemon recognizes

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `FocusConfig.cs`: All config properties already defined (Strategy enum, GridFractionX/Y, SnapTolerancePercent, OverlayColors, OverlayDelayMs). Has `Load()`, `WriteDefaults()`, `GetConfigPath()` methods. Uses System.Text.Json with kebab-case enum converter.
- `OverlayColors.cs`: Per-direction ARGB hex strings (#BF4488CC format). `GetArgb()` parses hex to uint. Alpha is the first byte of the hex string.
- `DaemonStatus.cs`: Tracks StartTime, LastAction — not needed for settings but shows STA-thread state pattern.
- `Strategy` enum: 6 values (Balanced, StrongAxisBias, ClosestInDirection, EdgeMatching, EdgeProximity, AxisOnly) — dropdown needs all six.

### Established Patterns
- JSON config with kebab-case enum names via `JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)`
- Config path: `%APPDATA%/focus/config.json`
- STA thread: All WinForms controls created on the STA thread in DaemonApplicationContext constructor
- Assembly resource embedding: Icon already embedded via .csproj `<EmbeddedResource>`

### Integration Points
- `TrayIcon.cs` line 109: `OnSettingsClicked` currently opens config.json in default editor — must be replaced with opening the new SettingsForm
- `DaemonApplicationContext` constructor: Settings form needs access to the STA thread context for proper WinForms operation
- `FocusConfig.Load()` / `WriteDefaults()`: Settings form reads config on open, writes on save. Need an atomic write method (write .tmp, File.Replace).
- Config path from `FocusConfig.GetConfigPath()` — reuse for save target

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 15-settings-form*
*Context gathered: 2026-03-04*
