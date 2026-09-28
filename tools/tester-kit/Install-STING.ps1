# STING Tools - tester install (per user, no administrator rights needed).
#
# Copies the plugin to %LOCALAPPDATA%\Planscape\STING-Tester\Plugin and registers it
# with every installed Revit 2025 / 2026 / 2027 through a per-user StingTools.addin.
# Run it again to update: it replaces the plugin and keeps your licence and settings.
$ErrorActionPreference = 'Stop'
$kit     = Split-Path -Parent $MyInvocation.MyCommand.Path
$src     = Join-Path $kit 'Plugin'
$dest    = Join-Path $env:LOCALAPPDATA 'Planscape\STING-Tester\Plugin'
$addinId = 'A1B2C3D4-5678-9ABC-DEF0-123456789ABC'

function Say($m, $c = 'Gray') { Write-Host $m -ForegroundColor $c }

if (-not (Test-Path (Join-Path $src 'StingTools.dll'))) { Say "Plugin\StingTools.dll not found next to this script. Extract the whole ZIP first, then run the installer from the extracted folder." Red; exit 1 }

# Revit locks the DLLs while it runs, so a copy would half-fail.
while (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Say "Revit is running. Close every Revit window, then press Enter (or Ctrl+C to stop)." Yellow
    [void](Read-Host)
}

$versions = @(2025, 2026, 2027) | Where-Object {
    (Test-Path "C:\Program Files\Autodesk\Revit $_\Revit.exe") -or (Test-Path (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$_"))
}
if (-not $versions) { Say "No Revit 2025, 2026 or 2027 found on this PC. STING needs one of them." Red; exit 1 }

# A per-machine copy of the manifest would load STING twice.
foreach ($v in $versions) {
    $machine = "C:\ProgramData\Autodesk\Revit\Addins\$v\StingTools.addin"
    if (Test-Path $machine) { Say "Found a per-machine $machine. Revit would load STING twice. Ask IT to remove it (it needs administrator rights), then run this installer again." Red; exit 1 }
}

Say "Installing STING Tools to $dest ..." Cyan
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $src '*') $dest -Recurse -Force
# Files extracted from a downloaded ZIP are marked as coming from the internet;
# Revit refuses to load a blocked DLL.
Get-ChildItem $dest -Recurse -File | Unblock-File

$dll = Join-Path $dest 'StingTools.dll'
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>STING Tools</Name>
    <Assembly>$dll</Assembly>
    <AddInId>$addinId</AddInId>
    <FullClassName>StingTools.Core.StingToolsApp</FullClassName>
    <VendorId>Planscape</VendorId>
    <VendorDescription>Planscape - ISO 19650 BIM Automation</VendorDescription>
    <VendorEmail>support@planscape.app</VendorEmail>
    <UseRevitContext>false</UseRevitContext>
  </AddIn>
</RevitAddIns>
"@
foreach ($v in $versions) {
    $dir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $file = Join-Path $dir 'StingTools.addin'
    if (Test-Path $file) {
        $old = Get-Content $file -Raw
        if ($old -notmatch [regex]::Escape($dll)) { Copy-Item $file "$file.before-tester-kit" -Force; Say "  Revit ${v}: previous StingTools.addin kept as StingTools.addin.before-tester-kit" Yellow }
    }
    Set-Content -Path $file -Value $manifest -Encoding UTF8
    Say "  Revit ${v}: registered" Green
}

Say ""
Say "Installed. Next:" Cyan
Say "  1. If you have not sent your machine code yet, run Get-MachineCode.cmd and send the code to Planscape."
Say "  2. When you receive StingTools.lic, run Install-Licence.cmd (or paste it in Revit: STING > Activate)."
Say "  3. Start Revit. The STING panel is under View > User Interface if it is not already docked."
Say "  Log file (attach it to bug reports): $dest\StingTools_yyyyMMdd.log"
