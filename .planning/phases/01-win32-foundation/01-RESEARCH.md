# Phase 1: Win32 Foundation - Research

**Researched:** 2026-02-27
**Domain:** Win32 API / C# P/Invoke (CsWin32), window enumeration pipeline, DPI awareness, CLI scaffolding
**Confidence:** HIGH (core Win32 facts verified via official MS docs; CsWin32 setup verified via official docs + v0.3.269 release confirmation; System.CommandLine verified via official tutorial updated Dec 2025)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Debug enumerate output:**
- Aligned columnar table format (like `tasklist` / `docker ps`)
- Long window titles truncated at ~40 characters with ellipsis
- Windows sorted by Z-order (topmost first, matching EnumWindows natural order)
- Summary line at the end: window count and monitor count (e.g., "Found 12 windows on 2 monitors")
- UWP duplicate HWNDs noted in summary line (e.g., "Filtered 3 duplicate UWP HWNDs") rather than shown in the table

**Filtering behavior:**
- Strict Alt+Tab algorithm match: check WS_EX_TOOLWINDOW, WS_EX_APPWINDOW, owner chain — replicate exact Alt+Tab window set
- Top-level dialog boxes included if they appear in Alt+Tab (modal dialogs owned by a parent naturally excluded by the algorithm)
- Always-on-top (WS_EX_TOPMOST) windows included as normal navigation candidates, flagged in debug output

**Window info displayed:**
- Table columns: HWND, Process Name, Title (truncated ~40 chars), Bounds (L,T,R,B), Monitor index, Flags
- Process name included (e.g., "chrome.exe") — aids identification and previews Phase 3 exclude-by-process
- Monitor number column (1, 2, 3...) for instant multi-monitor validation
- Bounds as L,T,R,B matching Win32 RECT convention (raw from DWMWA_EXTENDED_FRAME_BOUNDS)
- Compact "Flags" column with single-character markers: "T" for topmost (extensible for future flags)

### Claude's Discretion
- Column widths and exact alignment approach
- Table border style (plain text, box-drawing characters, or minimal separators)
- HWND display format (hex vs decimal)
- Exact error messaging for edge cases (no windows found, API failures)

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| ENUM-01 | Tool enumerates all top-level windows via EnumWindows | EnumWindows via CsWin32 NativeMethods.txt; WNDENUMPROC callback pattern documented |
| ENUM-02 | Tool filters out hidden windows (IsWindowVisible check) | IsWindowVisible P/Invoke verified; must come before cloaked check |
| ENUM-03 | Tool filters out cloaked windows (DWMWA_CLOAKED check) | DwmGetWindowAttribute(DWMWA_CLOAKED) != 0 pattern confirmed via Raymond Chen blog + MS docs |
| ENUM-04 | Tool filters out minimized windows (IsIconic check) | IsIconic P/Invoke; check after visibility/cloaked to avoid API calls on excluded windows |
| ENUM-05 | Tool gets accurate visible bounds via DWMWA_EXTENDED_FRAME_BOUNDS | Confirmed returns physical pixels, excludes invisible resize borders; never use GetWindowRect |
| ENUM-06 | Tool uses UWP-safe window filtering (Alt+Tab algorithm) | Full algorithm documented: GetAncestor(GA_ROOTOWNER) + GetLastActivePopup loop + WS_EX_TOOLWINDOW check; UWP dedup via ApplicationFrameWindow class name |
| NAV-06 | Tool uses DPI-aware coordinates (PerMonitorV2 manifest) | app.manifest with dpiAwareness=PerMonitorV2 + ApplicationManifest in .csproj; DWMWA_EXTENDED_FRAME_BOUNDS returns physical pixels always |
| DBG-01 | User can run `--debug enumerate` to list all detected windows | System.CommandLine 2.0.3 subcommand pattern; columnar table output design locked in CONTEXT.md |
</phase_requirements>

---

## Summary

