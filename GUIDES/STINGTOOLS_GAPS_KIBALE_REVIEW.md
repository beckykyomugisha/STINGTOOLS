# STINGTOOLS — Gaps & Enhancement Register

> Produced while planning the **Kibale NP lodge** project (see [KIBALE_NP_BIM_MODELLING_PLAYBOOK.md](KIBALE_NP_BIM_MODELLING_PLAYBOOK.md)).
> Every entry is evidence-backed with a file:line citation. Nothing here has been fixed — this is the register.
> **Date of review: 2026-08-08.** Re-verify before acting; the codebase moves.

## How to read this

| Severity | Meaning |
|---|---|
| **P0** | Silently produces a wrong deliverable. A user cannot tell it went wrong. |
| **P1** | Blocks or badly degrades a real workflow; the user notices but cannot fix it. |
| **P2** | Friction, drift, or a missing capability with a manual workaround. |

---

# Part A — Correctness gaps

## A-1 · P0 · A failed quantity silently becomes zero

`StingTools/BOQ/Takeoff/TakeoffRule.cs:224-232`

```csharp
case "each": case "item": case "nr": case "no": case "": return 1.0;
default: return 0.0;   // m / m² / m³ / kg
```

When `EvaluateQuantity` cannot resolve a rule's `quantitySource`, a **measured** unit falls back to `0.0`. The row is still produced — with a description, a classification, a rate and a section — and reads as a genuine, cheap item. Nothing in the export, the rate audit or `BOQPrepForExport` flags a zero-quantity measured line.

**Why it matters here:** eight buildings' worth of walls, floors and roofs. One bad rule or one unbound geometry parameter and a whole trade quietly prices at nil.

**Suggested fix:** return `double.NaN` or set a `QuantityResolved = false` flag on the line; surface the count in `BOQPrepForExport` as a hard gate ("N measured lines have zero quantity"), and tint them in the export.

## A-2 · P0 · Two Uniclass parameter sets that never meet

- **Writer:** `UniclassClassify` → `Temp/StandardsEngine.cs:333-378, 742-792`. Writes `ASS_CLASS_COD_TXT` / `ASS_CLASS_DESC_TXT` from a **21-entry `BuiltInCategory` dictionary hard-coded in C#** — not a data file, not extensible without a rebuild.
- **Reader:** `Core/Classification/ClassificationReader.cs:33-45`. The canonical resolver used by BOQ, COBie, handover and IFC export reads `UNICLASS_PR_TXT`, `UNICLASS_SS_TXT`, `UNICLASS_EF_TXT`, `NBS_CODE_TXT`.

**The automatic command does not populate the parameters the reader consumes.** Run "Uniclass classify", get classification data the BOQ never sees, and the fallback chain drops to `Native.Family`.

**Suggested fix:** point the writer at `UNICLASS_SS_TXT` / `UNICLASS_PR_TXT`, and move the 21-entry map to `Data/STING_UNICLASS_MAP.csv` with the standard corporate-baseline + project-override loader.

## A-3 · P1 · Repeated links are taken off once unless you find a second checkbox

`StingTools/BOQ/BOQCostManager.cs:3251` — `if (!seenTitles.Add(linkName)) continue;`

A link placed N times is quantified **once**. The ×N multiplier (`BOQCostManager.cs:3297-3319`) is opt-in per link, via a *second* picker that only appears after you have already ticked the link for inclusion (`UI/BOQCostManagerPanel.cs:686-708`).

The default is defensible — a shared reference model placed once is the common case. The **discoverability** is not. A user who links a cottage seven times and exports gets one cottage, with no warning.

**Suggested fix:** when a link is included and `instanceCount > 1` and the multiplier is off, emit a prominent warning row in the BOQ audit sheet and a gate in `BOQPrepForExport`: *"Link 'C01' is placed 7× but is taken off ×1."* Let the user confirm rather than discover.

## A-4 · P1 · Room-finish default is a UK office

`StingTools/Model/PlasteringEngine.cs:982-983`

```csharp
if (string.IsNullOrEmpty(entry.FloorFinish))
    entry.FloorFinish = "Power-floated concrete + carpet/vinyl";
```

`RoomFinishScheduler` invents a finish when `BLE_ROOM_FINISH_FLOOR_TXT` is empty. On a safari lodge with parquet and screed, running "Room Finishes" on an unpopulated model writes carpet into every room and the finishes bill follows.

**Suggested fix:** leave it empty and report the count of unset rooms. A silent plausible default is worse than a blank.

---

# Part B — Missing capability

## B-1 · P1 · No East African / AAQS method of measurement

`StingTools/BOQ/MeasurementStandard/MeasurementStandards.cs` implements `Nrm2Standard` (`:33`), `Cesmm4Standard` (`:86`), `PomiStandard` (`:166`), `Icms3Standard` (`:202`), `MmhwStandard` (`:264`).

The regional standard across Kenya / Uganda / Tanzania / Rwanda is the **Standard Method of Measurement of Building Works for Eastern Africa (2nd ed., 2008)**, and the AAQS *Standard Method of Measuring Building Work for Africa*. Neither is present. POMI is the workable stand-in.

This is STINGTOOLS' home market. An `SmmEaStandard : IMeasurementStandard` — units, trade classes, deduction thresholds, description ladder — is the single most differentiating BOQ feature the product could add. The interface is small (`IMeasurementStandard.cs`: `PreferredUnit`, `ClassifyRow`, `BuildDescription`, `ApplyDeductions`) and the registry (`:304-324`) takes a new entry with one line.

## B-2 · P1 · No earthwork path at all

- `Data/STING_DEFAULT_COST_RATES.csv:115` → `Toposolid,60,m²` — priced by **area**.
- No takeoff rule targets the toposolid category.
- No command reads Revit's graded-region **Cut** / **Fill** properties.
- No topography command appears anywhere in `StingCommandHandler.cs`.

On a site with 27.75 m of fall, earthworks may exceed the cost of a building. Today they cannot reach the bill except as a manual row.

**Suggested fix:** a `Site_CutFillTakeoff` command that reads graded-region Cut/Fill from toposolids and emits measured rows in m³ (excavate / cart away / imported fill / compact), plus corporate takeoff rules and default rates for those four operations.

## B-3 · P2 · `ViewStylePack.Checksum` declared but never computed

Noted in `CLAUDE.md` already; repeating here so it sits with the rest. Wire it or drop it.

---

# Part C — Proposed tool: Scope Box Manager & Renamer

