<#
.SYNOPSIS
    Path-discipline gate -- project paths must resolve through the shared resolvers.

.DESCRIPTION
    Every StingTools project path should resolve through StingPaths / ProjectFolderEngine /
    CoordStores so the on-disk layout lives in one place. This gate enforces that in two
    tiers, because the two kinds of residue carry different risk.

    TIER 1 -- LEGACY BUCKET NAMES (hard zero).
      Any occurrence of the "STING_BIM_MANAGER" or "_bim_manager" literals outside the
      resolvers and the migration code. These are the pre-consolidation folder names; a
      hand-built path to one of them reads or writes the OLD location, so a writer here
      silently forks a store away from its readers. Allowed count: 0. No baseline.

    TIER 2 -- RAW-DIRECTORY "_BIM_COORD" CONSTRUCTIONS (ratcheted burn-down).
      Path.Combine(..., "_BIM_COORD", ...) whose BASE argument is a raw directory rather
      than a resolver result. These write <rvtDir>/_BIM_COORD -- the PRE-consolidation
      sibling -- so each one re-creates the folder MigrateFromLegacy just retired, and
      forks the store away from the consolidated <root>/_data/_BIM_COORD that readers
      fall back from. Ratcheted against tools/path_discipline_baseline.txt: the count may
      fall, never rise. The target is 0; the baseline is a burn-down, not an accepted state.

      A combine whose base IS a resolver (StingPaths.Meta / GetMetaPath / GetDataPath /
      GetRootPath) lands under _data and is CORRECT -- those are counted and reported
      separately, never baselined. Tier 2 previously counted both together and described
      them as "mostly land in the right place", which reported OK while 124 of 140 sites
      were writing the sibling. Both tiers now share one $resolverBase discriminator.

    WHY THIS WAS REWRITTEN
      The previous gate reported a clean ZERO baseline while ~10 legacy-bucket sites and
      ~139 hand-rolled _BIM_COORD sites were live in the tree. Its single regex

          Path\.Combine\([^)]*(projDir|PathName|GetDirectoryName|docDir|projectDir)[^)]*,\s*"_BIM_COORD"

      had three holes, all of which this version closes:
        1. It never mentioned STING_BIM_MANAGER or _bim_manager, so every legacy-bucket
           site was invisible to it by construction.
        2. [^)]* cannot cross a close-paren, so the common nested form
           Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD", ...) escaped.
        3. It required one of five hard-coded variable names in the same expression, so the
           dominant two-line idiom (assign the parent dir on one line, combine on the next)
           was missed entirely.
      Tier 2 therefore matches on the bucket literal appearing anywhere in a Path.Combine
      argument list, independent of how the base was spelled or named.

.NOTES
    Exempt: the resolvers that legitimately name these buckets, plus the migration code
    whose whole job is to find and drain the legacy folders.

.EXAMPLE
    pwsh tools/check_path_discipline.ps1
#>

