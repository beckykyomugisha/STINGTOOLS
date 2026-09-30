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

        /// <summary>
        /// The per-level drawing types each discipline's PLAN (and RCP) routes to in this
        /// project's routing table — the same answer DrawingDispatcher gives every other
        /// caller. Phase is left blank so only phase-wildcard rules match: the
        /// PRESENTATION / TECHNICAL / FABRICATION variants are chosen deliberately, not
        /// by a per-level default.
        /// </summary>
        internal static DisciplineRouting RoutePerLevel(Document doc, IEnumerable<string> disciplines)
            => DisciplinePlanRouting.Select(disciplines,
                (disc, docType) => DrawingDispatcher.Resolve(doc, disc, null, docType));

        /// <summary>What a per-level step produces when params.drawingTypes names nothing.</summary>
        internal sealed class PerLevelSelection
        {
            public List<DrawingType> Types = new List<DrawingType>();
            /// <summary>Skips a (type, level) pair whose discipline has nothing modelled on that level; null = produce every pair.</summary>
            public Func<DrawingType, Level, bool> Include;
            /// <summary>One line per routing decision, for the log / report.</summary>
            public List<string> Notes = new List<string>();
        }

        /// <summary>
        /// The per-level default for an MEP set: for each of M / E / P / FP / MG that has
        /// anything modelled, the plan type its PLAN routes to (RoutePerLevel — the set
        /// the dialogs pre-tick), and only on the levels that discipline occupies. It used
        /// to be every MEP Plan type in the catalogue — ~24 including the presentation,
        /// technical, fabrication, SUDS 1:500 and healthcare variants — on every level.
        /// Shared by Produce Per Level, Produce &amp; Export and MEP Plans Per Level.
        /// </summary>
        internal static PerLevelSelection RoutedMepPerLevel(Document doc)
            => RoutedMepPerLevel(doc, StingTools.Core.Mep.MepLevelViewProducer.LevelsByDiscipline(doc));

        /// <summary>As <see cref="RoutedMepPerLevel(Document)"/>, with the discipline →
        /// level presence the caller has already computed, so a command that needs it
        /// for its own report does not collect every MEP element twice.</summary>
        internal static PerLevelSelection RoutedMepPerLevel(Document doc, Dictionary<string, HashSet<ElementId>> presence)
        {
            var sel = new PerLevelSelection();
            presence = presence ?? StingTools.Core.Mep.MepLevelViewProducer.LevelsByDiscipline(doc);
            var present = StingTools.Core.Mep.MepLevelViewProducer.Disciplines.Where(presence.ContainsKey).ToList();
            var routing = RoutePerLevel(doc, present);
            sel.Types = routing.Types;
            foreach (var p in routing.Picks) sel.Notes.Add($"{p.Discipline} / {p.DocType} → {p.Type.Id}");
            foreach (var d in routing.Unrouted) sel.Notes.Add($"{d}: no drawing type routes from {d} / PLAN — not produced.");
            foreach (var n in routing.NotPerLevel) sel.Notes.Add($"{n} is not a per-level plan — not produced.");
            foreach (var d in StingTools.Core.Mep.MepLevelViewProducer.Disciplines.Where(d => !presence.ContainsKey(d)))
                sel.Notes.Add($"{d}: nothing modelled — skipped.");
            sel.Include = (dt, lvl) =>
            {
                foreach (var d in routing.DisciplinesFor(dt.Id))
                    if (!presence.TryGetValue(d, out var set) || set.Contains(lvl.Id)) return true;
                return false;
            };
            return sel;
        }

        /// <summary>
        /// A per-level step's types: params.drawingTypes when it names some (produced on
        /// every picked level, as asked), else <see cref="RoutedMepPerLevel"/>. False with
        /// <paramref name="error"/> for an unknown id or nothing to produce.
        /// </summary>
        internal static bool TryStepPerLevelTypes(Document doc, out PerLevelSelection sel, out string error)
        {
            error = null;
            var requested = HeadlessProductionInputs.ParseList(WorkflowEngine.StepParam("drawingTypes"));
            if (requested.Count > 0)
            {
                sel = new PerLevelSelection
                {
                    Types = HeadlessProductionInputs.SelectTypes(DrawingTypeRegistry.ListAll(doc), requested, new string[0], out var unknown)
                };
                if (unknown.Count > 0)
                { error = "params.drawingTypes names drawing type(s) not in the catalogue: " + string.Join(", ", unknown) + "."; return false; }
                if (sel.Types.Count == 0) { error = "params.drawingTypes resolved to no drawing types."; return false; }
                return true;
            }
            sel = RoutedMepPerLevel(doc);
            foreach (var n in sel.Notes) StingLog.Info("Per-level default: " + n);
            if (sel.Types.Count == 0)
            {
                error = "no M/E/P/FP/MG discipline has anything modelled that routes to a per-level plan"
                      + (sel.Notes.Count > 0 ? " (" + string.Join(" ", sel.Notes) + ")" : "")
                      + "; name the types in params.drawingTypes.";
                return false;
            }
            return true;
        }

        // ── Workflow (headless) inputs ─────────────────────────────────────────
        // Inside a preset these commands take their inputs from the step's "params"
        // (see HeadlessProductionInputs for the keys and defaults) instead of a dialog.

        internal static bool Headless => WorkflowEngine.IsRunningPreset;

        internal static ViewDuplicateOption ToRevitDuplicate(string word)
        {
            switch (word)
            {
                case HeadlessProductionInputs.DuplicateWithDetailing: return ViewDuplicateOption.WithDetailing;
                case HeadlessProductionInputs.DuplicateAsDependent:   return ViewDuplicateOption.AsDependent;
                default:                                              return ViewDuplicateOption.Duplicate;
            }
        }

        /// <summary>
        /// ProduceOptions from the running step's params: output (views and sheets by
        /// default), duplicateOption, packageId. False with <paramref name="error"/> when
        /// a value cannot be read — the step fails rather than guessing.
        /// </summary>
        internal static bool TryStepOptions(out ProduceOptions opts, out string packageId, out string error)
        {
            opts = null; error = null;
            packageId = WorkflowEngine.StepParam("packageId");
            if (string.IsNullOrWhiteSpace(packageId)) packageId = null;
            var outputRaw = WorkflowEngine.StepParam("output");
            var sheets = HeadlessProductionInputs.ParseSheets(outputRaw);
            if (sheets == null)
            { error = $"params.output '{outputRaw}' is not 'Views and sheets' or 'Views only'."; return false; }
            var dupRaw = WorkflowEngine.StepParam("duplicateOption");
            var dup = HeadlessProductionInputs.ParseDuplicateOption(dupRaw);
            if (dup == null)
            { error = $"params.duplicateOption '{dupRaw}' is not Duplicate, DuplicateAsDependent or DuplicateWithDetailing."; return false; }
            opts = new ProduceOptions
            {
                CreateSheet = sheets.Value,
                PlaceOnSheet = sheets.Value,
                RunAnnotation = true,
                Idempotent = true,
                DuplicateOption = ToRevitDuplicate(dup),
            };
            return true;
        }

        /// <summary>A step's outcome as one line for the workflow report; warnings go to the log.</summary>
        internal static string StepSummary(string title, int views, int sheets, IList<string> warnings)
        {
            foreach (var w in (warnings ?? new List<string>()).Distinct()) StingLog.Warn($"{title}: {w}");
            var s = $"{title}: {views} view(s), {sheets} new sheet(s)";
            if (warnings != null && warnings.Count > 0) s += $", {warnings.Distinct().Count()} warning(s) (see the STING log)";
            StingLog.Info(s);
            return s + ".";
        }

        /// <summary>A dialog outside a workflow; the log inside one.</summary>
        internal static void Show(string title, string body)
        {
            if (Headless) StingLog.Info($"{title}: {body}");
            else TaskDialog.Show(title, body);
        }

        /// <summary>A confirmation outside a workflow; inside one, running the step IS the confirmation.</summary>
        internal static bool Confirm(TaskDialog td)
            => Headless || td.Show() == TaskDialogResult.Ok;

        /// <summary>
        /// What the preset changed from the drawing types' own settings, for the report —
        /// so a scale, detail level or annotation choice made in the dialog is visible in
        /// the outcome, not only in the saved preset. Null when it changed nothing.
        /// </summary>
        internal static string PresetSummary(DrawingProductionPreset preset)
        {
            var g = preset?.General;
            if (g == null) return null;
            var parts = new List<string>();
            if (g.ScaleOverride is int s && s > 0) parts.Add($"scale 1:{s}");
            if (!string.IsNullOrWhiteSpace(g.DetailLevelOverride)) parts.Add($"detail level {g.DetailLevelOverride}");
            if (!g.RunAnnotation) parts.Add("no annotation");
            else
            {
                var off = new List<string>();
                if (!g.RunAutoTag) off.Add("auto-tag");
                if (!g.RunAutoDim) off.Add("auto-dimension");
                if (!g.RunDecorative) off.Add("decorative");
                if (!g.RunSpots) off.Add("spots");
                if (off.Count > 0) parts.Add("annotation without " + string.Join(", ", off));
            }
            return parts.Count == 0 ? null : "Preset overrides applied to every produced view: " + string.Join(", ", parts) + ".";
        }

        internal static void ShowResult(string title, int views, int sheets, IList<string> warnings,
            DrawingProductionPreset preset = null)
        {
            var msg = new System.Text.StringBuilder();
            msg.AppendLine($"Views created: {views}");
            msg.AppendLine($"Sheets created: {sheets}");
            var presetLine = PresetSummary(preset);
            if (presetLine != null) msg.AppendLine(presetLine);
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (BatchProduceCommons.Headless) return ExecuteInWorkflow(doc, levels, ref message);

                // PERF-01: warm the per-document caches so every per-level
                // / per-DrawingType Apply call hits the (template name →
                // ElementId) and (pack id → pack) memos.
                DrawingTypePresentation.Prewarm(doc);

                var types = BatchProduceCommons.AllTypesByPurpose(doc, "Plan", "RCP");
                var contextLabels = levels.Select(l => l.Name).ToList();

                var dlg = new DrawingProductionConfigDialog(types, contextLabels, "PerLevel", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;
                // GAP-L: primed only once the dialog is confirmed (it was primed before the
                // dialog and never reset, so a cancelled run left the caches, and a
                // confirmed one kept them past the command); reset in finally.
                DrawingProducer.PrimeBatchCaches(doc);

                var opts = BatchProduceCommons.BuildOptions(res.Preset);
                var pickedTypes = BatchProduceCommons.ResolveSelectedTypes(doc, res.SelectedDrawingTypeIds);
                var pickedLevels = res.SelectedContexts
                    .Select(n => levels.FirstOrDefault(l => l.Name == n)).Where(l => l != null).ToList();
                int views = 0, sheets = 0; var warnings = new List<string>();
                Produce(doc, pickedTypes, pickedLevels, opts, res.Preset?.PackageId, ref views, ref sheets, warnings);
                BatchProduceCommons.ShowResult("Produce Per Level", views, sheets, warnings, res.Preset);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceViewsPerLevel", ex); return Result.Failed; }
            finally { DrawingProducer.ResetBatchCaches(); }
        }

        /// <summary>
        /// Inside a workflow: params.drawingTypes (default: the plan type each modelled
        /// M/E/P/FP/MG discipline routes to, on the levels it occupies —
        /// BatchProduceCommons.RoutedMepPerLevel), params.levels (default every level),
        /// params.output, params.duplicateOption, params.packageId. Fails the step, with
        /// the reason, when an input is wrong or nothing was produced.
        /// </summary>
        private static Result ExecuteInWorkflow(Document doc, List<Level> levels, ref string message)
        {
            if (!BatchProduceCommons.TryStepPerLevelTypes(doc, out var sel, out var err)
                || !BatchProduceCommons.TryStepOptions(out var opts, out var packageId, out err))
            { message = "Produce Per Level: " + err; return Result.Failed; }
            var types = sel.Types;

            if (levels.Count == 0) { message = "Produce Per Level: the model has no levels."; return Result.Failed; }
            var names = HeadlessProductionInputs.SelectNames(levels.Select(l => l.Name).ToList(),
                HeadlessProductionInputs.ParseList(WorkflowEngine.StepParam("levels")), out var unknownLevels);
            if (unknownLevels.Count > 0)
            { message = "Produce Per Level: params.levels names level(s) not in the model: " + string.Join(", ", unknownLevels) + "."; return Result.Failed; }
            var picked = levels.Where(l => names.Contains(l.Name)).ToList();

            int views = 0, sheets = 0; var warnings = new List<string>();
            DrawingTypePresentation.Prewarm(doc);
            using (DrawingProducer.PrimeBatchScope(doc))
                Produce(doc, types, picked, opts, packageId, ref views, ref sheets, warnings, sel.Include);
            message = BatchProduceCommons.StepSummary("Produce Per Level", views, sheets, warnings);
            if (views == 0) { message += " Nothing was produced."; return Result.Failed; }
            return Result.Succeeded;
        }

        /// <summary>
        /// One transaction per level, every picked type on it — shared by the dialog, the
        /// workflow, the Project Setup Wizard and the HVAC panel's per-level button.
        /// <paramref name="include"/> (optional) skips a (type, level) pair, e.g. a
        /// discipline with nothing modelled on that level. Must be called with no
        /// transaction open.
        /// </summary>
        internal static void Produce(Document doc, List<DrawingType> types, List<Level> levels, ProduceOptions opts,
            string packageId, ref int views, ref int sheets, List<string> warnings,
            Func<DrawingType, Level, bool> include = null)
        {
            using (var tg = new TransactionGroup(doc, "STING Produce Per Level"))
            {
                tg.Start();
                foreach (var level in levels)
                {
                    using (var t = new Transaction(doc, $"STING Produce Per Level - {level.Name}"))
                    {
                        t.Start();
                        try
                        {
                            // Counted per transaction and added to the totals only once
                            // Revit has committed it: a commit a failure handler rolls
                            // back produced nothing, and must not read as production.
                            int levelViews = 0, levelSheets = 0;
                            var levelTypes = new List<string>();
                            foreach (var dt in types)
                            {
                                if (include != null && !include(dt, level)) continue;
                                var dctx = new DrawingContext { Level = level, PackageId = packageId };
                                var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                                levelViews += pr.ViewIds.Count;
                                if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) levelSheets++;   // P-9: reuse is not production
                                warnings.AddRange(pr.Warnings);
                                levelTypes.Add(dt.Id);
                            }
                            var status = t.Commit();
                            if (status == TransactionStatus.Committed) { views += levelViews; sheets += levelSheets; }
                            else if (levelTypes.Count > 0)
                            {
                                var w = $"{level.Name}: the transaction did not commit ({status}); {levelViews} view(s) of "
                                      + $"{string.Join(", ", levelTypes)} were not kept.";
                                StingLog.Warn("ProduceViewsPerLevel " + w);
                                warnings.Add(w);
                            }
                        }
                        catch (Exception innerEx)
                        {
                            StingLog.Warn($"ProduceViewsPerLevel level={level.Name}: {innerEx.Message}");
                            warnings.Add($"{level.Name}: {innerEx.Message} — rolled back.");
                            t.RollBack();
                        }
                    }
                }
                tg.Assimilate();
            }
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches.
                DrawingTypePresentation.Prewarm(doc);

                // P-13c / K-C5: one parser. This used to prefix-filter here and
                // Split("::") by index below, which accepted names the binder
                // rejects — so a box could reach production with a drawing-type
                // id containing a space and fail later, deeper, less legibly.
                // ScopeBoxBinder.TryParseName is now the only grammar in the tree.
                var scopes = new List<Element>();
                var bindingByName = new Dictionary<string, ScopeBoxBinding>(StringComparer.Ordinal);
                var malformed = new List<string>();
                foreach (var e in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType())
                {
                    var nm = e.Name ?? "";
                    if (ScopeBoxBinder.TryParseName(nm, out var b, out var why))
                    {
                        b.ScopeBox = e;
                        scopes.Add(e);
                        bindingByName[nm] = b;
                    }
                    else if (why != null) malformed.Add(nm);
                }
                if (scopes.Count == 0)
                {
                    var why = malformed.Count == 0
                        ? "No STING::… scope boxes found in this project."
                        : $"No usable STING::… scope boxes.\n\n{malformed.Count} box(es) carry the "
                          + $"STING:: prefix but fail the naming grammar:\n  • "
                          + string.Join("\n  • ", malformed.Take(10))
                          + "\n\nUse the Scope Box Manager to fix them.";
                    // In a workflow the boxes ARE the required input: no boxes, no drawings,
                    // and a step that "succeeded" at producing nothing would hide that.
                    if (BatchProduceCommons.Headless)
                    { message = "Produce From Scope Boxes: " + why.Replace("\n\n", " ").Replace("\n  • ", "; "); return Result.Failed; }
                    TaskDialog.Show("STING", why);
                    return Result.Succeeded;
                }
                var dtIds = bindingByName.Values.Select(b => b.DrawingTypeId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var lib = DrawingTypeRegistry.GetLibrary(doc);
                var types = (lib?.DrawingTypes ?? new List<DrawingType>())
                    .Where(t => dtIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

                if (BatchProduceCommons.Headless)
                    return ExecuteInWorkflow(doc, scopes, bindingByName, types, levels, ref message);

                var dlg = new DrawingProductionConfigDialog(types, scopes.Select(s => s.Name).ToList(), "ScopeBoxes", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;
                // GAP-L: primed only once the dialog is confirmed (it was primed before the
                // dialog and never reset, so a cancelled run left the caches, and a
                // confirmed one kept them past the command); reset in finally.
                DrawingProducer.PrimeBatchCaches(doc);

                var opts = BatchProduceCommons.BuildOptions(res.Preset);
                int views = 0, sheets = 0; var warnings = new List<string>();
                var picked = res.SelectedContexts.Select(n => scopes.FirstOrDefault(s => s.Name == n)).Where(s => s != null).ToList();
                // DTW-26: only the ticked drawing types. Every type a box was bound to was
                // produced, ticked or not, so unticking a type in the dialog did nothing.
                var tickedTypes = types.Where(t => res.SelectedDrawingTypeIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();
                int boxesLeftOut = picked.Count(s => bindingByName.TryGetValue(s.Name ?? "", out var b)
                    && !tickedTypes.Any(t => string.Equals(t.Id, b.DrawingTypeId, StringComparison.OrdinalIgnoreCase)));
                if (boxesLeftOut > 0)
                    warnings.Add($"{boxesLeftOut} ticked box(es) are bound to a drawing type that is not ticked — not produced.");
                Produce(doc, picked, bindingByName, tickedTypes, levels, opts, res.Preset?.PackageId, ref views, ref sheets, warnings);
                BatchProduceCommons.ShowResult("Produce From Scope Boxes", views, sheets, warnings, res.Preset);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceFromScopeBoxes", ex); return Result.Failed; }
            finally { DrawingProducer.ResetBatchCaches(); }
        }

        /// <summary>
        /// Inside a workflow: every well-formed STING:: box bound to an M/E/P/FP/MG
        /// drawing type, or the types params.drawingTypes names (any discipline);
        /// params.output / duplicateOption / packageId as for Produce Per Level.
        /// </summary>
        private static Result ExecuteInWorkflow(Document doc, List<Element> scopes,
            Dictionary<string, ScopeBoxBinding> bindingByName, List<DrawingType> types, List<Level> levels,
            ref string message)
        {
            if (!BatchProduceCommons.TryStepOptions(out var opts, out var packageId, out var err))
            { message = "Produce From Scope Boxes: " + err; return Result.Failed; }
            var requested = HeadlessProductionInputs.ParseList(WorkflowEngine.StepParam("drawingTypes"));
            if (requested.Count > 0)
            {
                types = HeadlessProductionInputs.SelectTypes(types, requested, new string[0], out var unknown);
                if (unknown.Count > 0)
                { message = "Produce From Scope Boxes: params.drawingTypes names type(s) no STING:: box is bound to: " + string.Join(", ", unknown) + "."; return Result.Failed; }
            }
            else
            {
                // No types named: an MEP preset produces the MEP boxes only. The A / S
                // STING:: boxes in the same model are another team's drawings.
                var mep = new HashSet<string>(HeadlessProductionInputs.MepDisciplines, StringComparer.OrdinalIgnoreCase);
                var other = types.Where(t => !mep.Contains((t.Discipline ?? "").Trim())).Select(t => t.Id).ToList();
                types = types.Where(t => mep.Contains((t.Discipline ?? "").Trim())).ToList();
                if (other.Count > 0)
                    StingLog.Info("Produce From Scope Boxes: not an M/E/P/FP/MG type, not produced (name it in params.drawingTypes to include it): "
                                  + string.Join(", ", other));
                if (types.Count == 0 && other.Count > 0)
                {
                    message = $"Produce From Scope Boxes: the STING:: boxes are bound only to non-MEP drawing types ({string.Join(", ", other)}); "
                            + "nothing to produce for an MEP set. Name them in params.drawingTypes to produce them.";
                    return Result.Cancelled;
                }
            }
            if (types.Count == 0)
            { message = "Produce From Scope Boxes: the STING:: boxes name no drawing type that is in the catalogue."; return Result.Failed; }

            int views = 0, sheets = 0; var warnings = new List<string>();
            DrawingTypePresentation.Prewarm(doc);
            using (DrawingProducer.PrimeBatchScope(doc))
                Produce(doc, scopes, bindingByName, types, levels, opts, packageId, ref views, ref sheets, warnings);
            message = BatchProduceCommons.StepSummary("Produce From Scope Boxes", views, sheets, warnings);
            if (views == 0) { message += " Nothing was produced."; return Result.Failed; }
            return Result.Succeeded;
        }

        /// <summary>One transaction per box — shared by the dialog and the workflow.</summary>
        private static void Produce(Document doc, List<Element> scopes, Dictionary<string, ScopeBoxBinding> bindingByName,
            List<DrawingType> types, List<Level> levels, ProduceOptions opts, string packageId,
            ref int views, ref int sheets, List<string> warnings)
        {
            // DTW-40: a box's level segment is read as the planner names levels — the unique
            // level code first (ScopeBoxRevit.LevelCodes, "L01"), then the name, then the
            // name without spaces — so "Level 1", which the box grammar cannot spell, is
            // addressable. It was matched against Level.Name only.
            var codes = ScopeBoxRevit.LevelCodes(doc);
            var levelRefs = levels.Select(l => new LevelRef
            {
                Id = l.Id.Value, Name = l.Name,
                Code = codes.TryGetValue(l.Id.Value, out var c) ? c : null,
            }).ToList();
            using (var tg = new TransactionGroup(doc, "STING Produce From Scope Boxes"))
            {
                tg.Start();
                foreach (var scope in scopes)
                {
                    if (!bindingByName.TryGetValue(scope.Name ?? "", out var bnd)) continue;
                    var dt = types.FirstOrDefault(t => string.Equals(t.Id, bnd.DrawingTypeId, StringComparison.OrdinalIgnoreCase));
                    if (dt == null) continue;
                    Level lvl = null;
                    if (!string.IsNullOrWhiteSpace(bnd.LevelCode))
                    {
                        var lid = LevelSegmentResolver.Resolve(bnd.LevelCode, levelRefs, out var how);
                        lvl = lid.HasValue ? levels.FirstOrDefault(l => l.Id.Value == lid.Value) : null;
                        if (lvl == null)
                            warnings.Add($"{scope.Name}: level '{bnd.LevelCode}' — {how}; produced without a level.");
                    }

                    // Dependent views need the level's primary plan to hang from. A box
                    // whose level code names no level is produced as an independent view
                    // (DependentViewPlanner.UsesDependents) — say so rather than let the
                    // option be ignored silently.
                    if (lvl == null && opts?.DuplicateOption == ViewDuplicateOption.AsDependent
                        && (string.Equals(dt.Purpose, "Plan", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(dt.Purpose, "RCP", StringComparison.OrdinalIgnoreCase)))
                    {
                        var w = $"{scope.Name}: level '{bnd.LevelCode}' matches no level in the model, so its view "
                              + "is produced as an independent view, not as a dependent of a level plan.";
                        StingLog.Warn("ProduceFromScopeBoxes " + w);
                        warnings.Add(w);
                    }

                    using (var t = new Transaction(doc, $"STING Scope {scope.Name}"))
                    {
                        t.Start();
                        try
                        {
                            var dctx = new DrawingContext { Level = lvl, ScopeBox = scope, Tag = bnd.Tag, PackageId = packageId };
                            // DTW-41: a view the retired DrawingTypes_FromScopeBoxes producer made
                            // for this box (stamped with the type, cropped to the box, no
                            // production context) is adopted, not duplicated.
                            bnd.ScopeBox = scope;
                            var legacyView = ScopeBoxBinder.FindExistingView(doc, bnd);
                            if (legacyView != null
                                && string.IsNullOrEmpty(ParameterHelpers.GetString(legacyView, ParamRegistry.STING_VIEW_CONTEXT_TAG)))
                            {
                                var firstRule = (dt.ProductionRules ?? new List<ProductionRule>()).OrderBy(r => r.Idx).FirstOrDefault()
                                             ?? new ProductionRule { Idx = 0 };
                                if (DrawingProducer.AdoptView(doc, dt, dctx, firstRule, legacyView))
                                    warnings.Add($"{scope.Name}: '{legacyView.Name}' (made by the old Generate from Scope Boxes) was adopted, not duplicated.");
                            }
                            var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                            warnings.AddRange(pr.Warnings);
                            var status = t.Commit();
                            if (status == TransactionStatus.Committed)
                            {
                                views += pr.ViewIds.Count;
                                if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) sheets++;   // P-9: reuse is not production
                            }
                            else
                            {
                                var w = $"{scope.Name} ({dt.Id}): the transaction did not commit ({status}); "
                                      + $"{pr.ViewIds.Count} view(s) were not kept.";
                                StingLog.Warn("ProduceFromScopeBoxes " + w);
                                warnings.Add(w);
                            }
                        }
                        catch (Exception innerEx)
                        {
                            StingLog.Warn($"ProduceFromScopeBoxes box={scope.Name}: {innerEx.Message}");
                            warnings.Add($"{scope.Name}: {innerEx.Message} — rolled back.");
                            t.RollBack();
                        }
                    }
                }
                tg.Assimilate();
            }
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches before per-room loop.
                DrawingTypePresentation.Prewarm(doc);

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
                // GAP-L: primed only once the dialog is confirmed (it was primed before the
                // dialog and never reset, so a cancelled run left the caches, and a
                // confirmed one kept them past the command); reset in finally.
                DrawingProducer.PrimeBatchCaches(doc);

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
                                int tv = 0, ts = 0;
                                foreach (var dt in pickedTypes)
                                {
                                    var dctx = new DrawingContext { Room = room, Tag = roomLabel, PackageId = res.Preset?.PackageId };
                                    var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                                    tv += pr.ViewIds.Count;
                                    if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) ts++;   // P-9: reuse is not production
                                    warnings.AddRange(pr.Warnings);
                                }
                                var status = t.Commit();
                                if (status == TransactionStatus.Committed) { views += tv; sheets += ts; }
                                else warnings.Add($"{roomLabel}: the transaction did not commit ({status}); {tv} view(s) were not kept.");
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"ProduceInteriorElevations room={roomLabel}: {innerEx.Message}");
                                warnings.Add($"{roomLabel}: {innerEx.Message} — rolled back.");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce Interior Elevations", views, sheets, warnings, res.Preset);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceInteriorElevations", ex); return Result.Failed; }
            finally { DrawingProducer.ResetBatchCaches(); }
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches.
                DrawingTypePresentation.Prewarm(doc);

                var types = BatchProduceCommons.AllTypesByPurpose(doc, "Section");
                // DTW-23/24: sections are cut along grid lines — the contexts are the grids,
                // and the ticked ones are the ones produced. "Manual selection" (which
                // returned "requires picking") and "Per room" (which produced nothing) are gone.
                var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                    .Where(g => g.Curve is Line).OrderBy(g => g.Name).ToList();
                if (grids.Count == 0)
                {
                    TaskDialog.Show("STING", "Produce Sections cuts one section along each straight grid line, and the model has none.");
                    return Result.Succeeded;
                }
                var labels = grids.Select(g => "Grid " + g.Name).ToList();

                var dlg = new DrawingProductionConfigDialog(types, labels, "Sections", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;
                // GAP-L: primed only once the dialog is confirmed (it was primed before the
                // dialog and never reset, so a cancelled run left the caches, and a
                // confirmed one kept them past the command); reset in finally.
                DrawingProducer.PrimeBatchCaches(doc);

                var preset = res.Preset;
                var sec = preset?.SectionConfig ?? new SectionProductionConfig();
                var opts = BatchProduceCommons.BuildOptions(preset);
                int views = 0, sheets = 0; var warnings = new List<string>();
                var pickedTypes = BatchProduceCommons.ResolveSelectedTypes(doc, res.SelectedDrawingTypeIds);

                var ticked = new HashSet<string>(res.SelectedContexts ?? new List<string>(), StringComparer.Ordinal);
                var pickedGrids = grids.Where(g => ticked.Contains("Grid " + g.Name)).ToList();
                if (pickedGrids.Count == 0)
                {
                    TaskDialog.Show("STING", "No grid line is ticked — nothing to cut. Tick the grids to section along.");
                    return Result.Succeeded;
                }
                if (pickedTypes.Count == 0)
                {
                    TaskDialog.Show("STING", "No section drawing type is ticked — nothing to produce.");
                    return Result.Succeeded;
                }
                double depthMm = sec.DepthMm > 0 ? sec.DepthMm : 10000;

                IEnumerable<DrawingContext> contextsToProduce;
                {
                    contextsToProduce = pickedGrids.Select(g =>
                    {
                        try
                        {
                            var c = g.Curve as Line;
                            if (c == null) return null;
                            var origin = (c.GetEndPoint(0) + c.GetEndPoint(1)) * 0.5;
                            var len = (c.GetEndPoint(1) - c.GetEndPoint(0)).GetLength();

                            // P-5: one shared frame builder with the producer.
                            // This used to offset Min/Max by a perpendicular
                            // vector in MODEL space with an implicit identity
                            // transform — so height landed on the wrong axis
                            // (the producer put it on Y, this on Z), and for a
                            // north-south grid the perpendicular's negative
                            // components made Min > Max, which CreateSection
                            // rejects outright. The section now cuts ALONG the
                            // grid and looks perpendicular to it, which is what
                            // "section along grid line" means.
                            var bb = DrawingProducer.BuildSectionBox(
                                origin:       origin,
                                cutDirection: c.Direction,
                                halfWidthFt:  len * 0.5 + 5.0 / 0.3048,
                                bottomZ:      origin.Z - 3.0 / 0.3048,
                                topZ:         origin.Z + 30.0 / 0.3048,
                                depthFt:      depthMm / 304.8);

                            // Context tag "Grid-<name>" — the one the Setup Wizard uses too, so
                            // either reuses the other's section.
                            return new DrawingContext { CustomBounds = bb, Tag = "Grid-" + g.Name, PackageId = preset?.PackageId };
                        }
                        catch (Exception ex) { warnings.Add($"Grid {g.Name}: no section frame — {ex.Message}"); return null; }
                    }).Where(x => x != null).ToList();
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
                                int tv = 0, ts = 0;
                                foreach (var dt in pickedTypes)
                                {
                                    var pr = DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                                    tv += pr.ViewIds.Count;
                                    if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) ts++;   // P-9: reuse is not production
                                    warnings.AddRange(pr.Warnings);
                                }
                                var status = t.Commit();
                                if (status == TransactionStatus.Committed) { views += tv; sheets += ts; }
                                else warnings.Add($"{dctx.Tag}: the transaction did not commit ({status}); {tv} view(s) were not kept.");
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"ProduceSections context={dctx.Tag}: {innerEx.Message}");
                                warnings.Add($"{dctx.Tag}: {innerEx.Message} — rolled back.");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce Sections", views, sheets, warnings, res.Preset);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceSections", ex); return Result.Failed; }
            finally { DrawingProducer.ResetBatchCaches(); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProduceExteriorElevationsCommand : IExternalCommand
    {
        /// <summary>
        /// DTW-27: exterior elevations are produced through DrawingProducer, so they are
        /// idempotent (a re-run reuses each face's view — no new markers, no failing
        /// rename), get sheets when "Create sheets" is ticked, and are presented and
        /// stamped like every other produced drawing.
        ///
        /// Context tags: "Exterior-&lt;Face&gt;" (one sheet per face, rule 0), or
        /// "Exterior" with rules 0-3 when the four go on one 1+4 sheet. Stamps are
        /// "::::Exterior-North" / "::::Exterior". A view stamped with the older raw tag
        /// "exterior::face::&lt;Face&gt;" (an earlier build, or the Setup Wizard) is adopted
        /// and re-stamped, not duplicated.
        ///
        /// The markers are hosted on a floor plan of the ticked level nearest ground, and
        /// each face keeps the marker index that actually looks at the building.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }

                // PERF-01: pre-warm view-template + pack caches.
                DrawingTypePresentation.Prewarm(doc);

                var types = BatchProduceCommons.AllTypesByPurpose(doc, "Elevation")
                    .Where(t => !(t.Name ?? "").ToLowerInvariant().Contains("interior")).ToList();
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (levels.Count == 0) { TaskDialog.Show("STING", "The model has no levels to host elevation markers on."); return Result.Succeeded; }

                var dlg = new DrawingProductionConfigDialog(types, levels.Select(l => l.Name).ToList(), "ExteriorElevations", doc);
                var res = dlg.ShowAndWait();
                if (res == null || !res.Confirmed) return Result.Succeeded;
                // GAP-L: primed only once the dialog is confirmed; reset in finally.
                DrawingProducer.PrimeBatchCaches(doc);

                var elev = res.Preset?.ElevationConfig ?? new ElevationProductionConfig();
                var opts = BatchProduceCommons.BuildOptions(res.Preset);
                int views = 0, sheets = 0; var warnings = new List<string>();
                var pickedTypes = BatchProduceCommons.ResolveSelectedTypes(doc, res.SelectedDrawingTypeIds);

                // The markers' host: the ticked level nearest ground.
                var ticked = levels.Where(l => res.SelectedContexts.Contains(l.Name)).ToList();
                var host = ticked.OrderBy(l => Math.Abs(l.Elevation)).ThenBy(l => l.Elevation).FirstOrDefault();
                if (host == null) { TaskDialog.Show("STING", "Tick the level whose plan should host the elevation markers."); return Result.Succeeded; }
                if (ticked.Count > 1)
                    warnings.Add($"Exterior elevations are made once, not per level: the markers are hosted on {host.Name} (the ticked level nearest ground).");

                // Footprint from the walls.
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var w in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
                {
                    var wbb = w.get_BoundingBox(null);
                    if (wbb == null) continue;
                    minX = Math.Min(minX, wbb.Min.X); minY = Math.Min(minY, wbb.Min.Y);
                    maxX = Math.Max(maxX, wbb.Max.X); maxY = Math.Max(maxY, wbb.Max.Y);
                }
                if (minX > maxX) { TaskDialog.Show("STING", "No walls in project — cannot derive building footprint."); return Result.Succeeded; }
                double offFt = elev.OffsetMm / 304.8;

                var faces = new List<(string Face, ElevationStation Station)>();
                foreach (var face in elev.FacesTo ?? new List<string>())
                {
                    var st = ElevationFaces.ExteriorStation(face, minX, minY, maxX, maxY, offFt);
                    if (st == null) { warnings.Add($"'{face}' is not North, East, South or West — skipped."); continue; }
                    faces.Add((face, new ElevationStation { X = st.Value.X, Y = st.Value.Y, LookX = st.Value.LookX, LookY = st.Value.LookY }));
                }
                if (faces.Count == 0) { TaskDialog.Show("STING", "No face ticked — nothing to produce."); return Result.Succeeded; }

                // Views stamped by an earlier build or by the Setup Wizard, by their raw tag.
                var legacy = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && v.ViewType == ViewType.Elevation)
                    .Select(v => (View: v, Type: DrawingTypeStamper.Read(v),
                                  Tag: ParameterHelpers.GetString(v, ParamRegistry.STING_VIEW_CONTEXT_TAG) ?? ""))
                    .Where(x => x.Tag.StartsWith("exterior::face::", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                using (var tg = new TransactionGroup(doc, "STING Exterior Elevations"))
                {
                    tg.Start();
                    foreach (var dt in pickedTypes)
                    {
                        var elevSlots = Enumerable.Range(0, dt.Slots?.Count ?? 0)
                            .Where(i => string.Equals(dt.Slots[i]?.ViewType, "Elevation", StringComparison.OrdinalIgnoreCase)).ToList();
                        bool onePlusFour = elev.UseOneFourViewSheet && opts.CreateSheet;
                        if (onePlusFour && elevSlots.Count < faces.Count)
                        {
                            warnings.Add($"{dt.Id} lays out {elevSlots.Count} elevation slot(s), not {faces.Count}: each face gets its own sheet.");
                            onePlusFour = false;
                        }

                        // One production call per sheet: all faces on one sheet, or one per face.
                        var jobs = new List<(string Tag, List<(string Face, ElevationStation St, int RuleIdx, int Slot)> Faces)>();
                        if (onePlusFour)
                            jobs.Add(("Exterior", faces.Select((f, i) => (f.Face, f.Station, i, elevSlots[i])).ToList()));
                        else
                            foreach (var f in faces)
                                jobs.Add(($"Exterior-{f.Face}", new List<(string, ElevationStation, int, int)> { (f.Face, f.Station, 0, elevSlots.Count > 0 ? elevSlots[0] : 0) }));

                        foreach (var job in jobs)
                        {
                            var ctx = new DrawingContext
                            {
                                Tag = job.Tag, PackageId = res.Preset?.PackageId, OwnerLevel = host,
                                RulesOverride = job.Faces.Select(f => new ProductionRule
                                {
                                    Idx = f.RuleIdx, ViewType = "Elevation", SlotIndex = f.Slot, Required = true,
                                    NameSuffix = onePlusFour ? $" - {f.Face}" : null,
                                }).ToList(),
                                ElevationStations = job.Faces.ToDictionary(f => f.RuleIdx, f => f.St),
                            };
                            using (var t = new Transaction(doc, $"STING Exterior Elev {job.Tag} {dt.Id}"))
                            {
                                t.Start();
                                try
                                {
                                    foreach (var f in job.Faces)
                                    {
                                        var old = legacy.FirstOrDefault(x => string.Equals(x.Type, dt.Id, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(x.Tag, $"exterior::face::{f.Face}", StringComparison.OrdinalIgnoreCase));
                                        if (old.View != null && DrawingProducer.AdoptView(doc, dt, ctx, ctx.RulesOverride.First(r => r.Idx == f.RuleIdx), old.View))
                                            warnings.Add($"'{old.View.Name}' (stamped exterior::face::{f.Face}) was adopted as {dt.Id} {job.Tag}, not duplicated.");
                                    }
                                    var pr = DrawingProducer.ProduceAllViews(doc, dt, ctx, opts);
                                    foreach (var vid in pr.ViewIds)
                                    {
                                        try
                                        {
                                            var fp = doc.GetElement(vid)?.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                                            if (fp != null && !fp.IsReadOnly) fp.Set(elev.FarClipMm / 304.8);
                                        }
                                        catch (Exception ex) { warnings.Add($"{job.Tag}: far clip {elev.FarClipMm} mm not set — {ex.Message}"); }
                                    }
                                    warnings.AddRange(pr.Warnings);
                                    var status = t.Commit();
                                    if (status == TransactionStatus.Committed)
                                    {
                                        views += pr.ViewIds.Count;
                                        if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) sheets++;
                                    }
                                    else warnings.Add($"{job.Tag} ({dt.Id}): the transaction did not commit ({status}); {pr.ViewIds.Count} view(s) were not kept.");
                                }
                                catch (Exception innerEx)
                                {
                                    StingLog.Warn($"ProduceExteriorElevations {job.Tag}: {innerEx.Message}");
                                    warnings.Add($"{job.Tag} ({dt.Id}): {innerEx.Message} — rolled back.");
                                    if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                                }
                            }
                        }
                    }
                    tg.Assimilate();
                }
                BatchProduceCommons.ShowResult("Produce Exterior Elevations", views, sheets, warnings, res.Preset);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("ProduceExteriorElevations", ex); return Result.Failed; }
            finally { DrawingProducer.ResetBatchCaches(); }
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
                var packs = ViewStylePackRegistry.GetLibrary(doc).Packs.Where(p => p.IsManaged).ToList();
                if (packs.Count == 0)
                {
                    // Nothing was regenerated: Cancelled (a SKIP in a workflow report), not
                    // a success that did nothing.
                    PresetDialog.Show("STING", "No managed view-style packs found. Switch a pack to managed mode in the Drawing Type Editor first.", ref message);
                    if (PresetDialog.Quiet) message = "Regenerate Pack Templates: no managed view-style packs in this project; nothing to regenerate.";
                    return Result.Cancelled;
                }

                // Inside a preset every managed pack is regenerated — there is nobody to pick.
                List<ViewStylePack> chosen;
                if (PresetDialog.Quiet) chosen = packs;
                else
                {
                    var pickItems = packs.Select(p => new StingListPicker.ListItem { Label = p.Name ?? p.Id, Tag = p }).ToList();
                    var pickResult = StingListPicker.Show("Regenerate Pack Templates", "Pick managed packs to regenerate", pickItems, allowMultiSelect: true);
                    if (pickResult == null || pickResult.Count == 0) return Result.Cancelled;
                    chosen = pickResult.Select(r => r.Tag as ViewStylePack).Where(p => p != null).ToList();
                }

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
                                int packUpdated = 0;
                                foreach (var vt in new[] { ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.Section, ViewType.Elevation, ViewType.Detail, ViewType.ThreeD })
                                {
                                    var pr = new PackApplyResult();
                                    var id = ManagedTemplateSyncer.EnsureTemplate(doc, pack, vt, pr);
                                    if (id != ElementId.InvalidElementId) packUpdated++;
                                    warnings.AddRange(pr.Warnings);
                                }
                                var status = t.Commit();
                                if (status == TransactionStatus.Committed) updated += packUpdated;
                                else warnings.Add($"{pack.Name}: the transaction did not commit ({status}); its templates were not regenerated.");
                            }
                            catch (Exception innerEx)
                            {
                                StingLog.Warn($"RegeneratePackTemplates pack={pack.Name}: {innerEx.Message}");
                                warnings.Add($"{pack.Name}: {innerEx.Message} — rolled back.");
                                t.RollBack();
                            }
                        }
                    }
                    tg.Assimilate();
                }
                if (PresetDialog.Quiet)
                {
                    foreach (var w in warnings.Distinct()) StingLog.Warn($"Regenerate Pack Templates: {w}");
                    message = $"Regenerate Pack Templates: {updated} template(s) across {chosen.Count} managed pack(s)"
                            + (warnings.Count > 0 ? $", {warnings.Distinct().Count()} warning(s) (see the STING log)." : ".");
                    if (updated == 0) { message += " No template was regenerated."; return Result.Failed; }
                    return Result.Succeeded;
                }
                BatchProduceCommons.ShowResult("Regenerate Pack Templates", updated, 0, warnings);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; StingLog.Error("RegeneratePackTemplates", ex); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class DrawingPackageExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
                var packages = DrawingPackageManager.GetPackages(doc);
                if (packages.Count == 0) { TaskDialog.Show("STING", "No drawing packages found."); return Result.Succeeded; }
                var label = StingListPicker.Show("Export Drawing Package", "Pick a package to export", packages.Select(p => $"{p.PackageId} ({p.SheetCount} sheets)").ToList());
                if (string.IsNullOrEmpty(label)) return Result.Succeeded;
                var pkgId = packages.First(p => $"{p.PackageId} ({p.SheetCount} sheets)" == label).PackageId;

                var outDir = OutputLocationHelper.GetRoutedDirectory(doc, "PDF");
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document; if (doc == null) { message = "No active document"; return Result.Failed; }
                
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
