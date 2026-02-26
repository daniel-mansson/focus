# Architecture Research

**Domain:** Win32 Window Management CLI Tool — Directional Focus Navigation
**Researched:** 2026-02-26
**Confidence:** HIGH (Win32 API documentation is official and current; component patterns verified across multiple sources)

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         CLI Entry Point                          │
│                      (Program.cs / Main)                         │
│   Parses args → loads config → orchestrates pipeline             │
├──────────────────┬──────────────────────────────────────────────┤
│  Config Layer    │  Args Layer                                   │
│  (JSON config)   │  (CLI flags override config)                  │
├──────────────────┴──────────────────────────────────────────────┤
│                     Window Enumeration Layer                      │
│   EnumWindows → IsWindowVisible → DWMWA_CLOAKED → IsIconic      │
│   WS_EX_TOOLWINDOW / WS_EX_APPWINDOW → exclude list filter      │
├─────────────────────────────────────────────────────────────────┤
│                    Window Geometry Layer                          │
│   DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) → RECT      │
│   Derives center point (x, y) for each candidate                 │
├─────────────────────────────────────────────────────────────────┤
│                    Candidate Scoring Layer                        │
│   Current window center → direction filter → scoring algorithm   │
│   Balanced | StrongAxisBias | ClosestInDirection strategies       │
├─────────────────────────────────────────────────────────────────┤
│                    Focus Activation Layer                         │
│   SendInput(VK_MENU press) → SetForegroundWindow(hwnd)           │
│   SendInput(VK_MENU release)                                     │
├─────────────────────────────────────────────────────────────────┤
│                    Win32 Native Interop Layer                     │
│   P/Invoke declarations (user32.dll, dwmapi.dll)                 │
│   Typed structs: RECT, INPUT, KEYBDINPUT                         │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| Entry Point | Parse CLI args, load config, resolve overrides, call pipeline, set exit code | `Program.cs` with `System.CommandLine` or manual arg parsing |
| Config Loader | Read/deserialize JSON config, apply defaults, surface validation errors | `Config.cs` + `System.Text.Json` |
| Window Enumerator | Call `EnumWindows`, collect HWND list of all top-level windows | `WindowEnumerator.cs` with `GCHandle`-pinned callback |
| Window Filter | Apply visibility, cloaking, minimized, tool-window, and exclude-list checks | `WindowFilter.cs` with predicate chain |
| Geometry Resolver | Call `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` to get true visible RECT for each window | `WindowGeometry.cs` |
| Candidate Scorer | Accept direction + current window center + candidate list; compute score per strategy; return best match | `CandidateScorer.cs` with strategy pattern |
| Focus Activator | Simulate Alt keypress via `SendInput`, call `SetForegroundWindow`, release Alt | `FocusActivator.cs` |
| Native Interop | All P/Invoke extern declarations, Win32 structs, constants | `NativeMethods.cs` (or CsWin32 generated) |
| Logger | Conditional debug output: window list, scores, chosen target, API call results | `Logger.cs` with verbosity flag |
| Exit Code Reporter | Map result (switched / no candidate / error) to exit codes 0/1/2 | Inline in `Program.cs` |

## Recommended Project Structure

```
WindowFocusNavigation/
├── WindowFocusNavigation.csproj   # .NET 8, OutputType=Exe, nullable enabled
├── NativeMethods.txt              # CsWin32: declare APIs needed (optional)
├── Program.cs                     # Entry point: parse args, orchestrate, exit code
├── Config/
│   ├── AppConfig.cs               # Config model (strategy, wrap, excludes)
│   └── ConfigLoader.cs            # JSON load/default/merge with CLI overrides
├── Windows/
│   ├── WindowEnumerator.cs        # EnumWindows callback, returns List<HWND>
│   ├── WindowFilter.cs            # Predicate chain: visible, not cloaked, not minimized, not tool window, not excluded
│   ├── WindowGeometry.cs          # DwmGetWindowAttribute EXTENDED_FRAME_BOUNDS per HWND
│   └── WindowInfo.cs              # Record: HWND, Title, ProcessName, Rect, Center
├── Scoring/
│   ├── IScorer.cs                 # Interface: Score(WindowInfo from, WindowInfo candidate, Direction dir) → double
│   ├── BalancedScorer.cs          # Balanced distance + alignment scoring
│   ├── StrongAxisBiasScorer.cs    # Heavy axis-direction weighting
│   ├── ClosestInDirectionScorer.cs # Pure nearest-in-cone
│   └── ScorerFactory.cs           # Maps config strategy enum → IScorer
├── Focus/
│   └── FocusActivator.cs          # SendInput(Alt) + SetForegroundWindow
├── Native/
│   └── NativeMethods.cs           # P/Invoke: EnumWindows, DwmGetWindowAttribute, SetForegroundWindow, SendInput, GetForegroundWindow, IsWindowVisible, IsIconic, GetWindowLongPtr
└── Diagnostics/
    └── Logger.cs                  # Conditional verbose output to stderr
```

