# Phase 7: Hotkey Wiring - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Daemon detects and suppresses direction keys (arrows and WASD) while CAPSLOCK is held, and passes them through normally when CAPSLOCK is not held. This phase wires up the key interception only — no navigation action is triggered (that's Phase 8).

</domain>

<decisions>
## Implementation Decisions

### Modifier combinations
- CAPSLOCK held = daemon owns ALL direction keys, regardless of other modifiers (Shift, Ctrl, Alt, Win)
- CAPSLOCK + Shift/Ctrl/Alt/Win + direction = suppressed (not passed through)
- Modifier combos are reserved for future features — they will NOT trigger navigation in Phase 8 (only plain CAPSLOCK + direction triggers navigation)
- Verbose log includes which modifiers were held alongside the direction (e.g., "Shift+Left -> left (suppressed)")

### CAPSLOCK passthrough
- CAPSLOCK key is fully consumed by the daemon — no LED toggle, no caps lock state change
- Only direction keys (arrows + WASD) are suppressed while CAPSLOCK is held; non-direction keys (e.g., CAPSLOCK + T) pass through to the app
- No escape hatch for caps lock toggle (Shift handles individual capitals)
- Daemon forcibly resets caps lock state/LED to OFF on startup

### Direction key scope
- Intercept 8 keys only: Up/Down/Left/Right arrows + W/A/S/D
- No numpad arrow keys
- WASD mapping is hardcoded: W=up, A=left, S=down, D=right
- No special keys (Space, Escape, etc.) have any behavior while CAPSLOCK is held

### Key repeat behavior
- Only the initial keydown is logged in verbose mode; key repeats are suppressed silently (no log spam)
- Each navigation move (Phase 8) requires a distinct keypress — holding a direction key does NOT repeat navigation
- CAPSLOCK must already be held when the direction key is pressed (pressing direction first, then CAPSLOCK, does not intercept)

### Claude's Discretion
- WASD case sensitivity handling (technical detail of Win32 keyboard hook virtual key codes)
- Whether to track keyup events for direction keys (future-proofing decision)
- Exact verbose log format and structure
- Error handling for keyboard hook registration failures

</decisions>

<specifics>
## Specific Ideas

- Modifier combos reserved for future use — e.g., Shift + direction could mean "swap windows" in a later phase
- The "distinct press per switch" decision means the daemon should track keydown state and ignore repeat events, not just debounce

</specifics>

<deferred>
## Deferred Ideas

- Configurable modifier key (use key other than CAPSLOCK) — HOTKEY-05 in future requirements
- Configurable direction key mappings — HOTKEY-06 in future requirements
- Modifier combo actions (e.g., Shift+direction = move window) — future phase

</deferred>

---

*Phase: 07-hotkey-wiring*
*Context gathered: 2026-03-01*
