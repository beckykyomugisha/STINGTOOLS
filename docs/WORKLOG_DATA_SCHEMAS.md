# Worklog — data-schema drift (DSCH)

Branch `claude/data-schema-drift-validators-a30f10`, from `origin/main` @ `35e9ec7f9`
(2026-10-01). Unattended run; entries are appended, never rewritten.

## Coordination — files owned by open PRs (2026-10-01)

| PR | Files |
|---|---|
| #1033 Tagging worklog: pass 2 | `docs/WORKLOG_TAGGING.md` |
| #1032 TAGACC-21 proximity | `StingTools.Tags.Tests/ProximityRuleTests.cs`, `StingTools.Tags.Tests/StingTools.Tags.Tests.csproj`, `StingTools/Core/ParameterHelpers.cs`, `StingTools/Core/ProximityRule.cs`, `docs/CHANGELOG.md`, `docs/ROADMAP.md` |
| #1017 Licence trial | `StingTools.LicenseIssuer/Program.cs`, `StingTools.Licensing.Tests/*`, `StingTools/Core/Licensing/*`, `StingTools/Core/StingToolsApp.cs`, `StingTools/UI/ActivationDialog.cs`, `deploy/*`, `docs/CHANGELOG.md`, `package.bat`, `tools/tester-kit/Install-STING.ps1`, `tools/tester-kit/licence-issuer/README.md` |
| #1016 Tag-library smoke tests | `docs/CHANGELOG.md`, `docs/ROADMAP.md`, `docs/TAG_TEST_PROTOCOL.md`, `tools/tester-kit/TESTER_GUIDE.*` |

None touches `cost_rates_5d.csv`, `tools/validate_data_schemas.py` or `.github/workflows/*`,
so this branch is based on `main`. `CHANGELOG.md` / `ROADMAP.md` are shared log files:
entries are appended only.

## Decisions

