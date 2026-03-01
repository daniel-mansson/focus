# Requirements: Window Focus Navigation

**Defined:** 2026-02-26
**Core Value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## v1 Requirements (Complete)

All v1 requirements shipped and validated. See v1.0 milestone archive for details.

### Window Enumeration & Filtering

- [x] **ENUM-01**: Tool enumerates all top-level windows via EnumWindows
- [x] **ENUM-02**: Tool filters out hidden windows (IsWindowVisible check)
- [x] **ENUM-03**: Tool filters out cloaked windows (DWMWA_CLOAKED check)
- [x] **ENUM-04**: Tool filters out minimized windows (IsIconic check)
- [x] **ENUM-05**: Tool gets accurate visible bounds via DWMWA_EXTENDED_FRAME_BOUNDS
- [x] **ENUM-06**: Tool uses UWP-safe window filtering (Alt+Tab algorithm)
- [x] **ENUM-07**: Tool supports user-configurable exclude list by process name with regex/wildcard patterns

### Directional Navigation

- [x] **NAV-01**: User can navigate focus left
- [x] **NAV-02**: User can navigate focus right
- [x] **NAV-03**: User can navigate focus up
- [x] **NAV-04**: User can navigate focus down
- [x] **NAV-05**: Navigation works across multiple monitors via virtual screen coordinates
- [x] **NAV-06**: Tool uses DPI-aware coordinates (PerMonitorV2 manifest)
- [x] **NAV-07**: Tool supports "balanced" weighting strategy
- [x] **NAV-08**: Tool supports "strong-axis-bias" weighting strategy
- [x] **NAV-09**: Tool supports "closest-in-direction" weighting strategy

### Focus Activation

- [x] **FOCUS-01**: Tool switches focus using SetForegroundWindow with SendInput ALT bypass
- [x] **FOCUS-02**: Tool supports configurable wrap-around behavior (wrap / no-op / beep)

### Configuration & CLI

- [x] **CFG-01**: Tool reads settings from a JSON config file
- [x] **CFG-02**: User invokes tool with direction argument (e.g., `focus left`)
- [x] **CFG-03**: User can override config settings via CLI flags
- [x] **CFG-04**: Config file supports strategy, wrap behavior, and exclude list settings

### Output & Integration

- [x] **OUT-01**: Tool is silent by default (no output on success)
- [x] **OUT-02**: Tool returns meaningful exit codes (0=switched, 1=no candidate, 2=error)
- [x] **OUT-03**: User can enable verbose/debug output showing scored candidates via --verbose flag

### Debug & Testing

- [x] **DBG-01**: User can run `--debug enumerate` to list all detected windows with their properties
- [x] **DBG-02**: User can run `--debug score <direction>` to show all candidates with their scores
- [x] **DBG-03**: User can run `--debug config` to show resolved config

## v2.0 Requirements

Requirements for overlay preview daemon milestone. Each maps to roadmap phases.

### Daemon Infrastructure

- [ ] **DAEMON-01**: User can start a background daemon via `focus daemon` that persists until explicitly stopped
- [x] **DAEMON-02**: Daemon installs WH_KEYBOARD_LL hook and detects CAPSLOCK held/released state
- [ ] **DAEMON-03**: Daemon debounces CAPSLOCK hold with configurable activation delay before showing overlay
- [x] **DAEMON-04**: Daemon enforces single instance via named mutex (second launch exits with error)
- [ ] **DAEMON-05**: Daemon cleans up overlay windows and unhooks keyboard hook on exit/crash
- [x] **DAEMON-06**: Daemon filters LLKHF_INJECTED key events to prevent AHK-triggered overlay flicker

### Overlay Rendering

- [ ] **OVERLAY-01**: Overlay renders colored borders on the top-ranked target window for each of the 4 directions simultaneously
- [ ] **OVERLAY-02**: Overlay windows are click-through, always-on-top, excluded from taskbar/Alt+Tab, and excluded from navigation enumeration
- [ ] **OVERLAY-03**: Overlay dismisses immediately when CAPSLOCK is released
- [ ] **OVERLAY-04**: Overlay updates target positions when foreground window changes while CAPSLOCK is held
- [ ] **OVERLAY-05**: Overlay gracefully handles directions with no candidate (no overlay rendered for that direction)

### Renderer System

- [ ] **RENDER-01**: IOverlayRenderer interface defines the contract for overlay rendering
- [ ] **RENDER-02**: Default border renderer draws colored borders using Win32 GDI (no WPF/WinForms)
- [ ] **RENDER-03**: Renderer selection is driven by config (overlayRenderer field)

### Configuration

- [ ] **CFG-05**: Per-direction overlay colors configurable in JSON config (left/right/up/down, hex ARGB)
- [ ] **CFG-06**: Activation delay configurable in JSON config (overlayDelayMs, default ~150ms)
- [ ] **CFG-07**: Overlay renderer name configurable in JSON config (default: "border")

