# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

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

### Top Lessons (Verified Across Milestones)

1. CsWin32 NativeMethods.txt entries must be explicit — transitive dependencies are never generated (verified in Phase 1, 4, 5, 6)
2. Human verification catches real bugs that compilation alone misses — GDI alpha, overlay overlap, stale frames (verified in Phase 5, 6)
3. Two-plan phase structure (build → wire+verify) consistently produces focused, testable increments (verified across all 6 phases)
