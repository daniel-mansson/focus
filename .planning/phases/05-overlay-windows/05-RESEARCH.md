# Phase 5: Overlay Windows - Research

**Researched:** 2026-03-01
**Domain:** Win32 layered window rendering, GDI per-pixel alpha, DPI-aware overlays
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **Border appearance:** 2-3px thickness, rounded corners to match Windows 11 window chrome, ~75% opacity (ARGB alpha channel), clean edges only — no glow, shadow, or bloom effects
- **Direction color palette:** Cool/warm spatial scheme: blue-left, red-right, green-up, yellow/orange-down; muted/pastel saturation; all four directions equal visual weight
- **Test mode invocation:** New debug command `focus --debug overlay <direction>` (fits existing --debug pattern); targets the current foreground window; shows one direction at a time; overlay stays visible until a keypress, then removes and exits

### Claude's Discretion

- Exact ARGB hex values for the four direction colors
- Corner radius value (should approximate Win11 window radius)
- IOverlayRenderer interface design and renderer selection mechanism
- Win32 window style flags (WS_EX_TRANSPARENT, WS_EX_TOOLWINDOW, etc.)
- GDI rendering approach for borders with rounded corners
- How to exclude overlay windows from the tool's own enumeration

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| OVERLAY-02 | Overlay windows are click-through, always-on-top, excluded from taskbar/Alt+Tab, and excluded from navigation enumeration | Window style flags section, enumeration exclusion section |
| RENDER-01 | IOverlayRenderer interface defines the contract for overlay rendering | Interface design section in Architecture Patterns |
| RENDER-02 | Default border renderer draws colored borders using Win32 GDI (no WPF/WinForms) | GDI rendering pattern section, Code Examples |
| RENDER-03 | Renderer selection is driven by config (overlayRenderer field) | Config extension section |
| CFG-05 | Per-direction overlay colors configurable in JSON config (left/right/up/down, hex ARGB) | Config extension section |
| CFG-07 | Overlay renderer name configurable in JSON config (default: "border") | Config extension section |
</phase_requirements>

## Summary

Phase 5 delivers transparent, click-through, always-on-top colored border overlays using the Win32 layered window API (`UpdateLayeredWindow` with premultiplied-alpha DIB). The window style combination `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` achieves all required properties simultaneously: click-through (WS_EX_TRANSPARENT), excluded from Alt+Tab and taskbar (WS_EX_TOOLWINDOW), non-focus-stealing (WS_EX_NOACTIVATE), always visible above application windows (WS_EX_TOPMOST), and transparent rendering (WS_EX_LAYERED).

Rendering colored borders with rounded corners requires GDI drawing into a 32bpp BGRA premultiplied-alpha DIB section. Standard GDI `RoundRect` requires selecting a hollow brush and the color pen into a compatible DC, drawing into the DIB surface, then calling `UpdateLayeredWindow` with `ULW_ALPHA` and the appropriate `BLENDFUNCTION`. The critical constraint is that per-pixel alpha layered windows (those using `UpdateLayeredWindow`) **cannot** receive DWM rounded corners — the rounding must be drawn by the renderer itself via GDI `RoundRect`. Windows 11's rounded window radius is approximately 8px (DWMWCP_ROUND); `RoundRect` ellipse parameters of approximately 16×16 are the correct match for the border-only overlay (no fill inside the ellipse).

The existing architecture integrates cleanly: overlay HWNDs are created once at daemon startup on the STA thread (which already runs the WinForms message pump), reused by show/hide/UpdateLayeredWindow calls, and registered with `RegisterClassEx` using `Marshal.GetHINSTANCE(typeof(T).Module)` — the known fix for error 87. The enumeration filter already excludes WS_EX_TOOLWINDOW windows, so overlay HWNDs will naturally not appear in navigation results.

**Primary recommendation:** Use `UpdateLayeredWindow` + premultiplied-alpha DIB + GDI `RoundRect` exclusively. Never mix `SetLayeredWindowAttributes` on the same HWND. Create overlay HWNDs once at startup and reuse them via `ShowWindow`/`UpdateLayeredWindow`. Always pass `SWP_NOACTIVATE` in every `SetWindowPos` call.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| CsWin32 | 0.3.269 (already in project) | P/Invoke for all Win32 APIs needed | Already established; generates RegisterClassEx, CreateWindowEx, UpdateLayeredWindow, CreateDIBSection, RoundRect, ShowWindow, SetWindowPos |
| System.Windows.Forms | net8.0-windows (already in project) | Existing STA message pump | Already running; overlay HWNDs created on same thread |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Text.Json | net8.0 built-in | Config deserialization for color fields | Extending existing FocusConfig for CFG-05, CFG-07 |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| GDI RoundRect into DIB | Direct2D | D2D requires additional COM initialization, larger code surface; GDI sufficient for a thin static border |
| GDI RoundRect into DIB | GDI+ (Graphics class) | GDI+ alpha blending is less predictable with UpdateLayeredWindow; GDI direct pixel manipulation is the reliable pattern |
| UpdateLayeredWindow | SetLayeredWindowAttributes + WM_PAINT | SetLayeredWindowAttributes cannot do per-pixel alpha — only uniform opacity; UpdateLayeredWindow is required for correct rendering |

