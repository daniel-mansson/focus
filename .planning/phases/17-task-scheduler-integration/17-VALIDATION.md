---
phase: 17
slug: task-scheduler-integration
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-06
---

# Phase 17 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | Manual testing (installer + Task Scheduler) + PowerShell smoke script |
| **Config file** | none — Wave 0 creates test script |
| **Quick run command** | `schtasks /Query /TN "FocusDaemon" /V` |
| **Full suite command** | Build installer, run install, verify task, run uninstall, verify cleanup |
| **Estimated runtime** | ~60 seconds (manual install/uninstall cycle) |

---

## Sampling Rate

- **After every task commit:** Run `schtasks /Query /TN "FocusDaemon" /V` (if installed)
- **After every plan wave:** Full install/uninstall cycle with task verification
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 17-01-01 | 01 | 1 | SCHED-01 | manual + smoke | `schtasks /Query /TN "FocusDaemon"` (exit code 0) | N/A | ⬜ pending |
| 17-01-02 | 01 | 1 | SCHED-01 | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check PT0S) | N/A | ⬜ pending |
| 17-01-03 | 01 | 1 | SCHED-02 | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check RunLevel) | N/A | ⬜ pending |
| 17-01-04 | 01 | 1 | SCHED-02 | manual | `schtasks /Query /TN "FocusDaemon" /XML` (check LeastPrivilege) | N/A | ⬜ pending |
| 17-01-05 | 01 | 1 | SCHED-03 | manual | `schtasks /Query /TN "FocusDaemon"` (exit code non-zero after uninstall) | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `installer/test-scheduler.ps1` — PowerShell script to automate post-install verification: query task, verify XML fields, verify cleanup after uninstall
- [ ] Extend or reference existing `installer/test-installer.ps1` for build steps

*Note: Full automated testing of installer wizard pages requires interactive UI which cannot be automated in CI. The PowerShell script verifies post-install state via schtasks /Query.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Wizard checkbox page appears after dir select | SCHED-01 | Interactive UI cannot be scripted | Run installer, verify "Startup Options" page shows between dir select and progress |
| "Start at logon" checked by default | SCHED-01 | Wizard page defaults | Run fresh install, verify checkbox is pre-checked |
| "Run elevated" unchecked by default | SCHED-02 | Wizard page defaults | Run fresh install, verify checkbox is unchecked |
| UAC prompt when creating ONLOGON task | SCHED-02 | UAC dialog requires interactive approval | Run installer non-elevated, verify UAC prompt appears for schtasks |
| Upgrade pre-populates checkboxes | SCHED-01 | Requires existing task state | Install once, then run installer again, verify checkboxes match existing task |
| Uninstall UAC prompt for elevated task | SCHED-03 | UAC dialog requires interactive approval | Install with "Run elevated", then uninstall, verify UAC prompt for task deletion |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
