using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Select
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Stale-flag actions — make the dashboard's "STALE" count actionable.
    //
    //  ComplianceScan.StaleCount (the number shown on the BIM Coordination Center
    //  and Document Management dashboards) counts elements whose persisted
    //  STING_STALE_BOOL == 1 — the flag set by the StingStaleMarker IUpdater when
    //  geometry / material / spatial context invalidates a tagged element.
    //
    //  These commands operate on that SAME persisted flag, so the set they select
    //  / highlight is exactly the set the dashboard counted. They are distinct
    //  from SelectStaleElementsCommand ("SelectStale"), which recomputes staleness
    //  live by comparing stored vs current LVL/SYS/PROD/FUNC tokens.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Shared logic for the STING_STALE_BOOL flag: collection, view-filter
    /// creation, and highlight overrides. Kept internal so the standalone commands
    /// and the dashboard-count chooser share one implementation.</summary>
    internal static class StaleFlagHelper
    {
        internal const string FilterName = "STING - Stale Elements";

        /// <summary>Collect every non-type element whose STING_STALE_BOOL == 1.
        /// A null <paramref name="viewScope"/> scans the whole project (matching
        /// the project-wide dashboard count); otherwise it is limited to the view.</summary>
        internal static List<ElementId> CollectFlagged(Document doc, View viewScope)
        {
            var ids = new List<ElementId>();
            var collector = (viewScope != null
                ? new FilteredElementCollector(doc, viewScope.Id)
                : new FilteredElementCollector(doc))
                .WhereElementIsNotElementType();

            // Mirror ComplianceScan's scan scope exactly (same AllCategoryEnums
            // multicategory filter) so the set we select/highlight equals the
            // dashboard's StaleCount by construction — and so we don't read the
            // flag on every element in the model.
            var catEnums = SharedParamGuids.AllCategoryEnums;
            if (catEnums != null && catEnums.Length > 0)
                collector = collector.WherePasses(
                    new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

            foreach (Element elem in collector)
            {
                if (elem?.Category == null) continue;
                if (ParameterHelpers.GetInt(elem, ParamRegistry.STALE, 0) == 1)
                    ids.Add(elem.Id);
            }
            return ids;
        }

        /// <summary>Resolve the SharedParameterElement id for STING_STALE_BOOL, or
        /// null when the parameter has never been bound in this project (nothing has
        /// been flagged stale yet). A view filter rule cannot be built without it.</summary>
        internal static ElementId ResolveStaleParamId(Document doc)
        {
            foreach (SharedParameterElement sp in new FilteredElementCollector(doc)
                .OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
            {
                if (string.Equals(sp.Name, ParamRegistry.STALE, StringComparison.OrdinalIgnoreCase))
                    return sp.Id;
            }
            return null;
        }

        /// <summary>Intersection of STING's taggable categories with the categories
        /// Revit actually allows in a view filter — the same guard AecFilterFactory uses.</summary>
        internal static IList<ElementId> BuildFilterableCategoryIds(Document doc)
        {
            var filterable = ParameterFilterUtilities.GetAllFilterableCategories();
            var ids = new List<ElementId>();
            foreach (var bic in SharedParamGuids.AllCategoryEnums)
            {
                try
                {
                    var id = new ElementId(bic);
                    if (filterable.Contains(id)) ids.Add(id);
                }
                catch (Exception ex) { StingLog.Warn($"StaleFilter category {bic}: {ex.Message}"); }
            }
            return ids;
        }

        /// <summary>Find the existing "STING - Stale Elements" filter or create it
        /// (STING_STALE_BOOL == 1). Must be called inside an open transaction.
        /// Returns InvalidElementId when the stale parameter isn't bound.</summary>
        internal static ElementId FindOrCreateFilter(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, FilterName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;

            ElementId paramId = ResolveStaleParamId(doc);
            if (paramId == null) return ElementId.InvalidElementId;

            var catIds = BuildFilterableCategoryIds(doc);
            if (catIds.Count == 0) return ElementId.InvalidElementId;

            FilterRule rule = ParameterFilterRuleFactory.CreateEqualsRule(paramId, 1);
            var epf = new ElementParameterFilter(rule);
            var pfe = ParameterFilterElement.Create(doc, FilterName, catIds, epf);
            return pfe.Id;
        }

        /// <summary>Apply the red highlight override for the stale filter to a view.
        /// Must be called inside an open transaction.</summary>
        internal static void ApplyHighlight(Document doc, View view, ElementId filterId)
        {
            if (!view.GetFilters().Contains(filterId))
                view.AddFilter(filterId);

            var ogs = view.GetFilterOverrides(filterId) ?? new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(200, 0, 0));
            ogs.SetProjectionLineWeight(6);

            var solid = ParameterHelpers.GetSolidFillPattern(doc);
            if (solid != null)
            {
                var fill = new Color(255, 120, 120);
                ogs.SetSurfaceForegroundPatternId(solid.Id);
                try { ogs.SetSurfaceForegroundPatternVisible(true); } catch { }
                ogs.SetSurfaceForegroundPatternColor(fill);
                ogs.SetCutForegroundPatternId(solid.Id);
                try { ogs.SetCutForegroundPatternVisible(true); } catch { }
                ogs.SetCutForegroundPatternColor(fill);
            }
            ogs.SetSurfaceTransparency(30);

            view.SetFilterOverrides(filterId, ogs);
            view.SetFilterVisibility(filterId, true);
        }
    }

    /// <summary>Select every element the dashboard counts as stale
    /// (STING_STALE_BOOL == 1). Honours the project/view scope toggle like the
    /// other Select commands.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectStaleFlaggedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Select Stale (flagged)", "No active view."); return Result.Failed; }

            View scope = SelectionScopeHelper.IsProjectScope ? null : ctx.ActiveView;
            var ids = StaleFlagHelper.CollectFlagged(ctx.Doc, scope);
            string scopeLabel = SelectionScopeHelper.IsProjectScope ? "project" : "active view";

            if (ids.Count == 0)
            {
                TaskDialog.Show("Select Stale (flagged)",
                    $"No flagged-stale elements found ({scopeLabel}).\n\n" +
                    "The stale marker flags STING_STALE_BOOL when a tagged element's geometry, " +
                    "material or spatial context changes. Enable it from the Auto-Tagger config if you " +
                    "expected results.");
                return Result.Succeeded;
            }

            ctx.UIDoc.Selection.SetElementIds(ids);
            TaskDialog.Show("Select Stale (flagged)",
                $"Selected {ids.Count} flagged-stale element(s) ({scopeLabel}).\n\n" +
                "Use Re-Tag / Auto Tag (overwrite) to update them, then clear the stale flag.");
            StingLog.Info($"SelectStaleFlagged: {ids.Count} elements ({scopeLabel}).");
            return Result.Succeeded;
        }
    }

    /// <summary>Highlight flagged-stale elements in the active view with a red
    /// parameter filter override (STING_STALE_BOOL == 1).</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HighlightStaleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Highlight Stale", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            if (!view.AreGraphicsOverridesAllowed())
            {
                TaskDialog.Show("Highlight Stale",
                    "This view does not allow graphic overrides (it may be controlled by a view template " +
                    "or be a schedule/legend). Switch to a plan/3D view, or clear the template's filter control.");
                return Result.Cancelled;
            }

            try
            {
                using (var t = new Transaction(doc, "STING Highlight Stale"))
                {
                    t.Start();
                    ElementId filterId = StaleFlagHelper.FindOrCreateFilter(doc);
                    if (filterId == ElementId.InvalidElementId)
                    {
                        t.RollBack();
                        TaskDialog.Show("Highlight Stale",
                            "The STING_STALE_BOOL parameter is not bound in this project yet, so no filter " +
                            "could be built. Enable the stale marker (Auto-Tagger config) and modify a tagged " +
                            "element first, or load the STING shared parameters.");
                        return Result.Cancelled;
                    }
                    StaleFlagHelper.ApplyHighlight(doc, view, filterId);
                    t.Commit();
                }

                int count = StaleFlagHelper.CollectFlagged(doc, view).Count;
                TaskDialog.Show("Highlight Stale",
                    $"Applied the red stale filter to '{view.Name}'.\n\n" +
                    $"{count} flagged-stale element(s) are highlighted in this view. " +
                    "Use 'Clear Stale Highlight' to remove it.");
                StingLog.Info($"HighlightStale: applied to view '{view.Name}', {count} elements flagged.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HighlightStaleCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Remove the stale highlight filter from the active view (leaves the
    /// reusable filter definition in the project).</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearStaleHighlightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Clear Stale Highlight", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            var filter = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, StaleFlagHelper.FilterName, StringComparison.OrdinalIgnoreCase));

            if (filter == null || !view.GetFilters().Contains(filter.Id))
            {
                TaskDialog.Show("Clear Stale Highlight", "No stale highlight is applied to this view.");
                return Result.Succeeded;
            }

            try
            {
                using (var t = new Transaction(doc, "STING Clear Stale Highlight"))
                {
                    t.Start();
                    view.RemoveFilter(filter.Id);
                    t.Commit();
                }
                TaskDialog.Show("Clear Stale Highlight", $"Removed the stale highlight from '{view.Name}'.");
                StingLog.Info($"ClearStaleHighlight: removed from view '{view.Name}'.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClearStaleHighlightCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Entry point for clicking the dashboard's STALE count: offers Select
    /// (project-wide, matching the count), Highlight (active view) or Re-tag. Keeps
    /// the count itself actionable without hijacking the dedicated Retag buttons.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StaleCountActionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx?.ActiveView == null) { TaskDialog.Show("Stale Elements", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Project-wide count to match the dashboard metric.
            int count = StaleFlagHelper.CollectFlagged(doc, null).Count;

            var td = new TaskDialog("Stale Elements")
            {
                MainInstruction = count == 0
                    ? "No elements are flagged stale (STING_STALE_BOOL)."
                    : $"{count} element(s) flagged stale (STING_STALE_BOOL).",
                MainContent = "These are the elements the dashboard counts as stale. What would you like to do?",
                AllowCancellation = true
            };

            if (count == 0)
            {
                td.CommonButtons = TaskDialogCommonButtons.Close;
                td.Show();
                return Result.Succeeded;
            }

            td.TitleAutoPrefix = false;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select them",
                "Select all flagged-stale elements across the project.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Highlight in this view",
                "Apply a red view filter so they stand out on the drawing.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Re-tag them",
                "Re-derive tags to bring them back into agreement with the model.");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult res = td.Show();
            switch (res)
            {
                case TaskDialogResult.CommandLink1:
                    var ids = StaleFlagHelper.CollectFlagged(doc, null);
                    ctx.UIDoc.Selection.SetElementIds(ids);
                    StingLog.Info($"StaleCountAction: selected {ids.Count} flagged-stale elements.");
                    return Result.Succeeded;

                case TaskDialogResult.CommandLink2:
                    return new HighlightStaleCommand().Execute(cmd, ref msg, el);

                case TaskDialogResult.CommandLink3:
                    return new StingTools.Organise.RetagStaleCommand().Execute(cmd, ref msg, el);

                default:
                    return Result.Cancelled;
            }
        }
    }
}
