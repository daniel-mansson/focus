---
phase: quick
plan: 4
type: execute
wave: 1
depends_on: []
files_modified: [README.md, .github/workflows/release.yml]
autonomous: true
requirements: [prepare-github-public-release]
must_haves:
  truths:
    - "Visitor to the GitHub repo immediately understands what Focus does and how to install it"
    - "User can download Focus-Setup.exe directly from the GitHub Releases page"
    - "Pushing a version tag (e.g. v5.1) automatically builds and publishes a GitHub Release with the installer attached"
  artifacts:
    - path: "README.md"
      provides: "User-facing project README with install instructions, feature overview, usage guide"
      min_lines: 80
    - path: ".github/workflows/release.yml"
      provides: "GitHub Actions workflow that builds and creates a release on tag push"
  key_links:
    - from: ".github/workflows/release.yml"
      to: "build.ps1"
      via: "workflow runs build.ps1 to produce Focus-Setup.exe"
      pattern: "build\\.ps1"
    - from: ".github/workflows/release.yml"
      to: "GitHub Releases"
      via: "uploads Focus-Setup.exe as release asset on tag push"
      pattern: "softprops/action-gh-release|gh release create"
---

<objective>
Prepare the Focus GitHub repository for public release by creating a polished user-facing README and a GitHub Actions workflow that automatically builds and publishes installer releases when version tags are pushed.

Purpose: Users visiting the repo should immediately understand what Focus does, see how to install it, and be able to download the installer directly from GitHub Releases.
Output: README.md at repo root, .github/workflows/release.yml for automated release publishing.
</objective>

<execution_context>
@C:/Users/Daniel/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Daniel/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@SETUP.md
@build.ps1
@focus/focus.csproj
@installer/focus.iss
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create user-facing README.md</name>
  <files>README.md</files>
  <action>
Create a polished, user-facing README.md at the repo root. This replaces the developer-oriented SETUP.md as the primary landing page. SETUP.md stays as-is for developer/contributor reference.

Structure the README with these sections:

1. **Title and tagline** -- "Focus" with a one-line description: keyboard-driven directional window navigation for Windows, inspired by Hyprland's spatial switching.

2. **What it does** -- 3-5 bullet points covering the core value proposition:
   - Hold CAPSLOCK to see colored border overlays on nearby windows, press direction keys (arrows or WASD) to switch focus
   - Chain moves: keep holding CAPSLOCK and press multiple directions to navigate across your desktop
   - Window management: move (CAPS+TAB+direction), grow (CAPS+LSHIFT+direction), shrink (CAPS+LCTRL+direction) -- all grid-snapped
   - Number keys for direct window selection while overlays are visible
   - Also works as a stateless CLI (`focus left`) for scripting
   - System tray with settings UI for all configuration

3. **Installation** -- Two paths:
   - **Installer (recommended):** "Download Focus-Setup.exe from the [latest release](https://github.com/daniel-mansson/focus/releases/latest)." Mention it includes startup registration, no .NET runtime needed (self-contained), per-user install (no admin required for install itself).
   - **Build from source:** Brief pointer to SETUP.md for developers who want to build from source.

4. **Quick start** -- What happens after install:
   - Focus daemon starts automatically (runs in system tray)
   - Hold CAPSLOCK to see overlay previews, press arrow keys or WASD to navigate
   - Right-click the tray icon for settings, restart, or exit
   - Configuration stored in `%APPDATA%\focus\config.json`

5. **Hotkey reference** -- Clean table of all hotkeys:
   | Hotkey | Action |
   | CAPS + arrow/WASD | Navigate to window in direction |
   | CAPS + number | Jump to numbered window |
   | CAPS + TAB + direction | Move window by grid step |
   | CAPS + LSHIFT + direction | Grow window edge |
   | CAPS + LCTRL + direction | Shrink window edge |

6. **Configuration** -- Brief mention of config.json location, link to the six scoring strategies (balanced, strong-axis-bias, closest-in-direction, edge-matching, edge-proximity, axis-only) with one-line descriptions. Mention `focus --init-config` to create default config. Mention the Settings UI in the tray icon for GUI configuration.

7. **CLI usage** -- Show `focus <direction> [options]` with a few examples. Keep brief -- point to `focus --help` for full reference.

8. **Requirements** -- Windows 10 or later. No .NET runtime needed if using the installer. .NET 8 SDK needed only for building from source.

9. **License** -- "MIT" (we will add LICENSE file in the next task).

