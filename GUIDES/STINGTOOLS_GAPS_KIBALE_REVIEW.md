# STINGTOOLS — Gaps & Enhancement Register

> Produced while planning the **Kibale NP lodge** project (see [KIBALE_NP_BIM_MODELLING_PLAYBOOK.md](KIBALE_NP_BIM_MODELLING_PLAYBOOK.md)).
> Every entry is evidence-backed with a file:line citation.
> **Date of review: 2026-08-08.** Re-verify before acting; the codebase moves.
>
> **The original header said "nothing here has been fixed". That is no longer true** —
> **41 of 80 entries are closed** on `claude/kibale-integration`, 7 more partial, 32 open.
> See the status index below.
>
> *(Counted, not estimated: the index has 66 rows but some cover a range — `C-1 … C-6` is
> six entries, `H-6 … H-11` six, `J-1 … J-4` four. Expanding those gives 81 identifiers, of
> which `E-1b` is a sub-variant of `E-1` rather than a separate entry — hence 80. Re-run the
> count after editing the index rather than incrementing the header by hand.)*

## How to read this

| Severity | Meaning |
|---|---|
| **P0** | Silently produces a wrong deliverable. A user cannot tell it went wrong. |
| **P1** | Blocks or badly degrades a real workflow; the user notices but cannot fix it. |
| **P2** | Friction, drift, or a missing capability with a manual workaround. |

| Status | Meaning |
|---|---|
| **closed** | Fixed, with the commit named. **Compile-verified only — see the caveat below.** |
| **partial** | Some of the entry is fixed; the remainder is described in the entry. |
| **open** | Untouched. |

> ⚠️ **"Closed" means the code changed and the build is green. It does NOT mean it was run
> in Revit.** Nothing in this batch has been exercised against a real model. `lookup()`
> alone turned 29 call sites from writing `0` to writing real quantities. Before relying on
> any closed entry, work through
> [`docs/KIBALE_REVIT_VERIFICATION.md`](../docs/KIBALE_REVIT_VERIFICATION.md).

## Standing practice — measure the data, do not reason from the code's assumptions about it

**Every wrong call in this batch was wrong because someone described the data instead of
counting it — and every one was caught the same way, by counting it.** Four times, in a
single batch, on entries that had already been reviewed:

| Claim | What the data said | Cost if it had shipped |
|---|---|---|
| G-13's draft used `GetProperty(...) == 0` as "not found" | `GetProperty` collapses *absent* and *legitimately zero*; **8 true zeros exist**, 6 reachable through the exact columns these formulas read | A C10 blinding pour's steel formula fails and — composed with G-5 — **skips a write whose correct answer is zero**, inverting G-5 on those rows |
| G-3 was "33 formulas" | 33 is the **post-drop** count. G-6 was silently dropping 32 rows; the real surface is **65 of 112** | An evaluator built and signed off against half the surface |
| G-4 was "one caller in eight" of 18 call sites | 15 of the 18 are Revit's own `UnitUtils`, 1 the definition, 1 a log string, 1 a doc comment — **exactly one real call site** | Time spent auditing 17 sites that were never involved |
| **G-3's condition shapes were "0 string comparisons" — my own measurement, and it was wrong** | The regex looked for `= "` and **missed `<>` entirely**. Of 265 conditions, **200 are `PARAM <> ""`**, which `EvaluateNumeric` cannot evaluate (`ParseComparison` has `<= >= < > =`, no `<>`) | **The proposed TextExpressionParser would have failed 42 of the 65 formulas.** Caught only by simulating against the real data *before* committing |

The last row is the one worth keeping. It was nobody's review failure — the design was
agreed, the reasoning was sound, and it was wrong. Nothing but running it against the
shipped file would have found it, and running it *after* committing would have found it in
a Revit session with a QS waiting.

### The pattern to copy: how `lookup()` (G-13) was verified

Before a line was written:

1. **Enumerate every call against the shipped data.** All 29 `lookup()` calls extracted
   from `FORMULAS_WITH_DEPENDENCIES.csv` and each table/column checked against
   `MATERIAL_LOOKUP.csv` — zero missing tables, zero missing columns.
2. **Rebuild the registry's own key scheme**, rather than assuming it —
   `"CAT TypeKey"`, `"CAT:TypeKey"`, bare TypeKey when globally unique, bare Category for
   the `DEFAULT` row.
3. **Simulate the exact resolution order under the worst cases** — key present but empty,
   and key absent entirely. All 29 resolved; zero failures.
4. **Only then commit.**

Steps 1–3 cost minutes and are why G-13 landed without a correction while three entries
around it needed one. Apply the same to anything data-driven: a claim about a data file is
a hypothesis until it has been counted.

## Standing practice — a gate wired into no workflow is worse than no gate

A gate that never runs does not merely fail to catch things. **Its baseline gets trusted**,
and a clean baseline is read as evidence the problem does not exist. So an unrun gate
actively manufactures false confidence, which is worse than the honest ignorance of having
no gate at all. It is the same defect as an empty result list standing in for an error.

Three instances in this codebase, all found the same way — by checking whether the thing
had ever actually executed, not whether it existed:

| Gate | What it claimed | What was true |
|---|---|---|
| `tools/check_path_discipline.ps1` | baseline **zero** hand-rolled paths | wired into **no workflow**, so it had never run in CI — while ~9 legacy sibling sites and **139** hand-rolled `_BIM_COORD` sites were live in the tree |
| 11 `WORKFLOW_*.json` presets | ran and **reported success** | every step keyed `tag` instead of `commandTag`, so each preset resolved **zero steps** and reported success on doing nothing |
| `StingTools.Clash.Tests` / `.Routing.Tests` | 97 test methods counted as coverage | had not **compiled** since mid-May 2026; a test project that will not compile reports nothing — no red, no count, no signal |

**This retroactively explains why IM-2's count could not be reconciled.** The number was
not miscounted; it was produced by a gate that had never executed, so it was never a
measurement in the first place.

**The check to run** is not "does the gate exist" but "has it ever produced output". For a
CI gate: is it named in a workflow file. For a test project: does it compile and does its
case count appear. For a preset: does it resolve a non-zero number of steps. Any gate added
from here must fail loudly when it cannot run — `.github/workflows/stingtools-unit-tests.yml`
now builds every test project and fails on any that will not compile, and the H-5 data gate
runs a `--self-test` step *before* validation that asserts it still rejects planted defects.
A gate that cannot fail must be treated as a gate that is not there.

### Status index

| Entry | Sev | Status | Closing commit |
|---|---|---|---|
| A-1 failed quantity → zero | P0 | **closed** | `abeb6142c` |
| A-2 two Uniclass parameter sets | P0 | **closed** | `316f70375` data + `0c5573c0c` code |
| A-3 repeated links taken off once | P1 | **closed** | `2cf15fb6f` |
| A-4 room-finish default invents carpet | P1 | **closed** | `d703a53a2` |
| B-1 no East African / AAQS method | P1 | open | |
| B-2 no earthwork path | P1 | **closed** | `8fa0faef6` — Site_CutFillTakeoff; **migration** §11 |
| B-3 `ViewStylePack.Checksum` unused | P2 | open | |
| C-1 … C-6 Scope Box Manager | — | **closed** | `15bc74155` `fd52eea1d` |
| E-1 / E-1b material rate ~3,700× low | P0 | **closed** | `2113dfedb` |
| E-2 three dead carbon paths | P0 | **closed** | `160e6d9c4` — **migration** M-5 |
| E-3 keyword-order landmines | P1 | open | |
| E-4 waste never applies to priced qty | P1 | **closed** | `3ad81dd54` |
| E-5 two "primary material" algorithms | P1 | **closed** | `92e8934b9` — there were FIVE, four non-deterministic |
| E-6 a rate miss is silent | P1 | **closed** | `4b62cd5cb` |
| E-7 identity class leads the description | P2 | **partial** | `a69ae361e` normalised 580 classes; the description path still reads `MaterialClass` raw |
| E-8 dead Tier 2, schema drift, dup blocks | P2 | **partial** | `a69ae361e` closed the 72↔73 schema drift; dead Tier 2 and duplicate block families remain |
| E-9 missing East African materials | P2 | open | |
| E-10 quarter of library at flat default | P0 | **partial** | `a69ae361e` |
| E-11 seven invalid identity classes | P0 | **closed** | `a69ae361e` |
| E-12 third of library has no density/carbon | P1 | open | |
| E-13 cost has no unit | P1 | **closed** | `a69ae361e` (`MAT_COST_UNIT_OF_MEASURE`) |
| E-14 `MAT_ISO_19650_ID` has no grammar | P1 | open | |
| E-15 material-name inconsistencies | P1 | **closed** | `a69ae361e` (34 names) |
| E-16 the full alignment fix | — | **partial** | `a69ae361e` |
| F-1 three LOC vocabularies | P0 | **closed** | `e20dccbcd` — ParseLocCode reads the registry; TagConfig.LocCodes bridged so project codes work with no new file |
| F-2 untagged → first building | P0 | **closed** | `11cbc38f8` — **migration**, see verification doc |
| F-3 `BuildingCodeSeed` defeats level parser | P1 | open | |
| F-4 five level-code vocabularies | P1 | open | |
| F-5 `LG` renders "Level G" | P1 | open | |
| F-6 three values called "level" | P1 | open | |
| F-7 volume-code params dead | P2 | open | |
| F-8 `LocPatterns` dead code | P2 | open | |
| F-9 the SpatialCodeRegistry fix | — | **partial** | report `c59f83ccd`; registry + baseline `e20dccbcd`. Both decisions taken (MZ2, GF/00). `GetLevelCode` NOT yet rewired |
| G-1 / G-13 `lookup()` not implemented | P0 | **closed** | `a9eec757f` `b88cc4b4c` |
| G-2 CSV reader destroys quoted literals | P0 | **closed** | `20e84ba50` — re-scoped: 98.6 % of impact is tag config |
| G-3 TEXT path has no `if()` | P0 | **closed** | `74f8cee84` — TextExpressionParser; 65 of 112 |
| G-4 unit conversion, one real call site | P0 | **closed** | `e356d0642` — conversion deleted; **migration**, see verification doc |
| G-5 nothing fails loudly | P0 | **closed** | `5ee46d27c` `5d7443105` |
| G-6 32 rows silently dropped | P1 | **closed** | `cd1fc03a9` — guard now logs; 32 rows repaired to 12 columns |
| G-7 `MULTI` formulas never fire | P1 | open | |
| G-8 Type-vs-Instance binding ambiguity | P1 | open | |
| G-9 federated-model cache hazard | P1 | open | |
| G-10 arithmetic and modelling errors | P2 | **partial** | mortar `× 12` `6433778b3`; drainage 1000× `4af14251f` (**migration** M-4); sweep of all 303 formulas found no others |
| G-11 floors, screeds, finishes uncovered | P0 | **partial** | K-2/K-3 land finish codes and floor creation; screed and skirting still absent |
| G-12 priority order | — | — | |
| G-14 the three BOQ-engine traps | — | **closed** | Trap 1 `abeb6142c`; traps 2 + 3 `9af15632b` |
| **G-15 formula take-off not material-aware** | **P0** | **open** | new — see entry |
| H-1 IFC writer reports success writing nothing | P0 | **closed** | `60e24be6a` `ba703bb23` |
| H-2 hardcoded currency in IFC/ERP export | P0 | **closed** | `8b1cfcf19` |
| H-3 gates report 100 % on an empty bill | P0 | **closed** | `b11289a7e` |
| H-4 swallowed sheet-name write | P0 | **closed** | `3a66fc285` + `1c59c8eb3` — 12 write sites, 8 files, new `Core/SafeWrite.cs` |
| H-5 no schema validation for data files | P0 | **closed** | `8a82ba60a` — self-tested gate |
| H-6 … H-11 | — | open | |
| K-1 room finishes never written | P0 | **closed** | `daf87d34b` `9f02aa3bf` |
| K-2 no finish code parameter or legend | P1 | **closed** | `9f02aa3bf` |
| K-3 nothing turns a finish into an element | P1 | **closed** | `6f700cbc2` |
| K-4 no per-element BOQ exclusion | P0 | **closed** | `2351a47a2` |
| K-5 `PHASE_CREATED` not filtered | P1 | open | |
| K-6 two category-exclusion lists | P2 | open | |
| K-7 `{lvl}` renders empty and silent | P1 | **closed** | `84a7919af` |
| K-8 ISO pattern contradicts the data | P2 | **closed** | `84a7919af` |
| K-9 token-lock check is dead code | P1 | open | |
| K-10 Mark mapped to two parameters | P1 | open | |
| K-11 Mark dedup drifts from asset ID | P2 | open | |
| K-12 `WriteToRooms` over-reports | P2 | **closed** | `daf87d34b` |
| J-1 … J-4 material price book | — | open | |

