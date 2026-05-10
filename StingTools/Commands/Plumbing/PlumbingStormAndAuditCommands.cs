// PlumbingStormAndAuditCommands — Phase 179e STORM + AUDIT.
//
// Plumb_RWH         — BS 8515 RWH yield calc (replaces old RainwaterCalcCommand wrapper).
// Plumb_SuDS        — CIRIA C753 attenuation.
// Plumb_Soakaway    — BRE Digest 365.
// Plumb_SepticTank  — BS EN 12566-1.
// Plumb_FullAudit   — runs all five compliance domains, RAG dashboard.
// Plumb_RoofDrainage— BS EN 12056-3 roof outlet sizing.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;
using StingTools.UI.Plumbing;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbRwhCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var inst = StingPlumbingPanel.Instance;
            var s = inst?.ReadStormInputs();
            double area     = s != null && s.RwhAreaM2     > 0 ? s.RwhAreaM2     : 500;
            double rainfall = s != null && s.RwhRainfallMm > 0 ? s.RwhRainfallMm : 800;
            double cv       = (s?.RwhMaterial ?? "").Contains("0.90") ? 0.90 : 0.75;
            double demandLpd= s != null && s.RwhDemandL    > 0 ? s.RwhDemandL / 365.0 : 1500.0;
            var r = PlumbingSustainabilityCalc.RwhYield(area, rainfall, runoffCoefficient: cv,
                                                        filterEfficiency: 0.90, dailyDemandM3: demandLpd / 1000.0);
            string line = $"Yield {r.AnnualYieldM3:F1} m³/yr · tank {r.RecommendedTankM3:F2} m³ · η {r.YieldEfficiencyPct:F0}%";
            string status = $"RWH · {area:F0} m² · {rainfall:F0} mm/yr · {line}";
            if (inst != null)
            {
                inst.SetStormRwhResult(line, status);
                return Result.Succeeded;
            }
            var panel = StingResultPanel.Create("Rainwater Harvesting (BS 8515)");
            panel.SetSubtitle($"{area:F0} m² roof · {rainfall:F0} mm/yr · Cv {cv:F2}");
            panel.AddSection("YIELD")
                 .Metric("Annual catch (m³)",       r.AnnualRainfallM3.ToString("F1"))
                 .Metric("Annual demand (m³)",      r.AnnualDemandM3.ToString("F1"))
                 .Metric("Annual yield (m³)",       r.AnnualYieldM3.ToString("F1"))
                 .Metric("Yield efficiency %",      r.YieldEfficiencyPct.ToString("F0"))
                 .Metric("Recommended tank (m³)",   r.RecommendedTankM3.ToString("F2"));
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSuDSCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var inst = StingPlumbingPanel.Instance;
            var s = inst?.ReadStormInputs();
            double area     = s != null && s.SudsAreaM2 > 0 ? s.SudsAreaM2 : 5000;
            double imperm   = s != null && s.SudsImperm > 0 ? s.SudsImperm : 0.90;
            double pre      = area * (1.0 - imperm);
            double v = PlumbingSustainabilityCalc.SudsAttenuationM3(
                postDevAreaM2: area, preDevGreenAreaM2: pre,
                rainfallIntensityMmHr: 25, stormDurationHr: 1.0);
            string line = $"Attenuation volume {v:F1} m³";
            string status = $"SuDS · {area:F0} m² · imperm {imperm:F2} · {line}";
            if (inst != null)
            {
                inst.SetStormSudsResult(line, status);
                return Result.Succeeded;
            }
            var panel = StingResultPanel.Create("SuDS Attenuation (CIRIA C753)");
            panel.SetSubtitle($"{area:F0} m² post-dev · imperm {imperm:F2} · 25 mm/hr · 1 hr · 40% uplift");
            panel.AddSection("RESULT").Metric("Attenuation volume (m³)", v.ToString("F1"));
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSoakawayCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var inst = StingPlumbingPanel.Instance;
            var s = inst?.ReadStormInputs();
            double area    = s != null && s.SoakAreaM2    > 0 ? s.SoakAreaM2    : 200;
            double stormMm = s != null && s.SoakStormMmHr > 0 ? s.SoakStormMmHr : 25;
            double infilt  = s != null && s.SoakInfiltMs  > 0 ? s.SoakInfiltMs  : 0.05;
            double v = PlumbingSustainabilityCalc.SoakawayVolumeM3(
                catchmentAreaM2: area, rainfallIntensityMHr: stormMm / 1000.0,
                stormDurationHr: 1.0, infiltrationRateMHr: infilt);
            string line = $"Soakaway volume {v:F2} m³";
            string status = $"Soakaway · {area:F0} m² · {stormMm:F0} mm/hr · f {infilt:F3} · {line}";
            if (inst != null)
            {
                inst.SetStormSoakResult(line, status);
                return Result.Succeeded;
            }
            var panel = StingResultPanel.Create("Soakaway (BRE Digest 365)");
            panel.SetSubtitle($"{area:F0} m² catchment · {stormMm:F0} mm/hr · 1 hr · {infilt:F3} m/hr infiltration");
            panel.AddSection("RESULT").Metric("Soakaway volume (m³)", v.ToString("F2"));
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSepticTankCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var inst = StingPlumbingPanel.Instance;
            var s = inst?.ReadStormInputs();
            int pe = s != null && s.SepticPersons > 0 ? s.SepticPersons : 6;
            double l = PlumbingSustainabilityCalc.SepticTankLitres(populationEquivalent: pe);
            string line = $"Primary chamber {l:F0} L (PE {pe})";
            string status = $"Septic · PE {pe} · {line}";
            if (inst != null)
            {
                inst.SetStormSepticResult(line, status);
                return Result.Succeeded;
            }
            var panel = StingResultPanel.Create("Septic Tank (BS EN 12566-1)");
            panel.SetSubtitle($"PE = {pe}");
            panel.AddSection("RESULT").Metric("Primary chamber (L)", l.ToString("F0"));
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbRoofDrainageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var inst = StingPlumbingPanel.Instance;
            var s = inst?.ReadStormInputs();
            double area      = s != null && s.RoofAreaM2   > 0 ? s.RoofAreaM2   : 250;
            double intensity = s != null && s.RoofRainfall > 0 ? s.RoofRainfall : 0.021;
            double safety    = s != null && s.RoofSafety   > 0 ? s.RoofSafety   : 1.5;
            string roofType  = s?.RoofType ?? "Flat";
            double cr        = roofType.StartsWith("Pitched", StringComparison.OrdinalIgnoreCase) ? 0.85 : 0.90;
            double q = PlumbingSustainabilityCalc.RoofDrainageLps(area, cr, intensity) * safety;
            int outletSize  = q < 1.5 ? 75 : q < 5 ? 100 : q < 10 ? 125 : 150;
            int outletCount = (int)Math.Ceiling(q / (q < 1.5 ? 0.8 : q < 5 ? 1.5 : 3.0));
            string line = $"Q_r {q:F2} l/s · DN{outletSize} × {outletCount} outlets";
            string status = $"Roof · {area:F0} m² · {roofType} · Cr {cr:F2} · {line}";
            if (inst != null)
            {
                inst.SetStormRoofResult(line, status);
                return Result.Succeeded;
            }
            var panel = StingResultPanel.Create("Roof Drainage (BS EN 12056-3)");
            panel.SetSubtitle($"{area:F0} m² · {roofType} · Cr {cr:F2} · r {intensity:F4} l/s/m² · f {safety:F2}");
            panel.AddSection("RESULT")
                 .Metric("Design flow Q_r (l/s)",  q.ToString("F2"))
                 .Metric("Outlet DN (mm)",         outletSize.ToString())
                 .Metric("Outlets recommended",    outletCount.ToString());
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbFullAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var r = PlumbingComplianceScanner.Scan(ctx.Doc);

            var panel = StingResultPanel.Create("Plumbing Full Audit");
            panel.SetSubtitle($"Scan: {r.ScanUtc:u}");
            panel.AddSection("RAG DASHBOARD")
                 .Metric("Supply",   $"{r.Supply.PercentPass:F0}% [{r.Supply.Severity}]   pass {r.Supply.Pass} · warn {r.Supply.Warn} · fail {r.Supply.Fail}")
                 .Metric("Drainage", $"{r.Drainage.PercentPass:F0}% [{r.Drainage.Severity}]   pass {r.Drainage.Pass} · warn {r.Drainage.Warn} · fail {r.Drainage.Fail}")
                 .Metric("Vents",    $"{r.Vents.PercentPass:F0}% [{r.Vents.Severity}]   pass {r.Vents.Pass} · warn {r.Vents.Warn} · fail {r.Vents.Fail}")
                 .Metric("Backflow", $"{r.Backflow.PercentPass:F0}% [{r.Backflow.Severity}]   pass {r.Backflow.Pass} · warn {r.Backflow.Warn} · fail {r.Backflow.Fail}")
                 .Metric("HTM 04-01",$"{r.Htm.PercentPass:F0}% [{r.Htm.Severity}]   pass {r.Htm.Pass} · warn {r.Htm.Warn} · fail {r.Htm.Fail}");

            foreach (var d in new[] { r.Supply, r.Drainage, r.Vents, r.Backflow, r.Htm })
            {
                if (d.TopFindings.Count == 0) continue;
                panel.AddSection(d.Domain + " — TOP FINDINGS");
                foreach (var f in d.TopFindings) panel.Text("• " + f);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }
}
