using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows.Daemon;

[SupportedOSPlatform("windows6.0.6000")]
internal sealed class KeyboardHookHandler : IDisposable
{
    // CRITICAL: Must be static to prevent GC collection while hook is active.
    // GC does not know that unmanaged code holds a reference to this delegate.
    // If stored in a local or instance variable, GC can collect it after Install()
    // returns, crashing the process with ExecutionEngineException.
    private static HOOKPROC? s_hookProc;

    private readonly ChannelWriter<KeyEvent> _channelWriter;
    private global::Windows.Win32.UnhookWindowsHookExSafeHandle? _hookHandle;

    // Virtual key codes
    private const uint VK_CAPITAL = 0x14;
    private const uint VK_SHIFT   = 0x10;
    private const uint VK_CONTROL = 0x11;

    // KBDLLHOOKSTRUCT flags
    private const uint LLKHF_INJECTED = 0x00000010; // Injected from any process (DAEMON-06)
    private const uint LLKHF_ALTDOWN  = 0x00000020; // Alt key held

    // WM_KEYDOWN / WM_SYSKEYDOWN message codes
    private const uint WM_KEYDOWN    = 0x0100;
    private const uint WM_SYSKEYDOWN = 0x0104;

    public KeyboardHookHandler(ChannelWriter<KeyEvent> channelWriter)
    {
        _channelWriter = channelWriter;
    }

    /// <summary>
    /// Installs the WH_KEYBOARD_LL global keyboard hook.
    /// Must be called from a thread running a Windows message pump (e.g., Application.Run).
    /// </summary>
    public void Install()
    {
        s_hookProc = HookCallback;

        // GetModuleHandle(null?) returns a FreeLibrarySafeHandle with ownsHandle=false
        // so it will NOT call FreeLibrary on dispose — correct for a handle we don't own.
        var hmod = PInvoke.GetModuleHandle((string?)null);

        _hookHandle = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, s_hookProc, hmod, 0);

        if (_hookHandle.IsInvalid)
        {
            throw new InvalidOperationException(
                $"Failed to install keyboard hook: error {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Uninstalls the keyboard hook and allows the HOOKPROC delegate to be GC'd.
    /// </summary>
    public void Uninstall()
    {
        if (_hookHandle is { IsInvalid: false })
        {
            _hookHandle.Dispose();
            _hookHandle = null;
            s_hookProc = null; // Allow GC to collect delegate now that hook is removed
        }
    }

    public void Dispose() => Uninstall();

    private unsafe LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode < 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        var kbd = (KBDLLHOOKSTRUCT*)lParam.Value;

        // DAEMON-06: Filter AHK-injected events — prevents overlay flicker from AHK
        if (((uint)kbd->flags & LLKHF_INJECTED) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Only process VK_CAPITAL (CAPSLOCK)
        if (kbd->vkCode != VK_CAPITAL)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Filter modifier combinations — only bare CAPSLOCK triggers detection
        // Alt check: LLKHF_ALTDOWN flag in KBDLLHOOKSTRUCT.flags
        if (((uint)kbd->flags & LLKHF_ALTDOWN) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Ctrl check: GetKeyState high bit set means key is down
        if ((PInvoke.GetKeyState((int)VK_CONTROL) & 0x8000) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Shift check: GetKeyState high bit set means key is down
        if ((PInvoke.GetKeyState((int)VK_SHIFT) & 0x8000) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Post bare CAPSLOCK event to worker thread via Channel
        // MUST use TryWrite (fire-and-forget) — NEVER use WriteAsync in hook callback.
        // Hook callback has a 1000ms total budget on Windows 10 1709+.
        // TryWrite on unbounded channel always succeeds.
        bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
        _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time));

        // DAEMON-02: Suppress CAPSLOCK toggle — return non-zero to eat the event.
        // This prevents the CAPSLOCK toggle state from changing.
        // Return 1 for both key-down AND key-up to fully suppress the key.
        return (LRESULT)1;
    }
}
