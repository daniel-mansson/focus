# Pitfalls Research

**Domain:** Win32 window management — directional focus navigation CLI tool
**Researched:** 2026-02-26
**Confidence:** HIGH (majority of findings verified against official Microsoft documentation)

---

## Critical Pitfalls

### Pitfall 1: DPI Virtualization Corrupts Window Coordinates

**What goes wrong:**
When the process is not declared per-monitor DPI aware, Windows virtualizes coordinates returned by `GetWindowRect`. A window that is physically 800x600px at 150% DPI on a secondary monitor may be reported as ~533x400px (scaled to 96 DPI). Comparing coordinates across monitors with different DPI settings will produce completely wrong directional distances. A window visually to the right may score as "below" or "behind" the current window due to virtualized coordinate spaces.

**Why it happens:**
By default, .NET console apps have no DPI awareness manifest entry. Windows detects this and silently scales all coordinate values to 96 DPI logical units before returning them. The app never knows it is seeing virtualized coordinates. On single-monitor 100% DPI systems, the bug is invisible — it only surfaces on multi-monitor or high-DPI setups.

**How to avoid:**
Embed an application manifest declaring `PerMonitorV2` DPI awareness. For a .NET 8 console application, add an `app.manifest` file with the following content and reference it in the `.csproj`:

