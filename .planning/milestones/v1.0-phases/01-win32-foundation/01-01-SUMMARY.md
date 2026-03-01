---
phase: 01-win32-foundation
plan: 01
subsystem: infra
tags: [dotnet, cswin32, win32, pinvoke, system-commandline, dpi, windows]

# Dependency graph
requires: []
provides:
  - .NET 10 console project with CsWin32 0.3.269 P/Invoke source generation
  - PerMonitorV2 DPI awareness manifest embedded in binary
  - System.CommandLine 2.0.3 CLI skeleton with --debug option routing
  - WindowInfo record (Hwnd, ProcessName, Title, bounds, MonitorIndex, IsTopmost, IsUwpFrame)
  - MonitorHelper with EnumerateMonitors and GetMonitorIndex utilities
  - 18 Win32 API bindings via NativeMethods.txt (EnumWindows, DwmGetWindowAttribute, etc.)
affects: [02-window-enumeration, 03-navigation]

# Tech tracking
tech-stack:
  added:
    - Microsoft.Windows.CsWin32 0.3.269 (P/Invoke source generator)
    - System.CommandLine 2.0.3 (CLI parsing)
    - .NET 10 (net10.0 target - .NET 9 runtime not installed on dev machine)
  patterns:
    - CsWin32 NativeMethods.txt declares Win32 APIs, source generator produces PInvoke class at build time
    - AllowUnsafeBlocks=true required for CsWin32 unsafe pointer APIs
    - EmitCompilerGeneratedFiles=true enables inspection of generated .cs files in obj/Debug/net10.0/generated/
    - SupportedOSPlatform("windows5.0") suppresses CA1416 warnings for Windows-only P/Invoke calls
    - nint for public API surface, CsWin32 HWND/HMONITOR types internally with IntPtr conversion

key-files:
  created:
    - focus/focus.csproj
    - focus/app.manifest
    - focus/NativeMethods.txt
    - focus/Program.cs
    - focus/Windows/WindowInfo.cs
    - focus/Windows/MonitorHelper.cs
  modified: []

key-decisions:
  - "Use net10.0 target (not net9.0) — .NET 9 runtime not installed; .NET 10 SDK and runtime available on dev machine"
  - "Use GetWindowLong in NativeMethods.txt (not GetWindowLongPtr) — CsWin32 generates the 64-bit safe version from GetWindowLong"
  - "EmitCompilerGeneratedFiles=true kept in csproj — enables inspecting generated PInvoke source in obj/Debug/net10.0/generated/"
  - "SupportedOSPlatform('windows5.0') on MonitorHelper class — suppresses CA1416 with correct minimum version constraint"
  - "Focus.Windows namespace for WindowInfo/MonitorHelper — using global:: prefix needed when referencing Windows.Win32 types inside this namespace to avoid ambiguity"

patterns-established:
  - "Pattern: CsWin32 HWND/HMONITOR conversion — use (nint)(IntPtr)handle and new HWND((void*)(IntPtr)nint) for nint<->CsWin32 handle conversion"
  - "Pattern: Delegate lifetime safety — store MONITORENUMPROC/WNDENUMPROC callbacks in local variables before passing to unmanaged code"
  - "Pattern: Windows-only API attributes — add [SupportedOSPlatform('windows5.0')] at class level for Win32 utility classes"

requirements-completed: [NAV-06, ENUM-05]

# Metrics
duration: 7min
completed: 2026-02-27
---

# Phase 1 Plan 01: Win32 Foundation Scaffold Summary

**.NET 10 project with CsWin32 P/Invoke, PerMonitorV2 DPI manifest, System.CommandLine CLI, WindowInfo record, and MonitorHelper utilities — full compilation and routing verified**

## Performance

- **Duration:** 7 min
- **Started:** 2026-02-26T23:14:53Z
- **Completed:** 2026-02-27T23:21:46Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- .NET 10 console project builds with 0 errors, 0 warnings from scratch
- CsWin32 0.3.269 generates all 18 Win32 API bindings at compile time from NativeMethods.txt (verified via EmitCompilerGeneratedFiles)
- PerMonitorV2 DPI manifest embedded via ApplicationManifest property in .csproj
- System.CommandLine 2.0.3 --debug option routes "enumerate" to placeholder, null/empty to usage message, unknown to stderr
- WindowInfo record holds all 10 fields needed for enumeration pipeline output
- MonitorHelper EnumerateMonitors and GetMonitorIndex compile and reference correct PInvoke methods

## Task Commits

Each task was committed atomically:

1. **Task 1: Create .NET 10 project with CsWin32, DPI manifest, CLI skeleton** - `dc2722e` (feat)
2. **Task 2: Create WindowInfo record and MonitorHelper utility** - `594acbd` (feat)

