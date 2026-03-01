using System.Runtime.Versioning;
using System.Threading.Channels;
using System.Windows.Forms;
using global::Windows.Win32;

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

        // 4. Create unbounded event channel (producer: hook callback, consumer: monitor task)
        var channel = Channel.CreateUnbounded<KeyEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = false  // Never run continuations on the hook thread
        });

        // 5. Create daemon components
        var hook    = new KeyboardHookHandler(channel.Writer);
        var monitor = new CapsLockMonitor(channel.Reader, verbose);

        // 6. Set up cancellation (used by both Ctrl+C and tray Exit paths)
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;  // Prevent immediate termination — allow ordered cleanup
            cts.Cancel();
        };

        // 7. Start consumer task on thread pool — consumes KeyEvent from channel
        var consumerTask = Task.Run(() => monitor.RunAsync(cts.Token));

        // 8. Run STA message pump on dedicated thread — required for WH_KEYBOARD_LL
        var staThread = new Thread(() =>
        {
            Application.Run(new DaemonApplicationContext(hook, monitor, () => cts.Cancel()));
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = false;  // Keep process alive while message pump runs
        staThread.Start();

        // 9. Register cancellation callback that exits the STA message pump.
        //    Handles the Ctrl+C path where tray Exit hasn't already called Application.ExitThread().
        //    Application.ExitThread() is thread-safe and idempotent — safe to call multiple times.
        cts.Token.Register(() => Application.ExitThread());

        // 10. Block main thread until cancellation is signalled (Ctrl+C or tray Exit)
        try
        {
            cts.Token.WaitHandle.WaitOne();
        }
        catch (ObjectDisposedException) { }

        // 11. Cleanup — ordered to ensure no orphaned hooks or zombie processes

        // Wait for the STA thread to finish (it should exit once Application.ExitThread() was called)
        staThread.Join(TimeSpan.FromSeconds(3));

        // Uninstall keyboard hook (DaemonApplicationContext.Dispose does not uninstall —
        // we are the owner of hook lifecycle)
        hook.Uninstall();
        hook.Dispose();

        // Signal channel completion so the consumer's ReadAllAsync loop exits cleanly
        channel.Writer.Complete();

        // Wait for consumer task to drain and finish (with timeout)
        try
        {
            consumerTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch { }

        // Release the single-instance mutex
        DaemonMutex.Release(mutex);

        return 0;
    }
}