---

# Part A — Correctness gaps

## A-1 · P0 · A failed quantity silently becomes zero

`StingTools/BOQ/Takeoff/TakeoffRule.cs:224-232`

```csharp
case "each": case "item": case "nr": case "no": case "": return 1.0;
default: return 0.0;   // m / m² / m³ / kg
```

> **Status: closed** — `abeb6142c`. Compile-verified only.
>
> **Correction (2026-08-08).** This entry originally ended "…and nothing gated it." **That
> was wrong**, and the error mattered: a reviewer who checked would have found the claim
> false and deprioritised a real P0. `BOQModels.cs:104` `BlocksExport` already included
> `CouldNotMeasureCount`, and `BOQProfessionalExportCommand:92` already surfaced it, so the
> professional/tender export path **did** refuse. The framing below replaces it.

When `EvaluateQuantity` cannot resolve a rule's `quantitySource`, a **measured** unit falls back to `0.0`. The row is still produced — with a description, a classification, a rate and a section — and reads as a genuine, cheap item.

**Why it matters here:** eight buildings' worth of walls, floors and roofs. One bad rule or one unbound geometry parameter and a whole trade quietly prices at nil.

**The defect is gate topology plus signal quality — not an absence of gating.**

1. **Topology.** `BOQPrepForExport` — the one pre-flight a QS is told to trust, the command whose entire job is to answer "is this safe to export" — consulted **none** of the uncosted rollup. Its gates were compliance, containers, stale, BOQ band, warnings and placeholders. A downstream exporter caught the condition; the thing people actually run did not.
2. **Signal quality.** `CouldNotMeasureCount` *infers* the condition from `Quantity <= 0.0001` (`BOQCostManager.cs:2924`). That cannot separate "never measured" from "measured, and genuinely zero" — a demolition line, a zero-length segment, a nil provisional item. Carrying false positives, it can only ever be advisory. **That is precisely why it sat in a rollup rather than a gate**, and why "just gate on the existing count" was never available.

**Fix, on both axes** (`abeb6142c`): make the take-off report the failure *explicitly* —
`FallbackQuantity`/`EvaluateQuantity` return `double?`, null sets `BOQLineItem.QuantityResolved = false` and appends `[QUANTITY NOT RESOLVED]` — so the signal is false-positive-free; then gate `BOQPrepForExport` on that new `QuantityUnresolvedCount`, failing closed. `QuantityResolved` defaults **true** so every existing row, snapshot and clone keeps its meaning. `CouldNotMeasureCount` is kept as-is. `CostValidators` now separates `COST.QTY.UNRESOLVED` from `COST.QTY.ZERO`.

## A-2 · P0 · Two Uniclass parameter sets that never meet

- **Writer:** `UniclassClassify` → `Temp/StandardsEngine.cs:333-378, 742-792`. Writes `ASS_CLASS_COD_TXT` / `ASS_CLASS_DESC_TXT` from a **21-entry `BuiltInCategory` dictionary hard-coded in C#** — not a data file, not extensible without a rebuild.
- **Reader:** `Core/Classification/ClassificationReader.cs:33-45`. The canonical resolver used by BOQ, COBie, handover and IFC export reads `UNICLASS_PR_TXT`, `UNICLASS_SS_TXT`, `UNICLASS_EF_TXT`, `NBS_CODE_TXT`.

**The automatic command does not populate the parameters the reader consumes.** Run "Uniclass classify", get classification data the BOQ never sees, and the fallback chain drops to `Native.Family`.

**Suggested fix:** point the writer at `UNICLASS_SS_TXT` / `UNICLASS_PR_TXT`, and move the 21-entry map to `Data/STING_UNICLASS_MAP.csv` with the standard corporate-baseline + project-override loader.

> **Status: closed** — `316f70375` (data) + `0c5573c0c` (code). Compile-verified only.
>
> The entry described one defect. There were **three**, and the other two would each have
> kept the write path dead on their own:
>
> 1. **Wrong parameter** — the one described. Fixed by routing on the code's table prefix
>    (`Pr_`→`UNICLASS_PR_TXT`, `Ss_`→`UNICLASS_SS_TXT`, `EF_`→`UNICLASS_EF_TXT`). The map is
>    not single-table — 17 `Ss_` and 3 `Pr_` — so a blind repoint at `UNICLASS_SS_TXT` would
>    have filed Doors, Windows and Furniture as *systems*.
> 2. **Wrong element** — the writes targeted the instance. `UNICLASS_*` bind on **Type**, and
>    `ParameterHelpers.CachedLookup` is `el.LookupParameter` with **no type traversal**, so a
>    Type-bound write against an instance returns `false` on every element. This also applied
>    to the legacy `ASS_CLASS_COD_TXT` (Type-bound, `MR_PARAMETERS.csv:2272`) — the old
>    command's `"written to N elements"` line was reporting a write that never landed.
> 3. **A fourth name** — `UI/PlacementCenter/FamilyHintsBridge.cs` wrote
>    `STING_UNICLASS_PR_TXT`, a name in no parameter file, zero hits repo-wide outside that
>    one line.
>
> **Measured, not assumed.** The entry says "21-entry"; the dictionary held **20**. The right
> denominator is not the 206-category tag config but the **43 distinct categories** the
> `UNICLASS_*` parameters bind to — the map covered **18**. Seven rows derivable from a
> vetted corporate row by the same-system rule (duct fittings/accessories/flex/terminals →
> `Ss_55_30`; pipe equivalents → `Ss_45_30`) took it to **25 of 43**.
>
> **Left open, deliberately:**
> - **The 18 unmapped categories** (named in the header of `Data/STING_UNICLASS_MAP.csv`).
>   Each needs a real Uniclass lookup; inventing plausible codes is worse than a documented
>   gap. Now a data edit + `UniclassReloadMap`, no rebuild.
> - **`OST_StructuralColumns` / `OST_StructuralFraming`** carry correct codes but are not in
>   the bound 44, so their writes no-op. Closed under item 7 of the following round —
>   binding extension, Type. *(See the status line for that row.)*
> - **`ASS_CLASS_COD_TXT` retention.** Checked before deciding: **no code reads it** — the
>   command is its only reference — and it appears in no schedule, tag config, label
>   definition or COBie map. Kept for one release anyway, because it is a bound shared
>   parameter that live models may schedule. Retire it once that is confirmed.

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

> **Correction (2026-08-08): C-2 and C-4 contradicted each other, and the contradiction was
> load-bearing.** C-4 says build on `StingDataGridDialog`; the row above says it must be
> modeless. **Those two instructions cannot both be followed as written.**
> `StingDataGridDialog` assigns `DialogResult` on every close path — `:158`, `:218`, `:220`
> — and setting `DialogResult` on a window that was shown with `Show()` rather than
> `ShowDialog()` throws `InvalidOperationException`. Built as specified, **every close path
> throws**: the OK button, the Cancel button, and the window's X.
>
> **Resolution as landed** (`fd52eea1d`): `StingDataGridDialog` gains an `IsModeless` flag;
> when set, the close paths skip the `DialogResult` assignment and call `Close()` directly.
> The dialog is still the shell, it is genuinely modeless, and the existing ~40 modal
> callers are unaffected because the flag defaults false.
>
> **Generalisable point:** "reuse this dialog" and "must be modeless" is a combination worth
> checking explicitly whenever it appears in a spec — WPF's modal/modeless split is not a
> property you can bolt on at the call site.

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

## E-1b · 🔴 P0 · The UGX column is derived, not independent — which changes the fix

