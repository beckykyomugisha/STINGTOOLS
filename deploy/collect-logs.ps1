# Collects StingTools.log + the newest Revit journal(s) into a single zip
# on the Desktop. Send that zip back together with your screenshots.
$ErrorActionPreference = 'SilentlyContinue'
$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$log   = Join-Path $here 'CompiledPlugin\StingTools.log'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$out   = Join-Path ([Environment]::GetFolderPath('Desktop')) "STING_logs_$stamp"
New-Item -ItemType Directory -Force -Path $out | Out-Null

if (Test-Path $log) {
    Copy-Item $log $out
    Write-Host "Copied StingTools.log" -ForegroundColor Green
} else {
    Write-Host "No StingTools.log found yet — run a STING command in Revit first." -ForegroundColor Yellow
}

foreach ($ver in '2025','2026','2027') {
    $jdir = Join-Path $env:LOCALAPPDATA "Autodesk\Revit\Autodesk Revit $ver\Journals"
    if (Test-Path $jdir) {
        Get-ChildItem $jdir -Filter 'journal*.txt' |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 |
            ForEach-Object {
                Copy-Item $_.FullName (Join-Path $out ("Revit{0}_{1}" -f $ver, $_.Name))
                Write-Host "Copied newest Revit $ver journal" -ForegroundColor Green
            }
    }
}

$zip = "$out.zip"
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
Remove-Item $out -Recurse -Force
Write-Host ""
Write-Host "Logs zipped to:" -ForegroundColor Cyan
Write-Host "  $zip"
Write-Host "Send that file back along with your screenshots."