```xml
<!-- app.manifest -->
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">
        PerMonitorV2
      </dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

Use `DwmGetWindowAttribute` with `DWMWA_EXTENDED_FRAME_BOUNDS` rather than `GetWindowRect`. Unlike `GetWindowRect`, `DWMWA_EXTENDED_FRAME_BOUNDS` returns values in physical screen pixels and is not subject to DPI virtualization — this is the authoritative visible bound of a window.

**Warning signs:**
- Navigation works correctly in tests on the developer machine but fails on user machines with non-100% display scaling
- Windows on secondary monitors never get selected regardless of direction
- Distance calculations produce negative or zero results between visually adjacent windows

**Phase to address:**
Phase 1 — Core window enumeration and bounds retrieval. DPI awareness must be established before the first `GetWindowRect` call. The manifest must be present from day one; it cannot be added as a retrofit without retesting all coordinate logic.

---

### Pitfall 2: Cloaked Windows Appear Visible and Infiltrate Candidate Lists

**What goes wrong:**
`IsWindowVisible()` returns `TRUE` for cloaked windows. Windows 10/11 uses "cloaking" to make windows on other virtual desktops, certain UWP app frames, and shell-managed windows appear as if they were visible (they have `WS_VISIBLE` set, valid coordinates, and receive paint messages) while being invisible to the user. Iterating with `IsWindowVisible()` alone includes these windows in the navigation candidate set, causing the tool to try to focus windows on other virtual desktops or invisible shell frames — resulting in either a flash of the wrong window or a completely silent no-op.

**Why it happens:**
The Windows shell uses cloaking deliberately to prevent legacy apps from detecting virtual desktop changes. Cloaking was introduced in Windows 8/8.1 and is pervasive in Windows 10/11. Raymond Chen's documentation confirms: "a window is given all the trappings of visibility, without actually being presented to the user." `IsWindowVisible()` has no knowledge of DWM cloaking state.

**How to avoid:**
After passing `IsWindowVisible()`, additionally call `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))`. Reject any window where `cloaked != 0`. The three cloaking reasons to filter are:
- `DWM_CLOAKED_APP` (0x1): Cloaked by its owner application
- `DWM_CLOAKED_SHELL` (0x2): Cloaked by the shell (includes other virtual desktops)
- `DWM_CLOAKED_INHERITED` (0x4): Inherited from owner

All three should be rejected. The full filter condition for a "navigable" window is:
`IsWindowVisible(hwnd) AND DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED) == 0`

**Warning signs:**
- Navigation switches focus to a window that flashes in the taskbar but never actually appears on screen
- The tool selects a window that the user cannot see
- Behavior changes when virtual desktops are in use vs. not

**Phase to address:**
Phase 1 — Window enumeration and filtering. The cloaked-window check is part of the core filter pipeline and must be validated on a system with multiple virtual desktops enabled.

---

### Pitfall 3: SetForegroundWindow Silently Fails Due to Foreground Lock

**What goes wrong:**
`SetForegroundWindow()` returns `TRUE` (nonzero) even when it does not actually bring the window to the foreground. Instead, Windows flashes the target window's taskbar button. From the tool's perspective the call "succeeded," but the user's focus has not changed. This is the single most common source of "it works sometimes but not others" bugs in window management tools.

**Why it happens:**
Windows enforces a foreground lock: only processes that recently received user input or are directly related to the foreground process can set the foreground window. A CLI tool invoked via AutoHotkey hotkey is launched by the AutoHotkey process, which may or may not have foreground rights at the moment of invocation. Microsoft's documentation states: "It is possible for a process to be denied the right to set the foreground window even if it meets these conditions." The return value of `SetForegroundWindow` does not reliably indicate success.

**How to avoid:**
Use the established `SendInput` + ALT key workaround: simulate pressing and releasing the ALT key via `SendInput` before calling `SetForegroundWindow`. As documented in Microsoft's `LockSetForegroundWindow` remarks: "pressing the ALT key causes Windows itself to enable calls to `SetForegroundWindow`." This is the approach validated by the Aetopia bypass gist as "100% reliable" and noted in the project's own `PROJECT.md`.

The implementation order is:
1. `SendInput` — ALT key down (`VK_MENU`, `KEYEVENTF_EXTENDEDKEY`)
2. `SetForegroundWindow(target)`
3. `SendInput` — ALT key up (`VK_MENU`, `KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP`)

Do NOT use `keybd_event` — it is deprecated and has been reported to fail intermittently in production. Use `SendInput` exclusively.

Do NOT use `AttachThreadInput` — it can deadlock if the target thread has no message queue (console windows) or is across different desktops, and requires careful cleanup.

**Warning signs:**
- Focus switching works when the tool is run from a terminal manually but fails when invoked via AutoHotkey
- The target window's taskbar button blinks but focus does not move
- Inconsistent behavior between development machine and user machines

**Phase to address:**
Phase 1 — Focus activation. This must be validated under realistic AutoHotkey invocation conditions, not just manual CLI execution. Test with the hotkey chain: AHK → spawned .exe → `SetForegroundWindow`.

---

### Pitfall 4: GetWindowRect Includes Invisible 8px Shadow Borders on Windows 10/11

**What goes wrong:**
`GetWindowRect()` on Windows 10+ returns coordinates that include an invisible resize shadow border (typically ~8px on non-maximized sides). Two windows that appear visually adjacent will have overlapping ghost rectangles that are not visible to the user. Distance calculations using `GetWindowRect` will be systematically off, and windows will appear ~16px closer to each other than they visually are. On the right edge, this can cause the wrong window to be selected as "closest in direction" because the invisible border extends into the neighbor's visual space.

**Why it happens:**
Windows 10 kept the legacy resize border (`WS_THICKFRAME`) but made it invisible in DWM composition. `GetWindowRect` faithfully returns the full rectangle including the invisible border, because the border is still part of the window's logical geometry. `DWMWA_EXTENDED_FRAME_BOUNDS` was introduced specifically to return the visible bounds.

**How to avoid:**
Always use `DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &rect, sizeof(RECT))` for position and size of candidate windows. This returns the rendered, visible rectangle in physical screen coordinates. Always check the HRESULT return value; fall back to `GetWindowRect` only if `DwmGetWindowAttribute` fails (e.g., for windows with no DWM frame). Note from official docs: "Unlike the Window Rect, the DWM Extended Frame Bounds are not adjusted for DPI" — this is correct behavior when the process is properly declared per-monitor DPI aware.

**Warning signs:**
- Two windows side-by-side are not reliably distinguished: the algorithm sometimes picks the one further away
- Gap calculations between windows return negative values (windows "overlap" according to rects)
- Problems only appear with non-maximized windows; maximized windows are unaffected (they have no shadow border)

**Phase to address:**
Phase 1 — Core bounds retrieval. Use `DWMWA_EXTENDED_FRAME_BOUNDS` exclusively from the start. Never introduce `GetWindowRect` as a primary source of window geometry for navigation calculations.

---

### Pitfall 5: Window Filter Misses UWP/Modern App Host Windows

**What goes wrong:**
UWP and Windows Store apps show one window to the user but are represented by multiple HWNDs: the `ApplicationFrameWindow` (the visible host shell frame) and the actual `Windows.UI.Core.CoreWindow` (the app's content, usually a child). Simple filtering with `IsWindowVisible + !IsToolWindow` may return the content `CoreWindow` as a candidate while the correct navigable window is the `ApplicationFrameWindow`. Switching focus to the `CoreWindow` HWND directly can fail silently or produce unexpected behavior.

**Why it happens:**
UWP apps use a two-window architecture: the shell hosts a frame window (`ApplicationFrameWindow` class) that appears in the taskbar and Alt+Tab, while the actual app content renders in a child `CoreWindow`. `EnumWindows` only returns top-level windows, so it will find `ApplicationFrameWindow`. However, the `CoreWindow` may also surface in some enumeration scenarios. The filtering algorithm must replicate the taskbar's "visible + not tool + unowned or WS_EX_APPWINDOW" logic precisely.

**How to avoid:**
Use the Raymond Chen Alt+Tab algorithm as the canonical filter for "user-navigable" windows:
```
bool IsAltTabWindow(HWND hwnd) {
    if (!IsWindowVisible(hwnd)) return false;
    if (IsCloaked(hwnd)) return false;
    HWND root = GetAncestor(hwnd, GA_ROOTOWNER);
    HWND lastPopup = GetLastActivePopup(root);
    if (lastPopup != hwnd) return false;
    DWORD exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
    if (exStyle & WS_EX_TOOLWINDOW) return false;
    return true;
}
```
Additionally check that the window is not minimized (`IsIconic(hwnd) == FALSE`) and has non-zero dimensions from `DWMWA_EXTENDED_FRAME_BOUNDS`.

**Warning signs:**
- UWP apps like Calculator or Settings appear twice in debug enumeration output
- Attempting to focus a UWP window fails when the HWND class is `Windows.UI.Core.CoreWindow`
- Missing UWP apps from candidate list entirely

**Phase to address:**
Phase 1 — Window filtering and enumeration. Test explicitly with UWP apps (Calculator, Settings, Windows Terminal, Microsoft Store) during development.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Using `GetWindowRect` instead of `DWMWA_EXTENDED_FRAME_BOUNDS` | Simpler P/Invoke, no DWM dependency | 8px coordinate errors cause wrong window selection on Windows 10/11 | Never — `DWMWA_EXTENDED_FRAME_BOUNDS` is available from Vista |
| Using `keybd_event` instead of `SendInput` | Slightly simpler API | Intermittent failures in production; deprecated since Windows XP | Never — `SendInput` is the documented replacement |
| Skipping the cloaked-window check | Fewer API calls | Selects invisible windows on virtual desktops; confuses users | Never — this is required for Windows 10/11 correctness |
| Omitting the DPI awareness manifest | No manifest management | All coordinates virtualized on high-DPI systems; fails for large user base | Never on a distributed tool |
| Hard-coding the "Alt key press" workaround | Works today | May become unnecessary or break if Windows changes behavior | Acceptable short-term; add a version check comment to revisit |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| AutoHotkey invocation | Running the tool manually in a terminal and declaring "it works" | Test exclusively via AHK `Run` command with `A_ScriptHwnd` visible; the invocation path changes foreground rights |
| P/Invoke `DwmGetWindowAttribute` in C# | Declaring the return type as `void` and ignoring HRESULT | Declare as `int`, check result with `== 0` (S_OK) before trusting the output struct |
| P/Invoke `SendInput` ALT key | Using `keybd_event` (deprecated) or sending a VK that triggers a system menu (VK_F10) | Use `SendInput` with `VK_MENU` and `KEYEVENTF_EXTENDEDKEY`; the extended flag prevents the system menu from activating |
| Multi-monitor virtual screen | Assuming all coordinates are positive | Secondary monitors left of the primary have negative X coordinates in virtual screen space; bounding box math must handle signed integers |
| JSON config file path | Resolving config relative to `CWD` | Resolve relative to the executable's directory (`AppContext.BaseDirectory`); CWD is unpredictable when invoked by AHK |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Calling `GetWindowText` for all enumerated windows | Hangs for 2-5 seconds when a hung application is open | Only call `GetWindowText` for windows that pass all other filters and are needed for exclusion-list matching; set timeout or use `InternalGetWindowText` | Any session with a hung or unresponsive window |
| Calling `DwmGetWindowAttribute` without checking `IsWindowVisible` first | Unnecessary DWM calls for hundreds of invisible background windows | Apply cheap checks first: `IsWindowVisible` → `!IsIconic` → `!IsCloaked` → `DWMWA_EXTENDED_FRAME_BOUNDS` | Systems with many background processes (developer machines) |
| Creating a new process per hotkey invocation with slow startup | >100ms response time; noticeable lag for users | Use `PublishSingleFile` with `SelfContained=true` or Native AOT compilation to minimize .NET startup overhead | On machines without the .NET 8 runtime pre-installed if framework-dependent |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Injecting simulated ALT key globally without cleanup | Sticky ALT key state if the process crashes between key-down and key-up; menus can be unexpectedly activated in the foreground app | Use try/finally to always send ALT-up, even on exception; send ALT-up before `SetForegroundWindow`, not after |
| Accepting arbitrary window class names in exclude list config without validation | Config injection causing the app to reject all or no windows | Validate config values as simple strings; do not execute them or pass them to Win32 as format strings |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Selecting the wrong window due to off-axis bias in "closest" mode | User presses "right" and a window slightly below and to the right is selected instead of the one directly to the right | Implement configurable axis bias strategies as documented in PROJECT.md; default to "balanced" which penalizes off-axis distance |
| No feedback when no candidate window exists in direction | User presses hotkey, nothing happens, they don't know if the tool ran at all | Use exit code 1 (no candidate) so AutoHotkey can trigger a system beep; document this in README |
| Switching focus to minimized windows | User is surprised when a minimized window appears unexpectedly | Filter out `IsIconic(hwnd) == TRUE` windows from all candidate sets by default |
| Excluding the tool's own console window from navigation | If the tool somehow creates a visible window, it becomes a candidate | Add the current process HWND (if any) to the exclusion filter |

---

## "Looks Done But Isn't" Checklist

- [ ] **Window filtering:** Appears to work in single-monitor 100% DPI test — verify on 125%/150% DPI multi-monitor setup where invisible border + DPI effects combine
- [ ] **SetForegroundWindow:** Works when run from terminal — verify exclusively via AHK `Run` invocation; the launch path changes permission semantics
- [ ] **Virtual desktop filtering:** Works in basic tests — verify with 2+ virtual desktops active and windows spread across them; cloaked windows must be absent from candidates
- [ ] **UWP app navigation:** Works with Win32 apps — test explicitly with Calculator, Windows Terminal, and Settings (all UWP)
- [ ] **Config file loading:** Works from IDE — verify config is loaded from the executable directory, not the working directory
- [ ] **Exit codes:** Exits without error — verify exit code 0 on success, 1 on no-candidate, 2 on error are all exercised; AHK scripts depend on exit code for beep logic
- [ ] **Exclude list:** Empty exclude list works — verify apps named in exclude list are actually excluded and the match is case-insensitive and process-name-based

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| DPI virtualization baked into coordinate logic | HIGH | Add DPI awareness manifest, switch all coordinate reads to `DWMWA_EXTENDED_FRAME_BOUNDS`, retest all directional logic |
| GetWindowRect used instead of DWMWA_EXTENDED_FRAME_BOUNDS | LOW | Targeted swap of P/Invoke call; logic above it is unaffected |
| Cloaked windows included in candidates | LOW | Add one `DwmGetWindowAttribute` check per window in the filter pipeline |
| keybd_event used instead of SendInput | LOW | Direct API swap; call signature is similar |
| Config loaded from CWD instead of exe directory | LOW | Change one path resolution call |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| DPI virtualization (manifest + DWMWA_EXTENDED_FRAME_BOUNDS) | Phase 1 — Window enumeration | Test on 150% DPI secondary monitor; coordinate values must match visible position |
| Cloaked windows included | Phase 1 — Window filtering | Enable 2 virtual desktops; confirm windows on desktop 2 never appear as candidates while on desktop 1 |
| SetForegroundWindow silent failure | Phase 1 — Focus activation | Invoke exclusively via AHK hotkey; observe actual foreground change, not just return value |
| Invisible 8px borders in GetWindowRect | Phase 1 — Bounds retrieval | Log both `GetWindowRect` and `DWMWA_EXTENDED_FRAME_BOUNDS` for same window; confirm correct one is used |
| UWP window host/content HWND confusion | Phase 1 — Window filtering | Enumerate with debug flag, confirm only `ApplicationFrameWindow` class surfaces for UWP apps |
| GetWindowText hang on unresponsive windows | Phase 2 — Exclusion list matching | Open a deliberately hung app; confirm enumeration completes within 100ms |
| Config loaded from wrong directory | Phase 2 — Config system | Invoke from a different working directory; confirm config is still found |
| ALT key sticky state on crash | Phase 1 — Focus activation | Simulate crash between key-down and key-up; verify no stuck ALT state on resume |

---

## Sources

- [SetForegroundWindow function (winuser.h) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) — HIGH confidence (official docs)
- [Foreground activation permission is like love — Raymond Chen, The Old New Thing](https://devblogs.microsoft.com/oldnewthing/20090220-00/?p=19083) — HIGH confidence (official Microsoft blog)
- [Why does SetForegroundWindow immediately followed by GetForegroundWindow not return the same window? — Raymond Chen](https://devblogs.microsoft.com/oldnewthing/20161118-00/?p=94745) — HIGH confidence
- [Which windows appear in the Alt+Tab list? — Raymond Chen](https://devblogs.microsoft.com/oldnewthing/20071008-00/?p=24863) — HIGH confidence
- [How can I detect that my window has been suppressed from the screen by the shell? — Raymond Chen](https://devblogs.microsoft.com/oldnewthing/20200302-00/?p=103507) — HIGH confidence
- [DWMWINDOWATTRIBUTE (dwmapi.h) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) — HIGH confidence (official docs)
- [EnumWindows function (winuser.h) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows) — HIGH confidence (official docs)
- [High DPI Desktop Application Development on Windows — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows) — HIGH confidence (official docs)
- [Bypassing SetForegroundWindow restrictions — Aetopia GitHub Gist](https://gist.github.com/Aetopia/1581b40f00cc0cadc93a0e8ccb65dc8c) — MEDIUM confidence (community, corroborated by LockSetForegroundWindow docs)
- [Getting a window rectangle without the drop shadow — Cyotek](https://www.cyotek.com/blog/getting-a-window-rectangle-without-the-drop-shadow) — MEDIUM confidence (verified against DWMWA_EXTENDED_FRAME_BOUNDS docs)
- [SendInput hack to workaround SetForegroundWindow bug — PowerToys PR #1282](https://github.com/microsoft/PowerToys/pull/1282) — MEDIUM confidence (Microsoft project, but implementation-specific)
- [IsWindowVisible calls and win10 cloaked windows — Chromium Dev Group](https://groups.google.com/a/chromium.org/g/chromium-dev/c/ytxVuf9TIvM) — MEDIUM confidence (corroborated by Raymond Chen cloaking article)
- [GetWindowRect returns unexpected size with invisible borders — w3tutorials.net](https://www.w3tutorials.net/blog/getwindowrect-returns-a-size-including-invisible-borders/) — MEDIUM confidence (corroborated by official DWMWA_EXTENDED_FRAME_BOUNDS docs)

---

*Pitfalls research for: Win32 window management — directional focus navigation*
*Researched: 2026-02-26*
