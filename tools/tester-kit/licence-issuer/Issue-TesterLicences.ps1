# STING Tools - issue 90-day tester licences in one go.  FOR PLANSCAPE ONLY: never send
# this script, private.pem or issued-licenses.csv to a tester.
#
# 1. Fill testers.csv (Name,MachineCode) from the codes testers send you
#    (they get it by running Get-MachineCode.cmd from the tester kit).
# 2. Run from any folder:
#      .\Issue-TesterLicences.ps1 -Repo C:\Dev\STINGTOOLS -KeyDir C:\path\to\folder-with-private.pem
#    -Days defaults to 90. One StingTools.lic per tester lands in .\licences\<Name>\.
# 3. Send each tester their own StingTools.lic. A licence only works on the PC whose
#    code it was signed for.
param(
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][string]$KeyDir,
    [string]$Csv = (Join-Path $PSScriptRoot 'testers.csv'),
    [int]$Days = 90
)
$ErrorActionPreference = 'Stop'
$proj = Join-Path $Repo 'StingTools.LicenseIssuer\StingTools.LicenseIssuer.csproj'
if (-not (Test-Path $proj)) { throw "Licence issuer not found at $proj - is -Repo your STINGTOOLS checkout?" }
if (-not (Test-Path (Join-Path $KeyDir 'private.pem'))) { throw "private.pem not found in $KeyDir. It must be the key whose public half is in LicensePublicKey.cs; a new key would produce licences the plugin rejects." }
$rows = Import-Csv $Csv | Where-Object { $_.Name -and $_.MachineCode }
if (-not $rows) { throw "No testers in $Csv (columns: Name,MachineCode)." }

Write-Host "Building the issuer once..." -ForegroundColor Cyan
dotnet build $proj -c Release --nologo -v q | Out-Null
if ($LASTEXITCODE) { throw "Issuer build failed." }
$exe = Get-ChildItem (Join-Path $Repo 'StingTools.LicenseIssuer\bin\Release') -Recurse -Filter 'StingLicenseIssuer.exe' | Select-Object -First 1 -ExpandProperty FullName

$outRoot = Join-Path $PSScriptRoot 'licences'
Push-Location $KeyDir   # the issuer reads private.pem and appends issued-licenses.csv here
try {
    foreach ($r in $rows) {
        $safe = ($r.Name -replace '[^\w\- ]', '').Trim()
        $dir = Join-Path $outRoot $safe
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $out = Join-Path $dir 'StingTools.lic'
        & $exe issue --code $r.MachineCode.Trim() --name $r.Name --days $Days --out $out
        if (-not (Test-Path $out)) { Write-Host "  NOT issued for $($r.Name) - see the message above." -ForegroundColor Red }
    }
} finally { Pop-Location }
Write-Host ""
Write-Host "Licences are in $outRoot. The issue log is $KeyDir\issued-licenses.csv." -ForegroundColor Green
