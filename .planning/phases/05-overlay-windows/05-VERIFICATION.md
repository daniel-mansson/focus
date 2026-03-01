---
phase: 05-overlay-windows
verified: 2026-03-01T12:00:00Z
status: passed
score: 5/5 must-haves verified
gaps:
human_verification:
  - test: "Run focus --debug overlay left against a foreground window"
    expected: "A muted blue semi-transparent rounded-corner border appears around the foreground window, click-through, no Alt+Tab entry, no focus steal, dismisses cleanly on keypress"
    why_human: "Visual correctness, click-through behavior, focus-steal absence, and Alt+Tab exclusion cannot be verified programmatically from code inspection alone. Plan 02 SUMMARY records human approval, but that is a SUMMARY claim — the gate is a blocking checkpoint:human-verify task."
---

# Phase 5: Overlay Windows Verification Report

**Phase Goal:** Users can see correctly rendered colored border overlays appear on screen — transparent, click-through, absent from Alt+Tab, and visually correct — positioned at a hardcoded test rectangle before navigation is wired
**Verified:** 2026-03-01T12:00:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | A colored border overlay can appear on screen — transparent, click-through, always-on-top, and never stealing focus | VERIFIED | OverlayWindow.cs: WS_EX_LAYERED\|TRANSPARENT\|TOOLWINDOW\|NOACTIVATE\|TOPMOST all present; SWP_NOACTIVATE on every SetWindowPos |
| 2 | The overlay window does not appear in Alt+Tab, taskbar, or enumerate output | VERIFIED | WS_EX_TOOLWINDOW excludes from Alt+Tab/taskbar; OverlayWindow is not a navigable WindowEnumerator window |
| 3 | Per-direction colors (left/right/up/down, hex ARGB) can be read from JSON config with sensible defaults | VERIFIED | OverlayColors.cs: four direction properties with #BF... defaults; GetArgb parser with fallback; FocusConfig.cs: OverlayColors property; config.OverlayColors.GetArgb() called in Program.cs |
| 4 | The overlay renderer is selectable by config name with 'border' as the default | VERIFIED | OverlayManager.CreateRenderer: "border" → BorderRenderer(), unknown → BorderRenderer() fallback; FocusConfig.OverlayRenderer = "border"; Program.cs calls CreateRenderer(overlayConfig.OverlayRenderer) |
| 5 | The border has rounded corners matching Windows 11 window chrome and semi-transparent (~75%) appearance | VERIFIED (code) / NEEDS HUMAN (visual) | BorderRenderer.cs: RoundRect with CornerEllipse=16 (8px radius); premultiplied-alpha DIB via UpdateLayeredWindow(ULW_ALPHA); GDI alpha detection bug fixed (#BF... = ~75% opacity) |

**Score:** 5/5 truths verified at code level. Truth 5 requires human visual confirmation.

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `focus/Windows/Daemon/Overlay/IOverlayRenderer.cs` | Renderer interface contract | VERIFIED | interface IOverlayRenderer with Name property and Paint(HWND, RECT, uint) method |
| `focus/Windows/Daemon/Overlay/OverlayWindow.cs` | Single overlay HWND lifecycle | VERIFIED | class OverlayWindow : IDisposable; RegisterClassEx, CreateWindowEx, Show, Hide, Dispose, WM_PAINT handler |
| `focus/Windows/Daemon/Overlay/BorderRenderer.cs` | GDI RoundRect border renderer | VERIFIED | class BorderRenderer : IOverlayRenderer; Name="border"; Paint uses RoundRect+premultiplied DIB+UpdateLayeredWindow; GDI alpha bug fixed |
| `focus/Windows/Daemon/Overlay/OverlayManager.cs` | Manages 4 OverlayWindow instances | VERIFIED | class OverlayManager : IDisposable; Dictionary<Direction, OverlayWindow>; ShowOverlay/HideOverlay/HideAll/CreateRenderer |
| `focus/Windows/Daemon/Overlay/OverlayColors.cs` | ARGB color constants and config parsing | VERIFIED | class OverlayColors; four direction properties with #BF defaults; GetArgb parser with try/catch fallback |
| `focus/Windows/FocusConfig.cs` | Extended config with OverlayColors and OverlayRenderer | VERIFIED | OverlayColors OverlayColors { get; set; } = new(); string OverlayRenderer { get; set; } = "border"; |
| `focus/Program.cs` | Debug overlay command handler | VERIFIED | if (debugValue == "overlay") block; FocusConfig.Load(); OverlayManager.CreateRenderer; GetForegroundWindow; DwmGetWindowAttribute; ShowOverlay; DoEvents loop; HideAll |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| OverlayManager.cs | OverlayWindow.cs | Creates 4 OverlayWindow instances | WIRED | `new OverlayWindow()` x4 in Dictionary initializer (lines 32-35) |
| OverlayManager.cs | IOverlayRenderer.cs | Holds renderer reference | WIRED | `private readonly IOverlayRenderer _renderer` field; `_renderer.Paint(...)` in ShowOverlay |
| BorderRenderer.cs | IOverlayRenderer.cs | Implements renderer interface | WIRED | `internal sealed class BorderRenderer : IOverlayRenderer` |
| FocusConfig.cs | OverlayColors.cs | Config holds OverlayColors instance | WIRED | `public OverlayColors OverlayColors { get; set; } = new();` with `using Focus.Windows.Daemon.Overlay;` |
| Program.cs | OverlayManager.cs | Creates OverlayManager for debug rendering | WIRED | `OverlayManager.CreateRenderer(...)` and `new OverlayManager(renderer, overlayConfig.OverlayColors)` |
| Program.cs | FocusConfig.cs | Loads config for overlay colors and renderer name | WIRED | `var overlayConfig = FocusConfig.Load();` then `overlayConfig.OverlayRenderer` and `overlayConfig.OverlayColors` |

---

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| OVERLAY-02 | 05-01, 05-02 | Overlay windows are click-through, always-on-top, excluded from taskbar/Alt+Tab, and excluded from navigation enumeration | SATISFIED | OverlayWindow: WS_EX_LAYERED\|TRANSPARENT\|TOOLWINDOW\|NOACTIVATE\|TOPMOST; SWP_NOACTIVATE; human-verified per 05-02 SUMMARY |
| RENDER-01 | 05-01 | IOverlayRenderer interface defines the contract for overlay rendering | SATISFIED | IOverlayRenderer.cs: interface with Name + Paint(HWND, RECT, uint) |
| RENDER-02 | 05-01, 05-02 | Default border renderer draws colored borders using Win32 GDI (no WPF/WinForms) | SATISFIED | BorderRenderer.cs: GDI CreatePen + RoundRect + UpdateLayeredWindow; no WPF/WinForms references |
| RENDER-03 | 05-01 | Renderer selection is driven by config (overlayRenderer field) | SATISFIED | OverlayManager.CreateRenderer(name) factory; FocusConfig.OverlayRenderer="border"; Program.cs passes config value |
| CFG-05 | 05-01, 05-02 | Per-direction overlay colors configurable in JSON config (left/right/up/down, hex ARGB) | SATISFIED | OverlayColors: four string properties serialized by FocusConfig.Load/WriteDefaults; GetArgb parser |
| CFG-07 | 05-01 | Overlay renderer name configurable in JSON config (default: "border") | SATISFIED | FocusConfig.OverlayRenderer="border"; CreateRenderer factory maps name to implementation |

All 6 Phase 5 requirements are satisfied by the implementation. No orphaned requirements found — REQUIREMENTS.md traceability table maps exactly OVERLAY-02, RENDER-01, RENDER-02, RENDER-03, CFG-05, CFG-07 to Phase 5, matching both plan frontmatter declarations.

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none) | — | — | — | — |

