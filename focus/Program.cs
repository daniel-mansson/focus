using System.CommandLine;
using Focus.Windows;

var debugOption = new Option<string?>("--debug")
{
    Description = "Debug mode: enumerate | score | config"
};

var directionArgument = new Argument<string?>("direction")
{
    Description = "Direction to navigate: left | right | up | down",
    Arity = ArgumentArity.ZeroOrOne  // optional so --debug still works without direction
};

var rootCommand = new RootCommand("focus — directional window focus navigator");
rootCommand.Options.Add(debugOption);
rootCommand.Arguments.Add(directionArgument);

rootCommand.SetAction(parseResult =>
{
    var debugValue = parseResult.GetValue(debugOption);
    var directionValue = parseResult.GetValue(directionArgument);

    if (!string.IsNullOrEmpty(debugValue))
    {
        if (debugValue == "enumerate")
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000))
            {
                Console.Error.WriteLine("Error: This tool requires Windows Vista or later.");
                return 2;
            }
            try
            {
                var enumerator = new WindowEnumerator();
                var (windows, filteredUwpCount) = enumerator.GetNavigableWindows();

                if (windows.Count == 0)
                {
                    Console.WriteLine("No navigable windows found.");
                    return 0;
                }

                PrintWindowTable(windows, filteredUwpCount);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error enumerating windows: {ex.Message}");
                return 2;
            }
        }
        Console.Error.WriteLine($"Unknown --debug value: {debugValue}");
        return 2;
    }

    if (!string.IsNullOrEmpty(directionValue))
    {
        // Parse direction first (no platform dependency)
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
            // Enumerate windows (Phase 1 pipeline)
            var enumerator = new WindowEnumerator();
            var (windows, _) = enumerator.GetNavigableWindows();

            // Score and rank candidates (Plan 02-01)
            var ranked = NavigationService.GetRankedCandidates(windows, direction.Value);

            // Activate best candidate (Plan 02-02) — silent on success (exit code only)
            return FocusActivator.ActivateBestCandidate(ranked);
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

static string Truncate(string value, int maxLen)
{
    if (value.Length <= maxLen)
        return value;
    if (maxLen <= 3)
        return value[..maxLen];
    return value[..(maxLen - 3)] + "...";
}
