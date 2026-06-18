# Implementation Prompt — Phase 192: Kampala Uganda Temple (KUT) project alignment pack

> **Audience**: terminal coding agent with a Windows build environment
> (`dotnet build` against the Revit 2025 API **must** be run after every
> work item — that is the main advantage you have over the sandbox agent
> that authored Phase 191).
>
> **Repo**: STINGTOOLS — C# Revit 2025/2026/2027 plugin (`net8.0-windows`).
> Read `CLAUDE.md` (root) first: conventions, command patterns, transaction
> rules, data-file conventions. Log completed work in `docs/CHANGELOG.md`
> as `#### Completed (Phase 192x — …)` blocks. Do NOT extend `CLAUDE.md`
> except where directory/command structure changes.
>
> **Build check (do this FIRST)**: Phase 191 (commit on this branch) was
> authored without compile verification. Before any new work, run
> `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
> and fix any compile errors in: `Core/TagSchemeEngine.cs`,
> `Tags/TagSchemeCommands.cs`, `Core/SeqAssigner.cs`, `Core/TagConfig.cs`,
> `Core/TagConfig.Defaults.cs`, `Core/ParameterHelpers.cs`,
> `Core/ParamRegistry.cs`, `UI/StingCommandHandler.cs`,
> `Tags/TokenWriterCommands.cs`, `Tags/PreTagAuditCommand.cs`,
> `Organise/TagOperationCommands.cs`.

---

## Project context (read once, it drives every priority below)

STING is being deployed as the BIM Manager's toolset on the **Kampala
Uganda Temple Project** (six buildings, ~6,597 m², 52-month programme:
8 months documentation + 32 months construction + 12 months DLP). The
Owner (LDS Church Special Projects Department) mandates:

- **Revit native + PDF at every gate**; Deliverable A = LOD 200,
  Deliverable B (50%) = LOD 300, Deliverable C (100%) = LOD 350
  (proposed), Conformed set, **Deliverable D = LOD 400 as-built record
  model within 60 days of furniture installation**.
- **Owner platforms**: ACC (CDE), Navisworks (clash), **Bluebeam Studio**
  (bookmarked/hyperlinked review sessions, 2-week Owner review, comment
  resolution gates phase completion), **Fohlio** (FF&E/finishes/O&M —
  single source of truth, CDE links to it and never duplicates),
  **RIB SpecLink** (all specifications, CSI format), Teams, Niagara (BMS).
- **US standards**: CSI MasterFormat divisions 21/22/23/26/27/28,
  NFPA 13/72, lightning protection, NEC-style electrical calcs (voltage
  drop / demand / diversity / short-circuit on drawings), **ComCheck**
  energy-code input (lighting), ADA, AWI woodwork, ASHRAE 90.1/62.1.
- **A1 cross-cutting scope items**: Device Coordination (§7), Program
  Audit against the Owner's live Excel template (§19), door/window/
  hardware schedules, Group A stone with chain of custody, bilingual
  specifications.
- **Tag/naming**: ISO 19650 container naming `KUT-ZZZ-XX-XX-M3-A-0001`
  (project-originator-volume-level-type-discipline-number). Phase 191
  implemented the element-tag side as a **second rendering of the
  canonical 8 STING tokens** (see `Core/TagSchemeEngine.cs` and the
  CHANGELOG Phase 191 entry). The Owner's own BIM standards arrive in
  week 1 of mobilisation and supersede interim conventions — therefore
  EVERYTHING in this prompt must be **data-driven** (JSON/CSV overlays
  under `<project>/_BIM_COORD/`), never hardcoded.

**Architecture rules that apply to every item below** (these mirror how
the codebase already works — follow existing patterns, don't invent):

1. Corporate baseline data file in `StingTools/Data/` + project overlay
   in `<project>/_BIM_COORD/<file>.json`, merged by id, project wins
   (pattern: `DrawingTypeRegistry`, `TagSchemeRegistry`,
   `MepSizingRegistry`).
2. Commands: `[Transaction(TransactionMode.Manual)]` for writers,
   `ReadOnly` for audits; `TaskDialog` for UX; `StingLog` for logging;
   `OutputLocationHelper.GetOutputPath(doc, name)` for CSV/XLSX outputs;
   register a `case` in `UI/StingCommandHandler.cs` and (where a
   workflow may chain it) in `WorkflowEngine.ResolveCommand` + the
   known-tags list.
3. New shared parameters: UUIDv5 in namespace
   `a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a`, registered in
   `Core/ParamRegistry.cs` (constants), `Data/PARAMETER_REGISTRY.json`
   (`support_params`), `Data/MR_PARAMETERS.txt` + `.csv`.
4. Validators follow the `Core/Validation/` + `ValidationResult` pattern;
   gateable packs follow the `HealthcareValidatorGate` profile-gate
   pattern.
5. After each work item: `dotnet build` clean, then commit with a clear
   message. One logical item per commit.

---

## PART A — Finish the Phase 191 tag-scheme work (small, do first)

### A1. Token Confidence Audit command  *(highest priority — contractual gate-audit instrument)*

**Why**: STING's auto-population is guaranteed-fill — it never leaves a
token blank, it falls back to defaults (`LOC→BLD1`, `ZONE→Z01`,
SYS layer 7 = discipline default). On a six-building campus a default is
indistinguishable from a correct value in a completeness report, and the
error surfaces at the Deliverable D record-model verification. The
engine already writes provenance on every element:
`ASS_LOC_SOURCE_TXT` (values: `TYPE_OVERRIDE` / `Room` / `ProjectInfo` /
`Workset` / `Default`), `ASS_ZONE_SOURCE_TXT` (`TYPE_OVERRIDE` / `Room`
/ `Default`), `ASS_SYS_DETECT_LAYER_INT` (1–7; 1–5 = genuine detection,
6 = category fallback, 7 = discipline default). This item is purely the
reporting layer on top.

**Build**: `StingTools/Tags/TokenConfidenceAuditCommand.cs`
(`[Transaction(TransactionMode.ReadOnly)]`, handler tag
`TokenConfidenceAudit`).

- Scope: selection-else-project (reuse
  `Tags.TagSchemeCommandHelper.CollectScope`).
- For every element with a non-empty `ASS_TAG_1_TXT`: classify each of
  LOC / ZONE / SYS into **High** (Room / TYPE_OVERRIDE / Workset; SYS
  layers 1–5), **Medium** (ProjectInfo; SYS layer 6), **Low/Fallback**
  (`Default` or empty source; SYS layer 7 or unset).
- Report (TaskDialog): totals per confidence band; per-discipline and
  per-category fallback counts (top 10 worst categories); count of
  elements whose LOC is the literal default `BLD1` **with**
  `LOC_SOURCE=Default` (the silent-wrong-building case); first 10
  offender ElementIds.
- CSV (`STING_TokenConfidence_Audit.csv` via `OutputLocationHelper`):
  one row per element with any Low-band token —
  `ElementId,Category,Discipline,LOC,LOC_SOURCE,ZONE,ZONE_SOURCE,SYS,SYS_LAYER,Bands`.
- Wire into `WorkflowEngine.ResolveCommand` (tag
  `TokenConfidenceAudit`) so gate-audit workflow presets can chain it.

### A2. Registry cache invalidation + UI exposure

- In `Core/StingToolsApp.cs` document-close handler (where
  `LiveProfileSync.InvalidateCache` etc. are already called), add
  `TagSchemeRegistry.InvalidateCache(doc)`.
- Add three buttons to the dock panel TAGS tab (follow the existing
  WrapPanel pattern in `UI/StingDockPanel.xaml`): "Render Scheme Tags"
  (`TagScheme_Render`), "Scheme Inspect" (`TagScheme_Inspect`), "Scheme
  Audit" (`TagScheme_Audit`), plus "Token Confidence"
  (`TokenConfidenceAudit`) next to the existing QA buttons.
- Add the four tags to `WorkflowEngine` known-tags + `ResolveCommand`.
- Optional: add NLP patterns in `Tags/NLPCommandProcessor.cs`
  ("render scheme tags", "scheme audit", "token confidence").

### A3. KUT project-config worked example

Create `docs/examples/KUT/` containing ready-to-copy project overlay
files (these are documentation artifacts — the BIM Manager copies them
into the live project's `_BIM_COORD/`):

- `project_config.json` — `LOC_CODES` = `["BLD1","BLD2","BLD3","BLD4","BLD5","BLD6","EXT"]`
  (Temple / Meetinghouse / Housing / Grounds / Utility / Guard House /
  site), `CUSTOM_VALID_LOC` to match, `SEQ_INCLUDE_LOC: true`,
  `SEQ_INCLUDE_ZONE: false`.
- `tag_schemes.json` — the `kut-temple-example` scheme from
  `Data/STING_TAG_SCHEMES.json` copied with `"enabled": true`.
- `README.md` — 1-page setup sequence: seed `PRJ_ORG_PROJECT_CODE_TXT`
  = `KUT` + originator on Project Information → bind params
  (`LoadSharedParams`) → enable scheme → `TagScheme_Render` →
  `TagScheme_Inspect` → `TokenConfidenceAudit`. State the BEP rules:
  per-building workset prefixes (`BLD2_Mechanical`) OR one model per
  building with Project Information LOC set; rooms placed before first
  coordination publish.

### A4. Spatial-boundary LOC detection (optional, last in Part A)

Site elements (civil, external lighting) have no rooms and often no
meaningful worksets. Extend `SpatialAutoDetect.DetectLoc` with one more
fallback layer **before** the `BLD1` default: scope-box containment.
Scope boxes named `STING-LOC::<locCode>` (e.g. `STING-LOC::BLD2`)
declare a building boundary; an element whose bounding-box centre falls
inside (XY test, ignore Z) gets that LOC with `LOC_SOURCE="ScopeBox"`.
Cache the scope-box list on `TokenAutoPopulator.PopulationContext`
(follow the `CachedGrids` pattern). Update the Token Confidence Audit
(A1) to treat `ScopeBox` as High confidence. Document the naming
convention in the A3 README.

---

## PART B — Owner-standards compliance pack (the contractual core)

### B1. LOD Verification Engine  *(the single highest-value item in this prompt)*

**Why**: the BIM Manager's fee is anchored on model audits + LOD
verification at Deliverables A (LOD 200), B (300), C (350), conformed
set, quarterly construction audits, and the Deliverable D LOD 400
record-model verification. Nothing in STING verifies element maturity
against an LOD matrix today.

**Data** — `StingTools/Data/STING_LOD_MATRIX.json` (+ project overlay
`_BIM_COORD/lod_matrix.json`, merge by id):

```jsonc
{
  "version": "1.0",
  "milestones": [
    { "id": "deliverable-a", "name": "Deliverable A (Basis of Design)", "lod": 200 },
    { "id": "deliverable-b", "name": "Deliverable B (50% documentation)", "lod": 300 },
    { "id": "deliverable-c", "name": "Deliverable C (100% documentation)", "lod": 350 },
    { "id": "conformed-set", "name": "Conformed set", "lod": 350 },
    { "id": "deliverable-d", "name": "Deliverable D (record model)", "lod": 400 }
  ],
  "categoryRules": [
    {
      "category": "Mechanical Equipment",          // Revit category name; "*" = default rule
      "checks": {
        "200": { "requireGeometry": true, "forbidPlaceholderFamilies": false,
                  "requiredParams": ["ASS_TAG_1_TXT"] },
        "300": { "requireGeometry": true, "forbidPlaceholderFamilies": true,
                  "requiredParams": ["ASS_TAG_1_TXT","ASS_SYSTEM_TYPE_TXT"],
                  "requireTypeNotGeneric": true,
                  "requiredDims": ["HVC_FLOW_LS"] },               // numeric params that must be > 0 / non-empty
        "350": { "inherit": "300", "requireNoUnresolvedClash": false,
                  "requiredParams": ["+ASS_PRODCT_COD_TXT"] },      // "+" = add to inherited list
        "400": { "inherit": "350",
                  "requiredParams": ["+ASS_MODEL_REF_TXT","+MNT_TYPE_TXT"],
                  "requireManufacturerType": true }                  // type/family name must not match placeholder regexes
      }
    }
  ],
  "placeholderFamilyPatterns": ["(?i)generic", "(?i)placeholder", "(?i)^STING_SEED_"]
}
```

Author sensible corporate-baseline rules for the ~20 highest-volume
categories (walls, floors, doors, windows, mech equipment, elec
equipment, lighting fixtures, plumbing fixtures, ducts, pipes, conduits,
cable trays, air terminals, sprinklers, fire alarm devices, structural
framing/columns, casework, specialty equipment) plus a `"*"` default.
Keep checks honest: only what is verifiable from the Revit API
(geometry presence via `get_Geometry`/bbox, family/type naming, shared
parameter presence/non-emptiness, numeric > 0). Do NOT pretend to
verify geometric accuracy — document that limitation in the command
output ("parameter/naming maturity proxy, not geometric survey").

**Engine** — `StingTools/Core/Validation/LodVerificationEngine.cs`:
`Verify(doc, milestoneId, scope) → LodVerificationResult` with
per-element pass/fail + failed-check reasons, per-category and
per-discipline rollups, overall %.

**Commands** — `StingTools/Commands/Validation/LodVerifyCommand.cs`:
- `LOD_Verify` (ReadOnly): milestone picker (use `StingListPicker`),
  runs engine, TaskDialog summary + CSV
  (`STING_LOD_<milestone>_Audit.csv`) + writes a JSON gate-report to
  `_BIM_COORD/lod_reports/<milestone>_<yyyyMMdd>.json` (these are the
  artefacts that go in front of the Owner alongside drawings).
- `LOD_Stamp` (Manual, optional): write the verified milestone id into a
  new shared param `ASS_LOD_VERIFIED_TXT` on passing elements (new
  param, UUIDv5, full registration per the rules above).
- Workflow preset `Data/WORKFLOW_GateAudit.json`: ValidateTags →
  TokenConfidenceAudit → LOD_Verify → CompletenessDashboard →
  TagScheme_Audit — the standing gate-audit chain.

### B2. Owner Standards Pack (rule-pack loader)

**Why**: the Owner's BIM modeling standards arrive in week 1 and
supersede everything. Encoding them must be a configuration exercise.

**Build** — `StingTools/Core/Validation/OwnerStandardsPack.cs` +
`Data/STING_OWNER_STANDARDS_PACK.json` (+ `_BIM_COORD/owner_standards.json`
overlay):

- Rule types (extensible enum, switch in one evaluator):
  `paramRequired` (param X non-empty on categories Y),
  `paramPattern` (value matches regex), `paramInList` (value ∈ list),
  `familyNamePattern` (family name matches/forbidden regex per category),
  `typeNamePattern`, `worksetPattern` (workset names match
  `^(BLD[1-6]|EXT)_`), `viewNamePattern`, `sheetNumberPattern`,
  `tagSchemeConsistent` (delegates to the Phase 191
  `TagSchemeAuditCommand` logic — reuse `TagSchemeRenderer.Render`,
  do not duplicate).
- Each rule: `id`, `severity` (BLOCK/WARN/INFO), `description`,
  `source` (cite the Owner doc section — these strings go in front of
  the client).
- Command `OwnerStandards_Audit` (ReadOnly): run all rules, RAG
  summary, CSV + JSON report to `_BIM_COORD/owner_standards_reports/`.
  Ship a starter baseline encoding what A1/A2 already tell us (workset
  prefixes, `KUT-…` sheet naming pattern as a default-off rule,
  required `PRJ_ORG_*` fields populated, scheme-tag consistency).

### B3. Program Audit comparator (A1 §19)

**Why**: recurring milestone deliverable — audit model rooms against the
Owner's live Excel program template; output compliance + deficiency log.

**Build** — `StingTools/Commands/Validation/ProgramAuditCommand.cs`
(tag `Program_Audit`, ReadOnly) + small engine
`Core/Validation/ProgramAuditEngine.cs`:

- Input: Excel file picked by user (use ClosedXML — already referenced).
  Expected columns (make header-name matching forgiving,
  case/whitespace-insensitive, and configurable via
  `_BIM_COORD/program_audit_map.json`): `Room Name`, `Room Number`
  (optional), `Required Area` (+ unit m²/ft², default m²),
  `Department`/`Zone` (optional), `Required Count` (optional),
  `Building`/`Volume` (optional → matches LOC).
- Join order: Room Number → exact name → normalised name
  (trim/case/strip punctuation). Unjoined template rows = "missing from
  model"; unjoined model rooms = "not in program".
- Compare: area within tolerance (default ±5%, configurable), count per
  room type, building assignment when the template carries one.
- Output: TaskDialog summary (compliant / over / under / missing /
  extra), full XLSX deficiency log
  (`STING_ProgramAudit_<yyyyMMdd>.xlsx`) with a `Status` column per row
  (matches A1's "program audit + deficiency log" deliverable shape).

### B4. Device Coordination validator (A1 §7)

**Why**: explicit A1 scope — coordinate device locations against art,
furniture, millwork; switches/outlets/thermostats/sensors/AV vs
finishes and elevations. Labor-intensive manually; mostly geometric
proximity checks.

**Build** — `Core/Validation/DeviceCoordinationValidator.cs` +
`Commands/Validation/DeviceCoordinationCommand.cs` (tag
`DeviceCoord_Audit`, ReadOnly) + `Data/STING_DEVICE_COORD_RULES.json`
(+ overlay):

- Rule shape: `{ "id", "deviceCategories": [...], "againstCategories":
  [...], "minClearanceMm", "alignmentToleranceMm", "sameWallOnly":
  true, "severity", "description" }`.
- Ship a starter rule set: switch/outlet vs door swing side (door
  hinge-side check via FamilyInstance facing/hand), devices vs casework
  clearance, thermostats/sensors mounting-height consistency per room
  (compare Z within room, flag outliers > 50 mm), devices overlapping
  art/specialty equipment bounding boxes on the same wall, fire-alarm
  device vs decorative-lighting clearance.
- Geometry: bounding-box + wall-host comparisons only (cheap, robust).
  Group findings per room, CSV export, first-20 in dialog. Reuse the
  clash kernel's AABB helpers (`StingTools/Clash/AabbSweep`) if
  convenient — do not spin up the full clash engine.

---

## PART C — Platform integrations (Fohlio + SpecLink + Bluebeam)

### C1. Fohlio ExLink profile (FF&E / finishes round-trip)

**Why**: Fohlio is the Owner's single source of truth for FF&E,
finishes and O&M. The proposal's information hierarchy is
"CDE links to Fohlio, never duplicates". The integration is therefore a
**parameter sync + reference-link layer**, not a data copy.

**Build** (follow `ExLink/ExLinkDefaultLinks.cs` — 12 CAFM profiles
already exist; add Fohlio as the 13th):

1. `ExLink/FohlioLink.cs` — link profile + column mapping:
   STING/Revit params ↔ Fohlio fields. Default mapping (configurable
   via `_BIM_COORD/fohlio_map.json`):
   `ASS_TAG_1_TXT ↔ Item Tag/Code`, `Family`/`Type ↔ Product`,
   `Manufacturer`, `Model` (`ASS_MODEL_REF_TXT`), room
   (`Room Name/Number` via spatial lookup), finish params, cost ref,
   plus a **`FOHLIO_REF_TXT`** new shared param (full registration)
   holding the Fohlio item URL/ID — the "link, never duplicate" key.
2. Transport, two tiers:
   - **Tier 1 (ship now)**: CSV/XLSX exchange — `Fohlio_Export`
     (Manual/ReadOnly as appropriate) emits the FF&E register in
     Fohlio's import shape (categories: Furniture, Furniture Systems,
     Casework, Plumbing Fixtures, Lighting Fixtures, Specialty
     Equipment); `Fohlio_Import` reads a Fohlio export and writes back
     `FOHLIO_REF_TXT` + selected fields (SetIfEmpty unless user picks
     overwrite), with a preview/diff dialog before any write (reuse
     `ExcelLinkEngine` change-tracking).
   - **Tier 2 (stub + interface now, wire later)**: `IFohlioTransport`
     with a REST implementation skeleton (Fohlio has a v2 REST API;
     auth = API key header; base URL + key from
     `_BIM_COORD/fohlio_connection.json` — never hardcode, never commit
     keys). Implement list/get/update-item calls behind the interface;
     gate actual HTTP behind a "Test connection" button so the CSV path
     stays the default. If the public API docs are unreachable from
     your environment, keep Tier 2 as a clean stub with TODOs — the
     CSV path is the contractual deliverable.
3. Audit command `Fohlio_Audit` (ReadOnly): FF&E elements missing
   `FOHLIO_REF_TXT`, stale rows (model param ≠ last-imported Fohlio
   value snapshot stored in ExtensibleStorage — add a small
   `StingFohlioSnapshotSchema` following `Core/Storage/` patterns),
   counts per category. This powers the monthly "Fohlio kept current"
   KPI line.

### C2. SpecLink / CSI MasterFormat cross-reference

**Why**: all specifications live in RIB SpecLink, CSI-format. STING is
Uniclass/NRM2-centric today. The win is keeping model keynotes /
assembly classifications reconciled with the spec TOC per milestone.

**Build**:

1. **CSI MasterFormat layer** — new shared params (full registration):
   `CSI_SECTION_TXT` (e.g. `23 31 00`), `CSI_TITLE_TXT`. New data file
   `Data/STING_CSI_MASTERFORMAT_MAP.csv`: Revit category (+ optional
   family/type regex + optional SYS token) → CSI section + title.
   Author the obvious MEP/arch mappings for divisions 21/22/23/26/27/28
   + 08 (openings) + 09 (finishes) + 10/12 (specialties/furnishings) —
   ~80–120 rows is plenty for a starter; project overlay
   `_BIM_COORD/csi_map.csv` extends/overrides.
2. `CSI_Assign` command (Manual): walk taggable elements, resolve
   section via the map (category → refine by family regex → refine by
   SYS), write both params (SetIfEmpty default, overwrite option).
   Report unmapped categories.
3. `SpecLink_Reconcile` command (ReadOnly): input = the spec TOC
   exported from SpecLink (CSV or XLSX with `Section`,`Title` columns —
   header-forgiving like B3). Output three lists: model CSI sections
   with no spec section (spec gap), spec sections with no model content
   (possible over-specification — INFO only), section-title mismatches.
   XLSX report. This is run at every 50%/100% set.
4. Add `CSI_SECTION_TXT` to the COBie/handover export column options if
   trivially done via existing config (do not refactor exporters).

### C3. Bluebeam Studio comment close-out tracker

**Why**: comment resolution is a phase-completion condition (A1) and a
monthly KPI in the proposal (§4.6). Bluebeam Studio exports comment
summaries (CSV/XLSX/PDF — support CSV + XLSX).

**Build** — `Docs/ReviewCommentTracker.cs` +
`Docs/ReviewCommentCommands.cs` (tags `ReviewComments_Import`,
`ReviewComments_Dashboard`, `ReviewComments_Export`):

- Import: parse Bluebeam comment-summary CSV/XLSX (typical columns:
  Subject/Comment, Page Label, Author, Date, Status, Reply count —
  header-forgiving mapping configurable via
  `_BIM_COORD/review_comment_map.json`). Store rows in
  `_BIM_COORD/review_comments.json` keyed by session id + comment id
  (re-import = upsert; track first-seen/last-seen).
- Each comment: `sessionId`, `gate` (Deliverable A/B/C/conformed —
  picked at import), `owner` (assignable in the dashboard), `status`
  (Open / Answered / Resolved-pending-Owner / Closed), `ageDays`.
- Dashboard (reuse `StingDataGridDialog`): filter by gate/status/owner,
  close-out rate % (the KPI), overdue list (configurable SLA days).
- Export: monthly KPI CSV — gate, total, closed, close-out %, mean age,
  overdue count. Matches the proposal's monthly BIM status report line.

---

## PART D — US standards layer

### D1. ComCheck lighting input generator

**Why**: A1 lighting scope requires ComCheck input. STING already
computes LPD per ASHRAE 90.1 (`LightingPowerDensityCommand`).

**Build** — `Commands/Electrical/Lighting/ComCheckExportCommand.cs`
(tag `ComCheck_Export`, ReadOnly): emit a CSV/TXT structured for
ComCheck interior-lighting entry: one row per space —
space type (from `HVC_SPACE_TYPE_TXT` or room name mapping in
`Data/STING_COMCHECK_SPACE_MAP.csv` + overlay), floor area, allowed LPD
(reuse the existing ASHRAE 90.1 tables in the LPD engine — do NOT
duplicate the table), installed fixture schedule per space (fixture id,
description, lamps, watts each, quantity, total watts). Include a
summary block (total allowed vs proposed W). If a true ComCheck `.cck`
XML format is achievable cheaply, add it; otherwise the CSV companion
is the deliverable (designers paste into ComCheck — state this in the
dialog).

### D2. NFPA-flavoured validation presets (data, not code)

Do NOT build new calculation engines. Where STING validators are
BS/CIBSE-parameterised, expose US presets as data:

- Check `Core/Calc/` + `Core/Validation/` for hardcoded BS-only limits
  reachable from JSON registries (`STING_MEP_SIZING_RULES.json` already
  carries NEC tray/conduit fill). Add a
  `Data/STING_US_PRESET_OVERLAY.json` documented example overlay
  (sizing-rule values per NEC/SMACNA/ASHRAE where the registry already
  has the slot) + a `docs/US_STANDARDS_PRESET.md` page mapping which
  existing engines are already US-capable (NEC demand factors, NEC 392
  tray fill, IEEE 1584 arc flash, ASHRAE 90.1 LPD, Hunter's method in
  plumbing) and which remain BS-only (flag, don't fix, unless trivial).
- Lightning: the LPS module is BS EN 62305. Add an INFO-level note in
  its report when `PRJ_ORG_*` country is US/owner-mandated NFPA 780 —
  "performance spec by engineer of record; STING LPS figures are
  EN 62305" (A2 makes lightning a performance spec anyway).

---

## PART E — Project intelligence

### E1. Prototype drift report

**Why**: the temple adapts an Owner prototype (1-40 F) with significant
site adaptation; Owner peer reviewers will repeatedly ask "what changed
vs prototype?".

**Build** — extend `BIMManager/ParameterDiffCommands.cs` patterns into
`PrototypeDrift_Report` (ReadOnly): user picks the prototype model
(linked RVT chosen from loaded links, or a second open document).
Compare against current model per category: type counts
(added/removed/renamed types), key dimensional params on matching type
names, room program deltas (names/areas). Output XLSX register grouped
by discipline with a `Delta` column. Keep matching key = (category,
type name) — document that element-level GUID matching across detached
prototypes is unreliable, type-level is the honest grain.

### E2. 40-year HVAC life-cycle cost comparison (A1 §17)

**Build** — `Core/Hvac/LifeCycleCostEngine.cs` +
`Hvac_LifeCycleCompare` command + `Data/STING_HVAC_LCC_DEFAULTS.json`
(+ overlay): inputs per system option (2+ options) — capital cost,
annual energy cost (kWh × tariff; pull loads from the BlockLoad stamps
when present), annual maintenance cost/m², component replacement cycles
(years + % of capital), escalation %, discount % (do BOTH nominal and
NPV columns), 40-year horizon. Output XLSX: year-by-year table per
option + cumulative chart data columns + summary (40-yr total, NPV,
crossover year). This satisfies the A1 mechanical "40-year by year
financial comparison breakout including graphs" deliverable (graphs are
charted from the XLSX columns — state that in the dialog).

### E3. Kampala climate + regional data

- Check `Data/STING_CLIMATE_DATA.json` for a Kampala/Entebbe entry; if
  absent add it (ASHRAE design-day data for Entebbe station: cooling
  0.4% DB ≈ 29–30°C / MCWB ≈ 22–23°C, heating 99.6% ≈ 16–17°C,
  elevation ≈ 1,155 m — verify against ASHRAE 2021 climatic data before
  committing; cite source in the JSON entry).
- Confirm `PRJ_CLIMATE_SITE_ID` auto-stamp resolves it (the
  DocumentOpened fuzzy-match reads the address).
- Add a `docs/examples/KUT/climate_data.json` overlay example if the
  corporate entry is contentious.

### E4. Temple-specific seed content (small, optional)

- `Data/Seeds/`: extend `STING_SEED_PlumbingFixture.json` (or add a new
  seed) with a **baptismal font** variant — DCW/DHW supply + SAN drain +
  recirculation connectors, params for fill rate, heater capacity, water
  treatment loop (A1 mentions "font systems" repeatedly).
- Decorative-lighting hoisting params (A1 §5 lighting schedule):
  `LTG_HOIST_WEIGHT_KG`, `LTG_HOIST_MOTOR_TXT`, `LTG_HOIST_DROP_MM`
  (full param registration), added to the lighting schedule definitions
  in `MR_SCHEDULES.csv` if the schedule pipeline picks columns from
  there (verify before editing).

---

## PART F — Wrap-up requirements (apply to the whole prompt)

1. **Build + smoke**: `dotnet build` clean at every commit. If you have
   a Revit test environment, exercise: LoadSharedParams (verify
   `ASS_TAG_SCHEME_TXT` + every new param binds), BatchTag on a sample
   model, TagScheme_Render/Inspect/Audit, TokenConfidenceAudit,
   LOD_Verify with the corporate matrix, Program_Audit with a 5-row
   test XLSX you create, Fohlio_Export/Import round-trip on CSV,
   SpecLink_Reconcile with a 10-row TOC fixture, ReviewComments_Import
   with a synthetic Bluebeam CSV. Add fixtures under
   `Tests/fixtures/kut/` and unit tests for the pure-logic engines
   (LOD rule evaluation, program join, CSI resolution, LCC math,
   SeqAssigner key shapes incl. the new LOC overload) in the existing
   `Tests/` project.
2. **Registrations**: every new command → `StingCommandHandler` case +
   `WorkflowEngine` known-tags/ResolveCommand + dock-panel button on the
   most fitting tab (TAGS for A-items, BIM for B2/B3/C3/E1, the
   validation cluster for B1/B4, ELECTRICAL panel for D1, HVAC panel
   for E2).
3. **CHANGELOG**: one `#### Completed (Phase 192x — …)` block per part,
   with caveats stated honestly (anything not Revit-smoke-tested gets
   the standard "verify in Revit before merge" caveat).
