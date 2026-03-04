using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Renders a grid overlay on a layered window using direct pixel buffer writes.
/// Grid lines are drawn at regular intervals matching the configured grid fractions.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal static class GridRenderer
{
    // ~25% opacity white — subtle grid lines
    private const uint GridColor = 0x40FFFFFF;

    /// <summary>
    /// Paints grid lines onto the given overlay window at the specified work area bounds.
    /// Vertical lines at every stepX pixels, horizontal lines at every stepY pixels.
    /// </summary>
    public static unsafe void PaintGrid(HWND hwnd, RECT workArea, int stepX, int stepY)
    {
        int width  = workArea.right  - workArea.left;
        int height = workArea.bottom - workArea.top;

        if (width <= 0 || height <= 0 || stepX <= 0 || stepY <= 0)
            return;

        const uint color = GridColor;
        byte alpha = unchecked((byte)(color >> 24));
        byte r     = unchecked((byte)(color >> 16));
        byte g     = unchecked((byte)(color >> 8));
        byte b     = unchecked((byte)(color));

        // Premultiply once
        byte pr = (byte)((r * alpha) / 255);
        byte pg = (byte)((g * alpha) / 255);
        byte pb = (byte)((b * alpha) / 255);
        uint pixel = ((uint)alpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;

        var screenDC = PInvoke.GetDC(HWND.Null);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize        = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth       = width;
        bmi.bmiHeader.biHeight      = -height;
        bmi.bmiHeader.biPlanes      = 1;
        bmi.bmiHeader.biBitCount    = 32;
        bmi.bmiHeader.biCompression = 0;

        void* bits;
        var hBitmap = PInvoke.CreateDIBSection(screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);

        var memDC     = PInvoke.CreateCompatibleDC(screenDC);
        var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

        PInvoke.GdiFlush();
        NativeMemory.Clear(bits, (nuint)(width * height * 4));

        uint* pixelBuf = (uint*)bits;

        // Draw vertical grid lines
        for (int x = stepX; x < width; x += stepX)
        {
            for (int y = 0; y < height; y++)
                pixelBuf[y * width + x] = pixel;
        }

        // Draw horizontal grid lines
        for (int y = stepY; y < height; y += stepY)
        {
            for (int x = 0; x < width; x++)
                pixelBuf[y * width + x] = pixel;
        }

        var blend = new BLENDFUNCTION
        {
            BlendOp             = 0,
            BlendFlags          = 0,
            SourceConstantAlpha = 255,
            AlphaFormat         = 1,
        };

        var ptDst   = new System.Drawing.Point(workArea.left, workArea.top);
        var sizeSrc = new SIZE(width, height);
        var ptSrc   = new System.Drawing.Point(0, 0);

        PInvoke.UpdateLayeredWindow(
            hwnd, screenDC, &ptDst, &sizeSrc, memDC, &ptSrc,
            new COLORREF(0), &blend,
            UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);

        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }
}
