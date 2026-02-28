using Microsoft.Extensions.FileSystemGlobbing;

namespace Focus.Windows;

/// <summary>
/// Filters windows by process name using glob patterns (case-insensitive).
/// WindowInfo.ProcessName is already the bare filename (e.g. "notepad.exe") —
/// no path stripping is needed here.
/// </summary>
internal static class ExcludeFilter
{
    /// <summary>
    /// Removes windows whose process name matches any of the given glob patterns.
    /// Returns the original list unchanged if no patterns are provided.
    /// </summary>
    public static List<WindowInfo> Apply(List<WindowInfo> windows, string[] excludePatterns)
    {
        if (excludePatterns.Length == 0)
            return windows;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(excludePatterns);

        return windows.Where(w => !matcher.Match(w.ProcessName).HasMatches).ToList();
    }
}