## Files Created/Modified

- `focus/focus.csproj` - Project config: net10.0 target, AllowUnsafeBlocks, ApplicationManifest, CsWin32 + System.CommandLine packages
- `focus/app.manifest` - PerMonitorV2 DPI awareness declaration with Windows 10/11 supportedOS
- `focus/NativeMethods.txt` - 18 Win32 API declarations for CsWin32 source generator
- `focus/Program.cs` - CLI entry point with RootCommand and --debug option routing skeleton
- `focus/Windows/WindowInfo.cs` - Immutable record with all window fields, FlagsString property, TruncateTitle method
- `focus/Windows/MonitorHelper.cs` - Static utility class for monitor enumeration and HWND-to-index mapping

## Decisions Made

- Used `net10.0` instead of `net9.0` because the dev machine has .NET 10 SDK and runtime but .NET 9 runtime not installed. The binary could not run with net9.0 target.
- Used `GetWindowLong` in NativeMethods.txt (not `GetWindowLongPtr`). CsWin32 does not expose `GetWindowLongPtr` directly — `GetWindowLong` generates the platform-appropriate 64-bit safe version and also produces the `WINDOW_LONG_PTR_INDEX` enum.
- Added `EmitCompilerGeneratedFiles=true` to the .csproj permanently to enable inspection of CsWin32-generated code.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] GetWindowLongPtr replaced with GetWindowLong in NativeMethods.txt**
- **Found during:** Task 1 (build verification)
- **Issue:** `GetWindowLongPtr` is not a valid CsWin32 API name — it triggers warning PInvoke001 "Method not found" and generates no bindings or WINDOW_LONG_PTR_INDEX enum
- **Fix:** Replaced `GetWindowLongPtr` with `GetWindowLong` in NativeMethods.txt; CsWin32 maps this to the 64-bit safe GetWindowLongPtr call internally and generates the WINDOW_LONG_PTR_INDEX enum
- **Files modified:** focus/NativeMethods.txt
- **Verification:** Build succeeds with 0 warnings; WINDOW_LONG_PTR_INDEX.g.cs appears in generated output
- **Committed in:** dc2722e (Task 1 commit)

**2. [Rule 3 - Blocking] Target framework changed from net9.0 to net10.0**
- **Found during:** Task 1 (dotnet run verification)
- **Issue:** `dotnet run -- --help` failed with "You must install or update .NET" because .NET 9 runtime is not installed on the dev machine (available: 2.1, 2.2, 3.1, 7.0, 8.0, 10.0)
- **Fix:** Changed `<TargetFramework>` from `net9.0` to `net10.0` in focus.csproj
- **Files modified:** focus/focus.csproj
- **Verification:** `dotnet run -- --help` outputs command description; `dotnet run -- --debug enumerate` prints placeholder
- **Committed in:** dc2722e (Task 1 commit)

**3. [Rule 2 - Missing critical functionality] Added SupportedOSPlatform("windows5.0") to MonitorHelper**
- **Found during:** Task 2 (build verification)
- **Issue:** CA1416 warnings for PInvoke.EnumDisplayMonitors and PInvoke.MonitorFromWindow since they're Windows-only APIs
- **Fix:** Added `[SupportedOSPlatform("windows5.0")]` attribute at class level on MonitorHelper
- **Files modified:** focus/Windows/MonitorHelper.cs
- **Verification:** Build succeeds with 0 warnings after using "windows5.0" (not just "windows")
- **Committed in:** 594acbd (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (1 bug fix, 1 blocking environment fix, 1 missing platform annotation)
**Impact on plan:** All auto-fixes necessary for correctness and runnability. No scope creep.

## Issues Encountered

- `Focus.Windows` namespace caused ambiguity when referencing `Windows.Win32.Foundation.RECT` inside MonitorHelper — the compiler tried to resolve `Windows` as `Focus.Windows.Win32`. Fixed by adding explicit `using Windows.Win32.Foundation;` import (avoids global:: prefix on every usage).

## User Setup Required

None - no external service configuration required. The project builds and runs with the standard .NET 10 SDK.

## Next Phase Readiness

- Plan 01 complete: project scaffold, CsWin32 bindings, DPI manifest, CLI skeleton, WindowInfo data model, MonitorHelper utilities all ready
- Plan 02 (window enumeration pipeline) can use WindowInfo and MonitorHelper directly from Focus.Windows namespace
- EnumWindows, DwmGetWindowAttribute, IsWindowVisible, IsIconic, GetWindowLong all in NativeMethods.txt ready for Alt+Tab filter implementation
- UWP/CoreWindow behavior on Windows 11 24H2 remains as noted blocker (must validate with real windows in Plan 02)

---
*Phase: 01-win32-foundation*
*Completed: 2026-02-27*
