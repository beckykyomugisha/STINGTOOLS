# Material Schedule Export — Design

**Date:** 2026-08-16
**Branch:** `claude/material-schedule-export-32a359`
**Status:** Approved, ready for implementation planning

---

## 1. Problem

StingTools cannot produce a corporate-standard material schedule — the document a
site or procurement team uses to answer *"what do I buy, in what unit, and how
much of it"*.

The BOQ measures **finished work** in **measured units** (m² of wall, m³ of
concrete). A material schedule lists **commodities** in **supplier units** (bags
of cement, No. of blocks, trips of sand). Nothing in the codebase converts
between them and presents the result.

`BOQExportCommand` ships a sheet named "Material Schedule"
([`BOQExportCommand.cs:362`](../../../StingTools/BOQ/BOQExportCommand.cs)), but it
is mislabelled: it filters BOQ rows whose unit happens to be m²/m³/kg and prints
them unchanged. No commodities, no supplier units, no stage sections, no summary.
Its existence is why this capability looks present when it is not.

## 2. Reference sample

`PATMAC MALL, Material Schedule.pdf` — 4 pages, 11 sections plus a summary,
UGX 347,914,455 grand total.

**Structure.** Columns are `Item | Description | Unit | Quantity | Rate |
Amount`. Sections are **construction stages**, not NRM2 work sections:
Tools & Equipment → Sub-structure → Ground–First Floor → First–Second Floor →
Second–Wall Plate → Roof → Doors & Windows → Finishes → Electrical → Mechanical
→ External Works. Each closes with a single `Labour` lump line and a
`Sub-Total carried to summary`. The summary re-lists the sections and adds 5%
contingency.

**Units are trade units, not SI:** `Bags`, `Trips (Sino Truck)`, `Bdle`,
`J/Cans`, `Rolls`, `Pcs`, `Btles`, `Bkts`.

**Three row classes no model can produce:** site tools (wheelbarrows, hoes,
jerrycans), per-stage labour lump sums, and provisional sums (electrical
UGX 30m, mechanical UGX 30m, external UGX 15m).

### 2.1 Defects in the sample — the corporate standard must make these impossible

| # | Defect | Evidence |
|---|---|---|
| D1 | Duplicated section letters | `C`, `D` and `E` each used twice in the body |
| D2 | Summary order ≠ body order | body runs …Second–Wall Plate, Roof…; summary runs …Roof, Doors & Windows… |
| D3 | Amount ≠ Quantity × Rate | `DPM (1000g polythene) — 1 Roll × 300,000 = 150,000` |
| D4 | Same commodity, two rates | Sand at `1,500,000`/trip (Sub-structure) and `1,400,000`/trip (Ground–First) |

These four are the acceptance fixtures for the reconciler (§8).

## 3. Research — how material schedules are presented professionally

Consensus across QS and MTO practice:

- **Supplier units, not measured units.** The schedule speaks the vendor's
  language — sheets not m², bags not m³.
- **Wastage as its own visible step**, never folded silently into the quantity.
  A buried allowance is one a QS cannot argue with.
- **Every quantity traceable** back to the measurement that produced it.
- Procurement-grade schedules add supplier and lead-time columns; cost-grade
  ones stop at rate and amount.

