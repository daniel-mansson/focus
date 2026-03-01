using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Focus.Windows;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Renders a small number label (1-9) onto a layered overlay window using GDI and UpdateLayeredWindow.
/// The label is a filled rounded-rectangle background (semi-transparent dark) with a white digit on top.
/// Rendering uses a premultiplied-alpha DIB section for correct compositing via UpdateLayeredWindow.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal static class NumberLabelRenderer
{
    // Label dimensions
    private const int LabelWidth  = 28;
    private const int LabelHeight = 28;
    private const int LabelMargin = 6;   // margin from window edge
    private const int CornerRadius = 6;

    // Background: ARGB 0xCC222222 — semi-transparent dark
    private const byte BgAlpha = 0xCC;
    private const byte BgR     = 0x22;
    private const byte BgG     = 0x22;
    private const byte BgB     = 0x22;

    public static unsafe void PaintNumberLabel(HWND hwnd, RECT windowBounds, int number,
        NumberOverlayPosition position)
    {
        // 1. Calculate label position within the window bounds
        var (labelX, labelY) = GetLabelPosition(windowBounds, position);

        // 2. Reposition the overlay window to the label location
        PInvoke.SetWindowPos(hwnd, new HWND(new IntPtr(-1)), // HWND_TOPMOST
            labelX, labelY, LabelWidth, LabelHeight,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        // 3. Create 32bpp top-down DIB section
        var screenDC = PInvoke.GetDC(HWND.Null);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize        = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth       = LabelWidth;
        bmi.bmiHeader.biHeight      = -LabelHeight; // top-down
        bmi.bmiHeader.biPlanes      = 1;
        bmi.bmiHeader.biBitCount    = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        void* bits;
        var hBitmap   = PInvoke.CreateDIBSection(screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        var memDC     = PInvoke.CreateCompatibleDC(screenDC);
        var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

        // 4. Clear to transparent
        PInvoke.GdiFlush();
        NativeMemory.Clear(bits, (nuint)(LabelWidth * LabelHeight * 4));

        // 5. Draw rounded-rect background directly into pixel buffer (premultiplied alpha)
        PInvoke.GdiFlush();
        uint* pixelBuf = (uint*)bits;

        for (int py = 0; py < LabelHeight; py++)
        {
            for (int px = 0; px < LabelWidth; px++)
            {
                if (IsInsideRoundedRect(px, py, LabelWidth, LabelHeight, CornerRadius))
                {
                    // Premultiply: pr = (BgR * BgAlpha) / 255
                    byte pr = (byte)((BgR * BgAlpha) / 255);
                    byte pg = (byte)((BgG * BgAlpha) / 255);
                    byte pb = (byte)((BgB * BgAlpha) / 255);
                    pixelBuf[py * LabelWidth + px] = ((uint)BgAlpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;
                }
            }
        }

        // 6. Draw text using GDI onto the DIB — create a bold font and render the digit.
        //    Strategy: render white text, then fix up the alpha channel for text pixels.
        LOGFONTW lf = default;
        lf.lfHeight  = 20;
        lf.lfWeight  = 700; // FW_BOLD
        lf.lfQuality = FONT_QUALITY.CLEARTYPE_QUALITY;
        "Segoe UI".AsSpan().CopyTo(lf.lfFaceName.AsSpan());

        // CreateFontIndirect returns DeleteObjectSafeHandle — use the 'in' overload.
        using var hFont  = PInvoke.CreateFontIndirect(in lf);
        var oldFont      = PInvoke.SelectObject(memDC, hFont);

        PInvoke.SetBkMode(memDC, BACKGROUND_MODE.TRANSPARENT);
        PInvoke.SetTextColor(memDC, new COLORREF(0x00FFFFFF)); // white (GDI uses 0x00BBGGRR)

        // DrawText requires a PCWSTR — use fixed to pin the string
        string text      = number.ToString();
        var textRect     = new RECT { left = 0, top = 0, right = LabelWidth, bottom = LabelHeight };
        var format       = DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE;
        fixed (char* pText = text)
        {
            PInvoke.DrawText(memDC, pText, text.Length, &textRect, format);
        }

        // 7. Fix alpha for text pixels: GDI DrawText writes RGB but does NOT set alpha.
        //    Scan for pixels where RGB != 0 but alpha == 0, indicating GDI wrote text there.
        PInvoke.GdiFlush();
        for (int i = 0; i < LabelWidth * LabelHeight; i++)
        {
            uint pixel = pixelBuf[i];
            byte a = (byte)(pixel >> 24);
            byte r = (byte)(pixel >> 16);
            byte g = (byte)(pixel >> 8);
            byte b = (byte)(pixel);

            if (a == 0 && (r | g | b) != 0)
            {
                // Use the max RGB component as the text intensity (ClearType subpixel)
                byte intensity = Math.Max(r, Math.Max(g, b));
                // Premultiply white text at that intensity
                pixelBuf[i] = ((uint)intensity << 24) | ((uint)intensity << 16) | ((uint)intensity << 8) | intensity;
            }
        }

        // 8. Call UpdateLayeredWindow
        var blend = new BLENDFUNCTION
        {
            BlendOp             = 0,   // AC_SRC_OVER
            BlendFlags          = 0,
            SourceConstantAlpha = 255,
            AlphaFormat         = 1,   // AC_SRC_ALPHA
        };

        var ptDst   = new System.Drawing.Point(labelX, labelY);
        var sizeSrc = new SIZE(LabelWidth, LabelHeight);
        var ptSrc   = new System.Drawing.Point(0, 0);

        PInvoke.UpdateLayeredWindow(hwnd, screenDC, &ptDst, &sizeSrc, memDC, &ptSrc,
            new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);

        // 9. Cleanup — hFont disposed via 'using'; SelectObject restores old font first.
        PInvoke.SelectObject(memDC, oldFont);
        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }

    private static (int X, int Y) GetLabelPosition(RECT windowBounds, NumberOverlayPosition position)
    {
        int wWidth  = windowBounds.right  - windowBounds.left;
        int wHeight = windowBounds.bottom - windowBounds.top;

        return position switch
        {
            NumberOverlayPosition.TopLeft     => (windowBounds.left + LabelMargin, windowBounds.top + LabelMargin),
            NumberOverlayPosition.TopRight    => (windowBounds.right - LabelWidth - LabelMargin, windowBounds.top + LabelMargin),
            NumberOverlayPosition.BottomLeft  => (windowBounds.left + LabelMargin, windowBounds.bottom - LabelHeight - LabelMargin),
            NumberOverlayPosition.BottomRight => (windowBounds.right - LabelWidth - LabelMargin, windowBounds.bottom - LabelHeight - LabelMargin),
            NumberOverlayPosition.TopCenter   => (windowBounds.left + (wWidth - LabelWidth) / 2, windowBounds.top + LabelMargin),
            NumberOverlayPosition.Center      => (windowBounds.left + (wWidth - LabelWidth) / 2, windowBounds.top + (wHeight - LabelHeight) / 2),
            _                                 => (windowBounds.left + LabelMargin, windowBounds.top + LabelMargin),
        };
    }

    private static bool IsInsideRoundedRect(int px, int py, int w, int h, int radius)
    {
        bool inTL = px < radius && py < radius;
        bool inTR = px >= w - radius && py < radius;
        bool inBL = px < radius && py >= h - radius;
        bool inBR = px >= w - radius && py >= h - radius;

        if (inTL)
        {
            float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - radius) * (py - radius));
            return dist <= radius;
        }
        if (inTR)
        {
            int cx = w - 1 - radius;
            float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - radius) * (py - radius));
            return dist <= radius;
        }
        if (inBL)
        {
            int cy = h - 1 - radius;
            float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - cy) * (py - cy));
            return dist <= radius;
        }
        if (inBR)
        {
            int cx = w - 1 - radius;
            int cy = h - 1 - radius;
            float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            return dist <= radius;
        }

        return true; // Inside the non-corner region
    }
}