This is the one the project actually needs built. Specification follows from reconnaissance of everything that exists today.

## C-1 · Why

The binder grammar is strict and unforgiving (`Core/Drawing/ScopeBoxBinder.cs:51-53`):

```csharp
new Regex(@"^STING::([A-Za-z0-9_\-\.]+)(?:::([A-Za-z0-9_\-\.]+))?(?:::([A-Za-z0-9_\-\.]+))?$")
```

A space, a slash, or a mistyped drawing-type id and the box is skipped or warned. Today the only way to get a compliant name is to **type it by hand** into Revit's scope-box rename field, from memory, with no list of valid drawing-type ids in front of you. There are 93 of them.

Nothing today closes that loop:

| Existing | What it does | Why it is not enough |
|---|---|---|
| `DrawingTypes_SuggestFromScopeBoxes` (`StingCommandHandler.cs:6369-6398`) | prints one guessed **drawing-type id** per box | never proposes a `STING::` name; read-only text dump |
| `DrawingTypes_FromScopeBoxes` (`Commands/Drawing/GenerateFromScopeBoxesCommand.cs`) | generates views from already-correct names | consumes the grammar, does not help you satisfy it |
| `ScopeBoxManager` (`Docs/DocAutomationExtCommands.cs:1156+`) | **a TaskDialog stub** — three command links: audit usage, auto-assign, clear all | no grid, no rename, no selection, no grammar awareness |
| ProjectSetupWizard scope-box page (`UI/ProjectSetupWizard.xaml:338-397`) | grid with `Use` / `Current Name` / `Rename To` / `Angle`, plus a `{BLD}-{ZONE}-{INDEX}` pattern | **entirely unaware of `STING::`** — the wizard pattern and the binder grammar are disjoint; and it only exists during initial setup |

So the wizard already has 80 % of the UI, in the wrong place, producing the wrong names.

## C-2 · Where it goes

**Dock tab `DOCS` → section `📐 DRAWING TYPES`** (`UI/StingDockPanel.xaml:1413`), inserted in the main `WrapPanel` **immediately before line 1425**, so the three buttons read as a workflow:

```
Suggest From Scope Boxes  →  Scope Box Manager  →  Generate From Scope Boxes
   (read-only advice)         (fix the names)        (write the views)
```

XAML matching the surrounding style:

```xml
<Button Style="{StaticResource ActionBtn}" Content="Scope Box Manager"
        Tag="ScopeBoxManagerV2" Click="Cmd_Click"
        ToolTip="Rename scope boxes to the STING:: grammar with a drawing-type dropdown — no typos, live validation, click a row to select the box in the model."/>
```

**Decision needed:** the existing `ScopeBoxManager` tag has three call sites — `StingCommandHandler.cs:1312`, `UI/Modules/DocsCommandModule.cs:77`, and Sheet Manager (`UI/SheetManagerDialog.cs:580` toolbar + `:1334` context menu). Recommend **absorbing** it: point all three at the new dialog and retire the TaskDialog stub, keeping "audit usage" and "clear assignments" as action buttons in the new dialog's footer so nothing is lost.

## C-3 · What it does

**Grid, one row per scope box** (`OST_VolumeOfInterest`):

| Column | Type | Notes |
|---|---|---|
| ✔ | checkbox | include in the rename batch |
| Current name | read-only | |
| Status | read-only badge | 🟢 valid · 🟡 grammar OK but unknown drawing-type id · 🔴 has `STING::` prefix but fails the regex · ⚪ not a STING box |
| **Drawing type** | **combo** | populated from `DrawingTypeRegistry.ListAll(doc)` — `id`, with `Name`/`Purpose`/`PaperSize`/`Scale` as the display text. **This is the point of the whole tool: you cannot mistype an id you picked from a list.** |
| Level code | combo + free text | from the project's level vocabulary; `ZZ` for non-level-specific |
| Tag | free text | validated against `[A-Za-z0-9_\-\.]+` as you type |
| New name | read-only, computed | `STING::{type}::{level}::{tag}`, live |
| Rotation | read-only | degrees, from the existing `GetScopeBoxRotationDegrees` helper |
| Views | read-only | count of views already cropped to this box — warns before a rename that will orphan nothing but should still be visible |

**Behaviours**

1. **Click a row → select and zoom the box in Revit.** Copy the documented-safe modeless pattern verbatim from `UI/PlacementCenter/StingPlacementCenter.xaml.cs:1639-1653` (`Selection.SetElementIds` + `ShowElements`, no transaction required).
2. **Bulk fill.** Pick a drawing type once, apply to all checked rows. Same for level.
3. **Pattern with sequence.** Extend the wizard's token idea to the grammar: `STING::{type}::{level}::COT{INDEX:D2}` → `…::COT01`, `…::COT02`. Reuse the token vocabulary already documented at `ProjectSetupWizard.xaml:394` (`{BLD}`, `{LOC}`, `{ZONE}`, `{INDEX}`, `{NAME}`) and add `{TYPE}` / `{LEVEL}`.
4. **Live validation, three states,** shown per row before anything is committed. Red rows cannot be committed.
5. **Two-pass rename to survive swaps.** The existing implementation (`Temp/ProjectSetupCommand.cs:1094-1136`) is a single pass with a `takenNames` HashSet that **skips and warns** on collision — so an A→B / B→A cycle half-fails silently. The new tool must rename to temporary names first, then to targets, exactly as `BatchRenumberSheetsCommand` already does for sheets.
6. **Footer actions:** *Validate all* · *Audit view usage* (absorbed from the stub) · *Rename checked* · *Generate views now* (chains straight into `DrawingTypes_FromScopeBoxes`).
7. **Sync-back.** After renaming, patch any in-memory name-keyed collections — the wizard already has this problem and solves it at `ProjectSetupCommand.cs:1138-1145`, because `CreateTwoSectionsPerScopeBox` matches boxes **by name**.

## C-4 · What to build it on

