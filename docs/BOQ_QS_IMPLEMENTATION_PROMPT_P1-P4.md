# Implementation Brief — BOQ & Cost Manager, Quantity-Surveyor Upgrade (P1–P4)

> **For the implementing agent.** This is a self-contained spec. Read it fully
> before touching code. P0 (reviving the dead Cost buttons) is already done and
> merged on branch `claude/revive-cost-buttons`. This brief covers P1–P4.

---

## 0. Mission & context

STINGTOOLS' **BOQ & Cost Manager** (a WPF dockable panel inside the BIM
Coordination Center) produces a bill of quantities and runs cost-management
workflows (cost plans, payment certificates, variations, EVM) from a live Revit
model. The goal of P1–P4 is to turn it from a *per-element dump* into a
**real, QS-grade cost platform** that a professional Quantity Surveyor can work
**with** — not one that tries to replace them.

**Guiding principle:** the model owns *quantities*; the QS owns *rates and
adjustments*. The model will never measure everything (excavation, formwork,
scaffolding, prelims, dayworks), so the BOQ must be a **hybrid**: modelled
quantities + manual QS-measured rows + provisional/PC sums + dayworks, with
clear provenance and a clean Excel round-trip to whatever tool the QS uses
(CostX, Cubicost, Candy/CCS, Buildsoft, or plain Excel).

**Domain context:** Uganda-based practice (Planscape). Dual currency UGX/USD
already supported. Measurement standards: **NRM2** primary (also SMM7/CESMM/ICMS
scaffolding exists). Respect ISO 19650 naming already used across the codebase.

### Revit-API reality the implementation must respect
- Revit gives **net geometric** volumes/areas via `BuiltInParameter`
  (`HOST_VOLUME_COMPUTED`, `HOST_AREA_COMPUTED`). These are **not** measured
  quantities — NRM2/SMM7 have deduction thresholds, girth rules, laps, waste,
  contact-area formwork, rebar-by-weight, etc. Treat model quantity as a
  *starting point* with per-work-section adjustment/waste factors (a
  `WasteFactor` helper already exists).
- **2D content is not measurable.** Detail items, filled regions, model/detail
  lines, text, dimensions, and **imported CAD (`ImportInstance`)** must be
  excluded from takeoff. They currently leak in (see P1).
- Quantities are only as good as model **LOD**. Surface low-confidence rows
  rather than hiding the limitation (`RateConfidence` already exists).

---

## 1. Verified architecture (current state — do not re-discover, but do verify before editing)

