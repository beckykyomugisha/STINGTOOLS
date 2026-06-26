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
using System.Collections.Generic;
using StingTools.BOQ.MeasurementStandard;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;       // StingResultPanel

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

                StingResultPanel.Create("Measurement standard")
                    .AddSection("RESULT")
                    .Metric("Active standard", $"{picked[0].Label} ({id})")
                    .Text("Future BOQ_Build runs will classify and describe rows per this standard. " +
                          "Existing snapshots keep the standard they were saved with.")
                    .Show();
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

                var rp = StingResultPanel.Create("Measurement standard preview")
                    .SetSubtitle("How each standard classifies + measures common categories");
                foreach (var std in MeasurementStandardRegistry.All())
                {
                    var rows = new List<string[]>();
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
                        rows.Add(new[] { cat, section, unit });
                    }
                    rp.AddSection($"{std.DisplayName}  [{std.Version}]")
                        .Table(new[] { "Category", "Section", "Unit" }, rows);
                }

                rp.Show();
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
