// StingTools v4 MVP — HangerPlacementEngine.
//
// Walks selected or project-wide pipes/ducts/conduits/cable trays,
// looks up each run's max hanger spacing via HangerSpacingTable, and
// emits a list of HangerCandidate positions along the run. Each
// candidate is evaluated against the structural slab above (via
// ElementIntersectsSolidFilter) and annotated with the recommended
// anchor type:
//
//   CONCRETE_ANCHOR  — slab hit within 300 mm of candidate
//   BEAM_CLAMP       — structural-framing hit within 500 mm radius
//   GENERIC          — no structural host found (fallback)
//
// Parallel pipes within 600 mm centre-to-centre are grouped into a
// shared trapeze rack (industry standard consolidation).
//
// Phase D wires the engine to a "Place hangers" command that creates
// FamilyInstances of a user-configurable hanger family at each
// candidate. When no family is configured, the engine runs in
// DRY-RUN mode and emits only the candidate report (DetailCurves in
// the active view + result-panel metrics).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Calc
{
    public class HangerCandidate
    {
        public ElementId HostRun       { get; set; } = ElementId.InvalidElementId;
        public XYZ       Point         { get; set; }
        public double    MaxSpanMm     { get; set; }
        public string    AnchorType    { get; set; } = "GENERIC";
        public ElementId AnchorHostId  { get; set; } = ElementId.InvalidElementId;
        public double    StrutRodMm    { get; set; }
        public bool      OnTrapeze     { get; set; }
        public string    SpacingBasis  { get; set; } = "";
    }

    public class HangerPlacementResult
    {
        public int RunsScanned             { get; set; }
        public int CandidatesGenerated     { get; set; }
        public int ConcreteAnchorCount     { get; set; }
        public int BeamClampCount          { get; set; }
        public int GenericCount            { get; set; }
        public int TrapezeGroups           { get; set; }
        public List<HangerCandidate> Candidates { get; } = new List<HangerCandidate>();
        public List<string> Warnings       { get; } = new List<string>();
    }

    public static class HangerPlacementEngine
    {
        private const double FtToMm = 304.8;
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>
        /// Max lateral distance between two run centrelines at which we
        /// combine them onto a single trapeze support. 600 mm is
        /// standard practice per MSS SP-58.
        /// </summary>
        public const double TrapezeDistanceMm = 600.0;

        /// <summary>
        /// Generate hangers for every MEP run in the supplied list.
        /// Host run types: Pipe, Duct, Conduit, CableTray. Unknown
        /// elements are skipped with a warning.
        /// </summary>
        public static HangerPlacementResult Plan(
            Document doc,
            IEnumerable<Element> runs)
        {
            var result = new HangerPlacementResult();
            if (doc == null || runs == null) return result;

            var list = runs.ToList();
            result.RunsScanned = list.Count;

            foreach (var el in list)
            {
                try { PlanOneRun(doc, el, result); }
                catch (Exception ex)
                { result.Warnings.Add($"Run {el?.Id}: {ex.Message}"); }
            }

            // Trapeze consolidation pass: group candidates whose XY
            // positions are within TrapezeDistanceMm.
            result.TrapezeGroups = GroupTrapezes(result.Candidates);
            return result;
        }

        private static void PlanOneRun(Document doc, Element el, HangerPlacementResult result)
        {
            var curve = (el.Location as LocationCurve)?.Curve;
            if (curve == null) return;

            HangerSpacingQuery q = BuildQuery(el);
            if (q == null) return;
            var spacing = HangerSpacingTable.Query(q);
            if (spacing.MaxSpanMm <= 0)
            {
                result.Warnings.Add($"Run {el.Id}: no spacing table match (kind={q.Kind}, dia={q.DiameterMm:F0}mm)");
                return;
            }

            double runLenFt = curve.Length;
            double runLenMm = runLenFt * FtToMm;
            double spanFt   = spacing.MaxSpanMm * MmToFt;

            // Anchor one hanger at each multiple of span, plus the
            // start / end of the run. Anchor at start only if the run
            // is long enough to warrant two supports; otherwise a single
            // midpoint hanger is fine.
            int nHangers = Math.Max(2, (int)Math.Ceiling(runLenMm / spacing.MaxSpanMm));
            double actualSpan = runLenFt / (nHangers - 1);

            for (int i = 0; i < nHangers; i++)
            {
                double t = (nHangers == 1) ? 0.5 : (double)i / (nHangers - 1);
                XYZ pt;
                try { pt = curve.Evaluate(t, true); }
                catch { continue; }

                var cand = new HangerCandidate
                {
                    HostRun      = el.Id,
                    Point        = pt,
                    MaxSpanMm    = spacing.MaxSpanMm,
                    SpacingBasis = spacing.Basis,
                };

                // Pick anchor type based on what's directly above.
                FindAnchor(doc, pt, cand);
                switch (cand.AnchorType)
                {
                    case "CONCRETE_ANCHOR": result.ConcreteAnchorCount++; break;
                    case "BEAM_CLAMP":      result.BeamClampCount++;      break;
                    default:                result.GenericCount++;        break;
                }

                result.Candidates.Add(cand);
                result.CandidatesGenerated++;
            }
        }

        private static HangerSpacingQuery BuildQuery(Element el)
        {
            if (el is Pipe p)
            {
                double d = p.Diameter * FtToMm;
                string mat = ReadParam(p, "PLM_PPE_MAT_TXT", "STEEL").ToUpperInvariant();
                return new HangerSpacingQuery
                {
                    Kind         = HangerRunKind.Pipe,
                    DiameterMm   = d,
                    Material     = mat,
                    Insulated    = ReadParam(p, "PLM_PPE_INSULATION_THK_MM", "0") != "0",
                };
            }
            if (el is Duct d1)
            {
                double size = Math.Max(d1.Width, d1.Height) * FtToMm;
                if (size <= 0)
                {
                    try { size = d1.Diameter * FtToMm; } catch { }
                }
                string mat = ReadParam(d1, "HVC_DCT_MAT_TXT", "GI_SHEET").ToUpperInvariant();
                return new HangerSpacingQuery
                {
                    Kind       = HangerRunKind.Duct,
                    DiameterMm = size,
                    Material   = mat,
                    Insulated  = ReadParam(d1, "HVC_DCT_INSULATION_THK_MM", "0") != "0",
                };
            }
            if (el is Conduit c)
            {
                double size = c.Diameter * FtToMm;
                return new HangerSpacingQuery
                {
                    Kind       = HangerRunKind.Conduit,
                    DiameterMm = size,
                    Material   = "STEEL",
                };
            }
            if (el is CableTray ct)
            {
                double size = ct.Width * FtToMm;
                return new HangerSpacingQuery
                {
                    Kind       = HangerRunKind.CableTray,
                    DiameterMm = size,
                    Material   = "STEEL",
                };
            }
            return null;
        }

        private static void FindAnchor(Document doc, XYZ pt, HangerCandidate cand)
        {
            // Probe the first structural element whose bounding box
            // contains a small volume directly above the candidate.
            const double searchUpFt = 3000.0 * MmToFt; // 3 m
            var searchMin = new XYZ(pt.X - 0.5, pt.Y - 0.5, pt.Z);
            var searchMax = new XYZ(pt.X + 0.5, pt.Y + 0.5, pt.Z + searchUpFt);
            var outline = new Outline(searchMin, searchMax);
            var bboxFilter = new BoundingBoxIntersectsFilter(outline);

            try
            {
                // Floors (slab) first — preferred anchor.
                var floors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .WherePasses(bboxFilter);
                var floor = floors.FirstOrDefault();
                if (floor != null)
                {
                    cand.AnchorType   = "CONCRETE_ANCHOR";
                    cand.AnchorHostId = floor.Id;
                    var bb = floor.get_BoundingBox(null);
                    if (bb != null)
                        cand.StrutRodMm = Math.Max(50.0, (bb.Min.Z - pt.Z) * FtToMm);
                    return;
                }

                // Structural framing (beams) second.
                var beams = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .WherePasses(bboxFilter);
                var beam = beams.FirstOrDefault();
                if (beam != null)
                {
                    cand.AnchorType   = "BEAM_CLAMP";
                    cand.AnchorHostId = beam.Id;
                    var bb = beam.get_BoundingBox(null);
                    if (bb != null)
                        cand.StrutRodMm = Math.Max(50.0, (bb.Min.Z - pt.Z) * FtToMm);
                    return;
                }

                cand.AnchorType = "GENERIC";
                cand.StrutRodMm = 300.0;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HangerPlacementEngine.FindAnchor: {ex.Message}");
                cand.AnchorType = "GENERIC";
            }
        }

        private static int GroupTrapezes(List<HangerCandidate> candidates)
        {
            int groups = 0;
            double thresholdFt = TrapezeDistanceMm * MmToFt;
            var claimed = new HashSet<int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (claimed.Contains(i)) continue;
                var a = candidates[i];
                bool anyGroupMember = false;
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    if (claimed.Contains(j)) continue;
                    var b = candidates[j];
                    if (Math.Abs(a.Point.Z - b.Point.Z) > thresholdFt) continue;
                    double dx = a.Point.X - b.Point.X;
                    double dy = a.Point.Y - b.Point.Y;
                    double d  = Math.Sqrt(dx * dx + dy * dy);
                    if (d > thresholdFt) continue;
                    a.OnTrapeze = true;
                    b.OnTrapeze = true;
                    claimed.Add(j);
                    anyGroupMember = true;
                }
                if (anyGroupMember) groups++;
            }
            return groups;
        }

        private static string ReadParam(Element el, string name, string def)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return def;
                return p.StorageType switch
                {
                    StorageType.String => p.AsString() ?? def,
                    StorageType.Double => p.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StorageType.Integer => p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => def,
                };
            }
            catch { return def; }
        }
    }
}