Phase 1 establishes the complete Win32 interop foundation from a blank .NET 9 console project. The technical work falls into three clusters: project scaffold (CsWin32, DPI manifest, System.CommandLine), the enumeration/filtering pipeline (EnumWindows + Alt+Tab algorithm + cloaked/minimized/invisible checks), and the debug enumerate output command.

The core Win32 APIs are well-documented and have been stable for years. The Alt+Tab filtering algorithm, documented by Raymond Chen in 2007, remains the authoritative approach. The critical complication is UWP/Store apps: their frame container (ApplicationFrameHost) appears as a shell-level HWND that must be detected and deduplicated. The standard approach — check if the window class is `ApplicationFrameWindow`, enumerate child windows with different process IDs, prefer the child — is implemented by real-world tools (OBS Studio, AltSnap) and is the pattern to follow.

DWMWA_EXTENDED_FRAME_BOUNDS is the mandatory coordinate source: it returns physical pixels and excludes invisible resize borders, while GetWindowRect returns logical (DPI-scaled) pixels and includes those borders. With a PerMonitorV2 manifest, the process receives physical-pixel coordinates natively, making the bounds correct for navigation geometry without scaling math.

**Primary recommendation:** Scaffold the .NET 9 console project first (CsWin32 + manifest + System.CommandLine), build the window enumeration service as a standalone class, then wire it into the `--debug enumerate` command. This produces a testable artifact after each task.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Windows.CsWin32 | 0.3.269 (Jan 2026) | P/Invoke source generation for all Win32 APIs | Replaces manual DllImport; AOT-safe; generates strongly-typed wrappers from metadata; official Microsoft library |
| System.CommandLine | 2.0.3 | CLI argument parsing, subcommands, help | Stable official MS library; trim-friendly; AOT-compatible; `ParseResult.Invoke()` pattern handles errors + help automatically |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| .NET 9 | net9.0 | Target framework | Project already decided; note: PROJECT.md mentions .NET 8 but STATE.md and ROADMAP reference .NET 9 — use net9.0 per STATE.md |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| CsWin32 | Manual DllImport | DllImport is deprecated, not AOT-safe, more boilerplate; CsWin32 is clearly superior |
| System.CommandLine | CommandLineParser (NuGet) | Not officially maintained by MS; less AOT-friendly |
| System.CommandLine | Hand-rolled args parsing | No help generation, no error handling |

**Installation:**
```bash
dotnet add package Microsoft.Windows.CsWin32 --prerelease
dotnet add package System.CommandLine
```

---

## Architecture Patterns

### Recommended Project Structure
```
focus/
├── focus.csproj           # AllowUnsafeBlocks=true, ApplicationManifest=app.manifest
├── app.manifest           # PerMonitorV2 DPI awareness declaration
├── NativeMethods.txt      # CsWin32 API declarations
├── Program.cs             # CLI wiring (RootCommand, --debug option, enumerate subcommand)
└── Windows/
    ├── WindowEnumerator.cs   # EnumWindows + Alt+Tab filter pipeline
    ├── WindowInfo.cs         # Record/struct: HWND, title, processName, bounds, monitor, flags
    └── MonitorHelper.cs      # EnumDisplayMonitors, MonitorFromWindow, monitor index mapping
```

### Pattern 1: CsWin32 Project Setup

**What:** Configure .csproj and NativeMethods.txt so CsWin32 generates all required Win32 bindings at compile time.

**NativeMethods.txt entries needed:**
```
EnumWindows
IsWindowVisible
IsIconic
GetWindowLongPtr
GetAncestor
GetLastActivePopup
DwmGetWindowAttribute
GetWindowThreadProcessId
OpenProcess
QueryFullProcessImageName
CloseHandle
GetWindowTextW
GetWindowTextLengthW
EnumDisplayMonitors
MonitorFromWindow
GetMonitorInfo
GetClassNameW
```

**csproj configuration:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>focus</AssemblyName>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.269">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="System.CommandLine" Version="2.0.3" />
  </ItemGroup>
