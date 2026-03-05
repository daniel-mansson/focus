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

    // LALT held state: true when CAPS+LAlt is held (Move mode)
    private bool _lAltHeld;

    // LWIN held state: true when CAPS+LWin is held (Grow mode)
    private bool _lWinHeld;

    // Virtual key codes
    private const uint VK_CAPITAL  = 0x14;
    private const uint VK_CONTROL  = 0x11;   // Generic ctrl — used for CAPS modifier filter
    private const uint VK_LMENU   = 0xA4;   // Left Alt (Move mode)
    private const uint VK_LWIN    = 0x5B;   // Left Windows key (Grow mode)

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

    /// <summary>
    /// Returns true when the keyboard hook is currently installed and valid.
    /// Derived dynamically from the hook handle to avoid staleness after sleep/wake reinstalls.
    /// </summary>
    public bool IsInstalled => _hookHandle is { IsInvalid: false };

    /// <summary>
    /// Resets the CAPSLOCK and modifier held state tracked by the hook callback.
    /// Called when navigating to an elevated window — UIPI prevents the hook from seeing
    /// the CapsLock release, so the state must be cleared manually.
    /// </summary>
    public void ResetState()
    {
        _capsLockHeld = false;
        _lAltHeld = false;
        _lWinHeld = false;
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

        // LALT key: suppress when CAPS held (Move mode), pass through when CAPS not held.
        // Suppressed to prevent menu activation in target apps.
        if (kbd->vkCode == VK_LMENU)
        {
            if (!_capsLockHeld)
                return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

            bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
            _lAltHeld = isKeyDown;

            _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, Mode: WindowMode.Move));
            return (LRESULT)1;  // suppress LAlt from reaching app
        }

        // LWIN key: suppress when CAPS held (Grow mode), pass through when CAPS not held.
        // Suppressed to prevent Start menu activation.
        if (kbd->vkCode == VK_LWIN)
        {
            if (!_capsLockHeld)
                return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

            bool isKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
            _lWinHeld = isKeyDown;

            _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, Mode: WindowMode.Grow));
            return (LRESULT)1;  // suppress LWin from reaching app
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

            // Derive mode from _lAltHeld + _lWinHeld state
            // LAlt overrides LWin; LWin = grow (right/up expand, left/down contract)
            WindowMode mode = (_lAltHeld, _lWinHeld) switch
            {
                (true, _)  => WindowMode.Move,     // CAPS+LAlt = move
                (_, true)  => WindowMode.Grow,     // CAPS+LWin = grow/shrink by direction
                _          => WindowMode.Navigate   // bare CAPS+direction = navigate
            };

            // MUST use TryWrite (fire-and-forget) — NEVER use WriteAsync in hook callback.
            // Hook callback has a 1000ms total budget on Windows 10 1709+.
            _channelWriter.TryWrite(new KeyEvent(kbd->vkCode, isKeyDown, kbd->time, _lWinHeld, mode));

            // Suppress the key — return non-zero so the focused app never sees it
            return (LRESULT)1;
        }

        // Only process VK_CAPITAL (CAPSLOCK) from here on
        if (kbd->vkCode != VK_CAPITAL)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Filter modifier combinations — Ctrl+CAPS is filtered out (system shortcut).
        // Alt+CAPS and Win+CAPS are allowed for modifier-first activation of Move/Grow modes.
        if ((PInvoke.GetKeyState((int)VK_CONTROL) & 0x8000) != 0)
            return PInvoke.CallNextHookEx(null, nCode, wParam, lParam);

        // Update _capsLockHeld in real-time so direction key check above is accurate
        bool capsIsKeyDown = (uint)wParam.Value == WM_KEYDOWN || (uint)wParam.Value == WM_SYSKEYDOWN;
        _capsLockHeld = capsIsKeyDown;
        if (capsIsKeyDown)
        {
            _lAltHeld = (PInvoke.GetKeyState((int)VK_LMENU) & 0x8000) != 0;   // LAlt-first activation (Move)
            _lWinHeld = (PInvoke.GetKeyState((int)VK_LWIN)  & 0x8000) != 0;   // LWin-first activation (Grow)
        }
        else
        {
            _lAltHeld = false;  // CAPS release = master switch off (clears all modes)
            _lWinHeld  = false;
        }

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
