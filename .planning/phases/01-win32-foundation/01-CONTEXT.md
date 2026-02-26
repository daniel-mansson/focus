# Phase 1: Win32 Foundation - Context

**Gathered:** 2026-02-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Project scaffold, CsWin32 interop layer, window enumeration and filtering pipeline, DPI-aware coordinates, and a `focus --debug enumerate` command that prints all user-navigable windows. No directional navigation, no config system, no focus switching.

</domain>

<decisions>
## Implementation Decisions

### Debug enumerate output
- Aligned columnar table format (like `tasklist` / `docker ps`)
- Long window titles truncated at ~40 characters with ellipsis
- Windows sorted by Z-order (topmost first, matching EnumWindows natural order)
- Summary line at the end: window count and monitor count (e.g., "Found 12 windows on 2 monitors")
- UWP duplicate HWNDs noted in summary line (e.g., "Filtered 3 duplicate UWP HWNDs") rather than shown in the table

### Filtering behavior
- Strict Alt+Tab algorithm match: check WS_EX_TOOLWINDOW, WS_EX_APPWINDOW, owner chain — replicate exact Alt+Tab window set
- Top-level dialog boxes included if they appear in Alt+Tab (modal dialogs owned by a parent naturally excluded by the algorithm)
- Always-on-top (WS_EX_TOPMOST) windows included as normal navigation candidates, flagged in debug output

### Window info displayed
- Table columns: HWND, Process Name, Title (truncated ~40 chars), Bounds (L,T,R,B), Monitor index, Flags
- Process name included (e.g., "chrome.exe") — aids identification and previews Phase 3 exclude-by-process
- Monitor number column (1, 2, 3...) for instant multi-monitor validation
- Bounds as L,T,R,B matching Win32 RECT convention (raw from DWMWA_EXTENDED_FRAME_BOUNDS)
- Compact "Flags" column with single-character markers: "T" for topmost (extensible for future flags)

### Claude's Discretion
- Column widths and exact alignment approach
- Table border style (plain text, box-drawing characters, or minimal separators)
- HWND display format (hex vs decimal)
- Exact error messaging for edge cases (no windows found, API failures)

</decisions>

<specifics>
## Specific Ideas

- Table should feel like familiar CLI tools (`tasklist`, `docker ps`) — compact, scannable, aligned columns
- Summary line provides a quick sanity check without counting rows manually
- UWP dedup transparency: user should know filtering happened, not just see a clean list

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-win32-foundation*
*Context gathered: 2026-02-26*
