# Phase 2: Navigation Pipeline - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning

<domain>
## Phase Boundary

End-to-end directional focus switching: given a direction (left/right/up/down), find the most geometrically appropriate window and activate it. Covers the balanced scoring strategy, multi-monitor support via virtual screen coordinates, focus activation with SetForegroundWindow bypass, and meaningful exit codes. Config system, additional strategies, and exclude lists belong to Phase 3.

</domain>

<decisions>
## Implementation Decisions

### Multi-monitor behavior
- Treat all monitors as one virtual screen — pure geometry, no same-monitor preference
- Windows scored equally regardless of which monitor they're on
- Navigating "right" from the right edge of one monitor naturally reaches the next monitor
- Use raw virtual screen coordinates as-is — ignore physical gaps between monitors
- User setup: laptop screen below main monitor (vertical arrangement), so "down" reaches laptop, "up" reaches main monitor

### Activation edge cases
- If the best candidate is an elevated (admin) window and activation fails, skip it and try the next-best candidate in that direction
- If ALL candidates in a direction fail to activate (e.g., all elevated), return exit code 2 (error) — distinct from exit code 1 (no candidates exist)
- Only target visible windows — minimized windows are already filtered by Phase 1 enumeration
- Always attempt focus switch even when source is a fullscreen app — if the hotkey fires, the user pressed it intentionally

### Source reference point
- Origin point: geometric center of the foreground window
- Target measurement: nearest edge/point on the target window's bounds (large nearby windows shouldn't be penalized for having distant centers)
- If no foreground window can be determined (desktop focused, no window has focus), fall back to the center of the primary monitor
- Always exclude the currently focused window from candidates — you're navigating away from it

### Claude's Discretion
- Navigation feel: direction cone width, angle thresholds, and scoring weights for the balanced strategy
- How to handle diagonal edge cases (windows that are partially in the requested direction)
- SetForegroundWindow bypass implementation details (SendInput ALT technique)
- Tie-breaking logic when multiple windows score equally

</decisions>

<specifics>
## Specific Ideas

- User has laptop below main monitor — vertical monitor arrangement should feel natural with up/down navigation
- When the best candidate can't be activated, silently fall through to next-best rather than failing immediately — makes hotkey usage feel smooth
- "If it's the best candidate" philosophy: direction is the primary filter, but distant diagonal candidates should still be reachable if nothing else is available in that direction

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 02-navigation-pipeline*
*Context gathered: 2026-02-27*
