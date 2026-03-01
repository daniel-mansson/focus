---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: Overlay Preview
status: unknown
last_updated: "2026-03-01T17:35:49.808Z"
progress:
  total_phases: 6
  completed_phases: 6
  total_plans: 12
  completed_plans: 12
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-28)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** Phase 6 — Navigation Integration (Overlay Wiring)

## Current Position

Phase: 6 of 6 (Navigation Integration) — COMPLETE
Plan: 2 of 2 complete (06-02-PLAN.md done)
Status: Phase 6 complete — v2.0 Overlay Preview milestone fully shipped. CAPSLOCK hold shows directional colored borders, release hides instantly, foreground reposition works.
Last activity: 2026-03-01 - Completed 06-02: Daemon lifecycle wiring, ForceCapsLockOff, instant overlay show/hide, stale-frame fix, bounds clamping — all 8 human scenarios passed

Progress: [██████████] 100% (v2.0 milestone complete, 6/6 phases done)

## Performance Metrics

**Velocity (v1.0 baseline):**
- Total plans completed: 6 (v1.0)
- Average duration: unknown
- Total execution time: unknown

**By Phase (v1.0):**

| Phase | Plans | Status |
|-------|-------|--------|
| 1. Win32 Foundation | 2/2 | Complete |
| 2. Navigation Pipeline | 2/2 | Complete |
| 3. Config, Strategies & CLI | 2/2 | Complete |

**By Phase (v2.0):**

| Phase | Plans | Status | Duration |
|-------|-------|--------|----------|
| 4. Daemon Core | 2/2 | Complete | Plan 01: 3min, Plan 02: 16min |
| 5. Overlay Rendering | 2/2 | Complete | Plan 01: 7min, Plan 02: 30min |
| 6. Overlay Wiring | 2/2 | Complete | Plan 01: 3min, Plan 02: ~60min |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Carried from v1.0 + v2.0 research:

- Setup: Use CsWin32 0.3.269 for P/Invoke — append ~20 API names to NativeMethods.txt for v2.0
- Phase 1: Always use DWMWA_EXTENDED_FRAME_BOUNDS for coordinates
- Phase 4: Hook delegate MUST be stored in a static field — GC collects local variables after method returns
- Phase 4: WH_KEYBOARD_LL requires a dedicated STA thread running GetMessage/DispatchMessage — Thread.Sleep is not sufficient
- Phase 4: All enumeration/scoring MUST run on worker thread consuming Channel<KeyEvent>, not in hook callback (300ms timeout)
- Phase 5: Use UpdateLayeredWindow + premultiplied-alpha DIB exclusively — never mix with SetLayeredWindowAttributes on same HWND
- Phase 5: Overlay HWNDs created at startup and reused via ShowWindow — not created/destroyed per CAPSLOCK press
- Phase 5: WS_EX_NOACTIVATE + SWP_NOACTIVATE on every SetWindowPos — overlays must never steal focus
- Phase 5: RegisterClassEx requires Marshal.GetHINSTANCE(typeof(T).Module) — new HINSTANCE(0) causes error 87
- Phase 4 (04-01): TargetFramework must be net8.0-windows (not net8.0) when UseWindowsForms=true — NETSDK1136 enforces this
- Phase 4 (04-01): KBDLLHOOKSTRUCT must be explicitly in NativeMethods.txt — CsWin32 does not generate it as a transitive dependency of SetWindowsHookEx
- Phase 4 (04-01): SetWindowsHookEx with SafeHandle hmod returns UnhookWindowsHookExSafeHandle — use Dispose() instead of PInvoke.UnhookWindowsHookEx directly
- Phase 4 (04-02): Hook installed in DaemonApplicationContext constructor — Application.Run blocks until ExitThread; constructor runs before pump starts so hook is ready when first messages arrive
- Phase 4 (04-02): cts.Token.Register(() => Application.ExitThread()) for cross-thread STA shutdown — works for both Ctrl+C and tray Exit paths without needing Invoke or thread sync
- Phase 4 (04-02): Ordered cleanup: staThread.Join -> hook.Uninstall+Dispose -> channel.Writer.Complete -> consumerTask.Wait(500ms) -> mutex.Release
- Phase 4 (04-02): Inner NativeWindow pattern for WM_POWERBROADCAST — create sealed class inheriting NativeWindow, call CreateHandle(new CreateParams()), override WndProc
- [Phase 05-overlay-windows]: Raw PCWSTR overload for CreateWindowEx/UnregisterClass — HINSTANCE is not SafeHandle
- [Phase 05-overlay-windows]: Per-instance unique window class name (FocusOverlay_XXXXXXXX) to avoid registration conflicts
- [Phase 05-overlay-windows]: Marshal.SizeOf<WNDCLASSEXW> instead of unsafe sizeof — WNDCLASSEXW is a managed partial struct
- [Phase 05-overlay-windows]: DefWindowProc must be listed explicitly in NativeMethods.txt — not auto-generated transitively
- Phase 5 (05-02): GDI RoundRect draws RGB but leaves alpha at 0x00 in DIBs — detect drawn pixels via (pixel & 0x00FFFFFF) != 0, not pixAlpha != 0
- Phase 5 (05-02): Application.DoEvents + Thread.Sleep(16) as message pump for debug overlay command — no full WinForms Application.Run required
- Phase 5 (05-02): Background thread with ManualResetEventSlim for Console.ReadKey without blocking the message pump thread
- Quick-7: Direct pixel buffer writes (not GDI) required for selective directional edge rendering — GetPixelAlpha helper with primary-edge-first ordering minimizes sqrt calls via corner quadrant bounding box
- Phase 6 (06-01): HWINEVENTHOOK is in Windows.Win32.UI.Accessibility namespace despite file being named Windows.Win32.HWINEVENTHOOK.g.cs — use `using global::Windows.Win32.UI.Accessibility`
- Phase 6 (06-01): ForegroundMonitor delegate (WINEVENTPROC) must be pinned with GCHandle.Alloc — same pattern as hook delegate, but instance field instead of static field
- Phase 6 (06-01): Control.Invoke pattern for STA dispatch: volatile _shutdownRequested guard + try/catch ObjectDisposedException+InvalidOperationException around every Invoke call
- Phase 6 (06-01): Solo-window dim indicator color 0x30AAAAAA (~19% opacity neutral gray) — visible but subtle, confirms daemon activity when no navigation candidates exist
- Phase 6 (06-02): Fade animation removed entirely — instant show/hide preferred over 100ms fade-in/80ms fade-out after human testing; all WinForms Timer fade machinery removed
- Phase 6 (06-02): OverlayWindow.Show split into Reposition+Paint+Show to eliminate stale frame flash when re-activating an overlay with new bounds
- Phase 6 (06-02): Left/right overlay bounds expanded by 3px in all directions to prevent visual overlap with up/down border overlays on the same window
- Phase 6 (06-02): Overlay bounds clamped to monitor edges via MonitorFromWindow+GetMonitorInfo — handles partially off-screen source windows without rendering artifacts
- Phase 6 (06-02): Out-param pattern for STA-thread object creation — DaemonApplicationContext fills `out OverlayOrchestrator orchestrator` before returning, allowing DaemonCommand.Run to hold reference for ordered shutdown

### Pending Todos

None yet.

### Blockers/Concerns

- RESOLVED (04-01): CsWin32 HOOKPROC delegate interop verified — FreeLibrarySafeHandle pattern compiles, KBDLLHOOKSTRUCT must be explicitly listed in NativeMethods.txt
- RESOLVED (05-01): Premultiplied alpha compositing implemented in BorderRenderer — GdiFlush + pixel-level multiplication pattern established
- v1.0 carry: UWP enumeration behavior on Windows 11 24H2 must be validated with real windows
- v1.0 carry: SendInput + ALT bypass must be validated specifically via AHK invocation

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 6 | Add debug command showing all directions with overlays | 2026-03-01 | cbd6f49 | [6-add-debug-command-showing-all-directions](./quick/6-add-debug-command-showing-all-directions/) |
| 7 | Directional edge overlay with corner fade-out | 2026-03-01 | 5621b71 | [7-directional-edge-overlay-with-corner-fad](./quick/7-directional-edge-overlay-with-corner-fad/) |

## Session Continuity

Last session: 2026-03-01T17:30:00Z
Stopped at: Completed 06-02-PLAN.md — Daemon lifecycle wiring complete, all 8 human test scenarios passed, v2.0 Overlay Preview milestone COMPLETE. No further plans in roadmap.
Resume file: None
