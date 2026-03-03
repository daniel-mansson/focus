---
phase: 11-move-and-resize-single-monitor
verified: 2026-03-02T22:00:00Z
status: human_needed
score: 14/14 must-haves verified
re_verification: true
previous_status: passed
previous_score: 9/9
gaps_closed:
  - "Grow-down expands window downward (bottom edge moves down, never up)"
  - "Grow in any direction never shrinks the window when misaligned"
  - "Shrink-up moves the bottom edge upward (not the top edge downward)"
  - "All four shrink directions contract the opposite edge inward"
  - "Shrink at OS minimum size is a silent no-op (no position change)"
  - "Shift-first then CapsLock activates overlay and modes correctly"
  - "Overlay border redraws around the window's new position after every move step"
  - "Overlay border redraws around the window's new bounds after every grow step"
  - "Overlay border redraws around the window's new bounds after every shrink step"
  - "Overlay does not refresh if CapsLock was released before the dispatch completes"
  - "Navigate-target outlines (non-active window overlays) are hidden during Move/Grow/Shrink modes"
gaps_remaining: []
regressions: []
human_verification:
  - test: "Move foreground window with CAPS+TAB+direction"
    expected: "Window moves one grid step per keypress in the pressed direction; first press on a misaligned window snaps to nearest grid line; boundary clamp stops window at work area edge"
    why_human: "Requires live daemon, physical keypresses, and observable window position change"
  - test: "Grow edge with CAPS+LSHIFT+direction, including Shift-first+CapsLock activation"
    expected: "Grow-down expands the bottom edge downward (never upward); Grow-up expands top edge upward; boundary clamp at work area edge; holding LSHIFT before pressing CapsLock activates grow mode"
    why_human: "Directional snap correctness and Shift-first activation require live interaction"
  - test: "Shrink edge with CAPS+LCTRL+direction — verify direction semantics"
    expected: "Shrink-up contracts the bottom edge upward; Shrink-down contracts the top edge downward; Shrink-left contracts the right edge leftward; Shrink-right contracts the left edge rightward"
    why_human: "Opposite-edge mapping semantics require live window observation to confirm the correct edge moves"
  - test: "Shrink at OS minimum size"
    expected: "When SetWindowPos would clamp size at OS minimum, the window does not move — completely silent no-op"
    why_human: "Requires shrinking a window to OS ptMinTrackSize and observing no position drift"
  - test: "Overlay redraw after Move/Grow/Shrink"
    expected: "Active window border follows the window's new position immediately after each step; no navigate-target outlines are visible during Move/Grow/Shrink"
    why_human: "Requires live session to observe overlay tracking and navigate-target suppression"
  - test: "Maximized window guard"
    expected: "No error dialog, no restore, window remains maximized"
    why_human: "IsZoomed guard is correct in code; silent no-op requires a live maximized window to confirm"
  - test: "Elevated window guard with --verbose"
    expected: "No crash or dialog; single stderr line 'SetWindowPos failed (elevated window?)' if verbose; window position unchanged"
    why_human: "Requires running an elevated target process and observing daemon stderr"
---

# Phase 11: Move and Resize Single Monitor — Verification Report

**Phase Goal:** Users can move the foreground window by grid steps in any direction and grow or shrink any window edge by grid steps, with correct coordinate handling, snap-first behavior, boundary clamping, and guards against maximized and elevated windows
**Verified:** 2026-03-02
**Status:** HUMAN NEEDED (all automated checks passed; runtime behavior requires live session)
**Re-verification:** Yes — after three plan executions (11-01 initial implementation, 11-02 bug fixes, 11-03 overlay refresh)

---

## Re-Verification Summary

The previous VERIFICATION.md (status: passed, 9/9) covered Plan 01 only. Two gap-closure plans were subsequently executed:

- **Plan 02** (`7ea1554`, `4c0bbe6`) — Fixed grow/shrink directional snap, ComputeShrink edge inversion, OS min-size guard, Shift-first+CapsLock activation
- **Plan 03** (`54597ee`) — Added overlay refresh after MoveOrResize; suppressed navigate-target outlines during Move/Grow/Shrink

This re-verification covers all three plans' must-haves against the actual codebase.

**Gaps closed:** 11/11 (all UAT-diagnosed gaps addressed in code)
**Gaps remaining:** 0
**Regressions:** 0

---

## Goal Achievement

### Observable Truths — Combined from All Three Plans

| # | Truth | Source Plan | Status | Evidence |
|---|-------|-------------|--------|----------|
| 1 | CAPS+TAB+direction moves the foreground window one grid step in the pressed direction | 11-01 | VERIFIED | `ComputeMove` in WindowManagerService.cs: snap-first via `IsAligned` + `NearestGridLine`, then `Math.Clamp` boundary; dispatched from `OverlayOrchestrator.OnDirectionKeyDown` with `WindowMode.Move` |
| 2 | Repeated direction presses while CAPS+TAB held produce consecutive grid steps | 11-01 | VERIFIED | CapsLockMonitor repeat suppression pre-existing from Phase 10; each physical keydown fires exactly one `OnDirectionKeyDown` |
| 3 | A misaligned window snaps to the nearest grid line on the first move, then steps normally | 11-01 | VERIFIED | `GridCalculator.IsAligned` check on `vis.left`/`vis.top`; if not aligned, `NearestGridLine` (bidirectional); subsequent press finds window aligned and steps by `sign * stepX/Y` |
| 4 | Moving toward the monitor edge stops at the work area boundary | 11-01 | VERIFIED | `Math.Clamp(newVisLeft, work.left, work.right - visW)` (line 135) and `Math.Clamp(newVisTop, work.top, work.bottom - visH)` (line 145); uses `mi.rcWork` |
| 5 | Grow-down expands window downward (bottom edge moves down, never up) | 11-02 | VERIFIED | `ComputeGrow` case "down": `NearestGridLineCeiling(vis.bottom, work.top, stepY)` (line 197) — ceiling snaps the bottom edge downward (outward), never upward |
| 6 | Grow in any direction never shrinks the window when misaligned | 11-02 | VERIFIED | Ceiling for right/down outward movement, Floor for left/up outward movement; ceiling on a bottom edge below a grid line snaps it to the next grid line DOWN, not up |
| 7 | Shrink-up moves the bottom edge upward (not the top edge downward) | 11-02 | VERIFIED | `ComputeShrink` case "up" (lines 239-246): `newVisBottom` changes, `newVisTop` stays fixed; comment: "BOTTOM edge moves upward (inward)" |
| 8 | All four shrink directions contract the opposite edge inward | 11-02 | VERIFIED | up→bottom, down→top, left→right, right→left; each case modifies only one variable (the opposite edge); confirmed lines 239-273 |
| 9 | Shrink at OS minimum size is a silent no-op (no position change) | 11-02 | VERIFIED | Post-computation guard (line 280): `if (newVisW >= visW && newVisH >= visH) return win;` catches SetWindowPos OS-clamp scenario where grid math doesn't reduce visible size |
| 10 | Shift-first then CapsLock activates overlay and modes correctly | 11-02 | VERIFIED | VK_SHIFT filter removed from KeyboardHookHandler CapsLock section (lines 186-194); only Alt and Ctrl filters remain; comment updated to document Shift+CapsLock as allowed |
| 11 | Overlay border redraws around the window's new position after every move step | 11-03 | VERIFIED | Block lambda in `OnDirectionKeyDown` (lines 139-144): after `MoveOrResize`, calls `RefreshForegroundOverlayOnly()` if `_capsLockHeld` |
| 12 | Overlay does not refresh if CapsLock was released before dispatch completes | 11-03 | VERIFIED | Guard `if (_capsLockHeld)` on line 142 inside `_staDispatcher.Invoke` lambda prevents stale overlay flash on release race |
| 13 | Navigate-target outlines are hidden during Move/Grow/Shrink — only active window outline visible | 11-03 | VERIFIED | `RefreshForegroundOverlayOnly` (lines 302-320): calls `HideAll()` then `ShowForegroundOverlay(fgBounds, ForegroundBorderColor)` only — no navigate-target overlay calls |
| 14 | Maximized and elevated windows produce no visible error and no window change | 11-01 | VERIFIED | `if (PInvoke.IsZoomed(fgHwnd)) return;` (line 33); `SetWindowPos` result checked — if false and verbose, logs to stderr only (lines 92-93) |

