---
phase: quick-7
plan: 1
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/Overlay/IOverlayRenderer.cs
  - focus/Windows/Daemon/Overlay/BorderRenderer.cs
  - focus/Windows/Daemon/Overlay/OverlayManager.cs
autonomous: true
requirements: []
must_haves:
  truths:
    - "LEFT overlay draws only left edge + TL/BL corners + 20% fade on top/bottom left portions"
    - "RIGHT overlay draws only right edge + TR/BR corners + 20% fade on top/bottom right portions"
    - "UP overlay draws only top edge + TL/TR corners + 20% fade on left/right top portions"
    - "DOWN overlay draws only bottom edge + BL/BR corners + 20% fade on left/right bottom portions"
    - "Fade-out portions transition from full opacity to transparent"
    - "IOverlayRenderer.Paint accepts Direction parameter"
  artifacts:
    - path: "focus/Windows/Daemon/Overlay/IOverlayRenderer.cs"
      provides: "Updated Paint signature with Direction parameter"
      contains: "Direction direction"
    - path: "focus/Windows/Daemon/Overlay/BorderRenderer.cs"
      provides: "Directional edge rendering with corner fade-out"
      contains: "Direction direction"
    - path: "focus/Windows/Daemon/Overlay/OverlayManager.cs"
      provides: "Passes direction through to renderer"
      contains: "Paint(window.Hwnd, bounds, _colors.GetArgb(direction), direction)"
  key_links:
    - from: "focus/Windows/Daemon/Overlay/OverlayManager.cs"
      to: "focus/Windows/Daemon/Overlay/IOverlayRenderer.cs"
      via: "_renderer.Paint call with direction"
      pattern: "_renderer\\.Paint\\(.*direction\\)"
    - from: "focus/Windows/Daemon/Overlay/BorderRenderer.cs"
      to: "focus/Windows/Direction.cs"
      via: "Direction enum for edge selection"
      pattern: "Direction\\.(Left|Right|Up|Down)"
---

<objective>
Change the overlay border renderer from drawing a full box outline to drawing only the edge
corresponding to the navigation direction, plus rounded corners and 20% fade-out tails on the
perpendicular edges.

Purpose: Directional edge overlays give the user a clear visual cue about which direction
navigation came from, rather than a generic box around the target window.

Output: Updated IOverlayRenderer interface, BorderRenderer implementation with directional
edge painting, and OverlayManager wiring.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Windows/Direction.cs
@focus/Windows/Daemon/Overlay/IOverlayRenderer.cs
@focus/Windows/Daemon/Overlay/BorderRenderer.cs
@focus/Windows/Daemon/Overlay/OverlayManager.cs
@focus/Windows/Daemon/Overlay/OverlayWindow.cs
@focus/Windows/Daemon/Overlay/OverlayColors.cs

<interfaces>
<!-- Current interface that needs Direction parameter added -->
From focus/Windows/Daemon/Overlay/IOverlayRenderer.cs:
```csharp
internal interface IOverlayRenderer
{
    string Name { get; }
    void Paint(HWND hwnd, RECT bounds, uint argbColor);
}
```

From focus/Windows/Direction.cs:
```csharp
internal enum Direction { Left, Right, Up, Down }
```

