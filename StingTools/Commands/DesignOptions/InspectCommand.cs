// StingTools — Design Options Inspect command.
//
// Read-only diagnostic. Lists every option set + option in the doc with
// counts, primary flag, active flag, and sidecar metadata. Mirrors the
// pattern of DrawingTypesInspectCommand / AecFiltersInspectCommand.

using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DesignOptionsInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            DesignOptionRegistry.InvalidateCache(doc);
            var sets = DesignOptionRegistry.Snapshot(doc);
            var active = DesignOptionRegistry.ActiveOptionId(doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Document: {doc.Title}");
            sb.AppendLine($"Active option: " +
                (active == null || active == ElementId.InvalidElementId
                    ? DesignOptionParams.MAIN_MODEL_LABEL
                    : doc.GetElement(active)?.Name ?? active.ToString()));
            sb.AppendLine($"Sets        : {sets.Count}");
            sb.AppendLine($"Options     : {sets.Sum(s => s.Options.Count)}");
            sb.AppendLine();

            if (sets.Count == 0)
            {
                sb.AppendLine("No design option sets in this document.");
                TaskDialog.Show("STING — Design Options", sb.ToString());
                return Result.Succeeded;
            }

            foreach (var s in sets.OrderBy(x => x.Name))
            {
                sb.AppendLine($"■ {s.Name}");
                if (s.Metadata != null)
                {
                    if (!string.IsNullOrEmpty(s.Metadata.Purpose))
                        sb.AppendLine($"    purpose      : {s.Metadata.Purpose}");
                    if (!string.IsNullOrEmpty(s.Metadata.Kind))
                        sb.AppendLine($"    kind         : {s.Metadata.Kind}");
                    if (s.Metadata.DecisionDate.HasValue)
                        sb.AppendLine($"    decision due : {s.Metadata.DecisionDate.Value:yyyy-MM-dd}" +
                            (s.Metadata.Decided ? " (DECIDED)" : ""));
                    if (s.Metadata.ClientFacing)
                        sb.AppendLine($"    client-facing: yes");
                }

                foreach (var o in s.Options.OrderByDescending(o => o.IsPrimary).ThenBy(o => o.Name))
                {
                    string flags = (o.IsPrimary ? "PRIMARY  " : "         ")
                                 + (o.IsActive  ? "ACTIVE   " : "         ");
                    sb.AppendLine($"    · {flags}{o.Name,-30} elements={o.ElementCount,5}");
                    if (o.Metadata != null)
                    {
                        if (Math.Abs(o.Metadata.CostDelta) > 0.01)
                            sb.AppendLine($"        Δcost   : {o.Metadata.CostDelta:N0}");
                        if (Math.Abs(o.Metadata.CarbonDelta) > 0.01)
                            sb.AppendLine($"        Δcarbon : {o.Metadata.CarbonDelta:N0} kgCO₂e");
                        if (Math.Abs(o.Metadata.AreaDelta) > 0.01)
                            sb.AppendLine($"        Δarea   : {o.Metadata.AreaDelta:N1} m²");
                        if (o.Metadata.LinkedIssues?.Count > 0)
                            sb.AppendLine($"        issues  : {o.Metadata.LinkedIssues.Count}");
                        if (o.Metadata.LockedSheets?.Count > 0)
                            sb.AppendLine($"        sheets  : {o.Metadata.LockedSheets.Count}");
                    }
                }
                sb.AppendLine();
            }

            TaskDialog.Show("STING — Design Options Inspector", sb.ToString());
            return Result.Succeeded;
        }
    }
}
