<#
.SYNOPSIS
    Install flexpad: config file and a Start Menu shortcut.

.DESCRIPTION
    Deliberately a readable script rather than a packaged binary. A PyInstaller
    .exe would need code signing to avoid antivirus false positives, and an
    unsigned one gets flagged on reputation alone - the exact reason small ham
    utilities look untrustworthy. You can read this file before running it.

    Safe to re-run. Does not require administrator rights. Never overwrites an
    existing config.json. No autostart: flexpad is a panel you open when you
    want it.

.EXAMPLE
    .\install.ps1
#>

$ErrorActionPreference = 'Stop'

$Root      = $PSScriptRoot
$AppPy     = Join-Path $Root 'flexpad.py'
$Config    = Join-Path $Root 'config.json'
$Example   = Join-Path $Root 'config.example.json'
$MinPython = [version]'3.9'
$Shortcut  = Join-Path ([Environment]::GetFolderPath('Programs')) 'flexpad.lnk'

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

# --- Config -----------------------------------------------------------------
if (Test-Path $Config) {
    Say "config.json already exists - left untouched"
} else {
    Copy-Item $Example $Config
    Say "config.json created from the example - edit it, or use the app"
}

# --- Start Menu shortcut ----------------------------------------------------
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($Shortcut)
$lnk.TargetPath = $pythonw
$lnk.Arguments = "`"$AppPy`""
$lnk.WorkingDirectory = $Root
$lnk.Description = 'Programmable buttons for a FlexRadio'
$lnk.Save()
Say "Start Menu shortcut: $Shortcut"

Write-Host "`nDone. Launch flexpad from the Start Menu, or:  pythonw flexpad.py`n"
