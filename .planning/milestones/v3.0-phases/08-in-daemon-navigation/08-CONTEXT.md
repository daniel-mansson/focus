# Phase 8: In-Daemon Navigation - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

CAPSLOCK + direction keys trigger focus switching directly from the daemon, using the same scoring engine and config as the stateless CLI. No overlay involvement — pure hotkey-to-focus navigation. The overlay chaining behavior (persisting overlay across moves) is Phase 9.

</domain>

<decisions>
## Implementation Decisions

### No-match behavior
- Silent no-op when no candidate window exists in the pressed direction — focus stays on current window
- Wrap behavior follows the existing config.json wrap setting (same as CLI)
- Always navigate from current window's position as origin, even if that window would be excluded by the scoring engine
- When no foreground window can be determined (desktop focused, system dialog), use screen center as origin and find the best candidate in the pressed direction

### Navigation feedback
- No extra visual or audio feedback beyond the target window activating — the window coming forward IS the feedback
- Verbose logging of every successful navigation: direction, origin window, target window (matches Phase 7's verbose key logging pattern)
- No explicit latency budget — use the same scoring path as the CLI, which is already fast enough for interactive use
- If SetForegroundWindow fails, retry once with AllowSetForegroundWindow workaround, then log failure and give up

### Config reloading
- Read config fresh on each CAPSLOCK+direction press — always current, negligible overhead for a JSON file read
- Reuse the exact same config loading code path as the CLI — guarantees identical behavior per success criterion #3
- Validate config on load, log warnings for invalid settings in verbose mode, fall back to defaults

### Claude's Discretion
- Scoring engine instance lifecycle (fresh per call vs cached with config refresh)
- Internal architecture for wiring the scoring engine into the daemon's OnDirectionKeyDown callback
- Error handling details beyond the SetForegroundWindow retry pattern
- Thread marshalling between the keyboard hook thread and navigation execution

</decisions>

<specifics>
## Specific Ideas

- Navigation must produce identical results whether triggered from `focus left` CLI or CAPSLOCK+Left in daemon (success criterion #3)
- The OnDirectionKeyDown callback from Phase 7 is the hook point — currently a no-op, Phase 8 fills it in
- The stateless CLI (`focus <direction>`) must continue working independently when the daemon is not running (success criterion #4)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 08-in-daemon-navigation*
*Context gathered: 2026-03-01*
