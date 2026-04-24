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

            // Scope resolution — Fabrication sub-tab radio buttons drive
            // which MEP elements feed the engine. Delegated to the
            // shared FabricationScope resolver so the smart fallback
            // (Selection empty → Active view) applies consistently across
            // Generate Package, Cut List, Weld Map, and Isometrics.
            var scope = FabricationScope.Resolve(doc, uidoc);
            var ids = FabricationScope.FilterByRulesAndCategoryMask(scope, null);
            if (ids == null || ids.Count == 0)
            {
                TaskDialog.Show("STING v4 — Generate Fabrication Package",
                    $"Scope '{scope.ScopeLabel}' contains no MEP elements to package.\n\n" +
                    "FabricationEngine will:\n" +
                    "  1. Group elements per discipline rules (STING_FAB_RULES.json)\n" +
                    "  2. Create AssemblyInstances with SP-{DISC}-{SYS}-{LVL}-{SEQ} naming\n" +
                    "  3. Generate 5 views per assembly + BOM schedule\n" +
                    "  4. Lay out shop drawing sheets with title block populated\n" +
                    "  5. Emit per-discipline CSV sidecars (bend / weld / seam)");
                return Result.Cancelled;
            }

            // Shop-drawing composition dialog — lets users pick a
            // specific title block + view template + sheet-number
            // pattern instead of the per-discipline STING_TB_ASSEMBLY_*
            // default. Cancelling the dialog aborts the command.
            //
            // Skipped when the Fabrication tab's "Configure…" button has
            // already populated FabricationOptions.ShopDrawing — that
            // inline picker is the persistent path; the popup is only the
            // one-shot fallback when no panel choice exists.
            if (FabricationOptions.GenerateSheets && FabricationOptions.ShopDrawing == null)
            {
                var dlg = new StingTools.UI.ShopDrawingOptionsDialog(doc);
                try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                if (dlg.ShowDialog() != true) return Result.Cancelled;
                FabricationOptions.ShopDrawing = dlg.Result;
            }

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
