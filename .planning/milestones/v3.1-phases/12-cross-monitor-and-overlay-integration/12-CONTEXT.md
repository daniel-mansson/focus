# Phase 12: Cross-Monitor and Overlay Integration - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Moving a window at a monitor boundary transitions it to the adjacent monitor at the correct grid position, and the overlay reflects the active mode (move/resize) with correct directional arrows throughout all operations. Requirements: XMON-01, XMON-02, OVRL-01, OVRL-02, OVRL-03, OVRL-04.

</domain>

<decisions>
## Implementation Decisions

### Current mode architecture (from recent refactors)
- Two window operation modes, not three: **Move** (CAPS+LAlt+direction) and **Resize** (CAPS+LWin+direction)
- Shrink was removed as a separate mode (`67d591c`). `ComputeGrow` handles both expand (right/up) and contract (left/down) symmetrically
- `WindowMode` enum has three values: `Navigate`, `Move`, `Grow` (Grow = resize)
- Navigate overlay state already works: `OnModeEntered()` hides navigate targets, `OnModeExited()` restores them
- `RefreshForegroundOverlayOnly()` already called after each move/resize to update foreground border position

### Cross-monitor transition behavior
- Immediate jump on next keypress when window is at monitor edge — no "stick at boundary" checkpoint
- Movement axis snaps to first grid cell from the target monitor edge; perpendicular axis preserves current pixel position (no re-snap on perpendicular axis)
- If window is larger than target monitor work area, clamp to target work area boundaries (do not resize)
- Silent no-op when no adjacent monitor exists in the pressed direction (same pattern as hitting work area boundary)

### Mode indicator appearance
- Mode-specific colors: move and resize each use a distinct color so the active mode is immediately obvious
- Prominent/high-opacity arrows — these ARE the feedback mechanism, they should be clearly visible
- OVRL-04: all transitions are instant, no animation or flash effects

### Arrow layout per mode
- **Move mode** (CAPS+LAlt): 4 directional arrows arranged as a compass/cross at window center — indicates "you can move in any direction"
- **Resize mode** (CAPS+LWin): left/right arrow at the right edge center, up/down arrow at the top edge center — indicates which axis responds to which direction
- Arrows appear immediately on modifier hold (before any direction key is pressed) — tells user which mode they're in before they act
- After each operation, arrows reposition instantly to track the window's new position
- Navigate target outlines already hide when a mode modifier is held (existing `OnModeEntered` behavior) — mode arrows replace them

### Monitor adjacency and DPI
- Handle mixed DPI/scaling correctly (e.g., 4K at 150% + 1080p at 100%) — grid step recalculates using target monitor dimensions
- Primary use case is 2 monitors, but build adjacency detection to work for any reasonable count
- Build DPI-aware coordinate translation even though user currently has same-DPI monitors

### Claude's Discretion
- Arrow rendering technique (GDI+ triangles, Unicode glyphs, or hybrid) — whatever integrates cleanest with existing BorderRenderer/GDI+ overlay system
- Mode color palette — 2 distinct, accessible colors that contrast well on both light and dark backgrounds
- Arrow size relative to window dimensions
- Adjacency algorithm specifics — overlapping range vs nearest edge vs window-position-aware; pick what handles real-world layouts best
- Multi-monitor target selection logic (window-position-aware vs nearest-edge)

</decisions>

<specifics>
## Specific Ideas

- Resize arrows are positional indicators: left/right arrow at right edge center shows "press left/right to shrink/grow horizontally"; up/down arrow at top edge center shows "press up/down to grow/shrink vertically"
- Mode colors should be immediately distinguishable; arrows should be prominent enough to serve as the primary mode indicator
- Most overlay state management already works — `OnModeEntered`/`OnModeExited`/`RefreshForegroundOverlayOnly` handle the show/hide lifecycle; this phase adds mode-specific content to what's shown

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `WindowManagerService.MoveOrResize()`: Handles all move/resize with dual-rect pattern. Cross-monitor logic needs to extend this — currently clamps to work area boundary at MOVE-03
- `WindowManagerService.ComputeGrow()`: Already handles both expand (right/up) and contract (left/down) symmetrically with half-step edge moves
- `GridCalculator`: Stateless grid math (step, snap, tolerance). Already parameterized by work area dimensions — just pass target monitor's dimensions for XMON-02
- `MonitorHelper`: Enumerates monitors and maps HWND to monitor index. Needs adjacency detection added (no existing adjacent-monitor logic)
- `OverlayOrchestrator.OnModeEntered()`: Currently hides navigate targets and shows foreground border only. Needs to show mode-specific arrows instead
- `OverlayOrchestrator.RefreshForegroundOverlayOnly()`: Called after each move/resize — this is the integration point for arrow repositioning
- `BorderRenderer`: GDI+ painting on layered windows. Arrow rendering would extend this pattern
- `OverlayManager`: Manages per-direction overlay windows + foreground overlay + number labels. May need new overlay windows for arrow indicators

### Established Patterns
- Dual-rect coordinate handling: `GetWindowRect` for SetWindowPos input, `DWMWA_EXTENDED_FRAME_BOUNDS` for visible bounds. Cross-monitor must preserve this pattern
- STA thread marshaling: All overlay operations go through `_staDispatcher.Invoke()` from worker thread
- Config reload per-operation: `FocusConfig.Load()` called fresh each time so runtime changes take effect
- Guard pattern: maximized window check, UIPI/elevation check — both return silently

### Integration Points
- `WindowManagerService.ComputeMove()` is where cross-monitor detection would fire (when clamped position equals boundary)
- `OverlayOrchestrator.OnModeEntered()` is where mode-specific arrows replace the current "hide navigate targets" behavior
- `OverlayOrchestrator.OnDirectionKeyDown()` calls `RefreshForegroundOverlayOnly()` after move/resize — this becomes the arrow reposition point
- `KeyEvent.Mode` (WindowMode enum: Navigate, Move, Grow) carries the mode through the pipeline — overlay needs to read this

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 12-cross-monitor-and-overlay-integration*
*Context gathered: 2026-03-03*
