using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Focus.Windows;
using Focus.Windows.Daemon.Overlay;

namespace Focus.Windows.Daemon;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class DaemonApplicationContext : ApplicationContext
{
    private readonly KeyboardHookHandler _hook;
    private readonly CapsLockMonitor _monitor;
    private readonly Action _onExit;
    private readonly NotifyIcon _trayIcon;
    private readonly PowerBroadcastWindow _powerWindow;
    private readonly OverlayOrchestrator _orchestrator;
    private readonly OverlayManager _overlayManager;
    private readonly bool _background;
    private readonly bool _verbose;
    private readonly DaemonStatus _status;
    private SettingsForm? _settingsForm;

    /// <summary>
    /// Runs entirely on the STA thread. Creates OverlayOrchestrator here so all WinEvent hooks,
    /// WinForms Controls, and timers are created on the correct thread.
    /// </summary>
    public DaemonApplicationContext(KeyboardHookHandler hook, CapsLockMonitor monitor, Action onExit,
        FocusConfig config, bool background, bool verbose, DaemonStatus status, out OverlayOrchestrator orchestrator)
    {
        _hook       = hook;
        _monitor    = monitor;
        _onExit     = onExit;
        _background = background;
        _verbose    = verbose;
        _status     = status;

        // Install keyboard hook — safe here because Application.Run() starts the
        // message pump immediately after this constructor returns. The hook callback
        // requires an active message pump; by installing in the constructor, the
        // hook is ready the moment the pump starts.
        _hook.Install();

        // Create OverlayManager and OverlayOrchestrator on the STA thread.
        // OverlayOrchestrator installs a WinEvent hook (SetWinEventHook) and creates a WinForms
        // Control for cross-thread Invoke — both require the STA thread.
        var renderer = OverlayManager.CreateRenderer(config.OverlayRenderer);
        _overlayManager = new OverlayManager(renderer, config.OverlayColors);
        _orchestrator = new OverlayOrchestrator(_overlayManager, config, verbose, status);

        // Expose orchestrator to DaemonCommand.Run via out parameter so it can inject callbacks
        // and call RequestShutdown/Dispose during the ordered teardown sequence.
        orchestrator = _orchestrator;

        // Status labels (non-clickable, grayed out)
        var hookLabel    = new ToolStripLabel("Hook: Active")  { Enabled = false };
        var uptimeLabel  = new ToolStripLabel("Uptime: 0s")    { Enabled = false };
        var lastActLabel = new ToolStripLabel("Last: \u2014")  { Enabled = false };

        var menu = new ContextMenuStrip();

        // Block 1: Status
        menu.Items.Add(hookLabel);
        menu.Items.Add(uptimeLabel);
        menu.Items.Add(lastActLabel);

        // Separator 1
        menu.Items.Add(new ToolStripSeparator());

        // Block 2: Actions
        menu.Items.Add("Settings...", null, OnSettingsClicked);
        menu.Items.Add("Restart Daemon", null, OnRestartClicked);

        // Separator 2
        menu.Items.Add(new ToolStripSeparator());

        // Block 3: Exit
        menu.Items.Add("Exit", null, OnExitClicked);

        // Refresh status labels on every menu open (MENU-02)
        menu.Opening += (_, _) =>
        {
            hookLabel.Text    = _hook.IsInstalled ? "Hook: Active" : "Hook: Inactive";
            uptimeLabel.Text  = _status.FormatUptime();
            lastActLabel.Text = _status.FormatLastAction();
        };

        // Load custom icon from embedded assembly resource
        var iconStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("focus.ico")
            ?? throw new InvalidOperationException("Embedded icon resource 'focus.ico' not found.");
        var customIcon = new Icon(iconStream);

        _trayIcon = new NotifyIcon
        {
            Icon = customIcon,
            ContextMenuStrip = menu,
            Text = "Focus \u2014 Navigation Daemon",
            Visible = true
        };

        // Create hidden NativeWindow to receive WM_POWERBROADCAST for sleep/wake recovery.
        // ApplicationContext does not expose WndProc, so we use a message-only window.
        _powerWindow = new PowerBroadcastWindow(_hook, _monitor);
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(onApply: () => OnRestartClicked(null, EventArgs.Empty));
            _settingsForm.Show();
        }
        else
        {
            _settingsForm.BringToFront();
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
        }
    }

    private void OnRestartClicked(object? sender, EventArgs e)
    {
        var args = new List<string> { "daemon" };
        if (_background) args.Add("--background");
        if (_verbose)    args.Add("--verbose");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = Environment.ProcessPath!,
                Arguments       = string.Join(' ', args),
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            // Surface error in status labels — keep current daemon running (locked decision)
            _status.LastAction = $"Restart failed: {ex.Message}";
            return;
        }

        // Hide tray icon before exit to prevent ghost icon (Pitfall 3)
        _trayIcon.Visible = false;

        // Signal daemon shutdown
        _onExit();

        // Exit the STA message pump — new instance will AcquireOrReplace the mutex
        Application.ExitThread();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        // Hide tray icon immediately to prevent ghost icon in system tray
        _trayIcon.Visible = false;

        // Signal the daemon orchestrator to begin teardown
        _onExit();

        // Exit the STA message pump — unblocks Application.Run()
        Application.ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsForm?.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _powerWindow.DestroyHandle();
            // OverlayOrchestrator and OverlayManager disposal is handled by DaemonCommand.Run
            // after staThread.Join to ensure STA thread has fully exited before resources are freed.
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Hidden NativeWindow that receives WM_POWERBROADCAST messages for sleep/wake recovery.
    /// When the system resumes from sleep, the WH_KEYBOARD_LL hook may be invalidated by the OS.
    /// Uninstalling and reinstalling on PBT_APMRESUMEAUTOMATIC ensures the hook stays active.
    /// Also forces CAPSLOCK toggle OFF on resume — the daemon's key suppression may have left it ON.
    /// </summary>
    private sealed class PowerBroadcastWindow : NativeWindow
    {
        private const int WM_POWERBROADCAST      = 0x0218;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;

        private readonly KeyboardHookHandler _hook;
        private readonly CapsLockMonitor _monitor;

        public PowerBroadcastWindow(KeyboardHookHandler hook, CapsLockMonitor monitor)
        {
            _hook = hook;
            _monitor = monitor;

            // Create a message-only window to receive broadcast messages
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_POWERBROADCAST && (int)m.WParam == PBT_APMRESUMEAUTOMATIC)
            {
                // System resumed from sleep — reinstall hook and reset monitor state.
                // The hook is uninstalled first to ensure the new hook is registered fresh.
                _hook.Uninstall();
                _hook.Install();
                _monitor.ResetState();

                // Force CAPSLOCK toggle OFF after wake — the daemon's key suppression may
                // have left the toggle state inconsistent during the sleep transition.
                DaemonCommand.ForceCapsLockOff();
            }

            base.WndProc(ref m);
        }
    }
}
