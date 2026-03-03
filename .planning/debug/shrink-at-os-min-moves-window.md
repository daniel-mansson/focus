---
status: investigating
trigger: "Shrink at minimum OS size moves window instead of no-op"
created: 2026-03-02T00:00:00Z
updated: 2026-03-02T00:00:00Z
symptoms_prefilled: true
goal: find_root_cause_only
---

## Current Focus

hypothesis: "The minimum-size guard compares visW against stepX (grid step), but the OS-enforced minimum can be larger than one grid step. When visW > stepX (guard passes) but the computed new size < OS minimum, SetWindowPos enforces the size but still applies the new position offset, moving the window."
test: "Trace ComputeShrink case=left with a window at OS minimum size"
expecting: "Guard passes because visW > stepX, new rect is computed with left edge moved inward, SetWindowPos receives (newLeft, oldTop, newWidth, oldHeight) where newWidth < OS minimum; Windows clamps width but honors the new left position"
next_action: "COMPLETE - root cause confirmed by code trace"

## Symptoms

expected: "When a window is at its OS minimum size (larger than one grid step), pressing shrink from the left side should be a no-op -- window position and size unchanged"
actual: "Window moves rightward (position changes) even though size stays the same (clamped by OS)"
errors: "None reported -- silent incorrect behavior"
reproduction: "Resize a window to its OS minimum (e.g. Notepad, Calculator). Press CAPS+LCTRL+Left (shrink from left). Window shifts right."
started: "Phase 11 implementation (2026-03-02)"

## Eliminated

- hypothesis: "Guard is missing entirely"
  evidence: "Code at line 250 shows: if (visW <= stepX) return win; -- guard exists"
  timestamp: 2026-03-02T00:00:00Z

- hypothesis: "Guard uses wrong rect (winRect vs visRect)"
  evidence: "visW is computed from vis (DWMWA_EXTENDED_FRAME_BOUNDS) which is the visible/correct rect for size comparisons"
  timestamp: 2026-03-02T00:00:00Z

## Evidence

- timestamp: 2026-03-02T00:00:00Z
  checked: "ComputeShrink case=left, lines 249-255 of WindowManagerService.cs"
  found: |
    if (visW <= stepX) return win;   // guard: only triggers when visW <= 1 grid step
    newVisLeft = IsAligned ? vis.left + stepX : NearestGridLine(vis.left, work.left, stepX);
    newVisLeft = Math.Min(newVisLeft, newVisRight - stepX); // clamp: newVisLeft can be at most (right - stepX)
  implication: "Guard fires only when visW <= stepX. If OS minimum > stepX, visW can be > stepX yet the new computed size (visW - stepX) is still below the OS minimum. Guard does NOT catch this case."

- timestamp: 2026-03-02T00:00:00Z
  checked: "SetWindowPos behavior when requested size < OS minimum (per research docs and RESEARCH.md Pitfall 6 / Pattern 2 comment)"
  found: |
    RESEARCH.md states: 'STATE.md notes that SetWindowPos auto-clamps to WM_GETMINMAXINFO.ptMinTrackSize.'
    SetWindowPos with a size smaller than ptMinTrackSize: Windows applies the position (X, Y) but clamps the width/height to ptMinTrackSize.
    The call signature is SetWindowPos(hwnd, default, newX, newY, newCx, newCy, flags).
    If newCx < OS minimum, Windows uses OS minimum for width, but STILL places the left edge at newX.
  implication: "When the left edge moves rightward (shrink-left) and the resulting width is below OS minimum: Windows places left at newX (moved right), clamps width to OS minimum, so right edge = newX + OS_min_width. The window has moved right without shrinking."

- timestamp: 2026-03-02T00:00:00Z
  checked: "Concrete scenario trace for shrink-left at OS minimum size"
  found: |
    Assume: stepX=120px, OS_min_width=160px, visW=160px (exactly at OS minimum, visW > stepX so guard passes)
    vis.left=400, vis.right=560, newVisLeft=400+120=520, min-size clamp: Math.Min(520, 560-120=440) => 440
    new winRect left = 440 - borderL
    SetWindowPos receives: x=(440-borderL), cx=(560-440+borderR+borderL) = 120+borders
    But 120px < OS minimum 160px, so Windows: places window at x=(440-borderL), uses width=160px
    Result: window appears at x=(440-borderL) with width 160px -- shifted right by 40px from original (400-borderL)
  implication: "This is exactly the reported symptom: window moves right instead of being a no-op."

