// DeadLegDetector — graph walk on DCW / DHW / blended pipes flagging
// legs longer than 5×D or 5 m (HSG 274 Part 2 Legionella guidance).
// Phase 178c. Reads PLM_DEAD_LEG_LENGTH_M back to the offending pipes.

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
        public string SystemName         { get; set; } = "";
        public string Severity           { get; set; } = "WARN";
        public string Notes              { get; set; } = "";
    }

    public class DeadLegResult
    {
        public List<DeadLegFinding> Findings { get; } = new List<DeadLegFinding>();
        public int PipesScanned   { get; set; }
        public int LegsFlagged    { get; set; }
        public int PipesWritten   { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class DeadLegDetector
    {
        public static DeadLegResult Scan(Document doc, bool writeBack)
        {
            var r = new DeadLegResult();
            if (doc == null) return r;

            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
                .Where(IsPotableWater).ToList();
            r.PipesScanned = pipes.Count;
            if (pipes.Count == 0) return r;

            var pipeIds = new HashSet<long>(pipes.Select(p => p.Id.Value));

            foreach (var p in pipes)
            {
                try
                {
                    if (!IsTerminal(p)) continue;
                    double dia = p.Diameter * 0.3048; // m
                    double diaMm = dia * 1000.0;
                    double thresholdM = Math.Max(5.0, 5.0 * dia);
                    double accLen = TraverseToFirstBranch(doc, p, pipeIds);
                    if (accLen > thresholdM)
                    {
                        var f = new DeadLegFinding
                        {
                            TerminalPipeId   = p.Id,
                            LegLengthM       = accLen,
                            LegPipeDiameterMm= diaMm,
                            SystemName       = p.MEPSystem?.Name ?? "",
                            Severity         = accLen > thresholdM * 2 ? "ERROR" : "WARN",
                            Notes            = $"Leg {accLen:F1} m exceeds {thresholdM:F1} m (HSG 274 Part 2)"
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
                                    prm.Set(accLen.ToString("F2"));
                                    r.PipesWritten++;
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
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

        // A pipe is a "terminal" leg if exactly one end is connected
        // to anything else and the other end is free (or connected
        // to a fixture/terminal device).
        private static bool IsTerminal(Pipe p)
        {
            try
            {
                int connectedEnds = 0;
                foreach (Connector c in p.ConnectorManager.Connectors)
                    if (c.IsConnected) connectedEnds++;
                return connectedEnds <= 1;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        // Walk from the terminal pipe back through fittings until we
        // hit a node with branch = ≥3 connectors (i.e., the leg joins
        // the main loop). Sums pipe lengths along the way.
        private static double TraverseToFirstBranch(Document doc, Pipe start, HashSet<long> potablePipeIds)
        {
            double accLen = 0;
            var visited = new HashSet<long>();
            visited.Add(start.Id.Value);
            accLen += PipeLengthM(start);

            Element current = start;
            int safety = 200;
            while (safety-- > 0)
            {
                Element nextPipe = null;
                Element junction = null;
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
                            int neighbours = 0;
                            try
                            {
                                var ocm = (owner as FamilyInstance)?.MEPModel?.ConnectorManager
                                       ?? (owner as MEPCurve)?.ConnectorManager;
                                if (ocm != null)
                                    foreach (Connector oc in ocm.Connectors)
                                        if (oc.IsConnected) neighbours++;
                            }
                            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                            if (neighbours >= 3) { junction = owner; break; }
                            if (owner is Pipe pp && potablePipeIds.Contains(pp.Id.Value)) { nextPipe = pp; break; }
                            if (owner.Category?.Id?.Value == (long)BuiltInCategory.OST_PipeFitting) { nextPipe = owner; break; }
                        }
                        if (junction != null || nextPipe != null) break;
                    }
                }
                catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); break; }

                if (junction != null) break;
                if (nextPipe == null) break;
                visited.Add(nextPipe.Id.Value);
                if (nextPipe is Pipe np) accLen += PipeLengthM(np);
                current = nextPipe;
            }
            return accLen;
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
