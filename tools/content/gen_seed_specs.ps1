# gen_seed_specs.ps1
# Authors the model-family seed specs for the loadable component categories that
# had a tag family but no seed (the manifest's needs-spec gaps). Each spec follows
# the SymbolLibraryCreator schema (see STING_SEED_CommunicationDevice.json):
# standard STING_SEED_* / ASS_* params + 2D symbol geometry + a 3D box + swap
# candidates + per-product type variants. Re-run to regenerate.
#
#   pwsh tools/content/gen_seed_specs.ps1
#
$ErrorActionPreference = 'Stop'
$repo    = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$seedDir = Join-Path $repo 'StingTools\Data\Seeds'

function StdParams($id, $disc, $prod0) {
    @(
        [ordered]@{ name='STING_SEED_FAMILY_TXT';  type='Text'; shared=$true; isInstance=$true; default=$id },
        [ordered]@{ name='STING_DESIGN_REF_TXT';   type='Text'; shared=$true; isInstance=$true; default='' },
        [ordered]@{ name='STING_SWAP_HISTORY_TXT'; type='Text'; shared=$true; isInstance=$true; default='' },
        [ordered]@{ name='ASS_TAG_1';              type='Text'; shared=$true; isInstance=$true },
        [ordered]@{ name='ASS_DISCIPLINE_COD_TXT'; type='Text'; shared=$true; isInstance=$true; default=$disc },
        [ordered]@{ name='ASS_PRODCT_COD_TXT';     type='Text'; shared=$true; isInstance=$true; default=$prod0 }
    )
}

