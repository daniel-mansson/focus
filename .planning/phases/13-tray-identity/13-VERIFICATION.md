---
phase: 13-tray-identity
verified: 2026-03-04T00:00:00Z
status: human_needed
score: 4/4 must-haves verified
human_verification:
  - test: "Run the daemon and observe the system tray icon"
    expected: "A white L-shaped corner-bracket icon with a center dot appears in the tray (not the generic Windows application icon)"
    why_human: "Cannot render the Windows system tray visually or inspect a live NotifyIcon programmatically"
  - test: "Hover the tray icon for 1-2 seconds"
    expected: "Tooltip reads exactly: Focus — Navigation Daemon (with em dash, not two hyphens)"
    why_human: "Tooltip display is a live UI behavior; cannot verify from static code that the OS renders it correctly"
  - test: "Inspect the built focus.exe in Windows Explorer (right-click > Properties > Details tab, or simply view the icon in File Explorer)"
    expected: "The .exe shows the custom focus-bracket icon, not the generic Windows application icon"
    why_human: "Win32 ApplicationIcon embedding is only visible via the OS icon-rendering pipeline; cannot verify from code inspection alone"
---

# Phase 13: Tray Identity Verification Report

**Phase Goal:** The daemon has a distinct, polished presence in the system tray with a custom icon and correct tooltip
**Verified:** 2026-03-04
**Status:** human_needed — all automated checks passed; three items require live runtime confirmation
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | The system tray icon displays a custom focus-bracket icon (not the generic Windows application icon) | ? HUMAN | Code loads custom icon via `GetManifestResourceStream("focus.ico")`; runtime appearance requires human confirmation |
| 2 | Hovering the tray icon shows "Focus — Navigation Daemon" as tooltip text | ? HUMAN | `TrayIcon.cs:63` sets `Text = "Focus \u2014 Navigation Daemon"` (em dash U+2014); tooltip rendering requires human confirmation |
| 3 | The .exe file in Explorer shows the custom focus-bracket icon (not the generic icon) | ? HUMAN | `focus.csproj:12` has `<ApplicationIcon>focus.ico</ApplicationIcon>`; Win32 PE embedding confirmed by csproj; visual appearance requires human confirmation |
| 4 | The .ico file is embedded in the assembly — no external file needed at runtime | ✓ VERIFIED | `focus.csproj:15-17` has `<EmbeddedResource Include="focus.ico"><LogicalName>focus.ico</LogicalName></EmbeddedResource>`; `GetManifestResourceStream` with throw-on-null guard at `TrayIcon.cs:55-56` |

**Score:** 4/4 truths have supporting implementation; 3/4 require human runtime confirmation for full verification

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `tools/generate-icon/Program.cs` | Icon generator drawing L-shaped corner brackets + center dot at 16/20/24/32px | ✓ VERIFIED | 107 lines (min_lines: 60). `DrawFocusIcon(int size)` draws 4 L-brackets + center dot with `SmoothingMode.None`. `WriteIco` encodes PNG frames using `BinaryWriter`. |
| `tools/generate-icon/generate-icon.csproj` | Standalone console project for icon generation | ✓ VERIFIED | 9 lines (min_lines: 5). `net8.0-windows`, `UseWindowsForms`, `OutputType=Exe`. No NuGet dependencies. |
| `focus/focus.ico` | Multi-size ICO file with 4 PNG frames (16, 20, 24, 32px) | ✓ VERIFIED | File exists (970 bytes). ICO header hex confirms: reserved=0x0000, type=0x0001 (ICO), count=0x0004. Directory entries confirm frames: 16x16, 20x20, 24x24, 32x32, all bpp=32. |
| `focus/focus.csproj` | ApplicationIcon + EmbeddedResource entries for focus.ico | ✓ VERIFIED | Line 12: `<ApplicationIcon>focus.ico</ApplicationIcon>`. Lines 15-17: `<EmbeddedResource Include="focus.ico"><LogicalName>focus.ico</LogicalName></EmbeddedResource>`. Both present. |
| `focus/Windows/Daemon/TrayIcon.cs` | Runtime icon load from embedded resource and updated tooltip | ✓ VERIFIED | Lines 54-56: `GetManifestResourceStream("focus.ico")` with null-guard throw. Line 57: `new Icon(iconStream)`. Line 61: `Icon = customIcon`. Line 63: `Text = "Focus \u2014 Navigation Daemon"`. No `SystemIcons.Application` reference remains. |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `tools/generate-icon/Program.cs` | `focus/focus.ico` | Generator writes ICO file to disk | ✓ VERIFIED | `Program.cs:22`: `Path.Combine(Environment.CurrentDirectory, "..", "..", "focus", "focus.ico")` — exact output path resolved correctly. Pattern `focus\.ico` found at lines 20, 22, 29. |
| `focus/focus.csproj` | `focus/focus.ico` | ApplicationIcon + EmbeddedResource referencing focus.ico | ✓ VERIFIED | `focus.csproj:12`: `<ApplicationIcon>focus.ico</ApplicationIcon>`. `focus.csproj:15`: `<EmbeddedResource Include="focus.ico">`. Pattern `focus\.ico` found at lines 12, 15, 16. |
| `focus/Windows/Daemon/TrayIcon.cs` | `focus/focus.ico` | GetManifestResourceStream loads embedded ICO at runtime | ✓ VERIFIED | `TrayIcon.cs:55`: `.GetManifestResourceStream("focus.ico")`. Matches the `<LogicalName>focus.ico</LogicalName>` in csproj — no namespace guessing needed. |

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| ICON-01 | 13-01-PLAN.md | Daemon displays a custom multi-size .ico icon in the system tray (16, 20, 24, 32px) | ? HUMAN | ICO file contains 4 frames (16, 20, 24, 32px) verified from binary. Runtime tray display requires human confirmation. |
| ICON-02 | 13-01-PLAN.md | Tray icon tooltip shows "Focus — Navigation Daemon" on hover | ? HUMAN | `TrayIcon.cs:63` sets `Text = "Focus \u2014 Navigation Daemon"`. Hover tooltip rendering requires human confirmation. |
| ICON-03 | 13-01-PLAN.md | Custom .ico is embedded as assembly resource and replaceable by swapping the file | ✓ VERIFIED | EmbeddedResource in csproj confirmed. `GetManifestResourceStream` confirmed. Generator at `tools/generate-icon/` enables replacement by updating `Program.cs` and re-running. |

