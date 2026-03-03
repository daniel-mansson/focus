using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Renders mode-indicator chevron arrows on layered overlay windows using direct pixel buffer writes.
/// Move mode: 4 compass chevrons at window center.
/// Resize mode: left/right chevron at right edge center, up/down chevron at top edge center.
/// Follows the same DIB + UpdateLayeredWindow pipeline as BorderRenderer.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal static class ArrowRenderer
{
    private const int EdgeMargin = 2;

    private static int GetArrowSize(int width, int height)
        => Math.Clamp(Math.Min(width, height) * 3 / 80, 5, 15);

    private static int GetLineThickness(int arrowSize)
        => Math.Clamp(arrowSize / 6, 2, 5);

    /// <summary>
    /// Renders 4 hollow chevron arrows as a compass/cross at the window center (OVRL-01).
    /// </summary>
    public static unsafe void PaintMoveArrows(HWND hwnd, RECT bounds, uint argbColor)
    {
        int width  = bounds.right  - bounds.left;
        int height = bounds.bottom - bounds.top;

        if (width <= 0 || height <= 0)
            return;

        uint pixel = PremultiplyPixel(argbColor);

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
        int thickness = GetLineThickness(arrowSize);
        int depth     = arrowSize * 3 / 5;
        int spread    = arrowSize * 3 / 5;
        int gap       = arrowSize / 2 + spread * 2;
        int cx        = width  / 2;
        int cy        = height / 2;

        // Up chevron: tip pointing up
        DrawChevron(pixelBuf, width, height,
            cx, cy - gap - depth,
            cx - spread, cy - gap,
            cx + spread, cy - gap,
            thickness, pixel);

        // Down chevron: tip pointing down
        DrawChevron(pixelBuf, width, height,
            cx, cy + gap + depth,
            cx - spread, cy + gap,
            cx + spread, cy + gap,
            thickness, pixel);

        // Left chevron: tip pointing left
        DrawChevron(pixelBuf, width, height,
            cx - gap - depth, cy,
            cx - gap, cy - spread,
            cx - gap, cy + spread,
            thickness, pixel);

        // Right chevron: tip pointing right
        DrawChevron(pixelBuf, width, height,
            cx + gap + depth, cy,
            cx + gap, cy - spread,
            cx + gap, cy + spread,
            thickness, pixel);

        BlitToLayeredWindow(hwnd, screenDC, memDC, bounds, width, height);

        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }

    /// <summary>
    /// Renders axis indicator chevrons for resize mode (OVRL-02, OVRL-03).
    /// At right edge center: back-to-back left+right chevron pair.
    /// At top edge center:   back-to-back up+down chevron pair.
    /// </summary>
    public static unsafe void PaintResizeArrows(HWND hwnd, RECT bounds, uint argbColor)
    {
        int width  = bounds.right  - bounds.left;
        int height = bounds.bottom - bounds.top;

        if (width <= 0 || height <= 0)
            return;

        uint pixel = PremultiplyPixel(argbColor);

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
        int thickness  = GetLineThickness(arrowSize);
        int pairSize   = arrowSize * 3 / 4;
        int pairDepth  = pairSize * 3 / 5;
        int pairSpread = pairSize * 3 / 5;
        int pairGap    = pairSize / 4;

        // --- Horizontal axis pair at right edge center ---
        int hx = width - pairSize - EdgeMargin;
        int hy = height / 2;

        // Left-pointing chevron
        DrawChevron(pixelBuf, width, height,
            hx - pairGap - pairDepth, hy,
            hx - pairGap, hy - pairSpread,
            hx - pairGap, hy + pairSpread,
            thickness, pixel);

        // Right-pointing chevron
        DrawChevron(pixelBuf, width, height,
            hx + pairGap + pairDepth, hy,
            hx + pairGap, hy - pairSpread,
            hx + pairGap, hy + pairSpread,
            thickness, pixel);

        // --- Vertical axis pair at top edge center ---
        int vx = width / 2;
        int vy = pairSize + EdgeMargin;

        // Up-pointing chevron
        DrawChevron(pixelBuf, width, height,
            vx, vy - pairGap - pairDepth,
            vx - pairSpread, vy - pairGap,
            vx + pairSpread, vy - pairGap,
            thickness, pixel);

        // Down-pointing chevron
        DrawChevron(pixelBuf, width, height,
            vx, vy + pairGap + pairDepth,
            vx - pairSpread, vy + pairGap,
            vx + pairSpread, vy + pairGap,
            thickness, pixel);

        BlitToLayeredWindow(hwnd, screenDC, memDC, bounds, width, height);

        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }

    private static uint PremultiplyPixel(uint argbColor)
    {
        byte alpha = (byte)(argbColor >> 24);
        byte r     = (byte)(argbColor >> 16);
        byte g     = (byte)(argbColor >> 8);
        byte b     = (byte)(argbColor);
        byte pr = (byte)((r * alpha) / 255);
        byte pg = (byte)((g * alpha) / 255);
        byte pb = (byte)((b * alpha) / 255);
        return ((uint)alpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;
    }

    /// <summary>
    /// Draws a hollow chevron (two thick lines from tip to each arm endpoint, no base line).
    /// </summary>
    private static unsafe void DrawChevron(
        uint* pixelBuf, int width, int height,
        int tipX, int tipY, int armLeftX, int armLeftY, int armRightX, int armRightY,
        int thickness, uint pixel)
    {
        DrawThickLine(pixelBuf, width, height, tipX, tipY, armLeftX, armLeftY, thickness, pixel);
        DrawThickLine(pixelBuf, width, height, tipX, tipY, armRightX, armRightY, thickness, pixel);
    }

    /// <summary>
    /// Draws a thick line segment using distance-to-segment test for each pixel in the bounding box.
    /// </summary>
    private static unsafe void DrawThickLine(
        uint* pixelBuf, int width, int height,
        int x0, int y0, int x1, int y1,
        int thickness, uint pixel)
    {
        float halfT = thickness / 2.0f;
        float halfTSq = halfT * halfT;

        int minX = Math.Max(0, Math.Min(x0, x1) - thickness);
        int maxX = Math.Min(width  - 1, Math.Max(x0, x1) + thickness);
        int minY = Math.Max(0, Math.Min(y0, y1) - thickness);
        int maxY = Math.Min(height - 1, Math.Max(y0, y1) + thickness);

        float dx = x1 - x0;
        float dy = y1 - y0;
        float lenSq = dx * dx + dy * dy;

        if (lenSq < 1f)
            return;

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                float t = ((px - x0) * dx + (py - y0) * dy) / lenSq;
                t = Math.Clamp(t, 0f, 1f);
                float closestX = x0 + t * dx;
                float closestY = y0 + t * dy;
                float distSq = (px - closestX) * (px - closestX) + (py - closestY) * (py - closestY);
                if (distSq <= halfTSq)
                    pixelBuf[py * width + px] = pixel;
            }
        }
    }

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
