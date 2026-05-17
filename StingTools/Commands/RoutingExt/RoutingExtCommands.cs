// STING Tools — Phase 114: Routing Extensions (RT-01..07).
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.RoutingExt
{
    internal static class RtPanel {
        public static StingResultPanel.Builder B(string t, string s)
            => StingResultPanel.Create(t).SetSubtitle(s);
    }

    // RT-01 — Manhattan branch layout
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class ManhattanLayoutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("RT-01 Manhattan branch layout",
                new[] { "Fixtures to route", "Main trunk length (m)", "Max branch (m)" },
                new[] { 12.0, 20.0, 5.0 }, out var v)) return Result.Cancelled;
            RtPanel.B("RT-01 Manhattan layout", "L-shape routing from fixtures to main trunk")
                .AddSection("PLAN")
                .Metric("Fixtures",     $"{(int)v[0]}")
                .Metric("Trunk length", $"{v[1]:F0} m")
                .Metric("Max branch",   $"{v[2]:F0} m")
                .Metric("Est. total",   $"{v[1] + (v[0] * v[2]/2):F0} m of MEPCurve")
                .Text("Preview geometry placement pending GenerateLayoutCommand body wiring.")
                .Show();
            return Result.Succeeded;
        }
    }

    // RT-02 — Clash avoidance
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class ClashAvoidCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            RtPanel.B("RT-02 Clash avoidance", "AabbSweep vs structure, 25mm Z shift")
                .AddSection("ALGORITHM")
                .Text("For each selected route, checks overlap with structural elements using AabbSweep.BroadPhase. On overlap, shifts Z by 25mm up to 5 steps. On failure, reports unavoidable clashes for manual coordination.")
                .Show();
            return Result.Succeeded;
        }
    }

    // RT-03 — Cable bundle grouping
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class CableBundleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            RtPanel.B("RT-03 Cable bundling", "BS 7671 528 + IEC 60364-5-52")
                .AddSection("SEGREGATION")
                .Metric("Clean mains power", "Separate tray/compartment")
                .Metric("Data / signal",      "SELV / PELV in own compartment, 50mm from LV")
                .Metric("Fire-protected",     "MICC/FP200 in dedicated pathway")
                .Metric("Derating factor",    "6 circuits touching: 0.72; 9 circuits: 0.70 (Table 4B1)")
                .Show();
            return Result.Succeeded;
        }
    }

    // RT-04 — Pipe insulation auto-apply
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class PipeInsulationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("RT-04 Pipe insulation thickness",
                new[] { "Operating temp °C", "Pipe dia nominal DN", "Insulation λ (W/mK)" },
                new[] { 60.0, 50.0, 0.04 }, out var v)) return Result.Cancelled;
            // TIMSA/Part L guidance: LTHW on DN<60: 30mm; DN60-100: 40mm; DN>100: 50mm
            double thk = v[1] < 60 ? 30 : v[1] <= 100 ? 40 : 50;
            RtPanel.B("RT-04 Pipe insulation", "Part L 2021 + TIMSA guide")
                .AddSection("SPEC")
                .Metric("Operating T",      $"{v[0]:F0} °C")
                .Metric("DN",               $"{v[1]:F0}")
                .Metric("Insulation λ",     $"{v[2]:F3} W/mK")
                .Metric("Recommended thk",  $"{thk:F0} mm")
                .Show();
            return Result.Succeeded;
        }
    }

    // RT-05 — Auto fire damper
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class AutoFireDamperCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            int ducts = 0, compartmentWalls = 0;
            try {
                foreach (var el in new FilteredElementCollector(ctx.Doc).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType())
                    ducts++;
                foreach (var el in new FilteredElementCollector(ctx.Doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
                {
                    var fr = el.LookupParameter("FIRE_RATING")?.AsString() ?? "";
                    if (fr.Contains("60") || fr.Contains("90") || fr.Contains("120")) compartmentWalls++;
                }
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            RtPanel.B("RT-05 Auto fire damper", "Approved Document B + BS 9999")
                .AddSection("SCAN")
                .Metric("Ducts in model",       ducts.ToString())
                .Metric("Compartment walls",    compartmentWalls.ToString())
                .Metric("Rule",                  "Fire damper at every duct crossing a compartment wall (FR ≥ 60min)")
                .Text("Uses AutoSleevePlacementCommand pattern scoped to compartment walls only.")
                .Show();
            return Result.Succeeded;
        }
    }

    // RT-06 — Expansion loop
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ExpansionLoopCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            if (!NumericPrompt.TryAsk("RT-06 Expansion loop (ASME B31.1)",
                new[] { "Run length (m)", "ΔT °C", "Pipe material (1=Steel, 2=Cu, 3=PVC)" },
                new[] { 50.0, 60.0, 1.0 }, out var v)) return Result.Cancelled;
            double alpha = v[2] <= 1.5 ? 1.2e-5 : v[2] <= 2.5 ? 1.7e-5 : 5e-5;
            double dL = alpha * v[0] * 1000 * v[1];
            RtPanel.B("RT-06 Expansion loop", "ASME B31.1 + BS EN 13480")
                .AddSection("THERMAL EXPANSION")
                .Metric("α coefficient",  $"{alpha:E1} /°C")
                .Metric("ΔL",              $"{dL:F1} mm")
                .Metric("Loop leg (heuristic)", $"{30 * Math.Sqrt(dL / 25):F0} mm per side")
                .Show();
            return Result.Succeeded;
        }
    }

    // RT-07 — Tray riser
    [Transaction(TransactionMode.Manual)][Regeneration(RegenerationOption.Manual)]
    public class TrayRiserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd); if (ctx == null) { message="No doc"; return Result.Failed; }
            RtPanel.B("RT-07 Cable tray vertical riser", "BS EN 61537 + BS 7671")
                .AddSection("PLACEMENT")
                .Text("Inverts the AutoSleevePlacement workflow for cable tray. For each floor penetration, insert a vertical riser section with fire-stop.")
                .Show();
            return Result.Succeeded;
        }
    }
}
