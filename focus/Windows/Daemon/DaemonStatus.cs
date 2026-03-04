namespace Focus.Windows.Daemon;

/// <summary>
/// Mutable state holder for daemon health information displayed in the tray context menu.
/// All reads and writes happen on the STA thread, so no locking is needed.
/// </summary>
internal sealed class DaemonStatus
{
    /// <summary>
    /// The time the daemon started. Used to compute uptime.
    /// </summary>
    public DateTime StartTime { get; } = DateTime.Now;

    /// <summary>
    /// Human-readable description of the last navigation action.
    /// Defaults to an em-dash placeholder when no navigation has occurred yet.
    /// </summary>
    public string LastAction { get; set; } = "\u2014";

    /// <summary>
    /// Returns a human-friendly uptime string based on elapsed time since StartTime.
    /// </summary>
    public string FormatUptime()
    {
        var elapsed = DateTime.Now - StartTime;
        if (elapsed.TotalHours >= 1)
            return $"Uptime: {(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        if (elapsed.TotalMinutes >= 1)
            return $"Uptime: {(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        return $"Uptime: {(int)elapsed.TotalSeconds}s";
    }

    /// <summary>
    /// Returns the formatted last action label for display in the tray menu.
    /// </summary>
    public string FormatLastAction() => $"Last: {LastAction}";
}
