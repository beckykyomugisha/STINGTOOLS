# Material Schedule Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export a corporate-standard material schedule from Revit — construction-stage sections of buyable commodities in supplier units, with an include/exclude prices toggle, as a branded XLSX and (separately) as Revit schedule views.

**Architecture:** A Revit-free engine under `StingTools/Core/MaterialSchedule/` turns BOQ constituent lines into stage-sectioned commodities; a thin Revit-side adapter gathers its inputs; two renderers consume the result. All arithmetic lives in the engine so it is unit-tested headlessly, following the existing `CompoundTakeoff` / `CompoundTakeoffBuilder` split.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), Revit 2025 API, ClosedXML 0.104.2, Newtonsoft.Json 13.0.3, xUnit 2.6.2.

**Spec:** [`docs/superpowers/specs/2026-08-16-material-schedule-export-design.md`](../specs/2026-08-16-material-schedule-export-design.md)

---

## Before you start

**Read the spec first**, especially §4.3 (the eight code-verification findings). Six assumptions in the first draft were wrong; every one of them changes a task below.

**Baseline:** `StingTools.Boq.Tests` currently passes **196 tests**. Verify before touching anything:

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo -v q
```

Expected: `Passed! - Failed: 0, Passed: 196`.

**Two conventions this codebase enforces:**
- Never use a silent `catch {}` around a mutation. Log via `StingLog.Warn` / `StingLog.Error`.
- Never build a project path by hand. Resolve through `StingTools.Core.StingPaths`.

**Nullable:** `StingTools.csproj` has `<Nullable>disable</Nullable>`; `StingTools.Boq.Tests.csproj` has it **enabled**. Engine files are compiled into both, so initialise every reference-type field (`= ""`, `= new List<T>()`) or the test build emits CS8618.

---

## File Structure

### New — Revit-free engine (compiled into both plugin and tests)

| File | Responsibility |
|---|---|
| `StingTools/Core/MaterialSchedule/MaterialScheduleModel.cs` | POCOs only: commodity, stage, labour, PS, document, options, reconciliation |
| `StingTools/Core/MaterialSchedule/SupplierUnitConverter.cs` | Rule table + SI→supplier-unit conversion, wastage, rounding |
| `StingTools/Core/MaterialSchedule/CommodityRateResolver.cs` | Commodity key → UGX rate; project override beats baseline |
| `StingTools/Core/MaterialSchedule/StageMapper.cs` | Constituent kind/category/level → stage; sequential lettering |
| `StingTools/Core/MaterialSchedule/CommodityAggregator.cs` | Group constituents into stage sections of commodities |
| `StingTools/Core/MaterialSchedule/Reconciler.cs` | Post-build invariants |

### New — Revit-side

| File | Responsibility |
|---|---|
| `StingTools/BOQ/MaterialSchedule/MaterialScheduleBuilder.cs` | Gather BOQ + levels + manual store → engine inputs → document |
| `StingTools/BOQ/MaterialSchedule/MaterialScheduleXlsxWriter.cs` | ClosedXML renderer honouring `ShowPrices` |
| `StingTools/BOQ/MaterialSchedule/MaterialScheduleViewBuilder.cs` | Revit key schedules (Phase 4, isolated) |
| `StingTools/BOQ/BoqXlsxStyle.cs` | Shared XLSX banner/header/style helpers extracted from `BOQExportCommand` |
| `StingTools/Commands/MaterialSchedule/MaterialScheduleCommands.cs` | Two `IExternalCommand`s |
| `StingTools/UI/Modules/MaterialScheduleCommandModule.cs` | `ICommandModule` registration |

### New — data

| File | Responsibility |
|---|---|
| `StingTools/Data/STING_SUPPLIER_UNITS.json` | Commodity rules: match kinds, supplier unit, conversion, rounding, wastage |
| `StingTools/Data/STING_MATERIAL_STAGES.json` | Stage list, titles, preambles, kind/category routing |
| `StingTools/Data/STING_COMMODITY_RATES.csv` | Commodity key → supplier unit → UGX rate |

### Modified — all additive

| File | Change |
|---|---|
| `StingTools/BOQ/BOQModels.cs` | Add `ConstituentKind` to `BOQLineItem` + `Clone()` |
| `StingTools/BOQ/Takeoff/CompoundTakeoffBuilder.cs` | Set `ConstituentKind = c.Kind` |
| `StingTools/Core/ProjectFolderEngine.cs` | Add `["MaterialSchedule"] = "SCHEDULES"` |
| `StingTools/BOQ/BOQExportCommand.cs` | Delegate style helpers to `BoqXlsxStyle` |
| `StingTools/UI/CommandRegistry.cs` | Yield the new module |
| `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj` | `<Compile Include>` the six engine files; copy the three data files |

### One deliberate strengthening of the spec

The spec's §8 lists `Amount == round(OrderQty × Rate)` (defect **D3**) as a *detected* invariant. This plan makes `AmountUGX` a **derived property**, so D3 becomes impossible to represent rather than caught after the fact. `StageSection.SubTotalUGX` and `MaterialScheduleDocument.WorksSubtotalUGX` are derived for the same reason. The `Reconciler` then covers only what cannot be made structural: one rate per commodity (D4), letter integrity (D1/D2), unpriced commodities, and negative wastage. This is a strict improvement; it is called out because it differs from the spec text.

---

## Phase 0 — Enablers

These unblock everything else and are independently shippable.

### Task 1: Carry the constituent kind onto `BOQLineItem` (C2)

**Files:**
- Modify: `StingTools/BOQ/BOQModels.cs`
- Modify: `StingTools/BOQ/Takeoff/CompoundTakeoffBuilder.cs:275`

Today the constituent kind survives only as a string prefix in `Note` (`"[Compound: mortar_cement]"`). Stage routing needs it as a field.

- [x] **Step 1: Add the field to `BOQLineItem`**

In `StingTools/BOQ/BOQModels.cs`, immediately after the `public string Unit;` declaration (around line 129), add:

```csharp
        /// <summary>
        /// MAT-SCHED — the CompoundTakeoff constituent kind that produced this row
        /// ("mortar_cement" / "plaster_sand" / "rebar" / …), or null for rows from
        /// the non-compound path. Additive: defaults null so existing JSON
        /// snapshots deserialise unchanged. The material schedule routes rows to
        /// construction stages by this value; the "[Compound: …]" Note prefix
        /// stays for human readers.
        /// </summary>
        public string ConstituentKind;
```

- [x] **Step 2: Carry it through `Clone()`**

In the same file, inside `BOQLineItem.Clone()`, after the `Unit = this.Unit,` line, add:

```csharp
                ConstituentKind = this.ConstituentKind,
```

- [x] **Step 3: Populate it where the Note prefix is written**

In `StingTools/BOQ/Takeoff/CompoundTakeoffBuilder.cs`, inside the `new BOQLineItem { … }` initialiser (around line 275), add immediately before the `Note = …` line:

```csharp
                    ConstituentKind = c.Kind,
```

- [x] **Step 4: Build the plugin to verify it compiles**

```bash
dotnet build StingTools/StingTools.csproj -c Debug --nologo
```

Expected: `0 Error(s)`. Warning count must not increase from the current baseline of 0.

- [x] **Step 5: Commit**

```bash
git add StingTools/BOQ/BOQModels.cs StingTools/BOQ/Takeoff/CompoundTakeoffBuilder.cs
git commit -m "feat(boq): carry the compound constituent kind as a first-class field"
```

---

### Task 2: Route material-schedule exports to the schedules folder (C6)

**Files:**
- Modify: `StingTools/Core/ProjectFolderEngine.cs:85`

Unknown export keys silently land in `MISC`.

- [x] **Step 1: Add the key**

In the `ExportTypeToFolder` dictionary, immediately after the `["BOQ"] = "SCHEDULES",` entry, add:

```csharp
            ["MaterialSchedule"] = "SCHEDULES",
```

- [x] **Step 2: Build**

```bash
dotnet build StingTools/StingTools.csproj -c Debug --nologo
```

Expected: `0 Error(s)`.

- [x] **Step 3: Commit**

```bash
git add StingTools/Core/ProjectFolderEngine.cs
git commit -m "feat(paths): route MaterialSchedule exports to the SCHEDULES folder"
```

---

### Task 3: Extract the XLSX style helpers (C8)

**Files:**
- Create: `StingTools/BOQ/BoqXlsxStyle.cs`
- Modify: `StingTools/BOQ/BOQExportCommand.cs:535-580`

`BannerRow` / `WriteHeader` / `SourceLabel` are private instance methods; a second renderer cannot reach them.

- [x] **Step 1: Read the current helpers**

Open `StingTools/BOQ/BOQExportCommand.cs` and read lines 525-585. Note the exact colour constants (`NavyFill`, `HeaderFill`) declared at the top of the class (lines 28-35) — they move too.

- [x] **Step 2: Create the shared static**

Create `StingTools/BOQ/BoqXlsxStyle.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  BoqXlsxStyle.cs — shared ClosedXML styling for every BOQ-family workbook.
//
//  Extracted from BOQExportCommand (where these were private instance methods)
//  so the material-schedule renderer produces visually identical output instead
//  of a near-miss duplicate. BOQExportCommand now delegates here; its own
//  private wrappers remain so its ~40 call sites are untouched.
// ══════════════════════════════════════════════════════════════════════════
using ClosedXML.Excel;

namespace StingTools.BOQ
{
    internal static class BoqXlsxStyle
    {
        public static readonly XLColor NavyFill   = XLColor.FromArgb(26, 58, 92);
        public static readonly XLColor HeaderFill = XLColor.FromArgb(46, 94, 142);
        public static readonly XLColor ManualRow  = XLColor.FromArgb(255, 251, 230);

        /// <summary>Full-width navy title banner on row 1.</summary>
        public static void BannerRow(IXLWorksheet ws, string text)
        {
            var cell = ws.Cell(1, 1);
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 13;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = NavyFill;
            ws.Row(1).Height = 22;
        }

        /// <summary>Bold white-on-blue column header row.</summary>
        public static void WriteHeader(IXLWorksheet ws, int row, string[] cols)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = cols[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = HeaderFill;
                cell.Style.Alignment.WrapText = true;
            }
        }

        /// <summary>UGX money format — thousands separated, no decimals.</summary>
        public static void MoneyFormat(IXLRange range)
        {
            range.Style.NumberFormat.Format = "#,##0";
        }
    }
}
```

> If the real `BannerRow` / `WriteHeader` bodies in `BOQExportCommand` differ from the above (merged cells, different row height, borders), **copy the real bodies verbatim** rather than the approximations here. The goal is byte-identical output, not a rewrite.

- [x] **Step 3: Delegate from `BOQExportCommand`**

Replace the bodies of the two private methods in `BOQExportCommand.cs` so all existing call sites keep working:

```csharp
        private void BannerRow(IXLWorksheet ws, string text) => BoqXlsxStyle.BannerRow(ws, text);

        private void WriteHeader(IXLWorksheet ws, int row, string[] cols) => BoqXlsxStyle.WriteHeader(ws, row, cols);
```

Leave `SourceLabel` alone — it is BOQ-specific and the material schedule has no use for it.

- [x] **Step 4: Build**

```bash
dotnet build StingTools/StingTools.csproj -c Debug --nologo
```

Expected: `0 Error(s)`.

- [x] **Step 5: Commit**

```bash
git add StingTools/BOQ/BoqXlsxStyle.cs StingTools/BOQ/BOQExportCommand.cs
git commit -m "refactor(boq): extract shared XLSX style helpers for reuse"
```

---

## Phase 1 — The engine (Revit-free, TDD)

### Task 4: The data model

**Files:**
- Create: `StingTools/Core/MaterialSchedule/MaterialScheduleModel.cs`
- Modify: `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`
- Test: `StingTools.Boq.Tests/MaterialScheduleModelTests.cs`

- [x] **Step 1: Write the failing test**

Create `StingTools.Boq.Tests/MaterialScheduleModelTests.cs`:

```csharp
using System.Collections.Generic;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the money in a material schedule is DERIVED, never stored, so
    /// the PATMAC defect "Amount != Quantity x Rate" (D3) cannot be represented.
    /// </summary>
    public class MaterialScheduleModelTests
    {
        private static MaterialCommodity Cement(double orderQty, double rate) => new MaterialCommodity
        {
            CommodityKey = "cement",
            Description = "Cement (OPC 42.5N)",
            SupplierUnit = "Bags",
            NetQuantity = orderQty,
            OrderQuantity = orderQty,
            RateUGX = rate
        };

        [Fact]
        public void Commodity_Amount_Is_Order_Quantity_Times_Rate()
        {
            var c = Cement(350, 28000);
            Assert.Equal(9_800_000, c.AmountUGX);
        }

        [Fact]
        public void Stage_SubTotal_Sums_Commodities_Labour_And_Provisional_Sums()
        {
            var stage = new StageSection { StageId = "substructure", Title = "SUB-STRUCTURE" };
            stage.Commodities.Add(Cement(350, 28000));                       //  9,800,000
            stage.Labour.Add(new LabourLine { AmountUGX = 7_546_800 });      //  7,546,800
            stage.ProvisionalSums.Add(new ProvisionalSumLine { AmountUGX = 1_000_000 });

            Assert.Equal(18_346_800, stage.SubTotalUGX);
        }

        [Fact]
        public void Document_Applies_Contingency_To_The_Works_Subtotal()
        {
            var doc = new MaterialScheduleDocument();
            doc.Options.ContingencyPct = 5.0;
            var stage = new StageSection { StageId = "s", Title = "S" };
            stage.Commodities.Add(Cement(100, 10_000));                      // 1,000,000
            doc.Stages.Add(stage);

            Assert.Equal(1_000_000, doc.WorksSubtotalUGX);
            Assert.Equal(50_000, doc.ContingencyUGX);
            Assert.Equal(1_050_000, doc.GrandTotalUGX);
        }
    }
}
```

- [x] **Step 2: Wire the engine into the test project**

In `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`, inside the first `<ItemGroup>` that holds the `<Compile Include>` entries, append:

```xml
    <!-- MAT-SCHED — Document-free material-schedule engine. -->
    <Compile Include="..\StingTools\Core\MaterialSchedule\MaterialScheduleModel.cs" Link="MaterialSchedule\MaterialScheduleModel.cs" />