No orphaned requirements — all three IDs declared in plan and accounted for.

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| — | — | — | — | None found |

No TODO, FIXME, placeholder, `return null`, `return {}`, or stub patterns found in any of the five modified files.

---

### Human Verification Required

#### 1. Custom icon visible in system tray

**Test:** Build and run the daemon (`dotnet run` or launch the built `.exe`). Observe the system tray notification area.
**Expected:** A white icon with four L-shaped corner brackets and a center dot appears — visually distinct from the generic grey Windows application icon.
**Why human:** The system tray is a live Windows UI element. Code correctness (loading from embedded resource) is verified, but the OS rendering the correct icon cannot be confirmed programmatically.

#### 2. Tooltip text on hover

**Test:** With the daemon running, hover the mouse over the tray icon for 1-2 seconds.
**Expected:** Tooltip balloon or tooltip text reads exactly: `Focus — Navigation Daemon` — with an em dash (—), not two hyphens (--).
**Why human:** `NotifyIcon.Text` is set correctly in code (`\u2014` U+2014 em dash), but tooltip rendering by the OS shell cannot be verified without running the application.

#### 3. Custom .exe icon visible in File Explorer

**Test:** Navigate to the build output directory (e.g., `focus/bin/Debug/net8.0-windows/`) in Windows File Explorer and observe the icon on `focus.exe`.
**Expected:** The `.exe` shows the focus-bracket icon (white L-brackets + center dot), not the generic Windows application icon.
**Why human:** `<ApplicationIcon>focus.ico</ApplicationIcon>` in csproj instructs the compiler to embed the icon as a Win32 PE resource. Whether Explorer renders it correctly requires visual inspection.

---

### Gaps Summary

No gaps found. All five artifacts are present, substantive, and correctly wired to each other. All three key links are confirmed in the source. The ICO binary structure is valid (type=1, count=4, frames 16/20/24/32px at 32bpp). No anti-patterns or stub code detected.

Three observable truths involve live runtime behavior (tray icon appearance, tooltip rendering, Explorer .exe icon) that cannot be confirmed from static code analysis. These are flagged for human verification — they are not gaps in the implementation, but confirmations of correct behavior at runtime.

---

## Verification Notes

- Commits `7aa42e7` and `db07a29` confirmed present in git log.
- `SystemIcons.Application` is fully removed from `TrayIcon.cs` — no fallback to the generic icon.
- `LogicalName` metadata on `EmbeddedResource` eliminates namespace-prefix ambiguity for `GetManifestResourceStream("focus.ico")`.
- ICO file size (970 bytes) is plausible for four small PNG frames (16/20/24/32px geometric icons with mostly transparent pixels compress very well).
- Generator uses `SmoothingMode.None` — correct for pixel-perfect geometric icons, consistent with research findings.
- `BinaryWriter(output, Encoding.UTF8, leaveOpen: true)` — pitfall 3 correctly avoided.
- No new NuGet packages were added (generate-icon has zero `PackageReference` entries; focus.csproj has unchanged package set).

---

_Verified: 2026-03-04_
_Verifier: Claude (gsd-verifier)_
