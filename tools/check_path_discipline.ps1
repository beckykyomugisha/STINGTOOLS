<#
.SYNOPSIS
    Path-discipline gate -- no NEW hand-built <projectDir>/_BIM_COORD sibling paths.

.DESCRIPTION
    Every StingTools project path should resolve through StingPaths / ProjectFolderEngine
    so the on-disk layout lives in one place. Historically ~40 call sites built
    Path.Combine(<projectDir>, "_BIM_COORD", ...) by hand, scattering metadata folders as
    siblings of the .rvt. WP6 migrates them onto StingPaths.Meta; this gate stops the count
    from growing again while that migration is in flight.

    It scans StingTools/**/*.cs for lines that combine a project-directory token
    (projDir / PathName / GetDirectoryName / docDir / projectDir) with the literal
    "_BIM_COORD", and compares the per-file count against tools/path_discipline_baseline.txt.

    Exit 0 when every file is at or below its baseline (migrating a file below baseline is
    fine -- lower its baseline line to keep the ratchet tight). Exit 1 on any file that
    exceeds its baseline or is not in the baseline at all, i.e. on NEW sprawl.

.NOTES
    ProjectFolderEngine.cs, StingPaths.cs and CoordStores.cs are exempt -- they are the
    resolvers that legitimately name these buckets.

.EXAMPLE
    pwsh tools/check_path_discipline.ps1
#>

[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrEmpty($scriptDir)) {
        Write-Error "Cannot determine repo root. Pass -RepoRoot <path> explicitly."
        exit 2
    }
    $RepoRoot = Split-Path -Parent $scriptDir
}

$srcRoot = Join-Path $RepoRoot 'StingTools'
if (-not (Test-Path $srcRoot)) {
    Write-Error "Source root not found: $srcRoot"
    exit 2
}

# Resolvers that legitimately name these buckets.
$exemptFiles = @(
    'Core/ProjectFolderEngine.cs',
    'Core/StingPaths.cs',
    'Core/CoordStores.cs'
)

$pattern = 'Path\.Combine\([^)]*(projDir|PathName|GetDirectoryName|docDir|projectDir)[^)]*,\s*"_BIM_COORD"'

# Live per-file counts.
$live = @{}
Get-ChildItem -Path $srcRoot -Recurse -Filter *.cs | ForEach-Object {
    # Path relative to StingTools/ (compute against $srcRoot so the prefix drops naturally).
    $rel = $_.FullName.Substring($srcRoot.Length).TrimStart('\', '/').Replace('\', '/')
    if ($exemptFiles -contains $rel) { return }
    $n = 0
    Select-String -Path $_.FullName -Pattern $pattern -AllMatches |
        ForEach-Object { $n += $_.Matches.Count }
    if ($n -gt 0) { $live[$rel] = [int]$n }
}

# Baseline: "relative/path.cs<TAB>count" lines.
$baselinePath = Join-Path $PSScriptRoot 'path_discipline_baseline.txt'
$baseline = @{}
if (Test-Path $baselinePath) {
    Get-Content $baselinePath | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
        $parts = $_ -split "`t"
        if ($parts.Count -ge 2) { $baseline[$parts[0].Trim()] = [int]$parts[1].Trim() }
    }
}

$violations = @()
foreach ($file in $live.Keys) {
    $liveN = $live[$file]
    $baseN = if ($baseline.ContainsKey($file)) { $baseline[$file] } else { 0 }
    if ($liveN -gt $baseN) {
        $violations += "  $file : $liveN sibling _BIM_COORD path(s), baseline $baseN"
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Path-discipline gate FAILED -- new hand-built _BIM_COORD sibling path(s):" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host ""
    Write-Host "Use StingPaths.Meta(doc, `"_BIM_COORD`", ...) instead of Path.Combine(<projDir>, `"_BIM_COORD`", ...)."
    exit 1
}

$total = ($live.Values | Measure-Object -Sum).Sum
if ($null -eq $total) { $total = 0 }
Write-Host "Path-discipline OK -- no new sibling _BIM_COORD paths ($total remaining across $($live.Count) file(s), all within baseline)."
exit 0
