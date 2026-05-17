// PlumbingComplianceScanner — Phase 179e RAG dashboard data feed.
//
// Aggregates the existing per-domain validators (drainage / supply /
// vents / backflow / dead-leg) into a single PlumbingComplianceResult
// with five colour-coded scores. Drives the AUDIT tab tile dashboard.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class PlumbingDomainScore
    {
        public string Domain        { get; set; } = "";
        public int Total            { get; set; }
        public int Pass             { get; set; }
        public int Warn             { get; set; }
        public int Fail             { get; set; }
        public List<string> TopFindings { get; } = new List<string>();
        public string Severity => Fail > 0 ? "RED" : Warn > 5 ? "AMBER" : "GREEN";
        public double PercentPass => Total <= 0 ? 100.0 : 100.0 * Pass / Total;
    }

    public class PlumbingComplianceResult
    {
        public DateTime ScanUtc   { get; set; } = DateTime.UtcNow;
        public PlumbingDomainScore Supply   { get; set; } = new PlumbingDomainScore { Domain = "SUPPLY" };
        public PlumbingDomainScore Drainage { get; set; } = new PlumbingDomainScore { Domain = "DRAINAGE" };
        public PlumbingDomainScore Vents    { get; set; } = new PlumbingDomainScore { Domain = "VENTS" };
        public PlumbingDomainScore Backflow { get; set; } = new PlumbingDomainScore { Domain = "BACKFLOW" };
        public PlumbingDomainScore Htm      { get; set; } = new PlumbingDomainScore { Domain = "HTM 04-01" };
    }

    public static class PlumbingComplianceScanner
    {
        public static PlumbingComplianceResult Scan(Document doc)
        {
            var r = new PlumbingComplianceResult();
            if (doc == null) return r;

            // ── SUPPLY ──
            try
            {
                var report = WaterSupplySizer.Analyse(doc, writeBack: false);
                r.Supply.Total = report.PipesScanned;
                r.Supply.Fail  = report.PipesVelocityFailed + report.PipesDpFailed;
                r.Supply.Warn  = report.PipesUpsized;
                r.Supply.Pass  = Math.Max(0, report.PipesScanned - r.Supply.Fail - r.Supply.Warn);
                if (report.PipesVelocityFailed > 0)
                    r.Supply.TopFindings.Add($"{report.PipesVelocityFailed} pipe(s) exceed velocity limit");
                if (report.PipesDpFailed > 0)
                    r.Supply.TopFindings.Add($"{report.PipesDpFailed} pipe(s) exceed Pa/m limit");
                if (report.PipesUpsized > 0)
                    r.Supply.TopFindings.Add($"{report.PipesUpsized} pipe(s) recommend up-sizing");
            }
            catch (Exception ex) { r.Supply.TopFindings.Add("scan error: " + ex.Message); }

            // ── DRAINAGE ──
            try
            {
                var dfu = FixtureUnitAggregator.BuildDfuMap(doc);
                var sizing = DrainageSizer.AnalyseAndSize(doc, dfu.PipeDfu, writeBack: false, dryRun: true);
                r.Drainage.Total = sizing.PipesAnalysed;
                r.Drainage.Fail  = sizing.PipesUpsized;
                r.Drainage.Warn  = sizing.PipesSlopeInsufficient + sizing.PipesSelfCleansingFailed;
                r.Drainage.Pass  = Math.Max(0, sizing.PipesAnalysed - r.Drainage.Fail - r.Drainage.Warn);
                if (sizing.PipesUpsized > 0)
                    r.Drainage.TopFindings.Add($"{sizing.PipesUpsized} pipe(s) need up-sizing");
                if (sizing.PipesSlopeInsufficient > 0)
                    r.Drainage.TopFindings.Add($"{sizing.PipesSlopeInsufficient} slope failure(s)");
                if (sizing.PipesSelfCleansingFailed > 0)
                    r.Drainage.TopFindings.Add($"{sizing.PipesSelfCleansingFailed} self-cleansing failure(s)");
            }
            catch (Exception ex) { r.Drainage.TopFindings.Add("scan error: " + ex.Message); }

            // ── VENTS ──
            try
            {
                var dfu = FixtureUnitAggregator.BuildDfuMap(doc);
                var vents = VentDesigner.DesignVents(doc, dfu.PipeDfu);
                r.Vents.Total = vents.Count;
                int aav = 0, relief = 0;
                foreach (var v in vents)
                {
                    if (v.RequiresAav) aav++;
                    if (v.RequiresReliefVent) relief++;
                }
                r.Vents.Warn = aav;
                r.Vents.Fail = relief;
                r.Vents.Pass = Math.Max(0, vents.Count - aav - relief);
                if (aav > 0)    r.Vents.TopFindings.Add($"{aav} AAV(s) required");
                if (relief > 0) r.Vents.TopFindings.Add($"{relief} relief vent(s) recommended");
            }
            catch (Exception ex) { r.Vents.TopFindings.Add("scan error: " + ex.Message); }

            // ── BACKFLOW ──
            try
            {
                var classified = BackflowClassifier.ClassifyAll(doc);
                var crossConn  = CrossConnectionChecker.Scan(doc);
                r.Backflow.Total = classified.Count;
                int cat45 = 0;
                foreach (var c in classified)
                    if (c.Category >= FluidCategory.Category4) cat45++;
                r.Backflow.Warn = cat45;
                r.Backflow.Fail = crossConn.Count;
                r.Backflow.Pass = Math.Max(0, classified.Count - cat45 - crossConn.Count);
                if (cat45 > 0)         r.Backflow.TopFindings.Add($"{cat45} Cat-4/5 service(s)");
                if (crossConn.Count > 0) r.Backflow.TopFindings.Add($"{crossConn.Count} cross-connection finding(s)");
            }
            catch (Exception ex) { r.Backflow.TopFindings.Add("scan error: " + ex.Message); }

            // ── HTM 04-01 ──
            try
            {
                var deadLegs = DeadLegDetector.Scan(doc, writeBack: false);
                r.Htm.Total = deadLegs.PipesScanned;
                r.Htm.Fail  = deadLegs.LegsFlagged;
                r.Htm.Pass  = Math.Max(0, deadLegs.PipesScanned - deadLegs.LegsFlagged);
                if (deadLegs.LegsFlagged > 0)
                    r.Htm.TopFindings.Add($"{deadLegs.LegsFlagged} dead-leg(s) > HSG 274 limits");
            }
            catch (Exception ex) { r.Htm.TopFindings.Add("scan error: " + ex.Message); }

            return r;
        }
    }
}
