---
phase: quick-1
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - focus/Windows/Daemon/Overlay/OverlayManager.cs
  - focus/Windows/Daemon/Overlay/BorderRenderer.cs
  - focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
autonomous: false
requirements: [QUICK-1]
must_haves:
  truths:
    - "When user holds CAPSLOCK, a white border appears around the currently focused window"
    - "The white border is visible simultaneously with the directional colored overlays"
    - "When CAPSLOCK is released, the white border disappears along with directional overlays"
    - "When foreground changes while CAPSLOCK held, white border follows the new foreground window"
    - "Solo-window case still works (dim indicator on foreground, but now also gets white border)"
  artifacts:
    - path: "focus/Windows/Daemon/Overlay/BorderRenderer.cs"
      provides: "PaintFullBorder static method for all-edges rendering"
      contains: "PaintFullBorder"
    - path: "focus/Windows/Daemon/Overlay/OverlayManager.cs"
      provides: "5th OverlayWindow for foreground/active window border"
      contains: "_foregroundWindow"
    - path: "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs"
      provides: "Foreground border show/hide wiring"
      contains: "ShowForegroundBorder"
  key_links:
    - from: "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs"
      to: "focus/Windows/Daemon/Overlay/OverlayManager.cs"
      via: "ShowForegroundOverlay / HideForegroundOverlay calls"
      pattern: "_overlayManager\\.ShowForegroundOverlay"
    - from: "focus/Windows/Daemon/Overlay/OverlayManager.cs"
      to: "focus/Windows/Daemon/Overlay/BorderRenderer.cs"
      via: "PaintFullBorder for all-edge rendering"
      pattern: "BorderRenderer\\.PaintFullBorder"
---

<objective>
Add a white border around the currently active (foreground) window whenever the user holds CAPSLOCK.

Purpose: Provide visual confirmation of which window is currently focused, complementing the existing directional overlays that show navigation targets.

Output: Modified overlay system with a 5th overlay window dedicated to highlighting the foreground window with a white full-perimeter border.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@focus/Windows/Daemon/Overlay/BorderRenderer.cs
@focus/Windows/Daemon/Overlay/OverlayManager.cs
@focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
@focus/Windows/Daemon/Overlay/OverlayWindow.cs
@focus/Windows/Daemon/Overlay/IOverlayRenderer.cs
@focus/Windows/Daemon/Overlay/OverlayColors.cs

<interfaces>
<!-- Key types and contracts the executor needs -->

From focus/Windows/Daemon/Overlay/IOverlayRenderer.cs:
```csharp
internal interface IOverlayRenderer
{
    string Name { get; }
    void Paint(HWND hwnd, RECT bounds, uint argbColor, Direction direction);
}
```

From focus/Windows/Daemon/Overlay/OverlayManager.cs:
```csharp
internal sealed class OverlayManager : IDisposable
{
    // Currently has 4 OverlayWindows keyed by Direction
    public void ShowOverlay(Direction direction, RECT bounds);
    public void ShowOverlay(Direction direction, RECT bounds, uint colorOverride);
    public void HideOverlay(Direction direction);
    public void HideAll();
}
```

From focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs:
```csharp
// ShowOverlaysForCurrentForeground() is the core method that:
// 1. Enumerates windows
// 2. Scores candidates per direction
// 3. Shows directional overlays on targets
// 4. Handles solo-window dim indicator
// This is where foreground border rendering must be added.

// HideAll path: OnReleasedSta() calls _overlayManager.HideAll()
// The foreground overlay must also be hidden here.
```