Measured across all 1,279 rows:

| File | Rows | UGX ÷ USD |
|---|---|---|
| `BLE_MATERIALS.csv` | 815 | **3700.0 on every single row** |
| `MEP_MATERIALS.csv` | 464 | 3700.0 on 441; 3750.0 on 16; 3722.2 on 7 (rounding) |

**`MAT_COST_UNIT_UGX` carries no independent information.** It is `MAT_COST_UNIT_USD × 3700`, baked in at authoring time.

This overturns the fix I first recommended. Switching `MaterialCommands.cs:378` to read the UGX column would hard-wire a stale 2026 exchange rate into every material in the library, permanently, and the value would silently drift wrong as the shilling moves.

**The correct fix is the other one:** the library's real price is **USD**. Label it honestly and let the FX layer convert.

```csharp
// BOQ/Rates/MaterialLibraryRateProvider.cs:54-60
UnitRate = v,
CurrencyCode = "USD",        // was "UGX"
```

`UGX_PER_USD` already exists as a config key (`BOQCostManager.cs:700-704`, default 3700) and the registry already rebases currencies. One word, and the whole library re-prices correctly and follows the rate.

The `MAT_COST_UNIT_UGX` column then becomes a derived convenience field. Either regenerate it whenever the FX changes, or delete it — but do not let two prices that can disagree sit side by side in the same row.

**Caveat that must be settled first:** the CA-1 comment is not wrong for its own case. A human editing `ALL_MODEL_COST` in Revit's material browser genuinely does type project-base currency. So the provider cannot distinguish "typed by a human in UGX" from "loaded from the CSV in USD". The clean answer is to stop overloading `ALL_MODEL_COST` and stamp a dedicated `STING_MAT_RATE_USD` + `STING_MAT_RATE_CCY` pair at material creation, leaving `ALL_MODEL_COST` to the human. See E-12.

## E-10 · 🔴 P0 · A quarter of the library resolves carbon at the flat default

Cross-referencing every material name and class against `STING_CARBON_FACTORS_UG.json`:

- **705 of 1,279 rows (55 %) match no `byKeyword` entry at all** — they depend entirely on the class tier.
- **302 of 1,279 (23.6 %) match neither a keyword nor a valid class**, so they resolve at `defaultPerM3 = 200`, flagged `uganda-edge:default`.

By class, the 302 break down as: `Generic` 179 · `Ceiling` 46 · `Paint` 33 · `Flooring` 25 · `Fabric` 6 · `Lining` 6 · `Plaster` 4 · `Carpet` 3.

## E-11 · 🔴 P0 · Seven identity-class values are not valid carbon keys — and `Generic` is the single largest class

Full distribution of `BLE_APP-IDENTITY-CLASS`:

| BLE (815) | | MEP (464) | |
|---|---|---|---|
| **Generic** | **253** | Metal | 225 |
| Masonry | 78 | **Generic** | **120** |
| Concrete | 76 | Plastic | 87 |
| Wood | 72 | Glass | 14 |
| Ceiling | 69 | Insulation | 10 |
| Metal | 66 | **Lining** | **7** |
| Paint | 56 | Masonry | 1 |
| Flooring | 55 | | |
| Plaster | 25 | | |
| Insulation | 22 | | |
| Plastic | 20 | | |
| Glass | 12 | | |
| Fabric | 6 | | |
| Carpet | 5 | | |

**`Generic` is 373 of 1,279 rows — 29 % of the whole library.** Every one of them bills as *"Supply and fix **generic** walls"* (`BOQCostManager.cs:1329-1332`) and resolves no carbon class.

Nine values — `Generic`, `Ceiling`, `Paint`, `Flooring`, `Plaster`, `Fabric`, `Carpet`, `Lining` — are **not keys in `byMaterialClass`**, and several are not valid Revit `MaterialClass` values either. `Ceiling` and `Flooring` are *element types*, not materials; they belong in `MAT_ELEMENT_TYPE`, which already exists and already carries `A-CLG` / `A-FLR`.

**Fix:** normalise the nine to the twelve valid classes (`Concrete, Metal, Wood, Masonry, Glass, Plastic, Insulation, Gypsum, Ceramic, Stone, Liquid, Earth`). `STING_MATERIAL_CLASS_NORMALISER.csv` already exists to do exactly this and is **not applied** on the description path.

## E-12 · 🟠 P1 · A third of the library has no density and no carbon figure

**438 of 1,279 rows (34 %) have `PROP_DENSITY_KG_M3 = 0` and `PROP_CARBON_KG_M3 = 0`** — 318 in BLE, 120 in MEP, the same rows in both columns.

No density means no mass take-off (`BOQCostManager.cs:1821` resolves density for kg-based lines). No carbon means the value that *would* feed `STING_EMB_CARBON_NR` — once someone wires it (E-2) — is zero anyway.

**Good news alongside it:** `MAT_NAME` is **unique across all 1,279 rows**, case-insensitively, with zero cross-file collisions. The primary key is sound, which is what makes the `byMaterial` exact-match tier viable.

## E-13 · 🟠 P1 · Cost has no unit, and cannot be given one

There is no unit column anywhere in the 72. `MAT_COST_UNIT_USD` is a number with no denominator. The 815 BLE rows span `A-FIN` (404), `A-FLR` (95), `A-RF` (89), `A-CLG` (81), `A-BLK` (54), `A-STR` (51) — which in practice means per-m², per-m³, per-block and per-item prices sitting in the same column, indistinguishable.

This is why the two block families price at UGX 2,220 and UGX 96,200 — one is per block, the other per m² — and nothing records which is which.

**A rate without a unit is not a rate.** Add `MAT_COST_UNIT_OF_MEASURE` to the schema and populate it before anyone relies on library pricing.

## E-14 · 🟠 P1 · `MAT_ISO_19650_ID` has no fixed grammar — it is not a schema, it is a habit

Measured across all 1,279 rows. The field is presented as a structured identifier, and it is the value stamped into Revit's **Keynote**. It has **between 4 and 9 hyphen-separated segments**:

| Segments | Count (BLE) | Example |
|---|---|---|
| 4 | 20 | `A-FIN-WALL-SK1` |
| 5 | 171 | `A-FIN-TILE-ADHESIVE-001` |
| 6 | 112 | `A-FLR-GRANOLITHIC-40MM-INT-SC04` |
| 7 | **304** | `A-CLG-GYPSUM-STANDARD-9.5MM-INT-GB01` |
| 8 | 206 | `A-CLG-GYPSUM-FIRE-RATED-12.5MM-INT-GB04` |
| 9 | 2 | `A-RF-PURLIN-STEEL-C-SECTION-75MM-EXT-B06` |

MEP is the same story: 5 to 8 segments.

The variance is not random — it comes from the *type* segment being free-text that may itself contain hyphens (`FIRE-RATED`, `C-SECTION`). So no parser can split this field reliably, and no downstream code does: it is written straight to Keynote and never read back.

**Last-segment style is three different conventions:**

| Style | BLE | MEP |
|---|---|---|
| Code + digits (`GB01`, `SC04`) | 472 | 344 |
| Digits only (`001`) | 300 | 116 |
| Letters only | 33 | 4 |
| Outliers (`UGEXT01`, `UGEXT02`, `UGEXT03`) | 3 | 0 |

**Fix:** either commit to a fixed grammar — `<DISC>-<ELEMENT>-<TYPE>-<SIZE>-<INT|EXT|GEN>-<SEQ>`, six segments, type segment sanitised of hyphens — and regenerate all 1,279 values from the columns that already exist; or stop calling it an ISO identifier and treat it as a free-text keynote. The current state promises structure it does not have.

## E-15 · 🟠 P1 · Material-name inconsistencies that break the substring matching

`MAT_NAME` is the join key for carbon, waste and rate resolution, all by **substring, first-hit**. Inconsistent naming therefore silently changes results.

Measured:

| Defect | Count | Evidence |
|---|---|---|
| Names not ALL-CAPS | **12** | `BRICK CORE Common Brick Single Skin`, `BRICK CORE Facing Brick Single Skin`, `BRICK CORE Engineering Brick Single Skin`, `BRICK CORE Reclaimed Brick Single Skin` — the whole `BRICK CORE` family |
| Dimension separator: `NNNxNNN` | 27 BLE + 35 MEP | `CERAMIC TILES 300X300MM` |
| Dimension separator: `NNN X NNN` | 2 BLE + 7 MEP | `LADDER CABLE TRAY 300MM X 1.5MM` |
| Names **not** ending in a size token | 289 of 815 (35 %) BLE, 366 of 464 (79 %) MEP | `TILE ADHESIVE STANDARD CEMENTITIOUS`, `FIRE ALARM SOUNDER` |
| Empty `MAT_ELEMENT_TYPE` | 5 (MEP) | `M-ELC-CABLE-TRAY-POWDER-COATED-WHITE`, `M-ELC-CABLE-TRAY-STAINLESS-304`, `M-HVC-DUCT-PAINTED-WHITE` |

Two things are **sound** and should be preserved by any fix: `MAT_NAME` is unique across all 1,279 rows, and `MAT_CODE` has **zero duplicates**.

**The rule to enforce:** `<MATERIAL/TYPE> <QUALIFIER> <DIMENSION>`, ALL-CAPS, dimensions as `NNNxNNN` with no spaces, thickness last with a unit suffix. Then a *name* alone tells you the carbon keyword, the waste keyword and the bill description qualifier — which is what the matching already assumes and the data does not yet deliver.

## E-16 · 🟠 P1 · The full data-alignment fix, in the order it must be done

Materials, model elements and the bill are three views of one dataset. Fixing one without the others just moves the mismatch. Do it in this order — each step depends on the one above.

