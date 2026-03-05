---
phase: 16
slug: build-pipeline-installer
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-05
---

# Phase 16 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | Manual testing + PowerShell smoke check |
| **Config file** | none |
| **Quick run command** | `powershell -File build.ps1` |
| **Full suite command** | Build + install + verify + uninstall cycle (manual) |
| **Estimated runtime** | ~30 seconds (build only); ~5 minutes (full manual cycle) |

---

## Sampling Rate

- **After every task commit:** Run `powershell -File build.ps1`
- **After every plan wave:** Full install/upgrade/uninstall cycle (manual)
- **Before `/gsd:verify-work`:** All 6 requirements manually verified
- **Max feedback latency:** 30 seconds (build); manual steps as needed

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 16-01-01 | 01 | 1 | PKG-01 | smoke | `powershell -File build.ps1 && Test-Path installer/output/Focus-Setup.exe` | ❌ W0 | ⬜ pending |
| 16-01-02 | 01 | 1 | INST-01 | manual-only | Run Focus-Setup.exe, verify files at install path, check Apps & Features | N/A | ⬜ pending |
| 16-01-03 | 01 | 1 | INST-02 | manual-only | Start daemon, run installer, verify daemon stops | N/A | ⬜ pending |
| 16-01-04 | 01 | 1 | INST-03 | manual-only | Install, modify config, reinstall, verify config unchanged | N/A | ⬜ pending |
| 16-01-05 | 01 | 1 | INST-04 | manual-only | Uninstall via Apps & Features, verify files removed | N/A | ⬜ pending |
| 16-01-06 | 01 | 1 | INST-05 | manual-only | Install with checkbox checked, verify daemon starts | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `installer/focus.iss` — Inno Setup script (core deliverable)
- [ ] `build.ps1` — Build orchestration script (core deliverable)
- [ ] `.gitignore` update — Add `installer/output/` entry
- [ ] Inno Setup 6.7.1 must be installed on build machine with ISCC.exe on PATH

*Note: Most requirements are manual-only validation. The build script itself is the primary automatable check.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Install to chosen directory with Add/Remove Programs entry | INST-01 | Interactive wizard requires user interaction | Run Focus-Setup.exe, verify files at chosen path, check Apps & Features |
| Stops running daemon before upgrade | INST-02 | Requires running daemon process | Start daemon, run installer, verify daemon stops before file replacement |
| Upgrade preserves config | INST-03 | Multi-step lifecycle test | Install, modify config.json, reinstall, verify config unchanged |
| Uninstall removes files and shortcuts | INST-04 | Interactive uninstall wizard | Uninstall via Apps & Features, verify all files and shortcuts removed |
| "Launch Focus now" checkbox works | INST-05 | Interactive finish page | Install with checkbox checked, verify daemon starts after wizard closes |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
