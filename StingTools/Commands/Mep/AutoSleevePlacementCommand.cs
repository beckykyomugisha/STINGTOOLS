// StingTools Phase 109 — Auto-sleeve placement at MEP/wall crossings.
//
// Scans every pipe / duct / conduit / cable-tray in the model, tests
// intersection against every wall, floor, and roof, and inserts a
// sleeve family instance at the intersection midpoint.
//
// Sleeve family resolution (first match wins):
//   1. STING_SLV_<discipline>_<diameter>.rfa (e.g. STING_SLV_PIPE_150.rfa)
//   2. STING_SLV_GENERIC.rfa
//   3. First FamilySymbol whose name contains "SLEEVE"
//
// Each placed sleeve is tagged with the unified STING_SLEEVE_* schema
// (see Core/Mep/SleeveParamRegistry.cs) so the same downstream BCF and
// IFC PFV exports work whether sleeves were placed by this command or
// the selection-scoped PlaceSleevesCommand.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    public class SleevePlacementResult
    {
        public int Scanned       { get; set; }
        public int Intersections { get; set; }
        public int Created       { get; set; }
        public int Skipped       { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, int> ByDiscipline { get; } = new Dictionary<string, int>();
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoSleevePlacementCommand : IExternalCommand
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double AnnulusMm = 50.0;  // BS EN 1366-3 minimum annulus per side

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            FamilySymbol sleeveSymbol = ResolveSleeveSymbol(doc);
            if (sleeveSymbol == null)
            {
                TaskDialog.Show("STING — Auto-sleeve",
                    "No sleeve family found. Load STING_SLV_GENERIC.rfa or a family whose name contains \"SLEEVE\".");
                return Result.Cancelled;
            }

            var result = new SleevePlacementResult();
            try
            {
                var mepCats = new[]
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_CableTray
                };
                var hostCats = new[]
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Roofs
                };
                var mepEls = new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(mepCats))
                    .WhereElementIsNotElementType()
                    .ToList();
                result.Scanned = mepEls.Count;

                using (var tx = new Transaction(doc, "STING Auto-sleeve"))
                {
                    try { tx.Start(); }
                    catch (Exception ex) { result.Warnings.Add($"tx start: {ex.Message}"); goto Report; }

                    try
                    {
                        if (!sleeveSymbol.IsActive) { sleeveSymbol.Activate(); doc.Regenerate(); }

                        foreach (var mep in mepEls)
                        {
                            try
                            {
                                var hosts = FindHostIntersections(doc, mep, hostCats);
                                foreach (var (host, point) in hosts)
                                {
                                    result.Intersections++;
                                    try
                                    {
                                        var fi = doc.Create.NewFamilyInstance(
                                            point, sleeveSymbol, host,
                                            StructuralType.NonStructural);
                                        if (fi == null) { result.Skipped++; continue; }
                                        TagSleeve(fi, host, mep);
                                        result.Created++;
                                        string disc = DisciplineFor(mep);
                                        result.ByDiscipline[disc] =
                                            result.ByDiscipline.TryGetValue(disc, out var n) ? n + 1 : 1;
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Skipped++;
                                        result.Warnings.Add($"NewFamilyInstance {mep.Id}: {ex.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"intersect {mep.Id}: {ex.Message}");
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                        result.Warnings.Add($"AutoSleeve fatal: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("AutoSleevePlacementCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

        Report:
            ShowResult(result);
            return Result.Succeeded;
        }

        private List<(Element Host, XYZ Point)> FindHostIntersections(
            Document doc, Element mep, BuiltInCategory[] hostCats)
        {
            var hits = new List<(Element, XYZ)>();
            var curve = (mep.Location as LocationCurve)?.Curve;
            if (curve == null) return hits;

            // Bounding-box filter for host candidates, then fine solid intersect.
            var bb = mep.get_BoundingBox(null);
            if (bb == null) return hits;

            var outline = new Outline(bb.Min, bb.Max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);
            var candidates = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(hostCats))
                .WhereElementIsNotElementType()
                .WherePasses(bbFilter)
                .ToList();

            foreach (var host in candidates)
            {
                try
                {
                    var solids = GetSolids(host);
                    foreach (var solid in solids)
                    {
                        if (solid == null || solid.Volume <= 0) continue;
                        SolidCurveIntersection sci = solid.IntersectWithCurve(
                            curve, new SolidCurveIntersectionOptions());
                        if (sci == null) continue;
                        for (int i = 0; i < sci.SegmentCount; i++)
                        {
                            var seg = sci.GetCurveSegment(i);
                            if (seg == null) continue;
                            XYZ mid = seg.Evaluate(0.5, true);
                            hits.Add((host, mid));
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"AutoSleeve intersect {mep.Id} vs {host.Id}: {ex.Message}");
                }
            }
            return hits;
        }

        private static IEnumerable<Solid> GetSolids(Element el)
        {
            GeometryElement g = null;
            try { g = el.get_Geometry(new Options { ComputeReferences = false, IncludeNonVisibleObjects = false }); }
            catch { yield break; }
            if (g == null) yield break;
            foreach (GeometryObject obj in g)
            {
                if (obj is Solid s && s.Volume > 0) yield return s;
                else if (obj is GeometryInstance gi)
                    foreach (GeometryObject inner in gi.GetInstanceGeometry())
                        if (inner is Solid sx && sx.Volume > 0) yield return sx;
            }
        }

        private FamilySymbol ResolveSleeveSymbol(Document doc)
        {
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                {
                    if (!(el is FamilySymbol fs)) continue;
                    string fn = fs.FamilyName?.ToUpperInvariant() ?? "";
                    string nm = fs.Name?.ToUpperInvariant() ?? "";
                    if (fn == "STING_SLV_GENERIC") return fs;
                    if (fn.StartsWith("STING_SLV_") || fn.Contains("SLEEVE") || nm.Contains("SLEEVE"))
                        return fs;
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoSleeve sleeve resolve: {ex.Message}"); }
            return null;
        }

        private void TagSleeve(FamilyInstance fi, Element host, Element mep)
        {
            // Unified STING_SLEEVE_* schema — keep this in sync with
            // SleeveEngine.PlaceSleeves so BCF + IFC PFV exports see the
            // same parameter set regardless of which entry point placed
            // the sleeve.
            TrySetInt   (fi, SleeveParamRegistry.HostElementId, (int)(host.Id?.Value ?? 0));
            TrySetInt   (fi, SleeveParamRegistry.PenetratedId,  (int)(mep.Id?.Value ?? 0));

            double sizeMm = NominalSizeMm(mep) + 2.0 * AnnulusMm;
            string shape  = SleeveShape(mep);
            if (shape == "rectangular")
            {
                TrySetDouble(fi, SleeveParamRegistry.WidthMm,  sizeMm);
                TrySetDouble(fi, SleeveParamRegistry.HeightMm, sizeMm);
            }
            else
            {
                TrySetDouble(fi, SleeveParamRegistry.BoreMm, sizeMm);
            }

            // FIRE_RATING is a TYPE parameter on walls/floors. Read it
            // from the type rather than the instance, matching SleeveEngine.
            string fireRat = "";
            try
            {
                var typeId = host?.GetTypeId() ?? ElementId.InvalidElementId;
                if (typeId != ElementId.InvalidElementId)
                {
                    var typeEl = host.Document.GetElement(typeId);
                    fireRat = typeEl?.get_Parameter(BuiltInParameter.FIRE_RATING)?.AsString() ?? "";
                }
            }
            catch { }
            TrySetString(fi, SleeveParamRegistry.HostFireRating, fireRat);

            // Stable PFV UUID so re-runs and downstream exports stay linked.
            TrySetString(fi, SleeveParamRegistry.PfvUuid, MakePfvUuid(host, mep));
            TrySetString(fi, SleeveParamRegistry.RuleId, $"AutoSleeve|{DisciplineFor(mep)}");
            TrySetString(fi, SleeveParamRegistry.CreatedBy, "STING v4 AutoSleeve");
        }

        private static string SleeveShape(Element mep)
        {
            try
            {
                if (mep is Autodesk.Revit.DB.Mechanical.Duct d)
                    return (d.Width > 0 && d.Height > 0) ? "rectangular" : "round";
                if (mep is Autodesk.Revit.DB.Electrical.CableTray) return "rectangular";
            }
            catch { }
            return "round";
        }

        /// <summary>
        /// Same deterministic UUIDv5 used by SleeveEngine — keeps the BCF /
        /// IFC PFV round-trip key stable when both commands place sleeves
        /// against the same (host, mep) pair.
        /// </summary>
        private static string MakePfvUuid(Element host, Element mep)
        {
            try
            {
                string seed = $"{host?.UniqueId}|{mep?.UniqueId}";
                using var sha = System.Security.Cryptography.SHA1.Create();
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("STING_PFV|" + seed));
                var g = new byte[16];
                Array.Copy(bytes, g, 16);
                g[6] = (byte)((g[6] & 0x0F) | 0x50);
                g[8] = (byte)((g[8] & 0x3F) | 0x80);
                return new Guid(g).ToString();
            }
            catch { return Guid.NewGuid().ToString(); }
        }

        private static double NominalSizeMm(Element mep)
        {
            try
            {
                if (mep is MEPCurve m)
                {
                    var dia = m.LookupParameter("Diameter");
                    if (dia != null && dia.StorageType == StorageType.Double) return dia.AsDouble() * 304.8;
                    var w = m.LookupParameter("Width")?.AsDouble() ?? 0;
                    var h = m.LookupParameter("Height")?.AsDouble() ?? 0;
                    return Math.Max(w, h) * 304.8;
                }
            }
            catch { }
            return 100.0;
        }

        private static string DisciplineFor(Element mep)
        {
            if (mep?.Category == null) return "Other";
            var bic = (BuiltInCategory)mep.Category.Id.Value;
            switch (bic)
            {
                case BuiltInCategory.OST_PipeCurves: return "Pipe";
                case BuiltInCategory.OST_DuctCurves: return "Duct";
                case BuiltInCategory.OST_Conduit:    return "Conduit";
                case BuiltInCategory.OST_CableTray:  return "CableTray";
            }
            return "Other";
        }

        private void ShowResult(SleevePlacementResult r)
        {
            var panel = StingResultPanel.Create("MEP Auto-sleeve");
            panel.SetSubtitle("Sleeve placement at wall / floor / roof crossings");
            panel.AddSection("SUMMARY")
                 .Metric("MEP elements scanned",    r.Scanned.ToString())
                 .Metric("Intersections detected",  r.Intersections.ToString())
                 .Metric("Sleeves created",         r.Created.ToString())
                 .Metric("Skipped",                 r.Skipped.ToString());
            panel.AddSection("STANDARDS")
                 .Text("Sized per BS EN 1366-3 minimum annulus (50 mm clearance per side).")
                 .Text("Tagged with the unified STING_SLEEVE_* schema — same set as")
                 .Text("Place Sleeves; downstream BCF and IFC PFV exports see both.");
            if (r.ByDiscipline.Count > 0)
            {
                panel.AddSection("BY DISCIPLINE");
                foreach (var kv in r.ByDiscipline.OrderByDescending(k => k.Value))
                    panel.Metric(kv.Key, kv.Value.ToString());
            }
            if (r.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(30)) panel.Text(w);
                if (r.Warnings.Count > 30) panel.Text($"(+{r.Warnings.Count - 30} more — see StingLog)");
            }
            panel.Show();
        }

        private static void TrySetString(Element el, string param, string val)
        {
            try { var p = el.LookupParameter(param);
                  if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(val ?? ""); }
            catch { }
        }
        private static void TrySetInt(Element el, string param, int val)
        {
            try { var p = el.LookupParameter(param);
                  if (p == null || p.IsReadOnly) return;
                  if (p.StorageType == StorageType.Integer) p.Set(val);
                  else if (p.StorageType == StorageType.String) p.Set(val.ToString()); }
            catch { }
        }
        private static void TrySetDouble(Element el, string param, double valMm)
        {
            try { var p = el.LookupParameter(param);
                  if (p == null || p.IsReadOnly) return;
                  if (p.StorageType == StorageType.Double)  p.Set(valMm * MmToFt);
                  else if (p.StorageType == StorageType.String) p.Set(valMm.ToString("F0"));
                  else if (p.StorageType == StorageType.Integer) p.Set((int)Math.Round(valMm)); }
            catch { }
        }
    }
}
