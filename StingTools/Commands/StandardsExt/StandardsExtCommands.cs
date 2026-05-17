// STING Tools — Phase 116: Standards Extensions (STD-01..10 + REG-01).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.Select;
using StingTools.Standards;
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
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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

    // STD-04 — Parking audit (AECCalculations.CalculateParkingRequirements)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ParkingAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-04 Parking requirement",
                new[] { "GFA (m²)", "Type (1=Office,2=Retail,3=Restaurant,4=Hotel,5=Residential-Multi,6=Warehouse)" },
                new[] { 2000.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                string useType = v[1] <= 1.5 ? "Office" : v[1] <= 2.5 ? "Retail"
                                : v[1] <= 3.5 ? "Restaurant" : v[1] <= 4.5 ? "Hotel"
                                : v[1] <= 5.5 ? "Residential-Multi" : "Warehouse";
                var res = AECCalculations.CalculateParkingRequirements(v[0] * 10.7639, useType);
                StdP.B("STD-04 Parking", $"{ProjectStandardsManager.Instance.Region} · {res.StandardReference ?? "ITE / Approved Doc M"}")
                    .AddSection("REQUIREMENT")
                    .Metric("Total spaces",        res.TotalSpaces.ToString())
                    .Metric("Standard spaces",     res.StandardSpaces.ToString())
                    .Metric("Accessible spaces",   res.AccessibleSpaces.ToString())
                    .Metric("Van accessible",      res.VanAccessibleSpaces.ToString())
                    .Metric("Ratio",                $"{res.ParkingRatio:F2} {res.RatioBasis}")
                    .Metric("Use type",             res.UseType ?? useType)
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("ParkingAudit", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // STD-05 — Live load (AECCalculations.CalculateFloorLiveLoad)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class LiveLoadAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-05 Live load",
                new[] { "Type (1=Office,2=Residential,3=Hotel-Public,4=Assembly-Fixed,5=Assembly-Movable)", "Tributary area (m²)" },
                new[] { 1.0, 50.0 }, out var v)) return Result.Cancelled;
            try
            {
                string occ = v[0] <= 1.5 ? "Office" : v[0] <= 2.5 ? "Residential"
                            : v[0] <= 3.5 ? "Hotel-Public"
                            : v[0] <= 4.5 ? "Assembly-Fixed Seats" : "Assembly-Movable Seats";
                var res = AECCalculations.CalculateFloorLiveLoad(occ, true, v[1] * 10.7639);
                StdP.B("STD-05 Floor live load", $"{ProjectStandardsManager.Instance.Region} · {res.StandardReference ?? "ASCE 7-22 Table 4.3-1"}")
                    .AddSection("LIVE LOAD")
                    .Metric("Uniform",            $"{res.UniformLoadKPa:F2} kN/m² ({res.UniformLoadPSF:F0} psf)")
                    .Metric("Reduced",             $"{res.ReducedLoadKPa:F2} kN/m² ({res.ReducedLoadPSF:F0} psf)")
                    .Metric("Concentrated",        $"{res.ConcentratedLoadKN:F1} kN ({res.ConcentratedLoadLbs:F0} lb)")
                    .Metric("Reduction factor",   $"{res.ReductionFactor:F2}")
                    .Metric("Tributary area",      $"{res.TributaryArea:F0} ft² ({v[1]:F0} m²)")
                    .Metric("Occupancy",           res.OccupancyType ?? occ)
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("LiveLoad", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // STD-06 — Load combinations (AECCalculations.CalculateLoadCombinations)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class LoadCombinationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-06 Load combinations",
                new[] { "Dead (kips)", "Live (kips)", "Roof live (kips)", "Snow (kips)", "Wind (kips)", "Seismic (kips)" },
                new[] { 100.0, 60.0, 20.0, 15.0, 30.0, 25.0 }, out var v)) return Result.Cancelled;
            try
            {
                var res = AECCalculations.CalculateLoadCombinations(v[0], v[1], v[2], v[3], v[4], v[5]);
                var sec = StdP.B("STD-06 Load combinations", $"{ProjectStandardsManager.Instance.Region} · {res.StandardReference ?? "ASCE 7-22 §2.3"}")
                    .AddSection($"{res.DesignMethod ?? "LRFD"} COMBINATIONS");
                if (res.Combinations != null)
                    foreach (var kv in res.Combinations) sec.Metric(kv.Key, $"{kv.Value:F1} kips");
                sec.AddSection("GOVERNING")
                   .Metric("Combination", res.GoverningCombination ?? "-")
                   .Metric("Load",         $"{res.GoverningLoadKips:F1} kips ({res.GoverningLoadKN:F1} kN)")
                   .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("LoadCombos", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // STD-07 — EUI benchmark (AECCalculations.CalculateEUI)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class EUIBenchmarkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-07 Energy use intensity",
                new[] { "Annual kWh", "GFA (m²)", "Type (1=Office,2=Retail,3=School,4=Hospital,5=Hotel)" },
                new[] { 200000.0, 2000.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                string type = v[2] <= 1.5 ? "Office" : v[2] <= 2.5 ? "Retail"
                            : v[2] <= 3.5 ? "School K-12" : v[2] <= 4.5 ? "Hospital" : "Hotel";
                var res = AECCalculations.CalculateEUI(v[0], v[1] * 10.7639, type);
                StdP.B("STD-07 EUI benchmark", $"{ProjectStandardsManager.Instance.Region} · {res.StandardReference ?? "ENERGY STAR"}")
                    .AddSection("BENCHMARK")
                    .Metric("EUI",            $"{res.EUIKWhPerM2:F0} kWh/m²/yr ({res.EUIKBtuPerSqFt:F1} kBtu/ft²)")
                    .Metric("Baseline",       $"{res.BaselineEUI:F1} kBtu/ft²")
                    .Metric("vs baseline",    $"{res.PercentBetterThanBaseline:F1}% better")
                    .Metric("LEED EAp2 (est.)", res.EstimatedLEEDPoints.ToString())
                    .Metric("Verdict",         res.PercentBetterThanBaseline > 0 ? "PASS" : "REVIEW")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("EUIBench", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // STD-02 / REG-01 — Regional overlay (active driver of every Standards command)
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class SetRegionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }

            var mgr = ProjectStandardsManager.Instance;
            var regions = mgr.GetAvailableRegions().ToList();
            string current = mgr.Region ?? "International";

            string picked = StingListPicker.Show(
                "STING Standards — Active region",
                $"Current: {current}. Pick a region to switch electrical / HVAC / structural / fire / lighting / energy bindings.",
                regions);
            if (string.IsNullOrEmpty(picked)) return Result.Cancelled;

            try { mgr.ApplyRegionalPreset(picked); }
            catch (Exception ex) { StingLog.Error("ApplyRegionalPreset failed", ex); message = ex.Message; return Result.Failed; }

            // Persist on the Revit document so the choice travels with the .rvt.
            // Two channels: PROJECT_REGION on ProjectInformation (preferred —
            // visible in schedules) and a sidecar JSON next to the .rvt
            // (resilient fallback when the shared parameter isn't bound).
            bool paramWritten = false;
            try
            {
                var pi = ctx.Doc.ProjectInformation;
                if (pi != null)
                {
                    using (var t = new Transaction(ctx.Doc, "STING Set Region"))
                    {
                        t.Start();
                        var p = pi.LookupParameter("PROJECT_REGION");
                        if (p != null && !p.IsReadOnly)
                        {
                            p.Set(picked);
                            paramWritten = true;
                        }
                        t.Commit();
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PROJECT_REGION write skipped: {ex.Message}"); }
            bool sidecarWritten = ProjectRegionSidecar.Write(ctx.Doc, picked);

            var summary = mgr.GetConfigurationSummary();
            var panel = StingResultPanel.Create("Active region updated").SetSubtitle(picked);
            var sec = panel.AddSection("STANDARDS BINDING");
            foreach (var kv in summary.Standards) sec.Metric(kv.Key, kv.Value);
            sec.Metric("Unit system", summary.UnitSystem.ToString());
            var persist = panel.AddSection("PERSISTENCE");
            persist.Metric("PROJECT_REGION param", paramWritten ? "written" : "NOT BOUND — load shared params to enable");
            persist.Metric("Project sidecar",      sidecarWritten ? "written" : "FAILED (unsaved doc?)");
            persist.Metric("User store",            $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\StingTools\\config\\project-standards.json");
            if (!paramWritten)
                panel.Text("Tip: PROJECT_REGION isn't bound to Project Information on this project. The region is still saved to the per-project sidecar (sting_region.json) and the per-user appdata store, so document-open sync will recover it. Bind the shared parameter to surface the value in schedules and title blocks.");
            panel.Show();
            return Result.Succeeded;
        }
    }
    // STD-08 — Water use reduction (AECCalculations.CalculateWaterUseReduction)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class WaterUseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-08 Water use reduction (LEED WE / BREEAM Wat 01)",
                new[] { "Occupants", "Workdays/yr", "WC GPF", "Urinal GPF", "Lavatory GPM", "Shower GPM" },
                new[] { 100.0, 250.0, 1.28, 0.5, 0.5, 1.5 }, out var v)) return Result.Cancelled;
            try
            {
                var res = AECCalculations.CalculateWaterUseReduction(
                    occupants: (int)v[0], workdaysPerYear: (int)v[1],
                    toiletGPF: v[2], urinalGPF: v[3],
                    lavatoryGPM: v[4], showerGPM: v[5]);
                StdP.B("STD-08 Water use reduction", $"{ProjectStandardsManager.Instance.Region} · {res.StandardReference ?? "LEED WE Prereq"}")
                    .AddSection("REDUCTION")
                    .Metric("Baseline",        $"{res.BaselineGallonsPerYear:F0} gal/yr")
                    .Metric("Design",          $"{res.DesignGallonsPerYear:F0} gal/yr")
                    .Metric("Reduction",       $"{res.ReductionPercent:F1}%")
                    .Metric("Annual savings",  $"{res.AnnualSavingsGallons:F0} gal")
                    .Metric("LEED points",      res.EstimatedLEEDPoints.ToString())
                    .Metric("Prereq met",       res.MeetsPrerequisite ? "yes" : "REVIEW")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("WaterUse", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // STD-09 — Space efficiency (AECCalculations.CalculateSpaceEfficiency)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SpaceEffCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("STD-09 Space efficiency (BOMA / IFMA)",
                new[] { "Gross (m²)", "Usable (m²)", "Rentable (m²)", "Occupants", "Workstations" },
                new[] { 2000.0, 1700.0, 1850.0, 150.0, 130.0 }, out var v)) return Result.Cancelled;
            try
            {
                var res = AECCalculations.CalculateSpaceEfficiency(
                    grossFloorAreaSqFt: v[0] * 10.7639,
                    usableAreaSqFt:    v[1] * 10.7639,
                    rentableAreaSqFt:  v[2] * 10.7639,
                    totalOccupants:    (int)v[3],
                    workstations:      (int)v[4]);
                StdP.B("STD-09 Space efficiency", $"{ProjectStandardsManager.Instance.Region} · {res.StandardReference ?? "BOMA / IFMA"}")
                    .AddSection("EFFICIENCY")
                    .Metric("Usable / gross",   $"{res.EfficiencyRatio:F1}%")
                    .Metric("Load factor",      $"{res.LoadFactor:F2}")
                    .Metric("ft² / person",     $"{res.SqFtPerPerson:F0}")
                    .Metric("ft² / workstation", $"{res.SqFtPerWorkstation:F0}")
                    .Metric("Rating",           res.EfficiencyRating ?? "-")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("SpaceEff", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // STD-10 — Equipment lifecycle (AECCalculations.CalculateEquipmentLifecycle)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class LifecycleCostCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message = "No doc"; return Result.Failed; }
            // Names below match the ASHRAE service-life lookup in
            // AECCalculations.CalculateEquipmentLifecycle (Boiler-Cast Iron / -Steel,
            // Chiller-Centrifugal / -Screw, Pump-Base Mounted / -Inline, etc.).
            // Anything not in that lookup falls through to a 20 yr default.
            if (!NumericPrompt.TryAsk("STD-10 Equipment lifecycle (CIBSE TM56 / ISO 15686)",
                new[] { "Type (1=AHU, 2=Chiller-Centrifugal, 3=Boiler-Cast Iron, 4=Pump-Base Mounted, 5=Cooling Tower, 6=Transformer-Dry)", "Current age (years)", "Replacement cost ($)", "Annual maintenance ($)" },
                new[] { 1.0, 8.0, 50000.0, 2000.0 }, out var v)) return Result.Cancelled;
            try
            {
                string type = v[0] <= 1.5 ? "AHU"
                           : v[0] <= 2.5 ? "Chiller-Centrifugal"
                           : v[0] <= 3.5 ? "Boiler-Cast Iron"
                           : v[0] <= 4.5 ? "Pump-Base Mounted"
                           : v[0] <= 5.5 ? "Cooling Tower"
                           : "Transformer-Dry";
                var res = AECCalculations.CalculateEquipmentLifecycle(type, (int)v[1], v[2], v[3]);
                StdP.B("STD-10 Lifecycle cost", $"{ProjectStandardsManager.Instance.Region} · {res.StandardReference ?? "ISO 15686 + CIBSE TM56"}")
                    .AddSection("LIFECYCLE")
                    .Metric("Equipment type",     res.EquipmentType ?? type)
                    .Metric("Expected life",      $"{res.ExpectedLifeYears} years")
                    .Metric("Current age",        $"{res.CurrentAgeYears} years")
                    .Metric("Remaining life",     $"{res.RemainingLifeYears} years")
                    .Metric("% life used",        $"{res.PercentLifeUsed:F0}%")
                    .Metric("Replacement priority", res.ReplacementPriority ?? "-")
                    .Metric("Replacement cost",    $"${res.ReplacementCost:F0}")
                    .Metric("Annualised cost",     $"${res.AnnualizedCost:F0}/yr")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("Lifecycle", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
