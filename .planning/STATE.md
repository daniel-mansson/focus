---
gsd_state_version: 1.0
milestone: v2.0
milestone_name: Overlay Preview
status: roadmap_ready
last_updated: "2026-03-01T07:34:10Z"
progress:
  total_phases: 3
  completed_phases: 0
  total_plans: 6
  completed_plans: 1
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-28)

**Core value:** Given a direction, reliably switch focus to the most intuitive window in that direction — fast enough for hotkey use, accurate enough to feel natural.
**Current focus:** Phase 4 — Daemon Core (v2.0 Overlay Preview)

## Current Position

Phase: 4 of 6 (Daemon Core)
Plan: 1 of 2 complete (04-01-PLAN.md done, 04-02-PLAN.md next)
Status: In progress
Last activity: 2026-03-01 — Plan 04-01 executed: daemon core components built

Progress: [█░░░░░░░░░] ~17% (v2.0 milestone, 1/6 plans complete)

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
| 4. Daemon Core | 1/2 | In progress | Plan 01: 3min |
| 5. Overlay Rendering | 0/2 | Pending | — |
| 6. Overlay Wiring | 0/2 | Pending | — |

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

### Pending Todos

None yet.

### Blockers/Concerns

- RESOLVED (04-01): CsWin32 HOOKPROC delegate interop verified — FreeLibrarySafeHandle pattern compiles, KBDLLHOOKSTRUCT must be explicitly listed in NativeMethods.txt
- Phase 5 research flag: Premultiplied alpha compositing failure is subtle — a rendering spike against a known ARGB value is recommended before full OverlayWindow implementation
- v1.0 carry: UWP enumeration behavior on Windows 11 24H2 must be validated with real windows
- v1.0 carry: SendInput + ALT bypass must be validated specifically via AHK invocation

## Session Continuity

Last session: 2026-03-01T07:34:10Z
Stopped at: Completed 04-01-PLAN.md — daemon core components (KeyEvent, KeyboardHookHandler, CapsLockMonitor, DaemonMutex)
Resume file: None
