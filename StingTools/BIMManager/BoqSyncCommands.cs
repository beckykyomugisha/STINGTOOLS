using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// Feature gap 3 (plugin side) — BOQ → Planscape Sync.
    /// Collects elements with BOQ_UNIT_COST_NUM and BOQ_QUANTITY_NUM parameters,
    /// computes per-discipline totals, and pushes a snapshot to the Planscape server.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PushBoqSnapshotCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("STING — BOQ Sync", "No active document.");
                    return Result.Cancelled;
                }

                // Ensure logged in
                if (!PlanscapeServerClient.Instance.IsLoggedIn)
                {
                    TaskDialog.Show("STING — BOQ Sync",
                        "Not connected to Planscape server.\nPlease log in via BIM → Planscape.");
                    return Result.Cancelled;
                }

                // Collect all elements with BOQ cost/quantity params
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();

                var disciplineTotals = new Dictionary<string, (int items, double estimated, double actual)>(StringComparer.OrdinalIgnoreCase);
                double grandEstimated = 0.0;
                double grandActual    = 0.0;
                int    totalItems     = 0;

                foreach (var el in collector)
                {
                    Parameter costParam = el.LookupParameter("BOQ_UNIT_COST_NUM");
                    Parameter qtyParam  = el.LookupParameter("BOQ_QUANTITY_NUM");

                    if (costParam == null || !costParam.HasValue) continue;
                    if (qtyParam  == null || !qtyParam.HasValue)  continue;

                    double unitCost  = GetDoubleValue(costParam);
                    double qty       = GetDoubleValue(qtyParam);
                    double estimated = unitCost * qty;

                    // Actual cost — use BOQ_ACTUAL_COST_NUM if present, else same as estimated
                    Parameter actualParam = el.LookupParameter("BOQ_ACTUAL_COST_NUM");
                    double actual = actualParam != null && actualParam.HasValue
                        ? GetDoubleValue(actualParam)
                        : estimated;

                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    if (string.IsNullOrWhiteSpace(disc)) disc = "GEN";

                    if (!disciplineTotals.ContainsKey(disc))
                        disciplineTotals[disc] = (0, 0.0, 0.0);

                    var existing = disciplineTotals[disc];
                    disciplineTotals[disc] = (existing.items + 1, existing.estimated + estimated, existing.actual + actual);

                    grandEstimated += estimated;
                    grandActual    += actual;
                    totalItems++;
                }

                if (totalItems == 0)
                {
                    TaskDialog.Show("STING — BOQ Sync",
                        "No elements found with BOQ_UNIT_COST_NUM and BOQ_QUANTITY_NUM parameters.\n\n" +
                        "Run 'BOQ Cost Manager' first to populate cost data.");
                    return Result.Cancelled;
                }

                // Build payload
                var disciplines = disciplineTotals.Select(kv => new
                {
                    discipline = kv.Key,
                    items      = kv.Value.items,
                    estimated  = Math.Round(kv.Value.estimated, 2),
                    actual     = Math.Round(kv.Value.actual,    2),
                }).ToList();

                var dto = new
                {
                    totalEstimated = Math.Round(grandEstimated, 2),
                    totalActual    = Math.Round(grandActual,    2),
                    disciplines,
                };

                // Push to server
                Guid projectId = PlanscapeServerClient.Instance.CurrentProjectId;
                if (projectId == Guid.Empty)
                {
                    TaskDialog.Show("STING — BOQ Sync",
                        "No Planscape project linked.\nPlease link a project via BIM → Planscape.");
                    return Result.Cancelled;
                }

                var task = PlanscapeServerClient.Instance.PushBoqSnapshotAsync(projectId, dto);
                task.GetAwaiter().GetResult();

                bool ok = task.Result;

                if (!ok)
                {
                    TaskDialog.Show("STING — BOQ Sync", "Failed to push snapshot to Planscape. Check your connection.");
                    return Result.Failed;
                }

                StingLog.Info($"PushBoqSnapshot: pushed {totalItems} elements, estimated={grandEstimated:F2}, actual={grandActual:F2}");
                TaskDialog.Show("STING — BOQ Sync",
                    $"BOQ snapshot pushed successfully.\n\n" +
                    $"Elements: {totalItems}\n" +
                    $"Total Estimated: {grandEstimated:N2}\n" +
                    $"Total Actual: {grandActual:N2}\n\n" +
                    "The mobile cost dashboard will refresh automatically.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PushBoqSnapshotCommand", ex);
                TaskDialog.Show("STING — BOQ Sync", $"Error: {ex.Message}");
                return Result.Failed;
            }
        }

        private static double GetDoubleValue(Parameter p)
        {
            if (p.StorageType == StorageType.Double)  return p.AsDouble();
            if (p.StorageType == StorageType.Integer) return p.AsInteger();
            if (p.StorageType == StorageType.String)
            {
                if (double.TryParse(p.AsString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return v;
            }
            return 0.0;
        }
    }
}
