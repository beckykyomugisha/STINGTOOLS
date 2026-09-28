# STING Tools - install the StingTools.lic you received.
# Put StingTools.lic in this folder (or drag it onto Install-Licence.cmd), then run this.
param([string]$Path)
$kit = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Path) { $Path = Get-ChildItem $kit -Filter '*.lic' | Select-Object -First 1 -ExpandProperty FullName }
if (-not $Path -or -not (Test-Path $Path)) { Write-Host "No .lic file found. Put StingTools.lic in $kit and run this again." -ForegroundColor Red; exit 1 }
$dir = 'C:\ProgramData\Planscape\StingTools'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Copy-Item $Path (Join-Path $dir 'StingTools.lic') -Force
Write-Host "Licence installed to $dir\StingTools.lic" -ForegroundColor Green
Write-Host "Start (or restart) Revit. STING > Activate shows the expiry date. If it says the licence is for a different machine, re-run Get-MachineCode.cmd and send the code again."
