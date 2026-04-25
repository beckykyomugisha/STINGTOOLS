// Gap 2 / Phase 121 — ES coverage diagnostic.
//
// Read-only scan. For each of the four element-level schemas, counts how
// many elements carry an ES entity, how many carry only the legacy shared
// parameter, and how many lack both. Result panel makes it obvious at a
// glance whether the migration has run, partially run, or not at all.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Storage;
using StingTools.UI;

namespace StingTools.Commands.Storage
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class EsStorageDiagnosticCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open"; return Result.Failed; }
                var doc = ctx.Doc;

                var stale = Schema.Lookup(new Guid("E1A7B2C4-1011-1235-8411-F6E5D4C3B2A1"));
                var cluster = Schema.Lookup(StingClusterSchema.SchemaGuid);
                var position = Schema.Lookup(StingPositionSchema.SchemaGuid);
                var history = Schema.Lookup(StingTagHistorySchema.SchemaGuid);

                int total = 0;
                int staleEs = 0, staleShared = 0;
                int clusterEs = 0, clusterShared = 0;
                int positionEs = 0, positionShared = 0;
                int historyEs = 0, historyShared = 0;

                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    total++;

                    try
                    {
                        if (stale != null && (el.GetEntity(stale)?.IsValid() ?? false)) staleEs++;
                        else if (el.LookupParameter("STING_STALE_BOOL")?.HasValue ?? false) staleShared++;
                    }
                    catch { }

                    try
                    {
                        if (cluster != null && (el.GetEntity(cluster)?.IsValid() ?? false)) clusterEs++;
                        else if ((el.LookupParameter(ParamRegistry.CLUSTER_COUNT)?.HasValue ?? false) ||
                                 !string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.CLUSTER_LABEL)))
                            clusterShared++;
                    }
                    catch { }

                    try
                    {
                        if (position != null && (el.GetEntity(position)?.IsValid() ?? false)) positionEs++;
                        else if (ParameterHelpers.GetInt(el, ParamRegistry.TAG_POS, 0) != 0) positionShared++;
                    }
                    catch { }

                    try
                    {
                        if (history != null && (el.GetEntity(history)?.IsValid() ?? false)) historyEs++;
                        else if (!string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_TAG_MODIFIED_DT")))
                            historyShared++;
                    }
                    catch { }
                }

                var panel = StingResultPanel.Create("STING — Extensible Storage coverage")
                    .SetSubtitle($"Scanned {total} element(s) in '{doc.Title}'")
                    .AddSection("STALE FLAG")
                    .Metric("ES entity",        staleEs.ToString())
                    .Metric("Legacy shared-only", staleShared.ToString())
                    .AddSection("CLUSTER METADATA")
                    .Metric("ES entity",        clusterEs.ToString())
                    .Metric("Legacy shared-only", clusterShared.ToString())
                    .AddSection("TAG POSITION")
                    .Metric("ES entity",        positionEs.ToString())
                    .Metric("Legacy shared-only", positionShared.ToString())
                    .AddSection("TAG HISTORY")
                    .Metric("ES entity",        historyEs.ToString())
                    .Metric("Legacy shared-only", historyShared.ToString());

                int sharedAll = staleShared + clusterShared + positionShared + historyShared;
                int esAll     = staleEs + clusterEs + positionEs + historyEs;
                if (sharedAll > 0)
                {
                    panel.AddSection("ACTION")
                         .Text($"{sharedAll} element-field(s) still live only on legacy shared parameters.")
                         .Text("Run BIM ▸ Migrate to Extensible Storage to import them.");
                }
                else if (esAll == 0)
                {
                    panel.AddSection("ACTION")
                         .Text("No STING structured data detected. This is expected on a brand-new project — data appears after the first tagging or compliance run.");
                }
                else
                {
                    panel.AddSection("STATUS")
                         .Text("All detected STING structured data has been migrated to Extensible Storage.");
                }
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("EsStorageDiagnosticCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
