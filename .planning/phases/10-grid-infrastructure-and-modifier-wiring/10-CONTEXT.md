# Phase 10: Grid Infrastructure and Modifier Wiring - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

The daemon correctly detects CAPS+TAB, CAPS+LSHIFT, and CAPS+LCTRL combos and routes them as modifier-qualified direction events; the grid step is computed per monitor from work area dimensions using configurable parameters. This phase does NOT implement the actual move/resize operations (Phase 11) or overlay indicators (Phase 12).

</domain>

<decisions>
## Implementation Decisions

### TAB key behavior
- TAB is suppressed (eaten) when CAPS is held — not forwarded to the focused application
- Bare TAB (CAPS not held) passes through to the app unchanged (MODE-04)
- Releasing CAPS exits all modes (move/grow/shrink) regardless of whether TAB/SHIFT/CTRL are still held — CAPS is always the master switch
- If user is navigating (CAPS+direction) and presses TAB mid-stream, smoothly transition to move mode — navigation state resets, move mode activates without needing to release and re-hold CAPS

### Grid step defaults
- Separate fractions per axis: `gridFractionX` (default 16) and `gridFractionY` (default 12)
- This gives nearly square grid cells on typical 16:9/16:10 monitors (~120x87px on 1080p)
- `snapTolerancePercent` default 10 — windows within 10% of a grid step from a grid line are considered "close enough"
- Grid computed per-monitor from that monitor's work area (GRID-02)

### Snap-first behavior
- First operation snaps the window to the nearest grid line on the axis of operation
- Exception: if the window is within the snap tolerance (10%), snap AND step in one press — avoids imperceptible micro-moves
- Snap-first applies to ALL operations: move, grow, and shrink (consistent across modes)
- Move operations: only snap the movement axis (pressing Right snaps X only, not Y)
- Resize operations: only snap the affected edge (growing right edge only snaps that edge to grid)

### Modifier detection
- Left Shift (VK_LSHIFT) triggers grow mode; Right Shift does NOT
- Left Ctrl (VK_LCONTROL) triggers shrink mode; Right Ctrl does NOT
- TAB (VK_TAB) triggers move mode when CAPS is held

### Claude's Discretion
- How to wire TAB interception into the existing KeyboardHookHandler (follow existing direction key pattern)
- How to propagate mode information through CapsLockMonitor callbacks (new callback signatures or mode enum)
- GridCalculator service design (pure computation — work area dimensions + fractions)
- Config schema evolution (how to add gridFractionX/Y alongside existing config properties)

</decisions>

<specifics>
## Specific Ideas

- Snap tolerance threshold does double duty: it defines "close enough to grid" for snap AND controls whether first-press snap-only feels like a no-op (within tolerance = snap+step instead)
- Mode transitions should feel seamless — pressing TAB while already navigating with CAPS should be as natural as pressing a different direction

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `KeyboardHookHandler`: Already intercepts direction keys when CAPS held, reads Shift/Ctrl/Alt state via `GetKeyState`, passes modifier flags in `KeyEvent` record. TAB interception follows the same pattern.
- `KeyEvent` record: Already carries `ShiftHeld`, `CtrlHeld`, `AltHeld` booleans. May need Left/Right distinction (currently uses generic VK_SHIFT/VK_CONTROL — need VK_LSHIFT/VK_LCONTROL check).
- `CapsLockMonitor`: Consumes `KeyEvent` via Channel. Currently has `_onDirectionKeyDown(string)` callback — needs modifier-aware routing.
- `FocusConfig`: JSON config with kebab-case enum serialization, auto-defaults. Add `gridFractionX`, `gridFractionY`, `snapTolerancePercent` properties.
- `MonitorHelper`: Enumerates monitors, gets HMONITOR handles, maps HWND to monitor index. Needs work area rectangle retrieval (GetMonitorInfo → rcWork).

### Established Patterns
- Hook callback → Channel → CapsLockMonitor → callbacks: All keyboard input flows through this pipeline. New keys (TAB) and modifier routing follow this same pattern.
- Config: Properties with defaults on `FocusConfig`, loaded once at daemon startup via `FocusConfig.Load()`.
- VK code constants defined at top of `KeyboardHookHandler` as `private const uint`.

### Integration Points
- `KeyboardHookHandler.HookCallback`: Add TAB interception block alongside existing direction key and number key blocks
- `CapsLockMonitor`: Modify or extend direction callback to include mode qualifier (move/grow/shrink/navigate)
- `DaemonCommand.Run()`: CapsLockMonitor constructor takes callbacks — new callback signatures needed for mode-aware direction events
- `OverlayOrchestrator`: Currently receives `OnDirectionKeyDown(string)` — Phase 11/12 will need mode-qualified events

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 10-grid-infrastructure-and-modifier-wiring*
*Context gathered: 2026-03-02*
