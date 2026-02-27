using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Focus.Windows;

[SupportedOSPlatform("windows5.0")]
internal static class FocusActivator
{
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
    public static int ActivateBestCandidate(List<(WindowInfo Window, double Score)> rankedCandidates)
    {
        if (rankedCandidates.Count == 0)
            return 1; // exit code: no candidates in this direction

        foreach (var (window, _) in rankedCandidates)
        {
            if (TryActivateWindow(window.Hwnd))
                return 0; // exit code: success
            // Activation failed (likely elevated window) — silently try next candidate
        }

        return 2; // exit code: candidates existed but none could be activated
    }
}