**Installation:** No new packages required. All needed APIs added to NativeMethods.txt.

## Architecture Patterns

### Recommended Project Structure

```
focus/Windows/
├── Daemon/
│   ├── DaemonCommand.cs       # existing — passes overlay manager to ApplicationContext
│   ├── DaemonApplicationContext.cs  # existing — creates OverlayManager on STA thread
│   ├── CapsLockMonitor.cs     # existing — Phase 6 will call overlay show/hide
│   ├── KeyboardHookHandler.cs # existing
│   ├── KeyEvent.cs            # existing
│   ├── DaemonMutex.cs         # existing
│   ├── TrayIcon.cs            # existing
│   └── Overlay/               # NEW in Phase 5
│       ├── IOverlayRenderer.cs     # RENDER-01: interface contract
│       ├── OverlayWindow.cs        # single layered HWND lifecycle (create/show/hide/paint)
│       ├── OverlayManager.cs       # manages 4 OverlayWindow instances (one per direction)
│       ├── BorderRenderer.cs       # RENDER-02: GDI RoundRect border renderer
│       └── OverlayColors.cs        # default color constants + config deserialization helper
```

### Pattern 1: Win32 Layered Window Creation

**What:** Register a window class, create a borderless top-level window with layered+transparent styles, then call `UpdateLayeredWindow` to paint content.

**When to use:** Any time a transparent, click-through, always-on-top overlay is needed.

**Window styles for creation:**

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowexa

// Extended styles — all required for correct overlay behavior:
const uint WS_EX_LAYERED    = 0x00080000;  // Enables UpdateLayeredWindow
const uint WS_EX_TRANSPARENT = 0x00000020;  // Click-through (mouse events pass to underlying window)
const uint WS_EX_TOOLWINDOW = 0x00000080;  // Excludes from Alt+Tab and taskbar
const uint WS_EX_NOACTIVATE = 0x08000000;  // Never steals focus when shown
const uint WS_EX_TOPMOST    = 0x00000008;  // Always above normal windows

// Base style — borderless popup (no caption, no system menu, no border)
const uint WS_POPUP = 0x80000000;