Tone: Direct, concise, no marketing fluff. Written for a technical user who wants to understand what this does and get it running fast. Use code blocks for commands. No badges or shields initially -- keep it clean.

Do NOT include: the full scoring strategy deep-dive (that's in SETUP.md), AutoHotkey integration details (daemon replaced that), build instructions (that's in SETUP.md), troubleshooting (keep for later or SETUP.md).
  </action>
  <verify>
    <automated>test -f README.md && wc -l README.md | awk '{if ($1 >= 80) print "PASS: " $1 " lines"; else print "FAIL: only " $1 " lines"}'</automated>
  </verify>
  <done>README.md exists at repo root with all sections listed above, reads well as a GitHub landing page, contains download link to GitHub Releases latest, and is at least 80 lines.</done>
</task>

<task type="auto">
  <name>Task 2: Create GitHub Actions release workflow and LICENSE</name>
  <files>.github/workflows/release.yml, LICENSE</files>
  <action>
**Part A: Create LICENSE file**

Create an MIT license file at the repo root. Copyright holder: "Daniel Mansson". Year: 2025-2026.

**Part B: Create .github/workflows/release.yml**

Create a GitHub Actions workflow that triggers on version tag pushes and automatically builds + publishes a GitHub Release with the installer attached.

Workflow specification:

```yaml
name: Release
on:
  push:
    tags:
      - 'v*'
```

Jobs:

1. **build-and-release** -- runs-on: `windows-latest`

Steps:
- Checkout code
- Setup .NET 8 SDK (`actions/setup-dotnet@v4` with dotnet-version 8.0.x)
- Install Inno Setup: use `chocolatey` to install `innosetup` (choco is pre-installed on windows-latest runners). After install, add Inno Setup to PATH: `echo "C:\Program Files (x86)\Inno Setup 6" >> $env:GITHUB_PATH`
- Run `build.ps1` (which does dotnet publish + ISCC compile)
- Extract version from tag: `$version = "${{ github.ref_name }}".TrimStart('v')`
- Create GitHub Release using `softprops/action-gh-release@v2`:
  - tag_name: from the push trigger (automatic)
  - name: `Focus ${{ github.ref_name }}`
  - body: generate release notes with a brief template: "## Download\n\nDownload **Focus-Setup.exe** below and run to install.\n\n## What's Changed\n\n" followed by `generate_release_notes: true` for automatic changelog
  - files: `installer/output/Focus-Setup.exe`
  - draft: false
  - prerelease: false

Important details:
- The workflow must use `shell: pwsh` for steps running PowerShell (build.ps1)
- The `build.ps1` script already handles the full build pipeline (dotnet publish + ISCC)
- Inno Setup install via choco: `choco install innosetup --yes --no-progress`
- Ensure the ISCC.exe path is added to PATH before running build.ps1
- Use `permissions: contents: write` on the job to allow release creation

Do NOT include: complex matrix builds, multiple OS targets, caching (keep it simple), code signing steps.
  </action>
  <verify>
    <automated>test -f .github/workflows/release.yml && test -f LICENSE && echo "PASS: both files exist" || echo "FAIL: missing files"</automated>
  </verify>
  <done>LICENSE file exists with MIT license. GitHub Actions workflow exists at .github/workflows/release.yml, triggers on v* tags, installs .NET 8 + Inno Setup on windows-latest, runs build.ps1, and publishes Focus-Setup.exe as a GitHub Release asset using softprops/action-gh-release.</done>
</task>

</tasks>

<verification>
- README.md is well-structured and reads naturally as a GitHub landing page
- README.md links to https://github.com/daniel-mansson/focus/releases/latest for downloads
- .github/workflows/release.yml has valid YAML syntax
- .github/workflows/release.yml triggers on v* tag push only
- .github/workflows/release.yml installs both .NET 8 and Inno Setup before building
- .github/workflows/release.yml uploads installer/output/Focus-Setup.exe as release asset
- LICENSE file contains MIT license text
</verification>

<success_criteria>
- A visitor to github.com/daniel-mansson/focus sees a clear README explaining what Focus does and how to install it
- The README points users to download Focus-Setup.exe from GitHub Releases
- Pushing a tag like `git tag v5.1 && git push origin v5.1` will trigger an automated build that produces a GitHub Release with the installer attached
- The repo has a proper MIT license
</success_criteria>

<output>
After completion, create `.planning/quick/4-prepare-github-for-public-release/4-SUMMARY.md`
</output>
