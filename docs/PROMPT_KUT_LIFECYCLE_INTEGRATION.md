# PROMPT — KUT Lifecycle Integration (BOQ ⇄ Fohlio ⇄ SpecLink ⇄ Niagara)

> **Paste this whole file to a fresh Claude Code / agent session working in the
> `C:\Dev\STINGTOOLS` repo.** It is self-contained: context, the exact existing
> code to build on (with file references), the work to do in independently
> shippable phases, conventions, decisions to confirm, and acceptance criteria.

---

## 0. Your role & the one-paragraph goal

You are extending the **StingTools** Revit plugin (C# / .NET 8 / `net8.0-windows`,
Revit 2025/2026/2027) for the **KUT** project (Kampala Uganda Temple — Owner: The
Church; Lead Appointed Party: Symbion Consulting; Information Manager: Planscape /
Mayanja Davis). STING already links four systems to the model **but they never
meet**: the **BOQ/cost** engine, **Fohlio** (FF&E procurement), **CSI
MasterFormat / RIB SpecLink** (specifications) and **Niagara** (the Owner's BMS).
Your job is to **join all four on the Revit element** so KUT gets one
cost + spec + procurement + commissioning picture, where each system stays
authoritative for its own column. **Do not rebuild any sync — STING's value is
the join, the bill, and the gap reports, not re-implementing a vendor's plugin.**

The lifecycle spine (all four keys already live on the same element):

```
   SpecLink ──►   BOQ      ──►  Fohlio   ──►  Niagara
   specified     measured       procured      commissioned
   CSI_SECTION   NRM2 + rate    FOHLIO_REF    ICT_HEALTHIOT_DEVICE_ID
        └────────────── same Revit element ──────────────┘
```

---

## 1. Critical external facts (already researched — do not re-litigate)

- **Fohlio has an OFFICIAL Revit add-in.** It exports rooms/elements/quantities/
  parameters → Fohlio and imports spec data + product images + URLs back into
  Revit; it maps Revit families/params → Fohlio columns and uses **a chosen Revit
  parameter as the unique key (update-not-duplicate)**. That key should be set to
  STING's `ASS_TAG_1_TXT`. **Therefore STING must COEXIST, not duplicate**: let the
  official plugin (or STING's existing CSV round-trip as fallback) own Revit⇄Fohlio
  sync; STING only **consumes** the result (the link + the cost) and pulls FF&E cost
  into the BOQ. (Refs: fohlio.com Revit integration pages.)
- **RIB SpecLink / Deltek Specpoint** integrates via a Revit plugin + Word/PDF/CSV
  exports; there is **no clean public REST API**. STING's CSV-ToC reconcile is the
  correct lightweight lane — keep it. Do not attempt a live SpecLink API.
- **Niagara 4** exposes live point data via the **JSON Toolkit** (point name/status/
  present-value as JSON over HTTP GET) and **oBIX** (REST). Live read-back is an
  *optional* upgrade behind `TwinReadbackBase`; the shipped/default lane is CSV
  point-list export + station reconcile.
- **There is no published CSI→NRM2 crosswalk.** Build a small custom mapping (STING
  already computes both keys per element, so the join is local).

---

## 2. The existing code you build ON (read these first)

**BOQ / cost (just hardened in Phase 195 — reuse it, don't fight it):**
- `StingTools/BOQ/BoqTotals.cs` — single source of truth for summary maths (NRM
  cascade + VAT + contract sum). Pure, host-free, unit-tested.
- `StingTools/BOQ/BoqUnits.cs` — pure unit normalisation. Unit-tested.
- `StingTools/BOQ/Rates/IRateProvider.cs` — `RateRequest` / `RateLookup`
  (`RateLookup` now carries `RateIncludesOhp`). **You will add a provider here.**
- `StingTools/BOQ/Rates/RateProviders.cs` — concrete providers.
- `StingTools/BOQ/Rates/RateProviderRegistry.cs` — `Build(...)` registers providers
  in priority order: `param-override(100)` → `es-override(95)` →
  `material-library(95)` → `csv(90/85/80)` → `cobie(75)` → `default(60)`.
  Has a currency adapter (UGX/USD/GBP). **You register `FohlioRateProvider` here.**
- `StingTools/BOQ/BOQModels.cs` — `BOQLineItem` (fields incl. `QuantityMeasured`,
  `RateIncludesOhp`), `BOQDocument` (`OhpBaseWorksUGX`, `Totals()`, `GrandTotalUGX`,
  `VatUGX`, `ContractSumUGX`).
- `StingTools/BOQ/BOQCostManager.cs` — `BuildLineItemFromElement(...)` is the
  per-element pipeline (rate → quantity → NRM2 paragraph → carbon → line). It already
  computes the element's **NRM2 section** (`DeriveNrm2Section`). `ResolveRate(...)`
  drives the provider chain.
- `StingTools/BOQ/BOQExportCommand.cs` (basic XLSX) + `BOQProfessionalExportCommand.cs`
  (tender) — both totals route through `BoqTotals`. You add a **Spec ref** column.
- `StingTools/Data/cost_rates_5d.csv` — baseline rates (placeholders).
- Tests: `StingTools.Boq.Tests/` (xUnit). Pure helpers are linked in via
  `<Compile Include="..\StingTools\BOQ\X.cs" Link="X.cs"/>`. **Follow this pattern:
  extract any new pure maths into a host-free file and unit-test it.**

**Fohlio (FF&E):**
- `StingTools/ExLink/FohlioLink.cs` — `FohlioConnection.Load(doc)` reads
  `_BIM_COORD/fohlio_connection.json`; `FohlioRestTransport` is a **stub
  (`NotImplementedException`, "Tier 2, not yet wired")**.
- `StingTools/ExLink/FohlioCommands.cs` — `Fohlio_Export` / `Fohlio_Import` /
  `Fohlio_Audit`. Matching key `ASS_TAG_1_TXT`; writes back `ASS_MANUFACTURER_TXT`,
  `ASS_MODEL_REF_TXT`, `FOHLIO_REF_TXT`. **No cost field today.**
- `StingTools/ExLink/FohlioFinishesCommands.cs` — room-finish round-trip (BuiltIn
  room finish params, keyed on Room Number).
- `StingTools/Core/Storage/StingFohlioSnapshotSchema.cs` — ES schema
  GUID `E1A7B2C4-1011-1245-8411-F6E5D4C3B2CD`, fields `FohlioRef`, `SnapshotJson`,
  `CapturedUtcTicks`. **You extend this with cost.**
- `project-templates/KUT/_BIM_COORD/fohlio_map.json` — `{ Categories[], Columns[] }`.
  FF&E categories: Furniture, Furniture Systems, Casework, Plumbing Fixtures,
  Lighting Fixtures, Specialty Equipment.
- Param `FOHLIO_REF_TXT` GUID `0ecf2056-1239-52bc-87f8-17c281e67209`.

**CSI / SpecLink:**
- `StingTools/Commands/Classification/CsiCommands.cs` — `CSI_Assign`
  (writes `CSI_SECTION_TXT` + `CSI_TITLE_TXT`), `SpecLink_Reconcile` (XLSX diff:
  `SPEC_GAP` / `OVER_SPEC` / `TITLE_MISMATCH`).
- `StingTools/Core/Classification/CsiMasterFormat.cs` — rule resolve + section
  normalisation.
- `StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv` — columns
  `Category, FamilyRegex, TypeRegex, Sys, Section, Title`. Project overlay
  `_BIM_COORD/csi_map.csv`. **You add an `Nrm2` column.**
- Params `CSI_SECTION_TXT` (`3c2c7d9d-93e2-5f95-a002-69b17450efe6`),
  `CSI_TITLE_TXT` (`160a2335-1886-5503-b569-28d9e63f5a75`).
- Fixture `Tests/fixtures/kut/speclink_toc_sample.csv` (cols `Section,Title`).

**Niagara (BMS):**
- `StingTools/Commands/Twin/NiagaraCommands.cs` — `Niagara_ExportPoints`
  (`NiagaraPointListExportCommand`, CSV point list from `IoTDeviceRegistry`) and
  `Niagara_Reconcile` (`NiagaraReconcileCommand`, station CSV/XLSX vs model →
  `STATION_ONLY` / `MODEL_ONLY` / `MATCHED_COUNT`).
- `StingTools/Core/Twin/IoTDeviceRegistry.cs` — devices = elements carrying
  `ICT_HEALTHIOT_DEVICE_ID_TXT` (+ `_PROTOCOL_TXT` / `_ENDPOINT_TXT` /
  `_ALERT_BAND_TXT`). Live read-back parked behind `TwinReadbackBase`.

**KUT KPI + workflows:**
- `StingTools/Commands/Kpi/KutKpiDashboardCommand.cs` — `KutKpiSnapshot` already has
  `FfeTotal/FfeLinked/FfeStale`, `SpecTotal/SpecAssigned`, `BmsPoints/BmsNoEndpoint`.
  `KutKpiEngine.Gather(doc)` + `WriteHtml`. **You add the join metrics here.**
- `StingTools/Data/WORKFLOW_KUT_FFESync.json`, `_CoordinationCycle.json`,
  `_DeliverableD.json`, `_MonthlyReport.json`, `_Mobilisation.json`.
- KUT MIDP rows: Z-212 BOQ (prelim), Z-310 BOQ (tender), FF-600 FF&E schedule.

---

## 3. The work — five phases, each independently shippable

> Build phases in order. Each compiles, is verified, and delivers value alone.
> After each phase: build against Revit 2025 and (where pure logic was added) run
> the BOQ tests. Log each phase in `docs/CHANGELOG.md`.

### Phase A — Spec ref on every BOQ line + CSI↔NRM2 bridge (pure-internal, smallest)
1. Add an `Nrm2` column to `STING_CSI_MASTERFORMAT_MAP.csv` (+ the project overlay
   reader in `CsiMasterFormat.cs`) so one rule resolves both CSI **and** the NRM2
   section consistently. Author the ~40 KUT categories; leave blank = fall back to
   the BOQ's existing `DeriveNrm2Section`.
2. In `BOQCostManager.BuildLineItemFromElement`, read `CSI_SECTION_TXT` /
   `CSI_TITLE_TXT` off the element and stamp new `BOQLineItem.CsiSection` /
   `CsiTitle` fields (update `Clone()`).
3. Add a **"Spec ref"** column to both exports (`BOQExportCommand`,
   `BOQProfessionalExportCommand` item schedules).
4. **Acceptance:** a priced bill shows the CSI section per line; an element with a
   CSI rule gets a consistent NRM2 section.

### Phase B — Two cost↔spec gap rows in `SpecLink_Reconcile`
1. Extend `SpecLink_Reconcile` to also emit, using the BOQ document join:
   - `PRICED_UNSPECIFIED` — a BOQ line with cost (`TotalUGX > 0`) but **no** CSI
     section / not in the SpecLink ToC. Carry the value-at-risk (UGX).
   - `SPECIFIED_UNPRICED` — a CSI section in the SpecLink ToC with **zero** measured
     BOQ value.
2. **Acceptance:** XLSX report gains the two row types; the priced-unspecified UGX
   total is reported.

### Phase C — Fohlio cost → BOQ via a new rate provider (the cost integration)
1. Add params `FOHLIO_UNIT_COST_NR` (number) + `FOHLIO_CURRENCY_TXT` (text) to
   `MR_PARAMETERS.txt` + `.csv` (stable UUIDv5 GUIDs, namespace
   `a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a`, like `FOHLIO_REF_TXT`).
2. Extend `StingFohlioSnapshotSchema` with `UnitCost` (double), `Currency` (string),
   `QtyFromFohlio` (double), `LeadTimeDays` (int). Populate on `Fohlio_Import` (and
   document the official-plugin column mapping that writes `FOHLIO_UNIT_COST_NR`).
3. Add `FohlioRateProvider : IRateProvider` in `Rates/RateProviders.cs`:
   - Reads the element's `FOHLIO_UNIT_COST_NR` (+ `FOHLIO_CURRENCY_TXT`), else the
     ES snapshot `UnitCost`/`Currency`.
   - Returns `RateLookup { UnitRate, CurrencyCode, Confidence = 95,
     SourceId = "fohlio", RateIncludesOhp = false, Provenance = "Fohlio FF&E
     procurement" }`. The registry's currency adapter handles USD→UGX.
   - Register in `RateProviderRegistry.Build` at **priority 96** (above
     material-library/CSV, below the deliberate `param-override(100)`). Confirm this
     priority with the user (see §5).
   - Map `"fohlio"` → legacy source label `"Fohlio"` in
     `BOQCostManager.MapProviderIdToLegacySource`.
4. Add a per-category **FF&E treatment toggle** to `fohlio_map.json`:
   `"boqTreatment": "measured" | "pcSum" | "ownerSupplied-excluded"`. In
   `BuildLineItemFromElement`, `pcSum` → set `BOQRowSource.ProvisionalSum` with the
   Fohlio total as the PC value; `ownerSupplied-excluded` → skip the line.
5. **Acceptance:** FF&E rows priced from Fohlio carry `RateSource = "Fohlio"`,
   confidence 95, correct UGX after FX; `pcSum` items appear as provisional sums.

### Phase D — Niagara join: two commissioning-gap rows + the unified register
1. Add a cross-ledger reconcile (extend `Niagara_Reconcile` or add
   `KUT_LifecycleReconcile`): using the BOQ document + `IoTDeviceRegistry`, emit:
   - `PRICED_NO_BMS_POINT` — a priced MEP/equipment BOQ line whose element has **no**
     `ICT_HEALTHIOT_DEVICE_ID_TXT`/endpoint (handover gap). Scope to monitorable
     categories only (don't flag a door).
   - `COMMISSIONED_UNPRICED` — a Niagara/station point with **no** BOQ line
     (asset-register gap).
2. Emit a **unified KUT register** (XLSX): one row per asset, columns
   `Element | CSI section | NRM2 + UGX | Fohlio ref | BMS device/endpoint` with
   ✓/✗/⚠ per ledger.
3. **Acceptance:** the register lists every asset with its four-ledger status; the
   two new gap row types are produced.

### Phase E — One KUT dashboard + the lifecycle workflow
1. Extend `KutKpiSnapshot` with the **join** metrics no single ledger can produce:
   `FfePricedFromFohlioPct`, `PricedUnspecifiedValueUGX`, `PricedNoBmsPointCount`,
   `BoqVsFohlioVarianceUGX` (STING measured FF&E total vs Fohlio register total).
   Render them in `KutKpiEngine.WriteHtml` with RAG bands.
2. Add `StingTools/Data/WORKFLOW_KUT_LifecycleReconcile.json` chaining:
   `Fohlio_Import → CSI_Assign → BOQ_Refresh → SpecLink_Reconcile →
   Niagara_ExportPoints → Niagara_Reconcile → KUT_KpiDashboard`.
   Reference it from `WORKFLOW_KUT_MonthlyReport.json`.
3. **Acceptance:** one HTML board shows all four ledgers + the join metrics; the
   workflow runs end-to-end.

### Phase F (optional, behind a flag) — live read-back
- Fohlio REST Tier-2: implement `FohlioRestTransport` (`GET {BaseUrl}/ping` with
  `Authorization: Bearer {ApiKey}`; list/get items) for unattended monthly refresh.
- Niagara live: implement a `TwinReadbackBase` subclass using the **Niagara JSON
  Toolkit** (HTTP GET JSON of point status/present-value) or **oBIX**. Keep CSV as
  default; live read-back is opt-in.

---

## 4. Conventions (from CLAUDE.md — follow exactly)
- C#: PascalCase public, camelCase locals. State-changing commands
  `[Transaction(TransactionMode.Manual)]`; read-only `[Transaction(ReadOnly)]`.
  Use `TaskDialog` (not MessageBox), `StingLog.Info/Warn/Error` (no silent catch).
- Reuse helpers: `ParameterHelpers`, `OutputLocationHelper`, `BoqTotals`, the
  rate-provider chain. **Do not add a second markup/VAT path — everything goes
  through `BoqTotals`.**
- **Extract new pure maths into a host-free file** and link it into
  `StingTools.Boq.Tests` with xUnit tests (mirror `BoqTotals`/`BoqUnits`).
- New shared params: add to `MR_PARAMETERS.txt` **and** `.csv`, deterministic UUIDv5.
- Build to verify (Revit IS installed on this machine):
  `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:/Program Files/Autodesk/Revit 2025"`
  Tests: `dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`
- Branch off the current work; never commit to `main`. Log each phase in
  `docs/CHANGELOG.md`.

---

## 5. Decisions to confirm with the user BEFORE coding
1. **FF&E default treatment:** `measured` (priced in the contractor's bill) vs
   `pcSum` (Provisional/Prime-Cost sum referencing the Fohlio register). For a temple
   with heavy Owner-procured liturgical/specialty items, **PC-sum is the safer
   default**; confirm per-category.
2. **`FohlioRateProvider` priority:** 96 (proposed — Owner PO price beats
   material-library/CSV but loses to a QS's explicit inline override at 100). Confirm.
3. **Fohlio transport for KUT:** official Fohlio Revit plugin (recommended) vs
   STING's CSV round-trip (fallback). If the plugin is used, set its unique key to
   `ASS_TAG_1_TXT` and map a cost column to `FOHLIO_UNIT_COST_NR`.
4. **Currency:** Fohlio quotes USD; KUT bill is UGX. Reuse the Phase 195 currency
   adapter and stamp `ASS_CST_FX_DATE_DT` so FF&E FX is auditable at tender.
5. **Budget basis:** `BudgetVarianceUGX` against works total (excl VAT) or contract
   sum (incl VAT)?

---

## 6. Definition of done
- All five core phases build against Revit 2025 with **0 errors**; new pure logic has
  passing xUnit tests.
- A KUT element that is specified + measured + procured + monitored shows ✓ across all
  four ledgers in the unified register; broken chains are flagged.
- FF&E rows are priced from Fohlio (or carried as PC sums) with correct UGX/FX and
  `RateSource = "Fohlio"`.
- The bill shows a Spec ref per line; `SpecLink_Reconcile` reports priced-unspecified
  value; `KUT_KpiDashboard` shows the four-ledger join with RAG.
- `docs/CHANGELOG.md` logs each phase; no second markup/VAT path was introduced.

---

## 7. Hard guardrails
- **Do NOT** re-implement the Fohlio Revit plugin or a SpecLink API — coexist and
  consume.
- **Do NOT** double-count FF&E: an item is priced in the bill **or** a PC sum **or**
  Owner-supplied-excluded — never two of these.
- **Do NOT** bypass `BoqTotals` for any total, or invent a new confidence path —
  unmeasured/low-provenance rows must still surface via the Phase 195 mechanisms.
- Keep every external link **"link, never duplicate"**: the model references Fohlio/
  SpecLink/Niagara by id; those systems stay authoritative for their own data.
