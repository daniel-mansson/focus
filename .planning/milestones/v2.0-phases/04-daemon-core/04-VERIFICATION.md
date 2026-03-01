---
phase: 04-daemon-core
verified: 2026-03-01T08:30:00Z
status: passed
score: 14/14 must-haves verified
re_verification: false
human_verification:
  - test: "CAPSLOCK LED suppression — press CAPSLOCK while daemon runs"
    expected: "CAPSLOCK LED does NOT toggle; key is fully suppressed"
    why_human: "LED state is a hardware interaction visible at the keyboard, not detectable via code inspection"
  - test: "Tray icon Exit menu causes clean shutdown"
    expected: "Tray icon disappears, process exits, no orphaned hooks remain"
    why_human: "NotifyIcon visibility and tray ghost-icon behavior require visual inspection"
  - test: "Hook fires under fullscreen app focus after 2+ minutes idle"
    expected: "CAPSLOCK events logged continuously even under a fullscreen exclusive app"
    why_human: "WH_KEYBOARD_LL timeout behavior under fullscreen apps is a runtime behavior requiring live testing"
  - test: "Sleep/wake hook recovery — suspend system and resume"
    expected: "After resume, CAPSLOCK hold/release still logs — hook was reinstalled automatically"
    why_human: "WM_POWERBROADCAST + hook reinstall requires real system sleep cycle to verify"
  - test: "Background mode console detach"
    expected: "focus daemon --background prints startup message, returns console prompt, tray icon remains"
    why_human: "FreeConsole behavior and console-detach timing require live terminal observation"
---

# Phase 4: Daemon Core Verification Report

**Phase Goal:** Users can run `focus daemon` as a persistent background process that installs a global keyboard hook, detects CAPSLOCK held/released state, and shuts down cleanly — with no overlay code yet
**Verified:** 2026-03-01T08:30:00Z
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

All truths are derived from the phase's two PLAN frontmatter `must_haves` blocks (Plan 01 + Plan 02) and cross-referenced against ROADMAP.md success criteria.

#### Plan 01 Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | KeyboardHookHandler installs WH_KEYBOARD_LL hook via CsWin32 and fires CAPSLOCK key-down/key-up events to a Channel | VERIFIED | `PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, ...)` at line 52; `_channelWriter.TryWrite(new KeyEvent(...))` at line 109 of KeyboardHookHandler.cs |
| 2 | Hook callback suppresses CAPSLOCK toggle by returning non-zero LRESULT for VK_CAPITAL events | VERIFIED | `return (LRESULT)1;` at line 114 of KeyboardHookHandler.cs; returned for all bare CAPSLOCK events that pass modifier filters |
| 3 | Hook callback filters LLKHF_INJECTED events — AHK-synthesized keys never reach the Channel | VERIFIED | `if (((uint)kbd->flags & LLKHF_INJECTED) != 0) return PInvoke.CallNextHookEx(...)` at lines 84-85 |
| 4 | Hook callback ignores CAPSLOCK when Ctrl, Shift, or Alt is held — only bare CAPSLOCK triggers detection | VERIFIED | Alt check via `LLKHF_ALTDOWN` flag (line 93), Ctrl via `GetKeyState(VK_CONTROL) & 0x8000` (line 97), Shift via `GetKeyState(VK_SHIFT) & 0x8000` (line 101) |
| 5 | CapsLockMonitor consumes Channel and logs hold/release with timestamps when verbose is enabled | VERIFIED | `await foreach (var evt in _reader.ReadAllAsync(cancellationToken))` at line 27; `Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CAPSLOCK held")` at line 57 of CapsLockMonitor.cs |
| 6 | DaemonMutex acquires a Global named mutex and kills any existing daemon process on conflict | VERIFIED | `new Mutex(false, @"Global\focus-daemon", ...)` at line 16; `Process.GetProcessesByName("focus")` + `p.Kill()` at lines 27-41 of DaemonMutex.cs |

