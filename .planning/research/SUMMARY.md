# Project Research Summary

**Project:** windowfocusnavigation
**Domain:** Windows CLI tool — directional window focus navigation via Win32 API
**Researched:** 2026-02-26
**Confidence:** HIGH

## Executive Summary

This project is a stateless Windows CLI utility that implements directional focus navigation (left/right/up/down) using geometric window scoring. It is invoked per-hotkey — typically from AutoHotkey — and must complete in under 100ms. Research across comparable tools (i3, Hyprland, Komorebi, GlazeWM, AltSnap, right-window) confirms the core approach: enumerate top-level windows, filter to user-navigable candidates, score each candidate by geometric distance in the requested direction, and activate the winner. The tool's key differentiator is exposing multiple named scoring strategies and a verbose debug mode — neither of which any competitor offers today.

The recommended implementation is .NET 8 LTS + C# 12 with `System.CommandLine 2.0.3` for CLI parsing, `Microsoft.Windows.CsWin32 0.3.269` for Win32 P/Invoke source generation, and `System.Text.Json` (built-in) for config. The architecture is a clean sequential pipeline: enumerate → filter → resolve geometry → score → activate. The strategy pattern on the scoring layer keeps all three weighting algorithms independently testable without Win32 mocking. Build targets standard self-contained publish (`PublishSingleFile`) for simplicity; Native AOT is an available optimization path if startup benchmarks reveal a problem.

The single greatest risk area is the cluster of Win32 correctness requirements that all must be addressed in Phase 1: DPI-aware coordinates (embed `PerMonitorV2` manifest from day one), cloaked-window filtering (`DWMWA_CLOAKED` check alongside `IsWindowVisible`), accurate visible bounds (`DWMWA_EXTENDED_FRAME_BOUNDS` never `GetWindowRect`), and the `SendInput` ALT-key bypass for `SetForegroundWindow`. All five critical pitfalls identified in research map to Phase 1. Getting these right in the foundation prevents expensive retrofits later and ensures correctness on the hardware diversity of real user machines (mixed DPI, virtual desktops, UWP apps).

## Key Findings

### Recommended Stack

The stack is lean by design — the project constraint explicitly prohibits third-party native dependencies. Everything needed is available via Microsoft-owned packages or the .NET 8 in-box library. `CsWin32` eliminates the error-prone surface of hand-written P/Invoke for 12+ Win32 functions including complex structs. `System.CommandLine 2.0.3` reached stable (non-preview) status in February 2026 and is AOT-compatible, making it the right choice for a simple single-argument CLI. `System.Text.Json` handles the config file with no extra dependency and is 2-3x faster than Newtonsoft.

**Core technologies:**
- `.NET 8 LTS`: Runtime and SDK — LTS with security support through November 2026; project constraint; strongest AOT story in .NET
- `C# 12`: Language — ships with .NET 8 SDK; records, pattern matching, and source generators all GA
- `System.CommandLine 2.0.3`: CLI argument parsing, help text, exit codes — Microsoft's official CLI library, stable, AOT-capable, zero ceremony for a single-argument tool
- `Microsoft.Windows.CsWin32 0.3.269`: Win32 P/Invoke source generation — build-time-only, generates correct AOT-compatible declarations for all required Win32 APIs; replaces deprecated `dotnet/pinvoke`
- `System.Text.Json` (in-box): JSON config read/write — no extra dependency, AOT-safe with source generation, sufficient for a simple config POCO

**Key avoid:** `DllImport` (deprecated, not AOT-safe), `dotnet/pinvoke` (officially deprecated 2023), `Newtonsoft.Json` (not needed, not AOT-safe), background service / IHost (adds 50-100ms startup overhead and 5-15 MB for a stateless tool).

**Publish strategy:** Start with `dotnet publish -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true` (~40-80ms startup, well within 100ms budget). Native AOT publish is available but requires MSVC toolchain and yields diminishing returns — benchmark before committing to it.

### Expected Features

All P1 features are tightly scoped and form a coherent MVP. The feature dependency graph makes the ordering non-negotiable: enumeration must precede filtering, filtering must precede geometry resolution, geometry resolution must precede scoring. The Alt-key bypass for `SetForegroundWindow` is a non-optional correctness requirement, not optional polish. Multi-monitor support is near-zero-cost because `DWMWA_EXTENDED_FRAME_BOUNDS` returns virtual screen coordinates by default.