1. **Normalise `BLE_APP-IDENTITY-CLASS`** to the twelve valid values. This is the largest single win: it fixes 373 `Generic` rows (29 %), removes the *"Supply and fix generic walls"* bill text, and gives 302 rows a real carbon class instead of the flat 200 default. Apply `STING_MATERIAL_CLASS_NORMALISER.csv` — it already exists and is already wired for other purposes.
2. **Normalise `MAT_NAME`** per E-15 — case, separators, dimension position. Do it *after* step 1, because the class fixes some of what the keyword matching was compensating for.
3. **Populate `byMaterial`** in `STING_CARBON_FACTORS_UG.json` for the ~300 materials a real project actually uses. Exact-match beats substring, and the tier is already supported and completely empty.
4. **Add `MAT_COST_UNIT_OF_MEASURE`** and populate it (E-13). Until then no library rate is safe at any currency.
5. **Fix the 438 rows with zero density and zero carbon** (E-12), or mark them explicitly as "no data" so they can be reported rather than silently defaulting.
6. **Regenerate or retire `MAT_ISO_19650_ID`** (E-14).
7. **Then** align model element type naming to it, so a wall type name and its material name agree on how they describe the same product.

Steps 1, 2 and 4 are pure data edits with no code change and can be scripted against the CSVs. Step 3 is data. Steps 6 and 7 need a convention decision first.

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

## G-2 · 🔴 P0 · The CSV reader destroys every quoted literal — **primarily a TAG CONFIGURATION defect, not a formula-engine one**

> **Status: closed** — `20e84ba50`. Compile-verified only.
>
> **Re-scoped (2026-08-08).** This entry is filed under Part G, the formula engine. That is
> where it was *found*, and it is the wrong place to file it. Measured across all 76 shipped
> `Data/*.csv` files, the fix changes **13,399 data rows in 13 files** — and **13,212 of
> them (98.6 %) are the eight `STING_TAG_CONFIG_v5_0_*` files.** The formula engine's 88
> rows are secondary by two orders of magnitude.
>
> **What was actually broken: every Revit label formula the tag config carries.**
>
> ```
>   in the CSV :  if(TAG_PARA_STATE_2_BOOL, ASS_TAG_2_TXT, "")
>   as read    :  if(TAG_PARA_STATE_2_BOOL, ASS_TAG_2_TXT, )
> ```
>
> A Revit family formula with an empty third argument. That single line makes the defect
> legible: it is not "some literals lose their quotes", it is **the tag-family label layer
> being fed malformed formulas on every load**.
>
> **The evidence that no caller depended on the old behaviour** — this is the part worth
> keeping, because "53 call sites" made the change look unshippable:
>
> | Measure (76 CSVs) | Result |
> |---|---|
> | Rows compared, excluding comment lines | 64,779 |
> | Rows where output differs | **13,399** |
> | Rows where **field COUNT** differs | **0** |
> | Differences that restore a quote | 13,399 |
> | Differences that remove a quote | **0** |
>
> Field count unchanged on every row ⇒ **no caller's column indexing moves**, which is the
> only way a shared parser used by 53 files could break silently. And the change is strictly
> one-directional ⇒ nothing can depend on information the old parser produced, because it
> only ever destroyed. Counting *including* comment lines gives 13,432; the 33-row gap is
> comment text, not data. State the method with the number — an unqualified count here is
> what made two independent measurements disagree.
>
> Every quote-manipulating call site checked: all are CSV **writers** escaping output. The
> single reader-side `Trim('"')` is on a header and is idempotent.

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

## G-3 · 🔴 P0 · The TEXT path has no `if()` — **65 of 112 formulas are inert**

> **Corrected (2026-08-08): the headline "33" counts only the post-drop set.**
>
> | Measure | Count |
> |---|---|
> | TEXT formulas in `FORMULAS_WITH_DEPENDENCIES.csv` | **112** |
> | …containing `if(` | **65** |
> | …that survive loading today | 80 |
> | …loaded **and** containing `if(` — the original "33" | 33 |
>
> The gap is **G-6**: `FormulaEvaluatorCommand.cs:392` drops 32 rows with no log line, and
> those rows are themselves long nested-`if()` TEXT formulas. So "33" is not the size of the
> defect, it is the size of the part of the defect currently reachable.
>
> **G-6 is now closed.** Re-measured after the 32 rows were repaired to 12 columns:
> all 302 formulas load, all 112 TEXT formulas load, and **65 loaded TEXT formulas contain
> `if(`** — up from 33. **65 is the live figure an implementation has to satisfy**, and it
> is no longer a post-drop undercount.
>
> Shape of the 65, which determines what the fix has to be: **all 65 begin with `if(`**,
> **36 are nested**, and **all 65 use a comparison operator**. Nesting and comparison are the
> norm here, not edge cases — so this needs a recursive string-valued evaluator, not an
> `if()` special-case bolted onto the concatenation tokenizer.

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

## G-13 · ✅ The `lookup()` fix is far cheaper than it looks — **implement it, do not delete the formulas**

You asked whether `CST_S_*` and `BLE_FINISH_*` can be made usable rather than deleted. **Yes — and the missing piece is only the parser function. All the data already exists.**

I enumerated every `lookup()` call in the 27 formulas and checked each against `MATERIAL_LOOKUP.csv`:

```
Tables referenced by formulas but ABSENT from MATERIAL_LOOKUP.csv:  none
Columns absent within an existing table:                            none
```

Every one of the 27 tables/columns resolves. The full set needed:

| Table | Columns the formulas ask for |
|---|---|
| `CONCRETE` | `CEMENT_BAGS_PER_M3`, `SAND_RATIO`, `AGGREGATE_RATIO`, `WATER_PER_BAG`, `STEEL_KG_PER_M3`, `CARBON_KG_PER_M3` |
| `MORTAR` | `CEMENT_BAGS_PER_M3`, `SAND_RATIO` |
| `BLOCK` | `BLOCKS_PER_M2` |
| `BRICK_BOND` | `BRICKS_PER_M2`, `MORTAR_RATIO`, `WASTE_PCT` |
| `TILE` | `ADHESIVE_KG_PER_M2`, `WASTE_PCT` |
| `GROUT` | `GROUT_KG_PER_M2` |
| `PLASTER` | `THICKNESS_M`, `WASTE_PCT` |
| `PAINT` | `COVERAGE_M2_PER_L` |
| `PUTTY` | `KG_PER_M2` |
| `PLYWOOD` | `AREA_M2` |
| `FORMWORK` | `PROPS_PER_M2`, `RELEASE_AGENT_M2_PER_L`, `TIMBER_THICKNESS_M` |
| `ROOF_SHEET` | `COVERAGE_M2`, `FASTENERS_PER_M2` |
| `PURLIN` | `SPACING_M` |
| `REBAR_LAP` | `TENSION_LAP_FACTOR` |

And the accessor already exists — `UI/MaterialLookupCsv.cs:88`:

```csharp
public static double GetProperty(string name, string property)
```

backed by `MaterialLookupRow.Properties` (`UI/MaterialLookupParser.cs:258`), a `Dictionary<string,double>` holding **every** property in the row. The registry indexes each group under `"CATEGORY TypeKey"`, `"CATEGORY:TypeKey"`, bare `TypeKey` when globally unique, and bare `Category` for the `DEFAULT` row.

### The implementation, in full

In `ExpressionParser.ParsePrimary`, alongside the existing `if` and `log` handlers:

```csharp
if (ident.Equals("lookup", StringComparison.OrdinalIgnoreCase))
    return ParseLookup();
```

```csharp
// lookup(TABLE, KEY_PARAM, COLUMN) — TABLE and COLUMN are literals;
// KEY_PARAM is a parameter name whose VALUE is the row key.
private double ParseLookup()
{
    _pos++;                                   // past '('
    string table  = ReadBareToken();          // e.g. CONCRETE
    ExpectComma();
    string keyRef = ReadBareToken();          // e.g. CST_CONCRETE_GRADE_TXT
    ExpectComma();
    string column = ReadBareToken();          // e.g. CEMENT_BAGS_PER_M3
    SkipWhitespace();
    if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;

    // The key may be a parameter holding "C25", or a literal, or absent.
    string key = null;
    if (_ctx.TryGetValue(keyRef, out object kv)) key = kv as string ?? kv?.ToString();
    if (string.IsNullOrWhiteSpace(key)) key = "DEFAULT";

    // ⛔ WRONG — see the correction below. Kept only to show what NOT to do.
    double v = MaterialLookupCsv.GetProperty($"{table} {key}", column);
    if (v == 0) v = MaterialLookupCsv.GetProperty(table, column);   // DEFAULT row
    if (v == 0) { Fail($"lookup({table},{key},{column}) found no value"); return 0; }
    return v;
}
```

> **Correction (2026-08-08): the `v == 0` test above is wrong and must not be used.**
>
> `GetProperty` returns `0` for **both** "property absent" and "property present and
> legitimately zero". Treating `0` as "not found" therefore rejects real data.
> `MATERIAL_LOOKUP.csv` contains **eight true zeros**, and **six are reachable through the
> exact columns these 27 formulas read**:
>
> | Row | Column | Why 0 is correct |
> |---|---|---|
> | `CONCRETE C10`, `CONCRETE C7.5` | `STEEL_KG_PER_M3` | Unreinforced blinding |
> | `ROOF_SHEET CLAY_TILE`, `ROOF_SHEET CONCRETE_TILE` | `FASTENERS_PER_M2` | Nailed, not fixed |
> | `FORMWORK COLUMN`, `FORMWORK FOUNDATION` | `PROPS_PER_M2` | Self-standing, no props |
>
> Following the draft, a C10 blinding pour's steel formula would `Fail()` and — composed
> with the G-5 change — **skip a write whose correct answer is zero**. That inverts G-5 for
> those rows: it converts a correct zero into a missing quantity, which is the same class of
> silent wrongness G-5 exists to remove.
>
> **Use `MaterialLookupCsv.TryGetProperty` instead**, added for this purpose — it
> distinguishes absent from present-and-zero:
>
> ```csharp
> if (MaterialLookupCsv.TryGetProperty($"{table} {key}", column, out double v)) return v;
> if (MaterialLookupCsv.TryGetProperty(table, column, out v))                  return v;
> Fail($"lookup({table},{key},{column}) found no value");
> return 0;
> ```
>
> As landed in `a9eec757f`. Also note the empty-key handling: an empty *parameter value*
> must fall to `"DEFAULT"` rather than composing the key `"CONCRETE "`, which would miss
> silently.
>
> **Verified before building, not assumed:** 27 formulas / 29 calls, zero missing tables,
> zero missing columns, and a simulation of the exact resolution order resolves all 29 under
> both worst cases (key present-but-empty, key absent entirely).