#### Plan 02 Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 7 | Running `focus daemon` starts a persistent process with keyboard hook installed and tray icon visible | VERIFIED | DaemonCommand.Run starts STA thread with `Application.Run(new DaemonApplicationContext(...))` (line 66); DaemonApplicationContext calls `_hook.Install()` and creates NotifyIcon (lines 26-38 of TrayIcon.cs) |
| 8 | Running `focus daemon --verbose` logs CAPSLOCK hold/release events to stderr with timestamps | VERIFIED | `--verbose` option wired in Program.cs (line 58-61); passed to `DaemonCommand.Run(background, verbose)` (line 75); forwarded to `new CapsLockMonitor(channel.Reader, verbose)` (line 49 of DaemonCommand.cs) |
| 9 | Running `focus daemon --background` detaches from console and runs silently with tray icon | VERIFIED | `if (background) PInvoke.FreeConsole()` at line 37 of DaemonCommand.cs; startup message printed before FreeConsole (line 33) |
| 10 | A second `focus daemon` invocation kills the first and starts fresh — no error message | VERIFIED | DaemonMutex.AcquireOrReplace kills existing process by name (lines 27-41), re-acquires mutex, no error message on replacement path |
| 11 | Ctrl+C in foreground mode cleanly unhooks keyboard hook and exits process | VERIFIED | `Console.CancelKeyPress` handler cancels CTS (lines 54-58); cleanup path calls `hook.Uninstall()`, `channel.Writer.Complete()`, mutex release (lines 87-103 of DaemonCommand.cs) |
| 12 | Clicking Exit in the tray icon context menu cleanly unhooks and exits | VERIFIED | `OnExitClicked` sets `_trayIcon.Visible = false`, calls `_onExit()` (cancels CTS), calls `Application.ExitThread()` (lines 45-55 of TrayIcon.cs) |
| 13 | After system sleep/wake, the keyboard hook is automatically reinstalled | VERIFIED | PowerBroadcastWindow.WndProc handles `WM_POWERBROADCAST + PBT_APMRESUMEAUTOMATIC` with `_hook.Uninstall(); _hook.Install(); _monitor.ResetState()` (lines 92-98 of TrayIcon.cs) |
| 14 | Pressing CAPSLOCK while daemon runs does NOT toggle the CAPSLOCK LED — key is fully suppressed | VERIFIED (code) | `return (LRESULT)1` suppresses both keydown and keyup for bare CAPSLOCK; requires human for LED verification |

**Score: 14/14 truths verified (5 require human runtime confirmation for full end-to-end)**

---

## Required Artifacts

| Artifact | Provided | Status | Details |
|----------|----------|--------|---------|
| `focus/Windows/Daemon/KeyEvent.cs` | Immutable record for keyboard hook events | VERIFIED | 7 lines; `internal readonly record struct KeyEvent(uint VkCode, bool IsKeyDown, uint Timestamp)` |
| `focus/Windows/Daemon/KeyboardHookHandler.cs` | WH_KEYBOARD_LL hook install/uninstall, HOOKPROC callback, Channel producer | VERIFIED | 117 lines; substantive implementation with Install/Uninstall/Dispose/HookCallback; static `s_hookProc` field present |
| `focus/Windows/Daemon/CapsLockMonitor.cs` | Channel consumer with CAPSLOCK hold/release state machine and verbose logging | VERIFIED | 79 lines; ReadAllAsync consumer loop, `_isHeld` state machine, OnCapsLockHeld/OnCapsLockReleased, ResetState |
| `focus/Windows/Daemon/DaemonMutex.cs` | Named mutex single-instance enforcement with replace semantics | VERIFIED | 64 lines; AcquireOrReplace with kill-existing + re-acquire pattern; Release with safe dispose |
| `focus/Windows/Daemon/TrayIcon.cs` | DaemonApplicationContext subclass with NotifyIcon, context menu, and WM_POWERBROADCAST handler | VERIFIED | 104 lines; NotifyIcon "Focus Daemon", ContextMenuStrip with Exit, PowerBroadcastWindow inner NativeWindow |
| `focus/Windows/Daemon/DaemonCommand.cs` | Daemon subcommand orchestrator: mutex, channel, STA thread, consumer task, cleanup | VERIFIED | 108 lines; full lifecycle: mutex, startup message, FreeConsole, channel, STA thread, cancellation, ordered cleanup |
| `focus/Program.cs` | Updated CLI with 'daemon' subcommand added to RootCommand | VERIFIED | `var daemonCommand = new Command("daemon", ...)` at line 53; `rootCommand.Subcommands.Add(daemonCommand)` at line 78; `using Focus.Windows.Daemon` at line 3 |