</Project>
```

### Pattern 2: PerMonitorV2 DPI Manifest

**What:** Embed a Windows application manifest declaring PerMonitorV2 DPI awareness. Must be done before any HWND is created — cannot be changed after process starts.

**app.manifest:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="focus.app"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2, PerMonitor</dpiAwareness>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 / Windows 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
    </application>
  </compatibility>
</assembly>
```

**Why:** DWMWA_EXTENDED_FRAME_BOUNDS always returns physical pixels. With a DPI-unaware process, all other Win32 coordinate APIs would return virtualized (scaled) values, creating inconsistency. PerMonitorV2 ensures the process sees physical pixels everywhere.

### Pattern 3: EnumWindows + Alt+Tab Filter

**What:** The canonical algorithm for replicating what appears in Alt+Tab.

```csharp
// Source: Raymond Chen, "Which windows appear in the Alt+Tab list?"
// https://devblogs.microsoft.com/oldnewthing/20071008-00/?p=24863
static bool IsAltTabWindow(HWND hwnd)
{
    // Must be visible
    if (!PInvoke.IsWindowVisible(hwnd)) return false;

    // Must not be cloaked (virtual desktop / shell-hidden)
    uint cloaked = 0;
    PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
        &cloaked, sizeof(uint));
    if (cloaked != 0) return false;

    // Must not be minimized
    if (PInvoke.IsIconic(hwnd)) return false;

    // Extended style check: tool windows are excluded
    long exStyle = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
    bool isToolWindow = (exStyle & (long)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0;
    bool isAppWindow  = (exStyle & (long)WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0;

    // WS_EX_APPWINDOW forces inclusion (override owner-chain logic)
    if (isAppWindow) return true;

    // Walk owner chain to root owner
    HWND hwndWalk = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);

    // Walk visible last active popup chain back down
    HWND hwndTry;
    while ((hwndTry = PInvoke.GetLastActivePopup(hwndWalk)) != hwndWalk)
    {
        if (PInvoke.IsWindowVisible(hwndTry)) break;
        hwndWalk = hwndTry;
    }

    // Include only if we ended up back at the original hwnd
    if (hwndWalk != hwnd) return false;

    // Exclude tool windows (unless WS_EX_APPWINDOW already returned true above)
    if (isToolWindow) return false;

    return true;
}
```

### Pattern 4: DWMWA_EXTENDED_FRAME_BOUNDS for Window Bounds