| Concern | File | Key symbols |
|---|---|---|
| Data model | `StingTools/BOQ/BOQModels.cs` | `BOQLineItem` (fields: `NRM2Section, Category, Discipline, ItemName, FamilyName, TypeName, Quantity, Unit, RateUGX, RateUSD, EmbodiedCarbonKg, LifecycleCostUGX, BOQLineRef, Note, Source(enum BOQRowSource), RevitElementId, UniqueId, Level, Location, RateSource, RateConfidence, SortOrder`), `BOQSection`, `BOQDocument` |
| Build pipeline | `StingTools/BOQ/BOQCostManager.cs` | `BuildBOQDocument(doc)` → `CollectCandidateElements(doc, knownCats)` (line 1758) → `BuildLineItemFromElement(doc, el, …)` (186) → `GroupIntoSections(items)` (1794, groups by **NRM2§ + Discipline only**) → `AssignBoqLineRefs(boq)` (1856). `DeriveQuantity(el, unit)` (349), `GetLevelName`, `GetLocationName` |
| Panel UI (pure C#, no XAML) | `StingTools/UI/BOQCostManagerPanel.cs` | `Build()`, `BuildItemGrid()` (columns ~1017–1043: Ref, Item, Qty, Unit, Rate, Total, Src, Conf, CO₂, Note), `BuildSectionCard()` (876), filter pills/search (383–456), `DispatchAction(tag)` (1981), `RefreshAsync` |
| Dispatch | `StingTools/UI/StingDockPanel.xaml.cs` `DispatchCommand` → `StingTools/UI/StingCommandHandler.cs` `RunCommand<T>` (passes **null** ExternalCommandData → commands must use `ParameterHelpers.GetDoc(commandData)` / `GetApp(commandData)`) |
| Cost commands | `StingTools/Commands/Cost/*.cs` | CostCommands, CostPlanCommands, PaymentCertCommands, VariationAndEvmCommands, MeasurementStandardCommands, IfcAndIcmsCommands |
| Cost engines | `StingTools/Core/CostPlan/`, `Core/Evm/`, `Core/PaymentCert/`, `Core/Variation/` | `CostPlanEngine/Registry`, `EvmCalculator`, `PaymentCertEngine`, `VariationEngine` |
| Export | `StingTools/BOQ/BOQExportCommand.cs` (21-col XLSX, AutoFilter), `BOQProfessionalExportCommand.cs` (QS tender doc), `BOQModels`/`Rates/`/`Takeoff/TakeoffRule.cs` |
| ES schemas | `StingTools/Core/Storage/` (`StingCostPlanSchema`, `StingPaymentCertSchema`, `StingVariationSchema`, `StingCostRateOverrideSchema`) |

**Mandatory conventions (read `CLAUDE.md`):**
- Doc acquisition in every command: `var doc = ParameterHelpers.GetDoc(commandData);` (P0 standard).
- `[Transaction(TransactionMode.Manual)]` for mutating, `ReadOnly` for diagnostics; wrap DB writes in named `Transaction` ("STING …").
- Use `StingLog.Info/Warn/Error` — no silent catches. `TaskDialog` not `MessageBox`.
- Reuse UI building blocks: `StingListPicker`, `StingResultPanel`, `StingDataGridDialog`, `StingProgressDialog`, `StingExportDialog`.
- Data-driven: new behaviour goes in JSON/CSV under `StingTools/Data/` with a project override under `<project>/_BIM_COORD/`. Corporate baseline stays pristine; project edits flip origin (mirror existing registry patterns).
- **Verify with `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"` — must be 0 errors before each commit.**
- **Do not regress existing saved snapshots** (`deliverables`/snapshot JSON) — additive model fields only, with safe defaults so old JSON still deserialises.
- **Branch hygiene:** one branch per phase off latest `main` (e.g. `claude/boq-p1-aggregation`). P2 depends on P1, P3 on P1+P2, P4 on P0. Commit logically; do not push or open PRs unless asked. End commit messages with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.

---

## P1 — Real takeoff: exclude 2D noise + aggregate similar items

**Problem.** `CollectCandidateElements` sweeps every element whose category is in
`DiscMap` ∩ `AllCategoryEnums`, so imported-CAD detail and annotation leak in
(e.g. *"Small Power legend.dwg-2 — Detail Filled Region"* appears 275× as
`Qty 1.000 each`). And **nothing is aggregated** — each element is its own row,
so 7 identical shower units = 7 rows. A real BOQ shows **Item / Unit / Quantity**
with one line per distinct item.

### P1.1 — Takeoff exclusion filter
- In `CollectCandidateElements`, after the category checks, exclude
  non-measurable categories. At minimum reject: `OST_DetailComponents`,
  `OST_FilledRegion`, `OST_Lines`/detail & model lines, `OST_GenericAnnotation`,
  `OST_TextNotes`, `OST_Dimensions`, `OST_RvtLinks`, and any `ImportInstance`
  (CAD imports). Also skip elements with no derivable quantity/geometry.
- Make the exclusion list **data-driven**: config key
  `COST_TAKEOFF_EXCLUDE_CATEGORIES` (CSV of category names/BuiltInCategory),
  with a sensible hard-coded default so it works out of the box.
- Keep the existing Rooms/Spaces/Areas skip and `IsPhaseDemolished` logic.
- Log a one-line summary of how many elements were excluded and why.

### P1.2 — Aggregation layer (the headline change)
- Extend `BOQLineItem` (additive, defaulted) with:
  - `int SimilarCount = 1;` — number of constituent elements collapsed into the row.
  - `List<long> ConstituentElementIds` (or `string` CSV) — all element ids in the group, for back-selection/drill-down. Keep `RevitElementId`/`UniqueId` as the representative element.
  - `string AggregationKey` — the grouping key used (for debugging/export).
- Add an `AggregateLineItems(...)` step in `BuildBOQDocument` **after**
  per-element line items are built and **before** `GroupIntoSections`. Group by a
  configurable key: default `NRM2Section + Category + FamilyName + TypeName + Unit
  + spec-defining params` (and, when a spatial grouping mode is active per P2,
  include `Level`/`Zone`/`Location`). For each group:
  - `Quantity` = Σ constituent quantities; `SimilarCount` = count;
    `ConstituentElementIds` = all ids; `EmbodiedCarbonKg` = Σ; `Total` = Qty×Rate.
  - Rate is per-unit → keep the representative rate; if constituents disagree on
    rate, surface a warning and take the modal/most-confident. `RateConfidence` =
    min across the group. `Source` = most-significant (Manual/PS beat Model).
  - Preserve `Level`/`Location` only when uniform across the group; otherwise set
    to a "(various)" sentinel (still filterable).
- **Aggregation must be reversible/inspectable**: the panel needs a drill-down
  (P2) to list constituent elements and select them in Revit.
- Make aggregation **toggleable** (config `COST_AGGREGATE_SIMILAR`, default on) so
  power users can fall back to per-element rows.

### P1 acceptance
- The 275 filled-region rows disappear (excluded).
- 7 identical showers become **1 row, Qty 7, each**.
- `BOQLineRef` numbering still sequential per section; element-level provenance
  (constituent ids) retained and selectable.
- Existing snapshots still load; `dotnet build` clean.

---

## P2 — Location column, spatial grouping, and print/column profiles

`BOQLineItem` already carries `Level` and `Location` (populated per element,
exported as columns, searchable) — this phase is mostly presentation + grouping.

### P2.1 — Location/Level column in the grid
- In `BOQCostManagerPanel.BuildItemGrid()` add a **Location** column (and
  optionally **Level**), bound to the corresponding `BOQItemViewModel` fields.
- Add a **column-visibility toggle** in the toolbar (show/hide Location, Level,
  CO₂, Conf, Src, Note) — a lightweight checkable menu or pill row.

### P2.2 — Grouping modes
- Add a **grouping selector** (toolbar dropdown): `By Work Section (NRM2)`
  (current default), `By Level`, `By Zone`, `By Level → NRM2`, `By Location`.
- Refactor `GroupIntoSections` to accept a grouping strategy (don't hard-code the
  `(NRM2, Discipline)` tuple). NRM2 supports both **elemental** and **locational**
  bills — this delivers the locational bill.
- Grouping mode should feed the P1 aggregation key (e.g. *By Level* means similar
  items aggregate **within** a level, not across levels).

### P2.3 — Print / export column profiles
- Add a **print/export profile** concept: named column sets (e.g. *Tender*
  = Ref/Item/Unit/Qty/Rate/Amount, no Location/Src/Conf; *Internal* = all
  columns incl. Location/Conf/CO₂). Store profiles in
  `<project>/_BIM_COORD/boq_print_profiles.json` with corporate defaults in
  `StingTools/Data/`.
- Wire the profile into both the on-screen grid and the exports
  (`BOQExportCommand` / `BOQProfessionalExportCommand`) so "remove Location when
  printing" is one click. Keep Excel AutoFilter.

### P2 acceptance
- User can toggle a Location column on/off, regroup the bill by Level/Zone, and
  export a tender bill that omits internal columns — all without losing data.

---

## P3 — QS round-trip + hybrid bill (the integration surface)

A QS lives in Excel/their estimating tool, not in Revit. Make the exchange clean.

### P3.1 — First-class manual / PS / daywork rows
- Promote the "Manual row" path to a proper workflow. `BOQRowSource` already has
  Model/Manual/ProvisionalSum — ensure rows of each source can be **created,
  edited, and persisted** in the panel and survive a model re-build (keyed by a
  stable id, not `RevitElementId`). Add **Dayworks** and **PC Sum** as sources
  if not present.
- Manual/PS/daywork rows must **never be overwritten** by a model re-takeoff.

### P3.2 — QS export (priced & unpriced) in trade order
- Export the bill to XLSX in **NRM2 trade order** with the classic QS layout
  (Ref / Description / Unit / Qty / Rate / Amount), section collections, and a
  grand summary (prelims / contingency / OH&P already in `GRAND TOTAL`). Two
  modes: **unpriced** (blank Rate/Amount for the QS to price) and **priced**
  (current rates). Reuse/extend `BOQProfessionalExportCommand`.
- Each row carries a stable hidden key (`BOQLineRef` + `UniqueId`/aggregation key)
  so re-import can match.

### P3.3 — QS import (rates + rows back) with diff
- Re-import the QS's priced XLSX: match rows by the hidden key, **update
  `RateUGX`/`RateUSD` and `RateSource="QS"`**, and import any **QS-added rows**
  (manual measured items the model can't carry) as Manual rows.
- Show a **diff preview** (`StingDataGridDialog`): rate changes, new rows,
  unmatched rows, quantity drift (model vs QS-measured). Let the user accept/reject.
- Quantities remain **model-owned** by default; flag any row where the QS changed
  the quantity for review rather than silently overwriting.

### P3.4 — Rate library
- Support a project **rate library** (CSV/JSON) the QS maintains, layered through
  the existing `RateProviderRegistry` pipeline (BCIS → project rate card →
  material library → manual override). Add a "QS rate card" provider sourced from
  the imported XLSX / `_BIM_COORD/boq_rate_card.json`.

### P3 acceptance
- Round-trip: export unpriced → (QS prices in Excel) → import → rates land on the
  right rows, QS-added rows appear, model quantities preserved, diff shown.
- Manual/PS/daywork rows survive a model rebuild.

---

## P4 — Make valuations, variations & EVM usable end-to-end

P0 revived the buttons; the engines exist but need to be wired into complete,
correct workflows. Investigate the engines (`PaymentCertEngine`,
`VariationEngine`, `EvmCalculator`, `CostPlanEngine`) before extending — confirm
their current capability, then close the gaps below.

### P4.1 — Interim valuations / payment certificates (JCT/NEC/FIDIC)
- A valuation needs, per BOQ item or per section: **% complete (or
  measured-to-date qty)**, **previously certified**, **this period**, **materials
  on site (MOS)**, **retention** (% with limit/release), **VAT**, **net due this
  certificate**. Support a `% complete` input per row (or import from progress).
- Generate a numbered interim certificate (use the template engine / `MiniWord`
  if available, else XLSX) and persist a cert record (`StingPaymentCertSchema`),
  so the next valuation knows "previously certified".
- `PayCert_Approve`/`Sign` should advance status (Draft → Issued → Approved).

### P4.2 — Variations / change orders
- Variation workflow: instruction ref + status (**Anticipated → Instructed →
  Quoted → Agreed**), **omission + addition** lines, effect on contract sum and
  (optionally) completion date. `Variation_FromDiff` (snapshot diff) already
  scaffolds auto-mint — finish it.
- **Star-rate build-up** (first principles: labour + plant + material + OH&P) for
  items with no BOQ rate — `Star Rate Build-Up` button. Persist build-ups.
- Maintain a **VO register** (already a button) showing cumulative variation value
  and revised contract sum.

### P4.3 — EVM end-to-end
- `Calculate EVM` should compute **PV/BCWS, EV/BCWP, AC/ACWP, SPI, CPI, EAC, ETC,
  VAC, TCPI** from: a **baseline** (cost plan or a saved BOQ snapshot mapped to a
  programme) + **actuals** (`Import Actuals` CSV) + **% complete**. `Export S-Curve`
  emits the cumulative PV/EV/AC curve (XLSX/HTML).
- Persist EVM snapshots; link periods so trends work.

### P4.4 — Cost reporting & final account
- A **cost report / anticipated final cost** view: contract sum + agreed
  variations + pending variations + provisional-sum reconciliation + dayworks →
  anticipated final cost vs budget. Reuse the `GRAND TOTAL` machinery.
- **Final account reconciliation**: provisional/PC sums reconciled against
  outturn (a `BOQReconcileProvisionals` path already exists — extend).

### P4 acceptance
- A user can: set % complete, issue interim cert N (with retention/MOS/previous),
  raise a variation with omission/addition + star rate, recompute the revised
  contract sum, run EVM with imported actuals, and export an S-curve and a cost
  report — all from the panel, end to end.

---

## Cross-cutting deliverables & definition of done (every phase)
1. `dotnet build` clean (0 errors) against Revit 2025 before each commit.
2. New data files have corporate baseline + project-override loaders, mirroring
   existing registry patterns; corporate baseline untouched by project edits.
3. Additive model changes only — old snapshots/ES entities still deserialise.
4. A short **Revit manual-test checklist** per phase in the PR/commit body (the
   agent can't click buttons in CI; the human verifies in Revit).
5. Update `docs/CHANGELOG.md` with a `#### Completed (Phase N — …)` block;
   add any new gaps to `docs/ROADMAP.md`. Keep `CLAUDE.md` updated only where
   structure/commands change.
6. Honest caveats: if a workflow is wired but not Revit-verified, say so.

## Suggested sequencing
`P1` (data correctness — biggest felt impact) → `P2` (presentation/grouping,
depends on P1's aggregation key) → `P3` (QS round-trip, depends on P1+P2 row
identity) → `P4` (cost control, depends only on P0). Ship each on its own branch;
get human sign-off in Revit between phases.