---

## Key Link Verification

### Plan 01 Key Links

| From | To | Via | Pattern | Status |
|------|----|-----|---------|--------|
| KeyboardHookHandler.cs | KeyEvent.cs | TryWrite to Channel | `_channelWriter.TryWrite` | WIRED — line 109 |
| CapsLockMonitor.cs | KeyEvent.cs | ReadAllAsync from Channel | `_reader.ReadAllAsync` | WIRED — line 27 |
| KeyboardHookHandler.cs | NativeMethods.txt | CsWin32 SetWindowsHookEx | `PInvoke.SetWindowsHookEx` | WIRED — line 52; `SetWindowsHookEx` present in NativeMethods.txt line 24 |

### Plan 02 Key Links

| From | To | Via | Pattern | Status |
|------|----|-----|---------|--------|
| DaemonCommand.cs | KeyboardHookHandler.cs | Creates and passes Channel, installs hook | `KeyboardHookHandler` | WIRED — `new KeyboardHookHandler(channel.Writer)` line 48 |
| DaemonCommand.cs | CapsLockMonitor.cs | Starts consumer task with Channel reader | `CapsLockMonitor` | WIRED — `new CapsLockMonitor(channel.Reader, verbose)` line 49 |
| DaemonCommand.cs | DaemonMutex.cs | Acquires mutex before starting daemon | `DaemonMutex.AcquireOrReplace` | WIRED — line 24 |
| TrayIcon.cs | KeyboardHookHandler.cs | Reinstalls hook on WM_POWERBROADCAST wake event | `WM_POWERBROADCAST` | WIRED — PowerBroadcastWindow.WndProc at lines 90-99 |
| Program.cs | DaemonCommand.cs | daemon subcommand SetAction calls DaemonCommand.Run | `DaemonCommand.Run` | WIRED — line 75 |

---

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| DAEMON-01 | Plan 02 | User can start a background daemon via `focus daemon` that persists until explicitly stopped | SATISFIED | `focus daemon` subcommand in Program.cs; DaemonCommand starts non-background STA thread (`IsBackground = false`); process lives until CTS cancellation |
| DAEMON-02 | Plan 01 | Daemon installs WH_KEYBOARD_LL hook and detects CAPSLOCK held/released state | SATISFIED | KeyboardHookHandler installs WH_KEYBOARD_LL; CapsLockMonitor tracks held/released via Channel; human tester approved |
| DAEMON-04 | Plan 01 | Daemon enforces single instance via named mutex | SATISFIED | DaemonMutex uses `Global\focus-daemon`; replace semantics implemented. NOTE: REQUIREMENTS.md text says "second launch exits with error" but the locked user decision (RESEARCH.md) specifies "kills existing and starts fresh — no error message". Implementation correctly follows the user decision. REQUIREMENTS.md wording is stale and should be updated in a future pass. |
| DAEMON-05 | Plan 02 | Daemon cleans up overlay windows and unhooks keyboard hook on exit/crash | SATISFIED | Ordered cleanup in DaemonCommand.Run: hook.Uninstall, channel.Writer.Complete, staThread.Join(500ms), consumerTask.Wait(500ms), DaemonMutex.Release; tray Exit path also sets NotifyIcon.Visible=false |
| DAEMON-06 | Plan 01 | Daemon filters LLKHF_INJECTED key events to prevent AHK-triggered overlay flicker | SATISFIED | `if (((uint)kbd->flags & LLKHF_INJECTED) != 0) return PInvoke.CallNextHookEx(...)` at KeyboardHookHandler.cs lines 84-85 |

