<#
.SYNOPSIS
    Uninstall flexpad: stop the app, remove the program folder, the Start
    Menu shortcut and the Add/Remove Programs entry.

.DESCRIPTION
    Removes everything install.ps1 created:

      - the running app, if any
      - %LOCALAPPDATA%\Programs\flexpad (the program)
      - the Start Menu shortcut
      - the Add/Remove Programs entry

    Your settings in %APPDATA%\flexpad are KEPT by default because they hold
    your buttons, which took effort to write. Pass -Purge to delete them too.
    Python itself is untouched. Works whether run from the source folder or
    from the installed copy.

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -Purge
#>

param([switch]$Purge)

$ErrorActionPreference = 'Stop'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\flexpad'
$DataDir    = Join-Path $env:APPDATA 'flexpad'
$Shortcut   = Join-Path ([Environment]::GetFolderPath('Programs')) 'flexpad.lnk'
$Arp        = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\flexpad'

function Say([string]$m) { Write-Host "  $m" }

Write-Host "`nflexpad uninstaller`n"

# Stop a running copy so files aren't held open.
Get-CimInstance Win32_Process -Filter "Name = 'pythonw.exe' OR Name = 'python.exe'" |
    Where-Object { $_.CommandLine -like '*flexpad.py*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Say "stopped running flexpad (pid $($_.ProcessId))" }

if (Test-Path $Shortcut) { Remove-Item $Shortcut -Force; Say "removed Start Menu shortcut" }
if (Test-Path $Arp) { Remove-Item $Arp -Recurse -Force; Say "removed Add/Remove Programs entry" }

if (Test-Path $InstallDir) {
    # PowerShell has already read this script into memory, so the folder can
    # go even when this file is inside it.
    Remove-Item $InstallDir -Recurse -Force
    Say "removed $InstallDir"
}

if ($Purge) {
    if (Test-Path $DataDir) { Remove-Item $DataDir -Recurse -Force; Say "removed settings in $DataDir (-Purge)" }
} else {
    Say "settings kept in $DataDir (use -Purge to remove them)"
}

Write-Host "`nDone.`n"