**What:** Retrieve accurate visible bounds in physical pixels. Never use GetWindowRect as the primary source.

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
static bool TryGetWindowBounds(HWND hwnd, out RECT rect)
{
    rect = default;
    HRESULT hr = PInvoke.DwmGetWindowAttribute(hwnd,
        DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
        &rect,
        (uint)sizeof(RECT));
    return hr.Succeeded;
}
```

Key facts (HIGH confidence, verified via official MS docs):
- Returns **physical pixels** always — NOT adjusted for the calling process's DPI setting
- Excludes invisible resize borders included in GetWindowRect on Windows 10+
- Returns RECT with Left, Top, Right, Bottom (matching user's locked decision on L,T,R,B format)

### Pattern 5: UWP Deduplication

**What:** UWP/Store apps hosted by ApplicationFrameHost show two HWNDs: the frame window (class `ApplicationFrameWindow`) and the CoreWindow child. Only the frame window should appear in the list; the child should be filtered.

```csharp
// Source: OBS Studio window-helpers.c + AutoHotkey community research
static HWND GetUwpActualWindow(HWND hwnd)
{
    // Check if this is an ApplicationFrameWindow container
    Span<char> className = stackalloc char[256];
    PInvoke.GetClassName(hwnd, className);
    string cn = new string(className).TrimEnd('\0');

    if (cn != "ApplicationFrameWindow" && cn != "WinUIDesktopWin32WindowClass")
        return hwnd; // Not a UWP container — use as-is

    // Get the PID of the frame host
    uint framePid;
    PInvoke.GetWindowThreadProcessId(hwnd, &framePid);

    // Find child window owned by a DIFFERENT process (the actual app)
    HWND actualApp = HWND.Null;
    PInvoke.EnumChildWindows(hwnd, (child, _) =>
    {
        uint childPid;
        PInvoke.GetWindowThreadProcessId(child, &childPid);
        if (childPid != framePid)
        {
            actualApp = child;
            return false; // stop enumeration
        }
        return true;
    }, 0);

    return actualApp != HWND.Null ? actualApp : hwnd;
}
```

**Deduplication strategy for the enumerate output:**
- Keep the ApplicationFrameWindow HWND as the "visible" entry (it's what SetForegroundWindow accepts)
- Track child CoreWindow HWNDs and suppress them if they appear in EnumWindows output
- Count suppressed HWNDs for the summary line

### Pattern 6: Process Name from HWND

**What:** Get "chrome.exe" style process name for the Flags column.

```csharp
// Prefer QueryFullProcessImageName for Unicode paths; fall back to Path.GetFileName
static string GetProcessName(HWND hwnd)
{
    uint pid = 0;
    PInvoke.GetWindowThreadProcessId(hwnd, &pid);
    if (pid == 0) return "?";

    // PROCESS_QUERY_LIMITED_INFORMATION (0x1000) works even for elevated processes
    HANDLE hProcess = PInvoke.OpenProcess(
        PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
    if (hProcess.IsNull) return "?";

    try
    {
        Span<char> buffer = stackalloc char[260];
        uint size = 260;
        if (PInvoke.QueryFullProcessImageName(hProcess, 0, buffer, ref size))
            return Path.GetFileName(new string(buffer[..(int)size]));
    }
    finally
    {
        PInvoke.CloseHandle(hProcess);
    }
    return "?";
}
```

### Pattern 7: Monitor Index Mapping

**What:** Map an HWND to a 1-based monitor index for the debug output column.

```csharp
// Build monitor list via EnumDisplayMonitors once, then use MonitorFromWindow per HWND
static List<HMONITOR> EnumerateMonitors()
{
    var monitors = new List<HMONITOR>();
    PInvoke.EnumDisplayMonitors(HDC.Null, (RECT*)null, (hMon, _, _, _) =>
    {
        monitors.Add(hMon);
        return true;
    }, 0);
    return monitors;
}

static int GetMonitorIndex(HWND hwnd, List<HMONITOR> monitors)
{
    HMONITOR hm = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
    int idx = monitors.IndexOf(hm);
    return idx >= 0 ? idx + 1 : 1; // 1-based
}
```

**Note:** Monitor enumeration order from EnumDisplayMonitors does not reliably match Windows Display Settings numbering (varies by device ID, connection order). For a debug enumerate command, the numbering only needs to be consistent within a single invocation, not match Control Panel monitor numbers.

### Pattern 8: System.CommandLine 2.0 CLI Wiring

**What:** Wire up the `focus --debug enumerate` command using the 2.0.3 API.

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial
// Updated 2025-12-18

using System.CommandLine;

var debugOption = new Option<string?>("--debug")
{
    Description = "Debug mode: enumerate | score | config"
};

var rootCommand = new RootCommand("Directional window focus navigator");
rootCommand.Options.Add(debugOption);

var enumerateCommand = new Command("enumerate", "List all detected user-navigable windows");
rootCommand.Subcommands.Add(enumerateCommand);

enumerateCommand.SetAction(parseResult =>
{
    var enumerator = new WindowEnumerator();
    var windows = enumerator.GetNavigableWindows();
    EnumerateDebugCommand.Print(windows);
    return 0;
});

// Pattern: focus --debug enumerate → same as "focus enumerate"
// OR: make --debug a flag and "enumerate" a subcommand — use the subcommand approach
// per CONTEXT.md: `focus --debug enumerate` is the user-facing command

return rootCommand.Parse(args).Invoke();
```

**Implementation note on `--debug enumerate` vs subcommand:** The CONTEXT.md locks `focus --debug enumerate` as the invocation. In System.CommandLine 2.0, this is best modeled as a `--debug` option that accepts a string value (`enumerate`, `score`, `config`) rather than a subcommand, because the user writes `--debug enumerate` not `focus enumerate`. The option approach keeps the CLI shape correct.

### Anti-Patterns to Avoid
- **Using GetWindowRect:** Returns logical pixels + includes invisible borders. Always use DWMWA_EXTENDED_FRAME_BOUNDS.
- **Using IsWindowVisible alone:** Cloaked windows still pass IsWindowVisible — must also check DWMWA_CLOAKED != 0.
- **Walking with GetWindow in a loop:** EnumWindows is more reliable; GetWindow loops risk infinite loops or destroyed handles.
- **Using DllImport directly:** CsWin32 generates correct signatures including AOT-safe wrappers. Manual DllImport is deprecated.
- **Delegate lifetime bugs:** When passing WNDENUMPROC callbacks to EnumWindows, the delegate MUST be kept alive (stored in a variable) for the duration of the call. A GC-collected delegate causes crashes.
- **Setting DPI awareness in code:** DPI awareness for the process must be declared in the manifest before any HWND is created. Calling SetProcessDpiAwareness() at runtime may be too late.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Win32 P/Invoke declarations | Manual DllImport for each API | CsWin32 NativeMethods.txt | CsWin32 handles struct layout, calling conventions, safe handles, and AOT compatibility — manual declarations have well-known subtle bugs (64-bit LONG_PTR, BOOL vs bool, struct alignment) |
| CLI argument parsing | `args[0] == "--debug"` string matching | System.CommandLine 2.0.3 | Auto-generates `--help`, handles parse errors, returns proper exit code 1 on bad args |
| UWP window detection | Custom window class string parsing | Use `GetClassName` + compare to "ApplicationFrameWindow" | The class name is the documented canonical identifier — no guessing needed |

**Key insight:** Win32 P/Invoke has pervasive subtle correctness issues (struct layout, charset, BOOL semantics, GC lifetime) that CsWin32 resolves at code-generation time. Every manual DllImport is a bug waiting to happen at non-default DPI or 64-bit field widths.

---

## Common Pitfalls

### Pitfall 1: DWMWA_CLOAKED Type Mismatch
**What goes wrong:** `DwmGetWindowAttribute` is called with `BOOL` (4 bytes) for DWMWA_CLOAKED, but DWMWA_CLOAKED returns a `DWORD` (uint, 4 bytes) bitmask — not a BOOL. Calling with the wrong type causes incorrect values.
**Why it happens:** Raymond Chen's blog uses `BOOL isCloaked` in the example code, but the official DWMWINDOWATTRIBUTE docs show the three flag values (DWM_CLOAKED_APP=1, DWM_CLOAKED_SHELL=2, DWM_CLOAKED_INHERITED=4), implying it is a DWORD bitmask.
**How to avoid:** Use `uint cloaked = 0` and check `cloaked != 0`. Both work in practice since any nonzero DWORD is truthy, but the canonical check is `!= 0` not treating it as BOOL.
**Warning signs:** Seeing zeroes for all windows even when windows on other virtual desktops should be cloaked.

### Pitfall 2: Delegate Garbage Collection During EnumWindows
**What goes wrong:** The WNDENUMPROC lambda or delegate is garbage-collected mid-enumeration, causing an access violation or silent enumeration abort.
**Why it happens:** The .NET GC does not know about unmanaged code holding a reference to the delegate. If the delegate is created inline and not stored, it may be collected.
**How to avoid:** Store the delegate in a local variable before passing it to EnumWindows. CsWin32 may handle this differently than raw DllImport — check the generated signature.
**Warning signs:** Enumeration returning fewer windows than expected; intermittent crashes.

### Pitfall 3: GetWindowText on Cross-Process Windows
**What goes wrong:** `GetWindowText` on another process's window sends a WM_GETTEXT message to the target window. If the target is hung or unresponsive, this can block for up to 5 seconds per window.
**Why it happens:** WM_GETTEXT is a posted message that requires the target thread to process it.
**How to avoid:** Use `GetWindowTextW` (the internal-name version) which uses a copy mechanism for cross-process titles and has a built-in timeout. In CsWin32, the generated `PInvoke.GetWindowText` uses this approach. Alternatively, call with a short timeout awareness.
**Warning signs:** `--debug enumerate` taking several seconds to complete.

### Pitfall 4: UWP CoreWindow Appearing in EnumWindows Output
**What goes wrong:** Both the ApplicationFrameWindow (the frame host) and the actual UWP CoreWindow appear as separate entries in EnumWindows output, causing duplicate entries for apps like Calculator.
**Why it happens:** On Windows 10+, some CoreWindow handles are visible via EnumWindows despite being child windows.
**How to avoid:** After the Alt+Tab filter, check each HWND's class name. If it is `Windows.UI.Core.CoreWindow`, check if its parent/owner is already in the result set and suppress it.
**Warning signs:** Calculator appearing twice in the enumerate output; success criteria 3 fails.

### Pitfall 5: Monitor Index Not Matching Windows Display Settings
**What goes wrong:** The monitor numbered "1" in the output does not match Monitor 1 in Windows Display Settings.
**Why it happens:** EnumDisplayMonitors returns monitors in an order determined by device IDs and connection history, not by the logical numbering in Display Settings.
**How to avoid:** Document this limitation in the debug output; it does not affect navigation correctness since Phase 2 uses HMONITOR handles, not indices. For Phase 1, the index only needs to be stable within a single invocation.
**Warning signs:** User confusion about monitor numbers; this is cosmetic for Phase 1.

### Pitfall 6: Forgetting the PerMonitorV2 Manifest
**What goes wrong:** Without the manifest, the process is DPI-unaware. GetWindowRect returns virtualized logical coordinates. DWMWA_EXTENDED_FRAME_BOUNDS still returns physical pixels, creating coordinate space mismatch between APIs.
**Why it happens:** .NET console apps have no default DPI manifest.
**How to avoid:** Create app.manifest with PerMonitorV2 dpiAwareness and set `<ApplicationManifest>app.manifest</ApplicationManifest>` in the .csproj. This is the **highest recovery cost** item — fixing it after Phase 2 geometry code is written requires reviewing all coordinate math.
**Warning signs:** Window bounds on 125% or 150% DPI monitors are noticeably wrong; second success criterion fails.

### Pitfall 7: ApplicationFrameHost Process vs UWP App Process
**What goes wrong:** GetProcessName on an ApplicationFrameWindow HWND returns `ApplicationFrameHost.exe`, not `Calculator.exe` or `WindowsTerminal.exe`.
**Why it happens:** The frame container is a separate process from the hosted UWP app.
**How to avoid:** After running GetUwpActualWindow to find the child HWND, use that child's PID for QueryFullProcessImageName to get the correct process name.
**Warning signs:** All UWP apps showing "ApplicationFrameHost.exe" as the process name.

---

## Code Examples

Verified patterns from official sources:

### IsWindowCloaked Check
```csharp
// Source: Raymond Chen (devblogs.microsoft.com/oldnewthing/20200302-00/?p=103507)
// verified against DWMWINDOWATTRIBUTE docs (updated 2025-01-24)
static unsafe bool IsWindowCloaked(HWND hwnd)
{
    uint cloaked = 0;
    HRESULT hr = PInvoke.DwmGetWindowAttribute(
        hwnd,
        DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
        &cloaked,
        sizeof(uint));
    return hr.Succeeded && cloaked != 0;
}
```

### DWMWA_CLOAKED Values (for informational output)
```
DWM_CLOAKED_APP       = 0x00000001  // cloaked by owner app
DWM_CLOAKED_SHELL     = 0x00000002  // cloaked by shell (virtual desktop)
DWM_CLOAKED_INHERITED = 0x00000004  // inherited from owner window
```
Source: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute (updated 2025-01-24)

### GetWindowBounds via DWMWA_EXTENDED_FRAME_BOUNDS
```csharp
// Source: DWMWINDOWATTRIBUTE docs; confirmed returns physical pixels always
static unsafe bool TryGetExtendedFrameBounds(HWND hwnd, out RECT rect)
{
    RECT r = default;
    HRESULT hr = PInvoke.DwmGetWindowAttribute(
        hwnd,
        DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
        &r,
        (uint)sizeof(RECT));
    rect = r;
    return hr.Succeeded;
}
```

### System.CommandLine 2.0 Program.cs Entry Point
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial
// (updated 2025-12-18, targets .NET 9)
using System.CommandLine;

var debugOption = new Option<string?>("--debug")
{
    Description = "Debug subcommand: enumerate"
};

var rootCommand = new RootCommand("focus — directional window focus navigator");
rootCommand.Options.Add(debugOption);

rootCommand.SetAction(parseResult =>
{
    var debugValue = parseResult.GetValue(debugOption);
    if (debugValue == "enumerate")
    {
        EnumerateDebugCommand.Run();
        return 0;
    }
    // Phase 1: only enumerate is implemented
    Console.Error.WriteLine($"Unknown --debug value: {debugValue}");
    return 2;
});

return rootCommand.Parse(args).Invoke();
```

### Columnar Table Output Approach
```csharp
// Aligned table matching docker ps / tasklist style (locked in CONTEXT.md)
// Column layout (Claude's discretion for exact widths):
const int COL_HWND    = 12;  // "0x00031234"
const int COL_PROCESS = 20;  // "chrome.exe"
const int COL_TITLE   = 42;  // "Window Title..."
const int COL_BOUNDS  = 28;  // "100,200,1400,900"
const int COL_MON     =  4;  // "1"
const int COL_FLAGS   =  6;  // "T"

// Header
Console.WriteLine(
    $"{"HWND",-COL_HWND} {"PROCESS",-COL_PROCESS} {"TITLE",-COL_TITLE} " +
    $"{"BOUNDS",-COL_BOUNDS} {"MON",-COL_MON} {"FLAGS",-COL_FLAGS}");
Console.WriteLine(new string('-', COL_HWND + COL_PROCESS + COL_TITLE + COL_BOUNDS + COL_MON + COL_FLAGS + 5));

// Per-row (title truncated at ~40 chars per CONTEXT.md)
static string TruncateTitle(string title, int maxLen = 40) =>
    title.Length > maxLen ? title[..(maxLen - 3)] + "..." : title;
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual DllImport P/Invoke | CsWin32 source generator | CsWin32 went stable (0.3.x, 2024-2025) | Eliminates struct layout bugs, AOT-compatible |
| GetWindowRect for bounds | DWMWA_EXTENDED_FRAME_BOUNDS | Windows 10+ invisible borders (2015+) | GetWindowRect unreliable for visible bounds on all modern Windows |
| keybd_event() for Alt bypass | SendInput ALT bypass | Noted in STATE.md as decided | keybd_event is legacy; STATE.md already locked SendInput |
| WS_THICKFRAME-only filter | Full Raymond Chen Alt+Tab algorithm | Algorithm documented 2007; still correct | WS_THICKFRAME misses many valid windows; owner-chain algorithm is the reference |

**Deprecated/outdated:**
- `keybd_event()`: Noted in STATE.md ("Use SendInput ALT bypass (not keybd_event)") — applies to Phase 2, not Phase 1
- `DllImport` for Win32: CsWin32 is the current standard; do not mix approaches

---

## Open Questions

1. **CsWin32 WNDENUMPROC callback exact generated form**
   - What we know: CsWin32 generates WNDENUMPROC as a delegate struct; `AllowUnsafeBlocks=true` is required; the exact call syntax may differ from raw DllImport
   - What's unclear: Whether CsWin32 0.3.x generates a safe lambda-compatible overload or requires the unsafe delegate struct explicitly
   - Recommendation: After adding `EnumWindows` to NativeMethods.txt, inspect the generated code in `obj/Generated` before writing the EnumWindows call. The generated signature determines whether a lambda or delegate struct is required.

2. **UWP HWND behavior on Windows 11 24H2**
   - What we know: ApplicationFrameWindow class detection + child process mismatch is the standard pattern; OBS Studio uses this
   - What's unclear: Windows 11 24H2 may have altered UWP hosting details (STATE.md flags this as a validation concern)
   - Recommendation: Manual validation required — run `focus --debug enumerate` with Calculator, Settings, Store, Windows Terminal open and verify single-entry behavior. Flag 24H2-specific findings.

3. **`focus --debug enumerate` CLI shape**
   - What we know: System.CommandLine 2.0 supports both option-with-value (`--debug enumerate`) and subcommand (`enumerate`) patterns
   - What's unclear: The user wrote `focus --debug enumerate` — this implies `--debug` is an option that takes `enumerate` as its value, NOT `enumerate` as a subcommand. Confirmed approach: `Option<string?>("--debug")` with `GetValue(debugOption) == "enumerate"`.
   - Recommendation: Use option approach; this matches the locked invocation format exactly. Subcommand approach would require `focus enumerate` without `--debug`.

---

## Sources

### Primary (HIGH confidence)
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute — DWMWA_CLOAKED values, DWMWA_EXTENDED_FRAME_BOUNDS definition (updated 2025-01-24)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows — EnumWindows note: Windows 8+ enumerates only desktop app top-level windows (updated 2025-07-01)
- https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial — System.CommandLine 2.0 complete tutorial with subcommands (updated 2025-12-18)
- https://github.com/microsoft/CsWin32 — v0.3.269 confirmed Jan 16 2026; setup requirements (AllowUnsafeBlocks, NativeMethods.txt)
- https://microsoft.github.io/CsWin32/docs/getting-started.html — Official CsWin32 setup (project file, NativeMethods.txt syntax, unsafe requirements)
- https://devblogs.microsoft.com/oldnewthing/20071008-00/?p=24863 — Raymond Chen Alt+Tab algorithm (GA_ROOTOWNER + GetLastActivePopup)
- https://devblogs.microsoft.com/oldnewthing/20200302-00/?p=103507 — Raymond Chen cloaked window detection (DwmGetWindowAttribute + DWMWA_CLOAKED)

### Secondary (MEDIUM confidence)
- https://github.com/obsproject/obs-studio/blob/master/libobs/util/windows/window-helpers.c — OBS window filtering: WS_EX_TOOLWINDOW, DWMWA_CLOAKED, ApplicationFrameWindow UWP detection (production code, verified pattern)
- https://gist.github.com/emoacht/7e5a026080aeb7eb1b9316f5fe7628da — PerMonitorV2 manifest XML for .NET apps (community-verified, multiple sources agree)
- WebSearch findings on DWMWA_EXTENDED_FRAME_BOUNDS returning physical pixels — verified by multiple sources including official DPI documentation

### Tertiary (LOW confidence)
- CsWin32 WNDENUMPROC exact generated syntax — training knowledge + DeepWiki inference; must be verified by inspecting generated code after adding EnumWindows to NativeMethods.txt

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — CsWin32 v0.3.269 confirmed from GitHub (Jan 2026); System.CommandLine 2.0.3 confirmed from NuGet; .NET 9 per STATE.md
- Architecture: HIGH — All core Win32 patterns verified via official Microsoft documentation; OBS Studio code validates UWP approach
- Pitfalls: HIGH for known Win32 issues (GC lifetime, DPI, GetWindowRect); MEDIUM for UWP 24H2 specifics (flagged as open question)

**Research date:** 2026-02-27
**Valid until:** 2026-05-27 (stable domain — Win32 APIs don't change; CsWin32 0.3.x line is stable)