### Structure Rationale

- **Config/:** Isolated so config loading and validation can be tested independently of Win32 calls.
- **Windows/:** Groups all window data acquisition — enumeration, filtering, geometry — as a pure data pipeline. No focus side effects here.
- **Scoring/:** Strategy pattern means new scoring approaches can be added without touching the pipeline. Each scorer is independently testable with mock `WindowInfo` records.
- **Focus/:** Single responsibility — the only component that mutates system state. Isolated makes it easy to stub in tests and reason about side effects.
- **Native/:** All P/Invoke in one file. Keeps the rest of the codebase clean of `DllImport`/`LibraryImport` attributes and Win32 types. Mirror the pattern from CsWin32 or Vanara.
- **Diagnostics/:** Separated so verbose logging can be stripped or redirected without affecting core logic.

## Architectural Patterns

### Pattern 1: Sequential Pipeline with Early Exit

**What:** The execution pipeline is a linear sequence of stages: enumerate → filter → resolve geometry → score → activate. Each stage operates on the output of the previous. The pipeline exits early with a specific exit code if a stage produces an empty result (e.g., no candidates after filtering).

**When to use:** This tool is stateless and invoked per-keypress. The pipeline runs once and terminates. No concurrency, no state to manage between calls.

**Trade-offs:** Simple to reason about and test. Debuggable by logging inputs/outputs at each stage boundary. No flexibility needed beyond what the pipeline provides.

**Example:**
```csharp
// Program.cs orchestration
var config = ConfigLoader.Load(configPath, args);
var allWindows = WindowEnumerator.GetTopLevel();
var visible = WindowFilter.Apply(allWindows, config.ExcludeList);
var withGeometry = WindowGeometry.Resolve(visible);

var current = withGeometry.FirstOrDefault(w => w.Hwnd == GetForegroundWindow());
if (current is null) return ExitCode.Error;

var scorer = ScorerFactory.Create(config.Strategy);
var best = scorer.FindBest(current, withGeometry, direction);
if (best is null) return HandleNone(config.WrapBehavior);

FocusActivator.Activate(best.Hwnd);
return ExitCode.Switched;
```

### Pattern 2: Strategy Pattern for Scoring

**What:** Each scoring algorithm implements a common interface (`IScorer`). The factory selects the concrete implementation at runtime based on config or CLI flag. All scorers receive the same inputs: the current window's `WindowInfo`, the candidate list, and the direction enum.

**When to use:** Multiple weighting strategies are a first-class project requirement. Strategy pattern avoids a large if/switch block and makes adding a new algorithm a matter of adding one new class.

**Trade-offs:** Minor overhead of interface dispatch. Worth it for the testability and extensibility gain.

**Example:**
```csharp
public interface IScorer
{
    WindowInfo? FindBest(WindowInfo current, IEnumerable<WindowInfo> candidates, Direction direction);
}

// Caller:
var scorer = ScorerFactory.Create(config.Strategy); // returns BalancedScorer, etc.
var target = scorer.FindBest(currentWindow, candidates, direction);
```

### Pattern 3: Direction Filter Before Scoring

**What:** Before running the scoring algorithm, filter candidates to only those in the general half-plane of the requested direction. For "right", keep only windows whose center X > current center X. This eliminates clearly-wrong candidates before computing scores.

**When to use:** Always. Without a direction pre-filter, windows behind the current window will contaminate scores and can produce nonsensical selections.

**Trade-offs:** None — this is pure correctness, not a design tradeoff. Direction filter must account for edge cases where windows partially overlap (center-point comparison is sufficient for this).

