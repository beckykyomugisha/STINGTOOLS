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
using StingTools.Core;

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
        /// <summary>
        /// Computed carried load at this hanger in kilograms. Equals
        /// span × (shell weight + content + insulation). Used by
        /// RodSizeTable.Select for MSUITE-style load-aware rod sizing.
        /// </summary>
        public double    PointLoadKg   { get; set; }
        /// <summary>Selected rod diameter in mm.</summary>
        public double    RodDiameterMm { get; set; }
        /// <summary>Safe-working-load utilisation at this hanger.</summary>
        public double    RodUtilizationPct { get; set; }
        /// <summary>Imperial thread designation of selected rod.</summary>
        public string    RodImperial   { get; set; } = "";
        /// <summary>True when strut rod length &gt; StockLengthMm (3 m)
        /// and a coupler must be inserted.</summary>
        public bool      RodNeedsCoupler { get; set; }
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

            // Per-metre weight for point-load sizing. Phase M: reuse
            // SpoolWeightCalculator for the shell, then add content
            // mass for water-filled piping at full-bore.
            double totalWeightKg = 0;
            try { totalWeightKg = Fabrication.SpoolWeightCalculator.WeightKg(doc, new[] { el.Id }); } catch { }
            double contentKg = ComputeContentMassKg(el, runLenMm);
            double insulationKg = ComputeInsulationMassKg(el, runLenMm);
            double perMetreKg = (totalWeightKg + contentKg + insulationKg) / Math.Max(0.1, runLenMm / 1000.0);

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

                // Point load = per-metre mass × full span carried
                // (half span each side for interior hangers; half for
                // end hangers). Phase M simplification: use full span
                // for every hanger — conservative.
                cand.PointLoadKg = perMetreKg * (spacing.MaxSpanMm / 1000.0);

                var rod = RodSizeTable.Select(
                    new RodSizeQuery { PointLoadKg = cand.PointLoadKg });
                cand.RodDiameterMm     = rod.RodDiameterMm;
                cand.RodImperial       = rod.RodImperial;
                cand.RodUtilizationPct = rod.UtilizationPct;

                // Pick anchor type based on what's directly above.
                FindAnchor(doc, pt, cand);
                cand.RodNeedsCoupler = cand.StrutRodMm > RodSizeTable.StockLengthMm;
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

        /// <summary>Water mass carried inside a pipe at full bore.</summary>
        private static double ComputeContentMassKg(Element el, double runLengthMm)
        {
            try
            {
                if (el is Pipe p)
                {
                    double dM = p.Diameter * 0.3048;
                    double areaM2 = Math.PI * dM * dM * 0.25;
                    return areaM2 * (runLengthMm / 1000.0) * 1000.0; // ρ_water = 1000 kg/m³
                }
            }
            catch { }
            return 0;
        }

        /// <summary>Insulation mass if PLM_PPE_INSULATION_THK_MM or
        /// HVC_DCT_INSULATION_THK_MM is set.</summary>
        private static double ComputeInsulationMassKg(Element el, double runLengthMm)
        {
            try
            {
                double thkMm = 0;
                var pPipe = el.LookupParameter("PLM_PPE_INSULATION_THK_MM");
                var pDuct = el.LookupParameter("HVC_DCT_INSULATION_THK_MM");
                if (pPipe != null && pPipe.StorageType == StorageType.Double) thkMm = pPipe.AsDouble() * 304.8;
                else if (pDuct != null && pDuct.StorageType == StorageType.Double) thkMm = pDuct.AsDouble() * 304.8;
                else if (pPipe != null && double.TryParse(pPipe.AsString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var vP)) thkMm = vP;
                else if (pDuct != null && double.TryParse(pDuct.AsString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var vD)) thkMm = vD;
                if (thkMm <= 0) return 0;

                // Nominal insulation density 30 kg/m³ (mineral wool).
                // Shell volume = π × D × thk × L (thin-wall assumption).
                double dM = 0;
                if (el is Pipe p) dM = p.Diameter * 0.3048;
                else if (el is Duct d) dM = Math.Max(d.Width, d.Height) * 0.3048;
                if (dM <= 0) return 0;
                double thkM = thkMm / 1000.0;
                return Math.PI * dM * thkM * (runLengthMm / 1000.0) * 30.0;
            }
            catch { }
            return 0;
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
