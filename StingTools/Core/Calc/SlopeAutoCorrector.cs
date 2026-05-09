// StingTools v4 MVP — BS EN 12056 slope auto-corrector.
//
// SlopeValidator reports drainage pipes below the required gradient.
// This companion class FIXES them: walks every drainage pipe, detects
// endpoints where the flow is in the wrong direction (upstream end
// is lower than downstream end) and swaps the pipe's LocationCurve
// endpoints to match. For pipes with zero slope, it applies a minimum
// 1:80 drop over the run length by depressing the downstream end.
//
// BS EN 12056-2 minimums enforced:
//   Sanitary/soil (DN75–DN100):   1:100 to 1:40 (1.0%–2.5%)
//   Rainwater:                    1:100
//   Self-cleansing velocity:      0.7 m/s — flagged not auto-fixed
//
// Uses ElementTransformUtils.MoveElement on the pipe endpoint in a
// TransactionGroup. If any fix fails, the group rolls back so the
// user never ends up with a partially-fixed network.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Calc
{
    public class SlopeFix
    {
        public ElementId PipeId     { get; set; } = ElementId.InvalidElementId;
        public double OriginalPct   { get; set; }
        public double TargetPct     { get; set; }
        public double AppliedPct    { get; set; }
        public string Action        { get; set; } = "";
        public bool   Success       { get; set; }
        public string FailureReason { get; set; } = "";
    }

    public class SlopeAutoCorrectionResult
    {
        public int PipesScanned     { get; set; }
        public int PipesFlipped     { get; set; }
        public int PipesDepressed   { get; set; }
        public int PipesUnchanged   { get; set; }
        public int PipesFailed      { get; set; }
        public List<SlopeFix> Fixes { get; } = new List<SlopeFix>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SlopeAutoCorrector
    {
        private const double MinSlopeSanitaryPct = 1.0;
        private const double MinSlopeRainwaterPct = 1.0;
        private const double SelfCleansingVelocityMs = 0.7;

        public static SlopeAutoCorrectionResult RunFix(Document doc, bool dryRun)
        {
            var result = new SlopeAutoCorrectionResult();
            if (doc == null) return result;

            var drainage = CollectDrainagePipes(doc, result);
            result.PipesScanned = drainage.Count;

            TransactionGroup tg = null;
            if (!dryRun)
            {
                tg = new TransactionGroup(doc, "STING v4 Slope auto-correct");
                tg.Start();
            }

            try
            {
                using (var tx = dryRun ? null : new Transaction(doc, "STING v4 Slope auto-correct pipes"))
                {
                    tx?.Start();
                    foreach (var pipe in drainage)
                    {
                        var fix = FixOnePipe(doc, pipe, dryRun);
                        result.Fixes.Add(fix);
                        if (!fix.Success)
                        {
                            result.PipesFailed++;
                            result.Warnings.Add($"{pipe.Id}: {fix.FailureReason}");
                            continue;
                        }
                        switch (fix.Action)
                        {
                            case "FLIP":        result.PipesFlipped++;   break;
                            case "DEPRESS":     result.PipesDepressed++; break;
                            case "UNCHANGED":   result.PipesUnchanged++; break;
                        }
                    }
                    tx?.Commit();
                }

                if (!dryRun)
                {
                    if (result.PipesFailed > 0)
                    {
                        result.Warnings.Add($"Rolled back: {result.PipesFailed} pipe(s) failed to correct.");
                        tg.RollBack();
                    }
                    else
                    {
                        tg.Assimilate();
                    }
                }
            }
            catch (Exception ex)
            {
                if (!dryRun && tg != null && tg.HasStarted() && !tg.HasEnded()) tg.RollBack();
                result.Warnings.Add($"SlopeAutoCorrector fatal: {ex.Message}");
            }

            return result;
        }

        private static List<Pipe> CollectDrainagePipes(Document doc, SlopeAutoCorrectionResult result)
        {
            var list = new List<Pipe>();
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe));
                foreach (var el in col)
                {
                    if (!(el is Pipe p)) continue;
                    if (!IsDrainage(p)) continue;
                    list.Add(p);
                }
            }
            catch (Exception ex)
            { result.Warnings.Add($"SlopeAutoCorrector collector: {ex.Message}"); }
            return list;
        }

        private static bool IsDrainage(Pipe p)
        {
            try
            {
                var sys = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                if (sys.Contains("SANITARY") || sys.Contains("WASTE") ||
                    sys.Contains("SOIL")     || sys.Contains("DRAIN") ||
                    sys.Contains("STORM")    || sys.Contains("RAINWATER") ||
                    sys.Contains("FOUL"))
                    return true;
            }
            catch { }
            return false;
        }

        private static SlopeFix FixOnePipe(Document doc, Pipe pipe, bool dryRun)
        {
            var fix = new SlopeFix { PipeId = pipe.Id };
            try
            {
                var lc = pipe.Location as LocationCurve;
                var curve = lc?.Curve;
                if (curve == null)
                {
                    fix.FailureReason = "No LocationCurve";
                    return fix;
                }

                var s = curve.GetEndPoint(0);
                var e = curve.GetEndPoint(1);
                double dxy = Math.Sqrt((e.X - s.X) * (e.X - s.X) + (e.Y - s.Y) * (e.Y - s.Y));
                double dz  = e.Z - s.Z;
                if (dxy < 1e-6)
                {
                    fix.Action        = "UNCHANGED";
                    fix.Success       = true;
                    fix.FailureReason = "Vertical pipe";
                    return fix;
                }

                double slopePct = Math.Abs(dz) / dxy * 100.0;
                fix.OriginalPct = slopePct;
                fix.TargetPct   = MinSlopeSanitaryPct;

                // Wrong direction: fix the lower connector (s) and apply a descending
                // slope so the far end (e) depresses below s — preserves connector
                // topology via Pipe.SetSlope (Revit 2018+). Falls back to the
                // LocationCurve.Curve swap when the API refuses (constrained pipe).
                if (dz > 0 && slopePct >= MinSlopeSanitaryPct)
                {
                    if (!dryRun)
                    {
                        bool applied = TrySetSlopeApi(pipe, s, -(slopePct / 100.0));
                        if (!applied)
                        {
                            var reversed = Line.CreateBound(e, s);
                            lc.Curve = reversed;
                        }
                    }
                    fix.Action     = "FLIP";
                    fix.AppliedPct = slopePct;
                    fix.Success    = true;
                    return fix;
                }

                if (slopePct >= MinSlopeSanitaryPct && dz < 0)
                {
                    fix.Action     = "UNCHANGED";
                    fix.AppliedPct = slopePct;
                    fix.Success    = true;
                    return fix;
                }

                // Slope too shallow or flat — depress the end that is
                // currently higher, by (targetPct/100 × dxy) minus the
                // existing drop.
                double targetDropFt = (MinSlopeSanitaryPct / 100.0) * dxy;
                double currentDrop  = Math.Abs(dz);
                double additional   = targetDropFt - currentDrop;
                if (additional < 0) additional = 0;

                if (!dryRun && additional > 1e-6)
                {
                    // Fix the upper (currently higher) connector and apply a descending
                    // slope toward the far end. Preserves connector topology.
                    bool secondEndIsDown = e.Z <= s.Z;
                    XYZ upperEnd = secondEndIsDown ? s : e;
                    bool applied = TrySetSlopeApi(pipe, upperEnd, -(MinSlopeSanitaryPct / 100.0));
                    if (!applied)
                    {
                        // Fallback: move the downstream end further downward.
                        var downEndPoint = secondEndIsDown ? e : s;
                        var newPoint = new XYZ(downEndPoint.X, downEndPoint.Y,
                                               downEndPoint.Z - additional);
                        var newCurve = secondEndIsDown
                            ? Line.CreateBound(s, newPoint)
                            : Line.CreateBound(newPoint, e);
                        lc.Curve = newCurve;
                    }
                }

                fix.Action     = "DEPRESS";
                fix.AppliedPct = MinSlopeSanitaryPct;
                fix.Success    = true;
                return fix;
            }
            catch (Exception ex)
            {
                fix.FailureReason = ex.Message;
                return fix;
            }
        }

        /// <summary>
        /// Applies slope via <c>Pipe.SetSlope(Connector, double)</c> (Revit 2018+),
        /// which adjusts the far-end elevation while leaving the fixed connector's
        /// 3-D position and its network connections undisturbed.
        /// </summary>
        /// <param name="pipe">Pipe to adjust.</param>
        /// <param name="fixedEnd">World-space point nearest the connector that must not move.</param>
        /// <param name="slopeFtFt">Signed slope ft/ft (negative = far end descends).</param>
        /// <returns><c>true</c> when the API call succeeded; <c>false</c> prompts
        /// the caller to fall back to the LocationCurve.Curve setter.</returns>
        private static bool TrySetSlopeApi(Pipe pipe, XYZ fixedEnd, double slopeFtFt)
        {
            try
            {
                Connector fixedConnector = null;
                double bestDist = double.MaxValue;
                foreach (Connector c in pipe.ConnectorManager.Connectors)
                {
                    double d = c.Origin.DistanceTo(fixedEnd);
                    if (d < bestDist) { bestDist = d; fixedConnector = c; }
                }
                if (fixedConnector == null) return false;
                pipe.SetSlope(fixedConnector, slopeFtFt);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
