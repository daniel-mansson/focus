namespace Focus.Windows;

/// <summary>
/// The four navigation directions supported by the focus tool.
/// </summary>
internal enum Direction { Left, Right, Up, Down }

/// <summary>
/// Parses direction strings (case-insensitive) to the Direction enum.
/// </summary>
internal static class DirectionParser
{
    public static Direction? Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "left"  => Direction.Left,
        "right" => Direction.Right,
        "up"    => Direction.Up,
        "down"  => Direction.Down,
        _       => null
    };
}