From focus/Windows/Daemon/Overlay/BorderRenderer.cs:
```csharp
internal sealed class BorderRenderer : IOverlayRenderer
{
    private const int BorderThickness = 2;
    private const int CornerRadius = 8;
    // GetPixelAlpha renders a SINGLE edge per direction with fade tails.
    // For the foreground border, we need ALL 4 edges rendered on one overlay.
}
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add full-perimeter border rendering and foreground overlay window</name>
  <files>
    focus/Windows/Daemon/Overlay/BorderRenderer.cs
    focus/Windows/Daemon/Overlay/OverlayManager.cs
    focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
  </files>
  <action>
Three files need coordinated changes:

**1. BorderRenderer.cs — Add `PaintFullBorder` static method**

Add a new public static method `PaintFullBorder(HWND hwnd, RECT bounds, uint argbColor)` that renders all 4 edges + all 4 corner arcs on a single overlay window. This is distinct from the existing `Paint()` which renders only one directional edge with fade tails.

Implementation approach:
- Copy the same DIB-section + UpdateLayeredWindow pattern from the existing `Paint()` method.
- In the pixel loop, use a new `GetFullBorderAlpha(px, py, w, h, thickness, radius)` private static method that returns 1.0f if the pixel is within `BorderThickness` of ANY edge (top, bottom, left, right) OR on any of the 4 corner arcs. Return 0.0f otherwise. No fade tails needed — all 4 edges are full opacity.
- Use the same `BorderThickness` (2) and `CornerRadius` (8) constants.
- The color should be white with high opacity: `0xE0FFFFFF` (224/255 = ~88% opacity white). However, accept the color as a parameter so the caller controls it.

The `GetFullBorderAlpha` logic:
- If `px < thickness` (left edge) OR `px >= w - thickness` (right edge) OR `py < thickness` (top edge) OR `py >= h - thickness` (bottom edge): return 1.0f
- For corners: check all 4 quadrants (TL, TR, BL, BR) using the same arc distance formula as existing code. If pixel is on any arc, return 1.0f.
- Otherwise return 0.0f.

Actually, the corners are already covered by the edge strips (since any pixel within `thickness` of an edge is lit). The corner arcs in the existing code exist to connect the primary edge to the fade tails — but for a full perimeter border with all 4 edges, the edges themselves form the complete rectangle. The corner radius effect makes the corners rounded rather than square. So the approach is:

For the full border, render a rounded rectangle outline:
- Compute distance from pixel to the nearest edge of the bounding rect
- For corner pixels (within `radius` of a corner): check if pixel is on the rounded corner arc (distance from corner center is between `radius - thickness/2` and `radius + thickness/2`)
- For non-corner edge pixels: check if pixel is within `thickness` of any edge
- Skip pixels that fall inside the rounded corner cutout (outside the arc but inside the corner region)

Simplest correct approach: For each pixel, check if it's part of a rounded-rect outline with the given thickness and radius. The pixel is "on the border" if:
1. It's within `thickness` of an edge AND not in a corner cutout region, OR
2. It's on a corner arc

Corner cutout: A pixel at (px, py) is in the TL corner region if `px < radius && py < radius`. In that region, the pixel is on the border only if it's on the arc (distance from (radius, radius) is in [radius-thickness, radius]).

This matches the existing pattern. Write it as a clean helper.

**2. OverlayManager.cs — Add 5th foreground overlay window**

- Add a private field `private readonly OverlayWindow _foregroundWindow;` initialized in the constructor alongside the 4 directional windows.
- Add `ShowForegroundOverlay(RECT bounds, uint argbColor)` public method:
  - Calls `_foregroundWindow.Reposition(bounds)`
  - Calls `BorderRenderer.PaintFullBorder(_foregroundWindow.Hwnd, bounds, argbColor)`
  - Calls `_foregroundWindow.Show()`
- Add `HideForegroundOverlay()` public method:
  - Calls `_foregroundWindow.Hide()`
- Update `HideAll()` to also call `_foregroundWindow.Hide()`
- Update `Dispose()` to also call `_foregroundWindow.Dispose()`

Note: `ShowForegroundOverlay` calls `BorderRenderer.PaintFullBorder` directly (static method) rather than going through `IOverlayRenderer.Paint`, since the full-border rendering is a different contract (no direction parameter, all edges).

**3. OverlayOrchestrator.cs — Wire foreground border into overlay lifecycle**

- Add a constant: `private const uint ForegroundBorderColor = 0xE0FFFFFF;` (white, ~88% opacity — visible but not harsh)
- In `ShowOverlaysForCurrentForeground()`, BEFORE the directional overlay loop, get the foreground window bounds using `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` (same pattern as the existing solo-window code at the bottom of the method). Show the foreground border:
  ```csharp
  var fgHwnd = PInvoke.GetForegroundWindow();
  if (fgHwnd != default)
  {
      RECT fgBounds = default;
      var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref fgBounds, 1));
      var hr = PInvoke.DwmGetWindowAttribute(fgHwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, boundsBytes);
      if (hr.Succeeded && (fgBounds.right - fgBounds.left) > 0)
      {
          _overlayManager.ShowForegroundOverlay(fgBounds, ForegroundBorderColor);
      }
  }
  ```
- In `OnReleasedSta()`, no change needed — `_overlayManager.HideAll()` already hides all windows including the new foreground window (after OverlayManager update).
- The foreground changed callback `OnForegroundChanged` already calls `ShowOverlaysForCurrentForeground()` which will re-render the foreground border at the new position.

**Important details:**
- The foreground border overlay must NOT interfere with click-through behavior (OverlayWindow already has WS_EX_TRANSPARENT).
- The foreground border should render at the same z-order as other overlays (HWND_TOPMOST, already handled by OverlayWindow).
- In the solo-window case, the foreground window gets BOTH the white border AND the dim directional indicators. This is fine — the white border is a thin perimeter outline while the dim indicators are directional edge markers.
  </action>
  <verify>
    Build succeeds: `dotnet build focus/focus.csproj`
  </verify>
  <done>
    - `BorderRenderer.PaintFullBorder` static method exists and renders all 4 edges + rounded corners
    - `OverlayManager` has a 5th `_foregroundWindow` with `ShowForegroundOverlay`/`HideForegroundOverlay` methods
    - `OverlayOrchestrator.ShowOverlaysForCurrentForeground()` shows white border on foreground window
    - `HideAll()` hides the foreground overlay too
    - Project compiles without errors
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 2: Verify white border on active window</name>
  <action>Human verifies the white border overlay works correctly.</action>
  <what-built>White border around the currently active window when holding CAPSLOCK. The border is a thin (2px) white rounded-rectangle outline that appears simultaneously with the existing directional colored overlays.</what-built>
  <how-to-verify>
    1. Build and run the daemon: `dotnet run --project focus -- daemon --verbose`
    2. Open 2-3 windows (e.g., Notepad, File Explorer, a browser)
    3. Hold CAPSLOCK
    4. Expected: The currently focused window should have a white border outline around it, AND the directional colored overlays should appear on the navigation target windows as before
    5. While still holding CAPSLOCK, press a direction key (e.g., Right arrow)
    6. Expected: Focus moves to the target window, white border should now appear around the newly focused window
    7. Release CAPSLOCK
    8. Expected: All overlays disappear (white border + directional overlays)
    9. Test with a single window open — should see both white border AND dim indicators
  </how-to-verify>
  <resume-signal>Type "approved" or describe issues</resume-signal>
</task>

</tasks>

<verification>
- `dotnet build focus/focus.csproj` compiles without errors
- Visual verification: white border visible on foreground window when CAPSLOCK held
- White border disappears on CAPSLOCK release
- White border follows foreground changes while CAPSLOCK held
</verification>

<success_criteria>
- A white full-perimeter border is drawn around the currently active window whenever CAPSLOCK is held
- The border coexists with the existing directional navigation overlays
- The border tracks foreground window changes while CAPSLOCK is held
- All overlays (including the new border) hide when CAPSLOCK is released
- No regressions to existing overlay behavior
</success_criteria>

<output>
After completion, create `.planning/quick/1-add-a-white-border-around-the-currently-/1-SUMMARY.md`
</output>