**Example:**
```csharp
// Direction filter before scoring
IEnumerable<WindowInfo> FilterByDirection(WindowInfo current, IEnumerable<WindowInfo> all, Direction dir)
{
    return dir switch
    {
        Direction.Right => all.Where(w => w.Center.X > current.Center.X),
        Direction.Left  => all.Where(w => w.Center.X < current.Center.X),
        Direction.Down  => all.Where(w => w.Center.Y > current.Center.Y),
        Direction.Up    => all.Where(w => w.Center.Y < current.Center.Y),
        _ => throw new ArgumentOutOfRangeException()
    };
}
```

### Pattern 4: Scoring Formula — Axis Distance + Perpendicular Penalty

**What:** The canonical formula for directional window scoring uses two components:
- **Primary axis distance** (D_primary): distance along the direction of travel (e.g., delta-X for "right")
- **Perpendicular offset** (D_perp): distance off the movement axis (e.g., delta-Y for "right")
- **Score** = D_primary + (weight * D_perp)

Lower score = better candidate. The weight multiplier controls how much off-axis displacement is penalized relative to axial distance:
- Balanced: weight ≈ 1.0 (equal penalty for off-axis drift)
- Strong axis bias: weight ≈ 3.0-5.0 (heavily penalize off-axis windows)
- Closest in direction: weight ≈ 0.0 (ignore perpendicular, pure nearest)

**When to use:** This formula is the established pattern used by i3wm, Hyprland, and Awesome WM for floating-window directional focus. All three strategies are variants of this formula with different weight multipliers, not fundamentally different algorithms.

**Example:**
```csharp
// Balanced scorer
public WindowInfo? FindBest(WindowInfo current, IEnumerable<WindowInfo> candidates, Direction dir)
{
    var filtered = FilterByDirection(current, candidates, dir);
    return filtered
        .Select(c => (window: c, score: ComputeScore(current, c, dir, perpWeight: 1.0)))
        .MinBy(x => x.score)
        .window;
}

double ComputeScore(WindowInfo from, WindowInfo to, Direction dir, double perpWeight)
{
    var (dx, dy) = (to.Center.X - from.Center.X, to.Center.Y - from.Center.Y);
    return dir switch
    {
        Direction.Right => dx + perpWeight * Math.Abs(dy),
        Direction.Left  => -dx + perpWeight * Math.Abs(dy),
        Direction.Down  => dy + perpWeight * Math.Abs(dx),
        Direction.Up    => -dy + perpWeight * Math.Abs(dx),
        _ => double.MaxValue
    };
}
```

## Data Flow

### Request Flow (Single Invocation)

```
[AutoHotkey hotkey fires: "focus.exe right"]
    |
    v
[Program.cs: parse "right" → Direction.Right]
    |
    v
[ConfigLoader: load ~/.config/windowfocus/config.json → AppConfig]
    |  (CLI flags override config fields)
    v
[WindowEnumerator: EnumWindows callback → List<HWND>]
    |  (~50-200 HWNDs typically)
    v
[WindowFilter: IsWindowVisible + DWMWA_CLOAKED + IsIconic + style check + exclude list]
    |  (reduces to ~5-20 real app windows)
    v
[WindowGeometry: DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) per HWND]
    |  (returns List<WindowInfo> with Rect and Center)
    v
[Identify current: GetForegroundWindow() → match in WindowInfo list]
    |
    v
[Direction filter: keep only windows whose center is in requested direction]
    |
    v
[IScorer.FindBest: compute score per candidate, return minimum]
    |
    ├── no candidates → WrapBehavior: no-op (exit 1) / wrap / beep
    |
    v
[FocusActivator: SendInput(VK_MENU down) → SetForegroundWindow(hwnd) → SendInput(VK_MENU up)]
    |
    v
[Exit 0 — success]
```

### Config Resolution Flow

```
[Default AppConfig values (hardcoded)]
    |
    v
[JSON config file values override defaults]
    |
    v
[CLI flag values override config values]
    |
    v
[Resolved AppConfig consumed by pipeline]
```

### Key Data Flows

1. **HWND list → WindowInfo records:** Enumeration produces raw HWNDs. Filtering culls them. Geometry resolution converts each surviving HWND into a typed `WindowInfo` record with process name, title, bounds RECT, and computed center Point. All downstream components work with `WindowInfo`, never raw HWNDs except at the activation step.

2. **Scoring is read-only:** The scoring layer only reads `WindowInfo` data. It produces a `WindowInfo?` (the best candidate, or null). No Win32 calls in the scoring layer — this keeps it testable without mocking P/Invoke.