4. **No secrets**: connection files (`fohlio_connection.json`) are
   user-created at runtime; ship only a `.example` file and ensure the
   real filename is in `.gitignore` if `_BIM_COORD` examples are added
   to the repo.
5. **Priority order if time-boxed**: A1 → B1 → A2/A3 → B3 → C3 → B2 →
   C2 → C1 → B4 → D1 → E2 → E1 → E3 → A4 → D2 → E4.

---

## Appendix — what Phase 191 already shipped (do not redo)

- `Core/TagSchemeEngine.cs` — scheme POCOs, registry (corporate +
  `_BIM_COORD/tag_schemes.json` overlay, checksums, render stamp,
  ProjectInformation cache), renderer (token/projectInfo/literal
  segments, value maps, fallbacks, forbidden-target guard).
- `Data/STING_TAG_SCHEMES.json` — `iso19650-element` +
  `kut-temple-example` (both disabled).
- Pipeline hook in `TagPipelineHelper.RunFullPipeline` (after
  `WriteTag7All`) → covers all tagging commands + the IUpdater.
- `ASS_TAG_SCHEME_TXT` shared param (UUIDv5
  `2c8224df-92e0-567b-a9df-c8cd1e4402a3`) registered in ParamRegistry +
  PARAMETER_REGISTRY.json + MR_PARAMETERS.txt/csv.
- `SEQ_INCLUDE_LOC` per-building sequence grouping end-to-end
  (SeqAssigner overload, TagConfig flag/parse/reset/warning, both
  counter scans, five command call sites).
- `Tags/TagSchemeCommands.cs` — Render / Inspect / Audit commands,
  registered in `StingCommandHandler` (`TagScheme_Render` /
  `TagScheme_Inspect` / `TagScheme_Audit`).
- CHANGELOG Phase 191 entry.

**Known Phase 191 gaps you own**: compile verification; dock-panel
buttons; WorkflowEngine registration; document-close cache invalidation;
Token Confidence Audit; KUT example configs; scope-box LOC detection.
