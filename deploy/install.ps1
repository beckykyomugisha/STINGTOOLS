# STING Tools installer — writes a per-user .addin manifest for every
# installed Revit version, pointing at the CompiledPlugin folder that
# ships next to this script. No admin rights required.
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll  = Join-Path $here 'CompiledPlugin\StingTools.dll'

Write-Host "STING Tools installer" -ForegroundColor Cyan
Write-Host "---------------------"

if (-not (Test-Path $dll)) {
    Write-Host "ERROR: CompiledPlugin\StingTools.dll was not found next to this script." -ForegroundColor Red
    Write-Host "Make sure you extracted the WHOLE zip and kept the folder structure intact."
    exit 1
}

$template = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>STING Tools</Name>
    <Assembly>__DLL__</Assembly>
    <AddInId>A1B2C3D4-5678-9ABC-DEF0-123456789ABC</AddInId>
    <FullClassName>StingTools.Core.StingToolsApp</FullClassName>
    <VendorId>Planscape</VendorId>
    <VendorDescription>Planscape - ISO 19650 BIM Automation</VendorDescription>
    <UseRevitContext>false</UseRevitContext>
  </AddIn>
</RevitAddIns>
"@
$addin = $template.Replace('__DLL__', $dll)

$installed = 0
foreach ($ver in '2025','2026','2027') {
    $revitApi = "C:\Program Files\Autodesk\Revit $ver\RevitAPI.dll"
    if (Test-Path $revitApi) {
        $addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$ver"
        New-Item -ItemType Directory -Force -Path $addinsDir | Out-Null

        # Remove any per-machine duplicate that would make Revit load STING twice.
        $machineDup = "C:\ProgramData\Autodesk\Revit\Addins\$ver\StingTools.addin"
        if (Test-Path $machineDup) {
            Remove-Item $machineDup -Force -ErrorAction SilentlyContinue
            Write-Host "Removed conflicting machine-wide copy for $ver" -ForegroundColor Yellow
        }

        Set-Content -LiteralPath (Join-Path $addinsDir 'StingTools.addin') -Value $addin -Encoding UTF8
        Write-Host ("Installed for Revit {0}  ->  {1}\StingTools.addin" -f $ver, $addinsDir) -ForegroundColor Green
        $installed++
    }
}

if ($installed -eq 0) {
    Write-Host "No Autodesk Revit 2025 / 2026 / 2027 install was detected on this PC." -ForegroundColor Yellow
    Write-Host "Install Revit first, then run this installer again."
    exit 1
}

Write-Host ""
Write-Host "DONE. Fully close Revit if it is open, then reopen it." -ForegroundColor Cyan
Write-Host "STING dockable panels appear on the right; a 'STING Tools' ribbon tab is also added."
Write-Host "Plugin location: $here\CompiledPlugin   (do not move this folder after installing)"