From focus/Windows/Daemon/Overlay/OverlayManager.cs (sole Paint caller):
```csharp
// Line 48 — the only call site for _renderer.Paint:
_renderer.Paint(window.Hwnd, bounds, _colors.GetArgb(direction));
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add Direction to IOverlayRenderer.Paint and update OverlayManager caller</name>
  <files>
    focus/Windows/Daemon/Overlay/IOverlayRenderer.cs
    focus/Windows/Daemon/Overlay/OverlayManager.cs
  </files>
  <action>
1. In IOverlayRenderer.cs, add `using Focus.Windows;` at top. Update the Paint signature to:
   ```csharp
   void Paint(HWND hwnd, RECT bounds, uint argbColor, Direction direction);
   ```
   Update the XML doc param list to include `direction` ("The navigation direction that targeted this window").

2. In OverlayManager.cs, update line 48 (the single Paint call site) to pass direction through:
   ```csharp
   _renderer.Paint(window.Hwnd, bounds, _colors.GetArgb(direction), direction);
   ```

These are small, mechanical changes. The build will fail until Task 2 updates BorderRenderer, which is expected.
  </action>
  <verify>
    Grep confirms: IOverlayRenderer.cs contains "Direction direction" in Paint signature,
    OverlayManager.cs passes direction as 4th arg to Paint.
  </verify>
  <done>
    IOverlayRenderer.Paint accepts Direction parameter. OverlayManager passes direction through.
  </done>
</task>

<task type="auto">
  <name>Task 2: Implement directional edge rendering with corner fade-out in BorderRenderer</name>
  <files>
    focus/Windows/Daemon/Overlay/BorderRenderer.cs
  </files>
  <action>
Update BorderRenderer.Paint to accept the new `Direction direction` parameter and replace the
full-box RoundRect approach with directional edge-only rendering.

**Approach:** Keep the existing DIB + premultiplied-alpha + UpdateLayeredWindow pipeline. Replace
GDI RoundRect (step 7 in current code) with direct pixel buffer writing. This gives full control
over which pixels to draw and their alpha values.

Add `using Focus.Windows;` at top.

Update Paint signature to: `public unsafe void Paint(HWND hwnd, RECT bounds, uint argbColor, Direction direction)`

Keep steps 1-6 (DIB creation, GDI setup) and steps 9-10 (UpdateLayeredWindow, cleanup) exactly
as they are. Replace steps 7-8 (RoundRect draw + premultiplied alpha loop) with direct pixel
buffer rendering:

**Constants:**
- `BorderThickness = 2` (keep existing)
- `CornerRadius = 8` (half of existing CornerEllipse=16, used for actual radius math)
- `FadeExtent = 0.20` (20% of perpendicular edge length for fade tails)

**Rendering strategy — write directly to the uint* pixel buffer:**

Remove the GDI pen/brush creation (steps 6-7) entirely since we won't use GDI drawing. Remove
the old premultiplied-alpha fixup loop (step 8) since we write premultiplied pixels directly.
Keep GdiFlush and NativeMemory.Clear. Also remove SelectObject/DeleteObject for pen/brush in
cleanup (step 10) since they are no longer created.

For a pixel at (x, y) in the DIB:
1. Determine if the pixel is on a drawn segment (primary edge, corner arc, or fade tail)
2. Calculate a local alpha factor (1.0 for primary edge/corners, gradient 1.0->0.0 for fade)
3. Write premultiplied ARGB: `(localAlpha * alpha) << 24 | (localAlpha * alpha * r / 255) << 16 | ...`

**Edge geometry per direction:**

Define a helper method `private static float GetPixelAlpha(int x, int y, int w, int h, Direction dir, int thickness, int radius, float fadeExtent)` that returns 0.0 (transparent) or a value in (0.0, 1.0] for how opaque that pixel should be.

Logic inside GetPixelAlpha:

For **Direction.Left**:
- Primary edge: x in [0, thickness), full height. Alpha = 1.0
- Top-left corner arc: pixels within `thickness` of the arc at center (radius, radius) with radius `radius`, only for x in [0, radius] and y in [0, radius]. Use distance-from-arc check: if pixel center is within `thickness/2` of the arc curve. Alpha = 1.0
- Bottom-left corner arc: same logic at center (radius, h - 1 - radius). Alpha = 1.0
- Top fade tail: y in [0, thickness), x in [0, fadeLen) where fadeLen = (int)(w * fadeExtent). Alpha = 1.0 - (x / fadeLen). But skip pixels already covered by primary edge or corner.
- Bottom fade tail: y in [h - thickness, h), x in [0, fadeLen). Alpha = 1.0 - (x / fadeLen). Skip already-covered pixels.
- Everything else: 0.0

For **Direction.Right**:
- Primary edge: x in [w - thickness, w), full height. Alpha = 1.0
- Top-right corner: arc at center (w - 1 - radius, radius)
- Bottom-right corner: arc at center (w - 1 - radius, h - 1 - radius)
- Top fade tail: y in [0, thickness), x in [w - fadeLen, w). Alpha = 1.0 - ((w - 1 - x) / fadeLen). Skip covered.
- Bottom fade tail: y in [h - thickness, h), x in [w - fadeLen, w). Alpha gradient same. Skip covered.

For **Direction.Up**:
- Primary edge: y in [0, thickness), full width. Alpha = 1.0
- Top-left corner: arc at center (radius, radius)
- Top-right corner: arc at center (w - 1 - radius, radius)
- Left fade tail: x in [0, thickness), y in [0, fadeLen) where fadeLen = (int)(h * fadeExtent). Alpha = 1.0 - (y / fadeLen). Skip covered.
- Right fade tail: x in [w - thickness, w), y in [0, fadeLen). Alpha = 1.0 - (y / fadeLen). Skip covered.

For **Direction.Down**:
- Primary edge: y in [h - thickness, h), full width. Alpha = 1.0
- Bottom-left corner: arc at center (radius, h - 1 - radius)
- Bottom-right corner: arc at center (w - 1 - radius, h - 1 - radius)
- Left fade tail: x in [0, thickness), y in [h - fadeLen, h). Alpha = 1.0 - ((h - 1 - y) / fadeLen). Skip covered.
- Right fade tail: x in [w - thickness, w), y in [h - fadeLen, h). Alpha gradient same. Skip covered.

**Corner arc detection:** A pixel (px, py) is "on the arc" of a corner with center (cx, cy) and
radius r if: `float dist = MathF.Sqrt((px - cx)^2 + (py - cy)^2); bool onArc = dist >= r - thickness/2.0 && dist <= r + thickness/2.0;`
Additionally, the pixel must be in the correct quadrant (e.g., top-left corner: px <= cx && py <= cy).

**Pixel writing loop** (replaces steps 7-8):
```csharp
PInvoke.GdiFlush();
uint* pixelBuf = (uint*)bits;
int fadeLen_h = Math.Max(1, (int)(width * FadeExtent));
int fadeLen_v = Math.Max(1, (int)(height * FadeExtent));

