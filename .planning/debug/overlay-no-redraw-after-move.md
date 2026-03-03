---
status: diagnosed
trigger: "Overlay outline doesn't redraw after window move"
created: 2026-03-02T00:00:00Z
updated: 2026-03-02T00:00:00Z
---

## Current Focus

hypothesis: MoveOrResize never triggers ShowOverlaysForCurrentForeground, so overlay stays at stale position
test: traced OnDirectionKeyDown -> MoveOrResize path and all overlay refresh paths
expecting: confirmed — no refresh call exists after MoveOrResize returns
next_action: return diagnosis

## Symptoms

expected: After pressing Caps+Tab + arrow key to move a window, the white border overlay redraws around the window's new position
actual: The overlay border stays at the pre-move position
errors: none — silent visual bug
reproduction: Hold Caps, hold Tab (entering Move mode), press an arrow key; observe overlay stays at old bounds
started: Phase 11 introduction of MoveOrResize; overlay was never wired to refresh after move

## Eliminated

- hypothesis: ForegroundMonitor fires after move and triggers overlay refresh
  evidence: SetWindowPos does NOT change the foreground window, so EVENT_SYSTEM_FOREGROUND is never raised; OnForegroundChanged is not called
  timestamp: 2026-03-02

- hypothesis: The overlay refreshes itself independently on a timer
  evidence: No timer exists in OverlayOrchestrator for periodic overlay refresh; only _delayTimer which fires once on CapsLock hold
  timestamp: 2026-03-02

- hypothesis: ShowOverlaysForCurrentForeground is somehow called on the return path
  evidence: OnDirectionKeyDown short-circuits with `return` immediately after invoking MoveOrResize (line 143), never falls through to NavigateSta or any overlay update
  timestamp: 2026-03-02

## Evidence

- timestamp: 2026-03-02
  checked: OverlayOrchestrator.OnDirectionKeyDown (lines 131-152)
  found: |
    When mode != WindowMode.Navigate, it invokes WindowManagerService.MoveOrResize on the STA thread
    and then immediately returns. There is no call to ShowOverlaysForCurrentForeground anywhere in
    this branch.
  implication: The overlay is never refreshed after a move/resize operation.

- timestamp: 2026-03-02
  checked: ForegroundMonitor / OnForegroundChanged (OverlayOrchestrator line 279-285)
  found: |
    ForegroundMonitor hooks EVENT_SYSTEM_FOREGROUND via SetWinEventHook. This event fires only
    when the foreground window *changes* (i.e. focus moves to a different window). SetWindowPos
    with SWP_NOACTIVATE does not change the foreground window — it only repositions it. Therefore
    EVENT_SYSTEM_FOREGROUND is never raised by a move operation.
  implication: The ForegroundMonitor callback is not a usable trigger for overlay refresh after move.

- timestamp: 2026-03-02
  checked: WindowManagerService.MoveOrResize (lines 84-93)
  found: |
    MoveOrResize calls SetWindowPos with SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER and
    returns void. It has no knowledge of the overlay system and no callback/event to notify callers
    that the move completed.
  implication: The fix must be in OverlayOrchestrator, not in WindowManagerService.

- timestamp: 2026-03-02
  checked: ShowOverlaysForCurrentForeground (OverlayOrchestrator lines 291-439)
  found: |
    This method does everything needed: hides all overlays, reads current foreground window via
    GetForegroundWindow(), fetches DWMWA_EXTENDED_FRAME_BOUNDS for the new bounds, and calls
    ShowForegroundOverlay with the fresh RECT. It is already the correct refresh mechanism.
  implication: Calling this method after MoveOrResize will fix the bug.

## Resolution

root_cause: |
  In OverlayOrchestrator.OnDirectionKeyDown, when mode != WindowMode.Navigate (i.e. Move, Grow,
  or Shrink), the code invokes WindowManagerService.MoveOrResize and then immediately returns
  without refreshing the overlay. The ForegroundMonitor hook (EVENT_SYSTEM_FOREGROUND) does not
  fire because SetWindowPos with SWP_NOACTIVATE does not change foreground focus. There is no
  other mechanism to trigger an overlay redraw. The result is that the overlay border remains
  frozen at the window's pre-move coordinates for the duration of the CapsLock hold.

fix: |
  In OverlayOrchestrator.OnDirectionKeyDown, after the MoveOrResize Invoke call, add a call to
  ShowOverlaysForCurrentForeground() — but only if _capsLockHeld is true (overlay is visible).
  Because the Invoke is already on the STA thread, ShowOverlaysForCurrentForeground can be called
  directly inside the lambda.

  Change (in OverlayOrchestrator.cs, lines 135-144):

    if (mode != WindowMode.Navigate)
    {
        try
        {
            _staDispatcher.Invoke(() => WindowManagerService.MoveOrResize(direction, mode, _verbose));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        return;
    }

  To:

    if (mode != WindowMode.Navigate)
    {
        try
        {
            _staDispatcher.Invoke(() =>
            {
                WindowManagerService.MoveOrResize(direction, mode, _verbose);
                if (_capsLockHeld)
                    ShowOverlaysForCurrentForeground();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        return;
    }

verification: not yet applied
files_changed:
  - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