3. **Win32 calls are boundary-contained:** Only `WindowEnumerator`, `WindowFilter`, `WindowGeometry`, and `FocusActivator` make Win32 calls. All through `NativeMethods.cs`. Everything between enumeration and activation is pure C# logic.

## Scaling Considerations

This tool is a stateless single-process CLI with a ~100ms time budget. "Scaling" means performance across different Windows environments, not user load.

| Scale Concern | Current Scope | Mitigation |
|---------------|---------------|------------|
| Window count | Typical desktop: 50-200 HWNDs from EnumWindows | No issue — O(n) pipeline on small n completes in <5ms |
| DwmGetWindowAttribute cost | One call per visible window (~5-20 after filtering) | Call only after filtering, not for all 200 HWNDs |
| Config file I/O | JSON read on every invocation (no persistent process) | File is small; System.Text.Json is fast; acceptable |
| Startup cost | .NET 8 process startup ~50-80ms typically | Use PublishSingleFile + ReadyToRun or NativeAOT to reduce; still within 100ms budget |
| Large exclude lists | Linear scan per window | List is user-defined and small; no optimization needed |

## Anti-Patterns

### Anti-Pattern 1: GetWindowRect Instead of DwmGetWindowAttribute

**What people do:** Use `GetWindowRect()` to get window bounds for geometry calculations.

**Why it's wrong:** On Windows 10+, `GetWindowRect()` returns an oversized RECT that includes invisible DWM shadow borders (typically 7-8px on each side). This makes windows appear to overlap when they don't visually, and shifts center-point calculations outward, producing wrong directional selections for windows close to each other.

**Do this instead:** Use `DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, ...)` which returns the true visible bounds the user sees. This is the documented, correct API for visible window dimensions on Windows 10+.

### Anti-Pattern 2: IsWindowVisible Alone for Filtering

**What people do:** Filter candidate windows using only `IsWindowVisible()`.

**Why it's wrong:** Windows 10+ "cloaks" windows that are on non-active virtual desktops. Cloaked windows still return `true` from `IsWindowVisible()` because they have `WS_VISIBLE` style set. Including them produces attempts to focus windows the user can't see.

**Do this instead:** Check both `IsWindowVisible()` AND `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, ...)`. Skip any window where the cloaked value is non-zero.

### Anti-Pattern 3: SetForegroundWindow Without Alt Bypass

**What people do:** Call `SetForegroundWindow(hwnd)` directly from a background process.

**Why it's wrong:** Windows restricts which processes can take foreground focus. A process invoked via AutoHotkey is not the foreground process and does not hold recent input events. `SetForegroundWindow()` will silently fail — it returns FALSE and Windows instead flashes the taskbar button.

**Do this instead:** Simulate an Alt keypress with `SendInput()` before calling `SetForegroundWindow()`. Windows grants foreground permission when it detects an Alt key event, because Alt is part of the Alt+Tab focus-switch UI flow. Release Alt afterward. This is a documented, reliable workaround — not a hack.

### Anti-Pattern 4: Scoring All Windows in All Directions

**What people do:** Run the scoring algorithm against all enumerated windows and pick the "best" globally.

**Why it's wrong:** Without a direction pre-filter (keeping only windows whose center is in the requested direction), a window directly behind the current window can outcompete a correct candidate. Also, a window far away on the perpendicular axis can score better than one that is genuinely "to the right" because the perpendicular penalty doesn't overcome the primary-axis advantage of windows that are almost on-axis behind.

**Do this instead:** Always apply a half-plane direction filter before scoring. Only score windows whose center point is strictly in the requested direction from the current window's center. Score to find the best among valid candidates.

### Anti-Pattern 5: Monolithic P/Invoke-and-Logic File

**What people do:** Put window enumeration, filtering, geometry, scoring, and P/Invoke declarations all in one large file or class.

**Why it's wrong:** Makes the scoring logic untestable (P/Invoke calls prevent unit testing without running on Windows with real windows), makes the filter logic hard to reason about, and couples the scoring algorithm to the Win32 surface.

