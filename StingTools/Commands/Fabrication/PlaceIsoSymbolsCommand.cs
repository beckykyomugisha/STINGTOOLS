// StingTools v4 MVP — PlaceIsoSymbolsCommand.
//
// Standalone re-run of the ISO 6412 detail-symbol placer for an
// existing AssemblyInstance. Generate Fabrication Package wires the
// placer in automatically (gated on FabricationOptions.PlaceISO6412Symbols);
// this command is the manual entry point for the same pipeline so
// users can iterate symbol placement without regenerating the whole
// fabrication package — useful when the family library has just been
// updated, when symbols need to land on a different detail view than
// the auto-generated ISO 6412 one, or when the placement option was
// off at package time.
//
// Resolution order for (assembly, view):
//   1. Active view is the ISO 6412 view itself — its name was stamped
//      with the assembly id by AssemblyViewBuilder, so we parse the
//      assembly id back out of view.Name (`STING ISO 6412 - <name> ::<id>`).
//   2. Active view is some other section/plan/drafting view associated
//      with an assembly via View.AssociatedAssemblyInstanceId.
//   3. Selection contains AssemblyInstance(s) — for each, find its
//      stamped ISO 6412 section view by name suffix scan.
//   4. Otherwise — clear TaskDialog explaining what to select / open.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Core.Fabrication;
using StingTools.Core.Placement;