### REQUIREMENTS.md Cross-Reference

Requirements mapped to Phase 4 in REQUIREMENTS.md traceability table:
- DAEMON-01: Phase 4, Complete — verified
- DAEMON-02: Phase 4, Complete — verified
- DAEMON-04: Phase 4, Complete — verified (with documentation drift noted above)
- DAEMON-05: Phase 4, Complete — verified
- DAEMON-06: Phase 4, Complete — verified

No orphaned requirements: all Phase 4 requirements in REQUIREMENTS.md appear in plan frontmatter and are verified.

---

## Project Configuration Verification

| Check | Expected | Actual | Status |
|-------|----------|--------|--------|
| focus.csproj UseWindowsForms | `<UseWindowsForms>true</UseWindowsForms>` | Present in PropertyGroup | VERIFIED |
| focus.csproj TargetFramework | `net8.0-windows` | `<TargetFramework>net8.0-windows</TargetFramework>` | VERIFIED |
| NativeMethods.txt entry count | 30 (23 original + 7 added) | 30 lines | VERIFIED |
| NativeMethods.txt: SetWindowsHookEx | Present | Line 24 | VERIFIED |
| NativeMethods.txt: UnhookWindowsHookEx | Present | Line 25 | VERIFIED |
| NativeMethods.txt: CallNextHookEx | Present | Line 26 | VERIFIED |
| NativeMethods.txt: GetModuleHandle | Present | Line 27 | VERIFIED |
| NativeMethods.txt: FreeConsole | Present | Line 28 | VERIFIED |
| NativeMethods.txt: GetKeyState | Present | Line 29 | VERIFIED |
| NativeMethods.txt: KBDLLHOOKSTRUCT | Present (added as auto-fix) | Line 30 | VERIFIED |
| dotnet build | 0 errors | 0 errors, 1 warning (DPI manifest advisory — pre-existing) | VERIFIED |

---

## Critical Implementation Patterns Verified

| Pattern | Required By | Check | Status |
|---------|-------------|-------|--------|
| Static HOOKPROC field (`private static HOOKPROC? s_hookProc`) | Plan 01 task spec, GC safety | Present at KeyboardHookHandler.cs line 17 | VERIFIED |
| FreeLibrarySafeHandle with ownsHandle=false | Plan 01 task spec | `PInvoke.GetModuleHandle((string?)null)` returns SafeHandle directly — CsWin32 overload already correct | VERIFIED |
| UnhookWindowsHookExSafeHandle as hook handle type | Plan 01 deviation fix | `global::Windows.Win32.UnhookWindowsHookExSafeHandle? _hookHandle` at line 20 | VERIFIED |
| TryWrite (not WriteAsync) in hook callback | Plan 01 task spec, 1000ms budget | `_channelWriter.TryWrite(...)` at line 109 — no await in callback | VERIFIED |
| ResetState() for sleep/wake recovery | Plan 01 task spec | `public void ResetState() { _isHeld = false; }` at CapsLockMonitor.cs line 74 | VERIFIED |
| STA thread for WH_KEYBOARD_LL message pump | Plan 02 task spec | `staThread.SetApartmentState(ApartmentState.STA)` at DaemonCommand.cs line 68 | VERIFIED |
| staThread.IsBackground = false | Plan 02 task spec | Line 69 of DaemonCommand.cs | VERIFIED |
| cts.Token.Register(() => Application.ExitThread()) | Plan 02 task spec | Line 75 of DaemonCommand.cs | VERIFIED |

---

## Anti-Patterns Scan

Scanned all 6 files in `focus/Windows/Daemon/` plus `focus/Program.cs` for:
- TODO/FIXME/PLACEHOLDER comments
- Empty implementations (return null / return {} / return [])
- Console.log-only stubs
- Placeholder return values

**Result: No anti-patterns found.**

The only comments resembling "not yet implemented" are intentional forward-reference comments:
- CapsLockMonitor.cs: `// Phase 6 will hook overlay show logic here.` — this is correct; Phase 4 scope explicitly excludes overlay code. The method body has real logging logic, not an empty stub.

