# Phase 3: Config, Strategies & Complete CLI - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Add the configuration layer, all three weighting strategies, and complete CLI surface to make this a fully-featured v1 tool. JSON config for persistent settings, CLI flags for overrides, exclude list, wrap-around behavior, and debug/verbose output. The foundation (enumeration, navigation, focus activation) is built — this phase layers configurability and the remaining strategies on top.

</domain>

<decisions>
## Implementation Decisions

### Config file location & schema
- Config lives at `%APPDATA%\focus\config.json`
- When no config file exists, use hardcoded defaults silently — tool works out of the box with zero setup
- Support `focus --init-config` to generate a starter config file with all defaults and comments explaining each option
- Exclude list uses glob/wildcard patterns (e.g., `"Teams*"`, `"*Chrome*"`) — no regex needed

### Strategy feel & defaults
- Default strategy is **balanced** (already implemented in Phase 2)
- **Strong-axis-bias**: mild lane preference — lightly biases toward windows aligned on the navigation axis, but still considers off-axis candidates. Subtle but noticeable difference from balanced
- **Closest-in-direction**: pure distance with wide cone (~90°) — picks the nearest window center-to-center as long as it's roughly in the direction. Simple mental model: closest wins
- Fixed presets for v1 — no user-tunable weight parameters (CFG-05 deferred to v2)

### CLI flag naming & overrides
- Long flags only: `--strategy`, `--wrap`, `--exclude`, `--verbose`, `--debug`, `--init-config`. No short flags — this is hotkey-driven, not typed frequently
- CLI wins, simple merge: a CLI flag overrides the same config key entirely (no list merging — `--exclude` replaces the config exclude list)
- Debug uses `--debug <mode>` pattern: `focus --debug enumerate`, `focus --debug score left`, `focus --debug config` (consistent with Phase 1)
- `--verbose` output goes to stderr; stdout stays empty on normal operation

### Claude's Discretion
- Wrap-around behavior defaults and implementation details (user did not select this for discussion)
- Config file JSON schema structure (key names, nesting)
- `--init-config` output format and comment style
- Debug output formatting for `--debug score` and `--debug config`
- Error messages and validation for invalid config values

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

*Phase: 03-config-strategies-complete-cli*
*Context gathered: 2026-02-27*
