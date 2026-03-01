# Phase 4: Daemon Core - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Persistent background process started via `focus daemon` that installs a global WH_KEYBOARD_LL hook, detects CAPSLOCK held/released state, and shuts down cleanly. No overlay rendering — that's Phase 5/6. This phase delivers the daemon infrastructure, keyboard hook, CAPSLOCK detection, and process lifecycle management.

</domain>

<decisions>
## Implementation Decisions

### Daemon lifecycle
- Support both foreground (default) and background (`--background` flag) modes
- System tray icon with Exit option for daemon lifecycle management (overrides earlier out-of-scope decision — user explicitly chose this)
- Brief one-line confirmation on startup (e.g., "Focus daemon started. Listening for CAPSLOCK."), then silent
- Ctrl+C stops foreground mode; system tray Exit stops background mode
- Second instance kills the existing daemon and starts fresh (no error-and-exit)

### CAPSLOCK hold detection
- Suppress CAPSLOCK toggle behavior entirely while daemon is running — CAPSLOCK becomes a pure modifier key
- Quick taps are silently swallowed — CAPSLOCK does nothing unless held
- Only bare CAPSLOCK triggers hold detection — Ctrl+CAPSLOCK, Shift+CAPSLOCK, Alt+CAPSLOCK are ignored
- LLKHF_INJECTED events (AHK-synthesized) are filtered out — no overlay flicker from AHK
- This phase: hold/release events trigger debug log lines only (no overlay rendering)

### Logging & debug output
- Log to stdout/stderr only — no dedicated log file
- Silent by default after startup confirmation — consistent with v1's silent-by-default design
- `--verbose` flag enables CAPSLOCK event logging (reuses v1's existing flag pattern)
- Verbose log format: timestamped plain text (e.g., `[12:34:56.789] CAPSLOCK held`)

### Error handling
- Hook installation failure: print clear error message, exit immediately (no retries)
- Sleep/wake: auto-recover by detecting system events and reinstalling hook on wake
- No crash recovery or watchdog — if it crashes, user restarts manually

### Claude's Discretion
- Message pump implementation details (Win32 message loop vs Application.Run)
- Exact system tray icon design and tooltip text
- Background mode detach mechanism
- Hook reinstallation strategy on wake (polling vs system event subscription)
- Mutex naming convention

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 04-daemon-core*
*Context gathered: 2026-03-01*
