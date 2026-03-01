using System.Globalization;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Per-direction ARGB color configuration for overlay rendering (CFG-05).
/// Colors are stored as hex strings (e.g. "#BF4488CC") and parsed on demand.
/// </summary>
internal class OverlayColors
{
    public string Left  { get; set; } = "#BF4488CC"; // muted blue, ~75% opacity
    public string Right { get; set; } = "#BFCC4444"; // muted red
    public string Up    { get; set; } = "#BF44AA66"; // muted green
    public string Down  { get; set; } = "#BFCCAA33"; // muted amber

    public static OverlayColors Default => new();

    private static readonly uint DefaultLeft  = 0xBF4488CC;
    private static readonly uint DefaultRight = 0xBFCC4444;
    private static readonly uint DefaultUp    = 0xBF44AA66;
    private static readonly uint DefaultDown  = 0xBFCCAA33;

    /// <summary>
    /// Returns the ARGB color (0xAARRGGBB) for the requested direction.
    /// Falls back to hardcoded defaults if parsing fails.
    /// </summary>
    public uint GetArgb(Direction direction)
    {
        var (hexStr, fallback) = direction switch
        {
            Direction.Left  => (Left,  DefaultLeft),
            Direction.Right => (Right, DefaultRight),
            Direction.Up    => (Up,    DefaultUp),
            Direction.Down  => (Down,  DefaultDown),
            _               => (Left,  DefaultLeft),
        };

        try
        {
            var hex = hexStr.TrimStart('#');
            return uint.Parse(hex, NumberStyles.HexNumber);
        }
        catch
        {
            return fallback;
        }
    }
}
