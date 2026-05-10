// BS EN 12056 slope auto-corrector. Phase 178a hardening:
// connector-preserving fix path for connected pipe ends.
//
// Topology-safety rule: rewriting LocationCurve.Curve on a pipe whose
// end is connected to a fitting silently disconnects the connector
// pair. We detect connected ends and use ElementTransformUtils.MoveElement
// on the attached fitting so Revit drags the pipe end along and keeps
// the connection alive. Free ends fall through to the LocationCurve
// path (safe — no connector to break).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Calc
{
    public enum ConnectorImpact
    {
        NoConnections,       // both ends free — LocationCurve rewrite safe
        FlipDirection,       // endpoints unchanged; connector roles re-evaluated
        MovedAttachedFitting,// connected fitting moved to preserve topology
        SkippedConnected     // both ends connected; manual intervention required
    }

    public class SlopeFix
    {
        public ElementId PipeId     { get; set; } = ElementId.InvalidElementId;
        public double OriginalPct   { get; set; }
        public double TargetPct     { get; set; }
        public double AppliedPct    { get; set; }
        public string Action        { get; set; } = "";
        public bool   Success       { get; set; }
        public string FailureReason { get; set; } = "";
        public ConnectorImpact ConnectorImpact { get; set; } = ConnectorImpact.NoConnections;
        public ElementId MovedFittingId { get; set; } = ElementId.InvalidElementId;
        public double DeltaZFt      { get; set; }
    }

    public class SlopeAutoCorrectionResult
    {
        public int PipesScanned     { get; set; }
        public int PipesFlipped     { get; set; }
        public int PipesDepressed   { get; set; }
        public int PipesUnchanged   { get; set; }
        public int PipesFailed      { get; set; }
        public int PipesSkippedConnectedBothEnds { get; set; }
        public int PipesFittingsMoved { get; set; }
        public List<SlopeFix> Fixes { get; } = new List<SlopeFix>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SlopeAutoCorrector
    {
        private const double MinSlopeSanitaryPct = 1.0;
        private const double MinSlopeRainwaterPct = 1.0;
        private const double SelfCleansingVelocityMs = 0.7;

        // Phase 178c preview path: caller can request the planned fix list
        // without committing, then re-invoke with applySelected = the subset
        // the user accepted in the SyncStyles-style preview grid.
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
                            if (fix.ConnectorImpact == ConnectorImpact.SkippedConnected)
                                result.PipesSkippedConnectedBothEnds++;
                            else
                                result.PipesFailed++;
                            result.Warnings.Add($"{pipe.Id}: {fix.FailureReason}");
                            continue;
                        }
                        if (fix.ConnectorImpact == ConnectorImpact.MovedAttachedFitting)
                            result.PipesFittingsMoved++;
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

                bool startConnected = HasConnectedNeighbour(pipe, s, out var startFittingId);
                bool endConnected   = HasConnectedNeighbour(pipe, e, out var endFittingId);

                // Wrong direction: swap endpoints. Reversing a pipe's
                // LocationCurve does not change the physical end positions —
                // Revit re-evaluates connector roles, so this is safe even
                // for connected pipes.
                if (dz > 0 && slopePct >= MinSlopeSanitaryPct)
                {
                    if (!dryRun)
                    {
                        var reversed = Line.CreateBound(e, s);
                        lc.Curve = reversed;
                    }
                    fix.Action          = "FLIP";
                    fix.AppliedPct      = slopePct;
                    fix.Success         = true;
                    fix.ConnectorImpact = (startConnected || endConnected)
                        ? ConnectorImpact.FlipDirection
                        : ConnectorImpact.NoConnections;
                    return fix;
                }

                if (slopePct >= MinSlopeSanitaryPct && dz < 0)
                {
                    fix.Action     = "UNCHANGED";
                    fix.AppliedPct = slopePct;
                    fix.Success    = true;
                    return fix;
                }

                double targetDropFt = (MinSlopeSanitaryPct / 100.0) * dxy;
                double currentDrop  = Math.Abs(dz);
                double additional   = targetDropFt - currentDrop;
                if (additional < 0) additional = 0;
                fix.DeltaZFt = additional;

                bool secondEndIsDown = e.Z <= s.Z;
                bool downEndConnected = secondEndIsDown ? endConnected : startConnected;
                ElementId downFittingId = secondEndIsDown ? endFittingId : startFittingId;

                if (downEndConnected && (secondEndIsDown ? startConnected : endConnected))
                {
                    fix.Action          = "SKIP";
                    fix.Success         = false;
                    fix.FailureReason   = "Both ends connected — manual intervention required";
                    fix.ConnectorImpact = ConnectorImpact.SkippedConnected;
                    return fix;
                }

                if (downEndConnected && downFittingId != ElementId.InvalidElementId)
                {
                    if (!dryRun && additional > 1e-6)
                    {
                        ElementTransformUtils.MoveElement(doc, downFittingId, new XYZ(0, 0, -additional));
                    }
                    fix.Action          = "DEPRESS";
                    fix.AppliedPct      = MinSlopeSanitaryPct;
                    fix.Success         = true;
                    fix.ConnectorImpact = ConnectorImpact.MovedAttachedFitting;
                    fix.MovedFittingId  = downFittingId;
                    return fix;
                }

                if (!dryRun && additional > 1e-6)
                {
                    var downEndPoint = secondEndIsDown ? e : s;
                    var newPoint = new XYZ(downEndPoint.X, downEndPoint.Y,
                                           downEndPoint.Z - additional);
                    var newCurve = secondEndIsDown
                        ? Line.CreateBound(s, newPoint)
                        : Line.CreateBound(newPoint, e);
                    lc.Curve = newCurve;
                }

                fix.Action          = "DEPRESS";
                fix.AppliedPct      = MinSlopeSanitaryPct;
                fix.Success         = true;
                fix.ConnectorImpact = ConnectorImpact.NoConnections;
                return fix;
            }
            catch (Exception ex)
            {
                fix.FailureReason = ex.Message;
                return fix;
            }
        }

        private static bool HasConnectedNeighbour(Pipe pipe, XYZ endPt, out ElementId neighbourId)
        {
            neighbourId = ElementId.InvalidElementId;
            try
            {
                var cm = pipe.ConnectorManager;
                if (cm == null) return false;
                foreach (Connector c in cm.Connectors)
                {
                    if (c.Origin.DistanceTo(endPt) > 0.005) continue;
                    if (!c.IsConnected) return false;
                    foreach (Connector other in c.AllRefs)
                    {
                        if (other.Owner == null) continue;
                        if (other.Owner.Id == pipe.Id) continue;
                        var cat = other.Owner.Category?.Id?.Value ?? 0;
                        if (cat == (long)BuiltInCategory.OST_PipeFitting ||
                            cat == (long)BuiltInCategory.OST_PipeAccessory ||
                            cat == (long)BuiltInCategory.OST_PlumbingFixtures ||
                            cat == (long)BuiltInCategory.OST_MechanicalEquipment ||
                            cat == (long)BuiltInCategory.OST_PipeCurves)
                        {
                            neighbourId = other.Owner.Id;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
