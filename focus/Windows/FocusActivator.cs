using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Focus.Windows;

[SupportedOSPlatform("windows5.0")]
internal static class FocusActivator
{
    // advapi32 P/Invoke for elevation check (not available via CsWin32)
    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
        out int TokenInformation, int TokenInformationLength, out int ReturnLength);

    private const int TokenElevationType = 18; // TOKEN_INFORMATION_CLASS.TokenElevationType
    /// <summary>
    /// Attempts to activate a single window using the SendInput ALT bypass followed by SetForegroundWindow.
    /// </summary>
    /// <param name="hwnd">The window handle to activate.</param>
    /// <returns>true if SetForegroundWindow succeeded; false if the window could not be activated (e.g., elevated process).</returns>
    public static unsafe bool TryActivateWindow(nint hwnd)
    {
        var targetHwnd = new HWND((void*)(IntPtr)hwnd);

        // ALT keydown + keyup bypass: tricks Windows into allowing SetForegroundWindow from a
        // non-foreground process by making the system believe ALT is held (releases DWIN lock).
        INPUT keyDown = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = VIRTUAL_KEY.VK_MENU,
                    dwFlags = 0
                }
            }
        };

        INPUT keyUp = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = VIRTUAL_KEY.VK_MENU,
                    dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP
                }
            }
        };

        ReadOnlySpan<INPUT> inputs = stackalloc INPUT[2] { keyDown, keyUp };
        PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());

        // Immediately activate — no delay (same thread, same message pump tick)
        return PInvoke.SetForegroundWindow(targetHwnd);
    }

    /// <summary>
    /// Iterates ranked candidates in order (best score first) and attempts to activate each.
    /// Returns the appropriate exit code.
    /// </summary>
    /// <param name="rankedCandidates">Candidates sorted by score descending (best first).</param>
    /// <returns>
    /// 0 = activation succeeded,
    /// 1 = no candidates exist in this direction,
    /// 2 = candidates existed but all activation attempts failed.
    /// </returns>
    public static int ActivateBestCandidate(List<(WindowInfo Window, double Score)> rankedCandidates, bool verbose = false)
    {
        if (rankedCandidates.Count == 0)
            return 1; // exit code: no candidates in this direction

        foreach (var (window, _) in rankedCandidates)
        {
            bool ok = TryActivateWindow(window.Hwnd);
            if (verbose)
                Console.Error.WriteLine($"[focus] activating: 0x{window.Hwnd:X8} \"{Truncate(window.Title, 40)}\" -> {(ok ? "ok" : "failed")}");
            if (ok)
                return 0; // exit code: success
            // Activation failed (likely elevated window) — silently try next candidate
        }

        return 2; // exit code: candidates existed but none could be activated
    }

    /// <summary>
    /// Activates best candidate with wrap-around behavior when no candidates exist.
    /// </summary>
    [SupportedOSPlatform("windows6.0.6000")]
    public static int ActivateWithWrap(
        List<(WindowInfo Window, double Score)> candidates,
        List<WindowInfo> allWindows,
        Direction direction,
        Strategy strategy,
        WrapBehavior wrap,
        bool verbose)
    {
        if (candidates.Count > 0)
            return ActivateBestCandidate(candidates, verbose);

        // No candidates in this direction — apply wrap behavior
        return wrap switch
        {
            WrapBehavior.Wrap => HandleWrap(allWindows, direction, strategy, verbose),
            WrapBehavior.Beep => HandleBeep(),
            _ => 1  // NoOp: return exit code 1 (no candidates)
        };
    }

    [SupportedOSPlatform("windows6.0.6000")]
    private static int HandleWrap(
        List<WindowInfo> allWindows, Direction direction, Strategy strategy, bool verbose)
    {
        var opposite = direction switch
        {
            Direction.Left  => Direction.Right,
            Direction.Right => Direction.Left,
            Direction.Up    => Direction.Down,
            Direction.Down  => Direction.Up,
            _ => direction
        };

        var wrapped = NavigationService.GetRankedCandidates(allWindows, opposite, strategy);
        if (wrapped.Count == 0)
            return 1; // Nothing in any direction

        if (verbose)
            Console.Error.WriteLine($"[focus] wrap: no candidates {direction}, trying {opposite} ({wrapped.Count} found)");

        // Activate the LAST candidate (furthest in opposite direction = "wrap around" to the far side)
        // Reverse the list so the furthest candidate (highest score) is tried first
        wrapped.Reverse();
        return ActivateBestCandidate(wrapped, verbose);
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static int HandleBeep()
    {
        PInvoke.MessageBeep((global::Windows.Win32.UI.WindowsAndMessaging.MESSAGEBOX_STYLE)0xFFFFFFFF); // system default beep (0xFFFFFFFF = simple beep)
        return 1; // still exit code 1 (no focus switch)
    }

    /// <summary>
    /// Returns true if the window belongs to an elevated (admin) process and this daemon is NOT elevated.
    /// When both are elevated, UIPI is not a problem — returns false.
    /// </summary>
    public static unsafe bool IsWindowElevated(nint hwnd, Action<Exception>? onError = null)
    {
        try
        {
            var targetHwnd = new HWND((void*)(IntPtr)hwnd);
            PInvoke.GetWindowThreadProcessId(targetHwnd, out uint pid);
            if (pid == 0) return false;

            var hProcess = PInvoke.OpenProcess(
                global::Windows.Win32.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
                false, pid);
            if (hProcess.IsNull) return true; // can't open = assume elevated

            try
            {
                if (!OpenProcessToken(hProcess, TOKEN_QUERY, out var hToken))
                {
                    // OpenProcessToken fails with ACCESS_DENIED for elevated processes
                    // when the caller is not elevated — this IS the elevation signal.
                    int err = Marshal.GetLastWin32Error();
                    return err == 5; // ERROR_ACCESS_DENIED = elevated target
                }

                try
                {
                    if (GetTokenInformation(hToken, TokenElevationType,
                            out int elevationType, sizeof(int), out _))
                    {
                        // TokenElevationTypeFull (2) = process is running elevated via UAC
                        // TokenElevationTypeDefault (1) = UAC disabled or standard user
                        // TokenElevationTypeLimited (3) = filtered token (non-elevated admin)
                        if (elevationType != 2) return false;

                        // Target is fully elevated — check if WE are too
                        return !IsCurrentProcessElevated();
                    }
                    return false;
                }
                finally
                {
                    CloseHandleRaw(hToken);
                }
            }
            finally
            {
                PInvoke.CloseHandle(hProcess);
            }
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return false; // never crash the daemon for an elevation check
        }
    }

    internal static bool IsCurrentProcessElevated()
    {
        try
        {
            var hProcess = System.Diagnostics.Process.GetCurrentProcess().Handle;
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out var hToken))
                return false;

            try
            {
                if (GetTokenInformation(hToken, TokenElevationType,
                        out int elevationType, sizeof(int), out _))
                    return elevationType == 2; // TokenElevationTypeFull
                return false;
            }
            finally
            {
                CloseHandleRaw(hToken);
            }
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandleRaw(IntPtr hObject);

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..(maxLen - 3)] + "...";
}
