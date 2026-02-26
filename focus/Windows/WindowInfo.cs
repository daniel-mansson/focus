namespace Focus.Windows;

/// <summary>
/// Immutable record capturing all information about an enumerated window.
/// Bounds are in physical pixels from DWMWA_EXTENDED_FRAME_BOUNDS (L,T,R,B).
/// </summary>
internal record WindowInfo(
    nint Hwnd,
    string ProcessName,
    string Title,
    int Left,
    int Top,
    int Right,
    int Bottom,
    int MonitorIndex,
    bool IsTopmost,
    bool IsUwpFrame)
{
    /// <summary>
    /// Single-character flag markers. "T" for topmost. Extensible for future flags.
    /// </summary>
    public string FlagsString => IsTopmost ? "T" : string.Empty;

    /// <summary>
    /// Truncates a window title to maxLen characters, appending "..." if truncated.
    /// Edge case: if title length is 3 or fewer, returns title unchanged.
    /// </summary>
    public static string TruncateTitle(string title, int maxLen = 40)
    {
        if (title.Length <= maxLen)
            return title;

        // Handle edge case where maxLen - 3 would be negative or zero
        if (title.Length <= 3)
            return title;

        return title[..(maxLen - 3)] + "...";
    }
}
