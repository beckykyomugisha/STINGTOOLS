using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.SLD;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Coordination
{
    /// <summary>Run the selective-coordination check and surface results in a dialog.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectiveCoordCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var root = SLDCircuitTraverser.BuildHierarchy(doc);
            if (root == null)
            {
                TaskDialog.Show("STING Selective Coordination",
                    "No electrical hierarchy found. Place panels with downstream circuits first.");
                return Result.Cancelled;
            }
            var tcc = TccDatabaseLoader.Load(null);
            var pairs = SelectiveCoordEngine.Evaluate(root, tcc);
            var violations = SelectiveCoordEngine.ToViolations(pairs);

            // Stamp ELC_SEL_COORD_OK = 1 ONLY where every pair feeding the panel was
            // proven selective by the generic bands. Unassessed, not-assured, no-data
            // and not-selective panels all get 0 (= not verified). A panel is never
            // passed just because nothing was found against it.
            int proven = 0, notProven = 0;
            using (var tx = new Transaction(doc, "STING Selective Coordination"))
            {
                tx.Start();
                foreach (var p in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    var mine = pairs.Where(r =>
                        (r.Downstream?.ElementId != null && r.Downstream.ElementId == p.Id)
                        || (r.Downstream?.ElementId == null
                            && string.Equals(r.Downstream?.Label, p.Name, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    bool passes = mine.Count > 0 && mine.All(r => r.Result.Verdict == SelectivityVerdict.Selective);
                    if (passes) proven++; else notProven++;
                    try { ParameterHelpers.SetString(p, ParamRegistry.ELC_SEL_COORD_OK,
                            passes ? "1" : "0", overwrite: true); }
                    catch (Exception ex) { StingLog.Warn($"Coord stamp {p.Name}: {ex.Message}"); }
                }
                tx.Commit();
            }
            StingLog.Info($"SelectiveCoord: {pairs.Count} pair(s); " +
                string.Join(", ", pairs.GroupBy(r => r.Result.Verdict).Select(g => $"{g.Key} {g.Count()}")) +
                $"; panels proven {proven}, not proven {notProven}. Basis: {IecMcbBands.Basis}");

            TaskDialog.Show("STING Selective Coordination",
                $"{pairs.Count} upstream/downstream pair(s) assessed.\n" +
                $"  Selective (bands prove it): {pairs.Count(r => r.Result.Verdict == SelectivityVerdict.Selective)}\n" +
                $"  Not assured: {pairs.Count(r => r.Result.Verdict == SelectivityVerdict.NotAssured)}\n" +
                $"  Not selective: {pairs.Count(r => r.Result.Verdict == SelectivityVerdict.NotSelective)}\n" +
                $"  No curve data (MCCB / ACB / unknown curve): {pairs.Count(r => r.Result.Verdict == SelectivityVerdict.NoCurveData)}\n\n" +
                $"ELC_SEL_COORD_OK = 1 on {proven} panel(s); 0 (not verified) on {notProven}.\n\n" +
                $"Basis: {IecMcbBands.Basis}.");

            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var dlg = new SelectiveCoordDialog(root, violations, tcc);
                    try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                    dlg.ShowDialog();
                });
            }
            catch (Exception ex2) { StingLog.Warn($"OpenSelectiveCoordDialog: {ex2.Message}"); }

            return Result.Succeeded;
        }
    }
}