namespace StingTools.Commands.Fabrication
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceIsoSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var targets = ResolveTargets(doc, uidoc);
            if (targets.Count == 0)
            {
                TaskDialog.Show("STING v4 — ISO 6412 Symbols",
                    "Nothing to place against.\n\n" +
                    "Open a fabrication assembly's ISO 6412 detail view, OR select " +
                    "one or more AssemblyInstance elements, then re-run this command.\n\n" +
                    "If you haven't generated a package yet, run Generate Fabrication " +
                    "Package first — it creates the assembly + detail views automatically.");
                return Result.Cancelled;
            }

            // Resolve DrawingType context from the active view for discipline-aware filtering.
            var activeView = uidoc?.ActiveView;
            var (dtId, packId, discipline) = ResolveDrawingTypeContext(doc, activeView);
            string disciplineFilter = (string.IsNullOrEmpty(discipline) || discipline == "*")
                ? null : discipline;

            var result = new FabricationResult();
            foreach (var pair in targets)
            {
                try
                {
                    var view = doc.GetElement(pair.IsoViewId) as View;
                    if (view == null) continue;
                    int placed = IsoSymbolPlacer.PlaceSymbolsForAssembly(
                        doc, pair.AssyId, view, result, disciplineFilter);
                    result.SymbolsPlaced += placed;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"PlaceIsoSymbols {pair.AssyId}: {ex.Message}");
                    StingLog.Warn($"PlaceIsoSymbolsCommand: {ex.Message}");
                }
            }

            // Route result to PlacementResultBus so the Placement Centre + dock panel can display it.
            var summary = new PlacementRunSummary
            {
                Source        = "Symbols",
                DrawingTypeId = dtId,
                PackId        = packId,
                Headline      = result.SymbolsPlaced > 0
                    ? $"{result.SymbolsPlaced} symbol(s) placed across {targets.Count} assembly(ies)"
                    : "No symbols placed",
                Metrics = new List<string>
                {
                    $"Assemblies: {targets.Count}",
                    $"Symbols placed: {result.SymbolsPlaced}",
                    $"Replaced: {result.SymbolsReplaced}",
                    $"Unmatched: {result.UnmatchedMembers}",
                    disciplineFilter != null ? $"Discipline filter: {disciplineFilter}" : "Filter: none",
                },
                Warnings = new List<string>(result.Warnings ?? new List<string>()),
            };
            PlacementResultBus.Publish(summary);

            // Show rich dialog (falls back to TaskDialog if WPF fails).
            try
            {
                var dlg = new StingTools.UI.FabricationResultDialog(doc, result);
                dlg.ShowDialog();
            }
            catch
            {
                ShowResult(result, targets.Count); // existing TaskDialog fallback
            }

            return Result.Succeeded;
        }

        // ─── Target resolution ──────────────────────────────────────────

        private static List<(ElementId AssyId, ElementId IsoViewId)> ResolveTargets(
            Document doc, UIDocument uidoc)
        {
            var targets = new List<(ElementId, ElementId)>();

            // 1) Active view IS a STING ISO 6412 view (stamped by name).
            try
            {
                var av = doc.ActiveView;
                if (av != null && IsStingIsoView(av))
                {
                    var aid = ParseAssemblyIdFromIsoViewName(av.Name);
                    if (aid != null && aid != ElementId.InvalidElementId
                        && doc.GetElement(aid) is AssemblyInstance)
                    {
                        targets.Add((aid, av.Id));
                        return targets;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlaceIsoSymbols.IsoView: {ex.Message}"); }

            // 2) Active view associated with an assembly (older path —
            //    AssemblyViewUtils-created views like elevations).
            try
            {
                var av = doc.ActiveView;
                if (av is ViewSection || av is ViewPlan || av is ViewDrafting)
                {
                    var assyId = FindAssemblyForView(doc, av.Id);
                    if (assyId != null && assyId != ElementId.InvalidElementId)
                    {
                        targets.Add((assyId, av.Id));
                        return targets;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlaceIsoSymbols.ActiveView: {ex.Message}"); }

            // 3) Selection contains AssemblyInstance(s) — find each one's
            //    STING ISO 6412 view by name suffix `:: <id>`.
            try
            {
                var selIds = uidoc?.Selection?.GetElementIds();
                if (selIds != null)
                {
                    var nameIndex = BuildIsoViewNameIndex(doc);
                    foreach (var id in selIds)
                    {
                        var ai = doc.GetElement(id) as AssemblyInstance;
                        if (ai == null) continue;
                        if (nameIndex.TryGetValue(ai.Id.Value, out ElementId viewId))
                        {
                            targets.Add((ai.Id, viewId));
                            continue;
                        }
                        // Final fallback: AssemblyViewUtils-associated section.
                        var fallback = FindFirstSectionViewForAssembly(doc, ai);
                        if (fallback != null && fallback != ElementId.InvalidElementId)
                            targets.Add((ai.Id, fallback));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlaceIsoSymbols.Selection: {ex.Message}"); }

            return targets;
        }

        private static bool IsStingIsoView(View v)
        {
            try { return (v?.Name ?? "").StartsWith("STING ISO 6412", StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Parses the assembly element-id out of a STING ISO 6412 view
        /// name. Format: `STING ISO 6412 - {assyName} ::{id}`.
        /// </summary>
        private static ElementId ParseAssemblyIdFromIsoViewName(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return ElementId.InvalidElementId;
                int idx = name.LastIndexOf("::", StringComparison.Ordinal);
                if (idx < 0) return ElementId.InvalidElementId;
                string tail = name.Substring(idx + 2).Trim();
                if (long.TryParse(tail, out long val)) return new ElementId(val);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// One-pass scan of every ViewSection in the document whose name
        /// matches the STING ISO 6412 pattern. Returns map of
        /// assemblyId.Value → viewId.
        /// </summary>
        private static Dictionary<long, ElementId> BuildIsoViewNameIndex(Document doc)
        {
            var map = new Dictionary<long, ElementId>();
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(View));
                foreach (var el in col)
                {
                    if (!(el is View v) || v.IsTemplate) continue;
                    if (!IsStingIsoView(v)) continue;
                    var aid = ParseAssemblyIdFromIsoViewName(v.Name);
                    if (aid == null || aid == ElementId.InvalidElementId) continue;
                    map[aid.Value] = v.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildIsoViewNameIndex: {ex.Message}"); }
            return map;
        }

        /// <summary>
        /// Walks every AssemblyInstance in the document and returns the
        /// id of the one whose associated views contain <paramref name="viewId"/>.
        /// Returns InvalidElementId when no assembly owns the view.
        /// </summary>
        private static ElementId FindAssemblyForView(Document doc, ElementId viewId)
        {
            try
            {
                if (doc.GetElement(viewId) is View v
                    && v.AssociatedAssemblyInstanceId != ElementId.InvalidElementId)
                {
                    return v.AssociatedAssemblyInstanceId;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindAssemblyForView: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// Picks the first ViewSection associated with the assembly — that
        /// is the ISO 6412 axonometric created by AssemblyViewBuilder,
        /// since the builder only emits one Section view per assembly
        /// (elevations are CreateDetailSection but live as ViewSection too,
        /// which is fine — symbol placement works on any 2D detail view).
        /// </summary>
        private static ElementId FindFirstSectionViewForAssembly(Document doc, AssemblyInstance ai)
        {
            if (ai == null) return ElementId.InvalidElementId;
            try
            {
                // Revit has no AssemblyInstance.GetAssociatedAssemblyViews
                // helper; views own their owning-assembly via
                // View.AssociatedAssemblyInstanceId. Filter the view set by
                // that id and pick the first ViewSection — that's the
                // ISO 6412 axonometric AssemblyViewBuilder mints.
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(ViewSection)))
                {
                    if (el is ViewSection vs && vs.AssociatedAssemblyInstanceId == ai.Id)
                        return vs.Id;
                }
                // Fallback: any non-3D, non-schedule view bound to the
                // assembly. Catches projects where the user duplicated the
                // ISO into a Detail / DraftingView before triggering the
                // command.
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View v) || v.IsTemplate) continue;
                    if (v.AssociatedAssemblyInstanceId != ai.Id) continue;
                    if (v is View3D) continue;
                    if (v is ViewSchedule) continue;
                    return v.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindFirstSectionViewForAssembly: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        // ─── DrawingType context resolution ────────────────────────────

        private static (string dtId, string packId, string discipline) ResolveDrawingTypeContext(
            Document doc, View view)
        {
            if (view == null) return (null, null, null);
            try
            {
                var dtId = DrawingTypeStamper.Read(view);
                if (string.IsNullOrEmpty(dtId)) return (null, null, null);
                var dt = DrawingTypeRegistry.Get(doc, dtId);
                if (dt == null) return (dtId, null, null);
                string packId = null;
                if (!string.IsNullOrEmpty(dt.ViewStylePackId))
                {
                    var pack = DrawingTypeRegistry.TryGetPack(doc, dt.ViewStylePackId);
                    packId = pack?.Id;
                }
                return (dtId, packId, dt.Discipline);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlaceIsoSymbolsCommand.ResolveDrawingTypeContext: {ex.Message}");
                return (null, null, null);
            }
        }

        // ─── Result reporting ──────────────────────────────────────────

        private static void ShowResult(FabricationResult res, int assemblyCount)
        {
            foreach (var w in res.Warnings) StingLog.Warn($"PlaceIsoSymbols: {w}");

            var dlg = new TaskDialog("STING v4 — ISO 6412 Symbols")
            {
                MainInstruction = res.SymbolsPlaced > 0
                    ? $"{res.SymbolsPlaced} symbol(s) placed across {assemblyCount} assembly(ies)."
                    : "No symbols placed.",
                MainContent = BuildResultBody(res, assemblyCount),
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dlg.Show();
        }

        private static string BuildResultBody(FabricationResult res, int assemblyCount)
        {
            var lines = new List<string>();
            lines.Add($"Assemblies processed: {assemblyCount}");
            lines.Add($"Mode: {StingTools.Commands.Fabrication.FabricationOptions.SymbolPlacementMode}");
            lines.Add($"Symbols placed: {res.SymbolsPlaced}");
            if (res.SymbolsReplaced > 0) lines.Add($"Symbols replaced: {res.SymbolsReplaced}");
            if (res.UnmatchedMembers > 0)
            {
                lines.Add($"Members with no symbol mapping: {res.UnmatchedMembers}");
                foreach (var s in res.UnmatchedSamples.Take(5))
                    lines.Add("  • " + s);
                if (res.UnmatchedSamples.Count > 5)
                    lines.Add($"  (+{res.UnmatchedMembers - res.UnmatchedSamples.Count} more)");
            }
            if (res.MissingFamilies.Count > 0)
            {
                lines.Add("");
                lines.Add($"Missing families ({res.MissingFamilies.Count}):");
                foreach (var fam in res.MissingFamilies.OrderBy(f => f).Take(10))
                    lines.Add("  • " + fam);
                if (res.MissingFamilies.Count > 10)
                    lines.Add($"  (+{res.MissingFamilies.Count - 10} more — see StingTools.log)");
                lines.Add("");
                lines.Add("Drop the .rfa files into Families/ISO6412/ alongside the plugin and re-run.");
            }
            if (res.Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add($"{res.Warnings.Count} warning(s) — full detail in StingTools.log.");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