- timestamp: 2026-03-02T00:00:00Z
  checked: "Whether the same problem exists for direction=right (right edge moves inward)"
  found: |
    For case=right: newVisRight moves leftward; new rect has same left but smaller right.
    If computed width < OS min: Windows keeps the left edge fixed (newX = win.left unchanged) and expands rightward to OS min.
    This means the window grows rightward back to OS minimum -- it appears as a no-op from the user's perspective (window doesn't move or visibly shrink). Not the same bad symptom.
  implication: "The move symptom only manifests for the 'left' direction (and 'up' direction by analogy for vertical) because those directions change the anchor point (left/top edge)."

- timestamp: 2026-03-02T00:00:00Z
  checked: "Whether same problem exists for case=up (top edge moves downward)"
  found: |
    For case=up: newVisTop moves downward; new rect has same bottom but smaller top.
    If computed height < OS min: Windows places window at y=newTop (moved down), clamps height to OS min, so bottom = newTop + OS_min_height. Window has moved down.
    This matches the pattern -- 'up' direction also moves the window.
  implication: "The move-instead-of-no-op symptom affects both 'left' and 'up' directions -- any direction that adjusts the anchor (left/top) edge of the winRect."

## Resolution

root_cause: |
  The minimum-size guard in ComputeShrink checks `if (visW <= stepX) return win` (and `visH <= stepY`).
  This guard only prevents shrinking below ONE GRID STEP, not below the OS-enforced minimum window size
  (ptMinTrackSize from WM_GETMINMAXINFO, typically ~116-160px for most apps).

  When the window is at the OS minimum size (visW between stepX+1 and OS_min_width), the guard passes
  because visW > stepX. ComputeShrink computes a new rect with the left edge moved inward by stepX.
  The resulting width (visW - stepX, after min-size clamp) is passed to SetWindowPos.

  SetWindowPos behavior with size below OS minimum (documented in research Pitfall 6):
  Windows CLAMPS the width to ptMinTrackSize but still HONORS the requested position (newX, newY).
  For the 'left' direction: the left edge has moved rightward. Windows places the left edge at the new
  position and expands the width rightward to meet OS minimum. Net result: window has shifted right with
  its size unchanged -- the exact reported symptom.

  The same logic applies to the 'up' direction (which adjusts the top edge/Y position).

  Root cause in one sentence:
  The guard tests against (grid step size) instead of against (actual current visible width), so it
  fails to detect when the window is already at OS minimum but still larger than one grid step.
  The fix must compare the computed new size against the current size -- if no visible size change would
  occur, return win unchanged as a no-op.

fix: |
  Not implemented (goal: find_root_cause_only).

  Required code change in ComputeShrink, for the 'left' and 'up' cases (the ones that move the anchor edge):

  After computing newVisLeft (and the min-size clamp), add a check:
    if (newVisLeft >= newVisRight) return win;  // degenerate (already caught by clamp)

  The real fix: after ALL computation is complete (snap + min-size clamp applied), compare the resulting
  dimensions to the current dimensions. If the visible width/height did not change, return win unchanged:

  For case "left":
    int newVisW = newVisRight - newVisLeft;
    if (newVisW >= visW) return win;  // size didn't shrink (OS min will prevent it) -- no-op

  For case "right":
    int newVisW = newVisRight - newVisLeft;
    if (newVisW >= visW) return win;

  For case "up":
    int newVisH = newVisBottom - newVisTop;
    if (newVisH >= visH) return win;

  For case "down":
    int newVisH = newVisBottom - newVisTop;
    if (newVisH >= visH) return win;

  This is a post-computation no-op guard: if the grid math + min-size clamp produced no reduction in
  visible size, there is nothing to do and the call to SetWindowPos would only move the window without
  shrinking it -- so return win unchanged.

  Alternative fix (simpler, same effect): after all computation, compare newWinRect to winRect. If they
  are identical (or if only position changed without size changing), return win. But this over-constrains
  because the two rects are in different coordinate spaces; comparing the visible dimensions is cleaner.

verification: ""
files_changed: []