| Need | Use | Citation |
|---|---|---|
| Dialog shell | `StingDataGridDialog` — purple header, filter bar, DataGrid, action footer, status bar; grid is editable (`IsReadOnly = false`) and the raw `DataGrid` is exposed so you can add your own column types | `UI/StingDataGridDialog.cs:19, 115, 163, 210, 257` |
| Per-row combo | `DataGrid.Columns.Add(new DataGridComboBoxColumn{…})` via that exposed property — there is no built-in helper | `UI/StingDataGridDialog.cs:257` |
| Row model | copy `ScopeBoxRow : INotifyPropertyChanged` | `UI/ProjectSetupWizard.xaml.cs:1615` |
| Red/green row state | borrow `ListItem.IsInvalid` styling | `UI/StingListPicker.cs:18-26` |
| Enumerate boxes | `DocAutomationHelper.GetScopeBoxes(doc)` — already exists, already sorted | `Docs/DocAutomationExtCommands.cs:185-192` |
| Select in Revit | `SelectInModel(ElementId)` | `UI/PlacementCenter/StingPlacementCenter.xaml.cs:1639-1653` |
| Drawing type list | `DrawingTypeRegistry.ListAll(doc)`; then `Get(doc, id)` for the resolved profile | `Core/Drawing/DrawingTypeRegistry.cs:223, 46` |
| Modeless, not modal | follow `DrawingTypeEditorCommand.cs:25-33` — a modal WPF window blocks Revit's ExternalEvent queue | `Commands/Drawing/DrawingTypeEditorCommand.cs:25-33` |

**Do not** add a fourth name parser. There are already three:
- the authoritative regex (`ScopeBoxBinder.cs:51`, **private**)
- a looser `Split("::")` by index (`Commands/Drawing/BatchProduceCommands.cs:175-186`)
- a discipline sniffer (`Core/Drawing/MatchLineEngine.cs:684-700`)

## C-5 · The one prerequisite refactor

There is **no public way to validate a candidate scope-box name**. `ScopeBoxBinder._pattern` and `NamePrefix` are private, and `NameWarning` is only produced inside a whole-document `ScanProject`. You cannot ask "is this string legal?" without a `Document` and a full scan.

Add:

```csharp
public static bool TryParseName(string name, out ScopeBoxBinding binding, out string reason)
```

and refactor the loop at `ScopeBoxBinder.cs:83-106` to call it. That keeps **one** regex in the codebase and gives the dialog per-row validation. Then layer two more checks in the dialog:

- amber when `DrawingTypeRegistry.Get(doc, dtId) == null` — grammar fine, unknown type
- red on duplicate names, since Revit rejects them (`ProjectSetupCommand.cs:1094, 1114`)

## C-6 · Further automation worth having

- **Infer the drawing type from the box.** Size and aspect ratio predict paper size and scale; `DrawingTypes_SuggestFromScopeBoxes` already sniffs a discipline from the name. Combine them into a *proposed* row value the user confirms — suggestion, never silent application.
- **Reverse-generate boxes from buildings.** For a site like this, "create one scope box per linked building instance, aligned to its rotation, named from its LOC code" would replace an hour of manual work. The tilted-box maths already exists in `CreateTwoSectionsPerScopeBox` (`ProjectSetupCommand.cs:1161-1250`).
- **Round-trip to Excel**, matching `DrawingTypes_ExportExcel` / `_ImportExcel`, so a naming schedule can be agreed with the BIM manager offline.
- **`DrawingProductionConfigDialog` already exists** and takes `(availableTypes, contextLabels, commandType, doc)` returning selected contexts × drawing-type ids (`UI/DrawingProductionConfigDialog.cs:41-112`, called at `BatchProduceCommands.cs:190`). Check whether the new manager should hand off to it rather than duplicating the "which types, which boxes" step.

---

# Part D — Documentation drift in `CLAUDE.md`

Each of these sends a modeller looking for something that is not there.

| `CLAUDE.md` says | Reality |
|---|---|
| BOQ tags `BOQ_RateAudit`, `BOQ_Validate`, `BOQ_DeltaReport` | **None exist.** Real: `BOQ_RateGapReport`, `BOQPrepForExport`, `BOQSnapshotSave`/`BOQSnapshotCompare` (`StingCommandHandler.cs:3551-3579`) |
| `BOQSupportCommands.cs` — 506 lines | **984 lines**, 13 command classes |
| Project rate card at `_BIM_COORD/boq_rate_card.json` | **`_BIM_COORD/rate_card.json`** (`Rates/Providers/ProjectRateCardProvider.cs:50`) |
| Rate chain "BCIS → project rate card → material library → manual override" | Reverse: manual override 100 → ES 95 → material library 95 → CSV 90 → project rate card 87 → COBie 75 → default 60. BCIS is not in that file |
| `BOQ_DESCRIPTIONS.json` "keyed by section code" | Keyed by **category**, with the section as a payload field |
| `MultiBuilding_SetBldgCode` / `_AuditCodes` / `_SyncTags` / `_Export` | **None exist.** Real: `BuildingCodeSeed`, `PrjVolumeCodeAuto`, `SeqRangeValidation`, `BuildingAwareCDEFolders`, `FederationReview` |

---

---

# Part F — Level codes and building codes

The headline: **there is no single vocabulary for either, and the copies disagree.** This is the largest structural gap found in the review, and the one with the clearest fix.

## F-1 · P0 · The three LOC vocabularies do not talk to each other

| Key | Read by | Not read by |
|---|---|---|
| `LOC_CODES` | `TagConfig.LocCodes` (`TagConfig.cs:576, 733`) → tag writer, Excel import validator, token-writer UI, published picklists | — |
| `CUSTOM_VALID_LOC` | `ISO19650Validator.cs:161-166` — **the only key `ValidateTags` honours** | everything else |
| `LOC_CODES_EXTRA` | `LocVocabularyOverride.GetAllLocCodes()` (`MultiBuildingCommands.cs:24-42`), whose only two callers are `FederationReview` (`:252`) and `BuildingAwareCDEFolders` (`MultiBuildingExtraCommands.cs:90`) | the validator, `TagConfig`, Excel |

The comment above `LocVocabularyOverride` claims the extras are *"merged into the validator's accepted set."* **They are not.** The validator never calls it.

Compounding:
- `LOC_CODES_EXTRA` is **absent from `TagConfig`'s `knownKeys`** (`TagConfig.cs:694-715`), so every load logs `"unknown config key(s) … check for typos"` (`:720-724`) for the key the code itself tells you to use.
- The hard-coded base list in `LocVocabularyOverride` is `BLD1, BLD2, BLD3, EXT, XX`; `TagConfig.Defaults.cs:592-595` is `BLD1, BLD2, BLD3, EXT` — no `XX`. And if a project sets `LOC_CODES`, `LocVocabularyOverride` **ignores it** and starts from its own hard-coded five.
- Extras get `.Trim().ToUpperInvariant()` and nothing else — no length cap, no charset check. `BuildingAwareCDEFolders` then `Path.Combine`s the value into a **directory name** (`MultiBuildingExtraCommands.cs:103`).
- Non-array JSON parses successfully as a `JValue` and is **silently discarded** by `if (tok is JArray arr)`.

