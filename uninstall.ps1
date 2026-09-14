<#
.SYNOPSIS
    Uninstall flexpad: stop the app, remove the Start Menu shortcut and logs.

.DESCRIPTION
    Removes what install.ps1 created plus the runtime leftovers:

      - the Start Menu shortcut
      - flexpad.log and its rotations
      - ui_state.json (window position)

    config.json is KEPT by default because it holds your buttons, which took
    effort to write. Pass -Purge to delete it too. Python itself is untouched.

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -Purge
#>

param([switch]$Purge)

$ErrorActionPreference = 'Stop'
$Root     = $PSScriptRoot
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'flexpad.lnk'

function Say([string]$m) { Write-Host "  $m" }

Write-Host "`nflexpad uninstaller`n"

# Stop a running copy so files aren't held open.
Get-CimInstance Win32_Process -Filter "Name = 'pythonw.exe' OR Name = 'python.exe'" |
    Where-Object { $_.CommandLine -like '*flexpad.py*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Say "stopped running flexpad (pid $($_.ProcessId))" }

if (Test-Path $Shortcut) { Remove-Item $Shortcut; Say "removed Start Menu shortcut" }

foreach ($f in @('flexpad.log', 'flexpad.log.1', 'flexpad.log.2', 'ui_state.json')) {
    $p = Join-Path $Root $f
    if (Test-Path $p) { Remove-Item $p; Say "removed $f" }
}

if ($Purge) {
    $cfg = Join-Path $Root 'config.json'
    if (Test-Path $cfg) { Remove-Item $cfg; Say "removed config.json (-Purge)" }
} else {
    Say "config.json kept (use -Purge to remove it)"
}

Write-Host "`nDone. The folder itself is yours to delete.`n"