---

## Human Verification Required

### 1. CAPSLOCK LED Suppression

**Test:** Run `focus daemon`, press CAPSLOCK several times.
**Expected:** CAPSLOCK LED does NOT toggle; key is fully consumed by the hook.
**Why human:** LED state is a physical hardware indicator, not queryable via code inspection.

### 2. Tray Icon Exit — Visual Verification

**Test:** Start daemon, right-click tray area, click "Exit".
**Expected:** Tray icon disappears immediately (no ghost icon), terminal shows no errors, process exits.
**Why human:** NotifyIcon ghost-icon behavior and tray cleanup are visual runtime behaviors.

### 3. Hook Durability Under Fullscreen Apps (DAEMON-02 extended)

**Test:** Start daemon with `--verbose`, switch to a fullscreen game/app, hold CAPSLOCK.
**Expected:** `[HH:mm:ss.fff] CAPSLOCK held` appears; hook continues to fire after 2+ minutes idle.
**Why human:** WH_KEYBOARD_LL timeout behavior under Direct3D exclusive fullscreen is a runtime characteristic.

### 4. Sleep/Wake Hook Recovery

**Test:** Start daemon with `--verbose`, put system to sleep (Start > Power > Sleep), wake it, press CAPSLOCK.
**Expected:** CAPSLOCK still detected; PowerBroadcastWindow reinstalled the hook on PBT_APMRESUMEAUTOMATIC.
**Why human:** Requires actual system sleep cycle; PBT_APMRESUMEAUTOMATIC cannot be simulated in code inspection.

### 5. Background Mode Console Detach

**Test:** Run `focus daemon --background` from a terminal.
**Expected:** "Focus daemon started. Listening for CAPSLOCK." prints, terminal prompt returns (process detached), tray icon visible.
**Why human:** FreeConsole/detach behavior and whether stdout correctly returns to terminal require live observation.

**Note:** The 04-02-SUMMARY.md states a human tester approved all 8 verification checks, which covers items 1-5 above. These are listed here as documentation for future verification runs.

---

## Build Verification

```
Build succeeded.
  1 Warning(s)  [WFAC010: DPI manifest advisory — pre-existing from Phase 1, not introduced by Phase 4]
  0 Error(s)
```

All six daemon source files compile cleanly. The WFAC010 warning is a pre-existing advisory about the DPI manifest approach used since Phase 1 and is unrelated to Phase 4 work.

---

## Documentation Drift Note

**REQUIREMENTS.md DAEMON-04 wording:** "second launch exits with error" (stale)
**Actual user decision (RESEARCH.md locked constraint):** "second instance kills the existing daemon and starts fresh (no error-and-exit)"
**Impact:** Implementation is correct per user intent. REQUIREMENTS.md text should be updated in a future documentation pass to reflect "kills existing and starts fresh". This is not an implementation gap.

---

## Summary

Phase 4 goal is achieved. All six daemon component files exist, are substantive (no stubs), and are fully wired together into a working `focus daemon` command. The build passes with 0 errors. All 14 must-have truths are verified against the actual codebase:

- Plan 01: KeyEvent, KeyboardHookHandler (with WH_KEYBOARD_LL, LLKHF_INJECTED filter, CAPSLOCK suppression, modifier filtering), CapsLockMonitor (async Channel consumer with hold/release state machine), DaemonMutex (replace semantics)
- Plan 02: DaemonApplicationContext (NotifyIcon, Exit handler, PowerBroadcastWindow wake recovery), DaemonCommand (full lifecycle orchestrator), Program.cs daemon subcommand wiring

All 5 phase requirements (DAEMON-01, DAEMON-02, DAEMON-04, DAEMON-05, DAEMON-06) are implemented and evidenced in the codebase. The phase is ready to proceed to Phase 5 (Overlay Windows).

---

_Verified: 2026-03-01T08:30:00Z_
_Verifier: Claude (gsd-verifier)_