# category, disc(letter), discFull, hosting, w, d, h, @(@{name;prod}...)
$seeds = @(
    @{ file='DataDevice';            cat='Data Devices';                disc='E';  full='Electrical';   host='FaceBased';  w=100; d=30;  h=100;
       std='STING SEED FAMILY - Data Devices (BS EN 50173 / TIA-568)';
       v=@(@{name='DataOutletRJ45';prod='DAT'},@{name='DataOutletCat6A';prod='DAT-6A'},@{name='DataOutletFibre';prod='DAT-FO'},@{name='FloorBox';prod='DAT-FB'}) },
    @{ file='SecurityDevice';        cat='Security Devices';            disc='E';  full='Electrical';   host='FaceBased';  w=120; d=80;  h=120;
       std='STING SEED FAMILY - Security Devices (BS EN 62676 / BS EN 60839)';
       v=@(@{name='CctvFixed';prod='CAM'},@{name='CctvPtz';prod='CAM-P'},@{name='AccessReader';prod='ACR'},@{name='DoorContact';prod='DCN'},@{name='PirDetector';prod='PIR'}) },
    @{ file='NurseCallDevice';       cat='Nurse Call Devices';          disc='E';  full='Electrical';   host='FaceBased';  w=100; d=30;  h=100;
       std='STING SEED FAMILY - Nurse Call Devices (HTM 08-03)';
       v=@(@{name='CallPoint';prod='NC'},@{name='CeilingPull';prod='NC-CP'},@{name='BedheadSocket';prod='NC-BH'},@{name='OverDoorLight';prod='NC-ODL'}) },
    @{ file='TelephoneDevice';       cat='Telephone Devices';           disc='E';  full='Electrical';   host='FaceBased';  w=90;  d=30;  h=90;
       std='STING SEED FAMILY - Telephone Devices (BS EN 50173)';
       v=@(@{name='TelephoneOutlet';prod='TEL'},@{name='IpPhonePoint';prod='TEL-IP'}) },
    @{ file='AudioVisualDevice';     cat='Audio Visual Devices';        disc='E';  full='Electrical';   host='FaceBased';  w=200; d=100; h=200;
       std='STING SEED FAMILY - Audio Visual Devices';
       v=@(@{name='Projector';prod='PROJ'},@{name='Speaker';prod='SPK'},@{name='Screen';prod='SCR'},@{name='Display';prod='DISP'},@{name='FloorAvBox';prod='AV-FB'}) },
    @{ file='LightingDevice';        cat='Lighting Devices';            disc='E';  full='Electrical';   host='FaceBased';  w=86;  d=30;  h=86;
       std='STING SEED FAMILY - Lighting Devices (BS 7671)';
       v=@(@{name='Switch1Gang';prod='SW'},@{name='Dimmer';prod='DIM'},@{name='PirSensor';prod='SENS'},@{name='KeySwitch';prod='KSW'}) },
    @{ file='MechanicalControlDevice'; cat='Mechanical Control Devices'; disc='M'; full='Mechanical';   host='FaceBased';  w=90;  d=40;  h=120;
       std='STING SEED FAMILY - Mechanical Control Devices (BS EN ISO 16484)';
       v=@(@{name='Thermostat';prod='TSTAT'},@{name='RoomSensor';prod='SENS'},@{name='BmsController';prod='BMS'},@{name='DamperActuator';prod='ACT'}) },
    @{ file='DuctAccessory';         cat='Duct Accessories';            disc='M';  full='Mechanical';   host='Standalone'; w=300; d=300; h=250;
       std='STING SEED FAMILY - Duct Accessories (BS EN 1751 / BS EN 15650)';
       v=@(@{name='VolumeControlDamper';prod='VCD'},@{name='FireDamper';prod='FD'},@{name='MotorisedDamper';prod='MD'},@{name='Attenuator';prod='ATT'}) },
    @{ file='PipeAccessory';         cat='Pipe Accessories';            disc='P';  full='Plumbing';     host='Standalone'; w=150; d=80;  h=120;
       std='STING SEED FAMILY - Pipe Accessories (BS EN 1213 / BS EN 12266)';
       v=@(@{name='GateValve';prod='GV'},@{name='BallValve';prod='BV'},@{name='CheckValve';prod='CV'},@{name='Strainer';prod='STR'},@{name='PressureReducingValve';prod='PRV'}) },
    @{ file='FireProtection';        cat='Fire Protection';             disc='FP'; full='Fire';         host='FaceBased';  w=150; d=200; h=150;
       std='STING SEED FAMILY - Fire Protection (BS 5306)';
       v=@(@{name='Extinguisher';prod='EXT'},@{name='HoseReel';prod='HR'},@{name='FireBlanket';prod='BLK'},@{name='FireBucket';prod='BKT'}) },
    @{ file='FoodServiceEquipment';  cat='Food Service Equipment';      disc='A';  full='Architecture'; host='Standalone'; w=600; d=650; h=900;
       std='STING SEED FAMILY - Food Service Equipment';
       v=@(@{name='Oven';prod='OVEN'},@{name='Refrigerator';prod='FRDG'},@{name='CommercialSink';prod='SINK'},@{name='Hob';prod='HOB'},@{name='Dishwasher';prod='DISH'}) },
    @{ file='Furniture';             cat='Furniture';                   disc='A';  full='Architecture'; host='Standalone'; w=800; d=800; h=750;
       std='STING SEED FAMILY - Furniture';
       v=@(@{name='Desk';prod='DESK'},@{name='Chair';prod='CHAIR'},@{name='Table';prod='TABLE'},@{name='Cabinet';prod='CAB'},@{name='Sofa';prod='SOFA'}) },
    @{ file='Casework';              cat='Casework';                    disc='A';  full='Architecture'; host='Standalone'; w=600; d=600; h=900;
       std='STING SEED FAMILY - Casework';
       v=@(@{name='BaseUnit';prod='BASE'},@{name='WallUnit';prod='WALL'},@{name='TallUnit';prod='TALL'},@{name='Worktop';prod='WTOP'}) },

    # Arch / site loadables - close the engine-required set to 100%.
    @{ file='GenericModel';          cat='Generic Models';              disc='A';  full='Architecture'; host='Standalone'; w=300; d=300; h=300;
       std='STING SEED FAMILY - Generic Models';
       v=@(@{name='GenericComponent';prod='GEN'},@{name='Equipment';prod='EQP'},@{name='Bracket';prod='BRK'},@{name='AccessPanel';prod='APN'}) },
    @{ file='Door';                  cat='Doors';                       disc='A';  full='Architecture'; host='FaceBased';  w=900; d=100; h=2100;
       std='STING SEED FAMILY - Doors';
       v=@(@{name='SingleLeaf';prod='DR-S'},@{name='DoubleLeaf';prod='DR-D'},@{name='Sliding';prod='DR-SL'},@{name='FireDoor';prod='DR-FR'}) },
    @{ file='Window';                cat='Windows';                     disc='A';  full='Architecture'; host='FaceBased';  w=1200; d=100; h=1200;
       std='STING SEED FAMILY - Windows';
       v=@(@{name='Fixed';prod='WN-F'},@{name='Casement';prod='WN-C'},@{name='Sliding';prod='WN-SL'},@{name='Louvre';prod='WN-L'}) },
    @{ file='Parking';               cat='Parking';                     disc='A';  full='Architecture'; host='Standalone'; w=2400; d=4800; h=50;
       std='STING SEED FAMILY - Parking';
       v=@(@{name='Standard';prod='PK'},@{name='Accessible';prod='PK-A'},@{name='Compact';prod='PK-C'},@{name='Motorcycle';prod='PK-M'}) }
)