[CmdletBinding()]
param(
    [string]$RepoRoot,
    # Emit a fresh Tier-2 baseline to stdout instead of checking. Use when a
    # legitimate migration lowers counts and the baseline should be re-tightened.
    [switch]$UpdateBaseline
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

# Resolvers that legitimately name these buckets, and the migration code that must
# name the legacy folders in order to drain them.
$exemptFiles = @(
    'Core/ProjectFolderEngine.cs',   # owns the layout + the legacy migration
    'Core/StingPaths.cs',            # bucket resolver
    'Core/CoordStores.cs',           # store resolver + legacy merge
    'Core/CoordLog.cs'               # coord-log resolver (legacy read fallbacks)
)

# TIER 1 -- legacy bucket names inside a hand-built path. Any hit outside the exempt
# set is a failure.
#
# Scoped to Path.Combine on purpose. Naming a legacy bucket is legitimate when it is
# handed to a RESOLVER -- GetMetaPath(doc, "STING_BIM_MANAGER") returns the bucket
# under _data, which is correct. What is never legitimate is assembling the path
# yourself, because the only base available at the call site is the model directory,
# which is the pre-consolidation sibling location.
$legacyPattern = 'Path\.Combine\(.*"(STING_BIM_MANAGER|_bim_manager)"'

# Bases that already resolve through the layout owner -- combining a bucket name onto
# one of these is the CORRECT idiom, not residue.
$resolverBase = '(StingPaths\.(Meta|Data|Cde|Staging|Recycle|Export|ExportFile)|GetMetaPath|GetDataPath|GetProjectDataDir|GetRootPath|GetFolderPath|GetExportFolder|GetStagingPath|GetRecyclePath)\s*\('

# Explicit per-line opt-out for the genuinely-legitimate cases: a read fallback that
# must look at the OLD location to find pre-consolidation data, or a last-resort
# branch taken only after a resolver has already failed. Marking a line requires
# saying so in the source, which keeps the exemption visible in review rather than
# buried in a file allowlist.
#     ... // path-discipline: legacy-fallback -- <why>
$allowMarker = 'path-discipline:\s*legacy-fallback'

# TIER 2 -- hand-rolled _BIM_COORD inside a Path.Combine argument list. Deliberately
# does NOT constrain how the base argument is spelled: that was hole #3.
$siblingPattern = 'Path\.Combine\(.*"_BIM_COORD"'

$legacyHits = @{}
$siblingHits = @{}
$resolvedHits = @{}

Get-ChildItem -Path $srcRoot -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $rel = $_.FullName.Substring($srcRoot.Length).TrimStart('\', '/').Replace('\', '/')
        if ($exemptFiles -contains $rel) { return }

        # A legacy bucket name combined onto a RESOLVER result is correct usage --
        # Path.Combine(GetMetaPath(doc, "STING_BIM_MANAGER"), "tasks.json") lands under
        # _data. Only a combine whose base is a raw directory is residue.
        # The marker is normally written as a comment on the preceding line(s), so
        # look at the match line plus a little context above it.
        $legacyN = 0
        Select-String -Path $_.FullName -Pattern $legacyPattern -AllMatches -Context 3,0 |
            Where-Object {
                $ctx = ($_.Context.PreContext -join "`n") + "`n" + $_.Line
                $_.Line -notmatch $resolverBase -and $ctx -notmatch $allowMarker
            } |
            ForEach-Object { $legacyN += $_.Matches.Count }
        if ($legacyN -gt 0) { $legacyHits[$rel] = [int]$legacyN }

        # Tier 2 applies the SAME base discriminator as Tier 1. A bucket name combined
        # onto a resolver result lands under _data and is correct; only a combine whose
        # base is a raw directory writes the pre-consolidation sibling. Counting both as
        # one number was what let the gate report OK with the fork live.
        $sibN = 0
        $okN = 0
        Select-String -Path $_.FullName -Pattern $siblingPattern -AllMatches -Context 3,0 |
            ForEach-Object {
                $ctx = ($_.Context.PreContext -join "`n") + "`n" + $_.Line
                if ($_.Line -match $resolverBase -or $ctx -match $allowMarker) {
                    $okN += $_.Matches.Count
                } else {
                    $sibN += $_.Matches.Count
                }
            }
        if ($sibN -gt 0) { $siblingHits[$rel] = [int]$sibN }
        if ($okN -gt 0) { $resolvedHits[$rel] = [int]$okN }
    }

if ($UpdateBaseline) {
    Write-Output "# Path-discipline baseline -- hand-rolled Path.Combine(..., `"_BIM_COORD`", ...) sites."
    Write-Output "# Format: <relative path under StingTools/><TAB><count>."
    Write-Output "# The count may FALL (lower the line in the same PR) but never rise."
    Write-Output "# Legacy bucket names (STING_BIM_MANAGER / _bim_manager) are NOT baselined --"
    Write-Output "# they are a hard zero outside the resolvers. See check_path_discipline.ps1."
    $siblingHits.Keys | Sort-Object | ForEach-Object { Write-Output ("{0}`t{1}" -f $_, $siblingHits[$_]) }
    exit 0
}

# -- Baseline (Tier 2 only) ---------------------------------------------------
$baselinePath = Join-Path $PSScriptRoot 'path_discipline_baseline.txt'
$baseline = @{}
if (Test-Path $baselinePath) {
    Get-Content $baselinePath | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
        $parts = $_ -split "`t"
        if ($parts.Count -ge 2) { $baseline[$parts[0].Trim()] = [int]$parts[1].Trim() }
    }
}

$failed = $false

# -- Tier 1 -------------------------------------------------------------------
if ($legacyHits.Count -gt 0) {
    $failed = $true
    $legacyTotal = ($legacyHits.Values | Measure-Object -Sum).Sum
    Write-Host "Path-discipline FAILED -- legacy bucket name(s) outside the resolvers ($legacyTotal occurrence(s)):" -ForegroundColor Red
    $legacyHits.Keys | Sort-Object | ForEach-Object {
        Write-Host ("  {0} : {1}" -f $_, $legacyHits[$_]) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host 'These name the PRE-consolidation folders. Reads see stale data; writes fork a'
    Write-Host 'store away from its readers. Resolve through CoordStores.<Store>(doc) or'
    Write-Host 'StingPaths.Meta(doc, ...) instead.'
    Write-Host ""
}

# -- Tier 2 -------------------------------------------------------------------
$violations = @()
foreach ($file in $siblingHits.Keys) {
    $liveN = $siblingHits[$file]
    $baseN = if ($baseline.ContainsKey($file)) { $baseline[$file] } else { 0 }
    if ($liveN -gt $baseN) {
        $violations += ("  {0} : {1} hand-rolled _BIM_COORD path(s), baseline {2}" -f $file, $liveN, $baseN)
    }
}

if ($violations.Count -gt 0) {
    $failed = $true
    Write-Host "Path-discipline FAILED -- new hand-rolled _BIM_COORD path(s):" -ForegroundColor Red
    $violations | Sort-Object | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host ""
    Write-Host 'Use StingPaths.Meta(doc, "_BIM_COORD", ...) or CoordStores.<Store>(doc)'
    Write-Host 'rather than building the path by hand.'
    Write-Host ""
}

if ($failed) { exit 1 }

$sibTotal = ($siblingHits.Values | Measure-Object -Sum).Sum
if ($null -eq $sibTotal) { $sibTotal = 0 }
$okTotal = ($resolvedHits.Values | Measure-Object -Sum).Sum
if ($null -eq $okTotal) { $okTotal = 0 }
Write-Host "Path-discipline OK."
Write-Host "  Tier 1 legacy bucket names outside resolvers   : 0"
Write-Host "  Tier 2 raw-dir _BIM_COORD sites (must reach 0) : $sibTotal across $($siblingHits.Count) file(s), all within baseline"
Write-Host "  _BIM_COORD sites resolving correctly           : $okTotal"
if ($sibTotal -gt 0) {
    Write-Host ""
    Write-Host "Note: the $sibTotal raw-dir site(s) write <rvtDir>/_BIM_COORD -- the PRE-consolidation"
    Write-Host "sibling -- not <root>/_data/_BIM_COORD. Each one re-creates the folder a consolidation"
    Write-Host "just retired. The baseline is a burn-down, not an accepted state."
}
exit 0
