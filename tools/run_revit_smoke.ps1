<#
.SYNOPSIS
  Run the in-Revit drawing smoke tests (StingTools.Revit.SmokeTests, ROADMAP DRAW-1).

.DESCRIPTION
  Builds the plugin + smoke project against the chosen Revit version, then runs
  `dotnet test`, which (through ricaun.RevitTest) OPENS A NEW REVIT, runs the tests
  inside it on a model built from the metric template, and closes it again.

  Refuses to start while any Revit is running: the harness opens and closes Revit
  itself, and must never drive a session someone is working in.

  Results land in -ResultsDir (default: TestResults\revit-smoke\<timestamp>):
    smoke.trx          NUnit results (open in VS / any TRX viewer)
    smoke.log          full console output, including every engine warning
    summary.txt        Revit version, git commit, StingTools.dll hash, exit code

  Before believing a result, read HarnessIntegrityTests in summary/log: if Revit
  resolved a DIFFERENT StingTools.dll (the deployed add-in) that test fails and says so.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\run_revit_smoke.ps1
  powershell -ExecutionPolicy Bypass -File tools\run_revit_smoke.ps1 -RevitVersion 2026
#>
param(
    [ValidateSet('2025', '2026', '2027')]
    [string]$RevitVersion = '2025',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ResultsDir = ''
)

# 'Continue', not 'Stop': under Windows PowerShell 5.1, `native 2>&1` turns each stderr
# line into an ErrorRecord, and 'Stop' would abort the run on the first one even when
# the tool succeeds. Every native call's $LASTEXITCODE is checked explicitly instead.
$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'StingTools.Revit.SmokeTests\StingTools.Revit.SmokeTests.csproj'
$revitDir = "C:\Program Files\Autodesk\Revit $RevitVersion"

# -- 1. Never drive a Revit someone is using ---------------------------------
$running = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Revit is running (PID $($running.Id -join ', ')). Close every Revit window and re-run." -ForegroundColor Red
    Write-Host "The smoke harness opens and closes its own Revit; it will not attach to yours." -ForegroundColor Red
    exit 2
}

if (-not (Test-Path (Join-Path $revitDir 'RevitAPI.dll'))) {
    Write-Host "Revit $RevitVersion is not installed at '$revitDir'." -ForegroundColor Red
    exit 2
}

if ([string]::IsNullOrWhiteSpace($ResultsDir)) {
    $ResultsDir = Join-Path $repo ("TestResults\revit-smoke\" + (Get-Date -Format 'yyyyMMdd_HHmmss') + "_R$RevitVersion")
}
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
$log = Join-Path $ResultsDir 'smoke.log'
$summary = Join-Path $ResultsDir 'summary.txt'

# -- 2. Say which StingTools the installed add-in would load -----------------
# If Revit loads the deployed add-in, the CLR may hand the tests THAT StingTools.dll.
# HarnessIntegrityTests detects it; this just tells you in advance.
$addin = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\StingTools.addin"
$addinAssembly = '(no StingTools.addin for this version)'
if (Test-Path $addin) {
    $m = Select-String -Path $addin -Pattern '<Assembly>(.*)</Assembly>' | Select-Object -First 1
    if ($m) { $addinAssembly = $m.Matches[0].Groups[1].Value }
    Write-Host "Installed add-in loads: $addinAssembly" -ForegroundColor Yellow
    Write-Host "If HarnessIntegrityTests fails, move $addin aside for the run (or deploy this build)." -ForegroundColor Yellow
}

# -- 3. Build (plugin + smoke project) ----------------------------------------
# Only RevitVersion is passed: it selects which Revit ricaun opens. The API the code
# COMPILES against is left to resolve exactly as the shipped plugin's does (2025 first),
# because one plugin DLL built on the 2025 API is what 2026/2027 users load — and the
# plugin does not compile against the 2026 API at all.
$props = @("-p:RevitVersion=$RevitVersion")
Write-Host "Building $Configuration (tests will run in Revit $RevitVersion) ..."
& dotnet build $proj -c $Configuration @props -nologo -clp:Summary 2>&1 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed (exit $LASTEXITCODE) - see $log" -ForegroundColor Red
    exit $LASTEXITCODE
}

$dll = Join-Path $repo "StingTools.Revit.SmokeTests\bin\$Configuration\net8.0-windows\StingTools.dll"
$dllHash = if (Test-Path $dll) { (Get-FileHash $dll -Algorithm SHA256).Hash } else { '(missing)' }
$commit = (& git -C $repo rev-parse HEAD 2>$null)
$dirty = (& git -C $repo status --porcelain 2>$null | Measure-Object).Count

# -- 4. Run inside Revit -----------------------------------------------------
Write-Host "Running smoke tests inside Revit $RevitVersion (this opens and closes Revit) ..."
& dotnet test $proj -c $Configuration @props --no-build `
    --results-directory $ResultsDir `
    --logger "trx;LogFileName=smoke.trx" `
    --logger "console;verbosity=detailed" 2>&1 | Tee-Object -FilePath $log -Append
$testExit = $LASTEXITCODE

@(
    "StingTools Revit smoke run"
    "when          : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "revit         : $RevitVersion ($revitDir)"
    "configuration : $Configuration"
    "git commit    : $commit$(if ($dirty -gt 0) { " (+$dirty uncommitted change(s))" })"
    "StingTools.dll: $dll"
    "sha256        : $dllHash"
    "add-in points : $addinAssembly"
    "dotnet test   : exit $testExit ($(if ($testExit -eq 0) { 'no failures - inconclusive/skipped cases still need reading in smoke.trx' } else { 'FAILURES - read smoke.trx / smoke.log' }))"
    "results       : $ResultsDir"
) | Set-Content -Path $summary -Encoding utf8

Get-Content $summary | Write-Host
exit $testExit
