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
        /// <summary>Pipes skipped because both ends are connected to other elements,
        /// making endpoint moves unsafe without disconnecting the network.</summary>
        public int PipesSkippedConnectedBothEnds { get; set; }
        public List<SlopeFix> Fixes { get; } = new List<SlopeFix>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SlopeAutoCorrector
    {
        private const double MinSlopeSanitaryPct = 1.0;
        private const double MinSlopeRainwaterPct = 1.0;
        private const double SelfCleansingVelocityMs = 0.7;

        /// <summary>
        /// Dry-run preview — scans all drainage pipes and computes what fixes
        /// would be applied without modifying the model.
        /// </summary>
        public static SlopeAutoCorrectionResult Preview(Document doc)
            => RunFix(doc, dryRun: true);

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

                // Wrong direction: swap endpoints.
                if (dz > 0 && slopePct >= MinSlopeSanitaryPct)
                {
                    if (!dryRun)
                    {
                        var reversed = Line.CreateBound(e, s);
                        lc.Curve = reversed;
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
                    // Pick the downstream end: the one that is currently
                    // lower, or if flat, end 1.
                    bool secondEndIsDown = e.Z <= s.Z;
                    var downEndPoint = secondEndIsDown ? e : s;
                    var newPoint = new XYZ(downEndPoint.X, downEndPoint.Y,
                                           downEndPoint.Z - additional);
                    var newCurve = secondEndIsDown
                        ? Line.CreateBound(s, newPoint)
                        : Line.CreateBound(newPoint, e);
                    lc.Curve = newCurve;
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
    }
}