$written = 0
foreach ($s in $seeds) {
    $id = 'STING_SEED_' + $s.file
    $tv = @()
    foreach ($v in $s.v) { $tv += [ordered]@{ name=$v.name; params=[ordered]@{ ASS_PRODCT_COD_TXT=$v.prod } } }

    $sym = [ordered]@{
        id              = $id
        name            = ('STING Seed - ' + $s.cat)
        category        = $s.cat
        familyType      = 'SeedFamily'
        discipline      = $s.full
        subcategory     = 'STING_SEED'
        isSeed          = $true
        protectExisting = $true
        hosting         = $s.host
        symbolSize      = 4.0
        parameters      = (StdParams $id $s.disc $s.v[0].prod)
        geometry        = [ordered]@{
            lines = @(
                [ordered]@{ x1=-0.5; y1=-0.5; x2=0.5;  y2=-0.5 },
                [ordered]@{ x1=0.5;  y1=-0.5; x2=0.5;  y2=0.5  },
                [ordered]@{ x1=0.5;  y1=0.5;  x2=-0.5; y2=0.5  },
                [ordered]@{ x1=-0.5; y1=0.5;  x2=-0.5; y2=-0.5 }
            )
            arcs  = @(
                [ordered]@{ cx=0; cy=0; r=0.18; startDeg=0; endDeg=360 },
                [ordered]@{ cx=0; cy=0; r=0.30; startDeg=0; endDeg=180 }
            )
        }
        solid3D         = [ordered]@{ type='Box'; widthMm=$s.w; depthMm=$s.d; heightMm=$s.h }
        swapCandidates  = @(
            [ordered]@{ label='Manufacturer family (primary)';  familyPath=''; variantPattern='*'; priority=1; autoLoad=$false },
            [ordered]@{ label='Manufacturer family (fallback)'; familyPath=''; variantPattern='*'; priority=2; autoLoad=$false }
        )
        typeVariants    = $tv
    }

    $notes = @(
        ("Seed for the " + $s.cat + " category. Standard STING_SEED_* / ASS_* parameters + per-product type variants."),
        "Draft schematic seed - swap to a manufacturer family via Symbols_SwapToManufacturer when product data is available."
    )

    # Compose outer manually so single-element 'symbols' stays a JSON array
    # (PS 5.1 ConvertTo-Json unwraps 1-element arrays at the property level).
    $symJson   = ($sym   | ConvertTo-Json -Depth 12)
    $notesJson = ($notes | ConvertTo-Json)
    $stdJson   = ($s.std | ConvertTo-Json)
    $out = "{`n  `"version`": `"1.0`",`n  `"standard`": $stdJson,`n  `"_notes`": $notesJson,`n  `"symbols`": [`n$symJson`n  ]`n}`n"

    $path = Join-Path $seedDir ($id + '.json')
    [IO.File]::WriteAllText($path, $out, (New-Object System.Text.UTF8Encoding($false)))
    $written++
}
Write-Host ("Wrote {0} seed specs to {1}" -f $written, $seedDir)
