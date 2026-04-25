// StingTools v4 MVP — GenerateFabPackageCommand.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    /// <summary>
    /// Static option surface for Generate Fabrication Package, populated
    /// by the TAGS → Fabrication sub-tab before Execute runs.
    /// Mirrors the 16 CheckBox / 2 RadioButton controls documented in
    /// StingDockPanel.xaml under the Fabrication sub-tab.
    /// </summary>
    public static class FabricationOptions
    {
        // Scope radio buttons
        public static bool ScopeSelection { get; set; } = true;
        public static bool ScopeActiveView{ get; set; } = false;
        public static bool ScopeProject   { get; set; } = false;

        // Discipline rule toggles
        public static bool RulePipe       { get; set; } = true;
        public static bool RulePipeLB     { get; set; } = false;
        public static bool RuleDuct       { get; set; } = true;
        public static bool RuleDuctPitt   { get; set; } = false;
        public static bool RuleConduit    { get; set; } = true;

        // Output toggles
        public static bool GenerateAssemblies  { get; set; } = true;
        public static bool GenerateViews       { get; set; } = true;
        public static bool GenerateSheets      { get; set; } = true;
        public static bool PlaceISO6412Symbols { get; set; } = true;
        public static bool EmitPerDisciplineCsv{ get; set; } = true;

        // Content mode — ISO 6412 (workshop) vs Generic (geometry only)
        public static bool ContentModeIso6412  { get; set; } = true;

        /// <summary>
        /// Shop-drawing composition options captured from the
        /// ShopDrawingOptionsDialog. Null = use engine defaults
        /// (per-discipline STING_TB_ASSEMBLY_*, no view template,
        /// SP-{disc}-{sys}-{lvl}-{seq} sheet numbering).
        /// </summary>
        public static StingTools.UI.ShopDrawingOptions ShopDrawing { get; set; }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateFabPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Scope resolution — Fabrication tab radio buttons drive which
            // MEP elements feed the engine: Selection / Active view /
            // Project. CollectScopeWithFallback honours the chosen scope
            // first, then falls back through Selection → Active view →
            // Project so a click on Generate package never silently fails
            // when the user simply forgot to pre-select.
            string scopeLabelUsed;
            var ids = CollectScopeWithFallback(doc, uidoc, out scopeLabelUsed);
            if (ids == null || ids.Count == 0)
            {
                TaskDialog.Show("STING v4 — Generate Fabrication Package",
                    "No MEP elements found in selection, active view, or the project.\n\n" +
                    "FabricationEngine will:\n" +
                    "  1. Group elements per discipline rules (STING_FAB_RULES.json)\n" +
                    "  2. Create AssemblyInstances with SP-{DISC}-{SYS}-{LVL}-{SEQ} naming\n" +
                    "  3. Generate 5 views per assembly + BOM schedule\n" +
                    "  4. Lay out shop drawing sheets with title block populated\n" +
                    "  5. Emit per-discipline CSV sidecars (bend / weld / seam)\n\n" +
                    "Open a view containing pipes / ducts / conduits and try again.");
                return Result.Cancelled;
            }
            StingLog.Info($"GenerateFabPackage: collected {ids.Count} element(s) via {scopeLabelUsed} scope.");

            // Shop-drawing composition is now configured up front via the
            // Fabrication Workspace (Configure… button on the Title Block
            // strip) or the dock panel's Configure… button — both
            // populate FabricationOptions.ShopDrawing for the Revit
            // session. When ShopDrawing is null the engine falls back to
            // per-discipline STING_TB_ASSEMBLY_* auto-resolution, so we
            // no longer pop a one-shot picker mid-command (which used to
            // surface the basic Shop Drawing Composition dialog every
            // time the user clicked Generate Package without first
            // configuring).

            FabricationResult res;
            try
            {
                res = FabricationEngine.GenerateFabricationPackage(doc, ids);
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateFabPackageCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            FabricationUndoManager.Record(res);
            ShowResult(res);

            // Open first generated sheet for instant feedback
            if (res.SheetIds.Count > 0)
            {
                try
                {
                    var sheet = doc.GetElement(res.SheetIds[0]) as ViewSheet;
                    if (sheet != null) uidoc.ActiveView = sheet;
                }
                catch (Exception ex) { StingLog.Warn($"GenerateFabPackage open sheet failed: {ex.Message}"); }
            }

            return Result.Succeeded;
        }

        private static readonly BuiltInCategory[] MepCats = new[]
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
        };

        /// <summary>
        /// Resolve elements honouring FabricationOptions, then auto-fall
        /// back through Selection → Active view → Project so a click on
        /// Generate package never silently fails when the user simply
        /// forgot to pre-select.
        /// </summary>
        private static List<ElementId> CollectScopeWithFallback(
            Document doc,
            Autodesk.Revit.UI.UIDocument uidoc,
            out string scopeLabelUsed)
        {
            scopeLabelUsed = "selection";

            // 1) Honour the chosen scope first.
            if (FabricationOptions.ScopeProject)
            {
                scopeLabelUsed = "project";
                return CollectFromProject(doc);
            }
            if (FabricationOptions.ScopeActiveView)
            {
                scopeLabelUsed = "active view";
                var v = CollectFromActiveView(doc);
                if (v.Count > 0) return v;
                // Fallback: view was empty / non-graphical → try project.
                scopeLabelUsed = "project (active view empty)";
                return CollectFromProject(doc);
            }

            // 2) Selection scope.
            var sel = CollectFromSelection(doc, uidoc);
            if (sel.Count > 0) { scopeLabelUsed = "selection"; return sel; }

            // 3) Auto-fallback: active view.
            var view = CollectFromActiveView(doc);
            if (view.Count > 0)
            {
                scopeLabelUsed = "active view (auto-fallback from empty selection)";
                return view;
            }

            // 4) Auto-fallback: project.
            var proj = CollectFromProject(doc);
            if (proj.Count > 0)
            {
                scopeLabelUsed = "project (auto-fallback from empty selection)";
                return proj;
            }

            scopeLabelUsed = "selection";
            return new List<ElementId>();
        }

        private static List<ElementId> CollectFromProject(Document doc)
        {
            var ids = new List<ElementId>();
            try
            {
                var col = new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(MepCats))
                    .WhereElementIsNotElementType();
                foreach (var e in col) ids.Add(e.Id);
            }
            catch (Exception ex) { StingLog.Warn($"GenerateFabPackage.CollectFromProject: {ex.Message}"); }
            return ids;
        }

        private static List<ElementId> CollectFromActiveView(Document doc)
        {
            var ids = new List<ElementId>();
            try
            {
                var view = doc?.ActiveView;
                if (view == null) return ids;
                var col = new FilteredElementCollector(doc, view.Id)
                    .WherePasses(new ElementMulticategoryFilter(MepCats))
                    .WhereElementIsNotElementType();
                foreach (var e in col) ids.Add(e.Id);
            }
            catch (Exception ex) { StingLog.Warn($"GenerateFabPackage.CollectFromActiveView: {ex.Message}"); }
            return ids;
        }

        private static List<ElementId> CollectFromSelection(Document doc, Autodesk.Revit.UI.UIDocument uidoc)
        {
            var ids = new List<ElementId>();
            try
            {
                var sel = uidoc?.Selection?.GetElementIds();
                if (sel == null) return ids;
                foreach (var id in sel)
                {
                    var el = doc.GetElement(id);
                    if (el?.Category == null) continue;
                    int bic = (int)el.Category.Id.Value;
                    foreach (var c in MepCats)
                    {
                        if ((int)c == bic) { ids.Add(id); break; }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GenerateFabPackage.CollectFromSelection: {ex.Message}"); }
            return ids;
        }

        private void ShowResult(FabricationResult res)
        {
            var panel = StingResultPanel.Create("v4 Fabrication Package");
            panel.SetSubtitle(res.FormatSummary());
            panel.AddSection("ASSEMBLIES BY DISCIPLINE");
            if (res.AssembliesByDiscipline.Count == 0)
                panel.Text("No assemblies created.");
            else
                foreach (var kv in res.AssembliesByDiscipline)
                    panel.Metric(kv.Key, kv.Value.ToString());

            panel.AddSection("SHEETS")
                 .Metric("Generated", res.SheetIds.Count.ToString())
                 .Metric("Failed",    res.FailedCount.ToString());

            if (res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                if (res.Warnings.Count > 40) panel.Text($"(+{res.Warnings.Count - 40} more — see StingLog)");
            }
            panel.Show();
        }
    }
}
