---
phase: quick-5
plan: 01
type: execute
wave: 1
depends_on: []
files_modified: ["focus/Windows/Daemon/KeyboardHookHandler.cs", "focus/Windows/Daemon/CapsLockMonitor.cs", "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs", "focus/Windows/Daemon/Overlay/OverlayManager.cs", "focus/Windows/Daemon/Overlay/NumberLabelRenderer.cs", "focus/Windows/WindowSorter.cs", "focus/Windows/FocusConfig.cs", "focus/Windows/Daemon/DaemonCommand.cs"]
autonomous: false
requirements: ["QUICK-5"]
must_haves:
  truths: ["CAPS+1 activates the leftmost window by configured sort strategy", "CAPS+2 through CAPS+9 activates the 2nd through 9th window by position", "Number labels appear on each window overlay while CAPS is held", "Number overlay position is configurable", "Number overlay can be toggled on/off via config", "Two sort strategies exist: left-edge and center"]
  artifacts: ["focus/Windows/WindowSorter.cs", "focus/Windows/Daemon/Overlay/NumberLabelRenderer.cs", "focus/Windows/FocusConfig.cs (NumberOverlayEnabled, NumberOverlayPosition, NumberSortStrategy)", "focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs (OnNumberKeyDown)"]
  key_links: ["KeyboardHookHandler -> CapsLockMonitor via number key events in channel", "CapsLockMonitor -> OverlayOrchestrator via onNumberKeyDown callback", "OverlayOrchestrator -> WindowSorter.SortByPosition for Nth window lookup", "OverlayOrchestrator -> NumberLabelRenderer.PaintNumberLabel for overlay display"]
---

<objective>
Add CAPS+number (1-9) window selection: while holding CAPS, each visible window gets a number label
in its overlay. Pressing 1-9 activates the corresponding window. Windows are numbered by horizontal
position using a configurable sort strategy (left-edge or center). The number overlay is configurable
for position and on/off.

Purpose: Enables instant direct window access by number instead of sequential directional navigation.
Output: Working CAPS+number selection with configurable overlay numbers.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@focus/Windows/Daemon/KeyboardHookHandler.cs
@focus/Windows/Daemon/KeyEvent.cs
@focus/Windows/Daemon/CapsLockMonitor.cs
@focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
@focus/Windows/Daemon/Overlay/OverlayManager.cs
@focus/Windows/Daemon/Overlay/OverlayWindow.cs
@focus/Windows/Daemon/Overlay/BorderRenderer.cs
@focus/Windows/Daemon/TrayIcon.cs
@focus/Windows/FocusConfig.cs
@focus/Windows/WindowInfo.cs
@focus/Windows/WindowEnumerator.cs
@focus/Windows/NavigationService.cs
@focus/Windows/FocusActivator.cs
@focus/Windows/Direction.cs
@focus/Windows/Daemon/DaemonCommand.cs

<interfaces>
From focus/Windows/Daemon/KeyEvent.cs:
```csharp
internal readonly record struct KeyEvent(
    uint VkCode, bool IsKeyDown, uint Timestamp,
    bool ShiftHeld = false, bool CtrlHeld = false, bool AltHeld = false);
```

From focus/Windows/WindowInfo.cs:
```csharp
internal record WindowInfo(
    nint Hwnd, string ProcessName, string Title,
    int Left, int Top, int Right, int Bottom,
    int MonitorIndex, bool IsTopmost, bool IsUwpFrame);
```

From focus/Windows/FocusConfig.cs:
```csharp
internal class FocusConfig {
    public Strategy Strategy { get; set; } = Strategy.Balanced;
    public WrapBehavior Wrap { get; set; } = WrapBehavior.NoOp;
    public string[] Exclude { get; set; } = [];
    public OverlayColors OverlayColors { get; set; } = new();
    public string OverlayRenderer { get; set; } = "border";
    public int OverlayDelayMs { get; set; } = 0;
    // Load() uses JsonNamingPolicy.KebabCaseLower for enum serialization
}
```

From focus/Windows/Daemon/CapsLockMonitor.cs:
```csharp
internal sealed class CapsLockMonitor {
    public CapsLockMonitor(ChannelReader<KeyEvent> reader, bool verbose,
        Action? onHeld = null, Action? onReleased = null,
        Action<string>? onDirectionKeyDown = null);
    public async Task RunAsync(CancellationToken cancellationToken);
}
```

From focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs:
```csharp
internal sealed class OverlayOrchestrator : IDisposable {
    public void OnCapsLockHeld();
    public void OnCapsLockReleased();
    public void OnDirectionKeyDown(string direction);
    // ShowOverlaysForCurrentForeground() — private, enumerates windows + shows overlays
}
```

From focus/Windows/Daemon/Overlay/OverlayManager.cs:
```csharp
internal sealed class OverlayManager : IDisposable {
    public void ShowOverlay(Direction direction, RECT bounds);
    public void ShowOverlay(Direction direction, RECT bounds, uint colorOverride);
    public void ShowForegroundOverlay(RECT bounds, uint argbColor);
    public void HideAll();
}
```

From focus/Windows/Daemon/KeyboardHookHandler.cs:
```csharp
// IsDirectionKey() used for CAPS+direction interception
// Direction keys: VK_LEFT(0x25), VK_UP(0x26), VK_RIGHT(0x27), VK_DOWN(0x28), VK_W(0x57), VK_A(0x41), VK_S(0x53), VK_D(0x44)
// When _capsLockHeld && IsDirectionKey: suppress key and post KeyEvent to channel
```

From focus/Windows/Daemon/DaemonCommand.cs:
```csharp
// CapsLockMonitor wiring:
var monitor = new CapsLockMonitor(channel.Reader, verbose,
    onHeld:             () => orchestrator?.OnCapsLockHeld(),
    onReleased:         () => orchestrator?.OnCapsLockReleased(),
    onDirectionKeyDown: (dir) => orchestrator?.OnDirectionKeyDown(dir));
```
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Intercept number keys, add config, create WindowSorter, wire number activation</name>
  <files>
    focus/Windows/Daemon/KeyboardHookHandler.cs
    focus/Windows/Daemon/CapsLockMonitor.cs
    focus/Windows/Daemon/Overlay/OverlayOrchestrator.cs
    focus/Windows/Daemon/DaemonCommand.cs
    focus/Windows/FocusConfig.cs
    focus/Windows/WindowSorter.cs
  </files>
  <action>
    This task adds the full number-key pipeline: hook interception, channel delivery, sorting, and activation.

    **1. KeyboardHookHandler.cs — intercept number keys 1-9 while CAPS held:**

    Add VK constants for number keys: `VK_1 = 0x31` through `VK_9 = 0x39`.

    Add `IsNumberKey(uint vkCode)` method — returns true for VK codes 0x31-0x39 (keys 1-9).

    In `HookCallback`, add a block BEFORE the CAPSLOCK check (same position as direction key block),
    structured identically to the direction key interception:
    ```
    if (IsNumberKey(kbd->vkCode))
    {
        if (!_capsLockHeld)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
        _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time));
        return (LRESULT)1; // suppress
    }
    ```
    No modifier reading needed for number keys (unlike direction keys which log Shift/Ctrl/Alt).

    **2. CapsLockMonitor.cs — process number key events:**

    Add `Action<int>? _onNumberKeyDown` parameter to constructor (after `onDirectionKeyDown`).
    Store it in a private readonly field.

    Add `GetNumberFromVkCode(uint vkCode)` — returns `(int)(vkCode - 0x30)` for VK 0x31-0x39 (giving 1-9), or `null` otherwise.

    In `RunAsync`, after the direction key check, add a number key check:
    ```
    int? number = GetNumberFromVkCode(evt.VkCode);
    if (number is not null)
    {
        HandleNumberKeyEvent(evt, number.Value);
        continue;
    }
    ```

    Add `HandleNumberKeyEvent(KeyEvent evt, int number)`:
    - Only process key-down (ignore key-up).
    - Use `_directionKeysHeld` HashSet for repeat suppression (same mechanism as direction keys — number keys should not auto-repeat either). The HashSet name is slightly misleading but reusing it is fine since VK code ranges don't overlap.
    - On keydown (non-repeat): if verbose, log `[HH:mm:ss.fff] Number: {number}`. Invoke `_onNumberKeyDown?.Invoke(number)`.
    - On keyup: remove from `_directionKeysHeld`.

    **3. DaemonCommand.cs — wire the new callback:**

    Update the `CapsLockMonitor` constructor call to include the new parameter:
    ```csharp
    var monitor = new CapsLockMonitor(channel.Reader, verbose,
        onHeld:             () => orchestrator?.OnCapsLockHeld(),
        onReleased:         () => orchestrator?.OnCapsLockReleased(),
        onDirectionKeyDown: (dir) => orchestrator?.OnDirectionKeyDown(dir),
        onNumberKeyDown:    (num) => orchestrator?.OnNumberKeyDown(num));
    ```

    **4. FocusConfig.cs — add number overlay config properties:**

    Add a new enum `NumberSortStrategy` in FocusConfig.cs (at the top, alongside Strategy and WrapBehavior):
    ```csharp
    internal enum NumberSortStrategy { LeftEdge, Center }
    ```

    Add a new enum `NumberOverlayPosition` in FocusConfig.cs:
    ```csharp
    internal enum NumberOverlayPosition { TopLeft, TopRight, BottomLeft, BottomRight, TopCenter, Center }
    ```

    Add properties to FocusConfig:
    ```csharp
    public bool NumberOverlayEnabled { get; set; } = true;
    public NumberOverlayPosition NumberOverlayPosition { get; set; } = NumberOverlayPosition.TopLeft;
    public NumberSortStrategy NumberSortStrategy { get; set; } = NumberSortStrategy.LeftEdge;
    ```

    Update the verbose config dump in DaemonCommand.cs to include the three new fields:
    ```
    Console.Error.WriteLine($"[{ts}]   numberOverlayEnabled: {config.NumberOverlayEnabled}");
    Console.Error.WriteLine($"[{ts}]   numberOverlayPosition: {config.NumberOverlayPosition}");
    Console.Error.WriteLine($"[{ts}]   numberSortStrategy: {config.NumberSortStrategy}");
    ```

    **5. WindowSorter.cs — new file for position-based window sorting:**

    Create `focus/Windows/WindowSorter.cs`:
    ```csharp
    namespace Focus.Windows;

    internal static class WindowSorter
    {
        /// <summary>
        /// Sorts windows by horizontal position using the specified strategy.
        /// LeftEdge: sort by window's Left coordinate ascending (leftmost first).
        /// Center: sort by window's horizontal center ((Left+Right)/2) ascending (leftmost center first).
        /// Ties are broken by vertical position (Top ascending) for deterministic ordering.
        /// </summary>
        public static List<WindowInfo> SortByPosition(List<WindowInfo> windows, NumberSortStrategy strategy)
        {
            var sorted = new List<WindowInfo>(windows);
            sorted.Sort((a, b) =>
            {
                int primaryA, primaryB;
                if (strategy == NumberSortStrategy.Center)
                {
                    primaryA = (a.Left + a.Right) / 2;
                    primaryB = (b.Left + b.Right) / 2;
                }
                else // LeftEdge
                {
                    primaryA = a.Left;
                    primaryB = b.Left;
                }

                int cmp = primaryA.CompareTo(primaryB);
                if (cmp != 0) return cmp;

                // Tie-break by top edge
                return a.Top.CompareTo(b.Top);
            });
            return sorted;
        }
    }
    ```

    **6. OverlayOrchestrator.cs — add OnNumberKeyDown and number activation:**

    Add public method `OnNumberKeyDown(int number)` following the same pattern as `OnDirectionKeyDown`:
    ```csharp
    public void OnNumberKeyDown(int number)
    {
        if (_shutdownRequested) return;
        try { _staDispatcher.Invoke(() => ActivateByNumberSta(number)); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
    ```

    Add private method `ActivateByNumberSta(int number)`:
    ```csharp
    private void ActivateByNumberSta(int number)
    {
        var config = FocusConfig.Load();
        var enumerator = new WindowEnumerator();
        var (windows, _) = enumerator.GetNavigableWindows();
        var filtered = ExcludeFilter.Apply(windows, config.Exclude);

        // Remove the current foreground window from the list (user is selecting "other" windows)
        var fgHwnd = (nint)(IntPtr)PInvoke.GetForegroundWindow();
        var candidates = filtered.Where(w => w.Hwnd != fgHwnd).ToList();

        var sorted = WindowSorter.SortByPosition(candidates, config.NumberSortStrategy);

        // number is 1-based, index is 0-based
        int index = number - 1;
        if (index < 0 || index >= sorted.Count)
        {
            if (_verbose)
            {
                var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                Console.Error.WriteLine($"[{ts}] Number: {number} -> no window at index (have {sorted.Count})");
            }
            return;
        }

        var target = sorted[index];
        bool ok = FocusActivator.TryActivateWindow(target.Hwnd);

        if (_verbose)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Error.WriteLine($"[{ts}] Number: {number} -> 0x{target.Hwnd:X8} \"{WindowInfo.TruncateTitle(target.Title)}\" {(ok ? "ok" : "failed")}");
        }
    }
    ```

    Also modify `ShowOverlaysForCurrentForeground()` to add number label rendering at the END of the
    method (after all directional overlays are shown). First, load config fresh at the top of the method:
    ```csharp
    var config = FocusConfig.Load();
    ```
    Replace `_config.Exclude` with `config.Exclude` and `_config.Wrap` with `config.Wrap` throughout
    the method body. Keep `_config.OverlayDelayMs` in `OnHeldSta` since that is read before the overlay
    display method is called.

    Then at the end of the method, add:
    ```csharp
    // Number overlay labels — render after directional overlays so labels appear on top
    if (config.NumberOverlayEnabled)
    {
        var fgHwndVal = (nint)(IntPtr)PInvoke.GetForegroundWindow();
        var nonFgWindows = filtered.Where(w => w.Hwnd != fgHwndVal).ToList();
        var sorted = WindowSorter.SortByPosition(nonFgWindows, config.NumberSortStrategy);

        for (int i = 0; i < Math.Min(sorted.Count, 9); i++)
        {
            var w = sorted[i];
            var wBounds = new RECT { left = w.Left, top = w.Top, right = w.Right, bottom = w.Bottom };
            _overlayManager.ShowNumberLabel(i + 1, wBounds, config.NumberOverlayPosition);
        }
    }
    ```
  </action>
  <verify>
    `cd C:/Work/windowfocusnavigation/focus && dotnet build -c Debug 2>&1 | tail -5` — build succeeds with 0 errors.
    Verify WindowSorter.cs exists and contains both LeftEdge and Center sorting logic.
    Verify KeyboardHookHandler.cs contains IsNumberKey method handling VK codes 0x31-0x39.
    Verify CapsLockMonitor.cs has onNumberKeyDown callback parameter.
    Verify FocusConfig.cs has NumberOverlayEnabled, NumberOverlayPosition, NumberSortStrategy properties.
  </verify>
  <done>
    Number keys 1-9 are intercepted when CAPS held, routed through channel to CapsLockMonitor,
    which invokes OverlayOrchestrator.OnNumberKeyDown(n). The orchestrator sorts windows by position
    (using configured strategy) and activates the Nth window. Config has three new properties for
    controlling the number overlay feature. WindowSorter provides two sort strategies. Build succeeds.
  </done>