**Three different behaviours for an out-of-vocabulary LOC:** silently accepted (validator, lenient mode — the default); hard row failure, case-sensitive ordinal (`ExcelLinkCommands.cs:143-145`); red UI hint only (`TokenWriterCommands.cs:177-179`).

## F-2 · P0 · Untagged elements are silently filed under the first building

`StingTools/Core/TagConfig.cs:2295-2300`

```csharp
loc = LocCodes.FirstOrDefault(c => c != "XX" && !string.IsNullOrEmpty(c)) ?? "BLD1";
```

When LOC cannot be derived, the element is assigned to **whichever building happens to be first in the list**. No warning, no flag, no audit entry. On a multi-building project the first building silently absorbs every unplaceable element, and its cost, carbon and quantities are all wrong — while looking entirely plausible.

**Suggested fix:** assign `XX`, and report the count. `XX` is already a legal value the validator passes.

## F-3 · P1 · `BuildingCodeSeed`'s own level names defeat the level parser

`Core/MultiBuildingCommands.cs:84-102` produces `BLD2-L01-FFL`.
`ParameterHelpers.GetLevelCode` (`:483-571`) does not recognise the prefix, falls through to "extract digits" → `201` → returns **`L201`**.

Two STING features, shipped together, that break each other.

Also in the same command:
- **Storey height is hard-coded `double storey = 3.5;`** and the SSL offset hard-coded at 50 mm, while `SeedLevelCount` / `SeedGridsX` / `SeedGridsY` *are* configurable (`:69-82`).
- **It cannot create basements.** The loop starts at `i = 1`. The header comment at `:48-51` advertises `BLD2-B01-SSL @ -3.600` — **no code path produces it.** The comment is wrong.
- No mezzanines, no LG/UG, no plant levels, no split levels.

## F-4 · P1 · Five level-code vocabularies, mutually incompatible

