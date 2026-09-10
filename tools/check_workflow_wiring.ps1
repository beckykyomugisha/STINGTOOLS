<#
.SYNOPSIS
    Wiring gate -- data that names a command must name one that exists.
    Tiers 1-3 and 5 cover workflow presets; Tier 4 covers dock-panel buttons.

.DESCRIPTION
    A workflow preset is data, and so is a XAML button's Tag. Both name a command by
    string. Nothing checks that the string resolves, so a mis-keyed or mis-named entry
    does not fail loudly -- it does nothing. This gate closes every way that happened.

    Tier 4 lives here rather than in a sibling script because it is the same failure
    class and needs the same parsing: both tiers read `case "..."` labels out of C#
    source to learn which command names exist. Splitting them would duplicate that
    logic and add a second CI step for one idea.

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

    TIER 3 -- "order" DISAGREEING WITH ARRAY POSITION (hard zero, no baseline).
      WorkflowStep does not bind "order" either, so it is documentation that reads like
      configuration: 40 steps carry it and the engine ignores every one, executing in array
      position. Today all presets agree, so nothing runs out of sequence -- this tier exists
      to keep it that way. The failure it prevents is quiet and expensive: someone sorts a
      preset by "order" in an editor, or inserts a step and renumbers, and the file then
      states one sequence while the engine runs another with no error anywhere. Fix by
      reordering the array, renumbering "order", or deleting the key.

    TIER 5 -- STEP KEYS WorkflowStep DOES NOT BIND (explicit baseline). WF-4.
      Tier 1 catches ONE mis-keyed name. This catches the general case: any key an author
      writes into a step that the engine never reads. Newtonsoft ignores an unknown property
      silently, so the key sits in the file looking like configuration and does nothing --
      the same shape as Tier 1, without the single well-known spelling.

      THE BOUND SET IS DERIVED, NOT LISTED HERE. It is read out of WorkflowStep's own
      [JsonProperty("...")] attributes and property names in WorkflowEngine.cs, so a property
      added to the class is understood by this gate immediately and a list cannot rot. If that
      parse yields an implausibly small set the gate FAILS rather than reporting every key in
      every preset as unbound -- a check that finds everything broken is a broken check.

      Three keys are ALLOWED as documentation, not baselined: "order", "id" and "_notes"
      (40, 33 and 23 uses). They carry no execution intent, and "order" is already policed by
      Tier 3, which is the tier that makes it safe to leave in the files.

      Baselined in tools/workflow_step_key_baseline.txt are the four that DO express intent
      the engine has no support for -- skipIfFamilyLoaded, scheduleNameFilter, drawingTypeId,
      sheetNumberFilter -- across two penetration presets. They are not an accepted state:
      each has to be implemented in WorkflowStep or deleted from the preset. Implementing them
      is a Revit-behaviour decision, so they are made VISIBLE here rather than guessed at. The
      baseline may shrink, never grow.

    TIER 4 -- PANEL BUTTONS THAT DISPATCH TO NOTHING (hard zero + explicit baseline).
      A <Button Tag="X" Click="Cmd_Click"> in a panel XAML sends X to the dispatcher.
      If nothing handles X the click is a no-op, with no error and no log line.

      SCANS ALL SIX DOCKABLE PANELS: StingDockPanel, StingElectricalPanel, StingHvacPanel,
      Plumbing/StingPlumbingPanel, StingLpsPanel and Sustainability/StingSustainabilityPanel.
      Until 2026-08-08 only StingDockPanel.xaml was scanned, so ~377 buttons across the other
      five panels were ungated on the XAML side even though the handler side already read all
      six handlers. A panel in the list whose XAML or code-behind is missing FAILS the gate
      rather than being skipped -- a rename must be noticed, not absorbed.

      DISPATCH HAS THREE LAYERS and a check that knows about only one over-reports badly:
        L1  CommandRegistry -- StingTools/UI/Modules/*CommandModule.cs `registry.Register("X", ...)`,
            consulted FIRST by StingCommandHandler (`CommandRegistry.Instance.TryHandle`).
        L2  Code-behind suite runners -- StingDockPanel.xaml.cs Cmd_Click intercepts certain
            tags (`cmdTag == "X"`) and dispatches concrete tags itself, returning before the
            ExternalEvent path.
        L3  The `case "X":` switches in the six command handlers.
      Measured 2026-08-06: 26 button tags have no L3 case, and ALL 26 are reachable --
      23 via L2 runners, 3 via L1 registry. ZERO dock-panel buttons are dead. A one-layer
      check would report all 26 as broken; SILENT_BUTTONS_TODO.md records the same
      correction being needed once before (the "141 silent buttons" figure was ~96%
      false-positive for exactly this reason).

      SCOPE: only <Button> elements carrying Click="Cmd_Click". A naive scan of every
      Tag="..." in the XAML over-reports by ~177, because Tag also carries filter values,
      numerics and picker options on ComboBoxItem / TabItem controls that never dispatch.

      tools/button_wiring_baseline.txt is EMPTY and should stay that way.

    TIER 6 -- A BUTTON THE WORKFLOW ENGINE CANNOT CALL (explicit baseline).
      Tier 2 proves PRESET STEPS RESOLVE. It cannot prove a shipped command is reachable
      from a preset AT ALL, because a command in neither ResolveCommand nor any preset is
      invisible to it. Seven commands built during 2026-09 -- Materials_SetClass,
      Materials_StampCodes, Materials_RegisterAudit, Baseline_Audit, Baseline_Apply,
      Baseline_RenameTypes, Prod_CoverageAudit -- sat in exactly that gap for a month, so
      ProjectKickoff imported the material register, built host types from it, and never
      stamped a code, set a class or audited what it built, with no gate saying a word.

      This tier asks the other half: a Cmd_Click button tag with no ResolveCommand case is
      reported as NOT REACHABLE FROM A WORKFLOW. It is emphatically NOT a claim the button
      is broken -- Tier 4 already proves every one of them dispatches on a click.

      NOT A FAILURE BY DEFAULT, which is why it carries a baseline. Plenty of commands are
      legitimately interactive-only: they open a modeless window, they need a picked
      element, they are a dialog with no headless meaning. The baseline file lists the
      1,236 (of 1,681) that were already unreachable when this tier was written. It MAY
      SHRINK, NEVER GROW -- the whole value is that a NEW button forces a decision instead
      of a silence.

      A STALE ENTRY IS REPORTED, NOT FAILED. A tag leaving the list means somebody made it
      chainable, which is the outcome this tier wants; failing for it would punish the fix,
      and would break whichever of two independent PRs merged second. Note the asymmetry
      with Tier 5, whose baseline DOES fail on a stale entry -- there a stale line silently
      re-permits an unbound key, which is the opposite situation.

      IT USES THE SAME $resolvable SET AS TIER 2, deliberately: that set is every `case`
      label in WorkflowEngine.cs, a superset of ResolveCommand's own. A superset makes this
      tier UNDER-report rather than over-report, and the two tiers cannot disagree about
      what "resolvable" means.

      DOES NOT RESTATE docs/UNREACHABLE_COMMANDS_TRIAGE.md. That file asks whether a
      command class is reachable at all, across six dispatch layers, and derives its own
      counts; this asks the narrower question and the numbers are not comparable.

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
$reachBaseFile = Join-Path $scriptDir 'workflow_reachability_baseline.txt'

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

# ── Tier 6 baseline: button tags accepted as not reachable from a workflow.
$reachBaseline = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
if (Test-Path $reachBaseFile) {
    foreach ($line in Get-Content $reachBaseFile) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        [void]$reachBaseline.Add(($t -split '\s')[0])
    }
}
$reachUsedBase = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$tierSix = @()

# ── Tier 5 setup: the keys WorkflowStep actually binds, read from the class itself.
#    Derived rather than listed so the gate cannot drift from the source.
# A plain IndexOf('public class WorkflowStep') PREFIX-MATCHES "public class
# WorkflowStepResult". Were it declared first, this would silently parse the wrong
# class and read its keys as the step's. Anchor on a non-identifier character after
# the name. (Found by sabotage-verifying this tier: renaming the class to
# WorkflowStepRenamed did not trip the instrument check.)
#
# SEARCHED ACROSS FILES, not just WorkflowEngine.cs. W4 moved WorkflowPreset and
# WorkflowStep to Core/WorkflowPresetModel.cs so the preset SHAPE could be asserted
# without the Revit API, and this parse -- correctly -- failed loudly rather than
# reporting every key in every preset as unbound. It should not fail again for a
# move: WHERE the class lives is not this tier's business, only what it binds.
$stepSources = @()
foreach ($rel in @('StingTools/Core/WorkflowEngine.cs', 'StingTools/Core/WorkflowPresetModel.cs')) {
    $sp = Join-Path $RepoRoot $rel
    if (Test-Path $sp) { $stepSources += ,@($rel, (Get-Content -Raw -Path $sp)) }
}
$stepBody = $null
foreach ($pair in $stepSources) {
    $m = [regex]::Match($pair[1], 'public class WorkflowStep(?![A-Za-z0-9_])')
    if (-not $m.Success) { continue }
    $st = $m.Index
    $en = $pair[1].IndexOf("`n    public class ", $st + 10)
    if ($en -lt 0) { $en = $pair[1].Length }
    $stepBody = $pair[1].Substring($st, $en - $st)
    break
}
if ($null -eq $stepBody) {
    Write-Host "Workflow-wiring FAILED -- WorkflowStep class not found in WorkflowEngine.cs or WorkflowPresetModel.cs." -ForegroundColor Red
    Write-Host "It moved again, or was renamed. Point this parse at it -- do not delete the tier."
    exit 1
}

$boundKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($m in [regex]::Matches($stepBody, '\[JsonProperty\("([^"]+)"\)\]')) {
    [void]$boundKeys.Add($m.Groups[1].Value)
}
# Newtonsoft also binds a bare property name, case-insensitively, when no attribute is given.
foreach ($m in [regex]::Matches($stepBody, 'public\s+[\w<>?\[\]]+\s+(\w+)\s*\{\s*get')) {
    [void]$boundKeys.Add($m.Groups[1].Value)
}
# INSTRUMENT CHECK. If this parse breaks, every key in every preset reads as unbound --
# which is a broken checker, not a finding. Fail loudly instead.
if ($boundKeys.Count -lt 10) {
    Write-Host "Workflow-wiring FAILED -- only $($boundKeys.Count) bound step keys parsed from WorkflowStep; the reader is wrong, not the presets." -ForegroundColor Red
    exit 1
}

# Documentation keys: no execution intent, deliberately allowed. "order" is safe to leave
# in the files only because Tier 3 polices it against array position.
$docKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($k in @('order', 'id', '_notes')) { [void]$docKeys.Add($k) }

$keyBaseFile = Join-Path $scriptDir 'workflow_step_key_baseline.txt'
$keyBaseline = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
if (Test-Path $keyBaseFile) {
    foreach ($line in Get-Content $keyBaseFile) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        [void]$keyBaseline.Add(($t -split '\s')[0])
    }
}

$tierOne   = @()   # steps keyed "tag"
$tierTwo   = @()   # commandTag with no case label
$tierThree = @()   # "order" disagreeing with array position
$tierFive  = @()   # step keys WorkflowStep does not bind
$usedKeyBase = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$usedBase  = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$stepCount = 0
$files     = Get-ChildItem -Path $dataDir -Filter 'WORKFLOW_*.json' -File

foreach ($f in $files) {
    try   { $json = Get-Content -Raw -Path $f.FullName | ConvertFrom-Json }
    catch { Write-Host "Workflow-wiring FAILED -- $($f.Name) is not valid JSON: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }

    if ($null -eq $json.steps) { continue }
    $i = 0
    $orderVals = @()
    foreach ($step in $json.steps) {
        $i++; $stepCount++
        $names = @($step.PSObject.Properties.Name)
        if ($names -contains 'order') { $orderVals += [int]$step.order }

        # TIER 5 -- keys the engine never reads. Checked before the commandTag branches
        # below so a step that ALSO trips Tier 1 still has its stray keys reported;
        # a `continue` further down would hide them.
        foreach ($k in $names) {
            if ($boundKeys.Contains($k) -or $docKeys.Contains($k)) { continue }
            if ($keyBaseline.Contains($k)) { [void]$usedKeyBase.Add($k); continue }
            $tierFive += "$($f.Name) step $i : key '$k' is not bound by WorkflowStep -- it is read by nothing"
        }
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

    # TIER 3 -- "order" disagreeing with array position.
    if ($orderVals.Count -gt 1) {
        $sorted = @($orderVals | Sort-Object)
        if ("$orderVals" -ne "$sorted") {
            $tierThree += "$($f.Name) : order values $($orderVals -join ', ') are not in array sequence"
        }
    }
}

# ── Tier 4: dock-panel buttons. See .DESCRIPTION for why all three dispatch layers
#    must be consulted and why the scan is restricted to Cmd_Click <Button> elements.
$tierFour   = @()
$btnBaseFile = Join-Path $scriptDir 'button_wiring_baseline.txt'
$btnBaseline = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
if (Test-Path $btnBaseFile) {
    foreach ($line in Get-Content $btnBaseFile) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        [void]$btnBaseline.Add(($t -split '\s')[0])
    }
}

# EVERY dockable panel, not just the main one. Until 2026-08-08 this scanned
# StingDockPanel.xaml alone, so the Electrical / HVAC / Plumbing / LPS /
# Sustainability panel buttons were ungated -- and two KUT smoke-test steps
# (Lite_ComCheck on Electrical, Hvac_LifeCycleCompare on HVAC) live exactly
# there. The handler side already scanned all six handlers; only the XAML side
# was narrow. A panel listed here must have both files present or the gate
# fails loudly rather than silently skipping that panel.
$panels = @(
    @{ Xaml = 'StingTools/UI/StingDockPanel.xaml';                          Cb = 'StingTools/UI/StingDockPanel.xaml.cs' },
    @{ Xaml = 'StingTools/UI/StingElectricalPanel.xaml';                    Cb = 'StingTools/UI/StingElectricalPanel.xaml.cs' },
    @{ Xaml = 'StingTools/UI/StingHvacPanel.xaml';                          Cb = 'StingTools/UI/StingHvacPanel.xaml.cs' },
    @{ Xaml = 'StingTools/UI/Plumbing/StingPlumbingPanel.xaml';             Cb = 'StingTools/UI/Plumbing/StingPlumbingPanel.xaml.cs' },
    @{ Xaml = 'StingTools/UI/StingLpsPanel.xaml';                           Cb = 'StingTools/UI/StingLpsPanel.xaml.cs' },
    @{ Xaml = 'StingTools/UI/Sustainability/StingSustainabilityPanel.xaml'; Cb = 'StingTools/UI/Sustainability/StingSustainabilityPanel.xaml.cs' }
)

$btnTags     = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$btnOrigin   = @{}   # tag -> the panel XAML that first declared it, for the failure message
$dispatch    = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$btnUsedBase = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$panelsScanned = 0

foreach ($p in $panels) {
    $xamlPath = Join-Path $RepoRoot $p.Xaml
    $cbPath   = Join-Path $RepoRoot $p.Cb
    if (-not (Test-Path $xamlPath) -or -not (Test-Path $cbPath)) {
        Write-Host "Workflow-wiring FAILED -- $($p.Xaml) / $($p.Cb) not found; Tier 4 cannot run." -ForegroundColor Red
        Write-Host 'If a panel was renamed or removed, update the $panels list in this script.'
        exit 1
    }
    $panelsScanned++

    $xaml = Get-Content -Raw -Path $xamlPath
    foreach ($m in [regex]::Matches($xaml, '(?s)<Button\b[^>]*?>')) {
        $el = $m.Value
        if ($el -notmatch 'Cmd_Click') { continue }
        $t = [regex]::Match($el, 'Tag="([^"]+)"')
        if (-not $t.Success) { continue }
        $tag = $t.Groups[1].Value
        [void]$btnTags.Add($tag)
        if (-not $btnOrigin.ContainsKey($tag)) { $btnOrigin[$tag] = (Split-Path -Leaf $p.Xaml) }
    }

    # L2 -- code-behind suite runners intercepted in this panel's Cmd_Click
    $cb = Get-Content -Raw -Path $cbPath
    $ci = $cb.IndexOf('private void Cmd_Click')
    if ($ci -ge 0) {
        $cj = $cb.IndexOf("`n        private ", $ci + 10)
        if ($cj -lt 0) { $cj = $cb.Length }
        foreach ($m in [regex]::Matches($cb.Substring($ci, $cj - $ci), 'cmdTag\s*==\s*"([^"]+)"')) {
            [void]$dispatch.Add($m.Groups[1].Value)
        }
    }
}

# L3 -- switch cases in every command handler. The union is correct, not sloppy:
# the satellite handlers fall through to StingCommandHandler for tags they do not
# recognise, so a tag handled anywhere is reachable from any panel.
foreach ($h in @(
    'StingTools/UI/StingCommandHandler.cs',
    'StingTools/UI/StingElectricalCommandHandler.cs',
    'StingTools/UI/StingHvacCommandHandler.cs',
    'StingTools/UI/Plumbing/StingPlumbingCommandHandler.cs',
    'StingTools/UI/StingLpsCommandHandler.cs',
    'StingTools/UI/Sustainability/StingSustainabilityCommandHandler.cs')) {
    $hp = Join-Path $RepoRoot $h
    if (-not (Test-Path $hp)) { continue }
    foreach ($m in [regex]::Matches((Get-Content -Raw -Path $hp), 'case\s+"([^"]+)"\s*:')) {
        [void]$dispatch.Add($m.Groups[1].Value)
    }
}

# L1 -- CommandRegistry modules
$modDir = Join-Path $RepoRoot 'StingTools/UI/Modules'
if (Test-Path $modDir) {
    foreach ($mf in Get-ChildItem -Path $modDir -Filter '*CommandModule.cs' -File) {
        foreach ($m in [regex]::Matches((Get-Content -Raw -Path $mf.FullName), 'registry\.Register\(\s*"([^"]+)"')) {
            [void]$dispatch.Add($m.Groups[1].Value)
        }
    }
}

foreach ($t in $btnTags) {
    if ($dispatch.Contains($t)) { continue }
    if ($btnBaseline.Contains($t)) { [void]$btnUsedBase.Add($t); continue }
    $tierFour += "$($btnOrigin[$t]) button Tag=""$t"" reaches no registry entry, no Cmd_Click runner and no handler case"
}

# TIER 6 -- reachable by a click, not by a workflow. See .DESCRIPTION.
foreach ($t in $btnTags) {
    if ($resolvable.Contains($t)) { continue }
    if ($reachBaseline.Contains($t)) { [void]$reachUsedBase.Add($t); continue }
    $tierSix += "$($btnOrigin[$t]) button Tag=""$t"" has no case in WorkflowEngine.ResolveCommand -- no preset can call it"
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

if ($tierThree.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "Workflow-wiring FAILED -- Tier 3: $($tierThree.Count) preset(s) whose ""order"" contradicts execution:" -ForegroundColor Red
    $tierThree | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host 'WorkflowStep does NOT bind "order" -- steps execute in ARRAY position, so "order" is'
    Write-Host 'documentation that reads like configuration. While the two agree it is harmless; once'
    Write-Host 'they disagree the file says one sequence and the engine runs another, silently.'
    Write-Host 'Reorder the array to match, renumber "order" to match the array, or delete the key.'
}

if ($tierFive.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "Workflow-wiring FAILED -- Tier 5: $($tierFive.Count) step key(s) WorkflowStep does not bind:" -ForegroundColor Red
    $tierFive | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host 'Newtonsoft ignores an unknown property silently, so the key sits in the preset'
    Write-Host 'looking like configuration and is read by nothing. Either bind it -- add the'
    Write-Host 'property to WorkflowStep in WorkflowEngine.cs and make the engine act on it --'
    Write-Host 'or delete it from the preset. Do NOT add it to'
    Write-Host 'tools/workflow_step_key_baseline.txt to make this pass: that file records what'
    Write-Host 'was already unbound when the tier was written, and it may shrink, never grow.'
}

if ($tierFour.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "Workflow-wiring FAILED -- Tier 4: $($tierFour.Count) dock-panel button(s) dispatch to nothing:" -ForegroundColor Red
    $tierFour | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host 'Wire the tag in one of the three dispatch layers -- a CommandRegistry module'
    Write-Host '(StingTools/UI/Modules/*CommandModule.cs), a Cmd_Click suite runner, or a case in'
    Write-Host 'a command handler -- or remove the button. Do NOT add it to'
    Write-Host 'tools/button_wiring_baseline.txt unless review agrees the button should stay dead.'
}

if ($tierSix.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "Workflow-wiring FAILED -- Tier 6: $($tierSix.Count) dock-panel button(s) no workflow can call:" -ForegroundColor Red
    $tierSix | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host 'The button works; a preset cannot reach it. Add a case to'
    Write-Host 'WorkflowEngine.ResolveCommand returning the same command class'
    Write-Host 'StingCommandHandler dispatches -- usually one line, and it is what makes the'
    Write-Host 'command chainable, scriptable and visible to Tier 2. Only if the command is'
    Write-Host 'genuinely interactive-only (a modeless window, a picked element, a dialog with'
    Write-Host 'no headless meaning) add the tag to'
    Write-Host 'tools/workflow_reachability_baseline.txt, with review. That file may shrink,'
    Write-Host 'never grow: a line added to make this pass removes the only thing it does.'
}

$staleKeys = @($keyBaseline | Where-Object { -not $usedKeyBase.Contains($_) })
if ($staleKeys.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "Workflow-wiring FAILED -- Tier 5: $($staleKeys.Count) step-key baseline entry/ies are no longer unbound in any preset:" -ForegroundColor Red
    $staleKeys | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host 'Either the key is now BOUND by WorkflowStep, or no preset writes it any more.'
    Write-Host 'Both are good news; delete the line from tools/workflow_step_key_baseline.txt in'
    Write-Host 'the same commit. An entry for a key nobody writes is a claim about a preset that'
    Write-Host 'no longer says it, and it silently re-permits the key if it ever comes back.'
}

if ($failed) { exit 1 }

$stale = @($baseline | Where-Object { -not $usedBase.Contains($_) })
Write-Host "Workflow-wiring OK."
Write-Host "  Presets scanned                                 : $($files.Count)"
Write-Host "  Steps scanned                                   : $stepCount"
Write-Host "  ResolveCommand case labels                      : $($resolvable.Count)"
Write-Host "  Tier 1 steps keyed ""tag"" (must be 0)            : 0"
Write-Host "  Tier 2 unresolvable outside the baseline        : 0"
Write-Host "  Tier 3 presets whose ""order"" contradicts array  : 0"
Write-Host "  Tier 5 unbound step keys outside the baseline   : 0"
Write-Host "  WorkflowStep keys the engine binds              : $($boundKeys.Count)"
Write-Host "  Baselined 'no command exists' tags in use       : $($usedBase.Count)"
Write-Host "  Panel XAMLs scanned                             : $panelsScanned"
Write-Host "  Cmd_Click buttons scanned                       : $($btnTags.Count)"
Write-Host "  Dispatchable names (registry + runners + cases) : $($dispatch.Count)"
Write-Host "  Tier 4 buttons dispatching to nothing           : 0"
Write-Host "  Tier 6 buttons no workflow can call              : 0"
Write-Host "  Baselined workflow-unreachable buttons in use    : $($reachUsedBase.Count)"
if ($btnUsedBase.Count -gt 0) {
    Write-Host "  Baselined dead buttons in use                   : $($btnUsedBase.Count)"
}
if ($usedKeyBase.Count -gt 0) {
    Write-Host "  Baselined unbound step keys in use              : $($usedKeyBase.Count)"
}
if ($stale.Count -gt 0) {
    Write-Host ""
    Write-Host "Note: $($stale.Count) baseline entry/ies are no longer referenced by any preset:"
    $stale | Sort-Object | ForEach-Object { Write-Host "  $_" }
    Write-Host "Remove them from tools/workflow_wiring_baseline.txt -- the baseline may shrink, never grow."
}
$staleReach = @($reachBaseline | Where-Object { -not $reachUsedBase.Contains($_) })
if ($staleReach.Count -gt 0) {
    Write-Host ""
    Write-Host "Note: $($staleReach.Count) Tier 6 baseline entry/ies are now reachable from a workflow"
    Write-Host "(or their button is gone). Good news either way -- remove them from"
    Write-Host "tools/workflow_reachability_baseline.txt; that file may shrink, never grow."
    $staleReach | Sort-Object | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    if ($staleReach.Count -gt 20) { Write-Host "  ... and $($staleReach.Count - 20) more" }
}

# NOTE the asymmetry with the Tier 2 baseline above, which only WARNS about a stale
# entry. Tier 5's baseline FAILS on one, because "the baseline only shrinks" is not a
# rule if nothing enforces it -- and because a stale entry here is the exact signal
# that somebody FIXED a key (bound it, or deleted it from the preset) and left the
# claim behind. Tier 2's weaker behaviour is left alone: changing it would fail
# unrelated presets and does not belong in this change.
exit 0
