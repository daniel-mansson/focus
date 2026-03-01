using System.Runtime.Versioning;
using System.Threading.Channels;
using System.Windows.Forms;
using Focus.Windows.Daemon.Overlay;
using global::Windows.Win32;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Focus.Windows.Daemon;

[SupportedOSPlatform("windows6.0.6000")]
internal static class DaemonCommand
{
    /// <summary>
    /// Runs the focus daemon lifecycle: single-instance mutex, tray icon, keyboard hook,
    /// CAPSLOCK monitor, and clean shutdown on Ctrl+C or tray Exit.
    /// </summary>
    /// <param name="background">If true, detaches from console (FreeConsole) after startup message.</param>
    /// <param name="verbose">If true, CapsLockMonitor logs hold/release events to stderr.</param>
    /// <returns>Exit code: 0 = clean exit, 2 = error.</returns>
    public static int Run(bool background, bool verbose)
    {
        // 1. Acquire single-instance mutex (replace any existing daemon instance)
        Mutex? mutex = null;
        try
        {
            mutex = DaemonMutex.AcquireOrReplace();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error acquiring daemon mutex: {ex.Message}");
            return 2;
        }

        // 2. Print startup confirmation BEFORE any possible console detach
        Console.WriteLine("Focus daemon started. Listening for CAPSLOCK.");

        // 3. Detach console if background mode — after this, Console writes may fail silently
        if (background)
            PInvoke.FreeConsole();

        // 4. Force CAPSLOCK toggle OFF before installing the keyboard hook.
        //    Must be done before hook install so the synthetic keypress is not intercepted by us.
        ForceCapsLockOff();

        // 5. Load configuration from disk (renderer, colors, delay, strategy, etc.)
        var config = FocusConfig.Load();

        // 5a. If verbose, print the resolved config to stderr so the user knows what's in effect
        if (verbose)
        {
            var configPath = FocusConfig.GetConfigPath();
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.Error.WriteLine($"[{ts}] Config:");
            Console.Error.WriteLine($"[{ts}]   file: {configPath}");
            Console.Error.WriteLine($"[{ts}]   exists: {File.Exists(configPath)}");
            Console.Error.WriteLine($"[{ts}]   strategy: {config.Strategy}");
            Console.Error.WriteLine($"[{ts}]   wrap: {config.Wrap}");
            Console.Error.WriteLine($"[{ts}]   exclude: [{string.Join(", ", config.Exclude.Select(p => $"\"{p}\""))}]");
            Console.Error.WriteLine($"[{ts}]   overlayRenderer: {config.OverlayRenderer}");
            Console.Error.WriteLine($"[{ts}]   overlayDelayMs: {config.OverlayDelayMs}");
            Console.Error.WriteLine($"[{ts}]   overlayColors: left={config.OverlayColors.Left} right={config.OverlayColors.Right} up={config.OverlayColors.Up} down={config.OverlayColors.Down}");
        }

        // 6. Create unbounded event channel (producer: hook callback, consumer: monitor task)
        var channel = Channel.CreateUnbounded<KeyEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = false  // Never run continuations on the hook thread
        });

        // 7. Create keyboard hook handler (producer side)
        var hook = new KeyboardHookHandler(channel.Writer);

        // 8. Late-binding reference for OverlayOrchestrator.
        //    CapsLockMonitor is created before the STA thread (which creates OverlayOrchestrator),
        //    so we use closures over a captured nullable reference that the STA thread fills in.
        OverlayOrchestrator? orchestrator = null;

        var monitor = new CapsLockMonitor(channel.Reader, verbose,
            onHeld:             () => orchestrator?.OnCapsLockHeld(),
            onReleased:         () => orchestrator?.OnCapsLockReleased(),
            onDirectionKeyDown: (dir) => orchestrator?.OnDirectionKeyDown(dir));

        // 9. Set up cancellation (used by both Ctrl+C and tray Exit paths)
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;  // Prevent immediate termination — allow ordered cleanup
            cts.Cancel();
        };

        // 10. Start consumer task on thread pool — consumes KeyEvent from channel
        var consumerTask = Task.Run(() => monitor.RunAsync(cts.Token));

        // 11. Run STA message pump on dedicated thread — required for WH_KEYBOARD_LL.
        //     DaemonApplicationContext creates OverlayOrchestrator on the STA thread and assigns
        //     it to the captured 'orchestrator' variable via the out parameter.
        var staThread = new Thread(() =>
        {
            Application.Run(new DaemonApplicationContext(hook, monitor, () => cts.Cancel(), config, verbose, out orchestrator));
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = false;  // Keep process alive while message pump runs
        staThread.Start();

        // 12. Register cancellation callback that exits the STA message pump.
        //     Handles the Ctrl+C path where tray Exit hasn't already called Application.ExitThread().
        //     Application.ExitThread() is thread-safe and idempotent — safe to call multiple times.
        cts.Token.Register(() => Application.ExitThread());

        // 13. Block main thread until cancellation is signalled (Ctrl+C or tray Exit)
        try
        {
            cts.Token.WaitHandle.WaitOne();
        }
        catch (ObjectDisposedException) { }

        // 14. Ordered shutdown — prevents Invoke exceptions from worker threads during teardown.
        //     Signal shutdown first so CapsLockMonitor callbacks become no-ops immediately.
        orchestrator?.RequestShutdown();

        // Uninstall keyboard hook — stop producing events immediately
        hook.Uninstall();
        hook.Dispose();

        // Signal channel completion so the consumer's ReadAllAsync loop exits cleanly
        channel.Writer.Complete();

        // Wait for STA thread to finish (ApplicationContext disposes orchestrator on exit)
        staThread.Join(TimeSpan.FromMilliseconds(500));

        // Dispose orchestrator resources after the STA thread has exited
        orchestrator?.Dispose();

        try
        {
            consumerTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }

        // Release the single-instance mutex
        DaemonMutex.Release(mutex);

        return 0;
    }

    /// <summary>
    /// Forces the CAPSLOCK toggle state to OFF by sending a synthetic key press if it is currently ON.
    /// Called at daemon startup and after system sleep/wake to ensure CAPSLOCK is never left in a
    /// toggled-on state by the daemon's key suppression.
    /// </summary>
    internal static void ForceCapsLockOff()
    {
        const byte VK_CAPITAL = 0x14;
        // Low-order bit of GetKeyState is 1 when the toggle is ON.
        if ((PInvoke.GetKeyState(VK_CAPITAL) & 0x0001) != 0)
        {
            // Send synthetic CAPSLOCK down + up to toggle it OFF.
            PInvoke.keybd_event(VK_CAPITAL, 0x45, 0, 0);
            PInvoke.keybd_event(VK_CAPITAL, 0x45, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
        }
    }
}
