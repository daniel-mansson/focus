using System.CommandLine;
using Focus.Windows;
using Focus.Windows.Daemon;
using Focus.Windows.Daemon.Overlay;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;

var debugOption = new Option<string?>("--debug")
{
    Description = "Debug mode: enumerate | score | config | overlay | all"
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
    Description = "Scoring strategy: balanced | strong-axis-bias | closest-in-direction | edge-matching | edge-proximity | axis-only"
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

// --- Daemon subcommand ---
var daemonCommand = new Command("daemon", "Run as a persistent background daemon with CAPSLOCK overlay hook");
var backgroundOption = new Option<bool>("--background")
{
    Description = "Detach from console and run in background with tray icon only"
};
var daemonVerboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Log CAPSLOCK hold/release events to stderr"
};
daemonCommand.Options.Add(backgroundOption);
daemonCommand.Options.Add(daemonVerboseOption);

daemonCommand.SetAction(parseResult =>
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
    {
        Console.Error.WriteLine("Error: Daemon requires Windows Vista or later.");
        return 2;
    }

    bool background = parseResult.GetValue(backgroundOption);
    bool verbose = parseResult.GetValue(daemonVerboseOption);
    return DaemonCommand.Run(background, verbose);
});

rootCommand.Subcommands.Add(daemonCommand);

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
            "axis-only"             => Strategy.AxisOnly,
            _                       => null
        };
        if (parsed is null)
        {
            Console.Error.WriteLine($"Error: Unknown strategy '{strategyValue}'. Use: balanced, strong-axis-bias, closest-in-direction, edge-matching, edge-proximity, axis-only");
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

                // Run all six strategies for comparison
                var balanced   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.Balanced);
                var strongBias = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.StrongAxisBias);
                var closestDir = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.ClosestInDirection);
                var edgeMatch  = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.EdgeMatching);
                var edgeProx   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.EdgeProximity);
                var axisOnly   = NavigationService.GetRankedCandidates(filtered, scoreDirection.Value, Strategy.AxisOnly);

                PrintScoreTable(balanced, strongBias, closestDir, edgeMatch, edgeProx, axisOnly, scoreDirection.Value, config.Strategy);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 2;
            }
        }

        if (debugValue == "overlay")
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            {
                Console.Error.WriteLine("Error: This tool requires Windows Vista or later.");
                return 2;
            }

            var overlayDirection = DirectionParser.Parse(directionValue);
            if (overlayDirection is null)
            {
                Console.Error.WriteLine("Usage: focus --debug overlay <direction>");
                return 2;
            }

            // Load config for overlay colors and renderer selection
            var overlayConfig = FocusConfig.Load();
            var renderer = OverlayManager.CreateRenderer(overlayConfig.OverlayRenderer);

            // Get foreground window bounds using DWMWA_EXTENDED_FRAME_BOUNDS
            var fgHwnd = PInvoke.GetForegroundWindow();
            RECT fgBounds = default;
            var hr = PInvoke.DwmGetWindowAttribute(fgHwnd,
                DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                    System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref fgBounds, 1)));

            if (hr.Failed)
            {
                Console.Error.WriteLine($"Error: Could not get foreground window bounds (HRESULT: 0x{hr.Value:X8})");
                return 2;
            }

            Console.WriteLine($"Overlay: {overlayDirection.Value} on foreground window");
            Console.WriteLine($"  Bounds: {fgBounds.left},{fgBounds.top},{fgBounds.right},{fgBounds.bottom}");
            Console.WriteLine($"  Color: {overlayConfig.OverlayColors.GetArgb(overlayDirection.Value):X8}");
            Console.WriteLine("Press any key to dismiss...");

            // Create overlay on this thread (STA thread via UseWindowsForms=true in csproj)
            // Use Application.DoEvents() as a simple message pump for WM_PAINT handling
            using var overlayManager = new OverlayManager(renderer);
            overlayManager.ShowOverlay(overlayDirection.Value, fgBounds, overlayConfig.OverlayColors.GetArgb(overlayDirection.Value));

            // Wait for keypress on a background thread, then signal exit
            var exitEvent = new ManualResetEventSlim(false);
            var keyThread = new Thread(() =>
            {
                Console.ReadKey(true);
                exitEvent.Set();
            });
            keyThread.IsBackground = true;
            keyThread.Start();

            // Pump messages until keypress — simple DoEvents loop (~60fps)
            while (!exitEvent.IsSet)
            {
                Application.DoEvents();
                Thread.Sleep(16);
            }

            overlayManager.HideAll();
            Console.WriteLine("Overlay dismissed.");
            return 0;
        }

        if (debugValue == "all")
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            {
                Console.Error.WriteLine("Error: This tool requires Windows Vista or later.");
                return 2;
            }

            var allConfig = FocusConfig.Load();
            var allRenderer = OverlayManager.CreateRenderer(allConfig.OverlayRenderer);

            var enumerator = new WindowEnumerator();
            var (windows, _) = enumerator.GetNavigableWindows();
            var filtered = ExcludeFilter.Apply(windows, allConfig.Exclude);

            var fgHwnd = PInvoke.GetForegroundWindow();
            RECT fgBounds = default;
            PInvoke.DwmGetWindowAttribute(fgHwnd,
                DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                    System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref fgBounds, 1)));

            Console.WriteLine($"Debug ALL directions from foreground window");
            Console.WriteLine($"  Bounds: {fgBounds.left},{fgBounds.top},{fgBounds.right},{fgBounds.bottom}");
            Console.WriteLine($"  Strategy: {allConfig.Strategy}");
            Console.WriteLine();

            using var overlayManager = new OverlayManager(allRenderer);

            foreach (var dir in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
            {
                var ranked = NavigationService.GetRankedCandidates(filtered, dir, allConfig.Strategy);

                Console.WriteLine($"--- {dir} ---");

                if (ranked.Count == 0)
                {
                    Console.WriteLine($"  (no candidates)");
                    Console.WriteLine();
                    continue;
                }

                int displayCount = Math.Min(5, ranked.Count);
                for (int i = 0; i < displayCount; i++)
                {
                    var (w, score) = ranked[i];
                    bool isTop = i == 0;
                    string rank = isTop ? $"  #{i + 1}*" : $"  #{i + 1} ";
                    Console.WriteLine($"{rank} score={score:F1}  \"{Truncate(w.Title, 30)}\"  ({Truncate(w.ProcessName, 12)})  [{w.Left},{w.Top},{w.Right},{w.Bottom}]");
                }

                var topWindow = ranked[0].Window;
                var targetBounds = new RECT
                {
                    left   = topWindow.Left,
                    top    = topWindow.Top,
                    right  = topWindow.Right,
                    bottom = topWindow.Bottom
                };
                overlayManager.ShowOverlay(dir, targetBounds, allConfig.OverlayColors.GetArgb(dir));

                Console.WriteLine();
            }

            Console.WriteLine("Overlays shown on top candidates. Press any key to dismiss...");

            var exitEvent = new ManualResetEventSlim(false);
            var keyThread = new Thread(() => { Console.ReadKey(true); exitEvent.Set(); });
            keyThread.IsBackground = true;
            keyThread.Start();

            while (!exitEvent.IsSet)
            {
                Application.DoEvents();
                Thread.Sleep(16);
            }

            overlayManager.HideAll();
            Console.WriteLine("Overlays dismissed.");
            return 0;
        }

        Console.Error.WriteLine($"Unknown --debug value: {debugValue}. Use: enumerate, score, config, overlay, all");
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
    List<(WindowInfo Window, double Score)> axisOnly,
    Direction direction,
    Strategy activeStrategy)
{
    // Build a union set of all window HWNDs across all six lists
    var allHwnds = new Dictionary<nint, WindowInfo>();
    foreach (var (w, _) in balanced)   allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in strongBias) allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in closestDir) allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in edgeMatch)  allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in edgeProx)   allHwnds.TryAdd(w.Hwnd, w);
    foreach (var (w, _) in axisOnly)   allHwnds.TryAdd(w.Hwnd, w);

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
    var axisOnlyScores   = axisOnly.ToDictionary(x => x.Window.Hwnd, x => x.Score);

    // Active strategy marker columns
    string balancedHeader   = "BALANCED"    + (activeStrategy == Strategy.Balanced           ? "*" : " ");
    string strongBiasHeader = "STRONG-AXIS" + (activeStrategy == Strategy.StrongAxisBias     ? "*" : " ");
    string closestHeader    = "CLOSEST"     + (activeStrategy == Strategy.ClosestInDirection  ? "*" : " ");
    string edgeMatchHeader  = "EDGE-MATCH"  + (activeStrategy == Strategy.EdgeMatching        ? "*" : " ");
    string edgeProxHeader   = "EDGE-PROX"   + (activeStrategy == Strategy.EdgeProximity       ? "*" : " ");
    string axisOnlyHeader   = "AXIS-ONLY"   + (activeStrategy == Strategy.AxisOnly            ? "*" : " ");

    const int titleWidth  = 34;
    const int scoreWidth  = 12;

    // Header
    Console.WriteLine(
        $"{"WINDOW",-titleWidth} {balancedHeader,scoreWidth} {strongBiasHeader,scoreWidth} {closestHeader,scoreWidth} {edgeMatchHeader,scoreWidth} {edgeProxHeader,scoreWidth} {axisOnlyHeader,scoreWidth}");
    Console.WriteLine(
        $"{new string('-', titleWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)} {new string('-', scoreWidth)}");

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
        string axisOnlyScore   = axisOnlyScores.TryGetValue(hwnd, out var aos)   ? aos.ToString("F1") : "-";

        Console.WriteLine(
            $"{label,-titleWidth} {balancedScore,scoreWidth} {strongBiasScore,scoreWidth} {closestDirScore,scoreWidth} {edgeMatchScore,scoreWidth} {edgeProxScore,scoreWidth} {axisOnlyScore,scoreWidth}");
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
