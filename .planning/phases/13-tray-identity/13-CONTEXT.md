# Phase 13: Tray Identity - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Give the daemon a distinct visual identity in the system tray. Custom multi-size icon with correct tooltip text, icon embedded in the assembly. No runtime behavior changes — this is purely visual identity.

</domain>

<decisions>
## Implementation Decisions

### Icon visual design
- Crosshair/target concept — represents "focus" literally
- Monochrome white silhouette on transparent background — matches Windows 11 tray aesthetic
- Geometric bold style with corner brackets (camera focus brackets 「」) — strong presence at small sizes
- Include a center dot inside the brackets — gives a clear focal point

### Icon creation method
- Code-generated — no binary .ico committed to repo, defined as drawing primitives
- Icon serves as both the tray icon AND the .exe application icon (visible in Explorer, taskbar, Task Manager) — unified branding
- "Replaceable" (ICON-03) means swap the generation source code and rebuild — no runtime file override

### Tooltip text
- NotifyIcon.Text set to "Focus — Navigation Daemon" (em dash, not hyphen) per ICON-02
- Straightforward replacement of current "Focus Daemon" string

### Claude's Discretion
- Generation timing — MSBuild pre-build target vs standalone script that commits output
- Exact pixel layout of brackets and center dot at each size (16, 20, 24, 32px)
- Line thickness and bracket proportions at each resolution
- How to embed as both EmbeddedResource (for tray) and ApplicationIcon (for .exe)
- ICO file format details (BMP vs PNG frames for each size)

</decisions>

<specifics>
## Specific Ideas

- Focus brackets style: four L-shaped corner brackets with a solid dot in the center — like a camera viewfinder or focus reticle
- White on transparent to blend with Windows 11 system tray icons (which are monochrome/light)
- Must be visually distinct from the generic Windows application icon at a glance

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `DaemonApplicationContext` (TrayIcon.cs:10-89): Already creates NotifyIcon with context menu — icon and tooltip are single-line changes
- `focus.csproj`: WinForms enabled, no existing embedded resources or ApplicationIcon configured

### Established Patterns
- System.Drawing is already imported and available (used for SystemIcons.Application)
- CsWin32 package available for any Win32 interop if needed

### Integration Points
- `TrayIcon.cs:52-58`: NotifyIcon constructor — swap `SystemIcons.Application` for custom icon, update `Text` property
- `focus.csproj`: Add `<ApplicationIcon>` property and/or `<EmbeddedResource>` item
- Icon generation code needs a home — new file or build task in the project

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 13-tray-identity*
*Context gathered: 2026-03-04*
