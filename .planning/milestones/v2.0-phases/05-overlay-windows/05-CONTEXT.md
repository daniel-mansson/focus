# Phase 5: Overlay Windows - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Render colored border overlays on desktop windows — transparent, click-through, excluded from Alt+Tab, and DPI-correct. This phase delivers the overlay rendering infrastructure and a debug command to test it with hardcoded positions. Navigation wiring (showing overlays on actual directional candidates) is Phase 6.

</domain>

<decisions>
## Implementation Decisions

### Border appearance
- Thin borders: 2-3px thickness
- Rounded corners to match Windows 11 window chrome
- Semi-transparent: ~75% opacity (ARGB alpha channel)
- Clean edges only — no glow, shadow, or bloom effects

### Direction color palette
- Cool/warm spatial scheme: blue-left, red-right, green-up, yellow/orange-down
- Muted/pastel saturation — subtle and refined, not vivid
- All four directions equal visual weight — no hierarchy between horizontal and vertical
- Claude picks exact ARGB hex values that fit "muted cool/warm spatial" aesthetic

### Test mode invocation
- New debug command: `focus --debug overlay <direction>` (fits existing --debug pattern)
- Targets the current foreground window (renders border around whatever is focused)
- Shows one direction at a time, matching existing direction argument pattern
- Overlay stays visible until a keypress, then removes and exits

### Claude's Discretion
- Exact ARGB hex values for the four direction colors
- Corner radius value (should approximate Win11 window radius)
- IOverlayRenderer interface design and renderer selection mechanism
- Win32 window style flags (WS_EX_TRANSPARENT, WS_EX_TOOLWINDOW, etc.)
- GDI rendering approach for borders with rounded corners
- How to exclude overlay windows from the tool's own enumeration

</decisions>

<specifics>
## Specific Ideas

- Border should feel like a subtle directional hint, not a heavy highlight — "thin, rounded, translucent, clean"
- Color associations are spatial: cool tones (blue) for left, warm tones (red) for right, green for up, yellow/orange for down
- Debug overlay command follows existing CLI patterns: `focus --debug overlay left` parallels `focus --debug score left`

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-overlay-windows*
*Context gathered: 2026-03-01*