```

- [x] **Step 3: Run the test to verify it fails**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~MaterialScheduleModelTests"
```

Expected: build failure — `The type or namespace name 'MaterialSchedule' does not exist in the namespace 'StingTools.Core'`.

- [x] **Step 4: Write the model**

Create `StingTools/Core/MaterialSchedule/MaterialScheduleModel.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleModel.cs — MAT-SCHED pure data model.
//  No Revit API, no file I/O, no WPF. Every other MaterialSchedule file
//  depends on these types.
//
//  Money is DERIVED, never stored: the PATMAC reference sample shipped a row
//  reading "1 Roll x 300,000 = 150,000", and a stored Amount field is what
//  makes that representable. Deriving it removes the defect class instead of
//  detecting it.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One buyable commodity within one construction stage.</summary>
    public sealed class MaterialCommodity
    {
        public string CommodityKey = "";     // canonical merge key — "cement"
        public string Description = "";      // "Cement (OPC 42.5N)"
        public string Spec = "";             // "T16" / "8\" hollow" / "1000g"
        public string SupplierUnit = "";     // "Bags" / "Trips (Sino Truck)" / "No."
        public double NetQuantity;           // supplier units, PRE-wastage
        public double WastagePct;            // visible; never folded into the quantity
        public double OrderQuantity;         // post-wastage, rounded per the unit rule
        public double RateUGX;
        public string RateSource = "";       // "baseline" / "project" / "unpriced"
        public List<string> TraceRefs = new List<string>();

        /// <summary>Derived — see the file header. Rounded to whole UGX.</summary>
        public double AmountUGX => Math.Round(OrderQuantity * RateUGX, 0);

        /// <summary>True when no rate could be resolved for this commodity.</summary>
        public bool IsUnpriced => RateUGX <= 0;
    }

    /// <summary>
    /// A stage's labour lump. QS-entered: the BOQ's LabourUGX rate split is nulled
    /// on manual override and on modal-rate aggregation, so deriving from it would
    /// silently under-report. The derived figure is offered as a SUGGESTION only,
    /// and only when every contributing row carries a split.
    /// </summary>
    public sealed class LabourLine
    {
        public string Description = "Labour";
        public double AmountUGX;
        public double? SuggestedUGX;
        public string SuggestionBasis = "";
    }

    /// <summary>A provisional sum carried from the BOQ manual store.</summary>
    public sealed class ProvisionalSumLine
    {
        public string Description = "";
        public double AmountUGX;
        public string SourceRef = "";
    }

    /// <summary>One lettered section of the schedule.</summary>
    public sealed class StageSection
    {
        public string StageId = "";
        public string Letter = "";           // assigned by StageMapper — never authored
        public string Title = "";
        public string Preamble = "";
        public List<MaterialCommodity> Commodities = new List<MaterialCommodity>();
        public List<LabourLine> Labour = new List<LabourLine>();
        public List<ProvisionalSumLine> ProvisionalSums = new List<ProvisionalSumLine>();

        public double SubTotalUGX =>
            Commodities.Sum(c => c.AmountUGX)
            + Labour.Sum(l => l.AmountUGX)
            + ProvisionalSums.Sum(p => p.AmountUGX);
    }

    public sealed class MaterialScheduleOptions
    {
        /// <summary>Renderer flag only — the engine computes identically either way.</summary>
        public bool ShowPrices = true;
        public double ContingencyPct = 5.0;
    }

    public sealed class ReconciliationIssue
    {
        public string Code = "";             // "R1".."R4"
        public string Message = "";
        public string StageId = "";
        public string CommodityKey = "";
    }

    public sealed class MaterialScheduleReconciliation
    {
        public List<ReconciliationIssue> Issues = new List<ReconciliationIssue>();
        public bool IsClean => Issues.Count == 0;
    }

    public sealed class MaterialScheduleDocument
    {
        public string ProjectName = "";
        public string ProjectCode = "";
        public DateTime GeneratedUtc = DateTime.UtcNow;
        public string Currency = "UGX";
        public List<StageSection> Stages = new List<StageSection>();
        public MaterialScheduleOptions Options = new MaterialScheduleOptions();
        public MaterialScheduleReconciliation Reconciliation = new MaterialScheduleReconciliation();

        public double WorksSubtotalUGX => Stages.Sum(s => s.SubTotalUGX);
        public double ContingencyUGX => Math.Round(WorksSubtotalUGX * Options.ContingencyPct / 100.0, 0);
        public double GrandTotalUGX => WorksSubtotalUGX + ContingencyUGX;

        /// <summary>
        /// The summary is PROJECTED from the body, never authored alongside it —
        /// so the PATMAC defects D1 (duplicate letters) and D2 (summary order not
        /// matching body order) are unrepresentable.
        /// </summary>
        public IEnumerable<(string Letter, string Title, double SubTotalUGX)> Summary =>
            Stages.Select(s => (s.Letter, s.Title, s.SubTotalUGX));
    }
}
```

