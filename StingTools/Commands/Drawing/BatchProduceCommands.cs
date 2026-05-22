// StingTools — Drawing Template Manager · Phase 137
//
// BatchProduceCommands hosts the ten Phase 137 batch-production
// IExternalCommand classes. Each command launches the
// DrawingProductionConfigDialog, awaits user confirmation, then loops
// (selectedContexts × selectedDrawingTypes) through DrawingProducer
// inside a TransactionGroup, surfacing results in a TaskDialog.
//
// Tags (resolved by StingCommandHandler):
//   DrawingTypes_ProducePerLevel
//   DrawingTypes_ProduceFromScopeBoxes
//   DrawingTypes_ProduceInteriorElevations
//   DrawingTypes_ProduceExteriorElevations
//   DrawingTypes_ProduceSections
//   DrawingTypes_RegenerateTemplates
//   DrawingTypes_ConvertToManaged
//   DrawingTypes_DetachManaged
//   DrawingTypes_ExportPackage
//   DrawingTypes_SequencePackage
//   DrawingTypes_AuditPackages

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.UI;
using StingListPicker = StingTools.Select.StingListPicker;

namespace StingTools.Commands.Drawing
{
    internal static class BatchProduceCommons
    {
        internal static ProduceOptions BuildOptions(DrawingProductionPreset preset)
        {
            var o = new ProduceOptions();
            if (preset == null) return o;
            o.CreateSheet = preset.CreateSheets;
            o.PlaceOnSheet = preset.CreateSheets;
            o.RunAnnotation = preset.General?.RunAnnotation ?? true;
            o.Idempotent = preset.General?.Idempotent ?? true;
            switch (preset.General?.DuplicateOption)
            {
                case "DuplicateWithDetailing": o.DuplicateOption = ViewDuplicateOption.WithDetailing; break;
                case "DuplicateAsDependent":   o.DuplicateOption = ViewDuplicateOption.AsDependent; break;
                default:                       o.DuplicateOption = ViewDuplicateOption.Duplicate; break;
            }
            o.Preset = preset;
            return o;
        }

        internal static List<DrawingType> AllTypesByPurpose(Document doc, params string[] purposes)
        {
            var lib = DrawingTypeRegistry.GetLibrary(doc);
            var set = new HashSet<string>(purposes ?? new string[0], StringComparer.OrdinalIgnoreCase);
            return (lib?.DrawingTypes ?? new List<DrawingType>())
                .Where(t => set.Contains(t.Purpose ?? ""))
                .ToList();
        }