// Combined exStyle for CreateWindowEx:
uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
```

**HINSTANCE for RegisterClassEx:**

```csharp
// Source: https://github.com/microsoft/CsWin32/discussions/750
// DO NOT use new HINSTANCE(0) — causes error 87
// DO NOT use PInvoke.GetModuleHandle(someString) — wrong module
var hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(OverlayWindow).Module));
```

**WndProc for layered windows:**

```csharp
// CRITICAL: Always handle WM_PAINT in layered window WndProc, even if empty.
// If WM_PAINT is not handled, Windows re-queues it infinitely → 100% CPU.
protected override void WndProc(ref Message m)
{
    if (m.Msg == WM_PAINT)
    {
        // UpdateLayeredWindow manages all painting — WM_PAINT is not used.
        // Call Begin/EndPaint to validate the region and stop re-queuing.
        PInvoke.BeginPaint(hwnd, out var ps);
        PInvoke.EndPaint(hwnd, ps);
        return;
    }
    base.WndProc(ref m);
}
```

### Pattern 2: Per-Pixel Alpha DIB Rendering with UpdateLayeredWindow

**What:** Create a 32bpp BGRA DIB section, draw premultiplied-alpha pixels into it, call UpdateLayeredWindow.

**When to use:** Any time per-pixel transparency is needed. Required here for the semi-transparent colored border.

**Premultiplied alpha rule:** Every RGB channel value must be pre-multiplied by alpha/255 before writing to the DIB. Raw alpha compositing without premultiplication produces incorrect dark fringing.

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createdibsection
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow

// Step 1: Create 32bpp BGRA DIB section
var bmi = new BITMAPINFO();
bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
bmi.bmiHeader.biWidth = width;
bmi.bmiHeader.biHeight = -height;   // Negative = top-down DIB
bmi.bmiHeader.biPlanes = 1;
bmi.bmiHeader.biBitCount = 32;      // 32bpp BGRA
bmi.bmiHeader.biCompression = BI_RGB;  // BI_RGB with 32bpp gives us the 4th byte as alpha

void* bits;
var hBitmap = PInvoke.CreateDIBSection(
    screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, null, 0);

// Step 2: Create compatible DC, select bitmap
var memDC = PInvoke.CreateCompatibleDC(screenDC);
var oldBitmap = PInvoke.SelectObject(memDC, hBitmap);

// Step 3: Draw into DIB via GDI (transparent background first)
//   GdiFlush MUST be called before writing pixels directly to bits ptr
PInvoke.GdiFlush();

// Option A: Write pixels directly (for simple shapes):
// Pixel format = BGRA, premultiplied alpha
// For each pixel: B=blue*alpha/255, G=green*alpha/255, R=red*alpha/255, A=alpha
uint* pixelBuf = (uint*)bits;
// ... fill buffer ...

// Option B: Use GDI RoundRect for border (see Pattern 3)

// Step 4: Call UpdateLayeredWindow
var blendFunc = new BLENDFUNCTION
{
    BlendOp = AC_SRC_OVER,   // = 0
    BlendFlags = 0,
    SourceConstantAlpha = 255,  // Per-pixel alpha used; constant = 255 (full)
    AlphaFormat = AC_SRC_ALPHA  // = 1, enables per-pixel alpha
};

var ptDst = new POINT { x = windowLeft, y = windowTop };
var sizeSrc = new SIZE { cx = width, cy = height };
var ptSrc = new POINT { x = 0, y = 0 };

PInvoke.UpdateLayeredWindow(
    hwnd,
    screenDC,   // hdcDst = GetDC(null)
    &ptDst,     // new position on screen
    &sizeSrc,   // new size
    memDC,      // source DC with our DIB
    &ptSrc,     // source origin = (0,0)
    0,          // crKey (unused with ULW_ALPHA)
    &blendFunc,
    ULW_ALPHA); // use per-pixel alpha from BLENDFUNCTION

// Step 5: Cleanup DC resources (bitmap stays alive for reuse)
PInvoke.SelectObject(memDC, oldBitmap);
PInvoke.DeleteDC(memDC);
PInvoke.ReleaseDC(default, screenDC);
```

**CRITICAL:** Do NOT call SetLayeredWindowAttributes AND UpdateLayeredWindow on the same HWND. They are mutually exclusive modes.

### Pattern 3: GDI RoundRect Border (No Fill) into DIB

**What:** Draw a rounded-corner rectangle outline using GDI into the DIB, with transparent interior. This is the rendering approach for the border overlay.

**Windows 11 corner radius:** DWM uses approximately 8px physical-pixel radius for normal windows (DWMWCP_ROUND). For GDI `RoundRect`, the ellipse width and height parameters are the full ellipse dimensions (diameter), so use 16×16 to achieve ~8px radius corners.

**Key insight about layered windows and DWM rounding:** Per-pixel alpha layered windows (those using `UpdateLayeredWindow`) are explicitly listed by Microsoft as windows that "cannot ever be rounded" by DWM, even if `DwmSetWindowAttribute` is called. The renderer must draw rounded corners itself.

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-roundrect
// Source: https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners

// After creating DIB and selecting into memDC:

// Clear DIB to fully transparent first
// (GDI doesn't know about alpha — we zero the buffer manually)
PInvoke.GdiFlush();
NativeMemory.Clear(bits, (nuint)(width * height * 4));

// Set DC to not draw background (transparent interior)
PInvoke.SetBkMode(memDC, BACKGROUND_MODE.TRANSPARENT);

// Create pen with premultiplied alpha color
// COLORREF is BGRA in DIB memory, but GDI uses 0x00RRGGBB (no alpha in COLORREF)
// For GDI pen: color = COLORREF with the visual RGB; alpha goes through the DIB pixel values
// Since GDI ignores alpha, we paint opaque pixels then fix alpha in the pixel buffer afterwards,
// OR we use a null brush with a colored pen for the border outline only.
var hPen = PInvoke.CreatePen(PEN_STYLE.PS_SOLID, borderThickness, colorRef);
var hBrush = PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.NULL_BRUSH);  // Transparent interior
var oldPen = PInvoke.SelectObject(memDC, hPen);
var oldBrush = PInvoke.SelectObject(memDC, hBrush);

