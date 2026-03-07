---
phase: 18
slug: settings-ui-startup-controls
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-07
---

# Phase 18 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | Manual testing + PowerShell verification (`schtasks`) |
| **Config file** | `installer/test-scheduler.ps1` (existing from Phase 17) |
| **Quick run command** | `schtasks /Query /TN "FocusDaemon" /V` |
| **Full suite command** | Open settings, toggle both checkboxes through all state combinations, verify task state |
| **Estimated runtime** | ~60 seconds (manual) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build` + open settings to verify controls appear
- **After every plan wave:** Full manual test matrix of all toggle combinations with `schtasks /Query` confirmation
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 18-01-01 | 01 | 1 | SETS-01 | manual | `schtasks /Query /TN "FocusDaemon"` (exit 0 = exists) | N/A | ⬜ pending |
| 18-01-02 | 01 | 1 | SETS-01 | manual | Toggle OFF, `schtasks /Query /TN "FocusDaemon"` (exit non-zero) | N/A | ⬜ pending |
| 18-01-03 | 01 | 1 | SETS-01 | manual | Open settings, compare checkbox to `schtasks /Query` output | N/A | ⬜ pending |
| 18-01-04 | 01 | 1 | SETS-01 | manual | Cancel UAC prompt, verify checkbox reverts | N/A | ⬜ pending |
| 18-01-05 | 01 | 1 | SETS-02 | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check HighestAvailable) | N/A | ⬜ pending |
| 18-01-06 | 01 | 1 | SETS-02 | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check LeastPrivilege) | N/A | ⬜ pending |
| 18-01-07 | 01 | 1 | SETS-02 | manual | Uncheck "Run at startup", verify elevation checkbox disabled | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. The `installer/test-scheduler.ps1` script from Phase 17 can verify task state after settings UI operations. No new test infrastructure needed since all tests are manual (WinForms UI interaction + UAC prompts cannot be automated).

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Toggle ON creates FocusDaemon task | SETS-01 | WinForms UI interaction + UAC prompt | Check "Run at startup", approve UAC, run `schtasks /Query /TN "FocusDaemon"` |
| Toggle OFF deletes FocusDaemon task | SETS-01 | WinForms UI interaction + UAC prompt | Uncheck "Run at startup", approve UAC, verify task gone |
| Checkbox reflects actual task state on open | SETS-01 | Requires opening settings form | Open settings, compare checkbox state to `schtasks /Query` output |
| UAC cancel reverts toggle silently | SETS-01 | Requires UAC interaction | Toggle checkbox, cancel UAC prompt, verify checkbox returns to previous state |
| Elevation toggle changes RunLevel | SETS-02 | WinForms UI + UAC + XML inspection | Check "Request elevated permissions", approve UAC, verify `/XML` shows HighestAvailable |
| Elevation unchecked changes RunLevel back | SETS-02 | WinForms UI + UAC + XML inspection | Uncheck elevation, approve UAC, verify `/XML` shows LeastPrivilege |
| Elevation checkbox disabled when startup unchecked | SETS-02 | WinForms UI state inspection | Uncheck "Run at startup", verify elevation checkbox is grayed out |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