## Future Requirements

Deferred to future release. Tracked but not in current roadmap.

### Custom Renderers

- **RENDER-04**: Per-strategy custom renderer implementations (e.g., edge-matching draws connecting lines)
- **RENDER-05**: Strategy-aware renderer auto-selection based on active strategy

### Overlay Polish

- **OVERLAY-06**: Live overlay update on window layout change (new windows opened/closed while modifier held)
- **OVERLAY-07**: Configurable overlay border thickness in JSON config
- **OVERLAY-08**: DPI-aware border thickness scaling on mixed-DPI multi-monitor setups

### Enhanced Filtering

- **ENUM-08**: Tool supports exclude list by window class name (for UWP shared hosts)

### Performance

- **PERF-01**: Tool published as Native AOT single-file binary for sub-20ms startup

### Desktop Integration

- **DESK-01**: Tool filters windows by current virtual desktop (IVirtualDesktopManager)

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Animated overlay transitions (fade in/out) | Disproportionate complexity for minimal UX gain; overlay should be instant |
| Window title/content preview in overlay | DwmRegisterThumbnail complexity; colored border at window position IS the preview |
| CAPSLOCK toggle state feedback | Tool uses CAPSLOCK as modifier (hold), not toggle; conflating both confuses UX |
| Interactive/clickable overlay elements | Conflicts with WS_EX_TRANSPARENT (click-through); navigation is keyboard-only |
| System tray icon or context menu | Adds GUI layer to CLI-first tool; daemon lifecycle via `focus daemon` CLI |
| Auto-start on login | Deployment/packaging problem; document manual Startup folder approach |
| WPF/WinUI overlay rendering | Significant binary size and runtime dependencies; Win32 GDI sufficient |
| Window tiling / layout management | Entirely different problem domain |
| Focus-follows-mouse | Requires persistent mouse hook; incompatible with keyboard-first design |
| Linux / macOS support | Windows-specific by design (Win32 API) |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

### v1 (Complete)

| Requirement | Phase | Status |
|-------------|-------|--------|
| ENUM-01 | Phase 1 | Complete |
| ENUM-02 | Phase 1 | Complete |
| ENUM-03 | Phase 1 | Complete |
| ENUM-04 | Phase 1 | Complete |
| ENUM-05 | Phase 1 | Complete |
| ENUM-06 | Phase 1 | Complete |
| ENUM-07 | Phase 3 | Complete |
| NAV-01 | Phase 2 | Complete |
| NAV-02 | Phase 2 | Complete |
| NAV-03 | Phase 2 | Complete |
| NAV-04 | Phase 2 | Complete |
| NAV-05 | Phase 2 | Complete |
| NAV-06 | Phase 1 | Complete |
| NAV-07 | Phase 2 | Complete |
| NAV-08 | Phase 3 | Complete |
| NAV-09 | Phase 3 | Complete |
| FOCUS-01 | Phase 2 | Complete |
| FOCUS-02 | Phase 3 | Complete |
| CFG-01 | Phase 3 | Complete |
| CFG-02 | Phase 3 | Complete |
| CFG-03 | Phase 3 | Complete |
| CFG-04 | Phase 3 | Complete |
| OUT-01 | Phase 3 | Complete |
| OUT-02 | Phase 2 | Complete |
| OUT-03 | Phase 3 | Complete |
| DBG-01 | Phase 1 | Complete |
| DBG-02 | Phase 3 | Complete |
| DBG-03 | Phase 3 | Complete |

### v2.0

| Requirement | Phase | Status |
|-------------|-------|--------|
| DAEMON-01 | Phase 4 | Pending |
| DAEMON-02 | Phase 4 | Complete |
| DAEMON-03 | Phase 6 | Pending |
| DAEMON-04 | Phase 4 | Complete |
| DAEMON-05 | Phase 4 | Pending |
| DAEMON-06 | Phase 4 | Complete |
| OVERLAY-01 | Phase 6 | Pending |
| OVERLAY-02 | Phase 5 | Pending |
| OVERLAY-03 | Phase 6 | Pending |
| OVERLAY-04 | Phase 6 | Pending |
| OVERLAY-05 | Phase 6 | Pending |
| RENDER-01 | Phase 5 | Pending |
| RENDER-02 | Phase 5 | Pending |
| RENDER-03 | Phase 5 | Pending |
| CFG-05 | Phase 5 | Pending |
| CFG-06 | Phase 6 | Pending |
| CFG-07 | Phase 5 | Pending |

**Coverage:**
- v1 requirements: 28 total — all complete ✓
- v2.0 requirements: 17 total
- Mapped to phases: 17 ✓
- Unmapped: 0 ✓

---
*Requirements defined: 2026-02-26*
*Last updated: 2026-03-01 after milestone v2.0 roadmap creation (phases 4-6)*