**Score:** 14/14 truths verified

---

### Required Artifacts — All Three Plans

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `focus/Windows/Daemon/WindowManagerService.cs` | MoveOrResize, ComputeMove, ComputeGrow (directional snap), ComputeShrink (inverted edges + no-op guard) | VERIFIED | 291 lines; all five methods present; no stubs; directional snap (Ceiling/Floor) in ComputeGrow/ComputeShrink; post-computation guard at line 280 |
| `focus/NativeMethods.txt` | GetWindowRect and IsZoomed CsWin32 bindings | VERIFIED | `GetWindowRect` at line 64, `IsZoomed` at line 65 |
| `focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs` | Mode dispatch to WindowManagerService; RefreshForegroundOverlayOnly after MoveOrResize | VERIFIED | Line 141: `WindowManagerService.MoveOrResize(direction, mode, _verbose)` inside block lambda; line 143: `RefreshForegroundOverlayOnly()` guarded by `_capsLockHeld`; private method at lines 302-320 |
| `focus/Windows/GridCalculator.cs` | NearestGridLineFloor and NearestGridLineCeiling directional snap methods | VERIFIED | `NearestGridLineFloor` (lines 39-45, `Math.Floor`); `NearestGridLineCeiling` (lines 53-58, `Math.Ceiling`); original `NearestGridLine` (Math.Round) unchanged |
| `focus/Windows/Daemon/KeyboardHookHandler.cs` | Shift filter removed from CapsLock modifier check | VERIFIED | Lines 186-194: only Alt check (`LLKHF_ALTDOWN`) and Ctrl check (`VK_CONTROL`) remain; no `VK_SHIFT`/`GetKeyState.*SHIFT` filter |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `OverlayOrchestrator.cs` | `WindowManagerService.cs` | Block lambda in OnDirectionKeyDown calls MoveOrResize | WIRED | Line 141: `WindowManagerService.MoveOrResize(direction, mode, _verbose)` via `_staDispatcher.Invoke` block lambda |
| `OverlayOrchestrator.cs` | `RefreshForegroundOverlayOnly` | Called after MoveOrResize, guarded by _capsLockHeld | WIRED | Line 142-143: `if (_capsLockHeld) RefreshForegroundOverlayOnly()` inside same Invoke lambda |
| `WindowManagerService.cs` | `GridCalculator.cs` | ComputeGrow uses NearestGridLineCeiling for right/down, NearestGridLineFloor for left/up | WIRED | Lines 181, 189, 197, 205 in ComputeGrow; lines 244, 253, 262, 271 in ComputeShrink — 8 directional snap call sites |
| `WindowManagerService.cs` | Win32 SetWindowPos | PInvoke.SetWindowPos with SWP_NOZORDER, SWP_NOACTIVATE, SWP_NOOWNERZORDER | WIRED | Lines 84-90: flags correct; result checked for elevated guard (lines 92-93) |
| `KeyboardHookHandler.cs` | CapsLockMonitor | CapsLock keydown no longer filtered when Shift is held | WIRED | Lines 186-194: Shift filter removed; `_capsLockHeld = capsIsKeyDown` (line 198) fires for Shift+CapsLock combos |