- [x] **Step 5: Run the test to verify it passes**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~MaterialScheduleModelTests"
```

Expected: `Passed! - Failed: 0, Passed: 3`.

- [x] **Step 6: Commit**

```bash
git add StingTools/Core/MaterialSchedule/MaterialScheduleModel.cs StingTools.Boq.Tests/MaterialScheduleModelTests.cs StingTools.Boq.Tests/StingTools.Boq.Tests.csproj
git commit -m "feat(material-schedule): pure data model with derived money"
```

---

### Task 5: Supplier-unit conversion

**Files:**
- Create: `StingTools/Core/MaterialSchedule/SupplierUnitConverter.cs`
- Create: `StingTools/Data/STING_SUPPLIER_UNITS.json`
- Modify: `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`
- Test: `StingTools.Boq.Tests/SupplierUnitConverterTests.cs`

The BOQ emits m³ of sand; the schedule must print `Trips (Sino Truck)`.

- [x] **Step 1: Write the failing test**

Create `StingTools.Boq.Tests/SupplierUnitConverterTests.cs`:

```csharp
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — SI measured quantities become supplier units. Wastage is a
    /// separate visible step, and countable units round UP: you cannot buy 2.08
    /// trips of sand.
    /// </summary>
    public class SupplierUnitConverterTests
    {
        private static SupplierUnitRule SandTrips() => new SupplierUnitRule
        {
            CommodityKey = "sand",
            Description = "Sand",
            SupplierUnit = "Trips (Sino Truck)",
            SourceUnit = "m3",
            SourceUnitsPerSupplierUnit = 12.0,
            RoundUpToWhole = true,
            DefaultWastagePct = 0
        };

        private static SupplierUnitRule CementBags() => new SupplierUnitRule
        {
            CommodityKey = "cement",
            Description = "Cement (OPC 42.5N)",
            SupplierUnit = "Bags",
            SourceUnit = "bag",
            SourceUnitsPerSupplierUnit = 1.0,
            RoundUpToWhole = true,
            DefaultWastagePct = 2.5
        };

        [Fact]
        public void Converts_Cubic_Metres_To_Whole_Trips_Rounding_Up()
        {
            var r = SupplierUnitConverter.Convert(SandTrips(), sourceQuantity: 25.0);

            Assert.Equal("Trips (Sino Truck)", r.SupplierUnit);
            Assert.Equal(25.0 / 12.0, r.NetQuantity, 6);   // 2.0833…
            Assert.Equal(3, r.OrderQuantity);              // ceil, not 2
        }

        [Fact]
        public void Applies_Wastage_Before_Rounding_And_Reports_It_Separately()
        {
            // 100 bags + 2.5% = 102.5 → 103
            var r = SupplierUnitConverter.Convert(CementBags(), sourceQuantity: 100.0);

            Assert.Equal(100.0, r.NetQuantity, 6);   // net stays PRE-wastage
            Assert.Equal(2.5, r.WastagePct);
            Assert.Equal(103, r.OrderQuantity);
        }

        [Fact]
        public void Non_Countable_Units_Keep_Their_Fraction()
        {
            var rule = SandTrips();
            rule.SupplierUnit = "m³";
            rule.SourceUnitsPerSupplierUnit = 1.0;
            rule.RoundUpToWhole = false;

            var r = SupplierUnitConverter.Convert(rule, sourceQuantity: 2.5);

            Assert.Equal(2.5, r.OrderQuantity, 6);
        }

        [Fact]
        public void A_Zero_Or_Negative_Conversion_Factor_Falls_Back_To_One_Not_Infinity()
        {
            var rule = SandTrips();
            rule.SourceUnitsPerSupplierUnit = 0;   // bad data in the JSON

            var r = SupplierUnitConverter.Convert(rule, sourceQuantity: 25.0);

            Assert.Equal(25.0, r.NetQuantity, 6);
            Assert.False(double.IsInfinity(r.OrderQuantity));
        }

        [Fact]
        public void Rules_Resolve_By_Constituent_Kind()
        {
            var table = new SupplierUnitTable();
            table.Rules.Add(CementBags());
            table.Rules[0].MatchKinds.Add("mortar_cement");
            table.Rules[0].MatchKinds.Add("plaster_cement");

            Assert.Equal("cement", table.ResolveByKind("plaster_cement")?.CommodityKey);
            Assert.Null(table.ResolveByKind("formwork"));
        }
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~SupplierUnitConverterTests"
```

Expected: build failure — `The name 'SupplierUnitConverter' does not exist`.

- [x] **Step 3: Write the implementation**

Create `StingTools/Core/MaterialSchedule/SupplierUnitConverter.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  SupplierUnitConverter.cs — MAT-SCHED SI → supplier-unit conversion.
//
//  The BOQ measures in m3 / m2 / kg / bag / nr. A material schedule speaks the
//  vendor's language: Trips, Bags, Rolls, Pcs. This is a UNIT TABLE only — it
//  holds no measurement logic and derives no quantities.
//
//  Wastage is applied as its own visible step and never folded into NetQuantity,
//  so a QS can see and argue with the allowance.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class SupplierUnitRule
    {
        public string CommodityKey = "";
        public string Description = "";
        public string Spec = "";
        public string SupplierUnit = "";
        public string SourceUnit = "";                  // BoqUnits-normalised token
        public double SourceUnitsPerSupplierUnit = 1.0; // e.g. 12 m3 per Sino Truck trip
        public bool RoundUpToWhole = true;
        public double DefaultWastagePct = 0.0;
        /// <summary>CompoundTakeoff constituent kinds that map to this commodity.</summary>
        public List<string> MatchKinds = new List<string>();
    }

    public sealed class SupplierUnitTable
    {
        public List<SupplierUnitRule> Rules = new List<SupplierUnitRule>();

        /// <summary>First rule listing this constituent kind, or null.</summary>
        public SupplierUnitRule ResolveByKind(string constituentKind)
        {
            if (string.IsNullOrWhiteSpace(constituentKind)) return null;
            return Rules.FirstOrDefault(r => r.MatchKinds != null
                && r.MatchKinds.Any(k => string.Equals(k, constituentKind, StringComparison.OrdinalIgnoreCase)));
        }

        public SupplierUnitRule ResolveByCommodityKey(string commodityKey)
        {
            if (string.IsNullOrWhiteSpace(commodityKey)) return null;
            return Rules.FirstOrDefault(r =>
                string.Equals(r.CommodityKey, commodityKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    public struct SupplierUnitResult
    {
        public string SupplierUnit;
        public double NetQuantity;    // supplier units, PRE-wastage
        public double WastagePct;
        public double OrderQuantity;  // post-wastage, rounded per the rule
    }

    public static class SupplierUnitConverter
    {
        public static SupplierUnitResult Convert(SupplierUnitRule rule, double sourceQuantity)
        {
            if (rule == null)
                return new SupplierUnitResult
                {
                    SupplierUnit = "", NetQuantity = sourceQuantity,
                    WastagePct = 0, OrderQuantity = sourceQuantity
                };

            // Bad or missing conversion data must degrade to 1:1, never to
            // Infinity/NaN — a silent Infinity would print as a blank cell.
            double factor = rule.SourceUnitsPerSupplierUnit;
            if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor)) factor = 1.0;

            double net = sourceQuantity / factor;
            double waste = Math.Max(0, rule.DefaultWastagePct);
            double order = net * (1.0 + waste / 100.0);
            if (rule.RoundUpToWhole) order = Math.Ceiling(order - 1e-9);

            return new SupplierUnitResult
            {
                SupplierUnit = rule.SupplierUnit,
                NetQuantity = net,
                WastagePct = waste,
                OrderQuantity = order
            };
        }
    }
}
```

- [x] **Step 4: Add the file to the test project**

In `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`, after the `MaterialScheduleModel.cs` entry:

```xml
    <Compile Include="..\StingTools\Core\MaterialSchedule\SupplierUnitConverter.cs" Link="MaterialSchedule\SupplierUnitConverter.cs" />
```

- [x] **Step 5: Run the test to verify it passes**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~SupplierUnitConverterTests"
```

Expected: `Passed! - Failed: 0, Passed: 5`.

- [x] **Step 6: Create the shipped rule table**

Create `StingTools/Data/STING_SUPPLIER_UNITS.json`. Conversion factors are Ugandan-practice defaults; a project overrides them at `_BIM_COORD/supplier_units.json`.

```json
{
  "schemaVersion": "1.0",
  "note": "Corporate baseline. Project override: <project>/_data/coord/supplier_units.json (merged by commodityKey).",
  "rules": [
    {
      "commodityKey": "cement",
      "description": "Cement (OPC 42.5N)",
      "supplierUnit": "Bags",
      "sourceUnit": "bag",
      "sourceUnitsPerSupplierUnit": 1.0,
      "roundUpToWhole": true,
      "defaultWastagePct": 2.5,
      "matchKinds": ["mortar_cement", "plaster_cement"]
    },
    {
      "commodityKey": "sand",
      "description": "River and pit sand",
      "supplierUnit": "Trips (Sino Truck)",
      "sourceUnit": "m3",
      "sourceUnitsPerSupplierUnit": 12.0,
      "roundUpToWhole": true,
      "defaultWastagePct": 5.0,
      "matchKinds": ["mortar_sand", "plaster_sand"]
    },
    {
      "commodityKey": "aggregate",
      "description": "Aggregates (machine crushed)",
      "supplierUnit": "Trips (Sino Truck)",
      "sourceUnit": "m3",
      "sourceUnitsPerSupplierUnit": 12.0,
      "roundUpToWhole": true,
      "defaultWastagePct": 5.0,
      "matchKinds": []
    },
    {
      "commodityKey": "block",
      "description": "Hollow blocks 8\"",
      "supplierUnit": "No.",
      "sourceUnit": "nr",
      "sourceUnitsPerSupplierUnit": 1.0,
      "roundUpToWhole": true,
      "defaultWastagePct": 5.0,
      "matchKinds": ["blockwork", "units", "infill_block"]
    },
    {
      "commodityKey": "brick",
      "description": "Bricks",
      "supplierUnit": "No.",
      "sourceUnit": "nr",
      "sourceUnitsPerSupplierUnit": 1.0,
      "roundUpToWhole": true,
      "defaultWastagePct": 5.0,
      "matchKinds": ["brickwork"]
    },
    {
      "commodityKey": "rebar",
      "description": "Steel reinforcement bars",
      "supplierUnit": "Kg",
      "sourceUnit": "kg",
      "sourceUnitsPerSupplierUnit": 1.0,
      "roundUpToWhole": true,
      "defaultWastagePct": 7.5,
      "matchKinds": ["rebar", "mesh"]
    },
    {
      "commodityKey": "formwork-timber",
      "description": "Sawn timber for formwork",
      "supplierUnit": "m²",
      "sourceUnit": "m2",
      "sourceUnitsPerSupplierUnit": 1.0,
      "roundUpToWhole": false,
      "defaultWastagePct": 15.0,
      "matchKinds": ["formwork"]
    },
    {
      "commodityKey": "concrete-ready",
      "description": "In-situ concrete",
      "supplierUnit": "m³",
      "sourceUnit": "m3",
      "sourceUnitsPerSupplierUnit": 1.0,
      "roundUpToWhole": false,
      "defaultWastagePct": 5.0,
      "matchKinds": ["concrete", "precast_rib"]
    }
  ]
}
```

- [x] **Step 7: Add a test that the shipped file parses into the rule table**

Append to `StingTools.Boq.Tests/SupplierUnitConverterTests.cs`, inside the class:

```csharp
        [Fact]
        public void Shipped_Baseline_Json_Parses_And_Every_Rule_Is_Usable()
        {
            string path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json");
            Assert.True(System.IO.File.Exists(path), $"missing shipped file: {path}");

            var table = Newtonsoft.Json.JsonConvert
                .DeserializeObject<SupplierUnitTable>(System.IO.File.ReadAllText(path));

            Assert.NotNull(table);
            Assert.NotEmpty(table!.Rules);
            foreach (var r in table.Rules)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.CommodityKey), "rule with no commodityKey");
                Assert.False(string.IsNullOrWhiteSpace(r.SupplierUnit), $"{r.CommodityKey}: no supplierUnit");
                Assert.True(r.SourceUnitsPerSupplierUnit > 0, $"{r.CommodityKey}: non-positive conversion factor");
            }
        }
```

This is the guard against the failure mode recorded in memory: *valid JSON plus a green build can still be runtime-dead if Newtonsoft field names or types do not match.*

- [x] **Step 8: Ship the JSON to the test output and to the plugin's `data/` folder**

In `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`, inside a `<None>` `<ItemGroup>`:

```xml
    <None Include="..\StingTools\Data\STING_SUPPLIER_UNITS.json" Link="Data\STING_SUPPLIER_UNITS.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
```

Then confirm the plugin already globs `Data/*.json` to output `data/`:

```bash
grep -n "Data\\\\\*\|Data/\*\|Data\\\\" StingTools/StingTools.csproj | head
```

If the csproj lists data files individually rather than by glob, add `STING_SUPPLIER_UNITS.json` to that list in the same style as `STING_MATERIAL_RULES.json`.

Also add `Newtonsoft.Json` to the test project if it is not already referenced:

```xml
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

- [x] **Step 9: Run the tests**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~SupplierUnitConverterTests"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

- [x] **Step 10: Commit**

```bash
git add StingTools/Core/MaterialSchedule/SupplierUnitConverter.cs StingTools/Data/STING_SUPPLIER_UNITS.json StingTools.Boq.Tests/SupplierUnitConverterTests.cs StingTools.Boq.Tests/StingTools.Boq.Tests.csproj StingTools/StingTools.csproj
git commit -m "feat(material-schedule): supplier-unit conversion with visible wastage"
```

---

### Task 6: Commodity rates (C3)

**Files:**
- Create: `StingTools/Core/MaterialSchedule/CommodityRateResolver.cs`
- Create: `StingTools/Data/STING_COMMODITY_RATES.csv`
- Modify: `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`
- Test: `StingTools.Boq.Tests/CommodityRateResolverTests.cs`

No commodity rates exist anywhere in the codebase: both rate CSVs key on Revit *category*, so `ResolveConstituentRate` returns `(0, "None", 20)` for every constituent. An unpriced commodity must stay visibly unpriced — never borrow a neighbour's rate.

- [x] **Step 1: Write the failing test**

Create `StingTools.Boq.Tests/CommodityRateResolverTests.cs`:

```csharp
using System.Collections.Generic;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — commodity rates come from a dedicated price list, not the
    /// element-scoped BOQ rate providers. An unpriced commodity resolves to zero
    /// with a visible source, never to a borrowed rate.
    /// </summary>
    public class CommodityRateResolverTests
    {
        private static List<CommodityRate> Baseline() => new List<CommodityRate>
        {
            new CommodityRate { CommodityKey = "cement", SupplierUnit = "Bags", RateUGX = 28000 },
            new CommodityRate { CommodityKey = "sand",   SupplierUnit = "Trips (Sino Truck)", RateUGX = 1400000 }
        };

        [Fact]
        public void Resolves_From_The_Corporate_Baseline()
        {
            var r = new CommodityRateResolver(Baseline(), null);
            var hit = r.Resolve("cement");

            Assert.Equal(28000, hit.RateUGX);
            Assert.Equal("baseline", hit.Source);
        }

        [Fact]
        public void Project_Override_Beats_The_Baseline()
        {
            var overrides = new List<CommodityRate>
            {
                new CommodityRate { CommodityKey = "cement", SupplierUnit = "Bags", RateUGX = 31500 }
            };
            var r = new CommodityRateResolver(Baseline(), overrides);
            var hit = r.Resolve("cement");

            Assert.Equal(31500, hit.RateUGX);
            Assert.Equal("project", hit.Source);
        }

        [Fact]
        public void An_Unpriced_Commodity_Returns_Zero_And_Says_So()
        {
            var r = new CommodityRateResolver(Baseline(), null);
            var hit = r.Resolve("roofing-sheet");

            Assert.Equal(0, hit.RateUGX);
            Assert.Equal("unpriced", hit.Source);
            Assert.Contains("roofing-sheet", r.UnpricedKeys);
        }

        [Fact]
        public void Lookup_Is_Case_Insensitive()
        {
            var r = new CommodityRateResolver(Baseline(), null);
            Assert.Equal(28000, r.Resolve("CEMENT").RateUGX);
        }
    }
}
```

- [x] **Step 2: Run to verify it fails**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~CommodityRateResolverTests"
```

Expected: build failure — `The name 'CommodityRateResolver' does not exist`.

- [x] **Step 3: Write the implementation**

Create `StingTools/Core/MaterialSchedule/CommodityRateResolver.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  CommodityRateResolver.cs — MAT-SCHED commodity price list.
//
//  WHY THIS EXISTS: the BOQ's IRateProvider chain is element-scoped
//  (RateRequest.Element) and both shipped rate CSVs key on Revit CATEGORY, so
//  nothing in the codebase can price "one bag of cement". Constituent rows
//  currently resolve to (0, "None", 20) for every constituent.
//
//  An unpriced commodity stays visibly unpriced. Borrowing a neighbouring rate
//  would put a confident-looking number in a tender document.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class CommodityRate
    {
        public string CommodityKey = "";
        public string SupplierUnit = "";
        public double RateUGX;
        public string Source = "";      // "baseline" / "project" / "unpriced"
    }

    public sealed class CommodityRateResolver
    {
        private readonly Dictionary<string, CommodityRate> _baseline =
            new Dictionary<string, CommodityRate>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CommodityRate> _project =
            new Dictionary<string, CommodityRate>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unpriced =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public CommodityRateResolver(IEnumerable<CommodityRate> baseline,
                                     IEnumerable<CommodityRate> projectOverrides)
        {
            foreach (var r in baseline ?? Enumerable.Empty<CommodityRate>())
                if (!string.IsNullOrWhiteSpace(r?.CommodityKey)) _baseline[r.CommodityKey] = r;
            foreach (var r in projectOverrides ?? Enumerable.Empty<CommodityRate>())
                if (!string.IsNullOrWhiteSpace(r?.CommodityKey)) _project[r.CommodityKey] = r;
        }

        /// <summary>Commodity keys asked for but not priced. Drives the export gate.</summary>
        public IReadOnlyCollection<string> UnpricedKeys => _unpriced;

        public CommodityRate Resolve(string commodityKey)
        {
            if (string.IsNullOrWhiteSpace(commodityKey))
                return new CommodityRate { CommodityKey = "", RateUGX = 0, Source = "unpriced" };

            if (_project.TryGetValue(commodityKey, out var p) && p.RateUGX > 0)
                return new CommodityRate
                {
                    CommodityKey = p.CommodityKey, SupplierUnit = p.SupplierUnit,
                    RateUGX = p.RateUGX, Source = "project"
                };

            if (_baseline.TryGetValue(commodityKey, out var b) && b.RateUGX > 0)
                return new CommodityRate
                {
                    CommodityKey = b.CommodityKey, SupplierUnit = b.SupplierUnit,
                    RateUGX = b.RateUGX, Source = "baseline"
                };

            _unpriced.Add(commodityKey);
            return new CommodityRate { CommodityKey = commodityKey, RateUGX = 0, Source = "unpriced" };
        }

        /// <summary>
        /// Parse the shipped CSV: CommodityKey,SupplierUnit,RateUGX,Description.
        /// '#' comment lines and blank lines are skipped; unparseable rows are
        /// skipped and reported through <paramref name="skipped"/> rather than
        /// silently dropped.
        /// </summary>
        public static List<CommodityRate> ParseCsv(IEnumerable<string> lines, out List<string> skipped)
        {
            var outList = new List<CommodityRate>();
            skipped = new List<string>();
            bool headerSeen = false;

            foreach (string raw in lines ?? Enumerable.Empty<string>())
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var parts = line.Split(',');
                if (!headerSeen && parts[0].Trim().Equals("CommodityKey", StringComparison.OrdinalIgnoreCase))
                { headerSeen = true; continue; }

                if (parts.Length < 3) { skipped.Add(line); continue; }
                if (!double.TryParse(parts[2].Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double rate))
                { skipped.Add(line); continue; }

                outList.Add(new CommodityRate
                {
                    CommodityKey = parts[0].Trim(),
                    SupplierUnit = parts[1].Trim(),
                    RateUGX = rate,
                    Source = "baseline"
                });
            }
            return outList;
        }
    }
}
```

- [x] **Step 4: Add to the test project**

```xml
    <Compile Include="..\StingTools\Core\MaterialSchedule\CommodityRateResolver.cs" Link="MaterialSchedule\CommodityRateResolver.cs" />
```

- [x] **Step 5: Run to verify it passes**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~CommodityRateResolverTests"
```

Expected: `Passed! - Failed: 0, Passed: 4`.

- [x] **Step 6: Create the shipped price list**

Create `StingTools/Data/STING_COMMODITY_RATES.csv`. Rates are the PATMAC sample's own figures — Kampala, mid-2026, indicative only.

```csv
# STING_COMMODITY_RATES.csv v1.0 — corporate baseline commodity price list.
# Project override: <project>/_data/coord/commodity_rates.csv (same columns, wins by CommodityKey).
# Rates are indicative Kampala market prices; a live project MUST re-price before tender.
CommodityKey,SupplierUnit,RateUGX,Description
cement,Bags,28000,Cement OPC 42.5N 50kg bag
sand,Trips (Sino Truck),1400000,River and pit sand per Sino Truck trip
aggregate,Trips (Sino Truck),2400000,Machine-crushed aggregate per Sino Truck trip
block,No.,2500,Hollow block 8 inch
brick,No.,400,Burnt clay brick
rebar,Kg,3250,High-tensile reinforcement bar
formwork-timber,m²,7000,Sawn timber formwork per square metre contact area
concrete-ready,m³,450000,In-situ concrete supplied and placed
```

- [x] **Step 7: Add a shipped-file parse test**

Append to `CommodityRateResolverTests.cs`, inside the class:

```csharp
        [Fact]
        public void Shipped_Rate_Csv_Parses_With_No_Skipped_Rows()
        {
            string path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_COMMODITY_RATES.csv");
            Assert.True(System.IO.File.Exists(path), $"missing shipped file: {path}");

            var rates = CommodityRateResolver.ParseCsv(
                System.IO.File.ReadAllLines(path), out var skipped);

            Assert.Empty(skipped);
            Assert.NotEmpty(rates);
            Assert.All(rates, r => Assert.True(r.RateUGX > 0, $"{r.CommodityKey} priced at zero"));
        }

        [Fact]
        public void Every_Supplier_Unit_Rule_Has_A_Matching_Rate()
        {
            // Guards the seam: a commodity that can be measured but not priced
            // would export a confident-looking zero.
            string unitsPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json");
            string ratesPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_COMMODITY_RATES.csv");

            var table = Newtonsoft.Json.JsonConvert
                .DeserializeObject<SupplierUnitTable>(System.IO.File.ReadAllText(unitsPath));
            var rates = CommodityRateResolver.ParseCsv(System.IO.File.ReadAllLines(ratesPath), out _);
            var resolver = new CommodityRateResolver(rates, null);

            foreach (var rule in table!.Rules)
                Assert.True(resolver.Resolve(rule.CommodityKey).RateUGX > 0,
                    $"commodity '{rule.CommodityKey}' is measurable but has no baseline rate");
        }
```

- [x] **Step 8: Ship the CSV to test output and plugin data**

```xml
    <None Include="..\StingTools\Data\STING_COMMODITY_RATES.csv" Link="Data\STING_COMMODITY_RATES.csv">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
```

Add to `StingTools.csproj` data list if it enumerates files individually (see Task 5 Step 8).

- [x] **Step 9: Run the tests**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~CommodityRateResolverTests"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

- [x] **Step 10: Commit**

```bash
git add StingTools/Core/MaterialSchedule/CommodityRateResolver.cs StingTools/Data/STING_COMMODITY_RATES.csv StingTools.Boq.Tests/CommodityRateResolverTests.cs StingTools.Boq.Tests/StingTools.Boq.Tests.csproj StingTools/StingTools.csproj
git commit -m "feat(material-schedule): commodity price list with unpriced-key tracking"
```

---

### Task 7: Stage mapping and lettering

**Files:**
- Create: `StingTools/Core/MaterialSchedule/StageMapper.cs`
- Create: `StingTools/Data/STING_MATERIAL_STAGES.json`
- Modify: `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`
- Test: `StingTools.Boq.Tests/StageMapperTests.cs`

This is where PATMAC defects **D1** (letters `C`, `D`, `E` each used twice) and **D2** (summary order not matching body order) are eliminated.

- [x] **Step 1: Write the failing test**

Create `StingTools.Boq.Tests/StageMapperTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — routing is by CONSTITUENT KIND, not by element: a wall sends
    /// blockwork to its storey stage and plaster to Finishes, which is why the
    /// PATMAC Finishes section carries its own 600 bags of cement.
    ///
    /// Letters are assigned, never authored, so PATMAC defects D1 and D2 cannot
    /// recur.
    /// </summary>
    public class StageMapperTests
    {
        private static List<StageDefinition> Defs() => new List<StageDefinition>
        {
            new StageDefinition { StageId = "tools",        Title = "TOOLS AND EQUIPMENT", Order = 10 },
            new StageDefinition { StageId = "substructure", Title = "SUB-STRUCTURE",       Order = 20,
                                  Categories = { "Structural Foundations" } },
            new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE",    Order = 30,
                                  ConstituentKinds = { "blockwork", "mortar_cement", "mortar_sand", "concrete", "rebar" } },
            new StageDefinition { StageId = "finishes",     Title = "FINISHES",            Order = 40,
                                  ConstituentKinds = { "plaster", "plaster_cement", "plaster_sand" } }
        };

        [Fact]
        public void Plaster_Constituents_Route_To_Finishes_Not_To_The_Storey()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: "plaster_cement", category: "Walls",
                levelCode: "L01", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("finishes", stage);
        }

        [Fact]
        public void Blockwork_From_The_Same_Wall_Routes_To_The_Superstructure()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: "blockwork", category: "Walls",
                levelCode: "L01", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("superstructure", stage);
        }

        [Fact]
        public void Category_Routing_Applies_When_There_Is_No_Constituent_Kind()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: null, category: "Structural Foundations",
                levelCode: "GF", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("substructure", stage);
        }

        [Fact]
        public void An_Unmatched_Row_Goes_To_The_Named_Default_Never_Vanishes()
        {
            string stage = StageMapper.ResolveStageId(
                constituentKind: "something_new", category: "Casework",
                levelCode: "L02", defs: Defs(), defaultStageId: "superstructure");

            Assert.Equal("superstructure", stage);
        }

        [Fact]
        public void Letters_Are_Sequential_And_Unique()
        {
            var stages = new List<StageSection>
            {
                new StageSection { StageId = "tools" },
                new StageSection { StageId = "substructure" },
                new StageSection { StageId = "superstructure" },
                new StageSection { StageId = "finishes" }
            };

            StageMapper.AssignLetters(stages);

            Assert.Equal(new[] { "A", "B", "C", "D" }, stages.Select(s => s.Letter).ToArray());
            Assert.Equal(stages.Count, stages.Select(s => s.Letter).Distinct().Count());
        }

        [Fact]
        public void Lettering_Survives_More_Than_Twenty_Six_Stages()
        {
            var stages = Enumerable.Range(0, 28)
                .Select(i => new StageSection { StageId = $"s{i}" }).ToList();

            StageMapper.AssignLetters(stages);

            Assert.Equal("Z", stages[25].Letter);
            Assert.Equal("AA", stages[26].Letter);
            Assert.Equal("AB", stages[27].Letter);
            Assert.Equal(28, stages.Select(s => s.Letter).Distinct().Count());
        }

        [Fact]
        public void The_Summary_Is_Projected_From_The_Body_So_It_Cannot_Diverge()
        {
            var doc = new MaterialScheduleDocument();
            doc.Stages.Add(new StageSection { StageId = "a", Title = "ALPHA" });
            doc.Stages.Add(new StageSection { StageId = "b", Title = "BETA" });
            StageMapper.AssignLetters(doc.Stages);

            var summary = doc.Summary.ToList();

            Assert.Equal(doc.Stages.Count, summary.Count);
            for (int i = 0; i < summary.Count; i++)
            {
                Assert.Equal(doc.Stages[i].Letter, summary[i].Letter);
                Assert.Equal(doc.Stages[i].Title, summary[i].Title);
            }
        }
    }
}
```

- [x] **Step 2: Run to verify it fails**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~StageMapperTests"
```

