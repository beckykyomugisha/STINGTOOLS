// DeadLegDetector — walks DCW / DHW / blended branches and flags legs longer
// than the limit that applies to them. Phase 178c; limits reworked DSCH-25.
//
// HSG274 Part 2 gives no numeric dead-leg length — its test is time to
// temperature (§2.82). The limits used here are design proxies, owned by
// Data/Plumbing/STING_TMV_STANDARDS.json and resolved by
// WaterSafetyLimits.DeadLegLimitFor:
//   * a branch to an outlet on a healthcare project: spur ≤ 3 m
//     (HTM 04-01 Pt A §12.5, measured from the main to the outlet);
//   * blended water downstream of a TMV: ≤ 2 m (HTM 04-01 Pt A §10.48);
//   * a hot branch elsewhere: BS 8558 uninsulated-pipe length by OD (VERIFY);
//   * a pipe end connected to nothing (capped / redundant branch): ≤ 2 × DN
//     (VERIFY — HSG274 §2.77 says only "as close as possible");
//   * a cold branch elsewhere: no sourced length — counted as NOT CHECKED.
// A missing or invalid data file flags nothing and says so.
//
// Writes PLM_DEAD_LEG_LENGTH_M back to the offending terminal pipe.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class DeadLegFinding
    {
        public ElementId TerminalPipeId  { get; set; }
        public double LegLengthM         { get; set; }
        public double LegPipeDiameterMm  { get; set; }
        public double LimitM             { get; set; }
        public string SystemName         { get; set; } = "";
        public string Severity           { get; set; } = "WARN";
        public string Notes              { get; set; } = "";
        /// <summary>Set when the limit is not confirmed against its standard.</summary>
        public string Verify             { get; set; } = "";
    }

    public class DeadLegResult
    {
        public List<DeadLegFinding> Findings { get; } = new List<DeadLegFinding>();
        public int PipesScanned   { get; set; }
        public int LegsChecked    { get; set; }
        public int LegsFlagged    { get; set; }
        public int LegsNotChecked { get; set; }
        public int PipesWritten   { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>Why legs were not checked, with a count each.</summary>
        public Dictionary<string, int> NotCheckedReasons { get; } = new Dictionary<string, int>();
    }

    public static class DeadLegDetector
    {
        public static DeadLegResult Scan(Document doc, bool writeBack)
        {
            var r = new DeadLegResult();
            if (doc == null) return r;

            var limits = PlumbingTables.WaterSafety;
            if (limits == null)
            {
                r.Warnings.Add("NOT CHECKED — STING_TMV_STANDARDS.json unusable, no dead-leg limits applied: "
                               + string.Join("; ", PlumbingTables.WaterSafetyErrors));
                return r;
            }
            bool healthcare = TMVEngine.IsHealthcareProject(doc);

            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
                .Where(IsPotableWater).ToList();
            r.PipesScanned = pipes.Count;
            if (pipes.Count == 0) return r;

            var pipeIds = new HashSet<long>(pipes.Select(p => p.Id.Value));

            foreach (var p in pipes)
            {
                try
                {
                    var end = TerminalKind(p);
                    if (end == LegEnd.None) continue;

                    var walk = TraverseToFirstBranch(p, pipeIds);
                    string sys = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                    bool blended = walk.HitTmv || sys.Contains("BLEND") || sys.Contains("TMV") || sys.Contains("TEMPERED");
                    bool hot = sys.Contains("DHW") || sys.Contains("HWS") || sys.Contains("DOMESTIC HOT") || sys.Contains("HOT");
                    double nominalMm = p.Diameter * 304.8;
                    double odMm = OutsideDiameterMm(p);

                    var lim = WaterSafetyLimits.DeadLegLimitFor(limits, healthcare,
                        openEnd: end == LegEnd.Open, blended: blended, hot: hot,
                        outsideDiameterMm: odMm, nominalDiameterMm: nominalMm);
                    if (!lim.LimitM.HasValue)
                    {
                        r.LegsNotChecked++;
                        r.NotCheckedReasons[lim.NotCheckedReason] =
                            r.NotCheckedReasons.TryGetValue(lim.NotCheckedReason, out int n) ? n + 1 : 1;
                        continue;
                    }
                    r.LegsChecked++;
                    if (walk.LengthM <= lim.LimitM.Value + 1e-9) continue;

                    var f = new DeadLegFinding
                    {
                        TerminalPipeId    = p.Id,
                        LegLengthM        = walk.LengthM,
                        LegPipeDiameterMm = nominalMm,
                        LimitM            = lim.LimitM.Value,
                        SystemName        = p.MEPSystem?.Name ?? "",
                        Severity          = walk.LengthM > lim.LimitM.Value * 2 ? "ERROR" : "WARN",
                        Notes             = $"{(end == LegEnd.Open ? "Open-ended leg" : "Branch")} {walk.LengthM:F2} m exceeds {lim.LimitM.Value:0.##} m — {lim.Basis}",
                        Verify            = lim.Verify ?? ""
                    };
                    r.Findings.Add(f);
                    r.LegsFlagged++;
                    if (writeBack)
                    {
                        try
                        {
                            var prm = p.LookupParameter(ParamRegistry.PLM_DEAD_LEG_M);
                            if (prm != null && !prm.IsReadOnly && prm.StorageType == StorageType.String)
                            {
                                prm.Set(walk.LengthM.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                                r.PipesWritten++;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"DeadLegDetector write {p.Id}: {ex.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"DeadLegDetector pipe {p.Id}: {ex.Message}");
                }
            }
            return r;
        }

        private static bool IsPotableWater(Pipe p)
        {
            try
            {
                var sys = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                return sys.Contains("DCW") || sys.Contains("DHW") || sys.Contains("HWS")
                    || sys.Contains("DOMESTIC COLD") || sys.Contains("DOMESTIC HOT")
                    || sys.Contains("BLEND") || sys.Contains("TEMP");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private enum LegEnd { None, Open, Outlet }

        // A leg starts at a pipe that either ends in nothing (an unconnected end —
        // a capped or redundant branch, or unfinished modelling) or feeds a
        // plumbing fixture (an outlet branch).
        private static LegEnd TerminalKind(Pipe p)
        {
            try
            {
                int connected = 0; bool feedsFixture = false;
                foreach (Connector c in p.ConnectorManager.Connectors)
                {
                    if (c.ConnectorType != ConnectorType.End) continue;
                    if (!c.IsConnected) continue;
                    connected++;
                    foreach (Connector o in c.AllRefs)
                        if (o.Owner?.Category?.Id?.Value == (long)BuiltInCategory.OST_PlumbingFixtures) feedsFixture = true;
                }
                if (connected <= 1) return LegEnd.Open;
                return feedsFixture ? LegEnd.Outlet : LegEnd.None;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return LegEnd.None; }
        }

        private struct Walk { public double LengthM; public bool HitTmv; }

        // Walk from the terminal pipe back through pipes, fittings and in-line
        // accessories until a node with ≥ 3 connections (the leg joins the main).
        // A TMV accessory (PLM_TMV_CLASS_TXT set) ends the walk: the length so far
        // is the blended pipe downstream of the mixing device.
        private static Walk TraverseToFirstBranch(Pipe start, HashSet<long> potablePipeIds)
        {
            var w = new Walk { LengthM = PipeLengthM(start) };
            var visited = new HashSet<long> { start.Id.Value };
            Element current = start;
            int safety = 200;
            while (safety-- > 0)
            {
                Element next = null;
                bool junction = false;
                try
                {
                    var cm = (current as MEPCurve)?.ConnectorManager
                          ?? (current as FamilyInstance)?.MEPModel?.ConnectorManager;
                    if (cm == null) break;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector other in c.AllRefs)
                        {
                            var owner = other.Owner;
                            if (owner == null || owner.Id == current.Id || visited.Contains(owner.Id.Value)) continue;
                            long cat = owner.Category?.Id?.Value ?? 0;
                            if (cat == (long)BuiltInCategory.OST_PlumbingFixtures) continue;
                            int neighbours = ConnectedCount(owner);
                            if (cat == (long)BuiltInCategory.OST_PipeAccessory && IsTmv(owner)) { w.HitTmv = true; junction = true; break; }
                            if (neighbours >= 3) { junction = true; break; }
                            if (owner is Pipe pp && potablePipeIds.Contains(pp.Id.Value)) { next = pp; break; }
                            if (cat == (long)BuiltInCategory.OST_PipeFitting || cat == (long)BuiltInCategory.OST_PipeAccessory) { next = owner; break; }
                        }
                        if (junction || next != null) break;
                    }
                }
                catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); break; }

                if (junction || next == null) break;
                visited.Add(next.Id.Value);
                if (next is Pipe np) w.LengthM += PipeLengthM(np);
                current = next;
            }
            return w;
        }

        private static int ConnectedCount(Element e)
        {
            try
            {
                var cm = (e as FamilyInstance)?.MEPModel?.ConnectorManager ?? (e as MEPCurve)?.ConnectorManager;
                if (cm == null) return 0;
                int n = 0;
                foreach (Connector c in cm.Connectors) if (c.IsConnected) n++;
                return n;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static bool IsTmv(Element e)
        {
            try
            {
                var p = e.LookupParameter(ParamRegistry.PLM_TMV_CLASS);
                return p != null && p.HasValue && !string.IsNullOrWhiteSpace(p.AsString());
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private static double OutsideDiameterMm(Pipe p)
        {
            try
            {
                var od = p.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
                if (od != null && od.HasValue && od.AsDouble() > 0) return od.AsDouble() * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"DeadLegDetector OD {p?.Id}: {ex.Message}"); }
            return 0;
        }

        // Use the built-in CURVE_ELEM_LENGTH so this works on non-English Revit
        // installs where LookupParameter("Length") would return null.
        private static double PipeLengthM(Pipe pipe)
        {
            try
            {
                var lp = pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (lp != null && lp.HasValue) return lp.AsDouble() * 0.3048;
            }
            catch (Exception ex) { StingLog.Warn($"PipeLengthM {pipe?.Id}: {ex.Message}"); }
            return 0;
        }
    }
}
