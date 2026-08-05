// ══════════════════════════════════════════════════════════════════════════
//  MeasurementStandardCommands.cs — P6 commands.
//
//  Cost_SetMeasurementStandard — pick the active standard; persisted in
//                                project_config.json so subsequent BOQ
//                                builds + exports honour it.
//  Cost_StandardInspect        — diagnostic preview: shows how each
//                                standard classifies + describes a few
//                                sample categories.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ.MeasurementStandard;
using StingTools.Core;
using StingTools.Select;

namespace StingTools.Commands.Cost
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostSetMeasurementStandardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var items = MeasurementStandardRegistry.All()
                    .Select(s => new StingListPicker.ListItem
                    {
                        Label = s.DisplayName,
                        Detail = s.Version,
                        Tag = s.Id
                    }).ToList();
                var picked = StingListPicker.Show("STING — Measurement standard",
                    "Pick the measurement standard for future BOQ builds. Stored in project_config.json (key: COST_MEASUREMENT_STANDARD).",
                    items, allowMultiSelect: false);
                if (picked == null || picked.Count == 0) return Result.Cancelled;
                string id = picked[0].Tag as string ?? "nrm2";

                // Persist via TagConfig's project_config.json setter. The
                // BOQ engine reads this key when building snapshots; see
                // BOQDocument.MeasurementStandardId.
                TagConfig.SetConfigValue("COST_MEASUREMENT_STANDARD", id);

                TaskDialog.Show("STING — Measurement standard",
                    $"Measurement standard set to {picked[0].Label} ({id}).\n\n" +
                    "Future BOQ_Build runs will classify and describe rows per this standard. " +
                    "Existing snapshots keep the standard they were saved with.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_SetMeasurementStandard", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostStandardInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string[] samples = { "Walls", "Foundations", "Structural Framing",
                                     "Pipes", "Electrical Equipment", "Lighting Fixtures",
                                     "Roads", "Reinforcement" };

                var sb = new StringBuilder();
                sb.AppendLine("Standard / Category → (Section, Unit)");
                sb.AppendLine(new string('─', 60));
                foreach (var std in MeasurementStandardRegistry.All())
                {
                    sb.AppendLine();
                    sb.AppendLine($"━ {std.DisplayName}  [{std.Version}]");
                    foreach (var cat in samples)
                    {
                        var stubLine = new BOQ.BOQLineItem
                        {
                            Category = cat,
                            NRM2Section = cat.StartsWith("Wall", StringComparison.OrdinalIgnoreCase) ? "14"
                                : cat.StartsWith("Found", StringComparison.OrdinalIgnoreCase) ? "4"
                                : cat.StartsWith("Struct", StringComparison.OrdinalIgnoreCase) ? "15"
                                : cat.StartsWith("Pipe", StringComparison.OrdinalIgnoreCase) ? "33"
                                : cat.StartsWith("Electric", StringComparison.OrdinalIgnoreCase) ? "34"
                                : cat.StartsWith("Light", StringComparison.OrdinalIgnoreCase) ? "35"
                                : cat.StartsWith("Road", StringComparison.OrdinalIgnoreCase) ? "99"
                                : cat.StartsWith("Reinforc", StringComparison.OrdinalIgnoreCase) ? "15" : "99",
                            Quantity = 1
                        };
                        string section = std.ClassifyRow(stubLine, null);
                        string unit = std.PreferredUnit(cat);
                        sb.AppendLine($"  {cat,-22} → ({section,-6}, {unit})");
                    }
                }

                TaskDialog.Show("STING — Measurement standard preview", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_StandardInspect", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
