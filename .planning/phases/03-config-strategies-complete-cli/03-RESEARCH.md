# Phase 3: Config, Strategies & Complete CLI - Research

**Researched:** 2026-02-27
**Domain:** .NET 8 JSON config, glob wildcard matching, System.CommandLine option additions, Win32 MessageBeep, directional scoring algorithm variants
**Confidence:** HIGH (all core .NET APIs verified via official docs; Win32 MessageBeep verified via official MS docs; glob matching verified via official MS docs; scoring algorithm design is Claude's discretion with well-established geometric foundation)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Config file location & schema:**
- Config lives at `%APPDATA%\focus\config.json`
- When no config file exists, use hardcoded defaults silently — tool works out of the box with zero setup
- Support `focus --init-config` to generate a starter config file with all defaults and comments explaining each option
- Exclude list uses glob/wildcard patterns (e.g., `"Teams*"`, `"*Chrome*"`) — no regex needed

**Strategy feel & defaults:**
- Default strategy is **balanced** (already implemented in Phase 2)
- **Strong-axis-bias**: mild lane preference — lightly biases toward windows aligned on the navigation axis, but still considers off-axis candidates. Subtle but noticeable difference from balanced
- **Closest-in-direction**: pure distance with wide cone (~90°) — picks the nearest window center-to-center as long as it's roughly in the direction. Simple mental model: closest wins
- Fixed presets for v1 — no user-tunable weight parameters (CFG-05 deferred to v2)

**CLI flag naming & overrides:**
- Long flags only: `--strategy`, `--wrap`, `--exclude`, `--verbose`, `--debug`, `--init-config`. No short flags — this is hotkey-driven, not typed frequently
- CLI wins, simple merge: a CLI flag overrides the same config key entirely (no list merging — `--exclude` replaces the config exclude list)
- Debug uses `--debug <mode>` pattern: `focus --debug enumerate`, `focus --debug score left`, `focus --debug config` (consistent with Phase 1)
- `--verbose` output goes to stderr; stdout stays empty on normal operation

### Claude's Discretion

- Wrap-around behavior defaults and implementation details (user did not select this for discussion)
- Config file JSON schema structure (key names, nesting)
- `--init-config` output format and comment style
- Debug output formatting for `--debug score` and `--debug config`
- Error messages and validation for invalid config values

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CFG-01 | Tool reads settings from a JSON config file | System.Text.Json deserialization with defaults pattern; `%APPDATA%\focus\config.json` via `Environment.GetFolderPath(SpecialFolder.ApplicationData)` |
| CFG-02 | User invokes tool with direction argument (e.g., `focus left`) | Already complete in Phase 2 — no new work; confirmed by Phase 2 PLAN and current Program.cs |
| CFG-03 | User can override config settings via CLI flags | System.CommandLine 2.0 `Option<T>` additions to existing root command; check for non-null/non-default CLI value and override config field |
| CFG-04 | Config file supports strategy, wrap behavior, and exclude list settings | FocusConfig POCO with properties: Strategy (enum), Wrap (enum), Exclude (string[]); System.Text.Json deserializes with property-level defaults |
| ENUM-07 | Tool supports user-configurable exclude list by process name with regex/wildcard patterns | `Microsoft.Extensions.FileSystemGlobbing.Matcher.Match(string)` — in-memory, no I/O; case-insensitive via `StringComparison.OrdinalIgnoreCase`; applied after enumeration, before navigation |
| NAV-08 | Tool supports "strong-axis-bias" weighting strategy | Modified scoring: higher secondary weight (e.g., 4.0–6.0 vs balanced's 2.0) — lightly penalizes off-axis deviation more; same nearest-edge distance measurement as balanced |
| NAV-09 | Tool supports "closest-in-direction" weighting strategy | Simplified scoring: wide directional cone check (angle < 90° from nav axis), then pure center-to-center distance as score — no axis decomposition |
| FOCUS-02 | Tool supports configurable wrap-around behavior (wrap / no-op / beep) | wrap: when no candidates in direction, find best candidate in opposite direction; no-op: return exit code 1; beep: `MessageBeep(0xFFFFFFFF)` via CsWin32 then return exit code 1 |
| OUT-01 | Tool is silent by default (no output on success) | Already partially implemented (no stdout on success path); verify no Console.WriteLine leaks in navigation path; `--verbose` to stderr already wired in Phase 2 |
| OUT-03 | User can enable verbose/debug output showing scored candidates via --verbose flag | Already partially implemented in Phase 2 for navigation path; needs to show all 3 strategy scores in `--debug score` mode |
| DBG-02 | User can run `--debug score <direction>` to show all candidates with their scores without switching focus | New `--debug score <direction>` mode: enumerate, run all 3 strategies, print table with window + score-per-strategy, return exit 0 without switching focus |
| DBG-03 | User can run `--debug config` to show resolved config (defaults + file + overrides) | New `--debug config` mode: build resolved FocusConfig, serialize to readable format, print to stdout |
</phase_requirements>

---

## Summary

Phase 3 adds the configuration layer, three scoring strategies, and completes the CLI surface on top of Phase 2's working navigation pipeline. The existing `Program.cs` wiring is already in place for `--debug`, `--verbose`, and the direction argument. Phase 3 extends this with 4 new CLI options (`--strategy`, `--wrap`, `--exclude`, `--init-config`), extends `--debug` to handle `score` and `config` modes, and introduces two new files: `FocusConfig.cs` (the config model + loader) and changes to `NavigationService.cs` to support strategy selection.

The config system uses `System.Text.Json` with a POCO class that has C# property initializers as defaults. Deserialization replaces only the properties present in the JSON file, so missing keys naturally fall back to the C# defaults — no custom merge logic needed. `%APPDATA%\focus\config.json` is resolved via `Environment.GetFolderPath(SpecialFolder.ApplicationData)` which returns `C:\Users\USERNAME\AppData\Roaming` on Windows.

Process-name exclude list filtering uses `Microsoft.Extensions.FileSystemGlobbing.Matcher.Match(string)` — an in-memory API that performs no filesystem I/O. The `Matcher` is instantiated with `StringComparison.OrdinalIgnoreCase` so patterns like `"Teams*"` match `"Teams.exe"` case-insensitively. This requires adding the `Microsoft.Extensions.FileSystemGlobbing` NuGet package. The alternative (hand-rolling wildcard matching with Regex) is explicitly in the "don't hand-roll" category.

The two new scoring strategies are variations of the existing `ScoreCandidate` method. `strong-axis-bias` uses a higher secondary weight constant (stronger lane preference). `closest-in-direction` uses a wide angle cone filter and pure Euclidean distance. All three strategies are implemented as static methods on `NavigationService` (or a new `ScoringStrategies` class), with a strategy enum selecting which one runs.

**Primary recommendation:** Build in three sequential units: (1) `FocusConfig.cs` — POCO, loader, `--init-config` writer; (2) strategy variants in `NavigationService.cs` + `--debug score`; (3) complete CLI wiring in `Program.cs` — all new options, `--debug config`, and exclude filtering in the enumeration → navigation pipeline.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Text.Json | (BCL, .NET 8) | Read/write JSON config file | Already in .NET 8 BCL; no NuGet package needed; JsonSerializer.Deserialize with default POCO properties handles config merge naturally |
| Microsoft.Extensions.FileSystemGlobbing | 8.0.0 or latest 8.x | In-memory wildcard matching for process name exclude list | Official MS library; `Matcher.Match(string)` works without filesystem I/O; supports `*`, `?`, `**` glob patterns; case-insensitive mode built in |
| System.CommandLine | 2.0.3 (already in project) | Add `--strategy`, `--wrap`, `--exclude`, `--init-config` options to existing root command | Already wired; new `Option<T>` instances added to `rootCommand.Options` |
| CsWin32 | 0.3.269 (already in project) | `MessageBeep` for beep wrap-around behavior | Add `MessageBeep` to NativeMethods.txt; CsWin32 generates the typed binding |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Environment | (BCL) | `Environment.GetFolderPath(SpecialFolder.ApplicationData)` for APPDATA path | Used in FocusConfig.GetConfigPath() |
| System.IO.File | (BCL) | `File.Exists`, `File.ReadAllText`, `File.WriteAllText` for config file I/O | Config load and `--init-config` write |

### New NuGet Package Required

```
Microsoft.Extensions.FileSystemGlobbing
```

This is not in-box with .NET 8 BCL — it is a separate NuGet package. Use version 8.0.0 to match the project's .NET 8 target framework.

```xml
<PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="8.0.0" />
```

### New NativeMethods.txt Entry Required

```
MessageBeep
```

`MessageBeep` is in `winuser.h` / `User32.dll`. CsWin32 will generate the binding. The generated call is `PInvoke.MessageBeep(uType)` where `uType` is `uint`. Use `0xFFFFFFFF` for a simple speaker beep (system default beep).

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Microsoft.Extensions.FileSystemGlobbing | Regex hand-rolled with `^` anchors and `.*` | Regex works but forces users to write regex syntax; `*Chrome*` as a glob is more user-friendly than `.*Chrome.*`; globbing library handles edge cases correctly |
| System.Text.Json defaults via C# initializers | JSON merge/patch library | JSON merge libraries are overkill for a 3–5 key config; C# property initializers + deserialize-replaces pattern is simpler and sufficient |
| MessageBeep(0xFFFFFFFF) | Console.Beep() / Beep() Win32 API | `Console.Beep()` requires console attached; `MessageBeep` plays the system sound scheme; `MessageBeep` is preferred for GUI-adjacent tools. Note: `MessageBeep` is NOT redirected to remote clients; for RDP use `Beep()` instead — not a concern for this tool |

---

## Architecture Patterns

### Recommended Project Structure (Phase 3 additions)

```
focus/
├── focus.csproj                       # Add Microsoft.Extensions.FileSystemGlobbing
├── Program.cs                         # Add --strategy, --wrap, --exclude, --init-config options; extend --debug
├── NativeMethods.txt                  # Add MessageBeep
└── Windows/
    ├── FocusConfig.cs                 # (NEW) Config POCO, defaults, load/save, init-config template
    ├── ExcludeFilter.cs               # (NEW) Process-name glob matching via FileSystemGlobbing
    ├── NavigationService.cs           # (MODIFY) Add strategy parameter/overloads, implement NAV-08 and NAV-09
    ├── FocusActivator.cs              # (MODIFY) Add wrap-around behavior, MessageBeep
    ├── WindowEnumerator.cs            # (unchanged)
    ├── WindowInfo.cs                  # (unchanged)
    ├── Direction.cs                   # (unchanged)
    └── MonitorHelper.cs               # (unchanged)
```

### Pattern 1: Config POCO with C# Initializer Defaults

**What:** A plain C# class with property initializers representing defaults. `JsonSerializer.Deserialize<FocusConfig>` replaces only properties present in the JSON — missing keys keep the C# default.

**When to use:** Any time you have a small config file where defaults must be meaningful without the file existing.

```csharp
// Source: System.Text.Json official docs — default (Replace) behavior
// In FocusConfig.cs
namespace Focus.Windows;

internal enum Strategy { Balanced, StrongAxisBias, ClosestInDirection }
internal enum WrapBehavior { NoOp, Wrap, Beep }

internal class FocusConfig
{
    // Defaults — these apply when the config file is missing or the key is absent
    public Strategy Strategy { get; set; } = Strategy.Balanced;
    public WrapBehavior Wrap { get; set; } = WrapBehavior.NoOp;
    public string[] Exclude { get; set; } = [];

    /// <summary>
    /// Returns resolved path to config file: %APPDATA%\focus\config.json
    /// </summary>
    public static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "focus", "config.json");
    }

    /// <summary>
    /// Loads config from disk, applying defaults for missing/absent file.
    /// Never throws — logs warning and returns defaults on parse error.
    /// </summary>
    public static FocusConfig Load()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
            return new FocusConfig(); // all defaults

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            return JsonSerializer.Deserialize<FocusConfig>(json, options) ?? new FocusConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[focus] Warning: config parse error ({ex.Message}); using defaults.");
            return new FocusConfig();
        }
    }
}
```

**Key detail:** `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` allows the JSON to use `"balanced"` / `"strongAxisBias"` / `"closestInDirection"` as enum values. Without this, the JSON must use integer values. CamelCase is the most natural JSON convention for enum strings.

**Key detail:** `JsonSerializerOptions.PropertyNameCaseInsensitive = true` allows `"Strategy"`, `"strategy"`, or `"STRATEGY"` all to deserialize correctly — important for a user-edited config file.

### Pattern 2: JSON Schema Structure (Claude's Discretion)

**Recommended schema:**

```json
{
  "strategy": "balanced",
  "wrap": "noOp",
  "exclude": [
    "Teams*",
    "*Chrome*"
  ]
}
```

**Key names:** camelCase to match JSON conventions. Valid `strategy` values: `"balanced"`, `"strongAxisBias"`, `"closestInDirection"`. Valid `wrap` values: `"noOp"`, `"wrap"`, `"beep"`.

### Pattern 3: --init-config Template Writer (Claude's Discretion)

**Recommendation:** Use `Utf8JsonWriter` or string templating to write a JSON file with inline `//` comment lines (as a block comment file, not valid strict JSON). This makes the config self-documenting. However, since `System.Text.Json` requires `JsonCommentHandling.Skip` to read comments, document this in the file header. Alternatively, write a valid JSON file with no comments — simpler and always parseable. **Recommendation: valid JSON without comments** because `System.Text.Json` does not write comments natively, and the `--debug config` output already shows the resolved values.

```csharp
// Recommended: Write valid JSON using JsonSerializer.Serialize with indentation
var defaults = new FocusConfig();
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
var json = JsonSerializer.Serialize(defaults, options);
Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
File.WriteAllText(configPath, json);
Console.WriteLine($"Config written to: {configPath}");
```

This produces:
```json
{
  "strategy": "balanced",
  "wrap": "noOp",
  "exclude": []
}
```

If comments are desired: use `JsonCommentHandling.Skip` in the read options AND write a known-format comment block via `File.WriteAllText` with a string template. This requires Claude's discretion on whether the startup cost of hardcoded comment strings is worth it — recommended only if the user explicitly wants a self-documenting config.

### Pattern 4: Config + CLI Merge (CFG-03)

**What:** CLI flags override config. Since `--exclude` replaces (not merges) the config list, the merge is a simple priority check: if CLI value is non-null/non-empty, use it; otherwise use the config value.

```csharp
// In Program.cs SetAction lambda — after loading config
var config = FocusConfig.Load();

// Apply CLI overrides (CLI wins)
var strategyOverride = parseResult.GetValue(strategyOption);      // string? or enum
var wrapOverride = parseResult.GetValue(wrapOption);              // string? or enum
var excludeOverride = parseResult.GetValue(excludeOption);        // string[]?

if (strategyOverride is not null) config.Strategy = strategyOverride;
if (wrapOverride is not null) config.Wrap = wrapOverride;
if (excludeOverride is { Length: > 0 }) config.Exclude = excludeOverride;
```

**Note on CLI option types:** Use `Option<string?>` for `--strategy` and `--wrap` (string → enum parse manually, for better error messages). Use `Option<string[]>` with `AllowMultipleArgumentsPerToken = true` for `--exclude` to allow `--exclude "Teams*" "*Chrome*"`.

### Pattern 5: Process Name Exclude Filtering (ENUM-07)

**What:** After `WindowEnumerator.GetNavigableWindows()`, filter out windows whose `ProcessName` matches any exclude pattern. Uses `Microsoft.Extensions.FileSystemGlobbing.Matcher.Match(string)` — in-memory only.

**Critical detail:** `Matcher.Match(string)` treats the input as a *file path* internally. For a bare filename like `Teams.exe`, the pattern `Teams*` (without path separators) will match correctly because `*` matches zero-or-more chars excluding path separators. The pattern `*Teams*` also works. Use `StringComparison.OrdinalIgnoreCase` constructor to ensure case-insensitive matching.

```csharp
// In ExcludeFilter.cs — or as a static method in WindowEnumerator
// Source: Microsoft.Extensions.FileSystemGlobbing docs (updated 2025-12-23)

using Microsoft.Extensions.FileSystemGlobbing;

internal static class ExcludeFilter
{
    /// <summary>
    /// Returns true if the process name matches any exclude pattern.
    /// Patterns use glob wildcards: * (any chars, no separator), ? (single char).
    /// Matching is case-insensitive.
    /// </summary>
    public static bool IsExcluded(string processName, IEnumerable<string> excludePatterns)
    {
        var patterns = excludePatterns as string[] ?? excludePatterns.ToArray();
        if (patterns.Length == 0)
            return false;

        // OrdinalIgnoreCase: "Teams.exe" matches pattern "teams*"
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(patterns);

        // Match(string) — no filesystem I/O; matches processName against all include patterns
        var result = matcher.Match(processName);
        return result.HasMatches;
    }

    /// <summary>
    /// Filters a window list, removing windows whose process name matches any exclude pattern.
    /// </summary>
    public static List<WindowInfo> Apply(List<WindowInfo> windows, string[] excludePatterns)
    {
        if (excludePatterns.Length == 0)
            return windows;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(excludePatterns);

        return windows
            .Where(w => !matcher.Match(w.ProcessName).HasMatches)
            .ToList();
    }
}
```

**Where to call:** In `Program.cs` navigation path, after `enumerator.GetNavigableWindows()` and before `NavigationService.GetRankedCandidates()`. Also called in `--debug enumerate` and `--debug score` modes (exclude filter should be active in all operational modes).

### Pattern 6: Strong-Axis-Bias Strategy (NAV-08)

**What:** Same formula as balanced (primary + secondary weighted sum), but the secondary weight is increased to create a more pronounced lane preference. A window directly to the right beats a diagonally offset one more aggressively.

**Locked decision:** "Mild lane preference — lightly biases toward windows aligned on the navigation axis, but still considers off-axis candidates. Subtle but noticeable difference from balanced."

**Recommended implementation (Claude's discretion on weights):**

```csharp
// In NavigationService.cs — additional overload or strategy enum parameter
internal static double ScoreStrongAxisBias(
    double originX, double originY,
    WindowInfo candidate,
    Direction direction)
{
    var (nearX, nearY) = NearestPoint(originX, originY,
        candidate.Left, candidate.Top, candidate.Right, candidate.Bottom);

    bool inDirection = direction switch
    {
        Direction.Left  => nearX < originX,
        Direction.Right => nearX > originX,
        Direction.Up    => nearY < originY,
        Direction.Down  => nearY > originY,
        _ => false
    };
    if (!inDirection) return double.MaxValue;

    double primaryDist = direction switch
    {
        Direction.Left  => originX - nearX,
        Direction.Right => nearX - originX,
        Direction.Up    => originY - nearY,
        Direction.Down  => nearY - originY,
        _ => double.MaxValue
    };

    double secondaryDist = direction switch
    {
        Direction.Left or Direction.Right => Math.Abs(nearY - originY),
        Direction.Up   or Direction.Down  => Math.Abs(nearX - originX),
        _ => double.MaxValue
    };

    // Higher secondary weight = more aggressive lane preference
    // Balanced uses 2.0; StrongAxisBias uses 5.0 (Claude's discretion — tune during verification)
    const double primaryWeight   = 1.0;
    const double secondaryWeight = 5.0;

    return primaryWeight * primaryDist + secondaryWeight * secondaryDist;
}
```

**Recommended weight:** `secondaryWeight = 5.0` (versus 2.0 for balanced). This makes a window 1 pixel to the right but 500 pixels off-axis score worse than one 250 pixels directly to the right. Tune empirically during verification.

### Pattern 7: Closest-In-Direction Strategy (NAV-09)

**What:** Wide cone filter (the window center must be roughly in the navigation direction — angle < 90° from the nav axis), then pure Euclidean distance center-to-center as the score. No axis decomposition.

**Locked decision:** "Pure distance with wide cone (~90°) — picks the nearest window center-to-center as long as it's roughly in the direction."

```csharp
internal static double ScoreClosestInDirection(
    double originX, double originY,
    WindowInfo candidate,
    Direction direction)
{
    // Use candidate center-to-center (not nearest edge) — locked: "pure distance"
    double candCx = (candidate.Left + candidate.Right) / 2.0;
    double candCy = (candidate.Top + candidate.Bottom) / 2.0;

    double dx = candCx - originX;
    double dy = candCy - originY;

    // Wide cone filter: the candidate center must be in the "half-plane" for the direction
    // (~90° cone means the primary-axis component must be positive)
    bool inCone = direction switch
    {
        Direction.Left  => dx < 0,   // center is to the left
        Direction.Right => dx > 0,   // center is to the right
        Direction.Up    => dy < 0,   // center is above
        Direction.Down  => dy > 0,   // center is below
        _ => false
    };
    if (!inCone) return double.MaxValue;

    // Pure Euclidean distance center-to-center
    return Math.Sqrt(dx * dx + dy * dy);
}
```

**Design rationale:** The "wide cone" is implemented as a half-plane check (primary axis component > 0), which is exactly a 90° cone centered on the navigation direction. Any window whose center is in the same half-plane is eligible — only pure distance determines the winner. This matches the locked description: "picks the nearest window center-to-center as long as it's roughly in the direction."

**Note:** This strategy uses window centers (not nearest edges) because the description explicitly says "center-to-center distance." This is different from balanced and strong-axis-bias which use nearest-edge distance.

### Pattern 8: Strategy Dispatch in NavigationService

**What:** Add a `Strategy` parameter to `GetRankedCandidates` so the scoring strategy is configurable at call time.

```csharp
// In NavigationService.cs
public static List<(WindowInfo Window, double Score)> GetRankedCandidates(
    List<WindowInfo> allWindows,
    Direction direction,
    Strategy strategy = Strategy.Balanced) =>
    GetRankedCandidates(allWindows, direction, strategy, out _, out _, out _);

public static List<(WindowInfo Window, double Score)> GetRankedCandidates(
    List<WindowInfo> allWindows,
    Direction direction,
    Strategy strategy,
    out nint foregroundHwnd,
    out double originX,
    out double originY)
{
    // ... existing origin setup code ...

    Func<double, double, WindowInfo, Direction, double> scoreFn = strategy switch
    {
        Strategy.Balanced           => ScoreCandidate,
        Strategy.StrongAxisBias     => ScoreStrongAxisBias,
        Strategy.ClosestInDirection => ScoreClosestInDirection,
        _                           => ScoreCandidate
    };

    // ... existing filter/score/sort loop using scoreFn ...
}
```

### Pattern 9: Wrap-Around Behavior (FOCUS-02, Claude's Discretion)

**What:** When no candidates exist in the requested direction, the WrapBehavior enum controls what happens.

```csharp
// In FocusActivator.cs — new method or modification to ActivateBestCandidate
public static int ActivateWithWrap(
    List<(WindowInfo Window, double Score)> candidates,
    List<WindowInfo> allWindows,
    Direction direction,
    Strategy strategy,
    WrapBehavior wrap,
    bool verbose)
{
    if (candidates.Count > 0)
        return ActivateBestCandidate(candidates, verbose);

    // No candidates in this direction
    return wrap switch
    {
        WrapBehavior.Wrap => HandleWrap(allWindows, direction, strategy, verbose),
        WrapBehavior.Beep => HandleBeep(),
        _ => 1  // NoOp: return exit code 1 (no candidates)
    };
}

private static int HandleWrap(
    List<WindowInfo> allWindows, Direction direction, Strategy strategy, bool verbose)
{
    // Wrap: try opposite direction
    var opposite = direction switch
    {
        Direction.Left  => Direction.Right,
        Direction.Right => Direction.Left,
        Direction.Up    => Direction.Down,
        Direction.Down  => Direction.Up,
        _ => direction
    };

    var wrapped = NavigationService.GetRankedCandidates(allWindows, opposite, strategy);
    if (wrapped.Count == 0)
        return 1; // Nothing in any direction
    return ActivateBestCandidate(wrapped, verbose);
}

[SupportedOSPlatform("windows5.0")]
private static int HandleBeep()
{
    PInvoke.MessageBeep(0xFFFFFFFF); // simple beep (system default or speaker)
    return 1; // still exit code 1 (no focus switch)
}
```

**Default WrapBehavior:** `NoOp` (return exit code 1 silently). This is the safest default — no unexpected sounds, no confusing focus jumps when the cursor is at the screen edge.

**Wrap behavior for "wrap" mode:** Navigate to the furthest window in the opposite direction. This gives keyboard-navigation-style wraparound (left at leftmost → jumps to rightmost).

### Pattern 10: --debug score Mode (DBG-02)

**What:** Enumerate windows, apply exclude filter, run all 3 strategies, print candidate table with scores per strategy, return exit 0 without switching focus.

```csharp
// In Program.cs, extended --debug handling
if (debugValue == "score")
{
    // direction is a required second argument for "score" mode
    // e.g., focus --debug score left
    var scoreDirection = DirectionParser.Parse(directionValue);
    if (scoreDirection is null)
    {
        Console.Error.WriteLine("Usage: focus --debug score <direction>");
        return 2;
    }

    var (windows, _) = enumerator.GetNavigableWindows();
    var filtered = ExcludeFilter.Apply(windows, config.Exclude);

    // Run all three strategies
    var balanced     = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.Balanced);
    var strongBias   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.StrongAxisBias);
    var closestDir   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.ClosestInDirection);

    PrintScoreTable(balanced, strongBias, closestDir, scoreDirection.Value);
    return 0;
}
```

**Recommended output format (Claude's discretion):**

```
WINDOW                                     BALANCED   STRONG-AXIS  CLOSEST
----------------------------------------------------------------------
"Visual Studio Code"  (Code.exe)            142.3       311.5        982.1
"Windows Terminal"    (WindowsTerminal.exe)  89.1       178.2        641.3
```

### Pattern 11: --debug config Mode (DBG-03)

**What:** Show the fully resolved config (defaults + file + CLI overrides) to stdout.

```csharp
if (debugValue == "config")
{
    // config is already resolved (defaults → file → CLI overrides applied above)
    Console.WriteLine($"Config file: {FocusConfig.GetConfigPath()}");
    Console.WriteLine($"  exists: {File.Exists(FocusConfig.GetConfigPath())}");
    Console.WriteLine($"  strategy: {config.Strategy}");
    Console.WriteLine($"  wrap: {config.Wrap}");
    Console.WriteLine($"  exclude: [{string.Join(", ", config.Exclude.Select(p => $"\"{p}\""))}]");
    return 0;
}
```

### Pattern 12: New CLI Options

**What:** Add to `Program.cs` before `rootCommand.SetAction`:

```csharp
// Add after existing debugOption and verboseOption declarations

var strategyOption = new Option<string?>("--strategy")
{
    Description = "Scoring strategy: balanced | strong-axis-bias | closest-in-direction"
};

var wrapOption = new Option<string?>("--wrap")
{
    Description = "Wrap-around behavior: no-op | wrap | beep"
};

var excludeOption = new Option<string[]>("--exclude")
{
    Description = "Exclude processes by name pattern (overrides config exclude list)",
    AllowMultipleArgumentsPerToken = true
};
excludeOption.Arity = ArgumentArity.ZeroOrMore;

var initConfigOption = new Option<bool>("--init-config")
{
    Description = "Write a starter config file to %APPDATA%\\focus\\config.json"
};

rootCommand.Options.Add(strategyOption);
rootCommand.Options.Add(wrapOption);
rootCommand.Options.Add(excludeOption);
rootCommand.Options.Add(initConfigOption);
```

**Note on `--debug score <direction>`:** The `--debug` option is already `Option<string?>` with `ZeroOrOne` arity. The `score` mode requires a direction, which is already the `directionArgument` positional. So `focus --debug score left` works: `--debug` = `"score"`, `directionArgument` = `"left"`. No changes needed to the argument structure — just extend the `--debug` dispatch logic.

### Anti-Patterns to Avoid

- **Deserializing config with `JsonRequired` attributes:** Config keys should be optional — `JsonRequired` would throw if any key is missing from the file.
- **Storing config as a static/global:** Config must be resolved once per invocation (defaults → file → CLI overrides) and passed into services. A static `FocusConfig.Current` would make testing harder and obscures the override precedence.
- **Using Regex for process name matching:** Glob syntax (`Teams*`, `*Chrome*`) is what the user expects. Regex requires users to write `^Teams` and `.*Chrome.*`. Use the `Matcher` library.
- **Calling `Matcher.Execute(DirectoryInfoWrapper(...))` for process name matching:** This performs filesystem I/O. Use `Matcher.Match(processName)` (the extension method) instead — it matches the string directly without filesystem access.
- **Re-creating Matcher per window:** Create the `Matcher` once with all exclude patterns, then call `matcher.Match(w.ProcessName)` for each window. Matcher construction is relatively cheap but repeated reconstruction is wasteful.
- **Returning stdout output on success:** `OUT-01` requires silence on success. Any `Console.WriteLine` in the navigation path (not `--verbose`/`--debug`) is a bug.
- **Using `Console.Beep()` for the beep wrap behavior:** `Console.Beep()` requires a console window attached and blocks. `MessageBeep(0xFFFFFFFF)` is asynchronous, plays the system sound, and works from any process context.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Wildcard pattern matching (`Teams*`, `*Chrome*`) | Custom `string.Contains` + `StartsWith` logic | `Microsoft.Extensions.FileSystemGlobbing.Matcher` | Correct `*` and `?` semantics; handles edge cases (`*` at start/end, multiple wildcards); in-memory match API; officially maintained |
| Config file path (`%APPDATA%\focus\config.json`) | `Environment.GetEnvironmentVariable("APPDATA")` | `Environment.GetFolderPath(SpecialFolder.ApplicationData)` | `GetFolderPath` is the correct API; handles Windows APPDATA redirection and roaming profile scenarios; `GetEnvironmentVariable` may not be set in all contexts |
| JSON enum deserialization (string → enum) | Manual switch on string values | `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` | Handles all cases, null safety, and case policy automatically; no custom switch to maintain |

**Key insight:** The process-name exclude list looks simple (it's just pattern matching strings) but wildcard semantics — especially `*` at both ends (`*Chrome*`) vs anchored start (`Chrome*`) — have edge cases. The `Matcher` library has been tested at scale in MSBuild/dotnet CLI tooling. Custom implementations commonly get `?` wrong or fail on patterns with multiple `*`.

---

## Common Pitfalls

### Pitfall 1: Matcher.Match() Path-Separator Sensitivity

**What goes wrong:** `Matcher.Match("ApplicationFrameWindow")` against pattern `Application*` works, but `Matcher.Match("foo/bar.exe")` against pattern `foo*` does NOT match because `/` is a path separator and `*` does not cross separator boundaries.

**Why it happens:** `FileSystemGlobbing.Matcher` was designed for file paths. `*` stops at path separators. Process names like `Microsoft.Teams.exe` contain `.` (not `/`) so this is safe — but `QueryFullProcessImageName` returns full paths like `C:\Program Files\Teams\Teams.exe`.

**How to avoid:** In `ExcludeFilter`, pass only `Path.GetFileName(processName)` (the bare filename, e.g., `Teams.exe`) to `matcher.Match()`, not the full process path. `WindowInfo.ProcessName` is already set to `Path.GetFileName(...)` in `WindowEnumerator.GetProcessName()` — confirm this is the case before adding the filter.

**Warning signs:** Exclude pattern `Teams*` fails to exclude Teams windows even though the process name in `--debug enumerate` shows `Teams.exe`.

### Pitfall 2: JsonStringEnumConverter with Wrong Naming Policy

**What goes wrong:** Config file `"strategy": "strongAxisBias"` fails to deserialize; tool silently uses default `balanced` instead.

**Why it happens:** If `JsonStringEnumConverter` is not added, System.Text.Json only accepts integer values for enum properties by default. If it's added without `JsonNamingPolicy.CamelCase`, the expected JSON value is `"StrongAxisBias"` (PascalCase, the C# enum member name) not `"strongAxisBias"`.

**How to avoid:** Use `new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` in `JsonSerializerOptions.Converters`. This produces and consumes camelCase enum strings — the convention that matches the recommended JSON schema.

**Warning signs:** `--debug config` shows `strategy: Balanced` even when config file has `"strategy": "strongAxisBias"`. No error message, just silent fallback to default.

### Pitfall 3: CLI Option Arity for --exclude Array

**What goes wrong:** `focus --exclude "Teams*" "*Chrome*"` fails to parse, or only the first value is captured.

**Why it happens:** System.CommandLine 2.0 `Option<string[]>` defaults to `AllowMultipleArgumentsPerToken = false`. Without setting `AllowMultipleArgumentsPerToken = true` AND `Arity = ArgumentArity.ZeroOrMore`, multiple values after a single `--exclude` token are not parsed as a collection.

**How to avoid:** Set both properties on the `Option<string[]>`:

```csharp
excludeOption.AllowMultipleArgumentsPerToken = true;
excludeOption.Arity = ArgumentArity.ZeroOrMore;
```

**Warning signs:** `parseResult.GetValue(excludeOption)` returns only the first pattern; or a parse error when two space-separated values follow `--exclude`.

### Pitfall 4: --debug score Requires Direction Argument

**What goes wrong:** `focus --debug score` (without direction) confusingly returns "Unknown debug mode" or crashes with null reference.

**Why it happens:** The direction is the existing positional `directionArgument`. When `--debug score` is handled, the code must check that `directionValue` was also provided.

**How to avoid:** In the `--debug score` branch, explicitly check `directionValue` for null/empty before calling `DirectionParser.Parse`. If missing, print usage hint: `"Usage: focus --debug score <direction>"` and return exit 2.

**Warning signs:** `focus --debug score` (no direction) crashes or gives a confusing error.

### Pitfall 5: Wrap Behavior "Wrap" Mode Infinite Loop

**What goes wrong:** When `wrap = "wrap"` and there are NO windows visible at all (e.g., all windows minimized), the code navigates to the "opposite" direction which also has no candidates, loops, or returns wrong exit code.

**Why it happens:** If the enumerated window list is empty or has only the foreground window, both the original direction and the opposite direction have no candidates. A naive wrap implementation calls itself recursively.

**How to avoid:** In `HandleWrap`, do not recurse — just call `GetRankedCandidates` for the opposite direction once. If that is also empty, return exit code 1. No recursion.

### Pitfall 6: SupportedOSPlatform on MessageBeep

**What goes wrong:** CA1416 analyzer warning on `PInvoke.MessageBeep` call.

**Why it happens:** `MessageBeep` requires Windows XP or later (`windows5.1`). The `FocusActivator` class is already marked `[SupportedOSPlatform("windows5.0")]`. `windows5.0` covers `windows5.1`, so the annotation is sufficient.

**How to avoid:** Verify that `FocusActivator`'s `[SupportedOSPlatform]` attribute version covers `MessageBeep`'s minimum. `MessageBeep` requires Windows XP (5.1). `windows5.0` is Windows 2000 — technically earlier. The OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000) guard in Program.cs ensures Vista+ at runtime, which is higher than XP. The analyzer should be satisfied by the `windows5.0` class attribute. If a CA1416 warning appears, bump to `windows5.1` or add a local `[SupportedOSPlatform]`.

### Pitfall 7: Config File Not Created on First Run

**What goes wrong:** `FocusConfig.Load()` tries to read a non-existent file — this is the happy path (return defaults), but if the directory `%APPDATA%\focus\` does not exist and the user runs `--init-config`, `File.WriteAllText` throws `DirectoryNotFoundException`.

**Why it happens:** `%APPDATA%\focus\` does not exist until explicitly created.

**How to avoid:** In `--init-config` handler, call `Directory.CreateDirectory(Path.GetDirectoryName(configPath)!)` before `File.WriteAllText`. `Directory.CreateDirectory` is idempotent (no-op if the directory already exists).

---

## Code Examples

Verified patterns from official sources:

### Config Path Resolution

```csharp
// Source: Environment.SpecialFolder docs, GetFolderPath docs — stable API since .NET 1.0
// Returns C:\Users\USERNAME\AppData\Roaming\focus\config.json on Windows
var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
var configPath = Path.Combine(appData, "focus", "config.json");
```

### System.Text.Json Deserialize with Enum Converter

```csharp
// Source: System.Text.Json official docs — JsonStringEnumConverter
// Source: PropertyNameCaseInsensitive docs (learn.microsoft.com/en-us/dotnet/api/...)
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
var config = JsonSerializer.Deserialize<FocusConfig>(jsonText, options) ?? new FocusConfig();
```

### In-Memory Glob Matching with FileSystemGlobbing

```csharp
// Source: Microsoft.Extensions.FileSystemGlobbing docs (updated 2025-12-23)
// MatcherExtensions.Match(Matcher, string) — no filesystem I/O

using Microsoft.Extensions.FileSystemGlobbing;

var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
matcher.AddInclude("Teams*");
matcher.AddInclude("*Chrome*");

bool teamsExcluded = matcher.Match("Teams.exe").HasMatches;      // true
bool chromeExcluded = matcher.Match("chrome.exe").HasMatches;    // true
bool codeExcluded = matcher.Match("Code.exe").HasMatches;        // false
```

### MessageBeep (CsWin32)

```csharp
// Source: MessageBeep docs (updated 2025-07-01)
// Add MessageBeep to NativeMethods.txt, then:
// 0xFFFFFFFF = simple beep using speaker or default sound device

[SupportedOSPlatform("windows5.1")]
private static void PlayBeep()
{
    PInvoke.MessageBeep(0xFFFFFFFF); // async — returns immediately
}
```

### System.CommandLine 2.0 - Array Option with Multiple Values

```csharp
// Source: System.CommandLine 2.0 syntax docs
var excludeOption = new Option<string[]>("--exclude")
{
    Description = "Process name patterns to exclude",
    AllowMultipleArgumentsPerToken = true
};
excludeOption.Arity = ArgumentArity.ZeroOrMore;
rootCommand.Options.Add(excludeOption);

// In SetAction:
var excluded = parseResult.GetValue(excludeOption); // string[]?
// focus --exclude "Teams*" "*Chrome*" → excluded = ["Teams*", "*Chrome*"]
// focus --exclude "Teams*" --exclude "*Chrome*" → also works
```

### JsonSerializer.Serialize for --init-config

```csharp
// Source: System.Text.Json serialization docs
// WriteIndented = true for human-readable output
var defaults = new FocusConfig();
var serOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
var json = JsonSerializer.Serialize(defaults, serOptions);
Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
File.WriteAllText(configPath, json, Encoding.UTF8);
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Regex for wildcard matching | `Microsoft.Extensions.FileSystemGlobbing.Matcher` | Available since .NET Core 1.0 | Correct glob semantics; no regex string escaping needed; `*` and `?` work as users expect |
| `keybd_event` for beep | `MessageBeep` for system-sound beep | Win32 SDK classic | `MessageBeep` uses the system sound scheme; asynchronous; preferred for non-console tools |
| Hand-rolled config reading | `System.Text.Json` with default POCO properties | .NET 5+ | No Newtonsoft.Json dependency; BCL-included; sufficient for a 3-key config |

**Deprecated/outdated:**
- `Newtonsoft.Json (Json.NET)`: Project uses System.Text.Json (BCL). Do not add Newtonsoft.Json as a dependency — it is unnecessary for a 3-key config file.
- `IConfiguration / Microsoft.Extensions.Configuration`: The full IConfiguration stack (appsettings.json, environment variables, command-line providers) is overkill for this tool. The custom 3-layer merge (defaults → file → CLI) is cleaner to implement directly.

---

## Open Questions

1. **Glob pattern handling for process names with dots (e.g., `Microsoft.Teams.exe`)**
   - What we know: `Matcher` uses `.` as a literal character (not a regex metachar); `*Microsoft.Teams*` will match `Microsoft.Teams.exe` correctly
   - What's unclear: Whether users need to escape the dot in patterns — they should NOT need to (glob `.` is literal)
   - Recommendation: Document in `--debug config` output and `--init-config` template that patterns use glob wildcards (`*` and `?` only); `.` is literal; no regex

2. **`--debug score` output format: show all 3 strategies simultaneously or one selected strategy**
   - What we know: Success criterion says "lists all candidate windows with their computed scores for each of the three strategies"
   - What's unclear: Whether this means a multi-column table (all 3 strategies side-by-side) or 3 separate sections
   - Recommendation: Multi-column table — more efficient to read, clearly shows relative strategy differences

3. **Wrap behavior "wrap" mode edge case: should the wrapped focus target be the first or last window in opposite direction?**
   - What we know: The locked description says "wrap" is configurable, but not which candidate to select when wrapping
   - What's unclear: "Wrap" intuitively means "go to the farthest window in the opposite direction" (like a cylinder), but `GetRankedCandidates` returns best-first (closest) by default
   - Recommendation (Claude's discretion): On wrap, use the LAST (farthest/worst-scored) candidate from the opposite direction — this gives "jump to the far edge" semantics. Sort the opposite direction candidates and take `.Last()`.

4. **Whether `CFG-02` ("user invokes tool with direction argument") is truly complete from Phase 2**
   - What we know: `focus left/right/up/down` is fully wired in current `Program.cs` via `directionArgument`
   - What's unclear: Why CFG-02 appears in this phase's requirements list
   - Recommendation: Mark CFG-02 as already satisfied by Phase 2 in the plan — no new work needed. The plan should not add a task for it.

---

## Validation Architecture

> `workflow.nyquist_validation` is not set in `.planning/config.json` — this section is omitted.

---

## Sources

### Primary (HIGH confidence)

- https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/populate-properties — JsonObjectCreationHandling; default (Replace) behavior with C# initializers; .NET 8 specific features (updated 2025-12-04)
- https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.propertynamecaseinsensitive — PropertyNameCaseInsensitive property; default false (updated current)
- https://learn.microsoft.com/en-us/dotnet/core/extensions/file-globbing — Matcher patterns, Match() overloads, in-memory matching docs; OrdinalIgnoreCase constructor; `*` semantics (updated 2025-12-23)
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.filesystemglobbing.matcherextensions.match — Match(Matcher, String) overload; confirmed no filesystem I/O (updated 2026-02-11)
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-messagebeep — MessageBeep uType values; 0xFFFFFFFF = simple beep; async behavior; Windows XP minimum (updated 2025-07-01)
- https://learn.microsoft.com/en-us/dotnet/api/system.environment.specialfolder — ApplicationData returns APPDATA/Roaming on Windows (stable)
- https://learn.microsoft.com/en-us/dotnet/api/system.commandline.option.allowmultipleargumentspertoken — AllowMultipleArgumentsPerToken property for array options

### Secondary (MEDIUM confidence)

- https://github.com/dotnet/command-line-api/issues/1537 — System.CommandLine 2.0 Beta 2 release notes; confirmed Option<string[]> + AllowMultipleArgumentsPerToken pattern
- https://github.com/dotnet/runtime/issues/35251 — System.Text.Json does not write comments natively; confirmed Utf8JsonWriter as the only path; corroborates recommendation for valid JSON without comments for --init-config

### Tertiary (LOW confidence)

- Scoring weight recommendations (secondaryWeight = 5.0 for StrongAxisBias, half-plane cone for ClosestInDirection) — Claude's discretion based on geometric reasoning; needs empirical tuning during phase verification

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — System.Text.Json (BCL), FileSystemGlobbing (official MS), System.CommandLine 2.0 (already in project), MessageBeep (Win32 official docs) all verified
- Architecture: HIGH — Config POCO + deserialize pattern is standard .NET; Matcher.Match() API verified; CLI option extensions confirmed consistent with existing System.CommandLine 2.0 usage in project
- Pitfalls: HIGH for known .NET and Win32 issues; MEDIUM for scoring weight values (Claude's discretion, needs empirical tuning)
- Scoring strategies: MEDIUM (algorithm design is geometrically sound but specific constant values need empirical validation during verification)

**Research date:** 2026-02-27
**Valid until:** 2026-05-27 (System.Text.Json BCL stable; FileSystemGlobbing API stable; Win32 MessageBeep stable; System.CommandLine 2.0.3 pinned in project)
