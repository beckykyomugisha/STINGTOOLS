// STING Tools — Standards bulk wrappers (region-aware library calls).
//
// Each command collects inputs through NumericPrompt, dispatches to
// StandardsAPI / AECCalculations using the active region from
// ProjectStandardsManager, and renders the typed result through
// StingResultPanel. Replaces the original Phase-116 reference-card
// stubs that hard-coded calculation values inline.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.Standards;
using StingTools.UI;

namespace StingTools.Commands.StandardsExt
{
    internal static class Bk
    {
        public static StingResultPanel.Builder B(string t, string s) =>
            StingResultPanel.Create(t).SetSubtitle(s);

        public static string Region => ProjectStandardsManager.Instance.Region ?? "International";

        public static string GetStd(StandardsDiscipline d) =>
            ProjectStandardsManager.Instance.GetStandardForDiscipline(d);
    }

    // 1. Ventilation (ASHRAE 62.1 / CIBSE TM60)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class VentilationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Ventilation (ASHRAE 62.1 / CIBSE TM60)",
                new[] { "Floor area (m²)", "Occupants", "Space type (1=Office, 2=Class, 3=Retail)" },
                new[] { 100.0, 10.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                string space = v[2] <= 1.5 ? "Office" : v[2] <= 2.5 ? "Classroom" : "Retail";
                var res = StandardsAPI.CalculateVentilation(v[0], v[1], space);
                Bk.B("Ventilation", $"{Bk.Region} · {res.CIBSEReference ?? "ASHRAE 62.1"}")
                    .AddSection("RESULT")
                    .Metric("Space type",         res.SpaceType ?? space)
                    .Metric("Fresh air",          $"{res.FreshAirLPS:F0} L/s ({res.FreshAirM3H:F0} m³/h)")
                    .Metric("Air changes / hour", $"{res.AirChangesPerHour:F1}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("Ventilation", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 2. Plumbing pipe size (IPC / BS EN 806)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class PlumbingPipeSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Plumbing pipe (IPC / BS EN 806)",
                new[] { "Flow (gpm)", "Length (ft)", "Fixtures" },
                new[] { 20.0, 50.0, 8.0 }, out var v)) return Result.Cancelled;
            try
            {
                string std = Bk.GetStd(StandardsDiscipline.Plumbing);
                var res = StandardsAPI.CalculatePlumbingPipeSize(v[0], v[1], "Copper", std, (int)v[2]);
                Bk.B("Plumbing pipe size", $"{Bk.Region} · {res.IPCReference ?? std}")
                    .AddSection("RESULT")
                    .Metric("Nominal size", res.NominalSize ?? "-")
                    .Metric("Pipe Ø",       $"{res.PipeDiameterMM:F0} mm ({res.PipeDiameterInch:F2}\")")
                    .Metric("Velocity",     $"{res.VelocityMPS:F2} m/s ({res.VelocityFPS:F2} fps)")
                    .Metric("Flow",         $"{res.FlowRateGPM:F0} gpm · WSFU {res.WSFU:F0}")
                    .Metric("Compliant",    res.IsIPCCompliant ? "yes" : "review")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("PlumbingPipe", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 3. Duct size — equal friction (CIBSE Guide B3 / ASHRAE Handbook)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class DuctEqualFrictionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Equal friction duct (CIBSE B3 / ASHRAE)",
                new[] { "Flow (L/s)", "Friction (Pa/m)" },
                new[] { 500.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                // Library expects CFM + in.WG/100ft. Convert from L/s + Pa/m:
                //   1 L/s ≈ 2.1189 CFM
                //   100 ft = 30.48 m, 1 in.WG ≈ 248.84 Pa
                //   in.WG/100ft = (Pa/m × 30.48 m) / 248.84 Pa/in.WG
                double cfm = v[0] * 2.1189;
                double frictionInWG = v[1] * 30.48 / 248.84;
                var res = AECCalculations.CalculateDuctSize(cfm, frictionInWG);
                Bk.B("Equal friction duct", $"{Bk.Region} · {res.StandardReference ?? "CIBSE Guide B3"}")
                    .AddSection("RESULT")
                    .Metric("Round Ø",  $"{res.RoundDiameterMM:F0} mm ({res.RoundDiameterInches:F1}\")")
                    .Metric("Velocity", $"{res.VelocityMPS:F1} m/s ({res.VelocityFPM:F0} fpm)")
                    .Metric("Friction", $"{res.FrictionRate:F3} in.WG/100ft")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("DuctEqFr", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 4. Psychrometric (ASHRAE Handbook ch. 1) — closed-form, no library overload
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class PsychrometricCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Moist air (ASHRAE)",
                new[] { "DBT °C", "RH %", "P kPa" },
                new[] { 25.0, 60.0, 101.3 }, out var v)) return Result.Cancelled;
            double pws = 0.611 * Math.Exp(17.27 * v[0] / (v[0] + 237.3));
            double pw = v[1] / 100 * pws;
            double w = 0.622 * pw / (v[2] - pw);
            double h = 1.006 * v[0] + w * (2501 + 1.86 * v[0]);
            Bk.B("Psychrometric", $"{Bk.Region} · ASHRAE Handbook ch. 1")
                .AddSection("STATE")
                .Metric("DBT",            $"{v[0]:F1} °C")
                .Metric("RH",             $"{v[1]:F0}%")
                .Metric("Vapor pressure", $"{pw:F3} kPa")
                .Metric("Humidity ratio", $"{w * 1000:F2} g/kg")
                .Metric("Enthalpy",       $"{h:F1} kJ/kg")
                .Show();
            return Result.Succeeded;
        }
    }

