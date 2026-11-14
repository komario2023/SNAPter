# Started by Snapter.exe itself when the sources are newer than the built program (see app\Services\AutoUpdate.cs).
# It waits until the old exe has gone, builds (a small window shows that), stores the fingerprint and starts the new SNAPter.
# If the build fails, the old version starts anyway and a message shows the end of the log.
param([string]$Root, [int]$WaitPid = 0, [string]$ArgLine = '', [string]$Lang = 'en')
$AppArgs = @()
if ($ArgLine) { $AppArgs = @($ArgLine -split '\|' | Where-Object { $_ }) }

$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$exe = Join-Path $Root 'app\publish\SNAPter.exe'
$logDir = Join-Path $Root 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
$log = Join-Path $logDir 'autobuild.log'

function Start-SNAPter {
    if (Test-Path $exe) {
        $a = @('--after-build') + $AppArgs
        Start-Process -FilePath $exe -ArgumentList $a -WorkingDirectory $Root
    }
}

if ($WaitPid -gt 0) {
    try { Wait-Process -Id $WaitPid -Timeout 20 -ErrorAction SilentlyContinue } catch { }
}

# the words come from the language of the program (config\i18n\<lang>.json); English is the fallback
$txt = @{
    'autobuild.text'   = "SNAPter: the sources changed, building the new version...`r`nIt starts by itself in a moment."
    'autobuild.failed' = 'The build failed. The previous version starts.'
    'autobuild.log'    = 'Full log:'
}
try {
    $langFile = Join-Path $Root ("config\i18n\{0}.json" -f $Lang)
    if (Test-Path $langFile) {
        $j = Get-Content $langFile -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($k in @($txt.Keys)) { $v = $j.PSObject.Properties[$k]; if ($v -and $v.Value) { $txt[$k] = [string]$v.Value } }
    }
} catch { }

# the small window: it only shows that something is happening
$form = New-Object System.Windows.Forms.Form
$form.Text = 'SNAPter'
$form.FormBorderStyle = 'FixedToolWindow'
$form.StartPosition = 'CenterScreen'
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.Size = New-Object System.Drawing.Size(430, 96)
$label = New-Object System.Windows.Forms.Label
$label.Text = $txt['autobuild.text']
$label.AutoSize = $false
$label.Dock = 'Fill'
$label.TextAlign = 'MiddleCenter'
$form.Controls.Add($label)
$form.Show()
[System.Windows.Forms.Application]::DoEvents()

$ok = $false
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    "dotnet was not found" | Set-Content $log
} else {
    $proj = Join-Path $Root 'app\Snapter.csproj'
    $out = Join-Path $Root 'app\publish'
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $dotnet.Source
    $psi.Arguments = "publish `"$proj`" -c Release -r win-x64 --self-contained false -nologo -v q -o `"$out`""
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $oem = [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage)   # dotnet writes in the console code page
    $psi.StandardOutputEncoding = $oem
    $psi.StandardErrorEncoding = $oem
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEndAsync()
    $stderr = $p.StandardError.ReadToEndAsync()
    while (-not $p.WaitForExit(120)) { [System.Windows.Forms.Application]::DoEvents() }
    ($stdout.Result + "`r`n" + $stderr.Result) | Set-Content $log -Encoding UTF8
    $ok = ($p.ExitCode -eq 0)
}

$form.Close()
if ($ok) {
    & (Join-Path $PSScriptRoot 'BuildStamp.ps1') -Root $Root -Write | Out-Null   # the fingerprint: the next start does not build again
} else {
    $tail = ''
    if (Test-Path $log) { $tail = (Get-Content $log -Tail 12) -join "`r`n" }
    [void][System.Windows.Forms.MessageBox]::Show(($txt['autobuild.failed'] + "`r`n`r`n" + $tail + "`r`n`r`n" + $txt['autobuild.log'] + ' ' + $log), 'SNAPter', 'OK', 'Warning')
}
Start-SNAPter