---

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|----------------|-------------|--------|----------|
| MOVE-01 | 11-01, 11-03 | Move foreground window one grid step in any direction | SATISFIED | `ComputeMove` with snap-first + `Math.Clamp`; overlay refresh via `RefreshForegroundOverlayOnly` after each step |
| MOVE-02 | 11-01 | Consecutive grid steps while CAPS+TAB held | SATISFIED | Handled by CapsLockMonitor repeat suppression from Phase 10; each keydown fires one `OnDirectionKeyDown` |
| MOVE-03 | 11-01 | Window position clamped to monitor work area boundaries | SATISFIED | `Math.Clamp` on both axes using `mi.rcWork` in `ComputeMove` |
| SIZE-01 | 11-01, 11-02, 11-03 | Grow a window edge outward by one grid step | SATISFIED | `ComputeGrow` with directional snap (NearestGridLineCeiling for right/down, NearestGridLineFloor for left/up); overlay refresh after each grow step |
| SIZE-02 | 11-01, 11-02, 11-03 | Shrink a window edge inward by one grid step | SATISFIED | `ComputeShrink` with corrected edge mapping (opposite-edge semantics) and directional snap; overlay refresh after each shrink step |
| SIZE-03 | 11-01, 11-02 | Shrink stops at minimum window size | SATISFIED | Pre-check `if (visW <= stepX) return win` per case; post-computation guard `if (newVisW >= visW && newVisH >= visH) return win` for OS min-track size |
| SIZE-04 | 11-01 | Grow stops at monitor work area boundary | SATISFIED | `Math.Min/Max` clamp on the moving edge against `work.right/left/bottom/top` in `ComputeGrow` |
| GRID-03 | 11-02 (claimed); Phase 10 (traceability table) | Misaligned windows snap to nearest grid line on first operation | SATISFIED | Phase 10 established bidirectional `NearestGridLine` for navigate; Plan 11-02 extended to directional variants (`NearestGridLineFloor`/`Ceiling`) for grow/shrink correctness. GRID-03 is fully satisfied across both phases. |

**Requirements coverage: 8/8** — MOVE-01 through MOVE-03, SIZE-01 through SIZE-04, and GRID-03 are all satisfied.

**Traceability note:** REQUIREMENTS.md traceability table maps GRID-03 to Phase 10. Plan 11-02 also claims GRID-03 because it corrected snap direction for grow/shrink operations. This is a legitimate extended claim — the Phase 10 implementation was incomplete for directional snap in grow/shrink contexts. No orphaned requirements: the REQUIREMENTS.md traceability table maps MOVE-01 through SIZE-04 (7 requirements) to Phase 11; GRID-03 is shared between Phase 10 and Phase 11's gap closure work. All are satisfied.

---

### Commit Verification

All five implementation commits are present in the repository:

| Commit | Plan | Description |
|--------|------|-------------|
| `ebc546d` | 11-01 Task 1 | Create WindowManagerService with Move operation and wire OverlayOrchestrator |
| `130e143` | 11-01 Task 2 | Implement ComputeGrow and ComputeShrink resize operations |
| `7ea1554` | 11-02 Task 1 | Add directional snap to GridCalculator and fix ComputeGrow |
| `4c0bbe6` | 11-02 Task 2 | Fix ComputeShrink edge inversion, add no-op guard, remove Shift filter |
| `54597ee` | 11-03 Task 1 | Add overlay refresh after MoveOrResize in OverlayOrchestrator |

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None | - | - | - | No anti-patterns detected |

Scan result: No TODO/FIXME/HACK/PLACEHOLDER comments in any Phase 11 file. No `return null` or empty stubs. No `console.log`-only handlers. No unimplemented methods. Former stubs (`ComputeGrow`/`ComputeShrink` returning unchanged rect from Task 1 of Plan 01) were fully replaced in Task 2 of Plan 01 and further corrected in Plan 02 — final codebase has zero stubs.

---

### Human Verification Required

The following items cannot be verified programmatically. All code-level checks pass; these are runtime behavior observations.

