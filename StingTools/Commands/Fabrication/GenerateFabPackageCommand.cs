// StingTools v4 MVP — GenerateFabPackageCommand.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;
using StingTools.Core.Placement;
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

        /// <summary>Controls how ISO 6412 symbols are placed on shop drawings.</summary>
        public static PlacementMode SymbolPlacementMode { get; set; } = PlacementMode.Replace;

        public static bool PlaceISOPipe       { get; set; } = true;
        public static bool PlaceISODuct       { get; set; } = true;
        public static bool PlaceISOElectrical { get; set; } = true;

        /// <summary>ISO symbol placement strategy.</summary>
        public enum PlacementMode
        {
            /// <summary>Skip all symbol placement.</summary>
            Off,
            /// <summary>Place symbols; delete any pre-existing symbols on the same member first.</summary>
            Replace,
            /// <summary>Only place symbols on members that have no existing annotation.</summary>
            NewOnly,
            /// <summary>Place symbols AND keep existing annotations.</summary>
            Additive,
        }
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
                // Last-resort: open the Fabrication Workspace instead of
                // showing an error dialog. The workspace surfaces live
                // category counts + scope radios so the user can see the
                // 341 pipes that exist and pick a scope that includes
                // them. This eliminates the "no MEP elements" dead-end
                // that happens when Selection scope is on with nothing
                // selected.
                try
                {
                    var dlg = new StingTools.UI.FabricationWorkspaceDialog(doc);
                    try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                    dlg.ShowDialog();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"GenerateFabPackage: workspace fallback failed: {ex.Message}");
                    TaskDialog.Show("STING v4 — Generate Fabrication Package",
                        "No MEP elements found in selection, active view, or the project.\n\n" +
                        "Open a view containing pipes / ducts / conduits and try again.");
                }
                return Result.Cancelled;
            }
            StingLog.Info($"GenerateFabPackage: collected {ids.Count} element(s) via {scopeLabelUsed} scope.");

            // ── ISO symbol pre-flight check ────────────────────────────
            // Run BEFORE the (potentially slow) fabrication engine so the
            // user sees the coverage gap immediately and can abort or
            // acknowledge before committing to a full package generation.
            if (FabricationOptions.PlaceISO6412Symbols)
            {
                try
                {
                    var preflight = IsoSymbolPlacer.GetMissingFamilyReport();
                    if (preflight.MissingCount > 0)
                    {
                        StingLog.Warn($"ISO symbol pre-flight: {preflight.Summary}");
                        var td = new TaskDialog("STING v4 — ISO 6412 Symbol Pre-flight")
                        {
                            MainInstruction = $"{preflight.MissingCount} of {preflight.Total} ISO 6412 symbol families are missing",
                            MainContent =
                                preflight.Summary + "\n\n" +
                                "Symbol placement will silently skip members whose family is absent. " +
                                "The fabrication package will still be generated.\n\n" +
                                "To fix: author the missing families per Families/ISO6412/README.md, " +
                                "or run 'Load ISO Symbol Library' once the bundle is available.",
                            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                            DefaultButton  = TaskDialogResult.Ok,
                        };
                        if (preflight.MissingCount == preflight.Total)
                        {
                            td.MainInstruction = "No ISO 6412 symbol families found on disk";
                            td.MainContent = "Zero of the 188 catalogue families are present — all symbol placement will be skipped.\n\n" +
                                "Author or load the symbol library first, then regenerate the package.";
                        }
                        var preRes = td.Show();
                        if (preRes == TaskDialogResult.Cancel) return Result.Cancelled;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ISO symbol pre-flight: {ex.Message}"); }
            }

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

            // Publish to PlacementResultBus so the Placement Centre + dock panel can display it.
            var fabSummary = new PlacementRunSummary
            {
                Source   = "Symbols",
                Headline = res != null
                    ? $"Fabrication package: {res.AssemblyIds.Count} assemblies, {res.SymbolsPlaced} symbols"
                    : "Fabrication package complete",
                Metrics  = res != null ? new List<string>
                {
                    $"Assemblies created: {res.AssemblyIds.Count}",
                    $"Sheets created: {res.SheetIds.Count}",
                    $"Symbols placed: {res.SymbolsPlaced}",
                    $"Failed: {res.FailedCount}",
                    $"Warnings: {res.Warnings?.Count ?? 0}",
                } : new List<string>(),
                Warnings = res?.Warnings != null
                    ? new List<string>(res.Warnings)
                    : new List<string>(),
            };
            PlacementResultBus.Publish(fabSummary);

            ShowResult(res);

            // Open first generated sheet for instant feedback
            if (res.SheetIds.Count > 0)
            {
                try
                {
                    var sheet = doc.GetElement(res.SheetIds[0]) as ViewSheet;
                    if (sheet != null) uidoc.ActiveView = sheet;
                }
                catch (Exception ex2) { StingLog.Warn($"GenerateFabPackage open sheet failed: {ex2.Message}"); }
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
                // Sheet views aren't valid view filters for the
                // collector — bail early so the caller falls through
                // to the project-level fallback.
                if (view is ViewSheet) return ids;
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

        /// <summary>
        /// Light-themed result dialog matching the Fabrication Workspace's
        /// visual language: white background, orange section accents,
        /// card-based summary with Open last sheet / View log / Open
        /// Workspace / Close action row. Replaces the previous heavy
        /// StingResultPanel pop-up (dark-blue header) and the interim
        /// TaskDialog summary.
        /// </summary>
        private void ShowResult(FabricationResult res)
        {
            // Always log every warning so the audit trail stays
            // intact even though we no longer render them all in the UI.
            foreach (var w in res.Warnings) StingLog.Warn($"GenerateFabPackage: {w}");

            try
            {
                var doc = StingTools.UI.StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                var dlg = new StingTools.UI.FabricationResultDialog(doc, res);
                try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateFabPackage.ShowResult", ex);
                // Last-resort fallback: never silently swallow the result.
                TaskDialog.Show("STING v4 — Fabrication Package", res.FormatSummary());
            }
        }
    }
}
