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

    // CAPSLOCK state tracked in real-time from hook callbacks
    private bool _capsLockHeld;

    // TAB held state: true when CAPS+TAB is held (Move mode master switch)
    private bool _tabHeld;

    // Virtual key codes
    private const uint VK_CAPITAL  = 0x14;
    private const uint VK_SHIFT    = 0x10;   // Generic shift — used for CAPS modifier filter
    private const uint VK_CONTROL  = 0x11;   // Generic ctrl — used for CAPS modifier filter
    private const uint VK_TAB      = 0x09;
    private const uint VK_LSHIFT   = 0xA0;   // Left Shift only (MODE-02)

    // Direction key virtual key codes — arrows
    private const uint VK_LEFT  = 0x25;
    private const uint VK_UP    = 0x26;
    private const uint VK_RIGHT = 0x27;
    private const uint VK_DOWN  = 0x28;

    // Direction key virtual key codes — WASD
    private const uint VK_W = 0x57;
    private const uint VK_A = 0x41;
    private const uint VK_S = 0x53;
    private const uint VK_D = 0x44;

    // Number key virtual key codes — 1 through 9
    private const uint VK_1 = 0x31;
    private const uint VK_9 = 0x39;

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

    private static bool IsDirectionKey(uint vkCode) => vkCode switch
    {
        VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN => true,
        VK_W or VK_A or VK_S or VK_D => true,
        _ => false
    };

    private static bool IsNumberKey(uint vkCode) => vkCode >= VK_1 && vkCode <= VK_9;

    private unsafe LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode < 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        var kbd = (KBDLLHOOKSTRUCT*)lParam.Value;

        // DAEMON-06: Filter AHK-injected events — prevents overlay flicker from AHK
        if (((uint)kbd->flags & LLKHF_INJECTED) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // TAB key: suppress when CAPS held (MODE-01), pass through when CAPS not held (MODE-04)
        if (kbd->vkCode == VK_TAB)
        {
            if (!_capsLockHeld)
                return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);  // MODE-04: bare TAB passes through

            bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
            _tabHeld = isKeyDown;

            // Write TAB event so CapsLockMonitor can track mode transitions
            _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, Mode: WindowMode.Move));
            return (LRESULT)1;  // suppress TAB from reaching app
        }

        // LSHIFT key: observe (not suppress) when CAPS held for overlay mode transitions
        if (kbd->vkCode == VK_LSHIFT)
        {
            if (_capsLockHeld)
            {
                bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
                _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, Mode: WindowMode.Grow));
            }
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);  // never suppress — GetKeyState needs it
        }

        // Number key interception (1-9): suppress and route through channel when CAPS held.
        if (IsNumberKey(kbd->vkCode))
        {
            if (!_capsLockHeld)
                return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

            bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
            _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time));
            return (LRESULT)1; // suppress
        }

        // Direction key interception: runs before CAPSLOCK check so direction keys
        // are handled when CAPSLOCK is already held (regardless of other modifiers).
        if (IsDirectionKey(kbd->vkCode))
        {
            if (!_capsLockHeld)
            {
                // CAPSLOCK not held — pass direction key through normally (HOTKEY-04)
                return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);
            }

            // CAPSLOCK is held — intercept and suppress the direction key (HOTKEY-03)
            bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;

            // Read left-side modifier state (MODE-02: LSHIFT=Grow)
            bool lShiftHeld = (PInvoke.GetKeyState((int)VK_LSHIFT) & 0x8000) != 0;
            bool altHeld    = ((uint)kbd->flags & LLKHF_ALTDOWN) != 0;

            // Derive mode from _tabHeld + left-modifier state (MODE-01, MODE-02)
            // TAB overrides modifiers; LSHIFT = grow (right/up expand, left/down contract)
            WindowMode mode = (_tabHeld, lShiftHeld) switch
            {
                (true, _)  => WindowMode.Move,     // CAPS+TAB = move
                (_, true)  => WindowMode.Grow,     // CAPS+LSHIFT = grow/shrink by direction
                _          => WindowMode.Navigate  // bare CAPS+direction = navigate
            };

            // MUST use TryWrite (fire-and-forget) — NEVER use WriteAsync in hook callback.
            // Hook callback has a 1000ms total budget on Windows 10 1709+.
            _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, lShiftHeld, altHeld, mode));

            // Suppress the key — return non-zero so the focused app never sees it
            return (LRESULT)1;
        }

        // Only process VK_CAPITAL (CAPSLOCK) from here on
        if (kbd->vkCode != VK_CAPITAL)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Filter modifier combinations — only bare CAPSLOCK or SHIFT+CAPSLOCK triggers detection.
        // Alt+CAPS and Ctrl+CAPS are filtered out (system shortcuts).
        // Shift+CAPS is allowed so users can hold LShift first then press CAPS to enter grow mode.
        if (((uint)kbd->flags & LLKHF_ALTDOWN) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Ctrl check: GetKeyState high bit set means key is down
        if ((PInvoke.GetKeyState((int)VK_CONTROL) & 0x8000) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Update _capsLockHeld in real-time so direction key check above is accurate
        bool capsIsKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
        _capsLockHeld = capsIsKeyDown;
        if (!capsIsKeyDown)
            _tabHeld = false;  // CAPS release = master switch off (clears all modes)

        // Post bare CAPSLOCK event to worker thread via Channel
        // MUST use TryWrite (fire-and-forget) — NEVER use WriteAsync in hook callback.
        // TryWrite on unbounded channel always succeeds.
        _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, capsIsKeyDown, kbd->time));

        // DAEMON-02: Suppress CAPSLOCK toggle — return non-zero to eat the event.
        // This prevents the CAPSLOCK toggle state from changing.
        // Return 1 for both key-down AND key-up to fully suppress the key.
        return (LRESULT)1;
    }
}
