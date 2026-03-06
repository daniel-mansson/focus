$ErrorActionPreference = "Stop"

$setupExe = "installer\output\Focus-Setup.exe"
$testDir = "$env:LOCALAPPDATA\FocusTest"
$logDir = "installer\output\logs"
$uninstaller = "$testDir\unins000.exe"
$regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Focus_is1"
$passed = 0
$failed = 0

function Test-Step {
    param([string]$Name, [scriptblock]$Check)
    try {
        $result = & $Check
        if ($result) {
            Write-Host "  PASS: $Name" -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host "  FAIL: $Name" -ForegroundColor Red
            $script:failed++
        }
    } catch {
        Write-Host "  FAIL: $Name ($_)" -ForegroundColor Red
        $script:failed++
    }
}

function Wait-ForUninstall {
    param([int]$TimeoutSeconds = 30)
    $elapsed = 0
    while ($elapsed -lt $TimeoutSeconds) {
        if (-not (Test-Path $regKey)) { return $true }
        Start-Sleep -Seconds 1
        $elapsed++
    }
    return $false
}

# --- Prerequisites ---
Write-Host "`n=== Prerequisites ===" -ForegroundColor Cyan

if (-not (Test-Path $setupExe)) {
    Write-Host "Focus-Setup.exe not found. Running build.ps1 first..." -ForegroundColor Yellow
    powershell -File build.ps1
    if (-not (Test-Path $setupExe)) {
        throw "Build failed - $setupExe not produced"
    }
}

# Clean previous test artifacts
if (Test-Path $regKey) {
    if (Test-Path $uninstaller) {
        Write-Host "Cleaning previous install..." -ForegroundColor Yellow
        Start-Process $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES" -Wait
        Wait-ForUninstall | Out-Null
    }
}
if (Test-Path $testDir) {
    Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

# Read expected version from csproj
[xml]$csproj = Get-Content "focus/focus.csproj"
$expectedVersion = (Select-Xml -Xml $csproj -XPath "//Version").Node.InnerText

Write-Host "Setup: $setupExe"
Write-Host "Test dir: $testDir"
Write-Host "Expected version: $expectedVersion"

# --- Test 1: Fresh Install ---
Write-Host "`n=== Test 1: Fresh Install (silent) ===" -ForegroundColor Cyan

$installLog = "$logDir\install.log"
$proc = Start-Process $setupExe -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /DIR=`"$testDir`" /LOG=`"$installLog`"" -Wait -PassThru
Test-Step "Installer exit code is 0" { $proc.ExitCode -eq 0 }
Test-Step "focus.exe exists in install dir" { Test-Path "$testDir\focus.exe" }
Test-Step "Uninstaller created" { Test-Path $uninstaller }
Test-Step "Install log created" { Test-Path $installLog }

# Registry checks
Test-Step "Add/Remove Programs entry exists" { Test-Path $regKey }
if (Test-Path $regKey) {
    $reg = Get-ItemProperty $regKey
    Test-Step "Display name is 'Focus'" { $reg.DisplayName -eq "Focus" }
    Test-Step "Version matches csproj ($expectedVersion)" { $reg.DisplayVersion -eq $expectedVersion }
    Test-Step "Publisher is 'Daniel'" { $reg.Publisher -eq "Daniel" }
    Test-Step "Install location recorded" { $reg.InstallLocation -like "*FocusTest*" }
}

# Verify daemon was NOT launched (skipifsilent)
Start-Sleep -Seconds 2
$daemonRunning = Get-Process -Name "focus" -ErrorAction SilentlyContinue
Test-Step "Daemon NOT launched (skipifsilent)" { $null -eq $daemonRunning }

# --- Test 2: Upgrade (reinstall over existing) ---
Write-Host "`n=== Test 2: Upgrade Install (silent) ===" -ForegroundColor Cyan

# Create a fake config file to verify it survives
$configDir = "$env:APPDATA\focus"
$configFile = "$configDir\config.json"
$configExisted = Test-Path $configFile
if (-not $configExisted) {
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    '{"test": true}' | Set-Content $configFile
}
$configHashBefore = (Get-FileHash $configFile).Hash

$upgradeLog = "$logDir\upgrade.log"
$proc = Start-Process $setupExe -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /DIR=`"$testDir`" /LOG=`"$upgradeLog`"" -Wait -PassThru
Test-Step "Upgrade exit code is 0" { $proc.ExitCode -eq 0 }
Test-Step "focus.exe still exists after upgrade" { Test-Path "$testDir\focus.exe" }

# Config preservation
Test-Step "config.json preserved after upgrade" { Test-Path $configFile }
if (Test-Path $configFile) {
    $configHashAfter = (Get-FileHash $configFile).Hash
    Test-Step "config.json content unchanged" { $configHashBefore -eq $configHashAfter }
}

# Registry still intact
Test-Step "Add/Remove Programs entry still exists" { Test-Path $regKey }

# --- Test 3: Uninstall ---
Write-Host "`n=== Test 3: Uninstall (silent) ===" -ForegroundColor Cyan

# Inno Setup uninstaller copies itself to temp and re-launches.
# Start-Process -Wait only waits for the initial process.
# We poll the registry key to detect when uninstall actually completes.
Start-Process $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES"
$uninstallComplete = Wait-ForUninstall -TimeoutSeconds 30

Test-Step "Uninstall completed (registry removed)" { $uninstallComplete }
Test-Step "focus.exe removed" { -not (Test-Path "$testDir\focus.exe") }
Test-Step "Uninstaller removed" { -not (Test-Path $uninstaller) }
Test-Step "Install directory removed or empty" {
    (-not (Test-Path $testDir)) -or ((Get-ChildItem $testDir -Force -ErrorAction SilentlyContinue).Count -eq 0)
}
Test-Step "config.json NOT deleted by uninstall" { Test-Path $configFile }

# --- Cleanup ---
if (-not $configExisted -and (Test-Path $configFile)) {
    Remove-Item $configFile -Force
    $remaining = Get-ChildItem $configDir -Force -ErrorAction SilentlyContinue
    if ($remaining.Count -eq 0) {
        Remove-Item $configDir -Force -ErrorAction SilentlyContinue
    }
}
if (Test-Path $testDir) {
    Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Summary ---
$total = $passed + $failed
Write-Host "`n=== Results ===" -ForegroundColor Cyan
Write-Host "Passed: $passed / $total" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Yellow" })
if ($failed -gt 0) {
    Write-Host "Failed: $failed / $total" -ForegroundColor Red
    exit 1
}
Write-Host "All tests passed." -ForegroundColor Green
