using System.Runtime.Versioning;
using Focus.Windows.Daemon;
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
    private readonly OverlayWindow _modeArrowWindow;
    private readonly List<OverlayWindow> _numberWindows;
    private bool _disposed;

    /// <summary>
    /// Creates the manager and all four overlay windows plus 9 number label windows.
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
        _modeArrowWindow  = new OverlayWindow();

        _numberWindows = new List<OverlayWindow>();
        for (int i = 0; i < 9; i++)
            _numberWindows.Add(new OverlayWindow());
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
    /// Shows mode-specific arrow indicators on the mode arrow overlay window.
    /// Move mode: 4 compass arrows at window center (OVRL-01).
    /// Grow mode: axis indicator arrow pairs at right edge and top edge (OVRL-02, OVRL-03).
    /// </summary>
    public void ShowModeArrows(RECT bounds, WindowMode mode, uint argbColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _modeArrowWindow.Reposition(bounds);
        if (mode == WindowMode.Move)
            ArrowRenderer.PaintMoveArrows(_modeArrowWindow.Hwnd, bounds, argbColor);
        else if (mode == WindowMode.Grow)
            ArrowRenderer.PaintResizeArrows(_modeArrowWindow.Hwnd, bounds, argbColor);
        _modeArrowWindow.Show();
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
    /// Renders a number label (1-9) on the overlay window for the given window bounds.
    /// The label appears at the configured position within the window's bounds.
    /// </summary>
    public void ShowNumberLabel(int number, RECT windowBounds, NumberOverlayPosition position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (number < 1 || number > 9) return;

        var window = _numberWindows[number - 1];
        NumberLabelRenderer.PaintNumberLabel(window.Hwnd, windowBounds, number, position);
        window.Show();
    }

    /// <summary>
    /// Hides all four directional overlay windows, the foreground border overlay,
    /// and all nine number label overlay windows.
    /// </summary>
    public void HideAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var window in _windows.Values)
            window.Hide();
        _foregroundWindow.Hide();
        _modeArrowWindow.Hide();
        foreach (var nw in _numberWindows)
            nw.Hide();
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
        _modeArrowWindow.Dispose();

        foreach (var nw in _numberWindows)
            nw.Dispose();
    }
}