- **PROD in `cost_rates_5d.csv` — keep it, add it to the schema.** Options: (a) remove the
  column; (b) declare it. PROD is real data read by code: `LoadCsvRates` registers
  `DISC|PROD` keys that `CsvRateLookup` Pass 0 (confidence 97) resolves first (D6, #844).
  Removing it would delete the most specific rate tier. Chosen: (b), schema v2.
- **Where schemas live — one registry, `tools/data_schemas.json`.** Options: (a) keep
  dicts inside the Python validator; (b) a JSON registry read by the validator *and* by
  C# tests; (c) JSON-Schema files per data file. (a) cannot be read by the C# tests that
  hold readers to the schema; (c) would duplicate the POCO-derived key sets the
  validator already derives from C#. Chosen (b): one file, versioned
  (`registryVersion`, per-file `schemaVersion`), docs derived on demand (`--describe`)
  rather than a second copy in markdown.
- **Coverage — register every file, with an honest level.** Writing a full schema for
  ~400 files in one pass is not credible, and a glob that waves new files through
  would defeat "a new file without a schema fails". Chosen: every file is listed
  explicitly under `schemas` (full), `structural` (format checks + the reason there is
  no deeper schema) or matched by a `nonData` glob (binaries, docs). An unlisted file
  fails with the `--scaffold` command to run.
- **CSV headers pinned from the shipped files.** 56 single-table CSVs got their current
  header as their schema. That is a snapshot, not a reviewed contract (the registry
  description says so); its value is that any future insert/rename/reorder fails until
  the schema is changed with it. Hand-reviewed contracts: `cost_rates_5d.csv`,
  `STING_DEFAULT_COST_RATES.csv`.
- **Duplicate foundation row in `cost_rates_5d.csv`.** Options: delete the alias row;
  re-key it. The plugin writes PROD `FDN` (TagConfig) *and* `FND`
  (StructuralAdvancedDesignExt), and the alias row exists to register MAT_CODE `FDN`.
  Chosen: alias row carries PROD `FND`, so both spellings price at the product tier and
  the `S|FDN` key is no longer duplicated (it logged a warning on every load).
- **Cost File Browser override is never read.** Options: wire `cost_rates_override.json`
  into `LoadCsvRates` (which has no `Document` and ~8 document-less callers — a design
  change); or stop claiming it works. Chosen: the message now says plainly that pricing
  does not read it yet; wiring is ROADMAP DSCH-1.
- **Mixed line endings** fixed to each file's majority ending (smallest diff).
- **CI paths.** `stingtools-plugin.yml` and the `ci-gate.yml` plugin filter now include
  `tools/**`, `project-templates/**` and `GUIDES/kibale-project-config/**`, so a PR that
  only touches registered data outside `StingTools/` is still gated.

## Round 1 — fix and gate

Before: `python tools/validate_data_schemas.py` → exit 1 (`cost_rates_5d.csv: header
mismatch`), and no workflow ran it.

| # | Finding | Fix |
|---|---|---|
| R1-1 | Schema lacked `PROD` (inserted by D6, #844) | Schema v2 declares it (type, description, `unique` DISC+PROD) |
| R1-2 | **5D Cost Trace** (`Scheduling4DEngine.LoadCostRates`) read `cols[3]` as USD — after PROD that is `MAT_DISCIPLINE`; every row failed to parse, command reported "No cost rates found" | All readers go through `BOQ/Rates/CostRateCsv` (by header name). Trace is now first-row-wins like the BOQ |
| R1-3 | `LoadCsvRates` handled the shift with a second positional branch | Same parser; behaviour identical (keys DISC\|PROD, Category, MAT_CODE; UGX; first wins) |
| R1-4 | Cost File Browser demanded `MAT_CODE, RATE, UNIT`; the shipped card has no `RATE`, so it rejected its own format | Uses `CostRateCsvLayout.MissingRequired()` |
| R1-5 | Cost File Browser said "All 5D commands will use this file"; nothing reads the override | Message corrected; ROADMAP DSCH-1 |
| R1-6 | Duplicate `S\|FDN` key in `cost_rates_5d.csv` | Alias row re-keyed to PROD `FND` |
| R1-7 | Validator in no workflow | `--self-test` + validation steps in `stingtools-plugin.yml` *Validate data files* |
| R1-8 | Validator knew 5 files | Registry covers all 631 files under 4 roots |
| R1-9 | `WORKFLOW_PlumbingDesign.json`: 20 steps carried `label` twice (#630 renamed `description`→`label`); the long description won and the short step name was discarded | Second key renamed `_notes` (an allowed doc key) |
| R1-10 | `COBIE_TYPE_MAP.csv` (153 CRLF / 11 LF), `TAG_GUIDE_V3.csv` (10 / 449) mixed line endings | Normalised |

Tests: `StingTools.Boq.Tests/CostRateCsvTests` (9) — including the inserted-column
replay that a positional reader fails, and the parser-vs-registry column check.
After: validator OK, self-test 18/18, build 0/0.

## NEEDS REVIT CHECK

1. **5D Cost Trace prices again.** Open any model with walls → STING panel → BIM →
   5D Cost Trace. Expected: rates listed (Walls 85 USD/m²), not "No cost rates found".
2. **Cost File Browser accepts the shipped format.** BIM → Cost File Browser → Browse →
   pick `data/cost_rates_5d.csv` from the deployed plugin folder. Expected: accepted, and
   the confirmation says pricing does not read the override yet.
3. **Plumbing design workflow labels.** Run *Workflow presets → Full Plumbing Design
   Pipeline*. Expected: step names are the short labels ("Scan Fixture Units"), not the
   long descriptions.

## Round 2 — all data, from scratch

Method: surveyed every CSV (header, field counts, encoding, line endings), ran the
validator over the whole tree, ran every `tools/` check script not in a workflow,
and compared generated / baseline files with their sources. A subagent mapped each
ragged CSV to its readers and the consequence.

| # | Finding | Fix |
|---|---|---|
| R2-1 | `FAMILY_PARAMETER_BINDINGS.csv`: two descriptions with an unquoted comma shifted every later column; **`MAT_COST_SUPPLY_NR` and `STING_EMB_CARBON_NR` were never bound to a family** (Batch Add Family Params found no category) | `,` → `;` (two Python gates split naively, so quoting alone would not do) |
| R2-2 | `STING_MATERIAL_CLASS_NORMALISER.csv`: two regexes containing `[-,]` split in two → invalid regex, dropped; "Concrete C30" / "Steel, sections" never normalised | Quoted |
| R2-3 | `FORMULAS_WITH_DEPENDENCIES.csv` `CST_CALC_BLOCKS_NR`: unquoted comma shifted Input_Parameters into Unit → **never evaluated**; formula also named `CST_S_MAS_WASTAGE_FCT_PCT_NUM`, which does not exist | Quoted; `_PCT` (its own Input_Parameters and MR_PARAMETERS) |
| R2-4 | Unquoted commas / stray trailing commas: LUX_TARGETS (1), MATERIAL_LOOKUP (5), STING_COMMODITY_RATES (1, description truncated), COBIE_ATTRIBUTE_TEMPLATES (11) | Quoted / trimmed; quote-aware readers, no value moves except the commodity description |
| R2-5 | `STING_CSI_MASTERFORMAT_MAP.csv`: 19 rows carry Material (col 9) / Phase (col 10) the header never named | Header extended; schema v2 with `minFields` 6 |
| R2-6 | `STING_TAG_CONFIG_v5_0_HEALTH*.csv`, `STRUCTURAL_EXCEL_TEMPLATE.csv` are multi-section / headerless; the latter is read by nothing | `csv-sections`; dead file → ROADMAP DSCH-5 |
| R2-7 | `SyncParameterSchemaCommand` read FORMULAS `cols[0]` (Discipline) as the target and every other column as a dependency — the report could never be clean | By header name |
| R2-8 | Formulas name parameters that do not exist: `RGL_KCCA/NEMA/UMEME_APPROVAL_TXT` in three TAG7 Input_Parameters | Not fixed (TAG7 behaviour); declared in `alsoAllowed` with reason → ROADMAP DSCH-4 |
| R2-9 | 32 TAG7 paragraph formula rows carry 4 of 12 fields: FormulaEngine drops them (G-6), family-formula authoring uses them | Declared `minFields` 4 with reason → ROADMAP DSCH-3 |
| R2-10 | Validator bug: a doc comment "…class this fixture…" read as a class and hid PlacementRule's later properties → false UNKNOWN KEY on 81 rules | Comments and strings blanked before scanning; self-test guards it |
| R2-11 | `Data/Schemas/*.schema.json` (3 hand-written JSON Schemas) are read by nothing and have drifted — 303 / 2 / ~600 errors against their own files | Not deleted (the placement guide points at one); POCO-derived checks replace them in the gate → ROADMAP DSCH-8 |
| R2-12 | 11 check scripts under `tools/` in no workflow | Gated (see Gates). Not gated, with reason: `validate_dual_owner.py` (fails on main: (c) 6 > 5 → DSCH-6), `check_qs_nrm2_review.py` (3 checks fail on main → DSCH-7), `validate_declared_but_uncalled.py` (8 min 17 s, and fails: 16 new → DSCH-12). Not checks: `run_ci_gates.py` (local CI mirror), the `fix_*` / `gen_*` / `build_*` / `mint_*` / `expand_*` / `polish_*` / `transform_*` / `dedupe_*` generators (write files on purpose; their outputs are gated where a drift check exists) |
| R2-13 | `binding_simulator_baseline.txt` stale: 3604/3444/160 recorded vs 3668/3509/159 actual | Baseline refreshed; `--check` ratchet added and gated |
| R2-14 | The registry itself was not registered (only git-tracked after commit) — CI would have failed | Registered; coverage check proven |

Gates added this round: `minFields`, `refersTo`, comment-safe POCO scan; CI steps for
check_binding_scope, check_shared_param_types, check_tag_vocabulary,
check_title_block_surfaces, check_unattended_cycle, validate_param_readership
(+ self-test), register_count, binding_simulator, restamp_content_manifest --check,
check_command_app_acquisition, check_dispatch_parity. `run_ci_gates.py --quick`:
50 passed, 0 failed. Boq.Tests 1,373 / Tags.Tests 5,104 passing. Build 0/0.

## Round 3 — deeper: locale, POCO-derived keys for ~110 JSON files, duplicate names

| # | Finding | Fix |
|---|---|---|
| R3-1 | **`MepSymbolEngine`** read the MEP symbol index by position in the ISO index's layout: every MEP entry's ViewTypes came from the colour-scheme column ("Corporate") and matched no view; colour scheme from the paper size; paper size always 6 mm. `AppliesToViewType` also split on `,` while data and the ISO default use `\|` | By header name; split on `,` and `\|`; invariant culture |
| R3-2 | **`ARCHICAD_IFC_MAPPING.json`**: all 191 property mappings use `pset_name` / `property_name`; the class bound only `archicad_pset` / `archicad_prop` → no ArchiCAD property mapping ever wrote a value | Private `[JsonProperty]` aliases for both spellings |
| R3-3 | Culture-sensitive parses of shipped numbers (`MepSymbolEngine` paper size, BOQ_TEMPLATE rates) | Invariant culture. Other culture-default parses read user input or plugin-written text; listed in ROADMAP DSCH-9 |
| R3-4 | Two files named `STING_PUMP_CATALOGUE.json`; FindDataFile returns the root (empty by MEPG-2) so the 20-pump Plumbing copy was never read | Renamed `*.indicative-example.json` with a `_status` line; gate now fails on duplicate names under Data |
| R3-5 | `projectTypePresets` (STING_SPATIAL_CODES, populated in the Kibale overlay) is designed (F9) but has no reader | Declared docKey with reason → ROADMAP DSCH-11 |
| R3-6 | Validator: POCO scan missed `[JsonProperty]` names, private alias setters, fields, inherited members, partial classes; nested `Dictionary<string,T>` nodes had no kind | All handled; `dict-of-object`; `docKeys` (declared, reasoned); self-test 27 cases |
| R3-7 | Per-symbol `standard` / `description` keys (≈350 symbols) and root metadata keys (`description`, `version`, `note`, `$comment`…) are bound by nothing | Declared `docKeys` (documentation) per schema |

110 more JSON files now carry key schemas derived from their classes (seeds, symbol
catalogues, AEC filters, view style packs, title blocks, tag schemes, LOD matrix,
owner standards, material schedule tables, KUT / Kibale overlays). Registry:
193 full schemas, 214 structural-only, 225 non-data.

Note: commit `587481694` (MEP symbol engine) also carries the pump-catalogue rename,
which was already staged; history was not rewritten to split it.

### NEEDS REVIT CHECK (rounds 2–3)

4. **MEP symbol placement.** Open a floor plan with MEP fixtures → run the MEP symbol
   placement command (button tag `Symbols_PlaceMepDetail`). Expected: symbols place on plan views (they did not
   match any view before), with sizes from the catalogue (4/5/8 mm) rather than all 6 mm.
5. **ArchiCAD IFC import maps properties.** Import an ArchiCAD IFC with `Pset_WallCommon.Reference`
   set → STING Interop → ArchiCAD IFC import. Expected: `ASS_PRODCT_COD_TXT` on the walls
   carries the Reference value; before this fix no property mapping wrote anything.
6. **Batch Add Family Params binds MAT_COST_SUPPLY_NR and STING_EMB_CARBON_NR** to
   Materials-category families.
7. **Block-count formula evaluates.** On a masonry wall with `CST_S_MAS_NET_AREA_SQ_M`,
   `BLE_BLOCK_SIZE_TXT` and `CST_S_MAS_WASTAGE_FCT_PCT` set, run the formula pass;
   `CST_CALC_BLOCKS_NR` should be written (it never was).
8. **Material class normaliser.** A material named "Concrete C30" normalises to
   "Concrete".

## Round 4 — JObject readers vs the keys their files carry (45 files)

A subagent listed every key each JObject reader asks for and every key each file
holds, and each hit was checked by hand. No case mismatches.

| # | Finding | Fix |
|---|---|---|
| R4-1 | **Electrical snapshot** (`ElectricalSnapshotBuilder.BuildTemplateRules`) read `rule["match"]["namePatterns"]` / `rule["template"]`, which STING_PANEL_SCHEDULE_TEMPLATES.json never had: empty pattern and template on every row, priority from a loop counter, the fallback row printed a JSON object as a template name | Reads `namePatterns` / `templateName` / `priority`, like `PanelScheduleTemplateRegistry` |
| R4-2 | **Demand factor report**: BS7671_2018 classes carry `notes`, the reader asked for `description` — blank column | Falls back to `notes` |
| R4-3 | **Family swap registry**: `BuildSeedFamiliesCommand` writes flat `entries[]`; the only consumer (`SwapToManufacturerCommand`) reads `seeds[].candidates`. Auto-registered candidates are never read | Design decision — ROADMAP DSCH-13 |
| R4-4 | **Data shadowed by code constants**: STING_WIRE_TABLES (`aluminiumFactor`, `breakerSizes`, `correctionFactors`… vs `WireTableSet.AluminiumFactor`, `VoltageDropEngine.BreakerSizes*`), STING_AIC_TIERS `safetyMarginPct` (engine default 10.0), plumbing supply/drainage limits (`minSlopePct` vs `BSen12056Standards`) — editing the data changes nothing | ROADMAP DSCH-14 |
| R4-5 | Smaller: `designWindMs` absent from all 42 climate sites (always 3.0 m/s); `iaMultipliers.BS88 = "table"` dropped by a numeric-only guard; demand-factor `classificationPatterns.Data` has no load class (falls to 100 %); `GeneralPressureRegimeValidator` reads `_standard`, a key under the `_` comment convention | ROADMAP DSCH-14 |
| R4-6 | **15 files with no plugin reader**: HEALTHCARE_ALERT_ROUTING, LEGIONELLA_REPORT_TEMPLATE, STING_TMV_STANDARDS, STING_ARC_FLASH_PPE, STING_EXTERNAL_FORMATS, STING_US_PRESET_OVERLAY, Healthcare/Specialist/*.json (9); STING_MEDGAS_FAB_RULES read only by a test | Marked in the registry; ROADMAP DSCH-15 |

### NEEDS REVIT CHECK (round 4)

9. **Electrical snapshot template rules.** STING Electrical panel → the snapshot that
   lists panel-schedule template rules. Expected: each rule shows its name patterns
   (MSB, Main Switchboard…) and template name; the last row reads
   "(first template in the project)".
10. **Demand factor report (BS 7671).** Run it with the BS 7671 standard selected;
    the description column carries each class's notes.

## Round 5 — the remaining ~40 JObject / custom-parsed files

Three batches (A: legends, manifests, healthcare, IFC, presets, schemas, BEP; B: LPS,
carbon, keywords, tiers, vocabulary, symbols, brand; C: sustainability, sector packs,
HVAC loads, MEP design, routing, pipe materials).

| # | Finding | Fix |
|---|---|---|
| R5-1 | **Routing rules**: `SeparationRule` / `CorridorBand` had no `[JsonProperty]` for the files' snake_case keys → every separation rule required 0 mm (SeparationChecker never reported), every corridor band admitted every service at FFL+0 | `[JsonProperty]` on 8 members; registry checks both files against the classes (83 failures against the pre-fix class) |
| R5-2 | **Sector packs**: `OVERHEAD_PROFIT_PCT` written as `BOQ_TENDER_OVERHEAD_PROFIT_PCT`; readers ask for `BOQ_TENDER_OHP_PCT`. Values written with the current culture | Mapped; invariant culture |
| R5-3 | **BOQ client vocabulary** never loaded: the `_comment` string made the dictionary deserialisation throw | Object-valued entries only |
| R5-4 | **Material schema checks** read `columns` / `fields`, which MATERIAL_SCHEMA.json never had → "0 columns" failure, Schema Validate checked nothing | Reads `required_columns` / `optional_columns`; it now reports `MAT_COST_UNIT_OF_MEASURE` missing from both libraries → DSCH-16 |
| R5-5 | **Electrical carbon**: hours keyed `Lifts`, category is `Lifts / Elevators` → lifts at 3000 h not 1500 h | Key renamed |
| R5-6 | `project_bep.json`: shipped file used as a fallback with keys it lacks; `BIMManagerCommands` writes a project's BEP over the corporate file in `DataPath`; BREEAM Man 01 and the "BIM Execution Plan" readiness check pass because the shipped file exists | ROADMAP DSCH-17 (behaviour change needs an owner) |
| R5-7 | `TAG_PLACEMENT_PRESETS_DEFAULT.json` never loaded (loader reads a different name and shape); `family-library/manifest.json` never read (URL/SHA are constants; the dialog points at a config key nothing reads); `IFC/STING_IFC_PSET_MAPPING.json` 75 of 132 rows use keys `IfcPsetMapping` does not bind, and the class has no callers; `STING_CLIMATE_MONTHLY` sites carry no latitude (southern-hemisphere orientation backwards) | ROADMAP DSCH-18 |
| R5-8 | Data shadowed by constants (LPS tolerable risk, `locationCd`, SPD coordination rules, pipe `manningN`, hanger spacing; brand `fonts` / page layout reach nothing) | Added to DSCH-14 |
| R5-9 | Validator ran 1 m 51 s (class scan per array element) | Memoised: 5 s |

### NEEDS REVIT CHECK (round 5)

11. **Service separation is enforced.** Model a power tray and a data tray running in
    parallel 100 mm apart → run the separation check (routing validation). Expected: a
    violation (rule PWR_DATA_PARALLEL_ENCLOSED, 200 mm). Before: none, ever.
12. **Auto-drop corridor bands.** Run Routing → Auto drop on a hot-water pipe; the drop
    should target the HWS band (2800-2900 mm), not FFL+0.
13. **Sector pack OH&P.** Apply the Healthcare sector pack → BOQ tender dialog shows its OH&P %.
14. **BOQ client vocabulary.** Set the employer to "NHS England" and export a BOQ;
    paragraphs use that client's terms.

## Round 6 — the project overlays (KUT, Kibale) against their readers

Rebased on `origin/main` first (24 new commits, clean; gate green).

| # | Finding | Fix |
|---|---|---|
| R6-1 | **Kibale `rate_card.json` never priced anything**: compiled priority 87 is below the CSV (90) and material library (95), chain highest-first; the registry comment claimed the opposite | `GUIDES/kibale-project-config/boq_rate_policy.json` (rate card 93, as KUT); comment corrected; README row |
| R6-2 | Kibale `AUTO_TAGGER_DISC_FILTER: ["A","S"]` → JSON text split on `,` → filter matching nothing → auto-tagger skips every element | Reader tolerates arrays; overlay uses `"A,S"` |
| R6-3 | Kibale `SEQ_SCHEME: "DISC_SYS_LVL"` is not a scheme; dropped silently | Overlay → `Numeric` with a note; TagConfig now warns on an unknown value |
| R6-4 | `ClassificationStandard.Load` read only raw `<rvtDir>/_BIM_COORD`, `Set` writes the consolidated `_data/coord` → choice lasted one session | Load reads `StingPaths.MetaFile` first, raw path as fallback |
| R6-5 | KUT `project_config.json` ships inside `_BIM_COORD/`, but every reader looks beside the `.rvt` (then `data/`) → the six-building LOC codes and SEQ grouping are never applied | Template layout decision → ROADMAP DSCH-19 |
| R6-6 | Kibale `boq_custom_templates.json`: templates sharing a category carry no variant matcher, so four are never selected; the deliberately zero "Generic Models" rate-card row is dropped (`UnitRate <= 0`) | Authoring / design → ROADMAP DSCH-19 |

Gates after round 6: validator OK (633 files), self-test 27/27, `run_ci_gates.py --quick`
50/50, all 16 unit-test projects green (Revit.SmokeTests needs a running Revit and is not
a CI job: 8 of 13 fail without one, as on main).

### NEEDS REVIT CHECK (round 6)

15. **Classification standard persists.** BIM → set classification standard to CSI, close
    and reopen the model; it is still CSI.
16. **Kibale pricing.** With the Kibale overlay copied into `_data/coord` (incl. the new
    `boq_rate_policy.json`), run a BOQ; wall/floor rates come from `rate_card.json`.

## Round 7 — verification of rounds 1-6, and what the fixes woke up

An independent review re-derived every fix (CostRateCsv gives the same 115 keys and
rates as the old loader for the shipped file, plus the deliberate `S|FND`). No
regressions. New defects, three of them live only because earlier fixes made data load:

| # | Finding | Fix |
|---|---|---|
| R7-1 | ArchiCAD mappings now load: six target `PER_U_VALUE_W_M` (real: `PER_U_VALUE_W_M2K`); five target parameters defined nowhere; IFC booleans (`TRUE`, `.T.`) dropped for every YES/NO target; an unbound target wrote nothing silently | Name fixed; booleans parsed; unbound target logged once; `valueRefersTo` gate on `sting_param` (5 undefined listed → DSCH-21) |
| R7-2 | Separation rules now load: max-over-all-rules held a crossing (50 mm) to the parallel open-tray 300 mm | `RulesFor(.., crossing)`; checker detects perpendicular straight runs |
| R7-3 | Material schema check now reads the schema: counted thousands of empty cells as violations | `required_columns` = column presence only |
| R7-4 | `ConfigureCostFileCommand` described a model-folder file and a layout no reader understands; Cost File Browser judged the first physical line as the header | Text corrected; browser uses `CostRateCsv.Parse` |
| R7-5 | `STING_ELECTRICAL_CARBON` has no hours for Cooking / Water Heating / Space Heating / Process (3000 h default); `Lighting_24x7` is a category nothing produces | Not invented: → DSCH-14 |

Gate: self-test 28/28. `run_ci_gates.py --quick` 50/50; Tags 5,158, Boq 1,373, Routing 80,
Sustainability 438.

### NEEDS REVIT CHECK (round 7)

17. **Crossing vs parallel separation.** Repeat check 11 with the data tray crossing the
    power tray at 90° and 100 mm apart: no violation (crossing rule, 50 mm). Parallel at
    100 mm: violation at 300 mm (enclosure is not known from the model).
18. **ArchiCAD IFC booleans.** Import an IFC whose `Pset_WallCommon.IsExternal` is `.T.`;
    the mapped YES/NO parameter is set.

## Round 8 — verification of round 7

| # | Finding | Fix |
|---|---|---|
| R8-1 | **Regression from R7-2**: with `crossing`, only crossing/any rules applied, so pairs with only parallel / vertical rules (gas–power, hot–cold water, drainage over water) required 0 mm at a crossing; and the drop curve is vertical, so every horizontal neighbour read as "crossing" (power/data relaxed 300 → 50 mm) | **Reverted** (690b157a1). Conservative max restored; geometry/enclosure-aware selection → ROADMAP DSCH-22. NEEDS REVIT CHECK 17 is withdrawn |
| R8-2 | ArchiCAD "target not found" warning fired even when the built-in fallback wrote the value | Warns only when nothing wrote |
| R8-3 | `STING_IFC_PSET_MAPPING.json` names `ASS_TAG_1` (real: `ASS_TAG_1_TXT`); its reader has no callers | Fixed |
| R8-4 | Registry exceptions (`alsoAllowed`) were never checked for staleness | Gate fails when an allowed name now exists; self-test 29 |
| R8-5 | Parameter-name scan of all Data JSON (`sting_param` / `param` / `parameter` / `target` …, ~12,000 refs): no other misses (AEC filter `BIM_LOD` is a documented external name) | — |

Decision recorded: when a fix's refinement cannot be made safe from the model alone
(enclosure, true crossings), keep the conservative check and record the gap, rather
than relax a safety check on a heuristic.

## Round 9 — verification of round 8: CLEAN

No defects. The revert restores `RoutingRules.cs` / `SeparationChecker.cs` byte-for-byte
(empty diff against 3e089c39d~1) and nothing referenced the removed members;
`missingTarget` is correct on every path; `ASS_TAG_1_TXT` exists and no other value in
the pset map is missing; the stale-allowance walk handles refs without `alsoAllowed` and
unreadable targets. One hardening taken from it: `STING_IFC_PSET_MAPPING.json`
`sting_param` is now under `valueRefersTo` (reintroducing `ASS_TAG_1` fails the gate).

Stopping here: a full round found nothing new.

## Totals

| Round | Real defects found | Fixed on this branch | Recorded (ROADMAP) |
|---|---|---|---|
| 1 | 10 | 10 | DSCH-1 |
| 2 | 14 | 11 | DSCH-3..8, DSCH-12 |
| 3 | 7 | 6 | DSCH-9, DSCH-11 |
| 4 | 6 | 3 | DSCH-13..15 |
| 5 | 9 | 6 | DSCH-14, DSCH-16..18 |
| 6 | 6 | 4 | DSCH-19, DSCH-20 |
| 7 | 5 | 4 | DSCH-14, DSCH-21 |
| 8 | 5 (1 a regression of round 7) | 4 (incl. the revert) | DSCH-22 |
| 9 | 0 | — | — |
