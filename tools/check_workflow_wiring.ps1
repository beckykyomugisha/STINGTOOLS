<#
.SYNOPSIS
    Workflow-wiring gate -- every workflow step must name a command the engine can resolve.

.DESCRIPTION
    A workflow preset is data. Newtonsoft binds what it recognises and silently leaves the
    rest at its default, so a mis-keyed or mis-named step does not fail loudly -- it does
    nothing, and the run still reports success. This gate closes both ways that happened.

    TIER 1 -- STEPS KEYED "tag" INSTEAD OF "commandTag" (hard zero, no baseline).
      WorkflowStep binds [JsonProperty("commandTag")]. A step written {"tag": "..."} therefore
      deserialises with CommandTag == null, resolves to nothing, and is skipped. Eleven presets
      were written this way -- every step in each -- so each of those workflows executed ZERO
      steps while reporting success. The trap is easy to fall into because WorkflowStepResult,
      the OUTPUT record, legitimately serialises its tag as "tag". Allowed count: 0.

    TIER 2 -- commandTag ABSENT FROM ResolveCommand (hard zero + explicit baseline).
      WorkflowEngine.ResolveCommand maps a tag to an IExternalCommand. A tag with no case
      label resolves to null and the step is skipped -- same silent no-op as Tier 1, reached
      a different way. Most such tags were commands that existed and were reachable from a
      dock-panel button but had simply never been added to ResolveCommand.

      Baselined tags in tools/workflow_wiring_baseline.txt are the ones where NO command
      exists. They are NOT an accepted state: each is a step marked "optional": true with a
      label saying what is missing, and each is tracked in docs/ROADMAP.md. The baseline may
      shrink, never grow -- adding to it requires deciding, in review, that a tag genuinely
      has no command behind it.

    WHY THIS PARSES C# SOURCE TEXT
      The natural implementation -- reflect over ResolveCommand -- is not available. The test
      projects cannot reference StingTools.csproj because it needs the Revit API, which is not
      present on a CI runner and is not redistributable. Scanning the source for `case "..."`
      labels is therefore the honest mechanism, not a shortcut. Do NOT "improve" this into a
      compile-time reference or a unit test that loads the assembly: it cannot work.

.PARAMETER RepoRoot
    Repository root. Defaults to the parent of this script's directory.

.EXAMPLE
    pwsh tools/check_workflow_wiring.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not reliably bound inside a param default under -File, so resolve here
# (same shape as tools/check_path_discipline.ps1).
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrEmpty($scriptDir)) {
    Write-Error "Cannot determine script directory. Pass -RepoRoot <path> explicitly."
    exit 1
}
if ([string]::IsNullOrEmpty($RepoRoot)) { $RepoRoot = Split-Path -Parent $scriptDir }

$dataDir      = Join-Path $RepoRoot 'StingTools/Data'
$engine       = Join-Path $RepoRoot 'StingTools/Core/WorkflowEngine.cs'
$baselineFile = Join-Path $scriptDir 'workflow_wiring_baseline.txt'

if (-not (Test-Path $engine))  { Write-Host "Workflow-wiring FAILED -- WorkflowEngine.cs not found at $engine" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $dataDir)) { Write-Host "Workflow-wiring FAILED -- data directory not found at $dataDir" -ForegroundColor Red; exit 1 }

# ── Resolvable tags: every `case "..."` label in WorkflowEngine.cs. See .DESCRIPTION
#    for why this is a source-text scan and must stay one.
$engineText = Get-Content -Raw -Path $engine
$resolvable = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($m in [regex]::Matches($engineText, 'case\s+"([^"]+)"\s*:')) {
    [void]$resolvable.Add($m.Groups[1].Value)
}
if ($resolvable.Count -eq 0) {
    Write-Host "Workflow-wiring FAILED -- no case labels parsed from WorkflowEngine.cs." -ForegroundColor Red
    Write-Host "The switch shape changed; fix this gate rather than deleting it."
    exit 1
}

# ── Baseline: tags accepted as having no command behind them.
$baseline = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
if (Test-Path $baselineFile) {
    foreach ($line in Get-Content $baselineFile) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        [void]$baseline.Add(($t -split '\s')[0])
    }
}

$tierOne   = @()   # steps keyed "tag"
$tierTwo   = @()   # commandTag with no case label
$usedBase  = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$stepCount = 0
$files     = Get-ChildItem -Path $dataDir -Filter 'WORKFLOW_*.json' -File

foreach ($f in $files) {
    try   { $json = Get-Content -Raw -Path $f.FullName | ConvertFrom-Json }
    catch { Write-Host "Workflow-wiring FAILED -- $($f.Name) is not valid JSON: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }

    if ($null -eq $json.steps) { continue }
    $i = 0
    foreach ($step in $json.steps) {
        $i++; $stepCount++
        $names = @($step.PSObject.Properties.Name)
        if (($names -contains 'tag') -and -not ($names -contains 'commandTag')) {
            $tierOne += "$($f.Name) step $i : {""tag"": ""$($step.tag)""} -- WorkflowStep binds ""commandTag"""
            continue
        }
        $tag = $step.commandTag
        if ([string]::IsNullOrWhiteSpace($tag)) {
            $tierOne += "$($f.Name) step $i : no commandTag"
            continue
        }
        if (-not $resolvable.Contains($tag)) {
            if ($baseline.Contains($tag)) { [void]$usedBase.Add($tag) }
            else { $tierTwo += "$($f.Name) step $i : '$tag' has no case in WorkflowEngine.ResolveCommand" }
        }
    }
}

$failed = $false

if ($tierOne.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "Workflow-wiring FAILED -- Tier 1: $($tierOne.Count) step(s) the engine cannot see:" -ForegroundColor Red
    $tierOne | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host 'Rename the key to "commandTag". A step keyed "tag" deserialises to null and is'
    Write-Host 'skipped, so the workflow reports success having executed nothing.'
}

if ($tierTwo.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "Workflow-wiring FAILED -- Tier 2: $($tierTwo.Count) unresolvable commandTag(s):" -ForegroundColor Red
    $tierTwo | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host 'Either add a case to WorkflowEngine.ResolveCommand (the command usually already'
    Write-Host 'exists and is reachable from a dock-panel button), correct the tag in the preset,'
    Write-Host 'or -- only if no command exists -- mark the step "optional": true, say so in its'
    Write-Host 'label, add the tag to tools/workflow_wiring_baseline.txt and log it in docs/ROADMAP.md.'
}

if ($failed) { exit 1 }

$stale = @($baseline | Where-Object { -not $usedBase.Contains($_) })
Write-Host "Workflow-wiring OK."
Write-Host "  Presets scanned                                 : $($files.Count)"
Write-Host "  Steps scanned                                   : $stepCount"
Write-Host "  ResolveCommand case labels                      : $($resolvable.Count)"
Write-Host "  Tier 1 steps keyed ""tag"" (must be 0)            : 0"
Write-Host "  Tier 2 unresolvable outside the baseline        : 0"
Write-Host "  Baselined 'no command exists' tags in use       : $($usedBase.Count)"
if ($stale.Count -gt 0) {
    Write-Host ""
    Write-Host "Note: $($stale.Count) baseline entry/ies are no longer referenced by any preset:"
    $stale | Sort-Object | ForEach-Object { Write-Host "  $_" }
    Write-Host "Remove them from tools/workflow_wiring_baseline.txt -- the baseline may shrink, never grow."
}
exit 0
