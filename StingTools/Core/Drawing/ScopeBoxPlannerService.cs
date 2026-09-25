// StingTools — Scope-box planner · the operations, shared by dialog and commands
//
// One place for every write the planner makes, so the Scope Box Planner dialog,
// the ScopeBox_* dock-panel buttons and any workflow step run the same code.
// Each method returns a report a person can read; nothing fails silently.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    public enum ScopeBoxFootprintMode
    {
        /// <summary>One set of boxes per STING-LOC building box.</summary>
        Buildings,
        /// <summary>One set of boxes for the whole model's extent.</summary>
        Model,
        /// <summary>A set per selected level, from what is on that level; the level goes in the name.</summary>
        ModelPerLevel,
    }

    public sealed class ScopeBoxPlannerOptions
    {
        public List<string> DrawingTypeIds { get; set; } = new List<string>();
        public ScopeBoxFootprintMode FootprintMode { get; set; } = ScopeBoxFootprintMode.Model;
        /// <summary>Level ids: the levels to produce on, and in per-level mode the levels to plan.</summary>
        public List<long> LevelIds { get; set; } = new List<long>();
        public double FitFactor { get; set; } = ScopeBoxSizing.DefaultFitFactor;
        public double OverlapM { get; set; } = 2.0;
        public double PaddingM { get; set; } = 1.0;
        public bool AlignToGrid { get; set; } = true;
    }

    public sealed class ScopeBoxPlannerContext
    {
        public ScopeBoxStyle Style { get; set; }
        public Dictionary<string, (double W, double H)> Drawables { get; set; }
        /// <summary>Every drawing type an area box can serve — the checklist.</summary>
        public List<DrawingType> Candidates { get; set; } = new List<DrawingType>();
        public List<ScopeBoxSeed> Seeds { get; set; } = new List<ScopeBoxSeed>();
        public List<Level> Levels { get; set; } = new List<Level>();
        public int BuildingBoxCount { get; set; }
        public double GridAngleRad { get; set; }
        public ScopeBoxPlanFile SavedPlan { get; set; }
        /// <summary>Set when a saved plan exists but cannot be read. Create refuses while it is set.</summary>
        public string SavedPlanError { get; set; }
        public HashSet<string> ExistingNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Problems found while loading — shown in the dialog, never swallowed.</summary>
        public List<string> Problems { get; } = new List<string>();
    }

    public static class ScopeBoxPlannerService
    {
        public const string PlanFileName  = "scope_box_plan.json";
        public const string StyleFileName = "scope_box_style.json";

        public static ScopeBoxStyle LoadStyle(Document doc, List<string> problems)
        {
            string corporate = null, project = null;
            try
            {
                var p = StingToolsApp.FindDataFile("STING_SCOPE_BOX_STYLE.json");
                if (p != null && File.Exists(p)) corporate = File.ReadAllText(p);
                else problems?.Add("STING_SCOPE_BOX_STYLE.json is missing from the data folder — colours unavailable.");
            }
            catch (Exception ex) { problems?.Add($"Reading STING_SCOPE_BOX_STYLE.json: {ex.Message}"); }
            try
            {
                var p = StingPaths.MetaFile(doc, "_BIM_COORD", StyleFileName);
                if (File.Exists(p)) project = File.ReadAllText(p);
            }
            catch (Exception ex) { problems?.Add($"Reading the project {StyleFileName}: {ex.Message}"); }
            try { return ScopeBoxStyle.Load(corporate, project); }
            catch (Exception ex)
            {
                problems?.Add($"The project {StyleFileName} is not valid JSON ({ex.Message}) — using the corporate style only.");
                try { return ScopeBoxStyle.Load(corporate, null); }
                catch (Exception ex2) { problems?.Add($"STING_SCOPE_BOX_STYLE.json is not valid JSON: {ex2.Message}"); return new ScopeBoxStyle(); }
            }
        }

        public static ScopeBoxPlanFile LoadPlan(Document doc, out string error)
        {
            error = null;
            try
            {
                var p = StingPaths.MetaFile(doc, "_BIM_COORD", PlanFileName);
                return File.Exists(p) ? ScopeBoxPlanFile.FromJson(File.ReadAllText(p)) : null;
            }
            catch (Exception ex) { error = $"{PlanFileName} could not be read: {ex.Message}"; return null; }
        }

        public static string SavePlan(Document doc, ScopeBoxPlanFile plan)
        {
            var p = StingPaths.MetaFile(doc, "_BIM_COORD", PlanFileName);
            File.WriteAllText(p, plan.ToJson());
            return p;
        }

        public static Dictionary<string, (double W, double H)> LoadDrawables(List<string> problems)
        {
            try
            {
                var tb = StingToolsApp.FindDataFile("STING_TITLE_BLOCKS.json");
                var d = tb != null && File.Exists(tb)
                    ? ScopeBoxSizing.DrawablesFromTitleBlocks(File.ReadAllText(tb))
                    : new Dictionary<string, (double W, double H)>();
                if (d.Count == 0) problems?.Add("No drawable areas read from STING_TITLE_BLOCKS.json — nothing can be sized.");
                return d;
            }
            catch (Exception ex)
            {
                problems?.Add($"STING_TITLE_BLOCKS.json: {ex.Message}");
                return new Dictionary<string, (double W, double H)>();
            }
        }

        public static ScopeBoxPlannerContext Load(Document doc)
        {
            var ctx = new ScopeBoxPlannerContext();
            ctx.Style = LoadStyle(doc, ctx.Problems);
            ctx.Drawables = LoadDrawables(ctx.Problems);

            ctx.Candidates = (DrawingTypeRegistry.GetLibrary(doc)?.DrawingTypes ?? new List<DrawingType>())
                .Where(t => ScopeBoxSizing.IsAreaCandidate(t, out _))
                .OrderBy(t => t.Discipline).ThenBy(t => t.Scale).ThenBy(t => t.Id).ToList();
            ctx.Seeds = ScopeBoxRevit.Seeds(doc, ctx.Problems);
            ctx.Levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            var boxes = ScopeBoxRevit.AllBoxes(doc);
            ctx.BuildingBoxCount = boxes.Count(b => ScopeBoxNames.Classify(b.Name) == ScopeBoxKind.Building);
            foreach (var b in boxes) ctx.ExistingNames.Add(b.Name ?? "");
            ctx.GridAngleRad = ScopeBoxRevit.GridAngleRad(doc);
            ctx.SavedPlan = LoadPlan(doc, out var planError);
            ctx.SavedPlanError = planError;
            if (planError != null) ctx.Problems.Add(planError);
            return ctx;
        }

        public static ScopeBoxPlanRequest BuildRequest(Document doc, ScopeBoxPlannerContext ctx, ScopeBoxPlannerOptions o, List<string> warnings)
        {
            var req = new ScopeBoxPlanRequest
            {
                DrawingTypes = ctx.Candidates.Where(t => o.DrawingTypeIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList(),
                Drawables = ctx.Drawables, Seeds = ctx.Seeds, ExistingNames = ctx.ExistingNames,
                FitFactor = o.FitFactor, OverlapM = o.OverlapM, PaddingM = o.PaddingM,
                GridAngleRad = o.AlignToGrid ? ctx.GridAngleRad : 0,
            };
            switch (o.FootprintMode)
            {
                case ScopeBoxFootprintMode.Buildings:
                    req.Footprints = ScopeBoxRevit.BuildingFootprints(doc, warnings);
                    if (req.Footprints.Count == 0) warnings.Add("No STING-LOC:: building boxes in the project — draw one per building, or plan the whole model.");
                    break;
                case ScopeBoxFootprintMode.ModelPerLevel:
                    foreach (var l in ctx.Levels.Where(l => o.LevelIds.Contains(l.Id.Value)))
                        req.Footprints.Add(ScopeBoxRevit.ModelFootprint(doc, l));
                    if (req.Footprints.Count == 0) warnings.Add("Per-level planning needs at least one level ticked.");
                    break;
                default:
                    req.Footprints.Add(ScopeBoxRevit.ModelFootprint(doc, null));
                    break;
            }
            return req;
        }

        private static List<string> LevelCodes(ScopeBoxPlannerContext ctx, ScopeBoxPlannerOptions o)
            => ctx.Levels.Where(l => o.LevelIds.Contains(l.Id.Value)).Select(ParameterHelpers.GetLevelCodeForLevel).ToList();

        /// <summary>Create the New boxes and save the plan (merged over any saved plan).</summary>
        public static string Create(Document doc, ScopeBoxPlannerContext ctx, ScopeBoxPlannerOptions o,
            ScopeBoxPlanRequest req, ScopeBoxPlanResult res)
        {
            // Saving merges over the saved plan. If that file cannot be read, saving would
            // overwrite it and every box it lists would lose its drawing types.
            if (ctx.SavedPlanError != null)
                return $"Nothing created. {ctx.SavedPlanError} Fix or rename the file first — saving now would overwrite it.";
            var report = new List<string>();
            int made;
            using (var tx = new Transaction(doc, "STING Create Scope Boxes"))
            {
                tx.Start();
                made = ScopeBoxRevit.Create(doc, res.Boxes, report);
                tx.Commit();
            }
            var file = ScopeBoxPlanFile.From(req, res, LevelCodes(ctx, o), o.FootprintMode.ToString());
            var merged = ScopeBoxPlanFile.Merge(ctx.SavedPlan, file);
            var path = SavePlan(doc, merged);
            ctx.SavedPlan = merged;

            var sb = new StringBuilder();
            sb.AppendLine($"Created {made} scope box(es). Plan saved: {path}");
            int exists = res.Boxes.Count(b => b.Status == PlannedBoxStatus.Exists);
            int noSeed = res.Boxes.Count(b => b.Status == PlannedBoxStatus.NoSeed);
            if (exists > 0) sb.AppendLine($"{exists} already existed and were kept as they are.");
            if (noSeed > 0) sb.AppendLine($"{noSeed} were not created: their size class has no seed. See 'Seeds needed'.");
            foreach (var r in report) sb.AppendLine("• " + r);
            return sb.ToString();
        }

        public static string Colour(Document doc, ScopeBoxColourMode mode, bool allPlanViews, View active)
        {
            var report = new List<string>();
            var style = LoadStyle(doc, report);
            var plan = LoadPlan(doc, out var err);
            if (err != null) report.Add(err);
            var views = allPlanViews ? ScopeBoxRevit.PlanViews(doc) : new List<View> { active };
            views = views.Where(v => v != null && !v.IsTemplate).ToList();
            if (views.Count == 0) return "No view to colour.";
            int n;
            using (var tx = new Transaction(doc, mode == ScopeBoxColourMode.Off ? "STING Clear Scope Box Colours" : "STING Colour Scope Boxes"))
            {
                tx.Start();
                n = ScopeBoxRevit.Colour(doc, views, style, mode, plan, report);
                tx.Commit();
            }
            var head = mode == ScopeBoxColourMode.Off
                ? $"Cleared STING colours on {n} box/view pair(s) in {views.Count} view(s)."
                : $"Coloured by {mode}: {n} box/view pair(s) in {views.Count} view(s).";
            return head + (report.Count > 0 ? "\n• " + string.Join("\n• ", report) : "");
        }

        /// <summary>Rename the selected scope boxes to STING-SEED::&lt;w&gt;x&lt;d&gt; from their measured size.</summary>
        public static string RegisterSeeds(Document doc, ICollection<ElementId> selected)
        {
            var report = new List<string>();
            var boxes = (selected ?? new List<ElementId>()).Select(doc.GetElement)
                .Where(e => e?.Category != null && e.Category.Id.Value == (long)BuiltInCategory.OST_VolumeOfInterest).ToList();
            if (boxes.Count == 0) return "Select one or more scope boxes first, then register them as seeds.";
            var existing = ScopeBoxRevit.Seeds(doc, null);
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            int done = 0;
            using (var tx = new Transaction(doc, "STING Register Scope Box Seeds"))
            {
                tx.Start();
                foreach (var b in boxes)
                {
                    if (!ScopeBoxRevit.TryMeasure(b, out var m, out var why)) { report.Add($"'{b.Name}' {why}."); continue; }
                    var twin = existing.FirstOrDefault(s => s.Id != b.Id.Value.ToString()
                        && Math.Abs(s.WidthM - m.WidthM) < 0.05 && Math.Abs(s.DepthM - m.DepthM) < 0.05);
                    if (twin != null) { report.Add($"'{b.Name}' is the same size as seed '{twin.Name}' — not registered twice."); continue; }
                    var name = ScopeBoxNames.ComposeSeed(m.WidthM, m.DepthM);
                    try { b.Name = name; done++; existing.Add(new ScopeBoxSeed { Id = b.Id.Value.ToString(), Name = name, WidthM = m.WidthM, DepthM = m.DepthM }); }
                    catch (Exception ex) { report.Add($"'{b.Name}' could not be renamed to '{name}': {ex.Message}"); continue; }
                    if (levels.Count > 0 && (m.ZMinFt > levels.Min(l => l.Elevation) || m.ZMaxFt < levels.Max(l => l.Elevation)))
                        report.Add($"'{name}' does not span every level; its copies will not be offered to plans on the levels it misses. Make it taller.");
                }
                tx.Commit();
            }
            return $"Registered {done} seed(s)." + (report.Count > 0 ? "\n• " + string.Join("\n• ", report) : "");
        }

        /// <summary>Copy STING-SEED boxes from another project or template into this one.</summary>
        public static string ImportSeeds(UIApplication app, Document target, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "No file chosen.";
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(target.PathName ?? ""), StringComparison.OrdinalIgnoreCase))
                return "That is the open project — pick another file.";
            var report = new List<string>();
            Document source = null;
            try
            {
                source = app.Application.OpenDocumentFile(path);
                int n;
                using (var tx = new Transaction(target, "STING Import Scope Box Seeds"))
                {
                    tx.Start();
                    n = ScopeBoxRevit.ImportSeeds(source, target, report);
                    tx.Commit();
                }
                return $"Imported {n} seed(s) from {Path.GetFileName(path)}." + (report.Count > 0 ? "\n• " + string.Join("\n• ", report) : "");
            }
            finally
            {
                try { source?.Close(false); } catch (Exception ex) { StingLog.Warn($"ImportSeeds close: {ex.Message}"); }
            }
        }

        /// <summary>What production would make — shown before anything is produced.</summary>
        public sealed class ProductionItem
        {
            public Element Box; public Level Level; public DrawingType Type;
        }

        public static List<ProductionItem> PlanProduction(Document doc, ScopeBoxPlanFile plan, List<string> report)
        {
            var items = new List<ProductionItem>();
            if (plan == null) { report.Add($"No saved plan ({PlanFileName}) — create boxes in the Scope Box Planner first."); return items; }
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            var drawables = LoadDrawables(report);
            foreach (var box in ScopeBoxRevit.AllBoxes(doc).Where(b => ScopeBoxNames.Classify(b.Name) == ScopeBoxKind.Area))
            {
                if (!plan.TryResolve(box.Name, out var typeIds, out var levelCodes, out var why)) { report.Add($"'{box.Name}': {why}."); continue; }
                ScopeBoxRevit.TryMeasure(box, out var m, out _);
                var boxLevels = levelCodes.Count > 0
                    ? levels.Where(l => levelCodes.Contains(ParameterHelpers.GetLevelCodeForLevel(l), StringComparer.OrdinalIgnoreCase)).ToList()
                    : levels.Where(l => m == null || (l.Elevation >= m.ZMinFt && l.Elevation <= m.ZMaxFt)).ToList();
                foreach (var code in levelCodes.Where(c => !levels.Any(l => string.Equals(ParameterHelpers.GetLevelCodeForLevel(l), c, StringComparison.OrdinalIgnoreCase))))
                    report.Add($"'{box.Name}': level '{code}' is not in the project — skipped.");
                foreach (var id in typeIds)
                {
                    var dt = DrawingTypeRegistry.Get(doc, id);
                    if (dt == null) { report.Add($"'{box.Name}': drawing type '{id}' is no longer in the catalogue."); continue; }
                    if (m != null && ScopeBoxSizing.TryMaxExtent(dt, drawables, plan.FitFactor, out var w, out var d, out _)
                        && (Math.Min(m.WidthM, m.DepthM) > Math.Min(w, d) + 0.05 || Math.Max(m.WidthM, m.DepthM) > Math.Max(w, d) + 0.05))
                        report.Add($"'{box.Name}' ({ScopeBoxNames.Metres(m.WidthM)} × {ScopeBoxNames.Metres(m.DepthM)} m) is larger than '{id}' allows "
                                 + $"({ScopeBoxNames.Metres(w)} × {ScopeBoxNames.Metres(d)} m) — its plan will not fit the slot at 1:{dt.Scale}.");
                    foreach (var l in boxLevels) items.Add(new ProductionItem { Box = box, Level = l, Type = dt });
                }
            }
            return items;
        }

        public static string Produce(Document doc, List<ProductionItem> items, bool sheets)
        {
            var opts = new ProduceOptions { CreateSheet = sheets, PlaceOnSheet = sheets };
            int views = 0, madeSheets = 0; var warnings = new List<string>();
            DrawingTypePresentation.Prewarm(doc);
            DrawingProducer.PrimeBatchCaches(doc);
            using (var tg = new TransactionGroup(doc, "STING Produce From Area Boxes"))
            {
                tg.Start();
                foreach (var it in items)
                {
                    using (var t = new Transaction(doc, $"STING Area {it.Box.Name} {it.Level.Name} {it.Type.Id}"))
                    {
                        t.Start();
                        try
                        {
                            ScopeBoxNames.TryParseArea(it.Box.Name, out var area, out _, out _);
                            var pr = DrawingProducer.ProduceAllViews(doc, it.Type,
                                new DrawingContext { Level = it.Level, ScopeBox = it.Box, Tag = area }, opts);
                            views += pr.ViewIds.Count;
                            if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) madeSheets++;
                            warnings.AddRange(pr.Warnings);
                            t.Commit();
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            warnings.Add($"{it.Box.Name} / {it.Level.Name} / {it.Type.Id}: {ex.Message}");
                        }
                    }
                }
                tg.Assimilate();
            }
            var sb = new StringBuilder($"Produced {views} view(s)" + (sheets ? $" and {madeSheets} new sheet(s)" : "") + $" from {items.Count} box × level × type combination(s).");
            foreach (var w in warnings.Distinct().Take(25)) sb.Append("\n• ").Append(w);
            if (warnings.Distinct().Count() > 25) sb.Append($"\n… {warnings.Distinct().Count() - 25} more in the log.");
            foreach (var w in warnings) StingLog.Warn("ScopeBox produce: " + w);
            return sb.ToString();
        }
    }
}