Sources: [Vantazo — MTO explained](https://www.vantazo.com/blog/material-take-off-mto-explained-definition-and-role-in-construction/) ·
[Ruh AI — how to do a material takeoff](https://www.ruh.ai/industrial/construction/guides/material-takeoff-guide) ·
[Mastt — quantity surveyor report best practice](https://www.mastt.com/blogs/quantity-surveyor-report) ·
[ProjectManager — material schedule for construction](https://www.projectmanager.com/blog/material-schedule-construction)

## 4. Decisions taken

| Question | Decision |
|---|---|
| Document type | One engine; **prices are a user toggle**, include or exclude |
| Quantity source | **Reuse the BOQ pipeline** — `BOQCostManager.BuildBOQDocument` + `CompoundTakeoff` constituents |
| Sectioning | **Construction stages, auto-lettered**; summary projected from the body |
| Non-model rows | **Reuse `BOQManualStore`** — tools as Manual rows, services as ProvisionalSum rows, labour from the `LabourUGX` rate split |
| Outputs | **Branded XLSX** + **Revit schedule views** (no DOCX, no CSV) |
| Module shape | **Revit-free engine + thin Revit adapter + renderers** |

### 4.1 Stated assumption — supplier-unit table

The chosen source (BOQ pipeline, no recipe file) emits SI units. The schedule
must print `Trips (Sino Truck)`. A conversion table is therefore unavoidable:

`Data/STING_SUPPLIER_UNITS.json` — **unit conversion only** (commodity →
supplier unit, conversion factor, rounding rule, default wastage), corporate
baseline with project override. It contains no measurement logic and is not a
recipe engine. Without it the chosen source cannot produce the sample's units.

## 5. Architecture

```
Revit Document
   ├─ BOQCostManager.BuildBOQDocument(doc)     [existing, unchanged]
   │     └─ CompoundTakeoff constituents (bag / m³ / kg / nr, wastage applied)
   └─ BOQManualStore                           [existing] tools, PS, labour splits
                    │
                    ▼
   MaterialScheduleBuilder            Revit-side adapter, thin
        gathers BOQDocument · Level list · manual store · ProjectInformation
                    │  plain POCO inputs — no Autodesk.Revit.* beyond this line
                    ▼
   ┌─ Core/MaterialSchedule/   Revit-free, unit-tested ───────────┐
   │  StageMapper            constituent → stage; auto-lettering   │
   │  CommodityAggregator    merge by commodity key; wastage       │
   │  SupplierUnitConverter  m³ → Trips; ltr → Buckets; round up   │
   │  Reconciler             four arithmetic invariants            │
   └───────────────────────────────────────────────────────────────┘
                    │ MaterialScheduleDocument
          ┌─────────┴──────────┐
   XlsxWriter             ViewBuilder
   (ShowPrices flag)      (key schedules)
```

The engine is a pure function: same inputs → same document. No Revit API, no
file I/O, no `TaskDialog`. Gathering happens above the box, rendering below.

### 5.1 Files

| Path | Purpose | Target size |
|---|---|---|
| `Core/MaterialSchedule/MaterialScheduleModel.cs` | POCOs — commodity, stage, document, options, reconciliation | ~250 |
| `Core/MaterialSchedule/StageMapper.cs` | constituent kind + level → stage; sequential lettering | ~250 |
| `Core/MaterialSchedule/CommodityAggregator.cs` | merge constituents by commodity key; apply wastage | ~300 |
| `Core/MaterialSchedule/SupplierUnitConverter.cs` | SI → supplier unit; rounding rules; table loader | ~250 |
| `Core/MaterialSchedule/Reconciler.cs` | the four invariants | ~200 |
| `BOQ/MaterialSchedule/MaterialScheduleBuilder.cs` | Revit-side gather + engine call | ~300 |
| `BOQ/MaterialSchedule/MaterialScheduleXlsxWriter.cs` | ClosedXML renderer | ~350 |
| `BOQ/MaterialSchedule/MaterialScheduleViewBuilder.cs` | Revit key schedules | ~300 |
| `Commands/MaterialSchedule/MaterialScheduleCommands.cs` | two `IExternalCommand`s | ~200 |
| `Data/STING_MATERIAL_STAGES.json` | stage list, preambles, kind→stage map | — |
| `Data/STING_SUPPLIER_UNITS.json` | commodity → supplier unit conversion | — |

Every file stays under ~400 lines. No file in this feature is added to
`StingCommandHandler`; commands register through a `CommandRegistry` module,
following [`TempCommandModule.cs`](../../../StingTools/UI/Modules/TempCommandModule.cs).

## 6. Data model

```csharp
public sealed class MaterialCommodity {
    public string CommodityKey;     // canonical merge key — "cement.opc.42.5n"
    public string Description;      // "Cement (OPC 42.5N)"
    public string Spec;             // "T16" · "8\" hollow" · "1000g"
    public string SupplierUnit;     // "Bags" · "Trips (Sino Truck)" · "No." · "Rolls"
    public double NetQuantity;      // supplier units, pre-wastage
    public double WastagePct;       // visible column, never folded into quantity
    public double OrderQuantity;    // ceil for countable units
    public double RateUGX;
    public double AmountUGX;        // OrderQuantity × RateUGX
    public string RateSource;       // carried from the BOQ rate provider
    public List<string> TraceRefs;  // BOQ line refs feeding this commodity
}

public sealed class StageSection {
    public string StageId;          // "substructure"
    public string Letter;           // assigned at build time — never authored
    public string Title;            // "ELEMENT 01: SUB-STRUCTURE"
    public string Preamble;         // scope note printed under the heading
    public List<MaterialCommodity> Commodities;
    public List<LabourLine> Labour;
    public List<ProvisionalSumLine> ProvisionalSums;
    public double SubTotalUGX;
}

public sealed class LabourLine {
    public string Description;      // "Labour"
    public double AmountUGX;        // Σ LabourUGX rate split, or QS override
    public bool IsManualOverride;   // true when no rate split existed
}

public sealed class ProvisionalSumLine {
    public string Description;      // "Allow a provisional sum for electrical installations"
    public double AmountUGX;
    public string SourceRef;        // BOQManualStore row id
}

public sealed class MaterialScheduleDocument {
    public string ProjectName;
    public string ProjectCode;
    public DateTime GeneratedUtc;
    public string Currency = "UGX";
    public List<StageSection> Stages;
    public MaterialScheduleOptions Options;
    public MaterialScheduleReconciliation Reconciliation;

    public double WorksSubtotalUGX;     // Σ stage sub-totals
    public double ContingencyUGX;       // WorksSubtotal × Options.ContingencyPct
    public double GrandTotalUGX;        // WorksSubtotal + Contingency
}

public sealed class MaterialScheduleOptions {
    public bool ShowPrices = true;
    public double ContingencyPct = 5.0;
}
```

`MaterialScheduleDocument` carries its own contingency and grand total rather
than reusing `BOQDocument`'s markup waterfall: the sample applies a flat 5%
contingency to the works subtotal with no prelims, OH&P or VAT, which is a
different and simpler formula from `BoqTotals.Compute`. Conflating them would
make the schedule's grand total disagree with the sample it is replacing.

`OrderQuantity` rounds **up to whole units** for countable commodities — you
cannot buy 349.3 bags. The rounding rule is per-unit, driven by
`STING_SUPPLIER_UNITS.json`, and pinned by tests.

## 7. Stage mapping and summary integrity

Default stage order (`STING_MATERIAL_STAGES.json`, baseline + project override
at `_BIM_COORD/material_stages.json` via `StingPaths.MetaFile`):

`tools` → `substructure` → per-storey (generated from Revit levels in elevation
order) → `roof` → `doors-windows` → `finishes` → `electrical` → `mechanical` →
`external`.

**The routing key is the constituent kind, not the element.** A wall on Level 1
sends its `blockwork` and `mortar*` constituents to the `GF–L1` storey stage, and
its `plaster*` constituents to `finishes`. This is what the sample does, and why
its Finishes section carries its own 600 bags of cement. `CompoundLine.Kind`
([`CompoundTakeoff.cs:24`](../../../StingTools/BOQ/Takeoff/CompoundTakeoff.cs))
already provides this discrimination.

Section letters are assigned sequentially at build time. The summary is
projected from the same collection the body renders:

```csharp
var summary = doc.Stages.Select(s => (s.Letter, s.Title, s.SubTotalUGX));
```

Defects **D1** and **D2** become structurally unrepresentable rather than merely
corrected.

## 8. Reconciliation

The engine emits a `MaterialScheduleReconciliation` enforcing four invariants:

| Invariant | Defect caught |
|---|---|
| `Amount == round(OrderQty × Rate)` per commodity | D3 |
| `SectionSubTotal == Σ commodities + labour + PS` | drifted sub-totals |
| `Σ SectionSubTotals == works subtotal` | D2 |
| one rate per `CommodityKey` document-wide | D4 |

Failures render to a `Validation` sheet and gate the export behind a skippable
`TaskDialog`, matching the existing BOQ paragraph-coverage gate
([`BOQExportCommand.cs:59`](../../../StingTools/BOQ/BOQExportCommand.cs)).
Every catch around the maths logs via `StingLog` — no silent swallowing.

## 9. Outputs

### 9.1 XLSX

ClosedXML, reusing `BOQExportCommand`'s banner and header helpers for visual
consistency. Sheets: `Material Schedule` (one continuous sheet with stage
bands), `Summary`, `Validation`. Written to
`StingPaths.ExportFile(doc, "MaterialSchedule", …)`.

`ShowPrices = false` drops the Rate, Amount, contingency and grand-total
columns, and turns `Summary` into a pure commodity roll-up (commodity → total
order quantity across all stages). It is a **renderer flag only** — the engine
computes identically either way, so the two documents can never disagree.

### 9.2 Revit views — accepted tradeoff

Revit schedules schedule *elements*; they cannot hold derived commodity rows.
`MaterialScheduleViewBuilder` therefore creates a **key schedule per stage**,
acting as a data table populated from the computed document and regenerated
idempotently on re-run.

**This is a snapshot, not a live schedule.** It places on a sheet and issues
with the drawing set, but does not update when the model changes until the
command is re-run. The schedule name carries the generation timestamp so a stale
table is visibly stale. This tradeoff was reviewed and accepted; the alternative
(native Material Takeoff schedules filtered per stage) stays live but shows m³
of concrete rather than bags of cement, which defeats the purpose.

## 10. Commands

| Tag | Class | Transaction | Behaviour |
|---|---|---|---|
| `MaterialSchedule_Export` | `MaterialScheduleExportCommand` | Manual | Build → reconcile → gate → write XLSX → open |
| `MaterialSchedule_CreateViews` | `MaterialScheduleViewsCommand` | Manual | Build → reconcile → create/refresh key schedules |

Both registered via a `CommandRegistry` module, with buttons placed alongside
the existing BOQ export buttons.

## 11. Testing

New test files attach to `StingTools.Boq.Tests` by `<Compile Include>` — the
pattern that project already uses for `CompoundTakeoff.cs`. No new test project,
no Revit stub layer.

| Test file | Covers |
|---|---|
| `CommodityAggregatorTests` | merge by key, wastage application, order-qty rounding |
| `SupplierUnitConverterTests` | m³→trips, ceiling behaviour, unknown-unit passthrough |
| `StageMapperTests` | lettering never duplicates, kind routing, summary ≡ body |
| `ReconcilerTests` | **D1–D4 as named fixtures using the PATMAC numbers** |

The sample PDF's defects become the regression suite.

## 12. Out of scope

Explicitly not in this pass:

1. **The mislabelled sheet in `BOQExportCommand`** stays as-is. Renaming it is a
   separate, safe cleanup, deliberately not bundled with a new feature.
2. **DOCX and CSV outputs.**
3. **Commodities `CompoundTakeoff` does not emit today** — roofing sheets, paint
   litres, tile boxes, nails/kg. These come through as BOQ rows in measured
   units until the supplier-unit table covers them.

Consequence of (3): **the first export will not fully reproduce the sample's
Roof and Finishes sections.** Those sections will be present and priced, but
some rows will read in measured units rather than trade units until the unit
table is extended. This is a known, accepted limitation of the chosen source,
not a defect to be discovered at delivery.