**Do this instead:** Keep `NativeMethods.cs` as a pure declaration file with no logic. Separate enumeration, filtering, geometry, and scoring into distinct classes. The scorer takes only `WindowInfo` records (plain data), making it fully testable with in-memory test data.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Windows Win32 API (user32.dll) | P/Invoke via `DllImport` / `LibraryImport` | EnumWindows, IsWindowVisible, IsIconic, GetWindowLongPtr, GetForegroundWindow, SetForegroundWindow, SendInput |
| Windows DWM API (dwmapi.dll) | P/Invoke via `DllImport` / `LibraryImport` | DwmGetWindowAttribute with DWMWA_EXTENDED_FRAME_BOUNDS (visible bounds) and DWMWA_CLOAKED (filter virtual desktop windows) |
| JSON config file | `System.Text.Json` deserialization | Path: `%APPDATA%\windowfocusnav\config.json` or adjacent to exe. File is optional — defaults apply if absent. |
| AutoHotkey (invoker) | AutoHotkey spawns the process, passes direction as CLI arg, reads exit code | No IPC needed — tool is stateless, invoked per-call. Exit codes: 0=switched, 1=no candidate, 2=error. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Config → Pipeline | `AppConfig` record passed to pipeline entry | Resolved once at startup; immutable during execution |
| WindowEnumerator → WindowFilter | `List<nint>` (HWND list) | Raw HWNDs, no title/geometry yet |
| WindowFilter → WindowGeometry | `List<nint>` (filtered HWND list) | Only surviving HWNDs get geometry calls — saves Win32 round-trips |
| WindowGeometry → Scorer | `List<WindowInfo>` (typed records with Rect + Center) | No Win32 types cross this boundary; Scorer is pure logic |
| Scorer → FocusActivator | Single `nint` HWND of the winning window | Scorer returns `WindowInfo?`, entry point extracts HWND |
| Logger → All Components | Logger instance passed at construction or via static verbosity flag | Writes to stderr to keep stdout clean; gated on `--verbose` flag |

## Build Order Implications

The component dependency graph dictates this build order for phased development:

1. **Native/NativeMethods.cs first** — Every other component depends on P/Invoke declarations. Establish struct types (RECT, INPUT), constants (DWMWA_EXTENDED_FRAME_BOUNDS, DWMWA_CLOAKED), and extern signatures before writing components that call them.

2. **Windows/ layer second** — Enumeration, filtering, geometry, and the `WindowInfo` record can be built and manually smoke-tested end-to-end before any scoring exists. Dump the list of discovered windows to validate filtering logic.

3. **Scoring/ layer third** — Scorers depend only on `WindowInfo` and the `Direction` enum. Build and unit-test scorers with in-memory test data (no Win32 needed). This is where the core UX algorithm lives — iterate here.

4. **Focus/ layer fourth** — `FocusActivator` depends on a winning HWND. Build after scoring is validated. Test manually (not unit-testable without real windows).

5. **Config/ and CLI layer fifth** — Wire up JSON config loading and CLI arg parsing last, once the pipeline is working. Config layer depends on knowing which settings the pipeline consumes.

6. **Diagnostics/ throughout** — Logger can be a stub early and fleshed out in parallel. Verbose window enumeration output is essential for debugging filtering and scoring.

## Sources

- [EnumWindows — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows) — HIGH confidence, official API documentation
- [DwmGetWindowAttribute — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute) — HIGH confidence, official API documentation
- [SetForegroundWindow — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) — HIGH confidence, official API documentation with restriction conditions documented
- [SetForegroundWindow bypass via Alt key (gist)](https://gist.github.com/Aetopia/1581b40f00cc0cadc93a0e8ccb65dc8c) — MEDIUM confidence, verified against SetForegroundWindow official docs which confirm Alt key grants permission
- [Window cloaking — The Old New Thing (Raymond Chen)](https://devblogs.microsoft.com/oldnewthing/20200302-00/?p=103507) — HIGH confidence, official Microsoft blog post
- [DWMWINDOWATTRIBUTE enumeration — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) — HIGH confidence, official API documentation
- [Positioning Objects on Multiple Display Monitors — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/gdi/positioning-objects-on-multiple-display-monitors) — HIGH confidence, official documentation; confirms virtual screen coordinate approach
- [CsWin32 — Microsoft GitHub](https://github.com/microsoft/CsWin32) — MEDIUM confidence, actively maintained Microsoft tool for P/Invoke source generation
- [GlazeWM — GitHub](https://github.com/glzr-io/glazewm) — LOW confidence (architecture not inspected in detail), confirms directional focus navigation pattern on Windows
- [Komorebi — GitHub](https://github.com/LGUG2Z/komorebi) — LOW confidence (architecture overview only), confirms event-based window management pattern and komorebic CLI pattern

---
*Architecture research for: Win32 directional window focus navigation CLI tool*
*Researched: 2026-02-26*
