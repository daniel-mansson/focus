using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Focus.Windows.Daemon;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class DaemonApplicationContext : ApplicationContext
{
    private readonly KeyboardHookHandler _hook;
    private readonly CapsLockMonitor _monitor;
    private readonly Action _onExit;
    private readonly NotifyIcon _trayIcon;
    private readonly PowerBroadcastWindow _powerWindow;

    public DaemonApplicationContext(KeyboardHookHandler hook, CapsLockMonitor monitor, Action onExit)
    {
        _hook = hook;
        _monitor = monitor;
        _onExit = onExit;

        // Install keyboard hook — safe here because Application.Run() starts the
        // message pump immediately after this constructor returns. The hook callback
        // requires an active message pump; by installing in the constructor, the
        // hook is ready the moment the pump starts.
        _hook.Install();

        // Create tray icon with Exit context menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, OnExitClicked);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,  // Built-in system icon — no embedded resource needed
            ContextMenuStrip = menu,
            Text = "Focus Daemon",
            Visible = true
        };

        // Create hidden NativeWindow to receive WM_POWERBROADCAST for sleep/wake recovery.
        // ApplicationContext does not expose WndProc, so we use a message-only window.
        _powerWindow = new PowerBroadcastWindow(_hook, _monitor);
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
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _powerWindow.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Hidden NativeWindow that receives WM_POWERBROADCAST messages for sleep/wake recovery.
    /// When the system resumes from sleep, the WH_KEYBOARD_LL hook may be invalidated by the OS.
    /// Uninstalling and reinstalling on PBT_APMRESUMEAUTOMATIC ensures the hook stays active.
    /// </summary>
    private sealed class PowerBroadcastWindow : NativeWindow
    {
        private const int WM_POWERBROADCAST     = 0x0218;
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
                // System resumed from sleep — reinstall hook and reset monitor state
                // The hook is uninstalled first to ensure the new hook is registered fresh.
                _hook.Uninstall();
                _hook.Install();
                _monitor.ResetState();
            }

            base.WndProc(ref m);
        }
    }
}
