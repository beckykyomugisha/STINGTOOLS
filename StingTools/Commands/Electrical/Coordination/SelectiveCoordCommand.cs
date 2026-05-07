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
            var violations = SelectiveCoordEngine.Check(root, tcc);

            // Stamp every panel with the verification flag (1 = passes, 0 = fails).
            var failingPanels = new HashSet<string>(
                violations.Select(v => v.DownstreamDevice), StringComparer.OrdinalIgnoreCase);
            using (var tx = new Transaction(doc, "STING Selective Coordination"))
            {
                tx.Start();
                foreach (var p in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    bool passes = !failingPanels.Contains(p.Name ?? "");
                    try { ParameterHelpers.SetString(p, ParamRegistry.ELC_SEL_COORD_OK,
                            passes ? "1" : "0", overwrite: true); }
                    catch (Exception ex) { StingLog.Warn($"Coord stamp {p.Name}: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }

            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var dlg = new SelectiveCoordDialog(root, violations, tcc);
                    try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                    dlg.ShowDialog();
                });
            }
            catch (Exception ex) { StingLog.Warn($"OpenSelectiveCoordDialog: {ex.Message}"); }

            return Result.Succeeded;
        }
    }
}
