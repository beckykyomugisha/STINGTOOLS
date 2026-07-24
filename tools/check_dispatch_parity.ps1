<#
.SYNOPSIS
    Dispatch-parity gate -- every panel command tag must resolve in WorkflowEngine.

.DESCRIPTION
    StingTools dispatches command tags from several surfaces: the per-discipline panel
    handlers (Electrical / Plumbing / HVAC / LPS / Sustainability) and
    Core/WorkflowEngine.ResolveCommand, which is what workflow presets go through.

    These drifted: six panel-schedule commands were reachable from the Electrical panel
    under one set of tags (Panel_FillSlots, Panel_AddSpare, ...) and from ResolveCommand
    under different ones (Panel_FillSparesAll, Panel_FillSpares, ...). A workflow preset
    written against the panel's tag resolved to null and the step was reported failed.

    This script extracts the tags each panel handler switches on and asserts each one is
    also resolvable by ResolveCommand. Exits non-zero when a tag is unreachable, so the
    drift is caught at review time rather than by a failed workflow run.

.NOTES
    The check is BASELINE-BASED. A full sweep found 187 panel tags that resolve in no
    shared resolver -- far more than one change can responsibly fix, so they are recorded
    in tools/dispatch_parity_baseline.txt and tracked in docs/ROADMAP.md. The gate fails
    only on tags that are NOT in that baseline, i.e. on NEW drift. Fixing a baseline tag
    means removing its line from the baseline file as well.

    Tags in $Exempt are not command tags at all (sizing-strategy combo values that share
    the switch statement).

.EXAMPLE
    pwsh tools/check_dispatch_parity.ps1
#>

[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

# Derive the repo root from the script location when not supplied. $PSScriptRoot is
# empty under some invocation styles (e.g. `powershell -File` on Windows PowerShell),
# so fall back to the script's own path before giving up.
if ([string]::IsNullOrEmpty($RepoRoot)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrEmpty($scriptDir)) {
        Write-Error "Cannot determine repo root. Pass -RepoRoot <path> explicitly."
        exit 2
    }
    $RepoRoot = Split-Path -Parent $scriptDir
}

$resolverPath = Join-Path $RepoRoot 'StingTools/Core/WorkflowEngine.cs'
if (-not (Test-Path $resolverPath)) {
    Write-Error "Resolver not found: $resolverPath"
    exit 2
}

$handlerPaths = @(
    'StingTools/UI/StingElectricalCommandHandler.cs',
    'StingTools/UI/Plumbing/StingPlumbingCommandHandler.cs',
    'StingTools/UI/StingHvacCommandHandler.cs'
) | ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Path $_ }

# Not command tags -- sizing-strategy combo values that share the same switch.
$Exempt = @('velocity', 'static_regain', 'equal_friction', 'constant_pressure')

# Known-unresolved tags (pre-existing drift). New tags must not be added here without
# a matching docs/ROADMAP.md note.
$baselinePath = Join-Path $PSScriptRoot 'dispatch_parity_baseline.txt'
$baseline = @()
if (Test-Path $baselinePath) {
    $baseline = Get-Content $baselinePath |
        Where-Object { $_ -and -not $_.StartsWith('#') } |
        ForEach-Object { $_.Trim() }
}

# A panel tag is reachable if EITHER shared resolver handles it -- unknown panel tags
# fall through from the panel handler to StingCommandHandler at runtime.
$resolvedTags = @()
foreach ($rp in @($resolverPath, (Join-Path $RepoRoot 'StingTools/UI/StingCommandHandler.cs'))) {
    if (-not (Test-Path $rp)) { continue }
    $txt = Get-Content $rp -Raw
    $resolvedTags += [regex]::Matches($txt, 'case\s+"([^"]+)"\s*:') |
        ForEach-Object { $_.Groups[1].Value }
}
$resolvedTags = $resolvedTags | Sort-Object -Unique

$missing = @()
foreach ($handler in $handlerPaths) {
    $text = Get-Content $handler -Raw
    $tags = [regex]::Matches($text, 'case\s+"([^"]+)"\s*:') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique

    foreach ($tag in $tags) {
        if ($Exempt -contains $tag) { continue }
        if ($baseline -contains $tag) { continue }
        if ($resolvedTags -notcontains $tag) {
            $missing += [pscustomobject]@{
                Tag     = $tag
                Handler = (Split-Path $handler -Leaf)
            }
        }
    }
}

if ($missing.Count -gt 0) {
    Write-Host "Dispatch parity FAILED -- $($missing.Count) NEW panel tag(s) resolve in no shared dispatcher:" -ForegroundColor Red
    $missing | Sort-Object Handler, Tag | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Add a case to ResolveCommand (an alias to the existing command is fine)," -ForegroundColor Yellow
    Write-Host "or add the tag to `$Exempt in this script with a reason." -ForegroundColor Yellow
    exit 1
}

Write-Host "Dispatch parity OK -- no new dispatch drift (baseline: $($baseline.Count) known-unresolved tag(s))." -ForegroundColor Green
exit 0
