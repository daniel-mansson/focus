using System.Runtime.Versioning;
using global::Windows.Win32.Foundation;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Contract for overlay rendering implementations (RENDER-01).
/// Implementations paint a colored overlay onto a layered HWND.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal interface IOverlayRenderer
{
    /// <summary>
    /// Unique name used for config-driven renderer selection (CFG-07, RENDER-03).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Paints the overlay for the given bounds and color.
    /// Called on the STA thread. Must call UpdateLayeredWindow on the given HWND.
    /// </summary>
    /// <param name="hwnd">The overlay window handle to paint.</param>
    /// <param name="bounds">Target window bounds in physical screen coordinates.</param>
    /// <param name="argbColor">Color as 0xAARRGGBB (e.g., 0xBF4488CC for semi-transparent blue).</param>
    void Paint(HWND hwnd, RECT bounds, uint argbColor);
}
