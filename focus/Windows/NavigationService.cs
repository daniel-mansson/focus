using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Focus.Windows;

/// <summary>
/// Directional scoring engine that scores and ranks candidate windows for navigation.
/// Given a set of visible windows and a direction, returns candidates sorted by score (lowest = best).
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal static class NavigationService
{
    /// <summary>
    /// Returns windows sorted by directional score (ascending — lowest = best candidate).
    /// Windows behind the origin (wrong direction) are eliminated and never appear in results.
    /// The currently focused window is always excluded from candidates.
    /// </summary>
    public static unsafe List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows,
        Direction direction) =>
        GetRankedCandidates(allWindows, direction, out _, out _, out _);

    public static unsafe List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows,
        Direction direction,
        out nint foregroundHwnd,
        out double originX,
        out double originY)
    {
        // Get current foreground HWND as nint (0 = no foreground window / desktop focused)
        var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();
        foregroundHwnd = fgHwnd;

        // Determine scoring origin from foreground window bounds or primary monitor center
        (originX, originY) = GetOriginPoint(fgHwnd);

        // Local copies for lambda capture (out params can't be used in lambdas)
        double ox = originX, oy = originY;

        var result = new List<(WindowInfo Window, double Score)>();

        foreach (var window in allWindows)
        {
            // Always exclude the currently focused window from candidates
            if (window.Hwnd == fgHwnd)
                continue;

            double score = ScoreCandidate(ox, oy, window, direction);
            if (score < double.MaxValue)
                result.Add((window, score));
        }

        // Sort ascending: lowest score = best candidate
        result.Sort((a, b) =>
        {
            // Primary: compare scores
            int cmp = a.Score.CompareTo(b.Score);
            if (Math.Abs(a.Score - b.Score) < 1e-6)
            {
                // Tie-breaking: prefer smaller secondary distance (more aligned with movement axis)
                double secA = GetSecondaryDistance(ox, oy, a.Window, direction);
                double secB = GetSecondaryDistance(ox, oy, b.Window, direction);
                int secCmp = secA.CompareTo(secB);
                if (secCmp != 0)
                    return secCmp;

                // Still tied: prefer smaller primary distance (closer)
                double priA = GetPrimaryDistance(ox, oy, a.Window, direction);
                double priB = GetPrimaryDistance(ox, oy, b.Window, direction);
                return priA.CompareTo(priB);
            }
            return cmp;
        });

        return result;
    }

    /// <summary>
    /// Determines the scoring origin point.
    /// If foregroundHwnd is valid, uses the geometric center of its DWM bounds.
    /// Falls back to the primary monitor center if foreground window is unavailable or bounds fail.
    /// </summary>
    public static unsafe (double X, double Y) GetOriginPoint(nint foregroundHwnd)
    {
        if (foregroundHwnd != 0)
        {
            var hwnd = new HWND((void*)(IntPtr)foregroundHwnd);
            RECT boundsRect = default;
            var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref boundsRect, 1));
            var hr = PInvoke.DwmGetWindowAttribute(
                hwnd,
                DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                boundsBytes);

            if (hr.Succeeded && (boundsRect.right - boundsRect.left) > 0)
            {
                return (
                    (boundsRect.left + boundsRect.right) / 2.0,
                    (boundsRect.top + boundsRect.bottom) / 2.0
                );
            }
        }

        // Fall back to center of primary monitor when no foreground window or bounds retrieval fails
        return MonitorHelper.GetPrimaryMonitorCenter();
    }

    /// <summary>
    /// Returns the nearest point on the candidate rectangle to the given origin point.
    /// Uses Math.Clamp on both axes — correct nearest-point-on-AABB formula.
    /// </summary>
    internal static (double NearX, double NearY) NearestPoint(
        double px, double py,
        int left, int top, int right, int bottom)
    {
        double nearX = Math.Clamp(px, left, right);
        double nearY = Math.Clamp(py, top, bottom);
        return (nearX, nearY);
    }

    /// <summary>
    /// Scores a candidate window against the origin point for the given direction.
    /// Returns double.MaxValue if the candidate is not in the requested direction (eliminated).
    /// Lower score = better candidate.
    ///
    /// Formula: primaryWeight * primaryDist + secondaryWeight * secondaryDist
    /// Weights: primaryWeight = 1.0, secondaryWeight = 2.0
    /// The 2.0 secondary weight makes alignment matter — a window directly ahead beats
    /// a diagonally offset one. These are tunable constants (Claude's discretion, NAV-07).
    /// </summary>
    internal static double ScoreCandidate(
        double originX, double originY,
        WindowInfo candidate,
        Direction direction)
    {
        var (nearX, nearY) = NearestPoint(
            originX, originY,
            candidate.Left, candidate.Top, candidate.Right, candidate.Bottom);

        // Directional filter: nearest point must be strictly in the requested direction.
        // Strict inequality (</>): a window whose nearest edge is exactly at the origin line
        // is ambiguous and should not match.
        bool inDirection = direction switch
        {
            Direction.Left  => nearX < originX,
            Direction.Right => nearX > originX,
            Direction.Up    => nearY < originY,
            Direction.Down  => nearY > originY,
            _               => false
        };

        if (!inDirection)
            return double.MaxValue; // eliminated — wrong direction

        // Primary axis distance: how far in the requested direction (always positive)
        double primaryDist = direction switch
        {
            Direction.Left  => originX - nearX,
            Direction.Right => nearX - originX,
            Direction.Up    => originY - nearY,
            Direction.Down  => nearY - originY,
            _               => double.MaxValue
        };

        // Secondary axis distance: perpendicular deviation (absolute value)
        double secondaryDist = direction switch
        {
            Direction.Left or Direction.Right => Math.Abs(nearY - originY),
            Direction.Up   or Direction.Down  => Math.Abs(nearX - originX),
            _                                 => double.MaxValue
        };

        const double primaryWeight   = 1.0;
        const double secondaryWeight = 2.0;

        return primaryWeight * primaryDist + secondaryWeight * secondaryDist;
    }

    // Helper for tie-breaking: returns secondary axis distance for a candidate
    private static double GetSecondaryDistance(double originX, double originY, WindowInfo w, Direction direction)
    {
        var (nearX, nearY) = NearestPoint(originX, originY, w.Left, w.Top, w.Right, w.Bottom);
        return direction switch
        {
            Direction.Left or Direction.Right => Math.Abs(nearY - originY),
            Direction.Up   or Direction.Down  => Math.Abs(nearX - originX),
            _                                 => double.MaxValue
        };
    }

    // Helper for tie-breaking: returns primary axis distance for a candidate
    private static double GetPrimaryDistance(double originX, double originY, WindowInfo w, Direction direction)
    {
        var (nearX, nearY) = NearestPoint(originX, originY, w.Left, w.Top, w.Right, w.Bottom);
        return direction switch
        {
            Direction.Left  => originX - nearX,
            Direction.Right => nearX - originX,
            Direction.Up    => originY - nearY,
            Direction.Down  => nearY - originY,
            _               => double.MaxValue
        };
    }
}
