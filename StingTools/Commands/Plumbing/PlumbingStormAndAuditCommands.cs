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

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbRwhCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            // Phase 179e ships defaults; user-input dialog will replace these in 179f follow-up.
            var r = PlumbingSustainabilityCalc.RwhYield(roofAreaM2: 500, annualRainfallMm: 800);
            var panel = StingResultPanel.Create("Rainwater Harvesting (BS 8515)");
            panel.SetSubtitle("Defaults: 500 m² roof · 800 mm/yr · 1.5 m³/day demand · η=0.90 · Cv=0.75");
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
            double v = PlumbingSustainabilityCalc.SudsAttenuationM3(
                postDevAreaM2: 5000, preDevGreenAreaM2: 5000,
                rainfallIntensityMmHr: 25, stormDurationHr: 1.0);
            var panel = StingResultPanel.Create("SuDS Attenuation (CIRIA C753)");
            panel.SetSubtitle("Defaults: 5 000 m² post-dev · 25 mm/hr · 1 hr storm · 40% climate uplift");
            panel.AddSection("RESULT")
                 .Metric("Attenuation volume (m³)", v.ToString("F1"));
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
            double v = PlumbingSustainabilityCalc.SoakawayVolumeM3(
                catchmentAreaM2: 200, rainfallIntensityMHr: 0.025,
                stormDurationHr: 1.0, infiltrationRateMHr: 0.05);
            var panel = StingResultPanel.Create("Soakaway (BRE Digest 365)");
            panel.SetSubtitle("Defaults: 200 m² catchment · 25 mm/hr · 1 hr storm · 0.05 m/hr infiltration");
            panel.AddSection("RESULT")
                 .Metric("Soakaway volume (m³)", v.ToString("F2"));
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
            double l = PlumbingSustainabilityCalc.SepticTankLitres(populationEquivalent: 6);
            var panel = StingResultPanel.Create("Septic Tank (BS EN 12566-1)");
            panel.SetSubtitle("Defaults: PE = 6");
            panel.AddSection("RESULT")
                 .Metric("Primary chamber (L)", l.ToString("F0"));
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
            var ctx = ParameterHelpers.GetContext(data);
            double area = 250, cr = 0.90, intensity = 0.021;
            string source = "default UK 2-min storm";

            // Phase 189 — pull the project rainfall intensity if set. Falls
            // back to the Uganda regional profile when PRJ_ORG_REGION_TXT is
            // populated. STR_RAIN_INTENSITY_MMH is stored in mm/h on the
            // project; convert to l/s/m² for the calc (1 mm/h = 1/3600 l/s/m²).
            if (ctx?.Doc?.ProjectInformation != null)
            {
                var pi = ctx.Doc.ProjectInformation;
                double mmh = 0;
                try
                {
                    var p = pi.LookupParameter("STR_RAIN_INTENSITY_MMH");
                    if (p != null && p.HasValue && p.StorageType == StorageType.String
                        && double.TryParse(p.AsString(), out double v)) mmh = v;
                }
                catch (Exception ex) { StingLog.Warn($"Read STR_RAIN_INTENSITY_MMH: {ex.Message}"); }

                if (mmh <= 0)
                {
                    try
                    {
                        var pr = pi.LookupParameter("PRJ_ORG_REGION_TXT");
                        if (pr != null && pr.HasValue && pr.StorageType == StorageType.String)
                        {
                            var region = pr.AsString();
                            if (!string.IsNullOrEmpty(region))
                            {
                                var prof = StingTools.Core.UgandaRegionalDefaults.ForRegion(region);
                                if (prof != null) { mmh = prof.RainIntensityMmh; source = $"Uganda regional ({prof.Id})"; }
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Read PRJ_ORG_REGION_TXT: {ex.Message}"); }
                }
                else
                {
                    source = "STR_RAIN_INTENSITY_MMH (project)";
                }

                if (mmh > 0) intensity = mmh / 3600.0;
            }

            double q = PlumbingSustainabilityCalc.RoofDrainageLps(area, cr, intensity);
            int outletSize = q < 1.5 ? 75 : q < 5 ? 100 : q < 10 ? 125 : 150;
            int outletCount = (int)Math.Ceiling(q / (q < 1.5 ? 0.8 : q < 5 ? 1.5 : 3.0));

            var panel = StingResultPanel.Create("Roof Drainage (BS EN 12056-3)");
            panel.SetSubtitle($"Defaults: {area} m² flat roof · Cr {cr} · r {intensity:F4} l/s/m² ({intensity * 3600:F0} mm/h, {source}) · f 1.5");
            panel.AddSection("RESULT")
                 .Metric("Rainfall (mm/h)",        $"{intensity * 3600:F0}")
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
