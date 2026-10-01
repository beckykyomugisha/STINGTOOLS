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
