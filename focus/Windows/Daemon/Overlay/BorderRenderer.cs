using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Default renderer that draws a rounded-corner border using GDI RoundRect into
/// a premultiplied-alpha DIB section, then composites via UpdateLayeredWindow.
/// Implements RENDER-02 (border rendering) and RENDER-03 (name = "border").
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class BorderRenderer : IOverlayRenderer
{
    public string Name => "border";

    private const int BorderThickness = 2;
    private const int CornerEllipse = 16; // Win11 ~8px radius = 16px diameter for GDI RoundRect

    /// <inheritdoc/>
    public unsafe void Paint(HWND hwnd, RECT bounds, uint argbColor)
    {
        int width  = bounds.right  - bounds.left;
        int height = bounds.bottom - bounds.top;

        if (width <= 0 || height <= 0)
            return;

        // Extract ARGB components (format: 0xAARRGGBB)
        byte alpha = (byte)(argbColor >> 24);
        byte r     = (byte)(argbColor >> 16);
        byte g     = (byte)(argbColor >> 8);
        byte b     = (byte)(argbColor);

        // 1. Get screen DC for palette matching in UpdateLayeredWindow.
        var screenDC = PInvoke.GetDC(HWND.Null);

        // 2. Create 32bpp top-down DIB section.
        //    Use the raw HBITMAP-returning overload: CreateDIBSection(HDC, BITMAPINFO*, DIB_USAGE, void**, HANDLE, uint)
        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize        = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth       = width;
        bmi.bmiHeader.biHeight      = -height; // negative = top-down
        bmi.bmiHeader.biPlanes      = 1;
        bmi.bmiHeader.biBitCount    = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        void* bits;
        var hBitmap = PInvoke.CreateDIBSection(screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);

        // 3. Create compatible DC and select bitmap.
        //    HBITMAP has implicit conversion to HGDIOBJ.
        var memDC     = PInvoke.CreateCompatibleDC(screenDC);
        var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

        // 4. Clear DIB to fully transparent (zero all bytes).
        PInvoke.GdiFlush();
        NativeMemory.Clear(bits, (nuint)(width * height * 4));

        // 5. Set background mode transparent so GDI gaps don't overwrite our zeros.
        PInvoke.SetBkMode(memDC, BACKGROUND_MODE.TRANSPARENT);

        // 6. Create GDI pen — COLORREF uses 0x00BBGGRR (no alpha).
        var colorRef = new COLORREF((uint)(b | ((uint)g << 8) | ((uint)r << 16)));
        var hPen     = PInvoke.CreatePen(PEN_STYLE.PS_SOLID, BorderThickness, colorRef);
        var hBrush   = PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.NULL_BRUSH);

        // HPEN has implicit conversion to HGDIOBJ.
        var oldPen   = PInvoke.SelectObject(memDC, (HGDIOBJ)hPen);
        var oldBrush = PInvoke.SelectObject(memDC, hBrush);

        // 7. Draw rounded rectangle (border only — NULL_BRUSH = hollow interior).
        PInvoke.RoundRect(memDC, 0, 0, width, height, CornerEllipse, CornerEllipse);

        // 8. Apply premultiplied alpha to all GDI-drawn pixels.
        //    GDI writes full-opacity color but sets alpha=0xFF; transparent pixels remain 0.
        //    We need: alpha = desired alpha, RGB = color * alpha / 255 (premultiplied).
        PInvoke.GdiFlush();
        uint* pixelBuf = (uint*)bits;
        for (int i = 0; i < width * height; i++)
        {
            uint pixel = pixelBuf[i];
            byte pixAlpha = (byte)(pixel >> 24);
            if (pixAlpha != 0) // Only modify pixels GDI touched
            {
                byte pr = (byte)((((pixel >> 16) & 0xFF) * (uint)alpha) / 255);
                byte pg = (byte)((((pixel >>  8) & 0xFF) * (uint)alpha) / 255);
                byte pb = (byte)(( (pixel        & 0xFF) * (uint)alpha) / 255);
                pixelBuf[i] = ((uint)alpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;
            }
        }

        // 9. Call UpdateLayeredWindow to composite the DIB onto the layered window.
        //    UpdateLayeredWindow uses System.Drawing.Point* for position args.
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

        // 10. Cleanup GDI resources in reverse order (prevent GDI handle leaks).
        PInvoke.SelectObject(memDC, oldPen);
        PInvoke.SelectObject(memDC, oldBrush);
        PInvoke.DeleteObject((HGDIOBJ)hPen);
        // Do NOT DeleteObject on NULL_BRUSH — it is a stock object.
        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }
}
