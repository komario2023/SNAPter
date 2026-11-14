# The "fingerprint" of the sources: "<number of files>|<newest write time in ticks>". Program.cs (AutoUpdate.cs) computes the same text.
# Without -Write it prints the fingerprint; with -Write it stores it next to the built exe (app\publish\build.stamp).
param([string]$Root = (Split-Path -Parent $PSScriptRoot), [switch]$Write)

function Get-SourceStamp([string]$root) {
    $ext = '.cs', '.csproj', '.manifest', '.ico', '.resx'
    $files = Get-ChildItem (Join-Path $root 'app') -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $ext -contains $_.Extension.ToLowerInvariant() -and $_.FullName -notmatch '\\(obj|bin|publish)\\' }
    $max = 0L
    foreach ($f in $files) { $t = $f.LastWriteTimeUtc.Ticks; if ($t -gt $max) { $max = $t } }
    return ('{0}|{1}' -f @($files).Count, $max)
}

$stamp = Get-SourceStamp $Root
if ($Write) {
    $dir = Join-Path $Root 'app\publish'
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    [System.IO.File]::WriteAllText((Join-Path $dir 'build.stamp'), $stamp)
}
$stamp
