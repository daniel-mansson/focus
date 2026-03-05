# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v4.0 — System Tray & Settings UI

**Shipped:** 2026-03-05
**Phases:** 3 | **Plans:** 3 | **Quick Tasks:** 1

### What Was Built
- Custom focus-bracket ICO icon (16/20/24/32px PNG frames) generated from drawing primitives and embedded as assembly resource
- DaemonStatus class with live hook status, uptime tracking, and last action recording through navigation pipeline
- Three-group tray context menu with live status labels (hook, uptime, last action), Settings, Restart Daemon, and Exit
- WinForms settings form with About, Navigation, Grid & Snapping, Overlays (color swatches + shared opacity), and Keybindings sections
- Atomic config save via File.Replace with temp-file swap pattern; File.Move fallback for fresh installs
- Single-instance non-modal form pattern integrated into tray menu with Dispose cleanup

### What Worked
- Code-only WinForms (no designer/resx) with FlowLayoutPanel — compact, DPI-aware, no generated code to manage
- One plan per phase was sufficient — each phase was well-scoped and executed in 2-8 minutes
- Plain mutable DaemonStatus class (STA-thread-only, no locking) was simpler and correct — avoided over-engineering concurrency
- ContextMenuStrip.Opening event for live label refresh eliminated all stale-data bugs
- Quick task for Apply button UX change was the right granularity — small, tracked, atomic

### What Was Inefficient
- Icon generator approach (standalone script) works but commits output binary — pre-build MSBuild target was rejected due to build latency and file-lock risks, but it means focus.ico is a committed artifact
- Shared opacity across four direction colors is lossy if manually set to different alphas in JSON — acceptable tradeoff for simpler UI but worth noting

### Patterns Established
- ICO binary encoding: hand-written BinaryWriter with PNG frames — zero dependency icon generation
- EmbeddedResource with LogicalName: eliminates namespace-prefix guessing for GetManifestResourceStream
- Single-instance form pattern: nullable field + IsDisposed check + BringToFront
- ARGB hex parsing: uint.Parse + bit shifts for decomposition, ToHexColor for recomposition
- Atomic config write: WriteAllText(.tmp) then File.Replace; File.Move fallback for fresh install

