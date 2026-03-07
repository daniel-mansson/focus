# test-scheduler.ps1
# Automated verification script for FocusDaemon Task Scheduler integration.
# Run after installing Focus to verify scheduled task state.
#
# Usage:
#   powershell -File installer\test-scheduler.ps1
#
# Requires: schtasks.exe (built into Windows)

param(
    [switch]$Cleanup  # If specified, deletes the FocusDaemon task at the end
)

$ErrorActionPreference = 'SilentlyContinue'
$PassCount = 0
$FailCount = 0

function Pass($msg) {
    Write-Host "PASS: $msg" -ForegroundColor Green
    $script:PassCount++
}

function Fail($msg) {
    Write-Host "FAIL: $msg" -ForegroundColor Red
    $script:FailCount++
}

function Info($msg) {
    Write-Host "INFO: $msg" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "=== FocusDaemon Task Scheduler Verification ===" -ForegroundColor White
Write-Host ""

# --- Test 1: Task exists ---
schtasks /Query /TN "FocusDaemon" 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    Pass "Task exists (FocusDaemon)"
} else {
    Fail "Task not found (FocusDaemon) -- was the installer run with 'Start at logon' checked?"
    Write-Host ""
    Write-Host "Skipping remaining tests since task does not exist." -ForegroundColor Yellow
    exit 1
}

# --- Retrieve task XML for detailed checks ---
$xml = schtasks /Query /TN "FocusDaemon" /XML 2>$null
if (-not $xml) {
    Fail "Could not retrieve task XML"
    exit 1
}

# --- Test 2: ExecutionTimeLimit is PT0S (no time limit) ---
if ($xml -match 'PT0S') {
    Pass "ExecutionTimeLimit is PT0S (no time limit -- daemon runs indefinitely)"
} else {
    Fail "ExecutionTimeLimit is NOT PT0S -- daemon will be killed by Task Scheduler after the default timeout"
}

# --- Test 3: LogonTrigger present ---
if ($xml -match 'LogonTrigger') {
    Pass "LogonTrigger present (daemon starts at logon)"
} else {
    Fail "LogonTrigger not found in task XML"
}

# --- Test 4: RunLevel ---
if ($xml -match 'HighestAvailable') {
    Info "RunLevel is HighestAvailable (elevated / admin)"
} elseif ($xml -match 'LeastPrivilege') {
    Info "RunLevel is LeastPrivilege (standard user)"
} else {
    Fail "RunLevel not found in task XML"
}

# --- Test 5: Command points to focus.exe ---
if ($xml -match 'focus\.exe') {
    Pass "Command contains focus.exe"
} else {
    Fail "Command does not reference focus.exe"
}

# --- Test 6: Arguments contain 'daemon --background' ---
if ($xml -match '<Arguments>daemon --background</Arguments>') {
    Pass "Arguments are 'daemon --background'"
} else {
    Fail "Arguments do not contain 'daemon --background'"
}

# --- Test 7: InteractiveToken LogonType ---
if ($xml -match 'InteractiveToken') {
    Pass "LogonType is InteractiveToken (runs in user's interactive session)"
} else {
    Fail "LogonType is not InteractiveToken"
}

# --- Test 8: MultipleInstancesPolicy is IgnoreNew ---
if ($xml -match 'IgnoreNew') {
    Pass "MultipleInstancesPolicy is IgnoreNew (prevents duplicate daemon instances)"
} else {
    Fail "MultipleInstancesPolicy is not IgnoreNew"
}

# --- Summary ---
Write-Host ""
Write-Host "=== Results: $PassCount passed, $FailCount failed ===" -ForegroundColor $(if ($FailCount -eq 0) { 'Green' } else { 'Yellow' })
Write-Host ""

# --- Cleanup (optional) ---
if ($Cleanup) {
    Write-Host "Deleting FocusDaemon task..." -ForegroundColor Yellow
    $confirm = Read-Host "Are you sure? (y/N)"
    if ($confirm -eq 'y' -or $confirm -eq 'Y') {
        schtasks /Delete /TN "FocusDaemon" /F 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Task deleted." -ForegroundColor Green
        } else {
            Write-Host "Failed to delete task (may require elevation). Try: schtasks /Delete /TN FocusDaemon /F" -ForegroundColor Red
        }
    } else {
        Write-Host "Cleanup cancelled." -ForegroundColor Cyan
    }
}

exit $FailCount
