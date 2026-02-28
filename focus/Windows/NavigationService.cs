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
    // Cache for foreground window bounds used by ScoreEdgeMatching.
    // Populated once per GetRankedCandidates call and reset at the start of each call.
    private static (nint Hwnd, int Left, int Top, int Right, int Bottom) _fgBoundsCache;

    /// <summary>
    /// Returns windows sorted by directional score (ascending — lowest = best candidate).
    /// Windows behind the origin (wrong direction) are eliminated and never appear in results.
    /// The currently focused window is always excluded from candidates.
    /// Defaults to Strategy.Balanced for backward compatibility.
    /// </summary>
    public static List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows,
        Direction direction) =>
        GetRankedCandidates(allWindows, direction, Strategy.Balanced, out _, out _, out _);

    public static unsafe List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows,
        Direction direction,
        out nint foregroundHwnd,
        out double originX,
        out double originY) =>
        GetRankedCandidates(allWindows, direction, Strategy.Balanced,
            out foregroundHwnd, out originX, out originY);

    /// <summary>
    /// Returns windows sorted by directional score using the specified strategy.
    /// </summary>
    public static List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows,
        Direction direction,
        Strategy strategy) =>
        GetRankedCandidates(allWindows, direction, strategy, out _, out _, out _);

    public static unsafe List<(WindowInfo Window, double Score)> GetRankedCandidates(
        List<WindowInfo> allWindows,
        Direction direction,
        Strategy strategy,
        out nint foregroundHwnd,
        out double originX,
        out double originY)
    {
        // Reset foreground bounds cache so ScoreEdgeMatching re-queries on each navigation cycle
        _fgBoundsCache = default;

        // Get current foreground HWND as nint (0 = no foreground window / desktop focused)
        var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();
        foregroundHwnd = fgHwnd;

        // Determine scoring origin from foreground window bounds or primary monitor center
        (originX, originY) = GetOriginPoint(fgHwnd);

        // Local copies for lambda capture (out params can't be used in lambdas)
        double ox = originX, oy = originY;

        // Select scoring function based on strategy
        Func<double, double, WindowInfo, Direction, double> scoreFn = strategy switch
        {
            Strategy.Balanced           => ScoreCandidate,
            Strategy.StrongAxisBias     => ScoreStrongAxisBias,
            Strategy.ClosestInDirection => ScoreClosestInDirection,
            Strategy.EdgeMatching       => ScoreEdgeMatching,
            Strategy.EdgeProximity      => ScoreEdgeProximity,
            _                           => ScoreCandidate
        };

        var result = new List<(WindowInfo Window, double Score)>();

        foreach (var window in allWindows)
        {
            // Always exclude the currently focused window from candidates
            if (window.Hwnd == fgHwnd)
                continue;

            double score = scoreFn(ox, oy, window, direction);
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

    /// <summary>
    /// Scores a candidate window using strong axis bias.
    /// Same structure as ScoreCandidate but with a higher secondary weight (5.0 vs 2.0).
    /// Higher secondary weight = more aggressive lane preference vs balanced's 2.0.
    /// </summary>
    internal static double ScoreStrongAxisBias(
        double originX, double originY,
        WindowInfo candidate,
        Direction direction)
    {
        var (nearX, nearY) = NearestPoint(
            originX, originY,
            candidate.Left, candidate.Top, candidate.Right, candidate.Bottom);

        // Directional filter: same strict inequality as ScoreCandidate
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

        // Higher secondary weight = more aggressive lane preference vs balanced's 2.0
        const double primaryWeight   = 1.0;
        const double secondaryWeight = 5.0;

        return primaryWeight * primaryDist + secondaryWeight * secondaryDist;
    }

    /// <summary>
    /// Scores a candidate window using pure center-to-center Euclidean distance with a half-plane cone check.
    /// Pure distance with wide cone (~90° half-plane) — picks nearest window center-to-center.
    /// </summary>
    internal static double ScoreClosestInDirection(
        double originX, double originY,
        WindowInfo candidate,
        Direction direction)
    {
        // Compute candidate center
        double candCx = (candidate.Left + candidate.Right) / 2.0;
        double candCy = (candidate.Top + candidate.Bottom) / 2.0;

        // Half-plane cone check on center (not nearest edge)
        double dx = candCx - originX;
        double dy = candCy - originY;

        // Pure distance with wide cone (~90° half-plane) — picks nearest window center-to-center
        bool inCone = direction switch
        {
            Direction.Left  => dx < 0,
            Direction.Right => dx > 0,
            Direction.Up    => dy < 0,
            Direction.Down  => dy > 0,
            _               => false
        };

        if (!inCone)
            return double.MaxValue; // eliminated — not in half-plane cone

        // Score = pure Euclidean center-to-center distance
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Scores a candidate window using pure edge-to-edge distance on the movement axis.
    /// Ignores the perpendicular axis entirely — this is a 1D strategy.
    ///
    /// Edge-matching concept: for each direction, compare the same-type edge of the candidate
    /// to the reference edge of the foreground window:
    ///   Left:  compare right edges  — candidates whose right edge is left of the foreground's right edge
    ///   Right: compare left edges   — candidates whose left edge is right of the foreground's left edge
    ///   Up:    compare bottom edges — candidates whose bottom edge is above the foreground's bottom edge
    ///   Down:  compare top edges    — candidates whose top edge is below the foreground's top edge
    ///
    /// Score = absolute distance between the matching edges (lower = closer = better).
    /// Falls back to nearest-edge Balanced logic if foreground bounds cannot be retrieved.
    /// </summary>
    internal static unsafe double ScoreEdgeMatching(
        double originX, double originY,
        WindowInfo candidate,
        Direction direction)
    {
        // Populate the foreground bounds cache on first call per navigation cycle
        if (_fgBoundsCache.Hwnd == 0)
        {
            var fgHwnd = PInvoke.GetForegroundWindow();
            if (fgHwnd != default)
            {
                RECT boundsRect = default;
                var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref boundsRect, 1));
                var hr = PInvoke.DwmGetWindowAttribute(
                    fgHwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                    boundsBytes);

                if (hr.Succeeded && (boundsRect.right - boundsRect.left) > 0)
                {
                    _fgBoundsCache = ((nint)(IntPtr)(void*)fgHwnd,
                        boundsRect.left, boundsRect.top, boundsRect.right, boundsRect.bottom);
                }
            }
        }

        // If foreground bounds retrieval failed, fall back to Balanced (nearest-edge) logic
        if (_fgBoundsCache.Hwnd == 0)
            return ScoreCandidate(originX, originY, candidate, direction);

        int fgLeft   = _fgBoundsCache.Left;
        int fgTop    = _fgBoundsCache.Top;
        int fgRight  = _fgBoundsCache.Right;
        int fgBottom = _fgBoundsCache.Bottom;

        // Edge-matching: compare same-type edges; return distance if strictly in direction,
        // double.MaxValue otherwise. Perpendicular axis is completely ignored.
        return direction switch
        {
            Direction.Left  => candidate.Right < fgRight
                                ? (double)(fgRight - candidate.Right)
                                : double.MaxValue,
            Direction.Right => candidate.Left > fgLeft
                                ? (double)(candidate.Left - fgLeft)
                                : double.MaxValue,
            Direction.Up    => candidate.Bottom < fgBottom
                                ? (double)(fgBottom - candidate.Bottom)
                                : double.MaxValue,
            Direction.Down  => candidate.Top > fgTop
                                ? (double)(candidate.Top - fgTop)
                                : double.MaxValue,
            _               => double.MaxValue
        };
    }

    /// <summary>
    /// Scores a candidate window using near-edge-to-near-edge comparison on the movement axis.
    /// Ignores the perpendicular axis entirely — this is a 1D strategy.
    ///
    /// Near-edge concept: for each direction, the "near edge" is the edge of the window that faces
    /// the direction of movement. This is compared to the same edge of the foreground window:
    ///   Left:  compare left edges   — candidates whose left edge is left of the foreground's left edge
    ///   Right: compare right edges  — candidates whose right edge is right of the foreground's right edge
    ///   Up:    compare top edges    — candidates whose top edge is above the foreground's top edge
    ///   Down:  compare bottom edges — candidates whose bottom edge is below the foreground's bottom edge
    ///
    /// This differs from EdgeMatching, which uses the FAR edge of the foreground window (e.g., for a
    /// leftward move, EdgeMatching compares the foreground's right edge to the candidate's right edge;
    /// EdgeProximity compares the foreground's left edge to the candidate's left edge).
    ///
    /// Score = distance between the matching near edges (lower = closer = better).
    /// Falls back to nearest-edge Balanced logic if foreground bounds cannot be retrieved.
    /// </summary>
    internal static unsafe double ScoreEdgeProximity(
        double originX, double originY,
        WindowInfo candidate,
        Direction direction)
    {
        // Populate the foreground bounds cache on first call per navigation cycle
        if (_fgBoundsCache.Hwnd == 0)
        {
            var fgHwnd = PInvoke.GetForegroundWindow();
            if (fgHwnd != default)
            {
                RECT boundsRect = default;
                var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref boundsRect, 1));
                var hr = PInvoke.DwmGetWindowAttribute(
                    fgHwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                    boundsBytes);

                if (hr.Succeeded && (boundsRect.right - boundsRect.left) > 0)
                {
                    _fgBoundsCache = ((nint)(IntPtr)(void*)fgHwnd,
                        boundsRect.left, boundsRect.top, boundsRect.right, boundsRect.bottom);
                }
            }
        }

        // If foreground bounds retrieval failed, fall back to Balanced (nearest-edge) logic
        if (_fgBoundsCache.Hwnd == 0)
            return ScoreCandidate(originX, originY, candidate, direction);

        int fgLeft   = _fgBoundsCache.Left;
        int fgTop    = _fgBoundsCache.Top;
        int fgRight  = _fgBoundsCache.Right;
        int fgBottom = _fgBoundsCache.Bottom;

        // Near-edge comparison: compare the near-facing edge of both windows.
        // Strictly in direction (< not <=), perpendicular axis completely ignored.
        return direction switch
        {
            Direction.Left  => candidate.Left < fgLeft
                                ? (double)(fgLeft - candidate.Left)
                                : double.MaxValue,
            Direction.Right => candidate.Right > fgRight
                                ? (double)(candidate.Right - fgRight)
                                : double.MaxValue,
            Direction.Up    => candidate.Top < fgTop
                                ? (double)(fgTop - candidate.Top)
                                : double.MaxValue,
            Direction.Down  => candidate.Bottom > fgBottom
                                ? (double)(candidate.Bottom - fgBottom)
                                : double.MaxValue,
            _               => double.MaxValue
        };
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
