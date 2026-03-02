namespace Focus.Windows;

/// <summary>
/// Pure static class for grid step calculation and snap alignment.
/// All methods are stateless -- Win32 state (rcWork) is passed in by the caller.
/// Consumed by Phase 11 window move/resize operations.
/// </summary>
internal static class GridCalculator
{
    /// <summary>
    /// Computes grid step size in physical pixels for the given monitor work area.
    /// Uses rcWork dimensions (not rcMonitor) to exclude the taskbar.
    /// </summary>
    public static (int StepX, int StepY) GetGridStep(int workAreaWidth, int workAreaHeight, int gridFractionX, int gridFractionY)
    {
        int stepX = Math.Max(1, workAreaWidth / gridFractionX);
        int stepY = Math.Max(1, workAreaHeight / gridFractionY);
        return (stepX, stepY);
    }

    /// <summary>
    /// Returns the nearest grid line position for a given coordinate.
    /// origin = rcWork.left (for X) or rcWork.top (for Y) in virtual-screen coordinates.
    /// Grid lines are at origin + N*step for N = 0, 1, 2, ...
    /// </summary>
    public static int NearestGridLine(int pos, int origin, int step)
    {
        if (step <= 0) return pos;
        int offset = pos - origin;
        int nearest = (int)Math.Round((double)offset / step) * step;
        return origin + nearest;
    }

    /// <summary>
    /// Returns the nearest grid line position that is less than or equal to pos (floor/round-down).
    /// Used for edges moving outward/inward toward smaller values (leftward or upward).
    /// origin = rcWork.left (for X) or rcWork.top (for Y) in virtual-screen coordinates.
    /// </summary>
    public static int NearestGridLineFloor(int pos, int origin, int step)
    {
        if (step <= 0) return pos;
        int offset = pos - origin;
        int floored = (int)Math.Floor((double)offset / step) * step;
        return origin + floored;
    }

    /// <summary>
    /// Returns the nearest grid line position that is greater than or equal to pos (ceiling/round-up).
    /// Used for edges moving outward/inward toward larger values (rightward or downward).
    /// origin = rcWork.left (for X) or rcWork.top (for Y) in virtual-screen coordinates.
    /// </summary>
    public static int NearestGridLineCeiling(int pos, int origin, int step)
    {
        if (step <= 0) return pos;
        int offset = pos - origin;
        int ceiled = (int)Math.Ceiling((double)offset / step) * step;
        return origin + ceiled;
    }

    /// <summary>
    /// Returns true if pos is within snapTolerancePx of the nearest grid line.
    /// Used to determine if a window is "close enough" to aligned (skip snap-only on first press).
    /// </summary>
    public static bool IsAligned(int pos, int origin, int step, int snapTolerancePx)
    {
        int nearest = NearestGridLine(pos, origin, step);
        return Math.Abs(pos - nearest) <= snapTolerancePx;
    }

    /// <summary>
    /// Converts snap tolerance from percentage of grid step to pixels.
    /// Computed per-axis since gridFractionX != gridFractionY produces different step sizes.
    /// </summary>
    public static int GetSnapTolerancePx(int step, int snapTolerancePercent)
        => Math.Max(1, step * snapTolerancePercent / 100);
}
