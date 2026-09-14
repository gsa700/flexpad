<#
.SYNOPSIS
    Install flexpad: copy the program to your per-user Programs folder, put
    settings in your Roaming profile, add a Start Menu shortcut and an
    Add/Remove Programs entry.

.DESCRIPTION
    Deliberately a readable script rather than a packaged binary. A PyInstaller
    .exe would need code signing to avoid antivirus false positives, and an
    unsigned one gets flagged on reputation alone - the exact reason small ham
    utilities look untrustworthy. You can read this file before running it.

    Layout after install:
      %LOCALAPPDATA%\Programs\flexpad\   the program (replaced on every install)
      %APPDATA%\flexpad\                 config.json, flexpad.log, ui_state.json

    Safe to re-run: it upgrades the program and never touches an existing
    config.json. A config.json found beside this script (from the days when
    flexpad ran in place) is moved to the settings folder once. No autostart:
    flexpad is a panel you open when you want it. Does not need admin rights.

.EXAMPLE
    .\install.ps1
#>

$ErrorActionPreference = 'Stop'

$Root       = $PSScriptRoot
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\flexpad'
$DataDir    = Join-Path $env:APPDATA 'flexpad'
$MinPython  = [version]'3.9'
$Shortcut   = Join-Path ([Environment]::GetFolderPath('Programs')) 'flexpad.lnk'
$Files      = @('flexpad.py', 'config.example.json', 'README.md', 'LICENSE', 'uninstall.ps1',
                'assets\flexpad.ico', 'assets\flexpad.png')

function Say([string]$m) { Write-Host "  $m" }
function Fail([string]$m) { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

Write-Host "`nflexpad installer`n"

# --- Python -----------------------------------------------------------------
$python = $null
foreach ($candidate in @('python', 'python3')) {
    $found = Get-Command $candidate -ErrorAction SilentlyContinue
    # The WindowsApps stub resolves but is not a real interpreter.
    if ($found -and $found.Source -notlike '*WindowsApps*') { $python = $found.Source; break }
}
if (-not $python) {
    $launcher = Get-Command py -ErrorAction SilentlyContinue
    if ($launcher) { $python = (& $launcher.Source -3 -c "import sys; print(sys.executable)") }
}
if (-not $python) { Fail "Python not found. Install Python 3.9+ and re-run." }

$verText = (& $python -c "import sys; print('%d.%d' % sys.version_info[:2])")
if ([version]$verText -lt $MinPython) { Fail "Python $verText found, need $MinPython or newer." }
Say "Python $verText at $python"

& $python -c "import tkinter" 2>$null
if ($LASTEXITCODE -ne 0) { Fail "tkinter is missing. Re-run the Python installer and tick 'tcl/tk and IDLE'." }

$pythonw = Join-Path (Split-Path $python -Parent) 'pythonw.exe'
if (-not (Test-Path $pythonw)) { Fail "pythonw.exe not found beside python.exe. A windowed launcher is required." }
Say "Windowed launcher: pythonw.exe"

# --- Stop a running copy so the program files can be replaced ---------------
Get-CimInstance Win32_Process -Filter "Name = 'pythonw.exe' OR Name = 'python.exe'" |
    Where-Object { $_.CommandLine -like '*flexpad.py*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Say "stopped running flexpad (pid $($_.ProcessId))" }

# --- Program files ----------------------------------------------------------
New-Item -ItemType Directory -Force $InstallDir | Out-Null
foreach ($f in $Files) {
    $dest = Join-Path $InstallDir $f
    New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
    Copy-Item (Join-Path $Root $f) $dest -Force
}
$icon = Join-Path $InstallDir 'assets\flexpad.ico'
# A leftover marker would make the installed copy keep its data beside itself.
$marker = Join-Path $InstallDir 'portable'
if (Test-Path $marker) { Remove-Item $marker -Force }
Say "program installed to $InstallDir"

# --- Settings ---------------------------------------------------------------
New-Item -ItemType Directory -Force $DataDir | Out-Null
$config = Join-Path $DataDir 'config.json'
$legacy = Join-Path $Root 'config.json'
if (Test-Path $config) {
    Say "config.json already exists in $DataDir - left untouched"
} elseif ((Test-Path $legacy) -and -not (Test-Path (Join-Path $Root 'portable'))) {
    Move-Item $legacy $config
    Say "moved your existing config.json from $Root to $DataDir"
} else {
    Copy-Item (Join-Path $Root 'config.example.json') $config
    Say "config.json created from the example in $DataDir"
}

# --- Start Menu shortcut ----------------------------------------------------
$appPy = Join-Path $InstallDir 'flexpad.py'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($Shortcut)
$lnk.TargetPath = $pythonw
$lnk.Arguments = "`"$appPy`""
$lnk.WorkingDirectory = $InstallDir
$lnk.Description = 'Programmable buttons for a FlexRadio'
$lnk.IconLocation = "$icon,0"
$lnk.Save()
Say "Start Menu shortcut: $Shortcut"

# --- Add/Remove Programs entry ----------------------------------------------
# Per-user (HKCU), so no admin rights. Lets you find and remove it the normal
# Windows way instead of remembering where the uninstall script is.
$version = (Select-String -Path (Join-Path $Root 'flexpad.py') -Pattern '^__version__\s*=\s*"([^"]+)"' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -First 1)
if (-not $version) { $version = '0.0.0' }
$size = [int]((Get-ChildItem $InstallDir -File | Measure-Object Length -Sum).Sum / 1KB)
$arp = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\flexpad'
New-Item -Path $arp -Force | Out-Null
Set-ItemProperty -Path $arp -Name DisplayName -Value 'flexpad'
Set-ItemProperty -Path $arp -Name DisplayVersion -Value $version
Set-ItemProperty -Path $arp -Name Publisher -Value 'flexpad project'
Set-ItemProperty -Path $arp -Name InstallLocation -Value $InstallDir
Set-ItemProperty -Path $arp -Name DisplayIcon -Value $icon
Set-ItemProperty -Path $arp -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'uninstall.ps1')`""
Set-ItemProperty -Path $arp -Name EstimatedSize -Value $size -Type DWord
Set-ItemProperty -Path $arp -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $arp -Name NoRepair -Value 1 -Type DWord
Say "Registered in Add/Remove Programs (v$version)"

Write-Host "`nDone. Launch flexpad from the Start Menu.`n"
Write-Host "Settings: $config"
Write-Host "To remove:  .\uninstall.ps1   (add -Purge to delete your settings too)`n"