### Key Lessons
1. Code-only WinForms with layout panels is viable for settings forms — no designer needed for ~300 lines of UI code
2. STA-thread-only state holders avoid concurrency complexity — if all access is on one thread, don't add locks
3. ContextMenuStrip.Opening is the correct event for live menu data — never cache status labels
4. File.Replace is not safe for fresh installs (throws if target doesn't exist) — always provide File.Move fallback
5. Tray icon ghost prevention: set Visible = false before exit/restart to avoid stale icons in notification area

### Cost Observations
- Model mix: ~50% sonnet, ~40% opus, ~10% haiku (estimated)
- Sessions: ~2
- Notable: Fastest milestone — 3 phases, 3 plans, all completed in <15 minutes of execution time total. Well-scoped phases with clear research and single-plan structure executed cleanly.

---

## Milestone: v3.1 — Window Management

**Shipped:** 2026-03-03
**Phases:** 3 | **Plans:** 8 | **Quick Tasks:** 1

### What Was Built
- GridCalculator with per-monitor grid computation, configurable per-axis fractions (16x12), and directional snap variants (Floor/Ceiling)
- TAB interception and left-side modifier detection (VK_LSHIFT/VK_LCONTROL) for Move/Grow/Shrink mode routing
- WindowManagerService with dual-rect coordinate pattern, snap-first grid stepping, boundary clamping, maximized/elevated window guards
- Cross-monitor window transitions via adjacent monitor detection (overlapping-range algorithm on rcMonitor edges)
- Mode-aware overlay indicators — amber borders/arrows for Move, cyan for Grow — using DIB-rasterized triangle renderer (ArrowRenderer)
- Overlay refresh tracking through all move/resize operations with navigate-target suppression

### What Worked
- Dual-rect coordinate pattern (GetWindowRect for SetWindowPos, DwmGetWindowAttribute for overlay) prevented entire classes of border-offset bugs
- Gap closure plans (11-02, 11-03, 10-03) were effective — UAT found real issues that formal verification caught and fixed systematically
- GridCalculator being Win32-free (explicit int params, no RECT structs) made reasoning about math straightforward
- Per-axis grid fractions (16x12) produced near-square grid cells on 16:9 monitors — good UX decision
- Mode-at-event-time pattern eliminated race conditions between modifier release and event processing
- Quick task for modifier remap (grow/shrink to LShift) was the right granularity for a UX binding change

### What Was Inefficient
- Phase 11 needed 3 gap closure plans after initial plan — UAT revealed directional snap, shrink edge inversion, and overlay refresh issues that could have been anticipated
- ROADMAP.md phase detail checkboxes (11-02, 11-03, 12-02) were not updated to [x] after completion — cosmetic but suggests the update step needs automation
- Some decisions in STATE.md accumulated heavily (25+ entries) — milestone completion should trim these more aggressively

### Patterns Established
- Dual-rect coordinate pattern: GetWindowRect for SetWindowPos input, DwmGetWindowAttribute for visual overlay positioning only
- Snap-first pattern: check IsAligned on moving edge; if aligned step by grid, if not snap to nearest grid line
- Directional snap: NearestGridLineFloor for edges moving toward smaller values, NearestGridLineCeiling for edges moving toward larger values
- Silent no-op guards: IsZoomed (maximized) returns silently, elevated window SetWindowPos failure is silent
- Post-computation no-op guard: verify dimensions actually changed before calling SetWindowPos
- DIB pixel-write triangle rasterizer: bounding-box scan + cross-product sign test for filled triangles
- _currentMode set before _staDispatcher.Invoke: worker thread writes, STA thread reads inside Invoke — no lock needed

### Key Lessons
1. Gap closure plans are a strength, not a weakness — UAT-driven corrections caught real bugs (directional snap, shrink edge mapping) that would have been user-facing
2. The dual-rect coordinate pattern is essential for any Win32 window manipulation — mixing GetWindowRect and DwmGetWindowAttribute causes subtle positioning errors
3. Per-axis grid fractions are worth the small config complexity — uniform grid cells improve UX significantly on non-square monitors
4. Cross-monitor transitions need clean separation between adjacency detection (rcMonitor) and placement math (rcWork) — mixing them causes taskbar overlap
5. DIB pixel-write rendering is a viable alternative to GDI path-based rendering for simple geometric overlays — produces consistent premultiplied alpha

### Cost Observations
- Model mix: ~60% opus, ~30% sonnet, ~10% haiku (estimated)
- Sessions: ~3
- Notable: 8 plans across 3 phases in 2 days. Gap closure plans (10-03, 11-02, 11-03) added 3 plans beyond initial scope but were all quick to execute (~3-4 min each). Total milestone was fast despite being the most complex (window management + overlays + cross-monitor).

---

## Milestone: v3.0 — Integrated Navigation

**Shipped:** 2026-03-02
**Phases:** 3 | **Plans:** 3 | **Quick Tasks:** 6

### What Was Built
- Direction key interception/suppression in WH_KEYBOARD_LL hook — arrows + WASD silenced while CAPSLOCK held
- Full in-daemon navigation pipeline — CAPSLOCK + direction fires same scoring engine as CLI
- Overlay chaining — overlay persists and refreshes through sequential directional moves
- White foreground window border for visual orientation
- CAPS+number window selection with GDI text labels and position-stable numbering
- Runtime config reload on every keypress (zero-restart workflow)

### What Worked
- Phase 9 (Overlay Chaining) turned out to already work with existing v2.0 architecture — no new code was needed, just verification
- Quick task workflow (`/gsd:quick`) was highly effective for small UX improvements (6 tasks in ~40 min total)
- Fresh config load per keypress was a good user-driven decision — immediate feedback loop for tweaking settings
- Hook point pattern (Phase 7 creates no-op, Phase 8 fills it in) provided clean separation of concerns
- Milestone audit caught that Phase 9 was already working before formal execution began, saving an entire planning/execution cycle

### What Was Inefficient
- Phase 9 was planned as a separate phase with formal plans, but the overlay chaining behavior already worked from v2.0's ShowOverlaysForCurrentForeground architecture — could have been verified earlier
- STATE.md accumulated a lot of context (quick task decisions, key decisions) that became stale after milestone completion — suggests trimming accumulated context at milestone boundaries
- Dual-config pattern (startup config vs per-keypress config) was noted as tech debt but is actually by-design — audit flagged it unnecessarily

### Patterns Established
- Quick task workflow: `/gsd:quick` for atomic, tracked improvements outside formal phase plans
- Hook point pattern: add no-op method to orchestrator, wire callback, implement in future phase
- HashSet<uint> for key repeat suppression — cleared on keyup and ResetState() for sleep/wake safety
- GDI text on layered windows requires alpha fixup pass (premultiplied alpha from ClearType)
- Position-stable overlay numbering: sort full filtered window list including active window

### Key Lessons
1. Audit before completing milestones — the v3.0 audit correctly identified that Phase 9 was already working, saving a full planning/execution cycle
2. Quick tasks are the right granularity for UX polish — small, atomic, tracked, but without the overhead of full phase planning
3. Architecture can satisfy future requirements without new code — v2.0's overlay refresh-on-foreground-change already enabled v3.0 chaining
4. Fresh config load per interaction is a good pattern for desktop tools — users expect settings to take effect immediately
5. Runtime key repeat suppression needs explicit state cleanup on sleep/wake transitions

### Cost Observations
- Model mix: ~60% opus, ~30% sonnet, ~10% haiku (estimated)
- Sessions: ~4
- Notable: Phase 9 required zero code changes — existing architecture already chained. Quick tasks took 1-20 min each. Total milestone was 2 days.

---

## Milestone: v2.0 — Overlay Preview

**Shipped:** 2026-03-01
**Phases:** 3 | **Plans:** 6 | **Tasks:** 16

### What Was Built
- Background daemon (`focus daemon`) with WH_KEYBOARD_LL keyboard hook, CAPSLOCK detection/suppression, single-instance mutex
- Win32 layered-window overlay stack with GDI RoundRect BorderRenderer and premultiplied-alpha DIB compositing
- OverlayOrchestrator wiring CapsLock hold/release to directional navigation scoring and overlay show/hide via STA dispatch
- ForegroundMonitor (SetWinEventHook) for instant overlay reposition when foreground window changes
- Complete daemon lifecycle: tray icon, sleep/wake recovery, CAPSLOCK LED force-off, ordered shutdown

### What Worked
- CsWin32 P/Invoke code generation continued to be effective — just append API names to NativeMethods.txt
- Phase decomposition (daemon core → overlay rendering → integration wiring) kept each phase focused and testable
- Human verification checkpoints caught real bugs: GDI alpha detection, stale frame flash, overlay overlap
- Atomic task commits made rollback easy when deviations were found
- Plan 01 of each phase established the building blocks, Plan 02 wired and verified — consistent 2-plan pattern worked well

### What Was Inefficient
- Fade animation was fully implemented then removed after user testing — could have done instant-first and added fade only if requested
- CsWin32 namespace discovery (HWINEVENTHOOK in UI.Accessibility, KBDLLHOOKSTRUCT not transitive) required trial-and-error compilation — could be cataloged
- Some SUMMARY frontmatter fields (requirements-completed) weren't populated by the summary-extract tool, requiring manual extraction

### Patterns Established
- Static delegate field for Win32 hooks (HOOKPROC, WINEVENTPROC) — GC collects local variables after method returns
- GDI-into-DIB pixel detection: check RGB via `(pixel & 0x00FFFFFF) != 0`, never alpha channel
- UpdateLayeredWindow + premultiplied-alpha DIB as the exclusive compositing path for overlay windows
- Control.Invoke with volatile _shutdownRequested guard for cross-thread STA dispatch
- Out-param pattern for STA-thread object creation (DaemonApplicationContext fills `out` before returning)
- Channel<KeyEvent> for hook-to-worker thread communication (TryWrite in callback, ReadAllAsync on worker)

### Key Lessons
1. Always test visual UI features with humans before committing to animation/transition approaches — the user preferred instant over fade, saving ongoing complexity
2. CsWin32 struct dependencies are not transitive — always verify by building, and add missing types to NativeMethods.txt explicitly
3. Premultiplied alpha compositing has subtle GDI pitfalls — document the exact pixel detection pattern for any future renderer work
4. Win32 overlay windows need careful attention to focus stealing (WS_EX_NOACTIVATE, SWP_NOACTIVATE on every call)
5. Daemon shutdown ordering matters — join STA thread before disposing hooks to avoid use-after-free

### Cost Observations
- Model mix: ~70% opus, ~20% sonnet, ~10% haiku (estimated)
- Plan execution was fast: 3-60 min per plan depending on human verification needs
- Notable: Phase 4 and 6 Plan 01s completed in ~3 min each — well-structured plans with clear task boundaries execute quickly

---

## Milestone: v1.0 — CLI

**Shipped:** 2026-02-28
**Phases:** 3 | **Plans:** 6

### What Was Built
- Win32 window enumeration pipeline with Alt+Tab filtering, UWP dedup, DPI-aware bounds
- Directional navigation scoring engine with six weighting strategies
- Focus activation with SetForegroundWindow + SendInput ALT bypass
- JSON config system with strategy, wrap behavior, exclude list, CLI override support
- Complete debug surface (enumerate, score, config) and silent-by-default output

### What Worked
- CsWin32 for P/Invoke generation — minimal boilerplate, reliable Win32 interop
- Three-phase progression (enumerate → navigate → configure) built confidence incrementally
- Debug commands (--debug enumerate, score, config) were essential for development validation

### What Was Inefficient
- No automated tests — all verification was manual
- Some quick tasks (strategies 4-6) could have been planned as a single phase instead of individual tasks

### Key Lessons
1. CsWin32 with NativeMethods.txt is the right approach for Win32 interop in .NET — avoids manual DllImport declarations
2. DPI awareness must be set via manifest, not runtime API — DWMWA_EXTENDED_FRAME_BOUNDS gives accurate physical pixel bounds
3. Debug commands are essential infrastructure, not optional polish

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Phases | Plans | Key Change |
|-----------|--------|-------|------------|
| v1.0 | 3 | 6 | Established CsWin32 + NativeMethods.txt pattern, debug-first approach |
| v2.0 | 3 | 6 | Added human verification checkpoints, systematic deviation tracking |
| v3.0 | 3 | 3 | Quick task workflow for UX polish, milestone audit caught pre-satisfied requirements |
| v3.1 | 3 | 8 | Gap closure plans as first-class workflow, dual-rect coordinate pattern, DIB pixel-write rendering |
| v4.0 | 3 | 3 | Single-plan phases, code-only WinForms, fastest milestone (<15 min total execution) |

### Top Lessons (Verified Across Milestones)

1. CsWin32 NativeMethods.txt entries must be explicit — transitive dependencies are never generated (verified in Phase 1, 4, 5, 6, 10)
2. Human verification catches real bugs that compilation alone misses — GDI alpha, overlay overlap, stale frames, directional snap (verified in Phase 5, 6, 7, 8, 11, 12)
3. Two-plan phase structure (build → wire+verify) consistently produces focused, testable increments (verified across all 12 phases)
4. Milestone audit before completion saves wasted effort — Phase 9 was already working from v2.0 architecture (verified in v3.0)
5. Quick tasks (`/gsd:quick`) are the right tool for UX polish between formal phases (verified in v3.0, v3.1)
6. Gap closure plans are a natural part of execution, not a sign of poor planning — UAT-driven corrections catch real bugs systematically (verified in v3.1 — 3 gap closure plans)
7. Single-plan phases work when scope is tight — v4.0 proved that 1 plan per phase with thorough research can execute cleanly without gap closure (verified in v4.0 — 0 deviations in Phase 15)
8. Code-only WinForms with layout panels is viable for settings UIs — avoids designer files and handles DPI scaling (verified in v4.0 Phase 15)
