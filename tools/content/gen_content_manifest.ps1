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
    (Join-Path $dataDir 'TagFamilies'),                  # source flat (synced canonical 206)
    (Join-Path $repo 'CompiledPlugin\data\TagFamilies'), # deployed
    $tagDir                                              # source Seeds subset (legacy)
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

# ── Model-family seeds: enumerate Data/Seeds (status=spec, buildable via
#    SymbolLibraryCreator). Id is derived from the spec FILENAME (unique) - many
#    seeds can share a category (e.g. the 5 Specialty Equipment seeds), so the id
#    cannot be category-derived. Discipline is read from ASS_DISCIPLINE_COD_TXT.
function Get-SeedId([string]$stem) {
    $k = ($stem -creplace '(?<=[a-z0-9])(?=[A-Z])','-').ToLowerInvariant()
    return 'seed-' + ($k -replace '[^a-z0-9-]','')
}
$symbols = @()
$seedCats = @{}
Get-ChildItem (Join-Path $seedDir '*.json') | Sort-Object Name | ForEach-Object {
    try {
        $j = Get-Content $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $sym = $j.symbols[0]
        if (-not $sym) { return }
        $cat = $sym.category
        $dp  = $sym.parameters | Where-Object { $_.name -eq 'ASS_DISCIPLINE_COD_TXT' } | Select-Object -First 1
        $disc = if ($dp -and $dp.default) { $dp.default } else { '*' }
        $stem = $_.BaseName -replace '^STING_SEED_',''
        $symbols += [ordered]@{
            id=(Get-SeedId $stem); kind='modelFamily'; category=$cat; discipline=$disc;
            buildSpec=$_.Name; familyFile=("STING Seed - {0}.rfa" -f $cat); checksum=$null;
            status='spec'; protected=$true; origin='corporate' }
        $seedCats[$cat] = $true
    } catch { Write-Host ("  seed parse skip: {0} - {1}" -f $_.Name, $_.Exception.Message) }
}

# ── COMPUTED needs-spec against the REAL denominator ─────────────────────
# The categories that actually need a model-family seed are the ones an engine
# resolves a family for: placement-rule CategoryFilter + DWG fixture-map Category
# + swap-registry category. needs-spec = that union MINUS seeded MINUS system/
# datum families (never seeded) MINUS categories served by another seed. This
# tracks the right denominator automatically - add a placement rule for a new
# category and it surfaces here without editing this script.
function Read-Cats($glob, $pattern) {
    $set = New-Object System.Collections.Generic.HashSet[string]
    Get-ChildItem $glob -ErrorAction SilentlyContinue | ForEach-Object {
        [regex]::Matches((Get-Content $_.FullName -Raw -Encoding UTF8), $pattern) |
            ForEach-Object { if ($_.Groups[1].Value) { [void]$set.Add($_.Groups[1].Value) } }
    }
    return $set
}
$engineReq = New-Object System.Collections.Generic.HashSet[string]
(Read-Cats (Join-Path $dataDir 'Placement\STING_PLACEMENT_RULES*.json') '"CategoryFilter"\s*:\s*"([^"]+)"') | ForEach-Object { [void]$engineReq.Add($_) }
(Read-Cats (Join-Path $dataDir 'STING_DWG_FIXTURE_MAP.json')            '"Category"\s*:\s*"([^"]+)"')        | ForEach-Object { [void]$engineReq.Add($_) }
(Read-Cats (Join-Path $dataDir 'STING_FAMILY_SWAP_REGISTRY.json')       '"category"\s*:\s*"([^"]+)"')        | ForEach-Object { [void]$engineReq.Add($_) }

# System-family / datum / annotation categories that never take a model-family seed.
$excludeSystem = @('Walls','Floors','Roofs','Ceilings','Ducts','Flex Ducts','Pipes','Flex Pipes',
    'Conduits','Cable Trays','Duct Fittings','Pipe Fittings','Conduit Fittings','Cable Tray Fittings',
    'Duct Insulations','Pipe Insulations','Duct Linings','Stairs','Railings','Ramps','Roads','Rooms',
    'Areas','Spaces','Zones','Grids','Levels','Materials','Sheets','Model Groups','Parts','Assemblies',
    'RVT Links','Property Lines','Property Line Segments','Curtain Panels','Curtain Wall Mullions',
    'Toposolid','Pads')
