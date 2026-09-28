# STING Tools - remove the tester install. Your licence and project files are kept.
$dest = Join-Path $env:LOCALAPPDATA 'Planscape\STING-Tester\Plugin'
if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) { Write-Host "Close Revit first, then run this again." -ForegroundColor Yellow; exit 1 }
foreach ($v in 2025, 2026, 2027) {
    $file = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$v\StingTools.addin"
    if ((Test-Path $file) -and ((Get-Content $file -Raw) -match [regex]::Escape($dest))) {
        Remove-Item $file -Force
        if (Test-Path "$file.before-tester-kit") { Move-Item "$file.before-tester-kit" $file -Force; Write-Host "Revit ${v}: previous StingTools.addin restored" }
        else { Write-Host "Revit ${v}: unregistered" }
    }
}
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force; Write-Host "Removed $dest" }
Write-Host "Done. The licence in C:\ProgramData\Planscape\StingTools is left in place."