Expected: build failure — `The name 'StageMapper' does not exist`.

- [x] **Step 3: Write the implementation**

Create `StingTools/Core/MaterialSchedule/StageMapper.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  StageMapper.cs — MAT-SCHED construction-stage routing and lettering.
//
//  Routing key is the CONSTITUENT KIND, not the element. One wall sends its
//  blockwork and mortar to a storey stage and its plaster to Finishes — which
//  is why the PATMAC reference sample's Finishes section carries its own cement.
//
//  Section letters are ASSIGNED here and never authored, so the sample's
//  duplicated C/D/E letters and its mismatched summary order are unrepresentable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public sealed class StageDefinition
    {
        public string StageId = "";
        public string Title = "";
        public string Preamble = "";
        public int Order;
        /// <summary>Constituent kinds routed here. Checked before Categories.</summary>
        public List<string> ConstituentKinds = new List<string>();
        /// <summary>Revit category display names routed here.</summary>
        public List<string> Categories = new List<string>();
        /// <summary>When set, only rows on these level codes route here.</summary>
        public List<string> LevelCodes = new List<string>();
    }

    public sealed class StageLibrary
    {
        public string SchemaVersion = "1.0";
        public string DefaultStageId = "";
        public List<StageDefinition> Stages = new List<StageDefinition>();
    }

    public static class StageMapper
    {
        /// <summary>
        /// Resolve a row to a stage id. Precedence: constituent kind → category →
        /// level code → the caller's named default. An unmatched row is never
        /// dropped; it lands in the default so it stays visible and countable.
        /// </summary>
        public static string ResolveStageId(string constituentKind, string category,
            string levelCode, IReadOnlyList<StageDefinition> defs, string defaultStageId)
        {
            if (defs == null || defs.Count == 0) return defaultStageId ?? "";
            var ordered = defs.OrderBy(d => d.Order).ToList();

            if (!string.IsNullOrWhiteSpace(constituentKind))
            {
                var hit = ordered.FirstOrDefault(d => d.ConstituentKinds != null
                    && d.ConstituentKinds.Any(k => string.Equals(k, constituentKind, StringComparison.OrdinalIgnoreCase)));
                if (hit != null) return hit.StageId;
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var hit = ordered.FirstOrDefault(d => d.Categories != null
                    && d.Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)));
                if (hit != null) return hit.StageId;
            }

            if (!string.IsNullOrWhiteSpace(levelCode))
            {
                var hit = ordered.FirstOrDefault(d => d.LevelCodes != null && d.LevelCodes.Count > 0
                    && d.LevelCodes.Any(l => string.Equals(l, levelCode, StringComparison.OrdinalIgnoreCase)));
                if (hit != null) return hit.StageId;
            }

            return defaultStageId ?? "";
        }

        /// <summary>
        /// Stamp sequential letters (A, B, … Z, AA, AB, …) in list order. The list
        /// order IS the document order, and the summary projects from the same
        /// list, so body and summary cannot disagree.
        /// </summary>
        public static void AssignLetters(IList<StageSection> stages)
        {
            if (stages == null) return;
            for (int i = 0; i < stages.Count; i++)
                if (stages[i] != null) stages[i].Letter = ToLetter(i);
        }

        /// <summary>0 → "A", 25 → "Z", 26 → "AA" (spreadsheet-column style).</summary>
        public static string ToLetter(int zeroBasedIndex)
        {
            if (zeroBasedIndex < 0) return "";
            string s = "";
            int n = zeroBasedIndex;
            while (true)
            {
                s = (char)('A' + (n % 26)) + s;
                n = n / 26 - 1;
                if (n < 0) break;
            }
            return s;
        }
    }
}
```

- [x] **Step 4: Add to the test project**

```xml
    <Compile Include="..\StingTools\Core\MaterialSchedule\StageMapper.cs" Link="MaterialSchedule\StageMapper.cs" />
```