`Fail()` already exists on the parser (added in the G-5 fix), so an unresolvable lookup now **skips the formula** rather than writing a zero — the two fixes compose.

### What this turns back on

27 formulas, and they are exactly the ones a BOQ needs:

- **`CST_S_CON_*`** — cement bags, sand, aggregate, water per m³ of concrete
- **`CST_S_MAS_BLOCKS_NR`**, `CST_CALC_BLOCKS_NR` — block counts
- **`CST_S_FRM_*`** — plywood sheets, props, release agent, timber
- **`CST_S_REI_*`** — rebar lap lengths
- **`BLE_FINISH_TILE_QUANTITY_NR`**, `BLE_FINISH_ADHESIVE_WEIGHT_KG`, `BLE_FINISH_GROUT_WEIGHT_KG`
- paint, primer and putty litres
- `PER_SUST_CARBON_FOOTPRINT_KG`

### The sustainable answer

**Implement `lookup()`.** Deleting the formulas would throw away a complete, curated dataset and the parametric material take-off it drives — and you would have to rebuild both later. The cost is roughly forty lines against data that is already loaded, already indexed and already correct.

Two follow-ons once it is live, both small:

1. **Fix the four formulas whose `Input_Parameters` column omits the key parameter** — `BLE_FLR_TILE_QTY_NR`, `CST_TOTAL_ROOFING_COST`, `CST_TOTAL_PLASTER_COST`, `PER_SUST_WTR_RATING_NR`. `BuildContext` only resolves names listed in that column, so the key would arrive empty and every lookup would fall to the `DEFAULT` row.
2. **Verify `CST_S_MAS_MORTAR_VOLUME_CU_M`'s unexplained `× 12`** — it was unverifiable while `MORTAR_RATIO` was unreachable. Once `lookup()` works you can read the real value and settle whether the constant is right.

## G-14 · The three BOQ-engine traps — fix specs

The three failure modes documented in the playbook, with the fix for each.

### Trap 1 — a failed quantity becomes a zero (gap A-1)

`BOQ/Takeoff/TakeoffRule.cs:224-232` returns `1.0` for count units and **`0.0` for m, m², m³, kg**. The row survives with a description, a rate and a section, and reads as a real cheap item.

**Fix, mirroring the formula-engine change that is already merged:**

```csharp
private static double? FallbackQuantity(string unit)   // was double
{
    switch ((unit ?? "").ToLowerInvariant())
    {
        case "each": case "item": case "nr": case "no": case "": return 1.0;
        default: return null;      // measured unit with no resolvable source
    }
}
```

Then in `BuildLineItem`, a null quantity sets `line.QuantityResolved = false` and `line.Note += " [QUANTITY NOT RESOLVED]"`. Add a gate in `BOQPrepForExport`: *"N measured lines have no resolvable quantity"* — hard-fail, alongside the existing eight thresholds. Tint those rows in the export so they are visible on paper too.

### Trap 2 — the row is named wrongly because a parameter is missing

Not a code defect — a data-completeness one — but it is invisible today. `ResolveDiscipline` falls back to `"X"`, `DeriveNrm2Section` falls back to the discipline default, and `GetPrimaryMaterialName` returns empty. All three produce a plausible row.

**Fix:** a pre-flight report, `BOQ_ReadinessByElement`, listing every element that will produce a row together with which of the six required fields it is missing — category, type name, material, classification, complete STING tag, resolvable rate. It is a read-only pass over the same collection `BuildBOQDocument` already makes, so it is cheap. Today the modeller finds out by reading 4,000 bill lines.

### Trap 3 — unfilled `[tokens]` fall back to a generic sentence

`BOQExportCommand.EnsureAllParagraphsResolved` (`:614-661`) re-resolves any paragraph still matching `\[[A-Za-z0-9_]+\]`, and failing that synthesises *"Supply, deliver and install {discipline} {category}…"*. The fallback is reasonable; the problem is it is **silent**, and `ParagraphCoveragePct < 80` only warns — and is **skipped entirely when driven from the panel** (`InlineHost=1`, `:59-74`).

**Fix:** count fallback paragraphs separately from resolved ones and report both. Never skip the coverage gate for the panel path — an inline host is a reason to render the warning differently, not to drop it. And list the top ten unresolved token names, because they point straight at the parameters worth populating.

## G-15 · 🔴 P0 · The formula take-off is not material-aware, and disagrees with the C# take-off by ~2.3× on blockwork

> **Status: open.** Found while fixing the mortar `× 12` (`6433778b3`); that commit corrected
> the arithmetic and **did not** address this. Recommendation below is a recommendation, not
> something done.

`CST_S_MAS_MORTAR_VOLUME_CU_M` queries the **`BRICK_BOND`** table unconditionally:

```
CST_S_MAS_NET_AREA_SQ_M * lookup(BRICK_BOND, BLE_BRICK_BOND_TYPE_TXT, MORTAR_RATIO)
```

There is no test of what the wall is made of. A **blockwork** wall carries no
`BLE_BRICK_BOND_TYPE_TXT`, so the key is empty, the lookup falls to `BRICK_BOND DEFAULT`
= **0.025 m³/m²** — a brick figure — and the wall is billed on it.

`BLOCK 400x200 MORTAR_VOLUME_FACTOR` is **0.011 m³/m²**, less than half.

Meanwhile `BOQ/Takeoff/CompoundTakeoffBuilder.cs:90-99` does exactly the right thing:

```csharp
bool isBrick = material.Contains("brick");
if (isBrick)  mortarRatio = Prop($"BRICK_BOND {bond}", "MORTAR_RATIO",         "BRICK_BOND DEFAULT");
else          mortarRatio = Prop($"BLOCK {size}",      "MORTAR_VOLUME_FACTOR", "BLOCK DEFAULT");
```

**So one physical quantity has two owners that disagree.** For 50 m² of 200 mm blockwork:

| Path | Mortar |
|---|---|
| `CompoundTakeoffBuilder` (C#, material-aware) | `50 × 0.011` = **0.55 m³** |
| `CST_S_MAS_MORTAR_VOLUME_CU_M` (formula) | `50 × 0.025` = **1.25 m³** |

**2.27× apart**, on a wall neither path flags, and the error propagates into
`CST_S_MAS_CEMENT_BAGS_NR` and `CST_S_MAS_SAND_VOLUME_CU_M`.

**This was latent until `a9eec757f`.** While `lookup()` was unimplemented the formula wrote
`0` and nobody noticed. Implementing `lookup()` turned it on. That is not an argument
against implementing `lookup()` — it is an argument for treating every one of the 27
revived formulas as unverified until checked against a hand take-off.

**Secondary fragility:** the C# path's test is `material.Contains("brick")` on the *material
name string*. A block wall whose material is named "Brick-faced blockwork" takes the brick
branch. Same class of defect, smaller blast radius.

**Recommended fix — delete the formula and let `CompoundTakeoffBuilder` own masonry
mortar.** Making the formula material-aware is possible (`if(material contains brick, …)`)
but it means maintaining the same decision in two languages against the same data, which is
how the two paths drifted in the first place. **One quantity should have one owner.** The
formula is the one to lose: it cannot read the material name without another parameter, and
the C# path already handles block size, brick bond, mortar mix and plaster together.

If the formula is kept instead, it needs a material test *and* a second lookup against
`BLOCK`/`MORTAR_VOLUME_FACTOR`, and both paths need a regression test asserting they agree.

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

---

# Part H — Export, reachability, tests and metrics

Findings from a second sweep over areas the first four audits did not cover.

## H-1 · 🔴 P0 · The IFC quantity writer reports success having written nothing

`BOQ/IfcQuantitySetWriter.cs:158-194` — all four stamp helpers share this shape:

```csharp
Parameter par = el.LookupParameter(p);
if (par == null || par.IsReadOnly) return;   // silent
```

`Qto_WallBaseQuantities.NetArea` and its siblings are **shared parameters that must be pre-bound**. Unbound, every write is a no-op. Meanwhile at `:127`:

```csharp
StingTools.UI.IfcMaterialPsetWriter.Stamp(el, item);
stamped++;                                    // unconditional
```

`stamped++` counts *"I visited this element"*, not *"I wrote something"* — and that count is surfaced verbatim: `BOQ/BOQExportIfcQtoCommand.cs:133` → `.Metric("Elements stamped", stamped.ToString())`.

**So the command can report "Elements stamped: 4,812", the user exports IFC believing Cost-X or CostOS will read the quantities, and the file carries zero `Qto_*` values.** Nothing checks that even one parameter resolved. This is the most consequential single finding in the sweep.

## H-2 · 🔴 P0 · Currency is hardcoded in the IFC and ERP exports, and can contradict the rate

`BOQ/IfcQuantitySetWriter.cs:115` — `StampString(el, "Pset_StingCost", "Currency", "UGX");`
`BOQ/BoqErpExporter.cs:79, 98` — `new XElement("Currency", "UGX")`

A configurable currency exists and is honoured elsewhere (`BOQModels.cs:355`, `BOQTenderConfig.cs:71`, `BOQSupportCommands.cs:850`). These three ignore it. And `BcisHttpRateProvider.cs:98,128` returns rates defaulting to **GBP** — so a BCIS-priced bill exports an IFC whose `UnitRate` is in GBP and whose `Currency` field says **UGX**. Silently wrong figures in a machine-read cost deliverable.

## H-3 · 🔴 P0 · Readiness gates report 100 % on an empty bill

`BOQ/BOQSupportCommands.cs:94` and `:231`:

```csharp
double pricedPct = total > 0 ? 100.0 * pricedCount / total : 100.0;
double epdPct    = total > 0 ? 100.0 * verifiedRows / total : 100.0;
```

The zero-denominator branch returns a **perfect score**, not "unknown". A BOQ that produced zero rows — which gap A-1 makes a live scenario — reports **"100 % priced"** and **"100 % EPD-verified"** to the QS.

Same at `Core/ComplianceScan.cs:79` (`SchemeCoveragePct`), where the XML comment documents it as intent.

The codebase is inconsistent about this: `ComplianceScan.cs:70,103,106,110` and `BOQModels.cs:423` all correctly return **0** on a zero denominator. The three sites that chose 100 are the three most decision-bearing.

## H-4 · 🔴 P0 · A swallowed sheet-name write, next to a reported sheet-number write

`Core/Mep/MepLevelViewProducer.cs:162-163`:

```csharp
try { sheet.SheetNumber = number; } catch (Exception ex) { warnings.Add($"…sheet number: {ex.Message}"); }
try { sheet.Name = Substitute(namPat, disc, levelCode, seq); } catch { }
```

A number collision is reported. A name failure on the very next line is discarded. **The sheet ships to the CDE named `Unnamed` and the run reports success.**

Nine further swallowed *writes* (not benign optional reads) — `Core/Mep/MepCircuitBuilder.cs:237-241` (four consecutive parameter writes, and it is the **sole writer of `MEP_SYS_NAME`**), `Core/Mep/MepCrossStampOrchestrator.cs:88,132`, `Core/Fabrication/ShopDrawingComposer.cs:504`, `Core/Drawing/SheetPlacementBridge.cs:437,452`, `Core/Drawing/ViewStylePackApplier.cs:129`, `Core/Drawing/AnnotationRunner.cs:1196`, `Core/FamilySymbolAuthor.cs:1741`.

Of 712 `catch {}` sites, no *transaction commit* is swallowed — the exposure is partial writes inside an outer transaction, which is why the failure mode is a half-written model rather than a lost one.

## H-5 · 🔴 P0 · ~232 deserialization sites, no schema, syntax-only CI

`grep JsonConvert.DeserializeObject StingTools/` → **234 sites**. `MissingMemberHandling` appears **twice**, and both set `Ignore`.

So the other ~232 use Newtonsoft's default: **unknown members are silently dropped, missing members leave POCO defaults**. A field-name typo in a data file is undetectable at load.

CI does not close it — `.github/workflows/stingtools-plugin.yml:34-45` is `json.load()`, a pure syntax check. Well-formed JSON with a misspelled key passes. **There is no schema anywhere in the repo.**

Highest-risk files for a BOQ/documentation workflow: `STING_NRM2_MEASUREMENT_RULES.json` (a typo'd deduction key silently disables a deduction), `cost_rates_5d.csv` / `STING_DEFAULT_COST_RATES.csv` (a renamed column silently yields rate 0), `BOQ_DESCRIPTIONS.json`, `COBIE_TYPE_MAP.csv`, and above all **`STING_DRAWING_TYPES.json`** — the entire P0 track in `DRAWINGS_PRODUCTION_REVIEW.md` was silent unbound-key defects in that one file, fixed one at a time. The *class* of defect is still wide open because nothing validates it.

## H-6 · 🟠 P1 · `StingTools.Connectivity.Tests` passes CI while running zero of its 25 assertions

It is **not empty** — 172 lines, ~25 real assertions over `PlanscapeServerClient`. But the `.csproj:16` is `<OutputType>Exe</OutputType>` with a hand-rolled `int Main` and **no `Microsoft.NET.Test.Sdk`, no xunit, no `IsTestProject`**.

`dotnet test` on it discovers nothing and exits **0**, and the CI loop treats that as `OK`. The `[Fact]`/`[Theory]` census counts it as 0, so it does not even appear in the coverage headline.

**This is the #553 failure mode recurring in a new shape.** The workflow's own header warns that a project which fails to build "reports nothing at all" — the same is true of one that builds but exposes no test adapter. Fix is one line: run it with `dotnet run`, or convert the 25 `Check()` calls to `[Fact]`s.

Related: **`StingTools.SitePhotos.Tests` runs in no workflow at all.** `stingtools-unit-tests.yml:66-69,109` skips it, deferring to `stingtools-plugin.yml` — which has **no `dotnet test` step**. The hand-off target does not exist.

And the CI exclusion filter for `#596`/`#597` is applied **globally across every project**, so any future test whose name contains those substrings is silently excluded too.

## H-7 · 🟠 P1 · Reachability — the two triage docs are materially stale

**Good news the docs do not record: there are zero dead buttons in `StingDockPanel.xaml`.** All 1,314 `Tag=` values resolve; the three that fail a naive handler-only scan are registered in `UI/Modules/*.cs`.

**`SILENT_BUTTONS_TODO.md` is stale** — all five Healthcare buttons it parks as "genuinely silent" are now wired (`StingCommandHandler.cs:4309-4333`), including the prefix dispatcher at `:4054`. Its "Wired: 0" count misreports the repo. `Circuit_AssignAuto` and `Validation_BS7671` are also **not** silent — the Electrical `default:` arm forwards to the main handler and both resolve in `WorkflowEngine.cs:1557-1558`.

**`docs/UNREACHABLE_COMMANDS_TRIAGE.md` is stale by ~390 commands and self-contradictory.** Its header says 1,288 `IExternalCommand` classes; the actual count is **1,678**. Its Counts table says Category C = 3; the Category C heading says "Genuinely dead (23)". Its Phase-177 correction claims `PluginOnboardingWizardCommand` was wired under tag `PlanscapeOnboarding` — that string appears **nowhere in the tree**; the class has zero references and is also a stub.

Still genuinely unreachable: `PluginOnboardingWizardCommand` (0 refs) and **four of the five AVF heatmap commands** (`VisualiseAcoustic/Carbon/Compliance/FillHeatmapCommand` — 1 ref each, the declaration only), which the doc claims were wired in Phase 177.

**One genuinely silent button:** `DocPackage` on the **HVAC panel, RPRT tab**. Its only occurrence in the tree is as a string argument at `UI/DocAutomationDialog.cs:411` — not a dispatch case. The HVAC `default:` arm falls through to the main handler, which refuses it, and the user gets **nothing** — no dialog, no toast, one log line.

## H-8 · 🟠 P1 · Stubs that register successfully and do nothing

- **`Clash/ClashDetectionCommands.cs:184-229`** — `LiveClashUpdater` returns `"(Phase 106 stub)"` from `GetUpdaterName()`, logs *"updater id reserved; triggers deferred"*, and `Register` appears to succeed. Live clash detection is advertised and inert. This is the same file behind CI-excluded test `#596`, which the workflow itself labels *"Product defect, not a test defect"* — so the defect is **masked by the exclusion**.
- **`Commands/FabricationExt/FabricationExtCommands.cs:194`** — a live button whose stated purpose is to be *"a placeholder so the family-library authoring work has a stable dispatch target."*
- **`ExLink/FohlioLink.cs:161-167`** — `List`/`Get`/`Update` all throw `NotImplementedException` with an honest message. Loud, so P1 not P0.
- **`Model/ExcelStructuralEngine.cs:1038,1052`** — slab and foundation import throw, **and lines 568-599 catch `NotImplementedException` and continue**. An Excel structural import therefore produces a model with no slabs and no foundations. *Whether the skip is reported to the user is unconfirmed* — if it is not, this is P0.

## H-9 · 🟠 P1 · The federated-compliance feature is dead code, and carries a latent defect

`Core/ComplianceScan.cs:923-1017` defines `LinkedModelCompliance`, `FederatedComplianceResult` (with `FederatedCompliancePct`, `FederatedRAG`) and `FederatedComplianceScanner.ScanFederated(Document)`. **`ScanFederated` has exactly one reference in the tree — its own definition.** No command, no panel, no workflow.

If it were wired, `:979` would ship with it:

```csharp
Document linkedDoc = linkInst.GetLinkDocument();
if (linkedDoc == null) continue;
```

An unloaded or missing link is skipped from **both numerator and denominator**, with no counter and no warning — so `TotalAcrossAll` describes a subset while `FederatedRAG` presents it as coverage "across all". Directly relevant to this project's eight-link federation.

## H-10 · 🟡 P2 · Roadmap entries that are already closed, and one that must be fixed in pairs

- **IM-6** claims `StingTools.Clash.Tests` does not build with 14 `CS0246` errors. It builds clean — 0 warnings, 0 errors. Fixed 2026-08-06; the roadmap was never updated.
- **IM-3** claims the BCC still calls `ConfigPathForModel` with `_data.FilePath` at six sites. `grep -c` in `UI/BIMCoordinationCenter.cs` returns **0**.
- **IM-12** is genuinely open (`WarningsController.cs:109`), but **`Planscape.Server/src/Planscape.Infrastructure/Services/BackgroundJobs.cs:400` carries the same predicate inverted** — `s.WarningCount > 0` in the purge filter means zero-warning snapshots are **never purged**. Fixing IM-12 to `>= 0` without also fixing line 400 leaves the retention job still skipping them, and `ComplianceSnapshots` grows forever. **Must be one PR.**
- `tools/StampDrawingTypeChecksums` is confirmed **not gated by CI** — `grep` across `.github/workflows/` returns nothing.

*Not confirmed:* IM-2's "139 hand-rolled `_BIM_COORD` paths". Raw grep returns 212, but `tools/check_path_discipline.ps1:169-200` ratchets per-file against a baseline with a discriminator regex, so the two numbers are not comparable without running the gate. **Do not treat 212 as a regression.**

## H-11 · 🟡 P2 · `ComplianceScan`'s concurrent path can return another document's result

`Core/ComplianceScan.cs:20-22` is a process-wide static cache with a 30 s lifetime and **no document key**, while `Scan(Document doc, …)` takes a document. In normal use this is safe only because `StingToolsApp.OnViewActivated` calls `InvalidateCache()` on a document change — the safety lives in a different file, not in the cache's own contract.

But at `:203-206`, when a scan is already in progress, the concurrent caller does `if (_cached != null) return _cached;` with **no document check and no time bound**, bypassing the lifetime check entirely. Narrow window; needs a concurrent scan to hit.

Also unguarded: `BOQ/BOQCostManager.cs:2966` divides by `boq.AllItems.Count` with no zero check, unlike every neighbouring factor in the same scoring block. On an empty BOQ that is `0.0/0` → NaN. The NaN is contained by the downstream comparisons, so this is fragility rather than a visible wrong number.

---

# Part K — Room finishes, BOQ exclusion, sheet numbering, marks

## K-1 · 🔴 P0 · The STING room-finish parameters were never bound, so nothing was written at all

| Family | Written by | Read by |
|---|---|---|
| Revit built-ins `ROOM_FINISH_FLOOR/WALL/CEILING/BASE` | **only** `FohlioImportFinishesCommand` (`ExLink/FohlioFinishesCommands.cs:26-32`) | `ISBRoomFinishCommand`'s schedule (`ExLink/ISBAppsCommands.cs:218-236`) — fields literally `"Floor Finish"`, `"Wall Finish"`, … |
| STING `BLE_ROOM_FINISH_*_TXT` (`MR_PARAMETERS.txt:1593-1596`) | `RoomFinishScheduler.WriteToRooms` (`PlasteringEngine.cs:996-1014`) | the covering engine, BOQ description tokens |

> **Status: closed** — `daf87d34b` (write both families, count honestly) + `9f02aa3bf` (the
> bindings). Compile-verified only.
>
> **Correction (2026-08-08).** This entry originally read "two parameter families that never
> meet", which is **wrong, and understates it**. "Never meet" implies both halves were
> written and simply not joined up. They were not.

**`BLE_ROOM_FINISH_*_TXT` had ZERO rows in `CATEGORY_BINDINGS.csv`.** Verified against
`ff054c77a`: `grep -c "BLE_ROOM_FINISH_" StingTools/Data/CATEGORY_BINDINGS.csv` → **0**.

So on a stock-bound project the parameters did not exist on Rooms at all. `WriteToRooms`
attempted the write, `Parameter` came back null or the set threw, and the failure was
swallowed by `catch (Exception ex) { StingLog.Warn($"Param not bound: {ex.Message}"); }`
(`PlasteringEngine.cs:1360`) — **every run, on every room, in silence apart from a log line
nobody reads.** The STING half of the feature had never worked on any project that had not
hand-bound the parameters.

The built-in half worked, but only from `FohlioImportFinishesCommand`. So:

- **Run "Room Finishes" → nothing is written anywhere.** Not "written to the wrong family".
- **Build the ISB room finish schedule → empty**, because the built-ins were never written
  either unless a Fohlio import had run.

The only bridge is one-way and irrelevant here: `NativeParamMapper` copies **built-in →
STING** with `SetIfEmpty` during tagging (`ParameterHelpers.cs:2907-2914`) — and it too was
writing into unbound parameters.

**Fix (as landed):** two parts, and both were necessary —
1. **The bindings** (`9f02aa3bf`): 10 rows added to `CATEGORY_BINDINGS.csv` — the four
   `BLE_ROOM_FINISH_*_TXT`, the four new `*_COD_TXT` code parameters (Rooms, Instance), and
   two Floors-instance parameters for K-3.
2. **The built-in write** (`daf87d34b`): `WriteToRooms` now writes **both** families, so a
   native Revit schedule and an IFC export can see the values. Writing both is the right
   answer rather than re-pointing the ISB schedule — the built-ins are what everything
   outside STING can read.

**Lesson for the register generally:** a `catch` that logs and continues turns a
missing-binding defect into an invisible one. This is the same failure class as G-5 and
A-1, in a different subsystem.

## K-2 · 🟠 P1 · No finish code parameter, and no finish code legend

Every finish parameter in `MR_PARAMETERS.txt` is `TEXT` used as free-text prose. There is **no** `BLE_ROOM_FINISH_FLOOR_COD_TXT` or equivalent, no picklist, and no validation. `BLE_FINISH_TYPE_TXT` is the closest slot and is unvalidated.

And there is no legend to code against:
- `ROOM_TYPE_CLASSIFIER.csv` is a **lighting** classifier (`room_name_pattern, en12464_room_code, target_lux`).
- `CODE_LEGEND.json`'s single `FIN` entry is a **4D trade code** ("Finishes — wall / floor / ceiling finishes"), sibling to `EXC`, `MOB`, `JOI`.
- The de-facto list is `BLE_MATERIALS.csv`'s `MAT_CODE` (`PT-001`, `CLG-001`) — but **nothing links `MAT_CODE` to `BLE_ROOM_FINISH_*_TXT`**.

So finishes are described in sentences that cannot be filtered, sorted, validated or joined to a material. `RoomFinishScheduler`'s own defaults are hardcoded English: `"2 coat gypsum plaster + vinyl matt emulsion"`, `"Power-floated concrete + carpet/vinyl"`.

**Fix:** add a `*_COD_TXT` companion to each of the four room-finish params, and ship `Data/STING_FINISH_CODES.csv` joining code → description → `MAT_CODE`.

## K-3 · 🟠 P1 · Nothing turns a room finish into a finish element

No command reads `BLE_ROOM_FINISH_FLOOR_TXT` and creates the floor-finish element bounded by that room. `SmartCoveringFactory.ApplyCovering` injects finish layers into **wall compound types** and writes parameters on beams and columns; it operates on selected elements, **not from room data**, and it never creates a Floor.

For a project where floor finishes are the product being sold, per-room finish floors are entirely manual. A `Finish_CreateFloorsFromRooms` command — sketch a floor per room boundary, type resolved from the room's finish code, offset 0 from level, room-bounding off — is a contained, high-value addition.

## K-4 · 🔴 P0 · No per-element BOQ exclusion exists at all

Confirmed absent: `CST_EXCLUDE_BOOL`, `ASS_BOQ_EXCLUDE`, `BOQ_SKIP` — none exist in code, `MR_PARAMETERS.txt`, or `PARAMETER_REGISTRY.json`. No ExtensibleStorage BOQ suppression, despite `StingValidatorSuppressionSchema` proving the pattern works for validator findings. No `Exclude` field on `BOQModelOverride` (which carries only `RateUGX/RateUSD/NRM2Paragraph/Note/RateSource`, `BOQCostManager.cs:2678-2685`). And `BOQCostManagerPanel.cs:4888-4890` hard-returns on any `BOQRowSource.Model` row:

```csharp
private void DeleteRow(BOQItemViewModel vm)
{
    if (vm.Underlying.Source == BOQRowSource.Model) return;
```

`CST_PROVISIONAL_SUM` **reclassifies**, it does not exclude.

**Fix:** one boolean on `BOQModelOverride` plus a reason string. That sidecar already survives refresh, document re-open and Revit restart, and is already re-applied on every rebuild by `ApplyModelOverrides`. Optionally back it with a `CST_BOQ_EXCLUDE_BOOL` parameter for modellers who prefer the Properties palette.

> **Status: closed** — `2351a47a2`. Compile-verified only.
>
> The flag was the easy half. **The audit row is the feature**: an excluded element lands on
> `BOQDocument.UserExclusions` and prints at the top of the Audit Trail sheet with its
> reason, who and when. A quantity that vanishes from a bill with no trace is
> indistinguishable from a takeoff bug. An exclusion saved without a reason still appears,
> labelled `(no reason given)` and highlighted, with the count in the banner;
> `SetModelExclusion` refuses to record one in the first place.
>
> Two things decided whether it worked at all, neither visible from the entry:
> - **Links filter at item level, not element level.** The link takeoff is cached by link
>   path and that cache is **not** invalidated when an override is saved, so an
>   element-level filter is skipped entirely on a cache hit and the exclusion silently fails
>   to apply. Filtering `rawItems` covers both paths, and must run before
>   `AggregateLineItems` collapses rows and clears `UniqueId`.
> - **The upsert merge is tri-state.** `Excluded` is non-nullable and every existing caller
>   defaults it to `false`, so copying it unconditionally would clear an exclusion every time
>   someone edited a rate on the same row.
>
> **Left open:** no UI affordance. The flag is settable through `SetModelExclusion` or the
> sidecar; the panel checkbox and the optional `CST_BOQ_EXCLUDE_BOOL` parameter are not built.
> `DeleteRow`'s hard return on `BOQRowSource.Model` (`BOQCostManagerPanel.cs`) is untouched.

## K-5 · 🟠 P1 · `PHASE_CREATED` is not filtered — future-phase elements are billed

`IsPhaseDemolished` (`BOQCostManager.cs:3382-3395`) checks `BuiltInParameter.PHASE_DEMOLISHED` only. An element created in a *later* phase is billed against the current bill. On any phased project that is a straightforward over-measure.

## K-6 · 🟡 P2 · Two unrelated category-exclusion lists

`TagConfig.CategorySkipList` (config key `CATEGORY_SKIP`) is enforced at exactly one place — `ParameterHelpers.cs:4098`, the tagging pipeline. `BOQCostManager` contains **zero** references to it; the bill uses its own `COST_TAKEOFF_EXCLUDE_CATEGORIES` (`:3176-3188`).

So excluding a category from tagging does nothing to the bill, and vice versa, and neither list mentions the other. A modeller will reasonably assume one governs both.

## K-7 · 🟠 P1 · `IsoNaming` has no Level field, so `{lvl}` can render empty and silent

`DrawingType.IsoNaming` (`:427-440`) carries **Volume, Type, Role, Suitability, Revision** — and no Level, Project, Originator or Number.

`vol`, `type` and `role` all fall back to the profile in `DrawingTokenContext.Build`. **`{lvl}` does not** — `:57` is `{ "lvl", levelCode ?? string.Empty }`. If the producing command passes no level, the sheet number comes out `KBL26-PLN-COT01--DR-A-1001` with an empty segment and no warning.

Same shape for `{seq}`: absent entirely when the caller has no value (`:75`), leaving the literal `{seq:D4}` in the sheet number.

> **Status: closed** — `84a7919af`. Compile-verified only.
>
> **Correction (2026-08-08): the fallback is needed at BOTH ends, and this entry implied
> one.** Adding `Level` to `IsoNaming` and falling back to it in `DrawingTokenContext.Build`
> fixes the **title-block cells only**. The **sheet number** does not read the token
> dictionary for `lvl` at all: `DrawingProducer.SubstituteTokens` passes it as a separate
> caller-resolved argument into `ApplyTokenPattern`, and `{lvl}` is consumed there —
> `p.Replace("{lvl}", SafeShort(lvl))` — *before* the extras sweep that handles the rest of
> the ISO tokens.
>
> Patching only `DrawingTokenContext` would therefore have produced a drawing whose
> title block reads `ZZ` and whose sheet number still reads `…-COT01--DR-…`: **two surfaces
> disagreeing about one drawing**, which is worse than the original blank because it looks
> fixed. The landed fix applies the same fallback in both places —
> `lvl: ctx?.Level?.Name ?? dt?.IsoNaming?.Level ?? ""` at `DrawingProducer.cs`, with a
> comment recording why.
>
> **This correction was filed against "C-5" in the follow-up brief. C-5 is the
> `TryParseName` refactor** (closed by `15bc74155`) and is unrelated; the `{lvl}` claim
> lives here in K-7. Noted so the two are not conflated later.
>
> **Trap this creates.** `IsoNaming.Level` carries `NullValueHandling.Ignore` and is null in
> all 93 shipped corporate types, so drawing-type checksums are unchanged. **The first
> corporate type that sets `"level"` in `STING_DRAWING_TYPES.json` changes that type's
> hash** and must be re-stamped with `tools/StampDrawingTypeChecksums`, or the registry
> demotes it to `project` origin. Since K-7 exists precisely to make people populate that
> field, expect to hit this.

## K-8 · 🟡 P2 · The shipped ISO pattern suggestion contradicts the shipped data

`Iso19650Vocabulary.cs:350` offers:

```csharp
"{project}-{originator}-{vol}-{lvl}-DR-{role}-{seq:D4}",  // Full BS 1192 / ISO 19650-2
```

It **hardcodes `DR`** where the actual data file uses `{type}` (`STING_DRAWING_TYPES.json:76`) and where the doc comment on `DrawingType.cs:290` also specifies `{type}`. Three sources, two answers. Pick `{type}`.

Note the same file already carries a scar from this class of bug — the comment at `:344-349` records that an earlier shipped suggestion used `{prj}`/`{orig}`, which are not tokens the context emits, so choosing it rendered literal braces on the sheet.

## K-9 · 🟠 P1 · `BatchTagCommand`'s token-lock check is dead code

`Tags/BatchTagCommand.cs:777-782`:

```csharp
string lockStr = ParameterHelpers.GetString(el, ParamRegistry.Ext("TOKEN_LOCK"));
bool isLocked = !string.IsNullOrEmpty(lockStr) && …;
if (!isLocked) ParameterHelpers.SetString(el, paramName, current, overwrite: true);
```

**`_extendedParams["TOKEN_LOCK"]` is never registered.** `ParamRegistry.Ext` (`:698-705`) logs a warning and returns `""`; `GetString(el, "")` returns empty; `isLocked` is **always false**; the token is **always overwritten**.

So a user who locks `LVL,SYS,PROD` and runs **`BatchTag`** loses the lock silently, while the same lock honoured through **`TagAndCombine`** works. Two commands the user reasonably expects to be equivalent, one of which quietly ignores their instruction.

That path also covers only six tokens (`LVL, LOC, ZONE, SYS, FUNC, PROD`), missing `DISC`, `STATUS`, `REV`.

**Also:** there is **no UI anywhere** to set `ASS_TOKEN_LOCK_TXT` — zero hits across `UI/` and `Docs/`, `.cs` and `.xaml`. The feature exists, works through one path, and is undiscoverable.

## K-10 · 🟠 P1 · Mark is mapped to two different STING parameters

`ParameterHelpers.cs:2667` and `:2674` both read `BuiltInParameter.ALL_MODEL_MARK`:

```csharp
written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MARK, ParamRegistry.ID);          // ASS_ID_TXT
written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MARK, "ASS_SERIAL_NR_TXT");
```

A Mark of `D-101` becomes both the asset ID and the equipment **serial number**. For COBie handover, Serial Number should be the manufacturer's serial — this is a semantic error that reaches the client's asset register.

## K-11 · 🟡 P2 · Mark dedup silently drifts from the asset ID

`WarningsManager.cs:1055-1062` resolves duplicate-mark warnings by appending `_2`, `_3` to the Mark. But `MapAll` writes `ASS_ID_TXT` with `SetIfEmpty`, so the STING ID keeps the **pre-dedup** value. The two diverge with no warning, and `ASS_ID_TXT` is the one the bill and the handover use.

## K-12 · 🟡 P2 · `WriteToRooms` reports rooms it did not change

`PlasteringEngine.cs:996-1014` increments `written++` per room regardless of whether `SetIfEmpty` changed anything. On an already-populated model it reports every room as updated. The number is not evidence.

---

# Part J — Proposed tool: material price book

**The question this answers:** *can we not change the material library rates, and put in place a flexible way of editing and updating prices — for every future project, not just this one?*

Yes. But not by editing 1,279 CSV rows, and here is why.

## J-1 · Why the CSV is the wrong place for a price

- **The price is entangled with everything else.** A `BLE_MATERIALS.csv` row is 72 columns defining geometry, appearance, layers, thermal, structural and carbon properties. Changing a price means editing a row that also decides what the material looks like in a rendering. Wrong granularity for a figure that changes quarterly.
- **There is no unit column.** `MAT_COST_UNIT_USD` is a number with no denominator. Per-m², per-m³, per-block and per-item prices sit in the same column, indistinguishable (E-13).
- **The UGX column is derived.** It is USD × 3700 on every row. Two prices that can disagree, in one row, with nothing saying which wins.
- **No provenance.** No source, no quotation date, no validity. A QS cannot answer *"how old is this rate?"* — which is the first question they ask.

## J-2 · What already exists, and why none of it is sufficient

| Route | Grain | Verdict |
|---|---|---|
| Material library CSV | per material | wrong granularity, no unit, no provenance |
| `rate_card.json` (priority 87) | **Revit category** | cannot distinguish 200 mm block from a glazed screen. Good backstop, useless as a price book |
| `cost_rates_5d.csv` (90) | category | same limitation — and it is the silent fallback that absorbs every material-rate miss (E-6) |
| `CST_UNIT_RATE_UGX` override (100) | per element | correct for a genuine one-off; does not scale, does not carry to the next project |
| BCIS / Planscape feeds | live API | already built, lazily registered from `_BIM_COORD/rate_feeds.json`. Right answer for UK BCIS; **no equivalent published feed exists for Uganda** |
| `BOQQsExport` / `BOQQsImport` | bill rows | an Excel round-trip already exists — the natural editing surface for a QS |

**There is no material-level price book, and no price-update command.** That is the gap.

## J-3 · The proposal

A `STING_MATERIAL_PRICE_BOOK.json` on the corporate-baseline + project-override pattern the codebase already uses three times over, read by a new `MaterialPriceBookProvider` at **priority 93** — above the category CSV at 90, below the per-element override at 100.

Authored in full at [`GUIDES/kibale-project-config/material_price_book.json`](kibale-project-config/material_price_book.json). Shape:

```json
{
  "baseCurrency": "USD",
  "fx": { "UGX": 3700, "KES": 129, "TZS": 2600 },
  "prices": [
    { "materialName": "HOLLOW CONCRETE BLOCK 8IN (200MM)",
      "rate": 0.60, "currency": "USD", "unitOfMeasure": "each",
      "labourFraction": 0.35, "source": "Kampala supplier quotation",
      "quotedOn": "2026-07-15", "validUntil": "2026-12-31", "region": "UG-CENTRAL" }
  ]
}
```

**Why `materialName` is a safe key:** measured — `MAT_NAME` is unique across all 1,279 rows, case-insensitively, with zero cross-file collisions.

**What each field buys you:**

- `unitOfMeasure` — **required.** A rate without a unit is not a rate.
- `fx` in one place — change the shilling rate once and everything follows. Nobody edits a price to chase an exchange rate, which is what the current derived UGX column forces.
- `quotedOn` / `validUntil` — makes a rate **auditable**. The rate audit can then answer *"N materials priced from quotations older than six months"*, which nothing in the tool can do today.
- `region` — one corporate book serving Kampala, Fort Portal, Nairobi and Dar without forking.
- Plain JSON — it **diffs in git**, so a price change is reviewable and attributable like any other change.

## J-4 · The two code changes it needs

1. **`MaterialPriceBookProvider : IRateProvider`** — clone `ProjectRateCardProvider` (it is ~110 lines), key on material name instead of category, register at 93 in `RateProviderRegistry`.
2. **Stop overloading `ALL_MODEL_COST`.** Stamp `STING_MAT_RATE_NR` + `STING_MAT_RATE_CCY_TXT` + `STING_MAT_RATE_UOM_TXT` at material creation and read those instead. `ALL_MODEL_COST` then belongs to the human editing Revit's material browser, and the provider can finally tell a library price from a hand-typed one — **which is the exact ambiguity that produced the 3,700× defect** (E-1b).

Point `BOQQsExport` / `BOQQsImport` at the book and the loop closes: export to Excel, price it, import, `Cost_ReloadRules`.

---

# Part I — Fix order

Eight things, ordered by damage-per-hour-of-work.

| # | Fix | Gap | Why first |
|---|---|---|---|
| 1 | Return `null` instead of `0` from the formula engine's failure paths | G-5 | One change converts an entire invisible failure class into a visible one |
| 2 | `CurrencyCode = "USD"` in `MaterialLibraryRateProvider` | E-1, E-1b | One word; un-breaks every material rate in the product |
| 3 | Count actual writes in `IfcQuantitySetWriter`, not visits | H-1 | Stops the tool certifying a deliverable it did not produce |
| 4 | Zero-denominator → 0 or "n/a", never 100 | H-3 | Three call sites; stops a false green light reaching the QS |
| 5 | Fix `ParseCsvLine` to un-escape `""` | G-2 | Correct parser already exists in the test project |
| 6 | Warn when an included link is placed *n* > 1 and not multiplied | A-3 | Cheap gate; prevents a six-cottage under-count |
| 7 | Implement `lookup()` — or delete the 27 formulas that call it | G-1 | Either is better than writing 0 into a bill |
| 8 | Normalise the nine invalid identity classes; apply the normaliser on the description path | E-11 | 29 % of the library currently bills as "generic" |

Then the structural work: the `SpatialCodeRegistry` (F-9), schema validation for `Data/*.json` (H-5), and the Scope Box Manager (Part C).
