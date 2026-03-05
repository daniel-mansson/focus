---
phase: 13-tray-identity
plan: 01
subsystem: tray-icon
tags: [icon, system-tray, embedded-resource, winforms]
dependency_graph:
  requires: []
  provides: [custom-focus-bracket-icon, tray-icon-identity]
  affects: [focus-daemon-visual-identity]
tech_stack:
  added: []
  patterns: [ico-binary-encoder, embedded-resource-dual-embed, standalone-generator-script]
key_files:
  created:
    - tools/generate-icon/Program.cs
    - tools/generate-icon/generate-icon.csproj
    - focus/focus.ico
  modified:
    - focus/focus.csproj
    - focus/Windows/Daemon/TrayIcon.cs
key_decisions:
  - Committed output approach for icon generation (standalone script, not pre-build target) to avoid build latency and file-lock risks
  - LogicalName metadata on EmbeddedResource eliminates namespace-prefix guessing for GetManifestResourceStream
  - SmoothingMode.None for pixel-perfect geometric shapes (no anti-alias gray fringe at small sizes)
  - PNG frames in ICO container (not BMP/DIB) — simpler encoding, full ARGB transparency, Windows Vista+ guaranteed
metrics:
  duration_minutes: 2
  tasks_completed: 2
  tasks_total: 2
  files_created: 3
  files_modified: 2
  completed_date: "2026-03-04"
---

# Phase 13 Plan 01: Tray Identity - Custom Icon and Tooltip Summary

**One-liner:** Code-generated focus-bracket ICO (16/20/24/32px PNG frames) embedded as both Win32 ApplicationIcon and .NET EmbeddedResource, with tray tooltip updated to "Focus — Navigation Daemon".

## What Was Built

A custom multi-size ICO icon was generated from drawing primitives and integrated as the daemon's visual identity in two ways:

1. **Icon generator** (`tools/generate-icon/`) — standalone C# console project that draws L-shaped corner brackets and a center dot at each DPI size (16, 20, 24, 32px) using `System.Drawing.Graphics` with `SmoothingMode.None`. Uses a hand-written `BinaryWriter` ICO encoder with PNG frames.

2. **Assembly embedding** — `focus.csproj` references `focus.ico` as both `<ApplicationIcon>` (Win32 PE resource for Explorer/.exe icon) and `<EmbeddedResource>` with `<LogicalName>focus.ico</LogicalName>` (for runtime `GetManifestResourceStream`).

3. **Runtime icon load** — `TrayIcon.cs` loads the icon via `Assembly.GetExecutingAssembly().GetManifestResourceStream("focus.ico")` and updates the tooltip from `"Focus Daemon"` to `"Focus \u2014 Navigation Daemon"` (em dash U+2014).

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Create icon generator and produce focus.ico | 7aa42e7 | tools/generate-icon/Program.cs, tools/generate-icon/generate-icon.csproj, focus/focus.ico |
| 2 | Embed icon in assembly and update tray icon + tooltip | db07a29 | focus/focus.csproj, focus/Windows/Daemon/TrayIcon.cs |

## Verification

- `dotnet build` succeeds: 0 errors, 1 pre-existing warning (WFAC010 in app.manifest — unrelated to this plan)
- `focus/focus.ico` exists with ICO header: type=1, count=4
- ICO frames: 16x16, 20x20, 24x24, 32x32 — all bpp=32 PNG frames
- `focus.csproj` contains `<ApplicationIcon>focus.ico</ApplicationIcon>`
- `focus.csproj` contains `<EmbeddedResource Include="focus.ico"><LogicalName>focus.ico</LogicalName></EmbeddedResource>`
- `TrayIcon.cs` uses `GetManifestResourceStream("focus.ico")` — no `SystemIcons.Application`
- `TrayIcon.cs` tooltip: `"Focus \u2014 Navigation Daemon"` (em dash)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed AppContext.BaseDirectory path resolution in generator**
- **Found during:** Task 1
- **Issue:** `AppContext.BaseDirectory` resolves to the binary output directory (`bin/Debug/net8.0-windows/`) when using `dotnet run`, causing the ICO to be written to `tools/focus/focus.ico` instead of `focus/focus.ico`.
- **Fix:** Changed to `Environment.CurrentDirectory` which is the working directory at `dotnet run` invocation time — correct path when run from `tools/generate-icon/`.
- **Files modified:** `tools/generate-icon/Program.cs`
- **Commit:** 7aa42e7 (fix included in same commit as generator creation)

## Self-Check: PASSED

Files confirmed present:
- tools/generate-icon/Program.cs: FOUND
- tools/generate-icon/generate-icon.csproj: FOUND
- focus/focus.ico: FOUND
- focus/focus.csproj: FOUND (ApplicationIcon + EmbeddedResource confirmed)
- focus/Windows/Daemon/TrayIcon.cs: FOUND (GetManifestResourceStream + Navigation Daemon confirmed)

Commits confirmed:
- 7aa42e7: feat(13-01): create icon generator and produce focus.ico
- db07a29: feat(13-01): embed icon in assembly and update tray icon + tooltip