    // 5. Arc flash PPE (IEEE 1584 / NFPA 70E) — reference card
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ArcFlashCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            Bk.B("Arc flash", $"{Bk.Region} · IEEE 1584 + NFPA 70E")
                .AddSection("PPE CATEGORY (cal/cm²)")
                .Metric("Cat 0", "1.2 (≤ 1.2 cal/cm²)")
                .Metric("Cat 1", "4")
                .Metric("Cat 2", "8")
                .Metric("Cat 3", "25")
                .Metric("Cat 4", "40")
                .Show();
            return Result.Succeeded;
        }
    }

    // 6. Conduit fill — uses StandardsAPI.CalculateBoundingSize
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ConduitFillCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Conduit fill (NEC ch.9 / BS 7671)",
                new[] { "Number of cables", "Cable CSA (mm²)" },
                new[] { 6.0, 2.5 }, out var v)) return Result.Cancelled;
            try
            {
                int count = (int)v[0];
                double csaMm2 = v[1];
                var cableSizes = System.Linq.Enumerable
                    .Repeat(csaMm2.ToString(System.Globalization.CultureInfo.InvariantCulture), count)
                    .ToList();
                var res = StandardsAPI.CalculateBoundingSize(cableSizes, count);
                Bk.B("Conduit fill", $"{Bk.Region} · NEC ch.9 + BS 7671 App.4")
                    .AddSection("RESULT")
                    .Metric("Recommended size", res.RecommendedSize ?? "-")
                    .Metric("Cables",           res.NumberOfCables.ToString())
                    .Metric("Fill",             $"{res.FillPercentage:F1}%")
                    .Metric("Conduit type",     res.ConduitType ?? "EMT")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("ConduitFill", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 7. Steel beam — StandardsAPI.DesignSteelBeam (AISC 360 / EC3)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SteelBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Steel beam (AISC 360 / EC3)",
                new[] { "Span (m)", "UDL (kN/m)" },
                new[] { 8.0, 20.0 }, out var v)) return Result.Cancelled;
            try
            {
                double total = v[0] * v[1]; // kN
                string std = Bk.GetStd(StandardsDiscipline.Structural);
                string method = std.IndexOf("Eurocode", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "ULS" : "LRFD";
                string grade = std.IndexOf("Eurocode", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "S355" : "A992";
                var res = StandardsAPI.DesignSteelBeam(v[0], total, "Uniform", grade, method);
                Bk.B("Steel beam", $"{Bk.Region} · {res.EurocodeReference ?? std}")
                    .AddSection("DESIGN")
                    .Metric("Section",       res.SectionSize ?? "-")
                    .Metric("Applied moment",$"{res.AppliedMoment:F1} kNm")
                    .Metric("Required Zx",    $"{res.RequiredZx:F0} mm³")
                    .Metric("Steel grade",   res.SteelGrade ?? grade)
                    .Metric("Adequate",      res.IsAdequate ? "yes" : "review")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("SteelBeam", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 8. Concrete beam — reference card (no library overload yet)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ConcreteBeamCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            Bk.B("Concrete beam", $"{Bk.Region} · ACI 318 + EC2")
                .AddSection("DEFAULT RATIOS")
                .Metric("L/d simple span", "20")
                .Metric("L/d continuous",  "26")
                .Metric("min steel ρmin",  "1.4 / fy (ACI 10.5)")
                .Show();
            return Result.Succeeded;
        }
    }

    // 9. Pad foundation — reference card with quick sizing
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class FoundationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Pad foundation (ACI 318-14 / EC7)",
                new[] { "Column load (kN)", "Bearing capacity (kN/m²)" },
                new[] { 1000.0, 200.0 }, out var v)) return Result.Cancelled;
            double a = v[0] * 1.35 / v[1];
            double b = Math.Sqrt(a);
            Bk.B("Pad foundation", $"{Bk.Region} · ACI 318-14 + EC7")
                .AddSection("SIZING")
                .Metric("Required area", $"{a:F2} m²")
                .Metric("Square pad",    $"{b * 1000:F0} × {b * 1000:F0} mm")
                .Show();
            return Result.Succeeded;
        }
    }

    // 10. Seismic — AECCalculations.CalculateSeismicLoad
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SeismicCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Seismic (ASCE 7)",
                new[] { "Building weight (kips)", "Ss", "S1", "Site (1=A,2=B,3=C,4=D,5=E)" },
                new[] { 5000.0, 1.0, 0.4, 4.0 }, out var v)) return Result.Cancelled;
            try
            {
                string site = v[3] <= 1.5 ? "A" : v[3] <= 2.5 ? "B" : v[3] <= 3.5 ? "C"
                              : v[3] <= 4.5 ? "D" : "E";
                var res = AECCalculations.CalculateSeismicLoad(v[0], v[1], v[2], site);
                Bk.B("Seismic", $"{Bk.Region} · {res.StandardReference ?? "ASCE 7-22"}")
                    .AddSection("DESIGN")
                    .Metric("Base shear", $"{res.BaseShearKips:F0} kips ({res.BaseShearKN:F0} kN)")
                    .Metric("Cs",         $"{res.Cs:F3}")
                    .Metric("SDs",        $"{res.SDs:F3}")
                    .Metric("SD1",        $"{res.SD1:F3}")
                    .Metric("Fa / Fv",    $"{res.Fa:F2} / {res.Fv:F2}")
                    .Metric("SDC",        res.SeismicDesignCategory ?? "-")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("Seismic", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 11. Occupant load — AECCalculations.CalculateOccupantLoad
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class OccupantLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Occupant load (IBC 1004 / NFPA 101)",
                new[] { "Area (m²)", "Type (1=Office, 2=Classroom, 3=Retail-Ground, 4=Assembly-Unconcentrated)" },
                new[] { 500.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                string occ = v[1] <= 1.5 ? "Office" : v[1] <= 2.5 ? "Classroom"
                            : v[1] <= 3.5 ? "Retail-Ground" : "Assembly-Unconcentrated";
                double sqft = v[0] * 10.7639;
                var res = AECCalculations.CalculateOccupantLoad(sqft, occ);
                Bk.B("Occupant load", $"{Bk.Region} · {res.StandardReference ?? "IBC 1004"}")
                    .AddSection("RESULT")
                    .Metric("Occupants",   res.OccupantLoad.ToString())
                    .Metric("Load factor", $"{res.LoadFactor:F0} sqft/occupant")
                    .Metric("Type",        res.OccupancyType ?? occ)
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("OccLoad", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 12. Travel distance — AECCalculations.CalculateTravelDistance
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class TravelDistanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Travel distance (IBC 1017 / BS 9999)",
                new[] { "Group (1=A,2=B,3=E,4=F,5=H,6=R)", "Sprinklered? (0/1)" },
                new[] { 2.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                string grp = v[0] <= 1.5 ? "A" : v[0] <= 2.5 ? "B" : v[0] <= 3.5 ? "E"
                            : v[0] <= 4.5 ? "F" : v[0] <= 5.5 ? "H" : "R";
                bool sp = v[1] >= 0.5;
                var res = AECCalculations.CalculateTravelDistance(grp, sp);
                Bk.B("Travel distance", $"{Bk.Region} · {res.StandardReference ?? "IBC 1017"}")
                    .AddSection("LIMIT")
                    .Metric("Max travel",  $"{res.MaxTravelDistanceM:F0} m ({res.MaxTravelDistanceFt:F0} ft)")
                    .Metric("Group",       res.OccupancyGroup ?? grp)
                    .Metric("Sprinklered", res.IsSprinklered ? "yes" : "no")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("Travel", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 13. Egress width — AECCalculations.CalculateEgressWidth
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class EgressWidthCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Egress width (IBC 1005 / NFPA 101)",
                new[] { "Occupant load", "Sprinklered? (0/1)" },
                new[] { 300.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                var res = AECCalculations.CalculateEgressWidth(
                    occupantLoad: (int)v[0], egressComponent: "Corridor", sprinklered: v[1] >= 0.5);
                Bk.B("Egress width", $"{Bk.Region} · {res.StandardReference ?? "IBC 1005"}")
                    .AddSection("RESULT")
                    .Metric("Required width", $"{res.RequiredWidthMM:F0} mm ({res.RequiredWidthInches:F1}\")")
                    .Metric("Min width",       $"{res.MinimumWidthInches:F1}\"")
                    .Metric("Exits required",  res.ExitsRequired.ToString())
                    .Metric("Sprinklered",     res.IsSprinklered ? "yes" : "no")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("Egress", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 14. Space utilisation — AECCalculations.CalculateSpaceEfficiency
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SpaceUtilizationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Space utilisation (BOMA / IFMA)",
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
                Bk.B("Space utilisation", $"{Bk.Region} · {res.StandardReference ?? "BOMA / IFMA"}")
                    .AddSection("EFFICIENCY")
                    .Metric("Usable / gross",  $"{res.EfficiencyRatio:F1}%")
                    .Metric("Load factor",     $"{res.LoadFactor:F2}")
                    .Metric("ft² / person",    $"{res.SqFtPerPerson:F0}")
                    .Metric("ft² / workstation",$"{res.SqFtPerWorkstation:F0}")
                    .Metric("Rating",          res.EfficiencyRating ?? "-")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("SpaceUtil", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 15. Fire hydrant — reference card
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class HydrantCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            Bk.B("Hydrant", $"{Bk.Region} · NFPA 24 + BS EN 14384")
                .AddSection("PLACEMENT")
                .Metric("Max spacing",  "100 m (NFPA 1)")
                .Metric("From building", "≥ 12 m")
                .Metric("Flow test",    "946 L/min @ 137 kPa min")
                .Show();
            return Result.Succeeded;
        }
    }

    // 16. Maintenance cost — AECCalculations.CalculateMaintenanceCosts
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class MaintenanceCostCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Maintenance cost (BSRIA BG21 / IFMA)",
                new[] { "GFA (m²)", "Type (1=Office,2=Hospital,3=School,4=Warehouse)", "Building age (years)" },
                new[] { 2000.0, 1.0, 5.0 }, out var v)) return Result.Cancelled;
            try
            {
                string type = v[1] <= 1.5 ? "Office" : v[1] <= 2.5 ? "Hospital"
                            : v[1] <= 3.5 ? "School" : "Warehouse";
                var res = AECCalculations.CalculateMaintenanceCosts(v[0] * 10.7639, type, (int)v[2]);
                Bk.B("Maintenance cost", $"{Bk.Region} · {res.StandardReference ?? "IFMA + BSRIA BG21"}")
                    .AddSection("ANNUAL")
                    .Metric("Building type", res.BuildingType ?? type)
                    .Metric("Cost / ft²",    $"${res.CostPerSqFt:F2}")
                    .Metric("Annual cost",    $"${res.AnnualMaintenanceCost:F0}")
                    .Metric("Age factor",    $"{res.AgeFactor:F2}")
                    .Metric("Condition factor",$"{res.ConditionFactor:F2}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("MaintCost", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 17. Accessible toilet — AECCalculations.CalculateAccessibleToilet
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class AccessibleToiletCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Accessible toilet (ADA / BS 8300 / Part M)",
                new[] { "Width (mm)", "Depth (mm)", "Layout (1=Side, 2=Front)" },
                new[] { 1500.0, 2200.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                string layout = v[2] <= 1.5 ? "Side" : "Front";
                var res = AECCalculations.CalculateAccessibleToilet(v[0] / 25.4, v[1] / 25.4, layout);
                var sec = Bk.B("Accessible toilet", $"{Bk.Region} · {res.StandardReference ?? "ADA + BS 8300"}")
                    .AddSection("RESULT")
                    .Metric("Compliant", res.IsCompliant ? "yes" : "REVIEW")
                    .Metric("Required",  $"{res.RequiredWidth:F0}\" × {res.RequiredDepth:F0}\"")
                    .Metric("Actual",    $"{res.ActualWidth:F0}\" × {res.ActualDepth:F0}\"")
                    .Metric("WC ⌀ wall", $"{res.ToiletCenterlineFromWall:F0}\"");
                if (res.Issues != null && res.Issues.Count > 0)
                {
                    sec.AddSection("ISSUES");
                    foreach (var i in res.Issues) sec.Text(i);
                }
                sec.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("AccToilet", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 18. Accessible fixtures — AECCalculations.CalculateAccessibleFixtures
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class AccessibleFixturesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Accessible fixtures (ADA 309 / BS 8300)",
                new[] { "Toilets", "Urinals", "Lavatories", "Drinking fountains" },
                new[] { 8.0, 4.0, 8.0, 2.0 }, out var v)) return Result.Cancelled;
            try
            {
                var res = AECCalculations.CalculateAccessibleFixtures(
                    (int)v[0], (int)v[1], (int)v[2], (int)v[3]);
                Bk.B("Accessible fixtures", $"{Bk.Region} · {res.StandardReference ?? "ADA 309"}")
                    .AddSection("REQUIRED")
                    .Metric("Toilets",            $"{res.AccessibleToilets} of {res.TotalToilets}")
                    .Metric("Urinals",            $"{res.AccessibleUrinals} of {res.TotalUrinals}")
                    .Metric("Lavatories",         $"{res.AccessibleLavatories} of {res.TotalLavatories}")
                    .Metric("Drinking fountains", $"{res.HiLoDrinkingFountains} of {res.TotalDrinkingFountains}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("AccFix", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 19. EUI energy analysis — AECCalculations.CalculateEUI
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class EnergyAnalysisCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("EUI (Part L / ASHRAE 90.1 / CIBSE TM46)",
                new[] { "Annual kWh", "GFA (m²)", "Type (1=Office,2=Retail,3=Hotel,4=Hospital,5=School)" },
                new[] { 200000.0, 2000.0, 1.0 }, out var v)) return Result.Cancelled;
            try
            {
                string type = v[2] <= 1.5 ? "Office" : v[2] <= 2.5 ? "Retail"
                            : v[2] <= 3.5 ? "Hotel"  : v[2] <= 4.5 ? "Hospital" : "School K-12";
                var res = AECCalculations.CalculateEUI(v[0], v[1] * 10.7639, type);
                Bk.B("EUI", $"{Bk.Region} · {res.StandardReference ?? "ENERGY STAR + CIBSE TM46"}")
                    .AddSection("BENCHMARK")
                    .Metric("EUI",          $"{res.EUIKWhPerM2:F0} kWh/m²/yr ({res.EUIKBtuPerSqFt:F1} kBtu/ft²)")
                    .Metric("Baseline",     $"{res.BaselineEUI:F0} kBtu/ft²")
                    .Metric("vs baseline",  $"{res.PercentBetterThanBaseline:F1}% better")
                    .Metric("LEED EAp2",    $"{res.EstimatedLEEDPoints} points (est.)")
                    .Metric("Type",         res.BuildingType ?? type)
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("EUI", ex); m = ex.Message; return Result.Failed; }
        }
    }

    // 20. Sprinkler design criteria — StandardsAPI.DesignSprinklerSystem
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SprinklerCriteriaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string m, ElementSet e)
        {
            if (!NumericPrompt.TryAsk("Sprinkler criteria (NFPA 13 / BS EN 12845)",
                new[] { "Floor area (m²)", "Hazard (1=LH, 2=OH1, 3=OH2, 4=HHS)" },
                new[] { 1000.0, 2.0 }, out var v)) return Result.Cancelled;
            try
            {
                string hz = v[1] <= 1.5 ? "Light" : v[1] <= 2.5 ? "OrdinaryGroup1"
                          : v[1] <= 3.5 ? "OrdinaryGroup2" : "ExtraHazard";
                string std = Bk.GetStd(StandardsDiscipline.FireProtection);
                var res = StandardsAPI.DesignSprinklerSystem(v[0], "Office", hz, std);
                Bk.B("Sprinkler criteria", $"{Bk.Region} · {res.NFPAReference ?? std}")
                    .AddSection("HYDRAULIC")
                    .Metric("Hazard",          res.HazardClass ?? hz)
                    .Metric("Density",         $"{res.DesignDensity:F3} gpm/ft²")
                    .Metric("Design area",     $"{res.DesignAreaFt2:F0} ft² ({res.DesignAreaFt2 * 0.0929:F0} m²)")
                    .Metric("Heads in design",  res.DesignHeads.ToString())
                    .Metric("Total heads",      res.NumberOfHeads.ToString())
                    .Metric("Flow",             $"{res.FlowRateGPM:F0} gpm ({res.FlowRateGPM * 3.785:F0} L/min)")
                    .Metric("Hose stream",      $"{res.HoseStreamGPM:F0} gpm")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("SprinklerCrit", ex); m = ex.Message; return Result.Failed; }
        }
    }
}
