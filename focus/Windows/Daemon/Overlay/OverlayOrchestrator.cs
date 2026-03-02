using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Focus.Windows;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;
using global::Windows.Win32.Graphics.Gdi;

namespace Focus.Windows.Daemon.Overlay;

/// <summary>
/// Central coordinator that connects CapsLockMonitor (worker thread), ForegroundMonitor (STA thread),
/// and OverlayManager (STA thread) into a coherent overlay activation/deactivation lifecycle.
///
/// Threading model:
///   - OnCapsLockHeld/OnCapsLockReleased are called from the CapsLockMonitor worker thread.
///   - All OverlayManager calls and timer operations happen on the STA thread via Control.Invoke.
///   - ForegroundMonitor callback fires on the STA thread (WINEVENT_OUTOFCONTEXT).
///
/// Must be constructed on the STA thread.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class OverlayOrchestrator : IDisposable
{
    // ~19% opacity neutral gray — subtle "daemon is alive" indicator for the solo-window case.
    private const uint SoloDimColor = 0x30AAAAAA;

    // ~88% opacity white — thin full-perimeter border on the currently focused window.
    private const uint ForegroundBorderColor = 0xE0FFFFFF;

    // Left/right overlays are expanded by this many pixels to avoid overlapping up/down borders
    // on the same target window. Matches BorderRenderer.BorderThickness + 1.
    private const int LeftRightInset = 3;

    private readonly OverlayManager _overlayManager;
    private readonly FocusConfig _config;
    private readonly bool _verbose;

    // WinForms control used for cross-thread Invoke from CapsLockMonitor worker thread to STA.
    private readonly Control _staDispatcher;

    private readonly ForegroundMonitor _foregroundMonitor;

    // Optional activation delay (config.OverlayDelayMs). Fired once, then the Tick handler stops it.
    private readonly System.Windows.Forms.Timer _delayTimer;

    private bool _capsLockHeld;

    private bool _disposed;

    // Checked before Invoke to avoid ObjectDisposedException during shutdown.
    private volatile bool _shutdownRequested;

    /// <summary>
    /// Creates the orchestrator. Must be called on the STA thread.
    /// </summary>
    public OverlayOrchestrator(OverlayManager overlayManager, FocusConfig config, bool verbose = false)
    {
        _overlayManager = overlayManager;
        _config = config;
        _verbose = verbose;

        // Create a WinForms control to marshal cross-thread calls onto the STA thread.
        // Force handle creation immediately so Invoke is available before the message pump starts.
        _staDispatcher = new Control();
        _ = _staDispatcher.Handle;

        // Install the foreground window change hook on the STA thread.
        _foregroundMonitor = new ForegroundMonitor(OnForegroundChanged);
        _foregroundMonitor.Install();

        // Activation delay timer — fires once, then the Tick handler stops it.
        _delayTimer = new System.Windows.Forms.Timer();
        _delayTimer.Tick += OnDelayTimerTick;
    }

    // -----------------------------------------------------------------------------------------
    // Public API — called from CapsLockMonitor worker thread.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Called when CAPSLOCK is first held down. Marshals to the STA thread.
    /// </summary>
    public void OnCapsLockHeld()
    {
        if (_shutdownRequested) return;

        try
        {
            _staDispatcher.Invoke(OnHeldSta);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Called when CAPSLOCK is released. Marshals to the STA thread.
    /// </summary>
    public void OnCapsLockReleased()
    {
        if (_shutdownRequested) return;

        try
        {
            _staDispatcher.Invoke(OnReleasedSta);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Called when a number key (1-9) is pressed while CAPSLOCK is held.
    /// Marshals to the STA thread and activates the Nth window sorted by horizontal position.
    /// </summary>
    public void OnNumberKeyDown(int number)
    {
        if (_shutdownRequested) return;
        try { _staDispatcher.Invoke(() => ActivateByNumberSta(number)); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Called when TAB is first pressed while CAPSLOCK is held (entering Move mode).
    /// Hides navigate-target outlines immediately, keeping only the foreground border.
    /// </summary>
    public void OnTabDown()
    {
        if (_shutdownRequested) return;

        try
        {
            _staDispatcher.Invoke(() =>
            {
                if (_capsLockHeld)
                    RefreshForegroundOverlayOnly();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Called when TAB is released while CAPSLOCK is still held (returning to Navigate mode).
    /// Restores full overlay display including navigate-target outlines.
    /// </summary>
    public void OnTabReleased()
    {
        if (_shutdownRequested) return;

        try
        {
            _staDispatcher.Invoke(() =>
            {
                if (_capsLockHeld)
                    ShowOverlaysForCurrentForeground();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Called when a direction key is pressed while CAPSLOCK is held.
    /// Marshals to the STA thread and performs the full navigation pipeline:
    /// parse direction, load fresh config, enumerate windows, score, activate.
    /// </summary>
    /// <param name="direction">Cardinal direction: "up", "down", "left", "right"</param>
    /// <param name="mode">Window operation mode derived from modifier keys (default: Navigate).</param>
    public void OnDirectionKeyDown(string direction, WindowMode mode = WindowMode.Navigate)
    {
        if (_shutdownRequested) return;

        if (mode != WindowMode.Navigate)
        {
            try
            {
                _staDispatcher.Invoke(() =>
                {
                    WindowManagerService.MoveOrResize(direction, mode, _verbose);
                    if (_capsLockHeld)
                        RefreshForegroundOverlayOnly();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }

        try
        {
            _staDispatcher.Invoke(() => NavigateSta(direction));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void NavigateSta(string direction)
    {
        // 1. Parse direction string — defensive; CapsLockMonitor should never send invalid values.
        var dir = DirectionParser.Parse(direction);
        if (dir is null) return;

        // 2. Load config fresh on every call so runtime config changes are always respected.
        var config = FocusConfig.Load();

        // 3. Enumerate navigable windows.
        var enumerator = new WindowEnumerator();
        var (windows, _) = enumerator.GetNavigableWindows();

        // 4. Apply exclude filter.
        var filtered = ExcludeFilter.Apply(windows, config.Exclude);

        // 5. Score and rank candidates. The overload with out-params gives us the foreground HWND
        //    and origin for verbose logging. When no foreground window exists, NavigationService
        //    uses screen center as origin (locked decision — no fallback code needed here).
        var ranked = NavigationService.GetRankedCandidates(filtered, dir.Value, config.Strategy,
            out var fgHwnd, out var originX, out var originY);

        // 6. Verbose: log origin and candidate count.
        if (_verbose)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Error.WriteLine(
                $"[{ts}] Navigate: {direction} | origin: 0x{fgHwnd:X8} center=({originX:F0}, {originY:F0}) | candidates: {ranked.Count}");
        }

        // 7. Activate best candidate with wrap handling.
        int result = FocusActivator.ActivateWithWrap(ranked, filtered, dir.Value, config.Strategy, config.Wrap, _verbose);

        // 8. Verbose: log outcome. result==1 (no candidates) is a silent no-op per user decision.
        if (_verbose)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            if (result == 0)
                Console.Error.WriteLine($"[{ts}] Navigate: {direction} -> success");
            else if (result == 2)
                Console.Error.WriteLine($"[{ts}] Navigate: {direction} -> all activations failed");
        }
    }

    private void ActivateByNumberSta(int number)
    {
        var config = FocusConfig.Load();
        var enumerator = new WindowEnumerator();
        var (windows, _) = enumerator.GetNavigableWindows();
        var filtered = ExcludeFilter.Apply(windows, config.Exclude);

        var sorted = WindowSorter.SortByPosition(filtered, config.NumberSortStrategy);

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

    /// <summary>
    /// Signals that shutdown is in progress. Prevents further Invoke calls after disposal begins.
    /// Call before Dispose() from the shutdown path.
    /// </summary>
    public void RequestShutdown()
    {
        _shutdownRequested = true;
    }

    // -----------------------------------------------------------------------------------------
    // STA-thread methods — only called via Control.Invoke or directly on STA thread.
    // -----------------------------------------------------------------------------------------

    private void OnHeldSta()
    {
        _capsLockHeld = true;

        if (_config.OverlayDelayMs > 0)
        {
            _delayTimer.Interval = _config.OverlayDelayMs;
            _delayTimer.Start();
        }
        else
        {
            ShowOverlaysForActivation();
        }
    }

    /// <summary>
    /// Shows the appropriate overlays based on current modifier state.
    /// If LShift is already held (Grow mode), shows only the foreground border.
    /// Otherwise shows full navigate overlays.
    /// </summary>
    private void ShowOverlaysForActivation()
    {
        // VK_LSHIFT = 0xA0, VK_TAB = 0x09; high bit set = key is down
        bool lShiftHeld = (PInvoke.GetKeyState(0xA0) & 0x8000) != 0;
        bool tabHeld    = (PInvoke.GetKeyState(0x09) & 0x8000) != 0;
        if (lShiftHeld || tabHeld)
            RefreshForegroundOverlayOnly();
        else
            ShowOverlaysForCurrentForeground();
    }

    private void OnReleasedSta()
    {
        _capsLockHeld = false;

        // Stop delay timer — user released before delay elapsed, preventing a spurious trigger.
        _delayTimer.Stop();

        _overlayManager.HideAll();
    }

    private void OnDelayTimerTick(object? sender, EventArgs e)
    {
        // Fire once only.
        _delayTimer.Stop();

        // Only show overlays if CapsLock is still held (user may have released during delay).
        if (_capsLockHeld)
        {
            ShowOverlaysForActivation();
        }
    }

    private void OnForegroundChanged(HWND hwnd)
    {
        // Only reposition while CapsLock is held.
        if (!_capsLockHeld) return;

        ShowOverlaysForCurrentForeground();
    }

    // -----------------------------------------------------------------------------------------
    // Core scoring and overlay positioning.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Hides all overlays then shows only the active window border at its current position.
    /// Used after Move/Grow/Shrink so navigate-target outlines are not visible — only the
    /// active window outline tracks the window's new bounds.
    /// Must be called on the STA thread (inside _staDispatcher.Invoke).
    /// </summary>
    private unsafe void RefreshForegroundOverlayOnly()
    {
        _overlayManager.HideAll();

        var fgHwnd = PInvoke.GetForegroundWindow();
        if (fgHwnd == default) return;

        RECT fgBounds = default;
        var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref fgBounds, 1));
        var hr = PInvoke.DwmGetWindowAttribute(
            fgHwnd,
            DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            boundsBytes);

        if (hr.Succeeded && (fgBounds.right - fgBounds.left) > 0)
        {
            _overlayManager.ShowForegroundOverlay(fgBounds, ForegroundBorderColor);
        }
    }

    private unsafe void ShowOverlaysForCurrentForeground()
    {
        // Hide all overlays first so stale positions from a previous hold are never visible.
        _overlayManager.HideAll();

        // Load config fresh so runtime changes (strategy, wrap, exclude, number overlay) take effect immediately.
        var config = FocusConfig.Load();

        // Show white border around the currently focused window.
        var fgHwnd = PInvoke.GetForegroundWindow();
        if (fgHwnd != default)
        {
            RECT fgBounds = default;
            var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref fgBounds, 1));
            var hr = PInvoke.DwmGetWindowAttribute(
                fgHwnd,
                DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                boundsBytes);

            if (hr.Succeeded && (fgBounds.right - fgBounds.left) > 0)
            {
                _overlayManager.ShowForegroundOverlay(fgBounds, ForegroundBorderColor);
            }
        }

        var enumerator = new WindowEnumerator();
        var (windows, _) = enumerator.GetNavigableWindows();
        var filtered = ExcludeFilter.Apply(windows, config.Exclude);

        int candidatesFound = 0;

        foreach (Direction direction in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
        {
            var ranked = NavigationService.GetRankedCandidates(filtered, direction, config.Strategy);

            if (ranked.Count == 0)
            {
                if (config.Wrap == WrapBehavior.Wrap)
                {
                    // When wrap is enabled and no candidates exist in this direction,
                    // find the wrap target: the furthest window in the opposite direction.
                    var opposite = GetOppositeDirection(direction);
                    var wrapped = NavigationService.GetRankedCandidates(filtered, opposite, config.Strategy);

                    if (wrapped.Count == 0)
                    {
                        // Nothing in any direction — hide and continue.
                        _overlayManager.HideOverlay(direction);
                        continue;
                    }

                    // The list is sorted ascending by score (best/closest first).
                    // The wrap target is the LAST entry — the furthest window on the opposite edge.
                    var wrapTarget = wrapped[wrapped.Count - 1].Window;
                    var wrapBounds = new RECT
                    {
                        left   = wrapTarget.Left,
                        top    = wrapTarget.Top,
                        right  = wrapTarget.Right,
                        bottom = wrapTarget.Bottom
                    };

                    if (direction is Direction.Left or Direction.Right)
                    {
                        wrapBounds.left   -= LeftRightInset;
                        wrapBounds.top    -= LeftRightInset;
                        wrapBounds.right  += LeftRightInset;
                        wrapBounds.bottom += LeftRightInset;
                    }

                    ClampToMonitor(new HWND((nint)(IntPtr)wrapTarget.Hwnd), ref wrapBounds);

                    _overlayManager.ShowOverlay(direction, wrapBounds);
                    candidatesFound++;
                }
                else
                {
                    // OVERLAY-05: no candidate in this direction and wrap is disabled — hide silently.
                    _overlayManager.HideOverlay(direction);
                }
                continue;
            }

            candidatesFound++;

            var top = ranked[0].Window;
            var bounds = new RECT
            {
                left   = top.Left,
                top    = top.Top,
                right  = top.Right,
                bottom = top.Bottom
            };

            // Expand left/right overlays slightly so they don't overlap up/down borders
            // on the same window — renders the side borders just outside the window edge.
            if (direction is Direction.Left or Direction.Right)
            {
                bounds.left   -= LeftRightInset;
                bounds.top    -= LeftRightInset;
                bounds.right  += LeftRightInset;
                bounds.bottom += LeftRightInset;
            }

            // Clamp to the monitor so overlays on partially off-screen windows stay visible.
            ClampToMonitor(new HWND((nint)(IntPtr)top.Hwnd), ref bounds);

            _overlayManager.ShowOverlay(direction, bounds);
        }

        // OVERLAY-05 special case: solo window — all four directions have zero candidates.
        // Show a dim/muted border on the foreground window itself as a "daemon is alive" indicator.
        if (candidatesFound == 0)
        {
            var soloFgHwnd = PInvoke.GetForegroundWindow();

            if (soloFgHwnd != default)
            {
                RECT fgBounds = default;
                var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref fgBounds, 1));
                var hr = PInvoke.DwmGetWindowAttribute(
                    soloFgHwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                    boundsBytes);

                if (hr.Succeeded && (fgBounds.right - fgBounds.left) > 0)
                {
                    foreach (Direction direction in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
                    {
                        _overlayManager.ShowOverlay(direction, fgBounds, SoloDimColor);
                    }
                }
                // If bounds retrieval fails, do nothing — graceful degradation.
            }
        }

        // Number overlay labels — render after directional overlays so labels appear on top
        if (config.NumberOverlayEnabled)
        {
            var sorted = WindowSorter.SortByPosition(filtered, config.NumberSortStrategy);

            for (int i = 0; i < Math.Min(sorted.Count, 9); i++)
            {
                var w = sorted[i];
                var wBounds = new RECT { left = w.Left, top = w.Top, right = w.Right, bottom = w.Bottom };
                _overlayManager.ShowNumberLabel(i + 1, wBounds, config.NumberOverlayPosition);
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the cardinal direction directly opposite to the given direction.
    /// Used to find the wrap target when no candidates exist in the requested direction.
    /// </summary>
    private static Direction GetOppositeDirection(Direction direction) => direction switch
    {
        Direction.Left  => Direction.Right,
        Direction.Right => Direction.Left,
        Direction.Up    => Direction.Down,
        Direction.Down  => Direction.Up,
        _               => direction
    };

    /// <summary>
    /// Clamps a RECT to the physical bounds of the monitor that owns the given HWND.
    /// Ensures overlays on partially off-screen windows are pinned to the screen edge.
    /// </summary>
    private static unsafe void ClampToMonitor(HWND hwnd, ref RECT bounds)
    {
        var hMon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFO);
        if (!PInvoke.GetMonitorInfo(hMon, ref mi))
            return;

        var mon = mi.rcMonitor;
        if (bounds.left   < mon.left)   bounds.left   = mon.left;
        if (bounds.top    < mon.top)    bounds.top    = mon.top;
        if (bounds.right  > mon.right)  bounds.right  = mon.right;
        if (bounds.bottom > mon.bottom) bounds.bottom = mon.bottom;
    }

    // -----------------------------------------------------------------------------------------
    // Disposal.
    // -----------------------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // ForegroundMonitor must be uninstalled on the STA thread — Dispose() is called from
        // the STA shutdown path (via DaemonApplicationContext or direct disposal on STA thread).
        _foregroundMonitor.Dispose();

        _delayTimer.Stop();
        _delayTimer.Dispose();

        _staDispatcher.Dispose();
    }
}