</task>

<task type="auto">
  <name>Task 2: Render number labels on overlay windows using UpdateLayeredWindow</name>
  <files>
    focus/Windows/Daemon/Overlay/NumberLabelRenderer.cs
    focus/Windows/Daemon/Overlay/OverlayManager.cs
  </files>
  <action>
    This task adds the visual number labels that appear on each window overlay while CAPS is held.

    **1. NumberLabelRenderer.cs — new file for rendering number labels:**

    Create `focus/Windows/Daemon/Overlay/NumberLabelRenderer.cs`. This renderer paints a single digit
    (1-9) onto a small layered overlay window using the same UpdateLayeredWindow technique as BorderRenderer.

    The label is a filled rounded-rectangle background (semi-transparent dark, e.g. 0xCC222222) with a
    white number drawn on top. Use GDI text rendering (SelectObject with HFONT, SetTextColor, SetBkMode
    TRANSPARENT, DrawText or TextOut) into the DIB section before calling UpdateLayeredWindow.

    ```csharp
    using System.Runtime.InteropServices;
    using System.Runtime.Versioning;
    using Focus.Windows;
    using global::Windows.Win32;
    using global::Windows.Win32.Foundation;
    using global::Windows.Win32.Graphics.Gdi;
    using global::Windows.Win32.UI.WindowsAndMessaging;

    namespace Focus.Windows.Daemon.Overlay;

    [SupportedOSPlatform("windows6.0.6000")]
    internal static class NumberLabelRenderer
    {
        // Label dimensions
        private const int LabelWidth = 28;
        private const int LabelHeight = 28;
        private const int LabelMargin = 6;  // margin from window edge
        private const int CornerRadius = 6;

        // Colors
        private const uint BgColor = 0xCC222222;   // semi-transparent dark background
        private const uint TextColor = 0xFFFFFFFF;  // white text

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
            var hBitmap = PInvoke.CreateDIBSection(screenDC, &bmi, DIB_USAGE.DIB_RGB_COLORS,
                &bits, HANDLE.Null, 0);

            var memDC = PInvoke.CreateCompatibleDC(screenDC);
            var oldBitmap = PInvoke.SelectObject(memDC, (HGDIOBJ)hBitmap);

            // 4. Clear to transparent
            PInvoke.GdiFlush();
            NativeMemory.Clear(bits, (nuint)(LabelWidth * LabelHeight * 4));

            // 5. Draw rounded-rect background directly into pixel buffer (premultiplied alpha)
            PInvoke.GdiFlush();
            uint* pixelBuf = (uint*)bits;
            byte bgA = (byte)(BgColor >> 24);
            byte bgR = (byte)(BgColor >> 16);
            byte bgG = (byte)(BgColor >> 8);
            byte bgB = (byte)(BgColor);

            for (int py = 0; py < LabelHeight; py++)
            {
                for (int px = 0; px < LabelWidth; px++)
                {
                    if (IsInsideRoundedRect(px, py, LabelWidth, LabelHeight, CornerRadius))
                    {
                        byte pr = (byte)((bgR * bgA) / 255);
                        byte pg = (byte)((bgG * bgA) / 255);
                        byte pb = (byte)((bgB * bgA) / 255);
                        pixelBuf[py * LabelWidth + px] = ((uint)bgA << 24) | ((uint)pr << 16) | ((uint)pg << 8) | pb;
                    }
                }
            }

            // 6. Draw text using GDI onto the DIB — create a bold font, render the digit
            //    Since we're rendering into a premultiplied-alpha DIB, we need to handle text carefully.
            //    Strategy: render text in white, then fix up the alpha channel for text pixels.
            var hFont = PInvoke.CreateFont(
                20, 0, 0, 0,
                (int)FONT_WEIGHT.FW_BOLD,
                0, 0, 0,
                (byte)FONT_CHARSET.DEFAULT_CHARSET,
                (byte)FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
                (byte)FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
                (byte)FONT_QUALITY.CLEARTYPE_QUALITY,
                0,
                "Segoe UI");

            var oldFont = PInvoke.SelectObject(memDC, (HGDIOBJ)hFont.Value);

            // Set text rendering mode
            PInvoke.SetBkMode(memDC, BACKGROUND_MODE.TRANSPARENT);
            PInvoke.SetTextColor(memDC, new COLORREF(0x00FFFFFF)); // white (GDI uses 0x00BBGGRR)

            // Draw the digit centered in the label
            string text = number.ToString();
            var textRect = new RECT { left = 0, top = 0, right = LabelWidth, bottom = LabelHeight };
            // DT_CENTER | DT_VCENTER | DT_SINGLELINE
            PInvoke.DrawText(memDC, text, -1, &textRect, DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE);

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

            var ptDst = new System.Drawing.Point(labelX, labelY);
            var sizeSrc = new SIZE(LabelWidth, LabelHeight);
            var ptSrc = new System.Drawing.Point(0, 0);

            PInvoke.UpdateLayeredWindow(hwnd, screenDC, &ptDst, &sizeSrc, memDC, &ptSrc,
                new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);

            // 9. Cleanup
            PInvoke.SelectObject(memDC, oldFont);
            PInvoke.DeleteObject((HGDIOBJ)hFont.Value);
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
    ```

    **IMPORTANT GDI PInvoke note:** The CsWin32 source generator may not have all required GDI functions.
    Check `NativeMethods.txt` in the focus project root. If `CreateFont`, `DrawText`, `SetBkMode`,
    `SetTextColor`, `SelectObject` are not listed, add them. If they are already generated, use them directly.
    The existing BorderRenderer already uses `CreateDIBSection`, `CreateCompatibleDC`, `SelectObject`,
    `UpdateLayeredWindow` etc. — follow the same CsWin32 patterns.

    For `CreateFont`, CsWin32 may generate it with different parameter types. If so, adapt the call to match.
    Alternative: use `CreateFontIndirect` with a LOGFONT struct if CreateFont isn't available through CsWin32.
    If neither is available in CsWin32, add `CreateFontIndirect` and `LOGFONTW` to NativeMethods.txt and use:
    ```csharp
    LOGFONTW lf = default;
    lf.lfHeight = 20;
    lf.lfWeight = (int)FONT_WEIGHT.FW_BOLD;
    lf.lfQuality = (byte)FONT_QUALITY.CLEARTYPE_QUALITY;
    "Segoe UI".AsSpan().CopyTo(lf.lfFaceName);
    var hFont = PInvoke.CreateFontIndirect(&lf);
    ```

    **2. OverlayManager.cs — add number label overlay pool:**

    Add a `List<OverlayWindow> _numberWindows` field initialized with 9 OverlayWindow instances
    (for numbers 1-9) in the constructor:
    ```csharp
    private readonly List<OverlayWindow> _numberWindows;

    // In constructor:
    _numberWindows = new List<OverlayWindow>();
    for (int i = 0; i < 9; i++)
        _numberWindows.Add(new OverlayWindow());
    ```

    Add method `ShowNumberLabel(int number, RECT windowBounds, NumberOverlayPosition position)`:
    ```csharp
    public void ShowNumberLabel(int number, RECT windowBounds, NumberOverlayPosition position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (number < 1 || number > 9) return;

        var window = _numberWindows[number - 1];
        NumberLabelRenderer.PaintNumberLabel(window.Hwnd, windowBounds, number, position);
        window.Show();
    }
    ```

    Update `HideAll()` to also hide number label windows:
    ```csharp
    foreach (var nw in _numberWindows)
        nw.Hide();
    ```

    Update `Dispose()` to also dispose number label windows:
    ```csharp
    foreach (var nw in _numberWindows)
        nw.Dispose();
    ```
  </action>
  <verify>
    `cd C:/Work/windowfocusnavigation/focus && dotnet build -c Debug 2>&1 | tail -5` — build succeeds with 0 errors.
    Verify NumberLabelRenderer.cs exists and contains PaintNumberLabel static method.
    Verify OverlayManager.cs has ShowNumberLabel method and _numberWindows pool of 9 windows.
    Verify NativeMethods.txt includes any new PInvoke functions that were needed (CreateFont/CreateFontIndirect, DrawText, SetBkMode, SetTextColor).
  </verify>
  <done>
    Number labels (1-9) render as small rounded-rect badges with white digit on dark background.
    Labels appear at the configured position (top-left default) on each non-foreground window's overlay
    when CAPS is held. OverlayManager manages 9 additional overlay windows for number labels and
    hides/disposes them properly. Full build succeeds.
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 3: Verify CAPS+number window selection end-to-end</name>
  <what-built>
    Complete CAPS+number window selection feature:
    - Hold CAPS to see number labels (1-9) on windows, sorted left-to-right by position
    - Press CAPS+1 through CAPS+9 to instantly activate the corresponding window
    - Config supports: numberOverlayEnabled (on/off), numberOverlayPosition (top-left, top-right, bottom-left, bottom-right, top-center, center), numberSortStrategy (left-edge, center)
  </what-built>
  <how-to-verify>
    1. Build the project: `cd focus && dotnet build -c Release`
    2. Start daemon in verbose mode: `./bin/Release/net8.0/focus.exe daemon --verbose`
    3. Open 3+ windows side by side
    4. Hold CAPS — verify number labels appear on each non-foreground window (1 = leftmost, 2 = next, etc.)
    5. While holding CAPS, press 1 — verify the leftmost window gets focus
    6. Release CAPS, hold CAPS again, press 2 — verify the 2nd window gets focus
    7. Check verbose stderr output shows "Number: N -> 0xHHHHHHHH title ok" messages
    8. Test config: add `"numberOverlayPosition": "top-center"` to config.json and re-hold CAPS — labels should move
    9. Test config: add `"numberSortStrategy": "center"` to config.json — verify sort order may differ for asymmetrically sized windows
    10. Test config: add `"numberOverlayEnabled": false` — verify labels disappear but CAPS+N still activates
  </how-to-verify>
  <resume-signal>Type "approved" or describe issues to fix</resume-signal>
</task>

</tasks>

<verification>
- `cd C:/Work/windowfocusnavigation/focus && dotnet build -c Release` succeeds
- Daemon starts without errors with `focus.exe daemon --verbose`
- Number keys 1-9 are intercepted only when CAPS is held (pass through normally otherwise)
- Number labels render correctly as rounded-rect badges with digits
- Window activation by number works reliably
- Config changes (position, strategy, enabled) take effect on next CAPS hold
</verification>

<success_criteria>
- CAPS+1 through CAPS+9 activates the Nth window sorted by horizontal position
- Number overlay labels appear at configured position (default: top-left corner)
- Two sort strategies (left-edge, center) produce correct orderings
- Number overlay can be toggled on/off and repositioned via config.json
- No regression in existing CAPS+direction navigation
- Clean build with no warnings related to new code
</success_criteria>

<output>
After completion, create `.planning/quick/5-add-caps-number-window-selection-with-co/5-SUMMARY.md`
</output>
