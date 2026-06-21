// ============================================================================
// MepFittingBuilder.cs — Phase: MEP-from-DWG V3.
//
// After the straight runs are placed (MepRunBuilder), this finds where run
// ENDS coincide and inserts the appropriate fitting so the runs form connected
// systems: 2 ends → elbow (or in-line union), 3 → tee, 4 → cross. It works off
// the runs' own end Connectors (the canonical MEPCurve.ConnectorManager path),
// groups them by coincident origin + matching domain, and attempts the fitting.
//
// Fitting creation is genuinely runtime-fragile (the type's routing preferences
// must carry a fitting family; sizes/systems must agree), so EVERY attempt is
// guarded and counted — a failure is skipped, never thrown. TODO-VERIFY-API: the
// NewElbow/Tee/CrossFitting behaviour varies by family + routing prefs; verify
// in Revit.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Cad.Mep
{
    public class MepFittingBuildResult
    {
        public int Elbows { get; set; }
        public int Tees { get; set; }
        public int Crosses { get; set; }
        public int Unions { get; set; }
        public int Failed { get; set; }
        public int Junctions { get; set; }
        /// <summary>P1.4 — risers whose base connected to a horizontal run vs left floating.</summary>
        public int JoinedRisers { get; set; }
        public int FloatingRisers { get; set; }
        /// <summary>P5 — element ids of the fitting families created (for stamping + Replace cleanup).</summary>
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        /// <summary>P5 2.1 — mid-run branch taps found / teed / unsupported (conduit/tray).</summary>
        public int TapsFound { get; set; }
        public int TapsJoined { get; set; }
        public int TapsUnsupported { get; set; }
        /// <summary>P5 2.2 — approx. junctions just outside the join tolerance (raise tol to capture).</summary>
        public int NearMissJunctions { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public int Created => Elbows + Tees + Crosses + Unions;
    }

    public class MepFittingBuilder
    {
        private readonly Document _doc;
        // P5 2.2 — coincidence tolerance for two run ends meeting (default ~12 mm), in feet.
        private readonly double _tolFt;
        // Near-miss window: ends within (_tolFt, NearMissFt] are reported, not joined.
        private const double NearMissMm = 50.0;
        private readonly double _nearMissFt;

        public MepFittingBuilder(Document doc, double tolMm = 12.0)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _tolFt = (tolMm > 0 ? tolMm : 12.0) / 304.8;
            _nearMissFt = Math.Max(NearMissMm, tolMm * 2) / 304.8;
        }

        private sealed class EndRef
        {
            public Connector Conn;
            public XYZ Origin;
            public Domain Domain;
            public ElementId Owner;
            public XYZ Dir;     // connector facing direction
        }

        /// <summary>Insert fittings at coincident run ends. One transaction.</summary>
        public MepFittingBuildResult Build(IEnumerable<ElementId> runIds)
        {
            var result = new MepFittingBuildResult();
            if (runIds == null) return result;

            // Collect open END connectors from every run.
            var ends = new List<EndRef>();
            foreach (var id in runIds)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (!(_doc.GetElement(id) is MEPCurve mc)) continue;
                var cm = mc.ConnectorManager;
                if (cm == null) continue;
                try
                {
                    foreach (Connector c in cm.Connectors)
                    {
                        if (c.ConnectorType != ConnectorType.End) continue;
                        XYZ dir = XYZ.BasisZ;
                        try { dir = c.CoordinateSystem.BasisZ; } catch { }
                        ends.Add(new EndRef { Conn = c, Origin = c.Origin, Domain = c.Domain, Owner = id, Dir = dir });
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"Connectors of {id}: {ex.Message}"); }
            }
            if (ends.Count < 2) return result;

            // Group by coincident origin + domain.
            int Key(double v) => (int)Math.Round(v / _tolFt);
            var groups = ends
                .GroupBy(e => (Key(e.Origin.X), Key(e.Origin.Y), Key(e.Origin.Z), e.Domain))
                .Where(g => g.Count() >= 2)
                .ToList();

            // P5 2.2 — near-miss: ends that cluster at the wider tolerance but NOT at the
            // join tolerance (small corner gaps in the DWG). Reported so they're not silent.
            int CoarseKey(double v) => (int)Math.Round(v / _nearMissFt);
            int coarseJunctions = ends
                .GroupBy(e => (CoarseKey(e.Origin.X), CoarseKey(e.Origin.Y), CoarseKey(e.Origin.Z), e.Domain))
                .Count(g => g.Count() >= 2);
            result.NearMissJunctions = Math.Max(0, coarseJunctions - groups.Count);
            if (result.NearMissJunctions > 0)
                result.Warnings.Add($"≈{result.NearMissJunctions} junction(s) within {12:F0}–{NearMissMm:F0} mm not joined — raise the fitting tolerance to capture.");

            using (var tx = new Transaction(_doc, "STING MODEL: Insert MEP Fittings"))
            {
                tx.Start();
                try
                {
                    foreach (var g in groups)
                    {
                        // Re-check open-ness at use time (a prior fitting may have connected one).
                        var open = g.Where(e => SafeOpen(e.Conn) && e.Owner != ElementId.InvalidElementId).ToList();
                        // Drop ends whose owner appears twice in the same group (degenerate run).
                        open = open.GroupBy(e => e.Owner).Select(x => x.First()).ToList();
                        if (open.Count < 2) continue;
                        result.Junctions++;

                        try
                        {
                            switch (open.Count)
                            {
                                case 2: TryElbowOrUnion(open[0], open[1], result); break;
                                case 3: TryTee(open, result); break;
                                case 4: TryCross(open, result); break;
                                default:
                                    result.Warnings.Add($"Junction with {open.Count} ends not supported — skipped.");
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            if (result.Warnings.Count < 30) result.Warnings.Add($"Junction: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    StingLog.Error("MepFittingBuilder.Build", ex);
                    result.Warnings.Add($"Fitting batch failed (rolled back): {ex.Message}");
                    return new MepFittingBuildResult();
                }
            }
            return result;
        }

        /// <summary>P5 2.1 — mid-run branch taps. An open run END that lands on ANOTHER run's
        /// BODY (within tol, interior) is the most common MEP topology and the end-coincidence
        /// pass misses it. Break the main at that point and tee the three resulting ends.
        /// Pipe + Duct only (BreakCurve support); conduit/tray are counted unsupported.
        /// One transaction. TODO-VERIFY-API: PlumbingUtils/MechanicalUtils.BreakCurve.</summary>
        public void BuildMidRunTaps(IEnumerable<ElementId> runIds, MepFittingBuildResult result)
        {
            if (runIds == null) return;
            var ours = new HashSet<ElementId>(runIds.Where(id => id != null && id != ElementId.InvalidElementId));
            if (ours.Count < 2) return;

            // Snapshot open END connector origins as branch candidates (owner + point).
            var branches = new List<(ElementId owner, XYZ origin)>();
            foreach (var id in ours)
            {
                if (!(_doc.GetElement(id) is MEPCurve mc) || mc.ConnectorManager == null) continue;
                try
                {
                    foreach (Connector c in mc.ConnectorManager.Connectors)
                        if (c.ConnectorType == ConnectorType.End && !c.IsConnected) branches.Add((id, c.Origin));
                }
                catch { }
            }
            if (branches.Count == 0) return;

            using (var tx = new Transaction(_doc, "STING MODEL: Insert MEP Branch Taps"))
            {
                tx.Start();
                try
                {
                    foreach (var br in branches)
                    {
                        var mainId = FindBodyHit(ours, br.owner, br.origin, out XYZ breakPt);
                        if (mainId == null) continue;
                        result.TapsFound++;

                        var mainEl = _doc.GetElement(mainId) as MEPCurve;
                        bool isPipe = mainEl is Autodesk.Revit.DB.Plumbing.Pipe;
                        bool isDuct = mainEl is Autodesk.Revit.DB.Mechanical.Duct;
                        if (!isPipe && !isDuct) { result.TapsUnsupported++; continue; }   // conduit/tray: no BreakCurve

                        try
                        {
                            ElementId newId = isPipe
                                ? Autodesk.Revit.DB.Plumbing.PlumbingUtils.BreakCurve(_doc, mainId, breakPt)     // TODO-VERIFY-API
                                : Autodesk.Revit.DB.Mechanical.MechanicalUtils.BreakCurve(_doc, mainId, breakPt); // TODO-VERIFY-API
                            if (newId == null || newId == ElementId.InvalidElementId) { result.Failed++; continue; }
                            ours.Add(newId);
                            result.CreatedIds.Add(newId);   // the new half is part of the conversion (stamp + Replace cleanup)

                            var c1 = EndConnectorNear(mainId, breakPt);
                            var c2 = EndConnectorNear(newId, breakPt);
                            var cb = EndConnectorNear(br.owner, br.origin);
                            if (c1 == null || c2 == null || cb == null) { result.Failed++; continue; }

                            var f = _doc.Create.NewTeeFitting(c1, c2, cb);
                            Track(result, f);
                            result.TapsJoined++;
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            if (result.Warnings.Count < 30) result.Warnings.Add($"Branch tap: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    StingLog.Error("MepFittingBuilder.BuildMidRunTaps", ex);
                    result.Warnings.Add($"Branch-tap batch failed (rolled back): {ex.Message}");
                }
            }
        }

        // A main run whose body passes within tol of p (interior, not at its own ends).
        private ElementId FindBodyHit(HashSet<ElementId> ours, ElementId branchOwner, XYZ p, out XYZ breakPt)
        {
            breakPt = p;
            foreach (var id in ours)
            {
                if (id == branchOwner) continue;
                if (!(_doc.GetElement(id) is MEPCurve mc)) continue;
                if (!(mc.Location is LocationCurve lc) || !(lc.Curve is Line ln)) continue;
                XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
                if (p.DistanceTo(a) <= _tolFt || p.DistanceTo(b) <= _tolFt) continue; // that's an end-junction
                var cp = ClosestPointOnSegment(a, b, p, out double t, out double dist);
                if (dist <= _tolFt && t > 1e-3 && t < 1.0 - 1e-3) { breakPt = cp; return id; }
            }
            return null;
        }

        private Connector EndConnectorNear(ElementId id, XYZ pt)
        {
            if (!(_doc.GetElement(id) is MEPCurve mc) || mc.ConnectorManager == null) return null;
            Connector best = null; double bestD = double.MaxValue;
            try
            {
                foreach (Connector c in mc.ConnectorManager.Connectors)
                {
                    if (c.ConnectorType != ConnectorType.End) continue;
                    double d = c.Origin.DistanceTo(pt);
                    if (d < bestD) { bestD = d; best = c; }
                }
            }
            catch { }
            return best;
        }

        private static XYZ ClosestPointOnSegment(XYZ a, XYZ b, XYZ p, out double t, out double dist)
        {
            var ab = b - a; double len2 = ab.DotProduct(ab);
            t = len2 > 1e-12 ? (p - a).DotProduct(ab) / len2 : 0;
            t = Math.Max(0, Math.Min(1, t));
            var cp = a + t * ab; dist = cp.DistanceTo(p); return cp;
        }

        private static bool SafeOpen(Connector c)
        {
            try { return c != null && !c.IsConnected; } catch { return false; }
        }

        /// <summary>P1.4 — read-only count of risers whose base/top end now connects to a run.
        /// Run after Build (which joins coincident ends): a riser based at the run elevation
        /// and sharing a run's XY will have been joined; otherwise it is floating.</summary>
        public void CountRiserJoins(IEnumerable<ElementId> riserIds, MepFittingBuildResult result)
        {
            if (riserIds == null) return;
            foreach (var id in riserIds)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (!(_doc.GetElement(id) is MEPCurve mc) || mc.ConnectorManager == null) continue;
                bool anyConnected = false;
                try
                {
                    foreach (Connector c in mc.ConnectorManager.Connectors)
                        if (c.ConnectorType == ConnectorType.End && c.IsConnected) { anyConnected = true; break; }
                }
                catch { }
                if (anyConnected) result.JoinedRisers++; else result.FloatingRisers++;
            }
        }

        private void TryElbowOrUnion(EndRef a, EndRef b, MepFittingBuildResult result)
        {
            try { var f = _doc.Create.NewElbowFitting(a.Conn, b.Conn); Track(result, f); result.Elbows++; return; }
            catch { /* fall through to a co-located union / direct connect */ }
            try { a.Conn.ConnectTo(b.Conn); result.Unions++; }   // union: no new element to track
            catch (Exception ex) { result.Failed++; if (result.Warnings.Count < 30) result.Warnings.Add($"Elbow/union: {ex.Message}"); }
        }

        private void TryTee(List<EndRef> ends, MepFittingBuildResult result)
        {
            // Main = the two ends whose directions are most anti-parallel (collinear through-run);
            // the remaining end is the branch.
            int mi = 0, mj = 1; double best = double.MaxValue;
            for (int i = 0; i < ends.Count; i++)
                for (int j = i + 1; j < ends.Count; j++)
                {
                    double dot = ends[i].Dir.DotProduct(ends[j].Dir);   // ~ -1 for through-run
                    if (dot < best) { best = dot; mi = i; mj = j; }
                }
            int bi = Enumerable.Range(0, ends.Count).First(k => k != mi && k != mj);
            try { var f = _doc.Create.NewTeeFitting(ends[mi].Conn, ends[mj].Conn, ends[bi].Conn); Track(result, f); result.Tees++; }
            catch (Exception ex) { result.Failed++; if (result.Warnings.Count < 30) result.Warnings.Add($"Tee: {ex.Message}"); }
        }

        private void TryCross(List<EndRef> ends, MepFittingBuildResult result)
        {
            try { var f = _doc.Create.NewCrossFitting(ends[0].Conn, ends[1].Conn, ends[2].Conn, ends[3].Conn); Track(result, f); result.Crosses++; }
            catch (Exception ex) { result.Failed++; if (result.Warnings.Count < 30) result.Warnings.Add($"Cross: {ex.Message}"); }
        }

        private static void Track(MepFittingBuildResult result, Element fitting)
        {
            if (fitting?.Id != null && fitting.Id != ElementId.InvalidElementId) result.CreatedIds.Add(fitting.Id);
        }

        // ── P3.2 routing-preference pre-flight (read-only) ───────────────────
        /// <summary>Whether a run kind's resolved type carries the fitting families
        /// (routing preferences) needed for junctions to form. Surfaced in preview so the
        /// user knows BEFORE placing that fittings will (or won't) be created.</summary>
        public class RoutingPrefStatus
        {
            public MepRunKind Kind;
            public string TypeName = "";
            public bool TypeFound;
            public bool HasElbow;
            public bool HasJunction;   // tees
            public bool Ok => TypeFound && HasElbow && HasJunction;
        }

        /// <summary>For each kind present, inspect the first-available type's
        /// RoutingPreferenceManager for elbow + junction (tee) rules. TODO-VERIFY-API:
        /// RoutingPreferenceManager / RoutingPreferenceRuleGroupType against Revit 2025.</summary>
        public static List<RoutingPrefStatus> PreflightRoutingPrefs(Document doc, IEnumerable<MepRunKind> kinds)
        {
            var outl = new List<RoutingPrefStatus>();
            if (doc == null || kinds == null) return outl;
            foreach (var kind in kinds.Distinct())
            {
                var st = new RoutingPrefStatus { Kind = kind };
                var t = FirstCurveType(doc, kind);
                if (t != null)
                {
                    st.TypeFound = true; st.TypeName = t.Name;
                    try
                    {
                        var rpm = t.RoutingPreferenceManager;   // TODO-VERIFY-API
                        if (rpm != null)
                        {
                            st.HasElbow = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows) > 0;
                            st.HasJunction = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Junctions) > 0;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Routing prefs {kind}: {ex.Message}"); }
                }
                outl.Add(st);
            }
            return outl;
        }

        private static MEPCurveType FirstCurveType(Document doc, MepRunKind kind)
        {
            System.Type t;
            switch (kind)
            {
                case MepRunKind.Duct:      t = typeof(Autodesk.Revit.DB.Mechanical.DuctType); break;
                case MepRunKind.Pipe:      t = typeof(Autodesk.Revit.DB.Plumbing.PipeType); break;
                case MepRunKind.Conduit:   t = typeof(Autodesk.Revit.DB.Electrical.ConduitType); break;
                case MepRunKind.CableTray: t = typeof(Autodesk.Revit.DB.Electrical.CableTrayType); break;
                default: return null;
            }
            return new FilteredElementCollector(doc).OfClass(t).FirstElement() as MEPCurveType;
        }
    }
}
