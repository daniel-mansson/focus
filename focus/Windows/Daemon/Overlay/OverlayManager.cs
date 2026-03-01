using System.Runtime.Versioning;
using global::Windows.Win32.Foundation;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Manages four OverlayWindow instances (one per Direction) and coordinates
/// show/hide/paint using the configured IOverlayRenderer. Also manages a 5th
/// foreground overlay window that draws a full-perimeter white border around
/// the currently active window when CAPSLOCK is held.
///
/// This is the public API surface for Plan 02 (debug command) and Phase 6 (daemon wiring).
/// Must be instantiated on the STA thread — OverlayWindow HWNDs are created in the constructor.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class OverlayManager : IDisposable
{
    private readonly IOverlayRenderer _renderer;
    private readonly OverlayColors _colors;
    private readonly Dictionary<Direction, OverlayWindow> _windows;
    private readonly OverlayWindow _foregroundWindow;
    private bool _disposed;

    /// <summary>
    /// Creates the manager and all four overlay windows.
    /// Must be called on the STA thread.
    /// </summary>
    public OverlayManager(IOverlayRenderer renderer, OverlayColors colors)
    {
        _renderer = renderer;
        _colors   = colors;

        _windows = new Dictionary<Direction, OverlayWindow>
        {
            [Direction.Left]  = new OverlayWindow(),
            [Direction.Right] = new OverlayWindow(),
            [Direction.Up]    = new OverlayWindow(),
            [Direction.Down]  = new OverlayWindow(),
        };

        _foregroundWindow = new OverlayWindow();
    }

    /// <summary>
    /// Shows and paints the overlay for the given direction at the specified screen bounds.
    /// </summary>
    public void ShowOverlay(Direction direction, RECT bounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var window = _windows[direction];
        window.Reposition(bounds);
        _renderer.Paint(window.Hwnd, bounds, _colors.GetArgb(direction), direction);
        window.Show();
    }

    /// <summary>
    /// Shows and paints the overlay for the given direction at the specified screen bounds,
    /// using a color override instead of the configured direction color.
    /// Used for the solo-window dim indicator.
    /// </summary>
    public void ShowOverlay(Direction direction, RECT bounds, uint colorOverride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var window = _windows[direction];
        window.Reposition(bounds);
        _renderer.Paint(window.Hwnd, bounds, colorOverride, direction);
        window.Show();
    }

    /// <summary>
    /// Hides the overlay for the given direction.
    /// </summary>
    public void HideOverlay(Direction direction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _windows[direction].Hide();
    }

    /// <summary>
    /// Shows a full-perimeter white border around the foreground window.
    /// Uses BorderRenderer.PaintFullBorder directly since this is a distinct contract
    /// (all 4 edges rendered together, no direction parameter).
    /// </summary>
    public void ShowForegroundOverlay(RECT bounds, uint argbColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _foregroundWindow.Reposition(bounds);
        BorderRenderer.PaintFullBorder(_foregroundWindow.Hwnd, bounds, argbColor);
        _foregroundWindow.Show();
    }

    /// <summary>
    /// Hides the foreground border overlay.
    /// </summary>
    public void HideForegroundOverlay()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _foregroundWindow.Hide();
    }

    /// <summary>
    /// Hides all four directional overlay windows and the foreground border overlay.
    /// </summary>
    public void HideAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var window in _windows.Values)
            window.Hide();
        _foregroundWindow.Hide();
    }

    /// <summary>
    /// Factory method for renderer selection (RENDER-03, CFG-07).
    /// Resolves renderer name from config to an IOverlayRenderer implementation.
    /// Unknown names fall back to the default border renderer.
    /// </summary>
    public static IOverlayRenderer CreateRenderer(string name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "border" => new BorderRenderer(),
            _        => new BorderRenderer(), // default fallback — do not throw
        };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var window in _windows.Values)
            window.Dispose();

        _foregroundWindow.Dispose();
    }
}
