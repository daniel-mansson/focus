using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Focus.Windows;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Renderer that draws a directional edge border using direct pixel buffer writes into
/// a premultiplied-alpha DIB section, then composites via UpdateLayeredWindow.
/// Implements RENDER-02 (border rendering) and RENDER-03 (name = "border").
///
/// For each direction, renders:
///   - The primary edge (full alpha, BorderThickness wide)
///   - The two relevant corner arcs (full alpha, connecting primary edge to fade tails)
///   - 20% fade tails on perpendicular edges (gradient alpha, full opacity -> transparent)
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class BorderRenderer : IOverlayRenderer
{
    public string Name => "border";

    private const int BorderThickness = 2;
    private const int CornerRadius = 8;   // 8px radius (Win11 ~8px corner radius)
    private const float FadeExtent = 0.20f; // 20% of the perpendicular dimension for fade tails

    /// <inheritdoc/>
    public unsafe void Paint(HWND hwnd, RECT bounds, uint argbColor, Direction direction)
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
        var memDC     = PInvoke.CreateCompatibleDC(screenDC);
        var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

        // 4. Clear DIB to fully transparent (zero all bytes).
        PInvoke.GdiFlush();
        NativeMemory.Clear(bits, (nuint)(width * height * 4));

        // 5. Write directional edge pixels directly to the pixel buffer (premultiplied ARGB).
        //    This replaces GDI RoundRect + premultiplied fixup loop from the old approach.
        PInvoke.GdiFlush();
        uint* pixelBuf = (uint*)bits;

        // Effective radius clamped for small windows
        int effectiveRadius = Math.Min(CornerRadius, Math.Min(width / 2, height / 2));
        int fadeLenH = Math.Max(1, (int)(width  * FadeExtent));
        int fadeLenV = Math.Max(1, (int)(height * FadeExtent));

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                float a = GetPixelAlpha(px, py, width, height, direction,
                                        BorderThickness, effectiveRadius, fadeLenH, fadeLenV);
                if (a <= 0f) continue;

                byte localAlpha = (byte)(a * alpha);
                byte pr = (byte)((r * localAlpha) / 255);
                byte pg = (byte)((g * localAlpha) / 255);
                byte pb = (byte)((b * localAlpha) / 255);
                pixelBuf[py * width + px] = ((uint)localAlpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;
            }
        }

        // 6. Call UpdateLayeredWindow to composite the DIB onto the layered window.
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

        // 7. Cleanup GDI resources (no pen/brush created — only bitmap and DCs).
        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }

    /// <summary>
    /// Renders a full-perimeter rounded-rectangle border on a layered overlay window.
    /// All 4 edges are rendered at full opacity — no fade tails. Used for the foreground
    /// window highlight overlay (white border) that appears simultaneously with directional overlays.
    /// </summary>
    /// <param name="hwnd">The overlay window HWND (must be a layered window).</param>
    /// <param name="bounds">Screen-coordinate bounding rect of the target window.</param>
    /// <param name="argbColor">Color in 0xAARRGGBB format (e.g. 0xE0FFFFFF for ~88% white).</param>
    public static unsafe void PaintFullBorder(HWND hwnd, RECT bounds, uint argbColor)
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
        var memDC     = PInvoke.CreateCompatibleDC(screenDC);
        var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

        // 4. Clear DIB to fully transparent (zero all bytes).
        PInvoke.GdiFlush();
        NativeMemory.Clear(bits, (nuint)(width * height * 4));

        // 5. Write full-perimeter border pixels (premultiplied ARGB).
        PInvoke.GdiFlush();
        uint* pixelBuf = (uint*)bits;

        int effectiveRadius = Math.Min(CornerRadius, Math.Min(width / 2, height / 2));

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                float a = GetFullBorderAlpha(px, py, width, height, BorderThickness, effectiveRadius);
                if (a <= 0f) continue;

                byte localAlpha = (byte)(a * alpha);
                byte pr = (byte)((r * localAlpha) / 255);
                byte pg = (byte)((g * localAlpha) / 255);
                byte pb = (byte)((b * localAlpha) / 255);
                pixelBuf[py * width + px] = ((uint)localAlpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;
            }
        }

        // 6. Call UpdateLayeredWindow to composite the DIB onto the layered window.
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

        // 7. Cleanup GDI resources.
        PInvoke.SelectObject(memDC, oldBitmap);
        PInvoke.DeleteDC(memDC);
        PInvoke.DeleteObject((HGDIOBJ)hBitmap);
        PInvoke.ReleaseDC(HWND.Null, screenDC);
    }

    /// <summary>
    /// Returns the opacity [0.0, 1.0] for pixel (px, py) in a full-perimeter rounded-rect border.
    ///
    /// A pixel is "on the border" if:
    ///   1. It is within <paramref name="thickness"/> pixels of any edge AND not in a corner cutout, OR
    ///   2. It lies on a corner arc (within the rounded corner region).
    ///
    /// Corner cutout: if a pixel is within <paramref name="radius"/> of a corner but outside the arc,
    /// it is transparent (creates the rounded-corner effect).
    /// </summary>
    private static float GetFullBorderAlpha(int px, int py, int w, int h, int thickness, int radius)
    {
        // Determine which corner region the pixel falls in (if any).
        bool inTL = px < radius && py < radius;
        bool inTR = px >= w - radius && py < radius;
        bool inBL = px < radius && py >= h - radius;
        bool inBR = px >= w - radius && py >= h - radius;

        if (inTL)
        {
            // Top-left: center of arc is at (radius, radius)
            float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - radius) * (py - radius));
            return (dist >= radius - thickness && dist <= radius) ? 1.0f : 0.0f;
        }

        if (inTR)
        {
            // Top-right: center of arc is at (w - 1 - radius, radius)
            int cx = w - 1 - radius;
            float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - radius) * (py - radius));
            return (dist >= radius - thickness && dist <= radius) ? 1.0f : 0.0f;
        }

        if (inBL)
        {
            // Bottom-left: center of arc is at (radius, h - 1 - radius)
            int cy = h - 1 - radius;
            float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - cy) * (py - cy));
            return (dist >= radius - thickness && dist <= radius) ? 1.0f : 0.0f;
        }

        if (inBR)
        {
            // Bottom-right: center of arc is at (w - 1 - radius, h - 1 - radius)
            int cx = w - 1 - radius;
            int cy = h - 1 - radius;
            float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            return (dist >= radius - thickness && dist <= radius) ? 1.0f : 0.0f;
        }

        // Non-corner region: lit if within thickness of any edge.
        if (px < thickness || px >= w - thickness || py < thickness || py >= h - thickness)
            return 1.0f;

        return 0.0f;
    }

    /// <summary>
    /// Returns the opacity [0.0, 1.0] for pixel (px, py) in a DIB of size (w, h),
    /// given the navigation direction and rendering parameters.
    ///
    /// Returns 0.0 for transparent pixels, 1.0 for fully opaque primary edge/corner pixels,
    /// and a gradient value in (0.0, 1.0) for fade-tail pixels.
    ///
    /// Rendering logic per direction:
    ///   Left  — left edge + TL/BL corners + 20% fade on top-left / bottom-left
    ///   Right — right edge + TR/BR corners + 20% fade on top-right / bottom-right
    ///   Up    — top edge + TL/TR corners + 20% fade on left-top / right-top
    ///   Down  — bottom edge + BL/BR corners + 20% fade on left-bottom / right-bottom
    /// </summary>
    private static float GetPixelAlpha(
        int px, int py, int w, int h,
        Direction dir, int thickness, int radius, int fadeLenH, int fadeLenV)
    {
        switch (dir)
        {
            case Direction.Left:
            {
                // Primary edge: left strip
                if (px < thickness)
                    return 1.0f;

                // Top-left corner arc: quadrant x in [0, radius], y in [0, radius]
                if (px <= radius && py <= radius)
                {
                    float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - radius) * (py - radius));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Bottom-left corner arc: quadrant x in [0, radius], y in [h-1-radius, h-1]
                if (px <= radius && py >= h - 1 - radius)
                {
                    int cy = h - 1 - radius;
                    float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - cy) * (py - cy));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Top fade tail: top strip, left portion
                if (py < thickness && px < fadeLenH)
                {
                    // Skip if already covered by corner arc (handled above, but corner returns early)
                    float alpha = 1.0f - (float)px / fadeLenH;
                    if (alpha > 0f) return alpha;
                }

                // Bottom fade tail: bottom strip, left portion
                if (py >= h - thickness && px < fadeLenH)
                {
                    float alpha = 1.0f - (float)px / fadeLenH;
                    if (alpha > 0f) return alpha;
                }

                return 0.0f;
            }

            case Direction.Right:
            {
                // Primary edge: right strip
                if (px >= w - thickness)
                    return 1.0f;

                // Top-right corner arc: quadrant x in [w-1-radius, w-1], y in [0, radius]
                if (px >= w - 1 - radius && py <= radius)
                {
                    int cx = w - 1 - radius;
                    float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - radius) * (py - radius));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Bottom-right corner arc: quadrant x in [w-1-radius, w-1], y in [h-1-radius, h-1]
                if (px >= w - 1 - radius && py >= h - 1 - radius)
                {
                    int cx = w - 1 - radius;
                    int cy = h - 1 - radius;
                    float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Top fade tail: top strip, right portion
                if (py < thickness && px >= w - fadeLenH)
                {
                    float alpha = 1.0f - (float)(w - 1 - px) / fadeLenH;
                    if (alpha > 0f) return alpha;
                }

                // Bottom fade tail: bottom strip, right portion
                if (py >= h - thickness && px >= w - fadeLenH)
                {
                    float alpha = 1.0f - (float)(w - 1 - px) / fadeLenH;
                    if (alpha > 0f) return alpha;
                }

                return 0.0f;
            }

            case Direction.Up:
            {
                // Primary edge: top strip
                if (py < thickness)
                    return 1.0f;

                // Top-left corner arc: quadrant x in [0, radius], y in [0, radius]
                if (px <= radius && py <= radius)
                {
                    float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - radius) * (py - radius));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Top-right corner arc: quadrant x in [w-1-radius, w-1], y in [0, radius]
                if (px >= w - 1 - radius && py <= radius)
                {
                    int cx = w - 1 - radius;
                    float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - radius) * (py - radius));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Left fade tail: left strip, top portion
                if (px < thickness && py < fadeLenV)
                {
                    float alpha = 1.0f - (float)py / fadeLenV;
                    if (alpha > 0f) return alpha;
                }

                // Right fade tail: right strip, top portion
                if (px >= w - thickness && py < fadeLenV)
                {
                    float alpha = 1.0f - (float)py / fadeLenV;
                    if (alpha > 0f) return alpha;
                }

                return 0.0f;
            }

            case Direction.Down:
            {
                // Primary edge: bottom strip
                if (py >= h - thickness)
                    return 1.0f;

                // Bottom-left corner arc: quadrant x in [0, radius], y in [h-1-radius, h-1]
                if (px <= radius && py >= h - 1 - radius)
                {
                    int cy = h - 1 - radius;
                    float dist = MathF.Sqrt((px - radius) * (px - radius) + (py - cy) * (py - cy));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Bottom-right corner arc: quadrant x in [w-1-radius, w-1], y in [h-1-radius, h-1]
                if (px >= w - 1 - radius && py >= h - 1 - radius)
                {
                    int cx = w - 1 - radius;
                    int cy = h - 1 - radius;
                    float dist = MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    bool onArc = dist >= radius - thickness / 2.0f && dist <= radius + thickness / 2.0f;
                    if (onArc) return 1.0f;
                }

                // Left fade tail: left strip, bottom portion
                if (px < thickness && py >= h - fadeLenV)
                {
                    float alpha = 1.0f - (float)(h - 1 - py) / fadeLenV;
                    if (alpha > 0f) return alpha;
                }

                // Right fade tail: right strip, bottom portion
                if (px >= w - thickness && py >= h - fadeLenV)
                {
                    float alpha = 1.0f - (float)(h - 1 - py) / fadeLenV;
                    if (alpha > 0f) return alpha;
                }

                return 0.0f;
            }

            default:
                return 0.0f;
        }
    }
}
