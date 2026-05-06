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
//   1. Active view is a ViewSection and is associated with an
//      AssemblyInstance — place on the active view (1 assembly).
//   2. Selection contains AssemblyInstance(s) — for each, find the
//      first associated ViewSection (the ISO 6412 axonometric created
//      by AssemblyViewBuilder is a Section view).
//   3. Otherwise — clear TaskDialog explaining what to select / open.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;

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

            var result = new FabricationResult();
            foreach (var pair in targets)
            {
                try
                {
                    var view = doc.GetElement(pair.IsoViewId) as View;
                    if (view == null) continue;
                    int placed = IsoSymbolPlacer.PlaceSymbolsForAssembly(
                        doc, pair.AssyId, view, result);
                    result.SymbolsPlaced += placed;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"PlaceIsoSymbols {pair.AssyId}: {ex.Message}");
                    StingLog.Warn($"PlaceIsoSymbolsCommand: {ex.Message}");
                }
            }

            ShowResult(result, targets.Count);
            return Result.Succeeded;
        }

        // ─── Target resolution ──────────────────────────────────────────

        private static List<(ElementId AssyId, ElementId IsoViewId)> ResolveTargets(
            Document doc, UIDocument uidoc)
        {
            var targets = new List<(ElementId, ElementId)>();

            // 1) Active view is a section/detail tied to an assembly.
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

            // 2) Selection contains AssemblyInstance(s).
            try
            {
                var selIds = uidoc?.Selection?.GetElementIds();
                if (selIds != null)
                {
                    foreach (var id in selIds)
                    {
                        var ai = doc.GetElement(id) as AssemblyInstance;
                        if (ai == null) continue;
                        var viewId = FindFirstSectionViewForAssembly(doc, ai);
                        if (viewId == null || viewId == ElementId.InvalidElementId) continue;
                        targets.Add((ai.Id, viewId));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlaceIsoSymbols.Selection: {ex.Message}"); }

            return targets;
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
                var col = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance));
                foreach (var el in col)
                {
                    if (!(el is AssemblyInstance ai)) continue;
                    var views = ai.GetAssociatedAssemblyViews();
                    if (views != null && views.Contains(viewId)) return ai.Id;
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
            try
            {
                var views = ai.GetAssociatedAssemblyViews();
                if (views == null) return ElementId.InvalidElementId;
                foreach (var vid in views)
                {
                    if (doc.GetElement(vid) is ViewSection) return vid;
                }
                // Fallback: any non-3D, non-schedule view.
                foreach (var vid in views)
                {
                    var v = doc.GetElement(vid) as View;
                    if (v == null) continue;
                    if (v is View3D) continue;
                    if (v is ViewSchedule) continue;
                    return vid;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindFirstSectionViewForAssembly: {ex.Message}"); }
            return ElementId.InvalidElementId;
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
            lines.Add($"Symbols placed: {res.SymbolsPlaced}");
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