- [x] **Step 5: Run to verify it passes**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~StageMapperTests"
```

Expected: `Passed! - Failed: 0, Passed: 7`.

- [x] **Step 6: Create the shipped stage library**

Create `StingTools/Data/STING_MATERIAL_STAGES.json`:

```json
{
  "schemaVersion": "1.0",
  "note": "Corporate baseline. Project override: <project>/_data/coord/material_stages.json (merged by stageId).",
  "defaultStageId": "superstructure",
  "stages": [
    {
      "stageId": "tools",
      "title": "TOOLS AND EQUIPMENT",
      "preamble": "Site establishment tools and small plant. Manual rows only — no modelled source.",
      "order": 10,
      "constituentKinds": [],
      "categories": [],
      "levelCodes": []
    },
    {
      "stageId": "substructure",
      "title": "ELEMENT 01: SUB-STRUCTURE",
      "preamble": "The works in this element include all works up to and including the ground floor slab.",
      "order": 20,
      "constituentKinds": [],
      "categories": ["Structural Foundations", "Structural Foundation"],
      "levelCodes": []
    },
    {
      "stageId": "superstructure",
      "title": "ELEMENT 02: SUPERSTRUCTURE",
      "preamble": "Frame, walling and suspended slabs above the ground floor slab.",
      "order": 30,
      "constituentKinds": ["blockwork", "brickwork", "units", "mortar", "mortar_cement", "mortar_sand",
                           "concrete", "precast_rib", "infill_block", "mesh", "rebar", "formwork"],
      "categories": ["Walls", "Floors", "Structural Columns", "Structural Framing"],
      "levelCodes": []
    },
    {
      "stageId": "roof",
      "title": "ELEMENT 03: ROOF",
      "preamble": "Roof structure and covering.",
      "order": 40,
      "constituentKinds": [],
      "categories": ["Roofs"],
      "levelCodes": []
    },
    {
      "stageId": "doors-windows",
      "title": "ELEMENT 04: DOORS AND WINDOWS",
      "preamble": "Door and window units including ironmongery.",
      "order": 50,
      "constituentKinds": [],
      "categories": ["Doors", "Windows"],
      "levelCodes": []
    },
    {
      "stageId": "finishes",
      "title": "ELEMENT 05: FINISHES",
      "preamble": "Plaster, screeds, tiling, painting and decoration.",
      "order": 60,
      "constituentKinds": ["plaster", "plaster_cement", "plaster_sand"],
      "categories": ["Ceilings"],
      "levelCodes": []
    },
    {
      "stageId": "electrical",
      "title": "ELEMENT 06: ELECTRICAL INSTALLATION",
      "preamble": "Provisional sum pending specialist design.",
      "order": 70,
      "constituentKinds": [],
      "categories": ["Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures"],
      "levelCodes": []
    },
    {
      "stageId": "mechanical",
      "title": "ELEMENT 07: MECHANICAL INSTALLATION",
      "preamble": "Provisional sum pending specialist design.",
      "order": 80,
      "constituentKinds": [],
      "categories": ["Mechanical Equipment", "Plumbing Fixtures", "Duct Systems", "Pipe Systems"],
      "levelCodes": []
    },
    {
      "stageId": "external",
      "title": "ELEMENT 08: EXTERNAL WORKS",
      "preamble": "Boundary wall, paving and external services.",
      "order": 90,
      "constituentKinds": [],
      "categories": ["Site", "Hardscape", "Parking"],
      "levelCodes": []
    }
  ]
}
```

- [x] **Step 7: Add the shipped-file parse test**

Append to `StageMapperTests.cs`, inside the class:

```csharp
        [Fact]
        public void Shipped_Stage_Library_Parses_And_Its_Default_Stage_Exists()
        {
            string path = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Data", "STING_MATERIAL_STAGES.json");
            Assert.True(System.IO.File.Exists(path), $"missing shipped file: {path}");

            var lib = Newtonsoft.Json.JsonConvert
                .DeserializeObject<StageLibrary>(System.IO.File.ReadAllText(path));

            Assert.NotNull(lib);
            Assert.NotEmpty(lib!.Stages);
            Assert.False(string.IsNullOrWhiteSpace(lib.DefaultStageId));
            Assert.Contains(lib.Stages, s => s.StageId == lib.DefaultStageId);

            // Order values must be unique — equal Order makes section sequence
            // depend on list order, which is exactly the PATMAC D2 failure.
            Assert.Equal(lib.Stages.Count, lib.Stages.Select(s => s.Order).Distinct().Count());

            // A constituent kind must not route to two stages.
            var allKinds = lib.Stages.SelectMany(s => s.ConstituentKinds ?? new List<string>()).ToList();
            Assert.Equal(allKinds.Count, allKinds.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        }
```

- [x] **Step 8: Ship the JSON**

```xml
    <None Include="..\StingTools\Data\STING_MATERIAL_STAGES.json" Link="Data\STING_MATERIAL_STAGES.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
```

- [x] **Step 9: Run the tests**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~StageMapperTests"
```

Expected: `Passed! - Failed: 0, Passed: 8`.

- [x] **Step 10: Commit**

```bash
git add StingTools/Core/MaterialSchedule/StageMapper.cs StingTools/Data/STING_MATERIAL_STAGES.json StingTools.Boq.Tests/StageMapperTests.cs StingTools.Boq.Tests/StingTools.Boq.Tests.csproj StingTools/StingTools.csproj
git commit -m "feat(material-schedule): stage routing by constituent kind, assigned lettering"
```

---

### Task 8: The aggregator

**Files:**
- Create: `StingTools/Core/MaterialSchedule/CommodityAggregator.cs`
- Modify: `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`
- Test: `StingTools.Boq.Tests/CommodityAggregatorTests.cs`

Ties the previous three together: constituent rows in, stage sections of priced commodities out.

- [x] **Step 1: Write the failing test**

Create `StingTools.Boq.Tests/CommodityAggregatorTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — many constituent rows collapse into one purchasable commodity
    /// per stage, converted to supplier units and priced from the commodity list.
    /// </summary>
    public class CommodityAggregatorTests
    {
        private static SupplierUnitTable Units()
        {
            var t = new SupplierUnitTable();
            t.Rules.Add(new SupplierUnitRule
            {
                CommodityKey = "cement", Description = "Cement (OPC 42.5N)",
                SupplierUnit = "Bags", SourceUnit = "bag",
                SourceUnitsPerSupplierUnit = 1.0, RoundUpToWhole = true,
                DefaultWastagePct = 0,
                MatchKinds = { "mortar_cement", "plaster_cement" }
            });
            t.Rules.Add(new SupplierUnitRule
            {
                CommodityKey = "sand", Description = "Sand",
                SupplierUnit = "Trips (Sino Truck)", SourceUnit = "m3",
                SourceUnitsPerSupplierUnit = 12.0, RoundUpToWhole = true,
                DefaultWastagePct = 0,
                MatchKinds = { "mortar_sand", "plaster_sand" }
            });
            return t;
        }

        private static List<StageDefinition> Stages() => new List<StageDefinition>
        {
            new StageDefinition { StageId = "superstructure", Title = "SUPERSTRUCTURE", Order = 10,
                                  ConstituentKinds = { "mortar_cement", "mortar_sand" } },
            new StageDefinition { StageId = "finishes", Title = "FINISHES", Order = 20,
                                  ConstituentKinds = { "plaster_cement", "plaster_sand" } }
        };

        private static CommodityRateResolver Rates() => new CommodityRateResolver(
            new List<CommodityRate>
            {
                new CommodityRate { CommodityKey = "cement", RateUGX = 28000 },
                new CommodityRate { CommodityKey = "sand",   RateUGX = 1400000 }
            }, null);

        private static AggregatorInputs Inputs(params ConstituentInput[] rows) => new AggregatorInputs
        {
            Constituents = rows.ToList(),
            Units = Units(),
            StageDefs = Stages(),
            DefaultStageId = "superstructure",
            Rates = Rates()
        };

        [Fact]
        public void Rows_Of_The_Same_Commodity_In_The_Same_Stage_Merge_Into_One_Line()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag", Quantity = 120, TraceRef = "W1" },
                new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag", Quantity = 230, TraceRef = "W2" }));

            var stage = doc.Stages.Single(s => s.StageId == "superstructure");
            var cement = stage.Commodities.Single(c => c.CommodityKey == "cement");

            Assert.Equal(350, cement.OrderQuantity);
            Assert.Equal(9_800_000, cement.AmountUGX);
            Assert.Equal(new[] { "W1", "W2" }, cement.TraceRefs.ToArray());
        }

        [Fact]
        public void The_Same_Commodity_In_Two_Stages_Stays_Two_Lines()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_cement",  Unit = "bag", Quantity = 100 },
                new ConstituentInput { ConstituentKind = "plaster_cement", Unit = "bag", Quantity = 600 }));

            Assert.Equal(100, doc.Stages.Single(s => s.StageId == "superstructure")
                                        .Commodities.Single().OrderQuantity);
            Assert.Equal(600, doc.Stages.Single(s => s.StageId == "finishes")
                                        .Commodities.Single().OrderQuantity);
        }

        [Fact]
        public void Quantities_Are_Summed_Before_Conversion_Not_After()
        {
            // 7 m3 + 7 m3 = 14 m3 → 14/12 = 1.17 → 2 trips.
            // Converting first would give ceil(0.58)+ceil(0.58) = 2 as well, so use
            // an asymmetric pair that actually distinguishes: 7 + 4 = 11 → 1 trip;
            // per-row conversion would give 1 + 1 = 2.
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_sand", Unit = "m3", Quantity = 7 },
                new ConstituentInput { ConstituentKind = "mortar_sand", Unit = "m3", Quantity = 4 }));

            var sand = doc.Stages.Single(s => s.StageId == "superstructure")
                                 .Commodities.Single(c => c.CommodityKey == "sand");
            Assert.Equal(1, sand.OrderQuantity);
        }

        [Fact]
        public void Stages_Come_Back_In_Order_And_Lettered()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "plaster_cement", Unit = "bag", Quantity = 10 },
                new ConstituentInput { ConstituentKind = "mortar_cement",  Unit = "bag", Quantity = 10 }));

            Assert.Equal(new[] { "superstructure", "finishes" }, doc.Stages.Select(s => s.StageId).ToArray());
            Assert.Equal(new[] { "A", "B" }, doc.Stages.Select(s => s.Letter).ToArray());
        }

        [Fact]
        public void A_Constituent_With_No_Unit_Rule_Still_Appears_Unconverted()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "formwork", Category = "Walls",
                                       Description = "Formwork", Unit = "m2", Quantity = 45 }));

            var all = doc.Stages.SelectMany(s => s.Commodities).ToList();
            var fw = Assert.Single(all, c => c.Description == "Formwork");
            Assert.Equal(45, fw.OrderQuantity);
            Assert.Equal("m2", fw.SupplierUnit);
            Assert.True(fw.IsUnpriced);
        }

        [Fact]
        public void Empty_Stages_Are_Dropped_So_The_Summary_Has_No_Blank_Rows()
        {
            var doc = CommodityAggregator.Build(Inputs(
                new ConstituentInput { ConstituentKind = "mortar_cement", Unit = "bag", Quantity = 10 }));

            Assert.Single(doc.Stages);
            Assert.Equal("superstructure", doc.Stages[0].StageId);
        }
    }
}
```

- [x] **Step 2: Run to verify it fails**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~CommodityAggregatorTests"
```

Expected: build failure — `The name 'CommodityAggregator' does not exist`.

- [x] **Step 3: Write the implementation**

Create `StingTools/Core/MaterialSchedule/CommodityAggregator.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  CommodityAggregator.cs — MAT-SCHED constituent rows → stage sections.
//
//  Quantities are SUMMED IN SOURCE UNITS BEFORE conversion. Converting per row
//  and then adding would round up once per element and inflate the order —
//  eleven cubic metres of sand is one truck trip, not two.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    /// <summary>One constituent row handed to the engine by the Revit adapter.</summary>
    public sealed class ConstituentInput
    {
        public string ConstituentKind = "";
        public string Category = "";
        public string Description = "";
        public string Unit = "";        // source unit as measured
        public double Quantity;
        public string LevelCode = "";
        public string TraceRef = "";    // BOQ line ref / element id, for the audit trail
    }

    public sealed class AggregatorInputs
    {
        public List<ConstituentInput> Constituents = new List<ConstituentInput>();
        public SupplierUnitTable Units = new SupplierUnitTable();
        public List<StageDefinition> StageDefs = new List<StageDefinition>();
        public string DefaultStageId = "";
        public CommodityRateResolver Rates;
        public MaterialScheduleOptions Options = new MaterialScheduleOptions();
    }

    public static class CommodityAggregator
    {
        public static MaterialScheduleDocument Build(AggregatorInputs input)
        {
            var doc = new MaterialScheduleDocument();
            if (input == null) return doc;
            doc.Options = input.Options ?? new MaterialScheduleOptions();

            // (stageId, commodityKey) → accumulator in SOURCE units.
            var acc = new Dictionary<(string stage, string key), Accum>();

            foreach (var row in input.Constituents ?? new List<ConstituentInput>())
            {
                if (row == null) continue;

                string stageId = StageMapper.ResolveStageId(
                    row.ConstituentKind, row.Category, row.LevelCode,
                    input.StageDefs, input.DefaultStageId);

                var rule = input.Units?.ResolveByKind(row.ConstituentKind);
                // No rule → the row still appears, keyed by its own description and
                // carrying its measured unit. Silently dropping it would lose real
                // measured work from the document.
                string commodityKey = rule?.CommodityKey
                    ?? (string.IsNullOrWhiteSpace(row.Description) ? (row.ConstituentKind ?? "") : row.Description);

                var k = (stageId, commodityKey);
                if (!acc.TryGetValue(k, out var a))
                {
                    a = new Accum
                    {
                        Rule = rule,
                        Description = rule?.Description ?? row.Description ?? commodityKey,
                        Spec = rule?.Spec ?? "",
                        FallbackUnit = row.Unit ?? ""
                    };
                    acc[k] = a;
                }
                a.SourceQuantity += row.Quantity;
                if (!string.IsNullOrWhiteSpace(row.TraceRef)) a.TraceRefs.Add(row.TraceRef);
            }

            // Materialise stages in definition order, dropping empties.
            var orderedDefs = (input.StageDefs ?? new List<StageDefinition>())
                .OrderBy(d => d.Order).ToList();

            foreach (var def in orderedDefs)
            {
                var mine = acc.Where(kv => kv.Key.stage == def.StageId)
                              .OrderBy(kv => kv.Value.Description, StringComparer.OrdinalIgnoreCase)
                              .ToList();
                if (mine.Count == 0) continue;

                var section = new StageSection
                {
                    StageId = def.StageId,
                    Title = def.Title,
                    Preamble = def.Preamble
                };

                foreach (var kv in mine)
                {
                    var a = kv.Value;
                    var conv = SupplierUnitConverter.Convert(a.Rule, a.SourceQuantity);
                    var rate = input.Rates?.Resolve(kv.Key.key)
                               ?? new CommodityRate { RateUGX = 0, Source = "unpriced" };

                    section.Commodities.Add(new MaterialCommodity
                    {
                        CommodityKey = kv.Key.key,
                        Description = a.Description,
                        Spec = a.Spec,
                        SupplierUnit = string.IsNullOrWhiteSpace(conv.SupplierUnit)
                            ? a.FallbackUnit : conv.SupplierUnit,
                        NetQuantity = conv.NetQuantity,
                        WastagePct = conv.WastagePct,
                        OrderQuantity = conv.OrderQuantity,
                        RateUGX = rate.RateUGX,
                        RateSource = rate.Source,
                        TraceRefs = a.TraceRefs
                    });
                }

                doc.Stages.Add(section);
            }

            StageMapper.AssignLetters(doc.Stages);
            return doc;
        }

        private sealed class Accum
        {
            public SupplierUnitRule Rule;
            public string Description = "";
            public string Spec = "";
            public string FallbackUnit = "";
            public double SourceQuantity;
            public List<string> TraceRefs = new List<string>();
        }
    }
}
```

- [x] **Step 4: Add to the test project**

```xml
    <Compile Include="..\StingTools\Core\MaterialSchedule\CommodityAggregator.cs" Link="MaterialSchedule\CommodityAggregator.cs" />
```

- [x] **Step 5: Run to verify it passes**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~CommodityAggregatorTests"
```

Expected: `Passed! - Failed: 0, Passed: 6`.

- [x] **Step 6: Commit**

```bash
git add StingTools/Core/MaterialSchedule/CommodityAggregator.cs StingTools.Boq.Tests/CommodityAggregatorTests.cs StingTools.Boq.Tests/StingTools.Boq.Tests.csproj
git commit -m "feat(material-schedule): aggregate constituents into stage sections"
```

---

### Task 9: The reconciler

**Files:**
- Create: `StingTools/Core/MaterialSchedule/Reconciler.cs`
- Modify: `StingTools.Boq.Tests/StingTools.Boq.Tests.csproj`
- Test: `StingTools.Boq.Tests/MaterialScheduleReconcilerTests.cs`

D3 is already structurally impossible (`AmountUGX` is derived). The reconciler covers what cannot be made structural.

- [x] **Step 1: Write the failing test**