**Must have (table stakes — v1):**
- Four-direction navigation (left/right/up/down) — definition of the category, expected by all users
- Visible window filtering (hidden/minimized/cloaked) — cloaked check is mandatory for Windows 10/11 correctness
- Accurate visible bounds via `DWMWA_EXTENDED_FRAME_BOUNDS` — required for correct scoring, not optional
- `SetForegroundWindow` + `SendInput` ALT bypass — focus switch silently fails without this
- Multi-monitor support via virtual screen coordinates — near-zero cost, expected by modern users
- JSON config (strategy, wrap behavior, exclude list) — required for basic usability
- CLI direction argument + flag overrides — required for AutoHotkey integration
- Configurable wrap-around behavior (wrap / no-op / beep) — users have strong opinions; every competitor exposes this
- Meaningful exit codes (0/1/2) — required for hotkey script integration
- Silent by default, `--verbose` debug flag — hotkey-triggered tools must not produce visible output on success

**Should have (competitive differentiators):**
- Multiple named scoring strategies (`balanced`, `strong-axis-bias`, `closest-in-direction`) — no competitor exposes strategies; key differentiator for different physical layouts
- Verbose/debug mode with scored candidate list — no competitor exposes scoring internals; major DX win for troubleshooting

**Defer (v2+):**
- Native AOT binary — defer until startup latency measured as a real problem
- Virtual desktop awareness — IVirtualDesktopManager COM API is undocumented and brittle across Windows builds; documented pattern of breakage in Komorebi and bug.n
- Custom numeric weight parameters in config — named strategies cover common cases; raw weights are less user-friendly

**Anti-features (do not build):** GUI/system tray, background daemon, window tiling/layout management, focus-follows-mouse. All require persistent state or IPC that contradicts the stateless CLI design.

### Architecture Approach

The architecture is a strict sequential pipeline with no shared state between stages. Each stage operates on typed output from the previous: `List<HWND>` from enumeration → filtered `List<HWND>` → `List<WindowInfo>` records with bounds and center → `WindowInfo?` winner → HWND activation. Win32 calls are contained entirely within the `Native/`, `Windows/`, and `Focus/` layers. The scoring layer takes only `WindowInfo` records, making it fully unit-testable without P/Invoke mocking. The strategy pattern on `IScorer` means adding a new algorithm is one new class with no pipeline changes.

**Major components:**
1. `Program.cs` (Entry Point) — parse CLI args, load config, resolve overrides, orchestrate pipeline, set exit code
2. `Config/` (Config Loader) — load JSON from exe directory, apply defaults, surface validation errors; config resolution: hardcoded defaults → JSON file → CLI flag overrides
3. `Windows/` (Enumeration, Filter, Geometry, WindowInfo) — pure data acquisition pipeline; enumeration returns raw HWNDs, filter culls them, geometry resolves each surviving HWND to a typed `WindowInfo` record
4. `Scoring/` (IScorer + strategies + factory) — direction pre-filter then score; canonical formula: `score = D_primary + weight * D_perp`; lower score = better candidate
5. `Focus/FocusActivator.cs` — the only component that mutates system state: `SendInput(ALT down)` → `SetForegroundWindow(hwnd)` → `SendInput(ALT up)`
6. `Native/NativeMethods.cs` — all P/Invoke declarations in one file; rest of codebase contains no `DllImport`/`LibraryImport`
7. `Diagnostics/Logger.cs` — conditional verbose output to stderr; gated on `--verbose` flag

**Build order implied by dependencies:** Native interop first → Windows layer → Scoring layer → Focus layer → Config + CLI wiring → Diagnostics throughout.

### Critical Pitfalls

All five critical pitfalls must be addressed in Phase 1. Recovery costs are HIGH for DPI virtualization if retrofitted; LOW for the others. None of the five is acceptable to defer.

