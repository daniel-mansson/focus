# Phase 11: Move and Resize (Single Monitor) - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can move the foreground window by grid steps in any direction (CAPS+TAB+direction) and grow or shrink any window edge by grid steps (CAPS+LSHIFT+direction / CAPS+LCTRL+direction), with snap-first behavior, boundary clamping, and guards against maximized and elevated windows. Single monitor only — cross-monitor transitions are a separate phase (XMON-01/02).

</domain>

<decisions>
## Implementation Decisions

### Resize edge selection
- **Directional edge model:** The edge in the pressed direction is the one that moves
  - CAPS+LSHIFT+Right → right edge moves rightward (grow)
  - CAPS+LCTRL+Right → right edge moves leftward (shrink)
  - Same principle for all four directions — each direction key controls its own edge
- Grow and shrink are mirror operations on the same edge — same direction key, same edge, opposite movement direction

### Boundary behavior (grow)
- Growing stops at the monitor work area boundary — if the edge is already at the boundary, the operation is a no-op
- Do NOT push the opposite edge when hitting the boundary
- Work area boundary = rcWork (excludes taskbar), not rcMonitor

### Minimum size (shrink)
- Shrink stops when the window dimension would go below one grid cell (1 grid step)
- Do not allow sub-cell window sizes
- This is a hard stop — pressing shrink at minimum size is a silent no-op

### Snap-first behavior
- Per success criteria: a misaligned window snaps to the nearest grid line on the first operation, then steps by one grid cell on subsequent presses
- GridCalculator.IsAligned with configurable snapTolerancePercent (default 10%) determines if snap is needed

### Guarded windows
- Per success criteria: maximized and elevated (admin) windows produce no visible error and no window change — silent no-op

### Claude's Discretion
- Whether to restore a maximized window before moving/resizing, or simply refuse (success criteria says "no window change" so refuse is the safe read)
- Snap-first implementation details: whether snap-only counts as a "press" or is an invisible pre-step
- Overlay indicators for move/resize modes (OVRL-01/02/03 in requirements — may be deferred to a separate phase or included here)
- Internal architecture: whether to create a new WindowManagerService class or extend OverlayOrchestrator

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `GridCalculator` (Windows/GridCalculator.cs): Already has `GetGridStep`, `NearestGridLine`, `IsAligned`, `GetSnapTolerancePx` — all stateless, ready for Phase 11 consumption
- `FocusConfig` (Windows/FocusConfig.cs): Has `GridFractionX` (default 16), `GridFractionY` (default 12), `SnapTolerancePercent` (default 10) — grid config is already wired
- `MonitorHelper` (Windows/MonitorHelper.cs): Has `EnumerateMonitors`, `GetMonitorIndex` — can get monitor work area via `GetMonitorInfo`
- `WindowInfo` record: Has HWND, bounds (L/T/R/B from DWMWA_EXTENDED_FRAME_BOUNDS), MonitorIndex

### Established Patterns
- Channel<KeyEvent> pipeline: hook callback → CapsLockMonitor → OverlayOrchestrator callbacks
- `WindowMode` enum already has Navigate/Move/Grow/Shrink — keyboard routing is complete
- `OverlayOrchestrator.OnDirectionKeyDown` already routes by mode — Move/Grow/Shrink currently log "not yet implemented"
- Cross-thread marshaling via `Control.Invoke` to STA thread for all window operations
- CsWin32 for all Win32 interop (SetWindowPos, GetWindowRect, GetMonitorInfo etc. available via NativeMethods.txt)

### Integration Points
- `OverlayOrchestrator.OnDirectionKeyDown` line 131-149: The exact hook point — replace the "not yet implemented" log with actual move/resize logic
- `CapsLockMonitor.HandleDirectionKeyEvent` line 180-205: Calls `_onDirectionKeyDown` with direction string and WindowMode — no changes needed here
- `KeyboardHookHandler` line 164-172: Mode derivation from _tabHeld/lShiftHeld/lCtrlHeld — already complete

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 11-move-and-resize-single-monitor*
*Context gathered: 2026-03-02*
