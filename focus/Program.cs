using System.CommandLine;
using Focus.Windows;

var debugOption = new Option<string?>("--debug")
{
    Description = "Debug mode: enumerate | score | config"
};

var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Show navigation details (origin, candidates, scores)"
};

var directionArgument = new Argument<string?>("direction")
{
    Description = "Direction to navigate: left | right | up | down",
    Arity = ArgumentArity.ZeroOrOne  // optional so --debug still works without direction
};

var strategyOption = new Option<string?>("--strategy")
{
    Description = "Scoring strategy: balanced | strong-axis-bias | closest-in-direction | edge-matching | edge-proximity"
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
    Description = "Write a starter config file with defaults to %APPDATA%\\focus\\config.json"
};

var rootCommand = new RootCommand("focus — directional window focus navigator");
rootCommand.Options.Add(debugOption);
rootCommand.Options.Add(verboseOption);
rootCommand.Options.Add(strategyOption);
rootCommand.Options.Add(wrapOption);
rootCommand.Options.Add(excludeOption);
rootCommand.Options.Add(initConfigOption);
rootCommand.Arguments.Add(directionArgument);

rootCommand.SetAction(parseResult =>
{
    // --- Extract all CLI values ---
    var debugValue    = parseResult.GetValue(debugOption);
    var directionValue = parseResult.GetValue(directionArgument);
    var verbose        = parseResult.GetValue(verboseOption);
    var strategyValue  = parseResult.GetValue(strategyOption);
    var wrapValue      = parseResult.GetValue(wrapOption);
    var excludeValue   = parseResult.GetValue(excludeOption);
    var initConfig     = parseResult.GetValue(initConfigOption);

    // --- Handle --init-config (no platform dependency) ---
    if (initConfig)
    {
        var configPath = FocusConfig.GetConfigPath();
        if (File.Exists(configPath))
        {
            Console.Error.WriteLine($"Config already exists: {configPath}");
            Console.Error.WriteLine("Delete it first to regenerate.");
            return 1;
        }
        FocusConfig.WriteDefaults(configPath);
        Console.WriteLine($"Config written to: {configPath}");
        return 0;
    }

    // --- Load config + apply CLI overrides ---
    var config = FocusConfig.Load();

    // CLI strategy override (parse kebab-case string → enum)
    if (!string.IsNullOrEmpty(strategyValue))
    {
        var parsed = strategyValue.ToLowerInvariant() switch
        {
            "balanced"              => (Strategy?)Strategy.Balanced,
            "strong-axis-bias"      => Strategy.StrongAxisBias,
            "closest-in-direction"  => Strategy.ClosestInDirection,
            "edge-matching"         => Strategy.EdgeMatching,
            "edge-proximity"        => Strategy.EdgeProximity,
            _                       => null
        };
        if (parsed is null)
        {
            Console.Error.WriteLine($"Error: Unknown strategy '{strategyValue}'. Use: balanced, strong-axis-bias, closest-in-direction, edge-matching, edge-proximity");
            return 2;
        }
        config.Strategy = parsed.Value;
    }

    // CLI wrap override (parse kebab-case string → enum)
    if (!string.IsNullOrEmpty(wrapValue))
    {
        var parsed = wrapValue.ToLowerInvariant() switch
        {
            "no-op" or "noop"   => (WrapBehavior?)WrapBehavior.NoOp,
            "wrap"              => WrapBehavior.Wrap,
            "beep"              => WrapBehavior.Beep,
            _                   => null
        };
        if (parsed is null)
        {
            Console.Error.WriteLine($"Error: Unknown wrap behavior '{wrapValue}'. Use: no-op, wrap, beep");
            return 2;
        }
        config.Wrap = parsed.Value;
    }

    // CLI exclude override (replaces config list entirely per locked decision)
    if (excludeValue is { Length: > 0 })
        config.Exclude = excludeValue;

    // --- Handle --debug modes ---
    if (!string.IsNullOrEmpty(debugValue))
    {
        // --debug config (no platform dependency — just print resolved config)
        if (debugValue == "config")
        {
            var configPath = FocusConfig.GetConfigPath();
            Console.WriteLine($"Config file: {configPath}");
            Console.WriteLine($"  exists: {File.Exists(configPath)}");
            Console.WriteLine($"  strategy: {config.Strategy}");
            Console.WriteLine($"  wrap: {config.Wrap}");
            Console.WriteLine($"  exclude: [{string.Join(", ", config.Exclude.Select(p => $"\"{p}\""))}]");
            return 0;
        }

        // Platform-dependent debug modes
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
        {
            Console.Error.WriteLine("Error: This tool requires Windows Vista or later.");
            return 2;
        }

        if (debugValue == "enumerate")
        {
            try
            {
                var enumerator = new WindowEnumerator();
                var (windows, filteredUwpCount) = enumerator.GetNavigableWindows();
                var filtered = ExcludeFilter.Apply(windows, config.Exclude);

                if (filtered.Count == 0)
                {
                    Console.WriteLine("No navigable windows found.");
                    return 0;
                }

                PrintWindowTable(filtered, filteredUwpCount);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error enumerating windows: {ex.Message}");
                return 2;
            }
        }

        if (debugValue == "score")
        {
            var scoreDirection = DirectionParser.Parse(directionValue);
            if (scoreDirection is null)
            {
                Console.Error.WriteLine("Usage: focus --debug score <direction>");
                return 2;
            }

            try
            {
                var enumerator = new WindowEnumerator();
                var (windows, _) = enumerator.GetNavigableWindows();
                var filtered = ExcludeFilter.Apply(windows, config.Exclude);

                // Run all five strategies for comparison
                var balanced   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.Balanced);
                var strongBias = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.StrongAxisBias);
                var closestDir = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.ClosestInDirection);
                var edgeMatch  = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.EdgeMatching);
                var edgeProx   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.EdgeProximity);

                PrintScoreTable(balanced, strongBias, closestDir, edgeMatch, edgeProx, scoreDirection.Value, config.Strategy);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 2;
            }
        }

        Console.Error.WriteLine($"Unknown --debug value: {debugValue}. Use: enumerate, score, config");
        return 2;
    }

    // --- Navigation mode (direction argument required) ---
    if (!string.IsNullOrEmpty(directionValue))
    {
        var direction = DirectionParser.Parse(directionValue);
        if (direction is null)
        {
            Console.Error.WriteLine($"Error: Unknown direction '{directionValue}'. Use: left, right, up, down");
            return 2;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
        {
            Console.Error.WriteLine("Error: This tool requires Windows Vista or later.");
            return 2;
        }

        try
        {
            var enumerator = new WindowEnumerator();
            var (windows, _) = enumerator.GetNavigableWindows();

            // Apply exclude filter
            var filtered = ExcludeFilter.Apply(windows, config.Exclude);

            if (verbose)
                Console.Error.WriteLine($"[focus] enumerated: {filtered.Count} windows (strategy: {config.Strategy}, wrap: {config.Wrap})");

            // Score and rank with selected strategy
            var ranked = NavigationService.GetRankedCandidates(
                filtered, direction.Value, config.Strategy, out var fgHwnd, out var originX, out var originY);

            if (verbose)
            {
                Console.Error.WriteLine($"[focus] origin: 0x{fgHwnd:X8} center=({originX:F0}, {originY:F0})");
                Console.Error.WriteLine($"[focus] direction: {directionValue}");
                Console.Error.WriteLine($"[focus] candidates: {ranked.Count} in direction");
                for (int i = 0; i < ranked.Count; i++)
                {
                    var (w, score) = ranked[i];
                    Console.Error.WriteLine($"[focus]   #{i + 1}  score={score:F1}  \"{Truncate(w.Title, 40)}\"  ({w.Left},{w.Top},{w.Right},{w.Bottom})");
                }
            }

            // Activate with wrap-around behavior
            return FocusActivator.ActivateWithWrap(ranked, filtered, direction.Value, config.Strategy, config.Wrap, verbose);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    Console.WriteLine("No command specified. Use --help for usage.");
    return 1;
});

return rootCommand.Parse(args).Invoke();

// --- Local helper methods ---

static void PrintWindowTable(List<WindowInfo> windows, int filteredUwpCount)
{
    // Column widths
    const int hwndWidth    = 12;
    const int processWidth = 22;
    const int titleWidth   = 42;
    const int boundsWidth  = 24;
    const int monWidth     =  4;
    const int flagsWidth   =  6;

    // Header row
    Console.WriteLine(
        $"{"HWND",-hwndWidth} {"PROCESS",-processWidth} {"TITLE",-titleWidth} {"BOUNDS",-boundsWidth} {"MON",monWidth} {"FLAGS",-flagsWidth}");

    // Separator line
    Console.WriteLine(
        $"{new string('-', hwndWidth)} {new string('-', processWidth)} {new string('-', titleWidth)} {new string('-', boundsWidth)} {new string('-', monWidth)} {new string('-', flagsWidth)}");

    // Data rows
    foreach (var w in windows)
    {
        string hwndHex    = $"0x{w.Hwnd:X8}";
        string process    = Truncate(w.ProcessName, processWidth);
        string title      = WindowInfo.TruncateTitle(w.Title, titleWidth - 2); // leave 2 chars for alignment
        string bounds     = $"{w.Left},{w.Top},{w.Right},{w.Bottom}";
        string mon        = w.MonitorIndex.ToString();
        string flags      = w.FlagsString;

        Console.WriteLine(
            $"{hwndHex,-hwndWidth} {process,-processWidth} {title,-titleWidth} {bounds,-boundsWidth} {mon,monWidth} {flags,-flagsWidth}");
    }

    // Summary line
    Console.WriteLine();
    int monitorCount = windows.Count > 0 ? windows.Max(w => w.MonitorIndex) : 0;
    string summary = $"Found {windows.Count} windows on {monitorCount} monitors";
    if (filteredUwpCount > 0)
        summary += $" (filtered {filteredUwpCount} duplicate UWP HWNDs)";
    Console.WriteLine(summary);
}

static void PrintScoreTable(
    List<(WindowInfo Window, double Score)> balanced,
    List<(WindowInfo Window, double Score)> strongBias,
    List<(WindowInfo Window, double Score)> closestDir,
    List<(WindowInfo Window, double Score)> edgeMatch,
    List<(WindowInfo Window, double Score)> edgeProx,
    Direction direction,
    Strategy activeStrategy)
{
    // Build a union set of all window HWNDs across all five lists
    var allHwnds = new Dictionary<nint, WindowInfo>();
    foreach (var (w, _) in balanced)   allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in strongBias) allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in closestDir) allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in edgeMatch)  allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in edgeProx)   allHwnds.TryAdd(w.Hwnd, w);

    if (allHwnds.Count == 0)
    {
        Console.WriteLine($"No candidates in direction: {direction}");
        return;
    }

    // Build score lookups per strategy
    var balancedScores   = balanced.ToDictionary(x => x.Window.Hwnd, x => x.Score);
    var strongBiasScores = strongBias.ToDictionary(x => x.Window.Hwnd, x => x.Score);
    var closestDirScores = closestDir.ToDictionary(x => x.Window.Hwnd, x => x.Score);
    var edgeMatchScores  = edgeMatch.ToDictionary(x => x.Window.Hwnd, x => x.Score);
    var edgeProxScores   = edgeProx.ToDictionary(x => x.Window.Hwnd, x => x.Score);

    // Active strategy marker columns
    string balancedHeader   = "BALANCED"    + (activeStrategy == Strategy.Balanced           ? "*" : " ");
    string strongBiasHeader = "STRONG-AXIS" + (activeStrategy == Strategy.StrongAxisBias     ? "*" : " ");
    string closestHeader    = "CLOSEST"     + (activeStrategy == Strategy.ClosestInDirection  ? "*" : " ");
    string edgeMatchHeader  = "EDGE-MATCH"  + (activeStrategy == Strategy.EdgeMatching        ? "*" : " ");
    string edgeProxHeader   = "EDGE-PROX"   + (activeStrategy == Strategy.EdgeProximity       ? "*" : " ");

    const int titleWidth  = 34;
    const int scoreWidth  = 12;

    // Header
    Console.WriteLine(
        $"{"WINDOW",-titleWidth} {balancedHeader,scoreWidth} {strongBiasHeader,scoreWidth} {closestHeader,scoreWidth} {edgeMatchHeader,scoreWidth} {edgeProxHeader,scoreWidth}");
    Console.WriteLine(
        $"{new string('-', titleWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)}");

    // Sort rows: by balanced score ascending if available, else by hwnd
    var sortedHwnds = allHwnds.Keys
        .OrderBy(hwnd => balancedScores.TryGetValue(hwnd, out var s) ? s : double.MaxValue)
        .ThenBy(hwnd => hwnd)
        .ToList();

    foreach (var hwnd in sortedHwnds)
    {
        var w = allHwnds[hwnd];
        string label = $"{Truncate(w.Title, 22)} ({Truncate(w.ProcessName, 10)})";

        string balancedScore   = balancedScores.TryGetValue(hwnd, out var bs)    ? bs.ToString("F1")  : "-";
        string strongBiasScore = strongBiasScores.TryGetValue(hwnd, out var sbs) ? sbs.ToString("F1") : "-";
        string closestDirScore = closestDirScores.TryGetValue(hwnd, out var cs)  ? cs.ToString("F1")  : "-";
        string edgeMatchScore  = edgeMatchScores.TryGetValue(hwnd, out var ems)  ? ems.ToString("F1") : "-";
        string edgeProxScore   = edgeProxScores.TryGetValue(hwnd, out var eps)   ? eps.ToString("F1") : "-";

        Console.WriteLine(
            $"{label,-titleWidth} {balancedScore,scoreWidth} {strongBiasScore,scoreWidth} {closestDirScore,scoreWidth} {edgeMatchScore,scoreWidth} {edgeProxScore,scoreWidth}");
    }

    Console.WriteLine();
    Console.WriteLine($"Direction: {direction} | Active strategy: {activeStrategy} | {allHwnds.Count} candidates total");
}

static string Truncate(string value, int maxLen)
{
    if (value.Length <= maxLen)
        return value;
    if (maxLen <= 3)
        return value[..maxLen];
    return value[..(maxLen - 3)] + "...";
}