1. **DPI virtualization corrupts coordinates** — embed an `app.manifest` declaring `PerMonitorV2` DPI awareness from day one; use `DWMWA_EXTENDED_FRAME_BOUNDS` exclusively (it returns physical screen pixels, not DPI-scaled logical units); test on 125%/150% DPI secondary monitor before declaring any coordinate logic correct
2. **Cloaked windows appear visible** — after `IsWindowVisible`, call `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED)` and reject any non-zero value; test with 2+ virtual desktops active
3. **`SetForegroundWindow` silently fails** — simulate ALT keypress via `SendInput` (NOT `keybd_event`, which is deprecated) before calling `SetForegroundWindow`; test exclusively via AHK `Run` invocation, not from a terminal
4. **`GetWindowRect` includes invisible 8px shadow borders** — never use `GetWindowRect` as primary source of window geometry; `DWMWA_EXTENDED_FRAME_BOUNDS` only; fall back to `GetWindowRect` only if DWM attribute call fails (HRESULT check required)
5. **UWP/Store apps have dual HWND structure** — use the Raymond Chen Alt+Tab filter algorithm (`GetAncestor` + `GetLastActivePopup` + `WS_EX_TOOLWINDOW` check) as the canonical filter; test with Calculator, Settings, Windows Terminal, Microsoft Store

**Secondary pitfalls to address in Phase 2:**
- `GetWindowText` hangs on unresponsive windows — call only after all other filters pass, not during enumeration
- Config loaded from wrong directory — resolve from `AppContext.BaseDirectory`, not `CWD` (CWD is unpredictable from AHK)
- Negative X coordinates for monitors left of primary — bounding box math must handle signed integers

## Implications for Roadmap

Based on the feature dependency graph and pitfall-to-phase mapping from research, a three-phase structure is recommended:

### Phase 1: Win32 Foundation + Core Navigation Pipeline

**Rationale:** Every downstream feature depends on correct window enumeration, filtering, and focus activation. All 5 critical pitfalls are Phase 1 concerns. Building this correctly once eliminates the most expensive retrofit scenarios. The pipeline must be validated with real hotkey invocation (AutoHotkey) before adding any features on top of it.

**Delivers:** A working end-to-end focus switch for one direction (e.g., "right"), invokable from AHK, passing the full correctness checklist. This is the minimum proof-of-concept that validates the approach.

**Addresses (from FEATURES.md):**
- Window enumeration + filtering (visible, cloaked, minimized, UWP dual-HWND)
- Accurate bounds via `DWMWA_EXTENDED_FRAME_BOUNDS`
- Direction pre-filter + balanced scoring strategy (one working algorithm)
- `SetForegroundWindow` + `SendInput` ALT bypass
- Multi-monitor via virtual screen coordinates
- Exit codes (0/1/2)
- DPI awareness manifest

**Must avoid (from PITFALLS.md):**
- DPI manifest omission — embed `PerMonitorV2` from project creation
- `GetWindowRect` — never introduce it as a coordinate source
- `IsWindowVisible` alone — always pair with `DWMWA_CLOAKED` check
- `SetForegroundWindow` without ALT bypass — always use `SendInput` pattern
- `keybd_event` — use `SendInput` exclusively
- Testing from terminal only — validate via AHK `Run` invocation from the start

**Project structure to establish:** `Native/`, `Windows/`, `Scoring/` (BalancedScorer only), `Focus/`, `Diagnostics/` (stub)

### Phase 2: Full Feature Set + Config System

**Rationale:** Once the core pipeline is validated, add all remaining P1 features together. Config and CLI overrides are tightly coupled — they must land in the same phase. Exclude list matching is where the `GetWindowText` hang pitfall lives, so it gets addressed here. Verbose mode makes Phase 2 testing significantly faster.

**Delivers:** A fully-featured v1 tool with all four directions, JSON config, wrap-around behavior, exclude list, CLI flag overrides, verbose debug mode, and silent-by-default output.

**Addresses (from FEATURES.md):**
- All four directions (left/right/up/down)
- JSON config (strategy, wrap, exclude list) loaded from exe directory
- CLI direction argument + all flag overrides
- Configurable wrap-around (wrap / no-op / beep)
- Application exclude list by process name
- Silent on success / verbose/debug mode with scored candidates

**Uses (from STACK.md):**
- `System.CommandLine 2.0.3` for CLI argument parsing
- `System.Text.Json` for JSON config with source-generated serialization context
- `AppContext.BaseDirectory` for config path resolution