for (int py = 0; py < height; py++)
{
    for (int px = 0; px < width; px++)
    {
        float a = GetPixelAlpha(px, py, width, height, direction, BorderThickness, CornerRadius, FadeExtent);
        if (a <= 0f) continue;

        byte localAlpha = (byte)(a * alpha);
        byte pr = (byte)((r * localAlpha) / 255);
        byte pg = (byte)((g * localAlpha) / 255);
        byte pb = (byte)((b * localAlpha) / 255);
        pixelBuf[py * width + px] = ((uint)localAlpha << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;
    }
}
```

**GDI cleanup simplification:** Since we no longer create hPen or select hBrush, remove:
- The `CreatePen`, `GetStockObject(NULL_BRUSH)`, `SelectObject` calls for pen/brush (old steps 6-7)
- The `SelectObject` restore and `DeleteObject` for pen/brush in cleanup (old step 10)
- Keep: SelectObject(memDC, oldBitmap), DeleteDC, DeleteObject(hBitmap), ReleaseDC

Also remove `SetBkMode` call since we don't use GDI text/drawing.

**Important edge cases:**
- Very small windows (width or height < 2*CornerRadius): clamp fadeLen and corner radius to fit.
  Use `int effectiveRadius = Math.Min(CornerRadius, Math.Min(width / 2, height / 2));`
- Fade tail should not overlap with primary edge pixels. The GetPixelAlpha method should check
  primary edge first — if pixel is on the primary edge, return 1.0 immediately before checking fade.
- The corner arcs connect the primary edge to the fade tails. Corners are always drawn at full
  alpha (1.0) where they overlap with the border thickness band.

**Performance note:** The pixel loop iterates width*height pixels. For a typical 1920x1080 window
that is ~2M pixels. The float sqrt in corner detection should only be evaluated for pixels near
corners — add bounding box early-out: only check corner arc if pixel is within the corner's
quadrant rectangle (radius x radius area). For the vast majority of pixels, the check is just
2-3 integer comparisons.
  </action>
  <verify>
    <automated>cd C:/Work/windowfocusnavigation && dotnet build focus/focus.csproj --no-restore 2>&1 | tail -5</automated>
    Then manually test: `dotnet run --project focus -- --debug left` and `dotnet run --project focus -- --debug all`
    to visually confirm directional edges render correctly.
  </verify>
  <done>
    - `dotnet build` succeeds with zero errors
    - `--debug left` shows only the left edge, TL/BL corners, and fading 20% tails on top/bottom
    - `--debug right` shows only the right edge, TR/BR corners, and fading 20% tails
    - `--debug up` shows only the top edge, TL/TR corners, and fading 20% tails on left/right
    - `--debug down` shows only the bottom edge, BL/BR corners, and fading 20% tails
    - `--debug all` shows four different directional edges on four windows simultaneously
    - Fade tails visibly transition from opaque to transparent
  </done>
</task>

</tasks>

<verification>
1. `dotnet build focus/focus.csproj` compiles without errors
2. `dotnet run --project focus -- --debug left` — overlay visible only on left edge with corners and fade
3. `dotnet run --project focus -- --debug all` — all four directions show distinct directional edges
4. No regressions in existing overlay behavior (show/hide, click-through, non-focus-stealing)
</verification>

<success_criteria>
- IOverlayRenderer.Paint accepts Direction parameter
- BorderRenderer draws directional edges instead of full box
- Each direction renders: primary edge (full alpha), two relevant corners (full alpha), 20% perpendicular fade tails (gradient alpha)
- Build compiles, visual test confirms correct rendering
</success_criteria>

<output>
After completion, create `.planning/quick/7-directional-edge-overlay-with-corner-fad/7-SUMMARY.md`
</output>