# Loadable categories served by a different seed's category (not a distinct gap).
$coveredByOther = @{ 'Junction Boxes' = 'Electrical Equipment' }

$gapList = @()
foreach ($c in ($engineReq | Sort-Object)) {
    if ($seedCats.ContainsKey($c)) { continue }
    if ($excludeSystem -contains $c) { continue }
    if ($coveredByOther.ContainsKey($c)) { continue }
    $gapList += $c
}
foreach ($c in $gapList) {
    $symbols += [ordered]@{ id=('seed-'+(Get-Slug $c)); kind='modelFamily'; category=$c; discipline='*';
        buildSpec=$null; familyFile=$null; checksum=$null; status='needs-spec'; protected=$false; origin='corporate';
        notes='Engine-required (placement/DWG/swap) loadable category with no model-family seed yet.' }
}

# Coverage summary baked into the manifest so Content_Coverage can show the
# real denominator at runtime (regenerated whenever rules/seeds change).
$coverage = [ordered]@{
    engineRequiredCount = $engineReq.Count
    seededLoadableCount = @($engineReq | Where-Object { $seedCats.ContainsKey($_) }).Count
    needsSpecCount      = $gapList.Count
    needsSpec           = @($gapList)
    excludedSystemDatum = @($engineReq | Where-Object { $excludeSystem -contains $_ } | Sort-Object)
    note                = 'Seed denominator = categories an engine resolves a family for (placement CategoryFilter + DWG fixture-map Category + swap registry), minus system/datum families. NOT the 206 tag list.'
}

# ── Bundles (scoped load/seed targets) ──────────────────────────────────
$bundles = @(
    [ordered]@{ id='mep-core'; members=@('seed-mechanical-equipment','seed-air-terminal','seed-plumbing-fixture','seed-plumbing-equipment','seed-electrical-fixture','seed-lighting-fixture','sym-mep','tag-ducts','tag-pipes','tag-mechanical-equipment','tag-air-terminals') },
    [ordered]@{ id='mep-fixtures'; members=@('seed-air-terminal','seed-sprinkler','seed-electrical-fixture','seed-lighting-fixture','seed-communication-device','seed-fire-alarm-device') },
    [ordered]@{ id='electrical'; members=@('seed-electrical-equipment','seed-junction-box','seed-electrical-fixture','seed-lighting-fixture','sym-elec','sym-lighting','sym-sld') },
    [ordered]@{ id='healthcare'; members=@('seed-med-gas-outlet','seed-nurse-call-device','seed-lab-fixture') },
    [ordered]@{ id='tags-all';  members=@($tagFamilies | ForEach-Object { $_.id }) }
)

# ── Assemble ────────────────────────────────────────────────────────────
$manifest = [ordered]@{
    libraryVersion   = '2026.6.1'
    rootPrecedence   = 'projectFirst'
    _coverageNote    = 'tagFamilies = the canonical 206-family list from LABEL_DEFINITIONS.category_labels (NOT the on-disk .rfa count); all built. symbols = model-family seeds enumerated from Data/Seeds (status=spec, buildable via SymbolLibraryCreator). Model-family seeds cover LOADABLE component categories only: system families (Walls/Floors/Roofs/Ceilings/Ducts/Pipes/Conduits/Cable Trays/Stairs/Railings) and datum/annotation categories (Rooms/Areas/Grids/Levels) have NO seed by design - they are project types, not loadable .rfa content. Medical Equipment is modeled under Specialty Equipment (no separate Revit category). needs-build / needs-spec, where present, are tracked coverage gaps, not errors.'
    coverage         = $coverage
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
Write-Host ("  engine-required  : {0} loadable categories -> {1} seeded, {2} needs-spec (system/datum excluded)" -f $coverage.engineRequiredCount, $coverage.seededLoadableCount, $coverage.needsSpecCount)
Write-Host ("  bundles          : {0}" -f $bundles.Count)
