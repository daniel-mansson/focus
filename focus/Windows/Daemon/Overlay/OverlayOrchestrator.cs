using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Focus.Windows;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;

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

    // Fade timing: 100ms fade-in, 80ms fade-out. Timer fires every ~16ms (~60fps).
    private const float FadeInDurationMs  = 100f;
    private const float FadeOutDurationMs = 80f;

    private readonly OverlayManager _overlayManager;
    private readonly FocusConfig _config;

    // WinForms control used for cross-thread Invoke from CapsLockMonitor worker thread to STA.
    private readonly Control _staDispatcher;

    private readonly ForegroundMonitor _foregroundMonitor;

    // Optional activation delay (config.OverlayDelayMs). Fired once, then stopped.
    private readonly System.Windows.Forms.Timer _delayTimer;

    // Fade animation timer — fires at ~60fps during fade-in and fade-out.
    private readonly System.Windows.Forms.Timer _fadeTimer;

    private bool _capsLockHeld;

    // Fade state — managed exclusively on the STA thread.
    private float _fadeProgress;   // 0.0 = transparent, 1.0 = full opacity
    private bool _fadingIn;        // true = fading in, false = fading out

    // Last-shown overlay positions and colors — populated by ShowOverlaysForCurrentForeground.
    // Used by RepaintAllOverlays to re-paint with scaled alpha during fade animation.
    // Key: Direction, Value: (RECT bounds, uint argbColor)
    private readonly Dictionary<Direction, (RECT Bounds, uint Color)> _lastShown = new();

    private bool _disposed;

    // Checked before Invoke to avoid ObjectDisposedException during shutdown.
    private volatile bool _shutdownRequested;

    /// <summary>
    /// Creates the orchestrator. Must be called on the STA thread.
    /// </summary>
    public OverlayOrchestrator(OverlayManager overlayManager, FocusConfig config)
    {
        _overlayManager = overlayManager;
        _config = config;

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

        // Fade animation timer.
        _fadeTimer = new System.Windows.Forms.Timer();
        _fadeTimer.Interval = 16; // ~60fps
        _fadeTimer.Tick += OnFadeTick;
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
            ShowOverlaysForCurrentForeground();
            StartFadeIn();
        }
    }

    private void OnReleasedSta()
    {
        _capsLockHeld = false;

        // Stop delay timer — user released before delay elapsed, preventing a spurious trigger.
        _delayTimer.Stop();

        // Start fade-out instead of immediately hiding — overlays fade to transparent over ~80ms.
        StartFadeOut();
    }

    private void OnDelayTimerTick(object? sender, EventArgs e)
    {
        // Fire once only.
        _delayTimer.Stop();

        // Only show overlays if CapsLock is still held (user may have released during delay).
        if (_capsLockHeld)
        {
            ShowOverlaysForCurrentForeground();
            StartFadeIn();
        }
    }

    private void OnForegroundChanged(HWND hwnd)
    {
        // Only reposition while CapsLock is held.
        if (!_capsLockHeld) return;

        // If a fade-out is running (CAPSLOCK released path), ignore foreground changes.
        // _capsLockHeld is false in that case, so this guard already handles it.
        // If fading in or already at full opacity, snap to new foreground immediately.
        _fadeTimer.Stop();
        _fadeProgress = 1.0f;

        ShowOverlaysForCurrentForeground();
        RepaintAllOverlays(_fadeProgress);
    }

    // -----------------------------------------------------------------------------------------
    // Fade animation — all on STA thread (Timer.Tick fires on STA thread).
    // -----------------------------------------------------------------------------------------

    private void StartFadeIn()
    {
        _fadeTimer.Stop();
        _fadingIn = true;
        _fadeProgress = 0.0f;
        // Paint first frame immediately so the overlays appear without a 16ms delay.
        RepaintAllOverlays(_fadeProgress);
        _fadeTimer.Start();
    }

    private void StartFadeOut()
    {
        _fadeTimer.Stop();
        _fadingIn = false;
        // Keep _fadeProgress at its current value for a smooth transition from wherever we are.
        // If overlays were never shown (quick tap before delay elapsed), nothing is visible.
        if (_fadeProgress > 0.0f)
        {
            _fadeTimer.Start();
        }
        else
        {
            // Nothing visible — skip the fade and clean up immediately.
            _overlayManager.HideAll();
            _lastShown.Clear();
        }
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        float step = _fadingIn
            ? (16f / FadeInDurationMs)
            : (16f / FadeOutDurationMs);

        _fadeProgress = _fadingIn
            ? Math.Min(1.0f, _fadeProgress + step)
            : Math.Max(0.0f, _fadeProgress - step);

        RepaintAllOverlays(_fadeProgress);

        bool done = _fadingIn ? _fadeProgress >= 1.0f : _fadeProgress <= 0.0f;

        if (done)
        {
            _fadeTimer.Stop();

            if (!_fadingIn)
            {
                // Fade-out complete — hide all overlays and clear stored state.
                _overlayManager.HideAll();
                _lastShown.Clear();
            }
        }
    }

    /// <summary>
    /// Re-paints all currently tracked overlays with their last-known bounds and colors,
    /// but with alpha scaled by <paramref name="alphaScale"/>.
    /// Called each timer tick during fade-in and fade-out.
    /// </summary>
    private void RepaintAllOverlays(float alphaScale)
    {
        foreach (var (dir, (bounds, color)) in _lastShown)
        {
            // Scale the alpha channel: extract 0xAA from 0xAARRGGBB, multiply, recombine.
            uint originalAlpha = (color >> 24) & 0xFF;
            uint scaledAlpha   = (uint)(originalAlpha * alphaScale);
            uint scaledColor   = (color & 0x00FFFFFF) | (scaledAlpha << 24);

            _overlayManager.ShowOverlay(dir, bounds, scaledColor);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Core scoring and overlay positioning.
    // -----------------------------------------------------------------------------------------

    private unsafe void ShowOverlaysForCurrentForeground()
    {
        var enumerator = new WindowEnumerator();
        var (windows, _) = enumerator.GetNavigableWindows();
        var filtered = ExcludeFilter.Apply(windows, _config.Exclude);

        // Clear previous state — will be repopulated below.
        _lastShown.Clear();

        int candidatesFound = 0;

        foreach (Direction direction in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
        {
            var ranked = NavigationService.GetRankedCandidates(filtered, direction, _config.Strategy);

            if (ranked.Count == 0)
            {
                // OVERLAY-05: no candidate in this direction — hide that overlay silently.
                _overlayManager.HideOverlay(direction);
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

            uint color = _config.OverlayColors.GetArgb(direction);

            // Track for fade animation repaints.
            _lastShown[direction] = (bounds, color);

            _overlayManager.ShowOverlay(direction, bounds);
        }

        // OVERLAY-05 special case: solo window — all four directions have zero candidates.
        // Show a dim/muted border on the foreground window itself as a "daemon is alive" indicator.
        if (candidatesFound == 0)
        {
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
                    foreach (Direction direction in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
                    {
                        _lastShown[direction] = (fgBounds, SoloDimColor);
                        _overlayManager.ShowOverlay(direction, fgBounds, SoloDimColor);
                    }
                }
                // If bounds retrieval fails, do nothing — graceful degradation.
            }
        }
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

        _fadeTimer.Stop();
        _fadeTimer.Dispose();

        _staDispatcher.Dispose();
    }
}
