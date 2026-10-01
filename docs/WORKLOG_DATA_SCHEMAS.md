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