Create `StingTools.Boq.Tests/MaterialScheduleReconcilerTests.cs`:

```csharp
using System.Linq;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED — the invariants that cannot be made structural. Fixtures are the
    /// PATMAC MALL sample's own defects:
    ///   D1 duplicate section letters
    ///   D2 summary order not matching body order
    ///   D3 Amount != Quantity x Rate  (now impossible — AmountUGX is derived)
    ///   D4 the same commodity priced two ways
    /// </summary>
    public class MaterialScheduleReconcilerTests
    {
        private static MaterialCommodity Sand(double rate) => new MaterialCommodity
        {
            CommodityKey = "sand", Description = "Sand",
            SupplierUnit = "Trips (Sino Truck)",
            NetQuantity = 2, OrderQuantity = 2, RateUGX = rate
        };

        private static MaterialScheduleDocument TwoStages(double rateA, double rateB)
        {
            var doc = new MaterialScheduleDocument();
            var a = new StageSection { StageId = "substructure", Title = "SUB-STRUCTURE" };
            a.Commodities.Add(Sand(rateA));
            var b = new StageSection { StageId = "superstructure", Title = "SUPERSTRUCTURE" };
            b.Commodities.Add(Sand(rateB));
            doc.Stages.Add(a);
            doc.Stages.Add(b);
            StageMapper.AssignLetters(doc.Stages);
            return doc;
        }

        [Fact]
        public void D4_The_Same_Commodity_At_Two_Rates_Is_Flagged()
        {
            var doc = TwoStages(1_500_000, 1_400_000);   // the exact PATMAC discrepancy

            var rec = Reconciler.Check(doc);

            var issue = Assert.Single(rec.Issues, i => i.Code == "R1");
            Assert.Equal("sand", issue.CommodityKey);
            Assert.Contains("1,500,000", issue.Message);
            Assert.Contains("1,400,000", issue.Message);
        }

        [Fact]
        public void One_Consistent_Rate_Is_Clean()
        {
            var rec = Reconciler.Check(TwoStages(1_400_000, 1_400_000));
            Assert.True(rec.IsClean, string.Join(" | ", rec.Issues.Select(i => i.Message)));
        }

        [Fact]
        public void D1_Duplicate_Section_Letters_Are_Flagged()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[1].Letter = doc.Stages[0].Letter;   // force the PATMAC defect

            var rec = Reconciler.Check(doc);

            Assert.Contains(rec.Issues, i => i.Code == "R2");
        }

        [Fact]
        public void D2_The_Summary_Always_Matches_The_Body()
        {
            var doc = TwoStages(1_400_000, 1_400_000);

            var summary = doc.Summary.ToList();

            Assert.Equal(doc.Stages.Count, summary.Count);
            Assert.Equal(doc.Stages.Select(s => s.Letter), summary.Select(s => s.Letter));
            Assert.Equal(doc.WorksSubtotalUGX, summary.Sum(s => s.SubTotalUGX));
        }

        [Fact]
        public void An_Unpriced_Commodity_Is_Flagged_When_Prices_Are_Shown()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].RateUGX = 0;
            doc.Options.ShowPrices = true;

            var rec = Reconciler.Check(doc);

            Assert.Contains(rec.Issues, i => i.Code == "R3");
        }

        [Fact]
        public void An_Unpriced_Commodity_Is_Not_Flagged_When_Prices_Are_Hidden()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].RateUGX = 0;
            doc.Options.ShowPrices = false;

            var rec = Reconciler.Check(doc);

            Assert.DoesNotContain(rec.Issues, i => i.Code == "R3");
        }

        [Fact]
        public void An_Order_Quantity_Below_The_Net_Quantity_Is_Flagged()
        {
            var doc = TwoStages(1_400_000, 1_400_000);
            doc.Stages[0].Commodities[0].OrderQuantity = 1;   // net is 2
            doc.Stages[0].Commodities[0].NetQuantity = 2;

            var rec = Reconciler.Check(doc);

            Assert.Contains(rec.Issues, i => i.Code == "R4");
        }
    }
}
```

- [x] **Step 2: Run to verify it fails**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~MaterialScheduleReconcilerTests"
```

Expected: build failure — `The name 'Reconciler' does not exist`.

- [x] **Step 3: Write the implementation**

Create `StingTools/Core/MaterialSchedule/Reconciler.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  Reconciler.cs — MAT-SCHED post-build invariants.
//
//  The PATMAC reference sample failed on arithmetic, so the invariants are the
//  point of this feature. Three of the four defect classes are now structural:
//    D3 (Amount != Qty x Rate)  — impossible: AmountUGX is derived
//    D2 (summary != body)       — impossible: the summary projects from Stages
//  What remains is checked here.
//
//    R1  one rate per commodity key, document-wide            (PATMAC D4)
//    R2  section letters unique and sequential                (PATMAC D1)
//    R3  no unpriced commodity while prices are shown         (C3 risk)
//    R4  order quantity never below net quantity              (negative wastage)
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public static class Reconciler
    {
        public static MaterialScheduleReconciliation Check(MaterialScheduleDocument doc)
        {
            var rec = new MaterialScheduleReconciliation();
            if (doc == null) return rec;

            CheckRatesConsistent(doc, rec);
            CheckLetters(doc, rec);
            CheckPriced(doc, rec);
            CheckQuantities(doc, rec);

            doc.Reconciliation = rec;
            return rec;
        }

        /// <summary>R1 — the PATMAC sand-at-two-rates defect.</summary>
        private static void CheckRatesConsistent(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            var byKey = doc.Stages
                .SelectMany(s => s.Commodities)
                .Where(c => c.RateUGX > 0 && !string.IsNullOrWhiteSpace(c.CommodityKey))
                .GroupBy(c => c.CommodityKey, StringComparer.OrdinalIgnoreCase);

            foreach (var g in byKey)
            {
                var rates = g.Select(c => Math.Round(c.RateUGX, 2)).Distinct().OrderBy(r => r).ToList();
                if (rates.Count <= 1) continue;

                rec.Issues.Add(new ReconciliationIssue
                {
                    Code = "R1",
                    CommodityKey = g.Key,
                    Message = $"Commodity '{g.Key}' is priced {rates.Count} different ways: "
                            + string.Join(", ", rates.Select(r => r.ToString("N0", CultureInfo.InvariantCulture)))
                            + " UGX. One commodity must carry one rate across the document."
                });
            }
        }

        /// <summary>R2 — the PATMAC duplicate-letter defect.</summary>
        private static void CheckLetters(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            var dupes = doc.Stages
                .Where(s => !string.IsNullOrWhiteSpace(s.Letter))
                .GroupBy(s => s.Letter, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var g in dupes)
                rec.Issues.Add(new ReconciliationIssue
                {
                    Code = "R2",
                    Message = $"Section letter '{g.Key}' is used by {g.Count()} sections: "
                            + string.Join(", ", g.Select(s => s.Title))
                });

            for (int i = 0; i < doc.Stages.Count; i++)
            {
                string expected = StageMapper.ToLetter(i);
                if (!string.Equals(doc.Stages[i].Letter, expected, StringComparison.OrdinalIgnoreCase))
                    rec.Issues.Add(new ReconciliationIssue
                    {
                        Code = "R2",
                        StageId = doc.Stages[i].StageId,
                        Message = $"Section {i + 1} ('{doc.Stages[i].Title}') is lettered "
                                + $"'{doc.Stages[i].Letter}' but its position requires '{expected}'."
                    });
            }
        }

        /// <summary>R3 — an unpriced commodity in a priced document.</summary>
        private static void CheckPriced(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            if (doc.Options == null || !doc.Options.ShowPrices) return;

            foreach (var stage in doc.Stages)
                foreach (var c in stage.Commodities.Where(x => x.IsUnpriced))
                    rec.Issues.Add(new ReconciliationIssue
                    {
                        Code = "R3",
                        StageId = stage.StageId,
                        CommodityKey = c.CommodityKey,
                        Message = $"'{c.Description}' ({c.OrderQuantity:N0} {c.SupplierUnit}) in "
                                + $"{stage.Title} has no rate. It will total zero in a priced schedule."
                    });
        }

        /// <summary>R4 — wastage can only add.</summary>
        private static void CheckQuantities(MaterialScheduleDocument doc, MaterialScheduleReconciliation rec)
        {
            foreach (var stage in doc.Stages)
                foreach (var c in stage.Commodities)
                {
                    if (c.OrderQuantity + 1e-9 < c.NetQuantity)
                        rec.Issues.Add(new ReconciliationIssue
                        {
                            Code = "R4",
                            StageId = stage.StageId,
                            CommodityKey = c.CommodityKey,
                            Message = $"'{c.Description}': order quantity {c.OrderQuantity:N2} is below the "
                                    + $"net measured {c.NetQuantity:N2}. Wastage can only add."
                        });

                    if (c.WastagePct < 0)
                        rec.Issues.Add(new ReconciliationIssue
                        {
                            Code = "R4",
                            StageId = stage.StageId,
                            CommodityKey = c.CommodityKey,
                            Message = $"'{c.Description}': negative wastage {c.WastagePct:N1}%."
                        });
                }
        }
    }
}
```

- [x] **Step 4: Add to the test project**

```xml
    <Compile Include="..\StingTools\Core\MaterialSchedule\Reconciler.cs" Link="MaterialSchedule\Reconciler.cs" />
```

- [x] **Step 5: Run to verify it passes**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo --filter "FullyQualifiedName~MaterialScheduleReconcilerTests"
```

Expected: `Passed! - Failed: 0, Passed: 7`.

- [x] **Step 6: Run the whole suite**

```bash
dotnet test StingTools.Boq.Tests/StingTools.Boq.Tests.csproj --nologo -v q
```

Expected: `Passed! - Failed: 0, Passed: 232` — the 196 baseline plus 36 new
(Task 4: 3, Task 5: 6, Task 6: 6, Task 7: 8, Task 8: 6, Task 9: 7).

- [x] **Step 7: Commit**

```bash
git add StingTools/Core/MaterialSchedule/Reconciler.cs StingTools.Boq.Tests/MaterialScheduleReconcilerTests.cs StingTools.Boq.Tests/StingTools.Boq.Tests.csproj
git commit -m "feat(material-schedule): reconciler pinning the PATMAC defect classes"
```

---

## Phase 2 — The Revit adapter

### Task 10: `MaterialScheduleBuilder`

**Files:**
- Create: `StingTools/BOQ/MaterialSchedule/MaterialScheduleBuilder.cs`

Not unit-testable (needs a `Document`); keep it thin — gather, delegate, return. All arithmetic already lives in the engine.

- [x] **Step 1: Read the two APIs this leans on**

```bash
sed -n '2586,2600p' StingTools/BOQ/BOQCostManager.cs      # LoadManualStore
grep -n "public static BOQDocument BuildBOQDocument" StingTools/BOQ/BOQCostManager.cs
```

Note the exact `BuildBOQDocument` signature — Step 3 must match it.

- [x] **Step 2: Write the builder**

