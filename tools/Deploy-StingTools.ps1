<#
.SYNOPSIS
    Build StingTools and deploy it over the shared plugin target, recording what
    was deployed so the next launch can prove it.

.DESCRIPTION
    The deploy target is resolved from the .addin manifests every run, never
    hard-coded. It has moved before; a deploy that writes somewhere Revit is not
    loading from looks like a success and is worse than an error.

    Every installed Revit version shares one target, and so does every
    concurrent session working in this repo. This script therefore:

      1. Refuses while Revit or Planscape.Companion is running. The Companion
         holds a handle on the output directory, so a copy over it can half
         succeed and leave a mixed set of DLLs.
      2. Backs up the current deploy to CompiledPlugin.bak-<sha> before copying.
      3. Writes sting-deploy-stamp.json beside the DLL, recording branch,
         commit, build time and the DLL's SHA-256.

    Step 3 is what makes a later clobber detectable. Start-RevitLocal.ps1 checks
    the deployed DLL against that hash and refuses to launch when they differ.

    Deliberately pure ASCII, plain hyphens only - see StingDeploy.Common.ps1 for
    why. Do not tidy these into Unicode.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER SkipBuild
    Deploy whatever is already in bin\<Configuration> without rebuilding. The
    stamp still records the current branch and commit, so use this only when the
    existing output really was built from the current checkout.

.PARAMETER SkipBackup
    Do not create CompiledPlugin.bak-<sha>. Not recommended.

.PARAMETER Force
    Deploy even when Revit or the Companion is running. Expect a partial copy.

.EXAMPLE
    .\tools\Deploy-StingTools.ps1
    Build Release from the current branch, back up, deploy, stamp.

.EXAMPLE
    .\tools\Deploy-StingTools.ps1 -SkipBuild
    Deploy the existing Release output and stamp it.
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,
    [switch] $SkipBackup,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'StingDeploy.Common.ps1')

$repo = Split-Path $PSScriptRoot -Parent

# --- Resolve the real target -------------------------------------------------
$resolved = Resolve-StingDeployedAssembly
if (-not $resolved.Assembly) {
    throw "No StingTools.addin names a StingTools.dll. Install the addin manifest first."
}
$targetDll = $resolved.Assembly
$targetDir = Split-Path $targetDll -Parent

Write-Host ""
Write-Host "Target   : $targetDll"
Write-Host "From     : $($resolved.Manifests.Count) manifest(s)"
if ($resolved.Conflicting) {
    Write-Host "WARNING  : manifests disagree on the assembly; deploying to the first:" -ForegroundColor Yellow
    foreach ($t in $resolved.AllTargets) { Write-Host "    $t" }
}

# --- Refuse while anything holds the directory -------------------------------
$blockers = @()
$blockers += @(Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)
$blockers += @(Get-Process -Name 'Planscape.Companion' -ErrorAction SilentlyContinue)
if ($blockers.Count -gt 0 -and -not $Force) {
    Write-Host ""
    Write-Host "Cannot deploy - these hold the output directory:" -ForegroundColor Red
    foreach ($p in $blockers) { Write-Host "    $($p.ProcessName) (PID $($p.Id))" }
    Write-Host ""
    Write-Host "Close Revit and stop Planscape.Companion, then re-run."
    Write-Host "Deploying over a locked directory can half succeed and leave a mixed"
    Write-Host "set of DLLs, which is far harder to diagnose than a refusal."
    exit 1
}

# --- Identity for the stamp --------------------------------------------------
Push-Location $repo
try {
    $branch  = (git rev-parse --abbrev-ref HEAD).Trim()
    $commit  = (git rev-parse --short HEAD).Trim()
    $subject = (git log -1 --pretty=%s).Trim()
    $dirty   = [bool]((git status --porcelain) -ne $null -and (git status --porcelain).Length -gt 0)
} finally {
    Pop-Location
}

Write-Host "Branch   : $branch @ $commit"
Write-Host "Subject  : $subject"
if ($dirty) {
    Write-Host "WARNING  : worktree is DIRTY - the deploy will not match the commit." -ForegroundColor Yellow
}

# --- Build -------------------------------------------------------------------
$proj   = Join-Path $repo 'StingTools\StingTools.csproj'
$binDir = Join-Path $repo "StingTools\bin\$Configuration"

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Building $Configuration ..." -ForegroundColor Cyan
    & dotnet build $proj -c $Configuration -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE - nothing deployed." }
}
if (-not (Test-Path (Join-Path $binDir 'StingTools.dll'))) {
    throw "No StingTools.dll in $binDir. Build first, or drop -SkipBuild."
}

# --- Back up the current deploy ----------------------------------------------
if (-not $SkipBackup -and (Test-Path $targetDll)) {
    $existingTag = 'unknown'
    $existingStampPath = Get-StingStampPath -AssemblyPath $targetDll
    if (Test-Path $existingStampPath) {
        try { $existingTag = (Get-Content $existingStampPath -Raw | ConvertFrom-Json).commit } catch { }
    }
    $backup = "$targetDir.bak-$existingTag"
    if (Test-Path $backup) {
        $backup = "$backup-" + (Get-Date).ToString('yyyyMMddHHmmss')
    }
    Write-Host ""
    Write-Host "Backup   : $backup" -ForegroundColor Cyan
    Copy-Item -Path $targetDir -Destination $backup -Recurse -Force
}

# --- Copy --------------------------------------------------------------------
Write-Host "Deploying ..." -ForegroundColor Cyan
Copy-Item -Path (Join-Path $binDir '*') -Destination $targetDir -Recurse -Force

# --- Stamp -------------------------------------------------------------------
$stampPath = Write-StingDeployStamp -AssemblyPath $targetDll `
                                    -Branch $branch -Commit $commit `
                                    -CommitSubject $subject `
                                    -Configuration $Configuration `
                                    -DirtyWorktree $dirty

# --- Prove it ----------------------------------------------------------------
$verify = Test-StingDeployStamp
Write-Host ""
if ($verify.Status -eq 'Ok') {
    Write-Host "Deployed and stamped." -ForegroundColor Green
    Write-Host "  $branch @ $commit"
    Write-Host "  sha256 $($verify.Stamp.assemblySha256)"
    Write-Host "  stamp  $stampPath"
    Write-Host ""
    Write-Host "Start-RevitLocal.ps1 will now verify this on every launch, and refuse"
    Write-Host "if anything replaces it without going through this script."
} else {
    Write-Host "Deployed, but the verification re-read FAILED: $($verify.Status)" -ForegroundColor Red
    Write-Host $verify.Message -ForegroundColor Red
    exit 1
}
