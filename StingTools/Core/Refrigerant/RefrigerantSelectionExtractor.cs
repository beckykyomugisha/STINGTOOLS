// StingTools — Refrigerant sizing auto-population from the model.
//
// Closes gap 2.2 (HVAC gap remediation): the RefrigerantSizingDialog was
// dialog-only — capacity, equivalent length and lift were typed by hand.
// This helper pre-fills those inputs from the current Revit selection so
// STING starts to approach a Daikin-VRV-Xpress-style workflow.
//
// Two selection shapes are supported:
//   1. A VRF outdoor unit (ODU) — mechanical equipment.  Capacity is read
//      from HVC_CAPACITY_KW (summed over served indoor units where the
//      connector graph reaches them, otherwise the ODU's own value); the
//      equivalent length is the traced refrigerant-pipe run length from the
//      ODU; the lift is the world-Z delta between the ODU connector and the
//      farthest reachable IDU connector.
//   2. One or more refrigerant pipe segments — capacity from any equipment
//      touched by the run (else left at the manual default); equivalent
//      length is the summed straight length of the selected + connected
//      refrigerant pipe run + a fitting-equivalent allowance; lift is the
//      Z-span of the run's connector origins.
//
// The connector-graph walk reuses the traversal shape from
// HvacSegmentRoleDetector / PipeServiceDetector (Connector.AllRefs +
// Owner + a visited set + a depth guard). Refrigerant systems in Revit
// are Piping systems (Domain.DomainPiping); we filter the walk to pipe /
// equipment owners so a stray duct connector on a ducted IDU doesn't
// derail the trace.
//
// Every field is a *suggestion*. The dialog leaves them editable and the
// command never throws on an extraction failure — a failed field simply
// falls back to the manual default and is logged via StingLog.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Refrigerant
{
    /// <summary>
    /// Result of scanning the current selection for refrigerant-sizing inputs.
    /// Any field left at its sentinel (&lt;= 0 for capacity/length; HasLift
    /// false for lift) means "no model value — keep the manual default".
    /// </summary>
    public class RefrigerantSelectionResult
    {
        public bool   HasAnything      { get; set; }
        public double CapacityKw       { get; set; }
        public double EquivLengthM     { get; set; }
        public double LiftM            { get; set; }
        public bool   HasLift          { get; set; }
        public bool   HasVerticalRiser { get; set; }
        /// <summary>Number of IDUs whose capacity was summed (0 = ODU own value / none).</summary>
        public int    IduCount         { get; set; }
        /// <summary>Number of refrigerant pipe segments walked for length.</summary>
        public int    PipeCount        { get; set; }
        /// <summary>Human-readable summary of what was traced vs. defaulted —
        /// shown to the user so they know the numbers aren't authoritative.</summary>
        public string Note             { get; set; } = "";
        /// <summary>True when the length is a partial trace (couldn't reach a
        /// terminal / summed only the selected run) — surfaced in the Note.</summary>
        public bool   PartialTrace     { get; set; }
    }

    public static class RefrigerantSelectionExtractor
    {
        public const double FtToM = 0.3048;
        private const int MaxTraversal = 200;   // large VRF systems can carry many segments
        // Fitting equivalent-length allowance as a fraction of straight length
        // when we can't enumerate fittings individually. VRF designers commonly
        // add ~30% to actual length to approximate equivalent length; this is a
        // documented, editable suggestion — not an authoritative Leq.
        private const double FittingEquivFraction = 0.30;

        public const string CapacityParam = ParamRegistry.HVC_CAPACITY_KW;

        /// <summary>
        /// Scan the given selection for refrigerant-sizing inputs. Never throws;
        /// on any failure returns a result with HasAnything=false so the caller
        /// falls back to the manual dialog exactly as before.
        /// </summary>
        public static RefrigerantSelectionResult Extract(Document doc, ICollection<ElementId> selection)
        {
            var res = new RefrigerantSelectionResult();
            if (doc == null || selection == null || selection.Count == 0) return res;
            try
            {
                var elements = selection
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null)
                    .ToList();

                // Partition the selection.
                var equipment = elements
                    .OfType<FamilyInstance>()
                    .Where(IsMechanicalEquipment)
                    .ToList();
                var pipes = elements.OfType<Pipe>().ToList();

                if (equipment.Count > 0)
                    return FromEquipment(doc, equipment, pipes);
                if (pipes.Count > 0)
                    return FromPipes(doc, pipes);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RefrigerantSelectionExtractor.Extract: {ex.Message}");
            }
            return res;
        }

        // ── ODU-anchored extraction ─────────────────────────────────────────

        private static RefrigerantSelectionResult FromEquipment(
            Document doc, List<FamilyInstance> equipment, List<Pipe> selectedPipes)
        {
            var res = new RefrigerantSelectionResult { HasAnything = true };
            var notes = new List<string>();

            // Treat the first equipment element with refrigerant connectors as
            // the ODU anchor. If several are selected, prefer the one with the
            // largest own HVC_CAPACITY_KW (usually the ODU vs. an IDU).
            var odu = equipment
                .OrderByDescending(e => ReadDouble(e, CapacityParam))
                .FirstOrDefault(HasRefrigerantConnector) ?? equipment[0];

            // 1. Walk the refrigerant graph from the ODU to gather reachable
            //    IDUs + pipe segments + connector Z-range in one pass.
            var trace = WalkRefrigerantNetwork(doc, odu);

            // 2. Capacity: sum served-IDU capacity when we reached any; else
            //    fall back to the ODU's own stamped capacity.
            double iduCap = 0; int iduCount = 0;
            foreach (var idu in trace.IndoorUnits)
            {
                double c = ReadDouble(idu, CapacityParam);
                if (c > 0) { iduCap += c; iduCount++; }
            }
            if (iduCap > 0)
            {
                res.CapacityKw = iduCap;
                res.IduCount = iduCount;
                notes.Add($"capacity = Σ {iduCount} served IDU {CapacityParam} ({iduCap:F1} kW)");
            }
            else
            {
                double own = ReadDouble(odu, CapacityParam);
                if (own > 0)
                {
                    res.CapacityKw = own;
                    notes.Add($"capacity = ODU {CapacityParam} ({own:F1} kW); no IDU capacities reachable");
                }
                else
                {
                    notes.Add($"capacity: no {CapacityParam} on ODU or IDUs — kept manual default");
                }
            }

            // 3. Equivalent length: straight run + fitting allowance.
            if (trace.StraightLengthM > 0)
            {
                double equiv = trace.StraightLengthM * (1.0 + FittingEquivFraction);
                res.EquivLengthM = equiv;
                res.PipeCount = trace.PipeCount;
                notes.Add($"L_eq ≈ {trace.StraightLengthM:F1} m straight × (1+{FittingEquivFraction:P0} fittings) = {equiv:F1} m over {trace.PipeCount} segment(s)");
                if (!trace.ReachedTerminal)
                {
                    res.PartialTrace = true;
                    notes.Add("length is a PARTIAL trace (no IDU terminal reached) — verify manually");
                }
            }
            else if (selectedPipes.Count > 0)
            {
                double sum = selectedPipes.Sum(p => SafeLengthM(p));
                if (sum > 0)
                {
                    res.EquivLengthM = sum * (1.0 + FittingEquivFraction);
                    res.PipeCount = selectedPipes.Count;
                    res.PartialTrace = true;
                    notes.Add($"L_eq from selected pipes only ({sum:F1} m ×1.3) — no traced run from ODU");
                }
            }
            else
            {
                notes.Add("equivalent length: no refrigerant pipe run reachable from ODU — kept manual default");
            }

            // 4. Lift: Z-span of connector origins between ODU and reachable IDUs.
            if (trace.HasZRange)
            {
                // Sign convention matches the solver: +lift = ODU above IDU.
                res.LiftM = trace.OduZM - trace.MinIduZM;
                res.HasLift = true;
                res.HasVerticalRiser = Math.Abs(res.LiftM) > 0.5;
                notes.Add($"lift = ODU z {trace.OduZM:F1} m − far-IDU z {trace.MinIduZM:F1} m = {res.LiftM:+0.0;-0.0;0.0} m");
            }
            else
            {
                notes.Add("lift: no IDU connector reached — kept manual default");
            }

            res.Note = string.Join("; ", notes);
            return res;
        }

        // ── Pipe-anchored extraction ────────────────────────────────────────

        private static RefrigerantSelectionResult FromPipes(Document doc, List<Pipe> pipes)
        {
            var res = new RefrigerantSelectionResult { HasAnything = true, PartialTrace = true };
            var notes = new List<string>();

            // Grow the selected run through the connected refrigerant graph so
            // the user can pick one segment and get the whole leg's length.
            var runPipes = GrowPipeRun(doc, pipes);
            double sum = runPipes.Sum(p => SafeLengthM(p));
            if (sum > 0)
            {
                res.EquivLengthM = sum * (1.0 + FittingEquivFraction);
                res.PipeCount = runPipes.Count;
                notes.Add($"L_eq ≈ {sum:F1} m straight × (1+{FittingEquivFraction:P0}) = {res.EquivLengthM:F1} m over {runPipes.Count} connected segment(s)");
            }

            // Lift: Z-span across the run's connector origins.
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var p in runPipes)
            {
                foreach (var c in ConnectorOrigins(p))
                {
                    if (c.Z < minZ) minZ = c.Z;
                    if (c.Z > maxZ) maxZ = c.Z;
                }
            }
            if (maxZ > minZ)
            {
                // ConnectorOrigins returns raw Revit XYZ (feet) — convert to metres.
                res.LiftM = (maxZ - minZ) * FtToM;
                res.HasLift = true;
                res.HasVerticalRiser = res.LiftM > 0.5;
                notes.Add($"lift = run Z-span {res.LiftM:F1} m (magnitude only from pipe selection)");
            }

            // Capacity: try any equipment touched by the run.
            double cap = 0; int n = 0;
            var seen = new HashSet<ElementId>();
            foreach (var p in runPipes)
            {
                foreach (var owner in TouchingEquipment(p))
                {
                    if (!seen.Add(owner.Id)) continue;
                    double c = ReadDouble(owner, CapacityParam);
                    if (c > 0) { cap += c; n++; }
                }
            }
            if (cap > 0)
            {
                res.CapacityKw = cap;
                res.IduCount = n;
                notes.Add($"capacity = Σ {n} equipment {CapacityParam} touching the run ({cap:F1} kW)");
            }
            else
            {
                notes.Add($"capacity: no equipment {CapacityParam} on the run — kept manual default");
            }

            notes.Add("pipe-anchored trace — verify capacity/length against the design schedule");
            res.Note = string.Join("; ", notes);
            return res;
        }

        // ── Connector-graph traversal ───────────────────────────────────────

        private class NetworkTrace
        {
            public readonly List<FamilyInstance> IndoorUnits = new List<FamilyInstance>();
            public int    PipeCount       { get; set; }
            public double StraightLengthM { get; set; }
            public bool   ReachedTerminal { get; set; }
            public bool   HasZRange       { get; set; }
            public double OduZM           { get; set; }
            public double MinIduZM        { get; set; } = double.MaxValue;
        }

        /// <summary>
        /// Walk the refrigerant (piping-domain) connector graph outward from
        /// the ODU, summing pipe lengths and collecting reachable indoor units.
        /// </summary>
        private static NetworkTrace WalkRefrigerantNetwork(Document doc, FamilyInstance odu)
        {
            var t = new NetworkTrace();
            try
            {
                // ODU connector Z (lowest refrigerant connector — the pipe take-off).
                double? oduZ = LowestRefrigerantConnectorZ(odu);
                if (oduZ.HasValue) { t.OduZM = oduZ.Value * FtToM; t.HasZRange = true; }

                var visited = new HashSet<ElementId> { odu.Id };
                var queue = new Queue<Connector>();
                foreach (var c in RefrigerantConnectors(odu)) queue.Enqueue(c);

                int guard = 0;
                while (queue.Count > 0 && guard++ < MaxTraversal)
                {
                    var start = queue.Dequeue();
                    var refs = SafeAllRefs(start);
                    if (refs == null) continue;
                    foreach (Connector other in refs)
                    {
                        var owner = other?.Owner;
                        if (owner == null) continue;
                        if (!visited.Add(owner.Id)) continue;

                        if (owner is Pipe pipe)
                        {
                            if (!IsRefrigerantDomain(pipe)) continue;
                            t.PipeCount++;
                            t.StraightLengthM += SafeLengthM(pipe);
                            foreach (var oc in PipeConnectors(pipe))
                                if (oc.Id != other.Id) queue.Enqueue(oc);
                        }
                        else if (owner is FamilyInstance fi && IsMechanicalEquipment(fi))
                        {
                            // Reached another piece of equipment — treat as IDU
                            // if it isn't the ODU we started from.
                            t.IndoorUnits.Add(fi);
                            t.ReachedTerminal = true;
                            double? z = LowestRefrigerantConnectorZ(fi);
                            if (z.HasValue)
                            {
                                double zm = z.Value * FtToM;
                                if (zm < t.MinIduZM) t.MinIduZM = zm;
                                t.HasZRange = t.HasZRange && true;
                            }
                            // Continue through the IDU's other refrigerant
                            // connectors (loop / daisy-chained systems).
                            foreach (var oc in RefrigerantConnectors(fi))
                                if (oc.Id != other.Id) queue.Enqueue(oc);
                        }
                        else if (owner is FamilyInstance fitFi)
                        {
                            // Fitting / accessory — pass through its connectors.
                            foreach (var oc in FittingConnectors(fitFi))
                                if (oc.Id != other.Id) queue.Enqueue(oc);
                        }
                    }
                }

                if (t.MinIduZM == double.MaxValue) t.MinIduZM = t.OduZM; // no IDU → no lift
            }
            catch (Exception ex) { StingLog.Warn($"WalkRefrigerantNetwork: {ex.Message}"); }
            return t;
        }

        /// <summary>Grow a selected pipe set through the connected refrigerant graph.</summary>
        private static List<Pipe> GrowPipeRun(Document doc, List<Pipe> seed)
        {
            var found = new Dictionary<ElementId, Pipe>();
            foreach (var p in seed) if (p != null) found[p.Id] = p;
            try
            {
                var queue = new Queue<Pipe>(seed.Where(p => p != null));
                int guard = 0;
                while (queue.Count > 0 && guard++ < MaxTraversal)
                {
                    var pipe = queue.Dequeue();
                    foreach (var c in PipeConnectors(pipe))
                    {
                        var refs = SafeAllRefs(c);
                        if (refs == null) continue;
                        foreach (Connector other in refs)
                        {
                            if (other?.Owner is Pipe np && !found.ContainsKey(np.Id))
                            {
                                found[np.Id] = np;
                                queue.Enqueue(np);
                            }
                            else if (other?.Owner is FamilyInstance fit && !(fit is null))
                            {
                                // Hop through fittings so the run doesn't stop at an elbow.
                                foreach (var oc in FittingConnectors(fit))
                                {
                                    var frefs = SafeAllRefs(oc);
                                    if (frefs == null) continue;
                                    foreach (Connector fo in frefs)
                                        if (fo?.Owner is Pipe fp && !found.ContainsKey(fp.Id))
                                        { found[fp.Id] = fp; queue.Enqueue(fp); }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GrowPipeRun: {ex.Message}"); }
            return found.Values.ToList();
        }

        private static IEnumerable<FamilyInstance> TouchingEquipment(Pipe pipe)
        {
            foreach (var c in PipeConnectors(pipe))
            {
                var refs = SafeAllRefs(c);
                if (refs == null) continue;
                foreach (Connector other in refs)
                    if (other?.Owner is FamilyInstance fi && IsMechanicalEquipment(fi))
                        yield return fi;
            }
        }

        // ── Small helpers ───────────────────────────────────────────────────

        private static bool IsMechanicalEquipment(Element el)
        {
            try
            {
                var cat = el?.Category;
                if (cat == null) return false;
                return (BuiltInCategory)cat.Id.Value == BuiltInCategory.OST_MechanicalEquipment;
            }
            catch { return false; }
        }

        private static bool IsRefrigerantDomain(Pipe pipe)
        {
            // Refrigerant piping is Domain.DomainPiping. We accept any piping
            // pipe here; the walk is anchored on refrigerant connectors of the
            // ODU so a stray water pipe won't be reached in practice. If the
            // system abbreviation is available and clearly non-refrigerant we
            // could reject, but keeping it permissive avoids missing vendor
            // system-type naming variations.
            try
            {
                foreach (var c in PipeConnectors(pipe))
                    if (c.Domain == Domain.DomainPiping) return true;
            }
            catch { }
            return true;
        }

        private static bool HasRefrigerantConnector(FamilyInstance fi)
            => RefrigerantConnectors(fi).Any();

        private static IEnumerable<Connector> RefrigerantConnectors(FamilyInstance fi)
        {
            ConnectorSet set = null;
            try { set = fi?.MEPModel?.ConnectorManager?.Connectors; } catch { }
            if (set == null) yield break;
            foreach (Connector c in set)
            {
                bool ok = false;
                try { ok = c.Domain == Domain.DomainPiping; } catch { }
                if (ok) yield return c;
            }
        }

        private static IEnumerable<Connector> FittingConnectors(FamilyInstance fi)
        {
            ConnectorSet set = null;
            try { set = fi?.MEPModel?.ConnectorManager?.Connectors; } catch { }
            if (set == null) yield break;
            foreach (Connector c in set) yield return c;
        }

        private static IEnumerable<Connector> PipeConnectors(Pipe pipe)
        {
            ConnectorSet set = null;
            try { set = pipe?.ConnectorManager?.Connectors; } catch { }
            if (set == null) yield break;
            foreach (Connector c in set) yield return c;
        }

        private static IEnumerable<XYZ> ConnectorOrigins(Pipe pipe)
        {
            foreach (var c in PipeConnectors(pipe))
            {
                XYZ o = null;
                try { o = c.Origin; } catch { }
                if (o != null) yield return o;
            }
        }

        private static double? LowestRefrigerantConnectorZ(FamilyInstance fi)
        {
            double? z = null;
            foreach (var c in RefrigerantConnectors(fi))
            {
                try
                {
                    double cz = c.Origin.Z;
                    if (!z.HasValue || cz < z.Value) z = cz;
                }
                catch { }
            }
            return z;
        }

        private static ConnectorSet SafeAllRefsSet(Connector c)
        {
            try { return c?.AllRefs; } catch { return null; }
        }

        private static IEnumerable<Connector> SafeAllRefs(Connector c)
        {
            var set = SafeAllRefsSet(c);
            if (set == null) yield break;
            foreach (Connector r in set) yield return r;
        }

        private static double SafeLengthM(Pipe pipe)
        {
            try
            {
                // Prefer the built-in curve-length parameter (robust across
                // straight + flex pipe); fall back to the location curve.
                var p = pipe?.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double ft = p.AsDouble();
                    if (ft > 0) return ft * FtToM;
                }
                if (pipe?.Location is LocationCurve lc && lc.Curve != null)
                    return lc.Curve.Length * FtToM;
            }
            catch { }
            return 0;
        }

        private static double ReadDouble(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return v;
            }
            catch { }
            return 0;
        }
    }
}