Create `StingTools/BOQ/MaterialSchedule/MaterialScheduleBuilder.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleBuilder.cs — MAT-SCHED Revit-side gather.
//
//  Thin by design: read the BOQ, the manual store and the data files, hand
//  plain POCOs to the engine, return its document. No arithmetic here — every
//  number is computed in Core/MaterialSchedule where it is unit-tested.
//
//  C1: compound take-off is OFF by default (COST_COMPOUND_TAKEOFF). With it off
//  there are no constituent rows and the schedule would build EMPTY. We detect
//  that and report it rather than shipping a blank document.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.BOQ.MaterialSchedule
{
    internal sealed class MaterialScheduleBuildResult
    {
        public MaterialScheduleDocument Document;
        public bool CompoundTakeoffWasOff;
        public int ConstituentRowsSeen;
        public int RowsWithoutKind;
        public List<string> Warnings = new List<string>();
    }

    internal static class MaterialScheduleBuilder
    {
        public static MaterialScheduleBuildResult Build(Document doc, MaterialScheduleOptions options)
        {
            var result = new MaterialScheduleBuildResult();
            if (doc == null) { result.Document = new MaterialScheduleDocument(); return result; }

            result.CompoundTakeoffWasOff = !Takeoff.CompoundTakeoffBuilder.Enabled();

            var boq = BOQCostManager.BuildBOQDocument(doc);
            var inputs = new AggregatorInputs
            {
                Units = LoadUnits(doc),
                StageDefs = new List<StageDefinition>(),
                Options = options ?? new MaterialScheduleOptions()
            };

            var lib = LoadStages(doc);
            inputs.StageDefs = lib.Stages;
            inputs.DefaultStageId = lib.DefaultStageId;
            inputs.Rates = LoadRates(doc);

            foreach (var item in boq.AllItems.Where(i => i.Source == BOQRowSource.Model))
            {
                result.ConstituentRowsSeen++;
                if (string.IsNullOrWhiteSpace(item.ConstituentKind)) result.RowsWithoutKind++;

                inputs.Constituents.Add(new ConstituentInput
                {
                    ConstituentKind = item.ConstituentKind ?? "",
                    Category = item.Category ?? "",
                    Description = item.ItemName ?? "",
                    Unit = BoqUnits.Normalise(item.Unit),
                    Quantity = item.Quantity,
                    LevelCode = item.Level ?? "",
                    TraceRef = string.IsNullOrEmpty(item.BOQLineRef) ? item.Id : item.BOQLineRef
                });
            }

            var msDoc = CommodityAggregator.Build(inputs);
            msDoc.ProjectName = doc.ProjectInformation?.Name ?? "";
            msDoc.ProjectCode = doc.ProjectInformation?.Number ?? "";

            AppendManualRows(doc, msDoc, boq, result);
            Reconciler.Check(msDoc);

            result.Document = msDoc;
            if (result.CompoundTakeoffWasOff)
                result.Warnings.Add(
                    "Compound take-off is disabled (COST_COMPOUND_TAKEOFF). Walls and slabs were "
                  + "priced as single composite rates, so no cement / sand / block commodities were "
                  + "produced. Enable it in project config and re-run for a full material schedule.");
            if (result.RowsWithoutKind > 0)
                result.Warnings.Add(
                    $"{result.RowsWithoutKind} of {result.ConstituentRowsSeen} model rows carried no "
                  + $"constituent kind and were routed to the default stage.");

            StingLog.Info($"MaterialScheduleBuilder: {msDoc.Stages.Count} stage(s), "
                        + $"{msDoc.Stages.Sum(s => s.Commodities.Count)} commodity row(s), "
                        + $"{msDoc.Reconciliation.Issues.Count} reconciliation issue(s).");
            return result;
        }

        /// <summary>
        /// Tools become Manual rows; services become ProvisionalSum rows. Labour is a
        /// QS lump: the BOQ's L/P/M split is nulled on override and on modal-rate
        /// aggregation, so it is offered only as a SUGGESTION and only when every
        /// contributing row carries one.
        /// </summary>
        private static void AppendManualRows(Document doc, MaterialScheduleDocument msDoc,
            BOQDocument boq, MaterialScheduleBuildResult result)
        {
            try
            {
                var ps = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
                foreach (var group in ps.GroupBy(i => i.Category ?? ""))
                {
                    var section = msDoc.Stages.FirstOrDefault(s =>
                        s.Title.IndexOf(group.Key, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (section == null)
                    {
                        section = new StageSection { StageId = "ps-" + group.Key, Title = group.Key.ToUpperInvariant() };
                        msDoc.Stages.Add(section);
                    }
                    foreach (var row in group)
                        section.ProvisionalSums.Add(new ProvisionalSumLine
                        {
                            Description = string.IsNullOrEmpty(row.ResolvedNRM2Paragraph)
                                ? row.ItemName : row.ResolvedNRM2Paragraph,
                            AmountUGX = row.TotalUGX,
                            SourceRef = row.Id
                        });
                }

                foreach (var section in msDoc.Stages)
                {
                    var contributing = boq.AllItems
                        .Where(i => i.Source == BOQRowSource.Model)
                        .ToList();
                    bool allHaveSplit = contributing.Count > 0 && contributing.All(i => i.LabourUGX.HasValue);
                    var line = new LabourLine { Description = "Labour", AmountUGX = 0 };
                    if (allHaveSplit)
                    {
                        line.SuggestedUGX = contributing.Sum(i => i.LabourTotalUGX);
                        line.SuggestionBasis = $"{contributing.Count} of {contributing.Count} rows carry an L/P/M split";
                    }
                    else
                    {
                        line.SuggestionBasis = "no suggestion — not every contributing row carries an L/P/M split";
                    }
                    section.Labour.Add(line);
                }

                StageMapper.AssignLetters(msDoc.Stages);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaterialScheduleBuilder.AppendManualRows: {ex.Message}");
                result.Warnings.Add($"Manual / provisional-sum rows could not be appended: {ex.Message}");
            }
        }

        // ── data loading: corporate baseline, then project override ─────────

        private static SupplierUnitTable LoadUnits(Document doc)
        {
            var table = ReadJson<SupplierUnitTable>(StingToolsApp.FindDataFile("STING_SUPPLIER_UNITS.json"))
                        ?? new SupplierUnitTable();
            var over = ReadJson<SupplierUnitTable>(StingPaths.MetaFile(doc, "_BIM_COORD", "supplier_units.json"));
            if (over?.Rules != null)
                foreach (var r in over.Rules)
                {
                    table.Rules.RemoveAll(x => string.Equals(x.CommodityKey, r.CommodityKey, StringComparison.OrdinalIgnoreCase));
                    table.Rules.Add(r);
                }
            return table;
        }

        private static StageLibrary LoadStages(Document doc)
        {
            var lib = ReadJson<StageLibrary>(StingToolsApp.FindDataFile("STING_MATERIAL_STAGES.json"))
                      ?? new StageLibrary();
            var over = ReadJson<StageLibrary>(StingPaths.MetaFile(doc, "_BIM_COORD", "material_stages.json"));
            if (over != null)
            {
                if (!string.IsNullOrWhiteSpace(over.DefaultStageId)) lib.DefaultStageId = over.DefaultStageId;
                foreach (var s in over.Stages ?? new List<StageDefinition>())
                {
                    lib.Stages.RemoveAll(x => string.Equals(x.StageId, s.StageId, StringComparison.OrdinalIgnoreCase));
                    lib.Stages.Add(s);
                }
            }
            return lib;
        }

        private static CommodityRateResolver LoadRates(Document doc)
        {
            var baseline = new List<CommodityRate>();
            var project = new List<CommodityRate>();

            string basePath = StingToolsApp.FindDataFile("STING_COMMODITY_RATES.csv");
            if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
            {
                baseline = CommodityRateResolver.ParseCsv(File.ReadAllLines(basePath), out var skipped);
                foreach (string s in skipped) StingLog.Warn($"STING_COMMODITY_RATES.csv: unparsed row '{s}'");
            }

            string projPath = StingPaths.MetaFile(doc, "_BIM_COORD", "commodity_rates.csv");
            if (!string.IsNullOrEmpty(projPath) && File.Exists(projPath))
            {
                project = CommodityRateResolver.ParseCsv(File.ReadAllLines(projPath), out var skipped);
                foreach (string s in skipped) StingLog.Warn($"commodity_rates.csv: unparsed row '{s}'");
            }

            return new CommodityRateResolver(baseline, project);
        }

        private static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaterialScheduleBuilder.ReadJson '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
```

- [x] **Step 3: Build**

```bash
dotnet build StingTools/StingTools.csproj -c Debug --nologo
```

Expected: `0 Error(s)`. If `BuildBOQDocument` takes more than a `Document`, fix the call to match the real signature read in Step 1.

- [x] **Step 4: Commit**

```bash
git add StingTools/BOQ/MaterialSchedule/MaterialScheduleBuilder.cs
git commit -m "feat(material-schedule): Revit-side builder with compound-mode detection"
```

---

## Phase 3 — XLSX renderer and commands

### Task 11: `MaterialScheduleXlsxWriter`

**Files:**
- Create: `StingTools/BOQ/MaterialSchedule/MaterialScheduleXlsxWriter.cs`

- [ ] **Step 1: Write the renderer**

Create `StingTools/BOQ/MaterialSchedule/MaterialScheduleXlsxWriter.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleXlsxWriter.cs — MAT-SCHED ClosedXML renderer.
//
//  ShowPrices is a RENDERER flag: the engine computes identically either way,
//  so the priced and unpriced documents can never disagree about quantities.
//
//  Styling comes from BoqXlsxStyle so this workbook and the BOQ workbook are
//  visually one family.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using StingTools.Core.MaterialSchedule;

namespace StingTools.BOQ.MaterialSchedule
{
    internal static class MaterialScheduleXlsxWriter
    {
        public static void Write(MaterialScheduleDocument doc, string path)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));

            using (var wb = new XLWorkbook())
            {
                WriteScheduleSheet(wb.Worksheets.Add("Material Schedule"), doc);
                WriteSummarySheet(wb.Worksheets.Add("Summary"), doc);
                WriteValidationSheet(wb.Worksheets.Add("Validation"), doc);

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                wb.SaveAs(path);
            }
        }

        private static void WriteScheduleSheet(IXLWorksheet ws, MaterialScheduleDocument doc)
        {
            bool priced = doc.Options.ShowPrices;
            BoqXlsxStyle.BannerRow(ws,
                $"MATERIAL SCHEDULE — {doc.ProjectName}"
                + (priced ? "" : "  (QUANTITIES ONLY — NO PRICES)"));

            string[] cols = priced
                ? new[] { "Item", "Description", "Unit", "Net Qty", "Waste %", "Order Qty", "Rate UGX", "Amount UGX" }
                : new[] { "Item", "Description", "Unit", "Net Qty", "Waste %", "Order Qty" };

            int row = 3;
            foreach (var stage in doc.Stages)
            {
                // Stage heading
                var head = ws.Cell(row, 1);
                head.Value = $"{stage.Letter}    {stage.Title}";
                head.Style.Font.Bold = true;
                head.Style.Fill.BackgroundColor = BoqXlsxStyle.NavyFill;
                head.Style.Font.FontColor = XLColor.White;
                ws.Range(row, 1, row, cols.Length).Merge();
                row++;

                if (!string.IsNullOrWhiteSpace(stage.Preamble))
                {
                    var pre = ws.Cell(row, 1);
                    pre.Value = stage.Preamble;
                    pre.Style.Font.Italic = true;
                    ws.Range(row, 1, row, cols.Length).Merge();
                    row++;
                }

                BoqXlsxStyle.WriteHeader(ws, row, cols);
                row++;

                int item = 1;
                foreach (var c in stage.Commodities)
                {
                    ws.Cell(row, 1).Value = item++;
                    ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(c.Spec)
                        ? c.Description : $"{c.Description} — {c.Spec}";
                    ws.Cell(row, 3).Value = c.SupplierUnit;
                    ws.Cell(row, 4).Value = Math.Round(c.NetQuantity, 2);
                    ws.Cell(row, 5).Value = c.WastagePct;
                    ws.Cell(row, 6).Value = c.OrderQuantity;
                    if (priced)
                    {
                        ws.Cell(row, 7).Value = c.RateUGX;
                        // Live formula, not a baked number — the workbook stays
                        // arithmetically honest if a QS edits a rate downstream.
                        ws.Cell(row, 8).FormulaA1 = $"=F{row}*G{row}";
                        BoqXlsxStyle.MoneyFormat(ws.Range(row, 7, row, 8));
                        if (c.IsUnpriced)
                            ws.Range(row, 1, row, cols.Length).Style.Fill.BackgroundColor = BoqXlsxStyle.ManualRow;
                    }
                    row++;
                }

                foreach (var l in stage.Labour)
                {
                    ws.Cell(row, 2).Value = l.Description
                        + (l.SuggestedUGX.HasValue ? $"  (suggested {l.SuggestedUGX.Value:N0} — {l.SuggestionBasis})" : "");
                    ws.Cell(row, 2).Style.Font.Italic = true;
                    if (priced) { ws.Cell(row, 8).Value = l.AmountUGX; BoqXlsxStyle.MoneyFormat(ws.Range(row, 8, row, 8)); }
                    row++;
                }

                foreach (var p in stage.ProvisionalSums)
                {
                    ws.Cell(row, 2).Value = p.Description;
                    if (priced) { ws.Cell(row, 8).Value = p.AmountUGX; BoqXlsxStyle.MoneyFormat(ws.Range(row, 8, row, 8)); }
                    row++;
                }

                if (priced)
                {
                    var sub = ws.Cell(row, 2);
                    sub.Value = "Sub-Total carried to summary";
                    sub.Style.Font.Bold = true;
                    ws.Cell(row, 8).Value = stage.SubTotalUGX;
                    ws.Cell(row, 8).Style.Font.Bold = true;
                    BoqXlsxStyle.MoneyFormat(ws.Range(row, 8, row, 8));
                    row++;
                }
                row++;   // blank spacer between stages
            }

            foreach (var c in ws.ColumnsUsed()) c.AdjustToContents();
        }

        private static void WriteSummarySheet(IXLWorksheet ws, MaterialScheduleDocument doc)
        {
            bool priced = doc.Options.ShowPrices;
            BoqXlsxStyle.BannerRow(ws, priced ? "SUMMARY" : "SUMMARY — TOTAL QUANTITIES BY COMMODITY");

            if (priced)
            {
                BoqXlsxStyle.WriteHeader(ws, 3, new[] { "", "Element", "Amount UGX" });
                int row = 4;
                // Projected from the body — see MaterialScheduleDocument.Summary.
                foreach (var (letter, title, subtotal) in doc.Summary)
                {
                    ws.Cell(row, 1).Value = letter;
                    ws.Cell(row, 2).Value = title;
                    ws.Cell(row, 3).Value = subtotal;
                    BoqXlsxStyle.MoneyFormat(ws.Range(row, 3, row, 3));
                    row++;
                }
                row++;
                WriteTotal(ws, row++, "Sub-total", doc.WorksSubtotalUGX);
                WriteTotal(ws, row++, $"Add {doc.Options.ContingencyPct:N0}% contingency", doc.ContingencyUGX);
                WriteTotal(ws, row, "GRAND TOTAL", doc.GrandTotalUGX, bold: true);
            }
            else
            {
                BoqXlsxStyle.WriteHeader(ws, 3, new[] { "Commodity", "Unit", "Total Order Qty" });
                int row = 4;
                var rolled = doc.Stages.SelectMany(s => s.Commodities)
                    .GroupBy(c => new { c.CommodityKey, c.SupplierUnit })
                    .OrderBy(g => g.Key.CommodityKey);
                foreach (var g in rolled)
                {
                    ws.Cell(row, 1).Value = g.First().Description;
                    ws.Cell(row, 2).Value = g.Key.SupplierUnit;
                    ws.Cell(row, 3).Value = g.Sum(c => c.OrderQuantity);
                    row++;
                }
            }
            foreach (var c in ws.ColumnsUsed()) c.AdjustToContents();
        }

        private static void WriteTotal(IXLWorksheet ws, int row, string label, double value, bool bold = false)
        {
            ws.Cell(row, 2).Value = label;
            ws.Cell(row, 3).Value = value;
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 3).Style.Font.Bold = true;
            if (bold) ws.Range(row, 1, row, 3).Style.Fill.BackgroundColor = BoqXlsxStyle.HeaderFill;
            if (bold) ws.Range(row, 1, row, 3).Style.Font.FontColor = XLColor.White;
            BoqXlsxStyle.MoneyFormat(ws.Range(row, 3, row, 3));
        }

        private static void WriteValidationSheet(IXLWorksheet ws, MaterialScheduleDocument doc)
        {
            BoqXlsxStyle.BannerRow(ws, "VALIDATION");
            BoqXlsxStyle.WriteHeader(ws, 3, new[] { "Code", "Stage", "Commodity", "Issue" });

            int row = 4;
            foreach (var i in doc.Reconciliation.Issues)
            {
                ws.Cell(row, 1).Value = i.Code;
                ws.Cell(row, 2).Value = i.StageId;
                ws.Cell(row, 3).Value = i.CommodityKey;
                ws.Cell(row, 4).Value = i.Message;
                ws.Cell(row, 4).Style.Alignment.WrapText = true;
                row++;
            }
            if (doc.Reconciliation.IsClean)
                ws.Cell(4, 1).Value = "No reconciliation issues. Rates are consistent, "
                                    + "sections are correctly lettered, and every commodity is priced.";

            ws.Column(4).Width = 90;
            foreach (var c in ws.Columns(1, 3)) c.AdjustToContents();
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build StingTools/StingTools.csproj -c Debug --nologo
```

