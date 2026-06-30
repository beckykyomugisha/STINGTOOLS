$ErrorActionPreference = 'SilentlyContinue'
$removed = 0
foreach ($ver in '2025','2026','2027') {
    foreach ($base in @("$env:APPDATA\Autodesk\Revit\Addins\$ver",
                        "C:\ProgramData\Autodesk\Revit\Addins\$ver")) {
        $f = Join-Path $base 'StingTools.addin'
        if (Test-Path $f) {
            Remove-Item $f -Force
            Write-Host "Removed: $f" -ForegroundColor Yellow
            $removed++
        }
    }
}
if ($removed -eq 0) { Write-Host "Nothing to remove — STING was not registered." }
else { Write-Host "`nDone. Restart Revit to unload the plugin." -ForegroundColor Cyan }