**Must avoid (from PITFALLS.md):**
- `GetWindowText` called during bulk enumeration — call only for windows that pass all filters and are needed for exclude-list matching
- Config resolved from `CWD` instead of exe directory

### Phase 3: Additional Scoring Strategies + Polish

**Rationale:** Additional strategies are differentiators, not table stakes. They should be added only after the balanced strategy is validated by users. The strategy pattern established in Phase 1 makes adding these a matter of one new class each. AOT compilation is an optional optimization path, not a v1 requirement.

**Delivers:** The two additional scoring strategies (`strong-axis-bias`, `closest-in-direction`), exclude list pattern expansion if needed (window class matching, wildcard/regex), and optionally a Native AOT publish target.

**Addresses (from FEATURES.md):**
- `strong-axis-bias` and `closest-in-direction` strategies (P2)
- Application exclude list by window class if process name matching proves insufficient for UWP shared hosts (P2)
- Expanded exclude list patterns (P2)

**Defers:**
- Virtual desktop awareness — IVirtualDesktopManager is undocumented, brittle, breaks across Windows builds; document limitation instead
- Custom numeric weight parameters — named strategies are more user-friendly; add only if user demand is strong

### Phase Ordering Rationale

- **Win32 correctness cannot be retrofitted cheaply.** DPI awareness in particular requires retesting all coordinate logic if added late. The pitfall research explicitly flags this as HIGH recovery cost. It must be established before any other code runs.
- **Feature dependencies are strictly ordered.** Enumeration must precede filtering; filtering must precede geometry; geometry must precede scoring. The architecture confirms this pipeline dependency — there is no way to build the scoring layer without the layers below it.
- **Config and CLI are intertwined.** Config resolution (hardcoded defaults → JSON → CLI overrides) is a single concern that must be implemented atomically. Splitting them across phases creates a temporary inconsistency in the override chain.
- **Strategies are additive and zero-risk.** The `IScorer` interface isolates them from the pipeline. Deferring `strong-axis-bias` and `closest-in-direction` to Phase 3 carries zero risk — they are one class each once the interface is established.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 1 (UWP window filtering):** The Raymond Chen Alt+Tab algorithm is documented but the exact behavior of UWP host/content HWND enumeration with `EnumWindows` in current Windows 11 builds warrants hands-on testing. Build a debug enumeration dump early.
- **Phase 1 (SetForegroundWindow bypass):** The `SendInput` + ALT pattern is well-documented but invocation from AutoHotkey specifically (vs. other launchers) should be validated early. The foreground lock behavior may differ by AHK version and system configuration.

Phases with standard patterns (skip research-phase):
- **Phase 2 (JSON config + CLI parsing):** `System.CommandLine 2.0.3` is well-documented. `System.Text.Json` config loading is a standard pattern. No novel research needed.
- **Phase 3 (additional scorer strategies):** All three strategies are variants of the same `D_primary + weight * D_perp` formula with different weight multipliers. The formula is established in i3wm/Hyprland/Awesome WM. Implementation is mechanical once Phase 1 scoring is working.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All core technologies are Microsoft-owned with official documentation. Version compatibility verified. CsWin32 AOT configuration sourced from GitHub discussion (MEDIUM) but corroborated by official Native AOT docs. |
| Features | MEDIUM-HIGH | Core navigation features verified across multiple official tool repos. Win32-specific nuances (DWMWA_CLOAKED, DWMWA_EXTENDED_FRAME_BOUNDS) from official Microsoft Learn docs. Virtual desktop API deliberately excluded (documented brittleness). |
| Architecture | HIGH | Win32 API documentation is official and current. Component patterns verified across multiple sources. Sequential pipeline and strategy pattern are standard, well-understood patterns. P/Invoke boundary isolation is industry-standard practice for this domain. |
| Pitfalls | HIGH | Majority of pitfall findings verified against official Microsoft documentation and Raymond Chen's The Old New Thing (authoritative Windows internals source). SendInput bypass corroborated by Microsoft PowerToys PR. |

**Overall confidence:** HIGH

### Gaps to Address