Expected: `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add StingTools/BOQ/MaterialSchedule/MaterialScheduleXlsxWriter.cs
git commit -m "feat(material-schedule): branded XLSX renderer with prices toggle"
```

---

### Task 12: Commands and registration (C7)

**Files:**
- Create: `StingTools/Commands/MaterialSchedule/MaterialScheduleCommands.cs`
- Create: `StingTools/UI/Modules/MaterialScheduleCommandModule.cs`
- Modify: `StingTools/UI/CommandRegistry.cs`

- [ ] **Step 1: Write the export command**

Create `StingTools/Commands/MaterialSchedule/MaterialScheduleCommands.cs`:

```csharp
// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleCommands.cs — MAT-SCHED entry points.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ.MaterialSchedule;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.Commands.MaterialSchedule
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaterialScheduleExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                // Prices in or out.
                var priceDlg = new TaskDialog("Material Schedule")
                {
                    MainInstruction = "Include prices?",
                    MainContent = "A priced schedule carries Rate, Amount, contingency and a grand total. "
                                + "A quantities-only schedule is a buy-list for the site team.",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                priceDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Priced schedule",
                    "Quantities plus rates, amounts, contingency and grand total.");
                priceDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Quantities only",
                    "Commodities, units and order quantities. No money.");
                var choice = priceDlg.Show();
                if (choice == TaskDialogResult.Cancel) return Result.Cancelled;

                var options = new MaterialScheduleOptions
                {
                    ShowPrices = choice == TaskDialogResult.CommandLink1,
                    ContingencyPct = 5.0
                };

                var built = MaterialScheduleBuilder.Build(doc, options);
                var msDoc = built.Document;

                if (msDoc.Stages.Count == 0)
                {
                    TaskDialog.Show("Material Schedule",
                        "No material commodities were produced.\n\n"
                        + string.Join("\n\n", built.Warnings));
                    return Result.Cancelled;
                }

                // Reconciliation gate — skippable, mirroring the BOQ coverage gate.
                if (!msDoc.Reconciliation.IsClean)
                {
                    var issues = msDoc.Reconciliation.Issues;
                    var gate = new TaskDialog("Material Schedule — reconciliation")
                    {
                        MainInstruction = $"{issues.Count} reconciliation issue(s) found",
                        MainContent = string.Join("\n", issues.Take(6).Select(i => $"• [{i.Code}] {i.Message}"))
                                    + (issues.Count > 6 ? $"\n… and {issues.Count - 6} more." : "")
                                    + "\n\nAll issues are listed on the Validation sheet.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                        DefaultButton = TaskDialogResult.Cancel
                    };
                    gate.VerificationText = "Export anyway";
                    if (gate.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                }

                string path = StingPaths.ExportFile(doc, "MaterialSchedule",
                    $"MaterialSchedule_{msDoc.ProjectCode}", ".xlsx");
                MaterialScheduleXlsxWriter.Write(msDoc, path);

                string warn = built.Warnings.Count > 0
                    ? "\n\nWarnings:\n" + string.Join("\n", built.Warnings.Select(w => "• " + w))
                    : "";
                TaskDialog.Show("Material Schedule",
                    $"{msDoc.Stages.Count} stage(s), "
                  + $"{msDoc.Stages.Sum(s => s.Commodities.Count)} commodity row(s).\n"
                  + (options.ShowPrices ? $"Grand total: UGX {msDoc.GrandTotalUGX:N0}\n" : "")
                  + $"\n{path}{warn}");

                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                }
                catch (Exception ex) { StingLog.Warn($"Open material schedule xlsx: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MaterialScheduleExportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
```

- [ ] **Step 2: Write the registry module**

Create `StingTools/UI/Modules/MaterialScheduleCommandModule.cs`:

```csharp
// MAT-SCHED: command registry module for material-schedule button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class MaterialScheduleCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            registry.Register("MaterialSchedule_Export",
                app => StingCommandHandler.RunCommandPublic<Commands.MaterialSchedule.MaterialScheduleExportCommand>(app));
        }
    }
}
```

- [ ] **Step 3: Yield the module**

In `StingTools/UI/CommandRegistry.cs`, inside `EnumerateModules()`, after the `Modules.HealthcareCommandModule()` line, add:

```csharp
            yield return new Modules.MaterialScheduleCommandModule();
```

- [ ] **Step 4: Build**

```bash
dotnet build StingTools/StingTools.csproj -c Debug --nologo
```

Expected: `0 Error(s)`.

- [ ] **Step 5: Verify the tag dispatches**

```bash
pwsh -File tools/check_workflow_wiring.ps1
```

Expected: no new silent-button or unreachable-command findings. If the script reports `MaterialSchedule_Export` as a command with no button, that is expected until a button is added to the XAML — record it and continue.

- [ ] **Step 6: Commit**

```bash
git add StingTools/Commands/MaterialSchedule/ StingTools/UI/Modules/MaterialScheduleCommandModule.cs StingTools/UI/CommandRegistry.cs
git commit -m "feat(material-schedule): export command with prices toggle and reconciliation gate"
```

---

### Task 13: Add the panel button

**Files:**
- Modify: `StingTools/UI/StingDockPanel.xaml`

- [ ] **Step 1: Find where the BOQ export button lives**

```bash
grep -n "BOQExport\|BOQ_Export\|Tag=\"BOQ" StingTools/UI/StingDockPanel.xaml | head
```

- [ ] **Step 2: Add the button next to it**

Copy the *exact* markup of the neighbouring BOQ button — same `Style`, `Margin`, `Width` — changing only:

```xml
<Button Content="Material Schedule" Tag="MaterialSchedule_Export" Click="Cmd_Click"
        ToolTip="Export a stage-sectioned material schedule in supplier units (XLSX)." />
```

Do not invent a new style; match the sibling exactly.

- [ ] **Step 3: Build**

```bash
dotnet build StingTools/StingTools.csproj -c Debug --nologo
```

Expected: `0 Error(s)`. XAML errors surface here, not at runtime.

- [ ] **Step 4: Commit**

```bash
git add StingTools/UI/StingDockPanel.xaml
git commit -m "feat(material-schedule): add the export button to the dock panel"
```

---

### Task 14: End-to-end verification in Revit

Nothing below this line is provable headlessly. Do it before opening a PR.

- [ ] **Step 1: Confirm where Revit loads the plugin from**

```bash
grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
```

Copying anywhere else fails silently.

- [ ] **Step 2: Close Revit and the Planscape Companion tray app, then deploy**

```bash
cmd.exe /c deploy.bat
```

- [ ] **Step 3: Open a test model with modelled walls and slabs, and enable compound take-off**

Set `COST_COMPOUND_TAKEOFF` to `1` in the project config. Without it the command will warn and produce no commodities — which is itself worth confirming once (it is the C1 path).

- [ ] **Step 4: Run the command both ways**

Click **Material Schedule** → choose **Priced schedule**. Then re-run → **Quantities only**.

Confirm:
- the workbook opens with `Material Schedule`, `Summary` and `Validation` sheets
- section letters run `A`, `B`, `C`… with no duplicates
- the Summary letters and order match the body exactly
- units read `Bags` / `Trips (Sino Truck)` / `No.`, not `m3` / `bag`
- the unpriced run has no Rate, Amount, contingency or grand total
- both runs report the **same order quantities**
- the file landed under the project's `SCHEDULES` folder, not `MISC`

- [ ] **Step 5: Record the result**

Append a `#### Completed (Phase N — Material Schedule export)` entry to `docs/CHANGELOG.md` stating what was verified in Revit and what was not.

- [ ] **Step 6: Commit**

```bash
git add docs/CHANGELOG.md
git commit -m "docs(changelog): record material schedule export runtime verification"
```

---

## Phase 4 — Revit schedule views (isolated; may fail without blocking anything)

### Task 15: Key-schedule spike (C5)

`ViewSchedule.CreateKeySchedule` is used **nowhere** in this codebase, and key-schedule columns are project parameters bound to the key category — so the builder must create parameters before writing a row. Prove it before planning against it.

**Timebox: 2 hours.** Nothing downstream depends on the outcome.

- [ ] **Step 1: Write a throwaway command**

Create `StingTools/Commands/MaterialSchedule/KeyScheduleSpikeCommand.cs` — a `[Transaction(TransactionMode.Manual)]` `IExternalCommand` that, in one transaction, attempts:

1. `ViewSchedule.CreateKeySchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel))`
2. bind a text project parameter and a number project parameter to the key category
3. add both as schedule fields
4. create three key rows and set their values
5. place the schedule on a new sheet via `ScheduleSheetInstance.Create`

Report each step's success or the exact exception through `TaskDialog` and `StingLog.Info`.

- [ ] **Step 2: Run it in Revit and record the outcome**

Write the findings — which steps succeeded, exact exception text for any that failed — into `docs/superpowers/specs/2026-08-16-material-schedule-export-design.md` under a new `### 9.3 Key-schedule spike result` heading.

- [ ] **Step 3: Decide and record**

- **All five steps pass** → proceed to Task 16 as written.
- **Any step fails** → record the failure, delete the spike command, and implement the named fallback instead: a Generic Annotation family carrying the commodity fields as type parameters, one type per commodity row, scheduled normally.

- [ ] **Step 4: Commit the finding**

```bash
git add docs/superpowers/specs/2026-08-16-material-schedule-export-design.md
git commit -m "docs(material-schedule): record the key-schedule API spike result"
```

---

### Task 16: `MaterialScheduleViewBuilder`

**Do not start this task until Task 15 has recorded a result.** Its implementation depends on which branch the spike selected, so writing code for it now would be guessing.

**Files:**
- Create: `StingTools/BOQ/MaterialSchedule/MaterialScheduleViewBuilder.cs`
- Modify: `StingTools/Commands/MaterialSchedule/MaterialScheduleCommands.cs` (add `MaterialScheduleViewsCommand`)
- Modify: `StingTools/UI/Modules/MaterialScheduleCommandModule.cs` (register `MaterialSchedule_CreateViews`)

Requirements that hold whichever branch the spike selects:

- **Idempotent.** Re-running replaces the previously generated views rather than appending duplicates. Key off a name prefix — `STING Material Schedule — <Stage>`.
- **Visibly a snapshot.** The schedule name carries the generation timestamp so a stale table cannot be mistaken for a live one.
- **One transaction**, named `"STING Material Schedule — create views"`.
- **Never a silent catch.** Every failure logs through `StingLog` and surfaces in the command's result dialog.

---

## Self-review

**Spec coverage.** Every section maps to a task: §5 architecture → Tasks 4-11; §6 model → Task 4; §7 stages → Task 7 (with C1/C2 in Tasks 1 and 10); §8 reconciliation → Task 9; §9.1 XLSX → Task 11; §9.2 views → Tasks 15-16; §10 commands → Tasks 12-13; §11 testing → Tasks 4-9; §4.1 supplier units → Task 5; §4.2 commodity rates → Task 6; §4.3 findings C1-C8 → Tasks 1, 2, 3, 6, 7, 10, 12, 15. §12 out-of-scope items have no tasks, correctly.

**Type consistency.** `MaterialCommodity`, `StageSection`, `LabourLine`, `ProvisionalSumLine`, `MaterialScheduleOptions`, `MaterialScheduleDocument`, `ReconciliationIssue`, `MaterialScheduleReconciliation` are defined once in Task 4 and used unchanged thereafter. `SupplierUnitRule` / `SupplierUnitTable` / `SupplierUnitResult` (Task 5), `CommodityRate` / `CommodityRateResolver` (Task 6), `StageDefinition` / `StageLibrary` (Task 7), `ConstituentInput` / `AggregatorInputs` (Task 8) likewise. `StageMapper.ToLetter` is defined in Task 7 and consumed by `Reconciler` in Task 9.

**Known soft spots, called out rather than hidden:**

1. **Task 10's provisional-sum grouping matches stage titles by substring.** It works for the shipped stage titles but is not robust to a project that renames its stages. Left deliberately simple; if it misfires in Revit (Task 14), replace the substring match with an explicit `category → stageId` map in `STING_MATERIAL_STAGES.json`.
2. **Task 10's labour suggestion tests every model row rather than the rows contributing to that stage**, so the same suggestion appears on every section. Correct behaviour needs per-stage trace-ref filtering. Since labour is a QS-entered lump and the suggestion is advisory, this is acceptable for a first pass — but it must not be presented to the user as a per-stage figure until it is one. The `SuggestionBasis` string makes the basis visible, which is why it exists.
3. **Task 11 writes a live `=F*G` formula for Amount** while the model derives it. They agree on generation; if a QS edits a rate in Excel the formula updates and the model does not. That is intended — the workbook is the QS's to edit — but re-importing an edited workbook is not supported and is not in scope.
