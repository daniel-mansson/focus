using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Renders mode-indicator arrows on layered overlay windows using direct pixel buffer writes.
/// Move mode: 4 compass arrows at window center.
/// Resize mode: left/right arrow at right edge center, up/down arrow at top edge center.
/// Follows the same DIB + UpdateLayeredWindow pipeline as BorderRenderer.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal static class ArrowRenderer
{
    /// <summary>
    /// Arrow height in pixels, clamped to a reasonable range relative to window size.
    /// </summary>
    private static int GetArrowSize(int width, int height)
        => Math.Clamp(Math.Min(width, height) / 8, 16, 48);

    /// <summary>
    /// Renders 4 filled triangle arrows as a compass/cross at the window center (OVRL-01).
    /// Up/down/left/right arrows point outward from center with a small gap.
    /// </summary>
    public static unsafe void PaintMoveArrows(HWND hwnd, RECT bounds, uint argbColor)
    {
        int width  = bounds.right  - bounds.left;
        int height = bounds.bottom - bounds.top;

        if (width <= 0 || height <= 0)
            return;

        byte alpha = (byte)(argbColor >> 24);
        byte r     = (byte)(argbColor >> 16);
        byte g     = (byte)(argbColor >> 8);
        byte b     = (byte)(argbColor);

        var screenDC = PInvoke.GetDC(HWND.Null);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize        = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth       = width;
        bmi.bmiHeader.biHeight      = -height; // top-down
        bmi.bmiHeader.biPlanes      = 1;
        bmi.bmiHeader.biBitCount    = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        void* bits;
        var hBitmap = PInvoke.CreateDIBSection(screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);

        var memDC     = PInvoke.CreateCompatibleDC(screenDC);
        var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

        PInvoke.GdiFlush();
        NativeMemory.Clear(bits, (nuint)(width * height * 4));

        PInvoke.GdiFlush();
        uint* pixelBuf = (uint*)bits;

        int arrowSize = GetArrowSize(width, height);
        int baseWidth = arrowSize * 2 / 3;
        int gap       = arrowSize / 3;
        int cx        = width  / 2;
        int cy        = height / 2;

        // Up arrow: tip pointing up from center
        FillTriangle(pixelBuf, width, height,
            cx,            cy - arrowSize,   // tip
            cx - baseWidth / 2, cy - gap,    // base left
            cx + baseWidth / 2, cy - gap,    // base right
            alpha, r, g, b);

        // Down arrow: tip pointing down from center
        FillTriangle(pixelBuf, width, height,
            cx,            cy + arrowSize,   // tip
            cx - baseWidth / 2, cy + gap,    // base left
            cx + baseWidth / 2, cy + gap,    // base right
            alpha, r, g, b);

        // Left arrow: tip pointing left from center
        FillTriangle(pixelBuf, width, height,
            cx - arrowSize, cy,              // tip
            cx - gap, cy - baseWidth / 2,    // base top
            cx - gap, cy + baseWidth / 2,    // base bottom
            alpha, r, g, b);

        // Right arrow: tip pointing right from center
        FillTriangle(pixelBuf, width, height,
            cx + arrowSize, cy,              // tip
            cx + gap, cy - baseWidth / 2,    // base top
            cx + gap, cy + baseWidth / 2,    // base bottom
            alpha, r, g, b);

        BlitToLayeredWindow(hwnd, screenDC, memDC, bounds, width, height);

        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }

    /// <summary>
    /// Renders axis indicator arrows for resize mode (OVRL-02, OVRL-03).
    /// At right edge center: back-to-back left+right triangle pair indicating horizontal axis.
    /// At top edge center:   back-to-back up+down triangle pair indicating vertical axis.
    /// </summary>
    public static unsafe void PaintResizeArrows(HWND hwnd, RECT bounds, uint argbColor)
    {
        int width  = bounds.right  - bounds.left;
        int height = bounds.bottom - bounds.top;

        if (width <= 0 || height <= 0)
            return;

        byte alpha = (byte)(argbColor >> 24);
        byte r     = (byte)(argbColor >> 16);
        byte g     = (byte)(argbColor >> 8);
        byte b     = (byte)(argbColor);

        var screenDC = PInvoke.GetDC(HWND.Null);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize        = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth       = width;
        bmi.bmiHeader.biHeight      = -height; // top-down
        bmi.bmiHeader.biPlanes      = 1;
        bmi.bmiHeader.biBitCount    = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        void* bits;
        var hBitmap = PInvoke.CreateDIBSection(screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);

        var memDC     = PInvoke.CreateCompatibleDC(screenDC);
        var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

        PInvoke.GdiFlush();
        NativeMemory.Clear(bits, (nuint)(width * height * 4));

        PInvoke.GdiFlush();
        uint* pixelBuf = (uint*)bits;

        int arrowSize  = GetArrowSize(width, height);
        int pairSize   = arrowSize * 3 / 4; // slightly smaller for back-to-back pairs
        int pairBase   = pairSize * 2 / 3;

        // --- Horizontal axis pair at right edge center ---
        int hx = width - arrowSize * 2;  // anchor x for the pair
        int hy = height / 2;             // vertical center

        // Left-pointing triangle of horizontal pair
        FillTriangle(pixelBuf, width, height,
            hx - pairSize, hy,               // tip (pointing left)
            hx, hy - pairBase / 2,           // base top
            hx, hy + pairBase / 2,           // base bottom
            alpha, r, g, b);

        // Right-pointing triangle of horizontal pair
        FillTriangle(pixelBuf, width, height,
            hx + pairSize, hy,               // tip (pointing right)
            hx, hy - pairBase / 2,           // base top
            hx, hy + pairBase / 2,           // base bottom
            alpha, r, g, b);

        // --- Vertical axis pair at top edge center ---
        int vx = width  / 2;              // horizontal center
        int vy = arrowSize * 2;           // anchor y for the pair

        // Up-pointing triangle of vertical pair
        FillTriangle(pixelBuf, width, height,
            vx, vy - pairSize,               // tip (pointing up)
            vx - pairBase / 2, vy,           // base left
            vx + pairBase / 2, vy,           // base right
            alpha, r, g, b);

        // Down-pointing triangle of vertical pair
        FillTriangle(pixelBuf, width, height,
            vx, vy + pairSize,               // tip (pointing down)
            vx - pairBase / 2, vy,           // base left
            vx + pairBase / 2, vy,           // base right
            alpha, r, g, b);

        BlitToLayeredWindow(hwnd, screenDC, memDC, bounds, width, height);

        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }

    /// <summary>
    /// Fills a triangle defined by three vertices into the pixel buffer using a cross-product
    /// sign test for inside/outside determination. Writes premultiplied ARGB pixels.
    /// </summary>
    private static unsafe void FillTriangle(
        uint* pixelBuf, int width, int height,
        int x0, int y0, int x1, int y1, int x2, int y2,
        byte alpha, byte r, byte g, byte b)
    {
        // Premultiplied pixel value (alpha is full for arrows — same opacity throughout)
        byte pr = (byte)((r * alpha) / 255);
        byte pg = (byte)((g * alpha) / 255);
        byte pb = (byte)((b * alpha) / 255);
        uint pixel = ((uint)alpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;

        // Bounding box (clamped to DIB bounds)
        int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
        int maxX = Math.Min(width  - 1, Math.Max(x0, Math.Max(x1, x2)));
        int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
        int maxY = Math.Min(height - 1, Math.Max(y0, Math.Max(y1, y2)));

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                if (IsInsideTriangle(px, py, x0, y0, x1, y1, x2, y2))
                    pixelBuf[py * width + px] = pixel;
            }
        }
    }

    /// <summary>
    /// Returns true if point (px, py) is inside or on the edge of the triangle
    /// defined by (x0,y0), (x1,y1), (x2,y2), using the cross-product sign test.
    /// </summary>
    private static bool IsInsideTriangle(
        int px, int py,
        int x0, int y0, int x1, int y1, int x2, int y2)
    {
        int d1 = (px - x2) * (y0 - y2) - (x0 - x2) * (py - y2);
        int d2 = (px - x0) * (y1 - y0) - (x1 - x0) * (py - y0);
        int d3 = (px - x1) * (y2 - y1) - (x2 - x1) * (py - y1);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// Calls UpdateLayeredWindow to composite the pixel buffer onto the layered window.
    /// Extracted to avoid code duplication between PaintMoveArrows and PaintResizeArrows.
    /// </summary>
    private static unsafe void BlitToLayeredWindow(
        HWND hwnd, HDC screenDC, HDC memDC, RECT bounds, int width, int height)
    {
        var blend = new BLENDFUNCTION
        {
            BlendOp             = 0,   // AC_SRC_OVER
            BlendFlags          = 0,
            SourceConstantAlpha = 255, // use per-pixel alpha from the DIB
            AlphaFormat         = 1,   // AC_SRC_ALPHA
        };

        var ptDst   = new System.Drawing.Point(bounds.left, bounds.top);
        var sizeSrc = new SIZE(width, height);
        var ptSrc   = new System.Drawing.Point(0, 0);

        PInvoke.UpdateLayeredWindow(
            hwnd,
            screenDC,
            &ptDst,
            &sizeSrc,
            memDC,
            &ptSrc,
            new COLORREF(0),
            &blend,
            UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
    }
}
