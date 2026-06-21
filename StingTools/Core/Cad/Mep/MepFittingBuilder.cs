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
        public List<string> Warnings { get; } = new List<string>();
        public int Created => Elbows + Tees + Crosses + Unions;
    }

    public class MepFittingBuilder
    {
        private readonly Document _doc;
        // Coincidence tolerance for two run ends meeting (~12 mm), in feet.
        private const double TolFt = 12.0 / 304.8;

        public MepFittingBuilder(Document doc) => _doc = doc ?? throw new ArgumentNullException(nameof(doc));

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
            int Key(double v) => (int)Math.Round(v / TolFt);
            var groups = ends
                .GroupBy(e => (Key(e.Origin.X), Key(e.Origin.Y), Key(e.Origin.Z), e.Domain))
                .Where(g => g.Count() >= 2)
                .ToList();

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

        private static bool SafeOpen(Connector c)
        {
            try { return c != null && !c.IsConnected; } catch { return false; }
        }

        private void TryElbowOrUnion(EndRef a, EndRef b, MepFittingBuildResult result)
        {
            try { _doc.Create.NewElbowFitting(a.Conn, b.Conn); result.Elbows++; return; }
            catch { /* fall through to a co-located union / direct connect */ }
            try { a.Conn.ConnectTo(b.Conn); result.Unions++; }
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
            try { _doc.Create.NewTeeFitting(ends[mi].Conn, ends[mj].Conn, ends[bi].Conn); result.Tees++; }
            catch (Exception ex) { result.Failed++; if (result.Warnings.Count < 30) result.Warnings.Add($"Tee: {ex.Message}"); }
        }

        private void TryCross(List<EndRef> ends, MepFittingBuildResult result)
        {
            try { _doc.Create.NewCrossFitting(ends[0].Conn, ends[1].Conn, ends[2].Conn, ends[3].Conn); result.Crosses++; }
            catch (Exception ex) { result.Failed++; if (result.Warnings.Count < 30) result.Warnings.Add($"Cross: {ex.Message}"); }
        }
    }
}
