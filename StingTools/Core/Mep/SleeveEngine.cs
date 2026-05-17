// StingTools v4 MVP — Phase I sleeve engine.
//
// Detects MEP-vs-structure penetrations, sizes a sleeve per
// SleeveSizingRules (insulation-aware, clearance-driven), places a
// hosted void family at the intersection midpoint, wires it as an
// InstanceVoidCut against the host, and inherits the host type's
// fire rating. All writes in a single Transaction; failures on
// individual penetrations are caught so the batch never aborts.
//
// Host detection
//   For each selected MEP curve, intersect its LocationCurve with
//   wall / floor / roof / ceiling bounding boxes pulled via a
//   BoundingBoxIntersectsFilter. Finer verification via
//   ElementIntersectsElementFilter before placement.
//
// Sleeve geometry
//   SleeveSizingRule.Shape = "round"       → circular sleeve (diameter)
//                          = "rectangular" → rectangular sleeve (w×h)
//   Depth = host thickness + 2 × protrusion (default 10 mm each side).
//   Size = element OD/width + 2 × insulation + 2 × clearance,
//          clamped to rule.MinBoreMm upper-bound.
//
// Void cut
//   InstanceVoidCutUtils.AddInstanceVoidCut(doc, host, sleeveInstance).
//   Requires the sleeve family's "Cut with Voids When Loaded" to be
//   true — the engine sets it after placement when possible.
//
// Fire-rating inheritance
//   Reads BuiltInParameter.FIRE_RATING from host TYPE (walls/floors
//   carry it as a type parameter), writes it to
//   STING_SLEEVE_HOST_FIRE_RATING on the sleeve instance.
//
// Pset_ProvisionForVoid
//   Writes STING_SLEEVE_PFV_UUID to a deterministic GUIDv5 so that
//   IFC export under IFCExportOptions.AddOption(
//     "ExportProvisionForVoids","true") round-trips with Tekla's Hole
//   Reservation Manager using a stable key.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Mep
{
    public class SleeveResult
    {
        public int MepCurvesScanned  { get; set; }
        public int PenetrationsFound { get; set; }
        public int Placed            { get; set; }
        public int CutApplied        { get; set; }
        public int FireRatingWritten { get; set; }
        public int Skipped           { get; set; }
        public int Failed            { get; set; }
        public List<ElementId> PlacedIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SleeveEngine
    {
        private const double FtToMm = 304.8;
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>Sleeve protrudes this far from each face of the host.</summary>
        public const double ProtrusionMm = 10.0;

        /// <summary>Family-symbol name patterns tried in order.</summary>
        private static readonly string[] PreferredFamilyNames = new[]
        {
            "STING_SLEEVE_ROUND",
            "STING_SLEEVE_RECT",
            "STING_PROVISION_VOID",
        };

        public static SleeveResult PlaceSleeves(
            Document doc,
            IEnumerable<Element> mepCurves,
            bool dryRun = false)
        {
            var result = new SleeveResult();
            if (doc == null || mepCurves == null) return result;

            // Pre-load host collector inside a padded project AABB so we
            // only walk walls/floors/roofs that actually overlap the
            // selection.
            var hostCats = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Ceilings,
            };

            // Resolve sleeve families up-front.
            var roundSym = ResolveSleeveSymbol(doc, preferRectangular: false);
            var rectSym  = ResolveSleeveSymbol(doc, preferRectangular: true);
            if (roundSym == null && rectSym == null && !dryRun)
            {
                result.Warnings.Add(
                    "No sleeve family loaded (STING_SLEEVE_ROUND / STING_SLEEVE_RECT / " +
                    "STING_PROVISION_VOID). Falling back to dry run — engine reports the " +
                    "placements it would have made.");
                dryRun = true;
            }

            var runs = mepCurves.Where(el => el != null).ToList();
            result.MepCurvesScanned = runs.Count;
            if (runs.Count == 0) return result;

            // Candidate list accumulated dry; only applied under tx.
            var candidates = new List<SleeveCandidate>();
            foreach (var mep in runs)
            {
                try
                {
                    var rule = SleeveSizingRules.Resolve(mep);
                    if (rule == null)
                    {
                        result.Warnings.Add($"{mep.Id}: no sleeve rule for category {mep.Category?.Name}");
                        result.Skipped++;
                        continue;
                    }
                    var hits = FindPenetrations(doc, mep, hostCats);
                    foreach (var hit in hits)
                    {
                        candidates.Add(BuildCandidate(mep, hit.host, hit.midpoint, rule));
                        result.PenetrationsFound++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Warnings.Add($"{mep.Id}: penetration scan: {ex.Message}");
                }
            }

            if (dryRun || candidates.Count == 0) return result;

            using (var tx = new Transaction(doc, "STING v4 Place sleeves"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"tx start: {ex.Message}"); return result; }

                foreach (var cand in candidates)
                {
                    try
                    {
                        var symbol = cand.Rule.Shape == "rectangular" ? (rectSym ?? roundSym)
                                                                       : (roundSym ?? rectSym);
                        if (symbol == null) { result.Failed++; continue; }
                        if (!symbol.IsActive) symbol.Activate();

                        var fi = doc.Create.NewFamilyInstance(
                            cand.Midpoint, symbol, StructuralType.NonStructural);
                        if (fi == null) { result.Failed++; continue; }

                        ApplyShapeParameters(fi, cand);
                        TrySetString(fi, "STING_SLEEVE_PFV_UUID", MakePfvUuid(cand));
                        TrySetString(fi, "STING_SLEEVE_HOST_FIRE_RATING",
                            ReadHostFireRating(doc, cand.Host));
                        if (!string.IsNullOrEmpty(ReadHostFireRating(doc, cand.Host)))
                            result.FireRatingWritten++;

                        // Depth param: host wall/floor thickness + 2× protrusion.
                        TrySetDouble(fi, "STING_SLEEVE_DEPTH_MM",
                            cand.HostThicknessMm + 2 * ProtrusionMm);

                        // InstanceVoidCutUtils — can throw when the family
                        // is not flagged "Cut with Voids When Loaded".
                        try
                        {
                            if (InstanceVoidCutUtils.CanBeCutWithVoid(cand.Host))
                            {
                                InstanceVoidCutUtils.AddInstanceVoidCut(doc, cand.Host, fi);
                                result.CutApplied++;
                            }
                            else
                            {
                                result.Warnings.Add(
                                    $"Host {cand.Host.Id} CanBeCutWithVoid=false; sleeve placed without cut");
                            }
                        }
                        catch (Exception ex2)
                        { result.Warnings.Add($"InstanceVoidCut {cand.Host.Id}: {ex.Message}"); }

                        result.PlacedIds.Add(fi.Id);
                        result.Placed++;
                    }
                    catch (Exception ex2)
                    {
                        result.Failed++;
                        result.Warnings.Add($"sleeve place: {ex.Message}");
                    }
                }

                try { tx.Commit(); }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"tx commit: {ex.Message}");
                }
            }

            return result;
        }

        // ---- candidate record + builders ---------------------------------------

        private class SleeveCandidate
        {
            public Element MepElement { get; set; }
            public Element Host       { get; set; }
            public XYZ     Midpoint   { get; set; }
            public SleeveSizingRule Rule { get; set; }
            public double BoreMm      { get; set; }
            public double WidthMm     { get; set; }
            public double HeightMm    { get; set; }
            public double HostThicknessMm { get; set; }
        }

        private static SleeveCandidate BuildCandidate(Element mep, Element host, XYZ mid, SleeveSizingRule rule)
        {
            double elemDiaMm = ProbeDiameterMm(mep);
            double elemWMm   = ProbeWidthMm(mep);
            double elemHMm   = ProbeHeightMm(mep);
            double insulMm   = rule.IncludeInsulation ? ProbeInsulationMm(mep) : 0;
            double clearance = rule.ClearanceMm;

            double bore = 0, w = 0, h = 0;
            if (rule.Shape == "rectangular")
            {
                w = Math.Max(rule.MinBoreMm, elemWMm + 2 * insulMm + 2 * clearance);
                h = Math.Max(rule.MinBoreMm, elemHMm + 2 * insulMm + 2 * clearance);
            }
            else
            {
                bore = Math.Max(rule.MinBoreMm, elemDiaMm + 2 * insulMm + 2 * clearance);
            }

            return new SleeveCandidate
            {
                MepElement      = mep,
                Host            = host,
                Midpoint        = mid,
                Rule            = rule,
                BoreMm          = bore,
                WidthMm         = w,
                HeightMm        = h,
                HostThicknessMm = ProbeHostThicknessMm(host),
            };
        }

        private static void ApplyShapeParameters(FamilyInstance fi, SleeveCandidate cand)
        {
            if (cand.Rule.Shape == "rectangular")
            {
                TrySetDouble(fi, "STING_SLEEVE_WIDTH_MM",  cand.WidthMm);
                TrySetDouble(fi, "STING_SLEEVE_HEIGHT_MM", cand.HeightMm);
            }
            else
            {
                TrySetDouble(fi, "STING_SLEEVE_BORE_MM", cand.BoreMm);
            }
            TrySetString(fi, "STING_SLEEVE_RULE_ID", cand.Rule.Id);
        }

        // ---- penetration scan --------------------------------------------------

        private static List<(Element host, XYZ midpoint)> FindPenetrations(
            Document doc, Element mep, IEnumerable<BuiltInCategory> hostCats)
        {
            var hits = new List<(Element, XYZ)>();
            var lc = mep.Location as LocationCurve;
            var curve = lc?.Curve;
            if (curve == null) return hits;

            // Build a padded AABB around the MEP run to pre-filter hosts.
            var bb = mep.get_BoundingBox(null);
            if (bb == null) return hits;
            const double padFt = 1.0;
            var outline = new Outline(
                new XYZ(bb.Min.X - padFt, bb.Min.Y - padFt, bb.Min.Z - padFt),
                new XYZ(bb.Max.X + padFt, bb.Max.Y + padFt, bb.Max.Z + padFt));
            var bboxFilter = new BoundingBoxIntersectsFilter(outline);
            var catsList = new List<BuiltInCategory>(hostCats);

            foreach (var el in new FilteredElementCollector(doc)
                        .WherePasses(new ElementMulticategoryFilter(catsList))
                        .WhereElementIsNotElementType()
                        .WherePasses(bboxFilter))
            {
                // Fine check: ElementIntersectsElementFilter.
                try
                {
                    if (!(new ElementIntersectsElementFilter(el)).PassesFilter(doc, mep.Id))
                        continue;
                    // Midpoint: project the host's bounding-box centre onto the curve.
                    var hb = el.get_BoundingBox(null);
                    if (hb == null) continue;
                    var centre = 0.5 * (hb.Min + hb.Max);
                    var proj = curve.Project(centre);
                    if (proj == null) continue;
                    hits.Add((el, proj.XYZPoint));
                }
                catch (Exception ex)
                { StingLog.Warn($"SleeveEngine: penetration check {mep.Id}↔{el.Id}: {ex.Message}"); }
            }
            return hits;
        }

        // ---- host parameter reads ----------------------------------------------

        private static FamilySymbol ResolveSleeveSymbol(Document doc, bool preferRectangular)
        {
            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_GenericModel,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_PipeAccessory,
                };
                var allSymbols = new List<FamilySymbol>();
                foreach (var cat in cats)
                {
                    var col = new FilteredElementCollector(doc)
                        .OfCategory(cat).OfClass(typeof(FamilySymbol));
                    foreach (FamilySymbol fs in col) allSymbols.Add(fs);
                }
                foreach (var preferred in preferRectangular
                            ? new[] { "STING_SLEEVE_RECT", "STING_PROVISION_VOID", "STING_SLEEVE_ROUND" }
                            : new[] { "STING_SLEEVE_ROUND", "STING_PROVISION_VOID", "STING_SLEEVE_RECT" })
                {
                    var hit = allSymbols.FirstOrDefault(s =>
                        string.Equals(s.FamilyName, preferred, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) return hit;
                }
                // Keyword fallback.
                var kw = preferRectangular ? "RECT" : "ROUND";
                var kwHit = allSymbols.FirstOrDefault(s =>
                    (s.FamilyName ?? "").ToUpperInvariant().Contains("SLEEVE") &&
                    (s.FamilyName ?? "").ToUpperInvariant().Contains(kw));
                if (kwHit != null) return kwHit;
                return allSymbols.FirstOrDefault(s =>
                    (s.FamilyName ?? "").ToUpperInvariant().Contains("SLEEVE") ||
                    (s.FamilyName ?? "").ToUpperInvariant().Contains("PROVISION"));
            }
            catch (Exception ex)
            { StingLog.Warn($"SleeveEngine.ResolveSleeveSymbol: {ex.Message}"); return null; }
        }

        private static string ReadHostFireRating(Document doc, Element host)
        {
            try
            {
                // FIRE_RATING lives on the wall/floor TYPE, not the
                // instance. Look up the type and read.
                var typeId = host.GetTypeId();
                if (typeId == ElementId.InvalidElementId) return "";
                var typeEl = doc.GetElement(typeId);
                if (typeEl == null) return "";
                var p = typeEl.get_Parameter(BuiltInParameter.FIRE_RATING);
                return p?.AsString() ?? "";
            }
            catch { return ""; }
        }

        private static double ProbeDiameterMm(Element el)
        {
            try
            {
                if (el is Autodesk.Revit.DB.Plumbing.Pipe p) return p.Diameter * FtToMm;
                if (el is Autodesk.Revit.DB.Electrical.Conduit c) return c.Diameter * FtToMm;
                if (el is Autodesk.Revit.DB.Mechanical.Duct d)
                {
                    try { if (d.Diameter > 0) return d.Diameter * FtToMm; } catch { }
                }
            }
            catch { }
            return 0;
        }
        private static double ProbeWidthMm(Element el)
        {
            try
            {
                if (el is Autodesk.Revit.DB.Mechanical.Duct d && d.Width > 0) return d.Width * FtToMm;
                if (el is Autodesk.Revit.DB.Electrical.CableTray ct) return ct.Width * FtToMm;
            }
            catch { }
            return 0;
        }
        private static double ProbeHeightMm(Element el)
        {
            try
            {
                if (el is Autodesk.Revit.DB.Mechanical.Duct d && d.Height > 0) return d.Height * FtToMm;
                if (el is Autodesk.Revit.DB.Electrical.CableTray ct) return ct.Height * FtToMm;
            }
            catch { }
            return 0;
        }
        private static double ProbeInsulationMm(Element el)
        {
            try
            {
                var pPipe = el.LookupParameter("PLM_PPE_INSULATION_THK_MM");
                var pDuct = el.LookupParameter("HVC_DCT_INSULATION_THK_MM");
                if (pPipe?.StorageType == StorageType.Double) return pPipe.AsDouble() * FtToMm;
                if (pDuct?.StorageType == StorageType.Double) return pDuct.AsDouble() * FtToMm;
                if (pPipe != null && double.TryParse(pPipe.AsString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v1)) return v1;
                if (pDuct != null && double.TryParse(pDuct.AsString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v2)) return v2;
            }
            catch { }
            return 0;
        }
        private static double ProbeHostThicknessMm(Element host)
        {
            try
            {
                if (host is Wall w) return w.Width * FtToMm;
                if (host is Floor f)
                {
                    try { return f.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() * FtToMm ?? 0; }
                    catch { return 0; }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Generate a deterministic UUIDv5 key per (hostId, mepId) so the
        /// IFC Pset_ProvisionForVoid GlobalId is stable across re-runs,
        /// enabling Tekla Hole Reservation Manager round-trip.
        /// </summary>
        private static string MakePfvUuid(SleeveCandidate cand)
        {
            try
            {
                string seed = $"{cand.Host?.UniqueId}|{cand.MepElement?.UniqueId}";
                return DeterministicGuid(seed);
            }
            catch { return Guid.NewGuid().ToString(); }
        }

        private static string DeterministicGuid(string seed)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("STING_PFV|" + seed));
                var g = new byte[16];
                Array.Copy(bytes, g, 16);
                g[6] = (byte)((g[6] & 0x0F) | 0x50); // set version 5
                g[8] = (byte)((g[8] & 0x3F) | 0x80);
                return new Guid(g).ToString();
            }
        }

        // ---- writers -----------------------------------------------------------

        private static void TrySetString(Element el, string param, string val)
        {
            try { var p = el.LookupParameter(param);
                  if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(val ?? ""); }
            catch { }
        }
        private static void TrySetDouble(Element el, string param, double val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(val);
                else if (p.StorageType == StorageType.String)
                    p.Set(val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { }
        }
    }
}
