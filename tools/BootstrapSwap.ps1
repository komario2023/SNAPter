# One-time helper: a new build waits in app\publish_new while the old Snapter.exe is still running (it is locked and cannot be replaced).
# This script waits until the old program is closed, then copies the new build over app\publish and writes the build fingerprint.
param([string]$Root, [int]$WaitPid = 0)
$ErrorActionPreference = 'Continue'
$new = Join-Path $Root 'app\publish_new'
$dst = Join-Path $Root 'app\publish'
$logDir = Join-Path $Root 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
$log = Join-Path $logDir 'bootstrap.log'

if ($WaitPid -gt 0) { try { Wait-Process -Id $WaitPid -ErrorAction SilentlyContinue } catch { } }

$done = $false
for ($i = 0; $i -lt 100 -and -not $done; $i++) {
    try {
        Copy-Item (Join-Path $new '*') $dst -Recurse -Force -ErrorAction Stop
        $done = $true
    } catch {
        ("{0} retry {1}: {2}" -f (Get-Date), $i, $_.Exception.Message) | Add-Content $log
        Start-Sleep -Milliseconds 300
    }
}
if ($done) {
    Remove-Item $new -Recurse -Force -ErrorAction SilentlyContinue
    ("{0} swapped the new build into app\publish" -f (Get-Date)) | Add-Content $log
} else {
    ("{0} could not swap the new build" -f (Get-Date)) | Add-Content $log
}