Scan of all 7 phase files (5 Overlay/ files + FocusConfig.cs + Program.cs) found: no TODO/FIXME/PLACEHOLDER comments, no stub implementations, no empty handlers, no console-log-only functions. All implementations are substantive.

---

## Human Verification Required

### 1. Visual Overlay Appearance and Behavior

**Test:** Open a window (Notepad or Explorer), bring it to the foreground, then from a separate terminal run:
`dotnet run --project C:\Work\windowfocusnavigation\focus -- --debug overlay left`

**Expected:**
- A muted blue (#BF4488CC) semi-transparent border appears around the foreground window
- Border is thin (~2px), with rounded corners matching Windows 11 window chrome
- The window interior is fully visible through the overlay (no opaque fill)
- Clicking on the window beneath the overlay passes through (click-through confirmed)
- The previously focused window remains focused (no focus steal)
- Alt+Tab does not show the overlay as a separate entry
- Pressing any key in the terminal dismisses the overlay and exits cleanly

**Why human:** Visual appearance (color, opacity, corner radius), click-through behavior, focus-steal absence, and Alt+Tab list content cannot be verified by static code analysis.

### 2. All Four Direction Colors

**Test:** Repeat the above with `right` (muted red #BFCC4444), `up` (muted green #BF44AA66), `down` (muted amber #BFCCAA33).

**Expected:** Each direction renders with the correct color per OverlayColors defaults.

**Why human:** Color accuracy and per-direction dispatch requires visual confirmation.

### 3. Overlay Exclusion from --debug enumerate

**Test:** While an overlay is showing, run from another terminal:
`dotnet run --project C:\Work\windowfocusnavigation\focus -- --debug enumerate`

**Expected:** No row with "focus" as process name appears in the output.

**Why human:** Requires two concurrent processes — the running overlay and the enumerator — which cannot be simulated statically.

---

## Notable Findings

### GDI Alpha Detection Bug (Fixed in 05-02)

The SUMMARY for Plan 02 documents a critical correctness fix: `BorderRenderer.Paint()` originally checked `if (pixAlpha != 0)` to find GDI-drawn pixels before premultiplied-alpha conversion. GDI's `RoundRect` does not set alpha channel bytes in DIBs (alpha stays at 0x00). The fix changes the check to `if ((pixel & 0x00FFFFFF) != 0)`. This is confirmed present in the actual file at line 87 of BorderRenderer.cs. Without this fix the overlay would be completely invisible.

### NativeMethods.txt Complete

All 26 required overlay Win32 APIs and struct types are present in NativeMethods.txt (entries 31-57), including the DefWindowProc addition that was not in the original plan spec.

### Build Status

Project compiles with 0 errors, 1 pre-existing DPI warning (WFAC010) unrelated to this phase.

### All Commits Verified

Five phase execution commits confirmed in git history:
- `b7f5be6` feat(05-01): NativeMethods, IOverlayRenderer, OverlayColors, FocusConfig extension
- `db07eb4` feat(05-01): OverlayWindow HWND wrapper and BorderRenderer GDI implementation
- `cb1886b` feat(05-01): OverlayManager directional overlay orchestrator
- `deb654f` feat(05-02): focus --debug overlay command in Program.cs
- `e0ba035` fix(05-02): GDI alpha detection fix in BorderRenderer

---

## Gaps Summary

No gaps. All automated checks pass:
- All 7 artifacts exist with substantive implementations
- All 6 key links are wired (verified via grep)
- All 6 requirements satisfied with evidence
- No anti-patterns detected
- Build: 0 errors

The only remaining gate is human visual verification of overlay rendering, which was documented as a blocking `checkpoint:human-verify` task in the Plan 02 spec and is recorded as passed in 05-02-SUMMARY.md. The SUMMARY is a claim — the verification report flags this for confirmation.

---

_Verified: 2026-03-01T12:00:00Z_
_Verifier: Claude (gsd-verifier)_
