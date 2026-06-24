# gen_content_manifest.ps1
# Regenerates StingTools/Data/STING_CONTENT_MANIFEST.json from the real content
# on disk: every tag family (.rfa) gets a real SHA-256, every symbol catalogue is
# indexed, the model-family seeds are listed from Data/Seeds, and the loadable
# component categories that have a tag but NO seed yet are emitted as tracked
# "needs-spec" coverage gaps. Run after adding/removing seeds, tags or catalogues.
#
#   pwsh tools/content/gen_content_manifest.ps1
#
$ErrorActionPreference = 'Stop'
$repo    = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dataDir = Join-Path $repo 'StingTools\Data'
$tagDir  = Join-Path $dataDir 'TagFamilies\Seeds'
$symDir  = Join-Path $dataDir 'Symbols'
$seedDir = Join-Path $dataDir 'Seeds'
$outPath = Join-Path $dataDir 'STING_CONTENT_MANIFEST.json'

function Get-Slug([string]$s) {
    $x = $s.ToLowerInvariant() -replace '[^a-z0-9]+','-'
    return ($x.Trim('-'))
}
function Get-Disc([string]$cat) {
    switch -Regex ($cat) {
        'Fire Alarm|Fire Protection|Sprinkler'                         { return 'FP' }
        'Nurse Call'                                                   { return 'H'  }
        'Medical'                                                      { return 'H'  }
        'Data Device|Communication|Security Device|Telephone|Audio Visual' { return 'LV' }
        'Cable Tray|Conduit|Electrical Connector'                      { return 'E'  }
        'Lighting|Electrical'                                          { return 'E'  }
        'Plumbing|Pipe|Gas Pipe'                                       { return 'P'  }
        'Duct|Air Terminal|Mechanical|HVAC'                            { return 'M'  }
        'Structural|Analytical|Rebar|Truss|Beam|Brace|Bolt|Weld|Foundation|Stiffener|Slab|Connection|Framing|Reinforcement|Column' { return 'S' }
        default                                                        { return '*'  }
    }
}
function Get-Sha([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
}

# ── Tag families: CANONICAL 206-family list from LABEL_DEFINITIONS.json ──
# category_labels is the single source of truth (aligned with the 206-family
# TagFamilyConfig.CategoryTemplateMap). Each canonical family is matched to its
# on-disk .rfa (status=built + checksum) or flagged needs-build when the .rfa
# has not yet been generated into TagFamilies/Seeds. DO NOT drive this off the
# filesystem listing - that under-counts by every not-yet-generated family.
function Normalize-Core([string]$s) {
    $x = $s.ToLowerInvariant() -replace 'sting','' -replace 'tag',''
    return ($x -replace '[^a-z0-9]','')
}
# Index existing .rfa by normalized core token (strip "STING"/"Tag"/punctuation).
# Tag source: prefer the fullest generated set. "Create Tag Families" writes the
# complete canonical set (flat) to the deployed plugin's data/TagFamilies; source
# control's Data/TagFamilies/Seeds holds an older subset. Pick whichever has more.
$tagCandidates = @(
    (Join-Path $repo 'CompiledPlugin\data\TagFamilies'),
    $tagDir
) | Where-Object { Test-Path $_ }
$tagSrc = $tagCandidates |
    Sort-Object { @(Get-ChildItem (Join-Path $_ '*.rfa') -ErrorAction SilentlyContinue).Count } -Descending |
    Select-Object -First 1
Write-Host ("  tag source       : {0}" -f $tagSrc)

# Index generated .rfa by normalized core (strip STING/Tag/punctuation) so the
# canonical family_name matches regardless of plural/verbose/variant filename form.
$diskByNC = @{}
Get-ChildItem (Join-Path $tagSrc '*.rfa') | ForEach-Object {
    $k = Normalize-Core ([IO.Path]::GetFileNameWithoutExtension($_.Name))
    if (-not $diskByNC.ContainsKey($k)) { $diskByNC[$k] = $_ }
}

$labelPath = Join-Path $dataDir 'LABEL_DEFINITIONS.json'
$labels = (Get-Content $labelPath -Raw -Encoding UTF8 | ConvertFrom-Json).category_labels

$tagFamilies = @()
$seenIds = @{}
foreach ($p in $labels.PSObject.Properties) {
    $key = $p.Name
    $fam = $p.Value.family_name
    if (-not $fam) { $fam = "STING - $key Tag" }

    $id = 'tag-' + (Get-Slug $key)
    while ($seenIds.ContainsKey($id)) { $id = $id + '-x' }
    $seenIds[$id] = $true

    $disc = Get-Disc "$key $fam"
    $cat  = $key -replace '[^\x20-\x7E]','-'   # ASCII-clean display
    $nk   = Normalize-Core $fam

    if ($diskByNC.ContainsKey($nk)) {
        $f = $diskByNC[$nk]
        # Keep the real (possibly non-ASCII) filename so the resolver can find it.
        $tagFamilies += [ordered]@{
            id=$id; kind='tagFamily'; category=$cat; discipline=$disc;
            familyFile=$f.Name; checksum=(Get-Sha $f.FullName); status='built'; origin='corporate' }
    } else {
        $expected = ($fam -replace '[\\/:*?"<>|]',' ') -replace '\s+',' '
        $expected = $expected.Trim() + '.rfa'
        $tagFamilies += [ordered]@{
            id=$id; kind='tagFamily'; category=$cat; discipline=$disc;
            familyFile=$expected; checksum=$null; status='needs-build'; origin='corporate';
            notes='In canonical 206-family list (LABEL_DEFINITIONS.category_labels); .rfa not found in tag source.' }
    }
}

# ── Symbol catalogues (Data/Symbols/*.json, excluding infra/registry files) ──
$infra = @('STING_SYMBOL_STANDARDS.json','STING_SYMBOL_CONCEPTS.json',
           'STING_SYMBOL_ALIASES.json','STING_MIXED_STANDARD_PROFILES.json')
$symbolCatalogues = @()
Get-ChildItem (Join-Path $symDir '*.json') | Sort-Object Name | ForEach-Object {
    if ($infra -contains $_.Name) { return }
    $stem = $_.Name -replace '^STING_','' -replace '_SYMBOLS','' -replace '\.json$',''
    $std  = 'IEC'
    if ($_.Name -match '_BS\.json$')    { $std = 'BS' }
    elseif ($_.Name -match '_CIBSE')    { $std = 'CIBSE' }
    elseif ($_.Name -match '_IEEE')     { $std = 'IEEE' }
    elseif ($_.Name -match '_NFPA')     { $std = 'NFPA' }
    elseif ($_.Name -match 'ISO6412')   { $std = 'ISO' }
    $symbolCatalogues += [ordered]@{
        id        = 'sym-' + (Get-Slug $stem)
        kind      = 'symbolCatalogue'
        catalogue = $_.Name
        standard  = $std
        checksum  = (Get-Sha $_.FullName)
        status    = 'built'
        origin    = 'corporate'
    }
}

# ── Model-family seeds: BUILT (have a spec) + GAPS (needs-spec) ───────────
function SeedSpec($id,$cat,$disc,$spec) {
    [ordered]@{ id=$id; kind='modelFamily'; category=$cat; discipline=$disc;
                buildSpec=$spec; familyFile=("STING Seed - {0}.rfa" -f ($cat)); checksum=$null;
                status='spec'; protected=$true; origin='corporate' }
}
function SeedGap($id,$cat,$disc,$note) {
    [ordered]@{ id=$id; kind='modelFamily'; category=$cat; discipline=$disc;
                buildSpec=$null; familyFile=$null; checksum=$null;
                status='needs-spec'; protected=$false; origin='corporate'; notes=$note }
}

$symbols = @(
    # 16 specs in Data/Seeds (status=spec → buildable via SymbolLibraryCreator)
    (SeedSpec 'seed-mech-equipment'      'Mechanical Equipment'  'M'  'STING_SEED_MechanicalEquipment.json'),
    (SeedSpec 'seed-air-terminal'        'Air Terminals'         'M'  'STING_SEED_AirTerminal.json'),
    (SeedSpec 'seed-sprinkler'           'Sprinklers'            'FP' 'STING_SEED_Sprinkler.json'),
    (SeedSpec 'seed-elec-equipment'      'Electrical Equipment'  'E'  'STING_SEED_ElectricalEquipment.json'),
    (SeedSpec 'seed-junction-box'        'Electrical Equipment'  'E'  'STING_SEED_JunctionBox.json'),
    (SeedSpec 'seed-elec-fixture'        'Electrical Fixtures'   'E'  'STING_SEED_ElectricalFixture.json'),
    (SeedSpec 'seed-comm-device'         'Communication Devices' 'LV' 'STING_SEED_CommunicationDevice.json'),
    (SeedSpec 'seed-fire-alarm'          'Fire Alarm Devices'    'FP' 'STING_SEED_FireAlarmDevice.json'),
    (SeedSpec 'seed-plumbing-fixture'    'Plumbing Fixtures'     'P'  'STING_SEED_PlumbingFixture.json'),
    (SeedSpec 'seed-plumbing-equipment'  'Plumbing Equipment'    'P'  'STING_SEED_PlumbingEquipment.json'),
    (SeedSpec 'seed-lighting-fixture'    'Lighting Fixtures'     'E'  'STING_SEED_LightingFixture.json'),
    (SeedSpec 'seed-lab-fixture'         'Specialty Equipment'   'H'  'STING_SEED_LabFixture.json'),
    (SeedSpec 'seed-acoustic-seal'       'Specialty Equipment'   'M'  'STING_SEED_AcousticSeal.json'),
    (SeedSpec 'seed-medgas-outlet'       'Specialty Equipment'   'MG' 'STING_SEED_MedGasOutlet.json'),
    (SeedSpec 'seed-fire-damper'         'Specialty Equipment'   'M'  'STING_SEED_FireDamper.json'),
    (SeedSpec 'seed-speciality-equipment' 'Specialty Equipment'  '*'  'STING_SEED_SpecialityEquipment.json'),

    # GAPS - loadable component categories placed by MEP/placement/healthcare
    # that have a TAG but no model-family seed yet. Tracked, not silent.
    (SeedGap 'seed-data-devices'         'Data Devices'             'LV' 'LV outlet / data point - wall-mount; placed by MEP-from-DWG.'),
    (SeedGap 'seed-security-devices'     'Security Devices'         'LV' 'CCTV / access control - wall/ceiling-mount.'),
    (SeedGap 'seed-nurse-call'           'Nurse Call Devices'       'H'  'Healthcare nurse-call points - wall-mount.'),
    (SeedGap 'seed-telephone-devices'    'Telephone Devices'        'LV' 'Telephone outlets - wall-mount.'),
    (SeedGap 'seed-av-devices'           'Audio Visual Devices'     'LV' 'AV plates / projectors.'),
    (SeedGap 'seed-lighting-devices'     'Lighting Devices'         'E'  'Switches / sensors (distinct from Lighting Fixtures).'),
    (SeedGap 'seed-mech-control'         'Mechanical Control Devices' 'M' 'Thermostats / BMS sensors.'),
    (SeedGap 'seed-medical-equipment'    'Medical Equipment'        'H'  'Healthcare clinical equipment seed.'),
    (SeedGap 'seed-duct-accessory'       'Duct Accessories'         'M'  'Dampers / in-line VAV - needed for MEP coordination.'),
    (SeedGap 'seed-pipe-accessory'       'Pipe Accessories'         'P'  'Valves / strainers - needed for MEP coordination.'),
    (SeedGap 'seed-fire-protection'      'Fire Protection'          'FP' 'Extinguishers / hose reels (distinct from Sprinklers).'),
    (SeedGap 'seed-food-service'         'Food Service Equipment'   'A'  'FF&E - lower priority.'),
    (SeedGap 'seed-furniture'            'Furniture'                'A'  'FF&E - lower priority.'),
    (SeedGap 'seed-casework'             'Casework'                 'A'  'FF&E - lower priority.')
)

# ── Bundles (scoped load/seed targets) ──────────────────────────────────
$bundles = @(
    [ordered]@{ id='mep-core'; members=@('seed-mech-equipment','seed-air-terminal','seed-plumbing-fixture','seed-plumbing-equipment','seed-elec-fixture','seed-lighting-fixture','sym-mep','tag-ducts','tag-pipes','tag-mechanical-equipment','tag-air-terminals') },
    [ordered]@{ id='mep-fixtures'; members=@('seed-air-terminal','seed-sprinkler','seed-elec-fixture','seed-lighting-fixture','seed-comm-device','seed-fire-alarm') },
    [ordered]@{ id='electrical'; members=@('seed-elec-equipment','seed-junction-box','seed-elec-fixture','seed-lighting-fixture','sym-elec','sym-lighting','sym-sld') },
    [ordered]@{ id='healthcare'; members=@('seed-medgas-outlet','seed-nurse-call','seed-medical-equipment','seed-lab-fixture') },
    [ordered]@{ id='tags-all';  members=@($tagFamilies | ForEach-Object { $_.id }) }
)

# ── Assemble ────────────────────────────────────────────────────────────
$manifest = [ordered]@{
    libraryVersion   = '2026.6.1'
    rootPrecedence   = 'projectFirst'
    _coverageNote    = 'tagFamilies = the canonical 206-family list from LABEL_DEFINITIONS.category_labels (NOT the on-disk .rfa count). status=built has a generated .rfa; status=needs-build is a canonical family whose .rfa has not been generated yet. Model-family seeds cover LOADABLE component categories only: system families (Walls/Floors/Roofs/Ceilings/Ducts/Pipes/Conduits/Cable Trays/Stairs/Railings) and datum/annotation categories (Rooms/Areas/Grids/Levels) have NO model-family seed by design - they are project types, not loadable .rfa content. status=needs-spec is a category with a tag but no model-family seed yet. needs-build / needs-spec are tracked coverage gaps, not errors.'
    symbols          = $symbols
    symbolCatalogues = $symbolCatalogues
    tagFamilies      = $tagFamilies
    viewTemplates    = @('STING - Mechanical Plan','STING - Electrical Plan','STING - Plumbing Plan','STING - Coordination')
    filters          = @('STING - HVAC: Supply','STING - HVAC: Return','STING - Pipe: DCW','STING - Pipe: SAN','STING - MGS: O2')
    sharedParams     = 'MR_PARAMETERS.txt'
    swapMap          = 'STING_FAMILY_SWAP_REGISTRY.json'
    bundles          = $bundles
}

$json = $manifest | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($outPath, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("Wrote {0}" -f $outPath)
Write-Host ("  tagFamilies      : {0} ({1} built, {2} needs-build)" -f $tagFamilies.Count, ($tagFamilies | Where-Object {$_.status -eq 'built'}).Count, ($tagFamilies | Where-Object {$_.status -eq 'needs-build'}).Count)
Write-Host ("  symbolCatalogues : {0}" -f $symbolCatalogues.Count)
Write-Host ("  symbols (seeds)  : {0} ({1} spec, {2} needs-spec)" -f $symbols.Count, ($symbols | Where-Object {$_.status -eq 'spec'}).Count, ($symbols | Where-Object {$_.status -eq 'needs-spec'}).Count)
Write-Host ("  bundles          : {0}" -f $bundles.Count)
