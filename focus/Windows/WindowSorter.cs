namespace Focus.Windows;

/// <summary>
/// Provides position-based sorting of windows for CAPS+number window selection.
/// </summary>
internal static class WindowSorter
{
    /// <summary>
    /// Sorts windows by horizontal position using the specified strategy.
    /// LeftEdge: sort by window's Left coordinate ascending (leftmost first).
    /// Center: sort by window's horizontal center ((Left+Right)/2) ascending (leftmost center first).
    /// Ties are broken by vertical position (Top ascending) for deterministic ordering.
    /// </summary>
    public static List<WindowInfo> SortByPosition(List<WindowInfo> windows, NumberSortStrategy strategy)
    {
        var sorted = new List<WindowInfo>(windows);
        sorted.Sort((a, b) =>
        {
            int primaryA, primaryB;
            if (strategy == NumberSortStrategy.Center)
            {
                primaryA = (a.Left + a.Right) / 2;
                primaryB = (b.Left + b.Right) / 2;
            }
            else // LeftEdge
            {
                primaryA = a.Left;
                primaryB = b.Left;
            }

            int cmp = primaryA.CompareTo(primaryB);
            if (cmp != 0) return cmp;

            // Tie-break by top edge
            return a.Top.CompareTo(b.Top);
        });
        return sorted;
    }
}
