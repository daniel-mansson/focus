# Phase 6: Navigation Integration - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Holding CAPSLOCK shows colored borders on the actual top-ranked directional candidate windows (all four directions simultaneously), updating when the foreground window changes, and dismissing on CAPSLOCK release. This phase wires together the daemon keyboard hook (Phase 4), overlay rendering (Phase 5), and navigation scoring (Phase 2) into the final user-facing experience.

</domain>

<decisions>
## Implementation Decisions

### Activation feel
- No intentional activation delay — overlays appear as soon as CAPSLOCK hold is detected
- If the detection mechanism requires polling, maximum poll interval is 100ms
- The `overlayDelayMs` config key should still exist for user tuning, but default to 0
- CAPSLOCK taps (quick press-and-release) are fully suppressed — no toggle behavior at all
- On daemon startup, force CAPSLOCK toggle state OFF so users never end up typing in caps
- Note: REQUIREMENTS.md lists DAEMON-03 with "configurable activation delay" and CFG-06 with "default ~150ms" — user has overridden the default to 0 (no delay)

### Overlay transitions
- **Show:** Quick fade-in (~100ms) from transparent to full opacity when overlays first appear
- **Dismiss:** Quick fade-out (~80ms) when CAPSLOCK is released
- **Reposition (foreground change):** Instant — old overlays vanish, new overlays appear at new positions immediately (no cross-fade)
- **Live tracking:** No — overlays only recompute when the foreground window changes, not when target windows resize/move mid-hold
- Note: REQUIREMENTS.md "Out of Scope" lists "Animated overlay transitions (fade in/out)" — user has decided they DO want brief fade-in/out. This overrides the out-of-scope entry.

### Multi-direction overlap
- If one window is the best candidate for multiple directions, show ALL borders — one overlay window per direction, stacked on the same target
- Separate overlay windows per direction (reuse existing OverlayWindow architecture), not merged into a single multi-edge overlay
- No special handling at corners where two directional edges meet — each overlay paints its edge independently
- No limit on overlays per target window — up to 4 (all directions) is acceptable

### No-candidate indication
- When a direction has no reachable window: show nothing for that direction (silent absence)
- Special case — when ALL four directions have zero candidates (solo window): show a dim/muted border on all edges of the source (foreground) window, confirming the daemon is active
- This dim source border ONLY appears in the "totally alone" case (zero candidates in every direction), NOT on individual missing directions when other directions have targets

### Claude's Discretion
- Exact dim border color/opacity for the solo-window indicator
- Internal architecture for wiring CapsLockMonitor state changes to OverlayManager
- Timer/animation mechanism for fade-in/fade-out (WinForms Timer, async Task.Delay, etc.)
- How to efficiently detect foreground window changes (WinEventHook vs polling)

</decisions>

<specifics>
## Specific Ideas

- The overall feel should be: hold CAPSLOCK and instantly see where each direction would take you. Release and it's gone. No friction.
- Fade-in/fade-out should be subtle polish, not perceived delay — 100ms in, 80ms out.
- The solo-window dim border is a "daemon is alive" heartbeat, not a navigation cue.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 06-navigation-integration*
*Context gathered: 2026-03-01*