#### 1. Move Window (CAPS+TAB+direction)

**Test:** Hold CAPS+TAB, press each arrow direction while a normal (non-maximized, non-elevated) window is foreground.
**Expected:** Window moves one grid step per keypress in the pressed direction. First press on a misaligned window snaps to the nearest grid line instead of stepping. Boundary: window stops at the monitor work area edge (not behind taskbar).
**Why human:** Requires live daemon, physical keypresses, observable window position change.

#### 2. Grow Edge — Including Shift-First Activation (CAPS+LSHIFT+direction)

**Test:** (a) Hold LSHIFT first, then press CapsLock to activate overlay in grow mode. Then press each arrow direction. (b) Test grow-down specifically on a misaligned window.
**Expected:** (a) Shift-first+CapsLock activates the overlay correctly. (b) Grow-down expands the bottom edge downward — it does not snap the bottom edge upward. Each direction expands the edge in that direction; opposite edge does not move. Boundary: edge stops at work area boundary.
**Why human:** Shift-first+CapsLock activation and grow direction semantics require live interaction to confirm correct behavior feel.

#### 3. Shrink Edge — Verify Direction Semantics (CAPS+LCTRL+direction)

**Test:** Hold CAPS+LCTRL, press each arrow key and observe which edge moves.
**Expected:** Shrink-up contracts the bottom edge upward (height shrinks, top stays fixed). Shrink-down contracts the top edge downward (height shrinks, bottom stays fixed). Shrink-left contracts the right edge leftward (width shrinks, left stays fixed). Shrink-right contracts the left edge rightward (width shrinks, right stays fixed).
**Why human:** Opposite-edge mapping requires live window observation to confirm the correct edge moves for each direction key.

#### 4. Shrink at OS Minimum Size

**Test:** Shrink a window until it reaches OS minimum size. Continue pressing shrink in the same direction.
**Expected:** Once at OS minimum, further presses are silent no-ops — the window does not move and does not change size.
**Why human:** Requires shrinking to OS `ptMinTrackSize` and observing zero position drift — verifying the post-computation guard fires correctly in a live session.

#### 5. Overlay Redraw After Move/Grow/Shrink

**Test:** While performing Move, Grow, or Shrink operations, observe the overlay border behavior.
**Expected:** (a) Active window border immediately follows the window's new position after each step — no lag, no stale position. (b) No navigate-target outlines (surrounding window indicators) are visible during Move/Grow/Shrink modes — only the active window border is shown.
**Why human:** Overlay tracking and navigate-target suppression require live session observation.

#### 6. Maximized Window Guard

**Test:** Maximize a window, then attempt CAPS+TAB+direction (or CAPS+LSHIFT+direction).
**Expected:** Window remains maximized. No error dialog. No restore-then-move. Completely silent.
**Why human:** IsZoomed guard is correct in code; needs live maximized window to confirm silent no-op.

#### 7. Elevated Window Guard (with --verbose)

**Test:** With `--verbose` flag, attempt to move an elevated process window (e.g., Task Manager running as admin).
**Expected:** No crash or dialog. Single stderr line: "SetWindowPos failed (elevated window?)". Window position unchanged.
**Why human:** Requires an elevated target process and observation of daemon stderr.

---

### Gaps Summary

No gaps remain. All code-level must-haves from Plans 01, 02, and 03 are verified in the actual codebase:

- **Plan 01 truths (4):** Move, Grow, Shrink, Guards — all verified
- **Plan 02 truths (6):** Directional snap correctness, Shrink edge inversion fix, OS min-size no-op guard, Shift+CapsLock — all verified
- **Plan 03 truths (5):** Overlay refresh after move/resize, _capsLockHeld guard, navigate-target suppression — all verified

Seven runtime behavior items are flagged for human verification because they require live interaction with a running daemon — these are observable behavior questions, not code correctness questions.

---

_Verified: 2026-03-02_
_Verifier: Claude (gsd-verifier)_
