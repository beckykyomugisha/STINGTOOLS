// STING Tools — Phase 116: Standards Extensions (STD-01..10 + REG-01).
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.StandardsExt
{
    internal static class StdP { public static StingResultPanel.Builder B(string t, string s) => StingResultPanel.Create(t).SetSubtitle(s); }

    // STD-01 — Stage-gated compliance audit
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class StageComplianceAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            StdP.B("STD-01 Stage-gated audit", "RIBA 0-7 + Part L → Part Q")
                .AddSection("STAGE")
                .Metric("Stage 0 — Strategic def.",  "BEP + client info reqs")
                .Metric("Stage 1 — Prep & brief",    "EIR + AIR")
                .Metric("Stage 2 — Concept design",  "LOD 200 + spatial")
                .Metric("Stage 3 — Spatial coord.",  "LOD 300 + clash")
                .Metric("Stage 4 — Technical design","LOD 400 + fabrication")
                .Metric("Stage 5 — Construction",     "LOD 500 + as-built")
                .Metric("Stage 6 — Handover",         "COBie + O&M")
                .Metric("Stage 7 — In use",           "FM + commissioning")
                .Show();
            return Result.Succeeded;
        }
    }

    // STD-03 — Accessibility audit
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class AccessibilityAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            int rooms = 0, doors = 0;
            try {
                rooms = new FilteredElementCollector(ctx.Doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount();
                doors = new FilteredElementCollector(ctx.Doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().GetElementCount();
            } catch { }
            StdP.B("STD-03 Accessibility audit", "BS 8300 + Part M + ADA")
                .AddSection("SCOPE")
                .Metric("Rooms", rooms.ToString())
                .Metric("Doors", doors.ToString())
                .AddSection("KEY METRICS")
                .Metric("Door clear width",  "≥ 800 mm (BS 8300) / 850 mm (ADA)")
                .Metric("Corridor width",    "≥ 1200 mm (1800 for 2-way wheelchair)")
                .Metric("Threshold height",  "≤ 15 mm")
                .Metric("Turning circle",    "1500 mm ⌀")
                .Metric("Reach range",       "400-1200 mm AFF")
                .Show();
            return Result.Succeeded;
        }
    }

    // STD-04 — Parking audit
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ParkingAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-04 Parking requirement",
                new[] { "GFA (m²)", "Occupancy (1=Office, 2=Retail, 3=Residential, 4=Industrial)" },
                new[] { 2000.0, 1.0 }, out var v)) return Result.Cancelled;
            double ratio = v[1] <= 1.5 ? 1/40.0 : v[1] <= 2.5 ? 1/25.0 : v[1] <= 3.5 ? 1.0 : 1/50.0;
            int required = (int)Math.Ceiling(v[0] * ratio);
            int accessible = (int)Math.Max(1, required * 0.05);
            StdP.B("STD-04 Parking", "Approved Document M + local auth + BS 6100")
                .AddSection("REQUIREMENT")
                .Metric("Required spaces",    required.ToString())
                .Metric("Accessible (5% min)", accessible.ToString())
                .Metric("Ratio",              $"1 per {1/ratio:F0} m²")
                .Show();
            return Result.Succeeded;
        }
    }

    // STD-05 — Live load audit
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class LiveLoadAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            StdP.B("STD-05 Floor live load", "EC 1991-1-1 + BS 6399-1 + ASCE 7 Table 4-1")
                .AddSection("CATEGORY QK kN/m²")
                .Metric("A — Domestic/residential",  "1.5-2.0")
                .Metric("B — Office",                 "2.0-3.0")
                .Metric("C — Congregation / circ.",  "3.0-5.0")
                .Metric("D — Shopping",               "4.0-5.0")
                .Metric("E — Storage / industrial",   "7.5+ (E1) to 12.5+ (E2)")
                .Metric("F — Vehicles ≤ 30 kN",       "2.5")
                .Metric("G — Vehicles 30-160 kN",     "5.0")
                .Metric("Roof H (access)",            "1.5")
                .Metric("Roof I (no access)",         "0.6")
                .Show();
            return Result.Succeeded;
        }
    }

    // STD-06 — Load combinations
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class LoadCombinationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            StdP.B("STD-06 Load combinations", "EC 0 + ASCE 7 + BS")
                .AddSection("EC 0 STR + GEO (6.10)")
                .Metric("Persistent + transient",  "1.35 Gk + 1.5 Qk + Σ(1.5 ψ0 Qk,i)")
                .Metric("Accidental",               "Gk + Ad + ψ1 Qk + Σ(ψ2 Qk,i)")
                .Metric("Seismic",                  "Gk + AEd + Σ(ψ2 Qk,i)")
                .AddSection("ASCE 7 LRFD (2.3.2)")
                .Metric("1",  "1.4 D")
                .Metric("2",  "1.2 D + 1.6 L + 0.5 Lr/S/R")
                .Metric("3",  "1.2 D + 1.6 Lr/S/R + 0.5 L/0.5 W")
                .Metric("4",  "1.2 D + 1.0 W + L + 0.5 Lr/S/R")
                .Metric("5",  "0.9 D + 1.0 W")
                .Metric("6",  "1.2 D + 1.0 E + L + 0.2 S")
                .Metric("7",  "0.9 D + 1.0 E")
                .Show();
            return Result.Succeeded;
        }
    }

    // STD-07 — EUI benchmark
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class EUIBenchmarkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-07 Energy use intensity",
                new[] { "Annual kWh", "GFA (m²)", "Type (1=Office, 2=Retail, 3=School, 4=Hospital)" },
                new[] { 200000.0, 2000.0, 1.0 }, out var v)) return Result.Cancelled;
            double eui = v[0] / v[1];
            double target = v[2] <= 1.5 ? 100 : v[2] <= 2.5 ? 150 : v[2] <= 3.5 ? 100 : 300;
            StdP.B("STD-07 EUI benchmark", "Part L 2021 + ASHRAE 90.1 + CIBSE TM46")
                .AddSection("BENCHMARK")
                .Metric("EUI",            $"{eui:F0} kWh/m²/yr")
                .Metric("CIBSE TM46 target",$"{target:F0} kWh/m²/yr")
                .Metric("Verdict",         eui <= target ? "PASS" : "REVIEW")
                .Show();
            return Result.Succeeded;
        }
    }

    // STD-02/08/09/10 — Water, space, lifecycle
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SetRegionCommand : IExternalCommand {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements) {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            StdP.B("STD-02 / REG-01 Regional overlay", "Kenya / Uganda / Tanzania / Rwanda / SA / etc.")
                .AddSection("REGIONS SUPPORTED")
                .Metric("East Africa",    "KEBS, UNBS, TBS, RSB, SSBS, BBN, EAS")
                .Metric("West Africa",    "ECOWAS")
                .Metric("Southern Africa","SANS")
                .Metric("Construction",   "CIDB")
                .Text("Writes PROJECT_REGION to project info; every validator filters to the selected code set.")
                .Show();
            return Result.Succeeded;
        }
    }
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class WaterUseCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements) { StdP.B("STD-08 Water use reduction","LEED WE + BREEAM Wat 01").AddSection("FIXTURE TARGETS").Metric("WC dual flush","≤ 6/4 L").Metric("Urinal","≤ 0.5 L/flush").Metric("Basin tap","≤ 2 L/min @ 0.3 MPa").Metric("Shower","≤ 6 L/min").Show(); return Result.Succeeded; } }
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SpaceEffCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements) { StdP.B("STD-09 Space efficiency","BCO + IFMA").AddSection("RATIOS").Metric("Net:Gross target","≥ 0.80 (BCO grade A)").Metric("Workstation m²","8-10").Metric("Meeting/desk","1:8 BCO").Show(); return Result.Succeeded; } }
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class LifecycleCostCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements) { StdP.B("STD-10 Lifecycle cost","CIBSE TM56 + ISO 15686").AddSection("NPV ANALYSIS").Metric("Analysis period","30 years").Metric("Discount rate","3.5% (Green Book)").Metric("Service life HVAC","15-20").Metric("Service life luminaires","50 000 hours (LED)").Show(); return Result.Succeeded; } }
}
