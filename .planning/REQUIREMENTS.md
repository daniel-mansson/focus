# Requirements: Window Focus Navigation

**Defined:** 2026-02-26
**Core Value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

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
- [ ] **FOCUS-02**: Tool supports configurable wrap-around behavior (wrap / no-op / beep)

### Configuration & CLI

- [x] **CFG-01**: Tool reads settings from a JSON config file
- [ ] **CFG-02**: User invokes tool with direction argument (e.g., `focus left`)
- [ ] **CFG-03**: User can override config settings via CLI flags
- [x] **CFG-04**: Config file supports strategy, wrap behavior, and exclude list settings

### Output & Integration

- [ ] **OUT-01**: Tool is silent by default (no output on success)
- [x] **OUT-02**: Tool returns meaningful exit codes (0=switched, 1=no candidate, 2=error)
- [ ] **OUT-03**: User can enable verbose/debug output showing scored candidates via --verbose flag

### Debug & Testing

- [x] **DBG-01**: User can run `--debug enumerate` to list all detected windows with their properties (hwnd, title, bounds, cloaked status)
- [ ] **DBG-02**: User can run `--debug score <direction>` to show all candidates with their scores without switching focus
- [ ] **DBG-03**: User can run `--debug config` to show resolved config (defaults + file + overrides)

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Enhanced Filtering

- **ENUM-08**: Tool supports exclude list by window class name (for UWP shared hosts)

### Performance

- **PERF-01**: Tool published as Native AOT single-file binary for sub-20ms startup

### Desktop Integration

- **DESK-01**: Tool filters windows by current virtual desktop (IVirtualDesktopManager)

### Advanced Configuration

- **CFG-05**: User can define custom numeric weight parameters per strategy in config

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| GUI / system tray | Contradicts stateless CLI design — adds persistent process, IPC, maintenance burden |
| Background daemon / service mode | Stateless per-invocation model is core to the design; AOT solves startup latency if needed |
| Window tiling / layout management | Entirely different problem domain requiring persistent state and deep Win32 hooks |
| Focus-follows-mouse | Requires persistent background process with mouse hook; incompatible with CLI design |
| Linux / macOS support | Windows-specific by design (Win32 API) |
| Window resizing or moving | Only focus switching — pair with FancyWM/Komorebi for layout |
| Window title/class-based focus (name-based jumping) | Different tool category; use AHK WinActivate for this |
| Focus history tracking | Hidden state = unreproducible behavior; use geometric scoring only |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

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
| FOCUS-02 | Phase 3 | Pending |
| CFG-01 | Phase 3 | Complete |
| CFG-02 | Phase 3 | Pending |
| CFG-03 | Phase 3 | Pending |
| CFG-04 | Phase 3 | Complete |
| OUT-01 | Phase 3 | Pending |
| OUT-02 | Phase 2 | Complete |
| OUT-03 | Phase 3 | Pending |
| DBG-01 | Phase 1 | Complete |
| DBG-02 | Phase 3 | Pending |
| DBG-03 | Phase 3 | Pending |

**Coverage:**
- v1 requirements: 28 total
- Mapped to phases: 28
- Unmapped: 0 ✓

---
*Requirements defined: 2026-02-26*
*Last updated: 2026-02-27 after plan 02-02 completion (FOCUS-01, OUT-02 marked complete)*
