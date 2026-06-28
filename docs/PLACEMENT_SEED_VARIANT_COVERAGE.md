# Placement ↔ Seed-Family Variant Coverage (research)

**Overall: 254/370 rules (68%) match a variant in their mapped seed. 116 gaps below.**

Each gap = a rule whose FamilyTypeRegex/VariantHint matches NO type variant minted by its mapped seed, so placement falls back to the seed's first symbol (wrong type).

## Specialty Equipment — 52 gap(s)  → seed `STING_SEED_SpecialityEquipment`

- `hc-bedhead-trunking` [coverage] wants `BEDHEAD,TRUNKING`
- `hc-oxygen-suction-outlet` [coverage] wants `O2,SUCTION,4GAS`
- `hc-ceiling-pendant` [coverage] wants `PENDANT`
- `ed-classroom-ifp` [coverage] wants `IFP,WHITEBOARD`
- `ed-classroom-ifp-ks1` [coverage] wants `IFP,WHITEBOARD`
- `toilet-tp-holder-single-right` [coverage] wants `(?i)paper holder|toilet roll|tp holder|roll holder`
- `toilet-tp-holder-double-right` [coverage] wants `(?i)double.roll|twin.roll|double.paper`
- `toilet-tp-holder-recessed-right` [coverage] wants `(?i)recessed.paper|in.wall.roll`
- `toilet-grab-bar-side-right` [coverage] wants `(?i)grab bar|grab rail|side bar|stainless grab`
- `toilet-grab-bar-side-left-ada` [coverage] wants `(?i)fold.down|swing.down|hinged grab|left grab`
- `toilet-grab-bar-rear` [coverage] wants `(?i)rear grab|back.wall grab|behind toilet`
- `toilet-sanitary-bin` [coverage] wants `(?i)sanitary.bin|feminine.disposal|hygiene.bin|nappy.bin`
- `toilet-brush-holder` [coverage] wants `(?i)toilet brush|brush holder`
- `toilet-soap-dispenser-wall` [coverage] wants `(?i)soap dispenser|liquid soap|hand soap|sanitiser`
- `toilet-soap-dispenser-counter` [coverage] wants `SOAP_DISPENSER_COUNTERTOP`
- `toilet-mirror-standard` [coverage] wants `(?i)mirror|vanity mirror|looking glass`
- `toilet-medicine-cabinet` [coverage] wants `(?i)medicine cabinet|mirrored cabinet|bathroom cabinet`
- `toilet-towel-ring` [coverage] wants `(?i)towel ring|hand towel ring`
- `toilet-towel-bar` [coverage] wants `(?i)towel bar|towel rail|hand towel bar`
- `toilet-towel-hook` [coverage] wants `(?i)robe hook|bathrobe hook|towel hook`
- `toilet-paper-towel-dispenser` [coverage] wants `(?i)paper towel|towel dispenser|roll towel`
- `toilet-waste-bin` [coverage] wants `(?i)waste bin|rubbish bin|bin|litter bin|general waste`
- `toilet-coat-hook` [coverage] wants `(?i)coat hook|clothes hook|hook`
- `toilet-baby-changing-station` [coverage] wants `(?i)baby changing|changing station|nappy changing|infant changing`
- `toilet-bathroom-shelf` [coverage] wants `(?i)bathroom shelf|accessories shelf|vanity shelf`
- `shower-seat-fold-down` [coverage] wants `(?i)shower seat|fold.down seat|shower bench|flip seat`
- `shower-grab-bar-horizontal` [coverage] wants `(?i)shower grab|horizontal grab|shower rail`
- `shower-curtain-rod` [coverage] wants `(?i)shower curtain|curtain rod|curtain rail|shower screen`
- `shower-shampoo-niche` [coverage] wants `(?i)shampoo niche|shower shelf|recessed shelf|shampoo ledge`
- `toilet-feminine-napkin-disposal` [coverage] wants `(?i)napkin disposal|sanitary towel|feminine|SWD|FHD`
- `toilet-entrance-mat` [coverage] wants `(?i)entrance mat|anti.slip mat|floor mat|wet floor mat`
- `toilet-sanitary-vending-machine` [coverage] wants `(?i)vending machine|sanitary vending|dispenser machine`
- `a11y-grab-rail-wc-horizontal` [coverage] wants `GRAB_RAIL,HORIZONTAL`
- `a11y-grab-rail-wc-vertical` [coverage] wants `GRAB_RAIL,VERTICAL`
- `a11y-grab-rail-bath` [coverage] wants `GRAB_RAIL`
- `a11y-grab-rail-shower` [coverage] wants `GRAB_RAIL`
- `a11y-emergency-pull-cord-wc` [coverage] wants `PULLCORD,EMERGENCY`
- `a11y-hearing-loop-perimeter` [coverage] wants `LOOP_CABLE`
- `a11y-accessible-entrance-bell` [coverage] wants `BELL,INTERCOM`
- `a11y-tactile-hazard-stair` [coverage] wants `TACTILE_HAZARD`
- `a11y-tactile-direction-change` [coverage] wants `TACTILE_DIRECTION`
- `a11y-mirror-accessible-wc` [coverage] wants `MIRROR,TILTED`
- `a11y-shelf-accessible-height` [coverage] wants `SHELF`
- `a11y-signage-room-number` [coverage] wants `ROOM_SIGN,TACTILE`
- `a11y-signage-directional` [coverage] wants `DIRECTIONAL_SIGN`
- `a11y-lowered-counter-section` [coverage] wants `LOWERED`
- `a11y-door-entry-button` [coverage] wants `PUSH_PAD,DDA`
- `a11y-lift-call-button` [coverage] wants `LIFT_CALL`
- `a11y-ramp-handrail` [coverage] wants `HANDRAIL`
- `a11y-parking-bay-marker` [coverage] wants `DISABLED_BAY`
- `a11y-visual-contrast-floor` [coverage] wants `CONTRAST_STRIP`
- `a11y-height-adjustable-desk` [coverage] wants `HEIGHT_ADJUST`

