# Milestones
## v3.0 Integrated Navigation (Shipped: 2026-03-02)

**Phases:** 7-9 (3 phases, 3 plans, 6 tasks + 6 quick tasks)
**Timeline:** 2 days (2026-03-01 → 2026-03-02)
**Git range:** `d7c209a` → `40b732b` (61 commits)
**LOC:** 4,197 C# total (+670 lines net)

**Delivered:** Daemon-native directional focus navigation via CAPSLOCK + arrow/WASD hotkeys, eliminating the AutoHotkey dependency for window switching, with persistent overlay chaining and CAPS+number window selection.

**Key accomplishments:**
1. Direction key interception/suppression via WH_KEYBOARD_LL hook — arrows + WASD silenced while CAPSLOCK held
2. Full in-daemon navigation pipeline — CAPSLOCK + direction fires focus switch identical to CLI behavior
3. Overlay chaining — overlay persists and refreshes through sequential directional moves while CAPSLOCK held
4. White foreground window border for visual orientation while CAPSLOCK held
5. CAPS+number window selection with position-stable overlay labels
6. Runtime config reload — config changes take effect immediately without daemon restart

**Requirements:** 10/10 satisfied (HOTKEY-01 through HOTKEY-04, NAV-01 through NAV-03, CHAIN-01 through CHAIN-03)

**Tech debt:** 2 minor items (DPI manifest warning, dual-config pattern). See `milestones/v3.0-MILESTONE-AUDIT.md`.

---


## v2.0 Overlay Preview (Shipped: 2026-03-01)

**Phases:** 4-6 (3 phases, 6 plans, 16 tasks)
**Timeline:** 3 days (2026-02-26 → 2026-03-01)
**Git range:** `feat(04-01)` → `feat(06-02)` (41 commits)
**LOC:** 3,298 C# total (7,206 lines added)

**Delivered:** A persistent background daemon that renders directional overlay previews (colored borders) on candidate windows while CAPSLOCK is held, using a low-level keyboard hook and layered Win32 windows.

**Key accomplishments:**
1. WH_KEYBOARD_LL keyboard hook daemon with CAPSLOCK detection, suppression, and single-instance mutex (replace semantics)
2. Win32 layered-window overlay stack with GDI RoundRect BorderRenderer and premultiplied-alpha DIB compositing
3. OverlayOrchestrator wiring CapsLock hold/release to directional navigation scoring and overlay show/hide via STA dispatch
4. ForegroundMonitor (SetWinEventHook) triggering instant overlay reposition when foreground window changes
5. Instant show/hide transitions, stale-frame fix, left/right overlap prevention, and monitor-edge clamping
6. Complete `focus daemon` CLI with tray icon, sleep/wake recovery, CAPSLOCK LED force-off, and ordered shutdown

**Requirements:** 17/17 satisfied (DAEMON-01 through DAEMON-06, OVERLAY-01 through OVERLAY-05, RENDER-01 through RENDER-03, CFG-05 through CFG-07)

**Tech debt:** 5 items (2 low-severity code, 2 low-severity documentation, 1 info-level process). See `milestones/v2.0-MILESTONE-AUDIT.md`.

---

## v1.0 CLI (Shipped: 2026-02-28)

**Phases:** 1-3 (3 phases, 6 plans)
**Requirements:** 28/28 satisfied

**Delivered:** A lightweight C# CLI tool enabling directional window focus navigation on Windows — invoked via AutoHotkey hotkeys (`focus left/right/up/down`), switches focus to the most geometrically appropriate window using configurable weighting strategies.

**Key accomplishments:**
1. Win32 window enumeration pipeline with Alt+Tab filtering, UWP dedup, and DPI-aware bounds
2. Directional navigation scoring engine with three weighting strategies (balanced, strong-axis-bias, closest-in-direction)
3. Focus activation with SetForegroundWindow + SendInput ALT bypass for AHK invocation
4. JSON config system with strategy, wrap behavior, exclude list, and CLI override support
5. Complete debug surface (enumerate, score, config) and silent-by-default output

---