- **CsWin32 AOT mode configuration:** The `CsWin32RunAsBuildTask` + `allowMarshaling: false` combination is documented in a GitHub discussion rather than official docs. If AOT publish is pursued, validate this configuration empirically and add a build verification step.
- **`GetWindowText` hang mitigation:** The recommended approach (call only after all other filters) avoids the hang for the exclude list use case, but the exact timeout behavior with `InternalGetWindowText` is not formally documented. Test with a deliberately hung application during Phase 2 development.
- **AutoHotkey invocation foreground rights:** The `SendInput` ALT bypass is confirmed reliable, but AHK version differences (v1 vs v2) and the `A_ScriptHwnd` relationship to foreground lock permissions should be verified in the Phase 1 test environment before committing to the implementation.
- **Windows 11 24H2 UWP enumeration:** The Raymond Chen Alt+Tab algorithm dates from 2007. UWP app hosting has evolved. The filtering logic should be smoke-tested against current Windows 11 builds (24H2) during Phase 1, not assumed to work from documentation alone.

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn — EnumWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows)
- [Microsoft Learn — DwmGetWindowAttribute](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute)
- [Microsoft Learn — SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [Microsoft Learn — DWMWINDOWATTRIBUTE enum](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [Microsoft Learn — Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Microsoft Learn — System.CommandLine overview](https://learn.microsoft.com/en-us/dotnet/standard/commandline/)
- [Microsoft Learn — High DPI Desktop Application Development](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- [Microsoft Learn — Native interoperability best practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)
- [Microsoft Learn — Positioning Objects on Multiple Display Monitors](https://learn.microsoft.com/en-us/windows/win32/gdi/positioning-objects-on-multiple-display-monitors)
- [Raymond Chen — The Old New Thing (window cloaking)](https://devblogs.microsoft.com/oldnewthing/20200302-00/?p=103507)
- [Raymond Chen — Foreground activation permission is like love](https://devblogs.microsoft.com/oldnewthing/20090220-00/?p=19083)
- [Raymond Chen — Which windows appear in the Alt+Tab list?](https://devblogs.microsoft.com/oldnewthing/20071008-00/?p=24863)
- [Hyprland Dispatchers Wiki](https://wiki.hypr.land/Configuring/Dispatchers/) — official docs
- [i3 User's Guide](https://i3wm.org/docs/userguide.html) — official docs
- [Komorebi Focusing Windows docs](https://lgug2z.github.io/komorebi/usage/focusing-windows.html)
- [Microsoft.Windows.CsWin32 GitHub](https://github.com/microsoft/CsWin32) — version 0.3.269
- [NuGet: System.CommandLine 2.0.3](https://www.nuget.org/packages/System.CommandLine)

### Secondary (MEDIUM confidence)
- [right-window GitHub + issue #1](https://github.com/ntrrgc/right-window/issues/1) — directional algorithm edge cases and Z-order tiebreak frustration
- [AltSnap GitHub](https://github.com/RamonUnch/AltSnap) — feature parity and INI config precedent
- [GlazeWM GitHub](https://github.com/glzr-io/glazewm) — directional focus commands and wrap-around behavior
- [CsWin32 AOT support discussion #1169](https://github.com/microsoft/CsWin32/discussions/1169) — AOT configuration requirements
- [Aetopia — Bypassing SetForegroundWindow restrictions](https://gist.github.com/Aetopia/1581b40f00cc0cadc93a0e8ccb65dc8c) — SendInput bypass, corroborated by LockSetForegroundWindow docs
- [Microsoft PowerToys PR #1282](https://github.com/microsoft/PowerToys/pull/1282) — SendInput ALT bypass in production Microsoft code
- [Chromium Dev Group — cloaked windows](https://groups.google.com/a/chromium.org/g/chromium-dev/c/ytxVuf9TIvM) — corroborates Raymond Chen cloaking article

### Tertiary (LOW confidence)
- [GlazeWM cheatsheet](https://www.nulldocs.com/windows/glazewm-cheatsheet/) — feature summary only
- [Komorebi GitHub (architecture overview)](https://github.com/LGUG2Z/komorebi) — confirms CLI pattern, architecture not inspected in detail

---
*Research completed: 2026-02-26*
*Ready for roadmap: yes*