| # | Where | Codes |
|---|---|---|
| 1 | `shared/ifc/enums/StingLevelCodes.xml` (IDS only, no C# reads it) | `B3 B2 B1 GF MZ L01…L10 RF PR XX *` |
| 2 | `ParameterHelpers.GetLevelCode` (`:483-571`) | `GF LG UG SB SB# B# RF PH AT TR POD MZ PL L##` + 12-char passthrough |
| 3 | `Iso19650Vocabulary.LevelCodes` (`:257-269`) — used for **file names** | `ZZ XX B2 B1 00 01 02 … 20 RF MZ PH` — **numeric, incompatible with #2's `GF`/`L01`** |
| 4 | `ISO19650Validator` (`:209-228`) | a *grammar*, not a list; ≤4 chars, alphanumeric |
| 5 | `ProjectSetupWizard.ProposeIsoLevelCode` (`:303-334`) | `B01 GF L01 MZ01 RF UR` — **`UR`, `MZ01`, `B01` are not producible by #2**, which yields `MZ` and `B1` |

Plus `TagConfig.cs:2308-2310` rewrites `XX` → **`L00`**, a code in none of the five.

Notable consequences:
- **`TagConfig` has no `LvlCodes` at all.** There is `DefaultLocCodes()` and `DefaultZoneCodes()` (`TagConfig.Defaults.cs:592-601`) but no level equivalent, and no `LVL_CODES` config key.
- A level name the parser cannot read becomes a **12-char passthrough pseudo-code** (`:555-564`) which then **fails the validator's 4-char limit**. STING generates a value its own validator rejects.
- Only **one mezzanine** is representable — `"Mezzanine 2"` also returns `MZ`, silently colliding.
- **Split levels are unhandled**: `"Level 1a"` → `L01`, colliding with Level 1. `"Level 1.5"` → `L15`.
- Plant level only matches when the name both starts with `plant` **and** contains `room` — `"Plant Level"`, `"Plantroom"` all miss.
- A **second divergent copy** of the logic exists at `ParameterHelpers.cs:3466-3488` (`DeriveLevelCodeFromName`, used by `SheetTagger`) knowing only `L## GF B# RF MZ`.

## F-5 · P1 · `LG` renders as "Level G" in bill descriptions

`Temp/DataPipelineCommands.cs:4724-4743`

```csharp
_ when lvl.StartsWith("B") => $"Basement {lvl.Substring(1)}",
_ when lvl.StartsWith("L") => $"Level {lvl.Substring(1).TrimStart('0')}",
_ => lvl
```

`LG` starts with `L`, so it becomes **"Level G"**. `MZ`, `UG`, `PH`, `AT`, `TR`, `POD`, `PL`, `SB` all fall through to the raw code, so a bill line reads *"MZ, Corridor"* instead of *"Mezzanine, Corridor"*. These strings go to the client.

## F-6 · P1 · Three different values all called "level"

- **the code** — `GetLevelCode` → tags, SEQ counters, 4D tasks, Excel, legends, publish
- **the raw Revit level name** — `{lvl}` drawing token (`DrawingProducer.cs:1352`, `DrawingTokenContext.cs:57` pass `ctx?.Level?.Name`, truncated to 8 chars); the scope-box binder's level segment (matched against `Level.Name`); `BOQCostManager.cs:758` (`Level = GetLevelName(doc, el)`) and therefore all BOQ level grouping and carbon pivots
- **the wizard's ISO-normalised name** — a third form again

So the sheet number, the BOQ grouping and the asset tag can each carry a *different* level identifier for the same element. Nothing reconciles them.

## F-7 · P2 · `PRJ_VOLUME_CODE` and `ASS_VOLUME_COD_TXT` are both dead

- `PRJ_VOLUME_CODE` is written once by `PrjVolumeCodeAuto` (`MultiBuildingCommands.cs:213`) from `fileName.Split('-')[2]`. **Nothing reads it** — not sheet numbers, not file names, not the validator.
- `ASS_VOLUME_COD_TXT` appears **exactly once in the whole C# tree**, as an alias registration (`ParamRegistry.cs:2751`). Never written, never read.

What sheet numbers and ISO file names actually use for `{vol}` is neither: it comes from the **DrawingType profile JSON** (`DrawingTokenContext.cs:64` → `dt?.IsoNaming?.Volume`). So a project with 11 buildings gets **one volume per drawing type**, not one per building, and the LOC the element carries never reaches the sheet number. For Kibale that means the ISO file/sheet naming cannot distinguish COT01 from COT08 without hand-authoring 11 drawing-type variants.

## F-8 · P2 · `TagConfig.LocPatterns` is dead code that is nearly the fix

`TagConfig.cs:582`, defaults at `:1253-1258`, loaded at `:795-797`. A name→LOC inference map already shaped as `"building 1"`, `"annex"`, `"block a"` → code. **Zero consumers outside `TagConfig.cs`.** It is the closest existing thing to the alias table F-9 needs, and it is wired to nothing.

## F-9 · The fix — one registry, following a pattern that already exists twice

Copy `AecFilterRegistry` (`Core/Drawing/AecFilterRegistry.cs:18-90` — its own header says *"Mirrors ViewStylePackRegistry / DrawingTypeRegistry in shape so consumers learn one pattern"*). Corporate baseline + project override + per-document cache + `Reload`.

**New — 2 files:**
- `Data/STING_SPATIAL_CODES.json` — two arrays of `{ code, label, aliases[], kind }`. Seed LEVEL from `shared/ifc/enums/StingLevelCodes.xml` plus the C# codes; seed LOC from `TagConfig.Defaults.cs:592-595` plus the dead `LocPatterns` (`:1253-1258`), which already has the alias shape. Project override at `<project>/_BIM_COORD/spatial_codes.json` — the folder the other two registries already use, so no new path plumbing.
- `Core/SpatialCodeRegistry.cs` — `LevelCodes(doc)`, `LocCodes(doc)`, `MatchLevel(doc, name)`, `MatchLoc(doc, name)`, `Prose(doc, code)`, `Reload(doc)`.

**Changed — the choke points; every other consumer inherits the fix:**

| File | Change |
|---|---|
| `Core/ParameterHelpers.cs:483-571` | replace the 80-line if-chain with a registry alias lookup; keep the sanitize-passthrough as the miss path. Delete the duplicate at `:3466-3488` |
| `Core/ISO19650Validator.cs:209-228`, `:161-179` | membership test against the registry; fold all three LOC keys into one set |
| `Core/TagConfig.cs:576, 694-715, 733` | `LocCodes` delegates to the registry; add the keys to `knownKeys`; wire or delete `LocPatterns` |
| `Core/MultiBuildingCommands.cs:24-42` | `GetAllLocCodes()` becomes a one-line forward |
| `Core/MultiBuildingCommands.cs:84-102` | seed from the registry's LEVEL entries with per-entry elevation — this is what unlocks basements, mezzanines and plant levels |
| `UI/ProjectSetupWizard.xaml:243-266` + `.xaml.cs:55-58, 289-334, 812-868` | add a Code column; bind the Type combo and `ProposeIsoLevelCode` to the registry; load presets from named JSON blocks instead of four hard-coded switch cases |
| `Temp/DataPipelineCommands.cs:4724-4743` | read `label` from the registry — kills the `LG → "Level G"` bug |
| `Core/SectorPackCommand.cs:17-29, 63-81` | **add one field and one line.** `SectorPack` currently carries `Families/Presets/TagStyle/PreambleProfile/BoqDefaults/WorkflowPresets` and *no* spatial key, so a hospital and a data centre get identical level vocabularies. A `spatial_codes` key in `Data/SectorPacks/*_PACK.json` is what delivers "flexibly configured according to project type" |

**Optional in the same pass** — align the three "level" values (F-6): point `DrawingProducer.cs:1352`, `DrawingTokenContext.cs:57` and `BOQCostManager.cs:758` at the registry-resolved code so sheet numbers, BOQ groupings and tags finally agree.

---

---

# Part E — Materials

The material library is **genuinely Uganda-tuned in its metadata and its carbon basis** — HIMA CEMENT on every cementitious row, UNBS 822-1 / US 28-2001 standards, blocks sized in inches with mm in brackets because that is how Kampala sells them, iron sheets by gauge, cement:sand renders by ratio, a real artisan clamp-kiln brick carbon factor. That is a serious regional asset and it should be said plainly.

The problem is not the data. It is the **wiring between the data and the bill**.

## E-1 · 🔴 P0 · Every material-library rate is ~3,700× too low, and it wins

`Temp/MaterialCommands.cs:378-382` writes Revit's `ALL_MODEL_COST` from **`MAT_COST_UNIT_USD`** (e.g. `8.0`).

`BOQ/Rates/MaterialLibraryRateProvider.cs:54-60` reads that value and labels it **UGX**, explicitly suppressing FX conversion:

```csharp
// CA-1 — ALL_MODEL_COST is entered in the PROJECT BASE
// currency (UGX) … Label it UGX so no FX conversion fires.
UnitRate = v,
CurrencyCode = "UGX",
```

So a material whose real rate is **29,600 UGX** prices at **8 UGX**. The CA-1 comment is correct for a human hand-editing the MAT panel and wrong for everything the CSV created — which is all 1,279 of them.

**It is not a harmless miss.** `MaterialLibraryRateProvider` sits at **priority 95**, above `CsvRateProvider` at 90. The wrong rate therefore *beats* the correct category rate. And `MAT_COST_UNIT_UGX` — the one correct-currency figure in the library — is written to shared param `MAT_COST_UGX`, which **nothing reads**.

**Fix, pick one:** change `MaterialCommands.cs:378` to read `ColCostUgx` (safer — makes `ALL_MODEL_COST` genuinely UGX and matches the comment), or revert `MaterialLibraryRateProvider.cs:60` to `"USD"` and let the FX layer convert.

**Until then: do not rely on any material-library rate.** Use `_BIM_COORD/rate_card.json` (priority 87, keyed on Revit category) or the per-element priority-100 override (`CST_RATE_SOURCE = "Override"` + `CST_UNIT_RATE_UGX`).

## E-2 · 🔴 P0 · Three dead carbon paths

1. **Tier 1 is dead.** `CarbonFactorResolver.cs:62` reads `STING_EMB_CARBON_NR`. **Nothing writes it.** The CSV's `PROP_CARBON_KG_M3` goes to a shared param of the same name that no resolver reads. The parameter already exists and is bound to Materials (`MR_PARAMETERS.csv:3358`, `CATEGORY_BINDINGS.csv:16823`) — it just needs a line in `SharedParamMappings`.
2. **The WLCA fossil/biogenic split is always 0.** `PROP_CARBON_FOSSIL_KG_M3` and `PROP_CARBON_BIOGENIC_KG_M3` (CSV cols 70/71) have **no column constants and no mappings** — `MaterialCommands.cs` constants stop at 69. Self-documented at `CarbonFactorResolver.cs:135-138`.
3. **`byMaterial` in `STING_CARBON_FACTORS_UG.json` is empty** — the one exact-match tier that would give per-material precision is unpopulated.

Consequence: **all carbon resolves by keyword substring or material class**, with a `200 kgCO₂e/m³` generic default catching the rest.

## E-3 · 🟠 P1 · Keyword-order landmines in the carbon table

`UgCarbonFactors.ResolveSpecific` matches `byKeyword` by **substring, first-hit-in-file-order**. Real consequences in the shipped data:

| Material name | Hits | Should be | Error |
|---|---|---|---|
| `BLOCK HOLLOW 200MM` (the `09_WALL_CORES` family) | no keyword → class `Masonry` = **250** | `concrete block` = 140 | **+79 %** |
| `TERRAZZO TILES 20MM` | `"tile"` = **700** (fired ceramic) | cement-bound terrazzo | **~2×** |
| `LIGHTWEIGHT SCREED 40MM` | class is **`Metal`** in the CSV → **12,200** | screed = 290 | **42×** |
| `HERRINGBONE BLOCK PAVING 65MM` | class is **`Wood`** → 160 | concrete paving | wrong sign of error |

The last two are straightforward data errors in `BLE_APP-IDENTITY-CLASS`.

## E-4 · 🟠 P1 · Material waste never applies to priced quantities

`WasteTable.ResolveWastePercent(material, category, …)` is called with **`material = null`** at all three cost sites — `BOQCostManager.cs:986, 1203, 1582`. The carbon path *does* pass it (`SustainElementCarbon.cs:62`).

So a tiled floor is carbon-counted at **10 %** waste and priced at **5 %** (category "Floors" matches no keyword → project default). `WasteTable.cs:11-12` claims *"a quantity is grossed up by the SAME allowance whether you are pricing it or carbon-counting it."* **That is false as shipped.**

## E-5 · 🟠 P1 · Two different "primary material" algorithms

- **Rates:** `MaterialLibraryRateProvider.cs:103-107` — the **first** id from `GetMaterialIds(false)`. Non-deterministic on a compound.
- **Carbon + bill description:** `BOQCostManager.cs:3889-3899` — the **dominant material by volume**. Deterministic.

A compound wall can be priced off its plaster skin and carbon-counted off its blockwork core.

## E-6 · 🟠 P1 · A rate miss is silent

`MaterialLibraryRateProvider.cs:89` returns `null` on a miss and the chain falls to a flat **category** rate — `Walls, 315000, m²` — regardless of whether the wall is 200 mm hollow block or a glazed screen. No warning reaches the bill; the only trace is a provenance string.

## E-7 · 🟡 P2 · `BLE_APP-IDENTITY-CLASS` is the leading noun of your bill description

`BOQCostManager.cs:1316-1320` prepends `Material.MaterialClass` to the NRM2 paragraph, and the fallback at `:1329-1332` is literally:

```csharp
$"Supply and fix {matClass.ToLower()} {catName.ToLower()}."
```

The library uses `Generic` for terrazzo, clay roof tiles, mineral-fibre ceiling tiles and roofing felt. Those bill as **"Supply and fix generic roofs."**

`STING_MATERIAL_CLASS_NORMALISER.csv` exists and would fix this (`^tile|^ceramic|^porcelain → Ceramic`) but **is not applied on the description path** — `:1298` reads `MaterialClass` raw.

## E-8 · 🟡 P2 · Dead Tier 2, schema drift, duplicate block families

- **`MaterialLibraryRateProvider` Tier 2 can never return non-zero.** `MATERIAL_LOOKUP.csv` has no `COST` / `COST_USD` / `RATE` property in any of its 113 groups, so `GetCost()` returns 0 unconditionally. This also kills `MaterialRow.cs:200`, `MaterialPackRegistry.cs:191`, `StingMaterialUpdater.cs:222`, `MaterialWhatIfEngine.cs:81-82`.
- **Schema drift:** `MATERIAL_SCHEMA.json` declares **70** columns; the CSVs carry **72**. The validator (`DataPipelineCommands.cs:902-903`) compares against the schema, so the two carbon columns are unvalidated.
- **Two overlapping block families** price at **UGX 2,220** (`04_WALLS`, per block) and **UGX 96,200–125,800** (`09_WALL_CORES`, per m²) — and **nothing in the schema records the unit**. `MaterialNameCache` will pick whichever it finds first.
- `SOURCE_SHEET` (col 0) is entirely unconsumed; `MAT_THICKNESS_INCH` duplicates `MAT_THICKNESS_MM`.

## E-9 · 🟡 P2 · East African materials that do not exist in the library

Zero hits anywhere in `StingTools/Data/`: **murram · makuti · thatch · eucalyptus · maxpan · sisal · papyrus**. `hardcore` exists as a waste keyword (`WasteTable.cs:77`) and two parameters (`CST_S_EAR_HARDCORE_*`) but has **no material row and no rate**. `mvule` appears exactly once, in a `MAT_SPECIFICATIONS` free-text field (`BLE_MATERIALS.csv:406`), not as a material.

For a Kibale lodge that means murram sub-base, hardcore filling, makuti thatch and eucalyptus/mvule timber must all be added by hand. Rows and the exact naming to use are in the playbook, Part 4A.

Note also the manufacturer field carries `KNAUF`, `OBO BETTERMANN`, `LEGRAND`, `CABLOFIL`, `ATKORE`, `TENMAT` on the MEP side — European/US brands, several not stocked in Uganda.

---

---

# Part G — The formula engine

**Summary judgement: the formula layer cannot currently be trusted to produce a quantity.** Not "has some bugs" — the three highest-volume failure paths all end in a silently-written `0`, and there is essentially no way for a formula to fail loudly.

Counts first, because every number in the code is stale: the CSV has **302 data rows**, the engine **loads 270**, and `MasterSetupCommand.cs:203-204` tells the user *"Evaluate Formulas (199 definitions)"*.

## G-1 · 🔴 P0 · `lookup()` is not implemented — 27 formulas return zero

The v4.0 header of `FORMULAS_WITH_DEPENDENCIES.csv` advertises *"material-variation-aware via lookup()"*. 27 formulas call `lookup(TABLE, KEY_PARAM, COLUMN)`.

`ExpressionParser.ParsePrimary` (`Temp/FormulaEvaluatorCommand.cs:1145-1163`) recognises only `if` and `log`. `lookup` falls through to variable lookup, misses, **returns 0**, and leaves the parser sitting on the `(` — so the remainder of the expression is discarded too.

```
CST_S_FRM_PLYWOOD_SHEETS_NR =
  (SLAB_AREA + BEAM_AREA) / lookup(PLYWOOD, SIZE_TXT, AREA_M2) * 1.15
```
→ divisor 0 → divide-guard returns 0 → `* 1.15` never reached → **writes 0**.

Killed by this: **all** cement / sand / aggregate / water take-off, **all** block and brick counts, **all** paint / primer / putty litres, tile adhesive, grout, plaster volume, rebar lap length, and `PER_SUST_CARBON_FOOTPRINT_KG`.

`docs/CHANGELOG.md:9351` references a `FormulaEngine.Lookup`. **No such method exists in this tree.**

## G-2 · 🔴 P0 · The CSV reader destroys every quoted literal

`Core/StingToolsApp.cs:2495-2519`:

```csharp
if (c == '"') { inQuote = !inQuote; }   // toggles, never appends
```

Field boundaries survive; **quote characters are stripped from the content**. 54 loaded TEXT formulas contain quoted literals.

```
ELC_FIX_TAG_1_TXT = ASS_ID_TXT + "-" + ASS_TAG_1_TXT
```
arrives as `ASS_ID_TXT + - + ASS_TAG_1_TXT`. The `-` is no longer a literal, isn't in the context, and is dropped. **You get `A1B2`, not `A1-B2`.**

It corrupts numeric formulas too. `FLS_SFTY_COVERAGE_AREA_SQ_M` compares a sprinkler head type against `"Standard Response"`; after stripping, `ParseIfCondition` finds no quote, falls back to numeric comparison, both sides parse to 0, `0 == 0` is true, and the orphaned word `Response` parses to 0. **Sprinkler coverage area is written as zero on every element.**

`StingTools.Boq.Tests/FormulaSelfRefTests.cs:76-100` ships a **correct RFC-4180 parser**. The tests parse this file properly; production does not. Lift it.

## G-3 · 🔴 P0 · The TEXT path has no `if()` — 33 formulas are inert

`EvaluateText` (`:723`) splits on top-level `+`, emits quoted literals and `format(PARAM)` values, and **silently drops anything it doesn't recognise** (`// else skip unknown references`, `:753`).

33 of the 80 loaded TEXT formulas contain `if(`. Every single `WARN_*` threshold formula is in that set — the entire warning-threshold feature is dead. Example, `CSV:234`:

```
WARN_PER_THERM_U_VALUE_W_M2K_NR_WALLS = if(PER_THERM_U_VALUE_W_M2K > 0.70, " [!U > 0.70]", "")
```

## G-4 · 🔴 P0 · Unit conversion is applied to text parameters, and only by one caller

Two compounding defects.

**(a) It should not be applied at all.** `MR_PARAMETERS.txt` declares only `TEXT` (2,813), `YESNO` (265), `NUMBER` (221), `INTEGER` (93). **There is not a single `LENGTH`, `AREA` or `VOLUME` shared parameter.** Every `_MM` / `_SQ_M` / `_CU_M` value is text holding metric. Yet `FormulaEvaluatorCommand.cs:187-190` runs `ConvertToInternalUnits(result, formula.Unit)` before writing — converting metric to Revit-internal feet for a text field.

31 loaded formulas hit a real conversion case: `m` ÷0.3048 (8), `mm` ÷304.8 (11), `L` ÷28.3168 (6), `in` ÷12 (2), `m2` ÷0.09290304 (2), `kPa` ×1000, `CFM` ÷60. Meanwhile `m²`, `m³`, `kg`, `%`, `nr` fall to `default:` and pass through.

**So whether a formula is corrupted depends on whether the author typed `m2` or `m²`.** The proof is a matched pair with byte-identical expressions:

```
CST_S_MAS_NET_AREA_SQ_M       = WALL_AREA - OPENING_AREA   … unit m²   → correct
CST_S_MAS_NET_WALL_AREA_SQ_M  = WALL_AREA - OPENING_AREA   … unit m2   → 10.76× wrong
```

**(b) Only one of eight callers converts.** `FormulaEvaluatorCommand` converts. `TagPipelineHelper.RunFullPipeline` (`ParameterHelpers.cs:4250-4252`) does not — nor do `ScheduleCommands.cs:1457`, `ExcelLinkCommands.cs:940/1781/2207`, `SystemParamPushCommand.cs:849`, `FamilyStagePopulateCommand.cs:214`, `BatchTagCommand.cs:820`, `StateSelectCommands.cs:512`.

**The value in `PLM_INS_THICKNESS_MM` therefore depends on whether the user last ran Master Setup or Batch Tag.** For BOQ purposes that is non-deterministic data.

## G-5 · 🔴 P0 · Nothing fails loudly — every failure writes a zero

| Condition | Behaviour |
|---|---|
| Divide by zero | returns `0`, no log (`ParseMultiply:1083`) |
| Unknown identifier | returns `0` (`ParsePrimary:1163`) |
| Unresolved function (`lookup`) | returns `0`, discards rest of expression |
| `0^-1`, `(-1)^0.5` | returns `0` (`ParsePower:1098`) |
| Circular dependency | detected and logged — **then executed anyway** with stale inputs (`:530-547`) |
| TEXT in arithmetic | `double.TryParse` fails → `0`, no type error |
| **Partial context** | if *one* input resolves and the rest don't, the formula **runs** with the others as zero — a plausible wrong number instead of a skip |

And `WriteNumericResult:943` writes when `overwrite || |current| < 0.0001` — so **all of the above stamp `0` into the model**.

## G-6 · 🟠 P1 · 32 rows are silently dropped

`FormulaEvaluatorCommand.cs:392` — `if (cols.Length < 10) continue;`

32 CSV rows terminate after column 4. All are the long `*_TAG_7_PARA_*_TXT` narrative formulas. **Dropped with no log line.** (They would not have worked anyway — nested `if()` in the TEXT path, G-3.)

## G-7 · 🟠 P1 · `MULTI` discipline formulas never fire

`FormulaEvaluatorCommand.cs:91-93` treats `GEN`, `ALL` and empty as universal. **`MULTI` is not in that set**, so its 10 formulas only run on elements whose DISC token literally equals `MULTI`. Affects `ASS_FUNC_TXT`, `ASS_TAG_4/5/6_TXT`, `ASS_SEQ_NUM_TXT`, `ASS_INST_DETAIL_NUM_TXT`.

## G-8 · 🟠 P1 · Binding ambiguity makes the whole layer conditional

Of 302 formula targets, the data declares **289 as Type-only**, 10 Type+Instance, 3 Instance-only. But `CachedLookup` uses `el.LookupParameter(name)` — **instance-only, no type traversal**.

If the declared Type bindings were what the model had, ~289 formula writes would be dead. They probably are not, because the two binders in the repo **disagree**:

- `DataPipelineCommands.cs:792-793` honours the CSV column: `NewTypeBinding(catSet) : NewInstanceBinding(catSet)`
- `LoadSharedParamsCommand.cs:365-370` creates `InstanceBinding` **unconditionally**

So whether formulas write at all depends on which setup command was run. **That ambiguity is itself the finding** and should be settled before anyone trusts a formula output.

63 targets have no `CATEGORY_BINDINGS.csv` row at all — including `CST_S_MAS_BLOCKS_NR`, `CST_S_REI_TOTAL_WEIGHT_KG`, `CST_S_FRM_FORMWORK_AREA_SQ_M`, `PER_ACOUSTICS_RT60_S`.

## G-9 · 🟠 P1 · Federated-model cache hazard

`_formulasApplicableByType` is keyed on **`typeId.Value` with no document discriminator**. ElementIds collide across linked documents — type 123456 in Link A would serve its applicability list to type 123456 in Link B. `ClearFormulaApplicabilityCache` at batch boundary probably saves it today, but **the key is latently wrong** and this project runs eight links.

Also: `TagPipelineHelper._cachedFormulas` is TTL-based and **does not check file mtime** (`ParameterHelpers.cs:4474`), while `FormulaEngine._cachedFormulas` does. Edit the CSV mid-session and the two paths use different formula sets. And `_paramCache` caps at 50,000 with a 20 % eviction that iterates `Keys` in arbitrary order — it evicts 10,000 *random* entries, not the coldest. Expect thrash on a federated model.

## G-10 · 🟡 P2 · Genuine arithmetic and modelling errors

- **`PLM_DRN_FLW_RATE_LPS` is 1000× too small** — it applies the `* 0.5` half-full factor but **drops the `* 1000`** m³/s→L/s conversion that its sibling `PLM_PPE_FLW_LPS` has.
- **`BLE_FLR_TILE_QTY_NR` always returns 0** — it divides by `BLE_FINISH_TILE_SZ_TXT_W_MM`, which is not in its `Input_Parameters`, so it resolves to 0. It also assumes square tiles.
- **`CST_TOTAL_ROOFING_COST` and `CST_TOTAL_PLASTER_COST` both return 0** — their unit-price terms are missing from `Input_Parameters`.
- **U-value hardcodes λ = 0.72 W/mK** regardless of the wall material, directly contradicting the file's "material-variation-aware" header.
- **`CST_S_MAS_MORTAR_VOLUME_CU_M` has an unexplained `* 12`** — likely an order of magnitude out, unverifiable because the lookup table it depends on is unreachable.
- **`CST_S_EAR_DISPOSAL_VOLUME_CU_M = excavation * 0.25`** while backfill is `excavation − concrete`. Disposal should be `excavation − backfill`; the two are unreconciled and can exceed the excavation.
- **`FLS_EXIT_TRAVEL_DIST_M = sqrt(room_area) * 1.5`** is a geometric guess presented as a BS 9999 / Uganda NBC compliance value. Not defensible in a fire-strategy submission.
- **Copy-paste duplicates writing two answers for one quantity**: `BLE_FINISH_TILE_AREA_SQ_M` and `CST_CALC_TILE_M2` are byte-identical, but their consumers disagree — `CST_CALC_ADHESIVE_KG = tile × 5.5` (hard constant) vs `BLE_FINISH_ADHESIVE_WEIGHT_KG = tile × lookup(...)` (always 0). Same for two rebar weights, two block counts, two parking-space counts, two plot ratios.
- **Three `Input_Parameters` reference parameters that do not exist** in `MR_PARAMETERS.txt`: `RGL_NEMA_APPROVAL_TXT`, `RGL_KCCA_APPROVAL_TXT`, `RGL_UMEME_APPROVAL_TXT`.

## G-11 · 🔴 P0 for this project · Floors, screeds and finishes are not covered

- **There is no screed formula and no screed parameter.** `grep -i screed` across the formula CSV and `MR_PARAMETERS.txt` returns **nothing** — no thickness, no area, no volume.
- **There is no skirting length parameter and no skirting formula.** The only trace is an orphan `ASS_SKIRTING_TYPE_TXT` with no consumer. **Skirting cannot be taken off.**
- `BLE_FLR_AREA_SQ_M` and `BLE_CEILING_AREA_SQ_M` are both simply `ASS_ROOM_AREA_SQ_M` — *not floor geometry* — and only work if room area has been stamped onto the Floor/Ceiling element, which could not be confirmed.
- Every tile / grout / adhesive / plaster quantity is either `lookup()`-based (zero, G-1) or a hardcoded constant.

For a lodge whose floor finishes are the product being sold, this is the gap that matters most.

## G-12 · Priority order for fixes

1. Implement `lookup()` in `ParsePrimary`, **or delete the 27 formulas** — they currently write 0 into the BOQ.
2. Fix `ParseCsvLine` to un-escape `""` and preserve quotes. `FormulaSelfRefTests.cs:76-100` has a correct parser to lift.
3. Decide whether `ConvertToInternalUnits` should exist at all given no target is unit-typed — then apply that decision to **all eight** call sites.
4. Change `if (cols.Length < 10) continue;` to log, and repair the 32 truncated rows.
5. Add `if()` to `EvaluateText`, or move TEXT formulas onto the real parser.
6. **Make the silent-zero paths return `null` instead of `0`** so `WriteNumericResult` skips rather than stamping a false quantity. This one change converts an invisible failure class into a visible one.
7. Add the document key to `_formulasApplicableByType` before this ever runs across links.
8. Settle the Type-vs-Instance binding question and make the two binders agree.
9. Add screed and skirting parameters and formulas.
10. Correct the stale counts in `MasterSetupCommand.cs:203-204` and the four other places (199 → the real number).