// Draw rounded rectangle (border only, hollow interior)
// RoundRect(hdc, left, top, right, bottom, ellipseWidth, ellipseHeight)
// Ellipse 16x16 → corner radius ~8px (matches Win11 DWMWCP_ROUND)
PInvoke.RoundRect(memDC, 0, 0, width, height, 16, 16);

// GDI draws with full alpha (0xFF) in all pixels it touches.
// We need semi-transparent pixels. After GDI draws, fix alpha channel:
PInvoke.GdiFlush();
uint* pixelBuf = (uint*)bits;
byte targetAlpha = 191; // ~75% = 0xBF
for (int i = 0; i < width * height; i++)
{
    uint pixel = pixelBuf[i];
    byte a = (byte)(pixel >> 24);
    if (a != 0)  // GDI-drawn pixel (fully opaque)
    {
        // Apply target alpha + premultiply RGB channels
        byte r = (byte)(((pixel >> 16) & 0xFF) * targetAlpha / 255);
        byte g = (byte)(((pixel >> 8) & 0xFF) * targetAlpha / 255);
        byte b = (byte)((pixel & 0xFF) * targetAlpha / 255);
        pixelBuf[i] = ((uint)targetAlpha << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}

// Cleanup GDI objects
PInvoke.SelectObject(memDC, oldPen);
PInvoke.SelectObject(memDC, oldBrush);
PInvoke.DeleteObject(hPen);
// NULL_BRUSH is a stock object — do NOT call DeleteObject on it
```

### Pattern 4: HWND Reuse via ShowWindow + UpdateLayeredWindow

**What:** Create overlay HWNDs once at daemon startup. Reuse by calling `ShowWindow` + `UpdateLayeredWindow` to position and paint them. Never create/destroy per CAPSLOCK cycle.

**When to use:** Always — creation/destruction of windows per keypress introduces visible latency and GDI resource churn.

```csharp
// Show and position the overlay window
// CRITICAL: Use SWP_NOACTIVATE on every SetWindowPos call
PInvoke.SetWindowPos(
    hwnd,
    HWND_TOPMOST,     // Always-on-top Z-order
    x, y, width, height,
    SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

// Then call UpdateLayeredWindow to paint content
// (See Pattern 2 above)

// To hide:
PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
```

### Pattern 5: IOverlayRenderer Interface Design

**What:** Defines the contract for rendering implementations (RENDER-01).

```csharp
namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Contract for overlay rendering implementations.
/// Implementations receive a target window's bounds and a direction color,
/// and are responsible for painting the overlay HWND.
/// </summary>
internal interface IOverlayRenderer
{
    /// <summary>
    /// Paints the overlay for the given bounds and color.
    /// Called on the STA thread. Must not block.
    /// </summary>
    /// <param name="hwnd">The overlay window handle to paint.</param>
    /// <param name="bounds">Target window bounds in screen coordinates (physical pixels).</param>
    /// <param name="argbColor">Color in ARGB hex format (e.g., 0xBF0080FF for semi-transparent blue).</param>
    void Paint(nint hwnd, RECT bounds, uint argbColor);

    /// <summary>
    /// Name used in config for renderer selection (RENDER-03, CFG-07).
    /// </summary>
    string Name { get; }
}
```

### Pattern 6: Config Extension for CFG-05 and CFG-07

**What:** Extend `FocusConfig` to add overlay color and renderer fields.

```csharp
// In FocusConfig.cs — add new fields:
public OverlayColors OverlayColors { get; set; } = OverlayColors.Default;
public string OverlayRenderer { get; set; } = "border";

// New OverlayColors class:
internal class OverlayColors
{
    // Muted cool/warm spatial: blue-left, red-right, green-up, yellow-down
    // Format: ARGB (alpha, red, green, blue) — ~75% opacity = 0xBF alpha
    public string Left  { get; set; } = "#BF4488CC";  // muted blue
    public string Right { get; set; } = "#BFCC4444";  // muted red
    public string Up    { get; set; } = "#BF44AA66";  // muted green
    public string Down  { get; set; } = "#BFCCAA33";  // muted yellow-orange

    public static OverlayColors Default => new();

    // Parse ARGB hex string to uint for renderer consumption
    public uint GetArgb(Direction direction) { /* ... */ }
}
```

**Color rationale (Claude's Discretion):**
- Alpha: 0xBF = 191 = ~75% opacity
- Left (blue): `#BF4488CC` — cool, desaturated sky blue
- Right (red): `#BFCC4444` — warm, desaturated brick red
- Up (green): `#BF44AA66` — natural, desaturated sage green
- Down (yellow/orange): `#BFCCAA33` — warm, desaturated amber

**Corner radius (Claude's Discretion):** Use ellipse 16×16 for `RoundRect` to produce an ~8px corner radius, matching Windows 11's standard window rounding.

### Pattern 7: Debug Command `focus --debug overlay <direction>`

**What:** Standalone test without the daemon. Gets foreground window, creates one OverlayWindow, paints it, waits for keypress, removes overlay.

```csharp
// In Program.cs — extend the --debug handler:
if (debugValue == "overlay")
{
    var overlayDirection = DirectionParser.Parse(directionValue);
    if (overlayDirection is null)
    {
        Console.Error.WriteLine("Usage: focus --debug overlay <direction>");
        return 2;
    }

    var config = FocusConfig.Load();
    var renderer = RendererFactory.Create(config.OverlayRenderer);
    var fgHwnd = PInvoke.GetForegroundWindow();
    // Get DWMWA_EXTENDED_FRAME_BOUNDS of foreground window
    // Create OverlayWindow, call Paint, wait for Console.ReadKey(), then Dispose
}
```

### Anti-Patterns to Avoid

- **Mixing SetLayeredWindowAttributes with UpdateLayeredWindow on the same HWND:** Mutually exclusive — Windows will fail or produce incorrect rendering
- **Allocating new HWND per CAPSLOCK press:** Creates visible flash and GDI resource leak; always reuse HWNDs from startup
- **Creating overlay HWNDs on the wrong thread:** Win32 windows must be created and owned by the thread running their message pump; in this project that is the STA thread running `Application.Run`
- **Not handling WM_PAINT in the layered window WndProc:** Causes infinite WM_PAINT loop and 100% CPU utilization
- **Using WS_EX_TRANSPARENT alone for transparency:** WS_EX_TRANSPARENT creates a "sibling-transparent" window; actual visual transparency requires WS_EX_LAYERED + UpdateLayeredWindow
- **Using HINSTANCE(0) in RegisterClassEx:** Causes error 87; must use `Marshal.GetHINSTANCE(typeof(T).Module)`
- **Not premultiplying alpha:** Causes dark fringing on borders; every RGB channel must be multiplied by alpha/255 before writing to the DIB
- **Calling GDI functions after writing to DIB pixel pointer without GdiFlush:** Race condition; always call GdiFlush before direct pixel writes

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Window class registration | Custom ATOM management | CsWin32 RegisterClassEx pattern | Edge case: hInstance must be Marshal.GetHINSTANCE; CsWin32 generates correct types |
| Premultiplied alpha conversion | Custom math | Inline: `R*A/255` after GDI draw | Simple 1-liner; no library needed but must not be skipped |
| Per-pixel DIB setup | Custom bitmap format | CreateDIBSection with BI_RGB 32bpp top-down | BI_RGB + 32bpp is the correct pattern; any other format will not work with UpdateLayeredWindow |
| Color parsing from hex string | Custom parser | `uint.Parse(hex, NumberStyles.HexNumber)` | Trivial one-liner; no special library needed |
| WinForms message pump | Custom GetMessage loop | Existing Application.Run on STA thread | Already running in Phase 4; overlay HWNDs created on that thread participate automatically |

**Key insight:** The entire rendering pipeline is 3 Win32 calls: `CreateDIBSection`, `RoundRect` (or direct pixel write), `UpdateLayeredWindow`. Complexity lives in premultiplied alpha math and GDI object lifecycle, not in the overall structure.

## Common Pitfalls

### Pitfall 1: Premultiplied Alpha Failure (Silent Corruption)
**What goes wrong:** Pixels appear darker than intended; semi-transparent areas show black fringing.
**Why it happens:** `UpdateLayeredWindow` with `AC_SRC_ALPHA` requires premultiplied alpha. Unpremultiplied pixels (alpha=127, R=255, G=0, B=0) display as darker than they should (they look like alpha=127, R=127, G=0, B=0 because DWM expects them premultiplied).
**How to avoid:** After every GDI draw, iterate the pixel buffer and multiply R, G, B by A/255 before calling UpdateLayeredWindow. The STATE.md already flags this: "a rendering spike against a known ARGB value is recommended before full OverlayWindow implementation."
**Warning signs:** Colored border appears darker than expected ARGB value; edges look "muddy."

### Pitfall 2: HWND Created on Wrong Thread
**What goes wrong:** Window fails to receive messages; UpdateLayeredWindow on wrong thread silently fails or deadlocks.
**Why it happens:** Win32 requires that the message pump (GetMessage/DispatchMessage) runs on the same thread that created the window. Overlay HWNDs created on main thread cannot be pumped by the STA thread.
**How to avoid:** Create all overlay HWNDs inside the `DaemonApplicationContext` constructor or from a method called on the STA thread. Never create them from the worker thread or main thread.
**Warning signs:** `UpdateLayeredWindow` returns false; overlay never appears.

### Pitfall 3: WM_PAINT Infinite Loop
**What goes wrong:** Process reaches 100% CPU immediately after overlay window is created.
**Why it happens:** Layered windows that use `UpdateLayeredWindow` do not process `WM_PAINT` through GDI. If the WndProc does not call `BeginPaint`/`EndPaint` when handling `WM_PAINT`, Windows keeps re-queuing it.
**How to avoid:** Always handle `WM_PAINT` in any custom WndProc: call `BeginPaint` and `EndPaint` with nothing between them.
**Warning signs:** CPU spikes to 100% as soon as overlay window becomes visible.

### Pitfall 4: Focus Steal on ShowWindow
**What goes wrong:** Foreground application loses focus when overlay appears.
**Why it happens:** `ShowWindow(SW_SHOW)` on a window without `WS_EX_NOACTIVATE` activates it. Similarly, `SetWindowPos` without `SWP_NOACTIVATE` can activate the window.
**How to avoid:** Always include `WS_EX_NOACTIVATE` in the extended window style at creation time. Always pass `SWP_NOACTIVATE` in every `SetWindowPos` call. Use `SW_SHOWNOACTIVATE` instead of `SW_SHOW` in ShowWindow calls.
**Warning signs:** Active application loses focus or cursor blinks when overlay appears.

### Pitfall 5: Enumeration Self-Inclusion
**What goes wrong:** `focus --debug enumerate` shows the overlay HWND in the window list; overlay process appears in Alt+Tab.
**Why it happens:** Forgetting WS_EX_TOOLWINDOW, or the window is created without it.
**How to avoid:** `WS_EX_TOOLWINDOW` must be present at HWND creation time (in `CreateWindowEx`). The existing `WindowEnumerator.GetNavigableWindows` already applies the Raymond Chen Alt+Tab filter which skips tool windows (line 110: `if (isToolWindow) continue`). As long as the overlay has `WS_EX_TOOLWINDOW`, it is excluded automatically.
**Warning signs:** `focus --debug enumerate` shows rows with the `focus.exe` process name while daemon is running.

### Pitfall 6: DPI Coordinate Mismatch on Secondary Monitor
**What goes wrong:** Overlay appears at wrong position or wrong size on a secondary monitor at different DPI scale.
**Why it happens:** The project already uses PerMonitorV2 DPI awareness (verified in app.manifest). DWMWA_EXTENDED_FRAME_BOUNDS (used in WindowEnumerator) returns physical pixel coordinates. `UpdateLayeredWindow` positions the window in physical pixel coordinates on a PerMonitorV2 process. These must be consistent.
**How to avoid:** Use the DWMWA_EXTENDED_FRAME_BOUNDS rect of the target HWND directly as the position/size passed to UpdateLayeredWindow. Do not scale them. The DIB pixel dimensions must match the physical size of the target bounds rectangle exactly.
**Warning signs:** Border appears offset or wrong size on non-primary monitor; may look correct on primary monitor only.

### Pitfall 7: GDI Object Leak
**What goes wrong:** GDI handle count grows over time; after many CAPSLOCK cycles, GDI starts failing.
**Why it happens:** Pens, DCs, and bitmaps created per paint cycle without corresponding DeleteDC/DeleteObject/ReleaseDC.
**How to avoid:** Follow the strict pattern: CreateCompatibleDC → SelectObject (save old) → draw → SelectObject (restore old) → DeleteDC. CreateDIBSection bitmap reused across paint calls; only deleted on OverlayWindow disposal.
**Warning signs:** `focus` process GDI object count visible in Task Manager Details tab rises across CAPSLOCK cycles.

## Code Examples

Verified patterns from official sources:

### CreateDIBSection for 32bpp Premultiplied-Alpha Overlay

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createdibsection
// Source: https://github.com/microsoft/CsWin32/discussions/1331

unsafe HBITMAP CreateArgbDib(HDC screenDC, int width, int height, out void* bits)
{
    var bmi = new BITMAPINFO();
    bmi.bmiHeader.biSize        = (uint)sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth       = width;
    bmi.bmiHeader.biHeight      = -height;  // top-down
    bmi.bmiHeader.biPlanes      = 1;
    bmi.bmiHeader.biBitCount    = 32;
    bmi.bmiHeader.biCompression = (uint)BI_COMPRESSION.BI_RGB;

    void* bitsLocal;
    var hBmp = PInvoke.CreateDIBSection(
        screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bitsLocal, null, 0);
    bits = bitsLocal;
    return hBmp;
}
```

### UpdateLayeredWindow Call Pattern

```csharp
// Source: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow

unsafe void PaintOverlay(HWND hwnd, HDC memDC, int dstX, int dstY, int w, int h)
{
    var screenDC = PInvoke.GetDC(default);
    var blend = new BLENDFUNCTION
    {
        BlendOp             = 0,   // AC_SRC_OVER
        BlendFlags          = 0,
        SourceConstantAlpha = 255,
        AlphaFormat         = 1    // AC_SRC_ALPHA (per-pixel alpha)
    };
    var ptDst = new POINT { x = dstX, y = dstY };
    var sz    = new SIZE  { cx = w, cy = h };
    var ptSrc = new POINT { x = 0, y = 0 };

    PInvoke.UpdateLayeredWindow(
        hwnd, screenDC, &ptDst, &sz, memDC, &ptSrc,
        new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);

    PInvoke.ReleaseDC(default, screenDC);
}
```

### RegisterClassEx with Correct HINSTANCE

```csharp
// Source: https://github.com/microsoft/CsWin32/discussions/750
// Error 87 fix: use Marshal.GetHINSTANCE, NOT new HINSTANCE(0)

unsafe ATOM RegisterOverlayClass(WNDPROC wndProcDelegate)
{
    var hInstance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(OverlayWindow).Module));

    var wc = new WNDCLASSEXW
    {
        cbSize        = (uint)sizeof(WNDCLASSEXW),
        style         = 0,
        lpfnWndProc   = wndProcDelegate,
        cbClsExtra    = 0,
        cbWndExtra    = 0,
        hInstance     = hInstance,
        hIcon         = default,
        hCursor       = default,
        hbrBackground = default,  // No background brush
        lpszMenuName  = null,
        lpszClassName = className,
        hIconSm       = default
    };

    return PInvoke.RegisterClassEx(&wc);
}
```

### NativeMethods.txt Additions for Phase 5

```
# Existing (already present):
# GetForegroundWindow, SetWindowPos, etc.

# New entries needed for Phase 5:
RegisterClassEx
UnregisterClass
CreateWindowEx
DestroyWindow
ShowWindow
UpdateLayeredWindow
GetDC
ReleaseDC
CreateCompatibleDC
DeleteDC
CreateDIBSection
DeleteObject
SelectObject
GdiFlush
SetBkMode
CreatePen
GetStockObject
RoundRect
BeginPaint
EndPaint
```

### Overlay Window Exclusion from Enumeration — Verification Command

```powershell
# Verify overlays don't appear in enumeration while daemon is running:
focus --debug enumerate | Select-String "focus"
# Expected: zero rows with "focus" process name
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| SetLayeredWindowAttributes for per-pixel alpha | UpdateLayeredWindow with premultiplied DIB | Windows 2000+ | UpdateLayeredWindow is the only API for per-pixel alpha; SetLayeredWindowAttributes only supports uniform opacity |
| WM_PAINT + GDI for transparent window content | UpdateLayeredWindow (no WM_PAINT) | Windows 2000+ | UpdateLayeredWindow replaces WM_PAINT; painting via SetLayeredWindowAttributes is still used for uniform opacity |
| DWM for overlay rounded corners | GDI RoundRect into DIB | Windows 11 | Per-pixel alpha layered windows are permanently excluded from DWM auto-rounding; must draw rounds in GDI |

**Deprecated/outdated:**
- `AlphaBlend` API: Not needed; `UpdateLayeredWindow` + `BLENDFUNCTION` handles compositing directly
- `AnimateWindow`: Out of scope (project explicitly excludes animated transitions)
- GDI+ for overlay alpha: Unreliable with `UpdateLayeredWindow`; raw DIB pixel manipulation is more predictable

## Open Questions

1. **Exact border thickness in physical pixels**
   - What we know: User specified 2-3px. This is in logical pixels, but overlays use physical pixels.
   - What's unclear: Should border be 2px at 100% DPI = 4px at 200% DPI (DPI-scaled), or always 2-3 physical pixels?
   - Recommendation: Default to 2 physical pixels (no DPI scaling); the border is already thin enough to read at any scale. Future requirement OVERLAY-07/08 covers configurable and DPI-scaled thickness.

2. **NativeWindow vs raw WNDPROC for OverlayWindow**
   - What we know: Phase 4 uses WinForms `NativeWindow` subclassing for the power broadcast window; it works well on the STA thread.
   - What's unclear: Can we reuse `NativeWindow` (with `CreateHandle`) for overlay HWNDs, or should we use raw `RegisterClassEx`/`CreateWindowEx`?
   - Recommendation: Use raw `RegisterClassEx`/`CreateWindowEx` via CsWin32 directly. `NativeWindow` is designed for child/message-only windows and doesn't easily support `WS_EX_LAYERED` style at creation time. A thin `OverlayWindow` class wrapping the raw Win32 calls gives full control and cleaner lifetime management.

3. **OverlayWindow HWND lifecycle during --debug overlay command**
   - What we know: The debug command runs without the daemon (no `Application.Run`); it needs to create and destroy an overlay window.
   - What's unclear: Can `UpdateLayeredWindow` work without a running message pump?
   - Recommendation: For the debug command, use a minimal `Application.Run`-like approach: register class, `CreateWindowEx`, call `UpdateLayeredWindow` directly, call `Console.ReadKey()` on a separate thread to unblock, then destroy. The window doesn't need a pump for `UpdateLayeredWindow` itself — it just needs the WM_PAINT to not loop, handled by `BeginPaint`/`EndPaint` in WndProc. A blocking approach with `GetMessage`/`DispatchMessage` loop terminated by Console.ReadKey is cleanest.

## Validation Architecture

Nyquist validation is not enabled in `.planning/config.json` (no `nyquist_validation` field). This section is skipped per protocol.

## Sources

### Primary (HIGH confidence)

- [UpdateLayeredWindow (MS Docs)](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) — Parameters, BLENDFUNCTION, ULW_ALPHA semantics, WM_PAINT behavior
- [Extended Window Styles (MS Docs)](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) — WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, WS_EX_TRANSPARENT, WS_EX_LAYERED, WS_EX_TOPMOST definitions and exact behaviors
- [CreateDIBSection (MS Docs)](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createdibsection) — DIB section creation, 32bpp BI_RGB format, bits pointer lifecycle
- [Apply Rounded Corners (MS Docs)](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners) — Confirms per-pixel alpha layered windows cannot be rounded by DWM; must round in renderer
- [CsWin32 RegisterClassEx Discussion #750](https://github.com/microsoft/CsWin32/discussions/750) — Marshal.GetHINSTANCE fix for error 87
- [CsWin32 CreateDIBSection Discussion #1331](https://github.com/microsoft/CsWin32/discussions/1331) — Two overloads, correct null hSection pattern
- [RoundRect (MS Docs)](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-roundrect) — GDI rounded rectangle API
- Project source: `focus/app.manifest` — PerMonitorV2 DPI awareness already declared
- Project source: `focus/Windows/WindowEnumerator.cs` (line 109-111) — WS_EX_TOOLWINDOW already filtered; overlay windows excluded automatically

### Secondary (MEDIUM confidence)

- [Per-Pixel Alpha Blending in Win32 (duckmaestro, 2010)](https://duckmaestro.com/2010/06/06/per-pixel-alpha-blending-in-win32-desktop-applications/) — BLENDFUNCTION configuration, premultiplied alpha requirement, UpdateLayeredWindow call pattern (old article but describes stable Win32 behavior)
- [SetWindowPos Scaling Behaviour with WS_EX_LAYERED (MS Q&A)](https://learn.microsoft.com/en-us/answers/questions/382518/setwindowpos-scaling-behaviour-with-ws-ex-layered) — Physical pixel coordinates with PerMonitorV2; UpdateLayeredWindow coordinates are in physical pixels
- [High DPI Desktop Application Development (MS Docs)](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) — PerMonitorV2 behavior, physical pixel coordinate semantics

### Tertiary (LOW confidence)

- WebSearch results for GDI + premultiplied alpha — General community confirmation that GDI draws with full alpha then requires manual premultiplication pass; consistent across multiple sources but no single authoritative doc

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — CsWin32 already in project; Win32 APIs verified against official docs
- Architecture: HIGH — Window styles verified against official docs; GDI/DIB pattern documented in MS sources; HINSTANCE fix verified in CsWin32 issue tracker
- Pitfalls: HIGH for known Win32 gotchas (premultiplied alpha, WM_PAINT loop, thread ownership); MEDIUM for DPI coordinate behavior (MS Q&A, not primary docs)
- Color defaults: LOW (Claude's Discretion — specific hex values are esthetic choice, not technical requirement)

**Research date:** 2026-03-01
**Valid until:** 2026-04-01 (30 days; Win32 APIs are stable)