        internal static List<DrawingType> ResolveSelectedTypes(Document doc, IList<string> ids)
        {
            var lib = DrawingTypeRegistry.GetLibrary(doc);
            return (lib?.DrawingTypes ?? new List<DrawingType>())
                .Where(t => ids != null && ids.Contains(t.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        internal static void ShowResult(string title, int views, int sheets, IList<string> warnings)
        {
            var msg = new System.Text.StringBuilder();
            msg.AppendLine($"Views created: {views}");
            msg.AppendLine($"Sheets created: {sheets}");
            if (warnings != null && warnings.Count > 0)
            {
                msg.AppendLine();
                msg.AppendLine($"Warnings ({warnings.Count}):");
                foreach (var w in warnings.Take(20)) msg.AppendLine("  • " + w);
                if (warnings.Count > 20) msg.AppendLine($"  …and {warnings.Count - 20} more");
            }
            TaskDialog.Show(title, msg.ToString());
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProduceViewsPerLevelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: warm the per-document caches so every per-level
                // / per-DrawingType Apply call hits the (template name →
                // ElementId) and (pack id → pack) memos.
                DrawingTypePresentation.Prewarm(doc);
                DrawingProducer.PrimeBatchCaches(doc); // GAP-L

                var types = BatchProduceCommons.AllTypesByPurpose(doc, "Plan", "RCP");
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                var contextLabels = levels.Select(l => l.Name).ToList();

                var dlg = new DrawingProductionConfigDialog(types, contextLabels, "PerLevel", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;

                var opts = BatchProduceCommons.BuildOptions(res.Preset);
                var pickedTypes = BatchProduceCommons.ResolveSelectedTypes(doc, res.SelectedDrawingTypeIds);
                int views = 0, sheets = 0; var warnings = new List<string>();

                using (var tg = new TransactionGroup(doc, "STING Produce Per Level"))
                {
                    tg.Start();
                    foreach (var levelName in res.SelectedContexts)
                    {
                        var level = levels.FirstOrDefault(l => l.Name == levelName);
                        if (level == null) continue;
                        using (var t = new Transaction(doc, $"STING Produce Per Level - {level.Name}"))
                        {
                            t.Start();
                            try
                            {
                                foreach (var dt in pickedTypes)
                                {
                                    var dctx = new DrawingContext { Level = level, PackageId = res.Preset?.PackageId };
                                    var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                                    views += pr.ViewIds.Count;
                                    if (pr.SheetId != ElementId.InvalidElementId) sheets++;
                                    warnings.AddRange(pr.Warnings);
                                }
                                t.Commit();
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"ProduceViewsPerLevel level={level.Name}: {innerEx.Message}");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce Per Level", views, sheets, warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceViewsPerLevel", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProduceViewsFromScopeBoxesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches.
                DrawingTypePresentation.Prewarm(doc);
                DrawingProducer.PrimeBatchCaches(doc); // GAP-L

                var scopes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .Where(e => (e.Name ?? "").StartsWith("STING::", StringComparison.Ordinal))
                    .ToList();
                if (scopes.Count == 0)
                {
                    TaskDialog.Show("STING", "No STING::… scope boxes found in this project.");
                    return Result.Succeeded;
                }
                var dtIds = scopes.Select(s => (s.Name ?? "").Split(new[] { "::" }, StringSplitOptions.None)[1])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var lib = DrawingTypeRegistry.GetLibrary(doc);
                var types = (lib?.DrawingTypes ?? new List<DrawingType>())
                    .Where(t => dtIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();

                var dlg = new DrawingProductionConfigDialog(types, scopes.Select(s => s.Name).ToList(), "ScopeBoxes", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;

                var opts = BatchProduceCommons.BuildOptions(res.Preset);
                int views = 0, sheets = 0; var warnings = new List<string>();
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

                using (var tg = new TransactionGroup(doc, "STING Produce From Scope Boxes"))
                {
                    tg.Start();
                    foreach (var scopeName in res.SelectedContexts)
                    {
                        var scope = scopes.FirstOrDefault(s => s.Name == scopeName);
                        if (scope == null) continue;
                        var parts = (scope.Name ?? "").Split(new[] { "::" }, StringSplitOptions.None);
                        var dtId = parts.Length > 1 ? parts[1] : null;
                        var levelName = parts.Length > 2 ? parts[2] : null;
                        var tag = parts.Length > 3 ? parts[3] : null;
                        var dt = types.FirstOrDefault(t => string.Equals(t.Id, dtId, StringComparison.OrdinalIgnoreCase));
                        if (dt == null) continue;
                        var lvl = levels.FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));

                        using (var t = new Transaction(doc, $"STING Scope {scope.Name}"))
                        {
                            t.Start();
                            var dctx = new DrawingContext { Level = lvl, ScopeBox = scope, Tag = tag, PackageId = res.Preset?.PackageId };
                            var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                            views += pr.ViewIds.Count;
                            if (pr.SheetId != ElementId.InvalidElementId) sheets++;
                            warnings.AddRange(pr.Warnings);
                            t.Commit();
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce From Scope Boxes", views, sheets, warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceFromScopeBoxes", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProduceInteriorElevationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches before per-room loop.
                DrawingTypePresentation.Prewarm(doc);
                DrawingProducer.PrimeBatchCaches(doc); // GAP-L

                var types = BatchProduceCommons.AllTypesByPurpose(doc, "Elevation");
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Element>()
                    .Where(r => r.LookupParameter("Area")?.AsDouble() > 0)
                    .ToList();
                var labels = rooms.Select(r =>
                {
                    var name = ParameterHelpers.GetString(r, "Name") ?? "";
                    var num  = ParameterHelpers.GetString(r, "Number") ?? "";
                    return $"{name} ({num})";
                }).ToList();

                var dlg = new DrawingProductionConfigDialog(types, labels, "InteriorElevations", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;

                var opts = BatchProduceCommons.BuildOptions(res.Preset);
                int views = 0, sheets = 0; var warnings = new List<string>();
                var pickedTypes = BatchProduceCommons.ResolveSelectedTypes(doc, res.SelectedDrawingTypeIds);

                using (var tg = new TransactionGroup(doc, "STING Interior Elevations"))
                {
                    tg.Start();
                    foreach (var roomLabel in res.SelectedContexts)
                    {
                        var room = rooms.FirstOrDefault(r =>
                        {
                            var n = ParameterHelpers.GetString(r, "Name") ?? "";
                            var num = ParameterHelpers.GetString(r, "Number") ?? "";
                            return $"{n} ({num})" == roomLabel;
                        });
                        if (room == null) continue;
                        using (var t = new Transaction(doc, $"STING Interior Elev {roomLabel}"))
                        {
                            t.Start();
                            try
                            {
                                foreach (var dt in pickedTypes)
                                {
                                    var dctx = new DrawingContext { Room = room, Tag = roomLabel, PackageId = res.Preset?.PackageId };
                                    var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                                    views += pr.ViewIds.Count;
                                    if (pr.SheetId != ElementId.InvalidElementId) sheets++;
                                    warnings.AddRange(pr.Warnings);
                                }
                                t.Commit();
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"ProduceInteriorElevations room={roomLabel}: {innerEx.Message}");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce Interior Elevations", views, sheets, warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceInteriorElevations", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProduceSectionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches.
                DrawingTypePresentation.Prewarm(doc);
                DrawingProducer.PrimeBatchCaches(doc); // GAP-L

                var types = BatchProduceCommons.AllTypesByPurpose(doc, "Section");
                var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();
                var labels = new List<string> { "Manual selection (pick in model)" };
                labels.AddRange(grids.Select(g => "Grid " + g.Name));

                var dlg = new DrawingProductionConfigDialog(types, labels, "Sections", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;

                var preset = res.Preset;
                var sec = preset?.SectionConfig ?? new SectionProductionConfig();
                var opts = BatchProduceCommons.BuildOptions(preset);
                int views = 0, sheets = 0; var warnings = new List<string>();
                var pickedTypes = BatchProduceCommons.ResolveSelectedTypes(doc, res.SelectedDrawingTypeIds);

                if (string.Equals(sec.AutoPlace, "ManualSelection", StringComparison.OrdinalIgnoreCase))
                {
                    TaskDialog.Show("STING", "Manual section selection requires picking section box regions in the model. " +
                        "Use 'Along grid lines' or scope boxes for full automation.");
                    return Result.Succeeded;
                }

                IEnumerable<DrawingContext> contextsToProduce;
                if (string.Equals(sec.AutoPlace, "AlongGridLines", StringComparison.OrdinalIgnoreCase))
                {
                    contextsToProduce = grids.Select(g =>
                    {
                        var bb = new BoundingBoxXYZ();
                        try
                        {
                            var c = g.Curve as Line;
                            if (c == null) return null;
                            var origin = (c.GetEndPoint(0) + c.GetEndPoint(1)) * 0.5;
                            double depthFt = sec.DepthMm / 304.8;
                            var d = c.Direction;
                            // perpendicular = (-d.Y, d.X)
                            var perp = new XYZ(-d.Y, d.X, 0).Normalize();
                            var len = (c.GetEndPoint(1) - c.GetEndPoint(0)).GetLength();
                            var widthFt = len * 0.5 + 5.0 / 0.3048;
                            bb.Min = origin - perp * widthFt + new XYZ(0, 0, -3.0 / 0.3048);
                            bb.Max = origin + perp * widthFt + new XYZ(0, 0, 30.0 / 0.3048);
                            return new DrawingContext { CustomBounds = bb, Tag = "Grid-" + g.Name, PackageId = preset.PackageId };
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
                    }).Where(x => x != null);
                }
                else
                {
                    contextsToProduce = Enumerable.Empty<DrawingContext>();
                }

                using (var tg = new TransactionGroup(doc, "STING Produce Sections"))
                {
                    tg.Start();
                    foreach (var dctx in contextsToProduce)
                    {
                        using (var t = new Transaction(doc, $"STING Section {dctx.Tag}"))
                        {
                            t.Start();
                            try
                            {
                                foreach (var dt in pickedTypes)
                                {
                                    var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                                    views += pr.ViewIds.Count;
                                    if (pr.SheetId != ElementId.InvalidElementId) sheets++;
                                    warnings.AddRange(pr.Warnings);
                                }
                                t.Commit();
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"ProduceSections context={dctx.Tag}: {innerEx.Message}");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce Sections", views, sheets, warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceSections", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProduceExteriorElevationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches.
                DrawingTypePresentation.Prewarm(doc);
                DrawingProducer.PrimeBatchCaches(doc); // GAP-L

                var types = BatchProduceCommons.AllTypesByPurpose(doc, "Elevation")
                    .Where(t => !(t.Name ?? "").ToLowerInvariant().Contains("interior")).ToList();

                var dlg = new DrawingProductionConfigDialog(types, new List<string> { "Building (auto-detect footprint)" }, "ExteriorElevations", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;

                var elev = res.Preset?.ElevationConfig ?? new ElevationProductionConfig();
                var opts = BatchProduceCommons.BuildOptions(res.Preset);
                int views = 0, sheets = 0; var warnings = new List<string>();
                var pickedTypes = BatchProduceCommons.ResolveSelectedTypes(doc, res.SelectedDrawingTypeIds);

                // Footprint detection
                var walls = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().ToList();
                if (walls.Count == 0)
                {
                    TaskDialog.Show("STING", "No walls in project — cannot derive building footprint.");
                    return Result.Succeeded;
                }
                var bb = new BoundingBoxXYZ { Min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue), Max = new XYZ(double.MinValue, double.MinValue, double.MinValue) };
                foreach (var w in walls)
                {
                    var wbb = w.get_BoundingBox(null);
                    if (wbb == null) continue;
                    bb.Min = new XYZ(Math.Min(bb.Min.X, wbb.Min.X), Math.Min(bb.Min.Y, wbb.Min.Y), Math.Min(bb.Min.Z, wbb.Min.Z));
                    bb.Max = new XYZ(Math.Max(bb.Max.X, wbb.Max.X), Math.Max(bb.Max.Y, wbb.Max.Y), Math.Max(bb.Max.Z, wbb.Max.Z));
                }
                double offFt = elev.OffsetMm / 304.8;

                using (var tg = new TransactionGroup(doc, "STING Exterior Elevations"))
                {
                    tg.Start();
                    foreach (var face in elev.FacesTo ?? new List<string>())
                    {
                        // Revit's elevation marker face indexes: N=0, E=1, S=2, W=3 (viewer-facing).
                        int idx;
                        XYZ origin;
                        switch (face)
                        {
                            case "North": idx = 0; origin = new XYZ((bb.Min.X + bb.Max.X) / 2, bb.Max.Y + offFt, 0); break;
                            case "East":  idx = 1; origin = new XYZ(bb.Max.X + offFt, (bb.Min.Y + bb.Max.Y) / 2, 0); break;
                            case "South": idx = 2; origin = new XYZ((bb.Min.X + bb.Max.X) / 2, bb.Min.Y - offFt, 0); break;
                            case "West":  idx = 3; origin = new XYZ(bb.Min.X - offFt, (bb.Min.Y + bb.Max.Y) / 2, 0); break;
                            default: continue;
                        }
                        using (var t = new Transaction(doc, $"STING Exterior Elev {face}"))
                        {
                            t.Start();
                            try
                            {
                                foreach (var dt in pickedTypes)
                                {
                                    try
                                    {
                                        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                                            .Cast<ViewFamilyType>()
                                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Elevation);
                                        if (vft == null) { warnings.Add("No elevation ViewFamilyType."); continue; }
                                        var ownerPlan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
                                        if (ownerPlan == null) { warnings.Add("No owner plan for elevation marker."); continue; }
                                        var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, dt.Scale > 0 ? dt.Scale : 100);
                                        var view = marker.CreateElevation(doc, ownerPlan.Id, idx);
                                        try { view.Name = $"Exterior Elevation - {face} - {dt.Name}"; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                                        try
                                        {
                                            var fp = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                                            if (fp != null && !fp.IsReadOnly) fp.Set(elev.FarClipMm / 304.8);
                                        }
                                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                                        var ar = DrawingTypePresentation.Apply(doc, view, dt, new DrawingTypePresentation.ApplyOptions
                                        {
                                            AnnotationOptions = new AnnotationRunOptions { ViewScale = view.Scale }
                                        });
                                        warnings.AddRange(ar.Warnings);
                                        DrawingTypeStamper.Stamp(view, dt.Id);
                                        DrawingTypeStamper.StampPackage(view, res.Preset?.PackageId ?? dt.PackageId ?? "");
                                        ParameterHelpers.SetString(view, ParamRegistry.STING_VIEW_CONTEXT_TAG, $"exterior::face::{face}", overwrite: true);
                                        views++;
                                    }
                                    catch (Exception ex) { warnings.Add($"Exterior {face}/{dt.Name}: {ex.Message}"); }
                                }
                                t.Commit();
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"ProduceExteriorElevations face={face}: {innerEx.Message}");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce Exterior Elevations", views, sheets, warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceExteriorElevations", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RegeneratePackTemplatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
                var packs = ViewStylePackRegistry.GetLibrary(doc).Packs.Where(p => p.IsManaged).ToList();
                if (packs.Count == 0)
                {
                    TaskDialog.Show("STING", "No managed view-style packs found. Switch a pack to managed mode in the Drawing Type Editor first.");
                    return Result.Succeeded;
                }

                var pickItems = packs.Select(p => new StingListPicker.ListItem { Label = p.Name ?? p.Id, Tag = p }).ToList();
                var pickResult = StingListPicker.Show("Regenerate Pack Templates", "Pick managed packs to regenerate", pickItems, allowMultiSelect: true);
                if (pickResult == null || pickResult.Count == 0) return Result.Succeeded;
                var chosen = pickResult.Select(r => r.Tag as ViewStylePack).Where(p => p != null).ToList();

                int updated = 0; var warnings = new List<string>();
                ManagedTemplateSyncer.InvalidateCache();
                using (var tg = new TransactionGroup(doc, "STING Regenerate Pack Templates"))
                {
                    tg.Start();
                    foreach (var pack in chosen)
                    {
                        using (var t = new Transaction(doc, $"STING Regen {pack.Name}"))
                        {
                            t.Start();
                            try
                            {
                                foreach (var vt in new[] { ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.Section, ViewType.Elevation, ViewType.Detail, ViewType.ThreeD })
                                {
                                    var pr = new PackApplyResult();
                                    var id = ManagedTemplateSyncer.EnsureTemplate(doc, pack, vt, pr);
                                    if (id != ElementId.InvalidElementId) updated++;
                                    warnings.AddRange(pr.Warnings);
                                }
                                t.Commit();
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"RegeneratePackTemplates pack={pack.Name}: {innerEx.Message}");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Regenerate Pack Templates", updated, 0, warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("RegeneratePackTemplates", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]

    [Transaction(TransactionMode.ReadOnly)]
    public class DrawingPackageExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
                var packages = DrawingPackageManager.GetPackages(doc);
                if (packages.Count == 0) { TaskDialog.Show("STING", "No drawing packages found."); return Result.Succeeded; }
                var label = StingListPicker.Show("Export Drawing Package", "Pick a package to export", packages.Select(p => $"{p.PackageId} ({p.SheetCount} sheets)").ToList());
                if (string.IsNullOrEmpty(label)) return Result.Succeeded;
                var pkgId = packages.First(p => $"{p.PackageId} ({p.SheetCount} sheets)" == label).PackageId;

                var outDir = OutputLocationHelper.GetOutputDirectory(doc);
                var result = DrawingPackageManager.ExportPackage(doc, pkgId, outDir);
                BatchProduceCommons.ShowResult("Export Drawing Package", result.SheetCount, 0, result.Warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ExportPackage", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingPackageSequenceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
                var packages = DrawingPackageManager.GetPackages(doc);
                if (packages.Count == 0) { TaskDialog.Show("STING", "No drawing packages found."); return Result.Succeeded; }
                var seqLabel = StingListPicker.Show("Sequence Package", "Pick a package to (re)sequence", packages.Select(p => $"{p.PackageId} ({p.SheetCount} sheets)").ToList());
                if (string.IsNullOrEmpty(seqLabel)) return Result.Succeeded;
                var pkg = packages.First(p => $"{p.PackageId} ({p.SheetCount} sheets)" == seqLabel);

                // Order by current SheetNumber as a sane default; user can re-run after manual reorder.
                var orderedSheets = pkg.SheetIds
                    .Select(id => doc.GetElement(id) as ViewSheet)
                    .Where(s => s != null)
                    .OrderBy(s => s.SheetNumber)
                    .Select(s => s.Id)
                    .ToList();
                DrawingPackageManager.SetSequence(doc, pkg.PackageId, orderedSheets);
                TaskDialog.Show("STING", $"Sequenced {orderedSheets.Count} sheets for package '{pkg.PackageId}'.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("SequencePackage", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class DrawingPackageAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
                var packages = DrawingPackageManager.GetPackages(doc);
                if (packages.Count == 0) { TaskDialog.Show("STING", "No drawing packages found."); return Result.Succeeded; }
                var msg = new System.Text.StringBuilder();
                msg.AppendLine($"Found {packages.Count} package(s).");
                foreach (var p in packages)
                {
                    msg.AppendLine();
                    msg.AppendLine($"• {p.PackageId} — sheets: {p.SheetCount}, views: {p.ViewCount}");
                    var seqs = p.SheetIds
                        .Select(id => doc.GetElement(id) as ViewSheet)
                        .Where(s => s != null)
                        .Select(s => ParameterHelpers.GetInt(s, DrawingTypeStamper.PARAM_SHEET_SEQUENCE, 0))
                        .OrderBy(x => x).ToList();
                    int gaps = 0; for (int i = 1; i < seqs.Count; i++) if (seqs[i] - seqs[i - 1] > 1) gaps++;
                    msg.AppendLine($"  sequence range: {(seqs.Count > 0 ? seqs.First() : 0)}–{(seqs.Count > 0 ? seqs.Last() : 0)} (gaps: {gaps})");
                }
                TaskDialog.Show("Drawing Package Audit", msg.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("AuditPackages", ex); return Result.Failed; }
        }
    }
}
