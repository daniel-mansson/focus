using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Focus.Windows;

/// <summary>
/// Enumerates all user-navigable windows using the Raymond Chen Alt+Tab algorithm.
/// Filters hidden, cloaked, minimized windows and deduplicates UWP CoreWindow hosts.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal class WindowEnumerator
{
    // Extended window style flags (not in generated CsWin32 enums for these specific values)
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_TOPMOST = 0x00000008;

    /// <summary>
    /// Returns all user-navigable windows (Alt+Tab visible) in Z-order (topmost first),
    /// along with the count of UWP duplicate HWNDs that were suppressed.
    /// </summary>
    public unsafe (List<WindowInfo> Windows, int FilteredUwpCount) GetNavigableWindows()
    {
        // Step 1: Enumerate monitors once, reuse list for all windows
        var monitors = MonitorHelper.EnumerateMonitors();

        // Pre-allocate buffers outside the loop (CA2014: avoid stackalloc in loops)
        Span<char> classNameBuffer = stackalloc char[256];
        Span<char> titleBuffer = stackalloc char[512];

        // Step 2: EnumWindows — collect ALL HWNDs into raw list
        // Store delegate in local variable to prevent GC collection during unmanaged callback (RESEARCH Pitfall 2)
        var rawHwnds = new List<nint>();
        WNDENUMPROC enumCallback = (hWnd, _) =>
        {
            rawHwnds.Add((nint)(IntPtr)hWnd);
            return true;
        };
        PInvoke.EnumWindows(enumCallback, default);

        // Step 3: Apply Alt+Tab filter to each raw HWND
        // Step 4: UWP dedup
        // Step 5: Build WindowInfo for each surviving HWND
        var result = new List<WindowInfo>();
        int filteredUwpCount = 0;

        // Track ApplicationFrameWindow HWNDs added so far for CoreWindow dedup
        var afwHwnds = new HashSet<nint>();

        foreach (var hwndNint in rawHwnds)
        {
            var hwnd = new HWND((void*)(IntPtr)hwndNint);

            // --- Alt+Tab filter (Raymond Chen algorithm) ---

            // a. IsWindowVisible — skip if false (ENUM-02)
            if (!PInvoke.IsWindowVisible(hwnd))
                continue;

            // b. DWMWA_CLOAKED — skip if cloaked != 0 (ENUM-03, Pitfall 1: use uint not BOOL)
            uint cloaked = 0;
            var cloakedBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref cloaked, 1));
            PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, cloakedBytes);
            if (cloaked != 0)
                continue;

            // c. IsIconic — skip minimized windows (ENUM-04)
            if (PInvoke.IsIconic(hwnd))
                continue;

            // d. Read GWL_EXSTYLE — extract WS_EX_TOOLWINDOW and WS_EX_APPWINDOW flags
            int exStyle = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
            bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
            bool isTopmost = (exStyle & WS_EX_TOPMOST) != 0;

            // e. If WS_EX_APPWINDOW is set → include (overrides owner chain walk)
            if (!isAppWindow)
            {
                // f. Walk owner chain: GetAncestor(GA_ROOTOWNER), then loop GetLastActivePopup until stable
                var rootOwner = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
                var walk = rootOwner;
                while (true)
                {
                    var lastPopup = PInvoke.GetLastActivePopup(walk);
                    if (lastPopup == walk)
                        break;
                    if (PInvoke.IsWindowVisible(lastPopup))
                    {
                        walk = lastPopup;
                        break;
                    }
                    walk = lastPopup;
                    // Prevent infinite loops (GetLastActivePopup can cycle)
                    if (walk == rootOwner)
                        break;
                }

                // g. If walk result != original HWND → skip
                if (walk != hwnd)
                    continue;

                // h. If WS_EX_TOOLWINDOW → skip
                if (isToolWindow)
                    continue;
            }

            // --- Passed Alt+Tab filter ---

            // Step 4: UWP dedup — get class name
            int classLen = PInvoke.GetClassName(hwnd, classNameBuffer);
            string className = classLen > 0 ? new string(classNameBuffer[..classLen]) : string.Empty;

            // CoreWindow: check if a matching ApplicationFrameWindow parent is already in result
            if (className == "Windows.UI.Core.CoreWindow")
            {
                // Find the ApplicationFrameWindow ancestor (parent or owner)
                var parent = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_PARENT);
                nint parentNint = (nint)(IntPtr)parent;
                if (afwHwnds.Contains(parentNint))
                {
                    // ApplicationFrameWindow already in result — suppress this CoreWindow
                    filteredUwpCount++;
                    continue;
                }
                // No AFW parent found — keep CoreWindow (unusual but handle gracefully)
            }

            bool isUwpFrame = className == "ApplicationFrameWindow";

            // Step 5a: Process name
            // For UWP frames: find child HWND with different PID and use that child's process name
            string processName;
            if (isUwpFrame)
            {
                processName = GetUwpProcessName(hwnd);
            }
            else
            {
                PInvoke.GetWindowThreadProcessId(hwnd, out uint pid);
                processName = GetProcessName(pid);
            }

            // Step 5b: Title
            int titleLen = PInvoke.GetWindowText(hwnd, titleBuffer);
            string title = titleLen > 0 ? new string(titleBuffer[..titleLen]) : string.Empty;

            // Step 5c: Bounds from DWMWA_EXTENDED_FRAME_BOUNDS (physical pixels)
            int left = 0, top = 0, right = 0, bottom = 0;
            RECT boundsRect = default;
            var boundsBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref boundsRect, 1));
            var hr = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, boundsBytes);
            if (hr.Succeeded)
            {
                left = boundsRect.left;
                top = boundsRect.top;
                right = boundsRect.right;
                bottom = boundsRect.bottom;
            }

            // Step 5d: Monitor index
            int monitorIndex = MonitorHelper.GetMonitorIndex(hwndNint, monitors);

            // Step 5f: IsUwpFrame already set above

            var windowInfo = new WindowInfo(
                hwndNint,
                processName,
                title,
                left, top, right, bottom,
                monitorIndex,
                isTopmost,
                isUwpFrame);

            result.Add(windowInfo);

            // Track AFW HWNDs for CoreWindow dedup
            if (isUwpFrame)
                afwHwnds.Add(hwndNint);
        }

        return (result, filteredUwpCount);
    }

    /// <summary>
    /// For an ApplicationFrameWindow, finds the child HWND that belongs to a different process
    /// (the actual UWP app process) and returns its process name.
    /// Falls back to the frame's own process name if no child is found.
    /// </summary>
    private static unsafe string GetUwpProcessName(HWND afwHwnd)
    {
        PInvoke.GetWindowThreadProcessId(afwHwnd, out uint afwPid);

        nint childWithDifferentPid = 0;

        WNDENUMPROC childCallback = (childHwnd, _) =>
        {
            PInvoke.GetWindowThreadProcessId(childHwnd, out uint childPid);
            if (childPid != afwPid && childPid != 0)
            {
                childWithDifferentPid = (nint)(IntPtr)childHwnd;
                return false; // stop enumeration
            }
            return true;
        };

        PInvoke.EnumChildWindows(afwHwnd, childCallback, default);

        if (childWithDifferentPid != 0)
        {
            var childHwnd = new HWND((void*)(IntPtr)childWithDifferentPid);
            PInvoke.GetWindowThreadProcessId(childHwnd, out uint childPid);
            return GetProcessName(childPid);
        }

        // Fallback: use the frame's own process name
        return GetProcessName(afwPid);
    }

    /// <summary>
    /// Gets the process executable file name (e.g. "chrome.exe") for the given PID.
    /// Returns "?" if the process cannot be opened or the name cannot be retrieved.
    /// </summary>
    private static unsafe string GetProcessName(uint pid)
    {
        if (pid == 0)
            return "?";

        var handle = PInvoke.OpenProcess(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            pid);

        if (handle.IsNull)
            return "?";

        try
        {
            // Use heap-allocated array to avoid stackalloc + fixed double-pin issue
            char[] nameBuffer = new char[1024];
            uint size = (uint)nameBuffer.Length;

            fixed (char* namePtr = nameBuffer)
            {
                var queryResult = PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, namePtr, &size);
                if (queryResult)
                    return Path.GetFileName(new string(namePtr, 0, (int)size));
            }

            return "?";
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }
}
