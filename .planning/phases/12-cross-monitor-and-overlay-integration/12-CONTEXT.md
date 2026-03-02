# Phase 12: Cross-Monitor and Overlay Integration - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Moving a window at a monitor boundary transitions it to the adjacent monitor at the correct grid position, and the overlay reflects the active mode (move/grow/shrink) with correct directional arrows throughout all operations. Requirements: XMON-01, XMON-02, OVRL-01, OVRL-02, OVRL-03, OVRL-04.

</domain>

<decisions>
## Implementation Decisions

### Cross-monitor transition behavior
- Immediate jump on next keypress when window is at monitor edge — no "stick at boundary" checkpoint
- Movement axis snaps to first grid cell from the target monitor edge; perpendicular axis preserves current pixel position (no re-snap on perpendicular axis)
- If window is larger than target monitor work area, clamp to target work area boundaries (do not resize)
- Silent no-op when no adjacent monitor exists in the pressed direction (same pattern as hitting work area boundary)

### Mode indicator appearance
- Mode-specific colors: move, grow, and shrink each use a distinct color so the active mode is immediately obvious
- Prominent/high-opacity arrows — these ARE the feedback mechanism, they should be clearly visible
- OVRL-04: all transitions are instant, no animation or flash effects

### Arrow layout per mode
- Move mode (CAPS+Space): 4 directional arrows arranged as a compass/cross at window center — indicates "you can move in any direction"
- Grow mode (CAPS+LAlt): 4 outward-pointing arrows at the center of each edge — communicates "this mode expands the window"
- Shrink mode (CAPS+LCtrl): 4 inward-pointing arrows at the center of each edge — communicates "this mode contracts the window"
- Arrows appear immediately on modifier hold (before any direction key is pressed) — tells user which mode they're in before they act
- After each operation, arrows reposition instantly to track the window's new position

### Monitor adjacency and DPI
- Handle mixed DPI/scaling correctly (e.g., 4K at 150% + 1080p at 100%) — grid step recalculates using target monitor dimensions
- Primary use case is 2 monitors, but build adjacency detection to work for any reasonable count
- Build DPI-aware coordinate translation even though user currently has same-DPI monitors

### Claude's Discretion
- Arrow rendering technique (GDI+ triangles, Unicode glyphs, or hybrid) — whatever integrates cleanest with existing BorderRenderer/GDI+ overlay system
- Mode color palette — 3 distinct, accessible colors that contrast well on both light and dark backgrounds
- Arrow size relative to window dimensions
- Adjacency algorithm specifics — overlapping range vs nearest edge vs window-position-aware; pick what handles real-world layouts best
- Multi-monitor target selection logic (window-position-aware vs nearest-edge)

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. User wants mode colors to be immediately distinguishable and arrows to be prominent enough to serve as the primary mode indicator.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `WindowManagerService.MoveOrResize()`: Handles all move/resize with dual-rect pattern. Cross-monitor logic needs to extend this — currently clamps to work area boundary at MOVE-03
- `GridCalculator`: Stateless grid math (step, snap, tolerance). Already parameterized by work area dimensions — just pass target monitor's dimensions for XMON-02
- `MonitorHelper`: Enumerates monitors and maps HWND to monitor index. Needs adjacency detection added (no existing adjacent-monitor logic)
- `OverlayOrchestrator.OnModeEntered()`: Currently hides navigate targets and shows foreground border only. Needs to show mode-specific arrows instead
- `OverlayOrchestrator.RefreshForegroundOverlayOnly()`: Called after each move/resize. This is the integration point for arrow repositioning after operations
- `BorderRenderer`: GDI+ painting on layered windows. Arrow rendering would extend this pattern (new renderer or new methods)
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