## Lighting Fixtures — 6 gap(s)  → seed `STING_SEED_LightingFixture`

- `toilet-mirror-light` [coverage] wants `(?i)mirror light|vanity light|over.mirror|face.lit`
- `downlight-kitchen-ip44` [coverage] wants `(?i)Downlight.*IP44|Kitchen`
- `highbay-industrial` [coverage] wants `(?i)HighBay|High *Bay`
- `baseline-light-stair-tread` [coverage] wants `WALLWASH`
- `baseline-light-rcp-uplighter` [coverage] wants `UPLIGHTER,INDIRECT`
- `baseline-light-daylight-sensor-pair` [coverage] wants `DAYLIGHT`

## Security Devices — 5 gap(s)  → seed `STING_SEED_SecurityDevice`

- `elec-security-camera-stairwell` [coverage] wants `DOME`
- `baseline-security-devices-cctv-bullet-external` [coverage] wants `BULLET`
- `baseline-security-devices-pir-corridor-wide` [coverage] wants `WIDE,DUAL-TECH`
- `baseline-security-devices-safe-room` [coverage] wants `HIDDEN`
- `baseline-security-devices-carpark-licence-plate` [coverage] wants `ANPR`

## Plumbing Fixtures — 5 gap(s)  → seed `STING_SEED_PlumbingFixture`

- `toilet-wc-wall-hung` [coverage] wants `(?i)wall.hung|back.to.wall|concealed|frameless`
- `toilet-wc-no-window` [coverage] wants `CLOSE_COUPLED,WATER_CLOSET`
- `baseline-plumbing-shower-thermostatic` [coverage] wants `THERMOSTATIC`
- `baseline-plumbing-lab-sink` [coverage] wants `LAB`
- `baseline-plumbing-bedpan-washer` [coverage] wants `BEDPAN,SLUICE`

## Lighting Devices — 5 gap(s)  → seed `STING_SEED_LightingDevice`

- `mk-switch-cooker` [coverage] wants `(?i)cooker|45A`
- `baseline-lighting-devices-outdoor-porch` [coverage] wants `OUTDOOR`
- `baseline-lighting-devices-conference-scene` [coverage] wants `SCENE,MULTI`
- `baseline-lighting-devices-exterior-wall` [coverage] wants `OUTDOOR`
- `baseline-lighting-devices-sensor-daylight` [coverage] wants `DAYLIGHT,PHOTOELECTRIC`

## Nurse Call Devices — 4 gap(s)  → seed `STING_SEED_NurseCallDevice`

- `hc-nurse-call-reset` [coverage] wants `RESET`
- `toilet-emergency-pull-cord` [coverage] wants `(?i)pull cord|emergency cord|alarm pull`
- `toilet-emergency-cord-healthcare` [coverage] wants `HEALTHCARE_PULL_CORD,NURSE_CALL_TOILET`
- `baseline-communication-devices-nurse-call-cord` [coverage] wants `PULLCORD`

## Electrical Fixtures — 3 gap(s)  → seed `STING_SEED_ElectricalFixture`

- `toilet-hand-dryer` [coverage] wants `(?i)hand dryer|air dryer|blade dryer|dyson airblade`
- `baseline-electrical-fixtures-cleanroom-flush` [coverage] wants `FLUSH`
- `baseline-electrical-fixtures-cinema-theatre` [coverage] wants `DIMMED`

## Conduits — 3 gap(s)  → seed `(none)`

- `mk-conduit-1g-socket` [no-seed] wants `category has no seed`
- `mk-conduit-2g-socket` [no-seed] wants `category has no seed`
- `mk-conduit-1g-switch` [no-seed] wants `category has no seed`

## Air Terminals — 2 gap(s)  → seed `STING_SEED_AirTerminal`

- `baseline-air-terminal-fcu-cassette` [coverage] wants `CASSETTE,FCU`
- `baseline-air-terminal-fan-coil-wall` [coverage] wants `FCU,WALLMOUNT`

## Windows — 2 gap(s)  → seed `STING_SEED_Window`

- `win-hospital-ward` [coverage] wants `ANTI-LIGATURE`
- `win-fire-rated-corridor` [coverage] wants `E60`

## Stairs — 1 gap(s)  → seed `(none)`

- `arch-stair-handrail` [no-seed] wants `category has no seed`

## Data Devices — 1 gap(s)  → seed `STING_SEED_DataDevice`

- `elec-data-cctv-poe-uplink` [coverage] wants `POE,UPLINK`

## Furniture — 1 gap(s)  → seed `STING_SEED_Furniture`

- `hc-furniture-bed-clearance` [coverage] wants `BED`

## Communication Devices — 1 gap(s)  → seed `STING_SEED_CommunicationDevice`

- `mk-nurse-call` [coverage] wants `NURSE_CALL`

## Electrical Equipment — 1 gap(s)  → seed `STING_SEED_ElectricalEquipment`

- `mk-cooker-circuit-feeder` [coverage] wants `(?i)Cooker.*Connection|Outlet.*45A`

## Fire Alarm Devices — 1 gap(s)  → seed `STING_SEED_FireAlarmDevice`

- `baseline-fire-alarm-aspirating-cleanroom` [coverage] wants `ASPIRATING,ASD`

## Sprinklers — 1 gap(s)  → seed `STING_SEED_Sprinkler`

- `baseline-sprinklers-rack-storage` [coverage] wants `INRACK`

