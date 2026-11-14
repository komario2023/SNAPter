# Used by "Start SNAPter.bat".
# Exit code 0: the built program is up to date.
# Exit code 1: it has to be (re)built (exe missing, or a source file is newer than the exe).
# Exit code 2: it has to be rebuilt, but this very exe is running now (it must be closed first).
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "app\publish\SNAPter.exe"

function Test-Running {
    $p = Get-Process SNAPter -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
    return [bool]$p
}

if (-not (Test-Path $exe)) { exit 1 }
$built = (Get-Item $exe).LastWriteTime
$sources = Get-ChildItem (Join-Path $root "app") -Recurse -File -Include *.cs, *.csproj, *.manifest |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|publish)\\' }
foreach ($f in $sources) {
    if ($f.LastWriteTime -gt $built) {
        if (Test-Running) { exit 2 }
        exit 1
    }
}
exit 0
